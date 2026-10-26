# HCC — Customisation features: survey and plan (v1, 26 Oct 2026)

Two parts. **Part 1 (§1–6):** personal and per-device customisation of the app (text size, look, layout, formats, behaviour) for the desktop build and the Android APK. **Part 2 (§7–11):** the **masters** that let the owner configure the hotel itself (floors, rooms, room types, meal plans, payment methods, menu, …) and a multi-agent plan to build them fast. Hotel-wide *settings* (values, limits, dated rates) already live in **Master configuration** (`HCC_Master_Configuration_Design_v1.md`, DEC-057/058) and are not repeated; Part 2 adds the *lists* that configuration and every module pick from.

## 1. What the app offers today

| Area | Today | Where |
|---|---|---|
| Display mode | Auto / Desktop / Mobile, per device, localStorage `hcc-display-mode` (DEC-044) | Desktop top bar, demo toolbar, phone user menu, Hotel configuration › "Device & display (this device only)" (`src/app/display.tsx`) |
| Touch density | 44 / 48 px targets on coarse-pointer devices via `--hcc-touch-dense` / `--hcc-touch-primary` (DEC-060: "a density toggle can be added later") | `src/styles/z-touch.css`, `z-tablet.css` |
| Motion | Honours the OS `prefers-reduced-motion` only | `src/styles.css`, `home.css` |
| Theme | One theme, Maroon & Sand, WCAG AA | all CSS |
| Text size | None in app. Root is `font-size:14px`; ~1,300 `font-size` rules in **px** (9–25 px), 1 `rem` | `src/styles.css`, `src/styles/*.css` |
| Start page | Fixed per role (`landing` in `src/app/nav.ts`) | — |
| Phone tabs | Fixed per role, max 4 + More (`roleTabs` in `src/app/mobile/tabs.ts`) | — |
| Home tiles / sidebar | Fixed category order; sidebar is long (HANDOFF open item 6: collapsible groups) | `ModulesHome.tsx`, `NavDrawer.tsx` |
| Lists | Page size fixed: 100 rows desktop, 50 cards phone (`usePaged`, 18 call sites) | `src/components/ShowMore.tsx` |
| Toasts | Fixed 6.5 s | `App.tsx` |
| Number / date format | `Intl` `en-IN` hard-coded in 18 files (₹ with lakh grouping, "26 Oct 2026") | `src/domain/money.ts`, `src/components/ui.tsx` |
| Language | English only, `lang="en"` | `build.mjs` |
| APK | Status bar maroon, splash sand; `MainActivity` does not set `textZoom`, so the Android **system font size already scales the app's px text** in the WebView (untested at "Largest") | `04 Android` |

Per-device settings follow one rule set already (DEC-044): saved in the browser or APK on that device, never in hotel data, never audited. This plan keeps that rule and adds a second tier, "my preferences", that can later follow a user between devices.

## 2. Technical findings that shape the plan

