# LF-int — wave F3 (LF) integration DONE (2 Nov 2026)

Branch `claude/lf` (setup f71e6fbb). Integrator lf-int (Opus).

## Merges (in order, `--no-ff`)
1. `claude/lf-docdb` — clean.
2. `claude/lf-money` — conflict only in `08 Production/database/migrations.lock.json`: kept ours, regenerated with `node tools/migrations-lock.mjs --add --accept-out-of-order` (LF ranges sit below KEYS 4720/4721). `Hcc.slnx` auto-merged (union).
3. `claude/lf-desk` — conflicts: `migrations.lock.json` (regenerated the same way) and `03 Prototype/src/features/guests/GuestProfile.tsx` (docdb had rewritten the file CRLF→LF, so the whole file conflicted): took desk's CRLF file (registration-card purpose / vehicle / due-out / Form C lines) and re-applied docdb's two lines (`PartyPhotos` import + render).
Lock: 170 main migrations + 2 documents migrations, both in step.

## Review: lf-docdb two-database write order (data-critical) — PASS, no change
- Every write (`SqlDocumentStore.PutAsync`, `SqlExpenseStore` bill photo, `SqlCashOutStore` slip, `SqlComplianceStore` evidence) calls `IDocumentObjectStore.PutAsync` first; that runs in its own committed transaction on HccDocuments (no MSDTC); only then the Hcc command transaction stores `object_ref`. A rolled-back Hcc transaction leaves an orphan that the sweep removes after 24 h (margin covers open transactions).
- Replaced objects are released through an outbox event in the same Hcc transaction (only after commit).
- Move job: object committed, then `UPDATE … SET object_ref=@ref, content=NULL WHERE key=@k AND object_ref IS NULL` in a per-batch Hcc transaction; idempotent re-runs; failure = orphans only.
- Reads: ref present → HccDocuments (missing → typed error); ref NULL → inline column (fallback until 4719).
- Note (not changed): `ReplaceAsync` (key re-seal) re-seals the object in place outside the Hcc transaction; a later Hcc rollback leaves the object under the new key, which stays readable while the key ring holds it. Acceptable; flag for the W9 drop of inline columns.

## Fixes made
- Money (LF-money open points): counter-bill cash guard (`CashGuardService.EnsureReceiptAsync`, party `bill`, as POS: ₹2,00,000+ cash refused CASH_LIMIT); counter-bill lines naming a catalogue extra (id or code) or POS menu item take price / name / GST class from the master on the business date when no unit price is given (given price kept; prototype parity); invoice print "Supersedes … / Superseded by … (credit note …)" via `IInvoiceSupersessionReader` (SqlBillCorrectionStore) in `InvoiceQueries` + `SimpleInvoiceRenderer`. Tests: `CounterPostingTests.Catalogue_price_when_none_given_and_the_cash_receipt_limit`, print-note asserts in `BillCorrectionTests`.
- FIT test: phone-file allow-list by name — `MobileShell.tsx` (F2 V17-4 / V15-4, intended, as F1 did for m-keys.css) plus LF feature additions to phone screens (`sheets.tsx`, `alerts/Mobile.tsx`, `frontdesk/Arrivals.tsx`, `admin/SystemHealth.tsx`).
- BASE / FY26 hashes re-pinned once (BASE 286e328b→f8788cff, FY26 history 8aed04aa→14d8a8f7, FY26 whole 5b118f02→415be48f): SCHEMA 13 + the compliance demo evidence moved into `docObjectsX` (LF-docdb). `accounting-golden.json` NOT regenerated: its tests passed unchanged (no posting on the golden scenario changed).
- Sync: `STAY_LATE_CHARGE` reloads `bookings` only (it raises no approval; the `approvals` reload broke the frontdesk-only sync runtime and aborted the test run).
- RBAC matrix test: `GUEST_PHOTO`, `GUEST_PHOTO_REMOVE` rows (OWN MGR REC).
- Server test fixes: Closing / Compliance / Configuration / Expenses harnesses create, migrate and drop the `…Documents` twin; seed-coverage regex accepts `''` in descriptions (PERM-GST-010/011 were seeded); import README sections for `purposes.csv` / `vehicle-types.csv`, HTTP master count 39→41; `_role-reads.json` regenerated (only new LF grants: CTR-001/002, DOC-006, FOL-010, GST-010/011 …).

## Counts
- Prototype: before 2388 → after **2463/2463**; `tsc` clean; `npm run build` and `build:production` OK.
- Server: `dotnet build` 0 warnings 0 errors; full suite `-maxcpucount:1` **3877 tests in 38 projects** (baseline 3733 in 37; new project Hcc.Documents.Tests 43, Counter 21). After fixes, failures = the 6 known Linux baselines only (GstIds DPAPI 1, Legacy absolute folder 1, Operations HostingTests 3, Stays RoomBoard page ceiling 1). The six fixed projects re-run green (Access 97, Closing 68, Compliance 36, Configuration 35, Expenses 67, Import 2038, Integration role reads). Log: `/tmp/claude-0/lf-server.log` (first full run, before the fixes).

## Remains for L4 (next lane)
- UI screens for the money features: stay-rate change (F9), agreed rate, bill correction, counter bills (Alt+B), guest credit; counter-bill print include of `PrintNotes`; fit checks / `tools/fit-modals.json` / `check-print-layout` on the new pages and print blocks.
- Party-photo sync + production wiring of the photo screen; `Documents.tsx` party cards; registration-card party lines.
- Reminders: `GET /reminders` read in `sync/reads.ts` + role-read matrix row, snooze, sync loader; `reminders.leadDaysDefault` as a setting.
- HTTP booking client: send purpose / vehicle / basis.
- Money sync areas (`clients/sync/registry.ts` LF blocks empty): STAY_RATE_CHANGE, GUEST_RATE_*, BILL_CORRECTION_REQUEST, CTR_BILL_*, GUEST_CREDIT_*.
- LG extractor: map the legacy cancel flag into `lgc.bill.cancelled` (4685).
- A1 re-post ignores a bundled meal plan in the slab.

## DEC proposals
- DEC-240: LF migrations below KEYS (4660–4707) accepted out of order into the lock (contract-assigned ranges).
- DEC-241: a counter-bill line naming a catalogue / menu item defaults to the master price on the business date; a typed price overrides (no approval).
- DEC-242: counter-bill cash is guarded per bill (party "bill", like POS), not per customer.
- DEC-243: phone-less guest credit — Udhaar party key = `UD-<customer key>` on both sides (owner to confirm; LF-money open point 3).
- DEC-244: in-place re-seal of HccDocuments objects during key re-keying is accepted (non-transactional across databases).

**Decision renumbering (ADM-int, 3 Nov 2026):** the approved Administration panel plan (APR-028) owns DEC-214…221, so this wave's proposals were renumbered.

| Old proposed | New |
|---|---|
| DEC-214 | DEC-240 |
| DEC-215 | DEC-241 |
| DEC-216 | DEC-242 |
| DEC-217 | DEC-243 |
| DEC-218 | DEC-244 |
