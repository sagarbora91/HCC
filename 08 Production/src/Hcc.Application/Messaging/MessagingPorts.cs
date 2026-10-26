using Hcc.Application.Commands;
using Hcc.Domain.Messaging;

namespace Hcc.Application.Messaging;

/// <summary>
/// PERM ids used by messaging. RBAC v1 has no messaging PERM, so the module reuses the ids the prototype maps to
/// (<c>reservations</c> → RES, <c>checkin</c> → STY, <c>approve</c> → APR) instead of adding PERM ids (decision listed in the W2-10 report).
/// </summary>
public static class MessagingPermissions
{
    public const string View = "PERM-RES-001";
    public const string Send = "PERM-RES-002";
    public const string ConsentFallback = "PERM-STY-001";
    public const string EditTemplates = "PERM-APR-001";
}

/// <summary>Messaging settings. Check-in/out times belong to Master configuration (W2-1) later; defaults = prototype fixture.</summary>
// CONFIG-PORT: read check-in/out times and the hotel name from the Master configuration provider once W2-1 is merged.
public sealed class MessagingOptions
{
    public string CheckInTime { get; init; } = "14:00";

    public string CheckOutTime { get; init; } = "11:00";
}

/// <summary>An outbound message handed to the gateway (no body, no contact: the simulation needs neither).</summary>
public sealed record OutboundMessage(long MessageId, string Channel, bool ValidRecipient);

/// <summary>
/// Gateway port. ALL SENDING IS SIMULATED: <see cref="FakeMessageGateway"/> returns the predetermined outcome of the prototype;
/// a real WhatsApp/SMS/e-mail gateway would submit after commit and report statuses by callback onto the same columns.
/// </summary>
public interface IMessageGateway
{
    /// <summary>The (simulated) final disposition the provider will report for this message: Read, Delivered or Failed.</summary>
    string Submit(OutboundMessage message);
}

/// <summary>Deterministic simulation (prototype <c>msgOutcome</c>): nothing leaves the server.</summary>
public sealed class FakeMessageGateway : IMessageGateway
{
    public string Submit(OutboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return MessagingRules.Outcome(MessagingRules.WireId(message.MessageId), message.Channel, message.ValidRecipient);
    }
}

public sealed record MsgBooking(long ReservationId, string PublicNo, string Status, long? GuestId, string GuestName, MsgContact Contact,
    string? RoomNo, DateOnly Arrival, DateOnly Departure, string Hotel);

public sealed record MsgFolioFigures(long TotalPaise, long BalancePaise, long? ReceiptPaise);

public sealed record NewMessage(long MessageId, int PropertyId, long ReservationId, long? GuestId, string? TemplateCode, string Channel, string RecipientMasked,
    string? Subject, string Body, DateTime QueuedUtc, string Outcome, string? BlockedReason, string? RuleId, string? TriggerKey, long? RetryOf, int? Actor, DateTime CreatedUtc);

public sealed record StoredMessage(long MessageId, long ReservationId, string BookingNo, long? GuestId, string GuestName, string? TemplateCode, string Channel,
    string RecipientMasked, string? Subject, string Body, DateTime QueuedUtc, string Outcome, int BoostMinutes, string? BlockedReason, string? RuleId,
    long? RetryOf, long? RetriedBy, byte[] Version)
{
    public bool Blocked => BlockedReason is not null;
}

public sealed record StoredTemplate(MsgTemplateDef Def, byte[] Version);

public sealed record StoredRule(MsgRuleDef Def, byte[] Version);

public sealed record StoredConsent(MsgConsent Consent, DateTime UpdatedUtc, byte[] Version);

/// <summary>A state-driven trigger that no outbox event announces (R2 arrival today/tomorrow, R3 in house).</summary>
public sealed record SweepCandidate(string Event, long ReservationId);

