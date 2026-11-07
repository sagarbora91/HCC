# MEAS2 lane A (daily money): done, 7 Nov 2026

Branch `claude/meas2-a`, cut from `claude/meas` 79f7abe7. Model: Opus. Both twins changed together.

## Sizing claims checked

All the lane-A claims in MEAS-FOLLOWUP-SIZING.md matched the code at 79f7abe7 (line numbers too), with these additions found while checking:

- **Prototype flash room revenue also counted re-posted nights twice.** `flash.ts` summed every `kind 'room'` line, including the reversed original. It now counts live lines only (not a reversal and not reversed), the same as the server's filter.
- **Prototype allowances report classed every reversal as `correction`.** All prototype reversals are `kind 'adjustment'`, so a real void never showed as `void`, while the server shows it as `void`. Both twins now say `void`. A bill correction's credit-note line stays `correction`.
- **The server's approval join could never find the approver of an A2 bill correction.** `bill.approval.result_id` holds the new invoice id, not a charge id. The approver now comes from `c.approval_id` (for corrected lines) or, for credit-note lines, from `ivc.bill_correction`, matched by credit-note number.
- **Server flash money cells were `double`.** The ternary `px == "pct" ? actual : (long)actual` widened every cell to `double`. Money and count cells are now `long`, as the ReportRow contract requires. Percent cells stay `double`.

## Items fixed

| Item | Server | Prototype |
|---|---|---|
| M5: ADR divisor = charged nights (live room lines with amount above 0) | `MeasDailyReportsA.cs:32-42` (BasisText, `Adr`), the `Charged` sub-query in `DayAsync` (`:57-58`), and the ADR row (`:101`) | `flash.ts:19-31` (`FLASH_BASIS`, `dayFigures`) |
| L8: last year = the same calendar date (29 Feb maps to 28 Feb) | `MeasDailyReportsA.cs:35-36` `LastYear`, used at `:77` | `shared.ts:31-36` `sameDateLastYear`, used in `flash.ts` |
| L2 (flash part): integer half away from zero | `MeasDailyReportsA.cs:39` `DivRound` → `FixedCodeEngine.DivRound`; target RevPAR `DivRound(tOcc*tAdr,100)`; long cells `:94` | `shared.ts:21` `divRound` → `rptDivRound`; target RevPAR the same |
| L3: the high-balance text keeps paise | `MeasDailySupport.cs:92` and `:97` `MeasDailyRules.Rupees` | `shared.ts:38` `rupeesText`; `high-balance.ts:30` |
| N3: rate re-posts are kind `rate-change`; approver | `MeasDailyReportsB.cs:23` (Kinds), `:35` (BasisText), `:37-75` (SQL) | `allowances-voids.ts:13`, `:17-19` (`isRateRepost`), `:32-41`, `:57` (daily cap) |
| N4: room discrepancy age in local time, from the right start | `MeasDailyReportsA.cs` `OooSince` (after the definition) and the SQL columns 4-7 (`:301` onwards) | `room-discrepancy.ts:24-31` |

### N3 rules (both twins)

- **Identifying a re-post.** A rate re-post reversal is the reversal row that StayRateCommands writes: server label `Reversal · rate change · …`, prototype label `Reversal · … · rate change`.
- **Where its data comes from.** The report takes the user, the reason and the approver from the rate change that lists the reversal: `gst.stay_rate_change.reposted_json` on the server, `stayRateX.changes[].reposted` in the prototype.
- **Approver.** It is the change's `approved_by`. When the change was made directly by the Owner (role `OWN`), the Owner is the approver. When a Manager made the change directly within PERM-RATE-001, there is no approver, so the row is red "No approver". This follows the sizing recommendation, and it is an open point for the owner (see below).
- **Other lines.** They take their approval from the result row (`discount` / `correction`), else from the charge row's `approval_id`, else, for a credit-note line, from the correction. Accepted actions are `discount`, `correction`, `rate-change` and `bill-correction`.
- **Daily cap (prototype only).** A rate re-post no longer counts towards a user's daily discount cap. The rate-change limit governs it, not the discount limit. The server has no cap.

