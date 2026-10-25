# RBAC matrix check — 25 Oct 2026

Branch `claude/rbac-tests` (from integration/w05 @ 61e1369). Test file: `03 Prototype\tests\stream-rbac-matrix.mjs` (71 tests, part of `npm test`: **655/655 pass**, tsc clean). No product code changed.

**Sources used as the truth:** `HCC_RBAC_Matrix_v1.md` (§5 matrix, §6 limits, §7 routes, §8 separation of duty, §9 fields), DEC-040/052/055/057/058, `HCC_Wave05_UI_Placement_v1.md`, `HCC_Prototype_Spec_v1.md` §1.1 (visible sections per role), module specs 13–26 (their coarse-permission tables). Where a module spec and the matrix conflict, the conflict is listed below.

## What is covered

| Area | Coverage | How |
|---|---|---|
| Navigation | 10 users × 36 nav pages = 360 cells, on three surfaces (drawer, Modules Home tiles, search palette incl. quick actions) | Same expressions as `NavDrawer.tsx`, `ModulesHome.tsx`, `SearchPalette.tsx`; surfaces must agree with each other and with the documented role set per page. A new page without a documented role set fails the test. |
| Commands | 10 users × 185 commands = 1,850 cells | Every command in the handler registry + core switch. Allowed roles must not get `FORBIDDEN`; others must get the typed `FORBIDDEN`. 23 commands are driven with a representative valid payload (approvals, assigned records, cash book, config, yield, payroll…) because validation runs before the role check; the rest are checked at the guard with an empty payload. A new command without a documented role set fails the test. Disabled / expired / unknown user → `FORBIDDEN` on all 185. |
| Wave 0.5 | CB_LOCK/CB_REOPEN, all CMP_*, all HR_* (payroll, leave, rates), SETTING_SET + CFG_* (DEC-058: Tech admin non-money only, approval where named, Owner decides), Compliance visibility (page, Due-this-week card, assistant answers), Tally tab, Excel export reach | Exports have no commands; they follow the page, so their reach is checked through page visibility. |
| Money visibility | housekeeper, housekeeper2, maint | No money permission; `mntCanSeeCost`, `invMoneyRole`, `hovSeesCash`, `hrCanPay`, `cmpHasAccess`, assistant money flags all false; every assistant topic + money questions answered without a ₹ figure; guest requests / handover notes carry no money fields; nav limited to the six no-money pages; payslips only their own. |
| Self-approval | discount, refund (approve + execute, DEC-031), cash difference (+ SOD-004), expense, pay-run lock (both directions), leave, config request, policy request, closed-date correction, tariff publish, cash book lock by counter/closer (same day) | All refused with `FORBIDDEN`. |

## Open owner decision (recorded, not asserted)

| ID | Item |
|---|---|
| R-01 | Housekeeping / maintenance see their **own** pay on *My shifts & pay* (payslip card). Placement doc allows "only their own pay"; CLAUDE.md says they "never see money". Test only checks they never see anyone else's pay. |

## Mismatches (each is a known-failure test: logs `KNOWN-FAIL <id>`, flips to `XPASS` when fixed)

Classification: **App** = app bug vs. the documents · **Doc** = matrix/spec gap or conflict (decide, then fix the document or the app).

