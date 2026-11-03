// F7-R4 · batch 1 money reports (server): shift report (X / Z + hand-over), collection detail, credit bills, expense register, expenses by head.
// Same ids, column keys, parameters and figures as the prototype (03 Prototype/src/domain/reports/money/*.ts). Pure builders (MoneyRules) take the facts
// the sources return, so the unit tests drive them without a database.
using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Expenses;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Expenses;
using Hcc.Reporting.Sql;

namespace Hcc.Reporting.Reports.Money;

public static class MoneyRules
{
    public static string ReceiptNo(long id) => "RC-" + id.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>Include bucket of a collection kind: receipts / advances / corrections; null = credit raised (not a receipt).</summary>
    public static string? Include(string kind) => kind switch
    {
        "payment" or "counter" or "company-payment" or "udhaar-payment" => "receipts",
        "deposit" or "deposit-return" or "deposit-apply" => "advances",
        "refund" or "counter-credit" => "corrections",
        _ => null, // company-credit, ota-credit, guest-credit
    };

    /// <summary>Signed amount: refunds and deposit returns are negative (receipt amounts are stored positive).</summary>
    public static long Signed(CollectionFact f) => f.Kind is "refund" or "deposit-return" ? -f.Amount : f.Amount;

    public static double Share(long amount, long total) => total > 0 ? Math.Floor((amount * 2000.0 + total) / (2.0 * total)) / 10 : 0;

    public static string IstText(DateTime utc) => utc.AddMinutes(330).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    public static readonly string[] ExpGroups = ["Operating expenses", "Petty cash", "Recurring bills"];

    public static string ExpGroup(string? source) => source == "petty" ? ExpGroups[1] : source == "recurring" ? ExpGroups[2] : ExpGroups[0];

    public static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static ReportColumn Dt(string key, string label) => new(key, label, ReportColumnType.Date, false);

    public static ReportParam Range(ReportDateClass dc) => new("from", "From", "date", dc);

    public static ReportParam RangeTo(ReportDateClass dc) => new("to", "To", "date", dc);

    public static ReportRow Row(ReportRowKind kind, string? group, params (string Key, object? Value)[] cells)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in cells) d[k] = v;
        return new ReportRow(kind, d, group);
    }

    public static string Param(BoundParams p, string key) => p.Values.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;

    public static ReportAbout About(ReportDefinition d, BoundParams p) => new(DateTimeOffset.MinValue, string.Empty, d.DateBasis, false, p.Legacy, p.From, p.To);

    /// <summary>True when the caller sees the whole property (finance reports); a Cashier holds only the own-shift grant.</summary>
    public static async Task<bool> SeesAllAsync(IAccessPolicy policy, Caller c, CancellationToken ct)
    {
        var scope = new AccessScope(AccessScopeKinds.Property, 0);
        return await policy.HasAsync(c.PropertyId, c.PrincipalId, "PERM-RPT-001", scope, ct).ConfigureAwait(false)
            || await policy.HasAsync(c.PropertyId, c.PrincipalId, "PERM-RPT-002", scope, ct).ConfigureAwait(false);
    }

    public static string NameOf(IReadOnlyDictionary<int, string> names, int id) => names.TryGetValue(id, out var n) ? n : id == 0 ? string.Empty : "user " + id.ToString(CultureInfo.InvariantCulture);
}

