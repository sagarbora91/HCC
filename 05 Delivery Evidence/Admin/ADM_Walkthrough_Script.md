# Administration page — owner walkthrough script (ADM-1…3)

For the hotel-PC session after `claude/adm` is built (`npm start` in `03 Prototype`, FY26 scenario; the Windows copy is `E:\Projects\HCC\Hotel Control Centre\03 Prototype`). About 25 minutes. Tick each step; write anything unexpected in the **Result** column and save this file to `05 Delivery Evidence/Admin/ADM_Walkthrough_<date>.md`.

Demo people (fictional, FY26 only): **Sagar** (Owner), **Lata** (Tech admin), **Meera** (Manager), **Tanvi · Day shift manager** (custom role C01 = Manager without Accounting), **Rohan · Night shift manager** (C02 = Manager with Accounting view-only). Sign in from the demo staff picker; switch users through the user menu › Sign out.

Rules being shown: DEC-214…221 (`02 Planning/HCC_Admin_Panel_Plan_v1.md` §7).

## A. Where it lives (2 min)

| # | Do | Expect | Result |
|---|---|---|---|
| A1 | Sign in as Sagar. Modules Home › **Admin & Setup** tile | Tile lists **Administration** (not "Access control") | |
| A2 | Press Ctrl+K, type `roles` | Hit "Administration" under Admin & Setup; Enter opens it on the **Users** tab | |
| A3 | Look at the tab strip | Four tabs: Modules & features · Roles · Users · Audit | |

## B. Modules & features (5 min)

| # | Do | Expect | Result |
|---|---|---|---|
| B1 | Tab **Modules & features** | Top: the ten module switches as before. Below: **Sub-functions** grouped by module; core rows say "Always on" with the DEC-215 reason and have no button | |
| B2 | Front desk › **Reservations** › Switch off | Toast "Reservations switched off · data kept". Row shows Off | |
| B3 | Open the hamburger menu and Modules Home › Front Desk | **Reservations** and **Calendar** are gone; **Room board** and **Arrivals & check-in** stay | |
| B4 | Arrivals & check-in › **Walk-in** › book a room for today | Works (walk-in for today is a check-in, not a reservation) | |
| B5 | Back on the tab, switch Reservations on | Pages return in the menu | |
| B6 | Cash & billing › **Expenses** › Switch off | Confirm dialog names **Purchases & vendor payments** as going off with it; after OK both rows show Off, Purchases row says it is off with Expenses | |
| B7 | Switch **Expenses** on | Expenses On; **Purchases** stays Off until you switch it on yourself | |
| B8 | Switch Purchases on | Everything on again; group summary at the top reads "10 of 10 on · 6 core always on · 36 of 36 sub-functions on" (the number may differ if modules were added later) | |
| B9 | Sign in as Lata (Tech admin), same tab | She can switch sub-functions (non-money setting) | |
| B10 | Sign in as Meera (Manager), open Master configuration › Modules on/off | Read-only with the reason "Owner changes this…" | |

## C. Roles (8 min)

| # | Do | Expect | Result |
|---|---|---|---|
| C1 | As Sagar: tab **Roles** | List: built-in roles greyed "Built-in · read-only", then **C01 Day shift manager** and **C02 Night shift manager** (base Manager, money on, 1 user each, version) | |
| C2 | Click C01 | Role editor: grid of sub-functions grouped by module, each row Off / View / Full; every **Accounting** row is **Off**; money switch on; **Advanced: permission ids** collapsed; effective-access preview below; **Retire role** at the end | |
| C3 | Click C02 | Accounting rows show **View**; Day close shows Full | |
| C4 | **New role**: name `Front desk lead`, base **Reception**, money left on › Create role | New row C03 in the list; editor opens; grid matches Reception; base picker never offered **Owner** | |
| C5 | In C03 set Cash & billing › **Cash book** to Full | Row Full; preview now lists the Cash book page | |
| C6 | **New role**: name `Floor supervisor`, base **Housekeeper** | Money switch is **off and locked** with the DEC-061 note; no money feature can be set to Full | |
| C7 | In C03 open **Advanced: permission ids**; try to raise the discount limit above the Reception base | Refused with the SoD text (DEC-219); lowering it is allowed | |
| C8 | Try to edit a built-in (e.g. Manager) | Only the read-only matrix; no grid, no save | |
| C9 | Retire **Floor supervisor** | Confirm, then it shows **Retired** and leaves the pickers | |
| C10 | Try to retire **C01** | Refused: "held by an enabled user" (Tanvi) | |
| C11 | Sign in as Meera, tab Roles | "Owner only" explanation, read-only list, no editor | |
| C12 | Sign in as Lata (Tech admin), tab Roles | Same read-only view (roles are Owner-only, DEC-218) | |

