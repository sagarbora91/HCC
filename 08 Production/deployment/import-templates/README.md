# Importing your hotel's masters and opening balances

This folder holds the files you fill in once, when HCC starts, so you do not have to type your rooms, rates, staff,
accounts, departments, company clients, suppliers, stock items and the lists of Master configuration (floors, meal plans, payment methods, booking sources, shift patterns, ...) by hand. You load them with the script
`Import-HccMasters.ps1` (in the scripts folder, `C:\Program Files\HCC\Operations`; step 6d of `deployment\windows\INSTALL.md`).
The installer also copies this folder to `C:\Program Files\HCC\ImportTemplates`.

- `templates\` – empty files with the right column headings. Copy one, fill it in, save it.
- `samples\` – the same files filled with **made-up** data for a fictional hotel. Open them to see what a filled file looks like.
  Do not import the samples into your real hotel.

## How to fill a file

1. Open the template in Excel (or Google Sheets / LibreOffice). Keep the first row (the headings) exactly as it is.
2. Type one item per row. Leave a cell blank when the column says "optional".
3. Save with **File › Save As › CSV UTF-8 (Comma delimited) (*.csv)**. Plain "CSV (Comma delimited)" also works.
4. Amounts are in **rupees** with up to 2 decimals: `4200`, `4200.50`, `4,200.50`, `₹ 1,25,000` are all fine. HCC stores paise
   internally. If you type more than 2 decimals (`99.555`) HCC rounds to the nearest paisa (half away from zero: ₹99.56) and shows
   a warning.
5. Dates: `2026-11-01` is best; `01-11-2026` and `01/11/2026` (day first) are also accepted.
6. Lists inside one cell (for example two roles) are separated by `;` – never by a comma.
7. Text that a spreadsheet would run as a formula – a name, note, e-mail or table name starting with `=`, `+`, `-` or `@` – is kept
   as plain text with a leading apostrophe (`=HYPERLINK(…)` is stored as `'=HYPERLINK(…)`), and the check shows a warning for that
   cell. Codes, numbers, dates, phone numbers and lists of codes are checked by their own rules instead: formula text there is an
   error, while a minus sign in a number column stays a minus (`-10` percent in seasons).

**Never put passwords, PINs, setup codes, Aadhaar, PAN, passport or any other ID number in any file.** HCC refuses a file
that has such a column. Staff get their first password or PIN from you in Access control after the import.

## The order to import

| Step | File | Needs first | Who can commit |
|---|---|---|---|
| 1 | room-types | – | Owner |
| 2 | rooms | room types | Owner |
| 3 | tariffs | room types | Owner |
| 4 | seasons (optional) | room types | Owner |
| 5 | staff | – | Owner |
| 6 | ledgers | – | Owner |
| 7 | opening-balances | ledgers | Owner, **only before the first day is closed** |
| 8 | till-cash | – | Owner, **only before the first day is closed** |
| 9 | guests (optional) | – | Owner |
| 10 | pos-outlets (restaurant, room service) | – | Owner |
| 11 | pos-menu | pos-outlets | Owner |
| 12 | departments (staff and payroll) | – | Owner |
| 13 | companies (corporate clients) | room types (for negotiated rates) | Owner |
| 14 | suppliers (and service vendors) | – | Owner |
| 15 | stock-items (linen, amenities, cleaning) | – | Owner |
| 16 | floors | – | Owner |
| 17 | amenities | – | Owner |
| 18 | occupancy-charges | room types | Owner |
| 19 | meal-plans | room types | Owner |
| 20 | extras | – | Owner |
| 21 | discount-reasons | – | Owner |
| 22 | bank-accounts | – | Owner |
| 23 | payment-methods | bank-accounts | Owner |
| 24 | expense-categories | – | Owner |
| 25 | expense-vendors | – | Owner |
| 26 | menu-categories | – | Owner |
| 27 | pos-tables | pos-outlets | Owner |
| 28 | booking-sources | – | Owner |
| 29 | policies | booking-sources | Owner |
| 30 | id-types | – | Owner |
| 31 | number-series | – | Owner |
| 32 | designations | departments | Owner |
| 33 | shift-patterns | – | Owner |
| 34 | leave-types | – | Owner |
| 35 | holidays | – | Owner |
| 36 | hk-task-types | – | Owner |
| 37 | hk-zones | floors, rooms, staff | Owner |
| 38 | complaint-categories | – | Owner |
| 39 | lost-found-categories | – | Owner |

The script imports the files it finds in this order. You do not need every file: leave out the ones you do not use.

## Upload → check → commit (the script)

