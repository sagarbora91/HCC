# LF-docdb + LF-images — lane L2 DONE (partial: B5 member captures not built, see below)

Branch `claude/lf-docdb` (cut from setup commit f71e6fbb). Lane L2 of wave F3 (LF): contract section 1 (the separate documents database, DEC-200),
section 1.11 (G1 on-device compression and the size caps), section 10 (B5 party photos / IDs, B6 invoice notes).

## Built

**HccDocuments database (server)**
- `SqlOptions.DocumentsConnectionString` + `DocumentsConnection.Resolve` (empty = `<catalog>Documents`; must start with `Hcc` and differ from the main catalog,
  otherwise the platform refuses to start); `SqlDocumentConnectionFactory` (same session policy; unreachable = the documents-specific UNAVAILABLE text);
  `SqlPlatform` exposes `Documents`, `DocumentMigrations`, `DocumentObjects`, `DocumentMove`, `DocumentSweep`, `DocumentRefs`.
- Second migration stream: `database/migrations-docs/0001_objects.sql`, `0002_doc_meta.sql`, embedded as `Hcc.DocMigrations.*`
  (`EmbeddedMigrationSource(prefix)`), its own journal (`dbo.schema_migration` inside HccDocuments, same runner and shape; its own app-lock name),
  lock file `database/migrations-docs.lock.json`, `tools/migrations-lock.mjs --docs [--add]`.
- `Hcc.Application/Documents/DocumentObjectPorts.cs` verbatim from contract 1.5 (+ the `DocumentObjectReleased` event type/payload); `SqlDocumentObjectStore`
  (put / info / get / reseal / release / list / sweep / size, each its own transaction on HccDocuments).
- `Infrastructure.SqlServer/DocumentObjects/`: `DocumentMoveJob` (batches of 50, one Hcc transaction per batch, object first then ref, idempotent, journal in
  `plat.doc_move_run`, writes `plat.doc_meta main_database`), `DocumentSweepJob` (24 h margin, per property and kind), `DocumentRefCheck`
  (dangling refs / orphans, daily result in `plat.doc_ref_check`), `DocumentReleaseProjector` (outbox consumer `document-release`).
- The four stores (`SqlDocumentStore` image part, `SqlExpenseStore` bill photo, `SqlCashOutStore` slip, `SqlComplianceStore` evidence) take an optional
  `IDocumentObjectStore`: with it they write the object first and store `object_ref`; a row with a NULL ref still reads its inline column (both paths tested).
  Deletes / replacements publish `DocumentObjectReleased` in the Hcc transaction (the slip retention purge and the custom-obligation delete do it in SQL).
  Wave D re-key: `LockSealedUnderOtherKeysAsync` also selects moved captures; `GetAsync` / `ReplaceAsync` (-> `ResealAsync`) work on both locations.
  A dangling ref reads as 404 NOT_FOUND `details.reason = DOC_MISSING`.
- `Hcc.Api`: `migrate` = main, then documents (own backup gate), then the move job (exit 2 while dangling refs exist, exit 3 when a run moved nothing and
  rows remain); verbs `sweep-documents`, `check-documents`; `SchemaGuard` watches both journals (503 with `details.database = "documents"`; an unreadable
  documents database never blocks); `DocumentObjectsEndpoints` (`AddDocumentObjectsModule`, `POST /admin/documents/sweep` PERM-DOC-006, release worker,
  nightly sweep 03:30); `/system/health` also reports the documents database and version; `GET /admin/system-health` has the `databases` block
  (`SqlDatabaseHealth`, sizes from `sys.database_files`, last backup / drill per database, dangling / orphan counts).
- Migrations (main): 4670 object_ref columns (nullable content columns), 4671 `plat.backup_run.database_name`, 4672 PERM-DOC-006 (Owner, Tech admin),
  4673 `plat.doc_move_run`, 4674 `plat.doc_ref_check` (the reserved slot used for the daily check result), 4707 doc_kind `photo`.
- PowerShell (not executed, no Windows here): `Hcc-Common.ps1` (`DocumentsDatabase` in the config, `Get-HccDatabaseList`, `Get-HccDocumentRefCheckSql`,
  `Invoke-HccDocumentRefCheck`, `Get-HccBackupRunSql -MainDatabase`), `Backup-HccDatabase.ps1` (`-Database` list, main first, documents second, each its own
  file and run row in the MAIN database's `plat.backup_run`), `Restore-HccDrill.ps1` (both databases, `HccDrill_<stamp>` + `...Documents`, CHECKDB and row counts
  on both, reference check, one drill row per database), `Initialize-HccDatabase.ps1` (`-DocumentsDatabase`), `Register-HccBackupTasks.ps1` (text only),
  `hcc-ops.sample.json`, `appsettings*.json`, `INSTALL.md` (sections 1, 2, 5, 7, replacement PC), `tests/Test-HccDeployment.ps1` (+8 checks).

