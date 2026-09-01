using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AICompanion.Infrastructure
{
    public sealed class DpapiProtection
    {
        private const int CryptprotectUiForbidden = 0x1;
        private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("ChillWithYou.AICompanion.v1");

        public byte[] Protect(byte[] plain)
        {
            if (plain == null) throw new ArgumentNullException(nameof(plain));
            return Transform(plain, true);
        }

        public byte[] Unprotect(byte[] cipher)
        {
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            return Transform(cipher, false);
        }

        public string ProtectString(string value) => Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        public string UnprotectString(string value) => Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(value)));

        public static string Checksum(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        private static byte[] Transform(byte[] input, bool protect)
        {
            var inputBlob = ToBlob(input);
            var entropyBlob = ToBlob(OptionalEntropy);
            var outputBlob = new DataBlob();
            try
            {
                bool ok = protect
                    ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref outputBlob)
                    : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref outputBlob);
                if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
                var output = new byte[outputBlob.Length];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                if (inputBlob.Data != IntPtr.Zero) Marshal.FreeHGlobal(inputBlob.Data);
                if (entropyBlob.Data != IntPtr.Zero) Marshal.FreeHGlobal(entropyBlob.Data);
                if (outputBlob.Data != IntPtr.Zero) LocalFree(outputBlob.Data);
            }
        }

        private static DataBlob ToBlob(byte[] bytes)
        {
            var blob = new DataBlob { Length = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
            if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob { public int Length; public IntPtr Data; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
