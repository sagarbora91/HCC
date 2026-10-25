# T-01 / T-02 Tablet decisions - brief for the owner (25 Oct 2026)

Device: Samsung Galaxy Tab S7 (SM-T875), portrait ~673-800 dp wide, landscape ~1078x601. Tablet uses the desktop layout (DEC-044). Mock-ups: `T-01_Header_Options.html`, `T-02_Touch_Size_Options.html` (same folder, PNGs alongside). No product change made.

Status: a compact coarse-pointer header and 48 px sampled controls were already added on the integration line (`styles/z-tablet.css`); local emulation shows header 85 px portrait / 65 px landscape, drawer list 754 / 349 px. Not yet re-measured on the physical tablet.

## T-01 Header takes too much of the portrait screen (was 248 of 1006 px, ~25 %)

| Opt | What it looks like | Chrome height | Effort | Risk | Screens changed |
|---|---|---|---|---|---|
| A. Keep compact header | 2 rows: hamburger, breadcrumb, Search; date on row 2 | ~85-97 px (8 %) | S (physical re-check + APK) | Low | none |
| B. Single row + overflow menu | hamburger, breadcrumb, date, search icon, user menu (display, demo, sign out) | ~60 px (5 %) | M | Low-med: sign-out/display one tap deeper; demo controls hidden | App shell/top bar only (all screens inherit) |
| C. Auto-hide on scroll | header hides scrolling down, returns scrolling up; floating menu button | 0 px while reading | M | Med: scroll listeners per page container, date/Open status can be missed, hard to test | all scrolling pages |
| D. Phone shell in portrait | phone app bar + bottom tabs when portrait | 56 + 64 px | M-L | High: contradicts DEC-044, stretched cards at 800 px, tab set differs from desktop modules | whole app in portrait |

Pros/cons in one line: A free but unproven on device; B best fixed saving with familiar pattern; C max content but surprising behaviour; D reverses an approved decision.

Recommendation: **A now, B only if the physical re-measure still feels cramped.** The 25 % problem is already down to ~8 %; B gains a further ~3 % at the cost of burying sign-out.

Owner question: Is the compact header (about 8 % of portrait height) good enough once you see it on your Tab S7, or do you want the single-row header with a user menu?

## T-02 Controls are 32-38 dp, below 48 dp

| Opt | What it looks like | Rows per screen | Effort | Risk | Screens changed |
|---|---|---|---|---|---|
| A. 48 px everywhere on coarse pointers | one media query: buttons, fields, selects, rows, nav all 48 | about -25 % | M (CSS sweep, check 26 modules) | Low; tables/toolbars may wrap more, needs visual pass | all desktop-layout screens, tablet only |
| B. 44 dense + 48 primary | table rows, filters, chips 44; primary buttons, hamburger, drawer, dialogs 48 | about -10 % | M | Low-med: two sizes to keep consistent | all, tablet only |
| C. Density toggle in Display mode | Comfortable 48 / Standard 44 / Compact 32 stored per device | per choice | M-L (A or B plus setting, tests, persistence) | Med: more states to test | Display dialog + all |

Pros/cons: A simplest and matches the phone rule (48), but long tables get shorter; B keeps density and meets 44 (WCAG 2.2 AAA) but not strict Android 48 for dense rows; C gives the owner control but adds a setting and test matrix.

Recommendation: **B as the default, delivered as CSS variables so C can be added later at low cost.** Only touch devices (`pointer: coarse`) change; Windows desktop keeps 32-38 px.

Owner question: Do you want dense tables at 44 px (more rows, B) or a strict 48 px everywhere (A), accepting shorter lists on the tablet?

## Notes
- Sampled controls on the integration line already measure at least 48 px on Reservations and header; the full 26-module sweep is the real T-02 effort.
- Evidence: `05 Delivery Evidence\Acceptance\defects\T-01*`, `T-02*`, `D6_TabS7.md`, `Print_Tablet_Back_Verification_2026-10-25.md`.
