using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.MeasDaily;

namespace Hcc.Meas.Tests;

/// <summary>[F12-RPT-DAILY] Boundary tests of every exception rule of the daily-control pack (twins of tests/stream-meas-rpt-daily.mjs) and the report ids / switch keys.</summary>
public sealed class MeasDailyRulesTests
{
    [Fact]
    public void Flash_exactly_ten_percent_below_target_is_not_red_one_unit_below_is_red()
    {
        Assert.False(MeasDailyRules.FlashRed(90, 100));
        Assert.True(MeasDailyRules.FlashRed(89, 100));
        Assert.False(MeasDailyRules.FlashRed(0, null));
        Assert.False(MeasDailyRules.FlashRed(0, 0));
    }

    [Fact]
    public void Regrets_more_than_three_price_regrets_on_a_night_under_sixty_percent_is_red()
    {
        Assert.True(MeasDailyRules.RegretsRed(4, 5, 10));
        Assert.False(MeasDailyRules.RegretsRed(3, 5, 10));
        Assert.False(MeasDailyRules.RegretsRed(4, 6, 10));
        Assert.False(MeasDailyRules.RegretsRed(4, 0, 0));
    }

    [Fact]
    public void Rate_at_the_agreed_floor_passes_one_paisa_below_is_red()
    {
        Assert.Equal(8500, MeasDailyRules.AgreedFloor(10000, 1500));
        Assert.False(MeasDailyRules.RateBelowFloor(10000, 8500, 1500));
        Assert.True(MeasDailyRules.RateBelowFloor(10000, 8499, 1500));
        Assert.Equal(8501, MeasDailyRules.AgreedFloor(10001, 1500));
    }

    [Fact]
    public void Discrepancy_two_hours_is_not_red_two_hours_one_minute_is_red()
    {
        Assert.False(MeasDailyRules.DiscrepancyRed(120));
        Assert.True(MeasDailyRules.DiscrepancyRed(121));
    }

    [Fact]
    public void High_balance_walk_in_threshold_company_limit_and_age()
    {
        Assert.Empty(MeasDailyRules.HighBalanceReasons(false, 500000, 0, 0, 500000, 0));
        Assert.Single(MeasDailyRules.HighBalanceReasons(false, 500001, 0, 0, 500000, 0));
        Assert.Empty(MeasDailyRules.HighBalanceReasons(true, 150000, 50000, 100000, 500000, 1));
        Assert.Single(MeasDailyRules.HighBalanceReasons(true, 150001, 50000, 100000, 500000, 1));
        Assert.Empty(MeasDailyRules.HighBalanceReasons(false, 100, 0, 0, 500000, 2));
        Assert.Single(MeasDailyRules.HighBalanceReasons(false, 100, 0, 0, 500000, 3));
    }

    [Fact]
    public void Out_of_order_seven_days_is_not_red_eight_days_without_a_work_order_is_red()
    {
        Assert.False(MeasDailyRules.OooRed("OOO", 7, ""));
        Assert.True(MeasDailyRules.OooRed("OOO", 8, ""));
        Assert.False(MeasDailyRules.OooRed("OOO", 8, "WO-1"));
        Assert.False(MeasDailyRules.OooRed("OOS", 30, ""));
    }

    [Fact]
    public void No_show_rate_five_percent_is_not_red_above_is_red()
    {
        Assert.False(MeasDailyRules.NoShowRateRed(5, 100));
        Assert.True(MeasDailyRules.NoShowRateRed(6, 100));
        Assert.False(MeasDailyRules.NoShowRateRed(0, 0));
    }

    [Fact]
    public void Allowance_needs_a_reason_and_an_approver_and_respects_the_daily_cap()
    {
        Assert.Empty(MeasDailyRules.AllowanceReasons("promo", "manager", false));
        Assert.Equal(["No reason", "No approver"], MeasDailyRules.AllowanceReasons(" ", "", false));
        Assert.Equal(["User above the daily cap"], MeasDailyRules.AllowanceReasons("promo", "manager", true));
    }

    [Fact]
    public void Cashier_variance_limit_is_exclusive_and_the_third_variance_of_the_month_is_red()
    {
        Assert.Empty(MeasDailyRules.CashierReasons(10000, 10000, 1));
        Assert.Single(MeasDailyRules.CashierReasons(10001, 10000, 1));
        Assert.Single(MeasDailyRules.CashierReasons(-10001, 10000, 1));
        Assert.Empty(MeasDailyRules.CashierReasons(100, 10000, 2));
        Assert.Single(MeasDailyRules.CashierReasons(100, 10000, 3));
        Assert.Empty(MeasDailyRules.CashierReasons(0, 10000, 3));
    }

