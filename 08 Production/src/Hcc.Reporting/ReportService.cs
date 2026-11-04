// F7-R1 · what the endpoints call: catalogue, run, export (+ log row), print log, favourites, schedules CRUD, runs, and the scheduled-run tick.
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.Egress;

namespace Hcc.Reporting;

public interface IReportFavourites
{
    Task<bool> ToggleAsync(int propertyId, int principalId, string reportId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> FavouritesAsync(int propertyId, int principalId, CancellationToken ct = default);

    /// <summary>AF-FIX2 F6: set (not flip) the favourite, so a retried request after a lost answer gives the same state. Returns the state now stored.
    /// The default reads then toggles only when it differs; the SQL store overrides it with one idempotent statement.</summary>
    async Task<bool> SetAsync(int propertyId, int principalId, string reportId, bool favourite, CancellationToken ct = default)
    {
        var now = (await FavouritesAsync(propertyId, principalId, ct).ConfigureAwait(false)).Contains(reportId, StringComparer.Ordinal);
        return now == favourite ? now : await ToggleAsync(propertyId, principalId, reportId, ct).ConfigureAwait(false);
    }
}

public interface IReportPrincipalRoles
{
    Task<IReadOnlyList<string>> RolesAsync(int principalId, CancellationToken ct = default);
}

/// <summary>Where a scheduled run's file is kept (LF documents store by reference; the default keeps nothing and returns the reference).</summary>
public interface IReportFileStore
{
    Task<string> PutAsync(int propertyId, long runId, RenderedFile file, CancellationToken ct = default);
}

public sealed class ReferenceOnlyFileStore : IReportFileStore
{
    public Task<string> PutAsync(int propertyId, long runId, RenderedFile file, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Task.FromResult(string.Create(CultureInfo.InvariantCulture, $"rpt/{runId}/{file.FileName}"));
    }
}

/// <summary>Hands a finished run to the outbound queue (F5 egress outbox, queue scheduled-report). Returns true when it was held (internet OFF).</summary>
public interface IReportDelivery
{
    Task<bool> EnqueueAsync(int propertyId, long runId, IReadOnlyList<int> recipients, string fileRef, CancellationToken ct = default);
}

public sealed class EgressReportDelivery(EgressOutbox outbox, IEgressPolicy policy, TimeProvider clock) : IReportDelivery
{
    public async Task<bool> EnqueueAsync(int propertyId, long runId, IReadOnlyList<int> recipients, string fileRef, CancellationToken ct = default)
    {
        if (policy is IEgressSettingsCache cache) await cache.RefreshAsync(ct).ConfigureAwait(false);
        var held = !policy.Evaluate(EgressIntegration.Email, clock.GetUtcNow()).Allowed;
        await outbox.EnqueueAsync("scheduled-report", EgressIntegration.Email, runId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { propertyId, runId, recipients, fileRef }), ct).ConfigureAwait(false);
        return held;
    }
}

public sealed record ScheduleRequest(string ReportId, IReadOnlyDictionary<string, string>? Params, string Format, string Cadence, int? Day, string Time, IReadOnlyList<int> Recipients, bool Enabled = true);

public sealed record PrintLogRequest(IReadOnlyDictionary<string, string>? Params, int Rows, bool Provisional = false, string? Format = null);

