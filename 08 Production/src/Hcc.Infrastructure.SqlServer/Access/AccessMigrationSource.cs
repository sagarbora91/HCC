using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Infrastructure.SqlServer.Migrations;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>
/// Platform migrations (0001…, contiguous) plus the P1 access block 0100–0199 (<c>database/migrations/access/*.sql</c>).
/// Needed because the frozen <see cref="EmbeddedMigrationSource"/> requires versions contiguous from 0001, which rejects the
/// Wave 1 migration blocks; see the P1 report "Requested changes". Once the runner accepts blocks, the access scripts move into
/// <c>database/migrations/</c> and this class is deleted (same versions and checksums, so applied journals stay valid).
/// </summary>
public sealed partial class AccessMigrationSource : IMigrationSource
{
    public const int BlockStart = 100;
    public const int BlockEnd = 199;
    private const string Prefix = "Hcc.AccessMigrations.";

    [GeneratedRegex(@"^(\d{4})_([a-z0-9_]+)\.sql$")]
    private static partial Regex FileName();

    public IReadOnlyList<MigrationScript> Load()
    {
        var platform = new EmbeddedMigrationSource().Load();
        var access = LoadAccessScripts();
        var all = platform.Concat(access).ToList();
        if (all.Select(s => s.Version).Distinct().Count() != all.Count) throw new InvalidOperationException("Duplicate migration version.");
        return all.OrderBy(s => s.Version).ToList();
    }

    public static IReadOnlyList<MigrationScript> LoadAccessScripts()
    {
        var asm = typeof(AccessMigrationSource).Assembly;
        var list = new List<MigrationScript>();
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var file = res[Prefix.Length..];
            var m = FileName().Match(file);
            if (!m.Success) throw new InvalidOperationException($"Migration file name '{file}' must look like 0100_name.sql.");
            var version = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (version is < BlockStart or > BlockEnd) throw new InvalidOperationException($"Access migration {file} is outside block {BlockStart:0000}–{BlockEnd:0000}.");
            using var stream = asm.GetManifestResourceStream(res) ?? throw new InvalidOperationException($"Missing resource {res}.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            list.Add(new MigrationScript(version, m.Groups[2].Value, reader.ReadToEnd()));
        }

        var ordered = list.OrderBy(s => s.Version).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Version != BlockStart + i)
                throw new InvalidOperationException($"Access migrations must be contiguous from {BlockStart:0000}; found {ordered[i].Version:0000}.");
        }

        return ordered;
    }

    /// <summary>A migration runner over platform + access migrations for one database.</summary>
    public static MigrationRunner Runner(SqlPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return new MigrationRunner(platform.Connections, new AccessMigrationSource(), platform.BackupGate);
    }
}
