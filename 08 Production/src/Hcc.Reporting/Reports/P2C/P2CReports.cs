// F8-RPT-P2-C · batch c server reports. Same ids, parameters, column keys / types / money flags and figures as the prototype
// (03 Prototype/src/domain/reports/p2-c/*.ts). Pure builders take the facts the sources return, so the unit tests drive them without a database.
//   rpt.acc.asset-register, rpt.people.leave-balances, rpt.stock.grn-register, rpt.rest.sales-by-item, rpt.rest.voids, rpt.admin.scheduled-runs,
//   rpt.admin.cancelled-lines, rpt.admin.rate-sheet.
using System.Globalization;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Expenses;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Yield;
using Hcc.Contracts.Expenses;
using Hcc.Contracts.Yield;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.Money;

namespace Hcc.Reporting.Reports.P2C;

public static class P2CRules
{
    /// <summary>Catalogue D-B says max 92 days; the report enforces it in addition to the binder.</summary>
    public static void Cap92(BoundParams p)
    {
        if (p.From is { } f && p.To is { } t && t.DayNumber - f.DayNumber + 1 > 92) throw new HccException(HccErrorCode.VALIDATION, "The range is longer than 92 days. Narrow the range.");
    }

    /// <summary>Percent with one decimal, half up: same formula as the prototype share1.</summary>
    public static double Share(long amount, long total) => MoneyRules.Share(amount, total);

    public static string Stamp(DateTime utc) => MoneyRules.IstText(utc).Replace('T', ' ');

    /// <summary>Whole numbers as long, halves as double (leave days).</summary>
    public static object Days(decimal v) => v == Math.Floor(v) ? (object)(long)v : (double)v;

    public static string Name(IReadOnlyDictionary<int, string> names, int id) => MoneyRules.NameOf(names, id);

    public static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static ReportDefinition Def(string id, string title, string area, bool money, string? gate, string[] view, ReportDateClass dc, IReadOnlyList<ReportParam> ps,
        IReadOnlyList<ReportColumn> cols, string basis, ReportFormat[] formats, ReportPhone phone, string priority = "P2", bool ownShift = false, string[]? legacy = null) =>
        new(id, title, area, priority, "built", money, gate, view, [], "PERM-RPT-003", dc, ps, cols, basis, formats, phone, false, legacy, ownShift);

    public static readonly ReportFormat[] Sxp = [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf];

    public static readonly ReportFormat[] Sxpc = [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv];

    public static ReportAbout About(ReportDefinition d, BoundParams p) => MoneyRules.About(d, p);

    public static ReportResult Single(ReportDefinition d, BoundParams p, IReadOnlyList<ReportColumn> cols, List<ReportRow> rows) =>
        new(d.Id, d.Title, p, cols, rows, About(d, p));
}

/// <summary>Facts the P2C SQL source returns. Plain records; the builders below are pure.</summary>
public sealed record GrnFact(string GrnNo, DateOnly Date, string Supplier, string Item, long Qty, long RatePaise, string Bill);

public sealed record SaleLineFact(string ItemKey, string Item, string Category, long Qty, long ValuePaise, string Outlet, int SettledBy);

public sealed record VoidFact(string Time, string Check, string Item, long Qty, long ValuePaise, string Reason, int VoidBy, int? ApprovedBy, string Key);

public sealed record LeaveStaffFact(long StaffId, string Name, DateOnly Joined, bool Active, DateOnly? Exited, decimal Cl, decimal Sl, decimal El);

public sealed record LeaveDayFact(long StaffId, string Type, DateOnly Date, decimal Days, string Status);

public sealed record CancelledFact(DateOnly Date, string Key, string Booking, string Room, string Item, long AmountPaise, int By, string Reason);

public interface IP2CReportSource
{
    Task<IReadOnlyDictionary<int, string>> NamesAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<GrnFact>> GrnsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Lines of settled, not voided POS checks settled in the range (value before tax).</summary>
    Task<IReadOnlyList<SaleLineFact>> SalesAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<VoidFact>> VoidsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<(IReadOnlyList<LeaveStaffFact> Staff, IReadOnlyList<LeaveDayFact> Days)> LeaveAsync(int propertyId, DateOnly fyFrom, DateOnly fyTo, CancellationToken ct = default);

