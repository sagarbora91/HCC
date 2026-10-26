using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Hcc.Api.Hosting;

/// <summary>Hosting settings, section <c>Hcc:Hosting</c> (appsettings.Production.json on the hotel PC; no secrets: the certificate is found by thumbprint in the machine store).</summary>
public sealed class HccHostingOptions
{
    public const string Section = "Hcc:Hosting";

    /// <summary>Name shown by <c>/api/v1/system/discovery</c> and typed into the tablets/phones. Empty = the PC name.</summary>
    public string? ServerName { get; set; }

    /// <summary>LAN HTTPS port (all interfaces). Default 5443.</summary>
    public int HttpsPort { get; set; } = 5443;

    /// <summary>Plain-HTTP port bound to 127.0.0.1 ONLY, for FIRST-TIME SETUP (setup, first-Owner bootstrap, health, discovery; no
    /// sign-in). 0 = none (the default, F-L11). INSTALL.md sets 5080 for setup; Initialize-HccProperty.ps1 sets it back to 0.</summary>
    public int LoopbackHttpPort { get; set; }

    /// <summary>Separate listener for the anonymous booking routes only (F-H04). 0 = none (default). Bound only while
    /// <c>Hcc:WebBooking:Enabled</c> is true. Must differ from the other ports.</summary>
    public int PublicPort { get; set; }

    /// <summary>Address the public listener binds to. Default 127.0.0.1 (plain HTTP behind a reverse proxy on this PC that terminates
    /// TLS). Any other address gets HTTPS with the server certificate.</summary>
    public string? PublicBindAddress { get; set; } = "127.0.0.1";

    /// <summary>Reverse-proxy addresses whose <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> are trusted on the public listener (only there).</summary>
    public string[]? KnownProxies { get; set; }

    /// <summary>Same as <see cref="KnownProxies"/> as CIDR networks (for example 10.0.0.0/24).</summary>
    public string[]? KnownNetworks { get; set; }

    /// <summary>Largest request body in MB (F-L02). Default 8: the largest legitimate request is an ID image of about 5.4 MB.</summary>
    public int MaxRequestBodyMegabytes { get; set; } = 8;

    /// <summary>SHA-1 thumbprint (40 hex) of the server certificate in LocalMachine\My (created by New-HccCertificate.ps1).</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Folder holding the production web build's <c>index.html</c> (served at <c>/</c>). Empty = the <c>web</c> folder next to <c>Hcc.Api.exe</c>.</summary>
    public string? WebRoot { get; set; }
}

/// <summary>
/// Startup configuration checks. In Production every problem is reported at once and the service refuses to start, so a
/// half-configured hotel PC never serves with development settings. Development only checks the connection string is present.
/// </summary>
public static partial class HccConfigValidator
{
    [GeneratedRegex("^[0-9A-Fa-f]{40}$")]
    private static partial Regex Thumbprint();

    public static IReadOnlyList<string> Validate(IConfiguration configuration, bool production)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>();
        var cs = configuration["Hcc:Sql:ConnectionString"];
        if (string.IsNullOrWhiteSpace(cs))
        {
            errors.Add("Hcc:Sql:ConnectionString is missing.");
        }
        else if (production)
        {
            try
            {
                var b = new SqlConnectionStringBuilder(cs);
                if (!b.IntegratedSecurity) errors.Add("Hcc:Sql:ConnectionString must use Integrated Security=True (Windows login of the service account), not a SQL user.");
                if (!string.IsNullOrEmpty(b.Password) || !string.IsNullOrEmpty(b.UserID)) errors.Add("Hcc:Sql:ConnectionString must not contain a user name or password.");
                if (b.TrustServerCertificate) errors.Add("TrustServerCertificate=True is for local development only; production must not use it (give SQL Server a trusted certificate, or use Encrypt=Optional on a local instance).");
                if (b.Encrypt == SqlConnectionEncryptOption.Optional && !IsLocalDataSource(b.DataSource)) errors.Add("Encrypt=Optional is allowed only for a SQL Server instance on this same PC.");
                if (string.IsNullOrWhiteSpace(b.InitialCatalog)) errors.Add("Hcc:Sql:ConnectionString must name the database (Initial Catalog=Hcc).");
            }
            catch (ArgumentException ex)
            {
                errors.Add("Hcc:Sql:ConnectionString is not a valid connection string: " + ex.Message);
            }
        }

        if (!production) return errors;

        var backup = configuration["Hcc:Sql:BackupDirectory"];
        if (string.IsNullOrWhiteSpace(backup) || !Path.IsPathRooted(backup) || string.Equals(backup, "NUL", StringComparison.OrdinalIgnoreCase))
            errors.Add("Hcc:Sql:BackupDirectory must be a full folder path (for example D:\\HccBackups); \"NUL\" and relative paths are not allowed.");

