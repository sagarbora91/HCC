# Hotel Control Centre — Implementation Backlog (prototype M1–M3)

Version: 1.0 (M0) · 24 October 2026
Every item lists requirement IDs, dependencies, output and the acceptance case that proves it. Estimates are relative sizes (S ≈ ½ day, M ≈ 1–2 days, L ≈ 3–4 days of focused work) and are **provisional**.
Work starts only after APR-003 (owner approval of this M0 package).

## M1 — Prototype foundation (WP-02)

| ID | Item | REQ / DEC | Depends | Output | Proves | Size |
|---|---|---|---|---|---|---|
| BL-001 | Fix stale drawer oracle (₹10,700 → ₹10,200) and document the reason | AC-024 | — | Test passes 28/28 | AC-024 | S |
| BL-002 | Split `main.tsx`/`domain.ts` into `app/ features/ domain/ services/mock/ fixtures/ styles/` | Spec §6 | APR-003 | Same behaviour, tests green | QG-P1..3 | M |
| BL-003 | Simulated clock for all timestamps (incl. audit) | DEC-030 | BL-002 | Deterministic state | QG-P10, AC-018 | S |
| BL-004 | Typed error codes from the mock service | Spec §6 | BL-002 | `FORBIDDEN`, `NOT_AVAILABLE`, … | AC-021 | S |
| BL-005 | Idempotency keys on every mutating command from the UI (receipts, charges, checkout) | REQ-PAY-006, REQ-STAY-006 | BL-004 | Repeat → same result | AC-005 | M |
| BL-006 | Fault simulation wrapper (latency, rejection, lost response, stale) + demo toolbar | NFR-REL-002 | BL-005 | SCR-20 | AC-022 | M |
| BL-007 | Scenario loader S0–S6 + reset + schema versioning | DEC-022 | BL-002 | SCR-20 | AC-024, QG-P11 | M |
| BL-008 | Grouped navigation, role landing pages, header (business date, connectivity, demo label) | Spec §1–2 | BL-002 | Shell | AC-023 | S |
| BL-009 | Rename demo property (if DEC-037 approved); add ACC, MNT, TAD users | DEC-032/037 | BL-007 | Fixtures | — | S |
| BL-010 | `npm start -- --lan` option + README (install, build, test, URL, reset, limitations, firewall) | DEC-036 | — | README.md | QG-P2 | S |

## M2 — Connected guest journey (WP-03..06)

| ID | Item | REQ | Depends | Output | Proves | Size |
|---|---|---|---|---|---|---|
| BL-011 | Calendar timeline with non-drag move/date-change dialog | REQ-RACK-004/005 | BL-008 | SCR-03 | AC-002, AC-003 | L |
| BL-012 | Availability search by type/party; holds with expiry | REQ-BOOK-001/004 | BL-003 | SCR-04 | AC-030 | M |
| BL-013 | Booking panel fields: phone, children, company, OTA channel; printable confirmation | REQ-BOOK-002/003/008 | BL-012 | SCR-04 | AC-021 | M |
| BL-014 | Guest profile: party, company/GSTIN, masked ID, duplicate suggestion, registration card | REQ-GUEST-001..007 | BL-002 | SCR-05 | AC-032, AC-034 | L |
| BL-015 | Check-in stepper incl. walk-in | REQ-STAY-001 | BL-014 | SCR-06 | AC-004 | M |
| BL-016 | In-house stay page with room segment history; move/extend alternatives | REQ-STAY-002/003 | BL-011 | SCR-07 | AC-003, AC-009 | M |
| BL-017 | Night posting per business date; remove or relabel "post full stay" (per DEC-029) | REQ-CLOSE-003 | DEC-029 | WF-DAY-001 | AC-016 | S |
| BL-018 | Mixed-tender payment dialog with "not verified" confirmation | REQ-PAY-001/002 | BL-005 | SCR-09 | AC-005, AC-006 | M |
| BL-019 | Refund request → approval → execution against original receipt | REQ-PAY-003, DEC-031 | BL-018 | WF-PAY-001 | AC-007 | M |
| BL-020 | Company credit at checkout with approval | REQ-PAY-007, DEC-025 | BL-018 | SCR-10 | AC-008 | M |
| BL-021 | Folio corrections: reversal lines and sample credit note | REQ-FOLIO-006 | BL-019 | SCR-08 | AC-007 | M |
| BL-022 | Housekeeping: reject reason, repair request → block with reason/dates, stayover tasks | REQ-HK-001/003/004, REQ-ROOM-004 | BL-002 | SCR-12 | AC-010, AC-037 | M |
| BL-023 | S0 walkthrough automated in Playwright | AC-001 | BL-011..022 | E2E test | AC-001, QG-P6 | M |

