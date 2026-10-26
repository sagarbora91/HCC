# Masters wave — merge report (MA-1 … MA-7 + main / Wave B)

26 Oct 2026 · Integration lead: Claude (Opus) · Branch `claude/masters-integration` (worktree `E:\Projects\HCC\worktrees\masters-integration`), based on `main` b16e260, then `main` 074d87c (Wave B) merged on top. Not pushed.

## 1. What merged, in order

| # | Branch (tip) | Merge commit | Prototype tests after | Conflicts and how they were resolved |
|---|---|---|---|---|
| 1 | `claude/ma1-rooms` (4e6789f) — rooms, room types, amenities, occupancy charges; migration 2810; module RoomMasters | 7d769fb | 1069 → **1107** (+38) | none (first merge) |
| 2 | `claude/ma3-money` (06e68c5) — payment methods, bank accounts, expense categories / vendors; 2830; MoneyMasters | 3c66897 | → **1148** (+41) | Append-only files kept both sides (Decision Register, `sync/reads.ts`, `sync/registry.ts`, `selectors.ts` imports, mock `index.ts`, `_role-reads.json`, `sync-roles.mjs`, `Hcc.slnx`, `HccComposition.cs`). `sync/masters.ts`: MA-1 and MA-3 hooks side by side. Test interaction: once several masters exist, the masters loader reads every list, so `tests/_sync-harness.mjs` now answers every `MST_SERVER` list/history path with an empty page by default (a test's own routes win); `sync-money-master` pins only its own `mastersX` keys (old: the exact key list). |
| 3 | `claude/ma2-catalogue` (d5de601) — meal plans, extras, discount reasons; 2820; Catalogue | d38c6b1 | → **1180** (+32) | Append-only files both sides (handlers registry keeps `catalogue` last — it wraps folio APPROVAL). `sync/masters.ts` gains `MST_DELEGATES`. Real interaction: MA-2's "room types allowed" switches to MA-1's `roomTypes` master, which stores codes (`DELUXE`) while rooms keep the type key (`Deluxe`) — `catPlanAllowsType` now matches the master row by key, code or name; the `[CAT]` test saves the master code. Registration regexes (`m_admin,catalogue]`, `m_admin,roommasters]`) accept other masters on the registry line. |
| 4 | `claude/ma4-pos` (153281a) — outlets, tables, menu; 2840; POS extensions | 967ea89 | → **1215** (+35) | POS settle keeps MA-4's GST profile (`posCheckRate`) **and** MA-3's payment-method master (method lookup, retired refusal, drawer by settles-to). `sync/masters.ts` gains MA-4's optional `history`, `toBody`, `loadedElsewhere`, `reload`; MA-3's own loader follows the optional history route. |
| 5 | `claude/ma5-frontdesk` (8bca425) — booking sources, ID types, numbering series, policies; 2850; FrontDeskMasters | b515d8e | → **1249** (+34) | Folio handler keeps MA-3 methods and MA-5 numbering (`fdmAllocate` for INV / CRN). `toBody` became `(row, c)` for MA-4 and MA-5 alike. `sync-frontdesk-master` pins its four lists + "no history read" instead of the exact `/masters/*` request list. |
| 6 | `claude/ma6-people` (1234537) — designations, shift patterns, leave types, holidays; 2860; PeopleLists | 21653fd | → **1290** (+41) | MA-6's four routes are inline in `MST_SERVER`. `sync-accounting` serves the prototype holiday calendar from its fake `/hr/holidays` so compliance due dates compare like for like (with the new harness default the server would otherwise answer "no holidays"). |
| 7 | `claude/ma7-ops` (4896fc5) — HK task types, zones, complaint / lost-found categories; 2870–2871; OpsMasters | 197a46d | → **1332** (+42) | `sync-ops-masters` uses an unknown list as its "unmapped master" example (MA-1 maps `roomTypes` now); `stream-frontdesk-master` pins its four specs first in the shared Front-desk group (MA-7's categories follow). MA-7's `FieldSpec` additions (`optionsFrom`, `multiline`, `withCurrent`, textarea) and MA-1's per-master tools slot / hide-Add / no-reorder flags merged without conflict and both work. |
| 8 | Unify `sync/masters.ts` | 9053ebc | 1332 | One `MstServerRoute` hook set (section 2). |
| 9 | SCHEMA 8 → 9; migrations in order | 506fc80 | **1333** (+1 `[S9]`) | v8 saves migrate (no reset); hash pins recomputed — data unchanged, verified by reproducing the old hashes with `schema = 8`: BASE `d885a20a…` → `add626d3…`, FY26 history `ba4a1d4e…` → `35e63082…`, FY26 scenario `260433ce…` → `07f6681c…`. `Hcc.slnx` lists 2800–2871 in order after 2701 and before 2900 (3800 added after 3701 with the main merge). `_role-reads.json` checked against the real server (`HCC_UPDATE_ROLE_READS=1`, `RoleReadMatrixTests`): no drift. |
| 10 | `main` 074d87c (Wave B, DEC-070…087) | 84b6fdb | 1165 + MA → **1429** | See section 3 — 40 conflicted files, several real overlaps. |

Merge order used: MA-1, MA-3, MA-2, MA-4, MA-5, MA-6, MA-7 (as briefed), then main. After every merge: `npx tsc --noEmit`, `npm test` (full, ~1 min) and `dotnet build Hcc.slnx` (0 warnings) — all green before each commit.

## 2. The unified `MstServerRoute` hook set (`03 Prototype/src/clients/sync/masters.ts`)

One name per concept, superset of behaviour; a master with no hooks keeps the Floors shape.

| Hook | Meaning | Was (per agent) |
|---|---|---|
| `history?`, `historyPath?` | optional history route (none = no history read) | MA-4 made them optional |
| `fromServer(x)` | server list row → prototype `MasterRow` (default: floor mapper) | MA-1/4/5/6 `fromServer`, MA-7 `fromWire` (adapted with a typed `wire()` wrapper) |
| `toBody(row, c)` | extra Save body fields beyond code / name / sortOrder / id (`c` for lookups) | MA-1 `body`, MA-3 `toServer`, MA-4 `toBody(c,row)` (args swapped), MA-5/7 `toBody(row)`, MA-6 `saveFields` |
| `noBase` | `toBody` is the whole body (rooms send `roomNo`, no code/name/sort) | MA-1 |
| `rowId(id, c)` | server id of a prototype row id; `undefined` = not on the server yet → Save creates, Reorder skips, Retire/Reactivate/Delete refuse | MA-1 `serverId(c,id)` (throws itself), MA-5 `rowId(id)` |
| `ownLoader` | rows **and** history come from the owning area's loader (or another slice); `loadMasters` skips the master | MA-1 `noList` (rooms = `s.rooms`), MA-3 `ownLoader` |
| `loadedElsewhere` | rows come from another area's slice, history is still read here | MA-4 (outlets / tables / menu items in `posX`) |
| `reload` | slices reloaded after a command besides `mastersX` | MA-1 `reloadAlso`, MA-4 `reload` |
| `refuse` | operations refused before any network call, with the reason | MA-1 |
| `precheckSave(row)` | Save refused before any network call when it returns a reason | MA-1 |
| `MST_DELEGATES` (map) | masters whose commands another area sends entirely (patch loader on `mastersX`) | MA-2 (`CAT_MST_DELEGATES`; its private route type also renamed `body` → `toBody`) |

Areas adapted: `roommasters.ts`, `money-master.ts`, `pos-masters.ts`, `frontdesk-master.ts`, `ops-masters.ts`, the inline MA-6 people lists, `catalogue.ts`. `validateAreas()` passes (no two owners of a slice, no two mappers of one command type); `MST_SERVER` is exported from `sync/index.ts` for the test harness.

## 3. Merging main (Wave B) — real overlaps and the decisions taken

- **Holidays — one concept, MA-6's Holiday calendar master is the source of truth.**
  - Server: Wave B's `3400` created `hr.holiday (property, date, name)` and MA-6's `2860` created `hr.holiday (holiday_id, code = date, kind paid|restricted, sort, retire, row version)`. The table moved out of `2860` into a new **`3800_holiday_calendar.sql`** (runs after 3400): it copies Wave B's rows aside, rebuilds `hr.holiday` in the master shape and re-inserts them as active paid holidays. Payroll (`SqlPayrollStore.DaysAsync`, `HolidaysAsync`), leave days and compliance due dates all read the active `paid` rows. Wave B's `3400` is unchanged (no checksum change for databases built from main).
  - Wave B's server command `SaveHoliday` clashed with MA-6's master command of the same name (same route `POST /commands/SaveHoliday`): Wave B's is now **`SavePaidHoliday`** (`SavePaidHolidayRequest`), `RemoveHoliday` unchanged; both write the master table and keep Wave B's locked-pay-month guard. `StaffDays.Holidays` (date → name) is the single set; `PaidHolidays` is its dates (MA-6 name kept).
  - Prototype: `HR_HOLIDAY_SAVE` / `_REMOVE` write the master rows (with a master history line); `hrPaidHolidays` / `hrPaidHolidayOn` give Wave B's `{date, name}` view; MA-6's `hrHolidays` / `hrHolidayOn` / `hrHolidayPaid` stay; Wave B's own `hrHolidays` / `hrHolidayOn` / `HRHoliday` were removed (name clashes). The SCHEMA 8 → 9 migration moves a Wave B save's `staffX.holidays` into the master. The Leave › Holidays card links to the full calendar (`MstLink holidays`); the production sync reloads `mastersX` after a paid-holiday command.
  - **DEC-083 is binding over MA-6's draft:** an unworked holiday is **not** paid to daily-wage staff (MA-6 paid it) — changed on the server (`PayrollRules`) and in the prototype (`hrComputeLine`); MA-6's `[PPL]` test now expects 0 extra payable days (old: +1).
  - Payroll golden figures: unchanged (Wave B's "₹30,000 monthly / ₹800 per day" holiday test passes; FY26 / BASE hashes changed only by the SCHEMA field).
- **Leave types:** Wave B's past-attendance corrections use MA-6's leave-type list (`IsKnown` / `IsPaid` / `IsTracked`); `DayEntry.LeaveLabel` switched to codes (MA-6 made `LeaveType` a struct) and prints a custom code as itself.
- **POS:** settle = MA-3 method master + MA-4 GST profile + Wave B RST tax invoice and B2B buyer; the void's credit note comes from MA-5's numbering series (`fdmAllocate('CRN')`, one credit-note counter). Server `SaveMenuItem`: the MA-4 menu master for everyone, except a request-only caller (Manager, `PERM-POS-006` R) whose save becomes Wave B's Owner-decided request; `SaveMenuItemRequest` carries both MA-4's fields and Wave B's `Reason`. POS lists (`/pos/outlets`, `/pos/menu`, `/pos/menu-categories`) are readable with the role-matrix-v2 view right `PERM-POS-001` (any mode) or `PERM-MST-001` (MA-4 had keyed them on the old operate id). Restaurant role: open checks only (Wave B), no day sales, the masters "Manage list…" links stay.
- **Invoices:** Wave B's `IssueCoreAsync` (RST series inside the settle transaction) with MA-5's numbering-series lookup (prefix, start, padding from the master; built-in numbering when no row).
- **Cash:** MA-3's settles-to-till test everywhere (drawer, closing movements, cash book) plus Wave B's cash-out movements (bank deposit, till transfer, Owner drawing); the folio tender check = MA-3 method + Wave B own-shift rule (DEC-078).
- **RBAC:** `_role-reads.json` regenerated from the merged server; the new **Restaurant (RST)** role has **no masters grants** (PERM-MST-001: OWN, TAD; PERM-MST-002: OWN) and is in every role matrix test.
- Server fixes found by the full serial run of the merged result: Wave B's reserved-prefix rule (RST / BOS / CN may not be the tax-invoice prefix) was applied by MA-5's series lookup to **every** series, refusing every credit note and RST invoice — `InvoiceNumbering.ValidatePrefix(prefix, type)` now applies the reserved names to the tax-invoice series only (shape rule for all); Wave B's day-entry parser and request store used `Enum.Parse<LeaveType>` (MA-6 made it a struct) — they now accept any leave-type code (the service checks the type is known and active); `CorrectionRulesTests` uses `X-1` as its invalid code (`X` is a valid custom code now); MA-4's `PosMastersTests` Tech admin is user id 8 (Wave B's harness seeds the Restaurant waiter as 7); MA-6's people harness names `DesignationCode:` (Wave B inserted `BankReason` before it); MA-6's kernel holiday test expects 0 extra payable days for daily wage (DEC-083).
- Wave B test adjustments (old → new): the paid-holiday count reads "seeded paid + 2" (MA-6's demo calendar seeds 6 paid holidays in FY 2026-27; old "2 in FY …" / "None yet"); the not-found removal uses 3 Oct (2 Oct is seeded); `PosRbacV2Tests` names its `Reason:` argument.

## 4. Final verification (merged result)

| Check | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` | **1429 / 1429** (main 1165 + MA-1…MA-7 263 + `[S9]` 1). Branch-only before main: 1333 (1069 + 263 + 1). |
| `npm run build`, `npm run build:production` | OK (2673 KB / 2914 KB) |
| `dotnet build Hcc.slnx --no-incremental` | 0 warnings, 0 errors |
| `dotnet test Hcc.slnx -maxcpucount:1` (`.\SQLEXPRESS`, disposable `HccTest_*`) | **1256 / 1256** in 35 test projects (main 1098 + 158 from the seven MA test projects and MA additions). The first full run had 23 failures in 5 projects (merge interactions, fixed — section 3); the failed projects then reran green and a second full serial run was all green. The Integration `EndToEndTests` room-105 housekeeping count passed both times. |

Legitimately changed figures: SCHEMA 8 → 9 hash pins (above); MA-6 daily-wage holiday pay +1 day → 0 (DEC-083); Wave B holiday count text (seeded calendar); test fixtures that pinned an exact list of masters now pin their own.

## 5. Needed but could not change (agents' reports) — input for MA-8 … MA-11

- **MA-2:** the server night run should call `PostMealPlanNights` (plan lines are posted by the prototype's night posting only); the "bundled with room" plan slab (GST of a bundled plan follows the room slab — illustrative, CA to confirm) is not on the server.
- **MA-3:** `BillingRules.Tenders.Validate` and `ExpenseRules.ExpenseMethods` must read `mst.payment_method` (they still use the fixed lists); the CHECK constraints on `ops.expense`, `pos_bill`, `pos_receipt` method columns must be relaxed for new methods; `cash_movement` is keyed on the literal "Cash"; `ChartOfAccounts.Effective` should take the method / category ledgers.
- **MA-5:** the receipt, KOT and registration-card series are defined but unused; server `BOOK` does not reject a retired booking source.
- **MA-6:** `FieldSpec` lacks date / time types and dynamic options (MA-7 added `optionsFrom`, which covers the dynamic part — date / time inputs still missing).
- **MA-1:** `Room.floor` is numeric, so floor codes must be `F<n>`; occupancy charges post as a plain service charge — now that MA-2's extras catalogue is merged they should post through it.
- **MA-4:** modifiers are not built; the POS import still writes `category` / `price_paise` (MA-8 to move it onto categories and dated prices).

## 6. Known gaps found during the merge

1. **Holiday calendar lock:** MA-6's master commands (`SaveHoliday`, `RetireHoliday`, `DeleteHoliday`) do not check a locked pay month; Wave B's `SavePaidHoliday` / `RemoveHoliday` do. Add the guard to the master commands (MA-11).
2. **Seeded holidays:** the prototype demo fixture seeds MA-6's illustrative calendar (8 rows, 6 paid in FY 2026-27); DEC-083 and both servers seed none. Owner question: keep the demo calendar or start empty.
3. **RST series** (Wave B restaurant invoices) is not a row of MA-5's numbering-series master; it uses its own built-in numbering.
4. **Manager menu requests** (Wave B) carry only the W2-6 fields; on approval `DecideMenuChange` saves through the W2-6 store path (dated price kept correct, but MA-4 fields such as GST class or station cannot be requested).
5. **Migration order on old databases:** a database that already applied main's 3400 has no 2860 yet; the runner must apply the masters migrations (2801–2871) before 3800 there. Fresh and disposable test databases are fine; no production database exists.
6. The production payroll board still fills `staffX.holidays` (legacy, unread by the domain); the domain reads `mastersX.holidays`.
7. The masters loader's required `reads` include MA-7's four lists (all `always`, so harmless); optional lists are gated inside with `mayRead`.
8. MA-9 should fold the per-agent seeds into one `masters-seed.ts` and add the "legacy constants" grep (`EXPENSE_CATEGORIES`, the payment arrays, `POS_TENDERS`, `FOLIO_TENDERS`, `adminRoomTypes`).
