using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Tax;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Tax;

/// <summary>
/// GST master commands and read (DEC-069 in Master configuration, Tax group). Changes are Owner-only (DEC-058; PERM-ADM-003 'A'),
/// dated (effective from the business date or later, never backdated), kept as history (each change is a new version row) and
/// can be withdrawn until they take effect. Tech admin reads (PERM-ADM-003 'V'). Posted charges keep their own snapshot.
/// </summary>
public sealed class GstMasterService(ICommandRunner runner, IAccessPolicy policy, IGstMasterStore store, GstRateResolver resolver, TimeProvider clock)
{
    public const string OpSet = "SetGstSetting";
    public const string OpWithdraw = "WithdrawGstSetting";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<GstSettingChangeResult>> SetAsync(CommandContext ctx, SetGstSettingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, TaxPermissions.Configuration, ct: ct).ConfigureAwait(false);
        if (!GstSeed.IsKey(req.Key)) throw HccException.Validation("Unknown GST setting. Choose one of the GST master settings.");
        GstSeed.ValidateValue(req.Key, req.Value);
        var reason = Reason(req.Reason);
        DateOnly? from = string.IsNullOrWhiteSpace(req.EffectiveFrom) ? null : StayDates.Parse(req.EffectiveFrom, "effectiveFrom");

