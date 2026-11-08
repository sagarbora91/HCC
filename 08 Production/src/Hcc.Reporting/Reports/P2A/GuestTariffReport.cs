// F8-RPT-P2-A · rpt.guests.guest-tariff — Guest tariff detail (A42, LF B1). Server twin of p2-a/guest-tariff.ts. Stays in house or departed, by arrival date.
// Stay-level (SF R1): charges of every folio of the reservation are summed through bill.folio (aggregate, never a per-folio join).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.P2A.P2A;

namespace Hcc.Reporting.Reports.P2A;

public sealed class GuestTariffReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.guest-tariff";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("stay", "Stay"), T("room", "Room"), T("roomType", "Room type"), T("plan", "Plan"), M("standard", "Standard rate"), T("rateSource", "Rate source"), M("agreed", "Agreed rate"),
        M("extras", "Extra charges"), M("discount", "Discount"), M("netTariff", "Net tariff"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Guest tariff detail", "guests", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Financial,
        [new("party", "Guest or company", "text"), new("from", "From", "date", ReportDateClass.Financial), new("to", "To", "date", ReportDateClass.Financial)],
        Columns, "Stays that are in house or departed, by arrival date; the guest or company is matched on the name (part of it is enough). Standard rate = the tariff card rate on arrival; rate source = agreed (the guest's agreed rate was used), manual (the rate differs from the standard rate) or standard. Net tariff = room-night charges less discount, before GST; extra charges = every other line, before GST.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes, false, ["A42"]);

    private const string Sql = """
        SELECT r.public_no, rm.room_no, COALESCE(rt.name, rt.code), mp.code,
               COALESCE((SELECT TOP (1) t.rate_paise FROM inv.room_type_tariff t WHERE t.room_type_id = rt.room_type_id AND t.effective_from <= r.arrival ORDER BY t.effective_from DESC), rt.rack_rate_paise),
               r.nightly_rate_paise, r.rate_source,
               (SELECT a.rate_paise FROM gst.guest_agreed_rate a WHERE a.guest_id = g.guest_id AND a.room_type_id = rt.room_type_id),
               ISNULL((SELECT SUM(c.amount_paise) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id WHERE f.reservation_id = r.reservation_id AND c.kind = 'room'), 0),
               ISNULL((SELECT SUM(c.amount_paise) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id WHERE f.reservation_id = r.reservation_id AND c.kind = 'discount'), 0),
               ISNULL((SELECT SUM(c.amount_paise) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id WHERE f.reservation_id = r.reservation_id AND c.kind NOT IN ('room','discount')), 0),
               COALESCE(g.full_name, r.guest_name), co.name, g.company, r.arrival
          FROM inv.reservation r
          LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id
          LEFT JOIN gst.guest g ON g.guest_id = reg.guest_id
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
          LEFT JOIN inv.room_type rt ON rt.room_type_id = COALESCE(rm.room_type_id, r.room_type_id)
          LEFT JOIN bill.reservation_meal_plan rmp ON rmp.reservation_id = r.reservation_id
          LEFT JOIN bill.meal_plan mp ON mp.meal_plan_id = rmp.meal_plan_id
          LEFT JOIN grp.company_account co ON co.company_id = r.company_id
         WHERE r.property_id = @p AND r.status IN ('InHouse','Departed') AND r.arrival BETWEEN @from AND @to;
        """;

    private sealed record Item(DateOnly Arrival, string Stay, string Room, string Type, string Plan, long Standard, string Source, long? Agreed, long Tariff, long Extras, long Discount);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var q = (parameters.Values.GetValueOrDefault("party") ?? "").Trim();
        if (q.Length < 2) throw HccException.Validation("Type at least two letters of the guest or company name.");
        var items = new List<Item>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var hay = GuestsSql.Str(rd, 11) + "|" + GuestsSql.Str(rd, 12) + "|" + GuestsSql.Str(rd, 13);
                if (!hay.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                var std = Convert.ToInt64(rd.GetValue(4));
                long? nightly = rd.IsDBNull(5) ? null : Convert.ToInt64(rd.GetValue(5));
                var source = GuestsSql.Str(rd, 6) == "agreed" ? "agreed" : nightly is { } n && n != std ? "manual" : "standard";
                items.Add(new Item(GuestsSql.Date(rd, 14), rd.GetString(0), GuestsSql.Str(rd, 1), GuestsSql.Str(rd, 2), GuestsSql.Str(rd, 3), std, source,
                    rd.IsDBNull(7) ? null : Convert.ToInt64(rd.GetValue(7)), Convert.ToInt64(rd.GetValue(8)), Convert.ToInt64(rd.GetValue(10)), -Convert.ToInt64(rd.GetValue(9))));
            }
        }

        var sorted = items.OrderByDescending(i => i.Arrival).ThenByDescending(i => i.Stay, Natural).ToList();
        var rows = sorted.Select(i =>
        {
            var c = Cells();
            c["stay"] = i.Stay; c["room"] = i.Room; c["roomType"] = i.Type; c["plan"] = i.Plan; c["standard"] = i.Standard; c["rateSource"] = i.Source; c["agreed"] = i.Agreed;
            c["extras"] = i.Extras; c["discount"] = i.Discount; c["netTariff"] = i.Tariff - i.Discount;
            return Row(ReportRowKind.Row, c);
        }).ToList();
        var tc = Cells();
        tc["stay"] = $"Total · {Plural(sorted.Count, "stay")}"; tc["extras"] = sorted.Sum(i => i.Extras); tc["discount"] = sorted.Sum(i => i.Discount); tc["netTariff"] = sorted.Sum(i => i.Tariff - i.Discount);
        rows.Add(Row(ReportRowKind.Total, tc));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
