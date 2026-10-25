using Hcc.Application.Access;
using Hcc.Application.Housekeeping;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Stays.Tests.Harness;

/// <summary>Demo principals (prototype user ids → integer principal ids).</summary>
public static class Users
{
    public const int Owner = 1, Manager = 2, Reception = 3, Cashier = 4, Supervisor = 5, Housekeeper = 6, Housekeeper2 = 7, Accountant = 8, Maint = 9, TechAdmin = 10;

    public static int Of(string prototypeId) => prototypeId switch
    {
        "owner" => Owner, "manager" => Manager, "reception" => Reception, "cashier" => Cashier, "supervisor" => Supervisor,
        "housekeeper" => Housekeeper, "housekeeper2" => Housekeeper2, "accountant" => Accountant, "maint" => Maint, "techadmin" => TechAdmin,
        _ => throw new ArgumentOutOfRangeException(nameof(prototypeId), prototypeId, null),
    };
}

/// <summary>
/// Test stand-in for P1's policy: grants from RBAC matrix v1 §5 for the P3 PERM ids ('P' property-wide, 'A' assigned only),
/// implementing the shared scope conventions (<see cref="AccessScopeKinds"/>: null/property = property-wide, any = any scope,
/// task/room = assigned) through the real housekeeping <see cref="IAccessScopeResolver"/>s.
/// </summary>
public sealed class FakeAccessPolicy(IEnumerable<IAccessScopeResolver>? resolvers = null) : IAccessPolicy
{
    private static readonly string[] Full = ["PERM-GST-001", "PERM-GST-002", "PERM-GST-003", "PERM-STY-001", "PERM-STY-003",
        "PERM-HK-001", "PERM-HK-002", "PERM-HK-003", "PERM-HK-004", "PERM-ROOM-001", "PERM-ROOM-002"];

    private readonly Dictionary<int, Dictionary<string, char>> _grants = new()
    {
        [Users.Owner] = Full.ToDictionary(p => p, _ => 'P'),
        [Users.Manager] = Full.ToDictionary(p => p, _ => 'P'),
        [Users.Reception] = new() { ["PERM-GST-001"] = 'P', ["PERM-GST-002"] = 'P', ["PERM-GST-003"] = 'P', ["PERM-STY-001"] = 'P', ["PERM-STY-003"] = 'P', ["PERM-HK-001"] = 'P', ["PERM-ROOM-001"] = 'P' },
        [Users.Cashier] = new() { ["PERM-GST-001"] = 'P' },
        [Users.Accountant] = new() { ["PERM-GST-001"] = 'P' },
        [Users.Supervisor] = new() { ["PERM-HK-001"] = 'P', ["PERM-HK-002"] = 'P', ["PERM-HK-003"] = 'P', ["PERM-HK-004"] = 'P', ["PERM-ROOM-001"] = 'P', ["PERM-ROOM-002"] = 'P' },
        [Users.Housekeeper] = new() { ["PERM-HK-001"] = 'A', ["PERM-HK-003"] = 'A', ["PERM-ROOM-001"] = 'A' },
        [Users.Housekeeper2] = new() { ["PERM-HK-001"] = 'A', ["PERM-HK-003"] = 'A', ["PERM-ROOM-001"] = 'A' },
        [Users.Maint] = new() { ["PERM-HK-001"] = 'A', ["PERM-ROOM-001"] = 'A' },
        [Users.TechAdmin] = new(),
    };

    public List<(int Principal, string Perm, AccessScope? Scope)> Calls { get; } = [];

    /// <summary>Disable a user (no grants), as P1 would on the next request (PERM-SOD-005).</summary>
    public void Disable(int principalId) => _grants[principalId] = new();

    public async Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
    {
        lock (Calls) Calls.Add((principalId, permissionId, scope));
        if (!await HoldsAsync(propertyId, principalId, permissionId, scope, nullMeansAny: false, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, scope?.Kind is AccessScopeKinds.Task or AccessScopeKinds.Room
                ? "Only your assigned tasks are accessible." : "Access denied.");
    }

    /// <summary>As P1: <c>HasAsync</c> with no scope is a capability check (any scope); <c>RequireAsync</c> with no scope is property-wide only.</summary>
    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default)
        => HoldsAsync(propertyId, principalId, permissionId, scope, nullMeansAny: true, ct);

    private async Task<bool> HoldsAsync(int propertyId, int principalId, string permissionId, AccessScope? scope, bool nullMeansAny, CancellationToken ct)
    {
        if (!_grants.TryGetValue(principalId, out var g) || !g.TryGetValue(permissionId, out var s)) return false;
        if (s == 'P' || scope?.Kind == AccessScopeKinds.Any || (scope is null && nullMeansAny)) return true;
        if (scope is null || scope.Kind == AccessScopeKinds.Property) return false;
        var resolver = resolvers?.FirstOrDefault(r => r.Kind == scope.Kind);
        return resolver is not null && await resolver.IsOwnAsync(propertyId, principalId, scope, ct).ConfigureAwait(false);
    }
}

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}
