# af2-ctl01 — CTL-01 reduction threshold against the agreed rate (F9 digest lines) — DONE

Branch `claude/af2-ctl01` (from `origin/main` e949a9e5). Lane: audit CTL-01 / AUDITFIX-PLAN §2 Q4 (item 27), built with the plan's default answer.

## The rule (as built)

- A rate change of an in-house stay (STAY_RATE_CHANGE / `ChangeStayRate`, the legacy F9 "Rate and pax change") is judged against **two** bases and the **stricter wins**:
  1. the **tariff** — unchanged: a Manager may go at most PERM-RATE-001 (15 %, DEC-063 role matrix, Access control) below the room type's tariff;
  2. **new: the stay's agreed rate** — a Manager may go at most the Owner's threshold below the rate agreed on the booking.
- **Agreed rate** = the rate on the booking when the stay started (a guest's agreed rate B1 is applied at booking / check-in, so it is included), **moved only by the Owner**: an Owner-set or Owner-approved rate becomes the new agreed rate; a Manager's change does not, so a stay cannot be walked down in 15 % steps.
- **Threshold**: new setting `rate.agreedReductionBps` in Master configuration › Rooms and rate plans, type percent, **Owner only** (money group, DEC-058), **default 15 %** (= the plan's Q4 default: the Manager's PERM-RATE-001 limit measured against the agreed rate as well). Immediate; pending requests are not re-evaluated.
- Floor = agreed × (100 % − threshold), rounded up. **At the floor passes; one paisa below needs the Owner** (direct change → approval request; a Manager cannot approve it; the Owner can).
- Agreed rate lower than the master tariff: the tariff floor stays the binding one (e.g. agreed ₹1,800, tariff ₹2,000: ₹1,700 direct, ₹1,699.99 approval).
- The audit's reproduction (₹2,500 → ₹1,900 on a ₹2,000 tariff, approved by a Manager) now needs the Owner.

## Records, audit, digest

- Each applied change records `tariff`, `agreedRate` (before), `agreedAfter`, `thresholdBps`, `overThreshold` (prototype `s.stayRateX.changes[]`; server `gst.stay_rate_change` new columns, **migration 4920** `4920_rate_change_agreed_threshold.sql`, in the lock).
- Audit: the STAY_RATE_CHANGE / STAY_RATE_CHANGE_REQUESTED texts carry "tariff · agreed · bps below agreed · over the threshold (Owner)"; a change beyond the threshold writes a separate **RATE_REDUCTION_OVER_THRESHOLD** audit line (who changed, who approved or "applied directly", how far below, reason). Approval meta (prototype) carries `agreedRate` / `overThreshold` for the approver.
- Owner digest (F4-T2, Owner only PERM-DGS-001): the e-mail draft gets "Rate reductions beyond the agreed-rate threshold: N" and one line per change — `Room 101 · <booking> · agreed ₹2,500 → ₹1,900 (24% below; threshold 15%) · tariff ₹2,000 · by <changed by> · approved by <approver> | applied directly · <reason>`. WhatsApp carries only the count ("Rate cuts beyond threshold N", DEC-247: no money, no names). The Owner digest page shows the same lines.
- Server: `DigestFacts.RateReductions` + `DigestBodies.RateLine`; the day-close projector (`DigestDraftProjector`) reads them with `SqlRateReductionDigest` (filtered index on `over_threshold = 1`). `GET /stays/rate-changes` returns the new columns and the production client maps them (`clients/sync/lfmoney.ts rateChangesFromServer`).
- Permissions: no new permission. PERM-STY-010 (request), PERM-RATE-001 (A Owner / L Manager), Master configuration money group (Owner only), PERM-DGS-001 (digest, Owner). Separation of duty unchanged (never decide your own request).

## Files

Prototype: `src/domain/streams/stay-rate.ts` (strThresholdBps, strAgreedRate, strAgreedFloor, strReductionBps, strOverThreshold, strJudgedText; strCanDirect / strCanApprove), `src/services/mock/handlers/stay-rate.ts` (strApply records + audit line), `src/services/mock/handlers/folio.ts` (approval refusal text only, LF-RATE block), `src/domain/streams/config.ts` (setting), `src/domain/streams/digest.ts` (dgsRateCuts, dgsRateLine, card / e-mail / WhatsApp), `src/features/digest/OwnerDigest.tsx`, `src/features/lfmoney/modals.tsx` (agreed rate + judged text on the form), `src/domain/types.ts` (optional STRRateChange fields; no SCHEMA bump: optional fields), `src/clients/sync/lfmoney.ts` (rate-change row mapper only).
Server: `Hcc.Domain/Stays/StayRateRules.cs`, `Hcc.Application/Stays/StayRateCommands.cs`, `Hcc.Application/Stays/RateControlSettings.cs` (new), `Hcc.Infrastructure.SqlServer/Stays/SqlStayRateStore.cs`, `Hcc.Domain/Reporting/DigestBodies.cs`, `Hcc.Infrastructure.SqlServer/Reporting/SqlRateReductionDigest.cs` (new) + `DigestDraftProjector.cs`, `Hcc.Application/Counter/LfMoneyReads.cs` + `SqlLfMoneyReadStore.cs` (read columns), `Hcc.Api/Endpoints/LfMoneyEndpoints.cs` (settings provider), `database/migrations/4920_…sql` + `migrations.lock.json` + `Hcc.slnx`.
Not touched: folio payment precheck, counter-bill caps / price logic, money.ts.

## Tests

- Prototype: `tests/stream-af2-ctl01.mjs` (7: setting Owner-only + default, at threshold passes / one paisa over needs the Owner, the audit case, agreed lower than master, rate changed mid-stay, Owner raises the threshold, digest lists the lines), `tests/sync-af2-ctl01.mjs` (2: mapper). Existing LF-RATE and digest suites unchanged and green.
- Server: `Hcc.Stays.Tests/StayRateTests.cs` (+4 rule tests), `Hcc.Counter.Tests/Sql/RateThresholdTests.cs` (6 SQL: at threshold / one paisa over + Manager2 refused + Owner executes + audit line; the audit case; agreed lower than master; mid-stay; Owner sets the threshold (Manager refused); digest lines via SqlRateReductionDigest and the projector's drafts), `Hcc.Closing.Tests/Unit/RevenueByCategoryTests.cs` (+1 digest body test, +1 assert).
- Counts (run through tools\Invoke-Serialized.ps1 on this PC, 5 Nov 2026): **prototype 2849/2849** (was 2840; +9), `npx tsc --noEmit` clean; **server 4272/4272 in 42 projects** (was 4261; +11), `dotnet build Hcc.slnx` 0 warnings 0 errors. The first full run failed one test (MastersMigrationUpgradeTests: 4920 not listed in Hcc.slnx); fixed, Hcc.Masters.Tests re-run 36/36.

## Assumptions for the owner to confirm

1. **CTL-01 / Q4 (a)** built as the plan's default: the stricter of tariff and agreed rate; threshold default 15 % (the Manager's PERM-RATE-001 figure; the plan gives no separate number). The Owner can change it in Master configuration.
2. **Agreed rate moves only with the Owner** (an Owner-set / Owner-approved rate becomes the new agreed rate). Alternative: every applied change resets it (allows step-down by a Manager) — not chosen.
3. **DEC-241 (typed counter-bill price below master)**: per the Fable default noted in the plan (§2 Q1 option (b)), master − typed price is treated as a **discount under the DEC-063 / DEC-219 limits** (Cashier none, Reception ₹250, Manager ₹1,000, Owner unlimited). The Decision Register still lists DEC-241 as "Proposed — owner approval pending" and says nothing else, so the default stands. That enforcement is the af2-counter-caps lane's (counter bill caps); this lane does not touch counter bills. **Owner to confirm DEC-241 (b).**
4. Q4 (b) lists six digest line types (rate changes with both prices and names, zero-rate nights, waived fees, refunds with their receipt, corrections, reopens). This lane adds the **rate changes beyond the agreed-rate threshold** (the CTL-01 line the brief asks for). The other five are not in this lane.
5. Folio discounts (PERM-FOL-003, DEC-063 amounts) keep their rupee limits; they are not re-measured against the agreed rate.
