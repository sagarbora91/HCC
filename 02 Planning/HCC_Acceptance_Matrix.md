# Hotel Control Centre — Acceptance Matrix

Version: 1.0 (M0) · 24 October 2026
Statuses: **Passed** · **Failed** · **Partial** (some sub-cases evidenced) · **Not run** · **Not built** · **Deferred with approval** · **Production** (not applicable to the prototype).
A test counts as Passed only with an evidence location naming the build and scenario. Code existing is not evidence.

Evidence sources available today:
- **E1** = `03 Prototype/evidence/domain-tests.json` — domain unit tests, recorded run 2026-10-24T04:01Z (27/28 passed). Mock-service level only; no UI.
- **E2** = `05 Delivery Evidence/M0/m0_walkthrough_sim_output.txt` — S0 walkthrough executed against the domain, 2026-10-24T04:20Z.
- **E3 (25 Oct 2026, W0-D)** = `05 Delivery Evidence/Acceptance/` (`BL023_Walkthrough.md` browser 15/15 on v6-equivalent bundle; `D6_TabS7.md` physical Tab S7 on APK v6, 14 Pass / 1 Fail / 2 Not run; `D5_Roles.md` Part 2 21/21 and `D5_AC.md` Part 3 17/17, emulator API 35 on APK v8 SHA-256 `2BA3FE68…8A52C3`; WebView 83 AC-01/AC-02 re-tests). Combined: `W0-D_Acceptance_Report.md`. Rows below show UI status where W0-D evidence exists; unlisted rows remain Not run / Not built.

