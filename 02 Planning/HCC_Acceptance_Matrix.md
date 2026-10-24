# Hotel Control Centre — Acceptance Matrix

Version: 1.0 (M0) · 24 October 2026
Statuses: **Passed** · **Failed** · **Partial** (some sub-cases evidenced) · **Not run** · **Not built** · **Deferred with approval** · **Production** (not applicable to the prototype).
A test counts as Passed only with an evidence location naming the build and scenario. Code existing is not evidence.

Evidence sources available today:
- **E1** = `03 Prototype/evidence/domain-tests.json` — domain unit tests, recorded run 2026-10-24T04:01Z (27/28 passed). Mock-service level only; no UI.
- **E2** = `05 Delivery Evidence/M0/m0_walkthrough_sim_output.txt` — S0 walkthrough executed against the domain, 2026-10-24T04:20Z.
- UI / browser evidence: **none yet**.

| ID | Test | REQ | Screen | Workflow | Domain status | UI / E2E status | Evidence / gap |
|---|---|---|---|---|---|---|---|
| AC-001 | Reset → checkout → cleaning journey reconciles | REQ-STAY-005, REQ-RPT-003 | SCR-01..15 | all | Passed | Not run | E2; E1 "End-to-end close…"; UI walkthrough pending |
| AC-002 | Duplicate exclusive room allocation rejected; original intact | REQ-BOOK-006 | SCR-03/04 | WF-BOOK-001 | Passed | Not run | E1 "Overlapping room reservation…", "Adjacent… accepted" |
| AC-003 | Date change/extension into a future booking → conflict, original preserved | REQ-STAY-003 | SCR-07 | WF-STAY-001 | Passed | Not run | E1 "Room move conflict…"; alternatives list not built |
| AC-004 | Check-in to dirty or blocked room denied | REQ-STAY-001 | SCR-06 | WF-STAY-001 | Passed | Not run | E1 "Dirty room…", "Blocked room…" |
| AC-005 | Repeated receipt/charge/checkout → one effect | REQ-PAY-006, REQ-STAY-006 | SCR-08/09/10 | WF-PAY/CHECKOUT | Partial | Not run | E1 covers repeated night posting only; receipt & checkout keys untested |
| AC-006 | Worked fixture: ₹4,400 invoice, advance once, deposit separate | REQ-FOLIO-004 | SCR-08 | WF-FOLIO-001 | Passed | Not run | E1 "Worked ₹4,400…"; E2 multi-day |
| AC-007 | Refund and correction: eligible source, approval, linked reversal | REQ-PAY-003, REQ-FOLIO-006 | SCR-08/17 | WF-PAY/FOLIO-002 | Partial | Not built | E1 "Refund cannot exceed…"; request/approve/execute & credit note not built |
| AC-008 | Unpaid checkout blocked or via approved company credit | REQ-STAY-004, REQ-PAY-007 | SCR-10 | WF-CHECKOUT-001 | Partial | Not run | E1 "Unpaid checkout rejected"; company credit not built |
| AC-009 | Room move failing midway leaves valid state | REQ-STAY-002 | SCR-07 | WF-STAY-001 | Partial | Not built | Conflict case passes; fault injection not built |
| AC-010 | Not ready before inspection; blocked room stays blocked | REQ-HK-003 | SCR-12 | WF-HK-001 | Passed | Not run | E1 "Cleaning requires…", "Inspection does not clear…" |
| AC-011 | Housekeeper denied other tasks/finance, incl. direct service call | REQ-HK-005, REQ-ACC-009 | SCR-12 | WF-HK-001 | Passed | Not run | E1 "Housekeeper cannot post payments", "…another assignee" |
| AC-012 | Role change and disabled user take effect immediately | REQ-ACC-008 | SCR-16 | WF-ACC-001 | Passed | Not run | E1 "Disabling user…", "Role permission edits…"; E2 step 12 |
| AC-013 | Self-approval / self-escalation / last-owner removal denied | REQ-ACC-007 | SCR-16/17 | WF-APR/ACC | Partial | Not run | Self-approval and last owner pass (E1); self-escalation untested |
| AC-014 | Approval invalid after target changes | REQ-FOLIO-003 | SCR-17 | WF-APR-001 | Passed | Not run | E1 "Stale approval rejected…" |
| AC-015 | Drawer-funded expense reduces cash once; reports reconcile | REQ-EXP-003 | SCR-11/13 | WF-EXP-001 | Passed | Not run | E1 "Expense self approval blocked; posted cash counted once"; E2 step 9 |
| AC-016 | Night charge / close rerun → no duplicates | REQ-CLOSE-003/004 | SCR-14 | WF-DAY-001 | Passed | Not run | E1 "Night posting is repeat-safe"; E2 step 10 |
| AC-017 | Interrupted close resumes without reposting | REQ-CLOSE-005 | SCR-14 | WF-DAY-001 | Not built | Not built | Scenario S5 required |
| AC-018 | Business date crossing midnight stays consistent | REQ-CLOSE-001 | SCR-14 | WF-DAY-001 | Not run | Not run | Needs simulated clock (DEC-030) |
| AC-019 | Effective-dated rate/tax update leaves posted amounts unchanged | REQ-FOLIO-002, REQ-ROOM-003 | SCR-18 | WF-CFG-001 | Partial | Not run | Tax snapshot passes (E1); future-dated rate not built |
| AC-020 | KPI and export drill-down reconcile to sources | REQ-RPT-004/005 | SCR-15 | — | Not run | Not built | Drill-down/exports to build |
| AC-021 | Invalid/missing data and no-result states | REQ-BOOK-001, REQ-GUEST-004 | all | — | Partial | Not run | E1 "Invalid departure", "Guest registration required…"; S1 empty states pending |
| AC-022 | Lost response / stale version / retry | NFR-REL-002 | SCR-09/10 | WF-PAY-001 | Not built | Not built | Fault simulation (SCR-20) to build |
| AC-023 | Responsive and keyboard walkthrough at target sizes | NFR-UX-001/002 | all | — | — | Not run | Screenshots at 5 sizes pending |
| AC-024 | Refresh, fixture reset, clean startup | DEC-022 | SCR-20 | — | **Failed** | Not run | E1 "Fixture totals are derived…": expected ₹10,700, got ₹10,200. Analysis shows the test oracle is wrong (catalogue §2.4) — fix BL-001. **S0 oracle since DEC-052 (shared till, W0-B):** drawer ₹10,200 → ₹10,750 (+ ₹550 POS-F01 restaurant cash); walkthrough drawer path ₹10,750 → ₹11,250 → ₹11,250 → ₹12,750 → ₹12,450 (was ₹10,200 → ₹10,700 → ₹10,700 → ₹12,200 → ₹11,900); open POS checks must be settled before day close (`tests/stream-a2-till.mjs`). |
| AC-025 | DB access / authorisation bypass attempts | NFR-SEC-001 | — | — | Production | Production | M5 |
| AC-026 | Outbox failure and replay | NFR-REL-001 | — | — | Production | Production | M5 |
| AC-027 | Backup restore to separate environment | NFR-DR-001 | SCR-19 | — | Production | Production | M6 |
| AC-028 | Upgrade and rollback/recovery rehearsal | NFR-UPG-001 | — | — | Production | Production | M6 |
| AC-029 | Export of restricted fields obeys masking | REQ-RPT-005, REQ-ACC-004 | SCR-15 | — | Not built | Not built | Field masking not built |

