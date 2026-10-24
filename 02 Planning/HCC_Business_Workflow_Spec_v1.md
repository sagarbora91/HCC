# Hotel Control Centre — Business Workflow Specification v1 (prototype)

Version: 1.0 (M0) · 24 October 2026 · Production revision due M4
Status: **Proposed**. States and rules below are for prototype validation; real hotel policy requires owner decisions (see `HCC_Delivery_Summary.md` §4).

Conventions:
- **Cmd** = mock-service command name. **Perm** = `HCC_RBAC_Matrix_v1.md` ID. **Key** = idempotency key format; a repeated key returns the first result (`DUPLICATE`).
- Every successful command writes one audit entry (actor, command, target, before/after summary, simulated timestamp).
- Every failed command leaves state unchanged and returns a typed error: `FORBIDDEN`, `NOT_AVAILABLE`, `INVALID_TRANSITION`, `VALIDATION`, `STALE_VERSION`, `DAY_CLOSED`, `APPROVAL_REQUIRED`.
- Dates: business date ≠ calendar date ≠ event timestamp ≠ service date (DEC-042). Stay intervals are `[arrival, departure)` (DEC-024).

---

## WF-BOOK-001 Reservation lifecycle

```text
Draft ──hold──▶ Held ──confirm──▶ Confirmed ──check in──▶ Fulfilled (stay created)
  │               │ expire            │ cancel / no-show
  └─confirm──────▶│                   ▼
                  ▼               Cancelled / NoShow
               Expired
```

| Transition | Cmd | Perm | Preconditions | Effects | Errors |
|---|---|---|---|---|---|
| Create & hold | `BOOK {hold:true}` | PERM-RES-002 | Valid dates (arrival ≥ business date, 1–30 nights), party ≤ room max, room available for whole interval | Booking `Held`, `holdUntil = now + 2h` (DEC-038); inventory reserved | `VALIDATION`, `NOT_AVAILABLE` |
| Create confirmed | `BOOK` | PERM-RES-002 | As above | Booking `Confirmed` | as above |
| Confirm hold | `CONFIRM` | PERM-RES-002 | Status `Held`, not expired | `Confirmed`; version+1 | `INVALID_TRANSITION` (expired) |
| Expire | system on any availability query | — | `Held` and `holdUntil < now` | `Expired`; inventory released | — |
| Change dates / room | `MOVE` | PERM-RES-003 | `Held`/`Confirmed`; target interval available excluding itself | Dates/room replaced atomically; agreed rate kept; new nights priced by DEC (tariff effective on that night) | `NOT_AVAILABLE` (original intact) |
| Cancel | `CANCEL` | PERM-RES-004 | `Held`/`Confirmed`; advance refunded or forfeit recorded; deposit returned | `Cancelled` with reason; inventory released | `INVALID_TRANSITION` |
| No-show | `CANCEL {noShow}` | PERM-RES-004 | `Confirmed`, arrival ≤ business date | `NoShow`; advance per policy (default: retained as no-show charge, needs reason) | as above |
| Advance | `PAYMENT {kind:advance}` | PERM-PAY-001 | Booking open, shift open for cash | Receipt allocated to booking folio (DEC-034) | see WF-PAY |

Key: `book:<clientRequestId>`. Invariant: no two `Held`/`Confirmed`/`InHouse` allocations of one room overlap (AC-002).

## WF-STAY-001 Check-in and stay

```text
Expected (Confirmed booking) ──check in──▶ InHouse ──checkout──▶ CheckedOut
                                         │  move / extend / shorten (stays InHouse)
```

| Transition | Cmd | Perm | Preconditions | Effects | Errors |
|---|---|---|---|---|---|
| Register guest | `REGISTER` | PERM-GST-002 | Required fields present (name, phone, ID type + masked number, address) | Guest verified flag, version+1 | `VALIDATION` |
| Check in | `CHECKIN` | PERM-STY-001 | Booking `Confirmed`; arrival ≤ business date < departure; registered; room not blocked, `Ready`, no occupant; allocation still valid | Stay `InHouse`; room occupied; first room segment starts; folio open | `INVALID_TRANSITION`, `NOT_AVAILABLE` (dirty/blocked/occupied) |
| Walk-in | `BOOK` + `REGISTER` + `CHECKIN` in one guided flow | as above | as above | as above | as above |
| Room move | `MOVE` | PERM-STY-002 | Target vacant, ready, unblocked and free for `[business date, departure)` | Target reserved **before** old released; new segment from business date; old room → `Dirty` + HK task; agreed tariff preserved unless re-rated with approval | `NOT_AVAILABLE` — original unchanged (AC-009) |
| Extend / shorten | `MOVE {departure}` | PERM-STY-002 | New interval free on current room; shortening cannot go before business date + 1 | Departure updated; extra nights at tariff for those dates | `NOT_AVAILABLE` with alternative rooms listed (AC-003) |
| Early / late fee | `CHARGE {kind:early/late}` | PERM-FOL-002 | Policy from SCR-18 | Charge line | — |