    Task<IReadOnlyList<CancelledFact>> CancelledAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

// ── rpt.acc.asset-register ───────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class AssetRegisterReport(IFixedAssetStore store) : IReportSelector
{
    public const string Id = "rpt.acc.asset-register";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.T("no", "Asset no"), MoneyRules.T("name", "Name"), MoneyRules.T("ledger", "Ledger"), MoneyRules.T("vendor", "Vendor"), MoneyRules.Dt("bought", "Bought"),
        MoneyRules.M("cost", "Cost"), MoneyRules.M("itc", "ITC taken"), MoneyRules.Dt("putToUse", "Put to use"), MoneyRules.T("disposal", "Disposal"),
    ];

    public ReportDefinition Definition { get; } = P2CRules.Def(Id, "Fixed-asset register", "acc", true, "PERM-ACC-001", ["PERM-EXP-004", "PERM-ACC-001"], ReportDateClass.AsOf,
        [new("asOf", "As on", "date", ReportDateClass.AsOf)], Cols,
        "Assets bought on or before the date, by asset no. Cost is the bill less the ITC taken and net of posted purchase returns; put-to-use and disposal count only when they fall on or before the date. Depreciation is passed by the CA in Tally (DEC-138).",
        P2CRules.Sxpc, ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var list = await store.ListAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return Build(Definition, parameters, list);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IReadOnlyList<FixedAssetView> assets)
    {
        var asOf = p.To!.Value;
        var list = assets.Where(a => DateOnly.Parse(a.PurchaseDate, CultureInfo.InvariantCulture) <= asOf).OrderBy(a => a.AssetNo, StringComparer.Ordinal).ToList();
        var rows = new List<ReportRow>();
        foreach (var a in list)
        {
            string? put = a.PutToUse is { } pu && DateOnly.Parse(pu, CultureInfo.InvariantCulture) <= asOf ? pu : null;
            string? disposal = a.DisposedDate is { } dd && DateOnly.Parse(dd, CultureInfo.InvariantCulture) <= asOf ? "Disposed " + dd + (string.IsNullOrEmpty(a.DisposedVoucher) ? string.Empty : " · " + a.DisposedVoucher) : null;
            rows.Add(MoneyRules.Row(ReportRowKind.Row, null, ("no", a.AssetNo), ("name", a.Name), ("ledger", Hcc.Domain.Accounting.ChartOfAccounts.NameOf(a.Ledger)),
                ("vendor", string.IsNullOrEmpty(a.Vendor) ? null : a.Vendor), ("bought", DateOnly.Parse(a.PurchaseDate, CultureInfo.InvariantCulture)), ("cost", a.CostPaise), ("itc", a.ItcPaise),
                ("putToUse", put is null ? null : DateOnly.Parse(put, CultureInfo.InvariantCulture)), ("disposal", disposal)));
        }

        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("no", "Total"), ("name", null), ("ledger", null), ("vendor", null), ("bought", null), ("cost", list.Sum(a => a.CostPaise)),
                ("itc", list.Sum(a => a.ItcPaise)), ("putToUse", null), ("disposal", null)));
        return P2CRules.Single(d, p, Cols, rows);
    }
}

