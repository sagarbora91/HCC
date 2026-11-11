# cu-per AUDIT (wave CU, lane per, auditor A1 Opus)

Branch `claude/cu-per`, audited head 1827238d, base c45bbce3. Read: CU-CONTRACT §1, §2, §3.6; af2-per01-DONE.md; PER-01-measurement.md; cu-per-DONE.md; cu-per-timing.md; full `git diff c45bbce3..HEAD`.

## Verdict: PASS WITH NOTES

No defect that breaks a guest link, leaks across customers or properties, or can break the wave gate. The notes below are low severity and can be done in a later pass.

## What was checked and holds

- **Cache is a pure function of (secret, key set).** `Entry.ByToken` is `HMAC(secret, raw)` for exactly the keys it was built from, and it is used only when the property's current secret is byte-equal and the request's key-set fingerprint matches. So:
  - a new customer changes the count and sums, the entry is rebuilt and the genuine token resolves (tested: `Token_cache_sees_a_new_customer_after_a_refusal...`);
  - a rotated secret rebuilds from nothing (tested by SQL UPDATE);
  - an approved merge leaves the key set unchanged (the source moves from heads to `Aliases.Keys`), so the map stays correct even without `Invalidate`;
  - a deleted, expired or renamed key changes the set and forces a rebuild.
- **No false positive leak.** Even a stale hit can only return a raw key of the same property under the same secret. Every inbound caller (`CustomerCommands.Find` and `CustomerQueries.ProfileAsync`) then re-checks it against the live heads and raises NOT_FOUND. Properties are isolated by the dictionary key plus the per-property secret.
- **Cross-database tests.** Two test databases with the same property id either have different secrets, which forces a rebuild, or produce an identical map. The cache cannot give a wrong answer across tests. The counters are process-global, but `xunit.runner.json` has `parallelizeTestCollections: false` and the class is `internal`, so only this assembly sees it. No risk to the gate.
- **Thread safety.** Entries are immutable once published under `Gate`, and `LastUsed`/`_tick` are written under the lock. Concurrent builds are last-writer-wins, which is harmless. No torn reads.
- **Rebuilds.** All inbound callers pass the same set (heads + aliases), so callers do not thrash the cache. A forged token never triggers a rebuild. A rebuild with the same secret re-hashes only the new keys. The per-request cost that remains is the O(key chars) fingerprint, which is cheaper than the head pass the request already does.
- **Timing.** The dictionary lookup (randomised Marvin hashing) leaks less than the old per-key `string.Equals` loop did. The secret comparison has no attacker input.
- **(b) Commands.** Permission checks still run first and are unchanged. Errors for unknown keys, keys from another property, merged aliases (resolved through `Resolve`) and pending merges (`InsertMergeAsync` null gives INVALID_TRANSITION) are unchanged. The "same customer" validation is still raised before any read. Consent `GuestIds` comes from `CustomersAsync`, which the new head/full test pins as equal. In the old `DecideMerge`, the whole-property read only fed `ForAsync` raw keys, and nothing in the decision ever resolves inbound, so it validated nothing that is now lost.
- **(c) Duplicates.**
  - `CustomerHeadIndex.Emails` reproduces `Index`'s `guest?.Email ?? first non-empty profile e-mail` exactly. This includes the `''` case (SQL `email IS NOT NULL` keeps `''`, and `own` returns it just as `??` does) and the mobile fallback profile.
  - `NormEmail`/`NormName` are applied by one shared `Duplicates<T>` core.
  - The legacy rollup is computed per key, so restricting it to group members gives the same values.
  - The SQL is parameterised (`@p` INT) and property-scoped, and it touches no customer-key column. The parallel `nvarchar` lane does not affect it.
- **(e)** Scope matches §3.6, with no migrations and none of the shared files. `git diff --stat` equals `--ignore-cr-at-eol` (10 files, +516/−36). No existing test was weakened.

## Notes

