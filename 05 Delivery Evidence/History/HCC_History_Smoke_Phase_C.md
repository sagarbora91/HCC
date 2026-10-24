# FY26 history — Phase C smoke evidence (24 Oct 2026)

Environment: PC browser pane (Chromium, **emulated** viewports), build `dist/index.html` 1,817 KB, scenario FY26, SCHEMA 7. Tests: 435/435. Not a physical-device result.

## Desktop 1366 px — every nav page for every demo user
| User | Pages | Rendered OK | Problems |
|---|---|---|---|
| owner | 34 | 34 | none |
| manager | 31 | 31 | none |
| reception | 18 | 18 | none |
| cashier | 16 | 16 | none |
| supervisor | 6 | 6 | none |
| housekeeper / housekeeper2 | 6 / 6 | 6 / 6 | none |
| accountant | 9 | 9 | none |
| maint | 6 | 6 | none |
| techadmin | 4 | 4 | none |
Check per page: main content present, no horizontal page scroll. Slowest page switch ≈ 84 ms. Console errors: none.

## Phone 360 px — every More-menu entry for every demo user
| User | Bottom tabs | Entries visited | OK |
|---|---|---|---|
| owner | Home, Rooms, Approvals, Reports, More | 37 | 37 |
| manager | Home, Rooms, Approvals, Closing, More | 34 | 34 |
| reception | Rooms, Arrivals, Bookings, Folios, More | 21 | 21 |
| cashier | Cashier, Folios, Expenses, More | 19 | 19 |
| supervisor | Board, Inspect, More | 9 | 9 |
| housekeeper / housekeeper2 | My tasks, Report repair, More | 9 / 9 | 9 / 9 |
| accountant | Reports, Approvals, More | 12 | 12 |
| maint | Work orders, My repairs, More | 9 | 9 |
| techadmin | Users, More | 7 | 7 |
No page-level sideways scroll at 360 px; no script errors.

## Budgets (H-07)
- First screen usable ≈ 0.5 s on this PC (state already stored); generation of FY26 ≈ 0.3 s; JSON 7.4 MB (accounting snapshots 1.9 MB, POS 1.2 MB, bookings 1.2 MB, stock 0.8 MB, messages 0.6 MB).
- One command (e.g. night posting) ≈ 80–100 ms; FY-to-date report pack ≈ 250–320 ms.
- Mid-range phone / Tab S7 first-screen time still to be measured in Phase D (emulator + device).
