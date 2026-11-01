# ACC-3 reports and Tally contract (wave `claude/acc3`, 1 Nov 2026)

The shared shapes every ACC-3 lane builds against. **Copy, never reinterpret.** A lane that needs a change writes it in its DONE file; the coordinator amends this file and tells the other lanes. Sources of truth: `02 Planning/HCC_Accounting_Rulebook_v1.md` §7.4–7.10, §8.4–8.6 (the golden month), §10 ACC-3; `ACC1-POSTING-CONTRACT.md` (chart, parties, statements); `ACC2-POSTING-CONTRACT.md` (voucher types, GST month, TDS); DEC-140 (HCC prepares, Tally files); `05 Delivery Evidence/Accounting/ACC2-DONE.md` §5 (carry-overs). The wave also carries **DEC-193** (one ID per guest in the party, photo optional) as lane `partyid`.

## 1. One posting read for every report

Every report reads the posting table through the existing readers: prototype `accRptVouchers(s, from, to)` → `{opening, period}` (`src/domain/streams/accounting-reports.ts`), server `AccountingReportQueries` over `acc.voucher` / `acc.entry` (`SqlAccountingStore.Reports.cs`). Reports never read source documents directly; a figure a report needs that the postings do not carry is a defect in the postings (write it in the DONE file). Money = integer paise; display through `rupees()` / `MoneyMath`. Ledger codes and groups come from the effective chart (`accAccount`, `EffectiveChart`): a report never hard-codes a ledger name, only codes from this contract.

Date range = IST business dates, inclusive; a report that spans a locked and an open period marks itself **provisional** (same rule as the existing trial balance: `accRptOpeningPending`, locked snapshots).

## 2. Profit & loss (rulebook 7.4) — lane acc3-pl

```ts
export type ACCPlLine = {code:string; name:string; paise:number; group:string};          // paise signed as the ledger's balance movement (income positive)
export type ACCPl = {
  from:string; to:string; provisional:boolean;
  revenue:{lines:ACCPlLine[]; total:number};          // 4000, 4100, 4200, 4300 minus 4900, 4910 (discounts and credit notes shown as negative lines)
  otherIncome:{lines:ACCPlLine[]; total:number};      // 4950, 4960
  expenses:{consumption:ACCPlGroup; employee:ACCPlGroup; finance:ACCPlGroup; depreciation:ACCPlGroup; other:ACCPlGroup; total:number};
  profitBeforeTax:number;                             // revenue.total + otherIncome.total − expenses.total
  byTallyGroup:{group:string; name:string; paise:number}[];   // second view: the same figures summed by Tally group (contract ACC-1 §1)
};
export type ACCPlGroup = {name:string; lines:ACCPlLine[]; total:number};
```
Buckets by code: consumption 5000, 5010, 5015 · employee 5100, 5110, 5120 · finance 5080, 5090 · depreciation 5200 · other 5020–5070, 5300, 5400, 5900 (and any Owner-added expense ledger by its Tally group: Direct Expenses → consumption, Indirect Expenses → other). A ledger with no bucket goes to `other` with a warning. No tax line (proprietor). Golden: April 2026 profit **₹1,40,400** (rulebook §8.4).

**Balance sheet** (7.5, optional HCC view, marked "not the statutory statement"):
```ts
export type ACCBalanceSheet = {asOf:string; provisional:boolean;
  liabilities:{ownersFunds:ACCBsBlock; nonCurrent:ACCBsBlock; current:ACCBsBlock; total:number};
  assets:{nonCurrent:ACCBsBlock; current:ACCBsBlock; total:number};
  balanced:boolean; suspense:number; openingDifference:number};      // 9999 and 1400 must be 0 or provisional = true
export type ACCBsBlock = {name:string; lines:{code:string; name:string; paise:number}[]; total:number};
```
Owner's funds = 3100 + current-year profit (P&L from 1 April to `asOf`) − 3000; Duties & Taxes = 2110–2150 + 2310–2330 net of 1310–1350 (net Dr → current asset "GST input credit"); fixed assets at WDV from `accounting-assets` (`accAssetWdv(s, asOf)`); Sundry Debtors = 1100, 1110, 1115, 1120 parties; Loans & Advances = 1200, 1210, 1360, 1370. Golden: 30 April 2026 both sides **₹79,27,900**.

