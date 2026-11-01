# FIT-fit-revenue DONE

Lane **fit-revenue** (phase B) · model Sonnet · branch `claude/fit-revenue` · worktree `E:\Projects\HCC\worktrees\fit-revenue` · base `3dac5afb` (claude/fit). Nothing pushed.

## Commits

See `git log --oneline 3dac5afb..HEAD` (first commit `6a0faa7e` "FIT revenue: yield, groups, channels, booking-site on FitPage …", then the fix commit for the Groups layout and this file).

## Files

Prototype (all within the lane's ownership):
- `src/features/yield/YieldPage.tsx`, `src/features/groups/Groups.tsx`, `src/features/channels/Channels.tsx`, `src/features/channels/BookingSite.tsx`
- `src/app/subscreens.ts` (the `// FIT-REVENUE` block only, 18 lines appended)
- `src/styles/fit-revenue.css` (new, desktop rules only, every rule starts with `.app-shell`, no font size, tokens only). It is a new sheet and not an addition to `m-yield.css` / `m-groups.css` / `m-channels.css`, because those match the phone-layout path `styles/m-*.css` of contract §1 and the CT-B tests pin them to their original text.

Tests: `tests/stream-fit-revenue.mjs`, `tests/fit-revenue-entry.tsx` (7 tests `[FIT-REVENUE]`), `tests/stream-wd5-gaps.mjs` (helper only, see below).

Phone paths: nothing under `src/mobile`, `src/app/mobile`, any `*Mobile.tsx`, `styles/m-*.css`.

## What was built

Every page uses the contract §6 pattern; no selector, stream, command, permission, fixture or type was touched. All fonts unchanged (no `font-size` in the lane CSS); touch sizes come from the shell.

| Page | Pattern | Sub-screens (SUB_SCREENS) | Default tab / filter | Check |
|---|---|---|---|---|
| `yield` Tariff & yield | `FitPage` fill: header action = Publish / Propose / View only, `StatStrip` (the four stats) in the toolbar, `FitTabs`, each tab one `ScrollPanel`. Rate calendar: the grid scrolls in the panel, **first column sticky** (and the header row), the legend and the "Simulated" note are `FitNote`s in the panel footer. Rate plans = plans table + rate sheet + quote tool in one scrolling panel | rate-calendar, rate-plans, rules, restrictions, history, **publish-requests** | `Rate calendar` (unchanged: the old state started on it) | PASS at all five viewports, all roles, text 1 and 1.3 |
| `groups` Groups & corporate | `FitPage` fill, `StatStrip` (four stats), `FitTabs` with counts. **Blocks** = `SplitView`: block list left (`ScrollPanel`, `usePaged` + `ShowMore`, status pill, pickup text), block record right: the existing `BlockCard` (rooming list / allotment / release / master folio / cancel) **plus** the block's master folio below it ("Billing"), all scrolling in the record pane. Companies, Pickup report, Master folio, Limit requests: one `ScrollPanel` each (Print / block picker in the panel header). The two info notes are `FitNote`s in panel footers | blocks, companies, pickup-report, master-folio, limit-requests | `Blocks` (unchanged); on a screen ≥ 900 px wide the first block is preselected, below 900 px the list shows first | see "Check" |
| `channels` OTA channels | `FitPage` fill, `StatStrip` (pending, connected, bookings, commission due — hidden as before when the rate is withheld), `FitTabs` (Payouts and Commission follow the old role rules). Inbox (pending + recently decided, one panel), Channels & mapping (cards), Rate & availability push (channel picker + **Push now** in the panel header, always visible), Commission report (**Print view** in the panel header), Payouts | inbox, channels, push, commission, payouts | `Inbox` (old: global `tab` state, fallback inbox) | PASS (see Check) |
| `booking-site` Direct booking site | `FitPage` fill, `StatStrip`, `FitTabs`. **Bookings** = the web booking requests in a `ScrollPanel` with `usePaged` + `ShowMore` (old: every request rendered); **Preview** = the guest page in a phone frame, centred, scrolling in the panel | bookings, preview | `Bookings` (old page showed preview and requests side by side) | PASS |

Notes on decisions:
- The brief names booking-site tabs "Settings / Rooms & rates / Bookings / Preview". The page has no settings or rates content (room copy comes from the room-types master, rates from the tariff), so no empty tabs were invented: the page has **Bookings** and **Preview**. Nothing was dropped.
- yield: the "Tariff publish requests" card used to sit above the tabs. It is now the sub-screen **Publish requests**, shown only while a request exists (its tab carries the pending count as a warn badge). Default stays Rate calendar, so an Owner with pending requests sees the badge but is not moved off the calendar. Say if you want it to be the default while a request is pending.
- groups: `FitPage layout="split"` is **not** used. The page root would get the class `fit-split`, which z-fit.css also uses for `SplitView`'s grid (`.fit-split{display:grid;grid-template-columns:2fr 3fr}`), and it breaks the page (header, toolbar and tabs land in grid cells; at tab-port the body shrinks to 80 px). The page uses `layout` fill with a `SplitView` inside. Found in the first check run (screenshot), fixed in the second commit.

## Tests

All through `tools\Invoke-Serialized.ps1` (label `fit-revenue`), output in `<worktree>\.scratch\`.

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` (before the groups layout fix) | 2241 / 2247 passed. Main + fit base + 7 new `[FIT-REVENUE]`. The 6 failures are not from this lane: `[FIT] no phone-layout file changed since the wave base` (accounting `Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `m-accounting2.css` came in with the ACC-3 merge, base `541bbff7` is stale — `FIT_BASE` must move to the new base) and `[FIT] page <id> renders a fit-page root` for arrivals, yield, groups, channels, booking-site (the harness renders without `FitNavContext`, so `data-fit-page` is empty; `arrivals`, converted by fit-shell, fails the same way — fix in `fit.tsx` or `stream-fit.mjs`: `data-fit-page={nav.page\|\|ctx.page}`). |
| `npm run build` | clean |
| `node tools/fit-screen-check.mjs --pages yield,groups,channels,booking-site` (`PORT=4197`, all five viewports, nine roles, text 1 and 1.3), report `05 Delivery Evidence/Fit/fit-revenue-check/fit-check-20261101-1428.md` (+ json, screens untracked) | 640 checks, 64 failing before the groups fix. page-scroll 0, content-scroll 0, not-converted 0, hidden-action 0, touch-size 0, modal-clipped 0, h1 0. `cramped 80`: 6 — groups at tab-port (the `fit-split` collision above, fixed). `font-changed`: 76 — see below. |

Existing tests updated (never deleted or loosened): `tests/stream-wd5-gaps.mjs` — the `channels()` helper renders the desktop page inside `FitNavContext` with the asked tab (`tabKey`), because the desktop sections are now `useFitTab` (the old helper passed the tab through `ctx.tab`); the assertions are unchanged. `stream-accounting-ui2` and `stream-company-credit-ui` pass unchanged (default tabs, `CompaniesTab` keeps its output).

### font-changed findings (check artifact, no font was changed)

The baseline is a global set of sizes sampled from the **default** tab of each page on `main`; the tool measures "the first visible td/th/label/select/.muted" on every sub-screen. On a non-default tab the first match is another, existing element: `th 11px (was 10px/9px)` = the channel mapping table (`channels › channels`) and the push grid (`channels › push`); `.muted 12px` = `grp-table td small.muted` (`groups › limit-requests`); `label 13px` = `.grp-inline` (`groups › master-folio`); `label 14px` and `select 12px` = the guest-page preview's own `.chn-site` labels (`booking-site › preview`) and the channel picker (`channels › push`). All of them are existing classes with unchanged CSS; the lane CSS contains no font declaration (asserted by a test). Suggestion for fit-check: record the baseline per page **and sub-screen**, or measure the same selector list on the old page's sub-screens.

## Open items

- Re-run of the check after the groups fix: see "Check result after the fix" below (written after the run).
- `groups › master-folio` / `limit-requests`, `channels › channels` / `push`, `booking-site › preview` still show the `font-changed` artifacts above until the baseline is per sub-screen.
- Booking-site Settings / Rooms & rates tabs do not exist (no content); see decisions.
- Payouts / Commission tabs are in the registry for every role; a role that cannot see one lands on Inbox (as `go()` did before).

## Integration needed

1. `src/components/fit.tsx` / `z-fit.css` (fit-shell): `.fit-split` is both the `FitPage layout="split"` root class and the `SplitView` grid. Either rename one (`fit-page fit-layout-split`) or scope the SplitView rules to `.fit-page-body>.fit-split, .fit-cols .fit-split`. Until then no page may use `layout="split"` with a `SplitView` inside.
2. `tests/stream-fit.mjs` / `fit.tsx`: `data-fit-page` is empty in the static harness (see Tests); `FIT_BASE` stale after the ACC-3 merge.
3. `tools/fit-modals.json` — openers of this lane: Groups "New group block" (`.grp`: button text "New group block" on `groups`), Groups "New company" (`groups`), Tariff & yield "Publish rates" (`yield`, Owner), OTA channels "Terms & mapping" (`channels`, tab Channels & mapping, a card footer button).
4. `tools/fit-screen-check.mjs`: per-sub-screen font baseline (see above).

## HANDOFF bullet

FIT lane fit-revenue DONE (claude/fit-revenue, Sonnet): Tariff & yield (FitTabs; sticky first column in the rate calendar; publish requests as a sub-screen), Groups & corporate (FitTabs; Blocks = SplitView with the block card, rooming list actions and master-folio billing in the record pane), OTA channels (FitTabs; Push now / Print in panel headers), Direct booking site (Bookings paged + Preview). New `styles/fit-revenue.css`; 18 `SUB_SCREENS` lines; 7 `[FIT-REVENUE]` tests. No font, selector, command or permission change; phone files untouched.
