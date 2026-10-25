using System.Data;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Commands;

/// <summary>The SQL transaction a command handler writes into (business rows + audit + outbox, one commit).</summary>
public sealed class SqlCommandTransaction : ICommandTransaction
{
    internal SqlCommandTransaction(CommandContext context, SqlConnection connection, SqlTransaction transaction, SqlConnectionFactory factory)
    {
        Context = context;
        Connection = connection;
        Transaction = transaction;
        Factory = factory;
    }

    public CommandContext Context { get; }

    public SqlConnection Connection { get; }

    public SqlTransaction Transaction { get; }

    internal SqlConnectionFactory Factory { get; }

    internal int AuditCount { get; private set; }

    /// <summary>A command bound to this connection and transaction.</summary>
    public SqlCommand Command(string sql) => Factory.Command(sql, Connection, Transaction);

    public async Task AuditAsync(string action, string target, string detail, CancellationToken ct = default)
    {
        await using var cmd = Command("""
            INSERT plat.audit_event(property_id, principal_id, action, target, detail, operation, idem_key)
            VALUES (@p, @u, @a, @t, @d, @o, @k);
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = Context.PropertyId;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = Context.PrincipalId;
        cmd.Parameters.Add("@a", SqlDbType.VarChar, 60).Value = action;
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 60).Value = target;
        cmd.Parameters.Add("@d", SqlDbType.NVarChar, 1000).Value = detail;
        cmd.Parameters.Add("@o", SqlDbType.VarChar, 40).Value = Context.Operation;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 80).Value = Context.IdempotencyKey;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        AuditCount++;
    }

    public async Task<long> PublishAsync(string eventType, long aggregateId, object payload, CancellationToken ct = default)
    {
        await using var cmd = Command("""
            INSERT plat.outbox(event_type, aggregate_id, payload, property_id, principal_id)
            OUTPUT INSERTED.outbox_id VALUES (@e, @a, @j, @p, @u);
            """);
        cmd.Parameters.Add("@e", SqlDbType.VarChar, 60).Value = eventType;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = aggregateId;
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, -1).Value = CanonicalJson.Serialize(payload);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = Context.PropertyId;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = Context.PrincipalId;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}

/// <summary>
/// Runs a command in one SQL transaction: same-key requests are serialized with a transaction-owned application lock,
/// the stored idempotency record is checked (replay or IDEMPOTENCY_MISMATCH), the handler runs, and its result is stored
/// with the audit and outbox rows in the same commit (contracts §2, §4 rule 6; spike T08/T09). A failed handler stores
/// nothing, so the client may retry with the same key. Every command must write at least one audit row.
/// </summary>
public sealed class SqlCommandRunner(SqlConnectionFactory connections) : ICommandRunner
{
    public async Task<CommandResult<TResult>> RunAsync<TPayload, TResult>(CommandContext context, TPayload payload,
        Func<ICommandTransaction, CancellationToken, Task<TResult>> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        context.Validate();
        var hash = CanonicalJson.Sha256(payload);

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        try
        {
            await using (var lk = connections.Command("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;", conn, tx))
            {
                lk.Parameters.Add("@res", SqlDbType.NVarChar, 255).Value =
                    $"idem:{context.PropertyId}:{context.PrincipalId}:{context.Operation}:{context.IdempotencyKey}";
                var r = (int)(await lk.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
                if (r < 0) throw new HccException(HccErrorCode.UNAVAILABLE, "The same request is still being processed. Try again.");
            }

            await using (var q = connections.Command("""
                SELECT payload_sha256, result_json FROM plat.idempotency
                WHERE property_id = @p AND principal_id = @u AND operation = @o AND idem_key = @k;
                """, conn, tx))
            {
                AddKey(q, context);
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    var stored = (byte[])rd[0];
                    var json = rd.IsDBNull(1) ? "null" : rd.GetString(1);
                    await rd.CloseAsync().ConfigureAwait(false);
                    if (!stored.AsSpan().SequenceEqual(hash))
                        throw new HccException(HccErrorCode.IDEMPOTENCY_MISMATCH, "This request key was already used for a different request.");
                    var replay = JsonSerializer.Deserialize<TResult>(json, CanonicalJson.SerializerOptions)!;
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    return new CommandResult<TResult>(replay, true);
                }
            }

            var scope = new SqlCommandTransaction(context, conn, tx, connections);
            var result = await handler(scope, ct).ConfigureAwait(false);
            if (scope.AuditCount == 0)
                throw new InvalidOperationException($"Command {context.Operation} wrote no audit event; every command must audit.");

            await using (var ins = connections.Command("""
                INSERT plat.idempotency(property_id, principal_id, operation, idem_key, payload_sha256, result_json)
                VALUES (@p, @u, @o, @k, @h, @r);
                """, conn, tx))
            {
                AddKey(ins, context);
                ins.Parameters.Add("@h", SqlDbType.Binary, 32).Value = hash;
                ins.Parameters.Add("@r", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(result, CanonicalJson.SerializerOptions);
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new CommandResult<TResult>(result, false);
        }
        catch (SqlException ex)
        {
            await SafeRollbackAsync(tx).ConfigureAwait(false);
            throw SqlErrorTranslator.Translate(ex) ?? (Exception)new HccException(HccErrorCode.UNAVAILABLE, "The database rejected the request.", null, ex);
        }
        catch
        {
            await SafeRollbackAsync(tx).ConfigureAwait(false);
            throw;
        }
    }

    private static void AddKey(SqlCommand cmd, CommandContext context)
    {
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = context.PropertyId;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = context.PrincipalId;
        cmd.Parameters.Add("@o", SqlDbType.VarChar, 40).Value = context.Operation;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, ApiConventions.IdempotencyKeyMaxLength).Value = context.IdempotencyKey;
    }

    private static async Task SafeRollbackAsync(SqlTransaction tx)
    {
        try
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Already rolled back by XACT_ABORT.
        }
        catch (SqlException)
        {
            // Connection broken; the server rolls back the transaction.
        }
    }
}
