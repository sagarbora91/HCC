using System.Data;
using System.Text.Json;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Application.Reporting;
using Hcc.Domain.Reporting;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>
/// F4-T2: consumes the day close's <c>BusinessDayClosed</c> outbox event and writes the two HELD owner-digest drafts of that date (Email with money, WhatsApp counts only)
/// into digest.draft in the consumer's own transaction (at-least-once delivery, exactly-once effect; a re-close replaces the drafts). It never sends anything and writes to no
/// delivery outbox: the F9 adapter will move held drafts there.
/// </summary>
public sealed class DigestDraftProjector(OutboxConsumer outbox, SqlConnectionFactory connections, IReportSource reports, IRevenueCategorySource categories, TimeProvider clock)
{
    public const string ConsumerName = "owner-digest-drafts";
    public const string BusinessDayClosed = "BusinessDayClosed";

    public Task<int> RunOnceAsync(int batchSize = 50, CancellationToken ct = default) => outbox.ConsumeAsync(ConsumerName, [BusinessDayClosed], ApplyAsync, batchSize, ct);

    public async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        using var doc = JsonDocument.Parse(evt.PayloadJson);
        int propertyId = 0; DateOnly date = default;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name.Equals("propertyId", StringComparison.OrdinalIgnoreCase)) propertyId = p.Value.GetInt32();
            else if (p.Name.Equals("businessDate", StringComparison.OrdinalIgnoreCase)) date = DateOnly.Parse(p.Value.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (propertyId == 0 || date == default) return;
        var data = await reports.LoadAsync(null, propertyId, date, date, ct).ConfigureAwait(false);
        var cat = await categories.LoadAsync(propertyId, date, date, ct).ConfigureAwait(false);
        var facts = new DigestFacts(date, DailyPackCalculator.Compute(data, date, date), RevenueByCategoryCalculator.Compute(data, cat, date, date),
            await CountAsync(connection, transaction,
                "SELECT COUNT(*) FROM bill.approval a JOIN bill.folio f ON f.folio_id = a.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.property_id = @p AND a.status = 'Pending'", propertyId, date, ct).ConfigureAwait(false),
            await CountAsync(connection, transaction,
                "SELECT COUNT(*) FROM inv.reservation WHERE property_id = @p AND status = 'Confirmed' AND arrival = DATEADD(DAY, 1, @d)", propertyId, date, ct).ConfigureAwait(false));
        var now = clock.GetUtcNow().UtcDateTime;
        await UpsertAsync(connection, transaction, propertyId, date, "Email", false, DigestBodies.Email(facts), now, ct).ConfigureAwait(false);
        await UpsertAsync(connection, transaction, propertyId, date, "WhatsApp", true, DigestBodies.WhatsApp(facts), now, ct).ConfigureAwait(false);
    }

    private async Task<int> CountAsync(SqlConnection c, SqlTransaction t, string sql, int propertyId, DateOnly date, CancellationToken ct)
    {
        await using var cmd = connections.Command(sql, c, t);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task UpsertAsync(SqlConnection c, SqlTransaction t, int propertyId, DateOnly date, string channel, bool countsOnly, string body, DateTime now, CancellationToken ct)
    {
        await using var cmd = connections.Command("""
            DELETE digest.draft WHERE property_id = @p AND business_date = @d AND channel = @ch;
            INSERT digest.draft(property_id, business_date, channel, counts_only, status, reason, body, created_utc, created_by)
            VALUES (@p, @d, @ch, @co, 'held', @re, @body, @cu, 0);
            """, c, t);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@ch", SqlDbType.VarChar, 8).Value = channel;
        cmd.Parameters.Add("@co", SqlDbType.Bit).Value = countsOnly;
        cmd.Parameters.Add("@re", SqlDbType.NVarChar, 200).Value = DigestDraftQueries.HeldReason;
        cmd.Parameters.Add("@body", SqlDbType.NVarChar, -1).Value = body;
        var cu = cmd.Parameters.Add("@cu", SqlDbType.DateTime2);
        cu.Scale = 3;
        cu.Value = now;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
