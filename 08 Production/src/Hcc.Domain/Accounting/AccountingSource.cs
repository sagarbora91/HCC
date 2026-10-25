using System.Globalization;

namespace Hcc.Domain.Accounting;

/// <summary>Voucher type (prototype <c>ACCVoucherType</c>); numbering prefix SAL/RCT/PMT/JRN.</summary>
public enum VoucherType
{
    Sales,
    Receipt,
    Payment,
    Journal,
}

/// <summary>One side of a source record, still keyed by classification (resolved to a ledger through the mapping at read time).</summary>
public sealed record SourceEntry(string MapKey, long DebitPaise, long CreditPaise);

/// <summary>
/// A source record for accounting (one per operational record: folio line, receipt, outlet bill, expense, cash difference, pay
/// run). Its entries are classification keys, so the current mapping applies to unlocked dates while locked days keep their
/// snapshot (spec §2). Entries balance by construction except where the publishing module sent inconsistent figures.
/// </summary>
public sealed record AccountingSource(string SourceKey, string Kind, string Sub, DateOnly BusinessDate, VoucherType Type, string Narration,
    IReadOnlyList<SourceEntry> Entries)
{
    public long Debit => Entries.Sum(e => e.DebitPaise);

    public long Credit => Entries.Sum(e => e.CreditPaise);

    public bool IsZero => Entries.All(e => e.DebitPaise == 0 && e.CreditPaise == 0);
}

/// <summary>Source kinds (prototype <c>ACCSourceKind</c> + payroll).</summary>
public static class SourceKinds
{
    public const string Line = "line";
    public const string Receipt = "receipt";
    public const string Pos = "pos";
    public const string Expense = "expense";
    public const string Closing = "closing";
    public const string Payroll = "payroll";

    public static readonly IReadOnlyList<string> All = [Line, Receipt, Pos, Expense, Closing, Payroll];
}

/// <summary>Intra-state GST split (prototype <c>gstSplit</c>): SGST = half rounded toward zero, CGST = the rest (takes the odd paisa).</summary>
public static class GstSplit
{
    public static (long Cgst, long Sgst) Of(long taxPaise)
    {
        var sgst = taxPaise / 2; // C# integer division truncates toward zero, like Math.trunc
        return (taxPaise - sgst, sgst);
    }
}

/// <summary>
/// Voucher rules per source (module spec 20 §5, prototype <c>accLiveVouchers</c>). Signed amounts: a negative amount swaps the
/// side (discounts, reversals, credit notes, voids). The factories only build keyed entries; resolution to ledgers is
/// <see cref="VoucherBuilder"/>.
/// </summary>
public static class SourceRules
{
    private static SourceEntry Cr(string key, long amount) => amount >= 0 ? new(key, 0, amount) : new(key, -amount, 0);

    private static SourceEntry Dr(string key, long amount) => Cr(key, -amount);

    /// <summary>Folio line kind → mapping key (production "correction" = prototype "adjustment": reversals and credit notes).</summary>
    public static string LineKey(string kind) => "line:" + (kind == "correction" ? "adjustment" : kind);

    /// <summary>
    /// Folio line: Dr Guest ledger a+t · Cr line ledger a · Cr CGST · Cr SGST (Sales). CGST/SGST are the amounts STORED with the
    /// charge (agent C <c>tax.charge_gst</c>, or the stored tax split by <see cref="GstSplit"/>); tax is never recomputed here.
    /// </summary>
    public static AccountingSource FolioLine(string sourceKey, string kind, DateOnly date, long amountPaise, long cgstPaise, long sgstPaise, string narration)
    {
        var (cgst, sgst, taxPaise) = (cgstPaise, sgstPaise, checked(cgstPaise + sgstPaise));
        return new(sourceKey, SourceKinds.Line, kind, date, VoucherType.Sales, narration,
            [Dr("folio:guest", checked(amountPaise + taxPaise)), Cr(LineKey(kind), amountPaise), Cr("folio:cgst", cgst), Cr("folio:sgst", sgst)]);
    }

    /// <summary>
    /// Folio receipt: payment/deposit = Receipt (Dr tender · Cr receipt kind); refund/deposit-return = Payment (Dr receipt kind ·
    /// Cr tender); company-credit = Journal (Dr company receivables · Cr guest ledger).
    /// </summary>
    public static AccountingSource FolioReceipt(string sourceKey, string kind, string tender, DateOnly date, long amountPaise, string narration)
    {
        var counter = "receipt:" + kind;
        if (kind == "company-credit")
            return new(sourceKey, SourceKinds.Receipt, kind, date, VoucherType.Journal, narration, [Dr(counter, amountPaise), Cr("folio:guest", amountPaise)]);
        var t = "method:" + tender;
        var inflow = kind is "payment" or "deposit";
        return new(sourceKey, SourceKinds.Receipt, kind, date, inflow ? VoucherType.Receipt : VoucherType.Payment, narration,
            inflow ? [Dr(t, amountPaise), Cr(counter, amountPaise)] : [Dr(counter, amountPaise), Cr(t, amountPaise)]);
    }

