using System.Text.Json;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8D-LOW] Money review L6: <c>ChargeResult.FolioId</c> names the folio the charge was routed to on a split stay (with that folio's version), not
/// the folio the caller addressed. A charge that stays on the addressed folio (single folio, explicit folio, or a category routed to it) is unchanged.
/// Real composition over HTTP, fictional data.
/// </summary>
[Trait("Category", "Sql")]
public sealed class F8dLowChargeFolioTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        DECLARE @p INT = SCOPE_IDENTITY();
        UPDATE inv.property SET gstin = '27AAAAA0000A1Z2', address = N'12 Station Road, Latur (fictional)' WHERE property_id = @p;
        INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Deluxe', 3, N'Deluxe', 300000, 1);
        DECLARE @t INT = SCOPE_IDENTITY();
        INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p, '201', @t);
        EXEC inv.usp_rebuild_type_night_count @p, '2026-10-01', '2027-01-01';
        SELECT @p;
        """;

    [Fact]
    public async Task F8D_LOW_L6_charge_result_names_the_routed_folio_and_its_version()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-25", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation",
            new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("L6 Guest (fictional)", "9800000611"), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto("L6 Guest (fictional)", "9800000611", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1611")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);

        // Single-folio stay: the result names the addressed folio, as before.
        var single = await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new { folioId = stay.FolioId, label = "Laundry", amountPaise = 50000L, gstCategory = (string?)null }, Ct);
        Assert.Equal(stay.FolioId, single.FolioId);

        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany",
            new SaveCompanyRequest(null, null, "Deccan Agro Ltd (fictional)", "27AAPFU0939F1ZV", City: "Latur", CreditLimitPaise: 5_000_000), Ct);
        var b = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new { reservationId = booking.ReservationId, payerKind = "company", companyId = company.CompanyId,
            customerKey = (string?)null, payerName = (string?)null, payerGstin = (string?)null, routes = new[] { "extras" }, reason = "Company pays the extras" }, Ct);

        // Addressed to folio A, routed (extras) to folio B: the result names B and B's current version.
        var routed = await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new { folioId = stay.FolioId, label = "Airport drop", amountPaise = 60000L, gstCategory = (string?)null }, Ct);
        Assert.Equal(b.FolioId, routed.FolioId);
        Assert.Equal(b.FolioId, (long)await s.CountAsync($"SELECT folio_id FROM bill.charge WHERE charge_id = {routed.ChargeId}", Ct));
        var vb = await Wire.GetAsync<JsonElement>(staff.Reception, $"/folios/{b.FolioId}", Ct);
        Assert.Equal(vb.GetProperty("version").GetString(), routed.FolioVersion);

        // ExplicitFolio on A: the line stays on A and the result names A.
        var explicitA = await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge",
            new { folioId = stay.FolioId, label = "Minibar", amountPaise = 10000L, gstCategory = (string?)null, explicitFolio = true }, Ct);
        Assert.Equal(stay.FolioId, explicitA.FolioId);
        var va = await Wire.GetAsync<JsonElement>(staff.Reception, $"/folios/{stay.FolioId}", Ct);
        Assert.Equal(va.GetProperty("version").GetString(), explicitA.FolioVersion);
    }
}
