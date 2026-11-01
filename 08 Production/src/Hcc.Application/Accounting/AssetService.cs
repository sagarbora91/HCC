using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Expenses;
using Hcc.Contracts;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

// ACC-3 · lane acc3-carry — C1 fixed asset put to use and disposal (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7, §9; rulebook F2, F4).
// Prototype twins: services/mock/handlers/accounting-assets.ts and domain/streams/accounting-assets.ts (accAssetPutToUseRefusal,
// accAssetDisposalFlag, accAssetDepreciationFactor) — same rules, same texts.
//   SetAssetPutToUse  {assetId, date?}                            the date the asset was first used (depreciation counts from it, F2).
//   DisposeFixedAsset {assetId, proceedsPaise, method?, date?, reason?}   rulebook F4: a sale is a manual Receipt Dr tender · Cr block (the sale
//                     value) through the manual-voucher API (Owner approved at once, Accountant pending); nothing received = no voucher; the
//                     flag "short-term capital gain / loss — CA" when the block goes negative or holds no other asset (no automatic P&L entry).
// Permission: PERM-ACC-010 (Owner + Accountant). The contract names PERM-ACC-004, which is the Owner-only ledger mapping (DEC-063 G11) — see
// ACC3-CARRY-DONE.md.

/// <summary>Rulebook F2 / F4 rules shared by the service and its tests.</summary>
public static class AssetRules
{
    public const string Permission = "PERM-ACC-010";

    /// <summary>31 March of the financial year that contains <paramref name="date"/>.</summary>
    public static DateOnly FyEnd(DateOnly date) => new(date.Month >= 4 ? date.Year + 1 : date.Year, 3, 31);

    /// <summary>F2: 1 (≥ 180 days in the year, or put to use in an earlier year), 0.5 (fewer than 180 days), 0 (not put to use by then / disposed before the year).</summary>
    public static decimal DepreciationFactor(DateOnly? putToUse, DateOnly? disposed, DateOnly fyEnd)
    {
        if (putToUse is not { } p || p > fyEnd) return 0m;
        var fyStart = new DateOnly(fyEnd.Year - 1, 4, 1);
        if (disposed is { } d && d < fyStart) return 0m;
        if (p < fyStart) return 1m;
        return fyEnd.DayNumber - p.DayNumber + 1 >= 180 ? 1m : 0.5m;
    }

    public static string? PutToUseRefusal(FixedAssetView a, DateOnly date, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.DisposedDate is not null) return $"{a.AssetNo} is disposed.";
        if (string.CompareOrdinal(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), a.PurchaseDate) < 0) return $"The put-to-use date cannot be before the purchase ({a.PurchaseDate}).";
        if (date > businessDate) return "The put-to-use date cannot be after the business date.";
        return null;
    }

    /// <summary>F4 flag (null = none): the block balance after the sale value is negative, or no other live asset is left in the block.</summary>
    public static string? DisposalFlag(string ledger, long blockBalancePaise, long proceedsPaise, int othersInBlock)
    {
        var after = blockBalancePaise - proceedsPaise;
        if (after < 0) return $"Block {ledger} goes negative ({SourceRules.Rupees(after)}) — short-term capital gain — CA (rulebook F4).";
        if (othersInBlock == 0) return $"Block {ledger} has no asset left — short-term capital loss / gain — CA (rulebook F4).";
        return null;
    }
}

