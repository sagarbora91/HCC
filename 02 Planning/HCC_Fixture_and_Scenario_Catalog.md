# Hotel Control Centre — Fixture and Scenario Catalogue v1

Version: 1.0 (M0) · 24 October 2026
All names, phone numbers, emails, IDs, receipts and companies are **fictional** (`example.test` domain, `DEMO-` references). No real guest or staff data is used or requested.

## 1. Deterministic clock

| Item | Value |
|---|---|
| Timezone | Asia/Kolkata |
| Starting business date | 24 Oct 2026 (Saturday) |
| Simulated clock start | 24 Oct 2026 08:00 IST; advances +1 minute per command, jumps to 08:00 on "Open next day" |
| Real clock | Never used for state or audit (DEC-030 — current build uses real time for audit, to fix) |
| Previous date | 23 Oct 2026 recorded as closed (proposed fixture fix: the build has 23 Oct lines but no closing record) |

## 2. Base property fixture (current seed, from `src/domain.ts`)

Property: "Saagar Residency" in the current build → proposed "HCC Demo Residency" (DEC-037). 24 rooms, 3 floors, 3 types. Opening float ₹5,000. Synthetic tax 10%. Discount approval threshold ₹250. Check-in 14:00 / checkout 11:00. Inspection ON.

| Type | Rooms | Rate/night | Count |
|---|---|---:|---:|
| Classic | 101, 102, 105, 108, 201, 202, 205, 208, 301, 302, 305, 308 | ₹2,000 | 12 |
| Deluxe | 103, 104, 107, 203, 204, 207, 303, 304, 307 | ₹3,000 | 9 |
| Suite | 106, 206, 306 | ₹4,500 | 3 |

### 2.1 Room state at 24 Oct 08:00

| Room | Occupancy | Cleaning | Block | Booking |
|---|---|---|---|---|
| 101 | In house — Ananya Rao | Ready | — | HCC-1041 |
| 102 | In house — Vikram Shah | Ready | — | HCC-1042 |
| 103 | Arrival today — Ishaan Bose | Ready | — | HCC-1049 |
| 104 | In house — Riya Mehta | Ready | — | HCC-1043 |
| 105 | Vacant | Ready | — | — (walkthrough room) |
| 106, 107 | Vacant | Ready | — | — |
| 108 | Vacant | **Dirty** (task → Ravi) | — | — |
| 201 | In house — Arjun Kapoor | Ready | — | HCC-1044 |
| 202 | In house — Nisha Desai | Ready | — | HCC-1045 |
| 203, 205 | Vacant | Ready | — | — |
| 204 | Vacant | **Dirty** (task → Kavya) | — | — |
| 206 | In house — Kabir Sen | Ready | — | HCC-1046 |
| 207 | Arrival today — Aditi Nair | Ready | — | HCC-1050 |
| 208 | Vacant | **Dirty** (task → Ravi) | — | — |
| 301 | In house — Tara Iyer | Ready | — | HCC-1047 |
| 302 | In house — Dev Malhotra | Ready | — | HCC-1048 |
| 303 | Vacant | **Awaiting inspection** (Kavya) | — | — |
| 304, 306, 307 | Vacant | Ready | — | — |
| 305 | Vacant | **Cleaning** (Ravi) | — | — |
| 308 | Vacant | Ready | **Blocked** (repair) | — |

All in-house stays: arrival 23 Oct, departure 26 Oct, night of 23 Oct posted, one payment equal to one night's rate received 24 Oct. Arrivals: ₹1,000 advance each. Cash receipts: HCC-1041, -1044, -1047 (₹2,000 each) and HCC-1050 (₹1,000); the rest UPI.

### 2.2 Users

| ID | Display | Role | Enabled |
|---|---|---|---|
| owner | Sagar · Owner | Owner | Yes |
| manager | Meera · Manager | Manager | Yes |
| reception | Aarav · Reception | Reception | Yes |
| cashier | Priya · Cashier | Cashier | Yes |
| supervisor | Neha · Supervisor | HK supervisor | Yes |
| housekeeper | Ravi · Housekeeper | Housekeeper | Yes |
| housekeeper2 | Kavya · Housekeeper | Housekeeper | Yes |
| accountant | Farhan · Accountant | Accountant | Yes — **to add** |
| maint | Joseph · Maintenance | Maintenance | Yes — **to add** |
| techadmin | Lata · Tech admin | Technical administrator | Yes — **to add** |

