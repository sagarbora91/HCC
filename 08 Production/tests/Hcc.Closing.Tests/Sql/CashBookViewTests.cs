using Hcc.Application.Closing;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Closing;

namespace Hcc.Closing.Tests.Sql;

/// <summary>Cash-book view over the S0 walkthrough (drawer 10,725 / 11,225 / 11,225 / 12,725 / 12,425 rupees) with the DEC-065 lock state.</summary>
[Trait("Category", "Sql")]
public sealed class CashBookViewTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D24 = new(2026, 10, 24);
    private static readonly DateOnly D25 = new(2026, 10, 25);
    private static readonly DateOnly D26 = new(2026, 10, 26);

    [Fact]
    public async Task S0_range_opening_carries_forward_and_foots_with_golden_drawer_figures()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await CloseJourneyTests.RunAsync(h, Ct);
        var q = h.Get<CashBookQueries>();
        var r = await q.GetRangeAsync(h.As(h.Accountant), D24, D26, Ct);
        Assert.Equal(3, r.Days.Count);
        var (a, b, c) = (r.Days[0], r.Days[1], r.Days[2]);
        Assert.Equal((500000L, 802500L, 180000L, 1122500L, 1122500L, 0L), (a.OpeningPaise, a.ReceiptsPaise, a.PaymentsPaise, a.ClosingPaise, a.CountedPaise!.Value, a.DifferencePaise!.Value));
        Assert.Equal((1122500L, 0L, 0L, 1122500L, 1122500L), (b.OpeningPaise, b.ReceiptsPaise, b.PaymentsPaise, b.ClosingPaise, b.CarriedPaise));
        Assert.Equal((1122500L, 200000L, 80000L, 1242500L, 1242500L), (c.OpeningPaise, c.ReceiptsPaise, c.PaymentsPaise, c.ClosingPaise, c.CarriedPaise));
        Assert.All(r.Days, d => Assert.Equal(("Closed", true, false), (d.Status, d.Reconciles, d.Locked)));
        Assert.Equal((500000L, 1002500L, 260000L, 0L, 0L, 1242500L), (r.OpeningPaise, r.ReceiptsPaise, r.PaymentsPaise, r.DifferencesPaise, r.GapsPaise, r.ClosingPaise));
        Assert.Equal(r.ClosingPaise, r.OpeningPaise + r.ReceiptsPaise - r.PaymentsPaise + r.DifferencesPaise + r.GapsPaise);
        Assert.Contains(a.Entries, e => e is { Kind: "pos-sale", InPaise: 52500 });
        Assert.Contains(a.Entries, e => e is { Kind: "expense", OutPaise: 180000 });
        Assert.Contains(a.Entries, e => e is { Kind: "deposit", InPaise: 50000 });
        Assert.Contains(c.Entries, e => e is { Kind: "deposit-return", OutPaise: 50000 });
        var report = (await h.Reports.GetDailyReportAsync(h.As(h.Owner), D24, D26, Ct)).Report.Drawer;
        Assert.Equal((500000L, 1242500L), (report.OpeningPaise, report.ExpectedPaise));
        var tail = await q.GetRangeAsync(h.As(h.Owner), D26, D26, Ct);
        Assert.Equal((1122500L, 1242500L), (tail.OpeningPaise, tail.ClosingPaise));
    }

    [Fact]
    public async Task Day_view_shows_the_lock_and_its_history_through_owner_reopen()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await CloseJourneyTests.RunAsync(h, Ct);
        var q = h.Get<CashBookQueries>();
        await h.Closing.LockCashBookDayAsync(h.As(h.Accountant), Hotel.Key(), new LockCashBookDayRequest(D24), Ct);
        var locked = await q.GetDayAsync(h.As(h.Owner), D24, Ct);
        Assert.True(locked.Locked);
        Assert.Equal(h.Accountant, locked.LockedBy);
        Assert.Equal(["lock"], locked.History.Select(e => e.Action));
        Assert.False((await q.GetDayAsync(h.As(h.Owner), D25, Ct)).Locked);
        await h.Closing.ReopenCashBookDayAsync(h.As(h.Owner), Hotel.Key(), new ReopenCashBookDayRequest(D24, "Banking slip keyed on the wrong day"), Ct);
        var open = await q.GetDayAsync(h.As(h.Owner), D24, Ct);
        Assert.False(open.Locked);
        Assert.Equal(["lock", "reopen"], open.History.Select(e => e.Action));
        Assert.Equal("Banking slip keyed on the wrong day", open.History[1].Reason);
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => q.GetDayAsync(h.As(h.Owner), new DateOnly(2026, 12, 1), Ct));
    }

    [Fact]
    public async Task Cash_book_is_for_finance_roles_only()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var q = h.Get<CashBookQueries>();
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => q.GetRangeAsync(h.As(h.Reception), D24, D24, Ct));
        Assert.Single((await q.GetRangeAsync(h.As(h.Manager), D24, D24, Ct)).Days);
    }
}