Put your filled files in one folder (for example `D:\HccMasters`), named exactly like the templates (`rooms.csv`, `staff.csv`, ...).
Then, in PowerShell on the server PC, in the scripts folder (`C:\Program Files\HCC\Operations`):

```
.\Import-HccMasters.ps1 -Folder "D:\HccMasters" -DryRun                                  # check only: nothing is kept
.\Import-HccMasters.ps1 -Folder "D:\HccMasters" -OpeningBalancesDate 2026-10-31          # import for real
```

1. **Sign-in.** The script asks for the Owner's sign-in name and password (the password is not shown, not saved and not written
   to any file or log). It talks to the server over HTTPS (`https://localhost:5443`, or `ServerUrl` in `hcc-ops.json`, or
   `-ServerUrl`), with the hotel certificate installed in step 8 of INSTALL.md; it never sends a password over plain HTTP to another PC.
2. **Upload.** Each file is sent to the server (command `UploadImportBatch`, with the master it is). HCC checks every row at once.
3. **Report.** For each file the script prints the rows that are **rejected**, with the spreadsheet row, the column and what to fix
   (for example "Row 7 · rate · '42OO' is not an amount in rupees"), the warnings, and what will be **created**, **updated** or
   left **unchanged**. Nothing is saved yet.
4. **Commit** (only without `-DryRun`): everything in the file is saved in one go – either all rows or none (command
   `CommitImportBatch`). It is recorded in the audit trail with your name. The script then goes on with the next file, so the rooms
   file sees the room types that were just committed.
   - If a file has a rejected row, the script **stops** there and commits nothing more. Fix the file in Excel and run it again: files
     already committed show every row as "unchanged" and are skipped. `-ContinueOnErrors` skips only the rejected file and goes on.
   - Importing the same file again changes nothing (every row shows "unchanged"); committing the same upload twice is refused.
5. **Dry run** (`-DryRun`): the server runs each file's whole import and then undoes it, to prove it will go through. Nothing is kept.
   On a new hotel a later file can show "... is not in HCC yet" for something an earlier file of the same run creates (rooms need
   room types); the script marks those rows "expected in a dry run" and does not count them as rejected.
6. `-WhatIf` only lists the files it found, in the order it would import them, and sends nothing.
7. **Check** afterwards (INSTALL.md step 6d, "Verify"): the rooms and users show on their screens, and the import list
   (`GET /api/v1/import/batches`) shows every upload and commit.

The Technical administrator may upload, check and dry-run every file, but only the Owner can commit (money, rates, roles and
accounts are Owner-only, DEC-058). Behind the script are the server commands `UploadImportBatch` and `CommitImportBatch`
(`/api/v1/commands/...`); there is no import screen in the app yet.

## Columns of each file

### room-types.csv
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code of the room type: letters, digits, dash, up to 20 characters (e.g. `DLX`). The rooms and tariffs files use it. |
| name | yes | Name shown on screens and bills (up to 60 characters). |
| maxOccupancy | yes | Most guests allowed in one room of this type, 1 to 10. |
| rackRate | yes | Standard price per night in rupees **before GST**. Used when no dated tariff applies. |
| sortOrder | optional | Order on screens (1 first). Blank = order of the file. |

A room type already in HCC with the same code is **updated** (name, guests, rack rate, order).

### rooms.csv
| Column | Required | Meaning |
|---|---|---|
| roomNo | yes | Room number as on the door, up to 10 characters (e.g. `101`). |
| roomType | yes | Code of its room type. |

New rooms start as "Ready". A room already in HCC is left as it is; changing a room's type is done on the Rooms screen.

### tariffs.csv (best available rate, BAR)
| Column | Required | Meaning |
|---|---|---|
| roomType | yes | Code of the room type. |
| effectiveFrom | yes | First night the rate applies; it applies until the next dated rate of that room type. Not before today's business date. |
| rate | yes | Price per night in rupees before GST. |

Bookings already made keep the price agreed at booking. GST is added by HCC from the GST master.

### seasons.csv (optional, yield)
| Column | Required | Meaning |
|---|---|---|
| name | yes | Season name, e.g. `Diwali peak`. |
| from | yes | First night of the season. |
| to | yes | Last night of the season (at most one year after `from`). |
| adjustmentPercent | yes | Change to the BAR: `+15` raises 15 %, `-10` lowers 10 % (between -50 and +100, not 0). |
| roomTypes | optional | Room type codes separated by `;`. Blank = all room types. |
| priority | optional | When seasons overlap the higher number wins (default 1). |

A season whose name already exists in HCC with other dates is refused – change it on the Yield screen.

