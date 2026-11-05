using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Stays;
using Hcc.Domain.Tax;

namespace Hcc.Application.Stays;

/// <summary>POST /api/v1/stays/{id}/rate-change (LF-CONTRACT §2). Null rate / people / children = unchanged.</summary>
public sealed record ChangeStayRateRequest(long StayId, long? RatePaise, int? People, int? Children, string Scope, string Reason);

public sealed record RepostedNight(DateOnly Night, long ReversalChargeId, long NewChargeId);

/// <summary><see cref="Status"/> = Applied (direct, or executed from an approval) | PendingApproval.</summary>
public sealed record StayRateChangeResult(string Status, long? ChangeId, long? ApprovalId, DateOnly? EffectiveFrom, IReadOnlyList<RepostedNight> Reposted);

/// <summary>The caller's PERM-RATE-001 grant: 'A' (Owner) applies anything; 'L' (Manager) within <see cref="LimitBps"/> below the tariff.</summary>
public sealed record RateAuthority(bool Owner, bool Manager, int LimitBps);

/// <summary>The meta of a pending 'rate-change' approval (gst.stay_rate_request, migration 4690).</summary>
public sealed record StayRateRequestMeta(long ApprovalId, long StayId, string Scope, long NewRatePaise, int NewPeople, int NewChildren, long OldRatePaise, int OldPeople, int OldChildren);

/// <summary>One applied change (gst.stay_rate_change, 4675). CTL-01 (4920): the tariff and the agreed rate it was judged against, the agreed rate after it
/// (an Owner-set rate becomes the new agreed rate), the threshold in force and whether the change went beyond it (the owner digest lists those).</summary>
public sealed record StayRateChangeRow(int PropertyId, long StayId, long ReservationId, int RoomId, DateOnly BusinessDate, DateTime Utc, int ChangedBy, long? ApprovalId,
    int? ApprovedBy, string Scope, string Reason, long OldRate, long NewRate, int OldPeople, int NewPeople, int OldChildren, int NewChildren, DateOnly EffectiveFrom, string RepostedJson,
    long? TariffPaise = null, long? AgreedRatePaise = null, long? AgreedAfterPaise = null, int? ThresholdBps = null, bool OverThreshold = false);

/// <summary>CTL-01: what a change is judged against — the tariff, the stay's agreed rate, the Owner's threshold — and whether the Owner set it.</summary>
public sealed record RateControl(Paise Tariff, Paise Agreed, int ThresholdBps, bool OwnerSet);

/// <summary>Persistence of the rate change (SQL adapter Infrastructure/Stays/SqlStayRateStore). Everything inside the command transaction.</summary>
public interface IStayRateStore
{
    Task<RateAuthority> AuthorityAsync(ICommandTransaction tx, int principalId, CancellationToken ct);

    /// <summary>The room type's tariff on the date (latest inv.room_type_tariff on or before it, else the rack rate).</summary>
    Task<long> TariffAsync(ICommandTransaction tx, int roomTypeId, DateOnly date, CancellationToken ct);

    /// <summary>Max occupancy of the room type and whether it has an active extra-person (occupancy) charge.</summary>
    Task<(int Capacity, bool ExtraPersonCharge)> RoomTypeAsync(ICommandTransaction tx, int roomTypeId, CancellationToken ct);

    Task InsertRequestAsync(ICommandTransaction tx, StayRateRequestMeta meta, CancellationToken ct);

    Task<StayRateRequestMeta?> RequestAsync(ICommandTransaction tx, long approvalId, CancellationToken ct);

    /// <summary>Sets the booking's nightly rate, adults and children (inv.reservation).</summary>
    Task SetReservationAsync(ICommandTransaction tx, long reservationId, long ratePaise, int adults, int children, CancellationToken ct);

    Task<long> InsertChangeAsync(ICommandTransaction tx, StayRateChangeRow row, CancellationToken ct);

    /// <summary>CTL-01: the agreed rate recorded after the stay's latest change (null for rows before 4920 or no change) and the rate before its first change.</summary>
    Task<(long? LatestAgreedAfter, long? FirstOldRate)> AgreedHistoryAsync(ICommandTransaction tx, long stayId, CancellationToken ct);
}

