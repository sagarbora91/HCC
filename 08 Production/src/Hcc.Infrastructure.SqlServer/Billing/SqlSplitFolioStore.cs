using System.Data;
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Billing;

/// <summary>
/// F8 split folio SQL adapter (DEC-152, SF-CONTRACT 2.3 / 3): <see cref="ISplitFolioStore"/> over bill.folio (4350) and bill.folio_route (4351),
/// the folio summaries (<see cref="ISplitFolioReader"/>) and the routing used by <see cref="SqlBillingStore.InsertChargeAsync"/>.
/// Every method that takes a transaction runs inside the command transaction. Lock order: folio rows (seq order), then company.
/// </summary>
public sealed class SqlSplitFolioStore(SqlConnectionFactory connections, IBillingStore billing, ICompanyCreditGuard credit) : ISplitFolioStore, ISplitFolioReader
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The split folio store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static DateTime Unspec(DateTime v) => DateTime.SpecifyKind(v, DateTimeKind.Unspecified);

    private const string FolioCols = "f.folio_id, f.folio_seq, f.payer_kind, f.payer_company_id, f.payer_customer_key, f.payer_name, f.payer_gstin, f.removed_utc";

    private static FolioRef ReadRef(SqlDataReader rd) => new(rd.GetInt64(0), Convert.ToInt32(rd.GetValue(1), CultureInfo.InvariantCulture), rd.GetString(2),
        rd.IsDBNull(3) ? null : rd.GetInt64(3), S(rd, 4), S(rd, 5), rd.IsDBNull(6) ? null : rd.GetString(6).Trim(), !rd.IsDBNull(7));

    /// <summary>The folios of the reservation (seq order) and the effective routes (highest bill.folio_route.seq per category), read through <paramref name="command"/>.</summary>
    internal static async Task<FolioRoutingSnapshot> ReadSnapshotAsync(Func<string, SqlCommand> command, long reservationId, bool lockFolios, CancellationToken ct)
    {
        var folios = new List<FolioRef>();
        await using (var cmd = command($"SELECT {FolioCols} FROM bill.folio f {(lockFolios ? "WITH (UPDLOCK, HOLDLOCK)" : "")} WHERE f.reservation_id = @r ORDER BY f.folio_seq;"))
        {
            P(cmd, "@r", SqlDbType.BigInt, reservationId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) folios.Add(ReadRef(rd));
        }

        var routes = new Dictionary<FolioRouteCategory, long>();
        await using (var cmd = command("""
            SELECT r.category, r.folio_id FROM bill.folio_route r
             WHERE r.reservation_id = @r AND r.seq = (SELECT MAX(x.seq) FROM bill.folio_route x WHERE x.reservation_id = r.reservation_id AND x.category = r.category);
            """))
        {
            P(cmd, "@r", SqlDbType.BigInt, reservationId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) routes[FolioRouting.Parse(rd.GetString(0))] = rd.GetInt64(1);
        }

        return new FolioRoutingSnapshot(folios, routes);
    }

    /// <summary>
    /// The folio a new charge lands on (SF-CONTRACT 3, server mechanism). A single-folio reservation costs one indexed existence read and keeps
    /// <c>c.FolioId</c>; only the primary folio (seq 1) of a reservation with more than one live folio is routed (a charge addressed to B-D is explicit by nature).
    /// </summary>
    internal static async Task<long> ResolveChargeFolioAsync(SqlCommandTransaction tx, NewCharge c, CancellationToken ct)
    {
        if (c.ExplicitFolio) return c.FolioId;
        long reservationId;
        await using (var q = tx.Command("""
            SELECT p.reservation_id FROM bill.folio p
             WHERE p.folio_id = @f AND p.folio_seq = 1
               AND EXISTS (SELECT 1 FROM bill.folio x WHERE x.reservation_id = p.reservation_id AND x.folio_seq > 1 AND x.removed_utc IS NULL);
            """))
        {
            P(q, "@f", SqlDbType.BigInt, c.FolioId);
            if (await q.ExecuteScalarAsync(ct).ConfigureAwait(false) is not long r) return c.FolioId;
            reservationId = r;
        }

        long? reversesFolio = null;
        if (c.ReversesChargeId is { } original)
        {
            await using var q = tx.Command("SELECT folio_id FROM bill.charge WHERE charge_id = @o;");
            P(q, "@o", SqlDbType.BigInt, original);
            reversesFolio = await q.ExecuteScalarAsync(ct).ConfigureAwait(false) as long?;
        }

        var snap = await ReadSnapshotAsync(tx.Command, reservationId, false, ct).ConfigureAwait(false);
        return FolioRouting.Resolve(snap, c.Kind, false, reversesFolio, null);
    }

    // ---- ISplitFolioStore ----

    public async Task<SplitFolioStay?> LockStayAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct)
    {
        var t = Sql(tx);
        // The primary folio is created when the reservation has none (a Confirmed booking before its first advance); then all folio rows are locked.
        await using (var ensure = t.Command("""
            IF EXISTS (SELECT 1 FROM inv.reservation WHERE reservation_id = @r AND property_id = @p)
               AND NOT EXISTS (SELECT 1 FROM bill.folio WITH (UPDLOCK, HOLDLOCK) WHERE reservation_id = @r AND folio_seq = 1)
              INSERT bill.folio(reservation_id) VALUES (@r);
            """))
        {
            P(ensure, "@r", SqlDbType.BigInt, reservationId);
            P(ensure, "@p", SqlDbType.Int, propertyId);
            await ensure.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        string status;
        bool groupMember;
        long? companyId;
        await using (var q = t.Command("""
            SELECT r.status, r.company_id,
                   CASE WHEN r.group_block_id IS NOT NULL OR EXISTS (SELECT 1 FROM grp.member m WHERE m.reservation_id = r.reservation_id) THEN 1 ELSE 0 END
              FROM inv.reservation r WHERE r.reservation_id = @r AND r.property_id = @p;
            """))
        {
            P(q, "@r", SqlDbType.BigInt, reservationId);
            P(q, "@p", SqlDbType.Int, propertyId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            status = rd.GetString(0);
            companyId = rd.IsDBNull(1) ? null : rd.GetInt64(1);
            groupMember = rd.GetInt32(2) == 1;
        }

        var snap = await ReadSnapshotAsync(t.Command, reservationId, true, ct).ConfigureAwait(false);
        return new SplitFolioStay(reservationId, status, groupMember, companyId, snap);
    }

    public async Task<SplitFolioCompany?> CompanyAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("SELECT name, gstin, active FROM grp.company_account WHERE company_id = @c AND property_id = @p;");
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new SplitFolioCompany(companyId, rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1).Trim(), rd.GetBoolean(2)) : null;
    }

    public async Task<bool> CreditGuestActiveAsync(ICommandTransaction tx, int propertyId, string customerKey, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("SELECT 1 FROM gst.guest_credit_account WHERE property_id = @p AND customer_key = @k AND active = 1;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@k", SqlDbType.VarChar, customerKey, 40);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task<long> InsertFolioAsync(ICommandTransaction tx, long reservationId, int seq, string payerKind, long? companyId, string? customerKey, string? payerName,
        string? payerGstin, int by, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("""
            INSERT bill.folio(reservation_id, folio_seq, payer_kind, payer_company_id, payer_customer_key, payer_name, payer_gstin, opened_by, opened_utc)
            OUTPUT INSERTED.folio_id VALUES (@r, @s, @k, @c, @ck, @n, @g, @by, @u);
            """);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        P(cmd, "@s", SqlDbType.TinyInt, (byte)seq);
        P(cmd, "@k", SqlDbType.VarChar, payerKind, 8);
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        P(cmd, "@ck", SqlDbType.NVarChar, customerKey, 80);
        P(cmd, "@n", SqlDbType.NVarChar, payerName, 200);
        P(cmd, "@g", SqlDbType.Char, payerGstin, 15);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@u", SqlDbType.DateTime2, Unspec(utc));
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task InsertRouteAsync(ICommandTransaction tx, long reservationId, FolioRouteCategory category, long folioId, string? reason, int by, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("""
            INSERT bill.folio_route(reservation_id, category, folio_id, seq, reason, changed_by, changed_utc)
            VALUES (@r, @c, @f, ISNULL((SELECT MAX(x.seq) FROM bill.folio_route x WITH (UPDLOCK, HOLDLOCK) WHERE x.reservation_id = @r AND x.category = @c), 0) + 1, @why, @by, @u);
            """);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        P(cmd, "@c", SqlDbType.VarChar, FolioRouting.Code(category), 8);
        P(cmd, "@f", SqlDbType.BigInt, folioId);
        P(cmd, "@why", SqlDbType.NVarChar, reason, 200);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@u", SqlDbType.DateTime2, Unspec(utc));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<(int Charges, int Receipts, bool Invoiced)> FolioUseAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT (SELECT COUNT(*) FROM bill.charge WHERE folio_id = @f), (SELECT COUNT(*) FROM pay.receipt WHERE folio_id = @f),
                   CASE WHEN EXISTS (SELECT 1 FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = @f) THEN 1 ELSE 0 END;
            """);
        P(cmd, "@f", SqlDbType.BigInt, folioId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2) == 1);
    }

    public async Task MarkRemovedAsync(ICommandTransaction tx, long folioId, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Sql(tx).Command("UPDATE bill.folio SET removed_utc = @u WHERE folio_id = @f AND removed_utc IS NULL;");
        P(cmd, "@f", SqlDbType.BigInt, folioId);
        P(cmd, "@u", SqlDbType.DateTime2, Unspec(utc));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<SplitFolioCredit> CompanyFolioCreditAsync(ICommandTransaction tx, int propertyId, long companyId, long reservationId, long folioId, CancellationToken ct)
    {
        var co = await credit.LockAsync(tx, propertyId, companyId, ct).ConfigureAwait(false);
        var exposure = await credit.ExposureAsync(tx, propertyId, companyId, ct).ConfigureAwait(false);
        var estimate = exposure.Of("stay", reservationId); // the facts SQL counts a stay with a live company folio by that folio only (F8-SF-DOMAIN block)
        return new SplitFolioCredit(co.Name, new Paise(co.CreditLimitPaise), exposure.Total - estimate, estimate);
    }

    // ---- ISplitFolioReader ----

    public async Task<IReadOnlyList<FolioSummaryDto>?> SummaryAsync(int propertyId, long reservationId, CancellationToken ct)
    {
        FolioRoutingSnapshot snap;
        await using (var conn = await connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var chk = connections.Command("SELECT 1 FROM inv.reservation WHERE reservation_id = @r AND property_id = @p;", conn))
            {
                P(chk, "@r", SqlDbType.BigInt, reservationId);
                P(chk, "@p", SqlDbType.Int, propertyId);
                if (await chk.ExecuteScalarAsync(ct).ConfigureAwait(false) is null) return null;
            }

            snap = await ReadSnapshotAsync(sql => connections.Command(sql, conn), reservationId, false, ct).ConfigureAwait(false);
        }

        if (snap.Folios.Count == 0) return [];
        var primary = snap.Folios.FirstOrDefault(f => f.Seq == FolioRouting.PrimarySeq)?.FolioId ?? snap.Folios[0].FolioId;
        var rows = new List<FolioSummaryDto>();
        foreach (var f in snap.Folios)
        {
            var read = await billing.ReadFolioAsync(f.FolioId, ct).ConfigureAwait(false);
            var fig = FolioStatement.Compute(read?.Lines ?? [], read?.Receipts ?? []);
            string? invoiceNo;
            await using (var conn = await connections.OpenAsync(ct).ConfigureAwait(false))
            await using (var cmd = connections.Command("SELECT TOP (1) invoice_no FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = @f ORDER BY revision DESC;", conn))
            {
                P(cmd, "@f", SqlDbType.BigInt, f.FolioId);
                invoiceNo = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            }

            var routes = new List<string>();
            foreach (var cat in new[] { FolioRouteCategory.Room, FolioRouteCategory.Extras })
            {
                var target = snap.Routes.TryGetValue(cat, out var t) && snap.Folios.Any(x => x.FolioId == t && !x.Removed) ? t : primary;
                if (target == f.FolioId && !f.Removed) routes.Add(FolioRouting.Code(cat));
            }

            rows.Add(new FolioSummaryDto(f.FolioId, f.Seq, f.Letter, f.PayerKind, f.PayerName, f.PayerGstin, f.CompanyId, f.CustomerKey, routes,
                fig.Net.Value, fig.Tax.Value, fig.Total.Value, fig.Paid.Value, fig.Deposit.Value, fig.Balance.Value, invoiceNo, f.Removed));
        }

        return rows;
    }
}
