# AF2 round summary (5 Nov 2026, afternoon)

Branch `claude/af2-int` HEAD 98456bc0. `main` is still e949a9e5; the branch is NOT merged (owner's go awaited). Details: HANDOFF Session 13u; per-lane files `af2-*-DONE.md` in this folder.

## Lanes
| Lane | Branch / commits | What it does |
|---|---|---|
| af2-phone-digits | 5dda4ee5 / f67c5223 | Audit target `crm:ck_<token>`; `CustomerMerged` payload keys are `ck_` tokens; names with 8+ digits masked. Old audit rows keep raw keys (append-only). |
| af2-counter-caps | 443960ae | Prototype `counter.ts` caps: Rs 1 crore per unit, Rs 10 crore per bill (net before discount and GST). Values agent-chosen. |
| af2-paymethods | b59c34c3 | Folio / refund / company / group payment prechecks use the payment-methods master (retired refused, renamed accepted, till by master). |
| af2-hub-server | 778feaf7 | Report hub on the server in production: list, run, export, export log, DEC-061 money guard. |
| af2-per01 | f285167a | Customer list / search / profile on a narrow index-backed pass, migration 4910. 435 ms to 62 ms at 15k guests; 799 ms to 112 ms at 30k. |
| af2-ctl01 + af2-fix | c45bd7f7 + 246b9723 | Stay rate change checked against the tariff limit and the agreed rate (`rate.agreedReductionBps`, Owner-only, default 15 %, migration 4920); move / extend-stay included; digest line + `RATE_REDUCTION_OVER_THRESHOLD` audit; export-log principal filter in SQL; `SafeName` masks 8+ digits. |

Three should-fix findings from the independent read-through were fixed on `claude/af2-fix` 246b9723.

## Tests and builds
- Gate PASS at 5c853e63: prototype 2884, server 4324 in 42 projects (`05 Delivery Evidence/Gates/af2-int-gate-20261105-1617.md`).
- 98456bc0: versionCode 24 / versionName 23. APKs in `E:\Projects\HCC\Builds\2026-11-05b\`: demo sha256 0e80076ed06e7222cb9ee157c2b39e0033a8d021db8bf32c83b0b3edf62cd87d; server 00bfb45103bedafc3f80e57c62c7868a60041b30333ff083a5a9cb769dcb3fef (built and manifest-checked only).
- Emulator report `05 Delivery Evidence/Acceptance/APK_v23_Emulator_Report.md`: phone a-f PASS, tablet a-d PASS. Not verified: server APK on a device, Galaxy Tab S7, tablet reports / rate-change, retired-method text (no UI path).

## Open items
Follow-ups (code):
- `crm.*` key columns are VARCHAR(80): Devanagari keys collapse; needs an NVARCHAR migration lane.
- `CustomerCommands` still loads the whole property.
- Fixed lists remain in POS settle (`pos.ts`), expense till (`accounting.ts`), hall booking, `CTR_BILL_ISSUE` device check; PAY-08 does not match a pre-rename receipt with an equal post-rename one.
- Hub on the server: not done are the 22 prototype-only reports (P1: audit-log, daily-ops, cash-book, expenses-cash, receipts-deposits, rest.day-summary, rooms.occupancy, stock.stock-sheet), server paging, PDF button, server print renderer.

Owner decisions / confirmations:
- DEC-241 (typed price below master = discount under DEC-063 limits; pending in the register).
- 15 % default for the agreed-rate threshold; Manager over-threshold on move / extend refused outright (no approval request); every rate-changing move / extend writes a `stay_rate_change` row (shows in A36); a non-Owner raise does not move the agreed rate.
- Cap values, and whether the Rs 10 crore cap includes GST.
- Hub-server slice scope; owner diff review of the money lanes.

Carried over: server-APK pages (elevated shell or hotel PC); `tools\egress-proof.ps1` as Administrator; Galaxy Tab S7 run with v23; release keystore; DEC-242; DEC-243; AF-DEC-1...4; LG dry run repeat on the hotel PC.
