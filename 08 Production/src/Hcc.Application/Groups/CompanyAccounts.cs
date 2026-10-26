using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Groups;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;

namespace Hcc.Application.Groups;

public sealed record CompanyRow(long CompanyId, int PropertyId, string Name, string? Gstin, string? Contact, string? Phone, string? Email, string? City,
    IReadOnlyDictionary<string, long> Rates, long CreditLimitPaise, string Version);

public sealed record CompanyDetails(string Name, string? Gstin, string? Contact, string? Phone, string? Email, string? City, IReadOnlyDictionary<string, long> Rates);

public sealed record CompanyLimitRow(long RequestId, long CompanyId, long FromPaise, long ToPaise, string Reason, string Status, int RequestedBy, DateTime RequestedUtc,
    int? DecidedBy, string? Note);

/// <summary>Persistence of company accounts (SQL adapter: Hcc.Infrastructure.SqlServer/Groups/SqlCompanyStore, migration 2202).</summary>
public interface ICompanyStore
{
    /// <summary>The company row under UPDLOCK + HOLDLOCK (serializes every command of one company); null when absent.</summary>
    Task<CompanyRow?> LockAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct = default);

    /// <summary>True when another company of the property already has this name (case-insensitive).</summary>
    Task<bool> NameTakenAsync(ICommandTransaction tx, int propertyId, string name, long? exceptCompanyId, CancellationToken ct = default);

    Task<CompanyRow> InsertAsync(ICommandTransaction tx, int propertyId, CompanyDetails details, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Versioned update of the master fields; STALE_VERSION when <paramref name="expectedVersion"/> no longer matches.</summary>
    Task<string> UpdateAsync(ICommandTransaction tx, long companyId, string expectedVersion, CompanyDetails details, CancellationToken ct = default);

    Task<string> SetLimitAsync(ICommandTransaction tx, long companyId, long limitPaise, CancellationToken ct = default);

    /// <summary>Pending requests of the company become Rejected (superseded by a direct change).</summary>
    Task SupersedePendingAsync(ICommandTransaction tx, long companyId, int principalId, DateTime utc, CancellationToken ct = default);

    Task<long> InsertRequestAsync(ICommandTransaction tx, long companyId, long from, long to, string reason, int principalId, DateTime utc, CancellationToken ct = default);

    Task<CompanyLimitRow?> PendingRequestAsync(ICommandTransaction tx, long companyId, CancellationToken ct = default);

    /// <summary>The request row (locked); null when absent.</summary>
    Task<CompanyLimitRow?> LockRequestAsync(ICommandTransaction tx, long requestId, CancellationToken ct = default);

    Task DecideRequestAsync(ICommandTransaction tx, long requestId, string status, int principalId, DateTime utc, string? note, CancellationToken ct = default);

    Task<IReadOnlyList<CompanyRow>> ListAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<CompanyLimitRow>> ListRequestsAsync(int propertyId, CancellationToken ct = default);
}

/// <summary>
/// Company accounts of the Groups module (W4 N1; prototype <c>GRP_COMPANY_SAVE</c> / <c>GRP_LIMIT_SET</c> / <c>GRP_LIMIT_DECIDE</c>).
/// Save needs PERM-GRP-002. A credit limit is maker-checker: an approver (PERM-GRP-003 within its amount limit) sets it directly,
/// anyone else with group.manage raises a request that a different approver decides (PERM-SOD-001). Every command goes through
/// <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox).
/// </summary>
public sealed class CompanyCommands(ICommandRunner runner, ICompanyStore store, IAccessPolicy policy, TimeProvider clock)
{
    public const string OpSave = "SaveCompany";
    public const string OpLimit = "SetCompanyLimit";
    public const string OpDecide = "DecideCompanyLimit";

