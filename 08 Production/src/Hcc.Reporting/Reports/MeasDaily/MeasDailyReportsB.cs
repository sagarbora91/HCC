// F12-RPT-DAILY · server twins of the last four daily-control reports: allowances / voids, no-show and cancellation, inventory exceptions, cashier variance.
// Column keys, types, money flags, parameters and exception rules equal the prototype (03 Prototype/src/domain/reports/meas-daily/*). Rules: MeasDailyRules.
// Known server limits (see MEAS-RPT-DAILY-DONE.md): no per-user daily cap (the discount-limit table is not read here), retention / waiver lines are not posted by any
// server flow yet, cash paid-outs come from the closing step when it records them.
using System.Data;
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.MeasDaily.MeasCols;

namespace Hcc.Reporting.Reports.MeasDaily;

public sealed class AllowancesVoidsReport(SqlPlatform platform) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.money.allowances-voids";

    public string MeasKey => "meas.daily.adjustments";

    private static readonly string[] Kinds = ["discount", "allowance", "rebate", "void", "correction", "refund"];

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("date", "Date"), T("kind", "Kind"), T("bill", "Bill"), M("amount", "Amount"), T("reasonCode", "Reason code"), T("user", "User"), T("approver", "Approver"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Allowances, rebates, voids and corrections", "money", "P1", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("kind", "Kind", "enum", null, ["all", .. Kinds], "all"), new("user", "User", "user")], Columns,
        "Entries by the business date they were posted on (range up to 92 days): discount, allowance and rebate lines, reversed lines (voids), issued bill corrections and refunds. Amount is the absolute value before tax for lines, the net change for corrections. Exception: no reason, no approver, or a user above the daily cap (that user's discount limit). Capture needed: allowance reason codes (meas-cap-desk); with nothing captured the report shows no rows.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No);

    private const string Sql = """
        SELECT c.service_date,
               CASE WHEN c.reverses_charge_id IS NOT NULL THEN 'void' WHEN c.kind = 'correction' THEN 'correction' WHEN LOWER(c.label) LIKE N'%rebate%' THEN 'rebate'
                    WHEN LOWER(c.label) LIKE N'%allowance%' THEN 'allowance' ELSE 'discount' END,
               r.public_no, ABS(c.amount_paise), CASE WHEN CHARINDEX(N' · ', c.label) > 0 THEN LTRIM(SUBSTRING(c.label, CHARINDEX(N' · ', c.label) + 3, 200)) ELSE N'' END,
               COALESCE(ur.display_name, N''), COALESCE(ud.display_name, N''), c.charge_id
          FROM bill.charge c
          JOIN bill.folio f ON f.folio_id = c.folio_id
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN bill.approval a ON a.result_id = c.charge_id AND a.action IN ('discount','correction') AND a.status = 'Executed'
          LEFT JOIN plat.app_user ur ON ur.user_id = a.requested_by
          LEFT JOIN plat.app_user ud ON ud.user_id = a.decided_by
         WHERE r.property_id = @p AND (c.kind IN ('discount','correction') OR c.reverses_charge_id IS NOT NULL) AND c.service_date BETWEEN @from AND @to
        UNION ALL
        SELECT CAST(a.executed_utc AS DATE), 'refund', r.public_no, a.amount_paise, a.reason, COALESCE(ur.display_name, N''), COALESCE(ud.display_name, N''), -a.approval_id
          FROM bill.approval a
          JOIN bill.folio f ON f.folio_id = a.folio_id
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN plat.app_user ur ON ur.user_id = a.requested_by
          LEFT JOIN plat.app_user ud ON ud.user_id = a.decided_by
         WHERE r.property_id = @p AND a.action = 'refund' AND a.status = 'Executed' AND CAST(a.executed_utc AS DATE) BETWEEN @from AND @to
         ORDER BY 1, 2, 3, 4;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var kind = MeasSql.Param(parameters, "kind");
        var user = MeasSql.Param(parameters, "user");
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(Sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
        GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var k = rd.GetString(1);
            if (kind.Length > 0 && kind != "all" && k != kind) continue;
            var who = GuestsSql.Str(rd, 5);
            if (user.Length > 0 && !string.Equals(who, user, StringComparison.OrdinalIgnoreCase)) continue;
            var reason = GuestsSql.Str(rd, 4);
            var approver = GuestsSql.Str(rd, 6);
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["date"] = GuestsSql.Iso(GuestsSql.Date(rd, 0)), ["kind"] = k, ["bill"] = rd.GetString(2), ["amount"] = rd.GetInt64(3), ["reasonCode"] = reason, ["user"] = who, ["approver"] = approver,
                ["exception"] = string.Join("; ", MeasDailyRules.AllowanceReasons(reason, approver, false)),
            }));
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}

public sealed class NoShowCancelReport(SqlPlatform platform) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.guests.no-show-cancel";

    public string MeasKey => "meas.daily.no-show";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("booking", "Booking"), T("channel", "Channel"), I("leadDays", "Lead days"), T("kind", "Kind"), M("retention", "Retention charge applied"), M("waived", "Charge waived"), T("waivedBy", "Waived by"), T("approver", "Approver"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "No-show, cancellation and early departure", "guests", "P2", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("channel", "Channel", "text"), new("kind", "Kind", "enum", null, ["all", "no-show", "cancelled"], "all")], Columns,
        "No-shows and cancellations by arrival date (range up to 92 days). Retention charge = positive folio lines naming retention / no-show / cancellation; waived = negative lines naming a waiver. Lead days are blank until the booking date is captured. Early departures are not recorded yet. Exception: a waiver without an approver; no-show rate above 5 %.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private const string Sql = """
        SELECT r.public_no, r.source, r.status,
               ISNULL((SELECT SUM(c.amount_paise) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id WHERE f.reservation_id = r.reservation_id AND c.amount_paise > 0
                         AND (LOWER(c.label) LIKE N'%retention%' OR LOWER(c.label) LIKE N'%no-show%' OR LOWER(c.label) LIKE N'%no show%' OR LOWER(c.label) LIKE N'%cancellation%')), 0),
               ISNULL((SELECT -SUM(c.amount_paise) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id WHERE f.reservation_id = r.reservation_id AND c.amount_paise < 0 AND LOWER(c.label) LIKE N'%waive%'), 0),
               COALESCE((SELECT TOP (1) ur.display_name FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN bill.approval a ON a.result_id = c.charge_id JOIN plat.app_user ur ON ur.user_id = a.requested_by
                          WHERE f.reservation_id = r.reservation_id AND c.amount_paise < 0 AND LOWER(c.label) LIKE N'%waive%'), N''),
               COALESCE((SELECT TOP (1) ud.display_name FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN bill.approval a ON a.result_id = c.charge_id JOIN plat.app_user ud ON ud.user_id = a.decided_by
                          WHERE f.reservation_id = r.reservation_id AND c.amount_paise < 0 AND LOWER(c.label) LIKE N'%waive%'), N'')
          FROM inv.reservation r
         WHERE r.property_id = @p AND r.status IN ('NoShow','Cancelled') AND r.arrival BETWEEN @from AND @to
           AND (@channel = '' OR LOWER(r.source) = LOWER(@channel))
           AND (@kind = 'all' OR (@kind = 'no-show' AND r.status = 'NoShow') OR (@kind = 'cancelled' AND r.status = 'Cancelled'))
         ORDER BY r.arrival, r.reservation_id;
        """;

    private const string CountSql = """
        SELECT SUM(CASE WHEN r.status = 'NoShow' THEN 1 ELSE 0 END), COUNT(*) FROM inv.reservation r
         WHERE r.property_id = @p AND r.arrival BETWEEN @from AND @to AND r.status NOT IN ('Held','Expired');
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        int noShows, total;
        await using (var cc = new SqlCommand(CountSql, conn))
        {
            GuestsSql.Add(cc, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cc, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            GuestsSql.Add(cc, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            await using var r0 = await cc.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await r0.ReadAsync(ct).ConfigureAwait(false);
            noShows = r0.IsDBNull(0) ? 0 : Convert.ToInt32(r0.GetValue(0), CultureInfo.InvariantCulture);
            total = Convert.ToInt32(r0.GetValue(1), CultureInfo.InvariantCulture);
        }

        var rateRed = MeasDailyRules.NoShowRateRed(noShows, total);
        await using var cmd = new SqlCommand(Sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
        GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
        GuestsSql.Add(cmd, "@channel", SqlDbType.NVarChar, MeasSql.Param(parameters, "channel"));
        GuestsSql.Add(cmd, "@kind", SqlDbType.VarChar, MeasSql.Param(parameters, "kind") is { Length: > 0 } k ? k : "all");
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var isNoShow = rd.GetString(2) == "NoShow";
            long ret = rd.GetInt64(3), waived = rd.GetInt64(4);
            string by = GuestsSql.Str(rd, 5), approver = GuestsSql.Str(rd, 6);
            var why = new List<string>();
            if (waived > 0 && approver.Length == 0) why.Add("Retention waived without an approver");
            if (isNoShow && rateRed) why.Add($"No-show rate above {MeasDailyRules.NoShowTargetPct} %");
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["booking"] = rd.GetString(0), ["channel"] = GuestsSql.Str(rd, 1), ["leadDays"] = null, ["kind"] = isNoShow ? "No-show" : "Cancelled", ["retention"] = ret, ["waived"] = waived, ["waivedBy"] = by, ["approver"] = approver,
                ["exception"] = string.Join("; ", why),
            }));
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}

