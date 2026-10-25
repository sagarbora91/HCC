using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Yield;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Yield;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Yield;

/// <summary>
/// <see cref="IRatePublicationPort"/> over P2's tables: effective-dated BAR rows in <c>inv.room_type_tariff</c> (read by GetAvailability / QuoteStay
/// through <c>IInventoryStore.GetTariffsAsync</c>) and house restrictions in <c>inv.stay_restriction</c> (read by the reservation restriction check).
/// Runs inside the caller's command transaction. ETP-REUSE: none. When P2 gains its own tariff/restriction writer, swap this registration for it.
/// </summary>
public sealed class SqlRatePublicationPort(SqlConnectionFactory connections) : IRatePublicationPort
{
    public async Task WriteTariffsAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<TariffRow> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var s = SqlScope.Of(tx, connections);
        foreach (var r in rows.OrderBy(x => x.RoomTypeId).ThenBy(x => x.EffectiveFrom))
        {
            await using var q = s.Command("""
                IF NOT EXISTS (SELECT 1 FROM inv.room_type WHERE room_type_id = @t AND property_id = @p) THROW 52011, 'Unknown room type.', 1;
                UPDATE inv.room_type_tariff SET rate_paise = @r WHERE room_type_id = @t AND effective_from = @d;
                IF @@ROWCOUNT = 0 INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES (@t, @d, @r);
                """);
            q.Parameters.Int("@p", propertyId);
            q.Parameters.Int("@t", r.RoomTypeId);
            q.Parameters.Date("@d", r.EffectiveFrom);
            q.Parameters.Big("@r", r.RatePaise);
            try
            {
                await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number == 52011)
            {
                throw HccException.Validation("Unknown room type.");
            }
        }
    }

    public async Task UpsertRestrictionAsync(ICommandTransaction tx, int propertyId, YieldRestrictionDef restriction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(restriction);
        var s = SqlScope.Of(tx, connections);
        await using var q = s.Command("""
            UPDATE inv.stay_restriction SET room_type_id = @t, arrival_from = @f, arrival_to = @to, min_stay = @m, closed_to_arrival = @c, note = @n, active = 1
              WHERE property_id = @p AND code = @code;
            IF @@ROWCOUNT = 0 INSERT inv.stay_restriction(property_id, code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note, active)
              VALUES (@p, @code, @t, @f, @to, @m, @c, @n, 1);
            """);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = restriction.Code;
        q.Parameters.Int("@t", restriction.RoomTypeId);
        q.Parameters.Date("@f", restriction.From);
        q.Parameters.Date("@to", restriction.To);
        q.Parameters.Add("@m", SqlDbType.TinyInt).Value = (byte)restriction.MinStay;
        q.Parameters.Add("@c", SqlDbType.Bit).Value = restriction.ClosedToArrival;
        q.Parameters.Text("@n", restriction.Note, 200);
        await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveRestrictionAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        await using var q = s.Command("UPDATE inv.stay_restriction SET active = 0 WHERE property_id = @p AND code = @code;");
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = code;
        await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
