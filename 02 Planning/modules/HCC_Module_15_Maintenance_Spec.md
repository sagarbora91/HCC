# HCC Module 15 — Maintenance (prototype spec)

Status: built in the interactive prototype (working mock, fictional data). Prefix `mnt` / `MNT_`. Page ID `maintenance` (nav OPERATIONS, permission `housekeeping`).

## 1. Purpose
Give the hotel one place to see and run all maintenance work: what equipment exists, what is broken, who is fixing it, which rooms are out of service because of it, what preventive work is due, and what it costs. Builds **on** the MVP repair hand-off (stream D): `REPAIR_REQUEST` repair tasks and dated room blocks (`BLOCK` / `UNBLOCK`). It does not duplicate them.

## 2. Scope (prototype)
| Area | What the prototype does |
|---|---|
| Asset register | Fictional AC units, geysers, TVs, lift, generator, fire pump, RO plant — each in a room or a common area; make/model (fictional brands), install date, vendor/AMC link, status Working / Faulty / Retired. |
| Work orders (WO) | Corrective (raised directly, or converted from a housekeeping repair request) and Preventive (generated from PM plans). |
| Priority / SLA | P1 Urgent 4 h · P2 High 24 h · P3 Normal 72 h · P4 Low 7 days, measured on the simulated clock from creation to "Done". |
| Status flow | Open → In progress → On hold (parts) → In progress → Done → Verified. Done can be returned to In progress with a reason. Open/On hold can be Cancelled with a reason. |
| Technicians | Enabled Maintenance-role users, or an external vendor (from the vendor list). |
| Spares | Small spares list with stock, reorder level and unit cost; parts used are booked on the WO (stock decreases, cost snapshotted); Receive restocks. |
| Vendors / AMC | Fictional vendors (trade, phone); AMC contracts with period, visits/year, value; status Active / Expiring (≤ 30 days) / Expired. |
| Preventive maintenance | Plans per asset (every N days, next due). "Generate due work orders" creates one preventive WO per plan occurrence due on or before business date + 3 days. Idempotent per plan + due date. |
| Downtime per room | A WO on a room offers "Block room" (uses the same block rules as stream D: reason, dated, refused if allocations overlap). Downtime runs from the block until the WO is verified with "Unblock room", or the room is otherwise unblocked. |
| Reports | Open WOs by age (< 1 d, 1–3 d, 3–7 d, > 7 d), MTTR (corrective, created → done), PM compliance (preventive WOs due by today done on or before due date), SLA breaches, downtime per room, parts cost (money-gated), WOs by asset type. Printable. |

Out of scope: real vendor messaging, purchase orders, inventory valuation, meter readings, photos/attachments, scheduled background generation (generation is a button; no real timers).

## 3. Roles and visibility
| Role | Sees | Can do |
|---|---|---|
| Owner, Manager (`inspect` + money) | Everything, including costs | All commands |
| Supervisor (`inspect`, no money) | Everything except costs | Assign, verify/return, cancel, block, PM generate, register edits (assets, plans, vendors, spares receive). AMC save needs `expenses`. |
| Maintenance (assigned scope) | **Own** work orders only (big cards on phone), assets and spares (no costs) | Report a new fault (self-assigned), start, hold, add parts, complete own WOs. Cannot verify, cannot block/unblock. |
| Housekeeper | Read-only list of open WOs on rooms in their own tasks | Repairs are reported from the task card (existing `REPAIR_REQUEST`). |
| Others (no `housekeeping`) | Page hidden | — |

Costs (part cost, totals, AMC values) are shown only to users with `reports` or `expenses` (Owner, Manager, Cashier, Accountant). Housekeepers and maintenance never see money.

Rules: a technician cannot verify their own work (`FORBIDDEN`); verification needs `inspect`. Creating WOs and generating PM requires an open business day (`DAY_CLOSED`).

