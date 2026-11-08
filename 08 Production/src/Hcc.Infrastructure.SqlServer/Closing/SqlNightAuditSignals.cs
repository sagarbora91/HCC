using System.Data;
using Hcc.Application.Closing;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Closing;

/// <summary>
/// F8 (f8-int, QW-NA wiring): the two extra night-audit signals the pure rules need. Keys: rooms of stays checked out on the business date
/// that still hold an <c>active</c> or <c>manual</c> card (<c>ops.key_card</c>, migration 4720; the prototype's <c>naDepartedWithKeys</c>).
/// Backup: the last successful backup (<c>plat.backup_run</c>, migration 3920, same query as System health) in hotel local time.
/// Read only; never blocks the close.
/// </summary>
public sealed class SqlNightAuditSignals(SqlConnectionFactory connections)
{
    private static readonly TimeZoneInfo Hotel = Zone();

    private static TimeZoneInfo Zone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }

    public async Task<NightAuditSignals> ReadAsync(int propertyId, DateOnly businessDate, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT COUNT(DISTINCT kc.room_id)
              FROM ops.key_card kc
              JOIN gst.stay s ON s.reservation_id = kc.reservation_id
             WHERE kc.property_id = @p AND kc.status IN ('active', 'manual')
               AND s.status = 'CheckedOut' AND s.checked_out_date = @d;
            SELECT MAX(finished_utc) FROM plat.backup_run WHERE kind <> 'RestoreDrill' AND succeeded = 1;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var keys = await rd.ReadAsync(ct).ConfigureAwait(false) ? rd.GetInt32(0) : 0;
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        DateTime? backup = null;
        if (await rd.ReadAsync(ct).ConfigureAwait(false) && !rd.IsDBNull(0))
            backup = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(rd.GetDateTime(0), DateTimeKind.Utc), Hotel);
        return new NightAuditSignals(keys, backup);
    }
}
