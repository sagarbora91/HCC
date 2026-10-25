namespace Hcc.Domain.Access;

/// <summary>Role codes of the RBAC matrix v1 §3 (DEC-032 adds ACC, MNT, TAD).</summary>
public static class RoleCodes
{
    public const string Owner = "OWN";
    public const string Manager = "MGR";
    public const string Reception = "REC";
    public const string Cashier = "CSH";
    public const string Accountant = "ACC";
    public const string Supervisor = "SUP";
    public const string Housekeeper = "HKP";
    public const string Maintenance = "MNT";
    public const string TechAdmin = "TAD";

    /// <summary>Matrix column order.</summary>
    public static IReadOnlyList<string> All { get; } = [Owner, Manager, Reception, Cashier, Accountant, Supervisor, Housekeeper, Maintenance, TechAdmin];

    /// <summary>DEC-062: these roles sign in with a per-user password and may never sign in by PIN.</summary>
    public static IReadOnlySet<string> PasswordRoles { get; } = new HashSet<string>(StringComparer.Ordinal) { Owner, Accountant, TechAdmin };
}

/// <summary>Matrix cell letter (RBAC §5): A allow · L allow up to limit · R request only · V view only · M view masked.</summary>
public enum GrantMode
{
    Allow = 'A',
    Limit = 'L',
    Request = 'R',
    View = 'V',
    Masked = 'M',
}

/// <summary>Grant scope (RBAC §4): P property · S own cash shift · A assigned records · O own submissions.</summary>
public enum GrantScope
{
    Property = 'P',
    OwnShift = 'S',
    Assigned = 'A',
    OwnSubmission = 'O',
}

/// <summary>VIEW permissions are reads (V/M cells = allowed); ACTION permissions need A/L/R, and V/M only satisfy their ":VIEW" variant.</summary>
public enum PermissionKind
{
    Action,
    View,
}

public sealed record RoleAssignment(string RoleCode, DateTime ValidFromUtc, DateTime? ValidUntilUtc)
{
    public bool IsActive(DateTime nowUtc) => nowUtc >= ValidFromUtc && (ValidUntilUtc is null || nowUtc < ValidUntilUtc);
}

/// <summary>A role's grant on one permission. <see cref="Limit"/> is in <see cref="LimitUnit"/> (PAISE, or BPS for rate overrides).</summary>
public sealed record RoleGrant(string RoleCode, string PermissionId, GrantMode Mode, GrantScope Scope, long? Limit, string? LimitUnit = null);

public sealed record RoleDeny(string RoleCode, string PermissionId);

/// <summary>Reasoned user-level exception with expiry (RBAC §1): an extra allow or an explicit deny.</summary>
public sealed record UserException(string PermissionId, bool IsDeny, GrantMode Mode, GrantScope Scope, long? Limit, string? LimitUnit, DateTime ValidUntilUtc);

/// <summary>Everything the policy needs about one principal, loaded per request (no caching: revocation is effective on the next request).</summary>
public sealed record PrincipalSnapshot(
    int UserId,
    int PropertyId,
    bool Enabled,
    DateTime? ValidFromUtc,
    DateTime? ValidUntilUtc,
    IReadOnlyList<RoleAssignment> Roles,
    IReadOnlyList<RoleGrant> Grants,
    IReadOnlyList<RoleDeny> Denies,
    IReadOnlyList<UserException> Exceptions,
    IReadOnlyDictionary<string, PermissionKind> PermissionKinds)
{
    public bool IsValidAt(DateTime nowUtc) => Enabled && (ValidFromUtc is null || nowUtc >= ValidFromUtc) && (ValidUntilUtc is null || nowUtc < ValidUntilUtc);

    public IReadOnlySet<string> ActiveRoles(DateTime nowUtc) =>
        IsValidAt(nowUtc) ? Roles.Where(r => r.IsActive(nowUtc)).Select(r => r.RoleCode).ToHashSet(StringComparer.Ordinal) : new HashSet<string>();
}

public enum DecisionOutcome
{
    Allow,
    ApprovalRequired,
    Deny,
}

/// <summary>Result of one evaluation. For ApprovalRequired, <see cref="ApprovalKind"/> is LIMIT (above the grant limit) or REQUEST_ONLY (R cell).</summary>
public sealed record AccessDecision(DecisionOutcome Outcome, string Reason, GrantMode? Mode = null, GrantScope? Scope = null, long? Limit = null, string? LimitUnit = null, string? ApprovalKind = null)
{
    public bool Holds => Outcome != DecisionOutcome.Deny;
}

/// <summary>How the request's scope relates to grant scopes.</summary>
public sealed class ScopeMatch
{
    private readonly Func<GrantScope, bool>? _covers;

    private ScopeMatch(bool any, Func<GrantScope, bool>? covers)
    {
        IsAny = any;
        _covers = covers;
    }

