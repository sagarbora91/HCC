using Hcc.Application.Access;
using Hcc.Application.Expenses;
using Hcc.Contracts.Expenses;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Expenses;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Expense endpoints: <c>POST /api/v1/commands/{SubmitExpense|SubmitExpenseDraft|DecideExpense|ReverseExpense|EditExpense|VoidExpense}</c> and the
/// DEC-120 SCC register commands (<see cref="ExpenseRegisterCommands.Operations"/>); <c>GET /api/v1/expenses</c> (paged; own-only for submitters),
/// <c>/expenses/{id}</c>, <c>/expenses/{id}/bill-photo</c>, <c>/expenses/summary?date=</c>, <c>/expenses/options</c>, <c>/expenses/registers</c>,
/// <c>/expenses/export/ledger</c>, <c>/expenses/export/month?month=</c>.
/// </summary>
public static class ExpensesEndpoints
{
    public static IServiceCollection AddExpensesModule(this IServiceCollection services)
    {
        services.AddSingleton<IExpenseStore>(sp => new SqlExpenseStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IExpenseRegisterStore>(sp => new SqlExpenseRegisterStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IExpenseMasterSource>(sp => new SqlExpenseMasterSource(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IAccessScopeResolver>(sp => new SqlExpenseScopeResolver(sp.GetRequiredService<SqlPlatform>().Connections)); // "submission" (O)
        services.AddSingleton<ExpenseCommands>();
        services.AddSingleton<ExpenseQueries>();
        services.AddSingleton<ExpenseRegisterCommands>();
        services.AddSingleton<ExpenseRegisterQueries>();
        return services;
    }

    public static RouteGroupBuilder MapExpensesEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + ExpenseCommands.OpSubmit, async (HttpContext http, SubmitExpenseRequest body, ExpenseCommands c, CancellationToken ct) =>
            http.HccReply(await c.SubmitAsync(http.HccCommand(ExpenseCommands.OpSubmit), body, ct)));
        g.MapPost("/commands/" + ExpenseCommands.OpSubmitDraft, async (HttpContext http, SubmitExpenseDraftRequest body, ExpenseCommands c, CancellationToken ct) =>
            http.HccReply(await c.SubmitDraftAsync(http.HccCommand(ExpenseCommands.OpSubmitDraft), body, ct)));
        g.MapPost("/commands/" + ExpenseCommands.OpDecide, async (HttpContext http, DecideExpenseRequest body, ExpenseCommands c, CancellationToken ct) =>
            http.HccReply(await c.DecideAsync(http.HccCommand(ExpenseCommands.OpDecide), body, ct)));
        g.MapPost("/commands/" + ExpenseCommands.OpReverse, async (HttpContext http, ReverseExpenseRequest body, ExpenseCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReverseAsync(http.HccCommand(ExpenseCommands.OpReverse), body, ct)));
        g.MapPost("/commands/" + ExpenseCommands.OpEdit, async (HttpContext http, EditExpenseRequest body, ExpenseCommands c, CancellationToken ct) =>
            http.HccReply(await c.EditAsync(http.HccCommand(ExpenseCommands.OpEdit), body, ct)));
        g.MapPost("/commands/" + ExpenseCommands.OpVoid, async (HttpContext http, VoidExpenseRequest body, ExpenseCommands c, CancellationToken ct) =>
            http.HccReply(await c.VoidAsync(http.HccCommand(ExpenseCommands.OpVoid), body, ct)));

        // DEC-120 SCC registers.
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpSetBudget, async (HttpContext http, SetExpenseBudgetRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetBudgetAsync(http.HccCommand(ExpenseRegisterCommands.OpSetBudget), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpCopyBudgets, async (HttpContext http, CopyExpenseBudgetsRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.CopyBudgetsAsync(http.HccCommand(ExpenseRegisterCommands.OpCopyBudgets), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpSaveRecurring, async (HttpContext http, SaveRecurringExpenseRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveRecurringAsync(http.HccCommand(ExpenseRegisterCommands.OpSaveRecurring), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpToggleRecurring, async (HttpContext http, RecurringExpenseRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.ToggleRecurringAsync(http.HccCommand(ExpenseRegisterCommands.OpToggleRecurring), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpDeleteRecurring, async (HttpContext http, RecurringExpenseRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.DeleteRecurringAsync(http.HccCommand(ExpenseRegisterCommands.OpDeleteRecurring), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpPostRecurring, async (HttpContext http, PostRecurringExpenseRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.PostRecurringAsync(http.HccCommand(ExpenseRegisterCommands.OpPostRecurring), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpRequestPettyFloat, async (HttpContext http, RequestPettyFloatRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.RequestPettyFloatAsync(http.HccCommand(ExpenseRegisterCommands.OpRequestPettyFloat), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpDecidePettyFloat, async (HttpContext http, DecidePettyFloatRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.DecidePettyFloatAsync(http.HccCommand(ExpenseRegisterCommands.OpDecidePettyFloat), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpPettyDisbursement, async (HttpContext http, RecordPettyDisbursementRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.PettyDisbursementAsync(http.HccCommand(ExpenseRegisterCommands.OpPettyDisbursement), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpAddUdhaar, async (HttpContext http, AddUdhaarRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.AddUdhaarAsync(http.HccCommand(ExpenseRegisterCommands.OpAddUdhaar), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpPayUdhaar, async (HttpContext http, PayUdhaarRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.PayUdhaarAsync(http.HccCommand(ExpenseRegisterCommands.OpPayUdhaar), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpVoidUdhaar, async (HttpContext http, VoidUdhaarRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.VoidUdhaarAsync(http.HccCommand(ExpenseRegisterCommands.OpVoidUdhaar), body, ct)));
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpLockMonth, async (HttpContext http, LockExpenseMonthRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.LockMonthAsync(http.HccCommand(ExpenseRegisterCommands.OpLockMonth), body, ct)));
        // DEC-131 (SCC decisions wave): the Owner's unlock of a locked month, with a reason.
        g.MapPost("/commands/" + ExpenseRegisterCommands.OpUnlockMonth, async (HttpContext http, UnlockExpenseMonthRequest body, ExpenseRegisterCommands c, CancellationToken ct) =>
            http.HccReply(await c.UnlockMonthAsync(http.HccCommand(ExpenseRegisterCommands.OpUnlockMonth), body, ct)));

        g.MapGet("/expenses", async (HttpContext http, ExpenseQueries q, CancellationToken ct, string? status = null, string? from = null, string? to = null, int page = 1, int pageSize = 50) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ListAsync(property, principal, status, from, to, page, pageSize, ct));
        });
        g.MapGet("/expenses/summary", async (HttpContext http, ExpenseQueries q, string date, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.SummaryAsync(property, principal, date, ct));
        });
        g.MapGet("/expenses/options", async (HttpContext http, ExpenseQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.OptionsAsync(property, principal, ct));
        });
        g.MapGet("/expenses/registers", async (HttpContext http, ExpenseRegisterQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.RegistersAsync(property, principal, ct));
        });
        g.MapGet("/expenses/export/ledger", async (HttpContext http, ExpenseRegisterQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.LedgerCsvAsync(property, principal, ct));
        });
        g.MapGet("/expenses/export/month", async (HttpContext http, ExpenseRegisterQueries q, string month, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.MonthCsvAsync(property, principal, month, ct));
        });
        g.MapGet("/expenses/{expenseId:long}", async (long expenseId, HttpContext http, ExpenseQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetAsync(property, principal, expenseId, ct));
        });
        g.MapGet("/expenses/{expenseId:long}/bill-photo", async (long expenseId, HttpContext http, ExpenseQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.BillPhotoAsync(property, principal, expenseId, ct));
        });
        return api;
    }
}