    public const long MaxLimitPaise = 100_000_000_000; // Rs 100 crore, as the schema check

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<CompanyResult>> SaveAsync(CommandContext ctx, SaveCompanyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        var details = new CompanyDetails(GroupText.Required(req.Name, 200, "Enter the company name."), GroupText.Gstin(req.Gstin), GroupText.Optional(req.Contact, 120),
            GroupText.Phone(req.Phone), GroupText.Email(req.Email), GroupText.Optional(req.City, 80), Rates(req.Rates));
        if (req.CompanyId is not null) Version(req.ExpectedVersion);
        var limit = Limit(req.CreditLimitPaise);
        var direct = limit > 0 && req.CompanyId is null && await CanSetDirectlyAsync(ctx, limit, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            if (await store.NameTakenAsync(tx, ctx.PropertyId, details.Name, req.CompanyId, c).ConfigureAwait(false))
                throw GroupCommands.Invalid("A company account with this name already exists.");
            if (req.CompanyId is { } id)
            {
                _ = await store.LockAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false) ?? throw GroupCommands.NotFound("Company account not found.");
                var v = await store.UpdateAsync(tx, id, req.ExpectedVersion!, details, c).ConfigureAwait(false);
                await tx.AuditAsync("COMPANY_UPDATED", Target(id), details.Name + (details.Gstin is null ? "" : " · GSTIN " + details.Gstin), c).ConfigureAwait(false);
                return new CompanyResult(id, v, "none", null);
            }

            var row = await store.InsertAsync(tx, ctx.PropertyId, details, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            var version = row.Version;
            var status = "none";
            long? requestId = null;
            if (limit > 0)
            {
                if (direct)
                {
                    version = await store.SetLimitAsync(tx, row.CompanyId, limit, c).ConfigureAwait(false);
                    status = "set";
                }
                else
                {
                    requestId = await store.InsertRequestAsync(tx, row.CompanyId, 0, limit, "Opening limit for new account", ctx.PrincipalId, Now, c).ConfigureAwait(false);
                    status = "requested";
                }
            }

            await tx.AuditAsync("COMPANY_CREATED", Target(row.CompanyId), details.Name + (details.Gstin is null ? "" : " · GSTIN " + details.Gstin)
                + (status == "none" ? "" : $" · credit limit {Rupees(limit)} {(status == "set" ? "set" : "requested")}"), c).ConfigureAwait(false);
            await tx.PublishAsync("CompanyCreated", row.CompanyId, new { companyId = row.CompanyId, name = details.Name }, c).ConfigureAwait(false);
            return new CompanyResult(row.CompanyId, version, status, requestId);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CompanyResult>> SetLimitAsync(CommandContext ctx, SetCompanyLimitRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var limit = Limit(req.LimitPaise);
        var reason = GroupText.Optional(req.Reason, 200);
        var direct = await CanSetDirectlyAsync(ctx, limit, ct).ConfigureAwait(false);
        if (!direct)
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
            if (reason is null || reason.Length < 3) throw HccException.Validation("Give a reason for the credit-limit request.");
        }

        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var co = await store.LockAsync(tx, ctx.PropertyId, req.CompanyId, c).ConfigureAwait(false) ?? throw GroupCommands.NotFound("Company account not found.");
            if (limit == co.CreditLimitPaise) throw GroupCommands.Invalid("The new limit is the same as the current limit.");
            if (direct)
            {
                await store.SupersedePendingAsync(tx, co.CompanyId, ctx.PrincipalId, Now, c).ConfigureAwait(false);
                var v = await store.SetLimitAsync(tx, co.CompanyId, limit, c).ConfigureAwait(false);
                await tx.AuditAsync("COMPANY_LIMIT_SET", Target(co.CompanyId), $"{co.Name} · credit limit {Rupees(co.CreditLimitPaise)} → {Rupees(limit)}" + (reason is null ? "" : " · " + reason), c)
                    .ConfigureAwait(false);
                return new CompanyResult(co.CompanyId, v, "set", null);
            }

            if (await store.PendingRequestAsync(tx, co.CompanyId, c).ConfigureAwait(false) is not null)
                throw GroupCommands.Invalid("A credit-limit request for this company is already pending.");
            var rid = await store.InsertRequestAsync(tx, co.CompanyId, co.CreditLimitPaise, limit, reason!, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPANY_LIMIT_REQUESTED", Target(co.CompanyId), $"{co.Name} · credit limit {Rupees(co.CreditLimitPaise)} → {Rupees(limit)} requested · {reason}", c)
                .ConfigureAwait(false);
            return new CompanyResult(co.CompanyId, co.Version, "requested", rid);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CompanyResult>> DecideAsync(CommandContext ctx, DecideCompanyLimitRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var note = GroupText.Optional(req.Note, 200);
        return await runner.RunAsync(ctx, req, async (tx, c) =>
        {
            var r = await store.LockRequestAsync(tx, req.RequestId, c).ConfigureAwait(false) ?? throw GroupCommands.NotFound("Credit-limit request not found.");
            var co = await store.LockAsync(tx, ctx.PropertyId, r.CompanyId, c).ConfigureAwait(false) ?? throw GroupCommands.NotFound("Company account not found.");
            if (r.Status != "Pending") throw GroupCommands.Invalid("This credit-limit request is no longer pending.");
            if (r.RequestedBy == ctx.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot decide your own request (PERM-SOD-001).");
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Approve, amountPaise: r.ToPaise, ct: c).ConfigureAwait(false);
            var version = co.Version;
            if (req.Approve) version = await store.SetLimitAsync(tx, co.CompanyId, r.ToPaise, c).ConfigureAwait(false);
            await store.DecideRequestAsync(tx, r.RequestId, req.Approve ? "Approved" : "Rejected", ctx.PrincipalId, Now, note, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPANY_LIMIT_DECIDED", Target(co.CompanyId), $"{co.Name} · limit request {Rupees(r.FromPaise)} → {Rupees(r.ToPaise)} "
                + (req.Approve ? "approved" : "rejected") + (note is null ? "" : " · " + note), c).ConfigureAwait(false);
            return new CompanyResult(co.CompanyId, version, req.Approve ? "set" : "none", r.RequestId);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>True when the caller may set a limit of this size without a second person: PERM-GRP-003 within its amount limit.</summary>
    private async Task<bool> CanSetDirectlyAsync(CommandContext ctx, long limitPaise, CancellationToken ct)
    {
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Approve, amountPaise: limitPaise, ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (HccException e) when (e.Code is HccErrorCode.FORBIDDEN or HccErrorCode.APPROVAL_REQUIRED)
        {
            return false;
        }
    }

    private static long Limit(long paise)
        => paise is < 0 or > MaxLimitPaise ? throw HccException.Validation("Enter a credit limit between 0 and Rs 100 crore.") : paise;

    private static SortedDictionary<string, long> Rates(IReadOnlyDictionary<string, long>? rates)
    {
        var map = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var (code, paise) in rates ?? new Dictionary<string, long>())
        {
            var k = code?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(k) || k.Length > 20) throw HccException.Validation("Use the room type code for each negotiated rate.");
            if (paise is < 0 or > 10_000_000) throw HccException.Validation("A negotiated nightly rate must be between 0 and Rs 1,00,000.");
            if (paise > 0) map[k] = paise;
        }

        return map;
    }

    private static void Version(string? token)
    {
        var buffer = new byte[12];
        if (string.IsNullOrEmpty(token) || !Convert.TryFromBase64String(token, buffer, out var n) || n != 8)
            throw HccException.Validation("expectedVersion is not a valid version token.");
    }

    private static string Rupees(long paise) => "Rs " + (paise / 100m).ToString("N2", CultureInfo.InvariantCulture);

    private static string Target(long id) => "company:" + id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Company list and pending limit requests (PERM-GRP-001).</summary>
public sealed class CompanyQueries(ICompanyStore store, IAccessPolicy policy)
{
    public async Task<CompanyBookView> BookAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, GroupPermissions.View, ct: ct).ConfigureAwait(false);
        var companies = await store.ListAsync(propertyId, ct).ConfigureAwait(false);
        var requests = await store.ListRequestsAsync(propertyId, ct).ConfigureAwait(false);
        return new CompanyBookView(
            companies.Select(c => new CompanyView(c.CompanyId, c.Name, c.Gstin, c.Contact, c.Phone, c.Email, c.City, c.Rates, c.CreditLimitPaise, c.Version)).ToList(),
            requests.Select(r => new CompanyLimitRequestView(r.RequestId, r.CompanyId, r.FromPaise, r.ToPaise, r.Reason, r.Status, r.RequestedBy, r.RequestedUtc, r.DecidedBy, r.Note)).ToList());
    }
}
