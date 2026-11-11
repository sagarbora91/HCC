// FX-RPT-C · rpt.money.expense-month on a disposable HccTest_* database: the month's counted expenses by head (Posted and Reversed originals plus reversal records; rejected and other
// months left out), entries per head, GST paid and ITC allowed / disallowed on the total row, to the paise; empty month; PERM-RPT-002; housekeeping refused at the hub (DEC-061).
using Hcc.Reporting.Tests.CuB;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Expenses;
using Hcc.Reporting.Reports.FxC;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.FxC;

[Trait("Category", "Sql")]
public sealed class ExpenseMonthReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    // October: Repairs 1,000.00 (GST 180.00, ITC allowed) + 2,000.00 (no GST); Utilities 4,500.00 (GST 810.00, ITC disallowed); Linen 300.00 posted then reversed by -300.00.
    // Left out: a rejected Repairs 777.00 in October, Repairs 9,999.00 on 30 Sep.
    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            var (p, rec, mgr) = (db.PropertyId, db.Users["REC"], db.Users["MGR"]);
            await db.ScalarAsync($"""
                DECLARE @p INT = {p};
                INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc, decided_by, decided_utc, gst_paise, itc_allowed) VALUES
                  (@p, N'V1', N'Repairs', 100000, N'Cash', '2026-10-10', 'Posted', {rec}, '2026-10-10 05:00', {mgr}, '2026-10-10 06:00', 18000, 1),
                  (@p, N'V2', N'Repairs', 200000, N'UPI', '2026-10-11', 'Posted', {rec}, '2026-10-11 05:00', {mgr}, '2026-10-11 06:00', NULL, 0),
                  (@p, N'V3', N'Utilities', 450000, N'UPI', '2026-10-12', 'Posted', {rec}, '2026-10-12 05:00', {mgr}, '2026-10-12 06:00', 81000, 0),
                  (@p, N'V4', N'Repairs', 77700, N'Cash', '2026-10-13', 'Rejected', {rec}, '2026-10-13 05:00', {mgr}, '2026-10-13 06:00', NULL, 0),
                  (@p, N'V5', N'Repairs', 999900, N'Cash', '2026-09-30', 'Posted', {rec}, '2026-09-30 05:00', {mgr}, '2026-09-30 06:00', NULL, 0),
                  (@p, N'V6', N'Linen', 30000, N'Cash', '2026-10-14', 'Posted', {rec}, '2026-10-14 05:00', {mgr}, '2026-10-14 06:00', NULL, 0);
                DECLARE @e6 BIGINT = (SELECT expense_id FROM ops.expense WHERE property_id = @p AND vendor = N'V6');
                INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc, decided_by, decided_utc, reversal_of)
                  VALUES (@p, N'V6', N'Linen', -30000, N'Cash', '2026-10-15', 'Posted', {mgr}, '2026-10-15 05:00', {mgr}, '2026-10-15 06:00', @e6);
                UPDATE ops.expense SET status = 'Reversed', reversed_by = SCOPE_IDENTITY() WHERE expense_id = @e6;
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Hcc.Reporting.ReportRunner Runner() => CuBRig.Runner(db, new ExpenseMonthReport(new SqlExpenseStore(db.Platform.Connections)));

    [Fact]
    public async Task Heads_total_gst_and_itc_are_exact_to_the_paise()
    {
        var r = await Run(db, Runner(), "OWN", ExpenseMonthReport.Id, ("month", "2026-10"));
        var heads = r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        Assert.Equal([("Linen", 0L, 2L), ("Repairs", 300000L, 2L), ("Utilities", 450000L, 1L)], heads.Select(x => (S(x, "head"), L(x, "amount"), L(x, "entries"))));
        Assert.All(heads, x => Assert.Null(x.Cells["gstPaid"]));
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("Total", 750000L, 5L, 99000L, 18000L, 81000L), (S(total, "head"), L(total, "amount"), L(total, "entries"), L(total, "gstPaid"), L(total, "itcAllowed"), L(total, "itcDisallowed")));
        Assert.Equal(heads.Sum(x => L(x, "amount")), L(total, "amount"));
        Assert.Equal(L(total, "gstPaid"), L(total, "itcAllowed") + L(total, "itcDisallowed"));
    }

    [Fact]
    public async Task Another_month_is_separate_and_an_empty_month_is_a_zero_total()
    {
        var sep = await Run(db, Runner(), "ACC", ExpenseMonthReport.Id, ("month", "2026-09"));
        Assert.Equal(("Repairs", 999900L), (S(sep.Rows[0], "head"), L(sep.Rows[0], "amount")));
        var none = await Run(db, Runner(), "MGR", ExpenseMonthReport.Id, ("month", "2026-08"));
        var only = Assert.Single(none.Rows);
        Assert.Equal((ReportRowKind.Total, 0L, 0L, 0L), (only.Kind, L(only, "amount"), L(only, "entries"), L(only, "gstPaid")));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, Runner(), "OWN", ExpenseMonthReport.Id, ("month", "2026-13"))));
    }

    [Fact]
    public async Task Owner_manager_accountant_may_run_it_everyone_else_is_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, ExpenseMonthReport.Id, ("month", "2026-10"))));
        foreach (var role in new[] { "OWN", "MGR", "ACC" })
            Assert.Null(await Code(() => Run(db, runner, role, ExpenseMonthReport.Id, ("month", "2026-10"))));
        var svc = Service(db, new ExpenseMonthReport(new SqlExpenseStore(db.Platform.Connections)));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => svc.RunAsync(new Caller(db.PropertyId, db.Users["HKP"]), ExpenseMonthReport.Id, new Dictionary<string, string?>(), null, 50, true, Ct)));
    }
}
