// F12 MEAS · rpt.rooms.ppm-compliance — Preventive maintenance compliance (switch meas.hk.ppm, module Maintenance). Server twin of meas-hk/ppm-compliance.ts.
// Red row (per asset kind): a preventive task overdue more than 7 days; P1 corrective MTTR above 24 hours; a third failure of one asset inside 90 days.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using static Hcc.Reporting.Reports.MeasHk.MeasHkRules;

namespace Hcc.Reporting.Reports.MeasHk;

public sealed class PpmComplianceReport(SqlPlatform platform) : MeasHkReportBase(platform)
{
    public const string Id = "rpt.rooms.ppm-compliance";
    public static readonly string[] Kinds = ["AC", "Geyser", "TV", "Lift", "Generator", "Pump", "RO plant", "Other"];

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("kind", "Asset kind"), I("plansDue", "Plans due"), I("doneOnTime", "Done on time"), I("overdue", "Overdue"), T("ratio", "Preventive-to-corrective ratio"), T("mttr", "MTTR by priority"),
        I("repeatFailures", "Repeat failures in 90 days"), I("amcVisits", "AMC visits due"),
    ];

    private const string Basis = "Preventive work orders by their due date in the range (up to 366 days, from the FY start by default); a work order is done on time when finished on or before its due date, overdue when not done and past due at the To date. Ratio = preventive : corrective work orders created in the range. MTTR = created to done of corrective work orders finished in the range, by priority. Repeat failures = assets with 3 corrective work orders inside 90 days. AMC visits = visits of contracts running in the range for the vendors of that kind of asset. Red: a preventive task overdue more than 7 days, P1 MTTR above 24 hours, or a third failure of one asset in 90 days.";

    public override string MeasKey => "meas.hk.ppm";

    public override ReportDefinition Definition { get; } = new(Id, "Preventive maintenance compliance", "rooms", "P2", "MEAS", false, null, ["PERM-RPT-001"], ["PERM-MNT-001"], "PERM-RPT-003", ReportDateClass.Financial,
        [new("from", "From", "date", ReportDateClass.Financial), new("to", "To", "date", ReportDateClass.Financial), new("assetKind", "Asset kind", "enum", null, Kinds)],
        Columns, Basis, Std, ReportPhone.Yes);

    private sealed record Wo(long Id, string Kind, string Priority, long? Asset, string AssetKind, DateTime Created, DateTime? Done, DateOnly? Due, string Status);

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await ClockAsync(caller, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 366, clock.BusinessDate);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var data = await QueryAsync("""
            SELECT w.wo_id, w.kind, w.priority, w.asset_id, ISNULL(a.kind, 'Other'), w.created_utc, w.done_utc, w.due_date, w.status
              FROM mnt.work_order w LEFT JOIN mnt.asset a ON a.asset_id = w.asset_id
             WHERE w.property_id = @p AND w.status <> 'Cancelled';
            """, caller.PropertyId, null, ct).ConfigureAwait(false);
        var all = data.Select(r => new Wo(L(r[0]), S(r[1]), S(r[2]), r[3] is null ? null : L(r[3]), S(r[4]), (DateTime)r[5]!, r[6] as DateTime?, r[7] is null ? null : Dt(r[7]), S(r[8]))).ToList();
        var vendors = await QueryAsync("SELECT a.kind, a.vendor_id FROM mnt.asset a WHERE a.property_id = @p AND a.vendor_id IS NOT NULL;", caller.PropertyId, null, ct).ConfigureAwait(false);
        var amcs = await QueryAsync("SELECT vendor_id, from_date, to_date, visits FROM mnt.amc WHERE property_id = @p;", caller.PropertyId, null, ct).ConfigureAwait(false);
        var wanted = P(parameters, "assetKind");
        var rows = new List<ReportRow>();
        foreach (var kind in Kinds)
        {
            if (wanted != "" && wanted != kind) continue;
            var mine = all.Where(w => w.AssetKind == kind).ToList();
            var pm = mine.Where(w => w.Kind == "Preventive" && w.Due is { } d && d >= from && d <= to).ToList();
            var onTime = pm.Count(w => w.Done is { } dn && DateOnly.FromDateTime(dn) <= w.Due);
            var late = pm.Where(w => w.Done is null && w.Due!.Value.DayNumber < to.DayNumber).ToList();
            var overdueRed = late.Any(w => OverdueRed(w.Due!.Value, to));
            bool InRange(DateTime d) => DateOnly.FromDateTime(d) >= from && DateOnly.FromDateTime(d) <= to;
            var created = mine.Where(w => InRange(w.Created)).ToList();
            var nPm = created.Count(w => w.Kind == "Preventive");
            var nCor = created.Count(w => w.Kind == "Corrective");
            var mttr = new List<(string Pr, double? Hours)>();
            foreach (var pr in new[] { "P1", "P2", "P3", "P4" })
            {
                var m = Mean(mine.Where(w => w.Kind == "Corrective" && w.Priority == pr && w.Done is { } dn && InRange(dn)).Select(w => (w.Done!.Value - w.Created).TotalMinutes).ToList());
                mttr.Add((pr, m is null ? null : m / 60));
            }

            var p1 = mttr.First(m => m.Pr == "P1").Hours;
            var repeat = mine.Where(w => w.Kind == "Corrective" && w.Asset is not null && DateOnly.FromDateTime(w.Created) <= to).GroupBy(w => w.Asset!.Value)
                .Count(g => RepeatFailureRed(g.Select(w => DateOnly.FromDateTime(w.Created)).ToList()));
            var vendorSet = vendors.Where(v => S(v[0]) == kind).Select(v => L(v[1])).ToHashSet();
            var amc = amcs.Where(a => vendorSet.Contains(L(a[0])) && Dt(a[1]) <= to && Dt(a[2]) >= from).Sum(a => L(a[3]));
            if (pm.Count == 0 && nCor == 0 && repeat == 0 && amc == 0) continue;
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = kind, ["plansDue"] = (long)pm.Count, ["doneOnTime"] = (long)onTime, ["overdue"] = (long)late.Count, ["ratio"] = $"{nPm} : {nCor}",
                ["mttr"] = string.Join("; ", mttr.Where(m => m.Hours is not null).Select(m => $"{m.Pr} {FormatHours(R1(m.Hours!.Value))} h")), ["repeatFailures"] = (long)repeat, ["amcVisits"] = amc,
            }, overdueRed || P1MttrRed(p1) || repeat > 0));
        }

        return Done(parameters, Columns, rows);
    }

    /// <summary>JavaScript number-to-text: 24 -> "24", 3.2 -> "3.2".</summary>
    private static string FormatHours(double h) => h.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
