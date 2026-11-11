// CU-RPT-A · rpt.money.expenses-cash on the server: drawer figures per business date (cash book arithmetic) and expenses split by what they settle to (payment-method master),
// the seven range tiles to the paise, capital and unposted expenses left out, refusals, DEC-061 guard of the hub. Disposable HccTest_* database. Fictional data.
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Contracts.Closing;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Reporting.Reports.CuA;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.CuA;

[Trait("Category", "Sql")]
public sealed class ExpensesCashReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private readonly FakePolicy _policy = new();

    public async ValueTask InitializeAsync()
    {
        await CuARig.SeedCash(db);
        await CuARig.SeedExpenses(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Day_rows_total_and_tiles_to_the_paise()
    {
        var r = await CuARig.Run(db, _policy, db.Users["OWN"], ExpensesCashReport.Id, ("from", "2026-10-21"), ("to", "2026-10-24"));
        var d = CuARig.Data(r);
        Assert.Equal(["2026-10-21", "2026-10-22", "2026-10-23", "2026-10-24"], d.Select(x => CuARig.S(x, "date")));
        Assert.Equal(["—", "Closed", "Closed", "Open"], d.Select(x => CuARig.S(x, "day")));
        Assert.Null(d[0].Cells["opening"]);
        Assert.Equal([1000000L, 1395000, 1245000], d.Skip(1).Select(x => CuARig.L(x, "opening")));
        Assert.Equal([1400000L, 1245000, 1245000], d.Skip(1).Select(x => CuARig.L(x, "expected")));
        Assert.Equal([1395000L, 1245000], d.Skip(1).Take(2).Select(x => CuARig.L(x, "counted")));
        Assert.Null(d[3].Cells["counted"]);
        Assert.Equal([-5000L, 0], d.Skip(1).Take(2).Select(x => CuARig.L(x, "difference")));
        Assert.Equal([0L, 130000, -30000, 0], d.Select(x => CuARig.L(x, "cashExpenses")));
        var t = Assert.Single(r.Rows, x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("Total", 100000L), (CuARig.S(t, "date"), CuARig.L(t, "cashExpenses")));
        var tiles = Assert.Single(Hcc.Reporting.ReportRunner.Tables(r), x => x.Key == "tiles").Rows;
        Assert.Equal(
            [("Drawer-cash expenses", 100000L), ("UPI / bank expenses", 450000), ("Vendor-credit expenses (no drawer effect)", 1010000), ("Reversals included above", -30000),
             ("Posted expenses (net of reversals)", 1560000), ("Cash into drawer (payments + deposits + POS sales)", 700000), ("Cash out of drawer (refunds + returns + POS voids)", -50000)],
            tiles.Select(x => (CuARig.S(x, "tile"), CuARig.L(x, "amount"))));
    }

    private Task SeedExtra() => CuARig.SeedOnce(db, "extra", async () =>
    {
        var (p, csh, mgr) = (db.PropertyId, db.Users["CSH"], db.Users["MGR"]);
        // 20 Oct: a shift that never went through the day close (no cls.business_day row) holding folio / POS / Udhaar cash AND hall / counter-bill cash.
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT pay.till(property_id, code, name, created_by, created_utc) VALUES (@p, 'TB', N'Till B', {csh}, '2026-01-01');
            DECLARE @t INT = SCOPE_IDENTITY();
            INSERT pay.shift(till_id, business_date, status, opening_float_paise, opened_by, opened_utc, expected_paise, counted_paise, submitted_by, submitted_utc, closed_by, closed_utc)
              VALUES (@t, '2026-10-20', 'Closed', 0, {csh}, '2026-10-20 03:30', 0, 0, {csh}, '2026-10-20 16:00', {csh}, '2026-10-20 16:30');
            DECLARE @s BIGINT = SCOPE_IDENTITY();
            INSERT pay.cash_movement(shift_id, till_id, business_date, kind, source_type, source_id, amount_paise, label, reference, principal_id, recorded_utc) VALUES
              (@s, @t, '2026-10-20', 'payment', 'receipt', 101, 30000, N'Folio payment', N'RC-1', {csh}, '2026-10-20 06:00'),
              (@s, @t, '2026-10-20', 'pos-sale', 'pos-bill', 102, 40000, N'POS bill', N'B-1', {csh}, '2026-10-20 06:10'),
              (@s, @t, '2026-10-20', 'refund', 'receipt', 103, -5000, N'Folio refund', N'RC-2', {csh}, '2026-10-20 06:20'),
              (@s, @t, '2026-10-20', 'pos-void', 'pos-void', 104, -3000, N'POS void', N'B-2', {csh}, '2026-10-20 06:30'),
              (@s, @t, '2026-10-20', 'payment', 'udhaar', 105, 2000, N'Udhaar desk payment', N'UD-1', {csh}, '2026-10-20 06:40'),
              (@s, @t, '2026-10-20', 'payment', 'hall', 106, 500000, N'Hall advance (cash)', N'H-1', {csh}, '2026-10-20 07:00'),
              (@s, @t, '2026-10-20', 'pos-sale', 'counter-bill', 107, 120000, N'Counter bill (cash)', N'CB-1', {csh}, '2026-10-20 07:10'),
              (@s, @t, '2026-10-20', 'refund', 'hall-refund', 108, -20000, N'Hall refund (cash)', N'H-2', {csh}, '2026-10-20 07:20'),
              (@s, @t, '2026-10-20', 'pos-void', 'counter-bill', 109, -10000, N'Counter bill void', N'CB-2', {csh}, '2026-10-20 07:30');
            -- A second property with its own money master: a custom till method, a bank method and a credit method; expenses on 22 Oct.
            INSERT inv.property(code, name, time_zone, business_date) VALUES ('ZZ3', N'Second Hotel', 'Asia/Kolkata', '2026-10-24');
            DECLARE @o INT = SCOPE_IDENTITY();
            INSERT mst.bank_account(property_id, code, name, sort_order, kind, updated_by, updated_utc) VALUES (@o, 'BOX', N'Counter box', 0, 'till', {csh}, '2026-01-01');
            INSERT mst.bank_account(property_id, code, name, sort_order, kind, bank, account_last4, ifsc, updated_by, updated_utc) VALUES (@o, 'BNK', N'Main bank', 1, 'bank', N'Demo Bank', '1234', 'DEMO0001234', {csh}, '2026-01-01');
            DECLARE @box INT = (SELECT bank_account_id FROM mst.bank_account WHERE property_id = @o AND code = 'BOX'), @bnk INT = (SELECT bank_account_id FROM mst.bank_account WHERE property_id = @o AND code = 'BNK');
            INSERT mst.payment_method(property_id, code, name, sort_order, kind, settles_to_id, usable_expenses, updated_by, updated_utc) VALUES
              (@o, 'PETTY', N'Petty cash box', 0, 'cash', @box, 1, {csh}, '2026-01-01'), (@o, 'GPAY', N'GPay', 1, 'upi', @bnk, 1, {csh}, '2026-01-01'),
              (@o, 'VCR', N'Vendor credit', 2, 'credit', NULL, 1, {csh}, '2026-01-01');
            INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc, decided_by, decided_utc) VALUES
              (@o, N'Vendor B', N'Repairs', 7700, N'Petty cash box', '2026-10-22', 'Posted', {csh}, '2026-10-22 05:00', {mgr}, '2026-10-22 06:00'),
              (@o, N'Vendor B', N'Utilities', 1100, N'GPay', '2026-10-22', 'Posted', {csh}, '2026-10-22 05:00', {mgr}, '2026-10-22 06:00'),
              (@o, N'Vendor B', N'Linen', 2200, N'Vendor credit', '2026-10-22', 'Posted', {csh}, '2026-10-22 05:00', {mgr}, '2026-10-22 06:00');
            SELECT 1;
            """);
    });

    [Fact]
    public async Task Cash_in_and_out_tiles_count_folio_udhaar_and_pos_only_not_hall_or_counter_bill_cash()
    {
        await SeedExtra();
        var r = await CuARig.Run(db, _policy, db.Users["OWN"], ExpensesCashReport.Id, ("from", "2026-10-20"), ("to", "2026-10-20"));
        var tiles = Assert.Single(Hcc.Reporting.ReportRunner.Tables(r), x => x.Key == "tiles").Rows;
        // in: 300 + 400 + 20 (folio, POS, Udhaar) = 720; out: -50 - 30 = -80. The hall advance 5,000, counter bill 1,200, hall refund -200 and counter void -100 are drawer cash but not in the tiles.
        Assert.Equal((72000L, -8000L), (CuARig.L(tiles[5], "amount"), CuARig.L(tiles[6], "amount")));
    }

    [Fact]
    public async Task A_past_date_that_was_never_closed_shows_a_dash_and_blank_cash_figures()
    {
        await SeedExtra();
        var r = await CuARig.Run(db, _policy, db.Users["OWN"], ExpensesCashReport.Id, ("from", "2026-10-20"), ("to", "2026-10-20"));
        var d = Assert.Single(CuARig.Data(r));
        Assert.Equal("—", CuARig.S(d, "day"));
        Assert.All(new[] { "opening", "expected", "counted", "difference" }, k => Assert.Null(d.Cells[k]));
    }

    [Fact]
    public async Task A_second_property_with_a_custom_till_method_sees_only_its_own_expenses_split_by_its_own_master()
    {
        await SeedExtra();
        var other = Convert.ToInt32(await db.ScalarAsync("SELECT property_id FROM inv.property WHERE code = 'ZZ3';"), System.Globalization.CultureInfo.InvariantCulture);
        var r = await CuARig.RunFor(db, _policy, other, db.Users["OWN"], ExpensesCashReport.Id, ("from", "2026-10-21"), ("to", "2026-10-23"));
        var d = CuARig.Data(r);
        Assert.Equal([0L, 7700, 0], d.Select(x => CuARig.L(x, "cashExpenses")));   // 22 Oct only; the first property's expenses do not appear
        var tiles = Assert.Single(Hcc.Reporting.ReportRunner.Tables(r), x => x.Key == "tiles").Rows;
        Assert.Equal([7700L, 1100, 2200, 0, 11000, 0, 0], tiles.Select(x => CuARig.L(x, "amount")));
        // and the first property does not see the second one's rows
        var a = await CuARig.Run(db, _policy, db.Users["OWN"], ExpensesCashReport.Id, ("from", "2026-10-22"), ("to", "2026-10-22"));
        Assert.Equal(130000L, CuARig.L(CuARig.Data(a)[0], "cashExpenses"));
    }

    [Fact]
    public void Capital_and_unposted_expenses_stay_out_and_an_unknown_method_counts_as_bank()
    {
        static LedgerRow L(long id, string method, long amt, string status, bool capital) =>
            new(id, new DateOnly(2026, 10, 22), "C", method, "V", null, amt, null, null, status, null, "x", null, null, false, DateTime.UtcNow, null, false, capital);
        var view = new CashBookRangeView(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), [], 0, 0, 0, 0, 0, 0, 0, "");
        var (days, tiles) = ExpensesCashReport.Blocks(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), view,
            [L(1, "Cash", 1000, "Posted", false), L(2, "Cash", 5000, "Posted", true), L(3, "Cash", 700, "Rejected", false), L(4, "Mystery", 200, "Posted", false), L(5, "UPI", 300, "Posted", false)], null, (0, 0));
        Assert.Equal(1000L, CuARig.L(days[0], "cashExpenses"));
        Assert.Equal("—", CuARig.S(days[0], "day"));
        Assert.Equal([1000L, 500, 0, 0, 1500, 0, 0], tiles.Select(x => CuARig.L(x, "amount")));
    }

    [Fact]
    public async Task Cashier_reception_and_housekeeping_are_refused_and_the_hub_enforces_DEC_061()
    {
        foreach (var who in new[] { db.Users["CSH"], db.Users["REC"], 7 })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => CuARig.Run(db, _policy, who, ExpensesCashReport.Id, ("from", "2026-10-22"), ("to", "2026-10-22")))).Code);
        foreach (var perm in new[] { "PERM-RPT-001", "PERM-RPT-002", "PERM-RPT-003" }) _policy.Grants[perm].Add("HKP");
        var s = CuARig.Service(db, _policy);
        Assert.DoesNotContain(await s.CatalogueAsync(Rig.C(7), TestContext.Current.CancellationToken), e => e.Definition.Id == ExpensesCashReport.Id);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.ExportAsync(Rig.C(7), ExpensesCashReport.Id, "csv", Rig.NoParams, TestContext.Current.CancellationToken))).Code);
        Assert.Contains(await s.CatalogueAsync(Rig.C(2), TestContext.Current.CancellationToken), e => e.Definition.Id == ExpensesCashReport.Id && e.Money);
    }
}
