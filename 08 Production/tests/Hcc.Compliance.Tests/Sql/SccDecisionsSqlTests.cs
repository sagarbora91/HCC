using Hcc.Compliance.Tests.Harness;
using Hcc.Contracts.Compliance;
using Hcc.Domain.Errors;

namespace Hcc.Compliance.Tests.Sql;

/// <summary>
/// SCC decisions wave (27 Oct 2026) on SQL, on the prototype's BASE compliance (business date 24 Oct 2026): DEC-125 (the compliance FY follows the
/// business date on 1 April; a pick holds until the next 1 April; migration 4180) and DEC-133 (FSSAI and lodging licence renewal cards from the
/// firm profile's expiry dates; migration 4181). Figures equal tests/stream-compliance-dec.mjs.
/// </summary>
[Trait("Category", "Sql")]
public class SccDecisionsSqlTests : IAsyncLifetime
{
    private ComplianceWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await ComplianceWorld.BaseAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

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

    private Task<ComplianceStateView> State() => _w.Queries.GetStateAsync(_w.PropertyId, Users.Owner, Ct);

    private Task BusinessDate(string d) => _w.Db.ExecAsync($"UPDATE inv.property SET business_date = '{d}' WHERE property_id = {_w.PropertyId};", Ct);

    private async Task<int> FyOn(string d)
    {
        await BusinessDate(d);
        return (await State()).FyStart;
    }

