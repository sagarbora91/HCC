using Hcc.Application.Accounting;
using Hcc.Application.Hall;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Hall.Tests.Sql;

/// <summary>
/// F6 H1 on a disposable HccTest_* hotel (migrations 4300 / 4320 / 4740): HB-GOLDEN end to end — a SENT quotation (v2, 5 % Manager discount) is
/// accepted with a ₹20,000 UPI advance, the event is billed on the day at 160 plates (guaranteed 150) into an INV-series tax invoice (source 'hall'),
/// the advance is applied and the balance goes to the company; the accounting consumer turns the HallPosted events into balanced vouchers
/// (2000 Dr ₹20,000 applied, 4400 / 4200 Cr, CGST + SGST, 1110/CMP-1 Dr the balance). Then: a clash refused by the unique calendar hold
/// (incl. the combined hall), cancellation at 40 days → 4300 with GST, refund not by the canceller, Manager ≤ ₹50,000.
/// </summary>
[Trait("Category", "Sql")]
public sealed class HallBillingSqlTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(int Sagar, int Lawn, int Combined)> VenuesAsync(Hotel h)
    {
        await h.ScalarAsync($"""
            UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};
            INSERT hall.venue(property_id, code, name, kind, cap_round, cap_theatre, combined_of) VALUES
              ({h.PropertyId}, 'V-SAGAR', N'Sagar Hall', 'HALL', 150, 200, NULL), ({h.PropertyId}, 'V-LAWN', N'Garden Lawn', 'LAWN', 300, NULL, NULL),
              ({h.PropertyId}, 'V-SAGAR-LAWN', N'Sagar Hall + Garden Lawn', 'HALL', NULL, NULL, 'V-SAGAR,V-LAWN');
            """, Ct);
        long V(string code) => h.LongAsync($"SELECT venue_id FROM hall.venue WHERE property_id = {h.PropertyId} AND code = '{code}'", Ct).GetAwaiter().GetResult();
        return ((int)V("V-SAGAR"), (int)V("V-LAWN"), (int)V("V-SAGAR-LAWN"));
    }

    private static int _n;

    /// <summary>A SENT quotation (as lane H2's QUO_SEND leaves it) with the golden v2 lines; returns the quotation id.</summary>
    private static async Task<long> QuoteAsync(Hotel h, int venueId, string date, string slot = "EVENING", int pax = 150)
    {
        var n = Interlocked.Increment(ref _n);
        return await h.LongAsync($"""
            INSERT hall.enquiry(property_id, name, phone, event_type, dates, slot, pax, venue_id, source, follow_up, status, created_by, created_utc)
              VALUES ({h.PropertyId}, N'Kulkarni family (fictional)', '9000000123', 'RECEPTION', '{date}', '{slot}', {pax}, {venueId}, 'PHONE', '2026-10-24', 'QUOTED', {h.Reception}, SYSUTCDATETIME());
            DECLARE @e BIGINT = SCOPE_IDENTITY();
            INSERT hall.quotation(property_id, number, fy, enquiry_id, current_version, customer_name, customer_phone, company_id, created_by, created_utc)
              VALUES ({h.PropertyId}, 'QT/2627/{n:0000}', '2627', @e, 2, N'Kulkarni family (fictional)', '9000000123', 1, {h.Manager}, SYSUTCDATETIME());
            DECLARE @q BIGINT = SCOPE_IDENTITY();
            INSERT hall.quotation_version(quotation_id, v, status, event_type, dates, slot, venue_id, setup, pax, guaranteed_pax, valid_until, created_by, created_utc)
              VALUES (@q, 1, 'SUPERSEDED', 'RECEPTION', '{date}', '{slot}', {venueId}, 'ROUND', {pax}, 150, '2026-11-08', {h.Manager}, SYSUTCDATETIME()),
                     (@q, 2, 'SENT', 'RECEPTION', '{date}', '{slot}', {venueId}, 'ROUND', {pax}, 150, '2026-11-08', {h.Manager}, SYSUTCDATETIME());
            DECLARE @v BIGINT = (SELECT version_id FROM hall.quotation_version WHERE quotation_id = @q AND v = 2);
            INSERT hall.quotation_line(version_id, seq, kind, descr, qty, unit, rate_paise, list_rate_paise, discount_paise, gst_class) VALUES
              (@v, 1, 'HALL', N'Sagar Hall · evening', 1, 'slot', 1425000, 1500000, 0, 'HALL_RENT'),
              (@v, 2, 'MENU', N'Silver Veg per plate', 150, 'plate', 61750, 65000, 0, 'BANQUET_FOOD'),
              (@v, 3, 'EXTRA', N'Stage decor', 1, 'lot', 950000, 1000000, 0, 'SERVICE');
            SELECT @q;
            """, Ct);
    }

    private static AccountingVoucherProjector Projector(Hotel h) => new(h.Platform.Connections, h.Platform.Outbox, new StoredChargeGstReader(), h.Get<TimeProvider>());

    [Fact]
    public async Task HbGolden_accept_advance_bill_invoice_and_books_balance()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (sagar, _, _) = await VenuesAsync(h);
        var c = h.Get<HallBillingCommands>();
        var q = await QuoteAsync(h, sagar, "2026-12-05");
        var d1 = await h.BusinessDateAsync(Ct);

        var stale = await Assert.ThrowsAsync<HccException>(() => c.AcceptAsync(h.Ctx(h.Reception, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q, Version: 1), Ct));
        Assert.Equal(HccErrorCode.HCC_QUO_NOT_LATEST, stale.Code);
        var cashier = await Assert.ThrowsAsync<HccException>(() => c.AcceptAsync(h.Ctx(h.Cashier, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q), Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, cashier.Code);
        var acc = (await c.AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q, 2, Advance: new HallTenderIn(2_000_000, "UPI", "UPI-HB-1")), Ct)).Value;
        Assert.Equal(13_732_250L, acc.TotalPaise);
        Assert.Equal([3_433_063L, 6_866_125L, 3_433_062L],
            await h.RowsAsync($"SELECT amount_paise FROM hall.event_advance WHERE event_id = {acc.EventId} ORDER BY seq", r => r.GetInt64(0), Ct));
        Assert.Equal(2_000_000L, await h.LongAsync($"SELECT paid_paise FROM hall.event_advance WHERE event_id = {acc.EventId} AND seq = 1", Ct));
        Assert.Equal(1L, await h.LongAsync($"SELECT COUNT(*) FROM hall.calendar_hold WHERE event_id = {acc.EventId}", Ct)); // EVENING on one hall = one row
        Assert.Equal("WON", await h.ScalarAsync($"SELECT e.status FROM hall.enquiry e JOIN hall.quotation q ON q.enquiry_id = e.enquiry_id WHERE q.quotation_id = {q}", Ct));

        var early = await Assert.ThrowsAsync<HccException>(() => c.CloseAsync(h.Ctx(h.Reception, HallBillingCommands.OpClose), new CloseHallEventRequest(acc.EventId, 160), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, early.Code);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-12-05' WHERE property_id = {h.PropertyId};", Ct);
        var bill = (await c.CloseAsync(h.Ctx(h.Reception, HallBillingCommands.OpClose), new CloseHallEventRequest(acc.EventId, 160, Settle: new HallSettleIn("COMPANY", CompanyId: 1), BuyerAddress: "4 Ganesh Nagar, Latur (fictional)"), Ct)).Value;
        Assert.Equal((160, 14_460_900L, 2_000_000L, 12_460_900L), (bill.BilledPax, bill.TotalPaise, bill.AdvanceAppliedPaise, bill.BalancePaise));
        Assert.Equal(1L, await h.LongAsync($"SELECT COUNT(*) FROM ivc.invoice WHERE source_kind = 'hall' AND source_id = {acc.EventId} AND invoice_no = '{bill.InvoiceNo}'", Ct));
        Assert.Equal(14_460_900L, await h.LongAsync($"SELECT total_paise FROM hall.event WHERE event_id = {acc.EventId} AND status = 'BILLED'", Ct));

        var p = Projector(h);
        await p.RunOnceAsync(ct: Ct);
        var books = h.Get<AccountingQueries>();
        var all = new List<VoucherView>();
        foreach (var d in new[] { d1, new DateOnly(2026, 12, 5) })
        {
            var day = await books.DayAsync(h.As(h.Owner), d, Ct);
            Assert.DoesNotContain(day.Exceptions, x => x.Kind is "unbalanced" or "unmapped");
            all.AddRange(day.Vouchers.Where(v => v.SourceKind == "hall"));
        }

        Assert.Equal(["hall-advance", "hall-advance-apply", "hall-balance", "hall-invoice"], all.Select(v => v.Sub).Order(StringComparer.Ordinal).ToArray());
        Assert.All(all, v => Assert.True(v.Balanced, v.SourceKey));
        var adv = all.Single(v => v.Sub == "hall-advance");
        Assert.DoesNotContain(adv.Entries, e => e.Account is "2110" or "2120"); // DEC-142
        var by = all.SelectMany(v => v.Entries).GroupBy(e => e.Account).ToDictionary(g => g.Key, g => g.Sum(e => e.DebitPaise - e.CreditPaise));
        Assert.Equal(0L, by["2000"]);
        Assert.Equal(2_000_000L, all.Single(v => v.Sub == "hall-advance-apply").Entries.Single(e => e.Account == "2000").DebitPaise);
        Assert.Equal(-2_375_000L, by["4400"]);
        Assert.Equal(-9_880_000L, by["4200"]);
        Assert.Equal(-1_102_950L, by["2110"]);
        Assert.Equal(-1_102_950L, by["2120"]);
        Assert.Equal(0L, by["1100"]);
        Assert.Equal(12_460_900L, by["1110/CMP-1"]);
    }

    [Fact]
    public async Task Clash_cancel_40_days_and_refund_rules()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (sagar, lawn, combined) = await VenuesAsync(h);
        var c = h.Get<HallBillingCommands>();
        var q1 = await QuoteAsync(h, combined, "2026-12-03", pax: 300);
        var ev = (await c.AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q1, Advance: new HallTenderIn(5_000_000, "Bank transfer", "NEFT-HB-2")), Ct)).Value;
        Assert.Equal(2L, await h.LongAsync($"SELECT COUNT(*) FROM hall.calendar_hold WHERE event_id = {ev.EventId}", Ct)); // both halls of the combination
        var q2 = await QuoteAsync(h, lawn, "2026-12-03", "FULL_DAY");
        var clash = await Assert.ThrowsAsync<HccException>(() => c.AcceptAsync(h.Ctx(h.Reception, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q2), Ct));
        Assert.Equal(HccErrorCode.HCC_HALL_CLASH, clash.Code);
        var q3 = await QuoteAsync(h, sagar, "2026-12-03", "MORNING");
        await c.AcceptAsync(h.Ctx(h.Reception, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q3), Ct);

        var cancel = (await c.CancelAsync(h.Ctx(h.Reception, HallBillingCommands.OpCancel), new CancelHallEventRequest(ev.EventId, "Family emergency"), Ct)).Value;
        Assert.Equal((3_433_063L, 1_566_937L), (cancel.KeptPaise, cancel.RefundPaise));
        Assert.Equal(0L, await h.LongAsync($"SELECT COUNT(*) FROM hall.calendar_hold WHERE event_id = {ev.EventId} AND released_utc IS NULL", Ct));
        var self = await Assert.ThrowsAsync<HccException>(() => c.RefundAsync(h.Ctx(h.Reception, HallBillingCommands.OpRefund), new HallRefundRequest(ev.EventId, "Bank transfer", "NEFT-R"), Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, self.Code);
        await c.RefundAsync(h.Ctx(h.Manager, HallBillingCommands.OpRefund), new HallRefundRequest(ev.EventId, "Bank transfer", "NEFT-R"), Ct);
        Assert.Equal("PAID", await h.ScalarAsync($"SELECT refund_state FROM hall.event WHERE event_id = {ev.EventId}", Ct));

        await Projector(h).RunOnceAsync(ct: Ct);
        var day = await h.Get<AccountingQueries>().DayAsync(h.As(h.Owner), await h.BusinessDateAsync(Ct), Ct);
        var hall = day.Vouchers.Where(v => v.SourceKind == "hall").ToList();
        Assert.All(hall, v => Assert.True(v.Balanced, v.SourceKey));
        var cv = hall.Single(v => v.Sub == "hall-cancel");
        Assert.Equal([("2000", 3_433_063L, 0L), ("4300", 0L, 2_909_375L), ("2110", 0L, 261_844L), ("2120", 0L, 261_844L)],
            cv.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToArray());
        Assert.Equal(0L, hall.SelectMany(v => v.Entries).Where(e => e.Account == "2000").Sum(e => e.DebitPaise - e.CreditPaise));
    }
}
