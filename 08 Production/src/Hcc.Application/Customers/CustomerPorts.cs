using Hcc.Application.Commands;
using Hcc.Domain.Customers;

namespace Hcc.Application.Customers;

/// <summary>
/// PERM ids of Customers / CRM (role matrix v2, DEC-079, migration 3700 redefines the 2301 ids). View = profiles and history (Cashier masked);
/// Edit = contact reveal, tags, notes, preferences, consent (Owner, Manager, Reception); Money = lifetime spend and balances (Owner, Manager);
/// merging duplicates = PERM-CRM-003 for both steps (maker-checker: never the same person, DEC-066 pattern).
/// </summary>
public static class CustomerPermissions
{
    public const string View = "PERM-CRM-001";
    public const string Edit = "PERM-CRM-002";
    public const string Money = "PERM-CRM-004";
    public const string MergePropose = "PERM-CRM-003";
    public const string MergeDecide = "PERM-CRM-003";
}

/// <summary>Everything the pure customer index needs, read from the other modules' tables (read side only).</summary>
public sealed record CrmSource(IReadOnlyList<CrmGuest> Guests, IReadOnlyList<CrmStay> Stays, IReadOnlyDictionary<string, string> Aliases, DateOnly Today)
{
    public IReadOnlyList<Customer> Index() => CustomerRules.Index(Guests, Stays, Aliases, Today);
}

public sealed record CrmNote(long NoteId, string Body, int AuthorId, DateTime CreatedUtc);

public sealed record CrmPref(string Preferences, byte[] RowVer);

public sealed record CrmMerge(long MergeId, string SourceKey, string TargetKey, string SourceName, string TargetName, string Reason, string Status, int ProposedBy, DateTime ProposedUtc, int? DecidedBy, DateTime? DecidedUtc, string? DecisionNote, byte[] RowVer);

/// <summary>Per-customer data owned by this module.</summary>
public sealed record CrmOverlay(IReadOnlyList<string> Tags, IReadOnlyList<CrmNote> Notes, CrmPref? Pref, IReadOnlyList<string> MergedFrom);

/// <summary>One row of lgc.v_customer_stay (W1a LG, contract §9): an old-program stay, never a Duplicate copy.</summary>
public sealed record CrmLegacyStay(long StayId, string LegacyFy, long LegacyBookingId, int? LegacyBillNo, DateOnly Arrival, DateOnly? Departure, int Nights, string Room, int RoomCount, string Status, long? NetPaise);

public interface ICustomerReadStore
{
    Task<CrmSource> LoadAsync(int propertyId, CancellationToken ct = default);

    Task<CrmOverlay> OverlayAsync(int propertyId, string customerKey, CancellationToken ct = default);

    /// <summary>Tags of every customer key (for list rows).</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> TagsAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<CrmMerge>> MergesAsync(int propertyId, string? status, CancellationToken ct = default);

    /// <summary>AF-CRM (PR-01 / AF-DEC-3): the property's secret for the opaque ck_ customer key; created on first use.</summary>
    Task<byte[]> KeySecretAsync(int propertyId, CancellationToken ct = default);

    /// <summary>LG §9: the legacy stays of one customer key (lgc.v_customer_stay), newest arrival first.</summary>
    Task<IReadOnlyList<CrmLegacyStay>> LegacyStaysAsync(int propertyId, string customerKey, CancellationToken ct = default);

    /// <summary>LG §9: per customer key of <paramref name="keys"/> that has legacy stays: count, nights, Σ net paise, first and last arrival.</summary>
    Task<IReadOnlyDictionary<string, (int Stays, int Nights, long NetPaise, DateOnly First, DateOnly Last)>> LegacyRollupAsync(int propertyId, IEnumerable<string> keys, CancellationToken ct = default);
}

/// <summary>Write side, inside the command transaction (locked check-then-insert).</summary>
public interface ICustomerStore
{
    /// <summary>Inserts a Pending merge; returns null when the source or target is already merged away or the source already has a pending proposal.</summary>
    Task<long?> InsertMergeAsync(ICommandTransaction tx, int propertyId, string source, string target, string sourceName, string targetName, string reason, int principalId, DateTime utc, CancellationToken ct = default);

    Task<CrmMerge?> LockMergeAsync(ICommandTransaction tx, int propertyId, long mergeId, CancellationToken ct = default);

    Task RejectMergeAsync(ICommandTransaction tx, long mergeId, int principalId, DateTime utc, string? note, CancellationToken ct = default);

    /// <summary>
    /// Approves: writes the alias source to target (and re-points aliases that pointed at the source), moves tags/notes/preferences to the target
    /// (a differing source preference text is kept as a note), all inside the caller's transaction. False when either key is no longer a live customer key.
    /// </summary>
    Task<bool> ApproveMergeAsync(ICommandTransaction tx, CrmMerge merge, int principalId, DateTime utc, string? note, CancellationToken ct = default);

    /// <summary>False when the tag already exists.</summary>
    Task<bool> AddTagAsync(ICommandTransaction tx, int propertyId, string key, string tag, int principalId, DateTime utc, CancellationToken ct = default);

    Task<bool> RemoveTagAsync(ICommandTransaction tx, int propertyId, string key, string tag, CancellationToken ct = default);

    Task<int> TagCountAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default);

    Task<long> AddNoteAsync(ICommandTransaction tx, int propertyId, string key, string body, int principalId, DateTime utc, CancellationToken ct = default);

    Task<CrmPref?> LockPrefAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default);

    /// <summary>Empty text deletes the override.</summary>
    Task SetPrefAsync(ICommandTransaction tx, int propertyId, string key, string text, int principalId, DateTime utc, CancellationToken ct = default);
}