/// <summary>
/// LF A1 — rate or guest-count change during a stay (legacy "Rate And Pax Change", F9). <c>ChangeStayRate</c> (PERM-STY-010): the Owner, and a
/// Manager within the PERM-RATE-001 limit below the tariff, apply directly; anyone else (or a raise of guests on a room type with an extra-person
/// charge) raises a 'rate-change' approval that <see cref="DecideAsync"/> executes (Owner anything; Manager within the same limit; never the
/// requester). Applying: posted room nights of open days from the effective night are re-posted — a reversal row (kind 'correction', dated the
/// business date, migration 4686 marks the original) + a new room line at the new price with that night's GST slab (DEC-069); nights on a
/// closed business date are never touched (DEC-077: every night before the business date is closed here — a reopened day is corrected with A2).
/// The booking's rate / guests and the un-posted nights' price snapshots change, a gst.stay_rate_change row is written; outbox
/// <c>StayRateChanged</c> (no accounting entry of its own: the re-posted lines publish <c>ChargePosted</c>, every one balanced).
/// </summary>
public sealed class StayRateCommands(ICommandRunner runner, IAccessPolicy policy, IStayRateStore rates, IStayChangeStore stays, IBillingStore billing,
    IGstRateResolver gst, IStayNightPricer pricer, TimeProvider clock, IGstChargeSnapshots? gstSnapshots = null, IBusinessDayGuard? days = null,
    Hcc.Application.Configuration.ISettingsReader? settings = null)
{
    public const string OpChangeStayRate = "ChangeStayRate";
    public const string Permission = "PERM-STY-010";
    public const string EventStayRateChanged = "StayRateChanged";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<StayRateChangeResult>> ChangeAsync(CommandContext ctx, ChangeStayRateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.StayId <= 0) throw HccException.Validation("Select a stay.");
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, Permission, null, null, ct).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            // Request-only holder (Reception, 4676 'R'): continue; the authority check below routes it to an approval.
        }

        var reason = (req.Reason ?? "").Trim();
        if (reason.Length > 200) throw HccException.Validation("Keep the reason under 200 characters.");

        return await runner.RunAsync(ctx with { Operation = OpChangeStayRate }, req, async (tx, c) =>
        {
            var (s, bd) = await LoadAsync(tx, ctx.PropertyId, req.StayId, c).ConfigureAwait(false);
            var oldRate = s.NightlyRatePaise ?? await stays.LastNightRateAsync(tx, s.ReservationId, c).ConfigureAwait(false) ?? 0;
            var input = new StayRateChangeInput(new Paise(req.RatePaise ?? oldRate), req.People ?? s.Adults, req.Children ?? s.Children, req.Scope ?? "", reason);
            var (capacity, extra) = await rates.RoomTypeAsync(tx, s.RoomTypeId, c).ConfigureAwait(false);
            var refusal = StayRateRules.Refusal(s.StayStatus == "InHouse", bd, s.Departure, new Paise(oldRate), s.Adults, s.Children, input, capacity);
            if (refusal is not null) throw HccException.Validation(refusal);

            var auth = await rates.AuthorityAsync(tx, ctx.PrincipalId, c).ConfigureAwait(false);
            var tariff = new Paise(await rates.TariffAsync(tx, s.RoomTypeId, bd, c).ConfigureAwait(false));
            var guestsRise = input.People + input.Children > s.Adults + s.Children;
            var threshold = await ThresholdAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var agreed = await AgreedAsync(tx, s.StayId, oldRate, c).ConfigureAwait(false);
            if (StayRateRules.CanApplyDirectly(auth.Owner, auth.Manager, tariff, auth.LimitBps, agreed, threshold, input.Rate, guestsRise, extra, new Paise(oldRate)))
                return await ApplyAsync(tx, ctx.PropertyId, ctx.PrincipalId, s, bd, oldRate, input, null, null, new RateControl(tariff, agreed, threshold, auth.Owner), c).ConfigureAwait(false);

            var f = await billing.LockFolioAsync(tx, s.FolioId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
            var now = Now;
            var pending = (await billing.ApprovalsForFolioAsync(tx, s.FolioId, c).ConfigureAwait(false))
                .Any(a => a.Action == AdjustmentAction.RateChange && a.Status == ApprovalStatus.Pending && !ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now));
            if (pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A rate-change request is already pending for this stay.");
            var amount = new Paise(Math.Max(1, input.Rate.Value));
            var (approvalId, _) = await billing.InsertApprovalAsync(tx, new NewApproval(ctx.PropertyId, AdjustmentAction.RateChange, s.FolioId, null, "folio", s.FolioId,
                amount, f.Version, null, null, reason, ctx.PrincipalId, now, now + ApprovalRules.TimeToLive), c).ConfigureAwait(false);
            await rates.InsertRequestAsync(tx, new StayRateRequestMeta(approvalId, s.StayId, input.Scope, input.Rate.Value, input.People, input.Children, oldRate, s.Adults, s.Children), c).ConfigureAwait(false);
            await tx.AuditAsync("STAY_RATE_CHANGE_REQUESTED", Target(s.StayId),
                $"rate {oldRate} → {input.Rate.Value} paise · guests {s.Adults}+{s.Children} → {input.People}+{input.Children} · {input.Scope} · {reason} · approval {approvalId}"
                + Judged(tariff, agreed, threshold, input.Rate, input.Rate.Value != oldRate), c).ConfigureAwait(false);
            await tx.PublishAsync("ApprovalRequested", approvalId, new { approvalId, action = "rate-change", folioId = s.FolioId, amountPaise = amount.Value }, c).ConfigureAwait(false);
            return new StayRateChangeResult("PendingApproval", null, approvalId, null, []);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Decides a pending 'rate-change' approval inside the DecideApproval transaction (<see cref="ILfApprovalDecisions"/>).</summary>
    public async Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow peek, bool approve, string reason, string expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(peek);
        var f = await billing.LockFolioAsync(tx, peek.FolioId!.Value, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        var a = (await billing.LockApprovalAsync(tx, peek.ApprovalId, ct).ConfigureAwait(false))!;
        BillingService.EnsureVersion(a.Version, expectedVersion, "The approval request");
        var now = Now;
        var expired = ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now);
        if (a.Status != ApprovalStatus.Pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request is no longer pending.");
        if (a.RequestedBy == caller.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot decide your own request (PERM-SOD-001).");
        var meta = await rates.RequestAsync(tx, a.ApprovalId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "The rate-change request is missing.");

        if (!approve || expired)
        {
            if (approve) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request expired (24 hours). Dismiss it and request again.");
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ApprovalDecide, ct: ct).ConfigureAwait(false);
            var status = expired ? ApprovalStatus.Expired : ApprovalStatus.Rejected;
            var v = await billing.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, new ApprovalChange(status, caller.PrincipalId, now, reason, null, null, null), ct).ConfigureAwait(false);
            await tx.AuditAsync("APPROVAL_DECIDED", "approval-" + N(a.ApprovalId), $"rate-change · {status}" + (reason.Length > 0 ? " · " + reason : ""), ct).ConfigureAwait(false);
            return new ApprovalResult(a.ApprovalId, "rate-change", status.ToString(), a.Amount.Value, v, null, f.Version, null);
        }

        var (s, bd) = await LoadAsync(tx, caller.PropertyId, meta.StayId, ct).ConfigureAwait(false);
        var auth = await rates.AuthorityAsync(tx, caller.PrincipalId, ct).ConfigureAwait(false);
        var tariff = new Paise(await rates.TariffAsync(tx, s.RoomTypeId, bd, ct).ConfigureAwait(false));
        var current = s.NightlyRatePaise ?? await stays.LastNightRateAsync(tx, s.ReservationId, ct).ConfigureAwait(false) ?? 0;
        var threshold = await ThresholdAsync(tx, caller.PropertyId, ct).ConfigureAwait(false);
        var agreed = await AgreedAsync(tx, s.StayId, current, ct).ConfigureAwait(false);
        var newRate = new Paise(meta.NewRatePaise);
        if (!StayRateRules.CanApprove(auth.Owner, auth.Manager, tariff, auth.LimitBps, agreed, threshold, newRate, new Paise(current)))
            throw new HccException(HccErrorCode.FORBIDDEN, StayRateRules.CanApprove(auth.Owner, auth.Manager, tariff, auth.LimitBps, newRate)
                ? StayRateRules.OwnerDecidesText(agreed, threshold)
                : "This rate is beyond your limit below the tariff: the Owner decides it.");
        if (current != meta.OldRatePaise || s.Adults != meta.OldPeople || s.Children != meta.OldChildren)
            throw new HccException(HccErrorCode.STALE_VERSION, "The stay changed after this request. Reject it and request again.");
        var (capacity, _) = await rates.RoomTypeAsync(tx, s.RoomTypeId, ct).ConfigureAwait(false);
        var input = new StayRateChangeInput(new Paise(meta.NewRatePaise), meta.NewPeople, meta.NewChildren, meta.Scope, a.Reason);
        var refusal = StayRateRules.Refusal(s.StayStatus == "InHouse", bd, s.Departure, new Paise(current), s.Adults, s.Children, input, capacity);
        if (refusal is not null) throw HccException.Validation(refusal);

        var applied = await ApplyAsync(tx, caller.PropertyId, a.RequestedBy, s, bd, current, input, a.ApprovalId, caller.PrincipalId, new RateControl(tariff, agreed, threshold, auth.Owner), ct).ConfigureAwait(false);
        var newVersion = await billing.UpdateApprovalAsync(tx, a.ApprovalId, a.Version,
            new ApprovalChange(ApprovalStatus.Executed, caller.PrincipalId, now, reason, caller.PrincipalId, now, applied.ChangeId), ct).ConfigureAwait(false);
        await tx.AuditAsync("APPROVAL_DECIDED", "approval-" + N(a.ApprovalId), $"rate-change · approved · change {applied.ChangeId}", ct).ConfigureAwait(false);
        await tx.PublishAsync("ApprovalDecided", a.ApprovalId, new { approvalId = a.ApprovalId, action = "rate-change", status = "Executed", folioId = s.FolioId }, ct).ConfigureAwait(false);
        var fv = (await billing.LockFolioAsync(tx, s.FolioId, ct).ConfigureAwait(false))!.Version;
        return new ApprovalResult(a.ApprovalId, "rate-change", nameof(ApprovalStatus.Executed), a.Amount.Value, newVersion, applied.ChangeId, fv, null);
    }

    private async Task<StayRateChangeResult> ApplyAsync(ICommandTransaction tx, int propertyId, int changedBy, StayChangeContext s, DateOnly bd, long oldRate,
        StayRateChangeInput input, long? approvalId, int? approvedBy, RateControl rc, CancellationToken c)
    {
        var rateMoves = input.Rate.Value != oldRate;
        var over = rateMoves && StayRateRules.OverThreshold(rc.Agreed, rc.ThresholdBps, input.Rate);
        var agreedAfter = rc.OwnerSet && rateMoves ? input.Rate : rc.Agreed;
        bool Closed(DateOnly d) => d < bd;
        var lines = await billing.LinesAsync(tx, s.FolioId, c).ConfigureAwait(false);
        var reversed = lines.Where(l => l.ReversesChargeId is not null).Select(l => l.ReversesChargeId!.Value).ToHashSet();
        var nights = lines.Where(l => l.Kind == ChargeKind.Room && l.ReversesChargeId is null)
            .Select(l => new StayRoomNight(l.ChargeId, l.ServiceDate, l.Amount, reversed.Contains(l.ChargeId))).ToList();
        var from = StayRateRules.EffectiveFrom(input.Scope, bd, s.Arrival, s.Departure, Closed);
        var plan = StayRateRules.RepostPlan(nights, input.Scope, bd, s.Arrival, s.Departure, Closed);
        var now = Now;
        var reposted = new List<RepostedNight>();
        foreach (var n in plan)
        {
            var original = lines.Single(l => l.ChargeId == n.ChargeId);
            var reversal = new NewCharge(s.FolioId, ChargeKind.Correction, bd, Clip($"Reversal · rate change · {original.Label}"), -original.Amount, original.TaxBps,
                -original.Tax, original.ChargeId, approvalId, changedBy, now);
            var reversalId = await billing.InsertChargeAsync(tx, reversal, c).ConfigureAwait(false);
            await ReversalGstAsync(tx, original, reversalId, reversal.Tax, c).ConfigureAwait(false);
            var g = await gst.RoomNightAsync(tx, propertyId, n.Night, input.Rate, c).ConfigureAwait(false);
            var line = new NewCharge(s.FolioId, ChargeKind.Room, n.Night, FolioStatement.RoomNightLabel(n.Night), input.Rate, g.Bps, GstMath.LineTax(input.Rate, g.Bps),
                null, approvalId, changedBy, now);
            var lineId = await billing.InsertChargeAsync(tx, line, c).ConfigureAwait(false);
            if (gstSnapshots is not null)
            {
                var split = GstMath.Split(line.Tax);
                await gstSnapshots.RecordAsync(tx, new GstChargeSnapshot(lineId, g.Category, g.Bps, g.Itc, line.Tax, split.Cgst, split.Sgst, g.Rule), c).ConfigureAwait(false);
            }

            await tx.PublishAsync("ChargePosted", s.FolioId, new { folioId = s.FolioId, chargeId = reversalId, kind = "correction", serviceDate = bd, amountPaise = reversal.Amount.Value, taxPaise = reversal.Tax.Value }, c).ConfigureAwait(false);
            await tx.PublishAsync("ChargePosted", s.FolioId, new { folioId = s.FolioId, chargeId = lineId, kind = "room", serviceDate = n.Night, amountPaise = line.Amount.Value, taxPaise = line.Tax.Value }, c).ConfigureAwait(false);
            reposted.Add(new RepostedNight(n.Night, reversalId, lineId));
        }

        await rates.SetReservationAsync(tx, s.ReservationId, input.Rate.Value, input.People, input.Children, c).ConfigureAwait(false);
        var priceFrom = from < bd ? bd : from;
        if (priceFrom < s.Departure)
            await stays.WriteNightPricesAsync(tx, s.ReservationId, await pricer.PriceAsync(propertyId, priceFrom, s.Departure, input.Rate.Value, c).ConfigureAwait(false), c).ConfigureAwait(false);
        if (reposted.Count > 0) await billing.TouchFolioAsync(tx, s.FolioId, c).ConfigureAwait(false);

        var json = JsonSerializer.Serialize(reposted.Select(r => new { reversal = N(r.ReversalChargeId), line = N(r.NewChargeId), night = r.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));
        var changeId = await rates.InsertChangeAsync(tx, new StayRateChangeRow(propertyId, s.StayId, s.ReservationId, s.CurrentRoomId, bd, now, changedBy, approvalId, approvedBy,
            input.Scope, input.Reason, oldRate, input.Rate.Value, s.Adults, input.People, s.Children, input.Children, from, json,
            rc.Tariff.Value, rc.Agreed.Value, agreedAfter.Value, rc.ThresholdBps, over), c).ConfigureAwait(false);
        await tx.AuditAsync("STAY_RATE_CHANGE", Target(s.StayId),
            $"rate {oldRate} → {input.Rate.Value} paise · guests {s.Adults}+{s.Children} → {input.People}+{input.Children} · {input.Scope} from {from:yyyy-MM-dd} · re-posted {reposted.Count} night(s) · {input.Reason}"
            + (approvalId is { } ap ? $" · approval {ap}" : "") + Judged(rc.Tariff, rc.Agreed, rc.ThresholdBps, input.Rate, rateMoves), c).ConfigureAwait(false);
        if (over)
            // CTL-01: a separate audit line the owner review can find by action (who changed, who approved, how far below the agreed rate).
            await tx.AuditAsync("RATE_REDUCTION_OVER_THRESHOLD", Target(s.StayId),
                $"change {N(changeId)} · agreed {rc.Agreed.Value} → {input.Rate.Value} paise · {StayRateRules.ReductionBps(rc.Agreed, input.Rate)} bps below the agreed rate (threshold {rc.ThresholdBps} bps)"
                + $" · changed by {changedBy}" + (approvedBy is { } ab ? $" · approved by {ab}" : " · applied directly") + $" · {input.Reason}", c).ConfigureAwait(false);
        await tx.PublishAsync(EventStayRateChanged, s.StayId, new { changeId, stayId = s.StayId, reservationId = s.ReservationId, oldRatePaise = oldRate, newRatePaise = input.Rate.Value,
            effectiveFrom = from, reposted = reposted.Count, approvalId, agreedRatePaise = rc.Agreed.Value, overThreshold = over }, c).ConfigureAwait(false);
        return new StayRateChangeResult("Applied", changeId, approvalId, from, reposted);
    }

    private async Task ReversalGstAsync(ICommandTransaction tx, PostedLine original, long reversalId, Paise tax, CancellationToken c)
    {
        if (gstSnapshots is null) return;
        var o = await gstSnapshots.FindAsync(tx, original.ChargeId, c).ConfigureAwait(false);
        var split = GstMath.Split(tax);
        await gstSnapshots.RecordAsync(tx, new GstChargeSnapshot(reversalId, o?.Category ?? GstCategory.Room, original.TaxBps, o?.Itc ?? false, tax, split.Cgst, split.Sgst,
            "Reversal of charge " + N(original.ChargeId)), c).ConfigureAwait(false);
    }

    private async Task<(StayChangeContext Stay, DateOnly BusinessDate)> LoadAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken c)
    {
        var s = await stays.LockStayAsync(tx, propertyId, stayId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
        var bd = await stays.BusinessDateAsync(tx, propertyId, c).ConfigureAwait(false);
        if (days is not null) await days.EnsureOpenAsync(tx, propertyId, bd, c).ConfigureAwait(false);
        return (s, bd);
    }

    /// <summary>CTL-01 threshold in force (Master configuration, Owner-only); the default 15 % when no reader is wired.</summary>
    private async Task<int> ThresholdAsync(ICommandTransaction tx, int propertyId, CancellationToken c) =>
        settings is null ? RateControlSettings.DefaultThresholdBps
            : RateControlSettings.Read(await settings.GetAsync(tx, propertyId, RateControlSettings.ThresholdKey, null, c).ConfigureAwait(false));

    private async Task<Paise> AgreedAsync(ICommandTransaction tx, long stayId, long currentRate, CancellationToken c)
    {
        var (latest, first) = await rates.AgreedHistoryAsync(tx, stayId, c).ConfigureAwait(false);
        return StayRateRules.AgreedBase(latest, first, currentRate);
    }

    /// <summary>The audit suffix: what the change was judged against (CTL-01).</summary>
    private static string Judged(Paise tariff, Paise agreed, int thresholdBps, Paise newRate, bool rateMoves) =>
        !rateMoves ? "" : $" · tariff {tariff.Value} · agreed {agreed.Value} paise · {StayRateRules.ReductionBps(agreed, newRate)} bps below agreed"
            + (StayRateRules.OverThreshold(agreed, thresholdBps, newRate) ? $" · over the {thresholdBps} bps threshold (Owner)" : "");

    private static string Clip(string s) => s.Length <= 200 ? s : s[..200];

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Target(long stayId) => "stay-" + N(stayId);
}

/// <summary>'rate-change' decisions (DecideApproval → <see cref="StayRateCommands.DecideAsync"/>).</summary>
public sealed class StayRateApprovalHandler(StayRateCommands commands) : ILfApprovalHandler
{
    public AdjustmentAction Action => AdjustmentAction.RateChange;

    public Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow approval, bool approve, string reason, string expectedVersion, CancellationToken ct)
        => commands.DecideAsync(tx, caller, approval, approve, reason, expectedVersion, ct);
}