| ID | Test | REQ | Screen | Workflow | Domain status | UI / E2E status | Evidence / gap |
|---|---|---|---|---|---|---|---|
| AC-001 | Reset → checkout → cleaning journey reconciles | REQ-STAY-005, REQ-RPT-003 | SCR-01..15 | all | Passed | Passed (browser, emulator API 35 v8) | E2; E1 "End-to-end close…"; UI walkthrough pending. **W0-D 25 Oct:** BL023 steps 7-8, 10; D5_AC.md AC-M05..M08; Reset on Tab S7 (D6 #13) |
| AC-002 | Duplicate exclusive room allocation rejected; original intact | REQ-BOOK-006 | SCR-03/04 | WF-BOOK-001 | Passed | Not run | E1 "Overlapping room reservation…", "Adjacent… accepted" |
| AC-003 | Date change/extension into a future booking → conflict, original preserved | REQ-STAY-003 | SCR-07 | WF-STAY-001 | Passed | Not run | E1 "Room move conflict…"; alternatives list not built |
| AC-004 | Check-in to dirty or blocked room denied | REQ-STAY-001 | SCR-06 | WF-STAY-001 | Passed | Not run | E1 "Dirty room…", "Blocked room…" |
| AC-005 | Repeated receipt/charge/checkout → one effect | REQ-PAY-006, REQ-STAY-006 | SCR-08/09/10 | WF-PAY/CHECKOUT | Partial | Not run | E1 covers repeated night posting only; receipt & checkout keys untested |
| AC-006 | Worked fixture: ₹4,400 invoice, advance once, deposit separate | REQ-FOLIO-004 | SCR-08 | WF-FOLIO-001 | Passed | Passed (browser; emulator API 35 v8) | E1 "Worked ₹4,400…"; E2 multi-day. **W0-D 25 Oct:** BL023 steps 2, 3b, 6, 7 (₹4,400 invoice, ₹1,000 advance once, ₹500 deposit separate); D5_AC.md AC-M06 |
| AC-007 | Refund and correction: eligible source, approval, linked reversal | REQ-PAY-003, REQ-FOLIO-006 | SCR-08/17 | WF-PAY/FOLIO-002 | Partial | Not built | E1 "Refund cannot exceed…"; request/approve/execute & credit note not built |
| AC-008 | Unpaid checkout blocked or via approved company credit | REQ-STAY-004, REQ-PAY-007 | SCR-10 | WF-CHECKOUT-001 | Partial | Not run | E1 "Unpaid checkout rejected"; company credit not built |
| AC-009 | Room move failing midway leaves valid state | REQ-STAY-002 | SCR-07 | WF-STAY-001 | Partial | Not built | Conflict case passes; fault injection not built |
| AC-010 | Not ready before inspection; blocked room stays blocked | REQ-HK-003 | SCR-12 | WF-HK-001 | Passed | Partial (browser, emulator) | E1 "Cleaning requires…", "Inspection does not clear…". **W0-D 25 Oct:** Room 105 Ready only after Neha inspects (BL023 step 8; D5_AC.md); blocked-room case not run |
| AC-011 | Housekeeper denied other tasks/finance, incl. direct service call | REQ-HK-005, REQ-ACC-009 | SCR-12 | WF-HK-001 | Passed | Partial (browser, emulator; navigation-level) | E1 "Housekeeper cannot post payments", "…another assignee". **W0-D 25 Oct:** Housekeeper has no folio/cash pages, no ₹ on tasks (BL023 step 5; D5_Roles.md; D5_AC.md AC-M07); direct service call stays domain-only; R-01 open |
| AC-012 | Role change and disabled user take effect immediately | REQ-ACC-008 | SCR-16 | WF-ACC-001 | Passed | Passed with deviation (browser) | E1 "Disabling user…", "Role permission edits…"; E2 step 12. **W0-D 25 Oct:** BL023 step 12: policy v2, user disabled, no pages; no explicit denied-command audit row (UI blocks first); BL-02 source-fixed, no UI re-test |
| AC-013 | Self-approval / self-escalation / last-owner removal denied | REQ-ACC-007 | SCR-16/17 | WF-APR/ACC | Partial | Partial (browser, emulator) | Self-approval and last owner pass (E1); self-escalation untested. **W0-D 25 Oct:** Reception cannot approve own APR-1123 (BL023 step 5); self-escalation not run |
| AC-014 | Approval invalid after target changes | REQ-FOLIO-003 | SCR-17 | WF-APR-001 | Passed | Not run | E1 "Stale approval rejected…" |
| AC-015 | Drawer-funded expense reduces cash once; reports reconcile | REQ-EXP-003 | SCR-11/13 | WF-EXP-001 | Passed | Passed (browser; emulator API 35 v8) | E1 "Expense self approval blocked; posted cash counted once"; E2 step 9. **W0-D 25 Oct:** BL023 step 9 (EXP-1145 ₹300, drawer ₹12,450); D5_AC.md AC-M06 |
| AC-016 | Night charge / close rerun → no duplicates | REQ-CLOSE-003/004 | SCR-14 | WF-DAY-001 | Passed | Passed (browser) | E1 "Night posting is repeat-safe"; E2 step 10. **W0-D 25 Oct:** BL023 step 10: 8 nights posted, rerun refused "closed", totals unchanged; phone UI offers no re-close (D5_AC.md) |
| AC-017 | Interrupted close resumes without reposting | REQ-CLOSE-005 | SCR-14 | WF-DAY-001 | Not built | Not built | Scenario S5 required |
| AC-018 | Business date crossing midnight stays consistent | REQ-CLOSE-001 | SCR-14 | WF-DAY-001 | Not run | Not run | Needs simulated clock (DEC-030) |
| AC-019 | Effective-dated rate/tax update leaves posted amounts unchanged | REQ-FOLIO-002, REQ-ROOM-003 | SCR-18 | WF-CFG-001 | Partial | Not run | Tax snapshot passes (E1); future-dated rate not built |
| AC-020 | KPI and export drill-down reconcile to sources | REQ-RPT-004/005 | SCR-15 | — | Not run | Partial (browser, emulator) | Drill-down/exports to build. **W0-D 25 Oct:** Reports drill-down totals match (BL023 step 11; phone = desktop, D5_AC.md AC-M08c); CSV export + share sheet pass on Tab S7 v6 and emulator v8; export masking not built |
| AC-021 | Invalid/missing data and no-result states | REQ-BOOK-001, REQ-GUEST-004 | all | — | Partial | Not run | E1 "Invalid departure", "Guest registration required…"; S1 empty states pending |
| AC-022 | Lost response / stale version / retry | NFR-REL-002 | SCR-09/10 | WF-PAY-001 | Not built | Not built | Fault simulation (SCR-20) to build |
| AC-023 | Responsive and keyboard walkthrough at target sizes | NFR-UX-001/002 | all | — | — | Partial | Screenshots at 5 sizes pending. **W0-D 25 Oct:** Browser 1366 px, phone 412 px, Tab S7 1078/673 px checked (BL023, D5, D6); keyboard-only and the 5-size screenshot set not run; T-01/T-02 open |
| AC-024 | Refresh, fixture reset, clean startup | DEC-022 | SCR-20 | — | **Failed** | Passed (UI Reset) | E1 "Fixture totals are derived…": expected ₹10,700, got ₹10,200. Analysis shows the test oracle is wrong (catalogue §2.4) — fix BL-001. **S0 oracle since DEC-052 (shared till, W0-B):** drawer ₹10,200 → ₹10,750 (+ ₹550 POS-F01 restaurant cash); walkthrough drawer path ₹10,750 → ₹11,250 → ₹11,250 → ₹12,750 → ₹12,450 (was ₹10,200 → ₹10,700 → ₹10,700 → ₹12,200 → ₹11,900); open POS checks must be settled before day close (`tests/stream-a2-till.mjs`).. **W0-D 25 Oct:** Reset → S0 reloaded, drawer ₹10,750 (D6 #13 Tab S7; D5_Roles.md Reset on API 35 v8). Domain status unchanged here; full suite now 502/502 (HANDOFF Session 11), lead to confirm the E1 oracle row |
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
| AC-034 | Invoice, registration card, closing report print with demo watermark | REQ-FOLIO-005, REQ-GUEST-005 | SCR-08/05/14 | — | — | Partial (invoice print preview on emulator v8, report print on Tab S7 v6, A4 on v8; registration card and closing report not run; see W0-D report) |
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

UI-level update (W0-D, 25 Oct 2026): of AC-001…037, UI status is **Passed** for AC-001, AC-006, AC-015, AC-016, AC-024 (UI Reset); **Passed with deviation** AC-012; **Partial** AC-010, AC-011, AC-013, AC-020, AC-023, AC-034; all others unchanged (Not run / Not built). Open defects affecting UI rows: T-01, T-02, AC-04 (S3), AC-03, R-01 (S4); see `05 Delivery Evidence/Acceptance/W0-D_Acceptance_Report.md`. Domain-level counts above are the 24 Oct figures and were not re-derived.


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
