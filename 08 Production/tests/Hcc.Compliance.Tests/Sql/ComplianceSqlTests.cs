using Hcc.Application.Compliance;
using Hcc.Compliance.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Compliance;
using Hcc.Domain.Errors;

namespace Hcc.Compliance.Tests.Sql;

/// <summary>The prototype's [CMP] scenarios on SQL (BASE date 24 Oct 2026): 7 obligations, 37 rows; 1 overdue, 1 due soon, 1 upcoming, 32 filed, 2 not applicable.</summary>
[Trait("Category", "Sql")]
public class ComplianceSqlTests : IAsyncLifetime
{
    private ComplianceWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await ComplianceWorld.BaseAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private Task<CalendarView> Calendar(int user = Users.Owner, bool inactive = false) => _w.Queries.GetCalendarAsync(_w.PropertyId, user, inactive, Ct);

    private static async Task<string> Code(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (HccException e)
        {
            return e.Code.ToString();
        }

        return "NO_ERROR";
    }

    private async Task<string> Audit(string action) => (await _w.Db.StringAsync($"SELECT TOP 1 detail FROM plat.audit_event WHERE action = '{action}' ORDER BY 1 DESC;", Ct)) ?? string.Empty;

    [Fact]
    public async Task Fixture_has_37_rows_and_the_golden_counters()
    {
        var c = await Calendar();
        Assert.Equal(37, c.Items.Count);
        Assert.Equal(new CalendarCountsDto(1, 1, 1, 32, 2, 3), c.Counts);
        Assert.Equal("2026-10-24", c.AsOf);
        Assert.Equal(7, (await _w.Queries.GetObligationsAsync(_w.PropertyId, Users.Owner, Ct)).Count);
        Assert.Equal("Rao & Associates (fictional)", c.Settings.CaName);
        Assert.Equal("ca.desk@example.test", c.Settings.CaEmail);
        Assert.Equal(7, c.Settings.DueSoonDays);
        Assert.Equal("Overdue by 13 days", (await _w.ItemAsync("GSTR-1", "2026-09", Ct)).StatusText);
        Assert.Equal("Due in 7 days", (await _w.ItemAsync("PT", "2026-09", Ct)).StatusText);
        Assert.Equal("Due in 68 days", (await _w.ItemAsync("GSTR-9", "2025-26", Ct)).StatusText);
        var tds = await _w.ItemAsync("TDS", "2026-09", Ct);
        Assert.Equal("Filed", tds.Status);
        Assert.Equal("2026-10-07", tds.DueDate);
    }

    [Fact]
    public async Task Due_this_week_is_overdue_plus_the_window_and_the_badge_is_for_owner_and_accountant_only()
    {
        var due = await _w.Queries.GetDueThisWeekAsync(_w.PropertyId, Users.Owner, Ct);
        Assert.Equal(["GSTR-1:2026-09", "PT:2026-09"], due.Select(i => i.Kind + ":" + i.Period));
        Assert.Equal(2, (await _w.Queries.GetBadgeAsync(_w.PropertyId, Users.Owner, Ct)).Count);
        Assert.Equal(2, (await _w.Queries.GetBadgeAsync(_w.PropertyId, Users.Accountant, Ct)).Count);
        Assert.Equal(0, (await _w.Queries.GetBadgeAsync(_w.PropertyId, Users.Manager, Ct)).Count);
        Assert.Equal(0, (await _w.Queries.GetBadgeAsync(_w.PropertyId, Users.Reception, Ct)).Count);
    }

