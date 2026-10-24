# Hotel Control Centre — Delivery Summary: M0 Review Package

Date: 24 October 2026 · Milestone: **M0 — Specification** · Status: **Ready for owner review (APR-003 pending)**

## 1. What M0 is

M0 turns the conversation export and execution plan into a traceable specification so you can approve the prototype structure and walkthrough before more screens are built. It also captures what the existing half-built prototype in `03 Prototype` already does.

## 2. Package contents (`V:\Codex\Hotel Control Centre\02 Planning\`)

| Document | What to review |
|---|---|
| `HCC_Delivery_Summary.md` | This page — questions to answer, approval request |
| `HCC_Master_PRD_v1.md` | All 26 areas, prototype depth per area, 99 requirement IDs, current build status |
| `HCC_Decision_Register.md` | DEC-01…20 preserved; DEC-021…043 new (3 need your decision) |
| `HCC_Prototype_Spec_v1.md` | **Sitemap** (§1), role landing pages, 20 screen contracts, visual system |
| `HCC_RBAC_Matrix_v1.md` | 9 roles × 45 actions, scopes, limits, approval routes, separation-of-duty rules |
| `HCC_Business_Workflow_Spec_v1.md` | State machines for booking, stay, checkout, folio, payments, housekeeping, expenses, approvals, shift, day close, access, config |
| `HCC_Financial_Rulebook.md` | Money rules, the ₹4,400 worked fixture, drawer formula, report dictionary (synthetic tax) |
| `HCC_Fixture_and_Scenario_Catalog.md` | 24-room fixture, scenarios S0–S6, **12-step walkthrough with expected figures** (§4) |
| `HCC_Acceptance_Matrix.md` | AC-001…037 with current evidence status |
| `HCC_Quality_Gates.md` | Prototype gates QG-P1…12 and evidence rules |
| `HCC_Implementation_Backlog.md` | BL-001…037 for M1–M3 with dependencies and sizes |

Evidence: `V:\Codex\Hotel Control Centre\05 Delivery Evidence\M0\m0_walkthrough_sim.mjs` and `m0_walkthrough_sim_output.txt`.

Not created yet (per plan, created when due): Source Reuse Register (no third-party code imported — DEC-043), Architecture & Contracts, SQL Schema, Release Runbook (M4).

## 3. Facts inspected vs assumed vs unverified

**Inspected (24 Oct 2026):**
- Prototype source: `src/domain.ts` (mock service, 22 commands), `src/main.tsx` (10 pages, 14 dialogs), `src/styles.css`, build/test scripts.
- Type check passes.
- Recorded test run 2026-10-24T04:01Z: 27/28 passed. The failure is a wrong expected value in the test (₹10,700 vs a correctly derived ₹10,200), not a domain bug.
- The S0 walkthrough ran end to end against the current domain code across 24→26 Oct. Room 105 invoice ₹4,400, balance ₹0, deposit ₹0, room Ready after inspection, day close rerun blocked, disabled user denied.

**Assumed (prototype defaults, labelled Proposed):** 24-room property, 3 types, rates ₹2,000/₹3,000/₹4,500, 10% synthetic tax, ₹250 discount threshold, inspection on, 14:00/11:00 times, 2-hour holds, limits in RBAC §6.

**Unverified / not done:** the UI has not been clicked through in a browser this session; no screenshots or responsive checks; no physical Android test; GST rules, SQL Server 2025 Express limits and third-party repository licences are all unverified.

**Findings from the code inspection that matter for approval:**
1. The build has a "post full stay" action that posts future nights, so the guest can be checked out two days early (DEC-029).
2. A cashier cannot close their own shift; only Owner/Manager can.
3. Audit timestamps use the real clock, which breaks determinism (DEC-030).
4. Refunds are gated only by the `approve` permission, with no request → approve → execute chain (DEC-031).
5. Any Owner account is protected from being disabled, rather than only the last active one (DEC-033).
6. The fixture property is named "Saagar Residency", close to the real business name (DEC-037).
7. The 23 Oct night lines exist but 23 Oct is not recorded as closed.

## 4. Questions for you

### Needed now (prototype)

| # | Question | Proposed default |
|---|---|---|
| Q1 | **Night posting / walkthrough (DEC-029).** Should the walkthrough run across simulated days 24→26 Oct with a night audit each night (option B), or stay a single day with "post full stay" (option A)? | **B** — realistic and exercises night audit twice |
| Q2 | **Fixture name (DEC-037).** Rename "Saagar Residency" to "HCC Demo Residency"? | **Yes** |
| Q3 | **Sitemap and role landing pages** (Prototype Spec §1). Approve as drafted? | Approve |
| Q4 | **Prototype policy defaults as a bundle:** DEC-024, 025, 026, 027, 031, 033, 035, 038, 039, 040, 041, 042, plus the RBAC limits in §6. Accept for the prototype only (not hotel policy)? | Accept |
| Q5 | **Walkthrough script** (Fixture Catalogue §4). Approve the 12 steps and expected figures as the acceptance demo? | Approve |

### Can wait until production (M4) — no answer needed now

Real property (rooms, floors, types, legal entity, GSTIN) · actual staff roles and shifts · business-day cutoff · early/late and day-use charges · advance, deposit and cancellation/no-show policy · whether guests may depart unpaid and who approves · post-checkout invoice correction method (DEC-028: adjustment vs reopen) · discount/refund authority levels · GST classification and invoice series (needs qualified adviser) · guest ID data you are permitted to collect and its retention · foreign-guest reporting obligations · backup destination, acceptable data loss/downtime and responsible person · Windows host and Android devices · LAN/TLS setup.

## 5. Approval requested (APR-003)

Please confirm, with any changes:

> "M0 package v1 approved for prototype structure, sitemap, walkthrough and proposed defaults (answers Q1–Q5 as follows: …)."

Approval covers the **prototype only**. It does not approve hotel operating policy, tax treatment or production development.

## 6. Next bounded milestone after approval

**M1 — Prototype foundation** (Backlog BL-001…010): fix the stale test, restructure the code into feature folders, deterministic clock, typed errors, idempotency keys, fault simulation, scenario loader S0–S6, grouped navigation, LAN start option and README. Then **M2** (calendar, guest profile, check-in stepper, payment dialog, refunds, company credit, Playwright walkthrough) and **M3** (full access model, configuration panel, cashier shift, reports, responsive/accessibility evidence).
