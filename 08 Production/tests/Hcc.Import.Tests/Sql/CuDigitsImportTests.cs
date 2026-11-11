using System.Text.RegularExpressions;
using Hcc.Import.Tests.Harness;

namespace Hcc.Import.Tests.Sql;

/// <summary>CU digits: the audit text an import writes holds no 10-digit phone (the guest plan key is "phone|name").</summary>
[Trait("Category", "Sql")]
public sealed class CuDigitsImportTests : IAsyncLifetime
{
    private ImportWorld _w = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Regex TenDigits = new(@"\d(?:[\s\-()+]*\d){9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public async ValueTask InitializeAsync() => _w = await ImportWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    [Fact]
    public async Task Supplier_and_company_phones_never_reach_the_import_audit_rows()
    {
        // the base leaked here: "New supplier name · trade · phone" (ImportPlanner supplier create) and "phone A → B" (supplier and company updates)
        await _w.ImportSampleAsync("room-types");
        await _w.ImportSampleAsync("companies");
        await _w.ImportSampleAsync("suppliers");
        var co = await _w.UploadAsync("owner", "companies", Templates.Sample("companies").Replace("9890011223", "98900 99999", StringComparison.Ordinal));
        Assert.Contains(co.Plan, p => p.Action == "Update" && p.Detail.Contains("phone", StringComparison.Ordinal));
        await _w.CommitAsync("owner", co.BatchId);
        var sup = await _w.UploadAsync("owner", "suppliers", Templates.Sample("suppliers").Replace("9850012345", "9850099999", StringComparison.Ordinal));
        Assert.Contains(sup.Plan, p => p.Action == "Update" && p.Detail.Contains("phone", StringComparison.Ordinal));
        await _w.CommitAsync("owner", sup.BatchId);

        Assert.True(await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'IMPORT_SUPPLIER'") >= 3);
        Assert.True(await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'IMPORT_COMPANY'") >= 3);
        var all = (string?)await _w.ScalarAsync("SELECT STRING_AGG(CONCAT(action, '|', target, '|', detail), CHAR(10)) FROM plat.audit_event WHERE action LIKE 'IMPORT[_]%' OR action IN ('MNT_VENDOR', 'COMPANY_UPDATED', 'COMPANY_CREATED')") ?? "";
        Assert.Contains("Shree Laundry Services", all, StringComparison.Ordinal);
        Assert.Contains("phone", all, StringComparison.Ordinal);
        Assert.DoesNotMatch(TenDigits, all);
        foreach (var raw in new[] { "9822100200", "9850012345", "9850099999", "9890011223", "9890099999" }) Assert.DoesNotContain(raw, all, StringComparison.Ordinal);
    }
}
