using System.Text.Json;

namespace Hcc.GstIds.Tests.Harness;

/// <summary>The P0 golden export's BASE part (prototype fixture, DEC-039 era amounts; GST is re-applied by the tests).</summary>
public sealed record BaseGolden(string BusinessDate, long OpeningCashPaise, IReadOnlyList<BaseGolden.Receipt> FolioReceipts, IReadOnlyList<BaseGolden.Outlet> OutletReceipts,
    IReadOnlyList<BaseGolden.Expense> Expenses)
{
    public sealed record Receipt(string? BookingId, string Date, string Method, string Kind, long AmountPaise);

    public sealed record Outlet(string Date, string Method, long AmountPaise);

    public sealed record Expense(string Id, string Date, string Status, string Method, long AmountPaise);

    private sealed record File(BaseGolden Base);

    public static BaseGolden Load() => JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "base-fixture.golden.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Base;
}
