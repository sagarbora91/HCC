# W0-D Prototype Acceptance Report

> **ACCEPTED by the owner, 25 Oct 2026 (APR-011)** on APK v9 (SHA-256 `600aba4172c711ce6d6d454df6f0185168c50333d94eb206800c448849642369`), with the Tab S7 items (T-01…T-05 on v9, long-session Back) accepted as **unverified**. Conditions status: §7.1a.

Prepared 25 Oct 2026 (Session 11) from the evidence files listed in section 8. Every figure below is quoted from those files; nothing was re-run for this report. Scope: browser walkthrough, emulator and physical-tablet acceptance of the Hotel Control Centre prototype (Wave 0, step W0-D, brief `02 Planning/agents/W0-D_Acceptance_Brief.md`). Fictional data only. Wave 0.5 work (cash book, payroll, compliance, exports, master configuration) is **not** part of this acceptance.

## 1. Verdict in one paragraph
All scripted S0 flows pass in the browser (15/15), on the API 35 emulator on APK v8 (Part 2 21/21, Part 3 17/17) and on the physical Tab S7 (14 Pass, 1 Fail, 2 Not run, on an older APK). Two blockers found on old Android WebView (AC-01 S1, AC-02 S2) are fixed and verified on WebView 83. Open: 3 S3 (T-01, T-02, AC-04) and 2 S4 (AC-03, R-01) defects, plus one S3 (T-05) that was not reproduced. Recommendation: **ready for owner acceptance - yes, with conditions** (section 7).

## 2. Scope and builds tested

| Item | Value | Source |
|---|---|---|
| Final APK (v8) | `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`, versionCode 8, SHA-256 `2BA3FE68B03E2C535B758D288B3B8862F6CE3F87BB592A39E63A32C00F8A52C3` | D5_Roles.md, D5_AC.md |
| v8 web bundle | `03 Prototype\dist\index.html`, SHA-256 starts `76519ceb…` (ends `…2aa4d`); main verified tsc clean, **502/502**, build 1,884 KB | HANDOFF.md Session 11; W0-D_Final_Emulator_Brief.md |
| APK v7 (WebView 83 re-test) | SHA-256 `E87C692BD2DFE7EB70FE500D847CF1087340ADD21F4B5272CCAFCD4C7A5173EF` | D5_AC.md |
| APK v6 (Tab S7, WebView 83 first run) | SHA-256 `49c184d32d5e7eb099e474b185b9b9926e8bb38f8eb1ff940ccf86eddf75946b` | D6_TabS7.md, D5_AC.md |
| Browser walkthrough bundle | `dist\index.html` SHA-256 `4e33f7d6…c1dd` = same bundle as APK versionCode 6 | BL023_Walkthrough.md |

Devices and labels:

| Label | Device | Build | Report |
|---|---|---|---|
| BROWSER | Claude browser pane, 1366x768, Display Auto (desktop layout), scenario S0 | v6-equivalent bundle | `BL023_Walkthrough.md` |
| PHYSICAL | Samsung Galaxy Tab S7 SM-T875, Android 13, 1600x2560 @ 380 dpi | **APK v6 only** (not re-tested on v7/v8) | `D6_TabS7.md` |
| EMULATED | AVD `codex_api35_google`, Android 15 / API 35, WebView 124.0.6367.219, phone layout 412x842 css px | **APK v8** | `D5_Roles.md`, `D5_AC.md` |
| EMULATED | AVD `codex_apk_review`, Android 11, WebView 83.0.4103.120, phone layout 412x660 css px | v6 (fail), v7 (AC-01 re-test), **v8 (AC-02 re-test only)** | `D5_AC.md` |

Important: the browser walkthrough and the Tab S7 ran on pre-fix builds. The API 35 Part 3 run on v8 reproduced the same S0 oracle (drawer ₹10,750 → ₹11,250 → ₹12,750 → ₹12,450; Reports 24-26 Oct net revenue ₹62,500 / receipts ₹25,900 / expenses ₹2,950, identical to the desktop run, D5_AC.md AC-M08c), so the money flows are evidenced on the final build.

## 3. Results by area

