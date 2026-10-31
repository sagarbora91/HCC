# LG-DONE — wave W1a LG (legacy "Lodge" data), integrator

Branch `claude/lg` / worktree `E:\Projects\HCC\worktrees\lg`. Base ba5b47ab. Integrator: Opus, 1 Nov 2026. Not pushed; gate and the real-data dry run not run (coordinator).

## Merged lanes (in order)
| Lane | Head | Notes |
|---|---|---|
| lg-schema | 25d1f8b5 | migrations 4600 / 4601, guest dob / legacyGuestId, Customers 360 legacy read path |
| lg-import | 40b31442 | importer (`legacy.import`, `GET /legacy/runs`), migration 4605, cut-over draft opening entry |
| lg-extract | 22feebc2 | `tools/legacy/extract.py` (+ LG-R11) |
| lg-ui | 5bb7fc39 | Customers 360 legacy rows, Owner page "Legacy import" |
| lg-audit | 55613399 | independent audit, `LG-Audit-Report.md`, 2 audit tests |
| lg-tests | 0d577b04 (**final**) | golden run, `stream-legacy-golden.mjs`, `Run-LegacyDryRun.ps1`, dry-run checklist |
| main (ACC-2) | 7dd260b4 | merged on the coordinator's instruction; conflicts kept both sides (types.ts, Hcc.slnx, migrations.lock.json 42xx + 46xx, RbacV2MatrixTests rows, m-accounting2.css). LG migrations 4600/4601/4605 unchanged; lock in step (127). |

## Integration items applied
- Prototype `guests.ts`: `dob` in `guestClean`, `guestValidate` (yyyy-mm-dd from 1900), `guestFieldLabels`; production guest mapper (`clients/sync/frontdesk.ts`) reads `dob` and sends it (omitted when the draft has no dob key).
- `RbacV2MatrixTests` LaterPermissions: `PERM-LGC-001` Owner only; `_role-reads.json` regenerated (Owner gains PERM-LGC-001, nothing else changed).
- Production composition (`clients/sync/runtime.ts`): `setLegacyFetcher(...)` over `GET /customers/profile?key=` + `legacyStaysFromProfile`, `setLegacyImportClient(createLegacyImportClient(transport))`.
- `navRoleOk`: "Legacy import" hidden from the Tech admin; RBAC nav document row updated to Owner only.
- `03 Prototype/tests/stream-legacy-extract.mjs` (loads the extractor self-test in `npm test`).
- lg-tests: csproj copy item `Golden\Fixtures\run-golden\*.csv` kept; `stream-legacy-golden.mjs` now asserts on lg-ui's real `LEGACY_SAMPLE` (exported from `services/mock` in a `// LG` block).
- `Run-LegacyDryRun.ps1` checked against the server: `/api/v1` prefix and `Authorization: Bearer` are both correct (the server accepts bearer or the session cookie; token from `POST /api/v1/auth/sign-in` with `returnToken: true`) — note added, no logic change. Not run.
- `.gitignore`: `tools/legacy/__pycache__/`. No `_contract-stubs` left on the branch.
- Line endings of the touched files kept as committed (LF).
- `E:\Projects\HCC\Existing Software\DB-PROFILE\README.md` §2 note corrected: GuestID is carried forward every year (auditor confirmed 0 lost), not restarted (outside the repo).

## Coordinator rulings applied by the integrator
- **LG-R6** (dob survives saves): server `GuestRules.Clean` keeps `Dob` null when omitted and `""` when blank; `SqlStaysStore.UpdateGuestAsync` writes `dob = CASE WHEN @dobKeep = 1 THEN dob ELSE @dob END`. Prototype `GUEST_SAVE` keeps the stored dob when the draft has no `dob` key and deletes it on an explicit empty value (`guestDobCleared`). Tests: `GuestDobSqlTests` (kept on omit, cleared on ""), `[LG-INT]` prototype test.
- **LG-R8** (imported drafts > 500 lines): cap stays on hand entry (`SaveOpeningBalances` / `ACC_OPENING_SAVE`); reading and approving never apply it. Opening screen (desktop + phone) pages the lines (100 per page) and shows an oversized imported draft read-only with an Approve-whole note. Tests: `Hcc.Legacy.Tests/Integrator/OpeningCapTests` (501 hand lines refused; importer cut-over draft + 600 lines approved whole), `[LG-INT]` prototype test (paging, 650-line draft approved).

