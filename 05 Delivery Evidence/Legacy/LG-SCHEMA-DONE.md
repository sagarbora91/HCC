# LG-SCHEMA DONE

Lane lg-schema / Opus / branch `claude/lg-schema` / worktree `E:\Projects\HCC\worktrees\lg-schema` / base `ced9861e`.

**Contracts committed at cc2b8a62** (migrations 4600/4601, LegacyStayView, CustomerSummary/CustomerProfile additions, GuestDraftDto.Dob, GuestView.Dob/LegacyGuestId, ICustomerReadStore.LegacyStaysAsync/LegacyRollupAsync, `tests/Hcc.Legacy.Tests` project). Other lanes can merge `claude/lg-schema` now.

## Commits
- cc2b8a62 migrations 4600 + 4601, lock + slnx, Hcc.Legacy.Tests project + Sql schema tests, contracts and ports
- c2d1a621 Customers 360 legacy merge, guest DOB end to end, tests

## Files
- Migrations: `08 Production/database/migrations/4600_legacy_history.sql`, `4601_legacy_rbac_seed.sql`; `migrations.lock.json` (via `migrations-lock.mjs --add`); `Hcc.slnx` (2 migration lines + `tests/Hcc.Legacy.Tests`).
- Contracts: `Hcc.Contracts/Customers/CustomerContracts.cs` (LegacyStayView; CustomerSummary + `LegacyStays`, `FirstStay`; CustomerProfile + `LegacyStays`, `LegacyLifetimePaise` — appended with defaults so existing callers compile), `Hcc.Contracts/Stays/StaysContracts.cs` (GuestDraftDto `Dob`; GuestView `Dob`, `LegacyGuestId`).
- Application: `Customers/CustomerPorts.cs` (CrmLegacyStay + 2 reads), `Customers/CustomerQueries.cs` (merge), `Stays/StaysPorts.cs` (GuestRecord `LegacyGuestId`), `Stays/StaysCommands.cs` (Dob in ToDraft, IST today for validation, GuestView mapping).
- Domain: `Stays/GuestRules.cs` (GuestDraft `Dob`, `DobError`, Validate `today` param, Clean).
- Infrastructure: `Customers/SqlCustomerReadStore.Legacy.cs` (new partial), `Customers/SqlCustomerStore.cs` (one word: `partial`), `Stays/SqlStaysStore.cs` (dob insert/update/read, legacy_guest_id read).
- Prototype: `src/domain/types.ts` (`guests[]` `dob?`, `legacyGuestId?`; no SCHEMA bump), `src/features/guests/GuestForm.tsx` (date field "Date of birth (optional)", min 1900-01-01, max = simulated clock date; same component on desktop and phone).
- Tests: `tests/Hcc.Legacy.Tests/` (csproj, packages.lock.json, xunit.runner.json, `Sql/LegacyDb.cs` harness reusable by other lanes, `Sql/SchemaTests.cs`), `tests/Hcc.Customers.Tests/Sql/CustomersLegacySqlTests.cs`, `tests/Hcc.Stays.Tests/Sql/GuestDobSqlTests.cs`.

## What was built
- §2/§3: migration 4600 exactly as the contract (named constraints added; one extra index `IX_lgc_bill_stay` for the view join). §10: 4601 seeds PERM-LGC-001 (`is_money = 1`, Owner only).
- §9 server: `LegacyStaysAsync` (newest first, Duplicate hidden by the view), `LegacyRollupAsync` (keys via STRING_SPLIT on `|`). CustomerQueries: summary `Stays` live only, `LegacyStays` count, `Nights` + `LifetimePaise` include legacy (money null without PERM-CRM-004, like live), `FirstStay` = earlier arrival. Profile: `LegacyStays` (NetPaise masked as live), `LegacyLifetimePaise`, and profile `Nights` / `LifetimePaise` / `FirstStay` also include legacy (consistent with the summary); `AvgRatePaise`, `LastStay`, `Stays` stay live. Legacy rows on a merged-away key (crm.customer_alias) count for the merge target. Duplicates list summaries carry the rollup too.
- DOB: optional; must be yyyy-MM-dd, ≥ 1900-01-01, not after today (IST from the server clock); stored in `gst.guest.dob`; returned on GuestView only to readers with contact rights (PERM-GST-002), else null — my choice, contract silent. `LegacyGuestId` returned to all GuestView readers; no command writes it except the importer.

## Tests (through the lock, `dotnet test tests/<p> -maxcpucount:1`, output in `.scratch\`)
| Suite | Before | After |
|---|---|---|
| Hcc.Legacy.Tests (new) | 0 | 4/4 |
| Hcc.Customers.Tests | 28 | 30/30 |
| Hcc.Stays.Tests | 67 | 69/69 |
| Hcc.Platform.Tests (migration lock) | 70 | 70/70 |
| Hcc.Access.Tests (RBAC matrix) | 97 | 97/97 |
| Prototype `npx tsc --noEmit` / `npm test` | 1948 | 0 errors / 1948/1948 |
`dotnet build Hcc.slnx`: 0 warnings, 0 errors.

## Open items
- The prototype guest save path drops `dob` (`guestClean` in `src/domain/streams/guests.ts`, not my file) — see Integration needed; until then the form field is shown but not persisted in the mock.
- DOB masking for non-contact readers is a lane decision (contract silent) — confirm with the owner.
- An edit of a guest profile from a client that does not send `dob` clears it (same full-replace rule as the other fields).
- Legacy-only customers (invalid phones, no gst.guest row) do not appear in the Customers 360 list (the index is built from live guests/bookings) — as per contract.

## Integration needed
1. `03 Prototype/src/domain/streams/guests.ts`, `guestClean`: after `notes:t(d.notes),` add `...(t(d.dob)?{dob:t(d.dob)}:{}),`; in `guestValidate` add `if(t(d.dob)&&(!/^\d{4}-\d{2}-\d{2}$/.test(t(d.dob))||t(d.dob)<'1900-01-01'||t(d.dob)>s.clock.slice(0,10)))e.dob='Date of birth must be a date after 1900 and not in the future.';` (or without the clock bound if `s` is not in scope); add `dob:'Date of birth'` to `guestFieldLabels`. Also the production client's guest mapper should send/read `dob`.
2. `tests/Hcc.Access.Tests/Sql/RbacV2MatrixTests.cs` `LaterPermissions`: add `["PERM-LGC-001"] = ["A", "—", "—", "—", "—", "—", "—", "—", "—", "—"],` (currently not enforced, test passes without it).
3. lg-ui: role reads / prototype RBAC matrix (`tests/_role-reads.json`, `stream-rbac-matrix.mjs`) need PERM-LGC-001 for Owner if the Owner page checks it.
4. lg-import: reuse `Hcc.Legacy.Tests/Sql/LegacyDb.cs` for a migrated test database; `Fixtures\**` is already copied to the output.

## HANDOFF bullet
- **W1a LG lg-schema (1 Nov 2026):** migrations 4600 (lgc schema: import_run, guest_map, stay, room_segment, bill, receipt + `lgc.v_customer_stay`; `gst.guest.legacy_guest_id` + `dob` with UX_guest_legacy) and 4601 (PERM-LGC-001 Owner); Customers 360 shows legacy stays (summary/profile nights, lifetime, first stay include legacy; money masked as live); guest DOB end to end; new `tests/Hcc.Legacy.Tests`. Legacy 4, Customers 30, Stays 69, prototype 1948 green.
