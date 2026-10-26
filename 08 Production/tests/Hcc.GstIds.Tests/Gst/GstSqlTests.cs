using System.Data;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Tax;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using Hcc.Domain.Tax;
using Hcc.GstIds.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.GstIds.Tests.Gst;

/// <summary>GST master on SQL Server: seed, the resolver behind billing postings, snapshots, Owner-only dated changes, golden figures.</summary>
[Trait("Category", "Sql")]
public class GstSqlTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Day = GstIdsDb.Day;

    [Fact]
    public async Task Migration_seed_equals_the_code_seed_and_is_marked_illustrative()
    {
        var rows = await db.DumpAsync("SELECT setting_key, CONVERT(char(10), effective_from, 23), value FROM tax.gst_setting_version WHERE source = 'seed' ORDER BY setting_key, effective_from;");
        var expected = string.Concat(GstSeed.AllVersions.OrderBy(v => v.Key, StringComparer.Ordinal).ThenBy(v => v.EffectiveFrom)
            .Select(v => $"{v.Key}|{v.EffectiveFrom:yyyy-MM-dd}|{v.Value}|"));
        Assert.Equal(expected, rows);
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM tax.gst_setting WHERE note <> N'Illustrative — CA to confirm';"));
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM tax.gst_setting_version WHERE source = 'seed' AND reason NOT LIKE N'%Illustrative — CA to confirm%';"));
        var master = await db.Gst.MasterAsync(null, db.PropertyId, Ct);
        foreach (var d in GstSeed.Settings) Assert.Equal(GstMaster.Seed().ValueOn(d.Key, Day), master.ValueOn(d.Key, Day));
    }

    [Fact]
    public async Task Room_105_journey_through_billing_is_4265_incl_265_GST_with_per_line_snapshots()
    {
        var (stay, folio) = await db.InHouseAsync(200_000, Day, Day.AddDays(2));
        var n1 = await db.NightAsync(stay, Day);
        var n2 = await db.NightAsync(stay, Day.AddDays(1));
        Assert.Equal((200_000L, 500, 10_000L), (n1.AmountPaise, n1.TaxBps, n1.TaxPaise));
        Assert.Equal((500, 10_000L), (n2.TaxBps, n2.TaxPaise));
        var laundry = await db.ChargeAsync(folio, "Laundry", 50_000);
        Assert.Equal((1800, 9_000L), (laundry.TaxBps, laundry.TaxPaise));
        var disc = await db.Billing.RequestAdjustmentAsync(db.As("OWN"), GstIdsDb.Key(),
            new RequestAdjustmentRequest("discount", folio, null, 50_000, "Goodwill (fictional)", await db.FolioVersionAsync(folio)), Ct);
        Assert.Equal("Posted", disc.Value.Outcome);

        var f = await db.Queries.GetFolioAsync(db.As("OWN"), folio, Ct);
        Assert.Equal((400_000L, 26_500L, 426_500L), (f.Totals.NetPaise, f.Totals.TaxPaise, f.Totals.TotalPaise));
        Assert.Equal("₹4,265", new Paise(f.Totals.TotalPaise).ToString());
        var d = f.Lines.Single(l => l.Kind == "discount");
        Assert.Equal((-50_000L, 500, -2_500L), (d.AmountPaise, d.TaxBps, d.TaxPaise)); // follows the room-night rate

        // Per-charge snapshot: category, ITC, CGST/SGST per line; the split sums to the folio tax and to bill.charge.
        var snap = await db.DumpAsync($"""
            SELECT g.category, g.tax_bps, g.itc, g.tax_paise, g.cgst_paise, g.sgst_paise FROM tax.charge_gst g JOIN bill.charge c ON c.charge_id = g.charge_id
            WHERE c.folio_id = {folio} ORDER BY c.charge_id;
            """);
        Assert.Equal("room|500|False|10000|5000|5000|room|500|False|10000|5000|5000|laundry|1800|True|9000|4500|4500|room|500|False|-2500|-1250|-1250|", snap);
        Assert.Equal(26_500, await db.LongAsync($"SELECT SUM(g.cgst_paise + g.sgst_paise) FROM tax.charge_gst g JOIN bill.charge c ON c.charge_id = g.charge_id WHERE c.folio_id = {folio};"));
        Assert.Equal(0, await db.LongAsync($"SELECT COUNT(*) FROM bill.charge c LEFT JOIN tax.charge_gst g ON g.charge_id = c.charge_id WHERE c.folio_id = {folio} AND (g.charge_id IS NULL OR g.tax_paise <> c.tax_paise OR g.tax_bps <> c.tax_bps);"));
    }

    [Fact]
    public async Task Boundary_and_per_night_multi_rate_stay_are_tested_on_each_nights_charged_price()
    {
        var (stay, folio) = await db.InHouseAsync(750_000, Day, Day.AddDays(3));
        Assert.Equal((500, 37_500L), ((await db.NightAsync(stay, Day)) is var a ? (a.TaxBps, a.TaxPaise) : default));
        db.Stays.Stays[stay] = (folio, 750_001);                                     // upgrade: later nights at the new agreed price
        var b = await db.NightAsync(stay, Day.AddDays(1));
        Assert.Equal((1800, 135_000L), (b.TaxBps, b.TaxPaise));
        db.Stays.Stays[stay] = (folio, 450_000);
        await db.SetBusinessDateAsync(Day.AddDays(2));
        try
        {
            var c = await db.NightAsync(stay, Day.AddDays(2));
            Assert.Equal((500, 22_500L), (c.TaxBps, c.TaxPaise));
        }
        finally
        {
            await db.SetBusinessDateAsync(Day);
        }

        var f = await db.Queries.GetFolioAsync(db.As("OWN"), folio, Ct);
        Assert.Equal([500, 1800, 500], f.Lines.OrderBy(l => l.ServiceDate).Select(l => l.TaxBps));
        Assert.Equal(37_500 + 135_000 + 22_500, f.Totals.TaxPaise);
        var itc = await db.DumpAsync($"SELECT g.itc FROM tax.charge_gst g JOIN bill.charge c ON c.charge_id = g.charge_id WHERE c.folio_id = {folio} ORDER BY c.service_date;");
        Assert.Equal("False|True|False|", itc);
    }

    [Fact]
    public async Task Owner_only_changes_tech_admin_reads_others_denied()
    {
        foreach (var role in new[] { "MGR", "ACC", "TAD", "REC", "CSH" })
            await Expect.Fails(HccErrorCode.FORBIDDEN, () => db.SetGstAsync(GstKeys.LaundryBps, 1200, role: role));
        var tad = await db.GstMaster.GetAsync(db.PropertyId, db.User["TAD"], Day, Ct);
        Assert.False(tad.CanChange);
        Assert.Equal(GstSeed.Illustrative, tad.Note);
        Assert.Equal(10, tad.Settings.Count);
        var own = await db.GstMaster.GetAsync(db.PropertyId, db.User["OWN"], Day, Ct);
        Assert.True(own.CanChange);
        Assert.Equal(500, own.Summary[0].Bps);
        foreach (var role in new[] { "MGR", "ACC", "REC", "CSH", "HKP" })
            await Expect.Fails(HccErrorCode.FORBIDDEN, () => db.GstMaster.GetAsync(db.PropertyId, db.User[role], Day, Ct));
        // Validation: out of range, no change, unknown/derived key, missing reason.
        await Expect.Fails(HccErrorCode.VALIDATION, () => db.SetGstAsync(GstKeys.RoomLowBps, 2900));
        await Expect.Fails(HccErrorCode.VALIDATION, () => db.SetGstAsync(GstKeys.RoomLowBps, 500));
        await Expect.Fails(HccErrorCode.VALIDATION, () => db.SetGstAsync("gst.specified.status", 1));
        await Expect.Fails(HccErrorCode.VALIDATION, () => db.GstMaster.SetAsync(db.Ctx("OWN"), new SetGstSettingRequest(GstKeys.LaundryBps, 1200, null, ""), Ct));
    }

    [Fact]
    public async Task Dated_change_applies_from_its_date_is_never_backdated_and_can_be_withdrawn_before_it_takes_effect()
    {
        var (stay, folio) = await db.InHouseAsync(300_000, Day, Day.AddDays(3));
        await Expect.Fails(HccErrorCode.VALIDATION, () => db.SetGstAsync(GstKeys.RoomLowBps, 1200, "2026-10-23"));
        var change = await db.SetGstAsync(GstKeys.RoomLowBps, 1200, "2026-10-25");
        Assert.Equal(("Scheduled", 500L, 1200L), (change.Status, change.Before, change.After));
        Assert.Equal(500, (await db.NightAsync(stay, Day)).TaxBps);
        await db.SetBusinessDateAsync(Day.AddDays(1));
        try
        {
            Assert.Equal(1200, (await db.NightAsync(stay, Day.AddDays(1))).TaxBps);
            // Now in force: cannot be withdrawn (set the old value again instead).
            await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => db.GstMaster.WithdrawAsync(db.Ctx("OWN"), new WithdrawGstSettingRequest(change.VersionId, "Too late (fictional)"), Ct));
            var back = await db.SetGstAsync(GstKeys.RoomLowBps, 500);
            Assert.Equal("InForce", back.Status);
        }
        finally
        {
            await db.SetBusinessDateAsync(Day);
        }

        // A future change can be withdrawn; the master falls back to the earlier version for that date.
        var future = await db.SetGstAsync(GstKeys.BanquetBps, 1200, "2026-11-01");
        Assert.Equal(1200, (await db.Gst.MasterAsync(null, db.PropertyId, Ct)).ValueOn(GstKeys.BanquetBps, new DateOnly(2026, 11, 1)));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => db.GstMaster.WithdrawAsync(db.Ctx("TAD"), new WithdrawGstSettingRequest(future.VersionId, "No (fictional)"), Ct));
        var w = await db.GstMaster.WithdrawAsync(db.Ctx("OWN"), new WithdrawGstSettingRequest(future.VersionId, "Changed my mind (fictional)"), Ct);
        Assert.Equal(("Withdrawn", 1800L), (w.Value.Status, w.Value.After));
        Assert.Equal(1800, (await db.Gst.MasterAsync(null, db.PropertyId, Ct)).ValueOn(GstKeys.BanquetBps, new DateOnly(2026, 11, 1)));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => db.GstMaster.WithdrawAsync(db.Ctx("OWN"), new WithdrawGstSettingRequest(future.VersionId, "Again (fictional)"), Ct));

        // History keeps every version (seed + changes, withdrawn included); past dates keep the statutory seed.
        var view = await db.GstMaster.GetAsync(db.PropertyId, db.User["OWN"], Day, Ct);
        Assert.Contains(view.History, h => h.VersionId == future.VersionId && h.Status == "withdrawn");
        Assert.Contains(view.History, h => h.VersionId == change.VersionId && h.EffectiveFrom == "2026-10-25" && h.Source == "owner");
        Assert.Equal(1200, (await db.Gst.MasterAsync(null, db.PropertyId, Ct)).ValueOn(GstKeys.RoomLowBps, new DateOnly(2025, 9, 21)));
        // Audit + outbox rows were written for each change.
        Assert.True(await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action IN ('GST_SETTING_SET','GST_SETTING_WITHDRAWN');") >= 4);
        Assert.True(await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'GstSettingChanged';") >= 4);

        // Append-only history (trigger): values cannot be edited or rows deleted.
        await Assert.ThrowsAsync<SqlException>(() => db.ScalarAsync($"UPDATE tax.gst_setting_version SET value = 1 WHERE version_id = {change.VersionId};"));
        await Assert.ThrowsAsync<SqlException>(() => db.ScalarAsync($"DELETE tax.gst_setting_version WHERE version_id = {change.VersionId};"));
        _ = folio;
    }

    [Fact]
    public async Task Posted_lines_keep_their_snapshot_after_master_changes_new_postings_use_the_master()
    {
        var (stay, folio) = await db.InHouseAsync(200_000, Day, Day.AddDays(2));
        await db.NightAsync(stay, Day);
        await db.ChargeAsync(folio, "Laundry press", 50_000);
        var before = await db.Queries.GetFolioAsync(db.As("OWN"), folio, Ct);
        await db.SetGstAsync(GstKeys.LaundryBps, 1200);
        await db.SetGstAsync(GstKeys.RoomThreshold, 100_000);
        try
        {
            var after = await db.Queries.GetFolioAsync(db.As("OWN"), folio, Ct);
            Assert.Equal(before.Lines.Select(l => (l.ChargeId, l.TaxBps, l.TaxPaise)), after.Lines.Select(l => (l.ChargeId, l.TaxBps, l.TaxPaise)));
            Assert.Equal(before.Totals, after.Totals);
            Assert.Equal(1200, (await db.ChargeAsync(folio, "Laundry", 50_000)).TaxBps);
            Assert.Equal(1800, (await db.Gst.RoomNightAsync(null, db.PropertyId, Day, new Paise(200_000), Ct)).Bps); // threshold now ₹1,000
        }
        finally
        {
            await db.SetGstAsync(GstKeys.LaundryBps, 1800);
            await db.SetGstAsync(GstKeys.RoomThreshold, 750_000);
        }
    }

    [Fact]
    public async Task Specified_premises_derived_from_last_FY_tariffs_or_charged_nights_and_owner_opt_in_drive_restaurant_GST()
    {
        var (_, folio) = await db.InHouseAsync(200_000, Day, Day.AddDays(2));
        Assert.Equal(500, (await db.ChargeAsync(folio, "Breakfast", 20_000)).TaxBps);       // no unit above ₹7,500 in FY 2025-26
        Assert.Equal(500, (await db.ChargeAsync(folio, "Hall hire", 20_000, "food")).TaxBps); // explicit category wins
        await Expect.Fails(HccErrorCode.VALIDATION, () => db.ChargeAsync(folio, "X", 100, "liquor"));

        // A Suite tariff above ₹7,500 for June 2025 → FY 2025-26 had such a unit → specified premises in FY 2026-27 (18 % with ITC).
        await db.ScalarAsync("INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES (@t, '2025-06-01', 800000), (@t, '2025-07-01', 450000);",
            ("@t", SqlDbType.Int, db.SuiteTypeId));
        try
        {
            var food = await db.Gst.ChargeAsync(null, db.PropertyId, Day, GstCategory.Food, Ct);
            Assert.Equal((1800, true), (food.Bps, food.Itc));
            Assert.Equal(500, (await db.Gst.ChargeAsync(null, db.PropertyId, new DateOnly(2026, 3, 31), GstCategory.Food, Ct)).Bps); // FY 25-26 looks at FY 24-25
            var line = await db.ChargeAsync(folio, "Room service dinner", 30_000);
            Assert.Equal((1800, 5_400L), (line.TaxBps, line.TaxPaise));
        }
        finally
        {
            await db.ScalarAsync("DELETE inv.room_type_tariff WHERE room_type_id = @t;", ("@t", SqlDbType.Int, db.SuiteTypeId));
        }

        // Exactly ₹7,500 is not above (a room night actually charged above it: GstSpecifiedFromChargedNightsTests).
        await db.ScalarAsync("INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES (@t, '2025-06-01', 750000);", ("@t", SqlDbType.Int, db.SuiteTypeId));
        Assert.Equal(500, (await db.Gst.ChargeAsync(null, db.PropertyId, Day, GstCategory.Food, Ct)).Bps);
        await db.ScalarAsync("DELETE inv.room_type_tariff WHERE room_type_id = @t;", ("@t", SqlDbType.Int, db.SuiteTypeId));

        // Owner opt-in from a date.
        await db.SetGstAsync(GstKeys.FoodOptIn, 1, "2026-10-25");
        Assert.Equal(500, (await db.Gst.ChargeAsync(null, db.PropertyId, Day, GstCategory.Food, Ct)).Bps);
        Assert.Equal(1800, (await db.Gst.ChargeAsync(null, db.PropertyId, Day.AddDays(1), GstCategory.Food, Ct)).Bps);
        var view = await db.GstMaster.GetAsync(db.PropertyId, db.User["OWN"], Day.AddDays(1), Ct);
        Assert.True(view.SpecifiedPremises);
        Assert.Contains("opt-in", view.SpecifiedReason, StringComparison.Ordinal);
        await db.SetGstAsync(GstKeys.FoodOptIn, 0, "2026-10-26");
    }

    [Fact]
    public async Task BASE_drawer_is_10725_with_the_restaurant_cash_bill_at_5_percent_GST()
    {
        var g = BaseGolden.Load();
        var food = await db.Gst.ChargeAsync(null, db.PropertyId, Day, GstCategory.Food, Ct);
        Assert.Equal(500, food.Bps);
        var till = (await db.Cashier.CreateTillAsync(db.As("OWN"), GstIdsDb.Key(), new CreateTillRequest("GST", "Front desk"), Ct)).Value;
        var shift = (await db.Cashier.OpenShiftAsync(db.As("CSH"), GstIdsDb.Key(), new OpenShiftRequest(till.TillId, g.OpeningCashPaise), Ct)).Value;
        foreach (var r in g.FolioReceipts.Where(r => r.Method == "Cash"))
        {
            var (_, folio) = await db.InHouseAsync(200_000, Day, Day.AddDays(1));
            await db.Billing.RecordReceiptAsync(db.As("CSH"), GstIdsDb.Key(), new RecordReceiptRequest(folio, r.Kind, "Cash", r.AmountPaise, null, till.TillId), Ct);
        }

        // Restaurant cash bill: net from the DEC-039 fixture (₹550 = ₹500 + 10 %), re-taxed by the GST master (₹500 + 5 % = ₹525).
        await db.Runner.RunAsync(new(db.PropertyId, db.User["CSH"], "GstBaseOutlet", GstIdsDb.Key()), new { fixture = "BASE" }, async (tx, ct) =>
        {
            var n = 0;
            foreach (var o in g.OutletReceipts.Where(o => o.Method == "Cash"))
            {
                Assert.Equal(0, o.AmountPaise * 10_000 % 11_000);
                var net = new Paise(o.AmountPaise * 10_000 / 11_000);
                var gross = net + GstMath.LineTax(net, food.Bps);
                Assert.Equal(52_500, gross.Value);
                await db.Cashier.RecordMovementAsync(tx, till.TillId, CashMovementKind.PosSale, "pos-receipt", ++n, gross, "Restaurant · BASE", "POS", ct);
            }

            foreach (var e in g.Expenses.Where(e => e.Method == "Cash" && e.Status is "Posted" or "Reversed"))
                await db.Cashier.RecordMovementAsync(tx, till.TillId, CashMovementKind.Expense, "expense", ++n, new Paise(-e.AmountPaise), "Cash expense · " + e.Id, e.Id, ct);
            await tx.AuditAsync("TEST_FIXTURE", "shift", "BASE outlet cash (GST) + posted cash expense", ct);
            return new { movements = n };
        }, Ct);
        var view = await db.Queries.GetShiftAsync(db.As("CSH"), shift.ShiftId, Ct);
        Assert.Equal(1_072_500, view.Expected.ExpectedPaise);
        Assert.Equal("₹10,725", new Paise(view.Expected.ExpectedPaise).ToString());
    }
}

