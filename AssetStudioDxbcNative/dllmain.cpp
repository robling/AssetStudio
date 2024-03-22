// dllmain.cpp : Defines the entry point for the DLL application.
#include "pch.h"
#include "D3D_Shaders/includes/log.h"
#include "D3D_Shaders/includes/version.h"
#include "DecompileHLSL.h"
#include "dllmain.h"

#pragma comment(lib, "d3dcompiler.lib")

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
                     )
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

FILE* LogFile = NULL;
bool gLogDebug = false;

static HRESULT DisassembleMS(const void* pShaderBytecode, std::size_t BytecodeLength, std::string* asmText)
{
    ID3DBlob* disassembly = nullptr;
    UINT flags = D3D_DISASM_ENABLE_DEFAULT_VALUE_PRINTS;
    std::string comments = "//   using 3Dmigoto command line v" + std::string(VER_FILE_VERSION_STR) + " on " + LogTime() + "//\n";

    HRESULT hr = D3DDisassemble(pShaderBytecode, BytecodeLength, flags, comments.c_str(), &disassembly);
    if (FAILED(hr)) {
        LogInfo("  disassembly failed. Error: %x\n", hr);
        return hr;
    }

    // Successfully disassembled into a Blob.  Let's turn it into a C++ std::string
    // so that we don't have a null byte as a terminator.  If written to a file,
    // the null bytes otherwise cause Git diffs to fail.
    *asmText = string(static_cast<char*>(disassembly->GetBufferPointer()));

    disassembly->Release();
    return S_OK;
}

static HRESULT Decompile(const void* pShaderBytecode, size_t BytecodeLength, std::string* hlslText, std::string* shaderModel)
{
    // Set all to zero, so we only init the ones we are using here:
    ParseParameters p = { 0 };
    DecompilerSettings d;
    bool patched = false;
    bool errorOccurred = false;
    std::string disassembly;
    HRESULT hret;

    hret = DisassembleMS(pShaderBytecode, BytecodeLength, &disassembly);
    if (FAILED(hret))
        return E_FAIL;

    LogInfo("    creating HLSL representation\n");

    p.bytecode = pShaderBytecode;
    p.decompiled = disassembly.c_str(); // XXX: Why do we call this "decompiled" when it's actually disassembled?
    p.decompiledSize = disassembly.size();
    p.G = &d;

    // Disable IniParams and StereoParams registers. This avoids inserting
    // these in a shader that already has them, such as some of our test
    // cases. Also, while cmd_Decompiler is part of 3DMigoto, it is NOT
    // 3DMigoto so it doesn't really make sense that it should add 3DMigoto
    // registers, and if someone wants these registers there is nothing
    // stopping them from adding them by hand. May break scripts that use
    // cmd_Decompiler and expect these to be here, but those scripts can be
    // updated to add them or they can keep using an old version.
    d.IniParamsReg = -1;
    d.StereoParamsReg = -1;

    *hlslText = DecompileBinaryHLSL(p, patched, *shaderModel, errorOccurred);
    if (!hlslText->size() || errorOccurred) {
        LogInfo("    error while decompiling\n");
        return E_FAIL;
    }

    return S_OK;
}