    /// <summary>No record given: only property-wide grants apply (a scoped grant needs its shift/task to be named).</summary>
    public static ScopeMatch None { get; } = new(false, null);

    /// <summary>Capability check (menus): a scoped grant counts.</summary>
    public static ScopeMatch Any { get; } = new(true, null);

    /// <summary>A named record: <paramref name="covers"/> says whether a grant scope letter covers it (P always does).</summary>
    public static ScopeMatch For(Func<GrantScope, bool> covers) => new(false, covers);

    public bool IsAny { get; }

    public bool Covers(GrantScope scope) => scope == GrantScope.Property || IsAny || (_covers?.Invoke(scope) ?? false);
}

/// <summary>
/// DEC-040 evaluation (RBAC §2): 1 disabled/expired → deny; 2 record outside every applicable grant's scope → deny;
/// 3 explicit deny on any active role or exception → deny (deny wins); 4 no allow → deny; 5 allow → limit check
/// (above → approval required) else allow. Multiple roles = union of allows minus any explicit deny.
/// </summary>
public static class AccessEvaluator
{
    /// <summary>Suffix asking for the read side of an ACTION permission (e.g. <c>PERM-FOL-004:VIEW</c>).</summary>
    public const string ViewSuffix = ":VIEW";

    public static string View(string permissionId) => permissionId + ViewSuffix;

    public static AccessDecision Evaluate(PrincipalSnapshot principal, DateTime nowUtc, string permissionId, ScopeMatch scope, long? amount = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(permissionId);
        ArgumentNullException.ThrowIfNull(scope);

        if (!principal.IsValidAt(nowUtc)) return Deny("User disabled");

        var viewVariant = permissionId.EndsWith(ViewSuffix, StringComparison.Ordinal);
        var baseId = viewVariant ? permissionId[..^ViewSuffix.Length] : permissionId;
        if (!principal.PermissionKinds.TryGetValue(baseId, out var kind)) return Deny("Unknown permission");
        var viewRequest = viewVariant || kind == PermissionKind.View;

        var roles = principal.ActiveRoles(nowUtc);
        var candidates = principal.Grants
            .Where(g => g.PermissionId == baseId && roles.Contains(g.RoleCode))
            .Select(g => (g.Mode, g.Scope, g.Limit, g.LimitUnit))
            .Concat(principal.Exceptions
                .Where(e => !e.IsDeny && e.PermissionId == baseId && nowUtc < e.ValidUntilUtc)
                .Select(e => (e.Mode, e.Scope, e.Limit, e.LimitUnit)))
            .Where(c => viewRequest || c.Mode is GrantMode.Allow or GrantMode.Limit or GrantMode.Request)
            .ToList();

        var inScope = candidates.Where(c => scope.Covers(c.Scope)).ToList();
        if (candidates.Count > 0 && inScope.Count == 0) return Deny("Outside your assigned scope");

        var denied = principal.Denies.Any(d => d.PermissionId == baseId && roles.Contains(d.RoleCode))
                     || principal.Exceptions.Any(e => e.IsDeny && e.PermissionId == baseId && nowUtc < e.ValidUntilUtc);
        if (denied) return Deny("Explicitly denied");

        if (inScope.Count == 0) return Deny("No permission");

        if (viewRequest)
        {
            // Any unmasked grant wins over masked ones (union of allows).
            var best = inScope.OrderBy(c => c.Mode == GrantMode.Masked ? 1 : 0).ThenBy(c => c.Scope == GrantScope.Property ? 0 : 1).First();
            return new AccessDecision(DecisionOutcome.Allow, "Allowed", best.Mode, best.Scope);
        }

        var allow = inScope.Where(c => c.Mode == GrantMode.Allow || (c.Mode == GrantMode.Limit && c.Limit is null)).ToList();
        if (allow.Count > 0)
        {
            var a = allow.OrderBy(c => c.Scope == GrantScope.Property ? 0 : 1).First();
            return new AccessDecision(DecisionOutcome.Allow, "Allowed", a.Mode, a.Scope);
        }

        var limited = inScope.Where(c => c.Mode == GrantMode.Limit).OrderByDescending(c => c.Limit).ToList();
        if (limited.Count > 0)
        {
            var l = limited[0];
            if (amount is { } value && value <= l.Limit)
                return new AccessDecision(DecisionOutcome.Allow, "Within limit", l.Mode, l.Scope, l.Limit, l.LimitUnit);
            return new AccessDecision(DecisionOutcome.ApprovalRequired, amount is null ? "Amount needed for a limited permission" : "Above your limit",
                l.Mode, l.Scope, l.Limit, l.LimitUnit, "LIMIT");
        }

        var r = inScope[0];
        return new AccessDecision(DecisionOutcome.ApprovalRequired, "Request only: another user approves", r.Mode, r.Scope, null, null, "REQUEST_ONLY");
    }

    private static AccessDecision Deny(string reason) => new(DecisionOutcome.Deny, reason);
}
