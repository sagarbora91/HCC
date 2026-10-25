using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Domain;

public sealed class AccountingRulesTests
{
    private static readonly DateOnly D = new(2026, 10, 24);
    private static readonly IReadOnlyDictionary<string, string> Map = ChartOfAccounts.Effective(null);

    [Theory]
    [InlineData(1001, 501, 500)]
    [InlineData(-1001, -501, -500)]
    [InlineData(10000, 5000, 5000)]
    [InlineData(1, 1, 0)]
    [InlineData(0, 0, 0)]
    public void Gst_split_gives_cgst_the_odd_paisa_and_sums_to_the_tax(long tax, long cgst, long sgst)
    {
        Assert.Equal((cgst, sgst), GstSplit.Of(tax));
    }

    [Fact]
    public void Chart_has_gst_ledgers_and_suspense_is_never_a_mapping_target()
    {
        Assert.Equal("Output CGST", ChartOfAccounts.NameOf("2110"));
        Assert.Equal("Output SGST", ChartOfAccounts.NameOf("2120"));
        Assert.DoesNotContain(ChartOfAccounts.DefaultMapping.Values, v => v == ChartOfAccounts.Suspense);
        Assert.All(ChartOfAccounts.DefaultMapping.Values, v => Assert.NotNull(ChartOfAccounts.Find(v)));
        Assert.Null(ChartOfAccounts.Resolve(new Dictionary<string, string> { ["x:y"] = "9999" }, "x:y"));
        Assert.True(ChartOfAccounts.IsValidKey("expense:Kitchen gas"));
        Assert.False(ChartOfAccounts.IsValidKey("bogus:x"));
    }