**A season does not change a quote by itself.** It is a yield rule: HCC applies it to the BAR only when the Yield screen publishes the rates for those nights (then new quotes and bookings show the adjusted price). Importing a season only stores the rule; until the rates are published a quote for those nights still shows the plain BAR from the tariffs file.

### staff.csv
| Column | Required | Meaning |
|---|---|---|
| loginName | yes | Sign-in name: 3–40 lower-case letters, digits, dot, dash or underscore (e.g. `priya.reception`). |
| displayName | yes | Name shown on screens and in the audit trail. |
| roles | yes | One or more of: `REC` Reception, `CSH` Cashier, `MGR` Manager, `ACC` Accountant, `SUP` Housekeeping supervisor, `HKP` Housekeeper, `MNT` Maintenance, `TAD` Technical administrator – separated by `;`. The role name (`Reception`) works too. |

The Owner role is never imported. New users **cannot sign in yet**: after the commit HCC lists them, and you set each person's
first password (Owner, Accountant, Technical administrator) or 6-digit PIN (everyone else) in Access control › Set credential.
An existing user with a different name or roles is refused – change users in Access control.

### ledgers.csv (your chart of accounts)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Your own ledger code, e.g. the Tally code (up to 20 characters). It must not be one of HCC's 4-digit codes below. |
| name | yes | Ledger name. |
| group | yes | `Asset`, `Liability`, `Equity`, `Income` or `Expense`. |
| hccAccount | optional | The HCC account (4-digit code below, same group) this ledger rolls up into. Leave blank for capital/equity and for ledgers HCC never posts to (loans, fixed assets…). |

You do **not** need to list HCC's own accounts here – use their 4-digit codes directly in the opening balances.

HCC's own accounts (illustrative – your CA confirms):

| Code | Account | Group |
|---|---|---|
| 1000 | Cash in hand | Asset |
| 1010 | UPI clearing | Asset |
| 1020 | Card clearing | Asset |
| 1030 | Bank account | Asset |
| 1100 | Guest ledger | Asset |
| 1110 | Company receivables (city ledger) | Asset |
| 1200 | Staff advances | Asset |
| 2000 | Advance deposits | Liability |
| 2110 | Output CGST | Liability |
| 2120 | Output SGST | Liability |
| 2200 | Vendor payables | Liability |
| 2300 | Salaries payable | Liability |
| 2310 | PF payable | Liability |
| 2320 | ESIC payable | Liability |
| 2330 | Professional tax payable | Liability |
| 2340 | Other payroll deductions | Liability |
| 4000 | Room revenue | Income |
| 4100 | Other revenue | Income |
| 4900 | Discounts allowed | Income |
| 4910 | Refunds, reversals & credit notes | Income |
| 5000–5050 | Supplies, Laundry, Repairs & maintenance, Utilities, Petty cash & sundry, General expenses | Expense |
| 5100 | Salaries & wages | Expense |
| 5110 | Employer PF & ESIC | Expense |
| 5900 | Cash over / short | Expense |

### opening-balances.csv (trial balance on the day before HCC starts)
| Column | Required | Meaning |
|---|---|---|
| ledgerCode | yes | An HCC 4-digit account or a code from your ledgers file. |
| debit | one of the two | Debit balance in rupees (cash, bank, receivables, fixed assets, expenses). |
| credit | one of the two | Credit balance in rupees (payables, loans, capital, income). |

When uploading, also give the **as-at date** (the last day before HCC starts, e.g. `2026-10-25`). Rules:
- Each row has either a debit or a credit, not both; leave out zero balances.
- **Total debits must equal total credits.** If they differ, HCC shows both totals and the difference and refuses the file.
- Importing again (before the first day is closed) **replaces** all opening balances.
- Once the first business day is closed, opening balances can no longer be imported – your CA posts a journal instead.

### till-cash.csv
| Column | Required | Meaning |
|---|---|---|
| tillCode | yes | Code of the cash drawer, letters/digits/dash (e.g. `FD-1`). A new code creates the till. |
| tillName | yes | Name of the drawer. |
| openingCash | yes | Cash physically in the drawer when HCC starts, in rupees (count it). |

HCC records this as a counted opening, so the first cashier shift opens with exactly this float. Allowed only before the first
day is closed and only for a till that has no shifts yet. It should match the cash-in-hand (1000) opening balance.

### guests.csv (optional)
| Column | Required | Meaning |
|---|---|---|
| name | yes | Guest's full name. |
| phone | yes | 10-digit mobile number (`+91` and spaces allowed). |
| email | optional | E-mail address. |
| address | optional | Postal address. |
| nationality | optional | Default Indian. |
| company | optional | Company name (needed when a GSTIN is given). |
| gstin | optional | Company GSTIN, 15 characters. |
| notes | optional | Notes for the front desk. |

