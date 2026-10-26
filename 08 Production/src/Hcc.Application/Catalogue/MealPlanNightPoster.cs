using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Contracts.Catalogue;

namespace Hcc.Application.Catalogue;

/// <summary>MA-11: the closing night step's meal-plan lines through <see cref="CatalogueCommands.PostMealPlanNightsAsync"/> (PERM-CLS-002).</summary>
public sealed class MealPlanNightPoster(CatalogueCommands commands) : IMealPlanNightPoster
{
    public async Task<int> PostAsync(Caller caller, DateOnly serviceDate, string idempotencyKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var r = await commands.PostMealPlanNightsAsync(new CommandContext(caller.PropertyId, caller.PrincipalId, CatalogueCommands.OpPostMealPlanNights, idempotencyKey),
            new PostMealPlanNightsRequest(serviceDate), ct).ConfigureAwait(false);
        return r.Replayed ? 0 : r.Value.Posted;
    }
}
