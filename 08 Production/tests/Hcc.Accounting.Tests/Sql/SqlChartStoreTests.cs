using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Masters;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using Microsoft.Data.SqlClient;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-1 lane acc1-chart on SQL Server (migrations 4200–4202): the seeded Tally groups and built-in ledgers equal
/// <see cref="ChartOfAccounts"/>; PERM-ACC-007 is the Owner's only; UpsertLedger / RetireLedger / UpsertLedgerGroup through the real command
/// runner and access policy with history rows; retire refused while mapped or with a (locked) balance.
/// </summary>
[Trait("Category", "Sql")]
public sealed class SqlChartStoreTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private SqlChartStore Store => new(db.Platform.Connections);

    private SqlAccessPolicy Policy => new(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);

    private ChartService Service => new(db.Platform.Commands, Store, db.Store, Policy, db.Clock);

    private ChartQueries Queries => new(Store, db.Store, Policy);

    private static string Reason(HccException ex) => ex.Details is not null && ex.Details.TryGetValue("reason", out var r) ? r.GetString()! : "";

    private async Task<List<object[]>> RowsAsync(string sql)
    {
        await using var conn = await db.Platform.Connections.OpenAsync(Ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var rd = await cmd.ExecuteReaderAsync(Ct);
        var list = new List<object[]>();
        while (await rd.ReadAsync(Ct))
        {
            var row = new object[rd.FieldCount];
            rd.GetValues(row);
            list.Add(row);
        }

        return list;
    }

    private static string Gst(LedgerGst g) => g switch { LedgerGst.Input => "input", LedgerGst.Output => "output", _ => "none" };

    [Fact]
    public async Task Seeded_groups_and_built_in_ledgers_equal_the_chart_of_accounts()
    {
        var groups = await RowsAsync("SELECT group_id, name, parent_id, nature, side, is_custom FROM acc.ledger_group WHERE property_id IS NULL ORDER BY sort_order;");
        Assert.Equal(ChartOfAccounts.Groups.Select(g => (g.Id, g.Name, g.Parent, g.Nature, g.Side, false)),
            groups.Select(r => ((string)r[0], (string)r[1], r[2] is DBNull ? null : (string)r[2], (string)r[3], (string)r[4], (bool)r[5])));
        var ledgers = await RowsAsync("SELECT code, name, group_id, is_control, gst, note FROM acc.ledger WHERE property_id IS NULL ORDER BY code;");
        Assert.Equal(ChartOfAccounts.All.Select(a => (a.Code, a.Name, a.GroupId, a.Control, Gst(a.Gst), a.Note)),
            ledgers.Select(r => ((string)r[0], (string)r[1], (string)r[2], (bool)r[3], (string)r[4], r[5] is DBNull ? null : (string)r[5])));
    }

    [Fact]
    public async Task Perm_acc_007_is_the_owners_alone_and_every_other_role_is_refused()
    {
        var grants = await RowsAsync("SELECT role_code, mode, scope FROM plat.role_grant WHERE permission_id = 'PERM-ACC-007';");
        Assert.Equal([("OWN", "A", "P")], grants.Select(r => ((string)r[0], (string)r[1], (string)r[2])));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.permission WHERE permission_id = 'PERM-ACC-007' AND is_money = 1;"));
        foreach (var role in new[] { "MGR", "ACC", "TAD", "CSH", "REC" })
        {
            await Fails(HccErrorCode.FORBIDDEN, () => Service.UpsertLedgerAsync(db.As(role), Key(), new UpsertLedgerRequest(true, "5131", "Role test", "indirect-expenses", null, null, null, null), Ct));
            await Fails(HccErrorCode.FORBIDDEN, () => Service.RetireLedgerAsync(db.As(role), Key(), new RetireLedgerRequest("4400", null), Ct));
            await Fails(HccErrorCode.FORBIDDEN, () => Service.UpsertGroupAsync(db.As(role), Key(), new UpsertLedgerGroupRequest(null, "Role group", "sales"), Ct));
        }

        Assert.False((await Queries.MasterAsync(db.As("ACC"), Ct)).CanEdit);
        Assert.True((await Queries.MasterAsync(db.As("OWN"), Ct)).CanEdit);
        await Fails(HccErrorCode.FORBIDDEN, () => Queries.MasterAsync(db.As("HKP"), Ct));
    }

    [Fact]
    public async Task Owner_adds_and_edits_ledgers_and_groups_with_history_and_a_built_in_back_to_defaults_has_no_row()
    {
        var own = db.As("OWN");
        var added = (await Service.UpsertLedgerAsync(own, Key(), new UpsertLedgerRequest(true, "5130", "Guest amenities (fictional)", "indirect-expenses", "none", null, "Toiletries", null), Ct)).Value;
        Assert.True(added.Created);
        Assert.Equal("created", Assert.Single(added.Changes).Field);
        var dup = await Fails(HccErrorCode.VALIDATION, () => Service.UpsertLedgerAsync(own, Key(), new UpsertLedgerRequest(true, "5130", "Other", "sales", null, null, null, null), Ct));
        Assert.Equal(MasterReasons.CodeTaken, Reason(dup));

        await Service.UpsertLedgerAsync(own, Key(), new UpsertLedgerRequest(false, "4100", "Laundry & other services", null, null, null, null, null), Ct);
        var master = await Queries.MasterAsync(own, Ct);
        var l4100 = master.Ledgers.Single(l => l.Code == "4100");
        Assert.Equal(("Laundry & other services", true, "Income", "Cr"), (l4100.Name, l4100.Overridden, l4100.Group, l4100.Nature));
        var l5130 = master.Ledgers.Single(l => l.Code == "5130");
        Assert.Equal((false, "Expense", "Toiletries"), (l5130.System, l5130.Group, l5130.Note));
        Assert.Equal(["line:food", "pos:sales"], master.Ledgers.Single(l => l.Code == "4200").MappedBy); // ACC-2 parity: line:food → 4200
        Assert.Empty(master.Problems);

        var nature = await Fails(HccErrorCode.VALIDATION, () => Service.UpsertLedgerAsync(own, Key(), new UpsertLedgerRequest(false, "4100", null, "indirect-expenses", null, null, null, null), Ct));
        Assert.Contains("Income group", nature.Message, StringComparison.Ordinal);
        await Service.UpsertLedgerAsync(own, Key(), new UpsertLedgerRequest(false, "4100", "Other services revenue", null, null, null, null, null), Ct);
        Assert.Equal(0, await db.LongAsync($"SELECT COUNT(*) FROM acc.ledger WHERE property_id = {db.PropertyId} AND code = '4100';"));
        Assert.False((await Queries.MasterAsync(own, Ct)).Ledgers.Single(l => l.Code == "4100").Overridden);

        var g = (await Service.UpsertGroupAsync(own, Key(), new UpsertLedgerGroupRequest(null, "Banquet income", "sales"), Ct)).Value;
        Assert.Equal(("g-banquet-income", "sales", true), (g.Id, g.Parent, g.Created));
        var fixedGroup = await Fails(HccErrorCode.VALIDATION, () => Service.UpsertGroupAsync(own, Key(), new UpsertLedgerGroupRequest("sales", "Revenue", null), Ct));
        Assert.Equal(MasterReasons.Invalid, Reason(fixedGroup));
        await Service.UpsertLedgerAsync(own, Key(), new UpsertLedgerRequest(false, "4400", null, "g-banquet-income", null, null, null, null), Ct);
        master = await Queries.MasterAsync(own, Ct);
        Assert.Equal(("g-banquet-income", "Income"), (master.Ledgers.Single(l => l.Code == "4400").GroupId, master.Ledgers.Single(l => l.Code == "4400").Group));
        var gv = master.Groups.Single(x => x.Id == "g-banquet-income");
        Assert.Equal((true, "Cr", "PL", "Income"), (gv.Custom, gv.Nature, gv.Side, gv.LegacyGroup));
        Assert.Equal(["Sales Accounts", "Banquet income"], gv.Path);

        var history = (await Queries.HistoryAsync(own, Ct)).Where(h => h.Code is "5130" or "4100" or "4400" or "g-banquet-income").ToList(); // the retire test of this fixture adds 1540 rows
        Assert.Equal([("ledger", "5130", "created"), ("ledger", "4100", "name"), ("ledger", "4100", "name"), ("group", "g-banquet-income", "created"), ("ledger", "4400", "group")],
            history.Select(h => (h.Kind, h.Code, h.Field)));
        Assert.All(history, h => Assert.Equal(db.Users["OWN"], h.Actor));
    }

    [Fact]
    public async Task Retire_is_refused_while_mapped_or_with_a_locked_balance_and_an_unused_ledger_retires()
    {
        var own = db.As("OWN");
        var mapped = await Fails(HccErrorCode.INVALID_TRANSITION, () => Service.RetireLedgerAsync(own, Key(), new RetireLedgerRequest("4000", null), Ct));
        Assert.Equal(MasterReasons.InUse, Reason(mapped));
        Assert.Contains("line:room", mapped.Message, StringComparison.Ordinal);
        var structural = await Fails(HccErrorCode.INVALID_TRANSITION, () => Service.RetireLedgerAsync(own, Key(), new RetireLedgerRequest("1400", null), Ct));
        Assert.Contains("structural", structural.Message, StringComparison.Ordinal);
        await Fails(HccErrorCode.NOT_FOUND, () => Service.RetireLedgerAsync(own, Key(), new RetireLedgerRequest("7777", null), Ct));

        var r = (await Service.RetireLedgerAsync(own, Key(), new RetireLedgerRequest("1540", "No vehicles (fictional)"), Ct)).Value;
        Assert.False(r.Active);
        Assert.Equal("retired · No vehicles (fictional)", Assert.Single(r.Changes).After);
        Assert.False((await Queries.MasterAsync(own, Ct)).Ledgers.Single(l => l.Code == "1540").Active);
        var again = await Fails(HccErrorCode.INVALID_TRANSITION, () => Service.RetireLedgerAsync(own, Key(), new RetireLedgerRequest("1540", null), Ct));
        Assert.Equal(MasterReasons.Retired, Reason(again));
        await Service.UpsertLedgerAsync(own, Key(), new UpsertLedgerRequest(false, "1540", null, null, null, null, null, true), Ct);
        Assert.True((await Queries.MasterAsync(own, Ct)).Ledgers.Single(l => l.Code == "1540").Active);

        // 5400 takes an expense while "expense:Gas cylinders" points at it; the closed date is locked, then the mapping is reset:
        // the locked snapshot keeps 5400's debit, so retiring it is refused for the balance (live derivation alone would show zero).
        var d = new DateOnly(2026, 9, 3);
        await db.PublishAsync(AccountingEvents.ExpensePosted, 9301, new ExpensePostedEvent(9301, d, "Gas cylinders", "UPI", 168_000, "Demo Gas Agency", null));
        await db.ProjectAsync();
        await db.Accounting.SetMappingAsync(own, Key(), new SetLedgerMappingRequest("expense:Gas cylinders", "5400"), Ct);
        await db.Accounting.LockAsync(own, Key(), new LockPeriodRequest(d), Ct);
        await db.Accounting.ResetMappingAsync(own, Key(), new ResetLedgerMappingRequest("expense:Gas cylinders"), Ct);
        var usage = await ChartService.UsageAsync(db.Store, null, db.PropertyId, "5400", Ct);
        Assert.Equal((0, 168_000L), (usage.MappedBy.Count, usage.OwnBalance));
        var balance = await Fails(HccErrorCode.INVALID_TRANSITION, () => Service.RetireLedgerAsync(own, Key(), new RetireLedgerRequest("5400", null), Ct));
        Assert.Equal(MasterReasons.InUse, Reason(balance));
        Assert.Contains("balance of ₹1,680 Dr", balance.Message, StringComparison.Ordinal);
    }
}
