// Module 20 · Accounting automation — pure helpers, selectors and the state slice type. Exported names MUST start with 'acc' / 'ACC' (shared mock namespace).
// Vouchers are DERIVED from source records (folio lines, receipts, expenses, closing records); nothing is keyed by hand.
// Spec: 02 Planning/modules/HCC_Module_20_Accounting_Spec.md. Money = integer paise.
import {csvMinimal} from '../csv';
import type {State,Booking,Line,Receipt,Expense} from '../types';
import {drawer} from '../selectors';
import {reportPack,expenseCounts,closingOutlet,closingReopened} from './closing';
import {posSlice,type POSReceipt} from './pos';
import {lineTax,gstSplit} from '../gst';
import {mnyLedgerDefaults,mnyIsCredit} from './money-master';
import {cxSlice} from './cashout';
import {sysParkedAccounting,SYS_STUCK_TITLE,sysStuckDetail} from './syshealth';

// ── Chart of accounts (synthetic) ──
export type ACCGroup='Asset'|'Liability'|'Income'|'Expense'|'Suspense';
export type ACCAccount={code:string;name:string;group:ACCGroup;note?:string};
export const ACC_SUSPENSE='9999';
export const ACC_CHART:ACCAccount[]=[
  {code:'1000',name:'Cash in hand',group:'Asset',note:'Front-desk drawer'},
  {code:'1010',name:'UPI clearing',group:'Asset',note:'Settles to bank (simulated)'},
  {code:'1020',name:'Card clearing',group:'Asset',note:'Settles to bank (simulated)'},
  {code:'1030',name:'Bank account',group:'Asset',note:'Bank transfers'},
  {code:'1100',name:'Guest ledger',group:'Asset',note:'Open folio balances'},
  {code:'1110',name:'Company receivables (city ledger)',group:'Asset',note:'Authorised company credit'},
  {code:'1200',name:'Staff advances',group:'Asset',note:'Salary advances recovered through payroll'},
  {code:'2000',name:'Advance deposits',group:'Liability',note:'Refundable deposits held'},
  {code:'2100',name:'Synthetic tax payable (legacy)',group:'Liability',note:'Before DEC-069 only — kept for locked snapshots'},
  {code:'2110',name:'Output CGST',group:'Liability',note:'GST master, intra-state half (illustrative — CA to confirm)'},
  {code:'2120',name:'Output SGST',group:'Liability',note:'GST master, intra-state half (illustrative — CA to confirm)'},
  {code:'2200',name:'Vendor payables',group:'Liability',note:'Vendor-credit expenses'},
  {code:'2300',name:'Salaries payable',group:'Liability',note:'Net pay due to staff'},
  {code:'2310',name:'PF payable',group:'Liability',note:'Employee + employer PF (illustrative — CA to confirm)'},
  {code:'2320',name:'ESIC payable',group:'Liability',note:'Employee + employer ESIC (illustrative — CA to confirm)'},
  {code:'2330',name:'Professional tax payable',group:'Liability'},
  {code:'2340',name:'Other payroll deductions',group:'Liability'},
  {code:'3000',name:"Owner's drawings",group:'Liability',note:'Capital account (contra): cash the Owner takes from the drawer (DEC-073) — CA to confirm'},
  {code:'4000',name:'Room revenue',group:'Income'},
  {code:'4100',name:'Other revenue',group:'Income',note:'Services and extras'},
  {code:'4900',name:'Discounts allowed',group:'Income',note:'Contra revenue'},
  {code:'4910',name:'Refunds, reversals & credit notes',group:'Income',note:'Contra revenue (folio adjustments)'},
  {code:'5000',name:'Supplies',group:'Expense'},
  {code:'5010',name:'Laundry',group:'Expense'},
  {code:'5020',name:'Repairs & maintenance',group:'Expense'},
  {code:'5030',name:'Utilities',group:'Expense'},
  {code:'5040',name:'Petty cash & sundry',group:'Expense'},
  {code:'5050',name:'General expenses',group:'Expense'},
  {code:'5100',name:'Salaries & wages',group:'Expense'},
  {code:'5110',name:'Employer PF & ESIC',group:'Expense'},
  {code:'5900',name:'Cash over / short',group:'Expense',note:'Accepted drawer differences'},
  {code:ACC_SUSPENSE,name:'Suspense (unmapped)',group:'Suspense',note:'Never a mapping target'},
];
export const accAccount=(code:string)=>ACC_CHART.find(a=>a.code===code);
export const accLedgerName=(code:string)=>accAccount(code)?.name??code;

