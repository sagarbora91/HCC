using System.Data;
using System.Security.Cryptography;
using System.Text;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>Opaque tokens and their stored SHA-256.</summary>
public static class AccessTokens
{
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static byte[] Sha256(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Direct audit rows for sign-in/out (no Idempotency-Key: there is no principal before sign-in).</summary>
internal static class AccessAudit
{
    public static async Task WriteAsync(SqlConnectionFactory f, SqlConnection conn, SqlTransaction? tx, int propertyId, int principalId, string action, string target, string detail, string operation, CancellationToken ct)
    {
        await using var cmd = f.Command("""
            INSERT plat.audit_event(property_id, principal_id, action, target, detail, operation) VALUES (@p, @u, @a, @t, @d, @o);
            """, conn, tx);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
        cmd.Parameters.Add("@a", SqlDbType.VarChar, 60).Value = action;
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 60).Value = target;
        cmd.Parameters.Add("@d", SqlDbType.NVarChar, 1000).Value = detail;
        cmd.Parameters.Add("@o", SqlDbType.VarChar, 40).Value = operation;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Sign-in (DEC-062: password for OWN/ACC/TAD; PIN only on a registered, unrevoked device for everyone else) with one uniform
/// failure (F-M01: same message, reason BAD_CREDENTIALS and hashing cost for unknown, disabled, role-less, wrong-kind and wrong
/// secret; the precise reason goes to the audit row only), opaque sessions (idle/absolute expiry) and revocation on the next
/// request when the user is disabled, their roles/credential change (security stamp) or the device is revoked (AS-07).
/// PIN sessions are bound to their device: every request must carry the same device token (F-M07). Attempt throttling is
/// per client address in the API (<see cref="SignInThrottle"/>, F-M02), not a per-account lockout.
/// </summary>
public sealed class SqlSessionService(SqlConnectionFactory connections, ISecretHasher hasher, AccessOptions options, TimeProvider clock) : ISessionService
{
    private HashedSecret? _dummyPassword;
    private HashedSecret? _dummyPin;
    private const string SignInFailed = "Sign-in failed. Check the name and the password or PIN.";

    public async Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hasPassword = !string.IsNullOrEmpty(request.Password);
        var hasPin = !string.IsNullOrEmpty(request.Pin);
        if (hasPassword == hasPin) throw HccException.Validation("Enter either a password or a PIN.");
        var kind = hasPassword ? CredentialKind.Password : CredentialKind.Pin;
        var secret = hasPassword ? request.Password! : request.Pin!;
        var login = UserAdminRules.NormalizeLogin(request.LoginName);
        var now = Now();

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        HccException? failure;
        (AuthenticatedSession, string)? success = null;
        try
        {
            (success, failure) = await TrySignInAsync(conn, tx, login, kind, secret, deviceToken, now, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);   // failure counters and audit rows are kept
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (failure is not null) throw failure;
        return success!.Value;
    }

    private async Task<((AuthenticatedSession, string)?, HccException?)> TrySignInAsync(SqlConnection conn, SqlTransaction tx, string login, CredentialKind kind, string secret, string? deviceToken, DateTime now, CancellationToken ct)
    {
        // PIN sign-in needs a registered, unrevoked device first. This check depends only on the device token, never on the
        // account, so its distinct message says nothing about which sign-in names exist — and without a device no PIN is
        // ever tried (no PIN oracle from an unregistered host).
        int? deviceId = null, devicePropertyId = null;
        if (kind == CredentialKind.Pin)
        {
            if (!string.IsNullOrEmpty(deviceToken) && deviceToken.Length <= 200)
            {
                await using var q = connections.Command("SELECT device_id, property_id FROM plat.device WHERE token_sha256 = @h AND revoked_utc IS NULL;", conn, tx);
                q.Parameters.Add("@h", SqlDbType.Binary, 32).Value = AccessTokens.Sha256(deviceToken);
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    deviceId = rd.GetInt32(0);
                    devicePropertyId = rd.GetInt32(1);
                }
            }

            if (deviceId is null)
            {
                await AccessAudit.WriteAsync(connections, conn, tx, 0, 0, "SIGN_IN_FAILED", "device:unknown",
                    string.IsNullOrEmpty(deviceToken) ? "PIN sign-in without a device token." : "PIN sign-in from an unknown or revoked device.", "SignIn", ct).ConfigureAwait(false);
                return (null, Forbidden("PIN sign-in works only on a registered hotel device.", "DEVICE_NOT_REGISTERED"));
            }
        }

        int? userId = null;
        int propertyId = 0, stamp = 0;
        var displayName = string.Empty;
        var enabled = false;
        DateTime? from = null, until = null;
        await using (var q = connections.Command("""
            SELECT user_id, property_id, display_name, enabled, valid_from_utc, valid_until_utc, security_stamp
              FROM plat.app_user WITH (UPDLOCK, ROWLOCK) WHERE login_name = @l;
            """, conn, tx))
        {
            q.Parameters.Add("@l", SqlDbType.VarChar, 40).Value = login;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                userId = rd.GetInt32(0);
                propertyId = rd.GetInt32(1);
                displayName = rd.GetString(2);
                enabled = rd.GetBoolean(3);
                from = rd.IsDBNull(4) ? null : rd.GetDateTime(4);
                until = rd.IsDBNull(5) ? null : rd.GetDateTime(5);
                stamp = rd.GetInt32(6);
            }
        }

        var roles = new List<string>();
        HashedSecret? stored = null;
        var failed = 0;
        if (userId is not null)
        {
            await using (var q = connections.Command("SELECT role_code FROM plat.user_role WHERE user_id = @u;", conn, tx))
            {
                q.Parameters.Add("@u", SqlDbType.Int).Value = userId.Value;
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) roles.Add(rd.GetString(0));
            }

            await using (var q = connections.Command("""
                SELECT algorithm, iterations, salt, secret_hash, failed_count FROM plat.credential WITH (UPDLOCK, ROWLOCK)
                 WHERE user_id = @u AND kind = @k;
                """, conn, tx))
            {
                q.Parameters.Add("@u", SqlDbType.Int).Value = userId.Value;
                q.Parameters.Add("@k", SqlDbType.VarChar, 8).Value = CredentialPolicy.ToWire(kind);
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    stored = new HashedSecret(rd.GetString(0), rd.GetInt32(1), (byte[])rd[2], (byte[])rd[3]);
                    failed = rd.GetInt32(4);
                }
            }
        }

