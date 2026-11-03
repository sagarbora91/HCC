// F7-R7 · renders a DocumentModel to the file the caller asked for (html = the Lodge-look printable page, pdf = MinimalPdfWriter through the engine's PdfRenderer) and writes
// the rpt.export_log row (rule V.4: every print and export is logged). The model has already been built from a view the caller was allowed to read.
using System.Text;
using System.Text.Json;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;

namespace Hcc.Reporting.Documents;

public sealed class DocumentService(IReportPropertyInfo property, IReportExportLog exportLog, TimeProvider clock)
{
    public static ReportFormat ParseFormat(string? f) => f?.ToLowerInvariant() switch
    {
        null or "" or "html" or "print" => ReportFormat.Print,
        "pdf" => ReportFormat.Pdf,
        _ => throw HccException.Validation("format must be html or pdf."),
    };

    /// <summary>Property name for the letterhead (the builders take it as a string).</summary>
    public async Task<string> PropertyNameAsync(int propertyId, CancellationToken ct = default) => (await property.GetAsync(propertyId, ct).ConfigureAwait(false)).Name;

    public async Task<RenderedFile> RenderAsync(int propertyId, int principalId, DocumentModel m, string? format, IReadOnlyDictionary<string, string>? parameters = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(m);
        var f = ParseFormat(format);
        var prop = await property.GetAsync(propertyId, ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        var rows = m.Tables.Sum(t => t.Rows.Count);
        var safe = new string(m.Number.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' ? ch : '_').ToArray());
        RenderedFile file;
        if (f == ReportFormat.Pdf)
        {
            var result = DocumentResults.ToReport(m, at, "User " + principalId);
            file = new PdfRenderer().Render(result, new RenderContext(prop.Code, prop.Name, ReportFormat.Pdf)) with { FileName = $"{prop.Code}-{m.Id}-{safe}.pdf" };
        }
        else file = new RenderedFile($"{prop.Code}-{m.Id}-{safe}.html", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(DocumentHtml.FullPage(m)));
        await exportLog.AppendAsync(propertyId, new ReportExportLogRecord(0, at, principalId, m.Id, JsonSerializer.Serialize(parameters ?? new Dictionary<string, string>()), f, rows, file.FileName, false), ct).ConfigureAwait(false);
        return file;
    }
}
