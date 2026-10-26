using Hcc.Application.Commands;
using Hcc.Contracts.Catalogue;

namespace Hcc.Application.Catalogue;

/// <summary>
/// PERM ids used by the Catalogue module (no new ids, migration 2820): the three lists are money masters (PERM-MST-002, Owner — DEC-058);
/// reading them needs folio view (PERM-FOL-001: Reception, Cashier, Manager, Owner, Accountant — housekeeping never sees prices, DEC-061);
/// catalogue charges = PERM-FOL-002, reasoned discounts = PERM-FOL-003, plan of a Held / Confirmed booking = PERM-RES-002 (in house:
/// PERM-FOL-002), plan lines of a night = PERM-CLS-002 (night posting).
/// </summary>
public static class CataloguePermissions
{
    public const string ManageMoney = "PERM-MST-002";
    public const string View = "PERM-FOL-001";
    public const string ChargePost = "PERM-FOL-002";
    public const string Discount = "PERM-FOL-003";
    public const string Reservation = "PERM-RES-002";
    public const string NightPost = "PERM-CLS-002";
}

/// <summary>Table layout of one catalogue master (generic store; columns beyond code / name / sort order are master fields).</summary>
public sealed record CatalogueMaster(string Id, string Label, string Singular, string Table, string IdColumn, string IdField, string? PriceTable,
    IReadOnlyList<string> Columns, string AuditAction)
{
    public static readonly CatalogueMaster MealPlans = new(CatalogueIds.MealPlans, "Meal plans", "meal plan", "bill.meal_plan", "meal_plan_id", "mealPlanId",
        "bill.meal_plan_price", ["meals", "gst_treatment", "room_types"], "CAT_MEAL_PLAN");

    public static readonly CatalogueMaster Extras = new(CatalogueIds.Extras, "Extras and services", "extra", "bill.extra_item", "extra_item_id", "extraItemId",
        "bill.extra_item_price", ["category", "gst_category", "unit", "department"], "CAT_EXTRA");

    public static readonly CatalogueMaster DiscountReasons = new(CatalogueIds.DiscountReasons, "Discount reasons", "discount reason", "bill.discount_reason",
        "discount_reason_id", "discountReasonId", null, ["max_bps", "approval_above_bps", "roles"], "CAT_DISCOUNT_REASON");

    public static readonly IReadOnlyList<CatalogueMaster> All = [MealPlans, Extras, DiscountReasons];

    /// <summary>Integer columns (the rest are text).</summary>
    public static bool IsInt(string column) => column is "max_bps" or "approval_above_bps";
}

/// <summary>A catalogue row as stored. Fields = master columns as text (null = empty); Prices = dated amounts (meal plans, extras).</summary>
public sealed record CatRow(int Id, string Code, string Name, int SortOrder, bool Active, DateOnly? RetiredOn, DateTime UpdatedUtc, byte[] RowVer,
    IReadOnlyDictionary<string, string?> Fields, IReadOnlyList<(DateOnly From, long Amount)> Prices)
{
    public string? F(string column) => Fields.TryGetValue(column, out var v) ? v : null;
}

/// <summary>What a save writes (code / name / sort order are validated by MastersRules, fields by CatalogueRules).</summary>
public sealed record CatRowInput(string Code, string Name, IReadOnlyDictionary<string, string?> Fields, IReadOnlyList<(DateOnly From, long Amount)> Prices);

/// <summary>A reservation as the meal-plan commands need it.</summary>
public sealed record PlanReservation(long ReservationId, string Status, string? RoomTypeCode, int? MealPlanId, string? MealPlanCode);

/// <summary>An in-house folio whose room night of the date is posted and that has a plan without its plan line yet.</summary>
public sealed record PlanNightCandidate(long FolioId, long ReservationId, int MealPlanId, string PlanCode, string GstTreatment, long RoomAmountPaise,
    int Guests, long? AddOnPaise);

/// <summary>Write side (inside the command transaction; master rows locked UPDLOCK + HOLDLOCK).</summary>
public interface ICatalogueStore
{
    Task<IReadOnlyList<CatRow>> LockRowsAsync(ICommandTransaction tx, CatalogueMaster master, int propertyId, CancellationToken ct = default);

    Task<int> InsertAsync(ICommandTransaction tx, CatalogueMaster master, int propertyId, CatRowInput row, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Versioned update (STALE_VERSION); replaces the dated amounts.</summary>
    Task UpdateAsync(ICommandTransaction tx, CatalogueMaster master, int id, byte[] expectedVersion, CatRowInput row, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetActiveAsync(ICommandTransaction tx, CatalogueMaster master, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetSortAsync(ICommandTransaction tx, CatalogueMaster master, int id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, CatalogueMaster master, int id, byte[] expectedVersion, CancellationToken ct = default);

    /// <summary>Records that use the row (charges, bookings, approval requests) with a short label each.</summary>
    Task<(int Count, IReadOnlyList<string> Where)> UsageAsync(ICommandTransaction tx, CatalogueMaster master, int id, CancellationToken ct = default);

    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Role codes of the principal valid at <paramref name="utc"/>.</summary>
    Task<IReadOnlyList<string>> RoleCodesAsync(ICommandTransaction tx, int principalId, DateTime utc, CancellationToken ct = default);

    Task<PlanReservation?> LockReservationAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default);

    Task SetReservationPlanAsync(ICommandTransaction tx, long reservationId, int? mealPlanId, int principalId, DateTime utc, CancellationToken ct = default);

    Task LinkChargeAsync(ICommandTransaction tx, long chargeId, int? extraItemId, int? mealPlanId, int? discountReasonId, int? quantity, CancellationToken ct = default);

    Task LinkApprovalAsync(ICommandTransaction tx, long approvalId, int discountReasonId, CancellationToken ct = default);

    Task<IReadOnlyList<PlanNightCandidate>> PlanNightCandidatesAsync(ICommandTransaction tx, int propertyId, DateOnly serviceDate, CancellationToken ct = default);

    /// <summary>Records the plan line of a folio night; false when one exists already (repeat-safe).</summary>
    Task<bool> InsertPlanNightAsync(ICommandTransaction tx, long folioId, DateOnly serviceDate, long chargeId, CancellationToken ct = default);
}

/// <summary>Read side (own connection).</summary>
public interface ICatalogueReadStore
{
    /// <summary>Rows in sort order with their usage count.</summary>
    Task<IReadOnlyList<(CatRow Row, int Usage)>> RowsAsync(CatalogueMaster master, int propertyId, CancellationToken ct = default);

    /// <summary>Plans of reservations active in the window and catalogue links of charges posted in it.</summary>
    Task<CatalogueLinks> LinksAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<DateOnly?> BusinessDateAsync(int propertyId, CancellationToken ct = default);
}
