using Hcc.Application.Documents;

namespace Hcc.Infrastructure.SqlServer.DocumentObjects;

/// <summary>
/// LF-docdb: the four <c>Hcc</c> tables whose content moved to <c>HccDocuments</c> (contract §1.1), with the SQL the move job, the sweep and the
/// reference check share. <c>object_ref</c> sits on the content tables themselves (migration 4670).
/// </summary>
public sealed record DocumentContentTable(DocumentObjectKind Kind, string Table, string Key, string ContentColumn, bool Sealed, string OwnerTable, string ParentTable, string ContentTypeExpr)
{
    /// <summary>Rows still holding their content in Hcc (the move job's work list), after key @after, for @p's property is not needed: all properties.</summary>
    public string SelectToMoveSql => $"""
        SELECT TOP (@take) t.{Key}, p.property_id, {ContentTypeExpr}, t.{ContentColumn}
        FROM {Table} t JOIN {ParentTable} p ON p.{Key} = t.{Key}
        WHERE t.object_ref IS NULL AND t.{ContentColumn} IS NOT NULL AND t.{Key} > @after
          AND {(Sealed ? $"LEN(t.{ContentColumn})" : $"DATALENGTH(t.{ContentColumn})")} > 0
        ORDER BY t.{Key};
        """;

    public string SetRefSql => $"UPDATE {Table} SET object_ref = @ref, {ContentColumn} = NULL WHERE {Key} = @k AND object_ref IS NULL;";

    public string RemainingSql => $"SELECT COUNT(*) FROM {Table} t WHERE t.object_ref IS NULL AND t.{ContentColumn} IS NOT NULL AND {(Sealed ? $"LEN(t.{ContentColumn})" : $"DATALENGTH(t.{ContentColumn})")} > 0;";

    public string RefsSql => $"SELECT t.object_ref FROM {Table} t JOIN {ParentTable} p ON p.{Key} = t.{Key} WHERE p.property_id = @p AND t.object_ref IS NOT NULL;";

    public static readonly IReadOnlyList<DocumentContentTable> All =
    [
        new(DocumentObjectKind.IdImage, "doc.id_capture_image", "capture_id", "sealed", true, "doc.id_capture", "doc.id_capture", "p.content_type"),
        new(DocumentObjectKind.ExpenseBill, "ops.expense_bill_photo", "expense_id", "image", false, "ops.expense", "ops.expense", "t.content_type"),
        new(DocumentObjectKind.BankingSlip, "pay.banking_slip", "transfer_id", "image", false, "pay.banking_transfer", "pay.cash_transfer", "t.content_type"),
        new(DocumentObjectKind.ComplianceEvidence, "cmp.evidence_content", "evidence_id", "content", false, "cmp.evidence", "cmp.evidence", "LEFT(p.content_type, 30)"),
    ];

    public static DocumentContentTable For(DocumentObjectKind kind) => All.First(t => t.Kind == kind);
}
