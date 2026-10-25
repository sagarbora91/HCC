namespace Hcc.Contracts.Groups;

// Wire contracts of the Groups billing module (W3-4). Money is integer paise; dates are yyyy-MM-dd strings on requests and
// DateOnly on views; expectedVersion = base64 rowversion of the group (STALE_VERSION on mismatch).

/// <summary>A routing rule: category <c>room</c> | <c>extras</c> | <c>tax</c> → target <c>master</c> | <c>guest</c> (| <c>follow</c> for tax).</summary>
public sealed record RoutingRuleInput(string Category, string Target);

/// <summary>POST /commands/CreateGroup. Without rules: room → master, tax → follow, extras → guest (a guest-paid group sets all to guest).</summary>
public sealed record CreateGroupRequest(string Name, long? GroupBlockId, string Organiser, string BillingName, string BillingContact,
    string? BillingPhone = null, string? BillingEmail = null, string? BillingAddress = null, string? Gstin = null,
    int AttritionAllowanceBps = 0, int AttritionRateBps = 0, IReadOnlyList<RoutingRuleInput>? Rules = null);

/// <summary>POST /commands/UpdateGroup. Changing the attrition terms needs PERM-GRP-003.</summary>
public sealed record UpdateGroupRequest(long GroupId, string ExpectedVersion, string Name, string Organiser, string BillingName, string BillingContact,
    string? BillingPhone = null, string? BillingEmail = null, string? BillingAddress = null, string? Gstin = null,
    int AttritionAllowanceBps = 0, int AttritionRateBps = 0);

/// <summary>POST /commands/SetGroupRouting. <see cref="ReservationId"/> null = the group default; Target null removes a member override.</summary>
public sealed record SetGroupRoutingRequest(long GroupId, string ExpectedVersion, long? ReservationId, string Category, string? Target);

/// <summary>POST /commands/AttachGroupMember | DetachGroupMember (a reservation outside the group's block).</summary>
public sealed record GroupMemberRequest(long GroupId, string ExpectedVersion, long ReservationId);

/// <summary>POST /commands/ApplyGroupRouting — pickup billing: snapshots the route of every posted, not yet routed member line.</summary>
public sealed record ApplyGroupRoutingRequest(long GroupId);

/// <summary>POST /commands/RerouteGroupCharge — a posted line moves between master and guest (appended route, never an edit).</summary>
public sealed record RerouteGroupChargeRequest(long GroupId, string ExpectedVersion, long ChargeId, string NetTarget, string TaxTarget, string Reason);

/// <summary>POST /commands/TransferGroupMember — moves the member's untransferred master share off the guest folio onto the master folio.</summary>
public sealed record TransferGroupMemberRequest(long GroupId, long ReservationId);

/// <summary>POST /commands/RecordGroupReceipt — kind payment | deposit | refund; tender UPI | Card | Bank transfer (the master folio takes no cash).</summary>
public sealed record RecordGroupReceiptRequest(long GroupId, string Kind, string Tender, long AmountPaise, string Reference, long? RefundOf = null);

/// <summary>POST /commands/RequestGroupAttrition — unpicked block rooms after the cut-off at the group's attrition terms.</summary>
public sealed record RequestGroupAttritionRequest(long GroupId, string ExpectedVersion, string Reason);

/// <summary>POST /commands/DecideGroupAttrition — a different approver (PERM-GRP-003, Manager within limit) posts or rejects it.</summary>
public sealed record DecideGroupAttritionRequest(long AttritionId, bool Approve, string? Reason = null);

/// <summary>POST /commands/SettleGroup — closes the master folio (everything routed, transferred and paid; no pickup open).</summary>
public sealed record SettleGroupRequest(long GroupId, string ExpectedVersion);

public sealed record GroupResult(long GroupId, string Code, string Status, string Version);

public sealed record GroupRoutingResult(long GroupId, int Routed, int AlreadyRouted, string Version);

public sealed record GroupRerouteResult(long GroupId, long ChargeId, int Seq, string NetTarget, string TaxTarget, string Version);

public sealed record GroupTransferResult(long GroupId, long ReservationId, long EntryId, long FolioReceiptId, long AmountPaise, string Version);

public sealed record GroupReceiptResult(long GroupId, long EntryId, string Kind, string Tender, long AmountPaise, DateOnly BusinessDate, string Version);

public sealed record GroupAttritionResult(long AttritionId, long GroupId, string Status, long NetPaise, long TaxPaise, IReadOnlyList<long> EntryIds, string Version);

// ── Read models ──

public sealed record GroupSummaryView(long GroupId, string Code, string Name, string Status, long? GroupBlockId, string? BlockCode, DateOnly? Arrival,
    DateOnly? Departure, int Members, string Version);

public sealed record GroupRuleView(long? ReservationId, string Category, string Target);

public sealed record GroupPickupView(string RoomType, int Allotted, int Picked, int Released, int Remaining, long RatePaise, int PickedPercent);

/// <summary>One posted member line with its GST snapshot (CGST + SGST per line) and its route.</summary>
public sealed record GroupLineView(long ChargeId, string Kind, DateOnly ServiceDate, string Label, long NetPaise, int TaxBps, long TaxPaise, long CgstPaise,
    long SgstPaise, string GstCategory, long? ReversesChargeId, string NetTarget, string TaxTarget, bool Routed, int RouteSeq, long MasterPaise, long GuestPaise);

public sealed record GroupNightView(DateOnly Night, IReadOnlyList<GroupLineView> Lines, long MasterPaise, long GuestPaise);

public sealed record GroupMemberView(long ReservationId, string PublicNo, string Guest, string? Room, string Status, DateOnly Arrival, DateOnly Departure,
    long? FolioId, string Source, IReadOnlyList<GroupRuleView> Overrides, IReadOnlyList<GroupNightView> Nights, long NetPaise, long TaxPaise, long TotalPaise,
    long MasterSharePaise, long GuestSharePaise, long TransferredPaise, long PendingTransferPaise, long FolioPaidPaise, long FolioBalancePaise, long EstimatePaise);

public sealed record GroupMasterLineView(long EntryId, string Kind, DateOnly BusinessDate, DateOnly? ServiceDate, string Label, long AmountPaise, int? TaxBps,
    long? TaxPaise, long? CgstPaise, long? SgstPaise, string? Tender, string? Reference, long? ReservationId);

public sealed record GroupAttritionView(long AttritionId, string Status, long NetPaise, long TaxPaise, string Reason, int RequestedBy, int? DecidedBy, string DetailJson);

public sealed record GroupTotalsView(long MasterNetPaise, long MasterTaxPaise, long MasterCgstPaise, long MasterSgstPaise, long MasterChargesPaise,
    long ReceivedPaise, long DepositsPaise, long RefundedPaise, long MasterBalancePaise, long TransferredPaise, long PendingTransferPaise,
    long GuestSharePaise, long MembersPaidPaise, long MembersEstimatePaise, int UnroutedLines);

public sealed record GroupStatementView(long GroupId, string Code, string Name, string Status, string Organiser, string BillingName, string BillingContact,
    string? Gstin, long? GroupBlockId, string? BlockCode, string? BlockStatus, DateOnly? Cutoff, DateOnly BusinessDate, IReadOnlyList<GroupRuleView> Rules,
    IReadOnlyList<GroupPickupView> Pickup, IReadOnlyList<GroupMemberView> Members, IReadOnlyList<GroupMasterLineView> Master,
    IReadOnlyList<GroupAttritionView> Attrition, GroupTotalsView Totals, string Version);
