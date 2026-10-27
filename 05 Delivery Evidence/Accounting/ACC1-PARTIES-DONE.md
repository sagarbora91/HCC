# ACC1-PARTIES — DONE (lane acc1-parties, branch `claude/acc1-parties`, 27 Oct 2026)

Party sub-ledgers derived from the masters, vouchers posting to `<control>/<party>`, the `no-party` warning, the summary / detail trial
balance, the Udhaar credit raised as a sale (rulebook A17, DEC-144) and the master retire guard — prototype and server.
Contract: `02 Planning/agents/ACC1-POSTING-CONTRACT.md` §3, §4, §8, §9.

## Commits (base `39930edc`; wave base merged at `83be423d`)

| commit | what |
|---|---|
| `ae79fbc2` | prototype: `accounting-parties.ts`, vouchers / TB / exceptions / dates / reconcile / CSV / Tally in `accounting.ts`, Udhaar `kind` + `gstBps`, company payments, retire guard, tests |
| `78f9ee76` | server (WIP commit after the PC restart): domain, mapper, projector, store, Udhaar raise command + event, migrations 4210 / 4211 / 4215, golden regenerated; sync sends `kind` / `gstBps` |
| `0c86780d` | server tests `Domain/PartyLedgerTests.cs`, `Sql/PartyLedgerSqlTests.cs` |
| `aace26ac` | merge `claude/acc1` (SCHEMA 11 re-pins) |
| `79f8c200` | existing server accounting tests follow the party ids; one order-dependent Sql test date moved |
| (this file) | DONE file |

## Files

