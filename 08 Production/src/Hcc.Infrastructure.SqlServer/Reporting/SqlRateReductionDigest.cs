using System.Data;
using Hcc.Domain.Reporting;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>
/// CTL-01: the rate changes of one business date that went more than the Owner's threshold below the stay's agreed rate (migration 4920
/// <c>over_threshold = 1</c>), with names, for the owner digest's e-mail draft (the WhatsApp draft carries only their count, DEC-247).
/// </summary>
public static class SqlRateReductionDigest
{
    private const string Sql = """
        SELECT rm.room_no, r.public_no, COALESCE(c.tariff_paise, 0), COALESCE(c.agreed_rate_paise, c.old_rate_paise), c.new_rate_paise, COALESCE(c.threshold_bps, 0),
               COALESCE(uc.display_name, CAST(c.changed_by AS NVARCHAR(20))), CASE WHEN c.approved_by IS NULL THEN NULL ELSE COALESCE(ua.display_name, CAST(c.approved_by AS NVARCHAR(20))) END,
               c.reason
          FROM gst.stay_rate_change c
          JOIN inv.reservation r ON r.reservation_id = c.reservation_id
          JOIN inv.room rm ON rm.room_id = c.room_id
          LEFT JOIN plat.app_user uc ON uc.user_id = c.changed_by
          LEFT JOIN plat.app_user ua ON ua.user_id = c.approved_by
         WHERE c.property_id = @p AND c.business_date = @d AND c.over_threshold = 1
         ORDER BY c.change_id;
        """;

    public static async Task<IReadOnlyList<RateReductionLine>> LinesAsync(SqlConnectionFactory connections, SqlConnection connection, SqlTransaction? transaction, int propertyId, DateOnly date, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connections);
        await using var cmd = connections.Command(Sql, connection, transaction);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        var list = new List<RateReductionLine>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var agreed = rd.GetInt64(3);
            var now = rd.GetInt64(4);
            list.Add(new RateReductionLine(rd.GetString(0), rd.GetString(1), rd.GetInt64(2), agreed, now,
                agreed <= 0 || now >= agreed ? 0 : (int)((agreed - now) * 10_000 / agreed), rd.GetInt32(5),
                rd.GetString(6), rd.IsDBNull(7) ? null : rd.GetString(7), rd.GetString(8)));
        }

        return list;
    }
}