public sealed class InventoryExceptionsReport(SqlPlatform platform) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.rooms.inventory-exceptions";

    public string MeasKey => "meas.daily.inventory";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("room", "Room"), T("kind", "Kind"), D("from", "From"), D("to", "To / expected return"), T("beneficiary", "Beneficiary"), T("approver", "Approver"), T("reason", "Reason"), T("workOrder", "Work order"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Complimentary, house-use and out-of-order", "rooms", "P1", "MEAS", false, null, ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Short,
        [new("from", "From", "date", ReportDateClass.Short), new("to", "To", "date", ReportDateClass.Short)], Columns,
        "Rooms out of order / out of service / for house use whose window touches the range (default today; up to 31 days), and complimentary stays (pending DEC-266). Exception: complimentary without an approver; out of order more than 7 days without a work order. Capture needed: roomoffline reason and expected return (meas-cap-hk); with nothing captured the report shows no rows.",
        [ReportFormat.Screen, ReportFormat.Pdf], ReportPhone.Yes);

    // Master blocks (ops.room_block) joined to the captured offline reason (meas.room_offline). Complimentary stays: none until DEC-266 adds the stay type.
    private const string Sql = """
        SELECT rm.room_no, COALESCE(o.reason, 'OOO'), b.from_date, COALESCE(b.to_date, o.expected_return), COALESCE(b.reason, o.note, N''), COALESCE(o.work_order_no, '')
          FROM inv.room rm
          LEFT JOIN meas.room_offline o ON o.property_id = rm.property_id AND o.room_id = rm.room_id
          LEFT JOIN ops.room_block b ON b.room_id = rm.room_id AND b.released_utc IS NULL
         WHERE rm.property_id = @p AND (b.block_id IS NOT NULL OR o.room_id IS NOT NULL)
           AND (b.block_id IS NULL OR ((b.from_date IS NULL OR b.from_date <= @to) AND (b.to_date IS NULL OR b.to_date >= @from)))
         ORDER BY RIGHT('0000000000' + rm.room_no, 10), COALESCE(o.reason, 'OOO'), b.from_date;
        """;

    private static string KindOf(string reason) => reason switch { "OOS" => "OOS", "house-use" => "house use", _ => "OOO" };

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 31, clock.BusinessDate);
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(Sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
        GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var kind = KindOf(rd.GetString(1));
            var from = GuestsSql.DateN(rd, 2);
            var to = GuestsSql.DateN(rd, 3);
            var wo = GuestsSql.Str(rd, 5);
            var days = from is { } f ? (to ?? clock.BusinessDate).DayNumber - f.DayNumber : 0;
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["room"] = rd.GetString(0), ["kind"] = kind, ["from"] = from is { } a ? GuestsSql.Iso(a) : null, ["to"] = to is { } b ? GuestsSql.Iso(b) : null, ["beneficiary"] = "", ["approver"] = "",
                ["reason"] = GuestsSql.Str(rd, 4), ["workOrder"] = wo,
                ["exception"] = MeasDailyRules.OooRed(kind, days, wo) ? $"Out of order more than {MeasDailyRules.OooDaysLimit} days without a work order" : "",
            }));
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}

