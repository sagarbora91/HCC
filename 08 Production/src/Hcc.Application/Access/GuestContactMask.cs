using Hcc.Domain.Stays;

namespace Hcc.Application.Access;

/// <summary>
/// The <c>guest.contact</c> field mask (RBAC §9) for every read that returns a guest's phone or e-mail (F-L06): visible only to holders
/// of the guest registration permission PERM-GST-002, the rule Stays and CRM already apply; everyone else — for example a housekeeper or
/// accountant given a reservation, OTA or web-booking permission by a user exception — sees "••••••1234" / "g•••@domain".
/// With the seeded roles this changes nothing (Owner, Manager and Reception hold both permissions).
/// </summary>
public static class GuestContactMask
{
    public const string Permission = "PERM-GST-002";

    public static Task<bool> CanSeeAsync(IAccessPolicy policy, int propertyId, int principalId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.HasAsync(propertyId, principalId, Permission, null, ct);
    }

    public static string? Phone(bool visible, string? phone) => visible || phone is null ? phone : GuestRules.MaskPhone(phone);

    public static string? Email(bool visible, string? email) => visible || email is null ? email : GuestRules.MaskEmail(email);
}