| Area | Pass | Fail | Not run | Report / evidence |
|---|---:|---:|---:|---|
| BL-023 S0 walkthrough, browser 1366 px (15 steps incl. 3b, 4a/4b, 5b) | 15 | 0 | 0 | `BL023_Walkthrough.md`; screenshots `bl023\01…10-*.jpg` |
| API 35 emulator, APK v8, Part 2 (10 role rows, 5 Back rows, Reset, CSV+share, print A4, D-01, T-04) | 21 | 0 | 0 | `D5_Roles.md`; `d5-roles\v8-*` |
| API 35 emulator, APK v8, Part 3 (AC-M05..M08, BL-01, steps 4a/5b/7/8/9) | 17 | 0 | 0 | `D5_AC.md`; `d5-ac\v8-*` |
| Tab S7 physical, APK v6 | 14 | 1 | 2 | `D6_TabS7.md`; `APK\Tab-S7\w0d-*.png` |
| WebView 83 emulator, v6 first run | 1 (install) | 1 (cold start, AC-01) | 4 (AC-M05..M08) | `D5_AC.md`, `defects\AC-01-webview83-stuck-loading.png` |
| WebView 83 emulator, v7 re-test (AC-01) | 4 (two with a test-only CSS shim for AC-02) | 0 | 0 | `d5-ac\v7-ac01-*` |
| WebView 83 emulator, v8 re-test (AC-02, no shim) | 2 | 0 | 0 | `d5-ac\v8-ac02-*` |
| Automated suite on main (not a device run) | 502/502 | 0 | 0 | HANDOFF.md Session 11 |

