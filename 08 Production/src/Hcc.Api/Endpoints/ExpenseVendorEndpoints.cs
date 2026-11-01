using Hcc.Application.Accounting;
using Hcc.Application.Expenses;
using Hcc.Contracts.Expenses;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Expenses;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-2 · lane acc2-purchase — vendor payments, returns, creditor ageing and the fixed-asset register (02 Planning/agents/ACC2-POSTING-CONTRACT.md
/// §3, §9): <c>POST /api/v1/commands/{RecordVendorPayment|VoidVendorPayment|RecordExpenseReturn}</c>; <c>GET /api/v1/expenses/vendor-payments</c>,
/// <c>/expenses/vendor-ageing?asOf=&amp;vendor=</c>, <c>/accounting/assets</c> (PERM-EXP-004 or PERM-ACC-001).
/// </summary>
public static class ExpenseVendorEndpoints
{
    public static IServiceCollection AddExpenseVendorModule(this IServiceCollection services)
    {
        services.AddSingleton<IVendorPaymentStore>(sp => new SqlVendorPaymentStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IFixedAssetStore>(sp => new SqlFixedAssetStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<VendorPaymentCommands>();
        services.AddSingleton<VendorPaymentQueries>();
        return services;
    }

    // ACC-3 C1: the asset commands (PERM-ACC-010) — built from the platform with the manual-voucher port (sales post a Receipt).
    private static AssetService Assets(IServiceProvider sp) => sp.GetService<AssetService>() ?? new AssetService(sp.GetRequiredService<SqlPlatform>().Commands,
        sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetService<IFixedAssetStore>() ?? new SqlFixedAssetStore(sp.GetRequiredService<SqlPlatform>().Connections),
        sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<IGstManualVouchers>());

    public static RouteGroupBuilder MapExpenseVendorEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + VendorPaymentCommands.OpPay, async (HttpContext http, RecordVendorPaymentRequest body, VendorPaymentCommands c, CancellationToken ct) =>
            http.HccReply(await c.PayAsync(http.HccCommand(VendorPaymentCommands.OpPay), body, ct)));
        g.MapPost("/commands/" + VendorPaymentCommands.OpVoid, async (HttpContext http, VoidVendorPaymentRequest body, VendorPaymentCommands c, CancellationToken ct) =>
            http.HccReply(await c.VoidAsync(http.HccCommand(VendorPaymentCommands.OpVoid), body, ct)));
        g.MapPost("/commands/" + VendorPaymentCommands.OpReturn, async (HttpContext http, RecordExpenseReturnRequest body, VendorPaymentCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReturnAsync(http.HccCommand(VendorPaymentCommands.OpReturn), body, ct)));
        g.MapPost("/commands/" + VendorPaymentCommands.OpReallocate, async (HttpContext http, ReallocateVendorPaymentRequest body, VendorPaymentCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReallocateAsync(http.HccCommand(VendorPaymentCommands.OpReallocate), body, ct))); // ACC-3 C3
        BillingEndpoints.Command<SetAssetPutToUseRequest, FixedAssetResult>(g, AssetService.OpPutToUse, (sp, c, k, r, ct) => Assets(sp).PutToUseAsync(c, k, r, ct)); // ACC-3 C1
        BillingEndpoints.Command<DisposeFixedAssetRequest, FixedAssetResult>(g, AssetService.OpDispose, (sp, c, k, r, ct) => Assets(sp).DisposeAsync(c, k, r, ct)); // ACC-3 C1
        g.MapGet("/expenses/vendor-payments", async (HttpContext http, VendorPaymentQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ListAsync(property, principal, ct));
        });
        g.MapGet("/expenses/vendor-ageing", async (HttpContext http, VendorPaymentQueries q, CancellationToken ct, string? asOf = null, string? vendor = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.AgeingAsync(property, principal, asOf, vendor, ct));
        });
        g.MapGet("/accounting/assets", async (HttpContext http, VendorPaymentQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.AssetsAsync(property, principal, ct));
        });
        return api;
    }
}