## M3 — Complete prototype (WP-07..09)

| ID | Item | REQ | Depends | Output | Proves | Size |
|---|---|---|---|---|---|---|
| BL-024 | Resource.action permission model, scopes, limits, field masking, user exceptions | REQ-ACC-002..005 | BL-002 | RBAC v1 implemented | AC-011, AC-029 | L |
| BL-025 | Effective-access preview with reasons; self-escalation and last-active-owner rules | REQ-ACC-006/007, DEC-033 | BL-024 | SCR-16 | AC-013, AC-035 | M |
| BL-026 | Approvals & audit screen; expiry and invalidation states | REQ-AUD-001/002 | BL-024 | SCR-17 | AC-014 | M |
| BL-027 | Configuration panel: categories, effective dates, impact preview, history; five demonstrations | REQ-CFG-001 | BL-003 | SCR-18 | AC-019, AC-033 | L |
| BL-028 | Cashier shift screen with difference approval; cashier submits, manager approves | REQ-PAY-004, DEC-035 | BL-024 | SCR-11 | AC-031, AC-015 | M |
| BL-029 | Expenses: draft, reverse, receipt placeholder, duplicate warning | REQ-EXP-001..005 | BL-028 | SCR-13 | AC-015 | M |
| BL-030 | Day-closing resume after interruption; correction request (per DEC-028) | REQ-CLOSE-005/006 | BL-006 | SCR-14 | AC-017 | M |
| BL-031 | Reports tabs, report dictionary, drill-down, CSV export, print | REQ-RPT-002..006 | BL-024 | SCR-15 | AC-020, AC-029 | L |
| BL-032 | Backup & recovery simulation screen | REQ-BKP-001 | BL-008 | SCR-19 | — | S |
| BL-033 | Simulated offline mode | NFR-OFF-001 | BL-006 | SCR-20 | AC-036 | S |
| BL-034 | Roadmap page listing areas 13–26 (no enabled navigation) | PRD §3 | BL-008 | Read-only page | — | S |
| BL-035 | Responsive + accessibility pass at 5 sizes and 200% zoom; screenshot evidence | NFR-UX-001/002 | all | `05 Delivery Evidence/M3/` | AC-023, QG-P8/9 | M |
| BL-036 | Print layouts with demo watermark | REQ-FOLIO-005, REQ-GUEST-005 | BL-014, BL-031 | Print CSS | AC-034 | S |
| BL-037 | Delivery summary, acceptance matrix update, owner demonstration | Plan §3 | all | M3 package | Owner acceptance gate | S |

## Status — 25 October 2026