## New acceptance cases from M0

| ID | Test | REQ | Screen | Workflow | Domain status | UI status |
|---|---|---|---|---|---|---|
| AC-030 | Hold expires and releases inventory; refresh does not extend it | REQ-BOOK-004 | SCR-04 | WF-BOOK-001 | Not built | Not built |
| AC-031 | Cash difference needs reason + approval; cashier cannot approve own | REQ-PAY-004, PERM-SOD-004 | SCR-11 | WF-SHIFT-001 | Not built | Not built |
| AC-032 | Duplicate guest suggestion on matching phone | REQ-GUEST-004 | SCR-05 | — | Not built | Not built |
| AC-033 | The five configuration demonstrations work (plan §13) | REQ-CFG-001 | SCR-18 | WF-CFG-001 | Partial | Not run |
| AC-034 | Invoice, registration card, closing report print with demo watermark | REQ-FOLIO-005, REQ-GUEST-005 | SCR-08/05/14 | — | — | Not run |
| AC-035 | Effective-access preview explains allow/deny with deciding rule | REQ-ACC-006 | SCR-16 | WF-ACC-001 | Not built | Not built |
| AC-036 | Simulated offline disables mutations and shows data age | NFR-OFF-001 | SCR-20 | — | Not built | Not built |
| AC-037 | Room block with reason/dates rejects conflicting allocations | REQ-ROOM-004 | SCR-02 | — | Partial (blocks only if no allocation) | Not run |

