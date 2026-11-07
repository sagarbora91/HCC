// F12 MEAS · meas-study-pack · rpt.study.management-pack (catalogue 6.22), server twin of 03 Prototype/src/domain/reports/study-pack/management-pack.ts.
// Composes the study reports BY ID through the ReportRunner as the caller (their own MEAS switches, rule V / M and masks apply); an id that is not
// registered, switched off or not visible is listed in the contents with the reason. Executive summary: PackRules over IStudyPackFacts (room
// lines by night date, rooms in service) with the contract §9.2 definitions, the same arithmetic as the prototype packSummary.
// Schedule: meas.study.packSchedule (off / monthly / quarterly) → StudyPackSchedule keeps the F7 schedules of this report enabled only when due;
// the F7 worker (ReportService.RunDueAsync) then runs them.
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Charts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Reporting.Reports.StudyPack;

/// <summary>One posted room-night line (folio room line): room, night date, amount before GST (paise).</summary>
public sealed record RoomNightFact(string Room, DateOnly Night, long AmountPaise);

/// <summary>Facts for the executive summary (integration: a SQL implementation over the folio room lines; see the lane's DONE file).</summary>
public interface IStudyPackFacts
{
    Task<int> RoomsInServiceAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<RoomNightFact>> RoomNightsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record PackMonth(string Month, string Label, int RoomNights, int ChargedNights, long RoomRevenue, long Capacity, double Occupancy, long Adr, long RevPar);

public static class PackRules
{
    public const string Id = "rpt.study.management-pack";
    public const string Key = "meas.study.management-pack";
    public const string Footer = "Legacy to 30 Oct 2026 / HCC live";

    public static readonly IReadOnlyList<(int No, string Title, string[] Ids)> Chapters =
    [
        (1, "Revenue and cash", ["rpt.study.revenue-monthly", "rpt.study.year-comparison", "rpt.study.index-vs-base", "rpt.study.fy-deep-dive"]),
        (2, "Occupancy and operations", ["rpt.study.occupancy-history", "rpt.study.operations-patterns", "rpt.study.rooms-and-rates", "rpt.study.zero-rate-complimentary"]),
        (3, "Receivables", ["rpt.study.receivables-bad-debt"]),
        (4, "Revenue management", ["rpt.study.revenue-kpi", "rpt.study.channel-economics", "rpt.study.pricing-shape"]),
        (5, "Guests", ["rpt.study.guest-repeat-value", "rpt.study.guest-origin-plan"]),
        (6, "Housekeeping, F&B and halls", ["rpt.study.housekeeping-operations", "rpt.study.fnb-halls-ancillary"]),
        (7, "Finance, sales and compliance", ["rpt.study.finance-cost", "rpt.study.corporate-accounts", "rpt.study.compliance-risk"]),
        (8, "Forecast and Owner dashboard", ["rpt.study.forecast-scenarios", "rpt.study.owner-dashboard"]),
    ];

    public static IReadOnlyList<string> StudyIds => Chapters.SelectMany(c => c.Ids).ToList();

    private static ReportColumn T(string k, string l) => new(k, l, ReportColumnType.Text, false);

    private static ReportColumn I(string k, string l) => new(k, l, ReportColumnType.Int, false);

    private static ReportColumn P(string k, string l) => new(k, l, ReportColumnType.Percent, false);

    private static ReportColumn M(string k, string l) => new(k, l, ReportColumnType.Money, true);

    public static readonly IReadOnlyList<ReportColumn> ContentsCols = [I("no", "No."), T("chapter", "Chapter"), T("report", "Report id"), T("title", "Report"), T("status", "Status"), I("blocks", "Blocks")];
    public static readonly IReadOnlyList<ReportColumn> SummaryCols = [T("fy", "FY"), T("period", "Period"), I("rooms", "Rooms in service"), I("roomNights", "Room-nights slept"), I("chargedNights", "Charged nights"),
        M("roomRevenue", "Room revenue (before GST)"), P("occupancy", "Occupancy %"), M("adr", "ADR"), M("revpar", "RevPAR")];
    public static readonly IReadOnlyList<ReportColumn> ScoreCols = [T("kpi", "KPI"), P("actualPct", "Actual %"), P("targetPct", "Target %"), M("actualMoney", "Actual"), M("targetMoney", "Target"), T("status", "Status")];
    public static readonly IReadOnlyList<ReportColumn> NoteCols = [T("note", "Note")];

    public static ReportDefinition Definition { get; } = new(Id, "Management study pack (PDF + Excel)", "study", "P1", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003",
        ReportDateClass.FinancialYear, [new ReportParam("fy", "Financial year", "fy", ReportDateClass.FinancialYear)], ContentsCols,
        $"Study reports on their own bases (bill month, night date); executive summary on room lines by night date · {Footer}",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No);

