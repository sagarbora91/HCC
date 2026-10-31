using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Masters;
using Hcc.Domain.Accounting;
using Hcc.Domain.Masters;

namespace Hcc.Application.Groups;

// ACC-2 flows · company retire (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; ACC1-DONE §6 / §7 item 10; DEC-157). Prototype twin:
// GRP_COMPANY_SAVE {active} (services/mock/handlers/groups.ts) with grpRetireBlocker + accPartyRetireBlocker.

/// <summary>SQL adapter: SqlFlowsStore (grp.company_account active / retired_by / retired_utc / retire_reason, migration 4253).</summary>
public interface ICompanyRetireStore
{
    Task SetActiveAsync(ICommandTransaction tx, long companyId, bool active, int principalId, DateTime utc, string? reason, CancellationToken ct = default);
}

/// <summary>
/// SetCompanyActive (PERM-GRP-002 group.manage): retiring refuses (MASTER_IN_USE) open stays billed to the company, open group masters, an open
/// company-credit receivable and a non-zero 1110/CMP-&lt;id&gt; ledger balance (DEC-157); reactivating needs nothing. A retired company takes no
/// new booking, block or credit (<see cref="CompanyCreditCheck.NewAsync"/>) and is flagged in GET /groups/companies (<c>active</c>).
/// </summary>
public sealed class CompanyRetireService(ICommandRunner runner, ICompanyStore companies, ICompanyCreditStore credit, ICompanyRetireStore store, IAccessPolicy policy,
    TimeProvider clock, IAccountingStore? accounting = null)
{
    public const string Op = "SetCompanyActive";

    public async Task<CommandResult<CompanyActiveResult>> SetActiveAsync(CommandContext ctx, SetCompanyActiveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GroupPermissions.Manage, ct: ct).ConfigureAwait(false);
        var trimmed = (req.Reason ?? string.Empty).Trim();
        var reason = trimmed.Length == 0 ? null : trimmed[..Math.Min(200, trimmed.Length)];
        return await runner.RunAsync(ctx with { Operation = Op }, req, async (tx, c) =>
        {
            var co = await companies.LockAsync(tx, ctx.PropertyId, req.CompanyId, c).ConfigureAwait(false) ?? throw GroupCommands.NotFound("Company account not found.");
            if (co.Active == req.Active)
                return new CompanyActiveResult(co.CompanyId, co.Active, $"{co.Name} is already {(co.Active ? "active" : "retired")}.");
            if (!req.Active && await BlockerAsync(tx, co, c).ConfigureAwait(false) is { } why) throw MastersRules.Fail(MasterReasons.InUse, why);
            await store.SetActiveAsync(tx, co.CompanyId, req.Active, ctx.PrincipalId, clock.GetUtcNow().UtcDateTime, reason, c).ConfigureAwait(false);
            var msg = $"{co.Name} {(req.Active ? "reactivated" : "retired (hidden from pickers)")}{(reason is null ? "" : " · " + reason)}";
            await tx.AuditAsync(req.Active ? "COMPANY_REACTIVATED" : "COMPANY_RETIRED", "company:" + co.CompanyId.ToString(CultureInfo.InvariantCulture), msg, c).ConfigureAwait(false);
            return new CompanyActiveResult(co.CompanyId, req.Active, msg);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Why the company cannot be retired now (null = it can); same order and wording as the prototype.</summary>
    public async Task<string?> BlockerAsync(ICommandTransaction tx, CompanyRow co, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(co);
        var f = await credit.LoadAsync(tx, co.PropertyId, co.CompanyId, ct).ConfigureAwait(false);
        if (f.Stays.Count > 0)
            return $"{co.Name} has {f.Stays.Count} open stay{(f.Stays.Count == 1 ? "" : "s")} billed to it ({string.Join(", ", f.Stays.Take(3).Select(s => s.PublicNo))}"
                   + $"{(f.Stays.Count > 3 ? ", …" : "")}) — move or settle them before retiring the account.";
        if (f.GroupIds.Count > 0)
            return $"{co.Name} has {f.GroupIds.Count} open group master{(f.GroupIds.Count == 1 ? "" : "s")} — settle or cancel {(f.GroupIds.Count == 1 ? "it" : "them")} first.";
        var owed = f.Credits.Sum(r => r.Amount.Value) - f.Payments.Sum(p => p.Amount.Value);
        if (owed > 0) return $"{co.Name} still owes {CashGuard.Rs(owed)} on company credit — record the payment before retiring the account.";
        if (accounting is null) return null;
        var usage = await ChartService.UsageAsync(accounting, tx, co.PropertyId, "1110", ct).ConfigureAwait(false);
        var party = PartyLedgers.Company(co.CompanyId);
        if (!usage.PartyBalances.TryGetValue(party, out var b) || b == 0) return null;
        return $"{co.Name} has a ledger balance of ₹{(Math.Abs(b) / 100m).ToString("0.00", CultureInfo.InvariantCulture)} {(b > 0 ? "Dr" : "Cr")} on 1110/{party} — settle it to zero before retiring it (rulebook §3.3.1).";
    }
}
