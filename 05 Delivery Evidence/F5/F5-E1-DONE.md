# F5-E1 DONE — egress-server + egress-proof

Lane E1 / Opus / branch `claude/f5-e1` / worktree `/home/user/wt-f5-e1` / base `fd9a0aec` (claude/f5).

## Commits
`git log --oneline fd9a0aec..HEAD` (prefix "F5-E1:"): server gate + migrations; tests; PowerShell scripts; prototype mirror; this file + lock files.

## Files
- Server (new): `08 Production/src/Hcc.Infrastructure.Egress/*` (EgressModel, EgressPolicy, EgressGateHandler, EgressOutbox, EgressHealth, EgressServiceCollectionExtensions), `src/Hcc.Infrastructure.SqlServer/Egress/SqlEgressStore.cs`, `src/Hcc.Api/Endpoints/EgressEndpoints.cs`.
- Server (edited, F5-E1 comments): `HccErrorCode.cs` + `HccErrorCatalog.cs` (`NET_OFFLINE` → 503 "Internet is off"), `ConfigCatalog.cs` (13th group `internet`, money-flagged = Owner only), `ConfigurationService.cs` (net.* change needs a reason), `HccComposition.cs` (two F5-E1 blocks), `Hcc.Infrastructure.SqlServer.csproj` (ref), `Hcc.slnx`, `Hcc.iss` (Set-HccEgress -State Install line).
- Migrations 4730–4734 (+ `migrations.lock.json` via `--add --accept-out-of-order`): 4730 fail-closed guard (defaults are in code), 4731 `ops.egress_outbox` (pending/held/sent/failed, held_reason, held_at, index integration+status), 4732 `ops.egress_audit`, 4733 PERM-NET-001 (OWN) / PERM-NET-002 (OWN, MGR), 4734 `ops.egress_firewall_state` (seed wanted Off). 4735–4739 unused.
- Tests: new `tests/Hcc.Egress.Tests` (16), new `tests/Hcc.Architecture.Tests/EgressTests.cs` (3); pins updated in Configuration (13 groups), Platform (NET_OFFLINE), Access (RbacV2Matrix LaterPermissions PERM-NET-001/002).
- Scripts (NOT EXECUTED HERE — Linux lane): `tools/egress-proof.ps1`, `08 Production/deployment/windows/Set-HccEgress.ps1` (-State On|Off|Install|Status|Apply; rule "HCC Egress Block", task `\HCC\ToggleEgress` SYSTEM on demand disabled).
- Prototype: `src/domain/streams/egress.ts`, `src/services/mock/handlers/egress.ts`, `tests/stream-egress.mjs` (5); F5-E1 blocks in `handlers/index.ts`, `services/mock/index.ts`, `domain/types.ts` (`egressX?`), `domain/errors.ts` (`NET_OFFLINE`); `tests/stream-rbac-matrix.mjs` rows CFG_NET_SET / NET_TICK = OWN.

