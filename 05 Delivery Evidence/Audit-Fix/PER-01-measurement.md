# PER-01 measurement: customer list and profile at 1k / 10k / 30k guests (4 Nov 2026)

Lane af-ctl02-perf. Measurement only; no production code changed. Benchmark: `08 Production/tests/Hcc.Customers.Tests/Perf/CustomerQueryBenchmark.cs` (runs only with `HCC_PERF=1`; `HCC_PERF_N` picks the sizes).

Setup: Intel Xeon W-1250 3.30 GHz, 31.7 GB RAM, SQL Server Express (`.\SQLEXPRESS`, disposable `HccTest_*` database per N, dropped afterwards), .NET 10 Debug build (not Release), other lanes idle while the lock was held. Data: N fictional guests (phones 9000000000 + i, names from 10 first x 10 last names, so "rao" is about 10 %), each with one departed reservation, one registration, one folio and two room charges, on top of the small CrmWorld seed. Owner principal (contact + money views). Each row: 5 warm-up + 20 measured runs; p50 / p95 wall time; allocated bytes per call from `GC.GetTotalAllocatedBytes(true)`.

Why it is slow: every call goes through `SqlCustomerReadStore.LoadAsync` (`SqlCustomerStore.cs:62-80`), which reads every guest, every reservation (with two OUTER APPLY room lookups), every in-house/departed charge and every alias of the property, builds the whole customer index in memory, searches it, and only then takes the 50-row page. Cost is linear in property size and independent of page number, search text or whether a page is wanted at all; `ProfileAsync` pays the same load for one customer.

Notes: the `q=900012` probe returned 0 rows at every N because the generated phones (9000000000 + i) never contain that digit string (they contain "900001"); the load cost is the same whatever the text matches, so the timing stands, but it is not an owner-contact hit. Logical reads were not captured (no cheap DMV per call); allocations and wall time are the evidence.

### N = 1,000 guests (seeded in 0.2 s)
| call | total rows | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|---|
| ListAsync q=null page 1 | 1004 | 38 | 44 | 4.5 |
| ListAsync q=null page 20 | 1004 | 35 | 39 | 4.3 |
| ListAsync q=rao page 1 | 102 | 29 | 31 | 4.7 |
| ListAsync q=rao page 20 | 102 | 29 | 30 | 4.6 |
| ListAsync q=900012 page 1 | 0 | 29 | 30 | 4.5 |
| ListAsync q=900012 page 20 | 0 | 28 | 30 | 4.5 |
| ProfileAsync one key | 2 | 30 | 32 | 4.3 |

### N = 10,000 guests (seeded in 0.6 s)
| call | total rows | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|---|
| ListAsync q=null page 1 | 10004 | 295 | 330 | 43.1 |
| ListAsync q=null page 20 | 10004 | 295 | 326 | 43.1 |
| ListAsync q=rao page 1 | 1002 | 300 | 327 | 46.4 |
| ListAsync q=rao page 20 | 1002 | 299 | 322 | 46.4 |
| ListAsync q=900012 page 1 | 0 | 298 | 336 | 44.5 |
| ListAsync q=900012 page 20 | 0 | 293 | 349 | 44.5 |
| ProfileAsync one key | 2 | 289 | 332 | 43.1 |

### N = 30,000 guests (seeded in 1.5 s)
| call | total rows | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|---|
| ListAsync q=null page 1 | 30004 | 828 | 899 | 123.4 |
| ListAsync q=null page 20 | 30004 | 842 | 885 | 123.4 |
| ListAsync q=rao page 1 | 3002 | 841 | 921 | 133.8 |
| ListAsync q=rao page 20 | 3002 | 877 | 935 | 133.8 |
| ListAsync q=900012 page 1 | 0 | 854 | 897 | 127.7 |
| ListAsync q=900012 page 20 | 0 | 855 | 911 | 127.7 |
| ProfileAsync one key | 2 | 830 | 871 | 123.4 |

## Scaling

| N | p50 ms (list, any q) | MB allocated per call |
|---|---|---|
| 1,000 | about 30-38 | about 4.5 |
| 10,000 | about 295 | about 43-46 |
| 30,000 | about 830-875 | about 123-134 |

Linear: roughly 29 ms and 4.3 MB per 1,000 guests, per page view, per search and per profile open.

