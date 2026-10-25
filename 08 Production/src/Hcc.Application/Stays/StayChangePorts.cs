using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Domain.Inventory;
using Hcc.Domain.Stays;

namespace Hcc.Application.Stays;

/// <summary>An in-house stay with its reservation facts, loaded under row locks (stay, then reservation).</summary>
public sealed record StayChangeContext(
    long StayId, int PropertyId, long ReservationId, long FolioId, string StayStatus, DateOnly Arrival, DateOnly Departure, byte[] StayVersion,
    int CurrentRoomId, int CurrentSegmentSeq, int RoomTypeId, int Adults, int Children, long? NightlyRatePaise, long? GroupBlockId);

public sealed record ChangeRoom(int RoomId, string RoomNo, int RoomTypeId, int MaxOccupancy, string Hk, bool Occupied);

public sealed record StayChangeLog(long StayId, string Kind, DateOnly BusinessDate, int? FromRoomId, int? ToRoomId, DateOnly OldDeparture, DateOnly NewDeparture,
    string RateDecision, long? OldRatePaise, long? NewRatePaise, long RateLossPaise, string Reason);

/// <summary>Prices nights at a fixed rate with each night's GST (served by the inventory module's pricing).</summary>
public interface IStayNightPricer
{
    Task<IReadOnlyList<PricedNight>> PriceAsync(int propertyId, DateOnly from, DateOnly to, long ratePaise, CancellationToken ct = default);
}

public sealed class InventoryStayNightPricer(InventoryService inventory) : IStayNightPricer
{
    public Task<IReadOnlyList<PricedNight>> PriceAsync(int propertyId, DateOnly from, DateOnly to, long ratePaise, CancellationToken ct = default)
        => inventory.PriceAsync(propertyId, from, to, ratePaise, ct);
}

/// <summary>Persistence port of the stay-change commands (SQL adapter in Infrastructure/Stays). Everything runs in the command transaction.</summary>
public interface IStayChangeStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Locks the stay and its reservation (UPDLOCK); null when the stay is not at the property.</summary>
    Task<StayChangeContext?> LockStayAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct = default);

    Task<ChangeRoom?> LockRoomAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default);

    Task<bool> RoomBlockedDuringAsync(ICommandTransaction tx, int roomId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>The reservation's room nights (all rooms), ascending.</summary>
    Task<IReadOnlyList<RoomNightKey>> RoomNightsAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);

    /// <summary>Dates of the folio's canonical room-night charges that are not reversed (the nights already posted).</summary>
    Task<IReadOnlySet<DateOnly>> PostedNightsAsync(ICommandTransaction tx, long folioId, CancellationToken ct = default);

    /// <summary>The agreed rate of the latest night snapshot (fallback when the reservation has no booked rate).</summary>
    Task<long?> LastNightRateAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);

    /// <summary>Deletes the price-snapshot rows of nights on/after <paramref name="from"/> (released nights).</summary>
    Task DeleteNightPricesFromAsync(ICommandTransaction tx, long reservationId, DateOnly from, CancellationToken ct = default);

    /// <summary>Writes (replaces per night) the price snapshot of the given nights.</summary>
    Task WriteNightPricesAsync(ICommandTransaction tx, long reservationId, IReadOnlyList<PricedNight> nights, CancellationToken ct = default);

    /// <summary>Moves the stay into <paramref name="toRoom"/> at the business date: closes the open segment, opens the next, updates the reservation's room/type/rate and bumps the stay version.</summary>
    Task<byte[]> ApplyMoveAsync(ICommandTransaction tx, StayChangeContext s, ChangeRoom toRoom, DateOnly businessDate, long? newRatePaise, CancellationToken ct = default);

    /// <summary>Sets the new departure on the stay and its reservation (and the rate when given); bumps the stay version.</summary>
    Task<byte[]> ApplyDeparturesAsync(ICommandTransaction tx, StayChangeContext s, DateOnly newDeparture, long? newRatePaise, CancellationToken ct = default);

    Task SetRoomHkAsync(ICommandTransaction tx, int roomId, string hk, CancellationToken ct = default);

    /// <summary>One clean task for the vacated room, unique per stay and segment (causal key hk:move:&lt;stay&gt;:&lt;seq&gt;).</summary>
    Task<long> InsertMoveTaskAsync(ICommandTransaction tx, int propertyId, int roomId, long stayId, int segmentSeq, string note, DateOnly businessDate, DateTime utc, CancellationToken ct = default);

    Task InsertChangeAsync(ICommandTransaction tx, int propertyId, StayChangeLog log, int principalId, DateTime utc, CancellationToken ct = default);
}
