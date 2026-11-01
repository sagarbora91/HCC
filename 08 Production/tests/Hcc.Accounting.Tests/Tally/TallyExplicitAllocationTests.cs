using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Tally;

/// <summary>
/// ACC-3 integration: Tally bill allocations read acc3-ageing's explicit ACC-2 vendor-payment allocations (payment source key → bill source keys)
/// before oldest-first (prototype <c>TallyBills</c> with <c>alloc</c>, test [ACC3-INT]). Fictional data.
/// </summary>
public sealed class TallyExplicitAllocationTests
{
    private static TallyBook Book() => TallyBook.Build(ChartOfAccounts.Builtin, new Dictionary<string, long>(),
        new Dictionary<string, TallyPartyInfo> { ["2200/V1"] = new("Vendor One (fictional)", null) }, ["2200/V1", "1000", "5000"]);

    private static Voucher Bill(string no, string key, long amount, int day) => new(no, new DateOnly(2026, 4, day), VoucherType.Purchase, "", key, "expense", "",
        [new VoucherEntry("5000", amount, 0), new VoucherEntry("2200/V1", 0, amount)], []);

    private static readonly Voucher Pay = new("PAY/1", new DateOnly(2026, 4, 5), VoucherType.Payment, "", "vendor-payment:1", "vendor", "",
        [new VoucherEntry("2200/V1", 50_000, 0), new VoucherEntry("1000", 0, 50_000)], []);

    private static IReadOnlyList<VoucherExport.TallyBillAllocation> Run(IReadOnlyDictionary<string, IReadOnlyList<(string Ref, long Paise)>>? explicitAllocations)
    {
        var bills = new VoucherExport.TallyBills(Book(), explicitAllocations);
        bills.Allocate(Bill("PUR/1", "expense:1", 100_000, 1));
        bills.Allocate(Bill("PUR/2", "expense:2", 50_000, 2));
        return bills.Allocate(Pay)[0];
    }

    [Fact]
    public void Without_explicit_allocations_the_oldest_bill_is_paid_first() =>
        Assert.Equal([new VoucherExport.TallyBillAllocation("PUR/1", "Agst Ref", 50_000)], Run(null));

    [Fact]
    public void An_explicit_allocation_pays_its_own_bill_and_the_rest_goes_oldest_first()
    {
        Assert.Equal([new VoucherExport.TallyBillAllocation("PUR/2", "Agst Ref", 50_000)],
            Run(new Dictionary<string, IReadOnlyList<(string, long)>> { ["vendor-payment:1"] = [("expense:2", 50_000)] }));
        Assert.Equal([new VoucherExport.TallyBillAllocation("PUR/2", "Agst Ref", 20_000), new VoucherExport.TallyBillAllocation("PUR/1", "Agst Ref", 30_000)],
            Run(new Dictionary<string, IReadOnlyList<(string, long)>> { ["vendor-payment:1"] = [("expense:2", 20_000)] }));
    }
}