    /// <summary>Percent with one decimal (prototype div1) and paise division rounded half away from zero (Math.round on positives).</summary>
    public static double Div1(long a, long b) => b > 0 ? Math.Round(a * 1000.0 / b, MidpointRounding.AwayFromZero) / 10 : 0;

    /// <summary>F12-D L2: paise division in integers, rounded half away from zero (no double, exact above 2^53); 0 when the divisor is not positive. Equals the old Math.Round on the same inputs.</summary>
    public static long DivP(long a, long b)
    {
        if (b <= 0) return 0;
        var q = Math.DivRem(Math.Abs(a), b, out var r);
        if (r * 2 >= b) q++; // r < b, so r * 2 cannot overflow for b up to long.MaxValue / 2; larger b is not a paise count
        return a < 0 ? -q : q;
    }

    private static readonly string[] Mon = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>Monthly rows and the FY row: occupancy = distinct room-nights slept ÷ (rooms × days); ADR = room revenue ÷ charged nights.</summary>
    public static (IReadOnlyList<PackMonth> Months, PackMonth Fy) Summary(int rooms, IReadOnlyList<RoomNightFact> facts, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var months = new List<PackMonth>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var f = m < from ? from : m;
            var end = m.AddMonths(1).AddDays(-1);
            var t = end > to ? to : end;
            var inMonth = facts.Where(x => x.Night >= f && x.Night <= t).ToList();
            var slept = inMonth.Select(x => (x.Room, x.Night)).Distinct().Count();
            var rev = inMonth.Sum(x => x.AmountPaise);
            long cap = (long)rooms * (t.DayNumber - f.DayNumber + 1);
            months.Add(new PackMonth(m.ToString("yyyy-MM", CultureInfo.InvariantCulture), Mon[m.Month - 1] + " " + m.ToString("yy", CultureInfo.InvariantCulture), slept, inMonth.Count, rev, cap, Div1(slept, cap), DivP(rev, inMonth.Count), DivP(rev, cap)));
        }

        int rn = months.Sum(x => x.RoomNights), cn = months.Sum(x => x.ChargedNights);
        long rv = months.Sum(x => x.RoomRevenue), cp = months.Sum(x => x.Capacity);
        return (months, new PackMonth("fy", "FY", rn, cn, rv, cp, Div1(rn, cp), DivP(rv, cn), DivP(rv, cp)));
    }

    private static ReportRow Row(Dictionary<string, object?> cells, ReportRowKind k = ReportRowKind.Row) => new(k, cells);

    public static IReadOnlyList<ReportTable> SummaryTables(string fy, int rooms, IReadOnlyList<PackMonth> months, PackMonth tot, double targetOcc, long targetAdr)
    {
        ArgumentNullException.ThrowIfNull(months);
        ArgumentNullException.ThrowIfNull(tot);
        Dictionary<string, object?> Cells(string period, PackMonth m) => new(StringComparer.Ordinal)
        {
            ["fy"] = fy, ["period"] = period, ["rooms"] = (long)rooms, ["roomNights"] = (long)m.RoomNights, ["chargedNights"] = (long)m.ChargedNights,
            ["roomRevenue"] = m.RoomRevenue, ["occupancy"] = m.Occupancy, ["adr"] = m.Adr, ["revpar"] = m.RevPar,
        };
        var summary = months.Select(m => Row(Cells(m.Label, m))).Append(Row(Cells("FY " + fy, tot), ReportRowKind.Total)).ToList();
        var score = new List<ReportRow>
        {
            Row(new(StringComparer.Ordinal) { ["kpi"] = "Occupancy", ["actualPct"] = tot.Occupancy, ["targetPct"] = targetOcc, ["actualMoney"] = null, ["targetMoney"] = null, ["status"] = tot.Occupancy >= targetOcc ? "On target" : "Below target" }),
            Row(new(StringComparer.Ordinal) { ["kpi"] = "ADR (before GST)", ["actualPct"] = null, ["targetPct"] = null, ["actualMoney"] = tot.Adr, ["targetMoney"] = targetAdr, ["status"] = tot.Adr >= targetAdr ? "On target" : "Below target" }),
        };
        var labels = months.Select(m => m.Label).ToList();
        return
        [
            new ReportTable("summary", "Executive summary", SummaryCols, summary),
            new ReportTable("scorecard", "Scorecard", ScoreCols, score),
            ChartBlocks.Section(new ChartBlock("pack-revenue", "Room revenue by month", ChartKind.Bar, labels, [new ChartSeries("roomRevenue", "Room revenue (before GST)", ReportColumnType.Money, months.Select(m => (double?)m.RoomRevenue).ToList())], "Month")),
            ChartBlocks.Section(new ChartBlock("pack-occupancy", "Occupancy by month", ChartKind.Line, labels, [new ChartSeries("occupancy", "Occupancy %", ReportColumnType.Percent, months.Select(m => (double?)m.Occupancy).ToList())], "Month")),
        ];
    }