// ── rpt.money.shift-report ──────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class ShiftReport(IMoneyReportSource source, IAccessPolicy policy) : IReportSelector
{
    public const string Id = "rpt.money.shift-report";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.Dt("date", "Date"), MoneyRules.T("shift", "Shift"), MoneyRules.T("cashier", "Cashier"), MoneyRules.M("opening", "Opening float"), MoneyRules.M("receipts", "Receipts"),
        MoneyRules.M("payments", "Payments"), MoneyRules.M("expected", "Expected cash"), MoneyRules.M("counted", "Counted"), MoneyRules.M("difference", "Difference"),
        MoneyRules.T("handedTo", "Handed over to"), MoneyRules.T("handedAt", "Hand-over time"),
    ];

    public static readonly IReadOnlyList<ReportColumn> CashierCols =
        [MoneyRules.T("cashier", "Cashier"), MoneyRules.I("shifts", "Shifts"), MoneyRules.M("receipts", "Receipts"), MoneyRules.M("payments", "Payments"), MoneyRules.M("difference", "Difference")];

    public ReportDefinition Definition { get; } = new(Id, "Shift report (X and Z) and cash hand-over", "money", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003",
        ReportDateClass.Business, [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business), new("cashier", "Cashier", "user"), new("shift", "Shift", "enum", null, ["all", "Day"], "all")],
        Cols, "Per business date: opening float and counted cash from the shift count, receipts and payments from the drawer movements (cash in / cash out / cash expenses / bank and till hand-overs), expected = opening + receipts - payments. The open day is a running X reading; a closed day is the Z reading.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes, OwnShift: true);

    /// <summary>One row per shift: Z for a closed shift, X for one still open or submitted. Payments = receipts + opening - expected, so the identity always holds.</summary>
    public static IReadOnlyList<ShiftLine> Lines(IEnumerable<ShiftFact> facts, IReadOnlyDictionary<int, string> names) =>
        [.. facts.Select(f =>
        {
            var expected = f.StoredExpected is { } e && f.Status != "Open" ? e : f.Opening + f.Receipts + f.Other;
            var closed = f.Status == "Closed";
            var handed = f.HandKind switch { "banking" => ("Bank · " + f.HandBank).Trim(), "till" => f.HandTill ?? string.Empty, "drawing" => "Owner (drawing)", _ => string.Empty };
            return new ShiftLine(f.Date, f.Till + (closed ? " (Z)" : " (X, open)"), f.UserId, MoneyRules.NameOf(names, f.UserId), f.Opening, f.Receipts, f.Opening + f.Receipts - expected, expected, f.Counted,
                f.Counted is { } c ? c - expected : null, handed, f.HandUtc is { } u ? MoneyRules.IstText(u) : string.Empty);
        })];

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var all = await MoneyRules.SeesAllAsync(policy, caller, ct).ConfigureAwait(false);
        var names = await source.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var facts = await source.ShiftsAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        var cashier = MoneyRules.Param(parameters, "cashier");
        var shift = MoneyRules.Param(parameters, "shift");
        var lines = Lines(facts, names).Where(l => (all || l.CashierId == caller.PrincipalId)
            && (cashier.Length == 0 || l.CashierId.ToString(CultureInfo.InvariantCulture) == cashier) && (shift.Length == 0 || shift == "all" || l.Shift.StartsWith(shift, StringComparison.Ordinal))).ToList();
        return Build(Definition, parameters, lines);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IReadOnlyList<ShiftLine> lines)
    {
        var body = lines.Select(l => MoneyRules.Row(ReportRowKind.Row, null, ("date", l.Date), ("shift", l.Shift), ("cashier", l.Cashier), ("opening", l.Opening), ("receipts", l.Receipts), ("payments", l.Payments),
            ("expected", l.Expected), ("counted", l.Counted), ("difference", l.Difference), ("handedTo", l.HandedTo), ("handedAt", l.HandedAt))).ToList();
        if (body.Count > 0)
            body.Add(MoneyRules.Row(ReportRowKind.Total, null, ("date", null), ("shift", "Total"), ("cashier", null), ("opening", lines.Sum(l => l.Opening)), ("receipts", lines.Sum(l => l.Receipts)),
                ("payments", lines.Sum(l => l.Payments)), ("expected", lines.Sum(l => l.Expected)), ("counted", lines.Sum(l => l.Counted ?? 0)), ("difference", lines.Sum(l => l.Difference ?? 0)),
                ("handedTo", null), ("handedAt", null)));
        var per = lines.GroupBy(l => l.CashierId).Select(g => MoneyRules.Row(ReportRowKind.Row, null, ("cashier", g.First().Cashier), ("shifts", (long)g.Count()), ("receipts", g.Sum(l => l.Receipts)),
            ("payments", g.Sum(l => l.Payments)), ("difference", g.Sum(l => l.Difference ?? 0)))).ToList();
        var a = new ReportTable("shifts", "Shifts", Cols, body);
        var b = new ReportTable("cashiers", "Totals per cashier", CashierCols, per);
        return new ReportResult(Id, d.Title, p, a.Columns, a.Rows, MoneyRules.About(d, p), Sections: [a, b]);
    }
}

public sealed record ShiftLine(DateOnly Date, string Shift, int CashierId, string Cashier, long Opening, long Receipts, long Payments, long Expected, long? Counted, long? Difference, string HandedTo, string HandedAt);

