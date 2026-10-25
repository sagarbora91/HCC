# W05 C/D/E code review (Cash book, exports, Compliance calendar)

Date: 25 Oct 2026 · Reviewer: Claude (code-review agent; read-only, no code changed, no commits)
Target: `integration/w05` vs `main`, `03 Prototype/` — W05-D cash book, W05-E exports (xlsx, binary save, Reports Excel, Day pack, Tally preview), W05-C compliance calendar. Reviewed at merge commit `14ecba5`.
Note: while this review was running, `integration/w05` moved to `ab31b44` (W05-B payroll merged). Between `14ecba5` and `ab31b44` only staff/payroll files changed, so the C/D/E findings below hold for both commits. W05-B was not reviewed.

Method: read the whole diff plus the code it calls (`closing.ts` shiftMovements/closingSummary, `commands.ts` execute, closing handlers, FY26 `closeHistoryDays`, base grants). Each suspected bug was checked with throwaway Node scripts that bundle `src/services/mock/index.ts`, `src/app/xlsx.ts` and `src/features/reports/exportXlsx.ts` with esbuild, the same way `tests/run.mjs` does. The scripts live in the session scratchpad, not in the repo.

## Summary

| Severity | Count |
|---|---|
| S1 (wrong money / data loss) | 0 |
| S2 (control bypass) | 1 |
| S3 (wrong result in a realistic flow) | 5 |
| S4 (minor / robustness / hygiene) | 9 |

## Checked and found correct

These need no action. They are listed so nobody re-checks them.

- **One source of truth for cash (DEC-052).** Cash book entries come only from `shiftMovements` for each date, plus the closing record's `banked`. POS cash sales and voids appear once, and the closing record's `restaurant` field is never added a second time. Refunds and deposit returns come out as payments. A reversed expense on its own date comes out as a receipt.
- **FY26 carry-forward.** On all 206 days, the opening equals the previous day's counted minus banked. Every day reconciles with its closing record (the existing `[CB]` test checks this too). After a live `NEXT_DAY`, the opening equals the counted cash.
- **Tally.** June 2026 gives 894 vouchers, debit = credit = 23,79,372.50, and 0 vouchers out of balance. No voucher entry is negative, zero, or both debit and credit, so the `'-'+rupees(debit)` sign convention never produces `--`.
- **ZIP container.** Local and central headers, CRC32, the EOCD record, the UTF-8 flag and offsets are all correct. `dateSerial('2026-10-24')` gives 46319, which matches Excel. `& < > "` are escaped in cells and sheet names. Paise/100 is written as a number (for example −123.45).
- **Compliance date rules.** TDS for March is due 30 Apr. Rules roll over from December to January correctly. PT for Jan 2028 is due 2028-02-29 (leap year). Day rules are clamped to the month length. Role checks are enforced in the **handlers** (Manager, Cashier and Housekeeper get FORBIDDEN on `CMP_FILE`; an Accountant is refused on settings and reopen).
- **Platform rules.** There is no `Date.now()`, `new Date()` without the simulated clock, or `Math.random` in any domain code or handler; the only `new Date(...)` calls are deterministic UTC date maths. After a lock, a file or a settings change, `cashbookX` and `complianceX` contain no `undefined` and survive a JSON round trip. A state without the new slices reads as empty. All new commands go through `execute()`, so they get the standard key check, `processed` list and audit line.

---

## Findings (most severe first)

### F1 · S2 · Confirmed — The person who counted the cash can lock their own day (checker control bypassed)
`src/domain/streams/cashbook.ts:73-78` (`cbCounters`), used by `cbCanLock` at `:85` and by the `CB_LOCK` handler (`services/mock/handlers/cashbook.ts:8-13`).

`cbCounters` only knows two people: `closings[d].closedBy`, and `s.shift.submittedBy` **while `s.shift.date===d`**. The closing record does not store who counted, so that information is lost in three ways:
1. `NEXT_DAY` clears `s.shift`, so the counter disappears the next morning.
2. The built-in `SHIFT_CLOSE` (exact count, `commands.ts:146`) never sets `s.shift`, so the counter is never recorded at all.
3. The approver of a cash difference (`s.shift.decidedBy`) is never treated as a counter, even though the code comment says "submitter and approver".

Reproduced (BASE, seed):
- **A.** Manager `SHIFT_SUBMIT` (exact) → Owner `DAY_CLOSE`. Same day: the manager is refused ("different person"). After `NEXT_DAY`, `cbCounters` returns `['owner']`, and **the manager's `CB_LOCK` on the day they counted succeeds** (`lockedBy: 'manager'`).
- **B.** Manager `SHIFT_CLOSE` → Owner `DAY_CLOSE`. The manager can lock straight away; counters are `['owner']`.
- **C.** Cashier submits a short count → Manager approves the difference → Owner closes. The manager (the approver) can lock.

