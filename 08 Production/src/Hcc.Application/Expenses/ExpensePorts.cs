using Hcc.Application.Commands;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;

namespace Hcc.Application.Expenses;

/// <summary>PERM ids (RBAC matrix v1 section 5) used by the Expenses module. The grants are seeded in 0101_access_rbac_seed.</summary>
public static class ExpensePermissions
{
    public const string Submit = "PERM-EXP-001";
    public const string Decide = "PERM-EXP-002";
    public const string PostReverse = "PERM-EXP-003";
}

/// <summary>Outbox event names the Accounting agent consumes (one per approved / reversed expense).</summary>
public static class ExpenseEvents
{
    public const string Approved = "ExpenseApproved";
    public const string Reversed = "ExpenseReversed";
}

public sealed record ExpenseRecord(long ExpenseId, int PropertyId, string Vendor, string Category, Paise Amount, string Method, DateOnly BusinessDate,
    string Status, int SubmittedBy, string? Notes, string? Receipt, string? Reason, int? DecidedBy, DateTime? DecidedUtc, int? TillId, long? ReversalOf,
    long? ReversedBy, byte[] Version)
{
    public bool IsReversal => ReversalOf is not null;

    public ExpenseFact Fact => new(ExpenseId, Vendor, Amount, BusinessDate, Method, Status, IsReversal);
}

public sealed record NewExpense(int PropertyId, string Vendor, string Category, Paise Amount, string Method, DateOnly BusinessDate, string Status,
    int SubmittedBy, string? Notes, string? Receipt, string? Reason, int? DecidedBy, int? TillId, long? ReversalOf, DateTime NowUtc);

public sealed record ExpenseFilter(string? Status, DateOnly? From, DateOnly? To, int? SubmittedBy);

/// <summary>Persistence port of the Expenses commands and queries (SQL adapter in Infrastructure/Expenses).</summary>
public interface IExpenseStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<ExpenseRecord> InsertAsync(ICommandTransaction tx, NewExpense e, CancellationToken ct = default);

    /// <summary>Row lock (UPDLOCK) inside the command transaction; null when unknown at the property.</summary>
    Task<ExpenseRecord?> LockAsync(ICommandTransaction tx, int propertyId, long expenseId, CancellationToken ct = default);

    /// <summary>Writes the mutable columns (status, date, reason, decision, till, reversed_by) when the version still matches; STALE_VERSION otherwise. Returns the new row.</summary>
    Task<ExpenseRecord> UpdateAsync(ICommandTransaction tx, ExpenseRecord updated, byte[] expectedVersion, DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseFact>> FactsOnAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default);

    // Reads (own connection).
    Task<ExpenseRecord?> GetAsync(int propertyId, long expenseId, CancellationToken ct = default);

    Task<(IReadOnlyList<ExpenseRecord> Items, int Total)> ListAsync(int propertyId, ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseFact>> FactsOnAsync(int propertyId, DateOnly date, CancellationToken ct = default);
}
