# af-verify DONE (wave 4, independent verifier)

Lane af-verify · branch `claude/af-verify` · worktree `E:\Projects\HCC\worktrees\af-verify` · base = head of `claude/auditfix` (5b9d8dda = GitHub main). No production file edited; tests only. Nothing pushed, nothing merged.

Method: read the audit-fix code (not the lanes' DONE files) and attacked it. A passing test = the system refused or behaved correctly; a failing test = finding (kept failing). All heavy runs through `tools\Invoke-Serialized.ps1`, one at a time.

## Test files added (all new)
- `08 Production/tests/Hcc.Counter.Tests/Sql/AuditFixVerifyCounterTests.cs` (V-CTR, theories included)
- `08 Production/tests/Hcc.Billing.Tests/Sql/AuditFixVerifyPayTests.cs` (V-PAY)
- `08 Production/tests/Hcc.Customers.Tests/Sql/AuditFixVerifyCrmTests.cs` (V-CRM; includes real HTTP)
- `08 Production/tests/Hcc.Egress.Tests/Sql/AuditFixVerifyNetTests.cs` (V-NET)
- `08 Production/tests/Hcc.Invoices.Tests/Sql/AuditFixVerifyRegisterTests.cs` (V-REG)
- `03 Prototype/tests/sync-af-verify.mjs` (13 tests; V-CMAP, V-PAY-C, V-OPS, V-NET-C)
- Runner attempts: throw-away script `.scratch/runner/run-attempts.mjs` (not committed; results below).
The plan named `Hcc.Integration.Tests/AuditFixVerifyTests.cs`; the hostile tests sit in the project whose harness they need (Counter / Billing / Customers / Egress / Invoices) instead.

## Red findings (failing tests kept)

| # | Sev | Owner lane | Failing test | Smallest reproduction | What is wrong |
|---|---|---|---|---|---|
| F1 | **S2** | af-pay08 (client half, `clients/sync/money.ts` PAYMENT) | `[AF-VERIFY V-PAY-C03]` in `sync-af-verify.mjs` | Split payment, two UPI rows of 500.00 (refs A, B), no earlier receipt. Row 1 posts, row 2 is refused DUPLICATE_SUSPECTED (equal to row 1). Cashier ticks "second, separate payment"; the client resends **all** rows with `confirmDuplicate:true` under new keys `confirm-t1`, `confirm-t2`. Row 1 is posted again. Folio holds A, A, B = 3 receipts, 1,500.00 instead of 1,000.00. | The refusal text even says rows 1..i were already recorded, but the confirmed resend does not skip them (their original key `t1` is not reused). Cash rows also move the drawer twice. Fix: on a confirmed resend send only the rows from the refused row on (or reuse the original `tN` keys for rows before it). |
| F2 | S3 | af-ctr-svc (counter price path; pre-existing, not introduced by the lane) | `V_CTR_10b_overflow_sized_line_is_refused_VALIDATION_not_an_unhandled_exception` (theory, unit values 1e12 and up) | `IssueCounterBill` UPI, one line qty 99, unit 1,000,000,000,000 paise (and 1e13..1e16, long.MaxValue): `OverflowException` (>= 1e15) or `IndexOutOfRangeException` (1e12..1e14) instead of a VALIDATION refusal. Nothing is written (rolled back). | No upper bound on a unit price or bill total. HTTP 500 instead of a refusal. Values up to 1e11 paise x 99 (about Rs 99 crore) are accepted and issued - no sanity cap at all (see O3). |
| F3 | S4 | af-pay08 | `V_PAY_09_clock_stepped_back_two_minutes_still_flags_an_equal_receipt` | Record Rs 666 UPI; step the server clock back 2 minutes; record the equal receipt again: not flagged. | `FindEqual` requires `RecordedUtc <= now`, so after a backwards clock step the earlier receipt lies in the future and the guard is blind until the clock passes it. Use `Math.Abs` of the difference. |
| F4 | S4 | af-ctr-ui | `[AF-VERIFY V-CMAP-02]` | Open shift; run `CTR_BILL_ISSUE` with method `cash` (lower case, not a master name): mapper sends it with no `tillId` (the same for ` Cash `). | `mnyIsDrawer` is an exact-name test, so the precheck does not fire; the server then resolves `cash` to Cash and refuses ("Choose the till...") - no wrong posting, but the phone shows a server error. The UI uses a `<select>` so this is only reachable by a stale or hand-made command. |
| F5 | S4 | af-ctr-ui | `[AF-VERIFY V-CMAP-04]` | `CTR_BILL_ISSUE` mode `company` without a company id: sent to the server (server refuses "Choose the company account."). | Missing local precheck (the lane prechecked only mode `paid`). |
| F6 | S4 | af-sync-ops | `[AF-VERIFY V-OPS-02]` | `RPT_FAV_TOGGLE` sent through raw `transport.post` has no `Idempotency-Key` header and is never retried. | A toggle is not idempotent: a response lost after the server flipped the favourite makes the user's manual retry flip it back. Harmless to money; document or use a set (PUT true/false) endpoint. |

Totals: 6 red (S1 0, S2 1, S3 1, S4 4).

## Observations (green tests, recorded because the plan asks how the system behaves)
- O1 `V-CTR-07`: same key + same payload = replay; same key with a different spelling of the method (`cash` vs `Cash`) = IDEMPOTENCY_MISMATCH (correct). Two different keys with identical payload = two bills, two invoice numbers (as the plan expects).
- O2 `V-CTR-08`: mode `udhaar` / `company` ignore a stray `method` and a `tillId`; no drawer movement, method not stored. Mode `company` + unknown company id = refused, zero writes.
- O3 No maximum on a single counter bill (see F2); the price of a catalogue extra can be overridden to 1 paise (DEC-241 pending, recorded as current behaviour).
- O4 `V-PAY-01`: window is inclusive at exactly 10:00 (refused), 10:01 allowed, 9:59 refused. Same amount + same tender is flagged even with a different reference (by design). Four simultaneous equal payments on different keys post exactly one (`V-PAY-08`, the folio lock holds).
- O5 `V-PAY-03`: a refused request stores **no** `plat.idempotency` row (the runner stores the row in the same transaction as the work); a confirmed resend under the SAME key works (no IDEMPOTENCY_MISMATCH) and its replay is returned.
- O6 `V-CRM-10`: `"!!!"` and `"???"` are two customers; the identical name `"!!!"` twice is one customer (same raw bytes = same bucket). The plan wording ("`!!!` twice -> two customers") cannot be met by the specified hex fallback unless the names differ; I tested the different-name case.
- O7 `V-CRM-11`: audit rows (`plat.audit_event.target`) keep the raw key (`crm:<mobile>`) - stored rows stay raw by design; who may read the audit trail is outside this wave (Owner/Manager screens). Recorded for the owner's review.
- O8 Folio `PAYMENT` precheck in `money.ts` still uses the fixed list Cash / UPI / Card / Bank transfer and `tillFor` tests the literal "Cash", so a renamed drawer method in the master cannot be used on the folio (BIL-01 only fixed the counter). Pre-existing, out of scope; S4 for the next masters wave.
- O9 `V-REG-01` (CTL-02): three corrections (issued / rejected / pending) with 1, 2, 1 changes keep their own decider, approval id and changes; no row duplication from the joins; newest first. `ListAsync` has no row limit (whole property history).
- O10 Runner (REG-07), nine hostile attempts through `node tests/run.mjs` with a temporary suite: delayed async rejection -> exit 1 and the test's own name + message in `evidence/domain-tests.json`; floating unhandled rejection -> `[runner] unhandled:` result, exit 1; custom thenable that rejects -> failed under its name; `register()` throwing after an await -> `[suite] ...` result, exit 1; reject with `undefined` / a plain string -> failed; delayed passing async test -> exit 0. A test that never settles gives Node exit 13 within 3 s and **no** evidence file (loud, not silent; S4, af-runner could add a timeout). A timer that throws after the last suite gives exit 1 but the evidence file (already written) says 0 failed.

## Attempt table (attempt -> expected -> observed -> verdict)

### Counter bills (BIL-01 server half, BIL-02) - `AuditFixVerifyCounterTests`
| Id | Attempt | Expected | Observed | Verdict |
|---|---|---|---|---|
| V-CTR-01 | paid with `Cashh`, `Cash h`, `Cas`, `C4sh`, `Cash;`, `UP1`, `Company credit`, `company credit`, `COMPANY`, `Vendor credit`, `VCREDIT`, `Udhaar`, empty, blank, null (shift open) | VALIDATION "Select a payment method (...)", list has no Company credit, zero rows in ctr.bill / ctr.bill_line / cash_movement / invoice / outbox / udhaar | as expected, 15 cases | green |
| V-CTR-02 | `cash `, ` CASH`, `CASH`, `cash` without till | resolves to Cash, "Choose the till that receives the cash.", zero writes | as expected | green |
| V-CTR-03 | `cash `, `CASH` with an open till | one bill, one drawer movement, one invoice, stored method `Cash`, outbox tender `Cash`, movement amount = bill total | as expected | green |
| V-CTR-04 | UPI / upi / Card / Bank transfer with reference null, "", "   " | "Enter the transaction reference for a non-cash payment.", zero writes | as expected | green |
| V-CTR-05 | cash with till 999999 / -1 / 0 / a till whose shift was submitted; UPI that carries a till id | refused, zero drawer rows; UPI ignores the till | as expected | green |
| V-CTR-06 | cash receipt limit (Rs 1.9 lakh bill) with `Cash`, `cash `, `CASH`; same amount by UPI | CASH_LIMIT refusal, nothing written; UPI passes | as expected | green |
| V-CTR-07 / 07b | same key replay; same key + `cash` instead of `Cash`; same key + other amount; two keys same payload | replay / IDEMPOTENCY_MISMATCH / IDEMPOTENCY_MISMATCH / two bills | as expected | green |
| V-CTR-08 | udhaar and company modes with a stray method + till; company mode with unknown / real company | method ignored, no drawer, unknown company refused | as expected | green |
| V-CTR-09 | mode `PAID`, ` paid `, `cheque`, empty | accepted (trimmed, lower-cased) / refused | as expected | green |
| V-CTR-10 | unit -1, 0, long.MinValue; qty 0, -1, 100, int.Max/Min; discount > bill, negative, equal to bill; unit 1 paise on a catalogue extra; unknown extra / menu id | VALIDATION or recorded behaviour, zero writes | as expected (1-paise override accepted = DEC-241 pending) | green |
| V-CTR-10b | qty 99 x unit 1e12 ... long.MaxValue | VALIDATION | OverflowException / IndexOutOfRangeException | **RED F2** |
| V-CTR-11 | odd qty x unit on food / laundry / banquet | net + tax = total, CGST + SGST = tax, paise-exact | as expected, 12 cases | green |
| V-CTR-12 | older-bill lookup: take 0, -5, int.Min, 100000; invoice no `%`, `_`, `INV/%`, `' OR 1=1 --`, DROP TABLE, `[a-z]%`, NUL; 41 chars; 40 chars; blank; date bounds equal / Min..Max / outside; accountant; other property id | clamped to 1..500; wildcards and injection match nothing; 41 chars VALIDATION; accountant FORBIDDEN | as expected, table intact | green |
| V-CTR-13 | void request for bill id 0, -1, unknown, long.Max; a found bill | NOT_FOUND; one approval for a real bill | as expected | green |

### Client counter mapper (BIL-01 client) - `sync-af-verify.mjs`
| Id | Attempt | Expected | Observed | Verdict |
|---|---|---|---|---|
| V-CMAP-01 | shift closed: Cash / empty method / UPI / Udhaar + Cash | Cash and empty: 0 requests; UPI and Udhaar sent without tillId | as expected | green |
| V-CMAP-02 | open shift, method `cash` (lower case) | tillId 7 or refused locally | sent without tillId | **RED F4 (S4)** |
| V-CMAP-03 | `companyId` "12" from the select; "abc" | number 12; "abc" never sent | as expected | green |
| V-CMAP-04 | company mode without company id | refused locally | sent (server refuses) | **RED F5 (S4)** |
| (lane tests) | non-"Cash" drawer method -> tillId | | covered by the lane test `[AF-CTR] a drawer method ... still sends tillId`, passes in the sweep | green |

### PAY-08 - `AuditFixVerifyPayTests` + `sync-af-verify.mjs`
| Id | Attempt | Expected | Observed | Verdict |
|---|---|---|---|---|
| V-PAY-01 | equal receipt after 0:59, 9:59, 10:00, 10:01, 60:00 | refused, refused, refused, allowed, allowed | as expected | green |
| V-PAY-02 | amount +-1 paise; other folio; other reference; tender spelling `upi`; other tender | allowed / allowed / refused / refused / allowed | as expected | green |
| V-PAY-03 | refusal stores nothing; retry same key refused again; confirmed resend same key; replay of it | no idempotency / audit rows from the refusal; no IDEMPOTENCY_MISMATCH | as expected | green |
| V-PAY-04 | `confirmDuplicate` on a non-duplicate | posts once, audit has no "confirmed second payment" | as expected | green |
| V-PAY-05 | three equal receipts, two confirmed | audit of each refers to the newest earlier receipt with minutes | as expected | green |
| V-PAY-06 | deposit vs payment; equal deposit; confirmed deposit | independent kinds; deposit flagged; confirm posts | as expected | green |
| V-PAY-07 | split: UPI + Card of equal amount; second UPI row | allowed; second UPI refused | as expected (leads to F1 at client) | green |
| V-PAY-08 | four simultaneous equal payments, different keys | exactly one receipt | one receipt | green |
| V-PAY-09 | clock stepped back 2 min | still flagged | not flagged | **RED F3 (S4)** |
| V-PAY-10 | advance on a Confirmed booking (folio 0 + reservation) twice | second refused | refused | green |
| V-PAY-11 | cash duplicate with till; confirm | no extra drawer row from the refusal; exactly +1 after confirm | as expected | green |
| V-PAY-12 | refusal text | tender, amount, IST, minutes, receipt id | as expected | green |
| V-PAY-C01 | single tender refused then confirmed (client) | 2 receipts | 2 | green |
| V-PAY-C02 | split refusal on row 2 | names rows already recorded | as expected | green |
| V-PAY-C03 | split refusal on row 2, then confirmed resend | 2 receipts | 3 (row 1 posted twice) | **RED F1 (S2)** |
| (plan) | RESULT_UNKNOWN: same key on retry; reopened dialog with new key + equal payload surfaces DUPLICATE_SUSPECTED | | the key derivation is covered by the lane tests (`pay:dup`, `pay:dup:confirm`, `confirm-tN`) and by V-PAY-C01/C02 above with the guard modelled; the server side of "new key + equal payload" is V-PAY-02/03 | green |

### CRM (PR-01, ID-01) - `AuditFixVerifyCrmTests`
| Id | Attempt | Expected | Observed | Verdict |
|---|---|---|---|---|
| V-CRM-01 | Cashier and a custom masked role (View=M + merge permission, no Edit): JSON of list, list?q=name, list?q=<phone>, profile by token, profile by n: key, duplicates, merges (pending merge present) - direct serialisation and over HTTP | no seeded phone in any format (plain, spaced, dashed, +91), no 10-digit run, no separated digit run, no e-mail local part; phone search returns nothing | as expected (2 roles x 7 endpoints x 2 channels) | green |
| V-CRM-04 | raw phone as key for masked roles: plain, spaced, 0-prefixed, +91, 91, spaced, dashed, other seeded numbers, over HTTP | NOT_FOUND with the same message as a number that does not exist (no oracle), HTTP 404 | as expected | green |
| V-CRM-05 | Owner / Reception / Manager: raw phone and token | HTTP 200, identical body, key in the answer is the token | as expected | green |
| V-CRM-06 | forged / malformed tokens (right shape, all Z, short, empty prefix, upper-case prefix, 26 chars, bad char, SQL text, `n:`, empty, spaces, other-secret token) for reads and for AddTag / ProposeMerge | NOT_FOUND, nothing written | as expected | green |
| V-CRM-07 | tokens after a restarted queries object; relation to the persisted secret | identical; token = HMAC of the stored 32-byte secret | as expected | green |
| V-CRM-08 | eight concurrent first calls on a fresh database | one secret row, identical tokens | as expected | green |
| V-CRM-09 | merge flow: same customer by raw key and by token; masked role proposes with a raw phone target; maker decides own merge; stale (merged-away) token for owner / cashier / custom; tag through the stale token; second proposal | VALIDATION; NOT_FOUND; FORBIDDEN; survivor profile with no phone for masked roles; tag lands on the survivor's raw key; refused | as expected | green |
| V-CRM-10 | Devanagari pair; same name with extra spaces; mixed script `Rahul राहुल` x2 (case/space) vs `Rahul`; `!!!` vs `???`; NFC vs NFD nukta letters; searches | separate customers where names differ, one where the same; NFKC equal; Devanagari search finds only its guest | as expected (see O6 on `!!!` twice) | green |
| V-CRM-11 | audit target | observation only (O7) | raw key kept | green (observation) |
| (golden) | FY26 prototype index 1,155 customers | unchanged | `CustomerRulesGoldenTests` green in the full run | green |

### Sync mappers and role reads (OPS-01..03) - `sync-af-verify.mjs`
| Id | Attempt | Expected | Observed | Verdict |
|---|---|---|---|---|
| V-OPS-01 | KEY_REQUEST booking `abc` / `-5`, KEY_CANCEL `0`, TRC_PUBLISH `abc`, TRC_PROPOSE empty, RPT_SCHEDULE_SAVE recipient `abc`, RPT_SCHEDULE_DELETE `x`, RPT_FAV_TOGGLE empty report, KEY_LOCK_SAVE unknown room | all refused locally, 0 requests | as expected | green |
| V-OPS-02 | RPT_FAV_TOGGLE with a space / slash report id; RPT_LOG with `../../etc` | path-encoded; Idempotency-Key header | encoded correctly; no Idempotency-Key | **RED F6 (S4)** |
| V-OPS-03 | schedule recipients `['4',5]`, weekly day `'3'` | numbers 4, 5 and 3 | as expected | green |
| V-OPS-04 | housekeeping and a keys-view-only user boot | housekeeping reads no keys / tariff / report route; keys viewer reads log + encoder, not the lock mapping, schedules or tariff | as expected | green |

### SetNetSetting (item 23) - `AuditFixVerifyNetTests`
| Id | Attempt | Expected | Observed | Verdict |
|---|---|---|---|---|
| V-NET-01 | Manager, Reception, Accountant, Housekeeper x every switch | FORBIDDEN, no change rows, `ops.egress_firewall_state` untouched | as expected | green |
| V-NET-02 | unknown / mis-spelt keys (`NET.OTA`, `net.ota.`, `net.`, empty, `hotel.name`, SQL text) | VALIDATION, nothing stored | as expected | green |
| V-NET-03 | non-boolean values for each of 7 switches ("true", "yes", 1, 0, null, array, object, "", value omitted) | VALIDATION | as expected | green |
| V-NET-04 / 04b | garbage windows as text and as JSON (25:00, days [9], missing field, array, {}, days "all"); number, boolean, 200,000-char string | VALIDATION, nothing stored; a valid window can be cleared | as expected | green |
| V-NET-05 | reason null / "" / blank / 2 chars | VALIDATION | as expected | green |
| V-NET-06 | replay, mismatch, counts of change / audit rows, wanted_state | one change, one audit, `wanted_state` untouched, IDEMPOTENCY_MISMATCH | as expected | green |
| V-NET-07 | stale expected version | STALE_VERSION, value unchanged; correct version works | as expected | green |
| V-NET-08 | generic `SetSetting` of a net.* key by a Manager | not applied | not applied | green |
| V-NET-C01 | client: non-boolean value, 121-char / blank reason, unknown key, undefined value | refused on the device | as expected | green |

### Correction register (CTL-02) and runner (REG-07)
See V-REG-01 (green) and O10 (runner).

## Regression sweep (claude/auditfix head 5b9d8dda + my test files)
| Suite | Command (through the lock) | Expected | Observed |
|---|---|---|---|
| Prototype | `HCC_TEST_ONLY='^(?!.*sync-af-verify)' npm test` (filter only leaves out my own new file, the inline tests and m1 always run) | 2827/2827 | **2827/2827** passed (21 async), same as af-int |
| Server | `dotnet test Hcc.slnx -maxcpucount:1 --logger trx` (full, 42 projects) | 4162 passed, 0 failed | **4252 passed, 9 failed, 4261 total**: the 4162 baseline tests all pass; the 99 server tests I added give 90 green + 9 red (V_PAY_09 = F3, eight `V_CTR_10b` cases = F2). No other failure, no difference from af-int. |
| Prototype with my file | `npm test` (no filter) | | 2827 + 13 added tests: 4 of mine red (V-CMAP-02, V-CMAP-04, V-PAY-C03, V-OPS-02) |

Logs: `.scratch/npm-full.log`, `.scratch/dotnet-full.log` (not committed). The red tests will make the gate suites red until F1..F6 are fixed or the tests are consciously quarantined by the coordinator.

## Not run
- Browser checks (fit tool, phone checks): not run in this lane; no UI file was changed by me and the Playwright/Chrome paths were not needed for the attempts above.
- Emulator, device, real SQL Server on the hotel PC: not applicable.
- No LG dry run (needs the hotel PC) - owed after af-crm, as the plan says.

## HANDOFF bullet
af-verify (wave 4): 100+ hostile attempts across counter bills, PAY-08, CRM keys/names, sync mappers, SetNetSetting, correction register and the runner. Red: F1 S2 (confirmed resend of a split payment re-posts row 1: owner af-pay08 client), F2 S3 (absurd counter amounts crash with 500: af-ctr-svc), F3-F6 S4. Route F1 before the gate. Suites: see Regression sweep.
