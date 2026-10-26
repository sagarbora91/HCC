namespace Hcc.Contracts.Messaging;

// W2-10 messaging wire contracts. ALL SENDING IS SIMULATED. Recipients are always masked; no ID number appears anywhere (DEC-068).

public sealed record SendMessageRequest(long ReservationId, string Channel, string Body, string? TemplateCode = null, string? Subject = null);

public sealed record RunMessagingTriggersRequest(string? Note = null);

/// <summary>Advance one message (or every unfinished message when <see cref="MessageId"/> is null) by one simulated delivery step.</summary>
public sealed record SimulateMessageDeliveryRequest(long? MessageId = null);

public sealed record RetryMessageRequest(long MessageId);

public sealed record SetMessagingConsentRequest(long GuestId, bool? WhatsApp = null, bool? Sms = null, bool? Email = null, bool? OptOut = null);

public sealed record UpdateMessageTemplateRequest(string Code, string Body, string ExpectedVersion, string? Subject = null, string? Channel = null);

public sealed record UpdateMessageRuleRequest(string RuleId, string ExpectedVersion, bool? Enabled = null, string? Channel = null);

public sealed record MessageTimelineStep(string Status, string AtUtc);

public sealed record MessageView(long MessageId, string Id, long ReservationId, string BookingNo, string GuestName, string? TemplateCode, string Channel,
    string To, string? Subject, string Body, string Status, string? BlockedReason, string QueuedUtc, IReadOnlyList<MessageTimelineStep> Timeline,
    string? RuleId, long? RetryOf, long? RetriedBy, int? SmsSegments, bool Simulated, string? Outcome = null, int BoostMinutes = 0, long? GuestId = null);

public sealed record MessageListResult(IReadOnlyList<MessageView> Items, int Total, int Page, int PageSize);

public sealed record MessagingSummary(int Queued, int Sent, int Delivered, int Read, int Failed, int NotSent, int PendingTriggers, bool Simulated);

public sealed record SendMessageResult(long MessageId, string Id, string Status, string To, string Summary);

public sealed record RunMessagingTriggersResult(int Queued, int NotSent, string Summary);

public sealed record SimulateMessageDeliveryResult(int Advanced, string Summary);

public sealed record RetryMessageResult(long OriginalId, long NewMessageId, string Summary);

public sealed record ConsentView(long GuestId, bool WhatsApp, bool Sms, bool Email, bool OptOut, string? UpdatedUtc, string? Version);

/// <summary>One registered guest of an active (Confirmed or in-house) reservation: lets the screens key consent by booking.</summary>
public sealed record ConsentGuestRef(long ReservationId, long GuestId);

/// <summary>Every stored consent of the property plus the registered guests of active reservations (GET /messaging/consents).</summary>
public sealed record ConsentRoster(IReadOnlyList<ConsentView> Consents, IReadOnlyList<ConsentGuestRef> Guests);

public sealed record SetMessagingConsentResult(ConsentView Consent, int QueuedStopped, string Summary);

public sealed record TemplateView(string Code, string Name, string Channel, string Subject, string Body, string Version);

public sealed record RuleView(string RuleId, string Event, string TemplateCode, string Channel, bool Enabled, string Version);

public sealed record TemplatesResult(IReadOnlyList<TemplateView> Templates, IReadOnlyList<string> Placeholders);

public sealed record UpdateMessageTemplateResult(TemplateView Template, string Summary);

public sealed record UpdateMessageRuleResult(RuleView Rule, string Summary);

/// <summary>Outbox event types messaging consumes (produced by Reservations, Billing and Stays).</summary>
public static class MessagingEvents
{
    public const string ReservationCreated = "ReservationCreated";
    public const string ReservationConfirmed = "ReservationConfirmed";
    public const string ReceiptRecorded = "ReceiptRecorded";
    public const string StayDeparted = "StayDeparted";
}
