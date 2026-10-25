using System.Data;
using System.Globalization;
using Hcc.Domain.Dates;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Sql;
using Hcc.Platform.Tests.Golden;
using Hcc.Platform.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Platform.Tests.Sql;

/// <summary>
/// The BASE fixture's folio receipts persisted in the production schema (0002) through SqlClient; collections and the
/// expected drawer are then derived from the persisted rows and must equal the prototype's ₹21,500 / ₹10,750.
/// </summary>
public class GoldenSqlTests(MigratedDatabaseFixture f) : IClassFixture<MigratedDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BASE_receipts_persisted_in_SQL_reproduce_collections_and_drawer()
    {
        var g = GoldenFixture.Load().Base;
        await using var conn = await f.Db.OpenAsync(Ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(Ct);
        async Task<long> ScalarAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps)
        {
            await using var cmd = new SqlCommand(sql, conn, tx);
            foreach (var (n, t, v) in ps) cmd.Parameters.Add(n, t).Value = v;
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
        }

        var property = await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('GOLD', N'HCC Demo Residency', 'Asia/Kolkata', @d);",
            ("@d", SqlDbType.Date, g.Date));
        var folios = new Dictionary<string, long>();
        var n = 0;
        foreach (var bookingId in g.FolioReceipts.Select(r => r.BookingId!).Distinct())
        {
            var res = await ScalarAsync("""
                INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
                OUTPUT INSERTED.reservation_id VALUES (@p, @no, N'Fictional guest', @a, @dep, 'Confirmed');
                """, ("@p", SqlDbType.Int, (int)property), ("@no", SqlDbType.VarChar, bookingId), ("@a", SqlDbType.Date, g.Date.AddDays(-1 + (n++ % 2))), ("@dep", SqlDbType.Date, g.Date.AddDays(2)));
            folios[bookingId] = await ScalarAsync("INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES (@r);", ("@r", SqlDbType.BigInt, res));
        }

        foreach (var r in g.FolioReceipts)
        {
            await ScalarAsync("INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) OUTPUT INSERTED.receipt_id VALUES (@f, @k, @m, @a, @d);",
                ("@f", SqlDbType.BigInt, folios[r.BookingId!]), ("@k", SqlDbType.VarChar, r.Kind), ("@m", SqlDbType.VarChar, r.Method),
                ("@a", SqlDbType.BigInt, r.AmountPaise), ("@d", SqlDbType.Date, StayDates.Parse(r.Date)));
        }

        await tx.CommitAsync(Ct);

        // Read back through the adapter boundary and recompute with the kernel.
        var persisted = new List<DatedReceipt>();
        await using (var q = new SqlCommand("""
            SELECT r.business_date, r.method, r.kind, r.amount_paise FROM pay.receipt r
            JOIN bill.folio f ON f.folio_id = r.folio_id JOIN inv.reservation v ON v.reservation_id = f.reservation_id
            WHERE v.property_id = @p;
            """, conn))
        {
            q.Parameters.Add("@p", SqlDbType.Int).Value = (int)property;
            await using var rd = await q.ExecuteReaderAsync(Ct);
            while (await rd.ReadAsync(Ct))
                persisted.Add(new DatedReceipt(rd.GetFieldValue<DateOnly>(0), rd.GetString(1), FolioTotals.ParseKind(rd.GetString(2)), SqlValues.ReadPaise(rd, 3)));
        }

        Assert.Equal(g.FolioReceipts.Count, persisted.Count);
        Assert.Equal(2_150_000, CashFigures.Collections(persisted, g.Date).Value);
        Assert.Equal(1_075_000, CashFigures.ExpectedDrawer(new Paise(g.OpeningCashPaise), g.Date, persisted, g.Outlet, g.CashExpenses).Value);

        // Same figure computed set-based in T-SQL from the persisted rows.
        await using var sum = new SqlCommand("""
            SELECT SUM(CASE r.kind WHEN 'payment' THEN r.amount_paise WHEN 'refund' THEN -r.amount_paise ELSE 0 END)
            FROM pay.receipt r JOIN bill.folio f ON f.folio_id = r.folio_id JOIN inv.reservation v ON v.reservation_id = f.reservation_id
            WHERE v.property_id = @p AND r.business_date = @d;
            """, conn);
        sum.Parameters.Add("@p", SqlDbType.Int).Value = (int)property;
        sum.Parameters.Add("@d", SqlDbType.Date).Value = g.Date;
        Assert.Equal(2_150_000L, (long)(await sum.ExecuteScalarAsync(Ct))!);
    }
}
