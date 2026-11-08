// F8-RPT-P2-C · rpt.admin.rate-sheet (partial -> built on the engine): rate sheet / publish log / change history. Server twin of 03 Prototype/src/domain/reports/p2-c/rate-sheet.ts.
// Reads the yield module through its own ports (IYieldStore: config view, publishes, inputs; IYieldRateQuery: plan rates) - no new figures.
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Yield;
using Hcc.Contracts.Yield;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.Money;

namespace Hcc.Reporting.Reports.P2C;

public sealed class RateSheetReport(IYieldStore store, IYieldRateQuery quotes, IP2CReportSource source, TimeProvider? clock = null) : IReportSelector
{
    public const string Id = "rpt.admin.rate-sheet";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.T("roomType", "Room type"), MoneyRules.T("plan", "Plan"), MoneyRules.M("rate", "Rate"), MoneyRules.Dt("night", "Night"), MoneyRules.T("type", "Type"),
        MoneyRules.M("before", "Before"), MoneyRules.M("after", "After"), MoneyRules.T("when", "When"), MoneyRules.T("who", "Who"), MoneyRules.T("reason", "Reason"),
    ];

    public static readonly IReadOnlyList<string> Views = ["rate sheet", "publish log", "change history"];

    public ReportDefinition Definition { get; } = P2CRules.Def(Id, "Rate sheet, publish log and change history", "admin", true, "PERM-YLD-001", ["PERM-YLD-001"], ReportDateClass.Forward,
        [new("view", "View", "enum", null, Views, "rate sheet"), new("from", "From night", "date", ReportDateClass.Forward), new("to", "To night", "date", ReportDateClass.Forward)], Cols,
        "Nights in the range. Rate sheet: the rate each active plan sells at on that night (yield engine, rounded to the guardrail unit). Publish log: publishes whose nights overlap the range. Change history: the rate each publish moved (before and after) and each manual night override, oldest first.",
        P2CRules.Sxp, ReportPhone.Yes, legacy: ["C7.RoomTariffCard"]);

    private static IReadOnlyList<ReportColumn> By(params string[] keys) => [.. Cols.Where(c => keys.Contains(c.Key))];

    public static DateOnly Day(string s) => DateOnly.Parse(s.Length > 10 ? s[..10] : s, CultureInfo.InvariantCulture);

    public static DateTime Utc(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var from = parameters.From!.Value; var to = parameters.To!.Value;
        if (to.DayNumber - from.DayNumber + 1 > 92) throw new HccException(HccErrorCode.VALIDATION, "The range is longer than 92 days. Narrow the range.");
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var inputs = await store.LoadInputsAsync(caller.PropertyId, null, 1, now, null, ct).ConfigureAwait(false);
        if (to.DayNumber - inputs.BusinessDate.DayNumber > 90) throw new HccException(HccErrorCode.VALIDATION, "The report can look 90 days ahead at most.");
        var types = inputs.Types.ToDictionary(t => t.Id, t => t.Code);
        var names = await source.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var view = MoneyRules.Param(parameters, "view"); if (view.Length == 0) view = "rate sheet";
        var d = Definition;
        if (view == "publish log")
        {
            var pubs = await store.ListPublishesAsync(caller.PropertyId, 1000, ct).ConfigureAwait(false);
            return BuildLog(d, parameters, pubs, types, names);
        }

        if (view == "change history")
        {
            var pubs = await store.ListPublishesAsync(caller.PropertyId, 1000, ct).ConfigureAwait(false);
            var cfg = await store.GetConfigViewAsync(caller.PropertyId, 0, ct).ConfigureAwait(false);
            return BuildHistory(d, parameters, pubs, cfg.Overrides, types, names);
        }

        var config = await store.GetConfigViewAsync(caller.PropertyId, 0, ct).ConfigureAwait(false);
        var nights = to.DayNumber - from.DayNumber + 1;
        var rows = new List<ReportRow>();
        foreach (var t in types.OrderBy(t => t.Value, StringComparer.Ordinal))
            foreach (var plan in config.Plans.Where(x => x.Active))
            {
                var q = await quotes.QuoteAsync(caller.PropertyId, plan.PlanCode, t.Key, from, nights, ct).ConfigureAwait(false);
                if (q.Issues.Count == 0)
                {
                    foreach (var r in q.Rows) rows.Add(MoneyRules.Row(ReportRowKind.Row, t.Value, ("roomType", t.Value), ("plan", plan.Name), ("night", Day(r.Date)), ("rate", r.RatePaise)));
                    continue;
                }

                for (var i = 0; i < nights; i++)
                {
                    var one = await quotes.QuoteAsync(caller.PropertyId, plan.PlanCode, t.Key, from.AddDays(i), 1, ct).ConfigureAwait(false);
                    if (one.Issues.Count == 0 && one.Rows.Count == 1)
                        rows.Add(MoneyRules.Row(ReportRowKind.Row, t.Value, ("roomType", t.Value), ("plan", plan.Name), ("night", from.AddDays(i)), ("rate", one.Rows[0].RatePaise)));
                }
            }

        return P2CRules.Single(d, parameters, By("roomType", "plan", "night", "rate"), rows);
    }

    public static ReportResult BuildLog(ReportDefinition d, BoundParams p, IReadOnlyList<YieldPublishView> pubs, IReadOnlyDictionary<int, string> types, IReadOnlyDictionary<int, string> names)
    {
        var from = p.From!.Value; var to = p.To!.Value;
        var list = pubs.Where(x => Day(x.From) <= to && Day(x.To) >= from).OrderByDescending(x => x.PublishedUtc, StringComparer.Ordinal).ThenByDescending(x => x.PublishId).ToList();
        var rows = list.Select(x => MoneyRules.Row(ReportRowKind.Row, null,
            ("roomType", string.Join(", ", x.Rows.Select(r => types.TryGetValue(r.RoomTypeId, out var c) ? c : r.RoomTypeId.ToString(CultureInfo.InvariantCulture)).Distinct().OrderBy(c => c, StringComparer.Ordinal))),
            ("night", Day(x.From)), ("type", $"{x.Rows.Count} night rates to {x.To}"), ("when", P2CRules.Stamp(Utc(x.PublishedUtc))), ("who", P2CRules.Name(names, x.ActorId)),
            ("reason", string.IsNullOrEmpty(x.Reason) ? null : x.Reason))).ToList();
        return P2CRules.Single(d, p, By("roomType", "night", "type", "when", "who", "reason"), rows);
    }

    public static ReportResult BuildHistory(ReportDefinition d, BoundParams p, IReadOnlyList<YieldPublishView> pubs, IReadOnlyList<YieldOverrideView> overrides,
        IReadOnlyDictionary<int, string> types, IReadOnlyDictionary<int, string> names)
    {
        var from = p.From!.Value; var to = p.To!.Value;
        string T(int id) => types.TryGetValue(id, out var c) ? c : id.ToString(CultureInfo.InvariantCulture);
        var list = new List<(DateOnly Night, string At, int Seq, ReportRow Row)>();
        var n = 0;
        foreach (var x in pubs)
            foreach (var r in x.Rows)
            {
                var night = Day(r.Night);
                if (night < from || night > to) continue;
                list.Add((night, x.PublishedUtc, n++, MoneyRules.Row(ReportRowKind.Row, null, ("roomType", T(r.RoomTypeId)), ("night", night), ("type", r.Closing ? "Published (closing row)" : "Published"),
                    ("before", r.BeforePaise), ("after", r.AfterPaise), ("when", P2CRules.Stamp(Utc(x.PublishedUtc))), ("who", P2CRules.Name(names, x.ActorId)), ("reason", string.IsNullOrEmpty(x.Reason) ? null : x.Reason))));
            }

        foreach (var o in overrides)
        {
            var night = Day(o.Night);
            if (night < from || night > to) continue;
            list.Add((night, o.SetUtc, n++, MoneyRules.Row(ReportRowKind.Row, null, ("roomType", T(o.RoomTypeId)), ("night", night), ("type", "Override"), ("before", null), ("after", o.RatePaise),
                ("when", P2CRules.Stamp(Utc(o.SetUtc))), ("who", P2CRules.Name(names, o.ActorId)), ("reason", string.IsNullOrEmpty(o.Reason) ? null : o.Reason))));
        }

        var rows = list.OrderBy(x => x.Night).ThenBy(x => x.At, StringComparer.Ordinal).ThenBy(x => x.Seq).Select(x => x.Row).ToList();
        return P2CRules.Single(d, p, By("roomType", "night", "type", "before", "after", "when", "who", "reason"), rows);
    }
}