### 2.3 Expenses

| ID | Vendor | Amount | Funding | Status |
|---|---|---:|---|---|
| EXP-101 | Fresh Linen Services | ₹1,800 | Cash | Posted |
| EXP-102 | Morning Supplies | ₹850 | UPI | Pending (submitted by reception) |

### 2.4 Expected derived figures at start (oracle — AC-024)

Verified by running the current domain code, 24 Oct 2026:

| Figure | Value | Derivation |
|---|---:|---|
| Occupied | 8 | in-house stays |
| Sellable | 23 | 24 − 308 blocked |
| Available (vacant + ready + unblocked) | 10 | |
| Occupancy | 35% (34.8%) | 8 / 23 |
| Today's receipts (payments) | ₹21,500 | ₹19,500 in-house + ₹2,000 advances |
| Outstanding balances | ₹1,950 | 10% synthetic tax on each 23 Oct night |
| Posted expenses today | ₹1,800 | EXP-101 |
| Expected drawer | **₹10,750** | ₹5,000 + ₹7,000 cash receipts + ₹550 restaurant cash (POS-F01, ₹500 + ₹50 tax) − ₹1,800 cash expense |
| Restaurant (POS) today | ₹1,298 settled · 2 checks open | POS-F01 ₹550 cash (in the drawer) · POS-F02 ₹748 UPI (non-cash) · POS-F03 table T2 and POS-F04 room 201 open — they block day close until settled, charged to the room or voided |

**Figure change (24 Oct 2026, DEC-052 shared till, W0-B):** expected drawer **₹10,200 → ₹10,750** = ₹10,200 + ₹550 POS-F01 cash breakfast, which now counts once in the front-desk drawer. No fixture record changed; only the drawer formula (Financial Rulebook §4 + POS cash sales − same-day POS cash voids).

