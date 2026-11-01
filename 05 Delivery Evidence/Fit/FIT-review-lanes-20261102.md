# FIT wave W2a — read-only review of the eleven phase-B lane diffs (2 Nov 2026)

Reviewer: fit-review (Opus), read-only. Base `3dac5afb` (claude/fit). Method: `git diff 3dac5afb claude/fit-<lane>` per lane (source, styles, tests), read against `FIT_Briefs.md` (rules + "Phase B as run"), `FIT-LAYOUT-CONTRACT.md` §2 / §6 / §11 and `coordinator-notes.md`. No build or test was run (lock busy). Already-known items are **not** repeated: the fit-split / fit-steps / fit-board / fit-form root-class collisions, HeaderActions overflow, the `stream-fit.mjs` harness (FitNavContext, FIT_BASE), the label / td.muted baseline artifacts.

Lane heads reviewed: front-a fe07a118 · front-b 706369f8 · cash-a 3f9fe2e2 · cash-b 674c1707 · ops 7df4eb23 · guest de6507c6 · revenue f080c76a · people-a 9fc55aee · people-b 0cc7e249 · admin-a aec3b54a · admin-b fc6b70c6.

## Verdicts

| Lane | Verdict | Why (finding numbers) |
|---|---|---|
| fit-front-a | **FIX** | B1 (Dashboard counters lose font size); N1 (Front desk list, confirmed layout-only) |
| fit-front-b | OK | N8 (inline styles) |
| fit-cash-a | OK | N6 (duplicated day-card markup), nothing else |
| fit-cash-b | **FIX** | S1 (Expenses small-stats → StatStrip changes font size) |
| fit-ops | **FIX** | B1 (Maintenance report counters), S1 (HK / inventory / work orders / handover stats); DEC-061 clean |
| fit-guest | **FIX** | S2 (Messages "Simulated" / "Messaging is off" note dropped on 3 tabs), S1 (msg stats) |
| fit-revenue | **FIX** | S1 (channels / yield stats); N3, N4 |
| fit-people-a | OK | N2 (new hint copy, confirmed layout-only); gating and Close-day rule unchanged |
| fit-people-b | OK | — |
| fit-admin-a | OK | N5 (ServerDevicePanel moved), S4 (file outside ownership, subscreens block marker) |
| fit-admin-b | OK | S4 (subscreens block marker) |

