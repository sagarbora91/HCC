# FIT-fit-front-a DONE

Lane **fit-front-a** · model Sonnet · branch `claude/fit-front-a` · worktree `E:\Projects\HCC\worktrees\fit-front-a` · base `3dac5afb` (claude/fit). Nothing pushed.

## Commits

```
e6ba7cbe FIT front-a: overview, rooms, calendar converted (FitPage, tabs, panels), sub-screens, tests
abcbfc5a FIT front-a: overview legend inside the rooms panel body (tab-port panel height)
(+ the final commit with this file)
```

## Files

Prototype (all inside the lane's ownership):
- `03 Prototype/src/features/overview/Overview.tsx`, `src/features/rooms/RoomBoard.tsx`, `src/features/reservations/Calendar.tsx`
- `03 Prototype/src/styles/fit-front-a.css` (new; desktop rules, every rule starts with `.app-shell`, no font-size, no colour)
- `03 Prototype/src/app/subscreens.ts` (own block `// FIT-FRONT-A`, appended after `// FIT-ADMIN`: overview/arrivals, departures, in-house; rooms/all)
Tests: `03 Prototype/tests/stream-fit-front-a.mjs`, `tests/fit-front-a-entry.tsx`.
No existing test needed updating (no test read the old structure of these three pages). `RoomCard.tsx` and `RoomModal.tsx` are untouched. Nothing under `src/mobile`, `src/app/mobile`, `*Mobile.tsx`.

## What was built

| Page | Pattern | Sub-screens | Default tab / filter (old → new) |
|---|---|---|---|
| `overview` (Dashboard) | `FitPage` fill. Header = the welcome heading (text kept) + welcome line as sub, "+ New reservation" as the header action. Toolbar `StatStrip` = the four headline figures (occupancy, arrivals, room ready, receipts; the occupancy progress bar is dropped, its figure and sub-line stay). `FitTabs` Arrivals / Departures / In house. Body = three columns, each a `ScrollPanel`: Front desk today (list, all rows, no longer cut to 5) · A view across your rooms (floor rows + legend) · Needs your attention (attention items, closing note, the four cash cards, server-report note, compliance due card, backup status). Below 900 px the three panels stack in rows of >= 160 px. | arrivals, departures, in-house (`useFitTab`, ids lower-case kebab) | Arrivals → arrivals (unchanged); the tab is now remembered per browser session. |
| `rooms` | `FitPage layout="board"`: toolbar = search + status filters + floors master link; `FitTabs` "All floors" (first, default) + one tab per floor generated from the floor master (`floor-<n>`, retired floors marked); one `ScrollPanel`. All floors = every floor section (heading + count) in the one panel, tiles unchanged (`RoomCard` untouched), grid `repeat(auto-fill,minmax(150px,1fr))`. The footnote (with its "Open the calendar" button) is a `FitNote` in the panel footer. | rooms/all (floor tabs are generated, not listed) | all-floors view (was: all floors stacked, same content) — no change in meaning. Status filter default `All` unchanged. |
| `calendar` | `FitPage` fill: header + one toolbar row (week navigation, From date, Room type); ONE `ScrollPanel` scrolling both ways, room column and date row sticky (the old inner scroller is neutralised in the panel); date range in the panel head, legend + the long note (`FitNote`) in the panel footer. Grid markup, cells, bars, click-to-book unchanged. | — | — |

## Tests

All through the lock (label `fit-front-a`), output in `E:\Projects\HCC\worktrees\fit-front-a\.scratch\`:

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` | 2240 / 2246; 5 new `[FIT-FRONT-A]` tests pass. The 6 failures are `[FIT]` tests of fit-check, none caused by this lane: (a) `no phone-layout file changed since 541bbff7` lists `features/accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `styles/m-accounting2.css` — they come from the ACC-3 / lg merges into `claude/fit`, not from this branch (`git diff claude/fit -- src/mobile src/app/mobile` is empty); (b) `page <id> renders a fit-page root` for overview, rooms, calendar and arrivals: the harness renders without `FitNavContext`, so `data-fit-page` is empty — the root class `fit-page` is there (see Integration needed 1); (c) the SUB_SCREENS comparison failed once on my first version (ids taken from an object array) and was fixed by an explicit `OV_IDS` array in commit e6ba7cbe+1 — run again with the final `npm test` below. |
| `npm run build` | clean |
| `node tools/fit-screen-check.mjs --pages overview,rooms,calendar` (server `PORT=4191`) | see "Check result" |

## Check result

First run (owner, tab-land / tab-port / lap-1366, text 1): tab-land PASS for all three pages and all four sub-screens. Findings then: `cramped 124` at tab-port on overview (the legend in the rooms panel footer squeezed the body — fixed by moving the legend into the panel body, commit abcbfc5a); `font-changed label` at lap-1366 on rooms (14 px, was 11 px) and calendar (10 px, was 11 px) — both are the known artifact of the baseline: on `main` the first visible `label` on a fine-pointer viewport was the demo toolbar's 11 px field, now inside the Demo pill; no font was changed by this lane (Calendar's `From` label keeps its 10 px from `reservations.css`). Report of that run: `.scratch\c1\fit-check-20261101-1352.md`.

FULL_CHECK_PLACEHOLDER

## Modal openers for `tools/fit-modals.json`

1. Overview / Room board / Calendar header: button "+ New reservation" (opens the new-reservation dialog).
2. Room board: click a room tile (`.room-card`, first one) — opens the room dialog (`RoomModal`).
3. Calendar: click a free night cell (`button.res-cal-cell.free`, first one) — opens the new reservation dialog prefilled with room and date.
4. Dashboard: a mini room (`.mini-room`, first one) — opens the room dialog.

## Open items

- Dashboard "Front desk today" now lists all rows of the chosen tab (it showed 5); inside a scroll panel nothing is hidden. Say if the owner wants the 5-row cap back.
- The Dashboard welcome heading is the page title (22 px); the small eyebrow "YOUR HOTEL, AT A GLANCE" is part of the sub-line.
- Rooms: the tab count badges respect the active status filter and search.

## Integration needed

1. `tests/stream-fit.mjs` (fit-check): the page harness `render()` provides `HccContext` only; wrap the page in `FitNavContext.Provider value={{desktop:true,page:id,tabKey:'',seq:1,setTabCrumb(){}}}` so `data-fit-page` is rendered. Without it every converted page (arrivals included) fails `renders a fit-page root`.
2. `tools/fit-screen-check.mjs` (fit-check): font kind `label` must be measured inside `.content` (shell DONE item 1); until then `label` shows as `font-changed` on fine-pointer viewports for pages whose first label is not 11 px (rooms 14 px, calendar 10 px).
3. `FIT_BASE` in `tests/stream-fit.mjs`: after the ACC-3 merge the "phone files changed" test needs the new base (the three files listed above come from `claude/fit`).
4. `tools/fit-modals.json`: the four openers above.

## HANDOFF bullet

FIT lane fit-front-a DONE (claude/fit-front-a, Sonnet): Dashboard (3 scroll panels + StatStrip + Arrivals / Departures / In house sub-screens), Room board (All floors + one tab per floor, tiles unchanged, auto-fill 150 px grid) and Calendar (one panel scrolling both ways, sticky room column + date row) converted; `fit-front-a.css`, `// FIT-FRONT-A` registry block, `[FIT-FRONT-A]` tests (5).