    [Fact]
    public void Pay_run_journal_balances_when_gross_equals_net_plus_deductions_and_flags_otherwise()
    {
        var e = new PayrollPostedEvent(7, "2026-10", D, GrossPaise: 3_000_000, EmployerPfPaise: 180_000, EmployerEsicPaise: 97_500, EmployeePfPaise: 180_000,
            EmployeeEsicPaise: 22_500, ProfessionalTaxPaise: 20_000, AdvanceRecoveryPaise: 100_000, OtherDeductionsPaise: 0, NetPayablePaise: 2_677_500);
        var v = VoucherBuilder.Build(D, [AccountingEventMapper.PayRun(e)], Map).Single();
        Assert.Equal(VoucherType.Journal, v.Type);
        Assert.Equal("JRN/20261024/001", v.Number);
        Assert.True(v.Balanced);
        Assert.Equal(3_277_500, v.Debit);
        var byAcc = v.Entries.GroupBy(x => x.Account).ToDictionary(g => g.Key, g => (g.Sum(x => x.DebitPaise), g.Sum(x => x.CreditPaise)));
        Assert.Equal((3_000_000L, 0L), byAcc["5100"]);
        Assert.Equal((277_500L, 0L), byAcc["5110"]);
        Assert.Equal((0L, 360_000L), byAcc["2310"]);
        Assert.Equal((0L, 120_000L), byAcc["2320"]);
        Assert.Equal((0L, 2_677_500L), byAcc["2300"]);
        Assert.Equal((0L, 100_000L), byAcc["1200"]);

        var bad = VoucherBuilder.Build(D, [AccountingEventMapper.PayRun(e with { NetPayablePaise = 2_600_000 })], Map);
        Assert.Contains(VoucherBuilder.Exceptions(bad, bad, false, DayStatus.Closed), x => x.Kind == "unbalanced" && x.Severity == "error");
        var paid = VoucherBuilder.Build(D, [AccountingEventMapper.PayRunPaid(new PayrollPaidEvent(7, D, "Bank transfer", 2_677_500, "NEFT-DEMO"))], Map).Single();
        Assert.Equal([("2300", 2_677_500L, 0L), ("1030", 0L, 2_677_500L)], paid.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
    }

    [Fact]
    public void Outlet_bill_void_swaps_sides_and_vendor_credit_expense_is_a_journal()
    {
        var sale = new PosBillSettledEvent(11, "sale", D, "restaurant", "UPI", 105_000, 100_000, 2_500, 2_500, "DEMO-UPI");
        var vs = VoucherBuilder.Build(D, [AccountingEventMapper.PosBill(sale), AccountingEventMapper.PosBill(sale with { Kind = "void", TotalPaise = -105_000, NetPaise = -100_000, CgstPaise = -2_500, SgstPaise = -2_500 })], Map);
        Assert.Equal(["SAL/20261024/001", "SAL/20261024/002"], vs.Select(v => v.Number));
        Assert.Equal([("1010", 0L, 105_000L), ("4100", 100_000L, 0L), ("2110", 2_500L, 0L), ("2120", 2_500L, 0L)], vs[1].Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Equal(0, VoucherBuilder.TrialBalance(vs).Sum(r => Math.Abs(r.BalancePaise)));
        var exp = VoucherBuilder.Build(D, [AccountingEventMapper.Expense(new ExpensePostedEvent(5, D, "Supplies", "Vendor credit", 40_000, "Demo Traders", null))], Map).Single();
        Assert.Equal("JRN/20261024/001", exp.Number);
        Assert.Equal([("5000", 40_000L, 0L), ("2200", 0L, 40_000L)], exp.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
    }

    [Fact]
    public void Zero_sources_yield_no_voucher_and_zero_tax_entries_are_dropped()
    {
        var vs = VoucherBuilder.Build(D, [SourceRules.FolioLine("a", "service", D, 0, 0, 0, "zero"), SourceRules.FolioLine("b", "service", D, 50_000, 0, 0, "untaxed")], Map);
        var v = Assert.Single(vs);
        Assert.Equal(2, v.Entries.Count);
        Assert.Equal("SAL/20261024/001", v.Number);
    }

    [Fact]
    public void Exceptions_report_drift_open_day_and_pending_events()
    {
        var a = VoucherBuilder.Build(D, [SourceRules.FolioReceipt("r1", "payment", "Cash", D, 1000, "x")], Map);
        var b = VoucherBuilder.Build(D, [SourceRules.FolioReceipt("r1", "payment", "Cash", D, 1000, "x"), SourceRules.FolioReceipt("r2", "payment", "Cash", D, 5, "y")], Map);
        Assert.Contains(VoucherBuilder.Exceptions(a, b, locked: true, DayStatus.Locked), x => x.Kind == "drift" && x.Severity == "warning");
        Assert.DoesNotContain(VoucherBuilder.Exceptions(a, a, locked: true, DayStatus.Locked), x => x.Kind == "drift");
        Assert.Contains(VoucherBuilder.Exceptions(a, a, false, DayStatus.Open), x => x.Kind == "open-day" && x.Severity == "info");
        Assert.Contains(VoucherBuilder.Exceptions(a, a, false, DayStatus.Closed, pendingEvents: 2), x => x.Kind == "pending" && x.Severity == "error");
    }

    [Fact]
    public void Csv_guards_formula_cells_and_tally_range_is_at_most_31_days()
    {
        Assert.Equal("'=HYPERLINK(1)", VoucherExport.CsvSafe("=HYPERLINK(1)"));
        Assert.Equal("-5.00", VoucherExport.CsvSafe("-5.00"));
        Assert.Equal("\"a,b\"", VoucherExport.CsvCell("a,b"));
        Assert.Null(VoucherExport.TallyRangeError(D, D.AddDays(30)));
        Assert.NotNull(VoucherExport.TallyRangeError(D, D.AddDays(31)));
        Assert.NotNull(VoucherExport.TallyRangeError(D, D.AddDays(-1)));
        Assert.Equal("a&amp;b &lt;c&gt; &quot;", VoucherExport.XmlEscape("a&b <c> \"\u0001"));
    }

    [Fact]
    public void Reconciliation_compares_ledger_movements_with_operational_figures()
    {
        var vs = VoucherBuilder.Build(D,
        [
            SourceRules.FolioLine("c1", "room", D, 200_000, 5_000, 5_000, "n"),
            SourceRules.FolioLine("c2", "correction", D, -10_000, -250, -250, "n"),
            SourceRules.FolioReceipt("r1", "payment", "UPI", D, 100_000, "n"),
            SourceRules.FolioReceipt("r2", "refund", "UPI", D, 5_000, "n"),
            SourceRules.FolioReceipt("r3", "deposit", "Cash", D, 50_000, "n"),
        ], Map);
        var ok = Reconciliation.Check(vs, Map, new OperationalFigures(200_000, 0, 0, -10_000, 9_500, 95_000, 50_000, 0));
        Assert.All(ok, c => Assert.True(c.Ok, c.Id));
        var bad = Reconciliation.Check(vs, Map, new OperationalFigures(200_000, 0, 0, -10_000, 9_500, 96_000, 50_000, 0));
        Assert.False(bad.Single(c => c.Id == "collections").Ok);
    }
}
