# LG cut-over evening checklist (mapping v1 section 5, DEC-164)

Tick each line. Commands run from `E:\Projects\HCC\worktrees\lg` (or `main` once merged). Never type the Access password; the extractor reads it from `Module1.bas`.

## A. Before the evening (any day)
- [ ] Integrated branch clean; gate PASS: `pwsh -NoProfile -File tools\Start-Gate.ps1 -Repo E:\Projects\HCC\worktrees\lg -Wave lg`
- [ ] Dry run on the compacted files: `pwsh -NoProfile -File tools\legacy\dryrun\Run-LegacyDryRun.ps1` (add `-RateCard <csv>` once the owner supplies it, columns `code,single,double,extraPerson`). Exit 0 and every FY `MATCH` in `05 Delivery Evidence\Legacy\LG-DryRun-<date>.md`.
- [ ] Owner reviews the 20 masked guests and `companies-review.md` in the run folder.
- [ ] Rate card and the CA's invoice-continuity answer (next INV number) are in hand.

## B. Cut-over evening (old software stops)
- [ ] Last business day closed in the old software; nobody is typing in it.
- [ ] Copy the live `Lodge26-27.mdb` over `E:\Projects\HCC\Existing Software\DB-PROFILE\compacted\Lodge26-27.mdb` (rename the old copy with its date first).
- [ ] Final extract: `python -I tools\legacy\extract.py --mdb-dir "E:\Projects\HCC\Existing Software\DB-PROFILE\compacted" --out "E:\Projects\HCC\Existing Software\LG-EXTRACT\<yyyyMMdd-HHmm>" [--rate-card <csv>] [--cutover-date <yyyy-MM-dd>]`; exit 0.
- [ ] Masters: import `guests.csv`, `companies.csv`, `room-types.csv`, `rooms.csv`, `tariffs.csv`, `occupancy-charges.csv`, `id-types.csv`, `booking-sources.csv` through the Import screen (Owner).
- [ ] Importer dry run on this run folder (all parts, Legacy import page or the dry-run script); every FY `MATCH`.
- [ ] Real run: `guests` (seals IDs, then shreds `secret\guest_ids.jsonl`), `history`, `cutover`. `lgc.import_run` status Done.
- [ ] In-house guests (`cutover_inhouse.csv`, 10 to 40 rows): re-create each by hand (reservation, check-in on the cut-over business date, room, rate, meal plan, advance).
- [ ] Advances (`cutover_advances.csv`): enter on the re-created reservations, or keep as party opening credits.
- [ ] Receivables and customer openings: ACC-1 opening screen, review the generated opening set, Owner approves; total equals the sundry debtors figure the CA agrees for the date.
- [ ] Number series: INV starts at the next legacy BillNo if the CA wants continuity (`cutover_number_series.md`).
- [ ] Till cash counted and entered (`till-cash.csv`; not in the legacy data).
- [ ] `cutover_paidout.csv` handed to the CA.

## C. After
- [ ] Spot-check three customers in Customers 360 (legacy stays carry the Legacy badge, no folio link).
- [ ] Move the run folder to the encrypted backup; confirm `secret\` is empty.
- [ ] Update HANDOFF.