// ── Mapping (source classification → ledger) ──
export const ACC_DEFAULT_MAP:Record<string,string>={
  'line:room':'4000','line:service':'4100','line:discount':'4900','line:adjustment':'4910','folio:guest':'1100','folio:cgst':'2110','folio:sgst':'2120',
  'method:Cash':'1000','method:UPI':'1010','method:Card':'1020','method:Bank transfer':'1030',
  'receipt:payment':'1100','receipt:refund':'1100','receipt:deposit':'2000','receipt:deposit-return':'2000','receipt:company-credit':'1110',
  'expense:Supplies':'5000','expense:Laundry':'5010','expense:Maintenance':'5020','expense:Utilities':'5030','expense:Petty cash':'5040',
  'funding:Cash':'1000','funding:UPI':'1010','funding:Card':'1020','funding:Bank transfer':'1030','funding:Vendor credit':'2200',
  'closing:cash-difference':'5900',
  // Wave B1 (DEC-073): drawer cash banked (Dr bank / Cr cash) and taken by the Owner (Dr drawings / Cr cash); a till transfer books nothing.
  'closing:bank-deposit':'1030','closing:owner-drawing':'3000',
  // Restaurant / room-service bills settled at the outlet (shared till, DEC-052). Bills charged to a folio stay in the folio flow.
  'pos:sales':'4100','pos:cgst':'2110','pos:sgst':'2120',
};
const KEY_LABEL:Record<string,string>={line:'Folio line kind',folio:'Folio',method:'Receipt method (tender)',receipt:'Receipt kind (counter-ledger)',expense:'Expense category',funding:'Expense funding',closing:'Closing record',pos:'Outlet (POS) bill'};
export const accKeyGroup=(key:string)=>KEY_LABEL[key.split(':')[0]]??'Other';
export const accKeyValue=(key:string)=>key.slice(key.indexOf(':')+1);
export const ACC_KEY_PATTERN=/^(line|folio|method|receipt|expense|funding|closing|pos):.+$/;

// ── State slice ──
export type ACCEntry={account:string;debit:number;credit:number};
export type ACCVoucherType='Sales'|'Receipt'|'Payment'|'Journal';
export type ACCSourceKind='line'|'receipt'|'expense'|'closing'|'pos';
export type ACCVoucher={no:string;date:string;type:ACCVoucherType;narration:string;
  source:{kind:ACCSourceKind;id:string;sub:string;booking?:string};entries:ACCEntry[];unmapped:string[]};
export type ACCLock={date:string;lockedAt:string;lockedBy:string;vouchers:ACCVoucher[];debit:number;credit:number;count:number};
export type ACCMapChange={id:string;at:string;actor:string;key:string;before:string;after:string};
export type ACCLockEvent={id:string;at:string;actor:string;date:string;action:'lock'|'unlock';reason?:string};
/** State slice for this module, stored at s.accountingX (optional; absent in old saves → treat as empty). */
/** Production build only (W4-7, clients/sync/accounting.ts): the server's own evaluation of each business date in a window
 *  (GET /accounting/days). When present, the selectors below read it instead of deriving vouchers on the device — the server is
 *  the truth. `opening` = balances before `from` (cumulative trial balance) and, dated on its own "as of" date, the imported opening
 *  trial balance when that date falls inside the window (N5). `accounts` = name and group of ledger codes outside ACC_CHART (imported
 *  ledgers without an HCC account, e.g. Owner's capital · Equity). Never set in the prototype. */
export type ACCServerDay={status:ACCDayStatus;vouchers:ACCVoucher[];exceptions:ACCException[];checks:ACCCheck[]};
export type ACCServer={from:string;to:string;days:Record<string,ACCServerDay>;opening:ACCVoucher[];mapRows:ACCMapRow[];accounts?:Record<string,{name:string;group:ACCTBGroup}>};
/** DEC-077: one recorded Tally export (the Download XML button): the dates it carried, which were re-exports and which provisional.
 *  A03b-05: `ids` = its vouchers carried a REMOTEID (absent on exports recorded before that: Tally may hold those days without ids). */
export type ACCTallyExport={id:string;at:string;by:string;from:string;to:string;dates:string[];reexport:string[];provisional:string[];count:number;ids?:boolean};
/** DEC-077: a date whose vouchers changed after they were exported (its day was reopened, `reopen` set); A03b-05: or a date exported
 *  while accounting events were pending (no `reopen`). The next export carries it again. */
export type ACCReexport={at:string;reason:string;reopen?:string};
export type ACCState={mapping?:Record<string,string>;locks?:Record<string,ACCLock>;mapHistory?:ACCMapChange[];lockHistory?:ACCLockEvent[];server?:ACCServer;
  tallyExports?:ACCTallyExport[];reexport?:Record<string,ACCReexport>};
export const accEmpty=():ACCState=>({});
export const accSlice=(s:State):ACCState=>s.accountingX??accEmpty();
/** Effective mapping = defaults, then the ledgers of the money masters (MA-3: payment methods, expense categories; seed = the same
 *  defaults), overlaid with the slice's overrides (Accounting › Mapping wins). */
export function accMapping(s:State):Record<string,string>{return {...ACC_DEFAULT_MAP,...mnyLedgerDefaults(s),...(accSlice(s).mapping??{})};}
export const accLockOf=(s:State,date:string)=>accSlice(s).locks?.[date];
/** Roles that may lock a closed period (with the coarse 'reports' permission). */
export const ACC_LOCK_ROLES=['Owner','Manager','Accountant'];

// ── Voucher derivation ──
/** Signed credit amount → entry (positive = credit, negative = debit). */
const cr=(account:string,amount:number):ACCEntry=>amount>=0?{account,debit:0,credit:amount}:{account,debit:-amount,credit:0};
const dr=(account:string,amount:number):ACCEntry=>cr(account,-amount);
const RECEIPT_IN=['payment','deposit'];
const ymd=(d:string)=>d.split('-').join('');
const PREFIX:Record<ACCVoucherType,string>={Sales:'SAL',Receipt:'RCT',Payment:'PMT',Journal:'JRN'};

