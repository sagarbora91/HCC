# HCC Module 18 — Guest services, complaints, lost & found

Status: prototype spec (mock service, fictional data). Stream `services`, prefix `gsv` / `GSV_`. State slice `s.servicesX` (`GSVState`).
PRD row 18 ("Guest services / complaints / lost & found", Phase 2). Manual service *charges* stay in the folio module (CHARGE); this module never posts money itself.

## 1. Scope
| Area | In scope (prototype) | Out of scope |
|---|---|---|
| Service requests | Requests from in-house guests (towels, water, iron, wake-up call, taxi, amenities, minor repair, other) linked to booking + room; department + person assignment; SLA timer on the simulated clock; status flow; guest-facing completion note | Real telephony / wake-up call hardware; real taxi booking; guest self-service app |
| Complaints | Register with category, severity, owner, root cause, recovery action, follow-up; optional service-recovery discount **through the existing `DISCOUNT` command** (and its approval threshold) | Separate compensation ledger; vouchers |
| Lost & found | Found where/when/by whom, storage location, claim with ID check note, handover, disposal after retention days | Courier dispatch; police reporting integration |
| Reports | Response times (average, within-SLA %, by type/department), complaints by category & severity, lost & found ageing | Trend charts across months |

Guest notifications ("towels delivered", follow-up messages) are **Simulated** — shown as text only, nothing is sent.

## 2. Roles and access (coarse permissions only)
| Action | Permission | Notes |
|---|---|---|
| View Guest services page (requests, complaints, L&F, reports) | `reservations` | Reception, Manager, Owner |
| Log / assign / cancel service request | `reservations` | Supervisor (`inspect`) may assign/act on Housekeeping & Maintenance requests |
| Start / complete a request | `reservations`; or `inspect` for HK/Maintenance department; or assignee with `housekeeping` | Housekeepers / Maintenance act only on requests **assigned to them** (assigned-scope roles) |
| "My guest requests" page (`gsv-my-requests`, OPERATIONS) | `housekeeping` | Shows only own assignments for assigned-scope roles; **no money, no complaints** |
| Log complaint, update, resolve, follow-up | `reservations` | Closing a High/Critical complaint after follow-up needs `approve` |
| Service-recovery discount | `billing` (enforced by existing `DISCOUNT`) | Above `settings.discountLimit` → approval request; requester cannot approve own request (existing `APPROVAL`) |
| Log found item | `reservations` or `housekeeping` | Housekeepers log items found in rooms |
| Claim / handover | `reservations` | |
| Dispose item | `reservations`; valuables need `approve` | Only after retention period |
| Module settings (SLA minutes, retention days) | `approve` | |

## 3. Data (GSVState)
- `requests: GSVRequest[]` — id `SR-n`, booking, room, guest, type, detail, dept, assignee?, priority (Normal/Urgent), status, createdAt, dueAt, scheduledFor? (wake-up), startedAt?, doneAt?, note? (guest-facing), cancelReason?, by, log[].
- `complaints: GSVComplaint[]` — id `CMP-n`, booking?, room?, guest, category, severity, description, owner, status, rootCause?, recoveryAction?, recovery? {amount, approvalId?, lineId?, status}, followUpOn?, followUp? {outcome, satisfied, at, by}, createdAt, resolvedAt?, closedAt?, log[].
- `items: GSVItem[]` — id `LF-n`, description, category, valuable, foundWhere, room?, foundAt (clock), foundBy, storage, booking?, status, claim? {name, phone, idType, idNote, matchNote, at, by}, handover? {at, by, to, note}, disposal? {at, by, method, note}.
- `settings?` {sla: Record<type,minutes>, retentionDays (90), valuableRetentionDays (180)}.

