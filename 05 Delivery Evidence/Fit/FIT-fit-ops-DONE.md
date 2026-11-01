# FIT-fit-ops DONE

Lane fit-ops / Sonnet / branch `claude/fit-ops` / worktree `E:\Projects\HCC\worktrees\fit-ops` / base `3dac5afb` (head of `claude/fit`). Pages: `housekeeping`, `maintenance`, `handover`, `inventory`.

## Commits
See `git log --oneline 3dac5afb..HEAD` in the worktree (first commit `fd9ffffa`; later commits are check fixes and this file).

## Files
Prototype (all inside the lane ownership):
- `src/features/housekeeping/Housekeeping.tsx`, `src/features/maintenance/Maintenance.tsx`, `src/features/handover/HandoverPage.tsx`, `src/features/inventory/InventoryPage.tsx`
- `src/styles/fit-ops.css` (new, desktop rules only, every rule starts with `.app-shell`, no font size, no colour)
- `src/app/subscreens.ts` (lines inside the `// FIT-OPS` block only)
- Tests: `tests/stream-fit-ops.mjs`, `tests/fit-ops-entry.tsx` (new)

Why a new stylesheet: `housekeeping.css` is pinned to its pre-token original by the `[CT-C]` tests (appending to it failed three of them) and the `m-maintenance / m-handover / m-inventory` sheets count as phone sheets in `tests/stream-fit.mjs` ("no phone-layout file changed"). `styles/` files are bundled in name order, so `fit-ops.css` needs no `build.mjs` change. No file outside the ownership list was edited.

## What was built (pattern per page)

| Page | Pattern | Sub-screens | Default tab changes |
|---|---|---|---|
| `housekeeping` (supervisor / manager / owner) | `layout="board"`: `BoardColumns` with the four status columns (To do, In progress, Awaiting inspection, Done) plus a fifth column "Attendant load" (the old load card, same rows, same "Manage list…" link and note). Toolbar: the unchanged floor / assignee / zone filters (+ master links, Clear filters, "n of m tasks") and a `StatStrip` of the four counts. "Generate stayover tasks" is the header action. | none (no tabs) | none |
| `housekeeping` (Housekeeper, Maintenance role: "My tasks") | `layout="fill"`: `StatStrip` in the toolbar, one `ScrollPanel` with the zone-grouped large cards. | none | none |
| `maintenance` (Owner / Manager / Supervisor) | `FitTabs` over the six `MNT_TABS` ids. Work orders: `StatStrip` + the filter row, table in a `ScrollPanel`; the housekeeping repair requests are the side panel (`fit-cols`) when any exist. Preventive: compliance card + schedule `ScrollPanel`. Assets, Spares: `ScrollPanel` (add / filter buttons in the panel head, notes in the footer as `FitNote`). Vendors & AMC: two even `ScrollPanel`s. Reports: `StatStrip` + print button + 2 x 2 grid of four `ScrollPanel`s (the dialog `mnt-print` keeps the old `MntReportsTab`). | orders, pm, assets, spares, vendors, reports | none (Work orders stays default; now remembered per session) |
| `maintenance` (Maintenance technician) | `fill`: "Open work orders" cards in a `ScrollPanel`; repair requests and "Recently finished" in a side column (`fit-cols`) only when they exist. | none | none |
| `maintenance` (Housekeeper) | `fill`: info note as a `FitNote` (2 lines) in the toolbar, read-only list in a `ScrollPanel`. | none | none |
| `handover` | `FitTabs` over the four `useHovTabs` ids. **My checklists = `StepFlow`**: every run of the user is a step (current shift first, then the other shifts, which the old page hid behind "Show (n)": nothing is hidden now); the step body is one `ScrollPanel` (run head + items) whose **footer holds `HovRunActions`, so Submit checklist / Read & acknowledge / Reopen are always visible**. The incoming-note banner and the "Current shift" line stay above. Handover: banner, desk filter + "Write handover note", notes list and "Open carry-forward" side by side (`fit-cols`). Dashboard: toolbar, `StatStrip`, completion table + overdue list side by side. Templates: toolbar fixed above a scrolling grid. The demo clock stays in the header actions (prototype build). | checklists, handover, dashboard, templates | none (My checklists) |
| `inventory` | `FitTabs` over the seven old tabs (ids: stock, laundry, counts, losses, consumption, par-minimums, movements); each tab is one `ScrollPanel` with its buttons / filters in the panel head and its notes in the footer. The five KPI tiles are a `StatStrip` (the value tile still only for money roles). **Reorder alerts** moved from a card above the tabs to the side panel of the Stock tab (the default tab). Header actions: Issue / use (primary), Receive stock, New item. | stock, laundry, counts, losses, consumption, par-minimums, movements | none |