/** All vouchers for a business date, derived live from the source records with the current mapping (pure). */
export function accLiveVouchers(s:State,date:string,mapping=accMapping(s)):ACCVoucher[]{
  const out:Omit<ACCVoucher,'no'>[]=[];
  const map=(key:string,miss:string[])=>{const a=mapping[key];if(a&&accAccount(a)&&a!==ACC_SUSPENSE)return a;miss.push(key);return ACC_SUSPENSE;};
  const push=(v:Omit<ACCVoucher,'no'|'entries'|'unmapped'>,miss:string[],entries:ACCEntry[])=>{
    const e=entries.filter(x=>x.debit||x.credit);if(e.length)out.push({...v,entries:e,unmapped:[...new Set(miss)]});};
  for(const b of s.bookings){
    for(const l of b.lines.filter(l=>l.date===date&&l.amount!==0)){
      const miss:string[]=[],t=lineTax(l),g=gstSplit(t);
      push({date,type:'Sales',narration:`${l.label} · ${b.guest} · room ${b.room}`,source:{kind:'line',id:`${b.id}/${l.id}`,sub:l.kind,booking:b.id}},miss,
        [dr(map('folio:guest',miss),l.amount+t),cr(map('line:'+l.kind,miss),l.amount),cr(map('folio:cgst',miss),g.cgst),cr(map('folio:sgst',miss),g.sgst)]);
    }
  }
  for(const b of s.bookings){
    for(const r of b.receipts.filter(r=>r.date===date&&r.amount!==0)){
      const miss:string[]=[],src={kind:'receipt' as const,id:`${b.id}/${r.id}`,sub:r.kind,booking:b.id};
      const counter=map('receipt:'+r.kind,miss);
      if(r.kind==='company-credit'){push({date,type:'Journal',narration:`Company credit · ${r.reference} · ${b.guest}`,source:src},miss,[dr(counter,r.amount),cr(map('folio:guest',miss),r.amount)]);continue;}
      const tender=map('method:'+r.method,miss),inflow=RECEIPT_IN.includes(r.kind);
      const label=r.kind==='payment'?'Payment received':r.kind==='deposit'?'Deposit received':r.kind==='refund'?'Payment refunded':r.kind==='deposit-return'?'Deposit returned':r.kind;
      push({date,type:inflow?'Receipt':'Payment',narration:`${label} · ${r.method} · ${r.reference} · ${b.guest}`,source:src},miss,
        inflow?[dr(tender,r.amount),cr(counter,r.amount)]:[dr(counter,r.amount),cr(tender,r.amount)]);
    }
  }
  // One Sales voucher per outlet receipt: Dr tender, Cr outlet revenue + CGST + SGST. A void receipt (negative) reverses its sale.
  for(const r of posSlice(s).receipts.filter(r=>r.date===date&&r.amount!==0)){
    const miss:string[]=[],g=gstSplit(r.tax);
    push({date,type:'Sales',narration:`${r.kind==='void'?'Outlet bill void':'Outlet bill'} · ${r.outlet} · ${r.method} · ${r.reference} · ${r.order}`,source:{kind:'pos',id:r.id,sub:r.kind}},miss,
      [dr(map('method:'+r.method,miss),r.amount),cr(map('pos:sales',miss),r.net),cr(map('pos:cgst',miss),g.cgst),cr(map('pos:sgst',miss),g.sgst)]);
  }
  for(const e of s.expenses.filter(e=>e.date===date&&expenseCounts(e)&&e.amount!==0)){
    const miss:string[]=[],head=map('expense:'+e.category,miss),fund=map('funding:'+e.method,miss);
    push({date,type:mnyIsCredit(s,e.method)?'Journal':'Payment',narration:`${e.reversalOf?'Reversal of '+e.reversalOf+' · ':''}${e.vendor||e.description||'—'} · ${e.category} · ${e.method}`, // DEC-120: the vendor is optional
      source:{kind:'expense',id:e.id,sub:e.reversalOf?'reversal':'expense'}},miss,[dr(head,e.amount),cr(fund,e.amount)]);
  }
  // ── Wave B1 (DEC-073): cash leaving the drawer — same vouchers as the server projector (keys banking:{id}, banking:{id}/rejected, drawing:{id}) ──
  for(const r of cxSlice(s).records){
    const cash=(m:string[])=>map('method:Cash',m);
    if(r.kind==='banking'){
      if(r.date===date){const miss:string[]=[];push({date,type:'Journal',narration:`Cash deposited in bank · ${r.bank??''} · slip ${r.slip??''} · deposit ${r.id}`,source:{kind:'closing',id:'banking:'+r.id,sub:'bank-deposit'}},miss,
        [dr(map('closing:bank-deposit',miss),r.amount),cr(cash(miss),r.amount)]);}
      if(r.status==='Rejected'&&r.decidedDate===date){const miss:string[]=[];push({date,type:'Journal',narration:`Bank deposit rejected · cash back in the drawer · ${r.bank??''} · slip ${r.slip??''} · deposit ${r.id}`,
        source:{kind:'closing',id:'banking:'+r.id+'/rejected',sub:'bank-deposit-rejected'}},miss,[dr(cash(miss),r.amount),cr(map('closing:bank-deposit',miss),r.amount)]);}
    }else if(r.kind==='drawing'&&r.date===date){const miss:string[]=[];push({date,type:'Payment',narration:`Owner drawing · ${r.reason??''} · drawing ${r.id}`,source:{kind:'closing',id:'drawing:'+r.id,sub:'owner-drawing'}},miss,
      [dr(map('closing:owner-drawing',miss),r.amount),cr(cash(miss),r.amount)]);}
  }
  // ── end Wave B1 ──
  const diff=accCashDifference(s,date);
  if(diff){const miss:string[]=[],cash=map('method:Cash',miss),acct=map('closing:cash-difference',miss);
    push({date,type:'Journal',narration:`Cash ${diff>0?'over':'short'} accepted at shift close · ${date}`,source:{kind:'closing',id:`CLOSE/${date}/cash-difference`,sub:'cash-difference'}},miss,
      diff>0?[dr(cash,diff),cr(acct,diff)]:[dr(acct,-diff),cr(cash,-diff)]);}
  const n:Record<string,number>={};
  return (['Sales','Receipt','Payment','Journal'] as ACCVoucherType[]).flatMap(t=>out.filter(v=>v.type===t)).map(v=>{
    n[v.type]=(n[v.type]??0)+1;return {...v,no:`${PREFIX[v.type]}/${ymd(date)}/${String(n[v.type]).padStart(3,'0')}`};});
}
/** Accepted cash difference for a date (counted − expected) from its closing record, or the closed shift of the open day. */
export function accCashDifference(s:State,date:string):number{
  const c=s.closings?.[date];if(c)return c.cash.difference;
  if(date===s.date&&s.shift?.date===date&&s.shift.status==='Closed')return s.shift.counted-s.shift.expected;
  return 0;
}
/** Vouchers shown for a date: the locked snapshot when locked, else the live derivation. */
export function accVouchers(s:State,date:string):ACCVoucher[]{const sv=accSlice(s).server;if(sv)return sv.days[date]?.vouchers??[];return accLockOf(s,date)?.vouchers??accLiveVouchers(s,date);}
export const accVoucherTotals=(v:ACCVoucher)=>({debit:v.entries.reduce((a,e)=>a+e.debit,0),credit:v.entries.reduce((a,e)=>a+e.credit,0)});
export const accBalanced=(v:ACCVoucher)=>{const t=accVoucherTotals(v);return t.debit===t.credit;};
export function accTotals(vs:ACCVoucher[]){return vs.reduce((a,v)=>{const t=accVoucherTotals(v);return {debit:a.debit+t.debit,credit:a.credit+t.credit};},{debit:0,credit:0});}

