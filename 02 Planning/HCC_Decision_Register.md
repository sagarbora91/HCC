# Hotel Control Centre — Decision Register

Version: 1.0 · Updated: 24 October 2026 · Owner: Sagar

**Statuses:** Agreed · Proposed · Needs owner decision · Unverified.
**Rule:** later owner changes take precedence; earlier entries are superseded (never rewritten) and the impact is recorded.
**Sources:** [C] conversation export · [P] execution plan · [B] observed in the existing prototype build · [O] owner.

## 1. Approval log

| ID | Date | Approved by | Artifact / version | Scope of approval |
|---|---|---|---|---|
| APR-001 | 24 Oct 2026 | Owner (chat) | HCC_Claude_Execution_Plan_v1.md | Proceed with execution brief; **does not** approve screen structure, hotel policies or production |
| APR-002 | 24 Oct 2026 | Owner (chat) | — | Instruction to build the missing M0 planning package before further prototype work |
| APR-003 | 24 Oct 2026 | Owner (chat: "lets start with M1" after the recommended answers were put to him) | M0 package v1 | Q1 = multi-day walkthrough (DEC-029 option B); Q2 = rename fixture (DEC-037); Q3 sitemap approved; Q4 prototype defaults accepted; Q5 walkthrough approved. Prototype only — not hotel policy |
| APR-004 | 24 Oct 2026 | Owner (chat) | Execution approach | M2 and M3 may be built in parallel by up to 6 agents after M1 completes |
| APR-005 | 24 Oct 2026 | Owner (chat) | `HCC_Multi_Agent_Plan_v1.md`, A2 till decision pack | `git init` of the workspace and launch of Wave 0 (W0-A navigation, W0-B till, W0-C booking restriction UX; W0-D acceptance after merge). Does **not** authorize production construction (A4) |
| APR-006 | 24 Oct 2026 | Owner (chat) | `HCC_Wave05_UI_Placement_v1.md`, `HCC_Master_Configuration_Design_v1.md` | Both designs approved as proposed (DEC-055…DEC-058). Wave 0.5 prototype build after W0-D closes. Does **not** authorize production construction |
| APR-007 | 25 Oct 2026 | Owner (chat) | `02 Planning/agents/W05_Briefs.md` | Start Wave 0.5 now, in parallel with the last W0-D emulator checks, with the maximum practical number of agents and a mix of Opus and Sonnet (overrides the 'after W0-D' order in APR-006). Production construction still not authorized |

## 2. Original decisions from the conversation (preserved IDs)

| ID | Topic | Decision | Status | Src | Affects |
|---|---|---|---|---|---|
| DEC-01 | Audience | Internal hotel/lodging use; not a public commercial PMS | Agreed | C | All |
| DEC-02 | Scope | 26 areas in 3 phases; first 12 = MVP | Agreed (fine-grained acceptance pending) | C | PRD §3 |
| DEC-03 | Database | SQL Server 2025 Express is the authoritative production store | Agreed; schema/performance pending; edition limits **Unverified** | C | M4–M6 |
| DEC-04 | Architecture | Modular monolith + hexagonal + DDD bounded contexts + internal events | Agreed | C | M4 |
| DEC-05 | Backend | NestJS + TypeScript | **Superseded by DEC-054 (24 Oct 2026)** | C | M4 |
| DEC-06 | Frontend | React + TypeScript, responsive | Agreed; design system pending approval | C | M1–M3 |
| DEC-07 | Desktop delivery | Windows-hosted server, browser frontend, optional wrapper | Agreed | C | M6 |
| DEC-08 | Android | Responsive browser/PWA over hotel LAN | Agreed; PWA behaviour pending | C | M6 |
| DEC-09 | SQLite | Not the master DB; possible restricted offline tablet cache later | Agreed conceptually | C | Later |
| DEC-10 | Access | Master Access Control Panel: roles, enable/disable, actions, approvals, scopes | Agreed; matrix = `HCC_RBAC_Matrix_v1.md` (Proposed) | C | Area 12 |
| DEC-11 | UI reference | Kamra (workflow), Open Hotel PMS (UI/housekeeping), QloApps (admin) — inspiration only | Agreed direction | C | Prototype spec §6 |
| DEC-12 | Repo composition | Selective, licence-checked reuse; HAIP backend logic, Open Hotel PMS UI candidates | Conceptual; licence/quality **Unverified** | C | M4 |
| DEC-13 | Workflows first | State machines and exceptions before tables/code | Agreed | C | Workflow spec |
| DEC-14 | Finance | Separate charges, receipts, advances, deposits, settlements, tax, refunds | Agreed in principle | C | Financial rulebook |
| DEC-15 | Configuration | Separate Hotel Configuration Control Panel | Agreed; key inventory Proposed | C | REQ-CFG |
| DEC-16 | DB boundaries | Module-owned tables; no cross-module direct writes | Agreed | C | M4 |
| DEC-17 | Validation | Clickable end-to-end prototype before production | Agreed — current milestone | C | M0–M3 |
| DEC-18 | Prototype tech | React + mock services; browsable on Windows and Android | Agreed concept | C | M1 |
| DEC-19 | Deployment | Local-network-first, no SaaS dependency for core functions | Agreed | C | M6 |
| DEC-20 | Schedule | 8–12 weeks MVP / 14–20 weeks stable (one developer) | Planning estimate only; to be re-estimated after prototype + SQL spike | C | M4 |

