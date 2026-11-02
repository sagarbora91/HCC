# ADM-2…4 lane briefs and contract (2 Nov 2026)

Plan: `02 Planning/HCC_Admin_Panel_Plan_v1.md` (approved APR-028, DEC-214…221). ADM-1 is done (feature catalogue + sub-function switches, `claude/adm`). This file is the contract every lane codes against. Lanes: **S** server roles-as-data (Opus), **P** prototype domain + sync (Opus), **U** Administration page (Sonnet, after P), **G** ADM-4 evidence (lead). Branch `claude/adm`; commit and push after each lane (owner rule 2 Nov 2026).

## 0. Decisions that bind every lane

| Rule | Source |
|---|---|
| Owner is the super admin: always everything; never editable, never a base for a custom role | DEC-216 |
| Built-in roles (OWN MGR REC CSH ACC SUP HKP MNT RST TAD) are read-only templates; nothing changes for them | DEC-216 |
| A custom role = a **copy of one built-in base role at creation, then adjusted**; later changes to the base never flow down | DEC-216 |
| Base decides the sign-in kind (password for ACC/TAD bases; PIN otherwise; OWN cannot be a base) and the money rule | DEC-216, DEC-062 |
| Money invariant: a role based on HKP / SUP / MNT / RST never holds a money-flagged permission except own pay `PERM-HR-004` | DEC-061, DEC-063 G12 |
| SoD stays non-configurable: no self-approval, discount limits REC ₹250 / MGR ₹1,000, Owner-only credit-note approval. A custom role may be narrower than its base on these, never wider | DEC-219 |
| Roles edited by the **Owner only**; Tech admin may create users, assign non-money roles, set credentials | DEC-218 |
| A user never edits their own role or grants; the server refuses a change that leaves no enabled Owner | DEC-218 |
| Manager-based roles may hold Accounts (view / act) and Day close; approve / money cells in Accounting stay Owner + Accountant unless granted explicitly, with a warning | DEC-217 |
| Prototype demo roles become editable too (prototype `Role` stays the built-in union; custom roles live beside it) | DEC-221 |
| Every change is a versioned, audited policy change (who / when / before / after) | plan §2.5 |

## 1. The grid (what the Owner edits)

Role × **sub-function** (feature catalogue, ADM-1) with one access level per feature and one money flag per role:

| Level | Meaning |
|---|---|
| `off` | pages hidden, commands refused for users holding the role (same shape as a feature switch: FORBIDDEN, reason `MODULE_OFF`, `featureId`, plus `roleCode`) |
| `view` | pages visible, commands refused ("view only") |
| `full` | the base role's behaviour for that feature; if the base had no access, the feature's permissions are **added** (view + act, never approve / money) |
| `money` (per role, boolean) | whether money-flagged permissions of the base stay granted; forced `false` and locked for HKP / SUP / MNT / RST bases |

Core features (`core:true`) have no row: they follow the base role unchanged.

**Feature → permission ids.** The catalogue gains `permPrefixes` per feature (`['PERM-RES-']` …), the one place both sides read. `full` on a feature the base lacks grants its `-001` (view) and `-002` (act) ids property-wide (`A`/`P`); `view` grants `-001` only; `off` removes every id with those prefixes. `approve` (`-003`) and `reveal money` (`-004`) are never added by the grid; the Advanced editor (existing POLICY_SET / RoleGrantSet) may, with the DEC-217 warning.

## 2. Server contract (lane S)

### 2.1 Schema — migration `4800_roles_as_data.sql` (+ `tools/migrations-lock.mjs --add`)
- `plat.role` + `is_custom BIT NOT NULL DEFAULT 0`, `base_role VARCHAR(3) NULL REFERENCES plat.role`, `created_by INT NULL`, `created_utc DATETIME2(3) NULL`, `retired_utc DATETIME2(3) NULL`. Custom role codes are `C01`…`C99` (fits the existing `VARCHAR(3)` key; no FK rewrite).
- `plat.role_feature(role_code VARCHAR(3) FK, feature_id VARCHAR(40), access VARCHAR(4) CHECK IN ('OFF','VIEW','FULL'), PRIMARY KEY (role_code, feature_id))`. Absent row = `FULL`.
- `plat.role` + `money BIT NOT NULL DEFAULT 1`.
- `plat.policy_version(property_id, version INT, changed_by, changed_utc, summary NVARCHAR(200), before_json, after_json)` for the audit tab (or reuse `plat.audit` rows with action `ROLE_*` if that already carries before/after; lane S decides, documents it).

