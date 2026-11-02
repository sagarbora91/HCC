using System.Data;
using Hcc.Application.Reporting;
using Hcc.Domain.Reporting;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>F4-T2: rooms with their type code and the stay segments of the stays that touch [from, to] (gst.stay_segment, migration 0300), with the folio of each stay.</summary>
public sealed class SqlRevenueCategorySource(SqlConnectionFactory connections) : IRevenueCategorySource
{
    private const string Sql = """
        SELECT r.room_id, t.code FROM inv.room r JOIN inv.room_type t ON t.room_type_id = r.room_type_id WHERE r.property_id = @p ORDER BY r.room_id;
        SELECT sg.stay_id, s.folio_id, sg.room_id, t.code, sg.from_date, sg.to_date
          FROM gst.stay_segment sg JOIN gst.stay s ON s.stay_id = sg.stay_id
          JOIN inv.room r ON r.room_id = sg.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id
         WHERE s.property_id = @p AND sg.from_date <= @to AND (sg.to_date IS NULL OR sg.to_date >= @from)
         ORDER BY sg.stay_id, sg.seq;
        """;

    public async Task<RevenueCategoryData> LoadAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(Sql, c);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rooms = new List<CategoryRoom>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) rooms.Add(new CategoryRoom(rd.GetInt32(0), rd.GetString(1)));
        var segs = new List<CategorySegment>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            segs.Add(new CategorySegment(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetString(3), DateOnly.FromDateTime(rd.GetDateTime(4)), rd.IsDBNull(5) ? null : DateOnly.FromDateTime(rd.GetDateTime(5))));
        return new RevenueCategoryData(rooms, segs);
    }
}
