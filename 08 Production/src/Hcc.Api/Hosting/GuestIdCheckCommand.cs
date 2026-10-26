using System.Text.RegularExpressions;
using Hcc.Api.Endpoints;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.GuestIds;
using Microsoft.Data.SqlClient;

namespace Hcc.Api.Hosting;

/// <summary>
/// Hidden maintenance verb (A07-03 / A09-07): <c>Hcc.Api.exe verify-guest-id [--database &lt;name&gt;] [--server &lt;instance&gt;]</c>.
/// Opens the newest sealed guest ID number per key id in the database (default: the configured one; the restore drill passes its
/// scratch copy) with the guest ID key configured on this PC, loaded exactly as the server loads it, and prints ONE line:
/// <c>Guest ID check: OK (1 ID opened)</c>, <c>Guest ID check: no guest IDs to check ...</c> or <c>Guest ID check: FAILED - ...</c>.
/// Never prints a number. Read-only; connects with the configured connection string (Windows login of the caller). Exit code 0 = OK or
/// nothing to check, 1 = failed, 2 = wrong arguments. Not part of the web API; it runs before the web host is built.
/// </summary>
public static partial class GuestIdCheckCommand
{
    public const string Verb = "verify-guest-id";

    [GeneratedRegex("^[A-Za-z0-9_]{1,128}$")]
    private static partial Regex DatabaseName();

    public static bool IsVerb(string[] args) => args is { Length: > 0 } && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(output);
        string? database = null, server = null;
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (string.Equals(a, "--database", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) database = args[++i];
            else if (string.Equals(a, "--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) server = args[++i];
            else if ((string.Equals(a, "--environment", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--contentRoot", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length) i++; // host options, read by WebApplication.CreateBuilder
            else return await UsageAsync(output, $"unknown argument '{a}'").ConfigureAwait(false);
        }

        if (database is not null && !DatabaseName().IsMatch(database)) return await UsageAsync(output, "--database must be a plain database name (letters, digits, '_')").ConfigureAwait(false);
        if (server is not null && string.IsNullOrWhiteSpace(server)) return await UsageAsync(output, "--server needs an instance name, for example .\\SQLEXPRESS").ConfigureAwait(false);
        var cs = configuration["Hcc:Sql:ConnectionString"];
        if (string.IsNullOrWhiteSpace(cs))
        {
            await output.WriteLineAsync(GuestIdRestoreCheck.Prefix + "FAILED - Hcc:Sql:ConnectionString is not configured (run it in the server folder, where appsettings.Production.json is).").ConfigureAwait(false);
            return 1;
        }

        SqlConnectionStringBuilder b;
        try
        {
            b = new SqlConnectionStringBuilder(cs);
        }
        catch (ArgumentException)
        {
            await output.WriteLineAsync(GuestIdRestoreCheck.Prefix + "FAILED - Hcc:Sql:ConnectionString is not a valid connection string.").ConfigureAwait(false);
            return 1;
        }

        if (database is not null) b.InitialCatalog = database;
        if (server is not null) b.DataSource = server.Trim();
        b.ApplicationName = "HCC guest ID check";
        var result = await GuestIdRestoreCheck.RunAsync(new SqlConnectionFactory(new SqlOptions { ConnectionString = b.ConnectionString }),
            () => GuestIdEndpoints.KeyRing(configuration), ct).ConfigureAwait(false);
        await output.WriteLineAsync(result.Message).ConfigureAwait(false);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> UsageAsync(TextWriter output, string why)
    {
        await output.WriteLineAsync(GuestIdRestoreCheck.Prefix + "FAILED - " + why + ". Usage: Hcc.Api.exe " + Verb + " [--database <name>] [--server <instance>]").ConfigureAwait(false);
        return 2;
    }
}
