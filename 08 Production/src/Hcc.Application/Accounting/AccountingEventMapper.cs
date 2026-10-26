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

    // ── Wave B1 (DEC-073, A03b-01): cash leaving the drawer. Keys banking:{id}, banking:{id}/rejected, drawing:{id} (one source per record). ──

    public static string BankingKey(long transferId, bool rejected) => "banking:" + N(transferId) + (rejected ? "/rejected" : "");

    public static string DrawingKey(long transferId) => "drawing:" + N(transferId);

    public static AccountingSource Banking(CashBankingEvent e, bool rejected)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.CashBanking(BankingKey(e.TransferId, rejected), rejected, e.BusinessDate, e.AmountPaise,
            $"{(rejected ? "Bank deposit rejected · cash back in the drawer" : "Cash deposited in bank")} · {e.Bank} · slip {e.SlipNo} · till {e.TillId.ToString(CultureInfo.InvariantCulture)} · deposit {N(e.TransferId)}");
    }

    public static AccountingSource OwnerDrawing(OwnerDrawingEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.OwnerDrawing(DrawingKey(e.TransferId), e.BusinessDate, e.AmountPaise,
            $"Owner drawing · {e.Reason} · till {e.TillId.ToString(CultureInfo.InvariantCulture)} · drawing {N(e.TransferId)}");
    }

    // ── end Wave B1 ──

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

    // ── DEC-074 (wave B3) · group master folio, staff advances, full and final ─────────────────────────────────────────────────────
    // One source per operational record (keys below). Figures a pre-DEC-074 payload lacks are supplied by the consumer (read from the
    // source tables) or, for an old full and final, booked unsplit with a flag in the narration.

    public static string GroupReceiptKey(long entryId) => "group-receipt:" + N(entryId);

    public static string GroupAttritionKey(long attritionId) => "group-attrition:" + N(attritionId);

    public static string GroupSettlementKey(long groupId) => "group-settle:" + N(groupId);

    public static string AdvanceKey(long advanceId) => "advance:" + N(advanceId);

    public static string FnfKey(long fnfId) => "fnf:" + N(fnfId);

    public static string FnfPaidKey(long fnfId) => "fnf-paid:" + N(fnfId);

    /// <summary>Master-folio receipt; <paramref name="refundOfKind"/> = the kind of the refunded receipt (payload, else read by the consumer).</summary>
    public static AccountingSource GroupReceipt(GroupReceiptRecordedEvent e, string? refundOfKind)
    {
        ArgumentNullException.ThrowIfNull(e);
        var label = e.Kind switch
        {
            "payment" => "Group master payment",
            "deposit" => "Group master deposit",
            "refund" => "Group master refund" + (refundOfKind == "deposit" ? " of deposit" : ""),
            _ => e.Kind,
        };
        return SourceRules.GroupReceipt(GroupReceiptKey(e.EntryId), e.Kind, refundOfKind, e.Tender, e.BusinessDate, e.AmountPaise,
            $"{label} · {e.Tender} · group {N(e.GroupId)} · entry {N(e.EntryId)}");
    }

    public static AccountingSource GroupAttrition(GroupAttritionPostedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.CgstPaise + e.SgstPaise != e.TaxPaise)
            throw new InvalidOperationException($"Attrition {e.AttritionId}: CGST + SGST ({e.CgstPaise} + {e.SgstPaise}) differs from the tax {e.TaxPaise}.");
        return SourceRules.GroupAttrition(GroupAttritionKey(e.AttritionId), e.BusinessDate, e.NetPaise, e.CgstPaise, e.SgstPaise,
            $"Group attrition · {(e.EntryIds?.Count ?? 0).ToString(CultureInfo.InvariantCulture)} line(s) · group {N(e.GroupId)} · attrition {N(e.AttritionId)}");
    }

    /// <summary>Group settled: the deposits still held (payload, else read by the consumer) applied to the receivable on <paramref name="date"/>.</summary>
    public static AccountingSource GroupSettlement(GroupSettledEvent e, long depositsHeldPaise, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.GroupSettlement(GroupSettlementKey(e.GroupId), date, depositsHeldPaise,
            $"Group settled · deposits applied to the company receivable · group {N(e.GroupId)} · charges {SourceRules.Rupees(e.ChargesPaise)} · received {SourceRules.Rupees(e.ReceivedPaise)}");
    }

    public static AccountingSource Advance(AdvanceGivenEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var funding = string.IsNullOrEmpty(e.Funding) ? "Bank transfer" : e.Funding;   // W2 payloads carry no funding: HCC advances are bank transfers
        return SourceRules.AdvanceGiven(AdvanceKey(e.AdvanceId), funding, e.Date, e.AmountPaise,
            $"Salary advance given · {funding} · advance {N(e.AdvanceId)} · staff {N(e.StaffId)}");
    }

    /// <summary>
    /// Full and final: the salary journal (<c>fnf:{id}</c>) and, when net pay is due, its payment (<c>fnf-paid:{id}</c>), dated
    /// <paramref name="date"/> (the payload's business date, else the consumer's). A pre-DEC-074 payload has no split: earnings go to
    /// salaries, recoveries other than the advance to F&amp;F deductions, and the narration says so.
    /// </summary>
    public static IReadOnlyList<AccountingSource> Fnf(FnfSettledEvent e, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(e);
        var split = e.GrossPaise is not null;
        var advance = e.AdvanceRecoveredPaise ?? e.AdvanceWrittenOffPaise;
        var f = split
            ? new FnfBookFigures(e.GrossPaise ?? 0, e.AdditionsPaise ?? 0, e.EmployerPfPaise ?? 0, e.EmployerEsicPaise ?? 0, e.EmployeePfPaise ?? 0, e.EmployeeEsicPaise ?? 0,
                e.ProfessionalTaxPaise ?? 0, advance, e.DeductionsPaise ?? 0, e.NetPaise)
            : new FnfBookFigures(e.EarningsPaise, 0, 0, 0, 0, 0, 0, advance, checked(e.RecoveriesPaise - advance), e.NetPaise);
        var funding = string.IsNullOrEmpty(e.Funding) ? "Bank transfer" : e.Funding;
        var open = e.AdvanceLeftOpenPaise is > 0 ? $" · advance {SourceRules.Rupees(e.AdvanceLeftOpenPaise.Value)} left open" : "";
        var list = new List<AccountingSource>
        {
            SourceRules.Fnf(FnfKey(e.FnfId), date, f,
                $"Full and final {e.Month} · staff {N(e.StaffId)} · settlement {N(e.FnfId)}{(e.NetPaise < 0 ? $" · recoverable from the leaver {SourceRules.Rupees(-e.NetPaise)}" : "")}{open}"
                + (split ? "" : " · published before DEC-074: PF / ESIC / PT not split — check against the settlement")),
        };
        if (e.NetPaise > 0)
            list.Add(SourceRules.FnfPaid(FnfPaidKey(e.FnfId), funding, date, e.NetPaise, $"Full and final net pay paid · {funding} · settlement {N(e.FnfId)}"));
        return list;
    }

    // ── end DEC-074 (wave B3) ──
}
