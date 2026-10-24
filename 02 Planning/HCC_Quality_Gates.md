# Hotel Control Centre — Quality Gates

Version: 1.0 (M0) · 24 October 2026 · Status: Proposed

## 1. Evidence rules

- Every result names the build (commit or file timestamp), scenario (S0–S6) and date.
- Statuses: Passed / Failed / Not run / Deferred with approval. "Code exists" is never Passed.
- Screenshots prove layout only. Mock payments prove workflow only. Emulated viewports are labelled emulated; physical Android only after a named device test.
- Any test whose expected value is changed must record why (e.g. BL-001 oracle correction) in the delivery summary.

## 2. Prototype gates

| Gate | Check | Threshold | Tool / method |
|---|---|---|---|
| QG-P1 | Type check | 0 errors | `npm run typecheck` (passed 24 Oct 2026, current build) |
| QG-P2 | Build | Produces `dist/index.html` and starts via `npm start` | `npm run build` |
| QG-P3 | Domain tests | 100% pass; no skipped critical tests | `npm test` → `evidence/domain-tests.json` |
| QG-P4 | Financial oracle | Worked fixture exact to the paisa; S0 drawer figures match catalogue §4 | Unit test + S0 simulation |
| QG-P5 | Permission tests | Every PERM with "—" has at least one denial test per affected role group | Generated matrix test |
| QG-P6 | Browser E2E | S0 walkthrough steps 1–12 automated in Chromium at 1440×900 and 390×844 (housekeeper steps) | Playwright |
| QG-P7 | Exceptions | AC-002/003/004/005/008/010/011/013/014/016/017/022 automated | Unit + E2E |
| QG-P8 | Responsive | No page-level horizontal scroll, no clipped dialog actions at 5 sizes; 200% zoom on booking/payment/check-in | Screenshots + manual review |
| QG-P9 | Accessibility | Labels on all inputs, focus visible, dialogs trap/restore focus, status not by colour alone | axe-core scan + keyboard pass |
| QG-P10 | Determinism | Same scenario + same commands → identical state and figures on any date | Run tests with a shifted real clock |
| QG-P11 | Reset | Reset returns exactly the seed; schema-version mismatch resets safely | Unit + manual |
| QG-P12 | Data hygiene | No real personal data; all documents watermarked demo | Review |

**Critical failures (block any gate):** double booking, duplicate money, unexplained reconciliation difference, unauthorised access, lost history.

## 3. Production gates (summary; detailed at M4)

SQL Server integration tests · parallel booking/payment requests · transaction fault injection · server-side authorisation tests for commands, queries, exports and downloads · outbox replay idempotency · migration up/rehearsal · load at agreed targets · backup restore drill with measured RPO/RTO · dependency licence scan / SBOM.
