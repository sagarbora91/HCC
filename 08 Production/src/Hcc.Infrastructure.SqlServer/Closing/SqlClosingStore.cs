using System.Data;
using System.Globalization;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Closing;
using Hcc.Domain.Closing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Closing;

/// <summary>Runs a statement inside the command transaction when one is given, else on an own read-committed connection.</summary>
internal static class ClosingSql
{
    public static async Task<T> RunAsync<T>(SqlConnectionFactory connections, ICommandTransaction? tx, string sql, Func<SqlCommand, Task<T>> run,
        CancellationToken ct, params (string Name, SqlDbType Type, object? Value)[] ps)
    {
        if (tx is not null)
        {
            var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("Closing needs a SQL command transaction.");
            await using var cmd = s.Command(sql);
            Bind(cmd, ps);
            return await run(cmd).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c = connections.Command(sql, conn);
        Bind(c, ps);
        return await run(c).ConfigureAwait(false);
    }

    private static void Bind(SqlCommand cmd, (string Name, SqlDbType Type, object? Value)[] ps)
    {
        foreach (var (n, t, v) in ps) cmd.Parameters.Add(n, t).Value = v ?? DBNull.Value;
    }

    public static async Task<int> ExecAsync(ICommandTransaction tx, string sql, CancellationToken ct, params (string Name, SqlDbType Type, object? Value)[] ps)
    {
        var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("Closing needs a SQL command transaction.");
        await using var cmd = s.Command(sql);
        Bind(cmd, ps);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static DateOnly Date(SqlDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));

    public static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}

/// <summary>SQL Server <see cref="IClosingStore"/> over migration 0500 (cls) plus read-only queries of the P2–P4 tables.</summary>
public sealed class SqlClosingStore(SqlConnectionFactory connections) : IClosingStore
{
    private const string RunColumns = "run_id, property_id, business_date, last_run_ref, status, current_step, attempts, started_by";

    private static (string, SqlDbType, object?) P(int propertyId) => ("@p", SqlDbType.Int, propertyId);

    private static (string, SqlDbType, object?) D(DateOnly d, string name = "@d") => (name, SqlDbType.Date, d.ToDateTime(TimeOnly.MinValue));

    public Task<DateOnly?> BusinessDateAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, tx, "SELECT business_date FROM inv.property WHERE property_id = @p;",
            async c => await c.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : (DateOnly?)null, ct, P(propertyId));

