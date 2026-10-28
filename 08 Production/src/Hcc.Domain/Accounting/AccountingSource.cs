using System.Globalization;

namespace Hcc.Domain.Accounting;

/// <summary>Voucher type (prototype <c>ACCVoucherType</c>); numbering prefix SAL/RCT/PMT/JRN.</summary>
public enum VoucherType
{
    Sales,
    Receipt,
    Payment,
    Journal,
    Purchase, // ACC-2 (Tally Purchase)
    Contra,   // ACC-2 (Tally Contra)
}

/// <summary>
/// One side of a source record, still keyed by classification (resolved to a ledger through the mapping at read time). ACC-1 (contract §3):
/// <paramref name="Party"/> = the party the source names for this side (null = none: a control ledger then takes it on the control itself);
/// appended to the resolved code only when that code is a control ledger (<see cref="PartyLedgers.Compose"/>).
/// </summary>
public sealed record SourceEntry(string MapKey, long DebitPaise, long CreditPaise, string? Party = null);

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
    // ACC-2 (contract §1): manual vouchers, purchases / vendor payments, tax entries (RCM, GST set-off, TDS), OTA payouts.
    public const string Manual = "manual";
    public const string Purchase = "purchase";
    public const string Vendor = "vendor";
    public const string Tax = "tax";
    public const string Ota = "ota";

    public static readonly IReadOnlyList<string> All = [Line, Receipt, Pos, Expense, Closing, Payroll, Manual, Purchase, Vendor, Tax, Ota];
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

    /// <summary>
    /// DEC-076: money received from a company against its company-credit receivable = Receipt (Dr tender · Cr company receivables, the
    /// <c>receipt:company-credit</c> ledger the checkout's company credit debited). Sub <c>company-payment</c>, so the daily company-credit
    /// reconciliation (Sub <c>company-credit</c>) is unchanged.
    /// </summary>
    public static AccountingSource CompanyPayment(string sourceKey, string tender, DateOnly date, long amountPaise, string narration)
        => new(sourceKey, SourceKinds.Receipt, "company-payment", date, VoucherType.Receipt, narration,
            [Dr("method:" + tender, amountPaise), Cr("receipt:company-credit", amountPaise)]);

    /// <summary>Outlet bill settled at the outlet (DEC-052): Sales, Dr tender total · Cr outlet revenue net · Cr CGST · Cr SGST. A void is negative.</summary>
    public static AccountingSource PosBill(string sourceKey, string kind, string tender, DateOnly date, long totalPaise, long netPaise, long cgst, long sgst, string narration)
    {
        return new(sourceKey, SourceKinds.Pos, kind, date, VoucherType.Sales, narration,
            [Dr("method:" + tender, totalPaise), Cr("pos:sales", netPaise), Cr("pos:cgst", cgst), Cr("pos:sgst", sgst)]);
    }

    /// <summary>DEC-135: the GST input-credit ledger key (GST on expense bills marked ITC allowed; one ledger — a CGST / SGST / IGST split on CA request).</summary>
    public const string GstInputKey = "tax:gst-input";

    /// <summary>
    /// Posted expense (Payment; Journal for vendor credit): Dr category · Cr funding. A reversal record is negative and swaps sides. DEC-135: with
    /// <paramref name="itcPaise"/> (the bill's GST marked ITC allowed; negative on a reversal) Dr category (amount − GST) · Dr GST input credit ·
    /// Cr funding (amount); the ITC line exists only when it is not zero (prototype <c>accLiveVouchers</c>).
    /// </summary>
    public static AccountingSource Expense(string sourceKey, bool reversal, string category, string funding, DateOnly date, long amountPaise, string narration, long itcPaise = 0)
        => new(sourceKey, SourceKinds.Expense, reversal ? "reversal" : "expense", date, funding == "Vendor credit" ? VoucherType.Journal : VoucherType.Payment, narration,
            itcPaise == 0
                ? [Dr("expense:" + category, amountPaise), Cr("funding:" + funding, amountPaise)]
                : [Dr("expense:" + category, checked(amountPaise - itcPaise)), Dr(GstInputKey, ChartOfAccounts.ItcSplit(itcPaise).Cgst),
                    Dr(ChartOfAccounts.GstInputSgstKey, ChartOfAccounts.ItcSplit(itcPaise).Sgst), Cr("funding:" + funding, amountPaise)]); // ACC-1: ITC CGST half 1310 · SGST half 1320

    /// <summary>DEC-130: the Udhaar receivables ledger key (customer credit settled by a desk receipt).</summary>
    public const string UdhaarKey = "receipt:udhaar";

    /// <summary>
    /// DEC-130: an Udhaar payment taken as a desk receipt = Receipt (Dr tender · Cr Udhaar receivables). Sub <c>udhaar-payment</c>, so the daily
    /// collections reconciliation (folio receipts, Sub payment / refund) is unchanged; the cash is in the drawer movements and the cash ledger.
    /// </summary>
    public static AccountingSource UdhaarPayment(string sourceKey, string tender, DateOnly date, long amountPaise, string narration)
        => new(sourceKey, SourceKinds.Receipt, "udhaar-payment", date, VoucherType.Receipt, narration, [Dr("method:" + tender, amountPaise), Cr(UdhaarKey, amountPaise)]);

    // ── ACC-1 (acc1-parties) · party sub-ledgers and the Udhaar credit raised ──

    /// <summary>
    /// The source with each entry's party set from <paramref name="parties"/> by map key (<see cref="PartyContext.For"/>); an entry that
    /// already names a party keeps it. Null / <see cref="PartyContext.None"/> = unchanged.
    /// </summary>
    public static AccountingSource WithParties(AccountingSource source, PartyContext? parties)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (parties is null || parties == PartyContext.None) return source;
        return source with { Entries = source.Entries.Select(e => e.Party is null ? e with { Party = PartyLedgers.Clip(parties.For(e.MapKey)) } : e).ToList() };
    }

    /// <summary>Revenue key of an Udhaar credit's kind (prototype <c>expUdRevenueKey</c>): restaurant → F&amp;B (pos:sales), room → line:room, other → line:service.</summary>
    public static string UdhaarRevenueKey(string? kind) => kind switch
    {
        "restaurant" => "pos:sales",
        "room" => "line:room",
        _ => "line:service",
    };

    /// <summary>
    /// The GST inside an Udhaar credit's gross amount (prototype <c>expUdGst</c>): taxable = gross × 10000 ÷ (10000 + bps) rounded half away
    /// from zero, tax = gross − taxable, split by <see cref="GstSplit"/>. bps 0 = no GST.
    /// </summary>
    public static (long Taxable, long Cgst, long Sgst) UdhaarGst(long grossPaise, int gstBps)
    {
        if (gstBps <= 0) return (grossPaise, 0, 0);
        var n = checked(Math.Abs(grossPaise) * 10000);
        var d = 10000L + gstBps;
        var q = n / d;
        var t = q + ((n - q * d) * 2 >= d ? 1 : 0);
        var taxable = grossPaise < 0 ? -t : t;
        var (cgst, sgst) = GstSplit.Of(grossPaise - taxable);
        return (taxable, cgst, sgst);
    }

    /// <summary>
    /// ACC-1 (contract §4, rulebook A17, DEC-144): an Udhaar credit raised = Sales the same day — Dr Udhaar receivables (party = the customer,
    /// gross) · Cr the kind's revenue (taxable) · Cr CGST · Cr SGST. Kind <c>receipt</c> (like the Udhaar payment, so the folio-line
    /// reconciliation is unchanged), sub <c>udhaar-credit</c>.
    /// </summary>
    public static AccountingSource UdhaarCredit(string sourceKey, DateOnly date, string? kind, long grossPaise, int gstBps, string customerParty, string narration)
    {
        var (taxable, cgst, sgst) = UdhaarGst(grossPaise, gstBps);
        return new(sourceKey, SourceKinds.Receipt, "udhaar-credit", date, VoucherType.Sales, narration,
            [Dr(UdhaarKey, grossPaise) with { Party = PartyLedgers.Clip(customerParty) }, Cr(UdhaarRevenueKey(kind), taxable), Cr("folio:cgst", cgst), Cr("folio:sgst", sgst)]);
    }

    // ── end ACC-1 ──

    /// <summary>Accepted cash difference (counted − expected): short = Dr over/short · Cr cash; over = Dr cash · Cr over/short (Journal).</summary>
    public static AccountingSource CashDifference(string sourceKey, DateOnly date, long differencePaise, string narration)
        => new(sourceKey, SourceKinds.Closing, "cash-difference", date, VoucherType.Journal, narration,
            differencePaise > 0
                ? [Dr("method:Cash", differencePaise), Cr(ChartOfAccounts.CashDiffKey(differencePaise), differencePaise)] // ACC-1: an over → closing:cash-over (4950)
                : [Dr("closing:cash-difference", -differencePaise), Cr("method:Cash", -differencePaise)]);

    // ── Wave B1 (DEC-073, A03b-01): cash leaving the drawer ──

    /// <summary>Drawer cash banked: Journal (a contra in Tally), Dr bank deposit (1030) · Cr cash. A rejected deposit is the same with a negative amount.</summary>
    public static AccountingSource CashBanking(string sourceKey, bool rejected, DateOnly date, long amountPaise, string narration)
        => new(sourceKey, SourceKinds.Closing, rejected ? "bank-deposit-rejected" : "bank-deposit", date, VoucherType.Journal, narration,
            rejected ? [Dr("method:Cash", amountPaise), Cr("closing:bank-deposit", amountPaise)] : [Dr("closing:bank-deposit", amountPaise), Cr("method:Cash", amountPaise)]);

    /// <summary>Owner drawing: Payment, Dr Owner's drawings (3000) · Cr cash.</summary>
    public static AccountingSource OwnerDrawing(string sourceKey, DateOnly date, long amountPaise, string narration)
        => new(sourceKey, SourceKinds.Closing, "owner-drawing", date, VoucherType.Payment, narration, [Dr("closing:owner-drawing", amountPaise), Cr("method:Cash", amountPaise)]);

    // ── end Wave B1 ──

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

    // ── DEC-074 (wave B3) · group master folio, staff advances, full and final ──────────────────────────────────────────────────────

    /// <summary>
    /// A group's master-folio receivable: the company-credit key the member transfers already debit (1110 Company receivables), so the
    /// routed shares, attrition, receipts and settlement of one group net to zero in ONE ledger under any mapping.
    /// </summary>
    public const string GroupReceivableKey = "receipt:company-credit";

    /// <summary>Deposits a company paid on a group's master folio, held until settlement (default 2000 Advance deposits, as folio deposits).</summary>
    public const string GroupDepositKey = "receipt:group-deposit";

    /// <summary>Attrition / retention charges on a group master folio (default 4000 Room revenue — CA G7 to confirm the ledger).</summary>
    public const string AttritionKey = "line:attrition";

    /// <summary>
    /// Group attrition (Sales): Dr master receivable net + CGST + SGST · Cr attrition revenue net · Cr CGST · Cr SGST. CGST / SGST are the
    /// split STORED per attrition line (summed by Groups), never recomputed here.
    /// </summary>
    public static AccountingSource GroupAttrition(string sourceKey, DateOnly date, long netPaise, long cgstPaise, long sgstPaise, string narration)
        => new(sourceKey, SourceKinds.Line, "group-attrition", date, VoucherType.Sales, narration,
            [Dr(GroupReceivableKey, checked(netPaise + cgstPaise + sgstPaise)), Cr(AttritionKey, netPaise), Cr("folio:cgst", cgstPaise), Cr("folio:sgst", sgstPaise)]);

    /// <summary>
    /// Group master-folio receipt: payment = Receipt (Dr tender · Cr master receivable); deposit = Receipt (Dr tender · Cr group deposits);
    /// refund = Payment (Dr the ledger the refunded receipt credited — deposits for a deposit, else the receivable · Cr tender).
    /// </summary>
    public static AccountingSource GroupReceipt(string sourceKey, string kind, string? refundOfKind, string tender, DateOnly date, long amountPaise, string narration)
    {
        var t = "method:" + tender;
        var counter = (kind == "refund" ? refundOfKind : kind) == "deposit" ? GroupDepositKey : GroupReceivableKey;
        return kind == "refund"
            ? new(sourceKey, SourceKinds.Receipt, "group-refund", date, VoucherType.Payment, narration, [Dr(counter, amountPaise), Cr(t, amountPaise)])
            : new(sourceKey, SourceKinds.Receipt, "group-" + kind, date, VoucherType.Receipt, narration, [Dr(t, amountPaise), Cr(counter, amountPaise)]);
    }

    /// <summary>
    /// Group settled (Journal): the deposits still held (deposits − refunds of deposits) are applied to the master receivable — Dr group
    /// deposits · Cr master receivable. With the receipts and the transfers this leaves both ledgers at zero for the group.
    /// </summary>
    public static AccountingSource GroupSettlement(string sourceKey, DateOnly date, long depositsHeldPaise, string narration)
        => new(sourceKey, SourceKinds.Receipt, "group-settlement", date, VoucherType.Journal, narration,
            [Dr(GroupDepositKey, depositsHeldPaise), Cr(GroupReceivableKey, depositsHeldPaise)]);

    /// <summary>Salary advance given (Payment): Dr staff advances (1200, the ledger pay-run recoveries credit) · Cr funding.</summary>
    public static AccountingSource AdvanceGiven(string sourceKey, string funding, DateOnly date, long amountPaise, string narration)
        => new(sourceKey, SourceKinds.Payroll, "advance", date, VoucherType.Payment, narration,
            [Dr("payroll:advances", amountPaise), Cr("funding:" + funding, amountPaise)]);

    /// <summary>
    /// Full and final settled (Journal, mirrors <see cref="PayRun"/>): Dr salaries (exit-month gross) · Dr F&amp;F additions (e.g. leave
    /// encashment) · Dr employer PF · Dr employer ESIC · Cr PF payable (employee + employer) · Cr ESIC payable (employee + employer) · Cr PT
    /// payable · Cr staff advances (only the part recovered) · Cr F&amp;F deductions · Cr salaries payable (net) — or, when the net is
    /// negative, Dr recoverable from the leaver. Balanced when net = gross + additions − employee PF − ESIC − PT − advance − deductions.
    /// </summary>
    public static AccountingSource Fnf(string sourceKey, DateOnly date, FnfBookFigures f, string narration)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new(sourceKey, SourceKinds.Payroll, "fnf", date, VoucherType.Journal, narration,
        [
            Dr("payroll:salaries", f.GrossPaise), Dr("payroll:fnf-additions", f.AdditionsPaise), Dr("payroll:employer-pf", f.EmployerPfPaise),
            Dr("payroll:employer-esic", f.EmployerEsicPaise),
            Cr("payroll:pf-payable", checked(f.EmployeePfPaise + f.EmployerPfPaise)), Cr("payroll:esic-payable", checked(f.EmployeeEsicPaise + f.EmployerEsicPaise)),
            Cr("payroll:pt-payable", f.ProfessionalTaxPaise), Cr("payroll:advances", f.AdvanceRecoveredPaise), Cr("payroll:fnf-deductions", f.DeductionsPaise),
            f.NetPaise >= 0 ? Cr("payroll:net-payable", f.NetPaise) : Dr("payroll:fnf-recoverable", -f.NetPaise),
        ]);
    }

    /// <summary>Full-and-final net pay paid out (Payment, mirrors <see cref="PayRunPaid"/>): Dr salaries payable · Cr funding.</summary>
    public static AccountingSource FnfPaid(string sourceKey, string funding, DateOnly date, long amountPaise, string narration)
        => new(sourceKey, SourceKinds.Payroll, "fnf-payment", date, VoucherType.Payment, narration,
            [Dr("payroll:net-payable", amountPaise), Cr("funding:" + funding, amountPaise)]);

    // ── end DEC-074 (wave B3) ──

    // ── ACC-2 (acc2-manual, contract §2) · manual vouchers ──

    /// <summary>Map-key prefix of a manual voucher line: the ledger is named directly (<c>ledger:5080</c>), never through the mapping
    /// (<see cref="ChartOfAccounts.Resolve"/>); the party of a party ledger id travels in <see cref="SourceEntry.Party"/>.</summary>
    public const string LedgerKeyPrefix = "ledger:";

    /// <summary>The source key of a manual voucher (prototype <c>accManualSource</c> id).</summary>
    public static string ManualKey(long voucherId) => "manual:" + voucherId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// An approved manual voucher (or the reversing voucher of a void on a locked day) = one source of kind <c>manual</c>, sub = the template
    /// (<c>journal</c> when none), dated the voucher date, its lines as entered: <c>ledger:&lt;code&gt;</c> + the party of a party ledger id.
    /// </summary>
    public static AccountingSource Manual(string sourceKey, DateOnly date, VoucherType type, string? template, string narration,
        IEnumerable<(string Account, long DebitPaise, long CreditPaise)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new(sourceKey, SourceKinds.Manual, string.IsNullOrEmpty(template) ? "journal" : template, date, type, narration,
            lines.Select(l => new SourceEntry(LedgerKeyPrefix + PartyLedgers.ControlOf(l.Account), l.DebitPaise, l.CreditPaise, PartyLedgers.PartyOf(l.Account))).ToList());
    }

    // ── end ACC-2 manual ──

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

/// <summary>DEC-074 (wave B3): full-and-final figures as published by payroll (paise; all non-negative except <see cref="NetPaise"/>).</summary>
public sealed record FnfBookFigures(long GrossPaise, long AdditionsPaise, long EmployerPfPaise, long EmployerEsicPaise, long EmployeePfPaise, long EmployeeEsicPaise,
    long ProfessionalTaxPaise, long AdvanceRecoveredPaise, long DeductionsPaise, long NetPaise);
