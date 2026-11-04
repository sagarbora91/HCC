# af-sync-ops DONE

Lane af-sync-ops (wave 2) / Sonnet / branch `claude/af-sync-ops` / worktree `E:\Projects\HCC\worktrees\af-sync-ops` / base 6a29f3e6 + `claude/auditfix` merged (wave-0 runner fix, baseline 2778 tests).

## Commits
See `git log --oneline 6a29f3e6..HEAD` (main commit: "OPS-01/02/03: map KEY_*, TRC_* and RPT_* commands in the production sync layer; PUT/DELETE on HttpTransport"; DONE file commit after it).

## Files
Prototype, all LF (the repo stores LF):
- new `03 Prototype/src/clients/sync/keys.ts`, `tariffcard.ts`, `reports-hub.ts`
- edited `03 Prototype/src/clients/http.ts` (added `put` and `delete` to `HttpTransport`; `post` already existed; no Idempotency-Key on these, never retried)
- edited `03 Prototype/src/clients/sync/registry.ts` (blocks `// AF-SYNC-OPS` ... `// AF-SYNC-OPS end`: 3 imports, 3 `AREAS` entries)
- new tests `03 Prototype/tests/sync-keys.mjs`, `sync-tariffcard.mjs`, `sync-reports-hub.mjs`

## What was built
- OPS-01 KEY_* (`keys.ts`, area `keys`): KEY_REQUEST -> RequestKey {reservationId, event, reason, validTo, cards:1}; KEY_MANUAL -> RecordManualKey; KEY_CANCEL -> CancelKey; KEY_READ -> ReadKey {note}; KEY_LOCK_SAVE -> SaveKeyLock {roomId, hasLock, lockNo, doorName, note, expectedVersion}. Events and reasons are identical on both sides (checked against `KeyEvents.All` / `KeyReasons.All`), sent unchanged. Lock version learnt from GET /keys/locks (`ids.setVersion('keyLock', roomNo)`); the server room id is learnt as `ids 'room'` (room ids in the prototype are room numbers, so `serverId(room)` as written in the brief would be wrong). Loader keysX: jobs from GET /keys/log (oldest first), locks from /keys/locks (PERM-KEY-004), encoder from /keys/encoder, active cards from GET /keys/panel/{id} for the newest 25 bookings with a finished issue/extend in the log (the log has no cards, and without them the Cancel key button stays disabled). KEY_AGENT_STEP, KEY_CLOCK, KEY_SIM, KEY_ENCODER stay unmapped (demo encoder).
- OPS-02 TRC_* (`tariffcard.ts`, area `tariffcard`): GET /tariff-card -> tariffCardX (version id = server Code, numeric id learnt as `ids 'tariffVersion'`; line id 'L001' = LineNo; status lower-cased; publish log mapped). TRC_DRAFT_SAVE -> SaveTariffCardDraft (an edit that sends only lines or date takes label / date / weekend days / notes from the loaded version), TRC_PROPOSE/PUBLISH/DISCARD -> {versionId}, TRC_REJECT -> {versionId, reason}, TRC_WEEKEND_SET -> {versionId, days}. TRC_IMPORT unmapped.
- OPS-03 RPT_* and interim EX-01 (`reports-hub.ts`, area `reportsHub`, owns slice rptX because no other area did): loaders favourites (own uid), schedules and runs (PERM-RPT-004). RPT_FAV_TOGGLE -> POST /reports/favourites/{id}; RPT_SCHEDULE_SAVE -> POST /reports/schedules or PUT /reports/schedules/{sid} with ScheduleRequest (recipients via serverId); RPT_SCHEDULE_DELETE -> DELETE; RPT_LOG -> POST /reports/{id}/print-log {params, rows, provisional, format}, returns the log id. RPT_RUN_DUE unmapped. exportLog stays empty (no server list route).
- Deviations from the brief: (1) the report routes are not `/commands/`, so no Idempotency-Key is sent; a favourite toggle must not be replayed by a retry. (2) RPT_LOG reloads nothing (the export log is never loaded). (3) Loader gating uses `me.permIds` / `can('reports')`, not `mayRead`, because `sync/reads.ts` is not in this lane's ownership (see Integration needed).

