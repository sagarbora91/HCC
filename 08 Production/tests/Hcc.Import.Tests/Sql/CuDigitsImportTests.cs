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
    public async Task Guest_import_audit_rows_carry_no_phone_digits()
    {
        var up = await _w.UploadAsync("owner", "guests", "name,phone\nAsha Rao,9820011111\nVikram Shah,+91 98200 22222\n");
        Assert.True(up.CommitAllowed);
        await _w.CommitAsync("owner", up.BatchId);
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'IMPORT_GUEST'"));
        var all = (string?)await _w.ScalarAsync("SELECT STRING_AGG(CONCAT(action, '|', target, '|', detail), CHAR(10)) FROM plat.audit_event WHERE action LIKE 'IMPORT[_]%'") ?? "";
        Assert.Contains("Asha Rao", all, StringComparison.Ordinal);
        Assert.DoesNotMatch(TenDigits, all);
        Assert.DoesNotContain("9820011111", all, StringComparison.Ordinal);
        Assert.DoesNotContain("9820022222", all, StringComparison.Ordinal);
    }
}
