using Hcc.Application.Billing;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Invoices;

namespace Hcc.Invoices.Tests.Sql;

/// <summary>
/// W4-3 reads for the production web sync layer, on the real composition: the invoice issued for a folio (by source) and the reservation's
/// folio with its stay reference (CheckoutStay needs stay id + version). Golden AC-006: ₹4,265 incl. GST, per-line tax from the posted snapshot.
/// </summary>
[Trait("Category", "Sql")]
public class SyncLookupSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invoice_by_source_and_folio_by_reservation_carry_the_posted_figures()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: true, ct: Ct);
        var reservationId = await h.LongAsync($"SELECT reservation_id FROM gst.stay WHERE stay_id = {stay.StayId}", Ct);

        var folio = await h.Get<BillingQueries>().GetReservationFolioAsync(h.As(h.Reception), reservationId, Ct);
        Assert.Equal((stay.StayId, stay.FolioId), (folio.StayId!.Value, folio.Folio!.FolioId));
        Assert.Equal(await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay.StayId}", Ct), folio.StayVersion);
        Assert.Equal((426500L, 0L), (folio.Folio.Totals.TotalPaise, folio.Folio.Totals.BalancePaise));
        // The prototype screens compute a line's GST as amount × bps, half away from zero: the server's posted tax must be exactly that.
        foreach (var l in folio.Folio.Lines)
            Assert.Equal((long)Math.Round(l.AmountPaise * (decimal)l.TaxBps / 10000m, MidpointRounding.AwayFromZero), l.TaxPaise);

        await Expect.Fails(HccErrorCode.NOT_FOUND, () => w.Queries.GetInvoiceForSourceAsync(h.PropertyId, h.Reception, "folio", stay.FolioId, Ct));
        var issued = await w.IssueAsync(stay.FolioId, ct: Ct);
        var byFolio = await w.Queries.GetInvoiceForSourceAsync(h.PropertyId, h.Reception, "Folio", stay.FolioId, Ct);
        Assert.Equal((issued.InvoiceId, issued.InvoiceNo, 426500L, "folio", stay.FolioId), (byFolio.InvoiceId, byFolio.InvoiceNo, byFolio.TotalPaise, byFolio.SourceKind, byFolio.SourceId));
        Assert.Empty(byFolio.CreditNotes);
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => w.Queries.GetInvoiceForSourceAsync(h.PropertyId, h.Reception, "group", stay.FolioId, Ct));
    }
}
