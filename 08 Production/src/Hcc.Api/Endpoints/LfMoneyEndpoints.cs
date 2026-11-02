using Hcc.Application.Stays;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.LfMoney;

namespace Hcc.Api.Endpoints;

/// <summary>
/// LF-money (LF-CONTRACT §3, §7): <c>POST /api/v1/commands/{SetGuestAgreedRate|ClearGuestAgreedRate|SetGuestCreditAccount}</c> (Owner) and
/// <c>GET /api/v1/guests/{id}/money</c> (agreed rates + credit account, money roles only). The GSTR-1 legacy row rides the existing GET /invoices/gstr1.
/// </summary>
public static class LfMoneyEndpoints
{
    public static IServiceCollection AddLfMoneyModule(this IServiceCollection services)
    {
        services.AddSingleton<ILfGuestMoneyStore>(sp => new SqlLfGuestMoneyStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<LfMoneyGuestCommands>();
        return services;
    }

    public static RouteGroupBuilder MapLfMoneyEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + LfMoneyGuestCommands.OpSetAgreedRate, async (HttpContext http, SetGuestAgreedRateRequest body, LfMoneyGuestCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetAgreedRateAsync(http.HccCommand(LfMoneyGuestCommands.OpSetAgreedRate), body, ct)));
        g.MapPost("/commands/" + LfMoneyGuestCommands.OpClearAgreedRate, async (HttpContext http, ClearGuestAgreedRateRequest body, LfMoneyGuestCommands c, CancellationToken ct) =>
            http.HccReply(await c.ClearAgreedRateAsync(http.HccCommand(LfMoneyGuestCommands.OpClearAgreedRate), body, ct)));
        g.MapPost("/commands/" + LfMoneyGuestCommands.OpSetCreditAccount, async (HttpContext http, SetGuestCreditAccountRequest body, LfMoneyGuestCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetCreditAccountAsync(http.HccCommand(LfMoneyGuestCommands.OpSetCreditAccount), body, ct)));
        g.MapGet("/guests/{id:long}/money", async (HttpContext http, long id, LfMoneyGuestCommands c, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await c.ReadAsync(p, u, id, ct));
        });
        return api;
    }
}
