namespace Hcc.Application.Access;

/// <summary>
/// Authorization port frozen by the lead for Wave 1 (P1 implements it; P2–P5 call it). Resolution order follows
/// DEC-040: principal → roles → grants (PERM id, scope, limit) − explicit denies. Every command handler calls
/// <see cref="RequireAsync"/> before it writes; queries call it before they read. The actor always comes from the
/// session (<see cref="Commands.CommandContext.PrincipalId"/>), never from the request body.
/// </summary>
public interface IAccessPolicy
{
    /// <summary>Throws <c>HccException</c> FORBIDDEN (or APPROVAL_REQUIRED when <paramref name="amountPaise"/> exceeds the grant's limit).</summary>
    Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default);

    /// <summary>True when the principal holds the permission (for menus/capabilities and field masks); never throws for a denial.</summary>
    Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default);
}

/// <summary>Optional scope of a check (contracts §1): own shift for cashiers, an assigned task/work order for housekeeping/maintenance.</summary>
public sealed record AccessScope(string Kind, long TargetId);
