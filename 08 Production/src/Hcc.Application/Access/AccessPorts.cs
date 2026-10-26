using Hcc.Application.Commands;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;

namespace Hcc.Application.Access;

/// <summary>Access settings (configuration section <c>Hcc:Access</c>). Defaults follow contracts §1 and DEC-062.</summary>
public sealed class AccessOptions
{
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Wrong current secrets before a self-service credential change (ChangeOwnCredential) locks for <see cref="LockoutDuration"/>.
    /// Sign-in has no per-account lockout any more (F-M02); see the <c>SignIn*</c> throttle settings.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Sliding window of the sign-in throttle (F-M02).</summary>
    public TimeSpan SignInWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Failed sign-ins per client address (IPv4 address or IPv6 /64) in the window before that address gets 429.</summary>
    public int SignInFailuresPerSource { get; set; } = 20;

    /// <summary>Failed sign-ins for one sign-in name from one address in the window before that pair gets 429 (other addresses are unaffected).</summary>
    public int SignInFailuresPerSourceAndLogin { get; set; } = 5;

    /// <summary>Failed sign-ins for one name (from all addresses) in the window after which each further attempt is slowed down.</summary>
    public int SignInDelayAfterFailures { get; set; } = 5;

    /// <summary>Upper bound of the progressive delay (1 s, 2 s, 4 s … up to this); never a refusal.</summary>
    public TimeSpan SignInMaxDelay { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Idempotency records older than this are purged (F-L03).</summary>
    public TimeSpan IdempotencyRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Ended (revoked or expired) sessions older than this are purged (F-L03).</summary>
    public TimeSpan SessionRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>PBKDF2-SHA256 iterations for new password hashes (OWASP 2023 guidance: 600,000).</summary>
    public int PasswordIterations { get; set; } = 600_000;

    /// <summary>PBKDF2-SHA256 iterations for new PIN hashes. A 6-digit PIN is protected by lockout + device binding, not by the hash.</summary>
    public int PinIterations { get; set; } = 210_000;

    /// <summary>A new PIN may not equal any of the user's last N PINs.</summary>
    public int PinHistory { get; set; } = 3;
}

/// <summary>Stored one-way hash of a password or PIN.</summary>
public sealed record HashedSecret(string Algorithm, int Iterations, byte[] Salt, byte[] Hash);

/// <summary>Credential hashing abstraction (contracts §1). ETP-REUSE: align with ETP credential hashing if ETP has one.</summary>
public interface ISecretHasher
{
    HashedSecret Hash(string secret, int iterations);

    bool Verify(string secret, HashedSecret stored);
}

/// <summary>
/// Scope kinds a caller passes in <see cref="AccessScope.Kind"/> and the grant scope letter each one needs. One convention for
/// every module (Wave 1 integration):
/// <list type="bullet">
/// <item><c>scope = null</c> — property-wide grants only (fail closed: a scoped grant needs its record to be named).</item>
/// <item><see cref="Any"/> — the principal holds the permission at any scope ("may use this screen at all"); the handler must
/// then check the record itself (a scoped call, or a filter such as "only my tasks").</item>
/// <item><see cref="Property"/> — explicitly property-wide (same as null; used to ask "is this principal property-wide?").</item>
/// <item><see cref="Shift"/> (S, P4 shifts), <see cref="Task"/>/<see cref="Room"/>/<see cref="WorkOrder"/> (A, P3 tasks), <see cref="Submission"/> (O) —
/// resolved by the owning module's <see cref="IAccessScopeResolver"/>.</item>
/// </list>
/// </summary>
public static class AccessScopeKinds
{
    public const string Any = "any";
    public const string Property = "property";
    public const string Shift = "shift";
    public const string Task = "task";
    public const string Room = "room";
    public const string WorkOrder = "workOrder";
    public const string Submission = "submission";

    public static GrantScope? GrantScopeFor(string kind) => kind switch
    {
        Shift => GrantScope.OwnShift,
        Task or Room or WorkOrder => GrantScope.Assigned,
        Submission => GrantScope.OwnSubmission,
        _ => null,
    };
}

/// <summary>Ready-made scopes for <see cref="IAccessPolicy"/> calls (see <see cref="AccessScopeKinds"/>).</summary>
public static class AccessScopes
{
    /// <summary>Holds the permission at any scope (menus, list screens that filter to the principal's own records).</summary>
    public static AccessScope Any { get; } = new(AccessScopeKinds.Any, 0);

    public static AccessScope Property(int propertyId) => new(AccessScopeKinds.Property, propertyId);

    public static AccessScope Shift(long shiftId) => new(AccessScopeKinds.Shift, shiftId);

    public static AccessScope HkTask(long taskId) => new(AccessScopeKinds.Task, taskId);

    public static AccessScope Room(int roomId) => new(AccessScopeKinds.Room, roomId);
}

/// <summary>
/// Answers "is this shift/task/submission the principal's own?" for one scope kind. Implemented by the module that owns
/// the record (P4 shifts, P3 tasks/work orders, Wave 2 expenses). No resolver for a kind → the record is outside scope (deny).
/// </summary>
public interface IAccessScopeResolver
{
    string Kind { get; }

    Task<bool> IsOwnAsync(int propertyId, int principalId, AccessScope scope, CancellationToken ct = default);
}

/// <summary>Richer read of the policy for handlers that branch on the decision (e.g. raise an approval request instead of failing).</summary>
public interface IAccessEvaluator
{
    Task<AccessDecision> EvaluateAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default);
}

