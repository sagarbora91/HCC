// Accounting, expenses & compliance area (W4-7). Server modules: Accounting & Tally (W2-3), Expenses (W2-4), Compliance (W2-5).
//
//   loaders   accountingX ← GET /accounting/days?from&to (the server's evaluated days for the last 62 business dates: vouchers,
//                           exceptions, reconciliation, status, lock), /accounting/trial-balance?cumulative (opening balances
//                           before the window; N5: the server adds the imported opening trial balance from its "as of" date — when
//                           that date is inside the window, its rows come as `opening` and become a voucher on that date), /accounting/mapping (+/history), /accounting/locks/history. The prototype
//                           selectors read `accountingX.server` (domain/streams/accounting.ts) instead of deriving vouchers on the
//                           device; locked days also fill `locks` (who/when, snapshot) for the lock / unlock dialogs.
//             expenses    ← GET /expenses (window of 62 days + every Draft / Pending, pages of 100). Version kept per expense. DEC-120: description,
//                           GST on the bill, source, void, edit log (→ editLog / editors), hasBillPhoto (the photo: GET /expenses/{id}/bill-photo on demand).
//             expensesX   ← GET /expenses/registers (DEC-120 SCC registers: budgets, recurring templates, petty-cash top-ups, Udhaar, month locks).
//             complianceX ← GET /compliance/state (SCC tax module in full, DEC-116: firm profile, FY, records per FY and card,
//                           custom obligations, archived keys / categories, evidence file list, reopened log, settings) and
//                           /compliance/ca-packs. Cards, due dates and the control layer are computed by the same SCC rules on both sides;
//                           the ≈₹ payable feed (GET /compliance/payable), an evidence file and the evidence ZIP are fetched by the screens.
//   commands  ACC_LOCK/ACC_UNLOCK/ACC_MAP_SET/ACC_MAP_RESET → Lock/UnlockAccountingPeriod, Set/ResetLedgerMapping;
//             ACC_TALLY_EXPORT → GET /accounting/export/tally (recorded on the server, DEC-077) and the server's XML is saved;
//             EXPENSE/EXPENSE_SUBMIT/EXPENSE_APPROVE/EXPENSE_REVERSE → SubmitExpense, SubmitExpenseDraft, DecideExpense, ReverseExpense;
//             DEC-120: EXP_EDIT/EXP_VOID → EditExpense, VoidExpense; EXP_BUDGET_SET/EXP_BUDGET_COPY → SetExpenseBudget, CopyExpenseBudgets;
//             EXP_REC_SAVE/TOGGLE/DELETE/POST → Save/Toggle/Delete/PostRecurringExpense; EXP_PETTY_FLOAT/_DECIDE/EXP_PETTY_OUT → RequestPettyFloat,
//             DecidePettyFloat, RecordPettyDisbursement; EXP_UD_ADD/PAY/VOID → AddUdhaar, PayUdhaar, VoidUdhaar; EXP_MONTH_LOCK → LockExpenseMonth;
//             EXP_CSV → GET /expenses/export/ledger | /expenses/export/month (the server's SCC CSV is saved);
//             SCC decisions wave: EXP_MONTH_UNLOCK → UnlockExpenseMonth (DEC-131, Owner + reason); EXP_UD_PAY in cash carries the till with the
//             open shift and reloads the cash desk (DEC-130: a desk receipt into the drawer); EXPENSE / EXP_EDIT carry the ITC flag (DEC-135);
//             CMP_* → MarkComplianceDone, MarkComplianceNotApplicable, ReopenComplianceItem, SetComplianceFy, SaveComplianceProfile,
//             ToggleComplianceArchive, ToggleComplianceCategoryArchive, SaveCustomObligation, DeleteCustomObligation,
//             AddComplianceEvidence, DeleteComplianceEvidence, SaveComplianceSettings, GenerateCaPack.
//   versions  the server needs the record version on most commands while the prototype commands carry none: the version
//             loaded last (ids.version) is sent, so a record changed elsewhere since the last load answers STALE_VERSION.
//   till      a Cash expense decision / reversal needs the till with the open shift (DEC-052): ids 'till' → 'current' (learned by the
//             money area when it loads the shift) or the only till known on this device.
import type {Command,Expense,State} from '../../services/mock';
import type {EXPState,ExpEditLog} from '../../domain/streams/expenses-scc';
import type {ACCState,ACCVoucher,ACCServerDay,ACCLock,ACCMapRow,ACCException,ACCCheck,ACCDayStatus,ACCVoucherType,ACCSourceKind,ACCServer,ACCTBGroup} from '../../domain/streams/accounting';
import {ACC_DEFAULT_MAP,accAccount} from '../../domain/streams/accounting';
import type {CMPState,CMPPack,CMPRecord,CMPCustom,CMPFile,CMPReopened,CMPProfile,CMPCat} from '../../domain/streams/compliance';
import {cmpEmpty,CMP_DEFAULT_SETTINGS} from '../../domain/streams/compliance';
import {mnyMethodFor,mnyIsDrawer,mnyMethod} from '../../domain/streams/money-master';
import {cxSlice} from '../../domain/streams/cashout';
import {addDays} from '../../domain/money';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx} from './types';
import {serverId,protoUserId,utcToClock,paise,isoDate} from './map';
import {CASH_SLICES,cashBlockedReason} from './money';
import {saveTextFile,saveCsv} from '../../app/native';

/** GET /accounting/export/tally (TallyExportView; DEC-077 re-export / provisional dates and the export log id; A03b-05 pending accounting
 *  events — every unlocked date carried is then provisional — and the dates an export before voucher ids took to Tally). */
type SrvTallyExport={from:string;to:string;dates:number;voucherCount:number;debitPaise:number;creditPaise:number;simulated:boolean;fileName:string;xml:string;
  reexportDates?:string[]|null;provisionalDates?:string[]|null;exportId?:number|null;pendingEvents?:number|null;legacyDates?:string[]|null};
/** A03b-05: what the Accountant must know about a downloaded Tally file (provisional days, days Tally may hold without voucher ids), or undefined. */
export function tallyExportWarning(r:Pick<SrvTallyExport,'pendingEvents'|'provisionalDates'|'legacyDates'>):string|undefined{
  const pending=r.pendingEvents??0,prov=r.provisionalDates??[],legacy=r.legacyDates??[],parts:string[]=[];
  if(pending>0&&prov.length)parts.push(`Provisional: ${pending} accounting event${pending===1?' was':'s were'} not yet in the vouchers, so ${prov.join(', ')} may be incomplete — export again once Accounting shows no pending events (the vouchers keep their IDs, so the new file replaces them in Tally).`);
  if(legacy.length)parts.push(`${legacy.join(', ')} ${legacy.length===1?'was':'were'} exported before voucher IDs: if that earlier file was imported, delete those HCC vouchers in Tally before importing this one.`);
  return parts.length?parts.join(' '):undefined;
}

