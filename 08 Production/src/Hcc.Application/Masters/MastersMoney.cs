using Hcc.Application.Access;

namespace Hcc.Application.Masters;

/// <summary>
/// MA-11 (DEC-058 / DEC-061): who may SEE money values in a master list (amounts, rates, percentages). The roles with the hotel-money field
/// visible (Owner, Manager, Reception, Cashier, Accountant — <c>FieldMasks.HotelMoney</c>) each hold one of these grants; Supervisor,
/// Housekeeping, Maintenance, Tech admin and Restaurant hold none, so the money fields of a list read by them are sent as null.
/// </summary>
public static class MastersMoney
{
    public static readonly IReadOnlyList<string> ViewerPermissions = [MastersPermissions.ManageMoney, "PERM-RPT-002", "PERM-RES-001", "PERM-EXP-001"];

    public static async Task<bool> SeesMoneyAsync(IAccessPolicy policy, int propertyId, int principalId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        foreach (var perm in ViewerPermissions)
            if (await policy.HasAsync(propertyId, principalId, perm, null, ct).ConfigureAwait(false)) return true;
        return false;
    }
}
