namespace Hcc.Domain.Accounting;

/// <summary>Figures for one date from the operational (billing / payments) records, independent of the outbox.</summary>
public sealed record OperationalFigures(long RoomPaise, long ServicePaise, long DiscountPaise, long AdjustmentPaise, long TaxPaise,
    long NetReceiptsPaise, long DepositsNetPaise, long CompanyCreditPaise,
    long GroupAttritionPaise = 0, long GroupAttritionTaxPaise = 0, long GroupReceiptsNetPaise = 0);   // DEC-074 (wave B3): group master folios (grp.master_entry)

public sealed record ReconciliationCheck(string Id, string Label, long ReportPaise, long LedgerPaise, bool Ok, string Note);

/// <summary>
/// Ledger movement vs the operational figure for the same date (spec §7, prototype <c>accReconcile</c>): folio revenue by kind,
/// GST, net receipts (payments − refunds), deposits net and company credit; group master-folio attrition and receipts (DEC-074).
/// Expenses, outlet and cash checks are added when those modules publish (W2-4, W2-6, P6).
/// </summary>
public static class Reconciliation
{
    public static IReadOnlyList<ReconciliationCheck> Check(IReadOnlyList<Voucher> vouchers, IReadOnlyDictionary<string, string> mapping, OperationalFigures report)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(report);
        var tenders = mapping.Where(kv => kv.Key.StartsWith("method:", StringComparison.Ordinal)).Select(kv => kv.Value).ToHashSet(StringComparer.Ordinal);
        long Net(string? code, Func<Voucher, bool> f) => code is null ? 0 : vouchers.Where(f).Sum(v => v.Entries.Where(e => e.Account == code).Sum(e => e.CreditPaise - e.DebitPaise));
        Func<Voucher, bool> Line(string sub) => v => v.SourceKind == SourceKinds.Line && v.Sub == sub;
        long TenderMove(params string[] kinds) => vouchers.Where(v => v.SourceKind == SourceKinds.Receipt && kinds.Contains(v.Sub))
            .Sum(v => v.Entries.Where(e => tenders.Contains(e.Account)).Sum(e => e.DebitPaise - e.CreditPaise));
        string? M(string key) => mapping.GetValueOrDefault(key);
        var taxCodes = new[] { M("folio:cgst"), M("folio:sgst") }.Where(x => x is not null).Distinct(StringComparer.Ordinal);
        var credit = vouchers.Where(v => v.SourceKind == SourceKinds.Receipt && v.Sub == "company-credit")
            .Sum(v => v.Entries.Where(e => e.Account == M("receipt:company-credit")).Sum(e => e.DebitPaise - e.CreditPaise));
        static ReconciliationCheck C(string id, string label, long report, long ledger, string note) => new(id, label, report, ledger, report == ledger, note);
        return
        [
            C("room", "Room revenue", report.RoomPaise, Net(M("line:room"), Line("room")), "Folio room lines"),
            C("service", "Other revenue", report.ServicePaise, Net(M("line:service"), Line("service")), "Folio service lines"),
            C("discount", "Discounts (negative)", report.DiscountPaise, Net(M("line:discount"), Line("discount")), "Folio discount lines"),
            C("adjustment", "Adjustments & credit notes", report.AdjustmentPaise, Net(M("line:adjustment"), Line("correction")), "Folio correction / reversal lines"),
            // DEC-074 (wave B3): the GST ledgers also carry group attrition (a Sales line voucher), so the report side adds it.
            C("tax", "GST (CGST + SGST)", checked(report.TaxPaise + report.GroupAttritionTaxPaise), taxCodes.Sum(code => Net(code, v => v.SourceKind == SourceKinds.Line)),
                "Folio line tax + group attrition"),
            C("collections", "Net receipts (payments − refunds)", report.NetReceiptsPaise, TenderMove("payment", "refund"), "Cashier › collections"),
            C("deposits", "Deposits net (in − out)", report.DepositsNetPaise, TenderMove("deposit", "deposit-return"), "Receipts · deposits"),
            C("company-credit", "Company credit", report.CompanyCreditPaise, credit, "Receipts · company credit"),

            // ── DEC-074 (wave B3): group master folios ──
            C("group-attrition", "Group attrition (net)", report.GroupAttritionPaise, Net(M(SourceRules.AttritionKey), Line("group-attrition")), "Groups · master folio attrition"),
            C("group-receipts", "Group master receipts (in − out)", report.GroupReceiptsNetPaise, TenderMove("group-payment", "group-deposit", "group-refund"),
                "Groups · master folio payments, deposits, refunds"),
        ];
    }
}
