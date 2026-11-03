using Hcc.Application.Masters;
using Hcc.Application.PeopleLists;
using Hcc.Contracts.PeopleLists;
using Hcc.Domain.PeopleLists;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.PeopleLists;

namespace Hcc.Api.Endpoints;

/// <summary>
/// People lists endpoints (MA-6): commands <c>POST /api/v1/commands/{Save|Retire|Reactivate|Delete}{Designation|ShiftPattern|LeaveType|Holiday}</c> and
/// <c>Reorder{Designations|ShiftPatterns|LeaveTypes|Holidays}</c>; reads <c>GET /api/v1/hr/{designations|shift-patterns|leave-types|holidays}</c> with
/// <c>/history</c> and <c>/{id}/history</c>.
/// </summary>
public static class PeopleListsEndpoints
{
    public static IServiceCollection AddPeopleListsModule(this IServiceCollection services)
    {
        services.AddSingleton<IPeopleListsStore, SqlPeopleListsStore>();
        services.AddSingleton<IPeopleListsReadStore>(sp => new SqlPeopleListsReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IMasterUsage, SqlPeopleListsUsage>();
        services.AddSingleton<PeopleListsCommands>();
        services.AddSingleton<PeopleListsQueries>();
        return services;
    }

    public static RouteGroupBuilder MapPeopleListsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        var mst = g.MapGroup(string.Empty).RequireHccFeature(FeatureIds.Masters); // ADM follow-up (DEC-214): master-list commands only; reads stay on g
        const string c = "/commands/";

        mst.MapPost(c + "SaveDesignation", async (HttpContext http, SaveDesignationRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.SaveDesignationAsync(http.HccCommand("SaveDesignation"), b, ct)));
        mst.MapPost(c + "RetireDesignation", async (HttpContext http, RetireDesignationRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.RetireDesignationAsync(http.HccCommand("RetireDesignation"), b, ct)));
        mst.MapPost(c + "ReactivateDesignation", async (HttpContext http, ReactivateDesignationRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.ReactivateDesignationAsync(http.HccCommand("ReactivateDesignation"), b, ct)));
        mst.MapPost(c + "DeleteDesignation", async (HttpContext http, DeleteDesignationRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.DeleteDesignationAsync(http.HccCommand("DeleteDesignation"), b, ct)));
        mst.MapPost(c + "ReorderDesignations", async (HttpContext http, ReorderDesignationsRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.ReorderDesignationsAsync(http.HccCommand("ReorderDesignations"), b, ct)));

        mst.MapPost(c + "SaveShiftPattern", async (HttpContext http, SaveShiftPatternRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.SaveShiftPatternAsync(http.HccCommand("SaveShiftPattern"), b, ct)));
        mst.MapPost(c + "RetireShiftPattern", async (HttpContext http, RetireShiftPatternRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.RetireShiftPatternAsync(http.HccCommand("RetireShiftPattern"), b, ct)));
        mst.MapPost(c + "ReactivateShiftPattern", async (HttpContext http, ReactivateShiftPatternRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.ReactivateShiftPatternAsync(http.HccCommand("ReactivateShiftPattern"), b, ct)));
        mst.MapPost(c + "DeleteShiftPattern", async (HttpContext http, DeleteShiftPatternRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.DeleteShiftPatternAsync(http.HccCommand("DeleteShiftPattern"), b, ct)));
        mst.MapPost(c + "ReorderShiftPatterns", async (HttpContext http, ReorderShiftPatternsRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.ReorderShiftPatternsAsync(http.HccCommand("ReorderShiftPatterns"), b, ct)));

        mst.MapPost(c + "SaveLeaveType", async (HttpContext http, SaveLeaveTypeRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.SaveLeaveTypeAsync(http.HccCommand("SaveLeaveType"), b, ct)));
        mst.MapPost(c + "RetireLeaveType", async (HttpContext http, RetireLeaveTypeRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.RetireLeaveTypeAsync(http.HccCommand("RetireLeaveType"), b, ct)));
        mst.MapPost(c + "ReactivateLeaveType", async (HttpContext http, ReactivateLeaveTypeRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.ReactivateLeaveTypeAsync(http.HccCommand("ReactivateLeaveType"), b, ct)));
        mst.MapPost(c + "DeleteLeaveType", async (HttpContext http, DeleteLeaveTypeRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.DeleteLeaveTypeAsync(http.HccCommand("DeleteLeaveType"), b, ct)));
        mst.MapPost(c + "ReorderLeaveTypes", async (HttpContext http, ReorderLeaveTypesRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.ReorderLeaveTypesAsync(http.HccCommand("ReorderLeaveTypes"), b, ct)));

        mst.MapPost(c + "SaveHoliday", async (HttpContext http, SaveHolidayRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.SaveHolidayAsync(http.HccCommand("SaveHoliday"), b, ct)));
        mst.MapPost(c + "RetireHoliday", async (HttpContext http, RetireHolidayRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.RetireHolidayAsync(http.HccCommand("RetireHoliday"), b, ct)));
        mst.MapPost(c + "ReactivateHoliday", async (HttpContext http, ReactivateHolidayRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.ReactivateHolidayAsync(http.HccCommand("ReactivateHoliday"), b, ct)));
        mst.MapPost(c + "DeleteHoliday", async (HttpContext http, DeleteHolidayRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.DeleteHolidayAsync(http.HccCommand("DeleteHoliday"), b, ct)));
        mst.MapPost(c + "ReorderHolidays", async (HttpContext http, ReorderHolidaysRequest b, PeopleListsCommands x, CancellationToken ct) => http.HccReply(await x.ReorderHolidaysAsync(http.HccCommand("ReorderHolidays"), b, ct)));

        g.MapGet("/hr/designations", async (HttpContext http, PeopleListsQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.DesignationsAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/hr/shift-patterns", async (HttpContext http, PeopleListsQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ShiftPatternsAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/hr/leave-types", async (HttpContext http, PeopleListsQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.LeaveTypesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/hr/holidays", async (HttpContext http, PeopleListsQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.HolidaysAsync(property, principal, page, pageSize, status, ct));
        });

        foreach (var (route, list) in new[] { ("designations", PeopleList.Designations), ("shift-patterns", PeopleList.ShiftPatterns), ("leave-types", PeopleList.LeaveTypes), ("holidays", PeopleList.Holidays) })
        {
            var l = list;
            g.MapGet($"/hr/{route}/history", async (HttpContext http, PeopleListsQueries q, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(l, property, principal, null, page, pageSize, ct));
            });
            g.MapGet($"/hr/{route}/{{id:int}}/history", async (HttpContext http, PeopleListsQueries q, int id, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(l, property, principal, id, page, pageSize, ct));
            });
        }

        return api;
    }
}
