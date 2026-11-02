using System.Data;
using Hcc.Infrastructure.Egress;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Egress;

/// <summary>
/// F5-E1 SQL adapter of the egress gate (EGRESS-CONTRACT §1.2, §1, §4, §5): <c>ops.egress_outbox</c> (4731), <c>ops.egress_audit</c> (4732),
/// <c>ops.egress_firewall_state</c> (4734). Held → pending keeps the original order (item id); holding consumes no attempt.
/// </summary>
public sealed class SqlEgressStore(SqlConnectionFactory connections) : IEgressOutboxStore, IEgressAudit, IEgressHealthStore
{
    private static DateTimeOffset Utc(SqlDataReader rd, int i) => new(DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc));

    private static EgressIntegration Parse(string name) => EgressKeys.All.First(i => EgressKeys.Name(i) == name);

    private async Task<int> ExecAsync(string sql, Action<SqlParameterCollection> bind, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(sql, conn);
        bind(cmd.Parameters);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertAsync(string queue, EgressIntegration integration, string? refId, string payloadJson, bool held, string? heldReason, DateTimeOffset at, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            INSERT ops.egress_outbox(queue, integration, ref_id, payload, status, held_reason, held_at, created_utc, updated_utc)
            OUTPUT INSERTED.item_id VALUES (@q, @i, @r, @p, @s, @hr, @ha, @at, @at);
            """, conn);
        cmd.Parameters.Add("@q", SqlDbType.VarChar, 20).Value = queue;
        cmd.Parameters.Add("@i", SqlDbType.VarChar, 12).Value = EgressKeys.Name(integration);
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 80).Value = (object?)refId ?? DBNull.Value;
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, -1).Value = payloadJson;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 8).Value = held ? "held" : "pending";
        cmd.Parameters.Add("@hr", SqlDbType.NVarChar, 60).Value = held ? heldReason ?? EgressOutbox.HeldReason : DBNull.Value;
        cmd.Parameters.Add("@ha", SqlDbType.DateTime2).Value = held ? at.UtcDateTime : DBNull.Value;
        cmd.Parameters.Add("@at", SqlDbType.DateTime2).Value = at.UtcDateTime;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public Task<int> HoldPendingAsync(EgressIntegration integration, string heldReason, DateTimeOffset at, CancellationToken ct = default) => ExecAsync("""
        UPDATE ops.egress_outbox SET status = 'held', held_reason = @hr, held_at = @at, updated_utc = @at WHERE integration = @i AND status = 'pending';
        """, p =>
    {
        p.Add("@i", SqlDbType.VarChar, 12).Value = EgressKeys.Name(integration);
        p.Add("@hr", SqlDbType.NVarChar, 60).Value = heldReason;
        p.Add("@at", SqlDbType.DateTime2).Value = at.UtcDateTime;
    }, ct);

    public Task<int> ReplayHeldAsync(EgressIntegration integration, DateTimeOffset at, CancellationToken ct = default) => ExecAsync("""
        UPDATE ops.egress_outbox SET status = 'pending', held_reason = NULL, held_at = NULL, updated_utc = @at WHERE integration = @i AND status = 'held';
        """, p =>
    {
        p.Add("@i", SqlDbType.VarChar, 12).Value = EgressKeys.Name(integration);
        p.Add("@at", SqlDbType.DateTime2).Value = at.UtcDateTime;
    }, ct);

    public async Task<IReadOnlyList<EgressOutboxItem>> PendingAsync(EgressIntegration integration, int max, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (@n) item_id, queue, integration, ref_id, payload, status, held_reason, held_at FROM ops.egress_outbox
             WHERE integration = @i AND status = 'pending' ORDER BY item_id;
            """, conn);
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = max;
        cmd.Parameters.Add("@i", SqlDbType.VarChar, 12).Value = EgressKeys.Name(integration);
        var list = new List<EgressOutboxItem>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new(rd.GetInt64(0), rd.GetString(1), Parse(rd.GetString(2)), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetString(4), rd.GetString(5),
                rd.IsDBNull(6) ? null : rd.GetString(6), rd.IsDBNull(7) ? null : Utc(rd, 7)));
        return list;
    }

    public async Task<IReadOnlyList<EgressQueueCount>> CountsAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT q.queue, ISNULL(SUM(CASE WHEN o.status = 'held' THEN 1 END), 0), ISNULL(SUM(CASE WHEN o.status = 'pending' THEN 1 END), 0)
            FROM (VALUES ('messaging'), ('ota-push'), ('email'), ('scheduled-report')) q(queue)
            LEFT JOIN ops.egress_outbox o ON o.queue = q.queue AND o.status IN ('held', 'pending')
            GROUP BY q.queue ORDER BY CASE q.queue WHEN 'messaging' THEN 1 WHEN 'ota-push' THEN 2 WHEN 'email' THEN 3 ELSE 4 END;
            """, conn);
        var list = new List<EgressQueueCount>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new(rd.GetString(0), rd.GetInt32(1), rd.GetInt32(2)));
        return list;
    }

    public async Task WriteAsync(EgressAuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await ExecAsync("""
            INSERT ops.egress_audit(at_utc, integration, decision, reason, user_id, host) VALUES (@at, @i, @d, @r, @u, @h);
            """, p =>
        {
            p.Add("@at", SqlDbType.DateTime2).Value = entry.At.UtcDateTime;
            p.Add("@i", SqlDbType.VarChar, 12).Value = entry.Integration.Length > 12 ? entry.Integration[..12] : entry.Integration;
            p.Add("@d", SqlDbType.VarChar, 7).Value = entry.Allowed ? "allowed" : "refused";
            p.Add("@r", SqlDbType.VarChar, 30).Value = entry.Reason;
            p.Add("@u", SqlDbType.Int).Value = (object?)entry.UserId ?? DBNull.Value;
            p.Add("@h", SqlDbType.NVarChar, 255).Value = (object?)entry.Host ?? DBNull.Value;
        }, ct).ConfigureAwait(false);
    }

    public async Task<EgressFirewallRow?> FirewallAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT wanted_state, wanted_utc, applied_state, applied_utc, detail FROM ops.egress_firewall_state WHERE id = 1;", conn);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false)
            ? new EgressFirewallRow(rd.GetString(0), Utc(rd, 1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : Utc(rd, 3), rd.IsDBNull(4) ? null : rd.GetString(4))
            : null;
    }

    public async Task<bool> SetWantedFirewallAsync(string wanted, DateTimeOffset at, CancellationToken ct = default) => await ExecAsync("""
        UPDATE ops.egress_firewall_state SET wanted_state = @w, wanted_utc = @at WHERE id = 1 AND wanted_state <> @w;
        """, p =>
    {
        p.Add("@w", SqlDbType.VarChar, 3).Value = wanted;
        p.Add("@at", SqlDbType.DateTime2).Value = at.UtcDateTime;
    }, ct).ConfigureAwait(false) > 0;

    public async Task<IReadOnlyDictionary<string, (DateTimeOffset At, string Reason)>> LastRefusalsAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT a.integration, a.at_utc, a.reason FROM ops.egress_audit a
            WHERE a.audit_id IN (SELECT MAX(audit_id) FROM ops.egress_audit WHERE decision = 'refused' GROUP BY integration);
            """, conn);
        var map = new Dictionary<string, (DateTimeOffset, string)>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetString(0)] = (Utc(rd, 1), rd.GetString(2));
        return map;
    }
}
