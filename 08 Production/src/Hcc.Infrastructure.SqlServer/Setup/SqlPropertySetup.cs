using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Setup;
using Hcc.Contracts;
using Hcc.Contracts.Setup;
using Hcc.Domain.Access;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Domain.Setup;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Setup;

/// <summary>
/// <c>SetupProperty</c>: one SQL transaction under exclusive application locks — idempotency check (stored under property 0 /
/// principal 0, there being no session yet; the password is never part of the stored payload hash and a replay must present the same
/// password), "no property yet" check, <c>inv.property</c> row, first Owner (role OWN, PBKDF2 password per DEC-062), Master configuration
/// identity seeded through <see cref="ConfigurationService.SeedAsync"/>, audit rows <c>PROPERTY_SETUP</c> + <c>OWNER_BOOTSTRAP</c>, commit.
/// Replaces the manual <c>INSERT inv.property</c> of the Wave 1 install guide.
/// </summary>
public sealed class SqlPropertySetup(SqlConnectionFactory connections, ISecretHasher hasher, AccessOptions options, ConfigurationService configuration, TimeProvider clock)
    : IPropertySetup
{
    public const string Operation = "SetupProperty";
    private const string SetupLock = "hcc.setup.property";
    private const string OwnersLock = "hcc.access.owners"; // same lock as SqlUserAdministration (first Owner / last Owner rules)

    public async Task<SetupStatusResult> StatusAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("SELECT COUNT(*) FROM inv.property;", conn);
        var n = (int)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return new SetupStatusResult(n == 0, n);
    }

    public async Task<SetupPropertyResult> SetupAsync(string idempotencyKey, SetupPropertyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > ApiConventions.IdempotencyKeyMaxLength)
            throw HccException.Validation($"{ApiConventions.IdempotencyKeyHeader} must be 1–{ApiConventions.IdempotencyKeyMaxLength} characters.");
        var plan = PropertySetupRules.Plan(request.Name, request.Address, request.Code, request.Gstin, request.State, request.BusinessDate,
            request.TimeZone, request.CheckInTime, request.CheckOutTime, clock.GetUtcNow());
        var login = UserAdminRules.NormalizeLogin(request.OwnerLoginName);
        var display = UserAdminRules.DisplayName(request.OwnerDisplayName);
        PasswordRules.Validate(request.OwnerPassword, login);
        var payloadHash = CanonicalJson.Sha256(request with { OwnerPassword = string.Empty });
        var now = SqlSessionService.Truncate(clock.GetUtcNow().UtcDateTime);

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        try
        {
            foreach (var res in new[] { SetupLock, OwnersLock })
            {
                await using var lk = connections.Command("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;", conn, tx);
                lk.Parameters.Add("@res", SqlDbType.NVarChar, 255).Value = res;
                if ((int)(await lk.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999) < 0)
                    throw new HccException(HccErrorCode.UNAVAILABLE, "Setup is already running. Try again.");
            }

            if (await ReplayAsync(conn, tx, idempotencyKey, payloadHash, request.OwnerPassword, ct).ConfigureAwait(false) is { } replay)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return replay;
            }

            var hash = hasher.Hash(request.OwnerPassword, options.PasswordIterations);
            int propertyId, userId;
            await using (var ins = connections.Command("""
                IF EXISTS (SELECT 1 FROM inv.property) THROW 51901, 'Already set up.', 1;
                IF EXISTS (SELECT 1 FROM plat.app_user) THROW 51901, 'Already set up.', 1;
                DECLARE @pid TABLE(property_id INT);
                INSERT inv.property(code, name, time_zone, business_date, state_name, gstin, address) OUTPUT INSERTED.property_id INTO @pid
                  VALUES (@code, @name, @tz, @bd, @state, @gstin, @addr);
                DECLARE @p INT = (SELECT property_id FROM @pid);
                DECLARE @id TABLE(user_id INT);
                INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id INTO @id VALUES (@p, @l, @n, @now);
                DECLARE @u INT = (SELECT user_id FROM @id);
                INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, 'OWN', @now, @now);
                INSERT plat.credential(user_id, kind, algorithm, iterations, salt, secret_hash, changed_utc) VALUES (@u, 'PASSWORD', @alg, @it, @salt, @hash, @now);
                SELECT @p, @u;
                """, conn, tx))
            {
                ins.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = plan.Code;
                ins.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = plan.Name;
                ins.Parameters.Add("@tz", SqlDbType.VarChar, 64).Value = plan.TimeZone;
                ins.Parameters.Add("@bd", SqlDbType.Date).Value = plan.BusinessDate.ToDateTime(TimeOnly.MinValue);
                ins.Parameters.Add("@state", SqlDbType.NVarChar, 60).Value = plan.State;
                ins.Parameters.Add("@gstin", SqlDbType.VarChar, 15).Value = (object?)plan.Gstin ?? DBNull.Value;
                ins.Parameters.Add("@addr", SqlDbType.NVarChar, 200).Value = plan.Address;
                ins.Parameters.Add("@l", SqlDbType.VarChar, 40).Value = login;
                ins.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = display;
                ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                ins.Parameters.Add("@alg", SqlDbType.VarChar, 20).Value = hash.Algorithm;
                ins.Parameters.Add("@it", SqlDbType.Int).Value = hash.Iterations;
                ins.Parameters.Add("@salt", SqlDbType.VarBinary, 32).Value = hash.Salt;
                ins.Parameters.Add("@hash", SqlDbType.VarBinary, 64).Value = hash.Hash;
                try
                {
                    await using var rd = await ins.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    await rd.ReadAsync(ct).ConfigureAwait(false);
                    (propertyId, userId) = (rd.GetInt32(0), rd.GetInt32(1));
                }
                catch (SqlException ex) when (ex.Number == 51901)
                {
                    throw new HccException(HccErrorCode.FORBIDDEN, "This server is already set up: the hotel exists. Sign in as the Owner and change details in Master configuration.");
                }
            }

            var scope = new SqlCommandTransaction(new CommandContext(propertyId, userId, Operation, idempotencyKey), conn, tx, connections);
            await scope.AuditAsync("PROPERTY_SETUP", "property:" + propertyId.ToString(CultureInfo.InvariantCulture),
                $"Hotel '{plan.Name}' ({plan.Code}) set up · {plan.State}{(plan.Gstin is null ? "" : " · GSTIN " + plan.Gstin)} · business date {plan.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} · {plan.TimeZone}", ct).ConfigureAwait(false);
            await scope.AuditAsync("OWNER_BOOTSTRAP", "user:" + userId.ToString(CultureInfo.InvariantCulture), $"First Owner '{login}' created by first-time setup.", ct).ConfigureAwait(false);

            var seed = new List<KeyValuePair<string, ConfigValue>>
            {
                new("hotel", ConfigValue.Of(plan.Name)),
                new("brand.address", ConfigValue.Of(plan.Address)),
                new("checkin", ConfigValue.Of(plan.CheckInTime)),
                new("checkout", ConfigValue.Of(plan.CheckOutTime)),
            };
            if (plan.Gstin is not null) seed.Add(new("brand.gstin", ConfigValue.Of(plan.Gstin)));
            var seeded = await configuration.SeedAsync(scope, seed, "First-time hotel setup", ct).ConfigureAwait(false);

            var result = new SetupPropertyResult(propertyId, plan.Code, plan.Name, plan.State, plan.Gstin, plan.BusinessDate, plan.TimeZone,
                plan.CheckInTime, plan.CheckOutTime, userId, login, seeded,
                $"Hotel '{plan.Name}' is set up. Sign in as '{login}' with the password you chose.");
            await using (var idem = connections.Command("""
                INSERT plat.idempotency(property_id, principal_id, operation, idem_key, payload_sha256, result_json) VALUES (0, 0, @o, @k, @h, @r);
                """, conn, tx))
            {
                idem.Parameters.Add("@o", SqlDbType.VarChar, 40).Value = Operation;
                idem.Parameters.Add("@k", SqlDbType.VarChar, ApiConventions.IdempotencyKeyMaxLength).Value = idempotencyKey;
                idem.Parameters.Add("@h", SqlDbType.Binary, 32).Value = payloadHash;
                idem.Parameters.Add("@r", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(result, CanonicalJson.SerializerOptions);
                await idem.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch (SqlException ex)
        {
            await RollbackAsync(tx).ConfigureAwait(false);
            throw SqlErrorTranslator.Translate(ex) ?? (Exception)new HccException(HccErrorCode.UNAVAILABLE, "The database rejected the setup.", null, ex);
        }
        catch
        {
            await RollbackAsync(tx).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Same key: same request (and the same Owner password) → the stored result; anything else → IDEMPOTENCY_MISMATCH.</summary>
    private async Task<SetupPropertyResult?> ReplayAsync(SqlConnection conn, SqlTransaction tx, string key, byte[] payloadHash, string password, CancellationToken ct)
    {
        byte[] stored;
        string json;
        await using (var q = connections.Command("""
            SELECT payload_sha256, result_json FROM plat.idempotency WHERE property_id = 0 AND principal_id = 0 AND operation = @o AND idem_key = @k;
            """, conn, tx))
        {
            q.Parameters.Add("@o", SqlDbType.VarChar, 40).Value = Operation;
            q.Parameters.Add("@k", SqlDbType.VarChar, ApiConventions.IdempotencyKeyMaxLength).Value = key;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            (stored, json) = ((byte[])rd[0], rd.GetString(1));
        }

        var mismatch = new HccException(HccErrorCode.IDEMPOTENCY_MISMATCH, "This request key was already used for a different request.");
        if (!stored.AsSpan().SequenceEqual(payloadHash)) throw mismatch;
        var result = JsonSerializer.Deserialize<SetupPropertyResult>(json, CanonicalJson.SerializerOptions)!;
        await using (var c = connections.Command("SELECT algorithm, iterations, salt, secret_hash FROM plat.credential WHERE user_id = @u AND kind = 'PASSWORD';", conn, tx))
        {
            c.Parameters.Add("@u", SqlDbType.Int).Value = result.OwnerUserId;
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)
                || !hasher.Verify(password, new HashedSecret(rd.GetString(0), rd.GetInt32(1), (byte[])rd[2], (byte[])rd[3])))
                throw mismatch;
        }

        return result;
    }

    private static async Task RollbackAsync(SqlTransaction tx)
    {
        try
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Already rolled back by XACT_ABORT.
        }
    }
}
