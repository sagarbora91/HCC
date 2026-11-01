using System.Data;
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// ACC-3 acc3-tally: the log of the Tally files (<c>acc.tally_file</c>, migration 4266) and the GSTIN of the party sub-ledgers that have one
/// (companies <c>grp.company_account</c>, expense vendors <c>ops.expense_vendor</c>).
/// </summary>
public sealed class SqlTallyStore(SqlConnectionFactory connections) : ITallyFileLog, ITallyPartyGstin
{
    public async Task<TallyFileLogRow?> LastAsync(int propertyId, string kind, DateOnly from, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (1) file_id, exported_utc, exported_by, sha256 FROM acc.tally_file
            WHERE property_id = @p AND kind = @k AND from_date = @f ORDER BY file_id DESC;
            """, conn);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        cmd.Parameters.Set("@k", SqlDbType.VarChar, kind, 8);
        cmd.Parameters.Set("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new TallyFileLogRow(rd.GetInt64(0), rd.GetDateTime(1), rd.GetInt32(2), rd.GetString(3).Trim()) : null;
    }

    public async Task<long> RecordAsync(ICommandTransaction tx, NewTallyFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(file);
        var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The Tally log needs a SQL command transaction.");
        await using var cmd = connections.Command("""
            INSERT acc.tally_file(property_id, kind, from_date, to_date, item_count, byte_count, sha256, provisional, exported_by, exported_utc)
            OUTPUT INSERTED.file_id VALUES (@p, @k, @f, @t, @n, @b, @h, @pr, @u, @now);
            """, s.Connection, s.Transaction);
        cmd.Parameters.Set("@p", SqlDbType.Int, file.PropertyId);
        cmd.Parameters.Set("@k", SqlDbType.VarChar, file.Kind, 8);
        cmd.Parameters.Set("@f", SqlDbType.Date, file.From.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.Set("@t", SqlDbType.Date, file.To.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.Set("@n", SqlDbType.Int, file.Items);
        cmd.Parameters.Set("@b", SqlDbType.Int, file.Bytes);
        cmd.Parameters.Set("@h", SqlDbType.Char, file.Sha256, 64);
        cmd.Parameters.Set("@pr", SqlDbType.Bit, file.Provisional);
        cmd.Parameters.Set("@u", SqlDbType.Int, file.ExportedBy);
        cmd.Parameters.Set("@now", SqlDbType.DateTime2, file.ExportedUtc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(int propertyId, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT '1110/CMP-' + CONVERT(VARCHAR(20), company_id), gstin FROM grp.company_account WHERE property_id = @p AND gstin IS NOT NULL
            UNION ALL SELECT '2200/' + code, gstin FROM ops.expense_vendor WHERE property_id = @p AND gstin IS NOT NULL;
            """, conn);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            map.TryAdd(rd.GetString(0), rd.GetString(1).Trim());
        return map;
    }
}
