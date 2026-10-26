# A03a specified premises: performance of the tariff read, before and after (Wave C7, 26–27 Oct 2026)

**What was slow.** `SqlGstMasterStore.MaxUnitTariffAsync` finds the highest per-unit price of the previous financial year. Its answer decides "specified premises" and so the restaurant GST rate (5 % or 18 %). It runs for every food charge and for the GST master view.

Fix wave A (A03a-02) taught it to ignore a room night that was reversed. Since then its charged-nights branch has read every room night of the previous year, together with that night's folio and reservation, to check the property. It also checked each night for a reversal, and `bill.charge.reverses_charge_id` had no index for that check.

The Wave C room-board probe measured it on the 240-day A09 seed (`05 Delivery Evidence/Audit/probes/A09_roomboard_after_fix.md`, branch `claude/wave-c4-roomboard`). Query Store recorded 720 executions:
- **Reads:** 15,383 on average, 119,282 at most.
- **Time:** 48 ms on average, 1.34 s at most.

At the audit the same read cost about 1,500 pages. The cost grew with last year's room nights and with the plan the optimizer happened to pick.

**What changed.**
- **The statement** (`SqlGstMasterStore.MaxUnitTariffSql`). The charged-nights branch is now one `TOP (1) … ORDER BY amount_paise DESC` subquery. It walks the room nights from the highest amount down and stops at the first night that meets all of these:
  - it falls in the year;
  - it belongs to the property (folio → reservation);
  - nothing reverses it.

  `TOP (1)` by amount gives the same value as the old `MAX`. If there is no night, the subquery returns NULL, which the outer `MAX` ignores, as before. The two tariff branches have not changed.
- **Migration `3951_gst_specified_premises_indexes.sql`**, indexes only:
  - `IX_charge_room_night_amount` on `bill.charge(amount_paise DESC, service_date) INCLUDE (folio_id) WHERE kind = 'room' AND reverses_charge_id IS NULL`. It holds the room nights that are not themselves reversals, highest first. The statement names it in a table hint.
  - `IX_charge_reverses` on `bill.charge(reverses_charge_id) WHERE reverses_charge_id IS NOT NULL`. It holds only the reversal rows and serves the "was it reversed" check.
- **No cache.** A cached previous-year maximum would also have to follow:
  - rack-rate edits (the rack rate is not dated);
  - back-dated tariffs;
  - rooms added to a type;
  - reversals of old nights.

  It would also have to handle uncommitted reads inside command transactions. The index rewrite needs no invalidation and is already cheap, so nothing is cached.

## Measurement

**Where.** `08 Production/tests/Hcc.GstIds.Tests/Gst/GstSpecifiedPremisesPerfTests.cs`, run under the SQL lane lock on `.\SQLEXPRESS`, on a throwaway `HccTest_*` database that the fixture drops afterwards.

**Fixture.** 56,186 charges in all:
- **Room nights, property 1:** 18,720, at 20 one-night stays a day from 1 Apr 2024 to 23 Oct 2026. Rates run from ₹3,000 to ₹7,000; every 7th stay this FY pays ₹12,000.
- **Service lines:** two per stay, 37,440 in all.
- **Last FY, property 1:**
  - a ₹9,500 night reversed by a correction line;
  - a ₹9,000 night reversed by a room-kind line;
  - a standing ₹7,300 night;
  - a dated Suite tariff of ₹5,000.
- **FY 2024-25, property 1:** a standing ₹8,000 night.
- **Second property:** 20 nights at ₹15,000 last FY.

**Method.** Each figure is the median of 5 warm runs after 1 warm-up, with `SET STATISTICS IO, TIME`, for last FY (2025-26) of property 1.
- **Before:** the 70c2713 statement with the two indexes dropped (schema 3701).
- **Probe's plan shape:** the same with `OPTION (LOOP JOIN)`. Each night of the year is looked up in `bill.folio` and `inv.reservation`, which is the shape behind the probe's spikes.

| Statement | Schema | Logical reads | Elapsed ms |
|---|---|---|---|
| Old (70c2713), plan chosen on fresh statistics | 3701 | **16,978** | 41 |
| Old, in the probe's nested-loop shape | 3701 | **46,637** | 58 |
| Old, with the 3951 indexes present | 3951 | 16,978 | 49 |
| **New (as shipped)** | **3951** | **225** | **1** |

**Reading the table.**
- **The indexes alone do not help the old statement.** Only the rewrite does.
- **Where the new statement's reads go.** Most of its 225 reads are walking down from the highest amounts:
  - this FY's ₹12,000 nights are skipped by date, cheaply, inside the index;
  - the second property's 20 dearer nights are skipped after a folio and reservation lookup each;
  - the two reversed nights are skipped after their reversal check.

  For a hotel with one property, the first night of the year the walk reaches is normally the answer.
- **Plans are less exposed to statistics.** The index hint and the `TOP (1)` row goal keep the new plan an ordered index walk. Stale statistics therefore cannot easily move it into the scan or per-night lookup shapes behind the probe's 119k-read spikes (seven plans on the seed).

## Same result

The test compares the old statement, the new statement and the store method for six cases:

| Property | FY | Result | Why |
|---|---|---|---|
| 1 | 2024-25 | ₹8,000 | the standing night |
| 1 | 2025-26 | ₹7,300 | the ₹9,500 and ₹9,000 nights were reversed and do not count (A03a-02) |
| 1 | 2026-27 | ₹12,000 | |
| 1 | 2020-21 | ₹4,500 | no nights, so the Suite rack rate |
| 2 | 2025-26 | ₹15,000 | its own nights, never property 1's |
| 2 | 2024-25 | ₹3,000 | its rack rate |

The test also goes through the rate resolver:
- **Food on 24 Oct 2026** stays at 5 % without ITC, because last FY's highest is the standing ₹7,300.
- **Food on 31 Mar 2026** is 18 %, because FY 2024-25 had the ₹8,000 night.

The fix-wave-A tests still pass unchanged: `GstSpecifiedIgnoresReversedNightsTests`, `GstSpecifiedFromChargedNightsTests`, and the derivation cases in `GstSqlTests`. That is 40 of 40 in `Hcc.GstIds.Tests`.

## Assertions that keep it this way

- **New statement:** fewer than 1,000 reads on the fixture (tolerant; it reads 225).
- **Old statement:** reads more than 3 times the new one, and more than 10,000 in the loop shape. If the fixture stops showing the old cost, the test fails.
