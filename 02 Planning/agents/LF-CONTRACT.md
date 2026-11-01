# LF contract (wave `claude/lf`, prepared 1 Nov 2026) — legacy feature parity and the documents database

The shared rules every LF lane builds against. **Copy, never reinterpret.** A lane that needs a change writes it in its DONE file; the coordinator amends this file and tells the other lanes. Scope ruling: **DEC-200** (gap list A1–A8, B1–B7, G1; photos and every attached document in a separate SQL Server database; execution by Sonnet and Opus lanes). Sources of the behaviour: `02 Planning/HCC_Legacy_Feature_Gap_v1.md` (the gap list), the old software's forms in `E:\Projects\HCC\Existing Software\Forms\Forms\*.frm` and `Existing Software\HTML Demo\FUNCTION_MAP.md` (what each button does), and the HCC code on the base commit. Rulings that bind every feature: DEC-058 / DEC-061 (Owner-only money settings; housekeeping and maintenance never see money), DEC-063 (who requests, who approves), DEC-069 (dated GST master, integer paise, CGST + SGST per line), DEC-077 (a posted line on a closed day is never edited; reversals post on the business date unless the Owner reopened that day), DEC-194…180 + `FIT-LAYOUT-CONTRACT.md` (every new desktop screen fits one screen, no smaller text, sub-screens as tabs), DEC-197 (invoice series `HPE/2627/0001`; two series in FY 2026-27 → GSTR-1 Table 13), DEC-198 (rate card, room GST 18 % set by the Owner at setup), DEC-161…164 / DEC-199 (legacy data read-only in `lgc`, kept as is), DEC-116 (nothing ported partially).

**Base:** `claude/lf` = `claude/fit` e654fc89 (main 7dd260b4 with ACC-2 + W1a LG + the 1 Nov decisions). FIT is running now; **LF starts after FIT is on `main`** — the coordinator merges `main` into `claude/lf` then and re-cuts the lane worktrees; every new page uses `FitPage` / `FitTabs` / `SplitView` from `src/components/fit.tsx` (contract §4 of FIT) from the first commit.

**Not in LF:** A9 referrers (owner to confirm first), B8 messaging (W8 GL stand-ins), the §E reports as screens (W7 RPT — §13 below lists the data LF leaves for them).

## 0. Vocabulary and conventions

| Term | Meaning here |
|---|---|
| paise | every amount an integer number of paise (`number` in TypeScript, `BIGINT` in SQL); never a float, never rupees in code |
| rate | a room rate is **per night before GST** as HCC's tariffs are stored today (DEC-198: card ÷ 1.18); screens show the GST-inclusive figure beside it, computed with `gstForCharge` on the night's date |
| business date / clock | `s.date` / `s.clock` (prototype), `inv.property.business_date` + `TimeProvider` (server); never the real clock |
| approval | an `Approval` row (`s.approvals`, server `plat.approval` flow as used by folio corrections): kind, amount, reason, target, 24 simulated-hour expiry, nobody approves their own request, stale when the folio version moved |
| Owner-decides kinds | `RBAC_OWNER_DECIDES` in `rbac.ts`: corrections and credit notes are approved by an Owner (DEC-063 G3); LF adds `bill-correction` to that list |
| Manager limit | `adminPolicy(s).limits.Manager[perm]`; the server's `plat.role_permission` limit column; LF reuses `PERM-RATE-001` (rate override below tariff, Manager 15 %, seeded in 0101) |
| money roles | Owner, Manager, Reception, Cashier, Accountant (DEC-MA-11e); Supervisor, Housekeeper, Maintenance, Tech admin, Restaurant never receive a money value from LF |
| FIT page | a desktop page whose root is `FitPage` with the named layout; sub-screens registered in `src/app/subscreens.ts` inside the lane's own `// LF-<LANE>` block (the integrator adds the seven empty blocks in the setup commit) |
| phone page | `FeatureRegistration.mobile.pages` entry; 48 px targets, 16 px text, cards, no sideways scroll at 360 px |

Every command: `execute(state, uid, cmd)` with a typed `HccError`, an idempotency key, copy-on-write per slice; every server command through the command pipeline (`ICommandTransaction`, outbox events, audit) exactly as ACC-2 did. New state is optional fields / optional slices only; `SCHEMA` 12 → **13** (coordinator, setup commit, §11).

## 1. The documents database (owner ruling DEC-200) — lane lf-docdb (Opus)

### 1.1 What moves

Today the main database `Hcc` holds every blob: `doc.id_capture_image.sealed` (ID images, sealed text; migration 2100, 4605 adds the LG `legacy` kind), `ops.expense_bill_photo.image` (4150), `pay.banking_slip.image` (3100), `cmp.evidence_content.content` (4130). Metadata (who, when, which guest / expense / transfer / obligation, content type, size, status, OCR last-4) stays where it is. **Only the content moves** to a second database, default name **`HccDocuments`**, which has its own 50 GB Express allowance (the log file does not count). Nothing else leaves `Hcc`.

### 1.2 Databases, connection, configuration

