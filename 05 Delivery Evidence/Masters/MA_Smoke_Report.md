# Masters wave — smoke report (MA-10)

Run 2026-10-26 · Chrome (Playwright) against `03 Prototype/dist/index.html` served by `node serve.mjs` · tool `03 Prototype/tools/masters-smoke.mjs` (re-run: `npm run build`, `PORT=4181 node serve.mjs`, `node tools/masters-smoke.mjs`) · screenshots in `05 Delivery Evidence/Masters/smoke/` (JPEG, `<viewport>-s<scale>-<master>-list|form.jpg`) · raw numbers `smoke/smoke-results.json`.

## 1. Result

**Master screens: 168 pass / 0 fail** of 168 (28 masters × 3 viewports × 2 text scales; each = the list and one form).
**Phone flows: 16 pass / 0 fail.** **Role click-through: 36 role × viewport × grant runs.** **Console errors: 0.**

A master screen passes when, at that viewport and text scale: the list opens from Master configuration › group › Lists; one form opens (first row, or Add when empty); the page never scrolls sideways; no button / field lies outside the viewport or is clipped; on the phone every touch target is ≥ 44 px, text fields are 16 px and the Save button is in view; no console error. Text scale 1.3 = `localStorage['hcc-prefs-1']` `{"v":1,"p":{…defaults,"textScale":1.3}}`.

## 2. Per master × viewport (text scale 1 / 1.3; P = pass, F = fail)

| Master | 360×800 | 390×844 | 1366×768 |
|---|---|---|---|
| Floors (`floors`) | P / P | P / P | P / P |
| Amenities (`amenities`) | P / P | P / P | P / P |
| Room types (`roomTypes`) | P / P | P / P | P / P |
| Rooms (`rooms`) | P / P | P / P | P / P |
| Occupancy and extra charges (`occupancyCharges`) | P / P | P / P | P / P |
| Meal plans (`mealPlans`) | P / P | P / P | P / P |
| Extras and services (`extras`) | P / P | P / P | P / P |
| Discount reasons (`discountReasons`) | P / P | P / P | P / P |
| Payment methods (`paymentMethods`) | P / P | P / P | P / P |
| Bank accounts and tills (`bankAccounts`) | P / P | P / P | P / P |
| Expense categories (`expenseCategories`) | P / P | P / P | P / P |
| Expense vendors (`expenseVendors`) | P / P | P / P | P / P |
| Outlets (`outlets`) | P / P | P / P | P / P |
| Tables and counters (`posTables`) | P / P | P / P | P / P |
| Menu categories (`menuCategories`) | P / P | P / P | P / P |
| Menu items (`menuItems`) | P / P | P / P | P / P |
| Booking sources (`bookingSources`) | P / P | P / P | P / P |
| Guest ID types (`idTypes`) | P / P | P / P | P / P |
| Numbering series (`numberSeries`) | P / P | P / P | P / P |
| Booking policies (`policies`) | P / P | P / P | P / P |
| Complaint categories (`complaintCategories`) | P / P | P / P | P / P |
| Lost-and-found categories (`lostFoundCategories`) | P / P | P / P | P / P |
| Designations (`designations`) | P / P | P / P | P / P |
| Shift patterns (`shiftPatterns`) | P / P | P / P | P / P |
| Leave types (`leaveTypes`) | P / P | P / P | P / P |
| Holiday calendar (`holidays`) | P / P | P / P | P / P |
| Housekeeping task types (`hkTaskTypes`) | P / P | P / P | P / P |
| Housekeeping zones (`hkZones`) | P / P | P / P | P / P |

## 3. Phone flows (390×844, Owner)

