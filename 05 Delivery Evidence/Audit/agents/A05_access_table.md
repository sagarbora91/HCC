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
| Match (v2 proposal) | 139 |
| Not in matrix | 87 |
| Match | 74 |
| Matrix says yes but code refuses (v2 proposal) | 49 |
| Matrix says no but code grants (v2 proposal) | 23 |
| Matrix says yes but code refuses | 7 |
| Matrix says no but code grants (G11, see A05-01) | 1 |

Against matrix v1 only (a v1 row describes the action): Not in matrix: 299 · Match: 74 · Matrix says yes but code refuses: 7 (`Not in matrix` here = no v1 row; 212 of those routes have a v2 grid row, section a).

## (a) Route table

| # | Method | Path (under /api/v1) | Session | PERM gate | Roles granted by seed | Matrix v1 says | v2 proposal says | Verdict | Needs judgement |
|---|---|---|---|---|---|---|---|---|---|
| 1 | GET | `/accounting/chart` | yes | ACC-001 | OWN MGR CSH ACC | no row | unchanged | Not in matrix | A05-03: Cashier holds ACC-001 (whole books) |
| 2 | GET | `/accounting/days` | yes | ACC-001 | OWN MGR CSH ACC † | no row | unchanged | Not in matrix | A05-03: Cashier holds ACC-001 (whole books) |
| 3 | GET | `/accounting/days/{date}` | yes | ACC-001 | OWN MGR CSH ACC | no row | unchanged | Not in matrix | A05-03: Cashier holds ACC-001 (whole books) |
| 4 | GET | `/accounting/export/csv` | yes | ACC-005 | OWN MGR ACC | no row | unchanged | Not in matrix |  |
| 5 | GET | `/accounting/export/tally` | yes | ACC-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 6 | GET | `/accounting/locks/history` | yes | ACC-001 | OWN MGR CSH ACC † | no row | unchanged | Not in matrix | A05-03: Cashier holds ACC-001 (whole books) |
| 7 | GET | `/accounting/mapping` | yes | ACC-001 | OWN MGR CSH ACC † | no row | unchanged | Not in matrix | A05-03: Cashier holds ACC-001 (whole books) |
| 8 | GET | `/accounting/mapping/history` | yes | ACC-001 | OWN MGR CSH ACC † | no row | unchanged | Not in matrix | A05-03: Cashier holds ACC-001 (whole books) |
| 9 | GET | `/accounting/trial-balance` | yes | ACC-001 | OWN MGR CSH ACC † | no row | unchanged | Not in matrix | A05-03: Cashier holds ACC-001 (whole books) |
| 10 | GET | `/admin/audit` | yes | ADM-004 | OWN MGR ACC † | OWN MGR ACC | unchanged | Match |  |
| 11 | GET | `/admin/devices` | yes | ADM-001 | OWN TAD | OWN TAD | unchanged | Match |  |
| 12 | GET | `/admin/exceptions` | yes | ADM-001 / ADM-002 / ADM-003 [any] | OWN TAD † | OWN TAD | unchanged | Match | OR: RequireAnyAsync |
| 13 | GET | `/admin/roles` | yes | ADM-001 / ADM-002 / ADM-003 [any] | OWN TAD † | OWN TAD | unchanged | Match | OR: RequireAnyAsync |
| 14 | GET | `/admin/users` | yes | ADM-001 | OWN TAD † | OWN TAD | unchanged | Match |  |
| 15 | POST | `/auth/bootstrap-owner` | no | loopback only | — | no row | — | Not in matrix | Anonymous: loopback-only (server PC) |
| 16 | POST | `/auth/sign-in` | no | anonymous | — | no row | — | Not in matrix | Anonymous: by design |
| 17 | POST | `/auth/sign-out` | no | anonymous | — | no row | — | Not in matrix | Anonymous: by design |
| 18 | GET | `/availability` | yes | RES-001 | OWN MGR REC † | OWN MGR REC | unchanged | Match |  |
| 19 | GET | `/cashbook` | yes | RPT-002 | OWN MGR ACC † | OWN MGR CSH ACC | unchanged | Matrix says yes but code refuses | A05-02: matrix gives the Cashier V·S; every RPT-002 gate refuses the Cashier |
| 20 | GET | `/cashbook/{date}` | yes | RPT-002 | OWN MGR CSH(V)·S* ACC | OWN MGR CSH ACC | unchanged | Matrix says yes but code refuses | * scoped-only (CSH): passes only when the code names an own record/shift; A05-02: matrix gives the Cashier V·S; every RPT-002 gate refuses the Cashier |
| 21 | GET | `/cashbook/{date}/book` | yes | RPT-002 | OWN MGR CSH(V)·S* ACC | OWN MGR CSH ACC | unchanged | Matrix says yes but code refuses | * scoped-only (CSH): passes only when the code names an own record/shift; A05-02: matrix gives the Cashier V·S; every RPT-002 gate refuses the Cashier |
| 22 | GET | `/cashier/collections` | yes | RPT-002 | OWN MGR CSH(V)·S* ACC | OWN MGR CSH ACC | unchanged | Matrix says yes but code refuses | * scoped-only (CSH): passes only when the code names an own record/shift; A05-02: matrix gives the Cashier V·S; every RPT-002 gate refuses the Cashier |
| 23 | GET | `/cashier/desk` | yes | SHF-001 | OWN MGR REC·S CSH·S † | OWN MGR REC CSH | unchanged | Match |  |
| 24 | GET | `/cashier/folios` | yes | FOL-001 | OWN MGR REC CSH ACC(V) † | OWN MGR REC CSH ACC | unchanged | Match |  |
| 25 | GET | `/closing/business-date` | yes | session-only | OWN MGR REC CSH ACC SUP HKP MNT TAD † | no row | — | Not in matrix | Session-only by design (no PERM) |
| 26 | GET | `/closing/preflight` | yes | CLS-001 | OWN MGR ACC(V) | OWN MGR ACC | unchanged | Match |  |
| 27 | GET | `/closing/records` | yes | RPT-002 | OWN MGR ACC † | OWN MGR CSH ACC | unchanged | Matrix says yes but code refuses | A05-02: matrix gives the Cashier V·S; every RPT-002 gate refuses the Cashier |
| 28 | GET | `/closing/runs/{date}` | yes | CLS-001 | OWN MGR ACC(V) | OWN MGR ACC | unchanged | Match |  |
| 29 | GET | `/closing/snapshots/{date}` | yes | RPT-002 | OWN MGR CSH(V)·S* ACC | OWN MGR CSH ACC | unchanged | Matrix says yes but code refuses | * scoped-only (CSH): passes only when the code names an own record/shift; A05-02: matrix gives the Cashier V·S; every RPT-002 gate refuses the Cashier |
| 30 | POST | `/commands/AcceptChannelMessage` | yes | OTA-001 | OWN MGR REC | no row | OTA-002: OWN MGR REC | Match (v2) |  |
| 31 | POST | `/commands/AcknowledgeHandoverNote` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A | no row | HND-003: OWN MGR | Matrix says no but code grants (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id HK-001/PAY-001/APR-001 (v2 §1.2) |
| 32 | POST | `/commands/AddCheckItem` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-002: OWN MGR REC CSH | Match (v2) | borrowed id FOL-002 (v2 §1.2) |
| 33 | POST | `/commands/AddCustomerNote` | yes | GST-002 | OWN MGR REC | no row | CRM-002: OWN MGR REC | Match (v2) |  |
| 34 | POST | `/commands/AddCustomerTag` | yes | GST-002 | OWN MGR REC | no row | CRM-002: OWN MGR REC | Match (v2) |  |
| 35 | POST | `/commands/AddWorkOrderPart` | yes | HK-001 + ROOM-001 [all] · mask HK-004 | OWN MGR SUP HKP·A MNT·A | no row | MNT-002: OWN MGR SUP HKP MNT | Match (v2) | AND: HK-004 holders (manager) pass without ROOM-001 on a record; ROOM-001 is checked for the work order record; borrowed id HK-001/ROOM-001 (v2 §1.2) |
| 36 | POST | `/commands/AdvanceKot` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-002: OWN MGR REC CSH | Match (v2) | borrowed id FOL-002 (v2 §1.2) |
| 37 | POST | `/commands/AmendReservation` | yes | RES-003 (step-up APR-001) | OWN MGR REC | OWN MGR REC | unchanged | Match | PRIMARY: APR-001 only for a restriction override |
| 38 | POST | `/commands/ApplyGroupRouting` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 39 | POST | `/commands/AssignRole` | yes | ADM-001 | OWN TAD | OWN TAD | unchanged | Match |  |
| 40 | POST | `/commands/AssignServiceRequest` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP HKP·A* MNT·A* | no row | GSV-005: OWN MGR SUP | Matrix says no but code grants (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); * scoped-only (HKP MNT): passes only when the code names an own record/shift; borrowed id GST-002/HK-002/HK-003/ROOM-001 (v2 §1.2) |
| 41 | POST | `/commands/AssignWorkOrder` | yes | HK-002 | OWN MGR SUP | no row | MNT-003: OWN MGR SUP | Match (v2) | borrowed id HK-002 (v2 §1.2) |
| 42 | POST | `/commands/AttachGroupMember` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 43 | POST | `/commands/BlockRoom` | yes | ROOM-002 | OWN MGR SUP | OWN MGR SUP | unchanged | Match |  |
| 44 | POST | `/commands/BlockWorkOrderRoom` | yes | ROOM-002 + HK-004 [all] | OWN MGR SUP | no row | MNT-003: OWN MGR SUP | Match (v2) | AND: several ids; borrowed id ROOM-002/HK-004 (v2 §1.2) |
| 45 | POST | `/commands/CancelLeave` | yes | HR-004 · mask HR-002 | OWN·O MGR·O REC·O CSH·O ACC·O SUP·O HKP·O MNT·O TAD·O | no row | unchanged | Not in matrix |  |
| 46 | POST | `/commands/CancelReservation` | yes | RES-004 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 47 | POST | `/commands/CancelServiceRequest` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP | no row | GSV-002: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); borrowed id GST-002/HK-002 (v2 §1.2) |
| 48 | POST | `/commands/CancelWorkOrder` | yes | HK-004 | OWN MGR SUP | no row | MNT-003: OWN MGR SUP | Match (v2) | borrowed id HK-004 (v2 §1.2) |
| 49 | POST | `/commands/CaptureIdDocument` | yes | DOC-001 | OWN MGR REC | no row | DOC-002: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) |  |
| 50 | POST | `/commands/ChangeOwnCredential` | yes | session-only | — | no row | — | Not in matrix | Session-only by design (no PERM) |
| 51 | POST | `/commands/CheckIn` | yes | STY-001 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 52 | POST | `/commands/CheckoutStay` | yes | STY-003 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 53 | POST | `/commands/ClaimLostItem` | yes | GST-002 | OWN MGR REC | no row | GSV-002: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 54 | POST | `/commands/CommitImportBatch` | yes | IMP-001 + IMP-003 [all] | OWN | no row | — | Not in matrix | AND: IMP-002 suffices for non-money masters; IMP-003 for money masters (both Owner only) |
| 55 | POST | `/commands/CompleteCarryItem` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A | no row | HND-002: OWN MGR REC CSH SUP HKP MNT | Match (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id HK-001/PAY-001/APR-001 (v2 §1.2) |
| 56 | POST | `/commands/CompleteServiceRequest` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP HKP·A MNT·A | no row | GSV-002: OWN MGR REC SUP HKP MNT | Match (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); borrowed id GST-002/HK-003/ROOM-001 (v2 §1.2) |
| 57 | POST | `/commands/CompleteWorkOrder` | yes | HK-001 + ROOM-001 [all] · mask HK-004 | OWN MGR SUP HKP·A MNT·A | no row | MNT-002: OWN MGR SUP HKP MNT | Match (v2) | AND: HK-004 holders (manager) pass without ROOM-001 on a record; ROOM-001 is checked for the work order record; borrowed id HK-001/ROOM-001 (v2 §1.2) |
| 58 | POST | `/commands/ConfirmFnf` | yes | HR-008 | OWN ACC(L) | no row | unchanged | Not in matrix | limit mode L: over the limit -> APPROVAL_REQUIRED |
| 59 | POST | `/commands/ConfirmHold` | yes | RES-002 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 60 | POST | `/commands/ConfirmIdCapture` | yes | DOC-001 + GST-003 [all] | OWN MGR REC | no row | DOC-003: OWN MGR ACC | Matrix says no but code grants (v2) | AND: writes the full ID number |
| 61 | POST | `/commands/ConfirmWebRequest` | yes | WEB-001 | OWN MGR REC | no row | WEB-002: OWN MGR REC | Match (v2) |  |
| 62 | POST | `/commands/ConvertRepairToWorkOrder` | yes | HK-001 + ROOM-001 [all] · mask HK-004 | OWN MGR SUP HKP·A MNT·A | no row | MNT-003: OWN MGR SUP | Matrix says no but code grants (v2) | AND: several ids; borrowed id HK-001/ROOM-001 (v2 §1.2) |
| 63 | POST | `/commands/CopyRosterWeek` | yes | HR-002 | OWN MGR | no row | unchanged | Not in matrix |  |
| 64 | POST | `/commands/CreateGroup` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 65 | POST | `/commands/CreateGroupBlock` | yes | RES-002 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 66 | POST | `/commands/CreateOutlet` | yes | ADM-003 | OWN | no row | POS-006: OWN MGR | Matrix says yes but code refuses (v2) | borrowed id ADM-003 (v2 §1.2) |
| 67 | POST | `/commands/CreateReservation` | yes | RES-002 (step-up APR-001) | OWN MGR REC | OWN MGR REC | unchanged | Match | PRIMARY: APR-001 only for a restriction override |
| 68 | POST | `/commands/CreateTill` | yes | ADM-003 | OWN | no row | — | Not in matrix | borrowed id ADM-003 (v2 §1.2) |
| 69 | POST | `/commands/CreateUser` | yes | ADM-001 | OWN TAD | OWN TAD | unchanged | Match |  |
| 70 | POST | `/commands/CreateWebDepositLink` | yes | WEB-001 | OWN MGR REC | no row | WEB-002: OWN MGR REC | Match (v2) |  |
| 71 | POST | `/commands/CreateWorkOrder` | yes | HK-001 · mask HK-003 HK-004 | OWN MGR SUP HKP·A MNT·A | no row | MNT-002: OWN MGR SUP HKP MNT | Match (v2) | borrowed id HK-001 (v2 §1.2) |
| 72 | POST | `/commands/DecideApproval` | yes | APR-001 / PAY-005 / SHF-002 [any] | OWN MGR(L) ACC | OWN MGR ACC | unchanged | Match | OR: by kind: APR-001 (limit), PAY-005 company credit, SHF-002 cash difference; never the requester; limit mode L: over the limit -> APPROVAL_REQUIRED |
| 73 | POST | `/commands/DecideCompanyLimit` | yes | GRP-003 | OWN MGR(L) REC(R) | no row | GRP-003: OWN MGR REC | Match (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; request-only mode R: refused with APPROVAL_REQUIRED unless the command itself creates a request; Manager deciding a request matches the prototype (GRP_LIMIT_DECIDE needs approve); see A05-01 for the direct set |
| 74 | POST | `/commands/DecideCreditNote` | yes | INV-003 | OWN | no row | INV-003: OWN | Match (v2) |  |
| 75 | POST | `/commands/DecideCustomerMerge` | yes | CRM-002 | OWN MGR | no row | CRM-003: OWN MGR | Match (v2) |  |
| 76 | POST | `/commands/DecideExpense` | yes | EXP-002 + EXP-003 [all] | OWN MGR(L) ACC | OWN MGR ACC | unchanged | Match | AND: EXP-002 approve + EXP-003 post (approve above the limit goes to Owner); limit mode L: over the limit -> APPROVAL_REQUIRED |
| 77 | POST | `/commands/DecideGroupAttrition` | yes | GRP-003 | OWN MGR(L) REC(R) | no row | GRP-003: OWN MGR REC | Match (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; request-only mode R: refused with APPROVAL_REQUIRED unless the command itself creates a request |
| 78 | POST | `/commands/DecideLeave` | yes | HR-003 | OWN MGR | no row | unchanged | Not in matrix |  |
| 79 | POST | `/commands/DecideSettingRequest` | yes | APR-001 | OWN MGR(L) | no row | CFG-002: OWN TAD | Matrix says no but code grants (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; borrowed id APR-001 (v2 §1.2); A05-05: Manager reaches the one Manager-approver request |
| 80 | POST | `/commands/DecideVoid` | yes | APR-001 | OWN MGR(L) | no row | POS-003: OWN MGR REC CSH | Matrix says yes but code refuses (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; borrowed id APR-001 (v2 §1.2) |
| 81 | POST | `/commands/DecideYieldProposal` | yes | ADM-003 | OWN | no row | YLD-003: OWN | Match (v2) | borrowed id ADM-003 (v2 §1.2) |
| 82 | POST | `/commands/DeclineWebRequest` | yes | WEB-001 | OWN MGR REC | no row | WEB-002: OWN MGR REC | Match (v2) |  |
| 83 | POST | `/commands/DetachGroupMember` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 84 | POST | `/commands/DiscardFnf` | yes | HR-008 | OWN ACC(L) | no row | unchanged | Not in matrix | limit mode L: over the limit -> APPROVAL_REQUIRED |
| 85 | POST | `/commands/DisposeLostItem` | yes | GST-002 (step-up APR-001) | OWN MGR REC | no row | GSV-003: OWN MGR | Matrix says no but code grants (v2) | PRIMARY: APR-001 only for High/Critical complaints or disposal; v2 id is my reading; borrowed id GST-002/APR-001 (v2 §1.2) |
| 86 | POST | `/commands/EarlyDeparture` | yes | STY-002 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 87 | POST | `/commands/ExecuteAdjustment` | yes | PAY-004 | OWN MGR REC·S CSH·S | OWN MGR REC CSH | unchanged | Match | OR: refund execution; never the requester |
| 88 | POST | `/commands/ExtendStay` | yes | STY-002 (step-up APR-001) | OWN MGR REC | OWN MGR REC | unchanged | Match | PRIMARY: APR-001 only when the agreed rate changes |
| 89 | POST | `/commands/GenerateCaPack` | yes | CMP-001 | OWN ACC | no row | unchanged | Not in matrix |  |
| 90 | POST | `/commands/GeneratePmWorkOrders` | yes | HK-004 | OWN MGR SUP | no row | MNT-003: OWN MGR SUP | Match (v2) | borrowed id HK-004 (v2 §1.2) |
| 91 | POST | `/commands/GenerateStayoverTasks` | yes | HK-002 | OWN MGR SUP | OWN MGR SUP | unchanged | Match |  |
| 92 | POST | `/commands/GiveAdvance` | yes | HR-007 | OWN ACC | no row | unchanged | Not in matrix |  |
| 93 | POST | `/commands/GrantException` | yes | ADM-002 | OWN TAD(R) | OWN TAD | unchanged | Match | request-only mode R: refused with APPROVAL_REQUIRED unless the command itself creates a request; A05-08: TAD R cell = refusal with no request record |
| 94 | POST | `/commands/HandOverLostItem` | yes | GST-002 | OWN MGR REC | no row | GSV-002: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 95 | POST | `/commands/HoldWorkOrder` | yes | HK-001 + ROOM-001 [all] · mask HK-004 | OWN MGR SUP HKP·A MNT·A | no row | MNT-002: OWN MGR SUP HKP MNT | Match (v2) | AND: HK-004 holders (manager) pass without ROOM-001 on a record; ROOM-001 is checked for the work order record; borrowed id HK-001/ROOM-001 (v2 §1.2) |
| 96 | POST | `/commands/ImportChannelMessage` | yes | OTA-001 | OWN MGR REC | no row | OTA-002: OWN MGR REC | Match (v2) |  |
| 97 | POST | `/commands/ImportConfiguration` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-002: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2); A05-06: TAD changes non-money settings at once (GSTIN, ID retention) |
| 98 | POST | `/commands/IssueBankFile` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 99 | POST | `/commands/IssueInvoice` | yes | INV-002 | OWN MGR REC CSH | no row | INV-002: OWN MGR REC CSH | Match (v2) |  |
| 100 | POST | `/commands/LockAccountingPeriod` | yes | ACC-002 | OWN MGR ACC | no row | unchanged | Not in matrix |  |
| 101 | POST | `/commands/LockCashBookDay` | yes | CLS-003 | OWN MGR ACC | no row | — | Not in matrix | borrowed id CLS-003 (v2 §1.2) |
| 102 | POST | `/commands/LockPayRun` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 103 | POST | `/commands/LogComplaint` | yes | GST-002 | OWN MGR REC | no row | GSV-002: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 104 | POST | `/commands/LogLostItem` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP HKP·A* MNT·A* | no row | GSV-002: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); * scoped-only (HKP MNT): passes only when the code names an own record/shift; borrowed id GST-002/HK-003/ROOM-001 (v2 §1.2) |
| 105 | POST | `/commands/LogServiceRequest` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP HKP·A* MNT·A* | no row | GSV-002: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); * scoped-only (HKP MNT): passes only when the code names an own record/shift; borrowed id GST-002/HK-003/ROOM-001 (v2 §1.2) |
| 106 | POST | `/commands/MarkAttendance` | yes | HR-004 / HR-002 [any] | OWN·O MGR·O REC·O CSH·O ACC·O SUP·O HKP·O MNT·O TAD·O | no row | unchanged | Not in matrix | OR: own record (HR-004 scope O) or manager (HR-002) |
| 107 | POST | `/commands/MarkComplianceFiled` | yes | CMP-001 | OWN ACC | no row | unchanged | Not in matrix |  |
| 108 | POST | `/commands/MarkComplianceNotApplicable` | yes | CMP-001 | OWN ACC | no row | unchanged | Not in matrix |  |
| 109 | POST | `/commands/MarkNoShow` | yes | RES-004 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 110 | POST | `/commands/MarkWebDepositPaid` | yes | WEB-001 + PAY-002 [all] | OWN MGR REC | no row | WEB-002: OWN MGR REC | Match (v2) | AND: several ids |
| 111 | POST | `/commands/MoveRoom` | yes | STY-002 (step-up APR-001) | OWN MGR REC | OWN MGR REC | unchanged | Match | PRIMARY: APR-001 only when the agreed rate changes |
| 112 | POST | `/commands/OfferRecoveryDiscount` | yes | GST-002 | OWN MGR REC | no row | GSV-003: OWN MGR | Matrix says no but code grants (v2) | v2 id is my reading; borrowed id GST-002 (v2 §1.2) |
| 113 | POST | `/commands/OpenCheck` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-002: OWN MGR REC CSH | Match (v2) | borrowed id FOL-002 (v2 §1.2) |
| 114 | POST | `/commands/OpenShift` | yes | SHF-001 | OWN MGR REC·S CSH·S | OWN MGR REC CSH | unchanged | Match |  |
| 115 | POST | `/commands/PostCharge` | yes | FOL-002 | OWN MGR REC CSH | OWN MGR REC CSH | unchanged | Match |  |
| 116 | POST | `/commands/PostRoomNight` | yes | CLS-002 | OWN MGR | OWN MGR | unchanged | Match |  |
| 117 | POST | `/commands/PostRoomNights` | yes | CLS-002 | OWN MGR | OWN MGR | unchanged | Match |  |
| 118 | POST | `/commands/PrepareFnf` | yes | HR-008 | OWN ACC(L) | no row | unchanged | Not in matrix | limit mode L: over the limit -> APPROVAL_REQUIRED |
| 119 | POST | `/commands/PreparePayRun` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 120 | POST | `/commands/ProposeCustomerMerge` | yes | CRM-001 | OWN MGR | no row | CRM-003: OWN MGR | Match (v2) |  |
| 121 | POST | `/commands/ProposeYieldPublish` | yes | RPT-001 + APR-001 [all] | OWN MGR(L) | no row | YLD-002: OWN MGR | Match (v2) | AND: APR-001 checked with amount 0; limit mode L: over the limit -> APPROVAL_REQUIRED; borrowed id RPT-001/APR-001 (v2 §1.2) |
| 122 | POST | `/commands/PublishYieldTariffs` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-003: OWN | Match (v2) | AND: Owner only through ADM-003; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 123 | POST | `/commands/PullChannelMessages` | yes | OTA-001 | OWN MGR REC | no row | OTA-002: OWN MGR REC | Match (v2) |  |
| 124 | POST | `/commands/PurgeIdCaptures` | yes | DOC-002 | OWN MGR | no row | DOC-005: OWN | Matrix says no but code grants (v2) |  |
| 125 | POST | `/commands/PushChannelRates` | yes | OTA-001 | OWN MGR REC | no row | OTA-003: OWN MGR | Matrix says no but code grants (v2) | v2 id is my reading |
| 126 | POST | `/commands/QueueSimulatedMessage` | yes | OTA-002 | OWN MGR | no row | OTA-005: OWN TAD | Matrix says no but code grants (v2) | v2 id is my reading |
| 127 | POST | `/commands/QuoteStay` | yes | RES-002 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 128 | POST | `/commands/ReceiveSpare` | yes | HK-004 | OWN MGR SUP | no row | MNT-003: OWN MGR SUP | Match (v2) | borrowed id HK-004 (v2 §1.2) |
| 129 | POST | `/commands/RecordComplaintFollowUp` | yes | GST-002 (step-up APR-001) | OWN MGR REC | no row | GSV-003: OWN MGR | Matrix says no but code grants (v2) | PRIMARY: APR-001 only for High/Critical complaints or disposal; v2 id is my reading; borrowed id GST-002/APR-001 (v2 §1.2) |
| 130 | POST | `/commands/RecordGroupReceipt` | yes | GRP-005 (step-up GRP-003) | OWN MGR REC·S CSH·S | no row | GRP-005: OWN MGR REC CSH | Match (v2) | PRIMARY: GRP-003 step-up for master refunds |
| 131 | POST | `/commands/RecordReceipt` | yes | PAY-001 / PAY-002 [any] | OWN MGR REC·S CSH·S | OWN MGR REC CSH | unchanged | Match | OR: payment = PAY-001, deposit = PAY-002; cash needs the open own shift |
| 132 | POST | `/commands/RegisterDevice` | yes | ADM-001 | OWN TAD | OWN TAD | unchanged | Match |  |
| 133 | POST | `/commands/RegisterGuest` | yes | GST-002 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 134 | POST | `/commands/RejectChannelMessage` | yes | OTA-001 | OWN MGR REC | no row | OTA-002: OWN MGR REC | Match (v2) |  |
| 135 | POST | `/commands/RejectIdCapture` | yes | DOC-001 | OWN MGR REC | no row | DOC-003: OWN MGR ACC | Matrix says no but code grants (v2) |  |
| 136 | POST | `/commands/RekeyGuestIds` | yes | ADM-003 | OWN | OWN | unchanged | Match |  |
| 137 | POST | `/commands/ReleaseGroupBlock` | yes | RES-002 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 138 | POST | `/commands/RemoveCustomerTag` | yes | GST-002 | OWN MGR REC | no row | CRM-002: OWN MGR REC | Match (v2) |  |
| 139 | POST | `/commands/ReopenCashBookDay` | yes | CLS-004 | OWN | no row | — | Not in matrix | borrowed id CLS-004 (v2 §1.2) |
| 140 | POST | `/commands/ReopenChecklistRun` | yes | APR-001 | OWN MGR(L) | no row | HND-003: OWN MGR | Match (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; borrowed id APR-001 (v2 §1.2) |
| 141 | POST | `/commands/ReopenComplianceFiling` | yes | CMP-002 | OWN | no row | unchanged | Not in matrix |  |
| 142 | POST | `/commands/RequestAdjustment` | yes | FOL-003 / PAY-003 / FOL-005 [any] | OWN MGR REC(L) CSH(R) ACC(R) | OWN MGR REC CSH ACC | unchanged | Match | OR: by kind: discount FOL-003 (limits), refund PAY-003, correction FOL-005; R cells create a request; limit mode L: over the limit -> APPROVAL_REQUIRED; request-only mode R: refused with APPROVAL_REQUIRED unless the command itself creates a request |
| 143 | POST | `/commands/RequestCreditNote` | yes | INV-005 · mask INV-003 | MGR REC(R) CSH(R) ACC(R) | no row | INV-005: MGR REC CSH ACC | Match (v2) | request-only mode R: refused with APPROVAL_REQUIRED unless the command itself creates a request |
| 144 | POST | `/commands/RequestGroupAttrition` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 145 | POST | `/commands/RequestLeave` | yes | HR-004 · mask HR-002 | OWN·O MGR·O REC·O CSH·O ACC·O SUP·O HKP·O MNT·O TAD·O | no row | unchanged | Not in matrix |  |
| 146 | POST | `/commands/RequestRepair` | yes | ROOM-001 | OWN MGR REC SUP HKP·A MNT·A | OWN MGR REC SUP HKP MNT | unchanged | Match |  |
| 147 | POST | `/commands/RerouteGroupCharge` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 148 | POST | `/commands/ResetLedgerMapping` | yes | ACC-004 | OWN | no row | unchanged | Not in matrix |  |
| 149 | POST | `/commands/ResolveComplaint` | yes | GST-002 | OWN MGR REC | no row | GSV-003: OWN MGR | Matrix says no but code grants (v2) | v2 id is my reading; borrowed id GST-002 (v2 §1.2) |
| 150 | POST | `/commands/RetryMessage` | yes | RES-002 | OWN MGR REC | no row | MSG-002: OWN MGR REC | Match (v2) | borrowed id RES-002 (v2 §1.2) |
| 151 | POST | `/commands/RevealGuestId` | yes | GST-003 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 152 | POST | `/commands/RevealIdCaptureNumber` | yes | GST-003 | OWN MGR REC | no row | DOC-004: OWN MGR REC | Match (v2) |  |
| 153 | POST | `/commands/ReverseExpense` | yes | EXP-003 | OWN MGR ACC | OWN MGR ACC | unchanged | Match |  |
| 154 | POST | `/commands/RevertSetting` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-002: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2); A05-06: TAD changes non-money settings at once (GSTIN, ID retention) |
| 155 | POST | `/commands/RevokeDevice` | yes | ADM-001 | OWN TAD | OWN TAD | unchanged | Match |  |
| 156 | POST | `/commands/RevokeException` | yes | ADM-002 | OWN TAD(R) | OWN TAD | unchanged | Match | request-only mode R: refused with APPROVAL_REQUIRED unless the command itself creates a request; A05-08: TAD R cell = refusal with no request record |
| 157 | POST | `/commands/RevokeRole` | yes | ADM-001 | OWN TAD | OWN TAD | unchanged | Match |  |
| 158 | POST | `/commands/RunBusinessDayClose` | yes | CLS-002 | OWN MGR | OWN MGR | unchanged | Match |  |
| 159 | POST | `/commands/RunMessagingTriggers` | yes | RES-002 | OWN MGR REC | no row | MSG-002: OWN MGR REC | Match (v2) | borrowed id RES-002 (v2 §1.2) |
| 160 | POST | `/commands/SaveAmc` | yes | RPT-002 + HK-004 [all] | OWN MGR | no row | MNT-005: OWN MGR ACC | Matrix says yes but code refuses (v2) | AND: G12: Owner/Manager only; borrowed id RPT-002/HK-004 (v2 §1.2) |
| 161 | POST | `/commands/SaveChannel` | yes | OTA-002 | OWN MGR | no row | OTA-005: OWN TAD | Matrix says no but code grants (v2) | v2 id is my reading |
| 162 | POST | `/commands/SaveCompany` | yes | GRP-002 (step-up GRP-003) | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) | PRIMARY: GRP-003 decides a credit limit above 0 (Manager within limit); A05-01: Manager sets a credit limit directly up to 10,000 rupees; G11 says a direct change is Owner-only |
| 163 | POST | `/commands/SaveComplianceObligation` | yes | CMP-002 | OWN | no row | unchanged | Not in matrix |  |
| 164 | POST | `/commands/SaveComplianceSettings` | yes | CMP-002 | OWN | no row | unchanged | Not in matrix |  |
| 165 | POST | `/commands/SaveDepartment` | yes | HR-002 (step-up HR-009) | OWN MGR | no row | unchanged | Not in matrix | PRIMARY: HR-005 (pay) / HR-009 (leave cap, login link) for those fields |
| 166 | POST | `/commands/SaveGuestProfile` | yes | GST-002 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 167 | POST | `/commands/SaveGuestServiceSettings` | yes | APR-001 | OWN MGR(L) | no row | GSV-003: OWN MGR | Match (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; v2 id is my reading; borrowed id APR-001 (v2 §1.2) |
| 168 | POST | `/commands/SaveHandoverTemplate` | yes | APR-001 | OWN MGR(L) | no row | HND-003: OWN MGR | Match (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; borrowed id APR-001 (v2 §1.2) |
| 169 | POST | `/commands/SaveMaintenanceAsset` | yes | HK-004 | OWN MGR SUP | no row | — | Not in matrix | borrowed id HK-004 (v2 §1.2) |
| 170 | POST | `/commands/SaveMaintenanceVendor` | yes | HK-004 | OWN MGR SUP | no row | — | Not in matrix | borrowed id HK-004 (v2 §1.2) |
| 171 | POST | `/commands/SaveMenuItem` | yes | ADM-003 | OWN | no row | POS-006: OWN MGR | Matrix says yes but code refuses (v2) | borrowed id ADM-003 (v2 §1.2) |
| 172 | POST | `/commands/SavePmPlan` | yes | HK-004 | OWN MGR SUP | no row | MNT-002: OWN MGR SUP HKP MNT | Matrix says yes but code refuses (v2) | v2 id is my reading; borrowed id HK-004 (v2 §1.2) |
| 173 | POST | `/commands/SaveStaff` | yes | HR-002 (step-up HR-005/HR-009) | OWN MGR | no row | unchanged | Not in matrix | PRIMARY: HR-005 (pay) / HR-009 (leave cap, login link) for those fields |
| 174 | POST | `/commands/SaveYieldOccupancyRule` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-002: OWN MGR | Matrix says yes but code refuses (v2) | AND: Owner only through ADM-003; v2 id is my reading; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 175 | POST | `/commands/SaveYieldPlan` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-002: OWN MGR | Matrix says yes but code refuses (v2) | AND: Owner only through ADM-003; v2 id is my reading; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 176 | POST | `/commands/SaveYieldRestriction` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-002: OWN MGR | Matrix says yes but code refuses (v2) | AND: Owner only through ADM-003; v2 id is my reading; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 177 | POST | `/commands/SaveYieldSeason` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-002: OWN MGR | Matrix says yes but code refuses (v2) | AND: Owner only through ADM-003; v2 id is my reading; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 178 | POST | `/commands/SeedComplianceDefaults` | yes | CMP-002 | OWN | no row | unchanged | Not in matrix |  |
| 179 | POST | `/commands/SendKot` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-002: OWN MGR REC CSH | Match (v2) | borrowed id FOL-002 (v2 §1.2) |
| 180 | POST | `/commands/SendMessage` | yes | RES-002 | OWN MGR REC | no row | MSG-002: OWN MGR REC | Match (v2) | borrowed id RES-002 (v2 §1.2) |
| 181 | POST | `/commands/SendPayslip` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 182 | POST | `/commands/SetChannelStatus` | yes | OTA-002 | OWN MGR | no row | OTA-005: OWN TAD | Matrix says no but code grants (v2) | v2 id is my reading |
| 183 | POST | `/commands/SetChannelStopSell` | yes | OTA-001 | OWN MGR REC | no row | OTA-002: OWN MGR REC | Match (v2) |  |
| 184 | POST | `/commands/SetCheckItemQty` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-002: OWN MGR REC CSH | Match (v2) | borrowed id FOL-002 (v2 §1.2) |
| 185 | POST | `/commands/SetCompanyLimit` | yes | GRP-002 (step-up GRP-003) | OWN MGR REC | no row | GRP-003: OWN MGR REC | Matrix says no but code grants (G11, see A05-01) | PRIMARY: GRP-003 decides a credit limit above 0 (Manager within limit); A05-01: Manager sets a credit limit directly up to 10,000 rupees; G11 says a direct change is Owner-only |
| 186 | POST | `/commands/SetCredential` | yes | ADM-001 | OWN TAD | OWN TAD | unchanged | Match | A05-07: guard looks at roles, not at money exceptions |
| 187 | POST | `/commands/SetCustomerConsent` | yes | GST-002 | OWN MGR REC | no row | CRM-002: OWN MGR REC | Match (v2) |  |
| 188 | POST | `/commands/SetCustomerPreferences` | yes | GST-002 | OWN MGR REC | no row | — | Not in matrix |  |
| 189 | POST | `/commands/SetGroupRouting` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 190 | POST | `/commands/SetGstSetting` | yes | ADM-003 | OWN | OWN | unchanged | Match |  |
| 191 | POST | `/commands/SetLeavePolicy` | yes | HR-009 | OWN | no row | unchanged | Not in matrix |  |
| 192 | POST | `/commands/SetLedgerMapping` | yes | ACC-004 | OWN | no row | unchanged | Not in matrix |  |
| 193 | POST | `/commands/SetMenuItemAvailability` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-002: OWN MGR REC CSH | Match (v2) | borrowed id FOL-002 (v2 §1.2) |
| 194 | POST | `/commands/SetMessagingConsent` | yes | STY-001 · mask RES-002 | OWN MGR REC | no row | MSG-005: OWN MGR REC | Match (v2) | v2 id is my reading; borrowed id STY-001 (v2 §1.2) |
| 195 | POST | `/commands/SetRoster` | yes | HR-002 | OWN MGR | no row | unchanged | Not in matrix |  |
| 196 | POST | `/commands/SetSetting` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-002: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2); A05-06: TAD changes non-money settings at once (GSTIN, ID retention) |
| 197 | POST | `/commands/SetStaffActive` | yes | HR-002 | OWN MGR | no row | unchanged | Not in matrix |  |
| 198 | POST | `/commands/SetStatutoryRates` | yes | HR-009 | OWN | no row | unchanged | Not in matrix |  |
| 199 | POST | `/commands/SettleCheck` | yes | FOL-002 + PAY-001 [all] | OWN MGR REC CSH | no row | POS-005: OWN MGR REC CSH | Match (v2) | AND: PAY-001 own-shift scope for cash; borrowed id FOL-002/PAY-001 (v2 §1.2) |
| 200 | POST | `/commands/SettleGroup` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 201 | POST | `/commands/SetUserEnabled` | yes | ADM-001 | OWN TAD | OWN TAD | unchanged | Match | A05-07: guard looks at roles, not at money exceptions |
| 202 | POST | `/commands/SetYieldDayOfWeek` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-002: OWN MGR | Matrix says yes but code refuses (v2) | AND: Owner only through ADM-003; v2 id is my reading; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 203 | POST | `/commands/SetYieldForecast` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-002: OWN MGR | Matrix says yes but code refuses (v2) | AND: Owner only through ADM-003; v2 id is my reading; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 204 | POST | `/commands/SetYieldGuardrails` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-002: OWN MGR | Matrix says yes but code refuses (v2) | AND: Owner only through ADM-003; v2 id is my reading; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 205 | POST | `/commands/SetYieldOverride` | yes | RPT-001 + ADM-003 [all] | OWN | no row | YLD-002: OWN MGR | Matrix says yes but code refuses (v2) | AND: Owner only through ADM-003; v2 id is my reading; borrowed id RPT-001/ADM-003 (v2 §1.2) |
| 206 | POST | `/commands/ShortenStay` | yes | STY-002 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 207 | POST | `/commands/SimulateMessageDelivery` | yes | RES-002 | OWN MGR REC | no row | — | Not in matrix | borrowed id RES-002 (v2 §1.2) |
| 208 | POST | `/commands/StartServiceRequest` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP HKP·A MNT·A | no row | GSV-002: OWN MGR REC SUP HKP MNT | Match (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); borrowed id GST-002/HK-003/ROOM-001 (v2 §1.2) |
| 209 | POST | `/commands/StartWorkOrder` | yes | HK-001 + ROOM-001 [all] · mask HK-004 | OWN MGR SUP HKP·A MNT·A | no row | MNT-002: OWN MGR SUP HKP MNT | Match (v2) | AND: HK-004 holders (manager) pass without ROOM-001 on a record; ROOM-001 is checked for the work order record; borrowed id HK-001/ROOM-001 (v2 §1.2) |
| 210 | POST | `/commands/StockCountDecide` | yes | HK-004 | OWN MGR SUP | no row | STK-003: OWN MGR SUP | Match (v2) | borrowed id HK-004 (v2 §1.2) |
| 211 | POST | `/commands/StockCountSubmit` | yes | HK-003 | OWN MGR SUP HKP·A | no row | STK-002: OWN MGR SUP HKP | Match (v2) | borrowed id HK-003 (v2 §1.2) |
| 212 | POST | `/commands/StockIssue` | yes | HK-003 | OWN MGR SUP HKP·A | no row | STK-002: OWN MGR SUP HKP | Match (v2) | borrowed id HK-003 (v2 §1.2) |
| 213 | POST | `/commands/StockItemCreate` | yes | HK-004 · mask RPT-002 | OWN MGR SUP | no row | STK-003: OWN MGR SUP | Match (v2) | borrowed id HK-004 (v2 §1.2) |
| 214 | POST | `/commands/StockItemSet` | yes | HK-004 | OWN MGR SUP | no row | STK-005: OWN MGR SUP | Match (v2) | borrowed id HK-004 (v2 §1.2) |
| 215 | POST | `/commands/StockLaundryReceive` | yes | HK-003 · mask RPT-002 | OWN MGR SUP HKP·A | no row | STK-002: OWN MGR SUP HKP | Match (v2) | borrowed id HK-003 (v2 §1.2) |
| 216 | POST | `/commands/StockLaundrySend` | yes | HK-003 | OWN MGR SUP HKP·A | no row | STK-002: OWN MGR SUP HKP | Match (v2) | borrowed id HK-003 (v2 §1.2) |
| 217 | POST | `/commands/StockReceiveGrn` | yes | HK-003 · mask HK-004 RPT-002 | OWN MGR SUP HKP·A | no row | STK-002: OWN MGR SUP HKP | Match (v2) | borrowed id HK-003 (v2 §1.2) |
| 218 | POST | `/commands/StockRecordLoss` | yes | HK-003 | OWN MGR SUP HKP·A | no row | STK-002: OWN MGR SUP HKP | Match (v2) | borrowed id HK-003 (v2 §1.2) |
| 219 | POST | `/commands/StockRoomChange` | yes | HK-003 | OWN MGR SUP HKP·A | no row | STK-002: OWN MGR SUP HKP | Match (v2) | borrowed id HK-003 (v2 §1.2) |
| 220 | POST | `/commands/StockSettingsSet` | yes | HK-004 | OWN MGR SUP | no row | STK-005: OWN MGR SUP | Match (v2) | borrowed id HK-004 (v2 §1.2) |
| 221 | POST | `/commands/StockUse` | yes | HK-003 | OWN MGR SUP HKP·A | no row | STK-002: OWN MGR SUP HKP | Match (v2) | borrowed id HK-003 (v2 §1.2) |
| 222 | POST | `/commands/SubmitChecklistRun` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A | no row | HND-002: OWN MGR REC CSH SUP HKP MNT | Match (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id HK-001/PAY-001/APR-001 (v2 §1.2) |
| 223 | POST | `/commands/SubmitExpense` | yes | EXP-001 | OWN MGR REC·O CSH·O ACC·O | OWN MGR REC CSH ACC | unchanged | Match |  |
| 224 | POST | `/commands/SubmitExpenseDraft` | yes | EXP-001 | OWN MGR REC·O CSH·O ACC·O | OWN MGR REC CSH ACC | unchanged | Match |  |
| 225 | POST | `/commands/SubmitHandoverNote` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A | no row | HND-002: OWN MGR REC CSH SUP HKP MNT | Match (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id PAY-001/HK-001/APR-001 (v2 §1.2) |
| 226 | POST | `/commands/SubmitShiftCount` | yes | SHF-001 | OWN MGR REC·S CSH·S | OWN MGR REC CSH | unchanged | Match |  |
| 227 | POST | `/commands/TickChecklistItem` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A | no row | HND-002: OWN MGR REC CSH SUP HKP MNT | Match (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id HK-001/PAY-001/APR-001 (v2 §1.2) |
| 228 | POST | `/commands/ToggleComplianceObligation` | yes | CMP-002 | OWN | no row | unchanged | Not in matrix |  |
| 229 | POST | `/commands/ToggleHandoverTemplate` | yes | APR-001 | OWN MGR(L) | no row | HND-003: OWN MGR | Match (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; borrowed id APR-001 (v2 §1.2) |
| 230 | POST | `/commands/TransferGroupMember` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 231 | POST | `/commands/TransitionHousekeepingTask` | yes | HK-001 (step-up HK-002/HK-004/HK-003) | OWN MGR SUP HKP·A MNT·A | OWN MGR SUP HKP MNT | unchanged | Match | PRIMARY: plus HK-002/003/004 by transition |
| 232 | POST | `/commands/UnblockRoom` | yes | ROOM-002 | OWN MGR SUP | OWN MGR SUP | unchanged | Match |  |
| 233 | POST | `/commands/UnlockAccountingPeriod` | yes | ACC-003 | OWN | no row | unchanged | Not in matrix |  |
| 234 | POST | `/commands/UnlockPayRun` | yes | HR-009 | OWN | no row | unchanged | Not in matrix |  |
| 235 | POST | `/commands/UpdateComplaint` | yes | GST-002 | OWN MGR REC | no row | GSV-002: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 236 | POST | `/commands/UpdateGroup` | yes | GRP-002 | OWN MGR REC | no row | GRP-002: OWN MGR REC | Match (v2) |  |
| 237 | POST | `/commands/UpdateMessageRule` | yes | APR-001 | OWN MGR(L) | no row | MSG-003: OWN MGR TAD | Matrix says yes but code refuses (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; borrowed id APR-001 (v2 §1.2) |
| 238 | POST | `/commands/UpdateMessageTemplate` | yes | APR-001 | OWN MGR(L) | no row | MSG-003: OWN MGR TAD | Matrix says yes but code refuses (v2) | limit mode L: over the limit -> APPROVAL_REQUIRED; borrowed id APR-001 (v2 §1.2) |
| 239 | POST | `/commands/UploadImportBatch` | yes | IMP-001 | OWN TAD | no row | — | Not in matrix |  |
| 240 | POST | `/commands/VerifyWorkOrder` | yes | ROOM-002 + HK-004 [all] | OWN MGR SUP | no row | MNT-003: OWN MGR SUP | Match (v2) | AND: several ids; borrowed id ROOM-002/HK-004 (v2 §1.2) |
| 241 | POST | `/commands/ViewIdCaptureImage` | yes | GST-003 | OWN MGR REC | no row | DOC-004: OWN MGR REC | Match (v2) |  |
| 242 | POST | `/commands/VoidBill` | yes | FOL-002 + PAY-001 [all] | OWN MGR REC CSH | no row | POS-003: OWN MGR REC CSH | Match (v2) | AND: PAY-001 own-shift scope for cash; v2 id is my reading; borrowed id FOL-002/PAY-001 (v2 §1.2) |
| 243 | POST | `/commands/VoidCheckItem` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-003: OWN MGR REC CSH | Match (v2) | v2 id is my reading; borrowed id FOL-002 (v2 §1.2) |
| 244 | POST | `/commands/WithdrawGstSetting` | yes | ADM-003 | OWN | OWN | unchanged | Match |  |
| 245 | POST | `/commands/WithdrawScheduledSetting` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-002: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2); A05-06: TAD changes non-money settings at once (GSTIN, ID retention) |
| 246 | GET | `/compliance/badge` | yes | CMP-001 | OWN ACC | no row | unchanged | Not in matrix |  |
| 247 | GET | `/compliance/ca-packs` | yes | CMP-001 | OWN ACC † | no row | unchanged | Not in matrix |  |
| 248 | GET | `/compliance/calendar` | yes | CMP-001 | OWN ACC † | no row | unchanged | Not in matrix |  |
| 249 | GET | `/compliance/due-this-week` | yes | CMP-001 | OWN ACC | no row | unchanged | Not in matrix |  |
| 250 | GET | `/compliance/obligations` | yes | CMP-001 | OWN ACC † | no row | unchanged | Not in matrix |  |
| 251 | GET | `/configuration/export` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-001: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2) |
| 252 | GET | `/configuration/groups` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-001: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2) |
| 253 | GET | `/configuration/groups/{groupId}` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-001: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2) |
| 254 | GET | `/configuration/history` | yes | ADM-003:VIEW | OWN TAD(V) † | no row | CFG-001: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2) |
| 255 | POST | `/configuration/import-plan` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-002: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2) |
| 256 | GET | `/configuration/modules` | yes | session-only | — | no row | — | Not in matrix | Session-only by design (no PERM) |
| 257 | GET | `/configuration/requests` | yes | ADM-003:VIEW | OWN MGR TAD(V) † | no row | CFG-001: OWN TAD | Matrix says no but code grants (v2) | borrowed id ADM-003 (v2 §1.2); A05-05: Manager reaches the one Manager-approver request |
| 258 | GET | `/configuration/search` | yes | ADM-003:VIEW | OWN TAD(V) | no row | CFG-001: OWN TAD | Match (v2) | borrowed id ADM-003 (v2 §1.2) |
| 259 | GET | `/configuration/values` | yes | session-only | OWN MGR REC CSH ACC SUP HKP MNT TAD † | no row | — | Not in matrix | Session-only by design (no PERM) |
| 260 | GET | `/credit-note-requests` | yes | INV-001 | OWN MGR REC CSH ACC(V) † | no row | INV-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 261 | GET | `/credit-notes/{creditNoteId:long}` | yes | INV-001 | OWN MGR REC CSH ACC(V) | no row | INV-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 262 | GET | `/credit-notes/{creditNoteId:long}/document` | yes | INV-002 | OWN MGR REC CSH | no row | INV-002: OWN MGR REC CSH | Match (v2) |  |
| 263 | GET | `/customers` | yes | GST-001 · mask GST-002 FOL-001 | OWN MGR REC CSH(M) | no row | CRM-001: OWN MGR REC CSH | Match (v2) | GST-002 = phone/e-mail reveal, not a gate |
| 264 | GET | `/customers/duplicates` | yes | CRM-001 · mask GST-002 FOL-001 CRM-002 | OWN MGR | no row | CRM-003: OWN MGR | Match (v2) | GST-002 = phone/e-mail reveal, not a gate; v2 id is my reading |
| 265 | GET | `/customers/merges` | yes | CRM-001 · mask CRM-002 | OWN MGR | no row | CRM-003: OWN MGR | Match (v2) | v2 id is my reading |
| 266 | GET | `/customers/profile` | yes | GST-001 · mask GST-002 FOL-001 | OWN MGR REC CSH(M) | no row | CRM-001: OWN MGR REC CSH | Match (v2) | GST-002 = phone/e-mail reveal, not a gate |
| 267 | GET | `/documents/id-captures` | yes | DOC-001 | OWN MGR REC | no row | DOC-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) |  |
| 268 | GET | `/documents/id-captures/{id:long}` | yes | DOC-001 | OWN MGR REC | no row | DOC-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) |  |
| 269 | GET | `/expenses` | yes | EXP-001 / EXP-002 / EXP-003 [any] | OWN MGR REC·O CSH·O ACC·O † | OWN MGR REC CSH ACC | unchanged | Match | OR: rows filtered to what the role may see |
| 270 | GET | `/expenses/{expenseId:long}` | yes | EXP-001 / EXP-002 / EXP-003 [any] | OWN MGR REC·O CSH·O ACC·O | OWN MGR REC CSH ACC | unchanged | Match | OR: rows filtered to what the role may see |
| 271 | GET | `/expenses/options` | yes | EXP-001 / EXP-002 / EXP-003 [any] | OWN MGR REC·O CSH·O ACC·O | OWN MGR REC CSH ACC | unchanged | Match | OR: rows filtered to what the role may see |
| 272 | GET | `/expenses/summary` | yes | EXP-002 | OWN MGR(L) ACC | OWN MGR ACC | unchanged | Match | limit mode L: over the limit -> APPROVAL_REQUIRED |
| 273 | GET | `/folios/{folioId:long}` | yes | FOL-001 | OWN MGR REC CSH ACC(V) | OWN MGR REC CSH ACC | unchanged | Match |  |
| 274 | GET | `/folios/by-reservation/{reservationId:long}` | yes | FOL-001 | OWN MGR REC CSH ACC(V) † | OWN MGR REC CSH ACC | unchanged | Match |  |
| 275 | GET | `/groups` | yes | GRP-001 | OWN MGR REC CSH(V) ACC(V) † | no row | GRP-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 276 | GET | `/groups/{groupId:long}/statement` | yes | GRP-004 | OWN MGR REC CSH(V) ACC(V) † | no row | GRP-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 277 | GET | `/groups/companies` | yes | GRP-001 | OWN MGR REC CSH(V) ACC(V) † | no row | GRP-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 278 | GET | `/guest-services/complaints` | yes | GST-002 | OWN MGR REC † | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 279 | GET | `/guest-services/complaints/{id:long}` | yes | GST-002 | OWN MGR REC | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 280 | GET | `/guest-services/histories/{kind}` | yes | GST-002 | OWN MGR REC † | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 281 | GET | `/guest-services/history/{kind}/{id:long}` | yes | GST-002 | OWN MGR REC | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 282 | GET | `/guest-services/lost-items` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP HKP·A MNT·A † | no row | GSV-001: OWN MGR REC SUP HKP MNT | Match (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); borrowed id GST-002/HK-003/ROOM-001 (v2 §1.2) |
| 283 | GET | `/guest-services/reports/complaints` | yes | GST-002 / RPT-001 [any] | OWN MGR REC ACC | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says no but code grants (v2) | OR: "report.operations or front-office rights" (GuestServicesQueries.RequireReportAsync); v2 id is my reading; borrowed id GST-002/RPT-001 (v2 §1.2); A05-04: Reception reaches this through GST-002 although RPT-001 REC is a dash (G8) |
| 284 | GET | `/guest-services/reports/lost-found` | yes | GST-002 / RPT-001 [any] | OWN MGR REC ACC | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says no but code grants (v2) | OR: "report.operations or front-office rights" (GuestServicesQueries.RequireReportAsync); v2 id is my reading; borrowed id GST-002/RPT-001 (v2 §1.2); A05-04: Reception reaches this through GST-002 although RPT-001 REC is a dash (G8) |
| 285 | GET | `/guest-services/reports/response` | yes | GST-002 / RPT-001 [any] | OWN MGR REC ACC | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says no but code grants (v2) | OR: "report.operations or front-office rights" (GuestServicesQueries.RequireReportAsync); v2 id is my reading; borrowed id GST-002/RPT-001 (v2 §1.2); A05-04: Reception reaches this through GST-002 although RPT-001 REC is a dash (G8) |
| 286 | GET | `/guest-services/requests` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP HKP·A MNT·A † | no row | GSV-001: OWN MGR REC SUP HKP MNT | Match (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); borrowed id GST-002/HK-002/HK-003/ROOM-001 (v2 §1.2) |
| 287 | GET | `/guest-services/requests/{id:long}` | yes | GST-002 / HK-002 / HK-003 / ROOM-001 [any] | OWN MGR REC SUP | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | OR: front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records); borrowed id GST-002/HK-002 (v2 §1.2) |
| 288 | GET | `/guest-services/settings` | yes | GST-002 / APR-001 [any] | OWN MGR REC † | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | OR: several ids; borrowed id GST-002/APR-001 (v2 §1.2) |
| 289 | GET | `/guest-services/stays` | yes | GST-002 | OWN MGR REC † | no row | GSV-001: OWN MGR REC SUP HKP MNT | Matrix says yes but code refuses (v2) | borrowed id GST-002 (v2 §1.2) |
| 290 | GET | `/guests` | yes | GST-001 · mask GST-003 GST-002 | OWN MGR REC CSH(M) † | OWN MGR REC CSH | unchanged | Match | GST-002 = phone/e-mail reveal, not a gate |
| 291 | GET | `/guests/{guestId:long}` | yes | GST-001 · mask GST-003 GST-002 | OWN MGR REC CSH(M) | OWN MGR REC CSH | unchanged | Match | GST-002 = phone/e-mail reveal, not a gate |
| 292 | GET | `/handover/carry` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A | no row | HND-001: OWN MGR REC CSH SUP HKP MNT | Match (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id HK-001/PAY-001/APR-001 (v2 §1.2) |
| 293 | GET | `/handover/checklists` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A † | no row | HND-001: OWN MGR REC CSH SUP HKP MNT | Match (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id HK-001/PAY-001/APR-001 (v2 §1.2) |
| 294 | GET | `/handover/dashboard` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A | no row | HND-001: OWN MGR REC CSH SUP HKP MNT | Match (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id APR-001/HK-001/PAY-001 (v2 §1.2) |
| 295 | GET | `/handover/notes` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR REC·S CSH·S SUP HKP·A MNT·A † | no row | HND-001: OWN MGR REC CSH SUP HKP MNT | Match (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id HK-001/PAY-001/APR-001 (v2 §1.2) |
| 296 | GET | `/handover/templates` | yes | HK-001 / PAY-001 / APR-001 [any] · mask RPT-002 | OWN MGR † | no row | HND-001: OWN MGR REC CSH SUP HKP MNT | Matrix says yes but code refuses (v2) | OR: module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002; borrowed id APR-001/HK-001/PAY-001 (v2 §1.2) |
| 297 | GET | `/housekeeping/tasks` | yes | HK-001 | OWN MGR SUP HKP·A MNT·A † | OWN MGR SUP HKP MNT | unchanged | Match |  |
| 298 | GET | `/import/batches` | yes | IMP-001 | OWN TAD | no row | — | Not in matrix |  |
| 299 | GET | `/import/batches/{batchId:long}` | yes | IMP-001 | OWN TAD | no row | — | Not in matrix |  |
| 300 | GET | `/import/ledgers` | yes | IMP-001 | OWN TAD | no row | — | Not in matrix |  |
| 301 | GET | `/import/masters` | yes | IMP-001 | OWN TAD | no row | — | Not in matrix |  |
| 302 | GET | `/import/opening-balances` | yes | IMP-001 | OWN TAD | no row | — | Not in matrix |  |
| 303 | GET | `/invoices` | yes | INV-001 | OWN MGR REC CSH ACC(V) | no row | INV-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 304 | GET | `/invoices/{invoiceId:long}` | yes | INV-001 | OWN MGR REC CSH ACC(V) | no row | INV-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 305 | GET | `/invoices/{invoiceId:long}/document` | yes | INV-002 | OWN MGR REC CSH | no row | INV-002: OWN MGR REC CSH | Match (v2) |  |
| 306 | GET | `/invoices/by-source/{sourceKind}/{sourceId:long}` | yes | INV-001 | OWN MGR REC CSH ACC(V) | no row | INV-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 307 | GET | `/invoices/gstr1` | yes | INV-001 | OWN MGR REC CSH ACC(V) | no row | INV-001: OWN MGR REC CSH ACC | Match (v2) |  |
| 308 | GET | `/maintenance/amcs` | yes | HK-004 · mask RPT-002 | OWN MGR SUP † | no row | MNT-005: OWN MGR ACC | Matrix says no but code grants (v2) | borrowed id HK-004 (v2 §1.2) |
| 309 | GET | `/maintenance/registers` | yes | HK-001 · mask RPT-002 HK-004 | OWN MGR SUP HKP·A MNT·A † | no row | MNT-001: OWN MGR SUP HKP MNT | Match (v2) | borrowed id HK-001 (v2 §1.2) |
| 310 | GET | `/maintenance/report` | yes | HK-004 · mask RPT-002 | OWN MGR SUP | no row | MNT-004: OWN MGR ACC | Matrix says no but code grants (v2) | borrowed id HK-004 (v2 §1.2) |
| 311 | GET | `/maintenance/work-orders` | yes | HK-001 · mask HK-004 RPT-002 HK-003 | OWN MGR SUP HKP·A MNT·A † | no row | MNT-001: OWN MGR SUP HKP MNT | Match (v2) | borrowed id HK-001 (v2 §1.2) |
| 312 | GET | `/maintenance/work-orders/{id:long}` | yes | HK-001 · mask HK-004 RPT-002 HK-003 | OWN MGR SUP HKP·A MNT·A † | no row | MNT-001: OWN MGR SUP HKP MNT | Match (v2) | borrowed id HK-001 (v2 §1.2) |
| 313 | GET | `/me/capabilities` | yes | session-only | OWN MGR REC CSH ACC SUP HKP MNT TAD † | no row | — | Not in matrix | Session-only by design (no PERM) |
| 314 | GET | `/me/directory` | yes | session-only | OWN MGR REC CSH ACC SUP HKP MNT TAD † | no row | — | Not in matrix | Session-only by design (no PERM) |
| 315 | GET | `/messaging/consent/{guestId:long}` | yes | RES-001 | OWN MGR REC | no row | MSG-001: OWN MGR REC | Match (v2) | borrowed id RES-001 (v2 §1.2) |
| 316 | GET | `/messaging/consents` | yes | RES-001 | OWN MGR REC † | no row | MSG-001: OWN MGR REC | Match (v2) | borrowed id RES-001 (v2 §1.2) |
| 317 | GET | `/messaging/messages` | yes | RES-001 | OWN MGR REC † | no row | MSG-001: OWN MGR REC | Match (v2) | borrowed id RES-001 (v2 §1.2) |
| 318 | GET | `/messaging/messages/{id:long}` | yes | RES-001 | OWN MGR REC | no row | MSG-001: OWN MGR REC | Match (v2) | borrowed id RES-001 (v2 §1.2) |
| 319 | GET | `/messaging/rules` | yes | RES-001 | OWN MGR REC † | no row | MSG-001: OWN MGR REC | Match (v2) | borrowed id RES-001 (v2 §1.2) |
| 320 | GET | `/messaging/summary` | yes | RES-001 | OWN MGR REC | no row | MSG-001: OWN MGR REC | Match (v2) | borrowed id RES-001 (v2 §1.2) |
| 321 | GET | `/messaging/templates` | yes | RES-001 | OWN MGR REC † | no row | MSG-001: OWN MGR REC | Match (v2) | borrowed id RES-001 (v2 §1.2) |
| 322 | GET | `/ota/channels` | yes | OTA-001 | OWN MGR REC † | no row | OTA-001: OWN MGR REC ACC TAD | Matrix says yes but code refuses (v2) |  |
| 323 | GET | `/ota/channels/{id:int}/push-preview` | yes | OTA-001 | OWN MGR REC | no row | OTA-001: OWN MGR REC ACC TAD | Matrix says yes but code refuses (v2) |  |
| 324 | GET | `/ota/commission` | yes | OTA-003 | OWN MGR ACC | no row | OTA-004: OWN MGR ACC | Match (v2) |  |
| 325 | GET | `/ota/messages` | yes | OTA-001 · mask GST-002 | OWN MGR REC † | no row | OTA-001: OWN MGR REC ACC TAD | Matrix says yes but code refuses (v2) | GST-002 = phone/e-mail reveal, not a gate |
| 326 | GET | `/ota/messages/{id:long}/check` | yes | OTA-001 | OWN MGR REC | no row | OTA-001: OWN MGR REC ACC TAD | Matrix says yes but code refuses (v2) |  |
| 327 | GET | `/ota/pushes` | yes | OTA-001 | OWN MGR REC † | no row | OTA-001: OWN MGR REC ACC TAD | Matrix says yes but code refuses (v2) |  |
| 328 | GET | `/ota/reconciliation` | yes | OTA-003 | OWN MGR ACC | no row | OTA-004: OWN MGR ACC | Match (v2) |  |
| 329 | GET | `/payroll/board` | yes | HR-004 · mask HR-001 HR-006 | OWN·O MGR·O REC·O CSH·O ACC·O SUP·O HKP·O MNT·O TAD·O † | no row | unchanged | Not in matrix |  |
| 330 | GET | `/payroll/my/payslips` | yes | HR-004 | OWN·O MGR·O REC·O CSH·O ACC·O SUP·O HKP·O MNT·O TAD·O | no row | unchanged | Not in matrix |  |
| 331 | GET | `/payroll/payslips/{runId:long}/{staffId:long}` | yes | HR-004 · mask HR-006 | OWN·O MGR·O REC·O CSH·O ACC·O SUP·O HKP·O MNT·O TAD·O | no row | unchanged | Not in matrix |  |
| 332 | GET | `/payroll/rates/{month}` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 333 | GET | `/payroll/run/{runId:long}/bank-files/{issueNo:int}` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 334 | GET | `/payroll/run/{runId:long}/bank-files/latest` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 335 | GET | `/payroll/runs/{month}` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 336 | GET | `/payroll/runs/{month}/register.csv` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 337 | GET | `/payroll/runs/{month}/statutory.csv` | yes | HR-006 | OWN ACC | no row | unchanged | Not in matrix |  |
| 338 | GET | `/payroll/staff` | yes | HR-004 · mask HR-001 HR-005 | OWN·O MGR·O REC·O CSH·O ACC·O SUP·O HKP·O MNT·O TAD·O † | no row | unchanged | Not in matrix |  |
| 339 | GET | `/payroll/staff/{staffId:long}/leave-balance` | yes | HR-001 | OWN MGR ACC TAD(V) | no row | unchanged | Not in matrix |  |
| 340 | GET | `/pos/checks` | yes | FOL-002 | OWN MGR REC CSH † | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 341 | GET | `/pos/checks/{orderId:long}` | yes | FOL-002 | OWN MGR REC CSH † | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 342 | GET | `/pos/checks/closed` | yes | FOL-002 | OWN MGR REC CSH † | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 343 | GET | `/pos/close-blockers` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-004: OWN MGR CSH ACC | Matrix says no but code grants (v2) | v2 id is my reading; borrowed id FOL-002 (v2 §1.2) |
| 344 | GET | `/pos/day-summary` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 345 | GET | `/pos/in-house` | yes | FOL-002 | OWN MGR REC CSH † | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 346 | GET | `/pos/kitchen` | yes | FOL-002 | OWN MGR REC CSH | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 347 | GET | `/pos/menu` | yes | FOL-002 | OWN MGR REC CSH † | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 348 | GET | `/pos/outlets` | yes | FOL-002 | OWN MGR REC CSH † | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 349 | GET | `/pos/till` | yes | FOL-002 | OWN MGR REC CSH † | no row | POS-004: OWN MGR CSH ACC | Matrix says no but code grants (v2) | v2 id is my reading; borrowed id FOL-002 (v2 §1.2) |
| 350 | GET | `/pos/void-requests` | yes | FOL-002 | OWN MGR REC CSH † | no row | POS-001: OWN MGR REC CSH ACC | Matrix says yes but code refuses (v2) | borrowed id FOL-002 (v2 §1.2) |
| 351 | GET | `/public/booking/deposit/{token}` | no | anonymous (public, rate-limited) | — | no row | — | Not in matrix | Anonymous: public booking listener, rate-limited |
| 352 | GET | `/public/booking/quote` | no | anonymous (public, rate-limited) | — | no row | — | Not in matrix | Anonymous: public booking listener, rate-limited |
| 353 | POST | `/public/booking/requests` | no | anonymous (public, rate-limited) | — | no row | — | Not in matrix | Anonymous: public booking listener, rate-limited |
| 354 | GET | `/reports/daily` | yes | RPT-002 | OWN MGR CSH(V)·S* ACC | OWN MGR CSH ACC | unchanged | Matrix says yes but code refuses | * scoped-only (CSH): passes only when the code names an own record/shift; A05-02: matrix gives the Cashier V·S; every RPT-002 gate refuses the Cashier |
| 355 | GET | `/reservations` | yes | RES-001 · mask GST-002 | OWN MGR REC † | OWN MGR REC | unchanged | Match | GST-002 = phone/e-mail reveal, not a gate |
| 356 | GET | `/reservations/{id:long}` | yes | RES-001 · mask GST-002 | OWN MGR REC † | OWN MGR REC | unchanged | Match | GST-002 = phone/e-mail reveal, not a gate |
| 357 | GET | `/room-blocks/history` | yes | RES-001 | OWN MGR REC | OWN MGR REC | unchanged | Match |  |
| 358 | GET | `/rooms/board` | yes | HK-001 | OWN MGR SUP HKP·A MNT·A † | OWN MGR SUP HKP MNT | unchanged | Match |  |
| 359 | GET | `/rooms/status` | yes | RES-001 | OWN MGR REC † | OWN MGR REC | unchanged | Match |  |
| 360 | POST | `/setup/SetupProperty` | no | loopback only | — | no row | — | Not in matrix | Anonymous: loopback-only (server PC) |
| 361 | GET | `/setup/status` | no | loopback only | — | no row | — | Not in matrix | Anonymous: loopback-only (server PC) |
| 362 | GET | `/shifts/{shiftId:long}` | yes | SHF-001 | OWN MGR REC·S CSH·S | OWN MGR REC CSH | unchanged | Match |  |
| 363 | GET | `/stock/alerts` | yes | RPT-002 / HK-003 [any] | OWN MGR CSH(V)·S* ACC SUP HKP·A | no row | STK-001: OWN MGR ACC SUP HKP | Match (v2) | OR: RPT-002 at property scope = money view, HK-003 any scope = work view (no money); * scoped-only (CSH): passes only when the code names an own record/shift; borrowed id RPT-002/HK-003 (v2 §1.2) |
| 364 | GET | `/stock/consumption` | yes | RPT-002 / HK-003 [any] | OWN MGR CSH(V)·S* ACC SUP HKP·A | no row | STK-001: OWN MGR ACC SUP HKP | Match (v2) | OR: RPT-002 at property scope = money view, HK-003 any scope = work view (no money); * scoped-only (CSH): passes only when the code names an own record/shift; borrowed id RPT-002/HK-003 (v2 §1.2) |
| 365 | GET | `/stock/counts` | yes | RPT-002 / HK-003 [any] | OWN MGR ACC SUP HKP·A † | no row | STK-001: OWN MGR ACC SUP HKP | Match (v2) | OR: RPT-002 at property scope = money view, HK-003 any scope = work view (no money); borrowed id RPT-002/HK-003 (v2 §1.2) |
| 366 | GET | `/stock/items` | yes | RPT-002 / HK-003 [any] | OWN MGR ACC SUP HKP·A † | no row | STK-001: OWN MGR ACC SUP HKP | Match (v2) | OR: RPT-002 at property scope = money view, HK-003 any scope = work view (no money); borrowed id RPT-002/HK-003 (v2 §1.2) |
| 367 | GET | `/stock/laundry` | yes | RPT-002 / HK-003 [any] | OWN MGR ACC SUP HKP·A † | no row | STK-001: OWN MGR ACC SUP HKP | Match (v2) | OR: RPT-002 at property scope = money view, HK-003 any scope = work view (no money); borrowed id RPT-002/HK-003 (v2 §1.2) |
| 368 | GET | `/stock/losses` | yes | RPT-002 / HK-003 [any] | OWN MGR ACC SUP HKP·A † | no row | STK-001: OWN MGR ACC SUP HKP | Match (v2) | OR: RPT-002 at property scope = money view, HK-003 any scope = work view (no money); borrowed id RPT-002/HK-003 (v2 §1.2) |
| 369 | GET | `/stock/moves` | yes | RPT-002 / HK-003 [any] | OWN MGR ACC SUP HKP·A † | no row | STK-001: OWN MGR ACC SUP HKP | Match (v2) | OR: RPT-002 at property scope = money view, HK-003 any scope = work view (no money); borrowed id RPT-002/HK-003 (v2 §1.2) |
| 370 | GET | `/stock/settings` | yes | RPT-002 / HK-003 [any] | OWN MGR ACC SUP HKP·A † | no row | STK-001: OWN MGR ACC SUP HKP | Match (v2) | OR: RPT-002 at property scope = money view, HK-003 any scope = work view (no money); borrowed id RPT-002/HK-003 (v2 §1.2) |
| 371 | GET | `/system/discovery` | no | anonymous | — | no row | — | Not in matrix | Anonymous: by design |
| 372 | GET | `/system/health` | no | anonymous | — | no row | — | Not in matrix | Anonymous: by design |
| 373 | GET | `/tax/gst` | yes | ADM-003:VIEW · mask ADM-003 | OWN TAD(V) | OWN TAD | unchanged | Match |  |
| 374 | GET | `/web-booking/requests` | yes | WEB-001 | OWN MGR REC † | no row | WEB-001: OWN MGR REC ACC | Matrix says yes but code refuses (v2) |  |
| 375 | GET | `/web-booking/requests/{id:long}` | yes | WEB-001 · mask GST-002 | OWN MGR REC † | no row | WEB-001: OWN MGR REC ACC | Matrix says yes but code refuses (v2) | GST-002 = phone/e-mail reveal, not a gate |
| 376 | GET | `/yield/calendar` | yes | RPT-001 | OWN MGR ACC † | no row | YLD-001: OWN MGR ACC | Match (v2) | borrowed id RPT-001 (v2 §1.2) |
| 377 | GET | `/yield/config` | yes | RPT-001 | OWN MGR ACC † | no row | YLD-001: OWN MGR ACC | Match (v2) | borrowed id RPT-001 (v2 §1.2) |
| 378 | GET | `/yield/proposals` | yes | RPT-001 | OWN MGR ACC † | no row | YLD-001: OWN MGR ACC | Match (v2) | borrowed id RPT-001 (v2 §1.2) |
| 379 | GET | `/yield/publishes` | yes | RPT-001 | OWN MGR ACC † | no row | YLD-001: OWN MGR ACC | Match (v2) | borrowed id RPT-001 (v2 §1.2) |
| 380 | GET | `/yield/quote` | yes | RPT-001 | OWN MGR ACC | no row | YLD-001: OWN MGR ACC | Match (v2) | borrowed id RPT-001 (v2 §1.2) |

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

The 7 added routes (found by `git log -S` on the endpoint files): `GET /cashier/folios  (PaymentsEndpoints.cs:23)`, `POST /commands/DecideCompanyLimit  (GroupsEndpoints.cs:61)`, `POST /commands/SaveCompany  (GroupsEndpoints.cs:57)`, `POST /commands/SetCompanyLimit  (GroupsEndpoints.cs:59)`, `GET /groups/companies  (GroupsEndpoints.cs:63)`, `GET /me/directory  (AccessEndpoints.cs:174)`, `GET /payroll/run/{runId:long}/bank-files/latest  (PayrollEndpoints.cs:90)`. They arrived in `8ba06a1` (bank-files/latest, 09:22), `f9337e6` (cashier/folios, 10:19), `e2f8690` (me/directory, 11:43) and `5f0f885` (company accounts, 11:54), all 26 Oct and all after `245a709` (08:53). Two static page routes (`MapMethods "/"` and `"/index.html"` in `WebAppHosting.cs:90-91`) serve the web build and are not API routes; they are not counted, nor were they in the review.

Permission column of the review vs mine: they agree on 364 of 373 rows. The 9 that differ:

| Review row | Route | Review lists | A05 derives | Reading |
|---|---|---|---|---|
| #39 | POST /commands/DecideApproval | APR-001, PAY-005 | APR-001 PAY-005 SHF-002 | transitive: shift-difference branch calls SHF-002 (CashierService); review omits it |
| #166 | POST /commands/TransitionHousekeepingTask | task work / repair work, HK-001, HK-002, HK-004 | HK-001 HK-002 HK-004 HK-003 | transitive: HK-003 for the "work" transitions; review writes "task work" in words |
| #174 | POST /commands/CommitImportBatch | commit permission per master (Owner-only masters), IMP-001 | IMP-001 IMP-002 IMP-003 | review says "commit permission per master"; A05 names IMP-002 / IMP-003 |
| #242 | GET /ota/messages | OTA-001 | OTA-001 GST-002 | GST-002 is the phone/e-mail mask (GuestContactMask), not a gate |
| #312 | GET /reservations | RES-001 | RES-001 GST-002 | same: GST-002 mask |
| #313 | GET /reservations/{id:long} | RES-001 | RES-001 GST-002 | same: GST-002 mask |
| #357 | GET /web-booking/requests/{id:long} | WEB-001 | WEB-001 GST-002 | same: GST-002 mask |
| #367 | POST /commands/ProposeYieldPublish | ADM-003, APR-001, RPT-001 | APR-001 RPT-001 | REVIEW IS WRONG: ProposeAsync checks RPT-001 + APR-001 only (YieldService.cs:264-266), no ADM-003 |
| #368 | POST /commands/DecideYieldProposal | ADM-003, RPT-001 | ADM-003 | REVIEW IS WRONG: DecideAsync checks ADM-003 only (YieldService.cs:295), no RPT-001 |

The review's statement F-I02 is confirmed for HEAD. Its follow-up (a test that enumerates `EndpointDataSource` and fails on a new anonymous route, review checklist item 14) is not present: no test in `08 Production\tests` mentions `EndpointDataSource` (A05-13); `A05_routes.mjs` is a stand-in.

## (c) Mechanical sweeps

### c1. Money fields reachable by Supervisor, Housekeeper and Maintenance

Seed facts (`evidence\A05\A05_seed_grants.csv`): SUP holds HK-001, HK-002, HK-003, HK-004, ROOM-001, ROOM-002 and HR-004·O; HKP holds HK-001·A, HK-003·A, ROOM-001·A, HR-004·O; MNT holds HK-001·A, ROOM-001·A, HR-004·O. **None of these is a money-flagged permission** (`is_money = 1`); the same holds for TAD. Counts of money-flagged grants per role: OWN 41, MGR 31, ACC 20, REC 18, CSH 16, SUP 0, HKP 0, MNT 0, TAD 0.

76 routes are reachable by at least one of the three roles (`A05_sweep_hk_mnt_money.json`). I walked every response DTO of those routes (`A05_dto.mjs`, 1,319 record types indexed) for money-like field names. 17 routes have such fields; 14 of them are nullable fields that the code nulls for these roles, 3 are the roles' own pay.

| Route | Roles of SUP/HKP/MNT that reach it | Money-like response fields (`?` = nullable, `!` = always present) | Why it is safe / not safe |
|---|---|---|---|
| POST /commands/AcknowledgeHandoverNote | SUP HKP MNT | Cash.AmountPaise? | HandoverViews.Note(seesCash): amount null unless PAY-001 or RPT-002 held (HandoverQueries.cs:22-24, HandoverViews.cs:35-39) |
| POST /commands/CompleteCarryItem | SUP HKP MNT | Cash.AmountPaise? | HandoverViews.Note(seesCash): amount null unless PAY-001 or RPT-002 held (HandoverQueries.cs:22-24, HandoverViews.cs:35-39) |
| POST /commands/StockLaundryReceive | SUP HKP | ReceiveLaundryResult.ChargePaise? | money = HasAsync(RPT-002, Property) (StockQueries.cs:22); cost fields `money ? x : null` (:72, :94-95; StockCommands.cs:234, :290) |
| POST /commands/SubmitHandoverNote | SUP HKP MNT | Cash.AmountPaise? | HandoverViews.Note(seesCash): amount null unless PAY-001 or RPT-002 held (HandoverQueries.cs:22-24, HandoverViews.cs:35-39) |
| GET /handover/notes | SUP HKP MNT | Cash.AmountPaise? | HandoverViews.Note(seesCash): amount null unless PAY-001 or RPT-002 held (HandoverQueries.cs:22-24, HandoverViews.cs:35-39) |
| GET /maintenance/amcs | SUP | Amcs.ValuePaise? | money = HasAsync(RPT-002, Property) (MaintenanceQueries.cs:30-31); cost fields `money ? x : null` (:56, :61, :102, :119, :139) |
| GET /maintenance/registers | SUP HKP MNT | Spares.CostPaise? | money = HasAsync(RPT-002, Property) (MaintenanceQueries.cs:30-31); cost fields `money ? x : null` (:56, :61, :102, :119, :139) |
| GET /maintenance/report | SUP | MaintenanceReport.MonthPartsCostPaise? | money = HasAsync(RPT-002, Property) (MaintenanceQueries.cs:30-31); cost fields `money ? x : null` (:56, :61, :102, :119, :139) |
| GET /maintenance/work-orders | SUP HKP MNT | Parts.UnitCostPaise? Rows.PartsCostPaise? | money = HasAsync(RPT-002, Property) (MaintenanceQueries.cs:30-31); cost fields `money ? x : null` (:56, :61, :102, :119, :139) |
| GET /maintenance/work-orders/{id:long} | SUP HKP MNT | Parts.UnitCostPaise? Order.PartsCostPaise? | money = HasAsync(RPT-002, Property) (MaintenanceQueries.cs:30-31); cost fields `money ? x : null` (:56, :61, :102, :119, :139) |
| GET /payroll/board | SUP HKP MNT | RateVersions.PfWageCap! Rates.PfWageCap! Lines.PayType! Lines.BasePaise! Lines.EarnedPaise! … (53 fields) | own record only (DEC-061): lines/payslips filtered to the caller, LOCKED runs only (PayrollQueries.cs:105-107, :250); not hotel money |
| GET /payroll/my/payslips | SUP HKP MNT | Line.PayType! Line.EarnedPaise! Line.BasicPaise! Line.HraPaise! Line.WashingPaise! … (19 fields) | own record only (DEC-061): lines/payslips filtered to the caller, LOCKED runs only (PayrollQueries.cs:105-107, :250); not hotel money |
| GET /payroll/payslips/{runId:long}/{staffId:long} | SUP HKP MNT | Line.PayType! Line.EarnedPaise! Line.BasicPaise! Line.HraPaise! Line.WashingPaise! … (19 fields) | own record only (DEC-061): lines/payslips filtered to the caller, LOCKED runs only (PayrollQueries.cs:105-107, :250); not hotel money |
| GET /stock/counts | SUP HKP | Rows.VarianceValuePaise? | money = HasAsync(RPT-002, Property) (StockQueries.cs:22); cost fields `money ? x : null` (:72, :94-95; StockCommands.cs:234, :290) |
| GET /stock/items | SUP HKP | Rows.UnitCostPaise? Rows.ValuePaise? Rows.LaundryRatePaise? StockItemsPage.TotalValuePaise? | money = HasAsync(RPT-002, Property) (StockQueries.cs:22); cost fields `money ? x : null` (:72, :94-95; StockCommands.cs:234, :290) |
| GET /stock/laundry | SUP HKP | Rows.ChargePaise? | money = HasAsync(RPT-002, Property) (StockQueries.cs:22); cost fields `money ? x : null` (:72, :94-95; StockCommands.cs:234, :290) |
| GET /stock/losses | SUP HKP | Rows.ValuePaise? LossesPage.TotalValuePaise? | money = HasAsync(RPT-002, Property) (StockQueries.cs:22); cost fields `money ? x : null` (:72, :94-95; StockCommands.cs:234, :290) |

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
