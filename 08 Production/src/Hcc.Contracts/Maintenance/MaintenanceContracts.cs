namespace Hcc.Contracts.Maintenance;

// Wire contracts for the Maintenance module (W2-7). DEC-063 G12: part unit prices and AMC contract values are money; they are
// carried only in nullable fields that the server fills for readers who hold finance rights (Owner, Manager, Accountant) —
// Maintenance, Housekeeping and Supervisor receive null. Times are UTC ISO-8601, dates yyyy-MM-dd, ids numeric.

// ── Commands (POST /api/v1/commands/{Operation}, Idempotency-Key header) ──
/// <summary>CreateWorkOrder. Maintenance users are always self-assigned; Housekeepers report repairs from their task card instead.</summary>
public sealed record CreateWorkOrderRequest(string Title, string Priority, int? RoomId = null, int? AssetId = null, string? Area = null, string? Notes = null, int? AssigneeId = null);

/// <summary>ConvertRepairToWorkOrder: an open Housekeeping repair task (ops.hk_task kind repair) becomes a work order (one per task).</summary>
public sealed record ConvertRepairRequest(long TaskId, string? Priority = null, int? AssetId = null);

public sealed record AssignWorkOrderRequest(long WorkOrderId, string ExpectedVersion, int? TechnicianId = null, int? VendorId = null);

public sealed record StartWorkOrderRequest(long WorkOrderId, string ExpectedVersion);

public sealed record HoldWorkOrderRequest(long WorkOrderId, string ExpectedVersion, string Reason, int? SpareId = null);

public sealed record AddWorkOrderPartRequest(long WorkOrderId, string ExpectedVersion, int SpareId, int Qty);

public sealed record CompleteWorkOrderRequest(long WorkOrderId, string ExpectedVersion, string Note);

/// <summary>VerifyWorkOrder: approve (optionally unblock the room) or return with a reason. Not by the completer/assignee.</summary>
public sealed record VerifyWorkOrderRequest(long WorkOrderId, string ExpectedVersion, bool Approve, string? Reason = null, bool Unblock = false);

public sealed record CancelWorkOrderRequest(long WorkOrderId, string ExpectedVersion, string Reason);

/// <summary>BlockWorkOrderRoom: out-of-service block through the Housekeeping/Inventory room-block port, from the business date to <c>To</c> (null = open-ended).</summary>
public sealed record BlockWorkOrderRoomRequest(long WorkOrderId, string ExpectedVersion, string? To = null);

public sealed record GeneratePmWorkOrdersRequest(string? Note = null);

public sealed record SavePmPlanRequest(int? PlanId, string? ExpectedVersion, int AssetId, string Title, int EveryDays, string NextDue, bool Active = true);

public sealed record SaveAssetRequest(int? AssetId, string? ExpectedVersion, string Name, string Kind, int? RoomId, string? Area, string? Make, string? Model, string? InstalledOn, int? VendorId, string Status = "Working");

public sealed record SaveVendorRequest(int? VendorId, string? ExpectedVersion, string Name, string? Trade, string Phone);

/// <summary>SaveAmc (Owner and Manager only): contract value in paise.</summary>
public sealed record SaveAmcRequest(int? AmcId, string? ExpectedVersion, int VendorId, string Scope, string From, string To, int Visits, long ValuePaise);

public sealed record ReceiveSpareRequest(int SpareId, string ExpectedVersion, int Qty);

// ── Command results ──
public sealed record WorkOrderResult(long WorkOrderId, string No, string Status, string Version, string Detail);

public sealed record BlockWorkOrderRoomResult(long WorkOrderId, string No, int RoomId, string RoomNo, long BlockId, bool LinkedExisting, string Version);

public sealed record GeneratePmResult(int Generated, IReadOnlyList<long> WorkOrderIds, string HorizonDate);

public sealed record RegisterSaveResult(int Id, string Version, string Detail);

public sealed record ReceiveSpareResult(int SpareId, int Stock, string Version);

// ── Views ──
public sealed record WorkOrderPartView(int SpareId, string SpareName, int Qty, long? UnitCostPaise);

public sealed record WorkOrderHistoryView(string At, int By, string Action, string? Note);

public sealed record WorkOrderView(
    long WorkOrderId, string No, string Kind, string Title, string Priority, string Status, string Sla, string SlaDueUtc,
    int? RoomId, string? RoomNo, int? AssetId, string? Area, int? AssigneeId, int? VendorId,
    string CreatedUtc, int CreatedBy, string? StartedUtc, string? DoneUtc, string? VerifiedUtc,
    string? HoldReason, int? AwaitingSpareId, string? Note, string? ReturnReason, string? CancelReason,
    long? RepairTaskId, int? PlanId, string? DueDate, string? DowntimeFromUtc, string? DowntimeToUtc, long? BlockId,
    IReadOnlyList<WorkOrderPartView> Parts, long? PartsCostPaise, string Version);

public sealed record WorkOrderDetail(WorkOrderView Order, IReadOnlyList<WorkOrderHistoryView> History);

public sealed record WorkOrderPage(int Page, int PageSize, int Total, IReadOnlyList<WorkOrderView> Rows, bool OwnOnly, bool MoneyHidden);

public sealed record AssetView(int AssetId, string Name, string Kind, int? RoomId, string? RoomNo, string? Area, string Make, string Model, string? InstalledOn, int? VendorId, string Status, string Version);

public sealed record VendorView(int VendorId, string Name, string Trade, string Phone, string Version);

public sealed record SpareView(int SpareId, string Name, string Unit, int Stock, int ReorderAt, bool Low, long? CostPaise, string Version);

public sealed record PlanView(int PlanId, int AssetId, string Title, int EveryDays, string NextDue, bool Active, string? LastDone, string State, string Version);

public sealed record AmcView(int AmcId, int VendorId, string Scope, string From, string To, int Visits, string Status, long? ValuePaise, string Version);

public sealed record RegistersView(IReadOnlyList<AssetView> Assets, IReadOnlyList<VendorView> Vendors, IReadOnlyList<SpareView> Spares, IReadOnlyList<PlanView> Plans, bool MoneyHidden);

public sealed record AmcPage(IReadOnlyList<AmcView> Amcs, bool ValuesHidden);

public sealed record DowntimeRowView(int RoomId, string RoomNo, int Minutes, string Duration, int Orders, bool Running);

public sealed record AgeBucketView(string Label, int Count);

public sealed record KindRowView(string Kind, int Open, int Total);

public sealed record MaintenanceSummaryView(int Open, int InProgress, int OnHold, int Done, int Verified, int Breached);

/// <summary>Reports (manager roles). <c>MonthPartsCostPaise</c> is money: null unless the reader holds finance rights.</summary>
public sealed record MaintenanceReport(
    string BusinessDate, MaintenanceSummaryView Summary, IReadOnlyList<AgeBucketView> AgeBuckets, int? MttrMinutes, string? MttrDuration,
    int PmDue, int PmOnTime, int? PmCompliancePct, IReadOnlyList<DowntimeRowView> DowntimeByRoom, IReadOnlyList<KindRowView> ByKind,
    int PendingRepairs, IReadOnlyList<int> LowSpareIds, long? MonthPartsCostPaise);
