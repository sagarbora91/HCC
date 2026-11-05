# af2-per01 DONE (PER-01 rewrite, owner approved Q7)

Lane af2-per01 / branch `claude/af2-per01` / worktree `E:\Projects\HCC\worktrees\af2-per01` / base `origin/main` e949a9e5. Not merged, not pushed to main.

## What was built
The customer list / search / profile (Customers 360) reads no longer load the whole property and build every customer in memory per call:
1. **Key pass** - `SqlCustomerReadStore.HeadsAsync` reads only narrow rows (guest id / name / mobile; booking id / number / registered guest of this property / arrival, the booking name only when there is no such guest; aliases; business date), index-only from migration **4910** (`IX_guest_crm_head`, `IX_reservation_crm_head`). `CustomerRules.Heads` gives the same keys (`KeyOf` + merge aliases), names, mobiles, newest arrival and order as `CustomerRules.Index`; it keeps one head per customer plus a slot per booking (`CustomerHeadIndex`, `RowsOf`). Search (`SearchHeads`, the same rule via one shared matcher), tag filter, total and paging run on the heads.
2. **Detail load** - `SqlCustomerReadStore.LoadSomeAsync` reads full rows only for the page's customers (or the one profile): id sets in table variables, then seeks (`IX_charge_folio`, 4910; the reversal check now says `x.reverses_charge_id IS NOT NULL` so the filtered `IX_charge_reverses` is used). `CustomerRules.Index` on those rows rebuilds exactly the same customers.
3. `ck_` tokens: hashed only for the rows sent out; an inbound token is resolved by hashing keys until it matches (no per-call token map). `MergesAsync` needs no customer read. Role masking, money masking and the `ck_` output are untouched (same `CustomerViews`).
4. Hot-rule allocations cut with identical results: `Digits` (no StringBuilder), `Resolve` (early return when the key has no alias), `NormName` (one-pass ASCII path).
5. `LoadAsync` (still used by `DuplicatesAsync` and the commands) shares its SQL fragments with `LoadSomeAsync` and gained explicit `ORDER BY guest_id` / `reservation_id` (was scan order; pins the tie order both paths share).

Not changed: `DuplicatesAsync` (needs every customer); `CustomerCommands.cs` (merge / tag / note / preference commands still call `LoadAsync` to resolve the key; the file carries the audit / outbox calls another lane is editing, so it was left alone - follow-up: switch `Find` / `EditableAsync` to `HeadsAsync` + `LoadSomeAsync` the same way). Audit / outbox code in `SqlCustomerStore.cs` (write side) not touched. The bill-correction register was not flagged by the 4 Nov measurement; no change.

## Numbers (Debug build, this PC; p50 ms / MB per call, list page 1 q=null; same run, same database)
| N | before | after |
|---|---|---|
| 1,000 | 38 / 4.0 | 24 / 1.0 |
| 10,000 | 303 / 38.5 | 50 / 7.3 |
| 15,000 (legacy import size) | 435 / 54.8 | 62 / 10.1 |
| 30,000 | 799 / 109.7 | 112 / 19.8 |
Profile and searches are the same order. Full tables and the 4 Nov baseline: `05 Delivery Evidence/Audit-Fix/PER-01-measurement.md` (section "After the rewrite").
**Pass threshold** in `CustomerQueryBenchmark` (only with `HCC_PERF=1`): p95 <= 120 + 8 ms per 1,000 guests and median allocation <= 2 + 1 MB per 1,000 guests for every rewritten call; this run passes at 1k / 10k / 15k / 30k; the old query would fail from 10k up. `HCC_PERF_OLD=1` also times the pre-rewrite query ("(before)" rows).

