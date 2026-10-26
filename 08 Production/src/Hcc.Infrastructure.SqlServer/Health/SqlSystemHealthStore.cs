using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Health;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Health;

/// <summary>
/// SQL adapter of <see cref="ISystemHealthStore"/>: parked events from <c>plat.consumer_failure</c> (migration 2900) and backup results from
/// <c>plat.backup_run</c> (migration 3920, written by the backup scripts). The release runs <see cref="OutboxConsumer.ReleaseParkedSql"/> inside
/// the command transaction, so the release and its audit row commit together.
/// </summary>
public sealed class SqlSystemHealthStore(SqlConnectionFactory connections) : ISystemHealthStore
{
    private const string ParkedColumns = "f.consumer, f.outbox_id, o.event_type, o.property_id, f.attempts, f.last_error, f.first_failed_utc, f.last_failed_utc, f.parked_utc";

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static ParkedEventRec Parked(SqlDataReader rd) => new(rd.GetString(0), rd.GetInt64(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetInt32(3), rd.GetInt32(4),
        rd.GetString(5), Utc(rd, 6), Utc(rd, 7), Utc(rd, 8));

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("System health SQL needs the SQL command transaction.");

    public async Task<IReadOnlyList<ParkedEventRec>> ParkedAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT {ParkedColumns}
            FROM plat.consumer_failure f JOIN plat.outbox o ON o.outbox_id = f.outbox_id
            WHERE f.parked_utc IS NOT NULL AND (o.property_id = @p OR o.property_id IS NULL)
              AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = f.consumer AND d.outbox_id = f.outbox_id)
            ORDER BY f.outbox_id, f.consumer;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        var list = new List<ParkedEventRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Parked(rd));
        return list;
    }

    public async Task<BackupSummary> BackupsAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (1) kind, succeeded, started_utc, finished_utc, file_name, size_bytes, reason FROM plat.backup_run
             WHERE kind <> 'RestoreDrill' ORDER BY finished_utc DESC, run_id DESC;
            SELECT MAX(finished_utc) FROM plat.backup_run WHERE kind <> 'RestoreDrill' AND succeeded = 1;
            SELECT TOP (1) kind, succeeded, started_utc, finished_utc, file_name, size_bytes, reason FROM plat.backup_run
             WHERE kind = 'RestoreDrill' ORDER BY finished_utc DESC, run_id DESC;
            """, conn);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        static BackupRunRec Run(SqlDataReader r) => new(r.GetString(0), r.GetBoolean(1), Utc(r, 2), Utc(r, 3), r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetInt64(5), r.IsDBNull(6) ? null : r.GetString(6));
        var last = await rd.ReadAsync(ct).ConfigureAwait(false) ? Run(rd) : null;
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        DateTime? ok = await rd.ReadAsync(ct).ConfigureAwait(false) && !rd.IsDBNull(0) ? Utc(rd, 0) : null;
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var drill = await rd.ReadAsync(ct).ConfigureAwait(false) ? Run(rd) : null;
        return new BackupSummary(last, ok, drill);
    }

    public async Task<ParkedEventRec?> LockParkedAsync(ICommandTransaction tx, string consumer, long outboxId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"""
            SELECT {ParkedColumns}
            FROM plat.consumer_failure f WITH (UPDLOCK, ROWLOCK) JOIN plat.outbox o ON o.outbox_id = f.outbox_id
            WHERE f.consumer = @c AND f.outbox_id = @o AND f.parked_utc IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = f.consumer AND d.outbox_id = f.outbox_id);
            """);
        cmd.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
        cmd.Parameters.Add("@o", SqlDbType.BigInt).Value = outboxId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Parked(rd) : null;
    }

    public async Task<bool> ReleaseParkedAsync(ICommandTransaction tx, string consumer, long outboxId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(OutboxConsumer.ReleaseParkedSql);
        cmd.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
        cmd.Parameters.Add("@o", SqlDbType.BigInt).Value = outboxId;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }
}
