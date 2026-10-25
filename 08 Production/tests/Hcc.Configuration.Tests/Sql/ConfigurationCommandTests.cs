using System.Text.Json;
using Hcc.Configuration.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Configuration;
using Hcc.Domain.Configuration;
using static Hcc.Configuration.Tests.Harness.ConfigDatabase;

namespace Hcc.Configuration.Tests.Sql;

/// <summary>
/// [CFG] command behaviour on SQL with the real P1 policy (0101 role templates): DEC-058 permissions, validation, approvals (never
/// self-approve), effective dates, withdraw, history + revert, modules on/off, export/import, secrets, idempotency, audit + outbox.
/// Each test runs in its own fictional property.
/// </summary>
[Trait("Category", "Sql")]
public class ConfigurationCommandTests(ConfigDatabase db) : IClassFixture<ConfigDatabase>
{
    private static ConfigValue T(string s) => ConfigValue.Of(s);

    private static ConfigValue N(long n) => ConfigValue.Of(n);

    private Task<SetSettingResult> Set(Hotel h, int who, string key, object? value, string? effectiveFrom = null, string? reason = null) => db.SetAsync(h, who, key, value, effectiveFrom, reason);

    [Fact]
    public async Task Commands_enforce_DEC058_Tech_admin_money_forbidden_non_money_saved_others_forbidden()
    {
        var h = await db.NewHotelAsync();
        foreach (var (k, v) in new (string, object)[] { ("discountLimit", 60000), ("approvalExpiryHours", 12), ("invoicePrefix", "HCC-25"), ("fd.deposit", 200000) })
            Assert.Equal("FORBIDDEN", await Expect.Code(() => Set(h, h.TechAdmin, k, v)));
        var r = await Set(h, h.TechAdmin, "hotel", "HCC Demo Residency West");
        Assert.Equal(("applied", "—", "HCC Demo Residency West"), (r.Status, r.Before, r.After));
        Assert.Equal(T("HCC Demo Residency West"), await db.ValueAsync(h, "hotel"));
        await Set(h, h.TechAdmin, "brand.printFormat", "A5");
        Assert.Equal(T("A5"), await db.ValueAsync(h, "brand.printFormat"));
        foreach (var who in h.NonAdmins) Assert.Equal("FORBIDDEN", await Expect.Code(() => Set(h, who, "hotel", "X")));
        Assert.Equal("FORBIDDEN", await Expect.Code(() => Set(h, h.Reception, "checkin", "13:00")));
        Assert.Equal("VALIDATION", await Expect.Code(() => Set(h, h.Owner, "taxBps", 1200)));
        Assert.Equal("VALIDATION", await Expect.Code(() => Set(h, h.Owner, "currency", "USD")));
        Assert.Equal("VALIDATION", await Expect.Code(() => Set(h, h.Owner, "nope", 1)));
        // Owner changes money settings directly.
        Assert.Equal("applied", (await Set(h, h.Owner, "discountLimit", 60000)).Status);
    }

    [Fact]
    public async Task Validation_messages_are_plain_and_a_same_value_is_refused()
    {
        var h = await db.NewHotelAsync();
        Assert.Equal("Enter a time as HH:mm.", await Expect.Message(() => Set(h, h.Owner, "checkin", "25:00")));
        Assert.Equal("Use 2–8 capital letters, digits or hyphens.", await Expect.Message(() => Set(h, h.Owner, "invoicePrefix", "inv")));
        Assert.Matches("whole number between 1 and 72 h", await Expect.Message(() => Set(h, h.Owner, "fd.holdHours", 0)));
        Assert.Matches("Enter an amount from", await Expect.Message(() => Set(h, h.Owner, "discountLimit", -1)));
        Assert.Matches("15 capital letters", await Expect.Message(() => Set(h, h.Owner, "brand.gstin", "12345")));
        await Set(h, h.Owner, "brand.gstin", "27DEMO0000X0Z05");
        Assert.Equal("OK", await Expect.Code(() => Set(h, h.Owner, "brand.gstin", "")));
        Assert.Equal(SettingRules.NoChange, await Expect.Message(() => Set(h, h.Owner, "brand.printFormat", "A4")));
        Assert.Equal(T("Grand"), T((await Set(h, h.Owner, "hotel", "  Grand  ")).After));
    }

