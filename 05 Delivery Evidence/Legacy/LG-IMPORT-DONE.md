# LG-IMPORT DONE

Lane lg-import / Opus / branch `claude/lg-import` / worktree `E:\Projects\HCC\worktrees\lg-import` / base `ced9861e`. claude/lg-schema (25d1f8b5) merged in; contract stub removed.

## Commits
- 7f27920a legacy importer, lgc SQL store, migration 4605, fictional run folder + tests (provisional csproj, schema stub)
- 4d3852d4 compile fixes; 27 tests green
- 37062a6c merge claude/lg-schema (kept its 4600/4601 and csproj), stub dropped, 4605 locked
- 34ce26eb merge claude/lg-schema (DONE head 25d1f8b5)
- 2bb0b0b3 DONE file
- 48e47956 LG-R10: shred the whole secret\ folder; DONE file updated for LG-R7…R10

## Files
Server: `src/Hcc.Contracts/Legacy/LegacyContracts.cs`, `src/Hcc.Domain/Legacy/LegacyRules.cs`, `src/Hcc.Application/Legacy/{LegacyImportService,LegacyImportPorts,LegacyCsv}.cs`, `src/Hcc.Infrastructure.SqlServer/Legacy/SqlLegacyStore.cs`, `src/Hcc.Api/Endpoints/LegacyEndpoints.cs`, `src/Hcc.Api/HccComposition.cs` (two lines, each in a `// LG` block: `services.AddLegacyModule()` and `api.MapLegacyEndpoints()`).
Migration: `database/migrations/4605_legacy_capture_kind.sql` (+ lock entry, `Hcc.slnx` line).
Tests: `tests/Hcc.Legacy.Tests/Import/{LegacyImportWorld,LegacyImportTests}.cs`, `tests/Hcc.Legacy.Tests/Fixtures/run-fictional/**` (fictional run folder in the extractor layout, README inside).