## 4. Commands (`src/services/mock/handlers/maintenance.ts`)
| Command | Permission | Effect |
|---|---|---|
| `MNT_WO_CREATE {title, priority, room?, assetId?, area?, notes?, assignee?}` | housekeeping (not Housekeeper) ; day open | New corrective WO. Maintenance users are always self-assigned. Asset defaults the location. |
| `MNT_WO_FROM_REPAIR {taskId, priority?}` | inspect, or the assigned Maintenance user | Converts an open stream-D repair task into a WO (one WO per task). Status mirrors the task (To do → Open, In progress → In progress). |
| `MNT_WO_ASSIGN {id, assignee?, vendorId?}` | inspect | Maintenance user or external vendor. Linked repair task follows a Maintenance assignee. |
| `MNT_WO_START {id}` | assigned tech or inspect | Open/On hold → In progress. Linked repair task → In progress. |
| `MNT_WO_HOLD {id, reason, spareId?}` | assigned tech or inspect | In progress → On hold (reason required, optional awaited spare). |
| `MNT_WO_PART {id, spareId, qty}` | assigned tech or inspect | In progress / On hold; qty 1–stock; stock decreases; unit cost snapshot. |
| `MNT_WO_COMPLETE {id, note}` | assigned tech or inspect | In progress → Done (note ≥ 3 chars). Linked repair task → Done. Room block is **not** cleared. |
| `MNT_WO_VERIFY {id, approve, reason?, unblock?}` | inspect, not the completer/assignee | Done → Verified (plan `lastDone` updated; optional unblock of a WO-blocked room, closing downtime) or → In progress with reason. |
| `MNT_WO_CANCEL {id, reason}` | inspect | Open / On hold → Cancelled. |
| `MNT_WO_BLOCK {id, to?}` | inspect | Blocks the WO's room from today (reason "Maintenance · WO … · title"); refused with `NOT_AVAILABLE` if allocations overlap. Already-blocked room: links the existing block. Starts downtime. |
| `MNT_PM_GENERATE` | inspect ; day open | Creates due preventive WOs (horizon = date + 3), advances plan next-due. Repeat-safe. |
| `MNT_PLAN_SAVE {id?, assetId, title, everyDays, nextDue, active}` | inspect | Add/edit PM plan (every 1–365 days). |
| `MNT_ASSET_SAVE {id?, name, kind, room?, area?, make, model, installedOn, vendorId?, status}` | inspect | Add/edit asset. |
| `MNT_VENDOR_SAVE {id?, name, trade, phone}` | inspect | Add/edit vendor. |
| `MNT_AMC_SAVE {id?, vendorId, scope, from, to, visits, value}` | expenses | Add/edit AMC (value in paise). |
| `MNT_SPARE_RECEIVE {spareId, qty}` | inspect | Restock a spare. |

All handlers return audit text; idempotency keys are handled by `execute`.

## 5. Data (`s.maintenanceX`, type `MNTState`)
`assets`, `vendors`, `amcs`, `spares`, `plans`, `orders`. Work order: `id, kind, title, priority, status, room?, assetId?, area?, assignee?, vendorId?, createdAt, createdBy, startedAt?, doneAt?, doneBy?, verifiedAt?, verifiedBy?, holdReason?, awaitingSpare?, note?, returnReason?, cancelReason?, parts[{spareId, qty, unitCost}], repairTaskId?, planId?, dueDate?, downtimeFrom?, downtimeTo?, blockedByWo?, history[{at, by, action, note?}]`. Absent slice = empty.

## 6. Screens
Desktop `maintenance`: tabs Work orders · Preventive · Assets · Spares · Vendors & AMC · Reports. Work orders: KPI strip, repair requests waiting conversion, filters, table → WO dialog (`mnt-wo`) with all actions. Dialogs: `mnt-wo`, `mnt-new`, `mnt-hold`, `mnt-done`, `mnt-asset`, `mnt-plan`, `mnt-vendor`, `mnt-amc`, `mnt-receive`.
Phone `maintenance`: technician view — big cards (room/area, asset, priority, SLA, block info) with 56 px Start / Hold / Complete buttons and a "Report fault" FAB; manager view — KPI cards, section chips (Work orders, Preventive, Assets, Spares, AMC, Reports), list cards, FAB "New work order". Tables become cards; dialogs are sheets.

## 7. Fixture (fictional)
17 assets, 4 vendors, 3 AMCs (one expiring within 7 days), 9 spares (2 low/out), 7 PM plans (4 due within the horizon on 24 Oct), 9 work orders covering every status, one SLA breach, past PM occurrences (one late → compliance 50 %), and a verified WO with historical room downtime. Stream D's repair task for room 308 stays unconverted to demonstrate conversion. No money is posted to bookings; no existing fixture figures change. Scenario S1 (empty hotel) clears work orders.

## 8. Acceptance
- Repair request → WO → start → hold → part → resume → complete → verify (unblock) works end to end; the housekeeping repair task follows.
- Technician cannot see others' WOs, verify own work, block rooms, or see costs.
- PM generation is idempotent and advances plans; compliance and MTTR derive from data.
- Blocking a room with an overlapping allocation is refused; downtime is reported per room.
- Desktop 1366×800 and phone 390×844 render without horizontal scroll or console errors.