## Tests added

- **Server, `08 Production/tests/Hcc.Meas.Tests/MeasDailyRulesTests.cs`** (append): five `Meas2A_*` tests.
  - L2: DivRound with both signs, 2^40 paise and an exact half.
  - M5: ADR over 3 charged nights; blank when there are none; the basis text.
  - L8: 1 Mar 2028 → 1 Mar 2027; 29 Feb 2028 → 28 Feb 2027.
  - L3: 500050 → "5,000.50", and the lakh grouping.
  - N4: `OooSince` converts UTC to IST and takes the later of the offline start and the check-in.
- **Server, new `08 Production/tests/Hcc.Meas.Tests/MeasDailySqlTests.cs`**: an SQL round trip on a fresh `HccTest_*` database, with the re-posts written exactly as StayRateCommands writes them (correction reversal, `reposted_json`, approval on the charge row).
  - Flash: 3 paid nights and 1 zero-rate night, including two re-posted nights. ADR = 200000 (not 150000) and not red; occupancy 80.0; RevPAR 120000; last year blank.
  - Allowances: an approved re-post, an Owner direct re-post and a Manager direct re-post (red), plus a real void without approval (red). Only those two are red, and the `kind=rate-change` filter returns 3 rows.
  - Room discrepancy: the room was marked offline 61 minutes ago. Age is 1 h, the row is not red, and "since" is local time.
- **Prototype, `03 Prototype/tests/stream-meas-rpt-daily.mjs`** (append): six `[F12-MEAS2-A]` tests (M5, L8, L2, L3, N4, N3), twins of the above.
- **Golden updated:** `03 Prototype/tests/rpt-golden/goldens/rpt.guests.manager-flash.json`, written by `node tests/rpt-golden/accept.mjs`. Only this golden changed, and only its ADR row. The BASE fixture has no room charge tonight, so ADR is now blank and not red, where before it was 0 and red. RevPAR is unchanged.

## Commands run, with the counts they reported

All commands ran through `tools/Invoke-Serialized.ps1 -Label meas2-a`.

- In `03 Prototype`:
  - `npx tsc --noEmit`: OK.
  - `node tests/rpt-golden/accept.mjs`.
  - `HCC_TEST_ONLY='meas-rpt-daily|meas-digest|goldens|rpt-engine|rpt-guests-rooms' node tests/run.mjs`: **130/130 passed**, including the golden drift check and catalogue.json.
- In `08 Production`:
  - `dotnet test tests/Hcc.Meas.Tests -maxcpucount:1`: **105/105 passed**.
  - `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1`: **117/117 passed**.
  - No warning lines appeared in the build output.

## Not done or not verified

- The full prototype `npm test` and the full `dotnet test Hcc.slnx` were not run. They belong to the wave gate.
- **N4 twin gap.** The prototype offline capture (`MeasOffline`, `measure-types.ts`) records no set time, so a prototype room that is only marked offline starts its mismatch at the check-in. The server uses `meas.room_offline.set_utc`. The twins agree whenever a block exists or the mark is older than the check-in. Closing the gap needs a set time in the capture, outside this lane's files.
- **A2 bill corrections are still shaped differently on the two twins.**
  - The server lists every corrected line (kind `correction`) and every credit-note line.
  - The prototype lists the credit-note lines plus one summary row per issued correction (`blcState`); its re-posted lines are ordinary lines.
  - The approver now resolves on both twins, but the row counts differ.
  - Not changed: the owner decision covered rate re-posts only.
- **Owner decision needed.** Should a direct Manager rate change within PERM-RATE-001 count as approved (approver = that Manager), or stay red as it does now?
- **Study side of M5.** The study's charged nights (`SqlAnalyticsInputSource.cs:157`, amount > 0) belong to lane E.
- **Refund dates.** In the voids report, refund dates are still `CAST(executed_utc AS DATE)`, which is the UTC day. This is not in lane A's item list.
