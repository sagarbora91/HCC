# Importing your hotel's masters and opening balances

This folder holds the files you fill in once, when HCC starts, so you do not have to type your rooms, rates, staff,
accounts, departments, company clients, suppliers and stock items by hand. You load them with the script
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

### pos-outlets.csv (restaurant & room service)
| Column | Required | Meaning |
|---|---|---|
| outletCode | yes | Outlet code, letters/digits/dash, up to 20 characters (stored in capitals), e.g. `REST`. The menu file uses it. |
| outletName | yes | Name shown on the POS screen and on bills (up to 100 characters). |
| kind | yes | `table` for a dining outlet with tables, `room` for room service (charged to the guest's room). |
| tables | for kind table | Table names separated by `;` (each up to 10 characters), e.g. `T1;T2;T3`. Blank for kind `room`. |

An outlet that already exists with the same details is "unchanged"; one that exists with different details is refused (change it
on the POS setup screen). Nothing here posts any money.

### pos-menu.csv (restaurant & room-service menu)
| Column | Required | Meaning |
|---|---|---|
| itemCode | yes | Item code, letters/digits/dash/underscore, up to 20 characters (stored in capitals). |
| itemName | yes | Name shown on the POS screen and on bills (up to 100 characters). |
| category | yes | Menu category, e.g. `Main course`, `Beverages` (up to 60 characters). |
| price | yes | Selling price in rupees **before GST** (0 is allowed). GST is added by HCC from the GST master (restaurant 5 %). |
| veg | yes | `yes` for vegetarian, `no` for non-vegetarian. |
| outlets | optional | Outlet codes that sell the item, separated by `;`. Blank = every outlet already in HCC. Import the outlets file first. |

An item already in HCC (same code) is **updated** when its name, category, price, veg flag or outlets differ, and "unchanged"
otherwise. Items sold out ("86") keep their availability; it is changed on the POS screen.

### departments.csv (staff and payroll)
| Column | Required | Meaning |
|---|---|---|
| code | yes | Department code, 2 to 10 capital letters or digits (e.g. `FO`). Staff are put in a department by this code. |
| name | yes | Department name, 2 to 60 characters. |
| leaveCap | optional | Most staff of this department on leave on one day, 1 to 20. Blank = the leave policy default. |

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

## Limits

- One file = one kind of master; up to 5,000 rows and 2 million characters per file.
- Excel's semicolon-separated CSV (some regional settings) is also read.
- Every upload and commit is shown in `GET /api/v1/import/batches`; committed changes are in the audit trail.
