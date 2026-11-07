// F12-RPT-GUEST · rpt.guests.repeat-loyalty — Repeat guests and loyalty base (gst.stay, gst.guest, msg.message, meas.customer_preference; switch meas.guest.repeat). Twin of meas-guest/repeat-loyalty.ts.
// Customers are grouped by the last 10 digits of the mobile (else the lower-cased name). The revenue-share column is money-flagged (PERM-RPT-002). The 3+ stay list shows the name (GuestName mask, rule V).
using Hcc.Application.Billing;
using System.Data;
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasGuest;

public sealed class RepeatLoyaltyReport(SqlPlatform platform, ISettingsReader? settings = null) : MeasGuestBase(platform, settings)
{
    public const string Id = "rpt.guests.repeat-loyalty";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MeasGuestRules.T("month", "Month"), MeasGuestRules.I("seenBefore", "Customers seen before"), MeasGuestRules.P("repeatShare", "Repeat share"), MeasGuestRules.I("stays", "Stays"),
        MeasGuestRules.P("revenueShare", "Revenue share", true), MeasGuestRules.T("guests3", "3+ stay guests", ReportMask.GuestName), MeasGuestRules.T("loyaltyTag", "Loyalty tag"), MeasGuestRules.B("offerSent", "Offer sent"),
    ];

    public override string MeasKey => "meas.guest.repeat";

    public override ReportDefinition Definition { get; } = new(Id, "Repeat guests and loyalty base", "guests", "P2", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month)],
        Columns, "Customers (by mobile) with a stay that started in the month. Seen before = a completed stay earlier than their first one in the month. Repeat share = seen before / customers in the month; revenue share = folio totals of those customers’ stays / all stays of the month. The list shows guests with three or more completed stays.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private sealed record Stay(string Key, string Name, string Arrival, long Total);

    private const string Sql = """
        SELECT RIGHT(REPLACE(REPLACE(REPLACE(g.phone, ' ', ''), '-', ''), '+', ''), 10), g.full_name, CONVERT(CHAR(10), st.arrival, 23),
               ISNULL((SELECT SUM(c.amount_paise + c.tax_paise) FROM bill.charge c WHERE c.folio_id = st.folio_id), 0)
          FROM gst.stay st JOIN gst.guest g ON g.guest_id = st.guest_id
         WHERE st.property_id = @p AND st.arrival < @end;
        """;

    private static string KeyOf(string phone10, string name) => phone10.Length >= 10 && phone10.All(char.IsDigit) ? phone10 : "n:" + name.Trim().ToLowerInvariant();

    private static (long Customers, long Before, double? Share, long Stays, double? RevShare) Figures(IReadOnlyList<Stay> all, string month)
    {
        long before = 0, stays = 0, rev = 0, revRepeat = 0;
        var seen = all.Where(s => s.Arrival[..7] == month).GroupBy(s => s.Key).ToList();
        foreach (var c in seen)
        {
            var first = c.Min(s => s.Arrival)!;
            var repeat = all.Any(s => s.Key == c.Key && string.CompareOrdinal(s.Arrival, first) < 0);
            var r = c.Sum(s => s.Total);
            stays += c.Count();
            rev += r;
            if (repeat) { before++; revRepeat += r; }
        }

        return (seen.Count, before, MeasGuestRules.Pct1(before, seen.Count), stays, MeasGuestRules.Pct1(revRepeat, rev));
    }

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(Platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        var month = Param(parameters, "month");
        if (month.Length == 0) month = GuestsSql.Iso(clock.BusinessDate)[..7];
        var all = new List<Stay>();
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        var offers = new HashSet<string>(StringComparer.Ordinal);
        await using (var conn = await Platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand(Sql, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@end", SqlDbType.Date, DateTime.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(1));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) all.Add(new Stay(KeyOf(GuestsSql.Str(rd, 0), rd.GetString(1)), rd.GetString(1), rd.GetString(2), rd.GetInt64(3)));
            }

            await using (var cmd = new SqlCommand("SELECT customer_key, loyalty_tag FROM meas.customer_preference WHERE property_id = @p AND loyalty_tag IS NOT NULL;", conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) tags[rd.GetString(0)] = rd.GetString(1);
            }

            await using (var cmd = new SqlCommand("""
                SELECT DISTINCT RIGHT(REPLACE(REPLACE(REPLACE(g.phone, ' ', ''), '-', ''), '+', ''), 10), g.full_name
                  FROM msg.message m JOIN gst.guest g ON g.guest_id = m.guest_id
                 WHERE m.property_id = @p AND m.template_code IS NULL AND m.blocked_reason IS NULL;
                """, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) offers.Add(KeyOf(GuestsSql.Str(rd, 0), rd.GetString(1)));
            }
        }

        var f = Figures(all, month);
        if (f.Customers == 0) return Result(parameters, [], "cust.loyaltyTag (meas-cap-guest)");
        // MEAS2-B (M1): the summary row is red when the repeat share falls two months running (twin of repeatFalling).
        var m1 = MeasGuestRules.PrevMonth(month);
        var falling = MeasGuestRules.RepeatFalling(f.Share, Figures(all, m1).Share, Figures(all, MeasGuestRules.PrevMonth(m1)).Share);
        var rows = new List<ReportRow>
        {
            Row(red: falling, cells: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["month"] = month, ["seenBefore"] = f.Before, ["repeatShare"] = f.Share, ["stays"] = f.Stays, ["revenueShare"] = f.RevShare, ["guests3"] = "", ["loyaltyTag"] = "", ["offerSent"] = null,
            }),
        };
        var loyal = all.GroupBy(s => s.Key).Where(g => g.Any(s => s.Arrival[..7] == month) && g.Count() >= 3)
            .Select(g => (Key: g.Key, Name: g.OrderByDescending(s => s.Arrival, StringComparer.Ordinal).First().Name, Count: g.Count()))
            .OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal);
        foreach (var c in loyal)
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["month"] = "", ["seenBefore"] = null, ["repeatShare"] = null, ["stays"] = (long)c.Count, ["revenueShare"] = null, ["guests3"] = c.Name, ["loyaltyTag"] = tags.GetValueOrDefault(c.Key, ""), ["offerSent"] = offers.Contains(c.Key),
            }));
        return Result(parameters, rows);
    }

    /// <summary>Red row: the repeat share falls two months running.</summary>
    public static bool Falling(IReadOnlyList<(string Key, string Arrival)> stays, string month)
    {
        var all = stays.Select(s => new Stay(s.Key, "", s.Arrival, 0)).ToList();
        var m1 = MeasGuestRules.PrevMonth(month);
        return MeasGuestRules.RepeatFalling(Figures(all, month).Share, Figures(all, m1).Share, Figures(all, MeasGuestRules.PrevMonth(m1)).Share);
    }
}