## WF-CHECKOUT-001 Checkout

| Step | Rule |
|---|---|
| 1 Preview | Shows agreed nights vs posted nights, balance, deposit held, pending approvals. |
| 2 Preconditions | All agreed nights up to departure posted; balance = 0 **or** an approved company-credit authorisation for exactly the balance; deposit returned (or applied by explicit receipt); no pending approvals on folio. |
| 3 Effects (atomic) | Stay `CheckedOut`; occupancy ends; room `Dirty`; **exactly one** HK task with causal key `hk:checkout:<stayId>`; sample invoice number allocated once; audit entry. |
| 4 Repeat | Same checkout key → returns existing result (AC-005). Second checkout without key → `INVALID_TRANSITION` "already checked out". |
| 5 Failure | Simulated failure → nothing changes (room still occupied, stay still in house); retry allowed (AC-009/AC-022). |
| 6 After departure | Billing corrections via WF-FOLIO-002; no reopening of occupancy. Re-entry = new booking with fresh availability check. |

Cmd `CHECKOUT` · Perm PERM-STY-003 · Key `checkout:<stayId>`.

## WF-FOLIO-001 Charges, discounts, invoice

| Action | Cmd | Perm | Rules |
|---|---|---|---|
| Night charge | `NIGHT_POST` (WF-DAY) | PERM-CLS-002 | Unique per stay + service date + kind `room`; tax snapshot stored on the line |
| Service charge | `CHARGE` | PERM-FOL-002 | Description + amount > 0; service date = business date; key `charge:<clientRequestId>` |
| Discount ≤ limit | `DISCOUNT` | PERM-FOL-003 | Reason required; cannot exceed net charges; posts negative line before tax |
| Discount > limit | `DISCOUNT` → `APPROVAL_REQUIRED` | PERM-FOL-003 + PERM-APR-001 | Creates request bound to folio version (WF-APR) |
| Sample invoice | `INVOICE` | PERM-FOL-004 | Allowed at checkout; number from sample series `DEMO/2026-27/0001`, allocated once; posted invoice immutable |

### WF-FOLIO-002 Corrections

Posted lines are never edited or deleted. Correction = reversal line (same amount, opposite sign, link to original) plus new line, or a sample credit note after invoicing. Requires reason and approval (PERM-FOL-005). Corrections for a closed business date follow DEC-028.

## WF-PAY-001 Receipts, deposits, refunds

| Action | Cmd | Rules |
|---|---|---|
| Payment / advance | `PAYMENT {kind:payment}` | Amount > 0; tender required; reference required for non-cash; cash requires the user's open shift; one or more tender rows; key `pay:<clientRequestId>`; confirmation text "Manual entry — not verified with bank/UPI". |
| Deposit | `PAYMENT {kind:deposit}` | Stored as liability; never reduces folio balance. |
| Deposit return | `PAYMENT {kind:deposit-return}` | ≤ deposit held. |
| Refund request | `REFUND_REQUEST` | References an original receipt; amount ≤ (that receipt − prior refunds); reason. |
| Refund execute | `PAYMENT {kind:refund, approvalId}` | Approved request; executor ≠ requester; same amount; re-checked (DEC-031). |
| Lost response | fault sim | UI shows "Checking previous attempt"; resubmitting the same key returns the committed receipt — no second receipt (AC-022). |

## WF-HK-001 Housekeeping

```text
Dirty ─(task created)─▶ Assigned/To do ──start──▶ Cleaning ──complete──▶ AwaitingInspection ──pass──▶ Ready
                                                     ▲                       │ reject (reason)
                                                     └───────────────────────┘
(if inspection policy OFF: complete ──▶ Ready)
```

