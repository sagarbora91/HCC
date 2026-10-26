namespace Hcc.Contracts.Groups;

// Company accounts (W4 N1): wire contracts. Money is integer paise; expectedVersion = base64 rowversion of the company.

/// <summary>
/// POST /commands/SaveCompany: create (CompanyId null) or edit a company account (PERM-GRP-002). Rates = negotiated nightly rate per room type code.
/// On create a CreditLimitPaise above 0 is set directly by an approver (PERM-GRP-003 within limit) or becomes a pending request for another approver;
/// an edit never changes the limit (use SetCompanyLimit).
/// </summary>
public sealed record SaveCompanyRequest(long? CompanyId, string? ExpectedVersion, string Name, string? Gstin = null, string? Contact = null, string? Phone = null,
    string? Email = null, string? City = null, IReadOnlyDictionary<string, long>? Rates = null, long CreditLimitPaise = 0);

/// <summary>POST /commands/SetCompanyLimit: approvers set the limit directly; everyone else with group.manage raises a request (reason required).</summary>
public sealed record SetCompanyLimitRequest(long CompanyId, long LimitPaise, string? Reason = null);

/// <summary>POST /commands/DecideCompanyLimit: a different approver (PERM-GRP-003, Manager within limit) approves or rejects a pending request.</summary>
public sealed record DecideCompanyLimitRequest(long RequestId, bool Approve, string? Note = null);

/// <summary><see cref="LimitStatus"/>: <c>none</c> (no limit asked), <c>set</c> (applied now) or <c>requested</c> (waiting for another approver).</summary>
public sealed record CompanyResult(long CompanyId, string Version, string LimitStatus, long? RequestId);

public sealed record CompanyView(long CompanyId, string Name, string? Gstin, string? Contact, string? Phone, string? Email, string? City,
    IReadOnlyDictionary<string, long> Rates, long CreditLimitPaise, string Version);

public sealed record CompanyLimitRequestView(long RequestId, long CompanyId, long FromPaise, long ToPaise, string Reason, string Status, int RequestedBy,
    DateTime RequestedUtc, int? DecidedBy, string? Note);

/// <summary>GET /groups/companies (PERM-GRP-001).</summary>
public sealed record CompanyBookView(IReadOnlyList<CompanyView> Companies, IReadOnlyList<CompanyLimitRequestView> Requests);
