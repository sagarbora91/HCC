// CU-RPT-A · shared test rig of the four wave-CU reports: real SQL sources on the disposable HccTest_* database, the FakePolicy of the engine tests, and seeds
// (audit events, drawer shifts and movements, closed days, expenses, stays / charges / receipts). Fictional data. Dates around the property's business date 2026-10-24.
using System.Collections.Concurrent;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer.Closing;
using Hcc.Infrastructure.SqlServer.Expenses;
using Hcc.Infrastructure.SqlServer.MoneyMasters;
using Hcc.Infrastructure.SqlServer.Reporting;
using Hcc.Reporting.Reports.CuA;
using Hcc.Reporting.Sql;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.CuA;

internal static class CuARig
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private static readonly ConcurrentDictionary<string, bool> Done = new();

    public static async Task SeedOnce(ReportDatabase db, string key, Func<Task> seed)
    {
        var gate = Gates.GetOrAdd(db.Name + key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(Ct);
        try
        {
            if (Done.ContainsKey(db.Name + key)) return;
            await seed();
            Done[db.Name + key] = true;
        }
        finally { gate.Release(); }
    }

    public static FakePolicy Policy()
    {
        var p = new FakePolicy();
        p.Grants["PERM-ADM-004"] = ["OWN", "MGR", "ACC", "REC"]; // REC is mis-granted on purpose: it may open the log but never reads a detail
        return p;
    }

    public static IReportSelector[] Selectors(ReportDatabase db, FakePolicy p, MemoryStores roles)
    {
        var c = db.Platform.Connections;
        var closing = new SqlClosingStore(c);
        var expenses = new SqlExpenseStore(c);
        return
        [
            new AuditLogReport(db.Platform, p, roles), new DailyOpsReport(new SqlReportSource(c), closing, expenses),
            new CashBookReport(new SqlCashBookSource(c), closing, new SqlMoneyReportSource(c)),
            new ExpensesCashReport(new SqlCashBookSource(c), closing, expenses, new SqlPaymentMethodSource(c)),
        ];
    }

    public static ReportRunner Runner(ReportDatabase db, FakePolicy p) => Rig.Runner(p, Selectors(db, p, new MemoryStores()));

    public static async Task<ReportResult> Run(ReportDatabase db, FakePolicy p, int principal, string id, params (string K, string V)[] raw)
    {
        var r = await Runner(db, p).RunAsync(new Hcc.Application.Billing.Caller(db.PropertyId, principal), id, raw.ToDictionary(x => x.K, x => (string?)x.V), new RunOptions(All: true), Ct);
        RendererTests.ThreeWay(r);
        return r;
    }

    /// <summary>The hub service (catalogue, run, export) over the same selectors: the DEC-061 guard is the service's.</summary>
    public static ReportService Service(ReportDatabase db, FakePolicy p)
    {
        var m = new MemoryStores();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero));
        var prop = new FakeProperty(new DateOnly(2026, 10, 24));
        var cat = new ReportCatalogue(Selectors(db, p, m));
        return new ReportService(new ReportRunner(cat, p, prop, [new FixedCodeEngine()], clock), cat, [new XlsxRenderer(), new PdfRenderer(), new CsvRenderer(), new PrintHtmlRenderer()],
            prop, m, m, m, m, m, p, new ReferenceOnlyFileStore(), new FakeDelivery(), clock, m);
    }

    public static List<ReportRow> Data(ReportResult r) => r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();

    public static long L(ReportRow r, string k) => Convert.ToInt64(r.Cells[k], System.Globalization.CultureInfo.InvariantCulture);

    public static string S(ReportRow r, string k) =>
        r.Cells[k] is DateOnly d ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : Convert.ToString(r.Cells[k], System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>Till TA, three shifts (22 and 23 Oct counted and closed, 24 Oct open), their drawer movements, and the closed days 22 / 23 Oct (22 Oct also locked in the cash book).</summary>
    public static Task SeedCash(ReportDatabase db) => SeedOnce(db, "cash", async () =>
    {
        var (p, csh, mgr) = (db.PropertyId, db.Users["CSH"], db.Users["MGR"]);
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT pay.till(property_id, code, name, created_by, created_utc) VALUES (@p, 'TA', N'Till A', {csh}, '2026-01-01');
            DECLARE @t INT = SCOPE_IDENTITY();
            INSERT pay.shift(till_id, business_date, status, opening_float_paise, opened_by, opened_utc, expected_paise, counted_paise, submitted_by, submitted_utc, closed_by, closed_utc) VALUES
              (@t, '2026-10-22', 'Closed', 1000000, {csh}, '2026-10-22 03:30', 1400000, 1395000, {csh}, '2026-10-22 16:00', {mgr}, '2026-10-22 16:30'),
              (@t, '2026-10-23', 'Closed', 1395000, {csh}, '2026-10-23 03:30', 1245000, 1245000, {csh}, '2026-10-23 16:00', {mgr}, '2026-10-23 16:30'),
              (@t, '2026-10-24', 'Open', 1245000, {csh}, '2026-10-24 03:30', NULL, NULL, NULL, NULL, NULL, NULL);
            DECLARE @s22 BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @t AND business_date = '2026-10-22'),
                    @s23 BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @t AND business_date = '2026-10-23');
            INSERT pay.cash_movement(shift_id, till_id, business_date, kind, source_type, source_id, amount_paise, label, reference, principal_id, recorded_utc) VALUES
              (@s22, @t, '2026-10-22', 'payment', 'cua-test', 1, 500000, N'Payment / advance', N'RC-000001', {csh}, '2026-10-22 06:00'),
              (@s22, @t, '2026-10-22', 'expense', 'cua-test', 2, -100000, N'Expense', N'Repairs', {csh}, '2026-10-22 07:00'),
              (@s23, @t, '2026-10-23', 'payment', 'cua-test', 3, 200000, N'Payment / advance', N'RC-000002', {csh}, '2026-10-23 06:00'),
              (@s23, @t, '2026-10-23', 'refund', 'cua-test', 4, -50000, N'Payment refund', N'RC-000003', {csh}, '2026-10-23 07:00'),
              (@s23, @t, '2026-10-23', 'banking', 'cua-test', 5, -300000, N'Banked after count', N'Bank deposit', {mgr}, '2026-10-23 17:00');
            INSERT cls.close_run(property_id, business_date, run_ref, last_run_ref, status, current_step, attempts, started_by, started_utc, last_attempt_by, last_attempt_utc) VALUES
              (@p, '2026-10-22', 'r22', 'r22', 'Completed', 'done', 1, {mgr}, '2026-10-22 17:00', {mgr}, '2026-10-22 17:00'),
              (@p, '2026-10-23', 'r23', 'r23', 'Completed', 'done', 1, {mgr}, '2026-10-23 17:00', {mgr}, '2026-10-23 17:00');
            INSERT cls.business_day(property_id, business_date, run_id, closed_by, closed_utc)
              SELECT property_id, business_date, run_id, {mgr}, '2026-10-22 17:30' FROM cls.close_run WHERE property_id = @p;
            INSERT cls.cashbook_day(property_id, business_date, locked, locked_by, locked_utc) VALUES (@p, '2026-10-22', 1, {db.Users["ACC"]}, '2026-10-23 09:00');
            SELECT 1;
            """);
    });

    /// <summary>Expenses: E1 cash 1,000 (22 Oct); E4 cash 300 (22 Oct) reversed on 23 Oct by a -300 record; E2 UPI 4,500 and E3 vendor credit 10,100 (23 Oct).</summary>
    public static Task SeedExpenses(ReportDatabase db) => SeedOnce(db, "exp", async () =>
    {
        var (p, rec, mgr) = (db.PropertyId, db.Users["REC"], db.Users["MGR"]);
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc, decided_by, decided_utc) VALUES
              (@p, N'Vendor 1', N'Repairs', 100000, N'Cash', '2026-10-22', 'Posted', {rec}, '2026-10-22 05:00', {mgr}, '2026-10-22 06:00'),
              (@p, N'Vendor 2', N'Utilities', 450000, N'UPI', '2026-10-23', 'Posted', {rec}, '2026-10-23 05:00', {mgr}, '2026-10-23 06:00'),
              (@p, N'Vendor 3', N'Linen', 1010000, N'Vendor credit', '2026-10-23', 'Posted', {rec}, '2026-10-23 05:00', {mgr}, '2026-10-23 06:00'),
              (@p, N'Vendor 4', N'Repairs', 30000, N'Cash', '2026-10-22', 'Posted', {rec}, '2026-10-22 05:00', {mgr}, '2026-10-22 06:00');
            DECLARE @e4 BIGINT = (SELECT expense_id FROM ops.expense WHERE property_id = @p AND vendor = N'Vendor 4');
            INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc, decided_by, decided_utc, reversal_of)
              VALUES (@p, N'Vendor 4', N'Repairs', -30000, N'Cash', '2026-10-23', 'Posted', {mgr}, '2026-10-23 05:00', {mgr}, '2026-10-23 06:00', @e4);
            UPDATE ops.expense SET status = 'Reversed', reversed_by = SCOPE_IDENTITY() WHERE expense_id = @e4;
            SELECT 1;
            """);
    });
}
