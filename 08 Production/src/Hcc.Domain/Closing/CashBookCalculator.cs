using Hcc.Contracts.Closing;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Closing;

/// <summary>A cashier shift as the cash book needs it (<see cref="Counted"/> is set from the shift's count submission).</summary>
public sealed record CashBookShift(long ShiftId, string TillCode, DateOnly BusinessDate, string Status, long OpeningFloat, long? StoredExpected,
    long? Counted, int? SubmittedBy, int? ApprovedBy, string? Reason);

/// <summary>A signed drawer movement (+ in, − out).</summary>
public sealed record CashBookMovement(long MovementId, long ShiftId, string TillCode, DateOnly BusinessDate, string Kind, long Amount, string Label,
    string Reference, int PrincipalId, DateTime RecordedUtc);

public sealed record CashBookLock(DateOnly BusinessDate, bool Locked, int? LockedBy, DateTime? LockedUtc);

/// <summary>Everything the cash book reads for a range: drawer, locks, closed days and the cash each till carried into the range.</summary>
public sealed record CashBookData(IReadOnlyList<CashBookShift> Shifts, IReadOnlyList<CashBookMovement> Movements, IReadOnlyDictionary<string, long> CarryIn,
    IReadOnlyList<CashBookLock> Locks, IReadOnlyDictionary<DateOnly, IReadOnlyList<CashBookEventView>> Events, IReadOnlySet<DateOnly> ClosedDates);

/// <summary>
/// Cash-book arithmetic ported from the prototype (<c>cbDay</c>, <c>cbRange</c>): opening = previous closing carried forward (per till, from the
/// last count), receipts/payments = the drawer's signed movements, closing = book balance, counted vs expected = over/short with the cashier's
/// reason. The production drawer already holds banking and transfers as movements, so expected cash includes them and a count is taken after
/// banking (the prototype's separate "banked" amount is just a payment line here).
/// </summary>
public static class CashBookCalculator
{
    public const string Note = "Derived from the shared cash drawer (opening float carried from the last count, signed movements, counts). Payroll is paid by bank transfer, not drawer cash.";

    public static CashBookRangeView Compute(DateOnly from, DateOnly to, DateOnly businessDate, CashBookData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (to < from) throw HccException.Validation("'to' must be on or after 'from'.");
        var carry = new Dictionary<string, long>(data.CarryIn, StringComparer.Ordinal);
        var locks = data.Locks.ToDictionary(l => l.BusinessDate);
        var dates = data.Shifts.Select(s => s.BusinessDate).Concat(data.Movements.Select(m => m.BusinessDate)).Concat(data.ClosedDates).Append(businessDate)
            .Where(d => d >= from && d <= to && d <= businessDate).Distinct().Order().ToList();
        var days = new List<CashBookDayView>();
        var gaps = 0L;
        long? prevCarried = null;
        foreach (var d in dates)
        {
            var shifts = data.Shifts.Where(s => s.BusinessDate == d).ToList();
            var moves = data.Movements.Where(m => m.BusinessDate == d).OrderBy(m => m.MovementId).ToList();
            var tills = carry.Keys.Concat(shifts.Select(s => s.TillCode)).Concat(moves.Select(m => m.TillCode)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            long opening = 0, book = 0, carried = 0, expectedSum = 0, countedSum = 0, difference = 0;
            var anyShift = false;
            var anyCounted = false;
            var allCounted = true;
            var reconciles = true;
            int? countedBy = null, approvedBy = null;
            var reasons = new List<string>();
            foreach (var till in tills)
            {
                var ts = shifts.Where(s => s.TillCode == till).OrderBy(s => s.ShiftId).ToList();
                var tm = moves.Where(m => m.TillCode == till).ToList();
                var moved = tm.Sum(m => m.Amount);
                var tillOpening = ts.Count > 0 ? ts[0].OpeningFloat : carry.GetValueOrDefault(till);
                var tillBook = tillOpening + moved;
                opening += tillOpening;
                book += tillBook;
                if (ts.Count == 0)
                {
                    carried += tillBook;
                    carry[till] = tillBook;
                    continue;
                }

                anyShift = true;
                foreach (var s in ts)
                {
                    var expected = s.OpeningFloat + tm.Where(m => m.ShiftId == s.ShiftId).Sum(m => m.Amount);
                    if (s.StoredExpected is { } stored && stored != expected) reconciles = false;
                    if (s.Counted is not { } c) continue;
                    anyCounted = true;
                    difference += c - expected;
                    if (s.Reason is not null) reasons.Add(s.Reason);
                    countedBy = s.SubmittedBy;
                    if (s.ApprovedBy is not null) approvedBy = s.ApprovedBy;
                }

                var last = ts[^1];
                expectedSum += last.OpeningFloat + tm.Where(m => m.ShiftId == last.ShiftId).Sum(m => m.Amount);
                if (last.Counted is { } lc)
                {
                    countedSum += lc;
                    carried += lc;
                    carry[till] = lc;
                }
                else
                {
                    allCounted = false;
                    carried += tillBook;
                    carry[till] = tillBook;
                }
            }

            var entries = moves.Select(m => new CashBookEntry(m.MovementId, m.Kind, m.Amount < 0 ? "payment" : "receipt", m.Label, m.Reference, m.TillCode, m.ShiftId,
                m.PrincipalId, m.Amount > 0 ? m.Amount : 0, m.Amount < 0 ? -m.Amount : 0, m.RecordedUtc)).ToList();
            var lockRow = locks.GetValueOrDefault(d);
            var status = data.ClosedDates.Contains(d) ? "Closed" : d == businessDate ? "Open" : "Past";
            if (prevCarried is { } pc) gaps += opening - pc;
            prevCarried = carried;
            days.Add(new CashBookDayView(d, status, lockRow?.Locked == true, lockRow?.LockedBy, lockRow?.LockedUtc,
                data.Events.TryGetValue(d, out var ev) ? ev : [], opening, entries, entries.Sum(e => e.InPaise), entries.Sum(e => e.OutPaise),
                moves.Where(m => m.Kind == "banking").Sum(m => -m.Amount), book,
                anyShift ? expectedSum : null, anyShift && allCounted ? countedSum : null, anyCounted ? difference : null,
                reasons.Count > 0 ? string.Join(" · ", reasons) : null, countedBy, approvedBy, carried, reconciles));
        }

        var lastDay = days.Count > 0 ? days[^1] : null;
        return new CashBookRangeView(from, to, businessDate, days, days.Count > 0 ? days[0].OpeningPaise : 0, days.Sum(x => x.ReceiptsPaise), days.Sum(x => x.PaymentsPaise),
            days.Sum(x => x.CarriedPaise - x.ClosingPaise), gaps, lastDay?.CarriedPaise ?? 0, lastDay?.ClosingPaise ?? 0, Note);
    }
}
