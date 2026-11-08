// F8-RPT-P2-A · rpt.rooms.rooms-left — Rooms left by type (A7). Server twin of p2-a/rooms-left.ts. No money. Forward dates allowed (class D-FWD).
// total = active rooms of the type; occupied = distinct rooms with an inv.room_night of a holding reservation (not out of order); held = unpicked allotment of open group blocks
// (allotted - picked - released) for nights from the business date; out of order = an active ops.room_block over the night.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.P2A.P2A;

namespace Hcc.Reporting.Reports.P2A;

public sealed class RoomsLeftReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.rooms.rooms-left";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [D("date", "Date"), T("roomType", "Room type"), I("total", "Total rooms"), I("occupied", "Occupied"), I("held", "Held (group blocks)"), I("ooo", "Out of order"), I("free", "Free")];

    public ReportDefinition Definition { get; } = new(Id, "Rooms left by type", "rooms", "P2", "built", false, null, ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Forward,
        [new("from", "From", "date", ReportDateClass.Forward), new("to", "To", "date", ReportDateClass.Forward), new("roomType", "Room type", "text")],
        Columns, "Per night (range up to 31 days, up to 90 days ahead). Total = rooms of the type in service; occupied = rooms booked or in house that night (confirmed, in house, departed, or a hold still running); held = rooms kept for open group blocks and not yet picked up; out of order = blocked or offline rooms. Free = what is left.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes, false, ["A7"]);

    internal const string RoomsSql = "SELECT rm.room_id, COALESCE(rt.name, rt.code) FROM inv.room rm JOIN inv.room_type rt ON rt.room_type_id = rm.room_type_id WHERE rm.property_id = @p AND rm.active = 1;";

    internal const string NightsSql = $"""
        SELECT n.room_id, n.night FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id
         WHERE r.property_id = @p AND n.night BETWEEN @from AND @to AND {P2A.HoldingExpr};
        """;

    internal const string BlocksSql = "SELECT b.room_id, b.from_date, b.to_date FROM ops.room_block b WHERE b.property_id = @p AND b.released_utc IS NULL;";

    private const string GroupSql = """
        SELECT COALESCE(rt.name, rt.code), gb.arrival, gb.departure, t.allotted - t.picked - t.released
          FROM inv.group_block gb JOIN inv.group_block_type t ON t.group_block_id = gb.group_block_id JOIN inv.room_type rt ON rt.room_type_id = t.room_type_id
         WHERE gb.property_id = @p AND gb.status = 'Open';
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        Range(clock, parameters, 31, 90);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var q = (parameters.Values.GetValueOrDefault("roomType") ?? "").Trim();
        var rooms = new List<(int Id, string Type)>();
        var nights = new HashSet<(int, DateOnly)>();
        var blocks = new List<(int Room, DateOnly? From, DateOnly? To)>();
        var holds = new List<(string Type, DateOnly Arr, DateOnly Dep, long Left)>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            async Task Read(string sql, bool dates, Func<SqlDataReader, Task> each)
            {
                await using var cmd = new SqlCommand(sql, conn);
                GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                if (dates) { GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(from)); GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(to)); }
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) await each(rd).ConfigureAwait(false);
            }

            await Read(RoomsSql, false, rd => { rooms.Add((rd.GetInt32(0), rd.GetString(1))); return Task.CompletedTask; }).ConfigureAwait(false);
            await Read(NightsSql, true, rd => { nights.Add((rd.GetInt32(0), GuestsSql.Date(rd, 1))); return Task.CompletedTask; }).ConfigureAwait(false);
            await Read(BlocksSql, false, rd => { blocks.Add((rd.GetInt32(0), GuestsSql.DateN(rd, 1), GuestsSql.DateN(rd, 2))); return Task.CompletedTask; }).ConfigureAwait(false);
            await Read(GroupSql, false, rd => { holds.Add((rd.GetString(0), GuestsSql.Date(rd, 1), GuestsSql.Date(rd, 2), Convert.ToInt64(rd.GetValue(3)))); return Task.CompletedTask; }).ConfigureAwait(false);
        }

        var types = rooms.Select(r => r.Type).Distinct().Where(t => q.Length == 0 || string.Equals(t, q, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t, StringComparer.CurrentCulture).ToList();
        var rows = new List<ReportRow>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            foreach (var t in types)
            {
                var inType = rooms.Where(r => r.Type == t).ToList();
                var ooo = inType.Where(r => blocks.Any(b => b.Room == r.Id && (b.From is null || b.From <= d) && (b.To is null || d < b.To))).Select(r => r.Id).ToHashSet();
                var occupied = inType.Count(r => !ooo.Contains(r.Id) && nights.Contains((r.Id, d)));
                long held = d >= clock.BusinessDate ? holds.Where(h => h.Type == t && h.Arr <= d && d < h.Dep).Sum(h => Math.Max(0, h.Left)) : 0;
                var c = Cells();
                c["date"] = d; c["roomType"] = t; c["total"] = (long)inType.Count; c["occupied"] = (long)occupied; c["held"] = held; c["ooo"] = (long)ooo.Count;
                c["free"] = Math.Max(0, inType.Count - ooo.Count - occupied - held);
                rows.Add(Row(ReportRowKind.Row, c, GuestsSql.Iso(d)));
            }
        }

        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
