using Hcc.Application.Reminders;
using Hcc.Contracts;
using Hcc.Contracts.Reminders;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Reminders;

using Hcc.Domain.Configuration;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Desk reminders (LF B4, PERM-RMD-001): <c>POST /api/v1/commands/{SaveReminder|MarkReminderDone|DismissReminder}</c> and <c>GET /api/v1/reminders?bucket=today|upcoming|done</c>.
/// The wake-up alert needs no endpoint of its own: the desk derives it from the synced guest requests (ops.guest_request.wake_at_utc).
/// </summary>
public static class RemindersEndpoints
{
    public static IServiceCollection AddRemindersModule(this IServiceCollection services)
    {
        services.AddSingleton<IReminderStore>(sp => new SqlReminderStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<ReminderCommands>();
        services.AddSingleton<ReminderQueries>();
        return services;
    }

    public static RouteGroupBuilder MapRemindersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        var cmd = g.MapGroup(string.Empty).RequireHccFeature(FeatureIds.Alerts); // ADM-1 (DEC-214): refused while the sub-function is off; reads stay on g
        cmd.MapPost("/commands/" + ReminderCommands.OpSave, async (HttpContext http, SaveReminderRequest body, ReminderCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveAsync(http.HccCommand(ReminderCommands.OpSave), body, Hcc.Domain.Reminders.ReminderRules.DefaultLeadDays, ct)));
        cmd.MapPost("/commands/" + ReminderCommands.OpDone, async (HttpContext http, ReminderIdRequest body, ReminderCommands c, CancellationToken ct) =>
            http.HccReply(await c.DoneAsync(http.HccCommand(ReminderCommands.OpDone), body, ct)));
        cmd.MapPost("/commands/" + ReminderCommands.OpDismiss, async (HttpContext http, DismissReminderRequest body, ReminderCommands c, CancellationToken ct) =>
            http.HccReply(await c.DismissAsync(http.HccCommand(ReminderCommands.OpDismiss), body, ct)));
        g.MapGet("/reminders", async (HttpContext http, ReminderQueries q, TimeProvider clock, CancellationToken ct, string? bucket = null) =>
        {
            var (p, u) = http.HccActor();
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(5.5));
            return Results.Ok(await q.ListAsync(p, u, bucket ?? "today", today, ct));
        });
        return api;
    }
}
