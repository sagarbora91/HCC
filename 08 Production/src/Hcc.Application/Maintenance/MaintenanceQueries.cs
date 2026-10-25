using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Maintenance;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Maintenance;

namespace Hcc.Application.Maintenance;

/// <summary>
/// Maintenance reads. Visibility follows the prototype's <c>mntVisibleOrders</c>: supervisors and managers see every work order;
/// a Maintenance user only their own; a Housekeeper only active work orders on rooms of their own open tasks. Money (part unit
/// prices, parts cost, AMC values) is filled only for readers with finance rights (DEC-063 G12): the selector never sends it to
/// Maintenance or Housekeeping, so hiding it on screen is not the only safeguard.
/// </summary>
public sealed class MaintenanceQueries(IAccessPolicy policy, IMaintenanceReadStore reads, TimeProvider clock)
{
    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static string? Iso(DateTime? utc) => utc is { } u ? Iso(u) : null;

    private async Task RequireViewAsync(int propertyId, int principalId, CancellationToken ct) =>
        await policy.RequireAsync(propertyId, principalId, MaintenancePermissions.View, AccessScopes.Any, null, ct).ConfigureAwait(false);

    private Task<bool> ManagerAsync(int propertyId, int principalId, CancellationToken ct) =>
        policy.HasAsync(propertyId, principalId, MaintenancePermissions.Manage, AccessScopes.Property(propertyId), ct);

    private Task<bool> MoneyAsync(int propertyId, int principalId, CancellationToken ct) =>
        policy.HasAsync(propertyId, principalId, MaintenancePermissions.Money, AccessScopes.Property(propertyId), ct);

    private static (int Page, int PageSize) Paging(int page, int pageSize)
    {
        if (page < 1) throw HccException.Validation("page starts at 1.");
        if (pageSize < 1 || pageSize > ApiConventions.MaxPageSize) throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.");
        return (page, pageSize);
    }

    private async Task<(IReadOnlyList<MntOrder> Orders, bool OwnOnly)> VisibleAsync(MntSnapshot snap, int propertyId, int principalId, bool manager, CancellationToken ct)
    {
        if (manager) return (snap.Orders, false);
        var housekeeper = await policy.HasAsync(propertyId, principalId, MaintenancePermissions.HousekeeperWork, AccessScopes.Any, ct).ConfigureAwait(false);
        if (housekeeper)
        {
            var rooms = await reads.OwnOpenTaskRoomsAsync(propertyId, principalId, ct).ConfigureAwait(false);
            return (snap.Orders.Where(w => w.RoomId is { } r && rooms.Contains(r) && w.IsActive).ToList(), true);
        }

        return (snap.Orders.Where(w => w.AssigneeId == principalId).ToList(), true);
    }

    private static WorkOrderView ToView(MntOrder w, MntSnapshot snap, DateTime now, bool money)
    {
        var spares = snap.Spares.ToDictionary(s => s.Id, s => s.Name);
        var parts = w.Parts.Select(p => new WorkOrderPartView(p.SpareId, spares.GetValueOrDefault(p.SpareId, "Spare"), p.Qty, money ? p.UnitCostPaise : null)).ToList();
        return new WorkOrderView(w.Id, w.No, w.Kind, w.Title, w.Priority, w.Status, MntRules.Sla(w, now), Iso(MntRules.SlaDue(w)),
            w.RoomId, w.RoomNo, w.AssetId, w.Area, w.AssigneeId, w.VendorId, Iso(w.CreatedUtc), w.CreatedBy, Iso(w.StartedUtc), Iso(w.DoneUtc), Iso(w.VerifiedUtc),
            w.HoldReason, w.AwaitingSpareId, w.Note, w.ReturnReason, w.CancelReason, w.RepairTaskId, w.PlanId,
            w.DueDate is { } d ? StayDates.ToWire(d) : null, Iso(w.DowntimeFromUtc), Iso(w.DowntimeToUtc), w.BlockId,
            parts, money ? w.PartsCostPaise : null, VersionToken.ToWire(w.Version));
    }

