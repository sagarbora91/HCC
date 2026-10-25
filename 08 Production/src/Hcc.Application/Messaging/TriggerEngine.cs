using System.Globalization;
using Hcc.Domain.Messaging;

namespace Hcc.Application.Messaging;

public enum FireResult
{
    Queued,
    NotSent,
    Duplicate,
    Skipped,
}

/// <summary>
/// Turns a business event into messages (prototype <c>msgPendingTriggers</c> + <c>MSG_RUN_RULES</c>), inside whatever transaction
/// <see cref="IMessagingData"/> is bound to: a trigger consumer's outbox transaction or the RunMessagingTriggers command.
/// One message per (rule, event) through <c>msg.message.trigger_key</c>, so redelivery and re-runs never double-send. A message that
/// consent, opt-out or a missing contact forbids is still recorded, as "Not sent" with the reason (visible, never delivered).
/// </summary>
public sealed class TriggerEngine(IMessageGateway gateway, MessagingOptions options)
{
    /// <summary>Trigger key of a (rule, reservation) or (rule, receipt) event.</summary>
    public static string Key(string ruleId, long reservationId, long? receiptId) =>
        receiptId is { } r ? $"{ruleId}:rcpt:{r.ToString(CultureInfo.InvariantCulture)}" : $"{ruleId}:res:{reservationId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Fires every enabled rule of <paramref name="evt"/> for the reservation.</summary>
    public async Task<(int Queued, int NotSent)> OnEventAsync(IMessagingData d, int propertyId, string evt, long reservationId, long? receiptId, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(d);
        await d.EnsureDefaultsAsync(propertyId, nowUtc, ct).ConfigureAwait(false);
        int queued = 0, notSent = 0;
        foreach (var rule in await d.EnabledRulesForEventAsync(propertyId, evt, ct).ConfigureAwait(false))
        {
            switch (await FireAsync(d, propertyId, rule, reservationId, receiptId, nowUtc, ct).ConfigureAwait(false))
            {
                case FireResult.Queued: queued++; break;
                case FireResult.NotSent: notSent++; break;
            }
        }

        return (queued, notSent);
    }

    public async Task<FireResult> FireAsync(IMessagingData d, int propertyId, MsgRuleDef rule, long reservationId, long? receiptId, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(rule);
        var booking = await d.LoadBookingAsync(propertyId, reservationId, ct).ConfigureAwait(false);
        var template = await d.GetTemplateAsync(propertyId, rule.TemplateCode, ct).ConfigureAwait(false);
        if (booking is null || template is null) return FireResult.Skipped;

        var figures = await d.FolioFiguresAsync(reservationId, receiptId, ct).ConfigureAwait(false);
        var values = MessagingRules.Values(new MsgBookingFacts(booking.Hotel, booking.PublicNo, booking.GuestName, booking.RoomNo, booking.Arrival, booking.Departure,
            options.CheckInTime, options.CheckOutTime, figures.TotalPaise, figures.BalancePaise, figures.ReceiptPaise));
        var consent = await d.GetConsentAsync(propertyId, booking.GuestId, ct).ConfigureAwait(false);
        var why = MessagingRules.BlockReason(consent, booking.Contact, rule.Channel);
        var recipient = MessagingRules.Recipient(booking.Contact, rule.Channel);
        var id = await d.NextMessageIdAsync(ct).ConfigureAwait(false);
        var outcome = gateway.Submit(new OutboundMessage(id, rule.Channel, recipient.Valid));
        var body = MessagingRules.Render(template.Def.Body, values).Text;
        var subject = rule.Channel == MsgChannel.Email ? MessagingRules.Render(template.Def.Subject, values).Text : null;
        var inserted = await d.InsertMessageAsync(new NewMessage(id, propertyId, reservationId, booking.GuestId, rule.TemplateCode, rule.Channel, recipient.Masked, subject, body,
            nowUtc, outcome, why.Length > 0 ? why : null, rule.RuleId, Key(rule.RuleId, reservationId, receiptId), null, null, nowUtc), ct).ConfigureAwait(false);
        return !inserted ? FireResult.Duplicate : why.Length > 0 ? FireResult.NotSent : FireResult.Queued;
    }
}