**Historical note (fixed in BL-001):** the test "Fixture totals are derived, not hardcoded" expects ₹10,700. The derivation above gives ₹10,200, so the **test oracle is wrong**, not the domain. (₹10,700 is the drawer after the walkthrough's ₹500 cash deposit.) Fix in backlog BL-001.

## 3. Scenarios

| ID | Name | Purpose | Delta from base |
|---|---|---|---|
| S0 | Owner walkthrough | 12-step journey (§4) across 24–26 Oct | In-house departures moved to 28 Oct; arrivals HCC-1049/1050 moved to 27–29 Oct, so day closes are not blocked by unrelated stays; 23 Oct closed |
| S1 | Empty hotel | Empty states on every screen | No bookings, tasks, expenses; all rooms Ready |
| S2 | Room 105 blocked | Exception path for check-in/booking | Room 105 blocked (AC unit fault, 24–25 Oct), 108 dirty with an arrival assigned today |
| S3 | Exceptions day | Denials and recovery | Overlap attempt on 202; unpaid departure due (HCC-1042 departs 24 Oct, balance ₹200); pending discount APR then folio change (stale); expired hold on 106; cash count short by ₹100 |
| S4 | Company credit | Authorised credit checkout | Stay for "Demo Industries Pvt Ltd (fictional)" GSTIN-format placeholder `27AAAAA0000A1Z5`, balance ₹6,600 on company credit with manager approval |
| S5 | Night-audit interruption | Resume without reposting | Close stopped after posting 3 of 8 nights (`progress: posted [1041,1042,1043]`) |
| S6 | Access policy | Policy edits and protections | Second owner account disabled; Reception granted refund.request with expiry tomorrow; conflicting deny on Cashier report.export |

Fixture keys (room IDs, booking IDs HCC-1041…1050, user IDs, EXP-101/102) are stable across scenarios. Scenario state is explicit; no scenario relies on another having run.

## 4. Owner walkthrough script (S0) — expected results

Recommended multi-day version (DEC-029 option B). New booking ID in the current build is HCC-1101 (sequence-dependent; the script refers to it as "the Room 105 booking").

| Step | Business date | Actor | Actions | Expected result |
|---|---|---|---|---|
| 1 | 24 Oct | Owner | Open Overview; review room board, cash card | 8 occupied, 23 sellable, drawer ₹10,750 (was ₹10,200 before DEC-052), outstanding ₹1,950 |
| 2 | 24 Oct | Reception | Calendar → Room 105, 24→26 Oct, 2 guests, source Phone; record advance ₹1,000 UPI `DEMO-UPI-0001` | Booking Confirmed; folio balance −₹1,000; drawer unchanged |
| 3 | 24 Oct | Reception | Guest profile "Rahul Verma (fictional)" → register → check in | Room 105 Occupied; arrivals list updated; audit entries |
| 3b | 24 Oct | Cashier | Record refundable deposit ₹500 cash | Deposit held ₹500; balance still −₹1,000; drawer ₹11,250 (was ₹10,700) |
| 4a | 24 Oct | Manager | Approve EXP-102 (UPI); settle the open restaurant checks (POS-F03 table T2, POS-F04 room 201 — UPI, or charge 201 to the room); close cash shift counting ₹11,250; night audit posts 24 Oct; close day; open 25 Oct | 105 folio balance ₹1,200; 24 Oct closed; opening float ₹11,250 (was ₹10,700) |
| 4b | 25 Oct | Reception | Add Laundry service ₹500; request discount ₹500 "Service delay goodwill" | Discount above ₹250 → APR pending; balance ₹1,750 |
| 5 | 25 Oct | Reception → Manager | Reception tries to approve → **denied**; Housekeeper tries to record payment → **denied**; Manager approves | Discount line −₹500 (tax −₹50); balance ₹1,200 |
| 5b | 25 Oct | Manager | Night audit posts 25 Oct; shift close ₹11,250 (was ₹10,700); close day; open 26 Oct | 105 balance ₹3,400 — total ₹4,400 (net ₹4,000 + tax ₹400) |
| 6 | 26 Oct | Cashier | Settle ₹2,000 cash + ₹1,400 UPI `DEMO-UPI-0002`; return deposit ₹500 cash | Balance ₹0; deposit ₹0; drawer ₹12,750 (was ₹12,200) |
| 7 | 26 Oct | Reception | Checkout; print sample invoice | Stay CheckedOut; room 105 Dirty; exactly one HK task (Ravi); invoice ₹4,400 |
| 8 | 26 Oct | Housekeeper → Supervisor | Ravi starts, ticks 3 items, completes; Neha inspects | Room 105 Ready |
| 9 | 26 Oct | Cashier → Manager | Expense "Aqua Water Cans" ₹300 cash; manager approves/posts | Drawer ₹12,450 (was ₹11,900) |
| 10 | 26 Oct | Manager | Night audit 26 Oct; shift close counting ₹12,450 (was ₹11,900); close day; rerun close/post | Day closed; rerun → "already closed", totals unchanged |
| 11 | 26 Oct | Owner | Reports → drill from receipts, tax, deposits, expenses to the Room 105 records | Room 105 shows ₹4,000 net / ₹400 tax; deposit in ₹500 / out ₹500; expense ₹300 |
| 12 | 26 Oct | Owner | Remove a permission from Reception; disable user Aarav; switch to Aarav and try a booking | Next command → "User disabled"; audit shows policy change and denial |

Verification: steps 1–12 (except UI-only steps: print, drill-down, role-permission edit) were executed against the current domain code on 24 Oct 2026 and produced these values. Evidence: `05 Delivery Evidence/M0/m0_walkthrough_sim_output.txt`.

Findings from that run, fed into the backlog:
- Cashier cannot close their own shift in the current build (shift close requires `close`); the manager did it in the run. Proposed: cashier counts/submits, manager approves (WF-SHIFT-001).
- Reception's self-approval was denied for lacking `approve` permission, so the self-approval rule itself was not exercised in this run; a Manager-requests/Manager-approves case is needed (covered by an existing unit test).
- Day close on 24 Oct needed EXP-102 resolved first; the walkthrough includes that explicitly.

### 4.1 Alternative single-day version (DEC-029 option A)

Steps run on 24 Oct with a "post full stay" action. Not recommended: it posts future nights and then checks the guest out two days early, which does not show night-audit behaviour.

## 5. Masters in the fixtures (MA-9, 27 Oct 2026)

Every master of the masters wave (`src/domain/streams/masters.ts` framework; plan `HCC_Customisation_Features_Plan_v1.md` Part 2) starts from the **one seed module `03 Prototype/src/fixtures/ext/masters-seed.ts`**. A master's rows live in `s.mastersX.rows[<id>]` only after its first saved change; before that (BASE, every scenario, FY26, old saves) the rows are the seed, read by the master's `seed` hook (`s=>mastersSeed('<id>',s)`). The seed reproduces what the prototype hard-coded or derived before the wave, so **no figure moves**: BASE is never edited (it carries no `mastersX`), FY26 = BASE + history, and the FY26 determinism hashes do not read the seed. Seed rows are always active, have unique codes and ascending unique sort orders; a retired row is never seeded.

### 5.1 Seed rows per master (BASE and every scenario unless §5.2 says otherwise)

| Master (id) | Rows | Seed values (code → name) | Derived from |
|---|---:|---|---|
| Floors (`floors`) | 3 | F1 Floor 1 · F2 Floor 2 · F3 Floor 3 | the rooms' `floor` numbers |
| Room types (`roomTypes`) | 3 | CLASSIC · DELUXE · SUITE (bed, size, sleeps, amenities) | the rooms' `type` keys (Classic, Deluxe, Suite first) |
| Rooms (`rooms`) | 24 | the fixture rooms themselves (no seed) | `s.rooms` |
| Amenities (`amenities`) | 9 | WIFI TEA AC TV DESK VIEW LIVING BATH BFAST | constants |
| Occupancy charges (`occupancyCharges`) | 3 | X-CLASSIC X-DELUXE X-SUITE: extra adult ₹800, child ₹400, bed ₹1,000 (not Classic), child age 6–12, from 2025-04-01 | one per room type; illustrative |
| Meal plans (`mealPlans`) | 4 | EP ₹0 · CP ₹300 (bundled) · MAP ₹750 · AP ₹1,200 | constants |
| Extras (`extras`) | 8 | LAUNDRY ₹500 · DRYCLEAN · XBED · PICKUP · DROP · MINIBAR · LATECO · HALL | constants (LAUNDRY ₹500 = the worked ₹4,265 journey) |
| Discount reasons (`discountReasons`) | 5 | SERVICE CORP REPEAT LONGSTAY MGMT | constants |
| Payment methods (`paymentMethods`) | 6 | CASH → TILL · UPI · CARD · BANK → BANK-1 · VCREDIT · COMPANY | the old front-desk, POS and expense tender lists + Company credit |
| Bank accounts (`bankAccounts`) | 2 | TILL (Front-desk cash drawer) · BANK-1 (Demo Bank, fictional) | constants |
| Expense categories (`expenseCategories`) | 5 | SUPPLIES LAUNDRY MAINT UTIL PETTY | the old `EXPENSE_CATEGORIES` |
| Expense vendors (`expenseVendors`) | 2 / 0 / 3 / 16 | `V-<WORDS>` per distinct free-text vendor on `s.expenses` | BASE 2, S1 0, ACC 3, FY26 16 |
| Menu categories (`menuCategories`) | 6 | BEVERAGES BREAKFAST STARTERS MAINS BREADS-RICE DESSERTS | the menu items' category text, first appearance |
| Outlets, tables, menu items (`outlets`, `posTables`, `menuItems`) | — | the fixture's own POS data (no seed) | `s.posX` |
| Booking sources (`bookingSources`) | 8 + auto | WALKIN PHONE WHATSAPP DIRECT WEBSITE OTA AGENT CORPORATE; OTA channels and company accounts add their own rows (BASE and FY26 15 rows in all, S4 16) | the forms' source list + channels + companies |
| ID types (`idTypes`) | 7 | AADHAAR PASSPORT DL VOTER PAN OTHER VISA | the guest ID list + foreigner-only passport and visa |
| Number series (`numberSeries`) | 6 | INV (DEMO) · BOS · CRN (DEMO-CN) · RCP · KOT · REG | the numbers the prototype prints; INV / CRN continue `invoiceSeqC` / `creditNoteSeqC` in FY 2026-27 |
| Policies (`policies`) | 1 | DEFAULT (free cancellation up to arrival, no charges, no advance) | today's global behaviour |
| Designations (`designations`) | 11 | one per distinct designation of the staff master (HOTEL-MANAGER … SECURITY-GUARD-NIGHT) | `s.staffX.staff` |
| Shift patterns (`shiftPatterns`) | 4 | M 07–15 · E 15–23 · N 23–07 · G 09–18 | the staff roster constants |
| Leave types (`leaveTypes`) | 6 | CL 12 · SL 7 · EL 15 · CO · ML · LOP | the staff leave policy |
| Holiday calendar (`holidays`) | 8 | **demo data**, see §5.3 | constants |
| Housekeeping task types (`hkTaskTypes`) | 4 | DEP 40 min inspected · STAY 20 · DEEP 90 · INSP 10 | today's housekeeping behaviour |
| Housekeeping zones (`hkZones`) | 3 | Z1 Z2 Z3, one per floor, round-robin attendant | the rooms' floors + enabled housekeepers |
| Complaint categories (`complaintCategories`) | 8 | CLEAN MAINT NOISE STAFF FNB BILLING AMEN OTHER | constants (SLA hours illustrative) |
| Lost-and-found categories (`lostFoundCategories`) | 7 | JEWEL ELEC CASH DOCS CLOTH CARE OTHER | constants |

### 5.2 Per scenario

All scenarios share the seed above; only the rows **derived from records** change. BASE, S0, S2, S3, S5, S6 and AI1 are as §5.1 (2 expense vendors). S1 (empty hotel): 0 expense vendors (no expenses). S4 (company credit): one more auto booking source for the company account (16 rows). ACC (accounting): 3 expense vendors. FY26 (BASE + one year of history, the app's default): 16 expense vendors; every other list as §5.1. No scenario stores a `mastersX` slice and none carries a retired master row.

### 5.3 Demo data and the switch

The prototype seeds an **example holiday calendar** (8 rows, 6 paid in FY 2026-27, each row marked `demo:true`) so payroll and compliance screens show holidays. DEC-083 and the server seed none. It sits behind **one switch**: `MASTERS_SEED_OPTIONS.demoHolidays` in `masters-seed.ts` (`setMastersSeedOptions({demoHolidays:false})` before the first read starts the calendar empty). No other master seeds demo rows.

### 5.4 Checks (`tests/stream-masters-fixture.mjs`, test names `[MFX]`)

Old constants gone or marked LEGACY (`EXPENSE_CATEGORIES`, the three payment arrays, `adminRoomTypes`); every spec seed hook reads the seed module and no seed row literal lives elsewhere; every historical record of BASE and FY26 (receipts, POS receipts and orders, expenses, booking sources, rooms' floors and types, guests' ID types, staff designations and shifts, leave types, tasks' types, complaint and found-item categories, menu categories) resolves to a master row; each master's seed rows have unique codes and sort orders and none is retired; the golden figures (occupancy 8, collections ₹21,500 = 2,150,000 paise, drawer ₹10,725 = 1,072,500) and the BASE, FY26-history, FY26-bookings and FY26-whole-state hashes are unchanged; the demo switch works.

### 5.5 Seed values that differ between the prototype and the server seed (for MA-11)

| Master | Prototype seed | Server seed |
|---|---|---|
| Expense vendors | `V-<WORDS>` capped at 20 characters (`V-FRESH-LINEN-SERVIC`), name order; type rule also matches "electricity board" and "gas agency" | `V-0001`, `V-0002` … in name order (migration 2830); type rule without those two phrases |
| Bank account BANK-1 | "Demo Bank (fictional)", last four 4321, IFSC `DEMO0001234` | "Bank (to be set by the Owner)", no last four or IFSC |
| Menu categories | name-derived codes (`BEVERAGES`, `BREADS-RICE`), first-appearance order | `CAT-01`, `CAT-02` … in name order, sort 10, 20 … (migration 2840) |
| ID types | names carry "(demo)" (`Aadhaar (demo)`, `Passport + visa (demo, foreign national)`) because guest profiles store that text | `Aadhaar`, `Passport + visa (foreign national)` |
| Number series | INV prefix `DEMO`, padding 4, from 2025-04-01; BOS padding 4; CRN prefix `DEMO-CN`, padding 4 | INV prefix = the invoice-prefix setting or `INV`, padding = 16 − 6 − prefix length; BOS padding 7; CRN prefix `CN`, padding 8; from 2000-04-01 |
| Holiday calendar | 8 demo rows (§5.3) | none (DEC-083) |
| Room types, amenities, occupancy charges | seeded with illustrative beds, sizes, amenities and extras (₹800 / ₹400 / ₹1,000) | no amenity or occupancy-charge rows; room types exist only as the import wrote them |
| Housekeeping zones | one zone per floor with a round-robin default attendant | none |

Identical in both: payment methods, expense categories, meal plans, extras, discount reasons, booking sources, policies, shift patterns, leave types, task types, complaint and lost-and-found categories, floors.
