using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Billing;

/// <summary>
/// SQL Server implementation of <see cref="IBillingStore"/> (migration 0400 + the 0002 tables). Locks: UPDLOCK on the folio row
/// (all folio writers serialize), UPDLOCK + HOLDLOCK on the parent receipt for refunds (spike T06/C3) and on the room-night
/// business key (spike T07), UPDLOCK on the till row for every cash step. No insert-and-catch inside a transaction (§4 rule 8).
/// </summary>
public sealed class SqlBillingStore(SqlConnectionFactory connections) : IBillingStore
{
    private const string FolioColumns = """
        f.folio_id, r.property_id, r.reservation_id, r.status, r.arrival, r.departure, p.business_date, f.row_ver
        """;
    private const string LineColumns = "charge_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id";
    private const string ReceiptColumns = "receipt_id, kind, method, amount_paise, business_date, refund_of, reference";
    private const string ApprovalColumns = """
        approval_id, property_id, action, folio_id, shift_id, target_kind, target_id, amount_paise, target_version, expected_paise, counted_paise,
        reason, status, requested_by, decided_by, expires_utc, result_id, row_ver
        """;
    private const string ShiftSelect = """
        SELECT s.shift_id, s.till_id, t.property_id, s.business_date, s.status, s.opening_float_paise, s.expected_paise, s.counted_paise,
               s.submitted_by, s.approval_id, s.row_ver
        FROM pay.shift s {0} JOIN pay.till t ON t.till_id = s.till_id
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The billing store needs a SQL command transaction.");

    private static SqlCommand Cmd(ICommandTransaction tx, string sql) => Sql(tx).Command(sql);

    // ── folio ──

    public async Task<long> EnsureFolioAsync(ICommandTransaction tx, long reservationId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            DECLARE @f BIGINT = (SELECT folio_id FROM bill.folio WITH (UPDLOCK, HOLDLOCK) WHERE reservation_id = @r);
            IF @f IS NULL
            BEGIN
              INSERT bill.folio(reservation_id) VALUES (@r);
              SET @f = SCOPE_IDENTITY();
            END
            SELECT @f;
            """);
        cmd.Parameters.Add("@r", SqlDbType.BigInt).Value = reservationId;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<long?> FolioIdForReservationAsync(ICommandTransaction tx, long reservationId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT folio_id FROM bill.folio WHERE reservation_id = @r;");
        cmd.Parameters.Add("@r", SqlDbType.BigInt).Value = reservationId;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as long?;
    }

    public async Task<(long ReservationId, long FolioId)?> StayFolioAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT reservation_id, folio_id FROM gst.stay WHERE stay_id = @s AND property_id = @p;");
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = stayId;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? (rd.GetInt64(0), rd.GetInt64(1)) : null;
    }

    public async Task<Paise?> ReservationNightlyRateAsync(ICommandTransaction tx, long reservationId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT nightly_rate_paise FROM inv.reservation WHERE reservation_id = @r;");
        cmd.Parameters.Add("@r", SqlDbType.BigInt).Value = reservationId;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long v ? new Paise(v) : null;
    }

    public async Task<FolioHeader?> LockFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"""
            SELECT {FolioColumns}
            FROM bill.folio f WITH (UPDLOCK, HOLDLOCK)
            JOIN inv.reservation r ON r.reservation_id = f.reservation_id
            JOIN inv.property p ON p.property_id = r.property_id
            WHERE f.folio_id = @f;
            """);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        return await ReadFolioHeaderAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<string> TouchFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE bill.folio SET change_seq = change_seq + 1 OUTPUT INSERTED.row_ver WHERE folio_id = @f;");
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[] ?? throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        return SqlValues.RowVersionToWire(v);
    }

    public async Task<IReadOnlyList<PostedLine>> LinesAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT {LineColumns} FROM bill.charge WHERE folio_id = @f ORDER BY charge_id;");
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        return await ReadListAsync(cmd, ReadLine, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PostedReceipt>> ReceiptsAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT {ReceiptColumns} FROM pay.receipt WHERE folio_id = @f ORDER BY receipt_id;");
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        return await ReadListAsync(cmd, r => ReadReceipt(r, 0), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApprovalRow>> ApprovalsForFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT {ApprovalColumns} FROM bill.approval WHERE folio_id = @f ORDER BY approval_id;");
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        return await ReadListAsync(cmd, ReadApproval, ct).ConfigureAwait(false);
    }

    public async Task<PostedLine?> FindRoomNightAsync(ICommandTransaction tx, long folioId, DateOnly serviceDate, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"""
            SELECT {LineColumns} FROM bill.charge WITH (UPDLOCK, HOLDLOCK)
            WHERE folio_id = @f AND service_date = @d AND kind = 'room' AND reverses_charge_id IS NULL;
            """);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = serviceDate;
        return (await ReadListAsync(cmd, ReadLine, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<long> InsertChargeAsync(ICommandTransaction tx, NewCharge c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var cmd = Cmd(tx, """
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id, posted_utc, principal_id, approval_id)
            OUTPUT INSERTED.charge_id VALUES (@f, @k, @d, @l, @a, @b, @t, @rev, @u, @p, @apr);
            """);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = c.FolioId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 12).Value = BillingCodes.Code(c.Kind);
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = c.ServiceDate;
        cmd.Parameters.Add("@l", SqlDbType.NVarChar, 200).Value = c.Label;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = c.Amount.EnsureFieldRange().Value;
        cmd.Parameters.Add("@b", SqlDbType.Int).Value = c.TaxBps;
        cmd.Parameters.Add("@t", SqlDbType.BigInt).Value = c.Tax.Value;
        cmd.Parameters.Add("@rev", SqlDbType.BigInt).Value = (object?)c.ReversesChargeId ?? DBNull.Value;
        Utc(cmd, "@u", c.PostedUtc);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = c.PrincipalId;
        cmd.Parameters.Add("@apr", SqlDbType.BigInt).Value = (object?)c.ApprovalId ?? DBNull.Value;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<long> InsertReceiptAsync(ICommandTransaction tx, NewReceipt r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var cmd = Cmd(tx, """
            INSERT pay.receipt(folio_id, kind, method, amount_paise, refund_of, business_date, till_id, shift_id, reference, principal_id, recorded_utc, approval_id)
            OUTPUT INSERTED.receipt_id VALUES (@f, @k, @m, @a, @ro, @d, @till, @shift, @ref, @p, @u, @apr);
            """);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = r.FolioId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = FolioTotals.KindCode(r.Kind);
        cmd.Parameters.Add("@m", SqlDbType.VarChar, 16).Value = r.Tender;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = r.Amount.EnsureReceiptAmount().Value;
        cmd.Parameters.Add("@ro", SqlDbType.BigInt).Value = (object?)r.RefundOf ?? DBNull.Value;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = r.BusinessDate;
        cmd.Parameters.Add("@till", SqlDbType.Int).Value = (object?)r.TillId ?? DBNull.Value;
        cmd.Parameters.Add("@shift", SqlDbType.BigInt).Value = (object?)r.ShiftId ?? DBNull.Value;
        cmd.Parameters.Add("@ref", SqlDbType.NVarChar, 120).Value = r.Reference;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = r.PrincipalId;
        Utc(cmd, "@u", r.RecordedUtc);
        cmd.Parameters.Add("@apr", SqlDbType.BigInt).Value = (object?)r.ApprovalId ?? DBNull.Value;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<ReceiptLock?> LockReceiptAsync(ICommandTransaction tx, long receiptId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT folio_id, {ReceiptColumns} FROM pay.receipt WITH (UPDLOCK, HOLDLOCK) WHERE receipt_id = @r;");
        cmd.Parameters.Add("@r", SqlDbType.BigInt).Value = receiptId;
        return (await ReadListAsync(cmd, rd => new ReceiptLock(rd.GetInt64(0), ReadReceipt(rd, 1)), ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<Paise> RefundedAsync(ICommandTransaction tx, long receiptId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT ISNULL(SUM(amount_paise), 0) FROM pay.receipt WHERE refund_of = @r AND kind = 'refund';");
        cmd.Parameters.Add("@r", SqlDbType.BigInt).Value = receiptId;
        return new Paise((long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
    }

    // ── approvals ──

    public async Task<(long ApprovalId, string Version)> InsertApprovalAsync(ICommandTransaction tx, NewApproval a, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        await using var cmd = Cmd(tx, """
            INSERT bill.approval(property_id, action, folio_id, shift_id, target_kind, target_id, amount_paise, target_version, expected_paise, counted_paise,
                                 reason, status, requested_by, requested_utc, expires_utc)
            OUTPUT INSERTED.approval_id, INSERTED.row_ver
            VALUES (@p, @act, @f, @s, @tk, @tid, @a, @tv, @e, @c, @r, 'Pending', @u, @ru, @eu);
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = a.PropertyId;
        cmd.Parameters.Add("@act", SqlDbType.VarChar, 16).Value = BillingCodes.Code(a.Action);
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = (object?)a.FolioId ?? DBNull.Value;
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = (object?)a.ShiftId ?? DBNull.Value;
        cmd.Parameters.Add("@tk", SqlDbType.VarChar, 8).Value = a.TargetKind;
        cmd.Parameters.Add("@tid", SqlDbType.BigInt).Value = a.TargetId;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = a.Amount.Value;
        cmd.Parameters.Add("@tv", SqlDbType.Binary, 8).Value = a.TargetVersion is null ? DBNull.Value : SqlValues.RowVersionFromWire(a.TargetVersion);
        cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = (object?)a.Expected?.Value ?? DBNull.Value;
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = (object?)a.Counted?.Value ?? DBNull.Value;
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = a.Reason;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = a.RequestedBy;
        Utc(cmd, "@ru", a.RequestedUtc);
        Utc(cmd, "@eu", a.ExpiresUtc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), SqlValues.RowVersionToWire((byte[])rd[1]));
    }

    public Task<ApprovalRow?> PeekApprovalAsync(ICommandTransaction tx, long approvalId, CancellationToken ct) => ApprovalAsync(tx, approvalId, "", ct);

    public Task<ApprovalRow?> LockApprovalAsync(ICommandTransaction tx, long approvalId, CancellationToken ct) => ApprovalAsync(tx, approvalId, "WITH (UPDLOCK)", ct);

    private static async Task<ApprovalRow?> ApprovalAsync(ICommandTransaction tx, long approvalId, string hint, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT {ApprovalColumns} FROM bill.approval {hint} WHERE approval_id = @a;");
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = approvalId;
        return (await ReadListAsync(cmd, ReadApproval, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<string> UpdateApprovalAsync(ICommandTransaction tx, long approvalId, string expectedVersion, ApprovalChange c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var cmd = Cmd(tx, """
            UPDATE bill.approval SET status = @s,
              decided_by = COALESCE(@db, decided_by), decided_utc = COALESCE(@du, decided_utc), decision_reason = COALESCE(@dr, decision_reason),
              executed_by = COALESCE(@eb, executed_by), executed_utc = COALESCE(@eu, executed_utc), result_id = COALESCE(@rid, result_id)
            OUTPUT INSERTED.row_ver
            WHERE approval_id = @a AND row_ver = @v;
            """);
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 10).Value = c.Status.ToString();
        cmd.Parameters.Add("@db", SqlDbType.Int).Value = (object?)c.DecidedBy ?? DBNull.Value;
        UtcOrNull(cmd, "@du", c.DecidedUtc);
        cmd.Parameters.Add("@dr", SqlDbType.NVarChar, 200).Value = string.IsNullOrEmpty(c.DecisionReason) ? DBNull.Value : c.DecisionReason;
        cmd.Parameters.Add("@eb", SqlDbType.Int).Value = (object?)c.ExecutedBy ?? DBNull.Value;
        UtcOrNull(cmd, "@eu", c.ExecutedUtc);
        cmd.Parameters.Add("@rid", SqlDbType.BigInt).Value = (object?)c.ResultId ?? DBNull.Value;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = approvalId;
        cmd.Parameters.Add("@v", SqlDbType.Binary, 8).Value = SqlValues.RowVersionFromWire(expectedVersion);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[];
        SqlValues.EnsureVersionMatched(v is null ? 0 : 1, "The approval request");
        return SqlValues.RowVersionToWire(v!);
    }

    // ── property, tills, shifts ──

    public async Task<DateOnly?> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : null;
    }

    public async Task<bool> TillCodeExistsAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM pay.till WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND code = @c;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@c", SqlDbType.VarChar, 20).Value = code;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<TillRow> InsertTillAsync(ICommandTransaction tx, int propertyId, string code, string name, int principalId, DateTime createdUtc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            INSERT pay.till(property_id, code, name, created_by, created_utc)
            OUTPUT INSERTED.till_id, INSERTED.property_id, INSERTED.code, INSERTED.name, INSERTED.row_ver
            VALUES (@p, @c, @n, @u, @t);
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@c", SqlDbType.VarChar, 20).Value = code;
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = name;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
        Utc(cmd, "@t", createdUtc);
        return (await ReadListAsync(cmd, ReadTill, ct).ConfigureAwait(false)).Single();
    }

    public async Task<TillRow?> LockTillAsync(ICommandTransaction tx, int tillId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT till_id, property_id, code, name, row_ver FROM pay.till WITH (UPDLOCK, HOLDLOCK) WHERE till_id = @t;");
        cmd.Parameters.Add("@t", SqlDbType.Int).Value = tillId;
        return (await ReadListAsync(cmd, ReadTill, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<ShiftRow?> ActiveShiftAsync(ICommandTransaction tx, int tillId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, string.Format(System.Globalization.CultureInfo.InvariantCulture, ShiftSelect, "") + " WHERE s.till_id = @t AND s.status IN ('Open','Submitted');");
        cmd.Parameters.Add("@t", SqlDbType.Int).Value = tillId;
        return (await ReadListAsync(cmd, ReadShift, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<ShiftRow?> LastClosedShiftAsync(ICommandTransaction tx, int tillId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, string.Format(System.Globalization.CultureInfo.InvariantCulture, ShiftSelect, "").Replace("SELECT s.", "SELECT TOP (1) s.", StringComparison.Ordinal)
            + " WHERE s.till_id = @t AND s.status = 'Closed' ORDER BY s.shift_id DESC;");
        cmd.Parameters.Add("@t", SqlDbType.Int).Value = tillId;
        return (await ReadListAsync(cmd, ReadShift, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public Task<ShiftRow?> PeekShiftAsync(ICommandTransaction tx, long shiftId, CancellationToken ct) => ShiftAsync(tx, shiftId, "", ct);

    public Task<ShiftRow?> LockShiftAsync(ICommandTransaction tx, long shiftId, CancellationToken ct) => ShiftAsync(tx, shiftId, "WITH (UPDLOCK)", ct);

    private static async Task<ShiftRow?> ShiftAsync(ICommandTransaction tx, long shiftId, string hint, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, string.Format(System.Globalization.CultureInfo.InvariantCulture, ShiftSelect, hint) + " WHERE s.shift_id = @s;");
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = shiftId;
        return (await ReadListAsync(cmd, ReadShift, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<ShiftRow> InsertShiftAsync(ICommandTransaction tx, int tillId, DateOnly businessDate, Paise openingFloat, long? previousShiftId, int principalId,
        DateTime openedUtc, CancellationToken ct)
    {
        long id;
        await using (var cmd = Cmd(tx, """
            INSERT pay.shift(till_id, business_date, status, opening_float_paise, previous_shift_id, opened_by, opened_utc)
            OUTPUT INSERTED.shift_id VALUES (@t, @d, 'Open', @f, @prev, @u, @o);
            """))
        {
            cmd.Parameters.Add("@t", SqlDbType.Int).Value = tillId;
            cmd.Parameters.Add("@d", SqlDbType.Date).Value = businessDate;
            cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = openingFloat.EnsureFieldRange().Value;
            cmd.Parameters.Add("@prev", SqlDbType.BigInt).Value = (object?)previousShiftId ?? DBNull.Value;
            cmd.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
            Utc(cmd, "@o", openedUtc);
            id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        return (await PeekShiftAsync(tx, id, ct).ConfigureAwait(false))!;
    }

    public async Task<ShiftRow> UpdateShiftAsync(ICommandTransaction tx, long shiftId, string expectedVersion, ShiftUpdate u, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(u);
        await using (var cmd = Cmd(tx, """
            UPDATE pay.shift SET status = @st, expected_paise = @e, counted_paise = @c,
              submitted_by = COALESCE(@sb, submitted_by), submitted_utc = COALESCE(@su, submitted_utc), difference_reason = COALESCE(@r, difference_reason),
              approval_id = @a, closed_by = @cb, closed_utc = @cu
            WHERE shift_id = @s AND row_ver = @v;
            """))
        {
            cmd.Parameters.Add("@st", SqlDbType.VarChar, 10).Value = u.Status;
            cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = (object?)u.Expected?.Value ?? DBNull.Value;
            cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = (object?)u.Counted?.Value ?? DBNull.Value;
            cmd.Parameters.Add("@sb", SqlDbType.Int).Value = (object?)u.SubmittedBy ?? DBNull.Value;
            UtcOrNull(cmd, "@su", u.SubmittedUtc);
            cmd.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = (object?)u.Reason ?? DBNull.Value;
            cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = (object?)u.ApprovalId ?? DBNull.Value;
            cmd.Parameters.Add("@cb", SqlDbType.Int).Value = (object?)u.ClosedBy ?? DBNull.Value;
            UtcOrNull(cmd, "@cu", u.ClosedUtc);
            cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = shiftId;
            cmd.Parameters.Add("@v", SqlDbType.Binary, 8).Value = SqlValues.RowVersionFromWire(expectedVersion);
            SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The shift");
        }

        return (await PeekShiftAsync(tx, shiftId, ct).ConfigureAwait(false))!;
    }

    public async Task<long> InsertMovementAsync(ICommandTransaction tx, NewMovement m, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(m);
        await using var cmd = Cmd(tx, """
            INSERT pay.cash_movement(shift_id, till_id, business_date, kind, source_type, source_id, amount_paise, label, reference, principal_id, recorded_utc)
            OUTPUT INSERTED.movement_id VALUES (@s, @t, @d, @k, @st, @sid, @a, @l, @r, @p, @u);
            """);
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = m.ShiftId;
        cmd.Parameters.Add("@t", SqlDbType.Int).Value = m.TillId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = m.BusinessDate;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = CashDrawer.Code(m.Kind);
        cmd.Parameters.Add("@st", SqlDbType.VarChar, 16).Value = m.SourceType;
        cmd.Parameters.Add("@sid", SqlDbType.BigInt).Value = m.SourceId;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = m.Amount.Value;
        cmd.Parameters.Add("@l", SqlDbType.NVarChar, 200).Value = m.Label;
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 120).Value = m.Reference;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = m.PrincipalId;
        Utc(cmd, "@u", m.RecordedUtc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<MovementRow>> MovementsAsync(ICommandTransaction tx, long shiftId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, MovementSql);
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = shiftId;
        return await ReadListAsync(cmd, ReadMovement, ct).ConfigureAwait(false);
    }

    private const string MovementSql = "SELECT movement_id, kind, source_type, source_id, amount_paise, label, reference, business_date FROM pay.cash_movement WHERE shift_id = @s ORDER BY movement_id;";

    public async Task InsertShiftCountAsync(ICommandTransaction tx, long shiftId, Paise expected, Paise counted, string? denominationsJson, string? reason,
        long? approvalId, int principalId, DateTime submittedUtc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            INSERT pay.shift_count(shift_id, expected_paise, counted_paise, denominations, reason, approval_id, submitted_by, submitted_utc)
            VALUES (@s, @e, @c, @dn, @r, @a, @p, @u);
            """);
        cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = shiftId;
        cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = expected.Value;
        cmd.Parameters.Add("@c", SqlDbType.BigInt).Value = counted.Value;
        cmd.Parameters.Add("@dn", SqlDbType.NVarChar, 400).Value = (object?)denominationsJson ?? DBNull.Value;
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = (object?)reason ?? DBNull.Value;
        cmd.Parameters.Add("@a", SqlDbType.BigInt).Value = (object?)approvalId ?? DBNull.Value;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = principalId;
        Utc(cmd, "@u", submittedUtc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── queries ──

    public async Task<FolioSnapshot?> ReadFolioAsync(long folioId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        // One snapshot of header, lines, receipts and approvals (SNAPSHOT isolation is not enabled; a short serializable read keeps them consistent).
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        SqlCommand C(string sql)
        {
            var c = connections.Command(sql, conn, tx);
            c.Parameters.Add("@f", SqlDbType.BigInt).Value = folioId;
            return c;
        }

        FolioHeader? header;
        await using (var h = C($"""
            SELECT {FolioColumns} FROM bill.folio f
            JOIN inv.reservation r ON r.reservation_id = f.reservation_id JOIN inv.property p ON p.property_id = r.property_id
            WHERE f.folio_id = @f;
            """))
            header = await ReadFolioHeaderAsync(h, ct).ConfigureAwait(false);
        if (header is null) return null;
        IReadOnlyList<PostedLine> lines;
        IReadOnlyList<PostedReceipt> receipts;
        IReadOnlyList<ApprovalRow> approvals;
        await using (var l = C($"SELECT {LineColumns} FROM bill.charge WHERE folio_id = @f ORDER BY charge_id;")) lines = await ReadListAsync(l, ReadLine, ct).ConfigureAwait(false);
        await using (var r = C($"SELECT {ReceiptColumns} FROM pay.receipt WHERE folio_id = @f ORDER BY receipt_id;")) receipts = await ReadListAsync(r, rd => ReadReceipt(rd, 0), ct).ConfigureAwait(false);
        await using (var a = C($"SELECT {ApprovalColumns} FROM bill.approval WHERE folio_id = @f ORDER BY approval_id;")) approvals = await ReadListAsync(a, ReadApproval, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new FolioSnapshot(header, lines, receipts, approvals);
    }

    public async Task<(ShiftRow Shift, IReadOnlyList<MovementRow> Movements)?> ReadShiftAsync(long shiftId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        ShiftRow? shift;
        await using (var s = connections.Command(string.Format(System.Globalization.CultureInfo.InvariantCulture, ShiftSelect, "") + " WHERE s.shift_id = @s;", conn, tx))
        {
            s.Parameters.Add("@s", SqlDbType.BigInt).Value = shiftId;
            shift = (await ReadListAsync(s, ReadShift, ct).ConfigureAwait(false)).SingleOrDefault();
        }

        if (shift is null) return null;
        await using var m = connections.Command(MovementSql, conn, tx);
        m.Parameters.Add("@s", SqlDbType.BigInt).Value = shiftId;
        var movements = await ReadListAsync(m, ReadMovement, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return (shift, movements);
    }

    public async Task<IReadOnlyList<PostedReceipt>> ReceiptsOnAsync(int propertyId, DateOnly businessDate, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT x.receipt_id, x.kind, x.method, x.amount_paise, x.business_date, x.refund_of, x.reference
            FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
            WHERE r.property_id = @p AND x.business_date = @d ORDER BY x.receipt_id;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = businessDate;
        return await ReadListAsync(cmd, rd => ReadReceipt(rd, 0), ct).ConfigureAwait(false);
    }

    // ── readers ──

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(SqlCommand cmd, Func<SqlDataReader, T> read, CancellationToken ct)
    {
        var list = new List<T>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(read(rd));
        return list;
    }

    private static async Task<FolioHeader?> ReadFolioHeaderAsync(SqlCommand cmd, CancellationToken ct)
        => (await ReadListAsync(cmd, rd => new FolioHeader(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetString(3), rd.GetFieldValue<DateOnly>(4),
            rd.GetFieldValue<DateOnly>(5), rd.GetFieldValue<DateOnly>(6), SqlValues.RowVersionToWire((byte[])rd[7])), ct).ConfigureAwait(false)).SingleOrDefault();

    private static PostedLine ReadLine(SqlDataReader rd) => new(rd.GetInt64(0), BillingCodes.ParseChargeKind(rd.GetString(1)), rd.GetFieldValue<DateOnly>(2),
        rd.GetString(3), SqlValues.ReadPaise(rd, 4), rd.GetInt32(5), SqlValues.ReadPaise(rd, 6), rd.IsDBNull(7) ? null : rd.GetInt64(7));

    private static PostedReceipt ReadReceipt(SqlDataReader rd, int o) => new(rd.GetInt64(o), FolioTotals.ParseKind(rd.GetString(o + 1)), rd.GetString(o + 2),
        SqlValues.ReadPaise(rd, o + 3), rd.GetFieldValue<DateOnly>(o + 4), rd.IsDBNull(o + 5) ? null : rd.GetInt64(o + 5), rd.GetString(o + 6));

    private static ApprovalRow ReadApproval(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), BillingCodes.ParseAction(rd.GetString(2)),
        rd.IsDBNull(3) ? null : rd.GetInt64(3), rd.IsDBNull(4) ? null : rd.GetInt64(4), rd.GetString(5), rd.GetInt64(6), SqlValues.ReadPaise(rd, 7),
        rd.IsDBNull(8) ? null : SqlValues.RowVersionToWire((byte[])rd[8]), rd.IsDBNull(9) ? null : SqlValues.ReadPaise(rd, 9),
        rd.IsDBNull(10) ? null : SqlValues.ReadPaise(rd, 10), rd.GetString(11), BillingCodes.ParseStatus(rd.GetString(12)), rd.GetInt32(13),
        rd.IsDBNull(14) ? null : rd.GetInt32(14), DateTime.SpecifyKind(rd.GetDateTime(15), DateTimeKind.Utc), rd.IsDBNull(16) ? null : rd.GetInt64(16),
        SqlValues.RowVersionToWire((byte[])rd[17]));

    private static TillRow ReadTill(SqlDataReader rd) => new(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), SqlValues.RowVersionToWire((byte[])rd[4]));

    private static ShiftRow ReadShift(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetFieldValue<DateOnly>(3), rd.GetString(4),
        SqlValues.ReadPaise(rd, 5), rd.IsDBNull(6) ? null : SqlValues.ReadPaise(rd, 6), rd.IsDBNull(7) ? null : SqlValues.ReadPaise(rd, 7),
        rd.IsDBNull(8) ? null : rd.GetInt32(8), rd.IsDBNull(9) ? null : rd.GetInt64(9), SqlValues.RowVersionToWire((byte[])rd[10]));

    private static MovementRow ReadMovement(SqlDataReader rd) => new(rd.GetInt64(0), CashDrawer.Parse(rd.GetString(1)), rd.GetString(2), rd.GetInt64(3),
        SqlValues.ReadPaise(rd, 4), rd.GetString(5), rd.GetString(6), rd.GetFieldValue<DateOnly>(7));

    private static void Utc(SqlCommand cmd, string name, DateTime value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.DateTime2);
        p.Scale = 3;
        p.Value = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
    }

    private static void UtcOrNull(SqlCommand cmd, string name, DateTime? value)
    {
        if (value is { } v) Utc(cmd, name, v);
        else cmd.Parameters.Add(name, SqlDbType.DateTime2).Value = DBNull.Value;
    }
}
