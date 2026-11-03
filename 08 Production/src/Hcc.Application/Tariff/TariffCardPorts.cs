using Hcc.Application.Commands;
using Hcc.Domain.Tariff;

namespace Hcc.Application.Tariff;

/// <summary>Tariff card permissions (migration 4383): 001 view / print (Owner, Manager, Accountant, Reception, Cashier), 002 draft + propose
/// (Owner, Manager), 003 publish / reject / weekend days / import (Owner only, DEC-058). Housekeeping / maintenance / restaurant: none (DEC-061).</summary>
public static class TariffCardPermissions
{
    public const string View = "PERM-TRF-001";
    public const string Propose = "PERM-TRF-002";
    public const string Publish = "PERM-TRF-003";
}

/// <summary>A stored card version (mst.tariff_card_version + lines + publish log).</summary>
public sealed record TariffCardRec(long VersionId, string Label, DateOnly EffectiveFrom, string Status, IReadOnlyList<int> WeekendDays, IReadOnlyList<TrcLine> Lines,
    int CreatedBy, DateTime CreatedUtc, int? ProposedBy, int? PublishedBy, DateTime? PublishedUtc, long? SupersededBy, string? Notes, IReadOnlyList<TrcWrite> PublishLog);

/// <summary>Data-access port of the tariff card (migrations 4380–4383). Every write runs inside the command transaction.</summary>
public interface ITariffCardStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<int>> WeekendDefaultAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default);

    Task SetWeekendDefaultAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<int> days, CancellationToken ct = default);

    Task<TariffCardRec?> LockAsync(ICommandTransaction tx, int propertyId, long versionId, CancellationToken ct = default);

    Task<long> InsertAsync(ICommandTransaction tx, int propertyId, TariffCardRec rec, CancellationToken ct = default);

    /// <summary>Replaces a draft / proposed version's header and lines (the trigger refuses this on published rows).</summary>
    Task UpdateDraftAsync(ICommandTransaction tx, int propertyId, TariffCardRec rec, CancellationToken ct = default);

    Task SetStatusAsync(ICommandTransaction tx, long versionId, string status, int? by, string? notes, CancellationToken ct = default);

    Task DeleteDraftAsync(ICommandTransaction tx, long versionId, CancellationToken ct = default);

    /// <summary>Unknown room type / meal plan / extra codes of the lines (refused before publish).</summary>
    Task<IReadOnlyList<string>> UnknownCodesAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<TrcLine> lines, CancellationToken ct = default);

    /// <summary>Tariff schedule rows of the property on one date (inv.room_type_tariff, day_set, card_version_id).</summary>
    Task<IReadOnlyList<TrcTariffRow>> TariffsOnAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default);

    /// <summary>"master|ref" of every row earlier card versions wrote.</summary>
    Task<IReadOnlySet<string>> CardRefsAsync(ICommandTransaction tx, int propertyId, long exceptVersionId, CancellationToken ct = default);

    /// <summary>Dated master money for the plan keys ("occupancyCharges|TYPE|Adult", "mealPlans|CODE|AddOn", "extras|CODE|Price"): amount in force on
    /// the date and the amount stored on exactly that date; a key whose master row does not exist is absent.</summary>
    Task<IReadOnlyDictionary<string, (long? Now, long? OnDate)>> MoneyOnAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default);

    /// <summary>Applies one publish write to its master (tariff row with day_set + card_version_id, or the dated money row) and logs it.</summary>
    Task ApplyAsync(ICommandTransaction tx, int propertyId, long versionId, DateOnly date, TrcWrite write, int by, CancellationToken ct = default);

    /// <summary>Published versions effective on or before <paramref name="date"/> other than <paramref name="versionId"/> become superseded by it.</summary>
    Task<int> SupersedeAsync(ICommandTransaction tx, int propertyId, long versionId, DateOnly date, CancellationToken ct = default);

    Task MarkPublishedAsync(ICommandTransaction tx, long versionId, int by, DateTime utc, CancellationToken ct = default);

    // ── reads (own connection) ──
    Task<IReadOnlyList<TariffCardRec>> ListAsync(int propertyId, CancellationToken ct = default);
}
