# HCC — Pending inventory, the owner's new requests, suggestions and the next three months

**Date:** 2 November 2026 · **Prepared by:** planning lead (Fable) · **For:** Sagar (owner) · **Status:** planning document, read-only on code; nothing here is built or decided until the owner says so.

**How to read this.** Plain language first, technical detail in short sub-bullets. Every status claim names its source file. Status tags: **DONE** (on `main`), **PARTIAL** (built on a branch or half-built), **PLANNED** (spec or contract exists, no code), **NOT STARTED** (idea only), **BLOCKED-ON-OWNER**, **BLOCKED-ON-CA**, **BLOCKED-ON-HARDWARE**. Hours are **human-equivalent hours, always GUESS**, as ranges; agent wall-clock on this PC is usually much shorter but waits on the one-heavy-job rule and on decisions. "DEC-nnn" = a ruling in `Hotel Control Centre\02 Planning\HCC_Decision_Register.md`. "OP-nn" = an open point in `HCC_Build_Plan_v2_2026-10-27.md`. "OQ-n" = a question in a wave contract. Paths are under `E:\Projects\HCC\` unless written in full.

---

## 0. Top 10 next items (one page)

| # | Item | Why now | Needs from the owner | Effort (GUESS) | Section |
|---|---|---|---|---|---|
| 1 | **Gate FIT and fast-forward `main`** (fit check already PASS 0/5,554 on `claude/fit`) | Every later screen builds on the FIT components; 119 commits waiting | A "go" and ~1 h of PC time for the gate | 4–8 h | §2a |
| 2 | **APK v19** (phone + tablet emulators, tablet test prompt) | Owner ruled v19 only after FIT + ACC-3 (HANDOFF 13m); ACC-3 is on main | Run the tablet prompt on the Tab S7 afterwards | 20–40 h | §2a |
| 3 | **LF legacy parity + `HccDocuments`** (A1–A8, B1–B7, G1) | Daily-desk gaps vs the old Lodge software (rate change, bill correction, Form C, counter bill, 24-hour stay); image size risk | Nothing new (OQ-1…10 answered, DEC-201) | 60–120 h | §2a |
| 4 | **Front-desk key session** (real BIS adapter mapping) | Keys module is mock-only on main; a one-hour session at the desk unblocks it | 1–2 h at the front-desk PC; answers Q1–Q6 of the Key Agent spec | 8–20 h after the session | §2a, §2c |
| 5 | **Tariff card menu** (owner request 3.1) | Small delta on existing masters; gives one printable, GST-inclusive card (DEC-198) | Answers to §3.1 questions | 30–60 h | §3.1 |
| 6 | **ACC-4** (year lock, bank reconciliation, ACC-3 carry-overs) | Closes the accounting series; bank reconciliation is the owner's monthly reality | Bank CSV sample; CA items in §2c | 40–80 h | §2a |
| 7 | **HB hall booking spec + build with Quotation** (owner request 3.2) | Hall business today runs on paper; quotation, advances and T&C are the owner's ask | Approve the spec (OP-17) and answer §3.2 questions | spec 8–16 h; build 100–200 h | §3.2 |
| 8 | **Revenue by room category and tender** (owner request 3.3) | One report answering "how much, which rooms, cash or UPI" with a reconciliation to the books | Confirm the column set in §3.3 | 16–32 h (standalone), 8–16 h inside W7 | §3.3 |
| 9 | **W7 report module** (engine + the P1 planned reports) | 48 planned + 13 partial reports; Excel/PDF with one renderer; the Lodge day-end and GST reports the staff use monthly | OP-13 calculation engine status; approval of day-end / registration-card layouts | 330–660 h (all) · P1 slice 120–240 h | §2a |
| 10 | **Internet ON/OFF switch** (owner request 3.4) + go-live stand-ins (W8) | Build the egress gate *before* the first real adapter so every W8 integration is born behind it | Answers to §3.4 questions; choose providers (OP-15) | gate 24–48 h; W8 120–240 h | §3.4, §2b |

Parallelisable now: items 1 → 2 are serial; 3 waits for 1; the HB **spec** (7) and the Tariff-card **design** (5) can be written while 1–3 run, because they are Fable-only and need no PC heavy job.

---

## 1. Where HCC stands (2 Nov 2026)

1. `main` = **8a377f32 / bc856701** (CLAUDE.md status), **pushed to GitHub** `sagarbora91/HCC` (public, DEC-209); `git log main -5` confirms (checked today).
2. On main: prototype + production server Waves 1–4, audit fix waves A–D, masters MA-0…11, customisation Part 1 + themes, SCC payroll / leave / compliance / expenses ported in full, **ACC-1, ACC-2, ACC-3** (chart, parties, vouchers, GST set-off, TDS, P&L, ageing, Tally XML, exports), **W1a LG** legacy data (ten FYs, dry run all MATCH), **DEC-193 party IDs**, **KEYS** room-keys module (mock encoder) — `HANDOFF.md` 13l…13p.
3. Tests on main: prototype **2190/2190**, server **3733 in 37 projects**, 0 warnings; **SCHEMA 12**; migrations up to **4721** (KEYS-DONE.md, HANDOFF 13p).
4. **FIT** (fit-to-screen) is **built and integrated on `claude/fit`** (119 commits ahead of main, `FIT-DONE.md` in `worktrees\fit\05 Delivery Evidence\Fit\`, full fit check PASS; FIT-2 follow-ups committed 2 Nov 12:46) but **not gated and not on main**; 2 files uncommitted in that worktree. HANDOFF 13p still says "Next: FIT".
5. **LF** is planned (contract + briefs on `claude/lf`, DEC-200/201), no lane code. **RPT** has the catalogue only (`claude/rpt`, 130 reports, DEC-202). **HB** (hall booking) has no spec file yet (`02 Planning\modules\` has 13…26 only).
6. **APK**: latest tested is v17 (emulator); no v18 by ruling; v19 waits for FIT (HANDOFF 13m). The v17 physical-tab results never arrived.
7. Legacy Lodge replica demo + training media are complete and packaged (`/training-media` skill); APK 1.2026.11.01-1429 **not** on the Tab S7 (owner: "Not now").
8. Everything that moves bytes to the outside world (WhatsApp/SMS/e-mail, OCR, channel manager, payment gateway, e-invoice) is **simulated or NotConfigured** in Production (`08 Production\ETP_Reuse_Pending.md` spots 29, 34–38).
9. Knowledge graph refreshed 1 Nov morning (47,284 nodes); the 1 Nov afternoon update was refused by the shrink guard and restored; `GRAPH_REPORT.md` dated 2 Nov shows 49,455 nodes (HANDOFF 13o/13p).
10. The old Lodge software still runs the hotel; HCC is not live. Cut-over is an owner evening in W9 (DEC-164).

---

## 2. Pending inventory

### 2a. In-flight waves

| Item | What it is | Status | Depends on | Owner / CA input | Effort (GUESS) | Source |
|---|---|---|---|---|---|---|
| **W2a FIT** fit-to-screen | No page scroll on desktop/tablet, sub-screens as tabs, h1 22 px (DEC-194…196) | **PARTIAL** — integrated on `claude/fit`, check PASS 0/5,554, FIT-DONE written; gate + fast-forward + HANDOFF not done; 2 uncommitted files | nothing | "go" for the gate | 4–8 h (gate, docs, graph) | `worktrees\fit` git log; `FIT-DONE.md`; HANDOFF 13n |
| **APK v19** | versionCode 19 on FIT + ACC-3 + KEYS; emulator phone + tablet; tablet prompt | **PLANNED** (ruling: after FIT) | FIT on main | Run the Tab S7 prompt; return results | 20–40 h | HANDOFF 13m "APK, owner ruling 1 Nov" |
| **W2b LF** legacy parity + documents DB | A1 rate change, A2 bill correction, A3 purpose, A4 vehicle, A5 Form C, A6 24-hour stay, A7 counter bill, A8 F-keys, B1–B7, G1 image compression, `HccDocuments` second database, migrations 4670–4719 + `migrations-docs/` | **PLANNED** (contract + briefs on `claude/lf`; 9 lanes) | FIT on main | None blocking (OQ-1…10 answered DEC-201); CA: s.34(2) window, half-day slab | 60–120 h | `02 Planning\agents\LF-CONTRACT.md` §14, `LF_Briefs.md`; `HCC_Legacy_Feature_Gap_v1.md` |
| **W4 ACC-4** | Year lock + opening carry-forward, bank reconciliation with statement import, UPI/card clearing (B8), ACC-3 carry-overs (vendor-return allocation, GST tie vs set-off journal, server ageing source) | **PLANNED** (rulebook §10; migrations 4280–4299) | ACC-3 (done) | Bank statement CSV layout; CA: cash-ratio definition, asset disposal F4, TDS sub-sections, B11 ₹20,000 | 40–80 h | Rulebook §10; `ACC3-DONE.md` §7; DEC-206/207 |
| **W5 HB hall booking (module 27)** | Halls master, enquiry, quotation, advance, menus, event-day bill → invoice → accounting 4400 | **NOT STARTED** (spec to be written by Fable; owner approval OP-17; migrations 4300–4349) | ACC-3; W7 renderer for PDF (or interim HTML print) | Approve spec; §3.2 questions | spec 8–16 h; build 100–200 h | Build Plan §3 W5; DEC-151 |
| **W6 SF split folio** | Several folios per stay with a payer and routing rule; invoice per folio | **PLANNED** (migrations 4350–4369) | HB optional; ACC-3 | none | 40–80 h | Build Plan §3 W6; DEC-152 |
| **W7 RPT report module** | Catalogue + one renderer (screen/Excel/PDF) on ETP OpenXML + PDFsharp; 48 planned + 13 partial reports; scheduled runs | **PARTIAL** (catalogue v1 done, DEC-202; no architecture file, no engine) | ACC-3 done; messaging for delivery (W8) | OP-13 (calculation engine: owner said ≈3 Nov); approve day-end / registration-card layouts | engine 80–160 h; reports 250–500 h; P1 slice 120–240 h | `HCC_Report_Catalogue_v1.md` §2, §6; Build Plan W7; Lodge sizing §5.2 |
| **W8 GL go-live stand-ins** | Close the 38-spot ETP-reuse list; real adapters behind switches (messaging, OCR, channel manager); optional Form C portal file, PF ECR/ESIC | **PLANNED** (migrations 4500–4599) | Providers chosen; §3.4 egress gate first | OP-14, OP-15 | 120–240 h | `ETP_Reuse_Pending.md`; Build Plan W8 |
| **W9 FINAL release pipeline** | Inno Setup installer with SQL Express bootstrap, gate chain, keys session, VM stage, hotel-PC rehearsal, restore drill, staff trial, cut-over evening | **PLANNED** | W8 | Owner sessions (gate, keys, rehearsal, cut-over) | 40–80 h build + 20–40 h owner time | Build Plan W9; DEC-087, DEC-164 |
| **ETP gate per wave** | `tools\Start-Gate.ps1` chain: both suites from a clean clone, goldens, lock check, secret scan, e2e | **DONE** (used for ACC-1…3, LG) | — | — | — | HANDOFF 13l; Build Plan §1 |
| KEYS real adapter | Map Speed Check-in / check-out / read in the Be-Tech BIS 5.7 UI; poller on the front-desk PC; encoder registration screen | **PARTIAL / BLOCKED-ON-HARDWARE** (mock encoder only on main) | Front-desk session | Q1–Q6 of the spec; 36th room; room 106 truth | 8–20 h after the session | `02 Planning\HCC_KeyAgent_Spec_v1.md` §8–9; `agents\KEYS-DONE.md` §5–6 |

### 2b. Integration and go-live items

| Item | What it is | Status | Depends on | Owner / CA input | Effort (GUESS) | Source |
|---|---|---|---|---|---|---|
| Channel manager / OTA | Real ARI push + reservation pull behind the OTA port (today scripted import, pushes only logged) | **PLANNED** (W8 gl-channel) | Vendor API, egress gate (§3.4) | OP-15: which channel manager; contract | 40–80 h + certification | `ETP_Reuse_Pending.md` #36; Module 21/22 spec |
| Payment gateway / UPI link | Signed deposit link + callback for web/quote advances (today staff mark "paid", DEC-102) | **PLANNED** (W8, spot 35) | Website on (DEC-085: off at go-live, reverse proxy + CAPTCHA) | Which PSP; who holds the merchant account | 24–48 h | `ETP_Reuse_Pending.md` #35; DEC-085, DEC-102 |
| WhatsApp / SMS / e-mail | Real `IMessageGateway` adapter; outbox worker exists; Production refuses sends (503 NOT_CONFIGURED) | **PLANNED** (W8 gl-messaging) | Provider + DLT registration for SMS (verify), WhatsApp Business API account | OP-15 | 24–48 h | `ETP_Reuse_Pending.md` #29, #33; Module 24/25 spec |
| OCR for ID | Real `IIdOcrEngine` (today simulated / NotConfigured) | **PLANNED** (W8 gl-ocr) | Provider; lawyer on ID images (DEC-068/071) | OP-15 | 16–32 h | `ETP_Reuse_Pending.md` #34 |
| Tally export / import proof | XML masters + vouchers built (ACC-3); owner's import of golden files into a fresh TallyPrime company **not done** | **DONE** (export) / **BLOCKED-ON-OWNER** (OP-22 proof) | — | Import `tally-april-2026.*.xml`; open the workbooks in Excel | 1–2 h owner | Build Plan OP-22; `ACC3-DONE.md` §7 |
| Tally **import** (bank/CA entries back into HCC) | Not planned anywhere; HCC → Tally one way by DEC-140 | **NOT STARTED** | — | Does the owner want it at all? | 24–48 h if wanted | DEC-140 |
| PDF / Excel engine | Prototype `xlsx.ts` writer + server OpenXML exporter exist (ACC-3 exports); PDF only as HTML print; ETP PDFsharp exporter not yet wired | **PARTIAL** | W7 rpt-engine | — | in W7 engine | `ETP_Reuse_Pending.md` #9, #19, #22, #38 |
| Invoice PDF with logo / QR / e-invoice | `SimpleInvoiceRenderer` = HTML/text; no IRN (hotel below ₹5 cr by rulebook §1 — **CA to confirm** threshold applies) | **PARTIAL** | W7 | CA: e-invoicing applicability | 8–16 h (PDF) ; IRN adapter 24–48 h if ever | `ETP_Reuse_Pending.md` #38; Rulebook §1 |
| Lodge ID-proof patch (old software) | Stage 0 (shared folder + backup, 4–8 h), minimal patch (45–70 h), full (81–141 h); scripts drafted, **not executed** | **PLANNED** / **BLOCKED-ON-OWNER** | Owner decision whether Lodge runs > 6 months | Open questions 1–6 of the patch doc (live folders, PC layout, login, ID codes, lawyer) | see left | `02 Planning\HCC_Lodge_ID_Storage_Analysis_and_Patch_Design.md`; `HCC_Lodge_Rebuild_Sizing.md` §1 |
| Legacy data import at go-live (cut-over evening) | Extractor + importer built and dry-run MATCH; live PC `SupportFile` images must be present; in-house guests re-entered; opening entry approved | **DONE** (tooling) / **PLANNED** (the evening, W9) | W9 | Owner evening; review the 20-guest sample; 397 duplicate-ID groups after go-live | 4–8 h evening | HANDOFF 13m; DEC-164, DEC-184, DEC-199 |
| Physical device tests | Tab S7 with HCC v19; APK release-signed with the hotel CA | **BLOCKED-ON-OWNER** (v17 results never came; v19 replaces) | APK v19 | Run the prompt; return screenshots | 2–4 h owner | HANDOFF 13m; `Acceptance\APK_v17_Tablet_Test_Prompt.md` |
| Backups / restore drill | Scripts exist (`Backup-HccDatabase.ps1`, `Restore-HccDrill.ps1`, Task Scheduler); two-database drill added by LF; real drill on the hotel PC not run | **PARTIAL** | LF, W9 | Backup drive, BitLocker (DEC-087) | 8–16 h + owner session | `ETP_Reuse_Pending.md` #12–14; LF-CONTRACT §1.9 |
| Release keystore / hotel CA / key USBs | One-hour guided keys session on the hotel PC | **BLOCKED-ON-OWNER** | W9 | The session | 1–2 h owner + 4 h prep | DEC-087 |
| GST / e-invoice / CA confirmations | Room GST 18 % vs code default 5 %; s.34(2) window; half-day slab; TDS rates; cash-limit sections; cash-ratio; asset disposal; composite meal-plan supply; GST on advances risk (DEC-142) | **BLOCKED-ON-CA** | — | CA meeting with `05 Delivery Evidence\CA_Question_Pack_2026-10-25.md` + the items in §2c | 0 code until answers; 8–40 h after | §2c |
| Form C / FRRO | Fields + register + CSV in LF (A5); portal layout in W8 when the owner hands a sample | **PLANNED** | LF | A sample of the hotel's current FRRO upload (OQ-4) | 8–16 h (W8 part) | LF-CONTRACT OQ-4 |
| Website (direct booking) | Built, simulated; **off at go-live** | **DONE** (simulated) / **PLANNED** (exposure) | Reverse proxy, CAPTCHA, gateway | Decide when to expose | 16–32 h | DEC-085; Module 21/22 spec |

### 2c. Owner and CA decisions that block work (deduplicated)

**Owner — blocks a build step**

| # | Question | Blocks | Source |
|---|---|---|---|
| O-1 | OP-17: approve the hall-booking spec once written (and the quotation design of §3.2) | W5 HB | Build Plan §5 |
| O-2 | OP-15: providers for WhatsApp/SMS/e-mail, ID OCR, channel manager (and payment gateway); who signs contracts | W8 | Build Plan §5 |
| O-3 | OP-14: Form C FRRO file and PF ECR / ESIC files — build or leave (DEC-202 #6 says not at go-live) | W8 optional lanes | Build Plan §5; DEC-202 |
| O-4 | OP-13: the Excel-like calculation engine — delivered? where? | W7 computed columns | Build Plan §5 |
| O-5 | OP-21: do children / infants need an ID at check-in (built as "no") | nothing; confirms a constant | Build Plan §5; `ACC3-DONE.md` §7 |
| O-6 | OP-22: import the golden Tally files into a fresh TallyPrime company; open the exported workbooks | ACC-3 acceptance proof | Build Plan §5 |
| O-7 | Keys Q1–Q6: operator login for the agent, BTReg57.DLL elevation, the 36th room without a BIS door, room 106 truth, 12:00 departure acceptable, English UI, unlocked desk session | KEYS real adapter | `HCC_KeyAgent_Spec_v1.md` §9; `KEYS-DONE.md` §6 |
| O-8 | Keys: may the panel combine Extended + Reactivated; offer the key panel after check-in automatically? | KEYS polish | `KEYS-DONE.md` §6 |
| O-9 | Tab S7: run the APK v19 tablet prompt and return results | APK v19 close-out | HANDOFF 13m |
| O-10 | Lodge ID patch: do Stage 0? minimal or full? live `IDProof`/`Photo` folders and one live yearly file for the test; how reception PCs run Lodge | Lodge patch | ID patch doc §1 Q1–2, sizing §7 |
| O-11 | Report-use log on the Lodge PC for 30 days (so "used reports" rests on facts) | W7 scope trimming | Crystal map §8; sizing §7 |
| O-12 | Day-end and registration-card layouts for W7 (approve a mock-up) | W7 | sizing §5.3 |
| O-13 | Decision Register §3b DEC-096…110 "Proposed (agent rule)" rows — yes/no per row (folio at check-in, downgrade approval, OTA party-size refusal, CRM no un-merge, cash book no export, group master folio no cash, booking window 180 days, cash-book lock permission, quote lifetime 30 min) | none blocking; change requests if "no" | Decision Register §3b |
| O-14 | DEC-MA-11c: retire Wave B's holiday pair later? DEC-MA-101/102, MA-60/61 "owner to confirm" rows | none blocking | Decision Register §3 |
| O-15 | Customisation plan §6: OS font size handling, start page per staff, Hindi/Marathi UI (large), per-user prefs across devices | Customisation Parts 2–4 (optional for go-live) | `HCC_Customisation_Features_Plan_v1.md` §6 |
| O-16 | Audit D-items still open in code: D4 who records cash out of the drawer (built: Banking, transfers, drawings — confirm the second-person rule), D6 past-attendance correction rule, D7 Manager company credit limit ₹10,000 direct or Owner-only | small code changes | Audit §6 |
| O-17 | Installer choices: lock install folder, merge settings on upgrade, auto-clean a plain `.bak`; passphrase for the offline key copy; "hotel CA only" network config | W9 | HANDOFF 13e/13f "waiting on the owner" |
| O-18 | Legacy: room 211's type; review the 20 masked guests in `LG-DryRun-2026-11-01.md`; the 397 duplicate-ID groups after go-live (DEC-199 keeps them) | cut-over quality | HANDOFF 13m |
| O-19 | Bank statement CSV layout of the hotel's bank (for ACC-4 import) | ACC-4 bank lane | Rulebook §10 |
| O-20 | Website exposure timing and reverse proxy / CAPTCHA hosting | W8/W9 | DEC-085 |
| O-21 | The four new requests of 2 Nov: answers to the questions in §3.1–§3.4 | Tariff card, Quotation, Revenue report, Internet switch | this document |

**CA — confirms a rule (code waits where marked)**

| # | Question | Code impact | Source |
|---|---|---|---|
| C-1 | Room GST **18 %** on all rooms (owner) vs the law's slab (≤ ₹7,500 → 5 % without ITC); HCC default stays 5 %, Owner sets 18 % at setup | setting only | DEC-198, DEC-069 |
| C-2 | No GST on booking advances (DEC-142, against advice); switch exists | setting only; risk recorded | Rulebook OP-3 |
| C-3 | Bundled meal plan = composite supply at the room slab (DEC-MA-2a, MA-11f) | rule in code | Decision Register |
| C-4 | Bill-correction window: refuse after 30 Nov of the next FY (s.34(2)) | message text | LF OQ-2 |
| C-5 | Half-day charge = 50 % of the night rate at the night's slab | rule in code | LF OQ-5 |
| C-6 | TDS rates, rupee rounding, catch-up on crossing the threshold; sub-section split 194-I(a)/(b), 194J(a)/(b) | dated table + code | DEC-173, DEC-207 |
| C-7 | Cash-limit section numbers under the Income-tax Act 2025 (s.186 etc.), ₹35,000 transport, B11 loan ₹20,000 | guard message text | DEC-174, DEC-207, Rulebook OP-10 |
| C-8 | Cash-ratio definition; asset disposal treatment (F4); 1340 GST cash ledger as a separate asset | report formula | DEC-207/208 |
| C-9 | Full Aadhaar storage (DEC-068) now for party members too; retention (DEC-071) — lawyer | legal risk; switch-back path documented | Audit §6 D1/D2; `ACC3-DONE.md` §7 |
| C-10 | Licence lead times / penalties / authority for FSSAI and lodging licence cards; one ITC ledger vs CGST/SGST/IGST split (ACC-1 built the split) | card texts | HANDOFF 13j |
| C-11 | Payroll constants in code: daily wage × 26, 8-hour day, 15-minute grace, PF/ESIC/PT wage bases, employer PF split, F&F limits, OT 1× | code if the CA differs | Audit §6; CA pack |
| C-12 | E-invoicing applicability (rulebook: not below ₹5 cr); SAC codes; B2CL threshold | code if applicable | Rulebook §1; Audit §6 |
| C-13 | Two invoice series in FY 2026-27 (legacy 1…1908 and `HPE/2627/0001`) in GSTR-1 Table 13 | report (LF B7) | DEC-197 |

### 2d. Known defects and low items

| Item | Severity | Status | Source |
|---|---|---|---|
| V17-1 window insets lost until a rotation (modal footer clipped on the tablet AVD) | S3 | **OPEN** — confirm on the physical tab; FIT may have changed the footer | `Acceptance\APK_v17_Emulator_Report.md`; CLAUDE.md |
| V17-2…4, V15-4, V16-3 (status-bar colour read 1 s after rotation; app-bar date line wraps "Open"; cosmetic) | S4 | **OPEN** — fix in APK v19 lane | APK v17 report |
| Order-dependent `TallyFileSqlTests` | fixed 2 Nov | **DONE** | HANDOFF 13p |
| ACC-2 leftovers: GST month view does not show the pay-run blocker; dated TDS rates display-only | S4 | **OPEN** → ACC-4 | HANDOFF 13m |
| `EXP_VENDOR_PAY_VOID` Owner-only — confirm | decision | **OPEN** | DEC-175, DEC-207 |
| Audit "later" list: outbox purge / poison-event handling done in Wave C; nightly ID-image purge **not** built (DEC-071 keeps images) | by ruling | **CLOSED by DEC-071** | Audit §7 step 5 |
| Audit first-month items: role-matrix v2 D1–D7 rulings → DEC-079 settled; CA-driven rule changes (A04-04, A04-08…10, A03a-03, A03a-06) ≈ 15 h once answered | — | **BLOCKED-ON-CA** | Audit §7 |
| FIT lane notes to re-check at integration: Booking-site tabs vs main, yield publish-requests sub-screen, expenses register body ~168 px at tab-land, `HeaderActions` overflow with 6 items | S4 | **OPEN** (listed in `coordinator-notes.md`; FIT-DONE says review blockers resolved) | `worktrees\fit\05 Delivery Evidence\Fit\coordinator-notes.md` |
| No encoder registration screen (API only) | S4 | **OPEN** | `KEYS-DONE.md` §6 |
| Lodge demo leftovers (camera permission dialog, physical tablet run, two reworded course steps in deck/video) | demo only | **OPEN** | HANDOFF "Legacy Lodge HTML demo - integration 1-Nov" |

Counts: **in-flight waves 11**, **integration items 16**, **owner questions 21 groups**, **CA questions 13**, **defects/low 10** → about **71 pending items**, of which **21 are blocked on the owner** and **13 on the CA**.

---

## 3. The owner's new requests (2 Nov 2026) — mini-specs

### 3.1 Tariff card

**Purpose.** One menu where the Owner (and, read-only, the Manager and Reception) sees and maintains the hotel's complete price list — room categories by plan, extra person, meal plans, halls — dated from an effective date, shown **GST-inclusive as the hotel quotes it (DEC-198)**, printable as the card that hangs at the desk and goes to agents and the booking website.

**What already exists (so only the delta is proposed).**
- Room-type **rack rate = the dated tariff schedule** in Master configuration › Rooms (`s.tariffs`, `TARIFF_SCHEDULE`, Owner-only, DEC-MA-102; `features/admin/HotelSettings.tsx` "Schedule tariff"). Server `inv.room_type_tariff`.
- **Occupancy charges** master (extra adult / child / bed per type, dated) and **meal plans** (EP/CP/MAP/AP, per guest per night, bundled vs separate) and **extras catalogue** (MA-1, MA-2; DEC-MA-2a/2b).
- **Yield module 13**: seasons, day-of-week %, occupancy tiers, rate plans (BAR, Corporate, Weekend, Long-stay, Day-use), restrictions, **publish** into `s.tariffs`, history (`modules\HCC_Module_13_Tariff_Yield_Spec.md`).
- **Rate card DEC-198** as CSV import files in `Existing Software\DB-PROFILE\rate-card\` (GST-inclusive card ÷ 1.18 = rack).
- Discount rules: role limits (DEC-063: Reception ≤ ₹250, Manager ≤ ₹1,000, Owner no limit) and discount reasons with approval % (DEC-MA-2b); company negotiated rates (module 14); regular-guest agreed rate (LF B1).
- Report `rpt.admin.rate-sheet` (partial, P2) = rate sheet, publish log, change history; legacy `C7.RoomTariffCard` mapped to it.
- Halls: **nothing yet** (W5).

**Delta to build (the "Tariff card" page).**
- *Menu:* REVENUE & SALES › **Tariff card** (desktop page with sub-screens per DEC-196: **Rooms** · **Meal plans & extras** · **Halls & lawns** (after W5) · **Seasons & weekends** · **History** · **Print**); phone: cards, read-only except the Owner.
- *Roles:* edit = Owner only (money, DEC-058); Manager may **propose** a card version through the existing approvals mechanism (as yield proposals do); Reception / Cashier view; Housekeeping / Maintenance never see it (DEC-061).
- *Data model (sketch):* `tariff_card_version` (id, effective_from, label e.g. "Season 2026-27", status draft/published/superseded, created_by, approved_by, notes) + `tariff_card_line` (version_id, kind room/occupancy/meal/extra/hall, room_type or hall id, plan EP/CP/MAP/AP, weekday_set weekday/weekend/all, **rate_excl_paise** (stored), gst_bps snapshot, **display_incl_paise** (derived), min/max guests). Publishing a version **writes the existing masters** (tariff schedule rows, occupancy charges, meal-plan prices) with that effective date — the card is a view and a versioned snapshot, not a second price list (keeps DEC-MA-102). Server: one migration block (suggest 4380–4389), `mst.tariff_card_version/_line`; prototype slice `s.tariffCardX`, SCHEMA bump.
- *Rules:* integer paise; GST from the dated GST master (DEC-069) at the effective date; inclusive display = excl × (1 + bps/10,000) rounded half away from zero; entering an inclusive figure stores the exclusive paise and shows the rounding difference; a version cannot be published with an effective date before the business date; a published version is never edited (new version); weekend = Fri/Sat/Sun nights by default, Owner-editable (yield's DOW table is reused — a weekday/weekend pair becomes two published tariff rows or one yield DOW rule; recommend **published rows** so bookings pick them without yield logic).
- *Reports / print:* `rpt.admin.rate-sheet` extended to render the published card (A4, hotel letterhead, "Prices include GST @ 18 %" line per DEC-198, validity line, T&C footer); PDF through the W7 renderer, HTML print until then; CSV export; history of versions with who/when.
- *Links:* booking rate picker already reads `s.tariffs` (no change); web booking site preview reads the published card; quotations (§3.2) pick hall and room rates from the card; OTA channel mapping (module 21) shows card vs channel rate.
- *Open questions for the owner:* (1) Weekend = Fri/Sat/Sun or Sat/Sun? (2) Seasons: do you want dated seasonal cards (e.g. Diwali, wedding season) or one card a year? (3) Child rate band (age 5–12?) and child meal price? (4) Should the Manager be allowed to publish a card (today Owner-only)? (5) Hourly / day-use room rate on the card? (6) Should the card print in Marathi/Hindi as well (the Lodge has Hindi labels on documents)?
- *Acceptance tests:* publish a version → new booking on/after the date takes the new rate, earlier bookings unchanged; inclusive/exclusive round-trip at 5 % and 18 % (DEC-069 dated change on 22 Sep 2025 reproduces the Lodge's 5 % history); Manager proposal → Owner approve; print matches the screen; Housekeeping gets `FORBIDDEN`; CSV import of the DEC-198 files produces the same card.
- *Wave / effort:* small Opus lane (money) + Sonnet UI lane, after FIT, before or beside LF: **30–60 h GUESS** (hall rows +8–16 h after W5).

### 3.2 Quotation with terms and conditions (weddings, halls)

**Purpose.** Turn a hall or wedding enquiry into a priced, numbered, versioned quotation with the hotel's terms, send it as PDF (and later WhatsApp/e-mail), chase it until it is accepted or expires, and on acceptance create the hall booking (and room block) and take the advance.

**Where it sits.** This is the "quotation" step of **W5 HB hall booking** (Build Plan §3 W5, DEC-151) and should be designed inside the Module 27 spec rather than as a separate module; rooms for wedding guests use module 14 group blocks.

**Users / roles.** Reception / Manager: create, price from the tariff card, send; discounts within DEC-063 limits (Reception ≤ ₹250 is too small for halls — **owner to set a hall discount limit per role**, suggested Reception 0, Manager up to 10 % of the quote, Owner any); Manager: confirm acceptance and take the advance; Owner: approve beyond limits, edit T&C templates and the advance schedule defaults (money/terms = Owner-only, DEC-058). Accountant: read. Housekeeping / Maintenance: nothing.

**Screens / menu.** REVENUE & SALES › **Hall booking** page, sub-screens **Enquiries** · **Quotations** · **Calendar** · **Events** · **Menus** · **Templates** (Owner). Quotation editor: header (party, event type, dates, slots, pax, venue), lines table, totals with GST, advance schedule, T&C preview, version strip (v1, v2 …), status pill, actions Send / Revise / Accept / Mark lost / Print.

**Data model (sketch; migrations 4300–4349 as allotted).**
- `hall.venue` (id, name, kind hall/lawn/combined, capacity by setup, GST class 18 % service default per DEC-MA-4a, slots morning/evening/full-day, hourly/day rates via the tariff card).
- `hall.enquiry` (id, party/customer id, phone, event_type wedding/reception/engagement/birthday/corporate/other, event_date(s), slot, pax, source, status new/quoted/won/lost, follow_up_on, notes).
- `hall.quotation` (id, number from the **numbering-series master** (new document type QUO, e.g. `QT/2627/0001`), enquiry_id, version, parent_quotation_id, status draft/sent/revised/accepted/expired/lost, valid_until, issued_on, issued_by, discount_paise, discount_reason, approval_id, tnc_template_id, tnc_text_snapshot, totals excl/gst/incl in paise).
- `hall.quotation_line` (quotation_id, kind venue/room_block/catering/decor/extra/other, description, qty, unit per-plate/per-night/per-event/each, rate_paise, gst_bps snapshot, taxable_paise, cgst_paise, sgst_paise).
- `hall.quotation_advance` (quotation_id, due_on or trigger on-acceptance/30-days-before/on-event-day, amount_paise or percent, received_receipt_id).
- `hall.tnc_template` (id, event_type, title, body (numbered clauses), version, active, Owner-edited, effective_from) — clauses such as advance and cancellation slabs, pax guarantee, timing, damages, outside caterers/decorators, liquor/licence responsibility, GST note.
- `hall.event` (on acceptance: venue, dates, pax guarantee, menu, status confirmed/held/completed/cancelled) → event-day bill → GST invoice in the hotel's `HPE` series (DEC-197; **one series** per DEC-MA-5 unless the CA wants a separate hall series).
- Prototype: slice `s.hallX`, stream `hall.ts`, handlers `HALL_*`, SCHEMA bump.

**Rules.**
- GST per line from the dated GST master: venue hire and decor = other services 18 %; catering per plate = restaurant rate (5 %, or 18 % if specified premises — DEC-069/DEC-MA-4a; **CA to confirm** banquet catering treatment, often treated as outdoor catering/18 % — VERIFY); CGST + SGST half away from zero; integer paise.
- Advance on acceptance posts as **Advance deposits (2000)** per rulebook A6; **no GST on advances (DEC-142)** — the quotation shows "advance adjusted against the final invoice".
- Validity default 15 days (Owner setting); on expiry the status becomes expired and a desk reminder (LF B4) fires to Reception; a revision supersedes the parent (parent kept read-only).
- Accept → creates `hall.event` + optional **group block** for rooms (module 14) + advance receipt (any tender from the payment-methods master; Udhaar allowed per DEC-144); the venue is blocked on the calendar (hall availability + room board link).
- Cancellation / attrition after acceptance follows the HB spec (4300 cancellation charges), per the T&C clause chosen.
- Discounts above the role limit raise an approval (existing mechanism).

**Reports / exports / printouts.** Quotation PDF (letterhead, lines, GST summary, advance schedule, T&C, acceptance signature block, validity); **Quotation register** (new catalogue entry, P2); `rpt.guests.hall-enquiry-funnel`, `hall-events`, `hall-occupancy` already planned (P3) — raise funnel to P2; conversion % and lost reasons.

**Send.** PDF saved; "Send" goes through the messaging module outbox (WhatsApp/e-mail) — queued until a real gateway exists (W8) and the internet switch (§3.4) is ON; "Mark as sent by hand" for now.

**Open questions for the owner.** (1) Event types and the T&C text for each — hand over the current paper T&C. (2) Advance pattern today (e.g. 25 % on booking, 50 % 30 days before, balance on the day)? (3) Does the hotel charge GST on the whole package or itemised (venue / food / decor)? CA to confirm. (4) Per-plate menus: built on the POS menu master or free text? (5) Quotation numbering prefix (`QT`) and whether it resets yearly. (6) Who may give what discount on a hall quote? (7) Should a lost quotation ask for a reason list?

**Acceptance tests.** Golden event: enquiry → quote v1 (venue + 150 plates + decor) → v2 with 5 % Manager discount → accept → advance ₹20,000 by UPI → event bill with GST → invoice `HPE/…` with advance adjusted → accounting: 2000 debited, 4400 revenue, GST ledgers; expired quotation raises a reminder; Reception cannot edit a T&C template; PDF totals equal screen totals; same figures in both suites.

**Wave / effort.** W5 HB (Fable spec 8–16 h incl. this section; Opus 3 + Sonnet 3 lanes **100–200 h GUESS** incl. quotation; quotation alone ≈ 40–70 h).

### 3.3 Revenue report by room category with advance / cash / UPI split

**Purpose.** One report that answers, for any day, month or FY: how much room revenue each category earned, how much came in as advance, cash, UPI, card, bank or OTA, and how it ties to the books.

**Existing pieces to extend (catalogue `HCC_Report_Catalogue_v1.md`).** `rpt.money.revenue-tax` (built, P1: room / service / discounts / adjustments / net / GST per business date); `rpt.rooms.room-wise-revenue` (planned, P2: nights sold and tariff per room / room type / floor); `rpt.money.collection-detail` (planned, P1: every receipt by tender / bill / user, receipts / advances / corrections); `rpt.money.daily-collections` and `rpt.money.receipts-deposits` (built); `rpt.money.ota-payouts` (built). **Proposal: one new catalogue entry `rpt.money.revenue-by-category-tender` ("Revenue by room category and tender"), P1**, built as a view over the same selectors, so the figures equal the existing reports.

**Columns (two blocks on one page, both for the same date range).**
- *Block A — Revenue (accrual, business date of the posted line):* Room category · Rooms in category · Nights sold · Occupancy % · Room tariff (excl.) · Extra person / bed · Meal plans · Other room charges (service, F&B to folio) · Discounts · Corrections / credit notes · **Net revenue (excl. GST)** · CGST · SGST · **Gross (incl. GST)** · ADR. Rows per category + Total; a "Halls / counter bills / restaurant" row set below the room rows so the grand total equals `rpt.money.revenue-tax`.
- *Block B — Money received (receipt date, by tender from the payment-methods master):* Cash · UPI · Card · Bank transfer · Company credit (receivable raised) · Udhaar (receivable raised) · OTA (settled by the OTA, `ota-credit`) · **Advances received** (deposit receipts) · **Advances adjusted** (deposit-apply at check-out) · **Advances refunded** (deposit-return) · Refunds · **Net received**. Split by room category of the folio's stay (an advance before check-in takes the booked category; counter / hall receipts in their own rows).
- *Block C — Bridge (why A ≠ B):* Opening balance due + Gross revenue − Net received ± advance movement = Closing balance due (company + Udhaar receivables) and Closing advances held. This is what reconciles to the trial balance (ledgers 1000 cash, 1010 UPI, 1020 card, bank, 1110 company receivables, 1120 Udhaar, 2000 advance deposits, 4000-series revenue, GST output ledgers) and to the **day book / closing record** for a single day.

**Date basis and presets.** Business date (as every HCC report, D-F range class, up to 366 days, FY/month/day presets; DEC-202 #5 allows legacy FY history on stay and receipt reports — the legacy bills carry room number, so category comes from the rooms master: mark "legacy" in the header, **GUESS** that every legacy room maps). Toggle "stay basis" (revenue by the stay's arrival date, for marketing) as a secondary option.

**Drill-downs.** Category → stays → folio lines; tender cell → receipts (reuse `rpt.money.source-rows`); advance cells → the deposit receipts.

**Roles (DEC-202).** Owner, Accountant, Manager (it is not P&L / payroll); Cashier own-shift only through the shift report, not this one; Reception no (hotel money totals). Money flag y (PERM-RPT-002). Export Excel / CSV for the export roles; PDF via the W7 renderer; print.

**Open questions for the owner.** (1) Do you want revenue **including** or excluding GST as the headline (DEC-198 quotes inclusive; the books are exclusive)? Suggest both columns. (2) Should restaurant and hall revenue appear on the same page (as rows) or stay in their own reports? (3) Is "advance" to be shown by the day it was received, the day it was adjusted, or both (proposal: both)?

**Acceptance tests.** FY26 fixture: sum of Block A totals = `rpt.money.revenue-tax` totals for the range; sum of Block B = `daily-collections` by tender; Block C closes to the ACC trial balance on the ACC-RULEBOOK golden month; a legacy FY renders with the "legacy" header and equals the LG contract §6 totals for bills and receipts; Reception gets `FORBIDDEN`; Excel equals screen.

**Wave / effort.** Standalone Sonnet lane on existing selectors **16–32 h GUESS** (prototype + server query + exports); 8–16 h if done inside W7 after the engine exists. Recommend standalone, soon, because it is the owner's daily question.

### 3.4 Internet ON / OFF toggle (Owner-only)

**Purpose.** A single switch that, when OFF, guarantees HCC itself sends nothing to and fetches nothing from the internet, while everything on the hotel LAN keeps working.

**What counts as internet traffic in HCC today (measured 2 Nov).**
- Server `08 Production\src`: **no `HttpClient` / `WebClient` usage found**; the only external URLs in source are NuGet (build-time), OpenXML schema namespaces (not fetched), `demo.invalid` placeholders and a GitHub raw link in a reuse record. Messaging and OCR are `NotConfiguredMessageGateway` / `NotConfiguredIdOcrEngine` in Production (503 NOT_CONFIGURED); the OTA channel manager is simulated (pushes only logged); the booking website is **off at go-live** (DEC-085); the web deposit callback is simulated (DEC-102). Tally is a file export. Backups go to a local folder.
- Prototype / web client: single self-contained HTML, **no CDN, no external fonts** (CLAUDE.md rule); talks only to the LAN server over HTTPS with the hotel CA.
- APK: Capacitor WebView loading local assets + the LAN server; plugins app / status-bar / splash / filesystem / share / printer; no analytics. (Android OS itself still updates and syncs — outside HCC.)
- **So today the running software makes zero outbound internet calls.** The risk is the future: W8 adapters (WhatsApp/SMS/e-mail, OCR, channel manager, payment gateway, IRN), scheduled report e-mails, any update check. Build the gate **before** those arrive.

**Design — one egress gate, enforced in three places.**
1. *Server (the real gate).* A single `IEgressPolicy` + one named `HttpClient` ("egress") created only through `IHttpClientFactory` with an `EgressGateHandler` as the outermost `DelegatingHandler`. Every outbound adapter must use that client (an architecture test fails the build if any other `HttpClient`/`WebRequest`/`SmtpClient`/`Socket` to a non-LAN address appears in `src`). The handler reads the dated setting `net.internetEnabled` (Owner-only, PERM-ADM-003 money/tax group style, audited with reason, effective immediately) and the per-integration sub-switches `net.messaging`, `net.ota`, `net.ocr`, `net.payments`, `net.email`, `net.updateCheck`; OFF → the call is refused with `HccError NET_OFFLINE` before any DNS lookup. Outbox items for messaging / OTA push / scheduled e-mail stay **queued with status "held — internet off"** and replay automatically when ON (the outbox worker + retry already exist).
2. *Clients.* Web build: nothing to do (no egress). APK: `server.allowNavigation` limited to the HCC server host; external links open nothing when OFF (the client reads the switch in the config read set and shows a banner "Internet off — messages queue"). Key Agent poller: LAN only (server is on the LAN) — unaffected.
3. *Windows (belt and braces, optional).* `deployment\windows\Set-HccEgress.ps1` creates an **outbound Windows Firewall rule** for the `Hcc.Api` service executable blocking all non-RFC1918 destinations; the installer registers it disabled; the Owner's toggle calls an elevated scheduled task `\HCC\ToggleEgress` that enables/disables the rule (the API itself runs without admin). If the task is unavailable the software gate alone still holds. DNS: with the firewall rule in place DNS to public resolvers is also blocked for the service process; LAN SQL and LAN printers are unaffected (private ranges allowed).

**What degrades gracefully when OFF.** WhatsApp / SMS / e-mail: queued, badge count on Messages, the Owner's daily digest (§4) waits; OTA: rate/inventory pushes held, **booking pull stops — banner "OTA bookings are not being received; check the extranet or switch on"**, the room board shows the time of the last successful sync; OCR: scan button disabled → manual ID entry (already the fallback); web booking: unreachable anyway (DEC-085); payment links: staff record the deposit by hand (DEC-102); scheduled report e-mails held and listed in `rpt.admin.scheduled-runs`; update check (if ever built): skipped. Everything else — bookings, folios, POS, keys, printing, tablets, backups, day close — is LAN and keeps working.

**Security.** Toggle = Owner only (DEC-058 class), with reason, in the audit log and in Configuration history; a **"Scheduled window"** option (e.g. ON 09:00–10:00 daily for OTA sync) is a dated setting the Owner edits; System health page shows the egress state, the held-queue counts and the firewall-rule state; failure modes: the task cannot flip the rule → the software gate still refuses and System health shows "firewall rule not applied"; the setting read fails → **default OFF** (fail closed).

**The test that proves zero outbound calls.** (a) Unit: every adapter's requests pass through the gate (a counting handler records 0 sends when OFF, N when ON). (b) Architecture test: Roslyn/regex scan refuses any HTTP/SMTP/socket type outside `Hcc.Infrastructure.Egress`. (c) Gate step: run the e2e script with `net.internetEnabled=false` while a loopback DNS/HTTP sink (`127.0.0.1` fake resolver via a hosts entry for the adapter hosts) counts connections → must be 0; with the firewall rule on, `Get-NetFirewallRule` state and a blocked probe are logged in the gate report.

**Open questions for the owner.** (1) Default at go-live: OFF (recommended until W8 adapters exist) or ON? (2) Do you want per-integration switches as well as the master switch (recommended yes)? (3) Scheduled windows (e.g. OTA sync twice a day) or purely manual? (4) Should the Manager be allowed to switch ON for a window, or Owner only? (5) Apply the Windows firewall rule too (needs admin once at install) or software gate only? (6) Does "no internet" also mean the hotel PC's Windows Update is managed separately — yes, outside HCC; confirm you accept that.

**Effort.** Server gate + settings + banners + System health + tests **24–48 h GUESS**; firewall script + installer step +8–16 h. Wave: a small Opus lane (settings + server pipeline) **before W8**, ideally beside ACC-4.

---

## 4. Suggestions — other features for an Indian single-property hotel with banquet and restaurant

Priority: **Now** (next 1–2 months, cheap or go-live-relevant), **Next** (after go-live stabilises), **Later**. ★ = quick win (≤ 16 h GUESS). Legal lines say **verify with CA/lawyer**; nothing here asserts law.

**Front desk**
- ★ **Now — No-show / overbooking guards.** Yield restrictions exist advisory only (HANDOFF "open item 3"): enforce `yldCheckStay` in BOOK; an overbooking cap per type (e.g. +1 room) Owner-set; auto no-show marking at a cut-off hour with the policy charge shown (DEC-MA-5b posts nothing automatically — owner decision). 8–16 h. Depends: masters policies.
- **Now — Night-audit checklist.** The day close exists; add a checklist template "Night audit" (checklists module 19) with auto-ticks from data (all arrivals checked in or no-show, all departures checked out, POS checks closed, cash count submitted, keys cancelled for departed rooms, backup OK) and blockers shown before "Close day". 12–24 h.
- **Now — Deposit / advance ledger with refund workflow.** Advance deposits (2000) and deposit-return exist; add a desk view "Advances held" by booking with age, refund request → approval (DEC-031 refund flow) → payment by original tender, and the §3.3 report. 16–32 h.
- ★ **Now — Guest register printable + Form C (statutory).** Planned in LF A5 and catalogue P1 (`rpt.guests.guest-register`, `form-c-register`). Keep at P1. Legal: Form C for foreign guests (FRRO) and the guest register under Maharashtra lodging rules — **verify with lawyer** what the police want (format, 24 h online submission).
- **Next — Corporate / travel-agent accounts with credit limits.** Module 14 stores limits but the audit notes they are **not enforced** (D7); enforce at check-out / credit posting, statements by e-mail (W8), TDS-net receipts exist (A9). 16–32 h. Legal: TDS by the company on room rent (s.194-I) — CA.
- **Next — Walk-in and group check-in on the tablet with camera ID capture** (party IDs built DEC-193; OCR in W8).
- **Later — Guest self check-in / pre-arrival link** (website off at go-live; depends on the gateway and §3.4).

**Guest experience**
- ★ **Now — Guest feedback capture at check-out** (1–5 stars + comment on the tablet, QR on the bill; complaints module 18 already has categories/SLA). 8–16 h. Review-site links later.
- ★ **Now — Lost-and-found and complaint loops**: both exist (module 18, categories DEC-MA-7c); add "owner follow-up call" task and a 7-day unresolved escalation to the Owner digest. 4–8 h.
- **Next — Bulk WhatsApp / SMS templates** (pre-arrival, welcome, check-out thanks, invoice PDF): templates and outbox exist (module 24); real gateway W8; DLT template registration for SMS in India — **verify with the provider**. 8–16 h after W8.
- **Next — QR menu and room-service ordering** (guest scans, order lands as a KOT on the POS; needs the website/LAN captive page). 40–80 h.
- **Later — Loyalty / repeat-guest offers** on Customers 360 (legacy ten years already imported).

**Revenue and pricing**
- **Now — Tariff card** (§3.1) and **Revenue by category and tender** (§3.3).
- **Next — Dynamic pricing hints from occupancy history.** Yield module uses a *simulated* forecast; replace with the real curve from `lgc` (ten FYs by date/weekday/festival) + FY26 bookings on the books → suggested BAR with "why". 24–48 h. Depends: LG (done), yield (done). Hindu-calendar festival dates need a table the Owner maintains (holiday master exists).
- **Next — Rate change audit + "guest tariff" reports** (LF A1/B1, catalogue P1/P2).
- **Later — Competitor rate watch** (needs internet + a vendor; through the egress gate).

**Housekeeping**
- ★ **Now — Room-status board on the housekeeping tablet with photo on "ready"** (image compression G1 in LF). 8–16 h after LF.
- **Next — Minibar / amenity consumption per room → folio** (extras catalogue exists). 8–16 h.
- **Next — Deep-clean schedule generation** (task type exists, "no tasks generated yet", DEC-MA-7a). 8–16 h.

**Maintenance**
- ★ **Now — Preventive-maintenance calendar from asset register** (ACC-2 asset register + module 15 WOs): AMC due dates as compliance-style cards. 8–16 h.
- **Next — CCTV / DVR and power-backup (DG/UPS/inverter) log**: a checklist template per shift (DVR recording OK, HDD days left, DG run hours, diesel level, battery test) with photo; monthly report. 8–16 h. Legal: some police circulars ask hotels to keep CCTV footage for N days — **verify locally**.
- **Next — Vendor AMC and warranty vault** (document vault below).

**Restaurant / POS / kitchen**
- **Now — Kitchen display (KDS)** on a tablet: KOT by station exists (DEC-MA-4c); a station screen with bump/ready and elapsed time. 16–32 h.
- **Next — Recipe costing and food-cost %**: items × recipe (stock module 16 items) → cost per plate, menu margin, banquet per-plate cost. 24–48 h. Depends: stock item master.
- ★ **Next — Sales by item / hour / voids reports** (catalogue planned P2/P3) in W7.
- **Later — Table reservation and waitlist**; **online aggregator (Swiggy/Zomato) order import** through the egress gate.
- Legal: FSSAI licence card exists (DEC-133); display of FSSAI number on bills — **verify with CA/FSSAI rules**.

**Inventory / purchase**
- ★ **Now — Stock reorder alerts to the Owner digest** (`rpt.stock.reorder` built): push them, don't wait for the report. 4–8 h.
- **Next — Purchase order → GRN → vendor bill three-way match** (GRN simulated, purchases in ACC-2): a PO document with approval and the GRN register (catalogue P2). 24–48 h.
- **Next — Vendor payment calendar**: due dates from bill terms (DEC-204 says due = bill date until a credit-terms master exists) → a weekly "pay list" with TDS and the ₹10,000/₹35,000 cash guards. 12–24 h. CA: section numbers.

**Banquet / events**
- **Now — HB with Quotation (§3.2)**; **banquet function sheet / BEO** (one-page event order for kitchen, housekeeping, decor, with timings) printed from the event. +8–16 h inside W5.
- **Next — Hall calendar on the booking website** (enquiry form only, no price) once the website is exposed.
- **Later — Vendor (decorator/caterer/DJ) sub-contract tracking with payments and TDS** (s.194C — CA).

**HR / payroll**
- ★ **Now — Attendance on the tablet with PIN + selfie** (attendance exists; photo optional G1). 8–16 h. DPDP: staff consent notice — lawyer.
- **Next — Past-attendance correction maker-checker** (audit D6, Owner to rule). 6 h.
- **Next — PF ECR / ESIC / PT upload files** (catalogue P3, DEC-202 #6 says not at go-live; the CA's layouts needed). 16–32 h.
- **Later — Staff training records and the Lodge→HCC training videos per role** (training-media skill exists; HCC needs `window.TRAINING.replay`).

**Accounts / compliance**
- **Now — ACC-4** (year lock, bank reconciliation) and the §3.3 bridge.
- **Next — Document vault with expiry alerts**: licences (lodging, FSSAI, fire NOC, liquor if any, lift, boiler/DG, shop act, trade, music licence PPL/IPRS — **verify which apply**), insurance, AMC, staff documents; stored in `HccDocuments` (LF) with expiry → compliance cards (module exists). 16–32 h after LF.
- **Next — E-invoice readiness check** (turnover watch vs the threshold; CA) and GSTR-1 Table 13 with two series (LF B7).
- **Later — DPDP data-subject requests**: "export / erase my data" for a guest (export built per DEC-068 masking; erasure conflicts with DEC-071 keep-forever — **lawyer**). 8–16 h once ruled.

**Owner dashboards / analytics**
- ★ **Now — Owner daily WhatsApp / e-mail digest** at day close: occupancy, ADR, revenue by category, collections by tender, advances, expenses, cash in drawer, pending approvals, reorder alerts, complaints over SLA, tomorrow's arrivals and events. Content from existing selectors; delivery through the outbox (held until W8 and §3.4 ON; until then shown as a "Yesterday" card and printable). 8–16 h now, send later. DEC-202 #7: money by e-mail only, WhatsApp counts only.
- **Next — Month view**: revenue vs same month last year (ten legacy FYs) and vs budget (expense budgets exist). 16–32 h.
- **Next — Role-based report subscriptions**: W7 scheduled runs (`rpt.admin.scheduled-runs`) with a subscription editor per role per DEC-202 #7.
- **Later — Multi-property readiness**: one firm per property today (DEC-134); the schema has `property` in masters; plan a property column audit before any second property. Not now.

**Security / audit**
- ★ **Now — Audit-trail viewer with filters and export** (`rpt.admin.audit-log` built; add actor/entity/date filters, diff view for masters and settings, CSV). 8–16 h.
- **Next — Export and print log** (catalogue planned P2): who printed/exported what with money or ID data. 8–16 h.
- **Next — Sign-in anomalies**: failed PIN attempts, new device, after-hours Owner actions → digest. 8–16 h.

**Integrations**
- **Now — Internet switch (§3.4)**, then **W8** in the order: e-mail (cheapest, for digests and invoices) → WhatsApp → channel manager → OCR → payment gateway.
- **Next — Tally import proof (OP-22)** and, if the CA wants, **Tally bank-entry round-trip** (not planned).
- **Later — Government portals (FRRO Form C upload, GST portal JSON)** only when the owner asks (DEC-202 #6).

**Reliability / ops**
- **Now — Offline-first tablet mode**: today a 30-minute encrypted read-only copy (DEC-081) + printed morning lists. Extending to queued writes conflicts with the "all mutations through the server" rule; recommend **not** now; instead a UPS on the server PC (DEC-081) and a LAN health card on every tablet. 4–8 h.
- **Now — Restore drill on the hotel PC + two-database backup (LF)**; monthly drill in the Owner digest.
- ★ **Next — Printer health**: a test-print button per printer and a "printer offline" banner on POS/desk. 4–8 h.
- **Later — Second server PC warm standby** (SQL log shipping) if the hotel wants it; not for go-live.

**Mobile**
- **Now — APK v19** on FIT; **Next — phone pages for keys** (none yet, KEYS-DONE §5) and for Tariff card / Hall booking (Sonnet mobile rules).
- **Later — Owner-only "pocket" app view** (digest + approvals + occupancy) as a start-page preset (prefs exist).

---

## 5. Recommended sequence — the next three months (Nov 2026 – Jan 2027)

Constraints honoured: one heavy job at a time (lock), one emulator at a time, no new agent batch while a wave runs, Sonnet for mechanical lanes, Opus for money / statutory / migrations / merges, Fable for specs and contracts. Owner time is called out. Calendar days are GUESS.

| Weeks | Wave / work (on the PC) | Fable in parallel (no heavy jobs) | Owner time needed |
|---|---|---|---|
| **W1 (3–9 Nov)** | **FIT gate → main → HANDOFF/graph** (1 day). **APK v19** lane (Sonnet; 2–3 days: build, phone + tablet AVDs, V17-1…4, tablet prompt). Then **LF** lanes start (9 lanes, Opus + Sonnet; ~2 days on this PC) | Write **HB spec + Quotation** (§3.2) and the **Tariff card design** (§3.1); write the **egress-gate contract** (§3.4); answer-list to the owner (Appendix) | Install v19 on the Tab S7 and run the prompt (2–4 h); answer §3.1–3.4 questions; **front-desk key session** (1–2 h, any evening — needs the desk PC and BIS open) |
| **W2 (10–16 Nov)** | **LF integrator + gate → main**. Then two small lanes beside each other if RAM allows: **Tariff card** (Opus money + Sonnet UI) and **Revenue by category and tender** (Sonnet) | Review LF DONE files; HB spec to the owner for OP-17 | Approve HB spec; hand over paper T&C and the advance pattern; CA meeting booked (items C-1…C-13) |
| **W3 (17–23 Nov)** | **ACC-4** (Opus 2 + Sonnet 1) with the ACC-3 carry-overs; **Egress gate** lane (Opus, small) beside it only if the PC is free | W7 **RPT architecture** (`RPT-ARCHITECTURE.md`) incl. OP-13 hook; §3.3 bridge formulas reviewed against the rulebook | Bank CSV sample; Tally import proof (OP-22, 1–2 h); CA answers start arriving |
| **W4 (24–30 Nov)** | **W5 HB hall booking + Quotation** (Opus 3 + Sonnet 3; ~2–3 days) | HB review; prepare W7 briefs | — |
| **W5 (1–7 Dec)** | HB integrator + gate → main. **KEYS real adapter** lane (after the key session; Sonnet/Opus small) | W7 briefs final | Second key session to try a door (1 h) |
| **W6–W7 (8–21 Dec)** | **W7 RPT** engine lane first (Opus), then report lanes in batches of ≤ 8 (Sonnet) — P1 reports first (43), §3.3 and the day-end / registration-card layouts; **SF split folio** (Opus 2 + Sonnet 1) can run in the gap between batches if RAM allows | Review goldens (PDF drift) | Approve day-end / registration-card mock-ups (30 min) |
| **W8 (22–28 Dec)** | W7 gate → main. **APK v20** (all new modules, phone pages for keys/tariff/HB). Graph + vault refresh | **W8 brief** with the chosen providers behind the egress gate | Choose providers (OP-15) and sign up accounts (e-mail first); decide OP-14 |
| **W9–W10 (29 Dec – 11 Jan)** | **W8 go-live stand-ins**: gl-platform, gl-backup, gl-messaging (e-mail + WhatsApp), gl-channel, gl-ocr, gl-e2e, optional gl-formc; Owner digest switched to real delivery | W9 release brief; INSTALL.md / runbook review | Provider credentials into config (never code); approve message templates |
| **W11 (12–18 Jan)** | **W9 release pipeline**: installer, SQL bootstrap, gate chain, docs; **VM stage** | — | **Keys session** on the hotel PC (1 h); elevated gate run (1 h) |
| **W12 (19–25 Jan)** | Hotel-PC install rehearsal, **restore drill**, release-signed APK with the hotel CA, speed test on a year of data | Training deck + videos for HCC via `/training-media` (needs `window.TRAINING.replay`) | Staff trial with fictional data (2–3 evenings); push after secret scan |
| **after** | **Cut-over evening** (DEC-164): stop Lodge after the last check-out → extractor → masters import (rate card CSVs, DEC-198) → legacy import → in-house guests → opening entry approval (DEC-184) → number series `HPE/2627/0001` → first day close. Parallel run of Lodge for N days is the owner's call | — | The evening (4–8 h) + first-week support |

Dependencies in one line: FIT → v19 → LF → (Tariff card ∥ Revenue report) → ACC-4 (∥ egress gate) → HB → W7 → SF (fills gaps) → W8 → W9 → cut-over. The HB spec, RPT architecture and W8 brief are written while earlier waves run; nothing else overlaps on the PC.

What can slip without hurting go-live: SF split folio, W7 P2/P3 reports, OCR, channel manager (the hotel can keep the OTA extranet by hand), payment gateway, Lodge ID patch (do Stage 0 only if Lodge runs past January).

---

## 6. Appendix — the owner's question list with a suggested default answer

Answer with the number and "yes" or your change; a bare "apply defaults" accepts every default.

| # | Question | Suggested default |
|---|---|---|
| A1 | OP-17 — approve the HB spec when written? | Yes, after a 30-minute read |
| A2 | OP-15 — providers | E-mail: the hotel's existing Google Workspace / SMTP; WhatsApp: Meta Cloud API via an Indian BSP; SMS: DLT-registered gateway; channel manager: the one your OTAs already connect to (name it); payment: Razorpay or the bank's UPI QR (name it). Owner signs |
| A3 | OP-14 — Form C FRRO file and PF ECR / ESIC files in W8? | Form C register + CSV yes (LF); portal file only after a sample; ECR/ESIC later (DEC-202 #6) |
| A4 | OP-13 — calculation engine status? | Tell us where it is; W7 keeps the hook either way |
| A5 | OP-21 — children need an ID? | No (as built) |
| A6 | OP-22 — Tally import proof | Do it this week (1–2 h) |
| A7 | Keys Q1 operator login | Create a dedicated BIS operator "HCC Agent" with Guests/Cards rights |
| A8 | Keys Q2 elevation | Map at the front-desk PC with you present; no elevated retry on the dev PC |
| A9 | Keys Q3 the 36th room / room 106 | Name the room; treat 106 as locked unless the door really fails |
| A10 | Keys Q4 12:00 departure on cards | Yes; later check-outs re-issue |
| A11 | Keys Q5/Q6 English UI, unlocked desk session | Yes, with a screen-lock exception for the agent PC |
| A12 | Keys panel: combine Extended + Reactivated? auto-open after check-in? | Keep separate counts (as built on main); offer the panel as a toast after check-in, not automatic |
| A13 | Tab S7 v19 test | Run within a week of the build |
| A14 | Lodge ID patch | Stage 0 now (shared folder + nightly copy); no VB6 patch if HCC goes live by February |
| A15 | 30-day Lodge report-use log | Yes if a staff member can tick a sheet; else skip |
| A16 | Day-end / registration-card layout approval | Approve the Lodge look with HCC letterhead |
| A17 | DEC-096…110 agent rules | Accept all as built; revisit CRM un-merge later |
| A18 | DEC-MA-11c holiday pair | Retire Wave B's pair in W7 clean-up |
| A19 | Customisation §6 | OS font size honoured; staff may set a start page; no Hindi/Marathi UI now (print labels only, later) |
| A20 | Audit D4 / D6 / D7 | Cash-out needs a second person above ₹20,000; attendance correction = Manager enters, Owner approves; company limits Owner-only |
| A21 | Installer choices; key passphrase; "hotel CA only" | Lock folder yes; merge settings yes; auto-clean plain `.bak` no; passphrase set at the keys session; hotel CA only yes |
| A22 | Legacy: room 211 type; 20-guest review; duplicate-ID groups | Give 211's type; review the 20 in 15 minutes; duplicates after go-live |
| A23 | Bank CSV layout | Export one month from net banking and hand it over |
| A24 | Website exposure | Not before W8; then behind a reverse proxy with CAPTCHA |
| A25 | §3.1 Tariff card: weekend days; seasons; child band; Manager publish; day-use; bilingual print | Fri/Sat/Sun; one card a year + dated seasonal versions; child 5–12 at ₹… (give); Manager proposes only; day-use yes (yield plan exists); English now |
| A26 | §3.2 Quotation: T&C text; advance pattern; GST on package vs itemised; menus; numbering; hall discount limits; lost reasons | Hand over paper T&C; 25 % / 50 % at 30 days / balance on the day; itemised (CA); POS menu master; `QT/2627/0001` yearly; Reception 0 / Manager 10 % / Owner any; yes |
| A27 | §3.3 Revenue report: incl. or excl. GST headline; restaurant/hall rows; advance basis | Both columns; rows on the same page; both received and adjusted |
| A28 | §3.4 Internet switch: default; per-integration; windows; Manager; firewall; Windows Update | OFF at go-live; yes; manual + optional window; Owner only; yes (install step); accepted as outside HCC |
| A29 | Owner daily digest | Yes; e-mail with money, WhatsApp counts only (DEC-202 #7) |
| A30 | Parallel run of Lodge after cut-over | 7 days read-only; no double entry |
| A31 | CA meeting | Book it; take `CA_Question_Pack_2026-10-25.md` + items C-1…C-13 |

---

*Verification notes.* All status claims come from the files named; test counts, commits and migration numbers were read from `CLAUDE.md`, `HANDOFF.md` (13j–13p), `KEYS-DONE.md` and `git log` in the `fit` worktree on 2 Nov 2026. Not verified: whether `claude/fit`'s two uncommitted files matter (not inspected); the exact gate result for FIT (no gate report exists yet); the W7 catalogue's "built" statuses were taken as written (1 Nov) and not re-tested; the "zero outbound calls today" statement rests on a source grep for HTTP clients and URLs, not on a network capture.