    [Fact]
    public async Task Access_owner_and_accountant_only_reopen_and_manage_are_owner_only()
    {
        var g1 = await _w.IdOfAsync("GSTR-1", Ct);
        foreach (var u in new[] { Users.Manager, Users.Cashier, Users.Reception, Users.TechAdmin })
        {
            Assert.Equal("FORBIDDEN", await Code(() => Calendar(u)));
            Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(u, "x" + u), new MarkFiledRequest(g1, "2026-09", "ARN-12345"), Ct)));
            Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.GenerateCaPackAsync(_w.Ctx(u, "p" + u), new GenerateCaPackRequest(), Ct)));
        }

        var tds = await _w.IdOfAsync("TDS", Ct);
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.ReopenAsync(_w.Ctx(Users.Accountant, "r"), new ReopenFilingRequest(tds, "2026-09", "Wrong reference"), Ct)));
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.SaveObligationAsync(_w.Ctx(Users.Accountant, "s"), new SaveObligationRequest(null, "PT", "Another PT", null, null, new DueRuleDto("month-end")), Ct)));
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.SeedDefaultsAsync(_w.Ctx(Users.Accountant, "d"), new SeedDefaultsRequest(), Ct)));
        Assert.Equal("NO_ERROR", await Code(() => Calendar(Users.Accountant)));
    }

    [Fact]
    public async Task Migration_grants_equal_DEC055()
    {
        Assert.Equal(3, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id IN ('PERM-CMP-001','PERM-CMP-002');", Ct));
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-CMP-001' AND role_code = 'ACC';", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id LIKE 'PERM-CMP-%' AND role_code NOT IN ('OWN','ACC');", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-CMP-002' AND role_code <> 'OWN';", Ct));
    }

    [Fact]
    public async Task Mark_filed_records_who_when_and_reference_late_is_audited_and_the_key_is_idempotent()
    {
        var g1 = await _w.IdOfAsync("GSTR-1", Ct);
        var req = new MarkFiledRequest(g1, "2026-09", "ARN-AA2610-0042");
        var first = await _w.Commands.MarkFiledAsync(_w.Ctx(Users.Accountant, "f1"), req, Ct);
        Assert.False(first.Replayed);
        Assert.Equal(13, first.Value.DaysLate);
        var item = await _w.ItemAsync("GSTR-1", "2026-09", Ct);
        Assert.Equal("Filed", item.Status);
        Assert.Equal(Users.Accountant, item.Filing!.By);
        Assert.Equal("ARN-AA2610-0042", item.Filing.Reference);
        Assert.StartsWith("2026-10-24T08:00:00", item.Filing.AtUtc, StringComparison.Ordinal);
        Assert.Contains("13 day(s) late", await Audit("COMPLIANCE_FILE"), StringComparison.Ordinal);
        var replay = await _w.Commands.MarkFiledAsync(_w.Ctx(Users.Accountant, "f1"), req, Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM cmp.filing WHERE obligation_id = {g1} AND period = '2026-09';", Ct));
        Assert.Equal(0, (await Calendar()).Counts.Overdue);
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ComplianceFiled';", Ct));
        Assert.Equal("IDEMPOTENCY_MISMATCH", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Accountant, "f1"), req with { Reference = "ARN-OTHER-1" }, Ct)));
    }

    [Fact]
    public async Task Mark_filed_validation_reference_unknown_period_and_already_filed()
    {
        var g1 = await _w.IdOfAsync("GSTR-1", Ct);
        var tds = await _w.IdOfAsync("TDS", Ct);
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Owner, "a"), new MarkFiledRequest(g1, "2026-09", "ab"), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Owner, "b"), new MarkFiledRequest(g1, "2026-09", "<script>x</script>"), Ct)));
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Owner, "c"), new MarkFiledRequest(g1, "2026-10", "ARN-1234"), Ct)));
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Owner, "d"), new MarkFiledRequest(99999, "2026-09", "ARN-1234"), Ct)));
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Owner, "e"), new MarkFiledRequest(tds, "2026-09", "ARN-1234"), Ct)));
        Assert.Equal(0, await _w.Db.CountAsync($"SELECT COUNT(*) FROM cmp.filing WHERE obligation_id = {g1} AND period = '2026-09';", Ct));
    }

    [Fact]
    public async Task Not_applicable_needs_a_reason_and_shows_with_it()
    {
        var pt = await _w.IdOfAsync("PT", Ct);
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.MarkNotApplicableAsync(_w.Ctx(Users.Accountant, "n0"), new MarkNotApplicableRequest(pt, "2026-09", "no"), Ct)));
        await _w.Commands.MarkNotApplicableAsync(_w.Ctx(Users.Accountant, "n1"), new MarkNotApplicableRequest(pt, "2026-09", "Exempt this month (illustrative)"), Ct);
        var item = await _w.ItemAsync("PT", "2026-09", Ct);
        Assert.Equal("Not applicable", item.Status);
        Assert.Equal("Exempt this month (illustrative)", item.Filing!.Reason);
        Assert.Equal(3, (await Calendar()).Counts.NotApplicable);
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.MarkNotApplicableAsync(_w.Ctx(Users.Accountant, "n2"), new MarkNotApplicableRequest(pt, "2026-09", "Second time here"), Ct)));
    }

    [Fact]
    public async Task Reopen_is_owner_only_with_a_reason_and_keeps_the_filing_record()
    {
        var tds = await _w.IdOfAsync("TDS", Ct);
        var g1 = await _w.IdOfAsync("GSTR-1", Ct);
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.ReopenAsync(_w.Ctx(Users.Owner, "r0"), new ReopenFilingRequest(tds, "2026-09", "x"), Ct)));
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.ReopenAsync(_w.Ctx(Users.Owner, "r1"), new ReopenFilingRequest(g1, "2026-09", "Not filed yet at all"), Ct)));
        await _w.Commands.ReopenAsync(_w.Ctx(Users.Owner, "r2"), new ReopenFilingRequest(tds, "2026-09", "Wrong reference"), Ct);
        Assert.Equal("Overdue", (await _w.ItemAsync("TDS", "2026-09", Ct)).Status);
        Assert.Contains("reopened", await Audit("COMPLIANCE_REOPEN"), StringComparison.Ordinal);
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM cmp.filing_reopened WHERE obligation_id = {tds} AND period = '2026-09' AND status = 'Filed' AND reference IS NOT NULL AND reopened_by = {Users.Owner} AND reopen_reason = N'Wrong reference';", Ct));
        await _w.Commands.MarkFiledAsync(_w.Ctx(Users.Accountant, "r3"), new MarkFiledRequest(tds, "2026-09", "TDS-NEW-0001"), Ct);
        Assert.Equal("Filed", (await _w.ItemAsync("TDS", "2026-09", Ct)).Status);
    }

    [Fact]
    public async Task Edited_due_rule_applies_from_its_date_only_and_the_version_guards_the_edit()
    {
        var epf = await _w.IdOfAsync("EPF", Ct);
        var o = await _w.ObligationAsync(epf, Ct);
        SaveObligationRequest Edit(DueRuleDto rule, string? eff = null, string? version = null) => new(epf, null, "EPF contribution", o.Form, "Accountant", rule, eff, null, version ?? o.Version);
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveObligationAsync(_w.Ctx(Users.Owner, "e0"), Edit(new DueRuleDto("day", 31)), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveObligationAsync(_w.Ctx(Users.Owner, "e1"), Edit(new DueRuleDto("day", 20), "2026-10-01"), Ct)));
        var r = await _w.Commands.SaveObligationAsync(_w.Ctx(Users.Owner, "e2"), Edit(new DueRuleDto("day", 20), "2026-11-01"), Ct);
        Assert.True(r.Value.RuleChanged);
        Assert.Equal(2, (await _w.ObligationAsync(epf, Ct)).Rules.Count);
        Assert.Equal("2026-10-15", (await _w.ItemAsync("EPF", "2026-09", Ct)).DueDate);
        Assert.Equal("STALE_VERSION", await Code(() => _w.Commands.SaveObligationAsync(_w.Ctx(Users.Owner, "e3"), Edit(new DueRuleDto("day", 22), "2026-12-01"), Ct)));
        var again = await _w.Commands.SaveObligationAsync(_w.Ctx(Users.Owner, "e4"), Edit(new DueRuleDto("day", 20), null, r.Value.Version), Ct);
        Assert.False(again.Value.RuleChanged);
    }

    [Fact]
    public async Task Add_obligation_tracks_from_the_last_finished_period_refuses_duplicates_and_toggle_keeps_history()
    {
        var add = await _w.Commands.SaveObligationAsync(_w.Ctx(Users.Owner, "a1"), new SaveObligationRequest(null, "PT", "PT — annex building (fictional)", "State PT portal", null, new DueRuleDto("month-end")), Ct);
        Assert.Equal("2026-09", add.Value.TrackedFrom);
        var rows = (await Calendar()).Items.Where(i => i.ObligationId == add.Value.ObligationId).ToList();
        Assert.Single(rows);
        Assert.Equal("Due soon", rows[0].Status);
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveObligationAsync(_w.Ctx(Users.Owner, "a2"), new SaveObligationRequest(null, "PT", "pt — annex building (fictional)", null, null, new DueRuleDto("month-end")), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveObligationAsync(_w.Ctx(Users.Owner, "a3"), new SaveObligationRequest(null, "XYZ", "Something", null, null, new DueRuleDto("month-end")), Ct)));

        var epf = await _w.IdOfAsync("EPF", Ct);
        var o = await _w.ObligationAsync(epf, Ct);
        Assert.Equal("STALE_VERSION", await Code(() => _w.Commands.ToggleObligationAsync(_w.Ctx(Users.Owner, "t0"), new ToggleObligationRequest(epf, add.Value.Version), Ct)));
        var off = await _w.Commands.ToggleObligationAsync(_w.Ctx(Users.Owner, "t1"), new ToggleObligationRequest(epf, o.Version), Ct);
        Assert.False(off.Value.Active);
        Assert.DoesNotContain((await Calendar()).Items, i => i.ObligationId == epf);
        Assert.Equal(6, await _w.Db.CountAsync($"SELECT COUNT(*) FROM cmp.filing WHERE obligation_id = {epf};", Ct));
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Owner, "t2"), new MarkFiledRequest(epf, "2026-09", "ARN-1234"), Ct)));
        Assert.Contains((await Calendar(inactive: true)).Items, i => i.ObligationId == epf);
    }

    [Fact]
    public async Task Seed_defaults_runs_once()
    {
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.SeedDefaultsAsync(_w.Ctx(Users.Owner, "s2"), new SeedDefaultsRequest(), Ct)));
    }

    [Fact]
    public async Task Ca_pack_is_simulated_saved_with_counts_idempotent_and_only_20_are_kept()
    {
        var p = await _w.Commands.GenerateCaPackAsync(_w.Ctx(Users.Accountant, "ca1"), new GenerateCaPackRequest(), Ct);
        Assert.Equal(1, p.Value.Overdue);
        Assert.Equal(3, p.Value.Open);
        var pack = Assert.Single(await _w.Queries.GetCaPacksAsync(_w.PropertyId, Users.Owner, Ct));
        Assert.Equal("ca.desk@example.test", pack.To);
        Assert.Equal([1, 1, 32, 2], [pack.Overdue, pack.Due, pack.Filed, pack.NotApplicable]);
        Assert.StartsWith("Position as on 2026-10-24: 32 filed, 3 open, 2 not applicable.", pack.Lines[0], StringComparison.Ordinal);
        Assert.Contains(pack.Lines, l => l.StartsWith("OPEN · GSTR-1 · outward supplies · Sep 2026 · due 2026-10-11 · Overdue by 13 days", StringComparison.Ordinal));
        Assert.Contains("simulated", await Audit("COMPLIANCE_CA_PACK"), StringComparison.Ordinal);
        Assert.True((await _w.Commands.GenerateCaPackAsync(_w.Ctx(Users.Accountant, "ca1"), new GenerateCaPackRequest(), Ct)).Replayed);
        for (var i = 0; i < 21; i++) await _w.Commands.GenerateCaPackAsync(_w.Ctx(Users.Owner, "cb" + i), new GenerateCaPackRequest(), Ct);
        Assert.Equal(20, (await _w.Queries.GetCaPacksAsync(_w.PropertyId, Users.Owner, Ct)).Count);
        Assert.Equal(20, await _w.Db.CountAsync("SELECT COUNT(*) FROM cmp.ca_pack;", Ct));
    }

    [Fact]
    public async Task Settings_validate_the_window_changes_what_is_due_and_default_save_works_without_a_prior_row()
    {
        var s = (await Calendar()).Settings;
        Assert.Equal("AAAAAAAAAAA=", s.Version);
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Owner, "g0"), new SaveSettingsRequest(null, "not-an-email", null, s.Version), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Owner, "g1"), new SaveSettingsRequest(null, null, 0, s.Version), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Owner, "g2"), new SaveSettingsRequest("ab", null, null, s.Version), Ct)));
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Accountant, "g3"), new SaveSettingsRequest(null, null, 30, s.Version), Ct)));
        var saved = await _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Owner, "g4"), new SaveSettingsRequest(null, null, 3, s.Version), Ct);
        Assert.Equal(3, saved.Value.DueSoonDays);
        Assert.Equal(["GSTR-1:2026-09"], (await _w.Queries.GetDueThisWeekAsync(_w.PropertyId, Users.Owner, Ct)).Select(i => i.Kind + ":" + i.Period));
        Assert.Equal("STALE_VERSION", await Code(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Owner, "g5"), new SaveSettingsRequest(null, null, 30, s.Version), Ct)));
        await _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Owner, "g6"), new SaveSettingsRequest("Mehta & Co (fictional)", "ca@example.test", 30, saved.Value.Version), Ct);
        Assert.Equal(2, (await _w.Queries.GetDueThisWeekAsync(_w.PropertyId, Users.Owner, Ct)).Count);
        var pack = await _w.Commands.GenerateCaPackAsync(_w.Ctx(Users.Owner, "g7"), new GenerateCaPackRequest(), Ct);
        Assert.Equal("Mehta & Co (fictional)", pack.Value.ToName);
    }

    [Fact]
    public async Task A_failed_command_leaves_no_audit_row()
    {
        var before = await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event;", Ct);
        var g1 = await _w.IdOfAsync("GSTR-1", Ct);
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Owner, "z1"), new MarkFiledRequest(g1, "2026-09", "x"), Ct)));
        Assert.Equal(before, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event;", Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN.ToString(), await Code(() => _w.Commands.MarkFiledAsync(_w.Ctx(Users.Manager, "z2"), new MarkFiledRequest(g1, "2026-09", "ARN-1234"), Ct)));
    }
}
