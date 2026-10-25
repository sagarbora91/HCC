using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Groups;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;

namespace Hcc.Application.Groups;

/// <summary>
/// Group list (PERM-GRP-001) and the group statement (PERM-GRP-004): per member, per night, every posted line with its GST snapshot
/// (rate, tax, CGST + SGST per line) and route; master lines and receipts; pickup per room type; the prototype estimate
/// (<c>grpMasterFolio</c> / <c>grpBookingExposure</c>).
/// </summary>
public sealed class GroupQueries(IGroupStore store, IAccessPolicy policy, IGstRateResolver gst)
{
    // ETP-REUSE: printable group statement / rooming list PDF and Excel via the ETP exporters when V:\ETP is available.
    public async Task<IReadOnlyList<GroupSummaryView>> ListAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, GroupPermissions.View, ct: ct).ConfigureAwait(false);
        return (await store.ListAsync(propertyId, ct).ConfigureAwait(false))
            .Select(g => new GroupSummaryView(g.GroupId, g.Code, g.Name, g.Status, g.GroupBlockId, g.BlockCode, g.Arrival, g.Departure, g.Members, g.Version)).ToList();
    }

    public async Task<GroupStatementView> StatementAsync(int propertyId, int principalId, long groupId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, GroupPermissions.MoneyView, ct: ct).ConfigureAwait(false);
        var s = await store.LoadAsync(null, propertyId, groupId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Group not found.");
        var ledger = GroupLedger.Build(s, await GroupLedger.AgreedAsync(s, gst, null, ct).ConfigureAwait(false));
        return Statement(ledger);
    }

    public static GroupStatementView Statement(GroupLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var s = ledger.Snapshot;
        var g = s.Group;
        static GroupRuleView Rule(RoutingRule r) => new(r.ReservationId, GroupCodes.Code(r.Category), GroupCodes.Code(r.Target));
        var members = ledger.Members.Select(m =>
        {
            var nights = m.Lines.GroupBy(l => l.Charge.Line.ServiceDate).OrderBy(x => x.Key).Select(n =>
            {
                var lines = n.Select(l => new GroupLineView(l.Charge.Line.ChargeId, BillingCodes.Code(l.Charge.Line.Kind), l.Charge.Line.ServiceDate, l.Charge.Line.Label,
                    l.Charge.Line.Amount.Value, l.Charge.Line.TaxBps, l.Charge.Line.Tax.Value, l.Charge.Cgst.Value, l.Charge.Sgst.Value, l.Charge.GstCategory,
                    l.Charge.Line.ReversesChargeId, GroupCodes.Code(l.Route.Net), GroupCodes.Code(l.Route.Tax), l.Routed, l.Seq, l.Shares.Master.Value, l.Shares.Guest.Value)).ToList();
                return new GroupNightView(n.Key, lines, lines.Sum(l => l.MasterPaise), lines.Sum(l => l.GuestPaise));
            }).ToList();
            return new GroupMemberView(m.Member.ReservationId, m.Member.PublicNo, m.Member.Guest, m.Member.Room, m.Member.Status, m.Member.Arrival, m.Member.Departure,
                m.Member.FolioId, m.Member.Source, s.Rules.Where(r => r.ReservationId == m.Member.ReservationId).Select(Rule).ToList(), nights, m.Net.Value, m.Tax.Value,
                (m.Net + m.Tax).Value, m.MasterShare.Value, m.GuestShare.Value, m.Transferred.Value, m.PendingTransfer.Value, m.Folio?.Paid.Value ?? 0,
                m.Folio?.Balance.Value ?? 0, m.Estimate.Value);
        }).ToList();
        var pickup = s.Block is { } b
            ? b.Types.Select(t => GroupPickup.Line(new BlockTypeCounts(t.RoomType, t.Allotted, t.Picked, t.Released, new Paise(t.RatePaise ?? 0)), b.Status, b.Cutoff, s.BusinessDate))
                .Select(l => new GroupPickupView(l.RoomType, l.Allotted, l.Picked, l.Released, l.Remaining, l.Rate.Value, l.PickedPercent)).ToList()
            : [];
        var master = s.Entries.OrderBy(e => e.EntryId).Select(e => new GroupMasterLineView(e.EntryId, e.Kind, e.BusinessDate, e.ServiceDate, e.Label, e.Amount.Value, e.TaxBps,
            e.Tax?.Value, e.Cgst?.Value, e.Sgst?.Value, e.Tender, e.Reference, e.ReservationId)).ToList();
        var attrition = s.Attritions.Select(a => new GroupAttritionView(a.AttritionId, a.Status, a.Net.Value, a.Tax.Value, a.Reason, a.RequestedBy, a.DecidedBy, a.DetailJson)).ToList();
        var active = ledger.Members.Where(m => GroupEstimate.IsActive(m.Member.Status)).ToList();
        var f = ledger.Master;
        var totals = new GroupTotalsView(f.Net.Value, f.Tax.Value, ledger.MasterCgst.Value, ledger.MasterSgst.Value, f.Charges.Value,
            s.Entries.Where(e => e.Kind == "payment").Sum(e => e.Amount.Value), f.Deposits.Value, f.Refunded.Value, f.Balance.Value, ledger.Transferred.Value,
            ledger.PendingTransfer.Value, ledger.Members.Sum(m => m.GuestShare.Value), active.Sum(m => m.Folio?.Paid.Value ?? 0), active.Sum(m => m.Estimate.Value),
            ledger.UnroutedLines);
        return new GroupStatementView(g.GroupId, g.Code, g.Name, g.Status, g.Organiser, g.BillingName, g.BillingContact, g.Gstin, g.GroupBlockId, s.Block?.Code,
            s.Block is { } bb ? GroupPickup.EffectiveStatus(bb.Status, bb.Cutoff, s.BusinessDate) : null, s.Block?.Cutoff, s.BusinessDate,
            s.Rules.Where(r => r.ReservationId is null).Select(Rule).ToList(), pickup, members, master, attrition, totals, g.Version);
    }
}

