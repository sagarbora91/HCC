using Hcc.Application.Catalogue;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts.Catalogue;
using Hcc.Contracts.Closing;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// MA-11 review (MA-2 gap): the server night run posts the meal-plan lines after the room nights, and a plan bundled with the room gives the
/// room night the slab of room + meals (prototype catPlanNight; illustrative — CA to confirm). Real composition, disposable database.
/// </summary>
[Trait("Category", "Sql")]
public sealed class MealPlanNightCloseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task PlanAsync(Hotel h, string code, string gst, long addOnPaise)
    {
        var cat = h.Get<CatalogueCommands>();
        await cat.SaveMealPlanAsync(h.Ctx(h.Owner, CatalogueCommands.OpSaveMealPlan),
            new SaveMealPlanRequest(null, code, code + " plan", ["B"], gst, null, [new DatedAmount("2026-10-24", addOnPaise)]), Ct);
    }

    private static async Task SetPlanAsync(Hotel h, long reservationId, string code) =>
        await h.Get<CatalogueCommands>().SetReservationMealPlanAsync(h.Ctx(h.Owner, "SetReservationMealPlan"), new SetReservationMealPlanRequest(reservationId, code), Ct);

    [Fact]
    public async Task The_close_posts_plan_lines_after_the_room_nights_and_a_bundled_plan_lifts_the_room_night_slab()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await PlanAsync(h, "BB", "bundled", 30000);   // ₹300 per guest per night, bundled with the room
        await PlanAsync(h, "HB", "separate", 20000);  // ₹200, a separate restaurant line
        var (bundledRes, _, bundledFolio) = await h.SeedInHouseAsync("101", "2026-10-24", "2026-10-26", 720000, Ct);  // ₹7,200 + 2 × ₹300 = ₹7,800
        var (separateRes, _, separateFolio) = await h.SeedInHouseAsync("102", "2026-10-24", "2026-10-26", 720000, Ct);
        var (_, _, plainFolio) = await h.SeedInHouseAsync("103", "2026-10-24", "2026-10-26", 720000, Ct);
        await SetPlanAsync(h, bundledRes, "BB");
        await SetPlanAsync(h, separateRes, "HB");

        var run = (await h.CloseAsync(ct: Ct)).Value;
        Assert.Equal("Completed", run.Status);

        long Bps(long folio, string kind) => h.LongAsync($"SELECT tax_bps FROM bill.charge WHERE folio_id = {folio} AND kind = '{kind}' AND service_date = '2026-10-24'", Ct).GetAwaiter().GetResult();
        var plain = Bps(plainFolio, "room");
        var bundledRoom = Bps(bundledFolio, "room");
        Assert.True(bundledRoom > plain, $"bundled room night {bundledRoom} bps should take the slab of room + meals above plain {plain} bps");
        Assert.Equal(bundledRoom, Bps(bundledFolio, "service"));            // the plan line takes the same slab
        Assert.Equal(plain, Bps(separateFolio, "room"));                     // a separate plan does not touch the room night
        Assert.Equal(60000, await h.LongAsync($"SELECT amount_paise FROM bill.charge WHERE folio_id = {bundledFolio} AND kind = 'service'", Ct));
        Assert.Equal(40000, await h.LongAsync($"SELECT amount_paise FROM bill.charge WHERE folio_id = {separateFolio} AND kind = 'service'", Ct));
        Assert.Equal(2, await h.LongAsync("SELECT COUNT(*) FROM bill.meal_plan_night WHERE service_date = '2026-10-24'", Ct));
        Assert.Equal(0, await h.LongAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {plainFolio} AND kind = 'service'", Ct));
        Assert.Contains("\"planLines\":2", (string)(await h.ScalarAsync("SELECT detail FROM cls.close_step WHERE step = 'nights'", Ct))!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostRoomNights_on_its_own_also_posts_the_plan_lines_once()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await PlanAsync(h, "HB", "separate", 20000);
        var (res, _, folio) = await h.SeedInHouseAsync("104", "2026-10-24", "2026-10-26", 200000, Ct);
        await SetPlanAsync(h, res, "HB");
        var closing = h.Closing;
        await closing.PostRoomNightsAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightsRequest(Hotel.Day1), Ct);
        await closing.PostRoomNightsAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightsRequest(Hotel.Day1), Ct);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folio} AND kind = 'service'", Ct));
        Assert.Equal("Completed", (await h.CloseAsync(ct: Ct)).Value.Status);
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folio} AND kind = 'service'", Ct));
    }
}