**Fix:**
- Persist the people on the record. Add optional `cash.countedBy` and `cash.decidedBy` to `ClosingRecord`, filled in `closingSummary` from `s.shift`. Add `submittedBy` to `s.shiftClose` in `SHIFT_CLOSE` and `closeShift`.
- Make `cbCounters` read the record.
- For FY26 history, set `countedBy:'cashier'`.
- Add tests for A, B and C.

### F2 · S3 · Confirmed — The new compliance intent takes over everyday assistant questions; Manager, Cashier and Reception are then refused
`src/domain/streams/assistant.ts:82`

`compliance-due` was put **first** in `rules`, and ties go to the first match. Its loose patterns `\bdue (dates?|this week)\b`, `\bwhat (is|are) due\b`, `\bfiled\b` and `\bpf\b` therefore win ties against older intents. Verified:

| Question | Old intent (by the tie rules) | Now | Manager |
|---|---|---|---|
| "Which balances are due this week?" | balances | compliance-due | refused |
| "What payments are due this week?" | collections | compliance-due | refused |
| "Are there expenses due this week?" | expenses-week | compliance-due | refused |
| "Which expenses were filed today?" | expenses-week | compliance-due | refused |
| "What is due today?" | collections | compliance-due | refused |

The existing test "existing assistant intents unchanged" does not cover these phrasings.

**Fix:**
- Move `compliance-due` below `balances`, `collections` and `expenses-week`, or give it only specific keywords (complian, gst/gstr/tds/epf/esic, professional tax, filing/return).
- Drop the bare `due this week`, `what is due` and `filed` patterns.
- Add these phrasings to the regression test.

### F3 · S3 · Confirmed — The Owner cannot edit the TDS obligation at all
`services/mock/handlers/compliance.ts:24` (`sd>=1&&sd<=28`), together with fixture `fixtures/ext/compliance.ts:13` (`special:{month:3,day:30}`) and `features/compliance/modals.tsx:79`, which re-sends `cur.special` unchanged.

Any save of `CMPO-TDS` (even a rename) sends `special.day=30`. The handler rejects it with `VALIDATION: Special-month date is not valid.` (reproduced).

**Fix:** validate `special.day` against the length of the month after the special month (1..30 for April), or allow 1..31 and clamp, as `cmpDueDate` already does.

### F4 · S3 · Confirmed — Cash book range totals do not add up whenever a cash difference occurred
`src/domain/streams/cashbook.ts:68-71` (`cbRange`). Shown in `CashBook.tsx:105-106` and in the CSV summary.

The range shows `opening` = first day's opening, `receipts` and `payments` = sums, and `closing` = last day's **book** closing. Each day's book closing differs from the next opening by that day's counted − expected, so the range never satisfies opening + received − paid = closing.
- Live example: a cashier is ₹5 short, then `NEXT_DAY`. Range opening + receipts − payments = 10,750.00, but the range closing shows 10,745.00.
- FY26 (1 Apr – 23 Oct, 17 days with differences): 4,560.00 against 5,000.00.

The owner sees a "cash book" whose totals do not add up, with no explanation.

**Fix:** add `differences: Σ day.difference` to `cbRange`. Show it as its own line ("Cash over/short (counted − expected)") on screen and in the CSV, and assert opening + receipts − payments + differences = closing in a test.

### F5 · S3 · Confirmed — An annual rule due in January–March is due before the financial year has even ended
`src/domain/streams/compliance.ts:61` (`fixed` → `y=cmpFyStartYear(p)+1`).

The handler (`parseRule`) accepts any month from 1 to 12 for annual rules. Adding an annual obligation "31 Jan after the financial year" for FY 2025-26 gives due **2026-01-31**, but the period ends 2026-03-31. The row first appears (only finished periods are listed) as **Overdue by 266 days** (reproduced: `['2025-26','2026-01-31','Overdue']`).

**Fix:** `y = fyStart + (month <= 3 ? 2 : 1)`, or reject months 1–3 for annual rules. Add a test.

### F6 · S3 · Plausible — Exporting Reports to Excel for the full FY26 range is likely to run out of memory on the phone
`src/app/xlsx.ts:42-53` (`zipStored` pushes every byte into a JS `number[]`) and `src/app/native.ts` (`bytesToBase64` → a binary string → `btoa` → passed over the Capacitor bridge).

Range 1 Apr – 23 Oct (the allowed limit is 366 days when history exists) produces a **30 MB** xlsx. Node needs about **450 MB** of heap for it, and about 2.3 s on a desktop CPU. On an Android WebView this allocation, plus a 40 MB base64 string through `Filesystem.writeFile`, is a realistic crash or "failed" result. The page then shows a success toast anyway (see F11).

