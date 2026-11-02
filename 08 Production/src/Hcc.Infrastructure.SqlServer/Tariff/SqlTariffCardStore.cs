using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Tariff;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Tariff;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Tariff;

/// <summary>
/// SQL adapter of the tariff card port (migrations 4380–4383: mst.tariff_card_*; publish writes inv.room_type_tariff (day_set, card_version_id),
/// inv.occupancy_charge_rate, bill.meal_plan_price, bill.extra_item_price inside the command transaction).
/// </summary>
public sealed class SqlTariffCardStore(SqlConnectionFactory connections) : ITariffCardStore
{
    private const string Head = """
        SELECT version_id, label, effective_from, status, weekend_days, created_by, created_utc, proposed_by, published_by, published_utc, superseded_by, notes
          FROM mst.tariff_card_version
        """;

    private const string LineCols = """
        SELECT version_id, line_no, kind, room_type_code, [plan], occ_kind, meal_plan_code, extra_code, hall_id, day_set, rate_excl_paise, gst_bps, display_incl_paise,
               entered_incl_paise, min_guests, max_guests, age_from, age_to, note
          FROM mst.tariff_card_line
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) => tx as SqlCommandTransaction ?? throw new InvalidOperationException("The tariff card store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static int? I(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : Convert.ToInt32(rd.GetValue(i), CultureInfo.InvariantCulture);

    private static long? L(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt64(i);

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static TariffCardRec Rec(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetString(1), DateOnly.FromDateTime(rd.GetDateTime(2)), rd.GetString(3),
        TariffCardRules.ParseWeekend(rd.GetString(4)), [], rd.GetInt32(5), Utc(rd, 6), I(rd, 7), I(rd, 8), rd.IsDBNull(9) ? null : Utc(rd, 9), L(rd, 10), S(rd, 11), []);

    private static (long Version, TrcLine Line) Line(SqlDataReader rd) => (rd.GetInt64(0), new TrcLine(rd.GetInt32(1), rd.GetString(2), S(rd, 3), S(rd, 4), S(rd, 5), S(rd, 6), S(rd, 7),
        S(rd, 8), rd.GetString(9), L(rd, 10), rd.GetInt32(11), L(rd, 12), L(rd, 13), I(rd, 14), I(rd, 15), I(rd, 16), I(rd, 17), S(rd, 18)));

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<IReadOnlyList<int>> WeekendDefaultAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default)
    {
        const string q = "SELECT weekend_days FROM mst.tariff_card_setting WHERE property_id = @p;";
        if (tx is not null)
        {
            await using var cmd = Sql(tx).Command(q);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            return TariffCardRules.ParseWeekend(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(q, conn);
        P(c2, "@p", SqlDbType.Int, propertyId);
        return TariffCardRules.ParseWeekend(await c2.ExecuteScalarAsync(ct).ConfigureAwait(false) as string);
    }

    public async Task SetWeekendDefaultAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<int> days, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE mst.tariff_card_setting SET weekend_days = @w WHERE property_id = @p;
            IF @@ROWCOUNT = 0 INSERT mst.tariff_card_setting(property_id, weekend_days) VALUES (@p, @w);
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@w", SqlDbType.VarChar, TariffCardRules.WeekendText(days), 13);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<TariffCardRec?> LockAsync(ICommandTransaction tx, int propertyId, long versionId, CancellationToken ct = default)
    {
        TariffCardRec? rec;
        await using (var cmd = Sql(tx).Command(Head.Replace("FROM mst.tariff_card_version", "FROM mst.tariff_card_version WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)
            + " WHERE property_id = @p AND version_id = @v;"))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@v", SqlDbType.BigInt, versionId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            rec = await rd.ReadAsync(ct).ConfigureAwait(false) ? Rec(rd) : null;
        }

        if (rec is null) return null;
        var lines = new List<TrcLine>();
        await using (var cmd = Sql(tx).Command(LineCols + " WHERE version_id = @v ORDER BY line_no;"))
        {
            P(cmd, "@v", SqlDbType.BigInt, versionId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) lines.Add(Line(rd).Line);
        }

        return rec with { Lines = lines };
    }

    public async Task<long> InsertAsync(ICommandTransaction tx, int propertyId, TariffCardRec rec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rec);
        long id;
        await using (var cmd = Sql(tx).Command("""
            INSERT mst.tariff_card_version(property_id, label, effective_from, status, weekend_days, created_by, created_utc, notes)
            OUTPUT inserted.version_id VALUES (@p, @l, @f, 'draft', @w, @by, @at, @n);
            """))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@l", SqlDbType.NVarChar, rec.Label, 60);
            P(cmd, "@f", SqlDbType.Date, D(rec.EffectiveFrom));
            P(cmd, "@w", SqlDbType.VarChar, TariffCardRules.WeekendText(rec.WeekendDays), 13);
            P(cmd, "@by", SqlDbType.Int, rec.CreatedBy);
            P(cmd, "@at", SqlDbType.DateTime2, rec.CreatedUtc);
            P(cmd, "@n", SqlDbType.NVarChar, rec.Notes, 500);
            id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await InsertLinesAsync(tx, propertyId, id, rec.Lines, ct).ConfigureAwait(false);
        return id;
    }

    private static async Task InsertLinesAsync(ICommandTransaction tx, int propertyId, long id, IReadOnlyList<TrcLine> lines, CancellationToken ct)
    {
        foreach (var l in lines)
        {
            await using var cmd = Sql(tx).Command("""
                INSERT mst.tariff_card_line(version_id, line_no, kind, room_type_id, room_type_code, [plan], occ_kind, meal_plan_id, meal_plan_code, extra_id, extra_code, hall_id,
                  day_set, rate_excl_paise, gst_bps, display_incl_paise, entered_incl_paise, min_guests, max_guests, age_from, age_to, note)
                VALUES (@v, @n, @k, (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = @rt), @rt, @pl, @ok,
                  (SELECT meal_plan_id FROM bill.meal_plan WHERE property_id = @p AND code = @mp), @mp, (SELECT extra_item_id FROM bill.extra_item WHERE property_id = @p AND code = @ex), @ex,
                  @h, @ds, @r, @b, @di, @ei, @g1, @g2, @a1, @a2, @note);
                """);
            P(cmd, "@v", SqlDbType.BigInt, id);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@n", SqlDbType.Int, l.LineNo);
            P(cmd, "@k", SqlDbType.VarChar, l.Kind, 10);
            P(cmd, "@rt", SqlDbType.VarChar, l.RoomType, 20);
            P(cmd, "@pl", SqlDbType.VarChar, l.Plan, 20);
            P(cmd, "@ok", SqlDbType.VarChar, l.OccKind, 5);
            P(cmd, "@mp", SqlDbType.VarChar, l.MealPlan, 20);
            P(cmd, "@ex", SqlDbType.VarChar, l.Extra, 20);
            P(cmd, "@h", SqlDbType.VarChar, l.Hall, 20);
            P(cmd, "@ds", SqlDbType.VarChar, l.DaySet, 7);
            P(cmd, "@r", SqlDbType.BigInt, l.RateExcl);
            P(cmd, "@b", SqlDbType.Int, l.TaxBps);
            P(cmd, "@di", SqlDbType.BigInt, l.DisplayIncl);
            P(cmd, "@ei", SqlDbType.BigInt, l.EnteredIncl);
            P(cmd, "@g1", SqlDbType.TinyInt, l.MinGuests);
            P(cmd, "@g2", SqlDbType.TinyInt, l.MaxGuests);
            P(cmd, "@a1", SqlDbType.TinyInt, l.AgeFrom);
            P(cmd, "@a2", SqlDbType.TinyInt, l.AgeTo);
            P(cmd, "@note", SqlDbType.NVarChar, l.Note, 200);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task UpdateDraftAsync(ICommandTransaction tx, int propertyId, TariffCardRec rec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rec);
        await using (var cmd = Sql(tx).Command("""
            UPDATE mst.tariff_card_version SET label = @l, effective_from = @f, weekend_days = @w, notes = @n, status = @s, proposed_by = @pb
             WHERE property_id = @p AND version_id = @v AND status IN ('draft','proposed');
            DELETE mst.tariff_card_line WHERE version_id = @v;
            """))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@v", SqlDbType.BigInt, rec.VersionId);
            P(cmd, "@l", SqlDbType.NVarChar, rec.Label, 60);
            P(cmd, "@f", SqlDbType.Date, D(rec.EffectiveFrom));
            P(cmd, "@w", SqlDbType.VarChar, TariffCardRules.WeekendText(rec.WeekendDays), 13);
            P(cmd, "@n", SqlDbType.NVarChar, rec.Notes, 500);
            P(cmd, "@s", SqlDbType.VarChar, rec.Status, 10);
            P(cmd, "@pb", SqlDbType.Int, rec.ProposedBy);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await InsertLinesAsync(tx, propertyId, rec.VersionId, rec.Lines, ct).ConfigureAwait(false);
    }

    public async Task SetStatusAsync(ICommandTransaction tx, long versionId, string status, int? by, string? notes, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE mst.tariff_card_version SET status = @s, proposed_by = CASE WHEN @s = 'proposed' THEN @by ELSE proposed_by END, notes = COALESCE(@n, notes)
             WHERE version_id = @v;
            """);
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        P(cmd, "@s", SqlDbType.VarChar, status, 10);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@n", SqlDbType.NVarChar, notes, 500);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteDraftAsync(ICommandTransaction tx, long versionId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("DELETE mst.tariff_card_line WHERE version_id = @v; DELETE mst.tariff_card_version WHERE version_id = @v AND status = 'draft';");
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> UnknownCodesAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<TrcLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var missing = new List<string>();
        async Task<bool> Exists(string sql, string code)
        {
            await using var cmd = Sql(tx).Command(sql);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@c", SqlDbType.VarChar, code, 20);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
        }

        foreach (var t in lines.Select(l => l.RoomType).OfType<string>().Distinct())
            if (!await Exists("SELECT 1 FROM inv.room_type WHERE property_id = @p AND code = @c;", t).ConfigureAwait(false)) missing.Add("room type " + t);
        foreach (var m in lines.Where(l => l.Kind == "meal").Select(l => l.MealPlan).Concat(lines.Where(l => l.Kind == "room" && l.Plan is not (null or "" or "EP")).Select(l => l.Plan)).OfType<string>().Distinct())
            if (!await Exists("SELECT 1 FROM bill.meal_plan WHERE property_id = @p AND code = @c;", m).ConfigureAwait(false)) missing.Add("meal plan " + m);
        foreach (var x in lines.Where(l => l.Kind == "extra").Select(l => l.Extra).OfType<string>().Distinct())
            if (!await Exists("SELECT 1 FROM bill.extra_item WHERE property_id = @p AND code = @c;", x).ConfigureAwait(false)) missing.Add("extra " + x);
        return missing;
    }

    public async Task<IReadOnlyList<TrcTariffRow>> TariffsOnAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT t.code, x.effective_from, x.rate_paise, x.day_set, x.card_version_id
              FROM inv.room_type_tariff x WITH (UPDLOCK, HOLDLOCK) JOIN inv.room_type t ON t.room_type_id = x.room_type_id
             WHERE t.property_id = @p AND x.effective_from = @d;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@d", SqlDbType.Date, D(date));
        var list = new List<TrcTariffRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new TrcTariffRow(rd.GetString(0), DateOnly.FromDateTime(rd.GetDateTime(1)), rd.GetInt64(2), rd.GetString(3)[0], L(rd, 4)));
        return list;
    }

    public async Task<IReadOnlySet<string>> CardRefsAsync(ICommandTransaction tx, int propertyId, long exceptVersionId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT l.master + '|' + l.row_ref FROM mst.tariff_card_publish_log l JOIN mst.tariff_card_version v ON v.version_id = l.version_id
             WHERE v.property_id = @p AND v.version_id <> @v;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@v", SqlDbType.BigInt, exceptVersionId);
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) set.Add(rd.GetString(0));
        return set;
    }

    public async Task<IReadOnlyDictionary<string, (long? Now, long? OnDate)>> MoneyOnAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT 'occupancyCharges|' + t.code + '|' + k.kind,
                   (SELECT TOP (1) r.amount_paise FROM inv.occupancy_charge_rate r WHERE r.charge_id = c.charge_id AND r.kind = k.kind AND r.effective_from <= @d ORDER BY r.effective_from DESC),
                   (SELECT r.amount_paise FROM inv.occupancy_charge_rate r WHERE r.charge_id = c.charge_id AND r.kind = k.kind AND r.effective_from = @d)
              FROM inv.occupancy_charge c JOIN inv.room_type t ON t.room_type_id = c.room_type_id CROSS JOIN (VALUES ('Adult'),('Child'),('Bed')) k(kind)
             WHERE c.property_id = @p AND c.active = 1
            UNION ALL
            SELECT 'mealPlans|' + m.code + '|AddOn',
                   (SELECT TOP (1) x.amount_paise FROM bill.meal_plan_price x WHERE x.meal_plan_id = m.meal_plan_id AND x.effective_from <= @d ORDER BY x.effective_from DESC),
                   (SELECT x.amount_paise FROM bill.meal_plan_price x WHERE x.meal_plan_id = m.meal_plan_id AND x.effective_from = @d)
              FROM bill.meal_plan m WHERE m.property_id = @p
            UNION ALL
            SELECT 'extras|' + e.code + '|Price',
                   (SELECT TOP (1) x.amount_paise FROM bill.extra_item_price x WHERE x.extra_item_id = e.extra_item_id AND x.effective_from <= @d ORDER BY x.effective_from DESC),
                   (SELECT x.amount_paise FROM bill.extra_item_price x WHERE x.extra_item_id = e.extra_item_id AND x.effective_from = @d)
              FROM bill.extra_item e WHERE e.property_id = @p;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@d", SqlDbType.Date, D(date));
        var map = new Dictionary<string, (long?, long?)>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetString(0)] = (L(rd, 1), L(rd, 2));
        return map;
    }

    public async Task ApplyAsync(ICommandTransaction tx, int propertyId, long versionId, DateOnly date, TrcWrite write, int by, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var key = write.Ref.Split('|');
        var sql = write.Master switch
        {
            "tariffs" => """
                DECLARE @t INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = @k0);
                DECLARE @ds CHAR(1) = CASE @k2 WHEN 'weekday' THEN 'W' WHEN 'weekend' THEN 'E' ELSE 'A' END;
                DELETE inv.room_type_tariff WHERE room_type_id = @t AND effective_from = @d AND day_set = @ds;
                INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise, day_set, card_version_id) VALUES (@t, @d, @a, @ds, @v);
                """,
            "occupancyCharges" => """
                DECLARE @c INT = (SELECT TOP (1) c.charge_id FROM inv.occupancy_charge c JOIN inv.room_type t ON t.room_type_id = c.room_type_id WHERE c.property_id = @p AND t.code = @k0 AND c.active = 1);
                DELETE inv.occupancy_charge_rate WHERE charge_id = @c AND kind = @k1 AND effective_from = @d;
                INSERT inv.occupancy_charge_rate(charge_id, kind, effective_from, amount_paise) VALUES (@c, @k1, @d, @a);
                """,
            "mealPlans" => """
                DECLARE @m INT = (SELECT meal_plan_id FROM bill.meal_plan WHERE property_id = @p AND code = @k0);
                DELETE bill.meal_plan_price WHERE meal_plan_id = @m AND effective_from = @d;
                INSERT bill.meal_plan_price(meal_plan_id, effective_from, amount_paise) VALUES (@m, @d, @a);
                """,
            "extras" => """
                DECLARE @e INT = (SELECT extra_item_id FROM bill.extra_item WHERE property_id = @p AND code = @k0);
                DELETE bill.extra_item_price WHERE extra_item_id = @e AND effective_from = @d;
                INSERT bill.extra_item_price(extra_item_id, effective_from, amount_paise) VALUES (@e, @d, @a);
                """,
            _ => throw HccException.Validation("Unknown master " + write.Master + "."),
        };
        await using var cmd = Sql(tx).Command(sql + """

            INSERT mst.tariff_card_publish_log(version_id, master, row_ref, label, before_paise, after_paise, by_user) VALUES (@v, @ms, @ref, @lab, @b, @a, @by);
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        P(cmd, "@d", SqlDbType.Date, D(date));
        P(cmd, "@a", SqlDbType.BigInt, write.After);
        P(cmd, "@b", SqlDbType.BigInt, write.Before);
        P(cmd, "@k0", SqlDbType.VarChar, key[0], 20);
        P(cmd, "@k1", SqlDbType.VarChar, key.Length > 1 ? key[1] : "", 10);
        P(cmd, "@k2", SqlDbType.VarChar, key.Length > 2 ? key[2] : "", 10);
        P(cmd, "@ms", SqlDbType.VarChar, write.Master, 20);
        P(cmd, "@ref", SqlDbType.VarChar, write.Ref, 80);
        P(cmd, "@lab", SqlDbType.NVarChar, write.Label.Length > 120 ? write.Label[..120] : write.Label, 120);
        P(cmd, "@by", SqlDbType.Int, by);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> SupersedeAsync(ICommandTransaction tx, int propertyId, long versionId, DateOnly date, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE mst.tariff_card_version SET status = 'superseded', superseded_by = @v
             WHERE property_id = @p AND version_id <> @v AND status = 'published' AND effective_from <= @d;
            SELECT @@ROWCOUNT;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        P(cmd, "@d", SqlDbType.Date, D(date));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task MarkPublishedAsync(ICommandTransaction tx, long versionId, int by, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE mst.tariff_card_version SET status = 'published', published_by = @by, published_utc = @at WHERE version_id = @v AND status IN ('draft','proposed');");
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@at", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TariffCardRec>> ListAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        var recs = new List<TariffCardRec>();
        await using (var cmd = connections.Command(Head + " WHERE property_id = @p;", conn))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) recs.Add(Rec(rd));
        }

        var lines = new Dictionary<long, List<TrcLine>>();
        await using (var cmd = connections.Command(LineCols + " WHERE version_id IN (SELECT version_id FROM mst.tariff_card_version WHERE property_id = @p) ORDER BY version_id, line_no;", conn))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var (v, l) = Line(rd);
                if (!lines.TryGetValue(v, out var list)) lines[v] = list = [];
                list.Add(l);
            }
        }

        var log = new Dictionary<long, List<TrcWrite>>();
        await using (var cmd = connections.Command("""
            SELECT l.version_id, l.master, l.row_ref, l.before_paise, l.after_paise, l.label FROM mst.tariff_card_publish_log l
              JOIN mst.tariff_card_version v ON v.version_id = l.version_id WHERE v.property_id = @p ORDER BY l.log_id;
            """, conn))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var v = rd.GetInt64(0);
                if (!log.TryGetValue(v, out var list)) log[v] = list = [];
                list.Add(new TrcWrite(rd.GetString(1), rd.GetString(2), L(rd, 3), rd.GetInt64(4), rd.GetString(5)));
            }
        }

        return recs.Select(r => r with { Lines = lines.GetValueOrDefault(r.VersionId) ?? [], PublishLog = log.GetValueOrDefault(r.VersionId) ?? [] }).ToList();
    }
}
