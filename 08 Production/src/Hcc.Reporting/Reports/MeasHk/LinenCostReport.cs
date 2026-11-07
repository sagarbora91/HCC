// F12 MEAS · rpt.stock.linen-cost — Linen, laundry and guest-supply cost per occupied room (switch meas.hk.linen, module Inventory).
// Server twin of meas-hk/linen-cost.ts. Money: Cost per occupied room-night is money (gate PERM-STK-004; view PERM-STK-001), removed for Supervisor /
// Housekeeper / Maintenance / Stock roles (DEC-061). Red row: loss above 2 % of par in the month (meas.target.lossPct default);
// cost per occupied night above the target when the Owner sets meas.target.linenCost (no default: rule skipped).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using static Hcc.Reporting.Reports.MeasHk.MeasHkRules;

namespace Hcc.Reporting.Reports.MeasHk;

public sealed class LinenCostReport(SqlPlatform platform, Hcc.Application.Configuration.ISettingsReader? settings = null) : MeasHkReportBase(platform)
{
    public const string Id = "rpt.stock.linen-cost";
    private static readonly string[] Lost = ["Missing", "Taken by guest", "Expired", "Other"];
    private static readonly string[] Damaged = ["Damaged", "Stained beyond use"];

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("item", "Item"), I("par", "Par"), I("onHand", "On hand"), I("batches", "Laundry batches"), I("lost", "Lost"), I("damaged", "Damaged"), T("discarded", "Discarded (reason)"),
        I("issued", "Amenities issued"), M("costPerNight", "Cost per occupied room-night"),
    ];

    private const string Basis = "Quantities in the month (any one month with data). Par = pieces required in circulation (linen). Lost = Missing, Taken by guest, Expired, Other; Damaged = Damaged, Stained beyond use and laundry returned damaged; Discarded = captured discards with their reason. Issued = supplies used. Cost per occupied room-night = (lost + damaged + discarded + issued pieces × standard cost + laundry charge at the vendor rate) ÷ occupied room-nights. Red: loss above 2 % of par, or cost per occupied night above the target when one is set.";

    public override string MeasKey => "meas.hk.linen";

    public override ReportDefinition Definition { get; } = new(Id, "Linen, laundry and guest-supply cost per occupied room", "stock", "P2", "MEAS", true, "PERM-STK-004", ["PERM-STK-001"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month), new("group", "Item group", "enum", null, ["linen", "amenity", "cleaning"])], Columns, Basis, Std, ReportPhone.No);

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await ClockAsync(caller, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 31, clock.BusinessDate);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var monthStart = new DateOnly(from.Year, from.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        void Range(Microsoft.Data.SqlClient.SqlCommand c) { GuestsSql.Add(c, "@from", SqlDbType.Date, GuestsSql.D(monthStart)); GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(monthEnd)); }
        var group = P(parameters, "group");
        var items = await QueryAsync("""
            SELECT i.item_id, i.name, i.kind, i.cost_paise, ISNULL(i.laundry_rate_paise, 0),
                   ISNULL((SELECT SUM(s.qty) FROM stk.stock s WHERE s.item_id = i.item_id), 0),
                   ISNULL((SELECT SUM(CAST(pr.pieces AS BIGINT) * rc.n) FROM stk.item_par pr JOIN (SELECT room_type_id, COUNT(*) n FROM inv.room WHERE property_id = @p GROUP BY room_type_id) rc ON rc.room_type_id = pr.room_type_id
                            WHERE pr.item_id = i.item_id), 0) * ISNULL((SELECT par_sets FROM stk.setting WHERE property_id = @p), 3)
              FROM stk.item i WHERE i.property_id = @p AND (@g = '' OR i.kind = @g) ORDER BY i.kind, i.name;
            """, caller.PropertyId, c => GuestsSql.Add(c, "@g", SqlDbType.VarChar, group), ct).ConfigureAwait(false);
        var losses = await QueryAsync("SELECT item_id, reason, SUM(qty) FROM stk.loss WHERE property_id = @p AND business_date BETWEEN @from AND @to GROUP BY item_id, reason;", caller.PropertyId, Range, ct).ConfigureAwait(false);
        var batches = await QueryAsync("""
            SELECT l.item_id, COUNT(DISTINCT b.batch_id), SUM(l.sent) FROM stk.laundry_batch b JOIN stk.laundry_line l ON l.batch_id = b.batch_id
             WHERE b.property_id = @p AND b.sent_date BETWEEN @from AND @to GROUP BY l.item_id;
            """, caller.PropertyId, Range, ct).ConfigureAwait(false);
        var returnedDamaged = await QueryAsync("""
            SELECT l.item_id, SUM(ISNULL(l.damaged, 0)) FROM stk.laundry_batch b JOIN stk.laundry_line l ON l.batch_id = b.batch_id
             WHERE b.property_id = @p AND b.returned_date BETWEEN @from AND @to GROUP BY l.item_id;
            """, caller.PropertyId, Range, ct).ConfigureAwait(false);
        var issues = await QueryAsync("SELECT item_id, SUM(qty) FROM stk.move WHERE property_id = @p AND kind = 'use' AND business_date BETWEEN @from AND @to GROUP BY item_id;", caller.PropertyId, Range, ct).ConfigureAwait(false);
        var discards = await QueryAsync("SELECT item, reason, SUM(qty) FROM meas.discard WHERE property_id = @p AND discard_date BETWEEN @from AND @to GROUP BY item, reason;", caller.PropertyId, Range, ct).ConfigureAwait(false);
        var anyDiscard = L((await QueryAsync("SELECT COUNT(*) FROM meas.discard WHERE property_id = @p;", caller.PropertyId, null, ct).ConfigureAwait(false))[0][0]) > 0;
        var occ = await OccupiedAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var rn = occ.Sum(kv => kv.Value);
        // f12-int-an (MEAS-RPT-HK item 5): the Owner's stored meas.target.lossPct (whole percent), else the contract default 2.
        var lossPct = await Hcc.Reporting.Reports.MeasDaily.MeasTargets.GetAsync(settings, caller.PropertyId, "meas.target.lossPct", DefaultLossPct, ct).ConfigureAwait(false);
        var rows = new List<ReportRow>();
        foreach (var it in items)
        {
            var id = L(it[0]);
            var name = S(it[1]);
            var kind = S(it[2]);
            long cost = L(it[3]), rate = L(it[4]), par = L(it[6]);
            long Sum(string[] reasons) => losses.Where(l => L(l[0]) == id && reasons.Contains(S(l[1]))).Sum(l => L(l[2]));
            var lost = Sum(Lost);
            var damaged = Sum(Damaged) + returnedDamaged.Where(r => L(r[0]) == id).Sum(r => L(r[1]));
            var dis = discards.Where(d => S(d[0]) == name || S(d[0]) == id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var disQty = dis.Sum(d => L(d[2]));
            var batch = batches.FirstOrDefault(b => L(b[0]) == id);
            long sent = batch is null ? 0 : L(batch[2]);
            long? issued = kind == "linen" ? null : issues.Where(m => L(m[0]) == id).Sum(m => L(m[1]));
            var total = (lost + damaged + disQty + (issued ?? 0)) * cost + sent * rate;
            long? per = rn > 0 ? DivHalfUp(total, rn) : null; // F12-MEAS2-E L2: integer half-up on paise (contract §8), no double
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["item"] = name, ["par"] = par, ["onHand"] = L(it[5]), ["batches"] = batch is null ? 0L : L(batch[1]), ["lost"] = lost, ["damaged"] = damaged,
                ["discarded"] = disQty > 0 ? $"{disQty} ({string.Join("; ", dis.Select(d => $"{S(d[1])} {L(d[2])}"))})" : "", ["issued"] = issued, ["costPerNight"] = per,
            }, LinenLossRed(lost + damaged + disQty, par, lossPct)));
        }

        return Done(parameters, Columns, rows, anyDiscard ? null : Basis + " Discards: capture needed — no discard reason has been captured yet (meas-cap-hk).");
    }

    /// <summary>F12-MEAS2-E L2: n ÷ d rounded half away from zero in integers (d &gt; 0); equals JS Math.round for n ≥ 0.</summary>
    public static long DivHalfUp(long n, long d) => n >= 0 ? (2 * n + d) / (2 * d) : -((-2 * n + d) / (2 * d));
}
