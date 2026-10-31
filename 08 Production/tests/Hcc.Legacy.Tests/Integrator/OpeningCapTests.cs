using System.Globalization;
using System.Text;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Legacy.Tests.Import;

namespace Hcc.Legacy.Tests.Integrator;

/// <summary>
/// [LG-INT] coordinator ruling LG-R8: the 500-line cap of the ACC-1 opening entry applies to hand entry only. The legacy importer writes
/// the cut-over as a draft (LG-R7) that may hold more lines (756 customer openings in the real run); the Owner approves it whole.
/// Fictional data only.
/// </summary>
[Trait("Category", "Sql")]
public sealed class OpeningCapTests : IAsyncLifetime
{
    private LegacyImportWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await LegacyImportWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    [Fact]
    public async Task Hand_entry_is_capped_but_an_imported_draft_over_500_lines_is_read_and_approved_whole()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(5.5)));
        var store = new SqlOpeningStore(_w.Platform.Connections);
        var policy = new SqlAccessPolicy(_w.Platform.Connections, clock, []);
        var svc = new OpeningBalanceService(_w.Platform.Commands, store, new SqlAccountingStore(_w.Platform.Connections), policy, clock);
        var reads = new OpeningBalanceQueries(store, new SqlAccountingStore(_w.Platform.Connections), policy);
        var owner = new Caller(_w.PropertyId, _w.Users["owner"]);

        // hand entry: 501 lines are refused
        var tooMany = Enumerable.Range(0, 501).Select(i => new OpeningLineRequest($"1120/UD-HAND{i:D4}", 100, 0)).ToList();
        var ex = await Assert.ThrowsAsync<HccException>(() => svc.SaveAsync(owner, "k-hand", new SaveOpeningBalancesRequest(new DateOnly(2026, 4, 1), tooMany, null), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
        Assert.Contains("at most 500 lines", ex.Message, StringComparison.Ordinal);

        // the importer's cut-over draft, then 300 + 300 balanced party lines as a large real run would add (same store rows, written as the importer does)
        _ = (await _w.RunAsync(_w.Request(false, null, "cutover"))).Value;
        var next = Convert.ToInt32(await _w.ScalarAsync($"SELECT MAX(line_no) FROM acc.opening_line WHERE property_id = {_w.PropertyId}"), CultureInfo.InvariantCulture);
        var sql = new StringBuilder();
        for (var i = 0; i < 300; i++)
        {
            sql.Append(CultureInfo.InvariantCulture, $"INSERT acc.opening_line(property_id, line_no, account, debit_paise, credit_paise) VALUES ({_w.PropertyId}, {next + 1 + 2 * i}, '1120/UD-DEMOD{i:D4}', 100, 0);");
            sql.Append(CultureInfo.InvariantCulture, $"INSERT acc.opening_line(property_id, line_no, account, debit_paise, credit_paise) VALUES ({_w.PropertyId}, {next + 2 + 2 * i}, '1120/UD-DEMOC{i:D4}', 0, 100);");
        }

        await _w.ExecAsync(sql.ToString());
        var lines = await _w.CountAsync($"SELECT COUNT(*) FROM acc.opening_line WHERE property_id = {_w.PropertyId}");
        Assert.True(lines > OpeningBalanceRules.MaxLines);

        var view = (await reads.GetAsync(owner, Ct)).Entry!;
        Assert.Equal(lines, view.Lines.Count);
        var approved = (await svc.ApproveAsync(owner, "k-approve", new ApproveOpeningBalancesRequest(view.Version), Ct)).Value;
        Assert.Equal("approved", approved.Status);
        Assert.Equal("approved", await _w.ScalarAsync($"SELECT status FROM acc.opening_entry WHERE property_id = {_w.PropertyId}"));
        Assert.Equal(lines, await _w.CountAsync($"SELECT COUNT(*) FROM acc.opening_line WHERE property_id = {_w.PropertyId}"));
    }
}
