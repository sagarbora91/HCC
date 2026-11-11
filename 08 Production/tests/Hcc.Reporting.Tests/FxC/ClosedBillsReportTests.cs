// FX-RPT-C · rpt.rest.closed-bills on a disposable HccTest_* database: settled checks of one business date in closing order, invoice number when issued (else the bill number), table or
// room, pieces on lines not voided, tender (room folio for a posted bill), server = who opened the check; a voided bill is left out; total row to the paise; the own-shift Cashier sees
// only the bills they settled (PERM-POS-004 at shift scope); Reception and Housekeeping refused; housekeeping refused at the hub (DEC-061).
using Hcc.Reporting.Tests.CuB;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Reporting.Reports.FxC;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.FxC;

[Trait("Category", "Sql")]
public sealed class ClosedBillsReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    // 23 Oct. Restaurant: c1 T1 (REC opened, OWN settled) UPI 2 Paneer + 2 Tea + one voided Tea = 630.00 with invoice RST/2627/0000001; c2 T2 (CSH opened and settled) Card 1 + 1 = 315.00.
    // Room service: c3 room 101 posted to the folio (OWN) 2 Tea = 105.00. c4 T6 settled then voided (left out).
    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            var (own, rec, csh) = (db.Users["OWN"], db.Users["REC"], db.Users["CSH"]);
            await db.ScalarAsync(RoomsSql(db, "101") + $"""
                DECLARE @r101 INT = (SELECT room_id FROM inv.room WHERE room_no = '101');
                INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source) VALUES
                  (@p, 'HCC-5001', N'Ananya Rao', '2026-10-22', '2026-10-26', 'InHouse', @r101, @t, 1, 0, 'Direct');
                INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no = 'HCC-5001';
                DECLARE @f BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-5001');
                INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES (@f, 'service', '2026-10-23', N'Restaurant bill', 10000, 500, 500);
                DECLARE @ch BIGINT = SCOPE_IDENTITY();
                INSERT pos.outlet(property_id, code, name, kind, outlet_type, created_utc) VALUES (@p, 'R1', N'Restaurant', 'table', 'restaurant', '2026-01-01'), (@p, 'R2', N'Room service', 'room', 'room-service', '2026-01-01');
                DECLARE @o1 INT = (SELECT outlet_id FROM pos.outlet WHERE code = 'R1'), @o2 INT = (SELECT outlet_id FROM pos.outlet WHERE code = 'R2');
                INSERT pos.menu_item(property_id, code, name, category, price_paise, veg) VALUES (@p, 'PAN', N'Paneer', N'Mains', 25000, 1), (@p, 'TEA', N'Tea', N'Drinks', 5000, 1);
                DECLARE @pan INT = (SELECT item_id FROM pos.menu_item WHERE code = 'PAN'), @tea INT = (SELECT item_id FROM pos.menu_item WHERE code = 'TEA');
                INSERT pos.check_order(property_id, outlet_id, table_no, room_id, guest, covers, business_date, opened_utc, opened_by, status) VALUES
                  (@p, @o1, 'T1', NULL, NULL, 2, '2026-10-23', '2026-10-23T12:00:00', {rec}, 'Settled'), (@p, @o1, 'T2', NULL, NULL, 3, '2026-10-23', '2026-10-23T13:00:00', {csh}, 'Settled'),
                  (@p, @o2, NULL, @r101, NULL, 1, '2026-10-23', '2026-10-23T14:00:00', {own}, 'Settled');
                DECLARE @c1 BIGINT = (SELECT order_id FROM pos.check_order WHERE table_no = 'T1'), @c2 BIGINT = (SELECT order_id FROM pos.check_order WHERE table_no = 'T2'),
                        @c3 BIGINT = (SELECT order_id FROM pos.check_order WHERE room_id = @r101);
                INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES
                  (@c1, @pan, N'Paneer', 25000, 2), (@c1, @tea, N'Tea', 5000, 2), (@c2, @pan, N'Paneer', 25000, 1), (@c2, @tea, N'Tea', 5000, 1), (@c3, @tea, N'Tea', 5000, 2);
                INSERT pos.order_line(order_id, item_id, name, price_paise, qty, voided, void_reason, void_by, void_date) VALUES (@c1, @tea, N'Tea', 5000, 1, 1, N'Wrong item', {own}, '2026-10-23');
                INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, folio_id, charge_id, status) VALUES
                  (@c1, @p, 'POS/2610/0001', 'outlet', N'UPI', 60000, 500, 3000, 63000, '2026-10-23', '2026-10-23T13:00:00', {own}, NULL, NULL, 'Settled'),
                  (@c2, @p, 'POS/2610/0002', 'outlet', N'Card', 30000, 500, 1500, 31500, '2026-10-23', '2026-10-23T14:00:00', {csh}, NULL, NULL, 'Settled'),
                  (@c3, @p, 'POS/2610/0003', 'folio', NULL, 10000, 500, 500, 10500, '2026-10-23', '2026-10-23T15:00:00', {own}, @f, @ch, 'Settled');
                INSERT pos.check_order(property_id, outlet_id, table_no, room_id, guest, covers, business_date, opened_utc, opened_by, status, void_reason, voided_by, voided_utc) VALUES
                  (@p, @o1, 'T6', NULL, NULL, 2, '2026-10-23', '2026-10-23T17:00:00', {own}, 'Void', N'Guest left', {own}, '2026-10-23T18:00:00');
                DECLARE @c4 BIGINT = (SELECT order_id FROM pos.check_order WHERE table_no = 'T6');
                INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES (@c4, @pan, N'Paneer', 25000, 4);
                INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, folio_id, charge_id, status, void_reason, voided_by, voided_utc) VALUES
                  (@c4, @p, 'POS/2610/0004', 'outlet', N'UPI', 100000, 500, 5000, 105000, '2026-10-23', '2026-10-23T17:30:00', {own}, NULL, NULL, 'Void', N'Guest left', {own}, '2026-10-23T18:00:00');
                INSERT ivc.series(property_id, fy_code, doc_type, prefix, last_seq, created_utc) VALUES (@p, '2627', 'RST', 'RST', 1, '2026-10-23');
                INSERT ivc.invoice(property_id, doc_type, invoice_no, fy_code, seq, invoice_date, source_kind, source_id, source_ref, supply_type, place_of_supply, supplier_name, supplier_gstin, supplier_state,
                    inter_state, taxable_paise, cgst_paise, sgst_paise, igst_paise, total_paise, amount_words, issued_by, issued_utc)
                  SELECT @p, 'RST', 'RST/2627/0000001', '2627', 1, '2026-10-23', 'pos', bill_id, 'POS/2610/0001', 'B2C', '27', N'Test Hotel', '27AAAAA0000A1Z5', '27', 0, 60000, 1500, 1500, 0, 63000, N'Rupees', {own}, '2026-10-23 13:00'
                    FROM pos.bill WHERE bill_no = 'POS/2610/0001';
                -- the second bill was corrected: invoices revision 1 and 2; only the latest number is shown, once
                INSERT ivc.invoice(property_id, doc_type, invoice_no, fy_code, seq, invoice_date, source_kind, source_id, source_ref, supply_type, place_of_supply, supplier_name, supplier_gstin, supplier_state,
                    inter_state, taxable_paise, cgst_paise, sgst_paise, igst_paise, total_paise, amount_words, issued_by, issued_utc, revision)
                  SELECT @p, 'RST', v.no, '2627', v.seq, '2026-10-23', 'pos', bill_id, 'POS/2610/0002', 'B2C', '27', N'Test Hotel', '27AAAAA0000A1Z5', '27', 0, 30000, 750, 750, 0, 31500, N'Rupees', {own}, '2026-10-23 14:00', v.seq - 1
                    FROM pos.bill CROSS JOIN (VALUES ('RST/2627/0000002', 2), ('RST/2627/0000003', 3)) v(no, seq) WHERE bill_no = 'POS/2610/0002';
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Hcc.Reporting.ReportRunner Runner() =>
        CuBRig.Runner(db, new ClosedBillsReport(db.Platform, new SqlAccessPolicy(db.Platform.Connections, new FixedClock(Rig.Now), [])));

    [Fact]
    public async Task Settled_checks_in_closing_order_with_invoice_numbers_and_a_total_to_the_paise()
    {
        var r = await Run(db, Runner(), "OWN", ClosedBillsReport.Id, ("from", "2026-10-23"));
        var bills = r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        Assert.Equal(["RST/2627/0000001", "RST/2627/0000003", "POS/2610/0003"], bills.Select(x => S(x, "check")));
        Assert.Equal(["Restaurant", "Restaurant", "Room service"], bills.Select(x => S(x, "outlet")));
        Assert.Equal(["Table T1", "Table T2", "Room 101"], bills.Select(x => S(x, "table")));
        Assert.Equal(["2026-10-23T18:30", "2026-10-23T19:30", "2026-10-23T20:30"], bills.Select(x => S(x, "closedAt")));
        Assert.Equal([4L, 2L, 2L], bills.Select(x => L(x, "items"))); // the voided Tea line is not counted
        Assert.Equal([63000L, 31500L, 10500L], bills.Select(x => L(x, "total")));
        Assert.Equal(["UPI", "Card", "Room folio"], bills.Select(x => S(x, "tender")));
        Assert.Equal(["REC", "CSH", "OWN"], bills.Select(x => S(x, "server")));
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("Total", 8L, 105000L), (S(total, "check"), L(total, "items"), L(total, "total")));
    }

    [Fact]
    public async Task The_cashier_sees_only_their_own_bills_and_an_empty_day_has_no_total_row()
    {
        var r = await Run(db, Runner(), "CSH", ClosedBillsReport.Id, ("from", "2026-10-23"));
        Assert.Equal(["RST/2627/0000003", "Total"], r.Rows.Select(x => S(x, "check")));
        Assert.Equal(31500L, L(r.Rows[1], "total"));
        Assert.Empty((await Run(db, Runner(), "ACC", ClosedBillsReport.Id, ("from", "2026-10-22"))).Rows);
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, Runner(), "OWN", ClosedBillsReport.Id, ("from", "2026-10-22"), ("to", "2026-10-23"))));
    }

    [Fact]
    public async Task Owner_manager_accountant_and_cashier_may_run_it_reception_and_housekeeping_are_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, ClosedBillsReport.Id, ("from", "2026-10-23"))));
        foreach (var role in new[] { "OWN", "MGR", "ACC", "CSH" })
            Assert.Null(await Code(() => Run(db, runner, role, ClosedBillsReport.Id, ("from", "2026-10-23"))));
        var svc = Service(db, new ClosedBillsReport(db.Platform, new SqlAccessPolicy(db.Platform.Connections, new FixedClock(Rig.Now), [])));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => svc.RunAsync(new Caller(db.PropertyId, db.Users["HKP"]), ClosedBillsReport.Id, new Dictionary<string, string?> { ["from"] = "2026-10-23" }, null, 50, true, Ct)));
    }
}
