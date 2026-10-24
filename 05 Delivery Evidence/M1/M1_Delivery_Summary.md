# M1 — Prototype Foundation · Delivery Summary

Date: 24 Oct 2026 · Build: `03 Prototype` (files written 24 Oct 2026) · Approval basis: APR-003

## Delivered (BL-001 … BL-010)

| ID | Item | Status | Evidence |
|---|---|---|---|
| BL-001 | Stale drawer oracle fixed (₹10,700 → ₹10,200) | Done | `03 Prototype/evidence/domain-tests.json` |
| BL-002 | Code split into app/, features/, components/, domain/, services/mock/, fixtures/, styles/ | Done | Type check passes (cloud workspace and this PC) |
| BL-003 | Simulated clock for all timestamps, including audit | Done | Tests "Clock: …", "Determinism: …" |
| BL-004 | Typed errors (FORBIDDEN, NOT_FOUND, NOT_AVAILABLE, INVALID_TRANSITION, VALIDATION, STALE_VERSION, DAY_CLOSED) | Done | Tests "Typed error: …" |
| BL-005 | Idempotency keys on all UI commands; duplicate → "Already recorded" | Done | Tests "Idempotency: …"; browser smoke test |
| BL-006 | Fault simulation (slow, reject, lost response, stale) in demo toolbar | Done | Tests "Fault: …"; browser smoke test |
| BL-007 | Scenarios BASE, S0, S1, S2, S3, S6; versioned storage with safe reset | Done (S4/S5 shown as not yet built) | Tests "Scenario …", "Persistence: …" |
| BL-008 | Grouped navigation, role landing pages, header with business date/clock/mode/demo label | Done | Browser smoke test |
| BL-009 | Fixture renamed "HCC Demo Residency"; Accountant, Maintenance, Tech admin users; 23 Oct recorded closed | Done | Test "Fixture: …" |
| BL-010 | `npm start -- --lan`, README | Done | `03 Prototype/README.md` |

Extra rule added: a user cannot change the permissions of their own role (PERM-SOD-002).

## Checks

| Check | Result | Where run |
|---|---|---|
| Type check | Passed | Cloud workspace (Linux, Node 22) and this PC's VM |
| Build | Passed — 359 KB single file | Cloud workspace |
| Tests | **56/56 passed** (28 original + 28 M1) | Cloud workspace |
| Browser smoke test (Chromium, emulated) | Passed. All 10 pages opened with no console errors. Lost-response payment → retry → one receipt and an "Already recorded" message. Housekeeper at 390×844 lands on own tasks with no horizontal page scroll | Cloud workspace |
| Windows `npm test` / `npm run build` on this PC | **Not run** — please run once (README) | — |

## Known limitations

- Coarse 12-permission model. Full matrix → M3 (BL-024).
- "Post stay charges" still pre-posts future nights → M2 (BL-017).
- M0 walkthrough script in `05 Delivery Evidence/M0` imports the pre-M1 test bundle. It is kept as the historical M0 record; the S0 walkthrough is now an automated test.
- `tests/.generated/domain.mjs` (old build artefact) is no longer used. Safe to delete.
