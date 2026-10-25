namespace Hcc.Domain.Access;

/// <summary>
/// Field restrictions (RBAC §9, contracts §1 field masks, DEC-061). Visibility is the union over the principal's active roles.
/// Housekeeping/maintenance never see hotel money (rates, folios, cash, revenue, other staff pay) but always see their own pay.
/// </summary>
public static class FieldMasks
{
    public const string GuestIdNumber = "guest.idNumber";
    public const string GuestContact = "guest.contact";
    public const string HotelMoney = "money";
    public const string AuditValues = "audit.values";
    public const string StaffList = "staff.list";
    public const string StaffPay = "staff.pay";

    public const string Visible = "visible";
    public const string Restricted = "restricted";
    public const string Last4 = "last4";
    public const string Hidden = "hidden";
    public const string ActionAndTimeOnly = "actionAndTimeOnly";
    public const string OwnOnly = "own";

    private static readonly (string Field, string[] Roles, string Otherwise)[] Table =
    [
        (GuestIdNumber, [RoleCodes.Owner, RoleCodes.Manager, RoleCodes.Reception], Restricted),
        (GuestContact, [RoleCodes.Owner, RoleCodes.Manager, RoleCodes.Reception, RoleCodes.Cashier], Last4),
        (HotelMoney, [RoleCodes.Owner, RoleCodes.Manager, RoleCodes.Reception, RoleCodes.Cashier, RoleCodes.Accountant], Hidden),
        (AuditValues, [RoleCodes.Owner, RoleCodes.Manager, RoleCodes.Accountant], ActionAndTimeOnly),
        (StaffList, [RoleCodes.Owner, RoleCodes.TechAdmin, RoleCodes.Manager], Hidden),
        // Other staff members' pay: Owner and Accountant (payroll); everyone sees their own (DEC-061).
        (StaffPay, [RoleCodes.Owner, RoleCodes.Accountant], OwnOnly),
    ];

    public static IReadOnlyDictionary<string, string> For(IReadOnlySet<string> activeRoles)
    {
        ArgumentNullException.ThrowIfNull(activeRoles);
        return Table.ToDictionary(t => t.Field, t => t.Roles.Any(activeRoles.Contains) ? Visible : t.Otherwise, StringComparer.Ordinal);
    }

    /// <summary>DEC-061: a principal may always see their own pay; other staff pay only with the staff.pay field visible.</summary>
    public static bool CanSeePayOf(IReadOnlySet<string> activeRoles, int principalId, int staffUserId) =>
        principalId == staffUserId || For(activeRoles)[StaffPay] == Visible;

    /// <summary>Applies a mask level to a string value for the wire.</summary>
    public static string? Apply(string level, string? value) => level switch
    {
        Visible => value,
        Last4 => MaskLast4(value),
        Restricted => value is null ? null : "Restricted",
        _ => null,
    };

    public static string? MaskLast4(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= 4 ? new string('•', value.Length) : new string('•', value.Length - 4) + value[^4..];
    }
}
