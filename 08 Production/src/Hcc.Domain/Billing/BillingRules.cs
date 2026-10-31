using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Billing;

/// <summary>
/// Folio adjustment actions (contracts §3) plus the cashier's cash-difference approval (PERM-SHF-002) and, DEC-073, the two cash
/// hand-offs that need a second person: a bank deposit (confirmed by a different Manager or Owner) and a till transfer (accepted by the
/// receiver on their own open shift).
/// </summary>
public enum AdjustmentAction
{
    Discount,
    Refund,
    Credit,
    Correction,
    CashDifference,
    CashBanking,
    TillTransfer,
}

/// <summary>Approval states (prototype <c>Approval.status</c>).</summary>
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Executed,
}

/// <summary>Charge kinds (SQL <c>bill.charge.kind</c>). A correction is a reversal line linked to the original.</summary>
public enum ChargeKind
{
    Room,
    Service,
    Discount,
    Correction,
    Food, // ACC-2 parity (contract §8, rulebook A3): F&B charged to a folio (POS charge-to-folio, extras of GST class food) → line:food (4200)
}

public static class BillingCodes
{
    public static string Code(AdjustmentAction a) => a switch
    {
        AdjustmentAction.Discount => "discount",
        AdjustmentAction.Refund => "refund",
        AdjustmentAction.Credit => "credit",
        AdjustmentAction.Correction => "correction",
        AdjustmentAction.CashDifference => "cash-difference",
        AdjustmentAction.CashBanking => "cash-banking",
        AdjustmentAction.TillTransfer => "till-transfer",
        _ => throw new ArgumentOutOfRangeException(nameof(a), a, "Unknown action."),
    };

    public static AdjustmentAction ParseAction(string? code) => code switch
    {
        "discount" => AdjustmentAction.Discount,
        "refund" => AdjustmentAction.Refund,
        "credit" => AdjustmentAction.Credit,
        "correction" => AdjustmentAction.Correction,
        "cash-difference" => AdjustmentAction.CashDifference,
        "cash-banking" => AdjustmentAction.CashBanking,
        "till-transfer" => AdjustmentAction.TillTransfer,
        _ => throw HccException.Validation("Action must be discount, refund, credit or correction."),
    };

    public static string Code(ChargeKind k) => k switch
    {
        ChargeKind.Room => "room",
        ChargeKind.Service => "service",
        ChargeKind.Discount => "discount",
        ChargeKind.Correction => "correction",
        ChargeKind.Food => "food",
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "Unknown charge kind."),
    };

    public static ChargeKind ParseChargeKind(string code) => code switch
    {
        "room" => ChargeKind.Room,
        "service" => ChargeKind.Service,
        "discount" => ChargeKind.Discount,
        "correction" => ChargeKind.Correction,
        "food" => ChargeKind.Food,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown charge kind."),
    };

    public static ApprovalStatus ParseStatus(string code) => Enum.Parse<ApprovalStatus>(code, ignoreCase: false);
}

/// <summary>
/// Approval rules ported from the prototype (<c>03 Prototype/src/domain/streams/folio.ts</c>, handlers/folio.ts) and DEC-041:
/// a request is bound to action + target + amount + target version; it expires after 24 hours; the requester never decides it.
/// </summary>
public static class ApprovalRules
{
    /// <summary>RBAC §7: approvals expire after 24 hours (prototype FOLIO_APPROVAL_TTL_MINUTES).</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromHours(24);

    /// <summary>
    /// A Pending request past its expiry reads Expired; an Approved refund/credit that was not executed in time also expires
    /// (prototype <c>folioApprovalExpired</c>). Executed, rejected and expired records are final.
    /// </summary>
    public static bool IsExpired(ApprovalStatus status, AdjustmentAction action, DateTime expiresUtc, DateTime nowUtc)
        => !NeverLapses(action) && nowUtc > expiresUtc && (status == ApprovalStatus.Pending || (status == ApprovalStatus.Approved && action != AdjustmentAction.Discount));

    /// <summary>
    /// DEC-073: a bank deposit or a till transfer waits until it is decided. The cash has already left the drawer, so the request never
    /// lapses (it is stored with a far expiry as well, so SQL checks that read <c>expires_utc</c> keep seeing it).
    /// </summary>
    public static bool NeverLapses(AdjustmentAction action) => action is AdjustmentAction.CashBanking or AdjustmentAction.TillTransfer;

    /// <summary>Stored expiry of a request that never lapses (<see cref="NeverLapses"/>).</summary>
    public static readonly TimeSpan NoLapse = TimeSpan.FromDays(3650);

    public static ApprovalStatus Effective(ApprovalStatus status, AdjustmentAction action, DateTime expiresUtc, DateTime nowUtc)
        => IsExpired(status, action, expiresUtc, nowUtc) ? ApprovalStatus.Expired : status;

    /// <summary>Whether a request still holds part of a receipt's refundable amount.</summary>
    public static bool IsOutstanding(ApprovalStatus status, AdjustmentAction action, DateTime expiresUtc, DateTime nowUtc)
        => status is ApprovalStatus.Pending or ApprovalStatus.Approved && !IsExpired(status, action, expiresUtc, nowUtc);
}

/// <summary>Refund rules (Financial Rulebook §5, DEC-031; spike T06/C3).</summary>
public static class RefundRules
{
    /// <summary>Refundable remainder = original payment − executed refunds − outstanding (pending/approved, unexpired) requests, never below 0.</summary>
    public static Paise Remainder(Paise captured, Paise refunded, Paise outstanding)
    {
        var r = captured - refunded - outstanding;
        return r < Paise.Zero ? Paise.Zero : r;
    }

    /// <summary>A departed (settled) stay may refund only its credit balance, less refunds already requested.</summary>
    public static Paise DepartedLimit(Paise balance, Paise outstandingOnFolio)
    {
        var credit = balance < Paise.Zero ? -balance : Paise.Zero;
        return credit - outstandingOnFolio;
    }
}

/// <summary>Payment tenders (prototype FOLIO_TENDERS). Company credit is applied only through an approved credit request.</summary>
public static class Tenders
{
    public const string Cash = "Cash";
    public const string Upi = "UPI";
    public const string Card = "Card";
    public const string BankTransfer = "Bank transfer";
    public const string CompanyCredit = "Company credit";

    public static readonly IReadOnlyList<string> All = [Cash, Upi, Card, BankTransfer];

    public static string Validate(string? tender, string? reference)
    {
        if (tender is null || !All.Contains(tender, StringComparer.Ordinal)) throw HccException.Validation("Select a payment method (Cash, UPI, Card or Bank transfer).");
        if (tender != Cash && string.IsNullOrWhiteSpace(reference)) throw HccException.Validation("Enter the transaction reference for a non-cash payment.");
        return tender;
    }
}

/// <summary>Text field limits shared by billing commands.</summary>
public static class BillingText
{
    public static string Required(string? value, int max, string message)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw HccException.Validation(message);
        if (v.Length > max) throw HccException.Validation($"Keep it to {max} characters.");
        return v;
    }

    public static string Optional(string? value, int max)
    {
        var v = value?.Trim() ?? string.Empty;
        if (v.Length > max) throw HccException.Validation($"Keep it to {max} characters.");
        return v;
    }
}
