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

    /// <summary>Plain-HTTP port bound to 127.0.0.1 ONLY (first-Owner bootstrap and local checks from the server PC). 0 = none.</summary>
    public int LoopbackHttpPort { get; set; } = 5080;

    /// <summary>SHA-1 thumbprint (40 hex) of the server certificate in LocalMachine\My (created by New-HccCertificate.ps1).</summary>
    public string? CertificateThumbprint { get; set; }
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
        if (string.IsNullOrWhiteSpace(h.CertificateThumbprint) || !Thumbprint().IsMatch(h.CertificateThumbprint.Replace(" ", "", StringComparison.Ordinal)))
            errors.Add("Hcc:Hosting:CertificateThumbprint must be the 40-character thumbprint printed by New-HccCertificate.ps1.");
        return errors;
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
        HccConfigValidator.EnsureValid(builder.Configuration, builder.Environment.IsProduction());

        var h = builder.Configuration.GetSection(HccHostingOptions.Section).Get<HccHostingOptions>() ?? new HccHostingOptions();
        if (string.IsNullOrWhiteSpace(h.CertificateThumbprint)) return builder; // development: default Urls / launch settings
        var cert = FindCertificate(h.CertificateThumbprint);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.ListenAnyIP(h.HttpsPort, o => o.UseHttps(cert));
            if (h.LoopbackHttpPort != 0) k.Listen(IPAddress.Loopback, h.LoopbackHttpPort);
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