// ── Server DTOs (Hcc.Contracts.Accounting / Expenses / Compliance, camelCase) ──
type SrvEntry={account:string;ledger:string;debitPaise:number;creditPaise:number};
type SrvVoucher={number:string;date:string;type:string;narration:string;sourceKey:string;sourceKind:string;sub:string;debitPaise:number;creditPaise:number;
  balanced:boolean;entries:SrvEntry[];unmapped:string[]};
type SrvIssue={id:string;severity:string;kind:string;title:string;detail:string;voucher?:string|null;source?:string|null;key?:string|null};
type SrvRecon={id:string;label:string;reportPaise:number;ledgerPaise:number;ok:boolean;note:string};
type SrvLockInfo={lockId:number;lockedUtc:string;lockedBy:number;version:string};
export type SrvAccountingDay={date:string;status:string;provisional:boolean;voucherCount:number;debitPaise:number;creditPaise:number;balanced:boolean;errors:number;warnings:number;
  lock?:SrvLockInfo|null;vouchers:SrvVoucher[];exceptions:SrvIssue[];reconciliation:SrvRecon[]};
type SrvTbRow={account:string;name:string;group:string;debitPaise:number;creditPaise:number;balancePaise:number};
type SrvTrialBalance={date:string;cumulative:boolean;debitPaise:number;creditPaise:number;balanced:boolean;rows:SrvTbRow[];
  /** N5: imported opening trial balance (W4-13 Import) — its date, whether `rows` include it, and its rows. */
  openingAsOf?:string|null;openingIncluded?:boolean;opening?:SrvTbRow[]|null};
type SrvMappingRow={mapKey:string;group:string;value:string;account?:string|null;defaultAccount?:string|null;changed:boolean;uses:number;mapped:boolean};
type SrvMappingChange={changeId:number;atUtc:string;actor:number;mapKey:string;before?:string|null;after?:string|null};
type SrvLockEvent={lockId:number;businessDate:string;action:string;atUtc:string;actor:number;reason?:string|null};
export type SrvExpense={expenseId:number;ref:string;vendor:string;category:string;amountPaise:number;method:string;businessDate:string;status:string;submittedBy:number;
  notes?:string|null;receipt?:string|null;reason?:string|null;decidedBy?:number|null;decidedUtc?:string|null;reversalOf?:number|null;reversedBy?:number|null;version:string;
  /** DEC-120 SCC features (ExpenseView). */
  description?:string|null;gstRateBps?:number|null;gstPaise?:number|null;source?:string|null;sourceRef?:string|null;voidReason?:string|null;voidedBy?:number|null;
  voidedUtc?:string|null;hasBillPhoto?:boolean;createdUtc?:string|null;edits?:SrvExpenseEdit[]|null;
  /** DEC-135: 'allowed' | 'disallowed' when the bill has GST. */
  itc?:string|null};
type SrvExpenseEdit={editId:number;by:number;atUtc:string;reason:string;old:Record<string,string|number>;new:Record<string,string|number>};
/** GET /expenses/registers (ExpenseRegistersView). */
type SrvRegisters={businessDate:string;budgets:{month:string;category:string;amountPaise:number}[];
  recurring:{templateId:number;name:string;amountPaise:number;dueDay:number;category:string;method:string;vendor:string;active:boolean;createdBy:number;createdUtc:string}[];
  petty:{entryId:number;amountPaise:number;businessDate:string;status:string;note?:string|null;recordedBy:number;recordedUtc:string;decidedBy?:number|null;decidedUtc?:string|null;reason?:string|null}[];
  udhaar:{receivableId:number;name:string;mobile?:string|null;amountPaise:number;creditDate:string;dueDate?:string|null;note?:string|null;createdBy:number;createdUtc:string;void:boolean;
    voidReason?:string|null;voidedBy?:number|null;voidedDate?:string|null;settlements:{settlementId:number;amountPaise:number;date:string;mode:string;recordedBy:number;recordedUtc:string;
      /** DEC-130: taken as a desk receipt (cash into the drawer, booked in Accounting). */desk?:boolean|null;reference?:string|null}[]}[];
  months:{month:string;expensePaise:number;count:number;gstPaidPaise:number;byCategory:Record<string,number>;generatedBy:number;generatedUtc:string;stale:boolean;staleUtc?:string|null;
    /** DEC-135 / DEC-131 */gstItcPaise?:number|null;unlocked?:boolean|null;unlockedBy?:number|null;unlockedUtc?:string|null;unlockReason?:string|null;
    unlocks?:{by:number;atUtc:string;reason:string}[]|null}[]};
type SrvExport={fileName:string;csv:string;rows:number};
type SrvExpensePage={items:SrvExpense[];page:number;pageSize:number;total:number;ownOnly:boolean};
type SrvSettings={caName:string;caEmail:string;dueSoonDays:number;version:string};
/** GET /compliance/state (Hcc.Contracts.Compliance.ComplianceStateView). */
type SrvCmpProfile={name:string;pan:string;gstin:string;entity:string;gstType:string;hasEmployees:boolean;qrmp:boolean;notes:string;updatedBy?:number|null;updatedUtc?:string|null;version:string};
type SrvCmpRecord={fyStart:number;itemId:string;done:boolean;na:boolean;filedOn?:string|null;notes?:string|null;evidenceRef?:string|null;reviewerNote?:string|null;dueDate?:string|null;
  doneBy?:number|null;doneUtc?:string|null;naBy?:number|null;naUtc?:string|null;legacy?:string|null};
type SrvCmpCustom={customId:number;id:string;cat:string;name:string;freq:string;dueDay:number;months:string[];form:string;who:string;what:string;why:string;penalty:string;
  responsible:string;reviewer:string;risk?:string|null;buffer?:number|null;checklist:string[];workflow:string[];evidence:string[];updatedBy?:number|null;updatedUtc?:string|null;version:string};
type SrvCmpEvidence={evidenceId:number;fyStart:number;itemId:string;fileName:string;contentType:string;sizeBytes:number;addedBy:number;addedUtc:string};
type SrvCmpReopened={fyStart:number;itemId:string;part:string;wasDone:boolean;wasNa:boolean;filedOn?:string|null;notes?:string|null;evidenceRef?:string|null;reviewerNote?:string|null;
  dueDate?:string|null;recordedBy?:number|null;recordedUtc?:string|null;reopenedBy:number;reopenedUtc:string;reason:string};
export type SrvCmpState={asOf:string;profile?:SrvCmpProfile|null;fyStart:number;records:SrvCmpRecord[];custom:SrvCmpCustom[];archived:string[];archivedCats:string[];
  evidence:SrvCmpEvidence[];reopened:SrvCmpReopened[];settings:SrvSettings};
type SrvPack={packId:number;atUtc:string;by:number;to:string;toName:string;overdue:number;due:number;filed:number;notApplicable:number;lines:string[]};

