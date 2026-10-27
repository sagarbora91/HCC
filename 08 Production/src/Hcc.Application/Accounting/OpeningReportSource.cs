namespace Hcc.Application.Accounting;

/// <summary>
/// ACC-1 (integration): the opening entry for the report queries (<see cref="IReportOpeningSource"/>) over the opening lane's store.
/// Only an APPROVED entry posts (contract §5; prototype <c>accOpeningPosted</c>): a draft returns no voucher, so the imported opening is
/// used and the six-column trial balance says "opening balances pending"; an approved entry carries its journal <c>JRN/&lt;asOf&gt;/000</c>
/// with the 1400 balancing line (pending while that line is not zero).
/// </summary>
public sealed class OpeningReportSource(IOpeningEntryStore store) : IReportOpeningSource
{
    public async Task<ReportOpening?> GetAsync(int propertyId, CancellationToken ct = default)
    {
        var entry = await store.GetAsync(null, propertyId, ct).ConfigureAwait(false);
        return entry is null ? null : new ReportOpening(entry.Approved ? OpeningBalanceRules.Voucher(entry) : null, entry.Approved);
    }
}
