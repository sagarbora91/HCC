using System.Globalization;
using Hcc.Contracts.Customers;
using Hcc.Customers.Tests.Harness;
using Hcc.Domain.Customers;
using Microsoft.Data.SqlClient;

namespace Hcc.Customers.Tests.Sql;

/// <summary>
/// CU-NVARCHAR (wave CU, migrations 4930-4932): customer keys built from a Devanagari name are stored as NVARCHAR, so two different
/// customers never share tags, notes, aliases or pending merges (they used to collapse to question marks in VARCHAR(80)).
/// </summary>
[Trait("Category", "Sql")]
public class CustomersUnicodeKeyTests : IAsyncLifetime
{
    private const string Ram = "राम पाटील", Sita = "सीता जोशी", Geeta = "गीता देशमुख";

    private CrmWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await CrmWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Key() => Guid.NewGuid().ToString("N");

    private Task AddBooking(string no, string name) => _w.ExecAsync(
        $"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) VALUES ({_w.PropertyId}, '{no}', N'{name}', '2026-10-20', '2026-10-21', 'Confirmed');", Ct);

    private Task<string> Token(string rawKey) => _w.Queries.TokenAsync(_w.PropertyId, rawKey, Ct);

    private Task<CustomerProfile> Profile(string rawKey) => Token(rawKey).ContinueWith(t => _w.Queries.ProfileAsync(_w.PropertyId, _w.Users["owner"], t.Result, Ct), Ct).Unwrap();

    private static string KeyOfName(string name) => CustomerRules.KeyOf(null, name);

    private async Task Seed()
    {
        await AddBooking("HCC-D1", Ram);
        await AddBooking("HCC-D2", Sita);
        await AddBooking("HCC-D3", Geeta);
    }

    [Fact]
    public async Task Different_devanagari_names_have_different_keys_and_do_not_share_tags_or_notes()
    {
        await Seed();
        var kRam = KeyOfName(Ram);
        var kSita = KeyOfName(Sita);
        Assert.NotEqual(kRam, kSita);
        Assert.DoesNotContain('?', kRam);

        await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(await Token(kRam), "vip"), Ct);
        await _w.Commands.AddNoteAsync(_w.Ctx("reception", Key()), new AddCustomerNoteRequest(await Token(kRam), "Prefers ground floor"), Ct);
        await _w.Commands.SetPreferencesAsync(_w.Ctx("reception", Key()), new SetCustomerPreferencesRequest(await Token(kRam), "Quiet room"), Ct);

        var ram = await Profile(kRam);
        var sita = await Profile(kSita);
        Assert.Equal(["vip"], ram.Tags);
        Assert.Single(ram.Notes);
        Assert.Equal("Quiet room", ram.Preferences);
        Assert.Empty(sita.Tags);
        Assert.Empty(sita.Notes);
        Assert.NotEqual("Quiet room", sita.Preferences);

