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
