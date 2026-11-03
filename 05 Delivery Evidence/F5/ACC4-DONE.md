# F5: ACC-4 and the internet egress gate (DONE, cloud run 2–3 Nov 2026)

Branch `claude/f5`. It contains `main` with F1–F4, the lanes E1 (egress), A1 (year lock and bank) and U1 (UI and tests), and the integrator's fixes. Contracts: `02 Planning/agents/ACC4-CONTRACT.md`, `EGRESS-CONTRACT.md`. Lane notes: `F5-E1-DONE.md`, `F5-A1-DONE.md`, `F5-U1-DONE.md`, `F5-int-phase1.md`.

## Built
- **Year end:**
  - Preview, then carry-forward of balance-sheet ledgers into the next FY opening. The carry-forward is a draft until the Owner approves it.
  - Lock, reopen and relock.
  - The prototype refuses posting into a locked year with `ACC_YEAR_LOCKED`. On the server, `YearLockedCommandRunner` wraps the platform runner: every command whose date or month falls in a locked year is refused, and a command with no date is checked on the business date.
  - Exports from a reopened year carry "PROVISIONAL — year reopened": ACC-3 reports, Tally XML, and the trial balance, day book, statement and voucher CSVs, on both sides.
- **Bank reconciliation:**
  - Statement import with a layout mapping master. The generic layout (date, narration, ref, debit, credit, balance) comes first, because no sample from the owner's bank has been received (A23 default).
  - Auto and manual matching, and vouchers created from statement lines.
  - B8 UPI / card clearing settlement (1010 / 1020 → bank, net of MDR).
  - Reconciliation report and the clearing view.
- **Carry-overs:** C1 and C4 are done. C2 checks the posted GST set-off journal. C3 reads ageing from the opening date. C5 was already in place, and C6 is verified by a test.
- **Egress gate:**
  - `Hcc.Infrastructure.Egress`: one named `HttpClient("egress")` with `EgressGateHandler` as its outermost handler.
  - The `net.*` settings are Owner-only, need a reason, are audited and **default to OFF** (fail-closed).
  - A refused call gives `NET_OFFLINE` (HTTP 503) before DNS. Outbox items are held while the gate is off and replayed in order when it opens.
  - OCR scanning is gated by `net.ocr`.
  - System health has an Egress block, plus `GET /admin/system-health/egress` (PERM-NET-002).
  - An architecture test fails the build on any HttpClient / WebRequest / SmtpClient / Socket use outside the egress project.
- **UI:**
  - Accounting tabs Year end, Bank reconciliation and Clearing, with phone cards.
  - Master configuration › Internet access: the Owner edits with a reason prompt, the Manager reads, and other roles never see it.
  - Banners ("Internet off — messages queue", OTA last sync, including the phone OTA screen) and a held-queue badge on Messages.
  - APK `server.allowNavigation` is `["localhost"]`, because the server address is typed on the device.
- **Migrations:** 4280–4289 and 4293 (ACC-4), 4730–4734 (egress), all added out of order below 4721 with the lock tool. New test projects: `Hcc.Egress.Tests` and `Hcc.Architecture.Tests` (40 projects).

## Integration fixes
- F4 is merged into F5. The append blocks are kept and the lock was regenerated.
- The operator alert categories now include `EgressOutboxWorker`.
- `_role-reads.json` was regenerated on the merged code. The only change is the PERM-NET-001/002 rows, which are F5's egress permissions.

## Results (cloud, Linux, SQL Server 2022 in Docker)
- Prototype tests: **2571 / 2571**. That compares with 2484 on main before F4, and 2539 with F4. `tsc` clean; `npm run build` OK.
- `dotnet build Hcc.slnx`: **0 warnings, 0 errors**.
- **Server suite, run one project at a time** (because the container restarted three times): 40 projects, **3946 passed, 7 failed**.
  - 6 of the failures are the known Linux-only ones, which also fail on main: the DPAPI key ring, the Legacy absolute-folder rule, 3 Operations hosting checks, and the room-board page ceiling.
  - The 7th was `RoleReadMatrixTests`. The fixture was regenerated as described above and re-run green.
- Golden tests: April 2026 carries forward to the rulebook §8.5 balance sheet, and the fictional `bank-april-2026` statement reconciles to **zero**, on both the prototype and the server.

## Not run here (hotel PC)
- `tools/egress-proof.ps1`: the **zero-outbound proof** (hosts-file sink, e2e with `net.internetEnabled=false`, 0 connections) and `deployment/windows/Set-HccEgress.ps1` together with the firewall task. Both are Windows-only and were not executed.
- `tools/Start-Gate.ps1` on Windows.

## Gaps
- Payment-link gating: there is no payment-link feature yet (F9). The gate applies when one is built.
- The server books used in the golden carry-forward are the golden April data, not a live SQL ledger.

## DEC proposals (owner to approve)
- **DEC-223:** a year lock refuses every posting dated in the locked year on both sides. Reopening is Owner-only and stamps exports PROVISIONAL until relocked. Carry-forward is a draft that the Owner approves.
- **DEC-224:** bank reconciliation starts with the generic CSV layout. The owner's bank layout becomes a mapping-master row when a sample arrives (A23). UPI / card settle to the bank net of MDR.
- **DEC-225:** internet egress is OFF by default (fail-closed) at go-live. It is switched per integration by the Owner, with a reason and an audit record. Messages queue while it is off. No code may reach the internet except through the egress gate, enforced by the architecture test.
- **DEC-226:** APK `allowNavigation` is limited to `localhost`, because the HCC server address is entered on the device.