**Prototype (`03 Prototype`)**
- new `src/domain/streams/accounting-parties.ts` — `ACC_PARTY_CONTROLS`, `accPartyLedgers(s)`, `accPartyName`, `accIsParty`, `accIsControl`, `accControlOf`, `accPartyIdOf`, `accPartyLedgerId`, `accUdhaarPartyId`, `accPartySlug`, `accBankOf`, `accPartyFor(s, control, ctx)` (the per-source resolvers of §3), `accPost`. Leaf module (type imports + the money-master leaf only).
- `src/domain/streams/accounting.ts` (my sections) — `accLiveVouchers` posts every control entry through `accPost` (folio receipts: tender + company; POS: tender; expenses: funding + vendor; Udhaar payment: tender + customer; cash out: till / bank of the slip; cash difference: till); **new sources**: Udhaar credit raised (`udhaar:<id>`, Sales, sub `udhaar-credit`) and company payments (`company-payment:<id>`, Receipt, sub `company-payment`, parity with the server's `CompanyPaymentRecorded`); `accTrialBalance(vs, accounts?, detail=false, s?)` (summary sums parties into controls; detail = a row per party with `control` set); `ACCTBRow.control?`; `accEntryName(s, account)`; `accLedgerBalances`, `accPartyBalance`, `accPartyRetireBlocker`; `ACCException.kind` gains `'no-party'` (warning, not on a locked snapshot); `accDates` adds Udhaar credit and company payment dates; `accMapRows` counts their keys; `accSourceOf` handles both subs. Small edits outside the listed sections because party ids would otherwise break them: `accReconcile` (`isTender` / `net` compare the control part), `accCsv(vs, s?)` names the party, `accTallyXml` writes the **control** ledger name (summary; a detail Tally export is not built).
- `src/domain/streams/expenses-scc.ts` — `ExpUdKind`, `ExpUdReceivable.kind?` / `gstBps?`, `EXP_UD_KINDS`, `expUdKind`, `expUdRevenueKey`, `expUdGst`.
- `src/services/mock/handlers/expenses.ts` — `EXP_UD_ADD` raise path only: `kind` (restaurant | room | other) and `gstBps` (0–2800; absent with a kind = the GST master's rate: food / room night on the amount / other service).
- `src/domain/streams/money-master-specs.ts` — `canRetire` of bank accounts / tills (1000 / 1030) and expense vendors (2200) refuses a party ledger with a non-zero balance (`MASTER_IN_USE`). (The brief named `mnyVendorUsage` / `mnyAccountUsage` in `money-master.ts`; that file is a runtime-import-free leaf and its usage counts gate delete / code change, not retire — the specs' `canRetire` hook is where retire is refused.)
- `src/services/mock/index.ts` — one line in the `// ACC-1` block.
- `src/clients/sync/accounting.ts` — `EXP_UD_ADD` sends `kind` / `gstBps` to `AddUdhaar` (one line).
- tests: new `tests/stream-accounting-parties.mjs` (12 tests, `[ACC1-PARTIES]`); updated `stream-accounting.mjs`, `stream-cashout.mjs`, `stream-gst.mjs`, `stream-expenses-decisions.mjs` (cash / bank / Udhaar entries now carry `1000/TILL`, `1030/BANK-1`, `1120/UD-…`; `receipt:udhaar` uses 2 → 3 with the credit raised).

**Server (`08 Production`)**
- new `src/Hcc.Domain/Accounting/PartyLedgers.cs` — `PartyLedgers` (controls, `Compose`, `ControlOf`, `PartyOf`, `IsParty`, `DisplayName`, `Company(id)` = `CMP-<id>`, `Staff(id)` = `STF-<id>`, `Udhaar(name, mobile)`, `Slug`, `Clip`) and `PartyContext` (Tender, Company, Udhaar, Staff, Vendor, Bank, Channel → `For(mapKey)`).
- `AccountingSource.cs` — `SourceEntry(..., string? Party = null)`; `SourceRules.WithParties`, `UdhaarRevenueKey`, `UdhaarGst`, `UdhaarCredit`.
- `Vouchers.cs` — `VoucherBuilder.Build` appends the party when the resolved code is a control; `TrialBalance(vouchers, detail = false)`; `TrialBalanceRow.Control`; `NoPartyWarnings` added to `Exceptions` (not when locked).
- `AccountingEventMapper.cs` — every factory takes `PartyContext? parties`; company payment names `CMP-<id>`, advance and full-and-final name `STF-<id>`; new `UdhaarCreditKey`, `UdhaarCredit(UdhaarCreditRaisedEvent)`.
- `AccountingVoucherProjector.cs` — reads the parties when an event is consumed (tender → settles-to `mst.bank_account.code`; vendor → `ops.expense_vendor.code`; slip bank → bank account by code / name / bank, else the only bank; company of a company-credit receipt `pay.receipt.company_id`; company of a group `grp.group_account.company_id`; Udhaar customer from `ops.udhaar_receivable`); consumes `UdhaarCreditRaised`; writes `party_id`; the pay-run reversal reads `party_id` back.
- `SqlAccountingStore.cs` — reads `party_id`; lock snapshot writes `account_code` up to 45 characters.
- `Reconciliation.cs`, `VoucherExport.cs`, `AccountingQueries.cs` (one line: entry ledger name) — compare / name by the control part (needed by party ids; files not assigned to any lane).
- `Hcc.Contracts/Accounting/AccountingContracts.cs` — `AccountingEvents.UdhaarCreditRaised` (+ in `Consumed`), `UdhaarCreditRaisedEvent`.
- Udhaar raise: `ExpensesContracts.cs` (`AddUdhaarRequest.Kind`, `GstBps`), `ExpensePorts.cs` (`UdhaarRecord.Kind`, `GstBps`), `ExpenseRegisters.AddUdhaarAsync` (validates, stores, publishes `UdhaarCreditRaised` for every new credit), `SqlExpenseRegisterStore.cs` (insert / read `kind`, `gst_bps`).
- migrations `4210_acc_source_entry_party.sql` (`acc.source_entry.party_id VARCHAR(40) NULL`), `4211_acc_locked_entry_party.sql` (`acc.locked_entry.account_code` VARCHAR(4) → VARCHAR(45) — without it a lock would silently truncate party ids), `4215_exp_udhaar_kind_gst.sql` (`ops.udhaar_receivable.kind`, `gst_bps`); all three in `migrations.lock.json` (`--add`) and `Hcc.slnx`.
- tests: new `tests/Hcc.Accounting.Tests/Domain/PartyLedgerTests.cs` (16 cases), `Sql/PartyLedgerSqlTests.cs` (2); updated `Golden/GoldenAccountingTests.cs`, `Domain/AccountingRulesTests.cs`, `Domain/BooksDec074Tests.cs`, `Sql/MappingAndExportTests.cs`.
- **Golden regenerated** (`Golden/accounting-golden.json` via `export-accounting-golden.mjs`): not a mapping default — the party suffix changes the prototype's entries (`1000/TILL`, `1030/BANK-1`) and CSV ledger names (`Cash in hand · TILL`). The exporter now records each source's `parties` (tender settles-to, company, vendor) and the C# golden test applies them with `SourceRules.WithParties`. The trial balance (summary), exceptions and Tally XML in the golden are unchanged.

## Built against the contract

- **§3 party ids** `<control>/<party>`; controls 1000, 1030, 1110, 1115, 1120, 1200, 2200, 2400; parties derived, never stored; `accPartyLedgers(s)` returns `{id, control, partyId, name, active}`; posting to the control + `no-party` warning when the source does not name the party; summary / detail trial balance; retire refused on a non-zero balance (bank / till / vendor).
- **§3 resolution table** — company credit and company payment → company; group receivable / deposit / settlement → the block's company (server); Udhaar settlement and credit raised → the customer; vendor credit → the vendor; methods settling to a bank → the settles-to account; cash → the till (seed: the one till); staff advance given / recovered → staff (server: advance and full and final; see open items for the pay run); `receipt:ota` → channel (defined in `PartyContext`, nothing posts it in ACC-1).
- **§3 server** — `SourceEntry.Party`, `party_id` (4210), `VoucherEntry.Account` carries the full id, mapping first then the suffix.
- **§4** — `ExpUdReceivable.kind` / `gstBps`; Sales voucher Dr `1120/<customer>` gross · Cr `pos:sales` (→ 4200 once acc1-chart re-points it; 4100 today) / `line:room` (4000) / `line:service` (4100) · Cr 2110 · Cr 2120; key `udhaar:<id>`, sub `udhaar-credit`; settlement credits the party ledger; server `kind` / `gst_bps` (4215), event `UdhaarCreditRaised`, `SourceRules.UdhaarCredit`.
- **§8** — no new commands (EXP_UD_ADD gains fields); one export line in the mock barrel; no handler / sync registry entries needed.
- **§9** — migrations 4210, 4211, 4215 (within 4210–4219).

## Test counts

| suite | before | after |
|---|---|---|
| prototype `npm test` | 1867 total at the wave base (1854 passing on `39930edc` because of the SCHEMA 11 pins; 1867 / 1867 after the coordinator's re-pin `83be423d`) | **1869 / 1879** (1867 + 12 new `[ACC1-PARTIES]`); the 10 failures are the FY26 hash pins this lane moves on purpose (see "FY26 hash pins" below) |
| server `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1` | 58 (derived: 76 − the 18 new cases; not run separately on the base) | **76 / 76 passed** |

`npx tsc --noEmit` clean; `dotnet build Hcc.slnx` 0 warnings 0 errors; `npm run build` OK (`dist/index.html` 3212 KB). Logs (uncommitted): `E:\Projects\HCC\worktrees\acc1-parties\.scratch\` (`test2.log`, `acctest2.log`, `build3.log`, `tsc2.log`, `build-proto.log`).

## Contract / rulebook notes (no silent picks)

1. **Seeded till id.** Contract §3 says party id = the bank-accounts row `code` and also "one seeded till → `1000/TILL-MAIN`". The seeded row's code is `TILL` (prototype `SEED_TILL`, server `usp_seed_money_masters`), so the ledger is **`1000/TILL`**. The coordinator should amend the example (or rename the seed code, which moves fixture hashes).
2. **Table name.** Contract §4 says `exp.udhaar_receivable`; the table is `ops.udhaar_receivable` (migration 4150). Migration 4215 alters `ops.udhaar_receivable`.
3. Rulebook §3.3.1 lists the controls without 1000; the contract adds 1000 (tills). Not a conflict (the contract is a superset); built as the contract.
4. **Udhaar amount is tax-inclusive.** The register's amount is what the customer owes, so the GST is taken out of it: taxable = gross × 10000 ÷ (10000 + bps) rounded half away from zero, tax = gross − taxable, CGST / SGST as `gstSplit`. CA to confirm.

## Open items

- **CA note (contract §4):** Udhaar credits raised before ACC-1 have no kind / rate: the prototype books them as "other" (4100) with **no GST lines**. If those were taxable sales, the GST was never separated; the CA decides whether to re-state them.
- **Server back-fill:** credits raised on the server before migration 4215 never published `UdhaarCreditRaised`, so the server has no sale voucher for them (the prototype derives one). No production data exists before go-live; if any does, a one-off publish of the event per old row is needed.
- **Server default rate:** the prototype handler defaults `gstBps` from the GST master when only `kind` is sent; the server stores 0. The UI (acc1-ui) must always send `gstBps` (pre-filled from `gstForFood` / `gstForRoomNight` / `gstForService`).
- **Write-off:** `EXP_UD_VOID` (write-off) leaves the raised receivable on `1120/<customer>`; the bad-debt journal (rulebook A18) is a later wave.
- **Pay run:** `PayrollPostedEvent` carries one total advance recovery, so the server's pay-run voucher credits `1200` (control) with a `no-party` warning. A per-staff split needs the event (or a read of the run's lines) — ACC-2. The **prototype does not post payroll at all** (never did): T13a–c of the rulebook month cannot be produced by the prototype engine (acc1-tests should know).
- The prototype does not post group master-folio vouchers or OTA receivables (server only / ACC-2); the resolvers exist (`accPartyFor` 1110 / 1115).
- Tally XML carries the **control** names (summary); a party-detail Tally export with party masters is not built.
- The control list is a constant (`ACC_PARTY_CONTROLS` / `PartyLedgers.Controls`); once acc1-chart lands, the integrator may read `ACCLedger.control` instead.
- `Hcc.Expenses.Tests` were not run (lane rule: only the touched test project). `AddUdhaarAsync` now also publishes `UdhaarCreditRaised`; no Expenses test counts all outbox events of a credit (checked by search), but the integrator's full run should confirm.
- One order-dependent pre-existing test fixed in passing: `MappingAndExportTests.Dec135_and_Dec130…` used 20 Sep, inside the `Exports_follow_DEC063…` Tally window (2 Sep – 2 Oct); with xUnit's shuffled order the export counted 5 vouchers instead of 1. Moved to 20 Aug.
- **FY26 hash pins (for the integrator, not edited here):** BASE is unchanged, but the FY26 history (`src/fixtures/history/modules-sales.ts`) locks 20 accounting months with `accLiveVouchers` snapshots, and those snapshots now carry party ledger ids (and the Udhaar credit / company payment vouchers), so the FY26 history and FY26 hashes move deliberately: FY26 history `f3601522…` → **`6664ec330265a3bcd4f7cd118e945b253d707ed1d317aa16e715a18e0a6807d1`**, FY26 `f9eb386f…` → **`741149b1ddd8fbd91c9cb1956b3a348f3fa8fa158281309c12d210cc01c77991`**. Failing pins: [CAT], [FDM], [MFX] ×2, [MST], [OPSM], [PPL], [PERF], [POS-MST], [RMS] (10). Re-pin once after every ACC-1 lane is merged (acc1-chart's re-points move them again). The Udhaar `kind` / `gstBps` fields are optional and no seeded row carries them.

## Integration needed

1. **acc1-chart** — `accLedgerName` / `accAccount` receive plain codes only from my code (I pass `accControlOf(id)`), but UI code that calls `accLedgerName(e.account)` on an entry should call `accEntryName(s, e.account)` instead. If the chart lane changes `ACC_CHART` order or names, `accTrialBalance`'s party ordering follows automatically (it orders by the control's chart position). Its re-point `pos:sales` → 4200 makes the restaurant Udhaar credit post to 4200 with no change here; the golden JSON must be regenerated once after both lanes merge (`node "08 Production/tests/Hcc.Accounting.Tests/Golden/export-accounting-golden.mjs"`), and `PartyLedgerTests.Udhaar_credit_raised…` already reads `Map["pos:sales"]`.
2. **acc1-ui** — Udhaar screen: send `kind` (restaurant | room | other, `EXP_UD_KINDS`) and `gstBps` (pre-filled from the GST master) with `EXP_UD_ADD`; Parties tab: `accPartyLedgers(s)` + `accLedgerBalances(s)`; trial balance detail toggle: `accTrialBalance(vs, undefined, true, s)`; entry names: `accEntryName(s, account)`; CSV: `accCsv(vs, s)`; exception kind `no-party` (warning).
3. **acc1-statements** — `accTrialBalance6(s, from, to, detail)` can build on `accTrialBalance(vs, accounts, detail, s)` (the `control` field is set on party rows); the server `VoucherBuilder.TrialBalance(vouchers, detail)` does the same.
4. **acc1-opening** — opening lines may name party ids (`1110/CO-1`, `2200/V-…`); `accTrialBalance` / the server builder treat them like any posted party entry. Note the till id is `1000/TILL`, not `1000/TILL-MAIN`.
5. **Company retire** (file `src/services/mock/handlers/groups.ts`, not mine): companies have no retire command today (`GRP_COMPANY_SAVE` has no active flag). When one is added, refuse with `const why=accPartyRetireBlocker(s,'1110',c.id);if(why)k.fail('INVALID_TRANSITION',why);` (import from `../../../domain/streams/accounting`). Server: the same check before retiring `grp.company_account` (`PartyLedgers.Company(id)` on 1110).
6. **Staff exit** (file `src/services/mock/handlers/staff.ts`, `HR_STAFF_ACTIVE`): **do not** block the exit on a 1200 balance — the full and final settlement (which clears the advance) requires the exit first (`HR_FNF_SETTLE` asserts `!st.active&&st.exitedOn`). Recommended: block *reactivation-free deletion / archive* only, or add to `HR_FNF_CONFIRM` a warning when `accPartyBalance(s,'1200/'+st.id)!==0` after the settlement. Today the prototype posts no payroll, so the balance is always 0; on the server the advance (`AdvanceGiven`) and F&F post to `1200/STF-<id>`.
7. **Migrations lock order** — acc1-chart's 4200–4209 are numbered below my 4210 / 4211 / 4215; whichever merges second needs `node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order "ACC-1 parallel lanes"` and the `Hcc.slnx` lines kept in number order.

## HANDOFF bullet

- **ACC-1 parties (lane acc1-parties, 27 Oct):** party sub-ledgers `<control>/<party>` derived from the masters (tills / bank accounts `1000|1030/<code>`, companies `1110/<id>`, OTA `1115/<id>`, Udhaar customers `1120/UD-<mobile|slug>`, staff `1200/<id>`, vendors `2200/<code>`; prototype `accounting-parties.ts`, server `PartyLedgers` + `SourceEntry.Party` / `acc.source_entry.party_id`); vouchers post to the party (else to the control with a `no-party` warning that never blocks a lock); trial balance summary (controls) or detail (parties); bank / till / vendor retire refused on a non-zero balance; **Udhaar credit raised = Sales voucher the same day** (A17, DEC-144: Dr 1120/<customer> · Cr F&B / room / other · CGST · SGST; `kind` + `gstBps`, event `UdhaarCreditRaised`); company payments posted in the prototype; migrations 4210 / 4211 / 4215; accounting golden regenerated (party suffixes). Open: Udhaar rows before ACC-1 book as "other" without GST (CA), pay-run advance recovery not split by staff (ACC-2), prototype posts no payroll, Tally export stays control-level. `05 Delivery Evidence/Accounting/ACC1-PARTIES-DONE.md`.
