# F8-TESTS2 DONE (lane f8-tests2, Sonnet 5.5), 8 Nov 2026

Branch `claude/f8-tests2`, cut from `claude/f8` 8b2ec88e. Test files only; no production source touched. Head hash: see the final commit of the branch (`git log -1`).

## NOTHING WAS RUN
Every build/test goes through `Invoke-Serialized.ps1`. Both attempts (label `f8-tests2`) returned **exit 75 "lane builds are paused for a priority job"** (`_tools\PAUSE-LANE-BUILDS` exists, the gate holds the lock). I did not bypass it. **Counts run: 0 prototype, 0 server. The tests are not compiled and not executed**; expect first-run fixes (an assertion on an unverified shape, a C# compile slip). Run:
- `.scratch\run-proto.ps1` (sets `HCC_TEST_ONLY=f8-tests2`, `node --max-old-space-size=8192 tests/run.mjs`) via INV in `03 Prototype`; also `npx tsc --noEmit` is not needed (.mjs).
- `dotnet test tests/Hcc.Integration.Tests -maxcpucount:1 --filter F8Tests2` via INV in `08 Production`.

## Tests added
Prototype `03 Prototype/tests/stream-f8-tests2.mjs` (12 tests, `[F8-TESTS2]`):
1. variant (b): limit lowered to ₹9,000 before checkout, company credit refused with the D7 text, guest folio A pays, Manager cannot raise the limit, Owner raise lets checkout through.
2. variant (b2): limit lowered after approval, CHECKOUT re-checks and refuses.
3. variant (c): extra ₹600 on B over the limit refused (state untouched), same on A passes, totals.
4. variant (c2): extra within headroom passes on B; credit refused when over the limit.
5. golden with the food line through a POS room-post (kind food, ledger 4200 = POS net), revenue/GST identical to the unsplit control, vouchers balanced, 1100 nets 0, 1110 = folio B total, Σ invoices = control.
6. GST per invoice = Σ line tax of its own folio (I2/I3).
7. variant (d) DEC-061 walk, commands: housekeeper, housekeeper2, supervisor, maint refused 12 split/money commands, refusals copy-on-write.
8. variant (d) screens: RBAC/can() false, source gates on modals/Split/mobile Folio+Checkout.
9. H2: discount on mixed-rate A refused; on B posts at 5 % and stays on B (H8).
10. M6: late half day with extras routed to the company folio not refused by D7 at CHECKOUT.
11. M5: two Udhaar folios with equal balance get distinct 1120 parties.
(Test 4 contains a leftover scratch statement building an unused state; harmless.)

Server `08 Production/tests/Hcc.Integration.Tests/F8Tests2SplitTests.cs` (6 tests, 5 active + 1 skipped by design):
- variant (b) over HTTP; M1 (night on A, room routed to B, exposure = B only); M3 (RequestBillCorrection refused on both invoices); variant (d) DEC-061 walk for HKP, SUP, MNT over the summary/folio GETs and 7 commands; folio-join audit (guest count on /reservations, /reservations?view=inhouse, /guests, /stays is the same before and after opening folios B and C).
- variant (c): `Skip` with the bug text (below).

## Defaults used
SFQ-6 night posting never refused; SFQ-11 bill correction refused on a split stay; SFQ-13 Manager credit ≤ ₹10,000 per company folio; SFQ-20 mixed-rate adjustment refused; DEC-076 no cash on company folio.

## Bugs / open
- **M2 (known, F8-INT-DONE):** no posting-time D7 check on the company folio on the server (variant (c) skipped; the check would sit in the PostCharge path, `SqlBillingStore.InsertChargeAsync`/BillingService).
- No new bug found (nothing was run).

## Not written (still owed from the F8-INT list)
`stream-sf-golden.mjs`/`SfGoldenTests.cs` fixture bind scenario; server SQL tests for H6, H7, H8 (ExplicitFolio flag), H9, R6, R8, R9 (`SqlAnalyticsInputSource` ₹11,300, `SqlStudyPackFacts`), per-folio DocumentSql print, night-audit signals endpoint, no-show-due endpoint, OTA overbooking cap; folio-join audit on the movement register, desk folios and MEAS daily (only the four HTTP lists above are covered); prototype H2 is covered, M5/M6 are covered.
