// F12 MEAS · f12-int-an · server facts of the management pack's executive summary (MEAS-STUDY-PACK item 3), the SQL twin of the prototype packSummary:
// rooms in service = active rooms that are not halls; room-night facts = live folio room lines (kind 'room', neither reversing nor reversed) by their night
// (service_date), in the room the guest occupied that night (stay segment, else the reservation's room), bookings not Cancelled / Expired. Amount before GST, paise.
using System.Data;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Analytics;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.StudyPack;

public sealed class SqlStudyPackFacts(SqlPlatform platform) : IStudyPackFacts
{
    private const string RoomsSql = """
        SELECT rm.room_no, COALESCE(rt.name, rt.code), rt.code
          FROM inv.room rm JOIN inv.room_type rt ON rt.room_type_id = rm.room_type_id
         WHERE rm.property_id = @p AND rm.active = 1;
        """;

    private const string NightsSql = """
        SELECT COALESCE(sr.room_no, rm.room_no, ''), c.service_date, c.amount_paise
          FROM bill.charge c
          JOIN bill.folio f ON f.folio_id = c.folio_id
          JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
          LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
          OUTER APPLY (SELECT TOP (1) x.room_no FROM gst.stay_segment sg JOIN inv.room x ON x.room_id = sg.room_id
                        WHERE sg.stay_id = s.stay_id AND sg.from_date <= c.service_date AND (sg.to_date IS NULL OR sg.to_date > c.service_date) ORDER BY sg.seq DESC) sr
         WHERE r.property_id = @p AND r.status NOT IN ('Cancelled','Expired') AND c.kind = 'room' AND c.reverses_charge_id IS NULL AND c.reversed_by_charge_id IS NULL
           AND c.service_date BETWEEN @from AND @to
         ORDER BY c.service_date, c.charge_id;
        """;

    public async Task<int> RoomsInServiceAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(RoomsSql, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var n = 0;
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            if (!SqlAnalyticsInputSource.IsHall(rd.GetString(0), rd.GetString(1)) && !SqlAnalyticsInputSource.IsHall(rd.GetString(0), rd.GetString(2))) n++;
        return n;
    }

    public async Task<IReadOnlyList<RoomNightFact>> RoomNightsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(NightsSql, conn) { CommandTimeout = 120 };
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<RoomNightFact>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RoomNightFact(rd.GetString(0), DateOnly.FromDateTime(rd.GetDateTime(1)), rd.GetInt64(2)));
        return list;
    }
}
