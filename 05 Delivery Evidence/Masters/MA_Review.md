# Masters wave — MA-11 review

27 Oct 2026 · Reviewer: Claude (Opus), agent MA-11 · Branch `claude/ma11-review` (worktree `E:\Projects\HCC\worktrees\ma11-review`), based on `main` 799e4cc (MA-0…MA-7 merged, Wave B, customisation Part 1). Not pushed, not merged.

Scope (brief `02 Planning/agents/MA_Briefs.md` § MA-11 and the lead's list): the "needed but could not change" items and open items of `MA_Merge_Report.md`, money / GST / numbering / availability correctness, DEC-058 / DEC-061 permissions, migration order and FK direction, idempotency and STALE_VERSION, API contract notes. Every fix is its own commit with tests; decisions are appended to `02 Planning/HCC_Decision_Register.md` as **DEC-MA-11a…f**; the API appendix is **Appendix M** of `02 Planning/HCC_Production_Contracts_and_Schema_v1.md`.

## 1. Findings and fixes

| # | Sev. | Area (agent) | Finding | Fix (commit) |
|---|---|---|---|---|
| 1 | **High** | Payment methods (MA-3) | In production an Owner-added payment method was refused for folio receipts (`Tenders.Validate` fixed list; `pay.receipt.method` VARCHAR(16)), outlet bills (`PosRules.Tenders`; `CK_pos_bill_tender`, `CK_pos_receipt_tender`, VARCHAR(8)) and expenses (`ExpenseMethods.All`; `CK_expense_method`, VARCHAR(14)). | `PaymentMethodBook` (`Hcc.Domain/MoneyMasters`): resolve by name / code, usable at the place, active (`MASTER_RETIRED`), reference rule; defaults = the seeded rows when a property has no list (old messages unchanged). Port `IPaymentMethodSource` + `SqlPaymentMethodSource` (MoneyMasters module) used by `BillingService` (RecordReceipt, refund execution), `PosService` (SettleCheck), `ExpenseCommands` / `ExpenseQueries`. Migration **2890** relaxes the three CHECKs and widens the four columns to NVARCHAR(60). — b8591e5 |
| 2 | **High** | Drawer cash (MA-3) | The shared drawer (`pay.cash_movement`, shift, cash-book lock) was keyed on the literal "Cash": a till-settling method with another name would have bypassed the drawer. | "Settles to a till" decides (receipts, refunds, outlet settle and void — void now by the bill's `till_id`, expense approve / reverse, expense summary). `CK_pos_bill_cash` = till, shift and movement together or none. — b8591e5 |
| 3 | **High** | Numbering (MA-5 × Wave B) | Wave B's RST restaurant invoices could not be a row of the numbering-series master; as soon as the Owner stored the list, `InForceAsync("RST")` found no row and **every outlet bill's settle failed** ("No numbering series is in force…"). | RST is a GST document of `bill.number_series` (default row RST/2627/0000001 — identical numbers), migration **2891** (CHECKs + RST row for stored lists); a stored list without a row for a document falls back to the built-in numbering. Prototype: seeded row `nsr-rst` continues `posX.rstSeq`; `issueRst` allocates through `fdmAllocate('RST')`. — 02b0ef9 |
| 4 | Medium | Meal plans (MA-2) | The server night run did not post meal-plan lines (only the prototype did). | `IMealPlanNightPoster` (Closing) implemented by Catalogue: the close's night step and `PostRoomNights` call `PostMealPlanNights` after the room nights (idempotent per folio and date; step detail `planLines`). — 328b030 |
| 5 | Medium | GST of bundled plans (MA-2) | A plan "bundled with room" did not lift the room night's slab on the server (prototype: room + meals). | `IRoomNightBundle`: `PostRoomNight` takes the slab on room + bundled add-on (tax still on the room amount); the plan line uses the same slab. Illustrative — CA to confirm (DEC-MA-11f). — 328b030 |
| 6 | Medium | POS day summary (MA-3 × MA-4) | Bills paid by any method other than Cash / UPI / Card were added to `roomFolioPaise`; outlet cash was `tender = 'Cash'`. | Buckets by `till_id` (cash), UPI, Card, room (no tender), new `otherPaise`; outlet cash = receipts of bills that went into a till. — b8591e5 |
| 7 | Medium | Accounting (MA-3) | `ChartOfAccounts.Effective` ignored the ledgers stored on payment methods and expense categories, so a new method / category posted to Suspense. | `Effective(overrides, masterLedgers)`: defaults → master ledgers (`method:` / `funding:` / `expense:`) → Owner overrides; `IAccountingStore.MasterLedgersAsync`. — b8591e5 |
| 8 | Medium | Booking sources (MA-5) | `CreateReservation` (and the prototype `BOOK`) accepted a retired booking source. | `IBookingSourceCheck` / `SqlBookingSourceCheck` (same matching as the prototype `fdmSourceOf`: OTA channel, "OTA · manual", name or code); `MASTER_RETIRED`, field `source`. Prototype `BOOK` the same. — 02b0ef9 |
| 9 | Medium | Holidays (MA-6 × Wave B) | MA-6's `SaveHoliday` / `RetireHoliday` / `DeleteHoliday` ignored a locked pay month (Wave B's `SavePaidHoliday` / `RemoveHoliday` refuse it). | A **paid** holiday of a locked month cannot be added, changed, moved, retired, reactivated or deleted — Wave B's words ("<Month> payroll is locked — its holidays cannot change."); `IPeopleListsStore.PayMonthLockedAsync`. Prototype holiday spec `validate` / `canRetire` the same. MA-6 test "retire allowed" → refused (old → new noted). Two command sets: keep both for now (proposal DEC-MA-11c). — d457d66 |
| 10 | Medium | RBAC / DEC-061 | Money values in master reads reached Supervisor, Housekeeping, Maintenance, Tech admin, Restaurant: payment-method surcharge, expense-category approval limit, booking-policy charge / deposit %. Prototype: discount-reason percentages were not marked money. | `MastersMoney.SeesMoneyAsync` (PERM-MST-002 / RPT-002 / RES-001 / EXP-001 = the hotel-money roles) nulls them (contracts nullable); `maxPct` / `approvalAbovePct` flagged `money:true`; new test walks **every registered master spec** for Housekeeper and Maintenance (form fields and row values). — 53e7bab |
| 11 | Medium | POS menu requests (Wave B D5 × MA-4) | A Manager's menu request silently dropped GST class and station. | Intended as Owner-only money / masters fields (DEC-MA-11b): a request with a different value is refused with a clear message; current values pass. — d457d66 |
| 12 | Low | Solution file | `Hcc.slnx` did not list `2202_groups_company.sql` and `2702_import_pos.sql` (pre-existing). | Added; a test checks every migration file is listed. — d0629d0 |
| 13 | Low | Numbering (MA-5) | Receipt / KOT / registration-card series defined but unused. | Marked **reserved** (server `ReservedDocuments`, prototype `FDM_RESERVED_DOCS` + list help). — 02b0ef9 |
| 14 | Low | Migrations | Upgrade from a database built before the masters wave (merge report §6.5) was unproven. | `MastersMigrationUpgradeTests`: from main after Wave B (2900–3701 applied; 2810…2891 fill in below) and from main up to 2702 → backup gate, rows kept (Wave B holiday → paid master row; expenses; money masters seeded), Owner method accepted, all FKs / CHECKs trusted, **schema identical to a fresh database line by line**; masters FKs point from the modules to `mst.*`. — d0629d0 |

Checked without findings: every new server command checks PERM-MST-001 or PERM-MST-002 (money lists, money fields) or its module permission (PostExtra FOL-002, PostMealPlanNights CLS-002, discounts FOL-003); every command runs through `ICommandRunner`; each of the eight master test projects already has an idempotent-replay test and a STALE_VERSION test on its shared save path; migration numbers 2801–2871 ascend without duplicates, 3800 runs after 3400; applied migrations unchanged since main (no checksum drift).

## 2. What changed (files)

- Server: `Hcc.Domain/MoneyMasters/PaymentMethodBook.cs`, `Hcc.Application/MoneyMasters/PaymentMethodSource.cs`, `Hcc.Infrastructure.SqlServer/MoneyMasters/SqlPaymentMethodSource.cs`, `Hcc.Application/Billing/RoomNightBundle.cs`, `Hcc.Application/Closing/MealPlanNights.cs`, `Hcc.Application/Catalogue/MealPlanNightPoster.cs`, `Hcc.Infrastructure.SqlServer/Catalogue/SqlRoomNightBundle.cs`, `Hcc.Application/Reservations/BookingSourceCheck.cs`, `Hcc.Infrastructure.SqlServer/FrontDeskMasters/SqlBookingSourceCheck.cs`, `Hcc.Domain/FrontDeskMasters/BookingSourceRow.cs`, `Hcc.Application/Masters/MastersMoney.cs`; edits in BillingService, PosService, ExpenseRules / ExpenseCommands, SqlPosStore, SqlBillingStore, SqlExpenseStore, ChartOfAccounts, Accounting ports / service / queries / store, ClosingService, FrontDeskMastersRules / Queries, ReservationService, PeopleLists commands / ports / store, MoneyMasters commands / queries, contracts (MoneyMasters, FrontDeskMasters, Pos), endpoints (MoneyMasters, Catalogue, Closing, FrontDeskMasters), `Hcc.slnx`.
- Migrations (block 2890–2899): **2890_payment_method_records.sql**, **2891_number_series_rst.sql**.
- Prototype: `domain/streams/frontdesk-master.ts` (RST row, reserved docs), `services/mock/handlers/pos.ts` (RST via the master), `services/mock/handlers/reservations.ts` (retired source), `domain/streams/people-masters.ts` (holiday lock), `domain/streams/catalogue.ts` (money flags); tests `tests/stream-ma11-review.mjs` (5) + old → new updates in `stream-frontdesk-master.mjs`, `stream-people-master.mjs`.
- Docs: Decision Register DEC-MA-11a…f; contracts Appendix M; this report.

## 3. Tests

- Prototype: **1490 / 1490** (1485 + 5 new). `npx tsc --noEmit` clean; `npm run build` (2729 KB) and `npm run build:production` (2970 KB) OK.
- Server: 19 new tests — PaymentMethodBookTests 4, PaymentMethodMasterTests 3, MealPlanNightCloseTests 2, RstSeriesMasterTests 2, BookingSourceRetiredTests 1, FrontDeskMasters kernel +2, MoneyVisibilityTests 1, HolidayLockTests 1, MastersMigrationUpgradeTests 3 (plus extra assertions in PosRbacV2Tests, FrontDeskMastersHttpTests). Old → new: MoneyMastersHttpTests `surchargeBps` 0 → null for Housekeeping; FDM seed list + RST; MA-6 retire in a locked month allowed → refused. `dotnet build Hcc.slnx` 0 warnings; full serial run result in §5.

## 4. Left for the owner / later

| Sev. | Item | Why left |
|---|---|---|
| Low | Group master-folio receipts and company-credit payments keep their fixed tender lists (`CK_company_payment_tender`, migration 3500). | 3500 runs after block 2890–2899 on a fresh database; relaxing it needs a migration ≥ 3801 (outside MA-11's block). |
| Low | `CashFigures.ExpectedDrawer` (golden kernel, prototype parity) still tests the literal "Cash". | Production expected cash is the sum of `pay.cash_movement`, which now follows "settles to a till"; the kernel only feeds golden tests. |
| Owner | DEC-MA-11c: keep both holiday command sets (proposal) or retire Wave B's pair later. | Owner decision. |
| Owner | Seeded demo holiday calendar in the prototype vs none on the server (merge report §6.2). | Owner decision. |
| CA | Bundled meal-plan GST treatment (DEC-MA-11f), RST numbering format, policy percentages. | Illustrative — CA to confirm. |
| Low | Prototype holiday-lock refusal is `MASTER_INVALID` (VALIDATION) on save, server `INVALID_TRANSITION`. | The spec `validate` hook returns a message; the words are the same. |
| Low | Renamed payment methods / categories: records under the earlier name keep the default / override mapping, not the master ledger. | Accounting maps by stored text; the Owner can map the old name in Ledger mapping. |
| Low (MA-1) | Occupancy charges still post as a plain service charge, not through the extras catalogue. | Behaviour change for MA-8 / owner. |
| Low (MA-4/MA-6) | POS modifiers not built; POS import headers (MA-8); `FieldSpec` date / time inputs. | Out of review scope (MA-8 / UI). |
| Low | Production payroll board still fills legacy `staffX.holidays` (merge report §6.6). | Unread by the domain; cleanup for MA-9. |

## 5. Final verification

| Check | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` | **1490 / 1490** (1485 + 5) |
| `npm run build`, `npm run build:production` | OK (2729 KB / 2970 KB) |
| `dotnet build Hcc.slnx --no-incremental` | 0 warnings, 0 errors |
| `dotnet test Hcc.slnx -maxcpucount:1` (`.\SQLEXPRESS`, disposable `HccTest_*`) | **1275 / 1275** in 35 test projects (1256 + 19), first run, no failures (the room-105 EndToEnd count passed) |

Migration numbers used: **2890**, **2891** (block 2890–2899). Commits: b8591e5, 328b030, 02b0ef9, 53e7bab, d457d66, d0629d0, 4908fb4 and the final report commit.
