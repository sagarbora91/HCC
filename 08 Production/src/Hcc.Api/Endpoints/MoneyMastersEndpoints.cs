using Hcc.Application.Masters;
using Hcc.Application.MoneyMasters;
using Hcc.Contracts.MoneyMasters;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.MoneyMasters;

namespace Hcc.Api.Endpoints;

/// <summary>
/// MoneyMasters endpoints (MA-3): <c>POST /api/v1/commands/{Save|Retire|Reactivate|Delete}{PaymentMethod|BankAccount|ExpenseCategory|ExpenseVendor}</c>,
/// <c>Reorder{PaymentMethods|BankAccounts|ExpenseCategories|ExpenseVendors}</c> (PERM-MST-002) and
/// <c>GET /api/v1/masters/{payment-methods|bank-accounts|expense-categories|expense-vendors}</c> (+ <c>/history</c>, <c>/{id}/history</c>).
/// Needs the Masters module (AddMastersModule: IMastersStore, IMastersReadStore for history and business date).
/// </summary>
public static class MoneyMastersEndpoints
{
    public static IServiceCollection AddMoneyMastersModule(this IServiceCollection services)
    {
        services.AddSingleton<IMoneyMastersStore, SqlMoneyMastersStore>();
        services.AddSingleton<IMoneyMastersReadStore>(sp => new SqlMoneyMastersReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IMasterUsage, MoneyMasterUsage>();
        // MA-11: receipts, outlet bills and expenses resolve their method through mst.payment_method (BillingService, PosService, ExpenseCommands /
        // ExpenseQueries take it as an optional dependency; without this module they keep the old fixed lists).
        services.AddSingleton<IPaymentMethodSource>(sp => new SqlPaymentMethodSource(sp.GetRequiredService<SqlPlatform>().Connections));   // records per payment method / category / vendor; methods per account
        services.AddSingleton<MoneyMastersCommands>();
        services.AddSingleton<MoneyMastersQueries>();
        return services;
    }

    public static RouteGroupBuilder MapMoneyMastersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        const string pm = MoneyMastersCommands.OpSavePaymentMethod;
        g.MapPost("/commands/" + pm, async (HttpContext http, SavePaymentMethodRequest body, MoneyMastersCommands c, CancellationToken ct) => http.HccReply(await c.SavePaymentMethodAsync(http.HccCommand(pm), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpRetirePaymentMethod, async (HttpContext http, PaymentMethodIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.RetirePaymentMethodAsync(http.HccCommand(MoneyMastersCommands.OpRetirePaymentMethod), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpReactivatePaymentMethod, async (HttpContext http, PaymentMethodIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReactivatePaymentMethodAsync(http.HccCommand(MoneyMastersCommands.OpReactivatePaymentMethod), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpDeletePaymentMethod, async (HttpContext http, PaymentMethodIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.DeletePaymentMethodAsync(http.HccCommand(MoneyMastersCommands.OpDeletePaymentMethod), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpReorderPaymentMethods, async (HttpContext http, ReorderPaymentMethodsRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReorderPaymentMethodsAsync(http.HccCommand(MoneyMastersCommands.OpReorderPaymentMethods), body, ct)));

        g.MapPost("/commands/" + MoneyMastersCommands.OpSaveBankAccount, async (HttpContext http, SaveBankAccountRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveBankAccountAsync(http.HccCommand(MoneyMastersCommands.OpSaveBankAccount), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpRetireBankAccount, async (HttpContext http, BankAccountIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.RetireBankAccountAsync(http.HccCommand(MoneyMastersCommands.OpRetireBankAccount), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpReactivateBankAccount, async (HttpContext http, BankAccountIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReactivateBankAccountAsync(http.HccCommand(MoneyMastersCommands.OpReactivateBankAccount), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpDeleteBankAccount, async (HttpContext http, BankAccountIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.DeleteBankAccountAsync(http.HccCommand(MoneyMastersCommands.OpDeleteBankAccount), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpReorderBankAccounts, async (HttpContext http, ReorderBankAccountsRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReorderBankAccountsAsync(http.HccCommand(MoneyMastersCommands.OpReorderBankAccounts), body, ct)));

        g.MapPost("/commands/" + MoneyMastersCommands.OpSaveExpenseCategory, async (HttpContext http, SaveExpenseCategoryRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveExpenseCategoryAsync(http.HccCommand(MoneyMastersCommands.OpSaveExpenseCategory), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpRetireExpenseCategory, async (HttpContext http, ExpenseCategoryIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.RetireExpenseCategoryAsync(http.HccCommand(MoneyMastersCommands.OpRetireExpenseCategory), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpReactivateExpenseCategory, async (HttpContext http, ExpenseCategoryIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReactivateExpenseCategoryAsync(http.HccCommand(MoneyMastersCommands.OpReactivateExpenseCategory), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpDeleteExpenseCategory, async (HttpContext http, ExpenseCategoryIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.DeleteExpenseCategoryAsync(http.HccCommand(MoneyMastersCommands.OpDeleteExpenseCategory), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpReorderExpenseCategories, async (HttpContext http, ReorderExpenseCategoriesRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReorderExpenseCategoriesAsync(http.HccCommand(MoneyMastersCommands.OpReorderExpenseCategories), body, ct)));

        g.MapPost("/commands/" + MoneyMastersCommands.OpSaveExpenseVendor, async (HttpContext http, SaveExpenseVendorRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveExpenseVendorAsync(http.HccCommand(MoneyMastersCommands.OpSaveExpenseVendor), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpRetireExpenseVendor, async (HttpContext http, ExpenseVendorIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.RetireExpenseVendorAsync(http.HccCommand(MoneyMastersCommands.OpRetireExpenseVendor), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpReactivateExpenseVendor, async (HttpContext http, ExpenseVendorIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReactivateExpenseVendorAsync(http.HccCommand(MoneyMastersCommands.OpReactivateExpenseVendor), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpDeleteExpenseVendor, async (HttpContext http, ExpenseVendorIdRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.DeleteExpenseVendorAsync(http.HccCommand(MoneyMastersCommands.OpDeleteExpenseVendor), body, ct)));
        g.MapPost("/commands/" + MoneyMastersCommands.OpReorderExpenseVendors, async (HttpContext http, ReorderExpenseVendorsRequest body, MoneyMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReorderExpenseVendorsAsync(http.HccCommand(MoneyMastersCommands.OpReorderExpenseVendors), body, ct)));

        g.MapGet("/masters/payment-methods", async (HttpContext http, MoneyMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.PaymentMethodsAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/bank-accounts", async (HttpContext http, MoneyMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.BankAccountsAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/expense-categories", async (HttpContext http, MoneyMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ExpenseCategoriesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/expense-vendors", async (HttpContext http, MoneyMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ExpenseVendorsAsync(property, principal, page, pageSize, status, ct));
        });
        foreach (var (route, master) in new[]
                 {
                     ("payment-methods", MoneyMasterIds.PaymentMethods), ("bank-accounts", MoneyMasterIds.BankAccounts),
                     ("expense-categories", MoneyMasterIds.ExpenseCategories), ("expense-vendors", MoneyMasterIds.ExpenseVendors),
                 })
        {
            g.MapGet($"/masters/{route}/history", async (HttpContext http, MoneyMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(property, principal, master, null, page, pageSize, ct));
            });
            g.MapGet($"/masters/{route}/{{id:int}}/history", async (HttpContext http, MoneyMastersQueries q, int id, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(property, principal, master, id, page, pageSize, ct));
            });
        }

        return api;
    }
}
