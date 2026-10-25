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
        Assert.Contains(await db.Queries.MappingAsync(db.As("CSH"), Ct), r => r.MapKey == "expense:Kitchen gas" && !r.Mapped && r.Uses == 1);

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
        Assert.Equal(16, (await db.Queries.TallyAsync(db.As("OWN"), S2, S2, Ct)).Xml.Split('\n').Length);

        // Views: housekeeping / maintenance / reception / tech admin never see accounting.
        foreach (var role in new[] { "HKP", "MNT", "REC", "TAD", "SUP" })
            await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.DayAsync(db.As(role), S2, Ct));
        Assert.Contains(await db.Queries.ChartAsync(db.As("CSH"), Ct), a => a.Code == "2110" && a.Name == "Output CGST");
    }
}