    public static ReportTable Notes(int included) => new("notes", "Notes", NoteCols,
    [
        Row(new(StringComparer.Ordinal) { ["note"] = $"Footer: {Footer}. Legacy rows carry the legacy marker; HCC live data starts 31 Oct 2026." }),
        Row(new(StringComparer.Ordinal) { ["note"] = $"{included} of {StudyIds.Count} study reports included; the others are listed in the contents with the reason." }),
        Row(new(StringComparer.Ordinal) { ["note"] = "Definitions (contract §9.2): occupancy = distinct room-nights slept ÷ (rooms in service × days); ADR = room charge before GST ÷ charged nights; FY April–March; halls excluded." }),
    ]);
}

/// <summary>The pack selector. Sub-reports run through the ReportRunner resolved lazily (the runner's catalogue contains this selector).</summary>
public sealed class ManagementPackReport(IServiceProvider services) : IReportSelector, IMeasuredReport
{
    public ReportDefinition Definition => PackRules.Definition;

    public string MeasKey => PackRules.Key;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var fy = parameters.Values.GetValueOrDefault("fy") ?? "";
        DateOnly from = parameters.From ?? new DateOnly(int.Parse(fy[..4], CultureInfo.InvariantCulture), 4, 1), to = parameters.To ?? from.AddYears(1).AddDays(-1);
        var runner = services.GetRequiredService<ReportRunner>();
        var catalogue = services.GetRequiredService<IReportCatalogue>();
        var gate = services.GetService<IMeasurementGate>();
        var contents = new List<ReportRow>();
        var chapters = new List<ReportTable>();
        foreach (var (no, title, ids) in PackRules.Chapters)
        {
            foreach (var id in ids)
            {
                var sel = catalogue.Find(id);
                string status = "included";
                var blocks = 0L;
                var key = MeasurementCatalogue.Items.FirstOrDefault(i => i.ReportId == id)?.Key;
                if (gate is not null && key is not null && !await gate.IsOnAsync(caller.PropertyId, key, ct).ConfigureAwait(false)) status = "switched off";
                else if (sel is null) status = "not built yet";
                else
                {
                    try
                    {
                        var r = await runner.RunAsync(caller, id, new Dictionary<string, string?> { ["fy"] = fy }, new RunOptions(All: true), ct).ConfigureAwait(false);
                        foreach (var t in ReportRunner.Tables(r)) { chapters.Add(t with { Key = $"{id}:{t.Key}", Title = $"{no}. {sel.Definition.Title}{(t.Title is { } tt ? " · " + tt : "")}" }); blocks++; }
                    }
                    catch (HccException ex) { status = ex.Code == HccErrorCode.FORBIDDEN ? "not visible to you" : "not available: " + ex.Message; }
                }

                contents.Add(new ReportRow(ReportRowKind.Row, new Dictionary<string, object?>(StringComparer.Ordinal)
                    { ["no"] = (long)no, ["chapter"] = title, ["report"] = id, ["title"] = sel?.Definition.Title ?? id, ["status"] = status, ["blocks"] = blocks }));
            }
        }

        var facts = services.GetService<IStudyPackFacts>();
        var rooms = facts is null ? 0 : await facts.RoomsInServiceAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var nights = facts is null ? [] : await facts.RoomNightsAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var (months, tot) = PackRules.Summary(rooms, nights, from, to);
        var settings = services.GetService<ISettingsReader>();
        async Task<long> Target(string k, long d)
        {
            if (settings is null) return d;
            try { var v = await settings.GetAsync(caller.PropertyId, k, ct: ct).ConfigureAwait(false); return v.Number != 0 ? v.Number : d; }
            catch (HccException) { return d; }
        }

        var sections = new List<ReportTable> { new("contents", "Contents", PackRules.ContentsCols, contents) };
        sections.AddRange(PackRules.SummaryTables(fy, rooms, months, tot, await Target("meas.target.occupancy", 35).ConfigureAwait(false), await Target("meas.target.adr", 205000).ConfigureAwait(false)));
        sections.AddRange(chapters);
        sections.Add(PackRules.Notes(contents.Count(r => (string?)r.Cells["status"] == "included")));
        var legacy = string.CompareOrdinal(fy, "2026-27") < 0;
        return new ReportResult(Definition.Id, Definition.Title, parameters, sections[0].Columns, sections[0].Rows,
            new ReportAbout(default, "", Definition.DateBasis, false, legacy, from, to), Sections: sections);
    }
}

/// <summary>meas.study.packSchedule (off / monthly / quarterly) applied to the F7 schedules of the pack (M-10: monthly on the 1st).</summary>
public static class StudyPackSchedule
{
    public const string SettingKey = "meas.study.packSchedule";

