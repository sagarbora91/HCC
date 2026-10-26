using Hcc.Application.Commands;
using Hcc.Application.FrontDeskMasters;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;

namespace Hcc.Closing.Tests.Sql;

/// <summary>MA-11 review (MA-5 gap): CreateReservation refuses a retired booking source (MASTER_RETIRED), as the prototype BOOK does.</summary>
[Trait("Category", "Sql")]
public sealed class BookingSourceRetiredTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CommandResult<ReservationResult>> BookAsync(Hotel h, string room, string source)
    {
        var quote = (await h.Inventory.QuoteStayAsync(h.Ctx(h.Reception, "QuoteStay"), new QuoteStayRequest("2026-10-24", "2026-10-25", 2, 0, h.Rooms[room]), Ct)).Value;
        return await h.Reservations.CreateAsync(h.Ctx(h.Reception, "CreateReservation"),
            new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("Source test (fictional)", "9800000123"), h.Rooms[room], Source: source), Ct);
    }

    [Fact]
    public async Task A_retired_source_is_refused_for_new_bookings_and_other_sources_still_book()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        Assert.NotNull((await BookAsync(h, "105", "Walk-in")).Value);      // list not stored yet: every source books
        // Retire Walk-in (default row -1 before the list is stored).
        await h.Get<FrontDeskMastersCommands>().RetireAsync(h.Ctx(h.Owner, "RetireBookingSource"), FrontDeskMasterIds.BookingSources, new FrontDeskRowRequest(-1), Ct);
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => BookAsync(h, "106", "walk-in"));
        Assert.Equal("MASTER_RETIRED", ex.Details!["reason"].GetString());
        Assert.Equal("Walk-in is retired — choose an active booking source.", ex.Message);
        Assert.NotNull((await BookAsync(h, "107", "Phone")).Value);
        Assert.NotNull((await BookAsync(h, "108", "OTA · Booking.com (fictional)")).Value);   // not in the list: allowed (channels register themselves)
    }
}