| Flow | Result | Detail | Screenshot |
|---|---|---|---|
| Rooms: range add refuses a taken number with a reason | pass | Room 101 already exists. |  |
| Rooms: Add button names the range and is a 48 px target | pass | Add 401–403 (3 rooms) 48 px |  |
| Rooms: 401–403 added | pass | Added 401–403 (3 rooms) / Rooms 27 rooms | func-390-rooms-range-add.jpg |
| Rooms: Renumber and Set out of order tools present | pass |  |  |
| Floors: reorder by arrow button moves the row and saves | pass | Floor 1 → Floor 2; Order saved | func-390-floors-reorder.jpg |
| Room types: dated-money Add amount is a 48 px target | pass | 48 px |  |
| Room types: dated-money sub-form adds an amount with an effective date | pass | 1 → 2 rows | func-390-roomtypes-dated-money.jpg |
| Room types: multiselect (amenities) toggles from a 44 px+ row | pass | row 54 px | func-390-roomtypes-multiselect.jpg |
| Room types: save closes the sheet and confirms | pass | Room type saved · history recorded |  |
| Floors: empty Add shows inline validation | pass | Enter a code. | func-390-floors-validation.jpg |
| Floors: add saves, sheet closes | pass | Terrace added |  |
| Floors: retire from the sheet | pass | Retired 1; Terrace retired · old records keep the name |  |
| Floors: retired row form shows the reactivate path and a Delete (never used) | pass |  | func-390-floors-retired-sheet.jpg |
| Floors: unused row deletes | pass | Terrace deleted |  |
| Back button closes the sheet and stays in Master configuration (History API) | pass |  |  |
| Floors: a floor in use offers Retire, not Delete | pass |  |  |

## 4. Role click-through (Master configuration, desktop 1366 and phone 390)

Default grants (DEC-MA-1): the page is open to `admin` or `masters`, i.e. **Owner and Tech admin**. Every other role does not get the page (Admin & Setup lists only Approvals, Roadmap, Appearance & display, as their rights allow). To prove the read-only and no-money behaviour for the other roles, a second pass grants `masters` to every role (saved grants patched in IndexedDB — the same effect as the Owner granting it later).

| Role | Viewport | Grants | Master configuration | Groups | Read-only groups | Room types rack rate / Menu price / Occupancy charges / Bank accounts: money fields shown | Lock reason on money lists |
|---|---|---|---|---|---|---|---|
| Owner | 1366 | default | opens | 12 | 0 | 1* / 1* / 3* / 1 | – |
| Manager | 1366 | default | not offered |  |  |  |  |
| Reception | 1366 | default | not offered |  |  |  |  |
| Cashier | 1366 | default | not offered |  |  |  |  |
| Supervisor | 1366 | default | not offered |  |  |  |  |
| Housekeeper | 1366 | default | not offered |  |  |  |  |
| Accountant | 1366 | default | not offered |  |  |  |  |
| Maintenance | 1366 | default | not offered |  |  |  |  |
| Tech admin | 1366 | default | opens | 12 | 5 | 1* / 1* / 3* / 1 | shown |
| Owner | 390 | default | opens | 12 | 0 | 1* / 1* / 3* / 1 | – |
| Manager | 390 | default | not offered |  |  |  |  |
| Reception | 390 | default | not offered |  |  |  |  |
| Cashier | 390 | default | not offered |  |  |  |  |
| Supervisor | 390 | default | not offered |  |  |  |  |
| Housekeeper | 390 | default | not offered |  |  |  |  |
| Accountant | 390 | default | not offered |  |  |  |  |
| Maintenance | 390 | default | not offered |  |  |  |  |
| Tech admin | 390 | default | opens | 12 | 5 | 1* / 1* / 3* / 1 | shown |
| Owner | 1366 | masters granted | opens | 12 | 0 | 1* / 1* / 3* / 1 | – |
| Manager | 1366 | masters granted | opens | 12 | 12 | 1* / 1* / 3* / 1 | shown |
| Reception | 1366 | masters granted | opens | 12 | 12 | 1* / 1* / 3* / 1 | shown |
| Cashier | 1366 | masters granted | opens | 12 | 12 | 1* / 1* / 3* / 1 | shown |
| Supervisor | 1366 | masters granted | opens | 12 | 12 | 0 / 0 / 0 / 0 | shown |
| Housekeeper | 1366 | masters granted | opens | 12 | 12 | 0 / 0 / 0 / 0 | shown |
| Accountant | 1366 | masters granted | opens | 12 | 12 | 1* / 1* / 3* / 1 | shown |
| Maintenance | 1366 | masters granted | opens | 12 | 12 | 0 / 0 / 0 / 0 | shown |
| Tech admin | 1366 | masters granted | opens | 12 | 5 | 1* / 1* / 3* / 1 | shown |
| Owner | 390 | masters granted | opens | 12 | 0 | 1* / 1* / 3* / 1 | – |
| Manager | 390 | masters granted | opens | 12 | 12 | 1* / 1* / 3* / 1 | shown |
| Reception | 390 | masters granted | opens | 12 | 12 | 1* / 1* / 3* / 1 | shown |
| Cashier | 390 | masters granted | opens | 12 | 12 | 1* / 1* / 3* / 1 | shown |
| Supervisor | 390 | masters granted | opens | 12 | 12 | 0 / 0 / 0 / 0 | shown |
| Housekeeper | 390 | masters granted | opens | 12 | 12 | 0 / 0 / 0 / 0 | shown |
| Accountant | 390 | masters granted | opens | 12 | 12 | 1* / 1* / 3* / 1 | shown |
| Maintenance | 390 | masters granted | opens | 12 | 12 | 0 / 0 / 0 / 0 | shown |
| Tech admin | 390 | masters granted | opens | 12 | 5 | 1* / 1* / 3* / 1 | shown |

