namespace Hcc.Application.Hall;

/// <summary>F6 hall booking permissions (F6-CONTRACT §4; migration 4345). Role-specific limits (Cashier = advances and settling only, Reception cancels as a request,
/// discount limits) stay in <see cref="Hcc.Domain.Hall.HallRules"/>; the policy is the first gate on every command and read.</summary>
public static class HallPermissions
{
    /// <summary>Enquiries: add, follow up, mark lost (Owner, Manager, Reception).</summary>
    public const string Enquiries = "PERM-HAL-001";

    /// <summary>Quotations: create, revise, send (Owner, Manager, Reception; the Accountant views).</summary>
    public const string Quotations = "PERM-HAL-002";

    /// <summary>Accept a quotation and take advances (Owner, Manager, Reception; the Cashier takes advances only).</summary>
    public const string Accept = "PERM-HAL-003";

    /// <summary>Event-day bill, cancel, refund (Owner, Manager, Reception; the Cashier settles).</summary>
    public const string Bill = "PERM-HAL-004";

    /// <summary>T&amp;C templates and the placeholder override (Owner only).</summary>
    public const string Templates = "PERM-HAL-005";

    /// <summary>Halls master and menu packages (Owner; menus also Manager).</summary>
    public const string Masters = "PERM-HAL-006";
}