**Cash-ratio report** (7.9): `{fy, cashReceipts, totalReceipts, cashPayments, totalPayments, receiptsPct, paymentsPct, flagged}` — cash = entries on 1000 / 1005; totals = all receipts / payments vouchers; `flagged` when either ratio > 5 % (bps 500).

Server: `GET /accounting/pl?from&to`, `GET /accounting/balance-sheet?asOf`, `GET /accounting/cash-ratio?fy` (PERM-ACC-003 as the trial balance). Contracts `PlView`, `BalanceSheetView`, `CashRatioView` in `Hcc.Contracts/Accounting/AccountingReportContracts.cs` with the same fields (PascalCase).

## 3. Outstanding with ageing (7.6) — lane acc3-ageing

```ts
export type ACCAgeingRow = {account:string; party:string; name:string; control:string;
  bills:ACCAgeingBill[]; open:number; b0_30:number; b31_60:number; b61_90:number; b90:number};
export type ACCAgeingBill = {ref:string; date:string; due:string; amount:number; paid:number; open:number; days:number};
export type ACCAgeing = {asOf:string; side:'receivable'|'payable'; rows:ACCAgeingRow[]; totals:{open:number; b0_30:number; b31_60:number; b61_90:number; b90:number}};
```
Receivables = parties under 1100 / 1110 / 1115 / 1120 (company accounts, OTA channels, Udhaar customers); payables = 2200 vendors. **Bill-wise allocation:** each Dr entry on a receivable party (Cr on a payable party) is a bill `ref` = the voucher's source key; payments allocate **oldest-first** against open bills of that party (ACC-2 contract §3 for vendors; DEC-172: the opening balance is the oldest open item `{ref:'opening'}`); an explicit allocation recorded by ACC-2 (`vendorPayments[].allocations`) wins over oldest-first. `days` = asOf − bill date; buckets 0–30 / 31–60 / 61–90 / 90+. Totals tie to the party's ledger balance on `asOf` or the row is flagged.

**GST summary** (7.7): `{month, output:{rate5, rate18, b2b, b2cs, advances11A, advances11B, creditNotes}, itc:{cgst, sgst, igst}, rcm, setoff (ACC-2 accGstSetoff), cashPaid, tcsCredit, cashLedgerBalance (1340), ties:{gstr1:boolean; gstr3b:boolean; differences:string[]}}` — `gstr1` ties to the existing `gstr1Summary(s, from, to)` (`src/domain/gstr1.ts`) section totals; `gstr3b` ties to ACC-2's GST month (`accGstMonth`). Server: `GET /accounting/ageing?asOf&side`, `GET /accounting/gst/summary?month`.

## 4. Tally XML (rulebook §3.1, §7; DEC-140; ETP-reuse spot 21) — lane acc3-tally

One file per export = Tally's "Import Data" envelope, UTF-8, Windows line ends, importable into a **fresh TallyPrime company** without edits (the gate test). Reuse `Hcc.Domain/Accounting/VoucherExport.cs` (`RemoteId`, `XmlEscape`, `TallyRangeError`, the `ISDEEMEDPOSITIVE` convention: debit = `Yes` with a negative amount, credit = `No` with a positive amount) and ETP's exporter pieces where they fit (`E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f\src\Etp.Reporting.Infrastructure.SqlServer\AccountingAndSharingServices.cs`, provenance entry in `08 Production/third-party/source-reuse.json`, DEC-054).