// ── rpt.people.leave-balances ────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class LeaveBalancesReport(IP2CReportSource source) : IReportSelector
{
    public const string Id = "rpt.people.leave-balances";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.T("employee", "Employee"), MoneyRules.T("type", "Leave type"), MoneyRules.I("entitlement", "Entitlement"), MoneyRules.I("used", "Used"), MoneyRules.I("pending", "Pending"),
        MoneyRules.I("balance", "Balance"),
    ];

    public ReportDefinition Definition { get; } = P2CRules.Def(Id, "Leave balances", "people", false, null, ["PERM-HR-001"], ReportDateClass.AsOf, [new("asOf", "As on", "date", ReportDateClass.AsOf)], Cols,
        "The financial year (April to March) that contains the date. Entitlement = the policy quota with any per-employee override; used = approved leave days dated up to the date; pending = leave days of the year still waiting for a decision; balance = entitlement less used (no carry-forward). Half days count 0.5.",
        P2CRules.Sxp, ReportPhone.Yes);

    public static (DateOnly From, DateOnly To) Fy(DateOnly d)
    {
        var y = d.Month >= 4 ? d.Year : d.Year - 1;
        return (new DateOnly(y, 4, 1), new DateOnly(y + 1, 3, 31));
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = Fy(parameters.To!.Value);
        var (staff, days) = await source.LeaveAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        return Build(Definition, parameters, staff, days);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IReadOnlyList<LeaveStaffFact> staff, IReadOnlyList<LeaveDayFact> days)
    {
        var asOf = p.To!.Value;
        var rows = new List<ReportRow>();
        foreach (var st in staff.Where(s => s.Joined <= asOf && (s.Active || (s.Exited is { } e && e > asOf))).OrderBy(s => s.Name, StringComparer.CurrentCulture).ThenBy(s => s.StaffId))
            foreach (var (code, label, ent) in new[] { ("CL", "Casual Leave", st.Cl), ("SL", "Sick Leave", st.Sl), ("EL", "Earned Leave", st.El) })
            {
                decimal used = 0, pending = 0;
                foreach (var l in days.Where(x => x.StaffId == st.StaffId && x.Type == code))
                {
                    if (l.Status == "Approved" && l.Date <= asOf) used += l.Days;
                    else if (l.Status == "Pending") pending += l.Days;
                }

                rows.Add(MoneyRules.Row(ReportRowKind.Row, st.Name, ("employee", st.Name), ("type", label), ("entitlement", P2CRules.Days(ent)), ("used", P2CRules.Days(used)),
                    ("pending", P2CRules.Days(pending)), ("balance", P2CRules.Days(ent - used))));
            }

        return P2CRules.Single(d, p, Cols, rows);
    }
}

// ── rpt.stock.grn-register ───────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class GrnRegisterReport(IP2CReportSource source) : IReportSelector
{
    public const string Id = "rpt.stock.grn-register";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.T("grn", "GRN no"), MoneyRules.Dt("date", "Date"), MoneyRules.T("supplier", "Supplier"), MoneyRules.T("item", "Item"), MoneyRules.I("qty", "Qty"), MoneyRules.M("rate", "Rate"),
        MoneyRules.M("amount", "Amount"), MoneyRules.T("bill", "Bill no"),
    ];

    public static readonly IReadOnlyList<ReportColumn> SupplierCols =
        [MoneyRules.T("supplier", "Supplier"), MoneyRules.I("grns", "GRNs"), MoneyRules.I("qty", "Qty"), MoneyRules.M("amount", "Amount")];

    public ReportDefinition Definition { get; } = P2CRules.Def(Id, "Goods received (GRN) register", "stock", true, "PERM-STK-004", ["PERM-STK-004"], ReportDateClass.Business,
        [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business), new("supplier", "Supplier", "text")], Cols,
        "Goods received notes by the business date they were recorded, oldest first; amount = quantity x unit cost on the receipt. The second block totals each supplier.",
        P2CRules.Sxpc, ReportPhone.No);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        P2CRules.Cap92(parameters);
        var facts = await source.GrnsAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return Build(Definition, parameters, facts);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IReadOnlyList<GrnFact> facts)
    {
        var q = MoneyRules.Param(p, "supplier").Trim();
        var list = facts.Where(f => q.Length == 0 || f.Supplier.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Date).ThenBy(f => f.GrnNo, StringComparer.Ordinal).ToList();
        var rows = list.Select(f => MoneyRules.Row(ReportRowKind.Row, f.GrnNo, ("grn", f.GrnNo), ("date", f.Date), ("supplier", f.Supplier), ("item", f.Item), ("qty", f.Qty), ("rate", f.RatePaise),
            ("amount", f.Qty * f.RatePaise), ("bill", string.IsNullOrEmpty(f.Bill) ? null : f.Bill))).ToList();
        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("grn", "Total"), ("date", null), ("supplier", null), ("item", null), ("qty", list.Sum(f => f.Qty)), ("rate", null),
                ("amount", list.Sum(f => f.Qty * f.RatePaise)), ("bill", null)));
        var sup = list.GroupBy(f => f.Supplier).OrderBy(g => g.Key, StringComparer.CurrentCulture)
            .Select(g => MoneyRules.Row(ReportRowKind.Row, null, ("supplier", g.Key), ("grns", (long)g.Select(x => x.GrnNo).Distinct().Count()), ("qty", g.Sum(x => x.Qty)),
                ("amount", g.Sum(x => x.Qty * x.RatePaise)))).ToList();
        if (sup.Count > 0)
            sup.Add(MoneyRules.Row(ReportRowKind.Total, null, ("supplier", "Total"), ("grns", (long)list.Select(x => x.GrnNo).Distinct().Count()), ("qty", list.Sum(x => x.Qty)),
                ("amount", list.Sum(x => x.Qty * x.RatePaise))));
        var a = new ReportTable("lines", "Receipts", Cols, rows);
        var b = new ReportTable("suppliers", "Totals per supplier", SupplierCols, sup);
        return new ReportResult(d.Id, d.Title, p, a.Columns, a.Rows, P2CRules.About(d, p), Sections: [a, b]);
    }
}

