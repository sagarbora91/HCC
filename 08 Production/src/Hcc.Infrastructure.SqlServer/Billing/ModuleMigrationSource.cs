using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Infrastructure.SqlServer.Migrations;

namespace Hcc.Infrastructure.SqlServer.Billing;

/// <summary>
/// Platform migrations (0001…, contiguous, frozen <see cref="EmbeddedMigrationSource"/>) followed by Wave 1 module migrations
/// embedded from <c>database/modules/NNNN_name.sql</c> (block-numbered, e.g. P4 = 0400–0499, ascending, unique).
/// Interim until the frozen runner accepts block numbers in <c>database/migrations/</c> ("Requested changes" in the P4 report);
/// <see cref="MigrationRunner"/> applies pending scripts in list order and does not require contiguity itself.
/// </summary>
public sealed partial class ModuleMigrationSource : IMigrationSource
{
    private const string Prefix = "Hcc.ModuleMigrations.";

    [GeneratedRegex(@"^(\d{4})_([a-z0-9_]+)\.sql$")]
    private static partial Regex FileName();

    public IReadOnlyList<MigrationScript> Load()
    {
        var platform = new EmbeddedMigrationSource().Load();
        var asm = typeof(ModuleMigrationSource).Assembly;
        var modules = new List<MigrationScript>();
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var file = res[Prefix.Length..];
            var m = FileName().Match(file);
            if (!m.Success) throw new InvalidOperationException($"Module migration file name '{file}' must look like 0400_name.sql.");
            using var stream = asm.GetManifestResourceStream(res) ?? throw new InvalidOperationException($"Missing resource {res}.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            modules.Add(new MigrationScript(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value, reader.ReadToEnd()));
        }

        var last = platform.Count == 0 ? 0 : platform[^1].Version;
        var ordered = modules.OrderBy(s => s.Version).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Version <= last || (i > 0 && ordered[i].Version == ordered[i - 1].Version))
                throw new InvalidOperationException($"Module migration {ordered[i].Version:0000} must be unique and above platform version {last:0000}.");
        }

        return [.. platform, .. ordered];
    }
}