EXTERN_C_START
/*
__declspec(dllexport) void APIENTRY DXBCDiassemble_Test(unsigned char* data, unsigned int* out_len)
{
    std::cout << "Hello World! (" << *out_len << ")" << std::endl;
    std::cout << data[0] << std::endl;
    std::cout << data[1] << std::endl;
    std::cout << data[2] << std::endl;
    *out_len = 1123213;
}

__declspec(dllexport) byte* APIENTRY DXBCDiassemble_file(char* filename, unsigned long* out_len)
{
    *out_len = 0;
    std::cout << "Start Disassemble!" << std::endl;
    std::vector<::byte> ASM;
    HRESULT ok = disassembler(readFile(std::string(filename)), &ASM, NULL);
    std::cout << "End Disassemble!" << std::endl;

    if (FAILED(ok))
    {
        std::cout << "[Native] Disassemble Failed!" << std::endl;
        return nullptr;
    }

    auto out_data = new byte[ASM.size()];
    *out_len = ASM.size();
    std::copy(ASM.begin(), ASM.end(), out_data);
	return out_data;
}

__declspec(dllexport) byte* APIENTRY DXBCDiassemble_backup(char* filename, byte* data, unsigned long len, unsigned long* out_len)
{
    *out_len = 0;
    auto real_len = len - 38;
    if (real_len < 10)
    {
        std::cout << "Error Input DXBC binary! 1" << std::endl;
        return nullptr;
    }
    std::vector<byte> real_data;
    for (size_t i = 38; i < len; i++)
    {
        real_data.push_back(data[i]);
    }

    std::cout << (int)real_data[0] << (int)real_data[1] << (int)real_data[2] << (int)real_data[3] << std::endl;
    if (real_data[0] != 'D' || real_data[1] != 'X' || real_data[2] != 'B' || real_data[3] != 'C')
    {
        std::cout << "Error Input DXBC binary! 2" << std::endl;
        return nullptr;
    }
    std::cout << real_data.size() << std::endl;


    auto data_from_file = readFile(std::string(filename));
    if (data_from_file.size() != real_data.size())
    {
        std::cout << "data from file not match(length)" << std::endl;
        std::cout << "data from file:\t";
        std::cout << data_from_file.size();
        std::cout << "; real data:\t";
        std::cout << real_data.size() << ";" << std::endl;
    }
    else
    {
        for (size_t i = 0; i < data_from_file.size(); i++)
        {
            if (real_data[i] != data_from_file[i])
            {
                std::cout << "data from file not match(data)" << std::endl;
                break;
            }
        }
    }
    std::cout << "last byte fomr C#: " << (int)real_data[real_data.size() - 1] << std::endl;

    std::cout << "Start Disassemble!" << std::endl;
    std::vector<::byte> ASM;
    HRESULT ok = disassembler(real_data, &ASM, NULL);

    if (FAILED(ok))
    {
        std::cout << "[Native] Disassemble Failed!" << std::endl;
        return nullptr;
    }
    std::cout << "End Disassemble!" << std::endl;

    std::cout << "Start Decompile!" << std::endl;
    string hlsl;
    string sm;
    ok = Decompile(real_data.data(), real_data.size(), &hlsl, &sm);

    if (FAILED(ok))
    {
        std::cout << "[Native] Decompile Failed!" << std::endl;
        return nullptr;
    }
    std::cout << "End Decompile!" << std::endl;


    auto out_data = new byte[hlsl.size()];
    *out_len = hlsl.size();
    std::copy(hlsl.begin(), hlsl.end(), out_data);

    return out_data;
}
*/

bool CheckBinaryLength(unsigned long real_len)
{
    if (real_len < 10)
    {
        std::cout << "Error Input DXBC binary! 1" << std::endl;
        return false;
    }
    return true;
}

bool CheckFourCC(std::vector<byte>& real_data)
{
    std::cout << (int)real_data[0] << (int)real_data[1] << (int)real_data[2] << (int)real_data[3] << std::endl;
    if (real_data[0] != 'D' || real_data[1] != 'X' || real_data[2] != 'B' || real_data[3] != 'C')
    {
        std::cout << "Error Input DXBC binary! 2" << std::endl;
        return false;
    }
    return true;
}

__declspec(dllexport) byte* APIENTRY DXBCDiassemble(byte* data, unsigned long len, unsigned long* out_len)
{
    *out_len = 0;

    if (!CheckBinaryLength(len - 38)) return nullptr;

    std::vector<byte> real_data(len - 38);
    std::copy(data + 38, data + len, real_data.begin());

    if (!CheckFourCC(real_data)) return nullptr;

    std::cout << "Start Decompile!" << std::endl;
    string hlsl;
    string sm;
    auto ok = Decompile(real_data.data(), real_data.size(), &hlsl, &sm);

    if (FAILED(ok))
    {
        std::cout << "[Native] Decompile Failed!" << std::endl;
        return nullptr;
    }
    std::cout << "End Decompile!" << std::endl;

    auto out_data = new byte[hlsl.size()];
    *out_len = hlsl.size();
    std::copy(hlsl.begin(), hlsl.end(), out_data);

    return out_data;
}

__declspec(dllexport) void APIENTRY  free_data(byte* data) {
    delete[] data;
}
EXTERN_C_END