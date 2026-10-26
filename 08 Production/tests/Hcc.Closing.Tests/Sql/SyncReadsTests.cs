using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Closing;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// Wave 4 sync reads of the closing module: the standalone night posting (PostRoomNights, prototype CLOSE_RUN), the business date for any
/// signed-in user, and the bulk closing records read behind the closing history.
/// </summary>
[Trait("Category", "Sql")]
public sealed class SyncReadsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D24 = new(2026, 10, 24);
    private static readonly DateOnly D25 = new(2026, 10, 25);

    private static async Task<Hotel> HotelAsync()
    {
        var h = await Hotel.CreateAsync(Ct);
        foreach (var room in new[] { "101", "102", "103" }) await h.SeedInHouseAsync(room, "2026-10-23", "2026-10-28", ct: Ct);
        return h;
    }

    private static Task<long> Nights(Hotel h, string date) => h.LongAsync($"SELECT COUNT(*) FROM bill.charge WHERE kind = 'room' AND service_date = '{date}'", Ct);

    [Fact]
    public async Task PostRoomNights_posts_each_stay_once_without_closing_and_the_close_posts_nothing_again()
    {
        await using var h = await HotelAsync();
        var first = (await h.Closing.PostRoomNightsAsync(h.As(h.Manager), Hotel.Key(), new PostRoomNightsRequest(D24), Ct)).Value;
        Assert.Equal((3, 3, 0), (first.Stays, first.PostedNow, first.AlreadyPosted));
        Assert.Equal(3, await Nights(h, "2026-10-24"));
        Assert.Equal(D24, await h.BusinessDateAsync(Ct)); // no close, no date change

        var again = (await h.Closing.PostRoomNightsAsync(h.As(h.Manager), Hotel.Key(), new PostRoomNightsRequest(D24), Ct)).Value;
        Assert.Equal((3, 0, 3), (again.Stays, again.PostedNow, again.AlreadyPosted));

        var closed = (await h.CloseAsync(ct: Ct)).Value;
        Assert.Equal(("Completed", D25), (closed.Status, closed.NextBusinessDate!.Value));
        Assert.Equal(3, await Nights(h, "2026-10-24"));
    }

    [Fact]
    public async Task PostRoomNights_needs_the_close_permission_and_only_the_current_open_business_date()
    {
        await using var h = await HotelAsync();
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.Closing.PostRoomNightsAsync(h.As(h.Reception), Hotel.Key(), new PostRoomNightsRequest(D24), Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => h.Closing.PostRoomNightsAsync(h.As(h.Manager), Hotel.Key(), new PostRoomNightsRequest(D25), Ct));
        Assert.Equal(0, await Nights(h, "2026-10-24"));
        await h.CloseAsync(ct: Ct);
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => h.Closing.PostRoomNightsAsync(h.As(h.Manager), Hotel.Key(), new PostRoomNightsRequest(D24), Ct));
    }

    [Fact]
    public async Task Business_date_is_readable_by_any_user_and_records_come_in_one_range_read_for_report_viewers_only()
    {
        await using var h = await HotelAsync();
        var open = await h.Closing.GetBusinessDateAsync(h.As(h.Reception), Ct);
        Assert.Equal((D24, false), (open.BusinessDate, open.Closed));
        Assert.Empty((await h.Closing.GetRecordsAsync(h.As(h.Owner), D24, D25, Ct)).Items);

        await h.CloseAsync(ct: Ct);
        var next = await h.Closing.GetBusinessDateAsync(h.As(h.Cashier), Ct);
        Assert.Equal((D25, false), (next.BusinessDate, next.Closed));
        var records = await h.Closing.GetRecordsAsync(h.As(h.Owner), D24, D25, Ct);
        var one = Assert.Single(records.Items);
        Assert.Equal(D24, one.Record.BusinessDate);
        Assert.Equal((await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct)).Sha256, one.Sha256);

        await Expect.Fails(HccErrorCode.VALIDATION, () => h.Closing.GetRecordsAsync(h.As(h.Owner), D25, D24, Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => h.Closing.GetRecordsAsync(h.As(h.Owner), D24, D24.AddDays(100), Ct));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => h.Closing.GetRecordsAsync(h.As(h.Reception), D24, D25, Ct));
    }
}
