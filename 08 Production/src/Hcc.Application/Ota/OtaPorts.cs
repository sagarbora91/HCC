using Hcc.Application.Commands;
using Hcc.Contracts.Ota;

namespace Hcc.Application.Ota;

public sealed record ChannelMap(int RoomTypeId, string RoomType, string ChannelCode, bool StopSell);

public sealed record ChannelRecord(int ChannelId, int PropertyId, string Code, string Name, string Status, int CommissionBps, int MarkupBps,
    DateTime? LastSyncUtc, string StatusNote, DateTime? AriDirtyUtc, byte[] RowVersion, IReadOnlyList<ChannelMap> Map)
{
    public ChannelMap? MapFor(int roomTypeId) => Map.FirstOrDefault(m => m.RoomTypeId == roomTypeId);

    public ChannelMap? MapForCode(string code) => Map.FirstOrDefault(m => string.Equals(m.ChannelCode, code, StringComparison.Ordinal));
}

public sealed record MessageRecord(long MessageId, int PropertyId, int ChannelId, string ExternalId, string Kind, string ChannelRef, string Guest, string? Phone,
    string? Email, string ChannelRoomCode, DateOnly Arrival, DateOnly Departure, int Adults, int Children, long AmountPaise, DateTime? ReceivedUtc,
    string Status, long? ReservationId, int? RoomId, string? Reason, int? DecidedBy, byte[] RowVersion);

public sealed record MessageDraft(int PropertyId, int ChannelId, string ExternalId, string Kind, string ChannelRef, string Guest, string? Phone, string? Email,
    string ChannelRoomCode, DateOnly Arrival, DateOnly Departure, int Adults, int Children, long AmountPaise, string Status, DateTime? ReceivedUtc);

public sealed record PushRowDetail(int RoomTypeId, string RoomType, string ChannelCode, DateOnly Night, int Avail, long RatePaise, bool Closed, string? Why);

public sealed record PushDraft(int PropertyId, int ChannelId, DateTime RequestedUtc, int RequestedBy, string Summary, IReadOnlyList<PushRowDetail> Rows);

public sealed record LedgerEntry(int PropertyId, int ChannelId, long MessageId, long ReservationId, string Kind, long BasePaise, int CommissionBps, long CommissionPaise,
    string Note, DateTime CreatedUtc, int CreatedBy);

public sealed record LedgerTotals(long BasePaise, long CommissionPaise, int? CommissionBps);

/// <summary>Reads and writes of schema ota (SQL adapter in Hcc.Infrastructure.SqlServer/Ota). Writers take the command transaction.</summary>
public interface IOtaStore
{
    Task<IReadOnlyList<ChannelRecord>> ListChannelsAsync(int propertyId, ICommandTransaction? tx = null, CancellationToken ct = default);

    Task<ChannelRecord?> GetChannelAsync(int propertyId, int channelId, ICommandTransaction? tx = null, bool forUpdate = false, CancellationToken ct = default);

    Task<int> InsertChannelAsync(ICommandTransaction tx, int propertyId, string code, string name, int commissionBps, int markupBps,
        IReadOnlyList<(int RoomTypeId, string ChannelCode)> mapping, CancellationToken ct = default);

    /// <summary>Replaces the terms and the whole mapping (stop-sell flags of unchanged room types are kept). STALE_VERSION when the rowversion moved.</summary>
    Task UpdateChannelAsync(ICommandTransaction tx, int propertyId, int channelId, byte[] expected, string name, int commissionBps, int markupBps,
        IReadOnlyList<(int RoomTypeId, string ChannelCode)> mapping, CancellationToken ct = default);

    Task SetStatusAsync(ICommandTransaction tx, int channelId, string status, string note, DateTime? lastSyncUtc, CancellationToken ct = default);

    /// <summary>Sets/clears stop-sell on mapped room types; returns the number of mapping rows changed.</summary>
    Task<int> SetStopSellAsync(ICommandTransaction tx, int propertyId, int? channelId, int roomTypeId, bool on, DateTime nowUtc, CancellationToken ct = default);

    Task MarkPushedAsync(ICommandTransaction tx, int channelId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Check-then-insert under a lock. Returns the stored row and false when (channel, external id) or the New's (channel, ref) already exists.</summary>
    Task<(MessageRecord Message, bool Created)> InsertMessageAsync(ICommandTransaction tx, MessageDraft draft, CancellationToken ct = default);

    Task<MessageRecord?> GetMessageAsync(int propertyId, long messageId, ICommandTransaction? tx = null, bool forUpdate = false, CancellationToken ct = default);

    Task<MessageRecord?> FindNewAsync(int propertyId, int channelId, string channelRef, ICommandTransaction? tx = null, bool forUpdate = false, CancellationToken ct = default);

    Task<IReadOnlyList<(int Id, string Code, int MaxOccupancy)>> ListRoomTypesAsync(int propertyId, ICommandTransaction? tx = null, CancellationToken ct = default);

    Task<IReadOnlyList<MessageRecord>> ListMessagesAsync(int propertyId, string? status, int limit, CancellationToken ct = default);

    Task<MessageRecord?> NextQueuedAsync(ICommandTransaction tx, int propertyId, int? channelId, CancellationToken ct = default);

    Task ActivateAsync(ICommandTransaction tx, long messageId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Sets status (and decision fields); reservation / room only when given.</summary>
    Task SetMessageStateAsync(ICommandTransaction tx, long messageId, string status, string? reason, int? decidedBy, DateTime? decidedUtc, long? reservationId,
        int? roomId, CancellationToken ct = default);

    Task UpdateStayAsync(ICommandTransaction tx, long messageId, DateOnly arrival, DateOnly departure, int adults, int children, long? amountPaise,
        int? roomId, CancellationToken ct = default);

    Task AddLedgerAsync(ICommandTransaction tx, LedgerEntry entry, CancellationToken ct = default);

    Task<LedgerTotals> LedgerTotalsAsync(ICommandTransaction tx, long messageId, CancellationToken ct = default);

    Task<long> InsertPushAsync(ICommandTransaction tx, PushDraft draft, CancellationToken ct = default);

    Task<IReadOnlyList<PushLogView>> ListPushesAsync(int propertyId, int? channelId, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<CommissionRowView>> CommissionRowsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRowView>> ReconciliationRowsAsync(int propertyId, DateOnly from, DateOnly to, int? channelId, CancellationToken ct = default);
}
