using Hcc.Application.Commands;
using Hcc.Domain.MoneyMasters;

namespace Hcc.Application.MoneyMasters;

/// <summary>
/// MA-11: the property's payment methods (mst.payment_method + the kind of the settles-to account) for the commands that take money —
/// RecordReceipt / ExecuteAdjustment (folio), SettleCheck / VoidBill (outlet), SubmitExpense / DecideExpense / ReverseExpense. An empty
/// list means "not seeded yet" and the commands use <see cref="PaymentMethodBook.Defaults"/>. With <paramref name="tx"/> the read runs
/// inside that command transaction (no locks taken); without it on its own connection.
/// </summary>
public interface IPaymentMethodSource
{
    Task<IReadOnlyList<TenderMethod>> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default);
}

/// <summary>The fixed default list (tests and compositions without the MoneyMasters module).</summary>
public sealed class DefaultPaymentMethods : IPaymentMethodSource
{
    public static readonly DefaultPaymentMethods Instance = new();

    public Task<IReadOnlyList<TenderMethod>> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default) =>
        Task.FromResult(PaymentMethodBook.Defaults);
}