```xml
<ENVELOPE>
 <HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER>
 <BODY><IMPORTDATA>
  <REQUESTDESC><REPORTNAME>All Masters</REPORTNAME><STATICVARIABLES><SVCURRENTCOMPANY>{company}</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
  <REQUESTDATA>
   <TALLYMESSAGE xmlns:UDF="TallyUDF"><GROUP NAME="{group}" ACTION="Create"><NAME.LIST><NAME>{group}</NAME></NAME.LIST><PARENT>{parent or empty for Tally's primaries}</PARENT><ISSUBLEDGER>No</ISSUBLEDGER></GROUP></TALLYMESSAGE>
   <TALLYMESSAGE xmlns:UDF="TallyUDF"><LEDGER NAME="{ledger}" ACTION="Create"><NAME.LIST><NAME>{ledger}</NAME></NAME.LIST><PARENT>{group}</PARENT><ISBILLWISEON>{Yes for party ledgers}</ISBILLWISEON><OPENINGBALANCE>{signed, Tally sign: Cr positive, Dr negative}</OPENINGBALANCE>{<GSTREGISTRATIONTYPE>…</GSTREGISTRATIONTYPE><PARTYGSTIN>…</PARTYGSTIN> for companies / vendors with a GSTIN}</LEDGER></TALLYMESSAGE>
  </REQUESTDATA>
 </IMPORTDATA></BODY>
</ENVELOPE>
```
Masters file: Tally's 28 primary groups are **not** created (they exist in every company); Owner sub-groups (ACC-1 contract §1, `parent` set) and every ledger of the effective chart + every party sub-ledger (`<control name>/<party name>` → Tally ledger name = the party's display name, group = the control's Tally group; two parties with one name get ` (2)`), opening balances as at the FY start from the approved opening entry.

Vouchers file (`REPORTNAME` = `Vouchers`): one `<VOUCHER REMOTEID="…" VCHKEY="…" VCHTYPE="{Receipt|Payment|Contra|Journal|Sales|Purchase|Credit Note|Debit Note}" ACTION="Create" OBJVIEW="Accounting Voucher View">` per HCC voucher: `<DATE>yyyyMMdd</DATE>`, `<GUID>` = `REMOTEID` (`VoucherExport.RemoteId(identity, sourceKey)`, identity from `acc.tally_identity`), `<VOUCHERTYPENAME>`, `<VOUCHERNUMBER>` = HCC voucher no., `<NARRATION>`, `<PERSISTEDVIEW>Accounting Voucher View</PERSISTEDVIEW>`, then one `<ALLLEDGERENTRIES.LIST>` per entry with `<LEDGERNAME>`, `<ISDEEMEDPOSITIVE>`, `<AMOUNT>` (rupees with two decimals, debit negative) and, on a party ledger, `<BILLALLOCATIONS.LIST><NAME>{bill ref}</NAME><BILLTYPE>{New Ref|Agst Ref}</BILLTYPE><AMOUNT>…</AMOUNT></BILLALLOCATIONS.LIST>` (New Ref on the invoice / purchase entry, Agst Ref on receipts / payments allocated per §3; `On Account` when nothing allocates). Voucher-type mapping: receipt kinds → Receipt, payments / vendor payments / remittances → Payment, cash↔bank → Contra, invoices → Sales, purchases → Purchase, credit notes → Credit Note, purchase returns → Debit Note, everything else → Journal. Range ≤ 31 days per file (existing `TallyRangeError`), provisional / re-export notes as today (`ReexportNote`, `PendingNote`). Company name = Master configuration `brand.legalName` (Owner), `SVCURRENTCOMPANY` only when set.

Golden file: the rulebook April 2026 month exported from the `ACC-RULEBOOK` scenario → `tests/Hcc.Accounting.Tests/Golden/tally-april-2026.masters.xml` + `.vouchers.xml`, byte-stable (sorted by date, voucher no.); the gate imports them into a fresh Tally company on the owner's PC (owner step, W3 gate adds) — the lane tests only well-formedness, counts, Σ Dr = Σ Cr per voucher, the REMOTEID rule and the golden bytes.