    [Fact]
    public async Task Tech_admin_change_with_an_Owner_approver_becomes_a_request_the_requester_cannot_decide_and_the_Owner_approves()
    {
        var h = await db.NewHotelAsync();
        var r = await Set(h, h.TechAdmin, "sec.pinLock", true, reason: "Shared tablet at reception");
        Assert.Equal("requested", r.Status);
        Assert.Equal(ConfigValue.Of(false), await db.ValueAsync(h, "sec.pinLock"));
        var a = await db.RequestAsync(h, r.RequestId!.Value);
        Assert.Equal(("Pending", "Owner", "Master configuration · PIN lock on shared devices · Off → On"), (a.Status, a.Approver, a.Summary));
        Assert.Equal("INVALID_TRANSITION", await Expect.Code(() => Set(h, h.TechAdmin, "sec.pinLock", true)));
        Assert.Matches("You raised this request", await Expect.Message(() => db.DecideAsync(h, h.TechAdmin, a.RequestId, true)));
        Assert.Matches("Only the Owner", await Expect.Message(() => db.DecideAsync(h, h.Manager, a.RequestId, true)));
        Assert.Equal("FORBIDDEN", await Expect.Code(() => db.DecideAsync(h, h.Reception, a.RequestId, true)));
        var managerView = await db.Queries.RequestsAsync(h.PropertyId, h.Manager);
        Assert.Empty(managerView.Items); // a Manager sees only Manager-approver requests
        Assert.Equal("Executed", (await db.DecideAsync(h, h.Owner, a.RequestId, true)).Status);
        Assert.Equal(ConfigValue.Of(true), await db.ValueAsync(h, "sec.pinLock"));
        Assert.Equal("Executed", (await db.RequestAsync(h, a.RequestId)).Status);
        var hist = (await db.HistoryAsync(h, "sec.pinLock"))[0];
        Assert.Matches("Approved request .* from Lata", hist.Reason);
        Assert.Equal(("Lata", "Sagar", "approval"), (hist.ChangedBy, hist.ApprovedBy, hist.Source));
        Assert.Equal("INVALID_TRANSITION", await Expect.Code(() => db.DecideAsync(h, h.Owner, a.RequestId, true)));
    }

    [Fact]
    public async Task Manager_approver_request_Manager_decides_rejection_keeps_value_stale_and_expired_refused()
    {
        // Approve.
        var h = await db.NewHotelAsync();
        var r = await Set(h, h.TechAdmin, "inspection", false);
        Assert.Equal("Manager", (await db.RequestAsync(h, r.RequestId!.Value)).Approver);
        Assert.Null((await db.Queries.RequestsAsync(h.PropertyId, h.Manager)).Items.Single().DecideBlock);
        Assert.Equal("FORBIDDEN", await Expect.Code(() => db.Queries.RequestsAsync(h.PropertyId, h.Reception)));
        Assert.Equal("FORBIDDEN", await Expect.Code(() => db.DecideAsync(h, h.Accountant, r.RequestId.Value, true)));
        await db.DecideAsync(h, h.Manager, r.RequestId.Value, true);
        Assert.Equal(ConfigValue.Of(false), await db.ValueAsync(h, "inspection"));

        // Reject keeps the value.
        var h2 = await db.NewHotelAsync();
        var r2 = await Set(h2, h2.TechAdmin, "inspection", false);
        Assert.Equal("Rejected", (await db.DecideAsync(h2, h2.Manager, r2.RequestId!.Value, false, "Keep inspections")).Status);
        Assert.Equal(ConfigValue.Of(true), await db.ValueAsync(h2, "inspection"));
        Assert.Equal(("Rejected", "Keep inspections"), ((await db.RequestAsync(h2, r2.RequestId.Value)).Status, (await db.RequestAsync(h2, r2.RequestId.Value)).DecisionReason));

        // The Owner changed the setting meanwhile → stale.
        var h3 = await db.NewHotelAsync();
        var r3 = await Set(h3, h3.TechAdmin, "inspection", false);
        await Set(h3, h3.Owner, "inspection", false);
        Assert.Equal("STALE_VERSION", await Expect.Code(() => db.DecideAsync(h3, h3.Manager, r3.RequestId!.Value, true)));

        // Expired after approvalExpiryHours (24 h).
        var h4 = await db.NewHotelAsync();
        var r4 = await Set(h4, h4.TechAdmin, "inspection", false);
        var start = db.Clock.Now;
        db.Clock.Now = start.AddHours(25);
        try
        {
            Assert.Equal("INVALID_TRANSITION", await Expect.Code(() => db.DecideAsync(h4, h4.Manager, r4.RequestId!.Value, true)));
            Assert.Equal("Rejected", (await db.DecideAsync(h4, h4.Manager, r4.RequestId!.Value, false)).Status);
        }
        finally
        {
            db.Clock.Now = start;
        }
    }

