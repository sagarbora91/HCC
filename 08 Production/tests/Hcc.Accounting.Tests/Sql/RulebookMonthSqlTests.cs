using System.Text.Json;
using Hcc.Accounting.Tests.Golden;
using Hcc.Accounting.Tests.Harness;
using Hcc.Contracts.Accounting;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-1 (lane acc1-tests): the rulebook golden month through the real outbox → accounting consumer → vouchers → cumulative trial balance
/// (PERM-ACC-001), on a disposable database with the real P1 policy. The events and the expectation are those of
/// <c>Golden/rulebook-month.json</c> (rulebook §8, April 2026, fictional); the opening balances are NOT loaded here — they arrive through the
/// opening endpoint of lane acc1-opening (see the skipped test in <see cref="RulebookMonthTests"/>), so the expectation is the month's flow
/// (<c>expected.flow</c>). Not driven: T16 (an Owner drawing from the bank, rulebook B6 — ACC-2) and T4 T6 T7b T11 T14 T17 T18 T19.
/// </summary>
[Trait("Category", "Sql")]
public sealed class RulebookMonthSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static JsonDocument Month => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "rulebook-month.json")));

    [Fact]
    [Trait("Lane", "acc1-chart")]
    public async Task The_months_events_become_balanced_vouchers_and_the_cumulative_trial_balance_equals_the_rulebook_flow()
    {
        using var month = Month;
        var root = month.RootElement;
        var events = root.GetProperty("events").EnumerateArray().ToList();

        // ── publish every event of the month (dates inside April 2026, closed days of the harness property) and run the consumer once ──
        var id = 0L;
        foreach (var ev in events)
            await db.PublishAsync(ev.GetProperty("type").GetString()!, ++id, ev.GetProperty("payload").Clone());
        Assert.Equal(events.Count, await db.ProjectAsync());   // exactly one source per event
        Assert.Equal(0, await db.ProjectAsync());              // and exactly once

        // ── the Owner's mapping of the month (Owner only, DEC-063 G11): the advance tender to 2000, folio restaurant lines to 4200 (F&B revenue: needs lane acc1-chart) ──
        foreach (var o in root.GetProperty("mappingOverrides").EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Reverse())
            await db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest(o.Name, o.Value.GetString()!), Ct);

        // ── the day views: every voucher balances, no error on any date that has vouchers, the bank deposit and the cash shortage sit on their rulebook dates ──
        var dates = events.Select(e => DateOnly.Parse(e.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToList();
        foreach (var d in dates)
        {
            var day = await db.Queries.DayAsync(db.As("ACC"), d, Ct);
            Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
            Assert.Equal(0, day.Errors);
            Assert.Equal("Closed", day.Status);   // April is before the harness business date (24 Oct 2026)
        }

        Assert.Equal("bank-deposit", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 25), Ct)).Vouchers.Single().Sub);
        Assert.Equal("cash-difference", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 26), Ct)).Vouchers.Single().Sub);
        Assert.Equal("company-payment", (await db.Queries.DayAsync(db.As("ACC"), new DateOnly(2026, 4, 10), Ct)).Vouchers.Single().Sub);

        // ── cumulative trial balance at 30 April = the expectation worked out from the rulebook (control ledgers; parties sum into them) ──
        var tb = await db.Queries.TrialBalanceAsync(db.As("OWN"), new DateOnly(2026, 4, 30), cumulative: true, Ct);
        Assert.True(tb.Balanced);
        Assert.Equal(0, tb.Rows.Sum(r => r.BalancePaise));
        var got = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in tb.Rows) got[RulebookMonthTests.Control(r.Account)] = got.GetValueOrDefault(RulebookMonthTests.Control(r.Account)) + r.BalancePaise;
        foreach (var k in got.Where(x => x.Value == 0).Select(x => x.Key).ToList()) got.Remove(k);
        var want = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("expected").GetProperty("flow").GetProperty("control").EnumerateObject()) want[p.Name] = p.Value.GetInt64();
        Assert.False(got.ContainsKey("9999"), "something posted to 9999 Suspense");
        Assert.Equal(want, got);
    }
}