`*` = a dated-money sub-form is shown. Housekeeper, Maintenance and Supervisor show `0` everywhere: no money field is offered (DEC-061).

Rupee amounts anywhere in the 12 group pages of the three no-money roles (masters granted): **none**.

## 5. Defects

### Fixed in this run

- Phone status chips (Active / Retired / All) sat in a sideways-scrolling row; at 360 px with text scale 1.3 the "All" chip was off screen in 8 masters (rooms, mealPlans, expenseVendors, menuItems, bookingSources, designations, shiftPatterns, hkTaskTypes). Chips now wrap and are 48 px / 16 px (`m-masters.css`, `.mst-m-list .m-chips`).
- Dated-money sub-form: "Add amount" was 41 px high at 360 and 390 px (roomTypes, occupancyCharges ×3 fields, mealPlans, extras, menuItems). Now a full-width 48 px button; amount / date inputs 48 px / 16 px; Remove is 48 px.
- DEC-061 leak in the Master configuration hub: with `masters` granted, Housekeeper, Maintenance and Supervisor saw tariffs, the GST master, deposit / discount limits, menu prices and statutory wage ceilings on every group page (desktop `HotelSettings.tsx`, phone `mobile/admin/Settings.tsx`, `SettingSheet.tsx`, group summaries). New `mstMaskMoney` (masters.ts) / `cfgMoneyMask` turns every rupee amount into "₹ ••" for roles without money visibility and hides the Tariffs / GST blocks with a note. Verified by the browser pass (no rupee amount in any of the 12 group pages, 3 roles × 2 viewports) and by `tests/stream-masters-phone.mjs`.
- Desktop smoke screenshots now scroll the inline form into view (tool only).

### Left for other owners

- By design (DEC-MA-1, `src/app/nav.ts` `p:['admin','masters']`): only Owner and Tech admin are offered Master configuration. Manager, Reception, Cashier, Supervisor, Housekeeper, Accountant and Maintenance get Admin & Setup without it (and no "Manage list…" links). The brief's "every role sees the hub" therefore holds only after the Owner grants `masters`; with it granted all 9 roles open the hub (12 groups, every non-editable group Read only with its reason). If the owner wants a read-only hub for everyone by default, change the nav gate — a decision, not a defect.
- Roles with `masters` granted and money visibility (Manager, Reception, Cashier, Accountant) see money fields (rack rate, menu price, occupancy charges) read-only; DEC-061 only forbids this for housekeeping / maintenance (and Supervisor, which `mstSeesMoney` also treats as no-money).
- `src/features/admin/config/*` (W05-A): setting rows still show the label of money settings (not the value) to no-money roles; only amounts are masked. Fine for DEC-061, noted for the CP-3 / theming agents.
- Not covered by the browser pass: on-screen keyboard behaviour, real touch devices (emulators are not allowed in this wave), the production (server) build, and long-text clipping inside non-control elements (checked by eye on the screenshots only).

### Notes

- First pass (before the fixes) found 8 chip rows and 5 dated-money buttons failing; the numbers above are the second, full pass on the fixed build.
- Masters whose list shows no Add (Rooms: tools above the list) were opened through their first row; Rooms tools (range add, renumber, out of order) are exercised in section 3.
