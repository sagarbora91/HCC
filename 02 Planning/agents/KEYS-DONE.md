# KEYS-DONE: room keys module (branch `claude/keys`)

Date: 2 Nov 2026. Worktree `E:\Projects\HCC\worktrees\keys` (from `main` 649e4982). Contract: `E:\Projects\HCC\02 Planning\HCC_KeyAgent_Spec_v1.md`. Not merged, not pushed. Original BIS folder untouched.

## 1. What was built

### Server (`E:\Projects\HCC\worktrees\keys\08 Production`)
| Part | Files |
|---|---|
| Migrations | `database\migrations\4720_keys_schema.sql` (tables `ops.key_lock`, `ops.key_encoder`, `ops.key_job`, `ops.key_card`; append-only triggers; seed of `key_lock` from existing rooms), `4721_keys_rbac_seed.sql` (PERM-KEY-001…004). Both in `migrations.lock.json` and `Hcc.slnx`. Numbered 4720+ because the LF wave reserves 4670-4719. |
| Domain | `src\Hcc.Domain\Keys\KeyRules.cs` (events, reasons, states, result codes, 2-minute queue, 30-second heartbeat, BIS door list, card expiry 12:00, mismatch warning, state machine) |
| Contracts | `src\Hcc.Contracts\Keys\KeysContracts.cs` |
| Application | `src\Hcc.Application\Keys\` `KeysPorts.cs`, `KeyCommands.cs` (RequestKey, CancelKey, ReadKey, RecordManualKey, SaveKeyLock, RegisterKeyEncoder, check-out hook), `KeyQueriesAndAgent.cs` (panel, job, log, locks, encoder; `KeyAgentService` for the agent contract) |
| SQL | `src\Hcc.Infrastructure.SqlServer\Keys\SqlKeyStore.cs`, `KeyDepartureProjector.cs` (outbox consumer of `StayDeparted`) |
| API | `src\Hcc.Api\Endpoints\KeysEndpoints.cs` + two lines in `HccComposition.cs` |
| Tests | new project `tests\Hcc.Keys.Tests` (36 tests); grid rows for PERM-KEY-001…004 added in `tests\Hcc.Access.Tests\Sql\RbacV2MatrixTests.cs` |

Endpoints (spec section 4 plus staff side). Staff, session: `POST /api/v1/commands/{RequestKey|CancelKey|ReadKey|RecordManualKey|SaveKeyLock|RegisterKeyEncoder}`; `GET /api/v1/keys/{panel/{reservationId}|job/{id}|log|locks|encoder}`. Agent, bearer token of one encoder: `GET /keys/jobs?encoder=&wait=` (long poll, 200 or 204), `POST /keys/jobs/{id}/state?encoder=`, `POST /keys/jobs/{id}/result?encoder=`, `POST /keys/encoders/{id}/heartbeat`.

Behaviour:
* Job lifecycle `queued -> picked -> waiting_card -> done / failed / expired`; atomic claim (READPAST), one job at a time per encoder, oldest first, queue time 2 minutes, a picked job the agent abandoned fails with `UiTimeout` after 2 more minutes, a repeated result changes nothing (idempotent jobId), another encoder's job is refused.
* Requests that need no agent are written as failed jobs at once so the log and the manual button work: room without a lock (`NoLockRoom`), encoder offline (heartbeat older than 30 s or none: `EncoderOffline`). "Key issued manually" (`RecordManualKey`) writes a done job of kind `manual` plus a card of status `manual`.
* Check-out hook: the `StayDeparted` outbox event queues a software cancel only when the room has an active card; every failure is swallowed (`CancelForDepartureAsync` never throws); offline leaves a failed cancel job (= the reconciliation task). **No check-in hook was added**: key issue is an explicit staff action from the booking panel, so check-in cannot be blocked by keys and the core `CheckIn` command was not touched.
* Mismatch warning: card expiry is the last day 12:00 (BIS Speed Check-in), the folio check-out time is read from the dated `checkout` setting (default 11:00); a difference is returned as `warning` on the job and the panel.
* Locks: seed from existing rooms; rooms added later get their default row on first use or when the Manager opens the mapping; 106 and every room without a BIS door (101-107, 201-214, 301-314) have `has_lock = 0`; editable by Manager / Owner with a version check.
* Permissions: PERM-KEY-001 read, 002 issue, 003 cancel = Owner, Manager, Reception; 004 configure (lock mapping, register encoder) = Owner, Manager. No separate key operator login. Housekeeping and Accountant: nothing.

### Event types, reasons and counts (coordinator addition)
* Columns `event_type` (`issued`, `reissued`, `extended`, `reactivated`, `cancelled`, `read`) and `reason` (`extension`, `lost`, `damaged`, `other`) on **both** `key_job` and `key_card` (migration 4720). A database CHECK makes the reason mandatory for `reissued`, `extended`, `reactivated`; the application validates it first. Triggers make both tables append-only (event, reason, room, requester, time and card validity can never change; no deletes).
* Counts are never stored: `CountsAsync` groups the successful (`done`, including manual) jobs by event for a booking or a room. Who, when and why are on each row. The panel line is "Issued n, Re-issued n, Reactivated n" where Reactivated = extended + reactivated (the raw split is in the API and the Key log totals).
* Key log report: `GET /keys/log` filters by date, room, booking, user and event, with totals and row count; prototype page "Room keys > Key log".

### Prototype (`E:\Projects\HCC\worktrees\keys\03 Prototype`)
* Domain `src\domain\streams\keys.ts`, handlers `src\services\mock\handlers\keys.ts` (`KEY_REQUEST`, `KEY_CANCEL`, `KEY_READ`, `KEY_MANUAL`, `KEY_LOCK_SAVE`, `KEY_ENCODER`, `KEY_SIM`, `KEY_AGENT_STEP`, `KEY_CLOCK`), slice `s.keysX` (optional, no SCHEMA bump), the simulated encoder = the keyagent MockEncoder semantics.
* UI `src\features\keys\`: **key panel** (button "Room key" in the booking dialog; encoder badge, counts line, active cards, Issue key / Re-issue / Extend / Reactivate / Lost card / Cancel key / Read card, reason and valid-until fields, mismatch warning, offline and no-lock notes, "Key issued manually" fallback, the booking's key log); page **Room keys** with tabs Key log (filters + totals + print), Lock mapping (Manager / Owner), Encoder (badge, demo agent controls). Styles `src\styles\m-keys.css` (tokens only, no page scroll: the tables scroll inside their cards; text is never shrunk). English labels with Hinglish hints in button titles and search keywords (neighbouring screens carry no Hinglish labels). Nav "Room keys" under FRONT DESK, navmap entries added, RBAC documentation rows added in `tests\stream-rbac-matrix.mjs`.
* One shared-file edit: `src\services\mock\commands.ts` gains the import and one line before the audit push (`if CHECKOUT: try keyAfterCheckout catch {}`), so check-out never waits for keys.
* Prototype clock note: the simulated business clock ticks one minute per command, so key time-outs (2-minute queue, 30-second heartbeat) are measured in "key minutes" that only the demo command `KEY_CLOCK` advances.

### Key agent prototype (`E:\Projects\HCC\keyagent`)
`IKeyEncoder.ExtendKey` added (mock implements it with a switch; `Btlock57UiEncoder` returns `NotSupported` with the "UNMAPPED, re-issue instead" text), `src\ServerPoller.cs` (minimal HTTP poller: heartbeat, long poll, state, run job on the encoder, result) and `--poll BASEURL ENCODERID TOKEN [--mock]` in `Program.cs`; two new self-test checks.

## 2. Tests

| Suite | Result |
|---|---|
| Prototype `npm test` | **2190 / 2190** (baseline 2176 + 14 `[KEY]` tests); `npx tsc --noEmit` clean |
| Server `Hcc.Keys.Tests` | **36 / 36** (6 rule tests + theory cases, 17 SQL tests with the fake agent, 1 HTTP end-to-end) |
| Server full suite `dotnet test Hcc.slnx -maxcpucount:1` | 37 projects, 3733 tests (baseline 3697 + 36 new): 3730 passed on the first run; the 2 failures caused by this work were fixed (role-read fixture `03 Prototype\tests\_role-reads.json` regenerated, only PERM-KEY-001…004 added; `OperatorAlertsTests` now lists `KeyDepartureWorker`) and re-run green (Integration 19/19, Operations 52/52); **3732 / 3733 expected**, the one other failure is below |
| keyagent `--selftest` | ALL PASSED (17 checks incl. the two new extend checks; mock only) |
| Prototype `npm run build` | clean (`dist\index.html`) |

Known unrelated failure: `Hcc.Accounting.Tests TallyFileSqlTests.Masters_and_vouchers_files_are_logged...` fails in this worktree (expects 2 masters rows, finds 3) because the other test in the same class (`Without_a_legal_name_the_company_is_the_property_name`) also logs a masters file and xUnit ran it first in this path; in the `acc3` worktree the same two tests pass. It is an order-dependent test in ACC-3 code (shared class fixture, no keys involvement); fix it in the test (count only the FY of the first export) before the next gate.

Covered: every job state, offline fallback and manual record, room 106 and a room without a BIS door (and mapping it), 2-minute expiry and abandoned-pick `UiTimeout`, one job at a time, idempotent request and result, other-encoder and wrong-token refusal, permissions (Manager all, Reception no mapping, Housekeeping and Accountant none), mismatch warning with and without the 12:00 folio setting, check-out hook (queues, replays once, offline leaves a failed job, never throws), event and reason validation (also the database CHECK and append-only triggers), counts per booking and room (Issued 1, Re-issued 2, Reactivated 1), Key log filters and totals, the panel over real HTTP.

## 3. Migration numbers
4720 `keys_schema`, 4721 `keys_rbac_seed`. LF keeps 4670-4719. Add the numbers to HANDOFF when merging.

## 4. Proposed DEC entries
* **DEC-KEY-1** Key events are an append-only log (`key_job`, `key_card`) with event type and reason; counts per booking and per room are derived, never stored; Reactivated in the panel = extended + reactivated.
* **DEC-KEY-2** Manager and Owner hold every key permission; Reception issues, re-issues, cancels and reads but cannot map locks; no key operator login. Lock mapping and encoder registration are Manager / Owner (PERM-KEY-004).
* **DEC-KEY-3** Rooms without a BIS door and room 106 have `has_lock = 0`; the Manager edits the mapping in "Room keys > Lock mapping".
* **DEC-KEY-4** Check-out is a software cancel through the outbox and never depends on the card; an offline encoder leaves a failed cancel job as the reconciliation task. Key issue is a staff action from the booking, not a check-in hook.
* **DEC-KEY-5** The card expiry (last day 12:00, BIS Speed Check-in) is compared with the folio check-out time; a difference is a visible warning, not a block.
* **DEC-KEY-6** Extend / reactivate is `NotSupported` on the real adapter until mapped at the front desk; staff re-issue a card instead.

## 5. UNTESTED against real hardware (everything below is designed, not run)
* The real `Btlock57UiEncoder`: issue (keyboard-driven Speed Check-in), cancel, read, extend. The BIS app could not be started on the dev PC (BTReg57.DLL), so no control, caption or key sequence was run. Extend and reactivate through the BIS UI are UNMAPPED.
* `ServerPoller` against a real HCC server and a real encoder (the server side is tested with a fake agent in process and over HTTP); no tray host, DPAPI token storage, back-off or auto-start.
* Card writes, the physical lock accepting a card, new guest SN invalidating the old card, 12:00 expiry behaviour at the door, `vendor_row_seen` reconciliation (columns exist, the nightly job is not built), PC clock skew report.
* Real folio check-out timing on a live property (the setting is read from the dated `checkout` setting; the test sets it by SQL).
* The migration seed of `key_lock` for rooms that already exist at migration time (the tests migrate an empty database; the lazy default path is tested).
* Phone layout: the page and panel were not given a dedicated phone page; they were not exercised in an emulator or the browser pane.

## 6. Open items and decisions for the owner
1. Operator login for the agent (spec Q1), BTReg57.DLL elevation (Q2), 36th room without a BIS door (Q3, mapping screen ready), 12:00 departure acceptable (Q4), language (Q5), unlocked front-desk session (Q6): unchanged from the spec.
2. Confirm that "Reactivated" on the panel may combine "extended" and "reactivated" (the log keeps them apart).
3. Should the key panel be offered automatically after check-in? (Not added, to keep check-in untouchable.)
4. Register the front-desk encoder once from the API (`RegisterKeyEncoder`, Manager) and store the token on the PC; there is no screen for it yet (the Encoder tab shows status only).