## 4. Workflows and rules
### 4.1 Service requests
Status flow: `Open → Assigned → In progress → Done`; `Open/Assigned/In progress → Cancelled` (reason required).
- Request only for an **In house** booking (room taken from the booking). Business day must be open.
- Default department by type: towels/water/amenities/iron → Housekeeping; minor repair → Maintenance; wake-up/taxi/other → Front desk.
- SLA (simulated minutes, editable): towels 20, water 10, iron 15, amenities 20, taxi 30, repair 45, other 30, wake-up = scheduled time (must be in the future, HH:mm, same or next day). Urgent halves the SLA (minimum 5 min).
- `dueAt = createdAt + SLA`. Overdue = not finished and clock > dueAt; finished late = doneAt > dueAt. Response time = doneAt − createdAt.
- Assignee must be an enabled user from the department pool: Housekeeping → Housekeeper/Supervisor; Maintenance → Maintenance; Front desk → Reception/Manager/Owner.
- Completion requires a guest-facing note (≥ 3 chars). Completed/cancelled requests cannot change.
### 4.2 Complaints
Status flow: `Open → Investigating → Resolved → Closed`; follow-up "not satisfied" re-opens to `Investigating`.
- Required: guest name, category, severity, description, owner (enabled user with `reservations`).
- Resolve requires root cause **and** recovery action. Sets follow-up date (default next business date).
- Recovery discount: `GSV_COMPLAINT_RECOVERY` runs the existing `DISCOUNT` command unchanged (permission, open-folio, day-closed, net-charge cap, approval threshold, one pending request). The complaint stores the posted line ID or the approval ID; status shown from the approval. A second recovery is refused while one is posted or pending.
- Follow-up: record outcome + satisfied yes/no; satisfied → Closed (High/Critical need `approve`).
### 4.3 Lost & found
Status: `Stored → Claimed → Handed over`; `Stored → Disposed`.
- Valuables (Jewellery, Electronics, Cash / wallet, Documents) must be stored in "Front office safe".
- Claim: claimant name, phone, ID type, ID check note (last 4 characters only — never the full ID number), description-match note.
- Handover: only after a claim; records receiver and note.
- Disposal: only after retention (90 days; valuables 180) counted from found date to business date; method Donated / Destroyed / Handed to police; valuables need `approve`.

## 5. Commands
`GSV_REQUEST_CREATE, GSV_REQUEST_ASSIGN, GSV_REQUEST_START, GSV_REQUEST_COMPLETE, GSV_REQUEST_CANCEL, GSV_COMPLAINT_LOG, GSV_COMPLAINT_UPDATE, GSV_COMPLAINT_RESOLVE, GSV_COMPLAINT_RECOVERY, GSV_COMPLAINT_FOLLOWUP, GSV_LF_LOG, GSV_LF_CLAIM, GSV_LF_HANDOVER, GSV_LF_DISPOSE, GSV_SETTINGS`. All idempotent via command key; all return audit text; typed errors (FORBIDDEN, NOT_FOUND, VALIDATION, INVALID_TRANSITION, DAY_CLOSED).

## 6. Screens
| ID | Desktop | Phone |
|---|---|---|
| guest-services | Tabs: Requests (board table, SLA chips, filters) · Complaints · Lost & found · Reports (printable) | Chip filter tabs, cards, FAB "New request / Log complaint / Log item" per tab |
| gsv-my-requests | Own assigned requests: Start / Complete with guest note | Cards, big buttons |
| Modals | `gsv-request`, `gsv-complete`, `gsv-complaint`, `gsv-complaint-view`, `gsv-item`, `gsv-item-view`, `gsv-print` | Same (full-screen sheets) |
| Folio action | "Guest request" button in booking dialog (in-house stays) | Same |

## 7. Fixtures (fictional, extendBase)
Four requests on in-house stays (one overdue, one done within SLA, one done late, one assigned to Ravi), three complaints (Open, Resolved awaiting follow-up, Closed), four lost & found items (one past retention, one claimed, one valuable in safe). No money is posted; existing figures unchanged.

## 8. Acceptance
- Request SLA computed from simulated clock; overdue flagged with text label.
- Housekeeper sees and completes only own requests; no money visible.
- Recovery discount above limit creates a normal Pending discount approval; self-approval refused; below limit posts a discount line.
- Disposal refused before retention; valuable disposal refused without `approve`.
- Repeated command key creates no duplicate.
