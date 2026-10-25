using Hcc.Application.Commands;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Invoices;

/// <summary>PERM ids of the invoices module (migration 2401; role matrix v2 proposal §2.4 + D7).</summary>
public static class InvoicePermissions
{
    /// <summary>View invoices, credit notes, the register and the GSTR-1 summary (OWN, MGR, REC, CSH; ACC view).</summary>
    public const string View = "PERM-INV-001";

    /// <summary>Issue / print / reprint an invoice (OWN, MGR, REC, CSH).</summary>
    public const string Issue = "PERM-INV-002";

    /// <summary>Approve a credit note (Owner only, DEC-063 G3). The holder issues a credit note directly.</summary>
    public const string Approve = "PERM-INV-003";

    /// <summary>Request a credit note (MGR A; REC, CSH, ACC R).</summary>
    public const string Request = "PERM-INV-005";

    /// <summary>Master configuration key of the tax-invoice prefix (W2-1 built-in setting).</summary>
    public const string PrefixSetting = "invoicePrefix";
}

/// <summary>The document being invoiced as the source module provides it (lines carry their stored tax snapshot).</summary>
public sealed record InvoiceSourceDocument(string Kind, long SourceId, string Reference, string? DefaultRecipientName, DateOnly? PeriodFrom, DateOnly? PeriodTo,
    IReadOnlyList<SourceLine> Lines);

/// <summary>One credit-note line to post back to the source account (positive amounts; the source books them as reductions).</summary>
public sealed record CreditPosting(int CreditLineNo, long? SourceLineId, GstCategory Category, int Bps, Paise Taxable, Paise Tax);

/// <summary>
/// Invoice source port. The folio source (<see cref="FolioInvoiceSource"/>) serves checked-out, settled stay folios; W3-4's group master
/// folio plugs in as another implementation with <see cref="Kind"/> = "group" (no other change: numbering, documents, credit notes
/// and reports are source-agnostic). Lock order: the source document first, then the invoice tables.
/// </summary>
public interface IInvoiceSource
{
    string Kind { get; }

    /// <summary>Locks the source and returns it; INVALID_TRANSITION while it is not closed and settled; NOT_FOUND for another property.</summary>
    Task<InvoiceSourceDocument> LoadForIssueAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct);

    /// <summary>Takes the same lock as <see cref="LoadForIssueAsync"/> (credit notes serialize with folio changes).</summary>
    Task LockAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct);

    /// <summary>Books the credit note on the source account in the same transaction; returns the posting id per line (null = not posted).</summary>
    Task<IReadOnlyList<long?>> PostCreditAsync(ICommandTransaction tx, int propertyId, long sourceId, string creditNoteNo, string reason, DateOnly date,
        IReadOnlyList<CreditPosting> lines, CancellationToken ct);
}

/// <summary>The hotel as the supplier on documents, read inside the command (property row + Master configuration copies).</summary>
public sealed record SupplierInfo(string Name, string? Gstin, string StateName, string? Address, DateOnly BusinessDate);

public sealed record InvoiceRecord(long InvoiceId, int PropertyId, InvoiceDocType DocType, string InvoiceNo, string FyCode, int Seq, DateOnly InvoiceDate,
    string SourceKind, long SourceId, string SourceRef, DateOnly? PeriodFrom, DateOnly? PeriodTo, Recipient Recipient, string SupplierName,
    string SupplierGstin, string SupplierState, string? SupplierAddress, IReadOnlyList<InvoiceLine> Lines, InvoiceFigures Figures, string AmountWords,
    int IssuedBy, DateTime IssuedUtc);

public sealed record CreditNoteRecord(long CreditNoteId, int PropertyId, string CreditNoteNo, string FyCode, int Seq, DateOnly NoteDate, long InvoiceId,
    long RequestId, CreditMode Mode, string Reason, IReadOnlyList<CreditLine> Lines, IReadOnlyList<long?> FolioChargeIds, InvoiceFigures Figures,
    string AmountWords, int IssuedBy, int ApprovedBy, DateTime IssuedUtc);

public sealed record CreditNoteRequestRow(long RequestId, int PropertyId, long InvoiceId, CreditMode Mode, Paise? Amount, IReadOnlyList<RequestedCreditLine> Lines,
    string Reason, string Status, bool Direct, int RequestedBy, DateTime RequestedUtc, DateTime ExpiresUtc, int? DecidedBy, string? DecisionReason,
    long? CreditNoteId, string Version);

public sealed record NewCreditNoteRequest(int PropertyId, long InvoiceId, CreditMode Mode, Paise? Amount, IReadOnlyList<RequestedCreditLine> Lines, string Reason,
    bool Direct, int RequestedBy, DateTime RequestedUtc, DateTime ExpiresUtc);

/// <summary>Display facts of a folio for its invoice (booking number, guest, room, stay dates).</summary>
public sealed record FolioParty(int PropertyId, string PublicNo, string GuestName, string? RoomNo, DateOnly Arrival, DateOnly Departure);

