namespace Hcc.Application.Documents;

// LF-CONTRACT.md §1.5 (copied verbatim): the store API of the separate documents database (HccDocuments, DEC-200).
public enum DocumentObjectKind { IdImage, GuestPhoto, ExpenseBill, BankingSlip, ComplianceEvidence, CounterAttachment, LegacyImage }

public sealed record NewDocumentObject(int PropertyId, DocumentObjectKind Kind, string OwnerTable, long OwnerId, string ContentType, ReadOnlyMemory<byte>? Content, string? SealedText, int CreatedBy);

public sealed record DocumentObjectInfo(Guid ObjectRef, DocumentObjectKind Kind, string ContentType, int SizeBytes, bool Sealed, DateTime CreatedUtc, DateTime? DeletedUtc);

public interface IDocumentObjectStore
{
    /// Writes the object in its own committed transaction on HccDocuments and returns the ref the caller stores in its Hcc row (§1.8 order).
    Task<Guid> PutAsync(NewDocumentObject o, CancellationToken ct = default);

    Task<DocumentObjectInfo?> InfoAsync(Guid objectRef, CancellationToken ct = default);

    Task<(DocumentObjectInfo Info, byte[]? Content, string? SealedText)?> GetAsync(Guid objectRef, CancellationToken ct = default);

    /// Replaces the sealed text (Wave D re-key); refuses when the object is not sealed.
    Task ResealAsync(Guid objectRef, string sealedText, CancellationToken ct = default);

    /// Marks deleted (release); the sweep removes the bytes. Idempotent.
    Task ReleaseAsync(Guid objectRef, CancellationToken ct = default);

    /// Refs of live (not deleted) objects of a kind for a property, oldest first, paged — the sweep and the health check read this.
    Task<IReadOnlyList<(Guid Ref, DateTime CreatedUtc)>> ListAsync(int propertyId, DocumentObjectKind kind, DateTime? createdBefore, int take, CancellationToken ct = default);

    /// Deletes rows marked deleted before `before`, and (orphan sweep) rows older than `before` whose ref is NOT in `live`. Returns the count.
    Task<int> SweepAsync(int propertyId, DocumentObjectKind kind, DateTime before, IReadOnlySet<Guid> live, CancellationToken ct = default);

    Task<DocumentDatabaseSize> SizeAsync(CancellationToken ct = default);
}

public sealed record DocumentDatabaseSize(string Database, long DataBytes, long LogBytes, long LimitBytes /* 50 GB Express */, int ObjectCount, long ContentBytes);

/// <summary>Outbox event type published in an Hcc transaction when a row that held a document object is purged or deleted (contract §1.7).</summary>
public static class DocumentObjectEvents
{
    public const string Released = "DocumentObjectReleased";
}

/// <summary>Payload of <see cref="DocumentObjectEvents.Released"/>.</summary>
public sealed record DocumentObjectReleased(Guid ObjectRef, string Kind);
