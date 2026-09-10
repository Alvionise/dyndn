using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Protects the router password at rest with Windows DPAPI (current-user scope) so the
/// stored config no longer contains a recoverable secret. Values without the
/// <see cref="ProtectedPrefix"/> marker are treated as legacy plaintext and passed through.
/// </summary>
internal static class PasswordProtector
{
    private const string ProtectedPrefix = "dpapi:";
    private const uint CryptProtectUiForbidden = 0x1;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DyndDns:RouterPassword:v1");
    private static readonly GCHandle EntropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || plaintext.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return plaintext;

        try
        {
            var protectedBytes = Transform(Encoding.UTF8.GetBytes(plaintext), protect: true);
            return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
        }
        catch (Exception ex)
        {
            // Saving the config must not fail when DPAPI is unavailable; keep it readable.
            System.Diagnostics.Trace.TraceError($"DPAPI protect failed, storing plaintext: {ex.Message}");
            return plaintext;
        }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return stored;

        try
        {
            var bytes = Transform(Convert.FromBase64String(stored[ProtectedPrefix.Length..]), protect: false);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"DPAPI unprotect failed: {ex.Message}");
            return string.Empty;
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);

        try
        {
            var inputBlob = new DataBlob
            {
                cbData = (uint)input.Length,
                pbData = inputHandle.AddrOfPinnedObject()
            };
            var entropyBlob = new DataBlob
            {
                cbData = (uint)Entropy.Length,
                pbData = EntropyHandle.AddrOfPinnedObject()
            };

            DataBlob outputBlob = default;
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);

            if (!succeeded)
                throw new CryptographicException(Marshal.GetLastWin32Error());

            try
            {
                var result = new byte[(int)outputBlob.cbData];
                Marshal.Copy(outputBlob.pbData, result, 0, result.Length);
                return result;
            }
            finally
            {
                if (outputBlob.pbData != IntPtr.Zero)
                    LocalFree(outputBlob.pbData);
            }
        }
        finally
        {
            inputHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public uint cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        IntPtr pszDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