- `Hcc:Sql:DocumentsConnectionString` (new; `SqlOptions.DocumentsConnectionString`). Empty = derived from `ConnectionString` by replacing `Initial Catalog` with `<catalog>Documents` (so `Hcc` → `HccDocuments`, `HccTest_x` → `HccTest_xDocuments`). The derived name must start with `Hcc` (the scripts' `Assert-HccDatabaseName` rule) and must differ from the main catalog; the server refuses to start otherwise. Same login, same `Encrypt` / `TrustServerCertificate` policy, same `CommandTimeoutSeconds`.
- `SqlConnectionFactory` stays the main factory. New `SqlDocumentConnectionFactory` (same session policy: XACT_ABORT, QUOTED_IDENTIFIER, ANSI_NULLS checks) built from the documents connection string; registered as its own singleton; `SqlPlatform` exposes `Documents` (factory) and `DocumentMigrations` (runner).
- `appsettings.json`, `appsettings.Production.sample.json`, `deployment/windows/hcc-ops.sample.json` (`"DocumentsDatabase": "HccDocuments"`), `INSTALL.md` §1–2, §5 (migrate), §9 (backups), §restore all name both databases.

### 1.3 Schema of `HccDocuments` (its own migration stream)

Folder `08 Production/database/migrations-docs/` (file names `NNNN_name.sql`, same regex as the main runner), embedded as `Hcc.DocMigrations.*` by a second `EmbeddedMigrationSource(prefix)`; journal table `plat.schema_journal` inside `HccDocuments` (same shape the main runner writes); lock file `08 Production/database/migrations-docs.lock.json`, written by `tools/migrations-lock.mjs --docs --add` (the tool gains a `--docs` switch selecting folder + lock file; `MigrationLockTests` checks both). Block **D0001–D0019** belongs to lf-docdb (numbers 0001–0019 in that stream; `D` is how documents name them in text, the file is `0001_objects.sql`). `Hcc.slnx` lists each file as the main ones are listed.

```sql
-- 0001_objects.sql (HccDocuments)
CREATE SCHEMA dob;   -- document objects
GO
CREATE TABLE dob.object(
  object_id BIGINT IDENTITY CONSTRAINT PK_dob_object PRIMARY KEY,
  object_ref UNIQUEIDENTIFIER NOT NULL CONSTRAINT UQ_dob_object_ref UNIQUE,     -- the key the main database stores (never the identity)
  property_id INT NOT NULL,                                                      -- no FK: another database
  kind VARCHAR(20) NOT NULL CONSTRAINT CK_dob_object_kind CHECK (kind IN ('id-image','guest-photo','expense-bill','banking-slip','compliance-evidence','counter-attachment','legacy-image')),
  owner_table VARCHAR(40) NOT NULL,          -- 'doc.id_capture' | 'ops.expense' | 'pay.banking_transfer' | 'cmp.evidence' | 'ctr.bill' | ...
  owner_id BIGINT NOT NULL,                  -- that table's key (informational: the sweep uses the ref list it gets from Hcc, not this)
  content_type VARCHAR(30) NOT NULL,         -- image/jpeg | image/png | image/webp | application/pdf
  size_bytes INT NOT NULL CONSTRAINT CK_dob_object_size CHECK (size_bytes BETWEEN 1 AND 8388608),
  sealed BIT NOT NULL,                       -- 1 = content is the application-sealed text (ID images and legacy images, DEC-068 / Wave D re-key)
  sha256 BINARY(32) NOT NULL,                -- of the stored bytes (plain) or of the sealed text (UTF-8); the restore drill compares it
  created_utc DATETIME2(3) NOT NULL,
  created_by INT NOT NULL,
  deleted_utc DATETIME2(3) NULL);            -- set by the release consumer; the sweep removes the row + content later (§1.7)
CREATE INDEX IX_dob_object_owner ON dob.object(property_id, owner_table, owner_id);
CREATE INDEX IX_dob_object_sweep ON dob.object(created_utc) WHERE deleted_utc IS NULL;
GO
CREATE TABLE dob.object_content(
  object_id BIGINT NOT NULL CONSTRAINT PK_dob_object_content PRIMARY KEY CONSTRAINT FK_dob_object_content REFERENCES dob.object ON DELETE CASCADE,
  content VARBINARY(MAX) NULL,               -- plain bytes (sealed = 0)
  sealed_text VARCHAR(MAX) NULL,             -- the sealed string exactly as doc.id_capture_image.sealed holds it today (sealed = 1)
  CONSTRAINT CK_dob_object_content_one CHECK ((content IS NULL) <> (sealed_text IS NULL)));
GO
-- 0002_journal_note.sql: plat.doc_meta(key, value) with ('main_database', '<name>') written by the move job so a restore of a wrong pair is detected (§1.9).
```

Why two content columns: the Wave D re-key (`SqlDocumentStore.LockSealedUnderOtherKeysAsync`, `ReplaceAsync`) compares `LEFT(sealed, LEN(prefix))` on the sealed text; keeping the text column keeps that code a one-table move instead of a redesign.

### 1.4 Changes in `Hcc` (main stream, migrations **4620–4624**)

- 4620 `doc_object_refs.sql`: `ALTER TABLE doc.id_capture ADD object_ref UNIQUEIDENTIFIER NULL;` `ALTER TABLE ops.expense_bill_photo ADD object_ref UNIQUEIDENTIFIER NULL;` `ALTER TABLE pay.banking_slip ADD object_ref UNIQUEIDENTIFIER NULL;` `ALTER TABLE cmp.evidence ADD object_ref UNIQUEIDENTIFIER NULL;` plus the two LF kinds that are born in the new store (`gst.guest_photo` — B5, lane lf-images; `ctr.bill_attachment` — none in LF, reserved). The old content columns become **nullable** (`sealed`, `image`, `content` → NULL allowed) so the move can clear them row by row.
- 4621 `plat_backup_run_database.sql`: `plat.backup_run ADD database_name NVARCHAR(128) NOT NULL DEFAULT 'Hcc'`; index on (database_name, started_utc).
- 4622 `doc_rbac_seed.sql`: `PERM-DOC-006 documents.objects.admin (orphan sweep, move job, documents database health)` → Owner, Tech admin (non-money).
- 4623 `plat_doc_move_log.sql`: `plat.doc_move_run(run_id, started_utc, finished_utc, moved_rows, cleared_rows, remaining_rows, status, error)` — the idempotent move's journal (§1.6).
- 4624 reserved (lf-docdb fallout). **4669** is reserved for dropping the emptied content columns / tables; it is **not written in LF** (it runs in W9 after the owner's restore drill on the hotel PC, when every row has moved — recorded in the Build Plan).

### 1.5 The store API (contract file; consumers copy it verbatim)

`Hcc.Application/Documents/DocumentObjectPorts.cs`:

```csharp
public enum DocumentObjectKind { IdImage, GuestPhoto, ExpenseBill, BankingSlip, ComplianceEvidence, CounterAttachment, LegacyImage }
public sealed record NewDocumentObject(int PropertyId, DocumentObjectKind Kind, string OwnerTable, long OwnerId, string ContentType, ReadOnlyMemory<byte>? Content, string? SealedText, int CreatedBy);
public sealed record DocumentObjectInfo(Guid ObjectRef, DocumentObjectKind Kind, string ContentType, int SizeBytes, bool Sealed, DateTime CreatedUtc, DateTime? DeletedUtc);
public interface IDocumentObjectStore
{
    /// Writes the object in its own committed transaction on HccDocuments and returns the ref the caller stores in its Hcc row (§1.8 order).
    Task<Guid> PutAsync(NewDocumentObject o, CancellationToken ct = default);
    Task<DocumentObjectInfo?> InfoAsync(Guid objectRef, CancellationToken ct = default);
    Task<(DocumentObjectInfo Info, byte[]? Content, string? SealedText)?> GetAsync(Guid objectRef, CancellationToken ct = default);
    /// Replaces the sealed text (Wave D re-key); refuses when the object is not sealed.
    Task ResealAsync(Guid objectRef, string sealedText, CancellationToken ct = default);
    /// Marks deleted (release); the sweep removes the bytes. Idempotent.
    Task ReleaseAsync(Guid objectRef, CancellationToken ct = default);
    /// Refs of live (not deleted) objects of a kind for a property, oldest first, paged — the sweep and the health check read this.
    Task<IReadOnlyList<(Guid Ref, DateTime CreatedUtc)>> ListAsync(int propertyId, DocumentObjectKind kind, DateTime? createdBefore, int take, CancellationToken ct = default);
    /// Deletes rows marked deleted before `before`, and (orphan sweep) rows older than `before` whose ref is NOT in `live`. Returns the count.
    Task<int> SweepAsync(int propertyId, DocumentObjectKind kind, DateTime before, IReadOnlySet<Guid> live, CancellationToken ct = default);
    Task<DocumentDatabaseSize> SizeAsync(CancellationToken ct = default);
}
public sealed record DocumentDatabaseSize(string Database, long DataBytes, long LogBytes, long LimitBytes /* 50 GB Express */, int ObjectCount, long ContentBytes);
```

`SqlDocumentObjectStore` (Infrastructure, folder `Documents/`) implements it on `SqlDocumentConnectionFactory`. The four existing stores (`SqlDocumentStore` image part, `SqlExpenseStore` bill photo, `SqlCashOutStore` slip, `SqlComplianceStore` evidence content) keep their interfaces but read / write through `IDocumentObjectStore` by `object_ref`; a row whose `object_ref` is NULL still reads the old column (pre-move rows) — **both paths are tested** until 4669 runs. `LegacyImportService` (W1a) writes legacy images as kind `LegacyImage` through the same store (it already goes through `IDocumentImageStore.PutAsync` — only the adapter changes). The Wave D re-key (`DocumentSecretsRekey` / `SqlDocumentStore.LockSealedUnderOtherKeysAsync` + `ReplaceAsync`) reads moved objects through `ListAsync` + `GetAsync` and writes `ResealAsync`; un-moved rows as today.

### 1.6 Upgrade path for an existing install (idempotent move)

`Hcc.Api.exe migrate` does, in this order, each step logged to the console and `plat.doc_move_run`: (1) migrate `Hcc` (backup gate as today, 4620–4624 included); (2) migrate `HccDocuments` (own backup gate: a COPY_ONLY backup of `HccDocuments` when it already holds rows; a fresh database needs none); (3) **move job**: for each of the four tables, in batches of 50 rows ordered by key, under one `Hcc` transaction per batch: read the content, `PutAsync` to `HccDocuments` (own transaction), then `UPDATE <row> SET object_ref = @ref, <content column> = NULL`; a batch that fails rolls back its `Hcc` rows and leaves at most 50 objects in `HccDocuments` that the sweep removes later (§1.7); re-running `migrate` continues with the rows whose `object_ref IS NULL`. The job ends with a verification count (`remaining_rows` = rows with content still in `Hcc`); `migrate` exits non-zero if remaining > 0 after a run that moved nothing (a stuck row is named in the log). (4) `SchemaGuard` watches **both** journals; while either is behind, every endpoint answers 503 with the existing public message (plus `database: "documents"` in the details for the server PC). A fresh install has nothing to move: step 3 reports `moved 0`.

Schema versions are reported as `Hcc 4624 / HccDocuments 0002` on `/api/v1/system/health` (unauthenticated loopback) and `/admin/system-health`.

### 1.7 References between the databases: integrity and orphans

- **No cross-database foreign key.** The `Hcc` row carries `object_ref`; `dob.object` carries `owner_table` / `owner_id` only as a hint.
- **Delete path:** when an `Hcc` row is purged / deleted (ID retention purge, banking-slip retention purge `SqlRetentionPurge`, expense void never deletes a photo, evidence removal, legacy capture purge), the `Hcc` transaction publishes an outbox event `DocumentObjectReleased {objectRef, kind}`; the consumer calls `ReleaseAsync` (marks `deleted_utc`); a failure parks the event (System health shows it as today, retry as today). Nothing in `Hcc` ever waits on `HccDocuments` inside its transaction.
- **Orphan sweep:** a background job (outbox worker schedule, daily at 03:30 server time, and `Hcc.Api.exe sweep-documents` on demand, PERM-DOC-006 holders through `POST /admin/documents/sweep`): per kind, `ListAsync` the live refs older than **24 hours** in `HccDocuments`, fetch the set of `object_ref` the matching `Hcc` table holds, `SweepAsync` what is not referenced (and everything marked deleted more than 24 hours ago). Logged to `plat.doc_move_run` with `status = 'Sweep'`. The 24-hour margin covers a write whose `Hcc` transaction is still open.
- **Dangling refs** (an `Hcc` row whose object is missing in `HccDocuments`): the read endpoint answers 404 `{code: NOT_FOUND, details.reason: 'DOC_MISSING'}`; the UI shows "Image missing — see System health"; System health counts dangling refs per kind (a daily check writes the count; the restore drill fails on any).

### 1.8 Transactions across the two databases (the write order)

1. **Blob first, in its own committed transaction** on `HccDocuments` (`PutAsync` returns the ref).
2. **Then the `Hcc` command transaction** writes the metadata row with that ref, the audit row and the outbox event, and commits.
3. If step 2 fails, the blob is an orphan; the sweep removes it after 24 hours. Nothing is lost, nothing points at nothing.
4. If step 1 fails (documents database unreachable or full), the command fails with `UNAVAILABLE` and the message **"The documents database is not reachable (or full). The rest of HCC keeps working; try the photo again later."** Commands that carry an optional image (expense without a bill photo, check-in without an ID image) run without it; commands whose whole purpose is the image (ID capture, slip photo, evidence upload) refuse.
5. Reads never join the databases: the metadata list comes from `Hcc`, the bytes from `HccDocuments` on demand (as the listing today never reads the image).
6. Nothing uses distributed transactions (MSDTC): SQL Express on one PC, two local transactions, the order above.

### 1.9 Backup, restore, restore drill, installer, first-time setup, System health

- **Backup:** `Backup-HccDatabase.ps1` gains `-Database` as a list; default from `hcc-ops.json` `Database` + `DocumentsDatabase`; the scheduled tasks (`Register-HccBackupTasks.ps1`: Full daily, Differential every N hours) back up both databases in one run, **main first, documents second**, each file named per database (`HccDocuments_FULL_<stamp>.bak.hccenc`), each run row in `plat.backup_run` with `database_name`; retention per database as today (A09-10 already counts by parsed name). The pre-migration backup (`SqlBackupGate`) runs for each database that is about to receive migrations.
- **Restore (INSTALL.md "moving to a new PC"):** restore both; then run `Hcc.Api.exe check-documents`, which compares every `object_ref` in `Hcc` against `HccDocuments` and prints dangling / orphan counts and the `plat.doc_meta main_database` note; a pair from different backup times is allowed (the documents backup may be older) but the report says how many refs are missing, and `migrate` refuses to run the move job while dangling refs exist (nothing to move in that case anyway).
- **Restore drill:** `Restore-HccDrill.ps1` restores the newest chain of **both** databases into scratch (`HccDrill_<stamp>` and `HccDrill_<stamp>Documents`), runs CHECKDB on both, the row-count check on both, the guest-ID check as today, and the ref check (`Invoke-HccDocumentRefCheck`: count of `object_ref` in the scratch main database not present in the scratch documents database → must be 0 for a chain taken in one run); result row `kind = 'RestoreDrill'` per database.
- **Installer / scripts:** `Initialize-HccDatabase.ps1` creates both databases (`-DocumentsDatabase HccDocuments`, same service login rights, same backup operators, same private backup folder); `Initialize-HccProperty.ps1` unchanged; `Hcc.iss` unchanged (it never creates databases); `Install-HccService.ps1` unchanged. The uninstaller never removes either database.
- **System health** (`GET /admin/system-health`, prototype page `syshealth`): new block `databases: [{name, kind: 'main'|'documents', schemaVersion, reachable, dataBytes, logBytes, limitBytes, pct, objectCount?, contentBytes?, danglingRefs?, orphanObjects?, lastBackup?, lastDrill?}]`, shown as a two-row "Databases" panel with a per-database size bar (amber at 70 %, red at 85 % of 50 GB) and the last backup per database. Sizes from `sys.database_files` inside each database (readable by the service login without extra rights). The prototype demo shows two fictional rows (`SYSState.databases`).

### 1.10 The prototype mirror (lane lf-docdb defines, lane lf-images consumes)

The prototype keeps images in IndexedDB. Today they sit in records (`Expense.billPhoto` data URL) and in `complianceFilesX`. LF gives them **one slice of their own**, mirroring the second database:

```ts
export type DOBKind='id-image'|'guest-photo'|'expense-bill'|'banking-slip'|'compliance-evidence'|'legacy-image';
export type DOBObject={ref:string;kind:DOBKind;owner:string;contentType:string;bytes:number;dataUrl:string;at:string;by:string};
/** State slice `s.docObjectsX` (schema 13): every image / file of the demo, keyed by ref; saved only when it changed (its own IndexedDB record). */
export type DOBState=Record<string,DOBObject>;
```

Records carry `objectRef?: string` (`Expense.billPhotoRef`, `MSGDoc.objectRef`, the slip's `objectRef`, `CMPEvidence.objectRef`); `restoreState` moves a v12 save's `billPhoto` data URLs and `complianceFilesX` entries into `docObjectsX` once (v12 → v13 migration in `storage.ts`); the old fields are deleted after the move. `dobSize(s)` returns bytes per kind for the demo System health. The mock service refuses a put above the kind's cap (§1.11) with the server's message.

### 1.11 G1 — on-device compression and the size caps (lane lf-images)

`src/app/slipPhoto.ts` already has the pure plan search (`slipCompressPlan`) and the browser adapter. LF generalises it, keeping the slip behaviour byte-for-byte (its tests stay green):

```ts
export type PhotoProfile={maxBytes:number;edges:number[];qualities:number[];label:string};
export const PHOTO_SLIP:PhotoProfile={maxBytes:102_399,edges:[1280,1024,800],qualities:[0.82,0.72,0.62,0.52,0.42,0.32],label:'deposit slip'};        // unchanged
export const PHOTO_DOC:PhotoProfile ={maxBytes:153_600,edges:[1600,1280,1024,800],qualities:[0.85,0.78,0.7,0.6,0.5,0.4],label:'document'};          // G1: ID images, guest photos, expense bills, evidence images
export function compressPhoto(file:Blob,profile:PhotoProfile):Promise<{dataUrl:string;bytes:number;width:number;height:number}>;
```

`compressSlipPhoto = f => compressPhoto(f, PHOTO_SLIP)`; `compressBillPhoto` switches from the SCC one-pass 1000 px / 0.7 to `PHOTO_DOC` (the SCC constants stay exported for the tests that pin them; the DONE file lists the SCC behaviour change — DEC-120's "SCC in full" covers the expense rules, not the pixel size, and the owner's G1 ruling asks for ~150 KB). ID capture and guest photo use `PHOTO_DOC` before upload; the OCR engine receives the compressed bytes (the simulated engine does not care; a real engine in W8 may ask for a larger image — note in `ETP_Reuse_Pending.md`).

Server caps, aligned (refused with the same wording on both sides, "Take the photo again, closer and in good light"):

| Kind | Today | LF |
|---|---|---|
| ID image (`DocumentRules.MaxImageBytes`, `doc.id_capture.size_bytes` CHECK 8 MB) | 8 MB | **153,600 bytes** (`DOC_MAX_BYTES`), JPEG / PNG / WebP |
| Guest photo (new, B5) | — | 153,600 |
| Expense bill (`ExpenseScc.PhotoMaxBytes`) | 4 MB | 153,600 |
| Banking slip (`CashOutRules.SlipMaxBytes`) | 102,399 | unchanged |
| Compliance evidence image | 5 MB | 153,600 for `image/*`; **PDF stays 5 MB** (challans are PDFs; **OQ-1**) |
| Legacy image (LG importer, server-side file read) | 8 MB | unchanged (105 files, 25 MB in all; never re-encoded) |

The CHECK constraints in `Hcc` are **not** lowered (old rows are larger); the application refuses. `dob.object.size_bytes` keeps 8 MB for the legacy kind.

## 2. A1 — Rate or guest-count change during a stay (lane lf-money-rate, Opus)

**Legacy** (`frmSalesBill` "Rate And Pax Change", F9): password panel, then old → new room charge, old → new extra charges, reason, "authorised by" name, and the scope *Update all days* / *Current & next day* / *Next day*; a row in `RateChangeDetail` (1,609 rows in FY 25-26: used daily) feeds the Rate Change Detail report.

**HCC rule.** Command `STAY_RATE_CHANGE {booking, rate?: paise, people?: number, children?: number, scope: 'next'|'today-next'|'all', reason, key}`:

- The stay is `In house`. At least one of `rate`, `people`, `children` differs from the booking. Capacity check as `BOOK` (`rmsCapacity`). `rate ≥ 0`.
- **Scope:** `next` = nights from the next business date on; `today-next` = tonight (the business date, not yet posted by the night audit) and later; `all` = every night of the stay **that is not on a closed business date** — a night already posted on a closed day is never changed here (DEC-077; use A2 after check-out, or the Owner reopens the day). A posted night of the current business date (the night audit already ran today) is re-posted: reversal line (`kind 'adjustment'`, `reversalOf`, dated the business date) + a new `room` line at the new rate with the GST slab of **that night's actual price** (DEC-069).
- **Authority** (the legacy password = HCC's limits and approvals): the Owner, and a Manager whose new rate is not more than the `PERM-RATE-001` limit (15 % of the Manager's policy) **below the tariff** for the room type on that date, apply the change directly; otherwise (Reception, or a Manager beyond the limit, or any raise of people where the extra-person charge changes) an approval `kind: 'rate-change'` is raised with `meta: {rate, people, children, scope, oldRate, oldPeople, oldChildren}`; a Manager approves within the same limit, the Owner approves anything. The approval executes the change (`APPROVAL` handler, folio stream section). A raise of the rate above the tariff needs no limit (it never loses money) but still an approval for Reception. No "authorised by" free text: the approver is the record.
- **Effect:** `Booking.rate`, `people`, `children` updated; the night audit posts the next nights at the new rate (it reads `b.rate` today — unchanged); meal-plan lines unchanged; occupancy charges (MA-1 master) for extra persons follow `people` from the effective night. A log row:

```ts
export type STRRateChange={id:string;booking:string;room:string;date:string;at:string;by:string;approvalId?:string;approvedBy?:string;
  scope:'next'|'today-next'|'all';reason:string;oldRate:number;newRate:number;oldPeople:number;newPeople:number;oldChildren:number;newChildren:number;
  effectiveFrom:string;reposted:{reversal:string;line:string;night:string}[]};
export type STRState={changes:STRRateChange[]};     // s.stayRateX
```

- Audit row `STAY_RATE_CHANGE` with old → new (no guest ID data). Phone: a sheet from the in-house stay card. Desktop: a modal from In-house and from the folio (sticky actions). The folio shows a "Rate changed · ₹old → ₹new from <date>" line in the header.
- **Server:** table `gst.stay_rate_change` (4625: the same columns, `scope CHECK`, `approval_id NULL`), command `ChangeStayRate` in `Hcc.Application/Stays`, approval kind `rate-change` in the approvals flow, outbox event `StayRateChanged` (no accounting entry: the room revenue posts with the night lines as today), endpoint `POST /stays/{id}/rate-change`, RBAC `PERM-STY-010 stay.rateChange.request` (Owner, Manager, Reception) with the Manager limit read from `PERM-RATE-001` (4626 seed).
- **Report data (W7 "Rate change detail"):** `stayRateX.changes` / `gst.stay_rate_change` joined to the stay.

## 3. B1 — A regular guest's agreed rate (lane lf-money-rate, Opus)

**Legacy** `GuestTariffDefineMaster`: per guest and room type, single / double / extra-person rates (AC and non-AC); `CustomerDiscountSetting` (836 rows): a per-booking discount type and value.

**HCC rule.** On the guest profile (Customers › Profile), **Owner-only** (money, DEC-058): `GUEST_RATE_SET {guestId, type, rate: paise per night, plan?: string, validTo?: date, note?}` and `GUEST_RATE_CLEAR {guestId, type}`:

```ts
/** Guest.agreedRates (optional): one per room type; sent to money roles only (DEC-MA-11e). */
agreedRates?:{type:string;rate:number;plan?:string;validTo?:string;note?:string;setBy:string;at:string}[];
```

- At `BOOK` / `CHECKIN` with a `guestId` (or a phone that resolves to one guest, `guestDuplicates` by phone) and an agreed rate for the booked room type valid on the arrival date, the booking form shows "Agreed rate ₹x (set by the Owner on d)" and the command accepts `rate: 'agreed'` → `Booking.rate = agreed`, `Booking.rateSource = 'agreed'`; otherwise the tariff. A Manager cannot undercut the tariff beyond the limit by choosing a guest rate: the agreed rate was set by the Owner, so it is allowed as is.
- Server: `gst.guest_agreed_rate(guest_id, room_type_id, rate_paise, plan_code, valid_to, note, set_by, set_utc)` (4627), `PERM-GST-010 guest.agreedRate (Owner)` (4628 seed), `GuestView.agreedRates` only for money roles.
- Report data (W7 "Guest tariff detail"): the table + stays that used `rateSource = 'agreed'`.

## 4. A2 — Correct a bill after check-out (lane lf-money-bill, Opus)

**Legacy** "Bill Updation": change a night's room rent / plan / hours-vs-time, add or delete an F&B or laundry line, delete a direct bill, with a `BillUpdationOLD` copy of the old bill for the Bill Updation report (683 rows in FY 25-26). The old software edited the bill in place.

**HCC rule — GST-safe form.** Posted invoices are immutable (`ivc.invoice` triggers; prototype `invoiceNo` never changes). "Correct this bill" = **one request** that, when the Owner approves, issues in one transaction: (a) a **credit note for the whole original invoice** (`CRN`, mode `full`), (b) the folio changes (reversal lines for removed / changed amounts, new lines for added charges — all dated the business date, DEC-077, GST at the slab of the night they belong to), (c) a **new tax invoice** (`INV`, next number, `revision = old + 1`) for the corrected folio, (d) a correction record. The original invoice stays, marked *superseded by* the new one on screen; both appear in Table 13 (B7).

```ts
export type BLCChange=
  |{kind:'room-rate';night:string;oldRate:number;newRate:number}            // one night's room rent (the legacy "Tarrif" column)
  |{kind:'remove-line';lineId:string}                                        // an F&B / service / extra line
  |{kind:'add-line';label:string;amount:number;gstCategory:string;date:string;extraId?:string};
export type BLCCorrection={id:string;booking:string;oldInvoiceNo:string;creditNoteNo?:string;newInvoiceNo?:string;changes:BLCChange[];
  reason:string;requestedBy:string;at:string;approvalId:string;approvedBy?:string;decidedAt?:string;status:'Pending'|'Issued'|'Rejected'|'Expired';
  oldTotal:number;newTotal:number;delta:number;settlement?:'refund-due'|'balance-due'|'none'};
export type BLCState={corrections:BLCCorrection[]};   // s.billCorrectionX
```

- Command `BILL_CORRECTION_REQUEST {booking, changes, reason, key}` — who may request = `rbacCanRequestCorrection` (Reception, Cashier, Accountant, Manager, Owner; DEC-063 G3); the stay is `Departed` with an `invoiceNo`; no other correction or credit-note request pending on it; every change valid (night within the stay; line exists, is not a reversal, not already reversed; amounts ≥ 0). The request computes `oldTotal`, `newTotal`, `delta` (preview shown before sending).
- **Approval:** kind `bill-correction` → `RBAC_OWNER_DECIDES` (Owner only). On approve, the `APPROVAL` handler (folio stream, lf-money-bill's own section) performs (a)–(d) atomically; stale if the folio version moved. Guard: the business date is **on or before 30 November of the FY following the invoice's FY** (s.34(2) CGST Act window for credit notes; message cites it; **CA to confirm**, OQ-2); refused after.
- **Money after the correction:** `delta < 0` → a refund is due: the refund flow stays as built (`REFUND_REQUEST` against the original receipt, `DEPARTED_REFUND_MSG` is satisfied because the credit note exists); `delta > 0` → a balance is due on the departed folio: `PAYMENT` on a departed stay is allowed for exactly that balance (a receipt kind `'payment'` with `reference 'Bill correction <id>'`) or B2 guest credit / company credit through the existing requests; `delta = 0` → nothing.
- **Accounting:** the credit note posts as a credit note posts today (its source rule, full reversal of the original invoice's revenue and tax); the new invoice posts as a folio sale on its date (the same rule the check-out invoice uses), so the ledgers carry the delta only; the correction id is the voucher narration. The lane writes the two sources with the existing kinds and tests the trial balance before = after + delta.
- **Server:** `ivc.invoice.revision INT NOT NULL DEFAULT 1` (4630; `UX_ivc_invoice_source` replaced by a unique index on `(property_id, source_kind, source_id, revision)`), `ivc.invoice_supersession(invoice_id, superseded_by_invoice_id, correction_id, utc)` (4631 — the invoice row itself stays immutable), `ivc.bill_correction` + `ivc.bill_correction_change` (4632), approval kind, command `RequestBillCorrection` / the approval executor in `Hcc.Application/Invoices`, endpoints `POST /invoices/{no}/corrections`, `GET /invoices/corrections` (list for the report), RBAC `PERM-FOL-010 folio.billCorrection.request` (PERM-FOL-005 holders) / approve = Owner (4633 seed), outbox `BillCorrected {old, creditNote, new}`. The invoice PDF / print shows "Supersedes HPE/2627/0012" on the new one and "Superseded by HPE/2627/0031 (credit note CRN/…)" on the old one.
- **Report data (W7 "Bill updation report"):** `billCorrectionX.corrections` / `ivc.bill_correction` with old and new totals per change.

## 5. B7 — GSTR-1 Table 13 with the legacy series; SAC summary (lane lf-money-bill, Opus)

- `gstr1Summary` (prototype) and `Gstr1Summary` (server) add Table-13 rows for the **legacy Lodge bill series** in FY 2026-27 (DEC-197): one row `docType 'LGC'`, label "Legacy Lodge bills (plain numbers, before cut-over)", `first`, `last`, `count`, `cancelled` = bills with `lgc.bill.status = 'Cancelled'` (or the legacy cancel flag the LG contract maps), from `lgc.bill` where `bill_date` is in the period (server) / from the legacy fixture lookup `customers-legacy` (prototype, `LegacyLookup` gains `bills(from,to)`). Rows appear only for periods that contain legacy bills (nothing after cut-over). The HCC series rows stay as today (INV now also counts counter bills, A7, and corrected invoices, A2 — the superseded invoice counts as issued, not cancelled).
- **SAC summary:** the server's `Gstr1SacRow` and the prototype's `sac` table become a **tab of the GSTR-1 sub-screen** ("HSN / SAC summary", Table 12 layout: SAC, description, UQC `OTH`, quantity = documents, taxable, CGST, SGST, IGST) with CSV export; legacy bills contribute **nothing** to Table 12 (their GST was reported by the CA from the old software; a note on the screen says so).
- Migration 4634: `lgc.bill` gains an index on `(property_id, bill_date)` if the LG migration did not add one (check first); 4635–4636 reserved.

## 6. A7 — Counter bill (lane lf-money-counter, Opus)

**Legacy** "Direct Bill" (F11): bill no, date, customer (guest name, optional room no), item lines (F&B / laundry items, qty × rate), GST %, add other, deduct other, remark, payment mode (cash / cheque with bank and cheque no / card), reference no; 22 bills in FY 25-26, 13 in 26-27.

**HCC rule.** New page **Cash & Billing › Counter bills** (`counter-bills`, FIT `split`: list + bill; sub-screens *Open / Issued / Credit*; phone page: list + sheet). Command `CTR_BILL_ISSUE`:

```ts
export type CTRLine={id:string;label:string;extraId?:string;menuItemId?:string;qty:number;unit:number;amount:number;gstCategory:string;taxBps:number;sac:string};
export type CTRBill={id:string;no:string;date:string;at:string;by:string;party:{name:string;phone?:string;gstin?:string;address?:string;guestId?:string;companyId?:string};
  lines:CTRLine[];discount?:{amount:number;reason:string};net:number;tax:number;cgst:number;sgst:number;total:number;remark?:string;
  settlement:{mode:'paid'|'udhaar'|'company';method?:string;reference?:string;receiptId?:string;udhaarId?:string;companyId?:string};
  status:'Issued'|'Credited';creditNoteNo?:string;creditNoteReason?:string;voidApprovalId?:string};
export type CTRState={bills:CTRBill[]};   // s.counterX
```

- `CTR_BILL_ISSUE {party, lines: {extraId?|menuItemId?|label, qty, unit?, gstCategory?}[], discount?, remark?, settlement: {mode, method?, reference?, companyId?}, key}` — permissions `billing` **and** `payments` (Reception, Cashier, Manager, Owner; the Restaurant role uses POS, not this). Lines come from the extras catalogue (`CAT_CHARGE`, price and GST class prefilled, editable as MA-2 allows) or the POS menu (price, outlet GST profile) or free text with a GST category of the GST master; unit price and amount in paise; GST per line at the dated master (`gstForCharge` by category on the bill date), CGST + SGST half away from zero per line; `discount` as a negative line at the folio-adjustment rate rule, within the DEC-063 role limits (Cashier none, Reception ≤ ₹250, Manager ≤ ₹1,000, Owner any; above = refused — a counter bill has no approval queue).
- **Invoice:** `no = fdmAllocate(s, 'INV')` (series `HPE/2627/…`, DEC-197) — one tax invoice at issue; B2B when `party.gstin` is given (validated as `grp` does). Server: `ivc.invoice.source_kind` gains `'counter'` (4637), the invoice lines carry each line's SAC (room 996311 only for a room-type line, else by category as `GSTR1_SAC`).
- **Settlement:** `paid` → a receipt in the cashier's open shift (`validMethod` rules: drawer needs an open shift, bank / UPI need a reference), cash guard `accCashEnforce` (ACC-2 §7); `udhaar` → an `ExpUdReceivable` (`kind: 'other'` or `'food'` by the dominant line class, `gstBps` of the bill) for the party (name + phone required) — rulebook A17 / DEC-144, Sales voucher Dr 1120/<customer>; `company` → `grpCreditRefusal` against the company account's limit, receivable Dr 1110/<company>. Accounting source `{kind: 'counter', id}`: Dr tender / 1120 / 1110 · Cr revenue by line class (`line:food` → 4200, `line:service` → 4100, a room-type line → 4000) · Cr 2110 / 2120.
- **Void:** `CTR_BILL_VOID_REQUEST {id, reason}` (same roles) → approval kind `credit-note` target the bill (Owner approves, DEC-063 G3) → a `CRN` credit note for the full bill, the receipt refunded through the refund flow or the receivable reversed (`EXP_UD_PAY_REVERSE` semantics / company payment reversal); status `Credited`.
- Print: the existing invoice print (hotel header, GSTIN, B6 notes); `printDocument` on Android.
- Server: `ctr.bill`, `ctr.bill_line` (4638), commands `IssueCounterBill`, `RequestCounterBillVoid` (`Hcc.Application/Counter`), endpoints `/counter-bills`, RBAC `PERM-CTR-001 counter.bill.issue` (Reception, Cashier, Manager, Owner), `PERM-CTR-002 counter.bill.void.request` (same) (4639 seed), outbox `CounterBillIssued`, `CounterBillCredited` → `SourceRules.Counter`, `CounterCredit`; 4640–4641 reserved.
- Report data (W7 "Direct bill detail", "Collection detail"): `counterX.bills` / `ctr.bill`.

## 7. B2 — Individual credit guests (lane lf-money-counter, Opus)

**Legacy:** guest opening balances (756 rows), Guest Outstanding / Bill-wise Outstanding / Guest Ledger reports; a guest could leave with a balance due.

**HCC rule.** HCC already has individual receivables: the **Udhaar register** (`ExpUdReceivable`, party ledger `1120/<customer>`, DEC-144) and company credit for stays. B2 joins them to the guest profile:

```ts
/** Guest.creditAccount (optional, Owner-only money field, DEC-058): the guest may take HCC credit up to `limit`; `customerKey` = the Udhaar party id. */
creditAccount?:{limit:number;active:boolean;customerKey:string;setBy:string;at:string;note?:string};
```

- `GUEST_CREDIT_ACCOUNT_SET {guestId, limit, active, note?}` (Owner; `PERM-GST-011 guest.creditAccount`, 4642 seed). The party ledger `1120/<customerKey>` is created by the ACC-1 parties rule the first time a receivable is raised (as today).
- **At check-out:** `GUEST_CREDIT_REQUEST {booking, reason}` (as `COMPANY_CREDIT_REQUEST`: `checkout` permission, in-house, balance > 0, the guest has an active credit account, `balance + open receivables ≤ limit` — refusal text mirrors `grpCreditRefusal`) → approval kind `guest-credit` (Manager within `FOLIO_MANAGER_CREDIT_LIMIT` / `PERM-PAY-005`, Owner beyond) → at `CHECKOUT` a receipt kind **`'guest-credit'`** for the balance and an `ExpUdReceivable {kind: 'room', gstBps, name, mobile, guestId, booking}`; accounting: Journal Dr 1120/<customer> · Cr 1100 (the mirror of `company-credit`). `Receipt.kind` gains `'guest-credit'`; server `pay.receipt` kind CHECK widened (4643).
- **Settlement:** the Udhaar desk settlement as today (`EXP_UD_PAY`, cash guard); the bill-wise list = open receivables per guest with the booking / counter bill / invoice reference, ageing 0–30 / 31–60 / 61–90 / 90+ (reuse `expVendorAgeing` shape); the **ledger** = the ACC-1 party statement of `1120/<customerKey>`.
- **Screens:** Customers › Profile gains a *Credit* section (limit, used, open bills, ledger link) for money roles; Cash & Billing › Cashier gains a sub-screen *Credit guests* (open bills by guest, ageing, settle). Counter bills on `udhaar` (A7) link to the same account when `party.guestId` is set.
- **Legacy opening balances:** not LF code — the cut-over evening enters unadjusted receivables as ACC-1 party opening balances (DEC-148 / DEC-164); the LG `cutover_receivables` file already lists them per guest; the Owner approves the opening entry. LF only makes sure a guest's `creditAccount.customerKey` equals the Udhaar party id the opening entry used (`customerKey` = `guestNormalisePhone(phone)` or the guest id when phone-less — one rule, both sides, written in `customers.ts` and `CustomerRules.cs`).
- Server: `gst.guest_credit_account` (4644), approval kind `guest-credit`, `GuestCreditRequest` command in Stays, `CheckOut` gains the `guest-credit` receipt + `UdhaarCreditRaised` event (existing rule A17), endpoints under `/guests/{id}/credit-account` and `/stays/{id}/guest-credit`.
- Report data (W7 "Guest ledger", "Guest outstanding", "Bill-wise outstanding"): party statements of 1120 + the receivable register joined to guests.

## 8. A3 / A4 / A5 / A6 — desk fields and rules (lane lf-desk, Sonnet)

### 8.1 A3 Purpose of visit

- New master **Purposes of visit** (`frontdeskMasters` list `purposes`, code + label + active; Owner / Tech admin, DEC-MA-1; CSV template `deployment/import-templates/purposes.csv`; the 36 legacy entries are extracted by `tools/legacy/extract.py --purposes` into that template layout from `PurposeOFVisitMaster` — names only, no guest data). Seed in the fixture: Business, Tourism, Family function, Medical, Education, Official, Transit, Other.
- `Booking.purpose?: string` (the master code). Optional at `BOOK`; **required at `CHECKIN`** when the Owner setting `frontdesk.purposeRequired` (Master configuration › Front desk, default **on**, non-money) is on — the legacy form required it. Shown on the In-house list filter, the registration card (GRC print) and the stay record. Server: `inv.reservation.purpose_code VARCHAR(20) NULL` + `mst.purpose_of_visit` (4645), `BookRequest` / `CheckInRequest` field, import template.
- Report data (W7 "Guest information", "Occupancy by purpose"): the field.

### 8.2 A4 Vehicle number and type

- `Booking.vehicle?: {no: string; type?: string}`; `no` upper-cased, 4–13 characters of `A–Z 0–9 - space`; optional at booking and check-in. Master **Vehicle types** (`vehicleTypes`: Car, Bike, Taxi, Bus, Other; legacy `VehicleMaster` names extracted like the purposes). Searchable: the In-house / Arrivals search box and Customers 360 search match a vehicle number. On the registration card.
- Server: `inv.reservation.vehicle_no VARCHAR(13) NULL`, `vehicle_type NVARCHAR(40) NULL`, `mst.vehicle_type` (4646); index `(property_id, vehicle_no)`.

### 8.3 A5 Form C — foreign guests

**Legacy** fields (`frmRoomBooking`): nationality, passport no / place of issue / issue date / expiry, visa no / place of issue / issue date / expiry, "Foreign Visitors Business", plus arrival in India and stay duration on the NRI (Foreigner) report.

- Per **stay** (not per profile — a guest arrives many times): `Booking.formC?`:

```ts
export type FormC={passportPlace:string;passportIssued:string;passportExpiry:string;      // the passport NUMBER is the guest's ID number (idType 'passport', DEC-068 store); never duplicated here
  visaNo:string;visaType:string;visaPlace:string;visaIssued:string;visaExpiry:string;
  arrivedFrom:string;arrivalInIndia:string;intendedStayDays:number;nextDestination:string;
  employedInIndia:boolean;employerOrBusiness?:string;addressAbroad:string;contactAbroad?:string;sex:'M'|'F'|'X'};
```

- Required at `CHECKIN` when the registered guest's `nationality` is not `Indian` and the Owner setting `frontdeskForm C required` (`frontdesk.formCRequired`, default **on**) is on; `idType` must be `passport`; `dob` required (LG added it). Dates ISO; expiry after the arrival date; `intendedStayDays ≥ 1`.
- **Visa number** is personal data: shown masked (last 4) to everyone except `PERM-DOC-004` holders (the ID-details rule, Owner / Manager / Reception), never in audit text, exports or messages (the free-text ID guard `guestLooksLikeIdNumber` applies); stored in clear in `gst.stay_form_c.visa_no` behind that permission (the ID-number sealing stays for the passport number only — OQ-3 asks whether the visa number should be sealed too).
- **Printable Form C sheet** (the Bureau of Immigration layout: hotel, guest, passport, visa, arrival, address, purpose, signature) via `printDocument`; **Foreigner register** sub-screen on Customers (or Arrivals): every foreign stay in a date range with the Form C fields, CSV export in a column order that matches the FRRO bulk-upload template as far as it is public (**OQ-4**: the exact portal file is W8 gl-formc's; LF ships the register and CSV).
- Server: `gst.stay_form_c` (4647: one row per stay, the columns above, `visa_no` NVARCHAR(30)), `CheckInRequest.FormC`, `GET /stays/form-c?from&to` (PERM-DOC-004), the print endpoint reuses the registration-card renderer; `frontdesk.formCRequired` and `frontdesk.purposeRequired` registered by the stays module's `SettingsProvider` (group Front desk).
- Report data (W7 "NRI / foreigner"): `gst.stay_form_c` joined to stays and guests.

### 8.4 A6 Check-out basis: 24-hour or fixed time; grace hours

**Legacy:** per booking `DaycloseType` = `Hours` (24 hours from the check-in time, `Hours` = 24 or as typed) or `Time` (the fixed check-out time); `CheckOutTimeHours` holds `LateHours` (late check-out grace, ≤ 24) and `CheckInHours` (early check-in grace, ≤ 24); "Half Day Charg" on the bill.

- Owner settings (Master configuration › Front desk › Stay timings, next to `checkin` / `checkout`): `frontdesk.defaultCheckoutBasis` `'fixed' | '24h'` (default **`fixed`**, HCC today), `frontdesk.lateCheckoutGraceHours` (0–24, default **1**), `frontdesk.earlyCheckinGraceHours` (0–24, default **1**), `frontdesk.halfDayPct` (0–100, default **50**, money → Owner-only; **CA / Owner to confirm**, OQ-5).
- `Booking.checkoutBasis?: 'fixed'|'24h'` (set at `BOOK` / `CHECKIN`, default from the setting; changeable while in house by `STAY_CHECKOUT_BASIS {booking, basis, reason}` — `checkin` permission); `Booking.checkedInAt?: string` (the clock at `CHECKIN` — set by `CHECKIN` from now on; absent on old bookings = `arrival + settings.checkin`).
- **Due-out moment** `stayDueOut(s, b)`: `fixed` → `departure` date at `settings.checkout`; `24h` → `checkedInAt + nights × 24 h` where nights = `nights(arrival, departure)`. Shown on the In-house list, the room board tile ("due 20:30") and the departures list; the night audit is unchanged (one room night per calendar business date — DEC-042's day model stays).
- **Late departure at `CHECKOUT`:** if the clock is after `dueOut + lateCheckoutGraceHours` the command refuses with "Guest is past the due-out time by h:mm — post the late check-out charge (half day) or extend the stay first" unless `cmd.lateCharge: 'half-day'|'full-night'|'waived'` is given: `half-day` posts a `room`-class line "Late check-out · half day" = `round(rate × halfDayPct / 100)` at that date's room GST slab (the night rate decides the slab, DEC-069); `full-night` = `EXTEND` by one night (existing command); `waived` needs the `approve` permission (Manager / Owner) and a reason, audited. Early check-in before `settings.checkin − earlyCheckinGraceHours` on a `fixed` stay shows a warning only (no charge — the legacy charged none; the Owner may add a catalogue extra).
- **Inventory for `24h` stays (owner ruling OQ-6, DEC-201):** a calendar night stays the inventory unit. A `24h` stay **does not hold the room for its departure date's night**. Until the guest checks out the room is occupied as usual (the board shows "due out 20:30"); once the guest checks out and housekeeping marks the room clean / inspected, the room is free to be sold again the same day. A same-day arrival may be **booked** on that room type as availability allows, but is **assigned** to this room only after it is vacated and cleaned (the existing room-status rule). No `registerRoomHold` for 24-hour stays.
- Server: `inv.reservation.checkout_basis CHAR(5) NOT NULL DEFAULT 'fixed'`, `gst.stay.checked_in_utc` exists; `StayRules.DueOut`, `CheckOutRequest.LateCharge`, settings registered (4648); the room hold for 24h stays in `AvailabilityRules` (4649 index if needed); 4650–4652 reserved for lf-desk.
- Report data (W7 "Room-wise allocation", "Occupancy average"): `checkout_basis`, due-out, late charges by label.

## 9. B3 / B4 / A8 — wake-up alerts, reminders, keyboard shortcuts (lane lf-alerts, Sonnet)

### 9.1 B3 Wake-up call alert

- `GSVRequest` of type `'Wake-up call'` gains `wakeAt?: string` (clock `YYYY-MM-DDTHH:mm`, today or tomorrow, must be after the clock — the legacy rule); `GSV_REQUEST_CREATE` validates it; the request's SLA clock starts at `wakeAt` instead of creation.
- **Desk alert:** `src/app/DeskAlerts.tsx` (mounted by `App.tsx` in both shells, under the top bar): every clock tick (the simulated clock advances by the demo controls / the server's business clock poll) it lists wake-ups with `wakeAt ≤ clock` not completed, and reminders due (§9.2); shows a persistent amber banner "Wake-up · Room 204 · 06:30 · Mr Rao" with **Done** (→ `GSV_REQUEST_COMPLETE`) and **Snooze 5 min** (`wakeAt + 5 min`, audited); a sound is **not** played (CSP / Android policy; a visual banner only — the legacy pop-up was visual too). Roles that see it: Front desk roles (Owner, Manager, Reception, Cashier) — housekeeping never (nothing about money, but the desk owns wake-ups).
- Server: `gsv.request.wake_at_utc DATETIME2(0) NULL` (4653); the client derives the alert from the synced requests (no push).

### 9.2 B4 Desk reminders (user-defined alerts, alert days)

**Legacy** `UserDefineAlert` (subject, date, description) + `AlertDaysDefine` (`NoOfDays` before the date the alert shows; room-booking limit and room-status refresh are not reminders and are skipped).

```ts
export type RMDReminder={id:string;subject:string;date:string;time?:string;leadDays:number;role:Role|'all';note?:string;repeat:'none'|'monthly'|'yearly';
  status:'open'|'done'|'dismissed';by:string;at:string;doneBy?:string;doneAt?:string;nextDate?:string};
export type RMDState={reminders:RMDReminder[];leadDaysDefault:number};   // s.remindersX
```

- Commands `RMD_SAVE {id?, subject, date, time?, leadDays?, role?, note?, repeat?}` (`checkin` or `expenses` or `approve` permission holders; a non-Owner / Manager may target only `'all'` or their own role), `RMD_DONE {id}` (a repeating reminder rolls `nextDate`), `RMD_DISMISS {id, reason}`; Owner setting `reminders.leadDaysDefault` (default **3**, non-money, group Front desk).
- Page **Front Desk › Reminders & wake-ups** (`desk-alerts`, FIT `split`, tabs *Today · Upcoming · Done · Wake-ups*; phone page with cards). The Dashboard's existing "attention" strip gets a count. The desk banner (§9.1) shows reminders whose `date − leadDays ≤ business date` for the signed-in role, with **Done** / **Later** (hides until the next day).
- Server: `plat.reminder` (4654), `ReminderCommands`, `/reminders` endpoints, RBAC `PERM-RMD-001 reminders.manage` (Owner, Manager, Reception, Accountant) (4655 seed); 4656 reserved.

### 9.3 A8 Function keys (desktop layout only)

**Legacy** (`frmSalesBill.Form_KeyDown`): F1 help, F2 calculator, F3 counter cashier, F4 edit check-in, F5 advance / deposit, F6 room status, F7 advance booking, F8 customer discount, F9 rate / pax change, F10 laundry balance, F11 direct bill.

- `src/app/shortcuts.ts`: a table `{key, label, page, modal?, permission}`; `App.tsx` desktop branch adds one `keydown` listener (the Ctrl+K one stays); a shortcut fires only when the user holds the permission, no modal is open and the focus is not in a text field that uses that key (none of these do); `preventDefault` so the browser's own F-key action does not run. Keys the browser cannot reliably yield are avoided: **F1, F5, F11, F12 are not used** (help, reload, full-screen, devtools).

| Key | Action (page / modal) | Legacy |
|---|---|---|
| F2 | New booking (Reservations › new) | F7 advance booking |
| F3 | Arrivals & check-in | F4 edit check-in |
| F4 | In-house guests | — |
| F6 | Room board | F6 |
| F7 | Folios (take a payment / deposit) | F5 deposit |
| F8 | Check-out (In-house › departures tab) | — |
| F9 | Rate / guest change (A1) on the selected stay | F9 |
| F10 | Cashier (counter cash) | F3 |
| Ctrl+F11 → **Alt+B** | Counter bill (A7) | F11 |
| Shift+F1 → **Alt+/** | Shortcut list (user menu › Keyboard shortcuts) | F1 |

**OQ-7:** the map above is the recommendation; the owner may re-assign. The user menu lists the shortcuts; `SearchPalette` shows the key beside the page; Android tablets with a keyboard get the same (desktop layout); phones nothing.

## 10. B5 / B6 — party photos and IDs; invoice notes (lane lf-images, Sonnet)

### 10.1 B5 One photo and ID per guest in the room

**Legacy** `frmMultipalePhotoAndIDProff`: several photos and ID proofs per booking.

- Captures bind to a **party member**: `doc.id_capture.party_seq TINYINT NULL` (NULL = the primary guest; 1–6 = `gst.guest_party.seq`) (4657); the capture list, the guest profile and Document capture show one card per party member with its own ID capture and **photo** (new `doc_kind 'photo'`, kind `GuestPhoto` in the object store, sealed like an ID image, PERM-DOC-004 to view, counted in the retention purge like captures). **Coordination (1 Nov 2026):** party-member IDs are already decided on `main` as **DEC-193** (one ID per guest, photo optional; Build Plan OP-20, built by the `claude/partyid` lane before / inside W3 ACC-3). When LF starts, lf-images first merges `main` and **reuses that implementation** (schema, sealing, capture per member); it adds only what DEC-193 lacks (the optional per-member photo in `HccDocuments`, compression) and never builds a second party-ID model. **Owner ruling OQ-8 (DEC-201): every party member's full ID number is stored**, exactly as the primary guest's (DEC-068 storage extended to party members): sealed / encrypted the same way, typed with the same validation, shown masked (last 4) to everyone except `PERM-DOC-004` holders, never in audit text, exports or messages (`guestLooksLikeIdNumber` guard), included in the retention purge and the guest-ID key re-key / drill check. Server: the party member's capture carries the sealed number like the primary's (`doc.id_capture` with `party_seq`), no new plain-text column. The registration card prints each member's ID type + last 4. Prototype: `MSGDoc.partySeq?: number`, `MSGDoc.kind` gains `'photo'`; `Guest.party[n].idType?`, `idLast4?` for display, the full number in the party member's capture like the primary's.
- Every image goes through `PHOTO_DOC` (§1.11) before upload and into the object store (§1.5).

### 10.2 B6 Notes and titles printed on invoices and receipts

**Legacy** `NoteMaster` (per room note shown on the bill), software settings `MainTitle` / `SubTitle`.

- Master configuration › Property and branding (non-money group, Owner and Tech admin, DEC-058): `brand.invoiceTitle` (≤ 60, default "Tax invoice"), `brand.invoiceSubtitle` (≤ 120, default ""), `brand.invoiceNotes` (≤ 600 characters, multi-line: terms, check-out time, bank details, "subject to Latur jurisdiction"), `brand.receiptFooter` (≤ 120), `brand.creditNoteNotes` (≤ 300); `brand.invoiceFooter` stays. Printed at print time (not stored on the document) on the folio invoice, counter bill, POS invoice, credit note and receipts in the prototype print templates and the server's PDF / print renderers (`Hcc.Infrastructure.SqlServer/Reporting` print models read `ConfigurationService`). Hindi labels stay a separate existing setting.
- Server: registered keys only (4658 none needed — settings are rows; reserve 4658–4660 for lf-images fallout).

## 11. State, schema, permissions, migrations, numbering

**Prototype `SCHEMA` 12 → 13** (coordinator, setup commit): new optional fields `Booking.purpose`, `vehicle`, `formC`, `checkoutBasis`, `checkedInAt`, `rateSource`; `Guest.agreedRates`, `creditAccount`, `party[].idType / idLast4`; `Receipt.kind` + `'guest-credit'`; `Expense.billPhotoRef` (replaces `billPhoto` after the v13 move); `MSGDoc.objectRef`, `partySeq`, kind `'photo'`; `GSVRequest.wakeAt`; slices `stayRateX?: STRState`, `billCorrectionX?: BLCState`, `counterX?: CTRState`, `remindersX?: RMDState`, `docObjectsX?: DOBState`; `SYSState.databases`. The coordinator declares every type above in the setup commit; lanes implement, never redeclare. Append-only blocks: `// LF` blocks in `handlers/index.ts` (`LF` object), `services/mock/index.ts`, `clients/sync/registry.ts`, `subscreens.ts`, `HccComposition.cs` (both the services and the endpoints block), `navmap.ts PAGE_CATEGORY` (`counter-bills: 'cash'`, `desk-alerts: 'front'`).

**Permissions (new codes; prototype equivalents in brackets):**

| code | meaning | roles |
|---|---|---|
| PERM-STY-010 | stay.rateChange.request (A1) [`checkin`; direct when Owner / Manager within PERM-RATE-001] | Owner, Manager, Reception |
| PERM-GST-010 | guest.agreedRate set / clear (B1) [`admin` + Owner] | Owner |
| PERM-FOL-010 | folio.billCorrection.request (A2) [`rbacCanRequestCorrection`]; approve = Owner | Reception, Cashier, Accountant, Manager, Owner |
| PERM-CTR-001 / 002 | counter.bill.issue / void.request (A7) [`billing` + `payments`] | Reception, Cashier, Manager, Owner |
| PERM-GST-011 | guest.creditAccount (B2) [`admin` + Owner] | Owner |
| PERM-RMD-001 | reminders.manage (B4) [`checkin` \| `expenses` \| `approve`] | Owner, Manager, Reception, Accountant |
| PERM-DOC-006 | documents.objects.admin (§1) [`admin`] | Owner, Tech admin |
| existing PERM-DOC-004 | Form C visa number, party photos (A5, B5) | Owner, Manager, Reception |

**Migrations (main stream; above the lock's highest 4605, so no out-of-order acceptance is needed):**

| Block | Lane |
|---|---|
| 4620–4624 | lf-docdb (object refs, backup_run database, PERM-DOC-006, move log, reserve) |
| 4625–4629 | lf-money-rate (stay_rate_change, seed, guest_agreed_rate, seed, reserve) |
| 4630–4636 | lf-money-bill (invoice revision, supersession, bill_correction, seed, lgc index, reserves) |
| 4637–4644 | lf-money-counter (counter source kind, ctr tables, seed, reserves, PERM-GST-011 seed, receipt kind, guest_credit_account) |
| 4645–4652 | lf-desk (purpose + master, vehicle + master, form_c, checkout basis + settings, reserves) |
| 4653–4656 | lf-alerts (wake_at, reminder, seed, reserve) |
| 4657–4660 | lf-images (party_seq + photo kind, reserves) |
| 4661–4668 | integrator |
| **4669** | reserved: drop the emptied content columns / tables — **W9, not LF** |
| **docs stream 0001–0019** | lf-docdb (`migrations-docs/`, own lock file) |

Every migration: `node "08 Production/tools/migrations-lock.mjs" --add` (`--docs --add` for the documents stream) and a `Hcc.slnx` line; RBAC seeds named `*_rbac_seed.sql`; never edit an applied one.

## 12. Tests, check, gate, "done"

- **Prototype:** one `tests/stream-lf-<lane>.mjs` per lane (`register({test,M,assert})`, names `[LF-<LANE>]`), every guard above as a test; the lf-tests lane adds scenario **`LF-PARITY`** (`src/fixtures/ext/lf-parity.ts`): a stay with a rate change (scope `all`, a re-posted night), a corrected bill (one night's rent lowered → credit note + revision-2 invoice, refund), a counter bill on Udhaar credit, a foreign guest with Form C, a `24h` stay checked out late with a half-day charge, a guest with an agreed rate, a credit guest at check-out, GSTR-1 for the month with the legacy row in Table 13 (from the legacy fixture); the ACC golden (`accounting-golden.json`) regenerated **once** by the integrator; `stream-fit.mjs` enforces `fit-page` on the new pages; `PAGE_CATEGORY` and `SUB_SCREENS` tests green; the phone smoke (`tests/check-phone-*.mjs`) covers the new phone pages; `npm test` counts recorded before / after in every DONE file.
- **Server:** tests in the project the lane touches (`Hcc.Documents.Tests` gains the two-database harness `DocumentsDatabases` — a `HccTest_*` pair, both migrated, dropped together; `Hcc.Platform.Tests/Migrations/MigrationLockTests` covers both lock files; `Hcc.Operations.Tests` covers the backup / drill script changes with `-WhatIf` runs; `Hcc.Invoices.Tests` the correction, revision uniqueness and Table 13; `Hcc.Stays.Tests` rate change, Form C, due-out, late charge, guest credit; `Hcc.Pos.Tests` or a new `Hcc.Counter.Tests` the counter bill; `Hcc.Integration.Tests/SystemHealthTests` the databases block). The move job is tested end to end: a `HccTest_*` database seeded with rows in all four content tables → `migrate` → zero remaining, every ref resolves, a second `migrate` moves nothing.
- **Check (coordinator, before the gate):** `node tools/fit-screen-check.mjs --pages counter-bills,desk-alerts,customers,cashier,inhouse,folios,syshealth` at the five FIT viewports × the roles that may open them; `tools/tablet-masters-check.mjs` on the two new masters; a Windows PowerShell dry run of `Initialize-HccDatabase.ps1 -WhatIf`, `Backup-HccDatabase.ps1 -WhatIf`, `Restore-HccDrill.ps1 -WhatIf` naming both databases.
- **Gate:** `tools\Start-Gate.ps1 -Repo E:\Projects\HCC\worktrees\lf -Wave lf` (both suites, both builds, both lock files, secret scan) — **plus** the real two-database restore drill on this PC against the gate commit's test databases (`Restore-HccDrill.ps1` on a `HccTest_*` pair with backups taken by the new script), whose log goes into the gate report.
- **Done** = every lane DONE file, `05 Delivery Evidence/Legacy/LF-DONE.md` with the before / after test counts, the gate PASS, the drill log, the owner's look at the five screens (rate change, correct this bill, counter bill, Form C, Reminders & wake-ups) on the Tab S7 screenshots, every OQ below answered or carried to the Decision Register as an open item, HANDOFF / CLAUDE.md / Build Plan ticks.

## 13. What LF leaves for the W7 report catalogue (§E of the gap list)

| Report | LF data |
|---|---|
| Rate change detail | `gst.stay_rate_change` (§2) |
| Bill updation report | `ivc.bill_correction` + changes, old / new invoice numbers (§4) |
| Direct bill detail, Collection detail | `ctr.bill`, `ctr.bill_line`, receipts kind by source (§6) |
| Guest ledger, Guest outstanding, Bill-wise outstanding | `1120/<customer>` statements, `exp.udhaar` receivables joined to `gst.guest_credit_account` (§7) |
| Guest tariff detail | `gst.guest_agreed_rate`, stays with `rate_source = 'agreed'` (§3) |
| NRI / foreigner | `gst.stay_form_c` (§8.3) |
| Guest information, Room-wise allocation detail, Occupancy average | `purpose_code`, `vehicle_no`, `checkout_basis`, due-out, late charges (§8) |
| Summary of documents issued (Table 13), SAC summary | `gstr1Summary` with the `LGC` row and the Table-12 export (§5) |
| Userwise check-in / check-out | already in the audit trail; nothing new |

## 14. Open questions for the owner (recommendation in bold)

| # | Question | Recommendation |
|---|---|---|
| OQ-1 | G1 caps: compliance evidence PDFs — compress images to 150 KB but keep PDFs at 5 MB? | **Owner, 1 Nov 2026: accepted.** **Yes** (challans are PDFs the portal issues; images 150 KB) |
| OQ-2 | A2: refuse a bill correction after 30 November of the following FY (s.34(2) credit-note window)? | **Owner, 1 Nov 2026: accepted.** **Yes, with the message naming the section; CA to confirm** |
| OQ-3 | A5: seal the visa number like the ID number, or keep it readable behind PERM-DOC-004? | **Owner, 1 Nov 2026: accepted.** **Readable behind PERM-DOC-004, masked elsewhere** (it is on the printed Form C anyway) |
| OQ-4 | A5: the FRRO portal's upload file layout — does the hotel upload Form C online today, and with which template? | **Owner, 1 Nov 2026: accepted.** **LF ships the register + a CSV in the public column order; W8 gl-formc matches the portal when the owner hands over a sample** |
| OQ-5 | A6: half-day charge = 50 % of the night rate at the night's GST slab? | **Owner, 1 Nov 2026: accepted.** **Yes, Owner-editable `frontdesk.halfDayPct`; CA to confirm the slab rule** |
| OQ-6 | A6: a 24-hour stay holds its room for the departure date's night when its due-out is after the fixed check-out time? | **Owner, 1 Nov 2026: No.** After the guest checks out the room is re-issued once housekeeping has cleaned it (DEC-201) |
| OQ-7 | A8: the key map in §9.3? | **Owner, 1 Nov 2026: accepted.** **As listed** (F1 / F5 / F11 / F12 avoided because browsers keep them) |
| OQ-8 | B5: party members get an ID **type + last 4 + image** only; the full number stays on the primary guest? | **Owner, 1 Nov 2026: No — keep the full ID number for every guest**, stored and protected like the primary's (DEC-201) |
| OQ-9 | §1.4: the emptied content tables are dropped by migration 4669 in W9 after the hotel-PC restore drill, not in LF? | **Owner, 1 Nov 2026: accepted.** **Yes** (keeps the fallback read path alive until the first real upgrade is verified) |
| OQ-10 | §1.9: documents backups on the same schedule as the main database (full daily, differential every N hours)? | **Owner, 1 Nov 2026: accepted.** **Yes, main first then documents in the same task** |
