using System.Data;
using Hcc.Api.Hosting;
using Hcc.Application.GuestIds;
using Hcc.Contracts.Stays;
using Hcc.GstIds.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Extensions.Configuration;

namespace Hcc.GstIds.Tests.GuestIds;

/// <summary>Runs <c>Hcc.Api.exe verify-guest-id</c> (A07-03 / A09-07) in-process against a test database, with keys loaded as the server loads them.</summary>
internal static class VerifyVerb
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Configuration with a connection string to ANOTHER database name, so the test proves <c>--database</c> is honoured.</summary>
    public static IConfiguration Config(GstIdsDb db, string? current, params (string Id, string Value)[] keys)
    {
        var c = new Dictionary<string, string?> { ["Hcc:Sql:ConnectionString"] = SqlConnectionFactory.LocalDevConnectionString(db.Server, "HccTest_not_this_one") };
        if (current is not null)
        {
            c["Hcc:GuestIdKeys:Current"] = current;
            c["Hcc:GuestIdKeys:AllowPlainKeys"] = "true";
        }

        foreach (var (id, value) in keys) c["Hcc:GuestIdKeys:Keys:" + id] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(c).Build();
    }

    public static string Plain(byte[] key) => "plain:" + Convert.ToBase64String(key);

    public static async Task<(int Exit, string Output)> RunAsync(GstIdsDb db, IConfiguration config)
    {
        await using var w = new StringWriter();
        var exit = await GuestIdCheckCommand.RunAsync([GuestIdCheckCommand.Verb, "--database", db.Name, "--server", db.Server], config, w, Ct);
        return (exit, w.ToString().Trim());
    }

    public static async Task<long> RegisterAsync(GstIdsDb db, string number, Hcc.Application.Stays.StaysCommands? stays = null)
    {
        var no = "V" + Guid.NewGuid().ToString("N")[..8];
        var rid = await db.LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, nightly_rate_paise)
            OUTPUT INSERTED.reservation_id VALUES (@p, @no, N'Fictional guest', '2026-10-24', '2026-10-26', 'Confirmed', 200000);
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@no", SqlDbType.VarChar, no));
        var ver = Convert.ToBase64String((byte[])(await db.ScalarAsync("SELECT row_ver FROM inv.reservation WHERE reservation_id = @r;", ("@r", SqlDbType.BigInt, rid)))!);
        var guest = new GuestDraftDto("Restore Check Guest (fictional)", "+91 98200 05555", "rc@example.test", "5 Demo Lane, Latur (fictional)", "Indian", "Passport (demo)", null, IdNumber: number);
        return (await (stays ?? db.StaysCmd).RegisterGuestAsync(db.Ctx("REC"), new RegisterGuestRequest(rid, ver, guest), Ct)).Value.GuestId;
    }

    /// <summary>No ID number, no last 4 and no other run of 4 digits may appear in what the verb prints.</summary>
    public static void NoLeak(string output, string number)
    {
        Assert.DoesNotContain(number, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(number[^4..], output, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{4}", output);
        Assert.Single(output.Split('\n'));
    }
}

/// <summary>A07-03 / A09-07: the restore drill's guest ID check over SQL Server. Every row in this class is sealed under k1 (the fixture's key).</summary>
[Trait("Category", "Sql")]
public class GuestIdRestoreCheckTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    [Fact]
    public async Task Sealed_with_the_installed_key_opens_and_prints_only_ok()
    {
        await VerifyVerb.RegisterAsync(db, "Z1357913");
        var (exit, output) = await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, "k1", ("k1", VerifyVerb.Plain(GstIdsDb.Key1))));
        Assert.Equal(0, exit);
        Assert.Equal("Guest ID check: OK (1 ID opened)", output);
        VerifyVerb.NoLeak(output, "Z1357913");
    }

    [Fact]
    public async Task Wrong_key_fails_clearly_without_leaking_the_number()
    {
        await VerifyVerb.RegisterAsync(db, "Z2468024");
        var (exit, output) = await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, "k1", ("k1", VerifyVerb.Plain(GstIdsDb.Key2))));
        Assert.Equal(1, exit);
        Assert.StartsWith("Guest ID check: FAILED - ", output, StringComparison.Ordinal);
        Assert.Contains("could not be decrypted with key k1 (wrong key or altered data)", output, StringComparison.Ordinal);
        Assert.Contains("-ImportOfflineCopy", output, StringComparison.Ordinal);
        VerifyVerb.NoLeak(output, "Z2468024");
    }

    [Fact]
    public async Task Key_id_not_installed_fails_naming_the_missing_key()
    {
        await VerifyVerb.RegisterAsync(db, "Z3692581");
        var (exit, output) = await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, "k9", ("k9", VerifyVerb.Plain(GstIdsDb.Key2))));
        Assert.Equal(1, exit);
        Assert.Contains("sealed with key 'k1', which is not installed on this server", output, StringComparison.Ordinal);
        Assert.Contains("New-HccGuestIdKey.ps1 -ImportOfflineCopy", output, StringComparison.Ordinal);
        VerifyVerb.NoLeak(output, "Z3692581");
    }

    [Fact]
    public async Task No_key_configured_while_ids_are_stored_fails()
    {
        await VerifyVerb.RegisterAsync(db, "Z4826048");
        var (exit, output) = await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, null));
        Assert.Equal(1, exit);
        Assert.Contains("no guest ID key is configured on this server", output, StringComparison.Ordinal);
        VerifyVerb.NoLeak(output, "Z4826048");
    }

    [Fact]
    public async Task A_key_entry_that_cannot_be_opened_on_this_pc_fails_with_the_import_hint()
    {
        await VerifyVerb.RegisterAsync(db, "Z5937159");
        // A DPAPI blob from "another PC" (random bytes): DPAPI refuses it, as on a replacement PC with the old settings file.
        var foreign = "dpapi-machine:" + Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(180));
        var (exit, output) = await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, "k1", ("k1", foreign)));
        Assert.Equal(1, exit);
        Assert.Contains("the guest ID key configured on this PC cannot be loaded", output, StringComparison.Ordinal);
        Assert.Contains("-ImportOfflineCopy", output, StringComparison.Ordinal);
        VerifyVerb.NoLeak(output, "Z5937159");
    }

    [Fact]
    public async Task A_database_that_cannot_be_read_fails_without_a_stack_trace()
    {
        await using var w = new StringWriter();
        var exit = await GuestIdCheckCommand.RunAsync([GuestIdCheckCommand.Verb, "--database", "HccTest_missing_" + Guid.NewGuid().ToString("N")[..8], "--server", db.Server],
            VerifyVerb.Config(db, "k1", ("k1", VerifyVerb.Plain(GstIdsDb.Key1))), w, VerifyVerb.Ct);
        Assert.Equal(1, exit);
        Assert.StartsWith("Guest ID check: FAILED - the guest ID numbers in database 'HccTest_missing_", w.ToString(), StringComparison.Ordinal);
        Assert.Single(w.ToString().Trim().Split('\n'));
    }
}

