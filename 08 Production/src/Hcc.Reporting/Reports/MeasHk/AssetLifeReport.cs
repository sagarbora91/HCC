// F12 MEAS · rpt.acc.asset-life — Asset register, FF&E reserve and renovation cycle (switch meas.hk.assets). Server twin of meas-hk/asset-life.ts.
// Money columns (removed, not blanked, without PERM-ACC-001; Housekeeping / Maintenance never hold it, DEC-061): WDV, Replacement cost, FF&E reserve accrued, Capex spent.
// Red row: an asset past its expected life with no active maintenance plan; the FY row when capex spent is above the reserve accrued.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using static Hcc.Reporting.Reports.MeasHk.MeasHkRules;

namespace Hcc.Reporting.Reports.MeasHk;

public sealed class AssetLifeReport(SqlPlatform platform) : MeasHkReportBase(platform)
{
    public const string Id = "rpt.acc.asset-life";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("asset", "Asset"), T("age", "Age"), T("expectedLife", "Expected life"), T("grade", "Condition grade"), D("replacementDue", "Replacement due"), M("wdv", "WDV"), M("replacementCost", "Replacement cost"),
        M("reserveAccrued", "FF&E reserve accrued"), M("capexSpent", "Capex spent"), T("refurbishment", "Refurbishment due (category)"),
    ];

    private const string Basis = "As of one date. Age from the installed date (or the put-to-use date for register assets); expected life, grade and replacement cost as captured. Replacement due = installed date + expected life. WDV = the register cost HCC holds (the CA passes depreciation in Tally). Month-end row: FF&E reserve accrued = ffe.reservePct of the FY room revenue (before GST) to the date; capex spent = register purchases of the FY. Refurbishment due is named when the replacement date falls within 12 months. Red: past expected life with no maintenance plan; capex spent above the reserve accrued.";

    public override string MeasKey => "meas.hk.assets";

    public override ReportDefinition Definition { get; } = new(Id, "Asset register, FF&E reserve and renovation cycle", "acc", "P2", "MEAS", true, "PERM-ACC-001", ["PERM-ACC-001"], ["PERM-HK-001", "PERM-MNT-001"], "PERM-RPT-003", ReportDateClass.AsOf,
        [new("asOf", "As of", "date", ReportDateClass.AsOf), new("kind", "Asset kind", "text")], Columns, Basis, Std, ReportPhone.No);

    private sealed record Asset(string Key, string Name, string Kind, DateOnly? Start, long? Wdv, bool Room, bool Planned);

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var count = L((await QueryAsync("SELECT COUNT(*) FROM meas.asset_life WHERE property_id = @p;", caller.PropertyId, null, ct).ConfigureAwait(false))[0][0]);
        if (count == 0) return Done(parameters, Columns, [], CaptureNote(Basis, "asset life fields: expected life, condition grade and replacement cost (meas-cap-hk)"));
        var asOf = parameters.To!.Value;
        var life = (await QueryAsync("SELECT asset_id, expected_life_months, condition_grade, replacement_cost_paise FROM meas.asset_life WHERE property_id = @p;", caller.PropertyId, null, ct).ConfigureAwait(false))
            .ToDictionary(r => S(r[0]), r => (Months: r[1] is null ? (int?)null : (int)L(r[1]), Grade: r[2] is null ? null : S(r[2]), Cost: r[3] is null ? (long?)null : L(r[3])), StringComparer.Ordinal);
        var mnt = await QueryAsync("""
            SELECT CAST(a.asset_id AS VARCHAR(30)), a.name, a.kind, a.installed_on, CASE WHEN a.room_id IS NOT NULL THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS (SELECT 1 FROM mnt.pm_plan pl WHERE pl.asset_id = a.asset_id AND pl.active = 1) THEN 1 ELSE 0 END
              FROM mnt.asset a WHERE a.property_id = @p AND a.status <> 'Retired';
            """, caller.PropertyId, null, ct).ConfigureAwait(false);
        var acc = await QueryAsync("""
            SELECT CAST(f.fixed_asset_id AS VARCHAR(30)), f.asset_no + N' ' + f.name, f.ledger, COALESCE(f.put_to_use, f.purchase_date), f.cost_paise, f.purchase_date
              FROM acc.fixed_asset f WHERE f.property_id = @p AND f.disposed_date IS NULL AND f.purchase_date <= @to;
            """, caller.PropertyId, c => GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(asOf)), ct).ConfigureAwait(false);
        var wanted = P(parameters, "kind");
        var assets = mnt.Select(r => new Asset(S(r[0]), S(r[1]), S(r[2]), r[3] is null ? null : Dt(r[3]), null, L(r[4]) == 1, L(r[5]) == 1))
            .Concat(acc.Select(r => new Asset(S(r[0]), S(r[1]), "Fixed assets " + S(r[2]), Dt(r[3]), L(r[4]), false, false)))
            .Where(a => wanted == "" || a.Kind == wanted).OrderBy(a => a.Kind, StringComparer.Ordinal).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();
        var rows = new List<ReportRow>();
        foreach (var a in assets)
        {
            var l = life.GetValueOrDefault(a.Key);
            int? age = a.Start is { } s ? WholeMonths(s, asOf) : null;
            DateOnly? due = a.Start is { } st && l.Months is { } m ? st.AddMonths(m) : null;
            var soon = due is { } dd && dd <= asOf.AddMonths(12);
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["asset"] = a.Name, ["age"] = age is null ? null : AgeText(age.Value), ["expectedLife"] = l.Months is { } em ? AgeText(em) : null, ["grade"] = l.Grade, ["replacementDue"] = due,
                ["wdv"] = a.Wdv, ["replacementCost"] = l.Cost, ["reserveAccrued"] = null, ["capexSpent"] = null, ["refurbishment"] = soon ? (a.Room ? "Room refurbishment" : "Plant and equipment") : null,
            }, PastLife(due, asOf) && !a.Planned));
        }

        var fyStart = asOf.Month >= 4 ? new DateOnly(asOf.Year, 4, 1) : new DateOnly(asOf.Year - 1, 4, 1);
        var fyEnd = fyStart.AddYears(1).AddDays(-1);
        var revenue = L((await QueryAsync("""
            SELECT ISNULL(SUM(c.amount_paise), 0) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
             WHERE r.property_id = @p AND c.kind = 'room' AND c.service_date BETWEEN @from AND @to;
            """, caller.PropertyId, c => { GuestsSql.Add(c, "@from", SqlDbType.Date, GuestsSql.D(fyStart)); GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(asOf)); }, ct).ConfigureAwait(false))[0][0]);
        var capex = L((await QueryAsync("""
            SELECT ISNULL(SUM(cost_paise), 0) FROM acc.fixed_asset WHERE property_id = @p AND disposed_date IS NULL AND purchase_date BETWEEN @from AND @to;
            """, caller.PropertyId, c => { GuestsSql.Add(c, "@from", SqlDbType.Date, GuestsSql.D(fyStart)); GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(asOf)); }, ct).ConfigureAwait(false))[0][0]);
        var accrued = (long)Math.Round(revenue * (double)DefaultFfePct / 100, MidpointRounding.AwayFromZero);
        rows.Add(new ReportRow(ReportRowKind.Total, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["asset"] = $"FY {fyStart.Year}-{fyEnd.Year % 100:00} to date", ["age"] = null, ["expectedLife"] = null, ["grade"] = null, ["replacementDue"] = null, ["wdv"] = null, ["replacementCost"] = null,
            ["reserveAccrued"] = accrued, ["capexSpent"] = capex, ["refurbishment"] = null,
        }, ReserveRed(capex, accrued) ? RedGroup : null));
        return Done(parameters, Columns, rows);
    }
}