// ── rpt.rest.sales-by-item ───────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class SalesByItemReport(IP2CReportSource source, IAccessPolicy policy) : IReportSelector
{
    public const string Id = "rpt.rest.sales-by-item";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.T("item", "Item"), MoneyRules.T("category", "Category"), MoneyRules.I("qty", "Qty"), MoneyRules.M("value", "Value"), new("share", "Share", ReportColumnType.Percent, false),
    ];

    public ReportDefinition Definition { get; } = P2CRules.Def(Id, "Sales by item", "rest", true, "PERM-POS-004", ["PERM-POS-004"], ReportDateClass.Business,
        [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business), new("outlet", "Outlet", "text")], Cols,
        "Settled checks by settlement date; value = quantity x price before tax (voided lines are left out); share = the item value in the report total. Categories by value, items by value inside a category.",
        P2CRules.Sxpc, ReportPhone.Yes, ownShift: true);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        P2CRules.Cap92(parameters);
        var all = await policy.HasAsync(caller.PropertyId, caller.PrincipalId, "PERM-POS-004", new AccessScope(AccessScopeKinds.Property, 0), ct).ConfigureAwait(false);
        var facts = await source.SalesAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return Build(Definition, parameters, facts.Where(f => all || f.SettledBy == caller.PrincipalId).ToList());
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IReadOnlyList<SaleLineFact> facts)
    {
        var q = MoneyRules.Param(p, "outlet").Trim();
        var items = facts.Where(f => q.Length == 0 || string.Equals(f.Outlet, q, StringComparison.OrdinalIgnoreCase)).GroupBy(f => f.ItemKey)
            .Select(g => (Name: g.First().Item, Cat: g.First().Category, Qty: g.Sum(x => x.Qty), Value: g.Sum(x => x.ValuePaise))).ToList();
        var total = items.Sum(x => x.Value);
        var rows = new List<ReportRow>();
        foreach (var cat in items.GroupBy(x => x.Cat).OrderByDescending(g => g.Sum(x => x.Value)).ThenBy(g => g.Key, StringComparer.CurrentCulture))
        {
            foreach (var x in cat.OrderByDescending(x => x.Value).ThenBy(x => x.Name, StringComparer.CurrentCulture))
                rows.Add(MoneyRules.Row(ReportRowKind.Row, cat.Key, ("item", x.Name), ("category", cat.Key), ("qty", x.Qty), ("value", x.Value), ("share", P2CRules.Share(x.Value, total))));
            rows.Add(MoneyRules.Row(ReportRowKind.Subtotal, cat.Key, ("item", cat.Key + " total"), ("category", null), ("qty", cat.Sum(x => x.Qty)), ("value", cat.Sum(x => x.Value)),
                ("share", P2CRules.Share(cat.Sum(x => x.Value), total))));
        }

        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("item", "Total"), ("category", null), ("qty", items.Sum(x => x.Qty)), ("value", total), ("share", total > 0 ? 100.0 : 0.0)));
        return P2CRules.Single(d, p, Cols, rows);
    }
}

