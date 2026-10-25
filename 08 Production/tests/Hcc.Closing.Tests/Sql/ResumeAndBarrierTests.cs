using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Closing;
using Hcc.Contracts.Housekeeping;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Stays;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Closing.Tests.Sql;

/// <summary>Crash-resume of every step and the late-write barrier across P2/P3/P4 (DAY_CLOSED), including writers racing the close.</summary>
[Trait("Category", "Sql")]
public sealed class ResumeAndBarrierTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D24 = new(2026, 10, 24);
    private static readonly DateOnly D25 = new(2026, 10, 25);

    private static async Task<(Hotel H, List<(long ReservationId, long StayId, long FolioId)> Stays)> HotelAsync()
    {
        var h = await Hotel.CreateAsync(Ct);
        var stays = new List<(long, long, long)>();
        foreach (var room in new[] { "101", "102", "103" }) stays.Add(await h.SeedInHouseAsync(room, "2026-10-23", "2026-10-28", ct: Ct));
        return (h, stays);
    }

    private static Task<long> Count(Hotel h, string sql) => h.LongAsync(sql, Ct);

    [Fact]
    public async Task Crash_in_preflight_or_inside_the_close_transaction_leaves_the_day_open_and_a_rerun_completes()
    {
        var (h, stays) = await HotelAsync();
        await using var _ = h;
        h.Observer.OnStep = (p, _, _) => p == "preflight" ? throw new SimulatedCrashException() : Task.CompletedTask;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => h.CloseAsync(ct: Ct));
        var run = await h.Closing.GetRunAsync(h.As(h.Manager), D24, Ct);
        Assert.Equal(("Running", "preflight", 1), (run.Status, run.CurrentStep, run.Attempts));

        // Crash after the barrier row was written, inside the close transaction: everything rolls back with it.
        h.Observer.OnStep = (p, _, _) => p == "barrier" ? throw new SimulatedCrashException() : Task.CompletedTask;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => h.CloseAsync(ct: Ct));
        Assert.Equal(0, await Count(h, "SELECT COUNT(*) FROM cls.business_day"));
        Assert.Equal(0, await Count(h, "SELECT COUNT(*) FROM cls.closing_snapshot"));
        Assert.Equal(3, await Count(h, "SELECT COUNT(*) FROM bill.charge WHERE kind = 'room' AND service_date = '2026-10-24'"));
        Assert.Equal(D24, await h.BusinessDateAsync(Ct));
        await h.PayAsync(stays[0].FolioId, 10000, "UPI", ct: Ct); // the day is still open for business

        h.Observer.OnStep = null;
        var done = (await h.CloseAsync(ct: Ct)).Value;
        Assert.Equal(("Completed", 3, D25), (done.Status, done.Attempts, done.NextBusinessDate!.Value));
        Assert.Equal(["preflight", "nights", "cash", "close", "snapshot", "next-day"], done.Steps.Select(s => s.Step));
        Assert.All(done.Steps, s => Assert.Equal("Done", s.Status));
        Assert.Equal(3, await Count(h, "SELECT COUNT(*) FROM bill.charge WHERE kind = 'room' AND service_date = '2026-10-24'"));
        Assert.Equal(10000, (await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct)).Record.Figures.Receipts.NetPaise);
    }

    [Fact]
    public async Task Crash_before_the_next_day_bars_every_module_until_the_rerun_opens_the_next_date()
    {
        var (h, stays) = await HotelAsync();
        await using var _ = h;
        var till = (await h.Till.CreateTillAsync(h.As(h.Owner), Hotel.Key(), new CreateTillRequest("FD-1", "Front desk"), Ct)).Value;
        var earlierKey = Hotel.Key();
        var earlier = await h.Billing.RecordReceiptAsync(h.As(h.Cashier), earlierKey, new RecordReceiptRequest(stays[0].FolioId, "payment", "UPI", 30000, "DEMO-UPI-1", null), Ct);

        h.Observer.OnStep = (p, _, _) => p == "next-day" ? throw new SimulatedCrashException() : Task.CompletedTask;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => h.CloseAsync(ct: Ct));
        h.Observer.OnStep = null;
        Assert.Equal(D24, await h.BusinessDateAsync(Ct));
        Assert.Equal("Closed", (await h.Closing.GetRunAsync(h.As(h.Manager), D24, Ct)).Status);
        Assert.True((await h.Closing.GetPreflightAsync(h.As(h.Accountant), null, Ct)).Closed);
        var sha = (await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct)).Sha256;

        // P4 billing + cashier + shared-till port
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.PayAsync(stays[0].FolioId, 10000, "UPI"));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.Billing.PostChargeAsync(h.As(h.Reception), Hotel.Key(), new PostChargeRequest(stays[1].FolioId, "Laundry", 20000), Ct));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.Till.OpenShiftAsync(h.As(h.Cashier), Hotel.Key(), new OpenShiftRequest(till.TillId, 500000), Ct));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.DrawerAsync(till.TillId, CashMovementKind.PosSale, 52500, "Restaurant"));
        // P2 inventory + reservations
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.Inventory.QuoteStayAsync(h.Ctx(h.Reception, "QuoteStay"), new QuoteStayRequest("2026-10-24", "2026-10-25", 2, 0, h.Rooms["105"]), Ct));
        // P3 stays + housekeeping
        var resVersion = await h.VersionAsync($"SELECT row_ver FROM inv.reservation WHERE reservation_id = {stays[2].ReservationId}", Ct);
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.Stays.RegisterGuestAsync(h.Ctx(h.Reception, "RegisterGuest"), new RegisterGuestRequest(stays[2].ReservationId, resVersion), Ct));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.Housekeeping.BlockRoomAsync(h.Ctx(h.Owner, "BlockRoom"), new BlockRoomRequest(h.Rooms["110"], "Leaking tap"), Ct));
        // A replay of a command committed before the close still returns its stored result (not a new write).
        var replay = await h.Billing.RecordReceiptAsync(h.As(h.Cashier), earlierKey, new RecordReceiptRequest(stays[0].FolioId, "payment", "UPI", 30000, "DEMO-UPI-1", null), Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(earlier.Value.ReceiptId, replay.Value.ReceiptId);

        var done = (await h.CloseAsync(who: h.Owner, ct: Ct)).Value; // resumed by another closer
        Assert.Equal(("Completed", 2), (done.Status, done.Attempts));
        Assert.Equal(D25, await h.BusinessDateAsync(Ct));
        Assert.Equal(sha, (await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct)).Sha256);
        Assert.Equal(1, await Count(h, "SELECT COUNT(*) FROM cls.closing_snapshot"));

        // The next day is open; the closed date stays barred for back-dated postings.
        Assert.Equal(D25, (await h.PayAsync(stays[0].FolioId, 10000, "UPI", ct: Ct)).Value.BusinessDate);
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.Billing.PostRoomNightAsync(h.As(h.Manager), Hotel.Key(), new PostRoomNightRequest(stays[0].StayId, D24), Ct));
        Assert.True((await h.Billing.PostRoomNightAsync(h.As(h.Manager), Hotel.Key(), new PostRoomNightRequest(stays[0].StayId, D25), Ct)).Value.Created);
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.CloseAsync(date: D24.AddDays(-1), ct: Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => h.CloseAsync(date: D25.AddDays(1), ct: Ct));
        var again = await h.CloseAsync(date: D24, ct: Ct);
        Assert.True(again.Replayed);
        Assert.Equal("Completed", again.Value.Status);
    }

    [Fact]
    public async Task A_write_in_flight_when_the_close_starts_is_waited_for_and_lands_in_the_snapshot()
    {
        var (h, stays) = await HotelAsync();
        await using var _ = h;
        var store = h.Get<IBillingStore>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = h.Get<ICommandRunner>().RunAsync(h.Ctx(h.Reception, "SlowPosting"), new { folio = stays[0].FolioId }, async (tx, c) =>
        {
            await store.InsertChargeAsync(tx, new NewCharge(stays[0].FolioId, ChargeKind.Service, D24, "Laundry (slow terminal)", new Paise(50000), 1000,
                new Paise(5000), null, null, h.Reception, DateTime.UtcNow), c);
            await store.TouchFolioAsync(tx, stays[0].FolioId, c);
            await tx.AuditAsync("TEST_SLOW_POSTING", "folio:" + stays[0].FolioId, "in flight", c);
            inside.SetResult();
            await gate.Task.WaitAsync(c);
            return new Hotel.DrawerResult(0);
        }, Ct);
        await inside.Task.WaitAsync(Ct);

        var close = h.CloseAsync(ct: Ct);
        await Task.Delay(1500, Ct);
        Assert.False(close.IsCompleted, "The close must wait for the in-flight writer before it raises the barrier.");
        gate.SetResult();
        await writer;
        var done = (await close).Value;
        Assert.Equal("Completed", done.Status);
        var figures = (await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct)).Record.Figures;
        Assert.Equal(3 * 200000 + 50000, figures.Revenue.NetPaise);
    }

    [Fact]
    public async Task A_write_arriving_while_the_barrier_is_up_waits_and_then_gets_DAY_CLOSED()
    {
        var (h, stays) = await HotelAsync();
        await using var _ = h;
        var atBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Observer.OnStep = async (p, _, c) =>
        {
            if (p != "barrier") return;
            atBarrier.SetResult();
            await release.Task.WaitAsync(c);
        };
        var close = h.CloseAsync(ct: Ct);
        await atBarrier.Task.WaitAsync(Ct);
        var late = h.PayAsync(stays[1].FolioId, 25000, "UPI", ct: Ct);
        await Task.Delay(1000, Ct);
        Assert.False(late.IsCompleted, "A late writer must wait at the barrier while the close transaction runs.");
        release.SetResult();
        Assert.Equal("Completed", (await close).Value.Status);
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => late);
        Assert.Equal(0, await Count(h, "SELECT COUNT(*) FROM pay.receipt"));
        Assert.Equal(0, (await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct)).Record.Figures.Receipts.NetPaise);
    }

    [Fact]
    public async Task Only_owner_and_manager_close_the_day_and_blockers_are_listed()
    {
        var (h, _) = await HotelAsync();
        await using var __ = h;
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.CloseAsync(who: h.Reception, ct: Ct));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.CloseAsync(who: h.Accountant, ct: Ct));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.Closing.GetPreflightAsync(h.As(h.Reception), null, Ct)); // DEC-063 G8
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.Reports.GetDailyReportAsync(h.As(h.Cashier), D24, D24, Ct)); // own-shift only (V·S)

        // A pending arrival blocks at preflight (persisted as Blocked); the night step never ran.
        await h.ScalarAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id) VALUES ({h.PropertyId}, 'HCC-1049', N'Arrival (fictional)', '2026-10-24', '2026-10-25', 'Confirmed', {h.Rooms["110"]});", Ct);
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.CloseAsync(ct: Ct));
        Assert.Equal("arrivals", ex.Details!["blockers"][0].GetProperty("id").GetString());
        var run = await h.Closing.GetRunAsync(h.As(h.Accountant), D24, Ct);
        Assert.Equal(("Blocked", "preflight"), (run.Status, run.CurrentStep));
        Assert.Equal(0, await Count(h, "SELECT COUNT(*) FROM bill.charge"));
        var pre = await h.Closing.GetPreflightAsync(h.As(h.Accountant), D24, Ct);
        Assert.Equal(3, pre.Checks.Single(c => c.Id == "nights").Items.Count);
        Assert.Single(pre.Checks.Single(c => c.Id == "arrivals").Items);
    }
}
