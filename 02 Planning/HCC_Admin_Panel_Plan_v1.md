# HCC Administration Panel: module switches and custom roles (plan v1)

2 Nov 2026 · Status: **Approved 2 Nov 2026 (APR-028): all §7 recommendations accepted, DEC-214…221. Build wave W2c `claude/adm`, migrations 4800–4849.** · Builds on DEC-040, DEC-057, DEC-058, DEC-062, DEC-063, DEC-079 and the RBAC matrix v2.

**In one line:** one Owner page, **Administration**, where the owner (super admin) switches modules *and their sub-functions* on or off, creates named roles such as "Day shift manager" or "Night shift manager" from the built-in ones, decides what each role may see and do, module by module, and creates the users that hold those roles. Everything is enforced on the server, not only hidden in the menu.

---

## 1. What exists today (facts from the code)

| Area | Prototype (`03 Prototype`) | Server (`08 Production`) |
|---|---|---|
| Roles | Fixed list of 10 in `domain/types.ts` (Owner, Manager, Reception, Cashier, Supervisor, Housekeeper, Accountant, Maintenance, Tech admin, Restaurant) | `plat.role` table, 10 seeded codes; `RoleCodes.All` hard-coded in C# |
| Permissions | Two layers: 13 coarse module gates (`can()`), and the 45-row matrix v1 with role templates, limits, scopes | ~151 `PERM-<MOD>-nnn` ids (matrix v2, DEC-079) in `plat.permission`, grants in `plat.role_grant`, all seeded by SQL migrations |
| Editing grants | `GRANT`, `POLICY_SET` commands exist in the mock | **No command edits `plat.role` or `plat.role_grant`.** Role templates change only by migration |
| Per-user overrides | `USER_EXCEPTION` (allow/deny, reason, expiry) | `plat.user_exception`, `GrantException` / `RevokeException` |
| Users | `USER_CREATE` / `USER_UPDATE`; one role per user | `plat.app_user` + `plat.user_role` (several roles per user, validity window), PIN or password by role (DEC-062) |
| Module switch-off | 10 optional modules in `config.ts` (POS, groups, OTA, website, messaging, documents, assistant, inventory, maintenance, payroll); core modules always on | Same 10 in `ConfigCatalog.cs`; `ModuleGate` refuses commands with MODULE_OFF; reads stay open |
| Sub-function switches | **None.** "Bookings inside Front desk" cannot be switched off today because Front desk is core | None |
| Admin UI | Access control page (`features/admin/AccessControl.tsx`), Master configuration › Modules on/off | `/admin/users`, `/roles`, `/exceptions`, `/audit` reads |

**How many modules:** 10 navigation categories, 41 pages (+ Dashboard), 10 switchable modules, planning specs numbered up to module 27. The "16 or 17" the owner remembers is closest to the **feature count in §3** (18 modules, 60-odd sub-functions).

**The gap, in two sentences.** Switching is all-or-nothing per optional module, and core modules have no switch at all. Roles are a closed list, and on the server nothing but a migration can change what a role may do, so a "Day shift manager without Accounts" cannot be made without a developer.

---

## 2. Design

### 2.1 Three layers, each one simple

1. **Feature catalogue** (code, not editable): every module lists its sub-functions, and each sub-function names its pages, its command prefixes and its permission ids. One file in the prototype (`domain/feature-catalogue.ts`) and one in C# (`FeatureCatalog.cs`), kept equal by a golden test.
2. **Switches** (data, Owner / Tech admin): `feature.<module>` and `feature.<module>.<sub>` dated settings in the existing settings registry (DEC-057). Off = page hidden, commands refused with MODULE_OFF, data kept. A sub-function is off if its module is off.
3. **Roles and grants** (data, Owner only): a role = a name + a *base kind* + a set of grants per sub-function (View / Act / Approve / Reveal money, exactly the matrix v2 numbering). Users hold roles. Per-user exceptions stay as today.

Effective access for a user and a sub-function = switch on **and** role grant **and** no deny exception (DEC-040 order unchanged).

### 2.2 Switches: what "disable bookings in Front desk" means

| Level | Example | Effect when off |
|---|---|---|
| Module | Restaurant & POS | As today (DEC-057): pages hidden, `POS_*` commands refused, data kept |
| Sub-function | Front desk › Reservations | Reservations and Calendar pages hidden, `RES_*` create/amend commands refused, walk-in check-in still works, existing bookings still visible on Arrivals (read-only), OTA pull parks new bookings with a notice |

**Always on (cannot be switched off):** sign-in, check-in / check-out, folio and payments, day close, Access control, Master configuration, audit, backup, System health. Locking these would break money or the law (GST invoice, day close). The catalogue marks them `core: true` and the UI shows the switch greyed with the reason.

**Dependencies:** a sub-function may require another (Invoices requires Folios, OTA payout requires OTA). Switching a parent off switches dependants off, with a confirm listing them.

