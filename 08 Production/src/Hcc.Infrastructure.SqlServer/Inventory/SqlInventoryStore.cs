using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Errors;
using Hcc.Domain.Inventory;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Inventory;

/// <summary>SQL adapter for <see cref="IInventoryStore"/> (inventory snapshot, quotes, group blocks, time-based sweep).</summary>
public sealed class SqlInventoryStore(SqlConnectionFactory connections) : IInventoryStore
{
    public async Task<PropertyInfo> GetPropertyAsync(int propertyId, ICommandTransaction? tx = null, CancellationToken ct = default)
        => await WithScopeAsync(tx, async s =>
        {
            await using var q = s.Command("SELECT business_date, time_zone FROM inv.property WHERE property_id = @p;");
            q.Parameters.Int("@p", propertyId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw InventoryErrors.NotFound("Property");
            return new PropertyInfo(propertyId, rd.GetDate(0), rd.GetString(1));
        }, ct).ConfigureAwait(false);

    public async Task<InventorySnapshot> LoadSnapshotAsync(int propertyId, DateOnly from, DateOnly to, DateTime nowUtc, ICommandTransaction? tx = null,
        CancellationToken ct = default)
        => await WithScopeAsync(tx, s => LoadSnapshotAsync(s, propertyId, from, to, nowUtc, ct), ct).ConfigureAwait(false);

    public static async Task<InventorySnapshot> LoadSnapshotAsync(SqlScope s, int propertyId, DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(s);
        await using var q = s.Command("""
            DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
            IF @bd IS NULL THROW 52010, 'Property not found.', 1;
            DECLARE @wf DATE = @from, @wt DATE = @to;
            SELECT @wf = CASE WHEN MIN(b.s) < @wf THEN MIN(b.s) ELSE @wf END, @wt = CASE WHEN MAX(b.departure) > @wt THEN MAX(b.departure) ELSE @wt END
            FROM (SELECT CASE WHEN arrival < @bd THEN @bd ELSE arrival END AS s, departure FROM inv.group_block
                  WHERE property_id = @p AND status = 'Open' AND departure > @bd) AS b;
            SELECT @bd;
            SELECT room_type_id, code, max_occupancy, rack_rate_paise, sort_order FROM inv.room_type WHERE property_id = @p;
            SELECT r.room_id, r.room_no, r.room_type_id, t.code, t.max_occupancy FROM inv.room r JOIN inv.room_type t ON t.room_type_id = r.room_type_id
              WHERE r.property_id = @p;
            SELECT b.room_id, b.from_night, b.to_night, b.reason FROM inv.room_block b JOIN inv.room r ON r.room_id = b.room_id
              WHERE r.property_id = @p AND b.active = 1 AND (b.to_night IS NULL OR @wf < b.to_night) AND (b.from_night IS NULL OR @wt > b.from_night);
            SELECT reservation_id, room_id, arrival, departure, status, hold_until_utc, guest_name, group_block_id FROM inv.reservation
              WHERE property_id = @p AND room_id IS NOT NULL AND status IN ('Held','Confirmed','InHouse') AND arrival < @wt AND departure > @wf;
            SELECT group_block_id, code, name, arrival, departure, cutoff, status, row_ver FROM inv.group_block
              WHERE property_id = @p AND departure > @bd;
            SELECT gt.group_block_id, gt.room_type_id, gt.allotted, gt.picked, gt.released, gt.rate_paise FROM inv.group_block_type gt
              JOIN inv.group_block g ON g.group_block_id = gt.group_block_id WHERE g.property_id = @p AND g.departure > @bd ORDER BY gt.group_block_id, gt.room_type_id;
            SELECT code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note FROM inv.stay_restriction
              WHERE property_id = @p AND active = 1;
            """);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Date("@from", from);
        q.Parameters.Date("@to", to);
        try
        {
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            var bd = rd.GetDate(0);
            var types = new List<RoomTypeInfo>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                types.Add(new RoomTypeInfo(rd.GetInt32(0), rd.GetString(1), rd.GetByte(2), rd.GetInt64(3), rd.GetInt32(4)));
            var rooms = new List<RoomInfo>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                rooms.Add(new RoomInfo(rd.GetInt32(0), rd.GetString(1), rd.GetInt32(2), rd.GetString(3), rd.GetByte(4)));
            var blocks = new List<RoomBlockWindow>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                blocks.Add(new RoomBlockWindow(rd.GetInt32(0), rd.GetDateOrNull(1), rd.GetDateOrNull(2), rd.GetString(3)));
            var stays = new List<StayOnRoom>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                stays.Add(new StayOnRoom(rd.GetInt64(0), rd.GetInt32(1), rd.GetDate(2), rd.GetDate(3), rd.GetString(4), rd.GetUtcOrNull(5), rd.GetString(6), rd.GetInt64OrNull(7)));
            var groupRows = new List<(long Id, string Code, string Name, DateOnly A, DateOnly D, DateOnly Cutoff, string Status, byte[] Ver)>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                groupRows.Add((rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetDate(3), rd.GetDate(4), rd.GetDate(5), rd.GetString(6), (byte[])rd[7]));
            var groupTypes = new List<(long Id, GroupBlockTypeInfo Type)>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                groupTypes.Add((rd.GetInt64(0), new GroupBlockTypeInfo(rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetInt32(4), rd.GetInt64OrNull(5))));
            var restrictions = new List<StayRestriction>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                restrictions.Add(new StayRestriction(rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetInt32(1), rd.GetDate(2), rd.GetDate(3), rd.GetByte(4), rd.GetBoolean(5), rd.GetString(6)));
            var groups = groupRows.Select(g => new GroupBlockInfo(g.Id, g.Code, g.Name, g.A, g.D, g.Cutoff, g.Status,
                groupTypes.Where(t => t.Id == g.Id).Select(t => t.Type).ToList(), g.Ver)).ToList();
            return new InventorySnapshot(bd, DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), rooms, types, blocks, stays, groups, restrictions);
        }
        catch (SqlException ex) when (ex.Number == 52010)
        {
            throw InventoryErrors.NotFound("Property");
        }
    }

