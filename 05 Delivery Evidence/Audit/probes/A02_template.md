# A02 - Feature coverage table (prototype commands, screens and server routes)

Agent A2 (Sonnet), independent audit of HCC, branch `claude/audit-fable-2026-10-26`, 26 Oct 2026. Audit-only: nothing in the product code was changed. This file states what the code does; it gives no go-live judgement (agent A2b does that from this file).

Reproduce: `$env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A02_commands.mjs"` (enumeration, writes `evidence/A02/*.json`) then `node "05 Delivery Evidence/Audit/probes/A02_render.mjs"` (this file's tables). Node v25.9.0 (recorded in `evidence/A02/run-output.txt`). The scripts bundle the prototype's real `registry` of command handlers and the real sync `AREAS` with esbuild from `03 Prototype/node_modules` (nothing installed, nothing written to the repo outside `Audit/`), parse the screens with the TypeScript compiler API, and read the server's `Endpoints/*.cs` as text.

## 0. Headline numbers

| Fact | Number | Where |
|---|---:|---|
| Prototype command types (union of the `switch` in `commands.ts` and the stream handlers) | **189** | 22 `case` labels in `src/services/mock/commands.ts` + 188 handler keys in `src/services/mock/handlers/*.ts`, 21 of them replace a `case` (so 22 + 188 - 21 = 189). `evidence/A02/commands.json` |
| ...mapped to a server command | **164** | a `CommandMapper` with that `type` in one of the 15 sync areas |
| ...unmapped with a written reason | **20** | listed in some area's `unmapped:{}` |
| ...absent (neither mapped nor listed) | **5** | `AI_ASK`, `AI_CLEAR`, `AI_SETTINGS`, `GRP_SWEEP`, `SHIFT_CLOSE` |
| Mappers registered in total | 171 | the 164 above + 7 production-only types the prototype's mock does not have (`INVOICE_ISSUE`, `USER_CREDENTIAL`, `DEVICE_REGISTER`, `DEVICE_REVOKE`, `HR_DEPT_SAVE`, `INV_ITEM_CREATE`, `SHIFT_OPEN`) |
| Distinct server commands the mappers send | 172 | all 172 exist as `POST /api/v1/commands/<Name>` (section D, 0 missing) |
| Server `/commands/<Name>` routes found in `08 Production/src/Hcc.Api/Endpoints/*.cs` | 216 | 122 direct `MapPost`, 94 through the `Command<>`/`Cmd<>` helpers; 6 further POST routes are not under `/commands` (`/auth/*`, `/configuration/import-plan`, `/setup/SetupProperty`, `/public/booking/requests`) |
| Server commands that no mapper sends | 44 | 1 of them (`RevealGuestId`) is sent by `clients/sync/guestid.ts`; the other 43 are sent by no client code at all (section D2) |
| Desktop screens (page ids) / phone screens | 38 / 36 | 11 core pages + 27 registered in `features/*/register.tsx`; 36 phone pages (`mobile.pages`); two desktop pages have no phone variant and are shown as-is on the phone (`customers`, `home`) |
| Screens with at least one unmapped or absent command | 14 desktop, 15 phone | counts include `GUEST_ID_VIEW`, which is unmapped on purpose (production reveals through `guestid-ui.tsx`); without it: 10 desktop, 8 phone |
| State slices with a loader / without | 34 / 7 | the 7 without: `schema`, `scenario`, `processed`, `sequence` (local bookkeeping), `tariffs`, `taxSchedule` (retired), `assistantX` (`evidence/A02/state-keys.json`) |

How a refusal looks: `run()` in `03 Prototype/src/clients/sync/runtime.ts:302-305` looks the type up in the mapper table; a type with no mapper (unmapped **or** absent) is refused with `Not available on the server yet: <TYPE>` and nothing changes. Unmapped and absent therefore behave the same for the user; the difference is only that "unmapped" is documented and "absent" is not. No test walks the 189 prototype commands against `AREAS` (each `tests/sync-<area>.mjs` pins its own `unmapped` list, e.g. `tests/sync-admin.mjs:248`, `tests/sync-channels.mjs:156-159`), which is how the 5 absent ones are not caught. `validateAreas` (`runtime.ts`) only refuses duplicate slice owners and duplicate mappers.

## A. Summary tables

### A1. By sync area (file under `03 Prototype/src/clients/sync/`)

Method: a command counts in the area whose mapper sends it (mapped), whose `unmapped` list names it (unmapped), or, for the absent ones, in the area that already serves the same prototype module (the module's mapped commands vote; `assistant` has no area). Rows for `services/*` and `channels/*` are split by file. "Extra production-only mappers" are mapper types the mock does not have (they are not in the 189).

{{summaryAreaTable}}

### A2. By prototype module (the stream that owns the command in `services/mock/handlers/`)

{{summaryModuleTable}}

## B. Per-command appendix

Columns: command type, owning mock module, status, screens that dispatch it (`D:` desktop page ids, `P:` phone page ids, `booking dialog` = reachable from the shared booking dialog, opened from many screens; at most 5 ids shown, `+N` = more), the sync area file with the mapper (or the area that lists it as unmapped), the server command(s) the mapper sends (`†` = the name is passed through a helper such as `post(c,'SaveStaff',...)` in `staff.ts` and was found as a literal in the mapper source rather than as the first argument of `api.command`), the slices reloaded after success, and the reason from the area's `unmapped:{}`. Sorted by sync area, then by command. Screen attribution is a static source walk (section C method note), so a command shown as `—` is dispatched by no screen file.

{{appendixB}}

### B2. Mapper types that do not exist in the prototype mock (production-only)

{{prodOnlyTable}}

`EDIT_BLOCK` appears in `rooms.ts:163` `unmapped:{}` but is not a prototype command (no handler, no `case`, no screen): a stale entry. `EXPENSE_APPROVE` is mapped in `accounting.ts:225` and also listed as unmapped in `admin.ts:283` (pinned by `tests/sync-admin.mjs:248`).

### B3. Commands that no screen dispatches (7)

| Command | Status | What the source says |
|---|---|---|
| `NIGHT_POST` | mapped | alias of `CLOSE_RUN` (`closing.ts:56`); only the day-close flow and tests send it |
| `POST_NIGHTS` | unmapped | "M1 compatibility command with no UI button" (`handlers/closing.ts:3`, `closing.ts:60`) |
| `SHIFT_CLOSE` | **absent** | re-registered in `handlers/closing.ts:49`; no screen file contains it (the Cashier and Day closing screens dispatch `SHIFT_SUBMIT` and `SHIFT_DIFFERENCE_DECIDE` instead); only `commands.ts:150`, the mock handler and tests refer to it |
| `SETTINGS` | unmapped | legacy BL-027 command, replaced by `SETTING_SET` per key |
| `TAX_SCHEDULE` | unmapped | the mock handler always refuses (`handlers/admin.ts:340`, DEC-069) |
| `HR_RATES_SET` | mapped | alias of `HR_STAT_RATES_SET` (`handlers/staff.ts:254`, `staff.ts:212`) |
| `GRP_SWEEP` | **absent** | internal auto-release of expired group blocks, "also runs before every GRP command" (`handlers/groups.ts:9`); never sent by a screen |

## C. Per-screen table

Method note. Screens: desktop = the 11 core pages built in `src/app/App.tsx:209` plus every page in `extraPages` (`features/*/register.tsx` `pages:{}`); phone = `mobile.pages` in the registrations (the phone shell shows the desktop page when no phone page is registered: `customers`, `home`). For each screen the probe walks the TypeScript symbol graph from the page component through imports under `src/features`, `src/mobile`, `src/app/mobile` and (for slices only, two hops) `src/domain`, collecting (a) `type:'X'` literals that are known command types (`ctx.run`), (b) `open({type:'<modal>'})` and `booking(...)` calls, then does the same inside the modal components registered for that platform. The shared **booking dialog** (`BookingModal`, opened with `booking(id)`) is counted once in section C3, not in every screen, so "Own commands" excludes it. Four things the walk cannot see statically and that were added by text scan: the desktop `new` reservation dialog books through the typed client `useReservations().commit` (that is the `BOOK` command, added to every screen that opens `new`); the Master configuration registry builds commands in `domain/streams/config.ts` (`settings` screen: `YLD_GUARD_SET`, `GSV_SETTINGS`, `POS_MENU_TOGGLE`, `AI_SETTINGS`, `HR_STAT_RATES_SET`, `HR_LEAVE_POLICY_SET`, `CMP_SETTINGS_SET`); the Approvals page runs `type:r.command` for the kinds the handlers queue (`meta:{command:'...'}`: nine types). Slices are read as `s.<slice>` / `ctx.s.<slice>` / `const {..}=s`; a slice that only a domain selector reads (two hops deep) is shown as "via selectors". `sequence`, `scenario`, `schema`, `processed` are local bookkeeping and are left out of the empty-slice column. "Loader fills" means some area in `AREAS` returns that slice (section C4); it does not say the data is complete (that is what `tests/sync-*.mjs` check). Reports in production read the server through `sync/reports.ts`, not through slices.

Cross-check of the walk: every `type:'X'` command literal found anywhere in `src/features`, `src/mobile`, `src/app` is attributed to at least one screen; the only commands attributed to no screen are the 7 in B3 (their source lines are in `evidence/A02/screens.json` `notOnScreenRefs`).

### C1. Desktop screens (38)

{{desktopTable}}

### C2. Phone screens (36; `customers` and `home` use the desktop page)

{{phoneTable}}

### C3. The shared booking dialog (opened with `booking(id)` from most screens)

`bookingActions` slots from every stream are rendered inside it, which is why it reaches commands of other modules.

{{bookingDialogTable}}

### C4. Which areas fill which slices (loaders)

{{loadersTable}}

Slices of the State skeleton that no loader fills: {{notLoaded}}. Screens that read them: `assistant` (`assistantX`), `settings` and `yield` (`tariffs`: the dated tariff list stays empty, so `rateFor()` falls back to the room's own rate; whether the Yield screen then shows an empty history on the server was not run).

## D. Mapper targets against the server routes

D1. Result: **0 mapper targets without a matching `/commands/<Name>` route** (172 distinct names checked, all found). Full list with the `file:line` of each route and the prototype commands that send it: `evidence/A02/mapper_target_routes.md`. Method: server names are read from `Hcc.Api/Endpoints/*.cs` as text: literal `MapPost("/commands/Name")`, `MapPost("/commands/" + Class.OpX)` with `OpX` resolved through the `const string` declarations under `08 Production/src`, and the helper calls `Command<..>(g, "Name"|Class.OpX, ...)` / `Cmd<..>(...)`; no unresolved constant remained (`server-routes.json` `unresolved: []`). Mapper names are the literal first argument of every `api.command(...)` reachable from the mapper's `send()`, followed through helper functions in the bundled sync layer, plus (marked `†`) literals in the mapper source that equal a server command name. This is a name check only: request-body shape and permission are not compared here. Nothing was executed against a running server.

Mapper types with no detected server command: none. Mapper `send()` calls whose name is not a literal: 19, all `staff.ts` `post(c, name, ...)` helper calls; their names were taken from the literals in each mapper (`†`).

D2. Server commands that no mapper sends (44), and whether other client code sends them. These are server abilities with no prototype command to map (the prototype has no screen for them) or a client path outside the mappers:

{{serverNotTargetedTable}}

## E. Findings (facts only; severity is a proposal, A2b and the lead decide)

| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A02-01 | Medium | Coverage / silent refusal | `03 Prototype/src/clients/sync/runtime.ts:305`; `features/assistant/register.tsx:8`; `domain/streams/config.ts:483` | 5 prototype commands have neither a mapper nor an `unmapped` entry (appendix B, rows marked ABSENT). The Management assistant page (desktop `assistant`, phone `assistant`, nav item `p:'reports'`) and the AI setting in Master configuration stay in the production build; no `HCC_BUILD==='production'` hiding was found in `nav.ts`, `navmap.ts`, `ModulesHome.tsx`, `registry.ts`. | A user with Reports access can open the Assistant and every action answers "Not available on the server yet: AI_ASK" (and `AI_CLEAR`, `AI_SETTINGS`). `SHIFT_CLOSE` and `GRP_SWEEP` are not dispatched by any screen. | List the three AI commands (and the two dormant ones) in an `unmapped` with a reason, or hide the assistant in production; add a foundation test that every prototype command is mapped or listed. | 2 | High (absent), Medium (page reachable: not run) |
| A02-02 | Medium (A3a/A2b to confirm; could be High) | GST master / Master configuration | `08 Production/src/Hcc.Application/Tax/GstSettings.cs:12-24`; `08 Production/src/Hcc.Application/Configuration/ConfigurationService.cs:42-43`; `08 Production/src/Hcc.Api/Endpoints/TaxEndpoints.cs:33,35`; `03 Prototype/src/clients/sync/admin.ts:257-263` | The server lists every `gst.*` key in the settings registry as `Editable = false` ("edited in Money and tax -> GST master"); `SetSetting` throws `NotConfigurable` for a non-editable key. The only edit path is `POST /commands/SetGstSetting` and `WithdrawGstSetting` (Owner only). The prototype's `SETTING_SET` mapper always sends `SetSetting`; no client file sends `SetGstSetting`, `WithdrawGstSetting` or reads `GET /tax/gst` (searched `src/clients`, `src/features`, `src/mobile`, `src/app`: 0 hits). | In the production build the "Money and tax" GST rates shown on Master configuration (`settings` screen) cannot be changed from the app; a rate change (e.g. after the CA answers, DEC-069) would have to be made by calling the API outside the app. Static reading only, not run. | Add mappers `SetGstSetting` / `WithdrawGstSetting` (and a `GET /tax/gst` loader) for `gst.*` keys in `admin.ts`, with a test. | 6 | Medium |
| A02-03 | Low | Documentation of gaps | `03 Prototype/src/clients/sync/rooms.ts:163`; `03 Prototype/src/clients/sync/admin.ts:283`; `03 Prototype/src/clients/sync/accounting.ts:225` | `EDIT_BLOCK` is listed as unmapped but is not a prototype command; `EXPENSE_APPROVE` is mapped and also listed as unmapped. | The `unmapped` lists the README says are the record of gaps contain one stale and one contradictory entry. | Remove both entries and the matching assertion in `tests/sync-admin.mjs:248`. | 0.5 | High |
| A02-04 | Info | Screens with all commands unmapped | appendix C1/C2 | `documents` (3 of 3: `MSG_DOC_EXTRACT/CONFIRM/DISCARD`), `backup` (1 of 1: `BACKUP_SIMULATE`, demo only by design), `assistant` (see A02-01). Partly unmapped: `groups` (4 of 8: allot, cancel, release, rooming), `closing` (2: `CORRECTION_ADJUST`, `CORRECTION_ADJUST_DECIDE`), `approvals` (2: `POLICY_DECIDE`, `CORRECTION_ADJUST_DECIDE`), `access` (2: `GRANT`, `POLICY_SET`), `settings` (`TARIFF_SCHEDULE`, `AI_SETTINGS`). | These screens render on server data but the listed buttons are refused. Reasons are in appendix B. | A2b to rank. | - | High |
| A02-05 | Info | Server ahead of the client | section D2 | 43 server commands are sent by no client code: customer CRM (notes, tags, merge, preferences, consent), group management (update, routing, members, attrition, receipts, settle), ID capture documents (`CaptureIdDocument`, `ConfirmIdCapture`, `RejectIdCapture`, `ViewIdCaptureImage`, `RevealIdCaptureNumber`, `PurgeIdCaptures`), masters import (`UploadImportBatch`, `CommitImportBatch`), GST master (A02-02), web deposit link/paid, `ChangeOwnCredential`, outlets/menu/till set-up, stock settings, `EarlyDeparture`, `SeedComplianceDefaults`, `RekeyGuestIds`, `SetYieldForecast`, channel import/simulate. | The prototype has no command for most of them; they are reachable only by API or script. Whether a daily hotel task depends on any of them is A2b's call. | - | - | High |

## F. The three closing lists

### Claims I could not confirm

1. That the Assistant (and other server-less screens) are hidden in the production build: no hiding code was found in the files named in A02-01, but the production UI was not run (the Master configuration module switches, `cfgModulesOff`, could still hide it if the server's module list is loaded as `configX.modulesOff`).
2. That `SETTING_SET` on a `gst.*` key fails on the real server (A02-02): read from `GstSettings.cs` (`Editable = false`) and `ConfigurationService.cs:43`, not executed. Whether the production screen even offers the edit for those keys was not run.
3. That every screen's commands are exactly those listed: the attribution is a static source walk, not a UI run. Cross-checks that hold: every command literal found in UI source is attributed to a screen; the 7 unattributed commands are explained in B3. Commands built by string concatenation or by registries other than `config.ts` / approvals `meta.command` would be missed.
4. That the server route list (216) is complete: it is regex extraction over the C# source, cross-checked by count (122 direct `MapPost` + 94 helper calls; the 133 `.MapPost(` occurrences in `Hcc.Api` also include the helper definitions and 6 non-`/commands` routes); no ASP.NET endpoint dump was taken because nothing may be run against a server.
5. That each loader returns complete data, and that every GET a loader sends exists on the server (`tests/_role-reads.json` is the pinned list and `RoleReadMatrixTests` is described as running against the real server; neither was run here).
6. Request-body compatibility of the 172 mapper targets (names only were compared).

### Open owner/CA decisions I hit (and the code that depends on each)

1. Role matrix v2 (D1-D7, `02 Planning/HCC_RBAC_Matrix_v2_Proposal.md`): `GRANT`, `POLICY_SET`, `POLICY_DECIDE` stay unmapped until it is decided (`admin.ts:277-283`, reasons in appendix B).
2. GST rates are "illustrative - CA to confirm" (DEC-069): the edit path is `SetGstSetting` / `WithdrawGstSetting` (A02-02); today's mapper cannot reach it (`admin.ts:257`).
3. Whether the simulated Management assistant ships in production (module 26): `AI_ASK`, `AI_CLEAR`, `AI_SETTINGS` (A02-01).
4. Real document / ID capture instead of the fictional-sample OCR: `MSG_DOC_EXTRACT/CONFIRM/DISCARD` map to `CaptureIdDocument` / `ConfirmIdCapture` / `RejectIdCapture` once a capture screen exists (`services/messaging.ts` `unmapped`; DEC-068 and CA question L1 govern what may be stored).
5. Group block change/cancel/rooming flows (`GRP_BLOCK_ALLOT`, `GRP_BLOCK_CANCEL`, `GRP_RELEASE`, `GRP_ROOMING`): the reasons point at backlog items for block allotment change, block cancellation and a block version read (`money.ts` `unmapped`).
6. Closed-date corrections: `CORRECTION_ADJUST` and `CORRECTION_ADJUST_DECIDE` wait for a charge picker and the approvals slice (`closing.ts`), i.e. an owner decision on how a free-form correction after day close is meant to work on the server (server corrections reverse a whole posted line).

### Questions for the lead

1. Should "absent" (5) and "unmapped" (20) be rated the same by A2b? They refuse identically at run time; only the documentation differs.
2. A02-02 touches money (GST). Please have A3a confirm whether `SetSetting` really refuses `gst.*` and whether the server's `GET /tax/gst` is meant to be shown on the Master configuration screen; I did not run it.
3. 43 server commands have no sender (D2). Is a client for the CRM, group-management and masters-import features in scope for go-live, or are they API/script-only by design? A2b needs that to rank the gaps.
4. The 7 production-only mappers (B2) exist because the screens call types the mock never had; if the lead wants the prototype contract in sync, `Command` types in `domain/types.ts` are loose (`type: string`), so nothing flags them.

## G. Appendix: capability gaps declared in code

`CAPABILITY_GAPS` in `03 Prototype/src/clients/sync/capabilities.ts` lists six known differences between server grants and the prototype's coarse permissions (Accountant gets "expenses"; Cashier gets "reports" through PERM-RPT-002:V; "approve" follows PERM-APR-001 only; request-only grants show the button and the server answers `APPROVAL_REQUIRED`; limits are enforced server-side; module PERM ids do not change `can()`). They concern who sees a button, not which commands are mapped; A5 owns the role side.
