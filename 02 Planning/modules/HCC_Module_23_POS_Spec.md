# HCC Module 23 — Restaurant POS & room service (prototype spec)

Status: built in the interactive prototype (stream `pos`, 24 Oct 2026). Fictional menu, synthetic 10% tax (no GST claim), integer paise, simulated clock. POS hardware, KOT printers and card terminals are **simulated** (browser print dialog only).

## 1. Scope
| In scope (prototype) | Out of scope |
|---|---|
| Two outlets: **Restaurant** (tables T1–T8) and **Room service** (in-house rooms) | Real POS/KOT printer hardware, card terminal integration |
| Fictional menu (paise prices, veg flag, category, outlet availability, "86" toggle) | Recipes / stock deduction (Module 21 Inventory) |
| One open check per table / per room; add, change qty, remove unsent items | Split bills, bill-level discounts, service charge, tips |
| Send KOT (kitchen order ticket) with per-outlet daily KOT number | Kitchen display hardware |
| Kitchen status per KOT: Preparing → Ready → Served (forward only) | Course timing / modifiers |
| Settle at outlet: Cash / UPI / Card (reference for UPI/Card) | Mixed tender on one bill |
| Settle to room folio of an in-house booking (one folio service line, tax snapshot) | Posting to a departed / future booking |
| Void item / void bill with reason; approval above ₹500, no self-approval | Voiding a bill already posted to a folio (use folio correction, Module 06) |
| Outlet day summary and printable report; KOT and bill print (A4 + phone) | Accounting export (Module 24) |

## 2. Roles & permissions (coarse model, no new values)
| Action | Permission | Roles in fixture |
|---|---|---|
| View POS page, open checks, add/remove items, send KOT, kitchen status, 86 an item | `billing` | Owner, Manager, Reception, Cashier |
| Post a check to a room folio (same rule as `CHARGE`) | `billing` | same |
| Settle at outlet (Cash/UPI/Card) | `payments` | Owner, Manager, Reception, Cashier |
| Void ≤ ₹500 | `billing` | same |
| Void > ₹500 | request (any `billing` user) → decide by a **different** user with `approve` | Owner, Manager |
Housekeepers, maintenance, supervisors, tech admin never see the page (no `billing`).

## 3. Data (state slice `s.posX`, type `POSState`)
- `outlets[]` {id, name, kind 'table'|'room', tables[]} · `menu[]` {id, name, category, price, veg, outlets[], available}
- `orders[]` POSOrder {id, outlet, table? | room?+booking?+guest?, covers, date (opened business date), openedAt, openedBy, status Open|Settled|Void, version, lines[], kots[], settlement?, voidReason?, pendingVoid? (approval id)}
- line {id, item, name, price, qty, kot? (KOT id once sent), voided?, voidReason?, voidBy?, voidApproval?}
- kot {id, no, at, by, status Preparing|Ready|Served, readyAt?, servedAt?}
- `receipts[]` outlet receipts {id, order, outlet, method, amount (gross, ± for void), net, tax, taxBps, reference, date, actor, kind sale|void}
- `kotSeq`, `billSeq` counters; settlement {mode outlet|folio, billNo, method?, reference?, net, taxBps, tax, total, date, at, by, receiptId? | booking+folioLineId}

Absent slice = fixture-free empty state (old saves). Fixture adds menu, outlets and four example checks (no money into existing bookings, drawer unchanged).

## 4. Rules
1. **Money**: line value = price × qty (paise). Bill net = Σ active (non-void) lines. Tax = round(net × taxBps / 10000) where taxBps = `taxRateFor(s, s.date)` **at settlement** (snapshot stored on the settlement and on the folio line). Before settlement the preview uses today’s rate.
2. **Day-closed rule**: every POS mutation calls `open()` → `DAY_CLOSED` when the business day is closed.
3. **Open check**: one Open check per table and per room. Room-service checks need an **In house** occupant; the booking ID and guest are bound at open.
4. **Unsent lines** may change quantity or be removed freely. **Sent lines** (on a KOT) can only be **voided with a reason**.
5. **KOT**: sends every unsent line; fails if none. KOT number = per outlet per business date. Status moves forward only; `Served` is final.
6. **Settle** requires: check Open, ≥1 active line, no unsent lines, no pending void. Idempotent: a settled check cannot settle again (`INVALID_TRANSITION`), plus the idempotency key.
   - **Outlet** (`payments`): method Cash/UPI/Card; UPI/Card need a reference; Cash requires the cash shift open. Creates one outlet receipt in `posX.receipts`.
   - **Room folio** (`billing`): target booking must be In house and open (`financial()` = mutable + day open). Pushes **one** line in the CHARGE shape `{id:'posline:<order>', label:'<Outlet> · bill <no> · <n> items', amount:net, date:s.date, kind:'service', taxBps}`; booking `version++`. A second post of the same check is impossible (status + deterministic line id).
