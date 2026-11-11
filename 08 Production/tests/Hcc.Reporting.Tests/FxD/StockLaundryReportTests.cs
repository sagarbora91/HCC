// FX-RPT-D · rpt.stock.laundry on a disposable HccTest_* database: batches newest first, pieces / received / damaged / missing (blank while out), laundry charge to the paise and
// removed without PERM-STK-004, rule V / M through the real access policy, DEC-061 at the hub, screen = Excel = PDF = print.
using System.Globalization;
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.FxD;
using Hcc.Reporting.Tests.CuB;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;
using static Hcc.Reporting.Tests.FxD.FxDRig;

namespace Hcc.Reporting.Tests.FxD;

[Trait("Category", "Sql")]
public sealed class StockLaundryReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await AddSupervisor(db);
            await db.ScalarAsync(ItemsSql(db) + $"""
                DECLARE @o INT = {db.Users["OWN"]};
                INSERT stk.laundry_batch(property_id, vendor, sent_date, sent_utc, status, returned_date, actor_id, received_by, charge_paise) VALUES
                  (@p, N'Clean Co', '2026-10-20', '2026-10-20T04:00:00', 'Returned', '2026-10-22', @o, @o, 123456);
                INSERT stk.laundry_batch(property_id, vendor, sent_date, sent_utc, status, actor_id) VALUES (@p, N'Clean Co', '2026-10-22', '2026-10-22T04:00:00', 'Out', @o);
                INSERT stk.laundry_batch(property_id, vendor, sent_date, sent_utc, status, returned_date, actor_id, received_by, charge_paise)
                  VALUES (@p, N'Quick Wash', '2026-10-22', '2026-10-22T05:00:00', 'Returned short', '2026-10-24', @o, @o, 7000);
                DECLARE @b1 BIGINT = (SELECT MIN(batch_id) FROM stk.laundry_batch), @b2 BIGINT = (SELECT MIN(batch_id) + 1 FROM stk.laundry_batch), @b3 BIGINT = (SELECT MAX(batch_id) FROM stk.laundry_batch);
                INSERT stk.laundry_line(batch_id, item_id, sent, received, damaged) VALUES
                  (@b1, {Item("L-BED")}, 30, 26, 2), (@b1, {Item("L-TOW")}, 20, 20, 0), (@b2, {Item("L-BED")}, 10, NULL, NULL), (@b3, {Item("L-TOW")}, 8, 5, 1);
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReportRunner Runner() => CuBRig.Runner(db, new StockLaundryReport(Store(db)));

    private async Task<string> BatchRef(string vendor, string sent) =>
        "LB-" + Convert.ToInt64(await db.ScalarAsync($"SELECT batch_id FROM stk.laundry_batch WHERE vendor = N'{vendor}' AND sent_date = '{sent}';"), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    // B1 Clean Co sent 20 Oct, returned: Bedsheet 30 sent / 26 back / 2 damaged (missing 2), Towel 20 / 20 / 0 -> pieces 50, received 46, damaged 2, missing 2, charge 1,234.56.
    // B2 Clean Co sent 22 Oct, still out: 10 Bedsheets, nothing back. B3 Quick Wash sent 22 Oct (later id), returned short: Towel 8 / 5 / 1 -> missing 2, charge 70.00.
    [Fact]
    public async Task Batches_come_newest_sent_first_with_blank_return_figures_while_out_and_the_exact_charge()
    {
        var r = await Run(db, Runner(), "OWN", StockLaundryReport.Id);
        var d = Data(r);
        Assert.Equal([await BatchRef("Quick Wash", "2026-10-22"), await BatchRef("Clean Co", "2026-10-22"), await BatchRef("Clean Co", "2026-10-20")], d.Select(x => S(x, "batch")));
        Assert.Equal(("Quick Wash", "2026-10-22", "Returned short", 8L, 5L, 1L, 2L, 7000L), (S(d[0], "vendor"), S(d[0], "sent"), S(d[0], "status"), L(d[0], "pieces"), L(d[0], "received"), L(d[0], "damaged"), L(d[0], "missing"), L(d[0], "charge")));
        Assert.Equal(("Out", 10L), (S(d[1], "status"), L(d[1], "pieces")));
        Assert.All(new[] { "received", "damaged", "missing", "charge" }, k => Assert.Null(d[1].Cells[k]));
        Assert.Equal(("Returned", 50L, 46L, 2L, 2L, 123456L), (S(d[2], "status"), L(d[2], "pieces"), L(d[2], "received"), L(d[2], "damaged"), L(d[2], "missing"), L(d[2], "charge")));
        Assert.Equal(3, r.Rows.Count);
    }

    [Fact]
    public async Task Money_gate_holders_see_the_charge_the_view_only_role_does_not()
    {
        var runner = Runner();
        foreach (var role in new[] { "MGR", "ACC" })
            Assert.Contains((await Run(db, runner, role, StockLaundryReport.Id)).Columns, c => c.Key == "charge");
        var sup = await Run(db, runner, "SUP", StockLaundryReport.Id);
        Assert.DoesNotContain(sup.Columns, c => c.Key == "charge");
        Assert.Equal(3, Data(sup).Count);
        Assert.Contains("charge", sup.Removed!);
    }

    [Fact]
    public async Task Roles_without_the_stock_view_right_are_refused_and_the_hub_refuses_the_no_money_roles()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, StockLaundryReport.Id)));
        var svc = Service(db, new StockLaundryReport(Store(db)));
        var none = new Dictionary<string, string?>();
        foreach (var role in new[] { "HKP", "SUP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => svc.RunAsync(new Caller(db.PropertyId, db.Users[role]), StockLaundryReport.Id, none, null, 50, true, Ct)));
    }
}