## Equivalence proof (tests)
- `tests/Hcc.Customers.Tests/Kernel/CustomerHeadsEquivalenceTests.cs` (18 cases): on seeded random data (shared / short / empty mobiles, case and Devanagari variants, symbol-only names, bookings without a profile or with an unknown guest id, every status, arrival ties, aliases incl. a chain and a cycle) `Heads` equals `Index` key for key, name, mobile, newest arrival and order; each customer and a 7-customer page rebuild byte-for-byte (JSON) from `RowsOf` only; `SearchHeads` equals `Search` (names, Devanagari, digits, masked roles); fast `NormName` / `Digits` equal the old rule on 4,000+ random ASCII strings and edge cases.
- `tests/Hcc.Customers.Tests/Sql/CustomerQueryEquivalenceTests.cs` (4 tests, SQL): the pre-rewrite `ListAsync` / `ProfileAsync` kept verbatim as `PreRewriteCustomerQueries` (test project only; `InternalsVisibleTo Hcc.Customers.Tests` added to `Hcc.Application.csproj`). On CrmWorld plus 120 random guests / 300 bookings (folios, a reversed night, service lines, room segments, reserved nights, another property's guest, approved aliases incl. a chain, tags): **1,152 list calls** (4 roles x 12 search texts x 3 tags x 8 page / page-size cases incl. 0 / 500 / beyond the end) and **every profile** by `ck_` token, raw name key, raw mobile key and merged-away key for 4 roles (incl. the NOT_FOUND cases) return identical JSON; merge list tokens unchanged; `LoadSomeAsync` ignores other properties' ids and empty sets.

## Files
- `08 Production/src/Hcc.Domain/Customers/CustomerRules.cs` (CrmGuestHead, CrmStayHead, CustomerHead, CustomerHeadIndex, Heads, SearchHeads, Digits / Resolve / NormName fast paths) - LF
- `08 Production/src/Hcc.Application/Customers/CustomerPorts.cs` (CrmHeadSource, CrmDetail, HeadsAsync / LoadSomeAsync on the port) - CRLF
- `08 Production/src/Hcc.Application/Customers/CustomerQueries.cs` (ListAsync, ProfileAsync, MergesAsync, lazy CustomerKeySpace) - CRLF
- `08 Production/src/Hcc.Application/Hcc.Application.csproj` (InternalsVisibleTo) - CRLF
- `08 Production/src/Hcc.Infrastructure.SqlServer/Customers/SqlCustomerStore.cs` (read store only: LoadAsync refactor, HeadsAsync, LoadSomeAsync) - LF
- `08 Production/database/migrations/4910_crm_customer_list_indexes.sql` (new, index only), `migrations.lock.json` (`--add`), `Hcc.slnx` (lists the file) - LF
- Tests: `Kernel/CustomerHeadsEquivalenceTests.cs`, `Sql/CustomerQueryEquivalenceTests.cs` (new, LF); `Perf/CustomerQueryBenchmark.cs` (threshold, 15k, before rows, `q=0001234`; CRLF kept)
- Evidence: `PER-01-measurement.md` (appended), this file.
Line endings: `git diff --stat` and `--ignore-cr-at-eol` give identical counts.

## Tests (all through `tools\Invoke-Serialized.ps1`)
- `dotnet build Hcc.slnx`: 0 warnings, 0 errors.
- `dotnet test Hcc.slnx -maxcpucount:1`: 42 projects, **4,283 tests** (was 4,261; +22 in Hcc.Customers.Tests, now 103). One failure in the first full run (`Hcc.Masters.Tests` checks that `Hcc.slnx` lists every migration); fixed by listing 4910; `Hcc.Masters.Tests` re-run: 36/36.
- Benchmark `HCC_PERF=1 HCC_PERF_N=1000,10000,15000,30000 HCC_PERF_OLD=1`: pass (7 min 16 s); all `HccTest_*` databases dropped.
- Prototype (`03 Prototype`): nothing changed there, so `npm test` was not re-run (2,840 on main).

## HANDOFF bullet
af2-per01 (5 Nov): PER-01 rewrite done (owner Q7). Customer list / search / profile = narrow index-backed key pass + full rows for the page only (migration 4910); identical results proved against the old query (1,152 list calls + every profile, 4 roles). 30k guests: 799 -> 112 ms, 110 -> 20 MB per call; legacy size (15k): about 60 ms / 10 MB. Benchmark has a pass threshold. Follow-up: `CustomerCommands` key lookup still loads the whole property.
