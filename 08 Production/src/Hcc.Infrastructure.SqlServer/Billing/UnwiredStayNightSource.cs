using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Infrastructure.SqlServer.Billing;

/// <summary>
/// Placeholder <see cref="IStayNightSource"/> until stays (P3) and the agreed nightly rates (P2 quote nights) are merged:
/// PostRoomNight answers UNAVAILABLE instead of guessing a rate. Replaced by the P2/P3 implementation at integration.
/// </summary>
public sealed class UnwiredStayNightSource : IStayNightSource
{
    public Task<StayNight> ResolveAsync(ICommandTransaction tx, int propertyId, long stayId, DateOnly serviceDate, CancellationToken ct)
        => throw new HccException(HccErrorCode.UNAVAILABLE, "Room-night posting is not connected to stays yet (P2/P3 integration).");
}
