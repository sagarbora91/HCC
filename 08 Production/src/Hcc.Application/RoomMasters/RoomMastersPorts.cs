using Hcc.Application.Commands;

namespace Hcc.Application.RoomMasters;

/// <summary>The four lists of the module that share status / order / delete operations (table + id column chosen by the SQL adapter).</summary>
public enum RoomMasterKind
{
    RoomType,
    Amenity,
    OccupancyCharge,
    Room,
}

/// <summary>A room type as stored (inv.room_type; RackPaise = the base rack rate, tariffs separately).</summary>
public sealed record RoomTypeRec(int RoomTypeId, string Code, string Name, int MaxAdults, int MaxChildren, bool ExtraBedAllowed, string? BedType, int? SizeSqft,
    IReadOnlyList<string> Amenities, string? Description, int SortOrder, bool Active, DateOnly? RetiredOn, long RackPaise, DateTime UpdatedUtc, byte[] RowVer);

/// <summary>A room as the rooms master stores it (inv.room + MA-1 columns). FloorCode = the floor row's code, or F&lt;hundreds digit&gt;.</summary>
public sealed record RoomRec(int RoomId, string RoomNo, int? FloorId, string FloorCode, int RoomTypeId, string RoomTypeCode, IReadOnlyList<string> Features, bool Smoking,
    string? ConnectingRoomNo, string? Extension, string? Notes, bool Active, DateOnly? RetiredOn, long? RetireBlockId, byte[] RowVer);

public sealed record AmenityRec(int AmenityId, string Code, string Name, string? Icon, bool ShowOnWebsite, int SortOrder, bool Active, DateOnly? RetiredOn, byte[] RowVer);

/// <summary>An occupancy-charge row; Rates = kind ('Adult' | 'Child' | 'Bed') → dated amounts.</summary>
public sealed record OccupancyRec(int ChargeId, string Code, string Name, int RoomTypeId, string RoomTypeCode, int? ChildAgeFrom, int? ChildAgeTo, int SortOrder, bool Active,
    DateOnly? RetiredOn, byte[] RowVer, IReadOnlyDictionary<string, IReadOnlyList<(DateOnly From, long Paise)>> Rates);

/// <summary>How a room type is used: rooms (all / in service) and reservations that name it.</summary>
public sealed record RoomTypeUsage(int Rooms, int RoomsInService, int Reservations)
{
    public int Total => Rooms + Reservations;
}

/// <summary>Live room nights of a room from the business date on (they lock renumbering, type change, retiring).</summary>
public sealed record LiveNights(int Count, DateOnly? Last, IReadOnlyList<string> Reservations);

/// <summary>Write side (inside the command transaction; rows locked with UPDLOCK).</summary>
public interface IRoomMastersStore
{
    Task<IReadOnlyList<RoomTypeRec>> LockRoomTypesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<(DateOnly From, long Paise)>> TariffsAsync(ICommandTransaction tx, int roomTypeId, CancellationToken ct = default);

    Task<int> InsertRoomTypeAsync(ICommandTransaction tx, int propertyId, RoomTypeRec row, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateRoomTypeAsync(ICommandTransaction tx, RoomTypeRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Replaces the type's tariffs from <paramref name="from"/> on with <paramref name="rates"/> (inv.room_type_tariff).</summary>
    Task ReplaceTariffsFromAsync(ICommandTransaction tx, int roomTypeId, DateOnly from, IReadOnlyList<(DateOnly From, long Paise)> rates, CancellationToken ct = default);

    Task<RoomTypeUsage> RoomTypeUsageAsync(ICommandTransaction tx, int roomTypeId, CancellationToken ct = default);

    Task<IReadOnlyList<AmenityRec>> LockAmenitiesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<int> InsertAmenityAsync(ICommandTransaction tx, int propertyId, AmenityRec row, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateAmenityAsync(ICommandTransaction tx, AmenityRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Room types and rooms that list the amenity code.</summary>
    Task<int> AmenityUsageAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default);

    Task<IReadOnlyList<OccupancyRec>> LockOccupancyChargesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<int> InsertOccupancyChargeAsync(ICommandTransaction tx, int propertyId, OccupancyRec row, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateOccupancyChargeAsync(ICommandTransaction tx, OccupancyRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);

    Task<IReadOnlyList<RoomRec>> LockRoomsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Inserts a room (hk Ready) and raises the type's sellable count on every materialised night.</summary>
    Task<int> InsertRoomAsync(ICommandTransaction tx, int propertyId, string roomNo, int floorId, int roomTypeId, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Number, floor and descriptive fields (not the type).</summary>
    Task UpdateRoomAsync(ICommandTransaction tx, RoomRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Moves the room to another type: sellable −1 / +1 on materialised nights from <paramref name="from"/> where the room is not blocked.</summary>
    Task ChangeRoomTypeAsync(ICommandTransaction tx, int propertyId, int roomId, int oldTypeId, int newTypeId, DateOnly from, CancellationToken ct = default);

    Task<LiveNights> LiveNightsAsync(ICommandTransaction tx, int roomId, DateOnly from, CancellationToken ct = default);

    Task SetRoomActiveAsync(ICommandTransaction tx, int roomId, byte[] expectedVersion, bool active, DateOnly? retiredOn, long? retireBlockId, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Reservations, room nights, blocks and housekeeping records that reference the room (delete only at 0).</summary>
    Task<int> RoomReferencesAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default);

    /// <summary>Deletes a never-used room and lowers the type's sellable count on every materialised night.</summary>
    Task DeleteRoomAsync(ICommandTransaction tx, int propertyId, int roomId, int roomTypeId, byte[] expectedVersion, CancellationToken ct = default);

    Task SetActiveAsync(ICommandTransaction tx, RoomMasterKind list, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetSortAsync(ICommandTransaction tx, RoomMasterKind list, int id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, RoomMasterKind list, int id, byte[] expectedVersion, CancellationToken ct = default);
}

/// <summary>Read side (own connection).</summary>
public interface IRoomMastersReadStore
{
    Task<IReadOnlyList<(RoomTypeRec Type, int Rooms, IReadOnlyList<(DateOnly From, long Paise)> Tariffs)>> RoomTypesAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<RoomRec>> RoomsAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<AmenityRec>> AmenitiesAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<OccupancyRec>> OccupancyChargesAsync(int propertyId, CancellationToken ct = default);
}
