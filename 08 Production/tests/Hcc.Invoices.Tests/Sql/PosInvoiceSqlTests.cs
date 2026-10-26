using Hcc.Application.Commands;
using Hcc.Application.Invoices;
using Hcc.Application.Pos;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Pos;
using Hcc.Contracts.Stays;
using Hcc.Domain.Invoices;

namespace Hcc.Invoices.Tests.Sql;

/// <summary>
/// DEC-072 (audit A03a-01) on SQL with the real composition: every settled walk-in / paid-at-outlet POS bill is a GST tax invoice in the RST
/// series (gap-free per FY, 16 characters, restarting 1 April), B2C or B2B with a checked GSTIN; a void reverses it with a CN credit note; a
/// room-charged bill gets no RST invoice (its line is on the room invoice); the GSTR-1 working summary has the B2CS / B2B rows and the RST
/// documents row. Figures are worked by hand in the comments (5 % restaurant GST from the seeded GST master, not specified premises).
/// </summary>
[Trait("Category", "Sql")]
public class PosInvoiceSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A restaurant (tables T1–T20) and a room-service outlet with a small fictional menu, through the real POS commands.</summary>
    private sealed class Restaurant
    {
        private readonly Hotel _h;

        private Restaurant(Hotel h) => _h = h;

        public PosService Pos => _h.Get<PosService>();

        public PosQueries Queries => _h.Get<PosQueries>();

        public int Tables { get; private set; }

        public int RoomService { get; private set; }

        public Dictionary<string, int> Menu { get; } = [];

        public PosCaller As(int who) => new(_h.PropertyId, who);

        public static async Task<Restaurant> CreateAsync(Hotel h)
        {
            var r = new Restaurant(h);
            r.Tables = (await r.Pos.CreateOutletAsync(r.As(h.Owner), Hotel.Key(), new CreateOutletRequest("REST", "Restaurant", "table",
                Enumerable.Range(1, 20).Select(n => "T" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList()), Ct)).Value.OutletId;
            r.RoomService = (await r.Pos.CreateOutletAsync(r.As(h.Owner), Hotel.Key(), new CreateOutletRequest("RS", "Room service", "room", null), Ct)).Value.OutletId;
            foreach (var (code, name, rupees) in new[] { ("DAL", "Dal tadka", 280L), ("ROTI", "Tandoori roti", 40L), ("PANEER", "Paneer butter masala", 380L),
                         ("NAAN", "Butter naan", 70L), ("THALI", "Maharashtrian veg thali", 420L), ("TEA", "Masala chai", 60L), ("COFFEE", "Filter coffee", 80L) })
                r.Menu[code] = (await r.Pos.SaveMenuItemAsync(r.As(h.Owner), Hotel.Key(), new SaveMenuItemRequest(null, code, name, "Mains", rupees * 100, true,
                    [r.Tables, r.RoomService], null), Ct)).Value.ItemId;
            return r;
        }

        /// <summary>An open check with the items sent to the kitchen (table, or room service for an in-house room).</summary>
        public async Task<long> CheckAsync(string spot, params (string Item, int Qty)[] items)
        {
            var room = !spot.StartsWith('T');
            var o = (await Pos.OpenCheckAsync(As(_h.Cashier), Hotel.Key(), new OpenCheckRequest(room ? RoomService : Tables, room ? null : spot, room ? spot : null, 2,
                room ? null : "Walk-in · Mr. Patil (fictional)"), Ct)).Value;
            foreach (var (item, qty) in items) await Pos.AddItemAsync(As(_h.Cashier), Hotel.Key(), new AddCheckItemRequest(o.OrderId, Menu[item], qty, null), Ct);
            await Pos.SendKotAsync(As(_h.Cashier), Hotel.Key(), new SendKotRequest(o.OrderId, null), Ct);
            return o.OrderId;
        }

        public async Task<string> VersionAsync(long orderId) => (await Queries.GetCheckAsync(As(_h.Owner), orderId, Ct)).Version;

        public async Task<SettleResult> SettleUpiAsync(long orderId, string? gstin = null, string? name = null, string? address = null) =>
            (await Pos.SettleCheckAsync(As(_h.Cashier), Hotel.Key(), new SettleCheckRequest(orderId, "outlet", "UPI", "DEMO-UPI-" + orderId, null, null,
                await VersionAsync(orderId), gstin, name, address), Ct)).Value;

        public async Task<CheckView> CheckViewAsync(long orderId) => await Queries.GetCheckAsync(As(_h.Owner), orderId, Ct);
    }

    private static Task<long> CountAsync(Hotel h, string sql) => h.LongAsync(sql, Ct);

    [Fact]
    public async Task Walk_in_outlet_bills_are_RST_tax_invoices_with_the_bills_own_GST_and_appear_in_GSTR1_B2CS_and_the_documents_table()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var r = await Restaurant.CreateAsync(h);

        // Bill 1: dal ₹280 + 2 roti ₹80 = ₹360 net; 5 % = ₹18.00 (CGST 900 + SGST 900 paise); total ₹378.
        var a = await r.SettleUpiAsync(await r.CheckAsync("T3", ("DAL", 1), ("ROTI", 2)));
        // Bill 2: paneer ₹380 + 3 naan ₹210 = ₹590 net; 5 % = 2,950 paise → SGST 1,475 (half toward zero), CGST 1,475; total ₹619.50.
        var b = await r.SettleUpiAsync(await r.CheckAsync("T4", ("PANEER", 1), ("NAAN", 3)));
        Assert.Equal(("RST/2627/0000001", 37800L), (a.InvoiceNo, a.TotalPaise));
        Assert.Equal(("RST/2627/0000002", 61950L, (string?)null), (b.InvoiceNo, b.TotalPaise, b.InvoiceNote));
        Assert.All(new[] { a.InvoiceNo!, b.InvoiceNo! }, n => Assert.Equal(16, n.Length));

        var v = await w.Queries.GetInvoiceForSourceAsync(h.PropertyId, h.Accountant, "pos", b.BillId, Ct);
        Assert.Equal(("RST", "Tax invoice", "B2C", "27", false, InvoiceRulesTestsGstin), (v.DocType, v.DocLabel, v.SupplyType, v.PlaceOfSupply, v.InterState, v.Supplier.Gstin));
        Assert.Equal((59000L, 1475L, 1475L, 0L, 61950L, b.BillNo), (v.TaxablePaise, v.CgstPaise, v.SgstPaise, v.IgstPaise, v.TotalPaise, v.SourceRef));
        var line = Assert.Single(v.Lines);
        Assert.Equal(("996331", "food", 500, 2950L, b.BillId), (line.Sac, line.Category, line.TaxBps, line.TaxPaise, line.SourceLineId!.Value));
        Assert.Equal("Restaurant service · Restaurant · Table T4 · Paneer butter masala ×1, Butter naan ×3", line.Description);
        Assert.Equal("Walk-in · Mr. Patil (fictional)", v.Recipient.Name);
        // The invoice copies the bill's stored GST, never a recomputation (migration 1201 split on pos.bill).
        Assert.Equal((2950L, 1475L, 1475L), (await h.RowsAsync($"SELECT tax_paise, cgst_paise, sgst_paise FROM pos.bill WHERE bill_id = {b.BillId}",
            x => (x.GetInt64(0), x.GetInt64(1), x.GetInt64(2)), Ct)).Single());

        // Own series, gap-free; the INV (room) series is untouched. One INVOICE_ISSUED audit and InvoiceIssued event per bill, in the settle.
        Assert.Equal(2, await CountAsync(h, "SELECT last_seq FROM ivc.series WHERE doc_type = 'RST' AND fy_code = '2627'"));
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM ivc.series WHERE doc_type = 'INV'"));
        Assert.Equal(2, await CountAsync(h, "SELECT COUNT(*) FROM plat.audit_event WHERE action = 'INVOICE_ISSUED'"));
        Assert.Equal(2, await CountAsync(h, "SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'InvoiceIssued'"));
        // A document, not a voucher: the books still get exactly one PosBillSettled per bill and nothing else.
        Assert.Equal(2, await CountAsync(h, "SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'PosBillSettled'"));
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM plat.outbox WHERE event_type IN ('ChargePosted','ReceiptRecorded')"));

        // The bill print reads the number, hotel GSTIN, SAC and CGST / SGST from the check view.
        var bill = (await r.CheckViewAsync(a.OrderId)).Bill!;
        Assert.Equal(("RST/2627/0000001", "RST", "B2C", InvoiceRulesTestsGstin, "996331", 900L, 900L, (string?)null),
            (bill.Invoice!.InvoiceNo, bill.Invoice.DocType, bill.Invoice.SupplyType, bill.Invoice.SupplierGstin, bill.Invoice.Sac, bill.Invoice.CgstPaise, bill.Invoice.SgstPaise,
                bill.Invoice.CreditNoteNo));
        var text = await w.Queries.RenderInvoiceAsync(h.PropertyId, h.Cashier, v.InvoiceId, "text", Ct);
        Assert.Contains("RST/2627/0000002", text.Content, StringComparison.Ordinal);
        Assert.Contains("SAC 996331", text.Content, StringComparison.Ordinal);
        Assert.Contains("GSTIN " + InvoiceRulesTestsGstin, text.Content, StringComparison.Ordinal);

        // GSTR-1: B2CS 5 % = 36,000 + 59,000 = 95,000 taxable, CGST 900 + 1,475 = 2,375 (SGST the same), 2 documents; SAC 996331; RST row.
        var g = await w.Queries.Gstr1Async(h.PropertyId, h.Owner, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct);
        Assert.Equal([("B2CS", 500, 95000L, 2375L, 2375L, 2)], g.Rates.Select(x => (x.Section, x.TaxBps, x.TaxablePaise, x.CgstPaise, x.SgstPaise, x.Documents)));
        Assert.Equal([("996331", 500, 95000L)], g.Sac.Select(x => (x.Sac, x.TaxBps, x.TaxablePaise)));
        Assert.Equal([("RST", "RST/2627/0000001", "RST/2627/0000002", 2, 0)], g.Documents.Select(d => (d.DocType, d.First, d.Last, d.Count, d.Cancelled)));
        var register = await w.Queries.RegisterAsync(h.PropertyId, h.Manager, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct);
        Assert.Equal((2, 99750L), (register.Rows.Count, register.InvoicedTotalPaise)); // 37,800 + 61,950
    }

    private const string InvoiceRulesTestsGstin = Domain.InvoiceRulesTests.HotelGstin;

    [Fact]
    public async Task B2B_needs_a_checksum_valid_GSTIN_and_legal_name_a_large_walk_in_needs_name_and_address_and_refusals_number_nothing()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var r = await Restaurant.CreateAsync(h);
        // ₹54,000 net (50 thali ₹21,000 + 50 paneer ₹19,000 + 50 dal ₹14,000); 5 % = ₹2,700; total ₹56,700 — above the ₹50,000 r.46(e) line.
        var big = await r.CheckAsync("T1", ("THALI", 50), ("PANEER", 50), ("DAL", 50));
        await Expect.Fails(HccErrorCode.VALIDATION, () => r.SettleUpiAsync(big, gstin: "27AAAAA0000A1Z5", name: "X (fictional)")); // wrong check character
        await Expect.Fails(HccErrorCode.VALIDATION, () => r.SettleUpiAsync(big, gstin: "29AAGCB7383J1Z4"));                          // B2B without the legal name
        var large = await Expect.Fails(HccErrorCode.VALIDATION, () => r.SettleUpiAsync(big));
        Assert.Contains("₹50,000 or more", large.Message, StringComparison.Ordinal);
        // Refused inside the settle → the whole settle rolled back: no bill, no number, the check still open.
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM pos.bill"));
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM ivc.series"));
        Assert.Equal("Open", (await r.CheckViewAsync(big)).Status);

        var named = await r.SettleUpiAsync(big, name: "Mr. Patil (fictional)", address: "4 Ganj Golai, Latur (fictional)");
        Assert.Equal(("RST/2627/0000001", 5670000L), (named.InvoiceNo, named.TotalPaise));
        var nv = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, named.InvoiceId!.Value, Ct);
        Assert.Equal(("B2C", "Mr. Patil (fictional)", "4 Ganj Golai, Latur (fictional)"), (nv.SupplyType, nv.Recipient.Name, nv.Recipient.Address));

        // B2B: 2 thali ₹840; 5 % = ₹42 (2,100 + 2,100). Restaurant service is supplied where the hotel is: place of supply 27, CGST + SGST
        // even for a Karnataka (29) buyer (IGST Act s.12 as read — CA question G9).
        var company = await r.SettleUpiAsync(await r.CheckAsync("T2", ("THALI", 2)), gstin: " 29aagcb7383j1z4 ", name: "Demo Industries Pvt Ltd (fictional)");
        Assert.Equal("RST/2627/0000002", company.InvoiceNo);
        var cv = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, company.InvoiceId!.Value, Ct);
        Assert.Equal(("B2B", "29AAGCB7383J1Z4", "29", "27", false, 2100L, 2100L, 0L), (cv.SupplyType, cv.Recipient.Gstin, cv.Recipient.StateCode, cv.PlaceOfSupply, cv.InterState,
            cv.CgstPaise, cv.SgstPaise, cv.IgstPaise));
        Assert.Equal(("B2B", "29AAGCB7383J1Z4"), ((await r.CheckViewAsync(company.OrderId)).Bill!.Invoice!.SupplyType, (await r.CheckViewAsync(company.OrderId)).Bill!.Invoice!.BuyerGstin));

        // A room-folio bill takes no buyer (the room invoice takes the company GSTIN at checkout).
        await h.SeedInHouseAsync("105", "2026-10-24", "2026-10-25", ct: Ct);
        var rs = await r.CheckAsync("105", ("TEA", 1));
        var rsVersion = await r.VersionAsync(rs);
        await Expect.Fails(HccErrorCode.VALIDATION, () => r.Pos.SettleCheckAsync(r.As(h.Cashier), Hotel.Key(), new SettleCheckRequest(rs, "folio", null, null, null, null,
            rsVersion, "29AAGCB7383J1Z4", "Demo Industries Pvt Ltd (fictional)"), Ct));

        // GSTR-1: B2B 5 % 84,000 / 2,100; B2CS 5 % 54,00,000 / CGST 1,35,000.
        var g = await w.Queries.Gstr1Async(h.PropertyId, h.Owner, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct);
        Assert.Equal([("B2B", 500, 84000L, 2100L, 1), ("B2CS", 500, 5400000L, 135000L, 1)], g.Rates.Select(x => (x.Section, x.TaxBps, x.TaxablePaise, x.CgstPaise, x.Documents)));
    }

    [Fact]
    public async Task A_room_charged_bill_gets_no_RST_invoice_and_its_line_is_on_the_guests_INV_room_invoice_with_SAC_996331()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var r = await Restaurant.CreateAsync(h);
        await w.SetBusinessDateAsync("2026-10-23", Ct);
        var (_, stayId, folioId) = await h.SeedInHouseAsync("105", "2026-10-23", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stayId, new DateOnly(2026, 10, 23)), Ct);
        await w.SetBusinessDateAsync("2026-10-24", Ct);

        // Room service: tea ₹60 + coffee ₹80 = ₹140 net; 5 % = ₹7; posted to the folio, not invoiced in the restaurant series.
        var rs = await r.CheckAsync("105", ("TEA", 1), ("COFFEE", 1));
        var settled = (await r.Pos.SettleCheckAsync(r.As(h.Cashier), Hotel.Key(), new SettleCheckRequest(rs, "folio", null, null, null, null, await r.VersionAsync(rs)), Ct)).Value;
        Assert.Equal(("folio", (string?)null, (string?)null, 14700L), (settled.Mode, settled.InvoiceNo, settled.InvoiceNote, settled.TotalPaise));
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM ivc.invoice"));
        Assert.Null((await r.CheckViewAsync(rs)).Bill!.Invoice);
        var refused = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.Invoices.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue),
            new IssueInvoiceRequest("pos", settled.BillId), Ct));
        Assert.Contains("room folio", refused.Message, StringComparison.Ordinal);

        // Checkout: room night ₹2,000 + ₹100 GST and the room-service line ₹140 + ₹7 = ₹2,247 on INV/2627/0000001.
        var balance = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folioId, Ct)).Totals.BalancePaise;
        Assert.Equal(224700, balance);
        await h.PayAsync(folioId, balance, "UPI", ct: Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stayId, await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stayId}", Ct)), Ct);
        var inv = await w.IssueAsync(folioId, ct: Ct);
        Assert.Equal(("INV/2627/0000001", 224700L), (inv.InvoiceNo, inv.TotalPaise));
        var v = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, inv.InvoiceId, Ct);
        Assert.Equal([("996311", 200000L, 10000L), ("996331", 14000L, 700L)], v.Lines.Select(l => (l.Sac, l.TaxablePaise, l.TaxPaise)));
        Assert.Equal((5350L, 5350L), (v.CgstPaise, v.SgstPaise));
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM ivc.series WHERE doc_type = 'RST'"));
    }

    [Fact]
    public async Task Voiding_an_invoiced_bill_issues_a_full_CN_credit_note_books_nothing_twice_and_GSTR1_nets_B2CS_and_counts_RST_cancelled()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var r = await Restaurant.CreateAsync(h);
        var small = await r.SettleUpiAsync(await r.CheckAsync("T3", ("DAL", 1), ("ROTI", 2)));     // ₹378 ≤ ₹500 void limit: voided directly
        var large = await r.SettleUpiAsync(await r.CheckAsync("T4", ("PANEER", 1), ("NAAN", 3)));  // ₹619.50 > ₹500: needs a second person
        var kept = await r.SettleUpiAsync(await r.CheckAsync("T5", ("TEA", 2)));                  // ₹120 net + ₹6 = ₹126, stays
        Assert.Equal(new string?[] { "RST/2627/0000001", "RST/2627/0000002", "RST/2627/0000003" }, new[] { small.InvoiceNo, large.InvoiceNo, kept.InvoiceNo });

        // A credit note on a restaurant invoice comes only from the void.
        var req = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.RequestCreditAsync(h.Owner, small.InvoiceId!.Value, "full", ct: Ct));
        Assert.Contains("voiding its POS bill", req.Message, StringComparison.Ordinal);

        var v1 = (await r.Pos.VoidBillAsync(r.As(h.Cashier), Hotel.Key(), new VoidBillRequest(small.OrderId, "Wrong table billed", null), Ct)).Value;
        Assert.Equal(("Voided", "CN/2627/00000001"), (v1.Outcome, v1.CreditNoteNo));
        var pending = (await r.Pos.VoidBillAsync(r.As(h.Cashier), Hotel.Key(), new VoidBillRequest(large.OrderId, "Duplicate bill", null), Ct)).Value;
        Assert.Equal(("PendingApproval", (string?)null), (pending.Outcome, pending.CreditNoteNo));
        Assert.Equal(1, await CountAsync(h, "SELECT COUNT(*) FROM ivc.credit_note")); // nothing before the approval
        await r.Pos.DecideVoidAsync(r.As(h.Manager), Hotel.Key(), new DecideVoidRequest(pending.VoidRequestId!.Value, true, null), Ct);

        // Full reversal of each invoice: ₹360 + ₹18 (900 / 900) and ₹590 + ₹29.50 (1,475 / 1,475); nothing posted to any folio.
        var notes = await h.RowsAsync("SELECT n.credit_note_no, n.taxable_paise, n.cgst_paise, n.sgst_paise, n.total_paise, i.invoice_no, r.requested_by, n.approved_by, r.direct FROM ivc.credit_note n JOIN ivc.invoice i ON i.invoice_id = n.invoice_id JOIN ivc.credit_note_request r ON r.request_id = n.request_id ORDER BY n.credit_note_id",
            x => (x.GetString(0), x.GetInt64(1), x.GetInt64(2), x.GetInt64(3), x.GetInt64(4), x.GetString(5), x.GetInt32(6), x.GetInt32(7), x.GetBoolean(8)), Ct);
        Assert.Equal([("CN/2627/00000001", 36000L, 900L, 900L, 37800L, "RST/2627/0000001", h.Cashier, h.Cashier, true),
            ("CN/2627/00000002", 59000L, 1475L, 1475L, 61950L, "RST/2627/0000002", h.Cashier, h.Manager, true)], notes);
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM ivc.credit_note_line WHERE folio_charge_id IS NOT NULL"));
        Assert.Equal(2, await CountAsync(h, "SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CREDIT_NOTE_ISSUED'"));
        // The books: three PosBillSettled and two PosBillVoided (the void voucher Accounting already makes) — no ChargePosted for a credit note.
        Assert.Equal((3L, 2L, 0L), (await CountAsync(h, "SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'PosBillSettled'"),
            await CountAsync(h, "SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'PosBillVoided'"),
            await CountAsync(h, "SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ChargePosted'")));
        Assert.Equal("CN/2627/00000002", (await r.CheckViewAsync(large.OrderId)).Bill!.Invoice!.CreditNoteNo);
        var view = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, small.InvoiceId!.Value, Ct);
        Assert.Equal((0L, 37800L), (view.CreditableValuePaise, view.TotalPaise)); // the invoice is never changed; nothing is left to credit

        // GSTR-1: B2CS 5 % = 36,000 + 59,000 + 12,000 − 36,000 − 59,000 = 12,000 taxable, CGST 900 + 1,475 + 300 − 900 − 1,475 = 300;
        // documents in the row: 3 invoices + 2 credit notes. Table 13: RST 3 issued, 2 cancelled; CRN 2.
        var g = await w.Queries.Gstr1Async(h.PropertyId, h.Accountant, new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 24), Ct);
        Assert.Equal([("B2CS", 500, 12000L, 300L, 300L, 5)], g.Rates.Select(x => (x.Section, x.TaxBps, x.TaxablePaise, x.CgstPaise, x.SgstPaise, x.Documents)));
        Assert.Equal([("RST", "RST/2627/0000001", "RST/2627/0000003", 3, 2), ("CRN", "CN/2627/00000001", "CN/2627/00000002", 2, 0)],
            g.Documents.Select(d => (d.DocType, d.First, d.Last, d.Count, d.Cancelled)));
        // Voiding an open (never settled) check numbers nothing.
        var open = await r.CheckAsync("T6", ("TEA", 1));
        Assert.Null((await r.Pos.VoidBillAsync(r.As(h.Cashier), Hotel.Key(), new VoidBillRequest(open, "Guest left", null), Ct)).Value.CreditNoteNo);
        Assert.Equal(2, await CountAsync(h, "SELECT COUNT(*) FROM ivc.credit_note"));
    }

    [Fact]
    public async Task RST_numbering_is_gap_free_under_concurrency_and_rollback_and_restarts_on_1_April()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var r = await Restaurant.CreateAsync(h);
        // A command that takes an RST number and then fails: the number is not consumed (same transaction as the bill).
        var store = h.Get<IInvoiceStore>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get<ICommandRunner>().RunAsync(h.Ctx(h.Owner, "TestTakeNumber"), new { x = 1 }, async (tx, c) =>
        {
            await store.NextNumberAsync(tx, h.PropertyId, "2627", InvoiceDocType.RestaurantInvoice, "RST", DateTime.UtcNow, c);
            throw new InvalidOperationException("crash after numbering");
#pragma warning disable CS0162 // Unreachable code detected
            return new { ok = true };
#pragma warning restore CS0162
        }, Ct));

        var checks = new List<long>();
        foreach (var t in new[] { "T1", "T2", "T3", "T4", "T5", "T6" }) checks.Add(await r.CheckAsync(t, ("TEA", 1)));
        var settled = await Task.WhenAll(checks.Select(c => Task.Run(() => r.SettleUpiAsync(c), Ct)));
        Assert.Equal(Enumerable.Range(1, 6).Select(i => InvoiceNumbering.Format("RST", "2627", i)), settled.Select(s => s.InvoiceNo!).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(6, await CountAsync(h, "SELECT last_seq FROM ivc.series WHERE doc_type = 'RST' AND fy_code = '2627'"));

        // 31 March 2027 is still FY 2026-27; 1 April 2027 starts RST/2728 from 1. The RST series never touches INV or CN.
        await w.SetBusinessDateAsync("2027-03-31", Ct);
        var march = await r.SettleUpiAsync(await r.CheckAsync("T7", ("COFFEE", 1)));
        await w.SetBusinessDateAsync("2027-04-01", Ct);
        var april = await r.SettleUpiAsync(await r.CheckAsync("T8", ("COFFEE", 1)));
        var april2 = await r.SettleUpiAsync(await r.CheckAsync("T9", ("COFFEE", 1)));
        Assert.Equal(("RST/2627/0000007", "RST/2728/0000001", "RST/2728/0000002"), (march.InvoiceNo, april.InvoiceNo, april2.InvoiceNo));
        var invoiceDates = await h.RowsAsync("SELECT invoice_no, invoice_date FROM ivc.invoice WHERE invoice_no IN ('RST/2627/0000007', 'RST/2728/0000001') ORDER BY invoice_no",
            x => (x.GetString(0), DateOnly.FromDateTime(x.GetDateTime(1))), Ct);
        Assert.Equal([("RST/2627/0000007", new DateOnly(2027, 3, 31)), ("RST/2728/0000001", new DateOnly(2027, 4, 1))], invoiceDates);
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM ivc.series WHERE doc_type <> 'RST'"));
    }

    [Fact]
    public async Task Without_a_hotel_GSTIN_the_bill_settles_unnumbered_and_IssueInvoice_pos_issues_it_once_the_GSTIN_is_recorded()
    {
        await using var w = await InvoiceWorld.CreateAsync(withGstin: false, ct: Ct);
        var h = w.H;
        var r = await Restaurant.CreateAsync(h);
        var s = await r.SettleUpiAsync(await r.CheckAsync("T3", ("DAL", 1), ("ROTI", 2)));
        Assert.Null(s.InvoiceNo);
        Assert.Contains("(brand.gstin)", s.InvoiceNote, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM ivc.series"));
        Assert.Equal(1, await CountAsync(h, "SELECT COUNT(*) FROM plat.audit_event WHERE action = 'POS_INVOICE_NOT_ISSUED'"));
        Assert.Null((await r.CheckViewAsync(s.OrderId)).Bill!.Invoice);

        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{InvoiceRulesTestsGstin}' WHERE property_id = {h.PropertyId};", Ct);
        var key = Hotel.Key();
        var late = (await w.Invoices.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue, key), new IssueInvoiceRequest("pos", s.BillId), Ct)).Value;
        Assert.Equal(("RST/2627/0000001", "RST", 37800L, true), (late.InvoiceNo, late.DocType, late.TotalPaise, late.Created));
        var again = (await w.Invoices.IssueInvoiceAsync(h.Ctx(h.Cashier, InvoiceService.OpIssue), new IssueInvoiceRequest("pos", s.BillId), Ct)).Value;
        Assert.Equal((late.InvoiceId, false), (again.InvoiceId, again.Created)); // one invoice per bill
        Assert.Equal("RST/2627/0000001", (await r.CheckViewAsync(s.OrderId)).Bill!.Invoice!.InvoiceNo);
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => w.Invoices.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue), new IssueInvoiceRequest("pos", 999999), Ct));
    }

    [Fact]
    public async Task A03a_03_IssueInvoice_pos_refuses_an_inter_state_place_of_supply_and_numbers_nothing_the_hotels_state_is_issued()
    {
        await using var w = await InvoiceWorld.CreateAsync(withGstin: false, ct: Ct);
        var h = w.H;
        var r = await Restaurant.CreateAsync(h);
        var s = await r.SettleUpiAsync(await r.CheckAsync("T3", ("DAL", 1), ("ROTI", 2)));
        Assert.Null(s.InvoiceNo);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{InvoiceRulesTestsGstin}' WHERE property_id = {h.PropertyId};", Ct);

        foreach (var (supply, name, gstin) in new[] { ("B2B", "Karnataka Co (fictional)", "29AAGCB7383J1Z4"), ("B2C", null, (string?)null) })
        {
            var e = await Expect.Fails(HccErrorCode.VALIDATION, () => w.Invoices.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue),
                new IssueInvoiceRequest("pos", s.BillId, supply, name, gstin, null, "29"), Ct));
            Assert.StartsWith(InvoiceRules.InterStateRefusal, e.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await CountAsync(h, "SELECT COUNT(*) FROM ivc.series"));
        var issued = (await w.Invoices.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue),
            new IssueInvoiceRequest("pos", s.BillId, "B2B", "Karnataka Co (fictional)", "29AAGCB7383J1Z4", null, "27"), Ct)).Value;
        Assert.Equal("RST/2627/0000001", issued.InvoiceNo);
        var v = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, issued.InvoiceId, Ct);
        Assert.Equal(("B2B", "27", false, 0L), (v.SupplyType, v.PlaceOfSupply, v.InterState, v.IgstPaise));
    }
}
