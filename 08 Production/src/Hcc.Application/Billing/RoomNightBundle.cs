using Hcc.Application.Commands;
using Hcc.Domain.Money;

namespace Hcc.Application.Billing;

/// <summary>
/// MA-11 (MA-2 gap): the add-on of a meal plan bundled with the room ("bundled with room" GST treatment — illustrative, CA to confirm) for a
/// folio's room night. PostRoomNight takes the room-night slab on room + meals (one composite supply, the room the principal supply), as the
/// prototype's catPlanNight gives the new room-night line the slab of room + meals; the plan line itself is posted by PostMealPlanNights with
/// the same slab. Zero when the booking has no plan, a separate plan, or a zero add-on. Runs inside the posting transaction.
/// </summary>
public interface IRoomNightBundle
{
    Task<Paise> BundledAddOnAsync(ICommandTransaction tx, long folioId, DateOnly serviceDate, CancellationToken ct = default);
}