// ── Permissions (module PERM ids do not change can(); the loaders check them to avoid 403 noise) ──
const holds=(c:{me:{permIds:string[]}},...perm:string[])=>perm.some(p=>c.me.permIds.includes(p));
/** Days of accounting (and expenses) loaded around the business date; the server limit is 62. */
export const ACC_WINDOW_DAYS=62;
/** .NET DateTime from SQL has no offset ("Unspecified") — it is UTC by convention (DATETIME2 UTC columns). */
const utc=(v:string|null|undefined)=>!v?'':/(Z|[+-]\d\d:\d\d)$/i.test(v)?v:v+'Z';
const clock=(v:string|null|undefined)=>utcToClock(utc(v));

// ── Accounting ──
function voucherFromServer(v:SrvVoucher):ACCVoucher{
  return {no:v.number,date:isoDate(v.date,'voucher date'),type:v.type as ACCVoucherType,narration:v.narration,
    source:{kind:v.sourceKind as ACCSourceKind,id:v.sourceKey,sub:v.sub},
    entries:v.entries.map(e=>({account:e.account,debit:paise(e.debitPaise,'debitPaise'),credit:paise(e.creditPaise,'creditPaise')})),unmapped:[...v.unmapped]};
}
const issueFromServer=(x:SrvIssue):ACCException=>({id:x.id,severity:x.severity as ACCException['severity'],kind:x.kind as ACCException['kind'],title:x.title,detail:x.detail,
  ...(x.voucher?{voucher:x.voucher}:{}),...(x.source?{source:x.source}:{}),...(x.key?{key:x.key}:{})});
const checkFromServer=(x:SrvRecon):ACCCheck=>({id:x.id,label:x.label,report:paise(x.reportPaise),ledger:paise(x.ledgerPaise),ok:x.ok,note:x.note});
const mapRowFromServer=(r:SrvMappingRow):ACCMapRow=>({key:r.mapKey,group:r.group,value:r.value,account:r.account??'',...(r.defaultAccount?{defaultAccount:r.defaultAccount}:{}),
  changed:r.changed,uses:r.uses,mapped:r.mapped});

/** Server day → the prototype's view of that day (+ the lock record for locked days). */
export function dayFromServer(d:SrvAccountingDay):{day:ACCServerDay;lock?:ACCLock}{
  const vouchers=d.vouchers.map(voucherFromServer);
  const day:ACCServerDay={status:(['Open','Closed','Locked'].includes(d.status)?d.status:'Open') as ACCDayStatus,vouchers,
    exceptions:d.exceptions.map(issueFromServer),checks:d.reconciliation.map(checkFromServer)};
  const lock=d.lock?{date:d.date,lockedAt:clock(d.lock.lockedUtc),lockedBy:protoUserId(d.lock.lockedBy),vouchers,debit:paise(d.debitPaise),credit:paise(d.creditPaise),count:vouchers.length}:undefined;
  return {day,lock};
}

async function loadAccounting(c:LoaderCtx):Promise<ACCState>{
  if(!holds(c,'PERM-ACC-001'))return {};
  const to=c.current('date'),from=addDays(to,-(ACC_WINDOW_DAYS-1));
  const [days,tb,rows,mapHist,lockHist]=await Promise.all([
    c.api.get<SrvAccountingDay[]>('/accounting/days',{from,to}),
    c.api.get<SrvTrialBalance>('/accounting/trial-balance',{date:addDays(from,-1),cumulative:true}),
    c.api.get<SrvMappingRow[]>('/accounting/mapping'),
    c.api.get<SrvMappingChange[]>('/accounting/mapping/history'),
    c.api.get<SrvLockEvent[]>('/accounting/locks/history'),
  ]);
  const out:Record<string,ACCServerDay>={},locks:Record<string,ACCLock>={};
  for(const d of days){const m=dayFromServer(d);out[d.date]=m.day;
    if(m.lock&&d.lock){locks[d.date]=m.lock;c.ids.learn('accLock',d.date,d.lock.lockId);c.ids.setVersion('accLock',d.date,d.lock.version);}}
  const entries=(rows:SrvTbRow[])=>rows.map(r=>({account:r.account,debit:paise(r.debitPaise),credit:paise(r.creditPaise)}));
  const opening:ACCVoucher[]=tb.rows.length?[{no:'OPENING',date:addDays(from,-1),type:'Journal',narration:`Balances before ${from}`,
    source:{kind:'closing',id:'OPENING/'+from,sub:'opening'},entries:entries(tb.rows),unmapped:[]}]:[];
  // N5: the imported opening trial balance dated inside the window (not yet in the balance before it) counts from its own date.
  const imported=!tb.openingIncluded&&tb.openingAsOf&&tb.opening?.length?isoDate(tb.openingAsOf,'openingAsOf'):undefined;
  if(imported)opening.push({no:'OPENING-IMPORT',date:imported,type:'Journal',narration:`Imported opening balances as of ${imported}`,
    source:{kind:'closing',id:'OPENING-IMPORT/'+imported,sub:'opening-import'},entries:entries(tb.opening!),unmapped:[]});
  const accounts:NonNullable<ACCServer['accounts']>={};
  for(const r of [...tb.rows,...(tb.opening??[])])if(!accAccount(r.account))accounts[r.account]={name:r.name,group:tbGroup(r.group)};
  const mapRows=rows.map(mapRowFromServer);
  // Overrides only (the selectors overlay them on ACC_DEFAULT_MAP); '' = no ledger (Suspense).
  const mapping:Record<string,string>={};
  for(const r of mapRows)if(r.account!==(ACC_DEFAULT_MAP[r.key]??''))mapping[r.key]=r.account;
  return {mapping,locks,
    mapHistory:[...mapHist].sort((a,b)=>a.changeId-b.changeId).map(h=>({id:String(h.changeId),at:clock(h.atUtc),actor:protoUserId(h.actor),key:h.mapKey,before:h.before??'',after:h.after??''})),
    lockHistory:[...lockHist].sort((a,b)=>utc(a.atUtc).localeCompare(utc(b.atUtc))).map(h=>({id:`${h.lockId}:${h.action}`,at:clock(h.atUtc),actor:protoUserId(h.actor),date:h.businessDate,
      action:h.action==='unlock'?'unlock':'lock',...(h.reason?{reason:h.reason}:{})})),
    server:{from,to,days:out,opening,mapRows,...(Object.keys(accounts).length?{accounts}:{})}};
}
const TB_GROUPS:readonly ACCTBGroup[]=['Asset','Liability','Equity','Income','Expense','Suspense'];
function tbGroup(g:string):ACCTBGroup{return (TB_GROUPS as readonly string[]).includes(g)?g as ACCTBGroup:'Suspense';}