### 2.3 Roles: built-in, custom, and the super admin

- **Owner = super admin.** Always holds everything; cannot be edited, disabled or copied with less. Exactly as today (the GRANT handler already refuses touching the Owner template).
- **Built-in roles** (the 10) stay as read-only templates. They keep their seeded matrix v2 grants so nothing changes on day one.
- **Custom role** = "Start from &lt;built-in role&gt;, then adjust". Example: *Day shift manager* = Manager − Accounts & reports; *Night shift manager* = Manager + Accounts (view) + Day close. Stored as its own `plat.role` row (`role_code` becomes a 3-to-8-char code, `is_custom`, `base_role`) with its own `role_grant` rows. The base kind decides the sign-in method (PIN vs password, DEC-062) and the money invariant below.
- **Money invariant (DEC-061 / DEC-063 G12, kept):** a custom role based on Housekeeper, Supervisor, Maintenance or Restaurant can never be given a money-flagged permission (except own pay HR-004). The editor hides those cells; the server refuses the grant.
- **Separation-of-duty rules stay non-configurable** (matrix v1 §8: no self-approval, Manager ₹1,000 / Reception ₹250 limits, Owner-only credit-note approval). A custom role may be narrower than its base, never wider on an SoD rule.
- **No lock-out:** the server refuses a change that would leave no enabled Owner, and a user cannot edit their own role or their own grants.
- **Role × sub-function grid** is what the owner edits. Under the hood a grid cell maps to the 1–4 matrix v2 permission ids of that sub-function, so the ~151 ids never have to be shown. An "Advanced" toggle shows them for the Tech admin.

### 2.4 Users

Create user → name, role(s), sign-in kind (from the role), PIN or temporary password with must-change (already built in Wave C), validity window, device binding (existing). Several roles per user already exist on the server; the panel shows the **effective access preview** per user (backlog BL-025) so the owner can check "can the night manager open Accounts?" before saving.

### 2.5 The Administration page

Owner page under Admin & Setup, replacing today's Access control page (page id `access` stays so bookmarks and tests survive) with four tabs:

| Tab | Who | Contents |
|---|---|---|
| Modules & features | Owner, Tech admin (DEC-058) | Tree: 18 modules › sub-functions, switch per row, dependants shown, "why not switchable" on core rows |
| Roles | Owner | Role list (built-in greyed, custom editable), New role from base, grid role × sub-function with View / Act / Approve / Money cells, limits (existing `PolicyLimitEditor`), Advanced = permission ids |
| Users | Owner (Tech admin may create non-money users, DEC-058) | Existing users list + create / edit, roles, credentials, devices, exceptions, effective-access preview |
| Audit | Owner, Tech admin | Every switch, role and grant change with who / when / before / after (policy version bump as today) |

Phone shell: same tabs as cards; the grid becomes one card per sub-function with four toggles (48 px targets).

---

## 3. Feature catalogue (first cut, 18 modules)

Core sub-functions marked **(core)** cannot be switched off. Permission ids per the matrix v2 codes already seeded.

| # | Module | Sub-functions |
|---|---|---|
| 1 | Front desk | Room board (core), Reservations, Calendar, Arrivals & check-in (core), Guests register (core), Room keys |
| 2 | Cash & billing | Folios (core), Cashier & payments (core), Cash book, Expenses, Invoices & credit notes (core) |
| 3 | Restaurant & POS | Outlets & checks, Room service, Menu & prices |
| 4 | Housekeeping | Board & tasks, Inspections, Complaints, Lost & found |
| 5 | Maintenance | Work orders, Room downtime |
| 6 | Guest services | Requests, In-house list, Handover notes |
| 7 | Messaging | Guest messages, Templates |
| 8 | Document capture | ID capture, Show ID |
| 9 | Customers | Customer 360, CRM, Legacy stays |
| 10 | Revenue & sales | Yield & tariffs, Groups & companies, OTA channels, OTA payouts, Direct booking site |
| 11 | Stock & purchasing | Linen & stock, Purchases & vendors, Asset register |
| 12 | People & payroll | Staff & shifts, Leave, Payroll, My HR (core, own records) |
| 13 | Day closing | Day close (core), Closing reports |
| 14 | Accounting | Vouchers & ledgers, Trial balance / P&L / balance sheet, GST set-off & month lock, Tally export, TDS, Loans |
| 15 | Reports | Operations reports, Finance reports, Exports |
| 16 | Compliance | Calendar & obligations, Firm profile, Evidence |
| 17 | Management assistant | Assistant |
| 18 | Admin & setup | Administration (core), Master configuration (core), Masters, Approvals, Backup (core), System health (core), Appearance (core), Legacy import, Roadmap |

Hall booking (HB) and split folio (SF) are added to the catalogue when their waves land.

---

## 4. Build waves