public sealed class ReportService(ReportRunner runner, IReportCatalogue catalogue, IEnumerable<IReportRenderer> renderers, IReportPropertyInfo property,
    IReportExportLog exportLog, IReportScheduleStore schedules, IReportRunStore runs, IReportFavourites favourites, IReportPrincipalRoles roles,
    IAccessPolicy policy, IReportFileStore files, IReportDelivery delivery, TimeProvider clock)
{
    public const string SchedulePerm = "PERM-RPT-004";
    public static readonly IReadOnlySet<string> MoneyRecipientRoles = new HashSet<string>(StringComparer.Ordinal) { "OWN", "MGR", "ACC" };
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private readonly Dictionary<ReportFormat, IReportRenderer> _renderers = renderers.ToDictionary(r => r.Format);

    public Task<IReadOnlyList<ReportCatalogueEntry>> CatalogueAsync(Caller c, CancellationToken ct = default) => runner.CatalogueAsync(c, ct);

    public Task<ReportResult> RunAsync(Caller c, string id, IReadOnlyDictionary<string, string?> raw, int? page, int pageSize, CancellationToken ct = default) =>
        runner.RunAsync(c, id, raw, new RunOptions(page ?? 0, Math.Clamp(pageSize, 1, ReportRunner.PageDesktop)), ct);

    public static ReportFormat ParseFormat(string? f) => f?.ToLowerInvariant() switch
    {
        "xlsx" => ReportFormat.Xlsx, "pdf" => ReportFormat.Pdf, "csv" => ReportFormat.Csv,
        _ => throw HccException.Validation("format must be xlsx, pdf or csv."),
    };

    /// <summary>Runs the report for a file (rule V: view + export permission), renders it and writes the export log row.</summary>
    public async Task<RenderedFile> ExportAsync(Caller c, string id, string? format, IReadOnlyDictionary<string, string?> raw, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        var f = ParseFormat(format);
        var result = await runner.RunAsync(c, id, raw, new RunOptions(All: true, Export: true, ExportFormat: f.ToString()), ct).ConfigureAwait(false);
        var prop = await property.GetAsync(c.PropertyId, ct).ConfigureAwait(false);
        var file = _renderers[f].Render(result, new RenderContext(prop.Code, prop.Name, f));
        await exportLog.AppendAsync(c.PropertyId, new ReportExportLogRecord(0, clock.GetUtcNow(), c.PrincipalId, id, JsonSerializer.Serialize(result.Params.Values), f,
            result.TotalRows, file.FileName, result.About.Provisional), ct).ConfigureAwait(false);
        return file;
    }

    /// <summary>A print from the screen (rule V: view is enough): one log row.</summary>
    public async Task<long> PrintLogAsync(Caller c, string id, PrintLogRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(req);
        var def = (catalogue.Find(id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"No report {id}.")).Definition;
        if (!await runner.CanViewAsync(c, def, ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.FORBIDDEN, $"You cannot print {def.Title}.");
        if (req.Rows < 0) throw HccException.Validation("Row count.");
        return await exportLog.AppendAsync(c.PropertyId, new ReportExportLogRecord(0, clock.GetUtcNow(), c.PrincipalId, id, JsonSerializer.Serialize(req.Params ?? new Dictionary<string, string>()),
            PrintLogFormat(req.Format), req.Rows, null, req.Provisional), ct).ConfigureAwait(false);
    }

    /// <summary>AF-INT (EX-01): the format the device reports for a print-log row. Blank or "print" = Print; xlsx / csv / pdf as the export route parses them.</summary>
    private static ReportFormat PrintLogFormat(string? format) =>
        string.IsNullOrWhiteSpace(format) || string.Equals(format.Trim(), "print", StringComparison.OrdinalIgnoreCase) ? ReportFormat.Print : ParseFormat(format.Trim());

    public Task<bool> ToggleFavouriteAsync(Caller c, string id, CancellationToken ct = default) => SetFavouriteAsync(c, id, null, ct);

    /// <summary>Toggle when <paramref name="wanted"/> is null (older clients); otherwise set it (AF-FIX2 F6: idempotent on a retry).</summary>
    public async Task<bool> SetFavouriteAsync(Caller c, string id, bool? wanted, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        var def = (catalogue.Find(id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"No report {id}.")).Definition;
        if (!await runner.CanViewAsync(c, def, ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot open this report.");
        return wanted is bool w
            ? await favourites.SetAsync(c.PropertyId, c.PrincipalId, id, w, ct).ConfigureAwait(false)
            : await favourites.ToggleAsync(c.PropertyId, c.PrincipalId, id, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> FavouritesAsync(Caller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        return favourites.FavouritesAsync(c.PropertyId, c.PrincipalId, ct);
    }

    /// <summary>Own schedules (all for the Owner role).</summary>
    public async Task<IReadOnlyList<ReportScheduleRecord>> SchedulesAsync(Caller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await policy.RequireAsync(c.PropertyId, c.PrincipalId, SchedulePerm, ct: ct).ConfigureAwait(false);
        var all = await schedules.ListAsync(c.PropertyId, ct).ConfigureAwait(false);
        var owner = (await roles.RolesAsync(c.PrincipalId, ct).ConfigureAwait(false)).Contains("OWN");
        return owner ? all : all.Where(s => s.OwnerPrincipalId == c.PrincipalId).ToList();
    }

    public async Task<long> SaveScheduleAsync(Caller c, long? id, ScheduleRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(c.PropertyId, c.PrincipalId, SchedulePerm, ct: ct).ConfigureAwait(false);
        var def = (catalogue.Find(req.ReportId) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"No report {req.ReportId}.")).Definition;
        if (!await runner.CanExportAsync(c, def, ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.FORBIDDEN, "A schedule sends a file: you need to view and export this report.");
        var f = ParseFormat(req.Format);
        if (!def.Formats.Contains(f)) throw HccException.Validation("Choose a file format this report offers.");
        if (req.Cadence is not ("daily" or "weekly" or "monthly") || !TimeOnly.TryParseExact(req.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw HccException.Validation("Cadence: daily, weekly or monthly at HH:mm.");
        if (req.Cadence == "weekly" && req.Day is not (>= 0 and <= 6)) throw HccException.Validation("Weekly: choose a day (0 = Sunday).");
        if (req.Cadence == "monthly" && req.Day is not (>= 1 and <= 28)) throw HccException.Validation("Monthly: choose a day from 1 to 28.");
        if (req.Recipients is null || req.Recipients.Count is < 1 or > 10) throw HccException.Validation("Choose 1–10 recipients.");
        foreach (var r in req.Recipients)
        {
            var rr = await roles.RolesAsync(r, ct).ConfigureAwait(false);
            if (rr.Count == 0) throw HccException.Validation($"Recipient {r} is not an active user.");
            if (def.MoneyFlag && !rr.Any(MoneyRecipientRoles.Contains))
                throw new HccException(HccErrorCode.FORBIDDEN, "A money report goes only to the Owner, Manager or Accountant.");
        }

        ReportScheduleRecord? existing = null;
        if (id is { } sid)
        {
            existing = (await schedules.ListAsync(c.PropertyId, ct).ConfigureAwait(false)).FirstOrDefault(s => s.Id == sid) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Schedule not found.");
            if (existing.OwnerPrincipalId != c.PrincipalId && !(await roles.RolesAsync(c.PrincipalId, ct).ConfigureAwait(false)).Contains("OWN"))
                throw new HccException(HccErrorCode.FORBIDDEN, "Only its owner or the Owner changes a schedule.");
        }

        return await schedules.SaveAsync(c.PropertyId, new ReportScheduleRecord(id ?? 0, def.Id, req.Params ?? new Dictionary<string, string>(), f, req.Cadence,
            req.Cadence == "daily" ? null : req.Day, time, existing?.OwnerPrincipalId ?? c.PrincipalId, req.Recipients, req.Enabled, existing?.LastRun), ct).ConfigureAwait(false);
    }

    public async Task DeleteScheduleAsync(Caller c, long id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await policy.RequireAsync(c.PropertyId, c.PrincipalId, SchedulePerm, ct: ct).ConfigureAwait(false);
        var s = (await schedules.ListAsync(c.PropertyId, ct).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Schedule not found.");
        if (s.OwnerPrincipalId != c.PrincipalId && !(await roles.RolesAsync(c.PrincipalId, ct).ConfigureAwait(false)).Contains("OWN"))
            throw new HccException(HccErrorCode.FORBIDDEN, "Only its owner or the Owner deletes a schedule.");
        await schedules.DeleteAsync(c.PropertyId, id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReportRunRecord>> RunsAsync(Caller c, int take, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await policy.RequireAsync(c.PropertyId, c.PrincipalId, SchedulePerm, ct: ct).ConfigureAwait(false);
        return await runs.ListAsync(c.PropertyId, take, ct).ConfigureAwait(false);
    }

    /// <summary>Due on India date <paramref name="d"/> at time <paramref name="t"/>; at most once per India date.</summary>
    public static bool Due(ReportScheduleRecord s, DateOnly d, TimeOnly t)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!s.Enabled || t < s.Time) return false;
        if (s.LastRun is { } lr && DateOnly.FromDateTime(lr.ToOffset(Ist).DateTime) >= d) return false;
        return s.Cadence switch { "daily" => true, "weekly" => (int)d.DayOfWeek == s.Day, "monthly" => d.Day == s.Day, _ => false };
    }

    /// <summary>Relative tokens (range = yesterday | month-to-date; values today | yesterday | month-start) → dates on the business date.</summary>
    public static Dictionary<string, string?> ResolveTokens(IReadOnlyDictionary<string, string> p, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(p);
        static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["today"] = Iso(today), ["yesterday"] = Iso(today.AddDays(-1)), ["month-start"] = Iso(new DateOnly(today.Year, today.Month, 1)) };
        var o = p.ToDictionary(kv => kv.Key, kv => (string?)kv.Value, StringComparer.Ordinal);
        if (o.Remove("range", out var range))
        {
            if (range == "yesterday") o["from"] = o["to"] = map["yesterday"];
            else if (range == "month-to-date") { o["from"] = map["month-start"]; o["to"] = map["today"]; }
        }

        foreach (var k in o.Keys.ToList()) if (o[k] is { } v && map.TryGetValue(v, out var m)) o[k] = m;
        return o;
    }

    /// <summary>The outbox tick for scheduled runs: each due schedule runs as its owner (never wider), the file is stored by reference and
    /// delivery is queued on the egress outbox (held while the internet is off). Returns (ok, held, failed).</summary>
    public async Task<(int Ok, int Held, int Failed)> RunDueAsync(int propertyId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var local = now.ToOffset(Ist);
        var d = DateOnly.FromDateTime(local.DateTime);
        var t = TimeOnly.FromDateTime(local.DateTime);
        int ok = 0, held = 0, failed = 0;
        var prop = await property.GetAsync(propertyId, ct).ConfigureAwait(false);
        foreach (var s in (await schedules.ListAsync(propertyId, ct).ConfigureAwait(false)).Where(x => Due(x, d, t)))
        {
            var p = ResolveTokens(s.Params, prop.BusinessDate);
            var runId = await runs.StartAsync(propertyId, new ReportRunRecord(0, s.Id, s.ReportId, JsonSerializer.Serialize(p), s.OwnerPrincipalId, now, null, "running"), ct).ConfigureAwait(false);
            try
            {
                var caller = new Caller(propertyId, s.OwnerPrincipalId);
                var result = await runner.RunAsync(caller, s.ReportId, p, new RunOptions(All: true, Export: true, ExportFormat: s.Format.ToString()), ct).ConfigureAwait(false);
                var file = _renderers[s.Format].Render(result, new RenderContext(prop.Code, prop.Name, s.Format));
                var fileRef = await files.PutAsync(propertyId, runId, file, ct).ConfigureAwait(false);
                var h = await delivery.EnqueueAsync(propertyId, runId, s.Recipients, fileRef, ct).ConfigureAwait(false);
                await runs.FinishAsync(propertyId, runId, h ? "held" : "ok", result.TotalRows, fileRef, null, ct).ConfigureAwait(false);
                if (h) held++; else ok++;
            }
            catch (HccException ex)
            {
                await runs.FinishAsync(propertyId, runId, "failed", null, null, ex.Message, ct).ConfigureAwait(false);
                failed++;
            }

            await schedules.SaveAsync(propertyId, s with { LastRun = now }, ct).ConfigureAwait(false);
        }

        return (ok, held, failed);
    }
}
