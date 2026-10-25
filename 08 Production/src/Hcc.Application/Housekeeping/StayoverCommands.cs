using Hcc.Application.Access;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Contracts.Housekeeping;
using Hcc.Domain.Dates;

namespace Hcc.Application.Housekeeping;

/// <summary>Counts of one stay-over generation run.</summary>
public sealed record StayoverRun(int Occupied, int Created);

/// <summary>Persistence port of the stay-over generator (SQL adapter in Infrastructure/Housekeeping).</summary>
public interface IStayoverStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>
    /// One stay-over task for each room occupied on <paramref name="businessDate"/> (in-house stay, arrival on or before the date, departure after it;
    /// the room of the open segment), unique per room and date through the causal key <c>hk:stayover:&lt;room&gt;:&lt;yyyymmdd&gt;</c>.
    /// </summary>
    Task<StayoverRun> GenerateAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, DateTime utc, CancellationToken ct = default);
}

/// <summary>
/// GenerateStayoverTasks (prototype STAYOVER_GENERATE, PERM-HK-002): the manual catch-up of the daily stay-over cleans. The same generation runs
/// automatically when a new business day opens (<c>StayoverTaskProjector</c>). Idempotent per room and business date. Tasks are created
/// unassigned (as checkout cleans are); the supervisor assigns them.
/// </summary>
public sealed class StayoverCommands(ICommandRunner runner, IAccessPolicy policy, IStayoverStore store, TimeProvider clock, IBusinessDayGuard? days = null)
{
    public const string OpGenerate = "GenerateStayoverTasks";

    public async Task<CommandResult<GenerateStayoverTasksResult>> GenerateAsync(CommandContext ctx, GenerateStayoverTasksRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HousekeepingPermissions.Assign, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpGenerate }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureOpenAsync(tx, ctx.PropertyId, bd, c).ConfigureAwait(false);
            var run = await store.GenerateAsync(tx, ctx.PropertyId, bd, clock.GetUtcNow().UtcDateTime, c).ConfigureAwait(false);
            await tx.AuditAsync("STAYOVER_GENERATE", "property-" + ctx.PropertyId, $"{run.Created} stay-over task(s) created for {StayDates.ToWire(bd)}; {run.Occupied - run.Created} already existed", c).ConfigureAwait(false);
            return new GenerateStayoverTasksResult(StayDates.ToWire(bd), run.Created, run.Occupied - run.Created, run.Occupied);
        }, ct).ConfigureAwait(false);
    }
}
