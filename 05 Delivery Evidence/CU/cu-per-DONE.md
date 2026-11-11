# cu-per DONE (wave CU, lane per)

## What changed
- `08 Production/src/Hcc.Application/Customers/CustomerCommands.cs`: propose / tag / note / preference / consent resolve the key on the narrow key pass (`HeadsAsync`, as `ProfileAsync` does) instead of `LoadAsync` + `Index`. Tag, note and preference need only head key + name; merge proposal and consent read the full customer of the one or two heads only (`LoadSomeAsync`). `DecideMerge` needs no customer read (only outbound tokens). An approved merge calls `CustomerKeyTokenCache.Invalidate`.
- `CustomerQueries.cs`: `DuplicatesAsync` = key pass + new `ICustomerReadStore.GuestEmailsAsync` (one narrow SQL query: guest_id, email of the property) + `CustomerRules.FindDuplicates(heads, emails)`; full customers loaded only for members of duplicate groups. `CustomerKeySpace` now resolves inbound tokens through the cache.
- New `CustomerKeyTokenCache.cs` (Application): bounded (16 properties LRU, 250,000 keys, larger = old plain scan) token -> raw key map per property, valid only for the same secret and the same key-set fingerprint (count + two 64-bit sums), so rotation, new/renamed keys and merges can never serve a stale answer, forged tokens are refused from memory (O(1)), and a rebuild re-hashes only new keys.
- `CustomerRules.cs` (Domain): `CustomerHeadIndex.Emails` (the e-mail Index would give each customer), `FindDuplicates(heads, emailBySlot)`; one shared private `Duplicates<T>` core also serves the old `Customer` overload (same output).
- `CustomerPorts.cs`: `GuestEmailsAsync`. `SqlCustomerStore.cs`: its implementation.
- Tests: `tests/Hcc.Customers.Tests/Sql/CustomerQueryEquivalenceTests.cs` (+5 facts), `Perf/CustomerQueryBenchmark.cs` (+1 HCC_PERF-gated timing test).
- Evidence: `05 Delivery Evidence/CU/cu-per-timing.md`.

## Tests run (all through the lock)
- `dotnet test tests/Hcc.Customers.Tests -maxcpucount:1 --filter CustomerQueryEquivalenceTests`: 9 passed, 0 failed (4 existing + 5 new).
- Full `tests/Hcc.Customers.Tests`: **122 passed, 0 failed, 0 skipped**.
- Timing test (HCC_PERF=1): 1 passed.
New facts: duplicates identical (JSON) to a verbatim pre-rewrite finder for all 4 roles; head key/name/stays/guest ids equal the full index for every customer (chunks of 2); head e-mails equal full-index e-mails; forged token refused and repeats/forged add zero HMACs; new customer after a refusal resolves, rotated secret (SQL UPDATE) refuses the old token and accepts the new one.

## Timing (Debug build, SQL Express, p50 ms / MB per call; see cu-per-timing.md)
| N | tag+untag lookups, before -> after | Duplicates, before -> after |
|---|---|---|
| 1,000 | 67 -> 25 ms (two lookups vs full pair incl. write) | 28 -> 18 ms |
| 10,000 | 580 -> 81 ms, 77 -> 14 MB | 308 -> 66 ms, 45 -> 21 MB |
| 15,000 | 832 -> 102 ms, 110 -> 20 MB | 433 -> 106 ms, 65 -> 29 MB |
Note the "after" tag row includes the two command transactions; "before" is only the two lookups, so the real gain is larger.

## NOT verified
- Concurrency of the static cache under parallel requests (lock-protected by design, no stress test).
- Full server suite / `Hcc.slnx` (not allowed); API tests in other projects that construct `ICustomerReadStore` fakes (none found by grep).
- `DuplicatesAsync` still reads every guest's e-mail (narrow column) and the key pass: linear, not constant.

## Defaults used
- Cache is static (no DI change); explicit `Invalidate` on approved merge although correctness does not depend on it.
- Secret rotation has no code path today; tested by direct SQL UPDATE.
- Cache limits 16 properties / 250,000 keys.

## Open questions
None. Lane nvarchar: no migrations, no SQL parameter types changed.

## Shared files touched
None of the marked shared files (slnx, migrations lock, CSS, stream-fit). Line endings kept (CRLF in CustomerPorts.cs / CustomerQueries.cs).
