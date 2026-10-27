using System.Globalization;
using System.IO.Compression;
using Hcc.Application.Access;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Compliance;
using Hcc.Domain.Compliance;
using Hcc.Domain.Errors;

namespace Hcc.Application.Compliance;

/// <summary>
/// Reads (Owner + Accountant, PERM-CMP-001). <c>GET /compliance/state</c> carries everything SCC's calendar needs (the production client computes
/// the cards with the same SCC rules); the other reads are SCC's views computed on the server: month page, quarterly dashboard, 30-day
/// Action Center (badge), CSV register, month print, the ≈₹ payable feed from HCC's own data, an evidence file and the evidence ZIP.
/// The badge never throws: no access = 0.
/// </summary>
public sealed class ComplianceQueries(IAccessPolicy policy, IComplianceReadStore reads)
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static SettingsDto Settings(SettingsRecord s) => new(s.CaName, s.CaEmail, s.DueSoonDays, VersionToken.ToWire(s.Version));

    private async Task<(ComplianceSnapshot Snap, SccCalendar Cal, DateOnly Today)> LoadAsync(int propertyId, int principalId, CancellationToken ct)
    {
        await policy.RequireAsync(propertyId, principalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var snap = await reads.SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        return (snap, snap.Calendar(), await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false));
    }

    private static int FyOr(int? fy, ComplianceSnapshot s) => fy is { } f
        ? f is >= SccTaxRules.FyMin and <= SccTaxRules.FyMax ? f : throw HccException.Validation("Choose a financial year from FY 2026–27 to FY 2032–33.")
        : s.Settings.FyStart;

    private static RecordDto Rec(CmpRecord r) => new(r.FyStart, r.ItemId, r.Done, r.Na, ComplianceWire.Date(r.FiledOn), r.Notes, r.EvidenceRef, r.ReviewerNote,
        ComplianceWire.Date(r.DueDate), r.DoneBy, ComplianceWire.Utc(r.DoneUtc), r.NaBy, ComplianceWire.Utc(r.NaUtc), r.Legacy);

    public async Task<ComplianceStateView> GetStateAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var (s, _, today) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var p = s.Profile;
        return new ComplianceStateView(D(today),
            p is null ? null : new ProfileDto(p.Profile.Name, p.Profile.Pan, p.Profile.Gstin, p.Profile.Entity, p.Profile.GstType, p.Profile.HasEmployees, p.Profile.Qrmp, p.Profile.Notes,
                p.UpdatedBy, ComplianceWire.Utc(p.UpdatedUtc), VersionToken.ToWire(p.Version)),
            s.Settings.FyStart, s.Records.Select(Rec).ToList(),
            s.Custom.Select(c => new CustomObligationDto(c.Custom.CustomId, c.Custom.Id, c.Custom.Cat, c.Custom.Name, c.Custom.Freq, c.Custom.DueDay, c.Custom.Months, c.Custom.Form,
                c.Custom.Who, c.Custom.What, c.Custom.Why, c.Custom.Penalty, c.Custom.Responsible, c.Custom.Reviewer, c.Custom.Risk, c.Custom.Buffer, c.Custom.Checklist,
                c.Custom.Workflow, c.Custom.Evidence, c.UpdatedBy, ComplianceWire.Utc(c.UpdatedUtc), VersionToken.ToWire(c.Version))).ToList(),
            s.Archived, s.ArchivedCats,
            s.Evidence.Select(e => new EvidenceDto(e.EvidenceId, e.FyStart, e.ItemId, e.FileName, e.ContentType, e.SizeBytes, e.AddedBy, ComplianceWire.Utc(e.AddedUtc))).ToList(),
            s.Reopened.Select(r => new ReopenedDto(r.FyStart, r.ItemId, r.Part, r.Was.Done, r.Was.Na, ComplianceWire.Date(r.Was.FiledOn), r.Was.Notes, r.Was.EvidenceRef,
                r.Was.ReviewerNote, ComplianceWire.Date(r.Was.DueDate), r.Was.Done ? r.Was.DoneBy : r.Was.NaBy, ComplianceWire.Utc(r.Was.Done ? r.Was.DoneUtc : r.Was.NaUtc),
                r.ReopenedBy, ComplianceWire.Utc(r.ReopenedUtc), r.Reason)).ToList(),
            Settings(s.Settings));
    }

    /// <summary>The ≈₹ feed for the FY's chips (March of the FY … February after) and the panel (the business month), as SCC reads its bridge feed.</summary>
    private async Task<IReadOnlyDictionary<string, CmpPayableMonth>> FeedAsync(int propertyId, int fy, DateOnly today, CancellationToken ct)
    {
        var cur = new DateOnly(today.Year, today.Month, 1);
        var from = new DateOnly(fy, 3, 1);
        var to = new DateOnly(fy + 1, 2, 1);
        if (to > cur) to = cur;
        var months = new List<CmpPayableMonth>();
        if (from <= to) months.AddRange(await reads.PayableAsync(propertyId, from, to, ct).ConfigureAwait(false));
        if (cur < from || cur > to) months.AddRange(await reads.PayableAsync(propertyId, cur, cur, ct).ConfigureAwait(false));
        return months.GroupBy(m => m.Ym, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public async Task<PayableView> GetPayableAsync(int propertyId, int principalId, int? fy, CancellationToken ct = default)
    {
        var (s, _, today) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var feed = await FeedAsync(propertyId, FyOr(fy, s), today, ct).ConfigureAwait(false);
        return new PayableView(feed.ToDictionary(kv => kv.Key, kv => new PayableMonthDto(kv.Value.Pf, kv.Value.Esic, kv.Value.Pt, kv.Value.Gst, kv.Value.Sources, D(today)), StringComparer.Ordinal),
            ComplianceRules.PayableNote, ComplianceRules.PayableBasis);
    }

    public async Task<MonthView> GetMonthAsync(int propertyId, int principalId, int? fy, string? month, CancellationToken ct = default)
    {
        var (s, cal, today) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var y = FyOr(fy, s);
        var m = string.IsNullOrWhiteSpace(month) ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(today.Month) : month.Trim();
        if (!SccTaxCatalogue.Months.Contains(m)) throw HccException.Validation("Choose a month from April to March.");
        var feed = await FeedAsync(propertyId, y, today, ct).ConfigureAwait(false);
        var (cards, st) = cal.MonthPage(m, y, today);
        var p = s.Profile?.Profile;
        return new MonthView(D(today), y, m, SccTaxRules.CalendarYearForMonth(m, y), SccTaxRules.RenderFyText(SccTaxCatalogue.Note(m), y),
            new MonthStatsDto(st.Total, st.Completed, st.Pending, st.HighRisk, st.NotApplicable, st.Overdue, st.Evidence), cal.HasOverdueInMonth(m, y, today),
            cards.Select(v => new CardDto(v.Card.Id, m, v.Card.Cat, v.Name, v.Form, v.Card.Due, ComplianceWire.Date(v.Due), v.Card.Urgent, v.Card.Freq, v.Card.IsCustom, v.IsDone, v.IsNa,
                v.Days, v.Tag?.Text, v.IsOverdue, v.Control.Risk, v.Control.CashFlow, v.Control.Responsible, v.Control.Reviewer, v.Control.Buffer, ComplianceWire.Date(v.Control.InternalDate),
                v.Control.Escalation.Label, SccCalendar.PayableAmount(feed, v.Card.Name, m, y), SccTaxRules.QrmpAdvisory(v.Card, p?.GstType, p?.Qrmp == true, p is not null)?.Text,
                ComplianceWire.Date(v.Record?.FiledOn), v.Record?.EvidenceRef, v.Record?.ReviewerNote, v.Files)).ToList());
    }

    public async Task<DashboardView> GetDashboardAsync(int propertyId, int principalId, int? fy, string? quarter, CancellationToken ct = default)
    {
        var (s, cal, today) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var y = FyOr(fy, s);
        var q = cal.Quarter(string.IsNullOrWhiteSpace(quarter) ? "Q1" : quarter.Trim(), y, today);
        var ev = cal.EvidenceStatus("fy", y);
        return new DashboardView(D(today), y, q.Key, q.Total, q.Done, q.Pending, q.Overdue, q.HighRisk, q.Score,
            q.Quarters.Select(x => new QuarterScoreDto(x.Key, x.Label, x.Total, x.Score)).ToList(), q.Monthly.Select(r => new QuarterMonthDto(r.Month, r.Total, r.Done, r.Overdue, r.Score)).ToList(),
            q.CashHigh, q.CashMedium, q.CashLow, q.OwnerLoad.ToDictionary(o => o.Owner, o => o.Count, StringComparer.Ordinal),
            q.Focus.Select(i => new FocusItemDto(i.Card.Id, i.Month, SccTaxRules.RenderFyText(i.Card.Name, y), i.Card.Due, i.Control.Risk, i.Control.Responsible, i.Control.Reviewer,
                i.Control.Escalation.Label, ComplianceWire.Date(i.Control.InternalDate))).ToList(),
            ev.Pct, ev.DoneTotal, ev.DoneSatisfied, ev.Missing.Select(x => x.Id).ToList());
    }

    public async Task<ActionCenterView> GetActionCenterAsync(int propertyId, int principalId, int? fy, CancellationToken ct = default)
    {
        var (s, cal, today) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var y = FyOr(fy, s);
        var feed = await FeedAsync(propertyId, y, today, ct).ConfigureAwait(false);
        var rows = cal.ActionCenter(y, today);
        return new ActionCenterView(D(today), y, rows.Count(r => r.Days < 0), rows.Count(r => r.Days >= 0),
            rows.Select(r => new ActionRowDto(r.ItemId, r.Month, SccTaxRules.RenderFyText(r.Name, y), SccTaxRules.RenderFyText(r.Form, y), r.Due, ComplianceWire.Date(r.DueDate), r.Cat, r.Days,
                ComplianceRules.When(r.Days), SccCalendar.PayableAmount(feed, r.Name, r.Month, y))).ToList());
    }

    /// <summary>HCC extra kept: the Action Center rows inside the "Due this week" window (settings.dueSoonDays).</summary>
    public async Task<ActionCenterView> GetDueThisWeekAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var (s, cal, today) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var y = s.Settings.FyStart;
        var rows = cal.ActionCenter(y, today).Where(r => r.Days <= s.Settings.DueSoonDays).ToList();
        return new ActionCenterView(D(today), y, rows.Count(r => r.Days < 0), rows.Count(r => r.Days >= 0),
            rows.Select(r => new ActionRowDto(r.ItemId, r.Month, SccTaxRules.RenderFyText(r.Name, y), SccTaxRules.RenderFyText(r.Form, y), r.Due, ComplianceWire.Date(r.DueDate), r.Cat, r.Days,
                ComplianceRules.When(r.Days), null)).ToList());
    }

    /// <summary>SCC updateActionBadge: the Action Center count of the selected FY.</summary>
    public async Task<BadgeResult> GetBadgeAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        if (!await policy.HasAsync(propertyId, principalId, CompliancePermissions.Use, null, ct).ConfigureAwait(false)) return new BadgeResult(0);
        var snap = await reads.SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        var today = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        return new BadgeResult(snap.Calendar().ActionCenter(snap.Settings.FyStart, today).Count);
    }

    public async Task<RegisterCsvView> GetRegisterCsvAsync(int propertyId, int principalId, int? fy, CancellationToken ct = default)
    {
        var (s, cal, today) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var csv = cal.Csv(FyOr(fy, s), today) ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "Set the firm profile first to export its compliance register.");
        return new RegisterCsvView(csv.FileName, csv.Csv, csv.Rows);
    }

    /// <summary>SCC printCompliance (L59–136): the month's visible cards (A4 landscape on the client).</summary>
    public async Task<PrintView> GetPrintAsync(int propertyId, int principalId, int? fy, string month, CancellationToken ct = default)
    {
        var (s, cal, today) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var y = FyOr(fy, s);
        var m = (month ?? string.Empty).Trim();
        if (!SccTaxCatalogue.Months.Contains(m)) throw HccException.Validation("Choose a month from April to March.");
        var items = cal.MonthCards(m);
        var done = items.Count(i => !cal.IsNa(y, i.Id) && cal.IsDone(y, i.Id));
        var na = items.Count(i => cal.IsNa(y, i.Id));
        var f = s.Profile?.Profile;
        var firm = f?.Name ?? "All Firms";
        var rows = items.Select(i =>
        {
            var isNa = cal.IsNa(y, i.Id);
            var isDone = !isNa && cal.IsDone(y, i.Id);
            return new PrintRowDto(i.Due, SccTaxRules.StripHtml(SccTaxRules.RenderFyText(i.Name, y)), SccTaxRules.StripHtml(SccTaxRules.RenderFyText(i.Form, y)), i.Freq,
                SccTaxRules.ControlProfile(i, y, today).Responsible, isNa ? "N/A" : isDone ? "✓ Done" : "Pending");
        }).ToList();
        string Nn(int n) => n.ToString(CultureInfo.InvariantCulture);
        return new PrintView("Compliance — " + m + " — " + firm, m, SccTaxRules.CalendarYearForMonth(m, y), firm, f is null ? string.Empty : SccTaxRules.TypeLabel(f.GstType),
            SccTaxRules.FyLabel(y), items.Count, done, na, rows,
            $"{Nn(items.Count)} obligation{(items.Count == 1 ? string.Empty : "s")} · {Nn(done)} done · {Nn(items.Count - na - done)} pending{(na > 0 ? $" · {Nn(na)} N/A" : string.Empty)}");
    }

    public async Task<EvidenceFileView> GetEvidenceFileAsync(int propertyId, int principalId, long evidenceId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var f = await reads.EvidenceFileAsync(propertyId, evidenceId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Evidence file not found.");
        return new EvidenceFileView(f.Meta.FileName, f.Meta.ContentType, Convert.ToBase64String(f.Content));
    }

    /// <summary>SCC exportEvidenceZip (L2045–2098): folders &lt;Month&gt;_&lt;Category&gt;_&lt;Name&gt;__&lt;itemId&gt;, de-duplicated names, index.csv.</summary>
    public async Task<EvidenceZipView> GetEvidenceZipAsync(int propertyId, int principalId, int? fy, CancellationToken ct = default)
    {
        var (s, cal, _) = await LoadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (s.Profile is null) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Set the firm profile first to export its evidence pack.");
        var y = FyOr(fy, s);
        var plan = cal.ZipPlan(y);
        var files = (await reads.EvidenceFilesAsync(propertyId, y, ct).ConfigureAwait(false)).ToDictionary(f => f.Meta.EvidenceId);
        using var ms = new MemoryStream();
        var n = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (id, path) in plan.Entries)
            {
                if (!files.TryGetValue(id, out var f)) continue;
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using (var w = entry.Open()) w.Write(f.Content);
                n++;
            }

            if (n > 0)
            {
                var index = zip.CreateEntry("index.csv", CompressionLevel.Optimal);
                using var w = index.Open();
                w.Write(System.Text.Encoding.UTF8.GetBytes(plan.Index));
            }
        }

        return new EvidenceZipView(plan.FileName, n, n > 0 ? Convert.ToBase64String(ms.ToArray()) : string.Empty);
    }

    public async Task<IReadOnlyList<CaPackDto>> GetCaPacksAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        return (await reads.PacksAsync(propertyId, ct).ConfigureAwait(false))
            .Select(p => new CaPackDto(p.PackId, ComplianceWire.Utc(p.AtUtc), p.By, p.To, p.ToName, p.Overdue, p.Due, p.Filed, p.NotApplicable, p.Lines)).ToList();
    }
}
