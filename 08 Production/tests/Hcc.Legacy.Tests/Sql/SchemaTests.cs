using Hcc.Infrastructure.SqlServer.Customers;
using Microsoft.Data.SqlClient;

namespace Hcc.Legacy.Tests.Sql;

/// <summary>
/// Migration 4600 / 4601 on a fresh HccTest_* database (LG-HISTORY-CONTRACT §2, §3, §10, §11): the lgc schema exists, the view returns
/// non-duplicate stays with their bill, the guest columns and the unique legacy index hold, PERM-LGC-001 is Owner only. Fictional rows only.
/// </summary>
[Trait("Category", "Sql")]
public class SchemaTests : IAsyncLifetime
{
    private LegacyDb _db = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _db = await LegacyDb.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private int P => _db.PropertyId;

    private string Stay(string fy, long booking, string key, string arrival, string? departure, int nights, string status, int? billNo = null) => $"""
        INSERT lgc.stay(property_id, legacy_fy, legacy_booking_id, legacy_guest_id, customer_key, guest_name_snapshot, arrival_date, arrival_utc, departure_date, departure_utc,
                        nights, adults, children, room_no, room_count, status, legacy_bill_no)
        VALUES ({P}, '{fy}', {booking}, 7001, '{key}', N'Test Guest', '{arrival}', DATEADD(MINUTE, -330, CAST('{arrival}T12:00:00' AS DATETIME2(0))),
                {(departure is null ? "NULL, NULL" : $"'{departure}', DATEADD(MINUTE, -330, CAST('{departure}T10:00:00' AS DATETIME2(0)))")},
                {nights}, 2, 0, '101', 1, '{status}', {(billNo is null ? "NULL" : billNo.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))});
        """;

    private string Bill(string fy, int billNo, long booking, long netPaise) => $"""
        INSERT lgc.bill(property_id, legacy_fy, legacy_bill_no, legacy_transaction_id, stay_id, legacy_guest_id, customer_key, bill_date, bill_utc, room_no, nights,
                        room_charge_paise, extra_paise, fb_paise, laundry_paise, other_paise, discount_paise, room_gst_bps, room_gst_paise, fb_gst_paise, laundry_gst_paise,
                        other_gst_paise, advance_paise, net_paise, balance_paise, refund_paise, data_flag, paid_flag, b2b, out_of_fy)
        SELECT {P}, '{fy}', {billNo}, {billNo + 50000}, stay_id, 7001, customer_key, departure_date, departure_utc, '101', nights,
               {netPaise - netPaise / 21}, 0, 0, 0, 0, 0, 500, {netPaise / 21}, 0, 0, 0, 0, {netPaise}, 0, 0, 'C', 'Y', 0, 0
          FROM lgc.stay WHERE property_id = {P} AND legacy_fy = '{fy}' AND legacy_booking_id = {booking};
        """;

