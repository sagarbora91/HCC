# Hotel Control Centre — Preliminary Access Matrix (RBAC) v1

Version: 1.0 preliminary (M0) · 24 October 2026 · Final version due at M4
Status: **Proposed** — role templates are demonstration baselines, not approved staff assignments.

## 1. Model

`User → role assignments (with expiry) → resource.action grants → scope → field restrictions → limits / approval routes`, plus reasoned user-level exceptions with expiry. Policy is versioned; every change is audited.

## 2. Evaluation order (DEC-040)

1. User disabled, or assignment expired → **Deny** ("User disabled").
2. Record outside the grant's scope → **Deny** ("Outside your assigned rooms").
3. Explicit deny on any applicable role or exception → **Deny** (deny wins).
4. No applicable allow → **Deny** (default deny).
5. Allow found → check limit; above limit → **Approval required** (creates request); otherwise **Allow**.
6. Approval execution re-checks steps 1–5 for the executor and verifies the approval is bound to the same target, amount and version (DEC-041).

Multiple roles = union of allows, minus any explicit deny. "Inherit" in a user exception = no local override.

## 3. Roles

| Code | Role | Prototype status |
|---|---|---|
| OWN | Owner | Built |
| MGR | Manager | Built |
| REC | Reception | Built |
| CSH | Cashier | Built |
| SUP | Housekeeping supervisor | Built ("Supervisor") |
| HKP | Housekeeper | Built (2 users) |
| ACC | Accountant | To add (DEC-032) |
| MNT | Maintenance | To add (DEC-032) |
| TAD | Technical administrator | To add (DEC-032) |

## 4. Scopes

| Code | Meaning |
|---|---|
| P | Whole property |
| S | Own cash shift only |
| A | Assigned records only (tasks/rooms assigned to the user) |
| O | Own submissions only |

## 5. Permission matrix

Legend: **A** allow · **L** allow up to limit, above → approval · **R** request only (another user approves) · **V** view only · **M** view with masked fields · — deny. Scope letter follows where not P (e.g. `A·S` = allow, own shift).

| Perm ID | Resource.action | OWN | MGR | REC | CSH | ACC | SUP | HKP | MNT | TAD |
|---|---|---|---|---|---|---|---|---|---|---|
| PERM-RES-001 | reservation.view | A | A | A | V | V | M | — | — | — |
| PERM-RES-002 | reservation.create / hold / confirm | A | A | A | — | — | — | — | — | — |
| PERM-RES-003 | reservation.edit / change dates / move | A | A | A | — | — | — | — | — | — |
| PERM-RES-004 | reservation.cancel / no-show | A | A | A | — | — | — | — | — | — |
| PERM-RATE-001 | rate.override (vs tariff) | A | L | R | — | — | — | — | — | — |
| PERM-GST-001 | guest.view | A | A | A | M | M | — | — | — | — |
| PERM-GST-002 | guest.edit / register | A | A | A | — | — | — | — | — | — |
| PERM-GST-003 | guest.idDetails.view (field) | A | A | A | — | — | — | — | — | — |
| PERM-STY-001 | stay.checkIn | A | A | A | — | — | — | — | — | — |
| PERM-STY-002 | stay.move / extend | A | A | A | — | — | — | — | — | — |
| PERM-STY-003 | stay.checkOut | A | A | A | — | — | — | — | — | — |
| PERM-FOL-001 | folio.view | A | A | A | A | V | — | — | — | — |
| PERM-FOL-002 | folio.charge.post (service) | A | A | A | A | — | — | — | — | — |
| PERM-FOL-003 | folio.discount | A | L | L | — | — | — | — | — | — |
| PERM-FOL-004 | folio.invoice.issue / print | A | A | A | A | V | — | — | — | — |
| PERM-FOL-005 | folio.correction / credit note | A | R | R | — | R | — | — | — | — |
| PERM-PAY-001 | payment.record (own shift) | A | A | A·S | A·S | — | — | — | — | — |
| PERM-PAY-002 | deposit.record / return | A | A | A·S | A·S | — | — | — | — | — |
| PERM-PAY-003 | refund.request | A | A | R | R | — | — | — | — | — |
| PERM-PAY-004 | refund.execute (approved only) | A | A | — | A·S | — | — | — | — | — |
| PERM-PAY-005 | companyCredit.authorise | A | L | — | — | — | — | — | — | — |
| PERM-SHF-001 | shift.open / count / submit | A | A | A·S | A·S | — | — | — | — | — |
| PERM-SHF-002 | shift.difference.approve | A | L | — | — | A | — | — | — | — |
| PERM-HK-001 | hk.view board | A | A | V | — | — | A | A·A | A·A | — |
| PERM-HK-002 | hk.assign | A | A | — | — | — | A | — | — | — |
| PERM-HK-003 | hk.task.work (start/check/complete) | A | A | — | — | — | A | A·A | — | — |
| PERM-HK-004 | hk.inspect (pass/reject) | A | A | — | — | — | A | — | — | — |
| PERM-ROOM-001 | room.block.request | A | A | A | — | — | A | A·A | A·A | — |
| PERM-ROOM-002 | room.block.apply / unblock | A | A | — | — | — | A | — | — | — |
| PERM-EXP-001 | expense.submit | A | A | A·O | A·O | A·O | A·O | — | A·O | — |
| PERM-EXP-002 | expense.approve / reject | A | L | — | — | A | — | — | — | — |
| PERM-EXP-003 | expense.post / reverse | A | A | — | A·S | A | — | — | — | — |
| PERM-CLS-001 | close.preflight.view | A | A | V | V | V | — | — | — | — |
| PERM-CLS-002 | close.nightPost / closeDay / openNextDay | A | A | — | — | — | — | — | — | — |
| PERM-CLS-003 | close.correction.request | A | A | — | — | A | — | — | — | — |
| PERM-CLS-004 | close.correction.approve | A | — | — | — | — | — | — | — | — |
| PERM-APR-001 | approval.decide (discount, rate, refund, credit) | A | L | — | — | — | — | — | — | — |
| PERM-RPT-001 | report.operations | A | A | V | — | A | V | — | — | — |
| PERM-RPT-002 | report.finance | A | A | — | V·S | A | — | — | — | — |
| PERM-RPT-003 | report.export | A | A | — | — | A | — | — | — | — |
| PERM-ADM-001 | admin.users (create/enable/disable/assign) | A | — | — | — | — | — | — | — | A |
| PERM-ADM-002 | admin.roles (edit templates) | A | — | — | — | — | — | — | — | R |
| PERM-ADM-003 | admin.configuration | A | R | — | — | — | — | — | — | V |
| PERM-ADM-004 | audit.view | A | A | — | — | A | — | — | — | V |
| PERM-ADM-005 | backup.view / run (simulated) | A | V | — | — | — | — | — | — | A |

