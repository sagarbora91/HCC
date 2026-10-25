using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Domain.Billing;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;

namespace Hcc.Application.Groups;

/// <summary>One member line with its effective route: the persisted one (<see cref="Routed"/>) or the preview the rules would give.</summary>
public sealed record LedgerLine(MemberCharge Charge, LineRoute Route, bool Routed, int Seq, LineShares Shares)
{
    public bool IsReversal => Charge.Line.ReversesChargeId is not null;
}

public sealed record LedgerMember(MemberRow Member, IReadOnlyList<LedgerLine> Lines, Paise Net, Paise Tax, Paise MasterShare, Paise GuestShare,
    Paise Transferred, FolioFigures? Folio, Paise Estimate)
{
    public Paise PendingTransfer => MasterShare - Transferred;

    /// <summary>The guest folio still takes receipts (Billing <c>FolioHeader.IsOpen</c>: Confirmed or InHouse).</summary>
    public bool FolioOpen => Member.Status is "Confirmed" or "InHouse";
}

/// <summary>
/// The group's billing picture computed from one <see cref="GroupSnapshot"/>: effective routes, member shares, transfers and the
/// master folio figures. Pure (no I/O) apart from <see cref="AgreedAsync"/>, which prices nights that have no P2 snapshot.
/// </summary>
public sealed class GroupLedger
{
    private GroupLedger(GroupSnapshot snapshot, IReadOnlyList<LedgerMember> members, MasterFigures master, Paise attritionCgst, Paise attritionSgst,
        Paise routedCgst, Paise routedSgst)
    {
        Snapshot = snapshot;
        Members = members;
        Master = master;
        MasterCgst = attritionCgst + routedCgst;
        MasterSgst = attritionSgst + routedSgst;
    }

    public GroupSnapshot Snapshot { get; }

    public IReadOnlyList<LedgerMember> Members { get; }

    public MasterFigures Master { get; }

    public Paise MasterCgst { get; }

    public Paise MasterSgst { get; }

    public int UnroutedLines => Members.Sum(m => m.Lines.Count(l => !l.Routed));

    public Paise Transferred => Members.Aggregate(Paise.Zero, (a, m) => a + m.Transferred);

    public Paise PendingTransfer => Members.Aggregate(Paise.Zero, (a, m) => a + m.PendingTransfer);

    /// <summary>Agreed nights per member: the P2 snapshot, else the booked nightly rate priced by the GST master per night.</summary>
    public static async Task<IReadOnlyDictionary<long, IReadOnlyList<(DateOnly Night, Paise Rate, int Bps)>>> AgreedAsync(GroupSnapshot s, IGstRateResolver gst,
        ICommandTransaction? tx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(gst);
        var map = new Dictionary<long, IReadOnlyList<(DateOnly, Paise, int)>>();
        foreach (var m in s.Members)
        {
            var own = s.Agreed.Where(a => a.ReservationId == m.ReservationId).OrderBy(a => a.Night).Select(a => (a.Night, a.Rate, a.TaxBps)).ToList();
            if (own.Count == 0 && m.NightlyRatePaise is { } rate && GroupEstimate.IsActive(m.Status) && m.Status != "Departed")
                own = (await gst.RoomNightsAsync(tx, s.Group.PropertyId, m.Arrival, m.Departure, new Paise(rate), ct).ConfigureAwait(false))
                    .Select(n => (n.Night, new Paise(rate), n.Rate.Bps)).ToList();
            map[m.ReservationId] = own;
        }

        return map;
    }

    public static GroupLedger Build(GroupSnapshot s, IReadOnlyDictionary<long, IReadOnlyList<(DateOnly Night, Paise Rate, int Bps)>>? agreed = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        var routes = s.Routes.ToDictionary(r => r.ChargeId);
        var members = new List<LedgerMember>();
        Paise rNet = Paise.Zero, rTax = Paise.Zero, rCgst = Paise.Zero, rSgst = Paise.Zero;
        foreach (var m in s.Members.OrderBy(m => m.PublicNo, StringComparer.Ordinal))
        {
            var charges = s.Charges.Where(c => c.ReservationId == m.ReservationId).OrderBy(c => c.Line.ChargeId).ToList();
            var effective = new Dictionary<long, (LineRoute Route, bool Routed, int Seq)>();
            var lines = new List<LedgerLine>();
            foreach (var c in charges)
            {
                (LineRoute Route, bool Routed, int Seq) e;
                if (routes.TryGetValue(c.Line.ChargeId, out var r)) e = (new LineRoute(r.Net, r.Tax), true, r.Seq);
                else if (c.Line.ReversesChargeId is { } orig && effective.TryGetValue(orig, out var o)) e = (o.Route, false, 0);
                else if (c.Line.ReversesChargeId is not null) e = (new LineRoute(RouteTarget.Guest, RouteTarget.Guest), false, 0);
                else e = (GroupRouting.ForLine(s.Rules, m.ReservationId, c.Line.Kind), false, 0);
                effective[c.Line.ChargeId] = e;
                var shares = GroupRouting.Shares(c.Line.Amount, c.Line.Tax, e.Route);
                lines.Add(new LedgerLine(c, e.Route, e.Routed, e.Seq, shares));
                if (e.Route.Net == RouteTarget.Master) rNet += c.Line.Amount;
                if (e.Route.Tax == RouteTarget.Master)
                {
                    rTax += c.Line.Tax;
                    rCgst += c.Cgst;
                    rSgst += c.Sgst;
                }
            }

            var net = lines.Aggregate(Paise.Zero, (a, l) => a + l.Charge.Line.Amount);
            var tax = lines.Aggregate(Paise.Zero, (a, l) => a + l.Charge.Line.Tax);
            var master = lines.Aggregate(Paise.Zero, (a, l) => a + l.Shares.Master);
            var transferred = s.Entries.Where(e => e.Kind == "transfer" && e.ReservationId == m.ReservationId).Aggregate(Paise.Zero, (a, e) => a + e.Amount);
            var receipts = s.Receipts.Where(r => r.ReservationId == m.ReservationId).Select(r => r.Receipt).ToList();
            FolioFigures? folio = m.FolioId is null ? null : FolioStatement.Compute(charges.Select(c => c.Line).ToList(), receipts);
            var estimate = GroupEstimate.Exposure(m.Status, charges.Select(c => c.Line).ToList(),
                agreed is not null && agreed.TryGetValue(m.ReservationId, out var a) ? a : [], folio?.Paid ?? Paise.Zero);
            members.Add(new LedgerMember(m, lines, net, tax, master, net + tax - master, transferred, folio, estimate));
        }

        var attr = s.Entries.Where(e => e.Kind == "attrition").ToList();
        Paise Sum(string kind) => s.Entries.Where(e => e.Kind == kind).Aggregate(Paise.Zero, (a, e) => a + e.Amount);
        var figures = GroupMaster.Figures(rNet, rTax, attr.Aggregate(Paise.Zero, (a, e) => a + e.Amount), attr.Aggregate(Paise.Zero, (a, e) => a + (e.Tax ?? Paise.Zero)),
            Sum("payment"), Sum("deposit"), Sum("refund"));
        return new GroupLedger(s, members, figures, attr.Aggregate(Paise.Zero, (a, e) => a + (e.Cgst ?? Paise.Zero)),
            attr.Aggregate(Paise.Zero, (a, e) => a + (e.Sgst ?? Paise.Zero)), rCgst, rSgst);
    }
}
