using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Configuration;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;

namespace Hcc.Application.Configuration;

/// <summary>
/// Master configuration commands (prototype handlers <c>SETTING_SET</c>, <c>CFG_DECIDE</c>, <c>CFG_WITHDRAW</c>, <c>CFG_REVERT</c>,
/// <c>CFG_IMPORT</c> in <c>services/mock/handlers/admin.ts</c>): DEC-058 permission → validation → effective date → approval (Tech admin on
/// settings with an approver) → history (raw before/after kept for revert) → value applied now or scheduled. Every command runs
/// through <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox <c>SettingChanged</c>); actor = the session.
/// </summary>
public sealed class ConfigurationService(ICommandRunner runner, IAccessPolicy policy, SettingsRegistry registry, IConfigurationStore store, TimeProvider clock, Hcc.Application.Closing.IBusinessDayGuard? days = null)
{
    public const string OpSet = "SetSetting";
    public const string OpDecide = "DecideSettingRequest";
    public const string OpWithdraw = "WithdrawScheduledSetting";
    public const string OpRevert = "RevertSetting";
    public const string OpImport = "ImportConfiguration";

    /// <summary>Outbox event after a value is applied or scheduled (payload: key, effectiveFrom, changeId — never the value).</summary>
    public const string SettingChangedEvent = "SettingChanged";

    public const string ExportFormat = "hcc-master-configuration";

