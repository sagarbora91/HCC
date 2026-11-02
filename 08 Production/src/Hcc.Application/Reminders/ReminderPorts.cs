using Hcc.Application.Commands;

namespace Hcc.Application.Reminders;

/// <summary>Reminder permission (migration 4705): Owner, Manager, Reception, Accountant.</summary>
public static class ReminderPermissions
{
    public const string Manage = "PERM-RMD-001";
}

public sealed record ReminderRec(long ReminderId, string Subject, DateOnly Date, string? Time, int LeadDays, string Role, string? Note, string Repeat, string Status,
    DateOnly? NextDate, int CreatedBy, DateTime CreatedUtc, int? DoneBy, DateTime? DoneUtc);

/// <summary>Data-access port of the desk reminders (plat.reminder, migration 4704).</summary>
public interface IReminderStore
{
    Task<IReadOnlyCollection<string>> RolesOfUserAsync(ICommandTransaction tx, int userId, CancellationToken ct = default);

    Task<IReadOnlyCollection<string>> KnownRolesAsync(ICommandTransaction tx, CancellationToken ct = default);

    Task<ReminderRec?> LockAsync(ICommandTransaction tx, int propertyId, long reminderId, CancellationToken ct = default);

    Task<long> InsertAsync(ICommandTransaction tx, int propertyId, ReminderRec r, CancellationToken ct = default);

    Task UpdateAsync(ICommandTransaction tx, int propertyId, ReminderRec r, CancellationToken ct = default);

    // ── reads (own connection) ──
    Task<IReadOnlyList<ReminderRec>> ListAsync(int propertyId, int userId, CancellationToken ct = default);

    Task<IReadOnlyCollection<string>> RolesOfUserAsync(int userId, CancellationToken ct = default);
}
