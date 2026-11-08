using System.Globalization;
using System.Text.Json.Nodes;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8D-TESTS-A] SF-GOLDEN fixture bind, server side. Every expected figure is read from the prototype's
/// <c>03 Prototype/tests/sf-golden.fixture.json</c> (the file <c>stream-sf-golden.mjs</c> reads too), so the two suites cannot drift:
/// Deccan Agro rooms ₹9,450 + guest extras ₹1,850 = ₹11,300, two invoices, two GST documents. Also R8 (company-business report),
/// R9 (analytics input and study-pack facts bill the stay once). Fictional data; GSTIN 27AAPFU0939F1ZV.
/// </summary>
[Trait("Category", "Sql")]
public sealed class SfGoldenTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        DECLARE @p INT = SCOPE_IDENTITY();
        UPDATE inv.property SET gstin = '27AAAAA0000A1Z2', address = N'12 Station Road, Latur (fictional)' WHERE property_id = @p;
        INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Deluxe', 3, N'Deluxe', 300000, 1);
        DECLARE @t INT = SCOPE_IDENTITY();
        INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p, '201', @t);
        EXEC inv.usp_rebuild_type_night_count @p, '2026-10-01', '2027-01-01';
        SELECT @p;
        """;

    /// <summary>The shared fixture (found by walking up from the test binary to the repository root).</summary>
    internal static JsonNode Fixture()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "03 Prototype", "tests", "sf-golden.fixture.json");
            if (File.Exists(f)) return JsonNode.Parse(File.ReadAllText(f))!;
        }

        throw new FileNotFoundException("03 Prototype/tests/sf-golden.fixture.json was not found above " + AppContext.BaseDirectory);
    }

    internal static long L(JsonNode n, params string[] path)
    {
        foreach (var p in path) n = n[p]!;
        return n.GetValue<long>();
    }

    internal sealed record Run(long ReservationId, CheckInResult Stay, long FolioA, long FolioB, long CompanyId, CheckoutStayResult? Checkout, string Guest);

    /// <summary>The fixture stay over HTTP. checkout=false stops after the three nights (stay in house).</summary>
    internal static async Task<Run> RunAsync(HccServer s, EndToEndTests.Staff staff, JsonNode fx, bool checkout)
    {
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var guest = "Sf Golden Guest (fictional)";
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-27", 1, 0, room), Ct);
        Assert.Equal(L(fx, "folioB", "totalPaise"), quote.TotalPaise);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(guest, "9800000301"), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto(guest, "9800000301", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1301")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany", new SaveCompanyRequest(null, null, (string)fx["company"]!["name"]!, (string)fx["company"]!["gstin"]!, City: "Latur", CreditLimitPaise: L(fx, "company", "creditLimitPaise")), Ct);
        var opened = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio",
            new OpenFolioRequest(booking.ReservationId, "company", company.CompanyId, null, null, null, ["room"], "Company pays the room"), Ct);
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(stay.FolioId, "payment", "UPI", L(fx, "payments", "advanceUpiPaise"), "UPI-SFG-1", null), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(stay.FolioId, "Laundry", L(fx, "postings", "laundryPaise"), "laundry"), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(stay.FolioId, "Room service food", L(fx, "postings", "foodPaise"), "food"), Ct);
        for (var d = new DateOnly(2026, 10, 24); d < new DateOnly(2026, 10, 27); d = d.AddDays(1))
        {
            await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stay.StayId, d), Ct);
            await EndToEndTests.CloseDayAsync(staff.Owner, d);
        }

        CheckoutStayResult? co = null;
        if (checkout)
        {
            var till = await Wire.CommandAsync<TillResult>(staff.Owner, "CreateTill", new CreateTillRequest("FD-1", "Front desk"), Ct);
            await Wire.CommandAsync<ShiftResult>(staff.Reception, "OpenShift", new OpenShiftRequest(till.TillId, 500000), Ct);
            await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(stay.FolioId, "payment", "Cash", L(fx, "payments", "cashAtCheckoutPaise"), null, till.TillId), Ct);
            var fb = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{opened.FolioId}", Ct);
            var req = await Wire.CommandAsync<AdjustmentResult>(staff.Reception, "RequestAdjustment", new RequestAdjustmentRequest("credit", opened.FolioId, null, L(fx, "payments", "companyCreditPaise"), "Bill to Deccan Agro (fictional)", fb.Version), Ct);
            await Wire.CommandAsync<ApprovalResult>(staff.Owner, "DecideApproval", new DecideApprovalRequest(req.ApprovalId!.Value, true, "Company account", req.ApprovalVersion!), Ct);
            co = await Wire.CommandAsync<CheckoutStayResult>(staff.Reception, "CheckoutStay", new CheckoutStayRequest(stay.StayId, stay.StayVersion, CreditApprovalIds: [req.ApprovalId.Value]), Ct);
        }

        return new Run(booking.ReservationId, stay, stay.FolioId, opened.FolioId, company.CompanyId, co, guest);
    }

    [Fact]
    public async Task SF_GOLDEN_server_matches_the_shared_fixture_two_invoices_two_documents_sum_11300()
    {
        var fx = Fixture();
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var r = await RunAsync(s, staff, fx, checkout: true);
        Assert.Equal((int)L(fx, "invoices"), r.Checkout!.InvoiceNos!.Count);

        var rows = new List<(string No, string Supply, long Taxable, long Cgst, long Sgst, long Total)>();
        foreach (var f in new[] { r.FolioA, r.FolioB })
        {
            var no = (string)(await s.ScalarAsync($"SELECT invoice_no FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct))!;
            var supply = (string)(await s.ScalarAsync($"SELECT supply_type FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct))!;
            async Task<long> Col(string c) => Convert.ToInt64(await s.ScalarAsync($"SELECT {c} FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct), CultureInfo.InvariantCulture);
            rows.Add((no, supply, await Col("taxable_paise"), await Col("cgst_paise"), await Col("sgst_paise"), await Col("total_paise")));
        }

        Assert.Equal(r.Checkout.InvoiceNos, rows.Select(x => x.No));
        foreach (var (key, row) in new[] { ("folioA", rows[0]), ("folioB", rows[1]) })
            Assert.Equal(((string)fx[key]!["supply"]!, L(fx, key, "taxablePaise"), L(fx, key, "cgstPaise"), L(fx, key, "sgstPaise"), L(fx, key, "totalPaise")),
                (row.Supply, row.Taxable, row.Cgst, row.Sgst, row.Total));
        Assert.Equal((L(fx, "stay", "taxablePaise"), L(fx, "stay", "cgstPaise"), L(fx, "stay", "sgstPaise"), L(fx, "stay", "totalPaise")),
            (rows.Sum(x => x.Taxable), rows.Sum(x => x.Cgst), rows.Sum(x => x.Sgst), rows.Sum(x => x.Total)));
        // two GST documents = the folio invoices of this stay, and nothing else
        Assert.Equal((int)L(fx, "gstDocuments"), await s.CountAsync("SELECT COUNT(*) FROM ivc.invoice", Ct));
    }

    [Fact]
    public async Task SF_GOLDEN_R9_analytics_input_and_study_pack_facts_bill_the_split_stay_once()
    {
        var fx = Fixture();
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        await RunAsync(s, staff, fx, checkout: true);

        var src = s.Services.GetRequiredService<Hcc.Reporting.Analytics.SqlAnalyticsInputSource>();
        var input = await src.LoadAsync(s.PropertyId, new DateOnly(2026, 10, 27), Ct);
        var bill = Assert.Single(input.Bills, b => !b.Legacy);
        Assert.Equal(L(fx, "stay", "totalPaise"), bill.TotalPaise); // R9: ₹11,300 once, never per folio plus per stay
        Assert.Equal(L(fx, "stay", "totalPaise"), bill.PaidPaise);
        Assert.Equal(L(fx, "room", "ratePaise") * L(fx, "room", "nights"), bill.RoomChargePaise);
        Assert.Equal(L(fx, "stay", "cgstPaise") + L(fx, "stay", "sgstPaise"), bill.GstPaise);
        Assert.Equal(L(fx, "postings", "laundryPaise") + L(fx, "postings", "foodPaise"), bill.FnbPaise + bill.OtherPaise);
        Assert.Equal((int)L(fx, "room", "nights"), bill.ChargedNights);

        // SqlStudyPackFacts agrees: the three live room nights, ₹9,000 before GST, none of the guest folio's extras.
        var facts = s.Services.GetRequiredService<Hcc.Reporting.Reports.StudyPack.IStudyPackFacts>();
        var nights = await facts.RoomNightsAsync(s.PropertyId, new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 26), Ct);
        Assert.Equal((int)L(fx, "room", "nights"), nights.Count);
        Assert.Equal(L(fx, "folioB", "taxablePaise"), nights.Sum(n => n.AmountPaise));
        Assert.Equal(bill.RoomChargePaise, nights.Sum(n => n.AmountPaise));
    }

    [Fact]
    public async Task SF_GOLDEN_R8_company_business_attributes_only_the_company_folio()
    {
        var fx = Fixture();
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        await RunAsync(s, staff, fx, checkout: true);

        using var resp = await staff.Owner.GetAsync(ApiConventions.BasePath + "/reports/rpt.guests.company-business?from=2026-10-24&to=2026-10-27&all=true", Ct);
        var body = await resp.Content.ReadAsStringAsync(Ct);
        Assert.True(resp.IsSuccessStatusCode, body);
        var rows = JsonNode.Parse(body)!["rows"]!.AsArray();
        var row = Assert.Single(rows, x => (string?)x!["cells"]!["company"] == (string)fx["company"]!["name"]!)!["cells"]!;
        Assert.Equal(L(fx, "folioB", "taxablePaise"), row["tariff"]!.GetValue<long>());
        Assert.Equal(0L, row["other"]!.GetValue<long>()); // laundry and food are on the guest folio, not the company's business
        Assert.Equal(L(fx, "folioB", "taxablePaise"), row["total"]!.GetValue<long>());
        Assert.Equal(L(fx, "room", "nights"), row["roomNights"]!.GetValue<long>());
        Assert.Equal(1L, row["guests"]!.GetValue<long>());
    }
}
