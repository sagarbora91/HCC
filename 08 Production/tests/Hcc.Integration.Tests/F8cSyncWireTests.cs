using System.Globalization;
using System.Net;
using System.Text.Json;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8C-SYNC] The production web client's split-folio wire (03 Prototype/src/clients/sync/money-split.ts, money.ts) against the real composition:
/// the exact camelCase bodies the client sends (explicit nulls, <c>folioSeq</c>, <c>explicitFolio</c>) and the read fields it maps
/// (FolioSummaryDto / FolioView property names, letters = seq, removed folios, a removed letter not reused). It also pins the one server rule
/// the client works around: SetFolioRouting on folio A for a category held by B is refused (SF_ROUTE_TAKEN), so the client's SF_ROUTE_SET on A
/// sends SetFolioRouting to the holder without that category instead — the end state is the prototype's. No server change (fictional data).
/// </summary>
[Trait("Category", "Sql")]
public sealed class F8cSyncWireTests
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

    private static string[] Routes(JsonElement row) => row.GetProperty("routes").EnumerateArray().Select(x => x.GetString()!).ToArray();

    private static JsonElement Row(JsonElement summary, string letter) => summary.EnumerateArray().Single(x => x.GetProperty("letter").GetString() == letter);

    [Fact]
    public async Task F8C_SYNC_client_bodies_and_reads_of_the_split_folio_match_the_server()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-25", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation",
            new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("Sync Wire Guest (fictional)", "9800000411"), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto("Sync Wire Guest (fictional)", "9800000411", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1411")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany",
            new SaveCompanyRequest(null, null, "Deccan Agro Ltd (fictional)", "27AAPFU0939F1ZV", City: "Latur", CreditLimitPaise: 5_000_000), Ct);
        var rid = booking.ReservationId;

        // SF_FOLIO_OPEN as the client sends it (money-split.ts): every optional field present, null when unused.
        var b = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new { reservationId = rid, payerKind = "company", companyId = company.CompanyId,
            customerKey = (string?)null, payerName = (string?)null, payerGstin = (string?)null, routes = new[] { "room" }, reason = "Company pays the room" }, Ct);
        Assert.Equal(("B", 2), (b.Letter, b.Seq));

        // The summary read the client maps (sfLoadSplit): property names, letter = seq, effective routes per folio, company payer copied from the account.
        var summary = await Wire.GetAsync<JsonElement>(staff.Reception, $"/folios/by-reservation/{rid}/summary", Ct);
        foreach (var name in new[] { "folioId", "seq", "letter", "payerKind", "payerName", "payerGstin", "companyId", "customerKey", "routes", "netPaise", "taxPaise",
                     "totalPaise", "paidPaise", "depositPaise", "balancePaise", "invoiceNo", "removed" })
            Assert.True(Row(summary, "B").TryGetProperty(name, out _), name);
        Assert.Equal(stay.FolioId, Row(summary, "A").GetProperty("folioId").GetInt64());
        Assert.Equal(new[] { "extras" }, Routes(Row(summary, "A")));
        Assert.Equal(new[] { "room" }, Routes(Row(summary, "B")));
        Assert.Equal("Deccan Agro Ltd (fictional)", Row(summary, "B").GetProperty("payerName").GetString());
        Assert.Equal("27AAPFU0939F1ZV", Row(summary, "B").GetProperty("payerGstin").GetString());
        Assert.Equal(company.CompanyId, Row(summary, "B").GetProperty("companyId").GetInt64());

        // CHARGE {folio:'B'} → explicitFolio true on B's id; CHARGE without a folio → folio A's id, routed by the server (extras stay on A).
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new { folioId = b.FolioId, label = "Airport drop", amountPaise = 60000L, gstCategory = (string?)null, explicitFolio = true }, Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new { folioId = stay.FolioId, label = "Laundry", amountPaise = 50000L, gstCategory = (string?)null }, Ct);
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {b.FolioId}", Ct));
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {stay.FolioId}", Ct));

        // The folio B read the client maps (GET /folios/{id}): its own version, lines and totals.
        var vb = await Wire.GetAsync<JsonElement>(staff.Reception, $"/folios/{b.FolioId}", Ct);
        foreach (var name in new[] { "folioId", "reservationId", "reservationStatus", "businessDate", "version", "lines", "receipts", "approvals", "totals" })
            Assert.True(vb.TryGetProperty(name, out _), name);
        Assert.Equal(1, vb.GetProperty("lines").GetArrayLength());
        Assert.Equal(60000L + 10800L, vb.GetProperty("totals").GetProperty("totalPaise").GetInt64()); // ₹600 + 18 %

        // The server refuses routing folio A to a category B holds (SF_ROUTE_TAKEN) — the reason the client translates SF_ROUTE_SET on A …
        var taken = await Wire.CommandFailsAsync(staff.Owner, "SetFolioRouting", new { reservationId = rid, folioSeq = 1, routes = new[] { "room" }, reason = "Guest pays the room" },
            HttpStatusCode.UnprocessableEntity, "VALIDATION", Ct);
        Assert.Equal("SF_ROUTE_TAKEN", taken.Details!["reason"].GetString());
        // … into SetFolioRouting on the holder without that category: room goes back to A (the prototype's end state).
        await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "SetFolioRouting", new { reservationId = rid, folioSeq = 2, routes = Array.Empty<string>(), reason = "Guest pays the room" }, Ct);
        summary = await Wire.GetAsync<JsonElement>(staff.Reception, $"/folios/by-reservation/{rid}/summary", Ct);
        Assert.Equal(new[] { "room", "extras" }, Routes(Row(summary, "A")));
        Assert.Empty(Routes(Row(summary, "B")));

        // SF_FOLIO_REMOVE {folioSeq}: B has a line (refused, SF_FOLIO_NOT_EMPTY); an empty guest folio C is removed, shows removed, and C is not reused (next = D).
        var notEmpty = await Wire.CommandFailsAsync(staff.Owner, "RemoveFolio", new { reservationId = rid, folioSeq = 2 }, HttpStatusCode.Conflict, "INVALID_TRANSITION", Ct);
        Assert.Equal("SF_FOLIO_NOT_EMPTY", notEmpty.Details!["reason"].GetString());
        var c = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new { reservationId = rid, payerKind = "guest", companyId = (long?)null,
            customerKey = (string?)null, payerName = "Brother (fictional)", payerGstin = (string?)null, routes = Array.Empty<string>(), reason = (string?)null }, Ct);
        Assert.Equal("C", c.Letter);
        await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "RemoveFolio", new { reservationId = rid, folioSeq = 3 }, Ct);
        var d = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new { reservationId = rid, payerKind = "guest", companyId = (long?)null,
            customerKey = (string?)null, payerName = "Sister (fictional)", payerGstin = (string?)null, routes = Array.Empty<string>(), reason = (string?)null }, Ct);
        Assert.Equal(("D", 4), (d.Letter, d.Seq));
        summary = await Wire.GetAsync<JsonElement>(staff.Reception, $"/folios/by-reservation/{rid}/summary", Ct);
        Assert.True(Row(summary, "C").GetProperty("removed").GetBoolean());
        Assert.Equal("Brother (fictional)", Row(summary, "C").GetProperty("payerName").GetString());
        Assert.Equal(4, summary.GetArrayLength());
        Assert.Equal(string.Join(",", Enumerable.Range(1, 4).Select(i => i.ToString(CultureInfo.InvariantCulture))),
            string.Join(",", summary.EnumerateArray().Select(x => x.GetProperty("seq").GetInt32().ToString(CultureInfo.InvariantCulture))));
    }
}
