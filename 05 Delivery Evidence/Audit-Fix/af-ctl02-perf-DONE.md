# af-ctl02-perf DONE (wave 2)

Lane af-ctl02-perf / Sonnet / branch `claude/af-ctl02-perf` / worktree `E:\Projects\HCC\worktrees\af-ctl02-perf` / base 6a29f3e6 plus `claude/auditfix` merged (wave-0 runner fix).

## Commits
See `git log --oneline 6a29f3e6..HEAD` (CTL-02 register, PER-01 benchmark, measurement + DONE; the merge of `claude/auditfix` comes first).

## Files
Server: `08 Production/src/Hcc.Application/Invoices/BillCorrectionService.cs` (list row extended); `08 Production/src/Hcc.Infrastructure.SqlServer/LfMoney/SqlBillCorrectionStore.cs` (ListAsync).
Tests: new `08 Production/tests/Hcc.Invoices.Tests/Sql/BillCorrectionRegisterTests.cs`; new `08 Production/tests/Hcc.Customers.Tests/Perf/CustomerQueryBenchmark.cs`.
Docs: `05 Delivery Evidence/Audit-Fix/PER-01-measurement.md`, this file. Nothing under `03 Prototype`.
Path note: the brief names `Hcc.Infrastructure.SqlServer/Invoices/SqlBillCorrectionStore.cs`; the file actually lives in `.../LfMoney/`. The existing SQL correction tests are in `tests/Hcc.Counter.Tests/Sql/BillCorrectionTests.cs` (not edited); the new tests went to the owned `Hcc.Invoices.Tests` project, which links the same `Hotel` harness. Line endings: all server files, tests and the benchmark are CRLF as before; evidence docs LF.

## What was built
CTL-02: `BillCorrectionListRow` gained, appended at the end, `int RequestedBy, long ApprovalId, int? DecidedBy, DateTime? DecidedUtc, IReadOnlyList<StoredBillChange> Changes`. `SqlBillCorrectionStore.ListAsync` is one command: a table variable holds the property's correction ids, then two result sets (headers joined to `bill.approval` for `decided_by` / `decided_utc`; changes from `ivc.bill_correction_change` ordered by correction, seq), merged in memory. Pending corrections give null decider / decided time (the approval row is stamped only on decision). `GET /invoices/corrections` already returns `ListAsync` rows, so the JSON is additive (camelCase `requestedBy`, `approvalId`, `decidedBy`, `decidedUtc`, `changes[]`); the endpoint file is untouched. No rule, permission or posting changed.
PER-01: measurement only (see `PER-01-measurement.md`). The benchmark returns at once without `HCC_PERF=1`; its databases were dropped.

## Tests (commands through the lock)
- `dotnet build Hcc.slnx`: 0 warnings, 0 errors.
- `dotnet test tests/Hcc.Invoices.Tests -maxcpucount:1 --logger trx`: 54 before, 56 after (new `Correction_register_row_carries_requester_approval_decider_and_changes`, `Pending_correction_has_null_decider`), all pass.
- `dotnet test tests/Hcc.Counter.Tests -maxcpucount:1 --logger trx`: 22/22 (existing LF correction test with the `ListAsync` check).
- `dotnet test tests/Hcc.Integration.Tests -maxcpucount:1 --logger trx`: 22/22.
- Benchmark: `HCC_PERF=1 HCC_PERF_N=1000,10000` then `30000`, `dotnet test tests/Hcc.Customers.Tests --filter FullyQualifiedName~CustomerQueryBenchmark -maxcpucount:1`: passes (the Customers suite gains 1 test, a no-op without HCC_PERF).

## PER-01 headline numbers (Debug build, this PC; p50 / p95 ms; MB allocated per call)
1k: 29-38 / 30-44 ms, 4.5 MB. 10k: about 295 / 330 ms, 43-46 MB. 30k: 828-877 / 871-935 ms, 123-134 MB. Same for list page 1 or 20, any q, and ProfileAsync. Q7 recommendation: approve the rewrite (the allocation limb of the default rule is met at 10k and 30k; p95 at 30k is under 1 s).

## Open items
- `q="900012"` hit nothing because the generated phones never contain that digit string; timing is unaffected (full load either way).
- Logical reads were not captured.

