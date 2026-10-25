using Hcc.Application.Access;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>GET /me/capabilities: every permission the principal holds (best mode/scope/limit) plus field masks (RBAC §9, DEC-061).</summary>
public sealed class SqlCapabilitiesQuery(SqlConnectionFactory connections, AccessOptions options, TimeProvider clock) : ICapabilitiesQuery
{
    public async Task<CapabilitiesResult> GetAsync(AuthenticatedSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        PrincipalSnapshot? snapshot;
        await using (var conn = await connections.OpenAsync(ct).ConfigureAwait(false))
            snapshot = await AccessSnapshotLoader.LoadAsync(connections, conn, null, session.UserId, null, ct).ConfigureAwait(false);
        if (snapshot is null) throw new Domain.Errors.HccException(Contracts.HccErrorCode.FORBIDDEN, "Your session has ended. Sign in again.");
        var now = clock.GetUtcNow().UtcDateTime;
        var roles = snapshot.ActiveRoles(now);
        var grants = new List<CapabilityGrant>();
        foreach (var permissionId in snapshot.PermissionKinds.Keys.Order(StringComparer.Ordinal))
        {
            var decision = AccessEvaluator.Evaluate(snapshot, now, permissionId, ScopeMatch.Any);
            if (!decision.Holds) continue;
            // Report the strongest cell: for limited grants the highest limit, for request-only "R".
            grants.Add(new CapabilityGrant(permissionId, ((char)(decision.Mode ?? GrantMode.Allow)).ToString(), ((char)(decision.Scope ?? GrantScope.Property)).ToString(), decision.Limit, decision.LimitUnit));
        }

        return new CapabilitiesResult(session.UserId, session.PropertyId, session.DisplayName, CredentialPolicy.ToWire(session.Kind),
            RoleCodes.All.Where(roles.Contains).ToList(), grants, FieldMasks.For(roles), session.ToWire((int)options.IdleTimeout.TotalSeconds));
    }
}

/// <summary>Builds the P1 access services for one database (no DI container dependency), like <see cref="SqlPlatform"/>.</summary>
public sealed class AccessModule
{
    public AccessModule(SqlPlatform platform, AccessOptions options, TimeProvider clock, IEnumerable<IAccessScopeResolver>? resolvers = null)
    {
        ArgumentNullException.ThrowIfNull(platform);
        Options = options;
        Hasher = new Pbkdf2SecretHasher();
        Policy = new SqlAccessPolicy(platform.Connections, clock, resolvers ?? []);
        Sessions = new SqlSessionService(platform.Connections, Hasher, options, clock);
        Users = new SqlUserAdministration(platform.Connections, platform.Commands, Policy, Hasher, options, clock);
        Capabilities = new SqlCapabilitiesQuery(platform.Connections, options, clock);
        Migrations = AccessMigrationSource.Runner(platform);
    }

    public AccessOptions Options { get; }

    public ISecretHasher Hasher { get; }

    public SqlAccessPolicy Policy { get; }

    public ISessionService Sessions { get; }

    public IUserAdministration Users { get; }

    public ICapabilitiesQuery Capabilities { get; }

    /// <summary>Platform + access migrations (use instead of <c>SqlPlatform.Migrations</c> until the runner accepts migration blocks).</summary>
    public Migrations.MigrationRunner Migrations { get; }
}
