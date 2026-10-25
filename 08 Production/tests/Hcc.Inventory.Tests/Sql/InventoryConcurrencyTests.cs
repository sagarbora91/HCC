using System.Globalization;
using Hcc.Application.Inventory;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Errors;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Inventory.Tests.Sql;

/// <summary>
/// SQL concurrency through the production services (spike C1–C4 rerun on the P2 code): parallel bookings of one room have
/// one winner; overlapping ranges never double-allocate; room-type capacity (sold + held ≤ sellable) holds under a race;
/// mixed create/amend/cancel traffic never deadlocks (no UNAVAILABLE) and leaves the maintained counts exact.
/// </summary>
[Trait("Category", "Sql")]
public sealed class InventoryConcurrencyTests(GoldenDatabaseFixture f) : IClassFixture<GoldenDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private P2Services S => f.Services;

    private async Task<QuoteResult> QuoteAsync(string room, DateOnly a, DateOnly d)
        => (await S.Inventory.QuoteStayAsync(P2Services.Ctx(3, "QuoteStay"),
            new QuoteStayRequest(Wire(a), Wire(d), 1, 0, f.RoomIds[room]), Ct)).Value;

    private static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<ReservationResult> CreateAsync(QuoteResult q, string room)
        => (await S.Booking.CreateAsync(P2Services.Ctx(3, "CreateReservation"),
            new CreateReservationRequest(q.QuoteId, q.Version, new GuestInput("Race Guest"), f.RoomIds[room]), Ct)).Value;

    private static async Task<(string Code, T? Value)> TryAsync<T>(Func<Task<T>> call) where T : class
    {
        try
        {
            return ("OK", await call());
        }
        catch (HccException h)
        {
            return (h.Code.ToString(), null);
        }
    }

    [Fact]
    public async Task Twelve_parallel_bookings_of_one_room_have_one_winner_and_no_deadlock()
    {
        var a = new DateOnly(2026, 11, 15);
        var quotes = new List<QuoteResult>();
        for (var i = 0; i < 12; i++) quotes.Add(await QuoteAsync("105", a, a.AddDays(2)));
        var runs = await Task.WhenAll(quotes.Select(q => Task.Run(() => TryAsync(() => CreateAsync(q, "105")), Ct)));
        Assert.Equal(1, runs.Count(r => r.Code == "OK"));
        Assert.Equal(11, runs.Count(r => r.Code == "NOT_AVAILABLE"));
        Assert.Equal(2, await f.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE room_id = {f.RoomIds["105"]} AND night IN ('2026-11-15', '2026-11-16');", Ct));
        Assert.Equal(1, await f.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation WHERE guest_name = N'Race Guest' AND arrival = '2026-11-15';", Ct));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Overlapping_ranges_on_one_room_never_double_allocate()
    {
        var start = new DateOnly(2026, 12, 1);
        var quotes = new List<(QuoteResult Q, DateOnly A)>();
        for (var i = 0; i < 10; i++) quotes.Add((await QuoteAsync("108", start.AddDays(i), start.AddDays(i + 3)), start.AddDays(i)));
        var runs = await Task.WhenAll(quotes.Select(x => Task.Run(async () => (x.A, R: await TryAsync(() => CreateAsync(x.Q, "108"))), Ct)));
        Assert.DoesNotContain(runs, r => r.R.Code is not ("OK" or "NOT_AVAILABLE"));
        var winners = runs.Where(r => r.R.Code == "OK").Select(r => r.A).Order().ToList();
        Assert.NotEmpty(winners);
        for (var i = 1; i < winners.Count; i++) Assert.True(winners[i].DayNumber - winners[i - 1].DayNumber >= 3, "winning stays do not overlap");
        Assert.Equal(winners.Count * 3, await f.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE room_id = {f.RoomIds["108"]} AND night >= '2026-12-01' AND night < '2026-12-20';", Ct));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Room_type_capacity_holds_under_a_race_for_the_last_unit()
    {
        var suite = f.TypeIds["Suite"];
        await S.Inventory.CreateGroupBlockAsync(P2Services.Ctx(3, "CreateGroupBlock"), new CreateGroupBlockRequest("GB-RACE", "Race block", "2027-01-10", "2027-01-11",
            "2027-01-05", [new GroupBlockAllotment(suite, 2)]), Ct);
        var ids = new List<long>();
        for (var i = 0; i < 3; i++)
            ids.Add(Convert.ToInt64(await f.Db.ScalarAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) OUTPUT INSERTED.reservation_id VALUES (1, 'RACE-{i}', N'Count race', '2027-01-10', '2027-01-11', 'Confirmed');", Ct), CultureInfo.InvariantCulture));
        var rooms = new[] { "106", "206", "306" };
        var runs = await Task.WhenAll(ids.Select((id, i) => Task.Run(() => TryAsync(async () => (object)await S.AllocateAsync(id, null,
            new StayAllocation(f.RoomIds[rooms[i]], suite, new DateOnly(2027, 1, 10), new DateOnly(2027, 1, 11)))), Ct)));
        Assert.Equal(1, runs.Count(r => r.Code == "OK"));
        Assert.Equal(2, runs.Count(r => r.Code == "NOT_AVAILABLE"));
        Assert.Equal("3|1|2", (string)(await f.Db.ScalarAsync($"SELECT CONCAT(sellable, '|', sold, '|', held) FROM inv.type_night_count WHERE room_type_id = {suite} AND night = '2027-01-10';", Ct))!);
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Mixed_create_amend_cancel_traffic_never_deadlocks_and_keeps_counts_exact()
    {
        string[] rooms = ["103", "104", "107", "203", "204"];
        var start = new DateOnly(2027, 1, 1);
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(w => Task.Run(async () =>
        {
            var rnd = new Random(1000 + w);
            var mine = new List<ReservationResult>();
            for (var op = 0; op < 8; op++)
            {
                var room = rooms[rnd.Next(rooms.Length)];
                var a = start.AddDays(rnd.Next(10));
                var d = a.AddDays(1 + rnd.Next(3));
                var kind = mine.Count == 0 ? 0 : rnd.Next(3);
                string code;
                if (kind == 0)
                {
                    var q = await QuoteAsync(room, a, d);
                    var (c, r) = await TryAsync(() => CreateAsync(q, room));
                    if (r is not null) mine.Add(r);
                    code = c;
                }
                else
                {
                    var target = mine[rnd.Next(mine.Count)];
                    mine.Remove(target);
                    if (kind == 1)
                    {
                        var (c, r) = await TryAsync(async () => (await S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
                            new AmendReservationRequest(target.ReservationId, target.Version, Wire(a), Wire(d), f.RoomIds[room]), Ct)).Value);
                        mine.Add(r ?? target);
                        code = c;
                    }
                    else
                    {
                        (code, _) = await TryAsync(async () => (await S.Booking.CancelAsync(P2Services.Ctx(3, "CancelReservation"),
                            new CancelReservationRequest(target.ReservationId, target.Version), Ct)).Value);
                    }
                }

                outcomes.Add(code);
            }
        }, Ct)));

        Assert.Equal(48, outcomes.Count);
        Assert.DoesNotContain("UNAVAILABLE", outcomes);
        Assert.All(outcomes, c => Assert.Contains(c, new[] { "OK", "NOT_AVAILABLE", "VALIDATION" }));
        Assert.Contains("OK", outcomes);
        Assert.Equal(0, await S.DriftAsync());
        Assert.Equal(0, await f.Db.CountAsync("""
            SELECT COUNT(*) FROM inv.reservation r
            WHERE r.arrival >= '2027-01-01' AND r.status = 'Confirmed' AND r.guest_name = N'Race Guest'
              AND (SELECT COUNT(*) FROM inv.room_night n WHERE n.reservation_id = r.reservation_id AND n.room_id = r.room_id AND n.night >= r.arrival AND n.night < r.departure)
                  <> DATEDIFF(day, r.arrival, r.departure);
            """, Ct));
        Assert.Equal(0, await f.Db.CountAsync("SELECT COUNT(*) FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id WHERE r.status NOT IN ('Held','Confirmed','InHouse','Departed');", Ct));
    }
}
