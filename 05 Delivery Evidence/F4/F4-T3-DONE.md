# F4-T3 quick wins — done (branch claude/f4-t3)

**qw-audit** — `domain/streams/audit-viewer.ts` (avFilter by actor / entity type / entity id / date, avDiff before-after view, avCsv for `rpt.admin.audit-log`, money masked via `mstMaskMoney`); Audit log tab filters + diff table + CSV button (`ApprovalsAudit.tsx`). Migration 4390 (index).
**qw-feedback** — `domain/streams/feedback.ts`, `handlers/feedback.ts` (`FBK_RECORD`, `FBK_FOLLOWUP_SWEEP`, `FBK_FOLLOWUP_DONE`, `FBK_URL_SET`), `features/guest-services/Feedback.tsx` (sheet mounted in the check-out modal; bill line in the invoice modal, hidden until the address is set, Q12). Selectors for T2: `fbkSummary(s,date)` -> {count,avg,low}, `fbkOverdue(s,date)` -> {complaints,lostFound,total,rows}, `fbkOverdueLine`, `fbkSummaryLine`. Migrations 4393 (`ops.guest_feedback`), 4394 (`ops.feedback_followup`), 4395 (PERM-FBK-001..003). Server rules `Hcc.Domain/GuestServices/FeedbackRules.cs` + test.
**qw-alerts** — `qwReorderAlerts(s)` (= `invAlerts`), `qwReorderLine`; Stock banner (`features/stock/ReorderBanner.tsx`); `app/LanHealth.tsx` (production build only, "Server unreachable — changes are kept on this tablet" from `readOnly` sync state); printers in `syshealth.ts` F4-T3 block + Printers panel on System health with "Print test page" (`QW_PRINTER_TEST`, `QW_PRINTER_SET`). Migration 4397 (`plat.printer`).

## For the integrator
- **Day-close hook not wired**: call `FBK_FOLLOWUP_SWEEP` (needs `close` permission) from the close handler in the T2/closing block.
- **Owner follow-up "task"** is the `FBKFollowUp` row itself (open/done), not an `s.tasks` entry: a new `Task.kind` ripples into housekeeping type tables. Owner list UI is not built; the digest reads `fbkOverdue`.
- Types: `FBKState` gained optional `url?`. `SYSState` gained optional `printers?`.
- Server: only migrations + pure rules + test; no endpoints/store (tables are ready). Table names use schema `ops.` not `gsv.`; reserved 4391/4392/4396/4398/4399 unused. Lock added with `--accept-out-of-order` (4720+ already present).
- Catalogue row for `rpt.admin.audit-log` not added (no prototype catalogue file); id exported as `AV_REPORT_ID`.
- Tests: `stream-audit-viewer.mjs`, `stream-feedback.mjs`, `stream-qw-alerts.mjs`; server `FeedbackRulesTests`.