### 2.2 Commands (`/api/v1/commands/<name>`, Owner only = `PERM-ADM-002` + role check `OWN` in the session)
| Command | Body | Rules |
|---|---|---|
| `CreateRole` | `{name, baseRole, money?}` | base ∈ built-ins except OWN; copies base `role_grant` + `role_deny`; `credential_kind`, `owner_only_assign` from base; money forced false for HKP/SUP/MNT/RST; name unique (case-insensitive), 2–40 chars |
| `UpdateRole` | `{roleCode, name?, money?, expectedVersion}` | custom only; money=false removes `is_money=1` grants except HR-004; money=true restores the base's money grants |
| `SetRoleFeature` | `{roleCode, featureId, access}` | custom only; non-core feature; applies §1 grant rules; `off`/`view` on a feature whose dependants are `full` → those drop to the same level (same closure as ADM-1) |
| `SetRoleGrant` | `{roleCode, permissionId, mode?, scope?, limit?, limitUnit?}` (mode null = remove) | custom only (Advanced); money invariant; SoD list (`PERM-APR-001` limit may only go down from the base, `PERM-FOL-003` limit ≤ base, `PERM-FOL-005`/credit-note approve never added) |
| `RetireRole` | `{roleCode}` | custom only; refused while any enabled user holds it |

Every command: refuses when the actor holds the role being edited; bumps the policy version; audit row `ROLE_<VERB>` with before/after text.

### 2.3 Evaluation
- `SqlAccessPolicy` snapshot: `Roles` = held codes **plus the base code of each held custom role** (so `mine.Contains("MGR")`-style checks keep working for a Manager-based role); grants / denies are loaded for the held codes only (a custom role has its own copies).
- `RequireHccFeature(id)` additionally refuses when **every** held role has `role_feature = OFF` or `VIEW` for the feature (a user with two roles gets the union). New `IFeatureAccess.LevelAsync(propertyId, principalId, featureId)` → `off|view|full`; `GET /me/capabilities` returns `features: {featureId: level}` for non-full entries so the client hides pages.
- `AssignRole` accepts custom codes; `RoleCodes.PasswordRoles` check uses the base for custom roles; `owner_only_assign` from the row.
- `GET /admin/roles` adds `isCustom`, `baseRole`, `money`, `features` (non-full), `version`; built-ins unchanged.

### 2.4 Tests (`Hcc.Access.Tests` + `Hcc.Configuration.Tests`)
Day-shift manager (MGR base, `accounting.*` off) is refused on every Accounting command route and sees no accounting feature in capabilities; night-shift manager (MGR base, accounting `view`, `closing.dayclose` full) is refused on `SaveManualVoucher` (view) but allowed on `RunBusinessDayClose`; HKP-based role can never gain `PERM-RPT-002` (money invariant fails the command); last enabled Owner cannot lose OWN; actor cannot edit own role; built-in role edits refused; retire refused while held; grants copied at creation and unchanged when the base's seed changes (test edits the base grant directly and re-reads the custom role).

## 3. Prototype contract (lane P)

### 3.1 State (`SCHEMA` 13 → 14, new optional slice, old saves load unchanged)
```ts
// src/domain/streams/roles.ts
export type RoleAccess='off'|'view'|'full';
export type CustomRole={id:string;        // 'C01'…
  name:string;base:Role;                 // base ∈ roles minus 'Owner'
  money:boolean;                         // forced false + locked for Housekeeper/Supervisor/Maintenance/Restaurant
  features:Record<string,RoleAccess>;    // non-full entries only, full feature ids ('accounting.vouchers')
  grants:Permission[];                   // coarse module grants (copied from s.grants[base] at creation, then adjusted by the grid)
  template:Record<string,PolicyCell>;    // matrix cells (copied from policy.templates[base]); Advanced edits land here
  limits:Record<string,number>;
  retired?:boolean;createdBy:string;createdOn:string;version:number};
export type ROLState={roles:CustomRole[];history:PolicyChange[]};   // s.rolesX
```
`User` gains `customRole?:string` (the `CustomRole.id`); `User.role` stays the **base** so every existing `u.role==='Manager'` check, `s.grants[u.role]`, `templates[u.role]`, landing pages and the phone shell keep working.

