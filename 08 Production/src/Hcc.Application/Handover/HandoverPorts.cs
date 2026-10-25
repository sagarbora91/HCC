using Hcc.Application.Commands;
using Hcc.Contracts.Handover;
using Hcc.Domain.Handover;

namespace Hcc.Application.Handover;

/// <summary>
/// PERM ids used by the Shift handover module. The RBAC matrix has no handover rows, so the prototype's coarse permissions map onto
/// seeded ids (no new PERM ids, which keeps the 45-permission matrix test intact): housekeeping → PERM-HK-001, payments → PERM-PAY-001,
/// approve → PERM-APR-001, reports → PERM-RPT-002. Module access = any of Board / Payments / Approve (Accountant and Tech admin hold none).
/// </summary>
public static class HandoverPermissions
{
    public const string Board = "PERM-HK-001";
    public const string Payments = "PERM-PAY-001";
    public const string Approve = "PERM-APR-001";
    public const string FinanceReport = "PERM-RPT-002";

    public static readonly IReadOnlyList<string> AnyAccess = [Board, Payments, Approve];
}

public sealed record HovTemplateRec(long TemplateId, string Name, string Desk, string Shift, bool Active, int Version, byte[] RowVer, IReadOnlyList<string> Roles, IReadOnlyList<HovItemDef> Items);

public sealed record HovCarryRec(long CarryId, string Text, bool Done, int? DoneBy, DateTime? DoneUtc);

public sealed record HovNoteRec(long NoteId, string Desk, DateOnly Date, string Shift, string ToShift, int AuthorId, string AuthorName, DateTime WrittenUtc, string Issues, bool Nothing,
    string Keys, string Guests, long? CashPaise, string? CashNote, string Status, int? AckBy, DateTime? AckUtc, string? AckRemark, byte[] RowVer, IReadOnlyList<HovCarryRec> Carry);

public sealed record HovNoteWrite(string Desk, DateOnly Date, string Shift, string ToShift, string Issues, bool Nothing, string Keys, string Guests, long? CashPaise, string? CashNote, IReadOnlyList<string> Carry);

/// <summary>Role codes of a principal (active plat.user_role rows). Desk membership is by role code, as in the prototype.</summary>
public interface IHandoverPrincipals
{
    Task<IReadOnlyList<string>> RoleCodesAsync(int propertyId, int principalId, DateTime utcNow, CancellationToken ct = default);
}

/// <summary>Write side (inside the command transaction; rows are locked with UPDLOCK).</summary>
public interface IHandoverStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<HovTemplateRec?> LockTemplateAsync(ICommandTransaction tx, int propertyId, long templateId, CancellationToken ct = default);

    Task<bool> TemplateNameTakenAsync(ICommandTransaction tx, int propertyId, string name, long? exceptTemplateId, CancellationToken ct = default);

    Task<long> InsertTemplateAsync(ICommandTransaction tx, int propertyId, HandoverRules.CleanTemplate t, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateTemplateAsync(ICommandTransaction tx, long templateId, byte[] expectedVersion, HandoverRules.CleanTemplate t, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetTemplateActiveAsync(ICommandTransaction tx, long templateId, byte[] expectedVersion, bool active, int principalId, DateTime utc, CancellationToken ct = default);

    Task<HovRun?> LockRunAsync(ICommandTransaction tx, int propertyId, long templateId, DateOnly date, CancellationToken ct = default);

    Task<HovRun> CreateRunAsync(ICommandTransaction tx, int propertyId, HovTemplateRec template, DateOnly date, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateItemAsync(ICommandTransaction tx, long runId, HovItemState item, CancellationToken ct = default);

    Task SubmitRunAsync(ICommandTransaction tx, long runId, int principalId, DateTime utc, bool late, CancellationToken ct = default);

    Task ReopenRunAsync(ICommandTransaction tx, long runId, string reason, CancellationToken ct = default);

    /// <summary>Notes of the desk awaiting acknowledgement that someone else wrote.</summary>
    Task<IReadOnlyList<HovNoteRec>> IncomingAsync(ICommandTransaction tx, int propertyId, int principalId, string desk, CancellationToken ct = default);

    Task<HovNoteRec?> LockNoteByKeyAsync(ICommandTransaction tx, int propertyId, string desk, DateOnly date, string shift, CancellationToken ct = default);

    Task<HovNoteRec?> LockNoteAsync(ICommandTransaction tx, int propertyId, long noteId, CancellationToken ct = default);

    Task<long> InsertNoteAsync(ICommandTransaction tx, int propertyId, HovNoteWrite n, int principalId, DateTime utc, CancellationToken ct = default);

    Task ReplaceNoteAsync(ICommandTransaction tx, long noteId, HovNoteWrite n, int principalId, DateTime utc, CancellationToken ct = default);

    Task AcknowledgeNoteAsync(ICommandTransaction tx, long noteId, int principalId, DateTime utc, string? remark, CancellationToken ct = default);

    Task CompleteCarryAsync(ICommandTransaction tx, long carryId, int principalId, DateTime utc, CancellationToken ct = default);
}

/// <summary>Read side (own connection, no transaction).</summary>
public interface IHandoverReadStore
{
    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<HovTemplateRec>> TemplatesAsync(int propertyId, CancellationToken ct = default);

    /// <summary>Saved runs of a business date (with items).</summary>
    Task<IReadOnlyList<HovRun>> RunsAsync(int propertyId, DateOnly date, CancellationToken ct = default);

    /// <summary>Recent notes (last 45 days) plus every unacknowledged note and every note with an open carry-forward item, newest first.</summary>
    Task<IReadOnlyList<HovNoteRec>> NotesAsync(int propertyId, DateOnly businessDate, CancellationToken ct = default);
}
