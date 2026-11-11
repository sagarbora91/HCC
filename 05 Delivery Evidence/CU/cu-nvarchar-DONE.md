# cu-nvarchar DONE (wave CU, lane nvarchar)

Branch `claude/cu-nvarchar`, base c45bbce3. Not merged, not pushed to main.

## What changed
Migrations (new, LF, all re-runnable: each step checks column type / object first; dependent PK / unique / check / index dropped and recreated under the same names and definitions):
- `08 Production/database/migrations/4930_crm_merge_alias_key_unicode.sql` - `crm.customer_merge.source_key/target_key`, `crm.customer_alias.alias_key/target_key` to NVARCHAR(80); UX_crm_merge_pending, CK_crm_merge_distinct, PK_crm_alias, CK_crm_alias_distinct, IX_crm_alias_target.
- `4931_crm_tag_note_pref_key_unicode.sql` - `crm.customer_tag/note/pref.customer_key`; PK_crm_tag, IX_crm_note_customer, PK_crm_pref.
- `4932_lgc_customer_key_unicode.sql` - `lgc.guest_map/stay/bill/receipt.customer_key` (same CustomerRules key, joined in `lgc.v_customer_stay`); the four IX_lgc_*_customer indexes; `sp_refreshview lgc.v_customer_stay`.
- 4933 / 4934 not used (nothing left to convert).

C# (key parameters now Unicode):
- `src/Hcc.Infrastructure.SqlServer/Customers/SqlCustomerStore.cs` (all `@k/@s/@t` key parameters VarChar -> NVarChar; status / id lists untouched) - LF.
- `Customers/SqlCustomerReadStore.Legacy.cs` (`@k` NVarChar; `CAST(value AS NVARCHAR(80))` in the rollup join) - LF.
- `Legacy/SqlLegacyStore.cs` (`AddTagAsync` `@k` NVarChar; plus a second catch in `BulkAsync`: for NVARCHAR columns SqlBulkCopy reports an over-long value as `InvalidOperationException("... would be truncated ...")` instead of SQL 8152, so it is mapped to the same VALIDATION error; this kept the existing test `A_database_length_error_becomes_a_validation_naming_the_table` green without changing it) - CRLF kept.

Shared files (appended inside marked block): `08 Production/Hcc.slnx` (`<!-- CU-NVARCHAR -->` block with the three migrations), `08 Production/database/migrations.lock.json` (via `node tools/migrations-lock.mjs --add`).

Tests: `tests/Hcc.Customers.Tests/Sql/CustomersUnicodeKeyTests.cs` (new, 5 tests): different Devanagari names give different keys and do not share tags / notes / preference (stored key is real Devanagari, no `?`); separate pending merges and aliases per Devanagari source, duplicate pending for the same source still refused; ASCII phone key and name key still resolve; after migration all 11 key columns are nvarchar(80) and every index / PK / filtered unique / check exists with the right columns; **downgrades the schema to the old VARCHAR definitions, inserts rows, runs the real 4930-4932 files, checks rows kept, runs them a second time (no change), then inserts a Devanagari key**.

## Tests run (all through Invoke-Serialized.ps1)
- `dotnet test tests/Hcc.Customers.Tests -maxcpucount:1`: Passed 121, Failed 0 (116 existing + 5 new).
- `dotnet test tests/Hcc.Legacy.Tests -maxcpucount:1`: first run 37/38 (the truncation test above), after the BulkAsync fix Passed 38, Failed 0.
- `dotnet test tests/Hcc.Masters.Tests -maxcpucount:1` (checks Hcc.slnx lists every migration): Passed 36, Failed 0.
- `dotnet test tests/Hcc.Platform.Tests -maxcpucount:1` (migration lock test): see PLATFORM below.
- Line endings: `git diff --stat` equals `--ignore-cr-at-eol` for all edited files.

## Not verified
- Full server suite, `dotnet test Hcc.slnx` (rule 5). Other test projects that touch `lgc` / `crm` keys (Integration, Counter) not run.
- Run against a production-size database (timing of ALTER COLUMN over a large `crm`/`lgc` set); the ALTER takes a schema lock per table. Legacy import with real Devanagari names end to end (the CSV extractor encoding) not checked.
- Keys already stored with `?` before this migration (written by the old VARCHAR column) cannot be recovered; such tags / notes stay attached to the collapsed key.

## Defaults used
- `gst.guest_credit_account.customer_key` (VARCHAR(40)) left as is: it holds `CustomerRules.CreditKey` (phone last 10 digits, else guest id), never a name, so it is always ASCII. `bill.folio.payer_customer_key` was already NVARCHAR(80). `meas.customer_preference` already NVARCHAR(80).
- `lgc.*` converted as well because it holds the same name-derived key and is joined to `crm` keys in Customers 360.
- Collation unchanged (database default); new columns keep it.

## Open questions
- None blocking. The auditor may want a one-off check on the live database for keys that already contain `?`.

## Shared files touched
`08 Production/Hcc.slnx`, `08 Production/database/migrations.lock.json`. No SCHEMA / APK bump needed.