/// <summary>A room night actually charged above ₹7,500 last FY also makes the hotel specified premises (own database: bill.charge is append-only).</summary>
[Trait("Category", "Sql")]
public class GstSpecifiedFromChargedNightsTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    [Fact]
    public async Task A_charged_night_above_7500_in_the_previous_FY_derives_specified_premises()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(500, (await db.Gst.ChargeAsync(null, db.PropertyId, GstIdsDb.Day, GstCategory.Food, ct)).Bps);
        var (stay, _) = await db.InHouseAsync(900_000, new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 2));
        var night = await db.NightAsync(stay, new DateOnly(2025, 12, 1));
        Assert.Equal((1800, 162_000L), (night.TaxBps, night.TaxPaise));
        var food = await db.Gst.ChargeAsync(null, db.PropertyId, GstIdsDb.Day, GstCategory.Food, ct);
        Assert.Equal((1800, true), (food.Bps, food.Itc));
        Assert.Equal(500, (await db.Gst.ChargeAsync(null, db.PropertyId, new DateOnly(2026, 3, 31), GstCategory.Food, ct)).Bps);
    }
}

/// <summary>A03a-02: a night above ₹7,500 posted by mistake and reversed was never supplied at that price (own database: bill.charge is append-only).</summary>
[Trait("Category", "Sql")]
public class GstSpecifiedIgnoresReversedNightsTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    [Fact]
    public async Task A_reversed_night_above_7500_in_the_previous_FY_does_not_derive_specified_premises_a_standing_one_does()
    {
        var ct = TestContext.Current.CancellationToken;
        var (stay, folio) = await db.InHouseAsync(900_000, new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 2));
        var night = await db.NightAsync(stay, new DateOnly(2025, 12, 1));
        Assert.Equal((1800, 162_000L), (night.TaxBps, night.TaxPaise));
        var req = await db.Billing.RequestAdjustmentAsync(db.As("REC"), GstIdsDb.Key(),
            new RequestAdjustmentRequest("correction", folio, night.ChargeId, 900_000, "Posted at the wrong rate (fictional)", night.FolioVersion), ct);
        var ok = await db.Billing.DecideApprovalAsync(db.As("OWN"), GstIdsDb.Key(), new DecideApprovalRequest(req.Value.ApprovalId!.Value, true, null, req.Value.ApprovalVersion!), ct);
        Assert.Equal("Executed", ok.Value.Status);
        Assert.Equal(1L, await db.LongAsync("SELECT COUNT(*) FROM bill.charge WHERE reverses_charge_id = @c;", ("@c", SqlDbType.BigInt, night.ChargeId)));

        // The reversed ₹9,000 night (and its −₹9,000 reversal) does not count: FY 2025-26's highest is the Suite rack rate ₹4,500.
        var food = await db.Gst.ChargeAsync(null, db.PropertyId, GstIdsDb.Day, GstCategory.Food, ct);
        Assert.Equal((500, false), (food.Bps, food.Itc));
        var view = await db.GstMaster.GetAsync(db.PropertyId, db.User["OWN"], GstIdsDb.Day, ct);
        Assert.False(view.SpecifiedPremises);
        Assert.Contains("highest ₹4,500", view.SpecifiedReason, StringComparison.Ordinal);

        // A ₹9,000 night that stands still derives it.
        var (stay2, _) = await db.InHouseAsync(900_000, new DateOnly(2025, 12, 2), new DateOnly(2025, 12, 3));
        await db.NightAsync(stay2, new DateOnly(2025, 12, 2));
        var standing = await db.Gst.ChargeAsync(null, db.PropertyId, GstIdsDb.Day, GstCategory.Food, ct);
        Assert.Equal((1800, true), (standing.Bps, standing.Itc));
    }
}
