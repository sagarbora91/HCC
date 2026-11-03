using System.Globalization;
using System.Text.Json;
using Hcc.Application.Commands;

namespace Hcc.Application.Accounting;

/// <summary>F5-int (ACC4-CONTRACT §1.2): reads whether a date falls in a locked financial year, inside the writer's transaction.</summary>
public interface IYearLockGuard
{
    /// <summary>The locked FY label (e.g. "2026-27") that <paramref name="date"/> falls in, or null; a null date reads the property's business
    /// date (folio, POS, invoices and other postings dated "today").</summary>
    Task<string?> LockedFyAsync(ICommandTransaction tx, int propertyId, DateOnly? date, CancellationToken ct = default);
}

/// <summary>
/// F5-int: the common posting barrier of the year lock — the server mirror of the prototype's <c>yearGuard</c> (handlers/index.ts). Every
/// command whose payload carries a <c>Date</c> / <c>BusinessDate</c> / <c>VoucherDate</c> / <c>PostingDate</c> (DateOnly or "yyyy-MM-dd") or a
/// <c>Month</c> ("yyyy-MM") dated in a locked year is refused ACC_YEAR_LOCKED (INVALID_TRANSITION, details.reason) before its handler runs —
/// vouchers, journals, expenses, payroll, day reopen and month unlock alike; a payload without a date is checked on the property's business
/// date (folio, POS, invoices). Wraps the platform runner, so every service
/// (guarded by the business day or not) passes it. Year-end commands do not use the runner (YearLockService); a correction is a journal in the
/// next year (G4).
/// </summary>
public sealed class YearLockedCommandRunner(ICommandRunner inner, IYearLockGuard guard) : ICommandRunner
{
    private static readonly string[] DateNames = ["Date", "BusinessDate", "VoucherDate", "PostingDate"];

    public Task<CommandResult<TResult>> RunAsync<TPayload, TResult>(CommandContext context, TPayload payload,
        Func<ICommandTransaction, CancellationToken, Task<TResult>> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        var dates = DatesOf(payload).Select(d => (DateOnly?)d).ToList();
        if (dates.Count == 0) dates.Add(null); // no date in the payload: the business date
        return inner.RunAsync(context, payload, async (tx, c) =>
        {
            foreach (var d in dates)
                if (await guard.LockedFyAsync(tx, context.PropertyId, d, c).ConfigureAwait(false) is { } fy) throw YearLockRules.Locked(fy);
            return await handler(tx, c).ConfigureAwait(false);
        }, ct);
    }

    /// <summary>The dates a payload is posted on (top-level properties only, as the prototype reads <c>cmd.date</c> / <c>cmd.month</c>).</summary>
    public static IReadOnlyList<DateOnly> DatesOf<TPayload>(TPayload payload)
    {
        var list = new List<DateOnly>();
        if (payload is null) return list;
        JsonElement el;
        try { el = JsonSerializer.SerializeToElement(payload); }
        catch (NotSupportedException) { return list; }
        if (el.ValueKind != JsonValueKind.Object) return list;
        foreach (var p in el.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.String) continue;
            var s = p.Value.GetString()!;
            if (DateNames.Any(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (s.Length >= 10 && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) list.Add(d);
            }
            else if (string.Equals(p.Name, "Month", StringComparison.OrdinalIgnoreCase) && s.Length == 7
                     && DateOnly.TryParseExact(s + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)) list.Add(m);
        }
        return list;
    }
}