## D. Users (5 min)

| # | Do | Expect | Result |
|---|---|---|---|
| D1 | As Sagar: tab **Users** | People list shows Tanvi and Rohan with "Day shift manager (Manager)" / "Night shift manager (Manager)" | |
| D2 | **New user**: name `Priya`, role picker | Picker lists the built-ins and the custom roles with their base; Owner only for an Owner; choose **Front desk lead** › Create | |
| D3 | Open Priya › **Effective access** | Pages listed match the C03 grid (Cash book included after C5) | |
| D4 | Sign in as Lata: **New user** with role C01 Day shift manager | Refused / greyed: a money role is assigned by the Owner only; a non-money custom role is allowed | |
| D5 | As Sagar: Tanvi › change role to **Night shift manager** › save | Audit tab later shows the change; Tanvi's effective access now has Accounting view | |
| D6 | Try to change **your own** (Sagar's) role | Refused: "Use another owner account…" | |

## E. What the custom roles actually see (4 min)

| # | Do | Expect | Result |
|---|---|---|---|
| E1 | Sign in as **Tanvi** (set her back to Day shift manager first if D5 changed it) | Modules Home: **Accounts & Reports** tile has no **Accounting**; Ctrl+K "accounting" finds nothing | |
| E2 | Tanvi: Day closing › Night post / Day close path | Available (Manager base) | |
| E3 | Sign in as **Rohan** | Accounting page opens; **Trial balance / ledgers** readable | |
| E4 | Rohan: Accounting › Manual vouchers › Save a voucher | Refused: "Manual vouchers & loans (Accounting) is view only for your role Night shift manager." | |
| E5 | Rohan: Day closing › Day close | Allowed (Full) | |
| E6 | Switch the **Accounting › Manual vouchers** property switch off (as Sagar), then as Rohan | Page section hidden for everyone; the property switch wins over the role level | |

## F. Audit (1 min)

| # | Do | Expect | Result |
|---|---|---|---|
| F1 | As Sagar: tab **Audit** | Newest first: C03 created, C03 Cash book Full, Floor supervisor created/retired, Priya created, role changes; each line has version · who · when · before → after | |
| F2 | Approvals & audit page | The same ROLE_* and SETTING_SET entries appear in the audit trail | |

## G. Phone (3 min, Galaxy Tab or AVD, or browser at 360 px with Display = Mobile)

| # | Do | Expect | Result |
|---|---|---|---|
| G1 | Admin & Setup › Administration | Chip bar Modules / Roles / Users / Audit; default Users | |
| G2 | Roles › C01 | Cards per sub-function; a 3-button Off / View / Full control, 48 px tall, 16 px text; no sideways scroll | |
| G3 | Advanced permission ids | Note says desktop only | |

## Reset
Switch any sub-functions you left off back on; the demo state is in the browser only (Demo toolbar › Reset to FY26 restores everything).

## Sign-off
- [ ] All expectations met, or every deviation written in the Result column
- [ ] Deviations copied to `05 Delivery Evidence/Admin/ADM-DONE.md` §6 as open items
- Owner: ____________  Date: ____________