        var h = configuration.GetSection(HccHostingOptions.Section).Get<HccHostingOptions>() ?? new HccHostingOptions();
        if (h.HttpsPort is < 1 or > 65535) errors.Add("Hcc:Hosting:HttpsPort must be between 1 and 65535.");
        if (h.LoopbackHttpPort is < 0 or > 65535) errors.Add("Hcc:Hosting:LoopbackHttpPort must be between 0 and 65535.");
        if (h.LoopbackHttpPort != 0 && h.LoopbackHttpPort == h.HttpsPort) errors.Add("Hcc:Hosting:LoopbackHttpPort must differ from HttpsPort.");
        if (h.MaxRequestBodyMegabytes is < 1 or > 64) errors.Add("Hcc:Hosting:MaxRequestBodyMegabytes must be between 1 and 64.");
        ValidatePublicListener(configuration, h, errors);
        var hosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => x == "*"))
            errors.Add("AllowedHosts must list this server's names and addresses separated by ';' (for example hotel-pc;localhost;192.168.1.20;127.0.0.1), not \"*\". New-HccCertificate.ps1 -UpdateConfig writes it.");
        ValidateGuestIdKeys(configuration, errors);
        if (string.IsNullOrWhiteSpace(h.CertificateThumbprint) || !Thumbprint().IsMatch(h.CertificateThumbprint.Replace(" ", "", StringComparison.Ordinal)))
            errors.Add("Hcc:Hosting:CertificateThumbprint must be the 40-character thumbprint printed by New-HccCertificate.ps1.");
        return errors;
    }

    private static void ValidatePublicListener(IConfiguration configuration, HccHostingOptions h, List<string> errors)
    {
        if (h.PublicPort is < 0 or > 65535) errors.Add("Hcc:Hosting:PublicPort must be between 0 and 65535.");
        if (h.PublicPort != 0 && (h.PublicPort == h.HttpsPort || h.PublicPort == h.LoopbackHttpPort)) errors.Add("Hcc:Hosting:PublicPort must differ from HttpsPort and LoopbackHttpPort.");
        foreach (var p in h.KnownProxies ?? [])
            if (!IPAddress.TryParse(p?.Trim(), out _)) errors.Add($"Hcc:Hosting:KnownProxies: '{p}' is not an IP address.");
        foreach (var n in h.KnownNetworks ?? [])
            if (!System.Net.IPNetwork.TryParse(n?.Trim(), out _)) errors.Add($"Hcc:Hosting:KnownNetworks: '{n}' is not a network (for example 10.0.0.0/24).");
        if (h.PublicPort == 0 || !configuration.GetValue<bool>("Hcc:WebBooking:Enabled")) return;
        if (!IPAddress.TryParse(h.PublicBindAddress?.Trim(), out var bind))
        {
            errors.Add("Hcc:Hosting:PublicBindAddress must be an IP address (127.0.0.1 behind a reverse proxy on this PC).");
            return;
        }

        if (IPAddress.IsLoopback(bind) && (h.KnownProxies ?? []).Length == 0 && (h.KnownNetworks ?? []).Length == 0)
            errors.Add("Hcc:Hosting:PublicPort is bound to loopback for a reverse proxy, so Hcc:Hosting:KnownProxies must list the proxy's address (for example 127.0.0.1); otherwise every caller looks like one client and HTTPS cannot be confirmed.");
    }

    /// <summary>F-M05: in Production refuse plain keys, and machine-wide DPAPI unless explicitly allowed (with the settings file ACL checked at start-up).</summary>
    private static void ValidateGuestIdKeys(IConfiguration configuration, List<string> errors)
    {
        var g = configuration.GetSection("Hcc:GuestIdKeys");
        if (g.GetValue<bool>("AllowPlainKeys")) errors.Add("Hcc:GuestIdKeys:AllowPlainKeys is for development only; remove it in Production.");
        var current = g["Current"];
        var keys = g.GetSection("Keys").GetChildren().Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToList();
        if (string.IsNullOrWhiteSpace(current))
        {
            if (g.GetValue<bool>("Required")) errors.Add("Hcc:GuestIdKeys:Required is set but no guest ID key is configured (Hcc:GuestIdKeys:Current). Restore the key (INSTALL.md section 6b) before starting HCC; stored ID numbers cannot be opened without it.");
            return;
        }

        if (!keys.Any(k => k.Key == current)) errors.Add($"Hcc:GuestIdKeys:Keys:{current} (the current guest ID key) is missing.");
        var allowMachine = g.GetValue<bool>("AllowMachineScope");
        foreach (var k in keys)
        {
            var v = k.Value!.Trim();
            if (v.StartsWith("plain:", StringComparison.Ordinal)) errors.Add($"Guest ID key {k.Key} is a plain (unencrypted) key; Production accepts only DPAPI-wrapped keys (New-HccGuestIdKey.ps1).");
            else if (v.StartsWith("dpapi-machine:", StringComparison.Ordinal) && !allowMachine)
                errors.Add($"Guest ID key {k.Key} is wrapped for the whole PC (dpapi-machine:), which any program on this PC can open. Use a dpapi-user: key of the service account, or set Hcc:GuestIdKeys:AllowMachineScope=true only with appsettings.Production.json limited to SYSTEM, Administrators and the service account (New-HccGuestIdKey.ps1 does both; the server checks the file at start-up).");
            else if (!v.StartsWith("dpapi-user:", StringComparison.Ordinal) && !v.StartsWith("dpapi-machine:", StringComparison.Ordinal))
                errors.Add($"Guest ID key {k.Key} must start with dpapi-user: or dpapi-machine:.");
        }
    }

    /// <summary>Throws one <see cref="InvalidOperationException"/> listing every problem; used at start-up.</summary>
    public static void EnsureValid(IConfiguration configuration, bool production)
    {
        var errors = Validate(configuration, production);
        if (errors.Count > 0)
            throw new InvalidOperationException("HCC configuration is not valid:" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", errors));
    }

    public static bool IsLocalDataSource(string dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource)) return false;
        var host = dataSource.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        host = host.Split('\\')[0].Split(',')[0].Trim();
        return host is "." or "(local)" or "localhost" or "127.0.0.1" or "::1"
            || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Kestrel binding and the Windows-service setup (called from Program.cs).</summary>
