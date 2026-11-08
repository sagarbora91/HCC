# F8C guard re-baseline (6 stale prototype guard tests)

Cause: wave F8C legitimately added token-only CSS to `src/styles/folio.css` (35 lines, split-folio styles) and
`src/styles/mobile-frontdesk.css` (10 lines, phone Who-pays card) and phone files `BillRouting.tsx`, `routing.ts`
(plus `Arrivals.tsx`, already allowed). The CP-3 guards pin the converted sheets to base 799e4cc, and the FIT guard to 3dac5afb.
Product CSS was NOT changed; only tests and one helper.

## Changes
1. New `03 Prototype/tools/f8cAdded.mjs`: `F8C_BASE` = `007e345e^` (last commit before F8C touched the sheets),
   `addedLines()` reads `git diff -U0 F8C_BASE HEAD` and **throws if any hunk removes or changes an existing line**
   (only pure insertions are exempt); `withoutF8c()` cuts the inserted lines out; `onlyF8c()` returns just them.
   Applies only to folio.css and mobile-frontdesk.css.
2. `tests/stream-css-tokens-a.mjs` [CT-A]: the "resolving the tokens gives back the original" (mobile-frontdesk.css) and
   "line endings and line counts unchanged" tests compare `withoutF8c(...)` against the original (converted region still fully verified).
   NEW test "[CT-A] F8C: the lines wave F8C inserted ... no raw colour literal": both inserted blocks must be non-empty and contain no hex
   (tokenizer stats.hex = 0 and a regex check). The existing "no raw hex remains in the converted files" test still covers the whole file.
3. `tests/stream-css-tokens-c.mjs` [CT-C] (folio.css): "resolving tokens gives back the original", "tokenizer reproduces the committed file exactly",
   "changed lines differ only in colour literals" use `withoutF8c`. The no-raw-hex test is unchanged and covers the whole file.
4. `tests/stream-fit.mjs` [FIT] phone-layout guard: allow-list gains `src/mobile/frontdesk/BillRouting.tsx`, `routing.ts`,
   `src/styles/mobile-frontdesk.css` with a comment citing wave F8C. FIT base unchanged.

## Results
- Affected streams (`HCC_TEST_ONLY=stream-css-tokens|stream-fit`, Invoke-Serialized label f8c-guards): 316/316 passed (was 6 failing).
- Full `npm test` (Invoke-Serialized, label f8c-guards): 3308/3308 passed (30 async), 0 failed.