// ── rpt.money.collection-detail ─────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class CollectionDetailReport(IMoneyReportSource source, IAccessPolicy policy) : IReportSelector
{
    public const string Id = "rpt.money.collection-detail";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.Dt("date", "Date"), MoneyRules.T("receiptNo", "Receipt no"), MoneyRules.T("guest", "Guest", ReportMask.GuestName), MoneyRules.T("room", "Room"), MoneyRules.T("billNo", "Bill no"),
        MoneyRules.T("tender", "Tender"), MoneyRules.M("amount", "Amount"), MoneyRules.M("tds", "TDS"), MoneyRules.T("by", "Entered by"), MoneyRules.T("source", "Source"),
    ];

    public static readonly IReadOnlyList<ReportColumn> TenderCols = [MoneyRules.T("tender", "Tender"), MoneyRules.I("count", "Receipts"), MoneyRules.M("amount", "Amount"), MoneyRules.M("tds", "TDS")];

    public ReportDefinition Definition { get; } = new(Id, "Collection detail (receipts register)", "money", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003",
        ReportDateClass.Business, [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business), new("tender", "Tender", "text"),
            new("groupBy", "Group by", "enum", null, ["tender", "bill", "user"], "tender"), new("include", "Include", "enum", null, ["receipts", "advances", "corrections"], "receipts")],
        Cols, "Receipts by receipt (business) date. Refunds and deposit returns are negative. Credit raised on a company or guest account is not a receipt (see Credit bills); money received against that credit is a credit settlement.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, OwnShift: true);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var all = await MoneyRules.SeesAllAsync(policy, caller, ct).ConfigureAwait(false);
        var names = await source.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var facts = await source.CollectionsAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return Build(Definition, parameters, facts, names, all ? null : caller.PrincipalId);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IEnumerable<CollectionFact> facts, IReadOnlyDictionary<int, string> names, int? onlyBy)
    {
        var inc = MoneyRules.Param(p, "include"); if (inc.Length == 0) inc = "receipts";
        var gb = MoneyRules.Param(p, "groupBy"); if (gb.Length == 0) gb = "tender";
        var tender = MoneyRules.Param(p, "tender");
        var lines = facts.Where(f => MoneyRules.Include(f.Kind) == inc && (onlyBy is null || f.By == onlyBy) && (tender.Length == 0 || f.Tender == tender))
            .OrderBy(f => f.Date).ToList(); // stable: the source order is the record order within a date
        string Key(CollectionFact f) => gb == "bill" ? (f.BillNo.Length == 0 ? "(no bill)" : f.BillNo) : gb == "user" ? MoneyRules.NameOf(names, f.By) : f.Tender;
        var rows = new List<ReportRow>();
        foreach (var g in lines.GroupBy(Key).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            foreach (var f in g)
                rows.Add(MoneyRules.Row(ReportRowKind.Row, g.Key, ("date", f.Date), ("receiptNo", f.ReceiptNo), ("guest", f.Guest), ("room", f.Room), ("billNo", f.BillNo), ("tender", f.Tender),
                    ("amount", MoneyRules.Signed(f)), ("tds", f.Tds), ("by", MoneyRules.NameOf(names, f.By)), ("source", f.Source)));
            rows.Add(MoneyRules.Row(ReportRowKind.Subtotal, g.Key, ("date", null), ("receiptNo", "Total " + g.Key), ("guest", null), ("room", null), ("billNo", null), ("tender", null),
                ("amount", g.Sum(MoneyRules.Signed)), ("tds", g.Sum(f => f.Tds)), ("by", null), ("source", null)));
        }

        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("date", null), ("receiptNo", "Grand total"), ("guest", null), ("room", null), ("billNo", null), ("tender", null),
                ("amount", lines.Sum(MoneyRules.Signed)), ("tds", lines.Sum(f => f.Tds)), ("by", null), ("source", null)));
        var summary = lines.GroupBy(f => f.Tender).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => MoneyRules.Row(ReportRowKind.Row, null, ("tender", g.Key), ("count", (long)g.Count()), ("amount", g.Sum(MoneyRules.Signed)), ("tds", g.Sum(f => f.Tds)))).ToList();
        var a = new ReportTable("receipts", "Receipts", Cols, rows);
        var b = new ReportTable("tenders", "Summary by tender", TenderCols, summary);
        return new ReportResult(Id, d.Title, p, a.Columns, a.Rows, MoneyRules.About(d, p), Sections: [a, b]);
    }
}

