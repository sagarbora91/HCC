# ADM-DONE — Administration panel waves ADM-1…4 (2–3 Nov 2026, cloud session)

Branch `claude/adm` (on top of main 456167a + the plan). Plan: `02 Planning/HCC_Admin_Panel_Plan_v1.md` (APR-028, DEC-214…221). Lane contract and per-lane notes: `02 Planning/agents/ADM_Briefs.md`. **Not merged to main; the server gate has not run** (this container has no dotnet SDK — see §5).

## 1. What was built

| Wave | Prototype (`03 Prototype`) | Server (`08 Production`) |
|---|---|---|
| **ADM-1** switches | `src/domain/feature-catalogue.ts` (18 modules / 60 sub-functions: pages, command prefixes, requires, `permPrefixes`, module `coarse`); `feature.<module>.<feature>` settings in `streams/config.ts` (`cfgFeatureOn`, `cfgPageOn`, `cfgBeforeCommand`, dependants cascade on off, walk-in BOOK for today exempt while Reservations is off); Master configuration › Modules on/off gains the **Sub-functions** tree; golden `08 Production/database/feature-catalogue.json` via `node tools/feature-catalogue.mjs --write` | `FeatureCatalog.cs` + `FeatureIds`; `feature.*` booleans in the modules group; `IModuleGate.IsFeatureOnAsync / EnsureFeatureOnAsync`; `RequireHccFeature()` on 16 feature route groups; CreateReservation gated inside `ReservationService` (walk-in exempt); dependants cascade in `ConfigurationService.ApplyAsync`; `FeatureCatalogTests` (golden drift) + `FeatureGateHttpTests` |
| **ADM-2** roles as data | `streams/roles.ts` (custom roles `C01…`: base, money, feature levels off/view/full, grants + template copied from the base and frozen in `baseCopy`), `handlers/roles.ts` (`ROL_CREATE / ROL_UPDATE / ROL_FEATURE_SET / ROL_GRANT_SET / ROL_RETIRE`; `USER_CREATE/UPDATE` take `customRole`), `User.customRole?`, `s.rolesX`, **SCHEMA 14**; `can()`, `authorize()`, `navFilterModules`, `execute()` honour the role's feature level; demo roles C01 Day shift manager / C02 Night shift manager (+ users Tanvi, Rohan; fictional) in FY26 only; sync mapping to the server commands and `/me/capabilities.features` | Migration **4800** (`plat.role` + `is_custom/base_role/money/created_*/retired_utc`, `plat.role_feature`, `plat.role_origin_grant`, `plat.role_grant_override`, `plat.policy_version`, view `plat.v_user_role_effective`), `RoleRules.cs`, `SqlRoleAdministration` + `SqlFeatureAccess`, commands `CreateRole / UpdateRole / SetRoleFeature / SetRoleGrant / RetireRole` (Owner only, never own role, never built-ins), `GET /admin/roles` (+ isCustom, baseRole, money, features, version, retired), `GET /admin/policy-history`, capabilities `features`, snapshot roles = held + base codes, `RequireHccFeature` also applies the per-role level; tests `RoleRulesTests`, `RoleAdministrationTests`, `RoleFeatureHttpTests` |
| **ADM-3** page | `features/admin/AccessControl.tsx` = **Administration** (page id `access`): tabs Modules & features · Roles (Owner only; grid Off / View / Full per non-core feature by module, money switch, Advanced permission ids with the DEC-217 warning, effective-access preview, Retire) · Users (built-in + custom role picker, money roles Owner-assign only, exceptions, credentials in production) · Audit (policy + role history, newest first; production reads `/admin/policy-history`); `RolesPanel.tsx`, `adm-roles.css` (tokens only), phone `src/mobile/admin/Access.tsx` (chip bar, 48 px segmented controls) | — |
| **ADM-4** | this file, HANDOFF, plan ticks, FIT allow-list for the admin files | hotel-PC checklist §5 |

