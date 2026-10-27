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
//             complianceX ← GET /compliance/obligations, /compliance/calendar?includeInactive=true (filings + settings),
//                           /compliance/ca-packs. Periods and due dates are computed by the same rules on both sides.
//   commands  ACC_LOCK/ACC_UNLOCK/ACC_MAP_SET/ACC_MAP_RESET → Lock/UnlockAccountingPeriod, Set/ResetLedgerMapping;
//             ACC_TALLY_EXPORT → GET /accounting/export/tally (recorded on the server, DEC-077) and the server's XML is saved;
//             EXPENSE/EXPENSE_SUBMIT/EXPENSE_APPROVE/EXPENSE_REVERSE → SubmitExpense, SubmitExpenseDraft, DecideExpense, ReverseExpense;
//             DEC-120: EXP_EDIT/EXP_VOID → EditExpense, VoidExpense; EXP_BUDGET_SET/EXP_BUDGET_COPY → SetExpenseBudget, CopyExpenseBudgets;
//             EXP_REC_SAVE/TOGGLE/DELETE/POST → Save/Toggle/Delete/PostRecurringExpense; EXP_PETTY_FLOAT/_DECIDE/EXP_PETTY_OUT → RequestPettyFloat,
//             DecidePettyFloat, RecordPettyDisbursement; EXP_UD_ADD/PAY/VOID → AddUdhaar, PayUdhaar, VoidUdhaar; EXP_MONTH_LOCK → LockExpenseMonth;
//             EXP_CSV → GET /expenses/export/ledger | /expenses/export/month (the server's SCC CSV is saved);
//             CMP_* → MarkComplianceFiled, MarkComplianceNotApplicable, ReopenComplianceFiling, SaveComplianceObligation,
//             ToggleComplianceObligation, SaveComplianceSettings, GenerateCaPack.
//   versions  the server needs the record version on most commands while the prototype commands carry none: the version
//             loaded last (ids.version) is sent, so a record changed elsewhere since the last load answers STALE_VERSION.
//   till      a Cash expense decision / reversal needs the till with the open shift (DEC-052): ids 'till' → 'current' (learned by the
//             money area when it loads the shift) or the only till known on this device.
import type {Command,Expense,State} from '../../services/mock';
import type {EXPState,ExpEditLog} from '../../domain/streams/expenses-scc';
import type {ACCState,ACCVoucher,ACCServerDay,ACCLock,ACCMapRow,ACCException,ACCCheck,ACCDayStatus,ACCVoucherType,ACCSourceKind,ACCServer,ACCTBGroup} from '../../domain/streams/accounting';
import {ACC_DEFAULT_MAP,accAccount} from '../../domain/streams/accounting';
import type {CMPState,CMPObligation,CMPFiling,CMPPack,CMPRule,CMPKind,CMPFreq} from '../../domain/streams/compliance';
import {mnyMethodFor} from '../../domain/streams/money-master';
import {addDays} from '../../domain/money';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx} from './types';
import {serverId,protoUserId,utcToClock,paise,isoDate} from './map';
import {CASH_SLICES} from './money';
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
  voidedUtc?:string|null;hasBillPhoto?:boolean;createdUtc?:string|null;edits?:SrvExpenseEdit[]|null};
type SrvExpenseEdit={editId:number;by:number;atUtc:string;reason:string;old:Record<string,string|number>;new:Record<string,string|number>};
/** GET /expenses/registers (ExpenseRegistersView). */
type SrvRegisters={businessDate:string;budgets:{month:string;category:string;amountPaise:number}[];
  recurring:{templateId:number;name:string;amountPaise:number;dueDay:number;category:string;method:string;vendor:string;active:boolean;createdBy:number;createdUtc:string}[];
  petty:{entryId:number;amountPaise:number;businessDate:string;status:string;note?:string|null;recordedBy:number;recordedUtc:string;decidedBy?:number|null;decidedUtc?:string|null;reason?:string|null}[];
  udhaar:{receivableId:number;name:string;mobile?:string|null;amountPaise:number;creditDate:string;dueDate?:string|null;note?:string|null;createdBy:number;createdUtc:string;void:boolean;
    voidReason?:string|null;voidedBy?:number|null;voidedDate?:string|null;settlements:{settlementId:number;amountPaise:number;date:string;mode:string;recordedBy:number;recordedUtc:string}[]}[];
  months:{month:string;expensePaise:number;count:number;gstPaidPaise:number;byCategory:Record<string,number>;generatedBy:number;generatedUtc:string;stale:boolean;staleUtc?:string|null}[]};
