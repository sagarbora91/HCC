using Hcc.Application.Commands;
using Hcc.Domain.Billing;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;

namespace Hcc.Application.Groups;

/// <summary>PERM ids of the Groups module (migration 2201; ids per the role matrix v2 proposal, "Groups billing").</summary>
public static class GroupPermissions
{
    public const string View = "PERM-GRP-001";
    public const string Manage = "PERM-GRP-002";
    public const string Approve = "PERM-GRP-003";
    public const string MoneyView = "PERM-GRP-004";
    public const string Payment = "PERM-GRP-005";
}

// ── Store rows ──

public sealed record GroupRow(long GroupId, int PropertyId, string Code, string Name, long? GroupBlockId, string Organiser, string BillingName,
    string BillingContact, string? BillingPhone, string? BillingEmail, string? BillingAddress, string? Gstin, int AllowanceBps, int AttritionRateBps,
    string Status, int CreatedBy, string Version);

public sealed record GroupDetails(string Name, string Organiser, string BillingName, string BillingContact, string? BillingPhone, string? BillingEmail,
    string? BillingAddress, string? Gstin, int AllowanceBps, int AttritionRateBps);

public sealed record NewGroup(int PropertyId, long? GroupBlockId, GroupDetails Details, int CreatedBy, DateTime CreatedUtc);

public sealed record BlockTypeRow(int RoomTypeId, string RoomType, int Allotted, int Picked, int Released, long? RatePaise);

public sealed record BlockRow(long GroupBlockId, int PropertyId, string Code, string Name, DateOnly Arrival, DateOnly Departure, DateOnly Cutoff, string Status,
    IReadOnlyList<BlockTypeRow> Types);

/// <summary>A member reservation: a block pickup (<c>block</c>) or attached by hand (<c>attached</c>).</summary>
public sealed record MemberRow(long ReservationId, string PublicNo, string Guest, string Status, DateOnly Arrival, DateOnly Departure, string? Room,
    long? FolioId, string Source, long? NightlyRatePaise);

/// <summary>A posted line on a member folio with its GST record (tax.charge_gst; the split falls back to the per-line rule when absent).</summary>
public sealed record MemberCharge(long ReservationId, long FolioId, PostedLine Line, string GstCategory, bool Itc, Paise Cgst, Paise Sgst);

public sealed record MemberReceipt(long ReservationId, PostedReceipt Receipt);

public sealed record RouteRow(long ChargeId, long ReservationId, int Seq, RouteTarget Net, RouteTarget Tax, string Source, string? Reason);

public sealed record NewRoute(long GroupId, long ChargeId, long ReservationId, RouteTarget Net, RouteTarget Tax, string Source, string? Reason,
    int RoutedBy, DateTime RoutedUtc);

public sealed record EntryRow(long EntryId, string Kind, DateOnly BusinessDate, DateOnly? ServiceDate, string Label, Paise Amount, string? GstCategory,
    int? TaxBps, bool? Itc, Paise? Tax, Paise? Cgst, Paise? Sgst, string? Tender, string? Reference, long? ReservationId, long? FolioReceiptId,
    long? AttritionId, long? RefundOf);

public sealed record NewEntry(long GroupId, string Kind, DateOnly BusinessDate, DateOnly? ServiceDate, string Label, Paise Amount, string? GstCategory,
    int? TaxBps, bool? Itc, Paise? Tax, Paise? Cgst, Paise? Sgst, string? GstRule, string? Tender, string? Reference, long? ReservationId, long? FolioId,
    long? FolioReceiptId, long? AttritionId, long? RefundOf, int PrincipalId, DateTime RecordedUtc);

public sealed record AttritionRow(long AttritionId, long GroupId, string Status, Paise Net, Paise Tax, string DetailJson, string GroupVersion, string Reason,
    int RequestedBy, int? DecidedBy);

public sealed record NewAttrition(long GroupId, Paise Net, Paise Tax, string DetailJson, string GroupVersion, string Reason, int RequestedBy, DateTime RequestedUtc);

