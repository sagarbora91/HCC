using System.Data;
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;

namespace Hcc.Infrastructure.SqlServer.Catalogue;

/// <summary>MA-11: add-on x guests of the folio's bundled meal plan on the service date (bill.reservation_meal_plan + dated bill.meal_plan_price).</summary>
public sealed class SqlRoomNightBundle : IRoomNightBundle
{
    public async Task<Paise> BundledAddOnAsync(ICommandTransaction tx, long folioId, DateOnly serviceDate, CancellationToken ct = default)
    {
        var sql = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The meal-plan bundle needs the SQL command transaction.");
        await using var cmd = sql.Command("""
            SELECT ISNULL((SELECT TOP 1 pp.amount_paise FROM bill.meal_plan_price pp WHERE pp.meal_plan_id = m.meal_plan_id AND pp.effective_from <= @d
                           ORDER BY pp.effective_from DESC), 0) * CASE WHEN r.adults + r.children < 1 THEN 1 ELSE r.adults + r.children END
              FROM bill.folio f
              JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              JOIN bill.reservation_meal_plan rm ON rm.reservation_id = r.reservation_id
              JOIN bill.meal_plan m ON m.meal_plan_id = rm.meal_plan_id
             WHERE f.folio_id = @f AND m.gst_treatment = 'bundled';
            """);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = serviceDate.ToDateTime(TimeOnly.MinValue);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null or DBNull ? Paise.Zero : new Paise(Convert.ToInt64(v, CultureInfo.InvariantCulture));
    }
}
