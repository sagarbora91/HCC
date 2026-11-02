using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-3 integration: the Tally file log <c>acc.tally_file</c> (migration 4266, <see cref="SqlTallyStore"/>) on SQL — every masters / vouchers file is
/// recorded with its kind, range, size and SHA-256; a second masters export of the same FY carries the RE-EXPORT note; the company in the file is
/// Master configuration <c>brand.legalName</c> when set. Own database; fictional data.
/// </summary>
[Trait("Category", "Sql")]
public sealed class TallyFileSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class LegalName(string? name) : ISettingsReader
    {
        public Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default)
            => Task.FromResult(key == "brand.legalName" && name is not null ? ConfigValue.Of(name) : ConfigValue.Of(string.Empty));

        public Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => GetAsync(propertyId, key, on, ct);
    }

    private TallyQueries Queries(string? legalName)
    {
        var tally = new SqlTallyStore(db.Platform.Connections);
        return new TallyQueries(db.Store, new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]),
            null, tally, null, null, tally, db.Platform.Commands, db.Clock, new LegalName(legalName), new SqlAgeingAllocationSource(db.Platform.Connections));
    }

    [Fact]
    public async Task Masters_and_vouchers_files_are_logged_with_their_sha256_and_a_repeat_is_flagged_as_re_export()
    {
        var q = Queries("Demo Legal Name Pvt Ltd (fictional)");
        var first = await q.MastersAsync(db.As("ACC"), "2026", Ct);
        Assert.Contains("<SVCURRENTCOMPANY>Demo Legal Name Pvt Ltd (fictional)</SVCURRENTCOMPANY>", first.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Notes, n => n.StartsWith("RE-EXPORT", StringComparison.Ordinal));
        var second = await q.MastersAsync(db.As("OWN"), "2026-27", Ct);
        Assert.Contains(second.Notes, n => n.StartsWith("RE-EXPORT", StringComparison.Ordinal));

        var row = await db.ScalarAsync("SELECT COUNT(*) FROM acc.tally_file WHERE property_id = @p AND kind = 'masters' AND sha256 = @s AND byte_count = @b;",
            ("@p", SqlDbType.Int, db.PropertyId), ("@s", SqlDbType.Char, first.Sha256), ("@b", SqlDbType.Int, first.Bytes));
        Assert.Equal(1, Convert.ToInt32(row));
        // At least our two exports: the sibling test in this class also logs a masters file in the shared fixture database, in either order.
        Assert.True(Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM acc.tally_file WHERE property_id = @p AND kind = 'masters';", ("@p", SqlDbType.Int, db.PropertyId))) >= 2);

        var v = await q.VouchersAsync(db.As("ACC"), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), Ct);
        Assert.Equal(1, Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM acc.tally_file WHERE property_id = @p AND kind = 'vouchers' AND sha256 = @s;",
            ("@p", SqlDbType.Int, db.PropertyId), ("@s", SqlDbType.VarChar, v.Sha256))));
    }

    [Fact]
    public async Task Without_a_legal_name_the_company_is_the_property_name()
    {
        var m = await Queries(null).MastersAsync(db.As("OWN"), "2025", Ct);
        var name = await db.Store.PropertyNameAsync(db.PropertyId, Ct);
        Assert.Contains($"<SVCURRENTCOMPANY>{System.Security.SecurityElement.Escape(name)}</SVCURRENTCOMPANY>", m.Xml, StringComparison.Ordinal);
    }
}
