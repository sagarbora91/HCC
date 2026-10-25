using System.Data;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>Loads a <see cref="PrincipalSnapshot"/> from the plat access tables (one round trip, several result sets).</summary>
public static class AccessSnapshotLoader
{
    /// <param name="permissionId">Base permission id to load grants for, or null for all (capabilities).</param>
    public static async Task<PrincipalSnapshot?> LoadAsync(SqlConnectionFactory factory, SqlConnection conn, SqlTransaction? tx, int userId, string? permissionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var cmd = factory.Command("""
            SELECT property_id, enabled, valid_from_utc, valid_until_utc FROM plat.app_user WHERE user_id = @u;
            SELECT role_code, valid_from_utc, valid_until_utc FROM plat.user_role WHERE user_id = @u;
            SELECT g.role_code, g.permission_id, g.mode, g.scope, g.limit_value, g.limit_unit
              FROM plat.role_grant g JOIN plat.user_role r ON r.role_code = g.role_code AND r.user_id = @u
             WHERE @perm IS NULL OR g.permission_id = @perm;
            SELECT d.role_code, d.permission_id
              FROM plat.role_deny d JOIN plat.user_role r ON r.role_code = d.role_code AND r.user_id = @u
             WHERE @perm IS NULL OR d.permission_id = @perm;
            SELECT permission_id, effect, mode, scope, limit_value, limit_unit, valid_until_utc
              FROM plat.user_exception WHERE user_id = @u AND revoked_utc IS NULL AND (@perm IS NULL OR permission_id = @perm);
            SELECT permission_id, kind FROM plat.permission WHERE @perm IS NULL OR permission_id = @perm;
            """, conn, tx);
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@perm", SqlDbType.VarChar, 20).Value = (object?)permissionId ?? DBNull.Value;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        var propertyId = rd.GetInt32(0);
        var enabled = rd.GetBoolean(1);
        DateTime? from = rd.IsDBNull(2) ? null : rd.GetDateTime(2);
        DateTime? until = rd.IsDBNull(3) ? null : rd.GetDateTime(3);

        var roles = new List<RoleAssignment>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            roles.Add(new RoleAssignment(rd.GetString(0), rd.GetDateTime(1), rd.IsDBNull(2) ? null : rd.GetDateTime(2)));

        var grants = new List<RoleGrant>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            grants.Add(new RoleGrant(rd.GetString(0), rd.GetString(1), (GrantMode)rd.GetString(2)[0], (GrantScope)rd.GetString(3)[0],
                rd.IsDBNull(4) ? null : rd.GetInt64(4), rd.IsDBNull(5) ? null : rd.GetString(5)));

        var denies = new List<RoleDeny>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) denies.Add(new RoleDeny(rd.GetString(0), rd.GetString(1)));

        var exceptions = new List<UserException>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            exceptions.Add(new UserException(rd.GetString(0), rd.GetString(1) == "DENY", (GrantMode)rd.GetString(2)[0], (GrantScope)rd.GetString(3)[0],
                rd.IsDBNull(4) ? null : rd.GetInt64(4), rd.IsDBNull(5) ? null : rd.GetString(5), rd.GetDateTime(6)));

        var kinds = new Dictionary<string, PermissionKind>(StringComparer.Ordinal);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            kinds[rd.GetString(0)] = rd.GetString(1) == "VIEW" ? PermissionKind.View : PermissionKind.Action;

        return new PrincipalSnapshot(userId, propertyId, enabled, from, until, roles, grants, denies, exceptions, kinds);
    }
}

/// <summary>
/// The real <see cref="IAccessPolicy"/> (DEC-040). Loads the principal's state on every call (no cache, so disable/role
/// changes take effect on the next request), resolves own-shift / assigned-task scope through the owning module's
/// <see cref="IAccessScopeResolver"/>, and maps decisions to FORBIDDEN / APPROVAL_REQUIRED.
/// </summary>
public sealed class SqlAccessPolicy(SqlConnectionFactory connections, TimeProvider clock, IEnumerable<IAccessScopeResolver> resolvers) : IAccessPolicy, IAccessEvaluator
{
    private readonly Dictionary<string, IAccessScopeResolver> _resolvers = resolvers.ToDictionary(r => r.Kind, StringComparer.Ordinal);

    public async Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
    {
        var decision = await EvaluateAsync(propertyId, principalId, permissionId, scope, amountPaise, ct).ConfigureAwait(false);
        switch (decision.Outcome)
        {
            case DecisionOutcome.Allow:
                return;
            case DecisionOutcome.ApprovalRequired:
                throw new HccException(HccErrorCode.APPROVAL_REQUIRED, decision.Reason, Details(
                    ("approvalKind", decision.ApprovalKind), ("permissionId", permissionId), ("limit", decision.Limit), ("limitUnit", decision.LimitUnit)));
            default:
                throw new HccException(HccErrorCode.FORBIDDEN, $"Not permitted: {decision.Reason}.", Details(("permissionId", permissionId)));
        }
    }

    public async Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default)
    {
        var decision = scope is null
            ? await EvaluateCoreAsync(propertyId, principalId, permissionId, null, null, capability: true, ct).ConfigureAwait(false)
            : await EvaluateAsync(propertyId, principalId, permissionId, scope, null, ct).ConfigureAwait(false);
        return decision.Holds;
    }

    public Task<AccessDecision> EvaluateAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
        => EvaluateCoreAsync(propertyId, principalId, permissionId, scope, amountPaise, capability: false, ct);

    private async Task<AccessDecision> EvaluateCoreAsync(int propertyId, int principalId, string permissionId, AccessScope? scope, long? amount, bool capability, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionId);
        var baseId = permissionId.EndsWith(AccessEvaluator.ViewSuffix, StringComparison.Ordinal) ? permissionId[..^AccessEvaluator.ViewSuffix.Length] : permissionId;
        PrincipalSnapshot? snapshot;
        await using (var conn = await connections.OpenAsync(ct).ConfigureAwait(false))
            snapshot = await AccessSnapshotLoader.LoadAsync(connections, conn, null, principalId, baseId, ct).ConfigureAwait(false);
        if (snapshot is null || snapshot.PropertyId != propertyId) return new AccessDecision(DecisionOutcome.Deny, "Unknown user for this property");

        ScopeMatch match;
        if (capability || scope?.Kind == AccessScopeKinds.Any) match = ScopeMatch.Any;
        else if (scope is null || scope.Kind == AccessScopeKinds.Property) match = ScopeMatch.None;
        else
        {
            var needed = AccessScopeKinds.GrantScopeFor(scope.Kind);
            var own = needed is not null && _resolvers.TryGetValue(scope.Kind, out var resolver)
                      && await resolver.IsOwnAsync(propertyId, principalId, scope, ct).ConfigureAwait(false);
            match = ScopeMatch.For(g => own && g == needed);
        }

        return AccessEvaluator.Evaluate(snapshot, clock.GetUtcNow().UtcDateTime, permissionId, match, amount);
    }

    internal static Dictionary<string, JsonElement> Details(params (string Key, object? Value)[] items) =>
        items.Where(i => i.Value is not null).ToDictionary(i => i.Key, i => JsonSerializer.SerializeToElement(i.Value), StringComparer.Ordinal);
}