    [Fact]
    public async Task Immediate_settings_cannot_be_dated_and_dated_ones_take_today_or_a_later_business_date()
    {
        var h = await db.NewHotelAsync();
        Assert.Matches("takes effect immediately", await Expect.Message(() => Set(h, h.Owner, "hotel", "New Name", "2026-10-30")));
        Assert.Matches("cannot be before the current business date", await Expect.Message(() => Set(h, h.Owner, "discountLimit", 60000, "2026-10-20")));
        Assert.Matches("at most one year", await Expect.Message(() => Set(h, h.Owner, "discountLimit", 60000, "2028-01-01")));
        Assert.Equal("applied", (await Set(h, h.Owner, "discountLimit", 60000, "2026-10-24")).Status);
        Assert.Equal(N(60000), await db.ValueAsync(h, "discountLimit"));
        var s = await Set(h, h.Owner, "discountLimit", 70000, "2026-10-27");
        Assert.Equal(("scheduled", "₹600", "₹700"), (s.Status, s.Before, s.After));
        Assert.Equal(N(60000), await db.ValueAsync(h, "discountLimit"));
        Assert.Equal(N(60000), await db.ValueAsync(h, "discountLimit", new(2026, 10, 26)));
        Assert.Equal(N(70000), await db.ValueAsync(h, "discountLimit", new(2026, 10, 27)));
        await Set(h, h.Owner, "discountLimit", 75000, "2026-10-27");
        var view = (await db.Queries.GroupAsync(h.PropertyId, h.Owner, "money")).Settings.Single(x => x.Key == "discountLimit");
        Assert.Single(view.Scheduled); // same date replaces
        Assert.Equal(("2026-10-27", "₹750"), (view.Scheduled[0].EffectiveFrom, view.Scheduled[0].Display));
        Assert.Equal(N(75000), await db.ValueAsync(h, "discountLimit", new(2026, 10, 30)));
        Assert.Matches("1 dated rate ahead · 1 change pending", (await db.Queries.GroupsAsync(h.PropertyId, h.Owner)).Groups.Single(g => g.Id == "money").Summary);
        // Dated money change never alters posted records: the hub writes only cfg tables.
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM bill.charge;"));
    }

    [Fact]
    public async Task Next_business_day_settings_come_into_force_when_the_next_day_opens()
    {
        var h = await db.NewHotelAsync();
        Assert.Matches(@"next business day \(2026-10-25\)", await Expect.Message(() => Set(h, h.Owner, "fd.noShowTime", "22:00", "2026-10-30")));
        Assert.Equal("scheduled", (await Set(h, h.Owner, "fd.noShowTime", "22:00")).Status);
        await Set(h, h.TechAdmin, "fd.holdHours", 4);
        Assert.Equal(T("23:00"), await db.ValueAsync(h, "fd.noShowTime"));
        Assert.Equal(T("22:00"), await db.ValueAsync(h, "fd.noShowTime", new(2026, 10, 25)));
        await Set(h, h.Owner, "discountLimit", 65000, "2026-10-25");
        await db.SetBusinessDateAsync(h, new(2026, 10, 25)); // P6 opens the next day
        Assert.Equal((T("22:00"), N(4), N(65000)), (await db.ValueAsync(h, "fd.noShowTime"), await db.ValueAsync(h, "fd.holdHours"), await db.ValueAsync(h, "discountLimit")));
        var hist = await db.HistoryAsync(h);
        Assert.All(hist, e => Assert.Equal("applied", e.Status));
        Assert.All((await db.Queries.GroupAsync(h.PropertyId, h.Owner, "frontdesk")).Settings, x => Assert.Empty(x.Scheduled));
    }