## 2. Rules enforced (and tested)
- Owner = super admin, never editable or a base (DEC-216). Built-ins read-only. Custom role = frozen copy of its base + grid/money/advanced adjustments.
- Money forced off and locked for Housekeeper / Supervisor / Maintenance / Restaurant bases (DEC-061, G12); SoD limits may only narrow (DEC-219).
- Roles edited by the Owner only; Tech admin creates users and assigns non-money roles (DEC-218); nobody edits their own role; last enabled Owner protected.
- Feature `off` for a role: pages hidden, commands refused "<Feature> (<Module>) is not part of your role <name>."; `view`: "… is view only for your role <name>." (same sentences on the server, FORBIDDEN + `reason MODULE_OFF`, `featureId`, `roleCode`, `access`).
- When every switchable feature of a module is off for a role, the module's core pages/commands are off for it too (that is how "Manager minus Accounts" hides the Accounting page whose owner feature is core).
- Property-level switches (ADM-1) win over role levels; core features (DEC-215) can never be switched off at either level.

## 3. Tests (cloud container, 3 Nov 2026)
| Suite | Result |
|---|---|
| Prototype `npx tsc --noEmit`, `npm run build` | clean, single HTML built |
| Prototype full suite (`node --max-old-space-size=10240 tests/run.mjs`) | **2493 pass / 24 fail** — the 24 are `[CT-A/B/C]` colour-token round-trips that `git show` commit 799e4cc, absent from this shallow clone; they pass on the hotel PC |
| New: `[ADM1]` 10 · `[ADM2]` 13 · `[ADM3]` 11 | all pass |
| Server | **not compiled, not run** (no dotnet here) |

## 4. Owner-visible behaviour to try (prototype, FY26 scenario)
1. Sign in as Owner → Admin & Setup › Administration › Roles: C01 "Day shift manager" (Manager base, Accounting off) and C02 "Night shift manager" (Manager base, Accounting view). Sign in as Tanvi: no Accounting tile, `ACC_*` commands refused. Sign in as Rohan: Accounting opens, saving a voucher is refused as view-only, Day close works.
2. Master configuration › Modules on/off › Sub-functions: switch Front desk › Reservations off → Reservations and Calendar vanish for everyone, walk-in check-in still works; switch Expenses off → Purchases goes with it and says so.
3. New role from Housekeeper: money switch locked; grid cannot add Accounts money.

## 5. Hotel-PC checklist (before the gate)
1. `git fetch && git checkout claude/adm` · `03 Prototype`: `npm install`, `npm test` (expect 0 failures incl. CT-A/B/C), `npm run build`.
2. `08 Production`: `dotnet build Hcc.slnx` — watch the items lane S listed in `ADM_Briefs.md` "Lane S notes" (pattern variables in throw expressions, `OUTPUT … INTO` on the trigger table, migration 4800 batches, tests asserting the exact `roles` list / role JSON).
3. `dotnet test` serial on `.\SQLEXPRESS` (`tools/Invoke-Serialized.ps1`): `Hcc.Access.Tests`, `Hcc.Configuration.Tests`, then everything. Regenerate `tests/_role-reads.json` with `HCC_UPDATE_ROLE_READS=1` (`RoleReadMatrixTests`) because `/admin/policy-history` was added by hand.
4. `tools/Start-Gate.ps1`; on PASS the owner merges to main; APK bump goes with the next APK wave (v19 prep exists).

## 6. Open items
- ~~Server gates for guest requests, complaints, lost & found, leave, POS menu, message templates, maintenance assets, masters~~ — done 3 Nov 2026 (commit eacc398); the expense-vendor master list is gated as Masters, not Purchases (owner to confirm).
- ~~Sync mapping of `ROL_*` has no automated prototype tests~~ — done 3 Nov 2026 (`tests/sync-adm-roles.mjs`, 13 tests).
- DEC-217 warning text fires only for explicit `PERM-ACC-003/004` grants; the prototype matrix has no such ids yet, so it is server-only in practice.
- ADM-3 phone Roles tab verified by markup tests, not by a rendered phone smoke; run the walkthrough `ADM_Walkthrough_Script.md` §G on the hotel PC.

## 7. Integrated onto main (ADM-int, 3 Nov 2026, Opus integrator)
Merged origin/main c5c7d5f6 (F4 tariff / revenue / digest, F5 year lock / bank / egress, F6 hall booking, F7 reports, GST DEC-239) and then 53f0518d (docs only) into `claude/adm`.