A guest with the same name and phone already in HCC is not changed. ID documents are captured at check-in, never imported.

### pos-outlets.csv (Restaurant & room-service outlets (POS))
| Column | Required | Meaning |
|---|---|---|
| code | yes | Outlet code, letters/digits/dash, up to 20 characters (stored in capitals). |
| name | yes | Outlet name shown on the POS screen and bills, up to 100 characters. |
| type | yes | restaurant, room-service, bar or banquet (the older words table = restaurant and room = room-service still work). Room-service outlets have no tables. |
| opens | optional | Opening time HH:mm (blank = not set). |
| closes | optional | Closing time HH:mm (blank = not set). |
| gstProfile | optional | hotel (hotel status, default), standard or specified premises — decides the restaurant GST rate (illustrative, CA to confirm). Owner-only. |
| kotStation | optional | Kitchen station a ticket prints at, up to 20 characters (blank = one ticket per send). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the outlet's order, or puts a new outlet last. |
| active | optional | yes or no (blank keeps the state, new = yes). A file cannot retire an outlet — retire it on its list, where open checks are looked at. |
| tables | optional | Table names separated by ; (up to 10 characters each). Creates the tables of a new dining outlet (required then); an existing outlet keeps its tables, which are added or retired in the pos-tables list. Blank for room-service. |

An outlet already in HCC is **updated** (name, type, hours, GST profile, station, order) when those differ and "unchanged" otherwise. Its kind (dining or room service) cannot change, and a `tables` list naming a table HCC does not have is refused: tables are added, renamed or retired in the pos-tables file or list. Older files with `outletCode`, `outletName` and `kind` (`table` / `room`) still work. The GST profile is Owner-only, so the whole file is committed by the Owner. Nothing here posts any money.

