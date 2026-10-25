using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Handover;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Handover;
using Microsoft.Data.SqlClient;

namespace Hcc.Handover.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Set the property-local (IST) wall clock.</summary>
    public void SetLocal(string isoLocal) => Now = new DateTimeOffset(DateTime.Parse(isoLocal, CultureInfo.InvariantCulture), TimeSpan.FromHours(5.5)).ToUniversalTime();
}

public static class Hov
{
    /// <summary>The prototype's six fixture templates (code → name, desk, shift, roles, items).</summary>
    public sealed record Item(string Key, string Text, bool Mandatory = true, string? Due = null, bool Photo = false, string? Link = null);

    public sealed record Tpl(string Code, string Name, string Desk, string Shift, string[] Roles, Item[] Items);

    public static readonly Tpl[] Templates =
    [
        new("FD-OPEN", "Front desk opening", "Front desk", "Morning", ["REC"],
        [
            new("fo1", "Read and acknowledge the night handover note", Due: "07:30"), new("fo2", "Review today’s arrivals and special requests", Due: "08:30", Link: "reservations"),
            new("fo3", "Check the room board: dirty / inspection / blocked rooms", Due: "08:30", Link: "rooms"), new("fo4", "Verify the opening cash float with the cashier", Due: "09:00", Link: "cashier"),
            new("fo5", "Test the key-card encoder and printer (simulated)", false, "09:00"), new("fo6", "Lobby and desk presentable — photo", Due: "09:30", Photo: true),
            new("fo7", "Note VIP / repeat guests for the shift", false),
        ]),
        new("FD-CLOSE", "Front desk closing", "Front desk", "Evening", ["REC"],
        [
            new("fc1", "All due departures settled", Due: "20:00"), new("fc2", "Pending arrivals called / no-shows marked", Due: "21:00"), new("fc3", "Key cards reconciled against in-house rooms", Due: "22:00"),
            new("fc4", "Lost-property register updated", false), new("fc5", "Handover note written for the night shift", Due: "22:45"), new("fc6", "Desk tidy — photo", false, Photo: true),
        ]),
        new("CASH-HO", "Cashier handover", "Front desk", "Evening", ["CSH", "REC"],
        [
            new("ch1", "Count the drawer and record the count", Due: "21:30"), new("ch2", "Match UPI settlement report (simulated)", Due: "21:30"), new("ch3", "File expense vouchers for today", false),
            new("ch4", "Record the safe drop in the register", Due: "22:00"), new("ch5", "Card terminal batch closed (simulated)", false), new("ch6", "Cash float line written in the handover note", Due: "22:45"),
        ]),
        new("HK-FLOOR", "Housekeeping floor check", "Housekeeping", "Morning", ["HKP", "SUP"],
        [
            new("hk1", "Trolley stocked (linen, amenities, chemicals)", Due: "07:45"), new("hk2", "Corridors floors 1–3 clean — photo", Due: "09:00", Photo: true), new("hk3", "Lost & found items logged", false),
            new("hk4", "Note Do-Not-Disturb rooms for the supervisor", Due: "10:00"), new("hk5", "Linen par levels checked", false, "11:00"), new("hk6", "Repairs reported from rooms", false),
            new("hk7", "Chemicals locked in the store", Due: "14:30"),
        ]),
        new("NA-PRE", "Night audit pre-checks", "Front desk", "Night", ["MGR", "REC"],
        [
            new("na1", "All arrivals checked in or cancelled", Due: "23:30"), new("na2", "Room nights posted for in-house guests", Due: "00:30"), new("na3", "Pending approvals and expenses cleared", Due: "00:30"),
            new("na4", "Cash shift counted and reconciled", Due: "01:00"), new("na5", "Data backup taken (simulated)", false, "02:00"), new("na6", "Lobby and gate security round", Due: "03:00"),
        ]),
        new("MNT-RND", "Maintenance rounds", "Maintenance", "Morning", ["MNT", "SUP"],
        [
            new("mr1", "DG set diesel level noted", Due: "08:00"), new("mr2", "Overhead water tank level checked", Due: "08:30"), new("mr3", "Lift alarm and intercom test", Due: "09:30"),
            new("mr4", "Fire extinguisher gauges — photo", Due: "10:30", Photo: true), new("mr5", "Pump room inspected", false), new("mr6", "Blocked room 308 status update", false),
        ]),
    ];
}