// ── Trial balance ──
/** Trial-balance group: the chart's groups, plus Equity for imported ledgers (production only). */
export type ACCTBGroup=ACCGroup|'Equity';
export type ACCTBRow={account:string;name:string;group:ACCTBGroup;debit:number;credit:number;balance:number};
/** Trial balance over a set of vouchers. balance = debit − credit (positive = debit balance). Chart order. `accounts` names codes
 *  outside the chart (production: imported ledgers, ACCServer.accounts). */
export function accTrialBalance(vs:ACCVoucher[],accounts?:ACCServer['accounts']):ACCTBRow[]{
  const m=new Map<string,{debit:number;credit:number}>();
  for(const v of vs)for(const e of v.entries){const x=m.get(e.account)??{debit:0,credit:0};x.debit+=e.debit;x.credit+=e.credit;m.set(e.account,x);}
  const order=(c:string)=>{const i=ACC_CHART.findIndex(a=>a.code===c);return i<0?999:i;};
  return [...m.entries()].sort((a,b)=>order(a[0])-order(b[0])||(a[0]<b[0]?-1:1)).map(([account,x])=>({account,name:accAccount(account)?.name??accounts?.[account]?.name??account,group:accAccount(account)?.group??accounts?.[account]?.group??'Suspense',debit:x.debit,credit:x.credit,balance:x.debit-x.credit}));
}
/** Dates with any accounting activity, closed dates, locks and the business date — newest first. */
export function accDates(s:State):string[]{
  const sv=accSlice(s).server;if(sv)return [...new Set([s.date,...Object.keys(sv.days)])].filter(x=>/^\d{4}-\d{2}-\d{2}$/.test(x)&&x<=s.date).sort().reverse();
  const d=new Set<string>([s.date,...s.closedDates,...Object.keys(accSlice(s).locks??{}),...Object.keys(s.closings??{})]);
  s.bookings.forEach(b=>{b.lines.forEach(l=>d.add(l.date));b.receipts.forEach(r=>d.add(r.date));});
  posSlice(s).receipts.forEach(r=>d.add(r.date));
  s.expenses.filter(expenseCounts).forEach(e=>d.add(e.date));
  return [...d].filter(x=>/^\d{4}-\d{2}-\d{2}$/.test(x)&&x<=s.date).sort().reverse();
}
/** Vouchers for every date up to and including `date` (cumulative trial balance). */
export function accVouchersUpTo(s:State,date:string):ACCVoucher[]{const sv=accSlice(s).server;return [...(sv?sv.opening.filter(v=>v.date<date||(v.source.sub==='opening-import'&&v.date<=date)):[]),...accDates(s).filter(d=>d<=date).sort().flatMap(d=>accVouchers(s,d))];}

// ── Status & exceptions ──
export type ACCDayStatus='Open'|'Closed'|'Locked';
export function accDayStatus(s:State,date:string):ACCDayStatus{const sv=accSlice(s).server;if(sv)return sv.days[date]?.status??(date<s.date?'Closed':'Open');
  // DEC-077: the day the Owner reopened counts as Open (live vouchers, not lockable) until it is closed again.
  return accLockOf(s,date)?'Locked':closingReopened(s)?.date===date?'Open':s.closedDates.includes(date)?'Closed':'Open';}
