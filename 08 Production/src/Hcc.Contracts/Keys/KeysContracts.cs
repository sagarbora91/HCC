namespace Hcc.Contracts.Keys;

// Room keys (HCC_KeyAgent_Spec_v1). Staff endpoints: POST /api/v1/commands/{RequestKey|CancelKey|ReadKey|RecordManualKey|SaveKeyLock|RegisterKeyEncoder},
// GET /api/v1/keys/{panel|log|locks|encoder}. Agent endpoints (bearer token per encoder): GET /api/v1/keys/jobs, POST /keys/jobs/{id}/state|result,
// POST /keys/encoders/{id}/heartbeat.

/// <summary>Event = issued | reissued | extended | reactivated; Reason (extension | lost | damaged | other) is required for all but issued; ValidTo yyyy-MM-dd (last day, default the reservation departure).</summary>
public sealed record RequestKeyRequest(long ReservationId, string Event, string? Reason, string? ValidTo, int Cards = 1);

public sealed record CancelKeyRequest(long ReservationId);

public sealed record ReadKeyRequest(string? Note);

/// <summary>"Key issued manually" record: Event and Reason as for RequestKeyRequest; nothing is queued.</summary>
public sealed record RecordManualKeyRequest(long ReservationId, string Event, string? Reason, string? Note);

public sealed record SaveKeyLockRequest(int RoomId, bool HasLock, int? LockNo, string? DoorId, string? DoorName, string? Note, string ExpectedVersion);

public sealed record RegisterKeyEncoderRequest(string EncoderId, string PcName);

public sealed record RegisterKeyEncoderResult(string EncoderId, string Token);

public sealed record KeyJobView(string JobId, string Kind, string Event, string? Reason, long? ReservationId, int? RoomId, string? RoomNo, string State, string? ErrorCode,
    string? Message, string? CardNo, int RequestedBy, string RequestedByName, string RequestedUtc, string TimeoutUtc, string? FinishedUtc, string? EncoderId, string? Warning, string? Hint);

/// <summary>Counts derived from the log of successful (done or manual) events; Reactivated here is extended + reactivated for the panel line.</summary>
public sealed record KeyCountsView(int Issued, int Reissued, int Extended, int Reactivated, int Cancelled, int Read)
{
    public int ReactivatedTotal => Extended + Reactivated;
}

public sealed record KeyCardView(long CardId, string? CardNo, int RoomId, string RoomNo, string Event, string? Reason, string BeginAt, string EndAt, string Status, int IssuedBy, string IssuedUtc);

public sealed record KeyEncoderView(string? EncoderId, string? PcName, bool Online, string? Adapter, string? Detail, string? LastSeenUtc);

public sealed record KeyPanelView(long ReservationId, int? RoomId, string? RoomNo, bool RoomHasLock, string? LockNote, KeyCountsView BookingCounts, KeyCountsView RoomCounts,
    IReadOnlyList<KeyCardView> ActiveCards, IReadOnlyList<KeyJobView> Jobs, KeyEncoderView Encoder, string? Warning, string CardExpiry, string? FolioCheckout);

public sealed record KeyLogRow(string JobId, string RequestedUtc, string Event, string? Reason, string? RoomNo, string? BookingNo, long? ReservationId, int RequestedBy, string RequestedByName, string State, string? ErrorCode, string? CardNo);

public sealed record KeyLogView(IReadOnlyList<KeyLogRow> Rows, KeyCountsView Totals, int Total);

public sealed record KeyLockView(int RoomId, string RoomNo, bool HasLock, int? LockNo, string? DoorId, string? DoorName, string? Note, string Version);

public sealed record KeyLockList(IReadOnlyList<KeyLockView> Locks, IReadOnlyList<KeyEncoderView> Encoders);

// ── Agent side ──
public sealed record AgentJob(string JobId, string Kind, string Room, string? DoorName, string ValidFrom, string ValidTo, int Cards, string GuestRef, string RequestedBy, int TimeoutSec);

public sealed record AgentStateRequest(string State);

public sealed record AgentResultRequest(string Code, string? Message, string? CardNo, string? GuestSn, string? AgentVersion);

public sealed record AgentHeartbeatRequest(string? Adapter, bool Online, string? Detail, bool? X86);
