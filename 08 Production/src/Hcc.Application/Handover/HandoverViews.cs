using System.Globalization;
using Hcc.Application.Stays;
using Hcc.Contracts.Handover;
using Hcc.Domain.Handover;

namespace Hcc.Application.Handover;

/// <summary>Maps domain/store records to wire views (cash masked for readers without a money permission, R10).</summary>
public static class HandoverViews
{
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    public static string? Utc(DateTime? d) => d is null ? null : Utc(d.Value);

    public static HandoverTemplateView Template(HovTemplateRec t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return new(t.TemplateId, t.Name, t.Desk, t.Shift, t.Roles, t.Items.Select(i => new HandoverTemplateItemView(i.Key, i.Text, i.Mandatory, i.Due, i.Photo, i.Link)).ToList(),
            t.Active, t.Version, VersionToken.ToWire(t.RowVer));
    }

    public static ChecklistRunView Run(HovRun r, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(r);
        var p = HandoverRules.Progress(r, nowLocal);
        return new(r.RunId, r.TemplateId, r.TemplateVersion, r.Name, r.Desk, r.Shift, r.Roles, Date(r.Date), r.Open ? "Open" : "Submitted", r.Started, r.StartedBy,
            Utc(r.SubmittedUtc), r.SubmittedBy, r.ReopenReason,
            r.Items.Select(i => new ChecklistItemView(i.Key, i.Def.Text, i.Def.Mandatory, i.Def.Due, i.Def.Photo, i.Def.Link, i.Done, i.DoneBy, Utc(i.DoneUtc), i.Note, i.PhotoName,
                HandoverRules.ItemOverdue(r, i, nowLocal))).ToList(),
            new ChecklistProgressView(p.Total, p.Done, p.Pct, p.Mandatory, p.MandatoryDone, p.Overdue, p.Late));
    }

    public static HandoverNoteView Note(HovNoteRec n, bool seesCash)
    {
        ArgumentNullException.ThrowIfNull(n);
        return new(n.NoteId, HandoverRules.NoteRef(n.Desk, n.Date, n.Shift), n.Desk, Date(n.Date), n.Shift, n.ToShift, n.AuthorId, n.AuthorName, Utc(n.WrittenUtc), n.Issues, n.Nothing, n.Keys, n.Guests,
            n.CashPaise is null ? null : new HandoverCashView(seesCash ? n.CashPaise : null, seesCash ? n.CashNote : null),
            n.Carry.Select(c => new HandoverCarryView(c.CarryId, c.Text, c.Done, c.DoneBy, Utc(c.DoneUtc))).ToList(), n.Status, n.AckBy, Utc(n.AckUtc), n.AckRemark, VersionToken.ToWire(n.RowVer));
    }
}