/// <summary>Disposable HccTest_* database with the prototype handover fixture (6 templates, 8 runs, 3 notes), real access policy and real commands.</summary>
public sealed partial class HandoverWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    public const string Yesterday = "2026-10-23", Today = "2026-10-24";

    private readonly string _server;
    private readonly string _name;

    private HandoverWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        Commands = new HandoverCommands(Platform.Commands, Policy, new SqlHandoverStore(), new SqlHandoverPrincipals(Platform.Connections), Clock);
        Queries = new HandoverQueries(Policy, new SqlHandoverReadStore(Platform.Connections), new SqlHandoverPrincipals(Platform.Connections), Clock);
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public HandoverCommands Commands { get; }

    public HandoverQueries Queries { get; }

    public int PropertyId { get; private set; }

    /// <summary>Prototype user id → principal id.</summary>
    public Dictionary<string, int> Users { get; } = [];

    /// <summary>Prototype template code → template id.</summary>
    public Dictionary<string, long> Tpl { get; } = [];

    public CommandContext Ctx(string user, string key) => new(PropertyId, Users[user], "H", key);

    public static async Task<HandoverWorld> CreateAsync(CancellationToken ct)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var w = new HandoverWorld(server, name);
        try
        {
            await w.Platform.Migrations.MigrateAsync(ct);
            await w.SeedAsync(ct);
            return w;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public Task ExecAsync(string sql, CancellationToken ct = default) => ScalarAsync(sql, ct);

    private static string S(string? v) => v is null ? "NULL" : "N'" + v.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Utc(string date, string hhmm) => DateTime.Parse(date + "T" + hhmm, CultureInfo.InvariantCulture).AddHours(-5.5).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("supervisor", "SUP"), ("housekeeper", "HKP"), ("housekeeper2", "HKP"), ("accountant", "ACC"), ("maint", "MNT"), ("techadmin", "TAD") })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", ct);
            Users[login] = id;
        }

        foreach (var t in Hov.Templates) Tpl[t.Code] = await InsertTemplateAsync(t, ct);
        string Y(string h) => Yesterday + "T" + h;
        string T(string h) => Today + "T" + h;
        // Yesterday (23 Oct)
        await RunAsync("FD-OPEN", Yesterday, "reception", Y("07:05"), new() { ["fo1"] = ("reception", "07:10"), ["fo2"] = ("reception", "07:40"), ["fo3"] = ("reception", "07:55"), ["fo4"] = ("reception", "08:20"), ["fo6"] = ("reception", "08:40"), ["fo7"] = ("reception", "09:00") }, new() { ["fo6"] = (null, "IMG_20261023_0840.jpg") }, ("reception", "09:05"), ct);
        await RunAsync("HK-FLOOR", Yesterday, "housekeeper", Y("07:10"), new() { ["hk1"] = ("housekeeper", "07:20"), ["hk2"] = ("housekeeper", "08:50"), ["hk4"] = ("housekeeper2", "09:30"), ["hk5"] = ("housekeeper2", "10:40"), ["hk7"] = ("housekeeper", "14:10") }, new() { ["hk2"] = (null, "IMG_20261023_0850.jpg") }, ("supervisor", "14:20"), ct);
        await RunAsync("MNT-RND", Yesterday, "maint", Y("07:30"), new() { ["mr1"] = ("maint", "07:40"), ["mr2"] = ("maint", "08:10"), ["mr3"] = ("maint", "09:20"), ["mr4"] = ("maint", "10:15"), ["mr5"] = ("maint", "11:00") }, new() { ["mr4"] = ("Extinguisher near 2nd-floor lift at the lower green band — refill booked.", "IMG_20261023_1015.jpg") }, ("maint", "11:05"), ct);
        await RunAsync("CASH-HO", Yesterday, "cashier", Y("21:00"), new() { ["ch1"] = ("cashier", "21:10"), ["ch2"] = ("cashier", "21:20"), ["ch3"] = ("cashier", "21:25"), ["ch4"] = ("cashier", "21:40"), ["ch6"] = ("cashier", "22:30") }, [], ("cashier", "22:35"), ct);
        await RunAsync("FD-CLOSE", Yesterday, "reception", Y("19:00"), new() { ["fc1"] = ("reception", "19:30"), ["fc2"] = ("reception", "21:10") }, [], null, ct);
        await RunAsync("NA-PRE", Yesterday, "manager", Y("23:05"), new() { ["na1"] = ("manager", "23:10"), ["na2"] = ("manager", "23:50"), ["na3"] = ("manager", "23:55"), ["na4"] = ("manager", "23:58"), ["na6"] = ("manager", "23:59") }, [], ("manager", "23:59"), ct);
        // Today (24 Oct, 08:00)
        await RunAsync("FD-OPEN", Today, "reception", T("07:20"), new() { ["fo2"] = ("reception", "07:25"), ["fo3"] = ("reception", "07:40") }, new() { ["fo3"] = ("308 blocked for AC repair; 108, 204, 208 dirty.", null) }, null, ct);
        await RunAsync("HK-FLOOR", Today, "housekeeper", T("07:15"), new() { ["hk1"] = ("housekeeper", "07:30") }, [], null, ct);
        // Notes
        var n1 = await NoteAsync("Housekeeping", Yesterday, "Morning", "Evening", "supervisor", Y("14:40"), "Room 206 guest asked for extra pillows at turndown. Iron in 105 sparks — removed.", "Master key returned to the supervisor’s box.", "206 · extra pillows", null, null, "Acknowledged", "housekeeper2", Y("15:05"), null, ct);
        await CarryAsync(n1, 1, "Replace iron for room 105", ("housekeeper2", Y("16:05")), ct);
        var n2 = await NoteAsync("Maintenance", Yesterday, "Morning", "Evening", "maint", Y("14:50"), "Room 308 AC compressor tripping — room stays blocked.", "Terrace and pump-room keys on the maintenance board.", "", null, null, "Acknowledged", "supervisor", Y("15:20"), "Will follow up with the vendor.", ct);
        await CarryAsync(n2, 1, "Room 308 AC compressor — vendor visit (Coolwave Services, fictional)", null, ct);
        await CarryAsync(n2, 2, "Refill 2nd-floor fire extinguisher", null, ct);
        var n3 = await NoteAsync("Front desk", Yesterday, "Night", "Morning", "manager", T("06:45"), "Two arrivals today (Ishaan Bose 103, Aditi Nair 207) — both registered pending. Morning Supplies expense awaiting approval.", "14 key cards out, 6 in the drawer; spare master in the safe.", "Room 202 requested a 7:00 wake-up call (done). 301 late checkout request — check with manager.", 500000, "Opening float in the drawer as counted at close.", "Submitted", null, null, null, ct);
        await CarryAsync(n3, 1, "Confirm 301 late checkout with the manager", null, ct);
        await CarryAsync(n3, 2, "Call Aditi Nair to confirm arrival time", null, ct);
        Notes["HOVN-FRONT-2026-10-23-N"] = n3;
        Notes["HOVN-MAINTENANCE-2026-10-23-M"] = n2;
        Notes["HOVN-HOUSEKEEPING-2026-10-23-M"] = n1;
    }

    /// <summary>Prototype note id → note_id.</summary>
    public Dictionary<string, long> Notes { get; } = [];

    private async Task<long> InsertTemplateAsync(Hov.Tpl t, CancellationToken ct)
    {
        var id = Convert.ToInt64(await ScalarAsync($"INSERT ops.hov_template(property_id, name, desk, shift, active, version, updated_by, updated_utc) OUTPUT INSERTED.template_id VALUES ({PropertyId}, {S(t.Name)}, '{t.Desk}', '{t.Shift}', 1, 1, {Users["manager"]}, '2026-10-20T04:30:00');", ct), CultureInfo.InvariantCulture);
        foreach (var r in t.Roles) await ExecAsync($"INSERT ops.hov_template_role VALUES ({id}, '{r}');", ct);
        for (var i = 0; i < t.Items.Length; i++)
        {
            var it = t.Items[i];
            await ExecAsync($"INSERT ops.hov_template_item(template_id, seq, item_key, text, mandatory, due, photo, link) VALUES ({id}, {i + 1}, '{it.Key}', {S(it.Text)}, {(it.Mandatory ? 1 : 0)}, {S(it.Due)}, {(it.Photo ? 1 : 0)}, {S(it.Link)});", ct);
        }

        return id;
    }

    private async Task RunAsync(string code, string date, string startedBy, string startedLocal, Dictionary<string, (string By, string At)> done, Dictionary<string, (string? Note, string? Photo)> extra, (string By, string At)? submitted, CancellationToken ct)
    {
        var t = Hov.Templates.Single(x => x.Code == code);
        var started = Utc(startedLocal[..10], startedLocal[11..]);
        var (status, sb, su, lt) = submitted is null ? ("Open", "NULL", "NULL", "NULL")
            : ("Submitted", Users[submitted.Value.By].ToString(CultureInfo.InvariantCulture), $"'{Utc(date, submitted.Value.At)}'", "0");
        var runId = Convert.ToInt64(await ScalarAsync($"""
            INSERT ops.hov_run(property_id, template_id, template_version, name, desk, shift, business_date, status, started_by, started_utc, submitted_by, submitted_utc, late)
            OUTPUT INSERTED.run_id VALUES ({PropertyId}, {Tpl[code]}, 1, {S(t.Name)}, '{t.Desk}', '{t.Shift}', '{date}', '{status}', {Users[startedBy]}, '{started}', {sb}, {su}, {lt});
            """, ct), CultureInfo.InvariantCulture);
        foreach (var r in t.Roles) await ExecAsync($"INSERT ops.hov_run_role VALUES ({runId}, '{r}');", ct);
        for (var i = 0; i < t.Items.Length; i++)
        {
            var it = t.Items[i];
            done.TryGetValue(it.Key, out var d);
            extra.TryGetValue(it.Key, out var x);
            var tick = d.By is null ? "0, NULL, NULL" : $"1, {Users[d.By]}, '{Utc(date, d.At)}'";
            await ExecAsync($"INSERT ops.hov_run_item(run_id, seq, item_key, text, mandatory, due, photo, link, done, done_by, done_utc, note, photo_name) VALUES ({runId}, {i + 1}, '{it.Key}', {S(it.Text)}, {(it.Mandatory ? 1 : 0)}, {S(it.Due)}, {(it.Photo ? 1 : 0)}, {S(it.Link)}, {tick}, {S(x.Note)}, {S(x.Photo)});", ct);
        }
    }

    private async Task<long> NoteAsync(string desk, string date, string shift, string toShift, string author, string writtenLocal, string issues, string keys, string guests, long? cash, string? cashNote, string status, string? ackBy, string? ackLocal, string? remark, CancellationToken ct)
    {
        var written = Utc(writtenLocal[..10], writtenLocal[11..]);
        var ack = ackLocal is null ? "NULL" : $"'{Utc(ackLocal[..10], ackLocal[11..])}'";
        return Convert.ToInt64(await ScalarAsync($"""
            INSERT ops.hov_note(property_id, desk, business_date, shift, to_shift, author_id, written_utc, issues, nothing, keys, guests, cash_paise, cash_note, status, ack_by, ack_utc, ack_remark)
            OUTPUT INSERTED.note_id VALUES ({PropertyId}, '{desk}', '{date}', '{shift}', '{toShift}', {Users[author]}, '{written}', {S(issues)}, 0, {S(keys)}, {S(guests)}, {(cash is null ? "NULL" : cash.Value.ToString(CultureInfo.InvariantCulture))}, {S(cashNote)}, '{status}', {(ackBy is null ? "NULL" : Users[ackBy].ToString(CultureInfo.InvariantCulture))}, {ack}, {S(remark)});
            """, ct), CultureInfo.InvariantCulture);
    }

    private async Task CarryAsync(long noteId, int seq, string text, (string By, string At)? done, CancellationToken ct)
    {
        var d = done is null ? "0, NULL, NULL" : $"1, {Users[done.Value.By]}, '{Utc(done.Value.At[..10], done.Value.At[11..])}'";
        await ExecAsync($"INSERT ops.hov_carry(note_id, seq, text, done, done_by, done_utc) VALUES ({noteId}, {seq}, {S(text)}, {d});", ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (!NamePattern().IsMatch(_name)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"""
            IF DB_ID(N'{_name}') IS NOT NULL
            BEGIN
              ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
              DROP DATABASE [{_name}];
            END
            """, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}
