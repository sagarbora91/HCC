using System.Data;
using Hcc.Application.Access;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Billing;

/// <summary>
/// Own-shift ("S") resolver for P1's policy, scope kind <c>shift</c>: the shift is at the principal's property and the
/// principal opened it. With the DEC-052 shared till this means Reception/Cashier record cash, count and refund only on a
/// shift they opened; a manager (property scope) acts on any shift. (Owner to confirm, see the integration report.)
/// </summary>
public sealed class SqlShiftScopeResolver(SqlConnectionFactory connections) : IAccessScopeResolver
{
    public string Kind => AccessScopeKinds.Shift;

    public async Task<bool> IsOwnAsync(int propertyId, int principalId, AccessScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Kind != Kind) return false;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT COUNT(*) FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id
            WHERE s.shift_id = @s AND t.property_id = @p AND s.opened_by = @u;
            """, conn);
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = scope.TargetId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }
}
