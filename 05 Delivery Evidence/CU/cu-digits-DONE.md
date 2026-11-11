# cu-digits DONE (phone digits in counter-bill outbox / audit and import audit)

Lane digits / Sonnet / branch claude/cu-digits / worktree E:\Projects\HCC\worktrees\cu-digits / base c45bbce3. Not merged to main.

## What changed (all under `08 Production`, LF kept)
- NEW `src/Hcc.Domain/Audit/PhoneDigitsScrub.cs`: `Scrub(text)` replaces any run of 10 or more digits (joined by spaces, hyphens, plus, parentheses) with `***`; ISO dates and shorter numbers (amounts, ids) are untouched; `HasPhoneDigits`. AF2's `CustomerCommands.SafeName` only masks a *name* (whole-string rule, 8+ digits), which would wreck a full sentence, so this sentence-safe sibling follows the same regex idea.
- `src/Hcc.Application/Counter/CounterBillService.cs`: audit text of COUNTER_BILL_ISSUED is scrubbed; `Posted()` (used for both `CounterBillIssued` and `CounterBillCredited` outbox payloads) now carries `PartyPhone` as the display mask `••••••5555` (`GuestRules.MaskPhone`, null when no phone) and `PartyName` scrubbed.
- Consumer check: the only reader of the payload is `AccountingEventMapper.CounterBill` via `AccountingVoucherProjector`, which needs the real mobile to name the Udhaar party `UD-<10 digits>` (ledger identity, DEC / contract §3). So the number was NOT simply dropped: `AccountingVoucherProjector` now reads `party_name, party_phone` from `ctr.bill` by `BillId` (new `CounterUdhaarPartyAsync`) and passes it in `PartyContext.Udhaar`; `AccountingEventMapper.CounterBill` uses `parties.Udhaar` first and falls back to the payload (so old in-flight outbox rows with a raw number still project to the same party; the mapper unit test with a raw number is unchanged and green).
- `src/Hcc.Application/Import/ImportService.cs`: every audit row passes the scrub (new `AuditScrubbed` wrapper replaces all `tx.AuditAsync` calls; `AuditRow` scrubs before clipping). The leak was the guest plan key `phone|name` in `IMPORT_GUEST` text.
- Customer-facing bill untouched: `ctr.bill.party_phone`, `ops.udhaar_receivable.mobile`, invoice print and bill reads still hold/show the real data (asserted).

## Tests (all through the lock, `-maxcpucount:1`)
- New `tests/Hcc.Counter.Tests/Sql/CuDigitsCounterTests.cs` (scrub theory x7 + mapper test + SQL test: Udhaar bill with phone, bill whose name is a phone; audit + outbox hold no 10-digit run, mask keeps last 4, `ctr.bill.party_phone` and receivable mobile still raw).
- New `tests/Hcc.Accounting.Tests/Sql/CuDigitsCounterProjectionTests.cs`: masked payload published, projector lands on party `UD-9820005555`, payload has no number.
- New `tests/Hcc.Import.Tests/Sql/CuDigitsImportTests.cs`: guest import (two phones) -> no 10-digit run in any IMPORT_* audit row.
- Results: Import `--filter CuDigits` 1/1 passed; Accounting `--filter CuDigits` 1/1 passed; Counter: full project run 96 passed / 2 failed (both failures were mistakes in my new test: a regex check on an unmasked date line, and the stored bill phone is `919820005555` with country code, not 10 digits); after fixing the test, `--filter CuDigits` 9/9 passed. All 96 existing Counter tests passed in the full run (no code change since, only the test file).
- Red check (tests fail without the fix) was not run separately; the leak path (`phone|name` key in the audit detail, raw `PartyPhone` in payload) was confirmed by reading the code.

## NOT verified
- Full Counter/Import/Accounting suites beyond the figures above; full server suite (per contract).
- Old `plat.audit_event` rows are append-only and old outbox rows keep raw numbers (same as AF2); not scrubbed.

## Defaults used
- Threshold: 10+ digits (a phone), so 8-9 digit numbers, amounts and ISO dates stay readable in audit text.
- Payload keeps the last four digits as `••••••NNNN` (same display mask as the guest screens) rather than dropping the field.
- Other import masters' phones (companies, vendors) are covered by the same wrapper, not only guests.

## Open questions / out of scope
- Free-text reason / note / tag fields (e.g. counter void reason, import remark columns) are not scanned beyond the generic scrub applied to the whole audit text; user-typed digits below 10 are left.
- `CounterBillListRow` / LF money reads still return the real phone to permitted readers (unchanged by design).

## Shared files touched
None of the marked shared files (no slnx, migrations, registries, CSS). No migration, no SCHEMA bump.
