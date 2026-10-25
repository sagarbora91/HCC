using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Groups;

/// <summary>
/// SQL adapter of the Groups module (schema grp, migration 2200). Writes only grp.*; reads inv.group_block / inv.reservation /
/// inv.reservation_night (P2), gst.stay (P3), bill.folio / bill.charge / pay.receipt (P4) and tax.charge_gst (Wave 1 C).
/// </summary>
public sealed class SqlGroupStore(SqlConnectionFactory connections) : IGroupStore
{
    private const string GroupColumns = "group_id, property_id, code, name, group_block_id, organiser, billing_name, billing_contact, billing_phone, billing_email, "
        + "billing_address, gstin, attrition_allowance_bps, attrition_rate_bps, status, created_by, row_ver";

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The Groups SQL store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static DateOnly D(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static long? L(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt64(i);

    private static string V(SqlDataReader rd, int i) => SqlValues.RowVersionToWire((byte[])rd[i]);

    private async Task<T> RunAsync<T>(ICommandTransaction? tx, string sql, Action<SqlCommand> bind, Func<SqlCommand, Task<T>> run, CancellationToken ct)
    {
        if (tx is not null)
        {
            await using var cmd = Sql(tx).Command(sql);
            bind(cmd);
            return await run(cmd).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(sql, conn);
        bind(c2);
        return await run(c2).ConfigureAwait(false);
    }

    private static GroupRow ReadGroup(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), L(rd, 4), rd.GetString(5), rd.GetString(6),
        rd.GetString(7), S(rd, 8), S(rd, 9), S(rd, 10), S(rd, 11), rd.GetInt32(12), rd.GetInt32(13), rd.GetString(14), rd.GetInt32(15), V(rd, 16));

    private static async Task<GroupRow?> ReadGroupAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadGroup(rd) : null;
    }

    private static async Task<string> VersionAsync(SqlCommand cmd, string what, CancellationToken ct)
    {
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[];
        if (v is null) throw new HccException(HccErrorCode.STALE_VERSION, $"{what} was changed by someone else. Reload and try again.");
        return SqlValues.RowVersionToWire(v);
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<GroupRow> InsertGroupAsync(ICommandTransaction tx, NewGroup group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        var d = group.Details;
        await using var cmd = Sql(tx).Command($"""
            DECLARE @n BIGINT = NEXT VALUE FOR grp.seq_group_no;
            INSERT grp.group_account(property_id, code, name, group_block_id, organiser, billing_name, billing_contact, billing_phone, billing_email, billing_address,
              gstin, attrition_allowance_bps, attrition_rate_bps, status, created_by, created_utc)
            OUTPUT {string.Join(", ", GroupColumns.Split(", ").Select(c => "INSERTED." + c))}
            VALUES (@p, CONCAT('GRP-', @n), @name, @b, @org, @bn, @bc, @ph, @em, @ad, @gstin, @al, @ar, 'Open', @u, @utc);
            """);
        P(cmd, "@p", SqlDbType.Int, group.PropertyId);
        P(cmd, "@b", SqlDbType.BigInt, group.GroupBlockId);
        Bind(cmd, d);
        P(cmd, "@u", SqlDbType.Int, group.CreatedBy);
        P(cmd, "@utc", SqlDbType.DateTime2, group.CreatedUtc);
        return await ReadGroupAsync(cmd, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Group insert returned no row.");
    }

    private static void Bind(SqlCommand cmd, GroupDetails d)
    {
        P(cmd, "@name", SqlDbType.NVarChar, d.Name, 200);
        P(cmd, "@org", SqlDbType.NVarChar, d.Organiser, 120);
        P(cmd, "@bn", SqlDbType.NVarChar, d.BillingName, 200);
        P(cmd, "@bc", SqlDbType.NVarChar, d.BillingContact, 120);
        P(cmd, "@ph", SqlDbType.VarChar, d.BillingPhone, 20);
        P(cmd, "@em", SqlDbType.NVarChar, d.BillingEmail, 120);
        P(cmd, "@ad", SqlDbType.NVarChar, d.BillingAddress, 400);
        P(cmd, "@gstin", SqlDbType.Char, d.Gstin, 15);
        P(cmd, "@al", SqlDbType.Int, d.AllowanceBps);
        P(cmd, "@ar", SqlDbType.Int, d.AttritionRateBps);
    }

    public async Task<GroupRow?> LockGroupAsync(ICommandTransaction tx, long groupId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {GroupColumns} FROM grp.group_account WITH (UPDLOCK, HOLDLOCK) WHERE group_id = @g;");
        P(cmd, "@g", SqlDbType.BigInt, groupId);
        return await ReadGroupAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<string> UpdateGroupAsync(ICommandTransaction tx, long groupId, string expectedVersion, GroupDetails details, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        await using var cmd = Sql(tx).Command("""
            UPDATE grp.group_account SET name = @name, organiser = @org, billing_name = @bn, billing_contact = @bc, billing_phone = @ph, billing_email = @em,
              billing_address = @ad, gstin = @gstin, attrition_allowance_bps = @al, attrition_rate_bps = @ar, change_seq = change_seq + 1
            OUTPUT INSERTED.row_ver WHERE group_id = @g AND row_ver = @v;
            """);
        P(cmd, "@g", SqlDbType.BigInt, groupId);
        P(cmd, "@v", SqlDbType.Binary, SqlValues.RowVersionFromWire(expectedVersion), 8);
        Bind(cmd, details);
        return await VersionAsync(cmd, "The group", ct).ConfigureAwait(false);
    }

    public async Task<string> TouchGroupAsync(ICommandTransaction tx, long groupId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE grp.group_account SET change_seq = change_seq + 1 OUTPUT INSERTED.row_ver WHERE group_id = @g;");
        P(cmd, "@g", SqlDbType.BigInt, groupId);
        return await VersionAsync(cmd, "The group", ct).ConfigureAwait(false);
    }

    public async Task<string> SettleGroupAsync(ICommandTransaction tx, long groupId, string expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE grp.group_account SET status = 'Settled', settled_by = @u, settled_utc = @utc, change_seq = change_seq + 1
            OUTPUT INSERTED.row_ver WHERE group_id = @g AND row_ver = @v AND status = 'Open';
            """);
        P(cmd, "@g", SqlDbType.BigInt, groupId);
        P(cmd, "@v", SqlDbType.Binary, SqlValues.RowVersionFromWire(expectedVersion), 8);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        return await VersionAsync(cmd, "The group", ct).ConfigureAwait(false);
    }

    public Task<BlockRow?> BlockAsync(ICommandTransaction? tx, int propertyId, long groupBlockId, CancellationToken ct = default) =>
        RunAsync(tx, """
            SELECT group_block_id, property_id, code, name, arrival, departure, cutoff, status FROM inv.group_block WHERE group_block_id = @b AND property_id = @p;
            SELECT gt.room_type_id, rt.code, gt.allotted, gt.picked, gt.released, gt.rate_paise
              FROM inv.group_block_type gt JOIN inv.room_type rt ON rt.room_type_id = gt.room_type_id WHERE gt.group_block_id = @b ORDER BY rt.code;
            """, c => { P(c, "@b", SqlDbType.BigInt, groupBlockId); P(c, "@p", SqlDbType.Int, propertyId); }, async c =>
        {
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await ReadBlockAsync(rd, ct).ConfigureAwait(false);
        }, ct);

    /// <summary>Reads a block header result set followed by its type rows (consumes both result sets).</summary>
    private static async Task<BlockRow?> ReadBlockAsync(SqlDataReader rd, CancellationToken ct)
    {
        BlockRow? head = null;
        if (await rd.ReadAsync(ct).ConfigureAwait(false))
            head = new BlockRow(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), D(rd, 4), D(rd, 5), D(rd, 6), rd.GetString(7), []);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var types = new List<BlockTypeRow>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            types.Add(new BlockTypeRow(rd.GetInt32(0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetInt32(4), L(rd, 5)));
        return head is null ? null : head with { Types = types };
    }

    public async Task<long?> GroupOfBlockAsync(ICommandTransaction tx, long groupBlockId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT group_id FROM grp.group_account WITH (UPDLOCK, HOLDLOCK) WHERE group_block_id = @b;");
        P(cmd, "@b", SqlDbType.BigInt, groupBlockId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? id : null;
    }

    public async Task SetRuleAsync(ICommandTransaction tx, long groupId, long? reservationId, RouteCategory category, RouteTarget? target, int principalId, DateTime utc,
        CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(target is null
            ? "DELETE grp.routing_rule WHERE group_id = @g AND member_reservation_id = @r AND category = @c;"
            : """
              UPDATE grp.routing_rule SET target = @t, changed_by = @u, changed_utc = @utc
               WHERE group_id = @g AND category = @c AND (member_reservation_id = @r OR (@r IS NULL AND member_reservation_id IS NULL));
              IF @@ROWCOUNT = 0
                INSERT grp.routing_rule(group_id, member_reservation_id, category, target, changed_by, changed_utc) VALUES (@g, @r, @c, @t, @u, @utc);
              """);
        P(cmd, "@g", SqlDbType.BigInt, groupId);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        P(cmd, "@c", SqlDbType.VarChar, GroupCodes.Code(category), 8);
        P(cmd, "@t", SqlDbType.VarChar, target is { } t ? GroupCodes.Code(t) : null, 8);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ReservationFacts?> ReservationAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT r.reservation_id, r.status, r.group_block_id,
                   (SELECT m.group_id FROM grp.member m WHERE m.reservation_id = r.reservation_id),
                   (SELECT g.group_id FROM grp.group_account g WHERE g.group_block_id = r.group_block_id)
              FROM inv.reservation r WHERE r.reservation_id = @r AND r.property_id = @p;
            """);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new ReservationFacts(rd.GetInt64(0), rd.GetString(1), L(rd, 2), L(rd, 3), L(rd, 4)) : null;
    }

    public async Task AttachMemberAsync(ICommandTransaction tx, long groupId, long reservationId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("INSERT grp.member(group_id, reservation_id, attached_by, attached_utc) VALUES (@g, @r, @u, @utc);");
        P(cmd, "@g", SqlDbType.BigInt, groupId);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DetachMemberAsync(ICommandTransaction tx, long groupId, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("DELETE grp.member WHERE group_id = @g AND reservation_id = @r;");
        P(cmd, "@g", SqlDbType.BigInt, groupId);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> InsertRouteAsync(ICommandTransaction tx, NewRoute route, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var cmd = Sql(tx).Command("""
            INSERT grp.line_route(group_id, charge_id, reservation_id, seq, net_target, tax_target, source, reason, routed_by, routed_utc)
            OUTPUT INSERTED.seq
            SELECT @g, @c, @r, ISNULL((SELECT MAX(seq) FROM grp.line_route WITH (UPDLOCK, HOLDLOCK) WHERE charge_id = @c), 0) + 1, @n, @t, @s, @why, @u, @utc;
            """);
        P(cmd, "@g", SqlDbType.BigInt, route.GroupId);
        P(cmd, "@c", SqlDbType.BigInt, route.ChargeId);
        P(cmd, "@r", SqlDbType.BigInt, route.ReservationId);
        P(cmd, "@n", SqlDbType.VarChar, GroupCodes.Code(route.Net), 8);
        P(cmd, "@t", SqlDbType.VarChar, GroupCodes.Code(route.Tax), 8);
        P(cmd, "@s", SqlDbType.VarChar, route.Source, 8);
        P(cmd, "@why", SqlDbType.NVarChar, route.Reason, 200);
        P(cmd, "@u", SqlDbType.Int, route.RoutedBy);
        P(cmd, "@utc", SqlDbType.DateTime2, route.RoutedUtc);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<long> InsertEntryAsync(ICommandTransaction tx, NewEntry e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        await using var cmd = Sql(tx).Command("""
            INSERT grp.master_entry(group_id, kind, business_date, service_date, label, amount_paise, gst_category, tax_bps, itc, tax_paise, cgst_paise, sgst_paise, gst_rule,
              tender, reference, reservation_id, folio_id, folio_receipt_id, attrition_id, refund_of, principal_id, recorded_utc)
            OUTPUT INSERTED.entry_id
            VALUES (@g, @k, @bd, @sd, @l, @a, @cat, @bps, @itc, @tax, @cg, @sg, @rule, @ten, @ref, @r, @f, @fr, @at, @ro, @u, @utc);
            """);
        P(cmd, "@g", SqlDbType.BigInt, e.GroupId);
        P(cmd, "@k", SqlDbType.VarChar, e.Kind, 10);
        P(cmd, "@bd", SqlDbType.Date, e.BusinessDate.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@sd", SqlDbType.Date, e.ServiceDate?.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@l", SqlDbType.NVarChar, e.Label.Length <= 200 ? e.Label : e.Label[..200], 200);
        P(cmd, "@a", SqlDbType.BigInt, e.Amount.EnsureReceiptAmount().Value);
        P(cmd, "@cat", SqlDbType.VarChar, e.GstCategory, 8);
        P(cmd, "@bps", SqlDbType.Int, e.TaxBps);
        P(cmd, "@itc", SqlDbType.Bit, e.Itc);
        P(cmd, "@tax", SqlDbType.BigInt, e.Tax?.Value);
        P(cmd, "@cg", SqlDbType.BigInt, e.Cgst?.Value);
        P(cmd, "@sg", SqlDbType.BigInt, e.Sgst?.Value);
        P(cmd, "@rule", SqlDbType.NVarChar, e.GstRule, 80);
        P(cmd, "@ten", SqlDbType.VarChar, e.Tender, 16);
        P(cmd, "@ref", SqlDbType.NVarChar, e.Reference, 120);
        P(cmd, "@r", SqlDbType.BigInt, e.ReservationId);
        P(cmd, "@f", SqlDbType.BigInt, e.FolioId);
        P(cmd, "@fr", SqlDbType.BigInt, e.FolioReceiptId);
        P(cmd, "@at", SqlDbType.BigInt, e.AttritionId);
        P(cmd, "@ro", SqlDbType.BigInt, e.RefundOf);
        P(cmd, "@u", SqlDbType.Int, e.PrincipalId);
        P(cmd, "@utc", SqlDbType.DateTime2, e.RecordedUtc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<long> InsertAttritionAsync(ICommandTransaction tx, NewAttrition a, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        await using var cmd = Sql(tx).Command("""
            INSERT grp.attrition_request(group_id, status, net_paise, tax_paise, detail, group_version, reason, requested_by, requested_utc)
            OUTPUT INSERTED.attrition_id VALUES (@g, 'Pending', @n, @t, @d, @v, @why, @u, @utc);
            """);
        P(cmd, "@g", SqlDbType.BigInt, a.GroupId);
        P(cmd, "@n", SqlDbType.BigInt, a.Net.Value);
        P(cmd, "@t", SqlDbType.BigInt, a.Tax.Value);
        P(cmd, "@d", SqlDbType.NVarChar, a.DetailJson);
        P(cmd, "@v", SqlDbType.Binary, SqlValues.RowVersionFromWire(a.GroupVersion), 8);
        P(cmd, "@why", SqlDbType.NVarChar, a.Reason, 200);
        P(cmd, "@u", SqlDbType.Int, a.RequestedBy);
        P(cmd, "@utc", SqlDbType.DateTime2, a.RequestedUtc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private const string AttritionColumns = "attrition_id, group_id, status, net_paise, tax_paise, detail, group_version, reason, requested_by, decided_by";

    private static AttritionRow ReadAttrition(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), new Paise(rd.GetInt64(3)), new Paise(rd.GetInt64(4)),
        rd.GetString(5), V(rd, 6), rd.GetString(7), rd.GetInt32(8), rd.IsDBNull(9) ? null : rd.GetInt32(9));

    public async Task<long?> AttritionGroupAsync(ICommandTransaction tx, long attritionId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT group_id FROM grp.attrition_request WHERE attrition_id = @a;");
        P(cmd, "@a", SqlDbType.BigInt, attritionId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? id : null;
    }

    public async Task<AttritionRow?> LockAttritionAsync(ICommandTransaction tx, long attritionId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {AttritionColumns} FROM grp.attrition_request WITH (UPDLOCK, HOLDLOCK) WHERE attrition_id = @a;");
        P(cmd, "@a", SqlDbType.BigInt, attritionId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadAttrition(rd) : null;
    }

    public async Task DecideAttritionAsync(ICommandTransaction tx, long attritionId, string status, int principalId, DateTime utc, string? reason, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE grp.attrition_request SET status = @s, decided_by = @u, decided_utc = @utc, decision_reason = @why WHERE attrition_id = @a AND status = 'Pending';
            """);
        P(cmd, "@a", SqlDbType.BigInt, attritionId);
        P(cmd, "@s", SqlDbType.VarChar, status, 10);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        P(cmd, "@why", SqlDbType.NVarChar, reason, 200);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The attrition request");
    }

    private const string MembersCte = """
        DECLARE @m TABLE(reservation_id BIGINT PRIMARY KEY, src VARCHAR(8) NOT NULL);
        INSERT @m(reservation_id, src)
          SELECT r.reservation_id, 'block' FROM inv.reservation r JOIN grp.group_account g ON g.group_block_id = r.group_block_id
           WHERE g.group_id = @g AND g.property_id = @p;
        INSERT @m(reservation_id, src)
          SELECT gm.reservation_id, 'attached' FROM grp.member gm
           WHERE gm.group_id = @g AND NOT EXISTS (SELECT 1 FROM @m x WHERE x.reservation_id = gm.reservation_id);
        """;

    public Task<GroupSnapshot?> LoadAsync(ICommandTransaction? tx, int propertyId, long groupId, CancellationToken ct = default) =>
        RunAsync(tx, $"""
            SET NOCOUNT ON;
            {MembersCte}
            SELECT {GroupColumns} FROM grp.group_account WHERE group_id = @g AND property_id = @p;
            SELECT business_date FROM inv.property WHERE property_id = @p;
            SELECT b.group_block_id, b.property_id, b.code, b.name, b.arrival, b.departure, b.cutoff, b.status
              FROM inv.group_block b JOIN grp.group_account g ON g.group_block_id = b.group_block_id WHERE g.group_id = @g;
            SELECT gt.room_type_id, rt.code, gt.allotted, gt.picked, gt.released, gt.rate_paise
              FROM inv.group_block_type gt JOIN inv.room_type rt ON rt.room_type_id = gt.room_type_id
              JOIN grp.group_account g ON g.group_block_id = gt.group_block_id WHERE g.group_id = @g ORDER BY rt.code;
            SELECT member_reservation_id, category, target FROM grp.routing_rule WHERE group_id = @g ORDER BY rule_id;
            SELECT r.reservation_id, r.public_no, r.guest_name, r.status, r.arrival, r.departure, COALESCE(sr.room_no, rr.room_no), f.folio_id, m.src, r.nightly_rate_paise
              FROM @m m JOIN inv.reservation r ON r.reservation_id = m.reservation_id
              LEFT JOIN bill.folio f ON f.reservation_id = r.reservation_id
              LEFT JOIN inv.room rr ON rr.room_id = r.room_id
              OUTER APPLY (SELECT TOP 1 rm.room_no FROM gst.stay st JOIN gst.stay_segment sg ON sg.stay_id = st.stay_id JOIN inv.room rm ON rm.room_id = sg.room_id
                            WHERE st.reservation_id = r.reservation_id ORDER BY sg.seq DESC) sr
             ORDER BY r.public_no;
            SELECT f.reservation_id, c.folio_id, c.charge_id, c.kind, c.service_date, c.label, c.amount_paise, c.tax_bps, c.tax_paise, c.reverses_charge_id,
                   cg.category, cg.itc, cg.cgst_paise, cg.sgst_paise
              FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN @m m ON m.reservation_id = f.reservation_id
              LEFT JOIN tax.charge_gst cg ON cg.charge_id = c.charge_id
             ORDER BY c.charge_id;
            SELECT f.reservation_id, rc.receipt_id, rc.kind, rc.method, rc.amount_paise, rc.business_date, rc.refund_of, rc.reference
              FROM pay.receipt rc JOIN bill.folio f ON f.folio_id = rc.folio_id JOIN @m m ON m.reservation_id = f.reservation_id ORDER BY rc.receipt_id;
            SELECT lr.charge_id, lr.reservation_id, lr.seq, lr.net_target, lr.tax_target, lr.source, lr.reason
              FROM grp.line_route lr
             WHERE lr.group_id = @g AND lr.route_id = (SELECT MAX(x.route_id) FROM grp.line_route x WHERE x.group_id = @g AND x.charge_id = lr.charge_id);
            SELECT entry_id, kind, business_date, service_date, label, amount_paise, gst_category, tax_bps, itc, tax_paise, cgst_paise, sgst_paise, tender, reference,
                   reservation_id, folio_receipt_id, attrition_id, refund_of
              FROM grp.master_entry WHERE group_id = @g ORDER BY entry_id;
            SELECT {AttritionColumns} FROM grp.attrition_request WHERE group_id = @g ORDER BY attrition_id;
            SELECT n.reservation_id, n.night, n.rate_paise, n.tax_bps FROM inv.reservation_night n JOIN @m m ON m.reservation_id = n.reservation_id ORDER BY n.reservation_id, n.night;
            """, c => { P(c, "@g", SqlDbType.BigInt, groupId); P(c, "@p", SqlDbType.Int, propertyId); }, async c =>
        {
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return (GroupSnapshot?)null;
            var group = ReadGroup(rd);
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            var businessDate = D(rd, 0);
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var block = await ReadBlockAsync(rd, ct).ConfigureAwait(false);
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var rules = new List<RoutingRule>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                rules.Add(new RoutingRule(L(rd, 0), GroupCodes.ParseCategory(rd.GetString(1)), GroupCodes.ParseTarget(rd.GetString(2), allowFollow: true)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var members = new List<MemberRow>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                members.Add(new MemberRow(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), D(rd, 4), D(rd, 5), S(rd, 6), L(rd, 7), rd.GetString(8), L(rd, 9)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var charges = new List<MemberCharge>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var kind = BillingCodes.ParseChargeKind(rd.GetString(3));
                var tax = new Paise(rd.GetInt64(8));
                var line = new PostedLine(rd.GetInt64(2), kind, D(rd, 4), rd.GetString(5), new Paise(rd.GetInt64(6)), rd.GetInt32(7), tax, L(rd, 9));
                var hasGst = !rd.IsDBNull(10);
                var split = GstMath.Split(tax);
                charges.Add(new MemberCharge(rd.GetInt64(0), rd.GetInt64(1), line,
                    hasGst ? rd.GetString(10) : kind is ChargeKind.Room or ChargeKind.Discount ? GstCategories.Code(GstCategory.Room) : GstCategories.Code(GstCategories.OfLabel(line.Label)),
                    hasGst && rd.GetBoolean(11), hasGst ? new Paise(rd.GetInt64(12)) : split.Cgst, hasGst ? new Paise(rd.GetInt64(13)) : split.Sgst));
            }

            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var receipts = new List<MemberReceipt>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                receipts.Add(new MemberReceipt(rd.GetInt64(0), new PostedReceipt(rd.GetInt64(1), FolioTotals.ParseKind(rd.GetString(2)), rd.GetString(3), new Paise(rd.GetInt64(4)),
                    D(rd, 5), L(rd, 6), rd.GetString(7))));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var routes = new List<RouteRow>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                routes.Add(new RouteRow(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), GroupCodes.ParseTarget(rd.GetString(3)), GroupCodes.ParseTarget(rd.GetString(4)), rd.GetString(5), S(rd, 6)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var entries = new List<EntryRow>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                entries.Add(new EntryRow(rd.GetInt64(0), rd.GetString(1), D(rd, 2), rd.IsDBNull(3) ? null : D(rd, 3), rd.GetString(4), new Paise(rd.GetInt64(5)), S(rd, 6),
                    rd.IsDBNull(7) ? null : rd.GetInt32(7), rd.IsDBNull(8) ? null : rd.GetBoolean(8), P(rd, 9), P(rd, 10), P(rd, 11), S(rd, 12), S(rd, 13), L(rd, 14), L(rd, 15),
                    L(rd, 16), L(rd, 17)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var attritions = new List<AttritionRow>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) attritions.Add(ReadAttrition(rd));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var agreed = new List<AgreedNight>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) agreed.Add(new AgreedNight(rd.GetInt64(0), D(rd, 1), new Paise(rd.GetInt64(2)), rd.GetInt32(3)));
            return new GroupSnapshot(group, block, businessDate, rules, members, charges, receipts, routes, entries, attritions, agreed);
        }, ct);

    private static Paise? P(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : new Paise(rd.GetInt64(i));

    public Task<IReadOnlyList<GroupListRow>> ListAsync(int propertyId, CancellationToken ct = default) =>
        RunAsync<IReadOnlyList<GroupListRow>>(null, """
            SELECT g.group_id, g.code, g.name, g.status, g.group_block_id, b.code, b.arrival, b.departure,
                   (SELECT COUNT(*) FROM inv.reservation r WHERE g.group_block_id IS NOT NULL AND r.group_block_id = g.group_block_id)
                 + (SELECT COUNT(*) FROM grp.member m WHERE m.group_id = g.group_id),
                   g.row_ver
              FROM grp.group_account g LEFT JOIN inv.group_block b ON b.group_block_id = g.group_block_id
             WHERE g.property_id = @p ORDER BY g.group_id DESC;
            """, c => P(c, "@p", SqlDbType.Int, propertyId), async c =>
        {
            var list = new List<GroupListRow>();
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new GroupListRow(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), L(rd, 4), S(rd, 5), rd.IsDBNull(6) ? null : D(rd, 6),
                    rd.IsDBNull(7) ? null : D(rd, 7), rd.GetInt32(8), V(rd, 9)));
            return list;
        }, ct);

    public Task<long?> GroupOfFolioAsync(ICommandTransaction? tx, int propertyId, long folioId, CancellationToken ct = default) =>
        RunAsync(tx, """
            SELECT TOP (1) g.group_id
              FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              JOIN grp.group_account g ON g.group_block_id = r.group_block_id OR g.group_id = (SELECT m.group_id FROM grp.member m WHERE m.reservation_id = r.reservation_id)
             WHERE f.folio_id = @f AND r.property_id = @p;
            """, c => { P(c, "@f", SqlDbType.BigInt, folioId); P(c, "@p", SqlDbType.Int, propertyId); },
            async c => await c.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? (long?)id : null, ct);
}
