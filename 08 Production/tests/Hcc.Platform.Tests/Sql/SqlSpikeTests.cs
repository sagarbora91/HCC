using System.Data;
using System.Diagnostics;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Sql;
using Hcc.Platform.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Platform.Tests.Sql;

/// <summary>Production migrations + the A3 spike procedures in one disposable database; the spike T-script runs once at start.</summary>
public sealed class SpikeDatabaseFixture : IAsyncLifetime
{
    private DisposableDatabase? _db;

    public DisposableDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public IReadOnlyList<string> TScriptOutput { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        _db = await DisposableDatabase.CreateAsync(migrate: true);
        await using var conn = await _db.OpenAsync();
        foreach (var batch in SqlBatches.Split(Read("spike-procedures.sql")))
        {
            await using var cmd = new SqlCommand(batch, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 02-tests.sql through SqlClient: each test PRINTs "PASS Txx …" or "FAIL Txx …".
        var lines = new List<string>();
        conn.InfoMessage += (_, e) => lines.AddRange(e.Errors.Cast<SqlError>().Select(x => x.Message));
        foreach (var batch in SqlBatches.Split(Read("02-tests.sql")))
        {
            await using var cmd = new SqlCommand(batch, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        TScriptOutput = lines;
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SqlSpike", file));
}

/// <summary>
/// Rerun of the A3 SQL spike (T01–T10, C1–C4; 07 Integration Lab/evidence/sql-spike-results.json) through
/// Microsoft.Data.SqlClient against the production migrations, plus the .NET adapter checks contracts §5 left open
/// (BIGINT, Unicode, rowversion, DATE, TLS, connection policy).
/// </summary>
public class SqlSpikeTests(SpikeDatabaseFixture f) : IClassFixture<SpikeDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> TIds => new(Enumerable.Range(1, 10).Select(i => $"T{i:00}"));

    [Theory]
    [MemberData(nameof(TIds))]
    public void Spike_T_test_passes_through_SqlClient(string id)
    {
        var line = f.TScriptOutput.SingleOrDefault(l => l.StartsWith($"PASS {id} ", StringComparison.Ordinal) || l.StartsWith($"FAIL {id} ", StringComparison.Ordinal));
        Assert.NotNull(line);
        Assert.StartsWith("PASS", line, StringComparison.Ordinal);
    }

    private async Task<(bool Ok, int Number, long Ms)> ExecAsync(string sql)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = await f.Db.OpenAsync(Ct);
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
            await cmd.ExecuteNonQueryAsync(Ct);
            return (true, 0, sw.ElapsedMilliseconds);
        }
        catch (SqlException ex)
        {
            return (false, ex.Number, sw.ElapsedMilliseconds);
        }
    }

    [Fact]
    public async Task C1_overlapping_booking_blocks_then_loses_with_PK_violation()
    {
        var a = ExecAsync("EXEC inv.usp_book 1, 2, '2026-11-01', '2026-11-03', N'Racer A', 'C1A', 1, '00:00:03.000'");
        await Task.Delay(700, Ct);
        var b = await ExecAsync("EXEC inv.usp_book 1, 2, '2026-11-02', '2026-11-04', N'Racer B', 'C1B'");
        var ra = await a;
        Assert.True(ra.Ok);
        Assert.False(b.Ok);
        Assert.Equal(SqlErrorTranslator.DuplicateKey, b.Number);
        Assert.True(b.Ms >= 1500, $"B should have waited for A's locks (waited {b.Ms} ms)");
        Assert.Equal(2, await f.Db.CountAsync("SELECT COUNT(*) FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id WHERE n.room_id = 2 AND r.public_no = 'C1A'", Ct));
        Assert.Equal(0, await f.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation WHERE public_no = 'C1B'", Ct));
    }

    [Fact]
    public async Task C2_rolled_back_holder_releases_inventory_to_the_waiter()
    {
        var a = ExecAsync("BEGIN TRAN; EXEC inv.usp_book 1, 3, '2026-11-01', '2026-11-03', N'Rollback A', 'C2A', 1, '00:00:02.000'; ROLLBACK;");
        await Task.Delay(700, Ct);
        var b = await ExecAsync("EXEC inv.usp_book 1, 3, '2026-11-01', '2026-11-03', N'Winner B', 'C2B'");
        Assert.True((await a).Ok);
        Assert.True(b.Ok);
        Assert.True(b.Ms >= 1000, $"B should have waited ({b.Ms} ms)");
        Assert.Equal("C2B", await f.Db.ScalarAsync("SELECT DISTINCT r.public_no FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id WHERE n.room_id = 3 AND n.night = '2026-11-01'", Ct));
    }

    [Fact]
    public async Task C3_concurrent_refunds_cannot_exceed_the_remainder()
    {
        await f.Db.ExecAsync("EXEC inv.usp_book 1, 3, '2026-12-10', '2026-12-11', N'Refund guest', 'C3'", Ct);
        var rid = Convert.ToInt64(await f.Db.ScalarAsync("""
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date)
            OUTPUT INSERTED.receipt_id
            SELECT f.folio_id, 'payment', 'UPI', 100000, '2026-10-24' FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'C3';
            """, Ct), System.Globalization.CultureInfo.InvariantCulture);
        var a = ExecAsync($"EXEC pay.usp_refund {rid}, 75000, '2026-10-24', '00:00:03.000'");
        await Task.Delay(700, Ct);
        var b = await ExecAsync($"EXEC pay.usp_refund {rid}, 75000, '2026-10-24'");
        Assert.True((await a).Ok);
        Assert.False(b.Ok);
        Assert.Equal(50010, b.Number);
        Assert.Equal(75000L, await f.Db.ScalarAsync($"SELECT SUM(amount_paise) FROM pay.receipt WHERE refund_of = {rid}", Ct));
    }

    [Fact]
    public async Task C4_twelve_parallel_bookings_one_winner_no_deadlock()
    {
        var runs = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => ExecAsync($"EXEC inv.usp_book 1, 1, '2026-12-01', '2026-12-03', N'Rush {i}', 'C4-{i}'")));
        Assert.Equal(1, runs.Count(r => r.Ok));
        Assert.Equal(0, runs.Count(r => r.Number == SqlErrorTranslator.Deadlock));
        Assert.All(runs.Where(r => !r.Ok), r => Assert.Equal(SqlErrorTranslator.DuplicateKey, r.Number));
        Assert.Equal(1, await f.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation WHERE public_no LIKE 'C4-%'", Ct));
        Assert.Equal(2, await f.Db.CountAsync("SELECT COUNT(*) FROM inv.room_night WHERE room_id = 1 AND night IN ('2026-12-01', '2026-12-02')", Ct));
    }

    [Fact]
    public async Task Adapter_unicode_bigint_date_parameters_round_trip()
    {
        const string guest = "सागर बोरा · Ünïcødé";
        await using var conn = await f.Db.OpenAsync(Ct);
        await using (var book = new SqlCommand("inv.usp_book", conn) { CommandType = CommandType.StoredProcedure })
        {
            book.Parameters.Add("@property_id", SqlDbType.Int).Value = 1;
            book.Parameters.Add("@room_id", SqlDbType.Int).Value = 2;
            book.Parameters.Add("@arrival", SqlDbType.Date).Value = new DateOnly(2027, 1, 10);
            book.Parameters.Add("@departure", SqlDbType.Date).Value = new DateOnly(2027, 1, 12);
            book.Parameters.Add("@guest", SqlDbType.NVarChar, 200).Value = guest;
            book.Parameters.Add("@public_no", SqlDbType.VarChar, 20).Value = "A01";
            await book.ExecuteNonQueryAsync(Ct);
        }

        await using (var q = new SqlCommand("SELECT guest_name, arrival, departure FROM inv.reservation WHERE public_no = 'A01'", conn))
        await using (var rd = await q.ExecuteReaderAsync(Ct))
        {
            Assert.True(await rd.ReadAsync(Ct));
            Assert.Equal(guest, rd.GetString(0));
            Assert.Equal(new DateOnly(2027, 1, 10), rd.GetFieldValue<DateOnly>(1));
            Assert.Equal(new DateOnly(2027, 1, 12), rd.GetFieldValue<DateOnly>(2));
        }

        // BIGINT paise at the ₹10 crore limit round-trips as Int64 and passes the adapter's safe-integer check; one paisa more is refused by the CHECK.
        await using (var ins = new SqlCommand("""
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date)
            OUTPUT INSERTED.amount_paise
            SELECT f.folio_id, 'payment', 'Bank', @amt, @d FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'A01';
            """, conn))
        {
            ins.Parameters.Add("@amt", SqlDbType.BigInt).Value = ApiConventions.MaxPaisePerField;
            ins.Parameters.Add("@d", SqlDbType.Date).Value = new DateOnly(2026, 10, 24);
            await using var rd = await ins.ExecuteReaderAsync(Ct);
            Assert.True(await rd.ReadAsync(Ct));
            Assert.Equal(Paise.MaxPerField, SqlValues.ReadPaise(rd, 0));
        }

        await using var over = new SqlCommand("""
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date)
            SELECT f.folio_id, 'payment', 'Bank', @amt, '2026-10-24' FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'A01';
            """, conn);
        over.Parameters.Add("@amt", SqlDbType.BigInt).Value = ApiConventions.MaxPaisePerField + 1;
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => over.ExecuteNonQueryAsync(Ct))).Number);
    }

    [Fact]
    public async Task Adapter_rowversion_wire_token_detects_stale_writes()
    {
        await f.Db.ExecAsync("EXEC inv.usp_book 1, 3, '2027-02-01', '2027-02-02', N'Versioned', 'A02'", Ct);
        await using var conn = await f.Db.OpenAsync(Ct);
        byte[] v;
        await using (var q = new SqlCommand("SELECT row_ver FROM inv.reservation WHERE public_no = 'A02'", conn))
            v = (byte[])(await q.ExecuteScalarAsync(Ct))!;
        var token = SqlValues.RowVersionToWire(v);

        async Task<int> UpdateAsync(string name)
        {
            await using var u = new SqlCommand("UPDATE inv.reservation SET guest_name = @n WHERE public_no = 'A02' AND row_ver = @v", conn);
            u.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = name;
            u.Parameters.Add("@v", SqlDbType.Timestamp).Value = SqlValues.RowVersionFromWire(token);
            return await u.ExecuteNonQueryAsync(Ct);
        }

        SqlValues.EnsureVersionMatched(await UpdateAsync("Edited"), "Reservation");
        var stale = Assert.Throws<HccException>(() => SqlValues.EnsureVersionMatched(UpdateAsync("Stale").GetAwaiter().GetResult(), "Reservation"));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        Assert.Equal("Edited", await f.Db.ScalarAsync("SELECT guest_name FROM inv.reservation WHERE public_no = 'A02'", Ct));
    }

    [Fact]
    public async Task Adapter_room_night_posting_replay_returns_existing_charge()
    {
        await f.Db.ExecAsync("EXEC inv.usp_book 1, 2, '2027-03-01', '2027-03-02', N'Night post', 'A03'", Ct);
        var folio = Convert.ToInt64(await f.Db.ScalarAsync("SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'A03'", Ct), System.Globalization.CultureInfo.InvariantCulture);
        async Task<(long Id, bool Created)> PostAsync()
        {
            await using var conn = await f.Db.OpenAsync(Ct);
            await using var cmd = new SqlCommand("bill.usp_post_room_night", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@folio_id", SqlDbType.BigInt).Value = folio;
            cmd.Parameters.Add("@service_date", SqlDbType.Date).Value = new DateOnly(2027, 3, 1);
            cmd.Parameters.Add("@amount", SqlDbType.BigInt).Value = 200000L;
            cmd.Parameters.Add("@tax_bps", SqlDbType.Int).Value = 1000;
            var id = cmd.Parameters.Add("@charge_id", SqlDbType.BigInt);
            id.Direction = ParameterDirection.Output;
            var created = cmd.Parameters.Add("@created", SqlDbType.Bit);
            created.Direction = ParameterDirection.Output;
            await cmd.ExecuteNonQueryAsync(Ct);
            return ((long)id.Value, (bool)created.Value);
        }

        var first = await PostAsync();
        var replay = await PostAsync();
        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(20000L, await f.Db.ScalarAsync($"SELECT tax_paise FROM bill.charge WHERE charge_id = {first.Id}", Ct));
        Assert.Equal(20000L, MoneyMath.TaxOf(new Paise(200000), 1000).Value);
    }

    [Fact]
    public async Task Connection_policy_is_encrypted_with_quoted_identifier_and_xact_abort_on()
    {
        await using var conn = await f.Db.OpenAsync(Ct);
        await using var q = new SqlCommand("""
            SELECT c.encrypt_option, CAST(SESSIONPROPERTY('QUOTED_IDENTIFIER') AS int), CASE WHEN (@@OPTIONS & 16384) = 16384 THEN 1 ELSE 0 END
            FROM sys.dm_exec_connections c WHERE c.session_id = @@SPID;
            """, conn);
        await using var rd = await q.ExecuteReaderAsync(Ct);
        Assert.True(await rd.ReadAsync(Ct));
        Assert.Equal("TRUE", rd.GetString(0));
        Assert.Equal(1, rd.GetInt32(1));
        Assert.Equal(1, rd.GetInt32(2)); // XACT_ABORT ON
    }
}