| Wave | Scope | Lane / model | Proof |
|---|---|---|---|
| **ADM-0 Rulebook** | Owner answers §7; catalogue in §3 agreed; page mock-up | Lead (Fable) | Decision register entries DEC-2xx |
| **ADM-1 Feature catalogue + sub-function switches** ✅ built 2 Nov 2026 on `claude/adm` (cloud session; prototype tests green, server part compiles and runs its gate on the hotel PC) | Catalogue files (TS + C#) with golden equality test; `feature.*` settings; `ModuleGate` and `cfgPageOn` generalised to sub-functions; dependants; navmap filter; Modules & features tab | Opus (server), Sonnet (UI, tests) | Every page and command prefix is in the catalogue (test, like `PAGE_CATEGORY`); switch off Reservations → page hidden, `RES_CREATE` refused on both sides |
| **ADM-2 Roles as data** | Server commands `RoleCreate / RoleUpdate / RoleGrantSet / RoleDelete` (Owner, `PERM-ADM-002`), `is_custom` + `base_role` columns, money and SoD invariants in `AccessEvaluator`, no-lock-out rule, audit rows; prototype mirror (`ROLE_CREATE` …, `Role` type becomes string + base kind); sync adapter; migration 48xx + lock | Opus | Table-driven test: custom "Day shift manager" refused on every Accounting endpoint, "Night shift manager" allowed on view only; invariant tests fail the build |
| **ADM-3 Administration page** | Four tabs, role × sub-function grid, user create/edit with effective-access preview, phone shell, Ctrl+K entries | Sonnet (UI) + Opus (review) | Phone smoke; RBAC golden grid regenerated; prototype + server tests green |
| **ADM-4 Gate** | Full gate on the hotel PC (`tools/Start-Gate.ps1`), HANDOFF, APK (next versionCode after v19) | Lead | Gate PASS, evidence under `05 Delivery Evidence/Admin/` |

Size: roughly the same as one accounting wave (ACC-2). Suggested slot in `HCC_Build_Plan_v2`: **after APK v19 and LF, before HB** (HB and SF then register straight into the catalogue). The owner may pull it earlier if go-live staffing needs the shift-manager roles first.

---

## 5. Compatibility and risks

- **Day one, nothing changes:** built-in roles keep their seeded grants; all switches default to on; the 10 existing module switches become the module rows of the new tree (same setting keys kept, `feature.*` added).
- **Direct role checks in code** (about 8 `"OWN"` string checks on the server, `navRoleOk`, `EXP_VENDOR_PAY_ROLES`, channels handler) must move to permission checks, otherwise a custom role based on Manager would silently lose them. ADM-2 lists and converts each one (a test fails on any remaining role-string check outside the evaluator).
- **Prototype role type:** `Role` is a closed union used widely; making it a string with a `baseKind` touches fixtures and tests. Done in ADM-2 behind the existing `restoreState` migration; SCHEMA bump to 13.
- **Restaurant role** is production-only (DEC-079 D1); the panel shows it only when the server is the backend.
- **Audit:** every change is a versioned policy row (already the pattern), so the CA / auditor can read who switched Accounts on for the night manager and when.

---

## 6. What this does not do

- No per-page permissions beyond the sub-function (the matrix v2 View / Act / Approve / Money cells are the finest grain shown).
- No role hierarchy or inheritance at run time (a custom role is a copy at creation; later changes to the base do not flow down, so the owner is never surprised).
- No multi-property roles (single property, as everywhere in HCC).

---

## 7. Decisions for the owner (all recommended answers accepted, APR-028 → DEC-214…221)

| # | Question | Recommended answer |
|---|---|---|
| Q1 | Grain of switches: module + sub-function (as in §3), or also single pages / single actions? | **Module + sub-function.** Pages and actions follow the sub-function; finer than that becomes unreadable |
| Q2 | Agree the always-on list in §2.2 (check-in/out, folio, payments, day close, access, configuration, audit, backup, health)? | **Yes** |
| Q3 | Custom role = copy of a built-in base role, adjusted, with the base deciding sign-in kind and the money rule? | **Yes** (keeps DEC-061/062/063 intact) |
| Q4 | May a Manager-based custom role be given **Accounts (view)** and **Day close** (night manager case)? | **Yes**, view and act; approve / money cells in Accounting stay Owner + Accountant unless the owner grants them explicitly, with a warning |
| Q5 | Who edits roles and users: Owner only, or Owner + Tech admin for non-money roles (DEC-058 pattern)? | **Owner only for roles; Tech admin may create users and set credentials** |
| Q6 | Do SoD rules (no self-approval, discount limits, Owner-only credit notes) stay non-configurable in the panel? | **Yes** |
| Q7 | Placement in the build order: after LF and before HB, or earlier? | **After LF, before HB**; pull forward only if staffing needs it |
| Q8 | Should the prototype's demo roles also become editable (today the demo mirrors only matrix v1, DEC-079 D6)? | **Yes for ADM-2 onward**, because the panel must be demonstrable offline |
