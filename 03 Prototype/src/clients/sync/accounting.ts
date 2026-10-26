// Accounting, expenses & compliance area (W4-7). Server modules: Accounting & Tally (W2-3), Expenses (W2-4), Compliance (W2-5).
//
//   loaders   accountingX ← GET /accounting/days?from&to (the server's evaluated days for the last 62 business dates: vouchers,
//                           exceptions, reconciliation, status, lock), /accounting/trial-balance?cumulative (opening balances
//                           before the window), /accounting/mapping (+/history), /accounting/locks/history. The prototype
//                           selectors read `accountingX.server` (domain/streams/accounting.ts) instead of deriving vouchers on the
//                           device; locked days also fill `locks` (who/when, snapshot) for the lock / unlock dialogs.
//             expenses    ← GET /expenses (window of 62 days + every Draft / Pending, pages of 100). Version kept per expense.
//             complianceX ← GET /compliance/obligations, /compliance/calendar?includeInactive=true (filings + settings),
//                           /compliance/ca-packs. Periods and due dates are computed by the same rules on both sides.
//   commands  ACC_LOCK/ACC_UNLOCK/ACC_MAP_SET/ACC_MAP_RESET → Lock/UnlockAccountingPeriod, Set/ResetLedgerMapping;
//             EXPENSE/EXPENSE_SUBMIT/EXPENSE_APPROVE/EXPENSE_REVERSE → SubmitExpense, SubmitExpenseDraft, DecideExpense, ReverseExpense;
//             CMP_* → MarkComplianceFiled, MarkComplianceNotApplicable, ReopenComplianceFiling, SaveComplianceObligation,
//             ToggleComplianceObligation, SaveComplianceSettings, GenerateCaPack.
//   versions  the server needs the record version on most commands while the prototype commands carry none: the version
//             loaded last (ids.version) is sent, so a record changed elsewhere since the last load answers STALE_VERSION.
//   till      a Cash expense decision / reversal needs the till with the open shift (DEC-052): ids 'till' → 'current' (learned by the
//             money area when it loads the shift) or the only till known on this device.
import type {Command,Expense,State} from '../../services/mock';
import type {ACCState,ACCVoucher,ACCServerDay,ACCLock,ACCMapRow,ACCException,ACCCheck,ACCDayStatus,ACCVoucherType,ACCSourceKind} from '../../domain/streams/accounting';
import {ACC_DEFAULT_MAP} from '../../domain/streams/accounting';
import type {CMPState,CMPObligation,CMPFiling,CMPPack,CMPRule,CMPKind,CMPFreq} from '../../domain/streams/compliance';
import {EXPENSE_METHODS} from '../../domain/streams/closing';
import {addDays} from '../../domain/money';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx} from './types';
import {serverId,protoUserId,utcToClock,paise,isoDate} from './map';

// ── Server DTOs (Hcc.Contracts.Accounting / Expenses / Compliance, camelCase) ──
type SrvEntry={account:string;ledger:string;debitPaise:number;creditPaise:number};
type SrvVoucher={number:string;date:string;type:string;narration:string;sourceKey:string;sourceKind:string;sub:string;debitPaise:number;creditPaise:number;
  balanced:boolean;entries:SrvEntry[];unmapped:string[]};
type SrvIssue={id:string;severity:string;kind:string;title:string;detail:string;voucher?:string|null;source?:string|null;key?:string|null};
type SrvRecon={id:string;label:string;reportPaise:number;ledgerPaise:number;ok:boolean;note:string};
type SrvLockInfo={lockId:number;lockedUtc:string;lockedBy:number;version:string};
export type SrvAccountingDay={date:string;status:string;provisional:boolean;voucherCount:number;debitPaise:number;creditPaise:number;balanced:boolean;errors:number;warnings:number;
  lock?:SrvLockInfo|null;vouchers:SrvVoucher[];exceptions:SrvIssue[];reconciliation:SrvRecon[]};
type SrvTrialBalance={date:string;cumulative:boolean;debitPaise:number;creditPaise:number;balanced:boolean;
  rows:{account:string;name:string;group:string;debitPaise:number;creditPaise:number;balancePaise:number}[]};
