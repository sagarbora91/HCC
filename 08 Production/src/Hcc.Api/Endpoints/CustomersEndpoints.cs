using Hcc.Application.Customers;
using Hcc.Contracts.Customers;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Customers;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Customers / CRM endpoints: <c>POST /api/v1/commands/{ProposeCustomerMerge|DecideCustomerMerge|AddCustomerTag|RemoveCustomerTag|AddCustomerNote|SetCustomerPreferences|SetCustomerConsent}</c>
/// and <c>GET /api/v1/customers[?q=&amp;tag=&amp;page=&amp;pageSize=]</c>, <c>/customers/profile?key=</c>, <c>/customers/duplicates</c>, <c>/customers/merges[?status=]</c>.
/// </summary>
public static class CustomersEndpoints
{
    public static IServiceCollection AddCustomersModule(this IServiceCollection services)
    {
        services.AddSingleton<ICustomerStore, SqlCustomerStore>();
        services.AddSingleton<ICustomerReadStore>(sp => new SqlCustomerReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<CustomerCommands>();
        services.AddSingleton<CustomerQueries>();
        return services;
    }

    public static RouteGroupBuilder MapCustomersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + CustomerCommands.OpPropose, async (HttpContext http, ProposeCustomerMergeRequest body, CustomerCommands c, CancellationToken ct) =>
            http.HccReply(await c.ProposeMergeAsync(http.HccCommand(CustomerCommands.OpPropose), body, ct)));
        g.MapPost("/commands/" + CustomerCommands.OpDecide, async (HttpContext http, DecideCustomerMergeRequest body, CustomerCommands c, CancellationToken ct) =>
            http.HccReply(await c.DecideMergeAsync(http.HccCommand(CustomerCommands.OpDecide), body, ct)));
        g.MapPost("/commands/" + CustomerCommands.OpAddTag, async (HttpContext http, AddCustomerTagRequest body, CustomerCommands c, CancellationToken ct) =>
            http.HccReply(await c.AddTagAsync(http.HccCommand(CustomerCommands.OpAddTag), body, ct)));
        g.MapPost("/commands/" + CustomerCommands.OpRemoveTag, async (HttpContext http, RemoveCustomerTagRequest body, CustomerCommands c, CancellationToken ct) =>
            http.HccReply(await c.RemoveTagAsync(http.HccCommand(CustomerCommands.OpRemoveTag), body, ct)));
        g.MapPost("/commands/" + CustomerCommands.OpAddNote, async (HttpContext http, AddCustomerNoteRequest body, CustomerCommands c, CancellationToken ct) =>
            http.HccReply(await c.AddNoteAsync(http.HccCommand(CustomerCommands.OpAddNote), body, ct)));
        g.MapPost("/commands/" + CustomerCommands.OpPreferences, async (HttpContext http, SetCustomerPreferencesRequest body, CustomerCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetPreferencesAsync(http.HccCommand(CustomerCommands.OpPreferences), body, ct)));
        g.MapPost("/commands/" + CustomerCommands.OpConsent, async (HttpContext http, SetCustomerConsentRequest body, CustomerCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetConsentAsync(http.HccCommand(CustomerCommands.OpConsent), body, ct)));

        g.MapGet("/customers", async (HttpContext http, CustomerQueries cq, CancellationToken ct, string? q = null, string? tag = null, int page = 1, int pageSize = 50) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await cq.ListAsync(property, principal, q, tag, page, pageSize, ct));
        });
        g.MapGet("/customers/profile", async (HttpContext http, CustomerQueries q, CancellationToken ct, string key) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ProfileAsync(property, principal, key, ct));
        });
        g.MapGet("/customers/duplicates", async (HttpContext http, CustomerQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.DuplicatesAsync(property, principal, ct));
        });
        g.MapGet("/customers/merges", async (HttpContext http, CustomerQueries q, CancellationToken ct, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.MergesAsync(property, principal, status, ct));
        });
        return api;
    }
}
