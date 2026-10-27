# SCC decisions wave — compliance (tax): DEC-125 and DEC-133 (27 Oct 2026)

Branch `claude/dec-tax` (base `34d3de32`, fast-forwarded to `main` `f18812ca` for the DEC-133 register row). Code commit `1f105d0c`; this report is the commit after it.

## DEC-125 — the compliance FY follows the business date on 1 April

**Rule (same on prototype and server):** with a pick, the FY the business date was in is stored too (`fyChosenIn` / `cmp.setting.fy_chosen_in`). The calendar shows the pick while the business date is still in that FY; from the next 1 April — and when nothing was picked — it shows the business date's FY, kept inside SCC's list (before April 2026 → 2026–27, after 2032–33 → 2032–33). Never the device clock. Prototype `cmpEffectiveFy`, server `SccTaxRules.EffectiveFy`.

- Examples (tested on both sides): 2026–27 picked in Oct 2026 → stays; business date 1 Apr 2027 → 2027–28; 2026–27 picked on 2 Apr 2027 → holds until 31 Mar 2028, then 2028–29.
- **Everything uses the effective FY:** month page, overdue dots, dashboard, Action Center, Accounts tile badge, phone home, "Due this week", CSV register, month print, ≈₹ payable feed, evidence ZIP, CA pack, commands sent without an FY; server `GET /compliance/state` (`fyStart` = effective, new `fyChosenIn` = the pick in force or null) and every server query default.
- **Old data, no surprise jump:** a save / server row from before DEC-125 whose audit shows an FY pick (prototype `CMP FY SET`, server `COMPLIANCE_FY`) counts as picked in the current business-date FY, so it holds until the next 1 April. With no recorded pick the calendar simply follows the business date (today that is the same 2026–27). Prototype: restore step in `persistence.ts` (shape-detected, idempotent, SCHEMA stays 10). Server: migration **4180**.
- **Screen (desktop + phone):** the FY selector explains the rule (tooltip) and, while a pick of another FY holds, shows "Your pick · holds until 31 Mar 2027" (+ ", then FY …" when that differs). Checked in the browser at desktop width and 375 px (no sideways scroll).

## DEC-133 — FSSAI and lodging house licence cards (hotel in Latur, Maharashtra)

- **Firm profile** (Owner): optional FSSAI licence number + expiry date, lodging house licence number + expiry date (desktop dialog and phone sheet; under each date: "Renewal card: May page of FY 2027–28, due 31 May 2027 (risk High)"). Left out = kept, empty = cleared. Server migration **4181** (`cmp.profile` columns).
- **Cards:** "FSSAI Licence – Renewal" (FoSCoS portal) and "Lodging House Licence – Renewal" (licensing authority: local police / municipal corporation). A card exists only while its expiry date is set; it sits on the FY page of the expiry month, due on the expiry date; risk High, responsible "Owner / General Manager", reviewer Owner; SCC's control layer gives the internal deadline (T-5) and escalation. The licence number shows in the Form / Portal line.
- Action Center, badge, CSV, print, dashboard, done / NA / reopen, evidence files and archive (keys "FSSAI Licence", "Lodging House Licence", listed with the built-in keys) work as for every card; the cards stay when the firm has no employees.
- **Texts** (who / what / why / penalty, checklist, workflow, evidence) are plain and cautious; lead time and penalty amounts say "CA to confirm", no ₹ figures. One copy: `08 Production/src/Hcc.Domain/Compliance/LicenceCards.json` = prototype `compliance-licences.ts` (tests on both sides compare them). SCC's Maharashtra texts are unchanged.
- **Demo data (fictional):** DEMO-FSSAI-0001 expiring 31 May 2027 and DEMO-LODGING-0001 expiring 31 Dec 2027 — both in FY 2027–28, so the FY 2026–27 demo calendar stays exactly SCC's (all SCC parity tests unchanged). They appear on 1 April 2027 when the calendar moves to 2027–28 (DEC-125), or when the user picks 2027–28.

DEC-126 untouched (≈₹ PF / ESIC and GST figures as built).

## Tests

- Prototype `npm test` **1847/1847** (was 1838): new `tests/stream-compliance-dec.mjs` (7: [DEC-125] ×3, [DEC-133] ×4) and `tests/sync-compliance-dec.mjs` (2: production-client mapping); `[CMP] FY selector …` in `stream-compliance.mjs` now asserts the DEC-125 switch. `npx tsc --noEmit`, `npm run build`, `npm run build:production` — 0 warnings.
- Server `dotnet build Hcc.slnx` 0 warnings / 0 errors. Hcc.Compliance.Tests **35/35** (was 27): `Domain/SccDecisionsTests` (3), `Sql/SccDecisionsSqlTests` (5: FY switch and pick, every read on the effective FY, migration 4180 stamping rule, licence cards through every view and command, licence field rules); the FY test in `ComplianceSqlTests` renamed. Also run: Platform 70/70 (migration lock), Masters 20/20 (slnx), PeopleLists 41/41, Integration RoleReadMatrix 1/1 (endpoints unchanged, `_role-reads.json` not regenerated). Server total expected 3315 (3307 + 8).

## Migrations, pins

- **4180** `compliance_fy_follows_business_date` (`cmp.setting.fy_chosen_in` + stamping of earlier picks), **4181** `compliance_licence_fields` — both in `Hcc.slnx` and `migrations.lock.json`. No new permissions.
- Pins moved — only by the demo profile's fictional licence fields (checked: removing those four fields gives the old hashes): BASE `4a8aad03…` → `aecae3b2…`, history `197643a1…` → `5dc19a44…` (8 test files), FY26 whole state `8ba45941…` → `3b33a7c6…` (`stream-masters-fixture.mjs`, `stream-perf-store.mjs`). FY26 bookings pin unchanged. No golden files moved.

## For the owner / CA

1. The licence cards are in SCC's **Labour Law** category (where SCC keeps its Shops & Establishment renewal), so archiving "Labour Law" hides them too. A separate "Licences" category can be added if you prefer.
2. After a renewal the Owner enters the new expiry date: the card moves to that month / FY, and the filed card of the old expiry is no longer shown on its old page (its record stays stored). Keep it this way?
3. CA to confirm for the licence cards: how many days before expiry to apply (FSSAI / lodging), late-renewal rules and penalty amounts, and which authority issues the lodging licence in Latur (police, municipal corporation, or both).
