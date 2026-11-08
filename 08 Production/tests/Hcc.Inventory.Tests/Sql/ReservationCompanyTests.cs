using Hcc.Application.Commands;
using Hcc.Application.Reservations;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Errors;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Inventory.Tests.Sql;

/// <summary>
/// W4 gap G5: the company (name + GSTIN) lives on the reservation — accepted by CreateReservation / AmendReservation, validated,
/// returned by GET /reservations/{id} and GET /reservations. Rooms 301–302 in May 2027 are private to this class.
/// </summary>
[Trait("Category", "Sql")]
public sealed class ReservationCompanyTests(GoldenDatabaseFixture f) : IClassFixture<GoldenDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Gstin = "27ZZFIC0009F1Z5";

    private P2Services S => f.Services;

    private async Task<ReservationResult> BookAsync(string room, string a, string d, CompanyInput? company)
    {
        var q = (await S.Inventory.QuoteStayAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "QuoteStay"), new QuoteStayRequest(a, d, 2, 0, f.RoomIds[room]), Ct)).Value;
        return (await S.Booking.CreateAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "CreateReservation"),
            new CreateReservationRequest(q.QuoteId, q.Version, new GuestInput("Company Guest", "+91 90000 12345"), f.RoomIds[room], Company: company), Ct)).Value;
    }

    private Task<ReservationDetails> DetailsAsync(long id) => S.Booking.GetAsync(1, FakeAccessPolicy.Reception, id, Ct);

    private Task<CommandResult<ReservationResult>> AmendAsync(ReservationResult r, string room, string a, string d, CompanyInput? company)
        => S.Booking.AmendAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "AmendReservation"),
            new AmendReservationRequest(r.ReservationId, r.Version, a, d, f.RoomIds[room], Company: company), Ct);

    [Fact]
    public async Task Company_is_stored_on_create_and_returned_by_details_and_the_list()
    {
        var with = await BookAsync("301", "2027-05-03", "2027-05-05", new CompanyInput("  Acme Traders Pvt Ltd ", Gstin.ToLowerInvariant()));
        var none = await BookAsync("302", "2027-05-03", "2027-05-05", null);
        var nameOnly = await BookAsync("303", "2027-05-03", "2027-05-05", new CompanyInput("Name Only Co"));

        var d = await DetailsAsync(with.ReservationId);
        Assert.Equal(new ReservationCompany("Acme Traders Pvt Ltd", Gstin), d.Company);
        Assert.Null((await DetailsAsync(none.ReservationId)).Company);
        Assert.Equal(new ReservationCompany("Name Only Co", null), (await DetailsAsync(nameOnly.ReservationId)).Company);

        var list = await S.Booking.ListAsync(1, FakeAccessPolicy.Reception, "range", "2027-05-01", "2027-05-10", null, null, 1, 100, Ct);
        Assert.Equal(new ReservationCompany("Acme Traders Pvt Ltd", Gstin), list.Items.Single(i => i.ReservationId == with.ReservationId).Company);
        Assert.Null(list.Items.Single(i => i.ReservationId == none.ReservationId).Company);
        Assert.Equal(new ReservationCompany("Name Only Co", null), list.Items.Single(i => i.ReservationId == nameOnly.ReservationId).Company);
    }

    [Fact]
    public async Task Bad_company_is_refused_before_anything_is_written()
    {
        foreach (var bad in new[] { new CompanyInput("A"), new CompanyInput("Good Name", "NOT-A-GSTIN"), new CompanyInput("", Gstin), new CompanyInput(new string('x', 201)) })
        {
            var e = await Assert.ThrowsAsync<HccException>(() => BookAsync("304", "2027-05-12", "2027-05-13", bad));
            Assert.Equal(HccErrorCode.VALIDATION, e.Code);
        }

        // Nothing was booked: the room is still free on those dates.
        var ok = await BookAsync("304", "2027-05-12", "2027-05-13", new CompanyInput("Fine Co", Gstin));
        Assert.Equal("Fine Co", (await DetailsAsync(ok.ReservationId)).Company!.Name);
    }

    private async Task<long> AccountAsync(string name, long limitPaise)
        => Convert.ToInt64(await f.Db.ScalarAsync($"INSERT grp.company_account(property_id, name, gstin, credit_limit_paise, created_by, created_utc) OUTPUT INSERTED.company_id VALUES (1, N'{name}', '{Gstin}', {limitPaise}, 1, '2026-10-24');", Ct),
            System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_booking_billed_to_a_company_account_is_a_credit_guarantee_refused_beyond_the_limit()
    {
        // DEC-076. A Classic room at the Rs 2,000 rack rate + 5 % GST (DEC-069 master: <= Rs 7,500 a night) for 7–9 Jun 2027 = 2 × Rs 2,100 = Rs 4,200.
        var co = await AccountAsync("Kale Textiles (fictional)", 500_000); // Rs 5,000
        var first = await BookAsync("301", "2027-06-07", "2027-06-09", new CompanyInput("", null, co));
        Assert.Equal(new ReservationCompany("Kale Textiles (fictional)", Gstin, co), (await DetailsAsync(first.ReservationId)).Company); // the account's name and GSTIN
        var e1 = await S.Credit.ExposureAsync(null, 1, co, Ct);
        Assert.Equal((420_000L, 420_000L), (e1.Total.Value, e1.OpenStays.Value));

        // A second Rs 4,200 booking: used Rs 4,200, remaining Rs 800 → refused, and nothing is booked.
        var ex = await Assert.ThrowsAsync<HccException>(() => BookAsync("302", "2027-06-07", "2027-06-09", new CompanyInput("Kale", null, co)));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ex.Code);
        Assert.Equal("Credit limit exceeded for Kale Textiles (fictional): limit ₹5,000, already used by other business ₹4,200, headroom ₹800; this booking needs ₹4,200. "
            + "Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).", ex.Message);
        // The same company typed as invoice details only (no account) is the guest's bill: not credit-checked.
        var guestPays = await BookAsync("302", "2027-06-07", "2027-06-09", new CompanyInput("Kale Textiles (fictional)", Gstin));
        Assert.Null((await DetailsAsync(guestPays.ReservationId)).Company!.CompanyId);
        Assert.Equal(420_000L, (await S.Credit.ExposureAsync(null, 1, co, Ct)).Total.Value);

        // Amend: one more night (3 × Rs 2,100 = Rs 6,300 > Rs 5,000) is refused; one night less (Rs 2,100) always passes.
        var longer = await Assert.ThrowsAsync<HccException>(() => AmendAsync(first, "301", "2027-06-07", "2027-06-10", null));
        Assert.Contains("already used by other business ₹0, headroom ₹5,000; this booking needs ₹6,300", longer.Message, StringComparison.Ordinal);
        var shorter = (await AmendAsync(first, "301", "2027-06-07", "2027-06-08", null)).Value;
        Assert.Equal(210_000L, (await S.Credit.ExposureAsync(null, 1, co, Ct)).Total.Value);
        // Linking the guest-paid booking to the account: 2,100 + 4,200 = Rs 6,300 > Rs 5,000 → refused (it stays unlinked); unlinking is always allowed.
        await Assert.ThrowsAsync<HccException>(() => AmendAsync(guestPays, "302", "2027-06-07", "2027-06-09", new CompanyInput("", null, co)));
        Assert.Null((await DetailsAsync(guestPays.ReservationId)).Company!.CompanyId);
        await AmendAsync(shorter, "301", "2027-06-07", "2027-06-08", new CompanyInput("Kale Textiles (fictional)", Gstin));
        Assert.Equal(0L, (await S.Credit.ExposureAsync(null, 1, co, Ct)).Total.Value);
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task SetReservationCompany_links_an_existing_booking_within_the_limit_and_refuses_beyond_it()
    {
        // DEC-076: 14–16 Jun 2027 in a Classic room = 2 × (Rs 2,000 + 5 % GST) = Rs 4,200 per booking; limit Rs 5,000.
        var co = await AccountAsync("Patil Logistics (fictional)", 500_000);
        var a = await BookAsync("301", "2027-06-14", "2027-06-16", null);
        var b = await BookAsync("302", "2027-06-14", "2027-06-16", new CompanyInput("Walk-in Co"));
        Task<CommandResult<ReservationResult>> Set(ReservationResult r, CompanyInput? c)
            => S.Booking.SetCompanyAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "SetReservationCompany"), new SetReservationCompanyRequest(r.ReservationId, r.Version, c), Ct);
        var linked = (await Set(a, new CompanyInput("", null, co))).Value;
        Assert.Equal(new ReservationCompany("Patil Logistics (fictional)", Gstin, co), (await DetailsAsync(a.ReservationId)).Company);
        // The second: used Rs 4,200, remaining Rs 800, needs Rs 4,200 → refused; the booking keeps its free-text company.
        var ex = await Assert.ThrowsAsync<HccException>(() => Set(b, new CompanyInput("", null, co)));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ex.Code);
        Assert.Contains("already used by other business ₹4,200, headroom ₹800; this booking needs ₹4,200", ex.Message, StringComparison.Ordinal);
        Assert.Equal(new ReservationCompany("Walk-in Co", null), (await DetailsAsync(b.ReservationId)).Company);
        // Unlinking (invoice details only) frees the credit; nothing changed → refused; a stale version → STALE_VERSION.
        var unlinked = (await Set(linked, new CompanyInput("Patil Logistics (fictional)", Gstin))).Value;
        Assert.Equal(0L, (await S.Credit.ExposureAsync(null, 1, co, Ct)).Total.Value);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => Set(unlinked, new CompanyInput("Patil Logistics (fictional)", Gstin)))).Code);
        Assert.Equal(HccErrorCode.STALE_VERSION, (await Assert.ThrowsAsync<HccException>(() => Set(a, null))).Code);
        // With the first booking unlinked, the second fits (Rs 4,200 ≤ Rs 5,000).
        await Set(b, new CompanyInput("", null, co));
        Assert.Equal(new ReservationCompany("Patil Logistics (fictional)", Gstin, co), (await DetailsAsync(b.ReservationId)).Company);
        Assert.Equal(420_000L, (await S.Credit.ExposureAsync(null, 1, co, Ct)).Total.Value);
    }

    [Fact]
    public async Task Amend_changes_keeps_or_clears_the_company_and_a_company_only_amend_keeps_the_room_nights()
    {
        var r = await BookAsync("305", "2027-05-20", "2027-05-22", new CompanyInput("Old Co", Gstin));

        // No company in the amend: the stored one stays; dates move.
        var moved = (await AmendAsync(r, "305", "2027-05-21", "2027-05-23", null)).Value;
        var d = await DetailsAsync(r.ReservationId);
        Assert.Equal(new ReservationCompany("Old Co", Gstin), d.Company);
        Assert.Equal("2027-05-21", d.Arrival);

        // Company-only amend (same room and dates) is allowed and does not touch the room nights.
        var renamed = (await AmendAsync(moved, "305", "2027-05-21", "2027-05-23", new CompanyInput("New Co Ltd"))).Value;
        d = await DetailsAsync(r.ReservationId);
        Assert.Equal(new ReservationCompany("New Co Ltd", null), d.Company);
        Assert.NotEqual(moved.Version, renamed.Version);
        Assert.Equal(0, await S.DriftAsync());

        // Nothing at all changed → still refused.
        var same = await Assert.ThrowsAsync<HccException>(() => AmendAsync(renamed, "305", "2027-05-21", "2027-05-23", new CompanyInput("New Co Ltd")));
        Assert.Equal(HccErrorCode.VALIDATION, same.Code);

        // An empty name clears the company.
        await AmendAsync(renamed, "305", "2027-05-21", "2027-05-23", new CompanyInput(""));
        Assert.Null((await DetailsAsync(r.ReservationId)).Company);
    }
}
