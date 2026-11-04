using Hcc.Contracts;
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
    /// <summary>LF A1 (4679): rate / guest-count change of an in-house stay; executed when approved (Stays StayRateCommands).</summary>
    RateChange,
    /// <summary>LF A2 (4679): correction of a departed stay's bill (Owner); executed when approved (Invoices BillCorrectionService).</summary>
    BillCorrection,
    /// <summary>LF B2 (4679): the balance goes on the guest's own credit account at check-out (Manager within PERM-PAY-005, Owner beyond).</summary>
    GuestCredit,
    /// <summary>LF A7 (4679): void of a counter bill = a credit note (Owner); target_kind 'counter', no folio.</summary>
    CounterCredit,
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
        AdjustmentAction.RateChange => "rate-change",
        AdjustmentAction.BillCorrection => "bill-correction",
        AdjustmentAction.GuestCredit => "guest-credit",
        AdjustmentAction.CounterCredit => "counter-credit",
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
        "rate-change" => AdjustmentAction.RateChange,
        "bill-correction" => AdjustmentAction.BillCorrection,
        "guest-credit" => AdjustmentAction.GuestCredit,
        "counter-credit" => AdjustmentAction.CounterCredit,
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

    /// <summary>LF-money actions decided by their own module (<c>ILfApprovalDecisions</c>), not by the folio adjustment switch.</summary>
    public static bool IsLfMoney(AdjustmentAction action) => action is AdjustmentAction.RateChange or AdjustmentAction.BillCorrection
        or AdjustmentAction.GuestCredit or AdjustmentAction.CounterCredit;

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

/// <summary>A receipt recorded on a folio recently (id, kind, tender as stored, amount, server time), read for the PAY-08 duplicate guard.</summary>
public sealed record RecentReceipt(long ReceiptId, ReceiptKind Kind, string Tender, Paise Amount, DateTime RecordedUtc);

/// <summary>AF-DEC-1 (PAY-08): a receipt equal in kind, tender and amount to one recorded on the same folio within this window needs an explicit confirmation.</summary>
public static class DuplicateReceiptGuard
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public const string Reason = "DUPLICATE_SUSPECTED";

    /// <summary>The newest receipt of the same kind, tender (case-insensitive on the master's canonical name) and exact paise recorded within <see cref="Window"/> of <paramref name="nowUtc"/>, else null.</summary>
    public static RecentReceipt? FindEqual(IEnumerable<RecentReceipt> recent, ReceiptKind kind, string tender, Paise amount, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(recent);
        return recent
            .Where(x => x.Kind == kind && x.Amount == amount && string.Equals(x.Tender, tender, StringComparison.OrdinalIgnoreCase)
                // AF-FIX2 F3: absolute difference, so a receipt that lies "in the future" after the server clock stepped back (NTP / manual change) still counts.
                && (nowUtc - x.RecordedUtc).Duration() <= Window)
            .OrderByDescending(x => x.RecordedUtc).ThenByDescending(x => x.ReceiptId)
            .FirstOrDefault();
    }

    /// <summary>The refusal: VALIDATION with <c>details.reason</c> DUPLICATE_SUSPECTED and <c>details.receiptId</c>. Times are shown in IST (UTC+5:30).</summary>
    public static HccException Refusal(RecentReceipt found, string kindWord, DateTime nowUtc)
    {
        var minutes = Math.Max(0, (int)Math.Floor((nowUtc - found.RecordedUtc).TotalMinutes)); // never "-2 minutes ago" after a clock step back
        var at = found.RecordedUtc.AddMinutes(330).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return new HccException(HccErrorCode.VALIDATION,
            $"A {found.Tender} {kindWord} of {found.Amount} was recorded on this folio at {at} IST ({minutes} minutes ago), receipt {found.ReceiptId.ToString(System.Globalization.CultureInfo.InvariantCulture)}. If this is a second, separate payment, confirm it.",
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["reason"] = System.Text.Json.JsonSerializer.SerializeToElement(Reason),
                ["receiptId"] = System.Text.Json.JsonSerializer.SerializeToElement(found.ReceiptId),
            });
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