Server: `GET /accounting/tally/masters?fy`, `GET /accounting/tally/vouchers?from&to` (PERM-ACC-005 export, as today's `TallyAsync`); prototype `accTallyMasters(s, fy)`, `accTallyVouchers(s, from, to)` in a new `src/domain/streams/accounting-tally.ts`; download through `native.ts` `saveCsv`-style save (`saveText(name, mime)` — add it to `native.ts` if missing, lane acc3-exports owns that one function).

## 5. CSV and Excel exports (7.2, ETP-reuse spots 19 / 22) — lane acc3-exports

Every report of §2–§3 and the ACC-1 statements export as CSV (existing `*Csv` builders, formula-guarded cells `CsvFormula.Neutralise`) and **Excel**: prototype through `src/app/xlsx.ts` (one sheet per report, header row bold, money as numbers with `#,##0.00`, dates as dates, a title row with the range and "provisional" when set); server through the OpenXML exporter borrowed from ETP (`Etp.Reporting.Reporting/OpenXmlReportExporter.cs`, provenance in `source-reuse.json`, no new NuGet package beyond what ETP's exporter needs — if it needs `DocumentFormat.OpenXml`, add it to `Hcc.Infrastructure` with the lock-file update and say so). **Bulk**: `GET /accounting/export/statements.zip?from&to&parties=all|receivable|payable` = one CSV (or xlsx with `&format=xlsx`) per party, zipped (`System.IO.Compression`); prototype builds the same zip with the existing JSZip-free approach: a plain "store" zip writer in `src/app/zip.ts` (no package). File names `<property code>-<report>-<from>-<to>.<ext>`.

## 6. Screens (lane acc3-ui)

`src/features/accounting/Accounting.tsx` gains tabs **P&L** (range, Tally-group toggle, provisional banner), **Balance sheet** (as-of date, "not statutory" note), **Outstanding** (receivable / payable toggle, as-of, bucket columns, expand a party to its bills), **GST summary** (month, tie badges), **Cash ratio** (FY), **Tally export** (masters per FY, vouchers per range ≤ 31 days, notes shown), **Exports** (CSV / Excel buttons on every tab, bulk zip). Phone: cards per party / per P&L bucket, no sideways scroll at 360 px, 48 px targets. Money masked for roles without PERM-ACC-003 exactly as the trial balance today. Desktop and phone pages registered; `navmap.ts` keywords.

## 7. ACC-2 carry-overs (lane acc3-carry; ACC2-DONE §5)

| # | Item | Rule |
|---|---|---|
| C1 | Asset put-to-use and disposal (F4) | `ACC_ASSET_PUT_TO_USE {asset, date}`, `ACC_ASSET_DISPOSE {asset, date, proceeds, method}`; depreciation from the put-to-use date (≥ 180 days full, else half, rulebook F2); disposal posts proceeds Dr bank/cash · Cr block, profit/loss on sale to 4960 / 5400 |
| C2 | Purchase return against a capital purchase | reduces the asset's cost in the register (`ACCAsset.cost`, server `acc.asset`) |
| C3 | Re-allocate an existing vendor payment | `EXP_VENDOR_PAY_REALLOCATE {payment, allocations[]}` (Owner / Manager / Accountant), audit row, ageing follows |
| C4 | Vendor GST state vs GSTIN | validation: the GSTIN's first two digits must equal the vendor's state code (DEC-136: CGST + SGST only, so a different state is a warning, not a refusal) |
| C5 | Udhaar cash limit per customer (B9) | server totals per customer per day, not per credit |
| C6 | TDS remittance per sub-section | split 194-I(a)/(b), 194J(a)/(b) on the remittance and the compliance card |
| C7 | Form 168 figures | stored on the TDS remittance (`acc.tds_remittance.form168_json`) |
| C8 | Rulebook B11 | loans are received and repaid only through a bank ledger: a cash leg on 2400 is refused |
| C9 | Server GST set-off live | `MonthAsync` recomputes until the month lock, as the prototype does; the pay-run blocker shown in the month view |
| C10 | Dated GST-on-advances switch | `cmp.profile.gst_on_advances_from DATE NULL`; the switch applies to advances dated on or after it |
| C11 | Dated TDS rates in the production configuration screen | load `acc.tds_rate` versions into `configX.changes` (display) |

Migrations 4270–4276; prototype + server; tests for each.

## 8. Party-member IDs (DEC-193) — lane partyid

Today an ID belongs to `gst.guest` only; party members are `gst.guest_party(seq, name, age_band, relation)`. New rule: **every guest on a registration has their own ID; the photo is optional everywhere.**

```sql
-- migration 4620
ALTER TABLE gst.guest_party ADD id_type NVARCHAR(40) NULL, id_last4 VARCHAR(4) NULL, id_required BIT NOT NULL CONSTRAINT DF_guest_party_id_required DEFAULT 1;
CREATE TABLE gst.guest_party_id_secret(guest_id BIGINT NOT NULL, seq TINYINT NOT NULL, key_id VARCHAR(40) NOT NULL, sealed VARBINARY(64) NOT NULL, created_utc DATETIME2(3) NOT NULL, updated_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_guest_party_id_secret PRIMARY KEY (guest_id, seq), CONSTRAINT FK_guest_party_id_secret FOREIGN KEY (guest_id, seq) REFERENCES gst.guest_party(guest_id, seq));
-- doc.id_capture gains party_seq TINYINT NULL (NULL = the registered guest); photo capture is optional: no rule anywhere requires a photo
```
Rules: `id_required` = 1 for `Adult` and `Senior`, 0 for `Child` / `Infant` (Owner to confirm children — OP-21; default no); registration / check-in (`REGISTER`, `CHECKIN`, server `RegisterGuest`, `CheckInStay`) is refused with `VALIDATION` "ID missing for <name>" while any party member with `id_required` has no `id_last4`; the same per-type validation, sealing (`GuestIdProtector`, associated data `HCC-GUESTID-v1|property|guest|seq|keyId`), masking (`XXXX-XXXX-1234` for PERM-GST-003), reveal audit (`GUEST_ID_VIEW` with the seq) and retention (DEC-071) as the registered guest's ID; ID numbers never in logs / events / CSV. Contracts: `GuestPartyDto` gains `IdType?, IdNumber? (write only), IdLast4?, IdMasked?`; prototype `guests[].party[]` gains `idType, idLast4, idNumber?` (optional fields, **no SCHEMA bump**). Screens: `GuestForm.tsx` party rows get ID type + number (desktop and phone), the check-in sheet shows a red "ID missing" chip per member; the photo field label says "optional". Form C unaffected. Legacy import: the registered guest only (as built). Tests both sides. Migration block 4620–4624; permission reuse PERM-GST-002 / 003.

## 9. Permissions

No new codes except none: reports use PERM-ACC-003 (view) and PERM-ACC-005 (export) as the trial balance and today's Tally export; carry-over commands reuse ACC-2's codes (`EXP_VENDOR_PAY_REALLOCATE` under PERM-EXP-004; asset commands under PERM-ACC-004 Owner / Accountant).

## 10. State, schema, migrations

- **No SCHEMA bump**: P&L, balance sheet, ageing, GST summary, cash ratio and Tally are selectors; carry-overs add optional fields only (`ACCAsset.putToUse?`, `disposal?`, `ExpVendorPayment.allocations` exists; `CMPProfile.gstOnAdvancesFrom?`). The coordinator bumps SCHEMA at integration only if a persisted shape changed.
- Append-only blocks `// ACC-3` in `handlers/index.ts`, `services/mock/index.ts`, `clients/sync/registry.ts`, `HccComposition.cs`.
- Migrations: pl 4260–4261 (views only if needed) · ageing 4262–4265 (bill allocation snapshot table if needed) · tally 4266–4269 (`acc.tally_export` log) · exports — · carry 4270–4276 · partyid 4620–4624. `migrations-lock.mjs --add` + `Hcc.slnx` per migration.
- Tests: prototype `tests/stream-accounting-<lane>.mjs`; server `tests/Hcc.Accounting.Tests/<Lane>/` (+ `Hcc.Stays.Tests` / `Hcc.GstIds.Tests` for partyid). Golden month figures: rulebook §8.4–8.6 on both sides (lane acc3-tests).
