// F12-RPT-DAILY · server twins of the first five daily-control reports: manager flash, regrets and denials, rate variance, room discrepancy, high balance.
// Column keys, types, money flags, parameters and exception rules equal the prototype (03 Prototype/src/domain/reports/meas-daily/*). Rules: MeasDailyRules.
using System.Data;
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.MeasDaily.MeasCols;

namespace Hcc.Reporting.Reports.MeasDaily;

public sealed class ManagerFlashReport(SqlPlatform platform, ISettingsReader? settings = null) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.guests.manager-flash";

    public string MeasKey => "meas.daily.flash";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("kpi", "KPI"), P("pctActual", "Actual %"), P("pctTarget", "Target %"), P("pctLast", "Last year %"), P("pctVar", "Variance %"),
        M("amtActual", "Actual"), M("amtTarget", "Target"), M("amtLast", "Last year"), M("amtVar", "Variance"),
        I("cntActual", "Actual (count)"), I("cntTarget", "Target (count)"), I("cntLast", "Last year (count)"), I("cntVar", "Variance (count)"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Manager's flash", "guests", "P1", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Day,
        [new("from", "Date", "date", ReportDateClass.Day), new("compare", "Compare with", "enum", null, ["budget", "last year"], "budget")], Columns,
        "One business date. Occupancy = rooms slept / rooms in service; ADR = room charge before GST / rooms slept; RevPAR = room charge / rooms in service. Targets are meas.target.* (budget); last year = the date 365 days earlier. Exception: a KPI with a target more than 10 % below it. Comps are pending DEC-266.",
        [ReportFormat.Screen, ReportFormat.Pdf], ReportPhone.Yes);

    private sealed record Day(int Occupied, int Sell, long RoomRev, int Arrivals, int Departures, int NoShows, int Ooo);

    private static async Task<Day> DayAsync(SqlConnection conn, int p, DateOnly d, CancellationToken ct)
    {
        var (_, sell) = await MeasSql.OccAsync(conn, p, d, ct).ConfigureAwait(false);
        const string sql = """
            SELECT (SELECT COUNT(*) FROM inv.reservation r WHERE r.property_id = @p AND r.status IN ('InHouse','Departed') AND r.arrival <= @d AND r.departure > @d),
                   (SELECT ISNULL(SUM(c.amount_paise), 0) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
                     WHERE r.property_id = @p AND c.kind = 'room' AND c.service_date = @d AND c.reverses_charge_id IS NULL),
                   (SELECT COUNT(*) FROM inv.reservation r WHERE r.property_id = @p AND r.status IN ('Confirmed','InHouse','Departed') AND r.arrival = @d),
                   (SELECT COUNT(*) FROM inv.reservation r WHERE r.property_id = @p AND r.status IN ('InHouse','Departed') AND r.departure = @d),
                   (SELECT COUNT(*) FROM inv.reservation r WHERE r.property_id = @p AND r.status = 'NoShow' AND r.arrival = @d),
                   (SELECT COUNT(DISTINCT b.room_id) FROM ops.room_block b JOIN inv.room rm ON rm.room_id = b.room_id WHERE rm.property_id = @p AND b.released_utc IS NULL AND (b.from_date IS NULL OR b.from_date <= @d) AND (b.to_date IS NULL OR b.to_date > @d));
            """;
        await using var cmd = new SqlCommand(sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, p);
        GuestsSql.Add(cmd, "@d", SqlDbType.Date, GuestsSql.D(d));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return new Day(Convert.ToInt32(rd.GetValue(0), CultureInfo.InvariantCulture), sell, Convert.ToInt64(rd.GetValue(1), CultureInfo.InvariantCulture), Convert.ToInt32(rd.GetValue(2), CultureInfo.InvariantCulture),
            Convert.ToInt32(rd.GetValue(3), CultureInfo.InvariantCulture), Convert.ToInt32(rd.GetValue(4), CultureInfo.InvariantCulture), Convert.ToInt32(rd.GetValue(5), CultureInfo.InvariantCulture));
    }

    private static double Pct1(long n, long d) => d > 0 ? Math.Round(n * 1000.0 / d, MidpointRounding.AwayFromZero) / 10 : 0;

    private static long DivRound(long n, long d) => d > 0 ? (long)Math.Round((double)n / d, MidpointRounding.AwayFromZero) : 0;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var date = parameters.From!.Value;
        var lastYear = date.AddDays(-365);
        var compareLast = MeasSql.Param(parameters, "compare") == "last year";
        var tOcc = await MeasTargets.GetAsync(settings, caller.PropertyId, "meas.target.occupancy", MeasTargets.Occupancy, ct).ConfigureAwait(false);
        var tAdr = await MeasTargets.GetAsync(settings, caller.PropertyId, "meas.target.adr", MeasTargets.Adr, ct).ConfigureAwait(false);
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        var f = await DayAsync(conn, caller.PropertyId, date, ct).ConfigureAwait(false);
        var ly = await DayAsync(conn, caller.PropertyId, lastYear, ct).ConfigureAwait(false);
        var rows = new List<ReportRow>();

        void Add(string kpi, string px, double? actual, double? target, double? last)
        {
            var baseV = compareLast ? last : target;
            var red = actual is not null && target is not null && px != "cnt" && MeasDailyRules.FlashRed((long)Math.Round(actual.Value * (px == "pct" ? 10 : 1), MidpointRounding.AwayFromZero), (long?)Math.Round(target.Value * (px == "pct" ? 10 : 1), MidpointRounding.AwayFromZero));
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kpi"] = kpi, [px + "Actual"] = actual is null ? null : px == "pct" ? actual : (long)actual, [px + "Target"] = target is null ? null : px == "pct" ? target : (long)target,
                [px + "Last"] = last is null ? null : px == "pct" ? last : (long)last,
                [px + "Var"] = actual is not null && baseV is not null ? Math.Round(actual.Value - baseV.Value, 1, MidpointRounding.AwayFromZero) : null,
                ["exception"] = red ? $"{kpi} more than 10 % below target" : "",
            }));
        }

        var tRev = Math.Round(tOcc * tAdr / 100.0, MidpointRounding.AwayFromZero);
        Add("Occupancy %", "pct", f.Sell > 0 ? Pct1(f.Occupied, f.Sell) : null, tOcc, ly.Sell > 0 ? Pct1(ly.Occupied, ly.Sell) : null);
        Add("ADR", "amt", f.Occupied > 0 ? DivRound(f.RoomRev, f.Occupied) : null, tAdr, ly.Occupied > 0 ? DivRound(ly.RoomRev, ly.Occupied) : null);
        Add("RevPAR", "amt", f.Sell > 0 && f.Occupied > 0 ? DivRound(f.RoomRev, f.Sell) : null, tRev, ly.Sell > 0 && ly.Occupied > 0 ? DivRound(ly.RoomRev, ly.Sell) : null);
        Add("Arrivals", "cnt", f.Arrivals, null, ly.Arrivals);
        Add("Departures", "cnt", f.Departures, null, ly.Departures);
        Add("In-house", "cnt", f.Occupied, null, ly.Occupied);
        Add("No-shows", "cnt", f.NoShows, null, ly.NoShows);
        Add("Comps (pending DEC-266)", "cnt", 0, null, 0);
        Add("OOO", "cnt", f.Ooo, null, ly.Ooo);
        foreach (var k in new[] { 7, 14, 30 })
        {
            var (occ, _) = await MeasSql.OccAsync(conn, caller.PropertyId, date.AddDays(k), ct).ConfigureAwait(false);
            Add($"Rooms on books +{k}", "cnt", occ, null, null);
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}

