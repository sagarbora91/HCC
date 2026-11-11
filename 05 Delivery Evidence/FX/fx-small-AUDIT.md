# FX lane `small` - Opus audit (12 Nov 2026)

Branch `claude/fx-small`, head b4ce5b66, base 9c5abf01. Read: FX-CONTRACT §1 / §2 / §3.1, `fx-small-DONE.md`, `git diff 9c5abf01..claude/fx-small` (9 files), and the server and prototype code behind each claim.

## Verdict: FIX (two small items; everything else holds)

1. **`03 Prototype/src/clients/sync/hall.ts` line 117: the catch swallows every error, not only "no permission".**
   `c.api.get('/hall/templates').catch(()=>[])` turns any failure (network drop, 500, timeout) into "no templates". The case it was written for never happens. `GET /hall/templates` needs `View(PERM-HAL quotations)` (`HallQueries.TncTemplatesAsync`, HallCommands.cs:269). `GET /hall/snapshot` only returns quotations under that same check (`SnapshotAsync`, HallCommands.cs:297/301). So a role refused the templates also gets no quotations, and has nothing with terms to show or print.
   In practice the fallback only fires on a temporary error. The app then quietly shows the seeded "OWNER TO REPLACE" terms in place of the Owner's saved ones:
   - In the quote modal, Send is disabled and there is a placeholder warning, although the server would accept the send.
   - On the Owner's T&C page, the editor starts from the seed text. If the Owner edits and saves there, a new template version is written on top of the real wording.
   - Guest documents are not silently wrong: `prints.tsx:27` prints a placeholder warning. But the screen is misleading, and so is the editor.
   **What right looks like:** use the project's existing pattern (`clients/sync/cashier.ts:60`, `admin.ts:224`). Return `[]` only for `HccClientError` with `wireCode` `FORBIDDEN` or `NOT_FOUND`, and rethrow anything else so the loader fails and the slice is not replaced. Simpler still: skip the call when `r.seesMoney` is false, and let errors propagate. Then extend the `[HSYNC] FX-SMALL loader…` test so that a non-403 failure, such as `UNAVAILABLE`, rejects.

2. **`08 Production/src/Hcc.Application/Hall/HallStorePorts.cs` line 205 comment, and DONE "Defaults used" bullet 2: drafts are described wrongly.**
   The comment says the code and version are "null while a draft", and the DONE file says "the server only locks the version on SEND". In fact `InsertVersionRowAsync` (SqlHallQuoteStore.cs:186/209-211) writes `tnc_template_code` / `tnc_v` when a draft is created. Revise re-takes them, an event-type edit (`UpdateDraftAsync`, :355) re-takes them, and Send re-takes and locks them (:376). So a server draft carries the version taken on its business date. The client therefore uses the exact (code, v) branch for drafts too, which is correct.
   The "version in force on the created date" fallback only runs in two cases: rows from before migration 4332 with NULLs, and the empty-database case. In the empty-database case the server snapshot names `T-GEN` v1 (`HallQuoteRules.DefaultTemplate`), which is not in the template list.
   **What right looks like:** reword the comment to "the T&C version the snapshot was taken from (set on create / revise / send; null only on rows older than 4332)". Correct the DONE bullet to match. Make the test draft in `sync-hall.mjs` line 142 realistic: give it a code and version, as the server sends. Keep one NULL row to cover the fallback.

## Claims checked