/// <summary>The W3-6 read port over the group store: persisted routes only (preview routes are not invoiceable).</summary>
public sealed class GroupInvoiceSource(IGroupStore store) : IGroupInvoiceSource
{
    public async Task<GroupInvoiceData?> GetAsync(ICommandTransaction? tx, int propertyId, long groupId, CancellationToken ct = default)
    {
        var s = await store.LoadAsync(tx, propertyId, groupId, ct).ConfigureAwait(false);
        if (s is null) return null;
        var ledger = GroupLedger.Build(s);
        var lines = new List<GroupInvoiceLine>();
        Paise net = Paise.Zero, tax = Paise.Zero, cgst = Paise.Zero, sgst = Paise.Zero;
        foreach (var m in ledger.Members)
            foreach (var l in m.Lines.Where(l => l.Routed && (l.Route.Net == RouteTarget.Master || l.Route.Tax == RouteTarget.Master)))
            {
                var c = l.Charge;
                lines.Add(new GroupInvoiceLine("folio-charge", c.Line.ChargeId, m.Member.ReservationId, m.Member.PublicNo, m.Member.Guest, m.Member.Room, c.Line.ServiceDate,
                    BillingCodes.Code(c.Line.Kind), c.Line.Label, c.GstCategory, c.Itc, c.Line.Amount.Value, c.Line.TaxBps, c.Line.Tax.Value, c.Cgst.Value, c.Sgst.Value,
                    l.Route.Net == RouteTarget.Master, l.Route.Tax == RouteTarget.Master, l.Shares.Master.Value, c.Line.ReversesChargeId));
                if (l.Route.Net == RouteTarget.Master) net += c.Line.Amount;
                if (l.Route.Tax == RouteTarget.Master)
                {
                    tax += c.Line.Tax;
                    cgst += c.Cgst;
                    sgst += c.Sgst;
                }
            }

        foreach (var e in s.Entries.Where(e => e.Kind == "attrition").OrderBy(e => e.EntryId))
        {
            lines.Add(new GroupInvoiceLine("attrition", e.EntryId, null, null, null, null, e.ServiceDate!.Value, "attrition", e.Label, e.GstCategory ?? "room", e.Itc ?? false,
                e.Amount.Value, e.TaxBps ?? 0, e.Tax?.Value ?? 0, e.Cgst?.Value ?? 0, e.Sgst?.Value ?? 0, true, true, (e.Amount + (e.Tax ?? Paise.Zero)).Value, null));
            net += e.Amount;
            tax += e.Tax ?? Paise.Zero;
            cgst += e.Cgst ?? Paise.Zero;
            sgst += e.Sgst ?? Paise.Zero;
        }

        var g = s.Group;
        var received = ledger.Master.Received;
        return new GroupInvoiceData(g.GroupId, g.Code, g.Status, new GroupInvoiceParty(g.Code, g.Name, g.BillingName, g.BillingContact, g.BillingPhone, g.BillingEmail,
                g.BillingAddress, g.Gstin), s.Block?.Code, s.Block?.Arrival, s.Block?.Departure, lines, net.Value, tax.Value, cgst.Value, sgst.Value, (net + tax).Value,
            received.Value, (net + tax - received).Value, ledger.UnroutedLines, g.Version);
    }

    public async Task<IReadOnlyList<MasterRoutedShare>> MasterSharesForFolioAsync(ICommandTransaction? tx, int propertyId, long folioId, CancellationToken ct = default)
    {
        if (await store.GroupOfFolioAsync(tx, propertyId, folioId, ct).ConfigureAwait(false) is not { } groupId) return [];
        var s = await store.LoadAsync(tx, propertyId, groupId, ct).ConfigureAwait(false);
        if (s is null) return [];
        return GroupLedger.Build(s).Members.SelectMany(m => m.Lines)
            .Where(l => l.Charge.FolioId == folioId && l.Routed && (l.Route.Net == RouteTarget.Master || l.Route.Tax == RouteTarget.Master))
            .Select(l => new MasterRoutedShare(l.Charge.Line.ChargeId, groupId, s.Group.Code, l.Route.Net == RouteTarget.Master, l.Route.Tax == RouteTarget.Master, l.Shares.Master.Value))
            .ToList();
    }
}