## What was built (contract §5, coordinator rulings of 1 Nov)
- `POST /api/v1/commands/legacy.import` → `LegacyImportResult` (§5 shape); Owner only (PERM-LGC-001). Idempotency key computed by the server: `lgc-` + SHA-256(folder | parts | dryRun | SHA-256 of totals.json); a header key is not needed. Same request twice = replay.
- Folder checks before any write: absolute, no UNC, exists, has `totals.json`; **every `*.csv` under the folder** is refused if a header looks like an ID number (`idNumber`, `aadhaar/aadhar`, `pan`, `passport`, …).
- totals.json: `fy.<fy>.afterDedup` (also accepts `perFy`/array layouts); figures billsCount, netPaise, roomGstPaise, fbPaise, receiptsCount (advance + payment rows only), receiptsPaise, refundsCount, refundsPaise; `staysDuplicate` when present. Counts exact; money ±1 paise per row. A missing figure = difference (expected −1).
- `guests`: matches each valid-phone `lgc_guest_map` row by phone key **and** folded `guest_name` (never phone alone, LG-R1); group primary = `is_primary` else highest id → `gst.guest.legacy_guest_id`; `dob` from the secret file or the map's `dob`; ID number sealed via `IGuestIdStore` + `GuestIdProtector` (no new crypto) + id_type / last 4 via the documents store; skipped when HCC already holds a number for that guest (warning). `idNumber: null` rows (LG-R2): dob + images only. Images → `doc.id_capture` kind `legacy`, status Confirmed, OCR text sealed only when there is a number, image sealed with the documents module's own purposes and `IDocumentImageStore`. Tags from `lgc_tags.csv`. Never creates a guest; unmatched rows listed by legacy id only. After a committed run the **whole `secret\` folder** (jsonl + `images\photo`, `images\idproof`) is overwritten file by file with zeros and deleted, one audit row `legacy.secret_shredded` (LG-R10).
- `history`: `lgc.guest_map` replaced (guest_id resolved by the same matcher), then per FY under a SQL savepoint: delete + SqlBulkCopy into stay / room_segment / bill / receipt, `stay_ref` / `bill_ref` resolved; figures recomputed from the inserted rows; mismatch → savepoint rollback of that FY only + `differences[]`. Refused (DAY_CLOSED) once any business day is closed; `cutover` alone still runs.
- `cutover`: see Open item 1.
- Dry run: everything in the transaction, rolled back; an `lgc.import_run` row `DryRun` + audit is written afterwards. Committed: run row `Done` + audit `legacy.import` (summary; full JSON in `result_json`). Failed command: run row `Failed`.
- `GET /api/v1/legacy/runs?limit=` (PERM-LGC-001), newest first, with the parsed result.

## Tests
`dotnet test tests/Hcc.Legacy.Tests -maxcpucount:1` (through the lock): before = project did not exist; after = **31 passed / 0 failed** (27 lg-import + 4 lg-schema Sql). `dotnet build Hcc.slnx`: 0 warnings, 0 errors. Covered: ID-column rules, UNC/relative refusal, totals reader, tolerance, never-phone-only match, key inputs; dry run leaves no rows; full commit (guests, sealing, images, tags, history, cut-over draft, shred + audit); FY rollback on a wrong total (other FY kept); idempotent re-run; ID column refused before writing; Manager refused; history refused after a day close, cutover alone runs, Failed run listed.
Not run: Hcc.Platform.Tests (migration lock test) — `migrations-lock.mjs` check says the lock is in step (99).

## Open items
1. **Cut-over target — ACCEPTED by the coordinator as built (ruling LG-R7).** Was: §5 rule 3 names `imp.opening_set` / `imp.opening_balance` but also says "nothing approved by the importer; the Owner approves in the ACC-1 opening screen". `imp.opening_set` has no approval step (it is effective at once, needs an `imp.batch` row and a balanced set, and would replace the Owner's imported trial balance). Built: the lines go into the **ACC-1 opening entry as a draft** (`acc.opening_entry` / `acc.opening_line` via `IOpeningEntryStore.SaveDraftAsync`, as of 1 April of the business FY, 1400 balancing line recomputed). Receivables → `1110/CMP-<id>` when the bill's GSTIN matches a company account, else `1120/UD-<phone or name slug>`; customer openings → `1120/UD-…` (+ Dr / − Cr); advances → `2000` Cr total. Existing draft lines on other accounts are kept; an approved entry is not touched (warning). (Accepted, LG-R7.)
2. (Integrator, LG-R8) 756 customer openings will exceed the opening screen's 500-line edit limit (warning only).
3. `discountAmtOnlyPaise` is not a reconciled figure (not in the §5 result shape); lgc.bill.discount_paise is loaded as the CSV gives it.
4. Legacy captures follow DEC-071 like any capture (ruling LG-R9): the manual purge treats them as normal.
5. Host command dropped by the coordinator; lg-tests uses the HTTP command.
6. Import tests use their own disposable-DB world (`LegacyImportWorld`, same pattern as lg-schema's `LegacyDb`) rather than `LegacyDb`, because they need users, guests and the full service graph; consolidation is optional.
7. Done (LG-R10): secret images are shredded with the jsonl.
8. **DOB survival (integrator, LG-R6):** `SqlStaysStore` guest update writes `dob = @dob` unconditionally (line ~114), so a later guest save that omits `dob` **wipes** the importer's DOB. Integrator: make "dob omitted = unchanged".

## Integration needed
- HccComposition: already appended (two `// LG` blocks) — merge as is.
- lg-tests: may add files under `Fixtures/run-fictional/` (never change existing ones); its totals.json is hand-computed (README there). Request shape: `{ folder, parts, dryRun, expectedTotals? }`.
- Contract §3 note: `UQ_lgc_receipt` works because security rows carry a negative `legacy_row_id` (extractor ruling 2).

## HANDOFF bullet
- LG lg-import: `legacy.import` (Owner, PERM-LGC-001) loads a run folder — guests (match by phone + name, legacy id, DOB, sealed IDs, images as `legacy` captures, tags, secret file shredded), history per FY with savepoint rollback on any §6 mismatch, cut-over as an ACC-1 opening **draft** — plus `GET /legacy/runs`; migration 4605 (`doc_kind 'legacy'`). Hcc.Legacy.Tests 31/31. Cut-over draft accepted (LG-R7); whole secret folder shredded (LG-R10). Integrator: guest saves keep `dob` when omitted (LG-R6), >500-line draft (LG-R8).
