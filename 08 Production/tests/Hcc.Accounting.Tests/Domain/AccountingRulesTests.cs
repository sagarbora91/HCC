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
    public void Wave_B1_bank_deposit_rejection_and_owner_drawing_vouchers_balance_on_1030_3000_and_1000()
    {
        // DEC-073: ₹6,000 banked = Dr 1030 Bank account 6,000 / Cr 1000 Cash in hand 6,000 (Journal); its rejection is the exact reverse; a ₹1,500
        // Owner drawing = Dr 3000 Owner's drawings / Cr 1000 (Payment). Net on 1000: −6,000 + 6,000 − 1,500 = −₹1,500.
        var bank = new CashBankingEvent(11, 3, 42, D, 600_000, "SBI current ••4321", "DS-1001");
        var vs = VoucherBuilder.Build(D, [AccountingEventMapper.Banking(bank, rejected: false), AccountingEventMapper.Banking(bank with { Reason = "Wrong slip" }, rejected: true),
            AccountingEventMapper.OwnerDrawing(new OwnerDrawingEvent(12, 3, 42, D, 150_000, "Household"))], Map);
        Assert.All(vs, v => Assert.True(v.Balanced, v.Number));
        var dep = vs.Single(v => v.SourceKey == "banking:11");
        Assert.Equal((VoucherType.Journal, "closing", "bank-deposit"), (dep.Type, dep.SourceKind, dep.Sub));
        Assert.Equal([("1030", 600_000L, 0L), ("1000", 0L, 600_000L)], dep.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Equal([("1000", 600_000L, 0L), ("1030", 0L, 600_000L)], vs.Single(v => v.SourceKey == "banking:11/rejected").Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        var drawing = vs.Single(v => v.SourceKey == "drawing:12");
        Assert.Equal(VoucherType.Payment, drawing.Type);
        Assert.Equal([("3000", 150_000L, 0L), ("1000", 0L, 150_000L)], drawing.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Equal(-150_000, vs.SelectMany(v => v.Entries).Where(x => x.Account == "1000").Sum(x => x.DebitPaise - x.CreditPaise));
        Assert.Equal("Owner's drawings", ChartOfAccounts.NameOf("3000"));
        Assert.Contains(AccountingEvents.CashBanked, AccountingEvents.Consumed);
        Assert.Contains(AccountingEvents.CashBankingRejected, AccountingEvents.Consumed);
        Assert.Contains(AccountingEvents.OwnerDrawingRecorded, AccountingEvents.Consumed);
    }

    [Fact]
    public void Outlet_bill_void_swaps_sides_and_vendor_credit_expense_is_a_journal()
    {
        var sale = new PosBillSettledEvent(11, "sale", D, "restaurant", "UPI", 105_000, 100_000, 2_500, 2_500, "DEMO-UPI");
        var vs = VoucherBuilder.Build(D, [AccountingEventMapper.PosBill(sale), AccountingEventMapper.PosBill(sale with { Kind = "void", TotalPaise = -105_000, NetPaise = -100_000, CgstPaise = -2_500, SgstPaise = -2_500 })], Map);
        Assert.Equal(["SAL/20261024/001", "SAL/20261024/002"], vs.Select(v => v.Number));
        Assert.Equal([("1010", 0L, 105_000L), ("4200", 100_000L, 0L), // ACC-1: outlet sales → 4200
            ("2110", 2_500L, 0L), ("2120", 2_500L, 0L)], vs[1].Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Equal(0, VoucherBuilder.TrialBalance(vs).Sum(r => Math.Abs(r.BalancePaise)));
        var exp = VoucherBuilder.Build(D, [AccountingEventMapper.Expense(new ExpensePostedEvent(5, D, "Supplies", "Vendor credit", 40_000, "Demo Traders", null))], Map).Single();
        Assert.Equal("PUR/20261024/001", exp.Number); // ACC-2 contract §3.1: a bill on vendor credit is a Purchase voucher (was Journal)
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
    public void Wave_C3_parked_events_are_stuck_see_system_health_not_still_being_processed()
    {
        var a = VoucherBuilder.Build(D, [SourceRules.FolioReceipt("r1", "payment", "Cash", D, 1000, "x")], Map);
        var parked = VoucherBuilder.Exceptions(a, a, false, DayStatus.Closed, pendingEvents: 0, parkedEvents: 1);
        var stuck = Assert.Single(parked, x => x.Severity == "error");
        Assert.Equal(("stuck", "stuck", "Accounting events stuck — see System health"), (stuck.Id, stuck.Kind, stuck.Title));
        Assert.StartsWith("1 posting event(s) for this property could not be booked after repeated attempts", stuck.Detail, StringComparison.Ordinal);
        Assert.Contains("Admin & Setup › System health", stuck.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(parked, x => x.Kind == "pending");
        // Both kinds at once: the stuck one first (it is the one a person must act on); a locked date shows neither.
        Assert.Equal(["stuck", "pending"], VoucherBuilder.Exceptions(a, a, false, DayStatus.Closed, 3, 2).Where(x => x.Severity == "error").Select(x => x.Kind).ToArray());
        Assert.DoesNotContain(VoucherBuilder.Exceptions(a, a, true, DayStatus.Locked, 3, 2), x => x.Kind is "stuck" or "pending");
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
    public void A06_08_voucher_csv_neutralises_formula_narrations_quotes_line_breaks_and_keeps_amounts_plain()
    {
        var v = new Voucher("RV-1", D, VoucherType.Receipt, "=HYPERLINK(\"http://x.test\",\"y\") from @guest", "-src-1", "payment", "",
            [new VoucherEntry("1000", 1_234_550, 0), new VoucherEntry("1100", 0, 1_234_550)], []);
        var cr = v with { Number = "RV-2", Narration = "Room 101\r=cmd|' /C calc'!A0", SourceKey = "+src-2" };
        var rows = VoucherExport.Csv([v, cr]).Split("\r\n");
        Assert.Equal(VoucherExport.CsvHeader, rows[0]);
        Assert.EndsWith(",12345.50,,\"'=HYPERLINK(\"\"http://x.test\"\",\"\"y\"\") from @guest\",'-src-1", rows[1], StringComparison.Ordinal);
        Assert.EndsWith(",,12345.50,\"'=HYPERLINK(\"\"http://x.test\"\",\"\"y\"\") from @guest\",'-src-1", rows[2], StringComparison.Ordinal);
        Assert.EndsWith(",12345.50,,\"Room 101\r=cmd|' /C calc'!A0\",'+src-2", rows[3], StringComparison.Ordinal);   // the CR stays inside its quoted cell
        Assert.Equal("-1,250.50", VoucherExport.CsvSafe("-1,250.50"));                                                    // a negative amount keeps its sign
        Assert.Equal("'-2+3", VoucherExport.CsvSafe("-2+3"));
        Assert.Equal("'\t=1", VoucherExport.CsvSafe("\t=1"));
    }

    [Fact]
    public void Company_payment_is_a_receipt_against_company_receivables_and_leaves_the_company_credit_check_alone()
    {
        // DEC-076: checkout put Rs 6,300 on company credit (Journal Dr 1110 company receivables · Cr 1100 guest ledger); the company pays
        // Rs 6,300 by bank transfer → Receipt Dr 1030 bank · Cr 1110, so 1110 nets to 0 and the guest ledger is untouched.
        Assert.Contains(AccountingEvents.CompanyPaymentRecorded, AccountingEvents.Consumed);
        var vs = VoucherBuilder.Build(D,
        [
            SourceRules.FolioReceipt("receipt:9", "company-credit", "Company credit", D, 630_000, "n"),
            AccountingEventMapper.CompanyPayment(new CompanyPaymentRecordedEvent(4, 12, D, "Bank transfer", 630_000, "NEFT-DEMO-12", "Demo Industries (fictional)")),
        ], Map);
        var pay = vs.Single(v => v.SourceKey == "company-payment:12");
        Assert.Equal((VoucherType.Receipt, "company-payment", true), (pay.Type, pay.Sub, pay.Balanced));
        Assert.Equal([("1030", 630_000L, 0L), ("1110/CMP-4", 0L, 630_000L)], pay.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Contains("Demo Industries (fictional)", pay.Narration, StringComparison.Ordinal);
        Assert.Equal(0, vs.SelectMany(v => v.Entries).Where(e => PartyLedgers.ControlOf(e.Account) == "1110").Sum(e => e.DebitPaise - e.CreditPaise));
        // The daily company-credit reconciliation still compares only the checkout's company credit (Sub company-credit): Rs 6,300.
        var check = Reconciliation.Check(vs, Map, new OperationalFigures(0, 0, 0, 0, 0, 0, 0, 630_000)).Single(c => c.Id == "company-credit");
        Assert.True(check.Ok);
        Assert.Equal(630_000, check.LedgerPaise);
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

    [Fact]
    public void Dec135_itc_allowed_expense_books_its_GST_to_the_input_credit_ledger_and_the_reversal_mirrors_it()
    {
        // Rs 590 bulbs with Rs 90 GST, ITC allowed: Dr Repairs 500 · Dr GST input credit 90 · Cr UPI 590; the reversal record (−590, −90) swaps
        // every side. Disallowed (or an event published before DEC-135, no fields) keeps the whole bill in the category.
        // ACC-1 (contract §2): 1300 is the legacy ledger of locked snapshots (Duties & Taxes); the ITC splits half / half: 1310 Input CGST,
        // 1320 Input SGST (AccountingSource.Expense, integration).
        Assert.Equal(("GST input credit (legacy, locked snapshots only)", AccountGroup.Liability), (ChartOfAccounts.NameOf("1300"), ChartOfAccounts.Find("1300")!.Group));
        Assert.Equal("1310", ChartOfAccounts.DefaultMapping["tax:gst-input"]);
        Assert.True(ChartOfAccounts.IsValidKey("tax:gst-input"));
        Assert.Equal("GST input credit (ITC)", ChartOfAccounts.KeyGroup("tax:gst-input"));
        var vs = VoucherBuilder.Build(D,
        [
            AccountingEventMapper.Expense(new ExpensePostedEvent(51, D, "Maintenance", "UPI", 59_000, "Demo Electricals", null, 9_000, "allowed")),
            AccountingEventMapper.Expense(new ExpensePostedEvent(52, D, "Maintenance", "UPI", -59_000, "Demo Electricals", 51, -9_000, "allowed")),
            AccountingEventMapper.Expense(new ExpensePostedEvent(53, D, "Supplies", "Cash", 118_000, "Demo Soap", null, 18_000, "disallowed")),
            AccountingEventMapper.Expense(new ExpensePostedEvent(54, D, "Supplies", "Cash", 11_800, "Old bill", null)),
        ], Map);
        Assert.All(vs, v => Assert.True(v.Balanced, v.Number));
        static (string, long, long)[] E(Voucher v) => v.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)).ToArray();
        Assert.Equal([("5020", 50_000L, 0L), ("1310", 4_500L, 0L), ("1320", 4_500L, 0L), ("1010", 0L, 59_000L)], E(vs.Single(v => v.SourceKey == "expense:51")));
        Assert.Equal([("5020", 0L, 50_000L), ("1310", 0L, 4_500L), ("1320", 0L, 4_500L), ("1010", 59_000L, 0L)], E(vs.Single(v => v.SourceKey == "expense:52")));
        Assert.Equal([("5000", 118_000L, 0L), ("1000", 0L, 118_000L)], E(vs.Single(v => v.SourceKey == "expense:53")));
        Assert.Equal([("5000", 11_800L, 0L), ("1000", 0L, 11_800L)], E(vs.Single(v => v.SourceKey == "expense:54")));
        Assert.Equal(0, vs.SelectMany(v => v.Entries).Where(e => e.Account is "1310" or "1320").Sum(e => e.DebitPaise - e.CreditPaise));
        var xml = VoucherExport.TallyXml(vs, "HCC Demo Residency", "DEMO");
        Assert.Contains("<LEDGERNAME>Input CGST</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-45.00</AMOUNT>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Dec130_udhaar_payment_is_a_receipt_against_Udhaar_receivables_and_never_income_nor_the_collections_check()
    {
        Assert.Contains(AccountingEvents.UdhaarPaymentRecorded, AccountingEvents.Consumed);
        Assert.Equal("Udhaar receivables (customer credit)", ChartOfAccounts.NameOf("1120"));
        var vs = VoucherBuilder.Build(D,
        [
            AccountingEventMapper.UdhaarPayment(new UdhaarPaymentRecordedEvent(21, 31, D, "Cash", 100_000, "Ramesh (fictional)")),
            AccountingEventMapper.UdhaarPayment(new UdhaarPaymentRecordedEvent(21, 32, D, "UPI", 50_050, "Ramesh (fictional)", "UPI-77")),
        ], Map);
        Assert.Equal(["udhaar-payment:31", "udhaar-payment:32"], vs.Select(v => v.SourceKey));
        Assert.All(vs, v => Assert.Equal((VoucherType.Receipt, "udhaar-payment", true), (v.Type, v.Sub, v.Balanced)));
        Assert.Equal([("1000", 100_000L, 0L), ("1120", 0L, 100_000L)], vs[0].Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Equal([("1010", 50_050L, 0L), ("1120", 0L, 50_050L)], vs[1].Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Equal("Udhaar received · Ramesh (fictional) · UPI · UPI-77 · payment 32", vs[1].Narration);
        Assert.DoesNotContain(vs.SelectMany(v => v.Entries), e => e.Account.StartsWith('4'));   // never income
        // The daily collections check compares folio receipts only (Sub payment / refund): an Udhaar receipt leaves it alone.
        Assert.True(Reconciliation.Check(vs, Map, new OperationalFigures(0, 0, 0, 0, 0, 0, 0, 0)).Single(c => c.Id == "collections").Ok);
    }
}
