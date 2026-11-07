// F12-RPT-GUEST · rpt.guests.upsell — Upsell and upgrade (bill.charge labels UPG / LCO1 / LCO2, meas.booking_extra; switch meas.guest.upsell). Twin of meas-guest/upsell.ts.
// Revenue per arrival = upgrade + late check-out + breakfast add-on + extra bed lines (before GST) / arrivals, half up. Day-use cannot exist on the server (departure > arrival), so it is 0.
using Hcc.Application.Billing;
using System.Data;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasGuest;

public sealed class UpsellReport(SqlPlatform platform, ISettingsReader? settings = null) : MeasGuestBase(platform, settings)
{
    public const string Id = "rpt.guests.upsell";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MeasGuestRules.I("arrivals", "Arrivals"), MeasGuestRules.I("upgradesPaid", "Upgrades paid"), MeasGuestRules.I("upgradesFree", "Upgrades free"), MeasGuestRules.T("approver", "Approver"),
        MeasGuestRules.M("lateCheckout", "Late check-out charged"), MeasGuestRules.I("dayUse", "Day-use sold"), MeasGuestRules.M("breakfast", "Breakfast add-on"), MeasGuestRules.M("extraBed", "Extra bed"),
        MeasGuestRules.M("revenuePerArrival", "Revenue per arrival"),
    ];

    public override string MeasKey => "meas.guest.upsell";

    public override ReportDefinition Definition { get; } = new(Id, "Upsell and upgrade", "guests", "P2", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("deskUser", "Desk user", "user")],
        Columns, "Stays checked in within the range (up to 92 days), one summary row. Upgrades by the booked type kept at booking; late check-out = lines LCO1 / LCO2; day-use = arrival and departure on the same day; revenue per arrival = upsell lines before GST / arrivals.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private const string Sql = """
        SELECT st.reservation_id, COALESCE(x.upgrade, ''), COALESCE(au.display_name, ''),
               ISNULL((SELECT SUM(c.amount_paise) FROM bill.charge c WHERE c.folio_id = st.folio_id AND c.reverses_charge_id IS NULL AND c.label LIKE N'UPG ·%'), 0),
               ISNULL((SELECT SUM(c.amount_paise) FROM bill.charge c WHERE c.folio_id = st.folio_id AND c.reverses_charge_id IS NULL AND (c.label LIKE N'LCO1 ·%' OR c.label LIKE N'LCO2 ·%')), 0),
               ISNULL((SELECT SUM(c.amount_paise) FROM bill.charge c WHERE c.folio_id = st.folio_id AND c.reverses_charge_id IS NULL AND c.label LIKE N'%breakfast%'), 0),
               ISNULL((SELECT SUM(c.amount_paise) FROM bill.charge c WHERE c.folio_id = st.folio_id AND c.reverses_charge_id IS NULL AND c.label LIKE N'%extra bed%'), 0),
               x.upgrade_approver
          FROM gst.stay st
          LEFT JOIN meas.booking_extra x ON x.property_id = st.property_id AND x.reservation_id = st.reservation_id
          LEFT JOIN plat.app_user au ON au.user_id = x.upgrade_approver
         WHERE st.property_id = @p AND st.checked_in_date BETWEEN @from AND @to AND (@by = '' OR CAST(st.checked_in_by AS NVARCHAR(20)) = @by)
         ORDER BY st.reservation_id;
        """;

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(Platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        int arrivals = 0, paid = 0, free = 0;
        long lco = 0, bf = 0, bed = 0, upg = 0;
        var approvers = new SortedSet<string>(StringComparer.Ordinal);
        await using (var conn = await Platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            Add(cmd, "@by", SqlDbType.NVarChar, Param(parameters, "deskUser"));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                arrivals++;
                var up = rd.GetString(1);
                if (up == "paid") paid++;
                if (up == "free") { free++; if (rd.GetString(2).Length > 0) approvers.Add(rd.GetString(2)); }
                upg += rd.GetInt64(3);
                lco += rd.GetInt64(4);
                bf += rd.GetInt64(5);
                bed += rd.GetInt64(6);
            }
        }

        if (arrivals == 0) return Result(parameters, [], "UPG / LCO items, booked type kept (meas-cap-desk)");
        long? per = MeasGuestRules.RoundHalf((lco + bf + bed + upg) / (decimal)arrivals);
        return Result(parameters,
        [
            Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["arrivals"] = (long)arrivals, ["upgradesPaid"] = (long)paid, ["upgradesFree"] = (long)free, ["approver"] = string.Join(", ", approvers), ["lateCheckout"] = lco,
                ["dayUse"] = 0L, ["breakfast"] = bf, ["extraBed"] = bed, ["revenuePerArrival"] = per,
            }),
        ]);
    }
}