// ── Expenses ──
export function expenseFromServer(e:SrvExpense):Expense{
  const x:Expense={id:String(e.expenseId),vendor:e.vendor,category:e.category,amount:paise(e.amountPaise,'amountPaise'),method:e.method,date:isoDate(e.businessDate,'businessDate'),
    status:e.status as Expense['status'],actor:protoUserId(e.submittedBy)};
  if(e.notes)x.notes=e.notes;if(e.receipt)x.receipt=e.receipt;if(e.reason)x.reason=e.reason;
  if(e.decidedBy!=null)x.approver=protoUserId(e.decidedBy);if(e.decidedUtc)x.decidedAt=clock(e.decidedUtc);
  if(e.reversalOf!=null)x.reversalOf=String(e.reversalOf);if(e.reversedBy!=null)x.reversedBy=String(e.reversedBy);
  // DEC-120 SCC features.
  if(e.description)x.description=e.description;
  if(e.gstRateBps)x.gstRateBps=e.gstRateBps;if(e.gstPaise)x.gstPaise=paise(e.gstPaise,'gstPaise');
  if(e.source==='petty'||e.source==='recurring')x.source=e.source;if(e.sourceRef)x.sourceRef=e.sourceRef;
  if(e.voidReason)x.voidReason=e.voidReason;if(e.voidedBy!=null)x.voidedBy=protoUserId(e.voidedBy);if(e.voidedUtc)x.voidedAt=clock(e.voidedUtc);
  if(e.hasBillPhoto)x.hasBillPhoto=true;if(e.createdUtc)x.createdAt=clock(e.createdUtc);
  if(e.gstPaise)x.itc=e.itc==='allowed'?'allowed':'disallowed'; // DEC-135 (a bill without the flag = disallowed)
  if(e.edits?.length){
    x.editLog=e.edits.map((d):ExpEditLog=>({at:clock(d.atUtc),by:protoUserId(d.by),old:{...d.old},new:{...d.new},reason:d.reason}));
    const editors=[...new Set(e.edits.map(d=>protoUserId(d.by)))].filter(u=>u!==x.actor);if(editors.length)x.editors=editors;
  }
  return x;
}
/** GET /expenses/registers → s.expensesX (ids = the server ids as strings). */
export function registersFromServer(r:SrvRegisters):EXPState{
  const budgets:NonNullable<EXPState['budgets']>={};
  for(const b of r.budgets)(budgets[b.month]??={})[b.category]=paise(b.amountPaise,'amountPaise');
  return {budgets,
    recurring:r.recurring.map(t=>({id:String(t.templateId),name:t.name,amount:paise(t.amountPaise,'amountPaise'),dueDay:t.dueDay,category:t.category,method:t.method,vendor:t.vendor,
      active:t.active,createdAt:clock(t.createdUtc),createdBy:protoUserId(t.createdBy)})),
    petty:r.petty.map(p=>({id:String(p.entryId),amount:paise(p.amountPaise,'amountPaise'),date:isoDate(p.businessDate,'businessDate'),by:protoUserId(p.recordedBy),at:clock(p.recordedUtc),
      status:(p.status==='Approved'||p.status==='Rejected'?p.status:'Pending') as 'Pending'|'Approved'|'Rejected',...(p.note?{note:p.note}:{}),
      ...(p.decidedBy!=null?{decidedBy:protoUserId(p.decidedBy)}:{}),...(p.decidedUtc?{decidedAt:clock(p.decidedUtc)}:{}),...(p.reason?{reason:p.reason}:{})})),
    udhaar:r.udhaar.map(u=>({id:String(u.receivableId),name:u.name,mobile:u.mobile??'',amount:paise(u.amountPaise,'amountPaise'),date:isoDate(u.creditDate,'creditDate'),dueDate:u.dueDate??'',
      note:u.note??'',by:protoUserId(u.createdBy),at:clock(u.createdUtc),settlements:u.settlements.map(x=>({id:String(x.settlementId),amount:paise(x.amountPaise,'amountPaise'),date:x.date,mode:x.mode,
        by:protoUserId(x.recordedBy),at:clock(x.recordedUtc),...(x.desk?{desk:true as const}:{}),...(x.reference?{reference:x.reference}:{})})),...(u.void?{void:true,voidReason:u.voidReason??'',voidAt:u.voidedDate??'',...(u.voidedBy!=null?{voidBy:protoUserId(u.voidedBy)}:{})}:{})})),
    months:Object.fromEntries(r.months.map(m=>[m.month,{month:m.month,expense:m.expensePaise,count:m.count,byCategory:{...m.byCategory},gstPaid:m.gstPaidPaise,generatedAt:clock(m.generatedUtc),
      by:protoUserId(m.generatedBy),...(m.stale?{stale:true,staleAt:m.staleUtc?clock(m.staleUtc):clock(m.generatedUtc)}:{}),
      ...(m.gstItcPaise!=null?{gstItc:paise(m.gstItcPaise,'gstItcPaise')}:{}),
      ...(m.unlocked?{unlockedAt:clock(m.unlockedUtc),...(m.unlockedBy!=null?{unlockedBy:protoUserId(m.unlockedBy)}:{}),unlockReason:m.unlockReason??''}:{}),
      ...(m.unlocks?.length?{unlocks:m.unlocks.map(u=>({at:clock(u.atUtc),by:protoUserId(u.by),reason:u.reason}))}:{})}]))};
}
async function loadExpenseRegisters(c:LoaderCtx):Promise<EXPState>{
  if(!holds(c,'PERM-EXP-001','PERM-EXP-002','PERM-EXP-003'))return {};
  return registersFromServer(await c.api.get<SrvRegisters>('/expenses/registers'));
}
/** A bill photo data URL → the wire pair (base64, content type); undefined when none. */
export function billPhotoWire(v:unknown):{billPhotoBase64:string;billPhotoContentType:string}|undefined{
  const m=/^data:(image\/(?:jpeg|png));base64,(.+)$/.exec(typeof v==='string'?v:'');
  return m?{billPhotoBase64:m[2],billPhotoContentType:m[1]}:undefined;
}
async function expensePages(c:LoaderCtx,q:Record<string,string>):Promise<SrvExpense[]>{
  const out:SrvExpense[]=[];
  for(let page=1;page<=50;page++){const r=await c.api.get<SrvExpensePage>('/expenses',{...q,page,pageSize:100});out.push(...r.items);if(page*r.pageSize>=r.total||!r.items.length)break;}
  return out;
}
async function loadExpenses(c:LoaderCtx):Promise<Expense[]>{
  if(!holds(c,'PERM-EXP-001','PERM-EXP-002','PERM-EXP-003'))return [];
  const to=c.current('date'),from=addDays(to,-(ACC_WINDOW_DAYS-1));
  const parts=await Promise.all([expensePages(c,{from,to}),expensePages(c,{status:'Pending'}),expensePages(c,{status:'Draft'})]);
  const byId=new Map<number,SrvExpense>();for(const e of parts.flat())byId.set(e.expenseId,e);
  return [...byId.values()].sort((a,b)=>a.expenseId-b.expenseId).map(e=>{c.ids.setVersion('expense',String(e.expenseId),e.version);return expenseFromServer(e);});
}