    [Fact]
    public void The_nine_reports_carry_the_frozen_switch_keys_and_every_key_is_a_measurement_key()
    {
        var expected = new Dictionary<string, string>
        {
            ["rpt.guests.manager-flash"] = "meas.daily.flash", ["rpt.guests.regrets-denials"] = "meas.daily.regrets", ["rpt.guests.rate-variance"] = "meas.daily.rate-variance",
            ["rpt.rooms.room-discrepancy"] = "meas.daily.discrepancy", ["rpt.money.high-balance"] = "meas.daily.high-balance", ["rpt.money.allowances-voids"] = "meas.daily.adjustments",
            ["rpt.guests.no-show-cancel"] = "meas.daily.no-show", ["rpt.rooms.inventory-exceptions"] = "meas.daily.inventory", ["rpt.money.cashier-variance"] = "meas.daily.cashier",
        };
        IReportSelector[] all =
        [
            new ManagerFlashReport(null!), new RegretsDenialsReport(null!), new RateVarianceReport(null!), new RoomDiscrepancyReport(null!), new HighBalanceReport(null!),
            new AllowancesVoidsReport(null!), new NoShowCancelReport(null!), new InventoryExceptionsReport(null!), new CashierVarianceReport(null!),
        ];
        Assert.Equal(expected.Count, all.Length);
        foreach (var r in all)
        {
            Assert.Equal(expected[r.Definition.Id], ((IMeasuredReport)r).MeasKey);
            Assert.NotNull(MeasurementCatalogue.Item(((IMeasuredReport)r).MeasKey));
            Assert.Equal("exception", r.Definition.Columns[^1].Key);
        }
    }

    // ── F12-MEAS2-A (twins of the [F12-MEAS2-A] tests in tests/stream-meas-rpt-daily.mjs) ──

    [Fact]
    public void Meas2A_L2_flash_money_division_is_integer_half_away_from_zero()
    {
        Assert.Equal([3L, -3L, 2L, 0L, 0L], new[] { ManagerFlashReport.DivRound(5, 2), ManagerFlashReport.DivRound(-5, 2), ManagerFlashReport.DivRound(7, 3), ManagerFlashReport.DivRound(1, 0), ManagerFlashReport.DivRound(1, 3) });
        const long big = 1L << 40;                                                           // 2^40 paise: exact, no double on the way
        Assert.Equal(big / 3,ManagerFlashReport.DivRound(big, 3));                       // 2^40 mod 3 = 1 → rounds down
        Assert.Equal(big + 1, ManagerFlashReport.DivRound(big * 2 + 1, 2));                   // exact half → away from zero
    }

    [Fact]
    public void Meas2A_M5_adr_divides_by_charged_nights_and_is_blank_without_one()
    {
        // 3 paid nights of 3,000 and 1 zero-rate night: ADR = 9,000 / 3, not / 4
        Assert.Equal(300000L, ManagerFlashReport.Adr(900000, 3));
        Assert.Null(ManagerFlashReport.Adr(0, 0));
        Assert.Contains("charged nights (nights with a room charge above zero)", ManagerFlashReport.BasisText, StringComparison.Ordinal);
        Assert.Equal(ManagerFlashReport.BasisText, new ManagerFlashReport(null!).Definition.DateBasis);
    }

    [Fact]
    public void Meas2A_L8_last_year_is_the_same_calendar_date_and_29_february_maps_to_28()
    {
        Assert.Equal(new DateOnly(2027, 3, 1), ManagerFlashReport.LastYear(new DateOnly(2028, 3, 1)));
        Assert.Equal(new DateOnly(2027, 2, 28), ManagerFlashReport.LastYear(new DateOnly(2028, 2, 29)));
        Assert.Equal(new DateOnly(2025, 11, 7), ManagerFlashReport.LastYear(new DateOnly(2026, 11, 7)));
        Assert.Contains("same calendar date", ManagerFlashReport.BasisText, StringComparison.Ordinal);
    }

    [Fact]
    public void Meas2A_L3_high_balance_text_keeps_paise()
    {
        Assert.Equal("5,000.50", MeasDailyRules.Rupees(500050));
        Assert.Equal("5,000", MeasDailyRules.Rupees(500000));
        Assert.Equal("10,00,000", MeasDailyRules.Rupees(100000000));
        Assert.Equal(["Walk-in balance above 5,000.50 rupees"], MeasDailyRules.HighBalanceReasons(false, 500051, 0, 0, 500050, 0));
    }

    [Fact]
    public void Meas2A_N4_ooo_with_stay_start_is_local_and_the_later_of_offline_and_check_in()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        // offline set 09:00 UTC = 14:30 local, check-in 04:30 UTC = 10:00 local → 14:30 local (not the UTC value)
        Assert.Equal(new DateTime(2026, 11, 7, 14, 30, 0), MeasDailyRoomSince(null, new DateTime(2026, 11, 7, 9, 0, 0), new DateTime(2026, 11, 7, 4, 30, 0), ist));
        // block from 6 Nov (local midnight) before a check-in on 7 Nov 10:00 local → the check-in
        Assert.Equal(new DateTime(2026, 11, 7, 10, 0, 0), MeasDailyRoomSince(new DateTime(2026, 11, 6), null, new DateTime(2026, 11, 7, 4, 30, 0), ist));
        // block from 8 Nov after the check-in → 8 Nov 00:00 local; nothing known → null
        Assert.Equal(new DateTime(2026, 11, 8, 0, 0, 0), MeasDailyRoomSince(new DateTime(2026, 11, 8), null, new DateTime(2026, 11, 7, 4, 30, 0), ist));
        Assert.Null(MeasDailyRoomSince(null, null, null, ist));
    }

    private static DateTime? MeasDailyRoomSince(DateTime? block, DateTime? setUtc, DateTime? checkedInUtc, TimeZoneInfo zone) => RoomDiscrepancyReport.OooSince(block, setUtc, checkedInUtc, zone);
}
