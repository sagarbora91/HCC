# cu-nvarchar AUDIT (wave CU, lane nvarchar, auditor A1 / Opus)

Branch `claude/cu-nvarchar`, audited head 6631040e (code 45f8ffc5), base c45bbce3. Read: CU-CONTRACT sections 1, 2, 3.1; `cu-nvarchar-DONE.md`; `git diff c45bbce3..HEAD`; migrations 2300, 4600, 4684, 4694, 4812, 4900, 4910; `MigrationRunner.cs`; `CustomerRules.cs`; every C# use of a key column under `08 Production/src`. Light checks run: `node tools/migrations-lock.mjs --check` (in step, 238), `git diff --stat` vs `--ignore-cr-at-eol` (identical, 10 files), collation probes on `.\SQLEXPRESS` (server collation `Latin1_General_CI_AS`). No heavy job run; nothing in the lane's code was edited.

## Verdict: FIX (one test item; the production code and migrations are correct)

The schema change, the parameter changes and the migration safety are right. One required fix is in the tests: they do fail on the base, but not for the reason the brief names. The two Devanagari names never collide as VARCHAR, so the "do not share tags / merges" asserts would pass on the base as well.

## (a) Completeness

Every key column in `database/migrations/*.sql` that stores the CustomerRules key (`KeyOf` = phone last 10 digits, else `n:` + NormName, else `n:#` + hex):

| Column | Before | After |
|---|---|---|
| crm.customer_merge.source_key / target_key | VARCHAR(80) | NVARCHAR(80) (4930) |
| crm.customer_alias.alias_key / target_key | VARCHAR(80) | NVARCHAR(80) (4930) |
| crm.customer_tag / note / pref .customer_key | VARCHAR(80) | NVARCHAR(80) (4931) |
| lgc.guest_map / stay / bill / receipt .customer_key | VARCHAR(80) | NVARCHAR(80) (4932) |
| bill.folio.payer_customer_key (4350) | NVARCHAR(80) | unchanged, already right |
| meas.customer_preference(.._log).customer_key (4812) | NVARCHAR(80) | unchanged, already right; its C# params are NVarChar too (SqlMeasCaptureStore 198/215/230/383) |
| gst.guest_credit_account.customer_key (4694) | VARCHAR(40) | left. **Claim verified**: the only writer is `LfMoneyGuestCommands.cs:100` → `CustomerCreditKey.Of` (`CustomerRules.cs:455-461`), which gives 10 ASCII digits or `G-<guest id>`, never a name. |

Not customer keys: `lgc.guest_map.phone_key` (digits), `acc.*.source_key` / `map_key`, idem / causal / trigger / setting keys. The audit target of every CRM command is the `ck_` token (ASCII), not the raw key (`CustomerCommands.cs:31`). There are no views other than `lgc.v_customer_stay` (refreshed), and no functions, procedures, computed columns or schema-bound objects over these tables. `SqlBulkCopy` into lgc uses `typeof(string)` DataTable columns, so the destination type applies. The only `STRING_SPLIT` over keys is now `CAST(value AS NVARCHAR(80))` with an `NVarChar(-1)` parameter. **No remaining VARCHAR narrowing of a name-derived key was found.** The other VARCHAR `@k`s in `src` are kind codes, idem keys, accounting source keys and the credit key (all ASCII).

Reverse check (an NVARCHAR parameter against a VARCHAR column): **none in the lane.** One pre-existing case, outside the lane: `SqlGuestCreditStore.cs:23` joins `bill.folio.payer_customer_key` (NVARCHAR) to `gst.guest_credit_account.customer_key` (VARCHAR(40)). The server collation is a Windows collation, so the conversion still gives a range seek, and the table is tiny. Note only.

## (b) Migration safety: correct

- Re-runnable: each ALTER block is guarded by `sys.columns ... system_type_id = 167` (varchar). Each recreate is guarded by `sys.indexes` / `sys.key_constraints` / `sys.check_constraints` with `parent_object_id`. A second run is a no-op, and the test proves it.
- Recreated identically to 2300 / 4600: UX_crm_merge_pending (unique, `WHERE status = 'Pending'`, same columns), CK_crm_merge_distinct, PK_crm_alias (clustered by default, as in 2300), CK_crm_alias_distinct, IX_crm_alias_target, PK_crm_tag (property_id, customer_key, tag), IX_crm_note_customer, PK_crm_pref, IX_lgc_guest_map_key, IX_lgc_stay_customer / bill / receipt with `DESC` date columns. Not touched, correctly: PK_crm_merge, FK_crm_alias_merge, IX_lgc_bill_date (4684, no key column), IX_lgc_stay_guest, the UQ_lgc_* constraints. None of the originals had INCLUDE, a fill factor or a filter apart from UX_crm_merge_pending. Nullability is kept (bill / receipt NULL, the rest NOT NULL).
- Transactions: `MigrationRunner` runs each file in one transaction (one `SqlBatches` batch per GO) and rolls back on any error. A failure midway cannot leave a table without its PK. Auto-created statistics are dropped by ALTER COLUMN, and there are no `CREATE STATISTICS` objects.
- Key size: at most 4 + 160 + 60 bytes, far under the 900 / 1700-byte limits.
- Rows: VARCHAR to NVARCHAR widening keeps every value (proved by the downgrade test). The tables are small (lgc guest_map is about 15k rows after a real import).
- Lock / slnx: the lock is in step. The slnx lines sit in a `<!-- CU-NVARCHAR -->` block, which is nested inside the existing AF-CRM block (acceptable). 4930 > 4920, so no out-of-order flag is needed. **Merge order:** merge this lane before `pay-b` (4935). If 4935 lands first, 4930-4932 would follow it in the lock and need `--accept-out-of-order`. Note also that contract rule 3 gives migrations to lane nvarchar only.

