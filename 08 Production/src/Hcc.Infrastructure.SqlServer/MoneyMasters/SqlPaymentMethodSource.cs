using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.MoneyMasters;
using Hcc.Domain.MoneyMasters;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.MoneyMasters;

/// <summary>
/// MA-11: payment methods read from mst.payment_method (2830); "settles to a till" = the settles-to account's kind is 'till'.
/// AF2-PAYMETHODS: the second result set is the rows' earlier names (mst.master_history, field "name"; a later rename of the same text wins,
/// as in the master's own usage counts), so a value sent or stored under an earlier name resolves to the renamed row.
/// </summary>
public sealed class SqlPaymentMethodSource(SqlConnectionFactory connections) : IPaymentMethodSource
{
    private const string Sql = """
        SELECT m.code, m.name, m.kind, CAST(CASE WHEN a.kind = 'till' THEN 1 ELSE 0 END AS BIT), m.needs_reference, m.usable_frontdesk, m.usable_pos,
               m.usable_expenses, m.active, m.ledger, CAST(m.payment_method_id AS BIGINT)
          FROM mst.payment_method m LEFT JOIN mst.bank_account a ON a.bank_account_id = m.settles_to_id
         WHERE m.property_id = @p ORDER BY m.sort_order, m.code;
        SELECT h.old_value, h.row_id FROM mst.master_history h
         WHERE h.property_id = @p AND h.master = 'paymentMethods' AND h.field = 'name' AND h.old_value IS NOT NULL ORDER BY h.history_id;
        """;

    public async Task<IReadOnlyList<TenderMethod>> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default)
    {
        if (tx is SqlCommandTransaction sql)
        {
            await using var cmd = sql.Command(Sql);
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            return await ReadAsync(cmd, ct).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c = connections.Command(Sql, conn);
        c.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return await ReadAsync(c, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<TenderMethod>> ReadAsync(SqlCommand cmd, CancellationToken ct)
    {
        var rows = new List<(long Id, TenderMethod Method)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            rows.Add((rd.GetInt64(10), new TenderMethod(rd.GetString(0).TrimEnd(), rd.GetString(1), rd.GetString(2).TrimEnd(), rd.GetBoolean(3), rd.GetBoolean(4), rd.GetBoolean(5),
                rd.GetBoolean(6), rd.GetBoolean(7), rd.GetBoolean(8), rd.IsDBNull(9) ? null : rd.GetString(9))));
        var renames = new Dictionary<string, long>(StringComparer.Ordinal);
        if (await rd.NextResultAsync(ct).ConfigureAwait(false))
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) renames[rd.GetString(0)] = rd.GetInt64(1); // a later rename of the same text wins
        if (renames.Count == 0) return rows.Select(r => r.Method).ToList();
        return rows.Select(r =>
        {
            var former = renames.Where(x => x.Value == r.Id && !string.Equals(x.Key, r.Method.Name, StringComparison.Ordinal)).Select(x => x.Key).ToList();
            return former.Count == 0 ? r.Method : r.Method with { FormerNames = former };
        }).ToList();
    }
}
