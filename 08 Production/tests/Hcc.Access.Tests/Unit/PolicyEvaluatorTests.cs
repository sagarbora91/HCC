using Hcc.Domain.Access;

namespace Hcc.Access.Tests.Unit;

/// <summary>DEC-040 evaluation order, scopes, limits → approval, view vs action, masks (pure, no database).</summary>
public class PolicyEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Past = Now.AddDays(-30);

    private static readonly Dictionary<string, PermissionKind> Kinds = new(StringComparer.Ordinal)
    {
        ["PERM-RES-001"] = PermissionKind.View,
        ["PERM-RES-002"] = PermissionKind.Action,
        ["PERM-FOL-003"] = PermissionKind.Action,
        ["PERM-FOL-004"] = PermissionKind.Action,
        ["PERM-FOL-005"] = PermissionKind.Action,
        ["PERM-PAY-001"] = PermissionKind.Action,
        ["PERM-HK-003"] = PermissionKind.Action,
    };

    private static readonly RoleGrant[] Grants =
    [
        new("REC", "PERM-RES-002", GrantMode.Allow, GrantScope.Property, null),
        new("REC", "PERM-FOL-003", GrantMode.Limit, GrantScope.Property, 25_000, "PAISE"),
        new("MGR", "PERM-FOL-003", GrantMode.Limit, GrantScope.Property, 100_000, "PAISE"),
        new("REC", "PERM-FOL-005", GrantMode.Request, GrantScope.Property, null),
        new("REC", "PERM-PAY-001", GrantMode.Allow, GrantScope.OwnShift, null),
        new("MGR", "PERM-PAY-001", GrantMode.Allow, GrantScope.Property, null),
        new("ACC", "PERM-FOL-004", GrantMode.View, GrantScope.Property, null),
        new("SUP", "PERM-RES-001", GrantMode.Masked, GrantScope.Property, null),
        new("MGR", "PERM-RES-001", GrantMode.Allow, GrantScope.Property, null),
        new("HKP", "PERM-HK-003", GrantMode.Allow, GrantScope.Assigned, null),
    ];

    private static PrincipalSnapshot User(string[] roles, bool enabled = true, RoleDeny[]? denies = null, UserException[]? exceptions = null,
        DateTime? roleUntil = null, DateTime? roleFrom = null, DateTime? userUntil = null) =>
        new(7, 1, enabled, null, userUntil, roles.Select(r => new RoleAssignment(r, roleFrom ?? Past, roleUntil)).ToList(),
            Grants, denies ?? [], exceptions ?? [], Kinds);

    private static AccessDecision Eval(PrincipalSnapshot p, string perm, ScopeMatch? scope = null, long? amount = null) =>
        AccessEvaluator.Evaluate(p, Now, perm, scope ?? ScopeMatch.None, amount);

    [Fact]
    public void Step1_disabled_or_expired_user_is_denied_even_with_grants()
    {
        Assert.Equal(DecisionOutcome.Allow, Eval(User(["REC"]), "PERM-RES-002").Outcome);
        Assert.Equal("User disabled", Eval(User(["REC"], enabled: false), "PERM-RES-002").Reason);
        Assert.Equal(DecisionOutcome.Deny, Eval(User(["REC"], userUntil: Now), "PERM-RES-002").Outcome);
    }

    [Fact]
    public void Role_assignment_validity_dates_bound_the_grant()
    {
        Assert.Equal(DecisionOutcome.Deny, Eval(User(["REC"], roleUntil: Now), "PERM-RES-002").Outcome);          // expired at this instant
        Assert.Equal(DecisionOutcome.Deny, Eval(User(["REC"], roleFrom: Now.AddMinutes(1)), "PERM-RES-002").Outcome); // not started
        Assert.Equal(DecisionOutcome.Allow, Eval(User(["REC"], roleUntil: Now.AddMinutes(1)), "PERM-RES-002").Outcome);
    }

    [Fact]
    public void Step2_scope_own_shift_and_assigned_task()
    {
        var rec = User(["REC"]);
        Assert.Equal(DecisionOutcome.Allow, Eval(rec, "PERM-PAY-001", ScopeMatch.For(s => s == GrantScope.OwnShift)).Outcome);
        Assert.Equal("Outside your assigned scope", Eval(rec, "PERM-PAY-001", ScopeMatch.For(_ => false)).Reason);
        Assert.Equal(DecisionOutcome.Deny, Eval(rec, "PERM-PAY-001").Outcome);                 // no shift named → scoped grant does not apply
        Assert.True(Eval(rec, "PERM-PAY-001", ScopeMatch.Any).Holds);                          // menus: the capability exists
        Assert.Equal(DecisionOutcome.Allow, Eval(User(["REC", "MGR"]), "PERM-PAY-001", ScopeMatch.For(_ => false)).Outcome); // property grant covers any shift
        var hkp = User(["HKP"]);
        Assert.Equal(DecisionOutcome.Allow, Eval(hkp, "PERM-HK-003", ScopeMatch.For(s => s == GrantScope.Assigned)).Outcome);
        Assert.Equal(DecisionOutcome.Deny, Eval(hkp, "PERM-HK-003", ScopeMatch.For(_ => false)).Outcome);
    }

    [Fact]
    public void Step3_explicit_deny_wins_over_allows_from_other_roles_and_exceptions()
    {
        var denied = User(["REC", "MGR"], denies: [new RoleDeny("REC", "PERM-PAY-001")]);
        Assert.Equal("Explicitly denied", Eval(denied, "PERM-PAY-001").Reason);
        var exceptionDeny = User(["REC"], exceptions: [new UserException("PERM-RES-002", true, GrantMode.Allow, GrantScope.Property, null, null, Now.AddDays(1))]);
        Assert.Equal(DecisionOutcome.Deny, Eval(exceptionDeny, "PERM-RES-002").Outcome);
        var expiredDeny = User(["REC"], exceptions: [new UserException("PERM-RES-002", true, GrantMode.Allow, GrantScope.Property, null, null, Now)]);
        Assert.Equal(DecisionOutcome.Allow, Eval(expiredDeny, "PERM-RES-002").Outcome);
        // A deny on a role the user does not hold does not apply.
        Assert.Equal(DecisionOutcome.Allow, Eval(User(["MGR"], denies: [new RoleDeny("REC", "PERM-PAY-001")]), "PERM-PAY-001").Outcome);
    }

    [Fact]
    public void Step4_default_deny_and_unknown_permission()
    {
        Assert.Equal("No permission", Eval(User(["HKP"]), "PERM-RES-002").Reason);
        Assert.Equal("Unknown permission", Eval(User(["REC"]), "PERM-NOPE-001").Reason);
        var allowException = User(["HKP"], exceptions: [new UserException("PERM-RES-002", false, GrantMode.Allow, GrantScope.Property, null, null, Now.AddHours(1))]);
        Assert.Equal(DecisionOutcome.Allow, Eval(allowException, "PERM-RES-002").Outcome);
    }

    [Fact]
    public void Step5_limits_route_to_approval()
    {
        var rec = User(["REC"]);
        Assert.Equal(DecisionOutcome.Allow, Eval(rec, "PERM-FOL-003", amount: 25_000).Outcome);
        var above = Eval(rec, "PERM-FOL-003", amount: 25_001);
        Assert.Equal(DecisionOutcome.ApprovalRequired, above.Outcome);
        Assert.Equal(("LIMIT", 25_000L, "PAISE"), (above.ApprovalKind, above.Limit!.Value, above.LimitUnit));
        Assert.Equal(DecisionOutcome.ApprovalRequired, Eval(rec, "PERM-FOL-003").Outcome);   // limited grant without an amount is never a silent allow
        Assert.Equal(DecisionOutcome.Allow, Eval(User(["REC", "MGR"]), "PERM-FOL-003", amount: 90_000).Outcome); // union: highest limit
        var request = Eval(rec, "PERM-FOL-005");
        Assert.Equal(("REQUEST_ONLY", DecisionOutcome.ApprovalRequired), (request.ApprovalKind, request.Outcome));
    }

    [Fact]
    public void View_cells_satisfy_reads_only()
    {
        var acc = User(["ACC"]);
        Assert.Equal(DecisionOutcome.Deny, Eval(acc, "PERM-FOL-004").Outcome);               // V on an action: cannot issue
        Assert.Equal(DecisionOutcome.Allow, Eval(acc, AccessEvaluator.View("PERM-FOL-004")).Outcome);
        var sup = Eval(User(["SUP"]), "PERM-RES-001");
        Assert.Equal((DecisionOutcome.Allow, GrantMode.Masked), (sup.Outcome, sup.Mode!.Value));
        Assert.Equal(GrantMode.Allow, Eval(User(["SUP", "MGR"]), "PERM-RES-001").Mode);       // an unmasked grant wins
    }
}
