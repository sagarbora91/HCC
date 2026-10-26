# Hotel Control Centre — Independent audit, 26 October 2026

**Status: FINAL (19:20 IST). Verified against `c0c477d`; `main` moved to `f25b519` (APK v12 fix jobs A and B, client code only) while the audit ran, see §8.**

Auditor: Claude (Fable 5.1) as lead, with 13 parallel Claude agents (2 Fable, 6 Opus, 5 Sonnet) and one blind re-verifier (Opus). Scope and method: `02 Planning\agents\Audit_Prompt_Fable.md` and `02 Planning\agents\Audit_Multi_Agent_Plan_2026-10-26.md`. Branch `claude/audit-fable-2026-10-26` (working tree at `c0c477d`; `main` is at `f0a5a29`, see §5). **No product code was changed.** Agent working papers: `agents\`; raw evidence: `evidence\`; probe scripts: `probes\`.

## 1. One page for the owner

**Go-live readiness: 60 %** (per-gate scores in §9). The software is further along than most two-day builds have any right to be, and the parts that compute money are right. What stands between it and a real hotel is a short list of missing flows, one install defect, and decisions only you and the CA can make.

**What works, with evidence.** Every number in the handoff reproduces: the server builds with no warnings, all 962 server tests and 1008 prototype tests pass, the web app inside the v12 APK is byte-for-byte the current build, and no secret is in the repository. Three auditors recomputed the money with their own arithmetic, independent of the app's code: room GST, CGST/SGST splits, the ₹4,265 folio, the ₹10,725 drawer, group billing, credit notes, invoice numbers, the trial balance, Tally vouchers and two months of payslips, all match to the paisa (29 of 34 billing figures, every cash-book and accounting figure, 24 of 24 payroll comparisons; the only mismatches are a stale test-data file, not the product). Security: 19 of the 22 review findings are fully fixed, two partly, none regressed, no new serious issue, no vulnerable packages. Guest IDs: across 17 hops from the check-in form to the backup, no path was found by which a full ID number reaches a log, the audit trail, an export, a message or a cache. Role access holds where it matters: housekeeping and maintenance never see money, the Tech admin cannot reach money accounts, PIN sign-in is bound to the device.

**What does not work yet (all reproduced by the lead; details in §2).**
1. **Backups fail on a fresh hotel PC.** The SQL scripts do not trust SQL Express's own certificate, so database set-up, every nightly backup, the restore drill and the pre-upgrade backup all fail until one flag is set. A one-line fix, but as shipped the hotel would run with no backups.
2. **A hotel cannot be set up without a developer.** Rooms, tariffs, tills and opening balances load only through a test script that the install guide never mentions.
3. **A guest leaving early cannot be checked out.** No screen sends the early-departure command, and the app refuses to shorten a stay to today.
4. **Cash cannot leave the drawer.** There is no command for banking cash or moving it between tills, so a banked day shows up as a cash shortage.
5. **Walk-in restaurant bills are outside GST.** They get no tax invoice and are missing from the GSTR-1 working summary.
6. **Some money never reaches the books.** Group payments and attrition, staff advances and full-and-final write-offs are not booked, so the ledgers and the Tally file are silently incomplete.
7. **Payroll: a leaver's last PF, ESIC and PT appear in no statutory file, and a missed attendance mark can never be corrected,** so it costs the worker pay with no fix path.
8. **The server slows down with age.** The background workers scan the whole event table every two seconds and nothing is ever purged; measured, this grows linearly and would keep the hotel PC's SQL busy within months. Small fix.

**Also worth knowing.** You cannot change a GST rate from the app today (the screen sends a command the server refuses for tax settings), so the CA's first rate change needs a developer. The CA question pack tells the CA that every rule is a setting; about a third of them are code constants, and the pack should be corrected before the meeting. There is no release-signed APK yet, only a throwaway-signed one. Exports on a shared tablet land in a public folder and survive sign-out.

**The five things to fix first** (about 45 hours): the backup trust flag and the missing .NET runtime line in the install guide; a supported masters-import script and an install step for it; the early-departure action; the cash-banking and till-transfer commands with their accounting; the event-table index and purge. The full plan, about 110 developer hours before the first real guest plus about 50 in the first month, is in §7.

**Decisions only you can make** (§6): whether to keep full Aadhaar numbers against advice until the lawyer answers, and for how long; how walk-in restaurant bills are to be invoiced for GST; who may bank cash and whether a second person confirms; whether the Manager may set company credit limits up to ₹10,000; who may correct past attendance; the shared-till "own shift" rule; the release keystore, hotel CA and key USBs; and the role-matrix v2 items.

## 2. Findings

Agents raised 195 findings (10 High, 49 Medium, 78 Low, 52 Info as graded by them; `evidence\lead\findings_all.tsv`). The lead reproduced every High, re-graded one (A05-01 to Medium), and merged duplicates. The blind re-verifier's verdicts are in `agents\V1_verification.md` (column "V1"). Low and Info findings stay in the agent files; their areas are summarised after the table. Hours are developer hours, estimated by the agent that found the issue.

**High (9)**

| ID | Area | File:line | Finding and impact | Fix | Hours | Lead | V1 |
|----|------|-----------|--------------------|-----|------:|------|----|
| A09-01 | Install / backups | `deployment\windows\Hcc-Common.ps1:52-56`, `Backup-HccDatabase.ps1:38`, `hcc-ops.sample.json`, `INSTALL.md` | SQL scripts pass `-C` (trust server certificate) only when a flag is set; the sample config and the install guide never set it, and SQL Express ships a self-signed certificate. On a fresh PC database set-up, every scheduled backup, the restore drill and the pre-upgrade backup fail. | Default the flag to true for local instances in the common defaults and the sample; document it. | 3 | Confirmed (also hit it live) | Confirmed by probe (`sqlcmd` rejects the certificate without `-C`) |
| A02b-01 | Install / masters | `INSTALL.md:84`; `import-templates\README.md:42-46`; `W4_E2E2\scripts\setup.mjs` | Rooms, room types, tariffs, tills, opening balances, staff, ledgers, outlets and menu load only through `UploadImportBatch`/`CommitImportBatch`, called by nothing but the end-to-end test helper. | Ship `Import-HccMasters.ps1` (sign-in, upload, row report, commit) and an INSTALL step; or a screen. | 8 / 14 | Confirmed | Confirmed (code) |
| A02b-02 | Front desk | `Hcc.Domain\Stays\StayChangeRules.cs:139-140`; `Hcc.Application\Billing\BillingService.cs:387-389`; no client sends `EarlyDeparture` | Checkout refuses until every booked night is posted; shorten-stay refuses today and tells the user to record an early departure, which no client can send. The guest must be billed for unstayed nights and then discounted. | Map an "Leaves today" action to `EarlyDeparture {stayId, expectedVersion, reason}`. | 7 | Confirmed | Confirmed (code) |
| A03b-01 | Cash book | `Hcc.Domain\Payments\CashDrawer.cs:19-20`; `Hcc.Application\Payments\CashierService.cs:133-147` | `TransferOut` and `Banking` movement kinds exist but no command writes them; the only cashier commands are open shift and submit count. Banked cash appears as a shortage and is booked to cash over/short. | `RecordCashBanking` and `TransferTillCash` commands (maker-checker, open-shift and day-lock aware) plus their accounting event. | 8 | Confirmed | Confirmed (code) |
| A03a-01 | GST documents | `Hcc.Application\Invoices\InvoiceQueries.cs:103-119`; `Hcc.Domain\Pos\PosRules.cs:51-53`; `pos\modals.tsx:94` | GSTR-1 is built from invoices and credit notes only; walk-in and paid-at-outlet POS bills store CGST/SGST (migration 1201) but produce no GST document and are absent from the summary. Daily revenue under-reported. | After owner/CA decision D3: issue outlet bills as GST documents (`ivc` source kind `pos`, SAC 996331) or feed `pos.bill` into the GSTR-1 B2CS rows. | 12–16 | Confirmed | Confirmed (code) |
| A03b-02 | Accounting | `Hcc.Application\Accounting\AccountingEventMapper.cs` (no `Group*` case); `Groups\GroupCommands.cs:343,417` | `GroupReceiptRecorded`, `GroupAttritionPosted` and `GroupSettled` are published but never booked: attrition revenue and its GST never reach the ledgers or Tally; company receivables are never cleared. | Consume both events (Dr tender / Cr 1110; Dr 1110 / Cr 4000 / Cr 2110 / Cr 2120). | 6 | Confirmed | Confirmed (code) |
| A04-01 | Payroll statutory | `Hcc.Application\Payroll\PayrollCalculator.cs:32-33,44`; `PayrollQueries.cs:148-157`; `Hcc.Domain\Payroll\PayrollFiles.cs:85-104`; `PayrollService.Pay.cs:344` | A staff member settled F&F with the exit-month salary is removed from that month's run; the PF/ESIC/PT statement is built from run lines only, so the leaver's deductions appear in no statutory file; `FnfSettled` has no consumer, so the settlement is not in the books. | Include settled F&F lines in the statutory statement; book an F&F voucher from `FnfSettled`. | 6 | Confirmed | Confirmed (code) |
| A04-02 | Payroll attendance | `Hcc.Application\Payroll\PayrollService.cs:372`; `PayrollService.Leave.cs:42`; `PayrollRules.cs:148,183,242` | Attendance can be marked only for the business date and leave cannot start in the past; an unmarked day is deducted for ever, the pay-run only warns. | Audited correction of past attendance in the open month (maker-checker if pay changes). | 6 | Confirmed | Confirmed (code) |
| A08-01 | Performance | `Hcc.Infrastructure.SqlServer\Outbox\OutboxConsumer.cs:51-58`; `Accounting\AccountingVoucherProjector.cs:61-63`; `0001_platform.sql:42`; `SqlRetentionPurge.cs` | Four projector workers scan `plat.outbox` by event type every 2 s; the only index is on unpublished rows, there is no per-consumer cursor and delivered rows are never purged. Measured on a throwaway DB: 1.3 s CPU per poll at 100k events, about 6 s at 365k, linear. | Per-consumer watermark or an index on (event_type, outbox_id); purge or archive delivered events. | 6 | Confirmed (structure) | Confirmed by own probe: ~18k reads and 0.8–2.2 s CPU per poll at 250k events |

**Medium (de-duplicated to 39 rows covering all 49 agent Mediums)**

| ID | Area | Finding | Fix | Hours | Lead |
|----|------|---------|-----|------:|------|
| A02-02 / A02b-03 | GST master | The Master-configuration screen sends `SetSetting`, which the server refuses for `gst.*`; `SetGstSetting`/`WithdrawGstSetting` have no client and the screen shows the app's seed rates, not the server's. The Owner cannot apply a CA rate change. | Map the GST editor to the two commands and load `GET /tax/gst`. | 8 | Confirmed by A2b code reading |
| A05-01 | Access / credit | The Manager can set any company's credit limit up to ₹10,000 directly (seed `2201_groups_rbac_seed.sql:22`, `CompanyAccounts.cs:77,121,172-183`); DEC-063's wording says company credit limits are Owner-only, though the G11 ruling itself was about the Tech admin. Limits are not enforced today (next row), so the exposure is nil until they are. | Owner ruling, then seed or DEC-063 text. | 2 | Re-graded from High |
| A03a-05 | Company credit | Credit limits are stored but never checked, and company credit is not linked to a company account; a receivable can exceed the limit. | Link folio/reservation to `grp.company_account`; compute exposure and refuse beyond the limit. | 6–8 | Confirmed by A3a |
| A03a-02 | GST "specified premises" | One room night above ₹7,500 that was posted by mistake and reversed still flips the hotel to specified premises for the whole next year (restaurant at 18 %); no Owner override. | Exclude reversed charges from the derivation; allow an Owner declaration. | 2 | |
| A03a-03 | IGST | A place of supply outside Maharashtra turns the whole invoice, room nights included, into IGST while the ledgers book CGST/SGST; reachable only through the API. | Refuse a foreign place of supply until CA G9 is answered. | 3 | |
| A03a-04 | Credit notes | A full credit note is refused after a partial one whose GST came to exactly half a paisa; a few paise can never be credited. | Credit the remaining taxable value exactly in full mode. | 2 | |
| A03a-06 | Group attrition GST | Attrition GST is tested on the retention price, so a ₹9,000 block at 50 % is taxed at 5 % instead of 18 % (CA G7). | After G7: take the rate from the contracted block rate. | 1 | |
| A03b-03 / A04-07 / A04-06 | Accounting / advances | Staff advances and F&F write-offs are never booked while recoveries are, so ledger 1200 goes negative; a negative F&F net still marks the advance recovered, so what the leaver owes is lost. | Consume `AdvanceGiven` and the F&F write-off; record only the covered part. | 3 + 3 | Confirmed (no consumer) |
| A03b-04 / A08-02 | Projectors | One failing event comes first every tick, failures are not counted and `last_error` is never written, so one bad event silently stops all later accounting (or HK, messaging, OTA) and blocks `LockAccountingPeriod`. | Record attempts and error outside the effect transaction; park after N failures and alert. | 4 | |
| A03b-05 | Tally export | No voucher id in the Tally XML, so a re-import creates duplicates; exports during projector lag carry no provisional flag. | REMOTEID per voucher; export only locked dates or show a provisional banner. | 3 | |
| A03b-06 | Shared till | "Own shift" means the person who opened it, and the client opens the next day's shift as whoever ran the close, so Reception cannot take cash next morning without a Manager (APK12-3). | Owner decision D9. | 3 | |
| A03b-08 | Cash book client | The production cash book is derived on the device from receipts; server-only movements (several tills, future banking lines) would be missing. Two sources of truth. | Map the server `CashBookRangeView` into the screen. | 4 | |
| A03b-09 | Day close | The server does not block day close on pending expenses, the prototype does. | Align the gate list. | 1 | |
| A04-03 / A11-12 | CA pack | The pack tells the CA "no software change is needed" for any rate or rule; daily wage × 26, 8-hour day, 15-minute grace, PF/ESIC/PT wage bases, F&F limits, encashment formula, B2CL threshold, SAC codes and the chart of accounts are code constants. | Reword the pack into "setting" vs "needs code" columns. | 1 (doc) | Confirmed by both agents |
| A04-04 | Daily wage | Daily-wage staff are not paid for weekly offs although the CA pack (P9) says weekly offs are paid; Imran would get ₹2,400 more in August. | Ask the CA explicitly; dated setting. | 2 | |
| A04-05 | Holidays | No paid-holiday calendar (SCC had one); staff lose pay on public holidays unless marked present. | Dated holiday calendar treated like weekly offs. | 6 | |
| A04-08 / A04-09 / A04-10 | Payroll rules pending CA | Paise rounding will not match ECR/ESIC figures (P6); overtime at 1× (P8); advance recovery can take net pay to ₹0 (P11). | CA answers; dated settings; code for the OT base and weekly-off rule. | 0.5 + 4 + 3 | |
| A04-11 | Payslip disclosure | Only "gratuity is not calculated" is shown; notice pay, bonus, TDS on salary and LWF are neither calculated nor mentioned. | One disclosure line on payslip and F&F. | 1 | |
| A06-01 | Access (F-M03 residual) | No "must change at next sign-in" after an admin reset, and `RevokeDevice` is still reachable by Tech admin. | `must_change` flag + sign-in response; move device revocation to Owner. | 8 | |
| A07-01 / A11-02 | ID-image retention | The 90-day rule is not enforced: the purge is a manual command (`DocumentService.cs:204` TODO) that no screen or job calls; a second, unused `doc.retentionDays` (365) setting misleads the Owner. | Nightly purge from `RetentionWorker`; delete the duplicate setting. | 3 + 5 | Confirmed |
| A07-02 | ID retention (CA L2) | Full ID numbers are never deleted; weakest point under DPDP "erase when the purpose is served". | Owner/CA fix the period; setting + purge. | 6 | |
| A07-03 / A09-07 | Key restore | The backup key can be restored on another PC by script; the guest-ID key cannot, and the restore drill never proves IDs open after a restore. | `New-HccGuestIdKey.ps1 -ImportOfflineCopy`; drill step. | 4 | Confirmed (no guest-ID step in the drill) |
| A08-03 | Inventory → OTA | Extend, shorten, early departure, room move, hold expiry and room blocks do not tell the OTA projector that availability changed (matters once a real channel manager is connected). | One `InventoryChanged` event from the allocator path. | 3 | |
| A03a-12 / A03b-07 / A08-04 | Golden test data | `base-fixture.golden.json`, the stays, messaging and customers goldens still hold pre-DEC-069 10 % figures; six harnesses pin a fixed 10 % rate; two golden exporters crash on the current prototype. Server-equals-prototype is proven only for accounting and payroll. | Fix the exporters and regenerate under the GST master. | 6 | Confirmed by three agents |
| A08-05 | Migrations | A migration added inside an old block runs after the later blocks on an existing database but in number order on a fresh install; only the fresh order is tested; no committed checksum list. | Commit `migrations.lock.json` with a test. | 4 | |
| A09-02 | Backups | The daily full (02:30) and the 4-hourly differential from 06:30 start at the same minute at 02:30. | Start differentials at 04:30. | 0.5 | Confirmed by arithmetic |
| A09-03 | Install | INSTALL.md never says to install the .NET 10 ASP.NET Core runtime; step 5 fails on a clean PC. | Publish self-contained or add the prerequisite. | 1 | |
| A09-04 | Upgrade | The installer has never been compiled, and after an upgrade it starts the service before `migrate` runs; the server has no schema-version guard. | Refuse to serve when the DB version is behind. | 4 | |
| A09-05 | Monitoring | When a projector, the purge or a backup fails the operator sees nothing. | Event Log source + failure entries. | 4 | |
| A09-06 | Offline | The encrypted read-only copy is wiped after about 30 minutes of server outage, leaving every device blank. | Owner decision on the trade-off (F-H01); downtime procedure. | 3 | |
| A09-08 | Simulated adapters | The fake OCR and fake messaging adapters stay registered in Production. | Register "not configured" adapters that refuse clearly. | 1 | |
| A09-09 | Import templates | No templates for stock items, departments, companies or suppliers. | Add four templates and importers. | 8 | |
| A09-18 | Performance | The room board costs about 10,500 page reads per call for 24 rooms (avg 76 ms, worst 2.4 s at 274 business days). | Index for current/next-stay lookups. | 3 | |
| A10-01 | APK exports | Exports are written to the public `Documents/HCC` folder (`native.ts:74-103`), readable by the next user and untouched by sign-out. | Write to the app cache and share from there. | 6 | Confirmed |
| A10-02 | APK release | The only `in.hcc.server` build is signed with a throwaway keystore and bundles a throwaway CA; no real keystore or hotel CA exists yet. | Owner creates the keystore and CA (D10); build and test on a device. | 3 + owner | Confirmed |
| A11-01 | Module switch-off (DEC-057) | `RequireHccModule` / `IModuleGate.EnsureOnAsync` are defined but attached to no route; switching a module off only hides screens. | Attach the gate to the nine switchable modules' command routes. | 6 | Confirmed (no call sites) |
| A11-06 | Install guide | INSTALL.md has no "load your masters" step. | Add §6d. | 1.5 | |
| A11-11 | Decision Register | Out of date; DEC-044…051 missing; 25 agent-chosen production rules have no decision id. | Update statuses; add DEC ids. | 3 | |
| A02b-04 / A02b-05 / A02-01 | Coverage | Group blocks can be created but not picked up, released or cancelled (trap); no self-service PIN/password change; the Assistant page is reachable with every action refused. | Refuse block creation (1 h) or finish the flow (17 h); `ChangeOwnCredential` sheet (4 h); list the AI commands as unmapped (2 h). | 7–23 | |

**Low and Info (130), by area.** Documentation drift (A1 6, A11 8): stale READMEs, CLAUDE.md, HANDOFF top block, Node path note, migration counts, v12 tally. Security hardening (A6 8): software wrapping key for tokens, PIN-wrapped offline cache brute-forceable once extracted, password sessions not device-bound, per-name sign-in delay, CSV formula text not neutralised on import, web-root path in the anonymous 404, `dpapi-machine` default scope, throttle message hidden in the APK. Data protection (A7 4): no `FLAG_SECURE` on the reveal screen, no Aadhaar guard on free-text fields, plaintext offline key copy, rekey skips document rows. Architecture (A8 9): 35 cross-module writes through ports (accept and write into the rules), POS SQL tests lack the `Category=Sql` tag, yield occupancy rounding differs in 8 cases, possible deadlock between opposite-direction room-type moves (not reproduced), the `MigrationRunnerTests` edit at merge as a recorded deviation from the frozen-file rule. Billing (A3a 6, A3b 7): charge-description heuristics ("Express breakfast" taxed as laundry), group GSTINs accepted without checksum, editable GST snapshot table, combined-rate-only invoice print, no credit-note time-limit check. Access (A5 4): seven report routes refuse the Cashier against the matrix, the Cashier can read the full trial balance, GSTIN and retention settings change without approval, Tech-admin request-only cells refuse instead of creating a request. APK (A10 6): `--verify` does not check the bundled CA or run `apksigner`, no pinning, no CSP in the bundle, minify off. Install (A9 9) and coverage (A2b 5): see the agent files.

## 3. Feature coverage: what the production app can do on the server

The production build keeps every prototype screen and sends each action to the server through a mapper. A2 enumerated this statically (script `probes\A02_commands.mjs`); A2b judged each gap against the business workflow.

| Measure | Count |
|---|---:|
| Prototype command types | 189 |
| Mapped to a server command | 164 |
| Refused with a written reason ("not available on the server yet") | 20 |
| Refused silently (no mapper, no listed reason): `AI_ASK`, `AI_CLEAR`, `AI_SETTINGS`, `GRP_SWEEP`, `SHIFT_CLOSE` | 5 |
| Mapper targets with no matching server route | 0 |
| Server command routes | 216 |
| Server commands no client ever sends | 43 |
| Desktop / phone screens | 38 / 36 |
| Screens with every action refused | 3 (`assistant`, `documents`, `backup`) |

**Verdict.** None of the 25 unmapped or silent prototype commands blocks go-live (8 have a workaround, 17 are not needed on day one). The gaps that matter are the other way round: server features with **no screen and no supported script**.

| Rank | Gap | Verdict | Hours |
|---:|---|---|---:|
| 1 | Masters import (rooms, room types, tariffs, tills, opening balances, staff, ledgers, POS outlets and menu): `UploadImportBatch` / `CommitImportBatch` are called only by the end-to-end test helper `W4_E2E2\scripts\setup.mjs`; `INSTALL.md` has no import step | **Blocks go-live**: a hotel cannot be set up without a developer | 2 (adopt and document the script) – 15 (screen) |
| 2 | Early departure: no client sends `EarlyDeparture`; checkout refuses until every booked night is posted (`BillingService.cs:387-389`) and "shorten stay" refuses today's date and tells the user to record an early departure (`StayChangeRules.cs:139-140`) | **Blocks go-live**: a guest leaving early must be billed for nights not stayed and then discounted | 7 |
| 3 | GST rates cannot be changed from the app: the Master-configuration screen sends `SetSetting`, which the server refuses for `gst.*`; the editing commands `SetGstSetting` / `WithdrawGstSetting` have no client, and the screen shows the app's seed rates, not the server's (`GstSettings.cs:14-21`, `ConfigurationService.cs:42-43`, `ConfigurationQueries.cs:149`) | Medium today; **blocks go-live the day the CA changes any rate** | 8 |
| 4 | Group blocks: creation is mapped but pickup, release, cancel and re-allot are not, so a created block holds rooms until cut-off | Trap; workaround = do not create blocks | 1 (refuse creation) – 17 (full) |
| 5 | No self-service password / PIN change (`ChangeOwnCredential` has no client) | Should have before go-live | 4 |
| 6 | POS menu and outlet maintenance has no screen; price changes need a re-import | Workaround once rank 1 exists | 6 |
| 7–12 | Assistant/Documents pages reachable but inert; `tariffs` slice never loaded (Settings, Yield); compliance calendar empty on a fresh install (add obligations one by one); stale `unmapped` bookkeeping; closed-date corrections (waits DEC-028); editable role templates (waits RBAC v2) | Not needed for go-live | 1.5–12 each |

Shift close is not a gap: on the server a shift ends through `SubmitShiftCount` plus the approval of any difference, and day close refuses open shifts.

## 4. Money, GST and payroll figures recomputed independently

Three Opus agents recomputed the figures with their own arithmetic (plain scripts in `probes\A03a_*`, `probes\A3b_*`, `probes\A04_*`, no HCC code imported) and compared them with what the server tests assert.

**Billing and GST (A3a): 29 of 34 figures match; the 5 mismatches are all one stale file.**

| Figure | Independent arithmetic | HCC | Verdict |
|---|---|---|---|
| BASE day collections | ₹21,500 | ₹21,500 | Match |
| BASE drawer | ₹5,000 float + ₹7,000 cash + POS ₹525 − ₹1,800 expense = **₹10,725** | ₹10,725 in `GstIds`, `Closing`, `DailyPack` tests; **₹10,750 in `base-fixture.golden.json`** | Match, except the stale golden file |
| Room 105 folio | 2 nights ₹2,000 @5 % + laundry ₹500 @18 % − ₹500 discount @5 % = net ₹4,000, GST ₹265, **₹4,265**; CGST = SGST = ₹132.50 | ₹4,265 in `GstRules`, `GstSql`, `InvoiceSql` tests; **₹4,400 in the golden file** | Match, except the stale golden file |
| Mixed folio (₹7,500.00 night, ₹7,500.01 night, restaurant, tea, laundry, discount) | per-line CGST/SGST, odd paisa to CGST, half away from zero | same, line by line | Match |
| Slab boundary | ₹7,500.00 → 5 %; ₹7,500.01 → 18 %; 21 Sep 2025 → 12 % | same | Match |
| Group GB-1 / GB-2 / CO-1 / CO-2 | ₹8,600 / ₹3,780 + ₹5,670 / ₹70,600 / ₹97,450; attrition ₹5,250 + ₹262.50 GST | same | Match |
| Credit notes | ₹105; ₹106.44 (largest-remainder split); full credit ₹4,158.56 | same | Match |
| Invoice numbers | `INV/2627/0000001` (16 chars), `CN/2627/00000001`, FY roll on 1 Apr | same | Match |
| Rounding sweep | 4.6 million amount × rate pairs, server vs prototype | 0 mismatches | Match |

**Cash book, day close, accounting, Tally (A3b): every figure matches.** BASE closing (receipts ₹21,500 by tender, expenses ₹1,800, drawer ₹10,725, difference ₹0); trial balances for 23 and 24 Oct balance to the paisa; the ₹16,20,000 opening trial balance balances with activity; receipt, refund and credit-note vouchers hit the right ledgers (2110/2120 Output CGST/SGST); odd-paisa split; Tally voucher `RCT/20261024/001` byte for byte; all 62 Tally ledger entries carry the right sign.

**Payroll (A4): 24 of 24 comparisons match**, field by field, against both the prototype golden data and the server kernel run in memory: Ravi (monthly) net ₹11,111.04 for August and ₹10,876.62 for September; Imran (daily wage) ₹14,614.75 and ₹14,467.00; Ravi's full-and-final ₹13,393.80 recovering the whole ₹2,000 advance; PF caps, ESIC period rule, Maharashtra PT slabs including February, overtime per minute, 14 edge cases.

**The one arithmetic defect is in the test data, not the product.** `08 Production\tests\Hcc.Platform.Tests\Golden\base-fixture.golden.json` and the two tests that read it (`GoldenFigureTests`, `GoldenBillingTests`) still hold the pre-DEC-069 10 % figures (₹10,750, ₹4,400). A8 found the same for the stays, messaging and customers goldens, that six test harnesses pin a fixed 10 % rate, and that two golden exporters crash on the current prototype. The module tests pass anyway because they assert the right figures locally, but the "server = prototype" cross-check that the golden files were meant to give has lapsed since the GST master was introduced.

## 5. Claims in the handoff that we could not confirm, or found wrong

Re-run by A1 (`evidence\A01\`): server build 0 warnings; **962 server tests in 28 projects pass serially** (15 min; the only first-pass failure, `DeploymentScriptTests`, is a PowerShell 7 module-path effect and passes with the machine `PSModulePath`); `tsc` clean; **1008 prototype tests pass**; both web builds self-contained with no demo controls in the production build; secret scan clean; v12 APK hashes match and a fresh web bundle is byte-identical to the one inside both v12 APKs; no `HccTest_*` database left behind.

| Claim | Where | What we found |
|---|---|---|
| "migrated 0 → 2702, 45 migrations" | APK v12 report, `W4_E2E2_Report.md` | 46 migration files; a fresh database applies 46. `Hcc.slnx` lists only 44 (cosmetic). |
| v12 server-phone tally "12 pass / 2 fail / 1 partial / 1 not completed" | APK v12 report, HANDOFF | The table has 17 rows and 13 passes (S16 is "pass with note"). |
| "E2E run 2: all D1–D7 fixed, payroll/accounting/B2B/refund/credit note/groups/OTA/stock pass" | HANDOFF 12b | `W4_E2E2_Report.md:56`: one area FAIL, five areas pass only with an API workaround, eleven new defects; no end-to-end run after the later N1–N3 fixes. |
| `main` moved to `c0c477d` with the v12 HANDOFF paragraph | briefing for this audit | At audit start `main` was `f0a5a29`; `c0c477d` had been committed by another session onto the audit branch 14 s after it was created (`git reflog`). By 19:00 `main` had moved to `f25b519` and contains `c0c477d`, so this resolved itself; the audit's verdicts are against `c0c477d`. |
| "ETP (`V:\ETP`) was not moved" | CLAUDE.md, HANDOFF, MIGRATION.md, `ETP_Reuse_Pending.md` | `E:\ETP\Code` exists on this PC (ETP source at git `8d65d83f`, 17 Sep 2026, plus worktrees); an ETP test session was using SQL Server during this audit. |
| CLAUDE.md "APK v12 = next build/test", "Node 24 first on PATH"; `08 Production\README.md` "not merged into main", migrations listed to 0601; `04 Android\README.md` SDK path and JBR version; HANDOFF top block still "Session 2" | docs | Stale (A1, A11). |
| APK12-2 cause "auth.ts hides the throttling message" | APK v12 report | By code the client shows whatever the server sends; cause unexplained (A10). |
| W3-7 "allocator release deadlock" noted in HANDOFF Session 12 | audit prompt | No such note exists in HANDOFF, commits or code; a deadlock between opposite-direction room-type moves is possible by code reading, not reproduced (A8). |
| Secret-scan "173+ commits ahead of origin" | HANDOFF | 271 commits ahead at audit time. |
| v7 APK hash; "86 GB free"; `scratchpad\art` junctions removed; the v12 throwaway keys deleted | HANDOFF | Historic states, not checkable now (current state consistent). |
| All on-device behaviour in the v12 report (session-expiry cache wipe, `adb run-as` on the release build, uninstall/backup behaviour) | APK v12 report | Needs an emulator; not re-run in this audit. |

## 6. Decisions only the owner or the CA can make, and the code waiting on each

The full register (26 owner items O-01…O-26 and all 39 CA questions mapped to code) is in `agents\A11_docs_vs_code.md` §c. The ones that gate go-live:

**Owner decisions**

| # | Decision | What waits on it |
|---|---|---|
| D1 | **Full Aadhaar numbers** (DEC-068, against advice): keep, or switch to last-4 only until the lawyer answers CA question L1 | `GuestIdRules.cs`, migration 0601; the README documents the switch-back path. See A7's legal note (`agents\A07_data_protection.md` §d): Aadhaar Act 2016 and UIDAI guidance say hotels should keep at most a masked copy; DPDP Act 2023 adds notice, consent, minimisation, retention and breach duties. |
| D2 | Retention: delete guest ID numbers and images N days after departure, and make the purge automatic | Purge is a manual command today (`DocumentService.cs:204` TODO); numbers are never deleted; two conflicting retention settings exist |
| D3 | Walk-in restaurant bills: GST tax invoices in the `INV` series, a separate POS series, or none (CA) | `InvoiceQueries.cs:103-119` (GSTR-1 reads invoices and credit notes only); `pos.bill` already stores CGST/SGST |
| D4 | Cash movements out of the drawer (bank deposit, owner drawing, till transfer): who may record them and whether a second person confirms | `CashDrawer.cs:19-20` defines the kinds; no command writes them |
| D5 | Group attrition rule and its GST basis (CA G7); whether group receipts, attrition, staff advances and F&F write-offs must reach the ledgers before go-live | `AccountingEventMapper.cs` has no cases for `Group*`, advance grants or `FnfSettled` |
| D6 | Past-attendance correction: who may fix a missed mark in an open month (maker-checker?) | `PayrollService.cs:372` fixes attendance to today; `PayrollService.Leave.cs:42` refuses back-dated leave |
| D7 | Company credit limits: may the Manager set limits up to ₹10,000 directly (as seeded, `2201_groups_rbac_seed.sql:22`) or Owner only (DEC-063 wording)? Note the limits are stored but never enforced today | `CompanyAccounts.cs:77,121,172-183` |
| D8 | DEC-028 closed-date corrections: adjustment-only (as built) or controlled reopen | `BillingService.cs:242,338`; `ClosingEndpoints.cs` |
| D9 | Shared till: "own shift" = the person who opened it; the client opens the next day's shift as whoever ran the close, so Reception cannot take cash next morning without a Manager (APK12-3) | `SqlShiftScopeResolver.cs:10` |
| D10 | Release keystore, hotel CA, guest-ID and backup keys on USB, BitLocker on the backup drive (HANDOFF 12b) | `build.gradle` (`HCC_RELEASE_STORE_FILE`), `New-HccCertificate.ps1`, `New-HccGuestIdKey.ps1`, `New-HccBackupKey.ps1` |
| D11 | Role matrix v2 D1–D7 (RST role, Cashier reports and trial balance, Tech-admin view-only cells) | seeds `0101`…`2701`; `capabilities.ts` |
| D12 | Push to GitHub (271 commits ahead) after a secret scan | process |

**CA questions that change code, not just a setting.** The CA pack (25 Oct) tells the CA that every rate and rule is a dated setting and "no software change is needed". That is true for the GST rates, the ₹7,500 slab, the PF/ESIC/PT rates and the due dates. It is **not** true for: the room-slab basis and discount treatment (G2, G10–G12), the "specified premises" derivation (G5), the no-show rate (G7, no rule), the B2CL ₹1 lakh threshold and SAC codes, the chart of accounts, the daily wage × 26 basis, the 8-hour day and 15-minute grace, the PF/ESIC/PT wage bases, the employer PF split (single 13 %), the F&F limits, the leave-encashment formula and overtime at 1× (P1, P2, P5, P8, P11, P13). Each of these is a code constant (`agents\A04_payroll_statutory.md` §2, `agents\A03a_billing_gst.md` §5). The CA pack should be corrected before the CA meeting so the CA's answers come back with the right expectation of effort.

## 7. Go-live plan

Hours are developer hours (Claude agents or a human), estimated by the agents and rounded by the lead. Owner and CA items run in parallel and gate the steps marked ◆.

**Step 0 — decisions this week (owner, CA, lawyer; no code).** D1 Aadhaar (and whether production starts with last-4 only), D2 retention period, D3 walk-in restaurant GST documents, D4 who banks cash, D5 group/advance accounting before go-live, D6 attendance correction rule, D7 Manager credit limits, D9 shared-till rule, D10 keystore/CA/keys, corrected CA pack to the CA.

**Step 1 — install and data safety (about 20 h).** Backup trust flag default and schedule fix (A09-01, A09-02: 3.5 h); .NET runtime prerequisite and schema-version guard (A09-03, A09-04: 5 h); supported masters-import script with an INSTALL step and the four missing templates (A02b-01, A11-06, A09-09: 17 h, or 2 h if the test helper is adopted as-is and the templates wait); guest-ID key import for a replacement PC and a drill step that opens one ID (A07-03: 4 h). Then a real install rehearsal on the hotel PC with the real CA and keystore (D10), and a restore drill.

**Step 2 — daily front-desk and cash flows (about 30 h).** Early departure (A02b-02: 7 h); cash banking and till transfer with accounting (A03b-01: 8 h); ◆ shared-till rule per D9 (A03b-06: 3 h); group block creation refused in production until pickup/release exist (A02b-04: 1 h); self-service PIN change (A02b-05: 4 h); exports to the app cache on tablets (A10-01: 6 h).

**Step 3 — books and tax (about 35 h).** Group receipts, attrition, advances and F&F into the ledgers (A03b-02, A03b-03, A04-07: 11 h); ◆ walk-in POS GST documents and GSTR-1 rows per D3 (A03a-01: 14 h); GST master editable from the app (A02-02: 8 h); specified-premises latch and half-paisa credit note (A03a-02, A03a-04: 4 h).

**Step 4 — payroll (about 14 h).** F&F lines in the statutory statement and the F&F voucher (A04-01: 6 h); ◆ past-attendance correction per D6 (A04-02: 6 h); disclosure line (A04-11: 1 h); holiday calendar if the owner wants it (A04-05: 6 h, optional).

**Step 5 — keep it healthy (about 15 h).** Outbox index or cursor and purge (A08-01: 6 h); poison-event handling with an operator-visible error (A03b-04: 4 h); Event Log entries for failed workers, purges and backups (A09-05: 4 h); room-board index (A09-18: 3 h); nightly ID-image purge and the duplicate setting removed (A07-01, A11-02: 8 h, needed only if ID images are used).

**Before the first real guest: steps 0–4, about 100–110 h, plus step 5's outbox fix (6 h).** First month: the rest of step 5, golden-data regeneration (A08-04: 6 h), the module gate on the server (A11-01: 6 h), the Decision Register and docs clean-up (A11-11, A1/A11 stale lines: 6 h), the role-matrix v2 rulings (D11) and the remaining CA-driven rule changes (A04-04, A04-08…10, A03a-03, A03a-06: about 15 h once answered). Later: ETP stand-ins (printing, PDF invoices, real messaging and OCR, channel manager), 13 of which A9 rates as first-month needs; ETP source is now on this PC and covers the PDF, Excel, Tally, backup and installer pieces.

**Verification after each step:** the full serial server suite and the prototype suite (both green today), the end-to-end script against a fresh database, and a repeat of the v12 emulator checklist on a release-signed APK with the real hotel CA.

## 8. Method and limits

- **What was re-run:** both builds and both full test suites (A1, serially, with an unrelated ETP test session sharing the SQL instance); single test projects and in-memory kernel probes by A3a, A4, A6, A7, A8; a throwaway 365,000-row outbox timing database (A8); a year-of-data performance seed (A9); the deployment dry-run script (A6).
- **What was only read:** everything on-device for the APK (no emulator was started); the Windows-service, certificate-store and Task-Scheduler steps (no admin rights; reviewed on paper and through the scripts' `-WhatIf` tests); the booking website behind a proxy.
- **Independence:** each area had its own agent writing only its own file; golden figures were recomputed from fixture values with the agents' own arithmetic; a blind re-verifier (V1) received the findings with the evidence stripped and had to reproduce them from the repository; the lead reproduced every High itself (`evidence\lead\notes.md`).
- **Lane discipline:** one `dotnet test` lane with a lock file. Two deviations: A7 ran the full prototype suite once by accident (`tests\run.mjs` ignores file arguments; it passed 1008/1008 again), and A9 held the SQL lock for about three hours for its seed instead of the planned 30 minutes.
- **Blind re-verification (V1):** all 74 High/Medium/sampled-Low findings reached: 63 confirmed, 6 partly (A10-04, A11-08, A09-05, A09-18, and A09-02 / A03a-06 cause-only), 1 not reproduced (A01-04, because `main` moved). V1 re-grades accepted by the lead: A05-01 to Medium (limits unenforced); A05-06 to Medium (a Tech admin can change the GSTIN without approval); A07-01, A11-02, A09-02, A11-11 and A04-11 to Low (A07-01/A11-02 latent until ID capture is wired in the client). A09-09 merged into A02b-01.
- **Environment facts that changed during the audit:** `main` was at `f0a5a29`, not `c0c477d`; a second Claude session worked in this checkout throughout (it committed `c0c477d` onto the audit branch, moved `main` to `f25b519` with APK v12 fix jobs A and B, left an uncommitted HANDOFF edit and a new planning file that are not part of the audit commit, and left two `HccTest_APK13_*` / `HccTest_WEB13_*` databases, untouched). Fix job A touches two findings here: the APK12-2 throttle-message note (A10) and the own-shift cash tender (A03b-06); re-check them on `main`. ETP source is present on this PC.
- **Not legal advice.** The data-protection note is an auditor's reading for the owner's decision; CA/lawyer question L1 is open.

## 9. Readiness gate scores

Each gate scored 0 / 0.5 / 1 by the lead from verified evidence; readiness = Σ(score × weight).

| Gate | Weight | Score | Why |
|---|---:|---:|---|
| Builds and tests reproduce HANDOFF (A1) | 10 | 1 | 0 warnings, 962 + 1008 pass, byte-identical bundle, clean secret scan, no leftover DBs |
| Daily front-desk and billing flows mapped to the server (A2/A2b) | 15 | 0.5 | 164/189 commands mapped and no mapper without a route, but early departure and masters import are unreachable |
| Money/GST golden figures match independent recomputation (A3a/A3b) | 15 | 0.5 | All arithmetic matches; the golden cross-check file is stale, walk-in POS bills are outside GST, and GST rates cannot be changed from the app |
| Payroll correct for the rules as configured (A4) | 10 | 0.5 | 24/24 recomputed payslips match; leaver statutory deductions and attendance correction missing; several "settings" are code constants |
| Access control matches matrix and rulings (A5 + lead) | 10 | 0.5 | Money hiding, Tech-admin limits and device binding hold; Manager credit-limit and Cashier-report cells need rulings; module switch-off not enforced server-side |
| Security review fixes hold; no new High (A6) | 10 | 1 | 19 fixed, 2 partly (F-M03 known, F-M07 software key), none regressed, no new High, no vulnerable packages |
| Guest-ID data cannot leak in normal operation (A7) | 10 | 1 | No plaintext path over 17 hops; sealing confirmed; retention and legal position are owner decisions, not leaks |
| Install, backup, restore, upgrade rehearsable from scripts (A9) | 10 | 0 | Backups fail as shipped; installer never compiled; runtime prerequisite and masters step missing; drill never opens an ID |
| APK production build hardened and evidenced (A10) | 5 | 0.5 | Release build type verified not debuggable / no backup / no cleartext; only a throwaway-signed APK exists |
| Docs and decisions consistent with code (A11) | 5 | 0.5 | 28 stale statements, some misleading for go-live (ETP location, E2E "all pass", CA pack promise) |
| **Readiness** | **100** | **60 %** | |
