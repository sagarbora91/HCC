# Item 2 — Wave 1 server completion + APK v11 fixes (ready to run)

Prepared 25 Oct 2026 (end of Session 11b). Start a new session with: **"Start item 2 from 02 Planning/agents/W1_Item2_Briefs.md"**.
Rules: maximum parallel agents, Sonnet wherever possible (Opus for money/statutory rules, merging and verification); one emulator at a time; never push unless the owner says so. Base for server work: branch `claude/wave1-integration` (212 .NET tests on `.\SQLEXPRESS`, see `08 Production\WAVE1_PARALLEL_RULES.md`). Base for prototype fixes: `main` (773 tests, APK v10).

## Parallel agents (own worktree each under `E:\Projects\HCC\worktrees\`)
| # | Agent | Model | Branch from | Scope |
|---|---|---|---|---|
| A | P6 Closing & reports | Opus | `claude/wave1-integration` | Resumable business-day close, late-write barrier (DAY_CLOSED), immutable closing snapshot, owner reports from the same transactions; cash-book lock DEC-065 server-side; golden figures from the prototype (new GST: drawer ₹10,725, room 105 ₹4,265). Migrations block 0500–0599. |
| B | P7 Deployment & recovery | Sonnet | `claude/wave1-integration` | Windows service hosting, LAN TLS certificate, backup/restore drill + Task Scheduler scripts, installer outline, APK server-address setting; `// ETP-REUSE:` markers (ETP not on this PC). No migrations. |
| C | GST master + guest IDs server-side | Opus | `claude/wave1-integration` | DEC-069 dated GST master (port `03 Prototype/src/domain/gst.ts` rules + tests; CGST/SGST ledgers) replacing the fixed 10 % `ITaxRateSource`; DEC-068 full guest ID numbers (P3 stores last 4 today) with encryption at rest + masked reads + audited Show ID. Migrations 0600–0699. |
| D | Merge Wave 1 prototype changes with main | Opus | new branch from `main` | Bring P5 web clients (`03 Prototype/src/clients/*`, `build:production`) from `claude/wave1-integration` onto today's `main` (Wave 0.5 + polish + GST); all prototype tests + both builds green. Prototype files only. |
| E | Prototype fixes for APK v11 | Sonnet | `main` | V10-1 (S3) phone invoice + A4 print: CGST/SGST split like desktop; V10-2 Show-ID footnote only for ID-view roles; PB-1 Escape after search palette; PB-2 disable Reverse/Request refund on locked days; PB-3 phone button text 16 px. Defect files in `05 Delivery Evidence\Acceptance\defects\`. |

## Owner's order (25 Oct 2026): finish ALL items first → then build the APK → then run the tests (emulator/device) → then push to GitHub. Do not push or build the APK before the items are done.

## Item 4 (owner, 25 Oct 2026): the Tab S7 re-check is replaced by emulators — in the test step run APK v11 on `codex_api35_google` (phone) AND a tablet AVD (create `hcc_tablet_api35`: API 35 Google APIs x86_64, Pixel Tablet / 2560×1600 profile, desktop layout) to cover T-01…T-05 (compact header, 44/48 px touch sizes DEC-060, Back incl. long session, rotation). One emulator at a time.

## Then (lead, sequential)
1. Merge A + B + C into `claude/wave1-integration` (resolve `Hcc.slnx` / `Program.cs` / `HccComposition` lines), full `dotnet build` (0 warnings) + `dotnet test` on `.\SQLEXPRESS`.
2. Merge D + E into `main`; tsc/test/build; APK v11 + short emulator check (Sonnet).
3. Owner decision: merge `08 Production` into `main` or keep it on its branch.
4. Update HANDOFF.md, CLAUDE.md status, backlog.

## Open owner/CA items (do not block item 2)
CA: ESIC contribution-period rule and daily-wage ×26 basis, overtime 1× vs 2×, PF split/rounding, PT basis, gratuity; GST: < ₹1,000 exemption before 22 Sep 2025, specified-premises basis, discount split across rates, discount vs ₹7,500 slab, minibar/no-show rates, laundry/banquet 18 %. Legal: DEC-068 full Aadhaar storage. Wave 1: "own shift" = opened it; Cashier refused daily collections report; folio created at check-in.
