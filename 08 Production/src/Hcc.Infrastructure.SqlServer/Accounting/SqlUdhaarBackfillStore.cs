using System.Data;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer.Commands;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// ACC-2 parity (contract §8): the Udhaar credits (<c>ops.udhaar_receivable</c>, voided ones included — a write-off keeps its sale) that have
/// neither an UdhaarCreditRaised event in <c>plat.outbox</c> (not yet purged) nor the sale source <c>acc.source</c> <c>udhaar:&lt;id&gt;</c>.
/// </summary>
public sealed class SqlUdhaarBackfillStore : IUdhaarBackfillStore
{
    private const string Sql = """
        SELECT r.receivable_id, r.credit_date, r.customer_name, r.mobile, r.amount_paise, r.kind, r.gst_bps
        FROM ops.udhaar_receivable r WITH (UPDLOCK, HOLDLOCK)
        WHERE r.property_id = @p
          AND NOT EXISTS (SELECT 1 FROM plat.outbox o WHERE o.event_type = @e AND o.aggregate_id = r.receivable_id AND (o.property_id = @p OR o.property_id IS NULL))
          AND NOT EXISTS (SELECT 1 FROM acc.source x WHERE x.property_id = @p AND x.source_key = CONCAT('udhaar:', r.receivable_id))
        ORDER BY r.receivable_id;
        """;

    public async Task<IReadOnlyList<UnbookedUdhaarCredit>> UnbookedAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The Udhaar back-fill needs a SQL command transaction.");
        await using var cmd = new SqlCommand(Sql, s.Connection, s.Transaction);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@e", SqlDbType.VarChar, 60).Value = AccountingEvents.UdhaarCreditRaised;
        var list = new List<UnbookedUdhaarCredit>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new UnbookedUdhaarCredit(rd.GetInt64(0), DateOnly.FromDateTime(rd.GetDateTime(1)), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3).Trim(),
                rd.GetInt64(4), rd.IsDBNull(5) ? null : rd.GetString(5), rd.GetInt32(6)));
        return list;
    }
}