## (c) BulkAsync catch: narrow enough

`SqlLegacyStore.cs:118` catches only an `InvalidOperationException` whose own or inner message contains `"would be truncated"`. That is the SqlClient client-side over-length message, so no other `InvalidOperationException` is turned into a VALIDATION error. The new message also leaves out the "Truncated value" text, which could hold a guest name; good. Note: the match is on an English resource string. Microsoft.Data.SqlClient ships no Hindi resources, so this is fine here, but a localised SqlClient would fall through to the generic error (not masked, just less friendly).

## Required fix

1. **`08 Production/tests/Hcc.Customers.Tests/Sql/CustomersUnicodeKeyTests.cs:16` and `:70`.**
   - **What is wrong:** the names do not reproduce the collision. Under VARCHAR, `राम पाटील` → `n:??? ?????`, `सीता जोशी` → `n:???? ????` and `गीता देशमुख` → `n:???? ??????` (checked on `.\SQLEXPRESS`). The shapes differ, so on the base the `Assert.Empty(sita.Tags)` / `Notes` and the "two pending merges" asserts would pass. The tests fail on the base only through the `N'...'` row-count and `LIKE '%?%'` checks. The comment at line 70 ("both keys read 'n:????? ?????' and the primary key refused it") is false.
   - **What right looks like:** add a pair with the same UTF-16 shape, for example `राम पाटील` and `शाम पाटील` (both `n:??? ?????` as VARCHAR; checked). Use that pair for the tag / note / preference test and the pending-merge test, so the shared-row defect itself would be caught on the base. Then correct the comment.

## Notes for the orchestrator (no lane change needed)

- N1 **Collation edge.** Key columns use the database default `Latin1_General_CI_AS` (the old version-80 Windows collation). Characters added to Unicode after that table have no weight there: for example `N'n:' + NCHAR(0x0979) = N'n:'` is TRUE. Two keys that differ only in such a character would compare equal in SQL (and collide in PK_crm_tag / PK_crm_pref), while C# compares them ordinally. This is very rare for Marathi / Hindi names. A future option is `COLLATE Latin1_General_100_CI_AS` (or `_BIN2`, since keys are already lower-cased) on the key columns; that would need matching collations on `bill.folio.payer_customer_key` and `meas.customer_preference.customer_key`.
- N2 **80-character cut (pre-existing).** `P(..., 80)` truncates longer keys silently, and the columns are 80. Two Devanagari names sharing their first 78 UTF-16 units, or two symbol-only `n:#<hex>` keys (hex doubles the UTF-8 byte count, so it passes 80 at 39 bytes), share one key. This was not introduced here. Consider hashing over-long keys in `CustomerRules.KeyOf` in a later lane.
- N3 **(e) `?` keys already stored.** The DONE file is right that they cannot be recovered. The original text is lost at write time, and NormName never emits `?` (punctuation becomes a space), so any `?` in a key column is a collapse marker. Can such rows exist today? On this PC `.\SQLEXPRESS` holds no HCC database (only HccTest_* and ETP databases). The legacy import has only been run as a dry run, which rolls back (`LegacyImportService.cs:35, 98`). So no production rows exist. A read-only detection query is still worth shipping with the installer / pre-migration check, or as a System-health line: `SELECT 'crm.customer_tag', COUNT(*) FROM crm.customer_tag WHERE customer_key LIKE N'%?%'`, and the same for the other ten columns. Expected result: 0.
- N4 The Python extractor's key for Devanagari legacy names, compared with the C# `NormName`, is not verified (also listed in the DONE file). A legacy name and a live name only match if both sides normalise the same way.
- N5 DONE claims match the branch: 10 files, LF on the new SQL / test files, CRLF kept in `SqlLegacyStore.cs`, shared files only in marked blocks, no SCHEMA / APK bump. Test counts were not re-run by the auditor.

## (f) Merge note: lane `per` (claude/cu-per, 1827238d)

`git -C E:\Projects\HCC\worktrees\cu-per diff c45bbce3..HEAD -- "08 Production/src"` adds **no new key-bound SQL parameter**:
- `SqlCustomerReadStore.GuestEmailsAsync` (cu-per `SqlCustomerStore.cs` about line 184) binds only `@p` (`SqlDbType.Int`) and reads `gst.guest.email`. Nothing to change.
- The new key paths (`KeyPassAsync`, `CustomerKeyTokenCache`, `CustomersAsync`, duplicates on heads) resolve keys in C# and reach SQL only through methods this lane already converted: `OverlayAsync` (`@k` NVarChar, line 42), the tag / note / pref / merge methods, `LegacyRollupAsync` (`@keys` NVarChar + `CAST AS NVARCHAR(80)`), and `LoadSomeAsync` (id lists, BIGINT; VARCHAR is correct there).

**Parameters that must become NVarChar when merging: none.** Both lanes edit `SqlCustomerStore.cs` in different hunks (per: the new method after `LoadSomeAsync`; nvarchar: lines 42 and 293-460), so a textual merge should be clean. After the merge, re-run `Hcc.Customers.Tests` once, including the per lane's `CustomerQueryEquivalenceTests` and these 5 tests together.