/// <summary>Agreed price of one night (P2 <c>inv.reservation_night</c>: rate + GST rate snapshot).</summary>
public sealed record AgreedNight(long ReservationId, DateOnly Night, Paise Rate, int TaxBps);

/// <summary>Everything one group's billing needs, read in one batch (inside the command transaction or on the store's own connection).</summary>
public sealed record GroupSnapshot(GroupRow Group, BlockRow? Block, DateOnly BusinessDate, IReadOnlyList<RoutingRule> Rules, IReadOnlyList<MemberRow> Members,
    IReadOnlyList<MemberCharge> Charges, IReadOnlyList<MemberReceipt> Receipts, IReadOnlyList<RouteRow> Routes, IReadOnlyList<EntryRow> Entries,
    IReadOnlyList<AttritionRow> Attritions, IReadOnlyList<AgreedNight> Agreed);

public sealed record GroupListRow(long GroupId, string Code, string Name, string Status, long? GroupBlockId, string? BlockCode, DateOnly? Arrival, DateOnly? Departure,
    int Members, string Version);

/// <summary>Reservation facts the membership commands check.</summary>
public sealed record ReservationFacts(long ReservationId, string Status, long? GroupBlockId, long? AttachedGroupId, long? BlockGroupId);

/// <summary>Persistence of the Groups module (SQL adapter in Hcc.Infrastructure.SqlServer/Groups). Lock order: group → guest folio.</summary>
public interface IGroupStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>The group of an attrition request (plain read, so the group can be locked first).</summary>
    Task<long?> AttritionGroupAsync(ICommandTransaction tx, long attritionId, CancellationToken ct = default);

    Task<GroupRow> InsertGroupAsync(ICommandTransaction tx, NewGroup group, CancellationToken ct = default);

    /// <summary>The group row under UPDLOCK + HOLDLOCK (serializes every command of one group).</summary>
    Task<GroupRow?> LockGroupAsync(ICommandTransaction tx, long groupId, CancellationToken ct = default);

    /// <summary>Versioned update; STALE_VERSION when <paramref name="expectedVersion"/> no longer matches.</summary>
    Task<string> UpdateGroupAsync(ICommandTransaction tx, long groupId, string expectedVersion, GroupDetails details, CancellationToken ct = default);

    /// <summary>Moves the group's version (every routing, ledger or membership change).</summary>
    Task<string> TouchGroupAsync(ICommandTransaction tx, long groupId, CancellationToken ct = default);

    Task<string> SettleGroupAsync(ICommandTransaction tx, long groupId, string expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);

    Task<BlockRow?> BlockAsync(ICommandTransaction? tx, int propertyId, long groupBlockId, CancellationToken ct = default);

    Task<long?> GroupOfBlockAsync(ICommandTransaction tx, long groupBlockId, CancellationToken ct = default);

    /// <summary>Sets (or with <paramref name="target"/> null removes) one rule: the group default when <paramref name="reservationId"/> is null.</summary>
    Task SetRuleAsync(ICommandTransaction tx, long groupId, long? reservationId, RouteCategory category, RouteTarget? target, int principalId, DateTime utc,
        CancellationToken ct = default);

    Task<ReservationFacts?> ReservationAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default);

    Task AttachMemberAsync(ICommandTransaction tx, long groupId, long reservationId, int principalId, DateTime utc, CancellationToken ct = default);

    Task DetachMemberAsync(ICommandTransaction tx, long groupId, long reservationId, CancellationToken ct = default);

    /// <summary>Appends a route row; the seq is assigned in SQL (max for the charge + 1) and returned.</summary>
    Task<int> InsertRouteAsync(ICommandTransaction tx, NewRoute route, CancellationToken ct = default);

    Task<long> InsertEntryAsync(ICommandTransaction tx, NewEntry entry, CancellationToken ct = default);

    Task<long> InsertAttritionAsync(ICommandTransaction tx, NewAttrition attrition, CancellationToken ct = default);

    Task<AttritionRow?> LockAttritionAsync(ICommandTransaction tx, long attritionId, CancellationToken ct = default);

    Task DecideAttritionAsync(ICommandTransaction tx, long attritionId, string status, int principalId, DateTime utc, string? reason, CancellationToken ct = default);

    Task<GroupSnapshot?> LoadAsync(ICommandTransaction? tx, int propertyId, long groupId, CancellationToken ct = default);

    Task<IReadOnlyList<GroupListRow>> ListAsync(int propertyId, CancellationToken ct = default);

    /// <summary>The group whose member owns <paramref name="folioId"/>, or null.</summary>
    Task<long?> GroupOfFolioAsync(ICommandTransaction? tx, int propertyId, long folioId, CancellationToken ct = default);
}

