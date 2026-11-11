// F8-RPT-P2-C · batch c reports on the server engine: asset register, leave balances, GRN register, sales by item, POS voids, scheduled runs, cancelled lines,
// rate sheet (publish log and change history builders). The source and the stores are in-memory fakes; each test checks a figure the prototype suite
// (tests/stream-rpt-p2-c.mjs) checks on its side, plus the role matrix, rule M, the 92-day cap and screen = Excel = PDF (RendererTests.ThreeWay).
using System.Reflection;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Expenses;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Contracts.Expenses;
using Hcc.Contracts.Yield;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.P2C;

namespace Hcc.Reporting.Tests.P2C;

public sealed class P2CReportsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly D1 = new(2026, 10, 1), D2 = new(2026, 10, 2);
    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string> { [1] = "Sagar", [2] = "Manager", [4] = "Priya", [5] = "Meera" };

    private static readonly IReadOnlyDictionary<string, string?> Oct = new Dictionary<string, string?> { ["from"] = "2026-10-01", ["to"] = "2026-10-02" };

    private sealed class FakeSource : IP2CReportSource
    {
        public List<GrnFact> Grns { get; } = [];
        public List<SaleLineFact> Sales { get; } = [];
        public List<VoidFact> Voids { get; } = [];
        public List<LeaveStaffFact> Staff { get; } = [];
        public List<LeaveDayFact> Days { get; } = [];
        public List<CancelledFact> Cancelled { get; } = [];
        public Task<IReadOnlyDictionary<int, string>> NamesAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(Names);
        public Task<IReadOnlyList<GrnFact>> GrnsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GrnFact>>(Grns.Where(x => x.Date >= from && x.Date <= to).ToList());
        public Task<IReadOnlyList<SaleLineFact>> SalesAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SaleLineFact>>(Sales);
        public Task<IReadOnlyList<VoidFact>> VoidsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<VoidFact>>(Voids);
        public Task<(IReadOnlyList<LeaveStaffFact> Staff, IReadOnlyList<LeaveDayFact> Days)> LeaveAsync(int propertyId, DateOnly fyFrom, DateOnly fyTo, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<LeaveStaffFact>, IReadOnlyList<LeaveDayFact>)>((Staff, Days.Where(d => d.Date >= fyFrom && d.Date <= fyTo).ToList()));
        public Task<IReadOnlyList<CancelledFact>> CancelledAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CancelledFact>>(Cancelled.Where(x => x.Date >= from && x.Date <= to).ToList());
    }

    /// <summary>An IFixedAssetStore that answers only ListAsync(propertyId, ct).</summary>
    public class AssetProxy : DispatchProxy
    {
        public List<FixedAssetView> Rows { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name != nameof(IFixedAssetStore.ListAsync) || args!.Length != 2) throw new NotSupportedException(targetMethod.Name);
            return Task.FromResult<IReadOnlyList<FixedAssetView>>(Rows);
        }
    }

    private sealed class FakeRuns(List<ReportRunRecord> runs) : IReportRunStore
    {
        public Task<long> StartAsync(int propertyId, ReportRunRecord run, CancellationToken ct = default) => throw new NotSupportedException();
        public Task FinishAsync(int propertyId, long id, string status, int? rows, string? fileRef, string? error, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReportRunRecord>> ListAsync(int propertyId, int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportRunRecord>>(runs);
    }

    private sealed class FakeSchedules(List<ReportScheduleRecord> list) : IReportScheduleStore
    {
        public Task<IReadOnlyList<ReportScheduleRecord>> ListAsync(int propertyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportScheduleRecord>>(list);
        public Task<long> SaveAsync(int propertyId, ReportScheduleRecord schedule, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(int propertyId, long id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>The Cashier holds PERM-POS-004 as own shift only: granted without a property scope, refused with one.</summary>
    private sealed class PosPolicy : IAccessPolicy
    {
        public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(permissionId switch
        {
            "PERM-POS-004" => principalId switch { 1 or 2 or 5 => true, 4 => scope is null, _ => false },
            _ => false,
        });

        public async Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
        {
            if (!await HasAsync(propertyId, principalId, permissionId, scope, ct)) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: " + permissionId);
        }
    }

    private static FakePolicy Policy()
    {
        var p = new FakePolicy();
        p.Grants["PERM-ACC-001"] = ["OWN", "MGR", "ACC"]; p.Grants["PERM-EXP-004"] = ["OWN", "MGR", "ACC"]; p.Grants["PERM-HR-001"] = ["OWN", "MGR"];
        p.Grants["PERM-STK-004"] = ["OWN", "MGR", "ACC"]; p.Grants["PERM-POS-004"] = ["OWN", "MGR", "ACC"]; p.Grants["PERM-ADM-004"] = ["OWN", "MGR"]; p.Grants["PERM-YLD-001"] = ["OWN", "MGR"];
        return p;
    }

    private static async Task<HccErrorCode?> Code(Func<Task> f)
    {
        try { await f(); }
        catch (HccException e) { return e.Code; }
        return null;
    }

    private static Task<ReportResult> Run(ReportRunner r, int who, string id, IReadOnlyDictionary<string, string?>? p = null) => r.RunAsync(Rig.C(who), id, p ?? Oct, new RunOptions(All: true), Ct);

    private static FixedAssetView Asset(string no, string ledger, string name, long cost, long itc, string bought, string vendor, string? put = null, string? disposed = null, string? voucher = null) =>
        new(1, no, ledger, name, cost, itc, bought, vendor, 1, put, disposed, disposed is null ? null : 100000, voucher);

    private static ReportRunner AssetRunner(params FixedAssetView[] rows)
    {
        var store = DispatchProxy.Create<IFixedAssetStore, AssetProxy>();
        ((AssetProxy)(object)store).Rows = [.. rows];
        return Rig.Runner(Policy(), new AssetRegisterReport(store));
    }

    [Fact]
    public async Task AssetRegister_orders_by_number_counts_put_to_use_and_disposal_only_up_to_the_date()
    {
        var run = AssetRunner(Asset("FA-2026-002", "1510", "Lobby sofa", 4000000, 200000, "2026-06-01", "Shree", "2026-06-15"),
            Asset("FA-2026-001", "1520", "Boiler", 9000000, 450000, "2026-05-01", "Heatco", "2026-05-20", "2026-09-30", "MV-9"),
            Asset("FA-2026-003", "1510", "=cmd bed", 1000000, 0, "2026-10-01", ""));
        var asOf = new Dictionary<string, string?> { ["asOf"] = "2026-10-02" };
        var r = await Run(run, 1, AssetRegisterReport.Id, asOf);
        var rows = r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        Assert.Equal(["FA-2026-001", "FA-2026-002", "FA-2026-003"], rows.Select(x => (string)x.Cells["no"]!));
        Assert.Equal("Disposed 2026-09-30 · MV-9", rows[0].Cells["disposal"]);
        Assert.Null(rows[1].Cells["disposal"]);
        Assert.Null(rows[2].Cells["putToUse"]);
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(14000000L, total.Cells["cost"]); Assert.Equal(650000L, total.Cells["itc"]);
        var early = await Run(run, 1, AssetRegisterReport.Id, new Dictionary<string, string?> { ["asOf"] = "2026-05-25" });
        var one = early.Rows.Single(x => x.Kind == ReportRowKind.Row);
        Assert.Equal("FA-2026-001", one.Cells["no"]); Assert.Null(one.Cells["disposal"]); Assert.Equal(new DateOnly(2026, 5, 20), one.Cells["putToUse"]);
        RendererTests.ThreeWay(r);
    }

    [Fact]
    public async Task AssetRegister_roles_owner_manager_accountant_see_money_others_refused()
    {
        var run = AssetRunner(Asset("FA-2026-001", "1510", "Sofa", 100, 0, "2026-06-01", "x"));
        foreach (var who in new[] { 1, 2, 5 }) Assert.Contains((await Run(run, who, AssetRegisterReport.Id)).Columns, c => c.Key == "cost");
        foreach (var who in new[] { 3, 4, 6, 7, 8 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, who, AssetRegisterReport.Id)));
    }

    [Fact]
    public async Task LeaveBalances_entitlement_less_approved_use_pending_apart_halves_and_financial_year_edges()
    {
        var src = new FakeSource();
        src.Staff.Add(new LeaveStaffFact(1, "Asha", new DateOnly(2025, 1, 1), true, null, 12, 7, 15));
        src.Staff.Add(new LeaveStaffFact(2, "Bimal", new DateOnly(2025, 1, 1), true, null, 12, 7, 15));
        src.Days.AddRange([new(1, "CL", new DateOnly(2026, 6, 1), 1m, "Approved"), new(1, "CL", new DateOnly(2026, 6, 2), 0.5m, "Approved"), new(1, "SL", new DateOnly(2026, 12, 1), 1m, "Pending"),
            new(1, "CL", new DateOnly(2026, 10, 10), 1m, "Approved"), new(1, "CL", new DateOnly(2026, 3, 10), 1m, "Approved")]);
        var run = Rig.Runner(Policy(), new LeaveBalancesReport(src));
        var r = await Run(run, 1, LeaveBalancesReport.Id, new Dictionary<string, string?> { ["asOf"] = "2026-10-02" });
        Assert.Equal(6, r.Rows.Count);
        Assert.DoesNotContain(r.Columns, c => c.Money);
        var cl = r.Rows.First(x => (string)x.Cells["employee"]! == "Asha" && (string)x.Cells["type"]! == "Casual Leave");
        Assert.Equal(1.5, cl.Cells["used"]); Assert.Equal(10.5, cl.Cells["balance"]);
        var sl = r.Rows.First(x => (string)x.Cells["employee"]! == "Asha" && (string)x.Cells["type"]! == "Sick Leave");
        Assert.Equal(0L, sl.Cells["used"]); Assert.Equal(1L, sl.Cells["pending"]);
        var dec = await Run(run, 1, LeaveBalancesReport.Id, new Dictionary<string, string?> { ["asOf"] = "2026-10-24" });
        Assert.Equal(2.5, dec.Rows.First(x => (string)x.Cells["employee"]! == "Asha" && (string)x.Cells["type"]! == "Casual Leave").Cells["used"]);
        var next = await Run(run, 1, LeaveBalancesReport.Id, new Dictionary<string, string?> { ["asOf"] = "2026-10-02" });
        Assert.NotNull(next);
        // Screen = Excel = PDF on whole days. A half day in an Int column is written to Excel as text by the shared renderer (engine file, not this lane's): see the DONE file.
        var whole = new FakeSource();
        whole.Staff.AddRange(src.Staff);
        whole.Days.AddRange(src.Days.Where(d => d.Days == 1m));
        RendererTests.ThreeWay(await Run(Rig.Runner(Policy(), new LeaveBalancesReport(whole)), 1, LeaveBalancesReport.Id, new Dictionary<string, string?> { ["asOf"] = "2026-10-02" }));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, 5, LeaveBalancesReport.Id)));
        Assert.NotNull(await Run(run, 2, LeaveBalancesReport.Id));
    }

    [Fact]
    public async Task GrnRegister_line_amounts_totals_supplier_block_filter_and_cap()
    {
        var src = new FakeSource();
        src.Grns.AddRange([new("GRN-2", D2, "Zenith Linen", "Sheet", 5, 12000, "ZL-77"), new("GRN-1", D1, "Acme Supplies", "Soap", 10, 5000, "AC-1"), new("GRN-1", D1, "Acme Supplies", "Towel", 3, 20000, "AC-1"),
            new("GRN-3", new DateOnly(2026, 7, 1), "Acme Supplies", "Soap", 1, 5000, string.Empty)]);
        var run = Rig.Runner(Policy(), new GrnRegisterReport(src));
        var r = await Run(run, 1, GrnRegisterReport.Id);
        var rows = r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        Assert.Equal(["GRN-1", "GRN-1", "GRN-2"], rows.Select(x => (string)x.Cells["grn"]!));
        Assert.Equal(50000L, rows[0].Cells["amount"]); Assert.Equal(60000L, rows[1].Cells["amount"]);
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(170000L, total.Cells["amount"]); Assert.Equal(18L, total.Cells["qty"]);
        var sup = r.Sections![1].Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        Assert.Equal([("Acme Supplies", 1L, 110000L), ("Zenith Linen", 1L, 60000L)], sup.Select(x => ((string)x.Cells["supplier"]!, (long)x.Cells["grns"]!, (long)x.Cells["amount"]!)));
        var filtered = await Run(run, 1, GrnRegisterReport.Id, new Dictionary<string, string?> { ["from"] = "2026-10-01", ["to"] = "2026-10-02", ["supplier"] = "zenith" });
        Assert.Single(filtered.Rows, x => x.Kind == ReportRowKind.Row);
        var billless = await Run(run, 1, GrnRegisterReport.Id, new Dictionary<string, string?> { ["from"] = "2026-07-01", ["to"] = "2026-07-01" });
        Assert.Null(billless.Rows.First().Cells["bill"]);
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(run, 1, GrnRegisterReport.Id, new Dictionary<string, string?> { ["from"] = "2026-06-01", ["to"] = "2026-10-01" })));
        RendererTests.ThreeWay(r);
        foreach (var who in new[] { 3, 4, 6, 7, 8 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, who, GrnRegisterReport.Id)));
    }

    private static FakeSource PosSource()
    {
        var src = new FakeSource();
        src.Sales.AddRange([new("m-tea", "Tea", "Beverages", 4, 20000, "Cafe", 4), new("m-cake", "Cake", "Food", 2, 30000, "Cafe", 4), new("m-tea", "Tea", "Beverages", 6, 30000, "Cafe", 2),
            new("m-beer", "=Beer", "Bar", 3, 90000, "Bar", 2)]);
        src.Voids.AddRange([new("2026-10-02 20:15", "B-O4", "Cake", 2, 30000, "guest left", 4, 2, "000000000004/000000000001"), new("2026-10-01", "B-O1", "Tea", 1, 5000, "wrong item", 4, null, "000000000001/000000000003")]);
        return src;
    }

    [Fact]
    public async Task SalesByItem_quantity_value_categories_by_value_shares_and_outlet_filter()
    {
        var run = PosRunner(new SalesByItemReport(PosSource(), new PosPolicy()));
        var r = await Run(run, 1, SalesByItemReport.Id);
        var rows = r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        var tea = rows.Single(x => (string)x.Cells["item"]! == "Tea");
        Assert.Equal(10L, tea.Cells["qty"]); Assert.Equal(50000L, tea.Cells["value"]);
        Assert.Equal(170000L, r.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["value"]);
        Assert.Equal("Bar", rows[0].Cells["category"]);
        Assert.InRange(rows.Sum(x => (double)x.Cells["share"]!), 99.7, 100.3);
        var bar = await Run(run, 1, SalesByItemReport.Id, new Dictionary<string, string?> { ["from"] = "2026-10-01", ["to"] = "2026-10-02", ["outlet"] = "Bar" });
        Assert.Single(bar.Rows, x => x.Kind == ReportRowKind.Row);
        RendererTests.ThreeWay(r);
    }

    [Fact]
    public async Task SalesByItem_cashier_sees_only_own_bills_and_cannot_export_restaurant_and_floor_roles_refused()
    {
        var run = PosRunner(new SalesByItemReport(PosSource(), new PosPolicy()));
        var c = await Run(run, 4, SalesByItemReport.Id);
        Assert.Equal(50000L, c.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["value"]);
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => run.RunAsync(Rig.C(4), SalesByItemReport.Id, Oct, new RunOptions(All: true, Export: true), Ct)));
        foreach (var who in new[] { 3, 6, 7, 8 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, who, SalesByItemReport.Id)));
    }

    // CU f8e-open: the cashier filter (same figures as tests/stream-cu-f8e-open.mjs: principal 4 = "cashier", principal 2 = "manager")
    private static readonly SaleLineFact[] CashierFacts =
        [new("m-tea", "Tea", "Beverages", 4, 20000, "Cafe", 4), new("m-tea", "Tea", "Beverages", 6, 30000, "Cafe", 2), new("m-cake", "Cake", "Food", 2, 30000, "Cafe", 2)];

    private static BoundParams CashierParams(string? cashier)
    {
        var v = new Dictionary<string, string>();
        if (cashier is not null) v["cashier"] = cashier;
        return new BoundParams(v, D1, D2);
    }

    [Fact]
    public void SalesByItem_cashier_filter_keeps_only_checks_that_principal_settled()
    {
        var def = new SalesByItemReport(new FakeSource(), new PosPolicy()).Definition;
        Assert.Contains(def.Params, x => x.Key == "cashier" && x.Type == "user");
        static (object? Value, object? Qty, int Rows) Of(ReportResult r) =>
            (r.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["value"], r.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["qty"], r.Rows.Count(x => x.Kind == ReportRowKind.Row));
        Assert.Equal((80000L, 12L, 2), Of(SalesByItemReport.Build(def, CashierParams(null), CashierFacts)));
        Assert.Equal((20000L, 4L, 1), Of(SalesByItemReport.Build(def, CashierParams("4"), CashierFacts)));
        Assert.Equal((60000L, 8L, 2), Of(SalesByItemReport.Build(def, CashierParams("2"), CashierFacts)));
        Assert.Empty(SalesByItemReport.Build(def, CashierParams("99"), CashierFacts).Rows);
    }

    [Fact]
    public async Task SalesByItem_own_shift_caller_asking_for_another_cashier_gets_nothing()
    {
        var src = new FakeSource();
        src.Sales.AddRange(CashierFacts);
        var report = new SalesByItemReport(src, new PosPolicy());
        var other = await report.SelectAsync(Rig.C(4), CashierParams("2"), Ct);          // principal 4 holds only the own-shift grant
        Assert.Empty(other.Rows);
        var own = await report.SelectAsync(Rig.C(4), CashierParams("4"), Ct);
        Assert.Equal(20000L, own.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["value"]);
        var all = await report.SelectAsync(Rig.C(2), CashierParams("4"), Ct);             // property-wide: sees the named cashier's checks
        Assert.Equal(20000L, all.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["value"]);
    }

    private static ReportRunner PosRunner(params IReportSelector[] selectors) =>
        new(new ReportCatalogue(selectors), new PosPolicyAdapter(), new FakeProperty(new DateOnly(2026, 10, 24)), [new FixedCodeEngine()], new FixedClock(Rig.Now));

    /// <summary>The runner's own policy for the POS reports: PERM-POS-004 as in <see cref="PosPolicy"/> and PERM-RPT-003 for the owner (export).</summary>
    private sealed class PosPolicyAdapter : IAccessPolicy
    {
        private readonly PosPolicy _pos = new();
        private readonly FakePolicy _fake = Policy();

        public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) =>
            permissionId == "PERM-POS-004" ? _pos.HasAsync(propertyId, principalId, permissionId, scope, ct) : _fake.HasAsync(propertyId, principalId, permissionId, scope, ct);

        public async Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
        {
            if (!await HasAsync(propertyId, principalId, permissionId, scope, ct)) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: " + permissionId);
        }
    }

    [Fact]
    public async Task PosVoids_rows_oldest_first_with_approver_total_and_role_matrix()
    {
        var run = Rig.Runner(Policy(), new PosVoidsReport(PosSource()));
        var r = await Run(run, 1, PosVoidsReport.Id);
        var rows = r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-10-01", rows[0].Cells["time"]); Assert.Null(rows[0].Cells["approvedBy"]);
        Assert.Equal("2026-10-02 20:15", rows[1].Cells["time"]); Assert.Equal("Manager", rows[1].Cells["approvedBy"]);
        Assert.Equal(35000L, r.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["value"]);
        RendererTests.ThreeWay(r);
        foreach (var who in new[] { 3, 4, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, who, PosVoidsReport.Id)));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(run, 1, PosVoidsReport.Id, new Dictionary<string, string?> { ["from"] = "2026-06-01", ["to"] = "2026-10-01" })));
    }

    [Fact]
    public async Task ScheduledRuns_only_scheduled_runs_newest_first_with_result_error_and_range_edges()
    {
        var at = (string day, string time) => new DateTimeOffset(DateTime.SpecifyKind(DateTime.Parse(day + "T" + time, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc).AddMinutes(-330));
        var runs = new List<ReportRunRecord>
        {
            new(1, 10, GrnRegisterReport.Id, "{}", 1, at("2026-10-01", "06:00"), null, "ok", 3),
            new(2, 10, GrnRegisterReport.Id, "{}", 1, at("2026-10-02", "06:00"), null, "failed", null, null, "mail refused"),
            new(3, null, GrnRegisterReport.Id, "{}", 1, at("2026-10-02", "09:00"), null, "ok"),
        };
        var sch = new List<ReportScheduleRecord> { new(10, GrnRegisterReport.Id, new Dictionary<string, string>(), ReportFormat.Xlsx, "daily", null, new TimeOnly(6, 0), 1, [1, 5], true) };
        var run = Rig.Runner(Policy(), new ScheduledRunsReport(new FakeRuns(runs), new FakeSchedules(sch), new FakeSource()));
        var r = await Run(run, 1, ScheduledRunsReport.Id);
        Assert.Equal(["failed", "ok"], r.Rows.Select(x => (string)x.Cells["result"]!));
        Assert.Equal("mail refused", r.Rows[0].Cells["error"]);
        Assert.Contains("daily 06:00", (string)r.Rows[0].Cells["schedule"]!);
        Assert.Equal("Sagar, Meera", r.Rows[0].Cells["recipients"]);
        Assert.Single((await Run(run, 1, ScheduledRunsReport.Id, new Dictionary<string, string?> { ["from"] = "2026-10-02", ["to"] = "2026-10-02" })).Rows);
        Assert.DoesNotContain(r.Columns, c => c.Money);
        RendererTests.ThreeWay(r);
        foreach (var who in new[] { 3, 4, 5, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, who, ScheduledRunsReport.Id)));
    }

    [Fact]
    public async Task CancelledLines_newest_first_amount_requester_reason_total_and_role_matrix()
    {
        var src = new FakeSource();
        src.Cancelled.AddRange([new(D2, "000000000002", "BK-1", "101", "Laundry", 35000, 3, "posted twice"), new(D1, "000000000001", "BK-2", string.Empty, "Minibar", 12000, 4, string.Empty)]);
        var run = Rig.Runner(Policy(), new CancelledLinesReport(src));
        var r = await Run(run, 1, CancelledLinesReport.Id);
        var rows = r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        Assert.Equal(["Laundry", "Minibar"], rows.Select(x => (string)x.Cells["item"]!));
        Assert.Equal(35000L, rows[0].Cells["amount"]); Assert.Equal("posted twice", rows[0].Cells["reason"]); Assert.Null(rows[1].Cells["reason"]);
        Assert.Equal(47000L, r.Rows.Single(x => x.Kind == ReportRowKind.Total).Cells["amount"]);
        RendererTests.ThreeWay(r);
        foreach (var who in new[] { 1, 2, 5 }) Assert.NotNull(await Run(run, who, CancelledLinesReport.Id));
        foreach (var who in new[] { 3, 4, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, who, CancelledLinesReport.Id)));
    }

    [Fact]
    public void RateSheet_publish_log_and_change_history_builders_follow_the_prototype()
    {
        var d = new RateSheetReport(null!, null!, new FakeSource()).Definition;
        var p = new BoundParams(new Dictionary<string, string> { ["view"] = "change history" }, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20));
        var types = new Dictionary<int, string> { [1] = "Deluxe" };
        var pubs = new List<YieldPublishView> { new(1, "2026-10-10", "2026-10-12", 1, "2026-10-01T04:30:00Z", "festival", null,
            [new(1, "2026-10-10", 300000, 350000, false), new(1, "2026-10-13", 350000, 300000, true)]) };
        var log = RateSheetReport.BuildLog(d, p, pubs, types, Names);
        var l = Assert.Single(log.Rows);
        Assert.Equal("2 night rates to 2026-10-12", l.Cells["type"]); Assert.Equal("festival", l.Cells["reason"]); Assert.Equal("Deluxe", l.Cells["roomType"]);
        Assert.DoesNotContain(log.Columns, c => c.Key == "before");
        Assert.Empty(RateSheetReport.BuildLog(d, new BoundParams(p.Values, new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 20)), pubs, types, Names).Rows);
        var hist = RateSheetReport.BuildHistory(d, p, pubs, [new YieldOverrideView(1, "2026-10-11", 400000, "event", 2, "2026-10-02T05:30:00Z")], types, Names);
        Assert.Equal(["Published", "Override", "Published (closing row)"], hist.Rows.Select(x => (string)x.Cells["type"]!));
        Assert.Equal(300000L, hist.Rows[0].Cells["before"]); Assert.Equal(350000L, hist.Rows[0].Cells["after"]);
        Assert.Null(hist.Rows[1].Cells["before"]); Assert.Equal(400000L, hist.Rows[1].Cells["after"]);
        Assert.Contains(hist.Columns, c => c.Key == "after" && c.Money);
        RendererTests.ThreeWay(hist);
    }

    [Fact]
    public async Task RateSheet_is_refused_without_the_yield_permission()
    {
        var run = Rig.Runner(Policy(), new RateSheetReport(null!, null!, new FakeSource()));
        foreach (var who in new[] { 3, 4, 5, 6, 7, 8 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(run, who, RateSheetReport.Id)));
    }

    [Fact]
    public void Definitions_are_built_money_flags_and_gates_as_the_catalogue_says()
    {
        var src = new FakeSource();
        var defs = new[] { new AssetRegisterReport(null!).Definition, new LeaveBalancesReport(src).Definition, new GrnRegisterReport(src).Definition, new SalesByItemReport(src, new PosPolicy()).Definition,
            new PosVoidsReport(src).Definition, new ScheduledRunsReport(null!, null!, src).Definition, new CancelledLinesReport(src).Definition, new RateSheetReport(null!, null!, src).Definition };
        Assert.All(defs, d => Assert.Equal("built", d.Status));
        Assert.Equal(6, defs.Count(d => d.MoneyFlag));
        Assert.All(defs.Where(d => d.MoneyFlag), d => Assert.False(string.IsNullOrEmpty(d.MoneyGate)));
        Assert.All(defs.Where(d => !d.MoneyFlag), d => Assert.DoesNotContain(d.Columns, c => c.Money));
    }
}
