using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-2 parity (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; rulebook D1, §8.2 T13b): the pay run credits the advance recovered PER STAFF
/// member (<c>1200/STF-&lt;id&gt;</c>), so the 1200 control carries no <c>no-party</c> warning; an event without the split (published before
/// ACC-2) keeps the control line; the unlock reversal keeps the parties. Mirrors the prototype's tests/stream-accounting-parity.mjs (T13a–c).
/// </summary>
public sealed class PayrollPartyTests
{
    private static readonly DateOnly D = new(2026, 4, 30);
    private static readonly IReadOnlyDictionary<string, string> Map = ChartOfAccounts.Effective(null);

    private static List<(string, long, long)> E(Voucher v) => v.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)).ToList();

    /// <summary>The rulebook's T13b month: gross 2,40,000; PF 18,000 + 18,000; ESIC 1,500 + 6,500; PT 2,000; advance 5,000 from staff 7; net 2,13,500.</summary>
    private static PayrollPostedEvent T13b(IReadOnlyList<AdvanceRecoveryLine>? split) =>
        new(41, "2026-04", D, 24_000_000, 1_800_000, 650_000, 1_800_000, 150_000, 200_000, 500_000, 0, 21_350_000, 1, split);

    [Fact]
    public void T13b_pay_run_equals_the_rulebook_with_the_advance_on_the_staff_party_and_no_warning()
    {
        var v = Assert.Single(VoucherBuilder.Build(D, [AccountingEventMapper.PayRun(T13b([new AdvanceRecoveryLine(7, 500_000)]))], Map));
        Assert.Equal([("5100", 24_000_000L, 0L), ("5110", 1_800_000L, 0L), ("5110", 650_000L, 0L), ("2300", 0L, 21_350_000L), ("2310", 0L, 3_600_000L),
            ("2320", 0L, 800_000L), ("2330", 0L, 200_000L), ("1200/STF-7", 0L, 500_000L)], E(v));
        Assert.Equal(v.Entries.Sum(e => e.DebitPaise), v.Entries.Sum(e => e.CreditPaise));
        Assert.DoesNotContain(VoucherBuilder.Exceptions([v], [v], false, DayStatus.Closed), x => x.Kind == "no-party");
        Assert.Equal(("payroll:41", "payrun", VoucherType.Journal), (v.SourceKey, v.Sub, v.Type));
    }

    [Fact]
    public void Several_staff_recoveries_are_one_line_each_in_staff_order_and_an_unnamed_part_stays_on_the_control()
    {
        var src = SourceRules.PayRun("payroll:9", D, new PayRunFigures(3_000_000, 0, 0, 0, 0, 0, 300_000, 0, 2_700_000, [(12, 100_000), (3, 150_000)]), "Pay run");
        Assert.Equal([("STF-3", 150_000L), ("STF-12", 100_000L), (null, 50_000L)],
            src.Entries.Where(e => e.MapKey == SourceRules.AdvancesKey).Select(e => (e.Party, e.CreditPaise)).ToList());
        var v = Assert.Single(VoucherBuilder.Build(D, [src], Map));
        Assert.Equal(["1200/STF-3", "1200/STF-12", "1200"], v.Entries.Where(e => e.Account.StartsWith("1200", StringComparison.Ordinal)).Select(e => e.Account));
        Assert.Contains(VoucherBuilder.Exceptions([v], [v], false, DayStatus.Closed), x => x.Kind == "no-party");   // only the unnamed 50,000 warns
        Assert.Equal(v.Entries.Sum(e => e.DebitPaise), v.Entries.Sum(e => e.CreditPaise));
    }

    [Fact]
    public void An_event_without_the_split_keeps_the_control_line_and_the_reversal_keeps_every_party()
    {
        var old = Assert.Single(VoucherBuilder.Build(D, [AccountingEventMapper.PayRun(T13b(null))], Map));
        Assert.Contains(old.Entries, e => e.Account == "1200" && e.CreditPaise == 500_000);
        var posted = AccountingEventMapper.PayRun(T13b([new AdvanceRecoveryLine(7, 500_000)]));
        var rev = AccountingEventMapper.PayRunReversal(new PayRunUnlockedEvent(41, "2026-04", 1, 0, D), D, posted.SourceKey, posted.Entries, false);
        Assert.Equal(posted.Entries.Select(e => (e.MapKey, e.Party, e.CreditPaise, e.DebitPaise)), rev.Entries.Select(e => (e.MapKey, e.Party, e.DebitPaise, e.CreditPaise)));
        var both = VoucherBuilder.Build(D, [posted, rev], Map).SelectMany(v => v.Entries).GroupBy(e => e.Account).ToDictionary(g => g.Key, g => g.Sum(e => e.DebitPaise - e.CreditPaise));
        Assert.All(both.Values, b => Assert.Equal(0, b));
        Assert.True(both.ContainsKey("1200/STF-7"));
    }

    [Fact]
    public void T13a_and_T13c_advance_and_net_pay_post_to_the_staff_party_and_the_bank_the_funding_settles_to()
    {
        var adv = Assert.Single(VoucherBuilder.Build(D, [AccountingEventMapper.Advance(new AdvanceGivenEvent(5, 7, 500_000, 500_000, new DateOnly(2026, 4, 8), "Bank transfer"),
            new PartyContext(Tender: "BA-HDFC"))], Map));
        Assert.Equal([("1200/STF-7", 500_000L, 0L), ("1030/BA-HDFC", 0L, 500_000L)], E(adv));
        var paid = Assert.Single(VoucherBuilder.Build(D, [AccountingEventMapper.PayRunPaid(new PayrollPaidEvent(41, D, "Bank transfer", 21_350_000, "Bank file 1 · April 2026"),
            new PartyContext(Tender: "BA-HDFC"))], Map));
        Assert.Equal([("2300", 21_350_000L, 0L), ("1030/BA-HDFC", 0L, 21_350_000L)], E(paid));
        Assert.Equal("4200", ChartOfAccounts.DefaultMapping["line:food"]);   // ACC-2 parity: F&B folio lines (prototype Line.kind food)
    }
}
