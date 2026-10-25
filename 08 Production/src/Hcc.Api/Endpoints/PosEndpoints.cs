using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Pos;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Pos;
using Hcc.Infrastructure.SqlServer;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Restaurant POS endpoints: <c>POST /api/v1/commands/{CreateOutlet|SaveMenuItem|SetMenuItemAvailability|OpenCheck|AddCheckItem|SetCheckItemQty|SendKot|
/// AdvanceKot|SettleCheck|VoidCheckItem|VoidBill|DecideVoid}</c> (Idempotency-Key required) and GETs for checks, kitchen, menu, outlets, day summary and the
/// day-close blockers. All require a session; the actor is the session principal.
/// </summary>
public static class PosEndpoints
{
    public static IServiceCollection AddPosModule(this IServiceCollection services)
    {
        services.AddSingleton<IPosStore>(sp => new SqlPosStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IPosTaxRates, TaxRateSourcePosTaxRates>(); // swap for the GST master adapter at merge
        services.AddSingleton<IPosFolioPort, BillingPosFolioPort>();   // room posting through Billing's folio (IBillingStore)
        services.AddSingleton<IPosCloseGate, PosCloseGate>();          // open checks / pending voids block the day close
        services.AddSingleton<PosService>();                           // cash goes through ICashDrawerPort (CashierService, DEC-052)
        services.AddSingleton<PosQueries>();
        return services;
    }

    public static RouteGroupBuilder MapPosEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        Command<CreateOutletRequest, OutletResult>(g, "CreateOutlet", (s, c, k, r, ct) => s.CreateOutletAsync(c, k, r, ct));
        Command<SaveMenuItemRequest, MenuItemResult>(g, "SaveMenuItem", (s, c, k, r, ct) => s.SaveMenuItemAsync(c, k, r, ct));
        Command<SetMenuItemAvailabilityRequest, MenuItemResult>(g, "SetMenuItemAvailability", (s, c, k, r, ct) => s.SetMenuItemAvailabilityAsync(c, k, r, ct));
        Command<OpenCheckRequest, CheckResult>(g, "OpenCheck", (s, c, k, r, ct) => s.OpenCheckAsync(c, k, r, ct));
        Command<AddCheckItemRequest, CheckResult>(g, "AddCheckItem", (s, c, k, r, ct) => s.AddItemAsync(c, k, r, ct));
        Command<SetCheckItemQtyRequest, CheckResult>(g, "SetCheckItemQty", (s, c, k, r, ct) => s.SetItemQtyAsync(c, k, r, ct));
        Command<SendKotRequest, KotResult>(g, "SendKot", (s, c, k, r, ct) => s.SendKotAsync(c, k, r, ct));
        Command<AdvanceKotRequest, KotResult>(g, "AdvanceKot", (s, c, k, r, ct) => s.AdvanceKotAsync(c, k, r, ct));
        Command<SettleCheckRequest, SettleResult>(g, "SettleCheck", (s, c, k, r, ct) => s.SettleCheckAsync(c, k, r, ct));
        Command<VoidCheckItemRequest, VoidResult>(g, "VoidCheckItem", (s, c, k, r, ct) => s.VoidItemAsync(c, k, r, ct));
        Command<VoidBillRequest, VoidResult>(g, "VoidBill", (s, c, k, r, ct) => s.VoidBillAsync(c, k, r, ct));
        Command<DecideVoidRequest, DecideVoidResult>(g, "DecideVoid", (s, c, k, r, ct) => s.DecideVoidAsync(c, k, r, ct));
        g.MapGet("/pos/checks/{orderId:long}", async (long orderId, HttpContext http, PosQueries q, CancellationToken ct)
            => Results.Ok(await q.GetCheckAsync(http.PosCaller(), orderId, ct).ConfigureAwait(false)));
        g.MapGet("/pos/checks", async (HttpContext http, PosQueries q, CancellationToken ct)
            => Results.Ok(await q.OpenChecksAsync(http.PosCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/pos/outlets", async (HttpContext http, PosQueries q, CancellationToken ct)
            => Results.Ok(await q.OutletsAsync(http.PosCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/pos/menu", async (HttpContext http, PosQueries q, CancellationToken ct, int? outletId = null)
            => Results.Ok(await q.MenuAsync(http.PosCaller(), outletId, ct).ConfigureAwait(false)));
        g.MapGet("/pos/kitchen", async (HttpContext http, PosQueries q, CancellationToken ct)
            => Results.Ok(await q.KitchenAsync(http.PosCaller(), ct).ConfigureAwait(false)));
        g.MapGet("/pos/day-summary", async (HttpContext http, PosQueries q, CancellationToken ct, DateOnly? date = null)
            => Results.Ok(await q.DaySummaryAsync(http.PosCaller(), date, ct).ConfigureAwait(false)));
        g.MapGet("/pos/close-blockers", async (HttpContext http, PosQueries q, CancellationToken ct)
            => Results.Ok(await q.BlockersAsync(http.PosCaller(), ct).ConfigureAwait(false)));
        return api;
    }

    public static PosCaller PosCaller(this HttpContext http)
    {
        var (property, principal) = http.HccActor();
        return new PosCaller(property, principal);
    }

    private static void Command<TRequest, TResult>(RouteGroupBuilder group, string name,
        Func<PosService, PosCaller, string, TRequest, CancellationToken, Task<CommandResult<TResult>>> run)
    {
        group.MapPost("/commands/" + name, async (HttpContext http, TRequest? body, CancellationToken ct) =>
        {
            var caller = http.PosCaller();
            var key = http.Request.IdempotencyKey();
            if (body is null) throw HccException.Validation("The request body is required.");
            return http.HccReply(await run(http.RequestServices.GetRequiredService<PosService>(), caller, key, body, ct).ConfigureAwait(false));
        });
    }
}
