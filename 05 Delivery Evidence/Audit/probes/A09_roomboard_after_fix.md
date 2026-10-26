# A09-18 room board: before and after the fix (Wave C4, 26 Oct 2026)

**What changed.**
- `SqlHousekeepingReadStore.BoardSql` (`08 Production/src/Hcc.Infrastructure.SqlServer/Housekeeping/SqlHousekeepingStore.cs`). The two per-room `OUTER APPLY (SELECT TOP (1) …)` lookups are now set-based joins that are read once for the property:
  - **Current guest:** `LEFT JOIN` to the open stay segments. There is at most one open segment per room (`UX_stay_segment_open_room`), so the join cannot repeat a room.
  - **Next arrival:** `LEFT JOIN` to the Confirmed reservations with the rooms they hold nights in. `ROW_NUMBER() … PARTITION BY room ORDER BY arrival, reservation_id` keeps the first one per room, which is the same choice and tie-break as the old `TOP (1)`.
- Migration `3930_room_board_indexes.sql` adds one filtered index, `IX_reservation_confirmed ON inv.reservation(arrival, reservation_id) INCLUDE (departure, guest_name) WHERE status = 'Confirmed'`. It holds only bookings that are still to arrive. Index only: no table or column changes.
- The rows are unchanged, column for column. This was checked on every probe state below and by `RoomBoardQuerySqlTests`.

**How it was measured.**
- **Seed.** The audit's own seed `A09_perf_seed.mjs` ran unchanged (a copy with the samples path pointed at this checkout). It drove the pre-fix API (the build at `70c2713`) on a throwaway `HccTest_PERF_20261026163525` database with Query Store on: 240 business days, 1 Oct 2025 → 28 May 2026, all closed, in 15.5 min wall clock (`evidence/A09/roomboard-seed*.{log,json,txt}`).
- **Volumes after the seed:** 1,200 reservations and stays, 3,018 room nights, 2,996 housekeeping tasks (1,806 of them still open), 3,007 charges, 22,828 audit events, 208 MB.
- **Advance bookings added.** The seed books walk-ins only, so no booking is ever Confirmed for a later date. `A09_roomboard_enrich.sql` therefore wrote 34 Confirmed advance bookings, 5 holds, 4 cancellations and 2 active + 4 released blocks directly. The stress set `A09_roomboard_stress.sql` added 288 more Confirmed bookings over the next year (322 in all).
- **Measurement.** `A09_roomboard_measure.ps1` ran every statement with `SET STATISTICS IO, TIME` (1 warm-up, then the median of 5 runs, rows discarded) and `SET STATISTICS XML` (actual plan). It reads the new statement from the C# file, so it measures what ships. The old statement is the `70c2713` text.
- **Phases.** "Before" = schema 3701. "After" = the same data plus migration 3930, applied with the fixed build's `migrate`.
- **Lock and clean-up.** Each run held the SQL lane lock. The database was dropped afterwards (`roomboard-cleanup.txt`: 0 left). Wrapper: `A09_roomboard_run.ps1`.

## Room board statement, 24 rooms, warm cache

| State | Statement | Logical reads | CPU ms | Elapsed ms |
|---|---|---|---|---|
| Before (schema 3701), 34 Confirmed | **old** (as shipped) | **2,313** | 0–16 | 5–6 |
| Before (schema 3701) | new, without the index | 457 | 0 | 1–2 |
| After (+ 3930), same data | **new** (as shipped now) | **423** | 0 | 1 |
| After | old, with the index present | 1,498 | 0 | 2–3 |
| Stress, 322 Confirmed | **new** | **377** | 0–15 | 2–3 |
| Stress | old, with the index | 1,553 | 0–15 | 2 |
| Test fixture: 2 years of history (17,520 bookings), 74 Confirmed | **new** / old | **500** / 37,634 | | |

**Seed run, Query Store (old statement, as the API ran it).** 489 executions; avg 82.6 ms, max 974 ms; avg 9,727 reads, max 72,289 (`roomboard-before-querystore.txt`). The client side of the same calls (`roomboard-seed-results.json`, `GET /rooms/board`): median 24.3 ms, p95 663.5 ms, max 1,006.6 ms. The audit measured 10,547 reads avg and 2.4 s max over 274 days, so the same picture.

