using System.Data;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Housekeeping;
using Hcc.Domain.Dates;
using Hcc.Infrastructure.SqlServer.Outbox;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Housekeeping;

/// <summary>SQL adapter of <see cref="IStayoverStore"/> over gst.stay / gst.stay_segment / ops.hk_task (causal-key idempotency, locked check-then-insert).</summary>
public sealed class SqlStayoverStore : IStayoverStore
{
    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command(P3Sql.BusinessDateSql);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<StayoverRun> GenerateAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command(GenerateSql);
        return await RunAsync(cmd, propertyId, businessDate, utc, ct).ConfigureAwait(false);
    }

    internal const string GenerateSql = """
        DECLARE @occupied INT, @created INT;
        DECLARE @rooms TABLE(room_id INT PRIMARY KEY, stay_id BIGINT NOT NULL, ck VARCHAR(80) NOT NULL);
        INSERT @rooms(room_id, stay_id, ck)
        SELECT seg.room_id, s.stay_id, CONCAT('hk:stayover:', seg.room_id, ':', CONVERT(CHAR(8), @bd, 112))
        FROM gst.stay s JOIN gst.stay_segment seg ON seg.stay_id = s.stay_id AND seg.to_date IS NULL
        WHERE s.property_id = @p AND s.status = 'InHouse' AND s.arrival <= @bd AND s.departure > @bd;
        SET @occupied = @@ROWCOUNT;
        INSERT ops.hk_task(property_id, room_id, kind, status, stay_id, causal_key, business_date, created_utc)
        SELECT @p, r.room_id, 'stayover', 'ToDo', r.stay_id, r.ck, @bd, @utc FROM @rooms r
        WHERE NOT EXISTS (SELECT 1 FROM ops.hk_task t WITH (UPDLOCK, HOLDLOCK) WHERE t.causal_key = r.ck);
        SET @created = @@ROWCOUNT;
        SELECT @occupied, @created;
        """;

    internal static async Task<StayoverRun> RunAsync(SqlCommand cmd, int propertyId, DateOnly businessDate, DateTime utc, CancellationToken ct)
    {
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return new StayoverRun(rd.GetInt32(0), rd.GetInt32(1));
    }
}

/// <summary>
/// Opens the day's stay-over cleans when a new business day opens: consumes the closing module's <c>BusinessDateOpened</c> outbox event
/// (at-least-once delivery, exactly-once effect per consumer; the task causal key makes a replay a no-op). An event for a date that is no
/// longer the property's business date is skipped.
/// </summary>
public sealed class StayoverTaskProjector(OutboxConsumer outbox)
{
    public const string ConsumerName = "housekeeping-stayover";

    public Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default) =>
        outbox.ConsumeAsync(ConsumerName, ["BusinessDateOpened"], ApplyAsync, batchSize, ct);

    private sealed record Opened(int PropertyId, string BusinessDate);

    public static async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var e = JsonSerializer.Deserialize<Opened>(evt.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Outbox {evt.OutboxId}: empty BusinessDateOpened payload.");
        var date = StayDates.Parse(e.BusinessDate, "businessDate");
        await using (var cur = new SqlCommand("SELECT business_date FROM inv.property WHERE property_id = @p;", connection, transaction))
        {
            cur.Add("@p", SqlDbType.Int, e.PropertyId);
            if (await cur.ExecuteScalarAsync(ct).ConfigureAwait(false) is not DateTime now || DateOnly.FromDateTime(now) != date) return; // stale event
        }

        await using var cmd = new SqlCommand(SqlStayoverStore.GenerateSql, connection, transaction);
        await SqlStayoverStore.RunAsync(cmd, e.PropertyId, date, DateTime.SpecifyKind(evt.CreatedUtc, DateTimeKind.Utc), ct).ConfigureAwait(false);
    }
}
