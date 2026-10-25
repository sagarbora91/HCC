using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Messaging;
using Hcc.Contracts.Messaging;
using Hcc.Domain.Messaging;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Messaging;

/// <summary>
/// Messaging's outbox consumer (frozen <see cref="OutboxConsumer"/>: at-least-once delivery, exactly-once effect per consumer):
/// ReservationCreated(Confirmed) / ReservationConfirmed → R1, ReceiptRecorded(payment) → R4, StayDeparted → R5 and R6.
/// Redelivery is also harmless because every message carries a unique trigger key. Nothing is sent: messages are simulated.
/// </summary>
public sealed class MessagingTriggerProjector(OutboxConsumer outbox, SqlConnectionFactory connections, TriggerEngine engine, TimeProvider clock)
{
    public const string ConsumerName = "messaging-triggers";

    public static readonly IReadOnlyCollection<string> EventTypes =
        [MessagingEvents.ReservationCreated, MessagingEvents.ReservationConfirmed, MessagingEvents.ReceiptRecorded, MessagingEvents.StayDeparted];

    public Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default) => outbox.ConsumeAsync(ConsumerName, EventTypes, ApplyAsync, batchSize, ct);

    public async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        using var doc = JsonDocument.Parse(evt.PayloadJson);
        var root = doc.RootElement;
        var d = new SqlMessagingData(connection, transaction, connections);
        long? receiptId = null;
        long reservationId;
        string messagingEvent;
        switch (evt.EventType)
        {
            case MessagingEvents.ReservationCreated:
                if (!string.Equals(Str(root, "status"), "Confirmed", StringComparison.Ordinal)) return;
                reservationId = evt.AggregateId;
                messagingEvent = MsgEvent.BookingConfirmed;
                break;
            case MessagingEvents.ReservationConfirmed:
                reservationId = evt.AggregateId;
                messagingEvent = MsgEvent.BookingConfirmed;
                break;
            case MessagingEvents.ReceiptRecorded:
                if (!string.Equals(Str(root, "kind"), FolioTotals.KindCode(ReceiptKind.Payment), StringComparison.Ordinal)) return;
                if (Long(root, "folioId") is not { } folioId || Long(root, "receiptId") is not { } rid) return;
                if (await d.ReservationOfFolioAsync(folioId, ct).ConfigureAwait(false) is not { } res) return;
                reservationId = res;
                receiptId = rid;
                messagingEvent = MsgEvent.PaymentReceived;
                break;
            case MessagingEvents.StayDeparted:
                if (Long(root, "reservationId") is not { } sr) return;
                reservationId = sr;
                messagingEvent = MsgEvent.CheckedOut;
                break;
            default:
                return;
        }

        if (await d.PropertyOfReservationAsync(reservationId, ct).ConfigureAwait(false) is not { } propertyId) return;
        await engine.OnEventAsync(d, propertyId, messagingEvent, reservationId, receiptId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
    }

    private static string? Str(JsonElement root, string name) => Find(root, name) is { ValueKind: JsonValueKind.String } e ? e.GetString() : null;

    private static long? Long(JsonElement root, string name) => Find(root, name) is { ValueKind: JsonValueKind.Number } e && e.TryGetInt64(out var v) ? v : null;

    private static JsonElement? Find(JsonElement root, string name)
    {
        foreach (var p in root.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }
}