// ── rpt.money.credit-bills ──────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class CreditBillsReport(IMoneyReportSource source) : IReportSelector
{
    public const string Id = "rpt.money.credit-bills";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.Dt("date", "Date"), MoneyRules.T("billNo", "Bill no"), MoneyRules.T("party", "Company or guest", ReportMask.GuestName), MoneyRules.T("room", "Room"),
        MoneyRules.M("credit", "Credit amount"), MoneyRules.M("limit", "Credit limit"), MoneyRules.M("used", "Used after this bill"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Credit bills of the day", "money", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003",
        ReportDateClass.Business, [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business), new("party", "Party", "text")], Cols,
        "Credit raised by business date: company credit and guest credit given at check-out, and company counter bills. Used after this bill = credit raised on the account up to this bill less payments received up to its date (balances carried in from before HCC are in the accounting opening balances). Outstanding and ageing are in the accounting ageing report.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (credit, pays) = await source.CreditAsync(caller.PropertyId, parameters.To!.Value, ct).ConfigureAwait(false);
        return Build(Definition, parameters, credit, pays);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IEnumerable<CreditFact> credit, IEnumerable<CreditPaymentFact> pays)
    {
        var payList = pays.ToList();
        var running = new Dictionary<string, long>(StringComparer.Ordinal);
        var lines = new List<(CreditFact F, long Used)>();
        foreach (var f in credit.OrderBy(x => x.Date).ThenBy(x => x.Order))
        {
            var after = running.GetValueOrDefault(f.Account) + f.Credit;
            running[f.Account] = after;
            var paid = payList.Where(x => x.Account == f.Account && x.Date <= f.Date).Sum(x => x.Amount);
            lines.Add((f, Math.Max(0, after - paid)));
        }

        var q = MoneyRules.Param(p, "party").Trim();
        var shown = lines.Where(l => l.F.Date >= p.From && l.F.Date <= p.To && (q.Length == 0 || l.F.Party.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        var rows = shown.Select(l => MoneyRules.Row(ReportRowKind.Row, null, ("date", l.F.Date), ("billNo", l.F.BillNo), ("party", l.F.Party), ("room", l.F.Room), ("credit", l.F.Credit),
            ("limit", l.F.Limit), ("used", l.Used))).ToList();
        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("date", null), ("billNo", "Total credit raised"), ("party", null), ("room", null), ("credit", shown.Sum(l => l.F.Credit)), ("limit", null), ("used", null)));
        return new ReportResult(Id, d.Title, p, Cols, rows, MoneyRules.About(d, p));
    }
}

// ── rpt.money.expense-register ──────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class ExpenseRegisterReport(IExpenseStore store) : IReportSelector
{
    public const string Id = "rpt.money.expense-register";

    public static readonly IReadOnlyList<string> Statuses = ["all", "Draft", "Pending", "Posted", "Rejected", "Reversed", "Void"];

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.Dt("date", "Date"), MoneyRules.T("description", "Description or reference"), MoneyRules.T("category", "Category"), MoneyRules.T("funding", "Funding"), MoneyRules.M("amount", "Amount"),
        MoneyRules.M("gst", "GST amount"), new("gstRate", "GST rate", ReportColumnType.Percent, false), MoneyRules.T("itc", "ITC"), MoneyRules.T("status", "Status"), MoneyRules.T("vendor", "Vendor"),
        MoneyRules.T("by", "Created by"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Expense register", "money", "P1", "built", true, "PERM-RPT-002", ["PERM-EXP-001", "PERM-RPT-002"], [], "PERM-RPT-003",
        ReportDateClass.Business, [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business), new("status", "Status", "enum", null, Statuses, "all"),
            new("category", "Category", "text"), new("funding", "Funding", "text"), new("q", "Search", "text")], Cols,
        "Expenses by business date, oldest first. Totals count Posted and Reversed expenses with their reversal records; Draft, Pending, Rejected and Void rows are listed but not totalled, and capital purchases (asset register) are listed but not totalled.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var ledger = await store.LedgerAsync(null, caller.PropertyId, parameters.From, parameters.To, ct).ConfigureAwait(false);
        return Build(Definition, parameters, ledger);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IEnumerable<LedgerRow> ledger)
    {
        var status = MoneyRules.Param(p, "status"); var cat = MoneyRules.Param(p, "category"); var fund = MoneyRules.Param(p, "funding"); var q = MoneyRules.Param(p, "q").Trim();
        var list = ledger.Where(e => e.Date >= p.From && e.Date <= p.To && (status.Length == 0 || status == "all" || e.Status == status) && (cat.Length == 0 || e.Category == cat)
            && (fund.Length == 0 || e.Method == fund) && (q.Length == 0 || (e.DescriptionOrVendor + " " + e.Vendor + " " + e.Category).Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        static bool Counted(LedgerRow e) => e.Counts && !e.Capital;
        var rows = new List<ReportRow>();
        foreach (var g in list.GroupBy(e => e.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            foreach (var e in g)
                rows.Add(MoneyRules.Row(ReportRowKind.Row, g.Key, ("date", e.Date), ("description", e.DescriptionOrVendor + (e.IsReversal ? " (reversal)" : string.Empty)), ("category", e.Category),
                    ("funding", e.Method), ("amount", e.Amount), ("gst", e.GstPaise ?? 0), ("gstRate", e.GstRateBps is { } r and > 0 ? r / 100.0 : null),
                    ("itc", e.GstPaise is > 0 ? (e.ItcAllowed ? "allowed" : "disallowed") : string.Empty), ("status", e.Status), ("vendor", e.Vendor), ("by", e.CreatedBy)));
            var t = g.Where(Counted).ToList();
            rows.Add(MoneyRules.Row(ReportRowKind.Subtotal, g.Key, ("date", null), ("description", "Total " + g.Key), ("category", g.Key), ("funding", null), ("amount", t.Sum(e => e.Amount)),
                ("gst", t.Sum(e => e.GstPaise ?? 0)), ("gstRate", null), ("itc", null), ("status", null), ("vendor", null), ("by", null)));
        }

        var all = list.Where(Counted).ToList();
        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("date", null), ("description", "Grand total"), ("category", null), ("funding", null), ("amount", all.Sum(e => e.Amount)),
                ("gst", all.Sum(e => e.GstPaise ?? 0)), ("gstRate", null), ("itc", null), ("status", null), ("vendor", null), ("by", null)));
        return new ReportResult(Id, d.Title, p, Cols, rows, MoneyRules.About(d, p));
    }
}