    [Fact]
    public async Task Migration_creates_the_lgc_tables_the_view_and_the_guest_columns()
    {
        foreach (var t in new[] { "import_run", "guest_map", "stay", "room_segment", "bill", "receipt" })
            Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('lgc') AND name = '{t}';", Ct));
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM sys.views WHERE schema_id = SCHEMA_ID('lgc') AND name = 'v_customer_stay';", Ct));
        Assert.Equal(2, await _db.CountAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('gst.guest') AND name IN ('legacy_guest_id','dob') AND is_nullable = 1;", Ct));
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('gst.guest') AND name = 'UX_guest_legacy' AND is_unique = 1 AND has_filter = 1;", Ct));
    }

    [Fact]
    public async Task View_returns_non_duplicate_stays_with_their_bill_and_the_read_store_rolls_them_up()
    {
        await _db.ExecAsync(Stay("2024-25", 11, "9000012345", "2024-06-01", "2024-06-03", 2, "CheckedOut", 501) + Bill("2024-25", 501, 11, 630_000), Ct);
        await _db.ExecAsync(Stay("2025-26", 12, "9000012345", "2025-04-10", "2025-04-11", 1, "CheckedOut", 77) + Bill("2025-26", 77, 12, 315_000), Ct);
        await _db.ExecAsync(Stay("2025-26", 13, "9000012345", "2026-03-30", null, 2, "OpenAtBackup"), Ct);
        await _db.ExecAsync(Stay("2024-25", 99, "9000012345", "2025-04-10", "2025-04-11", 1, "Duplicate"), Ct);   // dropped copy (contract §7 rule 2)
        await _db.ExecAsync(Stay("2025-26", 14, "n:other guest", "2025-05-01", "2025-05-02", 1, "CheckedOut"), Ct);
        Assert.Equal(4, await _db.CountAsync($"SELECT COUNT(*) FROM lgc.v_customer_stay WHERE property_id = {P};", Ct));
        Assert.Equal(945_000, Convert.ToInt64(await _db.ScalarAsync($"SELECT SUM(net_paise) FROM lgc.v_customer_stay WHERE customer_key = '9000012345';", Ct)));

        var store = new SqlCustomerReadStore(_db.Platform.Connections);
        var stays = await store.LegacyStaysAsync(P, "9000012345", Ct);
        Assert.Equal([13L, 12L, 11L], stays.Select(s => s.LegacyBookingId));   // newest arrival first, Duplicate hidden
        Assert.Null(stays[0].Departure);
        Assert.Null(stays[0].NetPaise);
        Assert.Equal(501, stays[2].LegacyBillNo);
        Assert.Equal(630_000, stays[2].NetPaise);
        var roll = await store.LegacyRollupAsync(P, ["9000012345", "n:other guest", "9999999999"], Ct);
        Assert.Equal(2, roll.Count);
        var r = roll["9000012345"];
        Assert.Equal((3, 5, 945_000L, new DateOnly(2024, 6, 1), new DateOnly(2026, 3, 30)), r);
        Assert.Equal(1, roll["n:other guest"].Stays);
        Assert.Empty(await store.LegacyRollupAsync(P, [], Ct));
    }

    [Fact]
    public async Task Unique_index_refuses_two_guests_with_one_legacy_id_and_keys_are_unique_per_fy()
    {
        const string guest = "INSERT gst.guest(property_id, full_name, phone, verified, origin, created_utc, legacy_guest_id, dob) VALUES ({0}, N'Test Guest', '9000012345', 0, 'manual', '2026-10-01', {1}, '1985-03-12');";
        await _db.ExecAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, guest, P, "4242"), Ct);
        await _db.ExecAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, guest, P, "NULL"), Ct);
        await _db.ExecAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, guest, P, "NULL"), Ct);   // NULLs never collide (filtered index)
        await Assert.ThrowsAsync<SqlException>(() => _db.ExecAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, guest, P, "4242"), Ct));
        Assert.Equal(3, await _db.CountAsync("SELECT COUNT(*) FROM gst.guest;", Ct));

        await _db.ExecAsync(Stay("2025-26", 21, "9000012345", "2025-07-01", "2025-07-02", 1, "CheckedOut"), Ct);
        await Assert.ThrowsAsync<SqlException>(() => _db.ExecAsync(Stay("2025-26", 21, "9000012345", "2025-07-01", "2025-07-02", 1, "CheckedOut"), Ct));
        await _db.ExecAsync(Stay("2026-27", 21, "9000012345", "2026-07-01", "2026-07-02", 1, "CheckedOut"), Ct);   // same booking id, other FY
        await Assert.ThrowsAsync<SqlException>(() => _db.ExecAsync(Stay("2025-26", 22, "9000012345", "2025-07-01", "2025-07-02", 1, "Cancelled"), Ct));   // status CHECK
    }

    [Fact]
    public async Task Legacy_import_permission_is_seeded_for_the_owner_only()
    {
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM plat.permission WHERE permission_id = 'PERM-LGC-001' AND is_money = 1;", Ct));
        Assert.Equal("OWN", await _db.ScalarAsync("SELECT STRING_AGG(role_code, ',') FROM plat.role_grant WHERE permission_id = 'PERM-LGC-001';", Ct));
    }
}
