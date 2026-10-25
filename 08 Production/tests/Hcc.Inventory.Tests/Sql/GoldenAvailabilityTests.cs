using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Dates;
using Hcc.Domain.Inventory;
using Hcc.Inventory.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Inventory.Tests.Sql;

/// <summary>The prototype BASE scenario loaded into SQL (property 1) with the clock at the prototype's simulated 08:00 IST.</summary>
public sealed class GoldenDatabaseFixture : IAsyncLifetime
{
    private InventoryDatabase? _db;

    public GoldenInventory Golden { get; } = GoldenInventory.Load();

    public InventoryDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public P2Services Services { get; private set; } = null!;

    public Dictionary<string, int> RoomIds { get; } = [];

    public Dictionary<string, int> TypeIds { get; } = [];

    public async ValueTask InitializeAsync()
    {
        _db = await InventoryDatabase.CreateAsync();
        await Golden.SeedAsync(_db);
        Services = new P2Services(_db, new TestClock(new DateTimeOffset(GoldenInventory.LocalToUtc(Golden.Raw.Clock), TimeSpan.Zero)), Golden.Raw.TaxBps);
        await using var conn = await _db.Platform.Connections.OpenAsync();
        await using (var q = new SqlCommand("SELECT room_no, room_id FROM inv.room;", conn))
        await using (var rd = await q.ExecuteReaderAsync())
            while (await rd.ReadAsync()) RoomIds[rd.GetString(0)] = rd.GetInt32(1);
        await using (var q = new SqlCommand("SELECT code, room_type_id FROM inv.room_type;", conn))
        await using (var rd = await q.ExecuteReaderAsync())
            while (await rd.ReadAsync()) TypeIds[rd.GetString(0)] = rd.GetInt32(1);
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}

/// <summary>
/// Golden figures: GetAvailability, group-hold assignment, QuoteStay and CreateReservation outcomes reproduce the prototype
/// (03 Prototype resSearch / grpHolds / rateFor+taxRateFor / BOOK) on the BASE fixture exactly.
/// </summary>
[Trait("Category", "Sql")]
public sealed class GoldenAvailabilityTests(GoldenDatabaseFixture f) : IClassFixture<GoldenDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Availability_reasons_match_the_prototype_search_for_every_room()
    {
        Assert.Equal(6, f.Golden.Expected.Searches.Count);
        foreach (var s in f.Golden.Expected.Searches)
        {
            var q = s.Query;
            var result = await f.Services.Inventory.GetAvailabilityAsync(1, FakeAccessPolicy.Reception,
                new AvailabilityQuery(q.Arrival, q.Departure, q.Adults, q.Children, q.Type is null ? null : f.TypeIds[q.Type]), Ct);
            var label = $"{q.Arrival}→{q.Departure} ×{q.Adults} {q.Type ?? "all"}";
            Assert.Equal(s.Rooms.Select(r => r.RoomNo).Order(StringComparer.Ordinal), result.Rooms.Select(r => r.RoomNo).Order(StringComparer.Ordinal));
            foreach (var expected in s.Rooms)
            {
                var actual = result.Rooms.Single(r => r.RoomNo == expected.RoomNo);
                Assert.True(expected.Available == actual.Available, $"{label} room {expected.RoomNo}: available {actual.Available}, prototype {expected.Available}");
                Assert.True(expected.Reasons.SequenceEqual(actual.Reasons), $"{label} room {expected.RoomNo}: [{string.Join(",", actual.Reasons)}] vs prototype [{string.Join(",", expected.Reasons)}]");
            }
        }
    }