/// <summary>
/// Invoice persistence (schema <c>ivc</c>, migration 2400). Methods with a transaction run inside the command; a null transaction reads
/// committed data on the store's own connection. Issued documents are append-only (database triggers refuse UPDATE/DELETE).
/// </summary>
public interface IInvoiceStore
{
    Task<SupplierInfo> SupplierAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<FolioParty?> FolioPartyAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    /// <summary>The invoice already issued for the source, or null (callers hold the source lock, which serializes issues of one source).</summary>
    Task<long?> InvoiceIdForSourceAsync(ICommandTransaction tx, int propertyId, string sourceKind, long sourceId, CancellationToken ct);

    /// <summary>
    /// Next number of the (property, FY, type) series in this transaction: the row is created on first use with <paramref name="prefixIfNew"/>
    /// (locked check-then-insert), else advanced by one under an update lock; a rollback releases the number (gap-free).
    /// </summary>
    Task<(string Prefix, int Seq)> NextNumberAsync(ICommandTransaction tx, int propertyId, string fyCode, InvoiceDocType type, string prefixIfNew, DateTime utc, CancellationToken ct);

    Task<long> InsertInvoiceAsync(ICommandTransaction tx, InvoiceRecord invoice, CancellationToken ct);

    Task<InvoiceRecord?> ReadInvoiceAsync(ICommandTransaction? tx, int propertyId, long invoiceId, CancellationToken ct);

    /// <summary>Taxable value and tax already credited per invoice line (issued credit notes).</summary>
    Task<IReadOnlyDictionary<int, (Paise Taxable, Paise Tax)>> CreditedAsync(ICommandTransaction? tx, long invoiceId, CancellationToken ct);

    /// <summary>The invoice's pending request under UPDLOCK, or null (callers hold the source lock).</summary>
    Task<CreditNoteRequestRow?> PendingRequestAsync(ICommandTransaction tx, long invoiceId, CancellationToken ct);

    Task<CreditNoteRequestRow> InsertRequestAsync(ICommandTransaction tx, NewCreditNoteRequest request, CancellationToken ct);

    Task<CreditNoteRequestRow?> PeekRequestAsync(ICommandTransaction tx, int propertyId, long requestId, CancellationToken ct);

    Task<CreditNoteRequestRow?> LockRequestAsync(ICommandTransaction tx, long requestId, CancellationToken ct);

    /// <summary>Versioned status change; STALE_VERSION when <paramref name="expectedVersion"/> no longer matches. Returns the new version.</summary>
    Task<string> UpdateRequestAsync(ICommandTransaction tx, long requestId, string expectedVersion, string status, int? decidedBy, DateTime? decidedUtc,
        string? decisionReason, long? creditNoteId, CancellationToken ct);

    Task<long> InsertCreditNoteAsync(ICommandTransaction tx, CreditNoteRecord note, CancellationToken ct);

    Task<CreditNoteRecord?> ReadCreditNoteAsync(ICommandTransaction? tx, int propertyId, long creditNoteId, CancellationToken ct);

    Task<IReadOnlyList<CreditNoteRecord>> CreditNotesForInvoiceAsync(long invoiceId, CancellationToken ct);

    Task<IReadOnlyList<InvoiceRecord>> InvoicesInRangeAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<CreditNoteRecord>> CreditNotesInRangeAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<CreditNoteRequestRow>> RequestsAsync(int propertyId, string? status, CancellationToken ct);
}

/// <summary>Output format of a rendered document.</summary>
public enum DocumentFormat
{
    Html,
    Text,
}

public sealed record DocumentParty(string? Name, string? Gstin, string? Address, string? State);

public sealed record DocumentLine(string Description, string Sac, Paise Taxable, int Bps, Paise Cgst, Paise Sgst, Paise Igst);

/// <summary>What a printed tax invoice / bill of supply / credit note shows (r.46 / r.49 / r.53 particulars as read; CA to confirm the layout).</summary>
public sealed record InvoiceDocumentModel(string Title, string Number, DateOnly Date, DocumentParty Supplier, DocumentParty Recipient, string PlaceOfSupply,
    bool InterState, string? Reference, string? AgainstInvoice, string? Reason, IReadOnlyList<DocumentLine> Lines, InvoiceFigures Figures, string AmountWords,
    string Footer);

public sealed record RenderedDocument(string ContentType, string Content);

/// <summary>
/// Document rendering port. ETP-REUSE: the ETP PDF / Excel exporters (Hcc.Reporting) implement this for real print-outs and PDF when
/// V:\ETP is available; <see cref="SimpleInvoiceRenderer"/> is the HTML/text stand-in (no PDF, no logo, no QR code).
/// </summary>
public interface IInvoiceDocumentRenderer
{
    RenderedDocument Render(InvoiceDocumentModel document, DocumentFormat format);
}
