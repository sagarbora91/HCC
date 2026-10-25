using Hcc.Contracts.Inventory;
using Hcc.Contracts.Yield;
using Hcc.Domain.Errors;
using Hcc.Inventory.Tests.Harness;
using Hcc.Yield.Tests.Harness;

namespace Hcc.Yield.Tests.Sql;

/// <summary>Fresh BASE database per test (xunit builds one instance per test).</summary>
public abstract class YieldSqlTest : IAsyncLifetime
{
    protected YieldFixture F { get; } = new();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => F.InitializeAsync();

    public ValueTask DisposeAsync() => F.DisposeAsync();

    protected static readonly string[] AllTypes = ["Classic", "Deluxe", "Suite"];

    protected int[] Types(params string[] codes) => [.. codes.Select(c => F.TypeIds[c])];

    protected Task<int> CountAsync(string sql) => F.Db.CountAsync(sql, Ct);

    protected Task<object?> ScalarAsync(string sql) => F.Db.ScalarAsync(sql, Ct);

    protected Task ExecAsync(string sql) => F.Db.ExecAsync(sql, Ct);

    protected async Task<string> CodeAsync(Func<Task> call) => await Outcome.CodeAsync(call);

    protected Task<Hcc.Application.Commands.CommandResult<YieldPublishResult>> PublishAsync(int who, string from, string to, string[] types, string? key = null)
        => F.Service.PublishAsync(F.Ctx(who, "PublishYieldTariffs", key), new PublishYieldRequest(from, to, Types(types), "test"), Ct);

    protected Task<YieldCalendarResult> CalendarAsync(int nights = 20) => F.Queries.GetCalendarAsync(1, F.Owner, null, nights, Ct);

    protected async Task<YieldNightView> NightAsync(string type, string date, int nights = 30)
        => (await CalendarAsync(nights)).Cells.Single(c => c.RoomType == type && c.Date == date);

    protected async Task<QuoteResult> P2QuoteAsync(string type, string arrival, string departure)
        => (await F.P2.Inventory.QuoteStayAsync(P2Services.Ctx(FakeAccessPolicy.Owner, "QuoteStay"), new QuoteStayRequest(arrival, departure, 1, 0, null, F.TypeIds[type]), Ct)).Value;
}

[Trait("Category", "Sql")]
public sealed class YieldCalendarSqlTests : YieldSqlTest
{
    [Fact]
    public async Task Calendar_reproduces_the_prototype_figures_on_the_BASE_fixture() // 11 on the books: the 10 stays plus Held HCC-1090 (hold until 09:30 > clock 08:00)
    {
        var sat = await NightAsync("Classic", "2026-10-24");
        Assert.Equal((200000L, 220000L, 200000L), (sat.BasePaise, sat.SuggestedPaise, sat.LivePaise));
        Assert.Equal((23, 11, 14, 61, true), (sat.Occupancy.Sellable, sat.Occupancy.OnBooks, sat.Occupancy.Forecast, sat.Occupancy.Pct, sat.Occupancy.Simulated));
        Assert.Contains("Sat +10%", sat.Explain, StringComparison.Ordinal);
        Assert.Equal(180000, (await NightAsync("Classic", "2026-11-02")).SuggestedPaise);
        Assert.Equal(200000, (await NightAsync("Classic", "2026-10-28")).SuggestedPaise);
        Assert.Equal(325000, (await NightAsync("Deluxe", "2026-10-28")).SuggestedPaise);
        var suite = await NightAsync("Suite", "2026-11-07");
        Assert.Equal((4500, 4000, "ceiling", 630000L), (suite.RawBps, suite.AppliedBps, suite.Clamped, suite.SuggestedPaise));
        Assert.Contains("YLDR-1", suite.RestrictionCodes);
        var cal = await CalendarAsync(14);
        Assert.True(cal.Summary.HighDemandNights >= 1);
        Assert.True(cal.Summary.PendingNights > 0);
    }

