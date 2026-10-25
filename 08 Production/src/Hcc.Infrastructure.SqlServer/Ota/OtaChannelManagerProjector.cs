using System.Data;
using Hcc.Application.Commands;
using Hcc.Contracts.Ota;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Ota;

/// <summary>
/// The SIMULATED channel manager, driven by the outbox (frozen <see cref="OutboxConsumer"/>: at-least-once delivery, exactly-once effect per consumer):
/// ChannelPushQueued → the push is acknowledged (status Delivered, ack SIM-n; a real channel manager would be called here);
/// reservation created / amended / cancelled / no-show and published yield tariffs → every Connected channel of the property is marked "push due"
/// (ari_dirty_utc) until the next push. Nothing leaves the server.
/// </summary>
public sealed class OtaChannelManagerProjector(OutboxConsumer outbox, SqlConnectionFactory connections, TimeProvider clock)
{
    public const string ConsumerName = "ota-channel-manager";

    public static readonly IReadOnlyCollection<string> EventTypes =
    [
        OtaEvents.PushQueued, OtaEvents.ReservationCreated, OtaEvents.ReservationAmended, OtaEvents.ReservationCancelled, OtaEvents.ReservationNoShow,
        OtaEvents.YieldTariffsPublished,
    ];

    public Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default) => outbox.ConsumeAsync(ConsumerName, EventTypes, ApplyAsync, batchSize, ct);

    public async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var now = clock.GetUtcNow().UtcDateTime;
        string sql = evt.EventType switch
        {
            OtaEvents.PushQueued => "UPDATE ota.push SET status = 'Delivered', delivered_utc = @now, ack_ref = 'SIM-' + CAST(push_id AS VARCHAR(20)) WHERE push_id = @agg AND status = 'Queued';",
            OtaEvents.YieldTariffsPublished => "UPDATE ota.channel SET ari_dirty_utc = ISNULL(ari_dirty_utc, @now) WHERE status = 'Connected' AND property_id = (SELECT property_id FROM yld.publish WHERE publish_id = @agg);",
            _ => "UPDATE ota.channel SET ari_dirty_utc = ISNULL(ari_dirty_utc, @now) WHERE status = 'Connected' AND property_id = (SELECT property_id FROM inv.reservation WHERE reservation_id = @agg);",
        };
        await using var cmd = connections.Command(sql, connection, transaction);
        cmd.Parameters.Add("@agg", SqlDbType.BigInt).Value = evt.AggregateId;
        var p = cmd.Parameters.Add("@now", SqlDbType.DateTime2);
        p.Scale = 3;
        p.Value = now;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
