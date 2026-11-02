using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Documents;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Infrastructure.SqlServer.Documents;

/// <summary>LF-docdb: helpers the four stores share (dangling-ref error, release event).</summary>
public static class DocumentObjectAccess
{
    /// <summary>Contract §1.7: an <c>Hcc</c> row whose object is missing in HccDocuments reads as 404 NOT_FOUND with <c>details.reason = DOC_MISSING</c>.</summary>
    public static HccException Missing() => new(HccErrorCode.NOT_FOUND, "Image missing - see System health.",
        new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["reason"] = JsonSerializer.SerializeToElement("DOC_MISSING") });

    /// <summary>Publishes <c>DocumentObjectReleased</c> in the caller's Hcc transaction (the consumer marks the object deleted).</summary>
    public static Task PublishReleaseAsync(ICommandTransaction tx, Guid objectRef, string kind, long aggregateId, CancellationToken ct)
        => tx.PublishAsync(DocumentObjectEvents.Released, aggregateId, new DocumentObjectReleased(objectRef, kind), ct);

    /// <summary>The contract's order: the object first, in its own committed transaction; the caller's Hcc transaction then stores the ref.</summary>
    public static async Task<byte[]> ReadAsync(IDocumentObjectStore? objects, Guid objectRef, CancellationToken ct)
    {
        if (objects is null) throw new InvalidOperationException("This file is in the documents database, which is not configured.");
        var got = await objects.GetAsync(objectRef, ct).ConfigureAwait(false);
        return got?.Content ?? throw Missing();
    }
}
