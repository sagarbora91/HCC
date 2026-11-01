using System.Data;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Keys;
using Hcc.Contracts.Stays;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Keys;

/// <summary>
/// The key module outbox consumer: <c>StayDeparted</c> becomes a software cancel of the room key (the guest returns the card at check-out, no check-out card).
/// Check-out never depends on the card (spec 6.8): the hook swallows every failure, so the event is always marked delivered; an offline encoder
/// leaves a failed cancel job that the key log shows as the reconciliation task. One job per stay (idempotency key keys:checkout:stayId).
/// </summary>
public sealed class KeyDepartureProjector(OutboxConsumer outbox, KeyCommands keys)
{
    public const string ConsumerName = "key-checkout-cancel";

    public Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default) => outbox.ConsumeAsync(ConsumerName, [StaysEvents.StayDeparted], ApplyAsync, batchSize, ct);

    public async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var e = JsonSerializer.Deserialize<StayDepartedEvent>(evt.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (e is null) return;
        int? by;
        await using (var cmd = new SqlCommand("SELECT checked_out_by FROM gst.stay WHERE stay_id = @s;", connection, transaction))
        {
            cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = e.StayId;
            by = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as int?;
        }

        if (by is null) return;
        await keys.CancelForDepartureAsync(e.PropertyId, e.ReservationId, by.Value, "keys:checkout:" + e.StayId, ct).ConfigureAwait(false);
    }
}