type SrvMappingRow={mapKey:string;group:string;value:string;account?:string|null;defaultAccount?:string|null;changed:boolean;uses:number;mapped:boolean};
type SrvMappingChange={changeId:number;atUtc:string;actor:number;mapKey:string;before?:string|null;after?:string|null};
type SrvLockEvent={lockId:number;businessDate:string;action:string;atUtc:string;actor:number;reason?:string|null};
export type SrvExpense={expenseId:number;ref:string;vendor:string;category:string;amountPaise:number;method:string;businessDate:string;status:string;submittedBy:number;
  notes?:string|null;receipt?:string|null;reason?:string|null;decidedBy?:number|null;decidedUtc?:string|null;reversalOf?:number|null;reversedBy?:number|null;version:string};
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
  const opening:ACCVoucher[]=tb.rows.length?[{no:'OPENING',date:addDays(from,-1),type:'Journal',narration:`Balances before ${from}`,
    source:{kind:'closing',id:'OPENING/'+from,sub:'opening'},entries:tb.rows.map(r=>({account:r.account,debit:paise(r.debitPaise),credit:paise(r.creditPaise)})),unmapped:[]}]:[];
  const mapRows=rows.map(mapRowFromServer);
  // Overrides only (the selectors overlay them on ACC_DEFAULT_MAP); '' = no ledger (Suspense).
  const mapping:Record<string,string>={};
  for(const r of mapRows)if(r.account!==(ACC_DEFAULT_MAP[r.key]??''))mapping[r.key]=r.account;
  return {mapping,locks,
    mapHistory:[...mapHist].sort((a,b)=>a.changeId-b.changeId).map(h=>({id:String(h.changeId),at:clock(h.atUtc),actor:protoUserId(h.actor),key:h.mapKey,before:h.before??'',after:h.after??''})),
    lockHistory:[...lockHist].sort((a,b)=>utc(a.atUtc).localeCompare(utc(b.atUtc))).map(h=>({id:`${h.lockId}:${h.action}`,at:clock(h.atUtc),actor:protoUserId(h.actor),date:h.businessDate,
      action:h.action==='unlock'?'unlock':'lock',...(h.reason?{reason:h.reason}:{})})),
    server:{from,to,days:out,opening,mapRows}};
}

// ── Expenses ──
export function expenseFromServer(e:SrvExpense):Expense{
  const x:Expense={id:String(e.expenseId),vendor:e.vendor,category:e.category,amount:paise(e.amountPaise,'amountPaise'),method:e.method,date:isoDate(e.businessDate,'businessDate'),
    status:e.status as Expense['status'],actor:protoUserId(e.submittedBy)};
  if(e.notes)x.notes=e.notes;if(e.receipt)x.receipt=e.receipt;if(e.reason)x.reason=e.reason;
  if(e.decidedBy!=null)x.approver=protoUserId(e.decidedBy);if(e.decidedUtc)x.decidedAt=clock(e.decidedUtc);
  if(e.reversalOf!=null)x.reversalOf=String(e.reversalOf);if(e.reversedBy!=null)x.reversedBy=String(e.reversedBy);
  return x;
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

function expensePrecheck(cmd:Command):string|undefined{
  if(!Number.isSafeInteger(cmd.amount)||cmd.amount<=0)return 'Amount must be a positive whole number of paise.';
  if(!txt(cmd.vendor))return 'Enter a vendor.';
  if(!txt(cmd.category))return 'Choose a category.';
  if(!EXPENSE_METHODS.includes(cmd.method))return 'Choose a funding source.';
  return undefined;
}
const cmpItem=(cmd:Command)=>({obligationId:serverId(cmd.obligation,'Obligation'),period:txt(cmd.period)});
const reason5=(cmd:Command)=>{const r=txt(cmd.reason);return r.length>=5&&r.length<=120?undefined:'Give a reason of 5–120 characters.';};

export const accounting:SyncArea={
  id:'accounting',
  loaders:[
    {slice:'accountingX',requires:'reports',needs:['date'],load:loadAccounting},
    {slice:'expenses',needs:['date'],load:loadExpenses},
    {slice:'complianceX',requires:'reports',load:loadCompliance},
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
    // ── Expenses & petty cash (Petty cash is a category; limits per role are the server's PERM-EXP-002 limits) ──
    {type:'EXPENSE',reload:['expenses'],precheck:expensePrecheck,
      async send(c){const m=c.cmd;
        const r=await c.api.command<{expense:SrvExpense;duplicateOf:number[]}>('SubmitExpense',{vendor:txt(m.vendor),category:txt(m.category),amountPaise:m.amount,method:m.method,draft:!!m.draft,
          ...(txt(m.notes)?{notes:txt(m.notes).slice(0,300)}:{}),...(txt(m.receipt)?{receipt:txt(m.receipt).slice(0,120)}:{})});
        return {result:{id:String(r?.expense?.expenseId??''),duplicateOf:(r?.duplicateOf??[]).map(String)}};}},
    {type:'EXPENSE_SUBMIT',reload:['expenses'],
      async send(c){const id=String(c.cmd.id);await c.api.command('SubmitExpenseDraft',{expenseId:serverId(id,'Expense'),expectedVersion:loadedVersion(c,'expense',id,'This expense')});}},
    {type:'EXPENSE_APPROVE',reload:['expenses'],
      async send(c){const id=String(c.cmd.id),approve=!!c.cmd.approve,e=expenseOf(c);
        await c.api.command('DecideExpense',{expenseId:serverId(id,'Expense'),expectedVersion:loadedVersion(c,'expense',id,'This expense'),approve,
          ...(!approve&&txt(c.cmd.reason)?{reason:txt(c.cmd.reason)}:{}),...(approve&&e?.method==='Cash'?{tillId:currentTill(c)}:{})});}},
    {type:'EXPENSE_REVERSE',reload:['expenses'],precheck:cmd=>txt(cmd.reason)?undefined:'A reason is required to reverse an expense.',
      async send(c){const id=String(c.cmd.id),e=expenseOf(c);
        await c.api.command('ReverseExpense',{expenseId:serverId(id,'Expense'),expectedVersion:loadedVersion(c,'expense',id,'This expense'),reason:txt(c.cmd.reason),
          ...(e?.method==='Cash'?{tillId:currentTill(c)}:{})});}},
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
