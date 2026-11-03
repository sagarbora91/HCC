// F7-R1 · report module endpoints (F7-CONTRACT §3). Every route is signed in; permissions are the engine's (rule V / M), not the route's.
//   GET  /reports/catalogue                    reports the caller may view (+ canExport, money)
//   GET  /reports/{id}?from&to&…&page&pageSize JSON ReportResult (paged, masked)
//   GET  /reports/{id}/export?format=xlsx|pdf|csv&…   the file (view + export permission); one rpt.export_log row
//   POST /reports/{id}/print-log               a print from the screen: one rpt.export_log row (view permission)
//   GET|POST /reports/schedules · PUT|DELETE /reports/schedules/{sid} · GET /reports/runs (PERM-RPT-004)
//   POST /reports/favourites/{id}              toggle · GET /reports/favourites
using Hcc.Reporting;
using Hcc.Reporting.Sql;

namespace Hcc.Api.Endpoints;

public static class ReportEndpoints
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "page", "pageSize", "format" };

    private static Dictionary<string, string?> Raw(HttpContext http) =>
        http.Request.Query.Where(q => !Reserved.Contains(q.Key)).ToDictionary(q => q.Key, q => (string?)q.Value.ToString(), StringComparer.Ordinal);

    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/reports/catalogue", async (HttpContext http, ReportService s, CancellationToken ct) =>
            Results.Ok((await s.CatalogueAsync(http.HccCaller(), ct).ConfigureAwait(false)).Select(e => new { e.Definition, e.CanExport, e.Money })));
        g.MapGet("/reports/favourites", async (HttpContext http, ReportService s, CancellationToken ct) => Results.Ok(await s.FavouritesAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapPost("/reports/favourites/{id}", async (HttpContext http, ReportService s, string id, CancellationToken ct) =>
            Results.Ok(new { favourite = await s.ToggleFavouriteAsync(http.HccCaller(), id, ct).ConfigureAwait(false) }));
        g.MapGet("/reports/schedules", async (HttpContext http, ReportService s, CancellationToken ct) => Results.Ok(await s.SchedulesAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapPost("/reports/schedules", async (HttpContext http, ReportService s, ScheduleRequest body, CancellationToken ct) =>
            Results.Ok(new { id = await s.SaveScheduleAsync(http.HccCaller(), null, body, ct).ConfigureAwait(false) }));
        g.MapPut("/reports/schedules/{sid:long}", async (HttpContext http, ReportService s, long sid, ScheduleRequest body, CancellationToken ct) =>
            Results.Ok(new { id = await s.SaveScheduleAsync(http.HccCaller(), sid, body, ct).ConfigureAwait(false) }));
        g.MapDelete("/reports/schedules/{sid:long}", async (HttpContext http, ReportService s, long sid, CancellationToken ct) =>
        {
            await s.DeleteScheduleAsync(http.HccCaller(), sid, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        g.MapGet("/reports/runs", async (HttpContext http, ReportService s, int? take, CancellationToken ct) => Results.Ok(await s.RunsAsync(http.HccCaller(), take ?? 100, ct).ConfigureAwait(false)));
        g.MapGet("/reports/{id}", async (HttpContext http, ReportService s, string id, int? page, int? pageSize, CancellationToken ct) =>
            Results.Ok(await s.RunAsync(http.HccCaller(), id, Raw(http), page, pageSize ?? ReportRunner.PageDesktop, ct).ConfigureAwait(false)));
        g.MapGet("/reports/{id}/export", async (HttpContext http, ReportService s, string id, string? format, CancellationToken ct) =>
        {
            var f = await s.ExportAsync(http.HccCaller(), id, format, Raw(http), ct).ConfigureAwait(false);
            return Results.File(f.Bytes, f.ContentType, f.FileName);
        });
        g.MapPost("/reports/{id}/print-log", async (HttpContext http, ReportService s, string id, PrintLogRequest body, CancellationToken ct) =>
            Results.Ok(new { id = await s.PrintLogAsync(http.HccCaller(), id, body, ct).ConfigureAwait(false) }));
        return api;
    }
}

/// <summary>Runs due report schedules once a minute (the 2-second outbox poll is throttled here); delivery is held by the egress gate while net.* is OFF.</summary>
public sealed class ReportScheduleWorker(ReportService service, SqlReportStores stores, TimeProvider clock, ILogger<ReportScheduleWorker> logger) : OutboxPollingWorker(logger, "Scheduled reports")
{
    private long _lastMinute = -1;

    protected override async Task PollAsync(CancellationToken ct)
    {
        var minute = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
        if (minute == _lastMinute) return;
        _lastMinute = minute;
        foreach (var p in await stores.PropertyIdsAsync(ct).ConfigureAwait(false)) await service.RunDueAsync(p, ct).ConfigureAwait(false);
    }
}
