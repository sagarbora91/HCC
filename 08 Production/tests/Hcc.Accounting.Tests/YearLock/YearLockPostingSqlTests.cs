using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.YearLock;

/// <summary>
/// F5-int (ACC4-CONTRACT §1.2): the platform runner (<see cref="YearLockedCommandRunner"/>) refuses every command dated in a locked year with
/// ACC_YEAR_LOCKED — a manual voucher, a day / month unlock and an undated posting on a business date inside the year — and lets the next
/// year through.
/// </summary>
public sealed class YearLockPostingTests
{
    private sealed record Dated(DateOnly Date, string Note);
    private sealed record Monthly(string Month);
    private sealed record Plain(string Note);

    [Fact]
    public void Payload_dates_are_read_like_the_prototype()
    {
        Assert.Equal([new DateOnly(2026, 10, 24)], YearLockedCommandRunner.DatesOf(new Dated(new(2026, 10, 24), "x")));
        Assert.Equal([new DateOnly(2027, 3, 1)], YearLockedCommandRunner.DatesOf(new Monthly("2027-03")));
        Assert.Equal([new DateOnly(2026, 5, 2)], YearLockedCommandRunner.DatesOf(new ReopenBusinessDayLike(new(2026, 5, 2), "r")));
        Assert.Empty(YearLockedCommandRunner.DatesOf(new Plain("2026-10-24")));
        Assert.Empty(YearLockedCommandRunner.DatesOf<object?>(null));
    }

    private sealed record ReopenBusinessDayLike(DateOnly BusinessDate, string Reason);
}

[Trait("Category", "Sql")]
public sealed class YearLockPostingSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Plain(string Note);
    private sealed record Ok(int N);

    private static void IsYearLocked(HccException e, string fy)
    {
        Assert.Equal("ACC_YEAR_LOCKED", e.Details!["reason"].GetString());
        Assert.Contains(fy, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_post_dated_in_a_locked_year_is_refused()
    {
        var guard = new SqlYearLockGuard();
        var runner = db.Platform.Commands;
        Assert.IsType<YearLockedCommandRunner>(runner);
        CommandContext Ctx(string op) => new(db.PropertyId, db.Users["OWN"], op, AccountingDb.Key());

        // Open year: an undated command (business date 2026-10-24) runs.
        Assert.Equal(1, (await runner.RunAsync(Ctx("YearLockProbe"), new Plain("a"), async (tx, c) => { await tx.AuditAsync("probe", "year", "", c); return new Ok(1); }, Ct)).Value.N);

        await db.ScalarAsync("""
            INSERT acc.fiscal_year(property_id, fy, status, locked_by, locked_utc, carry_forward_id) VALUES (@p, '2026-27', 'locked', @u, '2027-04-10', 'CF-TEST');
            SELECT 1;
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@u", SqlDbType.Int, db.Users["OWN"]));

        // Undated posting on a business date in the locked year (folio / POS / invoices).
        IsYearLocked(await Fails(HccErrorCode.INVALID_TRANSITION,
            () => runner.RunAsync(Ctx("YearLockProbe"), new Plain("b"), async (tx, c) => { await tx.AuditAsync("probe", "year", "", c); return new Ok(2); }, Ct)), "2026-27");

        // Manual voucher dated in the year (journal / payment).
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
        var mv = new ManualVoucherService(runner, new SqlManualVoucherStore(db.Platform.Connections), db.Store, policy, db.Clock);
        IsYearLocked(await Fails(HccErrorCode.INVALID_TRANSITION, () => mv.SaveAsync(db.As("OWN"), AccountingDb.Key(),
            new SaveManualVoucherRequest(null, AccountingDb.Day, "Journal", "Correction", null, null, [new("5080", 100, 0), new("9999", 0, 100)]), Ct)), "2026-27");

        // Day unlock inside the year.
        var acc = new AccountingService(runner, db.Store, policy, db.Clock);
        IsYearLocked(await Fails(HccErrorCode.INVALID_TRANSITION, () => acc.UnlockAsync(db.As("OWN"), AccountingDb.Key(),
            new UnlockPeriodRequest(new(2026, 6, 1), "Correction after the year end", "AAAAAAAAAAE="), Ct)), "2026-27");

        // The guard: last day in, next year out.
        var fy = await runner.RunAsync(new CommandContext(db.PropertyId, db.Users["OWN"], "YearLockProbe", AccountingDb.Key()), new { Date = "2027-04-01" },
            async (tx, c) => { await tx.AuditAsync("probe", "year", "", c); return new { In = await guard.LockedFyAsync(tx, db.PropertyId, new DateOnly(2027, 3, 31), c), Out = await guard.LockedFyAsync(tx, db.PropertyId, new DateOnly(2027, 4, 1), c) }; }, Ct);
        Assert.Equal("2026-27", fy.Value.In);
        Assert.Null(fy.Value.Out);
    }
}