## What was built
- `AddHccEgress()`: one named client `"egress"`, `EgressGateHandler` added first (outermost; verified by a unit test and the architecture test). Requests must carry `HttpRequestOptions` key `hcc.egress.integration` (helper `request.For(EgressIntegration.X)`), else refused `integration-missing`. Policy asked before `base.SendAsync` → no socket/DNS; refusal = `HccException(NET_OFFLINE)` details `{reason, integration}`. Audit every allowed call, refusals ≤ 1 per integration per minute (`EgressActivity`).
- `EgressPolicy`: effective = master && switch && (no window || in window, hotel time IST; from inclusive, to exclusive, across midnight counts for start day). Cache 5 s; settings exception / missing / non-boolean / bad window / snapshot older than 30 s → OFF `settings-unreadable`.
- Settings `net.internetEnabled`, `net.messaging|ota|ocr|payments|email|updateCheck` (bool, default false), `net.window` (text JSON `{"from","to","days"}` or empty) in group Internet; Owner only; reason required.
- Outbox: `EgressOutbox.EnqueueAsync(queue, integration, ref, payload)` stores held when OFF; `EgressOutboxWorker` (subclass of the existing `OutboxPollingWorker`, 2 s) ON → held→pending in item order, OFF → pending→held, no attempts consumed; also writes the wanted firewall state and runs `schtasks /Run /TN \HCC\ToggleEgress` on Windows when it changes.
- `GET /api/v1/admin/system-health/egress` (PERM-NET-002): `EgressHealthView` {masterOn, effectiveNow, reason, integrations[{integration, switch, effectiveNow, reason, lastRefusalAt, lastRefusalReason}], window, changedAt, changedBy (Owner only), changeReason, queues[{queue, held, pending}] (messaging, ota-push, email, scheduled-report), firewallRule applied|not applied|unknown, firewallDetail}.
- Architecture scan: no `new HttpClient`, HttpClient injection, `CreateClient` other than "egress", WebRequest/WebClient/HttpWebRequest/SmtpClient/Socket/TcpClient/UdpClient/Dns outside `Hcc.Infrastructure.Egress` (comments/strings ignored). Existing code had no outbound use; one allow-list entry: `SignInThrottle.cs` `using System.Net.Sockets` (AddressFamily only).
- Prototype: `CFG_NET_SET {setting, value, reason}` (Owner, reason 3–120 chars, values in `configX.values['net.*']`, change log `egressX.changes`, replays/holds at once), `NET_TICK`, helpers `netDecide`, `netState(s,uid)`, `netEnqueue`, `netReplay`, `netHeldCounts`, `NET_SETTING_DEFS`.

## Tests
- `dotnet build "08 Production/Hcc.slnx"`: 0 warnings, 0 errors.
- `dotnet test` Hcc.Egress.Tests 16/16 (incl. SQL), Hcc.Architecture.Tests 3/3, Hcc.Configuration.Tests 35/35, Platform DatesAndErrors 9/9, Access RbacV2Matrix 7/7. Full server suite not run (another suite on this machine).
- Prototype: `tsc --noEmit` clean; `npm test` 2489/2489 (before 2484 + 5 new; first run caught the RBAC matrix rows, fixed).

## Open items
- Deviation: prototype command field is `setting`, not `key` (`key` is every command's idempotency key; `{key:'net.x'}` made the second change a replay).
- Prototype Internet group not registered in `CFG_GROUPS` (U1 owns the group UI; `NET_SETTING_DEFS` ready; `stream-fit-admin-a.mjs` asserts 12 groups and must move to 13 then). No SCHEMA bump (new optional slice `egressX`) — integrator decides.
- `config read set net.state` for clients not added server-side (U1 can read the egress endpoint); `syshealth.ts` / `SystemHealth.tsx` block = U1.
- Settings save does not invalidate the policy cache directly (Application cannot see Egress); effect within 5 s / next worker tick.
- egress-proof is currently vacuous by construction (HCC has no adapter yet); real value comes with F9 adapters. Scripts never run here.
- Other test projects' `packages.lock.json` gained the Egress project reference (committed).

## Integration needed
- U1: register Internet group in prototype `CFG_GROUPS` + provider from `NET_SETTING_DEFS`, commands via `CFG_NET_SET {setting,value,reason}`; fix `stream-fit-admin-a.mjs` count 12→13; client error map for `NET_OFFLINE`.
- f5-int: run full suites + `tools/egress-proof.ps1` on the hotel PC; `Set-HccEgress.ps1 -State Install` once.

## HANDOFF bullet
F5-E1 egress gate: `Hcc.Infrastructure.Egress` (one "egress" HttpClient, gate outermost, NET_OFFLINE 503, net.* Owner-only default OFF, held outbox + replay, audit), migrations 4730–4734, Hcc.Egress.Tests 16 + architecture scan 3, `GET /admin/system-health/egress`; prototype CFG_NET_SET {setting,…}; Set-HccEgress.ps1 / egress-proof.ps1 not executed yet (hotel PC).