/** kind 'stuck' / 'pending' come from the server (parked / unprocessed outbox events); the demo raises 'stuck' for a parked accounting event (Wave C3). */
export type ACCException={id:string;severity:'error'|'warning'|'info';kind:'unbalanced'|'unmapped'|'drift'|'stuck'|'pending'|'open-day';title:string;detail:string;voucher?:string;source?:string;key?:string};
const fingerprint=(vs:ACCVoucher[])=>vs.map(v=>v.source.id+'='+accVoucherTotals(v).debit).sort().join('|');
export function accExceptions(s:State,date:string):ACCException[]{
  const sv=accSlice(s).server;if(sv)return sv.days[date]?.exceptions??[];
  const vs=accVouchers(s,date),out:ACCException[]=[];
  for(const v of vs){const t=accVoucherTotals(v);
    if(t.debit!==t.credit)out.push({id:'unbal:'+v.no,severity:'error',kind:'unbalanced',title:`Voucher ${v.no} is unbalanced`,detail:`Debits ₹${(t.debit/100).toFixed(2)} ≠ credits ₹${(t.credit/100).toFixed(2)}`,voucher:v.no,source:v.source.id});
    for(const k of v.unmapped)out.push({id:'unmap:'+v.no+':'+k,severity:'error',kind:'unmapped',title:`No ledger mapped for “${accKeyValue(k)}”`,detail:`${accKeyGroup(k)} → posted to Suspense in ${v.no}. Map it in Mapping, then lock.`,voucher:v.no,source:v.source.id,key:k});}
  const t=accTotals(vs);
  if(t.debit!==t.credit)out.push({id:'unbal:day',severity:'error',kind:'unbalanced',title:'Trial balance does not agree',detail:`Debits ₹${(t.debit/100).toFixed(2)} ≠ credits ₹${(t.credit/100).toFixed(2)}`});
  const lock=accLockOf(s,date);
  if(lock&&fingerprint(lock.vouchers)!==fingerprint(accLiveVouchers(s,date)))out.push({id:'drift',severity:'warning',kind:'drift',title:'Live sources differ from the locked snapshot',detail:'Source records for this locked date changed after locking. The snapshot is kept; review and unlock if a correction is needed.'});
  // Wave C3: a parked accounting event (System health) blocks the lock of every unlocked date, as on the server.
  const stuck=lock?0:sysParkedAccounting(s);
  if(stuck)out.push({id:'stuck',severity:'error',kind:'stuck',title:SYS_STUCK_TITLE,detail:sysStuckDetail(stuck)});
  if(accDayStatus(s,date)==='Open')out.push({id:'open',severity:'info',kind:'open-day',title:'Business day not closed',detail:'Vouchers are provisional until the day is closed and locked.'});
  return out;
}
export const accBlocking=(xs:ACCException[])=>xs.filter(x=>x.severity==='error');
/** Why a date cannot be locked now ('' = it can). Permission is checked separately. */
export function accLockBlocker(s:State,date:string):string{
  if(accLockOf(s,date))return 'This date is already locked.';
  if(accDayStatus(s,date)==='Open')return 'Close the business day before locking it.';
  const e=accBlocking(accExceptions(s,date));
  if(e.length)return `Resolve ${e.length} exception${e.length===1?'':'s'} first (${e[0].title}).`;
  return '';
}

