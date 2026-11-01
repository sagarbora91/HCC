# LG-EXTRACT DONE

Lane lg-extract / Sonnet / branch `claude/lg-extract` / worktree `E:\Projects\HCC\worktrees\lg-extract` / base `ced9861e`. The extractor runs end to end on the live 26-27 file; the full ten-file run is the dry-run lane's job.

## Commits
See `git log --oneline ced9861e..HEAD` on `claude/lg-extract` (code commit + this file's update).

## Files
- `tools/legacy/extract.py`, `legacy_rules.py`, `extract_read.ps1`, `selftest_checks.py`, `README.md`, `selftest/*.csv` (fictional fixture, `<fy>__<Table>.csv`)
- `03 Prototype/tests/legacy-extract.mjs`
- this file

## What was built (contract section 4 / 6 / 7 / 8)
- `extract_read.ps1` (pwsh 7, ADODB + ACE, `SELECT *` per table so older files with fewer columns still read; password regex from `Module1.bas`, never printed) dumps 20 tables per FY to `<run>\raw\<fy>\`. Tables = the brief's list plus `TransactionData` (maps `TransactionID` to `BookingID`; identical in practice) and `PurposeOFVisitMaster` (real spelling).
- `extract.py` applies the rules: identity keys (section 1), Title Case, de-dup (section 7: bills / bookings / receipts across adjacent files, dropped stays loaded as `Duplicate`), paise (half away from zero on the printed value), fixed +05:30 UTC, masters CSVs with the exact template headers, `lgc_*.csv`, `cutover_*`, `secret\guest_ids.jsonl` + images, `totals.json`, `report.md`. Refuses `--out` inside a git work tree; exit 2 plus `SELFCHECK-FAILED.txt` on a reconciliation / key / reference / ID-column failure.
- `--self-test`: 236 checks on the invented fixture (20 guests, 30 bookings, two FYs: placeholder phones, 12-digit and landline phones, duplicate phone, M/S., GSTIN shared by 3, OTA tags, FY-boundary duplicate bill / booking / advance booking / receipt, out-of-FY bill, multi-room + shift, advance, security deposit, refund, 12 % / 5 % / `LuxuryTaxPer` 1, half-paise rounding, missing GuestID stub, open bookings). Result: `self-test OK: 236 checks`. `node tests/legacy-extract.mjs` runs it (skips without python).

## Live 26-27 dev run (compacted copy of 30 Oct 2026; folder deleted afterwards)
Read 90 s. 18,022 CustomerInfo rows; 1,932 bookings (all stays: 1,908 CheckedOut, 24 InHouseAtCutover); 1,908 bills, net Rs 50,70,985.00, room GST Rs 2,40,507.45, F&B Rs 2,846, Σ advance on bills Rs 20,65,469, BillNo 1-1,908, C/P/PaidFlag N = 1,723 / 185 / 157, slab 5 %: 1,908 (all equal contract section 6); 2,029 deposit rows (Σ DepositAmt + Advance = Rs 48,03,145: payments 2,026, advances 3 = Rs 5,800) + 1 refund (Rs 95); 2,330 room segments (349 shifts); no duplicates (single file). Guests (after rulings LG-R1 / LG-R2): 14,683 `guests.csv` rows (15,091 valid-phone legacy rows, 408 merged away, 2,931 no valid phone), 210 companies for review, 7,939 OTA tags, 14,065 secret rows (real ID number, or image on disk, or DOB), 483 of them with a malformed ID number flagged, 397 duplicate-ID groups in `duplicates-review.csv`. Cut-over: 24 in-house, 756 customer openings (net Rs 20,92,411.00), 151 receivables, 3 unadjusted advances, 480 paid-out rows.

## Tests
Python self-test 236/236 (`python -I tools/legacy/extract.py --self-test`). Prototype `npm test` / `tsc` not run: no prototype source touched and the new .mjs is not auto-loaded by `run.mjs` (see Integration).

## Open items / findings
1. **LG-R1 applied:** guests merge only when phone_key AND name_fold match or one fold is a prefix of the other (higher GuestID wins the profile); same phone with another name = separate `guests.csv` row, same customer_key. `guests.csv` may repeat a phone.
2. Σ discount: contract section 6 lists Σ DiscountAmt Rs 69,730; `discount_paise` = DiscountAmt + BillDiscount (mapping 4.3) = Rs 77,833.70 in 26-27. `totals.json` also carries `discountAmtOnlyPaise` (= 6,973,000 for 26-27). The importer should compare `discountPaise` (the written column).
3. Receivables: all 185 pending bills have `BalanceDue` = 0 in 26-27, so `cutover_receivables.csv` (151 rows, selected by `data_flag = P and paid_flag = N`) shows balance 0; the CA/owner must decide the receivable amount (net or net minus deposits) before the importer posts it.
4. Room 211 sits on `PackageID` 6 (the junk "209" package), so it is left out of `rooms.csv` with a warning; the owner must give its type. Hall `CON`/`CONAC` map to `CONF`.
5. LG-R2 applied: `guest_ids.jsonl` has a row for a real ID number OR an existing image OR a DOB; `idNumber` is null otherwise, images are never dropped. `IDCode` 0 with a real number gets idType `OTHER`.
6. `cutover_paidout.csv` is latest-file only (contract heading); `--paidout-all` writes every FY.
7. Security rows use a negative `legacy_row_id` (-DepositID) so `UQ_lgc_receipt` holds; a deposit row carrying both Advance and DepositAmt gets a second row with id -(DepositID + 1e12). `totals.json` `receiptsCount` = advance + payment rows (= CustomerDeposit rows), `securityCount`, `refundsCount` separate.
8. Not yet run on the other nine files (cross-file de-dup is exercised by the fixture only); the dry-run lane should check `report.md` against contract sections 6-7 (22 bills 22-23 -> 23-24, etc.). Two extra `pwsh` COM teardown segfaults seen only under git-bash (exit 139 after all tables were written); `extract.py` tolerates a non-zero reader exit when every required table was written (it ran clean from Python).

8b. LG-R11 applied: any ID number of 4+ non-placeholder characters is kept with its declared type; `"malformed": true` is added to the jsonl row when it fails the type's shape (Aadhaar 12 digits, PAN AAAAA9999A, passport 8 chars). `duplicates-review.csv` (`group_no,legacy_guest_id`, ids only) lists groups sharing one SHA-256 of the normalised ID number across different phone keys; the importer ignores it.

8c. LG-R14 applied: `n:` customer keys capped at 80 characters and every text for a bounded column cut to its contract section 3 length (bill RoomNo over 20 characters: full text at the front of the notes). Masters importer facts, done by the extractor: `guests-1.csv`, `guests-2.csv` ... in parts of at most 5,000 rows (26-27: 3 parts) beside `guests.csv`, and a row with a GSTIN always carries a company (own name when CompanyN is empty). Self-test gained the 90-character-name and 103-character-RoomNo cases. 26-27 dev run: same figures; longest values now within every limit.

## Integration needed
- Importer (`lg-import`): read `totals.json` -> `fy[<fy>].afterDedup` (keys `billsCount, netPaise, roomGstPaise, fbPaise, receiptsCount, receiptsPaise, refundsCount, refundsPaise`, plus `securityCount/Paise`, `stays`, `staysDuplicate`, `segments`, `bills`, `receipts`, `outOfFy`); `lgc_guest_map.csv` has trailing columns `guest_name, guest_phone, is_primary, dob` for matching (name+phone of the `guests.csv` row) and `guest_id` empty; `stay_ref` / `bill_ref` columns replace `stay_id` / `bill_id`; empty cell = NULL.
- `03 Prototype/tests/run.mjs` only loads `stream-*.mjs` / `sync-*.mjs`: to include the self-test in `npm test`, add `03 Prototype/tests/stream-legacy-extract.mjs` containing `export {register} from './legacy-extract.mjs';` (not my file).
- Add `tools/legacy/__pycache__/` to `.gitignore` if python writes it.

## HANDOFF bullet
LG lane lg-extract: `tools/legacy/extract.py` (+ `extract_read.ps1` ADODB reader, `legacy_rules.py`, fictional fixture, self-test 236/236, README) writes masters CSVs, `lgc_*.csv`, `cutover_*`, `secret\`, `totals.json`, `report.md`; live 26-27 dev run reproduces contract section 6 (1,908 bills Rs 50,70,985, 2,029 receipts). Open: rate card, room 211 type, receivable balance rule, full ten-file run by the dry-run lane.
