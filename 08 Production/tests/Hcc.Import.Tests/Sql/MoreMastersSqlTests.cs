using Hcc.Contracts;
using Hcc.Contracts.Import;
using Hcc.Domain.Errors;
using Hcc.Import.Tests.Harness;

namespace Hcc.Import.Tests.Sql;

/// <summary>
/// A09-09: departments, company accounts, suppliers and stock items import through the owning modules' stores (hr.department,
/// grp.company_account, mnt.vendor, stk.item), re-import as "Unchanged", update what the file may change, and stay Owner-only to commit.
/// </summary>
[Trait("Category", "Sql")]
public class MoreMastersSqlTests : IAsyncLifetime
{
    private ImportWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await ImportWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static readonly string[] NewMasters = ["departments", "companies", "suppliers", "stock-items"];

    [Fact]
    public async Task Samples_commit_into_the_owning_modules_and_reimport_changes_nothing()
    {
        // negotiated company rates refer to room types: without them the file is refused row by row
        var early = await _w.UploadAsync("owner", "companies", Templates.Sample("companies"));
        Assert.False(early.CommitAllowed);
        Assert.Contains(early.Errors, e => e is { Row: 2, Column: "negotiatedRates" } && e.Message.Contains("Import the room types file first", StringComparison.Ordinal));
        await _w.ImportSampleAsync("room-types");

        Assert.Equal(new ImportCounts(4, 0, 0, 0), (await _w.ImportSampleAsync("departments")).Counts);
        Assert.Equal(new ImportCounts(2, 0, 0, 0), (await _w.ImportSampleAsync("companies")).Counts);
        Assert.Equal(new ImportCounts(3, 0, 0, 0), (await _w.ImportSampleAsync("suppliers")).Counts);
        Assert.Equal(new ImportCounts(4, 0, 0, 0), (await _w.ImportSampleAsync("stock-items")).Counts);

        Assert.Equal(4, await _w.CountAsync("SELECT COUNT(*) FROM hr.department"));
        Assert.Equal(3, await _w.CountAsync("SELECT leave_cap FROM hr.department WHERE dept_code = 'HK'"));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM hr.department WHERE dept_code = 'FB' AND leave_cap IS NULL"));

        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM grp.company_account"));
        Assert.Equal(5000000, await _w.CountAsync("SELECT credit_limit_paise FROM grp.company_account WHERE name = N'Mehta Traders (fictional)'"));
        Assert.Equal("27ABCDE1234F1Z5", (string?)await _w.ScalarAsync("SELECT gstin FROM grp.company_account WHERE name = N'Mehta Traders (fictional)'"));
        Assert.Equal(380000, await _w.CountAsync("SELECT CAST(JSON_VALUE(rates_json, '$.DLX') AS BIGINT) FROM grp.company_account WHERE name = N'Mehta Traders (fictional)'"));
        Assert.Equal(0, await _w.CountAsync("SELECT credit_limit_paise FROM grp.company_account WHERE city = N'Latur'"));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM grp.company_limit_request"));   // stored value only, no request raised
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'CompanyCreated'"));

        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM mnt.vendor"));
        Assert.Equal("Laundry", (string?)await _w.ScalarAsync("SELECT trade FROM mnt.vendor WHERE name = N'Shree Laundry Services (fictional)'"));

        Assert.Equal(4, await _w.CountAsync("SELECT COUNT(*) FROM stk.item"));
        Assert.Equal(45000, await _w.CountAsync("SELECT cost_paise FROM stk.item WHERE code = 'LIN-BS'"));
        Assert.Equal(1200, await _w.CountAsync("SELECT laundry_rate_paise FROM stk.item WHERE code = 'LIN-BS'"));
        Assert.Equal(100, await _w.CountAsync("SELECT std_x100 FROM stk.item WHERE code = 'LIN-BS'"));
        Assert.Equal(0, await _w.CountAsync("SELECT cost_paise FROM stk.item WHERE code = 'CLN-FL'"));
        Assert.Equal(0, await _w.CountAsync("SELECT ISNULL(SUM(qty), 0) FROM stk.stock"));   // no opening stock: the store is counted after the import

        foreach (var (action, n) in new[] { ("IMPORT_DEPARTMENT", 4), ("HR_DEPT_SAVED", 4), ("IMPORT_COMPANY", 2), ("COMPANY_CREATED", 2), ("IMPORT_SUPPLIER", 3), ("MNT_VENDOR", 3),
                     ("IMPORT_STOCK_ITEM", 4), ("STOCK_ITEM_CREATE", 4) })
            Assert.Equal(n, await _w.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = '{action}'"));

        foreach (var m in NewMasters)
        {
            var again = await _w.UploadAsync("owner", m, Templates.Sample(m));
            Assert.True(again.ErrorCount == 0, m + ": " + string.Join(" | ", again.Errors.Select(e => e.Message)));
            Assert.Equal(0, again.Counts.Create + again.Counts.Update + again.Counts.Remove);
            Assert.Equal("Nothing to change: every row is already in HCC.", again.CommitBlockedReason);
        }
    }

    [Fact]
    public async Task Reimport_updates_departments_company_limits_suppliers_and_stock_levels_only()
    {
        await _w.ImportSampleAsync("room-types");
        foreach (var m in NewMasters) await _w.ImportSampleAsync(m);

        var dept = await _w.UploadAsync("owner", "departments", Templates.Sample("departments").Replace("HK,Housekeeping,3", "HK,Housekeeping,4", StringComparison.Ordinal));
        Assert.Equal(new ImportCounts(0, 1, 3, 0), dept.Counts);
        await _w.CommitAsync("owner", dept.BatchId);
        Assert.Equal(4, await _w.CountAsync("SELECT leave_cap FROM hr.department WHERE dept_code = 'HK'"));

        var co = await _w.UploadAsync("owner", "companies", Templates.Sample("companies").Replace("\"50,000\"", "75000", StringComparison.Ordinal));
        Assert.Equal(new ImportCounts(0, 1, 1, 0), co.Counts);
        Assert.Contains("credit limit ₹50,000 → ₹75,000", co.Plan.Single(p => p.Action == "Update").Detail, StringComparison.Ordinal);
        await _w.CommitAsync("owner", co.BatchId);
        Assert.Equal(7500000, await _w.CountAsync("SELECT credit_limit_paise FROM grp.company_account WHERE name = N'Mehta Traders (fictional)'"));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM grp.company_account"));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'COMPANY_LIMIT_SET'"));