- **M1–M3 (BL-001 … BL-036): DONE** in the prototype on `main` (502 tests, APK v8). BL-023 (S0 walkthrough) automated and passing; BL-035 evidence in `05 Delivery Evidence/Acceptance/`. **BL-037 (owner demonstration/acceptance): OPEN** — W0-D acceptance testing is complete (browser 15/15, Tab S7 17 checks, emulator API 35 Part 2 21/21 + Part 3 17/17); awaiting owner acceptance.
- Wave 0.5 is merged on `integration/w05` (584 tests), not yet on main; Wave 1 P0 started on `claude/wave1-p0` (`08 Production\`).

## Wave 0.5 — added items (APR-007)

| ID | Item | Output | Status |
|---|---|---|---|
| W05-A | Master configuration hub (settings provider API, `hrSettingDefs`/`cmpSettingDefs` wired) | Master configuration page | Done on `integration/w05`; review/browser fixes in progress |
| W05-B | Payroll, leave, payslips from SCC rules (FY26 locked-run net pay recomputed) | Staff pay pages | Done on `integration/w05`; CA items open (overtime 1x vs 2x, employer PF split/rounding, ESIC on OT, PT basis, gratuity not calculated); code review `W05B_Payroll_Code_Review_2026-10-25.md` |
| W05-C | Compliance calendar | Compliance page, Due-this-week card | Done on `integration/w05`; see W05-1, F2, F3, F5, F12, F14 |
| W05-D | Cash book | Cash book page, CSV, link from Day closing | Done on `integration/w05`; see F1, F4, F7, F13 |
| W05-E | Excel exports, day-pack PDF, Tally preview | Reports Excel, Day pack, Tally tab | Done on `integration/w05`; see F6, F8-F11 |
| W05-G | FY26 customer-profile gap | 1,155/1,155 customers with profiles | Done on `integration/w05` |
| Merge | Merge to main, SCHEMA 7 -> 8, full tsc/test/build, APK v9 | main + APK v9 | OPEN (lead, after fixes) |

## Open defects and decisions (25 Oct 2026)

| ID | Sev | Defect / decision | Source | Status |
|---|---|---|---|---|
| AC-03 | S4 | Day-closing count breakdown omits the 550 restaurant cash | `D5_AC.md` | Fix in progress (`claude/w05-phone-fixes`) |
| AC-04 | S3 | Native "Discard the details you entered?" dialog after count submit / Apply range | `D5_AC.md` | Fix in progress (`claude/w05-phone-fixes`) |
| R-01 | S4 | Housekeeping/maintenance see their own pay on "My shifts & pay" | `D5_Roles.md` | OPEN - owner decision |
| T-01 | S3 | Tablet portrait top bar ~25 % of height; landscape drawer list short | `D6_TabS7.md` | OPEN - owner decision (option mock-ups in `02 Planning\options\`) |
| T-02 | S3 | Desktop-layout controls 32-38 dp on the tablet (< 48 dp) | `D6_TabS7.md` | OPEN - owner decision (option mock-ups in `02 Planning\options\`) |
| W05-1 | S3 | Compliance visible in nav to Manager/Cashier (page blocked) | `W05_Browser_Check.md` | Fix in progress |
| W05-2 | S3 | Phone: no Excel button on Reports | `W05_Browser_Check.md` | Fix in progress (phone fixes) |
| W05-3 | S3 | Phone: no Tally export tab | `W05_Browser_Check.md` | Fix in progress (phone fixes) |
| W05-4 | S3 | Phone: no Day pack / cash book link from Day closing | `W05_Browser_Check.md` | Fix in progress (phone fixes) |
| W05-5 | S3 | Phone: no Due-this-week dashboard card (Manager does not see it) | `W05_Browser_Check.md` | Fix in progress (phone fixes) |
| W05-6 | S3 | Phone: touch targets / 16 px text | `W05_Browser_Check.md` | Fix in progress (phone fixes) |
| W05-7 | S4 | Cash book carry-forward partial | `W05_Browser_Check.md` | Fix in progress (`claude/w05-review-fixes`) |
| W05-8 | S4 | Cash book Excel export absent | `W05_Browser_Check.md` | Fix in progress (`claude/w05-review-fixes`) |
| F1 | S2 | The person who counted the cash can lock their own day (checker control bypassed) | `W05_DEC_Code_Review_2026-10-25.md` | Fix in progress (`claude/w05-review-fixes`) |
| F2 | S3 | Compliance intent takes over everyday assistant questions; Manager/Cashier/Reception refused | same | Fix in progress |
| F3 | S3 | Owner cannot edit the TDS obligation | same | Fix in progress |
| F4 | S3 | Cash book range totals do not add up when a cash difference occurred | same | Fix in progress |
| F5 | S3 | Annual rule due Jan-Mar is due before the financial year ends | same | Fix in progress |
| F6 | S3 | Full-FY26 Excel export may run out of memory on the phone (plausible) | same | Fix in progress |
| F7-F15 | S4 | Lower-severity findings (lock without closing record, xlsx writer edge cases, text ratios, Tally dates, false "Saved" toasts, menu visibility, lock blocks nothing, reopen erases filing, hygiene) | same | Backlog - fix when convenient |

## Not in prototype backlog (recorded, phase-tagged)

Areas 13–26 per PRD §3; Excel export; guest merge; day-use rates; foreign-guest reporting; real cross-device sync; production identity; SQL Server; real backup. These return at M4 planning.

## Provisional size total

M1 ≈ 6–8 days · M2 ≈ 12–16 days · M3 ≈ 14–18 days of focused build time, before owner review cycles. This will be revised after M1.
