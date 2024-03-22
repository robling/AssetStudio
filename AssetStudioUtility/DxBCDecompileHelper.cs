using AssetStudio.PInvoke;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudio
{
    public partial class DXBC
    {
        private static string CalculateHash(byte[] input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return Convert.ToHexString(sha256.ComputeHash(input));
            }
        }
        internal const string DllName = "AssetStudioDxbcNative";
        static DXBC()
        {
            DllLoader.PreloadDll(DllName);
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        private static extern IntPtr DXBCDiassemble([In] byte[] in_data, ulong in_len, ref ulong out_len);
    
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern void free_data(IntPtr data);
    
        public static string GetDXBCDiassembleText(byte[] in_data)
        {
            ulong len = 0;
         
            IntPtr dataPtr = DXBCDiassemble(in_data, (ulong)in_data.Length, ref len);
            if (len == 0 || dataPtr == IntPtr.Zero)
            {
                Console.WriteLine("DXBCDiassemble Failed");
                return string.Empty;
            }
            byte[] data = new byte[len];
            var rst = Marshal.PtrToStringAnsi(dataPtr);

            // 释放内存
            free_data(dataPtr);
            return rst;
        }
    }
}
