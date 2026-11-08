using Hcc.Contracts.Closing;

namespace Hcc.Application.Closing;

// F8-QW-NA · server twin of the prototype's naChecklist (03 Prototype/src/domain/streams/night-audit.ts). SHOW ONLY (owner question QWQ-1):
// a line is Blocking only when the existing close preflight already blocks on the same records; nothing here adds a blocker.

/// <summary>One night-audit line. <see cref="Group"/> is "operational" or "measurement".</summary>
public sealed record NightAuditLine(string Key, string Label, bool Ok, string Detail, bool Blocking, string? Link, string Group);

/// <summary>GET /api/v1/closing/night-audit?date= (PERM-CLS-001).</summary>
public sealed record NightAuditResult(DateOnly BusinessDate, bool Closed, IReadOnlyList<NightAuditLine> Lines);

/// <summary>Signals the preflight does not carry. A null value means "not known on this server" and the line is reported ok with a note.</summary>
public sealed record NightAuditSignals(int? DepartedRoomsWithActiveCard, DateTime? LastGoodBackupLocal);

public static class NightAuditRules
{
    private static string Plural(int n, string w) => $"{n} {w}{(n == 1 ? "" : "s")}";

    /// <summary>The lines in checklist order, from the preflight checks and the extra signals.</summary>
    public static IReadOnlyList<NightAuditLine> Build(PreflightResult pre, NightAuditSignals signals)
    {
        ArgumentNullException.ThrowIfNull(pre);
        ArgumentNullException.ThrowIfNull(signals);
        int Count(string id) => pre.Checks.FirstOrDefault(c => c.Id == id)?.Items.Count ?? 0;
        NightAuditLine FromCheck(string id, string key, string label, string okText, string noun, string link)
        {
            var n = Count(id);
            return new NightAuditLine(key, label, n == 0, n == 0 ? okText : $"{Plural(n, noun)} to resolve", true, link, "operational");
        }
        var approvals = Count("approvals") + Count("posVoids");
        var expenses = Count("expenses");
        var shiftOpen = Count("shift") > 0;
        var keys = signals.DepartedRoomsWithActiveCard;
        var backup = signals.LastGoodBackupLocal;
        var backupOk = backup is { } b && DateOnly.FromDateTime(b) == pre.BusinessDate;
        return
        [
            FromCheck("arrivals", "na.arrivals", "Arrivals resolved (checked in, cancelled or no-show)", "Every arrival is resolved", "arrival", "reservations"),
            FromCheck("departures", "na.departures", "Departures out", "Every departure is checked out", "overdue departure", "folios"),
            FromCheck("nights", "na.nights", "Room nights posted", "Tonight's room line is on every in-house stay", "stay without tonight's room line", "closing"),
            new("na.approvals", "Pending approvals and expenses cleared", approvals + expenses == 0,
                approvals + expenses == 0 ? "Nothing waiting" : $"{Plural(approvals, "approval")} and {Plural(expenses, "expense")} waiting", true, "approvals", "operational"),
            FromCheck("pos", "na.pos", "POS checks closed", "No open restaurant check", "open check", "pos"),
            new("na.cash", "Cash count submitted", !shiftOpen, shiftOpen ? "The drawer is not counted yet" : "Counted", true, "cashier", "operational"),
            new("na.keys", "Key cards cancelled for departed rooms", keys is null or 0,
                keys is null ? "Not checked on this server" : keys == 0 ? "No active card on a departed room" : $"{Plural(keys.Value, "room")} still with an active card", false, "keys", "operational"),
            new("na.backup", "Backup OK today", backupOk,
                backup is null ? "No backup recorded" : backupOk ? $"Backup at {backup:HH:mm}" : $"Last good backup {backup:yyyy-MM-dd HH:mm}", false, "syshealth", "operational"),
        ];
    }
}

/// <summary>Reads the preflight through <see cref="ClosingService"/> (same permission) and adds the extra signals when a source is registered.</summary>
public sealed class NightAuditService(ClosingService closing, Func<int, DateOnly, CancellationToken, Task<NightAuditSignals>>? signals = null)
{
    public async Task<NightAuditResult> GetAsync(Hcc.Application.Billing.Caller caller, DateOnly? date, CancellationToken ct = default)
    {
        var pre = await closing.GetPreflightAsync(caller, date, ct).ConfigureAwait(false);
        var sig = signals is null ? new NightAuditSignals(null, null) : await signals(caller.PropertyId, pre.BusinessDate, ct).ConfigureAwait(false);
        return new NightAuditResult(pre.BusinessDate, pre.Closed, NightAuditRules.Build(pre, sig));
    }
}