// ── rpt.money.expenses-by-head ──────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class ExpensesByHeadReport(IExpenseStore store) : IReportSelector
{
    public const string Id = "rpt.money.expenses-by-head";

    public static readonly IReadOnlyList<ReportColumn> Cols =
        [MoneyRules.T("group", "Group"), MoneyRules.T("head", "Head"), MoneyRules.I("entries", "Entries"), MoneyRules.M("amount", "Amount"), new("share", "Share", ReportColumnType.Percent, false)];

    public ReportDefinition Definition { get; } = new(Id, "Expenses by head", "money", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003",
        ReportDateClass.Financial, [MoneyRules.Range(ReportDateClass.Financial), MoneyRules.RangeTo(ReportDateClass.Financial), new("group", "Group", "enum", null, ["all", .. MoneyRules.ExpGroups], "all"),
            new("head", "Head", "text")], Cols,
        "Expenses by business date, net of reversals; Posted and Reversed expenses with their reversal records. Capital purchases sit on the asset register, not here.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Summary);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var ledger = await store.LedgerAsync(null, caller.PropertyId, parameters.From, parameters.To, ct).ConfigureAwait(false);
        return Build(Definition, parameters, ledger);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IEnumerable<LedgerRow> ledger)
    {
        var group = MoneyRules.Param(p, "group"); var head = MoneyRules.Param(p, "head");
        var list = ledger.Where(e => e.Date >= p.From && e.Date <= p.To && e.Counts && !e.Capital && (group.Length == 0 || group == "all" || MoneyRules.ExpGroup(e.Source) == group)
            && (head.Length == 0 || e.Category == head)).ToList();
        var total = list.Sum(e => e.Amount);
        var rows = new List<ReportRow>();
        foreach (var g in MoneyRules.ExpGroups)
        {
            var gl = list.Where(e => MoneyRules.ExpGroup(e.Source) == g).ToList();
            if (gl.Count == 0) continue;
            foreach (var h in gl.GroupBy(e => e.Category).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var a = h.Sum(e => e.Amount);
                rows.Add(MoneyRules.Row(ReportRowKind.Row, g, ("group", g), ("head", h.Key), ("entries", (long)h.Count()), ("amount", a), ("share", MoneyRules.Share(a, total))));
            }

            var ga = gl.Sum(e => e.Amount);
            rows.Add(MoneyRules.Row(ReportRowKind.Subtotal, g, ("group", g), ("head", "Total " + g), ("entries", (long)gl.Count), ("amount", ga), ("share", MoneyRules.Share(ga, total))));
        }

        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("group", "Grand total"), ("head", null), ("entries", (long)list.Count), ("amount", total), ("share", total > 0 ? 100.0 : 0.0)));
        return new ReportResult(Id, d.Title, p, Cols, rows, MoneyRules.About(d, p));
    }
}
