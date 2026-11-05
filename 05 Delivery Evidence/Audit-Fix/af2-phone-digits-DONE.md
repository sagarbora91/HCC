# af2-phone-digits DONE (V-CRM-11: raw phone digits in audit target and CustomerMerged outbox)

Lane af2-phone-digits / Sonnet / branch claude/af2-phone-digits / worktree E:\Projects\HCC\worktrees\af2-phone-digits / base e949a9e5. Not merged, not pushed to main.

## What changed
Server only (`08 Production/src/Hcc.Application/Customers/CustomerCommands.cs`, LF kept):
- Audit target is now `crm:` + the opaque `ck_` token (`CustomerKeySpace.TokenOf(rawKey)`, same HMAC the API shows), for CRM_MERGE_PROPOSE / APPROVE / REJECT, CRM_TAG (add and remove), CRM_NOTE, CRM_PREFERENCES, CRM_CONSENT. The token is stable per customer, so "who / what / when / which customer" is unchanged for the trail; only the digits are gone.
- `CustomerMerged` outbox payload is `{ MergeId, SourceKey = ck_ token, TargetKey = ck_ token }`.
- Audit detail text carries the customer name; a name that is itself a phone number (8 or more digits in a row) is masked as `***` (`SafeName`), so a customer registered under a number does not leak it in the detail. Normal and Devanagari names are untouched.
- No migration: `plat.audit_event` is append-only (trigger `TR_audit_event_append_only`, migration 0001, UPDATE/DELETE throws 51000), so old rows cannot be scrubbed.

## Consumers checked
Repo-wide search for readers of the `crm:` target and of `CustomerMerged`: no production reader. Subscribers to the outbox event: none (no consumer registers `CustomerMerged`; the Audit/A07 note says "keys only"). Audit viewer lists `target` as an opaque string. Reports, sync mappers, LG legacy import (uses `KeyOf` for rollups, not audit) and the client do not parse either field. Tests that read them were updated (below). The prototype has no CRM merge command and no outbox; its mock `s.audit` is covered by a parity test.

## Tests
- `tests/Hcc.Customers.Tests/Sql/CustomersKeyTests.cs`: new `Audit_rows_and_outbox_payloads_of_every_crm_command_carry_no_phone_digits` - tag add/remove, note, preferences, consent, three merges (Devanagari approved, Devanagari rejected, ASCII Vikram by raw keys), Devanagari phone-less customers and a customer named by a phone number; asserts no phone number and no 10-digit run in any CRM audit row or CustomerMerged payload, every CRM audit target is `crm:ck_...`, target equals the API token (note, propose, approve), outbox SourceKey / TargetKey are the tokens.
- `AuditFixVerifyCrmTests.V_CRM_11` flipped from "observation: raw key" to "target equals `crm:` + token, no phone digits".
- `03 Prototype/tests/stream-customers.mjs`: `[AF2-PHONE]` - mock audit rows of a Devanagari-named customer booked with a mobile number hold no raw phone digits.
- Counts: see the final section (filled from the runs).

## Open items
- HISTORICAL rows keep raw keys: existing `plat.audit_event` rows (`crm:<mobile>`) cannot be scrubbed (append-only) and old `plat.outbox` CustomerMerged payloads were not rewritten (no consumer; owner may decide to purge delivered outbox rows). New rows are clean.
- **Found, not fixed (outside this lane, needs its own migration): `crm.customer_merge.source_key/target_key`, `crm.customer_alias.alias_key/target_key`, `crm.customer_tag/note/pref.customer_key` are VARCHAR(80) (migration 2300).** A Devanagari `n:` key is stored as `n:???...`, so two different Devanagari customers share one stored key for tags, notes, preferences, pending merges and alias rows, and after approval the alias does not match the real customer. This is an AF-CRM (ID-01) follow-up: change those columns to NVARCHAR(80) (drop / recreate PK, `UX_crm_merge_pending`, `CK_crm_merge_distinct`, `IX_crm_alias_target`) and re-check `lgc` / `gst.guest_credit_account` key columns. Because of it, the audit target of a Devanagari merge APPROVE/REJECT is the token of the stored (mangled) key, not of the real customer; PROPOSE and all tag/note/prefs/consent targets use the live key and are exact. Once the columns are widened these become exact with no further change here.
- Free-text audit parts typed by users (merge reason, decision note, tag) are not scanned for digits (Wave D already refuses ID numbers in free text).
