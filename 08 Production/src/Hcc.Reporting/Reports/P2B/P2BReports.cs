// F8-RPT-P2-B · batch B reports (server): cash count sheet, counter bills, refunds, discount log, bank book, company credit and statement, licence expiry.
// Same ids, column keys, parameters and figures as the prototype (03 Prototype/src/domain/reports/p2-b/*.ts). Pure builders take facts so the unit tests need no database.
// Not built: rpt.money.payment-corrections (no audit trail of a receipt's payment type exists in code; see F8-RPT-P2-B-DONE.md).
using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Compliance;
using Hcc.Application.Groups;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Groups;
using Hcc.Domain.Compliance;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.Acc;
using Hcc.Reporting.Reports.Money;
using Hcc.Reporting.Sql;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.P2B;

// ── rpt.money.cash-count-sheet ──────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class CashCountSheetReport(IMoneyReportSource money, IP2BReportSource source, IAccessPolicy policy) : IReportSelector
{
    public const string Id = "rpt.money.cash-count-sheet";

    public static readonly IReadOnlyList<ReportColumn> Cols =
        [T("denomination", "Denomination"), I("count", "Notes or coins"), M("value", "Value"), M("expected", "Expected"), M("difference", "Difference")];

    public ReportDefinition Definition { get; } = new(Id, "Cash count sheet", "money", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Day,
        [new("from", "Shift or date", "date", ReportDateClass.Day)], Cols,
        "One business date (the drawer shift of the day). Notes and coins from the count grid where the shift kept one; a count without a breakdown leaves its note rows blank for hand entry. Expected = opening + receipts - payments; difference = counted - expected.",
        [ReportFormat.Pdf, ReportFormat.Screen], ReportPhone.Yes, OwnShift: true);

    private static ReportRow R(string label, long? count, long? value, long? expected, long? difference, ReportRowKind kind = ReportRowKind.Row, string? group = null) =>
        Row(kind, group, ("denomination", label), ("count", count), ("value", value), ("expected", expected), ("difference", difference));

    /// <summary>The sheet of one shift: a row per note denomination, coins, the total (counted / expected / difference) and two signature lines.</summary>
    public static IEnumerable<ReportRow> SheetRows(ShiftLine shift, CountGridFact? grid, string? group)
    {
        var hasGrid = grid?.Notes is not null;
        foreach (var n in new[] { 500, 200, 100, 50, 20, 10 })
        {
            long? k = hasGrid ? grid!.Notes!.GetValueOrDefault(n) : null;
            yield return R($"₹{n} notes", k, k is null ? null : k * n * 100, null, null, group: group);
        }

        yield return R("Coins (rupee total)", null, hasGrid ? grid!.CoinsPaise ?? 0 : null, null, null, group: group);
        yield return R(hasGrid ? "Total counted" : "Total counted (grid not kept)", null, shift.Counted, shift.Expected, shift.Counted is { } c ? c - shift.Expected : null, ReportRowKind.Total, group);
        yield return R("Counted by (signature): ____________________", null, null, null, null, group: group);
        yield return R("Checked by (signature): ____________________", null, null, null, null, group: group);
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var date = parameters.From!.Value;
        var all = await SeesAllAsync(policy, caller, ct).ConfigureAwait(false);
        var names = await money.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var facts = await money.ShiftsAsync(caller.PropertyId, date, date, ct).ConfigureAwait(false);
        var grids = (await source.CountGridsAsync(caller.PropertyId, date, ct).ConfigureAwait(false)).ToDictionary(g => g.ShiftId);
        var rows = new List<ReportRow>();
        foreach (var f in facts)
        {
            var line = ShiftReport.Lines([f], names)[0];
            if (!all && line.CashierId != caller.PrincipalId) continue;
            rows.AddRange(SheetRows(line, grids.GetValueOrDefault(f.ShiftId), facts.Count > 1 ? f.Till : null));
        }

        return new ReportResult(Id, Definition.Title, parameters, Cols, rows, About(Definition, parameters));
    }
}