    public async Task<WorkOrderPage> ListWorkOrdersAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, string? status = null, string? priority = null, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        if (status is not null && !MntStatus.All.Contains(status)) throw HccException.Validation("Unknown status.");
        if (priority is not null && !MntPriority.All.Contains(priority)) throw HccException.Validation("Unknown priority.");
        await RequireViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var manager = await ManagerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var snap = await reads.LoadAsync(propertyId, now, ct).ConfigureAwait(false);
        var (visible, own) = await VisibleAsync(snap, propertyId, principalId, manager, ct).ConfigureAwait(false);
        var rows = MntRules.Sort(visible.Where(w => (status is null || w.Status == status) && (priority is null || w.Priority == priority))).ToList();
        return new WorkOrderPage(page, pageSize, rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize).Select(w => ToView(w, snap, now, money)).ToList(), own, !money);
    }

    public async Task<WorkOrderDetail> GetWorkOrderAsync(int propertyId, int principalId, long workOrderId, CancellationToken ct = default)
    {
        await RequireViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var manager = await ManagerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var snap = await reads.LoadAsync(propertyId, now, ct).ConfigureAwait(false);
        var (visible, _) = await VisibleAsync(snap, propertyId, principalId, manager, ct).ConfigureAwait(false);
        var w = visible.FirstOrDefault(x => x.Id == workOrderId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Work order not found.");
        var history = await reads.HistoryAsync(workOrderId, ct).ConfigureAwait(false);
        return new WorkOrderDetail(ToView(w, snap, now, money), history.Select(h => new WorkOrderHistoryView(Iso(h.AtUtc), h.By, h.Action, h.Note)).ToList());
    }

    /// <summary>Assets, vendors, spares and plans. Spare unit costs only with finance rights. Technicians see assets and spares (no money).</summary>
    public async Task<RegistersView> RegistersAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var manager = await ManagerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var snap = await reads.LoadAsync(propertyId, now, ct).ConfigureAwait(false);
        var assets = snap.Assets.Select(a => new AssetView(a.Id, a.Name, a.Kind, a.RoomId, a.RoomNo, a.Area, a.Make, a.Model, a.InstalledOn is { } d ? StayDates.ToWire(d) : null, a.VendorId, a.Status, VersionToken.ToWire(a.Version))).ToList();
        var vendors = manager ? snap.Vendors.Select(v => new VendorView(v.Id, v.Name, v.Trade, v.Phone, VersionToken.ToWire(v.Version))).ToList() : [];
        var spares = snap.Spares.Select(s => new SpareView(s.Id, s.Name, s.Unit, s.Stock, s.ReorderAt, s.IsLow, money ? s.CostPaise : null, VersionToken.ToWire(s.Version))).ToList();
        var plans = manager
            ? snap.Plans.Select(p => new PlanView(p.Id, p.AssetId, p.Title, p.EveryDays, StayDates.ToWire(p.NextDue), p.Active, p.LastDone is { } l ? StayDates.ToWire(l) : null,
                MntRules.PlanState(p, snap.Orders, snap.BusinessDate), VersionToken.ToWire(p.Version))).ToList()
            : [];
        return new RegistersView(assets, vendors, spares, plans, !money);
    }

    /// <summary>AMC contracts: supervisor level and above; the contract value only with finance rights (Owner, Manager, Accountant).</summary>
    public async Task<AmcPage> AmcsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MaintenancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var snap = await reads.LoadAsync(propertyId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return new AmcPage(snap.Amcs.Select(a => new AmcView(a.Id, a.VendorId, a.Scope, StayDates.ToWire(a.From), StayDates.ToWire(a.To), a.Visits,
            MntRules.AmcStatus(a.From, a.To, snap.BusinessDate), money ? a.ValuePaise : null, VersionToken.ToWire(a.Version))).ToList(), !money);
    }

    /// <summary>Reports (supervisor level and above): age buckets, MTTR, PM compliance, SLA breaches, downtime per room, work orders by asset type.</summary>
    public async Task<MaintenanceReport> ReportAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MaintenancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var snap = await reads.LoadAsync(propertyId, now, ct).ConfigureAwait(false);
        var s = MntRules.Summary(snap.Orders, now);
        var mttr = MntRules.Mttr(snap.Orders);
        var pm = MntRules.PmCompliance(snap.Orders, snap.BusinessDate);
        return new MaintenanceReport(
            StayDates.ToWire(snap.BusinessDate), new MaintenanceSummaryView(s.Open, s.InProgress, s.OnHold, s.Done, s.Verified, s.Breached),
            MntRules.AgeBuckets(snap.Orders, now).Select(b => new AgeBucketView(b.Label, b.Count)).ToList(),
            mttr, mttr is { } m ? MntRules.Duration(m) : null, pm.Due, pm.OnTime, pm.Pct,
            MntRules.DowntimeByRoom(snap.Orders, snap.BlockedRoomIds, now).Select(d => new DowntimeRowView(d.RoomId, d.RoomNo, d.Minutes, MntRules.Duration(d.Minutes), d.Orders, d.Running)).ToList(),
            MntRules.ByKind(snap.Orders, snap.Assets.ToDictionary(a => a.Id)).Select(k => new KindRowView(k.Kind, k.Open, k.Total)).ToList(),
            snap.PendingRepairs, MntRules.LowSpares(snap.Spares).Select(x => x.Id).ToList(),
            money ? MntRules.MonthPartsCost(snap.Orders, snap.BusinessDate) : null);
    }
}