## Integration needed (for af-int)
1. `03 Prototype/src/clients/sync/lfmoney.ts` (`SrvCorrection` type and `billCorrectionsFromServer`, lines about 28-35). Replace with:
```ts
type SrvCorrectionChange={seq:number;kind:string;night:string|null;oldRatePaise:number|null;newRatePaise:number|null;chargeId:number|null;label:string|null;
  amountPaise:number|null;gstCategory:string|null;serviceDate:string|null};
type SrvCorrection={correctionId:number;oldInvoiceNo:string;creditNoteNo:string|null;newInvoiceNo:string|null;status:string;oldTotalPaise:number;newTotalPaise:number;
  deltaPaise:number;settlement:string|null;reason:string;requestedUtc:string;requestedBy:number;approvalId:number;decidedBy:number|null;decidedUtc:string|null;
  changes:SrvCorrectionChange[]};
function blcChange(c:SrvCorrectionChange):BLCChange|null{
  if(c.kind==='room-rate'&&c.night)return {kind:'room-rate',night:String(c.night).slice(0,10),oldRate:c.oldRatePaise??0,newRate:c.newRatePaise??0};
  if(c.kind==='remove-line'&&c.chargeId!=null)return {kind:'remove-line',lineId:protoId(c.chargeId)};
  if(c.kind==='add-line')return {kind:'add-line',label:c.label??'',amount:c.amountPaise??0,gstCategory:c.gstCategory??'',date:String(c.serviceDate??'').slice(0,10)};
  return null;
}
export function billCorrectionsFromServer(rows:SrvCorrection[],bookings:{id:string;invoiceNo?:string}[]):BLCState{
  const bookingOf=(inv:string)=>bookings.find(b=>b.invoiceNo===inv)?.id??'';
  return {corrections:rows.map(r=>({id:String(r.correctionId),booking:bookingOf(r.oldInvoiceNo),oldInvoiceNo:r.oldInvoiceNo,
    ...(r.creditNoteNo?{creditNoteNo:r.creditNoteNo}:{}),...(r.newInvoiceNo?{newInvoiceNo:r.newInvoiceNo}:{}),
    changes:(r.changes??[]).map(blcChange).filter((x):x is BLCChange=>x!==null),reason:r.reason,requestedBy:protoUserId(r.requestedBy),at:utcToClock(r.requestedUtc),
    approvalId:protoId(r.approvalId),...(r.decidedBy!=null?{approvedBy:protoUserId(r.decidedBy)}:{}),...(r.decidedUtc?{decidedAt:utcToClock(r.decidedUtc)}:{}),
    status:r.status as BLCCorrection['status'],oldTotal:r.oldTotalPaise,newTotal:r.newTotalPaise,delta:r.deltaPaise,
    ...(r.settlement?{settlement:r.settlement as NonNullable<BLCCorrection['settlement']>}:{})}))};
}
```
Import `BLCChange` from the types module if not already imported. Notes: `remove-line` carries the server charge id (use whatever id scheme the client uses for `bill.charge` rows if `protoId(chargeId)` does not match the prototype line ids); `approvedBy` comes from the approval's decider, so a Rejected correction also shows who decided; if the UI must show an approver only for Issued, set it only when `status==='Issued'`.
2. `03 Prototype/tests/sync-lf-finish-reads.mjs`: if it fakes `GET /invoices/corrections`, add to each fixture row `requestedBy:2,approvalId:9,decidedBy:null,decidedUtc:null,changes:[]`, and one decided row with `decidedBy:1,decidedUtc:'2026-10-24T05:00:00Z',changes:[{seq:1,kind:'room-rate',night:'2026-10-23',oldRatePaise:200000,newRatePaise:150000,chargeId:null,label:null,amountPaise:null,gstCategory:null,serviceDate:null}]`, with an assertion that `changes[0].newRate===150000`.

## HANDOFF bullet
af-ctl02-perf (wave 2, 4 Nov): CTL-02 closed on the server (the correction register carries requester, approval, decider and stored changes in one batch; 2 new SQL tests; client mapping owed to af-int). PER-01 measured: customer list / profile cost is linear, about 29 ms and 4.3 MB per 1,000 guests (30k: 0.85 s, 125 MB per call); recommendation: approve the SQL-side rewrite (Q7). See `05 Delivery Evidence/Audit-Fix/PER-01-measurement.md`.

## Secret scan and line endings
Secret scan (Invoke-SecretScan, base 6a29f3e6): ok=true, 0 findings, 507 lines in 12 files. Line endings: `git diff --stat claude/auditfix..HEAD` and `--ignore-cr-at-eol` show identical counts (316 insertions, 8 deletions); `SqlBillCorrectionStore.cs` is LF in the repo (kept LF), `BillCorrectionService.cs` and the new test files are CRLF.
