using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Groups;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Groups;

/// <summary>
/// Groups billing commands (W3-4). Ports the prototype module 14 rules (<c>03 Prototype/src/domain/streams/groups.ts</c>: block
/// pickup figures, master folio aggregation, exposure estimate; <c>billTo</c> company | guest) and adds the production billing the
/// prototype leaves "simulated": a master folio per group, routing rules with member overrides, pickup billing (route snapshots of
/// posted member lines), re-routing as an appended route with a reason (bill.charge is never edited), transfers of the master share
/// off the guest folio (company-credit receipt through <see cref="IGroupFolioPort"/>), attrition after the cut-off decided by a
/// different approver, master receipts (non-cash) and settlement. Every command: <see cref="ICommandRunner"/> (Idempotency-Key,
/// audit, outbox), the real <see cref="IAccessPolicy"/>, the business-day guard, the group row lock and its rowversion.
/// </summary>
public sealed class GroupCommands(ICommandRunner runner, IGroupStore store, IAccessPolicy policy, IGroupFolioPort folios, IGstRateResolver gst,
    TimeProvider clock, IBusinessDayGuard? days = null)
{
    public const string OpCreate = "CreateGroup";
    public const string OpUpdate = "UpdateGroup";
    public const string OpRouting = "SetGroupRouting";
    public const string OpAttach = "AttachGroupMember";
    public const string OpDetach = "DetachGroupMember";
    public const string OpApply = "ApplyGroupRouting";
    public const string OpReroute = "RerouteGroupCharge";
    public const string OpTransfer = "TransferGroupMember";
    public const string OpReceipt = "RecordGroupReceipt";
    public const string OpAttrition = "RequestGroupAttrition";
    public const string OpDecide = "DecideGroupAttrition";
    public const string OpSettle = "SettleGroup";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    // ── Group master ──

    public async Task<CommandResult<GroupResult>> CreateAsync(CommandContext ctx, CreateGroupRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        var details = Details(req.Name, req.Organiser, req.BillingName, req.BillingContact, req.BillingPhone, req.BillingEmail, req.BillingAddress, req.Gstin,
            req.AttritionAllowanceBps, req.AttritionRateBps);
        var rules = GroupRouting.Defaults.ToDictionary(r => r.Category, r => r.Target);
        foreach (var r in req.Rules ?? [])
        {
            var category = GroupCodes.ParseCategory(r.Category);
            var rule = new RoutingRule(null, category, GroupCodes.ParseTarget(r.Target, category == RouteCategory.Tax));
            GroupRouting.Validate(rule);
            rules[category] = rule.Target;
        }

        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            await OpenDayAsync(tx, c).ConfigureAwait(false);
            if (req.GroupBlockId is { } bid)
            {
                var block = await store.BlockAsync(tx, ctx.PropertyId, bid, c).ConfigureAwait(false) ?? throw NotFound("Group block not found.");
                if (block.Status == "Cancelled") throw Invalid("This group block was cancelled.");
                if (await store.GroupOfBlockAsync(tx, bid, c).ConfigureAwait(false) is not null) throw Invalid($"Block {block.Code} already has a group master.");
            }

            var g = await store.InsertGroupAsync(tx, new NewGroup(ctx.PropertyId, req.GroupBlockId, details, ctx.PrincipalId, Now), c).ConfigureAwait(false);
            foreach (var (category, target) in rules)
                await store.SetRuleAsync(tx, g.GroupId, null, category, target, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            var version = await store.TouchGroupAsync(tx, g.GroupId, c).ConfigureAwait(false);
            await tx.AuditAsync("GROUP_CREATED", Target(g.GroupId), $"{g.Code} · {details.Name} · bill to {details.BillingName}"
                + (details.Gstin is null ? "" : " · GSTIN " + details.Gstin) + " · " + RulesText(rules), c).ConfigureAwait(false);
            await tx.PublishAsync("GroupCreated", g.GroupId, new { groupId = g.GroupId, code = g.Code, groupBlockId = req.GroupBlockId }, c).ConfigureAwait(false);
            return new GroupResult(g.GroupId, g.Code, g.Status, version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<GroupResult>> UpdateAsync(CommandContext ctx, UpdateGroupRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        var details = Details(req.Name, req.Organiser, req.BillingName, req.BillingContact, req.BillingPhone, req.BillingEmail, req.BillingAddress, req.Gstin,
            req.AttritionAllowanceBps, req.AttritionRateBps);
        Version(req.ExpectedVersion);
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var s = await OpenGroupAsync(tx, ctx, req.GroupId, req.ExpectedVersion, c).ConfigureAwait(false);
            var g = s.Group;
            if ((g.AllowanceBps != details.AllowanceBps || g.AttritionRateBps != details.AttritionRateBps) && s.Attritions.Any(a => a.Status is "Pending" or "Posted"))
                throw Invalid("The attrition terms are fixed once attrition has been requested.");
            var version = await store.UpdateGroupAsync(tx, g.GroupId, req.ExpectedVersion, details, c).ConfigureAwait(false);
            await tx.AuditAsync("GROUP_UPDATED", Target(g.GroupId), $"{g.Code} · {details.Name} · bill to {details.BillingName}"
                + $" · attrition {GstMath.Percent(details.AttritionRateBps)} beyond {GstMath.Percent(details.AllowanceBps)} free", c).ConfigureAwait(false);
            return new GroupResult(g.GroupId, g.Code, g.Status, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Routing rules and members ──

    public async Task<CommandResult<GroupResult>> SetRoutingAsync(CommandContext ctx, SetGroupRoutingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        Version(req.ExpectedVersion);
        var category = GroupCodes.ParseCategory(req.Category);
        RouteTarget? target = req.Target is null ? null : GroupCodes.ParseTarget(req.Target, category == RouteCategory.Tax);
        if (target is { } t) GroupRouting.Validate(new RoutingRule(req.ReservationId, category, t));
        if (target is null && req.ReservationId is null) throw HccException.Validation("The group default needs a target; only a member override can be removed.");
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var s = await OpenGroupAsync(tx, ctx, req.GroupId, req.ExpectedVersion, c).ConfigureAwait(false);
            if (req.ReservationId is { } rid && s.Members.All(m => m.ReservationId != rid)) throw NotFound("That reservation is not a member of this group.");
            await store.SetRuleAsync(tx, s.Group.GroupId, req.ReservationId, category, target, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            var version = await store.TouchGroupAsync(tx, s.Group.GroupId, c).ConfigureAwait(false);
            var who = req.ReservationId is { } r ? "member " + (s.Members.First(m => m.ReservationId == r).PublicNo) : "group default";
            await tx.AuditAsync("GROUP_ROUTING_SET", Target(s.Group.GroupId),
                $"{s.Group.Code} · {who} · {GroupCodes.Code(category)} → {(target is { } x ? GroupCodes.Code(x) : "group default")} · applies to lines not yet routed", c).ConfigureAwait(false);
            return new GroupResult(s.Group.GroupId, s.Group.Code, s.Group.Status, version);
        }, ct).ConfigureAwait(false);
    }

    public Task<CommandResult<GroupResult>> AttachMemberAsync(CommandContext ctx, GroupMemberRequest req, CancellationToken ct = default)
        => MemberAsync(ctx, req, attach: true, ct);

    public Task<CommandResult<GroupResult>> DetachMemberAsync(CommandContext ctx, GroupMemberRequest req, CancellationToken ct = default)
        => MemberAsync(ctx, req, attach: false, ct);

    private async Task<CommandResult<GroupResult>> MemberAsync(CommandContext ctx, GroupMemberRequest req, bool attach, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        Version(req.ExpectedVersion);
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var s = await OpenGroupAsync(tx, ctx, req.GroupId, req.ExpectedVersion, c).ConfigureAwait(false);
            var facts = await store.ReservationAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false) ?? throw NotFound("Reservation not found.");
            string publicNo;
            if (attach)
            {
                if (facts.Status is "Cancelled" or "NoShow" or "Expired") throw Invalid("A cancelled, no-show or expired reservation cannot join a group.");
                if (s.Members.Any(m => m.ReservationId == req.ReservationId)) throw Invalid("This reservation is already a member of the group.");
                if (facts.AttachedGroupId is not null || facts.BlockGroupId is not null) throw Invalid("This reservation already belongs to another group.");
                if (facts.GroupBlockId is not null) throw Invalid("This reservation is a pickup of a group block; it belongs to the group on that block.");
                await store.AttachMemberAsync(tx, s.Group.GroupId, req.ReservationId, ctx.PrincipalId, Now, c).ConfigureAwait(false);
                publicNo = N(req.ReservationId);
            }
            else
            {
                var m = s.Members.FirstOrDefault(x => x.ReservationId == req.ReservationId) ?? throw NotFound("That reservation is not a member of this group.");
                if (m.Source == "block") throw Invalid("A block pickup stays in its group; cancel or move the reservation in Reservations instead.");
                if (s.Entries.Any(e => e.Kind == "transfer" && e.ReservationId == m.ReservationId))
                    throw Invalid("This member's master share was already transferred; it cannot leave the group.");
                await store.DetachMemberAsync(tx, s.Group.GroupId, req.ReservationId, c).ConfigureAwait(false);
                publicNo = m.PublicNo;
            }

            var version = await store.TouchGroupAsync(tx, s.Group.GroupId, c).ConfigureAwait(false);
            await tx.AuditAsync(attach ? "GROUP_MEMBER_ATTACHED" : "GROUP_MEMBER_DETACHED", Target(s.Group.GroupId), $"{s.Group.Code} · reservation {publicNo}", c).ConfigureAwait(false);
            return new GroupResult(s.Group.GroupId, s.Group.Code, s.Group.Status, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Pickup billing ──

    /// <summary>Snapshots the route of every posted member line that has none (idempotent; later rule changes leave routed lines alone).</summary>
    public async Task<CommandResult<GroupRoutingResult>> ApplyRoutingAsync(CommandContext ctx, ApplyGroupRoutingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var s = await OpenGroupAsync(tx, ctx, req.GroupId, null, c).ConfigureAwait(false);
            var ledger = GroupLedger.Build(s);
            var (routed, already) = await RouteAsync(tx, ledger, null, c).ConfigureAwait(false);
            var version = s.Group.Version;
            if (routed > 0)
            {
                version = await store.TouchGroupAsync(tx, s.Group.GroupId, c).ConfigureAwait(false);
                await tx.AuditAsync("GROUP_CHARGES_ROUTED", Target(s.Group.GroupId), $"{s.Group.Code} · {routed} posted line(s) routed by the rules · {already} already routed", c).ConfigureAwait(false);
                await tx.PublishAsync("GroupChargesRouted", s.Group.GroupId, new { groupId = s.Group.GroupId, routed }, c).ConfigureAwait(false);
            }
            else
            {
                await tx.AuditAsync("GROUP_CHARGES_ROUTED", Target(s.Group.GroupId), $"{s.Group.Code} · nothing new to route · {already} already routed", c).ConfigureAwait(false);
            }

            return new GroupRoutingResult(s.Group.GroupId, routed, already, version);
        }, ct).ConfigureAwait(false);
    }

    private async Task<(int Routed, int Already)> RouteAsync(ICommandTransaction tx, GroupLedger ledger, long? onlyReservation, CancellationToken ct)
    {
        int routed = 0, already = 0;
        foreach (var m in ledger.Members.Where(m => onlyReservation is null || m.Member.ReservationId == onlyReservation))
            foreach (var l in m.Lines)
            {
                if (l.Routed)
                {
                    already++;
                    continue;
                }

                await store.InsertRouteAsync(tx, new NewRoute(ledger.Snapshot.Group.GroupId, l.Charge.Line.ChargeId, m.Member.ReservationId, l.Route.Net, l.Route.Tax,
                    "rule", null, tx.Context.PrincipalId, Now), ct).ConfigureAwait(false);
                routed++;
            }

        return (routed, already);
    }

    /// <summary>
    /// Moves a posted line between master and guest: a new route row (seq + 1) with the reason; the line, its tax snapshot and the
    /// earlier route rows stay as they are. Never below the share already transferred off the guest folio.
    /// </summary>
    public async Task<CommandResult<GroupRerouteResult>> RerouteAsync(CommandContext ctx, RerouteGroupChargeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        Version(req.ExpectedVersion);
        var route = new LineRoute(GroupCodes.ParseTarget(req.NetTarget), GroupCodes.ParseTarget(req.TaxTarget));
        var reason = GroupText.Required(req.Reason, 200, "Give a reason for re-routing this line.");
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var s = await OpenGroupAsync(tx, ctx, req.GroupId, req.ExpectedVersion, c).ConfigureAwait(false);
            var ledger = GroupLedger.Build(s);
            var member = ledger.Members.FirstOrDefault(m => m.Lines.Any(l => l.Charge.Line.ChargeId == req.ChargeId))
                ?? throw NotFound("That posted line is not on a member folio of this group.");
            var line = member.Lines.First(l => l.Charge.Line.ChargeId == req.ChargeId);
            if (line.IsReversal || member.Lines.Any(l => l.Charge.Line.ReversesChargeId == req.ChargeId))
                throw Invalid("A reversed line and its reversal keep their route.");
            if (line.Route == route) throw HccException.Validation("The line is already routed that way.");
            var next = GroupRouting.Shares(line.Charge.Line.Amount, line.Charge.Line.Tax, route);
            var newShare = member.MasterShare - line.Shares.Master + next.Master;
            GroupRouting.EnsureRerouteAllowed(member.FolioOpen, member.MasterShare, newShare, member.Transferred);
            var seq = await store.InsertRouteAsync(tx, new NewRoute(s.Group.GroupId, req.ChargeId, member.Member.ReservationId, route.Net, route.Tax, "reroute", reason,
                ctx.PrincipalId, Now), c).ConfigureAwait(false);
            var version = await store.TouchGroupAsync(tx, s.Group.GroupId, c).ConfigureAwait(false);
            await tx.AuditAsync("GROUP_CHARGE_REROUTED", "charge:" + N(req.ChargeId),
                $"{s.Group.Code} · {member.Member.PublicNo} · {line.Charge.Line.Label} · net {GroupCodes.Code(line.Route.Net)} → {GroupCodes.Code(route.Net)}, tax {GroupCodes.Code(line.Route.Tax)} → {GroupCodes.Code(route.Tax)} · master share {line.Shares.Master} → {next.Master} · {reason}", c).ConfigureAwait(false);
            await tx.PublishAsync("GroupChargeRerouted", s.Group.GroupId, new { groupId = s.Group.GroupId, chargeId = req.ChargeId, seq, net = GroupCodes.Code(route.Net), tax = GroupCodes.Code(route.Tax) }, c).ConfigureAwait(false);
            return new GroupRerouteResult(s.Group.GroupId, req.ChargeId, seq, GroupCodes.Code(route.Net), GroupCodes.Code(route.Tax), version);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Routes the member's new lines, then moves the untransferred master share off the guest folio (company-credit receipt there).</summary>
    public async Task<CommandResult<GroupTransferResult>> TransferAsync(CommandContext ctx, TransferGroupMemberRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var first = await OpenGroupAsync(tx, ctx, req.GroupId, null, c).ConfigureAwait(false);
            var m = first.Members.FirstOrDefault(x => x.ReservationId == req.ReservationId) ?? throw NotFound("That reservation is not a member of this group.");
            if (m.FolioId is not { } folioId) throw Invalid("This member has no folio yet; nothing is posted.");
            var folio = await folios.LockFolioAsync(tx, ctx.PropertyId, folioId, c).ConfigureAwait(false);
            if (!folio.IsOpen) throw Invalid("The guest folio is no longer open; nothing can move off it.");
            var s = await store.LoadAsync(tx, ctx.PropertyId, req.GroupId, c).ConfigureAwait(false) ?? throw NotFound("Group not found."); // under the folio lock
            var ledger = GroupLedger.Build(s);
            await RouteAsync(tx, ledger, req.ReservationId, c).ConfigureAwait(false);
            var member = ledger.Members.First(x => x.Member.ReservationId == req.ReservationId);
            var amount = member.PendingTransfer;
            if (amount <= Paise.Zero) throw Invalid("Nothing to transfer: this member's master share is already on the master folio.");
            var reference = $"Group master {s.Group.Code} · {member.Member.PublicNo}";
            var receiptId = await folios.TransferToMasterAsync(tx, folioId, amount, s.BusinessDate, reference, Now, c).ConfigureAwait(false);
            var entryId = await store.InsertEntryAsync(tx, new NewEntry(s.Group.GroupId, "transfer", s.BusinessDate, null, $"Transfer from {member.Member.PublicNo} · {member.Member.Guest}",
                amount, null, null, null, null, null, null, null, null, null, member.Member.ReservationId, folioId, receiptId, null, null, ctx.PrincipalId, Now), c).ConfigureAwait(false);
            var version = await store.TouchGroupAsync(tx, s.Group.GroupId, c).ConfigureAwait(false);
            await tx.AuditAsync("GROUP_MEMBER_TRANSFERRED", Target(s.Group.GroupId),
                $"{s.Group.Code} · {member.Member.PublicNo} · {amount} moved to the master folio · folio {N(folioId)} receipt {N(receiptId)}", c).ConfigureAwait(false);
            await tx.PublishAsync("GroupMemberTransferred", s.Group.GroupId, new { groupId = s.Group.GroupId, reservationId = req.ReservationId, folioId, receiptId, entryId, amountPaise = amount.Value }, c).ConfigureAwait(false);
            return new GroupTransferResult(s.Group.GroupId, req.ReservationId, entryId, receiptId, amount.Value, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Master folio receipts ──

    public async Task<CommandResult<GroupReceiptResult>> RecordReceiptAsync(CommandContext ctx, RecordGroupReceiptRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.Kind is not ("payment" or "deposit" or "refund")) throw HccException.Validation("Receipt type must be payment, deposit or refund.");
        var amount = Paise.FromWire(req.AmountPaise).EnsureReceiptAmount();
        var reference = GroupText.Optional(req.Reference, 120) ?? string.Empty;
        var tender = Tenders.Validate(req.Tender, reference);
        if (tender == Tenders.Cash)
            throw HccException.Validation("The master folio takes UPI, card or bank transfer. Take cash on a member folio (the shared till), or ask for the Billing group-cash port.");
        if (req.Kind == "refund")
        {
            if (req.RefundOf is null) throw HccException.Validation("Select the master receipt to refund.");
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Approve, amountPaise: amount.Value, ct: ct).ConfigureAwait(false);
        }
        else
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Payment, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        }

        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var s = await OpenGroupAsync(tx, ctx, req.GroupId, null, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureCashBookOpenAsync(tx, ctx.PropertyId, s.BusinessDate, "a group receipt", c).ConfigureAwait(false);
            var ledger = GroupLedger.Build(s);
            string label;
            switch (req.Kind)
            {
                case "payment":
                    if (amount > ledger.Master.Balance)
                        throw HccException.Validation($"Payment cannot exceed the master balance due ({Clamp(ledger.Master.Balance)}). Record an advance as a deposit.");
                    label = $"Payment · {tender}";
                    break;
                case "deposit":
                    label = $"Deposit (advance) · {tender}";
                    break;
                default:
                {
                    var parent = s.Entries.FirstOrDefault(e => e.EntryId == req.RefundOf && e.Kind is "payment" or "deposit")
                        ?? throw NotFound("Select a payment or deposit of this group's master folio to refund.");
                    var refunded = s.Entries.Where(e => e.Kind == "refund" && e.RefundOf == parent.EntryId).Aggregate(Paise.Zero, (a, e) => a + e.Amount);
                    if (amount > parent.Amount - refunded) throw HccException.Validation($"Refund cannot exceed the rest of receipt {N(parent.EntryId)} ({parent.Amount - refunded}).");
                    var credit = -ledger.Master.Balance;
                    if (amount > credit) throw Invalid($"Only a credit balance can be refunded (credit {Clamp(credit)}).");
                    label = $"Refund of receipt {N(parent.EntryId)} · {tender}";
                    break;
                }
            }

            var entryId = await store.InsertEntryAsync(tx, new NewEntry(s.Group.GroupId, req.Kind, s.BusinessDate, null, label, amount, null, null, null, null, null, null, null,
                tender, reference, null, null, null, null, req.Kind == "refund" ? req.RefundOf : null, ctx.PrincipalId, Now), c).ConfigureAwait(false);
            var version = await store.TouchGroupAsync(tx, s.Group.GroupId, c).ConfigureAwait(false);
            await tx.AuditAsync("GROUP_RECEIPT_RECORDED", Target(s.Group.GroupId), $"{s.Group.Code} · {req.Kind} · {tender} · {amount} · ref {reference} · manual entry, not verified", c).ConfigureAwait(false);
            await tx.PublishAsync("GroupReceiptRecorded", s.Group.GroupId, new { groupId = s.Group.GroupId, entryId, kind = req.Kind, tender, amountPaise = amount.Value, businessDate = s.BusinessDate }, c).ConfigureAwait(false);
            return new GroupReceiptResult(s.Group.GroupId, entryId, req.Kind, tender, amount.Value, s.BusinessDate, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Attrition ──

    public async Task<CommandResult<GroupAttritionResult>> RequestAttritionAsync(CommandContext ctx, RequestGroupAttritionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        Version(req.ExpectedVersion);
        var reason = GroupText.Required(req.Reason, 200, "Give a reason (e.g. the contract clause) for the attrition charge.");
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var s = await OpenGroupAsync(tx, ctx, req.GroupId, req.ExpectedVersion, c).ConfigureAwait(false);
            if (s.Attritions.Any(a => a.Status == "Pending")) throw Invalid("An attrition request for this group is already pending.");
            if (s.Attritions.Any(a => a.Status == "Posted")) throw Invalid("Attrition was already posted for this group.");
            var priced = await PriceAttritionAsync(tx, s, c).ConfigureAwait(false);
            var detail = JsonSerializer.Serialize(priced.Select(p => new
            {
                roomType = p.Line.RoomType, night = p.Line.Night, rooms = p.Line.Rooms, perRoomNightPaise = p.Line.PerRoomNight.Value, netPaise = p.Line.Amount.Value,
                taxBps = p.Rate.Bps, taxPaise = p.Tax.Value, itc = p.Rate.Itc, rule = p.Rate.Rule,
            }), CanonicalJson.SerializerOptions);
            var net = priced.Aggregate(Paise.Zero, (a, p) => a + p.Line.Amount);
            var tax = priced.Aggregate(Paise.Zero, (a, p) => a + p.Tax);
            var id = await store.InsertAttritionAsync(tx, new NewAttrition(s.Group.GroupId, net, tax, detail, s.Group.Version, reason, ctx.PrincipalId, Now), c).ConfigureAwait(false);
            var version = await store.TouchGroupAsync(tx, s.Group.GroupId, c).ConfigureAwait(false);
            await tx.AuditAsync("GROUP_ATTRITION_REQUESTED", "attrition:" + N(id),
                $"{s.Group.Code} · {priced.Count} line(s) · {net} + GST {tax} · {GstMath.Percent(s.Group.AttritionRateBps)} beyond {GstMath.Percent(s.Group.AllowanceBps)} free · {reason}", c).ConfigureAwait(false);
            await tx.PublishAsync("GroupAttritionRequested", s.Group.GroupId, new { groupId = s.Group.GroupId, attritionId = id, netPaise = net.Value, taxPaise = tax.Value }, c).ConfigureAwait(false);
            return new GroupAttritionResult(id, s.Group.GroupId, "Pending", net.Value, tax.Value, [], version);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Approve (posts the lines on the master folio) or reject (waives). A different user with PERM-GRP-003; the Manager within the limit.</summary>
    public async Task<CommandResult<GroupAttritionResult>> DecideAttritionAsync(CommandContext ctx, DecideGroupAttritionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var note = GroupText.Optional(req.Reason, 200);
        if (!req.Approve && note is null) throw HccException.Validation("Give a reason for waiving the attrition charge.");
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var groupId = await store.AttritionGroupAsync(tx, req.AttritionId, c).ConfigureAwait(false) ?? throw NotFound("Attrition request not found.");
            var s = await OpenGroupAsync(tx, ctx, groupId, null, c).ConfigureAwait(false);
            var a = await store.LockAttritionAsync(tx, req.AttritionId, c).ConfigureAwait(false) ?? throw NotFound("Attrition request not found.");
            if (a.Status != "Pending") throw Invalid("This attrition request is no longer pending.");
            if (a.RequestedBy == ctx.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot decide your own request (PERM-SOD-001).");
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Approve, amountPaise: (a.Net + a.Tax).Value, ct: c).ConfigureAwait(false);
            var ids = new List<long>();
            if (req.Approve)
            {
                var priced = await PriceAttritionAsync(tx, s, c).ConfigureAwait(false);
                var net = priced.Aggregate(Paise.Zero, (x, p) => x + p.Line.Amount);
                var tax = priced.Aggregate(Paise.Zero, (x, p) => x + p.Tax);
                if (net != a.Net || tax != a.Tax) throw new HccException(HccErrorCode.STALE_VERSION, "The block pickup changed after this request. Reject it and request again.");
                foreach (var p in priced)
                {
                    var split = GstMath.Split(p.Tax);
                    ids.Add(await store.InsertEntryAsync(tx, new NewEntry(s.Group.GroupId, "attrition", s.BusinessDate, p.Line.Night,
                        $"Attrition · {p.Line.Rooms} unpicked {p.Line.RoomType} × {p.Line.PerRoomNight} · {p.Line.Night:yyyy-MM-dd}", p.Line.Amount, GstCategories.Code(GstCategory.Room),
                        p.Rate.Bps, p.Rate.Itc, p.Tax, split.Cgst, split.Sgst, Clip(p.Rate.Rule, 80), null, null, null, null, null, a.AttritionId, null, ctx.PrincipalId, Now), c).ConfigureAwait(false));
                }
            }

            await store.DecideAttritionAsync(tx, a.AttritionId, req.Approve ? "Posted" : "Rejected", ctx.PrincipalId, Now, note, c).ConfigureAwait(false);
            var version = await store.TouchGroupAsync(tx, s.Group.GroupId, c).ConfigureAwait(false);
            await tx.AuditAsync("GROUP_ATTRITION_DECIDED", "attrition:" + N(a.AttritionId),
                $"{s.Group.Code} · {(req.Approve ? $"posted {a.Net} + GST {a.Tax} on the master folio ({ids.Count} line(s))" : "waived")}" + (note is null ? "" : " · " + note), c).ConfigureAwait(false);
            if (req.Approve)
            {
                var split = GstSplitOf(a.Tax, a.DetailJson);
                await tx.PublishAsync("GroupAttritionPosted", s.Group.GroupId, new { groupId = s.Group.GroupId, attritionId = a.AttritionId, entryIds = ids, netPaise = a.Net.Value,
                    taxPaise = a.Tax.Value, cgstPaise = split.Cgst, sgstPaise = split.Sgst, businessDate = s.BusinessDate }, c).ConfigureAwait(false);
            }

            return new GroupAttritionResult(a.AttritionId, s.Group.GroupId, req.Approve ? "Posted" : "Rejected", a.Net.Value, a.Tax.Value, ids, version);
        }, ct).ConfigureAwait(false);
    }

    private static (long Cgst, long Sgst) GstSplitOf(Paise tax, string detailJson)
    {
        // Per-line split summed (never on the total): recompute from the stored per-line tax in the request detail.
        using var doc = JsonDocument.Parse(detailJson);
        long cgst = 0, sgst = 0;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var s = GstMath.Split(new Paise(e.GetProperty("taxPaise").GetInt64()));
            cgst += s.Cgst.Value;
            sgst += s.Sgst.Value;
        }

        return cgst + sgst == tax.Value ? (cgst, sgst) : throw new InvalidOperationException("Attrition tax split disagrees with its lines.");
    }

    private sealed record PricedAttrition(AttritionLine Line, GstRate Rate, Paise Tax);

    private async Task<IReadOnlyList<PricedAttrition>> PriceAttritionAsync(ICommandTransaction tx, GroupSnapshot s, CancellationToken ct)
    {
        var block = s.Block ?? throw Invalid("This group has no room block; attrition applies to unpicked block rooms only.");
        if (GroupPickup.EffectiveStatus(block.Status, block.Cutoff, s.BusinessDate) == "Open")
            throw Invalid($"Pickup is still open (cut-off {block.Cutoff:yyyy-MM-dd}); attrition applies after the cut-off or once the block is released.");
        if (s.Group.AttritionRateBps == 0) throw Invalid("This group has no attrition terms (attrition rate 0 %).");
        var inputs = block.Types.Select(t => new AttritionInput(t.RoomType, t.Allotted + t.Released, t.Picked,
            t.RatePaise is { } r ? new Paise(r) : throw Invalid($"Set the block rate for {t.RoomType} before charging attrition."))).ToList();
        var lines = GroupAttrition.Lines(inputs, block.Arrival, block.Departure, s.Group.AllowanceBps, s.Group.AttritionRateBps);
        if (lines.Count == 0) throw Invalid("No unpicked rooms beyond the free allowance; there is nothing to charge.");
        var priced = new List<PricedAttrition>();
        foreach (var l in lines)
        {
            var rate = await gst.RoomNightAsync(tx, s.Group.PropertyId, l.Night, l.PerRoomNight, ct).ConfigureAwait(false); // per unit per night
            priced.Add(new PricedAttrition(l, rate, GstMath.LineTax(l.Amount, rate.Bps)));
        }

        return priced;
    }

    // ── Settlement ──

    public async Task<CommandResult<GroupResult>> SettleAsync(CommandContext ctx, SettleGroupRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        Version(req.ExpectedVersion);
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var s = await OpenGroupAsync(tx, ctx, req.GroupId, req.ExpectedVersion, c).ConfigureAwait(false);
            if (s.Block is { } b && GroupPickup.EffectiveStatus(b.Status, b.Cutoff, s.BusinessDate) == "Open")
                throw Invalid($"Pickup on block {b.Code} is still open; release it or wait for the cut-off.");
            var live = s.Members.Where(m => m.Status is "Held" or "Confirmed" or "InHouse").Select(m => m.PublicNo).ToList();
            if (live.Count > 0) throw Invalid($"Check out or cancel the member booking(s) first ({string.Join(", ", live)}).");
            if (s.Attritions.Any(a => a.Status == "Pending")) throw Invalid("Decide the pending attrition request first.");
            var ledger = GroupLedger.Build(s);
            if (ledger.UnroutedLines > 0) throw Invalid($"{ledger.UnroutedLines} posted line(s) are not routed yet. Apply the routing first.");
            var pending = ledger.Members.Where(m => m.PendingTransfer != Paise.Zero).Select(m => m.Member.PublicNo).ToList();
            if (pending.Count > 0) throw Invalid($"The master share of {string.Join(", ", pending)} was settled on the guest folio; re-route those lines to the guest.");
            if (ledger.Master.Balance != Paise.Zero)
                throw Invalid(ledger.Master.Balance > Paise.Zero ? $"Collect the master balance due ({ledger.Master.Balance}) first." : $"Refund the master credit ({-ledger.Master.Balance}) first.");
            var version = await store.SettleGroupAsync(tx, s.Group.GroupId, req.ExpectedVersion, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("GROUP_SETTLED", Target(s.Group.GroupId), $"{s.Group.Code} · master charges {ledger.Master.Charges} · received {ledger.Master.Received} · balance 0", c).ConfigureAwait(false);
            await tx.PublishAsync("GroupSettled", s.Group.GroupId, new { groupId = s.Group.GroupId, chargesPaise = ledger.Master.Charges.Value, receivedPaise = ledger.Master.Received.Value }, c).ConfigureAwait(false);
            return new GroupResult(s.Group.GroupId, s.Group.Code, "Settled", version);
        }, ct).ConfigureAwait(false);
    }

    // ── helpers ──

    private async Task OpenDayAsync(ICommandTransaction tx, CancellationToken ct)
    {
        if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, tx.Context.PropertyId, ct).ConfigureAwait(false); // DAY_CLOSED (P6)
    }

    /// <summary>Day open → group locked (property, version, Open) → snapshot inside the transaction.</summary>
    private async Task<GroupSnapshot> OpenGroupAsync(ICommandTransaction tx, CommandContext ctx, long groupId, string? expectedVersion, CancellationToken ct)
    {
        await OpenDayAsync(tx, ct).ConfigureAwait(false);
        var g = await store.LockGroupAsync(tx, groupId, ct).ConfigureAwait(false);
        if (g is null || g.PropertyId != ctx.PropertyId) throw NotFound("Group not found.");
        if (expectedVersion is not null && !string.Equals(g.Version, expectedVersion, StringComparison.Ordinal))
            throw new HccException(HccErrorCode.STALE_VERSION, "The group was changed by someone else. Reload and try again.");
        if (g.Status != "Open") throw Invalid("This group is settled; its master folio is closed.");
        return await store.LoadAsync(tx, ctx.PropertyId, groupId, ct).ConfigureAwait(false) ?? throw NotFound("Group not found.");
    }

    private static GroupDetails Details(string? name, string? organiser, string? billingName, string? contact, string? phone, string? email, string? address,
        string? gstin, int allowanceBps, int rateBps)
        => new(GroupText.Required(name, 200, "Enter a name for the group."), GroupText.Required(organiser, 120, "Enter the organiser."),
            GroupText.Required(billingName, 200, "Enter the name to bill (as on the invoice)."), GroupText.Required(contact, 120, "Enter the billing contact."),
            GroupText.Phone(phone), GroupText.Email(email), GroupText.Optional(address, 400), GroupText.Gstin(gstin),
            GroupText.Bps(allowanceBps, "The free attrition allowance"), GroupText.Bps(rateBps, "The attrition rate"));

    private static void Version(string? token)
    {
        var buffer = new byte[12];
        if (string.IsNullOrEmpty(token) || !Convert.TryFromBase64String(token, buffer, out var n) || n != 8)
            throw HccException.Validation("expectedVersion is not a valid version token.");
    }

    private static string RulesText(IReadOnlyDictionary<RouteCategory, RouteTarget> rules)
        => string.Join(", ", rules.OrderBy(r => r.Key).Select(r => $"{GroupCodes.Code(r.Key)} → {GroupCodes.Code(r.Value)}"));

    private static Paise Clamp(Paise p) => p < Paise.Zero ? Paise.Zero : p;

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    internal static string Target(long groupId) => "group:" + N(groupId);

    internal static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);

    internal static HccException NotFound(string message) => new(HccErrorCode.NOT_FOUND, message);
}
