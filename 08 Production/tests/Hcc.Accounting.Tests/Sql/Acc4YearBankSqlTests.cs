// F5-A1 · ACC-4 on SQL (migrations 4280–4289 / 4293) with the real P1 policy: the stores persist the carry-forward / year states and the bank
// statement, rows and matches; the books are the production engine's April 2026 month (RulebookMonthTests.FullMonthVouchers) fed through a
// decorator over SqlYearLockStore (the voucher read itself is covered by the store's own test below). Golden: carry-forward = §8.5;
// the fictional April statement reconciles to difference 0; C2 / C3 checks.
using System.Data;
using Hcc.Accounting.Tests.Golden;
using Hcc.Accounting.Tests.Harness;
using Hcc.Api.Endpoints;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

[Trait("Category", "Sql")]
public sealed class Acc4YearBankSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private const string Hdfc = "1030/BA-HDFC";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string Csv = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Bank", "bank-april-2026.csv"));

    /// <summary>SqlYearLockStore with the golden month as the books and a ready checklist (persistence stays SQL).</summary>
    private sealed class GoldenBooks(SqlYearLockStore sql, List<Voucher> month) : IYearLockStore
    {
        public List<Voucher> Month { get; } = month;
        public YearCheckInput Input { get; set; } = new(true, [], [], 0, 0, 0, 0, [], false);
        public Task<IReadOnlyList<FiscalYearState>> YearsAsync(int p, CancellationToken ct) => sql.YearsAsync(p, ct);
        public Task SaveYearAsync(int p, FiscalYearState y, int a, string act, string? r, DateTime n, CancellationToken ct) => sql.SaveYearAsync(p, y, a, act, r, n, ct);
        public Task<IReadOnlyList<CarryForward>> CarryForwardsAsync(int p, CancellationToken ct) => sql.CarryForwardsAsync(p, ct);
        public Task SaveCarryForwardAsync(int p, CarryForward cf, CancellationToken ct) => sql.SaveCarryForwardAsync(p, cf, ct);
        public Task<IReadOnlyList<Voucher>> VouchersUpToAsync(int p, DateOnly d, CancellationToken ct) => Task.FromResult<IReadOnlyList<Voucher>>([.. Month.Where(v => v.Date <= d)]);
        public Task<YearCheckInput> CheckInputAsync(int p, string fy, DateOnly bd, CancellationToken ct) => Task.FromResult(Input);
        public Task<IReadOnlyList<string>> LockedMonthsAsync(int p, CancellationToken ct) => sql.LockedMonthsAsync(p, ct);
        public Task MarkReexportAsync(int p, DateOnly d, string r, DateTime n, CancellationToken ct) => Task.CompletedTask;
    }

    private SqlAccessPolicy Policy() => new(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
    private SqlYearLockStore Sql() => new(db.Platform.Connections, db.Store, new OpeningReportSource(new SqlOpeningStore(db.Platform.Connections)));

    [Fact]
    public async Task Year_golden_carry_forward_persists_lock_reopen_relock_and_roles()
    {
        await db.ScalarAsync("DELETE acc.carry_forward_line; DELETE acc.carry_forward; DELETE acc.fiscal_year_event; DELETE acc.fiscal_year;");
        var books = new GoldenBooks(Sql(), [.. RulebookMonthTests.FullMonthVouchers()]);
        var svc = new YearLockService(books, () => db.Clock.GetUtcNow().UtcDateTime);
        var policy = Policy();
        var own = await AccountingYearBankEndpoints.YearRoleAsync(policy, db.As("OWN"), Ct);
        var acc = await AccountingYearBankEndpoints.YearRoleAsync(policy, db.As("ACC"), Ct);
        Assert.Equal(("Owner", "Accountant", "none"), (own, acc, await AccountingYearBankEndpoints.YearRoleAsync(policy, db.As("REC"), Ct)));
        var bd = new DateOnly(2027, 4, 2);
        var cf = await svc.CarryForwardAsync(db.PropertyId, db.Users["ACC"], acc, "2026-27", Ct);
        var stored = (await books.CarryForwardsAsync(db.PropertyId, Ct)).Single();
        Assert.Equal(cf.Lines, stored.Lines);
        Assert.Equal(cf.Close, stored.Close);
        Assert.Equal(792_790_000, stored.Lines.Sum(l => l.Dr));
        Assert.Equal(-579_540_000, stored.Lines.Where(l => l.Ledger == "3100").Sum(l => l.Dr - l.Cr));
        Assert.Equal(14_040_000, stored.ProfitToCapital);
        await Fails(HccErrorCode.FORBIDDEN, () => svc.LockAsync(db.PropertyId, db.Users["ACC"], acc, "2026-27", cf.Id, bd, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => svc.CarryForwardAsync(db.PropertyId, db.Users["REC"], "none", "2026-27", Ct));
        books.Input = books.Input with { Balance9999 = 5 };
        Assert.Equal("ACC_YEAR_NOT_READY", (await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.LockAsync(db.PropertyId, db.Users["OWN"], own, "2026-27", cf.Id, bd, Ct))).Details!["reason"].GetString());
        books.Input = books.Input with { Balance9999 = 0 };
        await svc.LockAsync(db.PropertyId, db.Users["OWN"], own, "2026-27", cf.Id, bd, Ct);
        var years = await books.YearsAsync(db.PropertyId, Ct);
        Assert.Equal(YearStatus.Locked, years.Single().Status);
        Assert.Equal("ACC_YEAR_LOCKED", Assert.Throws<HccException>(() => YearLockRules.GuardPost(years, new DateOnly(2026, 4, 20))).Details!["reason"].GetString());
        // the approved carry-forward is the FY27 opening: the year-close voucher zeroes P&L and 3000 into 3100
        books.Month.Add(YearLockRules.CloseVoucher((await books.CarryForwardsAsync(db.PropertyId, Ct)).Single(c => c.Status == "approved")));
        Assert.Equal(cf.Lines, YearLockRules.Build(books.Month, new DateOnly(2027, 4, 1)).Lines);
        books.Month.RemoveAt(books.Month.Count - 1);
        await svc.ReopenAsync(db.PropertyId, db.Users["OWN"], own, "2026-27", "CA found a missed bill", Ct);
        Assert.True(YearLockRules.Provisional(await books.YearsAsync(db.PropertyId, Ct), new DateOnly(2027, 5, 1)));
        await svc.RelockAsync(db.PropertyId, db.Users["OWN"], own, "2026-27", bd, Ct);
        var all = await books.CarryForwardsAsync(db.PropertyId, Ct);
        Assert.Equal(new[] { "approved", "superseded" }, all.Select(c => c.Status).Order(StringComparer.Ordinal));
        Assert.Equal(3, await db.LongAsync("SELECT COUNT(*) FROM acc.fiscal_year_event WHERE action IN ('lock','reopen','relock')"));
    }

    [Fact]
    public async Task Year_store_reads_the_books_and_the_checklist()
    {
        var sql = Sql();
        var vs = await sql.VouchersUpToAsync(db.PropertyId, AccountingDb.Day, Ct);
        Assert.Equal(vs.Sum(v => v.Debit), vs.Sum(v => v.Entries.Sum(e => e.CreditPaise)));
        var x = await sql.CheckInputAsync(db.PropertyId, "2026-27", AccountingDb.Day, Ct);
        Assert.False(x.FyEnded);
        Assert.Contains(YearLockRules.Checks("2026-27", x), c => c.Id == "days-locked" && !c.Ok);
    }

    [Fact]
    public async Task Bank_golden_statement_reconciles_to_zero_on_sql()
    {
        await db.ScalarAsync("UPDATE acc.fiscal_year SET status = 'reopened', reopened_by = 1, reopen_reason = N'bank test reset', provisional_exports = 0 WHERE status = 'locked';");
        var books = new GoldenBooks(Sql(), [.. RulebookMonthTests.FullMonthVouchers()]);
        long next = 9000;
        var svc = new BankReconService(new SqlBankReconStore(db.Platform.Connections), books, Policy(), () => db.Clock.GetUtcNow().UtcDateTime, (c, r, ct) =>
        {
            var id = ++next;
            books.Month.Add(new Voucher($"X/{id}", r.Date!.Value, VoucherType.Journal, r.Narration!, "manual:" + id, "manual", r.Template!,
                r.Lines!.Select(l => new VoucherEntry(l.Account!, l.DebitPaise, l.CreditPaise)).ToList(), []));
            return Task.FromResult(new ManualVoucherResult(id, "approved", r.Date!.Value, null, "v1"));
        });
        await Fails(HccErrorCode.FORBIDDEN, () => svc.ImportAsync(db.As("REC"), Hdfc, "generic", "bank-april-2026.csv", Csv, false, Ct));
        var imp = await svc.ImportAsync(db.As("ACC"), Hdfc, "generic", "bank-april-2026.csv", Csv, false, Ct);
        Assert.Equal((15, 30_000_000L, 54_656_100L), (imp.Rows, imp.OpeningBalance, imp.ClosingBalance));
        Assert.Equal("BANK_DUP_FILE", (await Fails(HccErrorCode.VALIDATION, () => svc.ImportAsync(db.As("ACC"), Hdfc, "generic", "again.csv", Csv, false, Ct))).Details!["reason"].GetString());
        var auto = await svc.AutoMatchAsync(db.As("ACC"), Hdfc, Ct);
        Assert.Equal(10, auto.Matched);
        Assert.Equal(3, auto.Settlements.Count);
        var rows = await svc.RowsAsync(db.As("ACC"), Hdfc, Ct);
        var upi = rows.Where(r => r.Narration.StartsWith("UPI SETTL", StringComparison.Ordinal)).Select(r => r.Id).ToList();
        var contra = BankReconRules.BookLines(books.Month, Hdfc, new DateOnly(2026, 4, 30)).Single(l => l.Amount == 21_300_000);
        Assert.Equal("BANK_MATCH_SUM", (await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.MatchAsync(db.As("ACC"), upi.Take(2).ToList(), [contra.Ref], Ct))).Details!["reason"].GetString());
        var mm = await svc.MatchAsync(db.As("ACC"), upi, [contra.Ref], Ct);
        await svc.UnmatchAsync(db.As("ACC"), mm.Id, Ct);
        await svc.MatchAsync(db.As("ACC"), upi, [contra.Ref], Ct);
        await Fails(HccErrorCode.VALIDATION, () => svc.CreateVoucherAsync(db.As("ACC"), rows.Single(r => r.Narration.StartsWith("INTEREST", StringComparison.Ordinal)).Id, "9999", null, Ct));
        await svc.CreateVoucherAsync(db.As("ACC"), rows.Single(r => r.Narration.StartsWith("INTEREST", StringComparison.Ordinal)).Id, "4960", null, Ct);
        await svc.CreateVoucherAsync(db.As("ACC"), rows.Single(r => r.Narration.StartsWith("SMS", StringComparison.Ordinal)).Id, "5080", null, Ct);
        var rec = await svc.ReconAsync(db.As("ACC"), Hdfc, new DateOnly(2026, 4, 30), Ct);
        Assert.Equal((50_156_100L, 4_500_000L, 54_656_100L, 0L), (rec.BooksBalance, rec.IssuedTotal, rec.StatementBalance, rec.Difference));
        Assert.Empty(rec.BankOnly);
        Assert.Equal(0, BankReconRules.Clearing(books.Month, "1010", new DateOnly(2026, 4, 30)).Balance);
        Assert.Equal(0, BankReconRules.Clearing(books.Month, "1020", new DateOnly(2026, 4, 30)).Balance);
        await Fails(HccErrorCode.FORBIDDEN, () => svc.UpsertLayoutAsync(db.As("ACC"), BankReconRules.Generic with { Id = "acc-try" }, Ct));
        await svc.UpsertLayoutAsync(db.As("OWN"), new BankStatementLayout("amt-flag", "Amount + Dr/Cr", Hdfc, ';', 1, "DD-MMM-YYYY", new BankLayoutColumns("Txn Date", "Details", Amount: "Amount", DrCrFlag: "Type")), Ct);
        Assert.Contains(await svc.LayoutsAsync(db.As("ACC"), Ct), l => l.Id == "amt-flag" && l.Columns.DrCrFlag == "Type");
        Assert.Contains(await svc.LayoutsAsync(db.As("ACC"), Ct), l => l.Id == "generic");
    }

    [Fact]
    public void C2_journal_tie_compares_the_posted_set_off_journal()
    {
        var live = GstSetOff.Compute(new GstHeads(0, 1_450_000, 1_450_000), GstHeads.Zero, 10_000);
        var key = GstMonthRules.SourceKey("2026-04", new DateOnly(2026, 5, 10));
        var posted = new StoredGstSetoff(1, "2026-04", new DateOnly(2026, 5, 10), key, live, 1, DateTime.UtcNow);
        var want = GstSetOff.Entries(live);
        Voucher J(long scale) => new("J", posted.PostedOn, VoucherType.Journal, "set-off", key, "tax", "gst-setoff",
            want.Select(e => new VoucherEntry(e.MapKey, e.DebitPaise * scale, e.CreditPaise * scale)).ToList(), []);
        Assert.Null(GstMonthService.JournalTie("2026-04", posted, [J(1)], live));
        Assert.NotNull(GstMonthService.JournalTie("2026-04", posted, [J(2)], live));
        Assert.NotNull(GstMonthService.JournalTie("2026-04", posted, [], live));
    }
}