    [Fact]
    public async Task FY_follows_the_business_date_on_1_April_and_a_pick_holds_until_the_next_1_April()
    {
        var s = await State();
        Assert.Equal((2026, (int?)null), (s.FyStart, s.FyChosenIn));
        await _w.Commands.SetFyAsync(_w.Ctx(Users.Owner, "y1"), new SetFyRequest(2026), Ct);
        Assert.Equal(2026, Convert.ToInt32(await _w.Db.ScalarAsync($"SELECT fy_chosen_in FROM cmp.setting WHERE property_id = {_w.PropertyId};", Ct), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal((2026, (int?)2026), ((await State()).FyStart, (await State()).FyChosenIn));
        Assert.Equal(2026, await FyOn("2027-03-31"));
        Assert.Equal(2027, await FyOn("2027-04-01"));
        Assert.Null((await State()).FyChosenIn);
        await BusinessDate("2027-04-02");
        await _w.Commands.SetFyAsync(_w.Ctx(Users.Accountant, "y2"), new SetFyRequest(2026), Ct);
        Assert.Equal((2026, (int?)2027), ((await State()).FyStart, (await State()).FyChosenIn));
        foreach (var d in new[] { "2027-12-31", "2028-03-31" }) Assert.Equal(2026, await FyOn(d));
        Assert.Equal(2028, await FyOn("2028-04-01"));
        Assert.Equal(2032, await FyOn("2034-06-01"));
    }

    [Fact]
    public async Task Every_read_uses_the_effective_FY_month_dashboard_Action_Center_badge_due_this_week_CSV_print_payable_ZIP_CA_pack()
    {
        await BusinessDate("2027-04-01");
        var y = 2027;
        Assert.Equal(y, (await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, null, Ct)).FyStart);
        Assert.Equal(y, (await _w.Queries.GetDashboardAsync(_w.PropertyId, Users.Owner, null, "Q1", Ct)).FyStart);
        var ac = await _w.Queries.GetActionCenterAsync(_w.PropertyId, Users.Owner, null, Ct);
        Assert.Equal(y, ac.FyStart);
        Assert.Equal((await _w.Queries.GetActionCenterAsync(_w.PropertyId, Users.Owner, y, Ct)).Rows.Select(r => r.ItemId), ac.Rows.Select(r => r.ItemId));
        Assert.All(ac.Rows, r => Assert.True(string.CompareOrdinal(r.DueDate, "2027-04-01") >= 0, r.ItemId));
        Assert.Equal(ac.Rows.Count, (await _w.Queries.GetBadgeAsync(_w.PropertyId, Users.Accountant, Ct)).Count);
        Assert.Equal(y, (await _w.Queries.GetDueThisWeekAsync(_w.PropertyId, Users.Owner, Ct)).FyStart);
        Assert.Equal("Compliance_HCC_Demo_Residency_FY_2027_28.csv", (await _w.Queries.GetRegisterCsvAsync(_w.PropertyId, Users.Owner, null, Ct)).FileName);
        Assert.Equal("FY 2027–28", (await _w.Queries.GetPrintAsync(_w.PropertyId, Users.Owner, null, "April", Ct)).Fy);
        Assert.Equal((await _w.Queries.GetPayableAsync(_w.PropertyId, Users.Owner, y, Ct)).Months.Keys, (await _w.Queries.GetPayableAsync(_w.PropertyId, Users.Owner, null, Ct)).Months.Keys);
        Assert.EndsWith("_2027_28.zip", (await _w.Queries.GetEvidenceZipAsync(_w.PropertyId, Users.Owner, null, Ct)).FileName, StringComparison.Ordinal);
        await _w.Commands.GenerateCaPackAsync(_w.Ctx(Users.Accountant, "p1"), new GenerateCaPackRequest(), Ct);
        Assert.Contains("(FY 2027–28)", (await _w.Queries.GetCaPacksAsync(_w.PropertyId, Users.Owner, Ct))[0].Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_4180_counts_an_earlier_pick_as_made_in_the_current_business_date_FY_without_a_pick_the_FY_follows_the_date()
    {
        // A pre-DEC-125 row: the pick (audited COMPLIANCE_FY) without fy_chosen_in.
        await _w.Commands.SetFyAsync(_w.Ctx(Users.Owner, "m1"), new SetFyRequest(2028), Ct);
        await _w.Db.ExecAsync($"UPDATE cmp.setting SET fy_chosen_in = NULL WHERE property_id = {_w.PropertyId};", Ct);
        Assert.Equal(2026, (await State()).FyStart);
        var stamp = ComplianceWorld.MigrationText("4180_compliance_fy_follows_business_date.sql").Split("\nGO", StringSplitOptions.None)[1];
        await _w.Db.ExecAsync(stamp, Ct);
        Assert.Equal((2028, (int?)2026), ((await State()).FyStart, (await State()).FyChosenIn));
        Assert.Equal(2028, await FyOn("2027-03-31"));
        Assert.Equal(2027, await FyOn("2027-04-01"));
        await using var empty = await ComplianceWorld.EmptyAsync(Ct);
        await empty.Commands.SaveSettingsAsync(empty.Ctx(Users.Owner, "e1"), new SaveSettingsRequest(null, null, 9, "AAAAAAAAAAA="), Ct);
        await empty.Db.ExecAsync(stamp, Ct);
        Assert.Null(await empty.Db.ScalarAsync($"SELECT fy_chosen_in FROM cmp.setting WHERE property_id = {empty.PropertyId} AND fy_chosen_in IS NOT NULL;", Ct));
    }

    private async Task<string> Profile(string key, string? fssaiNo, string? fssaiExpiry, string? lodgingNo, string? lodgingExpiry)
    {
        var p = (await State()).Profile!;
        var r = await _w.Commands.SaveProfileAsync(_w.Ctx(Users.Owner, key), new SaveProfileRequest(p.Name, p.Pan, p.Gstin, p.Entity, p.GstType, p.HasEmployees, p.Qrmp, p.Notes, p.Version,
            fssaiNo, fssaiExpiry, lodgingNo, lodgingExpiry), Ct);
        return r.Value.Version;
    }

    [Fact]
    public async Task Licence_cards_from_the_firm_profile_in_the_month_Action_Center_badge_CSV_print_done_NA_evidence_archive_as_every_card()
    {
        await Profile("l1", " DEMO-FSSAI-0002 ", "2026-11-15", null, "2027-12-31");
        var p = (await State()).Profile!;
        Assert.Equal(("DEMO-FSSAI-0002", "2026-11-15", (string?)null, "2027-12-31"), (p.FssaiNo, p.FssaiExpiry, p.LodgingNo, p.LodgingExpiry));
        Assert.EndsWith("· FSSAI licence expires 2026-11-15 · lodging licence expires 2027-12-31",
            await _w.Db.StringAsync("SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'COMPLIANCE_PROFILE' ORDER BY audit_id DESC;", Ct), StringComparison.Ordinal);
        var nov = await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "November", Ct);
        var c = nov.Cards.Single(x => x.ItemId == "lic_fssai");
        Assert.Equal(("15 Nov", "2026-11-15", "High", 22, "2026-11-10", "Monitor", "Owner / General Manager"), (c.Due, c.DueDate, c.Risk, c.Days!.Value, c.InternalDate, c.Escalation, c.Responsible));
        Assert.Equal("FSSAI licence renewal (FoSCoS portal) · Licence no. DEMO-FSSAI-0002", c.Form);
        Assert.Contains("lic_fssai:22", (await _w.Queries.GetActionCenterAsync(_w.PropertyId, Users.Owner, null, Ct)).Rows.Select(r => r.ItemId + ":" + r.Days.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(12, (await _w.Queries.GetBadgeAsync(_w.PropertyId, Users.Accountant, Ct)).Count);
        var csv = await _w.Queries.GetRegisterCsvAsync(_w.PropertyId, Users.Owner, null, Ct);
        Assert.Equal(109, csv.Rows);
        Assert.Contains("HCC Demo Residency,FY 2026–27,November,15 Nov,FSSAI Licence – Renewal,FSSAI licence renewal (FoSCoS portal) · Licence no. DEMO-FSSAI-0002,Labour Law,On licence expiry,Owner / General Manager,High,Pending,,22",
            csv.Csv.Split("\r\n"));
        Assert.Contains((await _w.Queries.GetPrintAsync(_w.PropertyId, Users.Owner, null, "November", Ct)).Rows, r => r.Name == "FSSAI Licence – Renewal" && r.Status == "Pending");
        Assert.Contains((await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, 2027, "December", Ct)).Cards, x => x.ItemId == "lic_lodging" && x.DueDate == "2027-12-31");
        // Done in the FY of the expiry only; evidence; archive by key hides it; restore.
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Accountant, "d0"), new MarkDoneRequest(2027, "lic_fssai"), Ct)));
        await _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "e1"), new AddEvidenceRequest(2026, "lic_fssai", "ack.pdf", "application/pdf", "JVBERg=="), Ct);
        var done = await _w.Commands.MarkDoneAsync(_w.Ctx(Users.Accountant, "d1"), new MarkDoneRequest(2026, "lic_fssai", "2026-11-01", null, "FOSCOS-ACK-DEMO"), Ct);
        Assert.Equal(("2026-11-15", 0), (done.Value.DueDate, done.Value.DaysLate));
        var after = (await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "November", Ct)).Cards.Single(x => x.ItemId == "lic_fssai");
        Assert.Equal((true, 1, "✓ Filed"), (after.Done, after.Files, after.DaysTag));
        Assert.True((await _w.Commands.ToggleArchiveAsync(_w.Ctx(Users.Owner, "a1"), new ToggleArchiveRequest("FSSAI Licence"), Ct)).Value.Archived);
        Assert.DoesNotContain((await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "November", Ct)).Cards, x => x.ItemId == "lic_fssai");
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.ToggleArchiveAsync(_w.Ctx(Users.Accountant, "a2"), new ToggleArchiveRequest("FSSAI Licence"), Ct)));
        Assert.False((await _w.Commands.ToggleArchiveAsync(_w.Ctx(Users.Owner, "a3"), new ToggleArchiveRequest("FSSAI Licence"), Ct)).Value.Archived);
        await _w.Commands.ReopenAsync(_w.Ctx(Users.Owner, "r1"), new ReopenItemRequest(2026, "lic_fssai", "Wrong acknowledgement"), Ct);
        await _w.Commands.MarkNotApplicableAsync(_w.Ctx(Users.Accountant, "n1"), new MarkNotApplicableRequest(2026, "lic_fssai"), Ct);
        Assert.True((await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "November", Ct)).Cards.Single(x => x.ItemId == "lic_fssai").Na);
    }

    [Fact]
    public async Task Licence_fields_optional_Owner_only_valid_date_60_characters_null_keeps_empty_clears()
    {
        Assert.Equal("VALIDATION", await Code(() => Profile("v1", null, "2026-02-30", null, null)));
        Assert.Equal("VALIDATION", await Code(() => Profile("v2", null, null, new string('X', 61), null)));
        var p = (await State()).Profile!;
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.SaveProfileAsync(_w.Ctx(Users.Accountant, "v3"),
            new SaveProfileRequest(p.Name, p.Pan, p.Gstin, p.Entity, p.GstType, p.HasEmployees, p.Qrmp, p.Notes, p.Version, null, "2026-11-15"), Ct)));
        await Profile("v4", "DEMO-FSSAI-0003", "2026-11-15", "DEMO-LODGING-0003", "2027-12-31");
        await Profile("v5", null, null, null, null);
        var kept = (await State()).Profile!;
        Assert.Equal(("DEMO-FSSAI-0003", "2026-11-15", "DEMO-LODGING-0003", "2027-12-31"), (kept.FssaiNo, kept.FssaiExpiry, kept.LodgingNo, kept.LodgingExpiry));
        await Profile("v6", "", "", null, null);
        var cleared = (await State()).Profile!;
        Assert.Equal(((string?)null, (string?)null, "DEMO-LODGING-0003"), (cleared.FssaiNo, cleared.FssaiExpiry, cleared.LodgingNo));
        Assert.DoesNotContain((await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "November", Ct)).Cards, x => x.ItemId == "lic_fssai");
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Owner, "v7"), new MarkDoneRequest(2026, "lic_fssai"), Ct)));
    }
}