    [Fact]
    public async Task A_scheduled_change_can_be_withdrawn_before_its_date_and_history_is_kept()
    {
        var h = await db.NewHotelAsync();
        var s = await Set(h, h.Owner, "fd.deposit", 150000);
        Assert.Equal("FORBIDDEN", await Expect.Code(() => db.Service.WithdrawAsync(Ctx(h, h.TechAdmin), new(s.ChangeId!.Value))));
        await db.Service.WithdrawAsync(Ctx(h, h.Owner), new(s.ChangeId!.Value, "Not yet"));
        Assert.Equal(N(100000), await db.ValueAsync(h, "fd.deposit", new(2026, 10, 25)));
        Assert.Equal("withdrawn", (await db.HistoryAsync(h, "fd.deposit"))[0].Status);
        Assert.Equal("NOT_FOUND", await Expect.Code(() => db.Service.WithdrawAsync(Ctx(h, h.Owner), new(s.ChangeId.Value))));
        var applied = await Set(h, h.Owner, "hotel", "Applied Name");
        Assert.Equal("INVALID_TRANSITION", await Expect.Code(() => db.Service.WithdrawAsync(Ctx(h, h.Owner), new(applied.ChangeId!.Value))));
        // Tech admin withdraws a scheduled non-money change it may make.
        var t = await Set(h, h.TechAdmin, "fd.holdHours", 8);
        Assert.Equal("OK", await Expect.Code(() => db.Service.WithdrawAsync(Ctx(h, h.TechAdmin), new(t.ChangeId!.Value))));
    }

    [Fact]
    public async Task History_and_revert_to_previous_value_latest_change_only_same_permission_rules()
    {
        var h = await db.NewHotelAsync();
        var one = await Set(h, h.Owner, "hotel", "Name One");
        var two = await Set(h, h.Owner, "hotel", "Name Two");
        var hist = await db.HistoryAsync(h, "hotel");
        Assert.Equal(("Name One", "Name Two"), (hist[0].Before, hist[0].After));
        Assert.Equal("A newer change exists for this setting.", hist[1].RevertBlock);
        Assert.True(hist[0].CanRevert);
        var back = (await db.Service.RevertAsync(Ctx(h, h.Owner), new(two.ChangeId!.Value))).Value;
        Assert.Equal(T("Name One"), await db.ValueAsync(h, "hotel"));
        Assert.Equal(two.ChangeId, (await db.HistoryAsync(h, "hotel"))[0].RevertOf);
        Assert.Equal("revert", (await db.HistoryAsync(h, "hotel"))[0].Source);
        await db.Service.RevertAsync(Ctx(h, h.Owner), new(back.ChangeId!.Value));
        Assert.Equal(T("Name Two"), await db.ValueAsync(h, "hotel"));
        Assert.Equal("INVALID_TRANSITION", await Expect.Code(() => db.Service.RevertAsync(Ctx(h, h.Owner), new(one.ChangeId!.Value))));

        var d = await Set(h, h.Owner, "discountLimit", 60000);
        Assert.Equal("FORBIDDEN", await Expect.Code(() => db.Service.RevertAsync(Ctx(h, h.TechAdmin), new(d.ChangeId!.Value))));
        await db.Service.RevertAsync(Ctx(h, h.Owner), new(d.ChangeId!.Value));
        Assert.Equal(N(25000), await db.ValueAsync(h, "discountLimit"));

        var sched = await Set(h, h.Owner, "fd.deposit", 120000);
        Assert.Equal("Not in effect yet — withdraw it instead.", await Expect.Message(() => db.Service.RevertAsync(Ctx(h, h.Owner), new(sched.ChangeId!.Value))));
        // A revert by Tech admin of a setting with an approver becomes a request too.
        var pin = await Set(h, h.Owner, "sec.pinIdleMinutes", 10);
        var rq = (await db.Service.RevertAsync(Ctx(h, h.TechAdmin), new(pin.ChangeId!.Value))).Value;
        Assert.Equal("requested", rq.Status);
        await db.DecideAsync(h, h.Owner, rq.RequestId!.Value, true);
        Assert.Equal(N(5), await db.ValueAsync(h, "sec.pinIdleMinutes"));
        Assert.Equal(pin.ChangeId, (await db.HistoryAsync(h, "sec.pinIdleMinutes"))[0].RevertOf);
    }

