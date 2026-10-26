# A05 — Route → permission → role table

Agent A05 (Sonnet), independent audit of HCC, 26 Oct 2026. Branch `claude/audit-fable-2026-10-26`, source at `main` HEAD `c0c477d`. Read-only; no `dotnet test`, no full `npm test`. Node v25.9.0 (`E:\Tools\nodejs` first on PATH). All raw output is in `05 Delivery Evidence\Audit\evidence\A05\`; the scripts that produced it are in `05 Delivery Evidence\Audit\probes\A05_*.mjs` (run from the repo root, pure Node, no packages).

## 0. What was found, in plain words

- **380 routes**, not 373. The security review's table (373 rows, taken at commit `245a709`) is complete for that commit; 7 routes were added after it (section b). Every route is in a session-protected group except the 10 the review already lists. 6 session routes carry no PERM id by design (the review had 5; `/me/directory` is the new one).
- **The role seed is the matrix.** `0101_access_rbac_seed.sql` equals matrix v1 §5 in 404 of 405 cells; the one difference (Manager `PERM-ADM-005` = V where v1 says "status card, no page") is inert because no route checks that id. The prototype's own matrix (`admin.ts`) equals the seed in the same 404 cells. Because of this, "Match" against v1 for core routes is close to a statement about the seed; the useful signal is in the sweeps and the "borrowed id" modules.
- **One provisional hard mismatch (Matrix says no, code grants): A05-01.** The Manager can set a company credit limit directly (up to ₹10,000 per call: `SetCompanyLimit`, and the opening limit in `SaveCompany`) although DEC-063 G11 and the matrix note say a direct limit change is Owner-only, and the accepted prototype enforces that. It needs the lead's reading of G11.
- **One real "Matrix says yes, code refuses" cluster: A05-02.** The matrix gives the Cashier finance reports for the own shift (`RPT-002 V·S`); all 7 server routes behind `RPT-002` refuse the Cashier outright, while the Cashier can read the whole trial balance and ledger mapping (`PERM-ACC-001`, A05-03).
- **HK and MNT cannot reach hotel money.** Supervisor, Housekeeper and Maintenance hold zero money-flagged grants. Every money-like field in a response they can reach is either null for them or their own pay (DEC-061).
- **The Tech admin cannot reach money roles' credentials or money data**, with three small residuals (A05-06, A05-07, A05-08). DEC-057/058 are implemented in code, not only in the seed.
- Maker-checker is present on every approval path (20 request / decision pairs checked, with file:line). PIN sign-in needs a registered device of the same property and the PIN session is bound to that device on every request (DEC-062).
- The prototype side is consistent with the server: `reads.ts` agrees with the real server's recorded answers in all 702 role × route cells; the only coarse-permission difference is the documented Accountant "expenses".

## 1. Method and limits (read before trusting any cell)

1. `A05_routes.mjs` scans every `.cs` file under `08 Production\src\Hcc.Api` (blanking strings and comments), resolves `MapGroup` / `MapHccSecured` / `RequireHccSession` per variable, evaluates `"/commands/" + Const.Op…` from the code constants, and picks up the four helper styles (`Command<…>(g, "Name", …)`, `Cmd<…>`, Pos/Payroll/GuestServices/Billing private helpers). Helper *definitions* (5 `MapPost("/commands/" + name …)`) are excluded, which is why the 291 `MapGet`/`MapPost` calls give 286 literal routes + 94 helper-registered routes.
2. For each route it finds the Application/Infrastructure method(s) the handler calls (using the lambda parameter types, `GetRequiredService<T>()`, and local factory functions such as `Svc(sp)` for the type), then follows same-class helpers and static calls up to 3 levels to collect every `PERM-…` id, and records for each use the enclosing call (`RequireAsync`, `HasAsync`, …) and its scope argument (none / `AccessScopes.Any` / `AccessScopes.Property` / a record scope).
3. `A05_access.mjs` joins the ids with the 11 `*_rbac_seed.sql` migrations (82 permissions, 250 grants, 9 roles) and parses matrix v1 §5 and the v2 grids from the markdown. **A grant counts for a gate only if `AccessEvaluator` would pass it**: a VIEW-kind permission (or `:VIEW`) passes on A/L/R/V/M; an ACTION permission only on A/L/R; a grant with scope S/A/O passes a plain `RequireAsync` only if the code names a record or shift; `HasAsync` with no scope counts every scope.
4. Where a route has several ids the entry-gate semantics (`primary` / `and` / `or`) are my reading of the code and are listed in `A05_table.mjs` (`SEM`); the column "Needs judgement" repeats the basis. These are the least certain cells.
5. **Validation against the real server.** `03 Prototype\tests\_role-reads.json` holds the answers of the real .NET server per role for 78 GET routes (written by `RoleReadMatrixTests`; I did not re-run it). My static derivation agrees with it in 696 of 702 cells (6 disagreements, all on `GET /handover/templates` and `GET /configuration/requests` for roles I over- or under-grant; probe `A05_validate_reads.mjs`). For those 78 routes the roles in the table are the recorded real answers (marked †); for every other route they are the static derivation.
6. Roles in the table: `OWN MGR REC CSH ACC SUP HKP MNT TAD` (no RST; v2 D1 would add it). Notation: `(L)` limit, `(R)` request-only (refused with APPROVAL_REQUIRED unless the command itself raises a request), `(V)` view-only / masked view, `·S` own shift, `·A` assigned records, `·O` own submissions, `*` scoped grant that this gate refuses (the code does not pass a record scope), `†` taken from the recorded real-server answer.
7. Verdict: **Match / Matrix says no but code grants / Matrix says yes but code refuses / Not in matrix.** If matrix v1 (as revised by DEC-063) has a row that describes the action, the verdict is against v1. If not (borrowed ids, Wave 2/3 modules), the verdict is against the v2 *proposal* and is suffixed "(v2)": the proposal is not decided, so such a verdict is information, not a defect. `Not in matrix` = neither document describes the action.

Verdict tally over the 380 routes:

| Verdict | Routes |
|---|---|
{{TALLY}}

Against matrix v1 only (a v1 row describes the action): {{V1TALLY}} (`Not in matrix` here = no v1 row; 212 of those routes have a v2 grid row, section a).

## (a) Route table

{{ROUTE_TABLE}}

The same table with more columns (gate ids with scope/limit uses, `v2` ids, per-role cells as JSON) is in `evidence\A05\A05_route_table.csv` / `.json`; the raw enumeration is `A05_routes.csv`.

## (b) Reconciliation with the review's 373

| Quantity | Value | Where |
|---|---|---|
| `MapGet` / `MapPost` calls in `Hcc.Api\**` | 291 | `A05_mapcalls_per_file.json` (30 endpoint files + `HccComposition.cs` + `HccHosting.cs`) |
| of which helper definitions (`"/commands/" + name`) | 5 | Access, Billing, GuestServices, Payroll, Pos |
| literal routes | 286 | |
| routes registered through `Command<>` / `Cmd<>` helper calls | 94 | |
| **routes at HEAD** | **380** (158 GET, 222 POST; 216 of them `/commands/*`) | `A05_routes.csv` |
| review's table at `245a709` | 373 | `Security_Review_2026-10-26.md` lines 112–489 |
| difference | **+7, all added after the review**; no review row is missing from HEAD | `A05_review_diff.json` |
| anonymous | 10 (same list as the review) | |
| session routes | 370 = 363 + 7 | |
| session routes with a PERM id | 364 = 358 + 6 | |
| session-only | 6 = 5 + `/me/directory` | `/closing/business-date`, `/commands/ChangeOwnCredential`, `/configuration/modules`, `/configuration/values`, `/me/capabilities`, `/me/directory` |
| different PERM ids referenced by routes | 78 of 82 seeded | the 4 never referenced are in A05-09 |

The 7 added routes (found by `git log -S` on the endpoint files): {{ONLY_IN_A05}}. They arrived in `8ba06a1` (bank-files/latest, 09:22), `f9337e6` (cashier/folios, 10:19), `e2f8690` (me/directory, 11:43) and `5f0f885` (company accounts, 11:54), all 26 Oct and all after `245a709` (08:53). Two static page routes (`MapMethods "/"` and `"/index.html"` in `WebAppHosting.cs:90-91`) serve the web build and are not API routes; they are not counted, nor were they in the review.

Permission column of the review vs mine: they agree on 364 of 373 rows. The 9 that differ:

{{REVIEW_DIFF}}

The review's statement F-I02 is confirmed for HEAD. Its follow-up (a test that enumerates `EndpointDataSource` and fails on a new anonymous route, review checklist item 14) is not present: no test in `08 Production\tests` mentions `EndpointDataSource` (A05-13); `A05_routes.mjs` is a stand-in.

## (c) Mechanical sweeps

### c1. Money fields reachable by Supervisor, Housekeeper and Maintenance

Seed facts (`evidence\A05\A05_seed_grants.csv`): SUP holds HK-001, HK-002, HK-003, HK-004, ROOM-001, ROOM-002 and HR-004·O; HKP holds HK-001·A, HK-003·A, ROOM-001·A, HR-004·O; MNT holds HK-001·A, ROOM-001·A, HR-004·O. **None of these is a money-flagged permission** (`is_money = 1`); the same holds for TAD. Counts of money-flagged grants per role: OWN 41, MGR 31, ACC 20, REC 18, CSH 16, SUP 0, HKP 0, MNT 0, TAD 0.

76 routes are reachable by at least one of the three roles (`A05_sweep_hk_mnt_money.json`). I walked every response DTO of those routes (`A05_dto.mjs`, 1,319 record types indexed) for money-like field names. 17 routes have such fields; 14 of them are nullable fields that the code nulls for these roles, 3 are the roles' own pay.

{{C1_TABLE}}

Result: no hotel money reaches SUP/HKP/MNT. Two points for the lead: (i) the masking is by `money ? x : null` in each module (a negative: the roles are safe because they lack RPT-002/PAY-001), which is exactly the fragility v2 §1.3 describes; the guard that fails if a future grant breaks it does not exist yet. (ii) Own payslips carry employer cost fields (`CtcPaise`, `PfErPaise`, `EsicErPaise`); DEC-061 says "own pay and payslip", so I treat that as allowed but flag it as a question. SUP can list AMC contracts (`GET /maintenance/amcs`, HK-004) without values; G12 reads "AMC contracts: Owner, Manager", v2 MNT-005 gives ACC a view and not SUP (judgement in the table).

### c2. Tech admin (TAD) reach into money roles and money data (DEC-057/058)

TAD grants: ADM-001 A, ADM-002 R, ADM-003 V, ADM-005 A, HR-001 V, HR-004 A·O, IMP-001 A. 43 routes are reachable (including the session-only reads and the HR-004 self-service routes); none of them is a money route. The routes and the guards:

| Route group | Gate | What stops money reach | Evidence | Result |
|---|---|---|---|---|
| `GET /admin/users`, `/admin/devices`, `/admin/roles`, `/admin/exceptions`; `CreateUser`, `AssignRole`, `RevokeRole`, `SetUserEnabled`, `SetCredential`, `RegisterDevice`, `RevokeDevice` | ADM-001 A | roles flagged `owner_only_assign` (OWN MGR REC CSH ACC TAD) can be assigned or removed only by an Owner; resetting the credential of, enabling or disabling any account that holds such a role needs an Owner (F-M03 fix); any change to an Owner account needs an Owner; no self-change (SOD-002); last Owner protected (SOD-003) | `SqlUserAdministration.cs:600-609`, `:611-616`, `:624-637`, `:539-542`, `:641-652`; seed `0100`/`0101` `plat.role` rows | TAD can administer SUP, HKP and MNT accounts and devices only. Residual: A05-07 |
| `GrantException`, `RevokeException` | ADM-002 R | an R cell fails `RequireAsync` with APPROVAL_REQUIRED; nothing else is written | `SqlUserAdministration.cs:249`, `:303`; `SqlAccessPolicy.cs` `RequireAsync` | TAD cannot grant or revoke. No request record is created either (A05-08) |
| Master configuration: `SetSetting`, `RevertSetting`, `WithdrawScheduledSetting`, `ImportConfiguration`, `GET /configuration/*`, `POST /configuration/import-plan`, `GET /tax/gst` | ADM-003 `:VIEW` (V) | `ConfigAuthority.Technical`: money groups (rooms, money and tax, POS, people, compliance) and any setting flagged `Money` are refused; a setting that names an approver becomes a request; import skips refused and request-mode items | `ConfigurationPorts.cs:215-229`, `SettingRules.cs:203-230`, `ConfigurationService.cs:208-212`, `:347-349`, `ConfigCatalog.cs:50-61` | DEC-058 implemented. Residual: A05-05 (Manager decides one request), A05-06 (GSTIN and ID retention are non-money settings) |
| Tax: `SetGstSetting`, `WithdrawGstSetting` | ADM-003 A | a V cell does not pass an ACTION permission | `AccessModel.cs` (`Evaluate`: V/M only for `:VIEW`) | OWN only. GST master unreachable for TAD |
| Import: `UploadImportBatch`, `GET /import/*` | IMP-001 A | commit needs IMP-002 (rooms, guest profiles) or IMP-003 (money, rates, roles, accounts, opening balances), both OWN only | `2701_import_rbac_seed.sql`; `CommitImportBatch` uses `permsDirect IMP-001`, `permsVia IMP-002/003` | upload, validate, preview and dry-run only (DEC-057/058) |
| Payroll: `GET /payroll/staff`, `/payroll/board`, leave balance | HR-001 V (+HR-004 self) | staff list shows no pay; board returns all staff roster/attendance/leave but only the caller's own lines of LOCKED runs; rates, runs, advances, F&F only for HR-006 | `PayrollQueries.cs:25-43`, `:218-260` | no hotel money; staff roster and leave of everyone is visible (by design, `staff.list` field mask) |
| Not reachable by TAD | — | ledger mapping (ACC-004 OWN), company credit limits (GRP-002/003), payroll run/rates (HR-006/009), audit (ADM-004), guest ID reveal (GST-003), re-key (ADM-003 A), ID purge (DOC-002), invoices, shifts, folios | route table | G11 and the matrix note hold |

### c3. Maker-checker: can the same principal do both steps?

Every request / decision pair on the server compares the principals. None was found without a check.

| Pair | Rule | Evidence |
|---|---|---|
| RequestAdjustment → `DecideApproval` (discount, refund, correction, company credit) | decider ≠ requester (SOD-001) | `BillingService.cs:282` |
| Cash difference approval (SHF-002) | approver ≠ the person who counted (SOD-004) | `CashierService.cs:200` |
| Refund request → `ExecuteAdjustment` | executor ≠ requester (DEC-031) | `BillingService.cs:422` |
| RequestCreditNote → `DecideCreditNote` (Owner) | decider ≠ requester | `InvoiceService.cs:184` |
| SubmitExpense → `DecideExpense` | approver ≠ submitter | `ExpenseRules.cs:134-139` |
| RequestGroupAttrition → `DecideGroupAttrition` | decider ≠ requester | `GroupCommands.cs:392` |
| Company limit request → `DecideCompanyLimit` | decider ≠ requester | `CompanyAccounts.cs:160` |
| ProposeCustomerMerge → `DecideCustomerMerge` | decider ≠ proposer | `CustomerCommands.cs:74`, `CustomerRules.cs:238-241` |
| Config request → `DecideSettingRequest` | decider ≠ requester | `SettingRules.cs:233-239` (`DecideBlock`) |
| POS `VoidCheckItem`/`VoidBill` → `DecideVoid` | decider ≠ requester | `PosService.cs:413` |
| `ProposeYieldPublish` → `DecideYieldProposal` | decider ≠ proposer | `YieldService.cs:302-303` |
| `StockCountSubmit` → `StockCountDecide` | reviewer ≠ counter | `StockCommands.cs:374-375` |
| `CompleteWorkOrder` → `VerifyWorkOrder` | verifier ≠ doer / assignee | `MaintenanceRules.cs:337` |
| `SubmitHandoverNote` → `AcknowledgeHandoverNote` | acknowledger ≠ author | `HandoverCommands.cs:257` |
| `PreparePayRun` → `LockPayRun` | locker ≠ preparer | `PayrollService.Pay.cs:151-152` |
| `PrepareFnf` → `ConfirmFnf` / `DiscardFnf` | confirmer ≠ preparer, never your own account | `PayrollService.Pay.cs:329-332` |
| `RequestLeave` → `DecideLeave` | decider ≠ requester, never your own leave | `PayrollService.Leave.cs:93` |
| Payroll money on your own staff record (salary, advance, bank, PT slab, deactivate, login link) | refused for the person themselves (F-03, F-10, F-11) | `PayrollService.cs:62-66`, `:173`, `:206`, `:214`, `:281`; `PayrollService.Pay.cs:302` |
| Cash-book day: close → `LockCashBookDay` | the person who counted, approved the difference or closed the day cannot lock (DEC-065) | `CloseRules.cs:169-176` |
| User admin | no change to your own access (SOD-002); last active Owner protected (SOD-003) | `SqlUserAdministration.cs:539-542`, `:641-652` |

Matrix §7 lists a "closed-date correction" pair (CLS-003 request by MGR/ACC, CLS-004 approval by OWN). **No such command pair exists on the server**: CLS-003/CLS-004 are reused for `LockCashBookDay` / `ReopenCashBookDay` (borrowed ids, not in the matrix). That is a coverage question for A2b/A3b (question 3 in the list below).

### c4. Field masks on every DTO that carries ID numbers, phones or e-mails

`A05_dto_pii_fields.json` lists 29 routes whose response types contain phone / e-mail / ID / address / GSTIN style fields. By rule:

| Routes (DTO) | Fields | Mask | Clear for | Evidence | Result |
|---|---|---|---|---|---|
| `GET /guests`, `GET /guests/{id}` (`GuestView`) | phone, e-mail | `contact` flag = HasAsync(GST-002) | OWN MGR REC | `StaysCommands.cs:246`, `:257-258` | masked for CSH (last 4) |
| same | ID type, ID last 4, masked number | `idDetails` = HasAsync(GST-003); others get `Restricted`; the view never carries the full number (DEC-068) | OWN MGR REC | `StaysCommands.cs:245`, `:261-266` | ok |
| same | address, nationality, company, GSTIN | not masked for any GST-001 holder (the Cashier included) | all GST-001 holders | `StaysCommands.cs:259-263` | not in matrix §9; Info (A05-10) |
| `POST RevealGuestId`, `RevealIdCaptureNumber`, `ViewIdCaptureImage` | full ID number / image | GST-003 and an audit row without the number | OWN MGR REC | `GuestIdEndpoints.cs`, `DocumentsEndpoints.cs`; A7 audits the audit text | ok |
| `GET /documents/id-captures`, `/{id}` | ID type only; no number or image in the DTO | DOC-001 | OWN MGR REC | `IdCaptureView` has no number field | ok |
| `GET /customers`, `/customers/duplicates`, `/customers/profile` | phone, e-mail | `contact` = HasAsync(GST-002 `CustomerPermissions.Edit`) | OWN MGR REC | `CustomerQueries.cs:20`, `:84`, `:99` | masked for CSH |
| `GET /reservations`, `/reservations/{id}` | phone | `GuestContactMask.CanSeeAsync` (GST-002) | OWN MGR REC | `ReservationService.cs:199`, `:228-229` | ok |
| `GET /ota/messages`; `GET /web-booking/requests/{id}` | phone, e-mail | `GuestContactMask` | OWN MGR REC | `OtaQueries.cs:48`, `:62`; `WebBookingService.cs:109-110` | ok (F-L06) |
| `GET /web-booking/requests`; `GET /messaging/messages` (`To`) | phone / recipient | always masked (`PhoneMasked`, `RecipientMasked`) | nobody | `MessagingQueries.cs:30` | ok |
| `GET /payroll/staff` | staff phone, bank account, IFSC | phone always masked; account masked; IFSC only with HR-005 | HR-005 for IFSC | `PayrollQueries.cs:20-23`, `:40-44` | ok |
| `GET /groups/companies`, `/groups/{id}/statement`, `/cashier/folios`, invoices, credit notes, compliance settings, maintenance vendors | company contact, GSTIN, billing address, CA e-mail, vendor phone | business data, not guest PII; gated by the module permission | module holders | | n/a |
| `GET /me/capabilities` `fieldMasks["guest.contact"]` | — | says `visible` for CSH; server reads mask contact for CSH | — | `FieldMasks.cs:19` vs the rows above | A05-10 |

### c5. PIN sign-in, `X-HCC-Device` and property binding (DEC-062)

| Check | Result | Evidence |
|---|---|---|
| Only one sign-in route; PIN accepted only with a device token that matches an unrevoked `plat.device` row; otherwise `DEVICE_NOT_REGISTERED`, and the PIN is never tried (no oracle) | yes | `SqlSessionService.cs:88-111` |
| Device is bound to a property; PIN sign-in on a device of another property is refused | yes | `SqlUserAdministration.cs:345-348` (device row carries `actor.PropertyId`); `SqlSessionService.cs:177` |
| Password roles (OWN ACC TAD) can sign in only by password; PIN roles only by PIN | yes | `CredentialPolicy.RequiredKind`, `SqlSessionService.cs:170-178` |
| Every later request of a PIN session must carry the same device token, else `DEVICE_MISMATCH` (session kept alive so a thief cannot log the real tablet out) | yes, on all 370 session routes because `RequireHccSession` passes the header to `ValidateAsync` | `AccessEndpoints.cs:44-52`; `SqlSessionService.cs:264-267`, `:326-328` |
| Device revocation ends the session (`DEVICE_REVOKED`) | yes | `SqlSessionService.cs:262` |
| Who can register or revoke a device | ADM-001 = OWN and TAD (DEC-062) | `SqlUserAdministration.cs:330` |
| Residual | `POST /auth/sign-out` is anonymous and revokes the presented token without a device check, so a stolen PIN token can be used to end that session (not to use it). Info only | `AccessEndpoints.cs:154-159` |

## (d) Prototype permission-map gaps

Inputs: `03 Prototype\src\clients\sync\capabilities.ts`, `reads.ts`, `tests\_role-reads.json`, `src\domain\streams\admin.ts` (the prototype's fine-grained matrix), `src\fixtures\base.ts` (coarse grants). Parsed by `A05_proto.mjs`; result in `A05_prototype_permission_map.json`.

| Check | Result |
|---|---|
| `PERMISSION_MAP` rules vs the 12 prototype permissions | all 12 have a rule; no rule names a PERM id that the server does not seed |
| Server ids with no prototype permission | **65 of 82.** By design (module screens keep their own gates): ACC ×6, CMP ×2, CRM ×2, DOC ×2, GRP ×5, HR ×9, IMP ×3, INV ×4, OTA ×3, WEB ×1. Core ids that no rule covers: ADM-004/005, CLS-001/003/004, EXP-002/003, FOL-001/003/004/005, GST-001/003, HK-002/003, PAY-002..005, RATE-001, RES-003/004, ROOM-001/002, RPT-003, SHF-001/002, STY-002 |
| Prototype permissions with no server id | none (every one maps to ≥1 id) |
| Coarse permissions per role derived from the seed vs the prototype fixture (`base.ts`) | identical for all 9 roles **except Accountant: server gives `expenses`** (PERM-EXP-001 A·O; prototype Accountant = `reports` only). This is gap 1 in `CAPABILITY_GAPS` and is correct |
| Prototype fine-grained matrix (`admin.ts`, 45 rows) vs the seed | 404 of 405 cells equal; the one difference is Manager ADM-005 (seed V, prototype "—", matrix "status card") |
| `ROLE_MAP` | complete for the 9 seeded roles; no prototype role without a server role. `meFromCapabilities` falls back to `'Reception'` for an unknown role code (`capabilities.ts`, `?? 'Reception'`): harmless today, but the v2 RST role (D1) would be shown as Reception in every role-name gate (A05-14) |
| `reads.ts` `READS` (78 rules) vs `_role-reads.json` (real server) | 702 role × route cells, **0 disagreements** (HTTP 404 = gate passed, record missing). No rule without a route in the file and no route without a rule |
| `READS` rules that are `always` | `/me/capabilities`, `/me/directory`, `/closing/business-date`, `/configuration/values`, `/payroll/staff`, `/payroll/board`: the first four are session-only on the server; the payroll two are self-service (HR-004 for all 9 roles) and are row-filtered on the server |
| `CAPABILITY_GAPS` claims 1, 2, 3 | true (checked against the seed) |
| `CAPABILITY_GAPS` claim 6 | partly stale: it names PERM-HOV-*, PERM-MNT-*, PERM-POS*, PERM-SOD (as grants), none of which is a seeded id (the modules borrow core ids, v2 §1.2), and it does not mention PERM-IMP-* (W4-13) (A05-15) |
| Cashier and `reports` | `PERMISSION_MAP.reports` accepts RPT-002 in mode V, so the Cashier gets `reports` and the Reports menu; the server refuses every RPT-002 read for the Cashier (A05-02). `reads.ts` `FINANCE` is property-wide, so no 403 is sent, the page is empty |

## (e) Findings

Severity is given only for hard mismatches (Matrix says no, code grants = High). Everything else is "—" and a one-line impact for the lead to grade.

| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A05-01 | High (provisional) | Groups / credit | `08 Production/src/Hcc.Application/Groups/CompanyAccounts.cs:77,121,172-183`; seed `08 Production/database/migrations/2201_groups_rbac_seed.sql` (`MGR PERM-GRP-003 L 1000000`) | Matrix v1 note under §5: ledger mapping and company credit limits "Owner only (DEC-063 G11)"; `RBAC_Mismatch_Rulings_Proposed.md` G11: "a direct limit change is Owner-only"; accepted prototype `03 Prototype/src/services/mock/handlers/groups.ts:70-74` allows a direct set only for the Owner and routes everyone else to a request. Server: `SetCompanyLimit` (and the opening limit of `SaveCompany` on a new account) set the limit directly for any holder of GRP-003 within the amount limit (Manager ₹10,000). Deciding a request (`DecideCompanyLimit`) by the Manager is consistent with the prototype (`GRP_LIMIT_DECIDE` needs `approve`), so only the direct set differs. Route rows `POST /commands/SetCompanyLimit` (verdict overridden), `SaveCompany`. | A Manager can set any company's credit limit to ₹10,000 or less without the Owner (repeatable per company, no cumulative cap); G11's intent was that the Owner decides credit extended to companies. If G11 only meant "not the Tech admin", this is acceptable and the matrix wording should say so. | Lead to confirm the reading of G11. Then either make GRP-003 for credit limits Owner-only (Manager = R) or record the ruling "Manager ≤ ₹10,000" in DEC-063 and the matrix note. | 2 | Medium |
| A05-02 | — | Cashier / reports | `Hcc.Application/Billing/BillingQueries.cs:57`; `Closing/ClosingService.cs:126,347,395`; `Closing/CashBookQueries.cs:28`; `Reporting/ReportingQueries.cs:27`; seed `0101` `('CSH','PERM-RPT-002','V','S')` | Matrix v1 RPT-002: Cashier `V·S` (finance reports, own shift). Seven routes (`GET /cashier/collections`, `/closing/snapshots/{date}`, `/closing/records`, `/cashbook`, `/cashbook/{date}`, `/cashbook/{date}/book`, `/reports/daily`) call `RequireAsync(RPT-002)` with no scope, so the S grant never passes. Recorded real answers: CSH 403 on `/closing/records` and `/cashbook` (`_role-reads.json`). `capabilities.ts` gives the Cashier the coarse `reports` permission through RPT-002 V. | "Matrix says yes, code refuses": the Cashier sees a Reports menu whose data the server refuses (the loaders skip the reads, so the page is empty rather than erroring). The own-shift view (BL-024) is not implemented. | Either implement the own-shift filter for RPT-002 scope S or change matrix and prototype to "—" and hide the page for the Cashier. | 6 | High |
| A05-03 | — | Cashier / accounting | `08 Production/database/migrations/1001_accounting_rbac_seed.sql` (`('CSH','PERM-ACC-001','A','P')`); `Hcc.Application/Accounting/AccountingQueries.cs:27-156` | Recorded real answers: CSH 200 on `/accounting/days`, `/trial-balance`, `/mapping`, `/mapping/history`, `/locks/history`. The same Cashier is refused the cash book (A05-02). DEC-063 G13 says "Manager and Cashier keep Accounting" without the Tally tab and CSV export (ACC-005/006 are not granted to the Cashier: confirmed). | Not in matrix v1. Whole-hotel vouchers, trial balance and ledger mapping are readable by the Cashier while the own-shift report is not; inconsistent with matrix §10 "Cashier: finance reports for own shift". | Owner to confirm that G13 meant the whole Accounting view, or narrow ACC-001 for CSH to nothing (v2 gives CSH no ACC rows). | 1 | High (fact) |
| A05-04 | — | Reception / reports | `Hcc.Application/GuestServices/GuestServicesQueries.cs:154-158` | `RequireReportAsync`: Wide(GST-002) or RPT-001. Route rows `GET /guest-services/reports/complaints`, `/lost-found`, `/response` show REC. Matrix RPT-001 REC "—" and G8 ("Reception does not see Reports"). | Reception reads complaint / response-time / lost-and-found reports. G8 concerned the Reports page; the guest-service reports are a module screen, so this may be intended. | Owner to say yes or no. | 0.5 | Medium |
| A05-05 | — | Manager / config requests | `Hcc.Application/Configuration/ConfigurationQueries.cs:124`; `Hcc.Domain/Configuration/SettingRules.cs:233-239`; `Hcc.Application/Housekeeping/HousekeepingSettings.cs:19` | Exactly one setting (housekeeping inspection) names `Approver = Manager`. Recorded real answer: MGR 200 on `GET /configuration/requests`; the Manager can decide a Tech-admin request for that setting. Matrix §7 (after G9): config route "TAD → OWN"; G9: no setting requests for the Manager. | Small: a Manager decides one non-money Tech-admin request. | Owner to say whether a Manager may decide TAD requests; otherwise set `Approver = Owner`. | 0.5 | High |
| A05-06 | — | Tech admin / config | `Hcc.Application/Configuration/BuiltinSettings.cs:49-57` (`brand.gstin`), `:132` (`doc.retentionDays`), `:73-74`; `Hcc.Domain/Configuration/ConfigCatalog.cs:50-61` | The groups "Property and branding", "Front-desk policies", "Messages and documents", "Data" are non-money, so TAD changes their settings at once unless a setting names an approver. `brand.gstin` (tax identity printed on invoices), `doc.retentionDays` (ID-image retention), `fd.idRequired`, `fd.noShowTime` have none. DEC-058: "money, tax, payroll, compliance and approval limits stay Owner-only". | A Tech admin can change the GSTIN on tax invoices and the ID-image retention period without a second person. | Set `Approver = Owner` on `brand.gstin` and `doc.retentionDays` (or move them to a money group). | 1 | Medium |
| A05-07 | Low | Tech admin / F-M03 residual | `Hcc.Infrastructure.SqlServer/Access/SqlUserAdministration.cs:624-637` | The guard that makes credential reset, enable and disable Owner-only looks at the target's *roles* (`owner_only_assign`). A user with only a non-guarded role (SUP, HKP, MNT) who holds an Owner-granted money exception (`plat.user_exception`) can be reset by a Tech admin. | Tech admin could sign in as such a user (a PIN additionally needs a registered device, which TAD can also register). Only arises if the Owner grants money exceptions to housekeeping staff. | Also treat users with an active ALLOW exception on a money-flagged permission as guarded. | 2 | Medium |
| A05-08 | Low | Request-only cells | `SqlUserAdministration.cs:249,303`; `Hcc.Infrastructure.SqlServer/Access/SqlAccessPolicy.cs` (`RequireAsync` throws APPROVAL_REQUIRED) | Matrix §5: TAD ADM-002 = R ("role-template edits are requests the Owner approves"). The server has no role-template edit command (`GET /admin/roles` is read-only) and `GrantException`/`RevokeException` throw APPROVAL_REQUIRED for TAD without creating a request. Other R cells (FOL-005, PAY-003, INV-005, GRP-003 for REC) go through commands that do create a request. | The workflow "TAD asks, Owner approves" for access changes does not exist yet; TAD simply gets a refusal. | Build the request path or change the matrix cell to "—". | 8 (build) / 0.5 (doc) | High |
| A05-09 | Info | Seeded but never enforced | seed `0101` | Four seeded ids are checked by no route: PERM-RATE-001 (no server command prices below the tariff; the matrix limits Manager 15 % / Reception request are therefore not enforceable), PERM-RPT-003 (report export: the Cashier rule of G13 exists only in the UI; the server exports are gated by ACC-005/006 and HR-006), PERM-FOL-004 (superseded by INV-002), PERM-ADM-005 (only `HealthDetailsAllowedAsync` in `HccComposition.cs:70-71`; the Manager V cell is inert). | Matrix rows with no server enforcement. | Record in the backlog; decide whether rate override is in scope for go-live. | 0 | High |
| A05-10 | Low | Field masks | `08 Production/src/Hcc.Domain/Access/FieldMasks.cs:19` vs `Hcc.Application/Access/GuestContactMask.cs`, `Stays/StaysCommands.cs:246` | `FieldMasks.For()` returns `guest.contact = visible` for the Cashier (matrix §9: OWN MGR REC CSH see phone/e-mail), but every read masks contact unless the caller holds GST-002 (OWN MGR REC). | `/me/capabilities` tells the client the Cashier may see contact data; the server returns it masked. Safe direction, contradictory documents. | Align `FieldMasks` (or §9) with the reads. | 1 | High |
| A05-11 | — | v2 proposal vs seeds | seeds `2001`, `2101`, `2301`, `2501` vs `HCC_RBAC_Matrix_v2_Proposal.md` §2.4 | Eight seeded Wave 3 ids mean something different in v2: WEB-001 (seed: review/confirm/decline; v2: view), DOC-001 (capture; view bills), DOC-002 (purge; capture), CRM-001 (merge propose; view), CRM-002 (merge decide; notes/tags), OTA-001 (operate; view), OTA-002 (manage; resolve/map/push), OTA-003 (view commission; approve parity). GRP and INV ids are aligned. The seed 2201 and 2401 comments say the ids "follow v2 so the owner's ruling only has to confirm them". | If migration `2600` is written as the proposal describes ("insert every PERM-<MOD>-nnn"), either the insert collides with the seeded rows or the same id silently changes meaning for the grants already stored. My "(v2)" verdicts for these modules compare by action, not by id. | Renumber in one of the two documents before `2600`; add a test that compares seed text with the v2 grid. | 3 | High |
| A05-12 | Info | Security review table | `Security_Review_2026-10-26.md` rows #367, #368 | #367 `ProposeYieldPublish` lists ADM-003 (not checked: RPT-001 + APR-001, `YieldService.cs:264-266`); #368 `DecideYieldProposal` lists RPT-001 (not checked: ADM-003 only, `:295`). Six more rows omit a transitive id (table in section b). 7 routes post-date it. | The review table is a reliable list of routes and gates, with two wrong cells. | Correct the two cells if the table is re-issued. | 0.5 | High |
| A05-13 | Low | Regression test | `08 Production/tests` | No test enumerates `EndpointDataSource`; review checklist item 14 (route inventory fails on a new anonymous route) is not done. | A new unauthenticated route could be added without a failing test. | Add the test (my `A05_routes.mjs` logic ports directly); also pin the count and the 10 anonymous routes. | 3 | High |
| A05-14 | Info | Prototype | `03 Prototype/src/clients/sync/capabilities.ts` (`meFromCapabilities`) | Unknown server role → shown as `'Reception'`. | v2 D1 (RST) would be displayed as Reception in every role-name gate (Compliance, Approvals & audit nav); the server still decides every request. | Add RST to `ROLE_MAP`/`Role` when D1 is decided; make the fallback a no-access role. | 0.5 | High |
| A05-15 | Info | Prototype docs | `capabilities.ts` `CAPABILITY_GAPS` item 6 | Names PERM-HOV-*, PERM-MNT-*, PERM-POS*, PERM-SOD grants that do not exist; omits PERM-IMP-*. | Misleading comment only. | Edit the text. | 0.2 | High |
| A05-16 | Info | v2 proposal | `HCC_RBAC_Matrix_v2_Proposal.md` §3.4 C1 | C1 says the Cashier "no longer sees stock values" as a change; today the stock reads use `AccessScopes.Property` for RPT-002 (`StockQueries.cs:22`) and the recorded answer for the Cashier is 403 on all stock reads, so C1 is already true. | Stale statement in the proposal; the "parity" test in §3.3 would not find a difference. | Edit the proposal. | 0.2 | High |
| A05-17 | Info | Confirmed good | see section 0 | Seed = matrix v1 (404/405); prototype matrix = seed (404/405); `reads.ts` = real server (702/702); 10 anonymous routes, all justified; HK/MNT/TAD hold no money grants; 20 maker-checker pairs checked; DEC-062 device binding in code; DEC-057/058 in code (`SettingRules.Decide`, `EnsureOwnerForMoneyRoleAccountAsync`, import commit ids). | — | — | 0 | High |

## (f) Closing lists

### Claims I could not confirm

1. `_role-reads.json` is the output of `RoleReadMatrixTests`; I read it but did not run that test (no `dotnet test`). Its 702 answers are the ground truth for 78 GET routes; for the other 302 routes the roles are my static derivation (696/702 agreement on the validated set).
2. The `and` / `or` / `primary` semantics for the 77 routes with more than one PERM id are my reading of the code (listed in `A05_table.mjs`); a second reader (V1) should re-check them, especially Handover, GuestServices, Maintenance work-order commands and Stock.
3. The v2 id I assigned to each Wave 2 / 3 action (column "v2 proposal says"; 28 routes marked "v2 id is my reading" in the judgement column) is my reading of the grid text; the proposal does not map ids to routes.
4. Seeds only: I did not look at a deployed database. The role editing path that could change `plat.role_grant` after install (there is no command for it) was not audited.
5. The second half of `tests/sync-roles.mjs` (every page renders for every role against a fake server) was not run: the file only works through `tests/run.mjs`, which is the full suite, and it writes `tests/.generated`. I re-implemented its first half (reads.ts against the recorded answers) in `A05_validate_reads.mjs`.
6. Whether the `MapMethods` static routes in `WebAppHosting.cs` should count as routes: I did not count them (nor did the review).
7. Prototype UI behaviour beyond the permission map (what each screen shows a role) was not exercised.

### Open owner / CA decisions I hit (and the code that depends on each)

| Decision | Code that depends on it |
|---|---|
| Reading of DEC-063 G11: may the Manager set a company credit limit directly (≤ ₹10,000)? (A05-01) | `CompanyAccounts.cs:77,121,172-183`; seed `2201` MGR GRP-003 |
| BL-024: Cashier finance reports for the own shift, or not at all? (A05-02) | seven RPT-002 routes; `capabilities.ts` `reports`; seed `0101` CSH RPT-002 |
| Should the Cashier see the full trial balance and ledger mapping (G13 "keeps Accounting")? (A05-03) | seed `1001` CSH ACC-001 |
| Reception and guest-service reports (G8)? Manager deciding the one housekeeping config request? (A05-04, A05-05) | `GuestServicesQueries.cs:154-158`; `HousekeepingSettings.cs:19` |
| Which non-money settings may the Tech admin change without approval (GSTIN, ID retention)? (A05-06) | `BuiltinSettings.cs:49-57,132` |
| v2 decisions D1–D7 (RST role; dedicated ids per module; Cashier stock money; TAD templates; POS prices; prototype ids; Wave 3 grants) | the borrowed ids in POS, Stock, Maintenance, GuestServices, Messaging, Handover, Yield, Configuration (149 routes; section a, judgement "borrowed id"); migration `2600`; id collisions A05-11 |
| Is rate override below tariff in scope for go-live (matrix RATE-001)? (A05-09) | no server command exists; prototype has it |
| Own-payslip detail for HK/MNT: employer cost fields in the payslip (DEC-061 says "own pay and payslip") | `PayrollQueries.cs` `View(line…)` |
| Closed-date correction request / approval (matrix §7) | CLS-003/004 reused for cash-book lock/reopen; no correction pair |

### Questions for the lead

1. A05-01: do you read G11 as "not the Tech admin" or as "the Owner only, not the Manager"? That decides High or Info.
2. A05-02 / A05-03: do you want these graded as findings, or as owner decisions in the report's decision list?
3. The matrix §7 closed-date correction pair has no server command (CLS-003/004 are reused). Is that an A2b gap, an A3b cash-book note, or both?
4. Should V1 re-check my `SEM` table for the 77 multi-id routes, and may a Sonnet agent run `RoleReadMatrixTests` under the test lock to confirm the 702 recorded answers?
5. A05-11 (id collisions with v2) matters only if migration `2600` is still planned; who decides that?
6. The "Match" verdicts against v1 for core routes are largely the seed matching its own source; do you want the readiness gate "Access control matches matrix" scored on the seed match plus the sweeps rather than on the route verdict counts?

## Evidence index (`05 Delivery Evidence\Audit\evidence\A05\`)

| File | Content |
|---|---|
| `A05_routes.csv` / `.json` | the 380 routes: method, path, session, source file:line, handler targets, PERM ids (direct / via), each use with call and scope |
| `A05_mapcalls_per_file.json`, `A05_enumeration_summary.json` | the 291 map calls per file, the 5 helper definitions, anonymous list |
| `A05_review_rows.json`, `A05_review_diff.json` | the review's 373 rows parsed, the +7 / 0 missing result, the 9 PERM-column differences |
| `A05_seed_grants.csv`, `A05_seed_vs_v1.csv`, `A05_access_summary.json` | 250 seed grants; seed vs matrix v1 cell diff (1 row); 82 permissions, unchecked ids |
| `A05_route_access.csv` / `.json`, `A05_route_table.csv` / `.json` | the per-route role cells and the final table of section (a) |
| `A05_validate_vs_real_server.json`, `A05_reads_ts_vs_role_reads_json.json` | static derivation vs the recorded real answers (702 cells, 6 differences); `reads.ts` vs the recorded answers (0 differences) |
| `A05_dto_money_fields.json`, `A05_dto_pii_fields.json`, `A05_sweep_hk_mnt_money.json` | response-DTO walks for sweeps c1 and c4 |
| `A05_prototype_permission_map.json` | section (d) |

Probes (`05 Delivery Evidence\Audit\probes\`): `A05_lib.mjs` (scanner, index, route and PERM extraction), `A05_routes.mjs`, `A05_reconcile.mjs`, `A05_access.mjs`, `A05_validate_reads.mjs`, `A05_dto.mjs`, `A05_sweep.mjs`, `A05_proto.mjs`, `A05_table.mjs`, `A05_render.mjs` + `A05_report_template.md` (this file), `A05_trace.mjs` (debug aid). Re-run order (all from the repo root, Node 25): `routes → reconcile → access → table → dto → sweep → proto → validate_reads → render`; the whole chain takes about 20 seconds.
