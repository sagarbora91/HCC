using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Reservations;
using Hcc.Contracts.Masters;
using Hcc.Domain.FrontDeskMasters;
using Hcc.Domain.Masters;
using Hcc.Infrastructure.SqlServer.Commands;

namespace Hcc.Infrastructure.SqlServer.FrontDeskMasters;

/// <summary>MA-11: the booking source of a new reservation resolved against mst.booking_source (2850); a retired row is refused.</summary>
public sealed class SqlBookingSourceCheck : IBookingSourceCheck
{
    public async Task EnsureUsableAsync(ICommandTransaction tx, int propertyId, string source, CancellationToken ct = default)
    {
        var sql = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The booking-source check needs the SQL command transaction.");
        await using var cmd = sql.Command("SELECT code, name, segment, channel, active FROM mst.booking_source WHERE property_id = @p ORDER BY sort_order, code;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        var rows = new List<BookingSourceRow>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new BookingSourceRow(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetBoolean(4)));
        if (FrontDeskMastersRules.SourceRowOf(rows, source) is { Active: false } r)
            throw MastersRules.Fail(MasterReasons.Retired, $"{r.Name} is retired — choose an active booking source.", "source");
    }
}
