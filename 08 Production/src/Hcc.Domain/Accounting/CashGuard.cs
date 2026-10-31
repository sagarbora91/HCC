using Hcc.Contracts;
using System.Globalization;
using System.Text.Json;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Accounting;

/// <summary>Direction of a cash movement the guard checks.</summary>
public enum CashFlowKind
{
    Receipt,
    Payment,
}

/// <summary>The three limits in force on a date (paise; Master configuration keys <c>cash.receiptLimit</c>, <c>cash.paymentLimit</c>, <c>cash.transportLimit</c>).</summary>
public sealed record CashGuardLimits(long ReceiptLimitPaise, long PaymentLimitPaise, long TransportLimitPaise)
{
    /// <summary>The seed limits (₹2,00,000 receipts; ₹10,000 payments; ₹35,000 goods transport) — same as the prototype's ACC_CASH_SEED.</summary>
    public static CashGuardLimits Seed { get; } = new(CashGuard.SeedReceiptLimitPaise, CashGuard.SeedPaymentLimitPaise, CashGuard.SeedTransportLimitPaise);
}

/// <summary>What the guard decided: <see cref="Refused"/> (B9, a receipt) or <see cref="DeductionLost"/> (B10, a payment; allowed with a warning).</summary>
public sealed record CashGuardResult(bool Refused, bool DeductionLost, string Message, long LimitPaise, long TotalPaise)
{
    public static CashGuardResult Pass(long limit = 0, long total = 0) => new(false, false, string.Empty, limit, total);
}

/// <summary>
/// ACC-2 flows · cash guards (02 Planning/agents/ACC2-POSTING-CONTRACT.md §7; rulebook §5 B9, B10; prototype
/// <c>03 Prototype/src/domain/streams/accounting-guards.ts</c> <c>accCashGuard</c>, same limits and messages).
/// B9: cash received from one party totalling ₹2,00,000 or more on one day, or for one booking / transaction / event, is REFUSED
/// (<see cref="Reason"/> CASH_LIMIT; Income-tax Act 2025 s.186, old s.269ST). B10: cash paid to one party totalling MORE than ₹10,000 on one
/// day (₹35,000 for goods transport) is allowed with a warning and the record is marked "deduction lost" (old s.40A(3)). Section numbers:
/// VERIFY (rulebook OP-10, CA). Pure: the caller supplies the totals already recorded (without this record).
/// </summary>
public static class CashGuard
{
    public const string Reason = "CASH_LIMIT";
    public const string ReceiptLimitKey = "cash.receiptLimit";
    public const string PaymentLimitKey = "cash.paymentLimit";
    public const string TransportLimitKey = "cash.transportLimit";
    public const long SeedReceiptLimitPaise = 20_000_000;
    public const long SeedPaymentLimitPaise = 1_000_000;
    public const long SeedTransportLimitPaise = 3_500_000;
    public const string Verify = "VERIFY section numbers — CA to confirm (rulebook OP-10)";

    /// <summary>B9: refused when the day's total from the party, or the scope's total (booking / transaction / event), reaches the limit with this amount.</summary>
    public static CashGuardResult Receipt(long dayTotalBefore, long scopeTotalBefore, bool scoped, long amountPaise, long limitPaise, string who, DateOnly date)
    {
        if (amountPaise <= 0) return CashGuardResult.Pass(limitPaise);
        var day = checked(dayTotalBefore + amountPaise);
        var scope = scoped ? checked(scopeTotalBefore + amountPaise) : 0;
        if (day < limitPaise && scope < limitPaise) return CashGuardResult.Pass(limitPaise, Math.Max(day, scope));
        var total = Math.Max(day, scope);
        return new(true, false, ReceiptMessage(who, total, limitPaise, scope >= limitPaise && scope >= day, date), limitPaise, total);
    }

    /// <summary>B10: allowed; above the limit the result carries <see cref="CashGuardResult.DeductionLost"/> and the warning.</summary>
    public static CashGuardResult Payment(long dayTotalBefore, long amountPaise, long limitPaise, string who, DateOnly date)
    {
        if (amountPaise <= 0) return CashGuardResult.Pass(limitPaise);
        var day = checked(dayTotalBefore + amountPaise);
        if (day <= limitPaise) return CashGuardResult.Pass(limitPaise, day);
        var warning = PaymentWarning(who, day, limitPaise, date);
        return new(false, true, warning, limitPaise, day);
    }

    /// <summary>Refusal text (B9; prototype <c>accCashReceiptMessage</c>).</summary>
    public static string ReceiptMessage(string who, long total, long limit, bool scoped, DateOnly date)
        => $"Cash of {Rs(total)} from {who} {(scoped ? "for this booking / transaction" : "on " + Day(date))} reaches the {Rs(limit)} cash-receipt limit — take it by UPI, card or bank transfer (Income-tax Act 2025 s.186, old s.269ST; {Verify}).";

    /// <summary>Warning text (B10; prototype <c>accCashPaymentWarning</c>).</summary>
    public static string PaymentWarning(string who, long total, long limit, DateOnly date)
        => $"Cash paid to {who} on {Day(date)} totals {Rs(total)}, more than {Rs(limit)}: the income-tax deduction is lost (old s.40A(3); {Verify}) — pay by bank transfer or UPI to keep it. Marked “deduction lost”.";

    /// <summary>The typed refusal (VALIDATION with <c>details.reason</c> CASH_LIMIT, limit and total in paise).</summary>
    public static HccException Refusal(CashGuardResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var details = new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonSerializer.SerializeToElement(Reason),
            ["limitPaise"] = JsonSerializer.SerializeToElement(r.LimitPaise),
            ["totalPaise"] = JsonSerializer.SerializeToElement(r.TotalPaise),
        };
        return new HccException(HccErrorCode.VALIDATION, r.Message, details);
    }

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Indian grouping like the prototype's <c>money()</c>: ₹2,00,000 / ₹11,000 / ₹1,234.50.</summary>
    public static string Rs(long paise)
    {
        var neg = paise < 0;
        var a = Math.Abs(paise);
        var rupees = (a / 100).ToString(CultureInfo.InvariantCulture);
        var head = rupees.Length > 3 ? rupees[..^3] : string.Empty;
        var tail = rupees.Length > 3 ? rupees[^3..] : rupees;
        var groups = new List<string>();
        while (head.Length > 2)
        {
            groups.Insert(0, head[^2..]);
            head = head[..^2];
        }

        if (head.Length > 0) groups.Insert(0, head);
        groups.Add(tail);
        var text = string.Join(",", groups);
        var p = a % 100;
        return (neg ? "-" : string.Empty) + "₹" + text + (p == 0 ? string.Empty : "." + p.ToString("00", CultureInfo.InvariantCulture));
    }
}
