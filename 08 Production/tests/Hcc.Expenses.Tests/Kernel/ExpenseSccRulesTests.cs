using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;

namespace Hcc.Expenses.Tests.Kernel;

/// <summary>
/// DEC-120 · SCC expense features: the server rules (<see cref="ExpenseScc"/>) against SCC's own functions transcribed from
/// <c>expense\index.html</c> (scratchpad <c>scc\expense.js</c>; rupee doubles, JavaScript Math.round), the same vectors as the prototype test
/// <c>03 Prototype/tests/stream-expenses-scc.mjs</c>. Names say which SCC function is the oracle.
/// </summary>
public class ExpenseSccRulesTests
{
    // ── SCC, transcribed (JS numbers are doubles; Math.round = nearest, half up) ──
    private static double JsRound(double x)
    {
        var f = Math.Floor(x);
        return x - f >= 0.5 ? f + 1 : f;
    }

    private static double SccPrefill(double a, double r) => a > 0 && r > 0 ? JsRound(a * r / (100 + r) * 100) / 100 : 0;  // prefillGst 445-448

    private static (int Pct, string Status) SccBudgetRow(double bg, double ac)                                          // renderBudget 1282, 1287
    {
        var pct = bg != 0 ? (int)JsRound(ac / bg * 100) : 0;
        return (pct, bg == 0 ? "no budget" : pct > 100 ? "over" : pct >= 85 ? "near limit" : "ok");
    }

    private static string SccPettyStatus(double fl, double spent) => fl - spent < 0.15 * fl && fl != 0 ? "Replenish soon" : "Healthy"; // renderPetty 1213

    private static double SccPay(double outstanding, double a) => a > outstanding + 0.5 ? outstanding : a;                // payReceivable 1587

    private static string SccBucket(int days) => days <= 30 ? "0–30" : days <= 60 ? "31–60" : "60+";                      // udBucket 1560

    private static readonly DateOnly D = new(2026, 10, 24);

    [Fact]
    public void Gst_prefill_equals_SCC_prefillGst_on_every_grid_point_and_1180_at_18_percent_is_180()
    {
        Assert.Equal(18_000, ExpenseScc.GstPrefill(118_000, 1_800));
        var n = 0;
        var diff = new List<string>();
        for (long a = 1; a <= 200_000; a += a < 2_000 ? 1 : a < 20_000 ? 37 : 997)
        {
            foreach (var bps in new[] { 25, 100, 250, 300, 500, 1200, 1800, 2800, 4000, 10000 })
            {
                n++;
                var want = (long)JsRound(SccPrefill(a / 100d, bps / 100d) * 100);
                if (ExpenseScc.GstPrefill(a, bps) != want) diff.Add($"{a}@{bps}");
            }
        }

        Assert.True(n > 20_000);
        Assert.Empty(diff);
        // SCC's floating-point halves (the prototype oracle): 29 paise at 100 % → 14, 70 paise at 12 % → 7.
        Assert.Equal((14L, 7L), (ExpenseScc.GstPrefill(29, 10_000), ExpenseScc.GstPrefill(70, 1_200)));
        Assert.Equal(0, ExpenseScc.GstPrefill(0, 1_800));
        Assert.Equal(0, ExpenseScc.GstPrefill(118_000, 0));
    }

    [Fact]
    public void Gst_on_the_bill_is_kept_only_when_positive_and_more_than_the_amount_asks_save_anyway()
    {
        Assert.Equal(((int?)1800, (long?)18_000), ExpenseScc.Gst(1800, 18_000, 118_000, false));
        Assert.Equal(((int?)null, (long?)null), ExpenseScc.Gst(0, 0, 118_000, false));
        var ex = Assert.Throws<HccException>(() => ExpenseScc.Gst(1800, 20_000, 10_000, false));
        Assert.Equal("GST amount (₹200) is more than the entry amount (₹100). Save anyway?", ex.Message);
        Assert.Equal(((int?)1800, (long?)20_000), ExpenseScc.Gst(1800, 20_000, 10_000, true));
        Assert.Throws<HccException>(() => ExpenseScc.Gst(10_001, null, 100, false));
    }

    [Fact]
    public void Budget_tab_status_equals_SCC_renderBudget_near_from_85_over_above_100()
    {
        var map = new Dictionary<string, string> { ["no budget"] = "none", ["over"] = "over", ["near limit"] = "near", ["ok"] = "ok" };
        foreach (var bg in new long[] { 0, 1, 99, 100, 1000, 4999, 10000, 333333 })
        foreach (var p in new[] { 0, 1, 50, 84, 84.4, 84.5, 85, 89.5, 90, 99.4, 99.5, 100, 100.4, 100.5, 101, 150, 10000 })
        {
            var ac = (long)JsRound(bg * p / 100);
            var w = SccBudgetRow(bg, ac);
            Assert.Equal((w.Pct, map[w.Status]), ExpenseScc.BudgetRow(bg, ac));
        }

        Assert.Equal("near", ExpenseScc.BudgetRow(10_000, 10_000).Status); // SCC's 100 % edge: "near limit" on the tab, an error alert (owner question)
    }

