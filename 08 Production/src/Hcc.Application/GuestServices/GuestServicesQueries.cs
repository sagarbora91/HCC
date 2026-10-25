using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.GuestServices;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestServices;
using StaysVersion = Hcc.Application.Stays.VersionToken;

namespace Hcc.Application.GuestServices;

/// <summary>
/// Guest-services reads. Front office (guest.edit) sees everything; a supervisor (hk.assign) sees housekeeping and maintenance
/// requests; housekeeping / maintenance staff see only the requests assigned to them and the lost items they logged (no claimant
/// data). Reports need report.operations or front-office rights.
/// </summary>
public sealed class GuestServicesQueries(IAccessPolicy policy, IGuestServicesReadStore reads, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string Iso(DateTime utc) => GuestServicesCommands.Iso(utc);

    private static (int Page, int PageSize) Paging(int page, int pageSize)
    {
        if (page < 1) throw HccException.Validation("page starts at 1.");
        if (pageSize < 1 || pageSize > ApiConventions.MaxPageSize) throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.");
        return (page, pageSize);
    }

    private Task<bool> WideAsync(int p, int u, string perm, CancellationToken ct) => policy.HasAsync(p, u, perm, AccessScopes.Property(p), ct);

    private Task<bool> AnyAsync(int p, int u, string perm, CancellationToken ct) => policy.HasAsync(p, u, perm, AccessScopes.Any, ct);

    // ── Requests ──

    public async Task<GuestServicePage<ServiceRequestView>> RequestsAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, bool openOnly = false, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        RequestFilter filter;
        bool own;
        if (await WideAsync(propertyId, principalId, GuestServicePermissions.Manage, ct).ConfigureAwait(false)) (filter, own) = (new RequestFilter(null, null, openOnly), false);
        else if (await WideAsync(propertyId, principalId, GuestServicePermissions.Supervise, ct).ConfigureAwait(false)) (filter, own) = (new RequestFilter(null, "NotFrontDesk", openOnly), false);
        else
        {
            if (!await AnyAsync(propertyId, principalId, GuestServicePermissions.HousekeepingWork, ct).ConfigureAwait(false)
                && !await AnyAsync(propertyId, principalId, GuestServicePermissions.MaintenanceWork, ct).ConfigureAwait(false))
                await policy.RequireAsync(propertyId, principalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
            (filter, own) = (new RequestFilter(principalId, null, openOnly), true);
        }

        var props = await reads.PropertyAsync(propertyId, ct).ConfigureAwait(false);
        var rows = await reads.RequestsAsync(propertyId, filter, ct).ConfigureAwait(false);
        var now = Now;
        return new GuestServicePage<ServiceRequestView>(page, pageSize, rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize).Select(r => ToView(r, now, props.TimeZone)).ToList(), own);
    }

    public async Task<ServiceRequestView> RequestAsync(int propertyId, int principalId, long requestId, CancellationToken ct = default)
    {
        var r = await reads.RequestAsync(propertyId, requestId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Service request not found.");
        var wide = await WideAsync(propertyId, principalId, GuestServicePermissions.Manage, ct).ConfigureAwait(false);
        var sup = !wide && r.Dept != GuestServiceVocab.FrontDesk && await WideAsync(propertyId, principalId, GuestServicePermissions.Supervise, ct).ConfigureAwait(false);
        if (!wide && !sup && r.AssigneeId != principalId) throw new HccException(HccErrorCode.FORBIDDEN, "Only requests assigned to you are accessible.");
        var props = await reads.PropertyAsync(propertyId, ct).ConfigureAwait(false);
        return ToView(r, Now, props.TimeZone);
    }

    private static ServiceRequestView ToView(RequestRecord r, DateTime now, string tz)
    {
        var sla = GuestServiceRules.Label(r.Fact, now, tz);
        return new ServiceRequestView(r.RequestId, r.StayId, r.RoomNo, r.GuestName, r.Type, r.Detail, r.Dept, r.AssigneeId, r.Priority, r.Status, Iso(r.CreatedUtc), Iso(r.DueUtc),
            r.ScheduledUtc is { } s ? Iso(s) : null, r.StartedUtc is { } st ? Iso(st) : null, r.DoneUtc is { } d ? Iso(d) : null, r.GuestNote, r.CancelReason,
            new SlaView(sla.Tone, sla.Label, sla.MinutesLeft), StaysVersion.ToWire(r.Version));
    }

    // ── Complaints ──

    public async Task<GuestServicePage<ComplaintView>> ComplaintsAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, bool openOnly = false, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        await policy.RequireAsync(propertyId, principalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        var props = await reads.PropertyAsync(propertyId, ct).ConfigureAwait(false);
        var rows = await reads.ComplaintsAsync(propertyId, openOnly, ct).ConfigureAwait(false);
        return new GuestServicePage<ComplaintView>(page, pageSize, rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize).Select(c => ToView(c, props.BusinessDate)).ToList(), false);
    }

    public async Task<ComplaintView> ComplaintAsync(int propertyId, int principalId, long complaintId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        var c = await reads.ComplaintAsync(propertyId, complaintId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Complaint not found.");
        return ToView(c, (await reads.PropertyAsync(propertyId, ct).ConfigureAwait(false)).BusinessDate);
    }

    private static ComplaintView ToView(ComplaintRecord c, DateOnly today) => new(c.ComplaintId, c.StayId, c.RoomNo, c.GuestName, c.Category, c.Severity, c.Description, c.OwnerId, c.Status,
        c.RootCause, c.RecoveryAction, c.FollowUpOn, c.Status == GuestServiceVocab.Resolved && c.FollowUpOn is { } f && f <= today,
        c.FollowUpOutcome is null || c.FollowUpUtc is null ? null : new ComplaintFollowUpView(c.FollowUpOutcome, c.FollowUpSatisfied == true, Iso(c.FollowUpUtc.Value), c.FollowUpBy ?? 0),
        Iso(c.CreatedUtc), c.ResolvedUtc is { } r ? Iso(r) : null, c.ClosedUtc is { } cl ? Iso(cl) : null,
        c.RecoveryState is null || c.RecoveryAmountPaise is null ? null : new RecoveryView(c.RecoveryAmountPaise.Value, c.RecoveryReason ?? string.Empty, c.RecoveryApprovalId, c.RecoveryChargeId, Iso(c.RecoveryUtc ?? c.CreatedUtc), c.RecoveryBy ?? 0),
        c.RecoveryStatus, StaysVersion.ToWire(c.Version));

    // ── Lost & found ──

    public async Task<GuestServicePage<LostItemView>> ItemsAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var wide = await WideAsync(propertyId, principalId, GuestServicePermissions.Manage, ct).ConfigureAwait(false);
        if (!wide && !await AnyAsync(propertyId, principalId, GuestServicePermissions.HousekeepingWork, ct).ConfigureAwait(false)
            && !await AnyAsync(propertyId, principalId, GuestServicePermissions.MaintenanceWork, ct).ConfigureAwait(false))
            await policy.RequireAsync(propertyId, principalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        var props = await reads.PropertyAsync(propertyId, ct).ConfigureAwait(false);
        var (settings, _) = await reads.SettingsAsync(propertyId, ct).ConfigureAwait(false);
        var rows = await reads.ItemsAsync(propertyId, wide ? null : principalId, ct).ConfigureAwait(false);
        return new GuestServicePage<LostItemView>(page, pageSize, rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize).Select(i => ToView(i, props.BusinessDate, settings, wide)).ToList(), !wide);
    }

    private static LostItemView ToView(ItemRecord i, DateOnly today, GuestServiceSettings s, bool showClaim) => new(i.ItemId, i.Description, i.Category, i.Valuable, i.FoundWhere, i.RoomNo, i.StayId,
        Iso(i.FoundUtc), i.FoundDate, i.FoundBy, i.Storage, i.Status, GuestServiceRules.DisposeOn(i.FoundDate, i.Valuable, s),
        GuestServiceRules.IsDisposable(i.Status, today, i.FoundDate, i.Valuable, s),
        showClaim && i.ClaimName is not null ? new LostItemClaimView(i.ClaimName, i.ClaimPhone ?? string.Empty, i.ClaimIdType ?? string.Empty, i.ClaimIdNote ?? string.Empty, i.ClaimMatchNote ?? string.Empty, Iso(i.ClaimUtc ?? i.FoundUtc), i.ClaimBy ?? 0) : null,
        showClaim ? i.HandoverTo : null, i.DisposalMethod, StaysVersion.ToWire(i.Version));

    // ── Settings, history, reports ──

    public async Task<GuestServiceSettingsView> SettingsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        if (!await WideAsync(propertyId, principalId, GuestServicePermissions.Manage, ct).ConfigureAwait(false))
            await policy.RequireAsync(propertyId, principalId, GuestServicePermissions.Approve, ct: ct).ConfigureAwait(false);
        var (s, v) = await reads.SettingsAsync(propertyId, ct).ConfigureAwait(false);
        return new GuestServiceSettingsView(s.Sla, s.RetentionDays, s.ValuableRetentionDays, v is null ? null : StaysVersion.ToWire(v));
    }

    /// <summary>History trail of a request, complaint or item (<paramref name="kind"/> = request | complaint | item); front office only.</summary>
    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(int propertyId, int principalId, string kind, long recordId, CancellationToken ct = default)
    {
        if (kind is not ("request" or "complaint" or "item")) throw HccException.Validation("kind is request, complaint or item.");
        await policy.RequireAsync(propertyId, principalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return (await reads.HistoryAsync(propertyId, kind, recordId, ct).ConfigureAwait(false)).Select(l => new HistoryEntry(Iso(l.AtUtc), l.By, l.What)).ToList();
    }

    private async Task RequireReportAsync(int propertyId, int principalId, CancellationToken ct)
    {
        if (!await WideAsync(propertyId, principalId, GuestServicePermissions.Manage, ct).ConfigureAwait(false))
            await policy.RequireAsync(propertyId, principalId, GuestServicePermissions.Report, ct: ct).ConfigureAwait(false);
    }

    public async Task<ResponseReport> ResponseReportAsync(int propertyId, int principalId, bool byDept, CancellationToken ct = default)
    {
        await RequireReportAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var facts = (await reads.RequestsAsync(propertyId, new RequestFilter(null, null, false), ct).ConfigureAwait(false)).Select(r => r.Fact).ToList();
        var now = Now;
        var rows = GuestServiceRules.ResponseReport(facts, now, byDept).Select(r => new ResponseRow(r.Key, r.Count, r.Done, r.AvgMinutes, r.WithinSla, r.Pct, r.Open, r.Overdue)).ToList();
        var t = GuestServiceRules.ResponseTotals(facts, now);
        return new ResponseReport(rows, new ResponseTotals(t.Done, t.Within, t.Pct, t.Avg, t.Open, t.Overdue));
    }

    public async Task<IReadOnlyList<ComplaintCategoryRow>> ComplaintReportAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireReportAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var facts = (await reads.ComplaintsAsync(propertyId, false, ct).ConfigureAwait(false)).Select(c => new ComplaintFact(c.Category, c.Severity, c.Status, c.RecoveryState is not null)).ToList();
        return GuestServiceRules.ComplaintReport(facts).Select(r => new ComplaintCategoryRow(r.Category, r.Total, r.Open, r.BySeverity, r.Recovery)).ToList();
    }

    public async Task<LostFoundSummary> LostFoundSummaryAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireReportAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var props = await reads.PropertyAsync(propertyId, ct).ConfigureAwait(false);
        var (s, _) = await reads.SettingsAsync(propertyId, ct).ConfigureAwait(false);
        var xs = await reads.ItemsAsync(propertyId, null, ct).ConfigureAwait(false);
        return new LostFoundSummary(xs.Count(i => i.Status == GuestServiceVocab.Stored), xs.Count(i => i.Status == GuestServiceVocab.Claimed), xs.Count(i => i.Status == GuestServiceVocab.HandedOver),
            xs.Count(i => i.Status == GuestServiceVocab.Disposed), xs.Count(i => GuestServiceRules.IsDisposable(i.Status, props.BusinessDate, i.FoundDate, i.Valuable, s)),
            xs.Count(i => i.Valuable && i.Status is GuestServiceVocab.Stored or GuestServiceVocab.Claimed));
    }
}