**Fix:**
- Compute the total size first and write into one preallocated `Uint8Array` (or concatenate per-file `Uint8Array`s).
- Write the native file in base64 chunks (`appendFile`).
- Consider making the `Source rows` sheet optional, or capping the drill rows for long ranges.

### F7 · S4 · Confirmed — A closed date with no closing record can be locked by any checker, and it never shows in the cash book
`cashbook.ts:83` accepts `s.closedDates.includes(d)` without a record. In BASE, `2026-10-23` is in `closedDates` but has no record, so `cbCounters` is empty and the Manager may lock it. The locked day is still missing from `cbDates`, so nobody sees the lock.

**Fix:** require `s.closings[d]` for a lock.

### F8 · S4 · Confirmed — The xlsx writer can produce a corrupt or "repaired" workbook from odd input
`src/app/xlsx.ts`:
- `:23`/`:25`: `NaN` and `Infinity` (or `{paise:NaN}`) are written as `<v>NaN</v>`, which Excel reports as corrupt. Guard with `Number.isFinite` and write an empty cell or text instead.
- `:10-13` `sheetName`: a name starting or ending with `'`, or the reserved name `History`, passes through. Excel then repairs the file on open.
- Floats are written with full JS precision (`0.30000000000000004`); harmless, but money should always go through `{paise}`.

### F9 · S4 · Confirmed — Ratios and percentages go into the Reports workbook as text, not numbers
`src/features/reports/exportXlsx.ts:80` (`r.display`) and `:89` (table columns with unit `text`). Occupancy %, ADR and RevPAR are written as strings ("49.4%", "₹2,502.79"); the daily occupancy table has 30 such cells for June. The brief and ETP rule is "numbers as numbers".

**Fix:** write the numeric value with a `0.0%` or ₹ number format (add a percent style to `cellXfs`).

### F10 · S4 · Confirmed — The Tally range accepts impossible dates
`src/domain/streams/accounting.ts` (`accTallyXml`) checks only a regex. `2026-02-30` is accepted with no error. With NaN, the 31-day guard silently passes.

**Fix:** reuse a real calendar check (like `cmpValidDate`).

### F11 · S4 · Confirmed — "Saved" toasts appear even when saving failed
`features/reports/Reports.tsx` `exportExcel` and `features/accounting/Accounting.tsx` `TallyExport` both call `void save…()` and then always show a success toast, even when the result is `'failed'`.

**Fix:** `await` the result and show an error when it is `'failed'`.

### F12 · S4 · Confirmed (UX) — Users who may not open Compliance still see it in the menu
`features/compliance/register.tsx:20` gates the nav entry on `'reports'`. Cashier and Manager therefore see the nav item, the Accounts category tile entry and search hits (keywords include "gst", "pf", "return"), and then land on an "Owner and Accountant only" empty page. Data does not leak, because handlers and selectors check the role.

**Fix:** filter `compliance` with `cmpHasAccess` in the nav and search builders (or use a role-aware nav predicate).

### F13 · S4 · Plausible (design gap) — A cash book lock does not block anything
No command reads `cashbookX.locks`. Today past cash movements cannot change, so the lock is a sign-off marker only. If it is meant to block corrections or accounting changes for the date (as in ETP's locked days), that check is missing.

**Fix:** decide, and either document it as a sign-off marker or enforce it in `CORRECTION_ADJUST` and the accounting lock.

### F14 · S4 · Confirmed — Compliance reopen erases the filing record; old saves have no calendar
- `CMP_REOPEN` (`handlers/compliance.ts:49`) deletes the filing. Its reference, who filed it and when survive only inside the audit text. Keep a `history` array on the filing instead.
- A save without `complianceX` shows an empty calendar with no obligations, because fixtures are only added in `extendBase`. This is acceptable only if the lead's SCHEMA bump at merge resets saved state; otherwise seed the obligations when the slice is missing.

### F15 · S4 · Confirmed — Hygiene
- `csvCell` now exists 4 times, in `cashbook.ts:90`, `closing.ts`, `accounting.ts` and `staff.ts`, with different quoting rules. None guards against spreadsheet formula injection: a vendor or guest typed as `=HYPERLINK(...)` runs as a formula when the CSV is opened in Excel. Use one shared helper that prefixes `= + - @` with `'`.
- Dead code: `cbPrev` and `CBRangeRow` (cashbook.ts), `cmpDueLines` (compliance.ts), and the `closingSummary` preview branch in `DayPack.tsx:11` (the button is only shown once a record exists).
- The cash book lock and reopen keys are deterministic (`cb-lock:<date>:<history length>`). They work because the history only grows, but other commands use random UI keys. Prefer the app's `newKey` pattern.
- Missing tests: F1 A/B/C, F2 phrasings, F3 TDS edit, F4 range footing, F5 annual January rule, xlsx with `NaN` and apostrophe sheet names, Tally invalid dates.
