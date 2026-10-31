using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Application.Documents;
using Hcc.Application.GuestIds;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Legacy;
using Hcc.Domain.Accounting;
using Hcc.Domain.Customers;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;
using Hcc.Domain.Legacy;

namespace Hcc.Application.Legacy;

/// <summary>
/// The legacy "Lodge" importer (LG-HISTORY-CONTRACT §5; DEC-161…164). One command <c>legacy.import</c>, Owner only (PERM-LGC-001), through the
/// command pipeline with the idempotency key = folder + parts + dry run + SHA-256 of totals.json. Reads only the run folder written by
/// <c>tools/legacy/extract.py</c> on this server's disk (never an MDB). Parts:
/// <list type="bullet">
/// <item><c>guests</c>: matches <c>lgc_guest_map.csv</c> to the <c>gst.guest</c> rows the masters importer created (phone key + folded name), writes
/// <c>legacy_guest_id</c> / <c>dob</c>, seals ID numbers from <c>secret\guest_ids.jsonl</c> with the guest-ID store and protector (DEC-068/070), copies
/// photo / ID-proof images into the documents capture store (sealed, kind <c>legacy</c>), adds <c>lgc_tags.csv</c> tags. Never creates a guest.
/// After a committed run the jsonl file is overwritten with zeros and deleted (audit <c>legacy.secret_shredded</c>).</item>
/// <item><c>history</c>: <c>lgc.guest_map</c>, then per FY under a savepoint: delete + insert <c>lgc.stay / room_segment / bill / receipt</c>, reconcile
/// the inserted rows with totals.json (§6), roll that FY back on a mismatch; refused once a business day is closed.</item>
/// <item><c>cutover</c>: receivables, customer openings and advances as party lines of the ACC-1 opening entry, saved as a <b>draft</b> (the Owner
/// approves it in the opening-balances screen); the in-house list is only counted.</item>
/// </list>
/// A dry run does everything inside the transaction and rolls it back; its run row is recorded afterwards with status DryRun.
/// </summary>
public sealed class LegacyImportService(
    ICommandRunner runner,
    IAccessPolicy policy,
    ILegacyStore store,
    IGuestIdStore guestIds,
    GuestIdProtector protector,
    IDocumentStore documents,
    IDocumentImageStore images,
    IOpeningEntryStore openings,
    TimeProvider clock)
{
    public const string OpImport = "legacy.import";
    public const string SecretFile = "guest_ids.jsonl";
    public const int MaxListed = 200;
    private const int MaxImageBytes = 8_388_608;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private sealed class DryRunRollback(LegacyImportResult result) : Exception("Dry run: rolled back.")
    {
        public LegacyImportResult Result { get; } = result;
    }

    private sealed record SecretEntry(long LegacyGuestId, string? IdType, string? IdNumber, DateOnly? Dob, string? PhotoFile, string? IdProofFile);

    private sealed record Prepared(string Folder, IReadOnlyList<string> Parts, LegacyTotals Totals, string ExtractorVersion, byte[] TotalsSha,
        IReadOnlyList<LgcGuestMapRow> GuestMap, IReadOnlyList<(string Key, string Tag)> Tags, IReadOnlyDictionary<long, SecretEntry> Secrets, string? SecretPath,
        IReadOnlyList<LgcFyData> Years, LegacyCsv? Receivables, LegacyCsv? CustomerOpening, LegacyCsv? Advances, int InhouseRows, List<string> Warnings);

    /// <summary>Runs the importer. Validation of the folder and every CSV happens before the transaction (nothing is written on a refusal).</summary>
    public async Task<CommandResult<LegacyImportResult>> ImportAsync(CommandContext ctx, LegacyImportRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, LegacyPermissions.Import, ct: ct).ConfigureAwait(false);
        var prep = Prepare(req);
        var key = LegacyRules.IdempotencyKey(prep.Folder, prep.Parts, req.DryRun, prep.TotalsSha);
        var run = ctx with { Operation = OpImport, IdempotencyKey = key };
        var started = Now;
        CommandResult<LegacyImportResult> done;
        try
        {
            done = await runner.RunAsync(run, req with { Parts = prep.Parts }, async (tx, c) =>
            {
                await store.LockAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
                if (prep.Parts.Contains(LegacyRules.History) && await store.AnyDayClosedAsync(tx, ctx.PropertyId, c).ConfigureAwait(false))
                    throw new HccException(HccErrorCode.DAY_CLOSED, "Legacy history can be loaded only until the first business day is closed. Run the cut-over part alone.");
                var warnings = new List<string>(prep.Warnings);
                var candidates = await store.GuestsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
                var match = Match(prep.GuestMap, candidates);
                LegacyGuestsResult? guests = null;
                LegacyHistoryResult? history = null;
                LegacyCutoverResult? cutover = null;
                if (prep.Parts.Contains(LegacyRules.Guests)) guests = await GuestsAsync(tx, prep, match, warnings, c).ConfigureAwait(false);
                if (prep.Parts.Contains(LegacyRules.History)) history = await HistoryAsync(tx, prep, match, c).ConfigureAwait(false);
                if (prep.Parts.Contains(LegacyRules.Cutover)) cutover = await CutoverAsync(tx, prep, warnings, c).ConfigureAwait(false);
                var result = new LegacyImportResult(0, req.DryRun, prep.Parts, prep.ExtractorVersion, guests, history, cutover, warnings.Take(MaxListed).ToList(), !req.DryRun);
                if (req.DryRun) throw new DryRunRollback(result);
                var json = JsonSerializer.Serialize(result, CanonicalJson.SerializerOptions);
                var runId = await store.InsertRunAsync(tx, ctx.PropertyId, prep.Folder, prep.ExtractorVersion, false, string.Join(',', prep.Parts), started, Now, "Done",
                    ctx.PrincipalId, json, c).ConfigureAwait(false);
                result = result with { RunId = runId };
                await store.UpdateRunResultAsync(tx, runId, JsonSerializer.Serialize(result, CanonicalJson.SerializerOptions), c).ConfigureAwait(false);
                await tx.AuditAsync("legacy.import", "lgc-run:" + runId.ToString(CultureInfo.InvariantCulture), Clip(Summary(result), 1000), c).ConfigureAwait(false);
                return result;
            }, ct).ConfigureAwait(false);
        }
        catch (DryRunRollback dry)
        {
            var recorded = await runner.RunAsync(ctx with { Operation = OpImport + ".dry", IdempotencyKey = "lgc-dry-" + Guid.NewGuid().ToString("N") }, new { key }, async (tx, c) =>
            {
                var json = JsonSerializer.Serialize(dry.Result, CanonicalJson.SerializerOptions);
                var runId = await store.InsertRunAsync(tx, ctx.PropertyId, prep.Folder, prep.ExtractorVersion, true, string.Join(',', prep.Parts), started, Now, "DryRun",
                    ctx.PrincipalId, json, c).ConfigureAwait(false);
                var r = dry.Result with { RunId = runId };
                await store.UpdateRunResultAsync(tx, runId, JsonSerializer.Serialize(r, CanonicalJson.SerializerOptions), c).ConfigureAwait(false);
                await tx.AuditAsync("legacy.import", "lgc-run:" + runId.ToString(CultureInfo.InvariantCulture), Clip("Dry run (rolled back) · " + Summary(r), 1000), c).ConfigureAwait(false);
                return r;
            }, ct).ConfigureAwait(false);
            return new CommandResult<LegacyImportResult>(recorded.Value, false);
        }
        catch (HccException ex) when (!req.DryRun && ex.Code != HccErrorCode.IDEMPOTENCY_MISMATCH)
        {
            await RecordFailedAsync(ctx, prep, started, ex.Message, ct).ConfigureAwait(false);
            throw;
        }

        if (!done.Replayed && prep.Parts.Contains(LegacyRules.Guests) && prep.SecretPath is { } secret && File.Exists(secret))
        {
            Shred(secret);
            await runner.RunAsync(ctx with { Operation = OpImport + ".shred", IdempotencyKey = key + "-shred" }, new { key }, async (tx, c) =>
            {
                await tx.AuditAsync("legacy.secret_shredded", "lgc-run:" + done.Value.RunId.ToString(CultureInfo.InvariantCulture),
                    $"secret\\{SecretFile} overwritten with zeros and deleted after the committed guests part ({prep.Secrets.Count} entries)", c).ConfigureAwait(false);
                return new { shredded = true };
            }, ct).ConfigureAwait(false);
        }

        return done;
    }

    public async Task<LegacyRunList> RunsAsync(int propertyId, int principalId, int limit = 50, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, LegacyPermissions.Import, ct: ct).ConfigureAwait(false);
        var rows = await store.ListRunsAsync(propertyId, Math.Clamp(limit, 1, 200), ct).ConfigureAwait(false);
        return new LegacyRunList(rows.Select(r => new LegacyRunView(r.RunId, r.SourceFolder, r.ExtractorVersion, r.DryRun, r.Parts, Iso(r.StartedUtc),
            r.FinishedUtc is { } f ? Iso(f) : null, r.Status, r.RunBy,
            r.ResultJson is { } j ? JsonSerializer.Deserialize<LegacyImportResult>(j, CanonicalJson.SerializerOptions) : null)).ToList());
    }

    /// <summary>Overwrites a file with zeros (same length), flushes it to disk, then deletes it.</summary>
    public static void Shred(string path)
    {
        var length = new FileInfo(path).Length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            var zeros = new byte[64 * 1024];
            for (long left = length; left > 0; left -= zeros.Length) fs.Write(zeros, 0, (int)Math.Min(zeros.Length, left));
            fs.Flush(true);
        }

        File.Delete(path);
    }

    private async Task RecordFailedAsync(CommandContext ctx, Prepared prep, DateTime started, string message, CancellationToken ct)
    {
        try
        {
            await runner.RunAsync(ctx with { Operation = OpImport + ".failed", IdempotencyKey = "lgc-fail-" + Guid.NewGuid().ToString("N") }, new { started }, async (tx, c) =>
            {
                var runId = await store.InsertRunAsync(tx, ctx.PropertyId, prep.Folder, prep.ExtractorVersion, false, string.Join(',', prep.Parts), started, Now, "Failed",
                    ctx.PrincipalId, null, c).ConfigureAwait(false);
                await tx.AuditAsync("legacy.import", "lgc-run:" + runId.ToString(CultureInfo.InvariantCulture), Clip("Failed, nothing written · " + message, 1000), c).ConfigureAwait(false);
                return new { runId };
            }, ct).ConfigureAwait(false);
        }
        catch (HccException)
        {
            // the failure itself is what the caller sees; the run row is best effort
        }
    }

    private static string Summary(LegacyImportResult r)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"parts {string.Join(',', r.Parts)} · extractor {r.ExtractorVersion}");
        if (r.Guests is { } g) sb.Append(CultureInfo.InvariantCulture, $" · guests {g.MatchedExisting} matched, {g.SecretsSealed} IDs sealed, {g.Errors.Count} unmatched");
        if (r.History is { } h)
            foreach (var y in h.PerFy) sb.Append(CultureInfo.InvariantCulture, $" · {y.Fy} {(y.Reconciliation.Match ? "match" : "MISMATCH rolled back")} ({y.Stays} stays, {y.Bills} bills)");
        if (r.Cutover is { } k) sb.Append(CultureInfo.InvariantCulture, $" · cut-over {k.ReceivablesRows} receivables, {k.CustomerOpeningRows} openings, {k.AdvancesRows} advances");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- reading the run folder (before the transaction)

    private static Prepared Prepare(LegacyImportRequest req)
    {
        var parts = LegacyRules.NormaliseParts(req.Parts) ?? throw HccException.Validation("Choose the parts to import: guests, history, cutover.");
        if (LegacyRules.FolderProblem(req.Folder) is { } problem) throw HccException.Validation(problem);
        var folder = Path.GetFullPath(req.Folder.Trim());
        if (!Directory.Exists(folder)) throw HccException.Validation("The run folder does not exist on this server.");
        var totalsPath = Path.Combine(folder, "totals.json");
        if (!File.Exists(totalsPath)) throw HccException.Validation("The run folder has no totals.json: run the extractor again.");

        foreach (var csv in Directory.EnumerateFiles(folder, "*.csv", SearchOption.AllDirectories))
        {
            var header = LegacyCsv.ReadHeader(csv);
            if (header.FirstOrDefault(LegacyRules.IsIdColumn) is { } col)
                throw HccException.Validation($"{Path.GetRelativePath(folder, csv)} has a column '{col}' that looks like an ID number. ID numbers come only from secret\\{SecretFile}; nothing was imported.");
        }

        var totalsBytes = File.ReadAllBytes(totalsPath);
        var sha = SHA256.HashData(totalsBytes);
        var expectedText = string.IsNullOrWhiteSpace(req.ExpectedTotals) ? Encoding.UTF8.GetString(totalsBytes) : req.ExpectedTotals;
        var totals = LegacyRules.ParseTotals(expectedText, out var error) ?? throw HccException.Validation("Expected totals: " + error);
        var fileTotals = LegacyRules.ParseTotals(Encoding.UTF8.GetString(totalsBytes), out _);
        var version = fileTotals?.ExtractorVersion is { Length: > 0 } v ? v : totals.ExtractorVersion.Length > 0 ? totals.ExtractorVersion : "unknown";
        var warnings = new List<string>();

        LegacyCsv? Optional(string name) => File.Exists(Path.Combine(folder, name)) ? LegacyCsv.Read(Path.Combine(folder, name)) : null;
        LegacyCsv Required(string name) => Optional(name) ?? throw HccException.Validation($"The run folder has no {name}.");

        var needMap = parts.Contains(LegacyRules.Guests) || parts.Contains(LegacyRules.History);
        var map = needMap ? ReadGuestMap(Required("lgc_guest_map.csv")) : [];
        var tags = new List<(string, string)>();
        var secrets = new Dictionary<long, SecretEntry>();
        string? secretPath = null;
        if (parts.Contains(LegacyRules.Guests))
        {
            if (Optional("lgc_tags.csv") is { } t)
                for (var i = 0; i < t.Rows.Count; i++)
                {
                    var key = t.Req(t.Rows[i], "customer_key", i + 2);
                    var tag = t.Req(t.Rows[i], "tag", i + 2);
                    if (tag.Length > 30 || key.Length > 80) throw HccException.Validation($"lgc_tags.csv row {i + 2}: the tag or key is too long.");
                    tags.Add((key, tag));
                }

            secretPath = Path.Combine(folder, "secret", SecretFile);
            if (File.Exists(secretPath)) secrets = ReadSecrets(secretPath);
            else warnings.Add($"secret\\{SecretFile} is absent (already shredded by an earlier committed run, or no guest had an ID number): no ID numbers sealed.");
        }

        var years = new List<LgcFyData>();
        if (parts.Contains(LegacyRules.History)) years = ReadYears(Required("lgc_stay.csv"), Required("lgc_room_segment.csv"), Required("lgc_bill.csv"), Required("lgc_receipt.csv"));

        LegacyCsv? receivables = null, opening = null, advances = null;
        var inhouse = 0;
        if (parts.Contains(LegacyRules.Cutover))
        {
            receivables = Required("cutover_receivables.csv");
            opening = Required("cutover_customer_opening.csv");
            advances = Required("cutover_advances.csv");
            inhouse = Optional("cutover_inhouse.csv")?.Rows.Count ?? 0;
        }

        return new Prepared(folder, parts, totals, version, sha, map, tags, secrets, secretPath, years, receivables, opening, advances, inhouse, warnings);
    }

    private static List<LgcGuestMapRow> ReadGuestMap(LegacyCsv f)
    {
        var rows = new List<LgcGuestMapRow>(f.Rows.Count);
        var seen = new HashSet<long>();
        for (var i = 0; i < f.Rows.Count; i++)
        {
            var r = f.Rows[i];
            var line = i + 2;
            var id = f.LongReq(r, "legacy_guest_id", line);
            if (!seen.Add(id)) throw HccException.Validation($"lgc_guest_map.csv row {line}: legacy guest {id} appears twice.");
            var name = f.Req(r, "name_snapshot", line);
            rows.Add(new LgcGuestMapRow(id, null, f.Req(r, "customer_key", line), f.Get(r, "phone_key"), f.Bool(r, "phone_valid", line), Clip(name, 200),
                Clip(f.Get(r, "name_fold") ?? CustomerRules.NormName(name), 200), f.Bool(r, "deleted", line), f.Req(r, "source_fy", line),
                f.Get(r, "guest_name"), f.Get(r, "guest_phone"), f.Has("is_primary") && f.Bool(r, "is_primary", line), f.Date(r, "dob", line)));
        }

        return rows;
    }

    private static Dictionary<long, SecretEntry> ReadSecrets(string path)
    {
        var map = new Dictionary<long, SecretEntry>();
        var n = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            n++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var o = doc.RootElement;
                string? S(string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = o.GetProperty("legacyGuestId").GetInt64();
                DateOnly? dob = S("dob") is { } d && DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x) ? x : null;
                map[id] = new SecretEntry(id, S("idType"), S("idNumber"), dob, S("photoFile"), S("idProofFile"));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // the line itself is never echoed: it holds an ID number
                throw HccException.Validation($"secret\\{SecretFile} line {n} is not a valid entry.");
            }
        }

        return map;
    }

    private static (string Fy, long Id) Ref(string? text, string file, int line)
    {
        var parts = (text ?? string.Empty).Split('|');
        if (parts.Length != 2 || !LegacyRules.IsFy(parts[0]) || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            throw HccException.Validation($"{file} row {line}: the reference '{text}' is not 'YYYY-YY|number'.");
        return (parts[0], id);
    }

    private static string Fy(LegacyCsv f, string[] r, int line)
    {
        var fy = f.Req(r, "legacy_fy", line);
        return LegacyRules.IsFy(fy) ? fy : throw HccException.Validation($"{f.Name} row {line}: legacy_fy '{fy}' is not YYYY-YY.");
    }

    private static List<LgcFyData> ReadYears(LegacyCsv st, LegacyCsv sg, LegacyCsv bl, LegacyCsv rc)
    {
        var stays = new List<LgcStayRow>();
        for (var i = 0; i < st.Rows.Count; i++)
        {
            var r = st.Rows[i];
            var l = i + 2;
            stays.Add(new LgcStayRow(Fy(st, r, l), st.LongReq(r, "legacy_booking_id", l), st.LongReq(r, "legacy_guest_id", l), st.Req(r, "customer_key", l),
                Clip(st.Req(r, "guest_name_snapshot", l), 200), st.Date(r, "arrival_date", l) ?? throw HccException.Validation($"lgc_stay.csv row {l}: arrival_date is empty."),
                st.Utc(r, "arrival_utc", l) ?? throw HccException.Validation($"lgc_stay.csv row {l}: arrival_utc is empty."), st.Date(r, "departure_date", l), st.Utc(r, "departure_utc", l),
                st.Date(r, "expected_departure_date", l), st.Int(r, "nights", l) ?? 1, st.Int(r, "adults", l) ?? 1, st.Int(r, "children", l) ?? 0, st.Req(r, "room_no", l),
                st.Int(r, "room_count", l) ?? 1, st.Get(r, "meal_plan"), st.Int(r, "grc_no", l), st.Get(r, "purpose"), st.Get(r, "source_tag"), st.Req(r, "status", l),
                st.Get(r, "paid_flag"), st.Int(r, "legacy_bill_no", l), st.Get(r, "notes")));
        }

        var segs = new List<(string Fy, LgcSegmentRow Row)>();
        for (var i = 0; i < sg.Rows.Count; i++)
        {
            var r = sg.Rows[i];
            var l = i + 2;
            var (fy, id) = Ref(sg.Get(r, "stay_ref"), sg.Name, l);
            segs.Add((fy, new LgcSegmentRow(id, sg.Int(r, "seq", l) ?? throw HccException.Validation($"lgc_room_segment.csv row {l}: seq is empty."), sg.Req(r, "room_no", l),
                sg.Get(r, "room_type_code"), sg.Date(r, "from_date", l), sg.Date(r, "to_date", l), sg.Int(r, "adults", l), sg.Int(r, "children", l), sg.Long(r, "rate_paise", l),
                sg.Long(r, "extra_paise", l), sg.Req(r, "kind", l))));
        }

        var bills = new List<LgcBillRow>();
        for (var i = 0; i < bl.Rows.Count; i++)
        {
            var r = bl.Rows[i];
            var l = i + 2;
            var fy = Fy(bl, r, l);
            long? stay = bl.Get(r, "stay_ref") is { } sr ? Ref(sr, bl.Name, l).Id : null;
            long M(string c) => bl.Long(r, c, l) ?? 0;
            bills.Add(new LgcBillRow(fy, bl.Int(r, "legacy_bill_no", l) ?? throw HccException.Validation($"lgc_bill.csv row {l}: legacy_bill_no is empty."),
                bl.LongReq(r, "legacy_transaction_id", l), stay, bl.Long(r, "legacy_guest_id", l), bl.Get(r, "customer_key"),
                bl.Date(r, "bill_date", l) ?? throw HccException.Validation($"lgc_bill.csv row {l}: bill_date is empty."),
                bl.Utc(r, "bill_utc", l) ?? throw HccException.Validation($"lgc_bill.csv row {l}: bill_utc is empty."), bl.Req(r, "room_no", l), bl.Int(r, "nights", l),
                M("room_charge_paise"), M("extra_paise"), M("fb_paise"), M("laundry_paise"), M("other_paise"), M("discount_paise"), bl.Int(r, "room_gst_bps", l) ?? 0,
                M("room_gst_paise"), M("fb_gst_paise"), M("laundry_gst_paise"), M("other_gst_paise"), M("advance_paise"), M("net_paise"), M("balance_paise"), M("refund_paise"),
                bl.Get(r, "data_flag") ?? "?", bl.Get(r, "paid_flag") ?? "?", bl.Get(r, "gstin_snapshot"), bl.Bool(r, "b2b", l), bl.Bool(r, "out_of_fy", l), bl.Get(r, "notes")));
        }

        var receipts = new List<LgcReceiptRow>();
        for (var i = 0; i < rc.Rows.Count; i++)
        {
            var r = rc.Rows[i];
            var l = i + 2;
            long? stay = rc.Get(r, "stay_ref") is { } sr ? Ref(sr, rc.Name, l).Id : null;
            int? bill = rc.Get(r, "bill_ref") is { } br ? checked((int)Ref(br, rc.Name, l).Id) : null;
            receipts.Add(new LgcReceiptRow(Fy(rc, r, l), rc.Req(r, "legacy_source", l), rc.LongReq(r, "legacy_row_id", l), stay, bill, rc.Long(r, "legacy_guest_id", l),
                rc.Get(r, "customer_key"), rc.Date(r, "received_date", l) ?? throw HccException.Validation($"lgc_receipt.csv row {l}: received_date is empty."),
                rc.Utc(r, "received_utc", l) ?? throw HccException.Validation($"lgc_receipt.csv row {l}: received_utc is empty."), rc.Req(r, "kind", l), rc.Req(r, "method", l),
                rc.LongReq(r, "amount_paise", l), rc.Get(r, "reference"), rc.Get(r, "bank_name"), rc.Get(r, "notes")));
        }

        var fys = stays.Select(s => s.Fy).Concat(bills.Select(b => b.Fy)).Concat(receipts.Select(r => r.Fy)).Concat(segs.Select(s => s.Fy)).Distinct().Order(StringComparer.Ordinal);
        return fys.Select(fy => new LgcFyData(fy, stays.Where(s => s.Fy == fy).ToList(), segs.Where(s => s.Fy == fy).Select(s => s.Row).ToList(),
            bills.Where(b => b.Fy == fy).ToList(), receipts.Where(r => r.Fy == fy).ToList())).ToList();
    }

    // ---------------------------------------------------------------- guest matching (§1, §5 rule 1)

    /// <summary>Which legacy guests map to which HCC guest, and the groups (winner = the highest legacy id of a phone key).</summary>
    public sealed record GuestMatch(IReadOnlyDictionary<long, long> GuestOf, IReadOnlyList<(long GuestId, IReadOnlyList<long> LegacyIds)> Groups, int Invalid,
        IReadOnlyList<long> UnmatchedWinners);

    /// <summary>
    /// LG-R1 / LG-R3: each valid-phone legacy guest carries the name and phone of the guests.csv row it produced (<c>guest_name</c>, <c>guest_phone</c>);
    /// it maps to the HCC guest with the same phone key AND the same folded name — never on the phone alone, because one phone may belong to
    /// several guests with different names. Rows mapping to one guest form a group; its first id is the primary row (<c>is_primary</c>), else the
    /// highest legacy id. Invalid phones never map (DEC-162).
    /// </summary>
    public static GuestMatch Match(IReadOnlyList<LgcGuestMapRow> map, IReadOnlyList<LegacyGuestCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(candidates);
        var index = new Dictionary<(string, string), long>();
        foreach (var c in candidates.OrderBy(c => c.GuestId))
        {
            var pk = CustomerRules.PhoneKey(c.Phone);
            if (pk.Length > 0) index.TryAdd((pk, CustomerRules.NormName(c.FullName)), c.GuestId);
        }

        var guestOf = new Dictionary<long, long>();
        var unmatched = new List<long>();
        var valid = map.Where(m => m.PhoneValid && (!string.IsNullOrEmpty(m.PhoneKey) || !string.IsNullOrEmpty(m.GuestPhone))).ToList();
        foreach (var m in valid)
        {
            var pk = CustomerRules.PhoneKey(m.GuestPhone ?? m.PhoneKey);
            var fold = CustomerRules.NormName(m.GuestName ?? m.NameSnapshot);
            if (pk.Length > 0 && index.TryGetValue((pk, fold), out var g)) guestOf[m.LegacyGuestId] = g;
            else unmatched.Add(m.LegacyGuestId);
        }

        var groups = valid.Where(m => guestOf.ContainsKey(m.LegacyGuestId)).GroupBy(m => guestOf[m.LegacyGuestId]).OrderBy(g => g.Key)
            .Select(g => (g.Key, (IReadOnlyList<long>)g.OrderByDescending(m => m.IsPrimary).ThenByDescending(m => m.LegacyGuestId).Select(m => m.LegacyGuestId).ToList()))
            .ToList();
        return new GuestMatch(guestOf, groups, map.Count - valid.Count, unmatched);
    }

    private async Task<LegacyGuestsResult> GuestsAsync(ICommandTransaction tx, Prepared prep, GuestMatch match, List<string> warnings, CancellationToken ct)
    {
        var p = tx.Context.PropertyId;
        var actor = tx.Context.PrincipalId;
        var now = Now;
        var errors = match.UnmatchedWinners.Select(id => $"legacy guest {id.ToString(CultureInfo.InvariantCulture)}: no HCC guest with that phone and name (commit guests.csv through the masters importer first)").ToList();
        var dobOf = prep.GuestMap.Where(m => m.Dob is not null).ToDictionary(m => m.LegacyGuestId, m => m.Dob);
        int sealedCount = 0, dobs = 0, photos = 0, proofs = 0, tags = 0, kept = 0;
        var date = await store.BusinessDateAsync(tx, p, ct).ConfigureAwait(false);
        foreach (var (guestId, ids) in match.Groups)
        {
            var secret = ids.Select(id => prep.Secrets.GetValueOrDefault(id)).FirstOrDefault(s => s is not null);
            var dob = secret?.Dob ?? ids.Select(id => dobOf.GetValueOrDefault(id)).FirstOrDefault(d => d is not null);
            if (!await store.SetGuestLegacyAsync(tx, p, guestId, ids[0], dob, ct).ConfigureAwait(false))
            {
                errors.Add($"legacy guest {ids[0].ToString(CultureInfo.InvariantCulture)}: another HCC guest already carries this legacy id");
                continue;
            }

            if (dob is not null) dobs++;
            if (secret is null) continue;
            string? number = secret.IdNumber is null ? null : GuestIdRules.Normalise(secret.IdNumber);
            if (number is not null && number.Length is < 4 or > 20)
            {
                errors.Add($"legacy guest {secret.LegacyGuestId.ToString(CultureInfo.InvariantCulture)}: the ID number in the secret file is unusable (not sealed)");
                number = null;
            }

            if (number is null) { }
            else if (await guestIds.HasNumberAsync(tx, guestId, ct).ConfigureAwait(false)) kept++;
            else
            {
                await guestIds.UpsertAsync(tx, guestId, protector.Seal(p, guestId, number), now, ct).ConfigureAwait(false);
                await documents.SetGuestIdProfileAsync(tx, guestId, LegacyRules.IdTypeLabel(secret.IdType), GuestIdRules.Last4(number), ct).ConfigureAwait(false);
                sealedCount++;
            }

            if (await CopyImageAsync(tx, prep, "photo", secret.PhotoFile, guestId, secret, number, date, now, actor, errors, ct).ConfigureAwait(false)) photos++;
            if (await CopyImageAsync(tx, prep, "idproof", secret.IdProofFile, guestId, secret, number, date, now, actor, errors, ct).ConfigureAwait(false)) proofs++;
        }

        if (kept > 0) warnings.Add($"{kept} guest(s) already had an ID number in HCC: kept, the legacy number was not sealed over it.");
        foreach (var (key, tag) in prep.Tags)
            if (await store.AddTagAsync(tx, p, key, tag, actor, now, ct).ConfigureAwait(false)) tags++;

        await tx.AuditAsync("legacy.guests", "lgc-guests", $"{match.Groups.Count} guests matched · {sealedCount} ID numbers sealed · {photos} photos · {proofs} ID proofs · {tags} tags · {errors.Count} unmatched", ct)
            .ConfigureAwait(false);
        return new LegacyGuestsResult(prep.GuestMap.Count, 0, match.Groups.Count, match.Invalid, match.Groups.Sum(g => g.LegacyIds.Count - 1), sealedCount, dobs, photos, proofs, tags,
            errors.Take(MaxListed).ToList());
    }

    private async Task<bool> CopyImageAsync(ICommandTransaction tx, Prepared prep, string sub, string? file, long guestId, SecretEntry secret, string? number, DateOnly date,
        DateTime now, int actor, List<string> errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file)) return false;
        var who = $"legacy guest {secret.LegacyGuestId.ToString(CultureInfo.InvariantCulture)}";
        if (file != Path.GetFileName(file) || file.Contains("..", StringComparison.Ordinal))
        {
            errors.Add($"{who}: {sub} file name refused");
            return false;
        }

        var path = Path.Combine(prep.Folder, "secret", "images", sub, file);
        if (!File.Exists(path))
        {
            errors.Add($"{who}: {sub} image missing in secret\\images\\{sub}");
            return false;
        }

        var type = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null,
        };
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        if (type is null || bytes.Length is 0 or > MaxImageBytes)
        {
            errors.Add($"{who}: {sub} image is not a JPEG / PNG / WebP of at most 8 MB");
            return false;
        }

        var reference = Guid.NewGuid();
        var ocr = number is null ? null
            : protector.SealSecret(tx.Context.PropertyId, DocumentService.OcrPurpose(reference), JsonSerializer.Serialize(new { Number = number, Name = string.Empty, Dob = secret.Dob }));
        var id = await store.InsertCaptureAsync(tx, tx.Context.PropertyId, guestId, reference, type, bytes.Length, LegacyRules.DocKindOf(secret.IdType),
            number is null ? null : GuestIdRules.Last4(number), ocr, date, now, actor, ct).ConfigureAwait(false);
        await images.PutAsync(tx, id, protector.SealSecret(tx.Context.PropertyId, DocumentService.ImagePurpose(reference), Convert.ToBase64String(bytes)), ct).ConfigureAwait(false);
        return true;
    }

    // ---------------------------------------------------------------- history (§5 rule 2, §6)

    private async Task<LegacyHistoryResult> HistoryAsync(ICommandTransaction tx, Prepared prep, GuestMatch match, CancellationToken ct)
    {
        var p = tx.Context.PropertyId;
        var errors = new List<string>();
        var map = prep.GuestMap.Select(m => m with { GuestId = match.GuestOf.TryGetValue(m.LegacyGuestId, out var g) ? g : null }).ToList();
        await store.ReplaceGuestMapAsync(tx, p, map, ct).ConfigureAwait(false);
        var perFy = new List<LegacyFyResult>();
        foreach (var year in prep.Years)
        {
            var save = "lgc_" + year.Fy.Replace("-", "_", StringComparison.Ordinal);
            await store.SavepointAsync(tx, save, ct).ConfigureAwait(false);
            var written = await store.ReplaceFyAsync(tx, p, year, ct).ConfigureAwait(false);
            var figures = await store.FiguresAsync(tx, p, year.Fy, ct).ConfigureAwait(false);
            var diffs = LegacyRules.Differences(prep.Totals.PerFy.GetValueOrDefault(year.Fy), figures);
            if (written.UnresolvedStayRefs > 0) errors.Add($"{year.Fy}: {written.UnresolvedStayRefs} stay reference(s) did not resolve (left empty)");
            if (written.UnresolvedBillRefs > 0) errors.Add($"{year.Fy}: {written.UnresolvedBillRefs} bill reference(s) did not resolve (left empty)");
            var match2 = diffs.Count == 0;
            if (!match2)
            {
                await store.RollbackToSavepointAsync(tx, save, ct).ConfigureAwait(false);
                errors.Add($"{year.Fy}: reconciliation mismatch on {string.Join(", ", diffs.Select(d => d.Figure))}; that year was rolled back");
            }

            perFy.Add(new LegacyFyResult(year.Fy, written.Stays, written.Segments, written.Bills, written.Receipts, year.Stays.Count(s => s.Status == "Duplicate"),
                year.Bills.Count(b => b.OutOfFy),
                new LegacyReconciliation(figures.BillsCount, figures.NetPaise, figures.RoomGstPaise, figures.FbPaise, figures.ReceiptsCount, figures.ReceiptsPaise,
                    figures.RefundsCount, figures.RefundsPaise, match2, diffs.Select(d => new LegacyDifference(d.Figure, d.Expected, d.Actual)).ToList())));
        }

        foreach (var fy in prep.Totals.PerFy.Keys.Except(prep.Years.Select(y => y.Fy), StringComparer.Ordinal).Order(StringComparer.Ordinal))
            errors.Add($"{fy}: in totals.json but no rows in the history CSVs");
        await tx.AuditAsync("legacy.history", "lgc-history",
            Clip(string.Join(" · ", perFy.Select(y => $"{y.Fy} {(y.Reconciliation.Match ? "loaded" : "rolled back")} {y.Stays} stays {y.Bills} bills {y.Receipts} receipts")), 1000), ct)
            .ConfigureAwait(false);
        return new LegacyHistoryResult(perFy, errors.Take(MaxListed).ToList());
    }

    // ---------------------------------------------------------------- cut-over (§5 rule 3)

    private async Task<LegacyCutoverResult> CutoverAsync(ICommandTransaction tx, Prepared prep, List<string> warnings, CancellationToken ct)
    {
        var p = tx.Context.PropertyId;
        var companies = await store.CompaniesByGstinAsync(tx, p, ct).ConfigureAwait(false);
        var net = new Dictionary<string, long>(StringComparer.Ordinal);
        void Add(string account, long debitMinusCredit) => net[account] = net.GetValueOrDefault(account) + debitMinusCredit;
        static string Party(string control, string party) => control + "/" + (party.Length <= 36 ? party : party[..36].TrimEnd('-', '.', '_'));

        var rv = prep.Receivables!;
        for (var i = 0; i < rv.Rows.Count; i++)
        {
            var r = rv.Rows[i];
            var l = i + 2;
            var balance = rv.Long(r, "balance_paise", l) ?? 0;
            var amount = balance > 0 ? balance : rv.Long(r, "net_paise", l) ?? 0;
            if (amount <= 0) continue;
            var gstin = rv.Get(r, "gstin")?.ToUpperInvariant();
            var account = gstin is not null && companies.TryGetValue(gstin, out var cmp)
                ? Party("1110", PartyLedgers.Company(cmp))
                : Party("1120", PartyLedgers.Udhaar(rv.Get(r, "guest_name"), rv.Get(r, "phone_key")));
            Add(account, amount);
        }

        var op = prep.CustomerOpening!;
        for (var i = 0; i < op.Rows.Count; i++)
        {
            var r = op.Rows[i];
            var amount = op.Long(r, "opening_paise", i + 2) ?? 0;
            if (amount != 0) Add(Party("1120", PartyLedgers.Udhaar(op.Get(r, "guest_name"), op.Get(r, "phone_key"))), amount);   // + guest owes, − hotel holds
        }

        var ad = prep.Advances!;
        long advances = 0;
        for (var i = 0; i < ad.Rows.Count; i++) advances += ad.Long(ad.Rows[i], "amount_paise", i + 2) ?? 0;
        if (advances != 0) Add("2000", -advances);

        var note = "Party opening balances written as a DRAFT of the ACC-1 opening entry; the Owner approves them in Accounting › Opening balances.";
        await openings.SerializeAsync(tx, p, ct).ConfigureAwait(false);
        var current = await openings.GetAsync(tx, p, ct).ConfigureAwait(false);
        if (current?.Approved == true)
        {
            warnings.Add("The opening balances are already approved: the cut-over lines were NOT written. The Owner reopens them, then runs the cut-over part again.");
            note = "Not written: the opening entry is approved (reopen it first).";
        }
        else if (net.Count > 0)
        {
            var lines = (current?.Lines ?? []).Where(x => x.Account != OpeningBalanceRules.DifferenceAccount && !net.ContainsKey(x.Account))
                .Select(x => new OpeningLineRequest(x.Account, x.Debit, x.Credit))
                .Concat(net.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new OpeningLineRequest(kv.Key, kv.Value > 0 ? kv.Value : 0, kv.Value < 0 ? -kv.Value : 0)))
                .ToList();
            if (lines.Count > OpeningBalanceRules.MaxLines)
                warnings.Add($"The opening entry now has {lines.Count} lines (the screen edits at most {OpeningBalanceRules.MaxLines}); review it with the CA.");
            var withBalance = OpeningBalanceRules.WithBalance(lines);
            var asOf = OpeningBalanceRules.FyStart(await store.BusinessDateAsync(tx, p, ct).ConfigureAwait(false));
            await openings.SaveDraftAsync(tx, p, asOf, withBalance, "Legacy cut-over (draft, importer)", tx.Context.PrincipalId, Now, ct).ConfigureAwait(false);
            await tx.AuditAsync("ACC_OPENING_SAVED", "acc-opening:" + OpeningBalanceRules.Iso(asOf),
                Clip($"Opening balances as of {OpeningBalanceRules.Iso(asOf)} saved (draft) by the legacy importer · {net.Count} legacy party line(s) · {withBalance.Count} line(s) in all", 1000), ct)
                .ConfigureAwait(false);
        }

        return new LegacyCutoverResult(op.Rows.Count, rv.Rows.Count, ad.Rows.Count, prep.InhouseRows, note);
    }
}
