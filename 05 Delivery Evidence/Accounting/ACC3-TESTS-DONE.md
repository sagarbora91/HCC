# ACC3-TESTS-DONE — lane acc3-tests (Sonnet), branch claude/acc3-tests

Status: **verified against the landed lanes.** Merged claude/acc3-pl (7401d6b7), claude/acc3-ageing (26a6195e), claude/acc3-tally (6fa6ee7c); append-only `// ACC-3` lines of every lane kept (ageing/tally lines landed just after the `// ACC-3 end` marker in `services/mock/index.ts` and `HccComposition.cs`; the integrator may tidy placement), csproj copy items kept.

## Files
- `03 Prototype/src/fixtures/ext/accounting-rulebook.ts`: `ACC_RB_REPORTS` (expectations only, hand-computed from rulebook 8.1-8.6).
- `03 Prototype/tests/stream-accounting-golden3.mjs`: 15 tests `[ACC3-TESTS]`.
- `08 Production/tests/Hcc.Accounting.Tests/Golden/rulebook-reports.json` + `RulebookReportsTests.cs` (9 tests; the ACC3_REPORTS guard is gone, no csproj constant needed).

## Counts
- Prototype `npm test`: 2132/2132 (before my lane: 2095; ACC-3 lanes add the rest; mine = 15).
- `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1`: 282/282 (mine = 9). Byte equality of the Tally golden files vs the server builders is already asserted by acc3-tally's `TallyExportTests`; my test adds counts (239 = emit list), sum Dr = sum Cr per voucher, GUID = REMOTEID, unique ids, Windows line ends. Prototype: well-formed, one VOUCHER per HCC voucher, REMOTEID stable/unique, re-export byte equality, 31-day refusal.

## Findings (no production defects found)
1. Brief vs rulebook: Deccan Agro is nil at 30 April (T7b pays the 63,000); the 63,000 (six 10,500 company folios, 22 days) + Goibibo 21,000 (11 days) is the position on 28 April. Tests assert both.
2. Cash ratio definition (as built, matches my hand calc once stated): receipts = money received by Receipt vouchers (cash/bank/clearing legs) = 5,97,860 (T5c 4,52,000 + T6 31,500 + T7a 40,000 + T7b 57,000 net of TDS + T19 17,360 net), cash 1,52,000 (walk-in POS bills are Sales vouchers) = 25.42 % (2542 bps), flagged; payments 13,000 of 4,37,090 = 2.97 % (297 bps). Server returns integer basis points.
3. Prototype reads April to 1 May (pay run locked 1 May); server month dates the pay run 30 April.
4. Deccan's 63,000 is six separate bills in the prototype (one per folio), not one.

## HANDOFF bullet
ACC-3 golden reports verified both sides: P&L profit 1,40,400, balance sheet 79,27,900, ageing (Deccan nil on 30 April), GST 29,000 / challan 28,900, cash ratio, Tally golden counts; prototype 2132, Accounting.Tests 282.
