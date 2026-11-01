# ACC3-DONE — wave W3 ACC-3 integration (1 Nov 2026)

Integrator: Opus · branch `claude/acc3` · worktree `E:\Projects\HCC\worktrees\acc3` · base 9775b8a7 · final head: see the commit that adds this file (`git log -1 claude/acc3`).
Not pushed, gate not run, main not fast-forwarded (coordinator's step).

## 1. Commits (first parent, oldest first)
- c79aa3a5 merge acc3-pl (7401d6b7)
- db04e5c4 merge acc3-ageing (26a6195e)
- ef89ecaa merge acc3-tally (6fa6ee7c)
- f22dc6b2 merge acc3-exports (1fbc8fa3)
- 3ba85486 merge acc3-carry (4e990121)
- 88a285fc merge acc3-ui (93ede77d)
- 13e527db merge partyid (f8b57e32)
- 209993d0 merge main **5bb15c09** (main had moved past 3ac89dba: 3ac89dba + the 13n docs, report catalogue, FIT/LF/RPT planning merges; documents only, no conflict)
- 4203e0d4 integration I (selectors, exports, Tally, settings, permissions, loader)
- a81c580f merge acc3-tests **ec322951 (final)**
- 919607ee integration II (B11, party IDs, chips, GST-on-advances, tests)
- final commit: TallyQueries idempotency fix, tab-strip CSS, smoke + this file

Conflicts resolved: `// ACC-3` blocks in `services/mock/index.ts` and `HccComposition.cs` (one block, every lane's line, acc3-exports' `MapAccountingExportEndpoints` moved inside it, acc3-tests' copies after the end marker tidied), `source-reuse.json` (H02, H04, ACC3-EXPORTS-OPENXML, ETP-TALLY, ETP), `Hcc.slnx` and `migrations.lock.json` (4266, 4270–4273, 4620 — all `--accept-out-of-order` entries kept), `AccountingReportContracts.cs` (acc3-pl's §2 records, then acc3-ageing's §3).

## 2. Integration items applied
| Item | Where |
|---|---|
| `acc3-views.ts` stub bodies deleted; real `accPl`, `accBalanceSheet`, `accCashRatio`, `accAgeing`, `accGstSummary`, `accTallyMasters` (FY start date → `2026`), `accTallyVouchers` re-exported from the mock barrel | `03 Prototype/src/features/accounting/acc3-views.ts` |
| acc3-ui's invented export names mapped to acc3-exports' builders (`accReportFile` / `accReportsZip` over `accPlTables`, `accBalanceSheetTable`, `accCashRatioTable`, `accAgeingTables`, `accGstSummaryTable`, `accTablesCsv/Xlsx`, `zipFiles`); `_acc3-stubs.ts` deleted, `accounting-exports.ts` typed on the real modules and exported from the barrel | `acc3-views.ts`, `reports3.tsx`, `accounting-exports.ts`, `services/mock/index.ts` |
| Real shapes in the UI: GST set-off is an object (payable / challan rows), cash ratio in basis points (shown ÷100) | `reports3.tsx`, `accounting-exports.ts` |
| Tally voucher download = `accTallyVouchers` (bill allocations); `accTallyXml` keeps the DEC-077 notes and the `ACC_TALLY_EXPORT` log | `features/accounting/Accounting.tsx` |
| Server `OpenXmlExporter` fed with the new views: `ReportExports` (P&L + Tally groups, balance sheet, cash ratio, ageing + bills, GST summary) and `GET /accounting/export/report?kind&from&to&asOf&month&fy&format=xlsx|csv` (PERM-ACC-005 + the report's PERM-ACC-001) | `Hcc.Infrastructure.SqlServer/Reporting/ReportExports.cs`, `AccountingExportEndpoints.cs` |
| Tally bill allocations read acc3-ageing's explicit vendor allocations first, then oldest-first (server `TallyBills(book, explicit)` + `TallyQueries` reads `IAgeingAllocationSource`; prototype `TallyVoucherIn.alloc` from `accAgeingPayableTargets`, explicit only, as the server — returns' own-bill target stays ACC3-R4). Golden April 2026 Tally files unchanged (no explicit allocation in the month). | `VoucherExport.Tally.cs`, `TallyQueries.cs`, `AccountingTallyEndpoints.cs`, `accounting-tally.ts`, `accounting-ageing.ts` |
| Setting `brand.legalName` "Legal name (Tally company)" (Owner approver, Master configuration › Property and branding) → SVCURRENTCOMPANY; empty = property name | `BuiltinSettings.cs`, `config.ts`, `TallyQueries.CompanyAsync`, `accTallyCompany` |
| SQL test for `acc.tally_file` / `SqlTallyStore` (masters + vouchers rows with SHA-256, RE-EXPORT note, legal name / property name) | `tests/Hcc.Accounting.Tests/Sql/TallyFileSqlTests.cs` |
| One opening loader `ReportOpeningLoader.AddOpeningAsync` shared by `AccountingReportQueries` and `PlQueries` | `AccountingReportQueries.cs`, `PlQueries.cs` |
| ACC3-R3: GST summary reads GSTR-1 via `InvoiceQueries.Gstr1UncheckedAsync` after its own PERM-ACC-001 check (no PERM-INV-001 needed) | `InvoiceQueries.cs`, `AccountingAgeingEndpoints.cs` |
| ACC3-R5 B11: cash legs totalling ₹20,000 or more on a voucher touching 2400 refused, less passes (lane refused every cash line); tests adjusted + added | `CashGuard.cs`, `ManualVoucherService.cs`, `accounting-guards.ts`, `accounting-manual.ts`, `CarryTests.cs`, `stream-accounting-carry.mjs`, `stream-accounting-acc3-int.mjs` |
| PERM-RPT-001 enforced: the daily pack `GET /reports/daily` now needs RPT-001 (Owner/Manager/Accountant) besides RPT-002; the Cashier keeps the own-shift collections. PERM-RPT-003: no server report-export endpoint exists outside accounting (those use PERM-ACC-005), nothing to enforce yet | `ReportingQueries.cs` |
| Prototype Reports page Print gated like its exports (`rbacCanExportReports`, RPT-003: not the Cashier); the print modal refuses for other roles | `features/reports/Reports.tsx` |
| partyid: production client maps party `idType` / `idLast4` from `GuestView.party` and sends `idType` / `idNumber` per member (masked values never sent back); `shared.ts` party type extended; per-member Show ID = `RevealGuestId {guestId, partySeq}` | `clients/sync/frontdesk.ts`, `frontdesk/shared.ts`, `guestid.ts`, `guestid-ui.tsx`, `features/guests/GuestId.tsx` |
| `<PartyIdChips/>` in the phone check-in sheet heading | `mobile/frontdesk/Arrivals.tsx` |
| Firm-profile GST-on-advances checkbox dropped: the profile shows the state (and from-date) and points to the dated "GST on advances" dialog; the save no longer sends `gstOnAdvances` | `features/compliance/modals.tsx`, `stream-accounting-ui2.mjs` |
| acc3-tests: final branch already enabled the reports tests (no `ACC3_REPORTS` constant left) and fixed the adapter names | — |

## 3. Permission audit (ACC3-R1 amended)
- View PERM-ACC-001: `PlQueries` (pl, balance-sheet, cash-ratio), `AgeingQueries`, `GstSummaryQueries` — checked.
- PERM-ACC-005 export: `/accounting/export/statements.zip`, new `/accounting/export/report` — checked.
- PERM-ACC-006 Tally: `/accounting/tally/masters`, `/accounting/tally/vouchers` — checked.
- PERM-ACC-010 assets: `SetAssetPutToUse`, `DisposeFixedAsset` (`AssetRules.Permission`) — checked. Re-allocation PERM-EXP-004 (lane, unchanged).

## 4. Pins and schema
- partyid's re-pin stands after all merges (no later seed change; prototype suite green): BASE `aa7464e7…a78` → `237e220f…be1`; FY26 history `794297a2…476` → `ba51a0ea…2e8` (and the FY26 whole-state hash in the 9 files, as partyid set them). Recomputed once by running the full suite on the merged head — no change needed.
- No SCHEMA bump (SCHEMA 12): every new field is optional.

## 5. Test counts
All through tools/Invoke-Serialized.ps1, output in .scratch/integrator/ (read in full, not filtered).
- Prototype: npx tsc --noEmit clean; npm test **2176 / 2176** (main 2095; lanes + [ACC3-INT] 2 new); npm run build OK (dist/index.html 3635 KB).
- Server: dotnet build Hcc.slnx **0 warnings, 0 errors**; dotnet test Hcc.slnx -maxcpucount:1 (.SQLEXPRESS) **3697 tests in 36 projects**: first full run 3695 passed, 2 failed — both the integrator's new TallyFileSqlTests, which found a lane defect: the masters export command returned a bare number as its idempotency result and plat.idempotency refuses non-object JSON (CK_idempotency_result), so every server masters export with the log composed failed. Fixed (TallyQueries returns TallyFileRef) and the test's column name corrected; Hcc.Accounting.Tests rerun **305 / 305**. Server total **3697 / 3697** (baseline 3612; Accounting 235 → 305, Stays 69 → 74, GstIds 51 → 56, Expenses 67).
- Fallout fixed: acc3-views had to import through the mock barrel (ACC1-UI source rule) → accounting-exports.ts exported from the barrel; acc3-exports' test fixtures moved to the real GST set-off and basis-point shapes; the ACC2-UI firm-profile test follows the dated GST-on-advances note; accounting tab strip sideways scroll (smoke).

## 6. UI smoke
See `05 Delivery Evidence/Accounting/ACC3-UI-Smoke.md`.

## 7. Open items
- **CA:** asset disposal treatment (rulebook F4: Cr block with the sale value, capital gain / loss flag, no auto 4960 / 5400); TDS per sub-section split (194-I(a)/(b), 194J(a)/(b)); B11 threshold ₹20,000 and the section references; cash-ratio definition (Receipt / Payment vouchers, cash = 1000 / 1005); full-Aadhaar storage (DEC-068) now covering party members.
- **Owner:** OP-21 — do children / infants need an ID (default no; one constant on each side); import the golden Tally files into a fresh TallyPrime company at the gate (`ACC3-TALLY-DONE.md` steps; set `brand.legalName` if the Tally company name differs from the property name); open the exported workbooks in desktop Excel.
- Carried to ACC-4 (ACC3-R4): server vendor returns allocate oldest-first; server GST tie does not compare a posted set-off journal; server ageing reads every day from the first source. The Tally explicit allocations likewise use payments only (not a return's own bill).
- Contract text to amend (coordinator): §2 1340 not netted (ACC3-R6), §2/§3/§4/§9 permission codes (ACC3-R1), §5 builder names as built.

## 8. HANDOFF bullet
- **W3 ACC-3 integrated on `claude/acc3` (1 Nov 2026):** P&L / balance sheet / cash ratio, outstanding with ageing, GST summary, Tally masters + vouchers (bill-wise, explicit vendor allocations first, `brand.legalName` = Tally company), CSV / Excel / zip exports on both sides (server `/accounting/export/report`), ACC-2 carry-overs C1–C11 (B11 at ₹20,000), DEC-193 party IDs end to end (production client, per-member Show ID, phone chips). Rulings ACC3-R1…R6 proposed as DEC-203…208.
- Tests: prototype 2176/2176, server 3697/3697, 0 warnings. Migrations 4266, 4270–4273, 4620; SCHEMA 12 unchanged; BASE hash re-pinned (partyid seed IDs).
- Next: coordinator gate (`tools\Start-Gate.ps1 -Wave acc3`), owner imports the golden Tally files and opens the workbooks, then main; CA items in `ACC3-DONE.md` §7.

## 9. Decision Register entries proposed (DEC-194…202 are taken on main — numbering starts at DEC-203)
- **DEC-203 (ACC3-R1, amended):** accounting report reads use PERM-ACC-001 (view); -002 lock, -003 unlock (Owner), -004 mapping, -005 voucher CSV / Excel export, -006 Tally XML (Owner + Accountant), -010 asset commands.
- **DEC-204 (ACC3-R2):** ageing rows carry optional `flagged`, `note`, `advance`; GST summary output figures are tax amounts with `outputTaxable`, plus `bookOutput`, `cashPaid`; due date = bill date until a credit-terms master exists.
- **DEC-205 (ACC3-R3):** the GST summary reads the GSTR-1 working summary under the accounting view permission (no invoice permission needed).
- **DEC-206 (ACC3-R4):** carried to ACC-4 — server vendor returns allocate oldest-first, the server GST tie does not compare a posted set-off journal, server ageing reads every day from the first source.
- **DEC-207 (ACC3-R5):** carry-overs — disposal per rulebook F4 with a CA flag; asset commands PERM-ACC-010; stale set-off blocks the month lock; a TDS remittance covers exactly its sub-section; Form 168 on `acc.tds_remit`; B11 refuses cash of ₹20,000 or more on a loan voucher (rulebook wins; CA to confirm).
- **DEC-208 (ACC3-R6):** balance sheet follows rulebook §8.5 — 1340 GST cash ledger is a separate current asset; fixed assets at WDV = the 15xx ledger balances; P&L `warnings`; cash-ratio definition to the CA; one shared opening loader. Also: Tally company = Master configuration `brand.legalName` (Owner), VCHKEY not written, log table `acc.tally_file`; daily report pack needs PERM-RPT-001; report Print = export roles.