type SrvExport={fileName:string;csv:string;rows:number};
type SrvExpensePage={items:SrvExpense[];page:number;pageSize:number;total:number;ownOnly:boolean};
type SrvRuleView={effectiveFrom:string;type:string;text:string;day?:number|null;month?:number|null;specialMonth?:number|null;specialDay?:number|null};
export type SrvObligation={obligationId:number;kind:string;name:string;form:string;frequency:string;owner:string;trackedFrom:string;active:boolean;note?:string|null;version:string;rules:SrvRuleView[]};
type SrvFiling={status:string;by:number;atUtc:string;reference?:string|null;reason?:string|null};
type SrvCalendarItem={obligationId:number;kind:string;name:string;period:string;dueDate:string;days:number;status:string;filing?:SrvFiling|null};
type SrvSettings={caName:string;caEmail:string;dueSoonDays:number;version:string};
export type SrvCalendar={asOf:string;items:SrvCalendarItem[];settings:SrvSettings};
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
        by:protoUserId(x.recordedBy),at:clock(x.recordedUtc)})),...(u.void?{void:true,voidReason:u.voidReason??'',voidAt:u.voidedDate??'',...(u.voidedBy!=null?{voidBy:protoUserId(u.voidedBy)}:{})}:{})})),
    months:Object.fromEntries(r.months.map(m=>[m.month,{month:m.month,expense:m.expensePaise,count:m.count,byCategory:{...m.byCategory},gstPaid:m.gstPaidPaise,generatedAt:clock(m.generatedUtc),
      by:protoUserId(m.generatedBy),...(m.stale?{stale:true,staleAt:m.staleUtc?clock(m.staleUtc):clock(m.generatedUtc)}:{})}]))};
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

// ── Compliance ──
const ruleFromServer=(r:SrvRuleView):CMPRule=>r.type==='month-end'?{type:'month-end'}:r.type==='fixed'?{type:'fixed',month:Number(r.month),day:Number(r.day)}
  :{type:'day',day:Number(r.day),...(r.specialMonth&&r.specialDay?{special:{month:r.specialMonth,day:r.specialDay}}:{})};
