# Accessibility and performance audit (integration/w05), 25 Oct 2026

Report only. No product code changed, nothing committed. Build under test: `03 Prototype\dist\index.html` (2.11 MB, one inline script of 1.86 MB), branch integration/w05, served on 127.0.0.1:4182. Scenario FY26 (stored state 8,277,199 characters).

## 0. Method and limits

- Browser: headless Chrome 156 driven by my own Playwright script (own process, own profile), plus the built-in browser pane for first checks (the pane's page was hidden, so its paint timings were not usable). CPU throttling uses CDP `Emulation.setCPUThrottlingRate` (4x and 6x). No real device, no Chrome 83.
- Desktop pass 1366x768; phone pass 360x800, touch, mobile UA. Owner (Sagar) for all pages; Housekeeper and Maintenance for DEC-061.
- Pages: Sign-in, Modules Home, Dashboard, Arrivals and check-in, Reservations, Guests and folios, Cashier shift, Cash book, Housekeeping, Reports, Staff and payroll, Compliance, Master configuration, Customers.
- Contrast: computed from rendered colours (text colour blended over the first opaque ancestor backgrounds, ancestor opacity applied). Gradients and images are not resolved. 43 distinct pairs on desktop, 29 on phone.
- Other agents were running browsers on the same PC during my runs, so absolute timings, especially 4x/6x, are noisy (two 4x runs differ by up to 2x on the cold path). Treat them as order of magnitude. The 6x run is one run.
- Not done: screen-reader run (NVDA/TalkBack), real tablet, Chrome 83 execution (static analysis only), zoom to 200 percent.

## 1. Contrast results (Maroon and Sand)

Theme pairs, calculated: text #2B1D16 on #FAF6F0 15.12; muted #6B5547 on #FAF6F0 6.47; muted on #EFE4D6 5.55; primary #7A1F2B on #FAF6F0 9.48; white on #7A1F2B 10.20.

Across all 14 pages, every enabled text/background pair measured passes AA (lowest 4.60 on a 10 px "Blocked" note; status pills 4.78 to 4.82; sand-tinted chips 4.66 to 4.79). Placeholder text passes (#757575 on white 4.61; #6B5547 on white 6.96). The only failing pair is a disabled primary button label: #BD8F95 on #7A1F2B = 3.65 (disabled controls are exempt from 1.4.3, see C-3). Several passes are within 0.2 of the limit, so any palette tweak risks a failure (C-2).

## 2. Accessibility findings

Severity: S1 blocks a user or breaks the law of the target; S2 serious barrier or breaks a project rule on a main flow; S3 moderate; S4 minor. S1 count is zero on what I could execute.

| ID | Sev | Page / element | Measured vs target | Suggested fix |
|---|---|---|---|---|
| A-01 | S2 | All dialogs (Reservations "+ New reservation", Folio open guest, Arrivals "Start check-in", Compliance "Mark filed", Housekeeping "Room details", Cashier "Count drawer" on phone); `ModalHost.tsx` | After Escape the dialog closes but `document.activeElement` is `BODY` in 6 of 6 cases, desktop and phone. WCAG 2.4.3: focus should return to the opener. | Store `document.activeElement` when a modal opens and call `.focus()` on it at close (guard if removed). |
| A-02 | S2 | Tablet and desktop layout, every page. `button.icon-btn` 32x32, `button.text-btn` 24 high, `button.btn.small-btn` 29 high, "Modules Home" crumb 32 high, Search 36, `select` 25 high | Targets under 44 px: Reservations 228 of 229, Folio 224 of 225, Housekeeping 76 of 76, Dashboard 56 of 56, Cash book 31 of 33, Cashier 26 of 26. Target 44 to 48 px for the Tab S7 touch use. | Add a `@media (pointer:coarse)` rule that raises `.icon-btn`, `.text-btn`, `.small-btn`, `.mini-room`, selects and date inputs to min 44x44 (48 per CLAUDE.md), and the row link in lists. |
| A-03 | S2 | Phone 360, targets under 48 px | Counts of interactive elements under 48: Payroll 18 of 40, Folio 9 of 72, Reservations 8 of 70, Reports 7 of 25, Compliance 6 of 23, Cash book 4 of 28, Dashboard 4 of 23, others 2 to 3. "Modules Home" crumb is 99x40 on every page. Target 48. (Matches known W05-6.) | Make `.m-crumb`/Modules Home button min-height 48; inspect the 44 to 47 px items in Payroll (not listed individually in my scan). |
| A-04 | S2 | Phone 360, text under 16 px | Every page: `small` 13 px (155 on Reservations, 158 on Folio), buttons 14 px (160 on Reservations), `span.m-date.open` 12 px, pills 13 px, Customers `span.muted` 10 px (51). Target 16 px (CLAUDE.md phone rule). WCAG itself sets no minimum, so S2 is against the project rule. | Raise phone base and secondary text to 16 (secondary may be 14 only if the rule allows), pills to 14 minimum; remove the 10 px muted. |
| A-05 | S2 | Focus visibility: sign-in (`button.signin-user`), Arrivals/Reservations/Folio/Customers/Master config search inputs (desktop), Cash book and Reports date inputs (tabbed) | Sign-in: `outline:none`; only a pale tint (#F5E9E9) and a 1 px border #9A5659 (4.6:1 against the tint) mark focus. Search inputs: computed outline 0 px, no box-shadow on the input or its two ancestors when tabbed. Date inputs: outline reported `none` after Tab although `.focus()` shows a 3 px ring (needs a manual look). Target WCAG 2.4.7 and 2.4.11 visible ring. The global rule `button/a/input:focus-visible{outline:3px solid #7a1f2b}` is fine, but `.signin-user`, `.tile`, `.drawer-item` and `.yld-cell` switch it off (`outline:none`). | Remove the `outline:none` overrides or replace them with a 2 px ring; add `label.search:focus-within{outline:3px solid #7a1f2b}`; check the date inputs by eye. |
| A-06 | S2 (conditional) | Old WebView (Chrome 83): `:focus-visible` | Needs Chrome 86. In `styles.css` the focus ring is one selector list, so Chrome 83 drops the whole rule: no focus ring anywhere. Static check only. | Add a plain `:focus` fallback before the `:focus-visible` rule, or write separate rules. |
| A-07 | S3 | Colour-only state: Dashboard `button.mini-room.*` room tiles | Visible content is only the room number (10 px) plus the tile colour; the state ("Occupied") is only in `aria-label`. The legend (`i.dot.*` with text) is separate. WCAG 1.4.1. Other pills, chips and buttons all carry text, 0 empty pills on phone. | Add a letter/icon or a text suffix on the tile (O, R, D), or an outline pattern per state. |
| A-08 | S3 | Non-text contrast (1.4.11): form controls and cards | Input/date/select border #CDB9A3 on white = 1.9:1; search label border #E6D9C8 on #FAF6F0 = 1.29:1; sign-in card border same 1.29:1. Target 3:1 for the control boundary. | Darken input borders to about #8C7766 (3:1) or add a visible fill. |
| A-09 | S3 | Dialog names: `ModalHost.tsx` `role=dialog aria-label` | Only a booking dialog gets a meaningful name; the other types read "Hotel Control Centre dialog". Target: name = dialog title. | Use `aria-labelledby` pointing at the modal heading. |
| A-10 | S3 | Repeated identical accessible names | Cash book 14 buttons named "Open day"; Housekeeping 42 "Room details"; Arrivals 2 "Start check-in" (with no guest in the name). WCAG 2.4.6 / 4.1.2. | Add `aria-label="Open day 24 Oct"`, `"Room details, room 108"`, `"Start check-in, <guest>"`. |
| A-11 | S3 | Dialog initial focus | First focus goes to the Close button, not the first field or the heading. | Focus the first form control, or the dialog title. |
| A-12 | S3 | Duplicate h1 on phone | Modules Home phone has 2 `h1` "Modules"; Customers phone has 2 `h1`. | One h1 per page; the app-bar title should be a `div`/`p`. |
| A-13 | S3 | Old WebView: `100dvh` | Used by `.modal{max-height:calc(100dvh - 56px)}` and `.m-shell{min-height:100dvh}`; Chrome 83 ignores the declaration (needs 108), so modals/phone shell have no height bound. | Put a `vh` declaration before each `dvh` one. |
| A-14 | S4 | `<table>` on Dashboard, Arrivals, Reservations, Folio, Cashier, Cash book, Reports, Payroll, Compliance | 9 tables, header cells present, `scope` on none, no `<caption>`. | Add `scope="col"` and a caption (visually hidden). |
| A-15 | S4 | Icons | Most lucide SVGs have no `aria-hidden` (Reservations 119 of 121, Folio 120 of 122, Dashboard 34 of 35). No image tags exist, so no alt issues. They carry no title, so effect on AT is small. | Pass `aria-hidden="true"` in the shared icon wrapper. |
| A-16 | S4 | Bypass blocks | No skip link; `main`, `nav`, `header`, `footer` landmarks exist (footer absent on phone), `lang="en"`, title identical on every page ("Hotel Control Centre · Interactive Prototype"). | Add a "Skip to content" link; update `document.title` per page. |
| A-17 | S4 | Sub-12 px text on desktop | 8 px "DEMO — FICTIONAL DATA", 9 px labels (`demo-label`, "Welcome,", eyebrow), 10 px chips, avatar, `kbd`. The demo strip is removable, but the 9 to 10 px text on Dashboard is not. | Minimum 12 px for informative text. |
| A-18 | S4 | Disabled button | "Complete cleaning"/"Submit count & close shift" disabled label 3.65:1. Exempt from 1.4.3, but low vision users cannot read why. | Raise to 4.5:1 or show the reason beside it. |
| A-19 | S4 | Input font size on phone | `input` under 16 px: 1 on Customers phone (search 271x17 box); on desktop 2 to 10 per page. iOS zoom is not a concern for the Android target, but it breaks the 16 px rule. | 16 px on all phone inputs. |

Passes worth recording: all inputs and selects have an accessible name (0 unnamed form controls, 0 unnamed buttons on all pages and both widths); date inputs on Cash book and Reports have `aria-label`; all dialogs have `role="dialog"`/`alertdialog`, `aria-modal="true"`; the focus trap held (25 Tab + 6 Shift+Tab presses, 0 escapes, desktop and phone, all dialogs plus the Ctrl K palette); Escape closes every dialog; toast uses `role="status"` `aria-live="polite"`; exit dialog is `alertdialog`; viewport meta allows zoom; no horizontal scroll at 360 on any page; `prefers-reduced-motion` handled; no console errors or warnings on any page.

## 3. DEC-061 money visibility (R-01), asserted

Owner decision: housekeeping and maintenance staff may see their own pay and payslip on "My shifts & pay"; never hotel money, rates, folios, cash, revenue, or another staff member's pay. Tested as Ravi (Housekeeper, desktop), Kavya (Housekeeper, phone 360), Joseph (Maintenance, desktop and phone for the module checks).

| Assertion | Result |
|---|---|
| Own pay and payslip visible on My shifts & pay | PASS. Ravi: own gross/net, PF, ESIC, PT and 6 month net history; Kavya: "Latest payslip September 2026" net 12,245.37 and a 6 month payslip list; Joseph: own payslip lines. All carry the user's own name only. |
| Modules shown | Housekeeping and Maintenance, Guests and Services (tile only), Stock and Purchasing, People and Payroll. No Dashboard, Front Desk, Cash and Billing, Accounts and Reports, Revenue and Sales, Customers or Admin tile. |
| Cash book, Cashier shift, Reports, Guests and folios, Staff and payroll, Compliance, Accounting, Tariff reachable from any visible module | PASS: none reachable (checked in all 4 hotel-money modules, 3 users). |
| Hotel money on pages they can open | PASS: Housekeeping, Maintenance, Checklists and handover, Stock landing show no rupee amount and no words rate/folio/revenue/cash/payment. (Owner sees "Stock value ₹3,85,314" on Stock and Purchasing; Ravi and Joseph do not.) |
| Another staff member's payslip | PASS in the UI: the page has no staff picker; payslip list belongs to the signed-in person. Other names visible on the page are only the demo "Preview as" toolbar (a demo control, removed in production). |
| Search palette (Ctrl K) leaks | PASS: "folio", "rate", "cash", "revenue", "Kavya" return "Nothing matches"; "payroll"/"payslip" return only "My shifts & pay". |
| Not tested | Direct state manipulation or API calls; the prototype has no URL routes or server, so a role check on commands (not only on the UI) must be confirmed in the services layer tests. Guests and Services tile opens for Ravi/Joseph but none of its sub pages (Guest services, In-house stays, Guest requests, Messages) were offered by name; I did not see what the tile opens (S4 to confirm). |

## 4. Performance

Setup: FY26, Chrome 156 headless on this PC, cache disabled for loads, 1 viewport 1366x768. Long task = main thread task over 50 ms. "Wait" figures are click to two animation frames after the page content is present. Rates 4x and 6x are CPU throttle; 4x run 1 and run 2 are both quoted where they differ. A Tab S7 (Snapdragon 865+) in a current WebView is roughly 2 to 3x slower than this desktop for single-thread JS, an older tablet or phone 4 to 6x.

### 4.1 Loads

| Measure | 1x | 4x | 6x |
|---|---|---|---|
| Sign-in screen visible, returning user (state in IndexedDB), first paint | 130 to 150 ms (first run in a session 416 ms) | 520 to 690 ms (first run 2.2 to 2.6 s) | 1.46 to 1.68 s (first run 4.0 s) |
| Sign-in or Modules Home to interactive, returning user | about 0.3 s | about 1 to 2 s | 2 to 3 s |
| First run, empty IndexedDB (generates FY26 history in the browser), time to sign-in screen | 0.67 to 1.0 s (cold first 1.37 s) | 6.7 to 13.6 s | 17 to 23.7 s |
| Main thread blocked by that first-run generation (single long task) | 570 to 850 ms | 6.1 to 12.3 s | 15.7 to 21.4 s |
| JS parse and eval of the 1.86 MB bundle | DCL 77 to 100 ms warm cache-disabled | 550 to 650 ms | 1.2 to 1.6 s |
| Read and parse stored 8.28 MB state (IDB get / JSON.parse) | 21 to 26 ms / 41 to 63 ms | 22 to 44 ms / 230 to 455 ms | 22 to 66 ms / 590 to 1,018 ms |
| Network | 2.1 MB uncompressed from local server; no compression in `serve.mjs`; app is a single HTML so on a tablet the file is local | | |

Notes: the first-run cost is paid on a new device, after "Reset", and on every scenario switch that regenerates the data. The 4x and 6x numbers for it are 10 to 25 times the 1x cost, partly noise from other agents on the PC.

### 4.2 Opening and using heavy pages (returning user, state loaded)

| Action | DOM nodes | 1x time (longest task) | 4x time (longest task) | 6x time (longest task) |
|---|---|---|---|---|
| Dashboard | 423 | 181 ms (0) | 404 to 437 ms (198 to 230) | 638 ms (412) |
| Reservations list (1,279 booking rows, 100 rendered) | 2,327 | 203 ms (131) | 1,068 to 1,367 ms (864 to 1,108) | 2,236 ms (1,848) |
| Guests and folios list | 2,240 | 167 ms (97) | 1,185 ms (913) | 1,835 ms (1,461) |
| Cashier shift | 325 | 101 ms (0) | 352 ms (213) | 586 ms (441) |
| Cash book, default 14 days | 400 | 101 ms (0) | 321 ms (220) | 1,219 ms (906) |
| Cash book, 1 Apr to 24 Oct range | 1,521 | 202 ms (76) | 1,115 to 1,195 ms (895) | 2,806 ms (2,128) |
| Housekeeping board | 909 | 96 ms (52) | 550 ms (400) | 1,560 ms (1,208) |
| Reports, today | 370 | 118 ms (0) | 432 ms (305) | 1,227 ms (860) |
| **Reports, "FY history" (full financial year)** | 1,264 | **730 ms (634)** | **4,931 to 6,302 ms (4,742 to 6,030)** | **8,270 ms (7,991)** |
| Reports tabs after FY (Revenue and tax, Receipts, Expenses and cash, Occupancy) | 1,160 to 1,270 | 130 to 190 ms (57 to 66) | 417 to 863 ms | 1,058 to 2,117 ms |
| Staff and payroll | 393 | 84 ms (0) | 247 ms (118) | 697 ms (325) |
| Compliance calendar | 309 | 79 ms (0) | 357 ms (228) | 953 ms (454) |
| Master configuration | 385 | 152 ms (102) | 1,059 to 1,867 ms (885 to 1,704) | 3,808 ms (3,248) |
| **Customers (1,155 list; 100 rows rendered with Show more)** | 875 | 171 ms (89) | 1,392 ms (1,236) | not run |
| Customers search "Rohan" / clear | 411 / 875 | 111 / 80 ms | 274 / 389 ms | 520 / 727 ms |

### 4.3 Save, storage, memory

| Measure | Value |
|---|---|
| Stored state (IndexedDB `hcc`/`state`/`current`) | 8,277,199 characters; 7.94 MB UTF-8, about 15.8 MB if held as UTF-16 |
| Save trigger | One debounced write (about 0.7 s after the command) |
| `JSON.stringify` of 8.28 MB on the main thread | 52 ms (1x), 218 to 297 ms (4x), 738 ms (6x) |
| IndexedDB `put` to transaction complete | 64 ms (1x), 128 to 148 ms (4x), 432 ms (6x) |
| Command handling itself (housekeeping "Start cleaning" click) | 177 ms long task (1x), 398 to 1,036 ms (4x), 2,869 ms (6x) |
| Every command stringifies and rewrites the whole 8 MB; typing in a field that dispatches commands has the same cost | |
| JS heap after the whole walk (GC forced) | 26.2 MB used of 27 MB (1,866 DOM nodes, 268 listeners); no growth between pages (19 to 28 MB, no leak signal). Raw 8 MB string plus parsed object tree means a peak of about 50 MB at load |
| Console errors | none on any page, any run |

## 5. Performance findings

| ID | Sev | Page / element | Measured vs target | Suggested fix |
|---|---|---|---|---|
| P-01 | S2 | First-run data generation (fixture history, Apr to Oct) | 570 to 850 ms main thread block at 1x; 6 to 12 s at 4x; 16 to 21 s at 6x (noisy). Target under 3 s to first screen on tablet. | Ship FY26 history as a pre-built compressed JSON, or generate in a Web Worker with a progress screen; show a loader before the long task starts. |
| P-02 | S2 | Reports, "FY history" | 730 ms (1x), 4.9 to 6.3 s (4x), 8.3 s (6x), one 5 to 8 s task. Target under 1 s on tablet. | Compute the FY summary once and memoise per business date; render only the first 31 days and "Show more"; compute off the render path (useMemo + startTransition or worker). |
| P-03 | S2 | Whole-state save | 8.28 MB stringified and rewritten per command: 52 ms (1x), 218 to 297 ms (4x), 738 ms (6x) of main thread block, plus 64 to 432 ms put. A busy cashier on a tablet will feel it after every command. | Split the store (history, bookings, folios, config) and save only the changed slice; or save the command log plus periodic snapshots; stringify in a worker. |
| P-04 | S2 | Command latency | 177 ms long task for a simple housekeeping command at 1x, 0.4 to 1 s at 4x, 2.9 s at 6x. Likely a deep clone of the whole state per command (`clone.ts`, `structuredClone`). | Use structural sharing (immer or copy-on-write on touched slices) instead of cloning 8 MB; measure the reducer. |
| P-05 | S3 | Reservations, Folios lists | 2,240 to 2,327 DOM nodes; 131 ms task at 1x, 0.9 to 1.1 s at 4x, 1.5 to 1.8 s at 6x. Opens 100 rows at once. | Render 30 rows then Show more, or virtualise; drop the duplicate per-row icon button. |
| P-06 | S3 | Customers (1,155) | 171 ms at 1x, 1.4 s at 4x (a 1.2 s task). 100 rows rendered. Search is fast (under 120 ms 1x). | Render 40 rows; defer the avatar/stat computation. |
| P-07 | S3 | Master configuration | 152 ms at 1x, 1.1 to 1.9 s at 4x, 3.8 s at 6x for 385 nodes: computation, not DOM. | Lazy-compute each group when expanded. |
| P-08 | S3 | Cash book wide range | 1 Apr to 24 Oct: 1,521 nodes, 202 ms at 1x, 1.2 s at 4x, 2.8 s at 6x. | Summarise by month with expand, or paginate by week. |
| P-09 | S3 | Load-time parse | `JSON.parse` of 8.28 MB 41 to 63 ms (1x), 230 to 455 ms (4x), 0.6 to 1.0 s (6x), then render. | Same as P-03; or stream-parse per slice. |
| P-10 | S4 | Bundle | 1.86 MB inline script, unminified-by-gzip delivery (`serve.mjs` sends no compression); the Capacitor build loads from disk so it is only parse time (about 0.1 s at 1x, 0.6 s at 4x). | Fine for now; consider code-splitting the 26 feature modules if tablet parse time matters. |
| P-11 | S4 | Memory | 26 MB steady, about 50 MB peak. Comfortable for a 4 GB tablet. State string kept alongside the parsed object during load. | Release the raw string after parse. |

## 6. Old-WebView (Chrome 83) static findings

Build target is esbuild `es2020` (syntax is fine for Chrome 83), but runtime APIs and CSS are not downlevelled. Found in `dist/index.html`:

| Item | Needs | Where | Effect on Chrome 83 | Sev |
|---|---|---|---|---|
| `Array.prototype.at()` (5 uses) | Chrome 92 | Cash book period summary (`o.at(-1)?.closing`), accounting lock lookup (`.sort().at(-1)`), messaging log (`g.at(-1)`), unlock log (`c.unlocks.at(-1)`) | `TypeError`: Cash book and these screens would fail to render or act | S2 (S1 if Chrome 83 is a supported device) |
| `structuredClone` unguarded (leave policy command, `staff.ts:184`) | Chrome 98 | `HR_LEAVE_POLICY_SET` | Command throws; the other use is guarded (`clone.ts`) | S2 |
| `:focus-visible` (16 rules) | Chrome 86 | global focus ring | no focus ring (see A-06) | S2 |
| `100dvh` (5 uses) | Chrome 108 | modals, phone shell | no height bound (see A-13) | S3 |
| `:has()` toast offset, `:is()` | Chrome 105 / 88 | toast bottom position | toast may overlap the bottom bar | S4 |

Android 13 WebView on the Tab S7 updates through the Play Store and has none of these problems; this only matters for devices with a frozen old WebView.

## 7. Top 10 fixes in priority order

1. P-03 and P-04: stop cloning and re-serialising the whole 8 MB state on every command (slice the store, snapshot plus log). It affects every action on every device and costs 0.4 to 3 s of blocked main thread per command on a throttled tablet.
2. P-02: make Reports "FY history" fast (memoise, show the first month, worker). 5 to 8 s freeze at 4x to 6x.
3. P-01: remove or move the first-run history generation (pre-built data or worker plus loader). 6 to 20 s on a new device or after Reset.
4. A-01: return focus to the opener when a dialog closes (all dialogs, one change in `ModalHost.tsx`).
5. A-05 and A-06: restore a visible focus ring on sign-in, search and date inputs, and add a `:focus` fallback for Chrome 83.
6. A-02 and A-03: 44 to 48 px touch targets (coarse pointer rule for the tablet layout; Modules Home crumb and the 44 to 47 px items on the phone).
7. A-04: 16 px text on the phone (small 13 px, buttons 14 px, pills 13 px, muted 10 px).
8. Chrome 83 compatibility (Section 6): replace `.at(-1)` with `[length-1]`, guard `structuredClone`, add `vh` before `dvh`, if old WebViews are in scope.
9. P-05, P-06, P-07, P-08: render 30 to 40 rows then Show more on Reservations, Folios, Customers, Cash book range; lazy-compute Master configuration groups.
10. A-07, A-08, A-09, A-10: colour-only room tiles, 3:1 control borders, named dialogs (`aria-labelledby`), unique names for repeated buttons ("Open day", "Room details", "Start check-in").

## 8. Counts

| Severity | Accessibility (A-01 to A-19) | Performance (P-01 to P-11) |
|---|---|---|
| S1 | 0 | 0 |
| S2 | 6 (A-01 to A-06; A-06 is conditional on Chrome 83) | 4 (P-01 to P-04) |
| S3 | 7 (A-07 to A-13) | 5 (P-05 to P-09) |
| S4 | 6 (A-14 to A-19) | 2 (P-10, P-11) |

The Chrome 83 items in Section 6 add two more S2 candidates (`.at()`, `structuredClone`; `.at()` becomes S1 if Chrome 83 is a supported device). DEC-061 assertions all pass in the UI; command-level enforcement was not tested.

Working files (scripts and JSON) are in the session scratchpad, not in the project. Server on port 4182 stopped at the end; browser tab left on the app, viewport at desktop preset.
