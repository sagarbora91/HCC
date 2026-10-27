using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-1 lane acc1-parties (contract §3, §4; migrations 4210, 4211, 4215): the consumer reads the parties an event names (the tender's settles-to
/// account from the money masters, the vendor list row, the company, the Udhaar customer, the staff member, the bank a slip names), stores them in
/// acc.source_entry.party_id, and the vouchers, the lock snapshot (account_code VARCHAR(45)) and the trial balance carry the party ledger ids.
/// Own fixture database (its property gets the money-master seed and one vendor). Dates in September are closed (business date 24 Oct).
/// </summary>
[Trait("Category", "Sql")]
public sealed class PartyLedgerSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> SeedAsync()
    {
        var p = ("@p", SqlDbType.Int, (object)db.PropertyId);
        await db.ScalarAsync("EXEC mst.usp_seed_money_masters @p;", p);
        await db.ScalarAsync("""
            IF NOT EXISTS (SELECT 1 FROM ops.expense_vendor WHERE property_id = @p AND code = 'V-MORNING')
              INSERT ops.expense_vendor(property_id, code, name, sort_order, vendor_type, tds, updated_by, updated_utc)
              VALUES (@p, 'V-MORNING', N'Morning Supplies', 10, 'supplier', 0, 0, SYSUTCDATETIME());
            """, p);
        return await db.LongAsync("""
            DECLARE @id BIGINT = (SELECT company_id FROM grp.company_account WHERE property_id = @p AND name = N'Deccan Agro Ltd (fictional)');
            IF @id IS NULL
            BEGIN
              INSERT grp.company_account(property_id, name, created_by, created_utc) VALUES (@p, N'Deccan Agro Ltd (fictional)', 0, SYSUTCDATETIME());
              SET @id = SCOPE_IDENTITY();
            END
            SELECT @id;
            """, p);
    }

    [Fact]
    public async Task Events_post_to_party_ledgers_which_survive_the_lock_and_sum_into_their_controls()
    {
        var company = await SeedAsync();
        var d = new DateOnly(2026, 9, 25); // a closed date no other test of this fixture uses
        await db.PublishAsync("ReceiptRecorded", 9001, new { folioId = 1L, receiptId = 9001L, kind = "payment", tender = "Cash", amountPaise = 10_000L, businessDate = d });
        await db.PublishAsync(AccountingEvents.CompanyPaymentRecorded, company,
            new CompanyPaymentRecordedEvent(company, 81, d, "Bank transfer", 4_000_000, "NEFT-DECCAN", "Deccan Agro Ltd (fictional)"));
        await db.PublishAsync(AccountingEvents.ExpensePosted, 91, new ExpensePostedEvent(91, d, "Supplies", "Vendor credit", 59_000, "Morning Supplies", null));
        await db.PublishAsync(AccountingEvents.UdhaarCreditRaised, 31, new UdhaarCreditRaisedEvent(31, d, "Suresh Patil (fictional)", "9876543210", 105_000, "restaurant", 500));
        await db.PublishAsync(AccountingEvents.AdvanceGiven, 5, new AdvanceGivenEvent(5, 7, 500_000, 100_000, d, "Cash"));
        await db.PublishAsync(AccountingEvents.CashBanked, 3, new CashBankingEvent(3, 1, 1, d, 100_000, "Hotel current account", "S-1"));
        await db.ProjectAsync();

        var day = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
        static List<(string, long, long)> E(VoucherView v) => v.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToList();
        var by = day.Vouchers.ToDictionary(v => v.SourceKey);
        Assert.Equal([("1000/TILL", 10_000L, 0L), ("1100", 0L, 10_000L)], E(by["receipt:9001"]));
        Assert.Equal([("1030/BANK-1", 4_000_000L, 0L), ("1110/CMP-" + company, 0L, 4_000_000L)], E(by["company-payment:81"]));
        Assert.Equal([("5000", 59_000L, 0L), ("2200/V-MORNING", 0L, 59_000L)], E(by["expense:91"]));
        Assert.Equal([("1120/UD-9876543210", 105_000L, 0L), ("4100", 0L, 100_000L), ("2110", 0L, 2_500L), ("2120", 0L, 2_500L)],
            E(by["udhaar:31"]).Select(x => x.Item1 == ChartOfAccounts.DefaultMapping["pos:sales"] ? ("4100", x.Item2, x.Item3) : x).ToList());
        Assert.Equal([("1200/STF-7", 500_000L, 0L), ("1000/TILL", 0L, 500_000L)], E(by["advance:5"]));
        Assert.Equal([("1030/BANK-1", 100_000L, 0L), ("1000/TILL", 0L, 100_000L)], E(by["banking:3"]));
        Assert.Equal("Company receivables (city ledger) · CMP-" + company, by["company-payment:81"].Entries[1].Ledger);
        Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
        Assert.DoesNotContain(day.Exceptions, x => x.Kind == "no-party");
        Assert.Equal(0, day.Errors);

        // party_id is stored per side; the summary trial balance has control rows only and ties.
        // receipt cash 1 · company payment bank + company 2 · vendor credit 1 · Udhaar customer 1 · advance staff + cash 2 · banking bank + cash 2
        Assert.Equal(9L, await db.LongAsync("""
            SELECT COUNT(*) FROM acc.source s JOIN acc.source_entry e ON e.source_id = s.source_id
            WHERE s.property_id = @p AND s.business_date = @d AND e.party_id IS NOT NULL;
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@d", SqlDbType.Date, d.ToDateTime(TimeOnly.MinValue))));
        var tb = await db.Queries.TrialBalanceAsync(db.As("ACC"), d, false, Ct);
        Assert.True(tb.Balanced);
        Assert.DoesNotContain(tb.Rows, r => r.Account.Contains('/', StringComparison.Ordinal));
        Assert.Equal(105_000, tb.Rows.Single(r => r.Account == "1120").BalancePaise);

        // The lock keeps the party ledger ids (acc.locked_entry.account_code widened to 45 by migration 4211).
        await db.Accounting.LockAsync(db.As("ACC"), AccountingDb.Key(), new LockPeriodRequest(d), Ct);
        var locked = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
        Assert.Equal("Locked", locked.Status);
        Assert.Contains(locked.Vouchers.SelectMany(v => v.Entries), e => e.Account == "1120/UD-9876543210" && e.DebitPaise == 105_000);
        Assert.Contains(locked.Vouchers.SelectMany(v => v.Entries), e => e.Account == "1110/CMP-" + company && e.CreditPaise == 4_000_000);
    }

    [Fact]
    public async Task A_source_without_its_party_stays_on_the_control_with_a_warning_that_does_not_block_the_lock()
    {
        await SeedAsync();
        var d = new DateOnly(2026, 9, 26);
        // A vendor not on the list (an old record): the vendor credit stays on 2200.
        await db.PublishAsync(AccountingEvents.ExpensePosted, 92, new ExpensePostedEvent(92, d, "Supplies", "Vendor credit", 12_000, "Not On The List Traders", null));
        await db.ProjectAsync();
        var day = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
        var v = day.Vouchers.Single(x => x.SourceKey == "expense:92");
        Assert.Equal("2200", v.Entries[1].Account);
        var w = day.Exceptions.Single(x => x.Kind == "no-party");
        Assert.Equal(("warning", v.Number), (w.Severity, w.Voucher));
        Assert.Equal(0, day.Errors);
        await db.Accounting.LockAsync(db.As("ACC"), AccountingDb.Key(), new LockPeriodRequest(d), Ct);
        Assert.Equal("Locked", (await db.Queries.DayAsync(db.As("ACC"), d, Ct)).Status);
    }
}
