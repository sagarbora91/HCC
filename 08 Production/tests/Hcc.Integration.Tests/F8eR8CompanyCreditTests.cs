using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8E-R8] The company-credit half of SF-CONTRACT §8 R8 / §7 D7 on <c>rpt.money.company-credit</c> (P2-B), server side, on the SF-GOLDEN stay
/// (<c>03 Prototype/tests/sf-golden.fixture.json</c>; prototype twin <c>tests/stream-f8e-r8.mjs</c>). Deccan Agro's folio B = 3 nights ₹3,000 + 5 % =
/// 945000 paise. Pinned: "Used" = the D7 exposure counting the company folio only (the guest folio A never), the statement's company credit =
/// folio B's company-credit receipt, and a night posting is never refused by the limit (SFQ-6 default: the report shows Over limit, company credit
/// is refused until the Owner raises the limit). Integer paise only. Tests only, no product code. Fictional data; GSTIN 27AAPFU0939F1ZV.
/// </summary>
[Trait("Category", "Sql")]
public sealed class F8eR8CompanyCreditTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Report = "rpt.money.company-credit";

    private static string KindOf(JsonNode row)
    {
        var k = row["kind"];
        if (k is null) return "Row";
        if (k.GetValueKind() == JsonValueKind.Number) return k.GetValue<int>() switch { 0 => "Row", 1 => "Subtotal", _ => "Total" };
        var t = k.ToString();
        return char.ToUpperInvariant(t[0]) + t[1..];
    }

    private static long? Money(JsonNode cells, string key) => cells[key] is { } v ? v.GetValue<long>() : null;

    private static async Task<JsonNode> RunReportAsync(HttpClient c, long? companyId = null)
    {
        var q = companyId is { } id ? "&company=" + id : string.Empty;
        using var resp = await c.GetAsync(ApiConventions.BasePath + "/reports/" + Report + "?all=true" + q, Ct);
        var body = await resp.Content.ReadAsStringAsync(Ct);
        Assert.True(resp.IsSuccessStatusCode, body);
        return JsonNode.Parse(body)!;
    }

    private static JsonNode CompanyRow(JsonNode result, string name) =>
        Assert.Single(result["rows"]!.AsArray(), x => (string?)x!["cells"]!["company"] == name)!["cells"]!;

    private static List<JsonNode> Statement(JsonNode result)
    {
        var sections = result["sections"]!.AsArray();
        Assert.Equal(2, sections.Count);
        Assert.Equal("statement", (string?)sections[1]!["key"]);
        return sections[1]!["rows"]!.AsArray().Select(r => r!).ToList();
    }

    [Fact]
    public async Task F8E_R8_in_house_used_is_folio_B_only_945000_guest_folio_never_counts_statement_empty()
    {
        var fx = SfGoldenTests.Fixture();
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var r = await SfGoldenTests.RunAsync(s, staff, fx, checkout: false);
        var bTotal = SfGoldenTests.L(fx, "folioB", "totalPaise");
        var limit = SfGoldenTests.L(fx, "company", "creditLimitPaise");
        Assert.Equal(945000L, bTotal);

        var summary = await Wire.GetAsync<List<FolioSummaryDto>>(staff.Owner, $"/folios/by-reservation/{r.ReservationId}/summary", Ct);
        Assert.Equal(bTotal, summary.Single(f => f.Letter == "B").BalancePaise);
        Assert.True(summary.Single(f => f.Letter == "A").BalancePaise > 0, "folio A still owes, yet it is not the company's");

        var row = CompanyRow(await RunReportAsync(staff.Owner), (string)fx["company"]!["name"]!);
        Assert.Equal(limit, Money(row, "limit"));
        Assert.Equal(bTotal, Money(row, "used"));                      // D7: the company folio only
        Assert.Equal(limit - bTotal, Money(row, "remaining"));
        Assert.Equal("Within limit", (string?)row["status"]);
        // the report figure is the group credit picture
        var book = await Wire.GetAsync<CompanyBookView>(staff.Owner, "/groups/companies", Ct);
        Assert.Equal(bTotal, book.Companies.Single(c => c.CompanyId == r.CompanyId).Credit!.ExposurePaise);

        var st = Statement(await RunReportAsync(staff.Owner, r.CompanyId));
        Assert.DoesNotContain(st, x => KindOf(x) == "Row");             // no company credit given yet
        Assert.Equal(0L, Money(st[0]["cells"]!, "balance"));
        Assert.Equal(0L, Money(st[^1]["cells"]!, "balance"));
    }

    [Fact]
    public async Task F8E_R8_after_checkout_used_is_the_receivable_945000_statement_one_company_credit_of_folio_B()
    {
        var fx = SfGoldenTests.Fixture();
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var r = await SfGoldenTests.RunAsync(s, staff, fx, checkout: true);
        var bTotal = SfGoldenTests.L(fx, "folioB", "totalPaise");
        var limit = SfGoldenTests.L(fx, "company", "creditLimitPaise");

        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {r.FolioB} AND kind = 'company-credit' AND amount_paise = {bTotal}", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {r.FolioA} AND kind = 'company-credit'", Ct));

        var row = CompanyRow(await RunReportAsync(staff.Owner), (string)fx["company"]!["name"]!);
        Assert.Equal(bTotal, Money(row, "used"));
        Assert.Equal(limit - bTotal, Money(row, "remaining"));

        var st = Statement(await RunReportAsync(staff.Owner, r.CompanyId));
        Assert.Equal("Subtotal", KindOf(st[0]));
        Assert.Equal(0L, Money(st[0]["cells"]!, "balance"));
        var credit = Assert.Single(st, x => KindOf(x) == "Row")["cells"]!;
        Assert.Equal(bTotal, Money(credit, "credit"));
        Assert.Null(Money(credit, "payment"));
        Assert.Equal(bTotal, Money(credit, "balance"));
        var close = st[^1];
        Assert.Equal("Total", KindOf(close));
        Assert.Equal((bTotal, 0L, bTotal), (Money(close["cells"]!, "credit")!.Value, Money(close["cells"]!, "payment")!.Value, Money(close["cells"]!, "balance")!.Value));
    }

    [Fact]
    public async Task F8E_R8_night_posting_is_never_refused_by_the_limit_report_shows_over_limit_company_credit_refused_until_raised()
    {
        var fx = SfGoldenTests.Fixture();
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var bTotal = SfGoldenTests.L(fx, "folioB", "totalPaise");
        var limit = SfGoldenTests.L(fx, "company", "creditLimitPaise");
        var name = (string)fx["company"]!["name"]!;

        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        const string guest = "R8 Night Guest (fictional)";
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-27", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(guest, "9800000308"), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto(guest, "9800000308", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1308")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany", new SaveCompanyRequest(null, null, name, (string)fx["company"]!["gstin"]!, City: "Latur", CreditLimitPaise: limit), Ct);
        var opened = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio",
            new OpenFolioRequest(booking.ReservationId, "company", company.CompanyId, null, null, null, ["room"], "Company pays the room"), Ct);

        // The Owner lowers the limit to ₹9,000 (folio B will be ₹9,450) BEFORE the nights.
        var low = bTotal - 45000;
        await Wire.CommandAsync<CompanyResult>(staff.Owner, "SetCompanyLimit", new SetCompanyLimitRequest(company.CompanyId, low, "R8 test: limit lowered"), Ct);

        // Every night posts and every day closes (SFQ-6: the day close never stops on the limit).
        for (var d = new DateOnly(2026, 10, 24); d < new DateOnly(2026, 10, 27); d = d.AddDays(1))
        {
            await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stay.StayId, d), Ct);
            await EndToEndTests.CloseDayAsync(staff.Owner, d);
        }

        Assert.Equal(3, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {opened.FolioId} AND kind = 'room'", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {stay.FolioId} AND kind = 'room'", Ct));

        var row = CompanyRow(await RunReportAsync(staff.Owner), name);
        Assert.Equal(low, Money(row, "limit"));
        Assert.Equal(bTotal, Money(row, "used"));
        Assert.Equal(0L, Money(row, "remaining"));
        Assert.Equal("Over limit", (string?)row["status"]);

        // Company credit on B is refused with the D7 text while the company is over.
        var fb = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{opened.FolioId}", Ct);
        using (var refused = await Wire.PostAsync(staff.Reception, "/commands/RequestAdjustment",
                   new RequestAdjustmentRequest("credit", opened.FolioId, null, bTotal, "Bill to Deccan (over limit)", fb.Version), Wire.NewKey("r8-req"), Ct))
        {
            var text = await refused.Content.ReadAsStringAsync(Ct);
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
            var err = JsonSerializer.Deserialize<ErrorResponse>(text, HccServer.Json)!;
            Assert.Equal("INVALID_TRANSITION", err.Code);
            Assert.Contains("Credit limit exceeded", err.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {opened.FolioId} AND kind = 'company-credit'", Ct));

        // The Owner raises the limit: the request goes through and the report is back within limit.
        await Wire.CommandAsync<CompanyResult>(staff.Owner, "SetCompanyLimit", new SetCompanyLimitRequest(company.CompanyId, limit, "R8 test: limit raised"), Ct);
        fb = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{opened.FolioId}", Ct);
        var req = await Wire.CommandAsync<AdjustmentResult>(staff.Reception, "RequestAdjustment", new RequestAdjustmentRequest("credit", opened.FolioId, null, bTotal, "Bill to Deccan Agro (fictional)", fb.Version), Ct);
        Assert.NotNull(req.ApprovalId);
        row = CompanyRow(await RunReportAsync(staff.Owner), name);
        Assert.Equal(bTotal, Money(row, "used"));
        Assert.Equal(limit - bTotal, Money(row, "remaining"));
        Assert.Equal("Within limit", (string?)row["status"]);
    }
}
