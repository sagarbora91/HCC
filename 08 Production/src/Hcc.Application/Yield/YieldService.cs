using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Yield;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Yield;

namespace Hcc.Application.Yield;

/// <summary>
/// Yield &amp; tariff commands (port of the prototype's <c>YLD_*</c> handlers). Authorization first, every command through
/// <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox in one transaction); actor only from the session context.
/// The Owner (admin.configuration) changes rules and publishes; a Manager (approval.decide) can only propose a publish, which the
/// Owner decides; nobody decides their own proposal.
/// </summary>
public sealed class YieldService(IYieldStore store, IRatePublicationPort publication, ICommandRunner runner, IAccessPolicy access, TimeProvider clock, Hcc.Application.Closing.IBusinessDayGuard? days = null)
{
    /// <summary>Publish window in nights from the business date (prototype YLD_WINDOW).</summary>
    public const int WindowNights = 14;

    /// <summary>CONFIG: proposal lifetime; becomes the Master-configuration "approval expiry" setting (prototype approvalExpiryHours = 24).</summary>
    public static readonly TimeSpan ProposalLifetime = TimeSpan.FromHours(24);

    private static readonly int[] RoundOptions = [100, 1000, 5000, 10000];

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ───────────── rules (Owner) ─────────────
    public async Task<CommandResult<YieldChangeResult>> SavePlanAsync(CommandContext ctx, SaveYieldPlanRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            var cfg = await store.LoadConfigAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            var p = cfg.Plans.FirstOrDefault(x => string.Equals(x.Code, r.PlanCode, StringComparison.Ordinal)) ?? throw YieldVersion.NotFound("Rate plan");
            var before = $"{YieldFormat.Pct(p.AdjBps)} · min {p.MinNights} · {(p.Active ? "active" : "inactive")}";
            var adj = r.AdjBps ?? p.AdjBps;
            var min = r.MinNights ?? p.MinNights;
            var active = r.Active ?? p.Active;
            if (p.Code == "BAR")
            {
                if (adj != 0) throw HccException.Validation("BAR is the reference rate: its adjustment stays at 0%. Change seasons or rules instead.");
                if (!active) throw HccException.Validation("BAR cannot be deactivated.");
            }

            Bps(adj, -9000, 5000);
            if (p.IsDayUse)
            {
                if (min != 0) throw HccException.Validation("Day-use has no overnight (minimum nights 0).");
            }
            else if (min < 1 || min > 30) throw HccException.Validation("Minimum nights must be 1–30.");

            var close = p.CloseAtOcc;
            if (p.IsDayUse && r.CloseAtOcc is { } co)
            {
                if (co < 50 || co > 101) throw HccException.Validation("Closing occupancy must be 50–101%.");
                close = co;
            }

            var after = $"{YieldFormat.Pct(adj)} · min {min} · {(active ? "active" : "inactive")}";
            if (before == after && r.CloseAtOcc is null) throw HccException.Validation("No change.");
            var version = await store.UpdatePlanAsync(tx, ctx.PropertyId, p with { AdjBps = adj, MinNights = min, Active = active, CloseAtOcc = close },
                YieldVersion.Parse(r.ExpectedVersion), c).ConfigureAwait(false);
            await LogAsync(tx, ctx, "Plan · " + p.Name, before, after, r.Reason, c).ConfigureAwait(false);
            return new YieldChangeResult($"Rate plan {p.Code} · {before} → {after}", version);
        }, ct).ConfigureAwait(false);

    public async Task<CommandResult<YieldChangeResult>> SaveSeasonAsync(CommandContext ctx, SaveYieldSeasonRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            var inputs = await store.LoadInputsAsync(ctx.PropertyId, null, 1, Now, tx, c).ConfigureAwait(false);
            var cfg = await store.LoadConfigAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            string Show(YieldSeason s) => $"{StayDates.ToWire(s.From)}–{StayDates.ToWire(s.To)} {YieldFormat.Pct(s.AdjBps)}" +
                (s.RoomTypeIds.Count > 0 ? " · " + string.Join(", ", s.RoomTypeIds.Select(i => inputs.Types.First(t => t.Id == i).Code)) : "");
            if (r.Remove)
            {
                var old = cfg.Seasons.FirstOrDefault(x => x.Id == r.SeasonId) ?? throw YieldVersion.NotFound("Season");
                await store.DeleteSeasonAsync(tx, ctx.PropertyId, old.Id, YieldVersion.Parse(r.ExpectedVersion), c).ConfigureAwait(false);
                await LogAsync(tx, ctx, "Season · " + old.Name, Show(old), "removed", r.Reason, c).ConfigureAwait(false);
                return new YieldChangeResult($"Season {old.Name} removed", null);
            }

            var name = (r.Name ?? "").Trim();
            if (name.Length < 2) throw HccException.Validation("Name the season.");
            var from = StayDates.Parse(r.From, "start date");
            var to = StayDates.Parse(r.To, "end date");
            if (to < from) throw HccException.Validation("The season must end on or after its start date.");
            if (to.DayNumber - from.DayNumber > 365) throw HccException.Validation("A season can cover at most one year.");
            Bps(r.AdjBps, -5000, 10000);
            if (r.AdjBps == 0) throw HccException.Validation("A season needs a non-zero adjustment.");
            var types = TypeIds(inputs, r.RoomTypeIds);
            var rec = new YieldSeason(r.SeasonId ?? 0, name, from, to, r.AdjBps, types, r.Priority ?? 1);
            if (r.SeasonId is { } id)
            {
                var old = cfg.Seasons.FirstOrDefault(x => x.Id == id) ?? throw YieldVersion.NotFound("Season");
                var (_, ver) = await store.SaveSeasonAsync(tx, ctx.PropertyId, id, YieldVersion.Parse(r.ExpectedVersion), name, from, to, r.AdjBps, types, rec.Priority, c).ConfigureAwait(false);
                await LogAsync(tx, ctx, "Season · " + name, Show(old), Show(rec), r.Reason, c).ConfigureAwait(false);
                return new YieldChangeResult($"Season {name} updated · {Show(rec)}", ver);
            }

            var (_, v) = await store.SaveSeasonAsync(tx, ctx.PropertyId, null, null, name, from, to, r.AdjBps, types, rec.Priority, c).ConfigureAwait(false);
            await LogAsync(tx, ctx, "Season · " + name, "—", Show(rec), r.Reason, c).ConfigureAwait(false);
            return new YieldChangeResult($"Season {name} added · {Show(rec)}", v);
        }, ct).ConfigureAwait(false);

    public async Task<CommandResult<YieldChangeResult>> SetDayOfWeekAsync(CommandContext ctx, SetYieldDayOfWeekRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            if (r.Dow is null || r.Dow.Count != 7) throw HccException.Validation("Give an adjustment for each day of the week.");
            foreach (var v in r.Dow) Bps(v, -3000, 5000);
            var cfg = await store.LoadConfigAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            static string Show(IReadOnlyList<int> xs) => xs.Select((v, i) => v != 0 ? $"{YieldFormat.DowNames[i]} {YieldFormat.Pct(v)}" : "").Where(s => s.Length > 0).DefaultIfEmpty("none").Aggregate((a, b) => a + ", " + b);
            var before = Show(cfg.Dow);
            var after = Show(r.Dow);
            if (before == after) throw HccException.Validation("No change.");
            var ver = await store.SetDowAsync(tx, ctx.PropertyId, r.Dow, YieldVersion.Parse(r.ExpectedVersion), c).ConfigureAwait(false);
            await LogAsync(tx, ctx, "Day-of-week rules", before, after, r.Reason, c).ConfigureAwait(false);
            return new YieldChangeResult("Day-of-week rules · " + after, ver);
        }, ct).ConfigureAwait(false);

    public async Task<CommandResult<YieldChangeResult>> SaveOccupancyRuleAsync(CommandContext ctx, SaveYieldOccupancyRuleRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            var cfg = await store.LoadConfigAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            static string Show(string op, int occ, int adj) => $"occupancy {(op == ">=" ? "≥" : "<")} {occ}% → {YieldFormat.Pct(adj)}";
            if (r.Remove)
            {
                var old = cfg.OccRules.FirstOrDefault(x => x.Id == r.RuleId) ?? throw YieldVersion.NotFound("Rule");
                await store.DeleteOccRuleAsync(tx, ctx.PropertyId, old.Id, YieldVersion.Parse(r.ExpectedVersion), c).ConfigureAwait(false);
                await LogAsync(tx, ctx, "Occupancy rule", Show(old.Op, old.Occ, old.AdjBps), "removed", r.Reason, c).ConfigureAwait(false);
                return new YieldChangeResult("Occupancy rule removed · " + Show(old.Op, old.Occ, old.AdjBps), null);
            }

            if (r.Op is not (">=" or "<")) throw HccException.Validation("Choose “at or above” or “below”.");
            if (r.Occ < 1 || r.Occ > 100) throw HccException.Validation("Occupancy threshold must be 1–100%.");
            Bps(r.AdjBps, -5000, 5000);
            if (r.AdjBps == 0) throw HccException.Validation("A rule needs a non-zero adjustment.");
            if (r.Op == ">=" && r.AdjBps <= 0) throw HccException.Validation("High-occupancy rules should raise the rate.");
            if (r.Op == "<" && r.AdjBps >= 0) throw HccException.Validation("Low-occupancy rules should lower the rate.");
            if (cfg.OccRules.Any(x => x.Id != r.RuleId && x.Op == r.Op && x.Occ == r.Occ)) throw HccException.Validation("A rule with this threshold already exists.");
            if (r.RuleId is { } id)
            {
                var old = cfg.OccRules.FirstOrDefault(x => x.Id == id) ?? throw YieldVersion.NotFound("Rule");
                var (_, ver) = await store.SaveOccRuleAsync(tx, ctx.PropertyId, id, YieldVersion.Parse(r.ExpectedVersion), r.Op, r.Occ, r.AdjBps, c).ConfigureAwait(false);
                await LogAsync(tx, ctx, "Occupancy rule", Show(old.Op, old.Occ, old.AdjBps), Show(r.Op, r.Occ, r.AdjBps), r.Reason, c).ConfigureAwait(false);
                return new YieldChangeResult("Occupancy rule updated · " + Show(r.Op, r.Occ, r.AdjBps), ver);
            }

            var (_, v) = await store.SaveOccRuleAsync(tx, ctx.PropertyId, null, null, r.Op, r.Occ, r.AdjBps, c).ConfigureAwait(false);
            await LogAsync(tx, ctx, "Occupancy rule", "—", Show(r.Op, r.Occ, r.AdjBps), r.Reason, c).ConfigureAwait(false);
            return new YieldChangeResult("Occupancy rule added · " + Show(r.Op, r.Occ, r.AdjBps), v);
        }, ct).ConfigureAwait(false);

    public async Task<CommandResult<YieldChangeResult>> SetGuardrailsAsync(CommandContext ctx, SetYieldGuardrailsRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            Bps(r.FloorBps, -5000, 0);
            Bps(r.CeilBps, 0, 10000);
            if (!RoundOptions.Contains(r.RoundToPaise)) throw HccException.Validation("Choose a rounding unit.");
            var cfg = await store.LoadConfigAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            static string Show(YieldGuard g) => $"floor {YieldFormat.Pct(g.FloorBps)} · ceiling {YieldFormat.Pct(g.CeilBps)} · round {YieldFormat.Money(g.RoundToPaise)}";
            var now = new YieldGuard(r.FloorBps, r.CeilBps, r.RoundToPaise);
            if (Show(cfg.Guard) == Show(now)) throw HccException.Validation("No change.");
            var ver = await store.SetGuardAsync(tx, ctx.PropertyId, now, YieldVersion.Parse(r.ExpectedVersion), c).ConfigureAwait(false);
            await LogAsync(tx, ctx, "Guardrails", Show(cfg.Guard), Show(now), r.Reason, c).ConfigureAwait(false);
            return new YieldChangeResult("Guardrails · " + Show(now), ver);
        }, ct).ConfigureAwait(false);

    public async Task<CommandResult<YieldChangeResult>> SaveRestrictionAsync(CommandContext ctx, SaveYieldRestrictionRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            var inputs = await store.LoadInputsAsync(ctx.PropertyId, null, 1, Now, tx, c).ConfigureAwait(false);
            string Show(YieldRestrictionDef x) => $"{(x.RoomTypeId is null ? "All types" : inputs.Types.First(t => t.Id == x.RoomTypeId).Code)} · arrivals {StayDates.ToWire(x.From)}–{StayDates.ToWire(x.To)}" +
                (x.MinStay > 1 ? $" · min {x.MinStay} nights" : "") + (x.ClosedToArrival ? " · closed to arrival" : "");
            if (r.Remove)
            {
                var removed = await store.DeleteRestrictionAsync(tx, ctx.PropertyId, r.RestrictionId ?? 0, YieldVersion.Parse(r.ExpectedVersion), c).ConfigureAwait(false);
                await publication.RemoveRestrictionAsync(tx, ctx.PropertyId, removed.Code, c).ConfigureAwait(false);
                await LogAsync(tx, ctx, "Restriction", Show(removed), "removed", r.Reason, c).ConfigureAwait(false);
                return new YieldChangeResult("Restriction removed · " + Show(removed), null);
            }

            if (r.RoomTypeId is { } rt && inputs.Types.All(t => t.Id != rt)) throw HccException.Validation("Unknown room type.");
            var from = StayDates.Parse(r.From, "first arrival date");
            var to = StayDates.Parse(r.To, "last arrival date");
            if (to < from) throw HccException.Validation("The last arrival date must be on or after the first.");
            if (to.DayNumber - from.DayNumber > 90) throw HccException.Validation("A restriction can cover at most 90 days.");
            if (r.MinStay < 1 || r.MinStay > 14) throw HccException.Validation("Minimum stay must be 1–14 nights.");
            if (r.MinStay <= 1 && !r.ClosedToArrival) throw HccException.Validation("Set a minimum stay above 1 night or close to arrival.");
            var note = (r.Note ?? "").Trim();
            var (old, saved, _, ver) = await store.SaveRestrictionAsync(tx, ctx.PropertyId, r.RestrictionId, r.RestrictionId is null ? null : YieldVersion.Parse(r.ExpectedVersion),
                r.RoomTypeId, from, to, r.MinStay, r.ClosedToArrival, note, c).ConfigureAwait(false);
            await publication.UpsertRestrictionAsync(tx, ctx.PropertyId, saved, c).ConfigureAwait(false); // P2 enforces it from here on
            await LogAsync(tx, ctx, "Restriction", old is null ? "—" : Show(old), Show(saved), r.Reason, c).ConfigureAwait(false);
            return new YieldChangeResult((old is null ? "Restriction added · " : "Restriction updated · ") + Show(saved), ver);
        }, ct).ConfigureAwait(false);

    public async Task<CommandResult<YieldChangeResult>> SetOverrideAsync(CommandContext ctx, SetYieldOverrideRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            var inputs = await store.LoadInputsAsync(ctx.PropertyId, null, WindowNights, Now, tx, c).ConfigureAwait(false);
            var type = inputs.Types.FirstOrDefault(t => t.Id == r.RoomTypeId) ?? throw HccException.Validation("Choose a room type.");
            var night = StayDates.Parse(r.Night, "night");
            if (night < inputs.BusinessDate) throw HccException.Validation("Overrides apply to tonight or later.");
            var reason = (r.Reason ?? "").Trim();
            if (reason.Length < 3) throw HccException.Validation("Give a reason for the override.");
            var cfg = await store.LoadConfigAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            var existing = cfg.Overrides.FirstOrDefault(o => o.RoomTypeId == type.Id && o.Night == night);
            var key = $"{type.Code} {StayDates.ToWire(night)}";
            if (r.RatePaise is null)
            {
                if (existing is null) throw YieldVersion.NotFound("Override");
                await store.SetOverrideAsync(tx, ctx.PropertyId, type.Id, night, null, reason, ctx.PrincipalId, Now, c).ConfigureAwait(false);
                await LogAsync(tx, ctx, "Override · " + key, YieldFormat.Money(existing.RatePaise), "cleared", reason, c).ConfigureAwait(false);
                return new YieldChangeResult("Override cleared · " + key, null);
            }

            var rate = r.RatePaise.Value;
            if (rate < 10000 || rate > 10000000) throw HccException.Validation("Enter a nightly rate between ₹100 and ₹1,00,000.");
            var before = existing is not null ? YieldFormat.Money(existing.RatePaise)
                : night < inputs.BusinessDate.AddDays(WindowNights) ? "computed " + YieldFormat.Money(new YieldEngine(cfg, inputs).Night(type.Id, night).Computed) : "computed";
            await store.SetOverrideAsync(tx, ctx.PropertyId, type.Id, night, rate, reason, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            await LogAsync(tx, ctx, "Override · " + key, before, YieldFormat.Money(rate), reason, c).ConfigureAwait(false);
            return new YieldChangeResult($"Override · {key} → {YieldFormat.Money(rate)} (not live until published)", null);
        }, ct).ConfigureAwait(false);

    /// <summary>Set the SIMULATED rooms-sold forecast per night (fixture-style input; replaced by a real pickup feed later).</summary>
    public async Task<CommandResult<YieldChangeResult>> SetForecastAsync(CommandContext ctx, SetYieldForecastRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            if (r.Nights is null || r.Nights.Count == 0 || r.Nights.Count > 120) throw HccException.Validation("Give 1–120 nights.");
            var rows = r.Nights.Select(n =>
            {
                if (n.RoomsSold < 0 || n.RoomsSold > 1000) throw HccException.Validation("Rooms sold must be 0–1000.");
                return (StayDates.Parse(n.Night, "night"), n.RoomsSold);
            }).ToList();
            await store.SetForecastAsync(tx, ctx.PropertyId, rows, c).ConfigureAwait(false);
            await LogAsync(tx, ctx, "Forecast", "—", $"{rows.Count} nights (simulated)", null, c).ConfigureAwait(false);
            return new YieldChangeResult($"Forecast set for {rows.Count} nights (simulated)", null);
        }, ct).ConfigureAwait(false);

    // ───────────── publish / propose / decide ─────────────
    public async Task<CommandResult<YieldPublishResult>> PublishAsync(CommandContext ctx, PublishYieldRequest r, CancellationToken ct = default)
        => await ManageAsync(ctx, r, async (tx, c) =>
        {
            var (inputs, cfg, from, to, types) = await WindowAsync(ctx, tx, r.From, r.To, r.RoomTypeIds, c).ConfigureAwait(false);
            var engine = new YieldEngine(cfg, inputs);
            var res = await ApplyAsync(tx, ctx, engine, from, to, types, null, r.Reason, null, c).ConfigureAwait(false);
            return new YieldPublishResult(res.Id, res.Changed, StayDates.ToWire(from), StayDates.ToWire(to),
                $"{res.Changed} nightly tariff{(res.Changed == 1 ? "" : "s")} published {StayDates.ToWire(from)} to {StayDates.ToWire(to)} · existing bookings keep agreed rates");
        }, ct).ConfigureAwait(false);

    public async Task<CommandResult<YieldProposalResult>> ProposeAsync(CommandContext ctx, ProposeYieldPublishRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, YieldPermissions.View, ct: ct).ConfigureAwait(false);
        // approval.decide is a limited grant for the Manager (Rs 1,000): proposing a tariff publish moves no money, so the amount checked is 0.
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, YieldPermissions.Propose, amountPaise: 0, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            await store.EnsureSeededAsync(tx, ctx.PropertyId, true, c).ConfigureAwait(false);
            var (inputs, cfg, from, to, types) = await WindowAsync(ctx, tx, r.From, r.To, r.RoomTypeIds, c).ConfigureAwait(false);
            var now = Now;
            if (await store.HasLivePendingProposalAsync(tx, ctx.PropertyId, now, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "A tariff publish request is already pending. Ask the Owner to decide it first.");
            var engine = new YieldEngine(cfg, inputs);
            var rates = types.SelectMany(t => Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => (RoomTypeId: t, Night: from.AddDays(i), RatePaise: engine.Night(t, from.AddDays(i)).Suggested))).ToList();
            var map = rates.ToDictionary(x => (x.RoomTypeId, x.Night), x => x.RatePaise);
            var preview = engine.PlanPublish(from, to, types, (t, d) => map[(t, d)]);
            if (preview.Rows.Count == 0) throw HccException.Validation("Nothing to publish: the live tariffs already match the suggested rates.");
            var changed = preview.Rows.Count(x => x.Before != x.After);
            var reason = string.IsNullOrWhiteSpace(r.Reason) ? $"Publish yield tariffs {StayDates.ToWire(from)} to {StayDates.ToWire(to)}" : r.Reason.Trim();
            var draft = new YieldProposalDraft(from, to, ctx.PrincipalId, now, now + ProposalLifetime, reason, YieldEngine.TariffSignature(inputs.Tariffs, types), changed, types, rates);
            var id = await store.InsertProposalAsync(tx, ctx.PropertyId, draft, c).ConfigureAwait(false);
            var rec = await store.GetProposalAsync(ctx.PropertyId, id, tx, c).ConfigureAwait(false) ?? throw YieldVersion.NotFound("Proposal");
            var summary = $"Tariff publish {StayDates.ToWire(from)} to {StayDates.ToWire(to)} · {changed} night rate{(changed == 1 ? "" : "s")}";
            await LogAsync(tx, ctx, "Proposal " + id.ToString(CultureInfo.InvariantCulture), "—", summary + " · awaiting Owner", reason, c).ConfigureAwait(false);
            await tx.PublishAsync(YieldEvents.ProposalRaised, id, new { proposalId = id, from = StayDates.ToWire(from), to = StayDates.ToWire(to), changed }, c).ConfigureAwait(false);
            return new YieldProposalResult(id, "Pending", changed, YieldVersion.ToWire(rec.RowVersion), Iso(draft.ExpiresUtc), summary);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<YieldDecisionResult>> DecideAsync(CommandContext ctx, DecideYieldProposalRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, YieldPermissions.Decide, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            await store.EnsureSeededAsync(tx, ctx.PropertyId, true, c).ConfigureAwait(false);
            var p = await store.GetProposalAsync(ctx.PropertyId, r.ProposalId, tx, c).ConfigureAwait(false) ?? throw YieldVersion.NotFound("Tariff request");
            if (p.Status != "Pending") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request is no longer pending.");
            if (!p.RowVersion.AsSpan().SequenceEqual(YieldVersion.Parse(r.ExpectedVersion))) throw YieldVersion.Stale("The request");
            if (p.ProposedBy == ctx.PrincipalId)
                throw new HccException(HccErrorCode.FORBIDDEN, "You cannot approve your own request. Ask another authorised user to decide it.");
            var now = Now;
            var label = "Proposal " + p.ProposalId.ToString(CultureInfo.InvariantCulture);
            if (!r.Approve)
            {
                await store.CompleteProposalAsync(tx, p.ProposalId, "Rejected", ctx.PrincipalId, now, r.Note?.Trim(), null, c).ConfigureAwait(false);
                await LogAsync(tx, ctx, label, "pending", "rejected", r.Note, c).ConfigureAwait(false);
                await tx.PublishAsync(YieldEvents.ProposalDecided, p.ProposalId, new { proposalId = p.ProposalId, approved = false }, c).ConfigureAwait(false);
                return new YieldDecisionResult(p.ProposalId, "Rejected", null, 0, $"{label} · Rejected");
            }

            if (now > p.ExpiresUtc) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request expired. Reject it and ask for a new one.");
            var inputs = await store.LoadInputsAsync(ctx.PropertyId, null, WindowNights, now, tx, c).ConfigureAwait(false);
            if (YieldEngine.TariffSignature(inputs.Tariffs, p.RoomTypeIds) != p.Signature)
                throw new HccException(HccErrorCode.STALE_VERSION, "Tariffs changed after this request was raised. Reject it and propose again.");
            if (p.From < inputs.BusinessDate) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The first night of this request has passed. Reject it and propose again.");
            if (days is not null) await days.EnsureOpenAsync(tx, ctx.PropertyId, p.From, c).ConfigureAwait(false); // P6: DAY_CLOSED when the first night's date is closed
            var cfg = await store.LoadConfigAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            var engine = new YieldEngine(cfg, inputs);
            var map = p.Rates.ToDictionary(x => (x.RoomTypeId, x.Night), x => x.RatePaise);
            var res = await ApplyAsync(tx, ctx, engine, p.From, p.To, p.RoomTypeIds, (t, d) => map[(t, d)], p.Reason, p.ProposalId, c).ConfigureAwait(false);
            await store.CompleteProposalAsync(tx, p.ProposalId, "Executed", ctx.PrincipalId, now, r.Note?.Trim(), res.Id, c).ConfigureAwait(false);
            await tx.PublishAsync(YieldEvents.ProposalDecided, p.ProposalId, new { proposalId = p.ProposalId, approved = true, publishId = res.Id }, c).ConfigureAwait(false);
            return new YieldDecisionResult(p.ProposalId, "Executed", res.Id, res.Changed,
                $"{label} · approved · publish {res.Id} · {res.Changed} nightly tariff{(res.Changed == 1 ? "" : "s")} published");
        }, ct).ConfigureAwait(false);
    }

    // ───────────── helpers ─────────────
    private async Task<CommandResult<T>> ManageAsync<TReq, T>(CommandContext ctx, TReq request, Func<ICommandTransaction, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, YieldPermissions.View, ct: ct).ConfigureAwait(false);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, YieldPermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, request, async (tx, c) =>
        {
            await store.EnsureSeededAsync(tx, ctx.PropertyId, true, c).ConfigureAwait(false);
            return await body(tx, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private static void Bps(int v, int min, int max)
    {
        if (v < min || v > max) throw HccException.Validation($"Use an adjustment between {YieldFormat.Pct(min)} and {YieldFormat.Pct(max)}.");
    }

    private static IReadOnlyList<int> TypeIds(YieldInputs inputs, IReadOnlyList<int>? ids)
    {
        var list = (ids ?? []).Distinct().ToList();
        if (list.Any(i => inputs.Types.All(t => t.Id != i))) throw HccException.Validation("Unknown room type.");
        return list;
    }

    private async Task<(YieldInputs Inputs, YieldConfig Config, DateOnly From, DateOnly To, IReadOnlyList<int> Types)> WindowAsync(CommandContext ctx, ICommandTransaction tx,
        string? fromText, string? toText, IReadOnlyList<int>? typeIds, CancellationToken ct)
    {
        var from = StayDates.Parse(fromText, "first night");
        var to = StayDates.Parse(toText, "last night");
        var inputs = await store.LoadInputsAsync(ctx.PropertyId, null, WindowNights, Now, tx, ct).ConfigureAwait(false);
        if (from < inputs.BusinessDate) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Nights before the business date cannot be re-priced.");
        if (days is not null) await days.EnsureOpenAsync(tx, ctx.PropertyId, from, ct).ConfigureAwait(false); // P6: DAY_CLOSED when the first night's date is closed
        if (to < from) throw HccException.Validation("The last night must be on or after the first.");
        var last = inputs.BusinessDate.AddDays(WindowNights - 1);
        if (to > last) throw HccException.Validation($"Publish within the {WindowNights}-night window (to {StayDates.ToWire(last)}).");
        var types = TypeIds(inputs, typeIds);
        if (types.Count == 0) throw HccException.Validation("Choose at least one room type.");
        var cfg = await store.LoadConfigAsync(ctx.PropertyId, tx, ct).ConfigureAwait(false);
        return (inputs, cfg, from, to, types);
    }

    private async Task<(long Id, int Changed)> ApplyAsync(ICommandTransaction tx, CommandContext ctx, YieldEngine engine, DateOnly from, DateOnly to, IReadOnlyList<int> types,
        Func<int, DateOnly, long>? target, string? reason, long? proposalId, CancellationToken ct)
    {
        var plan = engine.PlanPublish(from, to, types, target);
        if (plan.Rows.Count == 0) throw HccException.Validation("Nothing to publish: the live tariffs already match the suggested rates.");
        var written = plan.Rows.Select(x => new TariffRow(x.RoomTypeId, x.Date, x.After)).ToList();
        if (days is not null) await days.EnsureOpenAsync(tx, ctx.PropertyId, from, ct).ConfigureAwait(false); // P6: DAY_CLOSED when the first night's date is closed
        await publication.WriteTariffsAsync(tx, ctx.PropertyId, written, ct).ConfigureAwait(false);
        await store.MarkOwnedAsync(tx, written, ct).ConfigureAwait(false);
        var id = await store.InsertPublishAsync(tx, ctx.PropertyId, from, to, ctx.PrincipalId, Now, (reason ?? "").Trim(), proposalId, plan.Rows, ct).ConfigureAwait(false);
        var changed = plan.Rows.Count(x => x.Before != x.After);
        var codes = string.Join(", ", types.Select(t => engine.Inputs.Types.First(x => x.Id == t).Code));
        await LogAsync(tx, ctx, "Publish " + id.ToString(CultureInfo.InvariantCulture), $"{StayDates.ToWire(from)} to {StayDates.ToWire(to)} · {codes}",
            $"{changed} night rate{(changed == 1 ? "" : "s")} written{(proposalId is null ? "" : " · approved proposal " + proposalId.Value.ToString(CultureInfo.InvariantCulture))}", reason, ct).ConfigureAwait(false);
        await tx.PublishAsync(YieldEvents.TariffsPublished, id, new
        {
            publishId = id,
            from = StayDates.ToWire(from),
            to = StayDates.ToWire(to),
            changed,
            rows = plan.Rows.Select(x => new { roomTypeId = x.RoomTypeId, night = StayDates.ToWire(x.Date), before = x.Before, after = x.After, closing = x.Closing }),
        }, ct).ConfigureAwait(false);
        return (id, changed);
    }

    private async Task LogAsync(ICommandTransaction tx, CommandContext ctx, string area, string before, string after, string? reason, CancellationToken ct)
    {
        var why = (reason ?? "").Trim();
        await store.AddHistoryAsync(tx, ctx.PropertyId, Now, ctx.PrincipalId, area, before, after, why, ct).ConfigureAwait(false);
        await tx.AuditAsync("Yield:" + ctx.Operation, area, $"{before} → {after}" + (why.Length > 0 ? " · " + why : ""), ct).ConfigureAwait(false);
    }

    public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