### (a) Hall terms
- **Cause.** Confirmed. At base, `hallFromServer` returned `templates:[]` and gave every quotation version `tnc:{templateId:'',v:0,clauses:[],hasPlaceholder:false}`. The loader read only `/hall/snapshot`. `GET /hall/templates` already existed (HallEndpoints.cs:80).
- **Save path already correct.** Confirmed. `HALL_TNC_TEMPLATE_SAVE` sends `SaveTncTemplate` with eventType, clauses and effectiveFrom (hall.ts:142-143), matching `SaveTncTemplateRequest`. It reloads `hallX`, which now re-reads the templates.
- **SQL.** Correct. `v.tnc_template_code` (VARCHAR(20) NULL) and `v.tnc_v` (INT NULL) are columns 15 and 16, and both are read null-safely (`S(r,15)`, `IsDBNull(16)`). Old rows with NULLs map to `null`/`null`. On the client, `tncTemplateCode&&tncV` also treats the `("",0)` snapshot as unlocked and falls back. No migration.
- **Locked lookup.** Reconstructing clauses from the template at (code, v) gives the same text as the stored `hall.quotation_tnc_snapshot`. Template versions are append-only (4331 header: "never changes an older one"). The clause wire shape `{n,heading,text}` and `effectiveFrom` `yyyy-MM-dd` (SqlHallStore.cs:282) match `SrvTncTemplate`.
- **Draft / fallback date boundary.** `hallTemplateVersion` picks the latest `effectiveFrom <= date`, with ties broken by highest v. That is correct and the same as the server's `VersionInForce`. The fallback date is `(sentUtc??createdUtc).slice(0,10)`, a UTC date. Between 00:00 and 05:30 IST this is one day earlier than the business date the server used. After item 2, this only matters for pre-4332 or empty-database rows: note, not a fix.
- **Partial saves.** The first server save of a non-GENERAL type also materialises the `T-GEN` placeholder (HallQuoteCommands.cs:466-471). So the client's `hallTemplates` and `hallTemplateFor` (type, else GENERAL) resolve the same way as the server's `TemplateFor`.
- **Who prints.** The quotation print uses the version's `tnc` (`logic.ts:168`, `prints.tsx:27`). `LogPrint` QUOTATION needs `View(Quotations)`, the same permission as the templates read. See item 1 for the fallback.

### (b) D25-1
- `shortDate` (`components/ui.tsx:5`) appends `T12:00:00`. Before the fix, any string that was not exactly `YYYY-MM-DD` already printed "Invalid Date", including ISO date-times. Numbers and Date objects never reached `shortDate` (the `typeof v==='string'` guard is unchanged). So the new regex cannot make anything worse: an ISO date-time now prints as raw text where it used to print "Invalid Date".
- I swept every demo engine report at its default run (scratch script, not committed). Every date-column cell is `YYYY-MM-DD` or null, except the total labels. With the base formatter, six reports print "Invalid Date": daily-ops, cash-book, expenses-cash, receipts-deposits, displacement ("Total · 7 nights") and occupancy. The new formatter fixes all six.
- Server cells pass straight through (`reports-hub-run.ts:72`). `DateOnly` serialises as `yyyy-MM-dd`. A server report that put a `DateTime` in a date column would show raw ISO text, which is still no regression. I did not audit every server report for that.

### (c) Server cashier expected cash
- Confirmed in the code. All three paths use the same `MovementSql` over `pay.cash_movement` (SqlBillingStore.cs:462) and the same `CashDrawer.Expected`:
  - `GetShiftAsync` (BillingQueries.cs:44-46): `ReadShiftAsync`.
  - `GetCashDeskAsync` (:98-100): `ReadShiftAsync`.
  - `SubmitShiftCountAsync` (CashierService.cs:135): `CashDesk.ExpectedAsync`, at :239-242, reads `MovementsAsync` inside the command transaction.
  - The approval stale check (:265) uses the same `ExpectedAsync`.
- No production change was needed.
- **Are the tests a tautology? No.** Both tests compare all three figures against an expected value worked out by hand: 500,000 - 120,345 - 30,075 = 349,580, and 250,000 - 45,001 = 204,999. They also check that the cash vendor payment appears as a movement (-120,345), so it really goes through the drawer. The close command's recorded `ExpectedPaise` is compared with the same constant, and so is the "Closed with no difference" result. Being characterisation tests, they pass on the base by design, as the brief asks: "add the test that proves it".

### (d) Test quality, scope, rules
- **Prototype.** I ran each file on its own with a scratch runner: `sync-hall.mjs` 5/5, `stream-fx-small.mjs` 2/2. `npx tsc --noEmit` is clean.
- **Would they fail on the base?**
  - `[FXS]` test 1 and the sweep: yes, six reports print "Invalid Date" with the base formatter.
  - `[HSYNC]` terms and loader tests: yes. At base, templates are `[]` and `reads` lacks `/hall/templates`.
  - The Hall SQL test does not compile against the base view, which has no `TncTemplateCode`.
- **Server tests.** Not re-run by the auditor; the lane's DONE file reports 3/3 and 11/11.
- **Line endings.** `--stat` matches `--stat --ignore-cr-at-eol` (9 files, +149 / -11), and every file is LF in the index and the working tree.
- **Scope.** Only the brief's files changed. The viewer change is the date cell only. No report folders, migration, SCHEMA, APK, FIT-watched file or shared marked-block file was touched. The commit trailer is present and the branch is pushed (origin = b4ce5b66).
- **Small inaccuracy.** The DONE file's "63/63" comes from the npm name filter. The two files hold 7 tests between them, so the count is not reproducible as stated. Not a defect.
