using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Documents;
using Hcc.Infrastructure.SqlServer.Documents;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.DocumentObjects;

/// <summary>
/// LF-docdb (contract §1.7): the consumer of <c>DocumentObjectReleased</c>. A purged / deleted <c>Hcc</c> row publishes the event inside its own
/// transaction; this consumer marks the object deleted in <c>HccDocuments</c> (idempotent). A failure parks the event like any other consumer
/// (System health shows it, retry as today); nothing in <c>Hcc</c> ever waits on the documents database.
/// </summary>
public sealed class DocumentReleaseProjector(OutboxConsumer outbox, IDocumentObjectStore store)
{
    public const string ConsumerName = "document-release";

    public Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default) => outbox.ConsumeAsync(ConsumerName, [DocumentObjectEvents.Released], ApplyAsync, batchSize, ct);

    public async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var e = JsonSerializer.Deserialize<DocumentObjectReleased>(evt.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (e is null || e.ObjectRef == Guid.Empty) return;
        await store.ReleaseAsync(e.ObjectRef, ct).ConfigureAwait(false);
    }
}
