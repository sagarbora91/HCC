# Importing your hotel's masters and opening balances

This folder holds the files you fill in once, when HCC starts, so you do not have to type your rooms, rates, staff and
accounts by hand.

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

## Upload → check → commit

1. **Upload** a file (command `UploadImportBatch`, choosing which master it is). HCC keeps the file and checks every row at once.
2. **Check the report.** Each problem shows the spreadsheet row, the column and what to fix (for example "Row 7 · rate · '42OO' is
   not an amount in rupees"). The preview lists what will be **created**, **updated** or left **unchanged**. Nothing is saved yet.
   Fix the file in Excel and upload it again until there are no errors.
3. **Dry run** (optional): runs the whole import and then undoes it, to prove it will go through. Nothing is kept.
4. **Commit**: everything in the file is saved in one go – either all rows or none. It is recorded in the audit trail with your name.
   Committing the same batch twice is refused; importing the same file again changes nothing (every row shows "unchanged").

The Technical administrator may upload, check and dry-run every file, but only the Owner can commit (money, rates, roles and
accounts are Owner-only, DEC-058).

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

## Limits

- One file = one kind of master; up to 5,000 rows and 2 million characters per file.
- Excel's semicolon-separated CSV (some regional settings) is also read.
- Every upload and commit is shown in `GET /api/v1/import/batches`; committed changes are in the audit trail.