- **Conflicts:** Decision Register (both blocks kept), `types.ts` SCHEMA, `features/admin/register.tsx` (NewRoleModal + NetSetModal), `handlers/index.ts` (`...roles` added to main's `registry0` under the F5 year guard), `AccessPorts.cs` (main's `AuditAsync` entity filter), migration lock (regenerated with `tools/migrations-lock.mjs --add --accept-out-of-order`), 14 hash-pin test files (main side, then re-pinned), `stream-fit.mjs` allow-list (union). `Hcc.slnx` merged clean; 4800 + 4801 added to its migration list.
- **SCHEMA 15** = ADM-2 custom roles (rolesX, User.customRole); 14 = F6 hall. Re-pins: BASE d6df4a46… → 70983afb…, FY26 history 961b04c2… → e1247964… (schema only), FY26 whole e64651dd… → f1b37a3e… (schema + demo roles C01/C02 and their users).
- **Catalogue additions** (prototype, `FeatureCatalog.cs`, golden): revenue.tariffcard (`tariff-card`, TRC_, PERM-TRF-), revenue.feedback (FBK_, QW_, PERM-FBK-), revenue.hall (`hall-booking`, HALL_, PERM-HAL-), revenue.quotations (QUO_, requires hall), accounting.yearend (ACC_YEAR_), accounting.bank (BANK_), reports.hub (`report-hub`), reports.digest (`owner-digest`, `revenue-category`, DIGEST_, PERM-DGS-), admin.internet (NET_, PERM-NET-). Still 18 modules. Demo roles and tests now take "every switchable Accounting feature" from the catalogue.
- **Lane S fixes:** RoomMasters `Post` helper used an out-of-scope `mst`; missing usings (Reservations endpoints, two test files); the dependants cascade wrote source `dependant` (9 chars, not in CK_cfg_change_source) → new migration **4801** allows `cascade`; `RequireHccFeature` skips a gate service a single-module test host does not compose (the full composition has both); TAD role-edit refusal is APPROVAL_REQUIRED (Request-mode seed grant); the modules group count follows the catalogue. Role reads regenerated: only `GET /admin/policy-history` for MGR/ACC → 200 (the prototype read rule now matches the server: PERM-ADM-001/002/004).
- **Prototype:** ADM `.cfg-features` CSS moved from the frozen `m-config.css` into `adm-roles.css` (CT-B was a real failure, not a clone artefact); fit allow-list rows for the new Administration page.
- **APK v20 S4 fixes (APK_v19_Emulator_Report):** V19-1 compact stat strips wrap rather than hide the last card (`z-fit.css`); V19-2 phone app-bar shows the date (label "Business date" kept for screen readers only); V19-3 phone key-log filters two per row, 48 px, full width (`m-keys.css`); V19-4 `build-production.mjs --assemble` runs Gradle by absolute path in `04 Android/android`.
- **Decision renumbering** (admin plan keeps DEC-214…221; wave proposals now Proposed rows in the register; tables in each DONE file):

| Old proposed | New | Source |
|---|---|---|
| DEC-214…218 | DEC-240…244 | LF/LF-int-DONE |
| DEC-219…222 | DEC-245…248 | F4/F4-int-DONE |
| DEC-223…226 | DEC-249…252 | F5/ACC4-DONE |
| DEC-227…234 | DEC-253…260 | F6/F6-DONE |
| DEC-235…238 | DEC-261…264 | F7/F7-DONE |

- **Counts:** prototype **2777/2777** (main 2731); tsc, build and build:production clean; fit check `access, overview, accounting, keys` owner on tab-land + lap-1366: 0 failing of 302; phone 360 px check (access tabs, keys, rooms): 7/7. Server: build 0 warnings; **4088 pass / 6 fail in 42 projects** (main 4059/42); the 6 are the known Linux-only ones (GstIds DPAPI, Legacy absolute folder, 3 Operations HostingTests, Stays RoomBoardQuerySqlTests ceiling). The full prototype suite needs a 6 GB heap here (`NODE_OPTIONS=--max-old-space-size=6144`).
