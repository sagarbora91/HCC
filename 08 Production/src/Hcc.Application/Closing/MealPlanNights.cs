using Hcc.Application.Billing;

namespace Hcc.Application.Closing;

/// <summary>
/// MA-11 (MA-2 gap): the close's night step posts the meal-plan lines of the night right after the room nights (prototype: the night posting
/// adds the plan line with each room night). Implemented by the Catalogue module (PostMealPlanNights, idempotent per folio and date through
/// bill.meal_plan_night). Returns the number of plan lines posted now.
/// </summary>
public interface IMealPlanNightPoster
{
    Task<int> PostAsync(Caller caller, DateOnly serviceDate, string idempotencyKey, CancellationToken ct = default);
}