### 3.2 Pure helpers (`roles.ts`)
`rolCustom(s,id)`, `rolOfUser(s,uid)`, `rolEffectiveGrants(s,u):Permission[]` (custom → `grants`, else `s.grants[u.role]`), `rolEffectiveTemplate(s,u)`, `rolFeatureLevel(s,uid,featureId):RoleAccess` (`full` for core features and users without a custom role), `rolCanBeBase(role)`, `rolMoneyLocked(base)`, `rolFeaturePerms(featureId):{coarse:Permission[];ids:string[]}` (coarse from the nav items of the feature's pages via `app/nav.ts` is a UI import — instead put a `coarse` list per feature module in the catalogue: see §3.5), `rolPreview(s,roleId)` (effective access preview = pages visible, features by level, money), `rolSodBlock(role,permId,cell)`.

### 3.3 Wiring
- `can(s,uid,p)` → reads `rolEffectiveGrants`. `authorize()` → `rolEffectiveTemplate` / limits. `rbac.ts` `roleOf` unchanged (base).
- `cfgPageOn(s,page)` stays property-level; add `cfgPageOnFor(s,uid,page)` = `cfgPageOn && rolFeatureLevel(s,uid,featureOfPage(page))!=='off'`; `navFilterModules(s,items,uid)` uses it when `uid` is given.
- `cfgBeforeCommand(s,cmd)` stays property-level; `execute()` additionally refuses a command whose feature is `off` or `view` for the actor: message `"<Feature> (<Module>) is not part of your role <name>."` code `FORBIDDEN`. Core features never. The ADM-1 walk-in exemption applies to `off` too.
- Commands (`handlers/roles.ts`, stream prefix `ROL_`): `ROL_CREATE {name,base,money?}`, `ROL_UPDATE {id,name?,money?}`, `ROL_FEATURE_SET {id,feature,access}`, `ROL_GRANT_SET {id,perm,effect|'none',scope?,limit?}` (Advanced; reuse the POLICY_SET validation), `ROL_RETIRE {id}`, `USER_CREATE`/`USER_UPDATE` accept `customRole`. Gate: Owner only (`PERM-ADM-002` allow + `user.role==='Owner'`); never the actor's own role; refuse retiring a held role; bump `rolesX.history` + `policy.version`.
- Invariants as tests: money lock, SoD, last-owner (unchanged), self-edit, base copy frozen.
- Fixture: seed two fictional custom roles in `fixtures/ext/roles.ts` — "Day shift manager" (Manager, accounting.* off) and "Night shift manager" (Manager, accounting.vouchers/gst/tds/tally `view`) — and one user each (names fictional), so the demo and the page have data. Keep BASE tests' counts in mind (`seed()` users list is asserted in places: add users only in FY26/ext seed, or update the asserting tests).
- Sync (`clients/sync/admin.ts` + `capabilities.ts`): map `ROL_*` → server commands in §2.2 (`CreateRole`, `UpdateRole`, `SetRoleFeature`, `SetRoleGrant`, `RetireRole`), read custom roles from `GET /admin/roles` (`isCustom`, `baseRole`, `money`, `features`) into `rolesX`, and `features` from `/me/capabilities` into the current user's effective levels.
- Tests `tests/stream-roles.mjs` (`[ADM2]`): mirror §2.4 on the mock.

### 3.4 ADM-1 catalogue additions (lane P owns `feature-catalogue.ts`; regenerate the golden; lane S mirrors in `FeatureCatalog.cs` — coordinate: P commits the JSON first)
Each feature gains `permPrefixes:string[]` (matrix v2 module prefixes, e.g. reservations `['PERM-RES-']`, folios `['PERM-FOL-']`, cashier `['PERM-PAY-','PERM-SHF-']`, expenses `['PERM-EXP-']`, accounting.* `['PERM-ACC-']` …) and each module gains `coarse:Permission[]` (the prototype coarse permissions its pages need, e.g. frontdesk `['reservations','checkin']`, cash `['billing','payments','checkout']`, accounting `['reports']`, admin `['admin']`). Golden JSON carries both.

## 4. Administration page (lane U, after P)
Replace `features/admin/AccessControl.tsx` content with an **Administration** page (page id stays `access`; nav label "Administration"; keywords: admin, roles, users, modules, features, audit, access control). Four tabs:
1. **Modules & features** — reuse `CfgModulesPanel` + `CfgFeaturesTree` (ADM-1) in place; link to Master configuration for the rest.
2. **Roles** — list: built-ins (greyed, "Built-in · read-only") then custom (name, base, money, users holding it, version). "New role" (name, base picker without Owner, money toggle locked for the four bases). Role editor = grid: one row per non-core feature grouped by module: level segmented control Off / View / Full, with the module's `switch`/feature state shown when the property switch is off ("switched off for everyone"); money toggle; "Advanced: permission ids" disclosure → existing `PolicyLimitEditor` + cell editor against `template` (POLICY_SET-style → `ROL_GRANT_SET`), DEC-217 warning on approve/money cells in Accounting; Retire with confirm. Effective-access preview panel (`rolPreview`).
3. **Users** — existing users list + create/edit (role picker lists built-ins and custom roles, base shown; sign-in kind from base), enable/disable, expiry, credentials (`CredentialsPanel` in production), exceptions (existing), effective-access preview per user.
4. **Audit** — `policy.history` + `rolesX.history` merged, newest first, version · who · when · summary · before → after.
Phone shell: same tabs as a segmented bar; grid rows become cards with three 48 px buttons. Desktop fit: the grid scrolls inside its panel (DEC-213). Tests: `tests/stream-admin-ui.mjs` renders each tab for Owner / Tech admin / Manager (Manager gets "Owner only" on Roles) with react-dom/server as other UI tests do; navmap/search entries; phone smoke list updated.

## 5. ADM-4 (lead)
Evidence `05 Delivery Evidence/Admin/ADM-DONE.md`: what was built, test counts, the hotel-PC checklist (`dotnet build Hcc.slnx`, `dotnet test` serial, `tools/Start-Gate.ps1`), open items. HANDOFF + plan §4 ticks + build plan row. APK bump is left to the next APK wave (v19 prep exists).