    private readonly ConfigAuthorityResolver _authority = new(policy);

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<SetSettingResult>> SetSettingAsync(CommandContext ctx, SetSettingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var authority = await GateAsync(ctx, ct).ConfigureAwait(false);
        var s = registry.Find(req.Key);
        if (s is null || !s.Editable) throw HccException.Validation(SettingRules.NotConfigurable);
        var expected = string.IsNullOrEmpty(req.ExpectedVersion) ? null : VersionToken.Parse(req.ExpectedVersion);
        var reason = Reason(req.Reason, 120);
        return await runner.RunAsync(ctx with { Operation = OpSet }, req, async (tx, c) =>
        {
            var version = await store.LockSettingAsync(tx, ctx.PropertyId, s.Key, c).ConfigureAwait(false);
            if (!(version is null ? expected is null : expected is not null && version.AsSpan().SequenceEqual(expected)))
                throw new HccException(HccErrorCode.STALE_VERSION, $"{s.Label} was changed by someone else. Reload and try again.");
            return await ChangeAsync(tx, ctx, s, authority, ConfigValue.FromJson(req.Value), req.EffectiveFrom, reason, null, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<DecideSettingRequestResult>> DecideAsync(CommandContext ctx, DecideSettingRequestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        // No configuration gate: a Manager decides requests whose approver is Manager (from Approvals). DecideBlock is the gate.
        var authority = await _authority.ResolveAsync(ctx.PropertyId, ctx.PrincipalId, ct).ConfigureAwait(false);
        var approve = await _authority.HoldsApproveAsync(ctx.PropertyId, ctx.PrincipalId, ct).ConfigureAwait(false);
        if (authority == ConfigAuthority.None && !approve)
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ConfigurationPermissions.DecideManagerApproved, null, null, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var reason = Reason(req.Reason, 200);
        return await runner.RunAsync(ctx with { Operation = OpDecide }, req, async (tx, c) =>
        {
            var r = await store.LockRequestAsync(tx, ctx.PropertyId, req.RequestId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Request not found.");
            if (r.Status != "Pending") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request is no longer pending.");
            var block = SettingRules.DecideBlock(r.RequestedBy, Enum.Parse<SettingApprover>(r.Approver), ctx.PrincipalId, authority, approve);
            if (block is not null) throw new HccException(HccErrorCode.FORBIDDEN, block);
            VersionToken.EnsureMatches(r.Version, expected, "This request");
            var now = UtcNow;
            if (!req.Approve)
            {
                await store.DecideRequestAsync(tx, r.RequestId, expected, "Rejected", ctx.PrincipalId, now, reason, null, c).ConfigureAwait(false);
                await tx.AuditAsync("CFG_DECIDE", Target("cfg-request", r.RequestId), $"Request {r.RequestId} · Rejected · {r.Summary}", c).ConfigureAwait(false);
                return new DecideSettingRequestResult(r.RequestId, "Rejected", r.Key, null, null, $"Request {r.RequestId} · Rejected · {r.Summary}");
            }

            if (now > r.ExpiresUtc) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request has expired. Reject it and ask for a new one.");
            var s = registry.Find(r.Key) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This setting is no longer in the registry.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var effectiveFrom = r.EffectiveFrom < bd ? SettingRules.EffectiveFrom(s, bd, null).Date : r.EffectiveFrom;
            await store.LockSettingAsync(tx, ctx.PropertyId, s.Key, c).ConfigureAwait(false);
            var versions = await VersionsAsync(tx, ctx.PropertyId, s.Key, c).ConfigureAwait(false);
            if (SettingRules.ValueOn(versions, effectiveFrom, s.Default) != ConfigValue.Parse(r.BeforeJson))
                throw new HccException(HccErrorCode.STALE_VERSION, "The setting changed after this request. Reject it and request again.");
            var value = ConfigValue.Parse(r.ValueJson);
            if (SettingRules.Validate(s, value) is { } err) throw HccException.Validation(err);
            var requester = await store.DisplayNameAsync(tx, r.RequestedBy, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var applied = await ApplyAsync(tx, ctx, s, value, effectiveFrom, bd, $"Approved request {r.RequestId} from {requester}", "approval",
                r.RequestedBy, ctx.PrincipalId, r.RequestId, r.RevertOf, c).ConfigureAwait(false);
            await store.DecideRequestAsync(tx, r.RequestId, expected, "Executed", ctx.PrincipalId, now, reason, applied.ChangeId, c).ConfigureAwait(false);
            var msg = $"Request {r.RequestId} · approved and applied · {s.Key} · {applied.Before} → {applied.After} · {(applied.Status == "applied" ? "effective" : "scheduled from")} {applied.EffectiveFrom}";
            await tx.AuditAsync("CFG_DECIDE", Target("cfg-request", r.RequestId), msg, c).ConfigureAwait(false);
            return new DecideSettingRequestResult(r.RequestId, "Executed", r.Key, applied.ChangeId, applied.EffectiveFrom, msg);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WithdrawScheduledSettingResult>> WithdrawAsync(CommandContext ctx, WithdrawScheduledSettingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var authority = await GateAsync(ctx, ct).ConfigureAwait(false);
        var reason = Reason(req.Reason, 200);
        return await runner.RunAsync(ctx with { Operation = OpWithdraw }, req, async (tx, c) =>
        {
            var row = await store.ChangeAsync(tx, ctx.PropertyId, req.ChangeId, c).ConfigureAwait(false);
            if (row is null || row.Withdrawn) throw new HccException(HccErrorCode.NOT_FOUND, "Scheduled change not found (it may already be in effect).");
            await store.LockSettingAsync(tx, ctx.PropertyId, row.Key, c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (row.EffectiveFrom <= bd) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only future-dated changes can be withdrawn.");
            var s = registry.Find(row.Key) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This setting is no longer in the registry.");
            var dec = SettingRules.Decide(s, authority);
            if (dec.Mode != ChangeMode.Allow)
                throw new HccException(HccErrorCode.FORBIDDEN, dec.Mode == ChangeMode.Request ? "Ask the approver to withdraw this scheduled change." : dec.Reason);
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            await store.WithdrawChangeAsync(tx, row.ChangeId, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await store.TouchSettingAsync(tx, ctx.PropertyId, s.Key, UtcNow, c).ConfigureAwait(false);
            var when = SettingRules.Wire(row.EffectiveFrom);
            var msg = $"Scheduled change withdrawn · {s.Key} · {row.AfterText} from {when}";
            await tx.AuditAsync("CFG_WITHDRAW", Target("cfg-change", row.ChangeId), msg + (reason is null ? "" : " · " + reason), c).ConfigureAwait(false);
            await tx.PublishAsync(SettingChangedEvent, row.ChangeId, new { key = s.Key, effectiveFrom = when, changeId = row.ChangeId, withdrawn = true }, c).ConfigureAwait(false);
            return new WithdrawScheduledSettingResult(row.ChangeId, s.Key, when, msg);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<SetSettingResult>> RevertAsync(CommandContext ctx, RevertSettingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var authority = await GateAsync(ctx, ct).ConfigureAwait(false);
        var reason = Reason(req.Reason, 120) ?? "Revert to previous value";
        return await runner.RunAsync(ctx with { Operation = OpRevert }, req, async (tx, c) =>
        {
            var row = await store.ChangeAsync(tx, ctx.PropertyId, req.ChangeId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Change not found.");
            await store.LockSettingAsync(tx, ctx.PropertyId, row.Key, c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var all = await store.ChangesAsync(tx, ctx.PropertyId, row.Key, c).ConfigureAwait(false);
            var s = registry.Find(row.Key);
            var block = RevertBlock(row, all, s, bd);
            if (block is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, block);
            // A revert is a normal change back to the previous value: same permission, approval and effective-date rules.
            return await ChangeAsync(tx, ctx, s!, authority, ConfigValue.Parse(row.BeforeJson), req.EffectiveFrom, reason, row.ChangeId, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ImportConfigurationResult>> ImportAsync(CommandContext ctx, ImportConfigurationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var authority = await GateAsync(ctx, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpImport }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var plan = await PlanAsync(req.Config, authority, bd, (key, cc) => VersionsAsync(tx, ctx.PropertyId, key, cc), c).ConfigureAwait(false);
            if (plan.Errors.Count > 0)
                throw HccException.Validation(string.Join(" · ", plan.Errors.Take(3)) + (plan.Errors.Count > 3 ? $" (+{plan.Errors.Count - 3} more)" : ""));
            if (plan.Apply.Count == 0) throw HccException.Validation($"Nothing to import: {plan.Unchanged} unchanged, {plan.Skipped.Count} skipped.");
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            foreach (var (s, value, eff) in plan.Apply)
            {
                await store.LockSettingAsync(tx, ctx.PropertyId, s.Key, c).ConfigureAwait(false);
                await ApplyAsync(tx, ctx, s, value, eff, bd, "Imported configuration", "import", ctx.PrincipalId, null, null, null, c).ConfigureAwait(false);
            }

            var msg = $"Configuration imported · {plan.Apply.Count} changed · {plan.Unchanged} unchanged · {plan.Skipped.Count} skipped";
            await tx.AuditAsync("CFG_IMPORT", "cfg-import", msg, c).ConfigureAwait(false);
            return new ImportConfigurationResult(plan.Apply.Count, plan.Unchanged, plan.Skipped, msg);
        }, ct).ConfigureAwait(false);
    }

    // ───────────── shared rules ─────────────

    /// <summary>Hub gate: PERM-ADM-003 at any mode (Owner A, Tech admin V); DEC-058 rights for the rest of the command.</summary>
    private async Task<ConfigAuthority> GateAsync(CommandContext ctx, CancellationToken ct)
    {
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ConfigurationPermissions.ConfigurationView, null, null, ct).ConfigureAwait(false);
        return await _authority.ResolveAsync(ctx.PropertyId, ctx.PrincipalId, ct).ConfigureAwait(false);
    }

    /// <summary>Validate and route one change for the caller (prototype <c>cfgChangeCommand</c>): apply/schedule, or raise a request.</summary>
    private async Task<SetSettingResult> ChangeAsync(ICommandTransaction tx, CommandContext ctx, RegisteredSetting s, ConfigAuthority authority, ConfigValue raw,
        string? requestedDate, string? reason, long? revertOf, CancellationToken c)
    {
        var dec = SettingRules.Decide(s, authority);
        if (dec.Mode == ChangeMode.Deny) throw new HccException(HccErrorCode.FORBIDDEN, dec.Reason);
        var value = SettingRules.Clean(s, raw);
        if (SettingRules.Validate(s, value) is { } err) throw HccException.Validation(err);
        var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
        var (effectiveFrom, dateError) = SettingRules.EffectiveFrom(s, bd, requestedDate);
        if (dateError is not null) throw HccException.Validation(dateError);
        if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
        if (dec.Mode == ChangeMode.Request)
        {
            if (await store.PendingRequestAsync(tx, ctx.PropertyId, s.Key, c).ConfigureAwait(false) is not null)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "A change to this setting is already waiting for approval.");
            var versions = await VersionsAsync(tx, ctx.PropertyId, s.Key, c).ConfigureAwait(false);
            var before = SettingRules.ValueOn(versions, effectiveFrom, s.Default);
            if (before == value) throw HccException.Validation(SettingRules.NoChange);
            var hours = await HoursAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var summary = $"Master configuration · {s.Label} · {SettingRules.Show(s, before)} → {SettingRules.Show(s, value)}";
            var now = UtcNow;
            var id = await store.InsertRequestAsync(tx, new NewSettingRequest(ctx.PropertyId, s.Key, value.ToJson(), before.ToJson(), effectiveFrom,
                dec.Approver.ToString(), summary, reason, revertOf, ctx.PrincipalId, now, now.AddHours(hours)), c).ConfigureAwait(false);
            var approver = dec.Approver == SettingApprover.Owner ? "Owner" : "Manager";
            var msg = $"Request {id} · configuration change requested ({approver} approval): {s.Key} · {SettingRules.Show(s, before)} → {SettingRules.Show(s, value)}";
            await tx.AuditAsync("CFG_REQUEST", Target("cfg-request", id), msg + (reason is null ? "" : " — " + reason), c).ConfigureAwait(false);
            return new SetSettingResult("requested", s.Key, null, id, SettingRules.Wire(effectiveFrom), SettingRules.Show(s, before), SettingRules.Show(s, value), null, msg);
        }

        return await ApplyAsync(tx, ctx, s, value, effectiveFrom, bd, reason, revertOf is null ? "change" : "revert", ctx.PrincipalId, null, null, revertOf, c).ConfigureAwait(false);
    }

    /// <summary>Writes one dated value (prototype <c>cfgApply</c>). A new future value for the same key and date replaces the earlier one.</summary>
    private async Task<SetSettingResult> ApplyAsync(ICommandTransaction tx, CommandContext ctx, RegisteredSetting s, ConfigValue value, DateOnly effectiveFrom,
        DateOnly bd, string? reason, string source, int changedBy, int? approvedBy, long? requestId, long? revertOf, CancellationToken c)
    {
        var rows = await store.ChangesAsync(tx, ctx.PropertyId, s.Key, c).ConfigureAwait(false);
        var active = rows.Where(r => !r.Withdrawn).ToList();
        var before = SettingRules.ValueOn(active.Select(r => r.AsDated()), effectiveFrom, s.Default);
        if (before == value) throw HccException.Validation(SettingRules.NoChange);
        var now = UtcNow;
        if (effectiveFrom > bd)
            foreach (var same in active.Where(r => r.EffectiveFrom == effectiveFrom))
                await store.WithdrawChangeAsync(tx, same.ChangeId, ctx.PrincipalId, now, c).ConfigureAwait(false);
        var beforeText = SettingRules.Show(s, before);
        var afterText = SettingRules.Show(s, value);
        var version = await store.TouchSettingAsync(tx, ctx.PropertyId, s.Key, now, c).ConfigureAwait(false);
        var id = await store.InsertChangeAsync(tx, new NewSettingChange(ctx.PropertyId, s.Key, effectiveFrom, value.ToJson(), before.ToJson(), beforeText, afterText,
            reason, source, changedBy, approvedBy, requestId, revertOf, now), c).ConfigureAwait(false);
        var applied = effectiveFrom <= bd;
        var when = SettingRules.Wire(effectiveFrom);
        var msg = $"Setting {s.Key} · {beforeText} → {afterText} · {(applied ? "effective" : "scheduled from")} {when}{(revertOf is { } rv ? " · reverts " + rv.ToString(CultureInfo.InvariantCulture) : "")}";
        await tx.AuditAsync("SETTING_SET", Target("cfg-change", id), msg, c).ConfigureAwait(false);
        await tx.PublishAsync(SettingChangedEvent, id, new { key = s.Key, effectiveFrom = when, changeId = id }, c).ConfigureAwait(false);
        return new SetSettingResult(applied ? "applied" : "scheduled", s.Key, id, null, when, beforeText, afterText, VersionToken.ToWire(version), msg);
    }

    /// <summary>Why a history entry cannot be reverted (null = it can). Only the latest change of a key that is in force.</summary>
    public static string? RevertBlock(SettingChangeRow row, IReadOnlyList<SettingChangeRow> allOfKey, RegisteredSetting? s, DateOnly bd)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(allOfKey);
        if (row.Withdrawn) return "This change was withdrawn.";
        if (row.EffectiveFrom > bd) return "Not in effect yet — withdraw it instead.";
        if (allOfKey.Any(x => !x.Withdrawn && x.ChangeId > row.ChangeId)) return "A newer change exists for this setting.";
        if (s is null || !s.Editable) return "This setting is no longer in the registry.";
        var current = SettingRules.ValueOn(allOfKey.Where(x => !x.Withdrawn).Select(x => x.AsDated()), bd, s.Default);
        return current != ConfigValue.Parse(row.ValueJson) ? "The value was changed elsewhere since; change it again instead." : null;
    }

    public sealed record ImportPlan(IReadOnlyList<(RegisteredSetting Setting, ConfigValue Value, DateOnly EffectiveFrom)> Apply, IReadOnlyList<ImportSkip> Skipped,
        IReadOnlyList<string> Errors, int Unchanged);

    /// <summary>Dry run of an import for these rights (prototype <c>cfgImportPlan</c>): what would change, what is skipped and why, blocking errors.</summary>
    public async Task<ImportPlan> PlanAsync(JsonElement data, ConfigAuthority authority, DateOnly bd,
        Func<string, CancellationToken, Task<IReadOnlyList<DatedValue>>> versionsOf, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(versionsOf);
        var apply = new List<(RegisteredSetting, ConfigValue, DateOnly)>();
        var skipped = new List<ImportSkip>();
        var errors = new List<string>();
        var unchanged = 0;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != ExportFormat
            || !data.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
        {
            errors.Add("This is not a Master configuration export file.");
            return new(apply, skipped, errors, 0);
        }

        if (!data.TryGetProperty("version", out var ver) || ver.ValueKind != JsonValueKind.Number || !ver.TryGetInt32(out var vn) || vn != 1)
        {
            errors.Add($"Unsupported export version {(data.TryGetProperty("version", out var vv) ? vv.ToString() : "undefined")}.");
            return new(apply, skipped, errors, 0);
        }

        foreach (var p in values.EnumerateObject())
        {
            var s = registry.Find(p.Name);
            if (s is null) { skipped.Add(new(p.Name, "Not in this registry")); continue; }
            if (s.Secret) { skipped.Add(new(p.Name, "Secrets are never imported")); continue; }
            if (!s.Editable) { skipped.Add(new(p.Name, "Not configurable")); continue; }
            var v = SettingRules.Clean(s, ConfigValue.FromJson(p.Value));
            var versions = await versionsOf(s.Key, ct).ConfigureAwait(false);
            var check = s.Mode == EffectiveMode.NextDay ? bd.AddDays(1) : bd;
            // Unchanged first: a never-set required text (empty default) exported from one property is not an error in another.
            if (SettingRules.ValueOn(versions, check, s.Default) == v && SettingRules.Scheduled(versions, bd).Count == 0) { unchanged++; continue; }
            if (SettingRules.Validate(s, v) is { } err) { errors.Add($"{s.Label}: {err}"); continue; }
            var dec = SettingRules.Decide(s, authority);
            if (dec.Mode == ChangeMode.Deny) { skipped.Add(new(p.Name, dec.Reason)); continue; }
            if (dec.Mode == ChangeMode.Request) { skipped.Add(new(p.Name, "Needs approval — change it on its own")); continue; }
            apply.Add((s, v, SettingRules.EffectiveFrom(s, bd, null).Date));
        }

        return new(apply, skipped, errors, unchanged);
    }

    private async Task<IReadOnlyList<DatedValue>> VersionsAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken c)
        => (await store.ChangesAsync(tx, propertyId, key, c).ConfigureAwait(false)).Where(r => !r.Withdrawn).Select(r => r.AsDated()).ToList();

    private async Task<long> HoursAsync(ICommandTransaction tx, int propertyId, CancellationToken c)
    {
        var s = registry.Get("approvalExpiryHours");
        var bd = await store.BusinessDateAsync(tx, propertyId, c).ConfigureAwait(false);
        var v = SettingRules.ValueOn(await VersionsAsync(tx, propertyId, s.Key, c).ConfigureAwait(false), bd, s.Default);
        return v.Kind == ConfigValueKind.Number && v.Number > 0 ? v.Number : 24;
    }

    private static string? Reason(string? text, int max)
    {
        var t = (text ?? string.Empty).Trim();
        return t.Length == 0 ? null : t.Length > max ? t[..max] : t;
    }

    private static string Target(string kind, long id) => kind + "-" + id.ToString(CultureInfo.InvariantCulture);
}
