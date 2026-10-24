# A2.3 — Cashier / POS till model: decision pack

24 Oct 2026 · Open integration item 1 · Plan §4 K01, instructions §4 A2.3 · **Needs owner decision (one question below).** Nothing is changed in the prototype until it is answered; the current drawer figures and all 462 tests stay as they are.

## What happens today (prototype, verified in code)
- `drawer()` (`src/domain/selectors.ts`) and `shiftMovements()` (`src/domain/streams/closing.ts`) count only **front-desk folio cash receipts** and **cash expenses**.
- Restaurant/room-service settlements are stored in `s.posX.receipts` (`method` Cash/UPI/Card, `kind` sale/void). Their cash is shown as "outlet cash" in the POS day summary (`domain/streams/pos.ts`), but it is **not** in the cashier drawer, the shift count or the closing pack's expected cash.
- Room service charged to a room (`mode:'folio'`) correctly becomes a folio service line — a charge, not cash. It is collected later as an ordinary front-desk payment, so it must never be counted as POS cash too.
- Open (unsettled) POS checks do not block day close.

## The decision: one shared till, or separate outlet tills?

### Worked example — one business day, ₹ (identical inputs for both options)
Opening float ₹5,000 at the front desk (+ ₹1,000 restaurant float in option B) · Front desk: cash advance ₹3,000, cash refund ₹500 · Restaurant: cash bill ₹1,200, UPI bill ₹800 · Room service ₹600 charged to Room 105's folio · Option B only: restaurant hands ₹1,000 cash to the front desk at 22:00 (cash transfer).

| Line | Option A — shared till | Option B — separate tills |
|---|---|---|
| Front desk expected cash | 5,000 + 3,000 − 500 + **1,200** = **8,700** | 5,000 + 3,000 − 500 + 1,000 (transfer in) = **8,500** |
| Restaurant expected cash | (in the shared till) | 1,000 + 1,200 − 1,000 (transfer out) = **1,200** |
| Consolidated hotel cash | 8,700 | 8,500 + 1,200 = **9,700** = 8,700 + the extra ₹1,000 restaurant float |
| UPI ₹800 | Non-cash; daily receipts by method | Non-cash; outlet receipts by method |
| Room service ₹600 | Folio charge only; no cash today | Folio charge only; no cash today |
| Transfer ₹1,000 | Not needed | Two linked movements that net to zero in the consolidation |
| Who counts | One count at the desk | One count per till; each difference approved separately |

Both options count the ₹1,200 restaurant cash exactly once.

### What I would build for either option
- Movement identity `(property, outlet, till, shift, source receipt id)`; kinds: sale, refund/void, expense, transfer-out, transfer-in, float, banking.
- Option A: POS cash sales/voids join `shiftMovements()` with a "Restaurant" label; `drawer()` = `shiftExpected().expected` stays the single formula.
- Option B: a `till` field on POS receipts; per-till expected and count; a `CASH_TRANSFER` command (two linked rows, approver for amounts above a limit); the closing pack shows each till plus the consolidation.
- Day close: **open unsettled POS checks block the close** (settle them, charge them to a room, or void them with approval) — proposed default.
- History fixtures (FY26) are regenerated coherently: Apr–Oct restaurant cash enters the daily expected cash (option A) or a restaurant till (option B). Closing records are recomputed with the real `closingSummary`; no assertions are loosened.
- Tests: cash sale, UPI sale, room charge (no cash), void, refund, transfer (B), replay with the same key, close blocked by an open check.

### Recommendation
**Option A (shared till)** if the restaurant has no separate cashier and its cash goes to the front-desk drawer during the shift — simplest for a small hotel and matches the current "one cashier shift per business day" decision (DEC-035). Choose **Option B** if a restaurant cashier keeps their own cash box.

**Question for the owner:** Does restaurant cash go into the front-desk cash drawer (A), or does the restaurant keep its own cash box that is counted separately (B)?