## Tests (merged result incl. main, through the lock; output in `.scratch\integrator\`)
| Suite | Result |
|---|---|
| `npx tsc --noEmit` | 0 errors |
| `npm test` | **2095 / 2095** (2076 main baseline + 19 LG: 12 LG-UI, 4 LG-TESTS, 1 LG-EXTRACT, 2 LG-INT) |
| `npm run build` | OK (dist/index.html 3556 KB) |
| `dotnet build Hcc.slnx` | 0 warnings, 0 errors |
| `dotnet test Hcc.slnx -maxcpucount:1` | **3612 / 3612** (3572 main baseline + 36 Hcc.Legacy.Tests + 2 Customers + 2 Stays). First full run had 1 failure (role-reads fixture drift: PERM-LGC-001); fixture regenerated, Hcc.Integration.Tests re-run 19/19, npm test re-run 2095/2095. |

## Open items (owner / CA)
- Rate card (OP-18); CA ruling on invoice-number continuity (OP-19).
- The 185 "pending" bills carry BalanceDue 0 — receivable amount must be decided before cut-over posting.
- Room 211 has no room type (junk package) — owner to give its type.
- 21 bills in 26-27 store a BillDiscount not deducted from NetBill (bill numbers in LG-AUDIT-DONE.md); loaded as recorded.
- 397 duplicate-ID groups (`duplicates-review.csv`, ids only) for Owner merge review in Customers 360 after go-live; never auto-merged.
- Dry run on this PC (`Run-LegacyDryRun.ps1`) and the gate: coordinator.

## HANDOFF bullet (proposed)
- **W1a LG legacy data integrated on `claude/lg`** (lanes schema, import, extract, ui, audit, tests + main/ACC-2 merged): migrations 4600/4601/4605, `legacy.import` (Owner, PERM-LGC-001, dry run / commit, cut-over as a draft ACC-1 opening entry), Customers 360 legacy stays, Owner page "Legacy import", extractor `tools/legacy/extract.py`, auditor report.
- Rulings: dob kept when a save omits it (LG-R6); imported opening drafts may exceed 500 lines, paged and approved whole (LG-R8).
- Tests: prototype 2095/2095, server 3612/3612, 0 warnings. No SCHEMA bump.
- Next: gate, dry run on this PC with the ten MDBs, then owner items OP-18, OP-19, pending-bill receivables, room 211 type, 21 over-stated bills, duplicate-ID review after go-live.

## Decision Register entries proposed (DEC-170 onwards; DEC-165…169 reserved for ACC-2)
- **DEC-170 (LG-R1)** Legacy guests merge only on phone + folded name (or name prefix); same phone, other name = separate profile, same customer key.
- **DEC-171 (LG-R2)** A secret row is written for an image or DOB even without an ID number.
- **DEC-172 (LG-R3)** `lgc_guest_map` carries helper columns (name, phone, is_primary, dob) for matching; `guest_id` empty in the CSV.
- **DEC-173 (LG-R4)** Discount reconciliation compares the written discount column (DiscountAmt + BillDiscount); DiscountAmt-only is informational.
- **DEC-174 (LG-R5)** `dob` is returned only to readers who may see contact details (not the Cashier).
- **DEC-175 (LG-R6)** A guest save that omits `dob` leaves it unchanged; only an explicit empty value clears it.
- **DEC-176 (LG-R7)** The cut-over is written as a draft ACC-1 opening entry (receivables 1110/1120 parties, customer openings 1120, advances 2000); the Owner approves it in the opening screen.
- **DEC-177 (LG-R8)** The 500-line opening cap applies to hand entry only; imported drafts may exceed it, are paged read-only and approved whole.
- **DEC-178 (LG-R9)** Legacy ID captures follow DEC-071 like any capture.
- **DEC-179 (LG-R10)** After a committed guests part the whole secret folder (jsonl + images) is shredded.
- **DEC-180 (LG-R11)** Malformed ID numbers (4+ non-placeholder characters) are kept with their declared type and flagged; `duplicates-review.csv` lists ID-hash groups across different phones for post-go-live review, never auto-merged.
