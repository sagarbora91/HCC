using System.Data;
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// ACC-3 acc3-ageing: SQL adapter of <see cref="IAgeingAllocationSource"/> — ACC-2's explicit vendor-payment allocations
/// (<c>ops.vendor_payment_alloc</c>, migration 4232) of payments that are not void, keyed by the payment voucher's source key
/// (<c>vendor-payment:&lt;id&gt;</c>, <c>AccountingEventMapper.VendorPaymentKey</c>); bill ref <c>expense:&lt;id&gt;</c> or <c>opening</c>.
/// No new table: the ageing is a selector (contract §10), so migrations 4262–4265 stay unused.
/// </summary>
public sealed class SqlAgeingAllocationSource(SqlConnectionFactory connections) : IAgeingAllocationSource
{
    private const string Sql = """
        SELECT a.vendor_payment_id, a.expense_id, a.opening, a.amount_paise
        FROM ops.vendor_payment_alloc a JOIN ops.vendor_payment p ON p.vendor_payment_id = a.vendor_payment_id
        WHERE p.property_id = @p AND p.voided = 0
        ORDER BY a.vendor_payment_id, a.alloc_id;
        -- F5-A1 (ACC4-CONTRACT §3 C1 / C4): a posted goods return allocates to its own bill explicitly first; oldest-first only for the rest.
        SELECT r.expense_id, r.return_of, ABS(r.amount_paise)
        FROM ops.expense r
        WHERE r.property_id = @p AND r.return_of IS NOT NULL AND r.status = 'Posted'
        ORDER BY r.expense_id;
        """;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<(string Ref, long Paise)>>> GetAsync(int propertyId, CancellationToken ct = default)
    {
        var map = new Dictionary<string, List<(string Ref, long Paise)>>(StringComparer.Ordinal);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(Sql, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = "vendor-payment:" + rd.GetInt64(0).ToString(CultureInfo.InvariantCulture);
            var opening = !rd.IsDBNull(2) && rd.GetBoolean(2);
            var reference = opening || rd.IsDBNull(1) ? AgeingRules.OpeningRef : "expense:" + rd.GetInt64(1).ToString(CultureInfo.InvariantCulture);
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add((reference, rd.GetInt64(3)));
        }

        // F5-A1: returns (second result set) — key = the return's own voucher (expense:<id>), ref = the bill it returns.
        if (await rd.NextResultAsync(ct).ConfigureAwait(false))
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                map["expense:" + rd.GetInt64(0).ToString(CultureInfo.InvariantCulture)] = [("expense:" + rd.GetInt64(1).ToString(CultureInfo.InvariantCulture), rd.GetInt64(2))];
        // F5-A1 end

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<(string Ref, long Paise)>)kv.Value, StringComparer.Ordinal);
    }
}
