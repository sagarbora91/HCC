using Hcc.Application.Commands;
using Hcc.Domain.MoneyMasters;

namespace Hcc.Application.MoneyMasters;

/// <summary>Write side of the money masters (inside the command transaction; rows locked with UPDLOCK, HOLDLOCK). Field values as in
/// <see cref="MoneyRow"/> (payment method <c>settlesTo</c> = the account's code; the store maps it to mst.bank_account).</summary>
public interface IMoneyMastersStore
{
    /// <summary>Fills an empty list of the property with the seed rows (mst.usp_seed_money_masters; idempotent).</summary>
    Task SeedAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Every row of the master for the property, locked, in sort order.</summary>
    Task<IReadOnlyList<MoneyRow>> LockAllAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default);

    Task<int> InsertAsync(ICommandTransaction tx, int propertyId, string master, MoneyDraft draft, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateAsync(ICommandTransaction tx, int propertyId, string master, int id, byte[] expectedVersion, MoneyDraft draft, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetActiveAsync(ICommandTransaction tx, string master, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetSortAsync(ICommandTransaction tx, string master, int id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, string master, int id, byte[] expectedVersion, CancellationToken ct = default);

    /// <summary>Stored record values of the master with their counts (method / category / vendor text on receipts, outlet bills, expenses).
    /// Bank accounts have none (their use is the payment methods settling to them).</summary>
    Task<IReadOnlyList<(string Value, int Count)>> ValueCountsAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default);

    /// <summary>Earlier names of the rows (mst.master_history field "name"): old name → row id.</summary>
    Task<IReadOnlyDictionary<string, int>> RenamesAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default);
}

/// <summary>Read side (own connection). Lists seed an empty property first (first-time setup after the migration).</summary>
public interface IMoneyMastersReadStore
{
    Task<IReadOnlyList<MoneyRow>> RowsAsync(int propertyId, string master, CancellationToken ct = default);

    Task<IReadOnlyList<(string Value, int Count)>> ValueCountsAsync(int propertyId, string master, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, int>> RenamesAsync(int propertyId, string master, CancellationToken ct = default);
}