    public Task<CloseRunRow?> FindRunAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, tx, $"""
            SELECT {RunColumns}, (SELECT closed_by FROM cls.business_day b WHERE b.property_id = r.property_id AND b.business_date = r.business_date),
                   (SELECT closed_utc FROM cls.business_day b WHERE b.property_id = r.property_id AND b.business_date = r.business_date)
            FROM cls.close_run r {(tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")} WHERE property_id = @p AND business_date = @d;
            """, async c =>
        {
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadRun(rd) : null;
        }, ct, P(propertyId), D(date));

    private static CloseRunRow ReadRun(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), ClosingSql.Date(rd, 2), rd.GetString(3), rd.GetString(4),
        rd.GetString(5), rd.GetInt32(6), rd.GetInt32(7), rd.IsDBNull(8) ? null : rd.GetInt32(8), rd.IsDBNull(9) ? null : rd.GetDateTime(9));

    public async Task<CloseRunRow> InsertRunAsync(ICommandTransaction tx, int propertyId, DateOnly date, string runRef, int principalId, DateTime nowUtc, CancellationToken ct)
    {
        var id = await ClosingSql.RunAsync(connections, tx, """
            INSERT cls.close_run(property_id, business_date, run_ref, last_run_ref, status, current_step, attempts, started_by, started_utc, last_attempt_by, last_attempt_utc)
            OUTPUT INSERTED.run_id VALUES (@p, @d, @ref, @ref, 'Running', 'preflight', 1, @u, @now, @u, @now);
            """, async c => (long)(await c.ExecuteScalarAsync(ct).ConfigureAwait(false))!, ct,
            P(propertyId), D(date), ("@ref", SqlDbType.VarChar, runRef), ("@u", SqlDbType.Int, principalId), ("@now", SqlDbType.DateTime2, nowUtc)).ConfigureAwait(false);
        return new CloseRunRow(id, propertyId, date, runRef, CloseRunStatus.Running, "preflight", 1, principalId, null, null);
    }

    public async Task UpdateRunAsync(ICommandTransaction tx, long runId, string status, string currentStep, int? attemptsIncrement, string? runRef, int principalId,
        DateTime nowUtc, CancellationToken ct)
    {
        var n = await ClosingSql.ExecAsync(tx, """
            UPDATE cls.close_run SET status = @s, current_step = @step, attempts = attempts + @inc, last_run_ref = COALESCE(@ref, last_run_ref),
                   last_attempt_by = CASE WHEN @inc > 0 THEN @u ELSE last_attempt_by END,
                   last_attempt_utc = CASE WHEN @inc > 0 THEN @now ELSE last_attempt_utc END
            WHERE run_id = @r;
            """, ct, ("@s", SqlDbType.VarChar, status), ("@step", SqlDbType.VarChar, currentStep), ("@inc", SqlDbType.Int, attemptsIncrement ?? 0),
            ("@ref", SqlDbType.VarChar, runRef), ("@u", SqlDbType.Int, principalId), ("@now", SqlDbType.DateTime2, nowUtc), ("@r", SqlDbType.BigInt, runId)).ConfigureAwait(false);
        if (n != 1) throw new InvalidOperationException("Close run not found.");
    }

    public Task UpsertStepAsync(ICommandTransaction tx, long runId, CloseStep step, string status, int attempt, string detailJson, int principalId, DateTime nowUtc,
        CancellationToken ct) => ClosingSql.ExecAsync(tx, """
            UPDATE cls.close_step WITH (UPDLOCK, HOLDLOCK) SET status = @s, attempt = @a, detail = @j, principal_id = @u, updated_utc = @now WHERE run_id = @r AND step = @step;
            IF @@ROWCOUNT = 0
              INSERT cls.close_step(run_id, step, status, attempt, detail, principal_id, updated_utc) VALUES (@r, @step, @s, @a, @j, @u, @now);
            """, ct, ("@r", SqlDbType.BigInt, runId), ("@step", SqlDbType.VarChar, CloseSteps.Code(step)), ("@s", SqlDbType.VarChar, status), ("@a", SqlDbType.Int, attempt),
        ("@j", SqlDbType.NVarChar, detailJson), ("@u", SqlDbType.Int, principalId), ("@now", SqlDbType.DateTime2, nowUtc));

    public Task<IReadOnlyList<CloseStepRow>> StepsAsync(long runId, CancellationToken ct) =>
        ClosingSql.RunAsync<IReadOnlyList<CloseStepRow>>(connections, null, "SELECT step, status, attempt, detail, updated_utc FROM cls.close_step WHERE run_id = @r;", async c =>
        {
            var list = new List<CloseStepRow>();
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new CloseStepRow(rd.GetString(0), rd.GetString(1), rd.GetInt32(2), rd.GetString(3), rd.GetDateTime(4)));
            return list;
        }, ct, ("@r", SqlDbType.BigInt, runId));

    public Task<PreflightData> PreflightDataAsync(ICommandTransaction? tx, int propertyId, DateOnly date, DateTime nowUtc, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, tx, """
            SELECT r.reservation_id, r.public_no, r.guest_name, rm.room_no, r.arrival
              FROM inv.reservation r LEFT JOIN inv.room rm ON rm.room_id = r.room_id
             WHERE r.property_id = @p AND r.status = 'Confirmed' AND r.arrival <= @d ORDER BY r.reservation_id;
            SELECT s.stay_id, r.public_no, r.guest_name, rm.room_no, s.departure
              FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id
              OUTER APPLY (SELECT TOP 1 room_id FROM gst.stay_segment g WHERE g.stay_id = s.stay_id ORDER BY g.seq DESC) sg
              LEFT JOIN inv.room rm ON rm.room_id = sg.room_id
             WHERE s.property_id = @p AND s.status = 'InHouse' AND s.departure <= @d ORDER BY s.stay_id;
            SELECT r.reservation_id, r.public_no, r.guest_name, r.hold_until_utc
              FROM inv.reservation r WHERE r.property_id = @p AND r.status = 'Held' AND r.hold_until_utc < @now ORDER BY r.reservation_id;
            SELECT s.stay_id, s.folio_id, r.public_no, r.guest_name, rm.room_no,
                   CASE WHEN EXISTS (SELECT 1 FROM bill.charge c WHERE c.folio_id = s.folio_id AND c.kind = 'room' AND c.service_date = @d AND c.reverses_charge_id IS NULL) THEN 1 ELSE 0 END
              FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id
              OUTER APPLY (SELECT TOP 1 room_id FROM gst.stay_segment g WHERE g.stay_id = s.stay_id ORDER BY g.seq DESC) sg
              LEFT JOIN inv.room rm ON rm.room_id = sg.room_id
             WHERE s.property_id = @p AND s.status = 'InHouse' AND s.arrival <= @d AND s.departure > @d ORDER BY s.stay_id;
            SELECT approval_id, action, amount_paise, reason FROM bill.approval
             WHERE property_id = @p AND status = 'Pending' AND expires_utc >= @now ORDER BY approval_id;
            SELECT s.shift_id, t.code, s.status, s.business_date, s.opening_float_paise
                   + COALESCE((SELECT SUM(m.amount_paise) FROM pay.cash_movement m WHERE m.shift_id = s.shift_id), 0)
              FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id
             WHERE t.property_id = @p AND s.status IN ('Open', 'Submitted') ORDER BY s.shift_id;
            """, async c =>
        {
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var arrivals = new List<PendingArrival>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                arrivals.Add(new PendingArrival(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), ClosingSql.Str(rd, 3), ClosingSql.Date(rd, 4)));
            var departures = new List<OverdueDeparture>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                departures.Add(new OverdueDeparture(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), ClosingSql.Str(rd, 3), ClosingSql.Date(rd, 4)));
            var holds = new List<ExpiredHold>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                holds.Add(new ExpiredHold(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetDateTime(3)));
            var nights = new List<NightTarget>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                nights.Add(new NightTarget(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), rd.GetString(3), ClosingSql.Str(rd, 4), rd.GetInt32(5) == 1));
            var approvals = new List<PendingApproval>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                approvals.Add(new PendingApproval(rd.GetInt64(0), rd.GetString(1), new Paise(rd.GetInt64(2)), rd.GetString(3)));
            var shifts = new List<ActiveShift>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                shifts.Add(new ActiveShift(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), ClosingSql.Date(rd, 3), new Paise(rd.GetInt64(4))));
            return new PreflightData(date, arrivals, departures, holds, nights, approvals, shifts);
        }, ct, P(propertyId), D(date), ("@now", SqlDbType.DateTime2, nowUtc));

    public Task<bool> IsClosedAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, tx, "SELECT COUNT(*) FROM cls.business_day WHERE property_id = @p AND business_date = @d;",
            async c => Convert.ToInt32(await c.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1, ct, P(propertyId), D(date));

    public Task<IReadOnlyList<DateOnly>> ClosedDatesAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        ClosingSql.RunAsync<IReadOnlyList<DateOnly>>(connections, null, "SELECT business_date FROM cls.business_day WHERE property_id = @p AND business_date BETWEEN @from AND @to ORDER BY business_date;", async c =>
        {
            var list = new List<DateOnly>();
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ClosingSql.Date(rd, 0));
            return list;
        }, ct, P(propertyId), D(from, "@from"), D(to, "@to"));

    public Task InsertClosedDayAsync(ICommandTransaction tx, int propertyId, DateOnly date, long runId, int closedBy, DateTime closedUtc, CancellationToken ct) =>
        ClosingSql.ExecAsync(tx, "INSERT cls.business_day(property_id, business_date, run_id, closed_by, closed_utc) VALUES (@p, @d, @r, @u, @now);", ct,
            P(propertyId), D(date), ("@r", SqlDbType.BigInt, runId), ("@u", SqlDbType.Int, closedBy), ("@now", SqlDbType.DateTime2, closedUtc));

    public Task<long> InsertSnapshotAsync(ICommandTransaction tx, NewSnapshot s, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(s);
        var f = s.Record.Figures;
        return ClosingSql.RunAsync(connections, tx, """
            INSERT cls.closing_snapshot(property_id, business_date, run_id, closed_by, closed_utc, net_revenue_paise, tax_paise, net_receipts_paise,
                   cash_expected_paise, cash_counted_paise, occupied_room_nights, sellable_room_nights, record_json, record_sha256)
            OUTPUT INSERTED.snapshot_id
            VALUES (@p, @d, @r, @u, @now, @net, @tax, @rec, @exp, @cnt, @occ, @sell, @j, @h);
            """, async c => (long)(await c.ExecuteScalarAsync(ct).ConfigureAwait(false))!, ct,
            P(s.PropertyId), D(s.BusinessDate), ("@r", SqlDbType.BigInt, s.RunId), ("@u", SqlDbType.Int, s.ClosedBy), ("@now", SqlDbType.DateTime2, s.ClosedUtc),
            ("@net", SqlDbType.BigInt, f.Revenue.NetPaise), ("@tax", SqlDbType.BigInt, f.Revenue.TaxPaise), ("@rec", SqlDbType.BigInt, f.Receipts.NetPaise),
            ("@exp", SqlDbType.BigInt, f.Drawer.ExpectedPaise), ("@cnt", SqlDbType.BigInt, f.Drawer.CountedPaise), ("@occ", SqlDbType.Int, f.Occupancy.OccupiedRoomNights),
            ("@sell", SqlDbType.Int, f.Occupancy.SellableRoomNights), ("@j", SqlDbType.NVarChar, s.Json), ("@h", SqlDbType.Binary, s.Sha256));
    }

    public Task<SnapshotRow?> ReadSnapshotAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, tx, "SELECT snapshot_id, business_date, record_json, record_sha256 FROM cls.closing_snapshot WHERE property_id = @p AND business_date = @d;", async c =>
        {
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await rd.ReadAsync(ct).ConfigureAwait(false) ? new SnapshotRow(rd.GetInt64(0), ClosingSql.Date(rd, 1), rd.GetString(2), (byte[])rd[3]) : null;
        }, ct, P(propertyId), D(date));

    public async Task<bool> AdvanceBusinessDateAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct) =>
        await ClosingSql.ExecAsync(tx, "UPDATE inv.property SET business_date = DATEADD(day, 1, @d) WHERE property_id = @p AND business_date = @d;", ct, P(propertyId), D(date)).ConfigureAwait(false) == 1;

    public Task<long> DepositsHeldAsync(ICommandTransaction tx, int propertyId, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, tx, """
            SELECT COALESCE(SUM(CASE x.kind WHEN 'deposit' THEN x.amount_paise WHEN 'deposit-return' THEN -x.amount_paise ELSE 0 END), 0)
              FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
             WHERE r.property_id = @p;
            """, async c => Convert.ToInt64(await c.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture), ct, P(propertyId));

    public Task<IReadOnlyList<int>> CountersAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct) =>
        ClosingSql.RunAsync<IReadOnlyList<int>>(connections, tx, """
            SELECT s.submitted_by FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id WHERE t.property_id = @p AND s.business_date = @d AND s.submitted_by IS NOT NULL
            UNION SELECT a.decided_by FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id JOIN bill.approval a ON a.shift_id = s.shift_id
                   WHERE t.property_id = @p AND s.business_date = @d AND a.status = 'Approved' AND a.decided_by IS NOT NULL
            UNION SELECT c.submitted_by FROM pay.shift_count c JOIN pay.shift s ON s.shift_id = c.shift_id JOIN pay.till t ON t.till_id = s.till_id
                   WHERE t.property_id = @p AND s.business_date = @d
            UNION SELECT closed_by FROM cls.business_day WHERE property_id = @p AND business_date = @d;
            """, async c =>
        {
            var list = new List<int>();
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetInt32(0));
            return list;
        }, ct, P(propertyId), D(date));

    public Task<CashBookDayRow?> LockCashBookDayRowAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, tx, "SELECT business_date, locked, locked_by, locked_utc FROM cls.cashbook_day WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND business_date = @d;",
            async c =>
            {
                await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
                return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadDay(rd) : null;
            }, ct, P(propertyId), D(date));

    private static CashBookDayRow ReadDay(SqlDataReader rd) =>
        new(ClosingSql.Date(rd, 0), rd.GetBoolean(1), rd.IsDBNull(2) ? null : rd.GetInt32(2), rd.IsDBNull(3) ? null : rd.GetDateTime(3));

    public Task SetCashBookLockAsync(ICommandTransaction tx, int propertyId, DateOnly date, bool locked, int principalId, DateTime nowUtc, string? reason, CancellationToken ct) =>
        ClosingSql.ExecAsync(tx, """
            UPDATE cls.cashbook_day SET locked = @l, locked_by = CASE WHEN @l = 1 THEN @u END, locked_utc = CASE WHEN @l = 1 THEN @now END
             WHERE property_id = @p AND business_date = @d;
            IF @@ROWCOUNT = 0
              INSERT cls.cashbook_day(property_id, business_date, locked, locked_by, locked_utc)
              VALUES (@p, @d, @l, CASE WHEN @l = 1 THEN @u END, CASE WHEN @l = 1 THEN @now END);
            INSERT cls.cashbook_event(property_id, business_date, action, principal_id, at_utc, reason)
            VALUES (@p, @d, CASE WHEN @l = 1 THEN 'lock' ELSE 'reopen' END, @u, @now, @why);
            """, ct, P(propertyId), D(date), ("@l", SqlDbType.Bit, locked), ("@u", SqlDbType.Int, principalId), ("@now", SqlDbType.DateTime2, nowUtc),
            ("@why", SqlDbType.NVarChar, reason));

    public Task<(CashBookDayRow? Day, IReadOnlyList<CashBookEventView> History)> ReadCashBookDayAsync(int propertyId, DateOnly date, CancellationToken ct) =>
        ClosingSql.RunAsync<(CashBookDayRow?, IReadOnlyList<CashBookEventView>)>(connections, null, """
            SELECT business_date, locked, locked_by, locked_utc FROM cls.cashbook_day WHERE property_id = @p AND business_date = @d;
            SELECT action, principal_id, at_utc, reason FROM cls.cashbook_event WHERE property_id = @p AND business_date = @d ORDER BY event_id;
            """, async c =>
        {
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            CashBookDayRow? day = await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadDay(rd) : null;
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var events = new List<CashBookEventView>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) events.Add(new CashBookEventView(rd.GetString(0), rd.GetInt32(1), rd.GetDateTime(2), ClosingSql.Str(rd, 3)));
            return (day, events);
        }, ct, P(propertyId), D(date));

    public async Task<IAsyncDisposable?> TryAcquireRunLeaseAsync(int propertyId, CancellationToken ct)
    {
        var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cmd = connections.Command("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;", conn);
            cmd.Parameters.Add("@res", SqlDbType.NVarChar, 255).Value = Resource(propertyId);
            var r = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (r >= 0) return new Lease(conn, Resource(propertyId));
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        await conn.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    private static string Resource(int propertyId) => "hcc:close:" + propertyId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Holds the session app lock on its own connection for the whole run; a crashed process drops the connection and the lock.</summary>
    private sealed class Lease(SqlConnection conn, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = new SqlCommand("EXEC sp_releaseapplock @Resource = @res, @LockOwner = 'Session';", conn);
                cmd.Parameters.Add("@res", SqlDbType.NVarChar, 255).Value = resource;
                await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (SqlException)
            {
                // Connection already broken: the server released the session lock with it.
            }
            finally
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// SQL <see cref="IBusinessDayGuard"/>: the barrier read takes a key-range shared lock (HOLDLOCK) on <c>cls.business_day</c> for the
/// rest of the writer's transaction, so the close's barrier insert and in-flight writers serialize (see migration 0500).
/// </summary>
public sealed class SqlBusinessDayGuard : IBusinessDayGuard
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The business-day guard needs a SQL command transaction.");

    public async Task<DateOnly?> EnsureBusinessDayOpenAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            DECLARE @d DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
            SELECT @d, CASE WHEN EXISTS (SELECT 1 FROM cls.business_day WITH (HOLDLOCK) WHERE property_id = @p AND business_date = @d) THEN 1 ELSE 0 END;
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false) || rd.IsDBNull(0)) return null; // unknown property: the handler reports it
        var date = ClosingSql.Date(rd, 0);
        if (rd.GetInt32(1) == 1) throw CloseRules.DayClosed(date);
        return date;
    }

    public async Task EnsureOpenAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT CASE WHEN @d < (SELECT business_date FROM inv.property WHERE property_id = @p)
                          OR EXISTS (SELECT 1 FROM cls.business_day WITH (HOLDLOCK) WHERE property_id = @p AND business_date = @d) THEN 1 ELSE 0 END;
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1) throw CloseRules.DayClosed(date);
    }

    public async Task EnsureCashBookOpenAsync(ICommandTransaction tx, int propertyId, DateOnly date, string what, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT COUNT(*) FROM cls.cashbook_day WITH (HOLDLOCK) WHERE property_id = @p AND business_date = @d AND locked = 1;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0)
            throw new HccException(HccErrorCode.DAY_CLOSED, CashBookRules.LockedMessage(date, what));
    }
}