## Tests
Commands (through the lock, in `03 Prototype`): `npx tsc --noEmit` (clean); dev loop `HCC_TEST_ONLY='sync-(keys|tariffcard|reports-hub|foundation)' npm test` = 86/86 passed; full `npm test`.
- Before (baseline from af-runner, claude/auditfix): 2778. After: 2792 (+14 new). Full run: **2787/2792 passed, 5 failed** — all five are `[D5] <role>: boots with zero 403 reads, no unknown read and no load errors` in `tests/sync-roles.mjs` (OWN, MGR, REC, CSH, ACC): the new GETs `/keys/log`, `/tariff-card`, `/reports/favourites` are not listed in `tests/_role-reads.json`. See Integration needed. The 14 new tests all pass.
- `npm run build:production`: built, 0 warnings. `npx tsc --noEmit`: clean (run after the last source edit). `npm run build` (prototype) not re-run: the sync layer is production-only and the bundle-separation test in `npm test` passed. Secret scan: ok, 0 findings.

## Open items
- PrintLogAsync ignores the format (server, `08 Production/src/Hcc.Reporting/ReportService.cs:95-104`): it always stores `ReportFormat.Print` and never reads `req.Format`. The client sends the format, but the server export log will show "print" for xlsx/csv until the server parses `req.Format` (print|xlsx|csv|pdf). EX-01 is therefore closed on the client side only; the server edit is outside this lane.
- Key log rows carry no job kind: a manually recorded key reads as its event's kind; `timeoutAt` is empty (state comes from the server). The lock save does not send `doorId` (the prototype has no such field): saving a lock may clear a stored door id; check `SqlKeyStore.SaveLockAsync`.
- A report format that arrives as an enum number is mapped by index; if the API serialises it as a string (expected) it is lower-cased.
- Not run: phone/APK checks, any emulator.

## Integration needed
1. `03 Prototype/src/clients/sync/reads.ts` READS: add rules matching the loaders, e.g. `'GET /keys/log':one(wide('PERM-KEY-001'))`, `'GET /keys/encoder':one(wide('PERM-KEY-001'))`, `'GET /keys/panel/:id':one(wide('PERM-KEY-001'))`, `'GET /keys/locks':one(wide('PERM-KEY-004'))`, `'GET /tariff-card':one(wide('PERM-TRF-001'))`, `'GET /reports/favourites':one(wide('PERM-RPT-001'),wide('PERM-RPT-002',true))` (view check as the server's `CanViewAsync`), `'GET /reports/schedules':one(wide('PERM-RPT-004'))`, `'GET /reports/runs':one(wide('PERM-RPT-004'))`; then switch the three loaders to `reads:[...]`/`c.mayRead`.
2. `Hcc.Integration.Tests` `RoleReadMatrixTests`: add the same routes, regenerate `03 Prototype/tests/_role-reads.json`; that turns the 5 `[D5]` failures green (sync-roles also asserts READS keys equal the fixture routes).
3. `08 Production/src/Hcc.Reporting/ReportService.cs` `PrintLogAsync`: use `ParseFormat`-like parsing of `req.Format` (accept print) instead of the constant `ReportFormat.Print`.
4. If another lane (af-small) changed `UNMAPPED_TEXT` for RPT_RUN_DUE / TRC_IMPORT / KEY_*: no overlap with these files; the texts stay in `runtime.ts`.

## HANDOFF bullet
OPS-01/02/03 + EX-01 client side (af-sync-ops, claude/af-sync-ops): new sync areas keys, tariffcard, reportsHub map KEY_*, TRC_* and RPT_FAV_TOGGLE / RPT_SCHEDULE_* / RPT_LOG to the server (HttpTransport gains put / delete). 14 new tests; full npm test 2787/2792, the 5 failures are sync-roles needing the new GETs added to reads.ts and `_role-reads.json` (Integration needed). Server PrintLogAsync still ignores the export format.
