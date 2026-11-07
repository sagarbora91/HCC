// F12-RPT-DAILY · shared pieces of the nine daily-control measurement reports (server twin of 03 Prototype/src/domain/reports/meas-daily/*).
// MeasDailyRules holds every exception rule as a pure static function (boundary-tested in Hcc.Meas.Tests); the report classes only load rows and call it.
using System.Data;
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Configuration;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasDaily;

internal static class MeasCols
{
    public static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    public static ReportColumn P(string key, string label) => new(key, label, ReportColumnType.Percent, false);

    public static ReportColumn D(string key, string label) => new(key, label, ReportColumnType.Date, false);

    public static ReportColumn Exc() => T("exception", "Exception");

    public static ReportRow Row(Dictionary<string, object?> cells) => new(ReportRowKind.Row, cells);

    public static ReportResult Result(string id, ReportDefinition def, BoundParams p, IReadOnlyList<ReportRow> rows) =>
        new(id, def.Title, p, def.Columns, rows, new ReportAbout(DateTimeOffset.MinValue, "", def.DateBasis, false, p.Legacy, p.From, p.To));
}

/// <summary>Targets (meas.target.*, M-9 owner defaults) and the OFF switch values the reports read; defaults apply when no reader / no stored value.</summary>
internal static class MeasTargets
{
    public const long Occupancy = 35;
    public const long Adr = 205000;
    public const long HighBalance = 500000;
    public const long CashVariance = 10000;

    public static async Task<long> GetAsync(ISettingsReader? reader, int propertyId, string key, long fallback, CancellationToken ct)
    {
        if (reader is null) return fallback;
        var v = await reader.GetAsync(propertyId, key, null, ct).ConfigureAwait(false);
        return v.Kind == ConfigValueKind.Number ? v.Number : fallback;
    }
}

/// <summary>Every exception rule of the daily-control pack as a pure function (catalogue 3.10; prototype twins are the *Red / *Rows functions).</summary>
public static class MeasDailyRules
{
    public const int RegretsPriceLimit = 3;
    public const int RegretsOccupancyLimitPct = 60;
    public const int DiscrepancyLimitMinutes = 120;
    public const int HighBalanceDays = 2;
    public const int OooDaysLimit = 7;
    public const int NoShowTargetPct = 5;
    public const int CashRepeatLimit = 3;

    /// <summary>A KPI with a target is red when it is more than 10 % below it (exactly 10 % below is not red).</summary>
    public static bool FlashRed(long actual, long? target) => target is > 0 && actual * 100 < target.Value * 90;

    /// <summary>More than 3 price regrets for one arrival night whose occupancy is under 60 %.</summary>
    public static bool RegretsRed(int priceRegretsOnNight, int occupied, int sellable) =>
        priceRegretsOnNight > RegretsPriceLimit && sellable > 0 && (long)occupied * 100 < (long)RegretsOccupancyLimitPct * sellable;

    /// <summary>Floor of the agreed rate: ceil(agreed * (10000 - bps) / 10000). A rate at the floor passes; one paisa below is red.</summary>
    public static long AgreedFloor(long agreed, int bps)
    {
        var f = Math.Min(10000, Math.Max(0, bps));
        return (agreed * (10000 - f) + 9999) / 10000;
    }

    public static bool RateBelowFloor(long agreed, long rate, int bps) => agreed > 0 && rate < AgreedFloor(agreed, bps);

    /// <summary>A mismatch older than 2 hours (120 minutes; exactly 2 h is not red).</summary>
    public static bool DiscrepancyRed(int ageMinutes) => ageMinutes > DiscrepancyLimitMinutes;

    /// <summary>High-balance reasons for one in-house stay (empty = not red).</summary>
    public static IReadOnlyList<string> HighBalanceReasons(bool hasCompany, long balance, long deposit, long limit, long threshold, int daysInHouse)
    {
        var r = new List<string>();
        if (hasCompany && balance > deposit + limit) r.Add("Balance above deposit plus credit limit");
        if (!hasCompany && balance > threshold) r.Add($"Walk-in balance above {(threshold / 100).ToString("N0", CultureInfo.GetCultureInfo("en-IN"))} rupees");
        if (daysInHouse > HighBalanceDays) r.Add("Balance older than 2 nights");
        return r;
    }

    /// <summary>OOO more than 7 days without a work order (exactly 7 is not red).</summary>
    public static bool OooRed(string kind, int days, string workOrder) => kind == "OOO" && days > OooDaysLimit && string.IsNullOrEmpty(workOrder);

    public static bool NoShowRateRed(int noShows, int total) => total > 0 && noShows * 100 > NoShowTargetPct * total;

    /// <summary>Allowance / void / correction entry: reasons it is red.</summary>
    public static IReadOnlyList<string> AllowanceReasons(string reason, string approver, bool userAboveDailyCap)
    {
        var r = new List<string>();
        if (string.IsNullOrWhiteSpace(reason)) r.Add("No reason");
        if (string.IsNullOrEmpty(approver)) r.Add("No approver");
        if (userAboveDailyCap) r.Add("User above the daily cap");
        return r;
    }

    /// <summary>Variance above the limit (exactly the limit is not red), or the third and later variance of the month by one cashier.</summary>
    public static IReadOnlyList<string> CashierReasons(long variance, long limit, int varianceCountThisMonth)
    {
        var r = new List<string>();
        if (Math.Abs(variance) > limit) r.Add("Variance above the cash-variance limit");
        if (variance != 0 && varianceCountThisMonth >= CashRepeatLimit) r.Add($"Variance number {varianceCountThisMonth} this month by this cashier");
        return r;
    }
}

internal static class MeasSql
{
    /// <summary>Rooms in service on a night (active rooms without a block covering it) and rooms on the books that night (confirmed, in-house, departed).</summary>
    public static async Task<(int Occ, int Sell)> OccAsync(SqlConnection conn, int propertyId, DateOnly d, CancellationToken ct)
    {
        const string sql = """
            SELECT (SELECT COUNT(*) FROM inv.reservation r WHERE r.property_id = @p AND r.status IN ('Confirmed','InHouse','Departed') AND r.arrival <= @d AND r.departure > @d),
                   (SELECT COUNT(*) FROM inv.room rm WHERE rm.property_id = @p AND rm.active = 1
                       AND NOT EXISTS (SELECT 1 FROM ops.room_block b WHERE b.room_id = rm.room_id AND b.released_utc IS NULL AND (b.from_date IS NULL OR b.from_date <= @d) AND (b.to_date IS NULL OR b.to_date > @d)));
            """;
        await using var cmd = new SqlCommand(sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, propertyId);
        GuestsSql.Add(cmd, "@d", SqlDbType.Date, GuestsSql.D(d));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (Convert.ToInt32(rd.GetValue(0), CultureInfo.InvariantCulture), Convert.ToInt32(rd.GetValue(1), CultureInfo.InvariantCulture));
    }

    public static string Param(BoundParams p, string key) => p.Values.GetValueOrDefault(key) ?? "";
}