export function obligationFromServer(o:SrvObligation):CMPObligation{
  return {id:String(o.obligationId),kind:o.kind as CMPKind,name:o.name,form:o.form,freq:(o.frequency==='Annual'?'Annual':'Monthly') as CMPFreq,
    rules:o.rules.map(r=>({from:r.effectiveFrom,rule:ruleFromServer(r)})),owner:o.owner,from:o.trackedFrom,active:o.active,...(o.note?{note:o.note}:{})};
}
/** Prototype rule (dialog) → DueRuleDto. */
export function ruleToServer(r:unknown):Record<string,unknown>{
  const x=(r??{}) as {type?:string;day?:number;month?:number;special?:{month:number;day:number}};
  if(x.type==='month-end')return {type:'month-end'};
  if(x.type==='fixed')return {type:'fixed',month:x.month,day:x.day};
  return {type:'day',day:x.day,...(x.special?{specialMonth:x.special.month,specialDay:x.special.day}:{})};
}
async function loadCompliance(c:LoaderCtx):Promise<CMPState>{
  const empty=c.current('complianceX') as CMPState|undefined;
  if(!holds(c,'PERM-CMP-001'))return {obligations:[],filings:[],packs:[],settings:{...(empty?.settings??{caName:'',caEmail:'',dueSoonDays:7})}};
  const [obs,cal,packs]=await Promise.all([
    c.api.get<SrvObligation[]>('/compliance/obligations'),
    c.api.get<SrvCalendar>('/compliance/calendar',{includeInactive:true}),
    c.api.get<SrvPack[]>('/compliance/ca-packs'),
  ]);
  for(const o of obs)c.ids.setVersion('obligation',String(o.obligationId),o.version);
  c.ids.setVersion('cmpSettings','1',cal.settings.version);
  const filings:CMPFiling[]=cal.items.filter(i=>i.filing).map(i=>({obligation:String(i.obligationId),period:i.period,status:i.filing!.status==='Filed'?'Filed':'Not applicable',
    by:protoUserId(i.filing!.by),at:clock(i.filing!.atUtc),...(i.filing!.reference?{reference:i.filing!.reference}:{}),...(i.filing!.reason?{reason:i.filing!.reason}:{})}));
  const cmpPacks:CMPPack[]=packs.map(p=>({id:String(p.packId),at:clock(p.atUtc),by:protoUserId(p.by),to:p.to,toName:p.toName,
    counts:{overdue:p.overdue,due:p.due,filed:p.filed,na:p.notApplicable},lines:[...p.lines]}));
  return {obligations:obs.map(obligationFromServer),filings,packs:cmpPacks,settings:{caName:cal.settings.caName,caEmail:cal.settings.caEmail,dueSoonDays:cal.settings.dueSoonDays}};
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
/** The Owner's locked-month override reason (SCC), when the command carries one. */
const override=(m:Command)=>txt(m.override)?{overrideReason:txt(m.override)}:{};
const cmpItem=(cmd:Command)=>({obligationId:serverId(cmd.obligation,'Obligation'),period:txt(cmd.period)});
const reason5=(cmd:Command)=>{const r=txt(cmd.reason);return r.length>=5&&r.length<=120?undefined:'Give a reason of 5–120 characters.';};

export const accounting:SyncArea={
  id:'accounting',
  loaders:[
    {slice:'accountingX',requires:'reports',reads:['GET /accounting/days','GET /accounting/trial-balance','GET /accounting/mapping','GET /accounting/mapping/history','GET /accounting/locks/history'],needs:['date'],load:loadAccounting},
    {slice:'expenses',reads:['GET /expenses'],needs:['date'],load:loadExpenses},
    {slice:'expensesX',reads:['GET /expenses/registers'],load:loadExpenseRegisters},
    {slice:'complianceX',requires:'reports',reads:['GET /compliance/obligations','GET /compliance/calendar','GET /compliance/ca-packs'],load:loadCompliance},
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
          ...(txt(m.description)?{description:txt(m.description)}:{}),...(DATE.test(String(m.date??''))?{expenseDate:m.date}:{}),...gst,...billPhotoWire(m.billPhoto),
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
          category:txt(m.category),method:m.method,description:txt(m.description)||null,reason:txt(m.reason),...gstWire(m),...(m.receipt!==undefined?{receipt:txt(m.receipt)||null}:{}),
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
    {type:'EXP_UD_PAY',reload:['expensesX'],
      async send(c){await c.api.command('PayUdhaar',{receivableId:serverId(c.cmd.id,'Credit'),amountPaise:c.cmd.amount,mode:txt(c.cmd.mode)});}},
    {type:'EXP_UD_VOID',reload:['expensesX'],
      async send(c){await c.api.command('VoidUdhaar',{receivableId:serverId(c.cmd.id,'Credit'),...(txt(c.cmd.reason)?{reason:txt(c.cmd.reason)}:{})});}},
    {type:'EXP_MONTH_LOCK',reload:['expensesX'],precheck:cmd=>/^\d{4}-\d{2}$/.test(String(cmd.month??''))?undefined:'Choose a month.',
      async send(c){await c.api.command('LockExpenseMonth',{month:String(c.cmd.month)});}},
    // SCC ledger / month CSV: built by the server (SCC columns + HCC Ref, Status) and saved on this device.
    {type:'EXP_CSV',reload:[],precheck:cmd=>cmd.kind==='ledger'||cmd.kind==='month'&&/^\d{4}-\d{2}$/.test(String(cmd.month??''))?undefined:'Choose what to export.',
      async send(c){const r=c.cmd.kind==='ledger'?await c.api.get<SrvExport>('/expenses/export/ledger'):await c.api.get<SrvExport>('/expenses/export/month',{month:String(c.cmd.month)});
        const saved=await saveCsv(/^[\w.-]+\.csv$/.test(r.fileName)?r.fileName:'expenses.csv',r.csv);
        return {result:{rows:r.rows,saved},...(saved==='failed'?{warning:'This device could not save the CSV. Try again.'}:{})};}},
    // ── Compliance calendar (Owner + Accountant, DEC-055) ──
    {type:'CMP_FILE',reload:['complianceX'],
      precheck:cmd=>{const r=txt(cmd.reference);return r.length>=4&&r.length<=40&&/^[\w\-/. ]+$/.test(r)?undefined:'Enter the acknowledgement / challan reference (4–40 characters: letters, digits and - / .).';},
      async send(c){await c.api.command('MarkComplianceFiled',{...cmpItem(c.cmd),reference:txt(c.cmd.reference)});}},
    {type:'CMP_NA',reload:['complianceX'],precheck:reason5,
      async send(c){await c.api.command('MarkComplianceNotApplicable',{...cmpItem(c.cmd),reason:txt(c.cmd.reason)});}},
    {type:'CMP_REOPEN',reload:['complianceX'],precheck:reason5,
      async send(c){await c.api.command('ReopenComplianceFiling',{...cmpItem(c.cmd),reason:txt(c.cmd.reason)});}},
    {type:'CMP_OBLIGATION_SAVE',reload:['complianceX'],
      precheck:cmd=>{const n=txt(cmd.name);return n.length>=3&&n.length<=60?undefined:'Obligation name must be 3–60 characters.';},
      async send(c){const m=c.cmd,id=m.id?String(m.id):undefined;
        const body:Record<string,unknown>={id:id?serverId(id,'Obligation'):null,name:txt(m.name),form:txt(m.form),owner:txt(m.owner)||'Accountant',rule:ruleToServer(m.rule),
          ...(txt(m.note)?{note:txt(m.note)}:{})};
        if(id){body.effectiveFrom=txt(m.effectiveFrom)||undefined;body.expectedVersion=loadedVersion(c,'obligation',id,'This obligation');}else body.kind=txt(m.kind);
        const r=await c.api.command<{obligationId:number}>('SaveComplianceObligation',body);
        return {result:{id:r?.obligationId!=null?String(r.obligationId):id}};}},
    {type:'CMP_OBLIGATION_TOGGLE',reload:['complianceX'],
      async send(c){const id=String(c.cmd.id);await c.api.command('ToggleComplianceObligation',{obligationId:serverId(id,'Obligation'),expectedVersion:loadedVersion(c,'obligation',id,'This obligation')});}},
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
