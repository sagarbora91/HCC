namespace Hcc.Contracts.Groups;

// Company accounts (W4 N1): wire contracts. Money is integer paise; expectedVersion = base64 rowversion of the company.

/// <summary>
/// POST /commands/SaveCompany: create (CompanyId null) or edit a company account (PERM-GRP-002). Rates = negotiated nightly rate per room type code.
/// On create a CreditLimitPaise above 0 is set directly by the Owner (PERM-GRP-006, DEC-076) or becomes a pending request the Owner decides;
/// an edit never changes the limit (use SetCompanyLimit).
/// </summary>
public sealed record SaveCompanyRequest(long? CompanyId, string? ExpectedVersion, string Name, string? Gstin = null, string? Contact = null, string? Phone = null,
    string? Email = null, string? City = null, IReadOnlyDictionary<string, long>? Rates = null, long CreditLimitPaise = 0);

/// <summary>POST /commands/SetCompanyLimit: the Owner (PERM-GRP-006) sets the limit directly; everyone else with group.manage raises a request (reason required).</summary>
public sealed record SetCompanyLimitRequest(long CompanyId, long LimitPaise, string? Reason = null);

/// <summary>POST /commands/DecideCompanyLimit: the Owner (PERM-GRP-006, DEC-076), never the requester, approves or rejects a pending request.</summary>
public sealed record DecideCompanyLimitRequest(long RequestId, bool Approve, string? Note = null);

/// <summary><see cref="LimitStatus"/>: <c>none</c> (no limit asked), <c>set</c> (applied now) or <c>requested</c> (waiting for the Owner).</summary>
public sealed record CompanyResult(long CompanyId, string Version, string LimitStatus, long? RequestId);

/// <summary>
/// POST /commands/RecordCompanyPayment (DEC-076): money received from the company against its company-credit receivable (PERM-GRP-005; UPI, card or
/// bank transfer with a reference; at most the receivable). <c>InvoiceNo</c> = the tax invoice the company says it pays (statement text).
/// </summary>
/// ACC-2 tds (contract §4 A9): <c>TdsPaise</c> = TDS the company deducted; AmountPaise + TdsPaise settle the receivable (≤ it; TDS ≤ 20 % of the sum).
public sealed record RecordCompanyPaymentRequest(long CompanyId, string Tender, long AmountPaise, string Reference, string? InvoiceNo = null, long TdsPaise = 0);

/// <summary>After the payment: the receivable left, the exposure used and the credit remaining under the limit.</summary>
public sealed record CompanyPaymentResult(long CompanyId, long PaymentId, long AmountPaise, DateOnly BusinessDate, long ReceivablePaise, long UsedPaise, long RemainingPaise,
    long TdsPaise = 0);

/// <summary>One line of the exposure: kind receivable | payment (negative) | stay (Id = reservation) | group (Id = group); Reference = booking no, invoice or group code.</summary>
public sealed record CompanyCreditItemView(string Kind, long Id, string Reference, long AmountPaise);

/// <summary>
/// A company's credit picture (DEC-076): Exposure = Receivable (company credit − payments) + OpenStays (live bookings billed to the company, estimated)
/// + GroupMasters (routed master balances); Remaining = limit − exposure, never below 0. A limit of 0 = no credit.
/// </summary>
public sealed record CompanyCreditView(long CompanyId, long CreditLimitPaise, long ExposurePaise, long ReceivablePaise, long OpenStaysPaise, long GroupMastersPaise,
    long RemainingPaise, IReadOnlyList<CompanyCreditItemView> Items);

public sealed record CompanyPaymentView(long PaymentId, long CompanyId, DateOnly BusinessDate, string Tender, string Reference, string? InvoiceNo, long AmountPaise,
    int RecordedBy, DateTime RecordedUtc, long TdsPaise = 0);

/// <summary><see cref="Credit"/> is null for a caller without PERM-GRP-004 (money view). <see cref="Active"/> (ACC-2 flows): false = retired.</summary>
public sealed record CompanyView(long CompanyId, string Name, string? Gstin, string? Contact, string? Phone, string? Email, string? City,
    IReadOnlyDictionary<string, long> Rates, long CreditLimitPaise, string Version, CompanyCreditView? Credit = null, bool Active = true);

public sealed record CompanyLimitRequestView(long RequestId, long CompanyId, long FromPaise, long ToPaise, string Reason, string Status, int RequestedBy,
    DateTime RequestedUtc, int? DecidedBy, string? Note);

/// <summary>GET /groups/companies (PERM-GRP-001; credit figures and payments for PERM-GRP-004 holders).</summary>
public sealed record CompanyBookView(IReadOnlyList<CompanyView> Companies, IReadOnlyList<CompanyLimitRequestView> Requests,
    IReadOnlyList<CompanyPaymentView>? Payments = null);
