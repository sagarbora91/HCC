using Hcc.Application.Commands;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;

namespace Hcc.Application.Billing;

/// <summary>
/// F8-FIX (money review M2, SF-CONTRACT §7.3, SFQ-7): D7 at posting time on a split stay's company folio. The prototype twin is
/// <c>sfAfterCommand</c> (handlers/folio-split.ts): a command other than a night posting that adds a line to the company folio is refused when
/// the folio's exposure rose and the company's exposure without this folio + the folio's exposure after the command exceeds the limit. No
/// override (DEC-076: only the Owner raises the limit); the desk may post the line to the guest folio A instead.
/// Usage, inside the command transaction: <see cref="BeforeAsync"/> before the line is inserted, <see cref="AfterAsync"/> after it; a refusal
/// throws and the runner rolls the whole command back. Night postings (PostRoomNight, the night run, meal-plan night lines) and the late
/// check-out half day inside CheckoutStay (review M6) never call it (SFQ-6: the day close must never stop).
/// A single-folio stay, or a split stay without a live company folio, returns null from <see cref="BeforeAsync"/> and nothing more is read.
/// The Manager's ₹10,000 limit (SFQ-13) is an approval limit of company credit, not of a posting: it is not consulted here.
/// </summary>
public static class CompanyFolioPostingGuard
{
    /// <summary>The stay's company folio and its exposure before the command.</summary>
    public sealed record Watch(long CompanyId, long ReservationId, long FolioId, Paise Before);

    public static async Task<Watch?> BeforeAsync(ISplitFolioStore? split, ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct)
    {
        if (split is null) return null;
        var stay = await split.LockStayAsync(tx, propertyId, reservationId, ct).ConfigureAwait(false);
        if (stay is null || !stay.Snapshot.IsSplit) return null;
        var cf = stay.Snapshot.Live.FirstOrDefault(f => f.PayerKind == "company");
        if (cf?.CompanyId is not { } companyId) return null;
        var before = await split.CompanyFolioCreditAsync(tx, propertyId, companyId, reservationId, cf.FolioId, ct).ConfigureAwait(false);
        return new Watch(companyId, reservationId, cf.FolioId, before.FolioEstimate);
    }

    public static async Task AfterAsync(ISplitFolioStore? split, ICommandTransaction tx, int propertyId, Watch? watch, CancellationToken ct)
    {
        if (split is null || watch is null) return;
        var after = await split.CompanyFolioCreditAsync(tx, propertyId, watch.CompanyId, watch.ReservationId, watch.FolioId, ct).ConfigureAwait(false);
        Check(watch.CompanyId, after, watch.Before);
    }

    /// <summary>The pure rule: refused only when the folio's exposure rose and used + after &gt; limit (same text as <see cref="SplitFolioRules.EnsureCompanyCredit"/>).</summary>
    public static void Check(long companyId, SplitFolioCredit after, Paise before)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (after.FolioEstimate <= before) return;
        SplitFolioRules.EnsureCompanyCredit(companyId, after.Company, after.Limit, after.UsedWithoutFolio, after.FolioEstimate);
    }
}
