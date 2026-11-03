# EGRESS contract — internet egress gate (wave F5)

Written 2 Nov 2026 by the F5 coordinator (Opus), docs-only. Source: Pending Inventory §3.4, Wave Plan §F5 / §1.4. Owner defaults **A28**: master switch **OFF at go-live**; per-integration switches yes; manual + optional scheduled window; **Owner only**; Windows firewall rule yes; Windows Update outside HCC (accepted). Today HCC makes **zero** outbound internet calls; this gate exists so the F9 adapters cannot.

## 1. Server — `Hcc.Infrastructure.Egress/` (new project)
```csharp
public enum EgressIntegration { Messaging, Ota, Ocr, Payments, Email, UpdateCheck }
public interface IEgressPolicy {
    EgressDecision Evaluate(EgressIntegration integration, DateTimeOffset now);   // never throws
    EgressState Current(DateTimeOffset now);                                       // for System health / clients
}
public sealed record EgressDecision(bool Allowed, string Reason);                  // Reason: "master-off", "integration-off", "outside-window", "settings-unreadable", "on"
public sealed record EgressState(bool MasterOn, IReadOnlyDictionary<EgressIntegration,bool> Integrations,
    EgressWindow? Window, bool EffectiveNow, DateTimeOffset? ChangedAt, string? ChangedBy, string? Reason);
public sealed record EgressWindow(TimeOnly From, TimeOnly To, IReadOnlyList<DayOfWeek> Days);  // hotel local time
public sealed class EgressGateHandler : DelegatingHandler   // outermost handler
public static class EgressServiceCollectionExtensions { IServiceCollection AddHccEgress(this IServiceCollection s); }  // registers policy + named client
```
- **One named client** `"egress"` via `IHttpClientFactory`: `services.AddHttpClient("egress").AddHttpMessageHandler<EgressGateHandler>()` registered **first** so it is the outermost handler. Every request carries the integration in `HttpRequestOptions` key `hcc.egress.integration`; a request without it is refused (`NET_OFFLINE`, reason `integration-missing`).
- `EgressGateHandler.SendAsync`: asks `IEgressPolicy` **before** calling `base.SendAsync` → no socket, **no DNS** when refused; refusal throws `HccException(HccError.NET_OFFLINE, reason)` (new code; HTTP 503 with `{code:"NET_OFFLINE", reason}` at the API edge). Each decision writes an audit row (refusals rate-limited to one per integration per minute).
- **Effective rule:** `EffectiveNow(i) = master && net.<i> && (window == null || now ∈ window)`; when a window is set, outside it is OFF.

### 1.1 Settings (dated, Owner-only, audited with reason, Configuration history; group **Internet**)
| Key | Type | Default |
|---|---|---|
| `net.internetEnabled` | bool | **false** |
| `net.messaging`, `net.ota`, `net.ocr`, `net.payments`, `net.email`, `net.updateCheck` | bool | false |
| `net.window` | `{from:'HH:mm', to:'HH:mm', days:number[]}` or null | null |
**Fail closed:** missing key, unreadable value, settings store exception, or a future-dated row only → treated as OFF, reason `settings-unreadable` (System health shows it). Change takes effect immediately (cache ≤ 5 s, invalidated on save). Change = Owner (PERM-NET-001), reason required; Manager read (PERM-NET-002); others nothing.

### 1.2 Outbox
Outbox items whose integration is OFF get status **`held`** with `heldReason = 'internet off'` (not a failure; no retry count consumed). The existing outbox worker gains a replay hook: on any `net.*` change to ON, and each worker tick while ON, `held` items of that integration move back to `pending` in original order. Held counts per queue (messaging, ota-push, email, scheduled-report) are exposed to System health.

### 1.3 Migrations 4730–4739
| No. | Content |
|---|---|
| 4730 | settings seed `net.*` (defaults OFF) + Internet group metadata |
| 4731 | outbox status `held` + `held_reason`, `held_at` columns; index (integration, status) |
| 4732 | `ops.egress_audit` (at, integration, decision, reason, user, host) |
| 4733 | RBAC seed PERM-NET-001 (Owner), PERM-NET-002 (Owner, Manager read) |
| 4734 | firewall-rule state row for System health (`ops.egress_firewall_state`) |
| 4735–4739 | spare / integrator |

## 2. Architecture test (`Hcc.Architecture.Tests/EgressTests.cs`, new project + `Hcc.slnx` line)
Scan every `*.cs` under `08 Production/src` (Roslyn syntax walk; regex fallback): **fail** on `new HttpClient(`, `HttpClient` constructor injection, `IHttpClientFactory.CreateClient(` with a name other than `"egress"`, `WebRequest`, `WebClient`, `HttpWebRequest`, `SmtpClient`, `System.Net.Sockets.Socket`, `TcpClient`, `UdpClient`, `Dns.` — **outside `Hcc.Infrastructure.Egress`**. Allow-list (explicit, commented, reviewed): test projects; the LAN Key Agent client if it targets a configured LAN host (must assert RFC1918 / loopback at startup). Second rule: `AddHttpClient("egress")` registration exists exactly once and `EgressGateHandler` is its first handler.

## 3. Unit / proof tests
- `Hcc.Egress.Tests`: counting inner handler — **0 sends when OFF**, N when ON; each sub-switch; window edges (from inclusive, to exclusive, across midnight); fail-closed on settings exception; missing integration option refused; outbox held → replay order; audit rows written.
- `tools/egress-proof.ps1` (gate step): hosts-file sink 127.0.0.1 for adapter hosts, local listener counts connections; run e2e with `net.internetEnabled=false` → **count must be 0**; log `Get-NetFirewallRule -DisplayName 'HCC Egress Block'` state and a blocked probe; output pasted into the gate report.

## 4. Windows firewall
`deployment/windows/Set-HccEgress.ps1 -State On|Off|Install|Status`: outbound rule "HCC Egress Block" for the `Hcc.Api` service executable blocking all destinations except 10/8, 172.16/12, 192.168/16, 127/8, ::1, fe80::/10. Installer registers the rule **enabled (blocking)** and the scheduled task `\HCC\ToggleEgress` (SYSTEM, on demand) **disabled**; the API writes the wanted state to `ops.egress_firewall_state` and starts the task; the task reads it and sets the rule. Task missing / fails → software gate still holds; System health "firewall rule not applied".

## 5. System health block **Egress** (server read + `features/admin/SystemHealth.tsx` block)
Master state, effective now (with reason), per-integration switches, window, last change (who / when / reason), held counts per queue, firewall rule state (applied / not applied / unknown), last refusal per integration. Read: Owner, Manager.

## 6. Clients
- Config read set gains `net.state` (EgressState without user ids for non-Owners).
- Banners: "Internet off — messages queue" (Messages, with held badge); OTA: "OTA bookings are not being received — last sync hh:mm" on Channel manager and room board; OCR scan button disabled with "Internet off — enter the ID by hand"; payment link actions hidden ("record the deposit by hand", DEC-102).
- Master configuration › **Internet** group (Owner): master switch, per-integration switches, window editor, reason prompt; Manager read-only; Housekeeping / Maintenance / Reception / Restaurant: not shown.
- Prototype mirrors: `s.config` keys `net.*` default OFF, command `CFG_NET_SET {key, value, reason}` Owner only, mock outbox `held` status + replay.
- APK `04 Android/capacitor.config.*`: `server.allowNavigation` = the HCC host only; external links open nothing when OFF.
