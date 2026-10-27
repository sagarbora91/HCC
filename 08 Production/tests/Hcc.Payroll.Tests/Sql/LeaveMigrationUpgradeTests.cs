using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>
/// SCC leave port (DEC-116), migration 4110 on a database from before it: every stored leave SPAN becomes one record per date it counted when it was
/// stored (the old rule: rostered working days that are not paid holidays) — so balances and pay stay what they were; the first counted date keeps the
/// row and its id; a span that counted no date keeps its first date (nothing is dropped). Policies, department caps and leave types are widened and
/// carry-forward / encashment cleared; OTH is added to every stored list.
/// </summary>
[Trait("Category", "Sql")]
public sealed partial class LeaveMigrationUpgradeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private sealed class FilteredSource(Func<int, bool> keep) : IMigrationSource
    {
        public IReadOnlyList<MigrationScript> Load() => new EmbeddedMigrationSource().Load().Where(s => keep(s.Version)).ToList();
    }

    private sealed class Db : IAsyncDisposable
    {
        private readonly string _server;

        public string Name { get; }

        public SqlPlatform Platform { get; }

        private Db(string server, string name)
        {
            (_server, Name) = (server, name);
            Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        }

        public static async Task<Db> CreateAsync()
        {
            var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
            var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
            if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
            await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master"));
            await conn.OpenAsync(Ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(Ct);
            return new Db(server, name);
        }

        public MigrationRunner Runner(Func<int, bool> keep) => new(Platform.Connections, new FilteredSource(keep), Platform.BackupGate);

        public async Task<object?> ScalarAsync(string sql)
        {
            await using var conn = await Platform.Connections.OpenAsync(Ct);
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
            return await cmd.ExecuteScalarAsync(Ct);
        }

        public async Task<int> CountAsync(string sql) => Convert.ToInt32(await ScalarAsync(sql), CultureInfo.InvariantCulture);

        public async Task<string> TextAsync(string sql) => Convert.ToString(await ScalarAsync(sql), CultureInfo.InvariantCulture) ?? "";

        public async ValueTask DisposeAsync()
        {
            if (!NamePattern().IsMatch(Name)) return;
            SqlConnection.ClearAllPools();
            await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END", conn)
                { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Migration_4110_splits_old_spans_into_the_dates_they_counted_and_keeps_every_record_balance_and_setting()
    {
        await using var old = await Db.CreateAsync();
        var first = await old.Runner(v => v < 4110).MigrateAsync(Ct);
        Assert.DoesNotContain(4110, first.Applied);
        var p = await old.CountAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('LVU', N'Leave Upgrade (fictional)', 'Asia/Kolkata', '2026-10-24');");
        await old.ScalarAsync($"INSERT hr.department(property_id, dept_code, name, leave_cap) VALUES ({p}, 'HK', N'Housekeeping', 2);");
        var s = await old.CountAsync($"""
            INSERT hr.staff(property_id, name, dept_code, designation, pay_type, monthly_paise, daily_paise, default_shift, weekly_off, joined, active, phone, created_by, created_utc)
            OUTPUT INSERTED.staff_id VALUES ({p}, N'Upgrade Asha (fictional)', 'HK', N'Attendant', 'monthly', 1800000, 0, 'M', 2, '2025-04-01', 1, '90000 12345', 1, '2026-01-01');
            """);
        // Tuesday is Asha's weekly off; Tue 17 Nov she is rostered to work, Wed 18 and Wed 25 Nov are rostered off; Thu 12 Nov is a paid holiday.
        await old.ScalarAsync($"""
            INSERT hr.roster(staff_id, work_date, shift_code, set_by, set_utc) VALUES ({s}, '2026-11-17', 'M', 1, '2026-10-20'), ({s}, '2026-11-18', 'OFF', 1, '2026-10-20'),
                                                                               ({s}, '2026-11-25', 'OFF', 1, '2026-10-20');
            INSERT hr.holiday(property_id, code, name, kind, sort_order, active, retired_on, updated_by, updated_utc) VALUES ({p}, '2026-11-12', N'Founders day (fictional)', 'paid', 10, 1, NULL, 1, '2026-10-01');
            INSERT hr.leave_policy(property_id, cl_days, sl_days, el_days, default_cap, updated_by, updated_utc) VALUES ({p}, 10, 7, 15, 2, 1, '2026-10-01');
            INSERT hr.leave_type(property_id, code, name, paid, yearly_quota, carry_forward, encashment, sort_order, active, retired_on, updated_by, updated_utc)
            VALUES ({p}, 'CL', N'Casual leave', 1, 12, 3, 1, 10, 1, NULL, 1, '2026-10-01'), ({p}, 'LOP', N'Leave without pay', 0, NULL, 0, 0, 60, 1, NULL, 1, '2026-10-01');
            INSERT hr.leave_request(property_id, staff_id, leave_type, from_date, to_date, half, days_x2, reason, status, requested_by, requested_utc, decided_by, decided_utc) VALUES
              ({p}, {s}, 'CL',  '2026-11-09', '2026-11-13', NULL, 6, N'Family function', 'Approved', 1, '2026-10-20', 2, '2026-10-21'),
              ({p}, {s}, 'LOP', '2026-11-16', '2026-11-18', NULL, 4, N'Personal', 'Pending', 1, '2026-10-20', NULL, NULL),
              ({p}, {s}, 'SL',  '2026-11-20', '2026-11-20', 'PM', 1, N'Doctor', 'Pending', 1, '2026-10-20', NULL, NULL),
              ({p}, {s}, 'CL',  '2026-11-24', '2026-11-25', NULL, 2, N'Counted nothing later', 'Rejected', 1, '2026-10-20', 2, '2026-10-21'),
              ({p}, {s}, 'EL',  '2026-11-26', '2026-11-26', NULL, 2, N'One day', 'Approved', 1, '2026-10-20', 2, '2026-10-21');
            """);
        var a = await old.CountAsync($"SELECT leave_id FROM hr.leave_request WHERE staff_id = {s} AND from_date = '2026-11-09';");

        var up = await old.Platform.Migrations.MigrateAsync(Ct);
        Assert.Contains(4110, up.Applied);
        Assert.Empty((await old.Platform.Migrations.MigrateAsync(Ct)).Applied);   // re-run is a no-op

        // One record per date: A → Mon 9, Wed 11, Fri 13 (Tue off, Thu holiday); B → Mon 16, Tue 17 (rostered to work), not Wed 18 (rostered off);
        // C (a half day) and E (one day) unchanged; D counted no date → its first date, nothing dropped.
        Assert.Equal("2026-11-09 CL Approved 2 2026-11-09..2026-11-13|2026-11-11 CL Approved 2 2026-11-09..2026-11-13|2026-11-13 CL Approved 2 2026-11-09..2026-11-13|"
            + "2026-11-16 LOP Pending 2 2026-11-16..2026-11-18|2026-11-17 LOP Pending 2 2026-11-16..2026-11-18|2026-11-20 SL Pending 1 2026-11-20..2026-11-20|"
            + "2026-11-24 CL Rejected 2 2026-11-24..2026-11-25|2026-11-26 EL Approved 2 2026-11-26..2026-11-26",
            await old.TextAsync($"""
                SELECT STRING_AGG(CONCAT(CONVERT(VARCHAR(10), from_date, 23), ' ', leave_type, ' ', status, ' ', days_x2, ' ', CONVERT(VARCHAR(10), request_from, 23), '..',
                                         CONVERT(VARCHAR(10), request_to, 23)), '|') WITHIN GROUP (ORDER BY from_date)
                  FROM hr.leave_request WHERE staff_id = {s};
                """));
        Assert.Equal(0, await old.CountAsync($"SELECT COUNT(*) FROM hr.leave_request WHERE staff_id = {s} AND to_date <> from_date;"));
        Assert.Equal(a, await old.CountAsync($"SELECT leave_id FROM hr.leave_request WHERE staff_id = {s} AND from_date = '2026-11-09';"));   // keeps its id
        // Balances and pay stay what they were: each old request's day units are unchanged (D was a rejected span, kept as its first date).
        Assert.Equal("CL 2026-11-09 6|LOP 2026-11-16 4|SL 2026-11-20 1|EL 2026-11-26 2", await old.TextAsync($"""
            SELECT STRING_AGG(CONCAT(leave_type, ' ', CONVERT(VARCHAR(10), request_from, 23), ' ', u), '|') WITHIN GROUP (ORDER BY request_from)
              FROM (SELECT leave_type, request_from, SUM(days_x2) AS u FROM hr.leave_request WHERE staff_id = {s} AND status <> 'Rejected' GROUP BY leave_type, request_from) x;
            """));
        Assert.Equal(3, await old.CountAsync($"SELECT COUNT(*) FROM hr.leave_request WHERE staff_id = {s} AND decided_by = 2 AND status = 'Approved' AND request_from = '2026-11-09';"));
        Assert.Equal(1, await old.CountAsync($"SELECT COUNT(*) FROM hr.leave_request WHERE staff_id = {s} AND reason IS NOT NULL AND legacy = 0 AND approver_id IS NULL AND from_date = '2026-11-20';"));

        // Settings: decimals and the week-off set (none); advisory department caps (0 and above); SCC's fixed list (OTH added, no carry / encashment).
        Assert.Equal("10.00 7.00 15.00 0", await old.TextAsync($"SELECT CONCAT(cl_days, ' ', sl_days, ' ', el_days, ' ', weekoff_mask) FROM hr.leave_policy WHERE property_id = {p};"));
        await old.ScalarAsync($"UPDATE hr.leave_policy SET cl_days = 10.5, weekoff_mask = 65 WHERE property_id = {p}; UPDATE hr.department SET leave_cap = 0 WHERE property_id = {p};");
        Assert.Equal(2, await old.CountAsync($"SELECT COUNT(*) FROM hr.leave_type WHERE property_id = {p} AND carry_forward = 0 AND encashment = 0 AND code IN ('CL', 'LOP');"));
        Assert.Equal("OTH Other 1 70", await old.TextAsync($"SELECT CONCAT(code, ' ', name, ' ', CAST(paid AS INT), ' ', sort_order) FROM hr.leave_type WHERE property_id = {p} AND code = 'OTH';"));
        await Assert.ThrowsAnyAsync<SqlException>(() => old.ScalarAsync($"UPDATE hr.leave_type SET carry_forward = 2 WHERE property_id = {p} AND code = 'CL';"));
        await Assert.ThrowsAnyAsync<SqlException>(() => old.ScalarAsync($"UPDATE hr.leave_request SET to_date = DATEADD(DAY, 1, from_date) WHERE staff_id = {s};"));   // one date
        foreach (var t in new[] { "leave_ent_override", "leave_blackout", "leave_agenda" })
            Assert.Equal(1, await old.CountAsync($"SELECT COUNT(*) FROM sys.tables WHERE name = '{t}' AND schema_id = SCHEMA_ID('hr');"));
        Assert.Equal(0, await old.CountAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1"));
        Assert.Equal(0, await old.CountAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1"));
    }
}