    [Fact]
    public async Task Module_switch_off_refuses_commands_keeps_data_and_core_is_never_switchable()
    {
        var h = await db.NewHotelAsync();
        Assert.True(await db.Gate.IsOnAsync(h.PropertyId, "pos"));
        await Set(h, h.TechAdmin, "module.pos", false);
        Assert.False(await db.Gate.IsOnAsync(h.PropertyId, "pos"));
        var ex = await Expect.Fails(HccErrorCode.FORBIDDEN, () => db.Gate.EnsureOnAsync(h.PropertyId, "pos"));
        Assert.Matches("Restaurant & POS is switched off in Master configuration", ex.Message);
        Assert.Equal("pos", ex.Details!["moduleId"].GetString());
        Assert.Equal("OK", await Expect.Code(() => db.Gate.EnsureOnAsync(h.PropertyId, "payroll")));
        Assert.Throws<InvalidOperationException>(() => db.Gate.IsOnAsync(h.PropertyId, "frontdesk").GetAwaiter().GetResult());
        Assert.Equal("9 of 10 on · 6 core always on", (await db.Queries.GroupsAsync(h.PropertyId, h.Owner)).Groups.Single(g => g.Id == "modules").Summary);
        var modules = await db.Queries.ModulesAsync(h.PropertyId);
        Assert.Equal(["pos"], modules.Modules.Where(m => !m.On).Select(m => m.Id));
        Assert.Contains("Master configuration", modules.Core);
        await Set(h, h.Owner, "module.pos", true);
        Assert.Equal("10 of 10 on · 6 core always on", (await db.Queries.GroupsAsync(h.PropertyId, h.Owner)).Groups.Single(g => g.Id == "modules").Summary);
        // Another property is unaffected.
        var other = await db.NewHotelAsync();
        await Set(h, h.Owner, "module.payroll", false);
        Assert.True(await db.Gate.IsOnAsync(other.PropertyId, "payroll"));
    }

