# F8E-R8 DONE: company-credit half of R8 / P2-B (9 Nov 2026)

Branch `claude/f8e-r8` (worktree `E:\Projects\HCC\worktrees\f8e-r8`), cut from main 674929a3. Head: see the commit that adds this file (`git log -1 claude/f8e-r8`).
Tests only. No product code changed.

## What R8's company-credit half requires (sources)
- SF-CONTRACT §8 R8: `rpt.money.company-credit` attributes to a company the lines of its company folio on a split stay, and the whole stay otherwise.
- On this report that means three things:
  - "Used" is the D7 exposure (§7.2). It counts the receivable plus, for a live split stay, only the company folio: posted lines, agreed unposted nights when `room` is routed to the folio, their GST, less the receipts on that folio. The guest folio never counts.
  - The statement's company-credit row is the company folio's receipt (§I5).
  - A night posting is never refused by the limit (§7.3, SFQ-6). The report then shows the company over its limit, and company credit is refused until the Owner raises the limit.
- Prototype code: `03 Prototype/src/domain/reports/p2-b/company-credit.ts:16-41`. It uses `grpCredit` / `grpExposure` / `sfStayExposureFor` (`src/domain/streams/groups.ts:156-202`).
- Server code: `08 Production/src/Hcc.Reporting/Reports/P2B/P2BReports.cs:235-298`. It uses `CompanyCredit.PictureAsync` and `SqlCompanyCreditStore.FactsSql` (`src/Hcc.Infrastructure.SqlServer/Groups/SqlCompanyCreditStore.cs:32-86`).

## What changed
- New `03 Prototype/tests/stream-f8e-r8.mjs`, 5 tests tagged `[F8E-R8]`. The figures come from `tests/sf-golden.fixture.json`. Folio B = 945000 paise.
  1. **In house.** Before the nights are posted, Used = the folio B estimate of 945000. After posting it is still 945000: folio B only, not the stay balance. Remaining = 5000000 − 945000, status "Within limit". `grpExposure` and `sfFolioExposure(B)` both equal 945000 and the receivable is 0. The statement is empty (opening 0, closing 0).
  2. **Otherwise-branch.** The same stay with no split, booking linked to the company: Used = the whole stay less the advance (1130000 − 100000).
  3. **After check-out.** Used = remaining receivable = 945000. The statement has one company-credit row: ref = folio B's invoice number (not A's or `Booking.invoiceNo`), credit 945000, balance 945000. Closing = (945000, 0, 945000).
  4. **POST_NIGHTS with the limit lowered to 900000 before the nights.** All three nights post on B. The report shows Used 945000, Remaining 0, "Over limit". CHARGE ₹600 onto B is refused (`INVALID_TRANSITION`, "Credit limit exceeded for …"); the same onto A passes and Used does not move. COMPANY_CREDIT_REQUEST is refused with the D7 "split folio" text. After the Owner raises the limit, the request goes through: approval amount 945000 on folio B, status back to "Within limit".
  5. **NIGHT_POST (the day-close path) with the limit at 100000.** Tonight's night posts on B, Used = 945000, "Over limit".
- New `08 Production/tests/Hcc.Integration.Tests/F8eR8CompanyCreditTests.cs`, 3 tests over HTTP on the real composition. They reuse `SfGoldenTests.RunAsync` / `Fixture` / `Seed`.
  1. **In house.** Report Used = 945000 = `/groups/companies` exposure; Remaining = limit − 945000; "Within limit". Folio A still owes, but that is not counted. The statement has no credit row.
  2. **After check-out.** One company-credit receipt of 945000 on folio B, none on A. Used 945000. The statement has one row (credit 945000, balance 945000). Closing = (945000, 0, 945000).
  3. **Limit lowered to 900000 after OpenFolio, before the nights.** Three PostRoomNight calls and three day closes all pass, and the nights are on B. The report shows Used 945000, Remaining 0, "Over limit". RequestAdjustment credit is refused with `INVALID_TRANSITION` and "Credit limit exceeded", and no company-credit receipt is written. After SetCompanyLimit back to 5000000, the request passes and the report shows "Within limit".

## Tests actually run (filtered only, through `tools/Invoke-Serialized.ps1`, label F8E-r8)
- **Prototype:** `HCC_TEST_ONLY='stream-f8e-r8|stream-sf-golden|stream-rpt-p2-b' npm test` gave **82/82 passed**. That covers the m1 base suite plus the 3 filtered files; the 5 `[F8E-R8]` tests are among them.
- **Server:** `dotnet test tests/Hcc.Integration.Tests -maxcpucount:1 --filter "FullyQualifiedName~F8eR8CompanyCreditTests|FullyQualifiedName~SfGoldenTests"` gave **6/6 passed** (3 `F8eR8CompanyCreditTests` + the 3 existing `SfGoldenTests`), 46 s. The first attempt failed to compile (a missing `using Hcc.Contracts.Inventory`); it was fixed and re-run.

## Tests NOT run
- Full `npm test`, `tsc`, the builds and the full server solution. The gate runs those.

## Defaults relied on (owner has not answered)
- **SFQ-6:** the night is posted, the report shows red / over, and company credit is refused until the Owner raises the limit.
- **SFQ-13:** not exercised. The approvals here are by the Manager in the prototype (945000 is under ₹10,000) and by the Owner on the server.

## Follow-up fix (coordinator instruction): server statement reference
- **Before:** the server statement's "Bill or reference" showed the reservation `public_no`. The prototype shows the company folio's own invoice number (F8-INT H1 / L1).
- **Fixed in three places:**
  - `08 Production/src/Hcc.Application/Groups/CompanyCredit.cs:16-17`: `CompanyCreditReceiptRow` gains an optional `InvoiceNo`.
  - `08 Production/src/Hcc.Infrastructure.SqlServer/Groups/SqlCompanyCreditStore.cs:35-37,112`: the facts SQL also reads the invoice of the receipt's own folio (`ivc.invoice`, `source_kind = 'folio'`).
  - `08 Production/src/Hcc.Reporting/Reports/P2B/P2BReports.cs:260`: `Ref = InvoiceNo ?? PublicNo`. The narration and the exposure items keep `PublicNo`.
- **Test:** `F8eR8CompanyCreditTests` (after check-out) now asserts that ref = folio B's invoice number, which differs from folio A's and from the reservation number.
- **Runs (filtered, through the lock):**
  - Hcc.Integration.Tests, F8eR8CompanyCreditTests + SfGoldenTests + F8IntSplitGoldenTests: **7/7**.
  - Hcc.Reporting.Tests `~P2B`: **11/11**.
  - Hcc.Groups.Tests `~CompanyCredit|~CompanyRetire`: **12/12**.

## Open item (left as is, per coordinator)
1. **Statement opening balance differs.**
   - The prototype starts at `c.openingOutstanding` (`company-credit.ts:22`).
   - The server always starts at 0 (`P2BReports.cs:264`, `long run = 0`).
   - The golden company has no opening balance, so neither test is affected. This is not part of R8 and was left for a decision.

## Screenshots
- None. No UI was touched.
