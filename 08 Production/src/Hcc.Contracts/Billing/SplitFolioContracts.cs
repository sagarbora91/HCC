namespace Hcc.Contracts.Billing;

// F8 split folio wire contracts (DEC-152, SF-CONTRACT §2.4 / §4). Money = integer paise. Routes are "room" | "extras".
// Folio seq 1 = letter A (the guest's primary folio), 2..4 = B..D. Existing billing commands already address a folio by FolioId;
// a split folio is addressed by the FolioId from FolioSummaryDto (equivalent to the contract's optional folioSeq).

/// <summary>POST /api/v1/commands/OpenFolio (billing, PERM-FOL-002). PayerKind guest | company | udhaar (group refused: SF_PAYER_KIND).
/// company: CompanyId (name + GSTIN copied from the account); udhaar: CustomerKey of an LF B2 credit guest; guest: PayerName, optional GSTIN.</summary>
public sealed record OpenFolioRequest(long ReservationId, string PayerKind, long? CompanyId, string? CustomerKey, string? PayerName, string? PayerGstin,
    IReadOnlyList<string> Routes, string? Reason);

/// <summary>POST /api/v1/commands/SetFolioRouting (billing). Applies to postings after it is set; routes exclusive across folios B–D.</summary>
public sealed record SetFolioRoutingRequest(long ReservationId, int FolioSeq, IReadOnlyList<string> Routes, string Reason);

/// <summary>POST /api/v1/commands/RemoveFolio (billing). Not folio A; only an empty folio (no charge, no receipt).</summary>
public sealed record RemoveFolioRequest(long ReservationId, int FolioSeq);

/// <summary>Result of OpenFolio / SetFolioRouting / RemoveFolio.</summary>
public sealed record FolioCommandResult(long ReservationId, long FolioId, int Seq, string Letter, string PayerKind, IReadOnlyList<string> Routes, bool Removed);

/// <summary>GET /api/v1/folios/{reservationId}: one row per folio (live and removed), seq order. Figures from FolioStatement per folio.</summary>
public sealed record FolioSummaryDto(long FolioId, int Seq, string Letter, string PayerKind, string? PayerName, string? PayerGstin, long? CompanyId,
    string? CustomerKey, IReadOnlyList<string> Routes, long NetPaise, long TaxPaise, long TotalPaise, long PaidPaise, long DepositPaise, long BalancePaise,
    string? InvoiceNo, bool Removed);

/// <summary>Checkout "settle and invoice every folio" (SF-CONTRACT §5): the invoice per folio in letter order (a ₹0 folio gets none).</summary>
public sealed record FolioInvoiceIssued(long FolioId, int Seq, string Letter, string? InvoiceNo, long TotalPaise);

public sealed record SplitCheckoutResult(long ReservationId, IReadOnlyList<FolioInvoiceIssued> Invoices);