public static class HccHostingExtensions
{
    /// <summary>Runs as a Windows service when started by the service manager (no effect on a console run), validates the
    /// configuration, and binds LAN HTTPS with the machine-store certificate when a thumbprint is configured.</summary>
    public static WebApplicationBuilder UseHccHosting(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Host.UseWindowsService(o => o.ServiceName = "HccServer");
        var production = builder.Environment.IsProduction();
        HccConfigValidator.EnsureValid(builder.Configuration, production);
        if (production && OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(builder.Configuration["Hcc:GuestIdKeys:Current"]))
            GuestIdKeyFileCheck.EnsureProtected(Path.Combine(builder.Environment.ContentRootPath, "appsettings.Production.json"));

        var h = builder.Configuration.GetSection(HccHostingOptions.Section).Get<HccHostingOptions>() ?? new HccHostingOptions();
        var map = new HccListeners.Map();
        builder.Services.AddSingleton(map);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = Math.Clamp(h.MaxRequestBodyMegabytes, 1, 64) * 1024L * 1024L;
            k.Limits.MaxRequestHeaderCount = 64;
        });
        if (string.IsNullOrWhiteSpace(h.CertificateThumbprint)) return builder; // development: default Urls / launch settings
        var cert = FindCertificate(h.CertificateThumbprint);
        var webBooking = builder.Configuration.GetValue<bool>("Hcc:WebBooking:Enabled");
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.ListenAnyIP(h.HttpsPort, o => o.MarkAs(HccListenerKind.Lan).UseHttps(cert));
            map.Add(h.HttpsPort, HccListenerKind.Lan);
            if (h.LoopbackHttpPort != 0)
            {
                k.Listen(IPAddress.Loopback, h.LoopbackHttpPort, o => o.MarkAs(HccListenerKind.Setup));
                map.Add(h.LoopbackHttpPort, HccListenerKind.Setup);
            }

            // F-H04: the public booking listener exists only while web booking is switched on.
            if (webBooking && h.PublicPort != 0)
            {
                var bind = IPAddress.Parse(h.PublicBindAddress!.Trim());
                k.Listen(bind, h.PublicPort, o =>
                {
                    o.MarkAs(HccListenerKind.Public);
                    if (!IPAddress.IsLoopback(bind)) o.UseHttps(cert);
                });
                map.Add(h.PublicPort, HccListenerKind.Public);
            }
        });
        return builder;
    }

    public static X509Certificate2 FindCertificate(string thumbprint)
    {
        var clean = thumbprint.Replace(" ", "", StringComparison.Ordinal);
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, clean, validOnly: false);
        if (found.Count == 0 || !found[0].HasPrivateKey)
            throw new InvalidOperationException($"Certificate {clean} was not found (with its private key) in LocalMachine\\My, or the service account cannot read its key. Run deployment\\windows\\New-HccCertificate.ps1.");
        return found[0];
    }

    /// <summary>GET /api/v1/system/discovery - no authentication, no secrets: lets the app confirm it is talking to an HCC server.</summary>
    public static RouteGroupBuilder MapHccDiscovery(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet("/system/discovery", (IConfiguration configuration) => Results.Ok(DiscoveryInfo.Create(configuration)));
        return api;
    }
}

/// <summary>GET /api/v1/system/discovery result.</summary>
public sealed record DiscoveryResult(string Product, string ServerName, string Version, string ApiVersion, string ApiBasePath);

public static class DiscoveryInfo
{
    public static DiscoveryResult Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var name = configuration[HccHostingOptions.Section + ":ServerName"];
        var info = typeof(DiscoveryInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return new DiscoveryResult("Hotel Control Centre", string.IsNullOrWhiteSpace(name) ? Environment.MachineName : name.Trim(), plus >= 0 ? info[..plus] : info, "v1", Hcc.Contracts.ApiConventions.BasePath);
    }
}