    [Fact]
    public async Task Default_rate_plans_are_seeded_and_quotes_list_restriction_issues()
    {
        var cfg = await F.Queries.GetConfigAsync(1, F.Owner, Ct);
        Assert.Equal(["BAR", "CORP", "DAYUSE", "LONG7", "WKND"], cfg.Plans.Select(p => p.PlanCode).Order(StringComparer.Ordinal));
        Assert.Equal(3, cfg.Seasons.Count);
        Assert.Equal(2, cfg.Restrictions.Count);
        var corp = await F.Queries.QuoteAsync(1, F.Owner, "CORP", F.TypeIds["Classic"], "2026-10-27", 1, Ct);
        Assert.Equal(175000, corp.TotalPaise);
        var min = await F.Queries.QuoteAsync(1, F.Owner, "BAR", F.TypeIds["Classic"], "2026-11-07", 1, Ct);
        Assert.Contains(min.Issues, x => x.Contains("Minimum stay 2", StringComparison.Ordinal));
        var day = await F.Queries.QuoteAsync(1, F.Owner, "DAYUSE", F.TypeIds["Classic"], "2026-11-07", 1, Ct);
        Assert.Contains(day.Issues, x => x.Contains("Closed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Server_side_quote_port_for_P2_matches_the_query()
    {
        Hcc.Application.Yield.IYieldRateQuery port = F.Queries;
        var q = await port.QuoteAsync(1, "WKND", F.TypeIds["Deluxe"], new DateOnly(2026, 10, 24), 2, Ct);
        Assert.Equal(2, q.Rows.Count);
        Assert.Equal(q.Rows.Sum(r => r.RatePaise), q.TotalPaise);
    }

    [Fact]
    public async Task Forecast_input_changes_occupancy_and_the_suggested_rate()
    {
        var before = await NightAsync("Classic", "2026-10-27");
        Assert.Equal(200000, before.SuggestedPaise);
        await F.Service.SetForecastAsync(F.Ctx(F.Owner, "SetYieldForecast"), new SetYieldForecastRequest([new YieldForecastNight("2026-10-27", 20)]), Ct);
        var after = await NightAsync("Classic", "2026-10-27");
        Assert.Equal(87, after.Occupancy.Pct);
        Assert.Equal(220000, after.SuggestedPaise);
    }
}

[Trait("Category", "Sql")]
public sealed class YieldPublishSqlTests : YieldSqlTest
{
    [Fact]
    public async Task Owner_publish_writes_dated_tariffs_that_P2_quotes_use_and_existing_bookings_keep_agreed_rates()
    {
        var existing = Convert.ToInt64(await ScalarAsync("SELECT nightly_rate_paise FROM inv.reservation WHERE public_no = 'HCC-1041';"), System.Globalization.CultureInfo.InvariantCulture);
        var before = await P2QuoteAsync("Classic", "2026-10-31", "2026-11-01");
        Assert.Equal(200000, before.Nights[0].RatePaise);

        var res = await PublishAsync(F.Owner, "2026-10-24", "2026-11-06", AllTypes);
        Assert.True(res.Value.ChangedNights > 0);
        var quote = await P2QuoteAsync("Classic", "2026-10-31", "2026-11-01");
        Assert.Equal(240000, quote.Nights[0].RatePaise);
        Assert.Equal(180000, (await P2QuoteAsync("Classic", "2026-11-02", "2026-11-03")).Nights[0].RatePaise);
        Assert.Equal(630000, (await P2QuoteAsync("Suite", "2026-11-06", "2026-11-07")).Nights[0].RatePaise);
        Assert.Equal(450000, (await P2QuoteAsync("Suite", "2026-11-07", "2026-11-08")).Nights[0].RatePaise); // closing row: unchanged after the window
        Assert.Equal(existing, Convert.ToInt64(await ScalarAsync("SELECT nightly_rate_paise FROM inv.reservation WHERE public_no = 'HCC-1041';"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, (await CalendarAsync(14)).Summary.PendingNights);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM yld.publish;"));
        Assert.Equal(await CountAsync("SELECT COUNT(*) FROM yld.publish_row;"), await CountAsync("SELECT COUNT(*) FROM yld.owned_tariff;"));
        Assert.True(await CountAsync("SELECT COUNT(*) FROM yld.history WHERE area LIKE 'Publish%';") >= 1);
    }

    [Fact]
    public async Task Publish_is_idempotent_by_key_and_fails_when_nothing_changes()
    {
        var first = await PublishAsync(F.Owner, "2026-10-24", "2026-11-06", AllTypes, "yld-1");
        var again = await PublishAsync(F.Owner, "2026-10-24", "2026-11-06", AllTypes, "yld-1");
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.PublishId, again.Value.PublishId);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM yld.publish;"));
        Assert.Equal("VALIDATION", await CodeAsync(() => PublishAsync(F.Owner, "2026-10-24", "2026-11-06", AllTypes)));
    }

    [Fact]
    public async Task Partial_window_leaves_other_nights_unchanged()
    {
        await PublishAsync(F.Owner, "2026-10-31", "2026-10-31", ["Classic"]);
        Assert.Equal(240000, (await P2QuoteAsync("Classic", "2026-10-31", "2026-11-01")).Nights[0].RatePaise);
        Assert.Equal(200000, (await P2QuoteAsync("Classic", "2026-11-01", "2026-11-02")).Nights[0].RatePaise);
        Assert.Equal(200000, (await P2QuoteAsync("Classic", "2026-10-30", "2026-10-31")).Nights[0].RatePaise);
    }

    [Fact]
    public async Task Manual_tariff_schedule_is_the_base_but_yield_rows_are_not()
    {
        await ExecAsync($"INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES ({F.TypeIds["Classic"]}, '2026-10-27', 220000);");
        Assert.Equal(220000, (await NightAsync("Classic", "2026-10-27")).BasePaise);
        await PublishAsync(F.Owner, "2026-10-24", "2026-11-06", ["Classic"]);
        Assert.Equal(220000, (await NightAsync("Classic", "2026-10-31")).BasePaise);
        Assert.Equal(200000, (await NightAsync("Classic", "2026-10-26")).BasePaise);
    }

    [Fact]
    public async Task Publish_window_rules()
    {
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => PublishAsync(F.Owner, "2026-10-23", "2026-10-25", ["Classic"])));
        Assert.Equal("VALIDATION", await CodeAsync(() => PublishAsync(F.Owner, "2026-10-24", "2026-11-20", ["Classic"])));
        Assert.Equal("VALIDATION", await CodeAsync(() => PublishAsync(F.Owner, "2026-10-24", "2026-10-24", [])));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.PublishAsync(F.Ctx(F.Owner, "PublishYieldTariffs"), new PublishYieldRequest("2026-10-24", "2026-10-24", [999], null), Ct)));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM yld.publish;"));
    }
}

