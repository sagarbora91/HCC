using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Application.Commands;

/// <summary>
/// Who runs which command under which Idempotency-Key. Actor identity comes from the server session (contracts §1),
/// never from a client-supplied uid.
/// </summary>
public sealed record CommandContext(int PropertyId, int PrincipalId, string Operation, string IdempotencyKey)
{
    public void Validate()
    {
        if (PropertyId <= 0 || PrincipalId <= 0) throw HccException.Validation("Property and principal are required.");
        if (string.IsNullOrWhiteSpace(Operation) || Operation.Length > 40) throw HccException.Validation("Operation name must be 1–40 characters.");
        if (string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > ApiConventions.IdempotencyKeyMaxLength)
            throw HccException.Validation($"{ApiConventions.IdempotencyKeyHeader} must be 1–{ApiConventions.IdempotencyKeyMaxLength} characters.");
    }
}

/// <summary>Result of a command; <see cref="Replayed"/> is true when an earlier identical request's stored result was returned.</summary>
public sealed record CommandResult<T>(T Value, bool Replayed);

/// <summary>
/// The transaction a command handler writes into. Audit and outbox records are committed atomically with the business
/// change and the idempotency record (contracts §4 rule 6), or not at all.
/// </summary>
public interface ICommandTransaction
{
    CommandContext Context { get; }

    /// <summary>Append an audit event (actor = the context's principal).</summary>
    Task AuditAsync(string action, string target, string detail, CancellationToken ct = default);

    /// <summary>Append an outbox event; returns the outbox id.</summary>
    Task<long> PublishAsync(string eventType, long aggregateId, object payload, CancellationToken ct = default);
}

/// <summary>Runs one command in one database transaction with idempotency, audit and outbox.</summary>
public interface ICommandRunner
{
    /// <summary>
    /// Same key + same canonical payload → the original stored result (handler not run again);
    /// same key + different payload → <see cref="HccErrorCode.IDEMPOTENCY_MISMATCH"/>.
    /// <typeparamref name="TResult"/> must serialize to a JSON object (a record/class): the stored result is checked by
    /// <c>CK_idempotency_result</c> (ISJSON), which rejects bare scalars such as a number or a string.
    /// </summary>
    Task<CommandResult<TResult>> RunAsync<TPayload, TResult>(CommandContext context, TPayload payload,
        Func<ICommandTransaction, CancellationToken, Task<TResult>> handler, CancellationToken ct = default);
}

/// <summary>An outbox event as delivered to a consumer.</summary>
public sealed record OutboxEvent(long OutboxId, string EventType, long AggregateId, string PayloadJson, DateTime CreatedUtc);