1. **`CustomerKeyTokenCache.cs` lines 84 and 127–147: the key-set identity is a non-cryptographic FNV-1a sum.**
   - **What is wrong.** If a key is renamed to a string with the same FNV-1a-64 hash, and the count stays the same, the stale entry stays in use. That customer's genuine token is then refused (NOT_FOUND) until the next key-set change: a false negative. Such strings can be computed (about 2^32 work), and staff control the names. The impact is one broken link, not a leak (see above). The class comment "Correct by construction" overstates this.
   - **What right looks like.** An exact check at the same O(N) cost: `keys.Count == old.Count && keys.All(old.ByRaw.ContainsKey)`, or the distinct count plus membership. Failing that, seed the FNV offset with a per-process random value.
2. **`CustomerKeyTokenCache.cs` line 19: the memory bound is loose.**
   - **What is wrong.** 16 × 250,000 keys at about 220 B/key (25-char token, the retained raw key, two dictionary entries) is up to about 0.9 GB. A real single-hotel install with about 15k keys uses about 3–4 MB. The cache also keeps raw keys (mobile numbers) and a copy of the secret in static memory for the life of the process; before this lane they lived for one request.
   - **What right looks like.** A global key budget (for example 250k across all properties) or `MaxProperties` of about 4, and one line in the class comment about the retained secret and PII.
3. **`CustomerCommands.cs` lines 76–78 and 206: missing keys give a 500.**
   - **What is wrong.** `both[sourceHead.Key]` and `.Customers[head.Key]` throw `KeyNotFoundException` (a 500) if a guest's phone or name changes between `HeadsAsync` and `LoadSomeAsync`, because the rebuilt key then differs. `ListAsync` and `ProfileAsync` already had this race from PER-01; this lane extends it to two writes. The same applies to `byKey[k]` in `DuplicatesAsync`, which now uses three reads with no common snapshot.
   - **What right looks like.** `TryGetValue`, then NOT_FOUND (or CONFLICT "customer changed, try again").
4. **`cu-per-timing.md` and `Perf/CustomerQueryBenchmark.cs` lines 96–99: the "before" rows are reconstructions, not pre-lane measurements.**
   - **What is wrong.**
     - The "key lookup ×2 (before)" row calls `ResolveInbound`, which now goes through the warm cache. Before this lane, each lookup did up to N HMACs.
     - The "Duplicates (before)" row leaves out the policy, key-secret, legacy-rollup and summary work that the "after" row includes.
     - Both effects understate the gain, so the conclusion stands. The DONE file does say "Debug build".
     - The benchmark doc comment promises a "forged ck_ token profile lookup" row that does not exist.
   - **What right looks like.** Label the "before" rows as reconstructions that run through the new cache, and add or remove the forged-token row.
5. **`CustomerQueryEquivalenceTests.cs`: test coverage.**
   - **What is wrong.**
     - Every seeded e-mail is lower-case `gN@example.test`. There is no `''`, padded, upper-case or gmail dot/+tag variant. Equivalence holds by construction, since both paths feed the same raw e-mail into the same `NormEmail` and `Head_emails_equal...` asserts the raw values are equal, but no test pins the `''` branch.
     - The duplicate equivalence test compares old with new, so on old code it passes trivially. That is expected for an equivalence test.
     - The two cache tests would fail on a naive token→key cache without the secret or key-set check, which is good.
     - No new test compares error codes of the five commands old against new; that rests on the existing command tests.
   - **What right looks like.** Add one guest with `email = ''` and one with `' Asha.Rao+x@GMail.com '` to the seed.
6. **`SqlCustomerStore.cs` line 187: `GuestEmailsAsync` reads `email`, which is not in `IX_guest_crm_head` (property_id, guest_id INCLUDE full_name, phone).**
   - **What is wrong.** On a single property the query is a clustered scan. That is acceptable, and indexes are out of scope for this lane.
   - **What right looks like.** If it is ever needed, an `INCLUDE (email)` in a later migration (lane `nvarchar` owns migrations).

## Tests re-run by the auditor

`dotnet test tests/Hcc.Customers.Tests -maxcpucount:1 --filter CustomerQueryEquivalenceTests` (through the lock): **9 passed, 0 failed, 0 skipped** (1 m 18 s, Debug), matching the DONE claim for this filter. The full project count (122) was not re-run; it is taken from the DONE file.
