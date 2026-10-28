using System.Globalization;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Application.Accounting;

/// <summary>
/// Turns consumed outbox payloads into accounting sources (one per operational record, stable source key). Pure: the consumer
/// supplies the stored CGST/SGST of a charge (agent C's per-charge GST record via <c>IChargeGstReader</c>) and anything it had to
/// read from the source tables. ACC-1 (contract §3): every factory takes the parties the consumer resolved (<see cref="PartyContext"/>: the
/// tender's settles-to account, the company, the Udhaar customer, the staff member, the vendor, the bank banked into); null = none named
/// (the control ledger takes the amount and the day shows a <c>no-party</c> warning).
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

    public static AccountingSource Receipt(long folioId, long receiptId, string kind, string tender, DateOnly businessDate, long amountPaise, PartyContext? parties = null)
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
        return SourceRules.WithParties(SourceRules.FolioReceipt(ReceiptKey(receiptId), kind, tender, businessDate, amountPaise, narration), parties);
    }

    public static string CompanyPaymentKey(long paymentId) => "company-payment:" + N(paymentId);

    /// <summary>DEC-076: a company's payment against its company-credit receivable (Dr tender · Cr company receivables).</summary>
    public static AccountingSource CompanyPayment(CompanyPaymentRecordedEvent e, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        // ACC-1: the company is always named by the event (1110/CMP-<id>); the tender's account comes from the consumer.
        return SourceRules.WithParties(SourceRules.CompanyPayment(CompanyPaymentKey(e.PaymentId), e.Tender, e.BusinessDate, e.AmountPaise,
            $"Company payment received · {e.Company} · {e.Tender} · {e.Reference} · payment {N(e.PaymentId)}"
                + (e.TdsPaise != 0 ? $" · TDS ₹{SourceRules.Rupees(e.TdsPaise)} deducted" : ""), e.TdsPaise), // ACC-2 tds: Dr 1360
            (parties ?? PartyContext.None) with { Company = PartyLedgers.Company(e.CompanyId) });
    }

    public static AccountingSource CashDifference(long shiftId, int tillId, DateOnly businessDate, long expectedPaise, long countedPaise, PartyContext? parties = null)
    {
        var diff = checked(countedPaise - expectedPaise);
        return SourceRules.WithParties(SourceRules.CashDifference(CashDifferenceKey(shiftId), businessDate, diff,
            $"Cash {(diff > 0 ? "over" : "short")} accepted at shift close · shift {N(shiftId)} · till {tillId.ToString(CultureInfo.InvariantCulture)}"), parties);
    }

    public static AccountingSource PosBill(PosBillSettledEvent e, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        var voidBill = e.Kind == "void";
        return SourceRules.WithParties(SourceRules.PosBill((voidBill ? "pos-void:" : "pos:") + N(e.BillId), e.Kind, e.Tender, e.BusinessDate, e.TotalPaise, e.NetPaise, e.CgstPaise, e.SgstPaise,
            $"{(voidBill ? "Outlet bill void" : "Outlet bill")} · {e.Outlet} · {e.Tender} · {e.Reference} · bill {N(e.BillId)}"), parties);
    }

    public static AccountingSource Expense(ExpensePostedEvent e, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        var reversal = e.ReversalOf is not null;
        // DEC-135: the GST of a bill marked ITC allowed goes to the GST input-credit ledger (an event without the fields books as before).
        var itc = string.Equals(e.Itc, "allowed", StringComparison.Ordinal) ? e.GstPaise ?? 0 : 0;
        return SourceRules.WithParties(SourceRules.Expense("expense:" + N(e.ExpenseId), reversal, e.Category, e.Funding, e.BusinessDate, e.AmountPaise,
            $"{(reversal ? $"Reversal of {N(e.ReversalOf!.Value)} · " : "")}{e.Vendor} · {e.Category} · {e.Funding}", itc), parties);
    }

    public static string UdhaarPaymentKey(long settlementId) => "udhaar-payment:" + N(settlementId);

    /// <summary>DEC-130: an Udhaar payment taken as a desk receipt (Dr tender · Cr Udhaar receivables; narration as the prototype's).</summary>
    public static AccountingSource UdhaarPayment(UdhaarPaymentRecordedEvent e, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.WithParties(SourceRules.UdhaarPayment(UdhaarPaymentKey(e.SettlementId), e.Tender, e.BusinessDate, e.AmountPaise,
            $"Udhaar received · {e.Customer} · {e.Tender}{(string.IsNullOrWhiteSpace(e.Reference) ? "" : " · " + e.Reference)} · payment {N(e.SettlementId)}"), parties);
    }

    /// <summary>ACC-1 (contract §4): source key of an Udhaar credit raised (prototype <c>udhaar:&lt;receivable id&gt;</c>).</summary>
    public static string UdhaarCreditKey(long receivableId) => "udhaar:" + N(receivableId);

    /// <summary>
    /// ACC-1 (contract §4, rulebook A17, DEC-144): the Udhaar credit raised = Sales (Dr 1120/&lt;customer&gt; gross · Cr revenue of the kind ·
    /// Cr CGST · Cr SGST). The customer party is derived from the event (mobile, else the name); narration as the prototype's.
    /// </summary>
    public static AccountingSource UdhaarCredit(UdhaarCreditRaisedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var kind = e.Kind is "restaurant" or "room" ? e.Kind : "other";
        var gst = e.GstBps > 0 ? " · GST " + (e.GstBps / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %" : "";
        return SourceRules.UdhaarCredit(UdhaarCreditKey(e.ReceivableId), e.BusinessDate, kind, e.AmountPaise, e.GstBps, PartyLedgers.Udhaar(e.Customer, e.Mobile),
            $"Udhaar credit · {e.Customer} · {kind}{gst} · credit UD-{N(e.ReceivableId)}");
    }

    // ── Wave B1 (DEC-073, A03b-01): cash leaving the drawer. Keys banking:{id}, banking:{id}/rejected, drawing:{id} (one source per record). ──

    public static string BankingKey(long transferId, bool rejected) => "banking:" + N(transferId) + (rejected ? "/rejected" : "");

    public static string DrawingKey(long transferId) => "drawing:" + N(transferId);

    public static AccountingSource Banking(CashBankingEvent e, bool rejected, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.WithParties(SourceRules.CashBanking(BankingKey(e.TransferId, rejected), rejected, e.BusinessDate, e.AmountPaise,
            $"{(rejected ? "Bank deposit rejected · cash back in the drawer" : "Cash deposited in bank")} · {e.Bank} · slip {e.SlipNo} · till {e.TillId.ToString(CultureInfo.InvariantCulture)} · deposit {N(e.TransferId)}"), parties);
    }

    public static AccountingSource OwnerDrawing(OwnerDrawingEvent e, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.WithParties(SourceRules.OwnerDrawing(DrawingKey(e.TransferId), e.BusinessDate, e.AmountPaise,
            $"Owner drawing · {e.Reason} · till {e.TillId.ToString(CultureInfo.InvariantCulture)} · drawing {N(e.TransferId)}"), parties);
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

    public static AccountingSource PayRunPaid(PayrollPaidEvent e, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.WithParties(SourceRules.PayRunPaid(PayRunPaidKey(e.PayRunId, e.LockSeq), e.Funding, e.BusinessDate, e.AmountPaise,
            $"Net pay paid · {e.Funding} · {e.Reference} · run {N(e.PayRunId)}"), parties);
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
    public static AccountingSource GroupReceipt(GroupReceiptRecordedEvent e, string? refundOfKind, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        var label = e.Kind switch
        {
            "payment" => "Group master payment",
            "deposit" => "Group master deposit",
            "refund" => "Group master refund" + (refundOfKind == "deposit" ? " of deposit" : ""),
            _ => e.Kind,
        };
        return SourceRules.WithParties(SourceRules.GroupReceipt(GroupReceiptKey(e.EntryId), e.Kind, refundOfKind, e.Tender, e.BusinessDate, e.AmountPaise,
            $"{label} · {e.Tender} · group {N(e.GroupId)} · entry {N(e.EntryId)}"), parties);
    }

    public static AccountingSource GroupAttrition(GroupAttritionPostedEvent e, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.CgstPaise + e.SgstPaise != e.TaxPaise)
            throw new InvalidOperationException($"Attrition {e.AttritionId}: CGST + SGST ({e.CgstPaise} + {e.SgstPaise}) differs from the tax {e.TaxPaise}.");
        return SourceRules.WithParties(SourceRules.GroupAttrition(GroupAttritionKey(e.AttritionId), e.BusinessDate, e.NetPaise, e.CgstPaise, e.SgstPaise,
            $"Group attrition · {(e.EntryIds?.Count ?? 0).ToString(CultureInfo.InvariantCulture)} line(s) · group {N(e.GroupId)} · attrition {N(e.AttritionId)}"), parties);
    }

    /// <summary>Group settled: the deposits still held (payload, else read by the consumer) applied to the receivable on <paramref name="date"/>.</summary>
    public static AccountingSource GroupSettlement(GroupSettledEvent e, long depositsHeldPaise, DateOnly date, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.WithParties(SourceRules.GroupSettlement(GroupSettlementKey(e.GroupId), date, depositsHeldPaise,
            $"Group settled · deposits applied to the company receivable · group {N(e.GroupId)} · charges {SourceRules.Rupees(e.ChargesPaise)} · received {SourceRules.Rupees(e.ReceivedPaise)}"), parties);
    }

    public static AccountingSource Advance(AdvanceGivenEvent e, PartyContext? parties = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        var funding = string.IsNullOrEmpty(e.Funding) ? "Bank transfer" : e.Funding;   // W2 payloads carry no funding: HCC advances are bank transfers
        // ACC-1: the staff member is always named by the event (1200/STF-<id>); the funding account comes from the consumer.
        return SourceRules.WithParties(SourceRules.AdvanceGiven(AdvanceKey(e.AdvanceId), funding, e.Date, e.AmountPaise,
            $"Salary advance given · {funding} · advance {N(e.AdvanceId)} · staff {N(e.StaffId)}"), (parties ?? PartyContext.None) with { Staff = PartyLedgers.Staff(e.StaffId) });
    }

    /// <summary>
    /// Full and final: the salary journal (<c>fnf:{id}</c>) and, when net pay is due, its payment (<c>fnf-paid:{id}</c>), dated
    /// <paramref name="date"/> (the payload's business date, else the consumer's). A pre-DEC-074 payload has no split: earnings go to
    /// salaries, recoveries other than the advance to F&amp;F deductions, and the narration says so.
    /// </summary>
    public static IReadOnlyList<AccountingSource> Fnf(FnfSettledEvent e, DateOnly date, PartyContext? parties = null)
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
        // ACC-1: the leaver is named by the event (1200/STF-<id> for the advance recovered and an amount recoverable from the leaver).
        var p = (parties ?? PartyContext.None) with { Staff = PartyLedgers.Staff(e.StaffId) };
        return list.Select(x => SourceRules.WithParties(x, p)).ToList();
    }

    // ── end DEC-074 (wave B3) ──
}
