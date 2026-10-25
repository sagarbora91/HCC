using System.Text.Json;
using Hcc.Domain.Dates;
using Hcc.Domain.Money;

namespace Hcc.Platform.Tests.Golden;

/// <summary>
/// Raw records + prototype-computed figures exported from the prototype BASE fixture by
/// <c>08 Production/tools/golden/export-golden.mjs</c> (regenerate after a prototype fixture change).
/// </summary>
public sealed record GoldenFixture(string GeneratedBy, string Source, bool FictionalDataOnly, GoldenBase Base, GoldenJourney Journey)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static GoldenFixture Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", "base-fixture.golden.json");
        return JsonSerializer.Deserialize<GoldenFixture>(File.ReadAllText(path), Options) ?? throw new InvalidOperationException("Empty golden file.");
    }
}

public sealed record GoldenReceipt(string? BookingId, string Date, string Method, string Kind, long AmountPaise)
{
    public DatedReceipt ToDated() => new(StayDates.Parse(Date), Method, FolioTotals.ParseKind(Kind), new Paise(AmountPaise));
}

public sealed record GoldenOutletReceipt(string Date, string Method, long AmountPaise);

public sealed record GoldenExpense(string Id, string Date, string Status, string Method, long AmountPaise);

public sealed record GoldenBaseExpected(long DrawerPaise, long CollectionsPaise, long OutletCashPaise, int Occupied);

public sealed record GoldenBase(string BusinessDate, long OpeningCashPaise, IReadOnlyList<GoldenReceipt> FolioReceipts,
    IReadOnlyList<GoldenOutletReceipt> OutletReceipts, IReadOnlyList<GoldenExpense> Expenses, GoldenBaseExpected Expected)
{
    public DateOnly Date => StayDates.Parse(BusinessDate);

    public IEnumerable<OutletReceipt> Outlet => OutletReceipts.Select(o => new OutletReceipt(StayDates.Parse(o.Date), o.Method, new Paise(o.AmountPaise)));

    public IEnumerable<CashExpense> CashExpenses => Expenses.Select(e =>
        new CashExpense(StayDates.Parse(e.Date), Enum.Parse<ExpenseStatus>(e.Status, ignoreCase: true), e.Method, new Paise(e.AmountPaise)));
}

public sealed record GoldenLine(string Kind, string Label, string Date, long AmountPaise, int TaxBps);

public sealed record GoldenJourneyExpected(long BalanceBeforeSettlementPaise, long NetPaise, long TaxPaise, long TotalPaise, long PaidPaise, long DepositPaise, long BalancePaise);

public sealed record GoldenJourney(string Room, IReadOnlyList<GoldenLine> Lines, IReadOnlyList<GoldenReceipt> Receipts, GoldenJourneyExpected Expected);