    [Fact]
    public async Task Group_holds_assign_the_same_rooms_as_the_prototype()
    {
        var bd = StayDates.Parse(f.Golden.Raw.BusinessDate);
        var snap = await f.Services.Store.LoadSnapshotAsync(1, bd, bd.AddDays(1), f.Services.Clock.GetUtcNow().UtcDateTime, null, Ct);
        var types = f.TypeIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var actual = AvailabilityRules.GroupHolds(snap).Select(h => $"{h.RoomNo}|{h.BlockCode}|{types[h.RoomTypeId]}|{StayDates.ToWire(h.From)}|{StayDates.ToWire(h.To)}").ToList();
        var expected = f.Golden.Expected.Holds.Select(h => $"{h.RoomNo}|{h.Block}|{h.Type}|{h.From}|{h.To}").ToList();
        Assert.Equal(18, expected.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Group_held_rooms_report_their_block_and_are_free_for_the_block_itself()
    {
        var deluxe = f.TypeIds["Deluxe"];
        var gb4 = Convert.ToInt64(await f.Db.ScalarAsync("SELECT group_block_id FROM inv.group_block WHERE code = 'GB-4';", Ct), System.Globalization.CultureInfo.InvariantCulture);
        var others = await f.Services.Inventory.GetAvailabilityAsync(1, FakeAccessPolicy.Reception, new AvailabilityQuery("2026-10-25", "2026-10-28", 1, 0, deluxe), Ct);
        var own = await f.Services.Inventory.GetAvailabilityAsync(1, FakeAccessPolicy.Reception, new AvailabilityQuery("2026-10-25", "2026-10-28", 1, 0, deluxe, null, gb4), Ct);
        foreach (var no in new[] { "303", "304", "307" })
        {
            Assert.Equal("GB-4", others.Rooms.Single(r => r.RoomNo == no).HeldForBlock);
            Assert.False(others.Rooms.Single(r => r.RoomNo == no).Available);
            Assert.True(own.Rooms.Single(r => r.RoomNo == no).Available, $"block context frees its own held room {no}");
        }

        Assert.Equal(3, others.Types.Single().AvailableRooms);
        Assert.Equal(6, own.Types.Single().AvailableRooms);
    }

    [Fact]
    public async Task Quotes_reproduce_the_prototype_nightly_rate_tax_and_totals()
    {
        foreach (var c in f.Golden.Expected.QuoteCases)
        {
            var q = (await f.Services.Inventory.QuoteStayAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "QuoteStay"),
                new QuoteStayRequest(c.Arrival, c.Departure, 1, 0, f.RoomIds[c.RoomNo]), Ct)).Value;
            Assert.Equal(c.Nights.Select(n => (n.Date, n.RatePaise, n.TaxBps, n.TaxPaise)), q.Nights.Select(n => (n.Date, n.RatePaise, n.TaxBps, n.TaxPaise)));
            Assert.Equal((c.NetPaise, c.TaxPaise, c.TotalPaise), (q.NetPaise, q.TaxPaise, q.TotalPaise));
            Assert.Equal(c.Violations, q.Violations.Select(v => v.Code));
            Assert.Equal(f.TypeIds[c.Type], q.RoomTypeId);
            Assert.Equal(f.Services.Clock.GetUtcNow().UtcDateTime.AddMinutes(30).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture), q.ExpiresAt);
        }
    }

    [Fact]
    public async Task Booking_outcomes_match_the_prototype_BOOK_command()
    {
        Assert.Equal(16, f.Golden.Expected.BookCases.Count);
        foreach (var c in f.Golden.Expected.BookCases)
        {
            var principal = c.Uid == "manager" ? FakeAccessPolicy.Manager : FakeAccessPolicy.Reception;
            ReservationResult? made = null;
            var code = await Outcome.CodeAsync(async () =>
            {
                var quote = (await f.Services.Inventory.QuoteStayAsync(P2Services.Ctx(principal, "QuoteStay"),
                    new QuoteStayRequest(c.Cmd.Arrival, c.Cmd.Departure, c.Cmd.People, 0, f.RoomIds[c.Cmd.Room]), Ct)).Value;
                made = (await f.Services.Booking.CreateAsync(P2Services.Ctx(principal, "CreateReservation"),
                    new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(c.Cmd.Guest), f.RoomIds[c.Cmd.Room], c.Cmd.Hold ?? false,
                        c.Cmd.RestrictionOverride), Ct)).Value;
            });
            Assert.True(c.Result.Code == code, $"{c.Name}: {code}, prototype {c.Result.Code}");
            if (made is null) continue;
            Assert.Equal(GoldenInventory.Status(c.Result.Status!), made.Status);
            Assert.Equal(c.Result.HoldUntil is null ? null : GoldenInventory.LocalToUtc(c.Result.HoldUntil).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture), made.HoldUntil);
            if (c.Cmd.RestrictionOverride is not null)
            {
                var d = await f.Services.Booking.GetAsync(1, principal, made.ReservationId, Ct);
                Assert.Equal("restriction overridden (MIN_STAY YLDR-1): Regular corporate guest", d.RestrictionOverride);
            }

            // Undo so every case starts from the BASE state, as the prototype test runs each case on a fresh copy.
            await f.Services.Booking.CancelAsync(P2Services.Ctx(FakeAccessPolicy.Owner, "CancelReservation"), new CancelReservationRequest(made.ReservationId, made.Version), Ct);
        }

        Assert.Equal(0, await f.Services.DriftAsync());
    }
}
