// ACC-4 · F5-A1 · year lock / carry-forward / reopen on the server (ACC4-CONTRACT §1, §6). Golden: the rulebook April 2026 month (the production
// engine's full month) taken as the whole FY → carry-forward = §8.5 balance sheet (rulebook-reports.json), profit 1,40,400 to 3100, 3000 zero.
using System.Text.Json;
using Hcc.Accounting.Tests.Golden;
using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Accounting.Tests.YearLock;

public sealed class YearLockTests
{
    private static readonly JsonDocument Doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "rulebook-reports.json")));
    private static readonly DateOnly MonthEnd = new(2026, 4, 30);
    private static Dictionary<string, long> Net(IEnumerable<CarryForwardLine> lines) =>
        lines.GroupBy(l => l.Ledger).ToDictionary(g => g.Key, g => g.Sum(l => l.Dr - l.Cr), StringComparer.Ordinal);

    [Fact]
    public void Golden_april_as_the_year_carries_the_balance_sheet()
    {
        var d = YearLockRules.Build(RulebookMonthTests.FullMonthVouchers(), MonthEnd);
        var bs = Doc.RootElement.GetProperty("expected").GetProperty("balanceSheet");
        Assert.True(d.Balanced);
        Assert.Equal(bs.GetProperty("assetsTotal").GetInt64(), d.Debit);
        Assert.Equal(14_040_000, d.ProfitToCapital);
        Assert.Equal(5_000_000, d.DrawingsToCapital);
        var n = Net(d.Lines);
        Assert.Equal(-bs.GetProperty("ownersFunds").GetProperty("total").GetInt64(), n["3100"]);
        foreach (var block in new[] { "nonCurrentLiabilities", "currentLiabilities" })
            foreach (var p in bs.GetProperty(block).GetProperty("lines").EnumerateObject()) Assert.Equal(-p.Value.GetInt64(), n[p.Name]);
        foreach (var block in new[] { "nonCurrentAssets", "currentAssets" })
            foreach (var p in bs.GetProperty(block).GetProperty("lines").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), n[p.Name]);
        Assert.DoesNotContain(d.Lines, l => l.Ledger[0] is '4' or '5' || l.Ledger == "3000");
        Assert.Equal(2, d.Lines.Count(l => l.Ledger == "2200"));
        Assert.Equal(0, d.Close.Sum(e => e.DebitPaise - e.CreditPaise));
    }

    [Fact]
    public void Close_voucher_opens_the_next_year_on_the_carry_forward()
    {
        var month = RulebookMonthTests.FullMonthVouchers();
        var d = YearLockRules.Build(month, MonthEnd);
        var cf = new CarryForward("CF-1", "2026-27", "2027-28", "approved", d.Lines, d.ProfitToCapital, d.DrawingsToCapital, d.Close, DateTime.UtcNow, 1);
        var v = YearLockRules.CloseVoucher(cf);
        Assert.Equal(new DateOnly(2027, 4, 1), v.Date);
        Assert.Equal(v.Debit, v.Credit);
        var after = YearLockRules.Build([.. month, v], new DateOnly(2027, 4, 1));
        Assert.Equal(d.Lines, after.Lines);
        Assert.Equal(0, after.ProfitToCapital);
    }

    [Fact]
    public void Checks_and_reopen_rules()
    {
        var bad = YearLockRules.Checks("2026-27", new YearCheckInput(false, [], ["2026-04"], 0, 100, 5, 0, ["manual MV-1"], false));
        Assert.Equal(new[] { "days-locked", "months-gst-locked", "suspense", "clearing", "pending" }, bad.Where(c => !c.Ok).Select(c => c.Id));
        var ex = Assert.Throws<HccException>(() => YearLockRules.RequireReady(new YearPreview("2026-27", default, default, false, bad, new([], 0, 0, [], 0, 0))));
        Assert.Equal("ACC_YEAR_NOT_READY", ex.Details!["reason"].GetString());
        var years = new[] { new FiscalYearState("2026-27", YearStatus.Locked), new FiscalYearState("2027-28", YearStatus.Locked) };
        Assert.Equal("ACC_YEAR_REOPEN_BLOCKED", Assert.Throws<HccException>(() => YearLockRules.CheckReopen("2026-27", years, [], "CA found a missed bill")).Details!["reason"].GetString());
        Assert.Equal("ACC_YEAR_REOPEN_BLOCKED", Assert.Throws<HccException>(() => YearLockRules.CheckReopen("2027-28", years, ["2028-04"], "CA found a missed bill")).Details!["reason"].GetString());
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => YearLockRules.CheckReopen("2027-28", years, [], "short")).Code);
        Assert.Equal("CA found a missed bill", YearLockRules.CheckReopen("2027-28", years, ["2027-05"], "CA found a missed bill"));
        Assert.Equal("ACC_YEAR_LOCKED", Assert.Throws<HccException>(() => YearLockRules.GuardPost(years, new DateOnly(2026, 6, 1))).Details!["reason"].GetString());
        Assert.True(YearLockRules.Provisional([new FiscalYearState("2026-27", YearStatus.Reopened, ProvisionalExports: true)], new DateOnly(2027, 5, 1)));
        Assert.Equal("2027-28", YearLockRules.Next("2026-27"));
    }

    [Fact]
    public async Task Service_lock_reopen_relock_with_roles()
    {
        var store = new MemoryYearStore(RulebookMonthTests.FullMonthVouchers());
        var svc = new YearLockService(store, () => new DateTime(2027, 4, 2, 10, 0, 0, DateTimeKind.Utc));
        var today = new DateOnly(2027, 4, 2);
        var cf = await svc.CarryForwardAsync(1, 7, "Accountant", "2026-27", TestContext.Current.CancellationToken);
        Assert.Equal("draft", cf.Status);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => svc.LockAsync(1, 7, "Accountant", "2026-27", cf.Id, today, TestContext.Current.CancellationToken))).Code);
        store.Input = store.Input with { UnlockedDays = [new DateOnly(2026, 4, 9)] };
        Assert.Equal("ACC_YEAR_NOT_READY", (await Assert.ThrowsAsync<HccException>(() => svc.LockAsync(1, 1, "Owner", "2026-27", cf.Id, today, TestContext.Current.CancellationToken))).Details!["reason"].GetString());
        store.Input = store.Input with { UnlockedDays = [] };
        var y = await svc.LockAsync(1, 1, "Owner", "2026-27", cf.Id, today, TestContext.Current.CancellationToken);
        Assert.Equal(YearStatus.Locked, y.Status);
        Assert.Equal("approved", store.Cfs.Single().Status);
        await svc.ReopenAsync(1, 1, "Owner", "2026-27", "CA found a missed bill", TestContext.Current.CancellationToken);
        Assert.True(YearLockRules.Provisional(store.Years, new DateOnly(2026, 10, 1)));
        var again = await svc.RelockAsync(1, 1, "Owner", "2026-27", today, TestContext.Current.CancellationToken);
        Assert.Equal(YearStatus.Locked, again.Status);
        Assert.Equal("superseded", store.Cfs.Single(c => c.Id == cf.Id).Status);
        Assert.Single(store.Cfs, c => c.Status == "approved");
        Assert.Contains(new DateOnly(2027, 4, 1), store.Reexport);
    }

    private sealed class MemoryYearStore(IReadOnlyList<Voucher> vouchers) : IYearLockStore
    {
        public List<FiscalYearState> Years { get; } = [];
        public List<CarryForward> Cfs { get; } = [];
        public List<DateOnly> Reexport { get; } = [];
        public YearCheckInput Input { get; set; } = new(true, [], [], 0, 0, 0, 0, [], false);
        public Task<IReadOnlyList<FiscalYearState>> YearsAsync(int propertyId, CancellationToken ct) => Task.FromResult<IReadOnlyList<FiscalYearState>>([.. Years]);
        public Task SaveYearAsync(int propertyId, FiscalYearState year, int actor, string action, string? reason, DateTime now, CancellationToken ct)
        { Years.RemoveAll(y => y.Fy == year.Fy); Years.Add(year); return Task.CompletedTask; }
        public Task<IReadOnlyList<CarryForward>> CarryForwardsAsync(int propertyId, CancellationToken ct) => Task.FromResult<IReadOnlyList<CarryForward>>([.. Cfs]);
        public Task SaveCarryForwardAsync(int propertyId, CarryForward cf, CancellationToken ct) { Cfs.RemoveAll(c => c.Id == cf.Id); Cfs.Add(cf); return Task.CompletedTask; }
        public Task<IReadOnlyList<Voucher>> VouchersUpToAsync(int propertyId, DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<Voucher>>([.. vouchers.Where(v => v.Date <= date)]);
        public Task<YearCheckInput> CheckInputAsync(int propertyId, string fy, DateOnly businessDate, CancellationToken ct) => Task.FromResult(Input);
        public Task<IReadOnlyList<string>> LockedMonthsAsync(int propertyId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task MarkReexportAsync(int propertyId, DateOnly date, string reason, DateTime now, CancellationToken ct) { Reexport.Add(date); return Task.CompletedTask; }
    }
}
