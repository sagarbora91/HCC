using Hcc.Application.Closing;
using Hcc.Contracts.Closing;

namespace Hcc.Closing.Tests;

/// <summary>F8-QW-NA: the night-audit lines (pure, no database). Fictional data. Show only: nothing new blocks (QWQ-1).</summary>
public sealed class NightAuditTests
{
    private static readonly DateOnly D = new(2026, 10, 24);

    private static PreflightCheck C(string id, params string[] items) =>
        new(id, id, "", items.Length > 0, items.Select(i => new PreflightItem(i, null)).ToList());

    private static PreflightResult Pre(params PreflightCheck[] checks) => new(D, false, checks);

    private static NightAuditLine L(IReadOnlyList<NightAuditLine> lines, string key) => lines.Single(l => l.Key == key);

    private static readonly NightAuditSignals Fine = new(0, new DateTime(2026, 10, 24, 2, 0, 0));

    [Fact]
    public void All_clear_gives_every_line_ok()
    {
        var lines = NightAuditRules.Build(Pre(C("arrivals"), C("departures"), C("nights"), C("approvals"), C("expenses"), C("pos"), C("shift")), Fine);
        Assert.Equal(8, lines.Count);
        Assert.All(lines, l => Assert.True(l.Ok, l.Key));
    }

    [Fact]
    public void Each_preflight_line_flips_at_its_boundary_and_keeps_blocking()
    {
        var lines = NightAuditRules.Build(Pre(C("arrivals", "a"), C("departures", "d", "e"), C("nights", "n"), C("approvals", "x"), C("expenses"), C("pos", "p"), C("shift", "s")), Fine);
        foreach (var k in new[] { "na.arrivals", "na.departures", "na.nights", "na.approvals", "na.pos", "na.cash" })
        {
            Assert.False(L(lines, k).Ok, k);
            Assert.True(L(lines, k).Blocking, k);
        }
        Assert.Equal("2 overdue departures to resolve", L(lines, "na.departures").Detail);
    }

    [Fact]
    public void Keys_and_backup_lines_never_block()
    {
        var lines = NightAuditRules.Build(Pre(), new NightAuditSignals(2, new DateTime(2026, 10, 23, 23, 59, 0)));
        Assert.False(L(lines, "na.keys").Ok);
        Assert.False(L(lines, "na.backup").Ok);
        Assert.DoesNotContain(lines, l => (l.Key is "na.keys" or "na.backup") && l.Blocking);
    }

    [Fact]
    public void Backup_on_the_business_date_is_ok_and_unknown_signals_are_not_failures()
    {
        Assert.True(L(NightAuditRules.Build(Pre(), Fine), "na.backup").Ok);
        var unknown = NightAuditRules.Build(Pre(), new NightAuditSignals(null, null));
        Assert.True(L(unknown, "na.keys").Ok);
        Assert.False(L(unknown, "na.backup").Ok);
    }

    [Fact]
    public void Lines_add_no_blocker_beyond_the_existing_preflight_checks()
    {
        var pre = Pre(C("arrivals"), C("shift", "s"));
        var blockingKeys = NightAuditRules.Build(pre, new NightAuditSignals(3, null)).Where(l => l.Blocking && !l.Ok).Select(l => l.Key).ToList();
        Assert.Equal(["na.cash"], blockingKeys);
    }
}