/// <summary>A guest folio as the transfer sees it (locked).</summary>
public sealed record GuestFolioState(long FolioId, long ReservationId, string ReservationStatus, bool IsOpen, string Version);

/// <summary>
/// Groups → Billing port (a new port in this module; Billing's files are untouched): moves a member's master share off the guest
/// folio as a <c>company-credit</c> receipt (tender "Company credit") — the same receivable the Accounting module books as
/// Dr company receivables · Cr guest ledger — inside the group command's transaction. Implemented over <c>IBillingStore</c>.
/// </summary>
public interface IGroupFolioPort
{
    Task<GuestFolioState> LockFolioAsync(ICommandTransaction tx, int propertyId, long folioId, CancellationToken ct = default);

    Task<long> TransferToMasterAsync(ICommandTransaction tx, long folioId, Paise amount, DateOnly businessDate, string reference, DateTime utc, CancellationToken ct = default);
}

// ── Read port for W3-6 (invoices & credit notes) ──

public sealed record GroupInvoiceParty(string Code, string Name, string BillingName, string BillingContact, string? BillingPhone, string? BillingEmail,
    string? BillingAddress, string? Gstin);

/// <summary>
/// One line of the group invoice: a routed member line (<c>folio-charge</c>, source id = bill.charge id) or a master attrition line
/// (<c>attrition</c>, source id = grp.master_entry id). Net / tax amounts are the whole line; <see cref="NetToMaster"/> /
/// <see cref="TaxToMaster"/> say which shares the master pays (a split line, e.g. net to master and tax to guest, is flagged).
/// CGST + SGST are the stored per-line split.
/// </summary>
public sealed record GroupInvoiceLine(string Source, long SourceId, long? ReservationId, string? PublicNo, string? Guest, string? Room, DateOnly ServiceDate,
    string Kind, string Label, string GstCategory, bool Itc, long NetPaise, int TaxBps, long TaxPaise, long CgstPaise, long SgstPaise, bool NetToMaster,
    bool TaxToMaster, long MasterPaise, long? ReversesChargeId);

public sealed record GroupInvoiceData(long GroupId, string Code, string Status, GroupInvoiceParty Party, string? BlockCode, DateOnly? Arrival, DateOnly? Departure,
    IReadOnlyList<GroupInvoiceLine> Lines, long MasterNetPaise, long MasterTaxPaise, long MasterCgstPaise, long MasterSgstPaise, long MasterChargesPaise,
    long ReceivedPaise, long BalancePaise, int UnroutedLines, string Version);

/// <summary>A member line (partly) billed to a group master — the guest-folio invoice leaves these shares out.</summary>
public sealed record MasterRoutedShare(long ChargeId, long GroupId, string GroupCode, bool NetToMaster, bool TaxToMaster, long MasterPaise);

/// <summary>
/// What W3-6 needs to issue a group invoice from the master folio. No permission check here: the invoice command checks its own
/// PERM (e.g. PERM-FOL-004). Only persisted routes count (run ApplyGroupRouting first; <see cref="GroupInvoiceData.UnroutedLines"/> &gt; 0 = not ready).
/// </summary>
public interface IGroupInvoiceSource
{
    Task<GroupInvoiceData?> GetAsync(ICommandTransaction? tx, int propertyId, long groupId, CancellationToken ct = default);

    Task<IReadOnlyList<MasterRoutedShare>> MasterSharesForFolioAsync(ICommandTransaction? tx, int propertyId, long folioId, CancellationToken ct = default);
}
