using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Hall;
using Hcc.Application.Invoices;
using Hcc.Application.Payments;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;

namespace Hcc.Hall.Tests.Sql;

/// <summary>
/// F6 H3 on a disposable HccTest_* hotel (migrations 4345 / 4346 / 4348): the PERM-HAL-001…006 seed per role, enquiries (add, follow-up, soft hold that warns and
/// never blocks, lost reason, calendar), the halls master (Owner only, stale version), the money strip for housekeeping (DEC-061), hall cash in the open shift's drawer
/// (advance in, cash refund out), the credit note on a printed hall invoice (reverses the books), and the print log.
/// </summary>
[Trait("Category", "Sql")]
public sealed class HallH3SqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<int> UserAsync(Hotel h, string login, string role) => Convert.ToInt32(await h.ScalarAsync($"""
        INSERT plat.app_user(property_id, login_name, display_name, created_utc) VALUES ({h.PropertyId}, '{login}', N'{login}', SYSUTCDATETIME());
        DECLARE @u INT = SCOPE_IDENTITY();
        INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, '{role}', DATEADD(day, -30, SYSUTCDATETIME()), SYSUTCDATETIME());
        SELECT @u;
        """, Ct), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Rbac_seed_gives_each_role_its_hall_permissions_and_housekeeping_none()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var hk = await UserAsync(h, "hk.sunita", "HKP");
        var policy = h.Get<IAccessPolicy>();
        async Task<string> Row(int who) => string.Concat(await Task.WhenAll(Enumerable.Range(1, 6).Select(async i =>
            await policy.HasAsync(h.PropertyId, who, $"PERM-HAL-00{i}", null, Ct) ? "Y" : "-")));
        Assert.Equal("YYYYYY", await Row(h.Owner));
        Assert.Equal("YYYY-Y", await Row(h.Manager));
        Assert.Equal("YYYY--", await Row(h.Reception));
        Assert.Equal("--YY--", await Row(h.Cashier));
        Assert.Equal("------", await Row(hk));
        // The Accountant holds the view of 002 and 004 only: the read passes, an action does not (mode V).
        Assert.True(await policy.HasAsync(h.PropertyId, h.Accountant, AccessEvaluator.View("PERM-HAL-002"), null, Ct));
        Assert.False(await policy.HasAsync(h.PropertyId, h.Accountant, "PERM-HAL-002", null, Ct));
        var denied = await Assert.ThrowsAsync<HccException>(() => h.Get<HallCommands>().AddEnquiryAsync(h.Ctx(hk, HallCommands.OpAddEnquiry), Enquiry("2026-12-05"), Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, denied.Code);
    }

    private static AddHallEnquiryRequest Enquiry(string date, int? venue = null, string slot = "EVENING", string name = "Joshi family (fictional)") =>
        new(name, "9000000456", "BIRTHDAY", [date], slot, 80, "PHONE", "2026-10-26", venue);

    [Fact]
    public async Task Enquiry_lifecycle_soft_hold_warns_but_never_blocks_and_the_calendar_shows_both()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (sagar, _, _) = await HallBillingSqlTests.VenuesAsync(h);
        var c = h.Get<HallCommands>();
        var q = h.Get<HallQueries>();
        var a = (await c.AddEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpAddEnquiry), Enquiry("2026-12-05", sagar), Ct)).Value;
        var b = (await c.AddEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpAddEnquiry), Enquiry("2026-12-05", sagar, name: "Kale family (fictional)"), Ct)).Value;
        Assert.Equal("OPEN", a.Status);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => c.AddEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpAddEnquiry), Enquiry("2020-01-01", sagar), Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => c.AddEnquiryAsync(h.Ctx(h.Cashier, HallCommands.OpAddEnquiry), Enquiry("2026-12-05"), Ct))).Code);

        var deny = await Assert.ThrowsAsync<HccException>(() => c.HoldEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpHoldEnquiry), new HoldHallEnquiryRequest(a.Id, sagar, 2), Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, deny.Code); // Manager or Owner only
        var h1 = (await c.HoldEnquiryAsync(h.Ctx(h.Manager, HallCommands.OpHoldEnquiry), new HoldHallEnquiryRequest(a.Id, sagar, 2), Ct)).Value;
        Assert.DoesNotContain("warning", h1.Message);
        var h2 = (await c.HoldEnquiryAsync(h.Ctx(h.Manager, HallCommands.OpHoldEnquiry), new HoldHallEnquiryRequest(b.Id, sagar, 3), Ct)).Value;
        Assert.Contains($"also soft-held by ENQ-{a.Id}", h2.Message); // warns, does not block
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => c.HoldEnquiryAsync(h.Ctx(h.Manager, HallCommands.OpHoldEnquiry), new HoldHallEnquiryRequest(a.Id, sagar, 4), Ct))).Code);

        var cal = await q.CalendarAsync(h.PropertyId, h.Reception, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), null, Ct);
        Assert.Equal(2, cal.Count(r => r.Kind == "SOFT_HOLD" && r.Date == "2026-12-05"));

        // an accepted event holds the hall hard: a further hold is refused (clash), the soft holds stay as warnings only
        var quote = await HallBillingSqlTests.QuoteAsync(h, sagar, "2026-12-05");
        await h.Get<HallBillingCommands>().AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(quote), Ct);
        Assert.Equal(HccErrorCode.HCC_HALL_CLASH, (await Assert.ThrowsAsync<HccException>(() => c.HoldEnquiryAsync(h.Ctx(h.Manager, HallCommands.OpHoldEnquiry), new HoldHallEnquiryRequest(a.Id, sagar, 1), Ct))).Code);
        cal = await q.CalendarAsync(h.PropertyId, h.Reception, new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 5), sagar, Ct);
        Assert.Contains(cal, r => r.Kind == "EVENT" && r.Status == "Confirmed" && r.Label.Contains("Kulkarni"));

        var lostNoNote = await Assert.ThrowsAsync<HccException>(() => c.LoseEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpLoseEnquiry), new LoseHallEnquiryRequest(b.Id, "LR-OTHER"), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, lostNoNote.Code);
        await c.LoseEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpLoseEnquiry), new LoseHallEnquiryRequest(b.Id, "LR-DATE"), Ct);
        var upd = await Assert.ThrowsAsync<HccException>(() => c.UpdateEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpUpdateEnquiry), new UpdateHallEnquiryRequest(b.Id, Note: "x"), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, upd.Code); // a lost enquiry is read-only until reopened
        await c.UpdateEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpUpdateEnquiry), new UpdateHallEnquiryRequest(b.Id, Status: "OPEN"), Ct);
        var stale = await Assert.ThrowsAsync<HccException>(() => c.UpdateEnquiryAsync(h.Ctx(h.Reception, HallCommands.OpUpdateEnquiry), new UpdateHallEnquiryRequest(b.Id, Note: "n", ExpectedVersion: 1), Ct));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        var all = await q.EnquiriesAsync(h.PropertyId, h.Reception, null, Ct);
        Assert.Equal(["OPEN", "OPEN", "WON"], all.Select(x => x.Status).Order(StringComparer.Ordinal).ToArray()); // a, b reopened, and the accepted quotation's enquiry
    }

    [Fact]
    public async Task Halls_master_is_owner_only_versioned_and_prices_are_stripped_for_housekeeping()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var hk = await UserAsync(h, "hk.sunita", "HKP");
        var c = h.Get<HallCommands>();
        var q = h.Get<HallQueries>();
        var req = new UpsertHallVenueRequest("V-MOTI", "Moti Hall", "HALL", [new HallVenueSlotIn("MORNING", "08:00", "13:00", 500_000), new HallVenueSlotIn("EVENING", "18:00", "23:00", 800_000)],
            Capacity: new Dictionary<string, int> { ["ROUND"] = 60, ["THEATRE"] = 80 });
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => c.UpsertVenueAsync(h.Ctx(h.Manager, HallCommands.OpUpsertVenue), req, Ct))).Code);
        var v1 = (await c.UpsertVenueAsync(h.Ctx(h.Owner, HallCommands.OpUpsertVenue), req, Ct)).Value;
        var v2 = (await c.UpsertVenueAsync(h.Ctx(h.Owner, HallCommands.OpUpsertVenue), req with { VenueId = (int)v1.Id, ExpectedVersion = 1, Slots = [new HallVenueSlotIn("EVENING", "18:00", "23:00", 900_000)] }, Ct)).Value;
        Assert.Equal(v1.Id, v2.Id);
        Assert.Equal(HccErrorCode.STALE_VERSION, (await Assert.ThrowsAsync<HccException>(() => c.UpsertVenueAsync(h.Ctx(h.Owner, HallCommands.OpUpsertVenue), req with { VenueId = (int)v1.Id, ExpectedVersion = 1 }, Ct))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => c.UpsertVenueAsync(h.Ctx(h.Owner, HallCommands.OpUpsertVenue), req with { Code = "V-MOTI2" }, Ct))).Code); // same name
        var rec = (await q.VenuesAsync(h.PropertyId, h.Reception, Ct)).Single();
        Assert.Equal(2, rec.Version);
        Assert.Equal(900_000L, rec.Slots.Single().RatePaise);
        Assert.Equal(60, rec.Capacity["ROUND"]);
        var strip = (await q.VenuesAsync(h.PropertyId, hk, Ct)).Single();
        Assert.All(strip.Slots, s => Assert.Null(s.RatePaise)); // DEC-061
        Assert.Single(strip.Slots);
    }

    [Fact]
    public async Task Cash_advance_and_cash_refund_move_the_drawer_of_the_open_shift()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (sagar, _, _) = await HallBillingSqlTests.VenuesAsync(h);
        var till = Convert.ToInt32(await h.ScalarAsync($"INSERT pay.till(property_id, code, name, created_by, created_utc) OUTPUT INSERTED.till_id VALUES ({h.PropertyId}, 'HALL-T1', N'Hall test till', {h.Owner}, SYSUTCDATETIME());", Ct),
            System.Globalization.CultureInfo.InvariantCulture);
        var c = h.Get<HallBillingCommands>();
        var quote = await HallBillingSqlTests.QuoteAsync(h, sagar, "2027-02-10");
        // no open shift: cash is refused (UPI would pass)
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => c.AcceptAsync(h.Ctx(h.Reception, HallBillingCommands.OpAccept), new AcceptQuotationRequest(quote, Advance: new HallTenderIn(500_000, "Cash")), Ct))).Code);
        await h.Get<CashierService>().OpenShiftAsync(h.As(h.Cashier), Hotel.Key(), new OpenShiftRequest(till, 100_000), Ct);
        var ev = (await c.AcceptAsync(h.Ctx(h.Reception, HallBillingCommands.OpAccept), new AcceptQuotationRequest(quote, Advance: new HallTenderIn(500_000, "Cash")), Ct)).Value;
        Assert.Equal(500_000L, await h.LongAsync("SELECT amount_paise FROM pay.cash_movement WHERE source_type = 'hall' AND kind = 'payment'", Ct));
        var more = (await c.ReceiveAdvanceAsync(h.Ctx(h.Cashier, HallBillingCommands.OpAdvance), new HallAdvanceRequest(ev.EventId, 300_000, "Cash", TillId: till), Ct)).Value;
        Assert.Equal(800_000L, await h.LongAsync("SELECT SUM(amount_paise) FROM pay.cash_movement WHERE source_type = 'hall'", Ct));
        _ = more;
        await c.CancelAsync(h.Ctx(h.Reception, HallBillingCommands.OpCancel), new CancelHallEventRequest(ev.EventId, "Date changed"), Ct); // 0 % slab: the whole 8,000 comes back
        await c.RefundAsync(h.Ctx(h.Manager, HallBillingCommands.OpRefund), new HallRefundRequest(ev.EventId, "Cash"), Ct);
        Assert.Equal(-800_000L, await h.LongAsync("SELECT amount_paise FROM pay.cash_movement WHERE source_type = 'hall-refund' AND kind = 'refund'", Ct));
        Assert.Equal(0L, await h.LongAsync("SELECT SUM(amount_paise) FROM pay.cash_movement WHERE source_type IN ('hall','hall-refund')", Ct));
        var gate = await Assert.ThrowsAsync<HccException>(() => c.ReceiveAdvanceAsync(h.Ctx(h.Cashier, HallBillingCommands.OpAdvance), new HallAdvanceRequest(ev.EventId, 100, "UPI", "U-1"), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, gate.Code); // advances only on a confirmed event
    }

    [Fact]
    public async Task Credit_note_on_a_printed_hall_invoice_reverses_the_books_and_hall_print_is_logged()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (sagar, _, _) = await HallBillingSqlTests.VenuesAsync(h);
        var c = h.Get<HallBillingCommands>();
        var quote = await HallBillingSqlTests.QuoteAsync(h, sagar, "2026-12-05");
        var acc = (await c.AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(quote, 2, Advance: new HallTenderIn(2_000_000, "UPI", "UPI-H3-1")), Ct)).Value;
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-12-05' WHERE property_id = {h.PropertyId};", Ct);
        var bill = (await c.CloseAsync(h.Ctx(h.Reception, HallBillingCommands.OpClose), new CloseHallEventRequest(acc.EventId, 160, Settle: new HallSettleIn("COMPANY", CompanyId: 1),
            BuyerAddress: "4 Ganesh Nagar, Latur (fictional)"), Ct)).Value;
        var invoiceId = await h.LongAsync($"SELECT invoice_id FROM hall.event WHERE event_id = {acc.EventId}", Ct);
        var inv = h.Get<InvoiceService>();
        var note = (await inv.RequestCreditNoteAsync(h.Ctx(h.Owner, "RequestCreditNote"), new RequestCreditNoteRequest(invoiceId, "full", "Wrong plates billed"), Ct)).Value;
        Assert.Equal("Issued", note.Outcome);
        Assert.Equal(bill.TotalPaise, note.TotalPaise);
        await HallBillingSqlTests.Projector(h).RunOnceAsync(ct: Ct);
        var day = await h.Get<AccountingQueries>().DayAsync(h.As(h.Owner), new DateOnly(2026, 12, 5), Ct);
        Assert.DoesNotContain(day.Exceptions, x => x.Kind is "unbalanced" or "unmapped");
        var cn = day.Vouchers.Single(v => v.Sub == "hall-credit-note");
        Assert.True(cn.Balanced);
        var net = day.Vouchers.Where(v => v.SourceKind == "hall" && v.Sub is "hall-invoice" or "hall-credit-note").SelectMany(v => v.Entries).GroupBy(e => e.Account).ToDictionary(g => g.Key, g => g.Sum(e => e.DebitPaise - e.CreditPaise));
        Assert.All(new[] { "4400", "4200", "2110", "2120" }, a => Assert.Equal(0L, net[a])); // the invoice and its credit note cancel
        Assert.Equal(0L, net["1100"]);

        var cmd = h.Get<HallCommands>();
        await cmd.LogPrintAsync(h.Ctx(h.Reception, HallCommands.OpLogPrint), new LogHallPrintRequest("INVOICE", acc.EventId), Ct);
        await cmd.LogPrintAsync(h.Ctx(h.Reception, HallCommands.OpLogPrint), new LogHallPrintRequest("BEO", acc.EventId, WithPrices: false), Ct);
        Assert.Equal(2L, await h.LongAsync($"SELECT COUNT(*) FROM hall.print_log WHERE ref_id = {acc.EventId}", Ct));
        Assert.Equal(0L, await h.LongAsync($"SELECT COUNT(*) FROM hall.print_log WHERE doc_kind = 'BEO' AND with_prices = 1", Ct));
        var hk = await UserAsync(h, "hk.sunita", "HKP");
        await cmd.LogPrintAsync(h.Ctx(hk, HallCommands.OpLogPrint), new LogHallPrintRequest("BEO", acc.EventId, WithPrices: true), Ct); // the kitchen / housekeeping copy
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => cmd.LogPrintAsync(h.Ctx(hk, HallCommands.OpLogPrint), new LogHallPrintRequest("INVOICE", acc.EventId), Ct))).Code);
        var events = await h.Get<HallQueries>().EventsAsync(h.PropertyId, hk, "BILLED", null, Ct);
        Assert.Null(events.Single().TotalPaise); // DEC-061
        Assert.Null(events.Single().InvoiceNo);
        Assert.All(events.Single().Lines, l => Assert.Null(l.RatePaise));
        var money = await h.Get<HallQueries>().EventsAsync(h.PropertyId, h.Reception, "BILLED", null, Ct);
        Assert.Equal(bill.TotalPaise, money.Single().TotalPaise);
    }
}
