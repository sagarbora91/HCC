using System.Data;
using Hcc.Application.Reporting;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>F4-T2: held owner-digest drafts (digest.draft, migration 4376). One row per (property, business date, channel); a re-close replaces it.</summary>
public sealed class SqlDigestDraftStore(SqlConnectionFactory connections) : IDigestDraftStore
{
    public async Task UpsertAsync(int propertyId, DigestDraft d, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            DELETE digest.draft WHERE property_id = @p AND business_date = @d AND channel = @ch;
            INSERT digest.draft(property_id, business_date, channel, counts_only, status, reason, body, created_utc, created_by)
            VALUES (@p, @d, @ch, @co, 'held', @re, @body, @cu, @by);
            """, c);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = d.Date.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@ch", SqlDbType.VarChar, 8).Value = d.Channel;
        cmd.Parameters.Add("@co", SqlDbType.Bit).Value = d.CountsOnly;
        cmd.Parameters.Add("@re", SqlDbType.NVarChar, 200).Value = d.Reason;
        cmd.Parameters.Add("@body", SqlDbType.NVarChar, -1).Value = d.Body;
        cmd.Parameters.Add("@cu", SqlDbType.DateTime2).Value = d.CreatedUtc;
        cmd.Parameters.Add("@by", SqlDbType.Int).Value = d.CreatedBy;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DigestDraft>> ListAsync(int propertyId, DateOnly date, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT business_date, channel, counts_only, status, reason, body, created_utc, created_by FROM digest.draft WHERE property_id = @p AND business_date = @d ORDER BY channel", c);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<DigestDraft>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new DigestDraft(DateOnly.FromDateTime(rd.GetDateTime(0)), rd.GetString(1), rd.GetBoolean(2), rd.GetString(3), rd.GetString(4), rd.GetString(5),
                DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc), rd.GetInt32(7)));
        return list;
    }
}