// ── Compliance (SCC tax module in full) ──
const opt=(v:string|null|undefined)=>v?v:'';
export function cmpRecordFromServer(r:SrvCmpRecord):CMPRecord{
  return {...(r.done?{done:true}:{}),...(r.na?{na:true}:{}),...(r.filedOn?{filedOn:r.filedOn}:{}),...(r.done||r.notes?{notes:opt(r.notes)}:{}),
    ...(r.done?{evidenceRef:opt(r.evidenceRef),reviewerNote:opt(r.reviewerNote)}:{}),...(r.dueDate?{dueDate:r.dueDate}:{}),
    ...(r.doneBy!=null?{by:protoUserId(r.doneBy),at:clock(r.doneUtc)}:{}),...(r.naBy!=null?{naBy:protoUserId(r.naBy),naAt:clock(r.naUtc)}:{}),...(r.legacy?{legacy:r.legacy}:{})};
}
export function cmpStateFromServer(st:SrvCmpState,packs:CMPPack[]):CMPState{
  const records:Record<string,Record<string,CMPRecord>>={};
  for(const r of st.records)(records[String(r.fyStart)]??={})[r.itemId]=cmpRecordFromServer(r);
  const custom:CMPCustom[]=st.custom.map(c=>({id:c.id,cat:c.cat as CMPCat,name:c.name,freq:c.freq,dueDay:c.dueDay,months:[...c.months],form:c.form,who:c.who,what:c.what,why:c.why,
    penalty:c.penalty,responsible:c.responsible,reviewer:c.reviewer,risk:(c.risk??'') as CMPCustom['risk'],buffer:c.buffer??null,checklist:[...c.checklist],workflow:[...c.workflow],
    evidence:[...c.evidence],urgent:false,...(c.updatedBy!=null?{updatedBy:protoUserId(c.updatedBy),updatedAt:clock(c.updatedUtc)}:{})}));
  const files:CMPFile[]=st.evidence.map(e=>({id:String(e.evidenceId),fy:e.fyStart,itemId:e.itemId,name:e.fileName,size:e.sizeBytes,type:e.contentType,addedBy:protoUserId(e.addedBy),addedAt:clock(e.addedUtc)}));
  const reopened:CMPReopened[]=st.reopened.map(r=>({fy:r.fyStart,itemId:r.itemId,part:r.part==='na'?'na':'done',
    record:cmpRecordFromServer({fyStart:r.fyStart,itemId:r.itemId,done:r.wasDone,na:r.wasNa,filedOn:r.filedOn,notes:r.notes,evidenceRef:r.evidenceRef,reviewerNote:r.reviewerNote,dueDate:r.dueDate,
      doneBy:r.wasDone?r.recordedBy:null,doneUtc:r.recordedUtc,naBy:r.wasNa&&!r.wasDone?r.recordedBy:null,naUtc:r.recordedUtc}),
    reopenedBy:protoUserId(r.reopenedBy),reopenedAt:clock(r.reopenedUtc),reopenReason:r.reason}));
  const p=st.profile;
  const profile:CMPProfile|undefined=p?{name:p.name,pan:p.pan,gstin:p.gstin,entity:p.entity as CMPProfile['entity'],gstType:p.gstType as CMPProfile['gstType'],hasEmployees:p.hasEmployees,
    qrmp:p.qrmp,notes:p.notes,...(p.updatedBy!=null?{updatedBy:protoUserId(p.updatedBy),updatedAt:clock(p.updatedUtc)}:{})}:undefined;
  return {...(profile?{profile}:{}),fyStart:st.fyStart,records,custom,archived:[...st.archived],archivedCats:st.archivedCats as CMPCat[],files,packs,
    settings:{caName:st.settings.caName,caEmail:st.settings.caEmail,dueSoonDays:st.settings.dueSoonDays},...(reopened.length?{reopened}:{})};
}
async function loadCompliance(c:LoaderCtx):Promise<CMPState>{
  const cur=c.current('complianceX') as CMPState|undefined;
  if(!holds(c,'PERM-CMP-001'))return {...cmpEmpty(),settings:{...CMP_DEFAULT_SETTINGS,...(cur?.settings??{})}};
  const [st,packs]=await Promise.all([c.api.get<SrvCmpState>('/compliance/state'),c.api.get<SrvPack[]>('/compliance/ca-packs')]);
  c.ids.setVersion('cmpSettings','1',st.settings.version);
  if(st.profile)c.ids.setVersion('cmpProfile','1',st.profile.version);
  for(const x of st.custom)c.ids.setVersion('cmpCustom',x.id,x.version);
  const cmpPacks:CMPPack[]=packs.map(p=>({id:String(p.packId),at:clock(p.atUtc),by:protoUserId(p.by),to:p.to,toName:p.toName,
    counts:{overdue:p.overdue,due:p.due,filed:p.filed,na:p.notApplicable},lines:[...p.lines]}));
  return cmpStateFromServer(st,cmpPacks);
}

// ── Command helpers ──
/** The version loaded last for a record the server checks (body field expectedVersion). */
function loadedVersion(c:CommandCtx,kind:string,id:string,what:string):string{
  const v=c.ids.version(kind,id);if(!v)throw new HccClientError('VALIDATION',`${what} is not loaded from the server yet. Refresh and try again.`);return v;
}
/** Till with the open cash shift (DEC-052: one shared front-desk till). */
function currentTill(c:CommandCtx):number{
  const cur=c.ids.server('till','current');if(cur)return cur;
  const known=c.ids.list('till');if(known.length===1){const t=c.ids.server('till',known[0]);if(t)return t;}
  throw new HccClientError('VALIDATION','Open cash shift required: the till with the open shift is not known on this device yet. Open Cashier, refresh and try again.');
}
const expenseOf=(c:CommandCtx)=>c.state.expenses.find(e=>e.id===String(c.cmd.id));
const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const DATE=/^\d{4}-\d{2}-\d{2}$/;