Notes:
- TAD can manage identities but holds **no** financial actions (separation of technical and financial power, plan §12). TAD role-template edits are requests the Owner approves.
- Manager **L** for approvals uses the manager limits in §6; above them the request routes to Owner.
- Housekeeper / Maintenance `A·A` = assigned tasks only; they never see guest finance or ID fields.

## 6. Limits (Proposed demonstration defaults, configurable in SCR-18)

| Limit | Reception | Manager | Owner |
|---|---|---|---|
| Discount without approval (per request) | ≤ ₹250 | ≤ ₹1,000 | unlimited |
| Rate override below tariff | request | ≤ 15% | unlimited |
| Refund execution | — | after approval | after approval (can approve others' requests only) |
| Cash difference acceptance | — | ≤ ₹200 | unlimited |
| Company credit at checkout | — | ≤ ₹10,000 per stay | unlimited |
| Expense approval | — | ≤ ₹5,000 | unlimited |

## 7. Approval routes

| Request | Raised by | Approved by | Bound to | Invalidated when |
|---|---|---|---|---|
| Discount above limit | REC / MGR | MGR (within limit) → OWN | folio, amount, folio version | any folio line/receipt change |
| Rate override | REC | MGR / OWN | booking, rate, booking version | dates/room/rate change |
| Refund | REC / CSH | MGR / OWN | original receipt, amount | receipt reversed or amount changes |
| Company credit at checkout | REC | MGR / OWN | folio, amount, version | folio change |
| Cash difference | CSH | MGR / ACC / OWN | shift, counted amount | new movement in shift |
| Expense | any submitter | MGR / ACC / OWN | expense, amount | amount/vendor edit |
| Closed-date correction | MGR / ACC | OWN | business date, correction lines | correction edited |
| Role template / sensitive config | TAD / MGR | OWN | policy version | newer policy version |

Approvals expire after 24 simulated hours (Proposed).

## 8. Separation-of-duty rules (always enforced, not configurable)

| ID | Rule |
|---|---|
| PERM-SOD-001 | No user approves their own request (discount, refund, expense, cash difference, policy). |
| PERM-SOD-002 | No user grants themselves a permission or raises their own limits. |
| PERM-SOD-003 | The last active Owner cannot be disabled, demoted or have admin removed (DEC-033). |
| PERM-SOD-004 | A cashier cannot approve the difference on their own shift. |
| PERM-SOD-005 | Disabled users are denied on their next command; open dialogs fail on submit. |
| PERM-SOD-006 | Emergency access (production only): named actor, limited duration/scope, reason, audit, owner notification; no hidden bypass. |

## 9. Field restrictions

| Field group | Visible to | Others see |
|---|---|---|
| Guest ID type/number | OWN, MGR, REC | "Restricted" |
| Guest phone/email | OWN, MGR, REC, CSH | masked (last 4) |
| Folio amounts | OWN, MGR, REC, CSH, ACC | hidden from HK/MNT entirely |
| Audit before/after values | OWN, MGR, ACC | action + time only |
| Staff list | OWN, TAD, MGR (view) | — |

## 10. Mapping from the current build

The build uses 12 coarse permissions per role (`reservations, checkin, billing, payments, checkout, housekeeping, inspect, expenses, approve, close, reports, admin`). Proposed M1 change: replace them with the resource.action IDs above, add scopes, limits, field masking, user exceptions and the three missing roles. The current build's grants differ from this matrix in these notable places (to be aligned):

| Current build | This matrix |
|---|---|
| Reception has `expenses` (full) | Reception: submit own only |
| Cashier has `reports` (all) | Cashier: finance reports for own shift |
| Refund gated by `approve` permission | Refund = request + separate execution (DEC-031) |
| Any Owner protected from disable | Only last active Owner (DEC-033) |
| Owner template locked; admin not delegable | TAD manages users; role edits need Owner approval |
