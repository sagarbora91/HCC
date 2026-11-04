# af-crm DONE (PR-01, ID-01, Missed D)

Lane af-crm / Sonnet / branch claude/af-crm / worktree E:\Projects\HCC\worktrees\af-crm / base 6a29f3e6 + claude/auditfix (wave-0 runner) merged. Wave 2.

## Commits
See `git log --oneline 6a29f3e6..HEAD` (main work: a475afdd "CRM: opaque ck_ wire key, Unicode name normalisation and search"; then the DONE-file commit).

## Files
Server (08 Production): `src/Hcc.Domain/Customers/CustomerRules.cs` (NormName, KeyOf, Search only; `CustomerCreditKey` untouched), new `src/Hcc.Domain/Customers/CustomerKeyToken.cs`, `src/Hcc.Application/Customers/CustomerQueries.cs` (new internal `CustomerKeySpace`, outbound tokens, inbound resolution, `TokenAsync`), `CustomerCommands.cs` (inbound resolution + token in results), `CustomerPorts.cs` (`ICustomerReadStore.KeySecretAsync`), `src/Hcc.Infrastructure.SqlServer/Customers/SqlCustomerStore.cs` (one method), `src/Hcc.Contracts/Customers/CustomerContracts.cs` (doc comments only), `database/migrations/4900_crm_customer_key_secret.sql` + `database/migrations.lock.json` (`--add`) + `Hcc.slnx` (`<!-- AF-CRM -->` block). Tests: `tests/Hcc.Customers.Tests/**` (new Sql/CustomersKeyTests.cs, Kernel/CustomerNormNameTests.cs, Kernel/CustomerKeyTokenTests.cs; existing CustomersSqlTests / CustomersLegacySqlTests updated).
Prototype (03 Prototype): `src/domain/streams/customers.ts` (`normName`, `customerSearch` only), `tests/stream-customers.mjs` (3 `[AF-CRM]` cases).
Line endings kept per file (CustomerQueries, CustomerPorts, CustomerContracts are CRLF; everything else LF).

## What was built
- AF-DEC-4 Unicode normalisation (server + prototype): NFKC, lower case, letters / digits / combining marks of any script kept, other characters become one space. ASCII output identical (20-name table test). Empty result (symbol-only name) keys as `n:#<hex of UTF-8 raw name>` on the server. Search takes the name branch when the text has any letter (Devanagari works); digits branch unchanged.
- AF-DEC-3 opaque wire key: `ck_` + first 22 chars of base64url HMAC-SHA256(per-property secret, raw key) = 25 characters. Secret in `crm.customer_key_secret` (migration 4900), created on first use, race-safe. Stored keys (alias/tag/note/pref/merge, lgc rollups, gst.guest_credit_account) stay raw; only the wire changed.
- Inbound: `ck_` token resolves to the index entry (merged-away alias keys also resolve) else NOT_FOUND; raw `n:` key accepted for every role; raw digit key only for readers with PERM-CRM-002 (contact), otherwise NOT_FOUND. Used by `ProfileAsync` and every `CustomerCommands` entry that takes a key (propose merge source/target, tag add/remove, note, preferences, consent).
- Outbound key field paths (all tokens): `CustomerList.Customers[].Key`; `CustomerProfile.Key`; `CustomerProfile.MergedFrom[]`; `DuplicateList.Groups[].Customers[].Key`; `DuplicateList.Groups[].SuggestedSurvivor`; `MergeList.Merges[].SourceKey` / `.TargetKey`; command results `MergeView.SourceKey/TargetKey` (ProposeCustomerMerge, DecideCustomerMerge) and `CustomerChangeResult.CustomerKey` (tag, note, preferences, consent).
- Public helper `CustomerQueries.TokenAsync(propertyId, rawKey, ct)`.

## Findings closed
PR-01 (masked key = mobile digits), ID-01 / Missed D (Devanagari names shared one customer and search returned nothing).

## Tests
Commands (all through the lock): `dotnet build Hcc.slnx`; `dotnet test tests/Hcc.Customers.Tests -maxcpucount:1 --logger trx`; `tests/Hcc.Legacy.Tests`; `tests/Hcc.Integration.Tests`; `npx tsc --noEmit`; `npm test`; `npm run build`; `npm run build:production`.
- Hcc.Customers.Tests: 30 before, 69 after (+39: 8 SQL key tests, 23 NormName incl. the 20-name ASCII table, 8 token tests incl. theory rows), 0 failed. `CustomerRulesGoldenTests` (1,155 customers) unchanged and green. Hcc.Legacy.Tests 38/38. Hcc.Integration.Tests 22/22.
- `Masked_list_profile_and_duplicates_json_contain_no_phone_digits` passed for all three Cashier responses: no seeded phone and no 10-digit run in the list, the profile or the duplicates JSON. By default the Cashier has no merge permission (PERM-CRM-003), so the duplicates response is only reachable after the test database grants CSH that permission (test-only insert into `plat.role_grant`; no rule changed).
- Prototype: `npx tsc --noEmit` clean; `npm test` 2781/2781 (20 async) after vs 2778 before (+3 `[AF-CRM]` cases); `npm run build` and `npm run build:production` exit 0, no warnings in the logs.
- Existing tests changed because of the new contract: assertions comparing `Key` to a phone now use `TokenAsync`; the three tests that sent the Cashier a raw mobile key to `ProfileAsync` now send the token (a raw mobile key from a masked role is NOT_FOUND by design).

## Open items
- Re-run the LG legacy dry run on the hotel PC (legacy import computes keys with `KeyOf`; keys of non-ASCII names change).
- The production client still computes customer keys locally from the guests slice and fetches legacy stays with `GET /customers/profile?key=<raw mobile key>` (`clients/sync/runtime.ts:144`): contact roles keep working (raw digits accepted); masked roles never could see a phone there, and now get NOT_FOUND for a raw mobile key (they must use the token from the list). The client should switch to the tokens returned by `GET /customers` (not done: outside this lane's files).
- The prototype `customers.ts` line 51 (`'n:'+normName(...)`) has no `n:#hex` fallback for symbol-only names (outside the two functions this lane may edit); only the server does.
- Not touched on purpose (outside the brief): the audit log target `crm:<rawKey>` and the `CustomerMerged` outbox payload (`SourceKey/TargetKey`) still carry raw keys (phone digits). If those are shown to masked roles they leak the same number; owner/coordinator to decide whether to token or mask them.
- Q3 (DEC-243: Udhaar party key) left as is.

## Integration needed
None for composition. `ICustomerReadStore` gained `KeySecretAsync`; the only implementation is `SqlCustomerReadStore` (done). Any other lane's fake of that interface needs the same method.

## HANDOFF bullet
PR-01 and ID-01 closed (af-crm, claude/af-crm): customer keys leave the server as opaque `ck_` tokens (HMAC under `crm.customer_key_secret`, migration 4900), raw mobile keys only accepted from contact roles; names normalise with Unicode (Devanagari customers are separate and searchable) on server and prototype. Re-run the LG dry run on the hotel PC; client should adopt the list tokens.
