# CU lane gst5 - DONE (11 Nov 2026)

## Finding (no data change needed)
The committed rate-card files already carry the owner's split. Nothing carries 18 % for rooms or breakfast.
- `03 Prototype/tests/fixtures/f4/rate-card/*.csv` were redone by F4-int / DEC-239 (3 Nov): exclusive = card price / 1.05 (Deluxe EP 2,000 -> 1,904.76; LUX 3,142.86; breakfast CP add-on 95.24 per guest = Rs 200 a room). Header of `stream-tariffcard.mjs` says so.
- The dated GST master (`src/domain/gst.ts`) already yields 5 % for rooms <= Rs 7,500 per night and `banquet` 18 % for halls. Unchanged.
- `08 Production/deployment/import-templates/{templates,samples}` hold no owner card and no GST-rate column (all prices "before GST"; samples are made-up hotel). `halls.csv` carries `gstClass` HALL_RENT only. Unchanged.
- `tests/rpt-golden/doc-goldens/doc.rate-card.base.json` already matches (test passes, no regeneration needed, none done).
- The real owner card (`E:\Projects\HCC\Existing Software\DB-PROFILE\rate-card`) is outside the repo and was not read or committed.

## Changed
- `03 Prototype/tests/stream-tariffcard.mjs`: one new test appended, "[TRC] CU-GST5 split per line" (10 room lines EP/CP: taxBps 500, inclusive price unchanged, exclusive = incl/1.05 and not incl/1.18; breakfast 500; hall line 1800). No other file changed.

## Tests
`HCC_TEST_ONLY='TRC|tariff|rate' node tests/run.mjs` (through the lock): 102/102 passed, 0 failed, including the new test and the existing F4 rate-card import test. Full `npm test`, tsc and server tests not run (no source changed).

## Not verified
Server import path of the same CSVs; the real owner card; CA view on 3rd-guest GST.

## Defaults used
- Left the 3rd-guest charge as is: occupancy-charges.csv carries 423.73 / 338.98 (= Rs 500 / 400 incl at 18 %), because the rate rule posts occupancy as "other services" 18 % (documented in GST-CA-2026-11-03.md). Changing it needs a rate-rule change, which this lane must not do. The new test pins this current behaviour (adult lines taxBps 1800).

## Open questions
1. Owner ruling says rooms 5 %: should the 3rd-guest (extra person) charge follow the room (5 %)? If yes, a rate-rule change in `trcLineBps` / occupancy posting plus CSV values 476.19 / 380.95 are needed, and the pinned assertion flips.

## Shared files touched
None.