1. **Text size needs a build-time transform, not a root font-size.** Because every rule is in px, changing `:root{font-size}` does nothing. Cheapest safe route: in `build.mjs` (and the dev server's CSS path) rewrite `font-size:<N>px` → `font-size:calc(<N>px * var(--hcc-text,1))`. Touch targets (`min-height`), paddings and widths stay in px, so layouts hold; text alone scales. A test asserts no plain `font-size:<N>px` survives in `dist/index.html`. The phone 16 px form rule scales with it (never below 16 px at scale ≥ 1). Alternative, CSS `zoom` on `#root` (scales everything, including spacing), is a one-liner but shrinks the usable width and changes which breakpoints look right; offer it only as an "Enlarge everything" option in a later step if the owner wants it.
2. **The APK already follows the OS font size.** Decide: keep it (recommended: one less surprise for staff who set Large text system-wide) and offer in-app steps on top, or pin `textZoom=100` and rely on in-app only. Either way the emulator test pass must include OS font size "Largest".
3. **Dark / high-contrast themes need a colour-token pass first.** 1,088 hard-coded hex colours in CSS and a handful of CSS variables. Theming = one mechanical migration (hex → `var(--c-…)` with the current palette as the light theme, status hues kept as separate tokens with their text labels), then each theme is a 40-line `[data-theme=…]` block. Without this pass no theme feature is realistic; with it, dark, high-contrast and an accent choice are each small. Images, SVG icons (currentColor) and print styles (`zz-print.css` forces light) are unaffected.
4. **Formats are easy to centralise.** `money()` and the date helpers are already the only formatters; 18 files import them. A preference-driven formatter (lakh vs. international grouping, `26 Oct 2026` vs `26/10/2026`, 12 h vs 24 h) is a change in two files plus a React re-render trigger.
5. **Per-user preferences across devices need server work.** The prototype's `User` record has no preferences field; the server has no endpoint. First release: per device only (localStorage, same as display mode). Second release: optional `prefs` on the user (SCHEMA bump, new command `USER_PREFS_SET` not audited beyond "changed own preferences"), server table + `PUT /me/preferences` in Wave 5 or later; device-only values stay as overrides.
6. **Language is the one large item.** Every string is inline JSX (no string table). Hindi/Marathi UI would be a string-extraction project across ~26 feature folders plus reports and print documents. Listed as a decision for the owner, not planned here.

## 3. Feature catalogue

Scope: **D** = this device, **U** = my preferences (per user, can sync later), **H** = hotel-wide (admin, audited — goes into Master configuration). Effort: S < ½ day agent work, M = 1–2 days, L = multi-day. Priority: P1 first release, P2 second, P3 later / on request.

### A. Text and readability
| # | Feature | Scope | Effort | Pri | Notes |
|---|---|---|---|---|---|
| A1 | Text size: Small 90 % · Default · Large 115 % · Extra large 130 % | D | M | **P1** | Build transform (§2.1) + `--hcc-text` on `<html>`; live preview; printed documents stay at 100 % |
| A2 | Honour OS font size (APK) on/off | D | S | P1 | Default on; off pins `textZoom=100` via a tiny Capacitor bridge call in `MainActivity` |
| A3 | Bold / heavier labels | D | S | P2 | `--hcc-weight` token on labels, nav, tile titles |
| A4 | Money emphasis: larger figures on KPI cards and folio totals | D | S | P3 | Owner/Manager habit; one token on `.money-stat`, `.f-metric strong` |
| A5 | Enlarge everything (layout zoom 110 / 125 %) | D | S | P3 | CSS `zoom` on `#root`; only after A1 ships and the owner asks |

### B. Appearance and theme
| # | Feature | Scope | Effort | Pri | Notes |
|---|---|---|---|---|---|
| B0 | Colour-token migration (prerequisite, no visible change) | — | M | **P1** | Mechanical hex → variable pass with a contrast test (keep the 0-below-4.5:1 result); Sonnet agent, one CSS file each |
| B1 | Theme: Light (Maroon & Sand) · Dark · Follow system | D/U | M | P2 | Dark palette must keep status colours + text labels; update `theme-color` meta and Capacitor `StatusBar` live |
| B2 | High-contrast theme (AAA text, thicker borders, no tints) | D/U | S after B0 | P2 | Good for the night auditor / older staff; stacks with A1 |
| B3 | Accent colour (maroon, deep green, navy, slate) | D/U | S after B0 | P3 | Brand stays maroon in print and sign-in; accent affects app bar, buttons, focus ring |
| B4 | Reduce motion (on / off / follow system) | D | S | P2 | Today follows the OS only |
| B5 | Density: Comfortable · Compact (desktop rows, card padding, tablet touch tokens) | D | S | P2 | DEC-060 already built on tokens; Compact never below 44 px on touch |
| B6 | Room-board tile style: colour tiles · outline tiles · letter-only | D/U | S | P3 | Colour-blind staff; the `.mr-code` letters already exist |

### C. Layout and navigation
| # | Feature | Scope | Effort | Pri | Notes |
|---|---|---|---|---|---|
| C1 | Start page after sign-in (per user, from permitted pages) | U | S | **P1** | Overrides `landing[role]`; e.g. Reception → Arrivals, Owner → Reports |
| C2 | Phone bottom tabs: choose and order up to 4 from permitted pages | U | M | P2 | Falls back to `roleTabs`; badges follow the page; "Reset to role default" |
| C3 | Pinned / favourite pages on Modules Home and the phone More sheet; recent pages | U | M | P2 | Keeps the 10 categories; adds a "Pinned" row on top |
| C4 | Desktop sidebar: collapsible groups, remember open/closed; compact icon-only sidebar | D | M | P2 | Closes HANDOFF open item 6 |
| C5 | Home dashboard: show/hide and reorder KPI cards and panels | U | M | P3 | Only cards the role may see; money cards never appear for housekeeping/maintenance (DEC-061) |
| C6 | Default filters remembered per page (e.g. Reports range preset, Housekeeping floor, Expenses "mine") | D | M | P3 | Per page key in prefs; "Clear remembered filters" |
| C7 | List page size: 25 · 50 · 100 · 200 | D | S | P2 | One constant in `usePaged` → pref; phone capped at 100 for speed |
| C8 | Keyboard shortcut cheat sheet + rebinding of the search key (Ctrl+K / "/") | D | S | P3 | Desktop only |

### D. Formats and language
| # | Feature | Scope | Effort | Pri | Notes |
|---|---|---|---|---|---|
| D1 | Number grouping: Indian (1,23,456) · International (123,456); show paise always / only when non-zero | U | S | P2 | `money()` already caches formatters; add a prefs-keyed cache |
| D2 | Date format: 26 Oct 2026 · 26/10/2026 · 2026-10-26; week starts Monday/Sunday (calendar) | U | S | P2 | Reports exports keep ISO for Excel |
| D3 | Time: 12 h / 24 h | U | S | P2 | Shift times, audit log, check-in times |
| D4 | Lakh/crore words on big KPIs (₹71.8 lakh vs ₹71,80,000) | U | S | P3 | Already used in History summary; make it a choice |
| D5 | UI language Hindi / Marathi | H + U | L | Owner decision | String extraction across the app; invoices and statutory prints stay English |

### E. Behaviour and notifications
| # | Feature | Scope | Effort | Pri | Notes |
|---|---|---|---|---|---|
| E1 | Toast duration 3 / 6.5 / 10 s / until dismissed; position (bottom/top) | D | S | P2 | Accessibility: "until dismissed" for screen-reader users |
| E2 | Confirmations: ask before check-out / cash movement / day close (on by default; per user may switch off only the non-money ones) | U | S | P2 | Money confirmations stay on (financial rulebook) |
| E3 | Sounds and vibration on new approval / arrival / task (APK) | D | S | P2 | Capacitor Haptics + a short bundled tone; silent by default |
| E4 | Keep screen awake on the room board / housekeeping board (APK) | D | S | P2 | Capacitor KeepAwake; auto-off after 30 min |
| E5 | Auto-refresh interval of boards when on the server (10 / 30 / 60 s / manual) | D | S | P2 | Sync client polling already exists; expose the interval |
| E6 | Default printer / paper size (A4, A5, 80 mm receipt) and "preview before print" | D | M | P3 | Pairs with the wireless-printer decision (OWNER-2) |
| E7 | Export defaults: CSV vs Excel, include header row, file-name pattern | D | S | P3 | `saveCsv` / xlsx helpers |
| E8 | Idle sign-out time on this device (5 / 15 / 30 min / never) within the hotel maximum | D (capped by H) | S | P2 | Hotel maximum is a Master configuration security setting (Tech admin) |
| E9 | PIN sign-in: remember staff on this device, show staff photos/initials | D | S | P2 | Extends `hcc-device-staff` (security review F-L09 keeps names only) |

### F. Accessibility (always available, grouped on one card)
| # | Feature | Scope | Effort | Pri | Notes |
|---|---|---|---|---|---|
| F1 | Always show focus ring; larger focus ring | D | S | P2 | `y-a11y.css` already has the ring rules |
| F2 | Underline all links / buttons-as-text | D | S | P3 | |
| F3 | Colour-blind safe status palette (adds patterns to room tiles) | D | S after B0 | P2 | Status labels already text; add stripes/dots |
| F4 | Screen-reader verbosity: announce toasts and badge changes | D | S | P3 | `aria-live` politeness level |

### G. Hotel-wide defaults (admin, Master configuration › "Appearance defaults")
| # | Feature | Scope | Effort | Pri | Notes |
|---|---|---|---|---|---|
| G1 | Hotel default theme / text size for new devices; "lock theme" for shared PCs | H | S | P3 | Audited like other configuration; personal settings override unless locked |
| G2 | Hotel logo, short name and colour on sign-in, app bar, prints | H | M | P2 | Already partly in property settings; make it drive the UI and invoices |
| G3 | Role default tabs and start pages (what C1/C2 reset to) | H | S | P3 | Replaces the hard-coded `roleTabs` / `landing` tables with configuration |

## 4. Settings UI design

- **Name:** "Appearance & display" (phone sheet and desktop page), reached from: desktop user menu (new) and top bar gear; Hotel configuration › the existing "Device & display" card becomes a link to it; phone user-menu sheet item and More › "Appearance & display". Every role sees it; it never shows hotel data, so no permission row is needed (add it to `PAGE_CATEGORY` so the nav test passes).
- **Sections (cards, in this order):** Text size (A1, A2, with a live sample line) · Appearance (B1, B2, B3, B4, B5) · Start & navigation (C1, C2, C3, C7) · Formats (D1–D4) · Alerts & behaviour (E1–E5, E8, E9) · Accessibility (F1–F4) · **Reset** (this device · my preferences). Each card says its scope in one line: "This device only" or "My preferences (all my devices once sync is on)".
- **Phone:** one page of ListCards → setting sheets (reuse the Master configuration sheet pattern: value, what it affects, reset). 48 px targets, 16 px text; segmented controls for ≤ 4 options, otherwise a radio list.
- **Behaviour:** changes apply immediately (no Save), undo via Reset. Text size and theme also offer a quick toggle in the user menu (three-step "A A A" control).
- **Not audited, not hotel data** (DEC-044 extended): logged nowhere except a local "preferences changed" toast.

## 5. Implementation plan

Follow the project rules: no new npm packages in `03 Prototype`; Capacitor packages only in `04 Android`; tests per stream in `tests/stream-prefs*.mjs`; Sonnet for mechanical/testing streams, Opus for the settings model and merge.

| Phase | Content | Streams (parallel) | Exit check |
|---|---|---|---|
| **P1 Foundation + first release** | `src/app/prefs.ts`: typed, versioned prefs (`hcc-prefs-1`), defaults, `usePrefs()` hook, cross-tab `storage` sync, applies `data-theme`, `data-density`, `--hcc-text` on `<html>`. Build transform for text size (§2.1). Settings page (desktop + phone). A1, A2, C1, B0 token migration (no visible change). | S1 prefs model + page (Opus) · S2 build transform + dist test (Sonnet) · S3 colour tokens, one agent per CSS group, contrast test kept (Sonnet ×3) · S4 Android `textZoom` bridge + emulator run at OS "Largest" (Sonnet) | tsc clean; 1008 + new tests; build size noted; Playwright at 360/390/1366 at each text size: no horizontal overflow, no clipped controls; a11y contrast unchanged |
| **P2 Appearance + layout** | B1 dark, B2 high contrast, B4, B5, C2 tabs, C3 pinned, C4 sidebar, C7 page size, D1–D3 formats, E1, E2, E8, E9, F1, F3 | one stream per letter group | dark-theme contrast audit (same script as MB-00); 9-role smoke with tabs reordered; print documents unchanged in both themes |
| **P3 APK extras** | E3 haptics/sound, E4 keep awake, E5 refresh interval, A5 if asked, G2 logo | `04 Android` + `native.ts` no-op fallbacks | emulator phone + tablet; battery note for keep-awake |
| **P4 Per-user sync** | `User.prefs` (SCHEMA 9), `USER_PREFS_SET`, server `PUT /me/preferences`, merge rule device-override > user > hotel default; G1, G3 | server stream in Wave 5+ | server tests; prefs survive sign-in on a second device |
| Later / decision | D5 language, C5 dashboard editor, E6 printer, C8 shortcuts, A4, B6, D4, E7, F2, F4 | — | — |

Files touched in P1: `03 Prototype/build.mjs`, `serve.mjs` (dev CSS path), `src/app/prefs.ts` (new), `src/app/App.tsx`, `src/app/context.tsx` (expose prefs), `src/app/display.tsx` (fold into the new page), `src/app/navmap.ts` (+ `PAGE_CATEGORY`), `src/app/nav.ts` (start page), `src/app/mobile/sheets.tsx` (user menu), `src/features/admin/HotelSettings.tsx` (link), new `src/features/prefs/` + `src/mobile/prefs/`, `src/styles/*.css` (tokens), `04 Android/.../MainActivity.java` (textZoom), `tests/stream-prefs.mjs`, `tests/stream-css-tokens.mjs`.

## 6. Decisions for the owner

1. Honour the Android system font size in the APK (recommended) or in-app sizes only?
2. Dark theme: wanted for the first release (adds the token pass to P1's critical path) or second?
3. May staff reorder their own phone tabs and start page (C1/C2), or should these stay uniform per role (then only G3 applies)?
4. Per-user sync of preferences across devices (P4) — worth server work in Wave 5, or stay per device?
5. Hindi/Marathi UI (D5): decide yes/no before Wave 5 so string extraction can be planned once, not retrofitted.

---

# Part 2 — Masters: configuring the hotel itself

## 7. What exists today

Rule already approved (Master configuration design §2, from the SCC lesson): **masters live once**; modules pick from them and link to the master instead of their own add forms. Status of each master, prototype and server (26 Oct 2026):

| Master | Prototype (mock) | Server (`08 Production`) | Gap |
|---|---|---|---|
| Hotel profile (name, address, state, GSTIN, times) | Master configuration › Property | `SetupProperty` first-time setup + `cfg.setting` | none |
| **Floors / wings** | `Room.floor` is a bare number; no list, no add | same (column only) | **No floor master** |
| **Room types** | derived from the rooms in the fixture (`adminRoomTypes` = distinct `Room.type`); no add/rename | `inv.room_type` via CSV import (W4-13) + `UpdateRoomType` | **No screen to add/rename/retire a type; no max occupancy, bed type, amenities** |
| **Rooms** | fixture only; `BLOCK`/`UNBLOCK` exist; no add, renumber, change type/floor, retire | CSV import only | **No "Add room" anywhere** |
| Tariffs (dated BAR), seasons, rate plans, restrictions, DOW rules | Tariff & yield (`YLD_*_SAVE`) + Master configuration › Rooms | `yld.*`, `inv.room_type_tariff`, import | done |
| GST master (dated) | Master configuration › Money (DEC-069) | `tax.*`, `SetGstSetting` | done |
| **Payment methods** | hard-coded in three different lists (folio `Cash/UPI/Card/Bank transfer`, expenses `Cash/UPI/Bank transfer/Vendor credit`, POS `Cash/UPI/Card`) | strings on receipts | **No master; lists disagree** |
| Document numbering | `invoicePrefix` setting; one series | invoices module series | partial (one series, no per-document prefixes/reset rule) |
| **Expense categories** | constant `EXPENSE_CATEGORIES` (5 values) | strings | **No master** |
| Vendors | maintenance vendors (`MNT_VENDOR_SAVE`); expense vendor is free text | `mnt.vendor`; expense vendor text | **Two vendor concepts; expenses not linked** |
| Companies / corporate accounts, credit limits | `GRP_COMPANY_SAVE`, `GRP_LIMIT_SET` | `grp.company_account`, `SaveCompany` | done |
| Guest ID types | hard-coded list with validators (`guestIdNumberError`) | same | fixed list (acceptable; add on/off per type) |
| Booking sources / market segments | free text or fixed values on bookings | column | **No master** |
| Users, roles, permissions, PIN | Access control (`USER_CREATE/UPDATE`, `GRANT`) | Access module | done |
| Staff, departments, statutory rates, leave policy, roster | Staff & payroll (`HR_STAFF_SAVE`, `GSV_DEPTS`, `HR_*_SET`) | `hr.staff`, `hr.department`, `SaveDepartment` | done; **no designations, shift patterns, leave types, holiday calendar** |
| Housekeeping settings | inspection on/off, dirty-room alert, attendant load | settings | **no task types / standard minutes / zones** |
| Checklist (handover) templates | `HOV_TEMPLATE_SAVE/TOGGLE` | `ops.hov_template*` | done |
| Linen & stock items, par levels, settings | `INV_ITEM_SET`, `StockItemCreate` | `stk.item`, `stk.item_par` | done |
| Maintenance assets, asset kinds, AMC, PM plans, spares | `MNT_ASSET_SAVE/KINDS/AMC/PLAN` | `mnt.*` | done |
| Guest-service request categories, departments, SLAs | `GSV_ITEM_CATEGORIES`, `GSV_DEPTS`, SLA settings | `SaveGuestServiceSettings` | done; **no complaint categories** |
| **POS outlets, tables/counters** | fixture; no add | `pos.outlet`, `pos.outlet_table`, `pos.counter` via import only | **No screen** |
| **POS menu** | `POS_MENU_TOGGLE` (availability only); no add/edit item, category, price, GST | import only | **No menu editor** |
| OTA channels | `CHN_CHANNEL_SAVE/STATUS` | `ota.*` | done |
| Message templates, trigger rules, consent | `MSG_TEMPLATE`, `MSG_CHANNELS` | `msg.template`, `msg.trigger_rule` | done |
| Compliance obligations, CA contact | `CMP_OBLIGATION_SAVE` | `SaveComplianceObligation` | done |
| Ledgers / chart of accounts, mapping | `ACC_MAP_SET` + ledger import | `acc.mapping`, import | done |
| **Extras / service catalogue** (extra bed, laundry, pickup…) with GST class | folio `CHARGE` takes a free label and amount | same | **No catalogue → inconsistent names and tax** |
| **Meal plans / packages** (EP, CP, MAP, AP) | none | none | **Missing** |
| **Discount types / reasons** | free text reason + limit | same | no master (optional) |
| Modules on/off | Master configuration › Modules | `/configuration/modules` | done |
| CSV import/export of masters | — | W4-13: room types, rooms, tariffs, seasons, staff, ledgers, opening balances, till cash, guests, POS outlets, POS menu | prototype has configuration JSON export only |

Bold rows are the gaps the owner noticed ("ability to add floors, rooms and all").

## 8. Masters catalogue

Columns: scope = who changes it (per DEC-058: **O** Owner for money/tax/rates; **T** Tech admin for non-money software lists; **M** Manager proposed for day-to-day operational lists — owner decision Q1 below); effort S/M/L; priority P1 (first batch), P2, P3. Every master follows the common rules in §9.

### A. Property structure
| # | Master | Fields | Rules | Scope | Effort | Pri |
|---|---|---|---|---|---|---|
| M1 | **Floors / wings** | code, name ("Ground", "1st", "Annexe"), sort order, active | Room board and housekeeping group by floor; a floor with rooms cannot be deleted, only retired once empty | T/M | S | **P1** |
| M2 | **Room types** | code, name, max adults/children, extra-bed allowed, bed type, size, amenities (from M4), description + photos (booking site), rack rate (links to dated tariff), sort order, active | Rename cascades to screens; a type in use cannot be deleted; retired types keep history; changing max occupancy warns about future bookings | O (rack rate) / T | M | **P1** |
| M3 | **Rooms** | number, floor (M1), type (M2), features (M4) overrides, smoking, connecting room, phone extension, notes, status (active / out of order with dates / retired) | Add many at once ("101–110 on floor 1, Deluxe"); renumber only when no future booking; change type only from a date with no booking; out-of-order feeds availability like a block; retire keeps history; availability, room board, calendar, housekeeping, yield counts all read the master | T/M | M | **P1** |
| M4 | Room features / amenities | code, name, icon, shown on booking site | pick list for M2/M3 and web booking filters | T | S | P2 |
| M5 | Occupancy and extra charges | extra adult, extra child, extra bed, child age bands; per room type; dated amounts with GST class | money → dated; posts as folio lines from the extras catalogue (M7) | O | S | P2 |

### B. Pricing and products
| # | Master | Fields | Rules | Scope | Effort | Pri |
|---|---|---|---|---|---|---|
| M6 | **Meal plans / packages** (EP, CP, MAP, AP, custom) | code, name, included meals, per-person per-night add-on (dated), GST treatment (bundled with room vs separate restaurant line — CA to confirm), room types allowed | Booking picks a plan; folio shows room + plan lines; reports by plan; booking site offers plans | O | M | **P1** |
| M7 | **Extras / service catalogue** | code, name, category (transport, laundry, F&B, misc), default price (dated), GST class from the GST master (services 18 %, restaurant 5 %…), unit, department that fulfils (guest services) | Replaces free-text `CHARGE`; "Other (describe)" stays for one-offs with Manager approval; names and tax become consistent on invoices | O | M | **P1** |
| M8 | Discount types / reasons | code, name, max %, needs approval above, shown to roles | feeds `DISCOUNT` and approvals; reporting by reason | O | S | P2 |
| M9 | Booking sources and market segments | source (Walk-in, Phone, Website, OTA name, Agent, Corporate), segment, commission % (agents), active | Reports by source; OTA channels and companies register their own entries automatically | M | S | P2 |
| M10 | Travel agents | name, contact, GSTIN, commission, credit terms, linked company account | reuses company accounts (`grp.company_account`) with an "agent" flag; commission accrual is a later server rule | O | S | P3 |

### C. Front desk and documents
| # | Master | Fields | Rules | Scope | Effort | Pri |
|---|---|---|---|---|---|---|
| M11 | Guest ID types | on/off per type, which need a photo capture, foreigner-only types (Passport + visa) | validators stay in code; the list is configurable, not free text | T | S | P2 |
| M12 | **Document numbering series** | per document (tax invoice, bill of supply, receipt, credit note, KOT, registration card): prefix, start, padding, reset (yearly on 1 Apr / never), active from | GST rule: invoice series unique and sequential per FY, no gaps — CA to confirm; changing a live series needs Owner + takes effect from the next FY or a date with no documents | O | M | P2 |
| M13 | Print templates and branding | invoice header/footer lines, terms, registration-card text, logo, signature block, paper size per document | ties to G2 in Part 1; preview before save | T | M | P2 |
| M14 | Cancellation / no-show / deposit policies as named policies | name, free-cancel hours, charge %, deposit %, applies to sources/plans | today single global settings; named policies let OTA/corporate differ | O | M | P3 |

### D. Money
| # | Master | Fields | Rules | Scope | Effort | Pri |
|---|---|---|---|---|---|---|
| M15 | **Payment methods** | code, name, kind (cash / UPI / card / bank / wallet / credit), settles to (till or bank account M16), needs reference no., usable at (front desk, POS, expenses), surcharge, active | One list for folio, POS, expenses and cash book; cash book and bank reconciliation read "settles to" | O | S | **P1** |
| M16 | Bank accounts and tills | name, bank, masked account, IFSC, opening balance (setup only), default for UPI/card | pairs with the till model (DEC till decision) and payroll bank file | O | S | P2 |
| M17 | **Expense categories** | code, name, ledger (acc mapping), needs bill, approval limit override, active | replaces the 5-value constant; accounting mapping reads it | O | S | **P1** |
| M18 | **Vendors (one list)** | name, type (supplier, contractor, AMC, utility, laundry), GSTIN, PAN (TDS), contact, payment terms, bank, active | merges maintenance vendors and expense vendor text; TDS applicability flag for compliance; "used in" counts | O | M | **P1** |

### E. Operations and people
| # | Master | Fields | Rules | Scope | Effort | Pri |
|---|---|---|---|---|---|---|
| M19 | Designations | name, department, grade, default pay rule | staff master picks from it | M | S | P2 |
| M20 | Shift patterns / duty times | name, start, end, break, night flag, weekly off rule | roster and attendance read it; overtime rule references | M | S | P2 |
| M21 | Leave types | name, paid/unpaid, yearly quota, carry-forward, encashment | leave policy becomes per type | O | S | P2 |
| M22 | Holiday calendar | date, name, paid holiday / restricted, FY | payroll and compliance due-date shifting | M | S | P2 |
| M23 | Housekeeping task types and standard minutes | task type (departure clean, stayover, deep clean, inspection), minutes, needs inspection, checklist | attendant load and board timings read it | M | S | P2 |
| M24 | Housekeeping zones / sections | zone = set of floors or rooms, default attendant | assignment and "my tasks" by zone | M | S | P3 |
| M25 | Complaint and lost-and-found categories | code, name, department, SLA | guest services and complaints report by category | M | S | P2 |

### F. Restaurant and POS
| # | Master | Fields | Rules | Scope | Effort | Pri |
|---|---|---|---|---|---|---|
| M26 | **Outlets** | code, name, type (restaurant, room service, bar, banquet), timings, GST profile (5 % / 18 % specified premises), KOT printer, active | one outlet cannot be removed while open checks exist | O | S | **P1** |
| M27 | Tables and counters | per outlet: table no., seats, section; counters for takeaway | floor plan on phone optional | M | S | P2 |
| M28 | **Menu categories and items** | category, item, short code, price (dated), GST class, veg/non-veg, available at outlets, kitchen station, active, "sold out today" | replaces toggle-only menu; price change dated; KOT groups by station | O (price) / M (availability) | M | **P1** |
| M29 | Modifiers / portions | half/full, add-ons with price | optional | O | S | P3 |

### G. System
| # | Master | Fields | Rules | Scope | Effort | Pri |
|---|---|---|---|---|---|---|
| M30 | CSV import/export for every master | templates with headers = fields above; validate → preview → commit (W4-13 pattern) | the import service already covers 11 masters; new masters register a spec each | O/T | M | P2 |
| M31 | Master change log and "used in" | per row: history (who, when, old → new), where used (counts with links) | reuse configuration history pattern | — | S | P1 (part of framework) |

## 9. Common framework (built once, used by every master)

1. **Data shape.** Every master row: `id`, `code` (short, unique, immutable once used), `name`, `sortOrder`, `active`, optional `retiredOn`, optional dated money fields (`effectiveFrom`, as tariffs do). Never hard delete: **retire** when in use, delete only when never used. "In use" is a pure selector per master (bookings, folios, checks, expenses…).
2. **Commands.** One pattern: `<PFX>_MASTER_SAVE {master, row}` (create or edit), `<PFX>_MASTER_RETIRE`, `<PFX>_MASTER_REORDER`; typed `HccError` codes (`MASTER_CODE_TAKEN`, `MASTER_IN_USE`, `MASTER_RETIRED`, `MASTER_LOCKED_BY_BOOKINGS`); idempotency keys; audit line "Master <name>: <field> old → new". Money fields follow the dated-settings rule (effective from, approval per DEC-058). Server: the same as one `SaveMaster`-style command per module (its own table, its own module folder, migrations in a reserved block), outbox + audit in the same transaction as every other command.
3. **UI.** One shared `MasterList` component (desktop: list beside detail with search, active/retired filter, drag or arrow reorder, "used in" counts; phone: list → sheet with sticky footer, 48 px targets) and one `MasterForm` built from a field spec (text, number, money, percent, select from another master, toggle, dated money). Placed inside **Master configuration** under its group (Rooms and rate plans → Floors, Room types, Rooms, Amenities; Money and tax → Payment methods, Bank accounts, Expense categories, Vendors; Restaurant and POS → Outlets, Tables, Menu; People → Designations, Shifts, Leave types, Holidays; Housekeeping → Task types, Zones; Front-desk policies → Sources, ID types, Numbering, Policies). Module pages get a **"Manage list…"** link (Owner/Tech admin/Manager by scope) instead of their own add forms; pick lists everywhere show active rows only and keep a retired value visible on old records.
4. **Permissions.** New permission `masters` (operational lists) alongside existing `settings`; money masters stay under the Owner rule (DEC-058). Housekeeping/maintenance roles never see money fields (DEC-061): the field spec marks money fields and the form hides them.
5. **Bulk.** Every master supports CSV import/export through the W4-13 import service (new `ImportMaster` enum values + column specs) and a "paste rows" quick-add on desktop; rooms also get a range generator.
6. **Tests.** Per master: save/edit/retire/in-use/duplicate-code/reorder; pick lists hide retired rows; history records; phone and desktop registration; `PAGE_CATEGORY` and navmap entries; server: migration, command, endpoint, RBAC seed, import template header test.
7. **Fixtures.** FY26 history must keep generating: new masters are seeded from the current fixture values (floors 1–3, the existing room types, payment methods, 5 expense categories, outlets, menu) so no figure moves; a test asserts the history hash is unchanged.

## 10. Multi-agent plan (masters)

Pattern as in Waves 3–4: isolated worktrees, own files only, shared files by appended lines; Sonnet for list-type masters and tests, Opus for money/GST/numbering/availability and for merge. Migrations reserved **2800–2899** (10 per agent). Target: framework in one day, batch 1 and 2 in parallel over the next two days, merge + APK on day four.

| # | Agent | Model | Builds (prototype + sync layer + server) | Owns | After |
|---|---|---|---|---|---|
| **MA-0** | **Masters framework** | Opus | §9 items 1–4, 6: `MasterList`/`MasterForm`, command pattern + error codes, in-use/retire rules, `masters` permission, Master configuration placement, test helpers; **reference master = Floors (M1)** end to end incl. server `cfg.master_floor`, migrations 2800–2809 | `src/features/masters/*`, `src/domain/streams/masters.ts`, `src/services/mock/handlers/masters.ts`, `src/clients/sync/masters.ts`, server `Hcc.Domain/Masters`, `Hcc.Api/Endpoints/MastersEndpoints.cs`, `tests/stream-masters.mjs` | — (also runs beside Part 1 P1 streams; no file overlap) |
| MA-1 | **Rooms and room types** (M2, M3, M4, M5) | Opus | type and room editors, range add, renumber/retype rules, out-of-order → availability, cascade to room board/calendar/housekeeping/yield counts; server `inv.room_type` edit + `inv.room` add/retire; migrations 2810–2819 | `src/features/rooms/masters*`, `src/domain/streams/rooms-master.ts`, server Inventory/Stays folders (own files), `tests/stream-rooms-master.mjs` | MA-0 |
| MA-2 | **Meal plans and extras catalogue** (M6, M7, M8) | Opus | plan on booking + folio lines, extras replacing free-text `CHARGE` (keep "Other" with approval), GST class per item from the GST master, discount reasons; server Billing/Pricing; 2820–2829 | `src/features/folio/catalogue*`, `src/domain/streams/catalogue.ts`, `tests/stream-catalogue.mjs` | MA-0 |
| MA-3 | **Payment methods, bank accounts, expense categories, vendors** (M15–M18) | Opus | one method list used by folio, POS, expenses, cash book; "settles to"; vendor merge (maintenance vendors become rows of the one list, TDS flag); expense categories with ledger mapping; 2830–2839 | `src/features/money/masters*`, `src/domain/streams/money-master.ts`, `tests/stream-money-master.mjs` | MA-0 |
| MA-4 | **POS masters** (M26–M29) | Opus (GST) | outlets, tables/counters, menu categories/items with dated price + GST class + stations + sold-out; KOT grouping; 2840–2849 | `src/features/pos/masters*`, server Pos folder own files, `tests/stream-pos-master.mjs` | MA-0 |
| MA-5 | **Front-desk lists** (M9, M11, M12, M14) | Opus (numbering) | booking sources/segments on bookings + reports, ID types on/off, numbering series per document with FY reset (CA note), named policies; 2850–2859 | `src/features/reservations/masters*`, `src/domain/streams/frontdesk-master.ts`, `tests/stream-frontdesk-master.mjs` | MA-0 |
| MA-6 | **People lists** (M19–M22) | Sonnet | designations, shift patterns, leave types, holiday calendar wired into staff, roster, leave policy, payroll; 2860–2869 | `src/features/staff/masters*`, `tests/stream-people-master.mjs` | MA-0 |
| MA-7 | **Housekeeping and services lists** (M23–M25) | Sonnet | task types + standard minutes into attendant load/board, zones into assignment, complaint/L&F categories; 2870–2879 | `src/features/housekeeping/masters*`, `src/features/services/masters*`, `tests/stream-ops-master.mjs` | MA-0 |
| MA-8 | **Import/export for all masters** (M30) | Sonnet | new `ImportMaster` specs + CSV templates + prototype export/import of masters (JSON + CSV), round-trip tests; 2880–2889 | server `Hcc.Domain/Import` (append specs), `deployment/import-templates/*`, `src/features/admin/config/import*`, `tests/stream-masters-import.mjs` | MA-1…MA-7 (specs need final fields) |
| MA-9 | **Fixture and history alignment** | Sonnet | seed every new master from fixture values, FY26 history hash unchanged, BASE untouched, scenario catalogue updated | `src/fixtures/ext/masters.ts`, `tests/stream-masters-fixture.mjs` | MA-1…MA-7 |
| MA-10 | **Phone screens and smoke** | Sonnet | phone list/sheet for every master, 9-role click-through, Playwright 360/390/1366, no money fields for HK/maintenance | `src/mobile/masters/*`, evidence report | MA-1…MA-7 |
| MA-11 | **Review** | Opus | money/GST/numbering/availability review of MA-1…MA-5, RBAC per DEC-058/061, migration order, API contract notes | review report + fixes in own commits | MA-1…MA-7 |
| Lead | merge → `claude/masters-integration` → full prototype + server tests → production web build smoke → main → APK v13 → emulator phone + tablet → HANDOFF | Opus | | | |

**Shared-file rules for this wave:** `types.ts` — one optional slice per agent (`s.mastersX`, `s.roomsMasterX`, …) and no edits to `Room` beyond MA-1; `registry.ts`, `navmap.ts`, `services/mock/index.ts`, `handlers/index.ts` — append your lines only; `config.ts` — one `registerSettingsGroup` line per agent; server `Hcc.slnx`/composition — one line each; migrations only in your reserved block. SCHEMA bump once by the lead at merge (8 → 9).

**Exit checks:** tsc clean; prototype 1008 + new tests; server 962 + new tests (serial); `npm run build` and `build:production`; FY26 history hash unchanged (MA-9); every hard-coded list named in §7 removed (a test greps for the old constants); a room added through the UI appears on the board, calendar, availability, housekeeping and yield counts on desktop and phone; invoices from the extras catalogue show the right GST per line; the old 5 expense categories and 3 payment lists still resolve on historical records.

## 11. Owner decisions for Part 2 — answered 26 Oct 2026 (DEC-MA-1…7 in the Decision Register)

1. **Who edits operational lists:** **Owner + Tech admin only** (DEC-058 unchanged; the Manager does not get the `masters` permission by default — the Owner may grant it later in Access control). Scope column "M" in §8 therefore reads as T. Money fields stay Owner.
2. **Floors as a master:** **yes** — named floors/wings, room board grouped by them (M1, in MA-0).
3. **Meal plans:** **now**, in MA-2 (EP/CP/MAP/AP on bookings, booking site, OTA mapping); GST treatment of bundled breakfast marked "Illustrative — CA to confirm".
4. **Extras catalogue:** **free text stays allowed for Reception** — the catalogue prefills name, price and GST class, but any label and amount may still be posted (no approval). Reports show "catalogue vs free text" so the Owner can tighten this later.
5. **Numbering series:** **one tax-invoice series** for the hotel; the series master (M12) exists so outlets/documents can get their own later.
6. **Vendors:** **keep separate** — maintenance vendors stay as they are; expenses get their own vendor master (M18 becomes "expense vendors": name, type, GSTIN, PAN/TDS flag, contact, terms). No migration of `mnt.vendor`.
7. **Batch order:** **all seven at once** — MA-1…MA-7 in parallel right after MA-0; then MA-8…MA-11.
