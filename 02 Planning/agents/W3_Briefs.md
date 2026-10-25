# Wave 3 — finish the server modules (next session)

Owner plan (26 Oct 2026): **next session = finish Wave 3 completely → merge it into the code (`main`) → APK v11 → testing (phone + tablet emulators) → push.**
Start with: "Start Wave 3 from 02 Planning/agents/W3_Briefs.md". Same shared rules as Wave 2 (`02 Planning/agents/W2_Briefs.md`, "Every agent"), base branch `claude/wave1-integration` (after W3-a and W3-b are merged). Mix of Opus and Sonnet, more Sonnet. Run SQL tests serially (`dotnet test Hcc.slnx -maxcpucount:1`). Module PERMs only in `NNNN_<module>_rbac_seed.sql`.

## Done and merged on 26 Oct (server branch claude/wave1-integration)
- W3-a `claude/w3a-config-checks` (Sonnet): POS void limit / Yield proposal lifetime / Maintenance PM lead days into Master configuration; day-close preflight blocks on POS open checks and pending voids.
- W3-b `claude/w3b-setup-payrev` (Opus, migration 1900): first-time hotel setup (property + first Owner, loopback-only, empty install only); pay-run unlock reverses the accounting voucher.

Session 12: Wave 3 COMPLETE — all W3 agents merged (849 .NET tests); server merged into main (3ba9559). Next: APK v11 → emulator tests → push.

## Remaining Wave 3 agents
| # | Work | Model | Migrations |
|---|---|---|---|
| W3-1 | OTA channels (rate/availability push, booking import, commissions — simulated channel manager) | Sonnet | 2500–2599 (1900 is used by W3-b property setup) |
| W3-2 | Booking website / web reservations (public quote + request, deposit link simulated) | Sonnet | 2000–2099 |
| W3-3 | Document capture / OCR (ID-card capture, masked storage per DEC-068, simulated OCR) | Sonnet | 2100–2199 |
| W3-4 | Groups billing (master folio, routing, block pickup billing, group invoice) | Opus | 2200–2299 |
| W3-5 | Customers / CRM (customer 360, merge duplicates, stay history, masked contacts) | Sonnet | 2300–2399 |
| W3-6 | Invoices & credit notes (numbering series per FY, GST invoice/credit-note documents, PDF via ETP-REUSE marker) | Opus | 2400–2499 |
| W3-7 | Stays gaps: room move, extend/shorten, early departure (P2 `IInventoryAllocator`), stay-over cleaning tasks | Sonnet | 0302–0399 (Stays block) |
| W3-8 | Inventory gap: room-block history (release dates so past snapshots never drift) | Sonnet | 0202–0299 (Inventory block) |
| W3-9 | Cash-book view on the server (day/range, carry-forward, over/short, lock state) | Sonnet | 0502–0599 (Closing block) |
| W3-10 | Role matrix v2 proposal: dedicated PERM ids per Wave 2/3 module (document + seed plan for the owner's decision; no code until approved) | Sonnet | — |

## Then (lead)
1. (W3-a and W3-b are already merged.) Merge all remaining W3 branches into `claude/wave1-integration`; full serial `dotnet test`; 0 warnings.
2. **Merge `08 Production` into `main`** (owner plan), keep prototype tests (781+) and both prototype builds green.
3. APK v11 from main (versionCode 11) → phone emulator `codex_api35_google` + new tablet AVD `hcc_tablet_api35` (one at a time) — checks in `02 Planning/agents/W1_Item2_Briefs.md` (Item 4).
4. Push `main` (and the server branch) to GitHub.