// ── rpt.rest.voids ───────────────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class PosVoidsReport(IP2CReportSource source) : IReportSelector
{
    public const string Id = "rpt.rest.voids";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.T("time", "Time"), MoneyRules.T("check", "Check"), MoneyRules.T("item", "Item"), MoneyRules.I("qty", "Qty"), MoneyRules.M("value", "Value"), MoneyRules.T("reason", "Reason"),
        MoneyRules.T("by", "Voided by"), MoneyRules.T("approvedBy", "Approved by"),
    ];

    public ReportDefinition Definition { get; } = P2CRules.Def(Id, "POS voids", "rest", true, "PERM-POS-004", ["PERM-POS-004"], ReportDateClass.Business,
        [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business)], Cols,
        "Voided lines by the business date of the void, oldest first. Value = quantity x price before tax. Approved by is filled when the void needed a second user.",
        P2CRules.Sxp, ReportPhone.No);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        P2CRules.Cap92(parameters);
        var names = await source.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var facts = await source.VoidsAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return Build(Definition, parameters, facts, names);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IReadOnlyList<VoidFact> facts, IReadOnlyDictionary<int, string> names)
    {
        var list = facts.OrderBy(f => f.Time, StringComparer.Ordinal).ThenBy(f => f.Key, StringComparer.Ordinal).ToList();
        string? N(int? id) => id is { } i ? P2CRules.Name(names, i) is { Length: > 0 } n ? n : null : null;
        var rows = list.Select(f => MoneyRules.Row(ReportRowKind.Row, null, ("time", f.Time), ("check", f.Check), ("item", f.Item), ("qty", f.Qty), ("value", f.ValuePaise),
            ("reason", string.IsNullOrEmpty(f.Reason) ? null : f.Reason), ("by", N(f.VoidBy)), ("approvedBy", N(f.ApprovedBy)))).ToList();
        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("time", "Total"), ("check", null), ("item", null), ("qty", list.Sum(f => f.Qty)), ("value", list.Sum(f => f.ValuePaise)),
                ("reason", null), ("by", null), ("approvedBy", null)));
        return P2CRules.Single(d, p, Cols, rows);
    }
}

