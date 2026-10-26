using System.Text;
using Hcc.Domain.Import;
using Hcc.Import.Tests.Harness;

namespace Hcc.Import.Tests.Kernel;

/// <summary>
/// Maintenance tool, not a test of behaviour: with HCC_UPDATE_TEMPLATES=1 it rewrites the header-only templates of the MA-8 masters
/// (list masters, POS outlets and menu) from the column specs and writes the README column tables of every master to
/// deployment/import-templates/.readme-columns.md (pasted into README.md by hand; the file is then deleted). Without the variable it does nothing.
/// </summary>
public class TemplateGenerator
{
    private static readonly string[] Regenerate = ["pos-outlets", "pos-menu"];

    [Fact]
    public void Update_templates_when_asked()
    {
        if (Environment.GetEnvironmentVariable("HCC_UPDATE_TEMPLATES") != "1") return;
        var md = new StringBuilder();
        foreach (var spec in ImportMasters.All)
        {
            var isNew = ImportLists.Find(spec.Master) is not null || Regenerate.Contains(spec.Wire);
            if (!isNew) continue;
            File.WriteAllText(Path.Combine(Harness.Templates.Folder, "templates", spec.Wire + ".csv"), string.Join(',', spec.Columns.Select(c => c.Name)) + "\n");
            md.Append($"### {spec.Wire}.csv ({spec.Title})\n| Column | Required | Meaning |\n|---|---|---|\n");
            foreach (var c in spec.Columns) md.Append($"| {c.Name} | {(c.Required ? "yes" : "optional")} | {c.Meaning.Replace("|", "/", StringComparison.Ordinal)} |\n");
            md.Append('\n');
        }

        File.WriteAllText(Path.Combine(Harness.Templates.Folder, ".readme-columns.md"), md.ToString());
    }
}
