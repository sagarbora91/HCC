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

    public static AccountingSource PayRun(PayrollPostedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.PayRun("payroll:" + N(e.PayRunId), e.BusinessDate,
            new PayRunFigures(e.GrossPaise, e.EmployerPfPaise, e.EmployerEsicPaise, e.EmployeePfPaise, e.EmployeeEsicPaise, e.ProfessionalTaxPaise,
                e.AdvanceRecoveryPaise, e.OtherDeductionsPaise, e.NetPayablePaise),
            $"Pay run {e.Period} · run {N(e.PayRunId)}");
    }

    public static AccountingSource PayRunPaid(PayrollPaidEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return SourceRules.PayRunPaid("payroll-paid:" + N(e.PayRunId), e.Funding, e.BusinessDate, e.AmountPaise,
            $"Net pay paid · {e.Funding} · {e.Reference} · run {N(e.PayRunId)}");
    }
}
