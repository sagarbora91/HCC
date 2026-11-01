# ACC3-TALLY DONE

Lane acc3-tally · Sonnet 5.5 (written by) · branch `claude/acc3-tally` · worktree `E:\Projects\HCC\worktrees\acc3-tally` · base 829f0d91 (= `claude/acc3`). Contract: `ACC3-REPORTS-CONTRACT.md` §4, §9, §10.

## Commits
`git log --oneline 829f0d91..HEAD` in the worktree (first: ec28127f "masters + vouchers XML, golden April 2026, log migration 4266, tests"; later commits fix tests / this file).

## Files
- Server: `Hcc.Domain/Accounting/VoucherExport.Tally.cs` (TallyBook, TallyBills, `TallyMastersXml`, `TallyVouchersXml`, `TallyVoucherType`), `Hcc.Domain/ThirdParty/Etp/EtpTallyXml.cs` (adapted from ETP, provenance entry `ETP-TALLY` in `third-party/source-reuse.json`), `Hcc.Application/Accounting/TallyQueries.cs`, `Hcc.Infrastructure.SqlServer/Accounting/SqlTallyStore.cs` (log + GSTIN reader), `Hcc.Api/Endpoints/AccountingTallyEndpoints.cs` + one `// ACC-3` line in `HccComposition.cs`, migration `4266_acc_tally_file_log.sql` (+ `Hcc.slnx` line, `migrations.lock.json` appended with `--accept-out-of-order`, because the lock is already at 4605 and the contract reserves 4266).
- Prototype: `src/domain/streams/accounting-tally.ts` (+ one export line in `services/mock/index.ts`, `// ACC-3` block).
- Tests / golden: `tests/Hcc.Accounting.Tests/Tally/TallyExportTests.cs`, `Golden/tally-april-2026.{input.json,masters.xml,vouchers.xml}`, `Golden/build-tally-golden.mjs` (regenerates all three from the ACC-RULEBOOK scenario), `03 Prototype/tests/stream-accounting-tally.mjs`; `Hcc.Accounting.Tests.csproj` gains `Golden\*.xml` copy (outside the ownership table, needed).

## What was built
- `GET /accounting/tally/masters?fy=2026` (or `2026-27`) and `GET /accounting/tally/vouchers?from&to` (PERM-ACC-006, Owner + Accountant, `AccountingPermissions.Tally`; contract text said 005, corrected by the coordinator). Views carry `xml`, `fileName`, `sha256`, `bytes`; each file is logged in `acc.tally_file` (kind, range, counts, sha256, provisional, who, when). The old `GET /accounting/export/tally` and its DEC-077 log stay; the new vouchers export writes that same per-date log too (clears re-export flags), same notes (re-export, reopened, pending, legacy).
- Masters: Owner sub-groups, every chart ledger, every party sub-ledger (Tally name = party display name, ` (2)` on a clash), opening balance at the FY start in Tally sign (Cr +, Dr -), bill-wise on party ledgers under Sundry Debtors / Creditors (opening kept as one bill "Opening balance" dated the day before the FY), GSTIN on companies / vendors. Profit & loss ledgers carry no opening; earlier years' net result goes to `Profit & Loss A/c` (ACTION Alter). Provisional note when the opening entry is not approved; re-export note on a repeat.
- Vouchers: REMOTEID = GUID = `VoucherExport.RemoteId`, voucher-type mapping (Receipt, Payment, Contra incl. cash/bank journals, Sales, Purchase, Credit Note sub `credit-note`, Debit Note = Purchase sub `return`, else Journal), `ALLLEDGERENTRIES.LIST` with bill allocations oldest-first over the financial year (New Ref = voucher number, Agst Ref, On Account). Opening journal never in a vouchers file. Written with `XmlWriter` (UTF-8 no BOM, CRLF, one-space indent); prototype imitates it byte for byte.
- Golden: 239 vouchers, 44 party ledgers; the prototype test and the C# test both rebuild the files from the shared `input.json` and compare bytes; the prototype also rebuilds them straight from the scenario state.