// ── Reconciliation with existing reports ──
export type ACCCheck={id:string;label:string;report:number;ledger:number;ok:boolean;note:string};
const isTender=(a:string,mapping:Record<string,string>)=>Object.entries(mapping).some(([k,v])=>k.startsWith('method:')&&v===a);
export function accReconcile(s:State,date:string):ACCCheck[]{
  const sv=accSlice(s).server;if(sv)return sv.days[date]?.checks??[];
  const vs=accVouchers(s,date),map=accMapping(s),p=reportPack(s,date,date);
  const v=(tab:string,id:string)=>p.tabs.find(t=>t.id===tab)!.metrics.find(m=>m.id===id)?.value??0;
  const net=(code:string,f:(x:ACCVoucher)=>boolean=()=>true)=>vs.filter(f).reduce((a,x)=>a+x.entries.filter(e=>e.account===code).reduce((b,e)=>b+e.credit-e.debit,0),0);
  const lines=(k:string)=>(x:ACCVoucher)=>x.source.kind==='line'&&x.source.sub===k;
  const tenderMove=(kinds:string[])=>vs.filter(x=>x.source.kind==='receipt'&&kinds.includes(x.source.sub)).reduce((a,x)=>a+x.entries.filter(e=>isTender(e.account,map)).reduce((b,e)=>b+e.debit-e.credit,0),0);
  // Expense vouchers: entry 0 is the expense head (Dr for an expense, Cr for a reversal record).
  const expenseMove=vs.filter(x=>x.source.kind==='expense').reduce((a,x)=>a+x.entries[0].debit-x.entries[0].credit,0);
  const c=(id:string,label:string,report:number,ledger:number,note:string):ACCCheck=>({id,label,report,ledger,ok:report===ledger,note});
  const checks=[
    c('room','Room revenue',v('revenue','room'),net(map['line:room'],lines('room')),'Reports › Revenue · room lines'),
    c('service','Other revenue',v('revenue','service'),net(map['line:service'],lines('service')),'Reports › Revenue · service lines'),
    c('discount','Discounts (negative)',v('revenue','discount'),net(map['line:discount'],lines('discount')),'Reports › Revenue · discount lines'),
    c('adjustment','Adjustments & credit notes',v('revenue','adjustment'),net(map['line:adjustment'],lines('adjustment')),'Reports › Revenue · adjustment lines'),
    c('tax','GST (CGST + SGST)',v('revenue','tax'),[...new Set([map['folio:cgst'],map['folio:sgst']])].reduce((a,code)=>a+net(code,x=>x.source.kind==='line'),0),'Reports › Revenue · tax'),
    c('collections','Net receipts (payments − refunds)',v('receipts','net'),tenderMove(['payment','refund']),'Overview “collections” · Reports › Receipts'),
    c('deposits','Deposits net (in − out)',v('receipts','deposit-in')+v('receipts','deposit-out'),tenderMove(['deposit','deposit-return']),'Reports › Receipts · deposits'),
    c('expenses','Posted expenses',v('expenses','total'),expenseMove,'Reports › Expenses · total'),
    c('outlet','Outlet bills settled (net of voids)',closingOutlet(s,date).net,vs.filter(x=>x.source.kind==='pos').reduce((a,x)=>a+x.entries.filter(e=>isTender(e.account,map)).reduce((b,e)=>b+e.debit-e.credit,0),0),'Restaurant › Day summary · outlet receipts'),
  ];
  const cashLedger=-net(map['method:Cash']);
  const rec=s.closings?.[date];
  if(rec)checks.push(c('cash','Cash in hand movement',rec.cash.counted-rec.cash.opening,cashLedger,'Closing record · counted − opening float'));
  else if(date===s.date)checks.push(c('cash','Cash in hand movement',drawer(s)-s.openingCash+accCashDifference(s,date),cashLedger,'Cashier · expected drawer − opening float'));
  return checks;
}

// ── Mapping table rows (defaults + classifications discovered in data) ──
export type ACCMapRow={key:string;group:string;value:string;account:string;defaultAccount?:string;changed:boolean;uses:number;mapped:boolean};
export function accMapRows(s:State):ACCMapRow[]{
  const sv=accSlice(s).server;if(sv)return sv.mapRows;
  const map=accMapping(s),uses:Record<string,number>={};
  const add=(k:string)=>{uses[k]=(uses[k]??0)+1;};
  s.bookings.forEach(b=>{b.lines.forEach(l=>{add('line:'+l.kind);add('folio:guest');if(lineTax(l)){add('folio:cgst');add('folio:sgst');}});b.receipts.filter(r=>r.kind==='company-credit').forEach(()=>add('folio:guest'));b.receipts.forEach(r=>{add('receipt:'+r.kind);if(r.kind!=='company-credit')add('method:'+r.method);});});
  s.expenses.filter(expenseCounts).forEach(e=>{add('expense:'+e.category);add('funding:'+e.method);});
  posSlice(s).receipts.forEach(r=>{add('pos:sales');if(r.tax){add('pos:cgst');add('pos:sgst');}add('method:'+r.method);});
  const keys=[...new Set([...Object.keys(ACC_DEFAULT_MAP),...Object.keys(uses)])];
  const order=Object.keys(KEY_LABEL);
  return keys.sort((a,b)=>order.indexOf(a.split(':')[0])-order.indexOf(b.split(':')[0])).map(key=>{const account=map[key]??'';
    return {key,group:accKeyGroup(key),value:accKeyValue(key),account,defaultAccount:ACC_DEFAULT_MAP[key],changed:!!account&&account!==ACC_DEFAULT_MAP[key],uses:uses[key]??0,mapped:!!account&&!!accAccount(account)&&account!==ACC_SUSPENSE};});
}

// ── Export (generic voucher import format) ──
const csvCell=csvMinimal;
const rupees=(p:number)=>(p/100).toFixed(2);
export const ACC_CSV_HEADER=['Date','Voucher No','Voucher Type','Ledger','Debit','Credit','Narration','Source ID'];
/** One row per voucher entry. Generic import format (not Tally-specific); amounts in rupees, 2 decimals; CRLF. */
export function accCsv(vs:ACCVoucher[]):string{
  const rows:(string|number)[][]=[ACC_CSV_HEADER];
  for(const v of vs)for(const e of v.entries)rows.push([v.date,v.no,v.type,accLedgerName(e.account),e.debit?rupees(e.debit):'',e.credit?rupees(e.credit):'',v.narration,v.source.id]);
  return rows.map(r=>r.map(csvCell).join(',')).join('\r\n');
}
/** Summary figures for a date (cards / stats). */
export function accDaySummary(s:State,date:string){
  const vs=accVouchers(s,date),t=accTotals(vs),ex=accExceptions(s,date);
  return {date,status:accDayStatus(s,date),count:vs.length,debit:t.debit,credit:t.credit,balanced:t.debit===t.credit,
    errors:accBlocking(ex).length,warnings:ex.filter(x=>x.severity==='warning').length,checks:accReconcile(s,date)};
}
/** Source record lookup for a voucher (dialog "trace to source"). */
export function accSourceOf(s:State,v:ACCVoucher):{booking?:Booking;line?:Line;receipt?:Receipt;expense?:Expense;posReceipt?:POSReceipt}{
  if(v.source.kind==='expense')return {expense:s.expenses.find(e=>e.id===v.source.id||'expense:'+e.id===v.source.id)};
  if(accSlice(s).server)return {}; // server source keys (charge:…, receipt:…, pos:…) are not prototype record ids
  if(v.source.kind==='pos')return {posReceipt:posSlice(s).receipts.find(r=>r.id===v.source.id)};
  if(v.source.kind==='closing')return {};
  const i=v.source.id.indexOf('/'),bid=v.source.id.slice(0,i),rid=v.source.id.slice(i+1),booking=s.bookings.find(b=>b.id===bid);
  return v.source.kind==='line'?{booking,line:booking?.lines.find(l=>l.id===rid)}:{booking,receipt:booking?.receipts.find(r=>r.id===rid)};
}

