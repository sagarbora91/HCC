# HCC training media plan — wave TRN (planner: Fable, executor: Sonnet) — 7 Nov 2026

Owner's ask (7 Nov 2026): "in the similar fashion [to the Manual System training] create similar training material for HCC covering each and every module and function; you become the planner and Sonnet becomes the executor."

This document is the whole plan. The executor (one Sonnet session, with Sonnet sub-agents where this plan allows) follows it top to bottom and reports at the phase gates in §9. Facts in §1 were read from the code on 7 Nov 2026; verify the hashes before starting.

---

## 0. One-page summary

| | |
|---|---|
| Product trained | **Hotel Control Centre (HCC)** web app, desktop shell first (1366×768), phone shell second (412×915), from the **prototype flavour** of the build (`npm run build` in `03 Prototype`, mock service, FY26 demo data, demo controls hidden in every shot) |
| Source commit | Cut branch `claude/training` from **`origin/main` = 3a0c2d0e** (ADM-UX included; GitHub is ahead of the local `main` 91024d16, which only adds a docs commit). Never merge to `main`; push the branch only |
| Pipeline | `/training-media` skill (`C:\Users\Sagar\.claude\skills\training-media\`): one `steps.json` → outlined screenshots → PPTX deck with narration notes → English + Hinglish narration → TTS → MP4 per chapter + full course → checks |
| Coverage | **25 chapters, ≈ 460 steps**, one chapter per module of the 18-module feature catalogue (`src/domain/feature-catalogue.ts`, 60 sub-functions) and every one of the 37 registered pages + Dashboard + Modules Home (§4 is the coverage matrix; a page or sub-function missing from it is a defect) |
| Story | One fictional guest, **Mr. Rahul Deshmukh, Pune** (same person as in the Manual System training so staff see one story in both), room **203 Deluxe CP**, arrives business date **24 Oct 2026**, 2 nights; plus the supporting records in §3 (walk-in, company guest, group block, hall event, restaurant check, expense, repair, leave, payroll, day close, accounts) |
| Roles | 10 HCC roles (Owner, Manager, Reception, Cashier, Supervisor, Housekeeper, Accountant, Maintenance, Tech admin, Restaurant). Deliverables are **per module** (chapter videos) with **per-role tracks** (ordered playlists), **role cards** (A4 PDF per role) and a **trainer's guide**; same shape the owner accepted for the Manual System |
| Languages | English (Windows Zira) and Hinglish (edge-tts hi-IN-SwaraNeural; Hindi in Devanagari, UI words in English) |
| Deliverables | `E:\Projects\HCC\Hotel Control Centre\06 Training\hcc-web\` (pipeline sources, committed) and `...\06 Training\hcc-phone\`; outputs in `build\` folders (git-ignored) and the final copy in `E:\Projects\HCC\Builds\<date>\training-hcc\` (decks, MP4s, role cards, trainer's guide, README, TRAINING_NOTES) |
| Phases | P0 setup + driver → P1 core story proven (chapters 0–8 captured) → P2 all chapters → P3 deck + English → P4 Hinglish → P5 TTS + video → P6 phone deck → P7 role cards, trainer's guide, README, Builds copy. Gates in §9 |
| Models | Planner Fable (this document; answers the executor's questions). Executor **Sonnet**: driver code, chapter scripts, captures, deck, narration proof-reading, Hinglish (sub-agents, one wave at a time), media, checks. No Opus needed: no money rules are changed, only shown |
| Size estimate | ≈ 520 slides, English ≈ 3 h 20 min, Hinglish ≈ 4 h, decks ≈ 60 MB each, MP4 ≈ 1.2 GB + 1.5 GB; executor ≈ 3 agent days sequential (no-heat rules) |

---

## 1. Facts from the code (7 Nov 2026)

- **Repo and layout.** `E:\Projects\HCC\Hotel Control Centre` (checkout on `claude/audit-fable-2026-10-26` = local `main` 91024d16, never check out `main` there). Feature work goes in a worktree `E:\Projects\HCC\worktrees\<name>` on `claude/<name>`; new worktrees need `03 Prototype\node_modules` as a **junction** to `E:\Projects\HCC\worktrees\fixwave-a\03 Prototype\node_modules` (never delete `fixwave-a`; remove the junction with `rmdir` before removing a worktree). `.claude\launch.json` is a local edit: leave it. Never use bare `git stash`.
- **Build and serve.** In `03 Prototype`: `npm run build` → `dist\index.html` + `Hotel Control Centre.html` (single self-contained HTML, ≈ 2.4 MB; the copy in the checkout is from 26 Oct and stale — always rebuild). `npm start` serves `http://127.0.0.1:4173` (`serve.mjs`). `npm test` (`node --max-old-space-size=8192 tests/run.mjs`, ≈ 2,900 tests on main, must stay green) and `npx tsc --noEmit`. Node 25 at `E:\Tools\nodejs`. No new npm packages in `03 Prototype`.
- **Build flavours.** `build.mjs`: default **prototype** (mock adapter, demo toolbar, scenario switcher, simulated clock, Reset) or `--production` (compiled out via `process.env.HCC_BUILD`). The training driver must live in the prototype flavour only (guard with `process.env.HCC_BUILD!=='production'`, same way `src/app/demo/` is removed).
- **Demo controls** (`src/app/demo/DemoToolbar.tsx`, CSS `.demo-toolbar` in `src/styles/demo.css`): Scenario (FY26 default = BASE + 206 generated business days 1 Apr–23 Oct 2026; also BASE, S0, S1, S2, S3, S6), **Preview as** (switch demo user — this is the role switch; there is no sign-in screen in the prototype), Next-action fault, display mode Auto/Desktop/Mobile (`src/app/display.tsx`, localStorage `hcc-display-mode`), Reset, Walkthrough (S0 12-step script). App context `useHcc()` (`src/app/context.tsx`) exposes `switchUser(id)`, `loadScenario(id)`, `resetScenario()`, `open(modal)`, navigation. **Hide `.demo-toolbar` in every screenshot** (`capture.hideSelectors`).
- **Clock and data.** `BUSINESS_DATE='2026-10-24'`, clock starts 08:00 and advances one minute per action (`s.clock`); real time is never used. Persistence = IndexedDB (`src/app/storage.ts`, manifest `hcc-slices-1`) — the driver must start every capture from the fixture (reload scenario FY26 on `replay` of step 0.1 and whenever an earlier step is requested) and must not save during training (`?training=1` ⇒ skip `saveStored`, or clear IndexedDB on start; executor's choice, say which in README).
- **Fixture hotel (fictional, not Hotel Parth):** 24 rooms 101–108, 201–208, 301–308; types Classic ₹2,000, Deluxe ₹3,000, Suite ₹4,500 (room 106/206/306 = Suite; 103/104/107/203/204/207/303/304/307 = Deluxe; others Classic); base rooms 108/204/208 Dirty, 303 Inspection, 305 Cleaning, 308 blocked (FY26 history changes the day's picture — read the board on the build you capture from). Demo users: `owner` Sagar · Owner, `manager` Meera · Manager, `reception` Aarav · Reception, `cashier` Priya · Cashier, `supervisor` Neha · Supervisor, `housekeeper` Ravi, `housekeeper2` Kavya, `accountant` Farhan, `maint` Joseph · Maintenance, `techadmin` Lata · Tech admin (+ FY26 adds C01/C02 custom-role users and possibly a Restaurant user — check `s.users` on the build). GST: dated GST master (DEC-069): rooms ≤ ₹7,500 → 5 %, restaurant 5 %, other services and halls 18 %, CGST + SGST always (DEC-136).
- **Navigation.** `src/app/navmap.ts`: 10 categories (Front Desk, Cash & Billing, Housekeeping & Maintenance, Guests & Services, Customers, Revenue & Sales, Stock & Purchasing, People & Payroll, Accounts & Reports, Admin & Setup) + pinned Dashboard; Modules Home (`home`) and category tile pages; Ctrl+K search palette with quick actions New reservation / Walk-in guest / Add expense. 37 registered pages (ids): rooms, calendar, reservations, guests, arrivals, keys, desk-alerts, folios, cashier, cashbook, pos, expenses, counter-bills, housekeeping, maintenance, handover, inhouse, guest-services, gsv-my-requests, messages, documents, customers, yield, groups, channels, booking-site, tariff-card, hall-booking, inventory, staff, my-hr, closing, reports, report-hub, owner-digest, revenue-category, accounting, assistant, compliance, plans-extras, access, settings, approvals, backup, syshealth, legacy-import, prefs, roadmap (+ overview = Dashboard).
- **Feature catalogue** (`src/domain/feature-catalogue.ts`, 18 modules): Front desk (room board, reservations, arrivals & check-in, guest register, reminders & wake-ups, room keys) · Cash & billing (folios, cashier & payments, cash book, counter bills, expenses, invoices & credit notes) · Restaurant & POS (outlets & checks, menu & prices) · Housekeeping (board & tasks, complaints, lost & found) · Maintenance (work orders; assets, AMC & preventive plans) · Guest services (requests, in-house list, checklists & handover) · Guest messaging (messages, templates & rules) · Document capture (ID capture) · Customers (Customer 360, CRM notes/tags/merge) · Revenue & sales (tariff & yield, groups & companies, OTA channels, OTA payouts, direct booking site, tariff card, guest feedback & QR, hall booking, hall quotations) · Stock & purchasing (linen & stock, purchases & vendor payments, asset register) · People & payroll (staff/shifts/attendance, leave, payroll, my shifts & pay) · Day closing (day close) · Accounting (chart/ledgers/opening balances, manual vouchers & loans, GST set-off & month lock, TDS, Tally export, year end, bank reconciliation) · Reports (reports, reports hub, revenue & owner digest, meal plans & extras) · Compliance (calendar) · Management assistant · Admin & setup (Administration, Master configuration, Masters, Approvals & audit, Backup & recovery, System health, Appearance & display, Legacy import, Internet access, Roadmap).
- **No training driver exists** (no `window.TRAINING` anywhere in `src`). 4 `data-testid`, 266 `aria-label` attributes: resolve targets by aria-label, button text, role and stable class names; add `data-tr="<id>"` attributes to controls that have no stable hook (harmless in production).
- **Room keys** are fully simulated in the prototype (mock encoder, `KEY_AGENT_STEP`), so the Key chapter can be captured without hardware.
- **Rules that apply to the captured content:** fictional data only (DEC-… throughout); Housekeeping and Maintenance never see hotel money (DEC-061) — their shots must show the masked view; money / tax / payroll settings are Owner-only (DEC-058); SCC-ported modules behave as SCC did (DEC-116) — teach what the build does, never a hoped-for behaviour.

---

## 2. Decisions taken by the planner (defaults; the owner may overrule)

| # | Decision | Why |
|---|---|---|
| T1 | **Capture from the FY26 demo fixture (fictional 24-room hotel), not a Hotel Parth dataset.** The deck's disclaimer and the README "Deviations" say so (rooms 101–308, Deluxe ₹3,000, demo users). | A Hotel Parth fixture (34 rooms, rate card, GST 5 %) is a separate masters-import piece of work (owner's rate-card CSVs still need redoing at 5 %); the training must not wait for it. When that fixture exists the pipeline re-captures in one run. |
| T2 | **Structure = one chapter per module, role tracks on top** (not one deck per role as in the Manual System). | HCC has 18 modules used by several roles each; per-module chapters avoid filming the same screen seven times, and a role track is just an ordered list of chapter videos. Role cards and the trainer's guide stay per role, as the owner liked. |
| T3 | **Driver = prototype-only module `src/app/demo/training/`** (`driver.ts`, `acts.ts`, `steps.json` embedded at build time or fetched from `/training/steps.json`), active only with `?training=1`; compiled out of the production flavour. | Skill `reference/driver-interface.md` asks for a separate bundle; HCC's single-HTML build makes a guarded module the equivalent. Production code stays clean; `npm test` and the PAGE_CATEGORY / registry tests are untouched. |
| T4 | **Role switching through `switchUser()` (Preview as)**, never a sign-in screen. Chapters say "signed in as Aarav (Reception)" in the chapter title slide; the top-bar user chip is visible in every shot. | The prototype has no sign-in; the production server does (PIN/password). README deviation: "On the real system you sign in with your PIN; the training switches users directly." |
| T5 | **Phone deck = the mobile shell captured in Chrome at 412×915** (display mode forced to `mobile`, `deviceScaleFactor` 2.6), **no emulator**. An APK capture (emulator, `android-apk.md`) is an optional add-on after P7 if the owner asks. | Same UI code as the APK WebView; no emulator time, no heat; one browser at a time still holds. |
| T6 | **Quiz per chapter (3 questions, answers in notes) + ten-question role check in each role card**; a separate graded manager exam (Lodge pattern) is **not** built unless the owner asks. | Matches the Manual System decks; the exam doubles the work. |
| T7 | **Outputs never committed** (`06 Training/**/build/`, `work/`, `*.mp4`, `*.pptx` git-ignored); sources (chapters, steps.json, config, content.json, Hinglish md, driver) committed on `claude/training` and pushed as a branch. Final files copied to `E:\Projects\HCC\Builds\<date>\training-hcc\`. | Repo is public on GitHub; media are GBs. |
| T8 | **Order of chapters follows the guest's day**, not the menu order (§4); the deck's menu-map slide shows the menu order. | Trainees learn by the story; the menu map is the lookup. |
| T9 | Narration voice and style as in `reference/narration-and-hinglish.md`; Hinglish glossary seeded from the page labels in §1 and the field labels met while scripting. | Consistency with the Lodge and Manual System courses. |
| T10 | Everything the build **cannot** do on the capture day goes to README "Deviations", never into a step. | Skill rule; prevents teaching ghosts. |

---

## 3. The storyline (fixed facts — put them in `steps.json.guest` and reuse verbatim)

Business date **Sat 24 Oct 2026** (the fixture's date; the FY26 history ends 23 Oct). Clock starts 08:00.

| Record | Facts |
|---|---|
| Main guest | **Mr. Rahul Deshmukh**, Pune, mobile 98XXXXXX01 (dummy), Aadhaar masked `XXXX XXXX 4321`, purpose business, car MH 12 AB 1234. Booking source Direct (phone). Arrival 24 Oct, departure 26 Oct (2 nights), **Deluxe 203, CP**, 2 adults (Mrs. Sneha Deshmukh), rate ₹3,000 + 5 % GST per night, advance ₹2,000 UPI at booking. Requests a wake-up call 06:30, two extra towels; a laundry charge ₹500 (18 %); dinner from room service ₹640 (5 %); one complaint (AC noise) → repair → service-recovery discount ₹300 approved by Manager; room moved 203 → 207 after the AC fault (second night). Check-out 26 Oct, balance paid by card, GST invoice printed, key card returned. |
| Walk-in | **Ms. Anita Rao**, Hyderabad, walk-in 24 Oct for 1 night, Classic 105, EP, pays cash in full at check-in; her counter bill for a friend's lunch (counter bill, paid now). |
| Company guest | **Shree Agro Industries** (GSTIN 27AABCS1234A1Z5, fictional) books **Mr. Vikram Jadhav** 24–25 Oct, Suite 206, on company credit (credit limit ₹50,000) — teaches company booking, credit limit, company-credit check-out, receivables in Accounting. |
| Group | **Patil wedding block**, 6 rooms 30 Oct–1 Nov, rooming list, block pickup — teaches Groups & corporate (create, hold rooms, pickup) without disturbing the main story's rooms. |
| Hall event | **Kulkarni engagement**, Hall A on 7 Nov 2026, 120 plates, enquiry → quotation with T&C → revision → confirmed with ₹20,000 advance → event-day bill at 18 %. |
| OTA | One inbound BookNest booking (fixture) applied, commission shown, payout received. |
| Restaurant | Table check T4: 2 × masala dosa, 2 × tea, KOT, settle UPI; one void with Manager approval; Rahul's room-service dinner posted to folio 203. |
| Housekeeping | 203 cleaned after the move, inspected by Neha; stayover service 206; room 108 blocked for deep clean and unblocked; lost & found: a charger found in 105 after Anita leaves. |
| Maintenance | Work order "AC noisy, room 203" raised from the complaint, assigned to Joseph, spare "AC capacitor" used, closed; room downtime 203 for 4 hours; preventive plan "Geyser service" shown; one asset with AMC. |
| Reminders | Wake-up 06:30 for 203 on 25 Oct; desk reminder "Collect ID copy from 206" with alert day. |
| Messages | Booking confirmation template to Rahul (simulated send), consent shown. |
| Stock | Linen issue 20 towels to floor 2, laundry batch out/in, GRN for 50 soaps from vendor "Sai Suppliers", monthly count. |
| Expenses | "Plumber visit ₹1,200, GST bill, ITC allowed" entered by Reception, approved by Manager; petty-cash float; an Udhaar entry; a recurring template (electricity) shown; month lock mention. |
| People | New joiner **Sunil Pawar, Room attendant** added, roster for the week, attendance marked, leave request by Kavya (1 CL) approved by Meera, payroll run for October (locked runs Apr–Sep exist), advance ₹2,000 to Ravi, payslip; Ravi's "My shifts & pay". |
| Day close | 24 Oct: pre-close list (pending expense approved, no pending credit note), night posting, close; 25 Oct opens; owner digest for 24 Oct next morning. |
| Accounting | Ledger of Rahul's receipts, party statement Shree Agro, journal voucher (owner's drawing), contra (cash to bank ₹20,000), loan entry shown, trial balance, day book, P&L and balance sheet for Oct, GST set-off for Sep + month lock, TDS on a contractor payment, Tally export file, bank reconciliation of one statement line, year-end screen (explain, do not run). |
| Admin | New user "Sunil" (Housekeeper, PIN), custom role "Night manager" from Manager base with Accounts view, switch **Reservations** sub-function off and on again (show the hidden page and the refused command), audit tab; Master configuration groups (Money and tax Owner-only), Masters: add floor 4? **No** — show room master edit of 203's amenities and the CSV import template instead; Approvals & audit; Backup now + restore drill; System health; Legacy import page (dry run button, explain only); Internet access; Roadmap. |

Operators: Reception = Aarav; Cashier = Priya; Supervisor = Neha; Housekeepers = Ravi, Kavya; Accountant = Farhan; Maintenance = Joseph; Manager = Meera; Tech admin = Lata; Owner = Sagar; Restaurant = the FY26 restaurant user if present, else Priya (say so in deviations).

Deviations to expect (write them down as found): fixture rooms differ from Hotel Parth; the demo has no sign-in; e-mail/WhatsApp/OCR are "not configured" simulations; Key Agent is the mock encoder; legacy import is explained, not run; year end is explained, not run.

---

## 4. Chapter table and coverage matrix (desktop deck)

Each lesson = one screen action per step, 3–8 steps per lesson, chapters sized as below (±20 %). **Every row of the feature catalogue and every page id appears in the "Covers" column; the executor ticks this table in `coverage.md` when the chapter replays cleanly.**

| Ch | Title (as role) | Pages | Covers (sub-functions, functions) | Steps |
|---|---|---|---|---|
| 0 | Welcome: Modules Home and the Dashboard (Aarav) | home, overview, prefs | tiles, breadcrumb, business date & clock, user chip, Ctrl+K search + quick actions, Appearance & display (text size, theme, start page), hamburger menu, help/shortcuts | 10 |
| 1 | Reservations and the Calendar (Aarav) | reservations, calendar | new reservation (Rahul), hold, confirm, amend dates, company booking (Vikram, Shree Agro), calendar grid, search, cancel a fixture booking, no-show mention | 22 |
| 2 | Arrivals, registration and check-in (Aarav) | arrivals, guests, keys, documents | expected arrivals, booking-ID lookup, registration card (every field), ID type + masked number, photo, check-in Rahul; walk-in Anita; Guest register profile; ID capture page; Room keys: issue, key log, re-issue lost card, cancel | 28 |
| 3 | The Room board (Aarav, Neha) | rooms | statuses and colours, filters, floor view, quick actions, block / unblock, room info drawer | 10 |
| 4 | In-house guests and guest services (Aarav) | inhouse, guest-services, gsv-my-requests, desk-alerts, messages | extend stay, room move 203→207, requests (towels) with SLA, complaint + recovery, lost & found, my requests view (Ravi), reminders + wake-up, messages: template, simulated send, consent, rules | 26 |
| 5 | The folio: charges, discounts and corrections (Priya) | folios, plans-extras | folio view, add catalogue charge (laundry), free-text charge, meal plans & extras page, discount request → Manager approval, correction, guest rate change (CTL-01 rule), company credit flag | 20 |
| 6 | Restaurant and room service (Restaurant user, Priya) | pos | outlet, open table check, items, KOT, room-service check posted to 203, settle UPI, void with approval, menu & prices, KOT/bill numbers | 18 |
| 7 | Cashier: shift, payments, cash book (Priya) | cashier, cashbook | open shift, receive advance UPI, card payment, drawer count, bank drop, owner drawing, close shift with difference; cash book day page, lock / reopen | 20 |
| 8 | Check-out, invoice, credit note, counter bills (Priya, Meera) | folios, counter-bills, approvals | settle Rahul (card), GST invoice (5 % split CGST/SGST, number series), print/PDF, duplicate; Vikram on company credit; refund; Owner-only credit note; counter bill paid now and Udhaar; Approvals queue | 24 |
| 9 | Housekeeping (Neha, Ravi) | housekeeping | board, assign task, start/finish, inspection pass/fail, stayover, block 108 / unblock, report repair from a room, complaints and lost & found entries, money masked (DEC-061) | 18 |
| 10 | Maintenance (Joseph, Neha) | maintenance | work order from the repair request, assign, spare part, vendor, close; room downtime; assets, AMC, preventive plan; masked money | 16 |
| 11 | Checklists and handover (Aarav, Neha) | handover | shift checklist complete, handover note, desk handover read | 8 |
| 12 | Customers 360 (Aarav, Meera) | customers | search by mobile, profile, stay history, tags, notes, preferences, consent, merge duplicates, legacy stays tab | 12 |
| 13 | Tariff, yield and the tariff card (Meera, Sagar) | yield, tariff-card | seasons, rate plans, restrictions, guardrails, override, publish; tariff card draft → propose → publish, rate sheet print | 16 |
| 14 | Groups, companies, OTA and website (Meera) | groups, channels, booking-site | group block + rooming list + pickup; company credit limit; OTA inbound applied, commission, payout; website request; guest feedback & QR | 20 |
| 15 | Hall booking (Meera) | hall-booking | venues & menus, enquiry, quotation + T&C, revision, discount, confirm, advance, event, event-day bill 18 % | 18 |
| 16 | Linen, stock and purchasing (Neha, Farhan) | inventory | linen issue, laundry batch, GRN, count, purchases & vendor payments, asset register (from Accounting), masked money for Supervisor | 16 |
| 17 | Expenses (Aarav, Meera, Sagar) | expenses | add expense with bill photo + GST + ITC, approve / reject, petty-cash float, Udhaar, recurring, budgets (Owner), edit/void with reason, month lock / Owner unlock | 16 |
| 18 | Staff, leave and payroll (Meera, Sagar, Ravi) | staff, my-hr | staff register, roster, attendance, leave request + approval (quota re-check), payroll run, advance, payslip, F&F mention, statutory (PF/ESIC/PT) view, My shifts & pay | 22 |
| 19 | Day close (Meera) | closing | pre-close checks, night posting, close day, reopen, closing reports, next day | 14 |
| 20 | Reports and the Report hub (Meera, Sagar) | reports, report-hub, owner-digest, revenue-category | reports page, hub search/run/filters/export/print/schedule/favourites, revenue by category, owner digest | 16 |
| 21 | Accounting (Farhan, Sagar) | accounting | chart, ledgers, party statements, opening balances, manual vouchers (journal, contra, receipt, payment), loans, trial balance, day book, P&L, balance sheet, cash ratio, ageing, GST set-off + month lock, TDS, Tally export, bank reconciliation, year end (explain) | 30 |
| 22 | Compliance calendar and the assistant (Farhan, Sagar) | compliance, assistant | obligations, firm profile, evidence, Action Center, ≈₹ payable; assistant questions and anomaly alerts | 12 |
| 23 | Administration and setup (Sagar, Lata) | access, settings, approvals, backup, syshealth, legacy-import, roadmap | users (create Sunil), roles (custom "Night manager"), Modules & features switch off/on, audit; Master configuration 12 groups (Money and tax Owner-only), Modules on/off; Masters (room 203 amenities, CSV import/export); Approvals & audit; Backup now + restore drill; System health; Legacy import (explain); Internet access; Roadmap | 34 |
| 24 | Closing: shortcuts, help, quick-reference (Aarav) | — | F-keys/shortcuts (`src/app/shortcuts.ts`), search palette recap, where to get help, quick-reference card | 6 |

Total ≈ **460 steps** → slides ≈ 1 + 25 + 460 + 25 recaps + 3 finals ≈ **514**.

Phone deck (`hcc-phone`, P6): 8 short chapters (≈ 90 steps) on the mobile shell: P0 app bar/tabs/More, P1 arrivals & check-in, P2 room board & housekeeping tasks (Ravi), P3 guest requests (my requests), P4 maintenance (Joseph), P5 folio & payment, P6 approvals & day close on the phone (Meera), P7 My shifts & pay + Appearance.

---

## 5. Role tracks (what each role watches, in order) and role cards

| Role | Chapters | Role card (A4) |
|---|---|---|
| Owner (Sagar) | all 0–24 | RC-1 Owner: the daily five looks (dashboard, digest, approvals, day close, system health), Owner-only powers |
| Manager (Meera) | 0–20, 22, 23 (users/approvals parts) | RC-2 Manager: approvals, discounts, day close, staff |
| Reception (Aarav) | 0, 1, 2, 3, 4, 5, 8, 11, 12, 14 (groups part), 17 (entry) | RC-3 Reception: the guest's day, keys, requests |
| Cashier (Priya) | 0, 5, 6 (settle), 7, 8, 17 | RC-4 Cashier: shift, payments, invoice, cash book |
| Supervisor (Neha) | 0, 3, 9, 10, 11, 16 | RC-5 Housekeeping supervisor |
| Housekeeper (Ravi, Kavya) | 0, 9, 11, 18 (My shifts & pay) + phone P0, P2, P3, P7 | RC-6 Room attendant (phone first) |
| Accountant (Farhan) | 0, 7, 8, 16, 17, 19–22 | RC-7 Accountant |
| Maintenance (Joseph) | 0, 10, 11 + phone P0, P4, P7 | RC-8 Maintenance |
| Restaurant | 0, 6 | RC-9 Restaurant |
| Tech admin (Lata) | 0, 23 | RC-10 Tech admin: users, backup, health, settings |

Each role card: forms → here **screens** the role owns, the sequence of a normal day, key facts (shortcuts, limits, who approves), the never-list, ten-question check with answer key. Trainer's guide (.docx, same pattern as the Manual System one): how to run a session per role, hands-on exercise per chapter on the demo build, half-day drill script, answer keys, attendance sheet.

---

## 6. Folder layout and files

```
E:\Projects\HCC\Hotel Control Centre\06 Training\
  README.md                     what is where, how to rebuild, deviations, role tracks (playlists), spot-check results
  TRAINING_NOTES.md             defects / oddities seen in the build while scripting (for the owner and the next wave)
  coverage.md                   §4 matrix ticked per chapter (+ every page id and feature id, from feature-catalogue.ts, with the chapter that shows it)
  hcc-web\
    training.config.json        from the skill template: app.url http://127.0.0.1:4173/?training=1 (dev server, no serve block), viewport 1366x768 @1.4, hideSelectors [".demo-toolbar", ".tr-coach"], output build / HCC-Training
    storyline.md                §3 + §4 of this plan, kept current
    make_steps.py, chapters_00..24.py   step/typ/btn/menu/ans/read helpers → steps.json
    steps.json                  ≈ 460 steps, schema templates/steps.template.json
    content.json                recap + 3 quiz per chapter, shortcuts, menu map, demo users (no real passwords)
    build\                      (git-ignored) shots\, deck\, work\, video\, HCC-Training.pptx, -hi.pptx, -notes.md, -notes-hinglish.md, .mp4
  hcc-phone\                    same shape, viewport 412x915 mobile, output HCC-Training-Phone
  role-cards\ (tools + PDFs), trainers-guide\ (tools + docx)
03 Prototype\src\app\demo\training\   driver.ts (window.TRAINING), acts.ts, targets.ts, index.ts (mounted only with ?training=1, prototype flavour)
03 Prototype\tests\stream-training.mjs   acts interpreter unit tests + "every step target resolves on a rendered page" smoke where feasible
```

Delivery copy: `E:\Projects\HCC\Builds\<yyyy-mm-dd>\training-hcc\` with decks, notes, MP4s (full + per chapter, en + hi), role cards, trainer's guide, README, TRAINING_NOTES, coverage.md, `SHA256.txt`.

Add to `.gitignore`: `06 Training/**/build/`, `06 Training/**/work/`, `*.mp4`, `*.pptx` under `06 Training/`.

---

## 7. The driver (what the executor builds first)

Contract: `reference/driver-interface.md` of the skill. HCC specifics:

1. **Mount.** `src/main.tsx` (or `App.tsx` after the context exists) imports `./app/demo/training` only when `process.env.HCC_BUILD!=='production'` and `new URLSearchParams(location.search).has('training')`. The module registers `window.TRAINING={replay, resolve, state}` and gets the app's context functions through a tiny registration call from `App` (e.g. `registerTrainingHooks({navigate, open, switchUser, loadScenario, resetScenario, getState})`) — no React internals, no direct `execute` calls.
2. **Determinism.** On `replay('0.1')` (and whenever an earlier step than the current one is asked): `loadScenario('FY26')`, `switchUser('reception')`, display mode per config (`desktop` or `mobile`), clear any saved slices; saving is disabled while `?training=1`. Fixed clock = the fixture clock (advances a minute per action, which is fine and deterministic).
3. **Acts** (`auto` array of a step, executed through the real UI): `nav:<page>` (via `navigate`), `user:<id>` (switchUser), `click:<target>`, `type:<target>|<value>` (set value with native setter + input/change events so React sees it), `select:<target>|<option text>`, `check:<target>`, `key:<Escape|Enter|ctrl+k>`, `menu:<Category>><Page>` (through Modules Home / hamburger), `modal:<type>` (via `open` only when a real button is not reachable; prefer click), `wait:<ms|selector>`, `js:<expr>` (last resort, log it). Each step's `check` is a boolean expression over the DOM/state the replay waits for (timeout 5 s → `{err}`).
4. **Targets** (`resolve`): `sel:<css>`, `aria:<label>`, `btn:<text>`, `tab:<label>`, `field:<label>` (label → control), `row:<text>` (first row containing text), `menuleaf:<Category>><Page>` (outline the category tile / menu entry), `modalbtn:<text>` (button inside the open dialog), `tr:<id>` (`data-tr` attribute). Prefer `aria:` and `tr:`; add `data-tr` where needed (≤ 1 line each, no behaviour change).
5. **Shots.** `capture.hideSelectors` hides `.demo-toolbar` (and the training panel if any). Keep top bar, breadcrumb and user chip visible. Dialogs stay open when the step shows them.
6. **Tests.** `tests/stream-training.mjs`: acts interpreter on a fake DOM (or on the real pages via jsdom-free smoke the suite already supports); the existing suites must stay green (`npm test`, `npx tsc --noEmit`), production build must contain no `TRAINING` string (`node build.mjs --production` then grep the output; add this assertion to the test).
7. **Replay before you write the next chapter.** `node capture.js --from 1.1 --to 1.22`; read `capture_report.json`; zero `fails` and zero `nohl` before moving on.

---

## 8. Rules for the executor (non-negotiable)

- **No-heat (work PC):** stages run sequentially; one browser, one ffmpeg (`-threads 4`), one LibreOffice; no emulator in this plan; long jobs (capture, tts, encode) in the background with a log file, no polling loops, no 7-minute checkpoint job; no new parallel agent batch while another wave of agents runs. Sub-agents (Sonnet) only for the Hinglish rewrite, 4–5 chapters per agent, **one wave at a time**, each writing only its own chapter blocks into the Hinglish md.
- **Repo:** worktree `E:\Projects\HCC\worktrees\training` on `claude/training` from `origin/main` (3a0c2d0e, after `git fetch`); node_modules junction to `fixwave-a`; commit often with clear messages; **push `claude/training` only**; never merge or push `main`; never touch `.claude\launch.json`; no new npm packages; stage only your own files; never bare `git stash`. Secret scan before each push (`tools\` has the gate helpers; a plain grep for keys/passwords/`.jks` is the minimum). The media and `build\` folders are never committed.
- **Content:** fictional data only; teach what the build does; every step's value must work on the build (replayed); money views for Housekeeping / Maintenance shots must be the masked ones; no real passwords or PINs anywhere (demo "Preview as" only); every page id and feature id of §1 appears in `coverage.md`; on-screen terms stay English in the Hinglish text; numbers kept exactly (`check.py` enforces).
- **Do not modify** the Lodge originals (`Existing Software\HTML Demo\training\`), the Manual System folder, the skill's scripts (copy and adapt if a change is needed, note it in README), or any production (`08 Production`) file.
- **Ask the planner (Fable) only** for a genuine decision (a chapter cannot be told as in §3 because the build behaves differently, a rule conflict, scope you think is missing). Everything else: decide, note in TRAINING_NOTES, continue. Message the owner only at the gates in §9.

---

## 9. Phases, gates and reporting

| Phase | Work | Done when (gate) | Report |
|---|---|---|---|
| **P0 setup** (½ day) | fetch; worktree + branch; junction; `npm test` baseline; `06 Training` skeleton from the skill templates; `.gitignore`; driver per §7 with chapter 0 steps; `npm test` + tsc green; production build free of the driver | capture of chapter 0 → 10 PNGs, 0 fails, 0 nohl; 3 PNGs eyeballed | Five lines: branch hash, test count, capture result, deviations so far, next |
| **P1 core story** (1 day) | chapters 1–8 scripted + replayed + captured (the guest's arrival to check-out and the cashier's day) | `capture_report.json` clean for 0–8; `coverage.md` rows 0–8 ticked; build_deck on 0–8 renders (`include` steps) and `check.py` passes | Same five lines + slide count; **owner may look at 5 slides here** |
| **P2 all chapters** (1 day) | chapters 9–24 | clean capture 0–24, ≈ 460 shots; coverage.md complete: every page id + feature id ticked or listed under Deviations with a reason | Five lines |
| **P3 deck + English** (½ day) | `content.json` (recaps, quizzes, shortcuts, menu map, demo users), `build_deck.py`, `render_slides.py`, proof-read the generated English narration (fix wording in steps.json, rebuild), `check.py --render` | check PASS; 5 rendered slides eyeballed; English notes read end to end once | Slide count, deck size, known gaps |
| **P4 Hinglish** (½–1 day) | Hinglish md via Sonnet sub-agents (one wave: 5 agents × 5 chapters), glossary in config, `check.py` Hinglish rules, `notes_to_pptx.py --lang hi` | check PASS for every block; Devanagari share and glossary rules met | Five lines |
| **P5 media** (≈ 4 h machine time, background) | `tts.py --lang en`, `encode.py --lang en`, then `--lang hi` (never together), `check.py --media`; spot-check 3 audio files per language by duration | media check PASS; durations table | Durations, sizes |
| **P6 phone deck** (½ day) | `hcc-phone` config + 8 chapters on the mobile shell; capture, deck, English narration, Hinglish, media (same steps as P3–P5, smaller) | checks PASS | Five lines |
| **P7 wrap** (½ day) | role cards (10 PDFs, reportlab, Manual System style with HCC maroon #7A1F2B), trainer's guide .docx, README (role tracks, deviations, rebuild commands, spot-check), TRAINING_NOTES, copy to `Builds\<date>\training-hcc\` + SHA256, HANDOFF.md session block, CLAUDE.md status line (one sentence), commit + push branch | files exist and open; `README` lists every deliverable with sizes/durations | Final report (§10 format) |

If the owner says "stop after P1", P1 alone is a usable mini-course (front desk + cashier, English).

---

## 10. Final report format (to the owner, in chat and in README "Spot-check")

- Branch and hash; test counts (prototype before/after; production build clean of the driver).
- Chapter table with steps, slides, English and Hinglish durations per chapter; totals; file sizes.
- Coverage: pages 39/39, features 60/60 (or the list of misses with reasons).
- Deviations list (README).
- Defects in the build found while scripting (TRAINING_NOTES) — these go to the next HCC fix wave, not fixed here.
- Where everything is (full Windows paths), how to rebuild after a UI change (one command list), what is owed by the owner (§11).

---

## 11. Owner questions — **answered 7 Nov 2026: "Defaults stand"** (all six defaults below are the owner's rulings; T1–T10 in §2 apply as written)

| # | Question | Default |
|---|---|---|
| Q1 | Capture from the fictional 24-room demo hotel now (T1), or wait for a Hotel Parth fixture (34 rooms, 5 % rate card)? | Demo hotel now; re-capture later in one run |
| Q2 | Per-module chapters with role tracks (T2), or one deck per role as in the Manual System? | Per module + role tracks |
| Q3 | Phone deck from the mobile shell in Chrome (T5), or also an APK/emulator capture? | Chrome mobile shell; APK capture only if asked |
| Q4 | Separate graded manager exam (Lodge pattern)? | No; chapter quizzes + role-card checks |
| Q5 | Marathi? | No (English + Hinglish, as before) |
| Q6 | Narration voices: keep Zira (en) and Swara (hi)? | Keep |

---

*Planner: Fable 5.1, 7 Nov 2026. Facts read from `E:\Projects\HCC\Hotel Control Centre` at 91024d16 / origin/main 3a0c2d0e and the training-media skill. Next free DEC number is not consumed by this plan (no owner ruling is recorded here until the owner answers §11).*