// ── rpt.money.counter-bills ─────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class CounterBillsReport(IP2BReportSource source) : IReportSelector
{
    public const string Id = "rpt.money.counter-bills";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        Dt("date", "Date"), T("billNo", "Bill no"), T("customer", "Customer", ReportMask.GuestName), T("item", "Item"), I("qty", "Qty"), M("rate", "Rate"), M("amount", "Amount"),
        M("tax", "Tax"), T("tender", "Tender"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Counter bills", "money", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [Range(ReportDateClass.Business), RangeTo(ReportDateClass.Business)], Cols,
        "Walk-in (counter) bills by bill date. Amount = net of the line (a bill discount is its own row); Tax and Tender are on the bill subtotal. A credited bill stays listed with its credit note. A counter bill is the old software's direct bill, not company credit.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No, LegacyIds: ["A39", "C2.DirectBillSummary", "C4.DirectBillDoc"]);

    public static IReadOnlyList<ReportRow> Rows(IEnumerable<CounterBillFact> bills)
    {
        var list = bills.OrderBy(b => b.Date).ThenBy(b => b.No, StringComparer.Ordinal).ToList();
        var rows = new List<ReportRow>();
        foreach (var b in list)
        {
            foreach (var l in b.Lines)
                rows.Add(Row(ReportRowKind.Row, null, ("date", b.Date), ("billNo", b.No), ("customer", b.Customer), ("item", l.Label), ("qty", (long)l.Qty), ("rate", l.UnitPaise), ("amount", l.AmountPaise), ("tax", null), ("tender", null)));
            if (b.DiscountPaise > 0)
                rows.Add(Row(ReportRowKind.Row, null, ("date", b.Date), ("billNo", b.No), ("customer", b.Customer), ("item", "Discount · " + b.DiscountReason), ("qty", null), ("rate", null), ("amount", -b.DiscountPaise), ("tax", null), ("tender", null)));
            rows.Add(Row(ReportRowKind.Subtotal, null, ("date", null), ("billNo", b.No), ("customer", null), ("item", "Bill total"), ("qty", null), ("rate", null), ("amount", b.NetPaise), ("tax", b.TaxPaise), ("tender", b.Tender)));
        }

        if (list.Count > 0)
            rows.Add(Row(ReportRowKind.Total, null, ("date", null), ("billNo", null), ("customer", null), ("item", "Grand total"), ("qty", null), ("rate", null), ("amount", list.Sum(b => b.NetPaise)), ("tax", list.Sum(b => b.TaxPaise)), ("tender", null)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var bills = await source.CounterBillsAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Cols, Rows(bills), About(Definition, parameters));
    }
}

// ── rpt.money.refunds ───────────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class RefundsReport(IMoneyReportSource money, IP2BReportSource source) : IReportSelector
{
    public const string Id = "rpt.money.refunds";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        Dt("date", "Date"), T("refundNo", "Refund no"), T("guest", "Guest", ReportMask.GuestName), T("billNo", "Bill no"), M("amount", "Amount"), T("tender", "Tender"), T("reason", "Reason"),
        T("approvedBy", "Approved by"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Refunds", "money", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [Range(ReportDateClass.Business), RangeTo(ReportDateClass.Business)], Cols,
        "Refund receipts of stays by receipt date (the refund of a payment, DEC-031). Reason and approver are those of the approval the refund executed; deposit returns are not refunds and are in the collection detail.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No, LegacyIds: ["A41"]);

    public static IReadOnlyList<ReportRow> Rows(IEnumerable<RefundFact> facts, IReadOnlyDictionary<int, string> names)
    {
        var list = facts.OrderBy(f => f.Date).ThenBy(f => f.RefundNo, StringComparer.Ordinal).ToList();
        var rows = list.Select(f => Row(ReportRowKind.Row, null, ("date", f.Date), ("refundNo", f.RefundNo), ("guest", f.Guest), ("billNo", f.BillNo), ("amount", f.AmountPaise), ("tender", f.Tender),
            ("reason", f.Reason), ("approvedBy", f.ApprovedBy == 0 ? string.Empty : NameOf(names, f.ApprovedBy)))).ToList();
        if (rows.Count > 0)
            rows.Add(Row(ReportRowKind.Total, null, ("date", null), ("refundNo", "Total refunded"), ("guest", null), ("billNo", null), ("amount", list.Sum(f => f.AmountPaise)), ("tender", null), ("reason", null), ("approvedBy", null)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var names = await money.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var facts = await source.RefundsAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Cols, Rows(facts, names), About(Definition, parameters));
    }
}

// ── rpt.money.discount-log ──────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class DiscountLogReport(IMoneyReportSource money, IP2BReportSource source) : IReportSelector
{
    public const string Id = "rpt.money.discount-log";

    public static readonly IReadOnlyList<ReportColumn> Cols =
        [Dt("date", "Date"), T("ref", "Booking or bill"), T("guest", "Guest", ReportMask.GuestName), T("reason", "Reason"), M("amount", "Amount"), T("approvedBy", "Approved by")];

    public ReportDefinition Definition { get; } = new(Id, "Discounts given", "money", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [Range(ReportDateClass.Business), RangeTo(ReportDateClass.Business), new("reason", "Reason", "text")], Cols,
        "Discount lines posted on stays and discounts on counter bills, by posting date; a discount that was reversed is left out. Amount is the discount given (positive). Sorted by reason, then date, with a subtotal per reason. The summary by reason is in the plans and extras report.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No, LegacyIds: ["C2.OtherDiscount"]);

    public static IReadOnlyList<ReportRow> Rows(IEnumerable<DiscountFact> facts, IReadOnlyDictionary<int, string> names, string reasonFilter)
    {
        var q = reasonFilter.Trim();
        var list = facts.Where(f => q.Length == 0 || f.Reason.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Reason, StringComparer.Ordinal).ThenBy(f => f.Date).ThenBy(f => f.Ref, StringComparer.Ordinal).ToList();
        var rows = new List<ReportRow>();
        foreach (var g in list.GroupBy(f => f.Reason))
        {
            rows.AddRange(g.Select(f => Row(ReportRowKind.Row, null, ("date", f.Date), ("ref", f.Ref), ("guest", f.Guest), ("reason", f.Reason), ("amount", f.AmountPaise),
                ("approvedBy", f.ApprovedBy == 0 ? string.Empty : NameOf(names, f.ApprovedBy)))));
            rows.Add(Row(ReportRowKind.Subtotal, null, ("date", null), ("ref", null), ("guest", null), ("reason", "Subtotal · " + g.Key), ("amount", g.Sum(f => f.AmountPaise)), ("approvedBy", null)));
        }

        if (list.Count > 0)
            rows.Add(Row(ReportRowKind.Total, null, ("date", null), ("ref", null), ("guest", null), ("reason", "Total discounts"), ("amount", list.Sum(f => f.AmountPaise)), ("approvedBy", null)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var names = await money.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var facts = await source.DiscountsAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Cols, Rows(facts, names, Param(parameters, "reason")), About(Definition, parameters));
    }
}

// ── rpt.money.bank-book ─────────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class BankBookReport(AccQueries q, Hcc.Application.Accounting.IAccountingStore store) : IReportSelector
{
    public const string Id = "rpt.money.bank-book";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [Dt("date", "Date"), T("voucher", "Voucher"), T("narration", "Narration"), T("counter", "Counter ledger"), M("debit", "Debit"),
         M("credit", "Credit"), M("balance", "Balance (Dr / Cr)")];

    public ReportDefinition Definition { get; } = new(Id, "Bank book", "money", "P2", "built", true, AccShared.View, [AccShared.View], [], "PERM-RPT-003", ReportDateClass.Financial,
        [new("account", "Bank ledger", "party", null, null, null, true), AccShared.Range()], Columns,
        "Vouchers of one bank ledger (Bank Accounts group) in the range, oldest first; opening = every voucher before From. Balance: Dr positive, Cr negative.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, LegacyIds: ["A17"]);

    private static string Side(long n) => n > 0 ? " — Dr" : n < 0 ? " — Cr" : "";

    public static List<ReportRow> Rows(StatementView st, IReadOnlyList<ReportRow> stamp)
    {
        var rows = new List<ReportRow>(stamp) { Row(ReportRowKind.Subtotal, null, ("narration", $"Opening balance · {st.Name}{Side(st.OpeningPaise)}"), ("balance", st.OpeningPaise)) };
        rows.AddRange(st.Rows.Select(r => Row(ReportRowKind.Row, null, ("date", r.Date), ("voucher", r.No), ("narration", r.Narration), ("counter", r.Counter), ("debit", r.DebitPaise),
            ("credit", r.CreditPaise), ("balance", r.RunningPaise))));
        rows.Add(Row(ReportRowKind.Total, null, ("narration", $"Closing balance{Side(st.ClosingPaise)}"), ("debit", st.DebitPaise), ("credit", st.CreditPaise), ("balance", st.ClosingPaise)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        var account = parameters.Values["account"];
        var chart = await store.ChartAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        if (chart.Find(account) is not { } l || !chart.IsUnder(l.GroupId, "bank")) throw HccException.Validation("Choose a ledger in the Bank Accounts group.");
        var st = await q.Reports().StatementAsync(caller, account, from, to, ct).ConfigureAwait(false);
        var stamp = await AccShared.StampAsync(q, caller, from, to, "voucher", ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(st, stamp), AccShared.About(Definition, parameters, stamp.Count > 0));
    }
}

// ── rpt.money.company-credit ────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class CompanyCreditReport(ICompanyStore companies, CompanyCredit credit, ICompanyCreditStore creditStore, IOpeningEntryStore? openings = null) : IReportSelector
{
    public const string Id = "rpt.money.company-credit";

    public static readonly IReadOnlyList<ReportColumn> Cols =
        [T("company", "Company"), T("contact", "Contact"), T("rates", "Negotiated rates"), M("limit", "Credit limit"), M("used", "Used"), M("remaining", "Remaining"), T("status", "Credit status")];

    public static readonly IReadOnlyList<ReportColumn> StatementCols =
        [Dt("date", "Date"), T("ref", "Bill or reference"), T("narration", "Narration"), M("credit", "Credit given"), M("payment", "Payment received"), M("balance", "Balance")];

    public ReportDefinition Definition { get; } = new(Id, "Company credit and statement", "money", "P2", "built", true, "PERM-GRP-004", ["PERM-GRP-001"], [], "PERM-RPT-003", ReportDateClass.Now,
        [new("company", "Company", "party")], Cols,
        "The state now (no history). One row per company: limit, used (receivable plus open stays billed to it, DEC-076) and remaining. With a company chosen, its statement: opening outstanding, company credit given, payments received (TDS included) and the running balance.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public static string RatesText(IReadOnlyDictionary<string, long> rates) =>
        string.Join("; ", rates.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => k + " " + (rates[k] / 100m).ToString("F2", CultureInfo.InvariantCulture)));

    public static string Level(long limit, long used) => limit <= 0 ? (used > 0 ? "Over limit" : "No credit") : used > limit ? "Over limit" : used * 10 >= limit * 8 ? "Near limit (≥ 80%)" : "Within limit";

    public static ReportRow CompanyRow(CompanyRow c, CompanyCreditView k) => Row(ReportRowKind.Row, null, ("company", c.Name), ("contact", string.Join(" · ", new[] { c.Contact, c.Phone }.Where(x => !string.IsNullOrEmpty(x)))),
        ("rates", RatesText(c.Rates)), ("limit", c.CreditLimitPaise), ("used", k.ExposurePaise), ("remaining", k.RemainingPaise), ("status", Level(c.CreditLimitPaise, k.ExposurePaise)));

    /// <param name="opening">CU-F8E-OPEN: the company's opening outstanding (prototype <c>openingOutstanding</c>), in paise: the approved opening entry's debit minus credit on its party ledger 1110/CMP-id, never below 0.</param>
    public static IReadOnlyList<ReportRow> Statement(CompanyCreditFacts f, long opening = 0)
    {
        var ev = f.Credits.Select(c => (c.BusinessDate, Ord: 0, Ref: c.InvoiceNo ?? c.PublicNo /* F8E-R8: the folio's own invoice (prototype twin, F8-INT L1) */, Narr: "Company credit · " + c.PublicNo, Credit: c.Amount.Value, Pay: 0L))
            .Concat(f.Payments.Select(p => (p.BusinessDate, Ord: 1, Ref: p.Reference.Length > 0 ? p.Reference : "GCP-" + p.PaymentId.ToString(CultureInfo.InvariantCulture),
                Narr: "Payment · " + p.Tender + (p.Tds.Value > 0 ? " (incl. TDS)" : string.Empty), Credit: 0L, Pay: p.Settled.Value)))
            .OrderBy(e => e.BusinessDate).ThenBy(e => e.Ord).ThenBy(e => e.Ref, StringComparer.Ordinal).ToList();
        long run = opening;
        var rows = new List<ReportRow> { Row(ReportRowKind.Subtotal, null, ("date", null), ("ref", null), ("narration", "Opening outstanding"), ("credit", null), ("payment", null), ("balance", run)) };
        foreach (var e in ev)
        {
            run += e.Credit - e.Pay;
            rows.Add(Row(ReportRowKind.Row, null, ("date", e.BusinessDate), ("ref", e.Ref), ("narration", e.Narr), ("credit", e.Credit == 0 ? null : e.Credit), ("payment", e.Pay == 0 ? null : e.Pay), ("balance", run)));
        }

        rows.Add(Row(ReportRowKind.Total, null, ("date", null), ("ref", null), ("narration", "Closing outstanding"), ("credit", ev.Sum(e => e.Credit)), ("payment", ev.Sum(e => e.Pay)), ("balance", run)));
        return rows;
    }

    /// <summary>Opening outstanding of one company: the APPROVED opening entry's net debit on its party ledger 1110/CMP-id (a draft never counts); 0 when none.</summary>
    public static long OpeningOf(OpeningEntry? entry, long companyId)
    {
        if (entry is not { Approved: true }) return 0;
        var ledger = "1110/" + Hcc.Domain.Accounting.PartyLedgers.Company(companyId);
        var net = entry.Lines.Where(l => string.Equals(l.Account, ledger, StringComparison.Ordinal)).Sum(l => l.Debit - l.Credit);
        return Math.Max(0, net);
    }

    private async Task<long> OpeningAsync(long companyId, int propertyId, CancellationToken ct)
        => openings is null ? 0 : OpeningOf(await openings.GetAsync(null, propertyId, ct).ConfigureAwait(false), companyId);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var q = Param(parameters, "company").Trim();
        var list = (await companies.ListAsync(caller.PropertyId, ct).ConfigureAwait(false)).Where(c => c.Active && (q.Length == 0 || c.CompanyId.ToString(CultureInfo.InvariantCulture) == q || c.Name == q))
            .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        var rows = new List<ReportRow>();
        foreach (var c in list)
        {
            var (view, _) = await credit.PictureAsync(caller.PropertyId, c, ct).ConfigureAwait(false);
            rows.Add(CompanyRow(c, view));
        }

        List<ReportTable>? sections = null;
        if (q.Length > 0 && list.Count == 1)
        {
            var facts = await creditStore.LoadAsync(null, caller.PropertyId, list[0].CompanyId, ct).ConfigureAwait(false);
            var opening = await OpeningAsync(list[0].CompanyId, caller.PropertyId, ct).ConfigureAwait(false);
            sections = [new ReportTable("companies", null, Cols, rows), new ReportTable("statement", "Statement · " + list[0].Name, StatementCols, Statement(facts, opening))];
        }

        return new ReportResult(Id, Definition.Title, parameters, Cols, rows, About(Definition, parameters), Sections: sections);
    }
}

// ── rpt.tax.licence-expiry ──────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class LicenceExpiryReport(IComplianceReadStore reads) : IReportSelector
{
    public const string Id = "rpt.tax.licence-expiry";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        T("licence", "Licence"), T("number", "Number"), Dt("issued", "Issued"), Dt("expires", "Expires"), I("daysLeft", "Days left"), new("card", "Renewal card", ReportColumnType.Bool, false),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Licence expiry", "tax", "P2", "built", false, null, ["PERM-CMP-001"], [], "PERM-RPT-003", ReportDateClass.Now,
        [new("within", "Within days", "text", null, null, "90")], Cols,
        "The state now, against the business date: licences and permits of the firm profile (FSSAI, lodging house) that expire within the chosen days, expired ones included (negative days). The profile keeps no issue date.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public static IReadOnlyList<ReportRow> Rows(CmpProfile? profile, DateOnly today, int within)
    {
        var list = new List<(DateOnly Key, ReportRow Row)>();
        foreach (var d in LicenceCards.Defs)
        {
            var (no, exp) = LicenceCards.Of(profile, d.Kind);
            no ??= string.Empty;
            if (no.Length == 0 && exp is null) continue;
            int? left = exp is { } e ? e.DayNumber - today.DayNumber : null;
            if (left is { } l && l > within) continue;
            var name = d.Name.EndsWith(" – Renewal", StringComparison.Ordinal) ? d.Name[..^" – Renewal".Length] : d.Name;
            list.Add((exp ?? DateOnly.MaxValue, Row(ReportRowKind.Row, null, ("licence", name), ("number", no), ("issued", null), ("expires", exp), ("daysLeft", left is null ? null : (long)left),
                ("card", LicenceCards.Card(profile, d.Kind) is null ? 0L : 1L))));
        }

        return [.. list.OrderBy(x => x.Key).Select(x => x.Row)];
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!int.TryParse(Param(parameters, "within"), NumberStyles.None, CultureInfo.InvariantCulture, out var within) || within > 3650)
            throw HccException.Validation("Within days: a whole number from 0 to 3650.");
        var snap = await reads.SnapshotAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var today = await reads.BusinessDateAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Cols, Rows(snap.Profile?.Profile, today, within), About(Definition, parameters));
    }
}
