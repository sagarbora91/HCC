// F7-R4 · batch 1 money reports on the server engine: shift report, collection detail, credit bills, expense register, expenses by head.
// The builders are driven with in-memory facts (the SQL source and the expense ledger are the only database reads); each test checks a tie-out the
// prototype suite (tests/stream-rpt-money-a.mjs) checks on its side, plus the role matrix, rule M and three-way equality (screen = Excel = PDF text).
using System.Reflection;
using Hcc.Application.Expenses;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Reporting.Reports.Money;
using Hcc.Reporting.Sql;

namespace Hcc.Reporting.Tests;

public sealed class MoneyAReportsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly D1 = new(2026, 10, 1), D2 = new(2026, 10, 2), D3 = new(2026, 10, 3);
    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string> { [1] = "Sagar", [2] = "Manager", [4] = "Priya", [5] = "Meera" };

    private sealed class FakeSource : IMoneyReportSource
    {
        public List<ShiftFact> Shifts { get; } = [];
        public List<CollectionFact> Collections { get; } = [];
        public List<CreditFact> Credit { get; } = [];
        public List<CreditPaymentFact> Payments { get; } = [];
        public Task<IReadOnlyDictionary<int, string>> NamesAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(Names);
        public Task<IReadOnlyList<ShiftFact>> ShiftsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ShiftFact>>(Shifts.Where(s => s.Date >= from && s.Date <= to).ToList());
        public Task<IReadOnlyList<CollectionFact>> CollectionsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CollectionFact>>(Collections.Where(s => s.Date >= from && s.Date <= to).ToList());
        public Task<(IReadOnlyList<CreditFact> Credit, IReadOnlyList<CreditPaymentFact> Payments)> CreditAsync(int propertyId, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<CreditFact>, IReadOnlyList<CreditPaymentFact>)>((Credit.Where(c => c.Date <= to).ToList(), Payments.Where(c => c.Date <= to).ToList()));
    }

    /// <summary>An IExpenseStore that answers only LedgerAsync (the one read the two expense reports make).</summary>
    public class LedgerProxy : DispatchProxy
    {
        public List<LedgerRow> Rows { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name != nameof(IExpenseStore.LedgerAsync)) throw new NotSupportedException(targetMethod.Name);
            var from = (DateOnly?)args![2]; var to = (DateOnly?)args[3];
            return Task.FromResult<IReadOnlyList<LedgerRow>>(Rows.Where(r => (from is null || r.Date >= from) && (to is null || r.Date <= to)).ToList());
        }
    }

    private static (IExpenseStore Store, LedgerProxy Proxy) Ledger(params LedgerRow[] rows)
    {
        var store = DispatchProxy.Create<IExpenseStore, LedgerProxy>();
        var proxy = (LedgerProxy)(object)store; proxy.Rows = [.. rows];
        return (store, proxy);
    }

    private static LedgerRow L(long id, DateOnly d, string cat, long amount, string status = "Posted", string? source = null, long? gst = null, int? bps = null, bool itc = false, bool capital = false, bool rev = false, string method = "Cash") =>
        new(id, d, cat, method, "Vendor " + id, "Entry " + id, amount, source, null, status, null, "Meera", gst, bps, rev, DateTime.UnixEpoch, null, itc, capital);

    private static FakeSource SourceWithData()
    {
        var s = new FakeSource();
        s.Shifts.Add(new(1, "Day", D1, "Closed", 500_000, 1_095_600, 1_095_600, 4, 905_600, -310_000, "banking", "SBI", null, new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc)));
        s.Shifts.Add(new(2, "Day", D2, "Closed", 500_000, 1_353_300, 1_353_000, 4, 1_101_800, -248_500, null, null, null, null));
        s.Shifts.Add(new(3, "Day", D3, "Open", 500_000, null, null, 5, 200_000, -50_000, null, null, null, null));
        s.Collections.AddRange([
            new(D1, "RC-000001", "Deepak Zende", "205", "INV/1", "Cash", 100_000, 0, 4, "stay", "payment"),
            new(D1, "RC-000002", "Vandana Joshi", "303", "INV/2", "UPI", 250_000, 0, 5, "stay", "payment"),
            new(D1, "RC-000003", "Vandana Joshi", "303", "INV/2", "Cash", 50_000, 0, 4, "stay", "refund"),
            new(D2, "RC-000004", "Deepak Zende", "205", "INV/1", "Cash", 80_000, 0, 4, "stay", "deposit"),
            new(D2, "RC-000005", "Deepak Zende", "205", "INV/1", "Cash", 80_000, 0, 4, "stay", "deposit-return"),
            new(D2, "GCP-1", "Deccan Agro Ltd", "", "INV/9", "Bank transfer", 570_000, 60_000, 1, "credit settlement", "company-payment"),
            new(D3, "RC-000006", "Walk In", "", "CB/1", "Card", 118_000, 0, 5, "counter bill", "counter"),
            new(D3, "RC-000007", "Nirmal Foods", "202", "INV/7", "Company credit", 183_750, 0, 4, "stay", "company-credit"),
        ]);
        s.Credit.AddRange([new(D1, 0, "INV/7", "Nirmal Foods", "202", 183_750, 500_000, "C:1"), new(D2, 1, "INV/8", "Nirmal Foods", "203", 100_000, 500_000, "C:1"),
            new(D2, 2, "INV/G", "A Guest", "104", 40_000, 0, "G:9")]);
        s.Payments.AddRange([new("C:1", D2, 50_000), new("C:1", D3, 1_000_000)]);
        return s;
    }

    private static readonly IReadOnlyDictionary<string, string?> Oct = new Dictionary<string, string?> { ["from"] = "2026-10-01", ["to"] = "2026-10-03" };

    private static ReportRunner Runner(FakePolicy? p, FakeSource src, IExpenseStore store) =>
        Rig.Runner(p, new ShiftReport(src, p ?? new FakePolicy()), new CollectionDetailReport(src, p ?? new FakePolicy()), new CreditBillsReport(src), new ExpenseRegisterReport(store), new ExpensesByHeadReport(store));

    private static Task<ReportResult> Run(ReportRunner r, int who, string id, IReadOnlyDictionary<string, string?>? p = null) => r.RunAsync(Rig.C(who), id, p ?? Oct, new RunOptions(All: true), Ct);

    private static IEnumerable<ReportRow> Body(ReportTable t) => t.Rows.Where(x => x.Kind == ReportRowKind.Row);

    private static ReportTable T(ReportResult r, int i = 0) => (r.Sections ?? [new ReportTable("main", null, r.Columns, r.Rows)])[i];

    private static long Cell(ReportRow r, string k) => Convert.ToInt64(r.Cells[k] ?? 0L);

    [Fact]
    public void Money_reports_are_in_the_catalogue_built_and_finance_gated()
    {
        var src = new FakeSource(); var (st, _) = Ledger();
        var cat = new ReportCatalogue([new ShiftReport(src, null!), new CollectionDetailReport(src, null!), new CreditBillsReport(src), new ExpenseRegisterReport(st), new ExpensesByHeadReport(st)]);
        Assert.Equal(5, cat.All.Count);
        Assert.All(cat.All, d => { Assert.Equal("built", d.Status); Assert.True(d.MoneyFlag); Assert.Equal("PERM-RPT-002", d.MoneyGate); });
        Assert.True(cat.All.Single(d => d.Id == ShiftReport.Id).OwnShift);
        Assert.True(cat.All.Single(d => d.Id == CollectionDetailReport.Id).OwnShift);
        Assert.False(cat.All.Single(d => d.Id == ExpenseRegisterReport.Id).OwnShift);
    }

    [Fact]
    public async Task Shift_report_identity_totals_and_handover()
    {
        var src = SourceWithData(); var (st, _) = Ledger();
        var r = await Run(Runner(null, src, st), 1, ShiftReport.Id);
        var rows = Body(T(r)).ToList();
        Assert.Equal(3, rows.Count);
        foreach (var x in rows) Assert.Equal(Cell(x, "expected"), Cell(x, "opening") + Cell(x, "receipts") - Cell(x, "payments"));
        Assert.Equal("Day (Z)", rows[0].Cells["shift"]); Assert.Equal("Day (X, open)", rows[2].Cells["shift"]);
        Assert.Equal("Bank · SBI", rows[0].Cells["handedTo"]); Assert.Equal("2026-10-01T23:30", rows[0].Cells["handedAt"]);
        Assert.Equal(-300L, Cell(rows[1], "difference")); // counted 1,353,000 vs expected 1,353,300
        Assert.Null(rows[2].Cells["counted"]);
        var total = T(r).Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(rows.Sum(x => Cell(x, "receipts")), Cell(total, "receipts"));
        Assert.Equal(T(r, 1).Rows.Sum(x => Cell(x, "receipts")), Cell(total, "receipts"));
        Assert.Equal(3L, T(r, 1).Rows.Sum(x => Cell(x, "shifts")));
    }

    [Fact]
    public async Task Shift_report_cashier_sees_own_shifts_only_and_cannot_export()
    {
        var src = SourceWithData(); var (st, _) = Ledger();
        var run = Runner(null, src, st);
        var r = await Run(run, 4, ShiftReport.Id);
        Assert.All(Body(T(r)), x => Assert.Equal("Priya", x.Cells["cashier"]));
        Assert.Equal(2, Body(T(r)).Count());
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => run.RunAsync(Rig.C(4), ShiftReport.Id, Oct, new RunOptions(All: true, Export: true), Ct)));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, 4, CreditBillsReport.Id)));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, 4, ExpenseRegisterReport.Id)));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, 4, ExpensesByHeadReport.Id)));
        var cd = await Run(run, 4, CollectionDetailReport.Id);
        Assert.All(Body(T(cd)), x => Assert.Equal("Priya", x.Cells["by"]));
    }

    [Fact]
    public async Task Collection_detail_buckets_signs_subtotals_and_summary()
    {
        var src = SourceWithData(); var (st, _) = Ledger();
        var run = Runner(null, src, st);
        var r = await Run(run, 1, CollectionDetailReport.Id);
        var a = T(r);
        Assert.DoesNotContain(Body(a), x => (string)x.Cells["receiptNo"]! == "RC-000007"); // credit raised is not a receipt
        var grand = Cell(a.Rows.Single(x => x.Kind == ReportRowKind.Total), "amount");
        Assert.Equal(100_000 + 250_000 + 570_000 + 118_000, grand);
        Assert.Equal(grand, a.Rows.Where(x => x.Kind == ReportRowKind.Subtotal).Sum(x => Cell(x, "amount")));
        Assert.Equal(grand, T(r, 1).Rows.Sum(x => Cell(x, "amount")));
        Assert.Equal(60_000L, Cell(a.Rows.Single(x => x.Kind == ReportRowKind.Total), "tds"));
        var adv = await Run(run, 1, CollectionDetailReport.Id, new Dictionary<string, string?>(Oct) { ["include"] = "advances" });
        Assert.Equal(0L, Cell(T(adv).Rows.Single(x => x.Kind == ReportRowKind.Total), "amount")); // deposit 80,000 - returned 80,000
        var cor = await Run(run, 1, CollectionDetailReport.Id, new Dictionary<string, string?>(Oct) { ["include"] = "corrections" });
        Assert.Equal(-50_000L, Cell(T(cor).Rows.Single(x => x.Kind == ReportRowKind.Total), "amount"));
        foreach (var g in new[] { "bill", "user" })
        {
            var x = await Run(run, 1, CollectionDetailReport.Id, new Dictionary<string, string?>(Oct) { ["groupBy"] = g });
            Assert.Equal(grand, Cell(T(x).Rows.Single(y => y.Kind == ReportRowKind.Total), "amount"));
        }
    }

    [Fact]
    public async Task Credit_bills_running_balance_limit_and_party_filter()
    {
        var src = SourceWithData(); var (st, _) = Ledger();
        var run = Runner(null, src, st);
        var r = await Run(run, 5, CreditBillsReport.Id);
        var rows = Body(T(r)).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(183_750L, Cell(rows[0], "used"));
        Assert.Equal(183_750L + 100_000 - 50_000, Cell(rows[1], "used")); // the ₹500 payment dated the second day is deducted
        Assert.Equal(500_000L, Cell(rows[0], "limit"));
        Assert.Equal(183_750L + 100_000 + 40_000, Cell(T(r).Rows.Single(x => x.Kind == ReportRowKind.Total), "credit"));
        var one = await Run(run, 5, CreditBillsReport.Id, new Dictionary<string, string?>(Oct) { ["party"] = "guest" });
        Assert.Single(Body(T(one)));
        var late = await Run(run, 5, CreditBillsReport.Id, new Dictionary<string, string?> { ["from"] = "2026-10-03", ["to"] = "2026-10-03" });
        Assert.Empty(T(late).Rows);
    }

    [Fact]
    public async Task Expense_register_totals_count_posted_only_and_leave_out_capital_and_pending()
    {
        var (st, _) = Ledger(L(1, D1, "Laundry", 400_000, gst: 20_000, bps: 500, itc: true), L(2, D1, "Laundry", 100_000, "Pending"), L(3, D2, "Supplies", 250_000, capital: true),
            L(4, D2, "Supplies", 90_000), L(5, D3, "Supplies", -90_000, rev: true), L(6, D3, "Supplies", 70_000, "Void"));
        var run = Runner(null, new FakeSource(), st);
        var r = await Run(run, 1, ExpenseRegisterReport.Id);
        var a = T(r);
        Assert.Equal(6, Body(a).Count());
        Assert.Equal(400_000L + 90_000 - 90_000, Cell(a.Rows.Single(x => x.Kind == ReportRowKind.Total), "amount"));
        Assert.Equal(a.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["amount"], a.Rows.Where(x => x.Kind == ReportRowKind.Subtotal).Sum(x => Cell(x, "amount")));
        var first = Body(a).First();
        Assert.Equal(5.0, first.Cells["gstRate"]); Assert.Equal("allowed", first.Cells["itc"]); Assert.Equal(20_000L, first.Cells["gst"]);
        Assert.Contains(Body(a), x => (string)x.Cells["description"]! == "Entry 5 (reversal)");
        var pend = await Run(run, 1, ExpenseRegisterReport.Id, new Dictionary<string, string?>(Oct) { ["status"] = "Pending" });
        Assert.Single(Body(T(pend)));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(run, 1, ExpenseRegisterReport.Id, new Dictionary<string, string?>(Oct) { ["status"] = "Nope" })));
    }

    [Fact]
    public async Task Expenses_by_head_groups_shares_and_total_match_the_register_counted_total()
    {
        var (st, _) = Ledger(L(1, D1, "Laundry", 400_000), L(2, D1, "Supplies", 300_000, source: "petty"), L(3, D2, "Utilities", 300_000, source: "recurring"), L(4, D2, "Supplies", 50_000, "Pending"),
            L(5, D2, "Assets", 900_000, capital: true));
        var run = Runner(null, new FakeSource(), st);
        var r = await Run(run, 1, ExpensesByHeadReport.Id);
        var a = T(r);
        var tot = a.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(1_000_000L, Cell(tot, "amount")); Assert.Equal(3L, Cell(tot, "entries"));
        Assert.Equal(a.Rows.Where(x => x.Kind == ReportRowKind.Subtotal).Sum(x => Cell(x, "amount")), Cell(tot, "amount"));
        Assert.Equal(40.0, Body(a).Single(x => (string)x.Cells["head"]! == "Laundry").Cells["share"]);
        Assert.Equal(["Operating expenses", "Petty cash", "Recurring bills"], Body(a).Select(x => (string)x.Cells["group"]!).ToArray());
        var reg = await Run(run, 1, ExpenseRegisterReport.Id);
        Assert.Equal(Cell(tot, "amount"), Cell(T(reg).Rows.Single(x => x.Kind == ReportRowKind.Total), "amount"));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(run, 1, ExpensesByHeadReport.Id, new Dictionary<string, string?>(Oct) { ["group"] = "Nope" })));
        Assert.Equal(33.3, MoneyRules.Share(1, 3)); Assert.Equal(66.7, MoneyRules.Share(2, 3));
    }

    [Theory]
    [InlineData(3)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(99)]
    public async Task Reception_housekeeping_maintenance_and_unknown_are_refused_on_every_report(int who)
    {
        var (st, _) = Ledger();
        var run = Runner(null, SourceWithData(), st);
        foreach (var id in new[] { ShiftReport.Id, CollectionDetailReport.Id, CreditBillsReport.Id, ExpenseRegisterReport.Id, ExpensesByHeadReport.Id })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, who, id)));
    }

    [Fact]
    public async Task Accountant_views_and_exports_all_five_and_an_export_deny_keeps_the_view()
    {
        var (st, _) = Ledger(L(1, D1, "Laundry", 400_000));
        var p = new FakePolicy();
        var run = Runner(p, SourceWithData(), st);
        foreach (var id in new[] { ShiftReport.Id, CollectionDetailReport.Id, CreditBillsReport.Id, ExpenseRegisterReport.Id, ExpensesByHeadReport.Id })
        {
            Assert.NotNull(await run.RunAsync(Rig.C(5), id, Oct, new RunOptions(All: true, Export: true), Ct));
            Assert.Contains(await run.CatalogueAsync(Rig.C(5), Ct), e => e.Definition.Id == id && e.CanExport && e.Money);
        }

        p.Denied.Add((5, "PERM-RPT-003"));
        Assert.NotNull(await Run(run, 5, ShiftReport.Id));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => run.RunAsync(Rig.C(5), ShiftReport.Id, Oct, new RunOptions(All: true, Export: true), Ct)));
    }

    [Fact]
    public async Task Finance_gate_denied_refuses_all_five_there_is_nothing_to_blank()
    {
        var (st, _) = Ledger();
        var p = new FakePolicy(); p.Denied.Add((2, "PERM-RPT-002"));
        var run = Runner(p, SourceWithData(), st);
        foreach (var id in new[] { ShiftReport.Id, CollectionDetailReport.Id, CreditBillsReport.Id, ExpensesByHeadReport.Id })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, 2, id)));
    }

    [Fact]
    public async Task Date_class_edges_inverted_and_future_are_refused()
    {
        var (st, _) = Ledger();
        var run = Runner(null, SourceWithData(), st);
        foreach (var id in new[] { ShiftReport.Id, CollectionDetailReport.Id, CreditBillsReport.Id, ExpenseRegisterReport.Id, ExpensesByHeadReport.Id })
        {
            Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(run, 1, id, new Dictionary<string, string?> { ["from"] = "2026-10-05", ["to"] = "2026-10-01" })));
            Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(run, 1, id, new Dictionary<string, string?> { ["from"] = "2026-10-24", ["to"] = "2026-11-02" })));
        }
    }

    [Fact]
    public async Task Three_way_screen_excel_and_pdf_text_agree_for_all_five()
    {
        var (st, _) = Ledger(L(1, D1, "Laundry", 400_000, gst: 20_000, bps: 500), L(2, D2, "Supplies", 300_000, source: "petty"));
        var run = Runner(null, SourceWithData(), st);
        foreach (var id in new[] { ShiftReport.Id, CollectionDetailReport.Id, CreditBillsReport.Id, ExpenseRegisterReport.Id, ExpensesByHeadReport.Id })
            RendererTests.ThreeWay(await Run(run, 1, id));
    }

    [Fact]
    public async Task Csv_neutralises_formula_text_in_the_register()
    {
        var (st, _) = Ledger(L(1, D1, "Laundry", 400_000) with { Description = "=SUM(A1)" });
        var r = await Run(Runner(null, new FakeSource(), st), 1, ExpenseRegisterReport.Id);
        var csv = new CsvRenderer().Render(r, new RenderContext("HPE", "Test Hotel", ReportFormat.Csv));
        var text = System.Text.Encoding.UTF8.GetString(csv.Bytes);
        Assert.Contains("'=SUM(A1)", text, StringComparison.Ordinal);
        Assert.Contains("4000.00", text, StringComparison.Ordinal);
    }

    private static async Task<HccErrorCode?> Code(Func<Task> f)
    {
        try { await f(); return null; }
        catch (HccException e) { return e.Code; }
    }
}
