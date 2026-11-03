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

/// <summary><see cref="MustChangeCredential"/> (A06-01): the password / PIN was set by an administrator; the app opens "Change my password / PIN"
/// at once, and the server refuses everything but GET /me/capabilities, ChangeOwnCredential and sign-out until it is changed.</summary>
public sealed record SignInResult(int UserId, string DisplayName, string CredentialKind, SessionInfo Session, string? SessionToken, bool MustChangeCredential = false);

/// <summary>One permission the principal holds (menus): mode A/L/R/V/M, scope P/S/A/O, limit in <see cref="LimitUnit"/> (PAISE or BPS).</summary>
public sealed record CapabilityGrant(string PermissionId, string Mode, string Scope, long? Limit, string? LimitUnit);

/// <summary>GET /api/v1/me/capabilities. Menus only — the server re-checks every request.</summary>
/// <summary>One staff member's display name and current role codes, for screens that show who did something or pick an assignee (no login name, no credentials).</summary>
public sealed record StaffNameView(int UserId, string DisplayName, bool Enabled, IReadOnlyList<string> Roles);

/// <summary>GET /api/v1/me/directory: names, ids and current role codes of the property's users, for any signed-in user.</summary>
public sealed record StaffDirectoryResult(IReadOnlyList<StaffNameView> Items);

/// <summary>ADM-2: <see cref="Features"/> lists the sub-functions that are not full for this user's roles (feature id → "off" / "view"); null or
/// empty = everything the permissions allow. Menus only: the server re-checks every command.</summary>
public sealed record CapabilitiesResult(
    int UserId,
    int PropertyId,
    string DisplayName,
    string CredentialKind,
    IReadOnlyList<string> Roles,
    IReadOnlyList<CapabilityGrant> Permissions,
    IReadOnlyDictionary<string, string> FieldMasks,
    SessionInfo Session,
    bool MustChangeCredential = false,
    IReadOnlyDictionary<string, string>? Features = null);

/// <summary>Role assignment with validity (UTC instants; null from = now, null until = open-ended).</summary>
public sealed record RoleAssignmentInput(string RoleCode, DateTime? ValidFromUtc = null, DateTime? ValidUntilUtc = null);

public sealed record CreateUserRequest(string LoginName, string DisplayName, IReadOnlyList<RoleAssignmentInput> Roles);

public sealed record UserVersionResult(int UserId, string Version);

public sealed record SetUserEnabledRequest(int UserId, bool Enabled, string ExpectedVersion);

public sealed record AssignRoleRequest(int UserId, string RoleCode, DateTime? ValidFromUtc, DateTime? ValidUntilUtc, string ExpectedVersion);

public sealed record RevokeRoleRequest(int UserId, string RoleCode, string ExpectedVersion);

/// <summary>Admin sets/resets a user's credential: <see cref="Password"/> for password roles, <see cref="Pin"/> for PIN roles (DEC-062).</summary>
public sealed record SetCredentialRequest(int UserId, string? Password, string? Pin, string ExpectedVersion);

/// <summary>
/// A user changes their own credential; <see cref="Current"/> is the current password or PIN. Exactly one of <see cref="NewPassword"/> /
/// <see cref="NewPin"/>, of the kind the account uses (DEC-062), different from the current one; PIN / password rules as for SetCredential.
/// Ends the user's other sessions (this one continues) and clears the A06-01 must-change flag.
/// </summary>
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
/// <summary>ADM-2: <see cref="IsCustom"/> roles (codes C01…C99) carry their <see cref="BaseRole"/>, the <see cref="Money"/> flag, the non-full
/// <see cref="Features"/> levels (feature id → off / view) and a <see cref="Version"/> for UpdateRole; built-ins are unchanged (not custom, no base).</summary>
public sealed record RoleTemplateView(string RoleCode, string Name, string CredentialKind, bool OwnerOnlyAssign, IReadOnlyList<CapabilityGrant> Grants, IReadOnlyList<RoleDenyView> Denies,
    bool IsCustom = false, string? BaseRole = null, bool Money = true, IReadOnlyDictionary<string, string>? Features = null, int Version = 0, bool Retired = false);

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

// ---------- ADM-2 roles as data (02 Planning/agents/ADM_Briefs.md §2.2) ----------

/// <summary>POST /api/v1/commands/CreateRole (Owner only): a copy of one built-in base role (never OWN). <see cref="Money"/> null = the base's (true;
/// forced false for HKP / SUP / MNT / RST bases).</summary>
public sealed record CreateRoleRequest(string Name, string BaseRole, bool? Money = null);

/// <summary>POST /api/v1/commands/UpdateRole: rename and / or switch money access of a custom role; <see cref="ExpectedVersion"/> from GET /admin/roles.</summary>
public sealed record UpdateRoleRequest(string RoleCode, string? Name, bool? Money, int ExpectedVersion);

/// <summary>POST /api/v1/commands/SetRoleFeature: <see cref="Access"/> off / view / full on a non-core sub-function of a custom role.</summary>
public sealed record SetRoleFeatureRequest(string RoleCode, string FeatureId, string Access, int? ExpectedVersion = null);

/// <summary>POST /api/v1/commands/SetRoleGrant (Advanced): one permission cell of a custom role. <see cref="Mode"/> null = remove the permission;
/// otherwise A/L/R/V/M with <see cref="Scope"/> P/S/A/O (default P) and, for L, a limit in PAISE or BPS.</summary>
public sealed record SetRoleGrantRequest(string RoleCode, string PermissionId, string? Mode = null, string? Scope = null, long? Limit = null, string? LimitUnit = null,
    int? ExpectedVersion = null);

/// <summary>POST /api/v1/commands/RetireRole: refused while any enabled user holds the role.</summary>
public sealed record RetireRoleRequest(string RoleCode, int? ExpectedVersion = null);

/// <summary>Result of every role command: the role's new version, the property's policy version and, for an Advanced grant, a DEC-217 warning.</summary>
public sealed record RoleChangeResult(string RoleCode, int Version, int PolicyVersion, string? Warning = null);

/// <summary>One versioned policy change (plat.policy_version): who / when / summary / before / after (JSON text).</summary>
public sealed record PolicyChangeView(int Version, string? RoleCode, string Action, int ChangedBy, string ChangedByName, DateTime ChangedUtc, string Summary,
    string? Before, string? After);

/// <summary>GET /api/v1/admin/policy-history (PERM-ADM-001/002/004 at any mode), newest first.</summary>
public sealed record PolicyHistoryResult(IReadOnlyList<PolicyChangeView> Items);