    [Fact]
    public async Task Export_has_no_secrets_import_into_another_property_applies_and_a_round_trip_is_a_no_op()
    {
        var h = await db.NewHotelAsync();
        await Set(h, h.Owner, "msg.gatewayKey", "sk-demo-123");
        await Set(h, h.Owner, "brand.printFormat", "A5");
        await Set(h, h.Owner, "discountLimit", 55000);
        await Set(h, h.Owner, "module.groups", false);
        var x = await db.Queries.ExportAsync(h.PropertyId, h.Owner);
        var text = JsonSerializer.Serialize(x, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        // Schema = the database's current migration version (the newest embedded module block, not 0700 once later modules are merged).
        Assert.Equal((ConfigurationServiceFormat, 1, new Hcc.Infrastructure.SqlServer.Migrations.EmbeddedMigrationSource().Load()[^1].Version), (x.Format, x.Version, x.Schema));
        Assert.DoesNotContain("sk-demo-123", text, StringComparison.Ordinal);
        Assert.Contains("msg.gatewayKey", x.Excluded);
        Assert.False(x.Values.ContainsKey("msg.gatewayKey"));
        Assert.Equal(("A5", 55000L, false), (x.Values["brand.printFormat"].GetString(), x.Values["discountLimit"].GetInt64(), x.Values["module.groups"].GetBoolean()));
        Assert.False(x.Values.ContainsKey("currency") || x.Values.ContainsKey("taxBps"));
        Assert.DoesNotContain("\"users\"", text, StringComparison.Ordinal);

        var t = await db.NewHotelAsync();
        using var doc = JsonDocument.Parse(text);
        var r = (await db.Service.ImportAsync(Ctx(t, t.Owner), new(doc.RootElement.Clone()))).Value;
        Assert.Equal("Configuration imported · 3 changed · " + r.Unchanged + " unchanged · 0 skipped", r.Message);
        Assert.Equal((T("A5"), N(55000)), (await db.ValueAsync(t, "brand.printFormat"), await db.ValueAsync(t, "discountLimit")));
        Assert.False(await db.Gate.IsOnAsync(t.PropertyId, "groups"));
        Assert.Equal(T(""), await db.ValueAsync(t, "msg.gatewayKey"));
        Assert.Equal("VALIDATION", await Expect.Code(() => db.Service.ImportAsync(Ctx(t, t.Owner), new(doc.RootElement.Clone()))));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE property_id = @p AND action = 'CFG_IMPORT' AND detail LIKE N'Configuration imported · 3 changed%';", ("@p", t.PropertyId)));
        Assert.All(await db.HistoryAsync(t), e => Assert.Equal(("import", "Imported configuration"), (e.Source, e.Reason)));
    }

    private const string ConfigurationServiceFormat = "hcc-master-configuration";

    [Fact]
    public async Task Import_Tech_admin_skips_money_settings_with_the_reason_bad_files_and_invalid_values_are_refused_whole()
    {
        var h = await db.NewHotelAsync();
        var x = await db.Queries.ExportAsync(h.PropertyId, h.Owner);
        var values = x.Values.ToDictionary(k => k.Key, k => k.Value);
        values["discountLimit"] = J(1);
        values["brand.printFormat"] = J("A5");
        values["msg.gatewayKey"] = J("leak");
        values["unknown.key"] = J(1);
        var file = J(x with { Values = values });
        var plan = await db.Queries.ImportPlanAsync(h.PropertyId, h.TechAdmin, file);
        Assert.Equal(["brand.printFormat"], plan.Apply.Select(p => p.Key));
        Assert.Contains("Owner-only", plan.Skipped.Single(k => k.Key == "discountLimit").Reason, StringComparison.Ordinal);
        Assert.Equal("Secrets are never imported", plan.Skipped.Single(k => k.Key == "msg.gatewayKey").Reason);
        Assert.Equal("Not in this registry", plan.Skipped.Single(k => k.Key == "unknown.key").Reason);
        await db.Service.ImportAsync(Ctx(h, h.TechAdmin), new(file));
        Assert.Equal((N(25000), T("A5"), T("")), (await db.ValueAsync(h, "discountLimit"), await db.ValueAsync(h, "brand.printFormat"), await db.ValueAsync(h, "msg.gatewayKey")));

        Assert.Equal("This is not a Master configuration export file.", await Expect.Message(() => db.Service.ImportAsync(Ctx(h, h.Owner), new(J(new { format = "other" })))));
        Assert.Matches("Unsupported export version 2", await Expect.Message(() => db.Service.ImportAsync(Ctx(h, h.Owner), new(J(x with { Version = 2 })))));
        var bad = x.Values.ToDictionary(k => k.Key, k => k.Value);
        bad["checkin"] = J("9am");
        bad["brand.printFormat"] = J("A4");
        Assert.Matches("Standard check-in time: Enter a time as HH:mm", await Expect.Message(() => db.Service.ImportAsync(Ctx(h, h.Owner), new(J(x with { Values = bad })))));
        Assert.Equal(T("A5"), await db.ValueAsync(h, "brand.printFormat")); // refused whole
        Assert.Equal("FORBIDDEN", await Expect.Code(() => db.Service.ImportAsync(Ctx(h, h.Reception), new(file))));
    }

    [Fact]
    public async Task Secrets_never_appear_in_history_audit_outbox_or_query_values()
    {
        var h = await db.NewHotelAsync();
        Assert.Equal("requested", (await Set(h, h.TechAdmin, "msg.gatewayKey", "sk-tad-999")).Status);
        await Set(h, h.Owner, "msg.gatewayKey", "sk-demo-123");
        var hist = (await db.HistoryAsync(h, "msg.gatewayKey"))[0];
        Assert.Equal(("Not set", "Set (hidden)"), (hist.Before, hist.After));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE N'%sk-demo-123%' OR detail LIKE N'%sk-tad-999%';"));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE payload LIKE N'%sk-demo-123%';"));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM cfg.change_request WHERE summary LIKE N'%sk-tad-999%';"));
        foreach (var who in new[] { h.Owner, h.TechAdmin })
        {
            var v = (await db.Queries.GroupAsync(h.PropertyId, who, "messages")).Settings.Single(s => s.Key == "msg.gatewayKey");
            Assert.Equal((null, "Set (hidden)", true), ((JsonElement?)v.Value, v.Display, v.Secret));
        }
    }

    [Fact]
    public async Task Queries_gate_on_PERM_ADM_003_show_read_only_reasons_and_search()
    {
        var h = await db.NewHotelAsync();
        foreach (var who in h.NonAdmins) Assert.Equal("FORBIDDEN", await Expect.Code(() => db.Queries.GroupsAsync(h.PropertyId, who)));
        var tad = await db.Queries.GroupsAsync(h.PropertyId, h.TechAdmin);
        Assert.Equal(("2026-10-24", 12), (tad.BusinessDate, tad.Groups.Count));
        Assert.All(tad.Groups.Where(g => g.Money), g => Assert.Equal((true, SettingRules.OwnerOnly), (g.ReadOnly, g.Reason)));
        var money = await db.Queries.GroupAsync(h.PropertyId, h.TechAdmin, "money");
        Assert.All(money.Settings.Where(s => s.Editable), s => Assert.Equal("deny", s.CanChange.Mode));
        var users = await db.Queries.GroupAsync(h.PropertyId, h.TechAdmin, "users");
        Assert.Equal(("request", "Owner"), (users.Settings.Single(s => s.Key == "sec.pinLock").CanChange.Mode, users.Settings.Single(s => s.Key == "sec.pinLock").CanChange.Approver));
        Assert.Equal("NOT_FOUND", await Expect.Code(() => db.Queries.GroupAsync(h.PropertyId, h.Owner, "nope")));
        var hit = Assert.Single((await db.Queries.SearchAsync(h.PropertyId, h.Owner, "gstin")).Hits);
        Assert.Equal(("property", "brand.gstin"), (hit.GroupId, Assert.Single(hit.Keys)));
        await Set(h, h.TechAdmin, "sec.pinLock", true);
        Assert.Matches("1 change pending", (await db.Queries.GroupsAsync(h.PropertyId, h.Owner)).Groups.Single(g => g.Id == "users").Summary);
        await Set(h, h.Owner, "fd.noShowTime", "21:00");
        Assert.Matches("1 change pending", (await db.Queries.GroupsAsync(h.PropertyId, h.Owner)).Groups.Single(g => g.Id == "frontdesk").Summary);
        Assert.Equal("VALIDATION", await Expect.Code(() => db.Queries.HistoryAsync(h.PropertyId, h.Owner, null, 1, 101)));
        Assert.Equal("FORBIDDEN", await Expect.Code(() => db.Queries.ExportAsync(h.PropertyId, h.Manager)));
    }

    [Fact]
    public async Task Idempotent_replay_stale_version_audit_and_outbox_in_the_same_transaction()
    {
        var h = await db.NewHotelAsync();
        var ctx = Ctx(h, h.Owner);
        var req = new SetSettingRequest("brand.printFormat", J("A5"));
        var first = await db.Service.SetSettingAsync(ctx, req);
        var again = await db.Service.SetSettingAsync(ctx, req);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value, again.Value);
        Assert.Equal("IDEMPOTENCY_MISMATCH", await Expect.Code(() => db.Service.SetSettingAsync(ctx, req with { Value = J("A4") })));
        // Stale: an old (or missing) version is refused once the setting has one.
        Assert.Equal("STALE_VERSION", await Expect.Code(() => db.Service.SetSettingAsync(Ctx(h, h.Owner), new("brand.printFormat", J("A4")))));
        Assert.Equal("OK", await Expect.Code(() => db.Service.SetSettingAsync(Ctx(h, h.Owner), new("brand.printFormat", J("A4"), ExpectedVersion: first.Value.Version))));
        Assert.Equal("STALE_VERSION", await Expect.Code(() => db.Service.SetSettingAsync(Ctx(h, h.Owner), new("brand.printFormat", J("80 mm thermal"), ExpectedVersion: first.Value.Version))));
        Assert.Equal(2, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE property_id = @p AND action = 'SETTING_SET';", ("@p", h.PropertyId)));
        Assert.Equal(2, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE property_id = @p AND event_type = 'SettingChanged';", ("@p", h.PropertyId)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE property_id = @p AND detail = N'Setting brand.printFormat · A4 → A5 · effective 2026-10-24';", ("@p", h.PropertyId)));
    }

    [Fact]
    public async Task Concurrent_changes_to_one_setting_serialize_one_wins_the_other_is_stale()
    {
        var h = await db.NewHotelAsync();
        var tasks = Enumerable.Range(0, 6).Select(i => Expect.Code(() => db.Service.SetSettingAsync(Ctx(h, h.Owner), new("hk.maxRoomsPerAttendant", J(10 + i))))).ToArray();
        var codes = await Task.WhenAll(tasks);
        Assert.Equal(1, codes.Count(c => c == "OK"));
        Assert.Equal(5, codes.Count(c => c == "STALE_VERSION"));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = @p;", ("@p", h.PropertyId)));
    }
}
