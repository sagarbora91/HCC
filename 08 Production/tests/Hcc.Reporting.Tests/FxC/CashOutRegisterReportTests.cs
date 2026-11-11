// FX-RPT-C · rpt.money.cash-out-register on a disposable HccTest_* database: bank deposits, till transfers and Owner drawings from pay.cash_transfer, oldest first, kind filter, totals
// per kind without rejected records, to the paise; permission PERM-RPT-002 (Owner, Manager, Accountant), housekeeping refused at the hub (DEC-061).
using Hcc.Reporting.Tests.CuB;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.FxC;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.FxC;

[Trait("Category", "Sql")]
public sealed class CashOutRegisterReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    // 22 Oct: T1 bank deposit 3,000.00 (Demo Bank, slip SL-1) confirmed by MGR. 23 Oct: T2 till transfer A to B 1,000.00 accepted; T3 Owner drawing 500.00; T4 bank deposit 700.00 rejected.
    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            var (p, own, mgr, csh) = (db.PropertyId, db.Users["OWN"], db.Users["MGR"], db.Users["CSH"]);
            await db.ScalarAsync($"""
                DECLARE @p INT = {p};
                INSERT pay.till(property_id, code, name, created_by, created_utc) VALUES (@p, 'TA', N'Till A', {csh}, '2026-01-01'), (@p, 'TB', N'Till B', {csh}, '2026-01-01');
                DECLARE @ta INT = (SELECT till_id FROM pay.till WHERE code = 'TA' AND property_id = @p), @tb INT = (SELECT till_id FROM pay.till WHERE code = 'TB' AND property_id = @p);
                INSERT pay.shift(till_id, business_date, status, opening_float_paise, opened_by, opened_utc, expected_paise, counted_paise, submitted_by, submitted_utc, closed_by, closed_utc) VALUES
                  (@ta, '2026-10-22', 'Closed', 1000000, {csh}, '2026-10-22 03:30', 1000000, 1000000, {csh}, '2026-10-22 16:00', {mgr}, '2026-10-22 16:30'),
                  (@ta, '2026-10-23', 'Open', 1000000, {csh}, '2026-10-23 03:30', NULL, NULL, NULL, NULL, NULL, NULL);
                DECLARE @s22 BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @ta AND business_date = '2026-10-22'), @s23 BIGINT = (SELECT shift_id FROM pay.shift WHERE till_id = @ta AND business_date = '2026-10-23');
                INSERT pay.cash_transfer(property_id, kind, status, business_date, amount_paise, from_till_id, from_shift_id, to_till_id, bank, slip_no, reason, recorded_by, recorded_utc, decided_by, decided_utc, decided_date) VALUES
                  (@p, 'banking', 'Confirmed', '2026-10-22', 300000, @ta, @s22, NULL, N'Demo Bank', N'SL-1', NULL, {csh}, '2026-10-22 10:00', {mgr}, '2026-10-22 11:00', '2026-10-22'),
                  (@p, 'till', 'Accepted', '2026-10-23', 100000, @ta, @s23, @tb, NULL, NULL, N'Float for the evening', {csh}, '2026-10-23 04:00', {mgr}, '2026-10-23 04:10', '2026-10-23'),
                  (@p, 'drawing', 'Posted', '2026-10-23', 50000, @ta, @s23, NULL, NULL, NULL, N'Owner personal use', {own}, '2026-10-23 05:00', NULL, NULL, NULL),
                  (@p, 'banking', 'Rejected', '2026-10-23', 70000, @ta, @s23, NULL, N'Demo Bank', N'SL-2', NULL, {csh}, '2026-10-23 06:00', {own}, '2026-10-23 06:30', '2026-10-23');
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Hcc.Reporting.ReportRunner Runner() => CuBRig.Runner(db, new CashOutRegisterReport(db.Platform));

    [Fact]
    public async Task Records_oldest_first_and_totals_per_kind_leave_out_rejected()
    {
        var r = await Run(db, Runner(), "OWN", CashOutRegisterReport.Id, ("from", "2026-10-22"), ("to", "2026-10-23"));
        var t = ReportRunner.Tables(r);
        Assert.Equal(["records", "kinds"], t.Select(x => x.Key));
        var rec = t[0].Rows;
        Assert.Equal(["bank deposit", "till transfer", "owner drawing", "bank deposit"], rec.Select(x => S(x, "kind")));
        Assert.Equal([300000L, 100000L, 50000L, 70000L], rec.Select(x => L(x, "amount")));
        Assert.Equal(("Demo Bank · SL-1", "Till A", null, "MGR", "Confirmed"), (S(rec[0], "bankSlip"), S(rec[0], "fromTill"), (string?)rec[0].Cells["toTill"], S(rec[0], "decidedBy"), S(rec[0], "status")));
        Assert.Equal(("Till B", "Float for the evening", "Accepted"), (S(rec[1], "toTill"), S(rec[1], "reason"), S(rec[1], "status")));
        Assert.Null(rec[2].Cells["decidedBy"]);
        Assert.Equal(("Rejected", "OWN"), (S(rec[3], "status"), S(rec[3], "decidedBy")));
        Assert.Equal([("bank deposit", 1L, 300000L), ("till transfer", 1L, 100000L), ("owner drawing", 1L, 50000L)], t[1].Rows.Select(x => (S(x, "kind"), L(x, "count"), L(x, "amount"))));
    }

    [Fact]
    public async Task The_kind_filter_narrows_both_blocks_and_a_bad_kind_is_refused()
    {
        var r = await Run(db, Runner(), "ACC", CashOutRegisterReport.Id, ("from", "2026-10-22"), ("to", "2026-10-23"), ("kind", "bank deposit"));
        var t = ReportRunner.Tables(r);
        Assert.Equal(2, t[0].Rows.Count);
        Assert.Equal(("bank deposit", 1L, 300000L), (S(Assert.Single(t[1].Rows), "kind"), L(t[1].Rows[0], "count"), L(t[1].Rows[0], "amount")));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, Runner(), "OWN", CashOutRegisterReport.Id, ("kind", "gift"))));
        var none = await Run(db, Runner(), "OWN", CashOutRegisterReport.Id, ("from", "2026-10-21"), ("to", "2026-10-21"));
        Assert.Empty(ReportRunner.Tables(none)[0].Rows);
        Assert.All(ReportRunner.Tables(none)[1].Rows, x => Assert.Equal(0L, L(x, "amount")));
    }

    [Fact]
    public async Task Owner_manager_accountant_may_run_it_everyone_else_is_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, CashOutRegisterReport.Id, ("from", "2026-10-23"), ("to", "2026-10-23"))));
        foreach (var role in new[] { "OWN", "MGR", "ACC" })
            Assert.Null(await Code(() => Run(db, runner, role, CashOutRegisterReport.Id, ("from", "2026-10-23"), ("to", "2026-10-23"))));
        var svc = Service(db, new CashOutRegisterReport(db.Platform));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => svc.RunAsync(new Caller(db.PropertyId, db.Users["HKP"]), CashOutRegisterReport.Id, new Dictionary<string, string?>(), null, 50, true, Ct)));
    }
}