## Recommendation for owner question Q7

Approve the SQL-side search / paging rewrite of `CustomerQueries.ListAsync` (and the profile read). Under the plan's own rule ("approve if p95 at 30k > 1 s or allocations > 50 MB per page"): p95 at 30k is 0.89-0.94 s (under 1 s on this PC in a Debug build), but allocations are 123-134 MB per call (over 50 MB) and already 43-46 MB at 10k, so the allocation limb of the rule is met. The legacy volume (14,683 guests) would sit near 60 MB and 0.4 s per call on this PC,; every search and every profile open repeats that cost. Keep `KeyOf` / alias semantics in the rewrite.

---

## After the rewrite (5 Nov 2026, lane af2-per01; owner approved Q7)

What changed (no change in results, order, paging, `ck_` keys or role masking; proved by the golden equivalence tests below):
- **Key pass** `SqlCustomerReadStore.HeadsAsync`: every guest (id, name, mobile) and every booking that is not an expired hold (id, number, registered guest of this property, arrival; the booking name only when there is no such guest) of the property, in id order from two covering indexes (migration **4910**: `IX_guest_crm_head`, `IX_reservation_crm_head`). No folio lines, no room lookups, no contact / preference columns.
- `CustomerRules.Heads` computes the same keys (`KeyOf` + merge aliases), display names, mobiles, newest arrival and order as `CustomerRules.Index` from those narrow rows; search (`SearchHeads`, same rule), tag filter, count and paging run on the heads.
- **Detail load** `LoadSomeAsync`: only the page's customers (or the one profile) get the full read (room from the last segment / last reserved night, folio lines, guest rows), by id sets in table variables, then seeks (`IX_charge_folio`, migration 4910; the reversal check now names `x.reverses_charge_id IS NOT NULL` so the filtered `IX_charge_reverses` is used). `CustomerRules.Index` then rebuilds exactly those customers.
- `ck_` tokens are hashed only for the rows sent out; an inbound token is matched by hashing keys until it matches (no 30k-entry token map per call). The merge list needs no customer read at all.
- Smaller allocations in hot rules (same results, covered by tests): `Digits` without a StringBuilder, `Resolve` returns at once when the key has no alias, `NormName` has a one-pass ASCII path.
- Unchanged on purpose: `DuplicatesAsync` and the merge / tag / note / preference commands still use the full `LoadAsync` (they need every customer, or live in `CustomerCommands.cs`, which another lane is editing; follow-up below). `LoadAsync` gained an explicit `ORDER BY` on guests and bookings (it was the scan order; this pins the tie order of customers with the same newest arrival and name, which both paths now share).