    [Fact]
    public void Budget_alert_equals_SCC_budgetAlertFor_business_month_only_warn_at_90_error_at_100()
    {
        Assert.Null(ExpenseScc.Alert("Supplies", D, D, 100_000, 89_000));
        Assert.Equal(new BudgetAlert("Supplies now 90% of Oct budget", "warn", 90), ExpenseScc.Alert("Supplies", D, D, 100_000, 89_500));
        Assert.Equal(new BudgetAlert("Supplies now 100% of Oct budget", "err", 100), ExpenseScc.Alert("Supplies", D, D, 100_000, 99_500));
        Assert.Null(ExpenseScc.Alert("Supplies", new DateOnly(2026, 9, 30), D, 100_000, 99_500));
        Assert.Null(ExpenseScc.Alert("Supplies", D, D, 0, 99_500));
        Assert.Equal("Supplies would be 95% of Oct budget once approved", ExpenseScc.Alert("Supplies", D, D, 100_000, 0, 95_000)!.Message);
    }

    [Fact]
    public void Copy_last_month_equals_SCC_copyBudgets_heads_and_messages()
    {
        Assert.Equal("No budgets found for 2026-09", Assert.Throws<HccException>(() => ExpenseScc.CopyHeads("2026-10", new Dictionary<string, long>(), new Dictionary<string, long>())).Message);
        Assert.Equal("Every head already has a budget for 2026-10", Assert.Throws<HccException>(() =>
            ExpenseScc.CopyHeads("2026-10", new Dictionary<string, long> { ["Supplies"] = 500 }, new Dictionary<string, long> { ["Supplies"] = 100 })).Message);
        Assert.Equal(["Laundry"], ExpenseScc.CopyHeads("2026-10", new Dictionary<string, long> { ["Supplies"] = 500, ["Laundry"] = 200 }, new Dictionary<string, long> { ["Supplies"] = 100 }));
        Assert.Equal(["Laundry", "Utilities"], ExpenseScc.CopyHeads("2026-10", new Dictionary<string, long> { ["Laundry"] = 1, ["Utilities"] = 3 }, new Dictionary<string, long> { ["Laundry"] = 0 }));
        Assert.Equal("2025-12", ExpenseScc.PrevMonth("2026-01"));
    }

    [Theory]
    [InlineData(31, "2027-02-27", false)]
    [InlineData(31, "2027-02-28", true)]   // rent on day 31 is due on the last day of February (SCC min(dueDay, days in month))
    [InlineData(31, "2028-02-28", false)]
    [InlineData(31, "2028-02-29", true)]
    [InlineData(5, "2026-10-04", false)]
    [InlineData(5, "2026-10-05", true)]
    [InlineData(31, "2026-04-30", true)]
    public void Recurring_due_equals_SCC_recurringDue(int dueDay, string today, bool due)
    {
        var d = DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(due, ExpenseScc.RecurringDue(true, dueDay, 0, d));
        Assert.False(ExpenseScc.RecurringDue(false, dueDay, 0, d));
        Assert.False(ExpenseScc.RecurringDue(true, dueDay, 1, d));
    }

    [Fact]
    public void Recurring_template_validation_follows_SCC()
    {
        Assert.Equal("Template name is required", Assert.Throws<HccException>(() => ExpenseScc.Template(" ", 100, 3)).Message);
        Assert.Equal("Enter a typical amount greater than 0", Assert.Throws<HccException>(() => ExpenseScc.Template("Rent", 0, 3)).Message);
        Assert.Equal("Due day must be between 1 and 31", Assert.Throws<HccException>(() => ExpenseScc.Template("Rent", 100, 32)).Message);
        Assert.Equal("rec:7:2026-10", ExpenseScc.RecRef(7, "2026-10"));
        Assert.Equal("Shop rent — 2026-10", ExpenseScc.RecurringDescription("Shop rent", "2026-10"));
        Assert.Equal("Already posted for 2026-10 (1 entry · ₹24,000). Confirming will post ANOTHER entry for this month.", ExpenseScc.AlreadyPostedMessage("2026-10", 1, new Paise(2_400_000)));
    }