    public static string Parse(string? v) => v is "off" or "quarterly" ? v : "monthly";

    /// <summary>Monthly = every 1st; quarterly = 1 Apr / 1 Jul / 1 Oct / 1 Jan; off = never (prototype packScheduleDue).</summary>
    public static bool Due(string setting, DateOnly d) => d.Day == 1 && setting switch { "monthly" => true, "quarterly" => d.Month is 1 or 4 or 7 or 10, _ => false };

    /// <summary>The pack's schedules with Enabled set for business month <paramref name="today"/> (the F7 Due rule still picks the 1st, 06:00); others unchanged.</summary>
    public static IReadOnlyList<ReportScheduleRecord> Apply(IReadOnlyList<ReportScheduleRecord> schedules, string setting, DateOnly today, bool itemOn)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var on = itemOn && Due(Parse(setting), new DateOnly(today.Year, today.Month, 1));
        return schedules.Select(s => s.ReportId == PackRules.Id && s.Enabled != on ? s with { Enabled = on } : s).ToList();
    }
}

/// <summary>Reads the setting and the MEAS gate and saves the pack schedules whose Enabled flag changes.</summary>
public sealed class StudyPackScheduler(IReportScheduleStore schedules, ISettingsReader settings, IReportPropertyInfo property, IMeasurementGate? gate = null)
{
    public async Task<int> SyncAsync(int propertyId, CancellationToken ct = default)
    {
        string? v;
        try { v = (await settings.GetAsync(propertyId, StudyPackSchedule.SettingKey, ct: ct).ConfigureAwait(false)).Text; }
        catch (HccException) { v = null; }
        var on = gate is null || await gate.IsOnAsync(propertyId, PackRules.Key, ct).ConfigureAwait(false);
        var today = (await property.GetAsync(propertyId, ct).ConfigureAwait(false)).BusinessDate;
        var before = await schedules.ListAsync(propertyId, ct).ConfigureAwait(false);
        var after = StudyPackSchedule.Apply(before, v ?? "monthly", today, on);
        var n = 0;
        for (var i = 0; i < before.Count; i++)
            if (!ReferenceEquals(before[i], after[i])) { await schedules.SaveAsync(propertyId, after[i], ct).ConfigureAwait(false); n++; }
        return n;
    }
}

/// <summary>F12-D L6: rate-limited failure log of the pack schedule sync: a warning on the first failure and then every <c>RepeatEvery</c>-th one in a row, an information line on recovery.</summary>
public sealed class StudyPackFailureLog(Microsoft.Extensions.Logging.ILogger logger, int repeatEvery = 30)
{
    private readonly Dictionary<int, int> _failures = [];

    public int RepeatEvery { get; } = repeatEvery < 1 ? 1 : repeatEvery;

    public void Failed(int propertyId, Exception ex)
    {
        var n = _failures.GetValueOrDefault(propertyId) + 1;
        _failures[propertyId] = n;
        if (n == 1 || n % RepeatEvery == 0)
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, ex, "Study pack schedule sync failed for property {PropertyId} ({Count} failed minute(s) in a row); retrying next minute.", propertyId, n);
    }

    public void Succeeded(int propertyId)
    {
        if (_failures.Remove(propertyId, out var n))
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Study pack schedule sync recovered for property {PropertyId} after {Count} failed minute(s).", propertyId, n);
    }
}

/// <summary>One minute of the pack schedule sync: every property is tried on its own, a failure is logged and never stops the host or the other properties.</summary>
public static class StudyPackTick
{
    public static async Task RunAsync(StudyPackScheduler scheduler, Func<CancellationToken, Task<IEnumerable<int>>> propertyIds, StudyPackFailureLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(propertyIds);
        ArgumentNullException.ThrowIfNull(log);
        IEnumerable<int> ids;
#pragma warning disable CA1031 // a background sync must never stop the host: a failed minute is logged and retried on the next tick
        try { ids = await propertyIds(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.Failed(0, ex); return; }
        foreach (var p in ids)
        {
            try { await scheduler.SyncAsync(p, ct).ConfigureAwait(false); log.Succeeded(p); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.Failed(p, ex); }
        }
#pragma warning restore CA1031
    }
}

/// <summary>Keeps the pack schedules in step with the setting once a minute, ahead of the F7 schedule worker's tick.</summary>
public sealed class StudyPackScheduleWorker(StudyPackScheduler scheduler, Hcc.Reporting.Sql.SqlReportStores stores, TimeProvider clock, Microsoft.Extensions.Logging.ILogger<StudyPackScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        var log = new StudyPackFailureLog(logger);
        do
        {
            await StudyPackTick.RunAsync(scheduler, async ct => await stores.PropertyIdsAsync(ct).ConfigureAwait(false), log, stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