## Tests
Prototype: `npx tsc --noEmit` clean; `npm test` 2105/2105 (10 new `[ACC3-TALLY]`). Server: `Hcc.Accounting.Tests` 254/254 (19 new in `Tally/TallyExportTests.cs`, incl. golden byte equality for masters and vouchers, chart parity, REMOTEID, bill allocation, real engine month). Not run: other server projects, `dotnet build Hcc.slnx` (only Accounting.Tests + its references built). Commands: `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1` in `08 Production`, `node tests/_one.mjs tests/stream-accounting-tally.mjs` / `npm test` in `03 Prototype`, all through `Invoke-Serialized.ps1`.

## Owner: import into a fresh TallyPrime company (gate step)
1. Create a new empty company in TallyPrime named exactly as the company shown in the files (golden: `Rulebook Hotels Pvt Ltd (fictional)`), financial year starting 1-4-2026, books beginning 1-4-2026; GST can stay off (GST ledgers import as plain duty ledgers).
2. Open the company. Go to Import > Masters (Alt+O > Import > Masters), give `E:\Projects\HCC\worktrees\acc3-tally\08 Production\tests\Hcc.Accounting.Tests\Golden\tally-april-2026.masters.xml`, set "Behaviour of import if master exists" to Combine/Ignore and press Enter. Expect about 114 masters created (no Owner sub-groups in this month), 0 errors. Check Sundry Debtors: Deccan Agro Ltd shows 40,000 Dr as bill "Opening balance" (and Trial Balance opening = 77,40,000 each side).
3. Import > Transactions, give `...\tally-april-2026.vouchers.xml`, set "Overwrite vouchers when a voucher with same GUID exists" to Yes. Expect 239 vouchers, 0 exceptions. Import it a second time: still 239, nothing doubled.
4. Check: Day Book April 2026 has 196 Sales, 19 Receipt, 9 Payment, 2 Purchase, 2 Contra, 11 Journal; Bill-wise outstandings of Deccan Agro nil after the receipts; Trial Balance debit = credit.
5. Tell the coordinator what Tally says if anything is refused. Points I could not test without Tally: the `BILLALLOCATIONS.LIST` inside a ledger master (NAME/BILLDATE/OPENINGBALANCE), the `ACTION="Alter"` of `Profit & Loss A/c` (only written when earlier years have a result, not in the golden), `On Account` bill NAME.

## Open items
- Allocation here is oldest-first only; acc3-ageing's explicit vendor allocations (ACC-2) are not read (integration: feed them into `TallyBills`).
- Company name = property name (`PropertyNameAsync`); there is no `brand.legalName` setting in `BuiltinSettings.cs` (only `brand.gstin`, `brand.address`). Integration: add the setting (Owner) and pass it to `TallyQueries`.
- GST ledgers are plain duty ledgers (no TAXTYPE); CA/Tally setup of GST registration stays manual.
- VCHKEY is deliberately not written (VoucherExport rule: Tally's own key is never invented); the contract template lists it.
- A range spanning two financial years allocates bills from the first FY's start; the masters of the second FY are imported into its own company/year.
- A re-export date before the range's FY start is not carried.
- No SQL test for `acc.tally_file` / `SqlTallyStore` yet.

## Integration needed
- `HccComposition.cs`: my `// ACC-3` block is one line; other lanes append inside it.
- Prototype UI (acc3-ui) calls `accTallyMasters(s, fy)` / `accTallyVouchers(s, from, to)` returning `{xml,fileName,bytes,error?,...}`; download with `saveText`.
- Run `build-tally-golden.mjs` again if any builder changes, then both test suites must stay green.

## HANDOFF bullet
ACC-3 tally: Tally masters per FY + vouchers per range with bill allocations (server XmlWriter + prototype mirror, byte-identical golden April 2026 files in `Hcc.Accounting.Tests/Golden`), `acc.tally_file` log (migration 4266), endpoints `/accounting/tally/{masters,vouchers}`; owner imports the golden files into a fresh Tally company at the gate (steps in `ACC3-TALLY-DONE.md`).
