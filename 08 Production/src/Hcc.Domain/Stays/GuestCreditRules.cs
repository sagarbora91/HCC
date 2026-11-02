using System.Globalization;
using Hcc.Domain.Money;

namespace Hcc.Domain.Stays;

/// <summary>A guest's credit account (LF B2, gst.guest_credit_account).</summary>
public sealed record GuestCreditAccount(long GuestId, Paise Limit, bool Active, string CustomerKey);

/// <summary>LF B2 rules: refusal text (mirrors the company-credit refusal), the Manager limit and the ageing buckets of the bill-wise list.</summary>
public static class GuestCreditRules
{
    public static string? Refusal(string guestName, GuestCreditAccount? account, Paise used, Paise needed)
    {
        if (account is null || !account.Active)
            return $"{guestName} has no active credit account. The Owner opens one on the guest profile (Customers › Profile › Credit).";
        if (needed.Value > 0 && used + needed > account.Limit)
            return $"Credit limit exceeded for {guestName}: limit {Rs(account.Limit)}{(account.Limit.Value == 0 ? " (no credit)" : "")}, used {Rs(used)}, "
                + $"remaining {Rs(new Paise(Math.Max(0, account.Limit.Value - used.Value)))}; this stay on guest credit needs {Rs(needed)}. "
                + "Only the Owner can raise the limit (Customers › Profile › Credit).";
        return null;
    }

    /// <summary>Manager approves up to the PERM-PAY-005 limit (₹10,000 seed); above it the Owner.</summary>
    public static bool CanApprove(bool owner, bool manager, Paise amount, Paise managerLimit) => owner || (manager && amount <= managerLimit);

    public static string Bucket(int days) => days <= 30 ? "0–30" : days <= 60 ? "31–60" : days <= 90 ? "61–90" : "90+";

    /// <summary>Party ledger of the account: 1120/UD-&lt;customer key&gt;.</summary>
    public static string PartyLedger(string customerKey) => "1120/UD-" + customerKey;

    private static string Rs(Paise p) => "₹" + (p.Value / 100m).ToString("#,##0.00", CultureInfo.GetCultureInfo("en-IN"));
}
