using System.Data;
using Hcc.Application.Access;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>What one retention pass removed.</summary>
public sealed record RetentionPurgeResult(int IdempotencyRecords, int Sessions);

/// <summary>
/// F-L03 retention: idempotency records (stored command results, which can hold guest names and phones) older than
/// <see cref="AccessOptions.IdempotencyRetention"/> (30 days) and ended sessions (revoked, or past their absolute expiry) older than
/// <see cref="AccessOptions.SessionRetention"/> (90 days). Deletes in batches; a replay of a purged key simply runs the command again,
/// which every command already treats as a new request. One audit row per pass that removed something.
/// </summary>
public sealed class SqlRetentionPurge(SqlConnectionFactory connections, AccessOptions options, TimeProvider clock)
{
    private const int Batch = 5000;

    public async Task<RetentionPurgeResult> RunOnceAsync(CancellationToken ct = default)
    {
        var now = SqlSessionService.Truncate(clock.GetUtcNow().UtcDateTime);
        var idem = await DeleteAsync("DELETE TOP (@n) FROM plat.idempotency WHERE created_utc < @cut;", now - options.IdempotencyRetention, ct).ConfigureAwait(false);
        var sessions = await DeleteAsync("""
            DELETE TOP (@n) FROM plat.session
             WHERE (revoked_utc IS NOT NULL AND revoked_utc < @cut) OR (revoked_utc IS NULL AND absolute_expires_utc < @cut);
            """, now - options.SessionRetention, ct).ConfigureAwait(false);
        if (idem + sessions > 0)
        {
            await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
            await AccessAudit.WriteAsync(connections, conn, null, 0, 0, "RETENTION_PURGE", "platform",
                $"Removed {idem} idempotency record(s) older than {options.IdempotencyRetention.TotalDays:0} days and {sessions} ended session(s) older than {options.SessionRetention.TotalDays:0} days.",
                "RetentionPurge", ct).ConfigureAwait(false);
        }

        return new RetentionPurgeResult(idem, sessions);
    }

    private async Task<int> DeleteAsync(string sql, DateTime cutoff, CancellationToken ct)
    {
        var total = 0;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        while (true)
        {
            await using var cmd = connections.Command(sql, conn);
            cmd.Parameters.Add("@n", SqlDbType.Int).Value = Batch;
            cmd.Parameters.Add("@cut", SqlDbType.DateTime2).Value = cutoff;
            var n = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            total += n;
            if (n < Batch) return total;
        }
    }
}
