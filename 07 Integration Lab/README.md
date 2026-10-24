# HCC Integration Lab

Isolated qualification of donor components (plan: `02 Planning/HCC_Component_Selection_and_Assembly_Plan_v1.md` §7). Nothing here is used by `03 Prototype`, `04 Android` or a production build.

## Run
From the workspace root:

```
node "07 Integration Lab/run.mjs"
```

Uses the esbuild already installed in `03 Prototype/node_modules` (no new packages). Writes `evidence/lab-results.json`. Bundles go to `.generated/` (disposable).

## Rules kept
- Donor checkouts in `06 Reference Repositories` are only read; `git status` stays clean (checked after each run).
- "upstream" suites = the donor's own spec file, unchanged, executed through `shims/vitest.mjs` (a minimal matcher shim — **not vitest**) and `shims/nestjs-common.mjs`. They test the unmodified donor function.
- "hcc" suites = original HCC tests, including runs against the live prototype mock (`03 Prototype/src/services/mock`).
- Adapted copies carry the donor's Apache-2.0 LICENSE beside them and a change notice in the file header. Provenance: `evidence/source-reuse.json`.

## Candidates

| Folder | Candidate | Result |
|---|---|---|
| `qualifications/H01-reservation-transitions` | HAIP reservation state machine | Reference only. Original HCC transition map + 42-cell conformance test against the mock. No code copied. |
| `qualifications/H02-los-tier-pricing` | HAIP LOS/occupancy pricing util | Adapted (paise/bps, validated, half-open bands, single rounding). Lab only; activation waits for an LOS-discount decision. |
| `qualifications/H03-refund-remainder` | HAIP refund remainder | Reference only. HCC's existing `folioRefundable` passes the donor scenarios and is stricter. |
| `qualifications/H04-property-calendar-date` | HAIP timezone calendar date | Adapted (invalid zone = error). Target: production clock adapter (A4). |
| `qualifications/O01-O02-room-night-helpers` | Open Hotel PMS check-in night / checkout release | O01 rejected (no gap), O02 reference/defer. |

Latest run: see `05 Delivery Evidence/Assembly/A0_A1_Result.md`.
