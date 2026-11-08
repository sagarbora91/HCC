// F8-RPT-P2-A · rpt.rooms.room-wise-revenue — Room-wise and room-type revenue (A11 / A44). Server twin of p2-a/room-wise-revenue.ts.
// Charges of every folio of the stay (SF R1) are attributed to the room the guest was in on the service date (dated stay segment, else the booked room).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.P2A.P2A;

namespace Hcc.Reporting.Reports.P2A;

public sealed class RoomWiseRevenueReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.rooms.room-wise-revenue";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [T("name", "Room or type"), I("nights", "Nights sold"), M("tariff", "Tariff"), M("extra", "Extra person"), M("other", "Other room charges"), M("total", "Total"), M("avg", "Average rate")];

    public ReportDefinition Definition { get; } = new(Id, "Room-wise and room-type revenue", "rooms", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Financial,
        [new("from", "From", "date", ReportDateClass.Financial), new("to", "To", "date", ReportDateClass.Financial), new("groupBy", "Group by", "enum", null, ["room", "room type", "floor"], "room")],
        Columns, "Room charges by the business date of the line, before GST. Nights sold = room nights charged and not reversed; tariff = room-night charges; extra person = extra person / bed lines; other room charges = meal plan lines, discounts and corrections (food, laundry and other service lines are in the service-lines report). Average rate = tariff / nights sold.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Summary, false, ["A11", "A44"]);

    private static readonly string Sql = $"""
        SELECT rm.room_no, COALESCE(rt.name, rt.code), COALESCE(fl.name, 'Floor ' + CAST(rm.floor_id AS NVARCHAR(20))), c.kind, c.label, c.amount_paise,
               CASE WHEN c.kind = 'room' AND c.amount_paise > 0 AND NOT EXISTS (SELECT 1 FROM bill.charge x WHERE x.reverses_charge_id = c.charge_id) THEN 1 ELSE 0 END
          FROM bill.charge c
          JOIN bill.folio f ON f.folio_id = c.folio_id
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN inv.room rm ON rm.room_id = {P2A.ChargeRoomIdExpr}
          LEFT JOIN inv.room_type rt ON rt.room_type_id = rm.room_type_id
          LEFT JOIN mst.floor fl ON fl.floor_id = rm.floor_id
         WHERE r.property_id = @p AND c.service_date BETWEEN @from AND @to;
        """;

    private sealed class Acc { public long N, T, E, O; }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var by = parameters.Values.GetValueOrDefault("groupBy") ?? "room";
        var acc = new Dictionary<string, Acc>(StringComparer.Ordinal);
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var col = RoomColumn(rd.GetString(3), rd.GetString(4));
                if (col is null) continue;
                var key = by == "room" ? GuestsSql.Str(rd, 0) : by == "room type" ? GuestsSql.Str(rd, 1) : GuestsSql.Str(rd, 2);
                if (key.Length == 0) key = "Unassigned";
                if (!acc.TryGetValue(key, out var a)) acc[key] = a = new Acc();
                var amount = rd.GetInt64(5);
                if (col == "tariff") a.T += amount; else if (col == "extra") a.E += amount; else a.O += amount;
                a.N += Convert.ToInt64(rd.GetValue(6));
            }
        }

        static Dictionary<string, object?> Cell(string name, Acc a)
        {
            var c = Cells();
            c["name"] = name; c["nights"] = a.N; c["tariff"] = a.T; c["extra"] = a.E; c["other"] = a.O; c["total"] = a.T + a.E + a.O; c["avg"] = DivRound(a.T, a.N);
            return c;
        }

        var keys = acc.Keys.OrderBy(k => k, by == "floor" ? StringComparer.CurrentCulture : Natural).ToList();
        var rows = keys.Select(k => Row(ReportRowKind.Row, Cell(k, acc[k]), k)).ToList();
        var tot = new Acc { N = acc.Values.Sum(a => a.N), T = acc.Values.Sum(a => a.T), E = acc.Values.Sum(a => a.E), O = acc.Values.Sum(a => a.O) };
        rows.Add(Row(ReportRowKind.Total, Cell($"Total · {Plural(keys.Count, by == "room" ? "room" : by == "floor" ? "floor" : "room type")}", tot)));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