// ── Tally-style XML preview (W05-E) ──
// Rule source (read-only): ETP Tally preparation — building or saving this XML is NOT "imported into Tally"; only Tally's own import confirms that.
// Here the preview is always SIMULATED: nothing is sent anywhere. Tally convention: debit = ISDEEMEDPOSITIVE Yes with a negative amount, credit = No with a positive amount.
export const TALLY_MAX_DAYS=31;
const xmlEsc=(t:string)=>t.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g,'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const tallyDate=(d:string)=>d.split('-').join('');
export type TallyPreview={xml:string;count:number;debit:number;credit:number;from:string;to:string;dates:number;simulated:true;error?:string;
  /** The business dates carried (incl. DEC-077 re-exports outside the range). */dateList:string[];
  /** DEC-077: dates carried again because they changed after an earlier export; provisional = the day is still reopened, or (A03b-05) a
   *  date that is not locked while accounting events are pending (`lagging`, `pending` = their count, production only). */reexport:string[];provisional:string[];
  /** DEC-077: the reopened day, when carried. */reopened:string[];lagging:string[];pending:number;
  /** A03b-05: carried dates an export recorded before vouchers carried a REMOTEID took to Tally. */legacy:string[]};
/** DEC-077: the XML note naming a re-exported / provisional day (escaped; "--" cannot end the comment early). */
export const accTallyNote=(text:string)=>`<!-- ${xmlEsc(text).split('--').join('- -')} -->`;
/** A03b-05: the fixed comment line after the SIMULATED comment (server VoucherExport.TallyIdNote, same text). */
export const ACC_TALLY_ID_NOTE='<!-- VOUCHER IDS · every voucher carries a REMOTEID and the same GUID (HCC-hotel identity-source record) that never changes.'
  +' With "Overwrite vouchers when a voucher with same GUID exists" set to Yes in Tally\'s import (TallyPrime: Import › Transactions), importing'
  +' a voucher again replaces it instead of adding a second copy. ACTION stays Create; Tally assigns its own VCHKEY and MASTERID. -->';
/** A03b-05: the hotel's Tally identity in the demo (the server keeps one random GUID per property, acc.tally_identity). */
export const ACC_TALLY_DEMO_ID='DEMO';
/** A03b-05: a voucher's REMOTEID / GUID (server VoucherExport.RemoteId): the same for that voucher in every export, unique per voucher. */
export const accTallyRemoteId=(identity:string,sourceKey:string)=>`HCC-${identity}-${sourceKey}`;
// The export notes (server VoucherExport.ReexportNote / ReexportAfterProvisionalNote / ReopenedNote / PendingNote / LegacyNote, same text).
export const accTallyReexportNote=(d:string,reason:string)=>`RE-EXPORT · ${d} changed after it was exported (day reopened: ${reason}). Its vouchers keep their REMOTEID: importing this file replaces them in Tally and adds the new ones, so nothing needs deleting first.`;
export const accTallyReexportProvisionalNote=(d:string,reason:string)=>`RE-EXPORT · ${d} was exported while it was provisional (${reason}). Its vouchers keep their REMOTEID: importing this file replaces them in Tally and adds any that were missing.`;
export const accTallyReopenedNote=(d:string)=>`PROVISIONAL · ${d} is reopened and may still change; it is carried again by the first export after it is closed again, and importing that file replaces these vouchers by their REMOTEID.`;
export const accTallyPendingNote=(d:string,n:number)=>`PROVISIONAL · ${d} is not locked and ${n} accounting event(s) of this hotel are not yet in the vouchers (still being processed, or parked after an error), so this day may be incomplete. It is carried again by the first export after they are processed, and importing that file replaces these vouchers by their REMOTEID.`;
export const accTallyLegacyNote=(d:string)=>`LEGACY · ${d} was exported before vouchers carried a REMOTEID. If that earlier file was imported, delete that day's HCC vouchers from it in Tally before importing this file, or Tally will hold them twice.`;
/** A03b-05: the re-export flag reason of a date exported while accounting events were pending (server AccountingQueries.PendingFlagReason). */
export const accTallyPendingReason=(n:number)=>`${n} accounting event(s) were still pending`;
/** Production: accounting events of the property not yet in the vouchers, from the server's "pending" exception (same count on every unlocked date). */
export function accPendingEvents(s:State,dates:string[]):number{
  const sv=accSlice(s).server;if(!sv)return 0;
  return Math.max(0,...dates.flatMap(d=>(sv.days[d]?.exceptions??[]).filter(x=>x.kind==='pending').map(x=>Number(/^(\d+)/.exec(x.detail)?.[1]??1))));
}
/**
 * Vouchers of every business date from..to (inclusive, ≤ 31 days) as a Tally "Import Data" envelope. A03b-05: every voucher carries a
 * stable REMOTEID / GUID (accTallyRemoteId; demo identity DEMO, production preview SERVER — the downloaded file is the server's, with the
 * hotel's own identity), so importing it again replaces it in Tally. DEC-077: dates flagged changed after an earlier export
 * (accountingX.reexport) are carried again even outside the range, each with a RE-EXPORT note; the reopened day is PROVISIONAL, and so
 * (A03b-05, production) is every date that is not locked while accounting events are pending.
 */
