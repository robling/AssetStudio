﻿using K4os.Compression.LZ4;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AssetStudio
{
    public static class ShaderConverter
    {
        public static string Convert(this Shader shader)
        {
            if (shader.m_SubProgramBlob != null) //5.3 - 5.4
            {
                var decompressedBytes = new byte[shader.decompressedSize];
                LZ4Codec.Decode(shader.m_SubProgramBlob, decompressedBytes);
                using (var blobReader = new BinaryReader(new MemoryStream(decompressedBytes)))
                {
                    var program = new ShaderProgram(blobReader, shader.version);
                    program.Read(blobReader, 0);
                    return header + program.Export(Encoding.UTF8.GetString(shader.m_Script));
                }
            }

            if (shader.compressedBlob != null) //5.5 and up
            {
                return header + ConvertSerializedShader(shader);
            }

            return header + Encoding.UTF8.GetString(shader.m_Script);
        }

        private static string ConvertSerializedShader(Shader shader)
        {
            var length = shader.platforms.Length;
            var shaderPrograms = new ShaderProgram[length];
            for (var i = 0; i < length; i++)
            {
                for (var j = 0; j < shader.offsets[i].Length; j++)
                {
                    var offset = shader.offsets[i][j];
                    var compressedLength = shader.compressedLengths[i][j];
                    var decompressedLength = shader.decompressedLengths[i][j];
                    var decompressedBytes = new byte[decompressedLength];
                    LZ4Codec.Decode(shader.compressedBlob, (int)offset, (int)compressedLength, decompressedBytes, 0, (int)decompressedLength);
                    using (var blobReader = new BinaryReader(new MemoryStream(decompressedBytes)))
                    {
                        if (j == 0)
                        {
                            shaderPrograms[i] = new ShaderProgram(blobReader, shader.version);
                        }
                        shaderPrograms[i].Read(blobReader, j);
                    }
                }
            }

            return ConvertSerializedShader(shader.m_ParsedForm, shader.platforms, shaderPrograms, 0);
        }

        private static string ConvertSerializedShader(SerializedShader m_ParsedForm, ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms, int indent)
        {
            var sb = new StringBuilder();
            sb.Append($"Shader \"{m_ParsedForm.m_Name}\" {{\n", indent);

            sb.Append(ConvertSerializedProperties(m_ParsedForm.m_PropInfo, indent+ 1));

            foreach (var m_SubShader in m_ParsedForm.m_SubShaders)
            {
                sb.Append(ConvertSerializedSubShader(m_SubShader, platforms, shaderPrograms, indent + 1));
            }

            if (!string.IsNullOrEmpty(m_ParsedForm.m_FallbackName))
            {
                sb.Append($"Fallback \"{m_ParsedForm.m_FallbackName}\"\n", indent);
            }

            if (!string.IsNullOrEmpty(m_ParsedForm.m_CustomEditorName))
            {
                sb.Append($"CustomEditor \"{m_ParsedForm.m_CustomEditorName}\"\n", indent);
            }

            sb.Append("}", indent);
            return sb.ToString();
        }

        private static string ConvertSerializedSubShader(SerializedSubShader m_SubShader, ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms, int indent)
        {
            var sb = new StringBuilder();
            sb.Append("SubShader {\n", indent);
            if (m_SubShader.m_LOD != 0)
            {
                sb.Append($"LOD {m_SubShader.m_LOD}\n", indent + 1);
            }

            sb.Append(ConvertSerializedTagMap(m_SubShader.m_Tags, indent + 1));

            foreach (var m_Passe in m_SubShader.m_Passes)
            {
                sb.Append(ConvertSerializedPass(m_Passe, platforms, shaderPrograms, indent + 1));
            }
            sb.Append("}\n", indent);
            return sb.ToString();
        }

        private static SerializedPlayerSubProgram[] FlattenPlayerSubPrograms(SerializedProgram program)
        {
            List<SerializedPlayerSubProgram> flatList = new List<SerializedPlayerSubProgram>();
            if (program?.m_PlayerSubPrograms == null) return flatList.ToArray();
            foreach (var subArray in program.m_PlayerSubPrograms)
            {
                if (subArray != null)
                {
                    flatList.AddRange(subArray);
                }
            }
            return flatList.ToArray();
        }

        private static string ConvertPrograms(Dictionary<int, string> name_map, SerializedProgram program, string programType, ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms, int indent)
        {
            var sb = new StringBuilder();
            Dictionary<string, string> code_note_mapper = new Dictionary<string, string>();
            if (program?.m_CommonParameters != null)
            {
                ConvertTextureBindings(name_map, program, indent, sb, code_note_mapper);
            }
            if (program?.m_CommonParameters != null)
            {
                ConvertConstantBuffer(name_map, program, programType, indent, sb, code_note_mapper);
            }
            if (program?.m_SubPrograms.Length > 0)
            {
                sb.Append($"Program \"{programType}\" {{\n", indent);
                sb.Append(ConvertSerializedSubPrograms(program.m_SubPrograms, platforms, shaderPrograms, indent + 1));
                sb.Append("}\n",indent);
            }
            SerializedPlayerSubProgram[] flattenedPlayerSubPrograms = FlattenPlayerSubPrograms(program);
            if (flattenedPlayerSubPrograms?.Length > 0)
            {
                sb.Append($"PlayerProgram \"{programType}\" {{\n", indent);
                sb.Append(ConvertSerializedPlayerSubPrograms(flattenedPlayerSubPrograms, platforms, shaderPrograms, indent + 1));
                sb.Append("}\n", indent);
            }
            return sb.ToString();
        }

        private static void ConvertTextureBindings(Dictionary<int, string> name_map, SerializedProgram program, int indent, StringBuilder sb, Dictionary<string, string> code_note_mapper)
        {
            foreach (var item in program.m_CommonParameters.m_TextureParams)
            {
                sb.Append("Textures:\n", indent);
                sb.Append("t", indent + 1);
                sb.Append(item.m_Index);
                sb.Append("\t: ");
                sb.Append(name_map[item.m_NameIndex]);
                sb.Append("\n");

                code_note_mapper.Add($"t{item.m_Index}.Sample", name_map[item.m_NameIndex]);
            }
        }

        private static char[] swizz_char = new char[] {'x', 'y','z', 'w'};
        
        private static void ConvertConstantBuffer(Dictionary<int, string> name_map, SerializedProgram program, string programType, int indent, StringBuilder sb, Dictionary<string, string> code_note_mapper)
        {
            sb.Append($"Parameters \"{programType}\" {{\n", indent);

            // var str = JsonSerializer.Serialize(program.m_CommonParameters, jop);
            // sb.Append(str, indent);

            var cb_bind = new Dictionary<int, string>();
            foreach (var item in program.m_CommonParameters.m_ConstantBufferBindings)
            {
                cb_bind[item.m_NameIndex] = "cb" + item.m_Index;
            }

            var cb_map = new Dictionary<string, string>();
            foreach (var item in program.m_CommonParameters.m_ConstantBuffers)
            {
                var cb_name = name_map[item.m_NameIndex];
                var cb_code_name = cb_bind[item.m_NameIndex];

                sb.Append($"{cb_code_name} : {cb_name}\n", indent + 1);
                foreach (var matrix in item.m_MatrixParams)
                {
                    var base_offset = matrix.m_Index / 16;
                    var name = $"{cb_code_name}[{base_offset}][{base_offset + 1}][{base_offset + 2}][{base_offset + 3}]";
                    var variable_name = name_map[matrix.m_NameIndex];
                    sb.Append($"{name} : {variable_name}\n", indent + 2);
                    code_note_mapper.Add($"{name}", variable_name);
                }
                foreach (var vector in item.m_VectorParams)
                {
                    var base_offset = vector.m_Index / 16;
                    var swizz = (vector.m_Index % 16) / 4;
                    var remains = (vector.m_Index % 16) % 4;
                    string name = "";
                    if (swizz == 0)
                    {
                        name = $"{cb_code_name}[{base_offset}]";
                    }
                    else if (swizz > 0 && swizz < 4 && remains == 0)
                    {
                        name = $"{cb_code_name}[{base_offset}].{swizz_char[swizz]}";
                    }
                    else
                    {
                        name = $"{cb_code_name}[{base_offset}]+{vector.m_Index % 16}";
                    }
                    var variable_name = name_map[vector.m_NameIndex];
                    sb.Append($"{name} : {variable_name}\n", indent + 2);
                }
            }

            sb.Append("}\n", indent);
        }

        private static string ConvertSerializedPass(SerializedPass m_Passe, ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms, int indent)
        {
            var sb = new StringBuilder();
            switch (m_Passe.m_Type)
            {
                case PassType.Normal:
                    sb.Append("Pass ", indent);
                    break;
                case PassType.Use:
                    sb.Append("UsePass ", indent);
                    break;
                case PassType.Grab:
                    sb.Append("GrabPass ", indent);
                    break;
            }
            if (m_Passe.m_Type == PassType.Use)
            {
                sb.Append($"\"{m_Passe.m_UseName}\"\n");
                sb.Append("", indent);
            }
            else
            {
                sb.Append("{\n");

                if (m_Passe.m_Type == PassType.Grab)
                {
                    if (!string.IsNullOrEmpty(m_Passe.m_TextureName))
                    {
                        sb.Append($"\"{m_Passe.m_TextureName}\"\n", indent);
                    }
                }
                else
                {
                    sb.Append(ConvertSerializedShaderState(m_Passe.m_State, indent + 1));
                    // sb.Append(ConvertSerializedShaderNameIndices(m_Passe.m_NameIndices, indent + 1));
                    var dic = new Dictionary<int, string>();
                    foreach (var i in m_Passe.m_NameIndices)
                    {
                        dic[i.Value] = i.Key;
                    }

                    sb.Append(ConvertPrograms(dic, m_Passe.progVertex, "vp", platforms, shaderPrograms, indent + 1));
                    sb.Append(ConvertPrograms(dic, m_Passe.progFragment, "fp", platforms, shaderPrograms, indent + 1));
                    sb.Append(ConvertPrograms(dic, m_Passe.progGeometry, "gp", platforms, shaderPrograms, indent + 1));
                    sb.Append(ConvertPrograms(dic, m_Passe.progHull, "hp", platforms, shaderPrograms, indent + 1));
                    sb.Append(ConvertPrograms(dic, m_Passe.progDomain, "dp", platforms, shaderPrograms, indent + 1));
                    sb.Append(ConvertPrograms(dic, m_Passe.progRayTracing, "rtp", platforms, shaderPrograms, indent + 1));
                }
                sb.Append("}\n", indent);
            }
            return sb.ToString();
        }

        private static void AppendSubProgram<T>(StringBuilder sb, T serializedSubProgram, ShaderCompilerPlatform platform,
            ShaderProgram shaderProgram, int indent, Func<T, uint> getBlobIndex, Func<T, string> getAdditionalInfo)
        {
            sb.Append($"SubProgram \"{GetPlatformString(platform)} ", indent);
            if (getAdditionalInfo != null)
            {
                sb.Append($"{getAdditionalInfo(serializedSubProgram)} ");
            }

            sb.Append("\" {\n");

            ShaderSubProgramWrap subProgramWrap = shaderProgram.m_SubProgramWraps[getBlobIndex(serializedSubProgram)];
            ShaderSubProgram subProgram = subProgramWrap.genShaderSubProgram();
            var subProgramsStr = subProgram.Export();
            var indentStr = GetindentString(indent + 1);
            subProgramsStr = $"{indentStr}{subProgramsStr}";
            subProgramsStr = subProgramsStr.Replace("\n", $"\n{indentStr}");
            sb.Append(subProgramsStr);

            sb.Append("\n");
            sb.Append("}\n", indent);
        }


        private static string ConvertSubPrograms<T>(IEnumerable<T> m_SubPrograms, ShaderCompilerPlatform[] platforms,
            ShaderProgram[] shaderPrograms, int indent, Func<T, uint> getBlobIndex,
            Func<T, ShaderGpuProgramType> getGpuProgramType, Func<T, string> getAdditionalInfo = null)
        {
            var sb = new StringBuilder();
            var groups = m_SubPrograms.GroupBy(getBlobIndex);

            foreach (var group in groups)
            {
                var programs = group.GroupBy(getGpuProgramType);
                foreach (var program in programs)
                {
                    for (int i = 0; i < platforms.Length; i++)
                    {
                        var platform = platforms[i];
                        if (CheckGpuProgramUsable(platform, program.Key))
                        {
                            var shaderProgram = shaderPrograms[i];
                            foreach (var subProgram in program)
                            {
                                AppendSubProgram(sb, subProgram, platform, shaderProgram, indent, getBlobIndex,
                                    getAdditionalInfo);
                            }

                            break;
                        }
                    }
                }
            }

            return sb.ToString();
        }

        private static string ConvertSerializedPlayerSubPrograms(SerializedPlayerSubProgram[] m_SubPrograms,
            ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms, int indent)
        {
            return ConvertSubPrograms(m_SubPrograms, platforms, shaderPrograms, indent, x => x.m_BlobIndex,
                x => x.m_GpuProgramType);
        }

        private static string Add_SerializedSubProgram(SerializedSubProgram sbp)
        {
            var str = JsonSerializer.Serialize(sbp.m_Parameters);
            return str;
        }
        private static string ConvertSerializedSubPrograms(SerializedSubProgram[] m_SubPrograms,
            ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms, int indent)
        {
            return ConvertSubPrograms(m_SubPrograms, platforms, shaderPrograms, indent, x => x.m_BlobIndex,
                x => x.m_GpuProgramType, x => $"hw_tier{x.m_ShaderHardwareTier:00}\n" + Add_SerializedSubProgram(x));
        }

        private static string ConvertSerializedShaderNameIndices(KeyValuePair<string, int>[] names, int indent)
        {
            var sb = new StringBuilder();
            sb.Append("m_NameIndices:\n", indent);
            indent += 1;
            foreach(var name in names)
            {
                sb.Append(name.Key, indent);
                sb.Append("\t\t: ");
                sb.Append(name.Value);
                sb.Append('\n');
            }
            return sb.ToString();
        }
        private static string ConvertSerializedShaderState(SerializedShaderState m_State, int indent)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(m_State.m_Name))
            {
                sb.Append($"Name \"{m_State.m_Name}\"\n", indent);
            }
            if (m_State.m_LOD != 0)
            {
                sb.Append($"LOD {m_State.m_LOD}\n", indent);
            }

            sb.Append(ConvertSerializedTagMap(m_State.m_Tags, indent));

            sb.Append(ConvertSerializedShaderRTBlendState(m_State.rtBlend, m_State.rtSeparateBlend, indent));

            if (m_State.alphaToMask.val > 0f)
            {
                sb.Append("AlphaToMask On\n", indent);
            }

            if (m_State.zClip?.val != 1f) //ZClip On
            {
                sb.Append("ZClip Off\n", indent);
            }

            if (m_State.zTest.val != 4f) //ZTest LEqual
            {
                sb.Append("ZTest ", indent);
                switch (m_State.zTest.val) //enum CompareFunction
                {
                    case 0f: //kFuncDisabled
                        sb.Append("Off");
                        break;
                    case 1f: //kFuncNever
                        sb.Append("Never");
                        break;
                    case 2f: //kFuncLess
                        sb.Append("Less");
                        break;
                    case 3f: //kFuncEqual
                        sb.Append("Equal");
                        break;
                    case 5f: //kFuncGreater
                        sb.Append("Greater");
                        break;
                    case 6f: //kFuncNotEqual
                        sb.Append("NotEqual");
                        break;
                    case 7f: //kFuncGEqual
                        sb.Append("GEqual");
                        break;
                    case 8f: //kFuncAlways
                        sb.Append("Always");
                        break;
                }

                sb.Append("\n");
            }

            if (m_State.zWrite.val != 1f) //ZWrite On
            {
                sb.Append("ZWrite Off\n", indent);
            }

            if (m_State.culling.val != 2f) //Cull Back
            {
                sb.Append("Cull ", indent);
                switch (m_State.culling.val) //enum CullMode
                {
                    case 0f: //kCullOff
                        sb.Append("Off");
                        break;
                    case 1f: //kCullFront
                        sb.Append("Front");
                        break;
                }
                sb.Append("\n");
            }

            if (m_State.offsetFactor.val != 0f || m_State.offsetUnits.val != 0f)
            {
                sb.Append($"Offset {m_State.offsetFactor.val}, {m_State.offsetUnits.val}\n", indent);
            }

            if (m_State.stencilRef.val != 0f ||
                m_State.stencilReadMask.val != 255f ||
                m_State.stencilWriteMask.val != 255f ||
                m_State.stencilOp.pass.val != 0f ||
                m_State.stencilOp.fail.val != 0f ||
                m_State.stencilOp.zFail.val != 0f ||
                m_State.stencilOp.comp.val != 8f ||
                m_State.stencilOpFront.pass.val != 0f ||
                m_State.stencilOpFront.fail.val != 0f ||
                m_State.stencilOpFront.zFail.val != 0f ||
                m_State.stencilOpFront.comp.val != 8f ||
                m_State.stencilOpBack.pass.val != 0f ||
                m_State.stencilOpBack.fail.val != 0f ||
                m_State.stencilOpBack.zFail.val != 0f ||
                m_State.stencilOpBack.comp.val != 8f)
            {
                sb.Append("Stencil {\n", indent);
                if (m_State.stencilRef.val != 0f)
                {
                    sb.Append($"Ref {m_State.stencilRef.val}\n", indent + 1);
                }
                if (m_State.stencilReadMask.val != 255f)
                {
                    sb.Append($"ReadMask {m_State.stencilReadMask.val}\n", indent + 1);
                }
                if (m_State.stencilWriteMask.val != 255f)
                {
                    sb.Append($"WriteMask {m_State.stencilWriteMask.val}\n", indent + 1);
                }
                if (m_State.stencilOp.pass.val != 0f ||
                    m_State.stencilOp.fail.val != 0f ||
                    m_State.stencilOp.zFail.val != 0f ||
                    m_State.stencilOp.comp.val != 8f)
                {
                    sb.Append(ConvertSerializedStencilOp(m_State.stencilOp, "", indent + 1));
                }
                if (m_State.stencilOpFront.pass.val != 0f ||
                    m_State.stencilOpFront.fail.val != 0f ||
                    m_State.stencilOpFront.zFail.val != 0f ||
                    m_State.stencilOpFront.comp.val != 8f)
                {
                    sb.Append(ConvertSerializedStencilOp(m_State.stencilOpFront, "Front", indent + 1));
                }
                if (m_State.stencilOpBack.pass.val != 0f ||
                    m_State.stencilOpBack.fail.val != 0f ||
                    m_State.stencilOpBack.zFail.val != 0f ||
                    m_State.stencilOpBack.comp.val != 8f)
                {
                    sb.Append(ConvertSerializedStencilOp(m_State.stencilOpBack, "Back", indent + 1));
                }
                sb.Append("}\n", indent);
            }

            if (m_State.fogMode != FogMode.Unknown ||
                m_State.fogColor.x.val != 0f ||
                m_State.fogColor.y.val != 0f ||
                m_State.fogColor.z.val != 0f ||
                m_State.fogColor.w.val != 0f ||
                m_State.fogDensity.val != 0f ||
                m_State.fogStart.val != 0f ||
                m_State.fogEnd.val != 0f)
            {
                sb.Append("Fog {\n", indent);
                if (m_State.fogMode != FogMode.Unknown)
                {
                    sb.Append("Mode ", indent + 1);
                    switch (m_State.fogMode)
                    {
                        case FogMode.Disabled:
                            sb.Append("Off");
                            break;
                        case FogMode.Linear:
                            sb.Append("Linear");
                            break;
                        case FogMode.Exp:
                            sb.Append("Exp");
                            break;
                        case FogMode.Exp2:
                            sb.Append("Exp2");
                            break;
                    }
                    sb.Append("\n");
                }
                if (m_State.fogColor.x.val != 0f ||
                    m_State.fogColor.y.val != 0f ||
                    m_State.fogColor.z.val != 0f ||
                    m_State.fogColor.w.val != 0f)
                {
                    var str = string.Format("Color ({0},{1},{2},{3})\n",
                        m_State.fogColor.x.val.ToString(CultureInfo.InvariantCulture),
                        m_State.fogColor.y.val.ToString(CultureInfo.InvariantCulture),
                        m_State.fogColor.z.val.ToString(CultureInfo.InvariantCulture),
                        m_State.fogColor.w.val.ToString(CultureInfo.InvariantCulture));
                    sb.Append(str, indent + 1);
                }
                if (m_State.fogDensity.val != 0f)
                {
                    sb.Append($"Density {m_State.fogDensity.val.ToString(CultureInfo.InvariantCulture)}\n", indent + 1);
                }
                if (m_State.fogStart.val != 0f ||
                    m_State.fogEnd.val != 0f)
                {
                    sb.Append($"Range {m_State.fogStart.val.ToString(CultureInfo.InvariantCulture)}, {m_State.fogEnd.val.ToString(CultureInfo.InvariantCulture)}\n", indent + 1);
                }
                sb.Append("}\n", indent);
            }

            if (m_State.lighting)
            {
                sb.Append($"Lighting {(m_State.lighting ? "On" : "Off")}\n", indent);
            }

            sb.Append($"GpuProgramID {m_State.gpuProgramID}\n", indent);

            return sb.ToString();
        }

        private static string ConvertSerializedStencilOp(SerializedStencilOp stencilOp, string suffix, int indent)
        {
            var sb = new StringBuilder();
            sb.Append($"Comp{suffix} {ConvertStencilComp(stencilOp.comp)}\n", indent);
            sb.Append($"Pass{suffix} {ConvertStencilOp(stencilOp.pass)}\n", indent);
            sb.Append($"Fail{suffix} {ConvertStencilOp(stencilOp.fail)}\n", indent);
            sb.Append($"ZFail{suffix} {ConvertStencilOp(stencilOp.zFail)}\n", indent);
            return sb.ToString();
        }

        private static string ConvertStencilOp(SerializedShaderFloatValue op)
        {
            switch (op.val)
            {
                case 0f:
                default:
                    return "Keep";
                case 1f:
                    return "Zero";
                case 2f:
                    return "Replace";
                case 3f:
                    return "IncrSat";
                case 4f:
                    return "DecrSat";
                case 5f:
                    return "Invert";
                case 6f:
                    return "IncrWrap";
                case 7f:
                    return "DecrWrap";
            }
        }

        private static string ConvertStencilComp(SerializedShaderFloatValue comp)
        {
            switch (comp.val)
            {
                case 0f:
                    return "Disabled";
                case 1f:
                    return "Never";
                case 2f:
                    return "Less";
                case 3f:
                    return "Equal";
                case 4f:
                    return "LEqual";
                case 5f:
                    return "Greater";
                case 6f:
                    return "NotEqual";
                case 7f:
                    return "GEqual";
                case 8f:
                default:
                    return "Always";
            }
        }

        private static string ConvertSerializedShaderRTBlendState(SerializedShaderRTBlendState[] rtBlend, bool rtSeparateBlend, int indent)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < rtBlend.Length; i++)
            {
                var blend = rtBlend[i];
                if (blend.srcBlend.val != 1f ||
                    blend.destBlend.val != 0f ||
                    blend.srcBlendAlpha.val != 1f ||
                    blend.destBlendAlpha.val != 0f)
                {
                    sb.Append("Blend ", indent);
                    if (i != 0 || rtSeparateBlend)
                    {
                        sb.Append($"{i} ");
                    }
                    sb.Append($"{ConvertBlendFactor(blend.srcBlend)} {ConvertBlendFactor(blend.destBlend)}");
                    if (blend.srcBlendAlpha.val != 1f ||
                        blend.destBlendAlpha.val != 0f)
                    {
                        sb.Append($", {ConvertBlendFactor(blend.srcBlendAlpha)} {ConvertBlendFactor(blend.destBlendAlpha)}");
                    }
                    sb.Append("\n");
                }

                if (blend.blendOp.val != 0f ||
                    blend.blendOpAlpha.val != 0f)
                {
                    sb.Append("BlendOp ", indent);
                    if (i != 0 || rtSeparateBlend)
                    {
                        sb.Append($"{i} ");
                    }
                    sb.Append(ConvertBlendOp(blend.blendOp));
                    if (blend.blendOpAlpha.val != 0f)
                    {
                        sb.Append($", {ConvertBlendOp(blend.blendOpAlpha)}");
                    }
                    sb.Append("\n");
                }

                var val = (int)blend.colMask.val;
                if (val != 0xf)
                {
                    sb.Append("ColorMask ", indent);
                    if (val == 0)
                    {
                        sb.Append(0);
                    }
                    else
                    {
                        if ((val & 0x2) != 0)
                        {
                            sb.Append("R");
                        }
                        if ((val & 0x4) != 0)
                        {
                            sb.Append("G");
                        }
                        if ((val & 0x8) != 0)
                        {
                            sb.Append("B");
                        }
                        if ((val & 0x1) != 0)
                        {
                            sb.Append("A");
                        }
                    }
                    sb.Append($" {i}\n");
                }
            }
            return sb.ToString();
        }

        private static string ConvertBlendOp(SerializedShaderFloatValue op)
        {
            switch (op.val)
            {
                case 0f:
                default:
                    return "Add";
                case 1f:
                    return "Sub";
                case 2f:
                    return "RevSub";
                case 3f:
                    return "Min";
                case 4f:
                    return "Max";
                case 5f:
                    return "LogicalClear";
                case 6f:
                    return "LogicalSet";
                case 7f:
                    return "LogicalCopy";
                case 8f:
                    return "LogicalCopyInverted";
                case 9f:
                    return "LogicalNoop";
                case 10f:
                    return "LogicalInvert";
                case 11f:
                    return "LogicalAnd";
                case 12f:
                    return "LogicalNand";
                case 13f:
                    return "LogicalOr";
                case 14f:
                    return "LogicalNor";
                case 15f:
                    return "LogicalXor";
                case 16f:
                    return "LogicalEquiv";
                case 17f:
                    return "LogicalAndReverse";
                case 18f:
                    return "LogicalAndInverted";
                case 19f:
                    return "LogicalOrReverse";
                case 20f:
                    return "LogicalOrInverted";
            }
        }

        private static string ConvertBlendFactor(SerializedShaderFloatValue factor)
        {
            switch (factor.val)
            {
                case 0f:
                    return "Zero";
                case 1f:
                default:
                    return "One";
                case 2f:
                    return "DstColor";
                case 3f:
                    return "SrcColor";
                case 4f:
                    return "OneMinusDstColor";
                case 5f:
                    return "SrcAlpha";
                case 6f:
                    return "OneMinusSrcColor";
                case 7f:
                    return "DstAlpha";
                case 8f:
                    return "OneMinusDstAlpha";
                case 9f:
                    return "SrcAlphaSaturate";
                case 10f:
                    return "OneMinusSrcAlpha";
            }
        }

        private static string ConvertSerializedTagMap(SerializedTagMap m_Tags, int indent)
        {
            var sb = new StringBuilder();
            if (m_Tags.tags.Length > 0)
            {
                sb.Append("Tags { ", indent);
                foreach (var pair in m_Tags.tags)
                {
                    sb.Append($"\"{pair.Key}\" = \"{pair.Value}\" ");
                }
                sb.Append("}\n");
            }
            return sb.ToString();
        }

        private static string GetindentString(int indent)
        {
            return new string(' ', indent * 4);
        }

        public static void Append(this StringBuilder sb, string str, int indent)
        {
            sb.Append($"{GetindentString(indent)}{str}");
        }

        private static string ConvertSerializedProperties(SerializedProperties m_PropInfo, int indent)
        {
            var sb = new StringBuilder();
            sb.Append("Properties {\n", indent);
            foreach (var m_Prop in m_PropInfo.m_Props)
            {
                sb.Append(ConvertSerializedProperty(m_Prop, indent + 1));
            }
            sb.Append("}\n", indent);
            return sb.ToString();
        }

        private static string ConvertSerializedProperty(SerializedProperty m_Prop, int indent)
        {
            var sb = new StringBuilder();
            sb.Append($"", indent);
            foreach (var m_Attribute in m_Prop.m_Attributes)
            {
                sb.Append($"[{m_Attribute}] ");
            }
            //TODO Flag
            sb.Append($"{m_Prop.m_Name} (\"{m_Prop.m_Description}\", ");
            switch (m_Prop.m_Type)
            {
                case SerializedPropertyType.Color:
                    sb.Append("Color");
                    break;
                case SerializedPropertyType.Vector:
                    sb.Append("Vector");
                    break;
                case SerializedPropertyType.Float:
                    sb.Append("Float");
                    break;
                case SerializedPropertyType.Range:
                    sb.Append($"Range({m_Prop.m_DefValue[1]}, {m_Prop.m_DefValue[2]})");
                    break;
                case SerializedPropertyType.Texture:
                    switch (m_Prop.m_DefTexture.m_TexDim)
                    {
                        case TextureDimension.Any:
                            sb.Append("any");
                            break;
                        case TextureDimension.Tex2D:
                            sb.Append("2D");
                            break;
                        case TextureDimension.Tex3D:
                            sb.Append("3D");
                            break;
                        case TextureDimension.Cube:
                            sb.Append("Cube");
                            break;
                        case TextureDimension.Tex2DArray:
                            sb.Append("2DArray");
                            break;
                        case TextureDimension.CubeArray:
                            sb.Append("CubeArray");
                            break;
                    }
                    break;
            }
            sb.Append(") = ");
            switch (m_Prop.m_Type)
            {
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Vector:
                    sb.Append($"({m_Prop.m_DefValue[0]},{m_Prop.m_DefValue[1]},{m_Prop.m_DefValue[2]},{m_Prop.m_DefValue[3]})");
                    break;
                case SerializedPropertyType.Float:
                case SerializedPropertyType.Range:
                    sb.Append(m_Prop.m_DefValue[0]);
                    break;
                case SerializedPropertyType.Int:
                    sb.Append((int)Math.Round(m_Prop.m_DefValue[0]));
                    break;
                case SerializedPropertyType.Texture:
                    sb.Append($"\"{m_Prop.m_DefTexture.m_DefaultName}\" {{ }}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            sb.Append("\n");
            return sb.ToString();
        }

        private static bool CheckGpuProgramUsable(ShaderCompilerPlatform platform, ShaderGpuProgramType programType)
        {
            switch (platform)
            {
                case ShaderCompilerPlatform.GL:
                    return programType == ShaderGpuProgramType.GLLegacy;
                case ShaderCompilerPlatform.D3D9:
                    return programType == ShaderGpuProgramType.DX9VertexSM20
                        || programType == ShaderGpuProgramType.DX9VertexSM30
                        || programType == ShaderGpuProgramType.DX9PixelSM20
                        || programType == ShaderGpuProgramType.DX9PixelSM30;
                case ShaderCompilerPlatform.Xbox360:
                case ShaderCompilerPlatform.PS3:
                case ShaderCompilerPlatform.PSP2:
                case ShaderCompilerPlatform.PS4:
                case ShaderCompilerPlatform.XboxOne:
                case ShaderCompilerPlatform.N3DS:
                case ShaderCompilerPlatform.WiiU:
                case ShaderCompilerPlatform.Switch:
                case ShaderCompilerPlatform.XboxOneD3D12:
                case ShaderCompilerPlatform.GameCoreXboxOne:
                case ShaderCompilerPlatform.GameCoreScarlett:
                case ShaderCompilerPlatform.PS5:
                    return programType == ShaderGpuProgramType.ConsoleVS
                        || programType == ShaderGpuProgramType.ConsoleFS
                        || programType == ShaderGpuProgramType.ConsoleHS
                        || programType == ShaderGpuProgramType.ConsoleDS
                        || programType == ShaderGpuProgramType.ConsoleGS;
                case ShaderCompilerPlatform.PS5NGGC:
                    return programType == ShaderGpuProgramType.PS5NGGC;
                case ShaderCompilerPlatform.D3D11:
                    return programType == ShaderGpuProgramType.DX11VertexSM40
                        || programType == ShaderGpuProgramType.DX11VertexSM50
                        || programType == ShaderGpuProgramType.DX11PixelSM40
                        || programType == ShaderGpuProgramType.DX11PixelSM50
                        || programType == ShaderGpuProgramType.DX11GeometrySM40
                        || programType == ShaderGpuProgramType.DX11GeometrySM50
                        || programType == ShaderGpuProgramType.DX11HullSM50
                        || programType == ShaderGpuProgramType.DX11DomainSM50;
                case ShaderCompilerPlatform.GLES20:
                    return programType == ShaderGpuProgramType.GLES;
                case ShaderCompilerPlatform.NaCl: //Obsolete
                    throw new NotSupportedException();
                case ShaderCompilerPlatform.Flash: //Obsolete
                    throw new NotSupportedException();
                case ShaderCompilerPlatform.D3D11_9x:
                    return programType == ShaderGpuProgramType.DX10Level9Vertex
                        || programType == ShaderGpuProgramType.DX10Level9Pixel;
                case ShaderCompilerPlatform.GLES3Plus:
                    return programType == ShaderGpuProgramType.GLES31AEP
                        || programType == ShaderGpuProgramType.GLES31
                        || programType == ShaderGpuProgramType.GLES3;
                case ShaderCompilerPlatform.PSM: //Unknown
                    throw new NotSupportedException();
                case ShaderCompilerPlatform.Metal:
                    return programType == ShaderGpuProgramType.MetalVS
                        || programType == ShaderGpuProgramType.MetalFS;
                case ShaderCompilerPlatform.OpenGLCore:
                    return programType == ShaderGpuProgramType.GLCore32
                        || programType == ShaderGpuProgramType.GLCore41
                        || programType == ShaderGpuProgramType.GLCore43;
                case ShaderCompilerPlatform.Vulkan:
                    return programType == ShaderGpuProgramType.SPIRV;
                default:
                    throw new NotSupportedException();
            }
        }

        public static string GetPlatformString(ShaderCompilerPlatform platform)
        {
            switch (platform)
            {
                case ShaderCompilerPlatform.GL:
                    return "openGL";
                case ShaderCompilerPlatform.D3D9:
                    return "d3d9";
                case ShaderCompilerPlatform.Xbox360:
                    return "xbox360";
                case ShaderCompilerPlatform.PS3:
                    return "ps3";
                case ShaderCompilerPlatform.D3D11:
                    return "d3d11";
                case ShaderCompilerPlatform.GLES20:
                    return "gles";
                case ShaderCompilerPlatform.NaCl:
                    return "glesdesktop";
                case ShaderCompilerPlatform.Flash:
                    return "flash";
                case ShaderCompilerPlatform.D3D11_9x:
                    return "d3d11_9x";
                case ShaderCompilerPlatform.GLES3Plus:
                    return "gles3";
                case ShaderCompilerPlatform.PSP2:
                    return "psp2";
                case ShaderCompilerPlatform.PS4:
                    return "ps4";
                case ShaderCompilerPlatform.XboxOne:
                    return "xboxone";
                case ShaderCompilerPlatform.PSM:
                    return "psm";
                case ShaderCompilerPlatform.Metal:
                    return "metal";
                case ShaderCompilerPlatform.OpenGLCore:
                    return "glcore";
                case ShaderCompilerPlatform.N3DS:
                    return "n3ds";
                case ShaderCompilerPlatform.WiiU:
                    return "wiiu";
                case ShaderCompilerPlatform.Vulkan:
                    return "vulkan";
                case ShaderCompilerPlatform.Switch:
                    return "switch";
                case ShaderCompilerPlatform.XboxOneD3D12:
                    return "xboxone_d3d12";
                case ShaderCompilerPlatform.GameCoreXboxOne:
                    return "xboxone";
                case ShaderCompilerPlatform.GameCoreScarlett:
                    return "xbox_scarlett";
                case ShaderCompilerPlatform.PS5:
                    return "ps5";
                case ShaderCompilerPlatform.PS5NGGC:
                    return "ps5_nggc";
                default:
                    return "unknown";
            }
        }

        private static string header = "//////////////////////////////////////////\n" +
                                      "//\n" +
                                      "// NOTE: This is *not* a valid shader file\n" +
                                      "//\n" +
                                      "///////////////////////////////////////////\n";
    }

    public class ShaderSubProgramEntry
    {
        public int Offset;
        public int Length;
        public int Segment;

        public ShaderSubProgramEntry(BinaryReader reader, int[] version)
        {
            Offset = reader.ReadInt32();
            Length = reader.ReadInt32();
            if (version[0] > 2019 || (version[0] == 2019 && version[1] >= 3)) //2019.3 and up
            {
                Segment = reader.ReadInt32();
            }
        }
    }

    public class ShaderProgram
    {
        public ShaderSubProgramEntry[] entries;
        public ShaderSubProgramWrap[] m_SubProgramWraps;

        public ShaderProgram(BinaryReader reader, int[] version)
        {
            var subProgramsCapacity = reader.ReadInt32();
            entries = new ShaderSubProgramEntry[subProgramsCapacity];
            for (int i = 0; i < subProgramsCapacity; i++)
            {
                entries[i] = new ShaderSubProgramEntry(reader, version);
            }
            m_SubProgramWraps = new ShaderSubProgramWrap[subProgramsCapacity];
        }

        public void Read(BinaryReader reader, int segment)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.Segment == segment)
                {
                    m_SubProgramWraps[i] = new ShaderSubProgramWrap(reader, entry);
                }
            }
        }

        public string Export(string shader)
        {
            var evaluator = new MatchEvaluator(match =>
            {
                var index = int.Parse(match.Groups[1].Value);
                ShaderSubProgramWrap subProgramWrap = m_SubProgramWraps[index];
                ShaderSubProgram subProgram = subProgramWrap.genShaderSubProgram();
                var subProgramsStr = subProgram.Export();
                return subProgramsStr;
            });
            shader = Regex.Replace(shader, "GpuProgramIndex (.+)", evaluator);
            return shader;
        }
    }

    public class ShaderSubProgramWrap
    {
        private byte[] buffer;
        private ShaderSubProgramEntry entry;
        
        public ShaderSubProgramWrap(BinaryReader reader, ShaderSubProgramEntry paramEntry)
        {
            entry = paramEntry;
            buffer = new byte[entry.Length];
            reader.BaseStream.Read(buffer, 0, entry.Length);
        }

        public ShaderSubProgram genShaderSubProgram()
        {
            ShaderSubProgram shaderSubProgram = null;
            using (var reader = new BinaryReader(new MemoryStream(buffer)))
            {
                shaderSubProgram = new ShaderSubProgram(reader);
            }
            return shaderSubProgram;
        }
    }

    public class ShaderSubProgram
    {
        private int m_Version;
        public ShaderGpuProgramType m_ProgramType;
        public string[] m_Keywords;
        public string[] m_LocalKeywords;
        public byte[] m_ProgramCode;

        private static int i = 0;
        public ShaderSubProgram(BinaryReader reader)
        {
            //LoadGpuProgramFromData
            //201509030 - Unity 5.3
            //201510240 - Unity 5.4
            //201608170 - Unity 5.5
            //201609010 - Unity 5.6, 2017.1 & 2017.2
            //201708220 - Unity 2017.3, Unity 2017.4 & Unity 2018.1
            //201802150 - Unity 2018.2 & Unity 2018.3
            //201806140 - Unity 2019.1~2021.1
            //202012090 - Unity 2021.2
            m_Version = reader.ReadInt32();
            m_ProgramType = (ShaderGpuProgramType)reader.ReadInt32();
            reader.BaseStream.Position += 12;
            if (m_Version >= 201608170)
            {
                reader.BaseStream.Position += 4;
            }
            var m_KeywordsSize = reader.ReadInt32();
            m_Keywords = new string[m_KeywordsSize];
            for (int i = 0; i < m_KeywordsSize; i++)
            {
                m_Keywords[i] = reader.ReadAlignedString();
            }
            if (m_Version >= 201806140 && m_Version < 202012090)
            {
                var m_LocalKeywordsSize = reader.ReadInt32();
                m_LocalKeywords = new string[m_LocalKeywordsSize];
                for (int i = 0; i < m_LocalKeywordsSize; i++)
                {
                    m_LocalKeywords[i] = reader.ReadAlignedString();
                }
            }
            m_ProgramCode = reader.ReadUInt8Array();
            reader.AlignStream();

            //TODO
        }

        public static string ConvertDxbc_diassemble(byte[] buf)
        {
            var result = DXBC.GetDXBCDiassembleText(buf);
            return result;
        }

        public static string ConvertDxbc(byte[] buf)
        {
            //var str = Convert.ToHexString(buf);
            //var buf2 = Convert.FromHexString(str);

            //if (buf2.Length == buf.Length)
            //{
            //    for (int i = 0; i < buf.Length; i++)
            //    {
            //        if (buf[i] != buf2[i])
            //        {
            //            Console.WriteLine("dxbc code not equal");
            //            break;
            //        }
            //    }
            //}
            //else
            //{
            //    Console.WriteLine("dxbc code not equal");
            //}

            return ConvertDxbc_diassemble(buf);
        }

        public string Export()
        {
            var sb = new StringBuilder();
            if (m_Keywords.Length > 0)
            {
                sb.Append("Keywords { ");
                foreach (string keyword in m_Keywords)
                {
                    sb.Append($"\"{keyword}\" ");
                }
                sb.Append("}\n");
            }
            if (m_LocalKeywords != null && m_LocalKeywords.Length > 0)
            {
                sb.Append("Local Keywords { ");
                foreach (string keyword in m_LocalKeywords)
                {
                    sb.Append($"\"{keyword}\" ");
                }
                sb.Append("}\n");
            }

            sb.Append("\"");
            if (m_ProgramCode.Length > 0)
            {
                switch (m_ProgramType)
                {
                    case ShaderGpuProgramType.GLLegacy:
                    case ShaderGpuProgramType.GLES31AEP:
                    case ShaderGpuProgramType.GLES31:
                    case ShaderGpuProgramType.GLES3:
                    case ShaderGpuProgramType.GLES:
                    case ShaderGpuProgramType.GLCore32:
                    case ShaderGpuProgramType.GLCore41:
                    case ShaderGpuProgramType.GLCore43:
                        sb.Append(Encoding.UTF8.GetString(m_ProgramCode));
                        break;
                    case ShaderGpuProgramType.DX9VertexSM20:
                    case ShaderGpuProgramType.DX9VertexSM30:
                    case ShaderGpuProgramType.DX9PixelSM20:
                    case ShaderGpuProgramType.DX9PixelSM30:
                        {
                            sb.Append(ConvertDxbc(m_ProgramCode));
                            /*var shaderBytecode = new ShaderBytecode(m_ProgramCode);
                            sb.Append(shaderBytecode.Disassemble());*/
                            // sb.Append("// shader disassembly not supported on DXBC");
                            break;
                        }
                    case ShaderGpuProgramType.DX10Level9Vertex:
                    case ShaderGpuProgramType.DX10Level9Pixel:
                    case ShaderGpuProgramType.DX11VertexSM40:
                    case ShaderGpuProgramType.DX11VertexSM50:
                    case ShaderGpuProgramType.DX11PixelSM40:
                    case ShaderGpuProgramType.DX11PixelSM50:
                    case ShaderGpuProgramType.DX11GeometrySM40:
                    case ShaderGpuProgramType.DX11GeometrySM50:
                    case ShaderGpuProgramType.DX11HullSM50:
                    case ShaderGpuProgramType.DX11DomainSM50:
                        {
                            //int start = 6;
                            //if (m_Version == 201509030) // 5.3
                            //{
                            //    start = 5;
                            //}
                            //var buff = new byte[m_ProgramCode.Length - start];
                            //Buffer.BlockCopy(m_ProgramCode, start, buff, 0, buff.Length);
                            
                            //sb.Append(ConvertDxbc(buff));
                            sb.Append(ConvertDxbc(m_ProgramCode));
                            /*var shaderBytecode = new ShaderBytecode(buff);
                            sb.Append(shaderBytecode.Disassemble());*/
                            // sb.Append("// shader disassembly not supported on DXBC");
                            break;
                        }
                    case ShaderGpuProgramType.MetalVS:
                    case ShaderGpuProgramType.MetalFS:
                        using (var reader = new BinaryReader(new MemoryStream(m_ProgramCode)))
                        {
                            var fourCC = reader.ReadUInt32();
                            if (fourCC == 0xf00dcafe)
                            {
                                int offset = reader.ReadInt32();
                                reader.BaseStream.Position = offset;
                            }
                            var entryName = reader.ReadStringToNull();
                            var buff = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
                            sb.Append(Encoding.UTF8.GetString(buff));
                        }
                        break;
                    case ShaderGpuProgramType.SPIRV:
                        try
                        {
                            sb.Append(SpirVShaderConverter.Convert(m_ProgramCode));
                        }
                        catch (Exception e)
                        {
                            sb.Append($"// disassembly error {e.Message}\n");
                        }
                        break;
                    case ShaderGpuProgramType.ConsoleVS:
                    case ShaderGpuProgramType.ConsoleFS:
                    case ShaderGpuProgramType.ConsoleHS:
                    case ShaderGpuProgramType.ConsoleDS:
                    case ShaderGpuProgramType.ConsoleGS:
                        sb.Append(Encoding.UTF8.GetString(m_ProgramCode));
                        break;
                    default:
                        sb.Append($"//shader disassembly not supported on {m_ProgramType}");
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
