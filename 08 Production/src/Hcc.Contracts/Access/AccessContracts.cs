namespace Hcc.Contracts.Access;

/// <summary>Access wire constants (contracts §1): HttpOnly session cookie for the browser, bearer token for the APK, device token header for PIN sign-in.</summary>
public static class AccessConventions
{
    public const string SessionCookie = "hcc_session";
    public const string DeviceTokenHeader = "X-HCC-Device";
}

/// <summary>
/// POST /api/v1/auth/sign-in. Exactly one of <see cref="Password"/> (Owner, Accountant, Tech admin) or <see cref="Pin"/>
/// (all other staff, registered device only — header <c>X-HCC-Device</c>) per DEC-062. <see cref="ReturnToken"/> = true
/// for the APK (secure storage); the browser relies on the HttpOnly cookie.
/// </summary>
public sealed record SignInRequest(string LoginName, string? Password = null, string? Pin = null, bool ReturnToken = false);

public sealed record SessionInfo(string IdleExpiresUtc, string AbsoluteExpiresUtc, int IdleTimeoutSeconds);

public sealed record SignInResult(int UserId, string DisplayName, string CredentialKind, SessionInfo Session, string? SessionToken);

/// <summary>One permission the principal holds (menus): mode A/L/R/V/M, scope P/S/A/O, limit in <see cref="LimitUnit"/> (PAISE or BPS).</summary>
public sealed record CapabilityGrant(string PermissionId, string Mode, string Scope, long? Limit, string? LimitUnit);

/// <summary>GET /api/v1/me/capabilities. Menus only — the server re-checks every request.</summary>
public sealed record CapabilitiesResult(
    int UserId,
    int PropertyId,
    string DisplayName,
    string CredentialKind,
    IReadOnlyList<string> Roles,
    IReadOnlyList<CapabilityGrant> Permissions,
    IReadOnlyDictionary<string, string> FieldMasks,
    SessionInfo Session);

/// <summary>Role assignment with validity (UTC instants; null from = now, null until = open-ended).</summary>
public sealed record RoleAssignmentInput(string RoleCode, DateTime? ValidFromUtc = null, DateTime? ValidUntilUtc = null);

public sealed record CreateUserRequest(string LoginName, string DisplayName, IReadOnlyList<RoleAssignmentInput> Roles);

public sealed record UserVersionResult(int UserId, string Version);

public sealed record SetUserEnabledRequest(int UserId, bool Enabled, string ExpectedVersion);

public sealed record AssignRoleRequest(int UserId, string RoleCode, DateTime? ValidFromUtc, DateTime? ValidUntilUtc, string ExpectedVersion);

public sealed record RevokeRoleRequest(int UserId, string RoleCode, string ExpectedVersion);

/// <summary>Admin sets/resets a user's credential: <see cref="Password"/> for password roles, <see cref="Pin"/> for PIN roles (DEC-062).</summary>
public sealed record SetCredentialRequest(int UserId, string? Password, string? Pin, string ExpectedVersion);

/// <summary>A user changes their own credential; <see cref="Current"/> is the current password or PIN.</summary>
public sealed record ChangeOwnCredentialRequest(string Current, string? NewPassword, string? NewPin);

/// <summary>User-level exception (RBAC §1): Effect ALLOW or DENY, with a reason and an expiry.</summary>
public sealed record GrantExceptionRequest(int UserId, string PermissionId, string Effect, string Mode, string Scope, long? Limit, string? LimitUnit, string Reason, DateTime ValidUntilUtc, string ExpectedVersion);

public sealed record RevokeExceptionRequest(int UserId, int ExceptionId, string ExpectedVersion);

public sealed record ExceptionResult(int ExceptionId, int UserId, string Version);

public sealed record RegisterDeviceRequest(string Label);

/// <summary><see cref="DeviceToken"/> is shown once (store it on the device); a replayed request returns null and the device must be registered again.</summary>
public sealed record RegisterDeviceResult(int DeviceId, string? DeviceToken);

public sealed record RevokeDeviceRequest(int DeviceId);

public sealed record DeviceRevokedResult(int DeviceId, int SessionsRevoked);

public sealed record UserRoleView(string RoleCode, DateTime ValidFromUtc, DateTime? ValidUntilUtc);

public sealed record UserSummary(int UserId, string LoginName, string DisplayName, bool Enabled, string CredentialKind, bool LockedOut, IReadOnlyList<UserRoleView> Roles, string Version);

public sealed record DeviceSummary(int DeviceId, string Label, DateTime RegisteredUtc, DateTime? LastSeenUtc, DateTime? RevokedUtc);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>First Owner of an empty installation (allowed only while no user exists, from the server PC itself).</summary>
public sealed record BootstrapOwnerRequest(int PropertyId, string LoginName, string DisplayName, string Password);

// ---------- W4-5 admin reads (Access control, Approvals & audit screens) ----------

/// <summary>An explicit deny on a role template (DEC-040 step 3).</summary>
public sealed record RoleDenyView(string PermissionId, string Reason);

/// <summary>One role template: its grants (mode A/L/R/V/M, scope P/S/A/O, limit) and explicit denies.</summary>
public sealed record RoleTemplateView(string RoleCode, string Name, string CredentialKind, bool OwnerOnlyAssign, IReadOnlyList<CapabilityGrant> Grants, IReadOnlyList<RoleDenyView> Denies);

public sealed record PermissionView(string PermissionId, string ResourceAction, string Kind, bool IsMoney);

/// <summary>GET /api/v1/admin/roles (PERM-ADM-001/002/003 at any mode): the role templates of the RBAC matrix, read-only.</summary>
public sealed record RolesResult(IReadOnlyList<RoleTemplateView> Roles, IReadOnlyList<PermissionView> Permissions);

/// <summary>One user-level exception (RBAC §1). <see cref="RevokedUtc"/> is set once revoked; expired ones have <see cref="ValidUntilUtc"/> in the past.</summary>
public sealed record UserExceptionView(int ExceptionId, int UserId, string PermissionId, string Effect, string Mode, string Scope, long? Limit, string? LimitUnit,
    string Reason, DateTime ValidUntilUtc, int GrantedBy, DateTime GrantedUtc, DateTime? RevokedUtc);

/// <summary>GET /api/v1/admin/exceptions?includeRevoked= (PERM-ADM-001/002 at any mode).</summary>
public sealed record ExceptionsResult(IReadOnlyList<UserExceptionView> Items);

/// <summary>One audit event (append-only <c>plat.audit_event</c>). <see cref="Actor"/> is the principal's display name.</summary>
public sealed record AuditEventView(long AuditId, DateTime AtUtc, int PrincipalId, string Actor, string Action, string Target, string Detail, string? Operation);

/// <summary>GET /api/v1/admin/audit?page&amp;pageSize&amp;action&amp;principalId&amp;from&amp;to (PERM-ADM-004), newest first.</summary>
public sealed record AuditPage(int Page, int PageSize, int Total, IReadOnlyList<AuditEventView> Items);
