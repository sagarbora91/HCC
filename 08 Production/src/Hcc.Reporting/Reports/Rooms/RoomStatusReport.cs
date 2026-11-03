// F7-R3 · rpt.rooms.room-status — Room status today (catalogue A26 / A29). Server twin of rooms/room-status.ts. D-NOW: the state at this moment. Money flag n.
// Guest names only for callers holding PERM-RES-001 (Owner, Manager, Reception); the Housekeeping, Maintenance and Accountant roles get an empty Guest cell.
// Out of order = an active ops.room_block covering tonight (group holds are not read here — see F7-R3-DONE).
using System.Data;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.Rooms;

public sealed class RoomStatusReport(SqlPlatform platform, IAccessPolicy policy) : IReportSelector
{
    public const string Id = "rpt.rooms.room-status";

    private static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    public static readonly IReadOnlyList<string> Filters = ["all", "vacant", "occupied", "dirty", "out of order", "due out"];

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("room", "Room"), T("type", "Type"), T("floor", "Floor"), T("status", "Status"), T("hk", "Housekeeping status"), T("guest", "Guest", ReportMask.GuestName),
        new("pax", "Pax", ReportColumnType.Int, false), new("checkIn", "Check-in", ReportColumnType.Date, false), new("checkOut", "Expected check-out", ReportColumnType.Date, false),
        new("nextArrival", "Next arrival", ReportColumnType.Date, false), T("notes", "Notes"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Room status today", "rooms", "P1", "built", false, null, ["PERM-RPT-001"], ["PERM-HK-001", "PERM-STY-001"], "PERM-RPT-003", ReportDateClass.Now,
        [new("filter", "Filter", "enum", null, Filters, "all")], Columns,
        "The state at this moment (business date, no history). Out of order = a block or a group hold tonight. Next arrival = the earliest booked arrival from today.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes, false, ["A26", "A29"], OwnShift: true);

    private const string Sql = """
        SELECT rm.room_no, COALESCE(rt.name, rt.code), COALESCE(fl.name, CAST(rm.floor_id AS NVARCHAR(20))), rm.hk_status,
               occ.guest, occ.pax, occ.checked_in_date, occ.departure,
               (SELECT MIN(n.arrival) FROM inv.reservation n WHERE n.property_id = rm.property_id AND n.room_id = rm.room_id AND n.status IN ('Confirmed','Held') AND n.arrival >= @bd),
               blk.reason, CASE WHEN blk.block_id IS NULL THEN 0 ELSE 1 END
          FROM inv.room rm
          JOIN inv.room_type rt ON rt.room_type_id = rm.room_type_id
          LEFT JOIN mst.floor fl ON fl.floor_id = rm.floor_id
          OUTER APPLY (SELECT TOP (1) COALESCE(g.full_name, r.guest_name) AS guest, r.adults + r.children AS pax, s.checked_in_date, r.departure
                         FROM gst.stay_segment sg JOIN gst.stay s ON s.stay_id = sg.stay_id AND s.status = 'InHouse'
                         JOIN inv.reservation r ON r.reservation_id = s.reservation_id JOIN gst.guest g ON g.guest_id = s.guest_id
                        WHERE sg.room_id = rm.room_id AND sg.to_date IS NULL) occ
          OUTER APPLY (SELECT TOP (1) b.block_id, b.reason FROM ops.room_block b
                        WHERE b.room_id = rm.room_id AND b.released_utc IS NULL AND (b.from_date IS NULL OR b.from_date <= @bd) AND (b.to_date IS NULL OR b.to_date > @bd)) blk
         WHERE rm.property_id = @p AND rm.active = 1
         ORDER BY RIGHT('0000000000' + rm.room_no, 10);
        """;

    private const string FlowSql = """
        SELECT (SELECT COUNT(*) FROM inv.reservation WHERE property_id = @p AND status = 'Confirmed' AND arrival = @bd),
               (SELECT COUNT(*) FROM inv.reservation WHERE property_id = @p AND status = 'InHouse' AND departure = @bd);
        """;

    private sealed record Rm(string Room, string Type, string Floor, string Hk, string Guest, long? Pax, DateOnly? In, DateOnly? Out, DateOnly? Next, string Reason, bool Blocked, bool Occupied)
    {
        public string Status => Blocked ? "Out of order" : Occupied ? "Occupied" : "Vacant";
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        var names = await policy.HasAsync(caller.PropertyId, caller.PrincipalId, "PERM-RES-001", new AccessScope(AccessScopeKinds.Property, 0), ct).ConfigureAwait(false);
        var f = parameters.Values.GetValueOrDefault("filter") ?? "all";
        var all = new List<Rm>();
        long ins, outs;
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand(Sql, conn))
            {
                GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                GuestsSql.Add(cmd, "@bd", SqlDbType.Date, GuestsSql.D(clock.BusinessDate));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    var occupied = !rd.IsDBNull(4);
                    all.Add(new Rm(rd.GetString(0), rd.GetString(1), GuestsSql.Str(rd, 2), rd.GetString(3), occupied && names ? rd.GetString(4) : "", occupied ? Convert.ToInt64(rd.GetValue(5)) : null,
                        GuestsSql.DateN(rd, 6), GuestsSql.DateN(rd, 7), GuestsSql.DateN(rd, 8), GuestsSql.Str(rd, 9), Convert.ToInt32(rd.GetValue(10)) == 1, occupied));
                }
            }

            await using var fc = new SqlCommand(FlowSql, conn);
            GuestsSql.Add(fc, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(fc, "@bd", SqlDbType.Date, GuestsSql.D(clock.BusinessDate));
            await using var fr = await fc.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await fr.ReadAsync(ct).ConfigureAwait(false);
            ins = Convert.ToInt64(fr.GetValue(0));
            outs = Convert.ToInt64(fr.GetValue(1));
        }

        var keep = all.Where(r => f == "all" || (f == "vacant" && r.Status == "Vacant") || (f == "occupied" && r.Status == "Occupied") || (f == "dirty" && r.Hk == "Dirty")
            || (f == "out of order" && r.Status == "Out of order") || (f == "due out" && r.Occupied && r.Out <= clock.BusinessDate)).ToList();
        var rows = keep.Select(r => new ReportRow(ReportRowKind.Row, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["room"] = r.Room, ["type"] = r.Type, ["floor"] = r.Floor, ["status"] = r.Status, ["hk"] = r.Hk, ["guest"] = r.Guest, ["pax"] = r.Pax, ["checkIn"] = r.In, ["checkOut"] = r.Out,
            ["nextArrival"] = r.Next, ["notes"] = r.Blocked ? r.Reason : "",
        })).ToList();
        long N(string s) => keep.Count(r => r.Status == s);
        rows.Add(new ReportRow(ReportRowKind.Total, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["room"] = $"{keep.Count} room{(keep.Count == 1 ? "" : "s")}", ["status"] = $"Occupied {N("Occupied")} · Vacant {N("Vacant")} · Out of order {N("Out of order")}",
            ["notes"] = $"Check-ins today {ins} · Check-outs today {outs}",
        }));
        var about = new ReportAbout(DateTimeOffset.MinValue, "", Definition.DateBasis, false, false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, about);
    }
}