function expensePrecheck(cmd:Command,s?:State):string|undefined{
  if(!Number.isSafeInteger(cmd.amount)||cmd.amount<=0)return 'Amount must be a positive whole number of paise.';
  // DEC-120 (SCC): the vendor is optional; the description is required once the expense goes for approval.
  if(!cmd.draft&&!txt(cmd.description))return 'Description is required';
  if(!txt(cmd.category))return 'Choose a category.';
  // MA-3: funding source = an active payment method usable for expenses (money masters; seed = the old four).
  if(!s||!mnyMethodFor(s,cmd.method,'expenses'))return 'Choose a funding source.';
  return undefined;
}
/** GST on the bill (rate basis points, amount paise) as sent; only the fields the command carries. */
const gstWire=(m:Command)=>({...(Number.isSafeInteger(m.gstRateBps)?{gstRateBps:m.gstRateBps}:{}),...(Number.isSafeInteger(m.gstPaise)?{gstPaise:m.gstPaise}:{})});
/** DEC-135: the ITC flag of the bill's GST, when the command carries one. */
const itcWire=(m:Command)=>m.itc==='allowed'||m.itc==='disallowed'?{itc:m.itc}:{};
/** DEC-130: why an Udhaar payment cannot be taken in this mode from this device (cash = the drawer rules of a desk receipt). */
function udPayPrecheck(cmd:Command,s?:State,me?:Parameters<typeof cashBlockedReason>[0]):string|undefined{
  if(!s)return undefined;const mode=txt(cmd.mode);
  if(mnyIsDrawer(s,mode)){const b=cashBlockedReason(me,s.shiftOpen,cxSlice(s).opener?.by);if(b)return b;
    if(!s.shiftOpen)return 'Open cash shift required: Udhaar cash goes into the open shift’s drawer. Open a cashier shift first, or take it by UPI / card / bank.';}
  else if(mnyMethod(s,mode)?.needsReference&&!txt(cmd.reference))return `Enter the ${mode} transaction reference.`;
  return undefined;
}
/** The Owner's locked-month override reason (SCC), when the command carries one. */
const override=(m:Command)=>txt(m.override)?{overrideReason:txt(m.override)}:{};
/** The card a compliance command is about: FY start (the selected FY when the command carries none) + item id. */
const cmpItem=(cmd:Command,s?:State)=>({fyStart:cmd.fy!==undefined?Number(cmd.fy):(s?.complianceX as CMPState|undefined)?.fyStart??2026,itemId:txt(cmd.itemId)});
const reason5=(cmd:Command)=>{const r=txt(cmd.reason);return r.length>=5&&r.length<=120?undefined:'Give a reason of 5–120 characters.';};
/** "cust-12" → 12 (the server's custom obligation id). */
const customId=(id:unknown)=>{const m=/^cust-(\d+)$/.exec(String(id??''));if(!m)throw new HccClientError('VALIDATION','This custom obligation is not on the server yet. Refresh and try again.');return Number(m[1]);};
const list=(v:unknown)=>(Array.isArray(v)?v.map(String):String(v??'').split('\n')).map(x=>x.trim()).filter(Boolean);
const dataUrlParts=(d:string)=>{const i=d.indexOf(','),head=i<0?'':d.slice(5,i);return {contentType:head.split(';')[0]||'application/octet-stream',base64:i<0?'':d.slice(i+1)};};