**Per table, one call** (before old → after new):
- room_night 710 → 71
- reservation 884 → 22 (2 of them in `IX_reservation_confirmed`; the rest are the in-house guests' names)
- stay_segment 168 → 7
- worktable 384 → 156
- room, room_type, room_block, stay unchanged (50, 48, 49, 20)

## Why the old statement spiked: its plan kept changing

Query Store holds seven plans for the old statement during the seed alone (`roomboard-before-querystore-plans.txt`, operator trees in `roomboard-before-querystore-plan-shapes.txt`, plans in `roomboard-plans/roomboard-seed-querystore-plan-*.sqlplan`). As the tables grew, automatic statistics updates on `inv.reservation`, `inv.room_night` and `gst.stay` kept recompiling it:

| Plan | Executions | Avg reads | Max reads | Avg ms | Max ms |
|---|---|---|---|---|---|
| 127 | 84 | 677 | 762 | 7.1 | 15.8 |
| 281 | 74 | 445 | 673 | 2.1 | 9.4 |
| **334** | 61 | **52,192** | 72,243 | **600.6** | **974.3** |
| 359 | 40 | 563 | 622 | 6.0 | 10.0 |
| **373** | 20 | **68,898** | 72,289 | 61.1 | 93.2 |
| 379 | 107 | 286 | 316 | 2.3 | 17.1 |
| 400 | 103 | 505 | 662 | 12.6 | 38.0 |
| 466 (after the enrichment's statistics update; the "before" row above) | 16 | 2,314 | 2,315 | 7.3 | 12.3 |

**Plan notes.**
- **The bad plans (334, 373).** The optimizer estimated one row per room. It drove the *current guest* lookup from a full clustered scan of `inv.reservation` **once per room**, joined to `gst.stay` and `gst.stay_segment`. It also scanned and sorted `inv.reservation` again per room for the *next arrival*. That is about 24 × the whole table, twice, and it gets worse every day.
- **The other seed plans.** For the next arrival they scan the whole of `inv.reservation` (or all of `IX_room_night_res`) once per room. For the current guest they scan the whole of `gst.stay_segment` once per room. That is cheap at 1,200 bookings (286–677 reads), but the cost is rooms × history. On the two-year test fixture the old statement read 37,634 pages.
- **Plan 466, measured "before".** It scans `PK_reservation` once per room (24 × 36 pages) and `PK_stay_segment` once per room (24 × 7 pages).
- **New statement, after the fix.**
  - Next arrival: one `Index Scan IX_reservation_confirmed` (34 rows, 2 pages), then 34 seeks into `IX_room_night_res`, a distinct sort and `ROW_NUMBER`, spooled once.
  - Current guest: one pass over the segments, spooled once.
  - Under stress (322 Confirmed) the optimizer switches to a hash join with one scan of `IX_room_night_res` (3,688 rows, 15 pages). This is still one pass for the property, not one per room.
- **What the cost now follows.** Rooms, guests in house and future bookings. Not the history.

**Equivalence.** The old and the new statement returned identical rows, every column and in the same order, in all three probe states (before, after, stress). The fixture had 10 rooms with a current guest, 22 (stress: 24) with a next arrival and 2 with an active block (`… differing rows=0` in each `roomboard-*-measure.txt`).

## What the board endpoint now spends its time on (not changed here)
- **`GET /rooms/board` also returns every open housekeeping task.** After 240 days that is **1,806 open tasks**, almost all of them stayover tasks that are never closed (A09-19). The response is **453 KB** (seed: board median 32.4 ms at the end).
- **The open-task statement** reads only 52 pages, but sorting and sending 1,806 rows costs 12 ms elapsed / 16 ms CPU. That is now more than the room statement (1 ms).
- **The fix belongs to A09-19** (expire or cancel stayover tasks at day close), which is a behaviour change. A filtered index on open tasks would not help while the open tasks themselves pile up.

## Other hot list reads checked (same data; not changed)

| Read | Reads | Elapsed ms | Pattern | Verdict |
|---|---|---|---|---|
| Bookings list, client loader (`view=range`, from business date − 180 days, page of 500) | 1,174 (stress 1,867) | 12–13 | 3 clustered scans of `inv.reservation` (COUNT, page, 36 pages each) + 500 page rows (1,005 temp-table reads) | No per-row correlated lookup. The scans grow linearly (about 3 × 220 pages at five years). Listed. |
| Arrivals view (`view=arrivals`) | 74 | 0 | 2 reservation scans + small page | Fine. The production client does not call it (it filters the range list). |
| In-house view (`view=inhouse`) | 359 | 0–1 | as above | Fine. Not called by the client. |
| Cashier folio list (`GET /cashier/folios`) | 120 (stress 205) | 3–5 | one clustered scan each of `inv.reservation` and `gst.stay`, hash joins; `OR` over In house / Confirmed today / Departed today | Grows linearly (about 400–500 reads, about 10 ms at five years). Making it flat needs a `UNION` rewrite (the "departed today" branch sits on `gst.stay`), which is not a one-index change. Listed. |
| **GST "specified premises" tariff** (`SqlGstMasterStore.MaxUnitTariffAsync`; food charges and the GST master view) | **9,304** at the end of the seed; Query Store over the seed: 720 executions, avg **15,383**, max **119,282** reads, avg 48 ms, max **1.34 s** | 7–12 | each of the previous financial year's 2,293 room charges is looked up in `bill.folio` and `inv.reservation` to check the property (4 reads each) | It was 1,500 reads at the audit; the A03a-02 change added the charged-nights branch. It grows with the previous year's room nights (about 20k reads for a full year at this hotel's size). This is the money/GST owner's code, so it is **listed, not changed**. Suggested fix: cache the previous year's maximum (it changes only when a day of that year is reopened), or aggregate per folio before joining. Raw: `tariff-before-measure.txt` (probe `A09_tariff_measure.ps1`). |

## Tests
`08 Production/tests/Hcc.Stays.Tests/Sql/RoomBoardQuerySqlTests.cs`:
- **`Board_returns_the_same_rows_as_the_old_statement_for_past_current_and_future_stays_blocks_and_holds`.** It runs the `70c2713` statement and `BoardSql` on the BASE fixture plus the following, then compares every column of every row:
  - departed stays with closed segments
  - a guest moved from one room to another
  - two bookings with the same arrival in one room (the tie goes to the lower id)
  - an earlier arrival booked later
  - a booking split over two rooms
  - a stale Confirmed booking, which still shows
  - a hold and a no-show with nights, which do not show
  - a cancellation
  - a dated block, a released block and a second property

  It then checks each case through the store.
- **`Board_reads_use_the_confirmed_index_and_stay_flat_with_two_years_of_history`.** On 17,520 past bookings and 72 future ones:
  - the plan uses `IX_reservation_confirmed`;
  - the board reads fewer than 2,000 pages (it reads 500);
  - the old statement reads more than that (37,634), so the test fails on the old statement.
