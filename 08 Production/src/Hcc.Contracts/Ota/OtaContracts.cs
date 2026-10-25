namespace Hcc.Contracts.Ota;

// W3-1 OTA channel wire contracts. EVERYTHING IS SIMULATED: no network call leaves the server (prototype modules 21-22, fictional channels).

/// <summary>Module PERM ids (migration 2501). Operate = inbox, push, stop-sell; Manage = connection, commission, markup, mapping; ViewMoney = commission ledger and reconciliation.</summary>
public static class OtaPermissions
{
    public const string Operate = "PERM-OTA-001";
    public const string Manage = "PERM-OTA-002";
    public const string ViewMoney = "PERM-OTA-003";
}

/// <summary>Outbox event types written by this module (and the ones its dispatcher listens to).</summary>
public static class OtaEvents
{
    public const string PushQueued = "ChannelPushQueued";
    public const string MessageReceived = "ChannelMessageReceived";
    public const string BookingImported = "ChannelBookingImported";
    public const string BookingModified = "ChannelBookingModified";
    public const string BookingCancelled = "ChannelBookingCancelled";

    // events of other modules that make a channel's availability / rates stale
    public const string ReservationCreated = "ReservationCreated";
    public const string ReservationAmended = "ReservationAmended";
    public const string ReservationCancelled = "ReservationCancelled";
    public const string ReservationNoShow = "ReservationNoShow";
    public const string YieldTariffsPublished = "YieldTariffsPublished";
}

public static class ChannelStatuses
{
    public const string Connected = "Connected";
    public const string Paused = "Paused";
    public const string Error = "Error";
    public const string Disconnected = "Disconnected";
}

public static class ChannelActions
{
    public const string Connect = "connect";
    public const string Pause = "pause";
    public const string Disconnect = "disconnect";
}

public static class MessageKinds
{
    public const string New = "New";
    public const string Modify = "Modify";
    public const string Cancel = "Cancel";
}

public static class MessageStatuses
{
    public const string Queued = "Queued";
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Applied = "Applied";
    public const string Rejected = "Rejected";
}

// ───────────── commands (every one needs Idempotency-Key; versioned ones carry expectedVersion = base64 rowversion) ─────────────
public sealed record ChannelRoomMapping(int RoomTypeId, string ChannelCode);

/// <summary>Create (ChannelId null) or change a channel's terms. A new channel starts Disconnected. Mapping replaces the whole list (blank code = unmapped).</summary>
public sealed record SaveChannelRequest(int? ChannelId, string? ExpectedVersion, string Code, string Name, int CommissionBps, int MarkupBps,
    IReadOnlyList<ChannelRoomMapping>? Mapping);

public sealed record SetChannelStatusRequest(int ChannelId, string ExpectedVersion, string Action);

/// <summary>Stop-sell a room type on one channel (ChannelId) or on every channel (null).</summary>
public sealed record SetChannelStopSellRequest(int? ChannelId, int RoomTypeId, bool On);

public sealed record PushChannelRatesRequest(int ChannelId);

/// <summary>A message as the (simulated) channel manager delivers it. ExternalId is the channel's own message id: delivering it again is a no-op.</summary>
public sealed record ChannelMessageInput(int ChannelId, string ExternalId, string Kind, string ChannelRef, string Guest, string? Phone, string? Email,
    string ChannelRoomCode, string Arrival, string Departure, int Adults, int Children, long AmountPaise);

/// <summary>Inbound webhook stand-in: stores the message as Pending (channel must be Connected).</summary>
public sealed record ImportChannelMessageRequest(ChannelMessageInput Message);

/// <summary>Scripts a message the simulated channel will deliver on the next pull (Manager).</summary>
public sealed record QueueSimulatedMessageRequest(ChannelMessageInput Message);

/// <summary>Delivers the next scripted message of a Connected channel into the inbox (all channels when ChannelId is null).</summary>
public sealed record PullChannelMessagesRequest(int? ChannelId = null);