/// <summary>A validated server session (contracts §1). The actor of every request comes from here, never from the body.</summary>
public sealed record AuthenticatedSession(long SessionId, int UserId, int PropertyId, string DisplayName, CredentialKind Kind, int? DeviceId, DateTime IdleExpiresUtc, DateTime AbsoluteExpiresUtc)
{
    public CommandContext CommandContext(string operation, string idempotencyKey) => new(PropertyId, UserId, operation, idempotencyKey);

    public SessionInfo ToWire(int idleSeconds) => new(Iso(IdleExpiresUtc), Iso(AbsoluteExpiresUtc), idleSeconds);

    public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Sign-in, session validation and sign-out (opaque server sessions, idle 30 min, absolute 12 h, AS-07 revocation).</summary>
public interface ISessionService
{
    /// <summary>Returns the session and its opaque token (the token is never stored, only its SHA-256).</summary>
    Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default);

    /// <summary>Throws UNAUTHENTICATED (401; details.reason = SESSION_INVALID) when the session is missing, unknown, expired, revoked or its user changed.</summary>
    Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default);

    /// <summary>
    /// As <see cref="ValidateAsync(string?, CancellationToken)"/>, with the request's <c>X-HCC-Device</c> token: a PIN session is
    /// refused (UNAUTHENTICATED, sessionEnd DEVICE_MISMATCH) unless the token is that of the device it was opened on (F-M07).
    /// Password sessions ignore it. The default (test doubles) ignores the device.
    /// </summary>
    Task<AuthenticatedSession> ValidateAsync(string? token, string? deviceToken, CancellationToken ct) => ValidateAsync(token, ct);

    Task SignOutAsync(string? token, CancellationToken ct = default);
}

/// <summary>User administration commands (PERM-ADM-001/002; never self-grant PERM-SOD-002; money roles Owner-only DEC-058; last Owner DEC-033).</summary>
public interface IUserAdministration
{
    Task<CommandResult<UserVersionResult>> CreateUserAsync(AuthenticatedSession actor, string idempotencyKey, CreateUserRequest request, CancellationToken ct = default);

    Task<CommandResult<UserVersionResult>> SetUserEnabledAsync(AuthenticatedSession actor, string idempotencyKey, SetUserEnabledRequest request, CancellationToken ct = default);

    Task<CommandResult<UserVersionResult>> AssignRoleAsync(AuthenticatedSession actor, string idempotencyKey, AssignRoleRequest request, CancellationToken ct = default);

    Task<CommandResult<UserVersionResult>> RevokeRoleAsync(AuthenticatedSession actor, string idempotencyKey, RevokeRoleRequest request, CancellationToken ct = default);

    Task<CommandResult<UserVersionResult>> SetCredentialAsync(AuthenticatedSession actor, string idempotencyKey, SetCredentialRequest request, CancellationToken ct = default);

    Task<CommandResult<UserVersionResult>> ChangeOwnCredentialAsync(AuthenticatedSession actor, string idempotencyKey, ChangeOwnCredentialRequest request, CancellationToken ct = default);

    Task<CommandResult<ExceptionResult>> GrantExceptionAsync(AuthenticatedSession actor, string idempotencyKey, GrantExceptionRequest request, CancellationToken ct = default);

    Task<CommandResult<ExceptionResult>> RevokeExceptionAsync(AuthenticatedSession actor, string idempotencyKey, RevokeExceptionRequest request, CancellationToken ct = default);

    Task<CommandResult<RegisterDeviceResult>> RegisterDeviceAsync(AuthenticatedSession actor, string idempotencyKey, RegisterDeviceRequest request, CancellationToken ct = default);

    Task<CommandResult<DeviceRevokedResult>> RevokeDeviceAsync(AuthenticatedSession actor, string idempotencyKey, RevokeDeviceRequest request, CancellationToken ct = default);

    Task<PagedResult<UserSummary>> ListUsersAsync(AuthenticatedSession actor, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<DeviceSummary>> ListDevicesAsync(AuthenticatedSession actor, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Creates the first Owner; refused once any user exists.</summary>
    Task<UserVersionResult> BootstrapOwnerAsync(BootstrapOwnerRequest request, CancellationToken ct = default);
}

public interface ICapabilitiesQuery
{
    Task<CapabilitiesResult> GetAsync(AuthenticatedSession session, CancellationToken ct = default);
}

/// <summary>W4-5 admin reads for the Access control and Approvals &amp; audit screens (read-only; the server stays the authority).</summary>
public interface IAccessAdminQueries
{
    /// <summary>Role templates (grants + explicit denies) and the permission catalogue. PERM-ADM-001/002/003 at any mode.</summary>
    Task<RolesResult> RolesAsync(AuthenticatedSession actor, CancellationToken ct = default);

    /// <summary>User exceptions (active and expired; revoked only when asked). PERM-ADM-001/002 at any mode.</summary>
    Task<ExceptionsResult> ExceptionsAsync(AuthenticatedSession actor, bool includeRevoked, CancellationToken ct = default);

    /// <summary>Audit events, newest first. PERM-ADM-004.</summary>
    Task<AuditPage> AuditAsync(AuthenticatedSession actor, int page, int pageSize, string? action, int? principalId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}
