# Hotel Control Centre — Mobile Version Plan v1 (Android phone)

Version 1.0 · 24 Oct 2026 · Status: **Proposed — owner approval needed** · Related: DEC-06, DEC-08, Prototype Spec §5

## 1. Goal and approach
HCC runs on Windows desktop **and** Android phones from **one codebase and one HTML file**. A phone is not a "squeezed desktop": it gets its own **mobile shell** (app bar, bottom tabs, full-screen sheets) and **role-focused screens**, while using the same mock service, the same state and the same commands. Nothing in the domain or business rules changes.

**Display mode setting (the toggle):** `Auto` (default — mobile below 768 px width) · `Desktop` · `Mobile`.
- Stored **per device** (browser localStorage `hcc-display-mode`), not in hotel data — each phone/PC chooses its own.
- Reachable from: (1) a **Display** item in the user menu / app bar, (2) **Hotel configuration → Device & display** (shows the current device's choice and explains it is per device), (3) the demo toolbar (quick switch for walkthroughs).
- Changing the mode never reloads or loses data; current page and open record are kept.
- Desktop browser + "Mobile" = phone preview for demos (centred 412 px frame on wide screens).

## 2. Mobile shell (SCR-M00)
| Element | Behaviour |
|---|---|
| Top app bar (56 px) | Back arrow (or menu), page title, business-date chip (Open/Closed), user avatar → menu (switch user in demo, display mode, sign-out placeholder) |
| Bottom tab bar (56–64 px, max 5 tabs) | Role-specific (§3). Badges for counts (tasks, approvals). Icon **and** text label |
| "More" tab | Full list of remaining permitted pages, grouped as on desktop |
| Sheets | All dialogs become full-screen sheets with sticky header (title, close) and sticky footer actions; Android back button / swipe-back closes the sheet first |
| Floating action button | One primary action per page (e.g. New booking, Add expense) |
| Demo strip | Collapsed into one "DEMO" pill in the app bar → opens demo sheet (scenario, user, fault, reset, walkthrough) |
| Toasts | Above the tab bar; never cover the primary button |
| Safe areas | `env(safe-area-inset-*)`, `viewport-fit=cover`, no page-level horizontal scroll |

## 3. Bottom tabs per role
| Role | Tabs |
|---|---|
| Owner | Home · Rooms · Approvals · Reports · More |
| Manager | Home · Rooms · Approvals · Closing · More |
| Reception | Rooms · Arrivals · Bookings · Folios · More |
| Cashier | Cashier · Folios · Expenses · More |
| Supervisor | Board · Inspect · Rooms · More |
| Housekeeper | My tasks · (Report repair) · More |
| Maintenance | My repairs · Rooms · More |
| Accountant | Reports · Expenses · Approvals · More |
| Tech admin | Users · Audit · More |
Tabs only show pages the user may open (same `can()` rules as desktop).

## 4. Screen-by-screen mobile design (SCR-Mxx)
| ID | Screen | Mobile layout | Key interactions |
|---|---|---|---|
| M01 | Home / Overview | KPI cards 2-up (occupancy, arrivals, ready rooms, receipts), "Needs attention" list, arrivals/departures segmented control | Tap card → filtered list |
| M02 | Room board | Floor chips (1/2/3) + status filter chips; rooms as 2-column tiles (number, status text+icon, guest initials) | Tap tile → room sheet (status, guest, next arrival, actions: book, check in, block, report repair) |
| M03 | Calendar | Default **agenda view**: one room-type group at a time, days as horizontal swipeable strip (7 days), bookings as cards; optional compact grid scrolls inside its container | Tap empty day → new booking prefilled; long-press not required (button alternative) |
| M04 | Reservations | Search bar + status chips; list cards (guest, room, dates, status, balance) | Swipe not required; tap → booking sheet |
| M05 | New booking (wizard) | 4 steps, one per screen: Dates & party → Pick room (available list with reasons for excluded) → Guest details → Review (Hold / Confirm) | Native date pickers; `inputmode="tel"` for phone; progress dots |
| M06 | Arrivals / check-in | Today's arrivals list + Walk-in FAB; check-in stepper one step per screen (6 steps) | Sticky "Next" button; partial-failure recovery card |
| M07 | Guest profile | Sections as collapsible cards: Contact · ID (masked) · Company · Party · History | Duplicate banner at top; print card hidden on phone (share/print later) |
| M08 | In-house stay | Header card (room, dates, balance) + segments timeline + actions row | Move/extend sheet shows conflicts and alternatives list |
| M09 | Folio | Balance hero (Due / Credit / Deposit held) + tabs Charges · Receipts · Requests; amounts right-aligned, tabular | Sticky footer: Record payment · Add charge · More (discount, refund, correction) |
| M10 | Payment | Amount keypad-friendly (`inputmode="decimal"`), tender chips (Cash/UPI/Card/Bank), "+ Split" adds rows, confirmation screen with "Not verified" note | Big Confirm button; lost-response recovery message |
| M11 | Checkout | Checklist card (nights posted, balance, deposit, pending requests) with green ticks; Confirm only enabled when clear | Invoice preview sheet |
| M12 | Housekeeping — My tasks (housekeeper) | Own tasks first, one large card each: room, kind, priority; buttons 56 px: Start → checklist (large checkboxes) → Complete | Report repair button on each card; no financial data anywhere |
| M13 | Housekeeping — Board / Inspect (supervisor) | Segmented control To do / Cleaning / Inspect / Done; assign via picker sheet; Inspect pass / Reject (reason sheet) | Generate stayovers button |
| M14 | Repairs (maintenance) | My repair tasks list → Start / Complete; room block info | |
| M15 | Cashier shift | Expected cash hero, movements list, count screen with denomination steppers (+/−), difference, Submit | Difference → reason sheet → approval request |
| M16 | Expenses | Status chips, list cards; Add expense sheet (vendor, category, amount, funding, receipt name) | Duplicate warning inline |
| M17 | Approvals inbox | Cards per request: kind, amount, requester, reason, expiry; Approve / Reject (reason) buttons; disabled with explanation for own requests | Badge count on tab |
| M18 | Day closing | Vertical stepper (Preflight → Nights → Cash → Close → Summary → Next day); each blocker is a tappable row | Resume after interruption shown clearly |
| M19 | Reports | Tab chips (Operations, Revenue & tax, Receipts, Expenses & cash, Occupancy); KPI cards + drill-down list; date range sheet | CSV export kept; print hidden on phone |
| M20 | Access control (phone = view-first) | Users list + enable/disable; effective-access lookup; matrix editing **desktop only** (shown as "Open on desktop to edit") | Prevents mistakes on a small screen |
| M21 | Hotel configuration | Category list → setting detail with impact preview; **Device & display** section with the mode toggle | Tariff/tax schedules editable |
| M22 | Audit log | Filter sheet + list | |
| M23 | Backup (simulated), Roadmap | Simple read-only cards | |
| M24 | Demo sheet | Scenario, preview-as user, next-action fault, reset, walkthrough steps with "Go there" | |

## 5. Android/phone requirements
- Touch targets ≥ 48×48 px; spacing ≥ 8 px; body text ≥ 16 px, labels ≥ 14 px; contrast ≥ WCAG AA (4.5:1) — builds on the contrast fix of 24 Oct.
- Correct keyboards: `inputmode="decimal"` amounts, `tel` phone, `numeric` counts; `enterkeyhint="next/done"`; inputs scroll into view above the keyboard.
- Android back button: closes sheet → goes back one step in wizards → previous page (History API), never exits with unsaved form without a confirm.
- No hover-only features; no drag-only features; no tiny tables — tables become cards below 768 px.
- Works in Chrome for Android 2 latest versions; portrait first, landscape usable.
- PWA groundwork (prototype): web app manifest (name, icons, theme colour, standalone display) so it can be "Added to Home screen". Offline/service worker **not** in prototype (DEC-08/NFR-OFF-001: production decision; LAN HTTP is not installable-as-PWA without HTTPS — documented).
- Performance: first screen usable < 2 s on a mid-range Android over hotel Wi-Fi (measured, not assumed).
- Demo data is per device (browser storage); two phones do not share state — stated on the demo sheet.

## 6. Technical design (prototype)
1. `useDisplayMode()` hook: reads localStorage `hcc-display-mode` (Auto/Desktop/Mobile) + `matchMedia('(max-width: 767px)')`; provides `isMobile`. Wrapped in try/catch (storage may be blocked).
2. `AppShell` chooses `DesktopShell` (current) or `MobileShell` (new) — both use the same `HccContext`, `run()`, registry and modals.
3. Registry extension: `FeatureRegistration.mobile?: {tabs?, pages?, sheets?}` so each feature supplies its own mobile page or reuses the desktop component with `compact` prop. Fallback: desktop component inside mobile shell (must still pass the 390 px no-overflow check).
4. `ModalHost` renders as full-screen sheet when `isMobile`; same modal bodies.
5. Shared mobile components: `AppBar`, `TabBar`, `Sheet`, `ListCard`, `Stepper`, `KpiCard`, `ChipFilter`, `StickyFooter`, `NumberStepper`.
6. CSS: `src/styles/mobile.css` with tokens (spacing, type scale, touch sizes); desktop CSS untouched.
7. Display toggle command is **not** a mock-service command (device preference, not hotel data); audit not required. Hotel configuration shows it under "Device & display (this device only)".
8. Manifest + icons inlined (data URIs) to keep the single-file build; `npm start -- --lan` already serves phones on the LAN.

## 7. Backlog (proposed)
| ID | Item | Size | Proves |
|---|---|---|---|
| MB-01 | Display-mode hook + toggle in user menu, config page and demo sheet | S | AC-M01 |
| MB-02 | Mobile shell: app bar, bottom tabs per role, More, safe areas, Android back handling | M | AC-M02, AC-M03 |
| MB-03 | Sheet version of all dialogs; sticky footers; keyboard-safe forms | M | AC-M04 |
| MB-04 | Shared mobile components + mobile.css tokens | M | — |
| MB-05 | Front desk mobile: M01–M08 (home, rooms, calendar agenda, reservations, booking wizard, check-in stepper, guest, in-house) | L | AC-M05 |
| MB-06 | Money mobile: M09–M11, M15–M17 (folio, payment, checkout, cashier, expenses, approvals) | L | AC-M06 |
| MB-07 | Housekeeping/maintenance mobile: M12–M14 | M | AC-M07 |
| MB-08 | Closing, reports, admin, config, audit, backup, roadmap: M18–M23 | M | AC-M08 |
| MB-09 | Demo sheet + mobile walkthrough (S0 steps on phone) | S | AC-M09 |
| MB-10 | Manifest/icons, Add-to-Home-screen check, LAN instructions | S | AC-M10 |
| MB-11 | Automated checks: Playwright at 360×800, 390×844, 412×915 (emulated) for every page × role; physical Android test on the owner's phone | M | AC-M11 |
Provisional total ≈ 10–14 focused build days; can run as 3 parallel streams after MB-01–04 (front desk / money / operations+admin).

## 8. Acceptance criteria
| ID | Test |
|---|---|
| AC-M01 | Toggle switches Auto/Desktop/Mobile instantly, keeps page/record, persists per device, survives refresh |
| AC-M02 | Each of the 9 roles sees only permitted tabs; badges correct |
| AC-M03 | Android back closes sheet → wizard step → page; unsaved form asks before leaving |
| AC-M04 | Every dialog usable at 360×800 with keyboard open; primary button never hidden |
| AC-M05 | Reception completes booking → check-in on phone only |
| AC-M06 | Cashier settles ₹3,400 split (₹2,000 cash + ₹1,400 UPI), returns deposit, closes shift on phone; figures match S0 oracle |
| AC-M07 | Housekeeper cleans Room 105 one-handed (all targets ≥ 48 px), sees no money |
| AC-M08 | Manager closes the day on phone; reports drill-down totals equal desktop |
| AC-M09 | Full S0 walkthrough runs on phone mode with identical figures to desktop (₹4,400 invoice; drawer ₹10,200→₹10,700→₹12,200→₹11,900) |
| AC-M10 | Can be added to Android home screen over LAN (limits documented if HTTP) |
| AC-M11 | No page-level horizontal scroll, no console errors, all inputs labelled, text contrast AA, at 3 phone sizes; physical device result recorded separately from emulation |

## 9. Decisions needed from owner
| ID | Question | Proposed default |
|---|---|---|
| DEC-044 | Approve "one codebase, separate mobile shell" approach and Auto/Desktop/Mobile toggle stored per device | Approve |
| DEC-045 | Which roles must work fully on phone in the prototype? | All 9; access-matrix **editing** desktop-only |
| DEC-046 | Calendar on phone: agenda view (default) vs compact grid | Agenda default, grid optional |
| DEC-047 | Printing (invoice, registration card, closing report) on phone | Hidden on phone in prototype; share/print later |
| DEC-048 | Test phone model(s) for physical validation | Owner's Android phone + one mid-range device |
| DEC-049 | Colour scheme for both versions (open from 24 Oct feedback) | Current green with high-contrast text; revisit at design review |

## 10. Sequence
1. Owner approves §9 decisions.
2. MB-01–04 foundation (toggle + shell + sheets + components).
3. MB-05 / MB-06 / MB-07–08 in parallel streams; MB-09 walkthrough.
4. MB-10–11 checks, physical Android test, delivery summary.
Prerequisite carried over from M3: finish the desktop UI walkthrough and responsive checks (BL-023, BL-035) first or alongside MB-11, so mobile and desktop are validated against the same build.

## 11. Owner decisions (24 Oct 2026)
| ID | Decision |
|---|---|
| DEC-044 | Approved: one codebase, separate mobile shell, Auto/Desktop/Mobile toggle stored per device |
| DEC-045 | Approved: all 9 roles fully usable on phone; access-matrix editing desktop-only |
| DEC-047 | **Changed:** printing stays available on phone (wireless printers). Invoice, registration card, booking confirmation and closing report get phone-friendly print layouts via the browser print/share dialog. Update M07/M09/M11/M18/M19 accordingly |
| DEC-048 | Validation on Android emulator (Chrome on emulated Pixel-class devices 360×800, 412×915) plus Playwright emulation; recorded as **emulated**, not physical-device evidence |
| DEC-049 | Open — owner choosing between colour options A–D (see chat 24 Oct) |
| DEC-049 | **Approved: Option D — Maroon & Sand.** Primary maroon #7A1F2B, background sand #FAF6F0, text dark brown #2B1D16; status colours unchanged (with text labels). Applies to desktop and mobile; replaces the green theme and the 24 Oct contrast override |
| DEC-050 | **Approved: mobile is part of the same single HTML** (`Hotel Control Centre.html`), switched by the display toggle — no separate mobile file |
| DEC-051 | **Approved direction: later wrap the same HTML in an Android APK with Capacitor** for on-phone usability testing. Prototype only; not a production release |

## 12. Capacitor-readiness requirements (so the same HTML wraps cleanly later)
- Single self-contained file: no CDN, no external fonts/scripts, relative paths only; works from Capacitor's local web server (no `npm start` needed on the phone).
- Display mode **Auto** detects the phone; inside the APK default to Mobile.
- Android hardware back button: handled through the History API now; in the APK also listen to Capacitor's App `backButton` event (sheet → wizard step → page → confirm exit).
- Storage: browser localStorage works inside the WebView; demo data stays on that phone; Reset available in the demo sheet.
- Printing (DEC-047): `window.print()` is not reliable in an Android WebView — the APK will need a print/share plugin (to be selected and verified at wrap time); in the browser, Chrome's print to wireless printer is used.
- Safe areas, status-bar colour (maroon), app icon and splash use the Option D palette.
- File downloads (CSV) in the WebView need the share/filesystem route — verify at wrap time.
- Wrap step (later milestone MB-12, after mobile acceptance): `npx cap init` → copy `dist/index.html` as the web asset → `npx cap add android` → build debug APK → install on the owner's phone. Requires Android Studio/JDK on the PC; not done in the prototype build itself.

| ID | Item | Size |
|---|---|---|
| MB-00 | Apply Option D theme tokens across desktop + mobile; remove green palette and contrast override; AA contrast check | S–M |
| MB-12 | Capacitor wrap: project scaffold, back-button + print/share plugins, debug APK, install guide | M |
