using System.Globalization;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Application.Accounting;

/// <summary>
/// Turns consumed outbox payloads into accounting sources (one per operational record, stable source key). Pure: the consumer
/// supplies the stored CGST/SGST of a charge (agent C's per-charge GST record via <c>IChargeGstReader</c>) and anything it had to
/// read from the source tables.
/// </summary>
public static class AccountingEventMapper
{
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static string ChargeKey(long chargeId) => "charge:" + N(chargeId);

    public static string ReceiptKey(long receiptId) => "receipt:" + N(receiptId);

    public static string CashDifferenceKey(long shiftId) => $"shift:{N(shiftId)}/cash-difference";

    public static string LineLabel(string kind, DateOnly serviceDate) => kind switch
    {
        "room" => "Room night · " + serviceDate.ToString("dd MMM", CultureInfo.InvariantCulture),
        "service" => "Service charge",
        "discount" => "Discount",
        "correction" => "Correction / reversal",
        _ => kind,
    };

    public static AccountingSource Charge(long folioId, long chargeId, string kind, DateOnly serviceDate, long amountPaise, long cgstPaise, long sgstPaise)
        => SourceRules.FolioLine(ChargeKey(chargeId), kind, serviceDate, amountPaise, cgstPaise, sgstPaise,
            $"{LineLabel(kind, serviceDate)} · folio {N(folioId)} · charge {N(chargeId)}");

    public static AccountingSource Receipt(long folioId, long receiptId, string kind, string tender, DateOnly businessDate, long amountPaise)
    {
        var label = kind switch
        {
            "payment" => "Payment received",
            "deposit" => "Deposit received",
            "refund" => "Payment refunded",
            "deposit-return" => "Deposit returned",
            "company-credit" => "Company credit",
            _ => kind,
        };
        var narration = kind == "company-credit" ? $"{label} · folio {N(folioId)} · receipt {N(receiptId)}" : $"{label} · {tender} · folio {N(folioId)} · receipt {N(receiptId)}";
        return SourceRules.FolioReceipt(ReceiptKey(receiptId), kind, tender, businessDate, amountPaise, narration);
    }

    public static AccountingSource CashDifference(long shiftId, int tillId, DateOnly businessDate, long expectedPaise, long countedPaise)
    {
        var diff = checked(countedPaise - expectedPaise);
        return SourceRules.CashDifference(CashDifferenceKey(shiftId), businessDate, diff,
            $"Cash {(diff > 0 ? "over" : "short")} accepted at shift close · shift {N(shiftId)} · till {tillId.ToString(CultureInfo.InvariantCulture)}");
    }

    public static AccountingSource PosBill(PosBillSettledEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var voidBill = e.Kind == "void";
        return SourceRules.PosBill((voidBill ? "pos-void:" : "pos:") + N(e.BillId), e.Kind, e.Tender, e.BusinessDate, e.TotalPaise, e.NetPaise, e.CgstPaise, e.SgstPaise,
            $"{(voidBill ? "Outlet bill void" : "Outlet bill")} · {e.Outlet} · {e.Tender} · {e.Reference} · bill {N(e.BillId)}");
    }

    public static AccountingSource Expense(ExpensePostedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var reversal = e.ReversalOf is not null;
        return SourceRules.Expense("expense:" + N(e.ExpenseId), reversal, e.Category, e.Funding, e.BusinessDate, e.AmountPaise,
            $"{(reversal ? $"Reversal of {N(e.ReversalOf!.Value)} · " : "")}{e.Vendor} · {e.Category} · {e.Funding}");
    }

    /// <summary>Pay-run voucher of one lock: lock 1 keeps the Wave 2 key <c>payroll:{run}</c>; a re-lock is <c>payroll:{run}/lock{n}</c>.</summary>
    public static string PayRunKey(long runId, int lockSeq) => "payroll:" + N(runId) + LockSuffix(lockSeq);

    public static string PayRunPaidKey(long runId, int lockSeq) => "payroll-paid:" + N(runId) + LockSuffix(lockSeq);

    /// <summary>The reversal booked when that lock is unlocked (unique per lock → consumed exactly once).</summary>
    public static string PayRunReversalKey(long runId, int lockSeq) => $"payroll-unlock:{N(runId)}/lock{lockSeq.ToString(CultureInfo.InvariantCulture)}";

    private static string LockSuffix(int lockSeq) => lockSeq <= 1 ? "" : "/lock" + lockSeq.ToString(CultureInfo.InvariantCulture);

    public static AccountingSource PayRun(PayrollPostedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.PayRun(PayRunKey(e.PayRunId, e.LockSeq), e.BusinessDate,
            new PayRunFigures(e.GrossPaise, e.EmployerPfPaise, e.EmployerEsicPaise, e.EmployeePfPaise, e.EmployeeEsicPaise, e.ProfessionalTaxPaise,
                e.AdvanceRecoveryPaise, e.OtherDeductionsPaise, e.NetPayablePaise),
            $"Pay run {e.Period} · run {N(e.PayRunId)}{(e.LockSeq > 1 ? $" · lock {e.LockSeq.ToString(CultureInfo.InvariantCulture)}" : "")}");
    }

    public static AccountingSource PayRunPaid(PayrollPaidEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.PayRunPaid(PayRunPaidKey(e.PayRunId, e.LockSeq), e.Funding, e.BusinessDate, e.AmountPaise,
            $"Net pay paid · {e.Funding} · {e.Reference} · run {N(e.PayRunId)}");
    }

    /// <summary>
    /// Unlock of a pay-run lock: the exact negative of that lock's posted voucher (<paramref name="original"/> entries), dated the unlock's
    /// business date. When a bank file was issued its payment voucher is kept, and the narration flags it.
    /// </summary>
    public static AccountingSource PayRunReversal(PayRunUnlockedEvent e, DateOnly date, string originalVoucherKey, IReadOnlyList<SourceEntry> original, bool paymentBooked)
    {
        ArgumentNullException.ThrowIfNull(e);
        var lockText = e.LockSeq.ToString(CultureInfo.InvariantCulture);
        var flag = e.BankFilesIssued > 0 || paymentBooked
            ? " · BANK FILE ALREADY ISSUED: net-pay payment voucher kept (not reversed); do not pay twice on re-lock"
            : "";
        return SourceRules.PayRunReversal(PayRunReversalKey(e.RunId, e.LockSeq), date, original,
            $"Reversal of pay run {e.Month} · run {N(e.RunId)} · lock {lockText} unlocked · reverses {originalVoucherKey}{flag}");
    }
}
