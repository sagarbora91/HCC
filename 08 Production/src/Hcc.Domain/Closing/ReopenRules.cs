using System.Globalization;
using Hcc.Contracts;
using Hcc.Contracts.Closing;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Closing;

/// <summary>
/// DEC-077 (audit D8; resolves DEC-028, which was adjustment-only): the Owner reopens a closed business day with a reason (audited),
/// corrections post on it, and it is closed again. Design (also in <c>08 Production/README.md</c> "Reopening a closed day"):
/// <list type="bullet">
/// <item>Who: Owner only — PERM-CLS-004 (close.correction.approve, the matrix's Owner-only closed-date correction approval). A reason is required.</item>
/// <item>Which day: a closed day before the business date (its close completed), never a day locked in Accounting (unlock that period
/// first), and only ONE reopened day per property at a time. Reopening day D never reopens later days and leaves their closing records
/// alone (nothing in the reopen window posts on them); the current day cannot be closed until D is closed again (preflight check
/// <see cref="CloseRules.Reopened"/>), so a reopen never outlives the operating day.</item>
/// <item>While reopened: a correction (reversal) of a line dated D posts on D, and a missed service charge may be posted on D
/// (PostCharge with serviceDate D); both need an open folio, so an issued invoice or credit note — number and GST — never changes
/// (a checked-out stay is corrected with a credit note). Receipts, refunds, deposits and cash entries stay dated the day the money moves
/// (the open business day): a reopened day does not reopen the drawer. The DEC-065 cash-book lock of D is NOT changed: while it is
/// locked, a correction or charge touching D is refused until the Owner reopens the cash book itself.</item>
/// <item>Tally: when D had been exported, its vouchers are marked changed; the next Tally export carries D again (with a RE-EXPORT note).</item>
/// <item>Close again: RunBusinessDayClose for D re-runs the checks that concern D (pending corrections of D's lines) and writes a new
/// closing record from the same source tables; the record it replaces is kept, stamped superseded (history).</item>
/// </list>
/// </summary>
public static class ReopenRules
{
    public const string StatusReopened = "Reopened";
    public const string StatusClosed = "Closed";

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);

    public static string EnsureReason(string? reason)
    {
        var r = reason?.Trim() ?? string.Empty;
        if (r.Length == 0) throw HccException.Validation("A reason is required to reopen a closed business day.");
        if (r.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        return r;
    }

    /// <summary>INVALID_TRANSITION when <paramref name="date"/> may not be reopened now (order: already reopened, not closed, close not finished, another day reopened, accounting lock).</summary>
    public static void EnsureCanReopen(DateOnly date, DateOnly businessDate, bool closed, DateOnly? reopenedDate, bool accountingLocked)
    {
        var d = Iso(date);
        if (reopenedDate == date) throw Invalid($"Business day {d} is already reopened.");
        if (!closed) throw Invalid($"Business day {d} is not closed: corrections post in the open day as usual.");
        if (date >= businessDate) throw Invalid($"Finish closing {d} first: the next business day is not open yet.");
        if (reopenedDate is { } other) throw Invalid($"Business day {Iso(other)} is already reopened. Close it again before reopening another day.");
        if (accountingLocked) throw Invalid($"Business day {d} is locked in Accounting. The Owner unlocks that period first (with a reason).");
    }

    /// <summary>What the reopen result tells the Owner (cash-book lock and Tally re-export spelled out).</summary>
    public static string Message(DateOnly date, bool cashBookLocked, bool tallyReexport)
    {
        var d = Iso(date);
        return $"Business day {d} reopened. Corrections of lines dated {d} and missed charges now post on {d}; issued invoice and credit-note numbers and their GST never change (a checked-out stay is corrected with a credit note). Close {d} again before closing today."
            + (cashBookLocked
                ? $" The cash book of {d} stays locked (DEC-065): reopening the day does not unlock it, so a correction touching {d} is refused until the Owner reopens that cash-book day."
                : $" Reopening the day does not change cash-book locks (DEC-065); the cash book of {d} is not locked.")
            + (tallyReexport ? $" {d} was exported to Tally: its vouchers are marked changed and the next Tally export carries them again." : "");
    }

    /// <summary>Checks the re-close of a reopened day runs: corrections of lines dated that day still pending (an approval would post on the day).</summary>
    public static IReadOnlyList<PreflightCheck> RecloseChecks(DateOnly date, IReadOnlyList<PendingApproval> pendingCorrections)
    {
        ArgumentNullException.ThrowIfNull(pendingCorrections);
        var d = Iso(date);
        var items = pendingCorrections.Select(a => new PreflightItem($"Correction · {a.Amount} · approval {a.ApprovalId.ToString(CultureInfo.InvariantCulture)}", "approval:" + a.ApprovalId.ToString(CultureInfo.InvariantCulture))).ToList();
        return [new PreflightCheck(CloseRules.Approvals, $"Corrections of {d} decided", $"Approve or reject the pending corrections of lines dated {d}, then close it again.", items.Count > 0, items)];
    }

    /// <summary>The exception line a closing record carries for one reopen of its day.</summary>
    public static string ExceptionNote(DateTime reopenedUtc, int reopenedBy, string reason, DateTime? reclosedUtc, int? reclosedBy)
    {
        var at = reopenedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var again = reclosedUtc is { } r ? $" · closed again {r.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC by user {reclosedBy?.ToString(CultureInfo.InvariantCulture)}" : "";
        return $"Reopened {at} UTC by user {reopenedBy.ToString(CultureInfo.InvariantCulture)} (DEC-077): {reason}{again}";
    }
}
