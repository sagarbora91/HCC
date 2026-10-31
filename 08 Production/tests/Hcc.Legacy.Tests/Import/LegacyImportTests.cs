using Hcc.Application.Legacy;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Legacy;

namespace Hcc.Legacy.Tests.Import;

/// <summary>[LG-IMPORT] pure rules: ID-column refusal, folder checks, totals.json reader, tolerance, guest matching never on the phone alone.</summary>
public sealed class LegacyRulesTests
{
    [Theory]
    [InlineData("idNumber", true)]
    [InlineData("IDNumber", true)]
    [InlineData("id_number", true)]
    [InlineData("aadhaar", true)]
    [InlineData("Aadhar No", true)]
    [InlineData("pan", true)]
    [InlineData("passport_no", true)]
    [InlineData("phone", false)]
    [InlineData("company", false)]
    [InlineData("phone_key", false)]
    public void Id_columns_are_recognised(string column, bool expected) => Assert.Equal(expected, LegacyRules.IsIdColumn(column));

    [Theory]
    [InlineData(@"\\server\share\run")]
    [InlineData("//server/share/run")]
    [InlineData(@"relative\run")]
    [InlineData("")]
    public void Unc_and_relative_folders_are_refused(string folder) => Assert.NotNull(LegacyRules.FolderProblem(folder));

    [Fact]
    public void Absolute_local_folder_is_accepted() => Assert.Null(LegacyRules.FolderProblem(@"E:\LG-EXTRACT\20261105-2130"));

    [Fact]
    public void Totals_reads_afterDedup_of_the_extractor_layout()
    {
        var t = LegacyRules.ParseTotals("""{"extractorVersion":"1.0","fy":{"2025-26":{"beforeDedup":{"billsCount":9},"afterDedup":{"billsCount":2,"netPaise":5,"staysDuplicate":1}}}}""", out var e);
        Assert.Null(e);
        Assert.Equal("1.0", t!.ExtractorVersion);
        Assert.Equal(2, t.PerFy["2025-26"].Figures["billsCount"]);
        Assert.Equal(1, t.PerFy["2025-26"].Figures[LegacyRules.DuplicateStaysFigure]);
        Assert.Null(t.PerFy["2025-26"].Figures["fbPaise"]);
    }

    [Fact]
    public void Money_tolerance_is_one_paise_per_row_counts_exact()
    {
        var exp = new LegacyExpected("2025-26", LegacyRules.FigureNames.ToDictionary(n => n, n => (long?)(n == "billsCount" ? 2 : 100)));
        var actual = new LegacyFigures(2, 102, 100, 100, 100, 100, 100, 100);
        Assert.Empty(LegacyRules.Differences(exp, actual));
        Assert.Contains(LegacyRules.Differences(exp, actual with { NetPaise = 103 }), d => d.Figure == "netPaise");
        Assert.Contains(LegacyRules.Differences(exp, actual with { BillsCount = 3 }), d => d.Figure == "billsCount");
        Assert.Equal(8, LegacyRules.Differences(null, actual).Count);
    }

    [Fact]
    public void Match_needs_phone_and_name_never_phone_alone()
    {
        LgcGuestMapRow Row(long id, string name) => new(id, null, "9876500001", "9876500001", true, name, name.ToLowerInvariant(), false, "2026-27", name, "9876500001", true);
        var candidates = new[] { new LegacyGuestCandidate(1, "Ravi Demo", "9876500001", null), new LegacyGuestCandidate(2, "Rita Demo", "9876500001", null) };
        var m = LegacyImportService.Match([Row(10, "Rita Demo"), Row(11, "Unknown Person")], candidates);
        Assert.Equal(2, m.GuestOf[10]);
        Assert.False(m.GuestOf.ContainsKey(11));
        Assert.Equal([11L], m.UnmatchedWinners);
    }