[Trait("Category", "Sql")]
public sealed class YieldApprovalSqlTests : YieldSqlTest
{
    private Task<Hcc.Application.Commands.CommandResult<YieldProposalResult>> ProposeAsync(int who, string from, string to, params string[] types)
        => F.Service.ProposeAsync(F.Ctx(who, "ProposeYieldPublish"), new ProposeYieldPublishRequest(from, to, Types(types), "Weekend demand"), Ct);

    private Task<Hcc.Application.Commands.CommandResult<YieldDecisionResult>> DecideAsync(int who, YieldProposalResult p, bool approve)
        => F.Service.DecideAsync(F.Ctx(who, "DecideYieldProposal"), new DecideYieldProposalRequest(p.ProposalId, p.ExpectedVersion, approve, null), Ct);

    [Fact]
    public async Task Manager_proposes_owner_approves_and_the_rates_are_published()
    {
        var p = (await ProposeAsync(F.Manager, "2026-10-30", "2026-11-01", "Classic")).Value;
        Assert.Equal("Pending", p.Status);
        Assert.Equal(200000, (await P2QuoteAsync("Classic", "2026-10-31", "2026-11-01")).Nights[0].RatePaise); // nothing live yet
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM yld.publish;"));
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => ProposeAsync(F.Manager, "2026-10-30", "2026-10-30", "Deluxe"))); // one pending request at a time
        Assert.Equal("FORBIDDEN", await CodeAsync(() => DecideAsync(F.Manager, p, true)));  // a Manager can never decide
        Assert.Equal("FORBIDDEN", await CodeAsync(() => ProposeAsync(F.Accountant, "2026-10-30", "2026-10-30", "Deluxe")));
        Assert.Equal("Pending", (await F.Queries.ListProposalsAsync(1, F.Owner, Ct)).Single().Status);

        var d = (await DecideAsync(F.Owner, p, true)).Value;
        Assert.Equal("Executed", d.Status);
        Assert.NotNull(d.PublishId);
        Assert.Equal(240000, (await P2QuoteAsync("Classic", "2026-10-31", "2026-11-01")).Nights[0].RatePaise);
        Assert.Equal("Executed", (await F.Queries.ListProposalsAsync(1, F.Owner, Ct)).Single().Status);
        Assert.Equal(p.ProposalId, Convert.ToInt64(await ScalarAsync("SELECT proposal_id FROM yld.publish;"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => DecideAsync(F.Owner, p, true)));
        Assert.Single(await F.Queries.ListPublishesAsync(1, F.Owner, Ct));
    }

    [Fact]
    public async Task Nobody_approves_their_own_proposal()
    {
        var p = (await ProposeAsync(F.Owner, "2026-10-30", "2026-11-01", "Classic")).Value;
        Assert.Equal("FORBIDDEN", await CodeAsync(() => DecideAsync(F.Owner, p, true)));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM yld.owned_tariff;"));
    }

    [Fact]
    public async Task Stale_or_rejected_proposals_do_not_publish()
    {
        var p = (await ProposeAsync(F.Manager, "2026-10-30", "2026-11-01", "Classic")).Value;
        await ExecAsync($"INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES ({F.TypeIds["Classic"]}, '2026-10-29', 210000);");
        Assert.Equal("Stale", (await F.Queries.ListProposalsAsync(1, F.Owner, Ct)).Single().Status);
        Assert.Equal("STALE_VERSION", await CodeAsync(() => DecideAsync(F.Owner, p, true)));
        var rejected = (await DecideAsync(F.Owner, p, false)).Value;
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM yld.owned_tariff;"));
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => DecideAsync(F.Owner, p, true)));
    }

    [Fact]
    public async Task Proposal_expires_on_the_clock_and_a_new_one_can_then_be_raised()
    {
        var p = (await ProposeAsync(F.Manager, "2026-10-30", "2026-10-31", "Suite")).Value;
        F.Clock.Now += TimeSpan.FromHours(25);
        Assert.Equal("Expired", (await F.Queries.ListProposalsAsync(1, F.Owner, Ct)).Single().Status);
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => DecideAsync(F.Owner, p, true)));
        var again = (await ProposeAsync(F.Manager, "2026-10-30", "2026-10-31", "Suite")).Value;
        Assert.NotEqual(p.ProposalId, again.ProposalId);
    }

    [Fact]
    public async Task Stale_expected_version_on_a_decision_is_refused()
    {
        var p = (await ProposeAsync(F.Manager, "2026-10-30", "2026-11-01", "Classic")).Value;
        var bad = p with { ExpectedVersion = Convert.ToBase64String(new byte[8]) };
        Assert.Equal("STALE_VERSION", await CodeAsync(() => DecideAsync(F.Owner, bad, true)));
    }
}

