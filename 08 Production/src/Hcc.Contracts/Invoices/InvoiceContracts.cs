namespace Hcc.Contracts.Invoices;

// Wire contracts for tax invoices and credit notes (W3-6). Money = integer paise; dates YYYY-MM-DD; versions = base64 rowversion.

/// <summary>
/// POST /api/v1/commands/IssueInvoice (K, PERM-INV-002). <c>SourceKind</c> = folio (a settled, checked-out folio; group master folios
/// plug in later through the same port). <c>SupplyType</c> = B2C (default) | B2B (needs <c>BuyerGstin</c> + <c>BuyerName</c>).
/// <c>PlaceOfSupply</c> = 2-digit state code, default the hotel's state. A repeat for the same folio returns the issued invoice.
/// </summary>
public sealed record IssueInvoiceRequest(string SourceKind, long SourceId, string? SupplyType = null, string? BuyerName = null, string? BuyerGstin = null,
    string? BuyerAddress = null, string? PlaceOfSupply = null);

public sealed record IssueInvoiceResult(long InvoiceId, string InvoiceNo, string DocType, DateOnly InvoiceDate, long TotalPaise, bool Created);

public sealed record CreditLineRequest(int InvoiceLineNo, long TaxablePaise);

/// <summary>
/// POST /api/v1/commands/RequestCreditNote (K). <c>Mode</c> = full | amount (<c>AmountPaise</c> taxable, spread in proportion) | lines
/// (<c>Lines</c>). Owner (PERM-INV-003): issued at once. Others (PERM-INV-005): a pending request the Owner decides.
/// </summary>
public sealed record RequestCreditNoteRequest(long InvoiceId, string Mode, string Reason, long? AmountPaise = null, IReadOnlyList<CreditLineRequest>? Lines = null);

/// <summary>Outcome Issued (with the credit note) or PendingApproval.</summary>
public sealed record CreditNoteRequestResult(string Outcome, long RequestId, string RequestVersion, long? CreditNoteId, string? CreditNoteNo, long? TotalPaise);

/// <summary>POST /api/v1/commands/DecideCreditNote (K V, PERM-INV-003 Owner; never the requester).</summary>
public sealed record DecideCreditNoteRequest(long RequestId, bool Approve, string? Reason, string ExpectedVersion);

// ── Views ──

public sealed record InvoiceLineView(int LineNo, long? SourceLineId, string Kind, DateOnly ServiceDate, string Description, string Category, string Sac,
    long TaxablePaise, int TaxBps, long TaxPaise, long CgstPaise, long SgstPaise, long IgstPaise, long CreditedTaxablePaise, long CreditedTaxPaise);

public sealed record PartyView(string? Name, string? Gstin, string? Address, string? StateCode, string? StateName);

public sealed record CreditNoteSummaryView(long CreditNoteId, string CreditNoteNo, DateOnly NoteDate, string Reason, long TaxablePaise, long TotalPaise);

/// <summary>GET /api/v1/invoices/{id} (PERM-INV-001).</summary>
public sealed record InvoiceView(long InvoiceId, string InvoiceNo, string DocType, string DocLabel, DateOnly InvoiceDate, string SourceKind, long SourceId,
    string SourceRef, DateOnly? PeriodFrom, DateOnly? PeriodTo, string SupplyType, PartyView Supplier, PartyView Recipient, string PlaceOfSupply,
    bool InterState, IReadOnlyList<InvoiceLineView> Lines, long TaxablePaise, long CgstPaise, long SgstPaise, long IgstPaise, long TotalPaise,
    string AmountInWords, long CreditableTaxablePaise, long CreditableValuePaise, IReadOnlyList<CreditNoteSummaryView> CreditNotes, int IssuedBy, DateTime IssuedUtc);

public sealed record CreditNoteLineView(int LineNo, int InvoiceLineNo, string Category, string Sac, long TaxablePaise, int TaxBps, long TaxPaise,
    long CgstPaise, long SgstPaise, long IgstPaise, long? FolioChargeId);

/// <summary>GET /api/v1/credit-notes/{id} (PERM-INV-001).</summary>
public sealed record CreditNoteView(long CreditNoteId, string CreditNoteNo, DateOnly NoteDate, long InvoiceId, string InvoiceNo, DateOnly InvoiceDate,
    string Mode, string Reason, PartyView Supplier, PartyView Recipient, string PlaceOfSupply, bool InterState, IReadOnlyList<CreditNoteLineView> Lines,
    long TaxablePaise, long CgstPaise, long SgstPaise, long IgstPaise, long TotalPaise, string AmountInWords, int IssuedBy, int ApprovedBy, DateTime IssuedUtc);

public sealed record CreditNoteRequestView(long RequestId, long InvoiceId, string InvoiceNo, string Mode, long? AmountPaise, IReadOnlyList<CreditLineRequest> Lines,
    string Reason, string Status, bool Direct, int RequestedBy, DateTime RequestedUtc, DateTime ExpiresUtc, int? DecidedBy, string? DecisionReason,
    long? CreditNoteId, string Version);

public sealed record RegisterRowView(string Kind, long Id, string Number, DateOnly Date, string SupplyType, string? RecipientName, string? RecipientGstin,
    string? AgainstInvoiceNo, long TaxablePaise, long CgstPaise, long SgstPaise, long IgstPaise, long TotalPaise);

/// <summary>GET /api/v1/invoices?from=&amp;to= (PERM-INV-001): invoices and credit notes by document date; credit notes count negative in the net.</summary>
public sealed record InvoiceRegisterView(DateOnly From, DateOnly To, IReadOnlyList<RegisterRowView> Rows, long InvoicedTaxablePaise, long InvoicedTaxPaise,
    long InvoicedTotalPaise, long CreditedTaxablePaise, long CreditedTaxPaise, long CreditedTotalPaise, long NetTotalPaise);

public sealed record Gstr1RateView(string Section, int TaxBps, long TaxablePaise, long CgstPaise, long SgstPaise, long IgstPaise, int Documents);

public sealed record Gstr1SacView(string Sac, int TaxBps, long TaxablePaise, long CgstPaise, long SgstPaise, long IgstPaise);

public sealed record Gstr1DocView(string DocType, string First, string Last, int Count);

/// <summary>GET /api/v1/invoices/gstr1?from=&amp;to= (PERM-INV-001): a working summary for the CA (illustrative — CA to confirm), not a return file.</summary>
public sealed record Gstr1View(DateOnly From, DateOnly To, IReadOnlyList<Gstr1RateView> Rates, IReadOnlyList<Gstr1SacView> Sac, IReadOnlyList<Gstr1DocView> Documents, string Note);

/// <summary>GET /api/v1/invoices/{id}/document?format=html|text and /credit-notes/{id}/document (PERM-INV-002 print).</summary>
public sealed record RenderedDocumentView(string Number, string ContentType, string Content);
