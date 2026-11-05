# MEAS-CAP-GUEST DONE

Lane meas-cap-guest / Sonnet 5.5 / branch `claude/meas-cap-guest` / worktree `E:\Projects\HCC\worktrees\meas-cap-guest` / base tag `meas-setup` (3040fa7f).

## Commits
See `git log --oneline meas-setup..HEAD` (hash in the coordinator report).

## Files
Prototype (`03 Prototype`)
- `src/domain/streams/services.ts` : appended block `F12-CAP-GUEST` (rules: `gsvSurveyProblem`, `gsvSurveyRateLimited`, `gsvSurveyNps`, `gsvReviewProblem`, `gsvRootCauses`, `gsvRootCauseListProblem`, `GsvMeasError`, `gsvMeasGuard`, `GSV_MEAS_KEYS`).
- `src/services/mock/handlers/meas-guest.ts` (new) : `MEAS_SURVEY_ADD`, `MEAS_REVIEW_SAVE`, `MEAS_ROOTCAUSE_LIST_SAVE`, `MEAS_ROOTCAUSE_SET`, `MEAS_LOYALTY_SET`.
- `src/services/mock/handlers/index.ts`, `src/clients/sync/registry.ts` : one import + one entry each, inside the `F12-CAP-GUEST` blocks. (`src/services/mock/index.ts` needed no line: `services.ts` is already exported.)
- `src/clients/sync/meas-guest.ts` (new, command-only sync area; no loader because `s.measX` is shared by six lanes).
- `src/features/services/SurveyForm.tsx`, `SurveyRegister.tsx`, `src/features/customers/Loyalty.tsx` (new dialogs).
- `src/fixtures/ext/meas-guest.ts` : not created (no seed data needed; the root-cause seed list is `GSV_ROOTCAUSE_SEED` in the domain block).
- `tests/stream-meas-guest.mjs` (new).
Server (`08 Production`)
- `src/Hcc.Operations/Meas/GuestRules.cs` (new), `database/migrations/4820_meas_guest_schema.sql` (+ `migrations.lock.json` entry, `Hcc.slnx` line in the `F12-CAP-GUEST` block), `tests/Hcc.Meas.Tests/GuestRulesTests.cs` (new; the project file is meas-settings' and is not committed here).

## What was built
- Survey: five scores 1-5 (overall, cleanliness, staff, value, optional breakfast), recommend 0-10, comment up to 500 chars, consent flag (must be true). Channels: `via:'qr'` (public: no staff role, rate-limited to 10 answers per 10 simulated minutes, follows the Direct booking site module switch) and `via:'desk'` (PERM-MEAS-002 = Owner, Manager, Reception). A survey cannot store ID data: any identity or ID field (idNumber, idType, photo, name, phone ...) is refused `MEAS_ID_DATA`, an ID-like comment is refused, a stay link keeps only booking id, room type and channel. SQL table `meas.survey` has no such columns, `consent` has a CHECK = 1, rows are append-only (trigger).
- Review figures per month and platform (replace on re-save), Manager/Owner. Root-cause list (seeded 6 causes, Manager/Owner edit; a cause used by a complaint can be retired, not removed) and the cause per complaint in a side table (the complaint row is untouched). Loyalty tag on `preference{customerId}.loyaltyTag` (keeps cap-desk's fields).
- Every command checks `measOn` first (`GSV_MEAS_KEYS`: survey, reputation, recovery, repeat) and refuses `MEAS_OFF`; idempotent on client id / command key; audited; simulated clock; no money.
- Refusals: shared code plus `reason` (`MEAS_OFF`, `MEAS_VALIDATION`, `MEAS_NO_CONSENT`, `MEAS_ID_DATA`, `MEAS_RATE_LIMIT`) via `GsvMeasError` (code VALIDATION; RESTRICTED for the rate limit), because `errors.ts` is not in this lane's row.

## Tests
Through `tools\Invoke-Serialized.ps1` (label meas-cap-guest; scripts in `.scratch`):
- `npx tsc --noEmit` exit 0.
- `npm test` full: 2891 / 2902 passed. Before my lane: 2884 (AF2 baseline) + this lane's 18 new `[F12-CAP-GUEST]` tests (all 18 pass; first run of the suite alone: 74/74 after the `measOn` export fix). The 11 failures are all `[RBAC] commands` tests in `tests/stream-rbac-matrix.mjs` (not in my row): every registered command needs a documented role set; they name `MEAS_LOYALTY_SET` first and will name every new MEAS_ command of every lane. See Integration needed 0.
- Server: `dotnet test tests/Hcc.Meas.Tests` with a temporary project file (removed before commit): 20 / 20 passed (`GuestRulesTests`).

## Open items
- Server: only the schema (4820), the rules class and its tests exist. The store, endpoints (`MeasSurveyAdd` etc., public rate-limited QR route, `RequireHccMeasurement`) and the sync loader for `s.measX` are not written (they need `IMeasurementGate` from meas-settings). Migration 4821-4824 unused.
- `MEAS_OFF` item-off test is conditional: `measOn` is the setup stub (always true) until meas-settings merges; the test runs the real refusal as soon as `measOn` honours `configX.values`.
- Phone form at 360 px and a fit check were not run (no page registered; dialogs use the shared one-column form and 48 px buttons). WhatsApp link not built (F9 egress).
- Complaint root cause is kept in `measX.rootCause` (contract), the existing free-text `rootCause` on the complaint is unchanged.

## Integration needed
0. `03 Prototype/tests/stream-rbac-matrix.mjs` (near line 187, the `GSV_*` row): add a `C(...)` entry for each command, e.g. `MEAS_SURVEY_ADD` (qr: any signed-in user; desk: OWN MGR REC), `MEAS_REVIEW_SAVE`, `MEAS_ROOTCAUSE_LIST_SAVE`, `MEAS_ROOTCAUSE_SET` (OWN MGR), `MEAS_LOYALTY_SET` (OWN MGR REC). Same for the other lanes' MEAS_ commands. Until then 11 RBAC tests fail on the merged branch.
1. `app/registry.ts` : import `registration as measGuestUi` from `../features/services/SurveyRegister` and add it to `registrations` (modals `meas-survey-new` (id = booking), `meas-survey-qr`, `meas-loyalty` (id = guest)); add buttons: "Guest survey" on the guest-services page / booking dialog, "Loyalty tag" on Customers 360.
2. `config.ts` (meas-settings) : add page `survey` to the `website` module's `pages` if a nav page is wanted (then `PAGE_CATEGORY` + `F12-KEYWORDS` in navmap.ts); the QR dialog already checks `cfgModuleOn(s,'website')`.
3. `errors.ts` : add `MEAS_OFF` etc. to `ErrorCode` if the integrator wants them as top-level codes; today they are `reason` on a VALIDATION error.
4. Server: `Hcc.Meas.Tests` project (meas-settings) picks up `tests/Hcc.Meas.Tests/GuestRulesTests.cs` automatically; wire `GuestRules` into the command handlers and the public QR endpoint.
5. Sync: one `measX` loader (integrator) for surveys, reviews, rootCauseList, rootCause, preference.

## HANDOFF bullet
MEAS guest captures (lane meas-cap-guest): survey (QR + desk, consent, no ID data, rate-limited), monthly review figures, root causes, loyalty tag; commands `MEAS_SURVEY_ADD` / `MEAS_REVIEW_SAVE` / `MEAS_ROOTCAUSE_LIST_SAVE` / `MEAS_ROOTCAUSE_SET` / `MEAS_LOYALTY_SET`, migration 4820 (`meas.survey`, `review_month`, `root_cause`); server store/endpoints still to write.