/// <summary>
/// Data access of messaging inside one transaction — either a command transaction or the outbox consumer's transaction
/// (the same <see cref="TriggerEngine"/> runs in both). Locks follow contracts §4 rule 8 (locked check-then-insert).
/// </summary>
public interface IMessagingData
{
    Task EnsureDefaultsAsync(int propertyId, DateTime utc, CancellationToken ct = default);

    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);

    Task<MsgBooking?> LoadBookingAsync(int propertyId, long reservationId, CancellationToken ct = default);

    /// <summary>Folio totals via the billing domain rules; <paramref name="receiptId"/> adds that receipt's amount.</summary>
    Task<MsgFolioFigures> FolioFiguresAsync(long reservationId, long? receiptId, CancellationToken ct = default);

    Task<long?> ReservationOfFolioAsync(long folioId, CancellationToken ct = default);

    Task<int?> PropertyOfReservationAsync(long reservationId, CancellationToken ct = default);

    Task<MsgConsent> GetConsentAsync(int propertyId, long? guestId, CancellationToken ct = default);

    Task<IReadOnlyList<MsgRuleDef>> EnabledRulesForEventAsync(int propertyId, string evt, CancellationToken ct = default);

    Task<StoredTemplate?> GetTemplateAsync(int propertyId, string code, CancellationToken ct = default);

    Task<long> NextMessageIdAsync(CancellationToken ct = default);

    /// <summary>False (nothing inserted) when a message for <c>TriggerKey</c> already exists.</summary>
    Task<bool> InsertMessageAsync(NewMessage message, CancellationToken ct = default);

    Task<IReadOnlyList<SweepCandidate>> SweepCandidatesAsync(int propertyId, DateOnly businessDate, CancellationToken ct = default);

    Task<StoredMessage?> LockMessageAsync(int propertyId, long messageId, CancellationToken ct = default);

    Task<IReadOnlyList<StoredMessage>> MessagesAsync(int propertyId, long? onlyId, long? guestId, int limit, CancellationToken ct = default);

    Task AddBoostAsync(long messageId, int minutes, CancellationToken ct = default);

    Task BlockMessageAsync(long messageId, string reason, CancellationToken ct = default);

    Task SetRetriedByAsync(long messageId, long retriedBy, CancellationToken ct = default);

    Task<bool> GuestExistsAsync(int propertyId, long guestId, CancellationToken ct = default);

    Task<StoredConsent?> GetStoredConsentAsync(int propertyId, long guestId, CancellationToken ct = default);

    Task UpsertConsentAsync(int propertyId, long guestId, MsgConsent consent, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Every stored consent row of the property (guest id, flags, version).</summary>
    Task<IReadOnlyList<(long GuestId, StoredConsent Consent)>> ConsentsAsync(int propertyId, CancellationToken ct = default);

    /// <summary>Registered guests of Confirmed reservations and in-house stays (reservation id, guest id).</summary>
    Task<IReadOnlyList<(long ReservationId, long GuestId)>> ActiveGuestsAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<StoredTemplate>> TemplatesAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<StoredRule>> RulesAsync(int propertyId, CancellationToken ct = default);

    Task<StoredTemplate?> LockTemplateAsync(int propertyId, string code, CancellationToken ct = default);

    Task<StoredRule?> LockRuleAsync(int propertyId, string ruleId, CancellationToken ct = default);

    Task UpdateTemplateAsync(int propertyId, string code, byte[] expected, string subject, string body, string channel, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateRuleAsync(int propertyId, string ruleId, byte[] expected, bool enabled, string channel, int principalId, DateTime utc, CancellationToken ct = default);
}

/// <summary>Opens <see cref="IMessagingData"/> on a command's transaction (Infrastructure implements it).</summary>
public interface IMessagingDataFactory
{
    IMessagingData For(ICommandTransaction tx);

    /// <summary>Read-only access on its own connection (queries).</summary>
    Task<T> ReadAsync<T>(Func<IMessagingData, CancellationToken, Task<T>> read, CancellationToken ct = default);
}