Counts: OK 6 · FIX 5 · BLOCK 0 (the one blocker, B1, is fixed in the shell's `StatStrip` + two call sites; it does not block merging the lanes).

## Findings

### B1 — blocker (DEC-195 font reduction) · shell `StatStrip`, used by front-a and ops
`03 Prototype/src/components/fit.tsx:165` (claude/fit): `StatStrip` renders every value as `<strong className="money-stat">`. In `src/styles.css`, `.stat>strong` (a count) is 35 px, but `.stat>strong.money-stat` is 30 px; at ≤ 1250 px money-stat is 25 px (count stays 35); at ≤ 1050 px count 30 px vs money-stat 22 px. So every **count** that used to be a plain `.stat>strong` shrinks — on the Tab S7 landscape app area (1138 px) from 35 px to 25 px:
- front-a `src/features/overview/Overview.tsx:27` — Room occupancy, Today's arrivals, Room ready (old markup: plain `<strong>`; the occupancy "%" was a 20 px `<span>` and is now inline in the value).
- ops `src/features/maintenance/Maintenance.tsx:181` (`MntReportsFit`) — PM compliance, SLA breaches, Open work orders (old `MntReportsTab`: plain `<strong>`; MTTR and Parts cost were already money-stat).
Not affected (old markup already money-stat via `Stat` / explicit class): guest Guest services, revenue Groups, people-b Accounting, cash-b POS Day summary.
The check tool did not flag it (front-a reports Overview PASS), so its baseline kinds do not separate `.stat>strong` from `.stat>strong.money-stat`.
**Fix (integrator, shell):** give the `StatStrip` item an optional `money?:boolean` (class `money-stat` only when true) — or `plain?:true` — and pass it so each value keeps its old class: Overview: money only for "Payment receipts"; MntReportsFit: money only for MTTR and Parts cost. Let `value` accept a ReactNode so Overview can keep `{m.occupancy}<span>%</span>`. Add `.stat>strong:not(.money-stat)` as its own element kind in the font baseline.

### S1 — should-fix (DEC-195 font change) · `small-stats` converted to `StatStrip` · cash-b, ops, guest, revenue
The old tiles were `.small-stats strong` (25 px; 23 px ≤ 1050 px). In `StatStrip` they become `.stat>strong.money-stat` (30 / 25 / 22 px): **smaller at tab-port (23 → 22 px)**, larger on desktop ≥ 1250 px (25 → 30 px); labels move from `.small-stats span` to `.stat-label` (same 11 / 10 px). Contract §2 asks for identical computed sizes.
- cash-b `src/features/expenses/Expenses.tsx:93` (5 tiles, incl. Pending approval, Drafts)
- ops `src/features/housekeeping/Housekeeping.tsx:33,50`, `src/features/inventory/InventoryPage.tsx:26`, `src/features/maintenance/Maintenance.tsx:74` (work orders), `src/features/handover/HandoverPage.tsx:93` (old `small-stats hov-stats`)
- guest `src/features/messaging/Messages.tsx:51` (old `small-stats msg-stats`)
- revenue `src/features/channels/Channels.tsx:31`, `src/features/yield/YieldPage.tsx:28` (old `small-stats yld-stats`)
**Fix:** either a `StatStrip variant="small"` in the shell that renders the old `small-stats` markup as one sideways-scrolling row, or each lane keeps `<div className="small-stats …">` in the toolbar with a one-row layout rule, exactly as cash-a did (`fol-stats`, `cb-sums fit-stats`) and people-a did (Staff). Re-run the check on these pages at tab-port.

### S2 — should-fix (content dropped) · guest · Messages
`src/features/messaging/Messages.tsx:64`: the page-level `<MsgSimulated/>` (production: "Messaging is off — <reason>"; demo: "Simulated. Nothing is sent…") was removed from the desktop page and now appears only in the **Outbox** panel footer (`MsgSimulatedNote`). Triggers, Templates and Consent no longer show it — and the production Tech admin (DEC-079 D4) only ever sees Triggers / Templates, so they never see "Messaging is off".
**Fix:** put `<MsgSimulatedNote/>` in the `FitPage` toolbar (one line each, FitNote), or in the footer of all four panels (`TriggersPanels`, `TemplatesPanel`, `ConsentPanel` too).

### S3 — should-fix (shell, search palette) · all lanes with role-gated tabs
`SubScreen` has no permission, so the palette lists "Staff & payroll › Payroll / Advances / Full and final / Statutory rates" (people-a), "Accounting › Tally export" (people-b), "Expenses › Vendor payments" (cash-b), "OTA channels › Commission report / Payouts" (revenue), "Restaurant › Day summary" (cash-b) and "Messages › Outbox / Consent" (guest) to roles whose page shows no such tab. Every page falls back to its default tab (checked: HrDesk `tabs.includes`, Accounting `tally` guard, `chnTabFor`, POS `sales`, Messages `shown`), so **no data or DEC-058 gate leaks** — but the entry is a dead end.
**Fix (integrator):** optional `can?:(s,uid)=>boolean` on `SubScreen` (or a per-page tab filter export) used by `SearchPalette`; at minimum hide the DEC-058 payroll entries for non-pay roles.

### S4 — should-fix (merge) · subscreens.ts and files outside ownership
- `src/app/subscreens.ts` is edited by all eleven lanes. Six lanes (front-a `// FIT-FRONT-A`, cash-a `// FIT-CASH-A`, cash-b `// FIT-CASH-B`, people-a `// FIT-PEOPLE-A`, people-b `// FIT-PEOPLE-B`, admin-a, admin-b) append at the **same tail** after `// FIT-ADMIN` → textual conflicts on every merge after the first. admin-a and admin-b both appended directly under `// FIT-ADMIN` **without their own marker** (admin-a: settings + approvals lines; admin-b: access + backup + prefs lines). Resolution is a union of the lines; no id collides within a page. **Fix at merge:** keep all lines, add `// FIT-ADMIN-A` and `// FIT-ADMIN-B` markers above the respective groups.
- `tests/stream-wd5-gaps.mjs` is edited by guest (line ~101, Templates assertion → FitTabs markup) and revenue (the `channels()` helper wraps `FitNavContext`): different hunks, should auto-merge; re-run the file after both.
- admin-a edited `03 Prototype/tools/tablet-masters-check.mjs` (adds a `group()` helper that presses `.fit-split-back` below 900 px) and appended to `src/styles/z-tablet-masters.css` — neither is in its ownership row. Both edits are layout-harness only and needed; **integrator to accept explicitly** (the brief requires "Integration needed").

### N1 — note · front-a · Dashboard "Front desk today" (coordinator asked to confirm)
`src/features/overview/Overview.tsx:35`: `BookingTable list={stayList}` (was `stayList.slice(0,5)`). Same source arrays (`arrivals`, `departures`, `bookings` In house), same `BookingTable compact`, rows open the same dialog, inside a ScrollPanel. Confirmed **layout-only** (more rows shown, nothing hidden). The tab count for "In house" still reads `m.occupied` as before. The stat icons and the occupancy progress bar of the old tiles are gone (decorative; the figures stay).

### N2 — note · people-a · Day closing copy and gating (coordinator asked to confirm)
- Header `Close <date>`: `disabled={busy||!canClose||!!blockers.length}`, command `DAY_CLOSE` key `dayclose:<date>` — identical to old step 4. `Open next business day`: `disabled={busy||!closed||!canClose}`, `NEXT_DAY` — identical to old step 6. Only one of the two is shown (open day → Close; closed day → Open next), which matches the old steps' visible buttons. "Try posting / closing again", Post room nights, Demo interrupt, Count & submit keep their conditions verbatim.
- New copy: `DayClosing.tsx:95` "Close … is the first button in the page header; it stays there on every step." and `:104` "Open next business day is the first button in the page header." plus step-rail notes ("n to resolve", "n blocking", "n waiting", "<date> reopened") derived from the same `closingPreflight` / approvals values. Pure layout guidance, no logic → **layout-only, accept**.
- Step 7 "Reopen & corrections" uses `user.role==='Owner'` only to choose a one- or two-column layout; `ReopenDay` itself still returns the form only for the Owner (unchanged). Default step = step 7 while a day is reopened (the old page put `ReopenDay` on top in that case) — equivalent prominence.
- The desktop `ReopenForm` / `ReopenedDay` are now a second copy of the markup (`ReopenDay.tsx`, `if(!l.phone)` branches): content checked line by line against the old desktop markup — same notes, links, form fields, Close-again rule. The reopened-day charge form is exercised by `stream-reopen.mjs` (`render(...,'panel',false)` hits the new branch).
- HrDesk: `tabs` (the DEC-058 role list) is unchanged and still filters the chosen tab (`tabs.includes(chosen)`); every `owner` / `edit` / `pay` / `mgr` gate inside the sections is unchanged.

### N3 — note · revenue · Yield publish requests and Booking site (coordinator asked to confirm)
- `YieldPage.tsx`: the "Publish requests" tab exists while `yldProposals(s).length>0` — exactly when the old `<Proposals/>` card rendered (it returned null on an empty list). Entry point = the tab, with a warn badge and count while proposals are pending; `Proposals` keeps its `slice(0,5)` and actions. Nothing hidden; the card is just no longer above every tab.
- `BookingSite.tsx`: `main` has only the guest preview and the web requests on this page (no settings or room-rate screens), so Bookings + Preview cover all content. The contract §6 row ("Settings, Rooms & rates") should be corrected in FIT-DONE. New StatStrip figures are new content (counts already on the page).
- Groups: the DEC-076 credit note now shows only on Companies and the "Rooms held for groups" note only on Blocks (both were page-wide). Acceptable (each sits with its subject).

### N4 — note · revenue / guest / front-b / admin-a · four copies of the 900 px media hook
`useWideSplit` (front-b `guests/useWideSplit.ts`), `useWide` (guest `Customers.tsx`), `useNarrow900` (admin-a `HotelSettings.tsx`) and a non-reactive snapshot in revenue `Groups.tsx:47` (`useState(()=>matchMedia…)[0]` — never updates on rotation, so a tablet rotated to landscape does not pre-select the first block). **Integrator:** export one `useWideSplit()` from `components/fit.tsx` and use it in all four (fixes the Groups rotation case).

### N5 — note · admin-a · production `ServerDevicePanel`
`HotelSettings.tsx:69`: the production-only server/device panel moved from the top of Master configuration (seen on every group) into the "Data and system" group only. Still reachable; confirm with the owner or keep it in the toolbar as a one-line FitNote.

### N6 — note · duplicated render code (maintenance risk, not a layout breach)
Desktop-only second copies that must now be kept in step with the original: cash-a `CashBook.tsx` desktop `DayCard` / range view; ops `MntReportsFit` vs `MntReportsTab` (the latter kept for the print dialog), `HovTemplatesFit` vs `HovTemplatesBody`, `HovDashboardBody fit` branch; people-a `ReopenForm` / `ReopenedDay` desktop branches; guest `CustomerPanelFit` vs `CustomerPanelPhone`. Each copy was checked against its original for content and gating (same commands, same disabled conditions, money columns still behind `cost` / `showMoney` / `permission('billing')`). Integrator: no action now; list them in FIT-DONE as follow-ups.

### N7 — note · DEC-061 (ops + shared) — clean
Housekeeper "My tasks" (`Housekeeping.tsx:33`): StatStrip of the four task counts only. Maintenance tech / housekeeper views: no money component added. `MntReportsFit` keeps `cost` on Parts cost and the SLA-breach Parts column; Spares / AMC keep `cost`; Inventory keeps `showMoney` on Stock value, reorder "approx." and write-off totals. The Attendant-load column and repair requests carry no amounts. `gsv-my-requests` still shows no prices. No shared component (FitPage, StatStrip, BoardColumns) injects money. Customers 360 Money tab is behind `permission('billing')`, as the old lifetime/average tiles were.

### N8 — note · front-b · inline styles
`GuestProfile.tsx` / `InHouse.tsx` `style={{maxHeight:'none',overflow:'visible'}}` and `Reservations.tsx` `style={{margin:0,gap:12}}` (no colours, no fonts). Already flagged by front-b; integrator moves them to a desktop sheet.

### Checks with no finding
- **Mobile untouched:** no lane changed anything under `src/mobile/**`, `src/app/mobile/**`, any `*Mobile.tsx` / `Mobile.tsx` or an `m-*.css` sheet. Every added CSS rule in the new / changed sheets starts with `.app-shell` (grep of all added selectors). Shared desktop files that the phone also renders keep the phone path byte-identical (`CashOutPanel phone`, `ExpBanners` without `fit`, `PosAvailability` — `useFitNav().desktop` is false outside the desktop shell, `HrDesk phone`, `ReopenDay phone`, `CustomersPage` → `CustomersPhonePage`, `PlansExtrasPage` phone branch, `ServerCollections phone`).
- **No font-size, no hex colour** added in any lane stylesheet; colours are `var(--…)` tokens (`--c-ffffff` is themed to `--surface`). New touch targets ≥ 44 px (`.grp-row`, `.f-report-item`).
- **Layout-only:** no change to selectors, stream functions, commands, handlers, `can()` / `permission()` checks, fixtures, `types.ts` / SCHEMA or money / GST code in any lane. Enabling conditions verified unchanged for Close day / Open next day, Post room nights, Lock / Reopen cash-book day, cash-out buttons (`why` / `shiftOk` kept), expense Approve / Reject (register + approval requests), folio actions (cash-a renders the same `BookingModal` in the record pane; the arrow still opens it as a dialog), Generate stayover, PM generate, Push now, Submit checklist (`HovRunActions` now in the panel footer, same rule), Add exception (form still wraps the `GuardedButton`).
- **Status / tab ids:** Expenses status tabs `All, Draft, Pending, Posted, Rejected, Reversed, Void` unchanged; expense sections, maintenance, handover, accounting, channels keep their old ids; inventory, groups, guest-services, messages, yield, access move from label ids to kebab ids (no caller passes the old labels: no `go(page,{tab})` in desktop code outside the shell). Groups' internal `setTab('Master folio')` became `setTab('master-folio')`.
- **Lists:** caps added are `usePaged` with Show more only (Messages outbox, Booking-site requests, Groups blocks, POS kitchen columns, Customers money list); no cap removed except N1. Inventory Movements keeps its `slice(-80)`, Expenses keeps `EXP_LEDGER_CAP`.
- **Tests:** no test deleted or skipped. Updated tests only open the right tab / step / section: `stream-customers-legacy.mjs` (legacy rows asserted on the Customers 360 Legacy tab via `initialTab`; the list's "(3 legacy)" still asserted on the page), `stream-wd5-gaps.mjs` (FitTabs markup; channels tab via FitNavContext), `stream-reopen.mjs` (default step 7 + preflight step + closing-summary step), `stream-prefs.mjs` / `stream-themes.mjs` (each Prefs section opened; card order now = section order; "Reset" card heading replaced by the sticky-bar assertion `Reset this device` + `fit-form-actions` in every section — acceptable). `tools/tablet-masters-check.mjs` (admin-a) only adds the Back step.
- **Content parity** spot-checked: compliance (main has Calendar / Dashboard views only; Filings / Licences do not exist), expenses (all seven SCC sections + approval requests reachable; masters links in "Lists & photos"), approvals (third tab "Pending expenses" instead of a card), cashier (count panel buttons sticky), POS (86 list a popover), handover ("Other shifts" now steps — more visible than before).
