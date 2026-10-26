# A08 — Architecture and test quality

Agent A08, Opus, 26 Oct 2026. Branch `claude/audit-fable-2026-10-26` at `c0c477d`. I only read the code. I wrote only to this file, to `evidence\A08\` and to `probes\A08_*`.
**Environment:** Node v25.9.0 with `E:\Tools\nodejs` first on PATH (`evidence\A08\node_version.txt`). I did **not** run `dotnet test`: A9 held `locks\dotnet-test.lock` the whole time and none of my findings needed a test run. My only SQL work was a bounded probe on `.\SQLEXPRESS`. It created and dropped `HccTest_A08outbox_<stamp>` four times. A check afterwards showed no `HccTest_A08%` databases left. I touched no other database. I did not run `DBCC DROPCLEANBUFFERS` or anything else that affects the whole instance.

**Probes** (run them from the repo root):
- `probes\A08_sweeps.mjs` → `evidence\A08\sweeps.txt` and `sweeps.json`: project references, layering, which tables each store writes, how migrations use DDL, DML and FKs, which transaction each write runs in, and `ExpectedVersion` and rowversion coverage.
- `probes\A08_test_sample.mjs` → `test_sample.txt`: the 40 sampled test bodies.
- `probes\A08_test_asserts.mjs` → `test_asserts.txt`: the assertion count for all 828 test methods.
- `probes\A08_golden_diff.mjs` → `golden_regen_diff.txt`: the repo's own golden exporters, copied unchanged into the scratch folder and run against the current prototype.
- `probes\A08_dup_logic.mjs` → `dup_logic.txt`: prototype rules compared with the server rules.
- `probes\A08_outbox_poll.ps1` + `.sql` → `outbox_poll_{100000,365000}.txt`.
- `probes\A08_dead_code.mjs` → `dead_code.txt`.
- Git history: `migration_commit_counts.txt` and `migration_edit_history.txt`.

---

## (a) Layering

| Check | Result | Evidence |
|---|---|---|
| Project references go only downward: Contracts ← Domain ← Application ← Infrastructure.SqlServer ← Operations ← Api | **Holds.** Contracts has no references. Domain → Contracts. Application → Domain. Infrastructure → Application, plus the `Microsoft.Data.SqlClient` package. Operations → Infrastructure. Api → Infrastructure and Operations. All 28 test projects reference Api and Infrastructure (Operations.Tests references Api and Operations). | `sweeps.txt` "Project references"; `src/*/*.csproj` |
| Forbidden `using` directives in Contracts, Domain or Application (Infrastructure, Api, SqlClient, `System.Data`, AspNetCore, `System.IO`, `HttpClient`) | **None** | `sweeps.txt` "Layering" (empty) |
| Wall clock or environment used directly in Contracts, Domain or Application (`DateTime.Now/UtcNow`, `DateTimeOffset.UtcNow`, `Environment.GetEnvironmentVariable`) | **None.** Every service uses an injected `TimeProvider`. | same sweep |
| Wall clock in the infrastructure SQL | Low. `inv.group_block.released_utc = SYSUTCDATETIME()` (`SqlInventoryStore.cs:350`). Role validity uses `SYSUTCDATETIME()` (`Access/SqlAccessAdminQueries.cs:30`). Both bypass `TimeProvider`, so tests with a simulated clock and the as-of block history use different clocks. | grep in `evidence` log of this session; finding A08-18 |
| Endpoints call SQL classes directly | **No.** Every `MapPost` delegates to an Application service. `Endpoints/*.cs` also holds DI registration, which is acceptable for a composition root. | grep `Map(Post|Get).*Sql` = 0 |
| Cosmetic issue | `Hcc.Api.csproj:8-9` references `Hcc.Operations` twice | |

**Verdict:** no layering violations. The module boundaries (b) are where the rules are bent.

## (b) Cross-module write table

How this was built: the creating migration defines who owns each table. For 0001/0002, ownership follows the schema rule in the contracts §4 and WAVE1 ownership table: inv → P2, bill/pay → P4, plat → P0. Every `INSERT/UPDATE/DELETE/MERGE` in `src/Hcc.Infrastructure.SqlServer`, `Api` and `Operations` was matched to the module folder of its file. The sweep found 408 write statements, 35 of them cross-module. All rows are in `sweeps.txt` "Write table". The cross-module rows:

| Store file (module) | Writes | Owned by | Lines | Verdict |
|---|---|---|---|---|
| `Stays/SqlStayChangeStore.cs` (P3) | `inv.reservation_night`, `inv.reservation`, `inv.room` | P2 | 96-109, 127, 144, 156 | **Cross-module.** W3-7 reprices nights and moves the reservation's room/type directly instead of going through a P2 port. Only the room nights go through `IInventoryAllocator`. |
| `Stays/SqlStaysStore.cs` (P3) | `inv.reservation` (status), `inv.room` (hk_status) | P2 | 176, 246 | Cross-module (check-in/out lifecycle; Wave 1 design) |
| `Housekeeping/SqlHousekeepingStore.cs` (P3) | `inv.room.hk_status` | P2 | 106 | Cross-module. P3 owns readiness in practice, but the column sits on a P2 table. |
| `Maintenance/SqlMaintenanceStore.cs` (W2) | `ops.hk_task` (assignee, status, times) | P3 | 473, 481, 489 | **Cross-module.** Maintenance drives housekeeping repair tasks directly. |
| `Documents/SqlDocumentStore.cs` (W3-3) | `gst.guest.id_type/id_last4` | P3 | 150 | Cross-module (ID profile on the guest) |
| `Closing/SqlClosingStore.cs` (P6) | `inv.property.business_date` | P2 | 224 | Cross-module. Closing owns the business date by design (DEC-065), but the column is on a P2 table. |
| `Yield/SqlRatePublicationPort.cs` (W2) | `inv.room_type_tariff`, `inv.stay_restriction` | P2 | 28-29, 51-70 | Cross-module through a named port. The adapter lives in the Yield folder, not in P2's. |
| `Import/SqlImportStore.cs` (W4-13) | `inv.room_type`, `inv.room`, `inv.type_night_count`, `plat.app_user`, `plat.user_role` | P2, P1 | 294-364 | **Cross-module.** The import writes users and roles without `SqlUserAdministration`'s rules. It is gated by PERM-IMP-003 (Owner only, `2701_import_rbac_seed.sql:8`) and refuses the OWN role (`SqlImportStore.cs:344`), but the separation-of-duties logic is now in two places. |
| `Setup/SqlPropertySetup.cs` (W3-b) | `inv.property`, `plat.app_user/user_role/credential`, `plat.idempotency` | P2, P1, P0 | 81-88, 137 | Cross-module by necessity (first-time setup). It keeps its own idempotency record (principal 0), outside the runner. |
| `Access/SqlSessionService.cs:30`, `Access/SqlUserAdministration.cs:490`, `Inventory/SqlInventoryStore.cs:394`, `Payroll/SqlPayrollStore.cs:715` | `plat.audit_event` | P0 | — | Direct audit inserts outside `ICommandTransaction.AuditAsync`: sign-in/out, owner bootstrap, hold-expiry sweep, bank-file download audit. They are legitimate non-command audits, but they do not count towards the runner's "audit mandatory" check. |
| `Migrations/MigrationRunner.cs:214` | `dbo.schema_migration` | journal | — | Expected |

The migrations sweep (`sweeps.txt` "Cross-module DDL/DML") found:
- One cross-module DDL: `1900_property_setup.sql:6` `ALTER TABLE inv.property ADD …` (Setup on a P2 table).
- 20 rbac seeds inserting `plat.permission` / `plat.role_grant`. This is the sanctioned "module PERMs in `NNNN_<module>_rbac_seed.sql`" rule.
- `0601_guest_id_documents.sql` creates `gst.guest_id_secret` in P3's `gst` schema from block 06.

**Verdict:** the WAVE1 rule says to use another module's tables only if they are yours, and otherwise to define an interface and stub it. Most of the writes above sit behind a port interface, but the SQL lives in the consuming module's folder and writes the foreign table directly. Finding A08-07.

## (c) Migration discipline

| Topic | Result | Evidence |
|---|---|---|
| Count | **46** migrations, not 47 as the plan says. `Hcc.slnx` lists only 44: it is missing `2202_groups_company.sql` and `2702_import_pos.sql`. They are still embedded through the csproj wildcard (`Hcc.Infrastructure.SqlServer.csproj:15`), so they are still applied. | `ls database/migrations`; diff against slnx |
| Block numbering vs ownership | Every file is in its module's block: 01 Access, 02 Inventory/Reservations (0202 W3-8, 0203 W4 G5), 03 Stays (0301 W3-7), 04 Billing, 05 Closing, 06 Tax/GuestIds, 07–27 one per module. No block 26 yet (RBAC v2 is still pending). | `sweeps.txt` "Migration GO" |
| 0001/0002 never edited | **True.** One commit each (`7fb901f`). `git log --follow` shows no later change. | `migration_commit_counts.txt` |
| Other edited migrations | 0100, 0101, 0200, 0300 and 0400 have only *renames* (moved into `database/migrations` at `92a6c6d`; 0200 had a two-line header comment change at `bbc8aee`). 0900 has a CHECK rewrite (`fb1b92b` 23:57 → `6015493` 00:11). 2400 had its immutability triggers changed from AFTER to INSTEAD OF (`3f842ab` → `02ecf87`). **All edits were inside the agent's own branch before merge.** None came after merge into integration or main, and no production database exists, so the checksum rule held in practice. | `migration_edit_history.txt` |
| Drift detection | Only at runtime, against a live database (`MigrationRunner.cs:188-194`). There is **no committed checksum manifest** and no test that compares current checksums with a released list, so an edit to an applied migration would first show up on the hotel PC as a refused upgrade. | grep "checksum" in tests/deployment |
| GO / transaction discipline | The runner splits on `GO` lines (`SqlValues.cs:52-64`) and runs every batch of a migration in one transaction with `XACT_ABORT ON` (`SqlConnectionFactory.cs:25-26`, `MigrationRunner.cs:208-226`). No migration contains a non-transactional statement (ALTER DATABASE, FULLTEXT, BACKUP, DBCC). Explicit `BEGIN TRAN` appears only inside the two procedures in 0200/0202, which follow `XACT_ABORT` + `CATCH → ROLLBACK; THROW` (`0200:199-227`, `0202:93-119`). | `sweeps.txt` "Migration GO" |
| FK direction | 341 FK references in total, 165 of them cross-module. **All 165 point from a later block to an earlier one.** No earlier module references a later one. All FKs are trusted (the platform test asserts this). | `sweeps.txt` "Cross-module FKs" |
| Platform test | `tests/Hcc.Platform.Tests/Sql/MigrationRunnerTests.cs:39` `Fresh_database_applies_every_module_block_in_order_and_cross_module_foreign_keys_resolve` | |
| What that test does not cover | (1) The upgrade path. In-block migrations added after later blocks are applied *out of numeric order* on an existing database (`MigrationRunner.cs:197` applies any unapplied version), while a fresh install applies them in order. Only the fresh order is tested. (2) The FK list it checks is the 12 Wave-1 FKs (lines 59-65). The 153 Wave 2–4 cross-module FKs are only covered by "all trusted". (3) Block ownership: it would accept a module writing in another module's block. (4) Its order asserts at lines 47-49 are tautological. `EmbeddedMigrationSource.Validate` already sorts (`MigrationRunner.cs:51`), so `Applied.Order().Distinct() == Applied` cannot fail. (5) The frozen test was relaxed at `865e76a` (lead, documented in HANDOFF Session 12); the earlier "≥ 0700 after Wave 1" guard was removed. (6) No checksum-manifest check. | finding A08-05 |

## (d) Command pipeline

| Topic | Result | Evidence |
|---|---|---|
| Runner | `SqlCommandRunner.RunAsync` runs in one READ COMMITTED transaction. A transaction-owned app lock covers `idem:{property}:{principal}:{operation}:{key}`. Then the stored record is checked (replay or `IDEMPOTENCY_MISMATCH`), then the handler runs. The audit count must be > 0, otherwise it throws and rolls back. The idempotency row is inserted and the transaction commits. | `SqlCommandRunner.cs:76-145` |
| **Idempotency scope** | **Per (property, principal, operation, key).** The same key from another user or another operation is a new command. That is tested in `CommandRunnerTests.cs:125` `Key_is_scoped_to_principal_and_operation`. Records are purged after 30 days (`SqlRetentionPurge.cs:23`); a replay after that runs again. | |
| Audit mandatory | Enforced at `SqlCommandRunner.cs:118`. The test `CommandRunnerTests.cs:105` `Command_without_audit_is_refused_and_rolled_back` **does exercise the real runner**: `_db.Platform().Commands` = `new SqlCommandRunner(Connections)` (`SqlServerRegistration.cs:18`) on a disposable database, and it checks that 0 rows remain. Not covered: audits written by direct `INSERT plat.audit_event` (see (b)) do not count, and no test checks that every handler branch audits. A no-op branch without an audit would turn into a 500. | |
| Outbox in the same transaction | **Yes.** `PublishAsync` inserts `plat.outbox` on the runner's connection and transaction (`SqlCommandRunner.cs:53-65`). `plat.outbox` has no other writer. | `sweeps.txt` |
| Bypasses | The only writes outside the runner are: sign-in/out and session validation (`SqlSessionService.cs:68, 273, 288`); `BootstrapOwnerAsync` (`SqlUserAdministration.cs:472`, own transaction + audit, no idempotency); `VerifyCurrentSecretAsync` (751); first-time setup (`SqlPropertySetup.cs:58`, own transaction + own idempotency row); the hold-expiry sweep (`SqlInventoryStore.cs:189-235`, own transaction per hold, audit with operation `Sweep`, **no outbox event**, errors swallowed at 381-395); outbox consumers and projectors (`OutboxConsumer.cs:88`, `AccountingVoucherProjector.cs:252` cash-difference sweep); retention purge; the yield seed on read (`SqlYieldStore.cs:58`: a GET writes default plans); and read-only RepeatableRead snapshots (Billing 425/454/541, POS 612, Messaging 418). None of these is a `/commands/*` route. **No `/commands/*` handler writes without the runner.** Every `/commands/*` registration I traced delegates to a service that returns `CommandResult<T>`. That covers the 110 direct `MapPost("/commands/" + X.OpY …)` lines plus the per-file helper registrations: `Command<TReq,TRes>(g, "Name", …)` in Billing and the `"/commands/" + op` helpers in GuestServices and Access. The exact route total is A5's table. The only `CommandResult` built outside the runner is a deliberate post-runner decoration that keeps secrets out of the idempotency store: guest-ID reveal, ID image view/reveal, the deposit-link token, the device token, and the import dry-run rollback (`GuestIdService.cs:134`, `DocumentService.cs:180,192`, `WebBookingService.cs:222`, `SqlUserAdministration.cs:359`, `ImportService.cs:198`). | `sweeps.txt` "Writes outside a runner transaction" (heuristic; every "unknown" row was hand-checked as a runner lambda or a store helper bound to the runner transaction) |
| `expectedVersion` | 230 command-shaped records in Contracts. 102 carry `ExpectedVersion`; 128 do not. Most of those 128 create rows, append rows, are reads, or are status transitions checked under `UPDLOCK` (an acceptable design). **Commands that overwrite existing state with neither a version nor a status guard (last writer wins):** SetLedgerMapping, ResetLedgerMapping, SetStatutoryRates, SetLeavePolicy, SaveDepartment, SetRoster, CopyRosterWeek, MarkAttendance, SetMenuItemAvailability, SetStockSettings, SetChannelStopSell, SetCustomerConsent, SetMessagingConsent, SetYieldOverride, SetYieldForecast, SetGstSetting, WithdrawGstSetting, SetCompanyLimit. The money-relevant ones are ledger mapping, statutory rates, GST setting and yield override. They have history tables (`acc.mapping_change`, `hr.rate_change`, `tax.gst_setting_version`), so a lost update is visible afterwards but not prevented. Where an update on a rowversion table is not compared inside the statement, the handler compares the version after an `UPDLOCK` read (spot-checked: stay changes `StayChangeCommands.cs:200-207`, reservations, user admin). | `sweeps.txt` "Command-like records", "UPDATEs on rowversion tables"; finding A08-08 |

## (e) Concurrency

| Topic | Status | Evidence |
|---|---|---|
| **W3-7 allocator-release deadlock** | **HANDOFF Session 12 has no note about it**, and neither do the commit messages or the code (grep for "deadlock" and "allocator" across HANDOFF, commit messages and code). Only the audit prompt mentions it. My static analysis: `StayChangeCommands.ReplanAsync` (`:246-260`) calls the allocator once per night run. The allocator's release path deletes *all* of the reservation's room nights (`SqlInventoryAllocator.cs:73-78`), so any shrink or move releases every run and then re-adds the wanted runs. Each call locks `inv.type_night_count` in its own ascending pass (`:68-70`). Across calls, the documented global order "type asc, night asc for all touched keys" (`0200_inventory_reservations.sql:3-4`, `SqlInventoryAllocator.cs:10-13`) is therefore broken. **Scenario:** two clerks at the same time move one guest Deluxe→Classic and another Classic→Deluxe, with overlapping nights. Each releases its old type, then waits on the other's type: SQL 1205. The victim gets `UNAVAILABLE` "The database was busy" (`SqlValues.cs:87`). Everything rolls back and the clerk retries; there is no data harm. There is **no concurrency test for stay changes**: `StayChangeSqlTests.cs` has 19 sequential tests, and `ConcurrencySqlTests.cs` covers only checkout and projectors. A latent risk: a future caller that releases one run of a multi-run reservation would delete every night but decrement counts for one run, and the counts would drift. Today only `ReplanAsync` does this and it releases every run. Not reproduced at runtime. | finding A08-06 |
| Room-row locks in a move | `MoveRoomAsync` locks the target room, then the current room (`StayChangeCommands.cs:55-57`). Two swaps in opposite directions would deadlock, but each target is occupied and would be refused anyway. Harmless. | |
| UPDLOCK ordering, stock | Item codes are trimmed, de-duplicated and sorted ordinally before locking (`StockCommands.cs:514-523`). Laundry and count lines use the same ordinal order (`:256`, `:382`). Consistent. Edge case: the ordinal de-dup lets "abc" and "ABC" through as two lines, and under a case-insensitive collation both hit the same row; same transaction, so no deadlock. | |
| Invoice number lock | `sp_getapplock` per (property, FY, doc type), `Exclusive`/`Transaction`, then `UPDLOCK, HOLDLOCK` read and single-row advance or first insert (`SqlInvoiceStore.cs:102-131`). Gap-free inside the transaction. Immutability triggers on the series (`2400_invoices.sql`). **Sound.** | |
| Drawer / shift | `UPDLOCK` on the till row for every cash step. The refund remainder is checked under `UPDLOCK, HOLDLOCK` on the parent receipt (`SqlBillingStore.cs:16-20`). Covered by parallel-receipt tests (`BillingCommandTests.cs:158`). **Sound.** | |
| Projector polling | Four `BackgroundService` workers each poll every 2 s (`AccountingEndpoints.cs:65`, `HousekeepingEndpoints.cs:68` with two consumers, `MessagingEndpoints.cs:91`, `OtaEndpoints.cs:87`), so five consumers in all, plus `RetentionWorker`. Each poll is `SELECT TOP 100 … WHERE event_type IN (SELECT value FROM OPENJSON(@types)) AND NOT EXISTS (dedup) ORDER BY outbox_id` (`OutboxConsumer.cs:51-58`). There is no index on `event_type`, no per-consumer watermark, and **no outbox or dedup purge**. **Measured** with the 0001 DDL in a throwaway database: at 100,000 events the accounting poll took **1.3 s CPU**; at 365,000 events (about 1–2 years at 500–1,000 events/day) it took **5.5–6.1 s CPU / 9.8–10.2 s elapsed** per poll. The housekeeping poll (one type) took 1.5 s CPU. The same query with the types put once into a keyed table variable took 0.6 s at 365k. Workers wait 2 s *after* each poll, so these polls run almost back-to-back. Most of a SQL Express core would be busy with polling after a year. The instance was busy while I measured (A1 and A9 tests plus an ETP session), so absolute timings may be high, but the cost grows linearly. | `outbox_poll_100000.txt`, `outbox_poll_365000.txt`; finding A08-01 |
| Projector failures and restart | There is no watermark: after a restart each consumer recomputes "pending" from the dedup table and catches up, exactly once per consumer. **A poison event blocks its consumer forever.** `ConsumeOnceAsync` aborts the batch on the first failing effect (`OutboxConsumer.cs:64-68`). The same oldest event comes first on the next poll. `attempts` is incremented only on success (`:106`, in the transaction that rolls back), and `last_error` is never written (grep). Example: `AccountingVoucherProjector.Parse` throws on an empty or odd payload (`:81-82`), after which every later accounting voucher stops, with only a warning log every 2 s. | finding A08-02 |
| Events that change availability but do not reach OTA | The OTA projector marks ARI dirty only on ReservationCreated/Amended/Cancelled/NoShow, PushQueued and YieldTariffsPublished (`OtaChannelManagerProjector.cs:20-24`). StayExtended, StayShortened (incl. early departure) and StayRoomMoved (`StayChangeCommands.cs:83,124,184`), the hold-expiry sweep (no event at all), room or maintenance blocks and group-block release do not. | finding A08-03 |
| Isolation | There is no READ_COMMITTED_SNAPSHOT or SNAPSHOT anywhere, so readers and writers block each other. Report reads use short RepeatableRead transactions (`SqlBillingStore.cs:425`). Note: the comment there says "serializable" but the code uses RepeatableRead. | grep |

## (f) Test quality

**Sample:** 40 tests from 10 projects, 4 each, chosen to cover both domain and SQL tests (bodies in `test_sample.txt`). Classes:
- **B** = asserts independent expected values: hand-worked numbers, prototype goldens, or rules.
- **M** = mirror: the expected value is computed by the code under test or by a re-implementation of it, or is trivially true.
- **S** = smoke: it only checks that something runs, or one shallow assertion.

| # | Project / test (file:line) | Class | Note |
|---|---|---|---|
| 1 | Billing `Domain/BillingDomainTests.cs:13` Expected_cash_parts… | B | 8,700 from the DEC-052 decision pack |
| 2 | Billing `BillingDomainTests.cs:120` Tenders_require_a_reference… | B | rules |
| 3 | Billing `Sql/BillingCommandTests.cs:26` Room_night_posts_once… | B | SQL, replay, mismatch, forbidden; pinned 10 % (A08-04) |
| 4 | Billing `BillingCommandTests.cs:277` Refund_is_requested_approved… | B | SoD + remainder + drawer |
| 5 | Invoices `Domain/InvoiceRulesTests.cs:29` Golden_AC006… | B | 4,265 / 132.50 + 132.50 |
| 6 | Invoices `InvoiceRulesTests.cs:73` Numbering_is_16_characters… | B | |
| 7 | Invoices `InvoiceRulesTests.cs:161` Rounding_never_credits_more_tax… | B | worked paisa example |
| 8 | Invoices `Sql/InvoiceSqlTests.cs:160` Issued_documents… immutable | B | DB triggers 52400–52405 |
| 9 | Payroll `Domain/PayrollRulesTests.cs:59` F07_contribution_period… | B | constants (payroll golden regenerates identically) |
| 10 | Payroll `PayrollRulesTests.cs:109` Overtime_at_the_ordinary_rate… | B with one **M** assert | line 204-205 re-implements `PayMath.MulDiv` with BigInteger and compares |
| 11 | Payroll `PayrollRulesTests.cs:204` F06_default_effective_date… | B | |
| 12 | Payroll `Sql/PayrollSqlTests.cs:91` F02_bank_file… F09_unlock… | B | strong: freeze at lock, audit without the number, re-issue, unlock |
| 13 | Closing `Sql/CloseJourneyTests.cs:166` …4265_for_room_105 | B | seeds charges by raw SQL, then reads the snapshot |
| 14 | Closing `Sql/ConcurrencySnapshotCashBookTests.cs:81` Snapshot_is_immutable… | B | includes a tamper alarm |
| 15 | Closing `Sql/ResumeAndBarrierTests.cs:114` A_write_in_flight… | B | uses `Task.Delay(1500)` as a timing assertion, so possibly flaky on a loaded PC |
| 16 | Closing `Sql/CashBookViewTests.cs:64` Cash_book_is_for_finance_roles_only | **S** | one 403 + `Assert.Single` |
| 17 | Accounting `Domain/AccountingRulesTests.cs:73` Zero_sources… | B | |
| 18 | Accounting `AccountingRulesTests.cs:105` Reconciliation… | B | |
| 19 | Accounting `Golden/GoldenAccountingTests.cs:135` Full_day_flow… | B | accounting golden regenerates identically |
| 20 | Accounting `Sql/AccountingFlowTests.cs:27` Folio_day_becomes_balanced_vouchers… | B | strong end to end; pinned 10 % |
| 21 | Groups `Kernel/GroupRulesGoldenTests.cs:63` Exposure_HCC1044_is_430000 | B | |
| 22 | Groups `GroupRulesGoldenTests.cs:122` Member_override… | B | |
| 23 | Groups `GroupRulesGoldenTests.cs:160` Attrition_GB1… | B | |
| 24 | Groups `Sql/GroupsSqlTests.cs:55` Pickup_billing_routes… idempotent | B | |
| 25 | GstIds `Gst/GstRulesGoldenTests.cs:20` Slab_boundary_7500… | B | |
| 26 | GstIds `GstRulesGoldenTests.cs:120` Per_line_rounding… CGST_takes_the_odd_paisa | B | plus property-style invariants |
| 27 | GstIds `GstRulesGoldenTests.cs:181` Room_105_journey_is_4265 | B | |
| 28 | GstIds `Gst/GstSqlTests.cs:159` Posted_lines_keep_their_snapshot… | B | |
| 29 | Pos `Domain/PosDomainTests.cs:12` Fixture_check_F01… | B | |
| 30 | Pos `PosDomainTests.cs:62` Bill_numbers… | B | |
| 31 | Pos `Sql/PosCommandTests.cs:95` Stale_check_version_is_refused | B | |
| 32 | Pos `PosCommandTests.cs:217` A_manager_can_reject_a_void… | B | |
| 33 | Yield `Domain/YieldRulesGoldenTests.cs:52` Saturday_classic… | B | |
| 34 | Yield `YieldRulesGoldenTests.cs:88` Floor_clamp… | B | |
| 35 | Yield `YieldRulesGoldenTests.cs:183` Formatting… | B | |
| 36 | Yield `Sql/YieldSqlTests.cs:105` Owner_publish_writes_dated_tariffs… | B | crosses into P2 quotes |
| 37 | Stock `Kernel/StockRulesTests.cs:16` Bath_towel_alert_suggests_65 | B | |
| 38 | Stock `StockRulesTests.cs:89` Validation_helpers | B | |
| 39 | Stock `Sql/StockSqlTests.cs:73` Same_idempotency_key_replays… | B | |
| 40 | Stock `Sql/StockSqlTests.cs:185` Loss_needs_a_reason… | B | |

**Ratio:** 38 behaviour, 1 smoke (#16) and 1 behaviour test containing one mirror assertion (#10). That is **95 % B / 2.5 % M / 2.5 % S.** Outside the sample I found two mirror tests:
- `MigrationRunnerTests.cs:47-49`: tautological order asserts.
- `Billing/BillingDomainTests.cs:83-93` `Negative_half_paisa_tax_differs_from_the_prototype_Math_round`: it re-implements JS `Math.round` inside the test and documents a prototype difference that no longer exists (A08-11).

The mechanical sweep counted 828 test methods (962 runs with Theory cases) and found exactly 1 without an assertion call: `Access.Tests/Unit/CredentialRuleTests.cs:30` `Valid_pins`. It is valid by design, because "no exception" is the assertion (`test_asserts.txt`).

**Projects without SQL integration tests:** only `Hcc.Operations.Tests` (deployment-script dry runs, by design). All 27 others create disposable `HccTest_*` databases. `Hcc.Pos.Tests` does too, but its 26 SQL tests have no `[Trait("Category","Sql")]`, so the README's no-database filter `--filter "Category!=Sql"` still runs them (A08-12).

**Golden JSON provenance** (`golden_regen_diff.txt`; each repo exporter was copied unchanged into the scratch folder and run against the current `03 Prototype`, and the repo was not written):

| Golden file | git history | Regenerated from the current prototype |
|---|---|---|
| `Hcc.Accounting.Tests/Golden/accounting-golden.json` | 1 commit (`a4dcb00`) | **identical** |
| `Hcc.Payroll.Tests/Golden/payroll-base.golden.json` | 1 commit (`6015493`) | **identical** |
| `Hcc.Stays.Tests/Golden/stays-golden.json` | 1 commit (`e97b2d5`) | **differs:** room taxBps 1000 vs now 500; journey receipt ₹4,400 vs ₹4,200; unpaid-departure ₹200 vs ₹100 |
| `Hcc.Messaging.Tests/Golden/messaging-golden.json` | 1 commit (`ae70e6b`) | **differs:** "payment of ₹2,200 … Balance due ₹200" vs ₹2,100 / ₹100 |
| `Hcc.Customers.Tests/Golden/customers-golden.json` | 1 commit (`812a3f6`) | **differs:** guests array has 10 entries, regenerated has 1,156 |
| `Hcc.Platform.Tests/Golden/base-fixture.golden.json` | 1 commit (`7fb901f`) | **exporter crashes** on the current prototype (`export-golden.mjs:38`, `j.approvals.at(-1)` is undefined) |
| `Hcc.Inventory.Tests/Golden/inventory-base.golden.json` | 2 commits in the P2 branch | **exporter crashes** (`taxRateFor is not a function`: removed when the DEC-069 GST master arrived) |

No golden was edited after its commit. The two that regenerate identically were clearly machine-generated. The other five are frozen snapshots of a pre-DEC-069 prototype. The tests still pass because the module harnesses pin `new FixedGstRateResolver()` at 10 % (`Billing/Harness/BillingHarness.cs:174`, `Accounting/Harness/AccountingDb.cs:121`, `Stays/Harness/P3Database.cs`, `Inventory/Harness/InventoryDatabase.cs`, `Ota/Harness/OtaFixture.cs`, `GuestServices/Harness/GsWorld.cs`). The DEC-069 rates are tested separately in `Hcc.GstIds.Tests` and `Hcc.Integration.Tests` (₹4,200). (A08-04)

## (g) Dead code and duplicated logic

**Dead code** (`dead_code.txt`): 1,702 public types; 36 are not referenced elsewhere in `src`. 28 of those are static `*Endpoints` extension classes (false positives). The real candidates are:
- Test doubles shipped in production assemblies: `FixedGstRateResolver` (`Hcc.Application/Tax/TaxPorts.cs:78`) and `InMemoryDocumentImageStore` (`Hcc.Application/Documents/DocumentPorts.cs:150`). If DI were wired wrongly, the 10 % resolver could be picked in production.
- Unused types: `HandoverDesk` and `HandoverShift` (`Hcc.Contracts/Handover/HandoverContracts.cs:5,12`) and `FilingFacts` (`Hcc.Domain/Compliance/ComplianceRules.cs:49`).
- Used only by tests: `RateAdjustments` (`Hcc.Domain/Pricing/RateAdjustments.cs:48`) and `GstPosting` (`Hcc.Domain/Tax/GstMaster.cs:324`).
- Write-only schema: `plat.outbox.last_error` is never written, and `IX_outbox_unpublished` is never used by the poll query.
(A08-13)

**Duplicated logic, prototype `src/domain` vs server `Hcc.Domain`** (`dup_logic.txt`). The prototype side is the real `gst.ts` (imported with Node type stripping) plus formulas copied verbatim with file:line. The server side is a line-for-line port of `MoneyMath.cs:14-22`, `GstMaster.cs:230-237`, `YieldEngine.cs:25-28,54` and `OtaRules.cs:52`.

| Rule | Range checked | Differences |
|---|---|---|
| GST line tax (`gst.ts:74-77` vs `MoneyMath.TaxOf`) | amounts −₹2,000…+₹2,000 in 1-paisa steps × 0/5/12/18/28 % (2,000,005 pairs) | **0.** The prototype now rounds half away from zero too. |
| CGST/SGST split (`gst.ts:81` vs `GstMaster.cs:233`) | tax −5,000…5,000 paise | **0.** CGST takes the odd paisa in both. |
| Yield round-to-unit (`yield.ts:101` vs `YieldEngine.cs:28`) and rate × (1 + bps) (yield plans `yield.ts:119,139`; OTA markup `channels.ts:69` vs `OtaRules.cs:52`) | ₹0–₹20,000 in 50-paisa steps × units ₹1/₹10/₹50/₹100; 9 bps values × 285k rates | **0** |
| **Yield forecast occupancy %** (`yield.ts:89` `Math.round(sold/sellable*100)` floating point vs `YieldEngine.cs:54` integer half-away) | sold ≤ sellable ≤ 200 | **8 pairs differ**, e.g. 23/40 rooms: prototype 57 %, server 58 %; 29/200: 14 % vs 15 %. The server is arithmetically right. An occupancy rule set exactly at 58 % would apply on the server and not in the prototype (A08-10). |
| Availability | not compared numerically | The models differ: the prototype uses `available()` = raw minus holds with `withHoldExempt`; the server uses `inv.type_night_count` sold + held ≤ sellable with block context. Parity is covered only by the inventory golden, whose exporter no longer runs (A08-04). |

## (h) Findings

| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A08-01 | High | Performance / projectors | 08 Production/src/Hcc.Infrastructure.SqlServer/Outbox/OutboxConsumer.cs:51-58; database/migrations/0001_platform.sql:31-48; Access/SqlRetentionPurge.cs:23-27 | probes/A08_outbox_poll.ps1 → evidence/A08/outbox_poll_365000.txt: accounting poll 5.5–6.1 s CPU, 9.8–10.2 s elapsed at 365k events; 1.3 s at 100k; 0.6 s with the types in a table variable. Five consumers poll every 2 s; no event_type index, no watermark, no outbox/dedup purge. | After months of use the background projectors keep SQL Express busy almost all the time. Front-desk commands and reports slow down and vouchers, HK tasks and messages lag. | Add a per-consumer watermark (last contiguous outbox_id) or an index on (event_type, outbox_id) and pass the types as a table. Archive or purge delivered events and dedup rows after N days, linked to the backup policy. Add a 1-year-volume perf test. | 6 | High (mechanism) / Medium (timeline: event volume assumed 500–1,000/day) |
| A08-02 | Medium | Projectors / reliability | Outbox/OutboxConsumer.cs:64-68, :106; Accounting/AccountingVoucherProjector.cs:81-82 | Static: the first failing effect aborts the batch, the same event comes first next tick, attempts only counts successes, last_error is never written (grep) | One bad event silently stops all later accounting vouchers (or HK tasks, messages, OTA) for that consumer. Only a warning log shows it. | Record attempts and last_error in a separate transaction. After N failures, park the event (dead-letter) and continue. Show stuck events in the accounting day exceptions and in health. | 4 | High |
| A08-03 | Medium | Inventory → OTA | Ota/OtaChannelManagerProjector.cs:20-24; Application/Stays/StayChangeCommands.cs:83,124,184; Inventory/SqlInventoryStore.cs:189-235 | Static: extend, shorten, early departure, room move, hold expiry, room/maintenance blocks and block release do not mark ARI dirty; the hold sweep publishes no event at all | Once a real channel manager is connected, channel availability goes stale after these changes, risking overselling or lost sales. The channel is simulated today. | Publish one `InventoryChanged` event from the allocator path (or from each of these commands) and include it in the OTA consumer. Publish from the sweep too. | 3 | High |
| A08-04 | Medium | Test quality / golden figures | tests/Hcc.Stays.Tests/Golden/stays-golden.json; tests/Hcc.Messaging.Tests/Golden/messaging-golden.json; tools/golden/export-golden.mjs:38; tests/Hcc.Inventory.Tests/Golden/export-inventory-golden.mjs:61; tests/Hcc.Billing.Tests/Harness/BillingHarness.cs:174 | probes/A08_golden_diff.mjs → evidence/A08/golden_regen_diff.txt: 2 of 7 goldens identical, 3 differ (10 % vs 5 % room GST, ₹4,400 vs ₹4,200), 2 exporters crash | "Server figures = prototype figures" is now proven only for accounting and payroll. Module SQL tests run on a pinned 10 % rate, and prototype changes are no longer cross-checked. | Fix the two exporters and regenerate all goldens under the DEC-069 master, or relabel the pinned-10 % tests as kernel tests. Add a script that regenerates and diffs every golden (run with the prototype tests). | 6 | High |
| A08-05 | Medium | Migrations | src/Hcc.Infrastructure.SqlServer/Migrations/MigrationRunner.cs:197; tests/Hcc.Platform.Tests/Sql/MigrationRunnerTests.cs:46-49 | Static plus git (865e76a). The upgrade order differs from the fresh-install order for in-block migrations; the order asserts are tautological; there is no checksum manifest. | After go-live, an in-block migration (e.g. 0302) runs after 2702 on the hotel database but before 0400 on a fresh install, which can produce different schemas. An edit to an applied script shows up only as a refused upgrade on site. | Commit `migrations.lock.json` (version → SHA-256) with a test that released entries never change and new versions are above the last release. Test the upgrade from the previous release's set. | 4 | High |
| A08-06 | Low | Concurrency (W3-7) | src/Hcc.Application/Stays/StayChangeCommands.cs:246-260; Infrastructure/Inventory/SqlInventoryAllocator.cs:68-78 | Static: several allocator passes in one transaction break the "type asc, night asc" lock order; there is no stay-change concurrency test. Not reproduced. | Two opposite cross-type moves at once: one clerk gets "database busy, try again". Fully rolled back, no data harm. A future single-run release would make the counts drift. | Plan all runs in one allocator call (one sorted delta set), or retry 1205 once in the runner. Add a two-way move concurrency test. | 4 | Medium |
| A08-07 | Low | Module boundaries | see table (b): Stays/SqlStayChangeStore.cs:96-156, Maintenance/SqlMaintenanceStore.cs:473-489, Import/SqlImportStore.cs:294-364, Yield/SqlRatePublicationPort.cs:28-70, Documents/SqlDocumentStore.cs:150, Closing/SqlClosingStore.cs:224 | evidence/A08/sweeps.txt "CROSS" rows (35 statements) | Owners cannot change their tables safely. Access SoD rules are duplicated in the import. This breaks WAVE1 "no cross-module table writes". | Either list these ports as sanctioned in the ownership table, or move each SQL adapter into the owning module's folder behind its port. | 2 (doc) / 8 (move) | High |
| A08-08 | Low | Optimistic concurrency | Contracts: SetLedgerMappingRequest, SetStatutoryRatesRequest, SetGstSettingRequest, SetYieldOverrideRequest … (sweeps.txt "Command-like records") | 128 of 230 command records have no ExpectedVersion; 18 overwrite state without a version or status guard | Two editors: the last save wins silently. History rows exist for mapping, rates and GST. | Add ExpectedVersion at least to ledger mapping, statutory rates, GST setting, yield override and company limit | 4 | Medium |
| A08-09 | Low | Command pipeline | Infrastructure/Inventory/SqlInventoryStore.cs:189-235, :381-395 | Static: the hold-expiry sweep runs before every reservation command, outside the runner (own transaction, audit operation 'Sweep', no outbox) and swallows SqlException/HccException | Expired holds release rooms without an event (A08-03). A real fault would be hidden and retried before every booking command. | Run it from a background worker, publish an event, and log swallowed errors | 2 | High |
| A08-10 | Low | Prototype vs server | 03 Prototype/src/domain/streams/yield.ts:89 vs 08 Production/src/Hcc.Domain/Yield/YieldEngine.cs:54 | probes/A08_dup_logic.mjs: 8 (sold, sellable) pairs differ, e.g. 23/40 → 57 % vs 58 % | A yield occupancy rule at the boundary applies on the server but not in the prototype preview | Prototype: `Math.round(sold*100/sellable)` (integer first). Server is right. | 0.5 | High |
| A08-11 | Low | Test quality | tests/Hcc.Billing.Tests/Domain/BillingDomainTests.cs:77-93 | It re-implements JS Math.round in the test. The prototype `gst.ts:74-77` now rounds half away (0 of 2,000,005 pairs differ). | A stale statement that the prototype and server disagree on negative half-paisa tax. It does not test the prototype. | Delete it, or retarget it to a prototype-parity check | 0.5 | High |
| A08-12 | Low | Test hygiene | tests/Hcc.Pos.Tests/Sql/*.cs (26 tests) | grep: no `[Trait("Category","Sql")]` | The README's no-database run (`--filter "Category!=Sql"`) fails on a PC without SQL | Add the trait | 0.25 | High |
| A08-13 | Low | Dead code | Hcc.Application/Tax/TaxPorts.cs:78; Hcc.Application/Documents/DocumentPorts.cs:150; Hcc.Contracts/Handover/HandoverContracts.cs:5,12; Hcc.Domain/Compliance/ComplianceRules.cs:49 | evidence/A08/dead_code.txt | Test doubles (a fixed 10 % GST resolver, an in-memory ID image store) ship in production assemblies. Unused types add noise. | Move the doubles to a shared test project and delete the unused types | 1 | High |
| A08-14 | Info | Layering | src/*/*.csproj; Hcc.Api.csproj:8-9 | sweeps.txt | Clean: references go downward only, no forbidden usings, clock always from TimeProvider. Duplicate Operations reference (cosmetic). | Remove the duplicate line | 0.1 | High |
| A08-15 | Info | Command pipeline | Commands/SqlCommandRunner.cs:76-145; tests/Hcc.Platform.Tests/Sql/CommandRunnerTests.cs:105-125 | read + test | Confirmed sound. Idempotency is per property + principal + operation + key under an app lock. The audit is mandatory and that is tested on the real runner. The outbox is in the same transaction. No `/commands/*` route bypasses the runner. | — | 0 | High |
| A08-16 | Info | Migrations | database/migrations/*; evidence/A08/migration_edit_history.txt | git log --follow | 0001/0002 never edited; other edits were all pre-merge. All 165 cross-module FKs point later → earlier. One cross-module DDL (1900 → inv.property). 46 migrations, of which slnx lists 44. | Add 2202 and 2702 to Hcc.slnx | 0.1 | High |
| A08-17 | Info | Concurrency | Invoices/SqlInvoiceStore.cs:102-131; Billing/SqlBillingStore.cs:16-20; Application/Stock/StockCommands.cs:514-523 | read | Invoice numbering (app lock + UPDLOCK, gap-free), till/refund locks and stock ordinal lock order are sound. There is no RCSI, so readers block writers. | Consider READ_COMMITTED_SNAPSHOT before go-live (needs a test run) | 2 | High |
| A08-18 | Low | Time source | Inventory/SqlInventoryStore.cs:350; Access/SqlAccessAdminQueries.cs:30 | grep SYSUTCDATETIME | Block release time and role-validity checks use the SQL clock instead of TimeProvider, so as-of history and tests with a simulated clock can disagree | Pass `@now` from TimeProvider | 0.5 | High |

## (i) Closing lists

### Claims I could not confirm
- **"HANDOFF.md Session 12 note on the W3-7 allocator release deadlock."** No such note exists in HANDOFF.md (Sessions 12/12b), in the commit messages (`2090896`, `3de9864`) or in the code. Only the audit prompt and plan mention it. My analysis in (e)/A08-06 is my own static reading, not reproduced at runtime.
- Plan §0: "47 migrations". The repo has 46.
- Plan A8 brief: "projector workers polling every 2 s … with three workers". There are four projector workers (five consumers) plus the retention worker.
- The absolute poll timings came from an instance shared with A1/A9 test runs and an ETP session. Only the linear growth and the roughly 10× gain from materialising the types are firm. The event volume per day (500–1,000) is my estimate, not measured.
- I did not check exhaustively whether every command handler audits on every branch (a no-op branch without an audit would return 500).
- Golden history: git shows no edits after commit, but I cannot prove a file was not hand-adjusted *before* its first commit. Regeneration only proves accounting and payroll.

### Open owner/CA decisions I hit (and the code that depends on each)
- **Outbox retention period:** how long delivered events and dedup rows must be kept (audit, accounting traceability, backups). Blocks the A08-01 purge (`SqlRetentionPurge.cs`, `0001_platform.sql` outbox/consumer_dedup).
- **Is a real OTA channel manager in scope for go-live?** If yes, A08-03 becomes High (`OtaChannelManagerProjector.cs`).
- **Golden reference:** should module tests run on the DEC-069 master (5 %/18 %) or stay pinned to the old 10 %? Affects `FixedGstRateResolver` in 6 harnesses and the stays/messaging goldens. The CA still has to confirm the illustrative rates.
- **Migration policy after go-live:** are in-block numbers (e.g. 0302) still allowed once the hotel database exists? Affects `MigrationRunner.cs:197` and `MigrationRunnerTests.cs:46-49`.

### Questions for the lead
1. Should the cross-module writes through ports (A08-07) be accepted as sanctioned and written into `WAVE1_PARALLEL_RULES.md`, or filed as refactors?
2. A08-01 severity depends on event volume. Does A9's performance work have event counts per day from the FY26 history? If volume is under 200/day, Medium is fair.
3. Do you want a runtime reproduction of A08-06? It needs the test lock and a temporary test outside the product tree (two concurrent opposite cross-type moves).
4. Is the frozen-file edit at `865e76a` (MigrationRunnerTests) acceptable as documented, or should the report list it as a deviation from WAVE1 "Frozen"?
