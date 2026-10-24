# HCC Module 19 — Checklists & shift handover (prototype spec)

Stream `handover` · prefix `hov` / `HOV` · state slice `s.handoverX` · page `handover` · PRD row 19 (Phase 2, "MVP housekeeping + cashier essentials").
Status: prototype (working mock, fictional data, simulated clock). Production backend out of scope.

## 1. Purpose
Replace paper shift registers: every role opens the app at the start of a shift, sees *its own* checklist for the shift, ticks items with large checkboxes (phone first), adds a note or a photo **file name** (placeholder — no upload in the prototype), and submits. At the end of a shift the outgoing person writes a **handover note** (open issues, cash/keys/guest notes, carry-forward items) that **the next shift must acknowledge**. Owner/Manager edit templates and watch completion on a dashboard.

## 2. Concepts
| Concept | Definition |
|---|---|
| Desk | Handover lane. `Front desk` (Reception, Cashier, Manager), `Housekeeping` (Supervisor, Housekeeper), `Maintenance` (Maintenance, Supervisor). Owner/Manager see every desk. |
| Shift | `Morning` 07:00–15:00, `Evening` 15:00–23:00, `Night` 23:00–07:00 (simulated clock, Asia/Kolkata). Current shift derives from `s.clock`. |
| Template | Named checklist for a desk + shift + roles; ordered items `{text, mandatory, due HH:mm?, photo?, link page?}`; `active`; `version`. |
| Run | One template on one business date. Created on the first tick. Snapshots the template items (later template edits never change an existing run). Status `Open` → `Submitted` (→ reopened by a manager). |
| Handover note | One per desk + business date + outgoing shift. Status `Submitted` → `Acknowledged`. Carry-forward items stay open on the desk until marked done. |

## 3. Fixture templates (fictional)
1. **Front desk opening** — Reception · Morning · 7 items (read handover, arrivals list → Reservations, room board → Room board, float verified → Cashier, key-card encoder test, lobby photo, VIP/notes).
2. **Front desk closing** — Reception · Evening · 6 items (departures settled → Folios, no-shows, late arrivals called, key cards reconciled, handover note written, desk photo).
3. **Cashier handover** — Cashier, Reception · Evening · 6 items (count drawer → Cashier, UPI settlement match *(simulated)*, expense vouchers → Expenses, safe drop, card terminal batch *(simulated)*, cash note in handover).
4. **Housekeeping floor check** — Housekeeper, Supervisor · Morning · 7 items (trolley stocked, corridors F1–F3 photo, lost & found, DND rooms, linen par, repairs reported → Housekeeping, chemicals stored).
5. **Night audit pre-checks** — Manager, Reception · Night · 6 items (arrivals resolved → Reservations, room nights posted → Day closing, approvals cleared → Approvals, cash counted → Cashier, backup *(simulated)*, lobby security round).
6. **Maintenance rounds** — Maintenance, Supervisor · Morning · 6 items (DG diesel level, overhead tank, lift alarm test, fire extinguisher gauge photo, pump room, blocked room 308 status → Maintenance).

Fixture state on 24 Oct 2026 08:00: yesterday's runs (mostly submitted; one evening run missed), today's *Front desk opening* partly ticked with an item due 07:30 not done (→ overdue), today's *Housekeeping floor check* started. Notes: Front desk Night→Morning note from Meera (Manager), **awaiting acknowledgement**, with a cash line and two carry-forward items; Housekeeping evening note acknowledged; Maintenance note acknowledged with an open carry item (room 308 AC compressor).

