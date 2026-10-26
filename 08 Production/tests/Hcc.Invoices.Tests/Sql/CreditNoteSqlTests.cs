using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Invoices.Tests.Sql;

/// <summary>Credit notes on SQL: prototype golden (₹100 → ₹105, then the refund), Owner approval, limits, full reversal, accounting hook.</summary>
[Trait("Category", "Sql")]
public class CreditNoteSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Golden_credit_note_100_rupees_needs_Owner_approval_posts_minus_105_to_the_folio_and_enables_the_refund()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: false, ct: Ct);
        var inv = await w.IssueAsync(stay.FolioId, ct: Ct);
        Assert.Equal(420000, inv.TotalPaise);

        // stream-c.mjs: a departed stay needs a credit note before a refund.
        var folio = await h.BillingQueries.GetFolioAsync(h.As(h.Owner), stay.FolioId, Ct);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.Billing.RequestAdjustmentAsync(h.As(h.Cashier), Hotel.Key(),
            new RequestAdjustmentRequest("refund", stay.FolioId, stay.ReceiptId, 11000, "Goodwill", folio.Version), Ct));

        var req = await w.RequestCreditAsync(h.Reception, inv.InvoiceId, "lines", lines: [new CreditLineRequest(1, 10000)], ct: Ct);
        Assert.Equal("PendingApproval", req.Outcome);
        Assert.Equal(0, (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), stay.FolioId, Ct)).Totals.BalancePaise); // nothing changes before approval
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.RequestCreditAsync(h.Cashier, inv.InvoiceId, "amount", 100, ct: Ct)); // one pending at a time
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.DecideAsync(h.Manager, req, true, Ct));                                       // DEC-063 G3

        var issued = await w.DecideAsync(h.Owner, req, true, Ct);
        Assert.Equal(("Issued", "CN/2627/00000001", 10500L), (issued.Outcome, issued.CreditNoteNo, issued.TotalPaise));
        await Expect.Fails(HccErrorCode.STALE_VERSION, () => w.DecideAsync(h.Owner, req, true, Ct)); // the old version is gone

        var cn = await w.Queries.GetCreditNoteAsync(h.PropertyId, h.Accountant, issued.CreditNoteId!.Value, Ct);
        Assert.Equal((10000L, 250L, 250L, 10500L, inv.InvoiceNo), (cn.TaxablePaise, cn.CgstPaise, cn.SgstPaise, cn.TotalPaise, cn.InvoiceNo));
        Assert.Equal("Rupees One Hundred Five Only", cn.AmountInWords);
        var charge = cn.Lines.Single().FolioChargeId!.Value;
        Assert.Equal(("correction", -10000L, 500, -500L), (await h.RowsAsync($"SELECT kind, amount_paise, tax_bps, tax_paise FROM bill.charge WHERE charge_id = {charge}",
            r => (r.GetString(0), r.GetInt64(1), r.GetInt32(2), r.GetInt64(3)), Ct)).Single());
        Assert.Equal((-250L, -250L, "room"), (await h.RowsAsync($"SELECT cgst_paise, sgst_paise, category FROM tax.charge_gst WHERE charge_id = {charge}",
            r => (r.GetInt64(0), r.GetInt64(1), r.GetString(2)), Ct)).Single());

        folio = await h.BillingQueries.GetFolioAsync(h.As(h.Owner), stay.FolioId, Ct);
        Assert.Equal(-10500, folio.Totals.BalancePaise); // prototype: credit note ₹100 + 5 % room-rate GST = −₹105
        var view = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, inv.InvoiceId, Ct);
        Assert.Equal((390000L, 409500L, 10000L), (view.CreditableTaxablePaise, view.CreditableValuePaise, view.Lines[0].CreditedTaxablePaise));
        Assert.Equal(420000, view.TotalPaise); // the invoice itself never changes

        // Billing's refund flow: one paisa above the credit is refused; the credit itself is refunded (Reception requests, Manager approves, Cashier executes).
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.Billing.RequestAdjustmentAsync(h.As(h.Reception), Hotel.Key(),
            new RequestAdjustmentRequest("refund", stay.FolioId, stay.ReceiptId, 10501, "Too much", folio.Version), Ct));
        var refund = (await h.Billing.RequestAdjustmentAsync(h.As(h.Reception), Hotel.Key(), new RequestAdjustmentRequest("refund", stay.FolioId, stay.ReceiptId, 10500, "Credit note refund", folio.Version), Ct)).Value;
        var approved = (await h.Billing.DecideApprovalAsync(h.As(h.Manager), Hotel.Key(), new DecideApprovalRequest(refund.ApprovalId!.Value, true, null, refund.ApprovalVersion!), Ct)).Value;
        await h.Billing.ExecuteAdjustmentAsync(h.As(h.Cashier), Hotel.Key(), new ExecuteAdjustmentRequest(approved.ApprovalId, approved.Version, "UPI", "DEMO-UPI-REFUND", null), Ct);
        Assert.Equal(0, (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), stay.FolioId, Ct)).Totals.BalancePaise);

        // Accounting's existing hook books the credit-note line from ChargePosted (contra revenue + CGST/SGST reversal).
        await h.Get<AccountingVoucherProjector>().RunOnceAsync(ct: Ct);
        var src = await h.RowsAsync($"SELECT source_kind, sub, business_date FROM acc.source WHERE source_key = 'charge:{charge}'", r => (r.GetString(0), r.GetString(1), r.GetDateTime(2)), Ct);
        Assert.Equal(("line", "correction", new DateTime(2026, 10, 24)), src.Single());
        Assert.Equal(10500, await h.LongAsync($"SELECT e.credit_paise FROM acc.source_entry e JOIN acc.source s ON s.source_id = e.source_id WHERE s.source_key = 'charge:{charge}' AND e.map_key = 'folio:guest'", Ct));

        // Register and GSTR-1 see the credit note.
        var register = await w.Queries.RegisterAsync(h.PropertyId, h.Owner, new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 24), Ct);
        Assert.Equal((420000L, 10500L, 409500L), (register.InvoicedTotalPaise, register.CreditedTotalPaise, register.NetTotalPaise));
        Assert.Equal(inv.InvoiceNo, register.Rows.Single(r => r.Kind == "CRN").AgainstInvoiceNo);
        var gstr1 = await w.Queries.Gstr1Async(h.PropertyId, h.Owner, new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 24), Ct);
        Assert.Equal(390000, gstr1.Rates.Single(r => r.Section == "B2CS").TaxablePaise);
        var doc = await w.Queries.RenderCreditNoteAsync(h.PropertyId, h.Cashier, cn.CreditNoteId, "text", Ct);
        Assert.Contains("Against invoice: " + inv.InvoiceNo, doc.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_issues_directly_full_credit_reverses_the_invoice_and_nothing_more_can_be_credited()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: true, ct: Ct);
        var inv = await w.IssueAsync(stay.FolioId, ct: Ct);

        await Expect.Fails(HccErrorCode.VALIDATION, () => w.RequestCreditAsync(h.Owner, inv.InvoiceId, "amount", 400001, ct: Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => w.RequestCreditAsync(h.Owner, inv.InvoiceId, "full", reason: " ", ct: Ct));
        var partial = await w.RequestCreditAsync(h.Owner, inv.InvoiceId, "amount", 10000, ct: Ct);
        Assert.Equal(("Issued", "CN/2627/00000001", 10644L), (partial.Outcome, partial.CreditNoteNo, partial.TotalPaise)); // spread 4445 / 4444 / 1111

        var full = await w.RequestCreditAsync(h.Owner, inv.InvoiceId, "full", ct: Ct);
        Assert.Equal(("Issued", "CN/2627/00000002", 426500L - 10644L), (full.Outcome, full.CreditNoteNo, full.TotalPaise));
        var v = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, inv.InvoiceId, Ct);
        Assert.Equal((0L, 0L, 2), (v.CreditableTaxablePaise, v.CreditableValuePaise, v.CreditNotes.Count));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.RequestCreditAsync(h.Owner, inv.InvoiceId, "amount", 1, ct: Ct));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.RequestCreditAsync(h.Manager, inv.InvoiceId, "full", ct: Ct));

        // Folio: the whole stay (₹4,265) is now a credit, refundable through Billing; per-charge GST snapshots stay consistent.
        Assert.Equal(-426500, (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), stay.FolioId, Ct)).Totals.BalancePaise);
        Assert.Equal(0, await h.LongAsync($"SELECT COALESCE(SUM(tax_paise), 0) FROM bill.charge WHERE folio_id = {stay.FolioId}", Ct));
        Assert.Equal(2, await h.LongAsync("SELECT COUNT(*) FROM ivc.credit_note_request WHERE direct = 1 AND status = 'Issued' AND decided_by = requested_by", Ct));
        Assert.Equal(2, await h.LongAsync("SELECT last_seq FROM ivc.series WHERE doc_type = 'CRN'", Ct));
    }

    [Fact]
    public async Task A03a_04_full_credit_after_a_half_paisa_partial_is_issued_and_ends_at_exactly_the_invoice_tax_CGST_and_SGST()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: false, ct: Ct);
        var inv = await w.IssueAsync(stay.FolioId, ct: Ct); // 2 × ₹2,000 at 5 % = ₹4,200 (CGST ₹100 + SGST ₹100)

        // Audit paper figures: ₹100.10 on the first night carries 500.5 → 501 paise of tax.
        var partial = await w.RequestCreditAsync(h.Owner, inv.InvoiceId, "lines", lines: [new CreditLineRequest(1, 10010)], ct: Ct);
        Assert.Equal(("Issued", 10511L), (partial.Outcome, partial.TotalPaise));
        // Refused before the fix ("rounding would credit more tax than was charged"): ₹1,899.90 alone would carry 9499.5 → 9500 against 9499 left.
        var full = await w.RequestCreditAsync(h.Owner, inv.InvoiceId, "full", ct: Ct);
        Assert.Equal(("Issued", 420000L - 10511L), (full.Outcome, full.TotalPaise));

        var cn = await w.Queries.GetCreditNoteAsync(h.PropertyId, h.Accountant, full.CreditNoteId!.Value, Ct);
        Assert.Equal([(1, 189989L, 9499L), (1, 1L, 0L), (2, 200000L, 10000L)], cn.Lines.Select(l => (l.InvoiceLineNo, l.TaxablePaise, l.TaxPaise)));
        Assert.Equal((389990L, 9749L, 9750L), (cn.TaxablePaise, cn.CgstPaise, cn.SgstPaise)); // + the partial's 251 / 250 = exactly 10000 / 10000
        var v = await w.Queries.GetInvoiceAsync(h.PropertyId, h.Owner, inv.InvoiceId, Ct);
        Assert.Equal((0L, 0L), (v.CreditableTaxablePaise, v.CreditableValuePaise));
        Assert.Equal([(200000L, 10000L), (200000L, 10000L)], v.Lines.Select(l => (l.CreditedTaxablePaise, l.CreditedTaxPaise)));

        // Folio and its GST snapshots end at exactly nothing: no paisa of tax, CGST or SGST stranded; the whole ₹4,200 is a credit.
        Assert.Equal(-420000, (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), stay.FolioId, Ct)).Totals.BalancePaise);
        Assert.Equal((0L, 0L, 0L, 0L), (await h.RowsAsync($"""
            SELECT SUM(c.amount_paise), SUM(c.tax_paise), SUM(g.cgst_paise), SUM(g.sgst_paise)
            FROM bill.charge c JOIN tax.charge_gst g ON g.charge_id = c.charge_id WHERE c.folio_id = {stay.FolioId}
            """, r => (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)), Ct)).Single());
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.RequestCreditAsync(h.Owner, inv.InvoiceId, "full", ct: Ct));
    }

    [Fact]
    public async Task Requester_never_decides_and_an_expired_request_cannot_be_approved()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        var stay = await w.DepartedStayAsync("105", ac006: false, ct: Ct);
        var inv = await w.IssueAsync(stay.FolioId, ct: Ct);
        var req = await w.RequestCreditAsync(h.Manager, inv.InvoiceId, "full", ct: Ct);
        Assert.Equal("PendingApproval", req.Outcome);
        // Give the Manager the approval permission by a user exception: still not their own request (SoD).
        await h.ScalarAsync($"""
            INSERT plat.user_exception(user_id, permission_id, effect, mode, scope, reason, valid_until_utc, granted_by, granted_utc)
            VALUES ({h.Manager}, 'PERM-INV-003', 'ALLOW', 'A', 'P', N'test exception', DATEADD(day, 1, SYSUTCDATETIME()), {h.Owner}, SYSUTCDATETIME());
            """, Ct);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => w.DecideAsync(h.Manager, req, true, Ct));

        // Past the 24-hour expiry: approval refused; a new request replaces the expired one.
        await h.ScalarAsync($"UPDATE ivc.credit_note_request SET expires_utc = DATEADD(hour, -1, SYSUTCDATETIME()) WHERE request_id = {req.RequestId};", Ct);
        var stale = (await w.Queries.RequestsAsync(h.PropertyId, h.Owner, "Pending", Ct)).Single();
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.DecideAsync(h.Owner, req with { RequestVersion = stale.Version }, true, Ct));
        var again = await w.RequestCreditAsync(h.Reception, inv.InvoiceId, "amount", 5000, ct: Ct);
        Assert.Equal("PendingApproval", again.Outcome);
        Assert.Equal("Expired", (await w.Queries.RequestsAsync(h.PropertyId, h.Owner, "Expired", Ct)).Single().Status);
        var done = await w.DecideAsync(h.Owner, again, true, Ct);
        Assert.Equal(5250, done.TotalPaise);
    }
}