**Prototype**
- `src/domain/streams/docobjects.ts` (`dobPut / dobGet / dobDelete / dobSize / dobCap / dobRefusal / dobRefCheck`, caps and the server's refusal text) and
  the idempotent v12 -> v13 move `dobMigrateV12` (called from `migrate()` in `services/mock/persistence.ts`, where `restoreState` lives; `storage.ts` itself only
  saves slices, `docObjectsX` is saved as its own slice record automatically). Bill photos use `Expense.billPhotoRef`, evidence files `CMPFile.objectRef`.
- `syshealth.ts`: demo "Databases" rows + `sysDbTone / sysDbPct / sysBytes`; `features/admin/SystemHealth.tsx` (rows inside the Backups panel, so the FIT page
  keeps two panels), `mobile/admin/SystemHealth.tsx`, `clients/sync/syshealth.ts` maps the server block.
- G1: `app/slipPhoto.ts` -> `PhotoProfile`, `PHOTO_SLIP`, `PHOTO_DOC`, `photoCompressPlan`, `compressPhoto`, `compressDocPhoto`; `compressSlipPhoto` and
  `slipCompressPlan` unchanged in behaviour; `compressBillPhoto` now uses PHOTO_DOC (SCC constants and `billFit` / `billPickRefusal` kept exported);
  the compliance evidence picker compresses images before upload.
- B6: five `brand.*` settings (config.ts and `BuiltinSettings.cs`), `domain/streams/printnotes.ts` + `features/exports/PrintNotes.tsx` (`PrintTitle`, `PrintNotes`),
  included in the folio invoice (and credit notes) and the POS document.

**Server caps (G1):** `DocumentRules.MaxImageBytes` 153,600, `ExpenseScc.PhotoMaxBytes` 153,600, `ComplianceRules.MaxImageBytes` 153,600 for `image/*` (PDF stays 5 MB);
one text everywhere: "Take the photo again, closer and in good light." Banking slip unchanged (102,399). Legacy images unchanged.

## Deviations from the contract text (please amend)
1. `object_ref` sits on the four **content tables** (`doc.id_capture_image`, `ops.expense_bill_photo`, `pay.banking_slip`, `cmp.evidence_content`), not on
   `doc.id_capture` / `cmp.evidence`: that is where the content lives in this schema.
2. The documents journal is `dbo.schema_migration` (the runner's table), not `plat.schema_journal`; same shape.
3. Jobs live in `Infrastructure.SqlServer/DocumentObjects/` (they need SQL); the existing test `No_background_worker_is_wired_to_the_ID_capture_purge` forbids workers
   that touch the `...Documents` namespaces, and the workers are not related to the ID purge.
4. Migrations 4670-4674 and 4707 are below the highest locked version (4721, KEYS), so they were added to the lock with `--accept-out-of-order` (reason recorded).
   4674 is used (reserve slot) for `plat.doc_ref_check`. 4708-4710 unused.
5. The prototype v12 -> v13 move is in `persistence.ts` (`migrate`), not `storage.ts`.
6. The expense bill photo cap message changed on both sides ("Photo too large (max 4MB)" is gone; 4 MB stays only as the picker's input refusal).
7. The B6 print models: the server has no PDF renderer yet, only the settings (registered, validated, readable); the web print templates read them.

## NOT built (lane stopped on budget; nothing half-wired)
- **B5 (built afterwards, see the B5 section below)**: the rest of this bullet is superseded; still not built: `Documents.tsx` party cards, registration-card party lines, production wiring of the photo screen.
  (old text:) no member capture command / UI, no `Documents.tsx` / `GuestProfile.tsx` party cards, no registration-card party lines,
  no photo capture screen. Done for B5: migration 4707 (doc_kind `photo`, index by party), object kind `GuestPhoto`, `MSGDoc.partySeq` / kind `photo` types from
  the setup, caps for guest photos. `party_seq` itself and the sealed per-member number are DEC-193 (4620) on main; the capture service does not use them yet.
- counter-bill print include (lf-ui includes `PrintNotes` itself), `tools/fit-modals.json`, `check-print-layout` run for the new print blocks.
- 4719 (drop the emptied content columns) is W9 by design.

## Integration needed (lf-int)
- `handlers/index.ts` (`LF-DOCDB` block) needs nothing: this lane adds no mock commands. `services/mock/index.ts` blocks `LF-DOCDB` (docobjects) and
  `LF-IMAGES` (printnotes) are filled; `HccComposition.cs` `LF-DOCDB` services/endpoints lines are filled.
- **Hash re-pin (the announced one):** the compliance fixture's demo evidence file moved from `complianceFilesX` to `docObjectsX` (otherwise a restored BASE would be rewritten by
  the v13 move and the persistence round-trip tests would fail). That changes the BASE / FY26 hashes: the pinned-hash tests listed below fail until the integrator re-pins once.
- `tools/lf-drill-check.ps1` is not written (needs Windows): run `Initialize-HccDatabase.ps1 -WhatIf`, `Backup-HccDatabase.ps1 -WhatIf`, `Restore-HccDrill.ps1 -WhatIf`
  and `tests\Test-HccDeployment.ps1` on the hotel PC.
- `ETP_Reuse_Pending.md`: a real OCR engine in W8 may want a larger image than 150 KB.
- Other lanes' tests that build their own `SqlPlatform` need nothing (the documents database is only touched when an image is stored).

## Tests (this worktree, SQL test server on this machine)
- `npx tsc --noEmit`: clean. `npm test`: **2392 / 2403 pass**. The 11 failures: `[FIT] no phone-layout file changed since the wave base` (pre-existing, ignore) and the 10 pinned
  BASE / FY26 hash tests ([CAT], [FDM], [MFX] x2, [MST], [OPSM], [PPL], [PERF], [POS-MST], [RMS]) which fail only because of the fixture move above (integrator re-pin).
  New: `tests/stream-lf-docdb.mjs` 8 tests, `tests/stream-lf-images.mjs` 7 tests; `stream-compliance.mjs` / `stream-expenses-scc.mjs` adjusted to the refs and the 150 KB cap.
- `dotnet build "08 Production/Hcc.slnx"`: 0 warnings, 0 errors.
- `Hcc.Documents.Tests` 41/41 (new: `DocumentObjectStoreTests`, 14 tests on the two-database harness `DocumentsDatabases`: store, caps text, derived name rule, both read paths,
  DOC_MISSING, release consumer, move job end to end (12 rows, zero remaining, second run moves nothing), sweep, ref check, health rows, journal status).
- `Hcc.Platform.Tests` MigrationLock 9/9 (new `DocumentsMigrationLockTests`, 3 tests: both lock files), `Hcc.Configuration.Tests` RegistryAndRules 13/13 (B6 keys),
  `Hcc.Compliance.Tests` ComplianceSqlTests 13/13 + 1 new (image cap / PDF), `Hcc.Expenses.Tests` ExpenseSccCommandTests 10/10 (cap at 153,600 and +1),
  `Hcc.Integration.Tests` SystemHealth + SchemaGuard 4/4 (new: databases block over HTTP, sweep permission, documents-behind 503), `Hcc.Operations.Tests` OperatorAlerts 7/7.
- `Hcc.Operations.Tests` whole project: 3 pre-existing Linux-only failures in `HostingTests` ("BackupDirectory must be a full folder path": Windows paths on Linux), unrelated.
- PowerShell: `tests/Test-HccDeployment.ps1` has 8 new checks; **not executed (no Windows)**; the scripts were edited with care but never run.

## Migrations / locks
Main: 4670-4674, 4707 (+ lock entries with accepted out-of-order reasons). Documents stream: 0001-0002 with `migrations-docs.lock.json`. `Hcc.slnx` lines are inside the
`LF-DOCDB` and `LF-IMAGES` range comments. Run `node tools/migrations-lock.mjs` and `node tools/migrations-lock.mjs --docs` (both in step at the last commit).

## B5 (DEC-201) — added after the first report
- **Server**: `CaptureIdDocumentRequest.PartySeq` / `IdCaptureView.PartySeq`; doc kind `photo` (no OCR, no review, confirmed at once, sealed like an ID image, kind GuestPhoto in HccDocuments);
  an ID capture with `PartySeq` confirms into `gst.guest_party_id_secret` (`UpsertPartyAsync`, AAD with the seq) and sets the member's `id_type` / `id_last4`
  (`SetPartyIdProfileAsync`); the registered guest's own number is untouched. Member must exist (404), seq 1-6 (400), non-ID roles refused (same permissions as ID capture).
  Tests `B5_party_photo_...` and `B5_party_member_full_id_...` in `Hcc.Documents.Tests` (43/43 with them).
- **Prototype**: `GUEST_PHOTO` / `GUEST_PHOTO_REMOVE` (`handlers/guestphotos.ts`, block `LF-IMAGES`), photo per person at `dobPhotoRef(guest, seq)`, 150 KB cap, `check-in` permission, never in audit text;
  `features/guests/PartyPhotos.tsx` (one card per person, `compressDocPhoto` at the pick site, shown only to ID viewers) inside `GuestProfile.tsx` (demo build only: the production
  build does not show it because the sync client does not map `GUEST_PHOTO` to `CaptureIdDocument {docKind: photo, partySeq}` yet - lf-int / W8). The member's full ID number was already
  captured in the guest form by DEC-193. 3 new tests in `stream-lf-images.mjs` (10 in all).
- Not done: `Documents.tsx` capture page cards, registration-card party lines (lf-desk left the placeholder), phone layout of the photo cards (reuses the same component), sync mapping.
