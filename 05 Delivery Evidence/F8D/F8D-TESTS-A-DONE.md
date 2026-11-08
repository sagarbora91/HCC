# F8D-TESTS-A DONE (lane f8d-tests-a, Sonnet 5.5), 8 Nov 2026

Branch `claude/f8d-tests-a` from `claude/f8c` 0af7a992. Test files only; no production source touched. Head hash: see `git log -1` of the branch (final commit includes this file).

## Added
- `03 Prototype/tests/sf-golden.fixture.json`: the SF-GOLDEN numbers (SF-CONTRACT 5.7), the single source for both suites.
- `03 Prototype/tests/stream-sf-golden.mjs` (5 tests, `[SF-GOLDEN]`): fixture consistency; two folios / two invoices in letter order / two GST documents / sum 11,300 = unsplit control; accounting = control except the receivable party; POS room-post food on 4200; R8 + R9 + R6 on the fixture stay.
- `08 Production/tests/Hcc.Integration.Tests/SfGoldenTests.cs` (3 tests): reads the same fixture file (walks up to `03 Prototype/tests`); two ivc.invoice rows with fixture figures and sum; R9 (`SqlAnalyticsInputSource` bill 11,300 once, paid 11,300; `IStudyPackFacts.RoomNightsAsync` 3 nights = 9,000); R8 (`rpt.guests.company-business` row = folio B only: tariff 9,000, other 0, 3 nights, 1 guest).
- `08 Production/tests/Hcc.Integration.Tests/F8dSplitMoneyTests.cs` (6 tests): H6 (night on B reposted on B; night on A before routing reposted on A), H7 (meal-plan line on the night's folio B), H8 (ExplicitFolio flag vs routed charge, B version bumped, discount stays on A at 18 %), H9 (Udhaar folio guest credit: one receivable on the payer, none on the stay guest), R6 (high-balance row: balance once 4,000; guest threshold, company limit, both).

## Run (through Invoke-Serialized, label f8d-tests-a; waited for the f8c gate pause to end)
- Prototype `HCC_TEST_ONLY=sf-golden node tests/run.mjs`: 61/61 (5 new + base tests m1 loads), all pass.
- Server `dotnet test tests/Hcc.Integration.Tests -maxcpucount:1 --filter "SfGoldenTests|F8dSplitMoneyTests"`: 9 tests, 8 passed first run; the 1 failure was my test (report date range after the business date), fixed, re-run `--filter SF_GOLDEN_R8` 1/1 passed. The combined 9 were not re-run together after that one-line fix.

## Not run
Full prototype suite, full server solution, tsc. Not covered (still owed from F8-INT list): per-folio DocumentSql print, night-audit signals endpoint, no-show-due endpoint, OTA overbooking cap, folio-join audit on movement register / desk folios / MEAS daily, `rpt.money.company-credit` (P2-B) R8 half.

## Bugs found
None. No test skipped.

## Defaults relied on
SFQ-6 (night posting never refused), SFQ-20 (mixed-rate adjustment refused; H8 discount uses a single-rate laundry folio), meas.target.highBalance not relied on (explicit threshold param). Divergence noted: contract 5.7 says food on 4200; CHARGE path gives 4100 (4200 only via POS), the fixture test states both.
