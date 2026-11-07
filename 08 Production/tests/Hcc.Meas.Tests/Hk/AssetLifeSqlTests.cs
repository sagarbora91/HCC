// F12-MEAS2-E · SQL round trip of rpt.acc.asset-life (AssetLifeReport) on a fresh .\SQLEXPRESS HccTest_* database with every migration applied
// (Hcc.Inventory.Tests harness). One fictional property: a room night re-posted exactly as StayRateCommands.ApplyAsync writes it (N2), register
// assets on an overridden and a built-in fixed-asset ledger (N5), and the linen cost-per-night integer division (L2).
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Inventory.Tests.Harness;
using Hcc.Reporting.Reports.MeasHk;

namespace Hcc.Meas.Tests.Hk;

public sealed class AssetLifeSqlFixture : IAsyncLifetime
{
    private InventoryDatabase? _db;

    public InventoryDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public int P { get; private set; }

    private async Task<long> IdAsync(string sql) => Convert.ToInt64(await Db.ScalarAsync(sql), CultureInfo.InvariantCulture);

    public async ValueTask InitializeAsync()
    {
        _db = await InventoryDatabase.CreateAsync();
        P = (int)await IdAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('ALS', N'Asset life test (fictional)', 'Asia/Kolkata', '2026-11-05');");
        var dlx = await IdAsync($"INSERT inv.room_type(property_id, code, max_occupancy, name) OUTPUT INSERTED.room_type_id VALUES ({P}, 'LDLX', 2, N'Deluxe');");
        var r101 = await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id VALUES ({P}, '101', {dlx});");

        // room revenue of FY 2026-27: night 1 posted at 3,000, re-posted at 2,000 (reversal kind 'correction' with reverses_charge_id, original marked,
        // then the new line, as StayRateCommands.ApplyAsync); night 2 at 1,000. Live room revenue = 3,000 (old query: 6,000).
        var res = await IdAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, nightly_rate_paise)
            OUTPUT INSERTED.reservation_id VALUES ({P}, 'AL-1', N'Guest (fictional)', '2026-11-01', '2026-11-03', 'Departed', {r101}, {dlx}, 2, 0, 200000);
            """);
        var f = await IdAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({res});");
        var orig = await IdAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) OUTPUT INSERTED.charge_id VALUES ({f}, 'room', '2026-11-01', N'Room night', 300000, 500, 15000);");
        var rev = await IdAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id) OUTPUT INSERTED.charge_id VALUES ({f}, 'correction', '2026-11-01', N'Reversal · rate change · Room night', -300000, 500, -15000, {orig});");
        await Db.ExecAsync($"UPDATE bill.charge SET reversed_by_charge_id = {rev} WHERE charge_id = {orig};");
        await Db.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({f}, 'room', '2026-11-01', N'Room night', 200000, 500, 10000);");
        await Db.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({f}, 'room', '2026-11-02', N'Room night', 100000, 500, 5000);");

        // a maintenance asset with its captured life, and two register assets: 1510 under the property's own ledger name, 1520 under the built-in name
        var ac = await IdAsync($"INSERT mnt.asset(property_id, name, kind, room_id, installed_on, status) OUTPUT INSERTED.asset_id VALUES ({P}, N'AC 101', 'AC', {r101}, '2020-01-01', 'Working');");
        await Db.ExecAsync($"INSERT meas.asset_life(property_id, asset_id, expected_life_months, condition_grade, replacement_cost_paise) VALUES ({P}, '{ac}', 120, 'B', 4500000);");
        await Db.ExecAsync($"INSERT acc.ledger(property_id, code, name, group_id, is_system) VALUES ({P}, '1510', N'Furniture (Owner name)', 'fixed-assets', 1);");
        async Task Asset(string no, string ledger, string name, long cost, string date)
        {
            var e = await IdAsync($"""
                INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc)
                OUTPUT INSERTED.expense_id VALUES ({P}, N'Vendor (fictional)', N'Assets', {cost}, 'Cash', '{date}', 'Draft', 1, SYSUTCDATETIME());
                """);
            await Db.ExecAsync($"""
                INSERT acc.fixed_asset(property_id, asset_no, ledger, name, cost_paise, purchase_date, vendor, expense_id, created_by, created_utc)
                VALUES ({P}, '{no}', '{ledger}', N'{name}', {cost}, '{date}', N'Vendor (fictional)', {e}, 1, SYSUTCDATETIME());
                """);
        }

        await Asset("FA-2627-001", "1510", "Sofa set", 12000, "2026-06-10");   // FY capex = 12,000 (this one only)
        await Asset("FA-2526-001", "1520", "Water pump", 5000, "2025-12-01");  // bought in the previous FY
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}

public sealed class AssetLifeSqlTests(AssetLifeSqlFixture f) : IClassFixture<AssetLifeSqlFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ReportResult> RunAsync(string kind = "") =>
        new AssetLifeReport(f.Db.Platform).SelectAsync(new Caller(f.P, 1), new BoundParams(new Dictionary<string, string> { ["kind"] = kind }, To: new DateOnly(2026, 11, 5)), Ct);

    [Fact]
    public async Task MEAS2_E_N2_reserve_base_counts_a_reposted_night_once_at_the_new_rate()
    {
        var r = await RunAsync();
        var total = Assert.Single(r.Rows, x => x.Kind == ReportRowKind.Total);
        Assert.Equal("FY 2026-27 to date", total.Cells["asset"]);
        Assert.Equal(9000L, total.Cells["reserveAccrued"]);                             // 3 % × (2,000 + 1,000), not 3 % × 6,000 = 18,000
        Assert.Equal(12000L, total.Cells["capexSpent"]);
        Assert.Equal("red", total.Group);                                               // capex 12,000 above the reserve 9,000 (the double count hid it)
    }

    [Fact]
    public async Task MEAS2_E_N5_register_asset_kind_is_the_ledger_name_and_filters_like_the_prototype()
    {
        var r = await RunAsync();
        var assets = r.Rows.Where(x => x.Kind == ReportRowKind.Row).Select(x => (string)x.Cells["asset"]!).ToList();
        Assert.Equal(["AC 101", "FA-2627-001 Sofa set", "FA-2526-001 Water pump"], assets); // AC, Furniture (Owner name), Plant & machinery
        Assert.Equal(12000L, r.Rows.Single(x => (string?)x.Cells["asset"] == "FA-2627-001 Sofa set").Cells["wdv"]); // WDV column = register cost

        var plant = await RunAsync("Plant & machinery");                                 // the built-in name of 1520
        Assert.Equal(["FA-2526-001 Water pump"], plant.Rows.Where(x => x.Kind == ReportRowKind.Row).Select(x => (string)x.Cells["asset"]!));
        var own = await RunAsync("Furniture (Owner name)");                              // the property's override of 1510 wins over the built-in
        Assert.Equal(["FA-2627-001 Sofa set"], own.Rows.Where(x => x.Kind == ReportRowKind.Row).Select(x => (string)x.Cells["asset"]!));
        var old = await RunAsync("Fixed assets 1510");                                   // the old server-only kind no longer matches
        Assert.DoesNotContain(old.Rows, x => x.Kind == ReportRowKind.Row);
    }

    [Theory]
    [InlineData(5L, 2L, 3L)]
    [InlineData(-5L, 2L, -3L)]
    [InlineData(4L, 3L, 1L)]
    [InlineData(7L, 2L, 4L)]
    [InlineData(1099511627776L * 3 + 1, 2L, 1649267441665L)]                             // 2^40 × 3 + 1 paise ÷ 2, exact (no double)
    public void MEAS2_E_L2_linen_cost_per_night_divides_in_integers_half_up(long n, long d, long expected) =>
        Assert.Equal(expected, LinenCostReport.DivHalfUp(n, d));
}
