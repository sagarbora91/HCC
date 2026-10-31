using Hcc.Domain.Accounting;

namespace Hcc.Domain.Ota;

/// <summary>
/// ACC-2 flows · an OTA payout from the channel's statement (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4; rulebook A13, §4.14; DEC-150).
/// Shape and arithmetic of the statement — prototype <c>chnPayoutProblem</c> (<c>03 Prototype/src/domain/streams/channels.ts</c>), same
/// messages. The open receivable (gross ≤ the channel's OTA credits less earlier payouts) is checked by the command.
/// </summary>
public static class OtaPayoutRules
{
    public const int StatementRefMax = 40;
    public const long MaxGrossPaise = 100_000_000_000;

    /// <summary>Why the statement figures cannot be recorded (null = fine). Amounts in paise.</summary>
    public static string? Problem(string? statementRef, long gross, long commission, long commissionGst, long tcs, long net)
    {
        var r = (statementRef ?? string.Empty).Trim();
        if (r.Length == 0) return "Enter the OTA statement reference.";
        if (r.Length > StatementRefMax) return "Keep the statement reference to 40 characters.";
        foreach (var (v, label) in new[] { (gross, "Gross"), (commission, "Commission"), (commissionGst, "GST on the commission"), (tcs, "TCS"), (net, "Net received") })
            if (v < 0) return label + ": enter an amount of ₹0 or more.";
        if (gross <= 0 || net <= 0) return "Enter the gross amount and the net amount received (both above ₹0).";
        if (gross > MaxGrossPaise) return "The gross amount is too large.";
        var sum = checked(net + commission + commissionGst + tcs);
        if (sum != gross)
            return $"Gross {CashGuard.Rs(gross)} must equal net {CashGuard.Rs(net)} + commission {CashGuard.Rs(commission)} + GST on it {CashGuard.Rs(commissionGst)} + TCS {CashGuard.Rs(tcs)} (= {CashGuard.Rs(sum)}).";
        return null;
    }

    /// <summary>The refusal when the gross is more than the channel's open receivable (OTA credits at check-out less payouts).</summary>
    public static string OverReceivable(string channel, long gross, long open)
        => $"The gross {CashGuard.Rs(gross)} is more than {channel}'s open receivable {CashGuard.Rs(Math.Max(0, open))} (OTA credits at check-out less payouts).";
}
