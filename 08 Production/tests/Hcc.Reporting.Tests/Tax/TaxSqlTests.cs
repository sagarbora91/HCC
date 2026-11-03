// F7-R5 · batch 2 tax on a disposable HccTest_* database: SAC summary (Table 12), documents issued (Table 13 with the legacy LGC row), GST by bill, invoice register,
// bill corrections, GST summary. Real ivc / bill / pay / ctr / lgc rows; rule V through the engine runner (FakePolicy); screen = Excel = PDF = print on every result.
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Invoices;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Invoices;
using Hcc.Reporting.Reports.Tax;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.Tax;

[Trait("Category", "Sql")]
public sealed class TaxSqlTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;
    private readonly FakePolicy _policy = Policy();

    private static FakePolicy Policy()
    {
        var p = new FakePolicy();
        p.Grants["PERM-INV-001"] = ["OWN", "MGR", "REC", "CSH", "ACC"];
        p.Grants["PERM-ACC-001"] = ["OWN", "MGR", "ACC"];
        p.Grants["PERM-ACC-013"] = ["OWN", "ACC"]; // F7: tax reports Owner + Accountant (DEC-202; migration 4249)
        return p;
    }

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await Seed();
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Fictional month 2026-10 (business date 2026-10-24). Invoices: 1 (B2C: room 5 % + laundry 18 %), 2 (B2B: room 18 % and a discount line), 3 (B2C, corrected; superseded by 4),
    // 4 (the re-issued bill of folio C), 5 (counter bill on credit). Credit notes: 1 (all of invoice 3), 2 (50,000 of invoice 2). Legacy Lodge bills 1204 and 1205 (cancelled).
    private async Task Seed()
    {
        var p = db.PropertyId;
        var own = db.Users["OWN"];
        var mgr = db.Users["MGR"];
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT mst.floor(property_id, code, name, sort_order, updated_by, updated_utc) VALUES (@p, 'F1', N'Floor 1', 1, {own}, '2026-01-01');
            DECLARE @fl INT = SCOPE_IDENTITY();
            INSERT inv.room_type(property_id, code, max_occupancy, name) VALUES (@p, 'CLS', 3, N'Classic');
            DECLARE @cls INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = 'CLS');
            INSERT inv.room(property_id, room_no, room_type_id, hk_status, floor_id) VALUES (@p, '101', @cls, 'Ready', @fl), (@p, '102', @cls, 'Ready', @fl), (@p, '103', @cls, 'Ready', @fl);
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source) VALUES
              (@p, 'HCC-2001', N'Ananya Rao', '2026-10-10', '2026-10-12', 'Departed', (SELECT room_id FROM inv.room WHERE room_no = '101'), @cls, 1, 0, 'Direct'),
              (@p, 'HCC-2002', N'Demo Traders Pvt Ltd', '2026-10-13', '2026-10-15', 'Departed', (SELECT room_id FROM inv.room WHERE room_no = '102'), @cls, 2, 0, 'Direct'),
              (@p, 'HCC-2003', N'Claire Dupont', '2026-10-08', '2026-10-10', 'Departed', (SELECT room_id FROM inv.room WHERE room_no = '103'), @cls, 1, 0, 'Direct');
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no IN ('HCC-2001','HCC-2002','HCC-2003');
            DECLARE @fa BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2001'),
                    @fb BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2002'),
                    @fc BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2003');
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES
              (@fa, 'payment', 'Cash', 80000, '2026-10-12'), (@fa, 'payment', 'UPI', 48600, '2026-10-12'),
              (@fb, 'company-credit', 'Credit', 212400, '2026-10-15'),
              (@fc, 'payment', 'UPI', 105000, '2026-10-20');
            INSERT ivc.series(property_id, fy_code, doc_type, prefix, last_seq, created_utc) VALUES (@p, '2627', 'INV', 'INV', 5, '2026-10-01'), (@p, '2627', 'CRN', 'CRN', 2, '2026-10-01');
            DECLARE @ph CHAR(15) = '27AAAAA0000A1Z5';
            INSERT ivc.invoice(property_id, doc_type, invoice_no, fy_code, seq, invoice_date, source_kind, source_id, source_ref, supply_type, buyer_name, buyer_gstin, place_of_supply, supplier_name,
                               supplier_gstin, supplier_state, inter_state, taxable_paise, cgst_paise, sgst_paise, igst_paise, total_paise, amount_words, issued_by, issued_utc, revision) VALUES
              (@p, 'INV', 'INV/2627/0000001', '2627', 1, '2026-10-12', 'folio', @fa, N'HCC-2001', 'B2C', N'Ananya Rao', NULL, '27', N'Test Hotel', @ph, '27', 0, 120000, 4300, 4300, 0, 128600, N'x', {own}, '2026-10-12', 1),
              (@p, 'INV', 'INV/2627/0000002', '2627', 2, '2026-10-15', 'folio', @fb, N'HCC-2002', 'B2B', N'Demo Traders Pvt Ltd', '27BBBBB1111B1Z6', '27', N'Test Hotel', @ph, '27', 0, 180000, 16200, 16200, 0, 212400, N'x', {own}, '2026-10-15', 1),
              (@p, 'INV', 'INV/2627/0000003', '2627', 3, '2026-10-10', 'folio', @fc, N'HCC-2003', 'B2C', N'Claire Dupont', NULL, '27', N'Test Hotel', @ph, '27', 0, 150000, 3750, 3750, 0, 157500, N'x', {own}, '2026-10-10', 1),
              (@p, 'INV', 'INV/2627/0000004', '2627', 4, '2026-10-20', 'folio', @fc, N'HCC-2003', 'B2C', N'Claire Dupont', NULL, '27', N'Test Hotel', @ph, '27', 0, 100000, 2500, 2500, 0, 105000, N'x', {own}, '2026-10-20', 2),
              (@p, 'INV', 'INV/2627/0000005', '2627', 5, '2026-10-18', 'counter', 9001, N'Counter 9001', 'B2C', N'Walk-in Customer', NULL, '27', N'Test Hotel', @ph, '27', 0, 50000, 4500, 4500, 0, 59000, N'x', {own}, '2026-10-18', 1);
            DECLARE @i1 BIGINT = (SELECT invoice_id FROM ivc.invoice WHERE invoice_no = 'INV/2627/0000001'), @i2 BIGINT = (SELECT invoice_id FROM ivc.invoice WHERE invoice_no = 'INV/2627/0000002'),
                    @i3 BIGINT = (SELECT invoice_id FROM ivc.invoice WHERE invoice_no = 'INV/2627/0000003'), @i4 BIGINT = (SELECT invoice_id FROM ivc.invoice WHERE invoice_no = 'INV/2627/0000004'),
                    @i5 BIGINT = (SELECT invoice_id FROM ivc.invoice WHERE invoice_no = 'INV/2627/0000005');
            INSERT ivc.invoice_line(invoice_id, line_no, kind, service_date, description, category, sac, taxable_paise, tax_bps, tax_paise, cgst_paise, sgst_paise, igst_paise) VALUES
              (@i1, 1, 'room', '2026-10-11', N'Room night', 'room', '996311', 100000, 500, 5000, 2500, 2500, 0),
              (@i1, 2, 'service', '2026-10-11', N'Laundry', 'laundry', '999712', 20000, 1800, 3600, 1800, 1800, 0),
              (@i2, 1, 'room', '2026-10-14', N'Room night', 'room', '996311', 200000, 1800, 36000, 18000, 18000, 0),
              (@i2, 2, 'service', '2026-10-14', N'Discount', 'room', '996311', -20000, 1800, -3600, -1800, -1800, 0),
              (@i3, 1, 'room', '2026-10-09', N'Room night', 'room', '996311', 150000, 500, 7500, 3750, 3750, 0),
              (@i4, 1, 'room', '2026-10-09', N'Room night (corrected)', 'room', '996311', 100000, 500, 5000, 2500, 2500, 0),
              (@i5, 1, 'service', '2026-10-18', N'Courier', 'other', '999799', 50000, 1800, 9000, 4500, 4500, 0);
            INSERT ivc.credit_note_request(property_id, invoice_id, mode, reason, status, direct, requested_by, requested_utc, expires_utc, decided_by, decided_utc) VALUES
              (@p, @i3, 'full', N'Bill correction', 'Issued', 1, {own}, '2026-10-20', '2026-10-21', {own}, '2026-10-20'),
              (@p, @i2, 'lines', N'Wrong rate', 'Issued', 1, {own}, '2026-10-22', '2026-10-23', {own}, '2026-10-22');
            DECLARE @q1 BIGINT = (SELECT request_id FROM ivc.credit_note_request WHERE invoice_id = @i3), @q2 BIGINT = (SELECT request_id FROM ivc.credit_note_request WHERE invoice_id = @i2);
            INSERT ivc.credit_note(property_id, credit_note_no, fy_code, seq, note_date, invoice_id, request_id, mode, reason, taxable_paise, cgst_paise, sgst_paise, igst_paise, total_paise, amount_words, issued_by, approved_by, issued_utc) VALUES
              (@p, 'CRN/2627/0000001', '2627', 1, '2026-10-20', @i3, @q1, 'full', N'Bill correction', 150000, 3750, 3750, 0, 157500, N'x', {own}, {own}, '2026-10-20'),
              (@p, 'CRN/2627/0000002', '2627', 2, '2026-10-22', @i2, @q2, 'lines', N'Wrong rate', 50000, 4500, 4500, 0, 59000, N'x', {own}, {own}, '2026-10-22');
            INSERT ivc.credit_note_line(credit_note_id, line_no, invoice_id, invoice_line_no, category, sac, taxable_paise, tax_bps, tax_paise, cgst_paise, sgst_paise, igst_paise) VALUES
              ((SELECT credit_note_id FROM ivc.credit_note WHERE credit_note_no = 'CRN/2627/0000001'), 1, @i3, 1, 'room', '996311', 150000, 500, 7500, 3750, 3750, 0),
              ((SELECT credit_note_id FROM ivc.credit_note WHERE credit_note_no = 'CRN/2627/0000002'), 1, @i2, 1, 'room', '996311', 50000, 1800, 9000, 4500, 4500, 0);
            INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, reason, status, requested_by, requested_utc, expires_utc, decided_by, decided_utc)
              VALUES (@p, 'correction', @fc, 'folio', @fc, 52500, N'Wrong rate', 'Executed', {mgr}, '2026-10-20', '2026-10-21', {own}, '2026-10-20');
            INSERT ivc.bill_correction(property_id, folio_id, old_invoice_id, credit_note_no, new_invoice_id, approval_id, reason, requested_by, requested_utc, approved_by, decided_utc, status, old_total_paise,
                                       new_total_paise, delta_paise, settlement)
              VALUES (@p, @fc, @i3, 'CRN/2627/0000001', @i4, (SELECT TOP (1) approval_id FROM bill.approval), N'Wrong rate', {mgr}, '2026-10-20', {own}, '2026-10-20T10:00:00', 'Issued', 157500, 105000, -52500, 'refund-due');
            INSERT ctr.bill(property_id, invoice_id, invoice_no, business_date, issued_utc, issued_by, party_name, party_phone, net_paise, tax_paise, cgst_paise, sgst_paise, total_paise, settlement_mode, status)
              VALUES (@p, @i5, 'INV/2627/0000005', '2026-10-18', '2026-10-18', {own}, N'Walk-in Customer', '9876500003', 50000, 9000, 4500, 4500, 59000, 'udhaar', 'Issued');
            INSERT lgc.bill(property_id, legacy_fy, legacy_bill_no, legacy_transaction_id, bill_date, bill_utc, room_no, room_charge_paise, extra_paise, fb_paise, laundry_paise, other_paise, discount_paise,
                            room_gst_bps, room_gst_paise, fb_gst_paise, laundry_gst_paise, other_gst_paise, advance_paise, net_paise, balance_paise, refund_paise, data_flag, paid_flag, b2b, out_of_fy, cancelled)
              VALUES (@p, '2026-27', 1204, 1, '2026-10-05', '2026-10-05', '101', 100000, 0, 0, 0, 0, 0, 500, 5000, 0, 0, 0, 0, 105000, 0, 0, 'C', 'Y', 0, 0, 0),
                     (@p, '2026-27', 1205, 2, '2026-10-06', '2026-10-06', '102', 100000, 0, 0, 0, 0, 0, 500, 5000, 0, 0, 0, 0, 105000, 0, 0, 'C', 'Y', 0, 0, 1);
            SELECT 1;
            """);
    }

    private ReportRunner Runner()
    {
        var store = new SqlInvoiceStore(db.Platform.Connections);
        var queries = new InvoiceQueries(store, _policy, null!);
        var summary = new GstSummaryQueries(new SqlAccountingStore(db.Platform.Connections), new SqlGstMonthStore(db.Platform.Connections), _policy, new Gstr1Port(queries));
        return Rig.Runner(_policy, new SacSummaryReport(queries, store), new DocumentsIssuedReport(queries), new GstByBillReport(store, db.Platform), new InvoiceRegisterReport(store, db.Platform),
            new BillCorrectionsReport(db.Platform), new GstSummaryReport(summary));
    }

    private sealed class Gstr1Port(InvoiceQueries q) : IGstr1Source
    {
        public Task<Gstr1View> GetAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default) => q.Gstr1UncheckedAsync(caller.PropertyId, from, to, ct);
    }

    private async Task<ReportResult> Run(int principal, string id, params (string K, string V)[] raw)
    {
        var d = raw.ToDictionary(x => x.K, x => (string?)x.V);
        var r = await Runner().RunAsync(new Caller(db.PropertyId, principal), id, d, new RunOptions(All: true), Ct);
        RendererTests.ThreeWay(r);
        return r;
    }

    private static async Task<HccErrorCode?> Code(Func<Task> f)
    {
        try { await f(); return null; }
        catch (HccException e) { return e.Code; }
    }

    private static List<ReportRow> Data(ReportResult r) => r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();

    private static string S(ReportRow r, string k) => r.Cells[k] is DateOnly d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Convert.ToString(r.Cells[k], CultureInfo.InvariantCulture) ?? "";

    private static long L(ReportRow r, string k) => Convert.ToInt64(r.Cells[k], CultureInfo.InvariantCulture);

    [Fact]
    public async Task Sac_summary_is_net_of_credit_notes_with_quantity_and_ties_to_the_gstr1_summary()
    {
        var r = await Run(1, SacSummaryReport.Id, ("month", "2026-10"));
        var d = Data(r);
        Assert.Equal([("996311", 500.0 / 100), ("996311", 18.0), ("999712", 18.0), ("999799", 18.0)], d.Select(x => (S(x, "sac"), (double)x.Cells["rate"]!)));
        Assert.Equal([200000L, 130000L, 20000L, 50000L], d.Select(x => L(x, "taxable")));
        Assert.Equal([5000L, 11700L, 1800L, 4500L], d.Select(x => L(x, "cgst")));
        Assert.Equal([4L, 4L, 1L, 1L], d.Select(x => L(x, "quantity")));
        Assert.Equal("Room or unit accommodation", S(d[0], "description"));
        Assert.Equal("OTH", S(d[0], "uqc"));
        var total = r.Rows[^1];
        Assert.Equal(ReportRowKind.Total, total.Kind);
        Assert.Equal(400000L, L(total, "taxable"));
        Assert.Equal(23000L, L(total, "cgst"));
        Assert.Equal(L(total, "taxable") + L(total, "cgst") + L(total, "sgst"), L(total, "totalValue"));
        Assert.Equal(d.Sum(x => L(x, "totalValue")), L(total, "totalValue"));
    }

    [Fact]
    public async Task Documents_issued_lists_the_legacy_series_first_then_each_series_with_real_cancelled_counts()
    {
        var r = await Run(1, DocumentsIssuedReport.Id, ("month", "2026-10"));
        var d = Data(r);
        Assert.Equal(["LGC", "INV", "CRN"], d.Select(x => S(x, "series")));
        Assert.Equal(("1204", "1205", 2L, 1L, 1L), (S(d[0], "fromNo"), S(d[0], "toNo"), L(d[0], "total"), L(d[0], "cancelled"), L(d[0], "net")));
        Assert.Equal(("INV/2627/0000001", "INV/2627/0000005", 5L, 0L, 5L), (S(d[1], "fromNo"), S(d[1], "toNo"), L(d[1], "total"), L(d[1], "cancelled"), L(d[1], "net")));
        Assert.Equal(("CRN/2627/0000001", "CRN/2627/0000002", 2L), (S(d[2], "fromNo"), S(d[2], "toNo"), L(d[2], "total")));
        Assert.Equal("Credit notes", S(d[2], "nature"));
        var total = r.Rows[^1];
        Assert.Equal((9L, 1L, 8L), (L(total, "total"), L(total, "cancelled"), L(total, "net")));
        Assert.All(r.Columns, c => Assert.False(c.Money));
        var empty = await Run(1, DocumentsIssuedReport.Id, ("month", "2026-09"));
        Assert.Single(empty.Rows); // only the total row
    }

    [Fact]
    public async Task Gst_by_bill_ties_to_the_sac_summary_lists_credit_notes_negative_and_has_a_line_level()
    {
        var r = await Run(1, GstByBillReport.Id, ("month", "2026-10"));
        var d = Data(r);
        Assert.Equal(["INV/2627/0000003", "INV/2627/0000001", "INV/2627/0000002", "INV/2627/0000005", "INV/2627/0000004", "CRN/2627/0000001", "CRN/2627/0000002"], d.Select(x => S(x, "billNo")));
        var b2b = d.Single(x => S(x, "billNo") == "INV/2627/0000002");
        Assert.Equal(("Demo Traders Pvt Ltd", "27BBBBB1111B1Z6", "102", 180000L, 180000L, 16200L), (S(b2b, "party"), S(b2b, "gstin"), S(b2b, "room"), L(b2b, "net"), L(b2b, "tax18"), L(b2b, "cgst")));
        Assert.Equal(-150000L, L(d.Single(x => S(x, "billNo") == "CRN/2627/0000001"), "net"));
        var total = r.Rows[^1];
        Assert.Equal((400000L, 200000L, 200000L, 23000L, 23000L), (L(total, "net"), L(total, "tax5"), L(total, "tax18"), L(total, "cgst"), L(total, "sgst")));
        Assert.Equal(d.Sum(x => L(x, "net")), L(total, "net"));
        foreach (var x in d) Assert.Equal(L(x, "net"), L(x, "tax0") + L(x, "tax5") + L(x, "tax12") + L(x, "tax18") + L(x, "tax28"));
        var sac = await Run(1, SacSummaryReport.Id, ("month", "2026-10"));
        Assert.Equal(L(sac.Rows[^1], "taxable"), L(total, "net"));
        var line = await Run(1, GstByBillReport.Id, ("month", "2026-10"), ("level", "line"));
        Assert.Contains(line.Columns, c => c.Key == "item");
        Assert.Equal(9, Data(line).Count);
        Assert.Equal(L(total, "net"), L(line.Rows[^1], "net"));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, GstByBillReport.Id, ("month", "2026-10"), ("level", "sideways"))));
    }

    [Fact]
    public async Task Invoice_register_splits_tariff_discount_gst_and_payments_and_filters_by_series_and_type()
    {
        var r = await Run(1, InvoiceRegisterReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"));
        var d = Data(r);
        Assert.Equal(["INV/2627/0000001", "INV/2627/0000002", "INV/2627/0000003", "INV/2627/0000004", "INV/2627/0000005", "CRN/2627/0000001", "CRN/2627/0000002"], d.Select(x => S(x, "invoiceNo")));
        var one = d[0];
        Assert.Equal((100000L, 20000L, 0L, 8600L, 128600L, 80000L, 48600L, 0L, "Issued", "101"), (L(one, "tariff"), L(one, "other"), L(one, "discount"), L(one, "gst"), L(one, "net"), L(one, "cash"), L(one, "cardUpi"), L(one, "credit"), S(one, "status"), S(one, "room")));
        var two = d[1];
        Assert.Equal((200000L, -20000L, 32400L, 212400L, 212400L), (L(two, "tariff"), L(two, "discount"), L(two, "gst"), L(two, "net"), L(two, "credit")));
        Assert.Equal(("Corrected", 0L), (S(d[2], "status"), L(d[2], "cash") + L(d[2], "cardUpi")));
        Assert.Equal(105000L, L(d[3], "cardUpi")); // payments sit on the current invoice of the folio
        Assert.Equal(("", 59000L), (S(d[4], "room"), L(d[4], "credit")));
        Assert.Equal((-150000L, -157500L), (L(d[5], "tariff"), L(d[5], "net")));
        foreach (var x in d) Assert.Equal(L(x, "net"), L(x, "tariff") + L(x, "other") + L(x, "discount") + L(x, "gst") + L(x, "roundOff"));
        var total = r.Rows[^1];
        Assert.Equal((446000L, 46000L), (L(total, "net"), L(total, "gst")));
        Assert.Equal(d.Sum(x => L(x, "net")), L(total, "net"));
        Assert.Equal(2, Data(await Run(1, InvoiceRegisterReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"), ("series", "CRN"))).Count);
        Assert.Equal(4, Data(await Run(1, InvoiceRegisterReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"), ("billType", "room"))).Count);
        Assert.Equal(["INV/2627/0000005"], Data(await Run(1, InvoiceRegisterReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"), ("billType", "counter"))).Select(x => S(x, "invoiceNo")));
        Assert.Equal(2, Data(await Run(1, InvoiceRegisterReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"), ("billType", "credit note"))).Count);
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, InvoiceRegisterReport.Id, ("from", "2026-04-01"), ("to", "2026-10-24"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, InvoiceRegisterReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"), ("series", "XYZ"))));
    }

    [Fact]
    public async Task Bill_corrections_show_old_and_new_invoice_amounts_gst_and_who()
    {
        var r = await Run(1, BillCorrectionsReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"));
        var d = Data(r);
        var x = Assert.Single(d);
        Assert.Equal(("INV/2627/0000003", "INV/2627/0000004", "Claire Dupont", "103"), (S(x, "oldNo"), S(x, "newNo"), S(x, "guest"), S(x, "room")));
        Assert.Equal((157500L, 105000L, -52500L, 7500L, 5000L), (L(x, "oldAmount"), L(x, "newAmount"), L(x, "difference"), L(x, "oldGst"), L(x, "newGst")));
        Assert.Equal(("Wrong rate", "MGR", "OWN"), (S(x, "reason"), S(x, "correctedBy"), S(x, "approvedBy")));
        Assert.Matches("^2026-10-20 \\d\\d:\\d\\d$", S(x, "correctedAt"));
        var total = r.Rows[^1];
        Assert.Equal((-52500L, "Total · 1 correction"), (L(total, "difference"), S(total, "correctedAt")));
        Assert.Empty(Data(await Run(1, BillCorrectionsReport.Id, ("from", "2026-10-01"), ("to", "2026-10-19"))));
        Assert.Empty(Data(await Run(1, BillCorrectionsReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"), ("correctedBy", "OWN"))));
        Assert.Single(Data(await Run(1, BillCorrectionsReport.Id, ("from", "2026-10-01"), ("to", "2026-10-24"), ("correctedBy", "mgr"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, BillCorrectionsReport.Id, ("from", "2026-04-01"), ("to", "2026-10-24"))));
    }

    [Fact]
    public async Task Gst_summary_runs_on_the_acc3_queries_with_tie_flags()
    {
        var r = await Run(1, GstSummaryReport.Id, ("month", "2026-10"));
        var line = r.Rows.ToDictionary(x => S(x, "line"), x => x, StringComparer.Ordinal);
        Assert.Equal(("Output GST by rate", ReportRowKind.Subtotal), (S(r.Rows[0], "line"), r.Rows[0].Kind));
        // GSTR-1 tax: 5 % = (2500+2500) + (3750+3750) + (2500+2500) - (3750+3750) ; 18 % = 3600 + 32400 - 9000 + 9000 ...
        Assert.Equal(10000L, L(line["Rate 5 %"], "amount"));
        Assert.Equal(46000L - 10000L, L(line["Rate 18 %"], "amount"));
        Assert.IsType<bool>(r.Rows[^1].Cells["tiesGstr1"]);
        Assert.Equal(ReportRowKind.Total, r.Rows[^1].Kind);
        Assert.Contains(line.Keys, k => k.StartsWith("Difference: ", StringComparison.Ordinal)); // no vouchers booked in this fixture: the books do not tie
    }

    [Fact]
    public async Task Roles_follow_the_permissions_and_housekeeping_is_refused()
    {
        // DEC-202 (coordinator ruling): tax reports are Owner + Accountant only (PERM-ACC-013); Manager, Reception, Cashier, Housekeeping refused.
        foreach (var p in new[] { 1, 5 }) Assert.Null(await Code(() => Run(p, InvoiceRegisterReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"))));
        foreach (var p in new[] { 2, 3, 4, 7 })
            foreach (var id in new[] { SacSummaryReport.Id, DocumentsIssuedReport.Id, GstByBillReport.Id, InvoiceRegisterReport.Id, GstSummaryReport.Id })
                Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(p, id)));
        foreach (var p in new[] { 1, 2, 5 }) Assert.Null(await Code(() => Run(p, BillCorrectionsReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"))));
        foreach (var p in new[] { 3, 4, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(p, BillCorrectionsReport.Id)));
    }
}
