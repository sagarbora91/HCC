using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.GuestServices;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestServices;
using StaysVersion = Hcc.Application.Stays.VersionToken;

namespace Hcc.Application.GuestServices;

/// <summary>
/// Guest-services use cases ported from the prototype handlers (<c>03 Prototype/src/services/mock/handlers/services.ts</c>):
/// service requests with SLAs, complaints (root cause, recovery, follow-up) and lost &amp; found. Every command goes through
/// <see cref="ICommandRunner"/> (idempotency, audit, outbox); the actor is the session principal in the context. Recovery discounts
/// are requested through Billing's <see cref="BillingService.RequestAdjustmentAsync"/> (DEC-041 binding, DEC-063 limits) in three
/// idempotent phases (claim, Billing call, link) - this module never posts money itself.
/// </summary>
public sealed partial class GuestServicesCommands(ICommandRunner runner, IAccessPolicy policy, IGuestServicesStore store,
    BillingService billing, IBillingStore billingStore, TimeProvider clock)
{
    public const string OpLogRequest = "LogServiceRequest";
    public const string OpAssignRequest = "AssignServiceRequest";
    public const string OpStartRequest = "StartServiceRequest";
    public const string OpCompleteRequest = "CompleteServiceRequest";
    public const string OpCancelRequest = "CancelServiceRequest";
    public const string OpLogComplaint = "LogComplaint";
    public const string OpUpdateComplaint = "UpdateComplaint";
    public const string OpResolveComplaint = "ResolveComplaint";
    public const string OpFollowUp = "RecordComplaintFollowUp";
    public const string OpOfferRecovery = "OfferRecoveryDiscount";
    public const string OpLogItem = "LogLostItem";
    public const string OpClaimItem = "ClaimLostItem";
    public const string OpHandOverItem = "HandOverLostItem";
    public const string OpDisposeItem = "DisposeLostItem";
    public const string OpSaveSettings = "SaveGuestServiceSettings";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    internal static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string? IsoOrNull(DateTime? utc) => utc is { } u ? Iso(u) : null;

    private static string Text(string? value, int min, int max, string whatRequired, string whatTooLong)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length < min) throw HccException.Validation(whatRequired);
        if (t.Length > max) throw HccException.Validation(whatTooLong);
        return t;
    }

    private static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);

    private static HccException NotFound(string message) => new(HccErrorCode.NOT_FOUND, message);

    // ── Authorization helpers (grants reused from other modules; see GuestServicePermissions) ──

    private Task<bool> WideAsync(CommandContext c, string perm, CancellationToken ct) =>
        policy.HasAsync(c.PropertyId, c.PrincipalId, perm, AccessScopes.Property(c.PropertyId), ct);

    private Task<bool> AnyScopeAsync(int propertyId, int principalId, string perm, CancellationToken ct) =>
        policy.HasAsync(propertyId, principalId, perm, AccessScopes.Any, ct);

    /// <summary>Front office, or (for housekeeping / maintenance requests) a supervisor.</summary>
    private async Task RequireSuperviseAsync(CommandContext c, string dept, CancellationToken ct)
    {
        if (await WideAsync(c, GuestServicePermissions.Manage, ct).ConfigureAwait(false)) return;
        if (dept != GuestServiceVocab.FrontDesk && await WideAsync(c, GuestServicePermissions.Supervise, ct).ConfigureAwait(false)) return;
        await policy.RequireAsync(c.PropertyId, c.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
    }

    /// <summary>Assignees: HK-003 for housekeeping, a maintenance profile (room.block.request without HK-003 or front-office rights) for maintenance, front office for the desk.</summary>
    private async Task<bool> EligibleAsync(CommandContext c, int user, string dept, CancellationToken ct)
    {
        if (user <= 0) return false;
        switch (dept)
        {
            case GuestServiceVocab.Housekeeping:
                return await AnyScopeAsync(c.PropertyId, user, GuestServicePermissions.HousekeepingWork, ct).ConfigureAwait(false);
            case GuestServiceVocab.Maintenance:
                return await AnyScopeAsync(c.PropertyId, user, GuestServicePermissions.MaintenanceWork, ct).ConfigureAwait(false)
                    && !await AnyScopeAsync(c.PropertyId, user, GuestServicePermissions.HousekeepingWork, ct).ConfigureAwait(false)
                    && !await policy.HasAsync(c.PropertyId, user, GuestServicePermissions.Manage, AccessScopes.Property(c.PropertyId), ct).ConfigureAwait(false);
            default:
                return await policy.HasAsync(c.PropertyId, user, GuestServicePermissions.Manage, AccessScopes.Property(c.PropertyId), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Front office may work any request; housekeeping / maintenance staff only the ones assigned to them (RBAC scope A).</summary>
    private async Task RequireWorkAsync(CommandContext c, RequestRecord r, CancellationToken ct)
    {
        if (await WideAsync(c, GuestServicePermissions.Manage, ct).ConfigureAwait(false)) return;
        var perm = r.Dept switch
        {
            GuestServiceVocab.Housekeeping => GuestServicePermissions.HousekeepingWork,
            GuestServiceVocab.Maintenance => GuestServicePermissions.MaintenanceWork,
            _ => null,
        };
        if (perm is not null)
        {
            if (await WideAsync(c, perm, ct).ConfigureAwait(false)) return;
            if (r.AssigneeId == c.PrincipalId && await AnyScopeAsync(c.PropertyId, c.PrincipalId, perm, ct).ConfigureAwait(false)) return;
        }

        throw new HccException(HccErrorCode.FORBIDDEN, "Only requests assigned to you are accessible.");
    }

    private static ServiceRequestResult ToResult(RequestRecord r, byte[] version) => new(r.RequestId, r.Status, r.Dept, r.AssigneeId, r.Priority, Iso(r.DueUtc),
        IsoOrNull(r.ScheduledUtc), StaysVersion.ToWire(version));

    private async Task<RequestRecord> LockRequestAsync(ICommandTransaction tx, CommandContext c, long id, string expectedVersion, CancellationToken ct)
    {
        var expected = StaysVersion.Parse(expectedVersion);
        var r = await store.LockRequestAsync(tx, c.PropertyId, id, ct).ConfigureAwait(false) ?? throw NotFound("Service request not found.");
        StaysVersion.EnsureMatches(r.Version, expected, "This request");
        return r;
    }

    // ── Service requests ──

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex Hhmm();

    public async Task<CommandResult<ServiceRequestResult>> LogRequestAsync(CommandContext ctx, LogServiceRequestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD: refuse when the property's business date is closed (IBusinessDayGuard) once P6 is merged.
            var stay = await store.StayAsync(tx, ctx.PropertyId, req.StayId, ct2).ConfigureAwait(false) ?? throw NotFound("Stay not found.");
            if (stay.Status != "InHouse") throw Invalid("Service requests can be logged only for in-house guests.");
            var type = req.RequestType ?? string.Empty;
            if (!GuestServiceVocab.RequestTypes.Contains(type)) throw HccException.Validation("Choose the request type.");
            var dept = string.IsNullOrWhiteSpace(req.Dept) ? GuestServiceRules.DefaultDept(type) : req.Dept;
            if (!GuestServiceVocab.Depts.Contains(dept)) throw HccException.Validation("Choose a department.");
            var urgent = req.Priority == "Urgent";
            if (req.Priority is not (null or "" or "Normal" or "Urgent")) throw HccException.Validation("Priority is Normal or Urgent.");
            var detail = (req.Detail ?? string.Empty).Trim();
            if (detail.Length > 160) throw HccException.Validation("Keep the detail under 160 characters.");
            if (type == GuestServiceVocab.Other && detail.Length < 3) throw HccException.Validation("Describe the request.");

            var now = Now;
            var props = await store.PropertyAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            var (settings, _) = await store.LockSettingsAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            var sla = GuestServiceRules.SlaMinutes(settings, type, urgent);
            DateTime? scheduled = null;
            DateTime due;
            if (type == GuestServiceVocab.WakeUp)
            {
                if (!Hhmm().IsMatch(req.Time ?? string.Empty)) throw HccException.Validation("Enter the wake-up time as HH:mm.");
                var tz = CalendarDate.AssertTimeZone(props.TimeZone);
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
                var hh = TimeOnly.ParseExact(req.Time!, "HH:mm", CultureInfo.InvariantCulture);
                var local = DateTime.SpecifyKind(DateOnly.FromDateTime(localNow).ToDateTime(hh), DateTimeKind.Unspecified);
                if (local <= localNow) local = local.AddDays(1);
                scheduled = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                due = scheduled.Value.AddMinutes(sla);
            }
            else due = now.AddMinutes(sla);

            if (req.AssigneeId is { } a && !await EligibleAsync(ctx, a, dept, ct2).ConfigureAwait(false))
                throw HccException.Validation($"Choose an enabled {dept.ToLowerInvariant()} team member.");
            var status = req.AssigneeId is null ? GuestServiceVocab.Open : GuestServiceVocab.Assigned;
            var id = await store.InsertRequestAsync(tx, new NewRequest(ctx.PropertyId, stay, type, detail, dept, req.AssigneeId, urgent ? "Urgent" : "Normal", status, now, due, scheduled, ctx.PrincipalId), ct2).ConfigureAwait(false);
            var rec = (await store.LockRequestAsync(tx, ctx.PropertyId, id, ct2).ConfigureAwait(false))!;
            await store.AppendLogAsync(tx, "request", id, now, ctx.PrincipalId, "Logged" + (req.AssigneeId is { } who ? $" · assigned to #{who}" : string.Empty), ct2).ConfigureAwait(false);
            await tx.AuditAsync("service-request.log", "request:" + id.ToString(CultureInfo.InvariantCulture), $"room {stay.RoomNo} · {type} · {dept} · due {Iso(due)}", ct2).ConfigureAwait(false);
            await tx.PublishAsync(GuestServiceEvents.RequestLogged, id, new { requestId = id, stayId = stay.StayId, room = stay.RoomNo, type, dept }, ct2).ConfigureAwait(false);
            return ToResult(rec, rec.Version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ServiceRequestResult>> AssignRequestAsync(CommandContext ctx, AssignServiceRequestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var r = await LockRequestAsync(tx, ctx, req.RequestId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            await RequireSuperviseAsync(ctx, r.Dept, ct2).ConfigureAwait(false);
            if (!GuestServiceRules.IsOpen(r.Status)) throw Invalid("This request is already finished.");
            var dept = string.IsNullOrWhiteSpace(req.Dept) ? r.Dept : req.Dept;
            if (!GuestServiceVocab.Depts.Contains(dept)) throw HccException.Validation("Choose a department.");
            if (dept != r.Dept) await RequireSuperviseAsync(ctx, dept, ct2).ConfigureAwait(false);
            if (!await EligibleAsync(ctx, req.AssigneeId, dept, ct2).ConfigureAwait(false))
                throw HccException.Validation($"Choose an enabled {dept.ToLowerInvariant()} team member.");
            var u = r with { Dept = dept, AssigneeId = req.AssigneeId, Status = r.Status == GuestServiceVocab.Open ? GuestServiceVocab.Assigned : r.Status };
            var version = await store.UpdateRequestAsync(tx, u, r.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "request", r.RequestId, Now, ctx.PrincipalId, $"Assigned to #{req.AssigneeId} ({dept})", ct2).ConfigureAwait(false);
            await tx.AuditAsync("service-request.assign", "request:" + r.RequestId.ToString(CultureInfo.InvariantCulture), $"{dept} · assignee {req.AssigneeId}", ct2).ConfigureAwait(false);
            return ToResult(u, version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ServiceRequestResult>> StartRequestAsync(CommandContext ctx, StartServiceRequestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var r = await LockRequestAsync(tx, ctx, req.RequestId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            await RequireWorkAsync(ctx, r, ct2).ConfigureAwait(false);
            if (r.Status is not (GuestServiceVocab.Open or GuestServiceVocab.Assigned)) throw Invalid("This request has already started or finished.");
            var now = Now;
            var u = r with { Status = GuestServiceVocab.InProgress, StartedUtc = now, AssigneeId = r.AssigneeId ?? ctx.PrincipalId };
            var version = await store.UpdateRequestAsync(tx, u, r.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "request", r.RequestId, now, ctx.PrincipalId, "Started", ct2).ConfigureAwait(false);
            await tx.AuditAsync("service-request.start", "request:" + r.RequestId.ToString(CultureInfo.InvariantCulture), "started", ct2).ConfigureAwait(false);
            return ToResult(u, version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ServiceRequestResult>> CompleteRequestAsync(CommandContext ctx, CompleteServiceRequestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var r = await LockRequestAsync(tx, ctx, req.RequestId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            await RequireWorkAsync(ctx, r, ct2).ConfigureAwait(false);
            if (!GuestServiceRules.IsOpen(r.Status)) throw Invalid("This request is already finished.");
            var note = Text(req.Note, 3, 200, "Add a short note for the guest (e.g. “Two bath towels delivered”).", "Keep the guest note under 200 characters.");
            var now = Now;
            var u = r with { Status = GuestServiceVocab.Done, DoneUtc = now, GuestNote = note, StartedUtc = r.StartedUtc ?? now, AssigneeId = r.AssigneeId ?? ctx.PrincipalId };
            var version = await store.UpdateRequestAsync(tx, u, r.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "request", r.RequestId, now, ctx.PrincipalId, "Completed · " + note, ct2).ConfigureAwait(false);
            await tx.AuditAsync("service-request.complete", "request:" + r.RequestId.ToString(CultureInfo.InvariantCulture), $"{(GuestServiceRules.IsLate(u.Fact) ? "late" : "within SLA")}", ct2).ConfigureAwait(false);
            await tx.PublishAsync(GuestServiceEvents.RequestDone, r.RequestId, new { requestId = r.RequestId, stayId = r.StayId, room = r.RoomNo, type = r.Type, guestNote = note }, ct2).ConfigureAwait(false);
            return ToResult(u, version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ServiceRequestResult>> CancelRequestAsync(CommandContext ctx, CancelServiceRequestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var r = await LockRequestAsync(tx, ctx, req.RequestId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            await RequireSuperviseAsync(ctx, r.Dept, ct2).ConfigureAwait(false);
            if (!GuestServiceRules.IsOpen(r.Status)) throw Invalid("This request is already finished.");
            var reason = Text(req.Reason, 3, 200, "Enter the reason for cancelling.", "Keep the reason under 200 characters.");
            var u = r with { Status = GuestServiceVocab.Cancelled, CancelReason = reason };
            var version = await store.UpdateRequestAsync(tx, u, r.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "request", r.RequestId, Now, ctx.PrincipalId, "Cancelled · " + reason, ct2).ConfigureAwait(false);
            await tx.AuditAsync("service-request.cancel", "request:" + r.RequestId.ToString(CultureInfo.InvariantCulture), reason, ct2).ConfigureAwait(false);
            return ToResult(u, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Complaints ──

    private static ComplaintResult ToResult(ComplaintRecord c, byte[] version) => new(c.ComplaintId, c.Status, c.Severity, c.OwnerId, c.FollowUpOn, StaysVersion.ToWire(version));

    private async Task<ComplaintRecord> LockComplaintAsync(ICommandTransaction tx, CommandContext c, long id, string expectedVersion, CancellationToken ct)
    {
        var expected = StaysVersion.Parse(expectedVersion);
        var rec = await store.LockComplaintAsync(tx, c.PropertyId, id, ct).ConfigureAwait(false) ?? throw NotFound("Complaint not found.");
        StaysVersion.EnsureMatches(rec.Version, expected, "This complaint");
        return rec;
    }

    private async Task RequireOwnerAsync(CommandContext c, int owner, CancellationToken ct)
    {
        if (owner <= 0 || !await policy.HasAsync(c.PropertyId, owner, GuestServicePermissions.Manage, AccessScopes.Property(c.PropertyId), ct).ConfigureAwait(false))
            throw HccException.Validation("The owner must be an enabled front-office user.");
    }

    public async Task<CommandResult<ComplaintResult>> LogComplaintAsync(CommandContext ctx, LogComplaintRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            StayInfo? stay = null;
            var guest = (req.GuestName ?? string.Empty).Trim();
            if (req.StayId is { } sid)
            {
                stay = await store.StayAsync(tx, ctx.PropertyId, sid, ct2).ConfigureAwait(false) ?? throw NotFound("Stay not found.");
                if (guest.Length == 0) guest = stay.GuestName;
            }

            if (guest.Length < 2) throw HccException.Validation("Enter the guest’s name.");
            if (!GuestServiceVocab.ComplaintCategories.Contains(req.Category ?? string.Empty)) throw HccException.Validation("Choose a category.");
            if (!GuestServiceVocab.Severities.Contains(req.Severity ?? string.Empty)) throw HccException.Validation("Choose the severity.");
            var description = Text(req.Description, 5, 400, "Describe the complaint.", "Keep the description under 400 characters.");
            var owner = req.OwnerId ?? ctx.PrincipalId;
            await RequireOwnerAsync(ctx, owner, ct2).ConfigureAwait(false);
            var now = Now;
            var id = await store.InsertComplaintAsync(tx, new NewComplaint(ctx.PropertyId, stay?.StayId, stay?.RoomId, guest, req.Category!, req.Severity!, description, owner, now, ctx.PrincipalId), ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "complaint", id, now, ctx.PrincipalId, $"Logged · owner #{owner}", ct2).ConfigureAwait(false);
            await tx.AuditAsync("complaint.log", "complaint:" + id.ToString(CultureInfo.InvariantCulture), $"{req.Category} · {req.Severity}", ct2).ConfigureAwait(false);
            await tx.PublishAsync(GuestServiceEvents.ComplaintLogged, id, new { complaintId = id, category = req.Category, severity = req.Severity }, ct2).ConfigureAwait(false);
            var rec = (await store.LockComplaintAsync(tx, ctx.PropertyId, id, ct2).ConfigureAwait(false))!;
            return ToResult(rec, rec.Version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ComplaintResult>> UpdateComplaintAsync(CommandContext ctx, UpdateComplaintRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var c = await LockComplaintAsync(tx, ctx, req.ComplaintId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            if (c.Status is not (GuestServiceVocab.Open or GuestServiceVocab.Investigating)) throw Invalid("Only an open complaint can be updated.");
            var changes = new List<string>();
            var u = c;
            if (req.OwnerId is { } o && o != c.OwnerId)
            {
                await RequireOwnerAsync(ctx, o, ct2).ConfigureAwait(false);
                u = u with { OwnerId = o };
                changes.Add("owner");
            }

            if (!string.IsNullOrEmpty(req.Severity) && req.Severity != c.Severity)
            {
                if (!GuestServiceVocab.Severities.Contains(req.Severity)) throw HccException.Validation("Choose the severity.");
                u = u with { Severity = req.Severity };
                changes.Add("severity " + req.Severity);
            }

            if (req.RootCause is not null && req.RootCause.Trim() != (c.RootCause ?? string.Empty))
            {
                u = u with { RootCause = NullIfEmpty(Text(req.RootCause, 0, 300, string.Empty, "Keep the root cause under 300 characters.")) };
                changes.Add("root cause");
            }

            if (req.RecoveryAction is not null && req.RecoveryAction.Trim() != (c.RecoveryAction ?? string.Empty))
            {
                u = u with { RecoveryAction = NullIfEmpty(Text(req.RecoveryAction, 0, 300, string.Empty, "Keep the recovery action under 300 characters.")) };
                changes.Add("recovery action");
            }

            if (c.Status == GuestServiceVocab.Open)
            {
                u = u with { Status = GuestServiceVocab.Investigating };
                changes.Add("investigating");
            }

            if (changes.Count == 0) throw HccException.Validation("Nothing to update.");
            var version = await store.UpdateComplaintAsync(tx, u, c.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "complaint", c.ComplaintId, Now, ctx.PrincipalId, "Updated · " + string.Join(", ", changes), ct2).ConfigureAwait(false);
            await tx.AuditAsync("complaint.update", "complaint:" + c.ComplaintId.ToString(CultureInfo.InvariantCulture), string.Join(", ", changes), ct2).ConfigureAwait(false);
            return ToResult(u, version);
        }, ct).ConfigureAwait(false);
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    public async Task<CommandResult<ComplaintResult>> ResolveComplaintAsync(CommandContext ctx, ResolveComplaintRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var c = await LockComplaintAsync(tx, ctx, req.ComplaintId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            if (c.Status is not (GuestServiceVocab.Open or GuestServiceVocab.Investigating)) throw Invalid("Only an open complaint can be resolved.");
            var rootCause = Text(req.RootCause ?? c.RootCause, 3, 300, "Record the root cause before resolving.", "Keep the root cause under 300 characters.");
            var action = Text(req.RecoveryAction ?? c.RecoveryAction, 3, 300, "Record the recovery action taken.", "Keep the recovery action under 300 characters.");
            var today = (await store.PropertyAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false)).BusinessDate;
            var followUp = req.FollowUpOn ?? today.AddDays(1);
            if (followUp < today) throw HccException.Validation("The follow-up date cannot be in the past.");
            var now = Now;
            var u = c with { RootCause = rootCause, RecoveryAction = action, Status = GuestServiceVocab.Resolved, ResolvedUtc = now, FollowUpOn = followUp };
            var version = await store.UpdateComplaintAsync(tx, u, c.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "complaint", c.ComplaintId, now, ctx.PrincipalId, "Resolved · follow-up " + StayDates.ToWire(followUp), ct2).ConfigureAwait(false);
            await tx.AuditAsync("complaint.resolve", "complaint:" + c.ComplaintId.ToString(CultureInfo.InvariantCulture), "follow-up " + StayDates.ToWire(followUp), ct2).ConfigureAwait(false);
            await tx.PublishAsync(GuestServiceEvents.ComplaintResolved, c.ComplaintId, new { complaintId = c.ComplaintId, followUpOn = StayDates.ToWire(followUp) }, ct2).ConfigureAwait(false);
            return ToResult(u, version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ComplaintResult>> RecordFollowUpAsync(CommandContext ctx, RecordComplaintFollowUpRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var c = await LockComplaintAsync(tx, ctx, req.ComplaintId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            if (c.Status != GuestServiceVocab.Resolved) throw Invalid("Follow-up is recorded after the complaint is resolved.");
            var outcome = Text(req.Outcome, 3, 300, "Record what the guest said.", "Keep the outcome under 300 characters.");
            if (req.Satisfied && c.Severity is "High" or "Critical")
                await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Approve, ct: ct2).ConfigureAwait(false);
            var now = Now;
            var u = c with { FollowUpOutcome = outcome, FollowUpSatisfied = req.Satisfied, FollowUpUtc = now, FollowUpBy = ctx.PrincipalId };
            u = req.Satisfied ? u with { Status = GuestServiceVocab.Closed, ClosedUtc = now } : u with { Status = GuestServiceVocab.Investigating, ResolvedUtc = null };
            var version = await store.UpdateComplaintAsync(tx, u, c.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "complaint", c.ComplaintId, now, ctx.PrincipalId, $"Follow-up · {(req.Satisfied ? "satisfied · closed" : "not satisfied · re-opened")} · {outcome}", ct2).ConfigureAwait(false);
            await tx.AuditAsync("complaint.follow-up", "complaint:" + c.ComplaintId.ToString(CultureInfo.InvariantCulture), req.Satisfied ? "satisfied · closed" : "not satisfied · re-opened", ct2).ConfigureAwait(false);
            return ToResult(u, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Recovery discount: claim → Billing RequestAdjustment → link ──

    private sealed record RecoveryClaim(long ComplaintId, long FolioId, string FolioVersion, string Reason, long AmountPaise);

    private sealed record RecoveryRelease(long ComplaintId);

    private sealed record RecoveryLink(long ComplaintId, string Outcome, long? ApprovalId, long? ChargeId);

    private static CommandContext Phase(CommandContext c, string op) => new(c.PropertyId, c.PrincipalId, op, c.IdempotencyKey);

    public async Task<CommandResult<RecoveryResult>> OfferRecoveryDiscountAsync(CommandContext ctx, OfferRecoveryDiscountRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        if (req.AmountPaise is < 1 or > 10_000_000_000) throw HccException.Validation("Enter the discount amount.");
        var extra = (req.Reason ?? string.Empty).Trim();
        if (extra.Length > 120) throw HccException.Validation("Keep the reason under 120 characters.");

        // Phase A: claim the complaint so a second offer cannot reach Billing while this one is in flight.
        var claim = await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var c = await LockComplaintAsync(tx, ctx, req.ComplaintId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            if (c.Status == GuestServiceVocab.Closed) throw Invalid("This complaint is closed.");
            if (c.StayId is not { } stayId) throw Invalid("Link the complaint to a stay before offering a folio discount.");
            var current = c.RecoveryStatus;
            if (!GuestRecoveryRules.CanOffer(current))
                throw Invalid("A recovery discount is already " + (current is GuestRecoveryRules.Posted or GuestRecoveryRules.Approved ? "posted" : "awaiting approval") + " for this complaint.");
            var stay = await store.StayAsync(tx, ctx.PropertyId, stayId, ct2).ConfigureAwait(false) ?? throw NotFound("Stay not found.");
            var folio = await billingStore.ReadFolioAsync(stay.FolioId, ct2).ConfigureAwait(false) ?? throw NotFound("Folio not found.");
            var reason = "Service recovery · CMP-" + c.ComplaintId.ToString(CultureInfo.InvariantCulture) + (extra.Length > 0 ? " · " + extra : string.Empty);
            var now = Now;
            var u = c with
            {
                RecoveryState = "Requesting", RecoveryAmountPaise = req.AmountPaise, RecoveryReason = reason, RecoveryFolioId = stay.FolioId,
                RecoveryApprovalId = null, RecoveryChargeId = null, RecoveryUtc = now, RecoveryBy = ctx.PrincipalId, ApprovalStatus = null,
            };
            await store.UpdateComplaintAsync(tx, u, c.Version, ct2).ConfigureAwait(false);
            await tx.AuditAsync("complaint.recovery.claim", "complaint:" + c.ComplaintId.ToString(CultureInfo.InvariantCulture), $"{req.AmountPaise} paise on folio {stay.FolioId}", ct2).ConfigureAwait(false);
            return new RecoveryClaim(c.ComplaintId, stay.FolioId, folio.Header.Version, reason, req.AmountPaise);
        }, ct).ConfigureAwait(false);

        // Phase B: Billing decides (posted within the caller's limit, otherwise an approval request). Nothing else posts money.
        var caller = new Caller(ctx.PropertyId, ctx.PrincipalId);
        AdjustmentResult adj;
        try
        {
            adj = (await billing.RequestAdjustmentAsync(caller, ctx.IdempotencyKey,
                new RequestAdjustmentRequest("discount", claim.Value.FolioId, null, claim.Value.AmountPaise, claim.Value.Reason, claim.Value.FolioVersion), ct).ConfigureAwait(false)).Value;
        }
        catch (HccException)
        {
            // Billing refused (limit, closed day, validation, stale folio): release the claim so the complaint can be retried.
            await runner.RunAsync(Phase(ctx, "ReleaseRecoveryClaim"), new RecoveryRelease(claim.Value.ComplaintId), async (tx, ct2) =>
            {
                var c = await store.LockComplaintAsync(tx, ctx.PropertyId, claim.Value.ComplaintId, ct2).ConfigureAwait(false) ?? throw NotFound("Complaint not found.");
                if (c.RecoveryState == "Requesting" && c.RecoveryBy == ctx.PrincipalId)
                {
                    await store.UpdateComplaintAsync(tx, c with
                    {
                        RecoveryState = null, RecoveryAmountPaise = null, RecoveryReason = null, RecoveryFolioId = null, RecoveryApprovalId = null,
                        RecoveryChargeId = null, RecoveryUtc = null, RecoveryBy = null, ApprovalStatus = null,
                    }, c.Version, ct2).ConfigureAwait(false);
                }

                await tx.AuditAsync("complaint.recovery.release", "complaint:" + c.ComplaintId.ToString(CultureInfo.InvariantCulture), "Billing refused the discount", ct2).ConfigureAwait(false);

                return new RecoveryRelease(c.ComplaintId);
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // Phase C: store the link to the posted charge or the pending approval.
        var link = new RecoveryLink(claim.Value.ComplaintId, adj.Outcome, adj.Outcome == "PendingApproval" ? adj.ApprovalId : null, adj.Outcome == "Posted" ? adj.ChargeId : null);
        return await runner.RunAsync(Phase(ctx, "LinkRecoveryDiscount"), link, async (tx, ct2) =>
        {
            var c = await store.LockComplaintAsync(tx, ctx.PropertyId, link.ComplaintId, ct2).ConfigureAwait(false) ?? throw NotFound("Complaint not found.");
            if (c.RecoveryState != "Requesting") throw Invalid("The recovery request is no longer open.");
            var now = Now;
            var u = c with { RecoveryState = "Recorded", RecoveryApprovalId = link.ApprovalId, RecoveryChargeId = link.ChargeId, RecoveryUtc = now, ApprovalStatus = link.ApprovalId is null ? null : "Pending" };
            var version = await store.UpdateComplaintAsync(tx, u, c.Version, ct2).ConfigureAwait(false);
            var money = (claim.Value.AmountPaise / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            var text = link.Outcome == "Posted" ? "posted to folio" : "approval requested #" + link.ApprovalId?.ToString(CultureInfo.InvariantCulture);
            await store.AppendLogAsync(tx, "complaint", c.ComplaintId, now, ctx.PrincipalId, $"Recovery discount ₹{money} · {text}", ct2).ConfigureAwait(false);
            await tx.AuditAsync("complaint.recovery.link", "complaint:" + c.ComplaintId.ToString(CultureInfo.InvariantCulture), $"{link.Outcome} · {claim.Value.AmountPaise} paise", ct2).ConfigureAwait(false);
            await tx.PublishAsync(GuestServiceEvents.RecoveryOffered, c.ComplaintId, new { complaintId = c.ComplaintId, outcome = link.Outcome, amountPaise = claim.Value.AmountPaise }, ct2).ConfigureAwait(false);
            return new RecoveryResult(c.ComplaintId, link.Outcome, link.ApprovalId, link.ChargeId, claim.Value.AmountPaise, u.RecoveryStatus, StaysVersion.ToWire(version));
        }, ct).ConfigureAwait(false);
    }

    // ── Lost & found ──

    private static LostItemResult ToResult(ItemRecord i, byte[] version, GuestServiceSettings s) =>
        new(i.ItemId, i.Status, i.Valuable, GuestServiceRules.DisposeOn(i.FoundDate, i.Valuable, s), StaysVersion.ToWire(version));

    private async Task<ItemRecord> LockItemAsync(ICommandTransaction tx, CommandContext c, long id, string expectedVersion, CancellationToken ct)
    {
        var expected = StaysVersion.Parse(expectedVersion);
        var rec = await store.LockItemAsync(tx, c.PropertyId, id, ct).ConfigureAwait(false) ?? throw NotFound("Lost & found item not found.");
        StaysVersion.EnsureMatches(rec.Version, expected, "This item");
        return rec;
    }

    public async Task<CommandResult<LostItemResult>> LogItemAsync(CommandContext ctx, LogLostItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var wide = await WideAsync(ctx, GuestServicePermissions.Manage, ct).ConfigureAwait(false);
        if (!wide && !await AnyScopeAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.HousekeepingWork, ct).ConfigureAwait(false)
            && !await AnyScopeAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.MaintenanceWork, ct).ConfigureAwait(false))
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var description = Text(req.Description, 3, 160, "Describe the item.", "Keep the description under 160 characters.");
            if (!GuestServiceVocab.ItemCategories.Contains(req.Category ?? string.Empty)) throw HccException.Validation("Choose a category.");
            var foundWhere = Text(req.FoundWhere, 2, 120, "Enter where the item was found.", "Keep the place under 120 characters.");
            if (!GuestServiceVocab.Storage.Contains(req.Storage ?? string.Empty)) throw HccException.Validation("Choose the storage location.");
            var valuable = GuestServiceRules.IsValuable(req.Category!);
            if (valuable && req.Storage != GuestServiceVocab.Safe) throw HccException.Validation($"Valuables ({req.Category}) must be stored in the {GuestServiceVocab.Safe}.");
            var props = await store.PropertyAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            int? roomId = null;
            long? stayId = null;
            if (!string.IsNullOrWhiteSpace(req.RoomNo))
            {
                var room = await store.RoomByNoAsync(tx, ctx.PropertyId, req.RoomNo.Trim(), ct2).ConfigureAwait(false) ?? throw NotFound("Room not found.");
                roomId = room.RoomId;
                stayId = await store.LastStayInRoomAsync(tx, room.RoomId, props.BusinessDate, ct2).ConfigureAwait(false);
            }

            var foundBy = wide && req.FoundBy is { } f && f > 0 ? f : ctx.PrincipalId;
            var now = Now;
            var id = await store.InsertItemAsync(tx, new NewItem(ctx.PropertyId, description, req.Category!, valuable, foundWhere, roomId, stayId, now, props.BusinessDate, foundBy, req.Storage!), ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "item", id, now, ctx.PrincipalId, "Logged · stored in " + req.Storage, ct2).ConfigureAwait(false);
            await tx.AuditAsync("lost-item.log", "item:" + id.ToString(CultureInfo.InvariantCulture), $"{req.Category} · {req.Storage}", ct2).ConfigureAwait(false);
            await tx.PublishAsync(GuestServiceEvents.LostItemLogged, id, new { itemId = id, valuable }, ct2).ConfigureAwait(false);
            var rec = (await store.LockItemAsync(tx, ctx.PropertyId, id, ct2).ConfigureAwait(false))!;
            var (settings, _) = await store.LockSettingsAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            return ToResult(rec, rec.Version, settings);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LostItemResult>> ClaimItemAsync(CommandContext ctx, ClaimLostItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var i = await LockItemAsync(tx, ctx, req.ItemId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            if (i.Status != GuestServiceVocab.Stored) throw Invalid("Only a stored item can be claimed.");
            var name = Text(req.Name, 2, 100, "Enter the claimant’s name.", "Keep the name under 100 characters.");
            var phone = GuestServiceRules.NormalizePhone(req.Phone) ?? throw HccException.Validation("Enter a 10-digit phone number.");
            if (!GuestServiceVocab.IdTypes.Contains(req.IdType ?? string.Empty)) throw HccException.Validation("Choose the ID document checked.");
            if (!GuestServiceRules.IdNoteOk(req.IdNote) || req.IdNote!.Trim().Length > 60)
                throw HccException.Validation("Record an ID check note with only the last 4 characters — never the full ID number.");
            var match = Text(req.MatchNote, 3, 200, "Note how the claimant described the item.", "Keep the note under 200 characters.");
            var now = Now;
            var u = i with { Status = GuestServiceVocab.Claimed, ClaimName = name, ClaimPhone = phone, ClaimIdType = req.IdType, ClaimIdNote = req.IdNote!.Trim(), ClaimMatchNote = match, ClaimUtc = now, ClaimBy = ctx.PrincipalId };
            var version = await store.UpdateItemAsync(tx, u, i.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "item", i.ItemId, now, ctx.PrincipalId, "Claimed · ID checked", ct2).ConfigureAwait(false);
            await tx.AuditAsync("lost-item.claim", "item:" + i.ItemId.ToString(CultureInfo.InvariantCulture), "claimed · ID checked", ct2).ConfigureAwait(false);
            var (settings, _) = await store.LockSettingsAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            return ToResult(u, version, settings);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LostItemResult>> HandOverItemAsync(CommandContext ctx, HandOverLostItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var i = await LockItemAsync(tx, ctx, req.ItemId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            if (i.Status != GuestServiceVocab.Claimed) throw Invalid("Record and verify the claim before handover.");
            var to = Text(string.IsNullOrWhiteSpace(req.To) ? i.ClaimName : req.To, 2, 100, "Enter who received the item.", "Keep the name under 100 characters.");
            var note = (req.Note ?? string.Empty).Trim();
            if (note.Length > 200) throw HccException.Validation("Keep the note under 200 characters.");
            var now = Now;
            var u = i with { Status = GuestServiceVocab.HandedOver, HandoverTo = to, HandoverNote = note, HandoverUtc = now, HandoverBy = ctx.PrincipalId };
            var version = await store.UpdateItemAsync(tx, u, i.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "item", i.ItemId, now, ctx.PrincipalId, "Handed over to " + to, ct2).ConfigureAwait(false);
            await tx.AuditAsync("lost-item.handover", "item:" + i.ItemId.ToString(CultureInfo.InvariantCulture), "handed over", ct2).ConfigureAwait(false);
            var (settings, _) = await store.LockSettingsAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            return ToResult(u, version, settings);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LostItemResult>> DisposeItemAsync(CommandContext ctx, DisposeLostItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            // P6-GUARD
            var i = await LockItemAsync(tx, ctx, req.ItemId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            if (i.Status != GuestServiceVocab.Stored) throw Invalid("Only an unclaimed stored item can be disposed.");
            if (i.Valuable) await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Approve, ct: ct2).ConfigureAwait(false);
            var props = await store.PropertyAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            var (settings, _) = await store.LockSettingsAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            var on = GuestServiceRules.DisposeOn(i.FoundDate, i.Valuable, settings);
            if (props.BusinessDate < on) throw Invalid($"Retention period not over — this item can be disposed from {StayDates.ToWire(on)}.");
            if (!GuestServiceVocab.Disposal.Contains(req.Method ?? string.Empty)) throw HccException.Validation("Choose the disposal method.");
            if (i.Valuable && req.Method == "Destroyed") throw HccException.Validation("Valuables are donated or handed to police, not destroyed.");
            var note = (req.Note ?? string.Empty).Trim();
            if (note.Length > 200) throw HccException.Validation("Keep the note under 200 characters.");
            var now = Now;
            var u = i with { Status = GuestServiceVocab.Disposed, DisposalMethod = req.Method, DisposalNote = note, DisposalUtc = now, DisposalBy = ctx.PrincipalId };
            var version = await store.UpdateItemAsync(tx, u, i.Version, ct2).ConfigureAwait(false);
            await store.AppendLogAsync(tx, "item", i.ItemId, now, ctx.PrincipalId, "Disposed · " + req.Method, ct2).ConfigureAwait(false);
            await tx.AuditAsync("lost-item.dispose", "item:" + i.ItemId.ToString(CultureInfo.InvariantCulture), $"{req.Method} · {(i.Valuable ? "valuable" : "ordinary")}", ct2).ConfigureAwait(false);
            return ToResult(u, version, settings);
        }, ct).ConfigureAwait(false);
    }

    // ── Settings ──

    public async Task<CommandResult<GuestServiceSettingsView>> SaveSettingsAsync(CommandContext ctx, SaveGuestServiceSettingsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestServicePermissions.Approve, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, ct2) =>
        {
            var (cur, version) = await store.LockSettingsAsync(tx, ctx.PropertyId, ct2).ConfigureAwait(false);
            if (version is not null) StaysVersion.EnsureMatches(version, StaysVersion.Parse(req.ExpectedVersion), "The guest-service settings");
            var sla = new Dictionary<string, int>(cur.Sla, StringComparer.Ordinal);
            if (req.SlaMinutes is not null)
                foreach (var (type, minutes) in req.SlaMinutes)
                {
                    if (!GuestServiceVocab.RequestTypes.Contains(type)) throw HccException.Validation($"Unknown request type {type}.");
                    if (minutes is < 5 or > 480) throw HccException.Validation($"SLA for {type} must be 5–480 minutes.");
                    sla[type] = minutes;
                }

            var retention = req.RetentionDays ?? cur.RetentionDays;
            var valuable = req.ValuableRetentionDays ?? cur.ValuableRetentionDays;
            if (retention is < 30 or > 365) throw HccException.Validation("Retention must be 30–365 days.");
            if (valuable < retention || valuable > 730) throw HccException.Validation("Valuables retention must be at least the normal retention (max 730 days).");
            var next = new GuestServiceSettings(sla, retention, valuable);
            var v = await store.SaveSettingsAsync(tx, ctx.PropertyId, next, version, ctx.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("guest-services.settings", "property:" + ctx.PropertyId.ToString(CultureInfo.InvariantCulture), $"retention {retention}/{valuable} days", ct2).ConfigureAwait(false);
            return new GuestServiceSettingsView(sla, retention, valuable, StaysVersion.ToWire(v));
        }, ct).ConfigureAwait(false);
    }
}