export const accounting:SyncArea={
  id:'accounting',
  loaders:[
    {slice:'accountingX',requires:'reports',reads:['GET /accounting/days','GET /accounting/trial-balance','GET /accounting/mapping','GET /accounting/mapping/history','GET /accounting/locks/history'],needs:['date'],load:loadAccounting},
    {slice:'expenses',reads:['GET /expenses'],needs:['date'],load:loadExpenses},
    {slice:'expensesX',reads:['GET /expenses/registers'],load:loadExpenseRegisters},
    {slice:'complianceX',requires:'reports',reads:['GET /compliance/state','GET /compliance/ca-packs'],load:loadCompliance},
  ],
  commands:[
    // ── Accounting (module 20) ──
    {type:'ACC_LOCK',reload:['accountingX'],precheck:cmd=>typeof cmd.date==='string'&&DATE.test(cmd.date)?undefined:'Choose a business date to lock.',
      async send(c){await c.api.command('LockAccountingPeriod',{businessDate:c.cmd.date});}},
    {type:'ACC_UNLOCK',reload:['accountingX'],precheck:cmd=>txt(cmd.reason).length>=3?undefined:'A reason is required to unlock a period.',
      async send(c){await c.api.command('UnlockAccountingPeriod',{businessDate:c.cmd.date,reason:txt(c.cmd.reason),expectedVersion:loadedVersion(c,'accLock',String(c.cmd.date),'This lock')});}},
    {type:'ACC_MAP_SET',reload:['accountingX'],precheck:cmd=>txt(cmd.account)?undefined:'Choose a ledger from the chart of accounts (not Suspense).',
      async send(c){await c.api.command('SetLedgerMapping',{mapKey:String(c.cmd.source),accountCode:String(c.cmd.account)});}},
    {type:'ACC_MAP_RESET',reload:['accountingX'],
      async send(c){await c.api.command('ResetLedgerMapping',{mapKey:String(c.cmd.source)});}},
    // DEC-077: the Tally download comes from the server, which records the export (so a later reopen of an exported day flags it for
    // re-export), carries flagged days again with a RE-EXPORT note and checks PERM-ACC-006 (A03b-13). The server's XML is the file saved.
    {type:'ACC_TALLY_EXPORT',reload:[],
      precheck:(cmd,_s,me)=>!me.permIds.includes('PERM-ACC-006')?'The Tally export is for the Owner and the Accountant.'
        :!(DATE.test(String(cmd.from??''))&&DATE.test(String(cmd.to??''))&&String(cmd.from)<=String(cmd.to))?'Choose a valid range: "from" on or before "to".':undefined,
      async send(c){const r=await c.api.get<SrvTallyExport>('/accounting/export/tally',{from:String(c.cmd.from),to:String(c.cmd.to)});
        const saved=await saveTextFile(/^[\w.-]+\.xml$/.test(r.fileName)?r.fileName:`HCC-tally-SIMULATED-${c.cmd.from}_${c.cmd.to}.xml`,r.xml,'application/xml');
        const note=tallyExportWarning(r);
        return {result:{count:r.voucherCount,dates:r.dates,reexport:r.reexportDates??[],provisional:r.provisionalDates??[],pending:r.pendingEvents??0,legacy:r.legacyDates??[],saved},
          ...(saved==='failed'?{warning:'The server recorded the Tally export, but this device could not save the XML. Try the download again.'+(note?' '+note:'')}:note?{warning:note}:{})};}},
    // ── Expenses & petty cash (Petty cash is a category; limits per role are the server's PERM-EXP-002 limits) ──
    {type:'EXPENSE',reload:['expenses','expensesX'],precheck:expensePrecheck,
      async send(c){const m=c.cmd,gst=gstWire(m);
        const r=await c.api.command<{expense:SrvExpense;duplicateOf:number[];budgetAlert?:string|null}>('SubmitExpense',{vendor:txt(m.vendor)||null,category:txt(m.category),amountPaise:m.amount,
          method:m.method,draft:!!m.draft,...(txt(m.notes)?{notes:txt(m.notes).slice(0,300)}:{}),...(txt(m.receipt)?{receipt:txt(m.receipt).slice(0,120)}:{}),
          ...(txt(m.description)?{description:txt(m.description)}:{}),...(DATE.test(String(m.date??''))?{expenseDate:m.date}:{}),...gst,...itcWire(m),...billPhotoWire(m.billPhoto),
          ...(m.confirmDuplicate?{confirmDuplicate:true}:{}),...(m.confirmGst?{confirmGst:true}:{}),...override(m)});
        return {result:{id:String(r?.expense?.expenseId??''),duplicateOf:(r?.duplicateOf??[]).map(String)},...(r?.budgetAlert?{warning:r.budgetAlert}:{})};}},
    {type:'EXPENSE_SUBMIT',reload:['expenses'],
      async send(c){const id=String(c.cmd.id);await c.api.command('SubmitExpenseDraft',{expenseId:serverId(id,'Expense'),expectedVersion:loadedVersion(c,'expense',id,'This expense'),...override(c.cmd)});}},
    {type:'EXPENSE_APPROVE',reload:['expenses','expensesX'],
      async send(c){const id=String(c.cmd.id),approve=!!c.cmd.approve,e=expenseOf(c);
        const r=await c.api.command<{budgetAlert?:string|null}>('DecideExpense',{expenseId:serverId(id,'Expense'),expectedVersion:loadedVersion(c,'expense',id,'This expense'),approve,
          ...(!approve&&txt(c.cmd.reason)?{reason:txt(c.cmd.reason)}:{}),...(approve&&e?.method==='Cash'?{tillId:currentTill(c)}:{}),...override(c.cmd)});
        const warn=r?.budgetAlert?{warning:r.budgetAlert}:{};
        return approve&&e?.method==='Cash'?{reload:CASH_SLICES,...warn}:warn;}}, // cash left the drawer: money's cash desk + cash book
    {type:'EXPENSE_REVERSE',reload:['expenses'],precheck:cmd=>txt(cmd.reason)?undefined:'A reason is required to reverse an expense.',
      async send(c){const id=String(c.cmd.id),e=expenseOf(c);
        await c.api.command('ReverseExpense',{expenseId:serverId(id,'Expense'),expectedVersion:loadedVersion(c,'expense',id,'This expense'),reason:txt(c.cmd.reason),
          ...(e?.method==='Cash'?{tillId:currentTill(c)}:{}),...override(c.cmd)});
        return e?.method==='Cash'?{reload:CASH_SLICES}:undefined;}},
    // ── DEC-120 · SCC expense features ──
    {type:'EXP_EDIT',reload:['expenses','expensesX'],precheck:cmd=>txt(cmd.reason)?undefined:'Reason required',
      async send(c){const m=c.cmd,id=String(m.id);
        const r=await c.api.command<{budgetAlert?:string|null}>('EditExpense',{expenseId:serverId(id,'Expense'),expectedVersion:loadedVersion(c,'expense',id,'This expense'),amountPaise:m.amount,
          category:txt(m.category),method:m.method,description:txt(m.description)||null,reason:txt(m.reason),...gstWire(m),...itcWire(m),...(m.receipt!==undefined?{receipt:txt(m.receipt)||null}:{}),
          ...billPhotoWire(m.billPhoto),...(m.confirmGst?{confirmGst:true}:{})});
        return r?.budgetAlert?{warning:r.budgetAlert}:undefined;}},
    {type:'EXP_VOID',reload:['expenses','expensesX'],precheck:cmd=>txt(cmd.reason)?undefined:'Choose a reason.',
      async send(c){const id=String(c.cmd.id);
        await c.api.command('VoidExpense',{expenseId:serverId(id,'Expense'),expectedVersion:loadedVersion(c,'expense',id,'This expense'),reason:txt(c.cmd.reason),...(txt(c.cmd.note)?{note:txt(c.cmd.note)}:{})});}},
    {type:'EXP_BUDGET_SET',reload:['expensesX'],
      async send(c){await c.api.command('SetExpenseBudget',{category:txt(c.cmd.category),amountPaise:Number(c.cmd.amount)||0});}},
    {type:'EXP_BUDGET_COPY',reload:['expensesX'],
      async send(c){await c.api.command('CopyExpenseBudgets',{});}},
    {type:'EXP_REC_SAVE',reload:['expensesX'],precheck:cmd=>txt(cmd.name)?undefined:'Template name is required',
      async send(c){const m=c.cmd;const r=await c.api.command<{id?:number|null}>('SaveRecurringExpense',{templateId:m.id?serverId(m.id,'Template'):null,name:txt(m.name),amountPaise:m.amount,
        dueDay:Math.floor(Number(m.dueDay)),category:txt(m.category),method:m.method,vendor:txt(m.vendor)||null,...(m.confirmDuplicate?{confirmDuplicate:true}:{})});
        return {result:{id:r?.id!=null?String(r.id):undefined}};}},
    {type:'EXP_REC_TOGGLE',reload:['expensesX'],async send(c){await c.api.command('ToggleRecurringExpense',{templateId:serverId(c.cmd.id,'Template')});}},
    {type:'EXP_REC_DELETE',reload:['expensesX'],async send(c){await c.api.command('DeleteRecurringExpense',{templateId:serverId(c.cmd.id,'Template')});}},
    {type:'EXP_REC_POST',reload:['expenses','expensesX'],
      async send(c){const r=await c.api.command<{id?:number|null;budgetAlert?:string|null}>('PostRecurringExpense',{templateId:serverId(c.cmd.id,'Template'),amountPaise:c.cmd.amount,
        ...(c.cmd.again?{again:true}:{}),...override(c.cmd)});
        return {result:{id:r?.id!=null?String(r.id):undefined},...(r?.budgetAlert?{warning:r.budgetAlert}:{})};}},
    {type:'EXP_PETTY_FLOAT',reload:['expensesX'],
      async send(c){await c.api.command('RequestPettyFloat',{amountPaise:c.cmd.amount,...(txt(c.cmd.note)?{note:txt(c.cmd.note)}:{})});}},
    {type:'EXP_PETTY_FLOAT_DECIDE',reload:['expensesX'],
      async send(c){await c.api.command('DecidePettyFloat',{entryId:serverId(c.cmd.id,'Top-up'),approve:!!c.cmd.approve,...(txt(c.cmd.reason)?{reason:txt(c.cmd.reason)}:{})});}},
    {type:'EXP_PETTY_OUT',reload:['expenses','expensesX'],
      async send(c){await c.api.command('RecordPettyDisbursement',{amountPaise:c.cmd.amount,category:txt(c.cmd.category),...(txt(c.cmd.for)?{for:txt(c.cmd.for)}:{}),
        ...(txt(c.cmd.receipt)?{receipt:txt(c.cmd.receipt)}:{}),...override(c.cmd)});}},
    {type:'EXP_UD_ADD',reload:['expensesX'],precheck:cmd=>txt(cmd.name)?undefined:'Customer name is required',
      async send(c){const m=c.cmd;await c.api.command('AddUdhaar',{name:txt(m.name),amountPaise:m.amount,...(txt(m.mobile)?{mobile:txt(m.mobile)}:{}),
        ...(DATE.test(String(m.dueDate??''))?{dueDate:m.dueDate}:{}),...(txt(m.note)?{note:txt(m.note)}:{})});}},
    // DEC-130: a desk receipt — cash needs the till with the open shift (and reloads the cash desk + cash book), other modes their reference.
    {type:'EXP_UD_PAY',reload:['expensesX'],precheck:(cmd,s,me)=>udPayPrecheck(cmd,s,me),
      async send(c){const mode=txt(c.cmd.mode),cash=mnyIsDrawer(c.state,mode);
        await c.api.command('PayUdhaar',{receivableId:serverId(c.cmd.id,'Credit'),amountPaise:c.cmd.amount,mode,...(txt(c.cmd.reference)?{reference:txt(c.cmd.reference)}:{}),
          ...(cash?{tillId:currentTill(c)}:{})});
        return cash?{reload:CASH_SLICES}:undefined;}},
    {type:'EXP_UD_VOID',reload:['expensesX'],
      async send(c){await c.api.command('VoidUdhaar',{receivableId:serverId(c.cmd.id,'Credit'),...(txt(c.cmd.reason)?{reason:txt(c.cmd.reason)}:{})});}},
    {type:'EXP_MONTH_LOCK',reload:['expensesX'],precheck:cmd=>/^\d{4}-\d{2}$/.test(String(cmd.month??''))?undefined:'Choose a month.',
      async send(c){await c.api.command('LockExpenseMonth',{month:String(c.cmd.month)});}},
    // DEC-131: the Owner unlocks a locked month with a reason (audited on the server).
    {type:'EXP_MONTH_UNLOCK',reload:['expensesX'],
      precheck:cmd=>!/^\d{4}-\d{2}$/.test(String(cmd.month??''))?'Choose a month.':txt(cmd.reason).length<3?'A reason is required to unlock a month.':undefined,
      async send(c){await c.api.command('UnlockExpenseMonth',{month:String(c.cmd.month),reason:txt(c.cmd.reason)});}},
    // SCC ledger / month CSV: built by the server (SCC columns + HCC Ref, Status) and saved on this device.
    {type:'EXP_CSV',reload:[],precheck:cmd=>cmd.kind==='ledger'||cmd.kind==='month'&&/^\d{4}-\d{2}$/.test(String(cmd.month??''))?undefined:'Choose what to export.',
      async send(c){const r=c.cmd.kind==='ledger'?await c.api.get<SrvExport>('/expenses/export/ledger'):await c.api.get<SrvExport>('/expenses/export/month',{month:String(c.cmd.month)});
        const saved=await saveCsv(/^[\w.-]+\.csv$/.test(r.fileName)?r.fileName:'expenses.csv',r.csv);
        return {result:{rows:r.rows,saved},...(saved==='failed'?{warning:'This device could not save the CSV. Try again.'}:{})};}},
    // ── Compliance calendar = SCC tax module in full (Owner + Accountant, DEC-055; Owner manages, DEC-058) ──
    {type:'CMP_DONE',reload:['complianceX'],precheck:cmd=>txt(cmd.itemId)?undefined:'Choose a compliance item.',
      async send(c){const m=c.cmd;await c.api.command('MarkComplianceDone',{...cmpItem(m,c.state),...(txt(m.filedOn)?{filedOn:txt(m.filedOn)}:{}),
        notes:txt(m.notes),evidenceRef:txt(m.evidenceRef),reviewerNote:txt(m.reviewerNote)});}},
    {type:'CMP_NA',reload:['complianceX'],
      async send(c){await c.api.command('MarkComplianceNotApplicable',cmpItem(c.cmd,c.state));}},
    {type:'CMP_REOPEN',reload:['complianceX'],precheck:reason5,
      async send(c){await c.api.command('ReopenComplianceItem',{...cmpItem(c.cmd,c.state),reason:txt(c.cmd.reason)});}},
    {type:'CMP_FY_SET',reload:['complianceX'],
      async send(c){await c.api.command('SetComplianceFy',{fyStart:Number(c.cmd.fy)});}},
    {type:'CMP_PROFILE_SET',reload:['complianceX'],precheck:cmd=>txt(cmd.name)?undefined:'Please enter a firm name.',
      async send(c){const m=c.cmd;await c.api.command('SaveComplianceProfile',{name:txt(m.name),pan:txt(m.pan),gstin:txt(m.gstin),entity:txt(m.entity)||'proprietorship',
        gstType:txt(m.gstType)||'regular',hasEmployees:m.hasEmployees!==false,qrmp:m.qrmp===true,notes:txt(m.notes),expectedVersion:c.ids.version('cmpProfile','1')??null});}},
    {type:'CMP_ARCHIVE_KEY',reload:['complianceX'],
      async send(c){await c.api.command('ToggleComplianceArchive',{key:txt(c.cmd.obligationKey)});}},
    {type:'CMP_ARCHIVE_CAT',reload:['complianceX'],
      async send(c){await c.api.command('ToggleComplianceCategoryArchive',{cat:txt(c.cmd.cat)});}},
    {type:'CMP_CUSTOM_SAVE',reload:['complianceX'],
      precheck:cmd=>!txt(cmd.name)?'Please enter an obligation name.':!(Array.isArray(cmd.months)&&cmd.months.length)?'Please select at least one applicable month.':undefined,
      async send(c){const m=c.cmd,id=txt(m.id);
        const r=await c.api.command<{id:string}>('SaveCustomObligation',{...(id?{customId:customId(id),expectedVersion:loadedVersion(c,'cmpCustom',id,'This custom obligation')}:{}),
          cat:txt(m.cat)||'gst',name:txt(m.name),freq:txt(m.freq)||'Monthly',dueDay:Number.isInteger(Number(m.dueDay))?Number(m.dueDay):0,months:(m.months as unknown[]).map(String),
          form:txt(m.form),who:txt(m.who),what:txt(m.what),why:txt(m.why),penalty:txt(m.penalty),responsible:txt(m.responsible),reviewer:txt(m.reviewer),
          risk:txt(m.risk)||null,buffer:m.buffer===null||m.buffer===undefined||m.buffer===''?null:Number(m.buffer),checklist:list(m.checklist),workflow:list(m.workflow),evidence:list(m.evidence)});
        return {result:{id:r?.id??id}};}},
    {type:'CMP_CUSTOM_DELETE',reload:['complianceX'],
      async send(c){await c.api.command('DeleteCustomObligation',{customId:customId(c.cmd.id)});}},
    {type:'CMP_EVIDENCE_ADD',reload:['complianceX'],precheck:cmd=>String(cmd.dataUrl??'').startsWith('data:')?undefined:'Choose a file to attach.',
      async send(c){const d=dataUrlParts(String(c.cmd.dataUrl));
        await c.api.command('AddComplianceEvidence',{...cmpItem(c.cmd,c.state),fileName:txt(c.cmd.name)||'evidence',contentType:d.contentType,base64:d.base64});}},
    {type:'CMP_EVIDENCE_DELETE',reload:['complianceX'],
      async send(c){await c.api.command('DeleteComplianceEvidence',{evidenceId:serverId(c.cmd.id,'Evidence file')});}},
    {type:'CMP_SETTINGS_SET',reload:['complianceX'],
      async send(c){const m=c.cmd;
        await c.api.command('SaveComplianceSettings',{...(m.caName!==undefined?{caName:txt(m.caName)}:{}),...(m.caEmail!==undefined?{caEmail:txt(m.caEmail)}:{}),
          ...(m.dueSoonDays!==undefined?{dueSoonDays:Number(m.dueSoonDays)}:{}),expectedVersion:loadedVersion(c,'cmpSettings','1','The compliance settings')});}},
    {type:'CMP_CA_PACK',reload:['complianceX'],
      async send(c){const r=await c.api.command<{packId:number}>('GenerateCaPack',{});return {result:{id:r?.packId!=null?String(r.packId):undefined}};}},
  ],
  unmapped:{},
};

/** Exported for tests: the state type the loaders fill. */
export type AccountingSlices=Pick<State,'accountingX'|'expenses'|'complianceX'>;