## 3. New decisions raised in M0

| ID | Topic | Proposal | Status | Src | Affects |
|---|---|---|---|---|---|
| DEC-021 | Prototype build tool | Keep the existing esbuild single-file bundle (instead of the plan's Vite-style suggestion). Output is a self-contained HTML; `npm start` serving is the documented route. | Proposed | B | M1 |
| DEC-022 | Prototype persistence | Browser `localStorage`, versioned key (`hcc-prototype-v1`), schema check, Reset control. Survives refresh; per browser only; not shared across devices. | Proposed | B, P | AC-024 |
| DEC-023 | Money representation | Integer paise everywhere; per-line tax rounded half-up to the paisa; no floating point. | Proposed | B, P | Financial rulebook |
| DEC-024 | Stay interval | Arrival-inclusive, departure-exclusive dates. Back-to-back stays on the same room are allowed. Day-use is a separate future rule. | Proposed | P | REQ-BOOK-006 |
| DEC-025 | Unpaid departure | Checkout blocked while balance ≠ 0 or deposit held, unless an authorised company-credit approval exists. | Proposed default | C, P | REQ-STAY-004, AC-008 |
| DEC-026 | Inspection | Supervisor inspection required before Ready (configurable; default ON). | Proposed default | P | REQ-HK-003 |
| DEC-027 | Discount threshold | Discounts above ₹250 per request require approval by another user with approval rights (configurable). | Proposed default | B | REQ-FOLIO-003 |
| DEC-028 | Closed-date corrections | Prototype: corrections post as dated adjustments in the open business day, referencing the closed date. Production: choose **adjustment-only** or **controlled reopen** — not both. | **Needs owner decision** (production) | C, P | REQ-CLOSE-006 |
| DEC-029 | Room-night posting timing (**Approved option B, APR-003**) | Night audit posts each night once. The current build also has a "post full stay" action that pre-posts future nights; proposal: remove it and run the walkthrough across simulated days 24→26 Oct. | **Needs owner decision** | B | REQ-CLOSE-003, walkthrough |
| DEC-030 | Deterministic clock | All prototype timestamps (including audit entries) come from the simulated clock, not the real clock. The current build uses the real clock for audit times — to be fixed. | Proposed | P, B | AC-018, AC-024 |
| DEC-031 | Refund authority | Cashier executes a refund only against an approved refund request (approver ≠ requester). The current build lets any user with `approve` refund directly. | Proposed | P, B | REQ-PAY-003 |
| DEC-032 | Prototype role set | Add Accountant, Technical administrator and Maintenance templates to the six already built. | Proposed | C, P | RBAC |
| DEC-033 | Owner protection | Rule = "last **active** owner cannot be disabled or demoted". Multiple owners allowed. The current build blocks disabling any owner. | Proposed | P, B | REQ-ACC-007 |
| DEC-034 | Advance handling | An advance is a receipt allocated to the reservation folio when received; it is never subtracted again at checkout. | Proposed | P | AC-006 |
| DEC-035 | Cash shifts | Prototype: one cashier shift per business day with opening float carried from the previous counted close. Production: multi-shift per day. | Proposed | B | REQ-PAY-004 |
| DEC-036 | LAN preview | `npm start` binds 127.0.0.1 by default; an explicit `--lan` option binds 0.0.0.0 for tablet testing, with firewall notes. | Proposed | B, P | Device testing |
| DEC-037 | Fixture property name (**Approved, APR-003; done in M1**) | Rename the demo property from "Saagar Residency" to "HCC Demo Residency" so fixtures are never mistaken for the real business; keep owner first name only in demo user labels. | **Needs owner decision** (minor) | B, P | Fixtures |
| DEC-038 | Hold expiry | Default hold expiry 2 hours of simulated time; expiry checked on every availability query. | Proposed default | P | REQ-BOOK-004 |
| DEC-039 | Synthetic tax | 10% synthetic tax, applied per line after discount (discount lines carry the same tax rate as negative tax). Not a GST statement. | Proposed (prototype only) | P, B | Financial rulebook |
| DEC-040 | Policy evaluation order | Disabled/expired → deny; scope mismatch → deny; explicit deny overrides grants; no grant → deny; then thresholds/approvals. Multiple roles = union of allows minus any explicit deny. | Proposed | P | RBAC §2 |
| DEC-041 | Approval binding | Approval bound to action + target record + amount + record version; any change invalidates it; execution re-checked. | Proposed | P, B | AC-014 |
| DEC-042 | Business date | Business date changes only through day close → open next day; calendar midnight does not change it. | Proposed | P | AC-018 |
| DEC-052 | Till model (A2.3) | **Approved option A — shared till:** restaurant / room-service cash goes into the front-desk drawer; POS cash sales and voids count once in the cashier's expected cash; room-service charged to a folio is a charge, not cash; open POS checks block day close. | Agreed (O, APR-005) | O | `HCC_A2_Till_Decision_Pack.md`, W0-B |
| DEC-053 | Stay-restriction override (A2.1) | Min-stay / closed-to-arrival restrictions are enforced for every booking source; users with the approve permission (Owner, Manager) may override with a reason, recorded on the booking and in the audit log. | Agreed (O, APR-005) | O | W0-C, contracts package §7 |
| DEC-054 | Production server stack | **.NET 10 (ASP.NET Core Web API, C#) + SQL Server 2025 Express**, reusing the owner's ETP Reporting Engine platform code (migrations with backup gate, backups/restore drills, Task Scheduler operations, installer, Excel/PDF/e-mail, Tally export) with provenance; React web + Capacitor APK stay as the clients. Supersedes DEC-05. Production construction still needs the A4 go-ahead. | Agreed (O, 24 Oct 2026) | O | `HCC_FirstParty_Reuse_ETP_SCC_v1.md`, contracts package §5, multi-agent plan Wave 1 |
| DEC-055 | Wave 0.5 placement | Payroll/leave upgrade the Staff and payroll tabs; payslip in My shifts and pay; **Cash book** = new page under Cash and billing; **Compliance calendar** = new page under Accounts and reports, visible to **Owner and Accountant** only, plus a Dashboard "Due this week" card; Excel export, day-pack PDF and a simulated Tally tab from ETP | Agreed (O, APR-006) | O | `HCC_Wave05_UI_Placement_v1.md` |
| DEC-056 | Payslip delivery | Payslip PDF through the native share sheet **plus a simulated WhatsApp send** (as in SCC); no real messaging in the prototype | Agreed (O, APR-006) | O | Wave 0.5 payroll |
| DEC-057 | Master configuration | One settings hub named **Master configuration** (renames Hotel configuration; page id stays `settings`), 12 groups, search and summaries, single settings registry with dated values, approvals and history; masters held once; module switch-off for restaurant/POS, groups, OTA channels, booking site, messaging, document capture, assistant, linen/stock, maintenance, payroll (core modules always on; data kept when off) | Agreed (O, APR-006) | O | `HCC_Master_Configuration_Design_v1.md` |
| DEC-058 | Who changes configuration | **Owner** changes all groups; **Tech admin** may change non-money groups (branding, templates, modules on/off, housekeeping settings, data and system). Money, tax, payroll, compliance and approval limits stay Owner-only; never self-approve | Agreed (O, APR-006) | O | Master configuration, RBAC |
| DEC-059 | Tablet portrait header (T-01) | Keep the **compact header** (≈85–97 px, ≈8 % of portrait height) as built; re-check on the Tab S7 and move to a single-row header with a user menu only if it still feels cramped. Options: `02 Planning/options/T-01_T-02_Decision_Brief.md` | Agreed (O, APR-008, 25 Oct 2026) | O | W0-D T-01 |
| DEC-060 | Touch size on tablets (T-02) | On touch (coarse-pointer) devices: dense table rows and filters **44 px**, primary actions, hamburger, drawer items and dialog buttons **48 px**, built on CSS variables (a density toggle can be added later). Windows desktop (fine pointer) unchanged | Agreed (O, APR-008, 25 Oct 2026) | O | W0-D T-02, APK v9 |
| DEC-061 | Own pay for non-money roles (R-01) | Housekeeping and maintenance staff **may see their own pay and payslip** on "My shifts & pay"; they still never see hotel money (rates, folios, cash, revenue, other staff pay) | Agreed (O, APR-008, 25 Oct 2026) | O | RBAC, W05-B |
| DEC-062 | Production staff sign-in | **PIN + device binding** for staff on shared hotel tablets/phones; **password per user** (PBKDF2-SHA256) for Owner, Accountant and Tech admin, who may not sign in by PIN. Resolves contracts §8 Q4 | Agreed (O, APR-009, 25 Oct 2026) | O | Wave 1 P1 |
| DEC-043 | Source reuse in prototype | Prototype uses original components only; no third-party code imported, so no Source Reuse Register entries yet. | Proposed | P | Reuse register |

## 4. Superseded / changed decisions

| Old | New | Date | Impact |
|---|---|---|---|
| DEC-05 NestJS + TypeScript backend | DEC-054 .NET 10 + ETP platform reuse | 24 Oct 2026 | Production server in C#; prototype business rules are ported with the prototype tests as golden figures; Node SQL-driver spike no longer needed (Microsoft.Data.SqlClient, as ETP); T-SQL spike results unchanged |
