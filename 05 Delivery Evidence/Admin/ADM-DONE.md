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
- Server gates for guest requests, complaints, lost & found, leave, POS menu, message templates, maintenance assets, masters: the prototype refuses them; the server relies on the per-role grid removing ids plus the existing module gates. Add `RequireHccFeature` to those route groups in a follow-up (list in `ADM_Briefs.md` Lane S notes).
- Sync mapping of `ROL_*` has no automated prototype tests.
- DEC-217 warning text fires only for explicit `PERM-ACC-003/004` grants; the prototype matrix has no such ids yet, so it is server-only in practice.
- ADM-3 phone Roles tab verified by markup tests, not by a rendered phone smoke; run `tools/masters-smoke.mjs`-style smoke on the hotel PC.
