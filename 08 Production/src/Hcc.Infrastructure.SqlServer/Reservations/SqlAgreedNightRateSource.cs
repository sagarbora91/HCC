using Hcc.Application.Commands;
using Hcc.Application.Reservations;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;

namespace Hcc.Infrastructure.SqlServer.Reservations;

/// <summary>SQL adapter for <see cref="IAgreedNightRateSource"/> over <c>inv.reservation_night</c>.</summary>
public sealed class SqlAgreedNightRateSource(SqlConnectionFactory connections) : IAgreedNightRateSource
{
    public async Task<AgreedNight?> GetAsync(ICommandTransaction tx, int propertyId, long reservationId, DateOnly night, CancellationToken ct = default)
    {
        await using var q = SqlScope.Of(tx, connections).Command("""
            SELECT n.rate_paise, n.tax_bps, n.tax_paise FROM inv.reservation_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id
            WHERE n.reservation_id = @r AND r.property_id = @p AND n.night = @n;
            """);
        q.Parameters.Big("@r", reservationId);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Date("@n", night);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new AgreedNight(reservationId, night, rd.GetInt64(0), rd.GetInt32(1), rd.GetInt64(2));
    }
}
