# af-netset DONE (wave 3, audit item 23 CFG_NET_SET)

Lane af-netset · branch claude/af-netset · worktree E:\Projects\HCC\worktrees\af-netset · base 6a29f3e6 (+ claude/auditfix merged).

## Contract sections used (02 Planning/agents/EGRESS-CONTRACT.md)
- §1.1: keys `net.internetEnabled`, `net.messaging`, `net.ota`, `net.ocr`, `net.payments`, `net.email`, `net.updateCheck` (bool), `net.window` (`{from,to,days}` or null). "dated, Owner-only, audited with reason, Configuration history"; "Change = Owner (PERM-NET-001), reason required; Manager read (PERM-NET-002)".
- §6: "Config read set gains `net.state`"; prototype mirror "command `CFG_NET_SET {key, value, reason}` Owner only".
- §4: "the API writes the wanted state to `ops.egress_firewall_state` and starts the task".
- F5-E1-DONE.md: the wanted state is written by `EgressOutboxWorker` from the policy; migration 4733 already seeds PERM-NET-001 (Owner) / PERM-NET-002.

## Is wanted_state written?  NO
`SetNetSetting` does not touch `ops.egress_firewall_state` or the firewall table. The existing `EgressOutboxWorker` already derives and writes `wanted_state` from the policy every 2 s after a `net.*` change. A test asserts the row is unchanged by the command. Set-HccEgress.ps1 untouched.

## Stopped on / contract silent (not invented)
1. Audit event name: the contract names none for a setting change (only `ops.egress_audit` for gate decisions). The plan's `NET_SETTING_SET` is NOT in the contract, so it was not added. The change is audited by the existing Master-configuration path: audit `SETTING_SET` ("Setting net.x · Off → On ...") + outbox `SettingChanged` + `cfg.setting_change` row with the reason. Owner to say if a separate `NET_SETTING_SET` audit row is wanted.
2. `net.state` config read set (§6) for clients is still not built server-side (not in this brief).
3. Reason length: plan says 3–200, contract/service/prototype/UI say 3–120. Used 3–120 (ConfigurationService truncates at 120).
4. Replays: `expectedVersion` is part of the request hash (as for SetSetting), so the client sends the setting's row version from the last load (null = never saved).

## Built
- `08 Production/src/Hcc.Application/Egress/NetSettingCommands.cs` (new): PERM-NET-001 check, key list, boolean/window shape, reason 3–120; delegates to `ConfigurationService.SetSettingAsync` (existing path).
- `08 Production/src/Hcc.Api/Endpoints/EgressEndpoints.cs`: DI line + `POST /commands/SetNetSetting` inside `// AF-NETSET` markers. No HccComposition.cs edit needed (registered in AddEgressModule). No migration (PERM-NET-001 exists in 4733).
- `03 Prototype/src/clients/sync/egress.ts` (new): CFG_NET_SET → SetNetSetting, reload configX/settings/configHistory/approvals; NET_TICK listed as unmapped. `registry.ts`: block `// AF-NETSET`.
- Tests: Hcc.Egress.Tests `NetSettingTests` (6): Owner_sets_net_state_on_with_reason_audit_and_change_row, Manager_is_FORBIDDEN, Unknown_key_is_VALIDATION, Missing_reason_is_VALIDATION, Same_key_replay_is_idempotent, Key_list_is_the_contract_list. `03 Prototype/tests/sync-egress.mjs` (3 tests, [AF-NET]).

## Test counts
- Hcc.Egress.Tests: 16 -> 22. Hcc.Architecture.Tests: 3 -> 3 pass. `dotnet build Hcc.slnx`: 0 warnings, 0 errors. `npx tsc --noEmit` clean.
- npm test (through the lock): 2781/2781 passed (20 async); before on claude/auditfix: 2778 (2777 main + runner self-test) -> +3 [AF-NET] tests. Secret scan ok (181 lines, 0 findings).

## Integration needed
None. (Optional follow-ups: item 1 above; net.state read set.)

## Owner review required (settings, plan section 7)
Owner-only internet switch; confirm the audit decision in item 1.

## HANDOFF bullet
af-netset: Owner Internet switch now has a server command SetNetSetting (PERM-NET-001, through Master configuration, audited SETTING_SET, no firewall write) and the client maps CFG_NET_SET; 6 server + 3 client tests. Audit name NET_SETTING_SET not added (contract silent).
