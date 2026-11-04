# af-ctr-reads DONE

Lane af-ctr-reads / branch claude/af-ctr-reads / worktree E:\Projects\HCC\worktrees\af-ctr-reads / base 6a29f3e6 (+ claude/auditfix merged: runner fix).

## Closed
- CON-01: `SqlLfMoneyReadStore.CounterBillsAsync` is one SqlCommand, one batch: fixed id set in `@ids`, lines and headers both joined to it (comment `// AF-CTR-READS (CON-01): one batch, one id set`).
- HIS-01 (server): store `CounterBillsAsync(propertyId, take, invoiceNo, from, to, ct)`; service `CounterBillsAsync(propertyId, principalId, invoiceNo=null, from=null, to=null, take=null, ct)` - take clamped 1..500, from>to and invoice no > 40 chars are VALIDATION, invoice no trimmed, compared `RTRIM(invoice_no) = @inv`; endpoint `GET /counter-bills?invoiceNo=&from=&to=&take=` (yyyy-MM-dd, bad date = VALIDATION). Response JSON shape unchanged. Same permission.

## Files
LfMoneyReads.cs, SqlLfMoneyReadStore.cs, LfMoneyEndpoints.cs (GET /counter-bills lambda only), tests/Hcc.Counter.Tests/Sql/LfMoneyReadsTests.cs (existing test calls now pass `ct:` by name because the service gained optional parameters before ct).

## Tests
`dotnet test tests/Hcc.Counter.Tests -maxcpucount:1 --logger trx` (via lock): before 22, after 27/27 pass. Five new: Counter_list_every_returned_header_has_its_lines_when_take_is_smaller_than_the_bill_count, Counter_lookup_by_invoice_no_finds_a_bill_outside_the_newest_take, Counter_lookup_by_date_range, Counter_lookup_from_after_to_is_VALIDATION, Counter_lookup_invoice_no_is_trimmed_and_exact.
`dotnet build Hcc.slnx`: 0 warnings, 0 errors (before the final test-only edit; src unchanged since).
Not run: Hcc.Integration.Tests (no test references /counter-bills; no other caller of the changed signatures). Prototype `npm test` not run (no prototype files touched).
Secret scan: ok=true, 0 findings.

## Open items / Integration needed
None. af-ctr-ui can call `GET /counter-bills?invoiceNo=...&from=...&to=...`.

## HANDOFF bullet
af-ctr-reads: counter-bill list reads in one SQL batch (no header without lines); `GET /counter-bills` now filters by invoice no / date range / take, so older bills can be found for credit notes.
