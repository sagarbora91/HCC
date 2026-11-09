// [F8E-SQL] First SQL Server execution of the report reads that had only fake-source tests: GRN register, sales by item, POS voids, leave balances (SqlP2CReportSource),
// counter bills and cash-count grids (SqlP2BReportSource). Each test seeds its own dates, asserts exact paise / quantities / day counts from the real SELECT, then feeds the
// facts to the report's own pure builder for totals. Disposable HccTest_* database. Fictional data. Tests only.
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Counter;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using Hcc.Reporting.Reports.P2B;
using Hcc.Reporting.Reports.P2C;

namespace Hcc.Reporting.Tests.Sql;

[Trait("Category", "Sql")]
public sealed class F8ESqlGapTests(ReportDatabase db) : IClassFixture<ReportDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BoundParams Bound(DateOnly? from, DateOnly? to) => new(new Dictionary<string, string>(), from, to);

    private async Task<int> OtherPropertyAsync(string code)
    {
        await db.ScalarAsync($"IF NOT EXISTS (SELECT 1 FROM inv.property WHERE code = '{code}') INSERT inv.property(code, name, time_zone, business_date) VALUES ('{code}', N'Other Hotel {code}', 'Asia/Kolkata', '2026-10-24'); SELECT 1;");
        return Convert.ToInt32(await db.ScalarAsync($"SELECT property_id FROM inv.property WHERE code = '{code}';"), System.Globalization.CultureInfo.InvariantCulture);
    }

    // ── GRN register ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grn_register_reads_lines_with_exact_paise_range_property_and_blank_bill_when_ref_is_the_GRN_number()
    {
        var p = db.PropertyId;
        var other = await OtherPropertyAsync("ZZ2");
        await db.ScalarAsync($"""
            DECLARE @p INT = {p}, @o INT = {other};
            INSERT stk.item(property_id, code, name, kind, unit, cost_paise, min_qty, reorder_qty, created_utc) VALUES
              (@p, 'TWL', N'Bath towel', 'linen', 'pcs', 15000, 10, 40, '2026-01-01'), (@p, 'SOAP', N'Soap bar', 'amenity', 'pcs', 1200, 50, 200, '2026-01-01'),
              (@o, 'TWL', N'Bath towel', 'linen', 'pcs', 15000, 10, 40, '2026-01-01');
            DECLARE @twl INT = (SELECT item_id FROM stk.item WHERE property_id = @p AND code = 'TWL'), @soap INT = (SELECT item_id FROM stk.item WHERE property_id = @p AND code = 'SOAP'),
                    @otwl INT = (SELECT item_id FROM stk.item WHERE property_id = @o AND code = 'TWL');
            -- G1: supplier invoice number kept as the bill; two lines
            INSERT stk.grn(property_id, business_date, supplier, ref, created_by, created_utc) VALUES (@p, '2026-11-02', N'Alpha Linen', N'BILL-77', 1, '2026-11-02 05:00');
            DECLARE @g1 BIGINT = SCOPE_IDENTITY();
            INSERT stk.grn_line(grn_id, item_id, qty, cost_paise) VALUES (@g1, @twl, 40, 15050), (@g1, @soap, 200, 1275);
            -- G2: ref equal to the GRN number = no supplier bill
            INSERT stk.grn(property_id, business_date, supplier, ref, created_by, created_utc) VALUES (@p, '2026-11-03', N'Beta Traders', N'tmp', 1, '2026-11-03 05:00');
            DECLARE @g2 BIGINT = SCOPE_IDENTITY();
            UPDATE stk.grn SET ref = 'GRN-' + CAST(@g2 AS VARCHAR(20)) WHERE grn_id = @g2;
            INSERT stk.grn_line(grn_id, item_id, qty, cost_paise) VALUES (@g2, @twl, 10, 14999), (@g2, @soap, 3, 1275);
            -- G3 outside the range; G4 another property on an in-range date
            INSERT stk.grn(property_id, business_date, supplier, ref, created_by, created_utc) VALUES (@p, '2026-11-09', N'Alpha Linen', N'BILL-80', 1, '2026-11-09 05:00');
            INSERT stk.grn_line(grn_id, item_id, qty, cost_paise) VALUES (SCOPE_IDENTITY(), @twl, 99, 99999);
            INSERT stk.grn(property_id, business_date, supplier, ref, created_by, created_utc) VALUES (@o, '2026-11-02', N'Elsewhere', N'X-1', 1, '2026-11-02 05:00');
            INSERT stk.grn_line(grn_id, item_id, qty, cost_paise) VALUES (SCOPE_IDENTITY(), @otwl, 7, 7777);
            SELECT 1;
            """);
        var g1 = Convert.ToInt64(await db.ScalarAsync($"SELECT grn_id FROM stk.grn WHERE property_id = {p} AND supplier = N'Alpha Linen' AND business_date = '2026-11-02';"), System.Globalization.CultureInfo.InvariantCulture);
        var g2 = Convert.ToInt64(await db.ScalarAsync($"SELECT grn_id FROM stk.grn WHERE property_id = {p} AND supplier = N'Beta Traders';"), System.Globalization.CultureInfo.InvariantCulture);
        var src = new SqlP2CReportSource(db.Platform.Connections);

        var facts = await src.GrnsAsync(p, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), Ct);
        Assert.Equal(
        [
            new GrnFact($"GRN-{g1}", new DateOnly(2026, 11, 2), "Alpha Linen", "Bath towel", 40, 15050, "BILL-77"),
            new GrnFact($"GRN-{g1}", new DateOnly(2026, 11, 2), "Alpha Linen", "Soap bar", 200, 1275, "BILL-77"),
            new GrnFact($"GRN-{g2}", new DateOnly(2026, 11, 3), "Beta Traders", "Bath towel", 10, 14999, ""),
            new GrnFact($"GRN-{g2}", new DateOnly(2026, 11, 3), "Beta Traders", "Soap bar", 3, 1275, ""),
        ], facts);

        // The report's own builder on those rows: amount = qty x unit cost in paise.
        var def = new GrnRegisterReport(src).Definition;
        var r = GrnRegisterReport.Build(def, Bound(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5)), facts);
        var lines = r.Sections![0].Rows;
        Assert.Equal(602000L, lines[0].Cells["amount"]);          // 40 x 15050
        Assert.Equal(255000L, lines[1].Cells["amount"]);          // 200 x 1275
        Assert.Equal(149990L, lines[2].Cells["amount"]);          // 10 x 14999
        Assert.Equal(3825L, lines[3].Cells["amount"]);            // 3 x 1275
        Assert.Equal((ReportRowKind.Total, 253L, 1010815L), (lines[4].Kind, lines[4].Cells["qty"], lines[4].Cells["amount"]));
        Assert.Null(lines[2].Cells["bill"]);
        var sup = r.Sections![1].Rows;
        Assert.Equal(("Alpha Linen", 1L, 240L, 857000L), ((string)sup[0].Cells["supplier"]!, (long)sup[0].Cells["grns"]!, (long)sup[0].Cells["qty"]!, (long)sup[0].Cells["amount"]!));
        Assert.Equal(("Beta Traders", 1L, 13L, 153815L), ((string)sup[1].Cells["supplier"]!, (long)sup[1].Cells["grns"]!, (long)sup[1].Cells["qty"]!, (long)sup[1].Cells["amount"]!));

        // Edge: a one-day range is inclusive on both ends; an empty period gives no rows and no total.
        Assert.Equal([$"GRN-{g2}", $"GRN-{g2}"], (await src.GrnsAsync(p, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3), Ct)).Select(x => x.GrnNo).ToArray());
        var none = await src.GrnsAsync(p, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 8), Ct);
        Assert.Empty(none);
        Assert.Empty(GrnRegisterReport.Build(def, Bound(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 8)), none).Sections![0].Rows);
        // The other property's receipt never shows under this property.
        Assert.DoesNotContain(facts, f => f.Supplier == "Elsewhere");
        Assert.Equal(7L, Assert.Single(await src.GrnsAsync(other, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), Ct)).Qty);
    }

    // ── POS seed shared by sales and voids ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<(int RestOutlet, int BarOutlet, int Paneer, int Tea, int Snack)> PosSeedAsync()
    {
        var p = db.PropertyId;
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            IF NOT EXISTS (SELECT 1 FROM pos.outlet WHERE property_id = @p AND code = 'REST')
            BEGIN
              INSERT pos.outlet(property_id, code, name, kind, created_utc) VALUES (@p, 'REST', N'Restaurant', 'table', '2026-01-01'), (@p, 'BAR', N'Bar', 'table', '2026-01-01');
              INSERT pos.menu_category(property_id, code, name, sort_order) VALUES (@p, 'CAT-A', N'Mains', 10), (@p, 'CAT-B', N'Drinks', 20);
              DECLARE @a INT = (SELECT category_id FROM pos.menu_category WHERE property_id = @p AND code = 'CAT-A'), @b INT = (SELECT category_id FROM pos.menu_category WHERE property_id = @p AND code = 'CAT-B');
              INSERT pos.menu_item(property_id, code, name, category, price_paise, veg, category_id) VALUES
                (@p, 'PNR', N'Paneer tikka', N'Mains', 25000, 1, @a), (@p, 'TEA', N'Tea', N'Drinks', 5000, 1, @b),
                (@p, 'SNK', N'Chakli', N'Snacks', 8000, 1, NULL);           -- no category row: the legacy text column is the category
            END
            SELECT 1;
            """);
        async Task<int> Id(string sql) => Convert.ToInt32(await db.ScalarAsync(sql), System.Globalization.CultureInfo.InvariantCulture);
        return (await Id($"SELECT outlet_id FROM pos.outlet WHERE property_id = {p} AND code = 'REST';"), await Id($"SELECT outlet_id FROM pos.outlet WHERE property_id = {p} AND code = 'BAR';"),
            await Id($"SELECT item_id FROM pos.menu_item WHERE property_id = {p} AND code = 'PNR';"), await Id($"SELECT item_id FROM pos.menu_item WHERE property_id = {p} AND code = 'TEA';"),
            await Id($"SELECT item_id FROM pos.menu_item WHERE property_id = {p} AND code = 'SNK';"));
    }

    // ── sales by item ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sales_by_item_reads_settled_lines_pre_tax_across_rates_and_leaves_out_voided_lines_voided_bills_open_checks_and_other_dates()
    {
        var p = db.PropertyId;
        var rec = db.Users["REC"];
        var own = db.Users["OWN"];
        var (rest, bar, paneer, tea, snack) = await PosSeedAsync();
        await db.ScalarAsync($"""
            DECLARE @p INT = {p}, @rest INT = {rest}, @bar INT = {bar}, @pnr INT = {paneer}, @tea INT = {tea}, @snk INT = {snack};
            -- C1: restaurant, 5 % bill, a voided tea line that must not count
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status) VALUES (@p, @rest, 'T1', 2, '2026-11-12', '2026-11-12 06:00', {rec}, 'Settled');
            DECLARE @c1 BIGINT = SCOPE_IDENTITY();
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES (@c1, @pnr, N'Paneer tikka', 25000, 2), (@c1, @tea, N'Tea', 5000, 3);
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty, voided, void_reason, void_by, void_date) VALUES (@c1, @tea, N'Tea', 5000, 1, 1, N'Wrong order', {rec}, '2026-11-12');
            INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, status)
              VALUES (@c1, @p, 'S-1', 'outlet', 'UPI', 65000, 500, 3250, 68250, '2026-11-12', '2026-11-12 07:00', {rec}, 'Settled');
            -- C2: bar, 18 % bill (other rate), tea at a changed snapshot price, a legacy-category item
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status) VALUES (@p, @bar, 'B1', 1, '2026-11-12', '2026-11-12 09:00', {own}, 'Settled');
            DECLARE @c2 BIGINT = SCOPE_IDENTITY();
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES (@c2, @tea, N'Tea', 5500, 4), (@c2, @snk, N'Chakli', 8000, 1);
            INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, status)
              VALUES (@c2, @p, 'S-2', 'outlet', 'Card', 30000, 1800, 5400, 35400, '2026-11-12', '2026-11-12 10:00', {own}, 'Settled');
            -- C3: a voided bill (check and bill both Void) with big lines: excluded
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status, void_reason, voided_by, voided_utc)
              VALUES (@p, @rest, 'T2', 2, '2026-11-12', '2026-11-12 11:00', {rec}, 'Void', N'Guest left', {own}, '2026-11-12 12:00');
            DECLARE @c3 BIGINT = SCOPE_IDENTITY();
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES (@c3, @pnr, N'Paneer tikka', 25000, 5);
            INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, status, void_reason, voided_by, voided_utc)
              VALUES (@c3, @p, 'S-3', 'outlet', 'UPI', 125000, 500, 6250, 131250, '2026-11-12', '2026-11-12 11:30', {rec}, 'Void', N'Guest left', {own}, '2026-11-12 12:00');
            -- C4: still open (no bill): excluded.  C5: settled on a later date: outside the range.
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status) VALUES (@p, @rest, 'T3', 2, '2026-11-12', '2026-11-12 13:00', {rec}, 'Open');
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES (SCOPE_IDENTITY(), @pnr, N'Paneer tikka', 25000, 9);
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status) VALUES (@p, @rest, 'T4', 2, '2026-11-20', '2026-11-20 06:00', {rec}, 'Settled');
            DECLARE @c5 BIGINT = SCOPE_IDENTITY();
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES (@c5, @pnr, N'Paneer tikka', 25000, 1);
            INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, status)
              VALUES (@c5, @p, 'S-5', 'outlet', 'UPI', 25000, 500, 1250, 26250, '2026-11-20', '2026-11-20 07:00', {rec}, 'Settled');
            SELECT 1;
            """);
        var src = new SqlP2CReportSource(db.Platform.Connections);

        var facts = (await src.SalesAsync(p, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12), Ct)).OrderBy(f => f.Item, StringComparer.Ordinal).ThenBy(f => f.Outlet, StringComparer.Ordinal).ToList();
        Assert.Equal(
        [
            new SaleLineFact(snack.ToString(), "Chakli", "Snacks", 1, 8000, "Bar", own),
            new SaleLineFact(paneer.ToString(), "Paneer tikka", "Mains", 2, 50000, "Restaurant", rec),
            new SaleLineFact(tea.ToString(), "Tea", "Drinks", 4, 22000, "Bar", own),             // 4 x 5500 (snapshot price), 18 % bill: value is still pre-tax
            new SaleLineFact(tea.ToString(), "Tea", "Drinks", 3, 15000, "Restaurant", rec),      // the voided 4th tea is not counted
        ], facts);

        var def = new SalesByItemReport(src, null!).Definition;
        var r = SalesByItemReport.Build(def, Bound(new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12)), facts);
        var total = r.Rows[^1];
        Assert.Equal((ReportRowKind.Total, 10L, 95000L), (total.Kind, total.Cells["qty"], total.Cells["value"]));   // 2+7+1 units, 50000+37000+8000 paise
        var teaRow = r.Rows.Single(x => x.Kind == ReportRowKind.Row && (string)x.Cells["item"]! == "Tea");
        Assert.Equal((7L, 37000L, 38.9), (teaRow.Cells["qty"], teaRow.Cells["value"], teaRow.Cells["share"]));     // 37000 / 95000 = 38.9 %
        Assert.Equal(["Mains total", "Drinks total", "Snacks total"], r.Rows.Where(x => x.Kind == ReportRowKind.Subtotal).Select(x => (string)x.Cells["item"]!).ToArray());
        var barOnly = SalesByItemReport.Build(def, new BoundParams(new Dictionary<string, string> { ["outlet"] = "Bar" }, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12)), facts);
        Assert.Equal(30000L, barOnly.Rows[^1].Cells["value"]);

        // Edge: empty period (nothing settled on the 13th; the voids test bills the 14th, so ranges stay one day wide) -> no rows, no total; the later-date bill appears only when the range reaches it.
        var empty = await src.SalesAsync(p, new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 13), Ct);
        Assert.Empty(empty);
        Assert.Empty(SalesByItemReport.Build(def, Bound(new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 13)), empty).Rows);
        Assert.Equal(25000L, Assert.Single(await src.SalesAsync(p, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), Ct)).ValuePaise);
    }

    // ── POS voids ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pos_voids_reads_voided_lines_with_approver_whole_check_stamp_reason_fallback_and_check_key()
    {
        var p = db.PropertyId;
        var rec = db.Users["REC"];
        var own = db.Users["OWN"];
        var mgr = db.Users["MGR"];
        var (rest, _, paneer, tea, _) = await PosSeedAsync();
        await db.ScalarAsync($"""
            DECLARE @p INT = {p}, @rest INT = {rest}, @pnr INT = {paneer}, @tea INT = {tea};
            -- VC1: a settled check (bill V-10) with an approved tea void (2 x 50.00), an unapproved paneer void, and a live line
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status) VALUES (@p, @rest, 'T5', 2, '2026-11-14', '2026-11-14 06:00', {rec}, 'Settled');
            DECLARE @c1 BIGINT = SCOPE_IDENTITY();
            INSERT pos.void_request(property_id, order_id, scope, line_id, amount_paise, reason, status, order_rev, requested_by, requested_utc, expires_utc, decided_by, decided_utc)
              VALUES (@p, @c1, 'item', NULL, 10000, N'Guest changed mind', 'Executed', 1, {rec}, '2026-11-14 06:30', '2026-11-15 06:30', {own}, '2026-11-14 06:35');
            DECLARE @vr BIGINT = SCOPE_IDENTITY();
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty, voided, void_reason, void_by, void_date, void_request_id) VALUES (@c1, @tea, N'Tea', 5000, 2, 1, N'Guest changed mind', {rec}, '2026-11-14', @vr);
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty, voided, void_reason, void_by, void_date) VALUES (@c1, @pnr, N'Paneer tikka', 25000, 1, 1, N'Spoilt', {mgr}, '2026-11-14');
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES (@c1, @pnr, N'Paneer tikka', 25000, 1);
            INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, status)
              VALUES (@c1, @p, 'V-10', 'outlet', 'UPI', 25000, 500, 1250, 26250, '2026-11-14', '2026-11-14 08:00', {rec}, 'Settled');
            -- VC2: an open check (no bill yet) -> key falls back to POS-<order id>; the void is dated the 15th (stamp = the date only)
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status) VALUES (@p, @rest, 'T6', 2, '2026-11-15', '2026-11-15 06:00', {rec}, 'Open');
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty, voided, void_reason, void_by, void_date) VALUES (SCOPE_IDENTITY(), @tea, N'Tea', 5000, 1, 1, N'Kitchen out', {rec}, '2026-11-15');
            -- VC3: a whole check voided on the 16th at 08:30 UTC = 14:00 IST; the line carries an empty reason so the check's reason is used
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status, void_reason, voided_by, voided_utc)
              VALUES (@p, @rest, 'T7', 2, '2026-11-16', '2026-11-16 06:00', {rec}, 'Void', N'Walk-out', {mgr}, '2026-11-16 08:30');
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty, voided, void_reason, void_by, void_date) VALUES (SCOPE_IDENTITY(), @pnr, N'Paneer tikka', 12000, 1, 1, N'', {mgr}, '2026-11-16');
            -- Outside the range: voided on the 20th
            INSERT pos.check_order(property_id, outlet_id, table_no, covers, business_date, opened_utc, opened_by, status) VALUES (@p, @rest, 'T8', 2, '2026-11-20', '2026-11-20 06:00', {rec}, 'Open');
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty, voided, void_reason, void_by, void_date) VALUES (SCOPE_IDENTITY(), @tea, N'Tea', 5000, 5, 1, N'Late', {rec}, '2026-11-20');
            SELECT 1;
            """);
        var src = new SqlP2CReportSource(db.Platform.Connections);

        var facts = (await src.VoidsAsync(p, new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 16), Ct)).OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        Assert.Equal(4, facts.Count);
        long OrderOf(VoidFact f) => long.Parse(f.Key[..12], System.Globalization.CultureInfo.InvariantCulture);
        Assert.All(facts, f => Assert.Matches(@"^\d{12}/\d{12}$", f.Key));
        var tea2 = facts.Single(f => f.Qty == 2);
        Assert.Equal(("2026-11-14", "V-10", "Tea", 2L, 10000L, "Guest changed mind", rec, (int?)own), (tea2.Time, tea2.Check, tea2.Item, tea2.Qty, tea2.ValuePaise, tea2.Reason, tea2.VoidBy, tea2.ApprovedBy));
        var spoilt = facts.Single(f => f.Reason == "Spoilt");
        Assert.Equal(("2026-11-14", "V-10", "Paneer tikka", 1L, 25000L, mgr, (int?)null), (spoilt.Time, spoilt.Check, spoilt.Item, spoilt.Qty, spoilt.ValuePaise, spoilt.VoidBy, spoilt.ApprovedBy));
        Assert.Equal(OrderOf(tea2), OrderOf(spoilt));
        var open = facts.Single(f => f.Reason == "Kitchen out");
        Assert.Equal(("2026-11-15", $"POS-{OrderOf(open)}", 5000L), (open.Time, open.Check, open.ValuePaise));
        var whole = facts.Single(f => f.Reason == "Walk-out");
        Assert.Equal(("2026-11-16 14:00", $"POS-{OrderOf(whole)}", 12000L, mgr), (whole.Time, whole.Check, whole.ValuePaise, whole.VoidBy));
        Assert.DoesNotContain(facts, f => f.Reason == "Late");
        Assert.DoesNotContain(facts, f => f.Item == "Paneer tikka" && f.Reason == "" && f.Time != "2026-11-16 14:00");   // the live paneer line never appears

        var names = await new SqlP2CReportSource(db.Platform.Connections).NamesAsync(p, Ct);
        var def = new PosVoidsReport(src).Definition;
        var r = PosVoidsReport.Build(def, Bound(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 16)), facts, names);
        var total = r.Rows[^1];
        Assert.Equal((ReportRowKind.Total, 5L, 52000L), (total.Kind, total.Cells["qty"], total.Cells["value"]));     // 2+1+1+1 units; 10000+25000+5000+12000 paise
        Assert.Equal("OWN", r.Rows.Single(x => Equals(x.Cells["reason"], "Guest changed mind")).Cells["approvedBy"]);
        Assert.Null(r.Rows.Single(x => Equals(x.Cells["reason"], "Spoilt")).Cells["approvedBy"]);
        Assert.Equal("2026-11-16 14:00", r.Rows.Single(x => Equals(x.Cells["reason"], "Walk-out")).Cells["time"]);

        // Edge: the 17th–19th has no voids.
        var none = await src.VoidsAsync(p, new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 19), Ct);
        Assert.Empty(none);
        Assert.Empty(PosVoidsReport.Build(def, Bound(new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 19)), none, names).Rows);
    }

    // ── leave balances ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Leave_balances_read_policy_override_defaults_status_year_and_type_filters_with_half_days_in_the_builder()
    {
        var p = db.PropertyId;
        var rec = db.Users["REC"];
        var own = db.Users["OWN"];
        var bare = await OtherPropertyAsync("ZZ3");       // no leave_policy row: SCC defaults 12 / 7 / 15
        await db.ScalarAsync($"""
            DECLARE @p INT = {p}, @b INT = {bare};
            INSERT hr.department(property_id, dept_code, name) VALUES (@p, 'FRD', N'Front desk'), (@b, 'FRD', N'Front desk');
            INSERT hr.leave_policy(property_id, cl_days, sl_days, el_days, default_cap, updated_by, updated_utc) VALUES (@p, 10, 6, 14, 3, {own}, '2026-04-01');
            INSERT hr.staff(property_id, name, dept_code, designation, pay_type, monthly_paise, daily_paise, default_shift, weekly_off, joined, exited_on, active, phone, created_by, created_utc) VALUES
              (@p, N'Asha', 'FRD', N'Receptionist', 'monthly', 2000000, 0, 'G', 0, '2025-01-01', NULL, 1, '9000000001', {own}, '2025-01-01'),
              (@p, N'Bimal', 'FRD', N'Receptionist', 'monthly', 2000000, 0, 'G', 0, '2025-01-01', NULL, 1, '9000000002', {own}, '2025-01-01'),
              (@p, N'Chitra', 'FRD', N'Receptionist', 'monthly', 2000000, 0, 'G', 0, '2024-01-01', '2026-06-30', 0, '9000000003', {own}, '2024-01-01'),
              (@b, N'Dev', 'FRD', N'Receptionist', 'monthly', 2000000, 0, 'G', 0, '2025-01-01', NULL, 1, '9000000004', {own}, '2025-01-01');
            DECLARE @asha BIGINT = (SELECT staff_id FROM hr.staff WHERE property_id = @p AND name = N'Asha'), @bimal BIGINT = (SELECT staff_id FROM hr.staff WHERE property_id = @p AND name = N'Bimal');
            INSERT hr.leave_ent_override(property_id, staff_id, cl_days, sl_days, el_days, updated_by, updated_utc) VALUES (@p, @bimal, 10.5, NULL, NULL, {own}, '2026-04-02');
            -- Asha: two approved CL (full + AM half), pending CL in December, approved SL, rejected / cancelled CL (ignored), LOP (not tracked),
            -- approved EL in the previous year and CL the day after the year end (both outside), approved CL on the last day of the year
            INSERT hr.leave_request(property_id, staff_id, leave_type, from_date, to_date, half, days_x2, reason, status, requested_by, requested_utc, decided_by, decided_utc, request_from, request_to) VALUES
              (@p, @asha, 'CL', '2026-05-04', '2026-05-04', NULL, 2, N'Family', 'Approved', {rec}, '2026-04-20', {own}, '2026-04-21', '2026-05-04', '2026-05-04'),
              (@p, @asha, 'CL', '2026-06-10', '2026-06-10', 'AM', 1, N'Dentist', 'Approved', {rec}, '2026-06-01', {own}, '2026-06-02', '2026-06-10', '2026-06-10'),
              (@p, @asha, 'CL', '2026-12-01', '2026-12-01', NULL, 2, N'Trip', 'Pending', {rec}, '2026-09-01', NULL, NULL, '2026-12-01', '2026-12-01'),
              (@p, @asha, 'SL', '2026-08-03', '2026-08-03', NULL, 2, N'Fever', 'Approved', {rec}, '2026-08-03', {own}, '2026-08-03', '2026-08-03', '2026-08-03'),
              (@p, @asha, 'CL', '2026-07-01', '2026-07-01', NULL, 2, N'No', 'Rejected', {rec}, '2026-06-20', {own}, '2026-06-21', '2026-07-01', '2026-07-01'),
              (@p, @asha, 'CL', '2026-07-02', '2026-07-02', NULL, 2, N'Changed', 'Cancelled', {rec}, '2026-06-20', NULL, NULL, '2026-07-02', '2026-07-02'),
              (@p, @asha, 'LOP', '2026-09-01', '2026-09-01', NULL, 2, N'Unpaid', 'Approved', {rec}, '2026-08-20', {own}, '2026-08-21', '2026-09-01', '2026-09-01'),
              (@p, @asha, 'EL', '2026-03-10', '2026-03-10', NULL, 2, N'Old year', 'Approved', {rec}, '2026-03-01', {own}, '2026-03-02', '2026-03-10', '2026-03-10'),
              (@p, @asha, 'CL', '2027-04-01', '2027-04-01', NULL, 2, N'Next year', 'Pending', {rec}, '2027-03-20', NULL, NULL, '2027-04-01', '2027-04-01'),
              (@p, @asha, 'CL', '2027-03-31', '2027-03-31', NULL, 2, N'Year end', 'Approved', {rec}, '2027-03-01', {own}, '2027-03-02', '2027-03-31', '2027-03-31'),
              (@p, @bimal, 'EL', '2026-10-05', '2026-10-05', NULL, 2, N'Pending EL', 'Pending', {rec}, '2026-09-25', NULL, NULL, '2026-10-05', '2026-10-05'),
              (@p, @bimal, 'EL', '2026-10-06', '2026-10-06', NULL, 2, N'Approved EL', 'Approved', {rec}, '2026-09-25', {own}, '2026-09-26', '2026-10-06', '2026-10-06');
            SELECT 1;
            """);
        var src = new SqlP2CReportSource(db.Platform.Connections);
        var (from, to) = LeaveBalancesReport.Fy(new DateOnly(2026, 10, 2));
        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)), (from, to));

        var (staff, days) = await src.LeaveAsync(p, from, to, Ct);
        Assert.Equal(["Asha", "Bimal", "Chitra"], staff.Select(s => s.Name).ToArray());                       // alphabetical, this property only
        Assert.Equal((10m, 6m, 14m), (staff[0].Cl, staff[0].Sl, staff[0].El));                                 // policy
        Assert.Equal((10.5m, 6m, 14m), (staff[1].Cl, staff[1].Sl, staff[1].El));                               // per-employee CL override, the rest from the policy
        Assert.Equal((false, new DateOnly(2026, 6, 30)), (staff[2].Active, staff[2].Exited!.Value));
        var ashaId = staff[0].StaffId; var bimalId = staff[1].StaffId;
        Assert.Equal(
        [
            (ashaId, "CL", new DateOnly(2026, 5, 4), 1m, "Approved"), (ashaId, "CL", new DateOnly(2026, 6, 10), 0.5m, "Approved"), (ashaId, "SL", new DateOnly(2026, 8, 3), 1m, "Approved"),
            (ashaId, "CL", new DateOnly(2026, 12, 1), 1m, "Pending"), (ashaId, "CL", new DateOnly(2027, 3, 31), 1m, "Approved"),
            (bimalId, "EL", new DateOnly(2026, 10, 5), 1m, "Pending"), (bimalId, "EL", new DateOnly(2026, 10, 6), 1m, "Approved"),
        ], days.Select(d => (d.StaffId, d.Type, d.Date, d.Days, d.Status)).OrderBy(d => d.StaffId).ThenBy(d => d.Date).ToArray());

        // The report builder on those facts, as on 2 Oct 2026 (half day counts 0.5; the 31 Mar approved leave is not yet used; a pending leave counts whatever its date).
        var def = new LeaveBalancesReport(src).Definition;
        var r = LeaveBalancesReport.Build(def, Bound(null, new DateOnly(2026, 10, 2)), staff, days);
        object[] Cells(string who, string type) { var x = r.Rows.Single(y => (string)y.Cells["employee"]! == who && (string)y.Cells["type"]! == type).Cells; return [x["entitlement"]!, x["used"]!, x["pending"]!, x["balance"]!]; }
        Assert.Equal<object[]>([10L, 1.5, 1L, 8.5], Cells("Asha", "Casual Leave"));
        Assert.Equal<object[]>([6L, 1L, 0L, 5L], Cells("Asha", "Sick Leave"));
        Assert.Equal<object[]>([14L, 0L, 0L, 14L], Cells("Asha", "Earned Leave"));
        Assert.Equal<object[]>([10.5, 0L, 0L, 10.5], Cells("Bimal", "Casual Leave"));
        Assert.Equal<object[]>([14L, 0L, 1L, 14L], Cells("Bimal", "Earned Leave"));            // the 6 Oct approved leave is after the as-on date
        Assert.DoesNotContain(r.Rows, x => (string)x.Cells["employee"]! == "Chitra");           // left before the as-on date
        var early = LeaveBalancesReport.Build(def, Bound(null, new DateOnly(2026, 6, 1)), staff, days);
        Assert.Contains(early.Rows, x => (string)x.Cells["employee"]! == "Chitra");             // still on the roll on 1 Jun
        Assert.Equal(1L, early.Rows.Single(x => (string)x.Cells["employee"]! == "Asha" && (string)x.Cells["type"]! == "Casual Leave").Cells["used"]);   // 10 Jun half day not yet

        // Edge: no policy row -> SCC defaults 12 / 7 / 15; a year with no leave at all -> empty days, balance = entitlement.
        var (bareStaff, bareDays) = await src.LeaveAsync(bare, from, to, Ct);
        var dev = Assert.Single(bareStaff);
        Assert.Equal(("Dev", 12m, 7m, 15m), (dev.Name, dev.Cl, dev.Sl, dev.El));
        Assert.Empty(bareDays);
        var (_, otherYear) = await src.LeaveAsync(p, new DateOnly(2028, 4, 1), new DateOnly(2029, 3, 31), Ct);
        Assert.Empty(otherYear);
    }

    // ── counter bills ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Counter_bills_read_heads_lines_tender_text_discount_and_multi_rate_tax_with_exact_paise()
    {
        var p = db.PropertyId;
        var own = db.Users["OWN"];
        var rec = db.Users["REC"];
        // The bill-A figures come from the real counter-bill rules (discount at the dominant line's rate), so the seeded paise are what the service would store.
        var t = CounterBillRules.Totals(
        [
            new CounterLine(1, "Paneer tikka", null, null, 2, new Paise(25000), GstCategory.Food, 500),
            new CounterLine(2, "Laundry", null, null, 1, new Paise(30000), GstCategory.Laundry, 1800),
        ], new Paise(5000));
        Assert.Equal((75000L, 7650L, 3825L, 3825L, 82650L), (t.Net.Value, t.Tax.Value, t.Cgst.Value, t.Sgst.Value, t.Total.Value));
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, reason, status, requested_by, requested_utc, expires_utc, decided_by, decided_utc)
              VALUES (@p, 'counter-credit', NULL, 'counter', 0, 23600, N'Wrong customer', 'Executed', {rec}, '2026-11-22 10:00', '2026-11-23 10:00', {own}, '2026-11-22 10:05');
            DECLARE @ap BIGINT = SCOPE_IDENTITY();
            -- A: paid by UPI, two rates (5 % food, 18 % laundry), discount 50.00
            INSERT ctr.bill(property_id, invoice_no, business_date, issued_utc, issued_by, party_name, discount_paise, discount_reason, net_paise, tax_paise, cgst_paise, sgst_paise, total_paise, settlement_mode, method, status)
              VALUES (@p, 'HPE/2627/0101', '2026-11-21', '2026-11-21 05:00', {rec}, N'Walk-in Patel (fictional)', 5000, N'Regular guest', 75000, 7650, 3825, 3825, 82650, 'paid', 'UPI', 'Issued');
            DECLARE @a BIGINT = SCOPE_IDENTITY();
            INSERT ctr.bill_line(bill_id, seq, label, qty, unit_paise, amount_paise, gst_category, tax_bps, sac) VALUES
              (@a, 1, N'Paneer tikka', 2, 25000, 50000, 'food', 500, '996331'), (@a, 2, N'Laundry', 1, 30000, 30000, 'laundry', 1800, '999712');
            -- B: on Udhaar credit, 5 %
            INSERT ctr.bill(property_id, invoice_no, business_date, issued_utc, issued_by, party_name, party_phone, discount_paise, net_paise, tax_paise, cgst_paise, sgst_paise, total_paise, settlement_mode, status)
              VALUES (@p, 'HPE/2627/0102', '2026-11-21', '2026-11-21 07:00', {rec}, N'Sharma (fictional)', '9000000001', 0, 15000, 750, 375, 375, 15750, 'udhaar', 'Issued');
            DECLARE @b BIGINT = SCOPE_IDENTITY();
            INSERT ctr.bill_line(bill_id, seq, label, qty, unit_paise, amount_paise, gst_category, tax_bps, sac) VALUES (@b, 1, N'Tea', 3, 5000, 15000, 'food', 500, '996331');
            -- C: paid in cash then credited by a credit note, 18 %
            INSERT ctr.bill(property_id, invoice_no, business_date, issued_utc, issued_by, party_name, discount_paise, net_paise, tax_paise, cgst_paise, sgst_paise, total_paise, settlement_mode, method, status, credit_note_no, credit_date, void_approval_id)
              VALUES (@p, 'HPE/2627/0103', '2026-11-22', '2026-11-22 05:00', {rec}, N'Gupta (fictional)', 0, 20000, 3600, 1800, 1800, 23600, 'paid', 'Cash', 'Credited', 'HPE/CN/0001', '2026-11-22', @ap);
            DECLARE @c BIGINT = SCOPE_IDENTITY();
            INSERT ctr.bill_line(bill_id, seq, label, qty, unit_paise, amount_paise, gst_category, tax_bps, sac) VALUES (@c, 1, N'Laundry', 2, 10000, 20000, 'laundry', 1800, '999712');
            -- D: outside the range
            INSERT ctr.bill(property_id, invoice_no, business_date, issued_utc, issued_by, party_name, discount_paise, net_paise, tax_paise, cgst_paise, sgst_paise, total_paise, settlement_mode, method, status)
              VALUES (@p, 'HPE/2627/0104', '2026-11-30', '2026-11-30 05:00', {rec}, N'Later (fictional)', 0, 1000, 50, 25, 25, 1050, 'paid', 'Cash', 'Issued');
            INSERT ctr.bill_line(bill_id, seq, label, qty, unit_paise, amount_paise, gst_category, tax_bps, sac) VALUES (SCOPE_IDENTITY(), 1, N'Mint', 1, 1000, 1000, 'food', 500, '996331');
            SELECT 1;
            """);
        var src = new SqlP2BReportSource(db.Platform.Connections);

        var bills = await src.CounterBillsAsync(p, new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 22), Ct);
        Assert.Equal(["HPE/2627/0101", "HPE/2627/0102", "HPE/2627/0103"], bills.Select(b => b.No).ToArray());
        var a = bills[0];
        Assert.Equal((new DateOnly(2026, 11, 21), "Walk-in Patel (fictional)", "UPI", 75000L, 7650L, 5000L, "Regular guest"), (a.Date, a.Customer, a.Tender, a.NetPaise, a.TaxPaise, a.DiscountPaise, a.DiscountReason));
        Assert.Equal([new CounterLineFact("Paneer tikka", 2, 25000, 50000), new CounterLineFact("Laundry", 1, 30000, 30000)], a.Lines);
        Assert.Equal(("Udhaar (credit)", 15000L, 750L, 0L), (bills[1].Tender, bills[1].NetPaise, bills[1].TaxPaise, bills[1].DiscountPaise));
        Assert.Null(bills[1].DiscountReason);
        Assert.Equal(("Cash · credited HPE/CN/0001", 20000L, 3600L), (bills[2].Tender, bills[2].NetPaise, bills[2].TaxPaise));
        Assert.Equal([new CounterLineFact("Laundry", 2, 10000, 20000)], bills[2].Lines);

        // The report's rows: a discount is its own negative row; grand total = Σ net (110000) and Σ tax (12000).
        var rows = CounterBillsReport.Rows(bills);
        Assert.Contains(rows, x => Equals(x.Cells["item"], "Discount · Regular guest") && Equals(x.Cells["amount"], -5000L));
        var grand = rows[^1];
        Assert.Equal((ReportRowKind.Total, 110000L, 12000L), (grand.Kind, grand.Cells["amount"], grand.Cells["tax"]));
        Assert.Equal(82650L - 7650L, 75000L);                                               // net = Σ lines - discount (50000 + 30000 - 5000)
        Assert.Equal(75000L, a.Lines.Sum(l => l.AmountPaise) - a.DiscountPaise);

        // Edge: the 30th shows only bill D; the 23rd–29th is empty with no total row.
        Assert.Equal("HPE/2627/0104", Assert.Single(await src.CounterBillsAsync(p, new DateOnly(2026, 11, 30), new DateOnly(2026, 11, 30), Ct)).No);
        var none = await src.CounterBillsAsync(p, new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 29), Ct);
        Assert.Empty(none);
        Assert.Empty(CounterBillsReport.Rows(none));
    }

    // ── cash-count grids ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cash_count_grids_read_the_latest_count_of_each_shift_of_the_date_with_notes_coins_and_no_breakdown()
    {
        var p = db.PropertyId;
        var other = await OtherPropertyAsync("ZZ2");
        var rec = db.Users["REC"];
        const string first = """{"notes":{"500":4,"100":10},"coinsPaise":2500}""";
        const string tcJson = """{"notes":{"500":2},"coinsPaise":0}""", emptyJson = "{}", otherJson = """{"notes":{"500":1}}""";
        const string latest = """{"notes":{"2000":1,"500":3,"50":7},"coinsPaise":1050}""";
        await db.ScalarAsync($"""
            DECLARE @p INT = {p}, @o INT = {other};
            INSERT pay.till(property_id, code, name, created_by, created_utc) VALUES (@p, 'TA', N'Till A', {rec}, '2026-01-01'), (@p, 'TB', N'Till B', {rec}, '2026-01-01'), (@p, 'TC', N'Till C', {rec}, '2026-01-01'),
              (@p, 'TD', N'Till D', {rec}, '2026-01-01'), (@p, 'TE', N'Till E', {rec}, '2026-01-01'), (@o, 'TA', N'Till A', {rec}, '2026-01-01');
            DECLARE @ta INT = (SELECT till_id FROM pay.till WHERE property_id = @p AND code = 'TA'), @tb INT = (SELECT till_id FROM pay.till WHERE property_id = @p AND code = 'TB'),
                    @tc INT = (SELECT till_id FROM pay.till WHERE property_id = @p AND code = 'TC'), @td INT = (SELECT till_id FROM pay.till WHERE property_id = @p AND code = 'TD'),
                    @te INT = (SELECT till_id FROM pay.till WHERE property_id = @p AND code = 'TE'), @ot INT = (SELECT till_id FROM pay.till WHERE property_id = @o AND code = 'TA');
            INSERT pay.shift(till_id, business_date, status, opening_float_paise, opened_by, opened_utc, expected_paise, counted_paise, closed_utc) VALUES
              (@ta, '2026-11-18', 'Closed', 500000, {rec}, '2026-11-18 03:00', 386050, 386050, '2026-11-18 15:00'),
              (@tb, '2026-11-18', 'Closed', 100000, {rec}, '2026-11-18 03:00', 120000, 120000, '2026-11-18 15:00'),
              (@tc, '2026-11-19', 'Closed', 0, {rec}, '2026-11-19 03:00', 1000, 1000, '2026-11-19 15:00'),
              (@td, '2026-11-18', 'Closed', 0, {rec}, '2026-11-18 03:00', 0, 0, '2026-11-18 15:00'),
              (@te, '2026-11-18', 'Closed', 0, {rec}, '2026-11-18 03:00', 70000, 70000, '2026-11-18 15:00'),
              (@ot, '2026-11-18', 'Closed', 0, {rec}, '2026-11-18 03:00', 5000, 5000, '2026-11-18 15:00');
            DECLARE @sa BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @ta), @sb BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @tb), @sc BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @tc),
                    @se BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @te), @so BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @ot);
            -- Till A: an early count, then the final one (the latest is the sheet). Till B: counted without a breakdown. Till C: another date. Till D: no count at all.
            -- Till E: a count whose grid is empty braces (a breakdown with nothing in it). Other property: same date, never shown.
            INSERT pay.shift_count(shift_id, expected_paise, counted_paise, denominations, submitted_by, submitted_utc) VALUES
              (@sa, 386050, 242500, N'{first}', {rec}, '2026-11-18 14:00'), (@sa, 386050, 386050, N'{latest}', {rec}, '2026-11-18 14:30'),
              (@sb, 120000, 120000, NULL, {rec}, '2026-11-18 14:00'), (@sc, 1000, 1000, N'{tcJson}', {rec}, '2026-11-19 14:00'),
              (@se, 70000, 70000, N'{emptyJson}', {rec}, '2026-11-18 14:00'), (@so, 5000, 5000, N'{otherJson}', {rec}, '2026-11-18 14:00');
            SELECT 1;
            """);
        var src = new SqlP2BReportSource(db.Platform.Connections);
        long ShiftOf(string till) => Convert.ToInt64(db.ScalarAsync($"SELECT s.shift_id FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id WHERE t.property_id = {p} AND t.code = '{till}';").GetAwaiter().GetResult(), System.Globalization.CultureInfo.InvariantCulture);

        var grids = (await src.CountGridsAsync(p, new DateOnly(2026, 11, 18), Ct)).OrderBy(g => g.ShiftId).ToList();
        Assert.Equal([ShiftOf("TA"), ShiftOf("TB"), ShiftOf("TE")], grids.Select(g => g.ShiftId).ToArray());   // not TC (other date), TD (no count), the other property
        var a = grids[0];
        Assert.Equal(new Dictionary<int, int> { [2000] = 1, [500] = 3, [50] = 7 }, a.Notes);                    // the LATEST count, not the first
        Assert.Equal(1050L, a.CoinsPaise);
        Assert.Equal(386050L, a.Notes!.Sum(kv => (long)kv.Key * kv.Value * 100) + a.CoinsPaise);              // 2000 + 1500 + 350 rupees = 385000 paise, + 1050 coins = the shift's counted_paise
        Assert.Null(grids[1].Notes);                                                                            // no breakdown kept -> null grid
        Assert.Null(grids[1].CoinsPaise);
        Assert.Empty(grids[2].Notes!);                                                                          // {}: a grid with no notes and no coins
        Assert.Null(grids[2].CoinsPaise);

        // Edge: another date and an empty date.
        var c = Assert.Single(await src.CountGridsAsync(p, new DateOnly(2026, 11, 19), Ct));
        Assert.Equal((ShiftOf("TC"), 2, 0L), (c.ShiftId, c.Notes![500], c.CoinsPaise!.Value));
        Assert.Empty(await src.CountGridsAsync(p, new DateOnly(2026, 11, 25), Ct));
        Assert.DoesNotContain(await src.CountGridsAsync(other, new DateOnly(2026, 11, 18), Ct), g => g.Notes is null);
    }
}
