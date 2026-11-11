# cu-digits AUDIT (Opus, A1)

Branch `claude/cu-digits`, head d8efcdab, base c45bbce3. Read: CU-CONTRACT §1, §2, §3.7, `af2-phone-digits-DONE.md`, `cu-digits-DONE.md`, `git diff c45bbce3..HEAD`, `git log -p c45bbce3..HEAD -- "08 Production/tests"`, plus the code the diff touches (CounterBillService, ImportPlanner, ImportService, AccountingVoucherProjector, AccountingEventMapper, PartyLedgers, GuestRules, CustomerCommands.SafeName, SqlCounterBillStore, Decision Register DEC-243). No heavy job run: every claim below was settled by reading.

## Verdict: FIX

The production change is sound and the money path is unchanged in every reachable case. Two problems with the tests: the import test passes on the base, so it proves nothing, and the `CounterBillCredited` payload is never asserted.

## (a) Security-flag question: was any assertion weakened?

- **No existing test was removed, skipped or loosened.** `git diff --name-status` shows only added test files (`A`). No AF2 test (`CustomersKeyTests`, `AuditFixVerifyCrmTests.V_CRM_11`, `stream-customers.mjs [AF2-PHONE]`) or any other existing test is touched.
- **`if (masked) Assert.DoesNotMatch(...)` is not a weakening.** `HasPhoneDigits == masked` is still asserted on every row. On the `masked=false` rows the theory asserts `Scrub(text) == text`, and that is stronger than "no 10-digit run". The unconditional form could never pass on the date row: the test's own `TenDigits` regex reads "2026-11-11 2026-11-12" as one run, while the scrub correctly leaves ISO dates alone. The change is right.
- **The `"9820005555"` → `"919820005555"` change corrects a test expectation; production behaviour did not change.** `CounterBillService.Digits()` is unchanged from the base and stores every ASCII digit, so "+91 98200 05555" was stored as `919820005555` before this lane too. The Udhaar receivable mobile (`p[^10..]`) and the party key (`PartyLedgers.Udhaar` → `digits[^10..]`) still use the last 10 digits, so they stay `9820005555` / `UD-9820005555`. The test asserts both.
- **Which new tests would pass on the base:**
  - Counter SQL test: fails on the base, as it should. The base payload carries `PartyPhone = "919820005555"`, and the second bill's name "98200 06666" is in the audit text.
  - Mapper test: fails on the base, as it should. The base mapper ignores `parties.Udhaar`, and a masked payload resolves to `UD-walk-in-fictional`.
  - Accounting projection test: fails on the base, as it should, because of the party assertion. Its outbox `NOT LIKE` assertion is vacuous: the test publishes its own masked payload.
  - Scrub theory: does not compile on the base, because the helper is new.
  - **Import test: PASSES on the base** (see FIX 1).

## (c) Accounting: same party in every case?

Yes, in every case that can happen. `ctr.bill.party_name` and `party_phone` are written once by `SqlCounterBillStore.InsertAsync`, and nothing in `src` or `database` updates them, so the phone can never change after issue. The values the projector reads from the bill row are exactly the values the old payload carried, for both events: Issued uses `name`/`phone` as inserted, and Credited uses `b.PartyName`/`b.PartyPhone` read back from the row.

| Case | Party before and after |
|---|---|
| New payload | `UD-<last 10>`, same |
| Old payload replayed | Bill row read, same |
| Bill credited / voided | Same: party fields untouched by the void |
| Bill row missing | Differs for old payloads only (Note 4) |

Other checks:
- **Idempotency.** The source key `counter:` / `counter-credit:<billId>` is unchanged, and the read is deterministic.
- **Read consistency.** The read runs inside the projector's transaction. The bill row commits in the same transaction as the outbox row, so the row is always there when the event is.
- **The mask cannot become a key.** `••••••5555` has 4 digits, which is under 10, so `PartyLedgers.Udhaar` can never key on it.
- **DEC-243** (phone-less guest credit → `UD-<customer key>`, still Proposed) is neither pre-empted nor contradicted: the counter rule `UD-<mobile>` / `UD-<slug>` is untouched.
- **Migrations and versions.** No migration, no SCHEMA bump.

## Findings

1. **FIX — `08 Production/tests/Hcc.Import.Tests/Sql/CuDigitsImportTests.cs:18-30` tests a path that never leaked.**
   - The guests planner passes `label = "{Name} · {GuestRules.MaskPhone(phone)}"` as the item key (`ImportPlanner.cs:1174-1176`). The raw `GuestKey` `phone|name` lives only in `GuestData`, so on the base `IMPORT_GUEST` already reads `Create · Asha Rao · ••••••1111 · New guest profile · batch N`. The test passes without the fix.
   - The DONE file's claim "the leak was the guest plan key `phone|name`" is wrong.
   - The real import leaks are raw phones in the details for suppliers (`ImportPlanner.cs:1070`, `"New supplier {name} · {trade} · {phone}"`, plus `phone A → B` at line 1080) and for companies (`Diff("phone", …)`, line 1037). The wrapper does scrub both, so the code is fine.
   - Right looks like this: the test imports a supplier with a 10-digit phone (`IMPORT_SUPPLIER`, `MNT_VENDOR`), then re-imports a company with a changed phone (`IMPORT_COMPANY` update). It asserts no 10-digit run in those rows. Correct the DONE claim.
