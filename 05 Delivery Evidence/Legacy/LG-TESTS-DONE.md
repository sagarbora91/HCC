# LG-TESTS DONE

Lane lg-tests / Sonnet / branch `claude/lg-tests` / worktree `E:\Projects\HCC\worktrees\lg-tests` / base ced9861e. **Completed after lg-import landed.**

## Commits
See `git log --oneline ced9861e..HEAD` (golden fixture + tests; dry-run script + checklist; this file).

## Files
- `08 Production/tests/Hcc.Legacy.Tests/Golden/Fixtures/make_golden.py` (stdlib generator, fictional data) and `Golden/Fixtures/run-golden/*.csv` (guests, companies, lgc_guest_map, lgc_stay, lgc_room_segment, lgc_bill, lgc_receipt, lgc_tags). Folder is `run-golden`, not `run-fictional`, to avoid colliding with lg-import's files.
- `Golden/GoldenImportTests.cs` (two tests), `Golden/GoldenWorld.cs` (stub, throws until wired), `Golden/Golden.csproj.fragment.txt` (csproj item to add).
- `03 Prototype/tests/stream-legacy-golden.mjs`; `tools/legacy/dryrun/Run-LegacyDryRun.ps1`; `05 Delivery Evidence/Legacy/LG-DryRun-Checklist.md`.

## What was built
- Dataset: 2 FYs (2025-26, 2026-27), 12 guests, 20 stay rows (one `Duplicate` = 2025-26/111, real copy 2026-27/201), 20 bills (one `out_of_fy` = 2025-26/10; one bill without a stay = 2026-27/10; one unpaid P/N bill), 24 receipts (2 refunds, 5 advances, 1 security), 1 multi-room stay (105), 1 shift (106, segment kind `shift`), company with guests 3/4/5 sharing a fictional GSTIN, OTA tag AGODA (guest 4, stays 104 and 205). The dropped FY-boundary bill and receipt are by design absent from the CSVs (dedup: billsDropped 1, receiptsDropped 1).
- Hand-worked figures (also in the test comments): 2025-26 net 79,993 rupees, room GST 7,608, F&B 8,200, 12 receipts 61,870, 1 refund 500; 2026-27 net 51,565, GST 2,280, F&B 3,700, 10 receipts 39,145, 1 refund 300. The generator's printout agreed with the hand sums for 2025-26 net.
- Tests: golden dry run asserts every reconciliation figure, `match`, row counts and that nothing is left; second test with `netPaise` of 2026-27 one rupee out expects 2025-26 committed (11/10/13 rows) and 2026-27 rolled back (0/0/0) with one difference.
- Prototype test: runs (4 tests) on an inline contract-shaped list; checks `M.LEGACY_SAMPLE` only if lg-ui exports it (it is not yet, so that test returns early).
- Dry-run script: host command tried first (`legacy-import --folder --parts --dry-run --out`; flags are my assumption), HTTP fallback `POST /commands/legacy.import` with `-Token`. Parse-checked only; not run.

## Tests
Merged `claude/lg-import` (contains lg-schema). Wired `GoldenWorld` over lg-import's `LegacyImportWorld` (writes the test's own hand-built totals.json into a temp copy of `run-golden`, layout `fy.<fy>.afterDedup`). Fixture adapted to the facts: guest_map helper columns, security receipt row ids negative, receiptsCount = advance + payment only (2026-27: 9 / 38,145).
Command (through the lock): `dotnet test tests/Hcc.Legacy.Tests -maxcpucount:1` -> **Passed 33, Failed 0** (31 from lg-schema/lg-import + my 2 golden tests; first run 31/2 failed on a missing totals.json, fixed). Prototype golden test: 4 pass under a plain node harness (not `npm test`; `legacy-sample.ts` not available, contract-shaped fallback).

## Open items
1. `stream-legacy-golden.mjs` should use lg-ui's real `legacy-sample.ts` rollups once merged (it checks `M.LEGACY_SAMPLE` only if exported).
2. `Run-LegacyDryRun.ps1` is HTTP-only (`POST /api/v1/commands/legacy.import`, local server, `-Token`), parse-checked, never run; it sends the Idempotency-Key header the API requires; bearer-token auth is assumed.

## Integration needed
- `Hcc.Legacy.Tests.csproj`: add `<None Include="Golden\Fixtures\run-golden\*.csv" CopyToOutputDirectory="PreserveNewest" />`.
- Merge order: after lg-import (per the briefs).

## HANDOFF bullet
LG tests lane: fictional golden run folder (2 FYs, 12 guests, 20 stays, 20 bills, 24 receipts, all edge cases) with hand-computed reconciliation tests (golden match + wrong-totals FY rollback) written but not yet compiled, prototype rollup test, `Run-LegacyDryRun.ps1` (masked 20-guest report) and the cut-over checklist; wire `GoldenWorld` after lg-import/lg-schema merge.
