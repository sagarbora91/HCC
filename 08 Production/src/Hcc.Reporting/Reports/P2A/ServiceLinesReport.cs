// F8-RPT-P2-A · rpt.guests.service-lines — Room service and extras lines (A5 / A6 / A43). Server twin of p2-a/service-lines.ts.
// Service charges (kind 'service') by posting date that no later charge reverses; group by the shared label rule; qty and catalogue link from bill.charge_catalogue.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.P2A.P2A;

namespace Hcc.Reporting.Reports.P2A;

public sealed class ServiceLinesReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.service-lines";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("date", "Date"), T("room", "Room"), T("guest", "Guest", ReportMask.GuestName), T("item", "Item"), T("group", "Group"), I("qty", "Qty"), M("rate", "Rate"), M("amount", "Amount"), M("tax", "Tax"),
        T("source", "Source"), T("billRef", "Bill ref"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Room service and extras lines", "guests", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("group", "Group", "enum", null, ["all", "food", "laundry", "other", "extras"], "all"), new("item", "Item", "text")],
        Columns, "Service lines by the business date they were posted (range up to 92 days); lines that were later reversed are left out. Group: catalogue extras = extras, restaurant bills = food, otherwise by the words of the item (laundry, food words, the rest = other). Amount and tax are in paise-exact rupees before/of GST.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No, false, ["A5", "A6", "A43"]);

    private static readonly string Sql = $"""
        SELECT c.service_date, rm.room_no, COALESCE(g.full_name, r.guest_name), c.label, ISNULL(cc.quantity, 1), c.amount_paise, c.tax_paise,
               CASE WHEN cc.extra_item_id IS NULL THEN 0 ELSE 1 END, {GuestsSql.BillNoExpr}, c.charge_id
          FROM bill.charge c
          JOIN bill.folio f ON f.folio_id = c.folio_id
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id
          LEFT JOIN gst.guest g ON g.guest_id = reg.guest_id
          LEFT JOIN inv.room rm ON rm.room_id = {P2A.ChargeRoomIdExpr}
          LEFT JOIN bill.charge_catalogue cc ON cc.charge_id = c.charge_id
         WHERE r.property_id = @p AND c.kind = 'service' AND c.service_date BETWEEN @from AND @to
           AND NOT EXISTS (SELECT 1 FROM bill.charge x WHERE x.reverses_charge_id = c.charge_id);
        """;

    private sealed record Item(DateOnly Date, string Room, string Guest, string Label, long Qty, long Amount, long Tax, string Group, string Bill, long Id);

    private static readonly Dictionary<string, string> GroupLabel = new() { ["food"] = "Food", ["laundry"] = "Laundry", ["other"] = "Other", ["extras"] = "Extras" };

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        Range(clock, parameters, 92);
        var g = parameters.Values.GetValueOrDefault("group") ?? "all";
        var q = (parameters.Values.GetValueOrDefault("item") ?? "").Trim();
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
                var label = rd.GetString(3);
                var grp = ServiceGroup(label, Convert.ToInt32(rd.GetValue(7)) == 1);
                if ((g != "all" && grp != g) || (q.Length > 0 && !label.Contains(q, StringComparison.OrdinalIgnoreCase))) continue;
                items.Add(new Item(GuestsSql.Date(rd, 0), GuestsSql.Str(rd, 1), GuestsSql.Str(rd, 2), label, Convert.ToInt64(rd.GetValue(4)), rd.GetInt64(5), rd.GetInt64(6), grp, GuestsSql.Str(rd, 8), rd.GetInt64(9)));
            }
        }

        var sorted = items.OrderBy(i => i.Date).ThenBy(i => i.Room, Natural).ThenBy(i => i.Id).ToList();
        var rows = sorted.Select(i =>
        {
            var c = Cells();
            c["date"] = i.Date; c["room"] = i.Room; c["guest"] = i.Guest; c["item"] = i.Label; c["group"] = GroupLabel[i.Group]; c["qty"] = i.Qty; c["rate"] = i.Qty > 0 ? i.Amount / i.Qty : i.Amount;
            c["amount"] = i.Amount; c["tax"] = i.Tax; c["source"] = i.Label.Contains(" · bill ", StringComparison.Ordinal) ? "room-service POS" : "folio"; c["billRef"] = i.Bill;
            return Row(ReportRowKind.Row, c);
        }).ToList();
        foreach (var k in new[] { "food", "laundry", "other", "extras" })
        {
            var xs = sorted.Where(i => i.Group == k).ToList();
            if (xs.Count == 0) continue;
            var c = Cells();
            c["item"] = $"{GroupLabel[k]} · {Plural(xs.Count, "line")}"; c["qty"] = (long)xs.Count; c["amount"] = xs.Sum(i => i.Amount); c["tax"] = xs.Sum(i => i.Tax);
            rows.Add(Row(ReportRowKind.Subtotal, c, GroupLabel[k]));
        }

        var t = Cells();
        t["item"] = $"Total · {Plural(sorted.Count, "line")}"; t["qty"] = (long)sorted.Count; t["amount"] = sorted.Sum(i => i.Amount); t["tax"] = sorted.Sum(i => i.Tax);
        rows.Add(Row(ReportRowKind.Total, t));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