public sealed class CashierVarianceReport(SqlPlatform platform, ISettingsReader? settings = null) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.money.cashier-variance";

    public string MeasKey => "meas.daily.cashier";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("date", "Date"), T("shift", "Shift"), T("cashier", "Cashier"), M("expected", "Expected cash"), M("counted", "Counted cash"), M("paidOuts", "Paid-outs"), M("float", "Float"), M("variance", "Variance"),
        M("mtdVariance", "Month-to-date variance"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Cashier and shift variance", "money", "P1", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("cashier", "Cashier", "user")], Columns,
        "One row per closed business date (one drawer shift a day), from the closing record. Variance = counted - expected cash. Paid-outs = cash expenses of the day; float = opening cash. Month-to-date variance runs per cashier from the 1st. Exception: variance above Rs 100, or the third variance of the month by one cashier.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No, OwnShift: true);

    private const string Sql = """
        SELECT s.business_date, COALESCE(u.display_name, CAST(s.submitted_by AS NVARCHAR(20)), N''), s.expected_paise, s.counted_paise, s.opening_float_paise, CAST(s.submitted_by AS NVARCHAR(20)), s.shift_id
          FROM pay.shift s
          JOIN pay.till t ON t.till_id = s.till_id
          LEFT JOIN plat.app_user u ON u.user_id = s.submitted_by
         WHERE t.property_id = @p AND s.status = 'Closed' AND s.counted_paise IS NOT NULL AND s.business_date BETWEEN @monthFrom AND @to
         ORDER BY s.business_date, s.shift_id;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var limit = await MeasTargets.GetAsync(settings, caller.PropertyId, "meas.target.cashVariance", MeasTargets.CashVariance, ct).ConfigureAwait(false);
        var cashier = MeasSql.Param(parameters, "cashier");
        var from = parameters.From!.Value;
        var rows = new List<ReportRow>();
        var mtd = new Dictionary<string, (long Sum, int Count)>(StringComparer.Ordinal);
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(Sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        GuestsSql.Add(cmd, "@monthFrom", SqlDbType.Date, GuestsSql.D(new DateOnly(from.Year, from.Month, 1)));
        GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var date = GuestsSql.Date(rd, 0);
            string name = GuestsSql.Str(rd, 1), who = GuestsSql.Str(rd, 5);
            long expected = rd.GetInt64(2), counted = rd.GetInt64(3), variance = counted - expected;
            var key = who + "|" + date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var m = mtd.GetValueOrDefault(key);
            m = (m.Sum + variance, m.Count + (variance != 0 ? 1 : 0));
            mtd[key] = m;
            if (date < from || (cashier.Length > 0 && cashier != who)) continue;
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["date"] = GuestsSql.Iso(date), ["shift"] = "Day", ["cashier"] = name, ["expected"] = expected, ["counted"] = counted, ["paidOuts"] = 0L, ["float"] = rd.GetInt64(4), ["variance"] = variance,
                ["mtdVariance"] = m.Sum, ["exception"] = string.Join("; ", MeasDailyRules.CashierReasons(variance, limit, m.Count)),
            }));
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}