/// <summary>Accept: New -> reservation; Modify -> amend; Cancel -> cancel. RoomId picks a specific room of the mapped type for a New.</summary>
public sealed record AcceptChannelMessageRequest(long MessageId, string ExpectedVersion, int? RoomId = null);

public sealed record RejectChannelMessageRequest(long MessageId, string ExpectedVersion, string Reason);

// ───────────── results ─────────────
public sealed record ChannelResult(int ChannelId, string Version, string Status, string Summary);

public sealed record ChannelMessageResult(long MessageId, string Version, string Status, bool Duplicate, string Summary);

public sealed record AcceptResult(long MessageId, string Version, string Status, long? ReservationId, string? PublicNo, int? RoomId, string Summary,
    long CommissionPaise);

public sealed record PushResult(long PushId, string Status, int Rows, int Closed, string Summary);

public sealed record PullResult(long? MessageId, string Summary);

// ───────────── views ─────────────
public sealed record ChannelMappingView(int RoomTypeId, string RoomType, string ChannelCode, bool StopSell);

public sealed record ChannelView(int ChannelId, string Code, string Name, string Status, string Version, int CommissionBps, int MarkupBps,
    string? LastSync, string StatusNote, bool PushDue, int PendingMessages, IReadOnlyList<ChannelMappingView> Mapping);

public sealed record ChannelMessageView(long MessageId, string Version, int ChannelId, string Channel, string ExternalId, string Kind, string ChannelRef,
    string Guest, string? Phone, string? Email, string ChannelRoomCode, int? RoomTypeId, string? RoomType, string Arrival, string Departure,
    int Adults, int Children, long AmountPaise, string? ReceivedAt, string Status, long? ReservationId, string? PublicNo, string? Room, string? Reason);

/// <summary>Pre-check of an inbox message against live availability (prototype chnAcceptCheck).</summary>
public sealed record MessageCheckView(bool Ok, string Reason, string? Room, int? RoomId, string? Warning);

public sealed record PushRowView(string RoomType, string ChannelCode, string Night, int Avail, long RatePaise, bool Closed, string? Why);

public sealed record PushPreviewView(int ChannelId, string BusinessDate, int Days, IReadOnlyList<PushRowView> Rows, IReadOnlyList<string> Unmapped, int Closed);

public sealed record PushLogView(long PushId, int ChannelId, string Channel, string RequestedAt, int Rows, int Closed, string Summary, string Status,
    string? DeliveredAt, string? AckRef);

public sealed record CommissionRowView(long MessageId, long ReservationId, string PublicNo, int ChannelId, string ChannelRef, string Guest, string Arrival,
    string Departure, string ReservationStatus, int Nights, long BasePaise, int CommissionBps, long CommissionPaise, long NetPaise);

public sealed record CommissionChannelTotal(int ChannelId, string Channel, int Bookings, int Nights, long BasePaise, long CommissionPaise, long NetPaise);

/// <summary>Commission on live bookings (Confirmed / InHouse / Departed): base = agreed room revenue (net of GST), commission = base x rate, half away from zero.</summary>
public sealed record CommissionReportView(string From, string To, IReadOnlyList<CommissionRowView> Rows, IReadOnlyList<CommissionChannelTotal> ByChannel,
    CommissionChannelTotal Total);

public sealed record ReconciliationRowView(long MessageId, long ReservationId, string PublicNo, string Channel, string ChannelRef, string ReservationStatus,
    string ChannelArrival, string ChannelDeparture, string Arrival, string Departure, long ChannelAmountPaise, long HotelBasePaise, long AmountVariancePaise,
    long LedgerCommissionPaise, long ExpectedCommissionPaise, long CommissionVariancePaise, IReadOnlyList<string> Flags);

/// <summary>Channel messages vs the hotel's reservations vs the commission ledger. Matched = no flags.</summary>
public sealed record ReconciliationView(string From, string To, int Bookings, int Matched, long LedgerCommissionPaise, long ExpectedCommissionPaise,
    long CommissionVariancePaise, IReadOnlyList<ReconciliationRowView> Rows);
