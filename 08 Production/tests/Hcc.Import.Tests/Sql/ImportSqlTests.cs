using Hcc.Application.Billing;
using Hcc.Application.Payments;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Import;
using Hcc.Domain.Errors;
using Hcc.Import.Tests.Harness;

namespace Hcc.Import.Tests.Sql;

/// <summary>Every master validates, previews and commits through the real module stores; idempotency, dry run, permissions, setup-state rule.</summary>
[Trait("Category", "Sql")]
public class ImportSqlTests : IAsyncLifetime
{
    private ImportWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await ImportWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static readonly string[] Order = ["room-types", "rooms", "tariffs", "seasons", "staff", "ledgers", "opening-balances", "till-cash", "guests"];

    [Fact]
    public async Task Every_sample_master_validates_previews_and_commits_into_the_owning_modules()
    {
        var types = await _w.UploadAsync("owner", "room-types", Templates.Sample("room-types"));
        Assert.Equal(new ImportCounts(3, 0, 0, 0), types.Counts);
        Assert.True(types.CommitAllowed);
        var preview = await _w.Service.GetBatchAsync(_w.PropertyId, _w.Users["owner"], types.BatchId, Ct);
        Assert.Equal("STD", preview.Plan[0].Key);
        Assert.Contains("rack ₹2,800", preview.Plan[0].Detail, StringComparison.Ordinal);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.room_type"));   // nothing written before the commit
        var committed = await _w.CommitAsync("owner", types.BatchId);
        Assert.True(committed.Committed);
        // a materialised type-night of DLX gets the new rooms' capacity
        await _w.ExecAsync("INSERT inv.type_night_count(room_type_id, night, sellable, sold, held) SELECT room_type_id, '2026-11-01', 0, 0, 0 FROM inv.room_type WHERE code = 'DLX';");
        foreach (var m in Order.Skip(1)) await _w.ImportSampleAsync(m, m == "opening-balances" ? "2026-10-25" : null);

        Assert.Equal(280000, await _w.CountAsync("SELECT rack_rate_paise FROM inv.room_type WHERE code = 'STD'"));
        Assert.Equal(6, await _w.CountAsync("SELECT COUNT(*) FROM inv.room WHERE hk_status = 'Ready'"));
        Assert.Equal(2, await _w.CountAsync("SELECT sellable FROM inv.type_night_count"));
        Assert.Equal(520050, await _w.CountAsync("SELECT t.rate_paise FROM inv.room_type_tariff t JOIN inv.room_type r ON r.room_type_id = t.room_type_id WHERE r.code = 'DLX' AND t.effective_from = '2026-12-20'"));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM yld.season"));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM yld.season_type st JOIN yld.season s ON s.season_id = st.season_id WHERE s.name = N'Year-end'"));
        Assert.Equal(-1000, await _w.CountAsync("SELECT adj_bps FROM yld.season WHERE name = N'Monsoon saver'"));
        Assert.Equal(5, await _w.CountAsync("SELECT COUNT(*) FROM plat.app_user WHERE created_by IS NOT NULL"));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.credential c JOIN plat.app_user u ON u.user_id = c.user_id WHERE u.created_by IS NOT NULL"));
        Assert.Equal("ACC", (string?)await _w.ScalarAsync("SELECT r.role_code FROM plat.user_role r JOIN plat.app_user u ON u.user_id = r.user_id WHERE u.login_name = 'meena.accounts'"));
        Assert.Equal(4, await _w.CountAsync("SELECT COUNT(*) FROM imp.ledger"));
        Assert.Equal(162000000, await _w.CountAsync("SELECT debit_paise FROM imp.opening_set"));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM gst.guest WHERE id_last4 IS NULL AND id_type IS NULL"));

        var tb = await _w.Service.OpeningBalancesAsync(_w.PropertyId, _w.Users["owner"], Ct);
        Assert.True(tb.Balanced);
        Assert.Equal(new DateOnly(2026, 10, 25), tb.AsOfDate);
        Assert.Equal(7, tb.Rows.Count);
        Assert.Equal("Owner's capital", tb.Rows.Single(r => r.LedgerCode == "CAP01").LedgerName);
        Assert.Equal(105800000, tb.Rows.Single(r => r.LedgerCode == "CAP01").CreditPaise);

        // DEC-035: the cashier's first shift on the imported till carries the counted opening cash
        var tillId = (int)(await _w.ScalarAsync("SELECT till_id FROM pay.till WHERE code = 'FD-1'"))!;
        var cashier = new CashierService(_w.Platform.Commands, _w.Billing, _w.Policy, _w.Clock);
        var shift = await cashier.OpenShiftAsync(new Caller(_w.PropertyId, _w.Users["owner"]), "open-1", new OpenShiftRequest(tillId, null), Ct);
        Assert.Equal(2500000, shift.Value.OpeningFloatPaise);

        Assert.True(await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'IMPORT_COMMIT'") == 9);
        Assert.Equal(9, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ImportCommitted'"));
        var list = await _w.Service.ListAsync(_w.PropertyId, _w.Users["techadmin"], 50, Ct);
        Assert.All(list.Batches, b => Assert.Equal("Committed", b.Status));
    }

    [Fact]
    public async Task Pos_outlets_and_menu_import_through_the_pos_store_update_prices_and_reimport_unchanged()
    {
        // the menu needs the outlets first
        var early = await _w.UploadAsync("owner", "pos-menu", Templates.Sample("pos-menu"));
        Assert.False(early.CommitAllowed);
        Assert.Contains(early.Errors, e => e.Column == "outlets");

        var outlets = await _w.ImportSampleAsync("pos-outlets");
        Assert.Equal(new ImportCounts(2, 0, 0, 0), outlets.Counts);
        var menu = await _w.ImportSampleAsync("pos-menu");
        Assert.Equal(new ImportCounts(5, 0, 0, 0), menu.Counts);
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM pos.outlet"));
        Assert.Equal(6, await _w.CountAsync("SELECT COUNT(*) FROM pos.outlet_table"));
        Assert.Equal("room", (string?)await _w.ScalarAsync("SELECT kind FROM pos.outlet WHERE code = 'RS'"));
        Assert.Equal(5, await _w.CountAsync("SELECT COUNT(*) FROM pos.menu_item"));
        Assert.Equal(10, await _w.CountAsync("SELECT COUNT(*) FROM pos.menu_item_outlet"));
        Assert.Equal(26000, await _w.CountAsync("SELECT price_paise FROM pos.menu_item WHERE code = 'PNR'"));
        Assert.Equal(0, await _w.CountAsync("SELECT CAST(veg AS int) FROM pos.menu_item WHERE code = 'CHK'"));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'POS_OUTLET_CREATED'"));
        Assert.Equal(5, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'POS_MENU_SAVED'"));

        // same files again: nothing to change; a new price is an update, not a duplicate
        var again = await _w.UploadAsync("owner", "pos-menu", Templates.Sample("pos-menu"));
        Assert.Equal(new ImportCounts(0, 0, 5, 0), again.Counts);
        var repriced = await _w.UploadAsync("owner", "pos-menu", Templates.Sample("pos-menu").Replace("260.00", "275.00", StringComparison.Ordinal));
        Assert.Equal(new ImportCounts(0, 1, 4, 0), repriced.Counts);
        await _w.CommitAsync("owner", repriced.BatchId);
        Assert.Equal(27500, await _w.CountAsync("SELECT price_paise FROM pos.menu_item WHERE code = 'PNR'"));
        Assert.Equal(5, await _w.CountAsync("SELECT COUNT(*) FROM pos.menu_item"));

        // outlets are not money (Setup commit), menu prices are Owner-only: the Tech admin may only prepare
        var tad = await _w.UploadAsync("techadmin", "pos-menu", Templates.Sample("pos-menu"));
        await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("techadmin", tad.BatchId));
    }

    [Fact]
    public async Task Re_commit_is_idempotent_and_reimporting_the_same_files_changes_nothing()
    {
        var up = await _w.UploadAsync("owner", "room-types", Templates.Sample("room-types"));
        var first = await _w.Service.CommitAsync(_w.Ctx("owner", "same-key"), new CommitImportBatchRequest(up.BatchId), Ct);
        var replay = await _w.Service.CommitAsync(_w.Ctx("owner", "same-key"), new CommitImportBatchRequest(up.BatchId), Ct);
        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Value.Counts, replay.Value.Counts);
        var again = await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("owner", up.BatchId));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, again.Code);
        foreach (var m in Order.Skip(1)) await _w.ImportSampleAsync(m, m == "opening-balances" ? "2026-10-25" : null);

        var auditBefore = await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event");
        foreach (var m in Order)
        {
            var r = await _w.UploadAsync("owner", m, Templates.Sample(m), m == "opening-balances" ? "2026-10-25" : null);
            Assert.True(r.ErrorCount == 0, m + ": " + string.Join(" | ", r.Errors.Select(e => e.Message)));
            Assert.Equal(0, r.Counts.Create + r.Counts.Update + r.Counts.Remove);
            Assert.Equal("Nothing to change: every row is already in HCC.", r.CommitBlockedReason);
            var c = await _w.CommitAsync("owner", r.BatchId);
            Assert.True(c.Committed);
            Assert.Empty(c.UsersNeedingCredential);
        }

        // only the upload + commit summaries were audited; no master row was written again
        Assert.Equal(auditBefore + 18, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event"));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM inv.room_type"));
        Assert.Equal(6, await _w.CountAsync("SELECT COUNT(*) FROM inv.room"));
        Assert.Equal(5, await _w.CountAsync("SELECT COUNT(*) FROM plat.app_user WHERE created_by IS NOT NULL"));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM gst.guest"));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM pay.shift"));
    }

    [Fact]
    public async Task Room_type_update_and_opening_balance_replacement_are_previewed()
    {
        await _w.ImportSampleAsync("room-types");
        var r = await _w.UploadAsync("owner", "room-types", "code,name,maxOccupancy,rackRate,sortOrder\nDLX,Deluxe Room,3,4400,2\nFAM,Family Room,5,6000,4\n");
        Assert.Equal(new ImportCounts(1, 1, 0, 0), r.Counts);
        Assert.Contains("rack ₹4,200 → ₹4,400", r.Plan.Single(p => p.Key == "DLX").Detail, StringComparison.Ordinal);
        await _w.CommitAsync("owner", r.BatchId);
        Assert.Equal(440000, await _w.CountAsync("SELECT rack_rate_paise FROM inv.room_type WHERE code = 'DLX'"));

        await _w.ImportSampleAsync("ledgers");
        await _w.ImportSampleAsync("opening-balances", "2026-10-25");
        var o = await _w.UploadAsync("owner", "opening-balances", "ledgerCode,debit,credit\n1000,30000,\nCAP01,,30000\n", "2026-10-24");
        Assert.Equal(3, o.Counts.Update);   // 1000 and CAP01 changed + the as-of date
        Assert.Equal(5, o.Counts.Remove);
        await _w.CommitAsync("owner", o.BatchId);
        var tb = await _w.Service.OpeningBalancesAsync(_w.PropertyId, _w.Users["owner"], Ct);
        Assert.Equal(2, tb.Rows.Count);
        Assert.Equal(new DateOnly(2026, 10, 24), tb.AsOfDate);
        Assert.Equal(3000000, tb.DebitPaise);
    }

    [Fact]
    public async Task Unbalanced_opening_trial_balance_is_refused_and_nothing_is_written()
    {
        var r = await _w.UploadAsync("owner", "opening-balances", "ledgerCode,debit,credit\n1000,25000,\n1030,350000.25,\n2200,,375000\n", "2026-10-25");
        Assert.False(r.CommitAllowed);
        var e = Assert.Single(r.Errors);
        Assert.Equal(0, e.Row);
        Assert.Contains("total debits ₹3,75,000.25, total credits ₹3,75,000, difference ₹0.25", e.Message, StringComparison.Ordinal);
        Assert.Equal(37500025L, r.DebitPaise);
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("owner", r.BatchId));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
        Assert.True(ex.Details!.ContainsKey("errors"));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM imp.opening_set"));
        Assert.Equal("Uploaded", (string?)await _w.ScalarAsync($"SELECT status FROM imp.batch WHERE batch_id = {r.BatchId}"));
    }

    [Fact]
    public async Task Opening_balances_and_till_cash_are_refused_once_the_first_business_day_is_closed()
    {
        var early = await _w.UploadAsync("owner", "till-cash", Templates.Sample("till-cash"));
        await _w.CloseFirstDayAsync();
        var masters = await _w.Service.MastersAsync(_w.PropertyId, _w.Users["owner"], Ct);
        Assert.False(masters.SetupState);
        var ex = await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("owner", early.BatchId));
        Assert.Equal(HccErrorCode.DAY_CLOSED, ex.Code);
        var ob = await _w.UploadAsync("owner", "opening-balances", "ledgerCode,debit,credit\n1000,1,\n2200,,1\n", "2026-10-25");
        Assert.Contains(ob.Errors, x => x.Row == 0 && x.Message.Contains("first business day is already closed", StringComparison.Ordinal));
        Assert.Equal(HccErrorCode.DAY_CLOSED, (await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("owner", ob.BatchId))).Code);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM pay.till"));
        // ordinary masters stay importable on an operating property
        await _w.ImportSampleAsync("room-types");
        await _w.ImportSampleAsync("rooms");
        Assert.Equal(6, await _w.CountAsync("SELECT COUNT(*) FROM inv.room"));
    }

    [Fact]
    public async Task Dry_run_executes_everything_then_rolls_back()
    {
        await _w.ImportSampleAsync("room-types");
        var r = await _w.UploadAsync("techadmin", "rooms", Templates.Sample("rooms"));
        var dry = await _w.CommitAsync("techadmin", r.BatchId, dryRun: true);
        Assert.True(dry.DryRun);
        Assert.False(dry.Committed);
        Assert.Equal(6, dry.Counts.Create);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.room"));
        Assert.Equal("Uploaded", (string?)await _w.ScalarAsync($"SELECT status FROM imp.batch WHERE batch_id = {r.BatchId}"));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'IMPORT_ROOM'"));
        var dryStaff = await _w.UploadAsync("owner", "staff", Templates.Sample("staff"));
        Assert.Equal(5, (await _w.CommitAsync("owner", dryStaff.BatchId, dryRun: true)).UsersNeedingCredential.Count);
        Assert.Equal(5, await _w.CountAsync("SELECT COUNT(*) FROM plat.app_user"));
        var real = await _w.CommitAsync("owner", r.BatchId);
        Assert.True(real.Committed);
        Assert.Equal(6, await _w.CountAsync("SELECT COUNT(*) FROM inv.room"));
    }

    [Fact]
    public async Task Tech_admin_validates_and_previews_but_only_the_owner_commits()
    {
        await _w.ImportSampleAsync("room-types");
        var money = await _w.UploadAsync("techadmin", "tariffs", Templates.Sample("tariffs"));
        Assert.True(money.CommitAllowed);
        Assert.Equal(4, (await _w.Service.GetBatchAsync(_w.PropertyId, _w.Users["techadmin"], money.BatchId, Ct)).Counts.Create);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("techadmin", money.BatchId))).Code);
        var rooms = await _w.UploadAsync("techadmin", "rooms", Templates.Sample("rooms"));
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("techadmin", rooms.BatchId))).Code);
        foreach (var user in new[] { "manager", "reception", "accountant" })
        {
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.UploadAsync(user, "rooms", Templates.Sample("rooms")))).Code);
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync(user, rooms.BatchId))).Code);
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.Service.GetBatchAsync(_w.PropertyId, _w.Users[user], rooms.BatchId, Ct))).Code);
        }

        Assert.True((await _w.CommitAsync("owner", money.BatchId)).Committed);
        Assert.True((await _w.CommitAsync("owner", rooms.BatchId)).Committed);
        Assert.Equal(HccErrorCode.NOT_FOUND, (await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("owner", 999_999))).Code);
    }

    [Fact]
    public async Task Row_errors_and_references_to_other_masters_block_the_commit()
    {
        var rooms = await _w.UploadAsync("owner", "rooms", Templates.Sample("rooms"));
        Assert.Equal(6, rooms.ErrorCount);
        Assert.All(rooms.Errors, e => Assert.Contains("Import the room types file first", e.Message, StringComparison.Ordinal));
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("owner", rooms.BatchId))).Code);
        // the same batch becomes valid once its references exist (the preview re-validates against today's data)
        await _w.ImportSampleAsync("room-types");
        var again = await _w.Service.GetBatchAsync(_w.PropertyId, _w.Users["owner"], rooms.BatchId, Ct);
        Assert.Equal(0, again.ErrorCount);
        Assert.True(again.CommitAllowed);
        await _w.CommitAsync("owner", rooms.BatchId);
        var move = await _w.UploadAsync("owner", "rooms", "roomNo,roomType\n101,DLX\n");
        Assert.Contains("already a STD room", Assert.Single(move.Errors).Message, StringComparison.Ordinal);

        var staff = await _w.UploadAsync("owner", "staff", "loginName,displayName,roles,password\nasha,Asha,REC,secret1\n");
        Assert.Contains(staff.Errors, e => e.Column == "password");
        var guests = await _w.UploadAsync("owner", "guests", "name,phone\nAsha Rao,12345\nAsha Rao,9820011111\nasha rao,+91 98200 11111\n");
        Assert.Contains(guests.Errors, e => e is { Row: 2, Column: "phone" });
        Assert.Contains(guests.Errors, e => e is { Row: 4, Column: "phone" } && e.Message == "Duplicate of row 3.");
        var broken = await _w.UploadAsync("owner", "rooms", "roomNo,roomType\n\"101,STD\n");
        Assert.Contains("never closed", Assert.Single(broken.Errors).Message, StringComparison.Ordinal);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => _w.UploadAsync("owner", "suppliers", "a\n1\n"))).Code);
    }

    [Fact]
    public async Task Existing_users_seasons_and_used_tills_are_not_changed_by_import()
    {
        await _w.ImportSampleAsync("room-types");
        await _w.ImportSampleAsync("staff");
        await _w.ImportSampleAsync("seasons");
        var staff = await _w.UploadAsync("owner", "staff", "loginName,displayName,roles\npriya.reception,Priya K,REC\nowner,Owner,REC\n");
        Assert.Equal(2, staff.ErrorCount);
        var seasons = await _w.UploadAsync("owner", "seasons", "name,from,to,adjustmentPercent\nDiwali peak,2026-11-05,2026-11-13,+15\n");
        Assert.Contains("Yield screen", Assert.Single(seasons.Errors).Message, StringComparison.Ordinal);

        // a till that already has a shift with movements (not an import opening) is refused
        var billingTill = await new CashierService(_w.Platform.Commands, _w.Billing, _w.Policy, _w.Clock)
            .CreateTillAsync(new Caller(_w.PropertyId, _w.Users["owner"]), "till-1", new CreateTillRequest("FD-2", "Back office"), Ct);
        await new CashierService(_w.Platform.Commands, _w.Billing, _w.Policy, _w.Clock)
            .OpenShiftAsync(new Caller(_w.PropertyId, _w.Users["owner"]), "shift-1", new OpenShiftRequest(billingTill.Value.TillId, 100000), Ct);
        var used = await _w.UploadAsync("owner", "till-cash", "tillCode,tillName,openingCash\nfd-2,Back office,1000\n");
        Assert.Contains("already has cash history", Assert.Single(used.Errors).Message, StringComparison.Ordinal);
    }
}