Class change (layout only): the Acknowledge button in a handover note card had the class `modal-actions`; on a page that class is measured by check 5 ("hidden action"), so it is now `hov-note-actions` with the same flex layout (rule in `fit-ops.css`).

DEC-061: no panel was moved so that a money component reaches a housekeeping / maintenance floor role. Maintenance costs keep `mntCanSeeCost`, inventory values keep `invMoneyRole`; the Housekeeper / Maintenance screens contain no rupee amount (test `[FIT-OPS] DEC-061`).

## Existing tests updated
None. `npm test` needed no change to any existing assertion (the only text assertions on these files, `[N3-6]` unassigned wording, still pass).

## Tests
- `tests/stream-fit-ops.mjs` (22 tests, names `[FIT-OPS]`): fit-page root + `data-fit-page` for every page x role, board / fill structure, default tabs and `go(page,{tab})` / session tab for maintenance and handover, StepFlow with the sign-off in `.fit-panel-foot`, inventory tabs and alerts, DEC-061 no `₹`, SUB_SCREENS match, lane CSS guard.
- Commands (through the lock, output in `.scratch\`): `npx tsc --noEmit` (clean), `npm test`, `npm run build`.
- `npm test` result and the three failures that are not this lane's: see "Results" below.

## Results
RESULTS_PLACEHOLDER

## Modal openers for `tools/fit-modals.json`
```json
{"page":"maintenance","opener":"New work order","lane":"fit-ops","roles":["owner","manager","supervisor"]},
{"page":"inventory","opener":"Issue / use","lane":"fit-ops","roles":["owner","manager","supervisor","housekeeper"]},
{"page":"handover","opener":"Write handover note","lane":"fit-ops","roles":["owner","manager","reception"],"tab":"handover"},
{"page":"housekeeping","opener":"Report repair","lane":"fit-ops","roles":["housekeeper"]}
```

## Open items
- Housekeeping has no `FitTabs`, so `housekeeping` adds no `SUB_SCREENS` line.
- Reorder alerts are shown only on the Stock tab (default); on the other inventory tabs the "Below minimum" count in the `StatStrip` stays visible.
- The Reports tab of Maintenance exists twice (`MntReportsTab` for the print dialog, `MntReportsFit` for the page) so the dialog output is unchanged.
- `tests/stream-fit.mjs` as shipped fails on any lane for two reasons that are not about layout: `FIT_BASE` (default `541bbff7`) is older than the phone files `main` changed since (set `FIT_BASE` to the real wave base), and its page harness renders pages without a `FitNavContext`, so `data-fit-page` is empty and every converted page (arrivals too) fails "root needs data-fit-page". Both are the integrator's / fit-check's to fix.

## Integration needed
- `tools/fit-modals.json`: append the four openers above.
- `tests/stream-fit.mjs`: provide `FitNavContext` `{desktop:true,page:id,...}` in the harness `render()` (see `tests/stream-fit-ops.mjs`) and set `FIT_BASE`.
- Nothing else outside the lane's files.

## HANDOFF bullet
FIT lane fit-ops: housekeeping (board + attendant-load column; My tasks list), maintenance (six tabs, technician / housekeeper views), handover (four tabs; a checklist run is a StepFlow with the sign-off always in the panel footer) and inventory (seven tabs, reorder alerts beside the stock table) converted to the FIT patterns; lane CSS in `src/styles/fit-ops.css`; tests `stream-fit-ops.mjs`; DEC-061 respected (no money on floor-role screens). Check report: see FIT-fit-ops-DONE.md.
