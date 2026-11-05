using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Housekeeping;
using Hcc.Domain.Inventory;
using Hcc.Domain.Money;
using Hcc.Domain.Stays;

namespace Hcc.Application.Stays;

/// <summary>PERM ids of the stay-change commands (RBAC matrix v1 §5): stay.move / extend, and approval.decide for a rate given up.</summary>
public static class StayChangePermissions
{
    public const string MoveExtend = "PERM-STY-002";
    public const string ApproveRate = "PERM-APR-001";
}

/// <summary>
/// Changes to an in-house stay (W3-7), ported from the prototype MOVE handler: <c>MoveRoom</c>, <c>ExtendStay</c>, <c>ShortenStay</c>,
/// <c>EarlyDeparture</c>. Each command checks PERM-STY-002 before the transaction, then runs in one <see cref="ICommandRunner"/>
/// transaction: stay + reservation locked, version checked, <see cref="IBusinessDayGuard"/> (the business date must be open), inventory changed
/// only through <see cref="IInventoryAllocator"/>, nightly price snapshots rewritten for un-posted nights only (posted nights are never
/// touched), audit + outbox in the same transaction. A rate change below the agreed tariff gives revenue up and also needs PERM-APR-001
/// for that amount (Manager within its limit, Owner). DEC-082: no early-departure fee is charged, only the nights stayed; a Manager may add a charge by hand.
/// CTL-01 (af2-fix finding 1): a new rate on a move or an extension is ALSO judged against the stay's agreed rate with the Owner's threshold
/// <c>rate.agreedReductionBps</c> (same <see cref="StayRateRules"/> helpers as ChangeStayRate; the stricter wins): beyond it only the Owner may set it
/// (there is no approval path here, so anyone else is refused with the ChangeStayRate wording). A rate that actually moves writes a
/// gst.stay_rate_change row (tariff, agreed rate, agreed rate after — only the Owner moves it — threshold, over_threshold), so the agreed baseline
/// survives the change and the owner digest lists a change beyond the threshold; RATE_REDUCTION_OVER_THRESHOLD is audited as for ChangeStayRate.
/// </summary>
public sealed class StayChangeCommands(ICommandRunner runner, IAccessPolicy policy, IStayChangeStore store, IInventoryAllocator allocator,
    IStayNightPricer pricer, TimeProvider clock, IBusinessDayGuard? days = null, IStayRateStore? rates = null,
    Hcc.Application.Configuration.ISettingsReader? settings = null)
{
    public const string OpMoveRoom = "MoveRoom";
    public const string OpExtendStay = "ExtendStay";
    public const string OpShortenStay = "ShortenStay";
    public const string OpEarlyDeparture = "EarlyDeparture";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<MoveRoomResult>> MoveRoomAsync(CommandContext ctx, MoveRoomRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.StayId <= 0) throw HccException.Validation("Select a stay.");
        if (req.ToRoomId <= 0) throw HccException.Validation("Select a room.");
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var reason = StayChangeRules.Reason(req.Reason, required: true);
        var rate = StayChangeRules.Rate(req.RateDecision, req.NewNightlyRatePaise);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StayChangePermissions.MoveExtend, null, null, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpMoveRoom }, req, async (tx, c) =>
        {
            var (s, bd) = await LoadAsync(tx, ctx, req.StayId, expected, c).ConfigureAwait(false);
            var to = await store.LockRoomAsync(tx, ctx.PropertyId, req.ToRoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            var from = await store.LockRoomAsync(tx, ctx.PropertyId, s.CurrentRoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            var blocked = await store.RoomBlockedDuringAsync(tx, to.RoomId, bd, s.Departure, c).ConfigureAwait(false);
            StayChangeRules.AssertMoveTarget(from.RoomId, to.RoomId, from.RoomTypeId, to.RoomTypeId, s.GroupBlockId, s.Adults, s.Children, to.MaxOccupancy,
                to.Hk, to.Occupied, blocked);

            var posted = await store.PostedNightsAsync(tx, s.FolioId, c).ConfigureAwait(false);
            var agreed = await AgreedRateAsync(tx, s, c).ConfigureAwait(false);
            var loss = await RateLossAsync(tx, ctx, rate, agreed, Unposted(bd, s.Departure, posted), c).ConfigureAwait(false);
            var ctl = await RateControlAsync(tx, ctx, s, rate, agreed, to.RoomTypeId, bd, c).ConfigureAwait(false);

            // Inventory: nights before the business date stay in the old room, the rest move (release + re-add through the allocator).
            var current = await store.RoomNightsAsync(tx, s.ReservationId, c).ConfigureAwait(false);
            var desired = current.Where(n => n.Night < bd).Concat(Range(to.RoomId, to.RoomTypeId, bd, s.Departure)).ToList();
            await ReplanAsync(tx, ctx.PropertyId, s.ReservationId, current, desired, c).ConfigureAwait(false);

            if (rate.Change) await RepriceAsync(tx, ctx.PropertyId, s.ReservationId, bd, s.Departure, posted, rate.NewRatePaise!.Value, c).ConfigureAwait(false);
            var version = await store.ApplyMoveAsync(tx, s, to, bd, rate.NewRatePaise, c).ConfigureAwait(false);
            await store.SetRoomHkAsync(tx, from.RoomId, RoomHk.Dirty, c).ConfigureAwait(false);
            var taskId = await store.InsertMoveTaskAsync(tx, ctx.PropertyId, from.RoomId, s.StayId, s.CurrentSegmentSeq,
                $"Guest moved to room {to.RoomNo}", bd, UtcNow, c).ConfigureAwait(false);
            await store.InsertChangeAsync(tx, ctx.PropertyId, new StayChangeLog(s.StayId, "move", bd, from.RoomId, to.RoomId, s.Departure, s.Departure,
                rate.Change ? RateDecisions.Change : RateDecisions.Keep, agreed, rate.NewRatePaise, loss, reason), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            if (ctl is not null) await RecordRateAsync(tx, ctx, s, to.RoomId, bd, agreed, rate.NewRatePaise!.Value, "Room move", reason, ctl, c).ConfigureAwait(false);

            await tx.AuditAsync("MOVE_ROOM", Target(s.StayId),
                $"reservation {s.ReservationId} · room {from.RoomNo} → {to.RoomNo} (" + (to.RoomTypeId == from.RoomTypeId ? "same type" : "type changed") + ") · " +
                (rate.Change ? $"rate {agreed} → {rate.NewRatePaise} paise (loss {loss})" : "agreed tariff preserved") + $" · {reason} · business date {StayDates.ToWire(bd)}", c).ConfigureAwait(false);
            var outboxId = await tx.PublishAsync(StayChangeEvents.StayRoomMoved, s.StayId,
                new StayRoomMovedEvent(s.StayId, s.ReservationId, ctx.PropertyId, from.RoomId, to.RoomId, StayDates.ToWire(bd)), c).ConfigureAwait(false);
            return new MoveRoomResult(s.StayId, s.ReservationId, from.RoomId, from.RoomNo, to.RoomId, to.RoomNo, RoomHk.Dirty, taskId, StayDates.ToWire(bd),
                rate.NewRatePaise ?? agreed, loss, VersionToken.ToWire(version), outboxId);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ChangeStayDatesResult>> ExtendStayAsync(CommandContext ctx, ExtendStayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.StayId <= 0) throw HccException.Validation("Select a stay.");
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var newDeparture = StayDates.Parse(req.NewDeparture, "newDeparture");
        var reason = StayChangeRules.Reason(req.Reason, required: req.RateDecision is not null && !string.Equals(req.RateDecision, RateDecisions.Keep, StringComparison.OrdinalIgnoreCase));
        var rate = StayChangeRules.Rate(req.RateDecision, req.NewNightlyRatePaise);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StayChangePermissions.MoveExtend, null, null, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpExtendStay }, req, async (tx, c) =>
        {
            var (s, bd) = await LoadAsync(tx, ctx, req.StayId, expected, c).ConfigureAwait(false);
            StayChangeRules.AssertDatesChangeable(s.GroupBlockId);
            StayChangeRules.AssertExtend(s.Departure, newDeparture, bd);
            var room = await store.LockRoomAsync(tx, ctx.PropertyId, s.CurrentRoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            if (await store.RoomBlockedDuringAsync(tx, room.RoomId, s.Departure, newDeparture, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.NOT_AVAILABLE, "Target room is unavailable for the requested stay.");

            var posted = await store.PostedNightsAsync(tx, s.FolioId, c).ConfigureAwait(false);
            var agreed = await AgreedRateAsync(tx, s, c).ConfigureAwait(false);
            var loss = await RateLossAsync(tx, ctx, rate, agreed, Unposted(bd, newDeparture, posted), c).ConfigureAwait(false);
            var ctl = await RateControlAsync(tx, ctx, s, rate, agreed, room.RoomTypeId, bd, c).ConfigureAwait(false);

            var current = await store.RoomNightsAsync(tx, s.ReservationId, c).ConfigureAwait(false);
            var desired = current.Concat(Range(room.RoomId, room.RoomTypeId, s.Departure, newDeparture)).ToList();
            await ReplanAsync(tx, ctx.PropertyId, s.ReservationId, current, desired, c).ConfigureAwait(false);

            if (rate.Change) await RepriceAsync(tx, ctx.PropertyId, s.ReservationId, bd, newDeparture, posted, rate.NewRatePaise!.Value, c).ConfigureAwait(false);
            else await store.WriteNightPricesAsync(tx, s.ReservationId, await pricer.PriceAsync(ctx.PropertyId, s.Departure, newDeparture, agreed, c).ConfigureAwait(false), c).ConfigureAwait(false);
            var version = await store.ApplyDeparturesAsync(tx, s, newDeparture, rate.NewRatePaise, c).ConfigureAwait(false);
            await store.InsertChangeAsync(tx, ctx.PropertyId, new StayChangeLog(s.StayId, "extend", bd, room.RoomId, room.RoomId, s.Departure, newDeparture,
                rate.Change ? RateDecisions.Change : RateDecisions.Keep, agreed, rate.NewRatePaise, loss, reason), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            if (ctl is not null) await RecordRateAsync(tx, ctx, s, room.RoomId, bd, agreed, rate.NewRatePaise!.Value, "Stay extended", reason, ctl, c).ConfigureAwait(false);
            return await FinishAsync(tx, ctx, s, room, "extend", StayChangeEvents.StayExtended, "EXTEND_STAY", newDeparture, bd, newDeparture.DayNumber - s.Departure.DayNumber,
                rate.NewRatePaise ?? agreed, loss, version, reason, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ChangeStayDatesResult>> ShortenStayAsync(CommandContext ctx, ShortenStayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.StayId <= 0) throw HccException.Validation("Select a stay.");
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var newDeparture = StayDates.Parse(req.NewDeparture, "newDeparture");
        var reason = StayChangeRules.Reason(req.Reason, required: true);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StayChangePermissions.MoveExtend, null, null, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpShortenStay }, req, (tx, c) => ReleaseAsync(tx, ctx, req.StayId, expected, newDeparture, reason, early: false, c), ct).ConfigureAwait(false);
    }

    /// <summary>EarlyDeparture: the guest leaves today and the remaining nights are released. DEC-082: no early-departure fee (only nights stayed are charged; a Manager may add a charge by hand).</summary>
    public async Task<CommandResult<ChangeStayDatesResult>> EarlyDepartureAsync(CommandContext ctx, EarlyDepartureRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.StayId <= 0) throw HccException.Validation("Select a stay.");
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var reason = StayChangeRules.Reason(req.Reason, required: true);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StayChangePermissions.MoveExtend, null, null, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpEarlyDeparture }, req, (tx, c) => ReleaseAsync(tx, ctx, req.StayId, expected, null, reason, early: true, c), ct).ConfigureAwait(false);
    }

    /// <summary>Shorten (new departure given) or early departure (leaves on the business date): releases the nights after it.</summary>
    private async Task<ChangeStayDatesResult> ReleaseAsync(ICommandTransaction tx, CommandContext ctx, long stayId, byte[] expected, DateOnly? requested, string reason, bool early, CancellationToken c)
    {
        var (s, bd) = await LoadAsync(tx, ctx, stayId, expected, c).ConfigureAwait(false);
        StayChangeRules.AssertDatesChangeable(s.GroupBlockId);
        var posted = await store.PostedNightsAsync(tx, s.FolioId, c).ConfigureAwait(false);
        var last = posted.Count == 0 ? (DateOnly?)null : posted.Max();
        DateOnly newDeparture;
        if (early)
        {
            StayChangeRules.AssertEarlyDeparture(s.Arrival, s.Departure, bd, last);
            newDeparture = bd;
        }
        else
        {
            newDeparture = requested!.Value;
            StayChangeRules.AssertShorten(s.Arrival, s.Departure, newDeparture, bd, last);
        }

        var room = await store.LockRoomAsync(tx, ctx.PropertyId, s.CurrentRoomId, c).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
        var current = await store.RoomNightsAsync(tx, s.ReservationId, c).ConfigureAwait(false);
        var desired = current.Where(n => n.Night < newDeparture).ToList();
        await ReplanAsync(tx, ctx.PropertyId, s.ReservationId, current, desired, c).ConfigureAwait(false);
        await store.DeleteNightPricesFromAsync(tx, s.ReservationId, newDeparture, c).ConfigureAwait(false);
        var version = await store.ApplyDeparturesAsync(tx, s, newDeparture, null, c).ConfigureAwait(false);
        var agreed = await AgreedRateAsync(tx, s, c).ConfigureAwait(false);
        var kind = early ? "early" : "shorten";
        await store.InsertChangeAsync(tx, ctx.PropertyId, new StayChangeLog(s.StayId, kind, bd, room.RoomId, room.RoomId, s.Departure, newDeparture,
            RateDecisions.Keep, agreed, null, 0, reason), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
        return await FinishAsync(tx, ctx, s, room, kind, StayChangeEvents.StayShortened, early ? "EARLY_DEPARTURE" : "SHORTEN_STAY", newDeparture, bd,
            s.Departure.DayNumber - newDeparture.DayNumber, agreed, 0, version, reason, c).ConfigureAwait(false);
    }

    private async Task<ChangeStayDatesResult> FinishAsync(ICommandTransaction tx, CommandContext ctx, StayChangeContext s, ChangeRoom room, string kind, string eventType,
        string auditAction, DateOnly newDeparture, DateOnly bd, int nights, long rate, long loss, byte[] version, string reason, CancellationToken c)
    {
        await tx.AuditAsync(auditAction, Target(s.StayId),
            $"reservation {s.ReservationId} · room {room.RoomNo} · departure {StayDates.ToWire(s.Departure)} → {StayDates.ToWire(newDeparture)} ({nights} night(s)) · " +
            (loss > 0 ? $"rate lowered, loss {loss} paise" : "agreed tariff") + (reason.Length > 0 ? " · " + reason : "") + $" · business date {StayDates.ToWire(bd)}", c).ConfigureAwait(false);
        var outboxId = await tx.PublishAsync(eventType, s.StayId, new StayDatesChangedEvent(s.StayId, s.ReservationId, ctx.PropertyId, room.RoomId, kind,
            StayDates.ToWire(s.Departure), StayDates.ToWire(newDeparture), StayDates.ToWire(bd)), c).ConfigureAwait(false);
        return new ChangeStayDatesResult(s.StayId, s.ReservationId, room.RoomId, room.RoomNo, StayDates.ToWire(s.Departure), StayDates.ToWire(newDeparture), nights,
            rate, loss, StayDates.ToWire(bd), VersionToken.ToWire(version), outboxId);
    }

    private async Task<(StayChangeContext Stay, DateOnly BusinessDate)> LoadAsync(ICommandTransaction tx, CommandContext ctx, long stayId, byte[] expected, CancellationToken c)
    {
        var s = await store.LockStayAsync(tx, ctx.PropertyId, stayId, c).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
        StayChangeRules.AssertInHouse(s.StayStatus);
        VersionToken.EnsureMatches(s.StayVersion, expected, "This stay");
        var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
        if (days is not null) await days.EnsureOpenAsync(tx, ctx.PropertyId, bd, c).ConfigureAwait(false); // writes belong to an open business day
        return (s, bd);
    }

    private async Task<long> AgreedRateAsync(ICommandTransaction tx, StayChangeContext s, CancellationToken c)
    {
        if (s.NightlyRatePaise is { } r) return r;
        return await store.LastNightRateAsync(tx, s.ReservationId, c).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "This stay has no agreed rate. Amend the reservation to price it.");
    }

    /// <summary>The revenue given up by a lower rate; > 0 needs the approval permission for that amount (STAY_RATE_LOSS audit by the caller's detail).</summary>
    private async Task<long> RateLossAsync(ICommandTransaction tx, CommandContext ctx, RateChoice rate, long agreed, int unpostedNights, CancellationToken c)
    {
        if (!rate.Change) return 0;
        var loss = StayChangeRules.DowngradeLoss(agreed, rate.NewRatePaise!.Value, unpostedNights);
        if (loss > 0) await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StayChangePermissions.ApproveRate, null, loss, c).ConfigureAwait(false);
        return loss;
    }

    /// <summary>CTL-01 judgement of a move / extension rate: tariff and agreed rate it was judged against, the threshold, the Owner flag, beyond the threshold.</summary>
    private sealed record RateControlResult(Paise Tariff, Paise Agreed, int ThresholdBps, bool Owner, bool Over);

    /// <summary>
    /// CTL-01 for a rate that actually moves on a move / extension (null when it does not, or when no rate store is wired — production always wires
    /// SqlStayRateStore through the LF-money module). Over the agreed-rate threshold: the Owner only; anyone else is refused with the ChangeStayRate text.
    /// </summary>
    private async Task<RateControlResult?> RateControlAsync(ICommandTransaction tx, CommandContext ctx, StayChangeContext s, RateChoice rate, long current, int roomTypeId,
        DateOnly bd, CancellationToken c)
    {
        if (rates is null || !rate.Change || rate.NewRatePaise is not { } nr || nr == current) return null;
        var newRate = new Paise(nr);
        var auth = await rates.AuthorityAsync(tx, ctx.PrincipalId, c).ConfigureAwait(false);
        var threshold = settings is null ? RateControlSettings.DefaultThresholdBps
            : RateControlSettings.Read(await settings.GetAsync(tx, ctx.PropertyId, RateControlSettings.ThresholdKey, null, c).ConfigureAwait(false));
        var (latest, first) = await rates.AgreedHistoryAsync(tx, s.StayId, c).ConfigureAwait(false);
        var agreed = StayRateRules.AgreedBase(latest, first, current);
        var over = StayRateRules.NeedsOwner(agreed, threshold, new Paise(current), newRate);
        if (over && !auth.Owner) throw new HccException(HccErrorCode.FORBIDDEN, StayRateRules.OwnerDecidesText(agreed, threshold));
        var tariff = new Paise(await rates.TariffAsync(tx, roomTypeId, bd, c).ConfigureAwait(false));
        return new RateControlResult(tariff, agreed, threshold, auth.Owner, over);
    }

    /// <summary>The gst.stay_rate_change row of a move / extension rate (scope today-next: un-posted nights from the business date; posted nights untouched),
    /// the agreed rate after it (only an Owner-set rate moves it) and, beyond the threshold, the RATE_REDUCTION_OVER_THRESHOLD audit line.</summary>
    private async Task RecordRateAsync(ICommandTransaction tx, CommandContext ctx, StayChangeContext s, int roomId, DateOnly bd, long oldRate, long newRate, string what,
        string reason, RateControlResult rc, CancellationToken c)
    {
        var why = $"{what} · {reason}";
        if (why.Length > 200) why = why[..200];
        var agreedAfter = rc.Owner ? newRate : rc.Agreed.Value;
        var changeId = await rates!.InsertChangeAsync(tx, new StayRateChangeRow(ctx.PropertyId, s.StayId, s.ReservationId, roomId, bd, UtcNow, ctx.PrincipalId, null, null,
            StayRateScopes.TodayNext, why, oldRate, newRate, s.Adults, s.Adults, s.Children, s.Children, bd, "[]",
            rc.Tariff.Value, rc.Agreed.Value, agreedAfter, rc.ThresholdBps, rc.Over), c).ConfigureAwait(false);
        if (rc.Over)
            await tx.AuditAsync("RATE_REDUCTION_OVER_THRESHOLD", Target(s.StayId),
                $"change {changeId.ToString(CultureInfo.InvariantCulture)} · agreed {rc.Agreed.Value} → {newRate} paise · {StayRateRules.ReductionBps(rc.Agreed, new Paise(newRate))} bps below the agreed rate (threshold {rc.ThresholdBps} bps)"
                + $" · changed by {ctx.PrincipalId.ToString(CultureInfo.InvariantCulture)} · applied directly · {why}", c).ConfigureAwait(false);
    }

    /// <summary>Un-posted nights on or after the business date up to (excluding) <paramref name="to"/>.</summary>
    private static int Unposted(DateOnly bd, DateOnly to, IReadOnlySet<DateOnly> posted)
        => Enumerable.Range(0, Math.Max(0, to.DayNumber - bd.DayNumber)).Count(i => !posted.Contains(bd.AddDays(i)));

    private async Task RepriceAsync(ICommandTransaction tx, int propertyId, long reservationId, DateOnly bd, DateOnly to, IReadOnlySet<DateOnly> posted, long rate, CancellationToken c)
    {
        var nights = (await pricer.PriceAsync(propertyId, bd, to, rate, c).ConfigureAwait(false)).Where(n => !posted.Contains(n.Date)).ToList();
        await store.WriteNightPricesAsync(tx, reservationId, nights, c).ConfigureAwait(false);
    }

    private static IEnumerable<RoomNightKey> Range(int roomId, int typeId, DateOnly from, DateOnly to)
    {
        for (var n = from; n < to; n = n.AddDays(1)) yield return new RoomNightKey(roomId, typeId, n);
    }

    /// <summary>
    /// Applies a night plan through the allocator. Extensions only add runs; anything that releases nights releases every current run and
    /// re-adds the wanted ones (the allocator deletes all of a reservation's room nights on any release), all in this transaction.
    /// </summary>
    private async Task ReplanAsync(ICommandTransaction tx, int propertyId, long reservationId, IReadOnlyList<RoomNightKey> current, IReadOnlyList<RoomNightKey> desired, CancellationToken c)
    {
        static StayAllocation Alloc(NightRun r) => new(r.RoomId, r.RoomTypeId, r.From, r.To);
        if (NightPlan.Removed(current, desired).Count == 0)
        {
            foreach (var run in NightPlan.Runs(NightPlan.Added(current, desired)))
                await allocator.ChangeAsync(tx, propertyId, reservationId, null, Alloc(run), returnToBlock: false, c).ConfigureAwait(false);
            return;
        }

        foreach (var run in NightPlan.Runs(current))
            await allocator.ChangeAsync(tx, propertyId, reservationId, Alloc(run), null, returnToBlock: false, c).ConfigureAwait(false);
        foreach (var run in NightPlan.Runs(desired))
            await allocator.ChangeAsync(tx, propertyId, reservationId, null, Alloc(run), returnToBlock: false, c).ConfigureAwait(false);
    }

    private static string Target(long stayId) => "stay-" + stayId.ToString(CultureInfo.InvariantCulture);
}
