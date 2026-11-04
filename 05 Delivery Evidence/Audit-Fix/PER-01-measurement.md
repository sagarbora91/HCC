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