/// <summary>Own database: starts with no sealed ID at all, then a key rotation (one test, so no other test can add rows first).</summary>
[Trait("Category", "Sql")]
public class GuestIdRestoreCheckEmptyAndRotationTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    [Fact]
    public async Task Empty_database_reports_no_guest_ids_then_after_rotation_one_id_per_key_is_opened()
    {
        var k1 = ("k1", VerifyVerb.Plain(GstIdsDb.Key1));
        var k2 = ("k2", VerifyVerb.Plain(GstIdsDb.Key2));
        // No sealed ID number: reported, not a failure - with or without a key on this PC.
        var empty = await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, "k1", k1));
        Assert.Equal((0, "Guest ID check: no guest IDs to check (the database holds no sealed ID numbers)."), empty);
        Assert.Equal(empty, await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, null)));

        // Rotation: an older number under k1, a newer one under k2 (current). Both keys installed -> one ID opened per key.
        await VerifyVerb.RegisterAsync(db, "Z6048260");
        var rotated = db.WithRing(new StaticGuestIdKeyRing("k2", new Dictionary<string, byte[]> { ["k1"] = GstIdsDb.Key1, ["k2"] = GstIdsDb.Key2 }));
        await VerifyVerb.RegisterAsync(db, "Z7159371", rotated.Stays);
        Assert.Equal(2L, await db.LongAsync("SELECT COUNT(DISTINCT key_id) FROM gst.guest_id_secret;"));
        var both = await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, "k2", k1, k2));
        Assert.Equal((0, "Guest ID check: OK (2 IDs opened, one per key: k1, k2)"), both);

        // The old key was not imported on the replacement PC: the drill fails although the newest number opens.
        var (exit, output) = await VerifyVerb.RunAsync(db, VerifyVerb.Config(db, "k2", k2));
        Assert.Equal(1, exit);
        Assert.Contains("sealed with key 'k1', which is not installed on this server", output, StringComparison.Ordinal);
        VerifyVerb.NoLeak(output, "Z6048260");
        VerifyVerb.NoLeak(output, "Z7159371");
    }
}

/// <summary>Argument handling of the verb (no database).</summary>
public class GuestIdCheckVerbArgumentTests
{
    private static async Task<(int Exit, string Output)> Run(string[] args, Dictionary<string, string?> config)
    {
        await using var w = new StringWriter();
        var exit = await GuestIdCheckCommand.RunAsync(args, new ConfigurationBuilder().AddInMemoryCollection(config).Build(), w, TestContext.Current.CancellationToken);
        return (exit, w.ToString().Trim());
    }

    [Fact]
    public async Task Verb_is_recognised_and_bad_arguments_are_refused_before_any_connection()
    {
        Assert.True(GuestIdCheckCommand.IsVerb(["verify-guest-id"]));
        Assert.True(GuestIdCheckCommand.IsVerb(["VERIFY-GUEST-ID", "--database", "x"]));
        Assert.False(GuestIdCheckCommand.IsVerb(["migrate"]));
        Assert.False(GuestIdCheckCommand.IsVerb([]));
        var cfg = new Dictionary<string, string?> { ["Hcc:Sql:ConnectionString"] = "Data Source=.\\SQLEXPRESS;Initial Catalog=Hcc;Integrated Security=True" };
        var bad = await Run(["verify-guest-id", "--database", "Hcc]; DROP DATABASE Hcc;--"], cfg);
        Assert.Equal(2, bad.Exit);
        Assert.Contains("--database must be a plain database name", bad.Output, StringComparison.Ordinal);
        var unknown = await Run(["verify-guest-id", "--reveal"], cfg);
        Assert.Equal(2, unknown.Exit);
        Assert.Contains("unknown argument '--reveal'", unknown.Output, StringComparison.Ordinal);
        var noConn = await Run(["verify-guest-id", "--environment", "Production", "--contentRoot", "C:\\x"], new());
        Assert.Equal(1, noConn.Exit);
        Assert.Equal("Guest ID check: FAILED - Hcc:Sql:ConnectionString is not configured (run it in the server folder, where appsettings.Production.json is).", noConn.Output);
    }
}