        // F-M01: the secret is always hashed once, at the configured iteration count of the attempted kind when there is no
        // stored hash (unknown name, disabled user, no role, wrong kind, no credential), so every path costs the same.
        var verified = hasher.Verify(secret, stored ?? DummyFor(kind));

        var required = CredentialPolicy.RequiredKind(roles);
        var refusal = userId is null ? $"Unknown sign-in name '{Trim(login)}'."
            : !enabled || (from is not null && now < from) || (until is not null && now >= until) ? "User disabled or outside validity."
            : required is null ? "User has no role."
            : required != kind ? $"Refused {CredentialPolicy.ToWire(kind)} sign-in; account requires {CredentialPolicy.ToWire(required.Value)} (DEC-062)."
            : stored is null ? "No credential set."
            : !verified ? $"Wrong {CredentialPolicy.ToWire(kind)} ({failed + 1} in a row)."
            : devicePropertyId is not null && devicePropertyId != propertyId ? "PIN sign-in on a device of another property."
            : null;

        if (refusal is not null)
        {
            if (userId is not null && stored is not null && !verified)
            {
                // Consecutive-failure counter for the administrators' view only. There is no hard lockout here: throttling is per
                // client address and per (address, sign-in name) in the API (F-M02), so a LAN device cannot lock the Owner out.
                await using var u = connections.Command("UPDATE plat.credential SET failed_count = failed_count + 1 WHERE user_id = @u AND kind = @k;", conn, tx);
                u.Parameters.Add("@u", SqlDbType.Int).Value = userId.Value;
                u.Parameters.Add("@k", SqlDbType.VarChar, 8).Value = CredentialPolicy.ToWire(kind);
                await u.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // The precise reason is kept in the audit row; the caller always gets the same message and reason (F-M01).
            await AccessAudit.WriteAsync(connections, conn, tx, userId is null ? 0 : propertyId, userId ?? 0, "SIGN_IN_FAILED",
                userId is null ? "user:unknown" : $"user:{userId}", refusal, "SignIn", ct).ConfigureAwait(false);
            return (null, Forbidden(SignInFailed, "BAD_CREDENTIALS"));
        }

        await using (var u = connections.Command("UPDATE plat.credential SET failed_count = 0, locked_until_utc = NULL WHERE user_id = @u AND kind = @k;", conn, tx))
        {
            u.Parameters.Add("@u", SqlDbType.Int).Value = userId!.Value;
            u.Parameters.Add("@k", SqlDbType.VarChar, 8).Value = CredentialPolicy.ToWire(kind);
            await u.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var uid = userId.Value;
        var token = AccessTokens.NewToken();
        var absolute = now + options.AbsoluteLifetime;
        long sessionId;
        await using (var ins = connections.Command("""
            INSERT plat.session(token_sha256, user_id, property_id, credential_kind, device_id, security_stamp, created_utc, last_seen_utc, absolute_expires_utc)
            OUTPUT INSERTED.session_id VALUES (@h, @u, @p, @k, @d, @s, @now, @now, @abs);
            UPDATE plat.device SET last_seen_utc = @now WHERE device_id = @d;
            """, conn, tx))
        {
            ins.Parameters.Add("@h", SqlDbType.Binary, 32).Value = AccessTokens.Sha256(token);
            ins.Parameters.Add("@u", SqlDbType.Int).Value = uid;
            ins.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            ins.Parameters.Add("@k", SqlDbType.VarChar, 8).Value = CredentialPolicy.ToWire(kind);
            ins.Parameters.Add("@d", SqlDbType.Int).Value = (object?)deviceId ?? DBNull.Value;
            ins.Parameters.Add("@s", SqlDbType.Int).Value = stamp;
            ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
            ins.Parameters.Add("@abs", SqlDbType.DateTime2).Value = absolute;
            sessionId = (long)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await AccessAudit.WriteAsync(connections, conn, tx, propertyId, uid, "SIGN_IN", $"session:{sessionId}",
            $"Signed in with {CredentialPolicy.ToWire(kind)}{(deviceId is null ? string.Empty : $" on device {deviceId}")}.", "SignIn", ct).ConfigureAwait(false);
        var session = new AuthenticatedSession(sessionId, uid, propertyId, displayName, kind, deviceId, Min(now + options.IdleTimeout, absolute), absolute);
        return ((session, token), null);
    }

    public Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default) => ValidateAsync(token, null, ct);

    public async Task<AuthenticatedSession> ValidateAsync(string? token, string? deviceToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) throw Invalid("NO_SESSION");
        var now = Now();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT s.session_id, s.user_id, s.property_id, s.credential_kind, s.device_id, s.security_stamp, s.created_utc, s.last_seen_utc,
                   s.absolute_expires_utc, s.revoked_utc, u.display_name, u.enabled, u.valid_from_utc, u.valid_until_utc, u.security_stamp, d.revoked_utc, d.token_sha256
              FROM plat.session s JOIN plat.app_user u ON u.user_id = s.user_id LEFT JOIN plat.device d ON d.device_id = s.device_id
             WHERE s.token_sha256 = @h;
            """, conn);
        q.Parameters.Add("@h", SqlDbType.Binary, 32).Value = AccessTokens.Sha256(token);
        long sessionId;
        AuthenticatedSession session;
        string? reason;
        await using (var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw Invalid("NO_SESSION");
            sessionId = rd.GetInt64(0);
            var created = rd.GetDateTime(6);
            var lastSeen = rd.GetDateTime(7);
            var absolute = rd.GetDateTime(8);
            DateTime? from = rd.IsDBNull(12) ? null : rd.GetDateTime(12);
            DateTime? until = rd.IsDBNull(13) ? null : rd.GetDateTime(13);
            if (!rd.IsDBNull(9)) throw Invalid("REVOKED");
            reason = !rd.GetBoolean(11) || (from is not null && now < from) || (until is not null && now >= until) ? "USER_DISABLED"
                : rd.GetInt32(5) != rd.GetInt32(14) ? "ACCESS_CHANGED"
                : !rd.IsDBNull(15) ? "DEVICE_REVOKED"
                : now >= absolute ? "ABSOLUTE_TIMEOUT"
                : !SessionLifetime.IsAlive(created, lastSeen, now, options.IdleTimeout, options.AbsoluteLifetime) ? "IDLE_TIMEOUT"
                : null;
            // F-M07: a PIN session works only from the device it was opened on. A missing or different device token is refused
            // without ending the session (a stolen token must not let its holder log the real tablet out).
            if (rd.GetString(3) == CredentialPolicy.ToWire(CredentialKind.Pin) && reason is null && !SameDevice(rd.IsDBNull(16) ? null : (byte[])rd[16], deviceToken))
                throw Invalid("DEVICE_MISMATCH");
            session = new AuthenticatedSession(sessionId, rd.GetInt32(1), rd.GetInt32(2), rd.GetString(10), CredentialPolicy.FromWire(rd.GetString(3)),
                rd.IsDBNull(4) ? null : rd.GetInt32(4), Min(now + options.IdleTimeout, absolute), absolute);
        }

        await using var u = connections.Command(reason is null
            ? "UPDATE plat.session SET last_seen_utc = @now WHERE session_id = @id AND revoked_utc IS NULL;"
            : "UPDATE plat.session SET revoked_utc = @now, revoked_reason = @r WHERE session_id = @id AND revoked_utc IS NULL;", conn);
        u.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
        u.Parameters.Add("@id", SqlDbType.BigInt).Value = sessionId;
        u.Parameters.Add("@r", SqlDbType.VarChar, 30).Value = (object?)reason ?? DBNull.Value;
        await u.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (reason is not null) throw Invalid(reason);
        return session;
    }

    public async Task SignOutAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return;
        var now = Now();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var u = connections.Command("""
            UPDATE plat.session SET revoked_utc = @now, revoked_reason = 'SIGNED_OUT'
            OUTPUT INSERTED.session_id, INSERTED.user_id, INSERTED.property_id
             WHERE token_sha256 = @h AND revoked_utc IS NULL;
            """, conn, tx);
        u.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
        u.Parameters.Add("@h", SqlDbType.Binary, 32).Value = AccessTokens.Sha256(token);
        long? sessionId = null;
        int userId = 0, propertyId = 0;
        await using (var rd = await u.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                sessionId = rd.GetInt64(0);
                userId = rd.GetInt32(1);
                propertyId = rd.GetInt32(2);
            }
        }

        if (sessionId is not null)
            await AccessAudit.WriteAsync(connections, conn, tx, propertyId, userId, "SIGN_OUT", $"session:{sessionId}", "Signed out.", "SignOut", ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private DateTime Now() => Truncate(clock.GetUtcNow().UtcDateTime);

    /// <summary>A hash nobody can match, at the same iteration count as real hashes of that kind (F-M01 timing).</summary>
    private static HashedSecret Dummy(int iterations) =>
        new(Pbkdf2SecretHasher.Algorithm, Math.Max(iterations, 1000), RandomNumberGenerator.GetBytes(16), RandomNumberGenerator.GetBytes(32));

    private HashedSecret DummyFor(CredentialKind kind) => kind == CredentialKind.Pin
        ? _dummyPin ??= Dummy(options.PinIterations)
        : _dummyPassword ??= Dummy(options.PasswordIterations);

    /// <summary>DATETIME2(3) precision so values read back compare equal.</summary>
    internal static DateTime Truncate(DateTime utc) => new(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

    private static bool SameDevice(byte[]? storedHash, string? deviceToken) =>
        storedHash is not null && !string.IsNullOrEmpty(deviceToken) && deviceToken.Length <= 200
        && CryptographicOperations.FixedTimeEquals(storedHash, AccessTokens.Sha256(deviceToken));

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string Trim(string s) => s.Length <= 40 ? s : s[..40];

    private static HccException Forbidden(string message, string reason) =>
        new(HccErrorCode.FORBIDDEN, message, SqlAccessPolicy.Details(("reason", reason)));

    /// <summary>No session, or it expired / was revoked / its user changed: 401 UNAUTHENTICATED (details.reason stays SESSION_INVALID).</summary>
    private static HccException Invalid(string reason) =>
        new(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again.", SqlAccessPolicy.Details(("reason", "SESSION_INVALID"), ("sessionEnd", reason)));
}
