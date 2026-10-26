using Hcc.Application.Billing;
using Hcc.Application.Groups;
using Hcc.Contracts.Groups;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Groups;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Groups billing endpoints (W3-4): <c>POST /api/v1/commands/{CreateGroup|UpdateGroup|SetGroupRouting|AttachGroupMember|DetachGroupMember|ApplyGroupRouting|
/// RerouteGroupCharge|TransferGroupMember|RecordGroupReceipt|RequestGroupAttrition|DecideGroupAttrition|SettleGroup|SaveCompany|SetCompanyLimit|DecideCompanyLimit}</c> (Idempotency-Key required),
/// <c>GET /api/v1/groups</c> and <c>GET /api/v1/groups/{groupId}/statement</c>. Every route requires a session.
/// </summary>
public static class GroupsEndpoints
{
    public static IServiceCollection AddGroupsModule(this IServiceCollection services)
    {
        services.AddSingleton<IGroupStore>(sp => new SqlGroupStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IGroupFolioPort>(sp => new BillingGroupFolioPort(sp.GetRequiredService<IBillingStore>()));
        services.AddSingleton<IGroupInvoiceSource, GroupInvoiceSource>(); // read port for W3-6 (group invoice from the master folio)
        services.AddSingleton<ICompanyStore>(sp => new SqlCompanyStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<CompanyCommands>();
        services.AddSingleton<CompanyQueries>();
        services.AddSingleton<GroupCommands>();
        services.AddSingleton<GroupQueries>();
        return services;
    }

    public static RouteGroupBuilder MapGroupsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + GroupCommands.OpCreate, async (HttpContext http, CreateGroupRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.CreateAsync(http.HccCommand(GroupCommands.OpCreate), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpUpdate, async (HttpContext http, UpdateGroupRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.UpdateAsync(http.HccCommand(GroupCommands.OpUpdate), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpRouting, async (HttpContext http, SetGroupRoutingRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetRoutingAsync(http.HccCommand(GroupCommands.OpRouting), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpAttach, async (HttpContext http, GroupMemberRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.AttachMemberAsync(http.HccCommand(GroupCommands.OpAttach), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpDetach, async (HttpContext http, GroupMemberRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.DetachMemberAsync(http.HccCommand(GroupCommands.OpDetach), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpApply, async (HttpContext http, ApplyGroupRoutingRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.ApplyRoutingAsync(http.HccCommand(GroupCommands.OpApply), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpReroute, async (HttpContext http, RerouteGroupChargeRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.RerouteAsync(http.HccCommand(GroupCommands.OpReroute), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpTransfer, async (HttpContext http, TransferGroupMemberRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.TransferAsync(http.HccCommand(GroupCommands.OpTransfer), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpReceipt, async (HttpContext http, RecordGroupReceiptRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.RecordReceiptAsync(http.HccCommand(GroupCommands.OpReceipt), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpAttrition, async (HttpContext http, RequestGroupAttritionRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.RequestAttritionAsync(http.HccCommand(GroupCommands.OpAttrition), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpDecide, async (HttpContext http, DecideGroupAttritionRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.DecideAttritionAsync(http.HccCommand(GroupCommands.OpDecide), body, ct)));
        g.MapPost("/commands/" + GroupCommands.OpSettle, async (HttpContext http, SettleGroupRequest body, GroupCommands c, CancellationToken ct) =>
            http.HccReply(await c.SettleAsync(http.HccCommand(GroupCommands.OpSettle), body, ct)));
        g.MapPost("/commands/" + CompanyCommands.OpSave, async (HttpContext http, SaveCompanyRequest body, CompanyCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveAsync(http.HccCommand(CompanyCommands.OpSave), body, ct)));
        g.MapPost("/commands/" + CompanyCommands.OpLimit, async (HttpContext http, SetCompanyLimitRequest body, CompanyCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetLimitAsync(http.HccCommand(CompanyCommands.OpLimit), body, ct)));
        g.MapPost("/commands/" + CompanyCommands.OpDecide, async (HttpContext http, DecideCompanyLimitRequest body, CompanyCommands c, CancellationToken ct) =>
            http.HccReply(await c.DecideAsync(http.HccCommand(CompanyCommands.OpDecide), body, ct)));
        g.MapGet("/groups/companies", async (HttpContext http, CompanyQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.BookAsync(property, principal, ct));
        });
        g.MapGet("/groups", async (HttpContext http, GroupQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ListAsync(property, principal, ct));
        });
        g.MapGet("/groups/{groupId:long}/statement", async (long groupId, HttpContext http, GroupQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.StatementAsync(property, principal, groupId, ct));
        });
        return api;
    }
}
