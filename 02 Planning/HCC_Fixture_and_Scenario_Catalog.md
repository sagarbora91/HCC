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
