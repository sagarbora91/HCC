using System.Data;
using Hcc.Application.Customers;

namespace Hcc.Infrastructure.SqlServer.Customers;

/// <summary>W1a LG (LG-HISTORY-CONTRACT §9): Customers 360 reads of the old program's history through lgc.v_customer_stay (Duplicate copies hidden).</summary>
public sealed partial class SqlCustomerReadStore
{
    public async Task<IReadOnlyList<CrmLegacyStay>> LegacyStaysAsync(int propertyId, string customerKey, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT stay_id, legacy_fy, legacy_booking_id, legacy_bill_no, arrival_date, departure_date, nights, room_no, room_count, status, net_paise
              FROM lgc.v_customer_stay WHERE property_id = @p AND customer_key = @k
             ORDER BY arrival_date DESC, stay_id DESC;
            """, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.NVarChar, customerKey ?? string.Empty, 80);
        var list = new List<CrmLegacyStay>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new CrmLegacyStay(rd.GetInt64(0), rd.GetString(1), rd.GetInt64(2), rd.IsDBNull(3) ? null : rd.GetInt32(3), DateOnly.FromDateTime(rd.GetDateTime(4)),
                rd.IsDBNull(5) ? null : DateOnly.FromDateTime(rd.GetDateTime(5)), rd.GetInt32(6), rd.GetString(7), rd.GetInt32(8), rd.GetString(9), rd.IsDBNull(10) ? null : rd.GetInt64(10)));
        return list;
    }

    public async Task<IReadOnlyDictionary<string, (int Stays, int Nights, long NetPaise, DateOnly First, DateOnly Last)>> LegacyRollupAsync(int propertyId, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var want = (keys ?? []).Where(k => !string.IsNullOrEmpty(k) && !k.Contains('|', StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, (int, int, long, DateOnly, DateOnly)>(StringComparer.Ordinal);
        if (want.Count == 0) return result;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT v.customer_key, COUNT(*), SUM(v.nights), SUM(COALESCE(v.net_paise, 0)), MIN(v.arrival_date), MAX(v.arrival_date)
              FROM lgc.v_customer_stay v
              JOIN (SELECT DISTINCT CAST(value AS NVARCHAR(80)) AS k FROM STRING_SPLIT(@keys, '|')) x ON x.k = v.customer_key
             WHERE v.property_id = @p
             GROUP BY v.customer_key;
            """, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@keys", SqlDbType.NVarChar, string.Join('|', want), -1);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            result[rd.GetString(0)] = (rd.GetInt32(1), rd.GetInt32(2), rd.GetInt64(3), DateOnly.FromDateTime(rd.GetDateTime(4)), DateOnly.FromDateTime(rd.GetDateTime(5)));
        return result;
    }
}
