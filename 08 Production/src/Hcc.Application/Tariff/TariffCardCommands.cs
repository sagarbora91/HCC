using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts.Tariff;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tariff;
using Hcc.Domain.Tax;

namespace Hcc.Application.Tariff;

/// <summary>
/// F4-T1 tariff card commands (F4-CONTRACT §1.5): SaveTariffCardDraft, ProposeTariffCard (PERM-TRF-002), PublishTariffCard,
/// RejectTariffCard, SetTariffCardWeekend (PERM-TRF-003, Owner), DiscardTariffCard (002). Publish writes the existing masters in the one
/// command transaction, one audit row per master row + one TRC_PUBLISH row; nothing reads the card for a price (DEC-MA-102).
/// </summary>
public sealed class TariffCardCommands(ICommandRunner runner, IAccessPolicy policy, ITariffCardStore store, IGstMasterStore gst, IGstRateResolver rates, TimeProvider clock)
{
    public const string OpSave = "SaveTariffCardDraft";
    public const string OpPropose = "ProposeTariffCard";
    public const string OpPublish = "PublishTariffCard";
    public const string OpReject = "RejectTariffCard";
    public const string OpDiscard = "DiscardTariffCard";
    public const string OpWeekend = "SetTariffCardWeekend";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<TariffCardVersionView>> SaveDraftAsync(CommandContext ctx, SaveTariffCardDraftRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, TariffCardPermissions.Propose, null, null, ct).ConfigureAwait(false);
        if (!DateOnly.TryParseExact(req.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
            throw TariffCardRules.Fail(TariffCardReasons.PastEffective, "Choose the date the card takes effect.");
        return await runner.RunAsync(ctx with { Operation = OpSave }, req, async (tx, c) =>
        {
            var today = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var lines = await PriceAsync(tx, ctx.PropertyId, from, req.Lines, c).ConfigureAwait(false);
            var label = string.IsNullOrWhiteSpace(req.Label) ? $"Tariff card from {req.EffectiveFrom}" : req.Label.Trim()[..Math.Min(60, req.Label.Trim().Length)];
            if (req.VersionId is { } id)
            {
                var old = await Open(tx, ctx, id, c).ConfigureAwait(false);
                var weekend = req.WeekendDays ?? old.WeekendDays;
                TariffCardRules.CheckDraft(from, today, weekend, lines);
                var upd = old with { Label = label, EffectiveFrom = from, WeekendDays = weekend, Lines = lines, Notes = req.Notes ?? old.Notes, Status = "draft", ProposedBy = null };
                await store.UpdateDraftAsync(tx, ctx.PropertyId, upd, c).ConfigureAwait(false);
                await tx.AuditAsync("TRC_DRAFT_SAVE", Code(id), $"draft saved · from {req.EffectiveFrom} · {lines.Count} lines{(old.Status == "proposed" ? " · proposal withdrawn" : "")}", c).ConfigureAwait(false);
                return View(upd);
            }

            var days = req.WeekendDays ?? await store.WeekendDefaultAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            TariffCardRules.CheckDraft(from, today, days, lines);
            var rec = new TariffCardRec(0, label, from, "draft", days, lines, ctx.PrincipalId, UtcNow, null, null, null, null, req.Notes, []);
            var nid = await store.InsertAsync(tx, ctx.PropertyId, rec, c).ConfigureAwait(false);
            await tx.AuditAsync("TRC_DRAFT_SAVE", Code(nid), $"draft created · from {req.EffectiveFrom} · {lines.Count} lines", c).ConfigureAwait(false);
            return View(rec with { VersionId = nid });
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<TariffCardVersionView>> ProposeAsync(CommandContext ctx, TariffCardIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, TariffCardPermissions.Propose, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpPropose }, req, async (tx, c) =>
        {
            var v = await Open(tx, ctx, req.VersionId, c).ConfigureAwait(false);
            if (v.Status != "draft") throw TariffCardRules.Fail(TariffCardReasons.PublishedReadOnly, $"{Code(v.VersionId)} is {v.Status}; only a draft can be proposed.");
            var plan = await PlanAsync(tx, ctx.PropertyId, v, c).ConfigureAwait(false);
            await store.SetStatusAsync(tx, v.VersionId, "proposed", ctx.PrincipalId, null, c).ConfigureAwait(false);
            await tx.AuditAsync("TRC_PROPOSE", Code(v.VersionId), $"proposed for Owner approval · {plan.Writes.Count} master rows from {v.EffectiveFrom:yyyy-MM-dd}", c).ConfigureAwait(false);
            return View(v with { Status = "proposed", ProposedBy = ctx.PrincipalId });
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<TariffCardVersionView>> PublishAsync(CommandContext ctx, TariffCardIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, TariffCardPermissions.Publish, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpPublish }, req, async (tx, c) =>
        {
            var v = await Open(tx, ctx, req.VersionId, c).ConfigureAwait(false);
            var plan = await PlanAsync(tx, ctx.PropertyId, v, c).ConfigureAwait(false);
            foreach (var w in plan.Writes)
            {
                await store.ApplyAsync(tx, ctx.PropertyId, v.VersionId, v.EffectiveFrom, w, ctx.PrincipalId, c).ConfigureAwait(false);
                await tx.AuditAsync("TRC_PUBLISH_ROW", Code(v.VersionId), $"{w.Master} · {w.Label} · {(w.Before is { } b ? Rs(b) : "new")} → {Rs(w.After)}", c).ConfigureAwait(false);
            }

            var sup = await store.SupersedeAsync(tx, ctx.PropertyId, v.VersionId, v.EffectiveFrom, c).ConfigureAwait(false);
            var now = UtcNow;
            await store.MarkPublishedAsync(tx, v.VersionId, ctx.PrincipalId, now, c).ConfigureAwait(false);
            await tx.AuditAsync("TRC_PUBLISH", Code(v.VersionId), $"published · from {v.EffectiveFrom:yyyy-MM-dd} · {plan.Writes.Count} master rows · {sup} superseded", c).ConfigureAwait(false);
            return View(v with { Status = "published", PublishedBy = ctx.PrincipalId, PublishedUtc = now, PublishLog = plan.Writes });
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<TariffCardVersionView>> RejectAsync(CommandContext ctx, RejectTariffCardRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, TariffCardPermissions.Publish, null, null, ct).ConfigureAwait(false);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length is < 3 or > 200) throw HccException.Validation("Give the reason for rejecting (3–200 characters).");
        return await runner.RunAsync(ctx with { Operation = OpReject }, req, async (tx, c) =>
        {
            var v = await Open(tx, ctx, req.VersionId, c).ConfigureAwait(false);
            if (v.Status != "proposed") throw TariffCardRules.Fail(TariffCardReasons.PublishedReadOnly, $"{Code(v.VersionId)} is {v.Status}; only a proposed card can be rejected.");
            var notes = ((v.Notes is null ? "" : v.Notes + " · ") + "Rejected: " + reason);
            notes = notes.Length > 500 ? notes[..500] : notes;
            await store.SetStatusAsync(tx, v.VersionId, "rejected", null, notes, c).ConfigureAwait(false);
            await tx.AuditAsync("TRC_REJECT", Code(v.VersionId), "rejected · " + reason, c).ConfigureAwait(false);
            return View(v with { Status = "rejected", Notes = notes });
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<TariffCardIdRequest>> DiscardAsync(CommandContext ctx, TariffCardIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, TariffCardPermissions.Propose, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpDiscard }, req, async (tx, c) =>
        {
            var v = await Open(tx, ctx, req.VersionId, c).ConfigureAwait(false);
            if (v.Status != "draft") throw TariffCardRules.Fail(TariffCardReasons.PublishedReadOnly, $"{Code(v.VersionId)} is {v.Status}; only a draft can be discarded.");
            await store.DeleteDraftAsync(tx, v.VersionId, c).ConfigureAwait(false);
            await tx.AuditAsync("TRC_DISCARD", Code(v.VersionId), "draft discarded", c).ConfigureAwait(false);
            return req;
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<SetTariffCardWeekendRequest>> SetWeekendAsync(CommandContext ctx, SetTariffCardWeekendRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, TariffCardPermissions.Publish, null, null, ct).ConfigureAwait(false);
        var days = req.Days ?? [];
        if (days.Any(d => d is < 0 or > 6) || days.Distinct().Count() != days.Count || days.Count >= 7) throw HccException.Validation("Weekend nights: pick up to six days of the week.");
        return await runner.RunAsync(ctx with { Operation = OpWeekend }, req, async (tx, c) =>
        {
            if (req.VersionId is { } id)
            {
                var v = await Open(tx, ctx, id, c).ConfigureAwait(false);
                await store.UpdateDraftAsync(tx, ctx.PropertyId, v with { WeekendDays = days }, c).ConfigureAwait(false);
            }
            else await store.SetWeekendDefaultAsync(tx, ctx.PropertyId, days, c).ConfigureAwait(false);

            await tx.AuditAsync("TRC_WEEKEND_SET", req.VersionId is { } x ? Code(x) : "tariff-card", "weekend nights " + TariffCardRules.WeekendText(days), c).ConfigureAwait(false);
            return req;
        }, ct).ConfigureAwait(false);
    }

    private async Task<TariffCardRec> Open(ICommandTransaction tx, CommandContext ctx, long id, CancellationToken ct)
    {
        var v = await store.LockAsync(tx, ctx.PropertyId, id, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Tariff card version not found.");
        if (v.Status is not ("draft" or "proposed"))
            throw TariffCardRules.Fail(TariffCardReasons.PublishedReadOnly, $"{Code(id)} is {v.Status}; published cards are never edited — make a new version with a later date.");
        return v;
    }

    private async Task<TrcPublishPlan> PlanAsync(ICommandTransaction tx, int propertyId, TariffCardRec v, CancellationToken ct)
    {
        var today = await store.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false);
        TariffCardRules.CheckDraft(v.EffectiveFrom, today, v.WeekendDays, v.Lines);
        var unknown = await store.UnknownCodesAsync(tx, propertyId, v.Lines.Where(l => l.Kind != "hall").ToList(), ct).ConfigureAwait(false);
        if (unknown.Count > 0) throw TariffCardRules.Fail(TariffCardReasons.Conflict, "Not in the masters: " + string.Join(", ", unknown) + ".");
        var tariffs = await store.TariffsOnAsync(tx, propertyId, v.EffectiveFrom, ct).ConfigureAwait(false);
        var refs = await store.CardRefsAsync(tx, propertyId, v.VersionId, ct).ConfigureAwait(false);
        var money = await store.MoneyOnAsync(tx, propertyId, v.EffectiveFrom, ct).ConfigureAwait(false);
        return TariffCardRules.Plan(v.Lines, v.EffectiveFrom, tariffs, refs, k => money.TryGetValue(k, out var m) ? m : null);
    }

    /// <summary>Prices the lines with the dated GST master at <paramref name="date"/> (§1.4): rooms / day use by the slab, extra persons and
    /// extras "other services", meal add-ons at the room slab (bundled), halls banquet.</summary>
    private async Task<IReadOnlyList<TrcLine>> PriceAsync(ICommandTransaction tx, int propertyId, DateOnly date, IReadOnlyList<TariffCardLineDto>? lines, CancellationToken ct)
    {
        var master = new GstMaster(await gst.ActiveVersionsAsync(tx, propertyId, ct).ConfigureAwait(false));
        var other = (await rates.ChargeAsync(tx, propertyId, date, GstCategory.Other, ct).ConfigureAwait(false)).Bps;
        var banquet = (await rates.ChargeAsync(tx, propertyId, date, GstCategory.Banquet, ct).ConfigureAwait(false)).Bps;
        var list = new List<TrcLine>();
        var n = 0;
        foreach (var d in lines ?? [])
        {
            n++;
            var line = new TrcLine(d.LineNo > 0 ? d.LineNo : n, d.Kind, d.RoomType, d.Plan, d.OccKind, d.MealPlan, d.Extra, d.Hall, d.DaySet, d.RateExcl, 0, null, d.EnteredIncl,
                d.MinGuests, d.MaxGuests, d.AgeFrom, d.AgeTo, d.Note);
            Func<long, int> bps = d.Kind switch
            {
                "room" or "day-use" or "meal" => e => master.RoomNight(date, new Paise(e)).Bps,
                "hall" => _ => banquet,
                _ => _ => other,
            };
            list.Add(TariffCardRules.Price(line, bps));
        }

        return list;
    }

    public static string Code(long id) => "TRC-" + id.ToString("0000", CultureInfo.InvariantCulture);

    private static string Rs(long paise) => (paise / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static TariffCardVersionView View(TariffCardRec r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new TariffCardVersionView(r.VersionId, Code(r.VersionId), r.Label, r.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.Status, r.WeekendDays,
            r.Lines.Select(l => new TariffCardLineDto(l.LineNo, l.Kind, l.RoomType, l.Plan, l.OccKind, l.MealPlan, l.Extra, l.Hall, l.DaySet, l.RateExcl, l.EnteredIncl, l.MinGuests,
                l.MaxGuests, l.AgeFrom, l.AgeTo, l.Note, l.TaxBps, l.DisplayIncl)).ToList(),
            r.CreatedBy, Wire(r.CreatedUtc), r.ProposedBy, r.PublishedBy, r.PublishedUtc is { } p ? Wire(p) : null, r.SupersededBy, r.Notes,
            r.PublishLog.Select(w => new TariffCardWriteDto(w.Master, w.Ref, w.Before, w.After, w.Label)).ToList());
    }

    private static string Wire(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Reads: GET /tariff-card (PERM-TRF-001).</summary>
public sealed class TariffCardQueries(IAccessPolicy policy, ITariffCardStore store)
{
    public async Task<TariffCardList> ListAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, TariffCardPermissions.View, null, null, ct).ConfigureAwait(false);
        var list = await store.ListAsync(propertyId, ct).ConfigureAwait(false);
        var weekend = await store.WeekendDefaultAsync(null, propertyId, ct).ConfigureAwait(false);
        return new TariffCardList(list.OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.VersionId).Select(TariffCardCommands.View).ToList(), weekend);
    }
}
