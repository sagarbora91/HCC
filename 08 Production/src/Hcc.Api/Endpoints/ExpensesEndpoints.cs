using Hcc.Application.Access;
using Hcc.Application.Expenses;
using Hcc.Contracts.Expenses;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Expenses;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Expense endpoints: <c>POST /api/v1/commands/{SubmitExpense|SubmitExpenseDraft|DecideExpense|ReverseExpense}</c>,
/// <c>GET /api/v1/expenses</c> (paged; own-only for submitters), <c>/expenses/{id}</c>, <c>/expenses/summary?date=</c>, <c>/expenses/options</c>.
/// </summary>
public static class ExpensesEndpoints
{
    public static IServiceCollection AddExpensesModule(this IServiceCollection services)
    {
        services.AddSingleton<IExpenseStore>(sp => new SqlExpenseStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IAccessScopeResolver>(sp => new SqlExpenseScopeResolver(sp.GetRequiredService<SqlPlatform>().Connections)); // "submission" (O)
        services.AddSingleton<ExpenseCommands>();
        services.AddSingleton<ExpenseQueries>();
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
        g.MapGet("/expenses/{expenseId:long}", async (long expenseId, HttpContext http, ExpenseQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetAsync(property, principal, expenseId, ct));
        });
        return api;
    }
}
