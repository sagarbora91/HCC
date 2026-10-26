using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.MoneyMasters;
using Hcc.Domain.MoneyMasters;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.MoneyMasters;

/// <summary>MA-11: payment methods read from mst.payment_method (2830); "settles to a till" = the settles-to account's kind is 'till'.</summary>
public sealed class SqlPaymentMethodSource(SqlConnectionFactory connections) : IPaymentMethodSource
{
    private const string Sql = """
        SELECT m.code, m.name, m.kind, CAST(CASE WHEN a.kind = 'till' THEN 1 ELSE 0 END AS BIT), m.needs_reference, m.usable_frontdesk, m.usable_pos,
               m.usable_expenses, m.active, m.ledger
          FROM mst.payment_method m LEFT JOIN mst.bank_account a ON a.bank_account_id = m.settles_to_id
         WHERE m.property_id = @p ORDER BY m.sort_order, m.code;
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
        var list = new List<TenderMethod>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new TenderMethod(rd.GetString(0).TrimEnd(), rd.GetString(1), rd.GetString(2).TrimEnd(), rd.GetBoolean(3), rd.GetBoolean(4), rd.GetBoolean(5),
                rd.GetBoolean(6), rd.GetBoolean(7), rd.GetBoolean(8), rd.IsDBNull(9) ? null : rd.GetString(9)));
        return list;
    }
}