        var sup = await _w.UploadAsync("owner", "suppliers", Templates.Sample("suppliers").Replace("9850012345", "9850099999", StringComparison.Ordinal));
        Assert.Equal(new ImportCounts(0, 1, 2, 0), sup.Counts);
        await _w.CommitAsync("owner", sup.BatchId);
        Assert.Equal("9850099999", (string?)await _w.ScalarAsync("SELECT phone FROM mnt.vendor WHERE name = N'Om Electricals (fictional)'"));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM mnt.vendor"));

        // levels change; a different unit cost is only a warning (goods receipts keep the average cost)
        var stock = await _w.UploadAsync("owner", "stock-items", Templates.Sample("stock-items").Replace("450.00,40,60", "480.00,50,60", StringComparison.Ordinal));
        Assert.Equal(new ImportCounts(0, 1, 3, 0), stock.Counts);
        Assert.Contains(stock.Warnings, w => w.Column == "unitCost");
        await _w.CommitAsync("owner", stock.BatchId);
        Assert.Equal(50, await _w.CountAsync("SELECT min_qty FROM stk.item WHERE code = 'LIN-BS'"));
        Assert.Equal(45000, await _w.CountAsync("SELECT cost_paise FROM stk.item WHERE code = 'LIN-BS'"));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'STOCK_ITEM_SET'"));
        var renamed = await _w.UploadAsync("owner", "stock-items", Templates.Sample("stock-items").Replace("Bed sheet (double)", "Bed sheet (king)", StringComparison.Ordinal));
        Assert.False(renamed.CommitAllowed);
        Assert.Contains(renamed.Errors, e => e is { Row: 2, Column: "code" });
    }

    [Fact]
    public async Task Rejected_rows_block_the_commit_dry_run_keeps_nothing_and_only_the_owner_commits()
    {
        await _w.ImportSampleAsync("room-types");
        var bad = await _w.UploadAsync("owner", "companies", "name,gstin,contact,phone,email,city,creditLimit,negotiatedRates\nGood Co,,,,,,1000,\nBad Co,27XX,,,,,,\n");
        Assert.Equal(1, bad.ErrorCount);
        Assert.Equal(new ImportRowError(3, "gstin", "Enter the GSTIN in the 15-character format, e.g. 27ZZFIC0009F1Z5."), Assert.Single(bad.Errors));
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("owner", bad.BatchId))).Code);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM grp.company_account"));

        foreach (var m in NewMasters)
        {
            var up = await _w.UploadAsync("techadmin", m, Templates.Sample(m));
            Assert.True(up.CommitAllowed, m);
            var dry = await _w.CommitAsync("techadmin", up.BatchId, dryRun: true);
            Assert.True(dry.DryRun);
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("techadmin", up.BatchId))).Code);
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.UploadAsync("manager", m, Templates.Sample(m)))).Code);
        }

        Assert.Equal(0, await _w.CountAsync("SELECT (SELECT COUNT(*) FROM hr.department) + (SELECT COUNT(*) FROM grp.company_account) + (SELECT COUNT(*) FROM mnt.vendor) + (SELECT COUNT(*) FROM stk.item)"));
    }
}