public sealed class RegretsDenialsReport(SqlPlatform platform) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.guests.regrets-denials";

    public string MeasKey => "meas.daily.regrets";

    private static readonly string[] Reasons = ["full", "price", "dates", "room-type", "policy", "other"];

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["full"] = "No room free", ["price"] = "Price too high", ["dates"] = "Dates did not suit", ["room-type"] = "Room type not available", ["policy"] = "Policy (ID, payment, group)", ["other"] = "Other",
    };

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("date", "Date"), T("reason", "Reason"), T("channel", "Channel"), I("leadDays", "Lead days"), T("roomType", "Room type"), I("nightsLost", "Nights lost"), I("roomsLost", "Rooms lost"),
        M("quotedRate", "Quoted rate"), T("convertedLater", "Converted later"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Regrets and denials", "guests", "P1", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("reason", "Reason", "enum", null, ["all", .. Reasons], "all"), new("channel", "Channel", "text"), new("roomType", "Room type", "text")],
        Columns,
        "Turn-aways by the business date they were logged (range up to 92 days). Lead days = days from the log date to the wanted arrival. Quoted rate is money (hidden without the money gate). Exception: more than 3 price regrets for one arrival night whose occupancy is under 60 %. Capture needed: res.turnaway log (meas-cap-desk); with nothing captured the report shows no rows.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private const string Sql = """
        SELECT t.business_date, t.reason, t.channel, t.lead_days, t.room_type, t.nights, t.rooms, t.quoted_rate_paise, t.arrival,
               (SELECT COUNT(*) FROM meas.turnaway x WHERE x.property_id = t.property_id AND x.arrival = t.arrival AND x.reason = 'price')
          FROM meas.turnaway t
         WHERE t.property_id = @p AND t.business_date BETWEEN @from AND @to
           AND (@reason = 'all' OR t.reason = @reason) AND (@channel = '' OR LOWER(t.channel) = LOWER(@channel)) AND (@type = '' OR LOWER(t.room_type) = LOWER(@type))
         ORDER BY t.recorded_utc, t.turnaway_id;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        var raw = new List<(DateOnly Date, string Reason, string Channel, int Lead, string Type, int Nights, int Rooms, long? Rate, DateOnly Arrival, int PriceOnNight)>();
        await using (var cmd = new SqlCommand(Sql, conn))
        {
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            GuestsSql.Add(cmd, "@reason", SqlDbType.VarChar, MeasSql.Param(parameters, "reason") is { Length: > 0 } r ? r : "all");
            GuestsSql.Add(cmd, "@channel", SqlDbType.NVarChar, MeasSql.Param(parameters, "channel"));
            GuestsSql.Add(cmd, "@type", SqlDbType.NVarChar, MeasSql.Param(parameters, "roomType"));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                raw.Add((GuestsSql.Date(rd, 0), rd.GetString(1), rd.GetString(2), Convert.ToInt32(rd.GetValue(3), CultureInfo.InvariantCulture), rd.GetString(4), Convert.ToInt32(rd.GetValue(5), CultureInfo.InvariantCulture),
                    Convert.ToInt32(rd.GetValue(6), CultureInfo.InvariantCulture), rd.IsDBNull(7) ? null : rd.GetInt64(7), GuestsSql.Date(rd, 8), Convert.ToInt32(rd.GetValue(9), CultureInfo.InvariantCulture)));
        }

        foreach (var x in raw)
        {
            var red = false;
            if (x.Reason == "price" && x.PriceOnNight > MeasDailyRules.RegretsPriceLimit)
            {
                var (occ, sell) = await MeasSql.OccAsync(conn, caller.PropertyId, x.Arrival, ct).ConfigureAwait(false);
                red = MeasDailyRules.RegretsRed(x.PriceOnNight, occ, sell);
            }

            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["date"] = GuestsSql.Iso(x.Date), ["reason"] = Labels.GetValueOrDefault(x.Reason, x.Reason), ["channel"] = x.Channel, ["leadDays"] = (long)x.Lead, ["roomType"] = x.Type, ["nightsLost"] = (long)x.Nights,
                ["roomsLost"] = (long)x.Rooms, ["quotedRate"] = x.Rate, ["convertedLater"] = "No",
                ["exception"] = red ? $"More than {MeasDailyRules.RegretsPriceLimit} price regrets on {GuestsSql.Iso(x.Arrival)}, a night under {MeasDailyRules.RegretsOccupancyLimitPct} % occupancy" : "",
            }));
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}

