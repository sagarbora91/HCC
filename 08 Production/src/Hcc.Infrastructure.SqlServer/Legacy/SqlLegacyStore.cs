using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Legacy;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Legacy;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Legacy;

/// <summary>
/// <c>lgc.*</c> persistence of the legacy importer (migration 4600, LG-HISTORY-CONTRACT §3): bulk inserts with <see cref="SqlBulkCopy"/> inside the
/// command transaction, stay / bill references resolved after the insert, per-FY savepoints, the §6 figures from the stored rows; plus the
/// guest columns <c>legacy_guest_id</c> / <c>dob</c> (gst.guest, never an ID column), <c>crm.customer_tag</c> and company GSTINs.
/// </summary>
public sealed class SqlLegacyStore(SqlConnectionFactory connections) : ILegacyStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The legacy store needs the SQL command transaction.");

    private static SqlCommand Cmd(ICommandTransaction tx, string sql)
    {
        var c = Sql(tx).Command(sql);
        c.CommandTimeout = 600;
        return c;
    }

    private static object Db(object? v) => v ?? DBNull.Value;

    private static object Db(DateOnly? d) => d is { } x ? x.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    public async Task LockAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;");
        cmd.Parameters.Add("@res", SqlDbType.NVarChar, 255).Value = "lgc:import:" + propertyId.ToString(CultureInfo.InvariantCulture);
        if ((int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999) < 0) throw new HccException(HccErrorCode.UNAVAILABLE, "Another legacy import is running. Try again in a moment.");
    }

    public async Task<bool> AnyDayClosedAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT CASE WHEN EXISTS (SELECT 1 FROM cls.business_day WITH (HOLDLOCK) WHERE property_id = @p) THEN 1 ELSE 0 END;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<IReadOnlyList<LegacyGuestCandidate>> GuestsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT guest_id, full_name, phone, legacy_guest_id FROM gst.guest WHERE property_id = @p;");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        var list = new List<LegacyGuestCandidate>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new LegacyGuestCandidate(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetInt64(3)));
        return list;
    }

    public async Task<bool> SetGuestLegacyAsync(ICommandTransaction tx, int propertyId, long guestId, long legacyGuestId, DateOnly? dob, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            IF EXISTS (SELECT 1 FROM gst.guest WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND legacy_guest_id = @l AND guest_id <> @g) SELECT 0;
            ELSE BEGIN
              UPDATE gst.guest SET legacy_guest_id = @l, dob = COALESCE(@d, dob) WHERE guest_id = @g AND property_id = @p;
              SELECT 1;
            END
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        cmd.Parameters.Add("@l", SqlDbType.BigInt).Value = legacyGuestId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = Db(dob);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    public async Task<bool> AddTagAsync(ICommandTransaction tx, int propertyId, string customerKey, string tag, int actor, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            IF NOT EXISTS (SELECT 1 FROM crm.customer_tag WHERE property_id = @p AND customer_key = @k AND tag = @t)
            BEGIN INSERT crm.customer_tag(property_id, customer_key, tag, added_by, added_utc) VALUES (@p, @k, @t, @u, @now); SELECT 1; END
            ELSE SELECT 0;
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 80).Value = customerKey;
        cmd.Parameters.Add("@t", SqlDbType.NVarChar, 30).Value = tag;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = actor;
        cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = utc;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    private static async Task BulkAsync(ICommandTransaction tx, string table, string batch, DataTable data, CancellationToken ct)
    {
        if (data.Rows.Count == 0) return;
        var t = Sql(tx);
        using var bulk = new SqlBulkCopy(t.Connection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, t.Transaction)
        {
            DestinationTableName = table,
            BulkCopyTimeout = 600,
            BatchSize = 5000,
        };
        foreach (DataColumn c in data.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        try
        {
            await bulk.WriteToServerAsync(data, ct).ConfigureAwait(false);
        }
        catch (SqlException e) when (e.Number is 4815 or 8152 or 2628)
        {
            // LG-R15: a value longer than its column is a data problem of the run folder, not an outage.
            throw HccException.Validation($"{table} ({batch}): the database refused a value longer than its column (SQL {e.Number.ToString(CultureInfo.InvariantCulture)}). Fix the extractor output; nothing was imported.");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("would be truncated", StringComparison.Ordinal) || (e.InnerException?.Message.Contains("would be truncated", StringComparison.Ordinal) ?? false))
        {
            // CU-NVARCHAR: for an NVARCHAR column SqlBulkCopy reports the over-long value itself (client side) instead of SQL error 8152.
            throw HccException.Validation($"{table} ({batch}): the database refused a value longer than its column. Fix the extractor output; nothing was imported.");
        }
    }

    private static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var t = new DataTable { Locale = CultureInfo.InvariantCulture };
        foreach (var (n, ty) in columns) t.Columns.Add(n, ty);
        return t;
    }

    public async Task ReplaceGuestMapAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<LgcGuestMapRow> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        await using (var del = Cmd(tx, "DELETE lgc.guest_map WHERE property_id = @p;"))
        {
            del.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var t = Table(("property_id", typeof(int)), ("legacy_guest_id", typeof(long)), ("guest_id", typeof(long)), ("customer_key", typeof(string)), ("phone_key", typeof(string)),
            ("phone_valid", typeof(bool)), ("name_snapshot", typeof(string)), ("name_fold", typeof(string)), ("deleted", typeof(bool)), ("source_fy", typeof(string)));
        foreach (var r in rows)
            t.Rows.Add(propertyId, r.LegacyGuestId, Db(r.GuestId), r.CustomerKey, Db(r.PhoneKey), r.PhoneValid, r.NameSnapshot, r.NameFold, r.Deleted, r.SourceFy);
        await BulkAsync(tx, "lgc.guest_map", "guest map", t, ct).ConfigureAwait(false);
    }

    private static void CheckName(string name)
    {
        if (name.Length is 0 or > 32 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw new ArgumentException("Bad savepoint name.", nameof(name));
    }

    public Task SavepointAsync(ICommandTransaction tx, string name, CancellationToken ct = default)
    {
        CheckName(name);
        Sql(tx).Transaction.Save(name);
        return Task.CompletedTask;
    }

    public Task RollbackToSavepointAsync(ICommandTransaction tx, string name, CancellationToken ct = default)
    {
        CheckName(name);
        Sql(tx).Transaction.Rollback(name);
        return Task.CompletedTask;
    }

    public async Task<LgcFyWritten> ReplaceFyAsync(ICommandTransaction tx, int propertyId, LgcFyData data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        await using (var del = Cmd(tx, """
            DELETE r FROM lgc.receipt r WHERE r.property_id = @p AND (r.legacy_fy = @fy
              OR r.stay_id IN (SELECT stay_id FROM lgc.stay WHERE property_id = @p AND legacy_fy = @fy)
              OR r.bill_id IN (SELECT bill_id FROM lgc.bill WHERE property_id = @p AND legacy_fy = @fy));
            DELETE b FROM lgc.bill b WHERE b.property_id = @p AND (b.legacy_fy = @fy OR b.stay_id IN (SELECT stay_id FROM lgc.stay WHERE property_id = @p AND legacy_fy = @fy));
            DELETE g FROM lgc.room_segment g JOIN lgc.stay s ON s.stay_id = g.stay_id WHERE s.property_id = @p AND s.legacy_fy = @fy;
            DELETE lgc.stay WHERE property_id = @p AND legacy_fy = @fy;
            """))
        {
            del.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            del.Parameters.Add("@fy", SqlDbType.Char, 7).Value = data.Fy;
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var st = Table(("property_id", typeof(int)), ("legacy_fy", typeof(string)), ("legacy_booking_id", typeof(long)), ("legacy_guest_id", typeof(long)), ("customer_key", typeof(string)),
            ("guest_name_snapshot", typeof(string)), ("arrival_date", typeof(DateTime)), ("arrival_utc", typeof(DateTime)), ("departure_date", typeof(DateTime)), ("departure_utc", typeof(DateTime)),
            ("expected_departure_date", typeof(DateTime)), ("nights", typeof(int)), ("adults", typeof(int)), ("children", typeof(int)), ("room_no", typeof(string)), ("room_count", typeof(int)),
            ("meal_plan", typeof(string)), ("grc_no", typeof(int)), ("purpose", typeof(string)), ("source_tag", typeof(string)), ("status", typeof(string)), ("paid_flag", typeof(string)),
            ("legacy_bill_no", typeof(int)), ("notes", typeof(string)));
        foreach (var s in data.Stays)
            st.Rows.Add(propertyId, s.Fy, s.LegacyBookingId, s.LegacyGuestId, s.CustomerKey, s.GuestNameSnapshot, s.ArrivalDate.ToDateTime(TimeOnly.MinValue), s.ArrivalUtc,
                Db(s.DepartureDate), Db(s.DepartureUtc), Db(s.ExpectedDepartureDate), s.Nights, s.Adults, s.Children, s.RoomNo, s.RoomCount, Db(s.MealPlan), Db(s.GrcNo),
                Db(s.Purpose), Db(s.SourceTag), s.Status, Db(s.PaidFlag), Db(s.LegacyBillNo), Db(s.Notes));
        await BulkAsync(tx, "lgc.stay", "FY " + data.Fy, st, ct).ConfigureAwait(false);
        var stayIds = await IdsAsync(tx, "SELECT legacy_booking_id, stay_id FROM lgc.stay WHERE property_id = @p AND legacy_fy = @fy;", propertyId, data.Fy, ct).ConfigureAwait(false);

        var unresolvedStay = 0;
        var sg = Table(("stay_id", typeof(long)), ("seq", typeof(short)), ("room_no", typeof(string)), ("room_type_code", typeof(string)), ("from_date", typeof(DateTime)),
            ("to_date", typeof(DateTime)), ("adults", typeof(int)), ("children", typeof(int)), ("rate_paise", typeof(long)), ("extra_paise", typeof(long)), ("kind", typeof(string)));
        foreach (var g in data.Segments)
        {
            if (!stayIds.TryGetValue(g.LegacyBookingId, out var sid))
            {
                unresolvedStay++;
                continue;
            }

            sg.Rows.Add(sid, checked((short)g.Seq), g.RoomNo, Db(g.RoomTypeCode), Db(g.FromDate), Db(g.ToDate), Db(g.Adults), Db(g.Children), Db(g.RatePaise), Db(g.ExtraPaise), g.Kind);
        }

        await BulkAsync(tx, "lgc.room_segment", "FY " + data.Fy, sg, ct).ConfigureAwait(false);

        object StayRef(long? booking)
        {
            if (booking is not { } b) return DBNull.Value;
            if (stayIds.TryGetValue(b, out var id)) return id;
            unresolvedStay++;
            return DBNull.Value;
        }

        var bl = Table(("property_id", typeof(int)), ("legacy_fy", typeof(string)), ("legacy_bill_no", typeof(int)), ("legacy_transaction_id", typeof(long)), ("stay_id", typeof(long)),
            ("legacy_guest_id", typeof(long)), ("customer_key", typeof(string)), ("bill_date", typeof(DateTime)), ("bill_utc", typeof(DateTime)), ("room_no", typeof(string)), ("nights", typeof(int)),
            ("room_charge_paise", typeof(long)), ("extra_paise", typeof(long)), ("fb_paise", typeof(long)), ("laundry_paise", typeof(long)), ("other_paise", typeof(long)),
            ("discount_paise", typeof(long)), ("room_gst_bps", typeof(int)), ("room_gst_paise", typeof(long)), ("fb_gst_paise", typeof(long)), ("laundry_gst_paise", typeof(long)),
            ("other_gst_paise", typeof(long)), ("advance_paise", typeof(long)), ("net_paise", typeof(long)), ("balance_paise", typeof(long)), ("refund_paise", typeof(long)),
            ("data_flag", typeof(string)), ("paid_flag", typeof(string)), ("gstin_snapshot", typeof(string)), ("b2b", typeof(bool)), ("out_of_fy", typeof(bool)), ("notes", typeof(string)));
        foreach (var b in data.Bills)
            bl.Rows.Add(propertyId, b.Fy, b.LegacyBillNo, b.LegacyTransactionId, StayRef(b.StayBookingId), Db(b.LegacyGuestId), Db(b.CustomerKey), b.BillDate.ToDateTime(TimeOnly.MinValue),
                b.BillUtc, b.RoomNo, Db(b.Nights), b.RoomChargePaise, b.ExtraPaise, b.FbPaise, b.LaundryPaise, b.OtherPaise, b.DiscountPaise, b.RoomGstBps, b.RoomGstPaise, b.FbGstPaise,
                b.LaundryGstPaise, b.OtherGstPaise, b.AdvancePaise, b.NetPaise, b.BalancePaise, b.RefundPaise, b.DataFlag, b.PaidFlag, Db(b.GstinSnapshot), b.B2b, b.OutOfFy, Db(b.Notes));
        await BulkAsync(tx, "lgc.bill", "FY " + data.Fy, bl, ct).ConfigureAwait(false);
        var billIds = await IdsAsync(tx, "SELECT legacy_bill_no, bill_id FROM lgc.bill WHERE property_id = @p AND legacy_fy = @fy;", propertyId, data.Fy, ct).ConfigureAwait(false);

        var unresolvedBill = 0;
        var rc = Table(("property_id", typeof(int)), ("legacy_fy", typeof(string)), ("legacy_source", typeof(string)), ("legacy_row_id", typeof(long)), ("stay_id", typeof(long)),
            ("bill_id", typeof(long)), ("legacy_guest_id", typeof(long)), ("customer_key", typeof(string)), ("received_date", typeof(DateTime)), ("received_utc", typeof(DateTime)),
            ("kind", typeof(string)), ("method", typeof(string)), ("amount_paise", typeof(long)), ("reference", typeof(string)), ("bank_name", typeof(string)), ("notes", typeof(string)));
        foreach (var r in data.Receipts)
        {
            object bill = DBNull.Value;
            if (r.BillNo is { } no)
            {
                if (billIds.TryGetValue(no, out var id)) bill = id;
                else unresolvedBill++;
            }

            rc.Rows.Add(propertyId, r.Fy, r.LegacySource, r.LegacyRowId, StayRef(r.StayBookingId), bill, Db(r.LegacyGuestId), Db(r.CustomerKey), r.ReceivedDate.ToDateTime(TimeOnly.MinValue),
                r.ReceivedUtc, r.Kind, r.Method, r.AmountPaise, Db(r.Reference), Db(r.BankName), Db(r.Notes));
        }

        await BulkAsync(tx, "lgc.receipt", "FY " + data.Fy, rc, ct).ConfigureAwait(false);
        return new LgcFyWritten(st.Rows.Count, sg.Rows.Count, bl.Rows.Count, rc.Rows.Count, unresolvedStay, unresolvedBill);
    }

    private static async Task<Dictionary<long, long>> IdsAsync(ICommandTransaction tx, string sql, int propertyId, string fy, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, sql);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@fy", SqlDbType.Char, 7).Value = fy;
        var map = new Dictionary<long, long>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[Convert.ToInt64(rd.GetValue(0), CultureInfo.InvariantCulture)] = rd.GetInt64(1);
        return map;
    }

    public async Task<LegacyFigures> FiguresAsync(ICommandTransaction tx, int propertyId, string fy, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT COUNT_BIG(*), ISNULL(SUM(net_paise), 0), ISNULL(SUM(room_gst_paise), 0), ISNULL(SUM(fb_paise), 0) FROM lgc.bill WHERE property_id = @p AND legacy_fy = @fy;
            SELECT ISNULL(SUM(CASE WHEN legacy_source = 'deposit' AND kind IN ('advance','payment') THEN 1 ELSE 0 END), 0),
                   ISNULL(SUM(CASE WHEN legacy_source = 'deposit' AND kind IN ('advance','payment') THEN amount_paise ELSE 0 END), 0),
                   ISNULL(SUM(CASE WHEN legacy_source = 'refund' THEN 1 ELSE 0 END), 0), ISNULL(SUM(CASE WHEN legacy_source = 'refund' THEN amount_paise ELSE 0 END), 0)
            FROM lgc.receipt WHERE property_id = @p AND legacy_fy = @fy;
            SELECT COUNT_BIG(*) FROM lgc.stay WHERE property_id = @p AND legacy_fy = @fy AND status = 'Duplicate';
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@fy", SqlDbType.Char, 7).Value = fy;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        static long L(SqlDataReader r, int i) => Convert.ToInt64(r.GetValue(i), CultureInfo.InvariantCulture);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var (bills, net, gst, fb) = (L(rd, 0), L(rd, 1), L(rd, 2), L(rd, 3));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var (rc, rp, fc, fp) = (L(rd, 0), L(rd, 1), L(rd, 2), L(rd, 3));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return new LegacyFigures(bills, net, gst, fb, rc, rp, fc, fp, L(rd, 0));
    }

    public async Task<IReadOnlyDictionary<string, long>> CompaniesByGstinAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT UPPER(gstin), MIN(company_id) FROM grp.company_account WHERE property_id = @p AND gstin IS NOT NULL GROUP BY UPPER(gstin);");
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetString(0).Trim()] = rd.GetInt64(1);
        return map;
    }

    public async Task<long> InsertCaptureAsync(ICommandTransaction tx, int propertyId, long guestId, Guid captureRef, string contentType, int sizeBytes, string ocrDocKind,
        string? ocrLast4, string? ocrSealed, DateOnly capturedDate, DateTime utc, int actor, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            INSERT doc.id_capture(capture_ref, property_id, guest_id, stay_id, doc_kind, content_type, size_bytes, status, ocr_doc_kind, ocr_last4, ocr_confidence, ocr_sealed,
              captured_date, captured_utc, captured_by, confirmed_utc, confirmed_by)
            OUTPUT INSERTED.capture_id
            VALUES (@ref, @p, @g, NULL, @k, @t, @sz, 'Confirmed', @ok, @l4, @cf, @os, @d, @utc, @by, @utc, @by);
            """);
        cmd.Parameters.Add("@ref", SqlDbType.UniqueIdentifier).Value = captureRef;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 10).Value = LegacyRules.CaptureKind;
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 20).Value = contentType;
        cmd.Parameters.Add("@sz", SqlDbType.Int).Value = sizeBytes;
        cmd.Parameters.Add("@ok", SqlDbType.VarChar, 10).Value = ocrDocKind;
        cmd.Parameters.Add("@l4", SqlDbType.VarChar, 4).Value = Db(ocrLast4);
        cmd.Parameters.Add("@cf", SqlDbType.TinyInt).Value = ocrSealed is null ? DBNull.Value : (byte)100;
        cmd.Parameters.Add("@os", SqlDbType.VarChar, 4000).Value = Db(ocrSealed);
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = capturedDate.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@utc", SqlDbType.DateTime2).Value = utc;
        cmd.Parameters.Add("@by", SqlDbType.Int).Value = actor;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<long> InsertRunAsync(ICommandTransaction tx, int propertyId, string folder, string extractorVersion, bool dryRun, string parts, DateTime startedUtc,
        DateTime finishedUtc, string status, int runBy, string? resultJson, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            INSERT lgc.import_run(property_id, source_folder, extractor_version, dry_run, parts, started_utc, finished_utc, status, run_by, result_json)
            OUTPUT INSERTED.run_id VALUES (@p, @f, @v, @d, @parts, @s, @e, @st, @u, @j);
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@f", SqlDbType.NVarChar, 400).Value = folder;
        cmd.Parameters.Add("@v", SqlDbType.VarChar, 20).Value = extractorVersion.Length > 20 ? extractorVersion[..20] : extractorVersion;
        cmd.Parameters.Add("@d", SqlDbType.Bit).Value = dryRun;
        cmd.Parameters.Add("@parts", SqlDbType.VarChar, 60).Value = parts;
        cmd.Parameters.Add("@s", SqlDbType.DateTime2).Value = startedUtc;
        cmd.Parameters.Add("@e", SqlDbType.DateTime2).Value = finishedUtc;
        cmd.Parameters.Add("@st", SqlDbType.VarChar, 10).Value = status;
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = runBy;
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, -1).Value = Db(resultJson);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateRunResultAsync(ICommandTransaction tx, long runId, string resultJson, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "UPDATE lgc.import_run SET result_json = @j WHERE run_id = @r;");
        cmd.Parameters.Add("@r", SqlDbType.Int).Value = checked((int)runId);
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, -1).Value = resultJson;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LegacyRunRecord>> ListRunsAsync(int propertyId, int limit, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (@n) run_id, source_folder, extractor_version, dry_run, parts, started_utc, finished_utc, status, run_by, result_json
            FROM lgc.import_run WHERE property_id = @p ORDER BY run_id DESC;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = limit;
        var list = new List<LegacyRunRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new LegacyRunRecord(Convert.ToInt64(rd.GetValue(0), CultureInfo.InvariantCulture), rd.GetString(1), rd.GetString(2), rd.GetBoolean(3), rd.GetString(4),
                DateTime.SpecifyKind(rd.GetDateTime(5), DateTimeKind.Utc), rd.IsDBNull(6) ? null : DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc), rd.GetString(7),
                rd.GetInt32(8), rd.IsDBNull(9) ? null : rd.GetString(9)));
        return list;
    }
}
