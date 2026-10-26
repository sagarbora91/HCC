using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Application.Yield;
using Hcc.Contracts;
using Hcc.Contracts.Import;
using Hcc.Domain.Errors;
using Hcc.Domain.Import;
using Hcc.Domain.Money;

namespace Hcc.Application.Import;

/// <summary>
/// Masters &amp; opening balances import (W4-13): UploadImportBatch → GET batch (validate + preview) → CommitImportBatch (one transaction,
/// idempotent on the Idempotency-Key, a committed batch is never applied twice, re-importing the same file plans only "Unchanged"; dry run
/// = the whole commit rolled back). Writes go through the owning modules' ports: <see cref="IImportInventoryWriter"/> (room types, rooms),
/// Yield's <see cref="IRatePublicationPort"/> (BAR tariffs) and <see cref="IYieldStore"/> (seasons + yield history), <see cref="IImportAccessWriter"/>
/// (users, no credential), <see cref="IBillingStore"/> (tills; opening cash = a counted, closed opening shift so the first OpenShift carries it,
/// DEC-035), <see cref="IStaysStore"/> (guest profiles, never an ID number) and the module's own ledgers / opening trial balance.
/// </summary>
public sealed class ImportService(
    ICommandRunner runner,
    IAccessPolicy policy,
    IImportStore store,
    IImportInventoryWriter inventory,
    IImportAccessWriter access,
    IRatePublicationPort rates,
    IYieldStore yield,
    IBillingStore billing,
    IStaysStore stays,
    TimeProvider clock) : IOpeningBalanceSource
{
    public const string OpUpload = "UploadImportBatch";
    public const string OpCommit = "CommitImportBatch";
    public const string OpeningShiftReason = "Opening cash (import)";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static ImportMasterSpec Spec(string? master) =>
        ImportMasters.Find(master) ?? throw HccException.Validation("Choose what to import: " + string.Join(", ", ImportMasters.All.Select(m => m.Wire)) + ".");

    public async Task<ImportMastersView> MastersAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, ImportPermissions.Prepare, ct: ct).ConfigureAwait(false);
        var snap = await store.SnapshotAsync(null, propertyId, ct).ConfigureAwait(false);
        return new ImportMastersView(!snap.AnyDayClosed, snap.BusinessDate, ImportMasters.All.Select(m => new ImportMasterView(m.Wire, m.Title, m.OwnerOnlyCommit, m.SetupStateOnly,
            m.DependsOn, m.Columns.Select(c => new ImportColumnView(c.Name, c.Required, c.Meaning, c.Example)).ToList())).ToList());
    }

    /// <summary>Parses + plans a CSV text against a snapshot (structural CSV problems become a row-0 error).</summary>
    public static ImportPlan PlanText(ImportMasterSpec spec, string csvText, ImportSnapshot snap, DateOnly? asOf)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var table = CsvParser.TryParse(csvText, out var error);
        if (table is null) return new ImportPlan(spec.Master, 0, [new ImportRowError(0, "", error!)], [], [], null, null);
        return ImportPlanner.Plan(spec.Master, table, snap, asOf);
    }

    public async Task<CommandResult<ImportBatchReport>> UploadAsync(CommandContext ctx, UploadImportBatchRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ImportPermissions.Prepare, ct: ct).ConfigureAwait(false);
        var spec = Spec(req.Master);
        var fileName = (req.FileName ?? string.Empty).Trim();
        if (fileName.Length == 0) fileName = spec.Wire + ".csv";
        if (fileName.Length > 200) throw HccException.Validation("The file name is at most 200 characters.");
        var text = req.CsvText ?? string.Empty;
        if (text.Length > CsvParser.MaxChars) throw HccException.Validation($"The file is too large (at most {CsvParser.MaxChars:N0} characters). Split it into smaller files.");
        DateOnly? asOf = null;
        if (spec.Master == ImportMaster.OpeningBalances && !string.IsNullOrWhiteSpace(req.AsOfDate))
            asOf = ImportValues.TryDate(req.AsOfDate, out var d) ? d : throw HccException.Validation("The opening balances date is not a date. Use YYYY-MM-DD.");
        var sha = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return await runner.RunAsync(ctx with { Operation = OpUpload }, req, async (tx, c) =>
        {
            var snap = await store.SnapshotAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var plan = PlanText(spec, text, snap, asOf);
            var now = Now;
            var id = await store.InsertBatchAsync(tx, ctx.PropertyId, spec.Wire, fileName, sha, text, asOf, plan.Rows, plan.Errors.Count, ctx.PrincipalId, now, c).ConfigureAwait(false);
            await tx.AuditAsync("IMPORT_UPLOAD", $"import:{id.ToString(CultureInfo.InvariantCulture)}",
                Clip($"{spec.Title} · {fileName} · {plan.Rows} rows · {plan.Errors.Count} errors · {plan.Counts.Create} new, {plan.Counts.Update} changed, {plan.Counts.Unchanged} unchanged", 1000), c).ConfigureAwait(false);
            var batch = new ImportBatchRecord(id, ctx.PropertyId, spec.Wire, fileName, text, asOf, plan.Rows, plan.Errors.Count, "Uploaded", ctx.PrincipalId, now, null, null, null);
            return Report(batch, spec, plan);
        }, ct).ConfigureAwait(false);
    }

    public async Task<ImportBatchReport> GetBatchAsync(int propertyId, int principalId, long batchId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, ImportPermissions.Prepare, ct: ct).ConfigureAwait(false);
        var b = await store.GetBatchAsync(null, propertyId, batchId, false, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Import batch not found.");
        var spec = Spec(b.Master);
        if (b.Status == "Committed" && b.ResultJson is not null)
        {
            var r = JsonSerializer.Deserialize<ImportCommitResult>(b.ResultJson, CanonicalJson.SerializerOptions)!;
            return new ImportBatchReport(b.BatchId, b.Master, b.FileName, b.Status, b.AsOfDate, b.Rows, 0, [], [], r.Counts, r.Plan, r.DebitPaise, r.CreditPaise, false,
                "This batch was committed already.", Iso(b.UploadedUtc), b.UploadedBy, b.CommittedUtc is { } cu ? Iso(cu) : null, b.CommittedBy);
        }

        var snap = await store.SnapshotAsync(null, propertyId, ct).ConfigureAwait(false);
        return Report(b, spec, PlanText(spec, b.CsvText, snap, b.AsOfDate));
    }

    public async Task<ImportBatchList> ListAsync(int propertyId, int principalId, int limit = 50, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, ImportPermissions.Prepare, ct: ct).ConfigureAwait(false);
        var rows = await store.ListBatchesAsync(propertyId, Math.Clamp(limit, 1, 200), ct).ConfigureAwait(false);
        return new ImportBatchList(rows.Select(b => new ImportBatchSummary(b.BatchId, b.Master, b.FileName, b.Status, b.Rows, b.Errors, Iso(b.UploadedUtc), b.UploadedBy,
            b.CommittedUtc is { } cu ? Iso(cu) : null, b.CommittedBy)).ToList());
    }

    public async Task<OpeningBalancesView> OpeningBalancesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, ImportPermissions.Prepare, ct: ct).ConfigureAwait(false);
        return await GetAsync(propertyId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OpeningBalancesView> GetAsync(int propertyId, CancellationToken ct = default)
    {
        var snap = await store.SnapshotAsync(null, propertyId, ct).ConfigureAwait(false);
        var batch = await store.OpeningBatchAsync(propertyId, ct).ConfigureAwait(false);
        var rows = snap.Opening.Select(o =>
        {
            var l = ImportPlanner.Ledger(snap, o.LedgerCode);
            return new OpeningBalanceRowView(o.LedgerCode, l?.Name ?? o.LedgerCode, l?.Group ?? "?", l?.HccAccount, o.DebitPaise, o.CreditPaise);
        }).OrderBy(r => r.LedgerCode, StringComparer.Ordinal).ToList();
        var d = rows.Sum(r => r.DebitPaise);
        var c = rows.Sum(r => r.CreditPaise);
        return new OpeningBalancesView(snap.OpeningAsOf, batch, d, c, d == c, rows);
    }

    public async Task<ImportLedgersView> LedgersAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, ImportPermissions.Prepare, ct: ct).ConfigureAwait(false);
        var snap = await store.SnapshotAsync(null, propertyId, ct).ConfigureAwait(false);
        return new ImportLedgersView(snap.Ledgers.Select(l => new ImportLedgerView(l.Code, l.Name, l.Group, l.HccAccount)).ToList());
    }

    private sealed class DryRunRollback(ImportCommitResult result) : Exception("Dry run: rolled back.")
    {
        public ImportCommitResult Result { get; } = result;
    }

    public async Task<CommandResult<ImportCommitResult>> CommitAsync(CommandContext ctx, CommitImportBatchRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ImportPermissions.Prepare, ct: ct).ConfigureAwait(false);
        var peek = await store.GetBatchAsync(null, ctx.PropertyId, req.BatchId, false, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Import batch not found.");
        var spec = Spec(peek.Master);
        if (!req.DryRun) await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ImportPermissions.CommitFor(spec), ct: ct).ConfigureAwait(false);
        try
        {
            return await runner.RunAsync(ctx with { Operation = OpCommit }, req, async (tx, c) =>
            {
                await store.LockImportsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
                var b = (await store.GetBatchAsync(tx, ctx.PropertyId, req.BatchId, true, c).ConfigureAwait(false))!;
                if (b.Status == "Committed") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This batch was committed already. Upload the file again to import it again.");
                var snap = await store.SnapshotAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
                if (spec.SetupStateOnly && snap.AnyDayClosed)
                    throw new HccException(HccErrorCode.DAY_CLOSED, $"{spec.Title} can be imported only while HCC is being set up; the first business day is already closed.");
                var plan = PlanText(spec, b.CsvText, snap, b.AsOfDate);
                if (!plan.Valid)
                {
                    var details = new Dictionary<string, JsonElement>
                    {
                        ["errors"] = JsonSerializer.SerializeToElement(plan.Errors.Take(50).ToList(), CanonicalJson.SerializerOptions),
                        ["errorCount"] = JsonSerializer.SerializeToElement(plan.Errors.Count),
                    };
                    throw new HccException(HccErrorCode.VALIDATION, $"The file has {plan.Errors.Count} error(s): {plan.Errors[0].Message} Fix the file and upload it again.", details);
                }

                var users = await ApplyAsync(tx, spec, b, snap, plan, c).ConfigureAwait(false);
                var result = new ImportCommitResult(b.BatchId, spec.Wire, req.DryRun, !req.DryRun, plan.Counts, PlanRows(plan), users, plan.DebitPaise, plan.CreditPaise);
                await store.MarkCommittedAsync(tx, b.BatchId, ctx.PrincipalId, Now, JsonSerializer.Serialize(result with { DryRun = false, Committed = true }, CanonicalJson.SerializerOptions), c).ConfigureAwait(false);
                var counts = plan.Counts;
                await tx.AuditAsync("IMPORT_COMMIT", $"import:{b.BatchId.ToString(CultureInfo.InvariantCulture)}",
                    Clip($"{spec.Title} · {b.FileName} · {counts.Create} created, {counts.Update} updated, {counts.Unchanged} unchanged, {counts.Remove} removed"
                        + (plan.DebitPaise is { } dr ? $" · Dr {new Paise(dr)} = Cr {new Paise(plan.CreditPaise ?? 0)}" : string.Empty), 1000), c).ConfigureAwait(false);
                await tx.PublishAsync("ImportCommitted", b.BatchId, new { batchId = b.BatchId, master = spec.Wire, created = counts.Create, updated = counts.Update, removed = counts.Remove }, c).ConfigureAwait(false);
                if (req.DryRun) throw new DryRunRollback(result);
                return result;
            }, ct).ConfigureAwait(false);
        }
        catch (DryRunRollback dry)
        {
            return new CommandResult<ImportCommitResult>(dry.Result, false);
        }
    }

    private async Task<IReadOnlyList<ImportCreatedUser>> ApplyAsync(ICommandTransaction tx, ImportMasterSpec spec, ImportBatchRecord b, ImportSnapshot snap, ImportPlan plan, CancellationToken ct)
    {
        var p = tx.Context.PropertyId;
        var actor = tx.Context.PrincipalId;
        var now = Now;
        var users = new List<ImportCreatedUser>();
        var tag = $"batch {b.BatchId.ToString(CultureInfo.InvariantCulture)}";
        async Task AuditRow(string action, string target, ImportPlanItem i) =>
            await tx.AuditAsync(action, Clip(target, 60), Clip($"{i.Action} · {i.Key} · {i.Detail} · {tag}", 1000), ct).ConfigureAwait(false);

        switch (spec.Master)
        {
            case ImportMaster.RoomTypes:
                foreach (var i in plan.Changes)
                {
                    var d = (RoomTypeData)i.Data!;
                    if (i.Action == ImportAction.Create) await inventory.InsertRoomTypeAsync(tx, p, d, ct).ConfigureAwait(false);
                    else await inventory.UpdateRoomTypeAsync(tx, p, d, ct).ConfigureAwait(false);
                    await AuditRow("IMPORT_ROOM_TYPE", "room_type:" + d.Code, i).ConfigureAwait(false);
                }

                break;
            case ImportMaster.Rooms:
                foreach (var i in plan.Changes)
                {
                    var d = (RoomData)i.Data!;
                    await inventory.InsertRoomAsync(tx, p, d, ct).ConfigureAwait(false);
                    await AuditRow("IMPORT_ROOM", "room:" + d.RoomNo, i).ConfigureAwait(false);
                }

                break;
            case ImportMaster.Tariffs:
            {
                var rows = plan.Changes.Select(i => (TariffData)i.Data!).Select(d => new Hcc.Domain.Yield.TariffRow(d.TypeId, d.From, d.RatePaise)).ToList();
                if (rows.Count > 0) await rates.WriteTariffsAsync(tx, p, rows, ct).ConfigureAwait(false);
                foreach (var i in plan.Changes) await AuditRow("IMPORT_TARIFF", "tariff:" + i.Key, i).ConfigureAwait(false);
                break;
            }

            case ImportMaster.Seasons:
                if (plan.Changes.Any()) await yield.EnsureSeededAsync(tx, p, true, ct).ConfigureAwait(false);
                foreach (var i in plan.Changes)
                {
                    var d = (SeasonData)i.Data!;
                    await yield.SaveSeasonAsync(tx, p, null, null, d.Name, d.From, d.To, d.AdjBps, d.TypeIds, d.Priority, ct).ConfigureAwait(false);
                    await yield.AddHistoryAsync(tx, p, now, actor, "Season · " + d.Name, "—", i.Detail, "Imported (" + tag + ")", ct).ConfigureAwait(false);
                    await AuditRow("IMPORT_SEASON", "season:" + d.Name, i).ConfigureAwait(false);
                }

                break;
            case ImportMaster.Staff:
                foreach (var i in plan.Changes)
                {
                    var d = (StaffData)i.Data!;
                    var (id, version) = await access.CreateUserAsync(tx, p, d, actor, now, ct).ConfigureAwait(false);
                    users.Add(new ImportCreatedUser(id, d.Login, d.DisplayName, version, d.CredentialKind));
                    await tx.AuditAsync("USER_CREATE", $"user:{id.ToString(CultureInfo.InvariantCulture)}",
                        Clip($"Created '{d.Login}' with roles {string.Join(",", d.Roles)} by import ({tag}); no credential until the Owner sets the first {(d.CredentialKind == "PASSWORD" ? "password" : "PIN")}.", 1000), ct).ConfigureAwait(false);
                }

                break;
            case ImportMaster.Ledgers:
                foreach (var i in plan.Changes)
                {
                    var d = (LedgerData)i.Data!;
                    await store.UpsertLedgerAsync(tx, p, d, b.BatchId, actor, now, ct).ConfigureAwait(false);
                    await AuditRow("IMPORT_LEDGER", "ledger:" + d.Code, i).ConfigureAwait(false);
                }

                break;
            case ImportMaster.OpeningBalances:
                if (plan.Changes.Any())
                {
                    var rows = plan.Items.Where(i => i.Data is OpeningData).Select(i => (OpeningData)i.Data!).ToList();
                    await store.ReplaceOpeningAsync(tx, p, b.AsOfDate!.Value, rows, b.BatchId, actor, now, ct).ConfigureAwait(false);
                    await tx.AuditAsync("IMPORT_OPENING_TB", $"opening:{ImportValues.Wire(b.AsOfDate.Value)}",
                        Clip($"Opening trial balance as at {ImportValues.Wire(b.AsOfDate.Value)} · {rows.Count} ledgers · Dr {new Paise(plan.DebitPaise ?? 0)} = Cr {new Paise(plan.CreditPaise ?? 0)} · {tag}", 1000), ct).ConfigureAwait(false);
                }

                break;
            case ImportMaster.TillCash:
                foreach (var i in plan.Changes)
                {
                    var d = (TillData)i.Data!;
                    var till = d.TillId is { } tid
                        ? await billing.LockTillAsync(tx, tid, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Till not found.")
                        : await billing.InsertTillAsync(tx, p, d.Code, d.Name, actor, now, ct).ConfigureAwait(false);
                    if (await billing.ActiveShiftAsync(tx, till.TillId, ct).ConfigureAwait(false) is not null || await billing.LastClosedShiftAsync(tx, till.TillId, ct).ConfigureAwait(false) is not null)
                        throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Till {d.Code} already has cash history; upload the file again.");
                    var cash = new Paise(d.OpeningPaise);
                    var shift = await billing.InsertShiftAsync(tx, till.TillId, snap.BusinessDate, cash, null, actor, now, ct).ConfigureAwait(false);
                    await billing.UpdateShiftAsync(tx, shift.ShiftId, shift.Version, new ShiftUpdate("Closed", cash, cash, actor, now, OpeningShiftReason, null, actor, now), ct).ConfigureAwait(false);
                    await billing.InsertShiftCountAsync(tx, shift.ShiftId, cash, cash, null, OpeningShiftReason, null, actor, now, ct).ConfigureAwait(false);
                    if (d.TillId is null) await tx.AuditAsync("TILL_CREATED", $"till:{till.TillId.ToString(CultureInfo.InvariantCulture)}", $"{d.Code} · {d.Name} · by import ({tag})", ct).ConfigureAwait(false);
                    await AuditRow("IMPORT_TILL_CASH", $"shift:{shift.ShiftId.ToString(CultureInfo.InvariantCulture)}", i).ConfigureAwait(false);
                }

                break;
            case ImportMaster.Guests:
                foreach (var i in plan.Changes)
                {
                    var d = (GuestData)i.Data!;
                    var id = await stays.InsertGuestAsync(tx, p, d.Draft, false, "manual", now, ct).ConfigureAwait(false);
                    await AuditRow("IMPORT_GUEST", $"guest:{id.ToString(CultureInfo.InvariantCulture)}", i).ConfigureAwait(false);
                }

                break;
            default:
                throw new InvalidOperationException("Unknown master.");
        }

        return users;
    }

    private static List<ImportPlanRow> PlanRows(ImportPlan plan) =>
        plan.Items.Take(ImportPlanner.MaxListed).Select(i => new ImportPlanRow(i.Row, i.Action.ToString(), i.Key, i.Detail)).ToList();

    private static ImportBatchReport Report(ImportBatchRecord b, ImportMasterSpec spec, ImportPlan plan)
    {
        var committed = b.Status == "Committed";
        string? blocked = committed ? "This batch was committed already."
            : plan.Errors.Count > 0 ? $"Fix the {plan.Errors.Count} error(s) in the file and upload it again."
            : plan.Changes.Any() ? null : "Nothing to change: every row is already in HCC.";
        var allowed = !committed && plan.Valid;
        return new ImportBatchReport(b.BatchId, spec.Wire, b.FileName, b.Status, b.AsOfDate, plan.Rows, plan.Errors.Count, plan.Errors.Take(ImportPlanner.MaxListed).ToList(),
            plan.Warnings.Take(ImportPlanner.MaxListed).ToList(), plan.Counts, PlanRows(plan), plan.DebitPaise, plan.CreditPaise, allowed, blocked, Iso(b.UploadedUtc), b.UploadedBy,
            b.CommittedUtc is { } cu ? Iso(cu) : null, b.CommittedBy);
    }
}