### pos-menu.csv (Restaurant & room-service menu (POS))
| Column | Required | Meaning |
|---|---|---|
| code | yes | Menu item code, letters/digits/dash/underscore, up to 20 characters (stored in capitals). |
| name | yes | Item name shown on the POS screen and bills, up to 100 characters. |
| category | yes | Menu category: the code or the name of a category (a new name creates the category), up to 60 characters. |
| shortCode | optional | Short code the waiter types, up to 10 characters. |
| price | yes | Selling price in rupees before GST (0 allowed). A different price starts from the business date; earlier prices stay in the history. |
| gstClass | optional | food (default), laundry, banquet or other — the GST master holds the rates. Owner-only. |
| veg | yes | yes for vegetarian, no for non-vegetarian. |
| outlets | optional | Outlet codes that sell the item, separated by ; (blank = every outlet already in HCC). |
| station | optional | Kitchen station, up to 20 characters (blank = the outlet's station). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the item's order, or puts a new item last. |
| active | optional | yes or no (blank keeps the state, new = yes). A file cannot retire an item — retire it on the menu list. |

An item already in HCC (same code) is **updated** when a column differs, and "unchanged" otherwise. A different price starts from the business date (earlier prices stay in the history); a category name that does not exist yet creates the category. Older files with `itemCode` and `itemName` still work. Items sold out ("86") keep their availability; it is changed on the POS screen.

### departments.csv (staff and payroll)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Department code, 2 to 10 capital letters or digits (e.g. `FO`). Staff are put in a department by this code. |
| name | yes | Department name, 2 to 60 characters. |
| leaveCap | optional | Advisory cap: staff of this department on leave on one day before the day is flagged, 0 or more (blank = 3, SCC's limit a day; it never blocks — the hotel-wide limit is 3 a day). |

A department already in HCC with the same code is **updated** (name, leave limit).

### companies.csv (company accounts, corporate clients)
| Column | Required | Meaning |
|---|---|---|
| name | yes | Company name, up to 200 characters. A re-import finds the account by this name (capitals do not matter). |
| gstin | optional | Company GSTIN, 15 characters, for B2B invoices. |
| contact | optional | Contact person. |
| phone | optional | Phone, 7 to 16 digits (`+91` and spaces allowed). |
| email | optional | E-mail address for invoices. |
| city | optional | City. |
| creditLimit | optional | Credit limit in rupees, up to ₹10 crore. Blank or `0` = no credit. HCC **stores** this value (the Groups screen shows it); it is not yet checked when a stay is charged to the company. |
| negotiatedRates | optional | Negotiated nightly rates before GST, as `room type=rupees` separated by `;`, e.g. `DLX=3800;STD=2600`. Import the room types first. |

An account already in HCC with the same name is **updated** with the file's values, including the credit limit (a pending limit
request for it is closed). Changing a limit later in the app keeps the usual approval rule.

### suppliers.csv (suppliers and service vendors)
| Column | Required | Meaning |
|---|---|---|
| name | yes | Supplier or vendor name, 3 to 120 characters. A re-import finds it by this name. |
| phone | yes | Phone number (digits, spaces, `-`; a leading `+` is allowed). |
| trade | optional | What they supply or repair (e.g. `Laundry`, `Electrical`), up to 60 characters. Blank = `General`. |

Suppliers go into the vendor list (Maintenance › Vendors & AMC), where work orders and AMC contracts pick them. Expenses and goods
receipts still take the supplier's name as typed text.

### stock-items.csv (linen, amenities, cleaning)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Item code like `LIN-BS`: 2 to 6 letters/digits, a dash, 1 to 10 letters/digits (stored in capitals). |
| name | yes | Item name, 2 to 120 characters. |
| kind | yes | `linen`, `amenity` or `cleaning`. |
| unit | yes | Unit of counting, e.g. `pcs`, `can`. |
| unitCost | optional | Cost per unit in rupees, at most ₹1,00,000. Blank = not costed yet. |
| minQty | optional | Minimum stock before a reorder is suggested, 0 to 10,000 (blank = 0). |
| reorderQty | optional | How many to reorder, 0 to 10,000 (blank = 0). |
| usePerRoomNight | optional | Standard use per occupied room-night, e.g. `1.5` (0 to 100, up to 2 decimals). |
| laundryRate | optional | Laundry charge per piece in rupees, **linen only**. |
| supplier | optional | Usual supplier's name. |

New items start with **no stock**: after the import, count each store and floor in Stock › Count so HCC knows what is there.
An item already in HCC (same code) keeps its name, kind and unit (a different value is refused: change it on the Stock screen);
only the minimum, reorder and use-per-room-night levels are **updated**. Its unit cost (kept as an average by goods receipts),
laundry rate and supplier are not changed by a re-import (a warning says so).

## Lists of the masters wave (Master configuration)

These files fill the lists you otherwise keep in Master configuration. Each row has a `code` (capital letters, digits, dash - the row is found again by it), a `name`, the columns below, a `sortOrder` and `active`. A blank optional cell clears the value, except `sortOrder` and `active` (blank keeps) and dated amounts (blank keeps the price). A column left out of the file entirely leaves that column as it is. **A file never retires or deletes a row** - retire on the list, where its use is checked. Lists that HCC starts with built-in rows (booking sources, ID types, numbering series, policies, leave types, shift patterns, payment methods, bank accounts, expense categories, task types, complaint categories) keep them: the file adds to or changes them.

**Export:** every list can be written back as such a file - `GET /api/v1/import/masters/<name>/export` (for example `floors`), or Master configuration › Data and system in the app - and importing that file again changes nothing (every row shows "unchanged"). That is how you copy the lists of one property to another.

### floors.csv (Floors)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Floor code F1, F2 … (the production room board maps room 101 to F1). Capital letters, digits or dash, up to 20 characters. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Floors name the room board. The production board maps room 101 to `F1`, 205 to `F2`, so use codes `F1`, `F2` … for numbered floors.

### amenities.csv (Room amenities)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| icon | optional | Icon shown next to the amenity (blank = none): wifi, snowflake, tv, coffee, briefcase, building, sofa, bath, utensils, bed, car, dumbbell, waves, cigarette-off, accessibility, baby. |
| showOnWebsite | optional | yes to list it on the booking website (blank = yes). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Room types and rooms pick amenities from this list.

### occupancy-charges.csv (Extra-person charges (occupancy))
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| roomType | yes | Code of the room type these charges belong to (one active row per room type). |
| extraAdult | optional | Extra adult per night. Amounts in rupees before GST, up to 2 decimals. The amount in force from the business date is imported; an amount already in effect is never rewritten — a different amount starts from the business date and the old one stays in the history. |
| extraChild | optional | Extra child per night. Same rules as extraAdult. |
| extraBed | optional | Extra bed per night. Same rules as extraAdult. |
| childAgeFrom | optional | Youngest age (years, 0 to 17) charged as a child; blank = no lower limit. |
| childAgeTo | optional | Oldest age (years, 0 to 17) charged as a child; blank = no upper limit. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

One active row per room type. Amounts are dated: the amount in force from today's business date is the one in the file; a different amount starts from the business date and the old one stays in the history.

### meal-plans.csv (Meal plans)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| meals | optional | Meals included, B (breakfast), L (lunch), D (dinner) separated by ; — blank = room only. |
| addOn | optional | Add-on per guest per night. Amounts in rupees before GST, up to 2 decimals. The amount in force from the business date is imported; an amount already in effect is never rewritten — a different amount starts from the business date and the old one stays in the history. |
| gst | yes | GST treatment: bundled (with the room) or separate (restaurant line). Illustrative — CA to confirm. |
| roomTypes | optional | Room type codes the plan is offered for, separated by ; (blank = every room type). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

The GST treatment of a bundled plan follows the room slab - illustrative, CA to confirm. Prices are per guest per night, dated like occupancy charges.

### extras.csv (Extras catalogue (charges))
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| category | yes | transport, laundry, fnb, room or misc. |
| price | optional | Default price. Amounts in rupees before GST, up to 2 decimals. The amount in force from the business date is imported; an amount already in effect is never rewritten — a different amount starts from the business date and the old one stays in the history. |
| gst | yes | GST class: food, laundry, banquet or other (the GST master holds the rates). |
| unit | yes | each, piece, bag, night, trip, hour or person. |
| department | optional | frontdesk, housekeeping, laundry, restaurant, transport, banquet or maintenance (blank = none). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

The catalogue prefills the Charge dialog on the folio; free-text charges stay allowed for Reception.

### discount-reasons.csv (Discount reasons)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| maxPct | yes | Largest discount allowed with this reason, percent of the folio's net charges (0.01 to 100). |
| approvalAbovePct | optional | Above this percent a second person must approve (blank = never; must be below the maximum). |
| roles | optional | Roles that may use the reason, Owner, Manager, Reception separated by ; (blank = every role that may discount). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Percentages are of the folio's net charges. A second person approves above `approvalAbovePct`.

### bank-accounts.csv (Bank accounts and tills)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| kind | yes | till (a cash drawer) or bank. |
| bank | optional | Bank name (required for kind bank; blank for a till). |
| accountLast4 | optional | Last 4 digits of the account number only — HCC never stores more. |
| ifsc | optional | IFSC code, for example HDFC0001234. |
| openingBalance | optional | Opening balance. Setup-only: it is not changed once records settle through the account (blank = 0). |
| defaultFor | optional | Method kinds this account is the default for: upi, card, bank, wallet separated by ; (one default per kind). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Only the last four digits of an account number are stored. The opening balance is setup-only: it is not changed once records settle through the account. One active account is the default for each of UPI, card, bank transfer and wallet.

### payment-methods.csv (Payment methods)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| kind | yes | cash, upi, card, bank, wallet or credit. |
| settlesTo | optional | Code of the bank account or till the money lands in (cash → a till, credit → blank, the rest → a bank account). |
| needsReference | optional | yes when a reference number (UPI id, card slip) must be typed. |
| usableAt | optional | Where it may be taken: frontdesk, pos, expenses separated by ;. |
| surchargePct | optional | Surcharge percent added to the bill (0 to 10; blank = none). |
| ledger | optional | 4-digit ledger of the chart of accounts the method posts to (an asset or liability account; blank = by kind). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Credit methods (vendor credit, company credit) settle to no account and are never taken at the front desk or the outlet. A cash method settles to a till; every other method to a bank account. Import the bank accounts first.

### expense-categories.csv (Expense categories)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| ledger | yes | 4-digit expense ledger of the chart of accounts the category posts to (5000 to 5900). |
| needsBill | optional | yes when a bill / receipt photo must be attached. |
| approvalLimit | optional | Manager approval limit for this category in rupees (blank = the general limit applies). |
| itcBlocked | optional | yes when the GST on these bills can never be claimed (CGST s.17(5): food, catering, health, club, vehicles ≤ 13 seats, construction…); the ITC switch then stays off (DEC-143). |
| rcm | optional | yes for a reverse-charge category (security agency, goods transport, advocate, hired vehicle — DEC-145): the bill carries no GST; HCC posts the GST to 2140. |
| rcmBps | optional | Reverse-charge GST percent without % (e.g. 18; required when rcm is yes). |
| capital | optional | yes for a fixed-asset category: the ledger is then a fixed-asset block 1500 to 1540 and each bill enters the asset register. |
| transport | optional | yes for hire of goods carriages: cash paid to one transporter in a day may reach ₹35,000 (not ₹10,000) before the income-tax deduction is lost. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

The ledger must be an expense account of the chart of accounts (5000 to 5900), or a fixed-asset block (1500 to 1540) for a capital category (ACC-2).

### expense-vendors.csv (Expense vendors)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| type | yes | supplier, utility, laundry or other. |
| gstin | optional | GSTIN, 15 characters (blank = unregistered). |
| tds | optional | yes when TDS is deducted from the vendor's bills. A PAN is an ID number and is never put in a file: type it on the vendor's row in HCC first (Master configuration › Expense vendors); a vendor without a PAN there cannot be set to yes. |
| state | optional | Two-letter GST state code (MH = the hotel's state, CGST + SGST bills; any other state = IGST bills). Blank = MH. |
| tdsSection | optional | rent, contractor, professional or commission — the TDS rate-table row (required when tds is yes). |
| payeeType | optional | individual (individual / HUF) or other — the TDS rate column (blank = from the PAN in HCC, else other). |
| contact | optional | Contact person and phone, up to 100 characters. |
| terms | optional | Payment terms, up to 60 characters. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

A PAN is an ID number and is never put in a file (HCC refuses a `pan` column): type it on the vendor's row in the app. `tds` can be `yes` only for a vendor that already has a PAN there. Expense vendors are separate from the maintenance vendors of the suppliers file.

### menu-categories.csv (Menu categories (POS))
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Menu items pick their category from this list (by code or name).

### pos-tables.csv (Restaurant tables and counters (POS))
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| outlet | yes | Code of the dining outlet the table belongs to. |
| tableNo | yes | Table or counter number as the waiter types it, up to 10 characters (one active row per outlet and number). |
| seats | optional | Seats, 1 to 60 (blank = not recorded). |
| section | optional | Section or area, up to 40 characters. |
| counter | optional | yes for a counter / bar seat rather than a table. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Tables and counters of the dining outlets (import the outlets first). One active row per outlet and table number. Tables named in the pos-outlets file already exist with the code `<outlet>-<table>`.

### booking-sources.csv (Booking sources and segments)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| segment | yes | DIRECT, OTA, CORPORATE, AGENT, GROUP or OTHER. |
| channel | optional | OTA channel or company / agent name (only for segments OTA, CORPORATE, AGENT). |
| commissionBps | optional | Commission percent paid on bookings (0 to 50; blank = 0). Owner-only (money). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Commission is Owner-only (money), so the whole file is committed by the Owner. HCC keeps its built-in sources (walk-in, phone, website, OTA, agent, corporate ...); the file adds to them.

### policies.csv (Booking policies (cancellation, no-show, deposit))
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| freeCancelHours | yes | Hours before arrival up to which cancelling is free, 0 to 720. |
| cancelChargeBps | optional | Cancellation charge percent of the first night(s), 0 to 100 (blank = 0). |
| noShowChargeBps | optional | No-show charge percent, 0 to 100 (blank = 0). |
| depositBps | optional | Advance deposit percent asked at booking, 0 to 100 (blank = 0). |
| sources | optional | Booking-source codes the policy applies to, separated by ; (blank = the default policy for every other source). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Cancellation, no-show and deposit rules. The default policy (no sources) already exists; a file adds policies for named booking sources, each source following one policy.

### id-types.csv (Guest ID types)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| accepted | optional | yes when the hotel accepts this ID at check-in (blank = yes). |
| needsPhoto | optional | yes when a photo of the ID must be taken. |
| foreignerOnly | optional | yes when only foreign nationals use it (passport + visa). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Which IDs the hotel accepts at check-in. Validators stay in the app; the file never carries ID numbers.

### number-series.csv (Document numbering series)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| document | yes | INV tax invoice, BOS bill of supply, CRN credit note, RCP receipt, KOT kitchen ticket, REG registration card, RST restaurant tax invoice. |
| prefix | yes | Printed prefix, 1 to 8 capital letters, digits or dash (GST documents: at most 6). |
| start | yes | First number of the series, 1 to 9,999,999. |
| padding | yes | Digits the number is padded to, 1 to 10 (GST documents: prefix + 6 + digits is at most 16 characters). |
| reset | yes | FY = numbers restart every 1 April (required for GST documents), NEVER = one run. |
| activeFrom | yes | First date the series is used. A series that has issued documents never changes; a new one starts from a date with no documents. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

GST documents (tax invoice, bill of supply, credit note) are sequential with no gaps, restart every financial year and are at most 16 characters - illustrative, CA to confirm. A series that exists is never changed by a file; a new one must start today or later, on a date with no documents of that kind.

### designations.csv (Staff designations)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| dept | optional | Department code (from the departments file); blank = none. |
| grade | optional | Grade, letters, digits, space, '.', '/' or '-', up to 20 characters. |
| defaultPayRule | optional | monthly-structured, monthly-basic or daily (blank = none). Owner-only (pay). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

The department code comes from the departments file. The default pay rule is Owner-only, so the whole file is committed by the Owner.

### shift-patterns.csv (Shift patterns)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Shift code, 1 to 3 capital letters or digits (OFF is reserved). Staff and the roster use it. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| start | yes | Duty start, 24-hour HH:mm. |
| end | yes | Duty end, 24-hour HH:mm (a shift past midnight must be marked night). |
| breakMinutes | optional | Unpaid break in minutes, 0 to 240 (blank = 0). |
| night | optional | yes for a night shift (ends after midnight). |
| weeklyOffRule | optional | fixed (a set weekly-off day) or roster (set in the roster); blank = fixed. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Staff, the roster and attendance use the shift code. A shift that ends after midnight must be marked night.

### leave-types.csv (Leave types)
| Column | Required | Meaning |
|---|---|---|
| code | yes | SCC’s fixed leave categories: CL, SL, EL, ML, CO, OTH and LOP (leave without pay). No other code. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| paid | optional | yes for paid leave, no for unpaid (blank = no). Fixed: only LOP (leave without pay) is unpaid. Owner-only (pay). |
| yearlyQuota | optional | Days per financial year for CL, SL and EL only, 0 or more, halves allowed (blank = not tracked). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

SCC’s leave categories (DEC-116, migration 4110): the list is fixed — names can change; paid is fixed (only LOP unpaid); a yearly quota only for Casual, Sick and Earned leave (SCC TRACKED_CATS, defaults 12 · 7 · 15). No carry-forward or encashment (SCC has neither). Owner-only.

### holidays.csv (Holiday calendar)
| Column | Required | Meaning |
|---|---|---|
| code | yes | The holiday date, YYYY-MM-DD (any common date format is accepted). One holiday per date. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| kind | yes | paid (counts like a weekly off in payroll) or restricted. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

The holiday calendar used by payroll day counts and compliance due dates; a paid holiday that is not worked counts like a weekly off for monthly staff (DEC-083). Owner-only.

### hk-task-types.csv (Housekeeping task types)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| appliesTo | yes | What it serves: checkout, move, stayover, inspection, deep separated by ; (each kind belongs to one active type). |
| standardMinutes | yes | Standard minutes for one task, 5 to 240 (feeds attendant load and board timing). |
| needsInspection | optional | yes when a finished clean waits for the supervisor (never for a stayover service). |
| checklist | yes | Checklist items separated by ; (1 to 8 items, each up to 60 characters). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Each task kind belongs to one active type, so a file normally changes the existing types (departure clean, stayover, deep clean, inspection) rather than adding new ones.

### hk-zones.csv (Housekeeping zones)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| floors | optional | Floor codes in the zone separated by ; (a floor belongs to one active zone). |
| rooms | optional | Extra room numbers in the zone separated by ; (a room belongs to one active zone). |
| defaultAttendant | optional | Sign-in name of the default attendant (the display name works too); blank = none. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

A floor or room belongs to one active zone. The attendant is the sign-in name from the staff file.

### complaint-categories.csv (Complaint categories)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| department | yes | Department that normally handles it: Housekeeping, Maintenance or Front desk. |
| slaHours | yes | Answer or resolve within this many hours, 1 to 720. |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

Used by guest services and the complaints reports; the SLA is in hours.

### lost-found-categories.csv (Lost-and-found categories)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Short code: capital letters, digits or dash, up to 20 characters. Never changes once the row is used; the row is found again by it on a re-import. |
| name | yes | Name shown on screens and documents, 1 to 60 characters. Another active row may not have the same name. |
| department | yes | Department that handles it: Housekeeping, Maintenance or Front desk. |
| slaHours | yes | Return or follow up within this many hours, 1 to 720. |
| valuable | optional | yes for a valuable (held in the front-office safe; disposal needs a manager). |
| sortOrder | optional | Order on screens (10, 20 …). Blank keeps the row's order, or puts a new row last. |
| active | optional | yes or no (blank keeps the row's state, a new row is active). A file cannot retire an active row — retire it on its list, where its use is checked. |

A valuable item is held in the front-office safe and disposed of only with a manager.

## Limits

- One file = one kind of master; up to 5,000 rows and 2 million characters per file.
- Excel's semicolon-separated CSV (some regional settings) is also read.
- Every upload and commit is shown in `GET /api/v1/import/batches`; committed changes are in the audit trail.


## LF front-desk lists

`purposes.csv` (purposes of visit) and `vehicle-types.csv` need nothing first; both are Owner or Tech admin lists. The legacy names come from `tools\legacy\extract.py --purposes --vehicle-types` (names only, no guest data).
