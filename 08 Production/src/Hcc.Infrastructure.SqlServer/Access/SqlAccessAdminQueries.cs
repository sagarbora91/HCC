using System.Data;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>
/// W4-5 admin reads: role templates, user exceptions and the audit log (the production build's Access control and Approvals &amp;
/// audit screens). Read-only; secrets (credential hashes, device and session tokens) are never selected.
/// </summary>
public sealed class SqlAccessAdminQueries(SqlConnectionFactory connections, IAccessPolicy policy) : IAccessAdminQueries
{
    private async Task RequireAnyAsync(AuthenticatedSession actor, string[] permissionIds, CancellationToken ct)
    {
        foreach (var p in permissionIds)
            if (await policy.HasAsync(actor.PropertyId, actor.UserId, p + Domain.Access.AccessEvaluator.ViewSuffix, null, ct).ConfigureAwait(false)) return;
        throw new HccException(HccErrorCode.FORBIDDEN, "Not permitted: administration access is needed to read this.", null);
    }

    public async Task<StaffDirectoryResult> DirectoryAsync(AuthenticatedSession actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT user_id, display_name, enabled FROM plat.app_user WHERE property_id = @p ORDER BY user_id;
            SELECT r.user_id, r.role_code FROM plat.user_role r JOIN plat.app_user u ON u.user_id = r.user_id
             WHERE u.property_id = @p AND r.valid_from_utc <= SYSUTCDATETIME() AND (r.valid_until_utc IS NULL OR r.valid_until_utc > SYSUTCDATETIME());
            """, conn);
        q.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var users = new List<(int Id, string Name, bool Enabled)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) users.Add((rd.GetInt32(0), rd.GetString(1), rd.GetBoolean(2)));
        var roles = new List<(int UserId, string Role)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) roles.Add((rd.GetInt32(0), rd.GetString(1)));
        var items = users.Select(u => new StaffNameView(u.Id, u.Name, u.Enabled, roles.Where(r => r.UserId == u.Id).Select(r => r.Role).ToList())).ToList();
        return new StaffDirectoryResult(items);
    }

    public async Task<RolesResult> RolesAsync(AuthenticatedSession actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await RequireAnyAsync(actor, ["PERM-ADM-001", "PERM-ADM-002", "PERM-ADM-003"], ct).ConfigureAwait(false);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT role_code, name, credential_kind, owner_only_assign FROM plat.role ORDER BY sort_order, role_code;
            SELECT role_code, permission_id, mode, scope, limit_value, limit_unit FROM plat.role_grant ORDER BY role_code, permission_id;
            SELECT role_code, permission_id, reason FROM plat.role_deny ORDER BY role_code, permission_id;
            SELECT permission_id, resource_action, kind, is_money FROM plat.permission ORDER BY permission_id;
            """, conn);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var roles = new List<(string Code, string Name, string Kind, bool OwnerOnly)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) roles.Add((rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetBoolean(3)));
        var grants = new List<(string Role, CapabilityGrant Grant)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            grants.Add((rd.GetString(0), new CapabilityGrant(rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetInt64(4), rd.IsDBNull(5) ? null : rd.GetString(5))));
        var denies = new List<(string Role, RoleDenyView Deny)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) denies.Add((rd.GetString(0), new RoleDenyView(rd.GetString(1), rd.GetString(2))));
        var perms = new List<PermissionView>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) perms.Add(new PermissionView(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetBoolean(3)));
        return new RolesResult(roles.Select(r => new RoleTemplateView(r.Code, r.Name, r.Kind, r.OwnerOnly,
            grants.Where(g => g.Role == r.Code).Select(g => g.Grant).ToList(), denies.Where(d => d.Role == r.Code).Select(d => d.Deny).ToList())).ToList(), perms);
    }

    public async Task<ExceptionsResult> ExceptionsAsync(AuthenticatedSession actor, bool includeRevoked, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await RequireAnyAsync(actor, ["PERM-ADM-001", "PERM-ADM-002"], ct).ConfigureAwait(false);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT e.exception_id, e.user_id, e.permission_id, e.effect, e.mode, e.scope, e.limit_value, e.limit_unit, e.reason, e.valid_until_utc,
                   e.granted_by, e.granted_utc, e.revoked_utc
              FROM plat.user_exception e JOIN plat.app_user u ON u.user_id = e.user_id
             WHERE u.property_id = @p AND (@all = 1 OR e.revoked_utc IS NULL)
             ORDER BY e.exception_id;
            """, conn);
        q.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
        q.Parameters.Add("@all", SqlDbType.Bit).Value = includeRevoked;
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var items = new List<UserExceptionView>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new UserExceptionView(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetString(5),
                rd.IsDBNull(6) ? null : rd.GetInt64(6), rd.IsDBNull(7) ? null : rd.GetString(7), rd.GetString(8), rd.GetDateTime(9), rd.GetInt32(10), rd.GetDateTime(11),
                rd.IsDBNull(12) ? null : rd.GetDateTime(12)));
        return new ExceptionsResult(items);
    }

    public async Task<AuditPage> AuditAsync(AuthenticatedSession actor, int page, int pageSize, string? action, int? principalId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (page < 1) throw HccException.Validation("page starts at 1.");
        if (pageSize < 1 || pageSize > ApiConventions.MaxPageSize) throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.");
        if (from is not null && to is not null && to < from) throw HccException.Validation("'to' must not be before 'from'.");
        await policy.RequireAsync(actor.PropertyId, actor.UserId, "PERM-ADM-004", ct: ct).ConfigureAwait(false);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        const string Where = """
            WHERE a.property_id = @p AND (@action IS NULL OR a.action = @action) AND (@who IS NULL OR a.principal_id = @who)
              AND (@from IS NULL OR a.at_utc >= @from) AND (@to IS NULL OR a.at_utc < @to)
            """;
        await using var q = connections.Command($"""
            SELECT COUNT(*) FROM plat.audit_event a {Where};
            SELECT a.audit_id, a.at_utc, a.principal_id, COALESCE(u.display_name, N'System'), a.action, a.target, a.detail, a.operation
              FROM plat.audit_event a LEFT JOIN plat.app_user u ON u.user_id = a.principal_id {Where}
             ORDER BY a.audit_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """, conn);
        q.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
        q.Parameters.Add("@action", SqlDbType.VarChar, 60).Value = string.IsNullOrWhiteSpace(action) ? DBNull.Value : action.Trim();
        q.Parameters.Add("@who", SqlDbType.Int).Value = (object?)principalId ?? DBNull.Value;
        // Dates are hotel-local (Asia/Kolkata, +05:30): [from 00:00 IST, to+1 00:00 IST).
        q.Parameters.Add("@from", SqlDbType.DateTime2).Value = from is { } f ? f.ToDateTime(TimeOnly.MinValue).AddMinutes(-330) : DBNull.Value;
        q.Parameters.Add("@to", SqlDbType.DateTime2).Value = to is { } t ? t.AddDays(1).ToDateTime(TimeOnly.MinValue).AddMinutes(-330) : DBNull.Value;
        q.Parameters.Add("@skip", SqlDbType.Int).Value = (page - 1) * pageSize;
        q.Parameters.Add("@take", SqlDbType.Int).Value = pageSize;
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var total = rd.GetInt32(0);
        var items = new List<AuditEventView>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new AuditEventView(rd.GetInt64(0), DateTime.SpecifyKind(rd.GetDateTime(1), DateTimeKind.Utc), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), rd.GetString(5), rd.GetString(6),
                rd.IsDBNull(7) ? null : rd.GetString(7)));
        return new AuditPage(page, pageSize, total, items);
    }
}
