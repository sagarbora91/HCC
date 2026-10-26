using Hcc.Contracts.Pos;
using Hcc.Pos.Tests.Harness;

namespace Hcc.Pos.Tests.Sql;

/// <summary>Reads the web client's POS screens need (W4-8): the shared till, pending void requests, closed checks and the check timing fields.</summary>
public class PosSyncReadTests(PosDb db) : IClassFixture<PosDb>
{
    [Fact]
    public async Task Till_read_reports_no_till_then_a_closed_then_an_open_shift()
    {
        var none = await db.Queries.TillAsync(db.As(Who.Cashier));
        Assert.Null(none.TillId);
        Assert.False(none.ShiftOpen);
        var (till, shift) = await db.OpenTillAsync(100_000);
        var open = await db.Queries.TillAsync(db.As(Who.Cashier));
        Assert.Equal(till, open.TillId);
        Assert.Equal(shift.ShiftId, open.ShiftId);
        Assert.True(open.ShiftOpen);
    }

    [Fact]
    public async Task Pending_void_requests_carry_amount_reason_spot_and_requester_until_decided()
    {
        var id = await db.SentTableAsync("T36", ("THALI", 4));
        var line = (await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Lines[0].LineId;
        var req = (await db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, line, "Kitchen error", null))).Value;
        var list = await db.Queries.PendingVoidsAsync(db.As(Who.Manager));
        var v = Assert.Single(list, x => x.VoidRequestId == req.VoidRequestId);
        Assert.Equal(("Table T36", "item", line, "Kitchen error", Who.Cashier), (v.Spot, v.Scope, v.LineId, v.Reason, v.RequestedBy));
        Assert.Equal(168_000, v.AmountPaise);
        Assert.Equal((await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Version, v.OrderVersion);
        await db.Pos.DecideVoidAsync(db.As(Who.Owner), PosDb.Key(), new DecideVoidRequest(req.VoidRequestId!.Value, false, "no"));
        Assert.DoesNotContain(await db.Queries.PendingVoidsAsync(db.As(Who.Manager)), x => x.VoidRequestId == req.VoidRequestId);
    }

    [Fact]
    public async Task Closed_checks_list_settled_and_void_checks_with_bill_and_timing_and_never_open_ones()
    {
        var (till, _) = await db.OpenTillAsync(0);
        var settled = await db.SentTableAsync("T37", ("TEA", 2));
        await db.SettleOutletAsync(settled, "Cash", till);
        var voided = await db.SentTableAsync("T38", ("TEA", 1));
        await db.Pos.VoidBillAsync(db.As(Who.Cashier), PosDb.Key(), new VoidBillRequest(voided, "Walked out", null));
        var open = await db.SentTableAsync("T35", ("TEA", 1));
        var closed = await db.Queries.ClosedChecksAsync(db.As(Who.Cashier), null);
        Assert.DoesNotContain(closed, c => c.OrderId == open);
        var s = Assert.Single(closed, c => c.OrderId == settled);
        Assert.Equal(("Settled", "Cash"), (s.Status, s.Bill!.Tender));
        Assert.NotNull(s.Bill.SettledUtc);
        Assert.Equal(Who.Cashier, s.Bill.SettledBy);
        Assert.NotNull(s.OpenedUtc);
        Assert.Equal(Who.Cashier, s.OpenedBy);
        Assert.All(s.Kots, k => Assert.Equal(Who.Cashier, k.CreatedBy));
        var v = Assert.Single(closed, c => c.OrderId == voided);
        Assert.Equal(("Void", "Walked out"), (v.Status, v.VoidReason));
        Assert.NotNull(v.VoidedUtc);
        Assert.Empty(await db.Queries.ClosedChecksAsync(db.As(Who.Cashier), PosDb.Day.AddDays(-3)));
    }

    [Fact]
    public async Task A_room_check_and_its_folio_bill_carry_the_reservation_id()
    {
        var g = await db.NewInHouseAsync("321", guest: "Guest C");
        var o = (await db.Pos.OpenCheckAsync(db.As(Who.Reception), PosDb.Key(), new OpenCheckRequest(db.RoomService, null, "321", 1, null))).Value;
        await db.AddAsync(o.OrderId, "TEA", 1, Who.Reception);
        await db.SendAsync(o.OrderId, Who.Reception);
        Assert.Equal(g.ReservationId, (await db.Queries.GetCheckAsync(db.As(Who.Reception), o.OrderId)).ReservationId);
        await db.SettleFolioAsync(o.OrderId, null, Who.Reception);
        var done = await db.Queries.GetCheckAsync(db.As(Who.Reception), o.OrderId);
        Assert.Equal(("folio", g.ReservationId), (done.Bill!.Mode, done.Bill.ReservationId));
    }

    [Fact]
    public async Task In_house_read_lists_only_in_house_stays_with_reservation_folio_and_room()
    {
        var a = await db.NewInHouseAsync("311", guest: "Guest A");
        var gone = await db.NewInHouseAsync("312", inHouse: false, guest: "Guest B");
        var list = await db.Queries.InHouseAsync(db.As(Who.Cashier));
        var s = Assert.Single(list, x => x.RoomNo == "311");
        Assert.Equal((a.StayId, a.FolioId, a.ReservationId, "Guest A"), (s.StayId, s.FolioId, s.ReservationId, s.Guest));
        Assert.DoesNotContain(list, x => x.StayId == gone.StayId);
    }
}
