using Hcc.Accounting.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// W2 contracts (POS, expenses, payroll) through the consumer; unmapped classification → Suspense blocks the lock until the Owner
/// maps it (DEC-063 G11); exports by role (G13): CSV not for the Cashier, Tally (simulated) Owner + Accountant only.
/// Dates in September are closed (business date 24 Oct) so they can be locked.
/// </summary>
[Trait("Category", "Sql")]
public sealed class MappingAndExportTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly S1 = new(2026, 9, 1);
    private static readonly DateOnly S2 = new(2026, 9, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    [Fact]
    public async Task Room_posted_pos_bill_is_booked_only_from_its_folio_line_and_a_void_is_a_negative_sales_voucher()
    {
        var d = new DateOnly(2026, 12, 5); // a date no other test of this fixture uses
        // mode = folio: the folio line's ChargePosted books it (guest ledger); PosBillSettled must not book it a second time.
        await db.PublishAsync(AccountingEvents.PosBillSettled, 41, new PosBillSettledEvent(41, "sale", d, "room service", null!, 46_200, 44_000, 1_100, 1_100, "Room posting", "folio"));
        await db.PublishAsync(AccountingEvents.PosBillSettled, 42, new PosBillSettledEvent(42, "sale", d, "restaurant", "UPI", 52_500, 50_000, 1_250, 1_250, "DEMO-UPI", "outlet"));
        await db.PublishAsync(AccountingEvents.PosBillVoided, 42, new PosBillSettledEvent(42, "void", d, "restaurant", "UPI", 52_500, 50_000, 1_250, 1_250, "Void of bill 42", "outlet"));
        await db.ProjectAsync();
        var day = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
        Assert.Equal(["pos-void:42", "pos:42"], day.Vouchers.Select(v => v.SourceKey).Order(StringComparer.Ordinal));
        Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
        static Dictionary<string, long> Net(VoucherView v) => v.Entries.GroupBy(e => e.Account).ToDictionary(g => g.Key, g => g.Sum(e => e.DebitPaise - e.CreditPaise));
        var sale = Net(day.Vouchers.Single(v => v.SourceKey == "pos:42"));
        var reversal = Net(day.Vouchers.Single(v => v.SourceKey == "pos-void:42"));
        Assert.Equal(sale.OrderBy(k => k.Key).Select(k => (k.Key, -k.Value)), reversal.OrderBy(k => k.Key).Select(k => (k.Key, k.Value)));
        Assert.Contains(sale, k => k.Value == 52_500); // the tender is debited the bill total once
    }

    [Fact]
    public async Task Pos_expense_and_payroll_events_post_balanced_vouchers_and_unmapped_blocks_lock_until_mapped()
    {
        await db.PublishAsync(AccountingEvents.PosBillSettled, 11, new PosBillSettledEvent(11, "sale", S1, "restaurant", "Cash", 52_500, 50_000, 1_250, 1_250, "Outlet cash"));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 21, new ExpensePostedEvent(21, S1, "Supplies", "Vendor credit", 40_000, "Demo Traders", null));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 22, new ExpensePostedEvent(22, S1, "Kitchen gas", "UPI", 120_000, "Demo Gas Agency", null));
        await db.PublishAsync(AccountingEvents.PayrollPosted, 31, new PayrollPostedEvent(31, "2026-08", S1, 3_000_000, 180_000, 97_500, 180_000, 22_500, 20_000, 100_000, 0, 2_677_500));
        await db.PublishAsync(AccountingEvents.PayrollPaid, 31, new PayrollPaidEvent(31, S1, "Bank transfer", 2_677_500, "NEFT-DEMO"));
        Assert.Equal(5, await db.ProjectAsync());

        var day = await db.Queries.DayAsync(db.As("ACC"), S1, Ct);
        Assert.Equal("Closed", day.Status);
        Assert.Equal(["SAL/20260901/001", "PMT/20260901/001", "PMT/20260901/002", "JRN/20260901/001", "JRN/20260901/002"], day.Vouchers.Select(v => v.Number));
        Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
        var gas = day.Vouchers.Single(v => v.SourceKey == "expense:22");
        Assert.Equal("9999", gas.Entries[0].Account);
        var unmapped = Assert.Single(day.Exceptions, x => x.Kind == "unmapped");
        Assert.Equal("expense:Kitchen gas", unmapped.Key);

        var blocked = await Fails(HccErrorCode.VALIDATION, () => db.Accounting.LockAsync(db.As("OWN"), Key(), new LockPeriodRequest(S1), Ct));
        Assert.Contains("exception", blocked.Message, StringComparison.Ordinal);
        Assert.Contains(await db.Queries.MappingAsync(db.As("MGR"), Ct), r => r.MapKey == "expense:Kitchen gas" && !r.Mapped && r.Uses == 1);
        // DEC-079 (A05-03): the Cashier no longer reads the books (mapping, trial balance); the daily collections report stays (Billing).
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.MappingAsync(db.As("CSH"), Ct));

        // Mapping rules: Owner only (G11), no Suspense, known or discovered key, no no-op.
        foreach (var role in new[] { "MGR", "ACC", "TAD", "CSH" })
            await Fails(HccErrorCode.FORBIDDEN, () => db.Accounting.SetMappingAsync(db.As(role), Key(), new SetLedgerMappingRequest("expense:Kitchen gas", "5050"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest("expense:Kitchen gas", "9999"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest("expense:Kitchen gas", "7777"), Ct));
        await Fails(HccErrorCode.NOT_FOUND, () => db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest("bogus:x", "4000"), Ct));
        await Fails(HccErrorCode.NOT_FOUND, () => db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest("expense:Never seen", "5050"), Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest("line:room", "4000"), Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Accounting.ResetMappingAsync(db.As("OWN"), Key(), new ResetLedgerMappingRequest("line:room"), Ct));

        var set = (await db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest("expense:Kitchen gas", "5050"), Ct)).Value;
        Assert.Null(set.Before);
        Assert.Equal("5050", set.After);
        var mapped = await db.Queries.DayAsync(db.As("ACC"), S1, Ct);
        Assert.Equal(0, mapped.Errors);
        Assert.Equal("5050", mapped.Vouchers.Single(v => v.SourceKey == "expense:22").Entries[0].Account);
        var lk = (await db.Accounting.LockAsync(db.As("OWN"), Key(), new LockPeriodRequest(S1), Ct)).Value;
        Assert.Equal(5, lk.VoucherCount);

        // Reset after lock: the locked snapshot keeps 5050, history records both changes.
        await db.Accounting.ResetMappingAsync(db.As("OWN"), Key(), new ResetLedgerMappingRequest("expense:Kitchen gas"), Ct);
        Assert.Equal("5050", (await db.Queries.DayAsync(db.As("ACC"), S1, Ct)).Vouchers.Single(v => v.SourceKey == "expense:22").Entries[0].Account);
        Assert.Equal([("expense:Kitchen gas", (string?)null, (string?)"5050"), ("expense:Kitchen gas", "5050", null)],
            (await db.Queries.MappingHistoryAsync(db.As("OWN"), Ct)).Select(h => (h.MapKey, h.Before, h.After)));

        // Payroll journal: PF payable = employee + employer.
        var pay = mapped.Vouchers.Single(v => v.SourceKey == "payroll:31");
        Assert.Equal(360_000, pay.Entries.Single(e => e.Account == "2310").CreditPaise);
    }

    [Fact]
    public async Task Exports_follow_DEC063_G13_and_tally_is_simulated_and_limited_to_31_days()
    {
        await db.PublishAsync(AccountingEvents.ExpensePosted, 41, new ExpensePostedEvent(41, S2, "Utilities", "Cash", 75_000, "Demo Power, \"East\"", null));
        await db.ProjectAsync();

        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.CsvAsync(db.As("CSH"), S2, S2, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.CsvAsync(db.As("HKP"), S2, S2, Ct));
        var (name, csv) = await db.Queries.CsvAsync(db.As("MGR"), S2, S2, Ct);
        Assert.Equal("HCC-vouchers-2026-09-02_2026-09-02.csv", name);
        var rows = csv.Split("\r\n");
        Assert.Equal("Date,Voucher No,Voucher Type,Ledger,Debit,Credit,Narration,Source ID", rows[0]);
        Assert.Equal("2026-09-02,PMT/20260902/001,Payment,Utilities,750.00,,\"Demo Power, \"\"East\"\" · Utilities · Cash\",expense:41", rows[1]);

        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.TallyAsync(db.As("MGR"), S2, S2, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.TallyAsync(db.As("CSH"), S2, S2, Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Queries.TallyAsync(db.As("ACC"), S2, S2.AddDays(31), Ct));
        var t = await db.Queries.TallyAsync(db.As("ACC"), S2, S2.AddDays(30), Ct);
        Assert.True(t.Simulated);
        Assert.Equal(1, t.VoucherCount);
        Assert.Equal(75_000, t.DebitPaise);
        Assert.Contains("SIMULATED EXPORT", t.Xml, StringComparison.Ordinal);
        Assert.Contains("<SVCURRENTCOMPANY>HCC Demo Residency</SVCURRENTCOMPANY>", t.Xml, StringComparison.Ordinal);
        Assert.Contains("<LEDGERNAME>Utilities</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-750.00</AMOUNT>", t.Xml, StringComparison.Ordinal);
        Assert.Contains("&quot;East&quot;", t.Xml, StringComparison.Ordinal);
        Assert.Equal(17, (await db.Queries.TallyAsync(db.As("OWN"), S2, S2, Ct)).Xml.Split('\n').Length);

        // Views: housekeeping / maintenance / reception / tech admin never see accounting; DEC-079: nor the Cashier (collections report only).
        foreach (var role in new[] { "HKP", "MNT", "REC", "TAD", "SUP", "CSH", "RST" })
            await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.DayAsync(db.As(role), S2, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.ChartAsync(db.As("CSH"), Ct));
        Assert.Contains(await db.Queries.ChartAsync(db.As("MGR"), Ct), a => a.Code == "2110" && a.Name == "Output CGST");
    }

    [Fact]
    public async Task Dec135_and_Dec130_input_credit_and_Udhaar_receipts_reach_the_vouchers_mapping_screen_Tally_and_a_balanced_trial_balance()
    {
        var d = new DateOnly(2026, 8, 20); // a closed date no other test of this fixture uses (ACC-1: moved out of the Exports test's 2 Sep – 2 Oct Tally window, order-dependent before)
        await db.PublishAsync(AccountingEvents.ExpensePosted, 61, new ExpensePostedEvent(61, d, "Maintenance", "UPI", 59_000, "Demo Electricals", null, 9_000, "allowed"));
        await db.PublishAsync(AccountingEvents.ExpenseReversed, 62, new ExpensePostedEvent(62, d, "Maintenance", "UPI", -59_000, "Demo Electricals", 61, -9_000, "allowed"));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 63, new ExpensePostedEvent(63, d, "Supplies", "Cash", 118_000, "Demo Soap", null, 18_000, "disallowed"));
        await db.PublishAsync(AccountingEvents.UdhaarPaymentRecorded, 21, new UdhaarPaymentRecordedEvent(21, 71, d, "Cash", 100_000, "Ramesh (fictional)", null, 501));
        await db.ProjectAsync();
        var day = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
        Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
        Assert.Equal([("5020", 50_000L, 0L), ("1300", 9_000L, 0L), ("1010", 0L, 59_000L)], day.Vouchers.Single(v => v.SourceKey == "expense:61").Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.Equal([("1000", 100_000L, 0L), ("1120/UD-RAMESH-FICTIONAL", 0L, 100_000L)], day.Vouchers.Single(v => v.SourceKey == "udhaar-payment:71").Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.Equal(0, day.Errors);
        var map = await db.Queries.MappingAsync(db.As("OWN"), Ct);
        Assert.Contains(map, r => r.MapKey == "tax:gst-input" && r.Account == "1300" && r.DefaultAccount == "1300" && r.Group == "GST input credit (ITC)" && r.Uses == 2);
        Assert.Contains(map, r => r.MapKey == "receipt:udhaar" && r.Account == "1120" && r.Uses == 1);
        var tally = await db.Queries.TallyAsync(db.As("OWN"), d, d, Ct);
        Assert.Contains("<LEDGERNAME>GST input credit</LEDGERNAME>", tally.Xml, StringComparison.Ordinal);
        Assert.Contains("<LEDGERNAME>Udhaar receivables (customer credit)</LEDGERNAME>", tally.Xml, StringComparison.Ordinal);
        Assert.Equal(tally.DebitPaise, tally.CreditPaise);
        var tb = await db.Queries.TrialBalanceAsync(db.As("ACC"), d, false, Ct);
        Assert.True(tb.Balanced);
        Assert.Equal(0, tb.Rows.Where(r => r.Account == "1300").Sum(r => r.BalancePaise));               // the reversal netted the input credit
        Assert.Equal(-100_000, tb.Rows.Single(r => r.Account == "1120").BalancePaise);                  // Cr: the receipt settled Udhaar receivables
    }
}