Run: same PC, SQL Server Express, .NET 10 Debug build, `HCC_PERF=1 HCC_PERF_N=1000,10000,15000,30000 HCC_PERF_OLD=1`, through the lock (other lanes' jobs queued, so the PC was otherwise quiet; one 15k p50 row at 157 ms is a one-off spike). Each N times the rewritten query and, in the same run on the same database, the pre-rewrite query kept verbatim in the test project (`PreRewriteCustomerQueries`, rows "(before)"). Note the "(before)" rows already profit a little from the new indexes and the cheaper `Digits` / `NormName` / `Resolve` (about 110 MB at 30k instead of 123 MB on 4 Nov); the 4 Nov tables above are the original baseline. 15k is about the legacy import (14,683 guests). New probe `q=0001234` hits real phones (9000001234 and 90000123xx), unlike `q=900012`.

### Summary (p50 ms / MB allocated per call, list page 1 q=null; profile about the same)

| N | 4 Nov baseline | before (same run) | after | speed-up | allocation cut |
|---|---|---|---|---|---|
| 1,000 | 38 ms / 4.5 MB | 38 ms / 4.0 MB | 24 ms / 1.0 MB | 1.6x | 4x |
| 10,000 | 295 ms / 43.1 MB | 303 ms / 38.5 MB | 50 ms / 7.3 MB | 6x | 5x |
| 15,000 | - | 435 ms / 54.8 MB | 62 ms / 10.1 MB | 7x | 5x |
| 30,000 | 828 ms / 123.4 MB | 799 ms / 109.7 MB | 112 ms / 19.8 MB | 7x | 5.5x |

After the rewrite the cost is about 3.5 ms and 0.65 MB per 1,000 guests plus about 10-15 ms for the 50 full rows of a page (was 29 ms and 4.3 MB per 1,000). At the legacy volume (14,683 guests) a page, a search or a profile open is about 60 ms and 10 MB on this PC. The remaining linear part is the key pass itself (the customer key is computed in C# by `KeyOf` with Unicode name normalisation and merge aliases, which SQL cannot reproduce exactly), now on narrow index-only rows.

### Pass threshold (in `CustomerQueryBenchmark`)

With `HCC_PERF=1` the benchmark now fails if any rewritten call has p95 over `120 + 8 ms per 1,000 guests` (1k: 128, 10k: 200, 15k: 240, 30k: 360 ms) or a median allocation over `2 + 1 MB per 1,000 guests` (1k: 3, 10k: 12, 15k: 17, 30k: 32 MB). That is about 1.5x the measured allocation (deterministic) and 2-3x the measured time, so it does not flake on this PC, and far under the pre-rewrite 29 ms / 4.3 MB per 1,000 guests, so a return to whole-property loading fails at 10k and above. This run: pass.

### Bill-correction register
The 4 Nov measurement did not flag `SqlBillCorrectionStore.ListAsync` (it is one command with an id table and two result sets, af-ctl02-perf); no change.

### Tables (this run)
#### N = 1,000 guests (seeded in 0.4 s; limit p95 128 ms, 3.0 MB)
| call | total rows | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|---|
| ListAsync q=null page 1 | 1004 | 24 | 28 | 1.0 |
| ListAsync q=null page 1 (before) | 1004 | 38 | 40 | 4.0 |
| ListAsync q=null page 20 | 1004 | 19 | 25 | 1.0 |
| ListAsync q=null page 20 (before) | 1004 | 34 | 63 | 3.9 |
| ListAsync q=rao page 1 | 102 | 19 | 20 | 1.2 |
| ListAsync q=rao page 1 (before) | 102 | 31 | 37 | 4.1 |
| ListAsync q=rao page 20 | 102 | 8 | 10 | 0.9 |
| ListAsync q=rao page 20 (before) | 102 | 31 | 36 | 4.0 |
| ListAsync q=900012 page 1 | 0 | 9 | 20 | 0.8 |
| ListAsync q=900012 page 1 (before) | 0 | 30 | 35 | 3.9 |
| ListAsync q=900012 page 20 | 0 | 9 | 11 | 0.8 |
| ListAsync q=900012 page 20 (before) | 0 | 30 | 35 | 3.9 |
| ListAsync q=0001234 page 1 | 0 | 8 | 10 | 0.8 |
| ListAsync q=0001234 page 1 (before) | 0 | 31 | 32 | 3.9 |
| ListAsync q=0001234 page 20 | 0 | 8 | 9 | 0.8 |
| ListAsync q=0001234 page 20 (before) | 0 | 31 | 33 | 3.9 |
| ProfileAsync one key | 2 | 12 | 15 | 0.8 |
| ProfileAsync one key (before) | 2 | 34 | 48 | 3.9 |

#### N = 10,000 guests (seeded in 0.9 s; limit p95 200 ms, 12.0 MB)
| call | total rows | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|---|
| ListAsync q=null page 1 | 10004 | 50 | 66 | 7.3 |
| ListAsync q=null page 1 (before) | 10004 | 303 | 337 | 38.5 |
| ListAsync q=null page 20 | 10004 | 44 | 50 | 7.3 |
| ListAsync q=null page 20 (before) | 10004 | 303 | 320 | 38.5 |
| ListAsync q=rao page 1 | 1002 | 51 | 56 | 8.7 |
| ListAsync q=rao page 1 (before) | 1002 | 314 | 382 | 39.9 |
| ListAsync q=rao page 20 | 1002 | 51 | 71 | 8.7 |
| ListAsync q=rao page 20 (before) | 1002 | 380 | 442 | 39.9 |
| ListAsync q=900012 page 1 | 0 | 47 | 109 | 7.1 |
| ListAsync q=900012 page 1 (before) | 0 | 302 | 326 | 38.5 |
| ListAsync q=900012 page 20 | 0 | 38 | 49 | 7.1 |
| ListAsync q=900012 page 20 (before) | 0 | 308 | 326 | 38.5 |
| ListAsync q=0001234 page 1 | 1 | 42 | 82 | 7.1 |
| ListAsync q=0001234 page 1 (before) | 1 | 311 | 345 | 38.5 |
| ListAsync q=0001234 page 20 | 1 | 35 | 45 | 7.1 |
| ListAsync q=0001234 page 20 (before) | 1 | 309 | 335 | 38.5 |
| ProfileAsync one key | 2 | 39 | 58 | 7.1 |
| ProfileAsync one key (before) | 2 | 302 | 324 | 38.5 |

#### N = 15,000 guests (seeded in 1.2 s; limit p95 240 ms, 17.0 MB)
| call | total rows | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|---|
| ListAsync q=null page 1 | 15004 | 62 | 75 | 10.1 |
| ListAsync q=null page 1 (before) | 15004 | 435 | 501 | 54.8 |
| ListAsync q=null page 20 | 15004 | 157 | 175 | 10.1 |
| ListAsync q=null page 20 (before) | 15004 | 419 | 448 | 54.8 |
| ListAsync q=rao page 1 | 1502 | 64 | 78 | 12.2 |
| ListAsync q=rao page 1 (before) | 1502 | 441 | 470 | 56.9 |
| ListAsync q=rao page 20 | 1502 | 67 | 89 | 12.2 |
| ListAsync q=rao page 20 (before) | 1502 | 446 | 463 | 56.9 |
| ListAsync q=900012 page 1 | 0 | 52 | 64 | 9.8 |
| ListAsync q=900012 page 1 (before) | 0 | 433 | 452 | 54.7 |
| ListAsync q=900012 page 20 | 0 | 54 | 72 | 9.8 |
| ListAsync q=900012 page 20 (before) | 0 | 420 | 442 | 54.7 |
| ListAsync q=0001234 page 1 | 11 | 55 | 76 | 9.9 |
| ListAsync q=0001234 page 1 (before) | 11 | 423 | 521 | 54.7 |
| ListAsync q=0001234 page 20 | 11 | 47 | 67 | 9.8 |
| ListAsync q=0001234 page 20 (before) | 11 | 422 | 443 | 54.7 |
| ProfileAsync one key | 2 | 50 | 78 | 9.9 |
| ProfileAsync one key (before) | 2 | 417 | 453 | 54.7 |

#### N = 30,000 guests (seeded in 1.9 s; limit p95 360 ms, 32.0 MB)
| call | total rows | p50 ms | p95 ms | allocated MB / call |
|---|---|---|---|---|
| ListAsync q=null page 1 | 30004 | 112 | 126 | 19.8 |
| ListAsync q=null page 1 (before) | 30004 | 799 | 830 | 109.7 |
| ListAsync q=null page 20 | 30004 | 114 | 129 | 19.8 |
| ListAsync q=null page 20 (before) | 30004 | 805 | 891 | 109.7 |
| ListAsync q=rao page 1 | 3002 | 115 | 132 | 24.0 |
| ListAsync q=rao page 1 (before) | 3002 | 823 | 863 | 113.9 |
| ListAsync q=rao page 20 | 3002 | 113 | 139 | 24.0 |
| ListAsync q=rao page 20 (before) | 3002 | 852 | 1010 | 113.9 |
| ListAsync q=900012 page 1 | 0 | 124 | 156 | 19.5 |
| ListAsync q=900012 page 1 (before) | 0 | 814 | 839 | 109.6 |
| ListAsync q=900012 page 20 | 0 | 104 | 129 | 19.5 |
| ListAsync q=900012 page 20 (before) | 0 | 794 | 844 | 109.6 |
| ListAsync q=0001234 page 1 | 11 | 112 | 146 | 19.6 |
| ListAsync q=0001234 page 1 (before) | 11 | 858 | 958 | 109.6 |
| ListAsync q=0001234 page 20 | 11 | 114 | 130 | 19.5 |
| ListAsync q=0001234 page 20 (before) | 11 | 825 | 921 | 109.6 |
| ProfileAsync one key | 2 | 107 | 121 | 19.6 |
| ProfileAsync one key (before) | 2 | 833 | 871 | 109.6 |
