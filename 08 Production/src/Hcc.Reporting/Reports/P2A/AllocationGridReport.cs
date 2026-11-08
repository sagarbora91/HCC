// F8-RPT-P2-A · rpt.rooms.allocation-grid — Room allocation grid (A31 / A56). Server twin of p2-a/allocation-grid.ts. No money. Max 31 days.
// The definition holds 31 date slots (d01..d31); a run returns as many as the range has. Cell = guest name (inv.room_night of a holding reservation), OOO (active ops.room_block) or empty.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.P2A.P2A;

namespace Hcc.Reporting.Reports.P2A;

public sealed class AllocationGridReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.rooms.allocation-grid";

    private static readonly string[] Mon = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static string Slot(int i) => "d" + (i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    public static string DateLabel(DateOnly d) => $"{d.Day} {Mon[d.Month - 1]}";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [T("room", "Room"), T("roomType", "Room type"), .. Enumerable.Range(0, 31).Select(i => T(Slot(i), "Day " + (i + 1))), I("nights", "Nights occupied")];

    public ReportDefinition Definition { get; } = new(Id, "Room allocation grid", "rooms", "P2", "built", false, null, ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Short,
        [new("from", "From", "date", ReportDateClass.Short), new("to", "To", "date", ReportDateClass.Short), new("roomType", "Room type", "text")],
        Columns, "One column per night (range up to 31 days). A cell shows the guest in the room that night (confirmed, in house, departed, or a hold still running), OOO for a blocked or offline room, empty when free. Nights occupied counts the guest cells.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No, false, ["A31", "A56"]);

    private const string RoomsSql = "SELECT rm.room_id, rm.room_no, COALESCE(rt.name, rt.code) FROM inv.room rm JOIN inv.room_type rt ON rt.room_type_id = rm.room_type_id WHERE rm.property_id = @p AND rm.active = 1;";

    private static readonly string NightsSql = $"""
        SELECT n.room_id, n.night, COALESCE(g.full_name, r.guest_name)
          FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id
          LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id LEFT JOIN gst.guest g ON g.guest_id = reg.guest_id
         WHERE r.property_id = @p AND n.night BETWEEN @from AND @to AND {P2A.HoldingExpr};
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        Range(clock, parameters, 31);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var q = (parameters.Values.GetValueOrDefault("roomType") ?? "").Trim();
        var rooms = new List<(int Id, string No, string Type)>();
        var guest = new Dictionary<(int, DateOnly), string>();
        var blocks = new List<(int Room, DateOnly? From, DateOnly? To)>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand(RoomsSql, conn))
            {
                GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) rooms.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2)));
            }

            await using (var cmd = new SqlCommand(NightsSql, conn))
            {
                GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(from));
                GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(to));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) guest[(rd.GetInt32(0), GuestsSql.Date(rd, 1))] = GuestsSql.Str(rd, 2);
            }

            await using (var cmd = new SqlCommand(RoomsLeftReport.BlocksSql, conn))
            {
                GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) blocks.Add((rd.GetInt32(0), GuestsSql.DateN(rd, 1), GuestsSql.DateN(rd, 2)));
            }
        }

        var dates = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1)) dates.Add(d);
        var cols = new List<ReportColumn> { Columns[0], Columns[1] };
        cols.AddRange(dates.Select((d, i) => T(Slot(i), DateLabel(d))));
        cols.Add(Columns[^1]);
        var rows = new List<ReportRow>();
        foreach (var r in rooms.Where(r => q.Length == 0 || string.Equals(r.Type, q, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Type, StringComparer.CurrentCulture).ThenBy(r => r.No, Natural))
        {
            var c = Cells();
            c["room"] = r.No; c["roomType"] = r.Type;
            long n = 0;
            for (var i = 0; i < dates.Count; i++)
            {
                var d = dates[i];
                if (guest.TryGetValue((r.Id, d), out var g)) { c[Slot(i)] = g; n++; }
                else c[Slot(i)] = blocks.Any(b => b.Room == r.Id && (b.From is null || b.From <= d) && (b.To is null || d < b.To)) ? "OOO" : "";
            }

            c["nights"] = n;
            rows.Add(Row(ReportRowKind.Row, c, r.Type));
        }

        return new ReportResult(Id, Definition.Title, parameters, cols, rows, About(Definition, parameters));
    }
}