Notes on the counts:
- Tab S7: the report's "Pass 14 · Fail 1 (T-05 long-session Back) · Not run 2" covers checks 1-16. The two Not run are the Reception-blocked restriction variant (check 10, covered on the emulator by D-C) and the portrait Arrivals sideways-scroll check (check 15, S0 has 0 arrivals). Check 7 is "Pass (cold start) / Fail (long session, T-05)". Check 17 is notes.
- Part 3 "Step 5 role gating" passed as *not attempted as an action*: the control or page is absent for Reception and Housekeeper (stricter than the guide's "denied" error).
- WebView 83 Parts 2-3 (roles, AC-M05..M08) were **not run** on v8 (section 6).

### 3.1 Highlights that match the S0 oracle (DEC-052 shared till)
- Drawer ₹10,750 at start (₹5,000 + ₹7,000 + ₹550 restaurant cash − ₹1,800 per Tab S7 #9) → ₹11,250 after ₹500 deposit → ₹12,750 after ₹2,000 cash settlement and ₹500 deposit return → ₹12,450 after ₹300 expense (BL023 steps 1-9; D5_AC.md AC-M06).
- Day close 24 Oct blocked by EXP-102, POS-F03 (T2 ₹1,914) and POS-F04 (201 ₹330) until resolved (BL023 step 4a; D5_AC.md AC-M08a).
- Room 105: net ₹4,000, tax ₹400, invoice ₹4,400 (DEMO/2026-27/0001), one housekeeping task (Ravi), Ready after Neha's inspection (BL023 steps 7-8; D5_AC.md).
- Restricted 7→8 Nov stay: Reception blocked, Manager override with reason recorded (HCC-1101 on the Tab S7 with real taps; HCC-1108 and the Reception-blocked case on the emulator).
- Role/tab/drawer matrix verified for Owner, Manager, Reception, Cashier, Supervisor, 2 Housekeepers, Accountant, Maintenance, Tech admin (D5_Roles.md); housekeeping/maintenance see no hotel money (only their own pay, R-01).
- Device behaviours on v8/API 35: Back (sheet → page → category → Modules → exit prompt) after clean launch, print, CSV share, Display switch and Reset all pass; CSV file in `Documents/HCC` (818 bytes) plus share sheet; print preview "ISO A4", one page, no beige block.

## 4. Defects (W0-D)

Severity scale as used in the evidence files: S1 blocker, S2 major, S3 moderate, S4 cosmetic/observation.

| ID | Sev | Summary | Status | Evidence |
|---|---|---|---|---|
| AC-01 | S1 | App hangs on "Loading demo data…" on old WebView (Intl `maximumFractionDigits`, `structuredClone`; source also removed `.at`/`replaceAll`) | **Fixed + verified**: WebView 83 on v7 (sign-in, Cashier ₹10,750, payment posted); v8 on WebView 83 loads (v8 AC-02 re-test ran on fresh data, no shim). Source: 492/492 + compat tests | `AC01_Source_Verification_2026-10-25.md`; `D5_AC.md`; `d5-ac\v7-ac01-*`; `defects\AC-01-webview83-stuck-loading.png` |
| AC-02 | S2 | Sheets/modals invisible on WebView < 87 (CSS `inset` shorthand) | **Fixed + verified** on v8, WebView 83, no shim: overlay y=0 (was 1449), DEMO and Record-payment sheets visible by real taps. Minor: `:has()` rules ignored on WV83 | `AC02_Source_Verification_2026-10-25.md`; `D5_AC.md`; `d5-ac\v8-ac02-*`; `defects\AC-02-*` |
| AC-03 | S4 | Count sheet formula "₹5,000 + ₹7,500 − ₹1,800 = ₹11,250" omits ₹550 restaurant cash (total is right) | **Open** (HANDOFF Session 11 lists a phone-fix agent covering AC-03/04; not yet verified) | `defects\AC-03-*.md/.png` |
| AC-04 | S3 | Native "Discard the details you entered?" dialog after a successful count submit or Reports date-range Apply; WebView frozen until answered; reproduced 4 times | **Open - being fixed for APK v9** | `defects\AC-04-discard-dialog-after-submit.md`, `…-after-count-submit.png` |
| BL-01 | S4 | Approved discount shows "Expired" pill in folio requests | **Fixed + verified** on v8 API 35: Approvals → Approved shows "Status Approved · Decided 25 Oct 08:02 · Meera". Source regression 497/497 | `BL01_BL02_Fixes_2026-10-25.md`; `d5-ac\v8-bl01-discount-approved.png`; `defects\BL-01-*.jpg` |
| BL-02 | S4 | Saving a role-template permission also logged an unrelated "Limit … not set → ₹250" and policy v1 → 3 | **Fixed in source only** (PolicyLimitEditor; 497/497, server-rendered + command tests). **No browser/device re-test found** | `BL01_BL02_Fixes_2026-10-25.md` |
| D-01 | not recorded | POS settle dialog said outlet cash is "separate from the front-desk drawer" (contradicts DEC-052) | **Fixed + verified** on v8 API 35, T2 Settle by Cash: "Cash sales count once in the shared front-desk drawer…". Related source change: Expenses & cash now includes POS cash (BASE ₹7,550 includes ₹550 once) | `Shared_Till_Reports_2026-10-25.md`; `d5-roles\v8-d01-settle-dialog-cash.png`; `defects\D-01-*.jpg` |
| T-01 | S3 | Tab S7: portrait top bar ≈ 248 of 1006 px (~25 %); landscape drawer list 254 of 601 px | **Open - owner decision.** Source mitigation in v8 (coarse-pointer `z-tablet.css`): local browser header 85 px portrait / 65 px landscape, drawer nav 754 / 349 px; **not re-measured on the physical tablet** | `defects\T-01-*.md`; `Print_Tablet_Back_Verification_2026-10-25.md`; `tablet-local\` |
| T-02 | S3 | Desktop layout on the tablet: controls 32-38 dp (selects 22 dp), below 48 dp; all real taps still hit | **Open - owner decision.** Source mitigation: sampled header/Reservations controls ≥ 48 px in local touch emulation (not every module); not re-measured on device | `defects\T-02-*.md`; same fix note |
| T-03 | S4 | Android print preview: Letter paper, empty beige block | **Fixed + verified** on v8 API 35 ("ISO A4", one page, white below). Local Chromium: 1 A4 page short report, 4 A4 pages / 62 rows long report. **Tab S7 not re-checked** | `Print_Tablet_Back_Verification_2026-10-25.md`; `d5-roles\v8-print-service-preview.png` |
| T-04 | S4 | "1 nights" in booking list/details | **Fixed + verified** on v8 API 35: phone card "Room 101 · 7 Nov – 8 Nov · 1 night" (phone cards show no guest count; "1 night · 1 guest" exists only in the desktop table, not device-checked) | `D5_Roles.md`, `D5_AC.md`; `d5-roles\v8-booking-list-1-night.png`; HANDOFF Session 10 |
| T-05 | S3 | Tab S7: 24 Back presses on Modules Home did nothing after a long share/print/Reset/Display/rotation session (intermittent, never reproduced) | **Mitigated, not verified physically.** Per-entry history idx + fallback + timer cancellation; real-app browser test with mocked native bridge; API 35 emulator: all 5 Back rows pass, not reproduced. Original sequence not re-run on the tablet | `defects\T-05-*.md`; `Print_Tablet_Back_Verification_2026-10-25.md`; `D5_Roles.md` |
| R-01 | S4 (observation) | Housekeeping/maintenance (also supervisor) users see their own pay (6 ₹ amounts, e.g. ₹10,926) on "My shifts & pay" | **Open - owner decision** (no hotel money elsewhere) | `defects\R-01-*.md`; `D5_Roles.md` |

Open by severity: **S3: T-01, T-02, AC-04** (plus T-05 unverified); **S4: AC-03, R-01** (plus BL-02 source-fixed, UI-unverified). S1/S2: none open.

### 4.1 Unlogged observations (from the evidence, not defects)
- BL023 note 3: a disabled user (Aarav) sees "no active access" and no pages, so no "denied command" audit row exists; the audit shows the POLICY SET entries.
- BL023 note 6: Reports → Expenses & cash "Cash into drawer" was ₹9,500 and excluded the ₹550 restaurant cash; addressed in source by the shared-till report change (Shared_Till_Reports), not re-checked in the UI.
- D5_Roles / D5_AC: UPI settle with an empty reference gives no visible message (not investigated); phone breadcrumb buttons are 40 px high (below 48 px).
- D6_TabS7: Mobile layout on the 1078 px tablet is stretched edge-to-edge; desktop layout remains the better tablet choice (Session 7b); Print opens a second blank WebView target.

## 5. Wave 0.5 items - not part of the prototype acceptance
Found by the Wave 0.5 browser check on `integration/w05` (`W05_Browser_Check.md`; defect files `defects\W05-*.md`); tracked separately:
- W05-1 Compliance calendar listed for Manager and Cashier (S3)
- W05-2 Phone Reports has no Excel
- W05-3 Phone Accounting has no Tally
- W05-4 Phone Day closing has no day pack and no cash book link
- W05-5 Phone Dashboard has no Due card
- W05-6 Phone touch targets under 48 px
- W05-7 Cash book carry-forward gap
- W05-8 Cash book has no Excel

## 6. Not verified / limits of this evidence
- **Tab S7 was not re-tested on v8** (or v7). The T-01, T-02, T-03, T-04 and T-05 fixes are verified only on the API 35 emulator or in local browser emulation, not on the physical tablet. Physical finger comfort, rotation and a long-session Back sequence (share, print, Reset, Display switch, rotation) are unconfirmed.
- Tab S7 portrait Arrivals sideways scroll and the Reception-blocked variant on the physical device: Not run.
- **WebView 83 Parts 2-3 not run on v8**: only sign-in/Cashier/payment (v7) and the AC-02 sheets (v8) are evidenced there. WebView versions between 83 and 124 were not tested.
- ~~BL-02 has no browser confirmation~~ — **closed 25 Oct:** confirmed in the UI on the v8 bundle (`BL023_Walkthrough_v8.md`).
- ~~Desktop not re-walked on v8~~ — **closed 25 Oct:** BL-023 re-run on the v8 bundle (`76519ceb…`), **15/15 Pass**, all figures match; D-01, BL-01, T-04 confirmed. AC-04 also reproduces on desktop after the Day closing count sheet (BL-v8-1, same defect).
- The emulator runs used DevTools to read the DOM, switch "Preview as" and type into fields, with real taps for key actions (D5_Roles.md, D5_AC.md). The browser walkthrough used some DOM scripting for reads, list-row clicks and the Reports date change (BL023_Walkthrough.md).
- Print and share were exercised up to the Android print/share sheets; nothing was printed or shared (D6_TabS7 #11, #12).
- Not tested at all: production concerns (DB, server, outbox, backup/restore), keyboard-only walkthrough at five sizes, S1/S2/S5 scenarios, refund/credit-note flow (matrix "Not built").
- Test-count caveats in the fix notes: the AC-01 note reports 492/492 with a third test added afterwards; the shared-till note reports 499/500 then a 3/3 focused re-run; the print/tablet note reports 501/501 before final refinements. The only full-suite figure for the v8 bundle is 502/502 on main (HANDOFF Session 11).

## 7. Open decisions and recommendation

### 7.1 Owner decisions needed
1. **T-01 portrait top bar** (S3): **DECIDED 25 Oct 2026 — DEC-059:** keep the compact header already in v8; re-check on the Tab S7 and move to a single-row header only if still cramped. Options: `02 Planning\options\T-01_T-02_Decision_Brief.md`.
2. **T-02 touch sizes on the desktop layout** (S3): **DECIDED — DEC-060:** on touch devices dense rows/filters 44 px, primary actions/menu/dialog buttons 48 px (CSS variables); Windows desktop unchanged. Being built for APK v9.
3. **R-01** (S4): **DECIDED — DEC-061:** housekeeping and maintenance may see their own pay and payslip on "My shifts & pay"; never hotel money.
4. **AC-04** (S3): confirm the dialog fix lands in APK v9 and is re-tested before acceptance.
5. **AC-03** (S4): count-sheet formula to include the ₹550 restaurant cash (text only; the ₹11,250 total is correct).
6. Optional product point: the Reports "Cash into drawer" definition (BL023 note 6) after the shared-till change.

### 7.1a Status of the conditions (25 Oct 2026, evening)
- (a) **AC-04 — met.** Fixed on `integration/w05` and re-tested on **APK v9** (versionCode 9, SHA-256 `600aba41…2369`, web bundle `85916176…`): no discard dialog after a successful count submit (Cashier, Day closing) or Reports "Apply range"; the unsaved-edit guard still asks. `APK_v9_Emulator_Retest.md`.
- (b) **T-01, T-02, R-01 — decided** (DEC-059, DEC-060, DEC-061); DEC-060 touch sizes built and measured in the browser re-check (`Fixes_Recheck_2026-10-25.md`).
- (c) **AC-03 — met:** formula shows the ₹550 restaurant cash and adds up (browser + v9).
- (d) **Tab S7 re-check — not done.** v9 cannot be installed over v8 (V9-1: the old PC's debug signing key was not migrated), so the tablet needs an uninstall (fictional demo data only) before v9. Owner to accept the tablet items as unverified or run the re-check.
- (e) **BL-02 — met** (`BL023_Walkthrough_v8.md`).
- v9 S0 regression on the emulator passes (drawer ₹10,750 → ₹11,250 → ₹12,750 → ₹12,450; Reception discount approved by the Manager per DEC-063; Android Back). Open S4: V9-2 (Cashier count switch 44 px, Reports range labels 14 px on the phone).

### 7.2 Recommendation
**Ready for owner acceptance: YES, with conditions.**
- The 15 S0 steps, API 35 Part 2 (21/21) and Part 3 (17/17) all pass on the final APK v8 with no S1/S2 defect open; the two old-WebView blockers are fixed and verified on WebView 83.
- Conditions: (a) AC-04 fixed and re-verified on APK v9 (it fires after every count submit and range Apply); (b) owner rulings on T-01, T-02 and R-01; (c) AC-03 text corrected or accepted as a known S4; (d) a short Tab S7 re-check on v9 of T-01..T-05 including one long-session Back sequence, or the owner explicitly accepts the tablet items as unverified; ~~(e) BL-02 confirmed once in the UI~~ (done 25 Oct, `BL023_Walkthrough_v8.md`).
- Not a gate for this acceptance: the W05-* defects (section 5).

## 8. Source files
`BL023_Walkthrough.md`, `D6_TabS7.md`, `D5_Roles.md`, `D5_AC.md`, `AC01_Source_Verification_2026-10-25.md`, `AC02_Source_Verification_2026-10-25.md`, `BL01_BL02_Fixes_2026-10-25.md`, `Shared_Till_Reports_2026-10-25.md`, `Print_Tablet_Back_Verification_2026-10-25.md`, `W05_Browser_Check.md`, `defects\*.md` (all under `05 Delivery Evidence\Acceptance\`); `HANDOFF.md` Sessions 10-11; `02 Planning\agents\W0-D_*Brief*.md`; `02 Planning\HCC_Decision_Register.md` (DEC-052 shared till, DEC-053 restriction override). Acceptance matrix updated: `02 Planning\HCC_Acceptance_Matrix.md`.
