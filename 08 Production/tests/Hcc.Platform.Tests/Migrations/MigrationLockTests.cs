using System.Globalization;
using System.Text.Json;
using Hcc.Infrastructure.SqlServer.Migrations;

namespace Hcc.Platform.Tests.Migrations;

/// <summary>
/// A08-05: the committed migration lock (<c>database/migrations.lock.json</c>, written by <c>tools/migrations-lock.mjs</c>). Same rules as the
/// tool: (a) a locked script never changes, is never removed or renamed; (b) a migration is numbered above every migration locked before it
/// (a lower number runs after the higher one on an existing database but before it on a fresh install), except the entries listed with a
/// reason as accepted out-of-order history; (c) every migration is in the lock.
/// </summary>
public static class MigrationLock
{
    public sealed record Entry(int Version, string File, string Sha256);

    public sealed record Accepted(int Version, string File, string Reason);

    public sealed record LockFile(IReadOnlyList<Entry> Migrations, IReadOnlyList<Accepted> AcceptedOutOfOrder);

    public static DirectoryInfo ProductionRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Hcc.slnx"))) return d;
        throw new InvalidOperationException("Hcc.slnx not found above the test output folder.");
    }

    public static string LockPath => Path.Combine(ProductionRoot().FullName, "database", "migrations.lock.json");

    public static LockFile Load() => Parse(File.ReadAllText(LockPath));

    public static LockFile Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var migrations = root.GetProperty("migrations").EnumerateArray()
            .Select(e => new Entry(e.GetProperty("version").GetInt32(), e.GetProperty("file").GetString()!, e.GetProperty("sha256").GetString()!)).ToList();
        var accepted = root.TryGetProperty("acceptedOutOfOrder", out var a)
            ? a.EnumerateArray().Select(e => new Accepted(e.GetProperty("version").GetInt32(), e.GetProperty("file").GetString()!,
                e.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "")).ToList()
            : [];
        return new LockFile(migrations, accepted);
    }

    public static string FileName(MigrationScript s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Version.ToString("0000", CultureInfo.InvariantCulture) + "_" + s.Name + ".sql";
    }

    private static string Pad(int v) => v.ToString("0000", CultureInfo.InvariantCulture);

    /// <summary>Every problem, one message each; empty = the lock and the migrations are in step.</summary>
    public static IReadOnlyList<string> Problems(LockFile lockFile, IReadOnlyList<MigrationScript> scripts)
    {
        ArgumentNullException.ThrowIfNull(lockFile);
        ArgumentNullException.ThrowIfNull(scripts);
        var problems = new List<string>();
        var byVersion = scripts.ToDictionary(s => s.Version);
        var accepted = new Dictionary<int, Accepted>();
        foreach (var a in lockFile.AcceptedOutOfOrder) accepted[a.Version] = a;
        var seen = new HashSet<int>();
        var high = 0;
        foreach (var e in lockFile.Migrations)
        {
            if (!seen.Add(e.Version)) problems.Add($"{Pad(e.Version)} is in the lock twice.");
            if (!byVersion.TryGetValue(e.Version, out var s))
                problems.Add($"(a) {e.File} is in the lock but its file is gone (a locked migration is never removed or renamed).");
            else if (!string.Equals(FileName(s), e.File, StringComparison.Ordinal))
                problems.Add($"(a) {Pad(e.Version)} is locked as {e.File} but the file is {FileName(s)} (a locked migration is never renamed).");
            else if (!string.Equals(s.Checksum, e.Sha256, StringComparison.Ordinal))
                problems.Add($"(a) {e.File} changed after it was locked (checksum drift on every database that applied it): add a new migration instead.");
            if (e.Version < high && !accepted.ContainsKey(e.Version))
                problems.Add($"(b) {e.File} comes after {Pad(high)} in the lock but is numbered below it, and is not accepted out-of-order history.");
            high = Math.Max(high, e.Version);
        }

        foreach (var a in accepted.Values)
        {
            if (!seen.Contains(a.Version)) problems.Add($"Accepted out-of-order entry {Pad(a.Version)} is not in the lock's migrations.");
            if (string.IsNullOrWhiteSpace(a.Reason)) problems.Add($"Accepted out-of-order entry {Pad(a.Version)} has no reason.");
        }

        foreach (var s in scripts.Where(s => !seen.Contains(s.Version)).OrderBy(s => s.Version))
        {
            if (s.Version < high)
                problems.Add($"(b) {FileName(s)} is numbered below the highest locked migration {Pad(high)}: on a database that already has {Pad(high)} it would run out of order. Renumber it above {Pad(high)}.");
            problems.Add($"(c) {FileName(s)} is not in migrations.lock.json: run node tools/migrations-lock.mjs --add (from 08 Production).");
        }

        return problems;
    }
}

