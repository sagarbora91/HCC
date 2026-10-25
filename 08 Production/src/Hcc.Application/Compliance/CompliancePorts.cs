using Hcc.Application.Commands;
using Hcc.Domain.Compliance;

namespace Hcc.Application.Compliance;

/// <summary>PERM ids for the Compliance calendar (added by migration 1100; RBAC matrix has no row yet — DEC-055: Owner and Accountant only).</summary>
public static class CompliancePermissions
{
    /// <summary>View the calendar, mark filed / not applicable, generate the CA pack. Owner + Accountant.</summary>
    public const string Use = "PERM-CMP-001";

    /// <summary>Reopen a filing, manage obligations and due rules, settings, seed defaults. Owner only.</summary>
    public const string Manage = "PERM-CMP-002";
}

public sealed record ObligationRecord(long ObligationId, int PropertyId, string Kind, string Name, string Form, string Frequency, string Owner,
    string FromPeriod, bool Active, string? Note, byte[] Version, IReadOnlyList<DatedRule> Rules)
{
    public ObligationSchedule Schedule => new(Frequency, FromPeriod, Rules);
}

public sealed record FilingRecord(long ObligationId, string Period, string Status, int By, DateTime AtUtc, string? Reference, string? Reason);

/// <summary><paramref name="IsDefault"/> = never saved (the defaults); a client holding the all-zero version token may then save without a prior read.</summary>
public sealed record SettingsRecord(string CaName, string CaEmail, int DueSoonDays, byte[] Version, bool IsDefault = false);

public sealed record PackRecord(long PackId, DateTime AtUtc, int By, string To, string ToName, int Overdue, int Due, int Filed, int NotApplicable, IReadOnlyList<string> Lines);

public sealed record ComplianceSnapshot(IReadOnlyList<ObligationRecord> Obligations, IReadOnlyList<FilingRecord> Filings, SettingsRecord Settings);

/// <summary>Persistence port of the compliance commands (SQL adapter in Infrastructure/Compliance). Lock* methods take UPDLOCK row locks.</summary>
public interface IComplianceStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<ObligationRecord?> LockObligationAsync(ICommandTransaction tx, int propertyId, long obligationId, CancellationToken ct = default);

    Task<FilingRecord?> GetFilingAsync(ICommandTransaction tx, long obligationId, string period, CancellationToken ct = default);

    Task InsertFilingAsync(ICommandTransaction tx, FilingRecord filing, CancellationToken ct = default);

    Task DeleteFilingAsync(ICommandTransaction tx, long obligationId, string period, CancellationToken ct = default);

    /// <summary>Keeps the reopened filing record (who, when, why); trims to <see cref="ComplianceRules.MaxReopened"/> per property.</summary>
    Task InsertReopenedAsync(ICommandTransaction tx, int propertyId, FilingRecord filing, int reopenedBy, DateTime reopenedUtc, string reason, CancellationToken ct = default);

    /// <summary>Locks the (property, name) key range so two adds cannot both pass the duplicate check.</summary>
    Task<bool> NameExistsAsync(ICommandTransaction tx, int propertyId, string name, long? excludeId, CancellationToken ct = default);

    Task<int> CountObligationsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<long> InsertObligationAsync(ICommandTransaction tx, int propertyId, string kind, string name, string form, string frequency, string owner,
        string fromPeriod, string? note, DueRule firstRule, int by, DateTime utc, CancellationToken ct = default);

    Task UpdateObligationAsync(ICommandTransaction tx, long obligationId, byte[] expectedVersion, string name, string form, string owner, string? note, int by, DateTime utc, CancellationToken ct = default);

    Task UpsertRuleAsync(ICommandTransaction tx, long obligationId, DatedRule rule, CancellationToken ct = default);

    Task SetActiveAsync(ICommandTransaction tx, long obligationId, byte[] expectedVersion, bool active, int by, DateTime utc, CancellationToken ct = default);

    Task<byte[]> ObligationVersionAsync(ICommandTransaction tx, long obligationId, CancellationToken ct = default);

    /// <summary>The property's settings, created with the defaults on first use (race-safe).</summary>
    Task<SettingsRecord> LockSettingsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task UpdateSettingsAsync(ICommandTransaction tx, int propertyId, byte[] expectedVersion, string caName, string caEmail, int dueSoonDays, int by, DateTime utc, CancellationToken ct = default);

    Task<ComplianceSnapshot> SnapshotAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Stores the pack and keeps the newest <see cref="ComplianceRules.MaxPacks"/>.</summary>
    Task<long> InsertPackAsync(ICommandTransaction tx, int propertyId, PackRecord pack, CancellationToken ct = default);
}

public interface IComplianceReadStore
{
    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);

    Task<ComplianceSnapshot> SnapshotAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<PackRecord>> PacksAsync(int propertyId, CancellationToken ct = default);
}

/// <summary>One calendar row = obligation × period (prototype <c>CMPItem</c>).</summary>
public sealed record CalendarEntry(ObligationRecord Obligation, CalendarRow Row, FilingRecord? Filing)
{
    public bool IsOpen => ComplianceRules.IsOpen(Row.Status);
}

public static class ComplianceCalendar
{
    /// <summary>Every tracked period of every (active) obligation, soonest due date first (prototype <c>cmpItems</c>).</summary>
    public static IReadOnlyList<CalendarEntry> Build(ComplianceSnapshot s, DateOnly businessDate, bool includeInactive = false)
    {
        ArgumentNullException.ThrowIfNull(s);
        var byKey = s.Filings.ToDictionary(f => (f.ObligationId, f.Period));
        var rows = new List<CalendarEntry>();
        foreach (var o in s.Obligations)
        {
            if (!o.Active && !includeInactive) continue;
            foreach (var p in ComplianceRules.Periods(o.Frequency, o.FromPeriod, businessDate))
            {
                byKey.TryGetValue((o.ObligationId, p), out var filing);
                rows.Add(new CalendarEntry(o, ComplianceRules.Row(o.Schedule, p, businessDate, s.Settings.DueSoonDays, filing?.Status), filing));
            }
        }

        return rows.OrderBy(r => r.Row.Due).ThenBy(r => r.Obligation.Name, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<CalendarEntry> DueThisWeek(ComplianceSnapshot s, DateOnly businessDate) =>
        Build(s, businessDate).Where(i => i.Row.Status is ComplianceStatus.Overdue or ComplianceStatus.DueToday or ComplianceStatus.DueSoon).ToList();
}