## 4. Rules
- **R1 Access.** A user may tick/submit a run only when their role is in the template's roles, or they are a manager (`approve` permission: Owner, Manager). Users without `housekeeping`, `payments` or `approve` have no checklist access (Accountant, Tech admin). Disabled/expired users are denied by `execute`.
- **R2 Templates.** Only `approve` (Owner/Manager) can create/edit/activate templates. Name ≥ 3 chars, 1–25 items, item text 3–120 chars, due `HH:mm`, roles non-empty and valid for the desk. Editing bumps `version`; existing runs keep their snapshot.
- **R3 Ticking.** Business day must be open (`DAY_CLOSED`). Run must be `Open`. An item flagged *photo* needs a photo file name before it can be ticked done. Notes ≤ 200 chars. Each tick records actor + simulated time.
- **R4 Submit.** All mandatory items done (`INVALID_TRANSITION` listing the count). If the desk has an incoming handover note (Submitted, written by someone else) the submitter must acknowledge it first. Late flag when submitted after the shift end.
- **R5 Reopen.** Manager only, reason required; returns the run to `Open`.
- **R6 Overdue.** An item is overdue when not done and its due time (business date; Night times before 23:00 roll to the next calendar day) is earlier than `s.clock`. A run is overdue when it has overdue items, or it is still open after its shift end.
- **R7 Handover note.** Author must belong to the desk (or be a manager). Text: open issues required (≥ 3 chars) unless "nothing to report" is ticked. Cash line (expected float, rupees → paise, note) only on the Front desk desk and only with `payments` permission. Up to 8 carry-forward items. One note per desk/date/shift; it can be replaced until acknowledged.
- **R8 Acknowledge.** Next-shift member of the desk (or a manager); **cannot acknowledge your own note** (`FORBIDDEN`, like no self-approval); only `Submitted` notes. Optional acknowledgement remark. Idempotent with key.
- **R9 Carry-forward.** Items stay open across notes until a desk member marks them done (records actor/time).
- **R10 No money for housekeeping/maintenance.** Cash lines are stored only on Front desk notes and rendered only to users with `payments` or `reports`. Housekeeping/Maintenance desk members never see Front desk notes.
- **R11 Demo clock.** `HOV_CLOCK` (Owner/Manager, `close`) advances the simulated clock by 15–120 min to demonstrate overdue flags; never past 06:59 of the next calendar day. Labelled *Demo*.

## 5. Commands
| Command | Fields | Perm | Notes |
|---|---|---|---|
| `HOV_TEMPLATE_SAVE` | `template{id?,name,desk,shift,roles[],items[]}` | approve | create or edit (version++) |
| `HOV_TEMPLATE_TOGGLE` | `id` | approve | active ↔ inactive |
| `HOV_ITEM` | `template,item,done?,note?,photo?` | R1 | creates today's run on first use |
| `HOV_RUN_SUBMIT` | `template` | R1 | R4 |
| `HOV_RUN_REOPEN` | `template,reason` | approve | R5 |
| `HOV_NOTE_SUBMIT` | `desk,shift?,issues,keys,guests,nothing?,cash?{amount,note},carry[]` | R7 | |
| `HOV_NOTE_ACK` | `id,remark?` | R8 | |
| `HOV_CARRY_DONE` | `note,item` | desk member | R9 |
| `HOV_CLOCK` | `minutes` | close | R11 |

## 6. Screens
**Desktop page `handover`** (tabs): *My checklists* (cards per run for the current shift first, other shifts collapsed; progress bar + %, overdue/mandatory pills, big checkboxes, due time, note/photo button, link-outs via `go()`, Submit) · *Handover* (incoming note to acknowledge banner, notes by desk with cash/keys/guest sections, carry-forward list, "Write handover note") · *Dashboard* (managers: completion by role and by desk for a chosen date, overdue items list, unacknowledged notes) · *Templates* (managers: table, edit modal, activate/deactivate). Demo clock buttons for managers.
**Phone page `handover`**: chip filter (Checklists / Handover / Dashboard / Templates by role) → `ListCard` per run → run detail with 56 px checkbox rows and a sticky "Submit checklist" footer; handover note sheet; acknowledge as the single primary action when a note is waiting.
**Modals**: `hov-item` (note + photo name), `hov-note` (write note), `hov-template` (wide editor), `hov-reopen`.

## 7. Navigation / permission decision
The pre-wired item used `reports`, which hides the page from Reception, Supervisor, Housekeeper and Maintenance. Nav items accept one permission only and no existing permission covers all checklist roles. Interim (inside `features/handover/register.tsx`): because `App` shows a nav entry when any item with its page ID is permitted, the same page component is registered under two page IDs — `handover` in OPERATIONS with `housekeeping` (Owner, Manager, Supervisor, Housekeeper, Maintenance) and `handover-desk` ("Desk checklists & handover") in FRONT DESK with `payments` (Owner, Manager, Reception, Cashier). Content is filtered by role inside the page. Only Owner/Manager see both entries. **Shared change requested:** allow `NavItem.p` to be `Permission | Permission[]` (any-of) in `app/nav.ts` and the `allowedIds` check in `app/App.tsx`; then collapse to one entry.

## 8. Acceptance (tests in `tests/stream-handover.mjs`)
Fixture figures; overdue at 08:00; tick creates run & snapshot; template edit does not change run; photo required; mandatory blocks submit; incoming note blocks submit until acknowledged; self-acknowledge forbidden; housekeeper cannot write Front desk note nor see cash; reception cannot edit templates; idempotent ack; day-closed rejects ticks; reopen requires reason; carry-forward done; clock advance cap; existing fixture totals unchanged.
