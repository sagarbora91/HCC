using System.Text.Json;
using Hcc.Api.Endpoints;
using Hcc.Contracts;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Api.Hosting;

/// <summary>
/// A09-04: the server refuses to serve while the database schema is behind this build (an upgrade started the service before
/// <c>Hcc.Api.exe migrate</c> ran). The journal is read once and the "current" answer is kept for the life of the process, so a
/// healthy server makes no per-request query. While behind (or while the database cannot be reached) it is read again at most every
/// <see cref="RecheckInterval"/>, so the server recovers by itself once <c>migrate</c> has run. A failed check never blocks:
/// requests then fail or succeed exactly as they would without the guard.
/// </summary>
public sealed partial class SchemaGuard(SqlConnectionFactory connections, IMigrationSource source, TimeProvider clock, ILogger<SchemaGuard> logger)
{
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>The message for callers other than the server PC (F-L01: no migration counts on the LAN).</summary>
    public const string PublicMessage = "Database schema is behind this build: run `Hcc.Api migrate` on the server PC.";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MigrationScript>? _scripts;
    private volatile bool _current;
    private volatile SchemaStatus? _behind;
    private long _checkedTicks = -1;

    /// <summary>The status when the schema is behind; null when it is current or could not be read.</summary>
    public async ValueTask<SchemaStatus?> BehindAsync(CancellationToken ct)
    {
        if (_current) return null;
        if (!Due()) return _behind;
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return _behind;   // another request is checking: never queue behind it
        try
        {
            if (_current || !Due()) return _behind;
            try
            {
                _scripts ??= source.Load();
                await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
                var status = await MigrationRunner.StatusAsync(conn, _scripts, ct).ConfigureAwait(false);
                if (!status.IsBehind)
                {
                    _behind = null;
                    _current = true;
                    return null;
                }

                if (_behind?.AppliedCount != status.AppliedCount)
                    LogBehind(logger, status.AppliedCount, status.BuildCount, status.DatabaseVersion, status.BuildVersion);
                _behind = status;
            }
            catch (SqlException ex)
            {
                _behind = null;
                LogCheckFailed(logger, ex.Number);
            }

            Interlocked.Exchange(ref _checkedTicks, clock.GetUtcNow().UtcTicks);
            return _behind;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the last check so the next request reads the journal again (tests; an operator tool after migrate).</summary>
    public void Invalidate()
    {
        _current = false;
        Interlocked.Exchange(ref _checkedTicks, -1);
    }

    private bool Due()
    {
        var at = Interlocked.Read(ref _checkedTicks);
        return at < 0 || clock.GetUtcNow().UtcTicks - at >= RecheckInterval.Ticks;
    }

    /// <summary>503 UNAVAILABLE with <c>details.reason = "SCHEMA_BEHIND"</c>; the server PC also gets the migration counts.</summary>
    public static IResult Unavailable(HttpContext http, SchemaStatus status)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(status);
        http.Response.Headers.RetryAfter = ((int)RecheckInterval.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var details = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["reason"] = JsonSerializer.SerializeToElement("SCHEMA_BEHIND") };
        var serverPc = AccessEndpoints.IsServerPc(http);
        if (serverPc)
        {
            details["appliedMigrations"] = JsonSerializer.SerializeToElement(status.AppliedCount);
            details["buildMigrations"] = JsonSerializer.SerializeToElement(status.BuildCount);
        }

        return Results.Json(new ErrorResponse(nameof(HccErrorCode.UNAVAILABLE), serverPc ? status.Message : PublicMessage, details),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "HCC database schema is behind this build: applied {Applied} of {Build} migrations (database version {DatabaseVersion}, build version {BuildVersion}). Run Hcc.Api.exe migrate (INSTALL.md step 5); until then the API answers 503.")]
    private static partial void LogBehind(ILogger logger, int applied, int build, int databaseVersion, int buildVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "HCC schema check could not read the database (SQL error {SqlNumber}); requests are served unchecked and the check is retried.")]
    private static partial void LogCheckFailed(ILogger logger, int sqlNumber);
}

public static class SchemaGuardExtensions
{
    /// <summary>A09-04: every <c>/api/v1</c> route answers 503 while the schema is behind, except health (which reports it itself)
    /// and discovery (no database; lets the app confirm it found HCC).</summary>
    public static WebApplication UseHccSchemaGuard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var guard = app.Services.GetRequiredService<SchemaGuard>();
        app.Use(async (http, next) =>
        {
            var path = http.Request.Path;
            if (path.StartsWithSegments(ApiConventions.BasePath, StringComparison.OrdinalIgnoreCase, out var rest)
                && !rest.StartsWithSegments("/system/health", StringComparison.OrdinalIgnoreCase)
                && !rest.StartsWithSegments("/system/discovery", StringComparison.OrdinalIgnoreCase)
                && await guard.BehindAsync(http.RequestAborted).ConfigureAwait(false) is { } behind)
            {
                await SchemaGuard.Unavailable(http, behind).ExecuteAsync(http).ConfigureAwait(false);
                return;
            }

            await next(http).ConfigureAwait(false);
        });
        return app;
    }
}
