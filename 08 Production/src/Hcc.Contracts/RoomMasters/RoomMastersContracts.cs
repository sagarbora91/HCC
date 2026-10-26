namespace Hcc.Contracts.RoomMasters;

// Wire contracts of the RoomMasters module (MA-1: room types M2, rooms M3, amenities M4, occupancy and extra charges M5; plan Part 2 §8).
// Master errors travel like MA-0's (VALIDATION / INVALID_TRANSITION with details.reason = Hcc.Contracts.Masters.MasterReasons.*). Money is
// integer paise; a dated amount list is [{amountPaise, effectiveFrom}] (prototype MstDatedAmount). The prototype counterpart is
// 03 Prototype/src/domain/streams/roommasters.ts (+ sync/roommasters.ts).

/// <summary>List ids of the module in mst.master_history.master (same ids as the prototype specs).</summary>
public static class RoomMasterIds
{
    public const string RoomTypes = "roomTypes";
    public const string Rooms = "rooms";
    public const string Amenities = "amenities";
    public const string OccupancyCharges = "occupancyCharges";

    /// <summary>effectiveFrom of the base rack rate (inv.room_type.rack_rate_paise) in a room type's dated rack-rate list.</summary>
    public const string BaseFrom = "2000-01-01";
}

/// <summary>One dated amount (paise) in force from <c>EffectiveFrom</c> (YYYY-MM-DD).</summary>
public sealed record DatedAmount(long AmountPaise, string EffectiveFrom);

/// <summary>A room type. <c>RackRates</c> = the base rack rate (from 2000-01-01) + the dated tariffs (inv.room_type_tariff); empty for a
/// reader without money visibility. <c>RoomCount</c> = rooms of the type (all, incl. retired).</summary>
public sealed record RoomTypeView(int RoomTypeId, string Code, string Name, int MaxAdults, int MaxChildren, bool ExtraBedAllowed, string? BedType, int? SizeSqft,
    IReadOnlyList<string> Amenities, string? Description, int SortOrder, bool Active, string? RetiredOn, int RoomCount, IReadOnlyList<DatedAmount> RackRates,
    string UpdatedUtc, string Version);

public sealed record RoomTypeList(int Page, int PageSize, int Total, IReadOnlyList<RoomTypeView> Items);

/// <summary>SaveRoomType K [V]: <c>RoomTypeId</c> null = create. <c>RackRates</c> null = unchanged; otherwise the whole dated list
/// (amounts before the business date must stay as they are; the Owner only — PERM-MST-002 — may change them).</summary>
public sealed record SaveRoomTypeRequest(int? RoomTypeId, string Code, string Name, int MaxAdults, int MaxChildren = 0, bool ExtraBedAllowed = false,
    string? BedType = null, int? SizeSqft = null, IReadOnlyList<string>? Amenities = null, string? Description = null, int? SortOrder = null,
    IReadOnlyList<DatedAmount>? RackRates = null, string? ExpectedVersion = null);

public sealed record RoomTypeIdRequest(int RoomTypeId, string ExpectedVersion);

public sealed record ReorderRoomTypesRequest(IReadOnlyList<int> RoomTypeIds);

/// <summary>A room as the rooms master shows it (no money). <c>FloorCode</c> = the floor's code, or F&lt;hundreds digit&gt; when the
/// room has no floor_id yet (rooms imported before MA-1).</summary>
public sealed record RoomMasterView(int RoomId, string RoomNo, int? FloorId, string FloorCode, int RoomTypeId, string RoomTypeCode, IReadOnlyList<string> Features,
    bool Smoking, string? ConnectingRoomNo, string? Extension, string? Notes, bool Active, string? RetiredOn, string Version);

public sealed record RoomMasterList(IReadOnlyList<RoomMasterView> Items);

/// <summary>AddRooms K: 1–50 new room numbers on one floor, of one type (range add "101–110"). Each number must be unused.</summary>
public sealed record AddRoomsRequest(IReadOnlyList<string> RoomNos, int FloorId, int RoomTypeId);

public sealed record AddRoomsResult(IReadOnlyList<RoomMasterView> Rooms);

/// <summary>SaveRoom K V: edit one room. A number change = renumber, a type change needs no booking from the business date on
/// (MASTER_LOCKED_BY_BOOKINGS).</summary>
public sealed record SaveRoomRequest(int RoomId, string RoomNo, int FloorId, int RoomTypeId, IReadOnlyList<string>? Features = null, bool Smoking = false,
    string? ConnectingRoomNo = null, string? Extension = null, string? Notes = null, string? ExpectedVersion = null);

/// <summary>RenumberRoom K V: new number, only with no booking from the business date on; past stays keep pointing at the room.</summary>
public sealed record RenumberRoomRequest(int RoomId, string RoomNo, string? ExpectedVersion = null);

public sealed record RoomIdRequest(int RoomId, string? ExpectedVersion = null);

public sealed record DeleteMasterResult(long RowId, string Code, bool Deleted);

/// <summary>Result of a status change / reorder: the row (0 for a reorder), its code (the new order for a reorder) and whether it is active.</summary>
public sealed record StatusResult(long RowId, string Code, bool Active);

/// <summary>An amenity (room feature) picked by room types and rooms; <c>ShowOnWebsite</c> = shown on the booking site.</summary>
public sealed record AmenityView(int AmenityId, string Code, string Name, string? Icon, bool ShowOnWebsite, int SortOrder, bool Active, string? RetiredOn, string Version);

public sealed record AmenityList(int Page, int PageSize, int Total, IReadOnlyList<AmenityView> Items);

public sealed record SaveAmenityRequest(int? AmenityId, string Code, string Name, string? Icon = null, bool ShowOnWebsite = true, int? SortOrder = null, string? ExpectedVersion = null);

public sealed record AmenityIdRequest(int AmenityId, string ExpectedVersion);

public sealed record ReorderAmenitiesRequest(IReadOnlyList<int> AmenityIds);

/// <summary>Occupancy and extra charges of one room type (money list, Owner only). Amount lists are empty for a reader without money
/// visibility.</summary>
public sealed record OccupancyChargeView(int ChargeId, string Code, string Name, int RoomTypeId, string RoomTypeCode, int? ChildAgeFrom, int? ChildAgeTo,
    IReadOnlyList<DatedAmount> ExtraAdult, IReadOnlyList<DatedAmount> ExtraChild, IReadOnlyList<DatedAmount> ExtraBed, int SortOrder, bool Active, string? RetiredOn, string Version);

public sealed record OccupancyChargeList(int Page, int PageSize, int Total, IReadOnlyList<OccupancyChargeView> Items);

public sealed record SaveOccupancyChargeRequest(int? ChargeId, string Code, string Name, int RoomTypeId, int? ChildAgeFrom = null, int? ChildAgeTo = null,
    IReadOnlyList<DatedAmount>? ExtraAdult = null, IReadOnlyList<DatedAmount>? ExtraChild = null, IReadOnlyList<DatedAmount>? ExtraBed = null,
    int? SortOrder = null, string? ExpectedVersion = null);

public sealed record OccupancyChargeIdRequest(int ChargeId, string ExpectedVersion);

public sealed record ReorderOccupancyChargesRequest(IReadOnlyList<int> ChargeIds);