    [Fact]
    public void Petty_status_equals_SCC_renderPetty_replenish_below_15_percent()
    {
        foreach (var fl in new[] { 0, 1000, 500000, 333333 })
        foreach (var spent in new[] { 0, 1, (int)Math.Floor(fl * 0.85), (int)Math.Ceiling(fl * 0.85), fl, fl + 1 })
            Assert.Equal(SccPettyStatus(fl, spent), ExpenseScc.PettyStatus(fl * 100L, spent * 100L));
        Assert.Equal("Petty: sundry", ExpenseScc.PettyDescription(" "));
        Assert.Equal("Petty: Tea and snacks", ExpenseScc.PettyDescription("Tea and snacks"));
    }

    [Fact]
    public void Udhaar_equals_SCC_buckets_overpay_tolerance_mobile_and_reminder_with_the_hotel_name()
    {
        foreach (var days in new[] { 0, 1, 30, 31, 59, 60, 61, 200 }) Assert.Equal(SccBucket(days), ExpenseScc.UdhaarBucket(D.AddDays(-days), D));
        // Exact binary rupee values, so SCC's float arithmetic is exact too (HCC is exact paise everywhere).
        foreach (var (outstanding, pay) in new (long, long)[] { (100_000, 100_000), (100_000, 100_050), (100_000, 100_075), (50, 100), (50, 125), (25, 75), (25, 100) })
            Assert.Equal((long)(SccPay(outstanding / 100d, pay / 100d) * 100), ExpenseScc.UdhaarPayment(outstanding, pay).Amount);
        Assert.Equal(0, ExpenseScc.UdhaarOut(100_000, 120_000));
        Assert.Equal("9876543210", ExpenseScc.UdhaarMobile("+91 98765-43210"));
        Assert.Null(ExpenseScc.UdhaarMobile("12345"));
        Assert.Equal("Namaste Mohan,\nA gentle reminder from HCC Demo Residency regarding your pending balance of ₹500 (due 2026-10-20).\nKindly clear it at your convenience. Thank you.",
            ExpenseScc.UdhaarReminder("HCC Demo Residency", "Mohan", 50_000, new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public void Expense_date_never_after_the_business_date_never_a_closed_day_reopened_day_allowed_except_for_drawer_cash()
    {
        Assert.Null(ExpenseScc.DateRefusal(D, D, false, false, false, true));
        Assert.Equal(HccErrorCode.VALIDATION, ExpenseScc.DateRefusal(D, D.AddDays(1), false, false, false, false)!.Code);
        Assert.Equal(HccErrorCode.DAY_CLOSED, ExpenseScc.DateRefusal(D, D, true, false, false, false)!.Code);
        Assert.Equal(HccErrorCode.DAY_CLOSED, ExpenseScc.DateRefusal(D, D.AddDays(-1), false, false, false, false)!.Code);
        Assert.Null(ExpenseScc.DateRefusal(D, D.AddDays(-1), false, true, false, false));                       // the Owner-reopened day
        Assert.Equal(HccErrorCode.VALIDATION, ExpenseScc.DateRefusal(D, D.AddDays(-1), false, true, false, true)!.Code); // drawer cash: today only
        Assert.Equal(HccErrorCode.DAY_CLOSED, ExpenseScc.DateRefusal(D, D.AddDays(-1), false, true, true, false)!.Code); // locked cash book stays locked
    }

    [Fact]
    public void Month_lock_refuses_a_future_month_and_the_stale_rule_reads_the_stored_flag_or_a_later_posting()
    {
        Assert.Equal("2026-10", ExpenseScc.LockableMonth("2026-10", D));
        Assert.Throws<HccException>(() => ExpenseScc.LockableMonth("2026-11", D));
        Assert.Throws<HccException>(() => ExpenseScc.LockableMonth("2026-13", D));
        var gen = new DateTime(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);
        LedgerRow Row(long id, string status, DateTime posted) => new(id, D, "Supplies", "UPI", "V", "x", 100, null, null, status, null, "u", null, null, false, gen.AddHours(-5), posted);
        Assert.False(ExpenseScc.Stale(false, gen, [Row(1, "Posted", gen.AddMinutes(-1))], "2026-10"));
        Assert.True(ExpenseScc.Stale(false, gen, [Row(1, "Posted", gen.AddMinutes(1))], "2026-10"));
        Assert.False(ExpenseScc.Stale(false, gen, [Row(1, "Rejected", gen.AddMinutes(1))], "2026-10"));
        Assert.True(ExpenseScc.Stale(true, gen, [], "2026-10"));
    }

    [Fact]
    public void Ledger_and_month_CSV_have_SCCs_columns_quoting_and_file_names_plus_HCC_ref_and_status()
    {
        var at = new DateTime(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            new LedgerRow(7, D, "Maintenance", "UPI", "=Hardware", "Cement, \"grade 53\"", 118_050, null, null, "Posted", null, "Riya", 18_008, 1800, false, at, at),
            new LedgerRow(5, D, "Supplies", "UPI", "Morning Supplies", null, 85_000, null, null, "Void", "Duplicate entry — entered twice", "Riya", null, null, false, at, null),
            new LedgerRow(9, D.AddDays(-30), "Petty cash", "Cash", "", "Petty: sundry", 5_000, "petty", null, "Posted", null, "Asha", null, null, false, at, at),
        };
        var l = ExpenseScc.LedgerCsv("HCC Demo Residency", D, rows);
        var lines = l.Csv.Split('\n');
        Assert.Equal(("ledger_2026-10-24.csv", 3), (l.FileName, l.Rows));
        // DEC-135: HCC's ITC column after Status (allowed / disallowed for a bill with GST, empty without).
        Assert.Equal("Date,Firm,Type,Category,Mode,Vendor,Description,Amount,Source,Void,VoidReason,CreatedBy,Store,GSTAmt,GSTRate%,Ref,Status,ITC", lines[0]);
        Assert.Equal("2026-09-24,HCC Demo Residency,expense,Petty cash,Cash,,Petty: sundry,50,petty,,,Asha,Unassigned,,,EXP-9,Posted,", lines[1]);
        Assert.Equal("2026-10-24,HCC Demo Residency,expense,Supplies,UPI,Morning Supplies,Morning Supplies,850,manual,Y,Duplicate entry — entered twice,Riya,Unassigned,,,EXP-5,Void,", lines[2]);
        Assert.Equal("2026-10-24,HCC Demo Residency,expense,Maintenance,UPI,'=Hardware,\"Cement, \"\"grade 53\"\"\",1180.5,manual,,,Riya,Unassigned,180.08,18,EXP-7,Posted,disallowed", lines[3]);
        var m = ExpenseScc.MonthCsv("HCC Demo Residency", "2026-10", rows);
        Assert.Equal(("expense_month_2026-10.csv", 1), (m.FileName, m.Rows));
        Assert.Equal("Date,Firm,Type,Category,Mode,Vendor,Description,Amount,Source,GSTAmt,GSTRate%,Ref,Status,ITC", m.Csv.Split('\n')[0]);
        Assert.Equal("2026-10-24,HCC Demo Residency,expense,Maintenance,UPI,'=Hardware,\"Cement, \"\"grade 53\"\"\",1180.5,manual,180.08,18,EXP-7,Posted,disallowed", m.Csv.Split('\n')[1]);
        var s = ExpenseScc.Summary(rows, "2026-10");
        Assert.Equal((118_050L, 1, 18_008L), (s.ExpensePaise, s.Count, s.GstPaidPaise));
    }

    [Fact]
    public void Masters_check_the_category_vendor_and_approval_limit_server_gaps()
    {
        var cats = new[] { new ExpenseCategoryRow("UTIL", "Utilities", true, true, null), new ExpenseCategoryRow("PETTY", "Petty cash", true, false, 100_000),
            new ExpenseCategoryRow("OLD", "Old head", false, false, null) };
        Assert.Equal("Choose a category from the list (Master configuration › Money and tax › Expense categories).",
            Assert.Throws<HccException>(() => ExpenseMasters.Category(cats, "Gas")).Message);                    // unknown text refused (was accepted)
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => ExpenseMasters.Category(cats, "Old head")).Code); // retired
        Assert.Equal("Utilities", ExpenseMasters.Category(cats, "util").Name);
        Assert.Equal("Utilities expenses need the bill / receipt number.", Assert.Throws<HccException>(() => ExpenseMasters.EnsureBill(cats[0], " ")).Message);
        Assert.Equal((100_000L, 500_000L), (ExpenseMasters.ApprovalLimit(cats[1], 500_000), ExpenseMasters.ApprovalLimit(cats[0], 500_000)));
        var vendors = new[] { new ExpenseVendorRow(1, "V-0001", "Fresh Linen Services", true), new ExpenseVendorRow(2, "V-0002", "Gone Traders", false) };
        Assert.Equal(("Fresh Linen Services", false), ExpenseMasters.Vendor(vendors, "fresh linen services"));
        Assert.Equal(("Brand New Shop", true), ExpenseMasters.Vendor(vendors, " Brand New Shop "));
        Assert.Equal((string.Empty, false), ExpenseMasters.Vendor(vendors, " "));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => ExpenseMasters.Vendor(vendors, "Gone Traders")).Code);
        Assert.Equal("V-BRAND-NEW-SHOP", ExpenseMasters.VendorCode("Brand New Shop", new HashSet<string>()));
        Assert.Equal("V-BRAND-NEW-SHOP-2", ExpenseMasters.VendorCode("Brand New Shop", new HashSet<string> { "V-BRAND-NEW-SHOP" }));
        // The six SCC void reasons.
        Assert.Equal(["Cash shortage correction", "Duplicate entry", "Missed receipt", "Wrong payment mode", "Voucher correction", "Other"], ExpenseScc.VoidReasons);
    }
}
