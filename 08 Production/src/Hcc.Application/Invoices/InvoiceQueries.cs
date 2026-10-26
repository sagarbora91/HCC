using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Invoices;

/// <summary>
/// Read side: invoice and credit-note views (PERM-INV-001), the register by date range, the GSTR-1 style summary, pending requests, and
/// rendered documents (print = PERM-INV-002) through <see cref="IInvoiceDocumentRenderer"/>.
/// </summary>
public sealed class InvoiceQueries(IInvoiceStore store, IAccessPolicy policy, IInvoiceDocumentRenderer renderer)
{
    public const int MaxRangeDays = 366;

    private Task ViewAsync(int propertyId, int principalId, CancellationToken ct) => policy.RequireAsync(propertyId, principalId, InvoicePermissions.View, ct: ct);

    private static void Range(DateOnly from, DateOnly to)
    {
        if (to < from) throw HccException.Validation("The end date is before the start date.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays) throw HccException.Validation($"Choose at most {MaxRangeDays} days.");
    }

    private static PartyView Supplier(InvoiceRecord i) => new(i.SupplierName, i.SupplierGstin, i.SupplierAddress, i.SupplierState, GstStates.Name(i.SupplierState));

    private static PartyView Recipient(InvoiceRecord i) => new(i.Recipient.Name, i.Recipient.Gstin, i.Recipient.Address, i.Recipient.Gstin is { } g ? Gstin.StateCode(g) : null,
        i.Recipient.Gstin is { } g2 ? GstStates.Name(Gstin.StateCode(g2)) : null);

    public async Task<InvoiceView> GetInvoiceAsync(int propertyId, int principalId, long invoiceId, CancellationToken ct = default)
    {
        await ViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var i = await store.ReadInvoiceAsync(null, propertyId, invoiceId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Invoice not found.");
        var credited = await store.CreditedAsync(null, invoiceId, ct).ConfigureAwait(false);
        var notes = await store.CreditNotesForInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
        var creditable = i.Lines.Select(l => credited.TryGetValue(l.LineNo, out var x) ? new CreditableLine(l, x.Taxable, x.Tax, x.Cgst, x.Sgst) : new CreditableLine(l, Paise.Zero, Paise.Zero, Paise.Zero, Paise.Zero)).ToList();
        var lines = creditable.Select(c => new InvoiceLineView(c.Line.LineNo, c.Line.SourceLineId, c.Line.Kind, c.Line.ServiceDate, c.Line.Description,
            GstCategories.Code(c.Line.Category), c.Line.Sac, c.Line.Taxable.Value, c.Line.Bps, c.Line.Tax.Value, c.Line.Cgst.Value, c.Line.Sgst.Value, c.Line.Igst.Value,
            c.CreditedTaxable.Value, c.CreditedTax.Value)).ToList();
        return new InvoiceView(i.InvoiceId, i.InvoiceNo, InvoiceNumbering.Code(i.DocType), InvoiceNumbering.Label(i.DocType), i.InvoiceDate, i.SourceKind, i.SourceId,
            i.SourceRef, i.PeriodFrom, i.PeriodTo, i.Recipient.Type.ToString(), Supplier(i), Recipient(i), i.Recipient.PlaceOfSupply, i.Recipient.InterState, lines,
            i.Figures.Taxable.Value, i.Figures.Cgst.Value, i.Figures.Sgst.Value, i.Figures.Igst.Value, i.Figures.Total.Value, i.AmountWords,
            CreditNoteRules.RemainingTaxable(creditable).Value, CreditNoteRules.RemainingValue(creditable).Value,
            notes.Select(n => new CreditNoteSummaryView(n.CreditNoteId, n.CreditNoteNo, n.NoteDate, n.Reason, n.Figures.Taxable.Value, n.Figures.Total.Value)).ToList(),
            i.IssuedBy, i.IssuedUtc);
    }

    /// <summary>W4-3: the invoice issued for a source (folio | group), NOT_FOUND when none is issued yet.</summary>
    public async Task<InvoiceView> GetInvoiceForSourceAsync(int propertyId, int principalId, string sourceKind, long sourceId, CancellationToken ct = default)
    {
        await ViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var id = await store.FindInvoiceForSourceAsync(propertyId, (sourceKind ?? "").Trim().ToLowerInvariant(), sourceId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "No invoice has been issued for this record yet.");
        return await GetInvoiceAsync(propertyId, principalId, id, ct).ConfigureAwait(false);
    }

    public async Task<CreditNoteView> GetCreditNoteAsync(int propertyId, int principalId, long creditNoteId, CancellationToken ct = default)
    {
        await ViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var (n, i) = await LoadNoteAsync(propertyId, creditNoteId, ct).ConfigureAwait(false);
        return new CreditNoteView(n.CreditNoteId, n.CreditNoteNo, n.NoteDate, i.InvoiceId, i.InvoiceNo, i.InvoiceDate, CreditNoteRules.Code(n.Mode), n.Reason, Supplier(i),
            Recipient(i), i.Recipient.PlaceOfSupply, i.Recipient.InterState,
            n.Lines.Select((l, k) => new CreditNoteLineView(l.LineNo, l.InvoiceLineNo, GstCategories.Code(l.Category), l.Sac, l.Taxable.Value, l.Bps, l.Tax.Value,
                l.Cgst.Value, l.Sgst.Value, l.Igst.Value, k < n.FolioChargeIds.Count ? n.FolioChargeIds[k] : null)).ToList(),
            n.Figures.Taxable.Value, n.Figures.Cgst.Value, n.Figures.Sgst.Value, n.Figures.Igst.Value, n.Figures.Total.Value, n.AmountWords, n.IssuedBy, n.ApprovedBy, n.IssuedUtc);
    }

    private async Task<(CreditNoteRecord Note, InvoiceRecord Invoice)> LoadNoteAsync(int propertyId, long creditNoteId, CancellationToken ct)
    {
        var n = await store.ReadCreditNoteAsync(null, propertyId, creditNoteId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Credit note not found.");
        var i = (await store.ReadInvoiceAsync(null, propertyId, n.InvoiceId, ct).ConfigureAwait(false))!;
        return (n, i);
    }

    public async Task<InvoiceRegisterView> RegisterAsync(int propertyId, int principalId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await ViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        Range(from, to);
        var invoices = await store.InvoicesInRangeAsync(propertyId, from, to, ct).ConfigureAwait(false);
        var notes = await store.CreditNotesInRangeAsync(propertyId, from, to, ct).ConfigureAwait(false);
        var invoiceNos = new Dictionary<long, InvoiceRecord>();
        foreach (var n in notes)
            if (!invoiceNos.ContainsKey(n.InvoiceId))
                invoiceNos[n.InvoiceId] = (await store.ReadInvoiceAsync(null, propertyId, n.InvoiceId, ct).ConfigureAwait(false))!;
        var rows = invoices.Select(i => new RegisterRowView(InvoiceNumbering.Code(i.DocType), i.InvoiceId, i.InvoiceNo, i.InvoiceDate, i.Recipient.Type.ToString(),
                i.Recipient.Name, i.Recipient.Gstin, null, i.Figures.Taxable.Value, i.Figures.Cgst.Value, i.Figures.Sgst.Value, i.Figures.Igst.Value, i.Figures.Total.Value))
            .Concat(notes.Select(n =>
            {
                var i = invoiceNos[n.InvoiceId];
                return new RegisterRowView("CRN", n.CreditNoteId, n.CreditNoteNo, n.NoteDate, i.Recipient.Type.ToString(), i.Recipient.Name, i.Recipient.Gstin, i.InvoiceNo,
                    n.Figures.Taxable.Value, n.Figures.Cgst.Value, n.Figures.Sgst.Value, n.Figures.Igst.Value, n.Figures.Total.Value);
            }))
            .OrderBy(r => r.Date).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
        long Sum(IEnumerable<long> xs) => xs.Aggregate(0L, (a, x) => checked(a + x));
        var invTotal = Sum(invoices.Select(i => i.Figures.Total.Value));
        var cnTotal = Sum(notes.Select(n => n.Figures.Total.Value));
        return new InvoiceRegisterView(from, to, rows, Sum(invoices.Select(i => i.Figures.Taxable.Value)), Sum(invoices.Select(i => i.Figures.Tax.Value)), invTotal,
            Sum(notes.Select(n => n.Figures.Taxable.Value)), Sum(notes.Select(n => n.Figures.Tax.Value)), cnTotal, checked(invTotal - cnTotal));
    }

    public async Task<Gstr1View> Gstr1Async(int propertyId, int principalId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await ViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        Range(from, to);
        var invoices = await store.InvoicesInRangeAsync(propertyId, from, to, ct).ConfigureAwait(false);
        var notes = await store.CreditNotesInRangeAsync(propertyId, from, to, ct).ConfigureAwait(false);
        var originals = new Dictionary<long, InvoiceRecord>();
        foreach (var n in notes)
            if (!originals.ContainsKey(n.InvoiceId))
                originals[n.InvoiceId] = (await store.ReadInvoiceAsync(null, propertyId, n.InvoiceId, ct).ConfigureAwait(false))!;
        var result = Gstr1Summary.Build(
            invoices.Select(i => new Gstr1Invoice(i.InvoiceNo, i.DocType, i.Recipient.Type, i.Recipient.InterState, i.Figures.Total, i.Lines)).ToList(),
            notes.Select(n =>
            {
                var i = originals[n.InvoiceId];
                return new Gstr1CreditNote(n.CreditNoteNo, i.Recipient.Type, i.Recipient.InterState, i.Figures.Total, n.Lines);
            }).ToList());
        return new Gstr1View(from, to,
            result.Rates.Select(r => new Gstr1RateView(r.Section, r.Bps, r.Taxable.Value, r.Cgst.Value, r.Sgst.Value, r.Igst.Value, r.Documents)).ToList(),
            result.Sac.Select(r => new Gstr1SacView(r.Sac, r.Bps, r.Taxable.Value, r.Cgst.Value, r.Sgst.Value, r.Igst.Value)).ToList(),
            result.Documents.Select(d => new Gstr1DocView(d.DocType, d.First, d.Last, d.Count)).ToList(),
            "Working summary from issued documents — illustrative, CA to confirm before filing (thresholds, SAC codes, sections).");
    }

    public async Task<IReadOnlyList<CreditNoteRequestView>> RequestsAsync(int propertyId, int principalId, string? status, CancellationToken ct = default)
    {
        await ViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (status is not (null or "" or "Pending" or "Issued" or "Rejected" or "Expired")) throw HccException.Validation("Status must be Pending, Issued, Rejected or Expired.");
        var rows = await store.RequestsAsync(propertyId, string.IsNullOrEmpty(status) ? null : status, ct).ConfigureAwait(false);
        var list = new List<CreditNoteRequestView>(rows.Count);
        var numbers = new Dictionary<long, string>();
        foreach (var r in rows)
        {
            if (!numbers.TryGetValue(r.InvoiceId, out var no))
                numbers[r.InvoiceId] = no = (await store.ReadInvoiceAsync(null, propertyId, r.InvoiceId, ct).ConfigureAwait(false))!.InvoiceNo;
            list.Add(new CreditNoteRequestView(r.RequestId, r.InvoiceId, no, CreditNoteRules.Code(r.Mode), r.Amount?.Value,
                r.Lines.Select(l => new CreditLineRequest(l.InvoiceLineNo, l.Taxable.Value)).ToList(), r.Reason, r.Status, r.Direct, r.RequestedBy, r.RequestedUtc,
                r.ExpiresUtc, r.DecidedBy, r.DecisionReason, r.CreditNoteId, r.Version));
        }

        return list;
    }

    public static DocumentFormat ParseFormat(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        null or "" or "html" => DocumentFormat.Html,
        "text" => DocumentFormat.Text,
        _ => throw HccException.Validation("Format must be html or text."),
    };

    private static DocumentParty SupplierParty(InvoiceRecord i) => new(i.SupplierName, i.SupplierGstin, i.SupplierAddress, $"{GstStates.Name(i.SupplierState)} ({i.SupplierState})");

    private static DocumentParty RecipientParty(InvoiceRecord i) => new(i.Recipient.Name ?? "Guest", i.Recipient.Gstin, i.Recipient.Address, null);

    private const string Footer = "Computer-generated document. GST per line at its stored rate — illustrative, CA to confirm.";

    public async Task<RenderedDocumentView> RenderInvoiceAsync(int propertyId, int principalId, long invoiceId, string? format, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, InvoicePermissions.Issue, ct: ct).ConfigureAwait(false);
        var f = ParseFormat(format);
        var i = await store.ReadInvoiceAsync(null, propertyId, invoiceId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Invoice not found.");
        var doc = new InvoiceDocumentModel(InvoiceNumbering.Label(i.DocType), i.InvoiceNo, i.InvoiceDate, SupplierParty(i), RecipientParty(i),
            $"{GstStates.Name(i.Recipient.PlaceOfSupply)} ({i.Recipient.PlaceOfSupply})", i.Recipient.InterState,
            $"{i.SourceRef}{(i.PeriodFrom is { } a && i.PeriodTo is { } b ? $" · {a:dd-MM-yyyy} to {b:dd-MM-yyyy}" : "")}", null, null,
            i.Lines.Select(l => new DocumentLine(l.Description, l.Sac, l.Taxable, l.Bps, l.Cgst, l.Sgst, l.Igst)).ToList(), i.Figures, i.AmountWords, Footer);
        var r = renderer.Render(doc, f);
        return new RenderedDocumentView(i.InvoiceNo, r.ContentType, r.Content);
    }

    public async Task<RenderedDocumentView> RenderCreditNoteAsync(int propertyId, int principalId, long creditNoteId, string? format, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, InvoicePermissions.Issue, ct: ct).ConfigureAwait(false);
        var f = ParseFormat(format);
        var (n, i) = await LoadNoteAsync(propertyId, creditNoteId, ct).ConfigureAwait(false);
        var byLine = i.Lines.ToDictionary(l => l.LineNo);
        var doc = new InvoiceDocumentModel("Credit note", n.CreditNoteNo, n.NoteDate, SupplierParty(i), RecipientParty(i),
            $"{GstStates.Name(i.Recipient.PlaceOfSupply)} ({i.Recipient.PlaceOfSupply})", i.Recipient.InterState, i.SourceRef,
            $"{i.InvoiceNo} dated {i.InvoiceDate:dd-MM-yyyy}", n.Reason,
            n.Lines.Select(l => new DocumentLine("Credit · " + byLine[l.InvoiceLineNo].Description, l.Sac, l.Taxable, l.Bps, l.Cgst, l.Sgst, l.Igst)).ToList(),
            n.Figures, n.AmountWords, Footer);
        var r = renderer.Render(doc, f);
        return new RenderedDocumentView(n.CreditNoteNo, r.ContentType, r.Content);
    }
}
