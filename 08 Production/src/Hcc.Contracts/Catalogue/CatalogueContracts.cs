namespace Hcc.Contracts.Catalogue;

// Wire contracts of the Catalogue module (MA-2: meal plans M6, extras catalogue M7, discount reasons M8; plan Part 2 §8, DEC-MA-3/4).
// Master errors travel as the frozen codes with details.reason (Hcc.Contracts.Masters.MasterReasons), as in the Masters module.
// Money = integer paise; percentages = basis points; dates yyyy-MM-dd. Prototype: 03 Prototype/src/domain/streams/catalogue.ts.

/// <summary>List ids (mst.master_history.master and the prototype master ids).</summary>
public static class CatalogueIds
{
    public const string MealPlans = "mealPlans";
    public const string Extras = "extras";
    public const string DiscountReasons = "discountReasons";
}

/// <summary>One dated amount: in force from <c>EffectiveFrom</c> until the next one.</summary>
public sealed record DatedAmount(string EffectiveFrom, long AmountPaise);

/// <summary>A meal plan. Meals ⊆ B, L, D; GstTreatment bundled | separate (Illustrative — CA to confirm); RoomTypes empty = every type.</summary>
public sealed record MealPlanView(int MealPlanId, string Code, string Name, IReadOnlyList<string> Meals, string GstTreatment, IReadOnlyList<string> RoomTypes,
    IReadOnlyList<DatedAmount> AddOn, int SortOrder, bool Active, string? RetiredOn, int UsageCount, string UpdatedUtc, string Version);

/// <summary>An extras catalogue item. GstCategory = food | laundry | banquet | other (the GST master keys).</summary>
public sealed record ExtraItemView(int ExtraItemId, string Code, string Name, string Category, string GstCategory, string Unit, string? Department,
    IReadOnlyList<DatedAmount> Prices, int SortOrder, bool Active, string? RetiredOn, int UsageCount, string UpdatedUtc, string Version);

/// <summary>A discount reason. Roles = OWN / MGR / REC (empty = every role that may give discounts).</summary>
public sealed record DiscountReasonView(int DiscountReasonId, string Code, string Name, int MaxBps, int? ApprovalAboveBps, IReadOnlyList<string> Roles,
    int SortOrder, bool Active, string? RetiredOn, int UsageCount, string UpdatedUtc, string Version);

public sealed record CataloguePage<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);

/// <summary>SaveMealPlan K [V] (PERM-MST-002): id null = create; an edit needs ExpectedVersion; amounts in effect cannot change.</summary>
public sealed record SaveMealPlanRequest(int? MealPlanId, string Code, string Name, IReadOnlyList<string>? Meals, string GstTreatment,
    IReadOnlyList<string>? RoomTypes, IReadOnlyList<DatedAmount>? AddOn, int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>SaveExtraItem K [V] (PERM-MST-002).</summary>
public sealed record SaveExtraItemRequest(int? ExtraItemId, string Code, string Name, string Category, string GstCategory, string Unit, string? Department,
    IReadOnlyList<DatedAmount>? Prices, int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>SaveDiscountReason K [V] (PERM-MST-002).</summary>
public sealed record SaveDiscountReasonRequest(int? DiscountReasonId, string Code, string Name, int MaxBps, int? ApprovalAboveBps, IReadOnlyList<string>? Roles,
    int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>Retire / Reactivate / Delete a meal plan (K V).</summary>
public sealed record MealPlanRef(int MealPlanId, string ExpectedVersion);

public sealed record ExtraItemRef(int ExtraItemId, string ExpectedVersion);

public sealed record DiscountReasonRef(int DiscountReasonId, string ExpectedVersion);

public sealed record ReorderMealPlansRequest(IReadOnlyList<int> MealPlanIds);

public sealed record ReorderExtraItemsRequest(IReadOnlyList<int> ExtraItemIds);

public sealed record ReorderDiscountReasonsRequest(IReadOnlyList<int> DiscountReasonIds);

public sealed record CatalogueDeleteResult(string Master, int RowId, string Code, bool Deleted);

/// <summary>Reorder result: the codes in the new order.</summary>
public sealed record CatalogueOrderResult(string Master, IReadOnlyList<string> Codes);

/// <summary>
/// PostExtra K (PERM-FOL-002): a catalogue charge on an open folio. Defaults: label = item name (× qty), amount = the item's price on the
/// business date × qty, GST category = the item's; each may be overridden (DEC-MA-4: no approval). The charge is linked to the item.
/// </summary>
public sealed record PostExtraRequest(long FolioId, int ExtraItemId, int? Quantity = null, string? Label = null, long? AmountPaise = null, string? GstCategory = null);

/// <summary>SetReservationMealPlan K: the booking's plan (null / empty = none). Held / Confirmed: PERM-RES-002; InHouse: PERM-FOL-002.</summary>
public sealed record SetReservationMealPlanRequest(long ReservationId, string? MealPlanCode);

public sealed record ReservationMealPlanResult(long ReservationId, string? MealPlanCode, string? Previous);

/// <summary>
/// PostMealPlanNights K (PERM-CLS-002): for every in-house folio with a plan whose room night of <c>ServiceDate</c> (default: the business
/// date) is posted, posts the plan line once (add-on × guests; bundled = room-night slab on room + plan, separate = restaurant rate).
/// </summary>
public sealed record PostMealPlanNightsRequest(DateOnly? ServiceDate = null);

public sealed record MealPlanNightsResult(DateOnly ServiceDate, int Posted, int AlreadyPosted, IReadOnlyList<long> ChargeIds);

/// <summary>
/// RequestReasonedDiscount K V (PERM-FOL-003): a discount with a reason from the master. Refused when the caller's role may not use the
/// reason or the amount is above the reason's maximum % of the folio's charges. Above the reason's approval % (and not the Owner), or
/// above the caller's own discount limit, an approval request is created; otherwise the discount posts. ExpectedVersion = the folio's.
/// </summary>
public sealed record RequestReasonedDiscountRequest(long FolioId, long AmountPaise, string ReasonCode, string? Note, string ExpectedVersion);

/// <summary>GET /catalogue/links: plans of reservations and catalogue links of charges (for the folio screens and reports).</summary>
public sealed record CatalogueLinks(IReadOnlyList<ReservationPlanLink> Plans, IReadOnlyList<ChargeCatalogueLink> Charges);

public sealed record ReservationPlanLink(long ReservationId, string MealPlanCode);

public sealed record ChargeCatalogueLink(long ChargeId, int? ExtraItemId, string? MealPlanCode, string? DiscountReasonCode);