| ID | Role(s) | Page / command | Documents say | App does | Suggested fix | Type |
|---|---|---|---|---|---|---|
| W05-1 | Manager, Cashier | Compliance calendar (drawer, Modules Home tile, search) | DEC-055: Owner + Accountant only | Listed for both (nav `p:'reports'`); page body shows "Owner and Accountant only" | Role-aware nav filter (e.g. `NavItem.when(s,uid)` using `cmpHasAccess`) — fix on another branch | App |
| F1 | Manager (any counter who is also a checker) | CB_LOCK of a past day | Day locked by a different person from the counter | After NEXT_DAY the counter is no longer known (`cbCounters` reads only the current `s.shift`), so the counter can lock — fix on another branch | Store `countedBy` in the closing record and read it in `cbCounters` | App |
| RBAC-1 | Supervisor, Maintenance | Room board | Spec §1.1: room board read-only (no guest finance); RES-001 SUP = M | Hidden (`p:'reservations'`) | Either add a masked read-only room board for `housekeeping`/`inspect`, or drop it from Spec §1.1 | Doc |
| RBAC-2 | Cashier, Accountant (+ Supervisor masked) | Reservations, Room board, Calendar | RES-001 CSH = V, ACC = V, SUP = M | Hidden | Matrix §5 and Spec §1.1 conflict — decide; if kept, read-only views | Doc |
| RBAC-3 | Accountant | Folios, Customers, Expenses, Day closing | Spec §1.1 "Folios (read), Expenses, Day closing (read)"; FOL-001 V, GST-001 M, EXP-001..003, CLS-001 V | Hidden: coarse grant is `Accountant:['reports']` (M1 approximation) | Give the Accountant read access (any-of nav + view-only mode) or move to resource.action grants (BL-024) | App |
| RBAC-4 / 4b | Supervisor, Maintenance (+ Accountant) | Expenses page; EXPENSE / EXPENSE_SUBMIT | EXP-001 = A·O (submit own) | Page hidden and commands `FORBIDDEN` (need `expenses`) | "Submit own expense" path for these roles (own-scope list only; MNT must not see other expenses) — or change the matrix | Doc |
| RBAC-5 | Reception | Housekeeping board | HK-001 REC = V | Hidden | Read-only board for reception, or matrix → — (room board already shows HK status) | Doc |
| RBAC-6 | Reception, Cashier | Day closing (preflight) | CLS-001 = V | Hidden (`p:'close'`) | Read-only preflight view, or matrix → — | Doc |
| RBAC-7 | Reception, Supervisor | Reports | RPT-001 report.operations = V | Hidden | Operations-only report view without money, or matrix → — | Doc |
| RBAC-8 | Manager | Access control | Spec §1.1: view only | Hidden (`p:'admin'`) | Read-only Access control for Manager | App |
| RBAC-9 / 9b | Manager | Master configuration; SETTING_SET | ADM-003 MGR = R; §7 "sensitive config raised by TAD / MGR" | Page hidden; SETTING_SET `FORBIDDEN` (DEC-058 names only Owner/Tech admin) | Decide whether DEC-058 replaced the Manager request route; then update the matrix or add a request path | Doc |
| RBAC-10 | Cashier (over), Tech admin (under) | Approvals & audit | ADM-004 audit.view: OWN MGR ACC, TAD V; CSH — | Cashier sees it (`p:'reports'`); Tech admin does not | Gate on approve/audit roles; give TAD a read-only audit view | App |
| RBAC-11 | Manager | Backup | ADM-005 MGR = V | Hidden | Read-only backup status for Manager | Doc |
| RBAC-12 | Cashier | DISCOUNT; GSV_COMPLAINT_RECOVERY | FOL-003 CSH = — | Allowed (`billing`) | Add a role check (discount = Owner/Manager/Reception) | App |
| RBAC-13 | Cashier (over), Accountant (under) | CORRECTION_REQUEST, CREDIT_NOTE | FOL-005: OWN A, MGR/REC/ACC R, CSH — | Cashier may request; Accountant refused | Role check per FOL-005 | App |
| RBAC-14 | Reception | REFUND_EXECUTE | PAY-004 REC = — | Allowed (`payments`) | Restrict execution to Owner/Manager/Cashier | App |
| RBAC-15 | Accountant | SHIFT_DIFFERENCE_DECIDE | SHF-002 ACC = A; §7 route → MGR/ACC/OWN | `FORBIDDEN` (needs `approve`) | Allow Accountant (not on own shift) | App |
| RBAC-16 | Accountant; Cashier | EXPENSE_APPROVE, EXPENSE_REVERSE | EXP-002 ACC = A; EXP-003 ACC = A, CSH = A·S | `FORBIDDEN` (needs `approve`) | Allow per matrix, with own-shift scope for Cashier | App |
| RBAC-17 | Accountant | CORRECTION_ADJUST (closed-date correction request) | CLS-003 ACC = A | `FORBIDDEN` (needs `close`) | Allow Accountant to request | App |
| RBAC-18 | Tech admin | ACC_MAP_SET / ACC_MAP_RESET | Matrix note + plan §12: TAD holds no financial actions | Allowed (Module 20 spec uses `admin`) | Require Owner (admin + reports) | Doc (module 20 vs matrix) |
| RBAC-19 | Tech admin | GRP_LIMIT_SET | Module 14: credit limit set by Owner; TAD no financial power | Sets the limit directly (`can(admin)`) | Use `admin` + `reports` (Owner) for set; TAD refused | App |
| RBAC-20 | Reception, Cashier | MNT_AMC_SAVE | Module 15: maintenance roles with money (Owner/Manager) | Allowed (needs only `expenses`); they cannot open the Maintenance page | Also require `housekeeping`/`inspect` | App |
| RBAC-21 | Maintenance | `mntVisibleOrders` selector | Housekeeping/maintenance never get money fields | Returns part `unitCost` to the Maintenance user (page hides it) | Strip money fields in the selector (and later in the API projection) | App |
| RBAC-22 | Owner, Manager | DISCOUNT limits | §6: Owner unlimited, Manager ≤ ₹1,000, Reception ≤ ₹250 | One hotel threshold (₹250) for every role; Owner/Manager discounts above it raise an approval | Use `adminLimitFor(role,'PERM-FOL-003')` | App |
| RBAC-23 | Manager | APPROVAL of a discount | APR-001 MGR = L (≤ ₹1,000), above → Owner | Manager approves any amount | Check the Manager limit in APPROVAL (company credit already does) | App |
| RBAC-24 | Manager, Cashier | Accounting › Tally export tab | Placement: Owner, Accountant | Visible to everyone who opens Accounting | Hide the tab unless Owner/Accountant | App |
| RBAC-25 | Cashier | Reports Excel / CSV export | RPT-003 CSH = — (RPT-002 = V·S) | Export buttons available with the Reports page | Hide export for Cashier, or change the matrix | Doc |

## Notes
- The coarse model (12 permissions) cannot express V/M/A·O cells; most **Doc** rows exist because the matrix (M0, "Proposed") is richer than the M1 build. Final matrix is due at M4.
- Expected sets for modules 13–26 follow each module spec's coarse-permission table; those currently match the app apart from RBAC-18/19/20.
- When a fix lands, its test prints `XPASS <id>`: delete that entry from `KNOWN` in the test file and from this list.