2. **FIX — `08 Production/tests/Hcc.Counter.Tests/Sql/CuDigitsCounterTests.cs:55` never asserts the `CounterBillCredited` payload.** The query filters `event_type = 'CounterBillIssued'`. Credited goes through the same `Posted()`, so it is safe by construction, but the brief names both events and nothing would catch a regression in the void path. Right looks like this: request and approve a void of the Udhaar bill, using a second approver (SoD). Then assert the same three checks on `CounterBillCredited` payloads (`DoesNotMatch(TenDigits)`, no raw number, mask present).
3. **NOTE — `CounterBillService.cs:236-237` scrubs the whole sentence, raw paise amounts included.** The COUNTER_BILL_ISSUED text prints `net {Net.Value} + tax … = {Total.Value}` as raw paise. Any value of ₹1 crore or more is 10+ digits (10^9 paise) and becomes `***`, so the audit amount is lost. Postings are unaffected: they use the structured fields. A space-separated run of numbers also merges, so `101 2026-11-11` becomes `***` because the date branch only wins when the date starts the match. Right looks like this: scrub only the interpolated `name` (and later the reason), not the whole template. The import texts format money through `Paise.ToString()` with Indian commas, so they are safe.
4. **NOTE — `AccountingVoucherProjector.cs:251-257`: the fallback for a missing bill row ignores the payload phone, and the bill read has no tenant filter.**
   - When the `ctr.bill` row is missing, the method returns `PartyLedgers.Udhaar(fallbackName, null)`. For an old payload carrying a raw number, the old code gave `UD-<mobile>`; this gives `UD-<slug>`. The mapper's own old-payload fallback is dead in the projector path, because the projector always supplies a non-null `Udhaar`. A row cannot normally be missing (same transaction, no delete).
   - Right looks like this: fall back to `PartyLedgers.Udhaar(e.PartyName, e.PartyPhone)`. That is safe for masked payloads (4 digits) and exact for old ones.
   - Also add `AND property_id = @p`; `propertyId` is in scope. Bill ids are global identities, so this is defence in depth only, and the sibling `UdhaarPartyAsync` has the same gap.
5. **NOTE — `PhoneDigitsScrub.cs:221-227`: format coverage.**
   - Masked: plain 10 digits, `+91`, `0` prefix, spaces, dashes, brackets, 5+5 grouping, Aadhaar-like 12 digits.
   - Not masked: dot- or slash-separated phones (`98200.05555`), phones split by `+` mid-run, 7-9-digit landlines (vendor phones may be 7+), and Devanagari digits. .NET `\d` matches Devanagari, but the count uses `char.IsAsciiDigit`, so `९८२०००५५५५` passes through unchanged.
   - Not damaged: GSTINs, invoice numbers (`/`), ISO dates and timestamps, last-4 values, and comma-formatted amounts.
   - Right looks like this: count `char.IsDigit`, and add Devanagari and dotted cases to the theory.
6. **NOTE — there are now two diverging helpers.** AF2 `CustomerCommands.SafeName` masks the whole name when it holds 8+ digits (`char.IsDigit`, `+` as a joiner). `PhoneDigitsScrub` masks per run at 10+ digits (`IsAsciiDigit`). The lane's reason for not extending AF2's helper is valid: it is a whole-string name rule. But the brief said "apply the same masking helper". Right looks like this: one follow-up makes `SafeName` call `PhoneDigitsScrub` with a shared threshold and digit rule.
7. **NOTE — `ImportService.cs:63-64` and the wrapped `Clip(…, 1000)` calls clip before scrubbing.** A phone cut at character 999 leaks up to 9 digits. `AuditRow` scrubs before clipping, which is the right order everywhere. File names with long timestamps (`guests_20261111103000.csv`) become `guests_***.csv` in the audit text. That is harmless.
8. **NOTE — projection narration.** `AccountingEventMapper.cs:68` narration now carries the scrubbed `PartyName` for new events. That is an improvement: a name that is a phone no longer reaches `acc.source`. Replayed old events keep their old narration. No other consumer exists: across `08 Production/src` and `03 Prototype/src`, `CounterBillIssued` / `CounterBillCredited` / `CounterBillPostedEvent` are read only by the accounting projector. Retention purge deletes rows and does not parse them.

## (d) Import plan key

Scrubbing touches only the audit strings, and only after the plan is built. Dedupe (`seen`, `snap.GuestKeys`), the stored batch result JSON and `PlanRows` still use the unscrubbed keys, so nothing that consumes the plan key is affected.

## (f) Scope, line endings, DONE claims

- **Scope.** Nine files, all inside the brief. No shared file, no migration, no SCHEMA bump. The customer-facing bill is unchanged and asserted.
- **Line endings.** `git diff --stat` and `--ignore-cr-at-eol` match (224/19), and the new files are LF.
- **Branch.** Pushed at d8efcdab.
- **DONE claims.** Accurate except the "leak was the guest plan key" claim (Finding 1). The admission that no red check was run is honest: the counter and accounting tests are red on the base, and the import test is not.

Auditor: Opus (A1), 11 Nov 2026.
