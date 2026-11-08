using Hcc.Application.Invoices;
using Hcc.Contracts.Invoices;
using Hcc.Invoices.Tests.Sql;

namespace Hcc.Invoices.Tests;

/// <summary>[F8-SF-SERVER] One tax invoice per folio (UX_ivc_invoice_source): a split stay's company folio is invoiced to the company, folio A to the guest.</summary>
[Trait("Category", "Sql")]
public class SplitFolioInvoiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task F8_SF_SERVER_two_folios_of_one_stay_get_two_invoices_with_the_right_buyers()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: false, ct: Ct);
        var rid = await h.LongAsync($"SELECT reservation_id FROM bill.folio WHERE folio_id = {stay.FolioId}", Ct);
        var company = await h.LongAsync("INSERT grp.company_account(property_id, name, gstin, credit_limit_paise, created_by, created_utc) OUTPUT INSERTED.company_id VALUES ("
            + h.PropertyId + ", N'Deccan Agro Ltd (fictional)', '27AAACD1111A1Z1', 5000000, 1, SYSUTCDATETIME());", Ct);
        var fb = await h.LongAsync($"""
            INSERT bill.folio(reservation_id, folio_seq, payer_kind, payer_company_id, payer_name, payer_gstin, opened_by, opened_utc)
            OUTPUT INSERTED.folio_id VALUES ({rid}, 2, 'company', {company}, N'Deccan Agro Ltd (fictional)', '27AAACD1111A1Z1', 1, SYSUTCDATETIME());
            """, Ct);
        await h.LongAsync($"""
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({fb}, 'service', '2026-10-23', N'Laundry service', 100000, 1800, 18000);
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES ({fb}, 'payment', 'UPI', 118000, '2026-10-24');
            SELECT 1;
            """, Ct);

        var a = await w.IssueAsync(stay.FolioId, ct: Ct);
        var b = await w.IssueAsync(fb, name: "Deccan Agro Ltd (fictional)", gstin: "27AAACD1111A1Z1", ct: Ct);
        Assert.NotEqual(a.InvoiceId, b.InvoiceId);
        Assert.NotEqual(a.InvoiceNo, b.InvoiceNo);
        Assert.True(a.Created && b.Created);
        Assert.Equal(118000L, b.TotalPaise);

        var va = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Accountant, a.InvoiceId, Ct);
        var vb = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Accountant, b.InvoiceId, Ct);
        Assert.StartsWith("Seed guest", va.Recipient.Name, StringComparison.Ordinal);
        Assert.Equal(("B2B", "Deccan Agro Ltd (fictional)", "27AAACD1111A1Z1"), (vb.SupplyType, vb.Recipient.Name, vb.Recipient.Gstin));
        Assert.Equal(2L, await h.LongAsync($"SELECT COUNT(*) FROM ivc.invoice WHERE source_kind = 'folio' AND source_id IN ({stay.FolioId}, {fb})", Ct));

    }
}