/// <summary>The C1 commands (prototype ACC_ASSET_PUT_TO_USE / ACC_ASSET_DISPOSE).</summary>
public sealed class AssetService(ICommandRunner runner, IAccessPolicy policy, IFixedAssetStore assets, IAccountingStore accounting, TimeProvider clock,
    IGstManualVouchers? manual = null)
{
    public const string OpPutToUse = "SetAssetPutToUse";
    public const string OpDispose = "DisposeFixedAsset";

    private static readonly DateOnly Beginning = new(2000, 1, 1);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string op, string key) => new(c.PropertyId, c.PrincipalId, op, key);

    private static DateOnly? Date(string? d, string field) => string.IsNullOrWhiteSpace(d) ? null : Hcc.Domain.Dates.StayDates.Parse(d, field);

    private async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
        => await accounting.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");

    /// <summary>Book balance of a block ledger on <paramref name="asOf"/> (every voucher up to it; lock snapshots for locked dates).</summary>
    private async Task<long> BlockBalanceAsync(ICommandTransaction tx, int propertyId, string ledger, DateOnly asOf, CancellationToken ct)
    {
        var chart = await accounting.ChartAsync(tx, propertyId, ct).ConfigureAwait(false);
        var mapping = await accounting.EffectiveMappingAsync(tx, propertyId, chart, ct).ConfigureAwait(false);
        var sources = await accounting.SourcesAsync(tx, propertyId, Beginning, asOf, ct).ConfigureAwait(false);
        var locks = (await accounting.LocksAsync(tx, propertyId, Beginning, asOf, ct).ConfigureAwait(false)).ToDictionary(l => l.BusinessDate);
        var dates = sources.Select(s => s.BusinessDate).Concat(locks.Keys).Distinct().Order();
        return dates.SelectMany(d => locks.TryGetValue(d, out var l) ? l.Vouchers : VoucherBuilder.Build(d, sources, mapping, chart))
            .SelectMany(v => v.Entries).Where(e => e.Account == ledger).Sum(e => e.DebitPaise - e.CreditPaise);
    }

    public async Task<CommandResult<FixedAssetResult>> PutToUseAsync(Caller caller, string idempotencyKey, SetAssetPutToUseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AssetRules.Permission, ct: ct).ConfigureAwait(false);
        var chosen = Date(req.Date, "date");
        return await runner.RunAsync(Ctx(caller, OpPutToUse, idempotencyKey), req, async (tx, c) =>
        {
            var a = await assets.LockAsync(tx, caller.PropertyId, req.AssetId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Asset not found.");
            var bd = await BusinessDateAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var date = chosen ?? bd;
            if (AssetRules.PutToUseRefusal(a, date, bd) is { } why) throw HccException.Validation(why);
            await assets.SetPutToUseAsync(tx, a.AssetId, date, caller.PrincipalId, Now, c).ConfigureAwait(false);
            var w = Hcc.Domain.Dates.StayDates.ToWire(date);
            await tx.AuditAsync("ACC_ASSET_PUT_TO_USE", "fixed-asset:" + a.AssetId.ToString(CultureInfo.InvariantCulture),
                $"{a.AssetNo} · {a.Name} put to use {w}{(a.PutToUse is null ? "" : " (was " + a.PutToUse + ")")} · depreciation counts from this date (rulebook F2)", c).ConfigureAwait(false);
            var u = (await assets.LockAsync(tx, caller.PropertyId, a.AssetId, c).ConfigureAwait(false))!;
            return new FixedAssetResult(u, null, null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FixedAssetResult>> DisposeAsync(Caller caller, string idempotencyKey, DisposeFixedAssetRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AssetRules.Permission, ct: ct).ConfigureAwait(false);
        if (req.ProceedsPaise is < 0 or > 10_000_000_000) throw HccException.Validation("Enter the sale value in paise (0 for a scrap with nothing received).");
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        var method = (req.Method ?? "").Trim();
        if (req.ProceedsPaise > 0 && method.Length == 0) throw HccException.Validation("Choose how the sale value was received.");
        var chosen = Date(req.Date, "date");
        return await runner.RunAsync(Ctx(caller, OpDispose, idempotencyKey), req, async (tx, c) =>
        {
            var a = await assets.LockAsync(tx, caller.PropertyId, req.AssetId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Asset not found.");
            if (a.DisposedDate is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{a.AssetNo} is already disposed.");
            var bd = await BusinessDateAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var date = chosen ?? bd;
            var w = Hcc.Domain.Dates.StayDates.ToWire(date);
            if (string.CompareOrdinal(w, a.PurchaseDate) < 0) throw HccException.Validation($"The disposal cannot be before the purchase ({a.PurchaseDate}).");
            if (date > bd) throw HccException.Validation("The disposal date cannot be after the business date.");
            var others = (await assets.ListAsync(tx, caller.PropertyId, c).ConfigureAwait(false)).Count(x => x.Ledger == a.Ledger && x.AssetId != a.AssetId && x.DisposedDate is null);
            var flag = AssetRules.DisposalFlag(a.Ledger, await BlockBalanceAsync(tx, caller.PropertyId, a.Ledger, date, c).ConfigureAwait(false), req.ProceedsPaise, others);
            string? voucher = null;
            if (req.ProceedsPaise > 0)
            {
                var chart = await accounting.ChartAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
                var mapping = await accounting.EffectiveMappingAsync(tx, caller.PropertyId, chart, c).ConfigureAwait(false);
                var tender = ChartOfAccounts.Resolve(mapping, "method:" + method, chart);
                if (tender is null || !ManualVoucherRules.IsCashBank(chart, tender)) throw HccException.Validation($"{method} does not settle to a cash or bank ledger.");
                var v = await (manual ?? new UnwiredGstManualVouchers()).SaveAsync(tx, caller, new GstManualVoucherDraft(date, "Receipt",
                    $"Fixed asset sold · {a.AssetNo} {a.Name} · block {a.Ledger} credited with the sale value (rulebook F4)" + (reason.Length > 0 ? " · " + reason : ""), a.AssetNo, "journal",
                    [(tender, req.ProceedsPaise, 0L), (a.Ledger, 0L, req.ProceedsPaise)], method), c).ConfigureAwait(false);
                voucher = v.Id;
            }

            await assets.DisposeAsync(tx, a.AssetId, date, req.ProceedsPaise, voucher, method.Length > 0 ? method : null, flag, reason.Length > 0 ? reason : null, caller.PrincipalId, Now, c)
                .ConfigureAwait(false);
            await tx.AuditAsync("ACC_ASSET_DISPOSE", "fixed-asset:" + a.AssetId.ToString(CultureInfo.InvariantCulture),
                $"{a.AssetNo} · {a.Name} disposed {w} · " + (req.ProceedsPaise > 0 ? $"sold for ₹{SourceRules.Rupees(req.ProceedsPaise)} by {method} · Dr tender · Cr {a.Ledger} · voucher {voucher}" : "scrapped, nothing received · no voucher")
                + (flag is null ? "" : " · " + flag), c).ConfigureAwait(false);
            var u = (await assets.LockAsync(tx, caller.PropertyId, a.AssetId, c).ConfigureAwait(false))!;
            return new FixedAssetResult(u, voucher, flag);
        }, ct).ConfigureAwait(false);
    }
}
