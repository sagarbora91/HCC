using System.Data;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Outbox;

/// <summary>
/// Writes <c>plat.outbox</c> rows inside a caller's transaction for writers that are not a command handler: the inventory count writer
/// (A08-03 InventoryChanged, also from the hold-expiry sweep) and the OTA projector (an automatic push it queues). Same columns and
/// statement as <c>SqlCommandTransaction.PublishAsync</c>; the row commits or rolls back with the caller's work.
/// </summary>
public static class OutboxWriter
{
    public static async Task<long> InsertAsync(Func<string, SqlCommand> command, string eventType, long aggregateId, string payloadJson, int? propertyId,
        int? principalId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var cmd = command("""
            INSERT plat.outbox(event_type, aggregate_id, payload, property_id, principal_id)
            OUTPUT INSERTED.outbox_id VALUES (@e, @a, @j, @p, @u);
            """);
        cmd.Parameters.Add("@e", SqlDbType.VarChar, 60).Value = eventType;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = aggregateId;
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, -1).Value = payloadJson;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = (object?)propertyId ?? DBNull.Value;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = (object?)principalId ?? DBNull.Value;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    /// <summary>Replaces the payload of an event this same transaction inserted (not yet committed, so no consumer has seen it).</summary>
    public static async Task UpdatePayloadAsync(Func<string, SqlCommand> command, long outboxId, string payloadJson, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var cmd = command("UPDATE plat.outbox SET payload = @j WHERE outbox_id = @o;");
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, -1).Value = payloadJson;
        cmd.Parameters.Add("@o", SqlDbType.BigInt).Value = outboxId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