    public async Task<IReadOnlyList<(DateOnly EffectiveFrom, long RatePaise)>> GetTariffsAsync(int roomTypeId, ICommandTransaction? tx = null, CancellationToken ct = default)
        => await WithScopeAsync(tx, async s =>
        {
            await using var q = s.Command("SELECT effective_from, rate_paise FROM inv.room_type_tariff WHERE room_type_id = @t ORDER BY effective_from;");
            q.Parameters.Int("@t", roomTypeId);
            var list = new List<(DateOnly, long)>();
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((rd.GetDate(0), rd.GetInt64(1)));
            return (IReadOnlyList<(DateOnly, long)>)list;
        }, ct).ConfigureAwait(false);

    public async Task<QuoteRecord> InsertQuoteAsync(ICommandTransaction tx, QuoteDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var s = SqlScope.Of(tx, connections);
        var net = draft.Nights.Sum(n => n.RatePaise);
        var tax = draft.Nights.Sum(n => n.TaxPaise);
        long id;
        await using (var ins = s.Command("""
            INSERT bill.quote(property_id, principal_id, room_type_id, room_id, arrival, departure, adults, children, rate_plan,
              net_paise, tax_paise, total_paise, expires_utc, created_utc)
            OUTPUT INSERTED.quote_id VALUES (@p, @u, @t, @room, @a, @d, @ad, @ch, @plan, @net, @tax, @total, @exp, @now);
            """))
        {
            ins.Parameters.Int("@p", draft.PropertyId);
            ins.Parameters.Int("@u", draft.PrincipalId);
            ins.Parameters.Int("@t", draft.RoomTypeId);
            ins.Parameters.Int("@room", draft.RoomId);
            ins.Parameters.Date("@a", draft.Arrival);
            ins.Parameters.Date("@d", draft.Departure);
            ins.Parameters.Add("@ad", SqlDbType.TinyInt).Value = (byte)draft.Adults;
            ins.Parameters.Add("@ch", SqlDbType.TinyInt).Value = (byte)draft.Children;
            ins.Parameters.Add("@plan", SqlDbType.VarChar, 20).Value = draft.RatePlan;
            ins.Parameters.Big("@net", net);
            ins.Parameters.Big("@tax", tax);
            ins.Parameters.Big("@total", net + tax);
            ins.Parameters.Utc("@exp", draft.ExpiresUtc);
            ins.Parameters.Utc("@now", draft.CreatedUtc);
            id = (long)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        foreach (var n in draft.Nights)
        {
            await using var night = s.Command("INSERT bill.quote_night(quote_id, night, rate_paise, tax_bps, tax_paise) VALUES (@q, @n, @r, @b, @x);");
            night.Parameters.Big("@q", id);
            night.Parameters.Date("@n", n.Date);
            night.Parameters.Big("@r", n.RatePaise);
            night.Parameters.Int("@b", n.TaxBps);
            night.Parameters.Big("@x", n.TaxPaise);
            await night.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return await ReadQuoteAsync(s, draft.PropertyId, id, false, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Quote");
    }

    public Task<QuoteRecord?> GetQuoteForUpdateAsync(ICommandTransaction tx, int propertyId, long quoteId, CancellationToken ct = default)
        => ReadQuoteAsync(SqlScope.Of(tx, connections), propertyId, quoteId, true, ct);

    public async Task MarkQuoteUsedAsync(ICommandTransaction tx, long quoteId, long reservationId, CancellationToken ct = default)
    {
        await using var up = SqlScope.Of(tx, connections).Command("UPDATE bill.quote SET used_by_reservation_id = @r WHERE quote_id = @q AND used_by_reservation_id IS NULL;");
        up.Parameters.Big("@q", quoteId);
        up.Parameters.Big("@r", reservationId);
        if (await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw HccException.Validation("This quote was already used for a booking. Request a new quote.");
    }

    private static async Task<QuoteRecord?> ReadQuoteAsync(SqlScope s, int propertyId, long quoteId, bool forUpdate, CancellationToken ct)
    {
        await using var q = s.Command($"""
            SELECT room_type_id, room_id, arrival, departure, adults, children, rate_plan, net_paise, tax_paise, total_paise, expires_utc,
              used_by_reservation_id, row_ver FROM bill.quote {(forUpdate ? "WITH (UPDLOCK, ROWLOCK)" : "")} WHERE quote_id = @q AND property_id = @p;
            SELECT night, rate_paise, tax_bps, tax_paise FROM bill.quote_night WHERE quote_id = @q ORDER BY night;
            """);
        q.Parameters.Big("@q", quoteId);
        q.Parameters.Int("@p", propertyId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        var head = (Type: rd.GetInt32(0), Room: rd.IsDBNull(1) ? (int?)null : rd.GetInt32(1), A: rd.GetDate(2), D: rd.GetDate(3), Ad: (int)rd.GetByte(4),
            Ch: (int)rd.GetByte(5), Plan: rd.GetString(6), Net: rd.GetInt64(7), Tax: rd.GetInt64(8), Total: rd.GetInt64(9),
            Exp: DateTime.SpecifyKind(rd.GetDateTime(10), DateTimeKind.Utc), Used: rd.GetInt64OrNull(11), Ver: (byte[])rd[12]);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var nights = new List<PricedNight>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) nights.Add(new PricedNight(rd.GetDate(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetInt64(3)));
        return new QuoteRecord(quoteId, head.Type, head.Room, head.A, head.D, head.Ad, head.Ch, head.Plan, nights, head.Net, head.Tax, head.Total, head.Exp, head.Used, head.Ver);
    }

    public async Task<int> SweepAsync(int propertyId, int principalId, DateTime nowUtc, CancellationToken ct = default)
    {
        var changed = 0;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        // Due blocks first, then lapsed holds; one record per short transaction so the sweep never holds two reservations' locks.
        foreach (var blockId in await IdsAsync(conn, "SELECT group_block_id FROM inv.group_block g JOIN inv.property p ON p.property_id = g.property_id WHERE g.property_id = @p AND g.status = 'Open' AND g.cutoff < p.business_date ORDER BY group_block_id;", propertyId, nowUtc, ct).ConfigureAwait(false))
        {
            changed += await InOwnTransactionAsync(conn, async s =>
            {
                var r = await ReleaseAsync(s, propertyId, blockId, null, dueOnly: true, ct).ConfigureAwait(false);
                if (r is null) return 0;
                await AuditAsync(s, propertyId, principalId, "GroupBlockAutoRelease", r.Code, $"{r.Code} · cut-off passed; unpicked rooms released", ct).ConfigureAwait(false);
                return 1;
            }, ct).ConfigureAwait(false);
        }

        foreach (var resId in await IdsAsync(conn, "SELECT reservation_id FROM inv.reservation WHERE property_id = @p AND status = 'Held' AND hold_until_utc <= @now ORDER BY reservation_id;", propertyId, nowUtc, ct).ConfigureAwait(false))
        {
            changed += await InOwnTransactionAsync(conn, async s =>
            {
                await using var q = s.Command("""
                    SELECT room_id, room_type_id, arrival, departure, group_block_id, public_no FROM inv.reservation WITH (UPDLOCK, ROWLOCK)
                    WHERE reservation_id = @r AND status = 'Held' AND hold_until_utc <= @now;
                    """);
                q.Parameters.Big("@r", resId);
                q.Parameters.Utc("@now", nowUtc);
                StayAllocation alloc;
                string publicNo;
                await using (var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return 0;
                    alloc = new StayAllocation(rd.GetInt32(0), rd.GetInt32(1), rd.GetDate(2), rd.GetDate(3), rd.GetInt64OrNull(4));
                    publicNo = rd.GetString(5);
                }

                await SqlInventoryAllocator.ChangeAsync(s, propertyId, resId, alloc, null, returnToBlock: true, ct).ConfigureAwait(false);
                await using (var up = s.Command("UPDATE inv.reservation SET status = 'Expired', status_utc = @now WHERE reservation_id = @r;"))
                {
                    up.Parameters.Big("@r", resId);
                    up.Parameters.Utc("@now", nowUtc);
                    await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await AuditAsync(s, propertyId, principalId, "HoldExpired", publicNo, $"{publicNo} · hold expired; room released", ct).ConfigureAwait(false);
                return 1;
            }, ct).ConfigureAwait(false);
        }

        return changed;
    }

    public async Task<GroupBlockResult> CreateGroupBlockAsync(ICommandTransaction tx, GroupBlockDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var s = SqlScope.Of(tx, connections);
        var typeNames = new Dictionary<int, string>();
        await using (var q = s.Command("SELECT room_type_id, code FROM inv.room_type WHERE property_id = @p;"))
        {
            q.Parameters.Int("@p", draft.PropertyId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) typeNames[rd.GetInt32(0)] = rd.GetString(1);
        }

        foreach (var a in draft.Allotments)
            if (!typeNames.ContainsKey(a.RoomTypeId)) throw HccException.Validation("Unknown room type.");
        await using (var dup = s.Command("SELECT COUNT(*) FROM inv.group_block WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND code = @c;"))
        {
            dup.Parameters.Int("@p", draft.PropertyId);
            dup.Parameters.Add("@c", SqlDbType.VarChar, 20).Value = draft.Code;
            if ((int)(await dup.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) throw HccException.Validation($"Block code {draft.Code} is already used.");
        }

        long id;
        await using (var ins = s.Command("""
            INSERT inv.group_block(property_id, code, name, arrival, departure, cutoff, status) OUTPUT INSERTED.group_block_id
            VALUES (@p, @c, @n, @a, @d, @cut, 'Open');
            """))
        {
            ins.Parameters.Int("@p", draft.PropertyId);
            ins.Parameters.Add("@c", SqlDbType.VarChar, 20).Value = draft.Code;
            ins.Parameters.Text("@n", draft.Name, 200);
            ins.Parameters.Date("@a", draft.Arrival);
            ins.Parameters.Date("@d", draft.Departure);
            ins.Parameters.Date("@cut", draft.Cutoff);
            id = (long)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        // Counts before the allotment rows (an ensured count row must not already include this block).
        var deltas = new TypeNightDeltas();
        foreach (var a in draft.Allotments) deltas.Add(a.RoomTypeId, draft.Arrival, draft.Departure, held: a.Rooms);
        try
        {
            await deltas.ApplyAsync(s, draft.PropertyId, ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == InventorySql.TypeFull))
        {
            var night = ex.Errors.Cast<SqlError>().First(e => e.Number == InventorySql.TypeFull).Message.Split('|');
            var type = night.Length > 1 && int.TryParse(night[1], out var t) && typeNames.TryGetValue(t, out var name) ? name : "room";
            throw new HccException(HccErrorCode.NOT_AVAILABLE,
                $"Not enough {type} rooms are free to hold that many for these dates (full on {(night.Length > 2 ? night[2] : "a night")}).", null, ex);
        }

        foreach (var a in draft.Allotments)
        {
            await using var gt = s.Command("INSERT inv.group_block_type(group_block_id, room_type_id, allotted, rate_paise) VALUES (@b, @t, @n, @r);");
            gt.Parameters.Big("@b", id);
            gt.Parameters.Int("@t", a.RoomTypeId);
            gt.Parameters.Int("@n", a.Rooms);
            gt.Parameters.Big("@r", a.RatePaise);
            await gt.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var v = s.Command("SELECT row_ver FROM inv.group_block WHERE group_block_id = @b;");
        v.Parameters.Big("@b", id);
        var ver = (byte[])(await v.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return new GroupBlockResult(id, draft.Code, InventoryErrors.VersionToWire(ver), "Open");
    }

    public async Task<GroupBlockResult> ReleaseGroupBlockAsync(ICommandTransaction tx, int propertyId, long groupBlockId, byte[] expectedVersion,
        CancellationToken ct = default)
        => await ReleaseAsync(SqlScope.Of(tx, connections), propertyId, groupBlockId, expectedVersion, dueOnly: false, ct).ConfigureAwait(false)
            ?? throw InventoryErrors.NotFound("Group block");

    /// <summary>Releases every unpicked room of a block (manual release, or the sweep at cut-off). Block row → counts → allotment rows.</summary>
    private static async Task<GroupBlockResult?> ReleaseAsync(SqlScope s, int propertyId, long blockId, byte[]? expectedVersion, bool dueOnly, CancellationToken ct)
    {
        string code, status;
        DateOnly a, d;
        bool due;
        byte[] ver;
        await using (var q = s.Command("""
            SELECT g.code, g.status, g.arrival, g.departure, CASE WHEN g.cutoff < p.business_date THEN 1 ELSE 0 END, g.row_ver
            FROM inv.group_block g WITH (UPDLOCK, ROWLOCK) JOIN inv.property p ON p.property_id = g.property_id
            WHERE g.group_block_id = @b AND g.property_id = @p;
            """))
        {
            q.Parameters.Big("@b", blockId);
            q.Parameters.Int("@p", propertyId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            (code, status, a, d, due, ver) = (rd.GetString(0), rd.GetString(1), rd.GetDate(2), rd.GetDate(3), rd.GetInt32(4) == 1, (byte[])rd[5]);
        }

        if (dueOnly && (status != "Open" || !due)) return null;
        if (status != "Open") throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only an open block can release rooms.");
        if (expectedVersion is not null && !InventoryErrors.VersionEquals(ver, expectedVersion)) throw InventoryErrors.Stale("This group block");

        var types = new List<(int Type, int Remaining)>();
        await using (var q = s.Command("SELECT room_type_id, allotted - picked FROM inv.group_block_type WITH (UPDLOCK, ROWLOCK) WHERE group_block_id = @b ORDER BY room_type_id;"))
        {
            q.Parameters.Big("@b", blockId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) types.Add((rd.GetInt32(0), rd.GetInt32(1)));
        }

        var deltas = new TypeNightDeltas();
        foreach (var (type, remaining) in types.Where(t => t.Remaining > 0)) deltas.Add(type, a, d, held: -remaining);
        await deltas.ApplyAsync(s, propertyId, ct).ConfigureAwait(false);

        await using var up = s.Command("""
            UPDATE inv.group_block_type SET released = released + (allotted - picked), allotted = picked WHERE group_block_id = @b;
            UPDATE inv.group_block SET status = 'Released', released_utc = SYSUTCDATETIME() OUTPUT INSERTED.row_ver WHERE group_block_id = @b;
            """);
        up.Parameters.Big("@b", blockId);
        var newVer = (byte[])(await up.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return new GroupBlockResult(blockId, code, InventoryErrors.VersionToWire(newVer), "Released");
    }

    private static async Task<List<long>> IdsAsync(SqlConnection conn, string sql, int propertyId, DateTime nowUtc, CancellationToken ct)
    {
        await using var q = new SqlCommand(sql, conn);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Utc("@now", nowUtc);
        var ids = new List<long>();
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) ids.Add(rd.GetInt64(0));
        return ids;
    }

    private async Task<int> InOwnTransactionAsync(SqlConnection conn, Func<SqlScope, Task<int>> work, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        try
        {
            var n = await work(new SqlScope(conn, tx, connections)).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return n;
        }
        catch (Exception ex) when (ex is SqlException or HccException)
        {
            // A sweep step that loses a race (e.g. the hold was confirmed meanwhile) is simply skipped; the command re-checks.
            try
            {
                if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            return 0;
        }
    }

    private static async Task AuditAsync(SqlScope s, int propertyId, int principalId, string action, string target, string detail, CancellationToken ct)
    {
        await using var cmd = s.Command("INSERT plat.audit_event(property_id, principal_id, action, target, detail, operation) VALUES (@p, @u, @a, @t, @d, 'Sweep');");
        cmd.Parameters.Int("@p", propertyId);
        cmd.Parameters.Int("@u", principalId);
        cmd.Parameters.Add("@a", SqlDbType.VarChar, 60).Value = action;
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 60).Value = target;
        cmd.Parameters.Text("@d", detail, 1000);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> WithScopeAsync<T>(ICommandTransaction? tx, Func<SqlScope, Task<T>> work, CancellationToken ct)
    {
        if (tx is not null) return await work(SqlScope.Of(tx, connections)).ConfigureAwait(false);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await work(new SqlScope(conn, null, connections)).ConfigureAwait(false);
    }
}