[Trait("Category", "Sql")]
public sealed class YieldRulesSqlTests : YieldSqlTest
{
    [Fact]
    public async Task Permissions_follow_the_RBAC_matrix()
    {
        foreach (var who in new[] { F.Manager, F.Reception, F.Accountant, F.TechAdmin, F.Cashier })
        {
            Assert.Equal("FORBIDDEN", await CodeAsync(() => PublishAsync(who, "2026-10-24", "2026-10-24", ["Classic"])));
            Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Service.SetDayOfWeekAsync(F.Ctx(who, "SetYieldDayOfWeek"), new SetYieldDayOfWeekRequest([0, 0, 0, 0, 0, 800, 1200], "AAAAAAAAAAA=", null), Ct)));
        }

        Assert.Equal("OK", await CodeAsync(() => F.Queries.GetConfigAsync(1, F.Manager, Ct)));
        Assert.Equal("OK", await CodeAsync(() => F.Queries.GetConfigAsync(1, F.Accountant, Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Queries.GetConfigAsync(1, F.Reception, Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Queries.GetConfigAsync(1, F.Cashier, Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Service.ProposeAsync(F.Ctx(F.TechAdmin, "ProposeYieldPublish"), new ProposeYieldPublishRequest("2026-10-30", "2026-10-30", Types("Suite"), null), Ct)));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM yld.publish;"));
    }

    [Fact]
    public async Task Seasons_add_update_with_version_priority_and_remove_with_history()
    {
        var add = (await F.Service.SaveSeasonAsync(F.Ctx(F.Owner, "SaveYieldSeason"), new SaveYieldSeasonRequest(null, null, false, "Test peak", "2026-11-06", "2026-11-06", 3000, [], 5, "demo"), Ct)).Value;
        Assert.Contains((await NightAsync("Suite", "2026-11-06")).Steps, x => x.Label == "Season · Test peak" && x.Bps == 3000);
        var cfg = await F.Queries.GetConfigAsync(1, F.Owner, Ct);
        var season = cfg.Seasons.Single(s => s.Name == "Test peak");
        Assert.Equal(add.Version, season.Version);
        Assert.Equal("STALE_VERSION", await CodeAsync(() => F.Service.SaveSeasonAsync(F.Ctx(F.Owner, "SaveYieldSeason"),
            new SaveYieldSeasonRequest(season.SeasonId, Convert.ToBase64String(new byte[8]), false, "Test peak", "2026-11-06", "2026-11-06", 2500, [], 5, null), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveSeasonAsync(F.Ctx(F.Owner, "SaveYieldSeason"), new SaveYieldSeasonRequest(null, null, false, "Bad", "2026-11-06", "2026-11-01", 1000, [], 1, null), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveSeasonAsync(F.Ctx(F.Owner, "SaveYieldSeason"), new SaveYieldSeasonRequest(null, null, false, "Bad", "2026-11-06", "2026-11-07", 20000, [], 1, null), Ct)));
        var upd = (await F.Service.SaveSeasonAsync(F.Ctx(F.Owner, "SaveYieldSeason"),
            new SaveYieldSeasonRequest(season.SeasonId, season.Version, false, "Test peak", "2026-11-06", "2026-11-06", 2500, Types("Classic"), 5, null), Ct)).Value;
        Assert.NotEqual(season.Version, upd.Version);
        await F.Service.SaveSeasonAsync(F.Ctx(F.Owner, "SaveYieldSeason"), new SaveYieldSeasonRequest(season.SeasonId, upd.Version, true, null, null, null, 0, null, null, "gone"), Ct);
        Assert.Equal(3, (await F.Queries.GetConfigAsync(1, F.Owner, Ct)).Seasons.Count);
        var history = (await F.Queries.GetConfigAsync(1, F.Owner, Ct)).History;
        Assert.Equal("removed", history[0].After);
        Assert.Equal("demo", history.Last(h => h.Area == "Season · Test peak").Reason);
    }

    [Fact]
    public async Task Day_of_week_guardrails_and_occupancy_rules_are_validated_and_versioned()
    {
        var cfg = await F.Queries.GetConfigAsync(1, F.Owner, Ct);
        var dow = (await F.Service.SetDayOfWeekAsync(F.Ctx(F.Owner, "SetYieldDayOfWeek"), new SetYieldDayOfWeekRequest([0, 0, 0, 0, 0, 800, 1200], cfg.SettingsVersion, null), Ct)).Value;
        Assert.Equal(800, (await NightAsync("Classic", "2026-10-30")).Steps.Single().Bps);
        Assert.Equal("STALE_VERSION", await CodeAsync(() => F.Service.SetGuardrailsAsync(F.Ctx(F.Owner, "SetYieldGuardrails"), new SetYieldGuardrailsRequest(-500, 4000, 5000, cfg.SettingsVersion, null), Ct)));
        await F.Service.SetGuardrailsAsync(F.Ctx(F.Owner, "SetYieldGuardrails"), new SetYieldGuardrailsRequest(-500, 4000, 5000, dow.Version!, null), Ct);
        var floor = await NightAsync("Classic", "2026-11-02");
        Assert.Equal(("floor", 190000L), (floor.Clamped, floor.SuggestedPaise));

        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveOccupancyRuleAsync(F.Ctx(F.Owner, "SaveYieldOccupancyRule"), new SaveYieldOccupancyRuleRequest(null, null, false, ">=", 80, 500, null), Ct))); // duplicate threshold
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveOccupancyRuleAsync(F.Ctx(F.Owner, "SaveYieldOccupancyRule"), new SaveYieldOccupancyRuleRequest(null, null, false, ">=", 70, -500, null), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveOccupancyRuleAsync(F.Ctx(F.Owner, "SaveYieldOccupancyRule"), new SaveYieldOccupancyRuleRequest(null, null, false, "<", 30, 500, null), Ct)));
        await F.Service.SaveOccupancyRuleAsync(F.Ctx(F.Owner, "SaveYieldOccupancyRule"), new SaveYieldOccupancyRuleRequest(null, null, false, ">=", 60, 500, null), Ct);
        Assert.Equal(210000, (await NightAsync("Classic", "2026-10-25")).SuggestedPaise);
    }

    [Fact]
    public async Task Plans_bar_is_fixed_and_other_plans_change_quotes()
    {
        var cfg = await F.Queries.GetConfigAsync(1, F.Owner, Ct);
        var bar = cfg.Plans.Single(p => p.PlanCode == "BAR");
        var corp = cfg.Plans.Single(p => p.PlanCode == "CORP");
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SavePlanAsync(F.Ctx(F.Owner, "SaveYieldPlan"), new SaveYieldPlanRequest("BAR", bar.Version, -500, null, null, null, null), Ct)));
        var saved = (await F.Service.SavePlanAsync(F.Ctx(F.Owner, "SaveYieldPlan"), new SaveYieldPlanRequest("CORP", corp.Version, -1000, 1, true, null, null), Ct)).Value;
        Assert.NotEqual(corp.Version, saved.Version);
        Assert.Equal(180000, (await F.Queries.QuoteAsync(1, F.Owner, "CORP", F.TypeIds["Classic"], "2026-10-27", 1, Ct)).TotalPaise);
        Assert.Equal("STALE_VERSION", await CodeAsync(() => F.Service.SavePlanAsync(F.Ctx(F.Owner, "SaveYieldPlan"), new SaveYieldPlanRequest("CORP", corp.Version, -900, 1, true, null, null), Ct)));
    }

    [Fact]
    public async Task Override_needs_a_reason_replaces_the_computed_rate_and_can_be_cleared()
    {
        var deluxe = F.TypeIds["Deluxe"];
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SetOverrideAsync(F.Ctx(F.Owner, "SetYieldOverride"), new SetYieldOverrideRequest(deluxe, "2026-10-27", 350000, ""), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SetOverrideAsync(F.Ctx(F.Owner, "SetYieldOverride"), new SetYieldOverrideRequest(deluxe, "2026-10-23", 350000, "past night"), Ct)));
        await F.Service.SetOverrideAsync(F.Ctx(F.Owner, "SetYieldOverride"), new SetYieldOverrideRequest(deluxe, "2026-10-27", 350000, "Corporate block"), Ct);
        var n = await NightAsync("Deluxe", "2026-10-27");
        Assert.Equal((350000L, 300000L), (n.SuggestedPaise, n.ComputedPaise));
        Assert.Contains("override", n.Explain, StringComparison.Ordinal);
        await F.Service.SetOverrideAsync(F.Ctx(F.Owner, "SetYieldOverride"), new SetYieldOverrideRequest(deluxe, "2026-10-27", null, "Block released"), Ct);
        Assert.Equal(300000, (await NightAsync("Deluxe", "2026-10-27")).SuggestedPaise);
        Assert.Equal("NOT_FOUND", await CodeAsync(() => F.Service.SetOverrideAsync(F.Ctx(F.Owner, "SetYieldOverride"), new SetYieldOverrideRequest(deluxe, "2026-10-27", null, "again"), Ct)));
    }

    [Fact]
    public async Task Restrictions_reach_P2_reservations_and_are_removed_again()
    {
        var deluxe = F.TypeIds["Deluxe"];
        var add = (await F.Service.SaveRestrictionAsync(F.Ctx(F.Owner, "SaveYieldRestriction"),
            new SaveYieldRestrictionRequest(null, null, false, deluxe, "2026-10-30", "2026-10-31", 3, false, "Expo", null), Ct)).Value;
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveRestrictionAsync(F.Ctx(F.Owner, "SaveYieldRestriction"),
            new SaveYieldRestrictionRequest(null, null, false, null, "2026-10-30", "2026-10-31", 1, false, null, null), Ct)));
        var quote = await P2QuoteAsync("Deluxe", "2026-10-30", "2026-10-31");
        Assert.Equal("MIN_STAY", quote.Violations.Single().Code);
        Assert.Empty((await P2QuoteAsync("Classic", "2026-10-30", "2026-10-31")).Violations);

        var cfg = await F.Queries.GetConfigAsync(1, F.Owner, Ct);
        var r = cfg.Restrictions.Single(x => x.Note == "Expo");
        Assert.Equal(add.Version, r.Version);
        Assert.Equal("STALE_VERSION", await CodeAsync(() => F.Service.SaveRestrictionAsync(F.Ctx(F.Owner, "SaveYieldRestriction"),
            new SaveYieldRestrictionRequest(r.RestrictionId, Convert.ToBase64String(new byte[8]), true, null, null, null, 1, false, null, null), Ct)));
        await F.Service.SaveRestrictionAsync(F.Ctx(F.Owner, "SaveYieldRestriction"), new SaveYieldRestrictionRequest(r.RestrictionId, r.Version, true, null, null, null, 1, false, null, "over"), Ct);
        Assert.Empty((await P2QuoteAsync("Deluxe", "2026-10-30", "2026-10-31")).Violations);
        Assert.Equal(2, (await F.Queries.GetConfigAsync(1, F.Owner, Ct)).Restrictions.Count);
    }

    [Fact]
    public async Task Rule_changes_write_history_and_audit()
    {
        await F.Service.SetForecastAsync(F.Ctx(F.Owner, "SetYieldForecast"), new SetYieldForecastRequest([new YieldForecastNight("2026-10-27", 5)]), Ct);
        Assert.True(await CountAsync("SELECT COUNT(*) FROM yld.history WHERE area = N'Forecast';") == 1);
        Assert.True(await CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action LIKE 'Yield:%';") >= 1);
    }
}
