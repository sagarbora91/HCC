# tools/legacy - the legacy "Lodge" extractor (wave LG, lane lg-extract)

Reads the old VB6 Lodge software's yearly Access files and writes the masters CSVs, the `lgc_*.csv` history files, the cut-over lists, the secret ID folder, `totals.json` and `report.md` described in `02 Planning/agents/LG-HISTORY-CONTRACT.md` section 4. Python 3.14 stdlib only, plus PowerShell 7 with the ACE OLEDB provider for reading `.mdb` files. The files it writes are real customer data: **never commit them**; the extractor refuses an `--out` inside a git work tree.

## Files

| File | Job |
|---|---|
| `extract.py` | Orchestrator and all rules except the pure ones. `--self-test` runs the fictional fixture. |
| `legacy_rules.py` | Pure rules: customer key, Title Case, phone validity, paise, IST to UTC, mappings, de-dup keys. |
| `extract_read.ps1` | One call per `.mdb`: `SELECT *` per table through ADODB / ACE into UTF-8 CSV under `<run>\raw\<fy>\`. Reads the password at run time from `Forms\Forms\Module1.bas` (never printed or stored). |
| `selftest_checks.py`, `selftest\*.csv` | The self-test and its invented fixture (`<fy>__<Table>.csv`, 20 guests, 30 bookings, two FYs, every edge of the contract). |

## Run on this PC

From the repository root, absolute paths, always `-I`:

```
python -I tools/legacy/extract.py --mdb-dir "E:\Projects\HCC\Existing Software\DB-PROFILE\compacted" --out "E:\Projects\HCC\Existing Software\LG-EXTRACT\20261105-2130"
python -I tools/legacy/extract.py --self-test
```

`--mdb-dir` is scanned for `LodgeYY-YY.mdb` exactly (dated copies and `-ORG` files are ignored). Options: `--latest-only`, `--fy 2025-26 2026-27`, `--rate-card <csv>`, `--cutover-date yyyy-mm-dd`, `--keep-raw` (raw per-table CSVs stay under `raw\`; otherwise deleted), `--paidout-all` (cutover_paidout.csv for every FY, default latest file only), `--support-dir <folder with Photo\ and IDProof\>` (repeatable; defaults to the live "Updated Version\SupportFile" then `Forms\Forms\SupportFile`). Reading one file takes about 90 s. Exit codes: 0 ok, 1 usage / read problem, 2 a self-check failed (`SELFCHECK-FAILED.txt` lists the failures).

## Cut-over evening

1. Stop the old software on the live PC and copy the live `Lodge26-27.mdb` (and every earlier `LodgeYY-YY.mdb`, or the compacted set) into one folder; copy `SupportFile\Photo` and `SupportFile\IDProof` next to it.
2. Run the extractor with a new `--out` folder outside the repository, `--rate-card` (once the owner supplies it) and `--cutover-date`.
3. Read `report.md` and `companies-review.md`; compare the table with contract section 6; fix nothing by hand in the CSVs.
4. Load `guests.csv`, `companies.csv`, `room-types.csv`, `rooms.csv`, `id-types.csv`, `booking-sources.csv` (+ `tariffs.csv`, `occupancy-charges.csv`) through the masters importer, then the legacy importer (`guests`, `history`, `cutover`), then re-enter `cutover_inhouse.csv` by hand.

## Output files

| File | Content |
|---|---|
| `guests.csv`, `companies.csv`, `room-types.csv`, `rooms.csv`, `id-types.csv`, `booking-sources.csv`, `tariffs.csv`, `occupancy-charges.csv` | Masters templates, header rows identical to `08 Production/deployment/import-templates/templates`. Never an ID number. `tariffs.csv` / `occupancy-charges.csv` and the `rackRate` column only with `--rate-card`. |
| `companies-review.md` | GSTINs with 3 or more guests and their names, for the owner. |
| `lgc_guest_map.csv`, `lgc_stay.csv`, `lgc_room_segment.csv`, `lgc_bill.csv`, `lgc_receipt.csv`, `lgc_tags.csv` | History, column order of contract section 3 (surrogate ids replaced by `stay_ref` = `fy\|booking`, `bill_ref` = `fy\|billno`); money in paise, dates `yyyy-mm-dd`, UTC `yyyy-mm-ddTHH:MM:SSZ`. `lgc_guest_map.csv` has four trailing helper columns: `guest_name`, `guest_phone` (the `guests.csv` row the legacy id maps to, for matching), `is_primary`, `dob`. |
| `cutover_inhouse.csv`, `cutover_advances.csv`, `cutover_receivables.csv`, `cutover_customer_opening.csv`, `cutover_paidout.csv`, `cutover_number_series.md` | Cut-over lists (latest file). |
| `secret\guest_ids.jsonl`, `secret\images\photo\`, `secret\images\idproof\` | ID numbers (only guests with a real number) and copied images; read by the importer only and shredded after a committed run. |
| `totals.json` | Per FY `beforeDedup` (every source row) and `afterDedup` (the rows written, what the importer must reproduce), `dedup`, `guests`, `cutover`. |
| `report.md` | The same as tables, every de-dup drop, warnings, rooms. |

Receipt rows: one per `CustomerDeposit` row (kind `advance` or `payment`), an extra `security` row (negative `legacy_row_id` = -DepositID) when `SecurityDeposit` > 0, one `refund` row per `CustomerRefund` (RefundAmt + SecurityDepositRefund). In `totals.json`, `receiptsCount` / `receiptsPaise` count the advance + payment rows only; security and refunds have their own figures.

## Rate-card CSV

`code,single,double,extraPerson` (rupees before GST, codes `SUITE,PREMIUM,DELUXE,TWIN,CONF`). `double` becomes `rackRate` and the `tariffs.csv` rate, `extraPerson` becomes `extraAdult` in `occupancy-charges.csv`. `single` is accepted and not used.

## Notes

- Money is `Decimal(printed value)` times 100, half away from zero; dates are IST wall clock, UTC = IST minus 5:30.
- `bill.extra_paise` = ExtraCharge + ACCharge + ElectricalExpCharges; `other_paise` = Others + Telephone + Addition + WaterDonation + Damages; `discount_paise` = DiscountAmt + BillDiscount (the summed DiscountAmt alone is `discountAmtOnlyPaise` in `totals.json`).
- The fixture is invented; no real row, name, phone, GSTIN or ID number appears in this folder.
