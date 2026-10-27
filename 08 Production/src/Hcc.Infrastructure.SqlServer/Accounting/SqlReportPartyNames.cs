using System.Data;
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// ACC-1 (integration): party display names for the report queries (<see cref="IReportPartyNames"/>; contract §3, prototype
/// <c>accPartyLedgers</c>) — derived from the masters, never stored twice: tills <c>1000/&lt;code&gt;</c> and bank accounts
/// <c>1030/&lt;code&gt;</c> (<c>mst.bank_account</c>), companies <c>1110/CMP-&lt;id&gt;</c> (<c>grp.company_account</c>), OTA channels
/// <c>1115/&lt;code&gt;</c> (<c>ota.channel</c>), Udhaar customers <c>1120/UD-&lt;mobile|slug&gt;</c> (<c>ops.udhaar_receivable</c>),
/// staff <c>1200/STF-&lt;id&gt;</c> (<c>hr.staff</c>) and expense vendors <c>2200/&lt;code&gt;</c> (<c>ops.expense_vendor</c>).
/// </summary>
public sealed class SqlReportPartyNames(SqlConnectionFactory connections) : IReportPartyNames
{
    private const string Sql = """
        SELECT CASE kind WHEN 'till' THEN '1000' ELSE '1030' END, code, name, CAST(NULL AS VARCHAR(10)) FROM mst.bank_account WHERE property_id = @p
        UNION ALL SELECT '1110', CONCAT('CMP-', company_id), name, NULL FROM grp.company_account WHERE property_id = @p
        UNION ALL SELECT '1115', code, name, NULL FROM ota.channel WHERE property_id = @p
        UNION ALL SELECT '1120', NULL, customer_name, mobile FROM ops.udhaar_receivable WHERE property_id = @p
        UNION ALL SELECT '1200', CONCAT('STF-', staff_id), name, NULL FROM hr.staff WHERE property_id = @p
        UNION ALL SELECT '2200', code, name, NULL FROM ops.expense_vendor WHERE property_id = @p;
        """;

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(int propertyId, CancellationToken ct = default)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(Sql, conn, null);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var control = rd.GetString(0);
            var name = rd.GetString(2);
            var party = control == "1120" ? PartyLedgers.Udhaar(name, rd.IsDBNull(3) ? null : rd.GetString(3)) : Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture)!;
            names.TryAdd(PartyLedgers.Compose(control, PartyLedgers.Clip(party)), name);
        }

        return names;
    }
}
