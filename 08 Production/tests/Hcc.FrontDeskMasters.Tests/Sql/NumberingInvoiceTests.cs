using Hcc.Application.FrontDeskMasters;
using Hcc.Application.Invoices;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts.Billing;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Stays;
using Hcc.Domain.Errors;

namespace Hcc.FrontDeskMasters.Tests.Sql;

/// <summary>The invoices module numbers through the numbering series master (real composition: HccComposition.AddHcc). DEC-MA-5: one
/// tax-invoice series; a series that has issued a number never changes; a new one starts in a financial year without documents.</summary>
[Trait("Category", "Sql")]
public class NumberingInvoiceTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum (as in Hcc.Invoices.Tests)

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> DepartedFolioAsync(Hotel h, string room)
    {
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-23' WHERE property_id = {h.PropertyId};", Ct);
        var (_, stayId, folioId) = await h.SeedInHouseAsync(room, "2026-10-23", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stayId, new DateOnly(2026, 10, 23)), Ct);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-24' WHERE property_id = {h.PropertyId};", Ct);
        var balance = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folioId, Ct)).Totals.BalancePaise;
        await h.PayAsync(folioId, balance, "UPI", ct: Ct);
        var version = await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stayId}", Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stayId, version), Ct);
        return folioId;
    }

    [Fact]
    public async Task Invoices_and_credit_notes_number_through_the_series_master_and_an_issuing_series_is_locked()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        var fdm = h.Get<FrontDeskMastersCommands>();
        var q = h.Get<FrontDeskMastersQueries>();
        var inv = h.Get<InvoiceService>();
        await fdm.SaveNumberSeriesAsync(h.Ctx(h.Owner, "SaveNumberSeries"), new SaveNumberSeriesRequest(-1, "INV", "Tax invoice", "INV", "HCC", 101, 5, "FY", "2000-04-01"), Ct);
        var first = (await inv.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue), new IssueInvoiceRequest("folio", await DepartedFolioAsync(h, "101")), Ct)).Value;
        Assert.Equal("HCC/2627/00101", first.InvoiceNo);
        var second = (await inv.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue), new IssueInvoiceRequest("folio", await DepartedFolioAsync(h, "102")), Ct)).Value;
        Assert.Equal("HCC/2627/00102", second.InvoiceNo);
        var view = (await q.NumberSeriesAsync(h.PropertyId, h.Owner, ct: Ct)).Items.Single(x => x.Code == "INV");
        Assert.Equal((2, "HCC/2627/00103"), (view.Issued, view.NextNumber));
        var locked = await Assert.ThrowsAsync<HccException>(() => fdm.SaveNumberSeriesAsync(h.Ctx(h.Owner, "SaveNumberSeries"),
            new SaveNumberSeriesRequest(view.RowId, "INV", "Tax invoice", "INV", "HCC", 101, 6, "FY", "2000-04-01", null, view.Version), Ct));
        Assert.Equal(("MASTER_INVALID", "padding"), (locked.Details!["reason"].GetString(), locked.Details["field"].GetString()));
        var renamed = await fdm.SaveNumberSeriesAsync(h.Ctx(h.Owner, "SaveNumberSeries"),
            new SaveNumberSeriesRequest(view.RowId, "INV", "Tax invoice (hotel)", "INV", "HCC", 101, 5, "FY", "2000-04-01", null, view.Version), Ct);
        Assert.Equal("INV", renamed.Value.Code);
        var thisFy = await Assert.ThrowsAsync<HccException>(() => fdm.SaveNumberSeriesAsync(h.Ctx(h.Owner, "SaveNumberSeries"),
            new SaveNumberSeriesRequest(null, "INV-NEW", "Tax invoice new", "INV", "NEW", 1, 5, "FY", "2026-12-01"), Ct));
        Assert.Contains("can start from 2027-04-01", thisFy.Message, StringComparison.Ordinal);
        await fdm.SaveNumberSeriesAsync(h.Ctx(h.Owner, "SaveNumberSeries"), new SaveNumberSeriesRequest(null, "INV-2728", "Tax invoice FY 2027-28", "INV", "NEW", 1, 5, "FY", "2027-04-01"), Ct);

        // The credit note numbers through the stored CRN default (CN, 8 digits = the numbers before MA-5).
        var r = (await inv.RequestCreditNoteAsync(h.Ctx(h.Owner, InvoiceService.OpRequestCreditNote), new RequestCreditNoteRequest(first.InvoiceId, "amount", "Goodwill (fictional)", 10000), Ct)).Value;
        Assert.Equal("CN/2627/00000001", r.CreditNoteNo);
    }
}