| Transition | Perm | Preconditions | Effects |
|---|---|---|---|
| Create | system | Checkout, room move, or stayover schedule | Task; room `Dirty` |
| Assign | PERM-HK-002 | Enabled housekeeper | Assignee set |
| Start | PERM-HK-003 (own) | Status To do; assignee = user (HKP) | Room `Cleaning`, start timestamp |
| Checklist tick | PERM-HK-003 | Status In progress | Item toggled |
| Complete | PERM-HK-003 | All items ticked | `AwaitingInspection` (or Ready if policy OFF), timestamp |
| Inspect pass | PERM-HK-004 | Awaiting inspection | Room `Ready` — **block, if any, stays** (AC-010) |
| Inspect reject | PERM-HK-004 | Reason required | Back to Cleaning, checklist cleared |
| Report repair | PERM-ROOM-001 | — | Block request → SUP/MGR applies block (reason, dates) |

Inspection policy change (SCR-18) applies to tasks **completed after** the change; tasks already awaiting inspection still need inspection (Proposed).

## WF-EXP-001 Expenses

`Draft → Pending → Approved → Posted`; branches `Rejected` (reason), `Reversed` (posted only, reason, creates opposite cash movement).

| Rule | Detail |
|---|---|
| Submit | Vendor, category, amount > 0, funding source |
| Approve | PERM-EXP-002, approver ≠ submitter, within limit |
| Post | Drawer cash → requires open shift, reduces expected drawer once; Vendor credit → no drawer effect |
| Duplicate warning | Same vendor + amount + date shows a warning (not a block) |

Current build note: approve and post are one step; Draft and Reversed are missing.

## WF-APR-001 Approvals

`Requested → Approved | Rejected | Expired | Cancelled | Invalidated → Executed`.

- Bound to action, target ID, amount, target version (DEC-041).
- Target version changes → `Invalidated` (shown with reason "folio changed after request").
- Approve and execute are separate where an operator executes (refund); for discounts, approval executes the posting immediately after re-checking.
- Self-approval denied (PERM-SOD-001). Double-click approve → one effect.

## WF-SHIFT-001 Cash shift

`Open → Counting → Submitted → Closed`; difference path `Submitted (difference) → Difference approved → Closed`.

Expected cash = opening float + cash receipts + cash deposits + transfers in − cash refunds − cash deposit returns − drawer-funded posted expenses − transfers out. Non-cash tenders excluded.

Current build: closes only when counted = expected; no approval path (to add).

## WF-DAY-001 Business-day closing

```text
Open ──preflight──▶ Preflight ──post nights──▶ Posting ──close──▶ Closed ──open next──▶ (next date) Open
                       │ blocking issues            │ interrupted → resume at Posting (no repost)
```

| Step | Rule |
|---|---|
| Preflight blockers | Unresolved arrivals (confirmed, arrival ≤ date); overdue departures; in-house stays without tonight's room line; pending approvals; pending expenses; open shift; expired holds not yet released |
| Post nights | One room line per in-house stay for the business date; key `night:<stayId>:<date>`; rerun posts nothing (AC-016) |
| Close | One closing record per date with totals snapshot; rerun → `INVALID_TRANSITION` "already closed" |
| Resume | Simulated interruption after posting k of n stays; resume posts the remaining n−k only (AC-017) |
| Open next day | Opening float = counted closing cash; business date + 1; new shift opens |
| Correction | DEC-028 |

## WF-ACC-001 Access policy changes

| Action | Rules |
|---|---|
| Edit role grant | PERM-ADM-002; cannot grant self; versioned; before/after audited; effective on the next command for all users holding the role (AC-012) |
| Disable user | PERM-ADM-001; cannot disable self; cannot disable last active owner; next command by that user → `FORBIDDEN` "User disabled" |
| User exception | Grant/deny with reason and expiry |
| Effective-access preview | Lists each action → Allowed / Denied / Approval required with the deciding rule |

## WF-CFG-001 Configuration changes

Each setting: key, type, unit, default, allowed range, effective date, permission, approval need, audit. Save shows impact preview; changes never rewrite posted lines (AC-019). Five mandatory demonstrations: discount threshold, inspection rule, future-dated rate, tax effective date, check-in/out times (plan §13).

Non-configurable invariants: authorisation enforcement, room-overlap protection, payment duplication protection, audit history (REQ-CFG-002).