// ── rpt.admin.scheduled-runs ─────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class ScheduledRunsReport(IReportRunStore runs, IReportScheduleStore schedules, IP2CReportSource source, IServiceProvider? services = null) : IReportSelector
{
    public const string Id = "rpt.admin.scheduled-runs";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.T("schedule", "Schedule"), MoneyRules.T("report", "Report"), MoneyRules.T("runAt", "Run at"), MoneyRules.T("recipients", "Recipients"), MoneyRules.T("result", "Result"), MoneyRules.T("error", "Error"),
    ];

    public ReportDefinition Definition { get; } = P2CRules.Def(Id, "Scheduled runs", "admin", false, null, ["PERM-ADM-004"], ReportDateClass.Business,
        [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business)], Cols,
        "Runs started by a schedule, by the date and time the run started, newest first. Result: ok, failed or held.", P2CRules.Sxp, ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        P2CRules.Cap92(parameters);
        var names = await source.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var all = await runs.ListAsync(caller.PropertyId, 5000, ct).ConfigureAwait(false);
        var sch = await schedules.ListAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var titles = (services?.GetService(typeof(IReportCatalogue)) as IReportCatalogue)?.All.ToDictionary(x => x.Id, x => x.Title, StringComparer.Ordinal) ?? new Dictionary<string, string>();
        return Build(Definition, parameters, all, sch, names, titles);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IReadOnlyList<ReportRunRecord> runs, IReadOnlyList<ReportScheduleRecord> schedules, IReadOnlyDictionary<int, string> names,
        IReadOnlyDictionary<string, string> titles)
    {
        var from = p.From!.Value; var to = p.To!.Value;
        var sch = schedules.ToDictionary(s => s.Id);
        var list = runs.Where(r => r.ScheduleId is not null && DateOnly.FromDateTime(r.StartedAt.UtcDateTime.AddMinutes(330)) is var day && day >= from && day <= to)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).ToList();
        var rows = list.Select(r =>
        {
            var title = titles.TryGetValue(r.ReportId, out var t) ? t : r.ReportId;
            sch.TryGetValue(r.ScheduleId!.Value, out var sc);
            var name = sc is null ? r.ScheduleId!.Value.ToString(CultureInfo.InvariantCulture)
                : $"{title} · {sc.Cadence} {sc.Time.ToString("HH':'mm", CultureInfo.InvariantCulture)} · {P2CRules.Name(names, sc.OwnerPrincipalId)}";
            var rec = sc is null ? null : string.Join(", ", sc.Recipients.Select(u => P2CRules.Name(names, u)));
            return MoneyRules.Row(ReportRowKind.Row, null, ("schedule", name), ("report", title), ("runAt", P2CRules.Stamp(r.StartedAt.UtcDateTime)), ("recipients", string.IsNullOrEmpty(rec) ? null : rec),
                ("result", r.Status), ("error", string.IsNullOrEmpty(r.Error) ? null : r.Error));
        }).ToList();
        return P2CRules.Single(d, p, Cols, rows);
    }
}

// ── rpt.admin.cancelled-lines ────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class CancelledLinesReport(IP2CReportSource source) : IReportSelector
{
    public const string Id = "rpt.admin.cancelled-lines";

    public static readonly IReadOnlyList<ReportColumn> Cols =
    [
        MoneyRules.Dt("date", "Date"), MoneyRules.T("booking", "Booking"), MoneyRules.T("room", "Room"), MoneyRules.T("item", "Item"), MoneyRules.I("qty", "Qty"), MoneyRules.M("amount", "Amount"),
        MoneyRules.T("by", "Cancelled by"), MoneyRules.T("reason", "Reason"),
    ];

    public ReportDefinition Definition { get; } = P2CRules.Def(Id, "Cancelled folio lines", "admin", true, "PERM-RPT-002", ["PERM-RPT-002"], ReportDateClass.Business,
        [MoneyRules.Range(ReportDateClass.Business), MoneyRules.RangeTo(ReportDateClass.Business)], Cols,
        "Reversal lines by the date they were posted (the cancelled line is shown with its own amount before tax), newest first.", P2CRules.Sxp, ReportPhone.No, legacy: ["C1.CancelItem"]);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        P2CRules.Cap92(parameters);
        var names = await source.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var facts = await source.CancelledAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return Build(Definition, parameters, facts, names);
    }

    public static ReportResult Build(ReportDefinition d, BoundParams p, IReadOnlyList<CancelledFact> facts, IReadOnlyDictionary<int, string> names)
    {
        var list = facts.OrderByDescending(f => f.Date).ThenByDescending(f => f.Key, StringComparer.Ordinal).ToList();
        var rows = list.Select(f => MoneyRules.Row(ReportRowKind.Row, null, ("date", f.Date), ("booking", f.Booking), ("room", string.IsNullOrEmpty(f.Room) ? null : f.Room), ("item", f.Item), ("qty", 1L),
            ("amount", f.AmountPaise), ("by", P2CRules.Name(names, f.By) is { Length: > 0 } n ? n : null), ("reason", string.IsNullOrEmpty(f.Reason) ? null : f.Reason))).ToList();
        if (rows.Count > 0)
            rows.Add(MoneyRules.Row(ReportRowKind.Total, null, ("date", null), ("booking", "Total"), ("room", null), ("item", null), ("qty", (long)list.Count), ("amount", list.Sum(f => f.AmountPaise)),
                ("by", null), ("reason", null)));
        return P2CRules.Single(d, p, Cols, rows);
    }
}