public sealed class MigrationLockTests
{
    private static IReadOnlyList<MigrationScript> Embedded() => new EmbeddedMigrationSource().Load();

    /// <summary>A lock that records <paramref name="scripts"/> in version order (what the tool writes for them).</summary>
    private static MigrationLock.LockFile LockOf(IEnumerable<MigrationScript> scripts, params MigrationLock.Accepted[] accepted)
        => new(scripts.Select(s => new MigrationLock.Entry(s.Version, MigrationLock.FileName(s), s.Checksum)).ToList(), accepted);

    [Fact]
    public void Committed_lock_records_every_embedded_migration_with_the_runner_checksum()
    {
        var scripts = Embedded();
        var lockFile = MigrationLock.Load();
        Assert.Empty(MigrationLock.Problems(lockFile, scripts));
        Assert.Equal(scripts.Count, lockFile.Migrations.Count);
        // The folder the build embeds is the folder the tool hashes.
        var onDisk = Directory.EnumerateFiles(Path.Combine(MigrationLock.ProductionRoot().FullName, "database", "migrations"), "*.sql")
            .Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(onDisk, lockFile.Migrations.Select(e => e.File).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void A_changed_removed_or_renamed_locked_script_fails_the_check()
    {
        var scripts = Embedded();
        var lockFile = LockOf(scripts);
        var two = scripts.Single(s => s.Version == 2);

        var edited = scripts.Select(s => s.Version == 2 ? new MigrationScript(2, two.Name, two.Sql + "\n-- edited after release") : s).ToList();
        var p = Assert.Single(MigrationLock.Problems(lockFile, edited));
        Assert.Equal("(a) 0002_core_inventory_billing_payments.sql changed after it was locked (checksum drift on every database that applied it): add a new migration instead.", p);

        // Line endings alone are not a change: the runner (and the tool) hash the text with CRLF turned into LF.
        var crlf = scripts.Select(s => s.Version == 2 ? new MigrationScript(2, two.Name, two.Sql.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal)) : s).ToList();
        Assert.Empty(MigrationLock.Problems(lockFile, crlf));

        var removed = scripts.Where(s => s.Version != 2).ToList();
        Assert.StartsWith("(a) 0002_core_inventory_billing_payments.sql is in the lock but its file is gone", Assert.Single(MigrationLock.Problems(lockFile, removed)), StringComparison.Ordinal);

        var renamed = scripts.Select(s => s.Version == 2 ? new MigrationScript(2, "core_renamed", two.Sql) : s).ToList();
        Assert.StartsWith("(a) 0002 is locked as 0002_core_inventory_billing_payments.sql but the file is 0002_core_renamed.sql", Assert.Single(MigrationLock.Problems(lockFile, renamed)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_migration_numbered_below_the_highest_locked_version_fails_the_check()
    {
        var scripts = Embedded();
        var lockFile = LockOf(scripts);
        var high = scripts.Max(s => s.Version).ToString("0000", CultureInfo.InvariantCulture);
        var late = scripts.Append(new MigrationScript(302, "stays_late_fix", "SELECT 1;")).ToList(); // e.g. an in-block 0302 after the 3900s
        var problems = MigrationLock.Problems(lockFile, late);
        Assert.Equal(2, problems.Count);
        Assert.Equal($"(b) 0302_stays_late_fix.sql is numbered below the highest locked migration {high}: on a database that already has {high} it would run out of order. Renumber it above {high}.", problems[0]);
        Assert.StartsWith("(c) 0302_stays_late_fix.sql is not in migrations.lock.json", problems[1], StringComparison.Ordinal);

        // Hand-appending it to the lock does not get round the rule: a lower number after a higher one needs an accepted reason.
        var appended = new MigrationLock.LockFile([.. lockFile.Migrations, new MigrationLock.Entry(302, "0302_stays_late_fix.sql", late[^1].Checksum)], []);
        Assert.StartsWith("(b) 0302_stays_late_fix.sql comes after " + high + " in the lock but is numbered below it", Assert.Single(MigrationLock.Problems(appended, late)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_migration_missing_from_the_lock_fails_the_check()
    {
        var scripts = Embedded();
        var lockFile = LockOf(scripts);
        var next = scripts.Max(s => s.Version) + 1;
        var added = scripts.Append(new MigrationScript(next, "next_change", "SELECT 1;")).ToList();
        var p = Assert.Single(MigrationLock.Problems(lockFile, added)); // above the highest: only "not in the lock"
        Assert.Equal($"(c) {next.ToString("0000", CultureInfo.InvariantCulture)}_next_change.sql is not in migrations.lock.json: run node tools/migrations-lock.mjs --add (from 08 Production).", p);
        Assert.Empty(MigrationLock.Problems(LockOf(added), added));
    }

    [Fact]
    public void Out_of_order_entries_are_accepted_only_with_a_reason()
    {
        var a = new MigrationScript(1, "one", "SELECT 1;");
        var b = new MigrationScript(300, "three_hundred", "SELECT 300;");
        var c = new MigrationScript(200, "two_hundred", "SELECT 200;");
        var appendOrder = LockOf([a, b, c]);
        Assert.StartsWith("(b) 0200_two_hundred.sql comes after 0300", Assert.Single(MigrationLock.Problems(appendOrder, [a, b, c])), StringComparison.Ordinal);
        Assert.Empty(MigrationLock.Problems(LockOf([a, b, c], new MigrationLock.Accepted(200, "0200_two_hundred.sql", "Merged from a parallel branch")), [a, b, c]));
        Assert.Equal("Accepted out-of-order entry 0200 has no reason.",
            Assert.Single(MigrationLock.Problems(LockOf([a, b, c], new MigrationLock.Accepted(200, "0200_two_hundred.sql", " ")), [a, b, c])));
        Assert.Equal("Accepted out-of-order entry 0250 is not in the lock's migrations.",
            Assert.Single(MigrationLock.Problems(LockOf([a, c, b], new MigrationLock.Accepted(250, "0250_x.sql", "typo")), [a, b, c])));
    }

    [Fact]
    public void Masters_and_other_late_merged_migrations_are_recorded_as_accepted_history()
    {
        // Added after higher blocks existed (git history up to Wave C 125713d): applied out of order on a database created before them.
        int[] history = [203, 2202, 2800, 2801, 2802, 2810, 2820, 2830, 2840, 2850, 2860, 2870, 2871, 3100, 3101, 3500, 3501, 3800, 3910, 3920, 3921];
        var lockFile = MigrationLock.Load();
        Assert.Subset(lockFile.AcceptedOutOfOrder.Select(x => x.Version).ToHashSet(), history.ToHashSet());
        Assert.All(lockFile.AcceptedOutOfOrder, x => Assert.False(string.IsNullOrWhiteSpace(x.Reason)));
        Assert.All(lockFile.AcceptedOutOfOrder, x => Assert.Contains(lockFile.Migrations, e => e.Version == x.Version && e.File == x.File));
    }
}