        return await runner.RunAsync(ctx with { Operation = OpSet }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var effective = from ?? bd;
            if (effective < bd) throw HccException.Validation($"A GST change cannot be backdated: choose {StayDates.ToWire(bd)} (the business date) or later.");
            if (effective > bd.AddDays(731)) throw HccException.Validation("Schedule a GST change at most two years ahead.");
            var master = new GstMaster(await store.LockActiveVersionsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false));
            var before = master.ValueOn(req.Key, effective);
            if (before == req.Value) throw HccException.Validation("That is already the value in force on that date — nothing to change.");
            var id = await store.InsertVersionAsync(tx, ctx.PropertyId, req.Key, effective, req.Value, reason, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            var when = StayDates.ToWire(effective);
            await tx.AuditAsync("GST_SETTING_SET", "gst-setting:" + req.Key,
                Clip($"{req.Key} {Show(req.Key, before)} → {Show(req.Key, req.Value)} from {when} · {reason} · {GstSeed.Illustrative}"), c).ConfigureAwait(false);
            await tx.PublishAsync("GstSettingChanged", id, new { versionId = id, key = req.Key, effectiveFrom = when, value = req.Value }, c).ConfigureAwait(false);
            return new GstSettingChangeResult(id, req.Key, when, before, req.Value, effective > bd ? "Scheduled" : "InForce");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<GstSettingChangeResult>> WithdrawAsync(CommandContext ctx, WithdrawGstSettingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, TaxPermissions.Configuration, ct: ct).ConfigureAwait(false);
        var reason = Reason(req.Reason);
        return await runner.RunAsync(ctx with { Operation = OpWithdraw }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            _ = await store.LockActiveVersionsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var v = await store.LockVersionAsync(tx, req.VersionId, c).ConfigureAwait(false);
            if (v is null || v.PropertyId != ctx.PropertyId) throw new HccException(HccErrorCode.NOT_FOUND, "GST change not found.");
            if (v.Status != "active") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This GST change was already withdrawn.");
            if (v.EffectiveFrom <= bd)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "This GST change is already in force. Set the previous value again from today instead.");
            await store.WithdrawVersionAsync(tx, v.VersionId, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            var after = new GstMaster(await store.LockActiveVersionsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)).ValueOn(v.Key, v.EffectiveFrom);
            var when = StayDates.ToWire(v.EffectiveFrom);
            await tx.AuditAsync("GST_SETTING_WITHDRAWN", "gst-setting:" + v.Key,
                Clip($"{v.Key} change to {Show(v.Key, v.Value)} from {when} withdrawn (back to {Show(v.Key, after)}) · {reason}"), c).ConfigureAwait(false);
            await tx.PublishAsync("GstSettingChanged", v.VersionId, new { versionId = v.VersionId, key = v.Key, effectiveFrom = when, value = after, withdrawn = true }, c).ConfigureAwait(false);
            return new GstSettingChangeResult(v.VersionId, v.Key, when, v.Value, after, "Withdrawn");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The master in force on <paramref name="date"/> (default: today's business date is the caller's job; null = latest seed date view).</summary>
    public async Task<GstMasterView> GetAsync(int propertyId, int principalId, DateOnly date, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, TaxPermissions.ConfigurationView, ct: ct).ConfigureAwait(false);
        var canChange = await policy.HasAsync(propertyId, principalId, TaxPermissions.Configuration, ct: ct).ConfigureAwait(false);
        var master = await resolver.MasterAsync(null, propertyId, ct).ConfigureAwait(false);
        var sp = await resolver.SpecifiedAsync(null, master, propertyId, date, ct).ConfigureAwait(false);
        var versions = await store.ActiveVersionsAsync(null, propertyId, ct).ConfigureAwait(false);
        var settings = GstSeed.Settings.Select(d =>
        {
            var from = versions.Where(v => v.Key == d.Key && v.EffectiveFrom <= date).Select(v => v.EffectiveFrom).DefaultIfEmpty(d.Seed[0].EffectiveFrom).Max();
            return new GstSettingView(d.Key, d.Label, d.Kind.ToString().ToLowerInvariant(), d.Unit, d.Help, master.ValueOn(d.Key, date), StayDates.ToWire(from));
        }).ToList();
        var th = new Paise(master.ValueOn(GstKeys.RoomThreshold, date));
        var lo = master.RoomNight(date, th);
        var hi = master.RoomNight(date, th + new Paise(1));
        var food = master.Food(date, sp);
        static string Itc(GstRate r) => r.Itc ? "with ITC" : "without ITC";
        var summary = new List<GstSummaryLine>
        {
            new($"Room night ≤ {th}", lo.Bps, lo.Itc, $"{Itc(lo)} · per room per night on the actual price"),
            new($"Room night > {th}", hi.Bps, hi.Itc, Itc(hi)),
            new("Restaurant / room service", food.Bps, food.Itc, $"{Itc(food)} · FY {sp.Fy} {(sp.Specified ? "specified premises" : "not specified premises")} ({sp.Reason})"),
            new("Laundry", master.Service(date, GstCategory.Laundry).Bps, true, "with ITC"),
            new("Banquet / hall", master.Service(date, GstCategory.Banquet).Bps, true, "with ITC"),
            new("Other services", master.Service(date, GstCategory.Other).Bps, true, "with ITC"),
        };
        var history = (await store.HistoryAsync(propertyId, ct).ConfigureAwait(false))
            .Select(h => new GstHistoryRow(h.VersionId, h.Key, StayDates.ToWire(h.EffectiveFrom), h.Value, h.Source, h.Status, h.Reason, h.ChangedBy,
                DateTime.SpecifyKind(h.ChangedUtc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)))
            .ToList();
        return new GstMasterView(StayDates.ToWire(date), GstSeed.Illustrative, canChange, sp.Fy, sp.Specified, sp.Reason, settings, summary, history);
    }

    private static string Show(string key, long value) => GstSeed.Def(key).Kind switch
    {
        GstValueKind.Percent => GstMath.Percent(checked((int)value)),
        GstValueKind.Money => new Paise(value).ToString(),
        _ => value == 1 ? "yes" : "no",
    };

    private static string Reason(string? reason)
    {
        var r = reason?.Trim() ?? string.Empty;
        if (r.Length < 3) throw HccException.Validation("Give a reason for the GST change (at least 3 characters).");
        if (r.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        return r;
    }

    private static string Clip(string s) => s.Length <= 1000 ? s : s[..1000];
}
