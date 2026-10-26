using Hcc.Application.Commands;
using Hcc.Application.Payroll;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Payroll;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Payroll;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Payroll and leave endpoints (W2-2): <c>POST /api/v1/commands/{name}</c> with an Idempotency-Key and <c>GET /api/v1/payroll/…</c> queries.
/// Every route requires a session; the actor comes from the session only. CSV files are returned as <c>text/csv</c> (fictional data).
/// </summary>
public static class PayrollEndpoints
{
    public static IServiceCollection AddPayrollModule(this IServiceCollection services)
    {
        services.AddSingleton<IPayrollStore>(sp => new SqlPayrollStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<PayrollService>();
        services.AddSingleton<PayrollQueries>();
        return services;
    }

    private static void Command<TRequest, TResult>(RouteGroupBuilder g, string name, Func<PayrollService, CommandContext, TRequest, CancellationToken, Task<CommandResult<TResult>>> run)
        => g.MapPost("/commands/" + name, async (HttpContext http, TRequest body, PayrollService svc, CancellationToken ct)
            => http.HccReply(await run(svc, http.HccCommand(name), body, ct).ConfigureAwait(false)));

    private static IResult Csv(CsvFile f) => Results.File(System.Text.Encoding.UTF8.GetBytes(f.Content), "text/csv; charset=utf-8", f.FileName);

    public static RouteGroupBuilder MapPayrollEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        Command<SaveDepartmentRequest, DepartmentResult>(g, PayrollService.OpSaveDepartment, (s, c, r, ct) => s.SaveDepartmentAsync(c, r, ct));
        Command<SaveStaffRequest, StaffResult>(g, PayrollService.OpSaveStaff, (s, c, r, ct) => s.SaveStaffAsync(c, r, ct));
        Command<SetStaffActiveRequest, StaffResult>(g, PayrollService.OpSetStaffActive, (s, c, r, ct) => s.SetStaffActiveAsync(c, r, ct));
        Command<SetRosterRequest, RosterResult>(g, PayrollService.OpSetRoster, (s, c, r, ct) => s.SetRosterAsync(c, r, ct));
        Command<CopyRosterWeekRequest, RosterResult>(g, PayrollService.OpCopyRosterWeek, (s, c, r, ct) => s.CopyRosterWeekAsync(c, r, ct));
        Command<MarkAttendanceRequest, AttendanceResult>(g, PayrollService.OpMarkAttendance, (s, c, r, ct) => s.MarkAttendanceAsync(c, r, ct));
        Command<RequestLeaveRequest, LeaveResult>(g, PayrollService.OpRequestLeave, (s, c, r, ct) => s.RequestLeaveAsync(c, r, ct));
        Command<DecideLeaveRequest, LeaveResult>(g, PayrollService.OpDecideLeave, (s, c, r, ct) => s.DecideLeaveAsync(c, r, ct));
        Command<CancelLeaveRequest, LeaveResult>(g, PayrollService.OpCancelLeave, (s, c, r, ct) => s.CancelLeaveAsync(c, r, ct));
        Command<SetLeavePolicyRequest, LeavePolicyResult>(g, PayrollService.OpSetLeavePolicy, (s, c, r, ct) => s.SetLeavePolicyAsync(c, r, ct));
        Command<GiveAdvanceRequest, AdvanceResult>(g, PayrollService.OpGiveAdvance, (s, c, r, ct) => s.GiveAdvanceAsync(c, r, ct));
        Command<SetStatutoryRatesRequest, RatesResult>(g, PayrollService.OpSetStatutoryRates, (s, c, r, ct) => s.SetStatutoryRatesAsync(c, r, ct));
        Command<PreparePayRunRequest, PayRunResult>(g, PayrollService.OpPreparePayRun, (s, c, r, ct) => s.PreparePayRunAsync(c, r, ct));
        Command<LockPayRunRequest, PayRunResult>(g, PayrollService.OpLockPayRun, (s, c, r, ct) => s.LockPayRunAsync(c, r, ct));
        Command<UnlockPayRunRequest, PayRunResult>(g, PayrollService.OpUnlockPayRun, (s, c, r, ct) => s.UnlockPayRunAsync(c, r, ct));
        Command<IssueBankFileRequest, BankFileResult>(g, PayrollService.OpIssueBankFile, (s, c, r, ct) => s.IssueBankFileAsync(c, r, ct));
        Command<SendPayslipRequest, PayslipSendResult>(g, PayrollService.OpSendPayslip, (s, c, r, ct) => s.SendPayslipAsync(c, r, ct));
        Command<PrepareFnfRequest, FnfResult>(g, PayrollService.OpPrepareFnf, (s, c, r, ct) => s.PrepareFnfAsync(c, r, ct));
        Command<DecideFnfRequest, FnfResult>(g, PayrollService.OpConfirmFnf, (s, c, r, ct) => s.ConfirmFnfAsync(c, r, ct));
        Command<DecideFnfRequest, FnfResult>(g, PayrollService.OpDiscardFnf, (s, c, r, ct) => s.DiscardFnfAsync(c, r, ct));

        g.MapGet("/payroll/staff", async (HttpContext http, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetStaffAsync(p, u, ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/staff/{staffId:long}/leave-balance", async (HttpContext http, long staffId, DateOnly? on, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetLeaveBalanceAsync(p, u, staffId, on, ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/rates/{month}", async (HttpContext http, string month, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Text(PayrollJson.Write(await q.GetRatesAsync(p, u, PayMonth.Parse(month), ct).ConfigureAwait(false)), "application/json");
        });
        g.MapGet("/payroll/runs/{month}", async (HttpContext http, string month, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetPayRunAsync(p, u, PayMonth.Parse(month), ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/runs/{month}/register.csv", async (HttpContext http, string month, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Csv(await q.RegisterCsvAsync(p, u, PayMonth.Parse(month), ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/runs/{month}/statutory.csv", async (HttpContext http, string month, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Csv(await q.StatutoryCsvAsync(p, u, PayMonth.Parse(month), ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/run/{runId:long}/bank-files/{issueNo:int}", async (HttpContext http, long runId, int issueNo, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Csv(await q.BankCsvAsync(p, u, runId, issueNo, ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/run/{runId:long}/bank-files/latest", async (HttpContext http, long runId, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Csv(await q.LatestBankCsvAsync(p, u, runId, ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/payslips/{runId:long}/{staffId:long}", async (HttpContext http, long runId, long staffId, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetPayslipAsync(p, u, runId, staffId, ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/board", async (HttpContext http, DateOnly? from, DateOnly? to, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetBoardAsync(p, u, from, to, ct).ConfigureAwait(false));
        });
        g.MapGet("/payroll/my/payslips", async (HttpContext http, PayrollQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetMyPayslipsAsync(p, u, ct).ConfigureAwait(false));
        });
        return api;
    }
}
