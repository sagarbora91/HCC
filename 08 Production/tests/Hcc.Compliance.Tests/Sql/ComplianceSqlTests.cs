using Hcc.Application.Compliance;
using Hcc.Compliance.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Compliance;
using Hcc.Domain.Compliance;
using Hcc.Domain.Errors;

namespace Hcc.Compliance.Tests.Sql;

/// <summary>
/// SCC tax module in full (DEC-116) on SQL, on the prototype's BASE compliance (business date 24 Oct 2026, FY 2026–27): the W05-C filings carried
/// onto SCC's cards by migration 4131, the SCC views and every command with HCC's safeguards. Figures equal tests/stream-compliance.mjs.
/// </summary>
[Trait("Category", "Sql")]
public class ComplianceSqlTests : IAsyncLifetime
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

    private async Task<string> Audit(string action) => (await _w.Db.StringAsync($"SELECT TOP 1 detail FROM plat.audit_event WHERE action = '{action}' ORDER BY audit_id DESC;", Ct)) ?? string.Empty;

    private Task<ComplianceStateView> State() => _w.Queries.GetStateAsync(_w.PropertyId, Users.Owner, Ct);

    private async Task<RecordDto?> Rec(string id, int fy = 2026) => (await State()).Records.SingleOrDefault(r => r.FyStart == fy && r.ItemId == id);

    [Fact]
    public async Task Migration_4131_carries_the_W05C_filings_onto_SCC_cards_and_sets_the_profile()
    {
        var s = await State();
        Assert.Equal(("HCC Demo Residency", "regular", false, true, 2026), (s.Profile!.Name, s.Profile.GstType, s.Profile.Qrmp, s.Profile.HasEmployees, s.FyStart));
        Assert.Equal(34, s.Records.Count(r => r.Legacy is not null));
        Assert.Equal(55, s.Records.Count(r => r.FyStart == 2026));
        var oc1 = s.Records.Single(r => r.ItemId == "oc1");
        Assert.True(oc1.Done);
        Assert.Equal("2026-10-06", oc1.FiledOn);
        Assert.StartsWith("TDS-202609-", oc1.EvidenceRef, StringComparison.Ordinal);
        var my6 = s.Records.Single(r => r.ItemId == "my6");
        Assert.Equal((true, false, "Staff count below the ESIC threshold until June (illustrative)"), (my6.Na, my6.Done, my6.Notes));
        foreach (var id in new[] { "oc4", "oc11", "dc5" }) Assert.Null(s.Records.SingleOrDefault(r => r.ItemId == id));
        Assert.Equal("Rao & Associates (fictional)", s.Settings.CaName);
    }

    [Fact]
    public async Task Migration_4131_turns_a_second_obligation_into_a_custom_one_archives_switched_off_kinds_and_keeps_reopened_filings()
    {
        await using var w = await ComplianceWorld.EmptyAsync(Ct);
        var epf = await w.LegacyObligationAsync("EPF", "EPF contribution", "Monthly", "day", 15, null, active: false, ct: Ct);
        var pt = await w.LegacyObligationAsync("PT", "PT", "Monthly", "month-end", null, null, ct: Ct);
        var annex = await w.LegacyObligationAsync("PT", "PT annex (fictional)", "Monthly", "day", 10, null, ct: Ct);
        var g9 = await w.LegacyObligationAsync("GSTR-9", "GSTR-9", "Annual", "fixed", 31, 12, ct: Ct);
        await w.LegacyFilingAsync(epf, "2026-05", "Filed", Users.Accountant, new DateOnly(2026, 6, 14), "EPF-1", null, Ct);
        await w.LegacyFilingAsync(pt, "2026-06", "Not applicable", Users.Accountant, new DateOnly(2026, 7, 30), null, "No staff in June", Ct);
        await w.LegacyFilingAsync(annex, "2026-06", "Filed", Users.Owner, new DateOnly(2026, 7, 9), "PTA-7", null, Ct);
        await w.LegacyFilingAsync(g9, "2025-26", "Filed", Users.Owner, new DateOnly(2026, 12, 30), "G9", null, Ct);
        await w.Db.ExecAsync($"INSERT cmp.filing_reopened(property_id, obligation_id, period, status, filed_by, filed_utc, reference, reason, reopened_by, reopened_utc, reopen_reason) VALUES ({w.PropertyId}, {pt}, '2026-05', 'Filed', 8, '2026-06-29T04:30:00', N'PT-5', NULL, 1, '2026-07-01T03:30:00', N'Wrong ref');", Ct);
        await w.RunLegacyMigrationAsync(Ct);
        var s = await w.Queries.GetStateAsync(w.PropertyId, Users.Owner, Ct);
        var jn8 = s.Records.Single(r => r.ItemId == "jn8");
        Assert.Equal((true, "EPF-1", "2026-06-14", "{0}:2026-05".Replace("{0}", epf.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)),
            (jn8.Done, jn8.EvidenceRef, jn8.FiledOn, jn8.Legacy));
        Assert.Equal((true, "No staff in June"), (s.Records.Single(r => r.ItemId == "jl12").Na, s.Records.Single(r => r.ItemId == "jl12").Notes));
        Assert.Equal(["EPF Contribution Deposit", "EPF Contribution"], s.Archived.OrderByDescending(k => k.Length));
        var custom = Assert.Single(s.Custom);
        Assert.Equal(("lab", 10, 12, "Monthly"), (custom.Cat, custom.DueDay, custom.Months.Count, custom.Freq));
        Assert.Equal("PTA-7", s.Records.Single(r => r.ItemId == custom.Id + "-Jul").EvidenceRef);
        Assert.Equal("G9", s.Records.Single(r => r.ItemId == "dc5" && r.FyStart == 2026).EvidenceRef);
        var reopened = Assert.Single(s.Reopened);
        Assert.Equal(("jn10", "done", "PT-5", "Wrong ref"), (reopened.ItemId, reopened.Part, reopened.EvidenceRef, reopened.Reason));
        Assert.Equal("regular", s.Profile!.GstType);
    }

    [Fact]
    public async Task Server_views_equal_the_prototype_October_page_Action_Center_dashboard_CSV_print()
    {
        var oct = await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Accountant, null, "October", Ct);
        Assert.Equal(new MonthStatsDto(8, 4, 4, 6, 2, 1, "4/4"), oct.Stats);
        Assert.True(oct.OverdueDot);
        Assert.Equal("⚠ 13d overdue", oct.Cards.Single(c => c.ItemId == "oc4").DaysTag);
        Assert.Equal("⚡ 7d left", oct.Cards.Single(c => c.ItemId == "oc11").DaysTag);
        Assert.Equal("✓ Filed", oct.Cards.Single(c => c.ItemId == "oc1").DaysTag);
        Assert.Equal("Only for QRMP-scheme firms — your firm files monthly (see the GSTR-1 / GSTR-3B Monthly card).", oct.Cards.Single(c => c.ItemId == "oc6").Advisory);
        Assert.Equal(1, oct.Cards.Single(c => c.ItemId == "oc1").Files);
        foreach (var m in SccTaxCatalogue.Months.Where(m => m != "October"))
            Assert.False((await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, 2026, m, Ct)).OverdueDot, m);
        var ac = await _w.Queries.GetActionCenterAsync(_w.PropertyId, Users.Owner, null, Ct);
        Assert.Equal(["oc4:-13", "oc2:7", "oc3:7", "oc11:7", "nv1:14", "nv3:18", "nv7:18", "nv2:22", "nv5:22", "nv6:22", "nv4:27"], ac.Rows.Select(r => r.ItemId + ":" + r.Days));
        Assert.Equal((1, 10), (ac.Overdue, ac.DueSoon));
        Assert.Equal(11, (await _w.Queries.GetBadgeAsync(_w.PropertyId, Users.Accountant, Ct)).Count);
        Assert.Equal(0, (await _w.Queries.GetBadgeAsync(_w.PropertyId, Users.Manager, Ct)).Count);
        Assert.Equal(["oc4", "oc2", "oc3", "oc11"], (await _w.Queries.GetDueThisWeekAsync(_w.PropertyId, Users.Owner, Ct)).Rows.Select(r => r.ItemId));
        var q3 = await _w.Queries.GetDashboardAsync(_w.PropertyId, Users.Owner, null, "Q3", Ct);
        Assert.Equal((26, 4, 22, 1, 19, 15), (q3.Total, q3.Done, q3.Pending, q3.Overdue, q3.HighRisk, q3.Score));
        Assert.Equal((9, 15, 2), (q3.CashHigh, q3.CashMedium, q3.CashLow));
        Assert.Equal(["oc2", "oc11", "nv1", "nv7", "nv2", "nv5", "nv6", "nv4"], q3.Focus.Select(f => f.ItemId));
        Assert.Equal((49, 47, 96), (q3.EvidenceDone, q3.EvidenceSatisfied, q3.EvidencePct));
        Assert.Equal(["jn4", "jl11"], q3.EvidenceMissing);
        var csv = await _w.Queries.GetRegisterCsvAsync(_w.PropertyId, Users.Owner, null, Ct);
        Assert.Equal(("Compliance_HCC_Demo_Residency_FY_2026_27.csv", 108), (csv.FileName, csv.Rows));
        var lines = csv.Csv.TrimStart('﻿').Split("\r\n");
        Assert.Equal("Firm,FY,Month,Due,Obligation,Form/Portal,Category,Frequency,Responsible,Risk,Status,Filed On,Days Left", lines[0]);
        Assert.StartsWith("HCC Demo Residency,FY 2026–27,April,30 Apr,TDS Deposit – March Deductions,", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(",Done,2026-04-29,'-177", lines[1], StringComparison.Ordinal);
        var print = await _w.Queries.GetPrintAsync(_w.PropertyId, Users.Owner, null, "October", Ct);
        Assert.Equal(("Compliance — October — HCC Demo Residency", 10, 4, 2, "10 obligations · 4 done · 4 pending · 2 N/A"), (print.Title, print.Obligations, print.Done, print.NotApplicable, print.Footer));
        Assert.Equal("FORBIDDEN", await Code(() => _w.Queries.GetStateAsync(_w.PropertyId, Users.Manager, Ct)));
    }

    [Fact]
    public async Task Mark_done_as_SCC_optional_fields_any_date_default_business_date_late_days_audited_idempotent_NA_read_only()
    {
        var first = await _w.Commands.MarkDoneAsync(_w.Ctx(Users.Accountant, "d1"), new MarkDoneRequest(2026, "oc4"), Ct);
        Assert.False(first.Replayed);
        Assert.Equal(("2026-10-24", "2026-10-11", 13), (first.Value.FiledOn, first.Value.DueDate, first.Value.DaysLate));
        var r = (await Rec("oc4"))!;
        Assert.Equal((true, "2026-10-24", "", "", "", Users.Accountant), (r.Done, r.FiledOn, r.Notes, r.EvidenceRef, r.ReviewerNote, r.DoneBy!.Value));
        Assert.Contains("filed on 2026-10-24 · 13 day(s) after the due date", await Audit("COMPLIANCE_DONE"), StringComparison.Ordinal);
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ComplianceFiled';", Ct));
        Assert.True((await _w.Commands.MarkDoneAsync(_w.Ctx(Users.Accountant, "d1"), new MarkDoneRequest(2026, "oc4"), Ct)).Replayed);
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Accountant, "d2"), new MarkDoneRequest(2026, "oc4"), Ct)));
        await _w.Commands.MarkDoneAsync(_w.Ctx(Users.Owner, "d3"), new MarkDoneRequest(2026, "nv1", "2026-09-01", "Ack 991", null, "Owner"), Ct);
        Assert.Equal(("2026-09-01", "Ack 991", "Owner"), ((await Rec("nv1"))!.FiledOn, (await Rec("nv1"))!.Notes, (await Rec("nv1"))!.ReviewerNote));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Owner, "d4"), new MarkDoneRequest(2026, "nv3", "2026-02-30"), Ct)));
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Owner, "d5"), new MarkDoneRequest(2026, "zz9"), Ct)));
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Owner, "d6"), new MarkDoneRequest(2026, "oc6"), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Owner, "d7"), new MarkDoneRequest(2033, "oc2"), Ct)));
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Manager, "d8"), new MarkDoneRequest(2026, "oc2"), Ct)));
        Assert.DoesNotContain((await _w.Queries.GetActionCenterAsync(_w.PropertyId, Users.Owner, null, Ct)).Rows, x => x.ItemId == "oc4");
        await using var empty = await ComplianceWorld.EmptyAsync(Ct);
        Assert.Equal("INVALID_TRANSITION", await Code(() => empty.Commands.MarkDoneAsync(empty.Ctx(Users.Owner, "e1"), new MarkDoneRequest(2026, "oc4"), Ct)));
        Assert.Empty((await empty.Queries.GetActionCenterAsync(empty.PropertyId, Users.Owner, null, Ct)).Rows);
    }

    [Fact]
    public async Task Not_applicable_has_no_reason_is_additive_over_done_and_reopen_is_owner_only_keeping_the_record()
    {
        await _w.Commands.MarkNotApplicableAsync(_w.Ctx(Users.Accountant, "n1"), new MarkNotApplicableRequest(2026, "oc11"), Ct);
        Assert.True((await Rec("oc11"))!.Na);
        Assert.Equal(3, (await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "October", Ct)).Stats.NotApplicable);
        await _w.Commands.MarkNotApplicableAsync(_w.Ctx(Users.Accountant, "n2"), new MarkNotApplicableRequest(2026, "oc1"), Ct);
        var oc1 = (await Rec("oc1"))!;
        Assert.Equal((true, true), (oc1.Na, oc1.Done));
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.MarkNotApplicableAsync(_w.Ctx(Users.Accountant, "n3"), new MarkNotApplicableRequest(2026, "oc1"), Ct)));
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.ReopenAsync(_w.Ctx(Users.Accountant, "r1"), new ReopenItemRequest(2026, "oc1", "Applies after all"), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.ReopenAsync(_w.Ctx(Users.Owner, "r2"), new ReopenItemRequest(2026, "oc1", "x"), Ct)));
        await _w.Commands.ReopenAsync(_w.Ctx(Users.Owner, "r3"), new ReopenItemRequest(2026, "oc1", "Applies after all"), Ct);
        oc1 = (await Rec("oc1"))!;
        Assert.Equal((false, true), (oc1.Na, oc1.Done));   // SCC un-NA keeps the done record underneath
        await _w.Commands.ReopenAsync(_w.Ctx(Users.Owner, "r4"), new ReopenItemRequest(2026, "oc1", "Wrong reference"), Ct);
        Assert.Null(await Rec("oc1"));
        Assert.Contains("reopened (was filed on 2026-10-06 · evidence TDS-202609-", await Audit("COMPLIANCE_REOPEN"), StringComparison.Ordinal);
        var kept = (await State()).Reopened;
        Assert.Equal(("oc1", "done", "Wrong reference"), (kept[0].ItemId, kept[0].Part, kept[0].Reason));
        Assert.StartsWith("TDS-202609-", kept[0].EvidenceRef, StringComparison.Ordinal);
        Assert.Equal(("oc1", "na"), (kept[1].ItemId, kept[1].Part));
        Assert.Equal("INVALID_TRANSITION", await Code(() => _w.Commands.ReopenAsync(_w.Ctx(Users.Owner, "r5"), new ReopenItemRequest(2026, "oc4", "Not filed yet"), Ct)));
    }

    [Fact]
    public async Task FY_pick_and_records_are_per_FY_DEC125_the_business_date_FY_after_1_April()
    {
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SetFyAsync(_w.Ctx(Users.Owner, "f0"), new SetFyRequest(2025), Ct)));
        await _w.Commands.SetFyAsync(_w.Ctx(Users.Accountant, "f1"), new SetFyRequest(2027), Ct);
        Assert.Equal(2027, (await State()).FyStart);
        Assert.Empty((await _w.Queries.GetActionCenterAsync(_w.PropertyId, Users.Owner, null, Ct)).Rows);
        await _w.Commands.MarkDoneAsync(_w.Ctx(Users.Owner, "f2"), new MarkDoneRequest(2027, "ap1"), Ct);
        Assert.True((await Rec("ap1", 2027))!.Done);
        Assert.True((await Rec("ap1", 2026))!.Done);
        await _w.Db.ExecAsync($"UPDATE inv.property SET business_date = '2027-05-02' WHERE property_id = {_w.PropertyId};", Ct);
        Assert.Equal(2027, (await State()).FyStart);
    }

    [Fact]
    public async Task Firm_profile_as_SCC_and_the_type_and_employees_decide_the_cards()
    {
        var v = (await State()).Profile!.Version;
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveProfileAsync(_w.Ctx(Users.Owner, "p0"), new SaveProfileRequest(" ", null, null, null, null, true, false, null, v), Ct)));
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.SaveProfileAsync(_w.Ctx(Users.Accountant, "p1"), new SaveProfileRequest("X", null, null, null, null, true, false, null, v), Ct)));
        var saved = await _w.Commands.SaveProfileAsync(_w.Ctx(Users.Owner, "p2"), new SaveProfileRequest("Hotel X", "abcde1234f", "27abc", "company", "composition", false, true, "n", v), Ct);
        var p = (await State()).Profile!;
        Assert.Equal(("ABCDE1234F", "27ABC", "composition", false, false, "company"), (p.Pan, p.Gstin, p.GstType, p.Qrmp, p.HasEmployees, p.Entity));
        Assert.Equal(["ap1", "ap_comp1", "ap4"], (await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "April", Ct)).Cards.Select(c => c.ItemId));
        Assert.Equal("STALE_VERSION", await Code(() => _w.Commands.SaveProfileAsync(_w.Ctx(Users.Owner, "p3"), new SaveProfileRequest("Hotel X", null, null, null, "both", true, true, null, v), Ct)));
        await _w.Commands.SaveProfileAsync(_w.Ctx(Users.Owner, "p4"), new SaveProfileRequest("Hotel X", null, null, null, "both", true, true, null, saved.Value.Version), Ct);
        Assert.Equal((true, 8), ((await State()).Profile!.Qrmp, (await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "April", Ct)).Cards.Count));
        await using var empty = await ComplianceWorld.EmptyAsync(Ct);
        await empty.Commands.SaveProfileAsync(empty.Ctx(Users.Owner, "e1"), new SaveProfileRequest("New Hotel", null, null, null, "unregistered", true, true, null), Ct);
        Assert.False((await empty.Queries.GetStateAsync(empty.PropertyId, Users.Owner, Ct)).Profile!.Qrmp);
        Assert.All((await empty.Queries.GetMonthAsync(empty.PropertyId, Users.Owner, 2026, "July", Ct)).Cards, c => Assert.Contains(c.Cat, new[] { "it", "lab" }));
    }

    [Fact]
    public async Task Archive_a_key_or_a_category_hides_it_everywhere_owner_only()
    {
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.ToggleArchiveAsync(_w.Ctx(Users.Owner, "a0"), new ToggleArchiveRequest("Nope"), Ct)));
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.ToggleArchiveAsync(_w.Ctx(Users.Accountant, "a1"), new ToggleArchiveRequest("GSTR-1"), Ct)));
        Assert.True((await _w.Commands.ToggleArchiveAsync(_w.Ctx(Users.Owner, "a2"), new ToggleArchiveRequest("GSTR-1"), Ct)).Value.Archived);
        Assert.DoesNotContain((await _w.Queries.GetActionCenterAsync(_w.PropertyId, Users.Owner, null, Ct)).Rows, r => r.ItemId == "oc4");
        Assert.DoesNotContain("GSTR-1 –", (await _w.Queries.GetRegisterCsvAsync(_w.PropertyId, Users.Owner, null, Ct)).Csv, StringComparison.Ordinal);
        Assert.False((await _w.Commands.ToggleArchiveAsync(_w.Ctx(Users.Owner, "a3"), new ToggleArchiveRequest("GSTR-1"), Ct)).Value.Archived);
        await _w.Commands.ToggleCategoryAsync(_w.Ctx(Users.Owner, "a4"), new ToggleCategoryArchiveRequest("lab"), Ct);
        Assert.All((await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "October", Ct)).Cards, c => Assert.NotEqual("lab", c.Cat));
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.ToggleCategoryAsync(_w.Ctx(Users.Owner, "a5"), new ToggleCategoryArchiveRequest("tax"), Ct)));
        Assert.Equal(["lab"], (await State()).ArchivedCats);
    }

    [Fact]
    public async Task Custom_obligations_as_SCC_due_day_0_to_31_retroactive_edit_no_cap_delete_purges_files()
    {
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveCustomAsync(_w.Ctx(Users.Owner, "c0"), new SaveCustomObligationRequest(null, "lab", "", null, 1, ["April"]), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveCustomAsync(_w.Ctx(Users.Owner, "c1"), new SaveCustomObligationRequest(null, "lab", "Fire NOC", null, 1, []), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.SaveCustomAsync(_w.Ctx(Users.Owner, "c2"), new SaveCustomObligationRequest(null, "lab", "Fire NOC", null, 32, ["April"]), Ct)));
        var add = await _w.Commands.SaveCustomAsync(_w.Ctx(Users.Owner, "c3"), new SaveCustomObligationRequest(null, "lab", "Fire NOC renewal", "Annual", 31, ["November", "April"], "Fire dept",
            Risk: "High", Buffer: 45, Checklist: ["Inspect", " ", "Pay fee"]), Ct);
        Assert.Contains("rolls over into the next month", await Audit("COMPLIANCE_CUSTOM"), StringComparison.Ordinal);
        var c = (await State()).Custom.Single();
        Assert.Equal(["April", "November"], c.Months);
        Assert.Equal((30, 2), (c.Buffer!.Value, c.Checklist.Count));
        var nov = (await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "November", Ct)).Cards.Single(x => x.ItemId == add.Value.Id + "-Nov");
        Assert.Equal(("2026-12-01", "High", 30, "2026-11-01", false), (nov.DueDate, nov.Risk, nov.Buffer, nov.InternalDate, nov.Urgent));
        await _w.Commands.SaveCustomAsync(_w.Ctx(Users.Owner, "c4"), new SaveCustomObligationRequest(add.Value.CustomId, "lab", "Fire NOC renewal", "Annual", 0, ["November"],
            ExpectedVersion: add.Value.Version), Ct);
        Assert.Equal("2026-11-30", (await _w.Queries.GetMonthAsync(_w.PropertyId, Users.Owner, null, "November", Ct)).Cards.Single(x => x.ItemId == add.Value.Id + "-Nov").DueDate);
        Assert.Equal("STALE_VERSION", await Code(() => _w.Commands.SaveCustomAsync(_w.Ctx(Users.Owner, "c5"), new SaveCustomObligationRequest(add.Value.CustomId, "lab", "X", null, 1, ["May"],
            ExpectedVersion: add.Value.Version), Ct)));
        for (var i = 0; i < 31; i++)
            await _w.Commands.SaveCustomAsync(_w.Ctx(Users.Owner, "same" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), new SaveCustomObligationRequest(null, null, "Same name", null, i, ["May"]), Ct);
        Assert.Equal(32, (await State()).Custom.Count);
        await _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "c6"), new AddEvidenceRequest(2026, add.Value.Id + "-Nov", "noc.pdf", "application/pdf", "JVBERg=="), Ct);
        var del = await _w.Commands.DeleteCustomAsync(_w.Ctx(Users.Owner, "c7"), new DeleteCustomObligationRequest(add.Value.CustomId), Ct);
        Assert.Equal(1, del.Value.EvidenceRemoved);
        Assert.DoesNotContain((await State()).Custom, x => x.CustomId == add.Value.CustomId);
        Assert.Equal(0, await _w.Db.CountAsync($"SELECT COUNT(*) FROM cmp.evidence WHERE item_id LIKE '{add.Value.Id}-%';", Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM cmp.custom_obligation WHERE custom_id = {add.Value.CustomId} AND deleted_utc IS NOT NULL;", Ct));
    }

    [Fact]
    public async Task LF_images_G1_an_evidence_picture_is_held_to_153600_bytes_a_PDF_keeps_5_MB()
    {
        // OQ-1: pictures (image/*) are compressed on the device to about 150 KB and refused above 153,600; challans are PDFs and keep the 5 MB limit.
        var jpegAtCap = Convert.ToBase64String(new byte[Hcc.Domain.Compliance.ComplianceRules.MaxImageBytes]);
        var jpegOver = Convert.ToBase64String(new byte[Hcc.Domain.Compliance.ComplianceRules.MaxImageBytes + 1]);
        var pdfBig = Convert.ToBase64String(new byte[200_000]);
        await _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "g1a"), new AddEvidenceRequest(2026, "jn4", "scan.jpg", "image/jpeg", jpegAtCap), Ct);
        var ex = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "g1b"), new AddEvidenceRequest(2026, "jn4", "scan2.jpg", "image/jpeg", jpegOver), Ct));
        Assert.Equal("Take the photo again, closer and in good light.", ex.Message);
        await _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "g1c"), new AddEvidenceRequest(2026, "jn4", "challan.pdf", "application/pdf", pdfBig), Ct);
    }

    [Fact]
    public async Task Evidence_files_up_to_5_MB_complete_the_evidence_pack_and_zip_as_SCC()
    {
        Assert.Equal(["jn4", "jl11"], (await _w.Queries.GetDashboardAsync(_w.PropertyId, Users.Owner, null, "Q1", Ct)).EvidenceMissing);
        var added = await _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "e1"), new AddEvidenceRequest(2026, "jn4", "form16a.pdf", "application/pdf", "JVBERi0xLjQ="), Ct);
        Assert.Equal(8, added.Value.SizeBytes);
        Assert.Equal(["jl11"], (await _w.Queries.GetDashboardAsync(_w.PropertyId, Users.Owner, null, "Q1", Ct)).EvidenceMissing);
        var file = await _w.Queries.GetEvidenceFileAsync(_w.PropertyId, Users.Owner, added.Value.EvidenceId, Ct);
        Assert.Equal(("form16a.pdf", "application/pdf", "JVBERi0xLjQ="), (file.FileName, file.ContentType, file.Base64));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "e2"), new AddEvidenceRequest(2026, "jn4", "big.bin", null, Convert.ToBase64String(new byte[5 * 1024 * 1024 + 1])), Ct)));
        Assert.Equal("VALIDATION", await Code(() => _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "e3"), new AddEvidenceRequest(2026, "jn4", "x", null, "not base64!"), Ct)));
        await _w.Commands.AddEvidenceAsync(_w.Ctx(Users.Accountant, "e4"), new AddEvidenceRequest(2026, "jn4", "form16a.pdf", "application/pdf", "QQ=="), Ct);
        var zip = await _w.Queries.GetEvidenceZipAsync(_w.PropertyId, Users.Owner, null, Ct);
        Assert.Equal(("Evidence_HCC_Demo_Residency_2026_27.zip", 3), (zip.FileName, zip.Files));
        using (var ms = new MemoryStream(Convert.FromBase64String(zip.Base64)))
        using (var archive = new System.IO.Compression.ZipArchive(ms))
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("October_Income_Tax_TDS_Deposit_September_Deductions__oc1/challan-281-sep-2026.txt", names);
            Assert.Contains(names, n => n.EndsWith("__jn4/form16a (2).pdf", StringComparison.Ordinal));
            Assert.Contains("index.csv", names);
        }

        await _w.Commands.DeleteEvidenceAsync(_w.Ctx(Users.Accountant, "e5"), new DeleteEvidenceRequest(added.Value.EvidenceId), Ct);
        Assert.Equal("NOT_FOUND", await Code(() => _w.Queries.GetEvidenceFileAsync(_w.PropertyId, Users.Owner, added.Value.EvidenceId, Ct)));
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.DeleteEvidenceAsync(_w.Ctx(Users.Accountant, "e6"), new DeleteEvidenceRequest(added.Value.EvidenceId), Ct)));
    }

    [Fact]
    public async Task Payable_from_HCC_data_EPF_ESIC_employee_plus_employer_PT_from_the_locked_run_GST_from_issued_invoices()
    {
        var p = _w.PropertyId;
        await _w.Db.ExecAsync($"""
            INSERT hr.department(property_id, dept_code, name) VALUES ({p}, 'FO', N'Front office');
            INSERT hr.staff(property_id, name, dept_code, designation, pay_type, monthly_paise, daily_paise, default_shift, weekly_off, joined, active, phone, created_by, created_utc)
              VALUES ({p}, N'Asha (fictional)', 'FO', N'Receptionist', 'monthly', 2000000, 0, 'M', 0, '2026-01-01', 1, '9000000001', 1, '2026-01-01');
            DECLARE @s BIGINT = SCOPE_IDENTITY();
            INSERT hr.pay_run(property_id, pay_month, status, rates_json, warnings_json, lock_seq, prepared_by, prepared_utc, locked_by, locked_utc)
              VALUES ({p}, '2026-08-01', 'Locked', N'[]', N'[]', 1, 1, '2026-09-01', 8, '2026-09-02');
            INSERT hr.pay_run_line(run_id, staff_id, line_json, earned_paise, gross_paise, pf_paise, pf_er_paise, esic_paise, esic_er_paise, pt_paise, advance_paise, net_paise)
              VALUES (SCOPE_IDENTITY(), @s, N'[]', 2000000, 2000000, 120000, 130000, 15000, 65000, 20000, 0, 1845000);
            INSERT hr.pay_run(property_id, pay_month, status, rates_json, warnings_json, lock_seq, prepared_by, prepared_utc)
              VALUES ({p}, '2026-09-01', 'Draft', N'[]', N'[]', 0, 1, '2026-10-01');
            """, Ct);
        var feed = await _w.Queries.GetPayableAsync(p, Users.Owner, null, Ct);
        var aug = feed.Months["2026-08"];
        Assert.Equal((250000L, 80000L, 20000L, 0L), (aug.PfPaise, aug.EsicPaise, aug.PtPaise, aug.GstPaise));
        Assert.Equal(["Payroll Aug 2026 (locked run)"], aug.Sources);
        Assert.False(feed.Months.ContainsKey("2026-09"));   // a draft run is not payable
        Assert.Contains("LOCKED pay run", feed.Basis, StringComparison.Ordinal);
        var sep = await _w.Queries.GetMonthAsync(p, Users.Owner, null, "September", Ct);
        Assert.Equal(2500, sep.Cards.Single(c => c.ItemId == "sp6").PayableRupees);   // EPF August wages on the September page (SCC P−1)
        Assert.Equal(800, sep.Cards.Single(c => c.ItemId == "sp7").PayableRupees);
        Assert.Equal(200, sep.Cards.Single(c => c.ItemId == "sp8").PayableRupees);
        Assert.Null((await _w.Queries.GetMonthAsync(p, Users.Owner, null, "October", Ct)).Cards.Single(c => c.ItemId == "oc9").PayableRupees);
    }

    [Fact]
    public async Task Ca_pack_is_simulated_with_SCC_lines_and_settings_keep_the_HCC_window()
    {
        var s = (await State()).Settings;
        Assert.Equal("FORBIDDEN", await Code(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Accountant, "g0"), new SaveSettingsRequest(null, null, 30, s.Version), Ct)));
        var saved = await _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Owner, "g1"), new SaveSettingsRequest(null, null, 3, s.Version), Ct);
        Assert.Equal(["oc4"], (await _w.Queries.GetDueThisWeekAsync(_w.PropertyId, Users.Owner, Ct)).Rows.Select(r => r.ItemId));
        Assert.Equal("STALE_VERSION", await Code(() => _w.Commands.SaveSettingsAsync(_w.Ctx(Users.Owner, "g2"), new SaveSettingsRequest(null, null, 30, s.Version), Ct)));
        Assert.Equal(3, saved.Value.DueSoonDays);
        var pack = await _w.Commands.GenerateCaPackAsync(_w.Ctx(Users.Accountant, "k1"), new GenerateCaPackRequest(), Ct);
        Assert.Equal(1, pack.Value.Overdue);
        var lines = (await _w.Queries.GetCaPacksAsync(_w.PropertyId, Users.Owner, Ct)).Single().Lines;
        Assert.Equal("Position as on 2026-10-24 (FY 2026–27): 49 done, 53 pending (1 overdue), 6 not applicable.", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("OPEN · GSTR-1 – September (Monthly Filers) · October · due 2026-10-11 · 13d overdue", StringComparison.Ordinal));
        Assert.Contains("simulated", await Audit("COMPLIANCE_CA_PACK"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_grants_equal_DEC055_and_a_failed_command_leaves_no_audit_row()
    {
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-CMP-001' AND role_code = 'ACC';", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id LIKE 'PERM-CMP-%' AND role_code NOT IN ('OWN','ACC');", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-CMP-002' AND role_code <> 'OWN';", Ct));
        var before = await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event;", Ct);
        Assert.Equal("NOT_FOUND", await Code(() => _w.Commands.MarkDoneAsync(_w.Ctx(Users.Owner, "z1"), new MarkDoneRequest(2026, "nope"), Ct)));
        Assert.Equal(before, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event;", Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN.ToString(), await Code(() => _w.Commands.MarkNotApplicableAsync(_w.Ctx(Users.Manager, "z2"), new MarkNotApplicableRequest(2026, "oc2"), Ct)));
    }
}
