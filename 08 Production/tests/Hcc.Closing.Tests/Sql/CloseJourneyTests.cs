using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Closing;
using Hcc.Contracts.Stays;
using Hcc.Domain.Payments;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// The prototype S0 three-day walkthrough (03 Prototype/tests/stream-f.mjs <c>walkthrough()</c>, DEC-069 figures) through the real
/// commands of every module and RunBusinessDayClose: drawer path ₹10,725 → ₹11,225 → ₹11,225 → ₹12,725 → ₹12,425, closing counts
/// ₹11,225 / ₹11,225 / ₹12,425, the interrupted night audit posting each night once, and every closed date's report equal to its
/// snapshot. The drawer figures are tax-source agnostic (cash amounts); the Room 105 invoice total is GST-dependent (see
/// <see cref="Room_105_invoice_is_4265_with_the_GST_master"/>).
/// </summary>
[Trait("Category", "Sql")]
public sealed class CloseJourneyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D24 = new(2026, 10, 24);
    private static readonly DateOnly D25 = new(2026, 10, 25);
    private static readonly DateOnly D26 = new(2026, 10, 26);

    public sealed record Walkthrough(Hotel H, long FolioId, long[] Drawers, FolioTotalsView Folio105, long FinalUpiPaise, List<long> BaseFolios);

    /// <summary>BASE day 1: 8 in-house stays, ₹5,000 float, cash ₹7,000 + UPI ₹14,500 payments, ₹525 restaurant cash, ₹1,800 cash expense.</summary>
    public static async Task<(int TillId, ShiftResult Shift, List<long> Folios)> BaseDayAsync(Hotel h, CancellationToken ct)
    {
        var folios = new List<long>();
        foreach (var room in new[] { "101", "102", "103", "104", "106", "107", "108", "109" })
            folios.Add((await h.SeedInHouseAsync(room, "2026-10-23", "2026-10-30", ct: ct)).FolioId);
        var (till, shift) = await h.OpenTillAsync(500000, ct);
        await h.PayAsync(folios[0], 200000, "Cash", till, ct: ct);
        await h.PayAsync(folios[0], 100000, "UPI", ct: ct);
        await h.PayAsync(folios[1], 200000, "UPI", ct: ct);
        await h.PayAsync(folios[2], 300000, "UPI", ct: ct);
        await h.PayAsync(folios[3], 200000, "Cash", till, ct: ct);
        await h.PayAsync(folios[4], 200000, "UPI", ct: ct);
        await h.PayAsync(folios[5], 450000, "UPI", ct: ct);
        await h.PayAsync(folios[6], 200000, "Cash", till, ct: ct);
        await h.PayAsync(folios[7], 200000, "UPI", ct: ct);
        await h.PayAsync(folios[7], 100000, "Cash", till, ct: ct);
        await h.DrawerAsync(till, CashMovementKind.PosSale, 52500, "Restaurant · breakfast POS-F01 (₹500 + 5 %)", ct: ct);
        await h.DrawerAsync(till, CashMovementKind.Expense, -180000, "Cash expense · Fresh Linen Services", ct: ct);
        return (till, shift, folios);
    }

    public static async Task<Walkthrough> RunAsync(Hotel h, CancellationToken ct)
    {
        var (till, shift1, baseFolios) = await BaseDayAsync(h, ct);
        var drawers = new List<long> { await h.ExpectedAsync(shift1.ShiftId, ct) };

        // Day 1 (24 Oct): Room 105 booked, UPI advance, registered, checked in, ₹500 cash deposit.
        var (_, stay105, folio) = await h.BookAndCheckInAsync("105", "2026-10-24", "2026-10-26", "Rahul Verma (fictional)", ct);
        await h.PayAsync(folio, 100000, "UPI", who: h.Reception, ct: ct);
        await h.PayAsync(folio, 50000, "Cash", till, kind: "deposit", ct: ct);
        drawers.Add(await h.ExpectedAsync(shift1.ShiftId, ct));

        // Night audit interrupted after 4 of 9 stays (S5), then resumed; the open shift blocks the close at the cash step.
        var nights = 0;
        h.Observer.OnStep = (point, _, _) => point == "night" && ++nights > 4 ? throw new SimulatedCrashException() : Task.CompletedTask;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => h.CloseAsync(ct: ct));
        Assert.Equal(4, await h.LongAsync("SELECT COUNT(*) FROM bill.charge WHERE kind = 'room' AND service_date = '2026-10-24'", ct));
        h.Observer.OnStep = null;
        var blocked = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.CloseAsync(ct: ct));
        Assert.Contains("cash shifts", blocked.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(9, await h.LongAsync("SELECT COUNT(*) FROM bill.charge WHERE kind = 'room' AND service_date = '2026-10-24'", ct));
        await h.CountExactAsync(shift1.ShiftId, ct);
        var day1 = (await h.CloseAsync(ct: ct)).Value;
        Assert.Equal(("Completed", 3, D25), (day1.Status, day1.Attempts, day1.NextBusinessDate!.Value));

        // Day 2 (25 Oct): float carried; laundry ₹500; ₹500 discount above Reception's limit, approved by the Manager.
        var shift2 = (await h.Till.OpenShiftAsync(h.As(h.Cashier), Hotel.Key(), new OpenShiftRequest(till, null), ct)).Value;
        drawers.Add(await h.ExpectedAsync(shift2.ShiftId, ct));
        await h.Billing.PostChargeAsync(h.As(h.Reception), Hotel.Key(), new PostChargeRequest(folio, "Laundry service", 50000), ct);
        var v = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, ct)).Version;
        // Prototype: Reception requests (above its ₹250 limit → approval request), the Manager approves (≤ ₹1,000, DEC-063 G1).
        var req = (await h.Billing.RequestAdjustmentAsync(h.As(h.Reception), Hotel.Key(), new RequestAdjustmentRequest("discount", folio, null, 50000, "Service delay goodwill", v), ct)).Value;
        Assert.Equal("PendingApproval", req.Outcome);
        var approved = (await h.Billing.DecideApprovalAsync(h.As(h.Manager), Hotel.Key(), new DecideApprovalRequest(req.ApprovalId!.Value, true, "Service delay", req.ApprovalVersion!), ct)).Value;
        Assert.Equal("Executed", approved.Status);
        await h.CountExactAsync(shift2.ShiftId, ct);
        Assert.Equal("Completed", (await h.CloseAsync(date: D25, ct: ct)).Value.Status);

        // Day 3 (26 Oct): ₹2,000 cash, the balance by UPI, deposit returned in cash, checkout, ₹300 cash expense.
        var shift3 = (await h.Till.OpenShiftAsync(h.As(h.Cashier), Hotel.Key(), new OpenShiftRequest(till, null), ct)).Value;
        await h.PayAsync(folio, 200000, "Cash", till, ct: ct);
        var balance = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, ct)).Totals.BalancePaise;
        await h.PayAsync(folio, balance, "UPI", ct: ct);
        await h.PayAsync(folio, 50000, "Cash", till, kind: "deposit-return", ct: ct);
        drawers.Add(await h.ExpectedAsync(shift3.ShiftId, ct));
        var folio105 = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, ct)).Totals;
        var stayVersion = await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay105}", ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stay105, stayVersion), ct);
        await h.DrawerAsync(till, CashMovementKind.Expense, -30000, "Cash expense · Aqua Water Cans", ct: ct);
        drawers.Add(await h.ExpectedAsync(shift3.ShiftId, ct));
        await h.CountExactAsync(shift3.ShiftId, ct);
        Assert.Equal("Completed", (await h.CloseAsync(date: D26, ct: ct)).Value.Status);
        return new Walkthrough(h, folio, [.. drawers], folio105, balance, baseFolios);
    }

    [Fact]
    public async Task S0_walkthrough_drawer_path_closing_counts_and_reports_equal_snapshots()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var w = await RunAsync(h, Ct);

        Assert.Equal([1072500L, 1122500L, 1122500L, 1272500L, 1242500L], w.Drawers);
        var snaps = new List<ClosingSnapshotView>();
        foreach (var d in new[] { D24, D25, D26 }) snaps.Add(await h.Closing.GetSnapshotAsync(h.As(h.Owner), d, Ct));
        Assert.Equal([1122500L, 1122500L, 1242500L], snaps.Select(s => s.Record.Figures.Drawer.CountedPaise!.Value));
        Assert.All(snaps, s => Assert.Equal(0, s.Record.Figures.Drawer.DifferencePaise));
        Assert.Equal(new DateOnly(2026, 10, 27), await h.BusinessDateAsync(Ct));

        // Day 1 record: collections ₹21,500 + Room 105 UPI advance; resumed night audit, 9 stays, each night posted once.
        var r1 = snaps[0].Record;
        Assert.Equal(2150000 + 100000, r1.Figures.Receipts.NetPaise);
        Assert.Equal((9, true, 3), (r1.NightAuditStays, r1.NightAuditResumed, r1.Attempts));
        Assert.Contains(r1.Exceptions, e => e.Contains("resumed", StringComparison.Ordinal));
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM (SELECT folio_id, service_date FROM bill.charge WHERE kind = 'room' GROUP BY folio_id, service_date HAVING COUNT(*) > 1) x", Ct));
        Assert.Equal((10, 9, "90.0%"), (r1.Figures.Occupancy.SellableRoomNights, r1.Figures.Occupancy.OccupiedRoomNights, r1.Figures.Occupancy.OccupancyPct));
        Assert.Equal(52500, r1.Figures.Drawer.Tills.Single().RestaurantPaise);
        Assert.Equal((180000L, 50000L), (r1.Figures.Expenses.CashPaise, r1.Figures.Receipts.DepositsInPaise));
        Assert.Equal(50000L, r1.DepositsHeldAtClosePaise);

        // Room 105 (tax-source agnostic): net ₹4,000; total = net + Σ stored line tax; settled; deposit in and out.
        Assert.Equal(400000, w.Folio105.NetPaise);
        Assert.Equal(w.Folio105.NetPaise + w.Folio105.TaxPaise, w.Folio105.TotalPaise);
        Assert.Equal(await h.LongAsync($"SELECT SUM(tax_paise) FROM bill.charge WHERE folio_id = {w.FolioId}", Ct), w.Folio105.TaxPaise);
        Assert.Equal((0L, 0L, w.Folio105.TotalPaise), (w.Folio105.BalancePaise, w.Folio105.DepositPaise, w.Folio105.PaidPaise));
        var r3 = snaps[2].Record.Figures;
        Assert.Equal((1, 50000L, 30000L), (r3.Days.Single().Departures, r3.Receipts.DepositsOutPaise, r3.Expenses.CashPaise));

        // The range report over the three days = the snapshots' figures per date, every closed date re-verified.
        var report = await h.Reports.GetDailyReportAsync(h.As(h.Accountant), D24, D26, Ct);
        Assert.All(report.DayStatus, s => Assert.Equal(("Closed", (bool?)true), (s.Status, s.MatchesSnapshot)));
        Assert.Equal(snaps.Sum(s => s.Record.Figures.Revenue.NetPaise), report.Report.Revenue.NetPaise);
        Assert.Equal(snaps.Sum(s => s.Record.Figures.Receipts.NetPaise), report.Report.Receipts.NetPaise);
        Assert.Equal(report.Report.Revenue.NetPaise, report.Report.Days.Sum(d => d.NetRevenuePaise));
        Assert.Equal((500000L, 1242500L, 1242500L), (report.Report.Drawer.OpeningPaise, report.Report.Drawer.ExpectedPaise, report.Report.Drawer.CountedPaise!.Value));
        Assert.Equal(report.Report.Revenue.TaxPaise, report.Report.Gst.Sum(g => g.CgstPaise + g.SgstPaise));
        Assert.Equal(9 + 9 + 8, report.Report.Occupancy.OccupiedRoomNights); // 9 + 9 on 24/25, 8 on 26 (105 departed)
    }

    /// <summary>
    /// GST-dependent golden (prototype DEC-069): Room 105 = ₹4,265 incl. ₹265 GST (rooms 5 %, laundry 18 %, discount at the room rate), final
    /// UPI ₹1,265, taxed at posting by agent C's GST master (IGstRateResolver) and reported from the per-charge GST records.
    /// </summary>
    [Fact]
    public async Task Room_105_invoice_is_4265_with_the_GST_master()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var w = await RunAsync(h, Ct);
        Assert.Equal(new FolioTotalsView(400000, 26500, 426500, 426500, 0, 0, 0), w.Folio105);
        Assert.Equal(126500, w.FinalUpiPaise);
        var pack = (await h.Reports.GetDailyReportAsync(h.As(h.Owner), D24, D26, Ct)).Report;
        var folioGst = await h.LongAsync($"SELECT COUNT(*) FROM tax.charge_gst g JOIN bill.charge c ON c.charge_id = g.charge_id WHERE c.folio_id = {w.FolioId}", Ct);
        Assert.Equal(4, folioGst); // 2 room nights, laundry, discount: each with its stored GST record
        Assert.Contains(pack.Gst, g => (g.Category, g.TaxBps, g.TaxPaise, g.CgstPaise, g.SgstPaise) == ("laundry", 1800, 9000L, 4500L, 4500L));
        Assert.Equal(pack.Revenue.TaxPaise, pack.Gst.Sum(g => g.CgstPaise + g.SgstPaise));
    }

    [Fact]
    public async Task Close_and_report_arithmetic_on_stored_GST_snapshots_gives_4265_for_room_105()
    {
        // Tax-source agnostic: the P4 lines are loaded with the DEC-069 snapshots (as P3's seed does), the close reads only the stored snapshots.
        await using var h = await Hotel.CreateAsync(Ct);
        var (_, stay, folio) = await h.SeedInHouseAsync("105", "2026-10-24", "2026-10-26", ct: Ct);
        await h.ScalarAsync($"""
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES
              ({folio}, 'room', '2026-10-24', N'Room night · 2026-10-24', 200000, 500, 10000),
              ({folio}, 'service', '2026-10-24', N'Laundry service', 50000, 1800, 9000),
              ({folio}, 'discount', '2026-10-24', N'Approved discount · Service delay goodwill', -50000, 500, -2500);
            """, Ct);
        await h.ScalarAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({folio}, 'room', '2026-10-25', N'Room night · 2026-10-25', 200000, 500, 10000);", Ct);
        await h.CloseAsync(ct: Ct);
        var day1 = (await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct)).Record.Figures.Revenue;
        Assert.Equal((200000L, 16500L, 216500L), (day1.NetPaise, day1.TaxPaise, day1.GrossPaise));
        await h.CloseAsync(date: D25, ct: Ct); // posts nothing new: the 25th night is already there
        var both = (await h.Reports.GetDailyReportAsync(h.As(h.Owner), D24, D25, Ct)).Report;
        Assert.Equal((400000L, 26500L, 426500L), (both.Revenue.NetPaise, both.Revenue.TaxPaise, both.Revenue.GrossPaise));
        Assert.Equal((13250L, 13250L), (both.Revenue.CgstPaise, both.Revenue.SgstPaise));
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folio} AND kind = 'room' AND service_date = '2026-10-25'", Ct));
        Assert.True(stay > 0);
    }
}
