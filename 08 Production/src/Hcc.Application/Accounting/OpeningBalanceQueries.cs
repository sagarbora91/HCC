using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Import;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Import;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

/// <summary>
/// ACC-1 opening balances reads (contract §5): <c>GET /accounting/opening</c> (PERM-ACC-001) and the opening the cumulative trial balance
/// adds (<see cref="EffectiveOpeningAsync"/>): the APPROVED hand-entered entry supersedes the imported opening trial balance (W4-13,
/// <see cref="IOpeningBalanceSource"/>), which stays for the CSV path. A draft never counts.
/// </summary>
public sealed class OpeningBalanceQueries(IOpeningEntryStore store, IAccountingStore accounting, IAccessPolicy policy)
{
    public async Task<OpeningBalancesStateView> GetAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        var businessDate = await accounting.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var entry = await store.GetAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var locked = entry is null ? null : await store.FirstLockOnOrAfterAsync(null, caller.PropertyId, entry.AsOf, ct).ConfigureAwait(false);
        return new OpeningBalancesStateView(OpeningBalanceRules.FyStart(businessDate), OpeningBalanceRules.Pending(entry), entry is null ? null : View(entry), locked);
    }

    public static OpeningEntryView View(OpeningEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new OpeningEntryView(e.AsOf, e.Status, e.Lines.Select(l => new OpeningLineView(l.Account, OpeningBalanceRules.NameOf(l.Account), l.Debit, l.Credit)).ToList(),
            e.Lines.Sum(l => l.Debit), e.Lines.Sum(l => l.Credit), OpeningBalanceRules.Difference(e), e.EnteredBy, e.EnteredUtc, e.ApprovedBy, e.ApprovedUtc, e.Note, e.Version);
    }

    /// <summary>
    /// The opening the cumulative trial balance adds (one call in <see cref="AccountingQueries.TrialBalanceAsync"/>): the approved entry as
    /// an <see cref="OpeningBalancesView"/> dated its <c>asOf</c> (a chart code maps to its HCC account; a party ledger id and 1400 keep
    /// their own code, name and group), else the imported opening, else an empty view.
    /// </summary>
    public static async Task<OpeningBalancesView> EffectiveOpeningAsync(IOpeningEntryStore? entries, IOpeningBalanceSource? imported, int propertyId, CancellationToken ct)
    {
        var entry = entries is null ? null : await entries.GetAsync(null, propertyId, ct).ConfigureAwait(false);
        if (entry is { Approved: true }) return AsOpeningView(entry);
        return imported is null ? new OpeningBalancesView(null, null, 0, 0, true, []) : await imported.GetAsync(propertyId, ct).ConfigureAwait(false);
    }

    public static OpeningBalancesView AsOpeningView(OpeningEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var rows = e.Lines.Where(l => l.Debit != 0 || l.Credit != 0).Select(l =>
        {
            var code = OpeningBalanceRules.CodeOf(l.Account);
            var group = ChartOfAccounts.Find(code)?.Group ?? AccountGroup.Suspense;
            return new OpeningBalanceRowView(l.Account, OpeningBalanceRules.NameOf(l.Account), group.ToString(), l.Account, l.Debit, l.Credit);
        }).ToList();
        var d = rows.Sum(r => r.DebitPaise);
        var c = rows.Sum(r => r.CreditPaise);
        return new OpeningBalancesView(e.AsOf, null, d, c, d == c, rows);
    }
}
