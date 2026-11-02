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
        // LF A1 rate change + the LF-money approval decisions (DecideApproval delegates 'rate-change' / 'bill-correction' / 'guest-credit' / 'counter-credit')
        services.AddSingleton<IStayRateStore, Hcc.Infrastructure.SqlServer.Stays.SqlStayRateStore>();
        services.AddSingleton<StayRateCommands>();
        services.AddSingleton<Hcc.Application.Billing.ILfApprovalHandler, StayRateApprovalHandler>();
        services.AddSingleton<Hcc.Application.Billing.ILfApprovalDecisions, Hcc.Application.Billing.LfApprovalDecisions>();
        // LF B2 guest credit at check-out
        services.AddSingleton<IGuestCreditStore, SqlGuestCreditStore>();
        services.AddSingleton<GuestCreditCommands>();
        services.AddSingleton<Hcc.Application.Billing.IGuestCreditReceivables>(sp => sp.GetRequiredService<GuestCreditCommands>());
        services.AddSingleton<Hcc.Application.Billing.ILfApprovalHandler>(sp => sp.GetRequiredService<GuestCreditCommands>());
        // LF A7 counter bills (INV series source 'counter'; void = 'counter-credit' approval → credit note)
        services.AddSingleton<Hcc.Application.Counter.ICounterBillStore, SqlCounterBillStore>();
        services.AddSingleton<Hcc.Application.Invoices.IInvoiceSource, Hcc.Application.Counter.CounterInvoiceSource>();
        services.AddSingleton<Hcc.Application.Counter.CounterBillService>();
        services.AddSingleton<Hcc.Application.Billing.ILfApprovalHandler>(sp => sp.GetRequiredService<Hcc.Application.Counter.CounterBillService>());
        // LF A2 bill correction after check-out (CRN + corrected lines + revision invoice)
        services.AddSingleton<Hcc.Application.Invoices.IBillCorrectionStore>(sp => new SqlBillCorrectionStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<Hcc.Application.Invoices.BillCorrectionService>();
        services.AddSingleton<Hcc.Application.Billing.ILfApprovalHandler>(sp => sp.GetRequiredService<Hcc.Application.Invoices.BillCorrectionService>());
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
        g.MapPost("/commands/" + StayRateCommands.OpChangeStayRate, async (HttpContext http, ChangeStayRateRequest body, StayRateCommands c, CancellationToken ct) =>
            http.HccReply(await c.ChangeAsync(http.HccCommand(StayRateCommands.OpChangeStayRate), body, ct)));
        g.MapPost("/stays/{id:long}/rate-change", async (HttpContext http, long id, ChangeStayRateRequest body, StayRateCommands c, CancellationToken ct) =>
            http.HccReply(await c.ChangeAsync(http.HccCommand(StayRateCommands.OpChangeStayRate), body with { StayId = id }, ct)));
        g.MapPost("/stays/{id:long}/guest-credit", async (HttpContext http, long id, GuestCreditRequestBody body, GuestCreditCommands c, CancellationToken ct) =>
            http.HccReply(await c.RequestAsync(http.HccCommand(GuestCreditCommands.OpRequest), body with { StayId = id }, ct)));
        g.MapPost("/commands/" + GuestCreditCommands.OpRequest, async (HttpContext http, GuestCreditRequestBody body, GuestCreditCommands c, CancellationToken ct) =>
            http.HccReply(await c.RequestAsync(http.HccCommand(GuestCreditCommands.OpRequest), body, ct)));
        g.MapPost("/counter-bills", async (HttpContext http, Hcc.Application.Counter.IssueCounterBillRequest body, Hcc.Application.Counter.CounterBillService c, CancellationToken ct) =>
            http.HccReply(await c.IssueAsync(http.HccCommand(Hcc.Application.Counter.CounterBillService.OpIssue), body, ct)));
        g.MapPost("/counter-bills/{id:long}/void", async (HttpContext http, long id, Hcc.Application.Counter.RequestCounterBillVoidRequest body, Hcc.Application.Counter.CounterBillService c, CancellationToken ct) =>
            http.HccReply(await c.RequestVoidAsync(http.HccCommand(Hcc.Application.Counter.CounterBillService.OpRequestVoid), body with { BillId = id }, ct)));
        g.MapPost("/invoices/{no}/corrections", async (HttpContext http, string no, Hcc.Application.Invoices.RequestBillCorrectionRequest body, Hcc.Application.Invoices.BillCorrectionService c, CancellationToken ct) =>
            http.HccReply(await c.RequestAsync(http.HccCommand(Hcc.Application.Invoices.BillCorrectionService.OpRequest), body with { InvoiceNo = Uri.UnescapeDataString(no) }, ct)));
        g.MapGet("/invoices/corrections", async (HttpContext http, Hcc.Application.Invoices.BillCorrectionService c, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await c.ListAsync(p, u, ct));
        });
        g.MapGet("/guests/{id:long}/money", async (HttpContext http, long id, LfMoneyGuestCommands c, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await c.ReadAsync(p, u, id, ct));
        });
        return api;
    }
}
