// F7-R3 · shared pieces of the guests + rooms selectors: property clock (business date, time zone), SQL fragments (folio balance, late-charge, bill number),
// text rules that mirror the prototype (03 Prototype/src/domain/reports/guests/shared.ts). The ID number never leaves SQL in full: only id_last4 is read.
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Domain.Dates;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.Guests;

internal static partial class GuestsSql
{
    public const string LateChargeLabel = "Late check-out · half day";

    /// <summary>Folio balance (net + tax − paid − credit) of folio <c>f</c>; the same arithmetic as FolioTotals / the revenue bridge.</summary>
    public const string BalanceExpr = """
        (ISNULL((SELECT SUM(c.amount_paise + c.tax_paise) FROM bill.charge c WHERE c.folio_id IN (SELECT y.folio_id FROM bill.folio y WHERE y.reservation_id = f.reservation_id)), 0)
         - ISNULL((SELECT SUM(CASE WHEN x.kind IN ('payment','deposit-apply','company-credit','ota-credit','guest-credit') THEN x.amount_paise WHEN x.kind = 'refund' THEN -x.amount_paise ELSE 0 END)
                     FROM pay.receipt x WHERE x.folio_id IN (SELECT y.folio_id FROM bill.folio y WHERE y.reservation_id = f.reservation_id)), 0))
        """;

    public const string LateChargeExpr = "ISNULL((SELECT SUM(c.amount_paise + c.tax_paise) FROM bill.charge c WHERE c.folio_id IN (SELECT y.folio_id FROM bill.folio y WHERE y.reservation_id = f.reservation_id) AND c.label = N'Late check-out · half day' AND c.reverses_charge_id IS NULL), 0)";

    public const string BillNoExpr = "(SELECT TOP (1) i.invoice_no FROM ivc.invoice i WHERE i.property_id = r.property_id AND i.source_kind = 'folio' AND i.source_id = f.folio_id ORDER BY i.revision DESC)";

    public sealed record Clock(DateOnly BusinessDate, TimeZoneInfo Zone);

    public static void Add(SqlCommand c, string name, SqlDbType type, object? value) => c.Parameters.Add(name, type).Value = value ?? DBNull.Value;

    public static object D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public static DateOnly Date(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    public static DateOnly? DateN(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateOnly.FromDateTime(rd.GetDateTime(i));

    public static string Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? "" : Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture) ?? "";

    public static async Task<Clock> ClockAsync(SqlConnectionFactory connections, int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT business_date, time_zone FROM inv.property WHERE property_id = @p;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        return new Clock(Date(rd, 0), CalendarDate.AssertTimeZone(rd.GetString(1)));
    }

    /// <summary>Property-local <c>yyyy-MM-dd HH:mm</c> of a UTC instant (empty when null).</summary>
    public static string Local(Clock clock, DateTime? utc) =>
        utc is null ? "" : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), clock.Zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Selector-side range guard (the engine's class limits are wider than these entries').</summary>
    public static void RangeGuard(DateOnly? from, DateOnly? to, int maxDays, DateOnly today, int lookAhead = 0)
    {
        if (from is { } f && to is { } t && t.DayNumber - f.DayNumber + 1 > maxDays) throw HccException.Validation($"The range is longer than {maxDays} days. Narrow the range.");
        if (to is { } t2 && t2 > today.AddDays(lookAhead))
            throw HccException.Validation(lookAhead > 0 ? $"This report can look at most {lookAhead} days ahead of the business date ({Iso(today)})." : $"The report cannot run for a date after the business date ({Iso(today)}).");
    }

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parens();

    /// <summary>"City" of an address: the segment before the state (<c>10 Demo Street, Bengaluru, Karnataka (fictional)</c> → <c>Bengaluru</c>).</summary>
    public static string CityOfAddress(string? address)
    {
        var parts = Parens().Replace(address ?? "", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 3 ? parts[^2] : parts.Length == 2 ? parts[0] : "";
    }

    /// <summary>The visa number is personal data: reports show the last four only.</summary>
    public static string VisaMasked(string v) => v.Length <= 4 ? "••••" : "••••" + v[^4..];

    /// <summary>Movement-register status group (prototype <c>movementStatus</c>).</summary>
    public static string MovementStatus(string reservationStatus) => reservationStatus switch
    {
        "InHouse" => "checked in", "Departed" => "checked out", "Cancelled" or "Expired" => "cancelled", "NoShow" => "no-show", _ => "booked",
    };
}
