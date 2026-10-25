using System.Security.Cryptography;
using System.Text;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Closing;
using Hcc.Domain.Errors;
using Microsoft.Data.SqlClient;

namespace Hcc.Closing.Tests.Sql;

/// <summary>Concurrent close attempts (one wins), snapshot immutability, and the DEC-065 cash-book day lock with Owner reopen.</summary>
[Trait("Category", "Sql")]
public sealed class ConcurrencySnapshotCashBookTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D24 = new(2026, 10, 24);
    private static readonly DateOnly D25 = new(2026, 10, 25);

    private static async Task<(Hotel H, List<(long ReservationId, long StayId, long FolioId)> Stays)> HotelAsync(int stays = 4)
    {
        var h = await Hotel.CreateAsync(Ct);
        var list = new List<(long, long, long)>();
        foreach (var room in new[] { "101", "102", "103", "104", "106", "107" }.Take(stays)) list.Add(await h.SeedInHouseAsync(room, "2026-10-23", "2026-10-28", ct: Ct));
        return (h, list);
    }

    [Fact]
    public async Task A_second_close_while_one_runs_is_refused_and_afterwards_replays_the_completed_run()
    {
        var (h, _) = await HotelAsync();
        await using var __ = h;
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Observer.OnStep = async (p, _, c) =>
        {
            if (p != "night" || paused.Task.IsCompleted) return;
            paused.SetResult();
            await release.Task.WaitAsync(c);
        };
        var first = h.CloseAsync(who: h.Manager, ct: Ct);
        await paused.Task.WaitAsync(Ct);
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.CloseAsync(who: h.Manager2, ct: Ct));
        Assert.Contains("already running", ex.Message, StringComparison.Ordinal);
        release.SetResult();
        Assert.Equal("Completed", (await first).Value.Status);
        var later = await h.CloseAsync(who: h.Manager2, ct: Ct);
        Assert.True(later.Replayed);
        Assert.Equal(1, later.Value.Attempts);
    }

    [Fact]
    public async Task Six_parallel_closes_one_wins_one_barrier_one_snapshot_each_night_once()
    {
        var (h, stays) = await HotelAsync(6);
        await using var __ = h;
        var who = new[] { h.Owner, h.Manager, h.Manager2, h.Owner, h.Manager, h.Manager2 };
        var tasks = who.Select(u => Task.Run(async () =>
        {
            try
            {
                var r = await h.CloseAsync(who: u, ct: Ct);
                return r.Replayed ? "replayed" : "won";
            }
            catch (HccException e) when (e.Code == HccErrorCode.INVALID_TRANSITION)
            {
                return "refused";
            }
        }, Ct)).ToList();
        var outcomes = await Task.WhenAll(tasks);
        Assert.Equal(1, outcomes.Count(o => o == "won"));
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM cls.business_day", Ct));
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM cls.closing_snapshot", Ct));
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM cls.close_run", Ct));
        Assert.Equal(stays.Count, await h.LongAsync("SELECT COUNT(*) FROM bill.charge WHERE kind = 'room' AND service_date = '2026-10-24'", Ct));
        Assert.Equal(D25, await h.BusinessDateAsync(Ct));
    }

    [Fact]
    public async Task Snapshot_is_immutable_and_later_activity_leaves_it_and_the_closed_report_unchanged()
    {
        var (h, stays) = await HotelAsync();
        await using var __ = h;
        await h.PayAsync(stays[0].FolioId, 120000, "UPI", ct: Ct);
        await h.CloseAsync(ct: Ct);
        var before = await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct);
        var json = (string)(await h.ScalarAsync("SELECT record_json FROM cls.closing_snapshot", Ct))!;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), before.Sha256);

        async Task<int> SqlError(string sql)
        {
            var e = await Assert.ThrowsAsync<SqlException>(() => h.ScalarAsync(sql, Ct));
            return e.Number;
        }

        Assert.Equal(51501, await SqlError("UPDATE cls.closing_snapshot SET net_revenue_paise = 0;"));
        Assert.Equal(51501, await SqlError("DELETE cls.closing_snapshot;"));
        Assert.Equal(51500, await SqlError("UPDATE cls.business_day SET closed_by = 0;"));

        // Next day: charges, receipts, a room night — none changes the closed date's record or its recomputed report.
        await h.Billing.PostChargeAsync(h.As(h.Reception), Hotel.Key(), new PostChargeRequest(stays[1].FolioId, "Laundry service", 50000), Ct);
        await h.PayAsync(stays[1].FolioId, 70000, "Card", ct: Ct);
        await h.CloseAsync(date: D25, ct: Ct);
        var after = await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct);
        Assert.Equal(before.Sha256, after.Sha256);
        Assert.Equal(before.Record.Figures.Receipts.NetPaise, after.Record.Figures.Receipts.NetPaise);
        var report = await h.Reports.GetDailyReportAsync(h.As(h.Owner), D24, D25, Ct);
        Assert.All(report.DayStatus.Where(d => d.Status == "Closed"), d => Assert.True(d.MatchesSnapshot));
        Assert.Equal((120000L, 70000L), (report.Report.Days[0].NetReceiptsPaise, report.Report.Days[1].NetReceiptsPaise));

        // The integrity alarm: a back-door edit of a closed date's records shows as a mismatch.
        await h.ScalarAsync($"INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES ({stays[2].FolioId}, 'payment', 'UPI', 100, '2026-10-24');", Ct);
        var tampered = await h.Reports.GetDailyReportAsync(h.As(h.Owner), D24, D24, Ct);
        Assert.False(tampered.DayStatus.Single().MatchesSnapshot);
    }

    [Fact]
    public async Task Cash_book_lock_is_a_checkers_job_blocks_corrections_and_only_the_owner_reopens_with_a_reason()
    {
        var (h, stays) = await HotelAsync(2);
        await using var __ = h;
        var (till, shift) = await h.OpenTillAsync(500000, Ct);
        await h.PayAsync(stays[0].FolioId, 100000, "Cash", till, ct: Ct);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.Closing.LockCashBookDayAsync(h.As(h.Accountant), Hotel.Key(), new LockCashBookDayRequest(D24), Ct));
        await h.CountExactAsync(shift.ShiftId, Ct);
        await h.CloseAsync(who: h.Manager, ct: Ct);

        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.Closing.LockCashBookDayAsync(h.As(h.Cashier), Hotel.Key(), new LockCashBookDayRequest(D24), Ct));  // no checker grant
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.Closing.LockCashBookDayAsync(h.As(h.Manager), Hotel.Key(), new LockCashBookDayRequest(D24), Ct));  // closed it
        var locked = (await h.Closing.LockCashBookDayAsync(h.As(h.Accountant), Hotel.Key(), new LockCashBookDayRequest(D24), Ct)).Value;
        Assert.True(locked.Locked);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.Closing.LockCashBookDayAsync(h.As(h.Owner), Hotel.Key(), new LockCashBookDayRequest(D24), Ct));

        // DEC-065: a correction touching the locked day is refused until the Owner reopens it.
        var night = await h.LongAsync($"SELECT charge_id FROM bill.charge WHERE folio_id = {stays[0].FolioId} AND kind = 'room' AND service_date = '2026-10-24'", Ct);
        async Task<CommandResultOf> CorrectAsync()
        {
            var v = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), stays[0].FolioId, Ct)).Version;
            var r = await h.Billing.RequestAdjustmentAsync(h.As(h.Owner), Hotel.Key(), new RequestAdjustmentRequest("correction", stays[0].FolioId, night, 200000, "Rate keyed wrongly on 24 Oct", v), Ct);
            return new CommandResultOf(r.Value.Outcome);
        }

        var blocked = await Expect.Fails(HccErrorCode.DAY_CLOSED, CorrectAsync);
        Assert.Contains("Cash book 2026-10-24 is locked", blocked.Message, StringComparison.Ordinal);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.Closing.ReopenCashBookDayAsync(h.As(h.Accountant), Hotel.Key(), new ReopenCashBookDayRequest(D24, "Need a fix"), Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => h.Closing.ReopenCashBookDayAsync(h.As(h.Owner), Hotel.Key(), new ReopenCashBookDayRequest(D24, "  "), Ct));
        await h.Closing.ReopenCashBookDayAsync(h.As(h.Owner), Hotel.Key(), new ReopenCashBookDayRequest(D24, "Room rate correction for HCC-S guest"), Ct);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.Closing.ReopenCashBookDayAsync(h.As(h.Owner), Hotel.Key(), new ReopenCashBookDayRequest(D24, "again"), Ct));
        Assert.Equal("PendingApproval", (await CorrectAsync()).Outcome);

        var day = await h.Closing.GetCashBookDayAsync(h.As(h.Accountant), D24, Ct);
        Assert.Equal((true, false), (day.Closed, day.Locked));
        Assert.Equal(["lock", "reopen"], day.History.Select(e => e.Action));
        Assert.Equal("Room rate correction for HCC-S guest", day.History[1].Reason);
        Assert.Equal(600000L, day.Drawer!.CountedPaise);
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CASHBOOK_REOPENED' AND detail LIKE N'%Room rate correction%'", Ct));
    }

    private sealed record CommandResultOf(string Outcome);
}