        // the stored key is the real Devanagari text, one row per customer
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_tag WHERE customer_key = N'{kRam}'", Ct));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_tag WHERE customer_key = N'{kSita}'", Ct));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_tag WHERE customer_key LIKE '%?%'", Ct));
        // a tag on the second customer is a second row (as VARCHAR both keys read 'n:????? ?????' and the primary key refused it)
        await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(await Token(kSita), "vip"), Ct);
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_tag WHERE tag = N'vip'", Ct));
        Assert.Equal(["vip"], (await Profile(kSita)).Tags);
        Assert.Empty((await Profile(KeyOfName(Geeta))).Tags);
    }

    [Fact]
    public async Task Different_devanagari_names_keep_separate_pending_merges_and_aliases()
    {
        await Seed();
        var vik = CrmWorld.VikramPhone;
        var m1 = (await _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(await Token(KeyOfName(Ram)), await Token(vik), "same guest as Vikram"), Ct)).Value;
        // a different source name is a different pending proposal (UX_crm_merge_pending is per source key)
        var m2 = (await _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(await Token(KeyOfName(Sita)), await Token(vik), "also Vikram"), Ct)).Value;
        Assert.NotEqual(m1.MergeId, m2.MergeId);
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_merge WHERE status = 'Pending'", Ct));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_merge WHERE source_key = N'{KeyOfName(Ram)}'", Ct));

        // approving the first alias leaves the second customer alone
        var done = (await _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m1.MergeId, true, m1.Version), Ct)).Value;
        Assert.Equal("Approved", done.Status);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_alias WHERE alias_key = N'{KeyOfName(Ram)}'", Ct));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_alias WHERE alias_key = N'{KeyOfName(Sita)}'", Ct));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_alias WHERE alias_key LIKE '%?%'", Ct));
        Assert.Equal("Pending", (await _w.Queries.MergesAsync(_w.PropertyId, _w.Users["owner"], "Pending", Ct)).Merges.Single().Status);

        // a second pending proposal for the same Devanagari source is still refused
        var sitaTok = await Token(KeyOfName(Sita));
        var vikTok = await Token(vik);
        await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(sitaTok, vikTok, "again"), Ct));
    }

    [Fact]
    public async Task Existing_ascii_and_phone_keys_still_resolve()
    {
        await Seed();
        var asha = await Profile(CrmWorld.Asha);
        Assert.Equal(await Token(CrmWorld.Asha), asha.Key);
        await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(await Token(CrmWorld.Asha), "regular"), Ct);
        Assert.Equal(["regular"], (await Profile(CrmWorld.Asha)).Tags);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_tag WHERE customer_key = '{CrmWorld.Asha}'", Ct));
        // the name-only ASCII key
        await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(await Token(CrmWorld.VikramName), "corp"), Ct);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_tag WHERE customer_key = N'{CrmWorld.VikramName}'", Ct));
    }

    private static readonly string[] KeyColumns =
    [
        "crm.customer_merge.source_key", "crm.customer_merge.target_key", "crm.customer_alias.alias_key", "crm.customer_alias.target_key",
        "crm.customer_tag.customer_key", "crm.customer_note.customer_key", "crm.customer_pref.customer_key",
        "lgc.guest_map.customer_key", "lgc.stay.customer_key", "lgc.bill.customer_key", "lgc.receipt.customer_key",
    ];

    private async Task AssertSchema()
    {
        foreach (var c in KeyColumns)
        {
            var p = c.Split('.');
            Assert.Equal("nvarchar", (string)(await _w.ScalarAsync($"SELECT t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('{p[0]}.{p[1]}') AND c.name = '{p[2]}'", Ct))!);
            Assert.Equal(160, Convert.ToInt32(await _w.ScalarAsync($"SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('{p[0]}.{p[1]}') AND name = '{p[2]}'", Ct), CultureInfo.InvariantCulture));
        }

        foreach (var (table, index) in new[]
        {
            ("crm.customer_merge", "UX_crm_merge_pending"), ("crm.customer_alias", "IX_crm_alias_target"), ("crm.customer_alias", "PK_crm_alias"),
            ("crm.customer_tag", "PK_crm_tag"), ("crm.customer_note", "IX_crm_note_customer"), ("crm.customer_pref", "PK_crm_pref"),
            ("lgc.guest_map", "IX_lgc_guest_map_key"), ("lgc.stay", "IX_lgc_stay_customer"), ("lgc.bill", "IX_lgc_bill_customer"), ("lgc.receipt", "IX_lgc_receipt_customer"),
        })
            Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM sys.indexes WHERE name = '{index}' AND object_id = OBJECT_ID('{table}')", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_crm_merge_pending' AND is_unique = 1 AND has_filter = 1", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_crm_merge_distinct' AND parent_object_id = OBJECT_ID('crm.customer_merge')", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_crm_alias_distinct' AND parent_object_id = OBJECT_ID('crm.customer_alias')", Ct));
        // the primary keys cover the same columns as before
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.name = 'PK_crm_tag'", Ct));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.name = 'PK_crm_alias'", Ct));
    }

    [Fact]
    public async Task After_migration_every_key_column_is_nvarchar_and_indexes_and_constraints_exist()
    {
        await AssertSchema();
        // the legacy view still works after the column change
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM lgc.v_customer_stay", Ct));
        // guest credit keys are phone digits or a guest id (CustomerRules.CreditKey), never a name: left VARCHAR(40) on purpose
        Assert.Equal("varchar", (string)(await _w.ScalarAsync("SELECT t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('gst.guest_credit_account') AND c.name = 'customer_key'", Ct))!);
    }

    private static string MigrationsDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "database", "migrations"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new InvalidOperationException("database/migrations not found"), "database", "migrations");
    }

    private async Task RunMigrationFile(string prefix)
    {
        var file = Directory.GetFiles(MigrationsDir(), prefix + "_*.sql").Single();
        var batches = System.Text.RegularExpressions.Regex.Split(await File.ReadAllTextAsync(file, Ct), @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var b in batches.Where(b => !string.IsNullOrWhiteSpace(b)))
            await _w.ExecAsync(b, Ct);
    }

    /// <summary>Puts the pre-4930 definitions back (VARCHAR(80), 2300 / 4600 names) so the real migration can be run over rows.</summary>
    private const string Downgrade = """
        DROP INDEX UX_crm_merge_pending ON crm.customer_merge;
        ALTER TABLE crm.customer_merge DROP CONSTRAINT CK_crm_merge_distinct;
        ALTER TABLE crm.customer_merge ALTER COLUMN source_key VARCHAR(80) NOT NULL;
        ALTER TABLE crm.customer_merge ALTER COLUMN target_key VARCHAR(80) NOT NULL;
        ALTER TABLE crm.customer_merge ADD CONSTRAINT CK_crm_merge_distinct CHECK (source_key <> target_key);
        CREATE UNIQUE INDEX UX_crm_merge_pending ON crm.customer_merge(property_id, source_key) WHERE status = 'Pending';
        DROP INDEX IX_crm_alias_target ON crm.customer_alias;
        ALTER TABLE crm.customer_alias DROP CONSTRAINT CK_crm_alias_distinct;
        ALTER TABLE crm.customer_alias DROP CONSTRAINT PK_crm_alias;
        ALTER TABLE crm.customer_alias ALTER COLUMN alias_key VARCHAR(80) NOT NULL;
        ALTER TABLE crm.customer_alias ALTER COLUMN target_key VARCHAR(80) NOT NULL;
        ALTER TABLE crm.customer_alias ADD CONSTRAINT PK_crm_alias PRIMARY KEY (property_id, alias_key);
        ALTER TABLE crm.customer_alias ADD CONSTRAINT CK_crm_alias_distinct CHECK (alias_key <> target_key);
        CREATE INDEX IX_crm_alias_target ON crm.customer_alias(property_id, target_key);
        ALTER TABLE crm.customer_tag DROP CONSTRAINT PK_crm_tag;
        ALTER TABLE crm.customer_tag ALTER COLUMN customer_key VARCHAR(80) NOT NULL;
        ALTER TABLE crm.customer_tag ADD CONSTRAINT PK_crm_tag PRIMARY KEY (property_id, customer_key, tag);
        DROP INDEX IX_crm_note_customer ON crm.customer_note;
        ALTER TABLE crm.customer_note ALTER COLUMN customer_key VARCHAR(80) NOT NULL;
        CREATE INDEX IX_crm_note_customer ON crm.customer_note(property_id, customer_key);
        ALTER TABLE crm.customer_pref DROP CONSTRAINT PK_crm_pref;
        ALTER TABLE crm.customer_pref ALTER COLUMN customer_key VARCHAR(80) NOT NULL;
        ALTER TABLE crm.customer_pref ADD CONSTRAINT PK_crm_pref PRIMARY KEY (property_id, customer_key);
        DROP INDEX IX_lgc_guest_map_key ON lgc.guest_map;
        ALTER TABLE lgc.guest_map ALTER COLUMN customer_key VARCHAR(80) NOT NULL;
        CREATE INDEX IX_lgc_guest_map_key ON lgc.guest_map(property_id, customer_key);
        DROP INDEX IX_lgc_stay_customer ON lgc.stay;
        ALTER TABLE lgc.stay ALTER COLUMN customer_key VARCHAR(80) NOT NULL;
        CREATE INDEX IX_lgc_stay_customer ON lgc.stay(property_id, customer_key, arrival_date DESC);
        DROP INDEX IX_lgc_bill_customer ON lgc.bill;
        ALTER TABLE lgc.bill ALTER COLUMN customer_key VARCHAR(80) NULL;
        CREATE INDEX IX_lgc_bill_customer ON lgc.bill(property_id, customer_key, bill_date DESC);
        DROP INDEX IX_lgc_receipt_customer ON lgc.receipt;
        ALTER TABLE lgc.receipt ALTER COLUMN customer_key VARCHAR(80) NULL;
        CREATE INDEX IX_lgc_receipt_customer ON lgc.receipt(property_id, customer_key, received_date DESC);
        """;

    [Fact]
    public async Task Migrations_convert_a_varchar_database_with_rows_and_can_run_again()
    {
        await _w.ExecAsync(Downgrade, Ct);
        Assert.Equal("varchar", (string)(await _w.ScalarAsync("SELECT t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('crm.customer_tag') AND c.name = 'customer_key'", Ct))!);

        // rows written the old way (ASCII keys only: that is all a VARCHAR column can hold)
        var p = _w.PropertyId;
        await _w.ExecAsync($"""
            INSERT crm.customer_tag(property_id, customer_key, tag, added_by, added_utc) VALUES ({p}, '9820011111', N'vip', 1, SYSUTCDATETIME()), ({p}, 'n:vikram shah', N'corp', 1, SYSUTCDATETIME());
            INSERT crm.customer_note(property_id, customer_key, body, author_id, created_utc) VALUES ({p}, '9820011111', N'note', 1, SYSUTCDATETIME());
            INSERT crm.customer_pref(property_id, customer_key, preferences, updated_by, updated_utc) VALUES ({p}, '9820011111', N'quiet', 1, SYSUTCDATETIME());
            INSERT crm.customer_merge(property_id, source_key, target_key, source_name, target_name, reason, status, proposed_by, proposed_utc)
              VALUES ({p}, 'n:vikram shah', '9811122233', N'Vikram Shah', N'Vikram Shah', N'same', 'Pending', 1, SYSUTCDATETIME());
            """, Ct);

        foreach (var n in new[] { "4930", "4931", "4932" }) await RunMigrationFile(n);
        await AssertSchema();
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_tag", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_note WHERE customer_key = '9820011111'", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_pref WHERE customer_key = '9820011111'", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_merge WHERE status = 'Pending' AND source_key = N'n:vikram shah'", Ct));

        // second run: nothing changes, nothing fails
        foreach (var n in new[] { "4930", "4931", "4932" }) await RunMigrationFile(n);
        await AssertSchema();
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_tag", Ct));

        // and a Devanagari key now fits where the old column would have turned it into question marks
        var k = KeyOfName(Ram);
        await _w.ExecAsync($"INSERT crm.customer_tag(property_id, customer_key, tag, added_by, added_utc) VALUES ({p}, N'{k}', N'vip', 1, SYSUTCDATETIME());", Ct);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_tag WHERE customer_key = N'{k}'", Ct));
    }
}