    /// <summary>Outlet bill settled at the outlet (DEC-052): Sales, Dr tender total · Cr outlet revenue net · Cr CGST · Cr SGST. A void is negative.</summary>
    public static AccountingSource PosBill(string sourceKey, string kind, string tender, DateOnly date, long totalPaise, long netPaise, long cgst, long sgst, string narration)
    {
        return new(sourceKey, SourceKinds.Pos, kind, date, VoucherType.Sales, narration,
            [Dr("method:" + tender, totalPaise), Cr("pos:sales", netPaise), Cr("pos:cgst", cgst), Cr("pos:sgst", sgst)]);
    }

    /// <summary>Posted expense (Payment; Journal for vendor credit): Dr category · Cr funding. A reversal record is negative and swaps sides.</summary>
    public static AccountingSource Expense(string sourceKey, bool reversal, string category, string funding, DateOnly date, long amountPaise, string narration)
        => new(sourceKey, SourceKinds.Expense, reversal ? "reversal" : "expense", date, funding == "Vendor credit" ? VoucherType.Journal : VoucherType.Payment, narration,
            [Dr("expense:" + category, amountPaise), Cr("funding:" + funding, amountPaise)]);

    /// <summary>Accepted cash difference (counted − expected): short = Dr over/short · Cr cash; over = Dr cash · Cr over/short (Journal).</summary>
    public static AccountingSource CashDifference(string sourceKey, DateOnly date, long differencePaise, string narration)
        => new(sourceKey, SourceKinds.Closing, "cash-difference", date, VoucherType.Journal, narration,
            differencePaise > 0
                ? [Dr("method:Cash", differencePaise), Cr("closing:cash-difference", differencePaise)]
                : [Dr("closing:cash-difference", -differencePaise), Cr("method:Cash", -differencePaise)]);

    /// <summary>
    /// Pay run posted (W2-2 contract): Journal. Dr salaries (gross) · Dr employer PF · Dr employer ESIC ·
    /// Cr salaries payable (net) · Cr PF payable (employee + employer) · Cr ESIC payable (employee + employer) · Cr PT payable ·
    /// Cr staff advances (recovered) · Cr other deductions. Balanced when gross = net + employee deductions.
    /// </summary>
    public static AccountingSource PayRun(string sourceKey, DateOnly date, PayRunFigures f, string narration)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new(sourceKey, SourceKinds.Payroll, "payrun", date, VoucherType.Journal, narration,
        [
            Dr("payroll:salaries", f.GrossPaise), Dr("payroll:employer-pf", f.EmployerPfPaise), Dr("payroll:employer-esic", f.EmployerEsicPaise),
            Cr("payroll:net-payable", f.NetPayablePaise), Cr("payroll:pf-payable", checked(f.EmployeePfPaise + f.EmployerPfPaise)),
            Cr("payroll:esic-payable", checked(f.EmployeeEsicPaise + f.EmployerEsicPaise)), Cr("payroll:pt-payable", f.ProfessionalTaxPaise),
            Cr("payroll:advances", f.AdvanceRecoveryPaise), Cr("payroll:other-deductions", f.OtherDeductionsPaise),
        ]);
    }

    /// <summary>Net pay paid out (W2-2 contract): Payment, Dr salaries payable · Cr funding.</summary>
    public static AccountingSource PayRunPaid(string sourceKey, string funding, DateOnly date, long amountPaise, string narration)
        => new(sourceKey, SourceKinds.Payroll, "payment", date, VoucherType.Payment, narration,
            [Dr("payroll:net-payable", amountPaise), Cr("funding:" + funding, amountPaise)]);

    /// <summary>
    /// Pay-run unlock (W3-b): Journal, the exact negative of the original pay-run voucher's entries (each debit becomes the same credit
    /// and vice versa, same keys, same order), so the two net to zero in every ledger.
    /// </summary>
    public static AccountingSource PayRunReversal(string sourceKey, DateOnly date, IReadOnlyList<SourceEntry> original, string narration)
    {
        ArgumentNullException.ThrowIfNull(original);
        return new(sourceKey, SourceKinds.Payroll, "payrun-reversal", date, VoucherType.Journal, narration,
            original.Select(e => new SourceEntry(e.MapKey, e.CreditPaise, e.DebitPaise)).ToList());
    }

    public static string Rupees(long paise)
    {
        var sign = paise < 0 ? "-" : "";
        var a = Math.Abs(paise);
        return sign + (a / 100).ToString(CultureInfo.InvariantCulture) + "." + (a % 100).ToString("00", CultureInfo.InvariantCulture);
    }
}

/// <summary>Pay-run figures as published by payroll (all paise, non-negative).</summary>
public sealed record PayRunFigures(long GrossPaise, long EmployerPfPaise, long EmployerEsicPaise, long EmployeePfPaise, long EmployeeEsicPaise,
    long ProfessionalTaxPaise, long AdvanceRecoveryPaise, long OtherDeductionsPaise, long NetPayablePaise);
