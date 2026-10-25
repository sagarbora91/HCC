using Hcc.Application.WebBooking;
using Hcc.Contracts.WebBooking;
using Hcc.WebBooking.Tests.Harness;

namespace Hcc.WebBooking.Tests.Sql;

/// <summary>
/// Web reservations on the real services over a disposable database. Golden (prototype channels.ts, DEC-069 GST): a Suite for two
/// nights at ₹4,500 is net 900000 + tax 45000 = 945000 paise.
/// </summary>
[Trait("Category", "Sql")]
public sealed class WebBookingSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Public_quote_golden_figures_and_reasons()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var q = await w.Web.QuoteAsync("2026-10-25", "2026-10-27", 2, 0, null, Ct);
        Assert.Equal("HCC Demo Residency", q.Hotel);
        var suite = q.RoomTypes.Single(t => t.RoomType == "Suite");
        Assert.True(suite.Bookable);
        Assert.Equal(2, suite.RoomsLeft);
        Assert.Equal(450_000, suite.NightlyRatePaise);
        Assert.Equal((900_000L, 45_000L, 945_000L), (suite.NetPaise, suite.TaxPaise, suite.TotalPaise));
        Assert.All(suite.NightRates, n => Assert.Equal((450_000L, 500, 22_500L), (n.RatePaise, n.TaxBps, n.TaxPaise)));
        Assert.Equal(["2026-10-25", "2026-10-26"], suite.NightRates.Select(n => n.Date));

        var classic = (await w.Web.QuoteAsync("2026-10-25", "2026-10-26", 3, 0, "classic", Ct)).RoomTypes.Single();
        Assert.False(classic.Bookable);
        Assert.Equal("Sleeps at most 2 guests including children", classic.Reason);

        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.Web.QuoteAsync("2026-10-20", "2026-10-21", 1, 0, null, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.Web.QuoteAsync("2026-10-25", "2026-10-25", 1, 0, null, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.Web.QuoteAsync("2026-10-25", "2026-10-26", 5, 0, null, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.Web.QuoteAsync("2026-10-25", "2026-10-26", 1, 0, "Penthouse", Ct)));
    }

    [Fact]
    public async Task Quote_uses_published_tariff_and_the_dated_gst_slab_per_night()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        // Tariff above the ₹7,500 slab from 26 Oct: the master taxes each night on its own price (DEC-069), not on a fixed rate.
        var suiteId = await w.Db.LongAsync("SELECT room_type_id FROM inv.room_type WHERE code = 'Suite';");
        await w.Db.ExecAsync($"INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES ({suiteId}, '2026-10-26', 800000);");
        var q = (await w.Web.QuoteAsync("2026-10-26", "2026-10-27", 2, 0, "Suite", Ct)).RoomTypes.Single();
        Assert.Equal(800_000, q.NightlyRatePaise);
        Assert.Equal((1_800, 144_000L), (q.NightRates[0].TaxBps, q.TaxPaise));   // 18 % on an ₹8,000 night
        Assert.Equal(q.NetPaise + q.TaxPaise, q.TotalPaise);
        Assert.Equal(q.NightRates.Sum(n => n.TaxPaise), q.TaxPaise);
    }

    [Fact]
    public async Task Request_is_pending_idempotent_and_holds_nothing()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var body = WebWorld.Request();
        var first = await w.Web.SubmitAsync("key-1", body, Ct);
        var again = await w.Web.SubmitAsync("key-1", body, Ct);
        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value, again.Value);
        Assert.Equal("WEB-0001", first.Value.RequestNo);
        Assert.Equal("Pending", first.Value.Status);
        Assert.Equal((900_000L, 45_000L, 945_000L), (first.Value.NetPaise, first.Value.TaxPaise, first.Value.TotalPaise));
        Assert.Equal(1, await w.Db.CountAsync("SELECT COUNT(*) FROM web.request;"));
        Assert.Equal(0, await w.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
        Assert.Equal(0, await w.Db.CountAsync("SELECT COUNT(*) FROM inv.room_night;"));
        Assert.Equal("IDEMPOTENCY_MISMATCH", await Expect.CodeAsync(() => w.Web.SubmitAsync("key-1", WebWorld.Request(adults: 1), Ct)));
    }

    [Fact]
    public async Task Public_path_leaks_no_contact_data_into_audit_or_outbox()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        await w.SubmitAsync(WebWorld.Request());
        Assert.Equal(0, await w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE '%Meher%' OR detail LIKE '%98000%' OR detail LIKE '%example.test%';"));
        Assert.Equal(0, await w.Db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE payload LIKE '%Meher%' OR payload LIKE '%98000%' OR payload LIKE '%example.test%';"));
        Assert.Equal(1, await w.Db.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'WebRequestCreated' AND principal_id = {WebBookingActors.PublicWeb};"));
    }

    [Fact]
    public async Task Request_validation_honeypot_sold_out_and_phone_cap()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request() with { Website = "http://spam.test" })));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request(phone: "123"))));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request(guest: "X"))));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request(arrival: "2026-10-20", departure: "2026-10-21"))));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request(type: "Classic", adults: 3))));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request(type: "Penthouse"))));
        Assert.Equal(0, await w.Db.CountAsync("SELECT COUNT(*) FROM web.request;"));

        for (var i = 0; i < 3; i++) await w.SubmitAsync(WebWorld.Request());
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request())));   // fourth open request from one phone
        await w.SubmitAsync(WebWorld.Request(phone: "9811122233"));

        // Sold out: both Suites booked through the normal reservation service.
        var ctx = 0;
        foreach (var no in new[] { "105", "106" })
        {
            var quote = (await w.Inventory.QuoteStayAsync(w.Ctx(Who.Reception, "q" + ++ctx, "QuoteStay"),
                new(("2026-11-01"), "2026-11-02", 2, 0, (int)await w.Db.LongAsync($"SELECT room_id FROM inv.room WHERE room_no = '{no}';")), Ct)).Value;
            await w.Reservations.CreateAsync(w.Ctx(Who.Reception, "r" + ctx, "CreateReservation"),
                new(quote.QuoteId, quote.Version, new("Walk In", null), (int)await w.Db.LongAsync($"SELECT room_id FROM inv.room WHERE room_no = '{no}';")), Ct);
        }

        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request(arrival: "2026-11-01", departure: "2026-11-02", phone: "9822233344"))));
        var q = (await w.Web.QuoteAsync("2026-11-01", "2026-11-02", 2, 0, "Suite", Ct)).RoomTypes.Single();
        Assert.False(q.Bookable);
        Assert.Equal(0, q.RoomsLeft);
        Assert.Equal("Sold out for these dates", q.Reason);
    }

    [Fact]
    public async Task Restriction_blocks_the_public_quote_and_request()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        await w.Db.ExecAsync($"INSERT inv.stay_restriction(property_id, code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note) VALUES ({w.PropertyId}, 'R1', NULL, '2026-10-25', '2026-10-25', 3, 0, N'Festival');");
        var q = (await w.Web.QuoteAsync("2026-10-25", "2026-10-27", 2, 0, "Suite", Ct)).RoomTypes.Single();
        Assert.False(q.Bookable);
        Assert.Contains("Minimum stay 3 nights", q.Reason);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.SubmitAsync(WebWorld.Request())));
    }

    [Fact]
    public async Task Confirm_creates_the_reservation_through_the_reservation_service_and_is_single_shot()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var receipt = await w.SubmitAsync(WebWorld.Request());
        var id = await w.RequestIdAsync(receipt.RequestNo);

        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => w.Web.ConfirmAsync(w.Ctx(Who.Housekeeper, "c0"), new(id), Ct)));
        var res = (await w.Web.ConfirmAsync(w.Ctx(Who.Reception, "c1"), new(id), Ct)).Value;
        Assert.Equal("Confirmed", res.Status);
        Assert.False(res.PriceChanged);
        Assert.Equal(945_000, res.BookedTotalPaise);
        Assert.StartsWith("HCC-", res.ReservationNo, StringComparison.Ordinal);
        Assert.Equal("Confirmed", await w.Db.StringAsync($"SELECT status FROM inv.reservation WHERE reservation_id = {res.ReservationId};"));
        Assert.Equal("Website", await w.Db.StringAsync($"SELECT source FROM inv.reservation WHERE reservation_id = {res.ReservationId};"));
        Assert.Equal(2, await w.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {res.ReservationId};"));
        Assert.Equal("Confirmed", await w.Db.StringAsync($"SELECT status FROM web.request WHERE request_id = {id};"));

        // Replay with the same key: same result, no second reservation. A second confirm with a new key is refused.
        Assert.True((await w.Web.ConfirmAsync(w.Ctx(Who.Reception, "c1"), new(id), Ct)).Replayed);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.ConfirmAsync(w.Ctx(Who.Manager, "c2"), new(id), Ct)));
        Assert.Equal(1, await w.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.DeclineAsync(w.Ctx(Who.Manager, "d1"), new(id, "Changed my mind"), Ct)));
    }

    [Fact]
    public async Task Confirm_when_sold_out_is_refused_and_the_request_can_still_be_declined()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var ids = new List<long>();
        for (var i = 0; i < 3; i++) ids.Add(await w.RequestIdAsync((await w.SubmitAsync(WebWorld.Request(phone: "98000123" + (40 + i)))).RequestNo));
        await w.Web.ConfirmAsync(w.Ctx(Who.Reception, "a"), new(ids[0]), Ct);
        await w.Web.ConfirmAsync(w.Ctx(Who.Reception, "b"), new(ids[1]), Ct);
        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(() => w.Web.ConfirmAsync(w.Ctx(Who.Reception, "c"), new(ids[2]), Ct)));
        Assert.Equal("Confirming", await w.Db.StringAsync($"SELECT status FROM web.request WHERE request_id = {ids[2]};"));
        Assert.Equal(2, await w.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
        // Another user cannot take over a request that someone is confirming; the claimant may retry or decline.
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.ConfirmAsync(w.Ctx(Who.Manager, "d"), new(ids[2]), Ct)));
        var declined = (await w.Web.DeclineAsync(w.Ctx(Who.Manager, "e"), new(ids[2], "Suites sold out"), Ct)).Value;
        Assert.Equal("Declined", declined.Status);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.ConfirmAsync(w.Ctx(Who.Reception, "f"), new(ids[2]), Ct)));
    }

    [Fact]
    public async Task Decline_needs_a_reason_and_staff_views_mask_the_phone_in_lists()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var receipt = await w.SubmitAsync(WebWorld.Request());
        var id = await w.RequestIdAsync(receipt.RequestNo);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.Web.DeclineAsync(w.Ctx(Who.Reception, "d0"), new(id, " "), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => w.Web.ListAsync(w.PropertyId, Who.Housekeeper, null, Ct)));

        var list = await w.Web.ListAsync(w.PropertyId, Who.Reception, "Pending", Ct);
        Assert.Equal(1, list.Pending);
        Assert.DoesNotContain("98000", list.Requests[0].PhoneMasked);
        Assert.EndsWith("2345", list.Requests[0].PhoneMasked);
        var detail = await w.Web.GetAsync(w.PropertyId, Who.Reception, id, Ct);
        Assert.Equal("+91 98000 12345", detail.Phone);
        Assert.Equal(2, detail.QuotedNights.Count);

        var d = (await w.Web.DeclineAsync(w.Ctx(Who.Reception, "d1"), new(id, "Suite reserved for maintenance"), Ct)).Value;
        Assert.Equal("Declined", d.Status);
        Assert.Equal("Suite reserved for maintenance", (await w.Web.GetAsync(w.PropertyId, Who.Manager, id, Ct)).Reason);
        Assert.Equal(0, (await w.Web.ListAsync(w.PropertyId, Who.Manager, null, Ct)).Pending);
        Assert.Equal(0, await w.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
    }

    [Fact]
    public async Task Deposit_link_flow_posts_a_deposit_receipt_on_the_folio()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var id = await w.RequestIdAsync((await w.SubmitAsync(WebWorld.Request())).RequestNo);

        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l0"), new(id), Ct)));   // not confirmed yet
        var confirmed = (await w.Web.ConfirmAsync(w.Ctx(Who.Reception, "c1"), new(id), Ct)).Value;

        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l1"), new(id, 945_001), Ct)));
        var link = (await w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l2"), new(id), Ct)).Value;
        Assert.Equal(283_500, link.AmountPaise);   // 30 % of 945000
        Assert.Equal("/pay/" + link.Token, link.Url);
        Assert.Equal(32, await w.Db.CountAsync("SELECT DATALENGTH(token_hash) FROM web.deposit_link;"));
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l3"), new(id), Ct)));   // one open link

        var view = await w.Web.DepositViewAsync(link.Token, Ct);
        Assert.Equal(("Open", 283_500L, "WEB-0001"), (view.Status, view.AmountPaise, view.RequestNo));
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => w.Web.DepositViewAsync(WebBookingTokens.Other, Ct)));

        // Reception cannot record the deposit; Manager (PERM-PAY-002) can.
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => w.Web.MarkDepositPaidAsync(w.Ctx(Who.Reception, "p0"), new(link.Token, "GW-100200"), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => w.Web.MarkDepositPaidAsync(w.Ctx(Who.Manager, "p1"), new(link.Token, "x"), Ct)));
        var paid = (await w.Web.MarkDepositPaidAsync(w.Ctx(Who.Manager, "p2"), new(link.Token, "GW-100200"), Ct)).Value;
        Assert.Equal(283_500, paid.AmountPaise);
        Assert.Equal(("deposit", "UPI", 283_500L), (
            await w.Db.StringAsync($"SELECT kind FROM pay.receipt WHERE receipt_id = {paid.ReceiptId};"),
            await w.Db.StringAsync($"SELECT method FROM pay.receipt WHERE receipt_id = {paid.ReceiptId};"),
            await w.Db.LongAsync($"SELECT amount_paise FROM pay.receipt WHERE receipt_id = {paid.ReceiptId};")));
        Assert.Equal(confirmed.ReservationId, await w.Db.LongAsync($"SELECT reservation_id FROM bill.folio WHERE folio_id = {paid.FolioId};"));
        Assert.Equal("Paid", (await w.Web.DepositViewAsync(link.Token, Ct)).Status);
        Assert.Equal(1, await w.Db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ReceiptRecorded';"));

        Assert.True((await w.Web.MarkDepositPaidAsync(w.Ctx(Who.Manager, "p2"), new(link.Token, "GW-100200"), Ct)).Replayed);     // same key: replay, no second receipt
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.MarkDepositPaidAsync(w.Ctx(Who.Manager, "p3"), new(link.Token, "GW-100201"), Ct)));
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l4"), new(id), Ct)));   // already paid
        Assert.Equal(1, await w.Db.CountAsync("SELECT COUNT(*) FROM pay.receipt;"));
    }

    [Fact]
    public async Task Expired_link_cannot_be_paid_and_a_new_one_replaces_it()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var id = await w.RequestIdAsync((await w.SubmitAsync(WebWorld.Request())).RequestNo);
        await w.Web.ConfirmAsync(w.Ctx(Who.Reception, "c1"), new(id), Ct);
        var link = (await w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l1"), new(id), Ct)).Value;
        w.Clock.Now = w.Clock.Now.AddHours(25);
        Assert.Equal("Expired", (await w.Web.DepositViewAsync(link.Token, Ct)).Status);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => w.Web.MarkDepositPaidAsync(w.Ctx(Who.Manager, "p1"), new(link.Token, "GW-100200"), Ct)));
        Assert.Equal(0, await w.Db.CountAsync("SELECT COUNT(*) FROM pay.receipt;"));
        var second = (await w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l2"), new(id, 200_000), Ct)).Value;
        Assert.NotEqual(link.Token, second.Token);
        Assert.Equal(200_000, second.AmountPaise);
        Assert.Equal("Cancelled", await w.Db.StringAsync($"SELECT status FROM web.deposit_link WHERE link_id = {link.LinkId};"));
        var paid = (await w.Web.MarkDepositPaidAsync(w.Ctx(Who.Owner, "p2"), new(second.Token, "GW-100300"), Ct)).Value;
        Assert.Equal(200_000, paid.AmountPaise);
    }

    [Fact]
    public async Task Guest_identity_documents_are_never_collected_or_returned()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        Assert.Equal(0, await w.Db.CountAsync("SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'web' AND (c.name LIKE '%id_number%' OR c.name LIKE '%aadhaar%' OR c.name LIKE '%passport%' OR c.name LIKE '%document%');"));
        var props = typeof(WebRequestDetail).GetProperties().Select(p => p.Name.ToUpperInvariant()).Concat(typeof(PublicQuoteResult).GetProperties().Select(p => p.Name.ToUpperInvariant()));
        Assert.DoesNotContain(props, n => n.Contains("AADHAAR", StringComparison.Ordinal) || n.Contains("PASSPORT", StringComparison.Ordinal) || n.Contains("IDNUMBER", StringComparison.Ordinal));
    }
}

internal static class WebBookingTokens
{
    public const string Other = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
}