## Summary (24 Oct 2026)

| Status | Count (AC-001…037, domain level) |
|---|---:|
| Passed | 11 |
| Partial | 9 |
| Failed | 1 (test-oracle defect) |
| Not built | 8 |
| Not run | 2 |
| Production only | 4 |
| UI-only (no domain test) | 2 |

No UI-level case has been run yet.


## Requirement traceability (REQ → screen → workflow → acceptance)

| REQ | Screen | Workflow | Acceptance |
|---|---|---|---|
| REQ-ROOM-001 | SCR-18, SCR-02 | WF-CFG-001 | AC-033 |
| REQ-ROOM-002 | SCR-18, SCR-02 | WF-CFG-001 | AC-033 |
| REQ-ROOM-003 | SCR-18, SCR-02 | WF-CFG-001 | AC-019 |
| REQ-ROOM-004 | SCR-18, SCR-02 | WF-CFG-001 | AC-037 |
| REQ-ROOM-005 | SCR-18, SCR-02 | WF-CFG-001 | AC-037 |
| REQ-ROOM-006 | SCR-18, SCR-02 | WF-CFG-001 | AC-010, AC-037 |
| REQ-BOOK-001 | SCR-04, SCR-03 | WF-BOOK-001 | AC-021 |
| REQ-BOOK-002 | SCR-04, SCR-03 | WF-BOOK-001 | AC-001, AC-021 |
| REQ-BOOK-003 | SCR-04, SCR-03 | WF-BOOK-001 | AC-001 |
| REQ-BOOK-004 | SCR-04, SCR-03 | WF-BOOK-001 | AC-030 |
| REQ-BOOK-005 | SCR-04, SCR-03 | WF-BOOK-001 | AC-003 |
| REQ-BOOK-006 | SCR-04, SCR-03 | WF-BOOK-001 | AC-002 |
| REQ-BOOK-007 | SCR-04, SCR-03 | WF-BOOK-001 | AC-006 |
| REQ-BOOK-008 | SCR-04, SCR-03 | WF-BOOK-001 | AC-034 |
| REQ-RACK-001 | SCR-02, SCR-03 | WF-BOOK-001 | AC-001, AC-023 |
| REQ-RACK-002 | SCR-02, SCR-03 | WF-BOOK-001 | AC-001 |
| REQ-RACK-003 | SCR-02, SCR-03 | WF-BOOK-001 | AC-001 |
| REQ-RACK-004 | SCR-02, SCR-03 | WF-BOOK-001 | AC-002 |
| REQ-RACK-005 | SCR-02, SCR-03 | WF-BOOK-001 | AC-003, AC-023 |
| REQ-GUEST-001 | SCR-05 | WF-STAY-001 | AC-021 |
| REQ-GUEST-002 | SCR-05 | WF-STAY-001 | AC-008 |
| REQ-GUEST-003 | SCR-05 | WF-STAY-001 | AC-029 |
| REQ-GUEST-004 | SCR-05 | WF-STAY-001 | AC-032 |
| REQ-GUEST-005 | SCR-05 | WF-STAY-001 | AC-034 |
| REQ-GUEST-006 | SCR-05 | WF-STAY-001 | AC-029 |
| REQ-GUEST-007 | SCR-05 | WF-STAY-001 | AC-020 |
| REQ-STAY-001 | SCR-06, SCR-07, SCR-10 | WF-STAY-001 / WF-CHECKOUT-001 | AC-004 |
| REQ-STAY-002 | SCR-06, SCR-07, SCR-10 | WF-STAY-001 / WF-CHECKOUT-001 | AC-009 |
| REQ-STAY-003 | SCR-06, SCR-07, SCR-10 | WF-STAY-001 / WF-CHECKOUT-001 | AC-003 |
| REQ-STAY-004 | SCR-06, SCR-07, SCR-10 | WF-STAY-001 / WF-CHECKOUT-001 | AC-008 |
| REQ-STAY-005 | SCR-06, SCR-07, SCR-10 | WF-STAY-001 / WF-CHECKOUT-001 | AC-001 |
| REQ-STAY-006 | SCR-06, SCR-07, SCR-10 | WF-STAY-001 / WF-CHECKOUT-001 | AC-005 |
| REQ-STAY-007 | SCR-06, SCR-07, SCR-10 | WF-STAY-001 / WF-CHECKOUT-001 | AC-007 |
| REQ-STAY-008 | SCR-06, SCR-07, SCR-10 | WF-STAY-001 / WF-CHECKOUT-001 | AC-033 |
| REQ-FOLIO-001 | SCR-08 | WF-FOLIO-001/002 | AC-006 |
| REQ-FOLIO-002 | SCR-08 | WF-FOLIO-001/002 | AC-019 |
| REQ-FOLIO-003 | SCR-08 | WF-FOLIO-001/002 | AC-014 |
| REQ-FOLIO-004 | SCR-08 | WF-FOLIO-001/002 | AC-006 |
| REQ-FOLIO-005 | SCR-08 | WF-FOLIO-001/002 | AC-034 |
| REQ-FOLIO-006 | SCR-08 | WF-FOLIO-001/002 | AC-007 |
| REQ-FOLIO-007 | SCR-08 | WF-FOLIO-001/002 | AC-005, AC-016 |
| REQ-PAY-001 | SCR-09, SCR-11 | WF-PAY-001 / WF-SHIFT-001 | AC-006 |
| REQ-PAY-002 | SCR-09, SCR-11 | WF-PAY-001 / WF-SHIFT-001 | AC-006 |
| REQ-PAY-003 | SCR-09, SCR-11 | WF-PAY-001 / WF-SHIFT-001 | AC-007 |
| REQ-PAY-004 | SCR-09, SCR-11 | WF-PAY-001 / WF-SHIFT-001 | AC-031 |
| REQ-PAY-005 | SCR-09, SCR-11 | WF-PAY-001 / WF-SHIFT-001 | AC-031 |
| REQ-PAY-006 | SCR-09, SCR-11 | WF-PAY-001 / WF-SHIFT-001 | AC-005, AC-022 |
| REQ-PAY-007 | SCR-09, SCR-11 | WF-PAY-001 / WF-SHIFT-001 | AC-008 |
| REQ-HK-001 | SCR-12 | WF-HK-001 | AC-001 |
| REQ-HK-002 | SCR-12 | WF-HK-001 | AC-010 |
| REQ-HK-003 | SCR-12 | WF-HK-001 | AC-010 |
| REQ-HK-004 | SCR-12 | WF-HK-001 | AC-037 |
| REQ-HK-005 | SCR-12 | WF-HK-001 | AC-011 |
| REQ-HK-006 | SCR-12 | WF-HK-001 | AC-023 |
| REQ-EXP-001 | SCR-13 | WF-EXP-001 | AC-015 |
| REQ-EXP-002 | SCR-13 | WF-EXP-001 | AC-013, AC-015 |
| REQ-EXP-003 | SCR-13 | WF-EXP-001 | AC-015 |
| REQ-EXP-004 | SCR-13 | WF-EXP-001 | AC-021 |
| REQ-EXP-005 | SCR-13 | WF-EXP-001 | AC-021 |
| REQ-CLOSE-001 | SCR-14 | WF-DAY-001 | AC-018 |
| REQ-CLOSE-002 | SCR-14 | WF-DAY-001 | AC-016 |
| REQ-CLOSE-003 | SCR-14 | WF-DAY-001 | AC-016 |
| REQ-CLOSE-004 | SCR-14 | WF-DAY-001 | AC-016 |
| REQ-CLOSE-005 | SCR-14 | WF-DAY-001 | AC-017 |
| REQ-CLOSE-006 | SCR-14 | WF-DAY-001 | AC-007 |
| REQ-RPT-001 | SCR-01, SCR-15 | — | AC-001 |
| REQ-RPT-002 | SCR-01, SCR-15 | — | AC-020 |
| REQ-RPT-003 | SCR-01, SCR-15 | — | AC-001, AC-020 |
| REQ-RPT-004 | SCR-01, SCR-15 | — | AC-020 |
| REQ-RPT-005 | SCR-01, SCR-15 | — | AC-029 |
| REQ-RPT-006 | SCR-01, SCR-15 | — | AC-020 |
| REQ-ACC-001 | SCR-16 | WF-ACC-001 | AC-012 |
| REQ-ACC-002 | SCR-16 | WF-ACC-001 | AC-012 |
| REQ-ACC-003 | SCR-16 | WF-ACC-001 | AC-011 |
| REQ-ACC-004 | SCR-16 | WF-ACC-001 | AC-029 |
| REQ-ACC-005 | SCR-16 | WF-ACC-001 | AC-014 |
| REQ-ACC-006 | SCR-16 | WF-ACC-001 | AC-035 |
| REQ-ACC-007 | SCR-16 | WF-ACC-001 | AC-013 |
| REQ-ACC-008 | SCR-16 | WF-ACC-001 | AC-012 |
| REQ-ACC-009 | SCR-16 | WF-ACC-001 | AC-011 |
| REQ-AUD-001 | SCR-17 | WF-APR-001 | AC-012 |
| REQ-AUD-002 | SCR-17 | WF-APR-001 | AC-014 |
| REQ-CFG-001 | SCR-18 | WF-CFG-001 | AC-033 |
| REQ-CFG-002 | SCR-18 | WF-CFG-001 | AC-002, AC-005 |
| REQ-BKP-001 | SCR-19 | — | AC-027 |
| NFR-SEC-001 | — | — | AC-025 |
| NFR-SEC-002 | — | — | AC-025 (prod) |
| NFR-PRIV-001 | — | — | QG-P12 |
| NFR-REL-001 | — | — | AC-005, AC-026 |
| NFR-REL-002 | — | — | AC-022 |
| NFR-CONC-001 | — | — | Production M5 |
| NFR-PERF-001 | — | — | Production M6 |
| NFR-UX-001 | — | — | AC-023 |
| NFR-UX-002 | — | — | AC-023, QG-P9 |
| NFR-OFF-001 | — | — | AC-036 |
| NFR-DR-001 | — | — | AC-027 |
| NFR-COMP-001 | — | — | Production gate |
| NFR-OBS-001 | — | — | Production M5 |
| NFR-UPG-001 | — | — | AC-028 |
