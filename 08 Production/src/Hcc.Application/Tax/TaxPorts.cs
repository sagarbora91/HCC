using Hcc.Application.Commands;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Tax;

/// <summary>PERM ids of the GST master (DEC-058: money/tax settings are Owner-only; Tech admin read-only).</summary>
public static class TaxPermissions
{
    /// <summary>admin.configuration — seeded OWN = A (change), TAD = V (read). Reads use the <c>:VIEW</c> variant.</summary>
    public const string Configuration = "PERM-ADM-003";

    public const string ConfigurationView = "PERM-ADM-003:VIEW";
}

/// <summary>
/// DEC-069 GST rate resolver (replaces the DEC-039 <c>ITaxRateSource</c>). Room nights are tested per unit per night on the
/// actual price charged; other charges by category. Reads the dated master on every call (no cache: an Owner change applies
/// to the next posting). Posted lines keep their stored snapshot — only new postings consult the resolver.
/// </summary>
public interface IGstRateResolver
{
    /// <remarks>
    /// Pass the command transaction when there is one: the master and the specified-premises inputs are then read inside it (the
    /// database runs READ COMMITTED with locks, so a second connection could wait on rows the command itself has written).
    /// </remarks>
    Task<GstRate> RoomNightAsync(ICommandTransaction? tx, int propertyId, DateOnly night, Paise chargedPerUnit, CancellationToken ct = default);

    /// <summary>Each night of [from, to) at one nightly price (quotes, agreed-night snapshots): one master read, one rate per night.</summary>
    Task<IReadOnlyList<(DateOnly Night, GstRate Rate)>> RoomNightsAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, Paise nightly, CancellationToken ct = default);

    /// <summary>Food, laundry, banquet or other service supplied on <paramref name="date"/> (room → VALIDATION; use <see cref="RoomNightAsync"/>).</summary>
    Task<GstRate> ChargeAsync(ICommandTransaction? tx, int propertyId, DateOnly date, GstCategory category, CancellationToken ct = default);
}

/// <summary>A stored master version as listed in the history (seed rows have no principal).</summary>
public sealed record GstVersionRow(long VersionId, int? PropertyId, string Key, DateOnly EffectiveFrom, long Value, string Source, string Status,
    string? Reason, int? ChangedBy, DateTime ChangedUtc, int? WithdrawnBy, DateTime? WithdrawnUtc);

/// <summary>Persistence of the GST master (tax schema, migration 0600).</summary>
public interface IGstMasterStore
{
    /// <summary>Active versions for the property (its own rows + the statutory seed rows); inside <paramref name="tx"/> when given, else on the store's own connection.</summary>
    Task<IReadOnlyList<GstSettingVersion>> ActiveVersionsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default);

    /// <summary>Highest nightly tariff of any accommodation unit in force on any day of [from, to] (rack rate, dated tariffs and posted room nights).</summary>
    Task<long> MaxUnitTariffAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<GstVersionRow>> HistoryAsync(int propertyId, CancellationToken ct = default);

    // Command side (inside the command transaction).
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Active versions under UPDLOCK + HOLDLOCK on the key (serializes concurrent changes of one setting).</summary>
    Task<IReadOnlyList<GstSettingVersion>> LockActiveVersionsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<long> InsertVersionAsync(ICommandTransaction tx, int propertyId, string key, DateOnly effectiveFrom, long value, string reason, int principalId, DateTime utc, CancellationToken ct = default);

    Task<GstVersionRow?> LockVersionAsync(ICommandTransaction tx, long versionId, CancellationToken ct = default);

    Task WithdrawVersionAsync(ICommandTransaction tx, long versionId, int principalId, DateTime utc, CancellationToken ct = default);
}

/// <summary>Per-charge GST snapshot (category, ITC, CGST/SGST split) written beside <c>bill.charge</c> in the posting transaction.</summary>
public sealed record GstChargeSnapshot(long ChargeId, GstCategory Category, int Bps, bool Itc, Paise Tax, Paise Cgst, Paise Sgst, string Rule);