7. **Void** (reason required, ≥ 3 chars): value = line value (item) or bill net (open check) / bill total (settled outlet bill, same business date only). Value ≤ `POS_VOID_LIMIT` (₹500) → immediate. Above → `s.approvals` entry {kind:'pos-void', target: order id, amount, reason, version: order version, expiresAt +24 h simulated, meta:{command:'POS_VOID_DECIDE', scope, line}}; check is locked (`pendingVoid`). Decision via `POS_VOID_DECIDE` (also from the shared Approvals inbox, which routes by `meta.command`): `approve` permission, not the requester, not expired, order version unchanged (`STALE_VERSION`). Pending void requests block day close (existing rule: pending approvals).
8. Voiding a settled **outlet** bill adds a negative `void` receipt (Cash requires shift open). A bill posted to a folio cannot be voided here.

## 5. Commands (`POS_*`)
| Command | Fields | Perm |
|---|---|---|
| `POS_ORDER_OPEN` | outlet, table \| room, covers?, guest? | billing |
| `POS_ITEM_ADD` | id, item, qty? | billing |
| `POS_ITEM_QTY` | id, line, qty (0 = remove; unsent only) | billing |
| `POS_KOT_SEND` | id | billing |
| `POS_KOT_STATUS` | id, kot, status Ready\|Served | billing |
| `POS_SETTLE` | id, mode outlet\|folio, method?, reference?, booking? | payments (outlet) / billing (folio) |
| `POS_VOID_ITEM` | id, line, reason | billing |
| `POS_VOID_BILL` | id, reason | billing (+payments for a settled bill) |
| `POS_VOID_DECIDE` | id (approval), approve, reason? | approve |
| `POS_MENU_TOGGLE` | item | billing |
All return audit text; errors are typed `HccError` codes (VALIDATION, FORBIDDEN, NOT_FOUND, INVALID_TRANSITION, DAY_CLOSED, STALE_VERSION, NOT_AVAILABLE).

## 6. Screens
**Desktop** (`pos`, nav CASH & BILLING): tabs **Orders** (outlet switch → table/room tiles with status; menu grid by category; current check panel with qty steppers, void, Send KOT, Settle, Print KOT/Bill), **Kitchen** (three-column KOT board Preparing/Ready/Served with advance buttons), **Day summary** (KPIs, by outlet, by settlement mode, outlet cash, voids, open checks, top items; Print).
Modals: `pos-open`, `pos-settle`, `pos-void`, `pos-print` (KOT or bill), `pos-report`.
**Phone**: same page ID; chips Orders / Kitchen / Summary; table/room cards; menu grid with ≥ 64 px tap tiles; sticky footer "Cart · n items · ₹x" opens the cart sheet (`pos-cart`) with steppers, Send KOT, Settle, Print. KOT cards in Kitchen; KPI cards in Summary. Print uses the same print modal (phone print preview / A4).

## 7. Known gap — outlet cash and the cashier drawer
`drawer()` (domain/selectors.ts) and `shiftMovements()` (domain/streams/closing.ts) count only **booking receipts** and cash expenses. Outlet cash cannot enter the drawer without editing those shared files, so outlet receipts are stored in `posX.receipts` and the POS Day summary shows **"Outlet cash (separate from the front-desk drawer)"**. Needed shared change (not made): in `drawer()` add `+ Σ posX.receipts where date===s.date && method==='Cash'` (amount is already signed) and list them in `shiftMovements()` as kind `pos-cash`, with `shiftExpected().other` picking them up. Also a day-close preflight item "Open POS checks" is not added (open checks simply carry over to the next business date).

## 8. Tests (`tests/stream-pos.mjs`)
Fixture figures and unchanged core figures, open/add/qty/KOT/status flow, settle outlet (cash/UPI reference/shift closed), folio post (line shape, tax snapshot, idempotency, not in-house, day closed), void below/above limit, no self-approval, stale version, inbox routing, settled-bill void, permissions (housekeeper / reception approve), day summary figures.
