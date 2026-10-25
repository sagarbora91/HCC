using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Hcc.Application.GuestIds;

namespace Hcc.Infrastructure.SqlServer.GuestIds;

/// <summary>
/// Guest ID key ring from configuration (DEC-068). Each key value is a 32-byte AES key wrapped by Windows DPAPI:
/// <c>dpapi-user:&lt;base64&gt;</c> (bound to the Windows account that runs the HCC service — preferred) or
/// <c>dpapi-machine:&lt;base64&gt;</c> (any process on this PC). <c>plain:&lt;base64&gt;</c> is accepted only when
/// <c>allowPlainKeys</c> is set (development / tests). Key values never go in git: put them in the service account's
/// environment or a protected appsettings.Production.json outside the repository. Generate one with
/// <c>dotnet run --project src/Hcc.Api -- guest-id-key new &lt;keyId&gt;</c>.
/// </summary>
public sealed class DpapiGuestIdKeyRing : IGuestIdKeyRing
{
    private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("HCC guest ID key v1");
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    public DpapiGuestIdKeyRing(string? currentKeyId, IReadOnlyDictionary<string, string?> keys, bool allowPlainKeys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (string.IsNullOrWhiteSpace(currentKeyId))
            throw new InvalidOperationException("Hcc:GuestIdKeys:Current is not configured: guest ID numbers cannot be encrypted (DEC-068).");
        foreach (var (id, value) in keys)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            _keys[id] = Unwrap(id, value, allowPlainKeys);
        }

        if (!_keys.ContainsKey(currentKeyId))
            throw new InvalidOperationException($"Hcc:GuestIdKeys:Keys:{currentKeyId} (the current guest ID key) is not configured.");
        CurrentKeyId = currentKeyId;
    }

    public string CurrentKeyId { get; }

    public byte[]? Key(string keyId) => _keys.TryGetValue(keyId, out var k) ? k : null;

    private static byte[] Unwrap(string id, string value, bool allowPlain)
    {
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        var scheme = colon < 0 ? string.Empty : value[..colon];
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(value[(colon + 1)..]);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"Guest ID key {id} is not valid base64.");
        }

        var key = scheme switch
        {
            "dpapi-user" => Dpapi.Unprotect(blob, machine: false),
            "dpapi-machine" => Dpapi.Unprotect(blob, machine: true),
            "plain" when allowPlain => blob,
            "plain" => throw new InvalidOperationException($"Guest ID key {id} is a plain key; plain keys are allowed only with Hcc:GuestIdKeys:AllowPlainKeys (development)."),
            _ => throw new InvalidOperationException($"Guest ID key {id} must start with dpapi-user:, dpapi-machine: or plain:."),
        };
        if (key.Length != 32) throw new InvalidOperationException($"Guest ID key {id} must be 32 bytes (AES-256).");
        return key;
    }

    /// <summary>A new random key wrapped for this PC (the raw key is never printed).</summary>
    public static string NewWrappedKey(bool machine)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            return (machine ? "dpapi-machine:" : "dpapi-user:") + Convert.ToBase64String(Dpapi.Protect(key, machine));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Windows DPAPI (crypt32 CryptProtectData) without the System.Security.Cryptography.ProtectedData package.</summary>
    private static class Dpapi
    {
        private const int UiForbidden = 0x1;
        private const int LocalMachine = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr LocalFree(IntPtr mem);

        public static byte[] Protect(byte[] data, bool machine) => Run(data, machine, protect: true);

        public static byte[] Unprotect(byte[] data, bool machine) => Run(data, machine, protect: false);

        private static byte[] Run(byte[] data, bool machine, bool protect)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI-protected guest ID keys need Windows.");
            var hIn = GCHandle.Alloc(data, GCHandleType.Pinned);
            var hEnt = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
            try
            {
                var input = new DataBlob { Size = data.Length, Data = hIn.AddrOfPinnedObject() };
                var entropy = new DataBlob { Size = Entropy.Length, Data = hEnt.AddrOfPinnedObject() };
                var flags = UiForbidden | (machine ? LocalMachine : 0);
                var ok = protect
                    ? CryptProtectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, flags, out var output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, flags, out output);
                if (!ok) throw new CryptographicException("DPAPI could not " + (protect ? "protect" : "unprotect") + " the guest ID key (wrong Windows account or PC?).",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
                try
                {
                    var result = new byte[output.Size];
                    Marshal.Copy(output.Data, result, 0, output.Size);
                    return result;
                }
                finally
                {
                    LocalFree(output.Data);
                }
            }
            finally
            {
                hIn.Free();
                hEnt.Free();
            }
        }
    }
}