public interface IGstChargeSnapshots
{
    Task RecordAsync(ICommandTransaction tx, GstChargeSnapshot snapshot, CancellationToken ct = default);

    /// <summary>The snapshot of a posted charge inside the transaction (reversals copy the original's category / ITC).</summary>
    Task<GstChargeSnapshot?> FindAsync(ICommandTransaction tx, long chargeId, CancellationToken ct = default);
}

/// <summary>
/// Test stand-in pinned to one rate for every supply (the DEC-039 synthetic 10 % that the P2–P4 module tests were written
/// against). Never registered by the composition — production uses <see cref="GstRateResolver"/>.
/// </summary>
public sealed class FixedGstRateResolver(int taxBps = 1000) : IGstRateResolver
{
    public Task<GstRate> RoomNightAsync(ICommandTransaction? tx, int propertyId, DateOnly night, Paise chargedPerUnit, CancellationToken ct = default)
        => Task.FromResult(new GstRate(taxBps, false, GstCategory.Room, "Fixed test rate"));

    public Task<IReadOnlyList<(DateOnly Night, GstRate Rate)>> RoomNightsAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, Paise nightly, CancellationToken ct = default)
    {
        var list = new List<(DateOnly, GstRate)>();
        for (var d = from; d < to; d = d.AddDays(1)) list.Add((d, new GstRate(taxBps, false, GstCategory.Room, "Fixed test rate")));
        return Task.FromResult<IReadOnlyList<(DateOnly Night, GstRate Rate)>>(list);
    }

    public Task<GstRate> ChargeAsync(ICommandTransaction? tx, int propertyId, DateOnly date, GstCategory category, CancellationToken ct = default)
        => Task.FromResult(new GstRate(taxBps, false, category, "Fixed test rate"));
}

/// <summary>The resolver over the dated master (<see cref="IGstMasterStore"/>) and the pure rules (<see cref="GstMaster"/>).</summary>
public sealed class GstRateResolver(IGstMasterStore store) : IGstRateResolver
{
    public async Task<GstMaster> MasterAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default)
        => new(await store.ActiveVersionsAsync(tx, propertyId, ct).ConfigureAwait(false));

    public async Task<GstRate> RoomNightAsync(ICommandTransaction? tx, int propertyId, DateOnly night, Paise chargedPerUnit, CancellationToken ct = default)
        => (await MasterAsync(tx, propertyId, ct).ConfigureAwait(false)).RoomNight(night, chargedPerUnit);

    public async Task<IReadOnlyList<(DateOnly Night, GstRate Rate)>> RoomNightsAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, Paise nightly, CancellationToken ct = default)
    {
        var master = await MasterAsync(tx, propertyId, ct).ConfigureAwait(false);
        var list = new List<(DateOnly, GstRate)>();
        for (var d = from; d < to; d = d.AddDays(1)) list.Add((d, master.RoomNight(d, nightly)));
        return list;
    }

    public async Task<GstRate> ChargeAsync(ICommandTransaction? tx, int propertyId, DateOnly date, GstCategory category, CancellationToken ct = default)
    {
        var master = await MasterAsync(tx, propertyId, ct).ConfigureAwait(false);
        return category switch
        {
            GstCategory.Room => throw Hcc.Domain.Errors.HccException.Validation("Room nights are taxed per night on the charged price."),
            GstCategory.Food => master.Food(date, await SpecifiedAsync(tx, master, propertyId, date, ct).ConfigureAwait(false)),
            _ => master.Service(date, category),
        };
    }

    public async Task<GstSpecifiedPremises> SpecifiedAsync(ICommandTransaction? tx, GstMaster master, int propertyId, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        var prev = FinancialYear.Of(date).Previous;
        var max = await store.MaxUnitTariffAsync(tx, propertyId, prev.From, prev.To, ct).ConfigureAwait(false);
        return master.SpecifiedPremises(date, max);
    }
}