    [Fact]
    public void Idempotency_key_depends_on_every_input()
    {
        var sha = new byte[32];
        var a = LegacyRules.IdempotencyKey(@"E:\r", ["guests"], true, sha);
        Assert.NotEqual(a, LegacyRules.IdempotencyKey(@"E:\r", ["guests"], false, sha));
        Assert.NotEqual(a, LegacyRules.IdempotencyKey(@"E:\r", ["history"], true, sha));
        Assert.NotEqual(a, LegacyRules.IdempotencyKey(@"E:\r", ["guests"], true, [1, .. new byte[31]]));
        Assert.StartsWith("lgc-", a, StringComparison.Ordinal);
    }
}

/// <summary>[LG-IMPORT] the importer against SQL Server on a disposable database with the fictional run folder.</summary>
public sealed class LegacyImportSqlTests : IAsyncLifetime
{
    private LegacyImportWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await LegacyImportWorld.CreateAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private long P => _w.PropertyId;

    [Fact]
    public async Task Dry_run_reconciles_and_leaves_no_rows()
    {
        var r = (await _w.RunAsync(_w.Request(true))).Value;
        Assert.True(r.DryRun);
        Assert.False(r.Committed);
        Assert.All(r.History!.PerFy, y => Assert.True(y.Reconciliation.Match, y.Fy + ": " + string.Join(",", y.Reconciliation.Differences.Select(d => d.Figure))));
        Assert.Equal(2, r.Guests!.MatchedExisting);
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.stay WHERE property_id = {P}"));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.guest_map WHERE property_id = {P}"));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM gst.guest WHERE property_id = {P} AND legacy_guest_id IS NOT NULL"));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM gst.guest_id_secret"));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM acc.opening_entry WHERE property_id = {P}"));
        Assert.True(File.Exists(_w.SecretPath), "a dry run never shreds the secret file");
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.import_run WHERE property_id = {P} AND status = 'DryRun' AND dry_run = 1 AND run_id = {r.RunId}"));
    }

    [Fact]
    public async Task Commit_loads_history_guests_secrets_and_cutover_then_shreds_the_secret_file()
    {
        var res = await _w.RunAsync(_w.Request(false));
        var r = res.Value;
        Assert.True(r.Committed);
        Assert.All(r.History!.PerFy, y => Assert.True(y.Reconciliation.Match));
        var fy25 = r.History.PerFy.Single(y => y.Fy == "2025-26");
        Assert.Equal((3, 3, 2, 4, 1), (fy25.Stays, fy25.Segments, fy25.Bills, fy25.Receipts, fy25.DuplicatesDropped));
        Assert.Equal(165750, fy25.Reconciliation.NetPaise);
        Assert.Empty(r.History.Errors);

        var g = r.Guests!;
        Assert.Equal((4, 0, 2, 1, 1), (g.CsvRows, g.Created, g.MatchedExisting, g.SkippedInvalidPhone, g.Merged));
        Assert.Equal((1, 2, 1, 1, 1), (g.SecretsSealed, g.DobSet, g.PhotosCopied, g.IdProofsCopied, g.TagsAdded));
        var ravi = _w.Guests["Ravi Demo"];
        Assert.Equal(101L, await _w.ScalarAsync($"SELECT legacy_guest_id FROM gst.guest WHERE guest_id = {ravi}"));
        Assert.Equal("7777", await _w.ScalarAsync($"SELECT id_last4 FROM gst.guest WHERE guest_id = {ravi}"));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM gst.guest_id_secret WHERE guest_id = {ravi}"));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM gst.guest WHERE guest_id = {_w.Guests["Rita Demo"]} AND legacy_guest_id IS NOT NULL"));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM gst.guest_id_secret WHERE guest_id = {_w.Guests["Sana Sample"]}"));
        Assert.Equal(2, await _w.CountAsync($"SELECT COUNT(*) FROM doc.id_capture WHERE property_id = {P} AND doc_kind = 'legacy' AND status = 'Confirmed'"));
        Assert.Equal(2, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.guest_map WHERE property_id = {P} AND guest_id = {ravi}"));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_tag WHERE property_id = {P} AND tag = 'OTA:AGODA'"));

        Assert.False(File.Exists(_w.SecretPath));
        Assert.False(Directory.Exists(Path.Combine(_w.Folder, "secret")), "LG-R10: the whole secret folder (images too) is shredded");
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'legacy.secret_shredded'"));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'legacy.import' AND target = 'lgc-run:{r.RunId}'"));
        Assert.Equal("Done", await _w.ScalarAsync($"SELECT status FROM lgc.import_run WHERE run_id = {r.RunId}"));

        // Cut-over: a DRAFT of the ACC-1 opening entry with the party lines (+ 1400 difference), never approved by the importer.
        Assert.Equal("draft", await _w.ScalarAsync($"SELECT status FROM acc.opening_entry WHERE property_id = {P}"));
        Assert.Equal(20000L, await _w.ScalarAsync($"SELECT debit_paise FROM acc.opening_line WHERE property_id = {P} AND account = '1120/UD-WALK-IN-GUEST'"));
        Assert.Equal(15000L, await _w.ScalarAsync($"SELECT credit_paise FROM acc.opening_line WHERE property_id = {P} AND account = '1120/UD-9876500002'"));
        Assert.Equal(30000L, await _w.ScalarAsync($"SELECT credit_paise FROM acc.opening_line WHERE property_id = {P} AND account = '2000'"));
        Assert.Equal((1, 1, 1, 1), (r.Cutover!.ReceivablesRows, r.Cutover.CustomerOpeningRows, r.Cutover.AdvancesRows, r.Cutover.InhouseRows));
    }

    [Fact]
    public async Task A_mismatching_fy_is_rolled_back_and_the_other_kept()
    {
        var totals = await File.ReadAllTextAsync(Path.Combine(_w.Folder, "totals.json"), TestContext.Current.CancellationToken);
        var wrong = totals.Replace("\"netPaise\": 105000, \"roomGstPaise\": 5000, \"fbPaise\": 0, \"receiptsCount\": 2, \"receiptsPaise\": 105000, \"refundsCount\": 0, \"refundsPaise\": 0, \"securityCount\"",
            "\"netPaise\": 999999, \"roomGstPaise\": 5000, \"fbPaise\": 0, \"receiptsCount\": 2, \"receiptsPaise\": 105000, \"refundsCount\": 0, \"refundsPaise\": 0, \"securityCount\"", StringComparison.Ordinal);
        Assert.NotEqual(totals, wrong);
        var r = (await _w.RunAsync(_w.Request(false, wrong, "history"))).Value;
        var bad = r.History!.PerFy.Single(y => y.Fy == "2026-27");
        Assert.False(bad.Reconciliation.Match);
        var d = Assert.Single(bad.Reconciliation.Differences);
        Assert.Equal(("netPaise", 999999L, 105000L), (d.Figure, d.Expected, d.Actual));
        Assert.True(r.History.PerFy.Single(y => y.Fy == "2025-26").Reconciliation.Match);
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.stay WHERE property_id = {P} AND legacy_fy = '2026-27'"));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.receipt WHERE property_id = {P} AND legacy_fy = '2026-27'"));
        Assert.Equal(3, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.stay WHERE property_id = {P} AND legacy_fy = '2025-26'"));
    }

    [Fact]
    public async Task Rerun_is_idempotent()
    {
        var first = await _w.RunAsync(_w.Request(false, null, "history"));
        var again = await _w.RunAsync(_w.Request(false, null, "history"));
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.RunId, again.Value.RunId);
        Assert.Equal(5, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.stay WHERE property_id = {P}"));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.import_run WHERE property_id = {P}"));
    }

    [Fact]
    public async Task A_csv_with_an_id_number_column_is_refused_before_anything_is_written()
    {
        await File.WriteAllTextAsync(Path.Combine(_w.Folder, "guests-extra.csv"), "name,phone,aadhaar\nTest Person,9876500009,x\n", TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.RunAsync(_w.Request(false)));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
        Assert.Contains("aadhaar", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM lgc.stay WHERE property_id = {P}"));
        Assert.True(File.Exists(_w.SecretPath));
    }

    [Fact]
    public async Task A_manager_is_refused()
    {
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.RunAsync(_w.Request(true), "manager"));
        Assert.Equal(HccErrorCode.FORBIDDEN, ex.Code);
        await Assert.ThrowsAsync<HccException>(() => _w.Service.RunsAsync(_w.PropertyId, _w.Users["manager"], 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task History_is_refused_after_the_first_day_close_but_cutover_alone_runs()
    {
        await _w.CloseFirstDayAsync();
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.RunAsync(_w.Request(false, null, "history")));
        Assert.Equal(HccErrorCode.DAY_CLOSED, ex.Code);
        var r = (await _w.RunAsync(_w.Request(false, null, "cutover"))).Value;
        Assert.True(r.Committed);
        var runs = await _w.Service.RunsAsync(_w.PropertyId, _w.Users["owner"], 10, TestContext.Current.CancellationToken);
        Assert.Contains(runs.Runs, x => x.Status == "Failed");
        Assert.Contains(runs.Runs, x => x.Status == "Done" && x.Result!.Cutover is not null);
    }

    [Fact]
    public async Task Over_long_soft_text_is_truncated_and_counted_key_text_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var bills = Path.Combine(_w.Folder, "lgc_bill.csv");
        var text = await File.ReadAllTextAsync(bills, ct);
        var longRoom = new string('R', 103);
        await File.WriteAllTextAsync(bills, text.Replace("2025-06-03T05:30:00Z,101,", "2025-06-03T05:30:00Z," + longRoom + ",", StringComparison.Ordinal), ct);
        var r = (await _w.RunAsync(_w.Request(false, null, "history"))).Value;
        Assert.Contains("lgc_bill.csv: 1 room_no value truncated", r.Warnings);
        Assert.All(r.History!.PerFy, y => Assert.True(y.Reconciliation.Match));
        Assert.Equal(new string('R', 20), await _w.ScalarAsync($"SELECT room_no FROM lgc.bill WHERE property_id = {P} AND legacy_fy = '2025-26' AND legacy_bill_no = 1"));

        var stays = Path.Combine(_w.Folder, "lgc_stay.csv");
        var st = await File.ReadAllTextAsync(stays, ct);
        await File.WriteAllTextAsync(stays, st.Replace("2025-26,2,103,9876500002,", "2025-26,2,103," + new string('9', 96) + ",", StringComparison.Ordinal), ct);
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.RunAsync(_w.Request(true, null, "history")));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
        Assert.Contains("lgc_stay.csv row 3: 'customer_key' is 96 characters", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_database_length_error_becomes_a_validation_naming_the_table()
    {
        var store = new Hcc.Infrastructure.SqlServer.Legacy.SqlLegacyStore(_w.Platform.Connections);
        var row = new LgcGuestMapRow(1, null, new string('9', 96), null, false, "X", "x", false, "2026-27");
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.Platform.Commands.RunAsync(_w.Ctx("owner"), new { t = 1 }, async (tx, c) =>
        {
            await store.ReplaceGuestMapAsync(tx, _w.PropertyId, [row], c);
            await tx.AuditAsync("t", "t", "t", c);
            return new { ok = true };
        }, TestContext.Current.CancellationToken));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
        Assert.Contains("lgc.guest_map (guest map)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unc_folder_is_refused()
    {
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.RunAsync(new Hcc.Contracts.Legacy.LegacyImportRequest(@"\\pc\share\run", ["history"], true)));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
    }
}
