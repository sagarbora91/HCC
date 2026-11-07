// F12-MEAS2-F · SelectAsync of all 32 measurement reports as the Owner on one seeded fictional property (MeasReportSqlFixture):
// no exception, the expected row count, and pinned values for the money reports. Also proves N1 (study bill), N2 (FF&E reserve), N3 (voids) and N4 (discrepancy age).
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Analytics;
using Hcc.Reporting.Reports.MeasDaily;
using Hcc.Reporting.Reports.MeasGuest;
using Hcc.Reporting.Reports.MeasHk;
using Hcc.Reporting.Reports.MeasPeople;
using Hcc.Reporting.Reports.MeasRevenue;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Meas.Tests.Reports;

public sealed class MeasReportSqlTests(MeasReportSqlFixture f) : IClassFixture<MeasReportSqlFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Bd = new(2026, 11, 5);
    private static readonly DateOnly Nov1 = new(2026, 11, 1);
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    private Caller Owner => new(f.P, f.Owner);

    private static BoundParams Bp(DateOnly? from = null, DateOnly? to = null, params (string K, string V)[] v) =>
        new(v.ToDictionary(x => x.K, x => x.V), from, to);

    private static TestClock Clock => new(new DateTimeOffset(2026, 11, 5, 9, 30, 0, TimeSpan.Zero)); // 15:00 on the property clock

    /// <summary>The 32 reports: id, parameters, expected row count.</summary>
    private Dictionary<string, (Func<Task<ReportResult>> Run, int Rows)> Reports()
    {
        var db = f.Db.Platform;
        var y = f.Yield;
        var policy = new OwnerPolicy();
        var c = Owner;
        var clock = Clock;
        Func<Task<ReportResult>> R(IReportSelector s, BoundParams p) => () => s.SelectAsync(c, p, Ct);
        var fy = new DateOnly(2026, 4, 1);
        var list = new (IReportSelector S, BoundParams P, int Rows)[]
        {
            (new ManagerFlashReport(db), Bp(Bd, Bd), 12),
            (new RegretsDenialsReport(db), Bp(Nov1, Bd), 5),
            (new RateVarianceReport(db), Bp(Nov1, Bd), 3),
            (new RoomDiscrepancyReport(db), Bp(null, null, ("at", "2026-11-05T15:00")), 2),
            (new HighBalanceReport(db), Bp(), 3),
            (new AllowancesVoidsReport(db), Bp(Bd, Bd), 5),
            (new NoShowCancelReport(db), Bp(Nov1, Bd), 2),
            (new InventoryExceptionsReport(db), Bp(Bd, Bd), 2),
            (new CashierVarianceReport(db), Bp(Nov1, Bd), 3),
            (new DeskTimingReport(db), Bp(Nov1, Bd), 2),
            (new RepeatLoyaltyReport(db), Bp(null, null, ("month", "2026-11")), 2),
            (new ReputationIndexReport(db), Bp(fy, new DateOnly(2027, 3, 31)), 3),
            (new SatisfactionReport(db, null, clock), Bp(Nov1, new DateOnly(2026, 11, 30), ("month", "2026-11")), 1),
            (new ServiceRecoveryReport(db, null, clock), Bp(Nov1, Bd), 1),
            (new UpsellReport(db), Bp(Nov1, Bd), 1),
            (new AssetLifeReport(db), Bp(null, Bd), 4),
            (new HkProductivityReport(db), Bp(Nov1, Bd), 2),
            (new LinenCostReport(db), Bp(Nov1, Bd), 2),
            (new OooDaysReport(db), Bp(new DateOnly(2026, 10, 20), Bd), 3),
            (new PpmComplianceReport(db), Bp(fy, Bd), 1),
            (new UtilitiesPerRoomReport(db), Bp(Nov1, Bd), 6),
            (new AgentScorecardReport(db, policy), Bp(Nov1, Bd), 3),
            (new AttritionReport(db), Bp(fy, new DateOnly(2027, 3, 31)), 2),
            (new StaffProductivityReport(db), Bp(Nov1, Bd), 3),
            (new StaffPulseReport(db, policy), Bp(null, null, ("fy", "2026-27")), 1),
            (new TrainingReport(db), Bp(null, Bd), 12),
            (new DemandCalendarReport(db, y, null, null, clock), Bp(Bd, null, ("nights", "7")), 7),
            (new DisplacementReport(db, y, null, null, clock), Bp(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 8), ("rooms", "2"), ("rate", "1500")), 4),
            (new OverbookingWashReport(db, y, null, null, clock), Bp(Nov1, Bd), 6),
            (new PickupPaceReport(db, y, null, null, clock), Bp(null, null, ("months", "3")), 3),
            (new RateParityReport(db, y, null, null, clock), Bp(Bd, null, ("nights", "3")), 3),
            (new RestrictionEffectReport(db, y, null, null, clock), Bp(new DateOnly(2026, 10, 27), Bd), 2),
        };
        return list.ToDictionary(x => x.S.Definition.Id, x => (R(x.S, x.P), x.Rows));
    }

    private static readonly string[] AllIds =
    [
        "rpt.guests.manager-flash", "rpt.guests.regrets-denials", "rpt.guests.rate-variance", "rpt.rooms.room-discrepancy", "rpt.money.high-balance",
        "rpt.money.allowances-voids", "rpt.guests.no-show-cancel", "rpt.rooms.inventory-exceptions", "rpt.money.cashier-variance", "rpt.guests.desk-timing",
        "rpt.guests.repeat-loyalty", "rpt.guests.reputation-index", "rpt.guests.satisfaction", "rpt.guests.service-recovery", "rpt.guests.upsell",
        "rpt.acc.asset-life", "rpt.rooms.hk-productivity", "rpt.stock.linen-cost", "rpt.rooms.ooo-days", "rpt.rooms.ppm-compliance", "rpt.rooms.utilities-per-room",
        "rpt.people.agent-scorecard", "rpt.people.attrition", "rpt.people.staff-productivity", "rpt.people.staff-pulse", "rpt.people.training",
        "rpt.revenue.demand-calendar", "rpt.revenue.displacement", "rpt.revenue.overbooking-wash", "rpt.revenue.pickup-pace", "rpt.revenue.rate-parity", "rpt.revenue.restriction-effect",
    ];

    public static TheoryData<string> Ids() => new(AllIds);

    [Fact]
    public void The_smoke_covers_exactly_32_distinct_reports()
    {
        var ids = Reports().Keys.ToList();
        Assert.Equal(32, ids.Count);
        Assert.Equal(32, AllIds.Distinct().Count());
        Assert.Empty(ids.Except(AllIds));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task Each_report_runs_as_the_Owner_with_the_expected_row_count(string id)
    {
        var (run, rows) = Reports()[id];
        var r = await run();
        Assert.Equal(id, r.Id);
        Assert.Equal(rows, r.Rows.Count);
    }

    private async Task<ReportResult> Run(string id) => await Reports()[id].Run();

    private static object? V(ReportRow r, string k) => r.Cells[k];

    [Fact]
    public async Task Flash_pins_occupancy_adr_and_revpar_with_the_reposted_night_counted_once()
    {
        var r = await Run("rpt.guests.manager-flash");
        ReportRow K(string k) => r.Rows.Single(x => (string?)x.Cells["kpi"] == k);
        Assert.Equal(50.0, V(K("Occupancy %"), "pctActual"));          // 3 slept / 6 rooms in service (105 is blocked)
        Assert.Equal(200000L, V(K("ADR"), "amtActual"));               // 6,000 of live room lines / 3 charged nights
        Assert.Equal(100000L, V(K("RevPAR"), "amtActual"));            // 6,000 / 6
        Assert.Equal(1L, V(K("OOO"), "cntActual"));
        Assert.Equal(2L, V(K("Arrivals"), "cntActual"));
        Assert.Equal(1L, V(K("Rooms on books +7"), "cntActual"));
    }

    [Fact]
    public async Task N3_voids_show_rate_reposts_as_their_own_kind_and_only_the_real_void_is_red()
    {
        var r = await Run("rpt.money.allowances-voids");
        var kinds = r.Rows.Select(x => (string)V(x, "kind")!).ToList();
        Assert.Equal(["discount", "rate-change", "rate-change", "refund", "void"], kinds);
        var rate = r.Rows.Where(x => (string)V(x, "kind")! == "rate-change").ToList();
        Assert.Equal(("SM-3", 220000L, "Corporate rate agreed", "Manager SM", "Owner SM", ""), ((string)V(rate[0], "bill")!, (long)V(rate[0], "amount")!, (string)V(rate[0], "reasonCode")!, (string)V(rate[0], "user")!, (string)V(rate[0], "approver")!, (string)V(rate[0], "exception")!));
        Assert.Equal(("SM-4", 250000L, "Owner SM", ""), ((string)V(rate[1], "bill")!, (long)V(rate[1], "amount")!, (string)V(rate[1], "approver")!, (string)V(rate[1], "exception")!));
        var disc = r.Rows.Single(x => (string)V(x, "kind")! == "discount");
        Assert.Equal((20000L, "Manager SM", ""), ((long)V(disc, "amount")!, (string)V(disc, "approver")!, (string)V(disc, "exception")!));
        Assert.Equal(30000L, (long)V(r.Rows.Single(x => (string)V(x, "kind")! == "refund"), "amount")!);
        var voids = r.Rows.Single(x => (string)V(x, "kind")! == "void");
        Assert.Equal((5000L, "No approver"), ((long)V(voids, "amount")!, (string)V(voids, "exception")!));
        Assert.Equal(1, r.Rows.Count(x => (string)V(x, "exception")! != ""));                   // only the real void is red
    }

    [Fact]
    public async Task N4_room_discrepancy_ages_in_local_time_from_the_offline_set_time()
    {
        var r = await Run("rpt.rooms.room-discrepancy");
        var ooo = r.Rows.Single(x => (string)V(x, "kind")! == "OOO-with-stay");
        Assert.Equal(("104", 1L, "", "2026-11-05 14:00"), ((string)V(ooo, "room")!, (long)V(ooo, "ageHours")!, (string)V(ooo, "exception")!, (string)V(ooo, "since")!)); // set 14:00 local, now 15:00: not red
        var vacant = r.Rows.Single(x => (string)V(x, "kind")! == "sold-but-vacant");
        Assert.Equal(("201", 3L), ((string)V(vacant, "room")!, (long)V(vacant, "ageHours")!));   // due at noon, 3 h ago: red
        Assert.NotEqual("", (string)V(vacant, "exception")!);
    }

    [Fact]
    public async Task N2_asset_life_reserve_base_counts_reposted_nights_once()
    {
        var r = await Run("rpt.acc.asset-life");
        var total = Assert.Single(r.Rows, x => x.Kind == ReportRowKind.Total);
        Assert.Equal(49500L, V(total, "reserveAccrued"));              // 3 % of 16,500.00 live room lines (the old query counted the three reversed originals too: 71,100)
        Assert.Equal(60000L, V(total, "capexSpent"));
        Assert.Equal("red", total.Group);
        var ac = r.Rows.Single(x => (string?)V(x, "asset") == "AC 102");
        Assert.Equal("red", ac.Group);                                 // past its 60-month life with no maintenance plan
    }

    [Fact]
    public async Task N1_study_bill_counts_the_reposted_night_once_through_the_analytics_loader()
    {
        var inp = await new SqlAnalyticsInputSource(f.Db.Platform).LoadAsync(f.P, new DateOnly(2026, 12, 31), Ct);
        var b = Assert.Single(inp.Bills, x => x.Id == "B-SM-1");
        Assert.Equal((450000L, 604000L, 2), (b.RoomChargePaise, b.TotalPaise, b.ChargedNights)); // 2,500 + 2,000 (not 2,500 + 2,500 + 2,000); other stays at the real extras
    }

    [Fact]
    public async Task High_balance_flags_the_walk_in_and_the_company_account()
    {
        var r = await Run("rpt.money.high-balance");
        Assert.Equal(["102", "103", "104"], r.Rows.Select(x => (string)V(x, "room")!));
        Assert.Equal((650000L, 150000L, 1L), ((long)V(r.Rows[0], "balance")!, (long)V(r.Rows[0], "overBy")!, (long)V(r.Rows[0], "daysInHouse")!));
        Assert.Equal((580000L, 100000L, 300000L, 180000L), ((long)V(r.Rows[1], "balance")!, (long)V(r.Rows[1], "deposit")!, (long)V(r.Rows[1], "limit")!, (long)V(r.Rows[1], "overBy")!));
        Assert.Equal(("", 200000L), ((string)V(r.Rows[2], "exception")!, (long)V(r.Rows[2], "balance")!));
    }

    [Fact]
    public async Task Cashier_variance_pins_the_month_to_date_and_the_limit()
    {
        var r = await Run("rpt.money.cashier-variance");
        var last = r.Rows[^1];
        Assert.Equal((-20000L, -25000L), ((long)V(last, "variance")!, (long)V(last, "mtdVariance")!));
        Assert.Contains("above", (string)V(last, "exception")!, StringComparison.Ordinal);
        Assert.Equal("", (string)V(r.Rows[0], "exception")!);
    }

    [Fact]
    public async Task Rate_variance_pins_the_card_rate_and_the_floor_exception()
    {
        var r = await Run("rpt.guests.rate-variance");
        Assert.Equal(["SM-1", "SM-3", "SM-4"], r.Rows.Select(x => (string)V(x, "booking")!));
        Assert.Equal((250000L, 200000L, -50000L), ((long)V(r.Rows[1], "cardRate")!, (long)V(r.Rows[1], "chargedRate")!, (long)V(r.Rows[1], "difference")!));
        Assert.Equal("", (string)V(r.Rows[1], "exception")!);                                      // 2,000 is above the 85 % floor of the agreed 2,200
        Assert.NotEqual("", (string)V(r.Rows[2], "exception")!);                                   // 2,000 is below the floor of 2,500
    }

    [Fact]
    public async Task Regrets_and_denials_pin_the_quoted_rate_and_the_four_price_regrets()
    {
        var r = await Run("rpt.guests.regrets-denials");
        Assert.Equal(4, r.Rows.Count(x => (string)V(x, "exception")! != ""));
        Assert.Equal(250000L, V(r.Rows[0], "quotedRate"));
    }

    [Fact]
    public async Task Upsell_pins_revenue_per_arrival()
    {
        var row = Assert.Single((await Run("rpt.guests.upsell")).Rows);
        Assert.Equal((4L, 1L, 1L, 50000L, 20000L, 30000L, 50000L), ((long)V(row, "arrivals")!, (long)V(row, "upgradesPaid")!, (long)V(row, "upgradesFree")!, (long)V(row, "lateCheckout")!, (long)V(row, "breakfast")!, (long)V(row, "extraBed")!, (long)V(row, "revenuePerArrival")!));
        Assert.Equal("Manager SM", V(row, "approver"));
        Assert.Null(row.Group);
    }

    [Fact]
    public async Task Service_recovery_pins_the_compensation_and_the_repeat()
    {
        var row = Assert.Single((await Run("rpt.guests.service-recovery")).Rows);
        Assert.Equal((2L, 50000L, 1L), ((long)V(row, "complaints")!, (long)V(row, "compensation")!, (long)V(row, "repeat")!));
        Assert.Equal("Manager SM", V(row, "approver"));
        Assert.Equal("red", row.Group);
    }

    [Fact]
    public async Task Repeat_loyalty_pins_the_share_and_the_loyal_guest()
    {
        var r = await Run("rpt.guests.repeat-loyalty");
        Assert.Equal((4L, 25.0, 1L), ((long)V(r.Rows[0], "stays")!, (double)V(r.Rows[0], "repeatShare")!, (long)V(r.Rows[0], "seenBefore")!));
        Assert.Equal(("Guest A (fictional)", "Gold", true), ((string)V(r.Rows[1], "guests3")!, (string)V(r.Rows[1], "loyaltyTag")!, (bool)V(r.Rows[1], "offerSent")!));
    }

    [Fact]
    public async Task Satisfaction_pins_the_scores_nps_and_the_unanswered_red()
    {
        var row = Assert.Single((await Run("rpt.guests.satisfaction")).Rows);
        Assert.Equal((6L, "4.0", 50L), ((long)V(row, "responses")!, (string)V(row, "overall")!, (long)V(row, "nps")!));
        Assert.Equal("red", row.Group);                                                            // the all-2 QR survey has had no complaint for 48 hours
    }

    [Fact]
    public async Task Desk_timing_pins_arrival_to_key_and_bill_to_checkout()
    {
        var r = await Run("rpt.guests.desk-timing");
        var noon = r.Rows.Single(x => (string)V(x, "hour")! == "12:00");
        Assert.Equal((3L, 4L), ((long)V(noon, "arrivals")!, (long)V(noon, "arrivalToKey")!));
        var ten = r.Rows.Single(x => (string)V(x, "hour")! == "10:00");
        Assert.Equal(10L, V(ten, "billToCheckout"));
        Assert.Equal("red", ten.Group);                                                            // 10 min against the 6 min check-out standard
    }

    [Fact]
    public async Task Hk_productivity_pins_minutes_pass_rate_and_the_failed_relet()
    {
        var r = await Run("rpt.rooms.hk-productivity");
        var asha = r.Rows[0];
        Assert.Equal(("Asha SM", 2L, 1L), ((string)V(asha, "attendant")!, (long)V(asha, "departures")!, (long)V(asha, "stayovers")!));
        Assert.Equal(("0 of 1", "red"), ((string)V(asha, "relet")!, asha.Group));
        Assert.Contains("Dusty", (string)V(asha, "rejections")!, StringComparison.Ordinal);
        Assert.Equal("red", asha.Group);
        Assert.Null(r.Rows[1].Group);
    }

    [Fact]
    public async Task Linen_cost_pins_the_cost_per_occupied_night()
    {
        var r = await Run("rpt.stock.linen-cost");
        var towel = r.Rows.Single(x => (string)V(x, "item")! == "Bath towel");
        Assert.Equal((96L, 3L, 3L, 49375L), ((long)V(towel, "par")!, (long)V(towel, "lost")!, (long)V(towel, "damaged")!, (long)V(towel, "costPerNight")!)); // (3+3+2) x 400 + 50 x 15 = 3,950 / 8 nights
        Assert.Equal("red", towel.Group);
        Assert.Equal(2500L, V(r.Rows.Single(x => (string)V(x, "item")! == "Soap"), "costPerNight")); // 40 x 5 / 8
    }

    [Fact]
    public async Task Ooo_days_pins_the_lost_revenue_and_the_idle_suites()
    {
        var r = await Run("rpt.rooms.ooo-days");
        Assert.Equal(["105", "201", "202"], r.Rows.Select(x => (string)V(x, "room")!));
        Assert.Equal((12L, 12L, 12L * 205000L), ((long)V(r.Rows[0], "roomDays")!, (long)V(r.Rows[0], "daysNoTicket")!, (long)V(r.Rows[0], "lostRevenue")!));
        Assert.Equal(17L, V(r.Rows[1], "idleNights"));
        Assert.All(r.Rows, x => Assert.Equal("red", x.Group));
    }

    [Fact]
    public async Task Ppm_compliance_pins_the_overdue_plan_and_the_repeat_failure()
    {
        var row = Assert.Single((await Run("rpt.rooms.ppm-compliance")).Rows);
        Assert.Equal(("AC", 1L, 0L, 1L, "1 : 3", 1L, "P1 4 h; P2 6 h; P3 3 h"), ((string)V(row, "kind")!, (long)V(row, "plansDue")!, (long)V(row, "doneOnTime")!, (long)V(row, "overdue")!, (string)V(row, "ratio")!, (long)V(row, "repeatFailures")!, (string)V(row, "mttr")!));
        Assert.Equal("red", row.Group);
    }

    [Fact]
    public async Task Utilities_pins_unit_cost_and_cost_per_occupied_night()
    {
        var r = await Run("rpt.rooms.utilities-per-room");
        var month = Assert.Single(r.Rows, x => x.Kind == ReportRowKind.Total);
        Assert.Equal((1000L, 58000L), ((long)V(month, "unitCost")!, (long)V(month, "costPerNight")!)); // 5,800 / 580 kWh; 5,800 / 10 occupied room-nights
        // MEAS2-F finding F-1: the divisor is every occupied night of the WHOLE month (10: the booked future nights of the in-house stays on 6 Nov too), not the nights to the To date (8, giving 72500).
        Assert.Equal("red", month.Group);
    }

    [Fact]
    public async Task Attrition_and_pulse_and_training_pin_their_red_rules()
    {
        var att = await Run("rpt.people.attrition");
        var hk = att.Rows.Single(x => (string)V(x, "dept")! == "Housekeeping");
        Assert.Equal((1L, "Better pay x1", 40.0, true), ((long)V(hk, "leavers")!, (string)V(hk, "exitReasons")!, (double)V(hk, "attritionPct")!, (bool)V(hk, "exception")!));
        var pulse = Assert.Single((await Run("rpt.people.staff-pulse")).Rows);
        Assert.Equal(("Housekeeping", 5L, 40L), ((string)V(pulse, "dept")!, (long)V(pulse, "responses")!, (long)V(pulse, "enps")!));
        var tr = await Run("rpt.people.training");
        Assert.Equal(10, tr.Rows.Count(x => ((string)V(x, "course")!).EndsWith("(missing)", StringComparison.Ordinal)));
        Assert.True((bool)V(tr.Rows.Single(x => (string)V(x, "course")! == "Fire safety"), "exception")!);
    }

    [Fact]
    public async Task Staff_productivity_pins_hours_per_room_night()
    {
        var r = await Run("rpt.people.staff-productivity");
        var fo = r.Rows.Single(x => (string)V(x, "dept")! == "Front Office");
        Assert.Equal((2L, "8.00", 1L), ((long)V(fo, "headcount")!, (string)V(fo, "hoursPerRoomNight")!, (long)V(fo, "lateMarks")!));
        Assert.Equal("16.00", V(r.Rows.Single(x => x.Kind == ReportRowKind.Total), "hoursPerRoomNight"));
    }

    [Fact]
    public async Task Agent_scorecard_pins_the_desk_clerk()
    {
        var r = await Run("rpt.people.agent-scorecard");
        var clerk = r.Rows.Single(x => (string)V(x, "user")! == "Clerk SM");
        Assert.Equal((4L, 1L, 1L, 20000L, 25000L, 5L), ((long)V(clerk, "checkins")!, (long)V(clerk, "checkouts")!, (long)V(clerk, "discountCount")!, (long)V(clerk, "discountAmount")!, (long)V(clerk, "cashVariance")!, (long)V(clerk, "regrets")!));
    }

    [Fact]
    public async Task Pickup_pace_pins_rooms_and_revenue_on_the_books()
    {
        // 16 live room-nights + the no-show night of SM-6 (a no-show is not cancelled, so PickupPaceReport.Nights keeps it on the books: finding F-2); the cancelled SM-7 is out.
        var r = await Run("rpt.revenue.pickup-pace");
        Assert.Equal(("2026-11", 17L, 4340000L, 1L), ((string)V(r.Rows[0], "month")!, (long)V(r.Rows[0], "rooms")!, (long)V(r.Rows[0], "revenue")!, (long)V(r.Rows[0], "pickup7")!));
        Assert.Equal((3L, 1200000L), ((long)V(r.Rows[1], "rooms")!, (long)V(r.Rows[1], "revenue")!));
    }

    [Fact]
    public async Task Overbooking_pins_the_walk_cost_and_rate_parity_the_competitor_rates()
    {
        var wash = await Run("rpt.revenue.overbooking-wash");
        var walked = Assert.Single(wash.Rows, x => (long)V(x, "walked")! > 0);
        Assert.Equal((1L, 150000L), ((long)V(walked, "walked")!, (long)V(walked, "walkCost")!));
        var par = await Run("rpt.revenue.rate-parity");
        Assert.Equal((200000L, 240000L, 220000L), ((long)V(par.Rows[0], "comp1")!, (long)V(par.Rows[0], "comp2")!, (long)V(par.Rows[0], "median")!));
        Assert.NotNull(V(par.Rows[0], "direct"));
    }

    [Fact]
    public async Task Restriction_effect_pins_the_revenue_of_the_restricted_nights()
    {
        var r = await Run("rpt.revenue.restriction-effect");
        Assert.Equal((200000L, 400000L, 2L), ((long)V(r.Rows[0], "revenue")!, (long)V(r.Rows[1], "revenue")!, (long)V(r.Rows[0], "refused")!));
    }

    [Fact]
    public async Task Displacement_and_demand_calendar_run_on_the_yield_defaults()
    {
        var disp = await Run("rpt.revenue.displacement");
        Assert.Equal(ReportRowKind.Total, disp.Rows[^1].Kind);
        var cal = await Run("rpt.revenue.demand-calendar");
        Assert.Equal(7, cal.Rows.Count);
        Assert.Contains("Festival", string.Join("|", cal.Rows.Select(x => (string)V(x, "event")!)), StringComparison.Ordinal);
    }
}