public sealed class RateVarianceReport(SqlPlatform platform) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.guests.rate-variance";

    public string MeasKey => "meas.daily.rate-variance";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("booking", "Booking"), T("guest", "Guest", ReportMask.GuestName), T("room", "Room"), M("cardRate", "Card rate"), M("chargedRate", "Charged rate"), M("difference", "Difference"), P("differencePct", "Difference %"),
        T("reasonCode", "Reason code"), T("user", "User"), T("approver", "Approver"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Rate variance / rate check", "guests", "P1", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("roomType", "Room type", "text"), new("user", "User", "user")], Columns,
        "Stay rate changes by the business date they were made on (range up to 92 days). Card rate = the tariff at the change; difference = charged - card; difference % of the card rate. Exception: no reason, or the new rate is below the agreed-rate floor set by the CTL-01 threshold (default 15 %).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private const string Sql = """
        SELECT r.public_no, COALESCE(g.full_name, r.guest_name), rm.room_no, c.tariff_paise, c.old_rate_paise, c.new_rate_paise, c.agreed_rate_paise, c.threshold_bps, c.reason,
               COALESCE(uc.display_name, CAST(c.changed_by AS NVARCHAR(20))), COALESCE(ua.display_name, CASE WHEN c.approved_by IS NULL THEN N'' ELSE CAST(c.approved_by AS NVARCHAR(20)) END), c.change_id
          FROM gst.stay_rate_change c
          JOIN inv.reservation r ON r.reservation_id = c.reservation_id
          JOIN inv.room rm ON rm.room_id = c.room_id
          LEFT JOIN inv.room_type rt ON rt.room_type_id = rm.room_type_id
          LEFT JOIN gst.stay s ON s.stay_id = c.stay_id
          LEFT JOIN gst.guest g ON g.guest_id = s.guest_id
          LEFT JOIN plat.app_user uc ON uc.user_id = c.changed_by
          LEFT JOIN plat.app_user ua ON ua.user_id = c.approved_by
         WHERE c.property_id = @p AND c.business_date BETWEEN @from AND @to
           AND (@type = '' OR LOWER(COALESCE(rt.name, rt.code)) = LOWER(@type))
           AND (@by = '' OR CAST(c.changed_by AS NVARCHAR(20)) = @by OR LOWER(uc.login_name) = LOWER(@by))
         ORDER BY c.changed_utc, c.change_id;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(Sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
        GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
        GuestsSql.Add(cmd, "@type", SqlDbType.NVarChar, MeasSql.Param(parameters, "roomType"));
        GuestsSql.Add(cmd, "@by", SqlDbType.NVarChar, MeasSql.Param(parameters, "user"));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            long oldRate = rd.GetInt64(4), newRate = rd.GetInt64(5);
            var card = rd.IsDBNull(3) ? oldRate : rd.GetInt64(3);
            var agreed = rd.IsDBNull(6) ? oldRate : rd.GetInt64(6);
            var bps = rd.IsDBNull(7) ? 1500 : Convert.ToInt32(rd.GetValue(7), CultureInfo.InvariantCulture);
            var reason = rd.GetString(8);
            var diff = newRate - card;
            var why = string.IsNullOrWhiteSpace(reason) ? "No reason recorded" : MeasDailyRules.RateBelowFloor(agreed, newRate, bps) ? $"Below the agreed-rate floor ({bps / 100.0:0.##} % threshold)" : "";
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["booking"] = rd.GetString(0), ["guest"] = GuestsSql.Str(rd, 1), ["room"] = rd.GetString(2), ["cardRate"] = card, ["chargedRate"] = newRate, ["difference"] = diff,
                ["differencePct"] = card > 0 ? Math.Round(diff * 1000.0 / card, MidpointRounding.AwayFromZero) / 10 : 0.0, ["reasonCode"] = reason, ["user"] = GuestsSql.Str(rd, 9), ["approver"] = GuestsSql.Str(rd, 10), ["exception"] = why,
            }));
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}

