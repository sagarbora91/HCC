# HCC master configuration panel — design (proposal)

24 Oct 2026 · Status: **proposed, owner to confirm** · Part of Wave 0.5 (with `HCC_Wave05_UI_Placement_v1.md`). Mockup shown in chat 24 Oct.

## 1. Why
The owner wants one place to customise the software. Today HCC has a good base: `Admin & Setup › Hotel configuration` uses one settings registry (`AdminSettingDef`: key, category, type, unit, editable, approver, effective, help), with dated tariff and tax schedules. But several settings live inside their own modules: yield guardrails (`YLD_GUARD_SET`), payroll rates (`HR_RATES_SET`), assistant thresholds (`AI_SETTINGS`), guest-service SLAs (`GSV_SETTINGS`), POS menu toggles, group credit limits, message templates and accounting mapping. Two more sets are coming: statutory rates and compliance obligations from SCC.

## 2. Lessons from ETP and SCC (turned into rules)

| Seen in | Problem | HCC rule |
|---|---|---|
| ETP UI audit (Sep 2026, `06-ui-ux.md`) | The same screen had 3–4 names ("Admin / Settings / Administration / Masters"); spelling and case were inconsistent | **One name:** "Master configuration" (page id `settings`, search keywords cover "settings", "admin", "masters", "setup"). Group names are fixed in the registry and tested. |
| ETP | Settings, Accounting and Registers forms didn't fit narrow screens | Phone/tablet layout is designed, not shrunk: list → details on phone, list beside details on tablet and desktop. 48 px targets, no sideways scroll. |
| ETP | Non-owners were silently redirected away from Settings | Everyone sees the list. Groups they can't change are shown **read-only with the reason** ("Owner changes this"), never silently hidden or redirected. |
| ETP | Raw technical error text reached users | Plain validation messages per field; typed error codes as everywhere in HCC. |
| SCC (C1 settings redesign, Aug 2026) | A horizontal settings tab bar got cut off and was hard to reach on phones | Grouped vertical list with **search** and a **live summary** per group (e.g. "22 of 26 on", "2 changes pending"); Android Back goes details → list → previous page. |
| SCC | Each module had its own "add employee / vendor / brand", so masters were entered twice and drifted | **Masters live once**: rooms, room types, rate plans, staff, departments, vendors, companies, outlets/menu, payment methods, tax and statutory rates, document numbering, templates. Modules pick from them; module pages link to the master instead of their own add forms. |
| SCC | PIN locks needed safe defaults | Security options are **off by default** and switched on deliberately; changes need the Owner. |
| SCC / ETP | Rates and rules changed over time; past records must not move | Money, tax and statutory values are **dated** (effective-from). A change never alters posted records (HCC already does this for tax and tariff). |

## 3. Structure (12 groups)
**Business setup:** Property and branding (name, address, GSTIN, logo, invoice header/footer, print format, language) · Rooms and rate plans (room types, rooms, plans, seasons, restrictions) · Front-desk policies (check-in/out times, hold expiry, deposit, ID requirements, no-show) · Money and tax (dated tax schedule, discount and approval limits, payment methods, document numbering, till model) · Restaurant and POS (outlets, menu, KOT).

**Operations and people:** Housekeeping and maintenance (inspection on/off, checklists, dirty-room threshold, preventive plans) · People, payroll and leave (departments, dated PF/ESIC/PT rates, leave policy, weekly offs, max staff off) · Compliance obligations (GST/TDS/PF/ESIC/PT list, due rules, CA contact).

**Software and security:** Modules on/off · Users, roles and PINs (links to Access control) · Messages and documents (templates, consents) · Data and system (backup schedule, restore drill, storage, diagnostics, about/version).

## 4. Behaviour of every setting (one registry for all modules)
Each entry declares: key, group, label, type and unit, validation, default, **who can see / who can change**, **approver** (none / Manager / Owner, never your own request — DEC-041), **effective** (immediately / from a date / next business day), **what it affects** (shown before saving), audit text, and whether it is **dated**. Save = one command (`SETTING_SET` extended) → approval if required → audit entry → history with "revert to previous value". Module-specific commands become thin wrappers over the same registry, so the old pages keep working and link to the hub.

**Modules on/off** ("customise the software"): turning a module off hides its tiles, menu items, search entries, reminders and phone tabs, and the server refuses its commands. Its data and history stay. Core modules (front desk, folios, cashier, closing, access, configuration) can't be turned off.

**Export/import of configuration** (JSON, secrets excluded) for backup and for setting up a second property later.

## 5. Production (.NET, DEC-054)
Same registry as a `config_setting` / `config_setting_value(effective_from, value, approved_by, version)` table pair. Values are read through one `IConfiguration`-style service with caching per business date. Masters are module-owned tables, edited only through their module commands. Fits contracts package §4 rules (dated values, audit and outbox in the same transaction).

## 6. Owner questions
1. Who may change configuration: **Owner only**, or Owner plus Tech admin for non-money groups (branding, templates, modules on/off)?
2. Which modules should be switchable off? Proposed: restaurant/POS, groups, OTA channels, booking site, messaging, document capture, assistant, linen/stock, maintenance, payroll.
3. Keep the page name "Hotel configuration" or rename it "Master configuration"?
