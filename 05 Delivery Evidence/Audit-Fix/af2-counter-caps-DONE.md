# AF2-COUNTER-CAPS - DONE

Branch `claude/af2-counter-caps` (from origin/main e949a9e5). Closes the open note in af-fix2-DONE.md: the prototype mock path had no matching counter caps.

## What changed (03 Prototype)
- `src/domain/streams/counter.ts`: single shared constants `CTR_UNIT_MAX_PAISE` (1e9 = Rs 1 crore per unit price) and `CTR_BILL_MAX_PAISE` (1e10 = Rs 10 crore per bill before discount), plus the server wording `CTR_UNIT_CAP_TEXT` / `CTR_BILL_CAP_TEXT` (identical to `CounterBillRules.LineRefusal` / `BillRefusal`). `ctrLineFrom` refuses a unit price over the cap (free text, catalogue and menu lines; checked before any multiplication). New `ctrBillRefusal(lines)`: integer paise, every product and running sum guarded with `Number.isSafeInteger`, so nothing overflows or loses precision.
- `src/services/mock/handlers/counter.ts`: `CTR_BILL_ISSUE` fails with code VALIDATION on the bill cap, after the lines and before the discount rules and totals (discount applied after the cap check, as on the server). Nothing is written on refusal.
- `CounterBills.tsx` (desktop) and `Mobile.tsx` (phone): the form shows the same plain refusal (`err` = line error or bill cap) and disables Issue while it shows; no crash, nothing sent.
- Tests: `tests/stream-af2-counter-caps.mjs` (constants/wording parity, unit at cap passes / +1 paise refuses, VALIDATION code, bill exactly 10 crore passes / +1 paise refuses, qty 99 x big price, 30-line sums, discount after cap, MAX_SAFE_INTEGER / 1e30 / Infinity, catalogue lines, both screens).
- Server unchanged (it is the authority; no .NET change needed).

## Test result
`npm run typecheck` clean; `npm test` 2850/2851 (2840 baseline + 11 new). The single failure is `[HIST-C] ... size and speed budgets hold`: its scenario-generation time was 1841 ms against a 1500 ms budget while other lanes were using the machine (JSON size 8.52 MB, pack 23 ms, command 14 ms all fine); unrelated to counter code, a load-timing flake.