public sealed class RoomDiscrepancyReport(SqlPlatform platform) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.rooms.room-discrepancy";

    public string MeasKey => "meas.daily.discrepancy";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("room", "Room"), T("foStatus", "Front-office status"), T("hkStatus", "Housekeeping status"), T("kind", "Mismatch kind"), T("since", "Since"), I("ageHours", "Age hours"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Room discrepancy", "rooms", "P1", "MEAS", false, null, ["PERM-HK-001"], [], "PERM-RPT-003", ReportDateClass.Now,
        [new("at", "As-of time (YYYY-MM-DDTHH:mm, default now)", "text")], Columns,
        "The state now (the property clock) unless an as-of time is given. Mismatch kinds: sold-but-vacant, vacant-but-occupied, OOO-with-stay (sleep-out has no data source yet). Exception: a mismatch older than 2 hours.",
        [ReportFormat.Screen, ReportFormat.Pdf], ReportPhone.Yes);

    // sold-but-vacant: a confirmed reservation whose arrival day is today or earlier and whose check-in time (@ci) has passed, not checked in.
    // OOO-with-stay: an in-house stay in a room with an active block or an OOO / OOS offline reason.
    private const string Sql = """
        SELECT rm.room_no, 'OOO-with-stay', 'Occupied', 'Out of order', COALESCE(CAST(blk.from_date AS DATETIME2), r.created_utc), rm.hk_status
          FROM inv.room rm
          JOIN inv.reservation r ON r.property_id = rm.property_id AND r.status = 'InHouse'
          JOIN gst.stay_segment sg ON sg.room_id = rm.room_id AND sg.to_date IS NULL
          JOIN gst.stay s ON s.stay_id = sg.stay_id AND s.reservation_id = r.reservation_id AND s.status = 'InHouse'
          OUTER APPLY (SELECT TOP (1) b.from_date FROM ops.room_block b WHERE b.room_id = rm.room_id AND b.released_utc IS NULL AND (b.from_date IS NULL OR b.from_date <= @bd) AND (b.to_date IS NULL OR b.to_date > @bd)) blk
         WHERE rm.property_id = @p AND rm.active = 1
           AND (blk.from_date IS NOT NULL OR EXISTS (SELECT 1 FROM ops.room_block b2 WHERE b2.room_id = rm.room_id AND b2.released_utc IS NULL AND b2.from_date IS NULL)
                OR EXISTS (SELECT 1 FROM meas.room_offline o WHERE o.property_id = rm.property_id AND o.room_id = rm.room_id AND o.reason IN ('OOO','OOS')))
        UNION ALL
        SELECT rm.room_no, 'sold-but-vacant', 'Reserved, arrival due', rm.hk_status, DATEADD(MINUTE, DATEDIFF(MINUTE, 0, CAST(@ci AS TIME)), CAST(r.arrival AS DATETIME2)), rm.hk_status
          FROM inv.reservation r
          JOIN inv.room_night n ON n.reservation_id = r.reservation_id AND n.night = r.arrival
          JOIN inv.room rm ON rm.room_id = n.room_id
         WHERE r.property_id = @p AND r.status = 'Confirmed' AND r.arrival <= @bd AND DATEADD(MINUTE, DATEDIFF(MINUTE, 0, CAST(@ci AS TIME)), CAST(r.arrival AS DATETIME2)) < @now;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        var at = MeasSql.Param(parameters, "at");
        var now = DateTime.TryParseExact(at, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, clock.Zone);
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(Sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        GuestsSql.Add(cmd, "@bd", SqlDbType.Date, GuestsSql.D(DateOnly.FromDateTime(now)));
        GuestsSql.Add(cmd, "@now", SqlDbType.DateTime2, now);
        GuestsSql.Add(cmd, "@ci", SqlDbType.VarChar, "12:00");
        var list = new List<(string Room, string Kind, string Fo, string Hk, DateTime Since)>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add((rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetDateTime(4)));
        }

        foreach (var x in list.OrderBy(x => x.Room, StringComparer.Ordinal))
        {
            var mins = (int)Math.Max(0, Math.Floor((now - x.Since).TotalMinutes));
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["room"] = x.Room, ["foStatus"] = x.Fo, ["hkStatus"] = x.Hk, ["kind"] = x.Kind, ["since"] = x.Since.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), ["ageHours"] = (long)(mins / 60),
                ["exception"] = MeasDailyRules.DiscrepancyRed(mins) ? $"{x.Kind} for more than 2 hours" : "",
            }));
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}

public sealed class HighBalanceReport(SqlPlatform platform, ISettingsReader? settings = null) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.money.high-balance";

    public string MeasKey => "meas.daily.high-balance";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("room", "Room"), T("guest", "Guest", ReportMask.GuestName), T("company", "Company"), I("daysInHouse", "Days in house"), M("balance", "Balance"), M("deposit", "Deposit"), M("limit", "Limit"), M("overBy", "Over by"), Exc(),
    ];

    public ReportDefinition Definition { get; } = new(Id, "High-balance and credit-limit", "money", "P1", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Now,
        [new("threshold", "Threshold in rupees (default meas.target.highBalance)", "text")], Columns,
        "In-house stays with an unpaid balance now (GST included). Days in house = nights since arrival. Over by = balance above deposit plus credit limit (company account) or above the threshold (no company). Exception: over the limit or threshold, or a balance older than 2 nights.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    private static readonly string Sql = $$"""
        SELECT rm.room_no, COALESCE(g.full_name, r.guest_name), COALESCE(co.name, N''), CASE WHEN r.company_id IS NULL THEN 0 ELSE 1 END,
               DATEDIFF(DAY, r.arrival, @bd), {{GuestsSql.BalanceExpr}},
               ISNULL((SELECT SUM(CASE WHEN x.kind = 'deposit' THEN x.amount_paise WHEN x.kind = 'deposit-return' THEN -x.amount_paise ELSE 0 END) FROM pay.receipt x WHERE x.folio_id = f.folio_id), 0),
               ISNULL(co.credit_limit_paise, 0)
          FROM inv.reservation r
          JOIN bill.folio f ON f.reservation_id = r.reservation_id
          JOIN gst.stay s ON s.reservation_id = r.reservation_id AND s.status = 'InHouse'
          JOIN gst.stay_segment sg ON sg.stay_id = s.stay_id AND sg.to_date IS NULL
          JOIN inv.room rm ON rm.room_id = sg.room_id
          JOIN gst.guest g ON g.guest_id = s.guest_id
          LEFT JOIN grp.company_account co ON co.company_id = r.company_id
         WHERE r.property_id = @p AND r.status = 'InHouse'
         ORDER BY RIGHT('0000000000' + rm.room_no, 10), r.reservation_id;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        var threshold = MeasTargets.HighBalance;
        var given = MeasSql.Param(parameters, "threshold").Replace(",", "").Trim();
        if (given.Length > 0 && decimal.TryParse(given, NumberStyles.Number, CultureInfo.InvariantCulture, out var rupees) && rupees >= 0) threshold = (long)Math.Round(rupees * 100, MidpointRounding.AwayFromZero);
        else threshold = await MeasTargets.GetAsync(settings, caller.PropertyId, "meas.target.highBalance", MeasTargets.HighBalance, ct).ConfigureAwait(false);
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(Sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        GuestsSql.Add(cmd, "@bd", SqlDbType.Date, GuestsSql.D(clock.BusinessDate));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var hasCo = Convert.ToInt32(rd.GetValue(3), CultureInfo.InvariantCulture) == 1;
            var days = Math.Max(0, Convert.ToInt32(rd.GetValue(4), CultureInfo.InvariantCulture));
            long balance = rd.GetInt64(5), deposit = rd.GetInt64(6), limit = rd.GetInt64(7);
            if (balance <= 0) continue;
            var allowed = hasCo ? deposit + limit : threshold;
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["room"] = rd.GetString(0), ["guest"] = GuestsSql.Str(rd, 1), ["company"] = GuestsSql.Str(rd, 2), ["daysInHouse"] = (long)days, ["balance"] = balance, ["deposit"] = deposit, ["limit"] = limit,
                ["overBy"] = Math.Max(0, balance - allowed), ["exception"] = string.Join("; ", MeasDailyRules.HighBalanceReasons(hasCo, balance, deposit, limit, threshold, days)),
            }));
        }

        return MeasCols.Result(Id, Definition, parameters, rows);
    }
}
