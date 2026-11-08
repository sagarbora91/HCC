# F8D-FOLIO DONE (lane f8d-folio, Sonnet 5.5), 8 Nov 2026
Branch claude/f8d-folio (from claude/f8c 0af7a992). Head hash: `git log -1` (this file is in the same commit). Nothing pushed.

## What changed
1. **Folio line table at 1024 px** - `03 Prototype/src/styles/folio.css` (F8D-FOLIO block): the table no longer has the 550 px minimum, cell padding 6 px side, amounts stay on one line, other cells wrap. Seen at 1024: fits the pane at text 1.0 (340/340). At text 1.3 it still scrolls sideways inside its own table panel (363 vs 340); text is never shrunk.
2. **Check-out dialog** - `features/folio/modals.tsx` CheckoutModal: summary in two columns, then `co-grid` (settle sheet / panels left, feedback right); dialog widened to 880 px; feedback block laid out (5 stars in one row, comment and category as stacked full-width fields). Where the dialog is short, the settle-sheet folio rows scroll in their own panel; the dialog body never scrolls (measured at 1440, 1024, 820, light and dark, 1.0 and 1.3: body scrollHeight = clientHeight in all 12; only 1024 x 768 at 1.3 scrolls the sheet list, 266 in 176). Below 760 px wide the old single column is kept (phone sheets not touched).
3. **Company-limit refusal wording** - shared text, unchanged numbers. Now: `limit ₹9,000, already used by other business ₹0, headroom ₹9,000; this split folio needs ₹9,450.` (was `used ₹0, remaining ₹9,000`). Prototype `domain/streams/groups.ts` grpCreditRefusal; server `08 Production/src/Hcc.Domain/Groups/CompanyCredit.cs` Message. Pinned tests updated: `tests/stream-company-credit.mjs`, `stream-company-credit-ui.mjs`, `sync-company-credit.mjs`, server `CompanyCreditRulesTests.cs`, `CompanyCreditSqlTests.cs`, `ReservationCompanyTests.cs`. Guest (Udhaar) credit text (`counter.ts`, `GuestCreditRules.cs`) left as is: it is a different rule and has its own pins; it still says used/remaining.
4. **Company folio payments** - SF-CONTRACT section 3 (company row: "approved company credit or a payment (Bank transfer / UPI / Card, DEC-076: no cash)") and section 6 ("company folio: no cash") ALLOW non-cash payments. So the "Record payment" control stays and the command refusal (SF_COMPANY_FOLIO) is unchanged. UI fix: the payment dialog on a company folio no longer lists cash methods (seen: UPI, Card, Bank transfer) and says why; the split dialog note now says settled on company credit or by bank / UPI / card, never cash (`modals.tsx` PaymentModal, `Split.tsx` line 64).

## Tests actually run (Invoke-Serialized, label f8d-folio)
- `npx tsc --noEmit` exit 0
- HCC_TEST_ONLY stream-company-credit 70/70, stream-split 71/71, stream-f8 74/74, sync-company-credit 61/61
- `dotnet test Hcc.Groups.Tests --filter CompanyCreditRulesTests` 5/5
- `npm run build` OK
## Tests NOT run
Full npm test; server SQL tests (CompanyCreditSqlTests, ReservationCompanyTests edited for the text but not run: need SQL Express); fit-screen-check; phone viewports. No new test was added for the payment-method filter (checked in the browser only).

## Defaults relied on
SFQ-1, SFQ-13 (company credit approval), DEC-076 (no cash on company). No owner answers assumed.

## Screenshots
`05 Delivery Evidence/F8D/folio/`: `<w>-<theme>-s<scale>-folio-table.png` and `-checkout.png` for 1440 / 1024 / 820, light and dark, 1.0 and 1.3; `1440-light-s1-company-payment.png`; `1440-light-s1-limit-refusal.png`. Golden stay created at runtime with commands (as tests/stream-f8-int.mjs), no seed change; high contrast not captured.