export function accTallyXml(s:State,from:string,to:string,company?:string,identity?:string):TallyPreview{
  const empty={xml:'',count:0,debit:0,credit:0,from,to,dates:0,simulated:true as const,dateList:[],reexport:[],provisional:[],reopened:[],lagging:[],pending:0,legacy:[]};
  // F10 (W05 review): a real calendar date, not only the yyyy-mm-dd shape (2026-02-30 used to pass).
  const real=(d:string)=>/^\d{4}-\d{2}-\d{2}$/.test(d)&&!Number.isNaN(Date.parse(d+'T00:00:00Z'))&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;
  if(!real(from)||!real(to)||from>to)return {...empty,error:'Choose a valid range: "from" on or before "to".'};
  const days=Math.round((Date.parse(to)-Date.parse(from))/86400000)+1;
  if(days>TALLY_MAX_DAYS)return {...empty,error:`Choose at most ${TALLY_MAX_DAYS} days.`};
  const flags=accSlice(s).reexport??{},open=closingReopened(s)?.date;
  const dates=[...new Set([...accDates(s).filter(d=>d>=from&&d<=to),...Object.keys(flags).filter(d=>accVouchers(s,d).length)])].sort(),vs=dates.flatMap(d=>accVouchers(s,d));
  const pending=accPendingEvents(s,dates),lagging=pending>0?dates.filter(d=>accDayStatus(s,d)!=='Locked'):[];
  const reexport=dates.filter(d=>flags[d]),reopened=dates.filter(d=>d===open),provisional=dates.filter(d=>d===open||lagging.includes(d));
  const legacy=dates.filter(d=>(accSlice(s).tallyExports??[]).some(e=>!e.ids&&e.dates.includes(d)));
  const notes=[...reexport.map(d=>accTallyNote(flags[d].reopen?accTallyReexportNote(d,flags[d].reason):accTallyReexportProvisionalNote(d,flags[d].reason))),
    ...reopened.map(d=>accTallyNote(accTallyReopenedNote(d))),...lagging.map(d=>accTallyNote(accTallyPendingNote(d,pending))),
    ...legacy.map(d=>accTallyNote(accTallyLegacyNote(d)))];
  const name=xmlEsc(company??s.settings.hotel),tag=identity??(accSlice(s).server?'SERVER':ACC_TALLY_DEMO_ID);
  const ids=vs.map(v=>accTallyRemoteId(tag,v.source.id));
  if(new Set(ids).size!==ids.length)throw new Error('Two vouchers in one Tally export share a source key; every REMOTEID must be unique.');
  const body=vs.map((v,i)=>`   <TALLYMESSAGE xmlns:UDF="TallyUDF"><VOUCHER REMOTEID="${xmlEsc(ids[i])}" VCHTYPE="${v.type}" ACTION="Create" OBJVIEW="Accounting Voucher View">
    <DATE>${tallyDate(v.date)}</DATE><GUID>${xmlEsc(ids[i])}</GUID><VOUCHERTYPENAME>${v.type}</VOUCHERTYPENAME><VOUCHERNUMBER>${xmlEsc(v.no)}</VOUCHERNUMBER><NARRATION>${xmlEsc(v.narration)}</NARRATION><EFFECTIVEDATE>${tallyDate(v.date)}</EFFECTIVEDATE>
${v.entries.map(e=>`    <ALLLEDGERENTRIES.LIST><LEDGERNAME>${xmlEsc(accLedgerName(e.account))}</LEDGERNAME><ISDEEMEDPOSITIVE>${e.debit?'Yes':'No'}</ISDEEMEDPOSITIVE><AMOUNT>${e.debit?'-'+rupees(e.debit):rupees(e.credit)}</AMOUNT></ALLLEDGERENTRIES.LIST>`).join('\n')}
   </VOUCHER></TALLYMESSAGE>`).join('\n');
  const t=accTotals(vs);
  const xml=`<?xml version="1.0" encoding="UTF-8"?>
<!-- SIMULATED PREVIEW · Hotel Control Centre prototype · fictional data · nothing was sent to Tally · saving this file does not mean it was imported -->
${ACC_TALLY_ID_NOTE}
${notes.map(n=>n+'\n').join('')}<ENVELOPE>
 <HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER>
 <BODY><IMPORTDATA>
  <REQUESTDESC><REPORTNAME>Vouchers</REPORTNAME><STATICVARIABLES><SVCURRENTCOMPANY>${name}</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
  <REQUESTDATA>
${body}
  </REQUESTDATA>
 </IMPORTDATA></BODY>
</ENVELOPE>
`;
  return {xml,count:vs.length,debit:t.debit,credit:t.credit,from,to,dates:dates.length,simulated:true,dateList:dates,reexport,provisional,reopened,lagging,pending,legacy};
}
