using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Hcc.Api.Hosting;

/// <summary>
/// F-M05: the settings file that holds the guest-ID keys may be readable only by SYSTEM, Administrators and the account the HCC
/// service runs as. Checked at start-up in Production whenever a guest-ID key is configured; any other reader (Users, Authenticated
/// Users, Everyone, a staff account) stops the service with a clear message and the icacls command that fixes it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GuestIdKeyFileCheck
{
    private const string System = "S-1-5-18";
    private const string Administrators = "S-1-5-32-544";
    private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    private const FileSystemRights ReadBits = FileSystemRights.ReadData | FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes;
    private const int GenericAll = 0x10000000;
    private const int GenericRead = unchecked((int)0x80000000);

    /// <summary>SIDs allowed to read the file: SYSTEM, Administrators, TrustedInstaller, the current process account and (if it exists) the <c>NT SERVICE\HccServer</c> service SID.</summary>
    public static HashSet<string> AllowedReaders()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System, Administrators, TrustedInstaller };
        using (var me = WindowsIdentity.GetCurrent())
        {
            if (me.User is not null) set.Add(me.User.Value);
        }

        try
        {
            set.Add(((SecurityIdentifier)new NTAccount("NT SERVICE", "HccServer").Translate(typeof(SecurityIdentifier))).Value);
        }
        catch (IdentityNotMappedException)
        {
            // The service does not exist (yet): nothing to add.
        }

        return set;
    }

    /// <summary>Problems with the file's access list; empty = protected. Pure over the access rules (unit-tested).</summary>
    public static IReadOnlyList<string> Problems(FileSystemSecurity security, ISet<string> allowedReaders)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(allowedReaders);
        var problems = new List<string>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            var rights = (int)rule.FileSystemRights;
            var reads = (rule.FileSystemRights & ReadBits) != 0 || (rights & (GenericAll | GenericRead)) != 0;
            if (!reads) continue;
            var sid = rule.IdentityReference.Value;
            if (allowedReaders.Contains(sid)) continue;
            problems.Add($"{Name(sid)} can read it");
        }

        return problems;
    }

    /// <summary>Throws when <paramref name="path"/> exists and is readable by anyone else than SYSTEM, Administrators and the service account.</summary>
    public static void EnsureProtected(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists) return;
        var problems = Problems(file.GetAccessControl(), AllowedReaders());
        if (problems.Count == 0) return;
        throw new InvalidOperationException(
            $"HCC configuration is not valid:{Environment.NewLine} - {path} holds the guest ID key but {string.Join(", ", problems)}. "
            + "Only SYSTEM, Administrators and the HCC service account may read it. Fix (PowerShell Admin): "
            + $"icacls \"{path}\" /inheritance:r /grant:r *S-1-5-18:(F) *S-1-5-32-544:(F) \"NT SERVICE\\HccServer:(R)\"  (use your service account if it is not NT SERVICE\\HccServer), then start the service again.");
    }

    private static string Name(string sid)
    {
        try
        {
            return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid;
        }
    }
}
