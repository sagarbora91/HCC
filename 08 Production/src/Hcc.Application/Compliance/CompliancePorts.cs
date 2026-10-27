using Hcc.Application.Commands;
using Hcc.Domain.Compliance;

namespace Hcc.Application.Compliance;

/// <summary>PERM ids for the Compliance calendar (migration 1101; DEC-055: Owner and Accountant only).</summary>
public static class CompliancePermissions
{
    /// <summary>View the calendar, mark done / not applicable, evidence files, change the FY, CA pack. Owner + Accountant.</summary>
    public const string Use = "PERM-CMP-001";

    /// <summary>Reopen (undo done / NA), the firm profile, archive, custom obligations, settings. Owner only.</summary>
    public const string Manage = "PERM-CMP-002";
}

public sealed record ProfileRecord(CmpProfile Profile, int? UpdatedBy, DateTime? UpdatedUtc, byte[] Version);

public sealed record CustomRecord(CmpCustom Custom, int? UpdatedBy, DateTime? UpdatedUtc, byte[] Version);

/// <summary>What an Owner reopen removed (HCC safeguard): <paramref name="Part"/> "done" or "na" and the record as it was.</summary>
public sealed record ReopenedRecord(int FyStart, string ItemId, string Part, CmpRecord Was, int ReopenedBy, DateTime ReopenedUtc, string Reason);

/// <summary><paramref name="IsDefault"/> = never saved (the defaults); a client holding the all-zero version token may then save without a prior read.
/// DEC-125: <paramref name="FyStart"/> is the stored pick; <paramref name="FyChosenIn"/> the business date's FY when it was picked (null = no pick);
/// readers use <see cref="Effective"/>.</summary>
public sealed record SettingsRecord(string CaName, string CaEmail, int DueSoonDays, int FyStart, byte[] Version, bool IsDefault = false, int? FyChosenIn = null)
{
    /// <summary>DEC-125: the FY every view and command reads on this business date.</summary>
    public int Effective(DateOnly businessDate) => SccTaxRules.EffectiveFy(FyStart, FyChosenIn, businessDate);
}

public sealed record PackRecord(long PackId, DateTime AtUtc, int By, string To, string ToName, int Overdue, int Due, int Filed, int NotApplicable, IReadOnlyList<string> Lines);

/// <summary>One property's compliance data (SCC STATE for one firm).</summary>
public sealed record ComplianceSnapshot(ProfileRecord? Profile, SettingsRecord Settings, IReadOnlyList<CmpRecord> Records, IReadOnlyList<CustomRecord> Custom,
    IReadOnlyList<string> Archived, IReadOnlyList<string> ArchivedCats, IReadOnlyList<CmpEvidence> Evidence, IReadOnlyList<ReopenedRecord> Reopened)
{
    public SccCalendar Calendar() => new(Profile?.Profile, Records, Custom.Select(c => c.Custom), Archived, ArchivedCats, Evidence);
}

/// <summary>Persistence port of the compliance commands (SQL adapter in Infrastructure/Compliance). Every command first takes the property's
/// settings row with UPDLOCK (<see cref="LockSettingsAsync"/>), so compliance commands of one property run one at a time.</summary>
public interface IComplianceStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>The property's settings, created with the defaults on first use (race-safe), locked.</summary>
    Task<SettingsRecord> LockSettingsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<ComplianceSnapshot> SnapshotAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task UpdateSettingsAsync(ICommandTransaction tx, int propertyId, byte[] expectedVersion, string caName, string caEmail, int dueSoonDays, int by, DateTime utc, CancellationToken ct = default);

    /// <summary>DEC-125: stores the pick and the business date's FY it was made in (<paramref name="chosenIn"/>).</summary>
    Task SetFyAsync(ICommandTransaction tx, int propertyId, int fyStart, int chosenIn, int by, DateTime utc, CancellationToken ct = default);

    /// <summary>Insert (expected null) or update (row version must match) the property's profile; returns the new version.</summary>
    Task<byte[]> SaveProfileAsync(ICommandTransaction tx, int propertyId, byte[]? expectedVersion, CmpProfile profile, int by, DateTime utc, CancellationToken ct = default);

    Task UpsertRecordAsync(ICommandTransaction tx, int propertyId, CmpRecord record, CancellationToken ct = default);

    Task DeleteRecordAsync(ICommandTransaction tx, int propertyId, int fyStart, string itemId, CancellationToken ct = default);

    /// <summary>Keeps the removed record; trims to <see cref="ComplianceRules.MaxReopened"/> per property.</summary>
    Task InsertReopenedAsync(ICommandTransaction tx, int propertyId, ReopenedRecord reopened, CancellationToken ct = default);

    /// <summary>Archives the key, or restores it when archived; returns true when it is archived now.</summary>
    Task<bool> ToggleArchiveAsync(ICommandTransaction tx, int propertyId, string key, int by, DateTime utc, CancellationToken ct = default);

    Task<bool> ToggleArchiveCategoryAsync(ICommandTransaction tx, int propertyId, string cat, int by, DateTime utc, CancellationToken ct = default);

    Task<(long CustomId, byte[] Version)> InsertCustomAsync(ICommandTransaction tx, int propertyId, CmpCustom custom, int by, DateTime utc, CancellationToken ct = default);

    Task<byte[]> UpdateCustomAsync(ICommandTransaction tx, int propertyId, CmpCustom custom, byte[] expectedVersion, int by, DateTime utc, CancellationToken ct = default);

    /// <summary>Soft-deletes the custom obligation and deletes its evidence files of every FY (SCC purges them); returns the files removed.</summary>
    Task<int> DeleteCustomAsync(ICommandTransaction tx, int propertyId, long customId, int by, DateTime utc, CancellationToken ct = default);

    Task<long> InsertEvidenceAsync(ICommandTransaction tx, int propertyId, int fyStart, string itemId, string fileName, string contentType, byte[] content, int by, DateTime utc, CancellationToken ct = default);

    /// <summary>Deletes the file; returns its metadata, or null when it is not this property's.</summary>
    Task<CmpEvidence?> DeleteEvidenceAsync(ICommandTransaction tx, int propertyId, long evidenceId, CancellationToken ct = default);

    /// <summary>Stores the pack and keeps the newest <see cref="ComplianceRules.MaxPacks"/>.</summary>
    Task<long> InsertPackAsync(ICommandTransaction tx, int propertyId, PackRecord pack, CancellationToken ct = default);
}

public interface IComplianceReadStore
{
    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);

    Task<ComplianceSnapshot> SnapshotAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<PackRecord>> PacksAsync(int propertyId, CancellationToken ct = default);

    /// <summary>One evidence file of the property (null when unknown).</summary>
    Task<(CmpEvidence Meta, byte[] Content)?> EvidenceFileAsync(int propertyId, long evidenceId, CancellationToken ct = default);

    /// <summary>Every evidence file of the property's FY, oldest first.</summary>
    Task<IReadOnlyList<(CmpEvidence Meta, byte[] Content)>> EvidenceFilesAsync(int propertyId, int fyStart, CancellationToken ct = default);

    /// <summary>
    /// Per month from <paramref name="from"/> to <paramref name="to"/> (first days): PF / ESIC (employee + employer) and PT of the month's LOCKED pay run,
    /// and the output GST of the invoices issued in the month less its credit notes. Months with neither a locked run nor GST are left out.
    /// </summary>
    Task<IReadOnlyList<CmpPayableMonth>> PayableAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
