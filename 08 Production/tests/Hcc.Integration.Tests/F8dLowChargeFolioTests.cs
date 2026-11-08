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

        // PostExtra (catalogue charge) addressed to A, routed (extras) to B: the result names B and B's version, the same rule.
        var extra = await s.CountAsync($"""
            INSERT bill.extra_item(property_id, code, name, category, gst_category, unit, sort_order, updated_by, updated_utc)
            VALUES ({s.PropertyId}, 'L6-IRON', N'Ironing (fictional)', 'laundry', 'laundry', 'piece', 1, 1, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """, Ct);
        var catalogue = await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostExtra", new { folioId = stay.FolioId, extraItemId = extra, quantity = 1, amountPaise = 8000L }, Ct);
        Assert.Equal(b.FolioId, catalogue.FolioId);
        Assert.Equal(b.FolioId, (long)await s.CountAsync($"SELECT folio_id FROM bill.charge WHERE charge_id = {catalogue.ChargeId}", Ct));
        vb = await Wire.GetAsync<JsonElement>(staff.Reception, $"/folios/{b.FolioId}", Ct);
        Assert.Equal(vb.GetProperty("version").GetString(), catalogue.FolioVersion);
    }

    [Fact]
    public async Task F8D_LOW_udhaar_folio_payer_name_is_the_credit_customers_when_none_is_sent()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-25", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation",
            new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("Stay Guest (fictional)", "9800000612"), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto("Stay Guest (fictional)", "9800000612", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1612")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        var payer = Convert.ToInt64(await s.ScalarAsync($"""
            INSERT gst.guest(property_id, full_name, phone, origin, created_utc) VALUES ({s.PropertyId}, N'Udhaar Customer (fictional)', '9800000613', 'manual', SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """, Ct), System.Globalization.CultureInfo.InvariantCulture);
        await Wire.CommandAsync<object>(staff.Owner, "SetGuestCreditAccount", new { guestId = payer, limitPaise = 1_000_000L, active = true }, Ct);
        var key = (string)(await s.ScalarAsync($"SELECT customer_key FROM gst.guest_credit_account WHERE guest_id = {payer}", Ct))!;

        // No payerName sent: the folio's payer is the credit customer (SF-CONTRACT §2.2), not the stay guest.
        var b = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new OpenFolioRequest(booking.ReservationId, "udhaar", null, key, null, null, ["extras"], "Udhaar customer pays the extras"), Ct);
        Assert.Equal("Udhaar Customer (fictional)", await s.ScalarAsync($"SELECT payer_name FROM bill.folio WHERE folio_id = {b.FolioId}", Ct));
        var summary = await Wire.GetAsync<JsonElement>(staff.Reception, $"/folios/by-reservation/{booking.ReservationId}/summary", Ct);
        Assert.Equal("Udhaar Customer (fictional)", summary.EnumerateArray().Single(x => x.GetProperty("letter").GetString() == "B").GetProperty("payerName").GetString());

        // A name the desk sends is kept; an unknown customer key is refused as before.
        await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "RemoveFolio", new { reservationId = booking.ReservationId, folioSeq = 2 }, Ct);
        var c = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new OpenFolioRequest(booking.ReservationId, "udhaar", null, key, "U. Customer (fictional)", null, [], null), Ct);
        Assert.Equal("U. Customer (fictional)", await s.ScalarAsync($"SELECT payer_name FROM bill.folio WHERE folio_id = {c.FolioId}", Ct));
        await Wire.CommandFailsAsync(staff.Owner, "OpenFolio", new OpenFolioRequest(booking.ReservationId, "udhaar", null, "0000000000", null, null, [], null),
            System.Net.HttpStatusCode.NotFound, "NOT_FOUND", Ct);
    }
}
