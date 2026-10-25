// W05-D · Cash book — pure selectors, lock rules and CSV. Exported names start with 'cb' / 'CB' (shared mock namespace).
// NO second source of truth: every entry is read from the existing `shiftMovements` selector (evaluated for the business date asked for),
// the closing record (opening, expected, counted, banked) and the cashier shift. Only the day LOCK is stored here (s.cashbookX).
// Rules referenced read-only from: SCC `expense` ledger (running balance, locked days) and ETP CashBookRepository.cs /
// CashBalanceReconciliationService.cs (opening = previous closing, book balance vs counted, difference shown separately).
import type {State} from '../types';
import {shiftMovements,type CashMovement} from './closing';
import {addDays} from '../money';

export type CBLogEntry={action:'lock'|'reopen';by:string;at:string;reason?:string};
export type CBLock={lockedBy:string;lockedAt:string;log:CBLogEntry[]};
/** State slice at s.cashbookX (optional; absent in old saves → no day is locked). `log` keeps lock/reopen history for dates that are open again. */
export type CBState={locks:Record<string,CBLock>;history:Record<string,CBLogEntry[]>};
export const cbSlice=(s:State):CBState=>{const x=s.cashbookX as Partial<CBState>|undefined;return {locks:x?.locks??{},history:x?.history??{}};};

/** Roles that may act as checker for a day lock (a different person from whoever counted). */
export const CB_CHECKER_ROLES=['Owner','Manager','Accountant'];
export const CB_REOPEN_ROLES=['Owner'];

export type CBEntry={id:string;kind:string;group:'receipt'|'payment';label:string;reference:string;guest?:string;booking?:string;order?:string;actor:string;in:number;out:number};
export type CBDay={
  date:string;known:boolean;status:'Open'|'Closed';locked?:CBLock;
  /** Opening balance carried forward from the previous day's counted cash less banking. */
  opening:number;entries:CBEntry[];receipts:number;payments:number;banked:number;
  /** Book balance: opening + receipts − payments − banking. */
  closing:number;
  /** From the closing record / cashier shift: expected cash, counted cash, difference (counted − expected). */
  expected?:number;counted?:number;difference?:number;
  /** Cash handed on to the next business day (counted − banked); equals `closing` when there is no difference. */
  carried:number;
  /** Book total reconciles with the closing record (opening + movements = expected cash). */
  reconciles:boolean;
};

function toEntry(m:CashMovement):CBEntry{
  const out=m.amount<0?-m.amount:0,inn=m.amount>0?m.amount:0;
  // Signed movement decides the side: a reversed expense is a receipt, a POS void or refund a payment.
  return {id:m.id,kind:m.kind,group:m.amount<0?'payment':'receipt',label:m.label,reference:m.reference,guest:m.guest,booking:m.booking,order:m.order,actor:m.actor,in:inn,out};
}
/** Dates the cash book knows: every closing record plus the current business date. */
export function cbDates(s:State):string[]{return [...new Set([...Object.keys(s.closings??{}),s.date])].sort();}
/** Opening balance of a date: closing record > current opening float > previous record's carry. */
function openingOf(s:State,d:string):number|undefined{
  const c=s.closings?.[d];if(c)return c.cash.opening;
  if(d===s.date)return s.openingCash;
  return undefined;
}
/** One business day of the cash book. Entries = the existing cashier-shift movements for that date (never recomputed differently). */
export function cbDay(s:State,d:string):CBDay{
  const rec=s.closings?.[d],known=rec!==undefined||d===s.date;
  const mv=known?shiftMovements({...s,date:d}):[];
  const entries=mv.map(toEntry);
  const banked=rec?.cash.banked??0;
  if(banked>0)entries.push({id:'bank:'+d,kind:'banking',group:'payment',label:'Banked after count',reference:'Cash taken to bank / owner handover',actor:rec!.closedBy,in:0,out:banked});
  const opening=openingOf(s,d)??0;
  const receipts=entries.reduce((a,e)=>a+e.in,0),payments=entries.reduce((a,e)=>a+e.out,0);
  const closing=opening+receipts-payments;
  const cur=d===s.date&&!rec;
  const expected=rec?.cash.expected??(cur&&s.shiftClose?s.shiftClose.expected:undefined);
  const counted=rec?.cash.counted??(cur&&s.shiftClose?s.shiftClose.counted:undefined);
  const difference=counted!==undefined&&expected!==undefined?counted-expected:undefined;
  const carried=counted!==undefined?counted-banked:closing;
  const reconciles=expected===undefined?true:expected===opening+receipts-(payments-banked);
  return {date:d,known,status:rec||s.closedDates.includes(d)?'Closed':'Open',locked:cbSlice(s).locks[d],opening,entries,receipts,payments,banked,closing,expected,counted,difference,carried,reconciles};
}
export type CBRangeRow=CBDay;
/** Range view: one row per known business date between from and to (inclusive). */
export function cbRange(s:State,from:string,to:string):{days:CBDay[];opening:number;receipts:number;payments:number;closing:number}{
  const days=cbDates(s).filter(d=>d>=from&&d<=to).map(d=>cbDay(s,d));
  return {days,opening:days[0]?.opening??0,receipts:days.reduce((a,d)=>a+d.receipts,0),payments:days.reduce((a,d)=>a+d.payments,0),closing:days.at(-1)?.closing??0};
}
/** Who counted a date (segregation of duties, W05 review F1): the closer, the counter and the approver of a cash difference — all read from the
 *  closing record so they survive NEXT_DAY — plus, for the current business date, the cashier shift's submitter / approver / SHIFT_CLOSE user. */
export function cbCounters(s:State,d:string):string[]{
  const out=new Set<string>();const rec=s.closings?.[d];
  if(rec?.closedBy)out.add(rec.closedBy);
  if(rec?.cash.countedBy)out.add(rec.cash.countedBy);
  if(rec?.cash.approvedBy)out.add(rec.cash.approvedBy);
  if(s.shift?.date===d){out.add(s.shift.submittedBy);if(s.shift.decidedBy)out.add(s.shift.decidedBy);}
  if(d===s.date&&s.shiftClose?.countedBy)out.add(s.shiftClose.countedBy);
  return [...out].filter(Boolean);
}
/** Legacy rule (F1): a closing record saved before `countedBy` existed does not say who counted, so only an Owner (who did not close it) may lock it. */
export const cbCounterUnknown=(s:State,d:string)=>{const rec=s.closings?.[d];return !!rec&&!rec.cash.countedBy;};
/** Whether `uid` could lock `d` now, with the reason when not. */
export function cbCanLock(s:State,uid:string,d:string):{ok:boolean;why?:string}{
  const u=s.users.find(x=>x.id===uid);
  if(!u||!CB_CHECKER_ROLES.includes(u.role))return {ok:false,why:'Locking needs a Manager, Accountant or Owner.'};
  // F7: a lock needs the closing record (a closed date without one is not shown in the cash book, so nobody would see the lock).
  if(!s.closings?.[d])return {ok:false,why:'Close the business day first.'};
  if(cbSlice(s).locks[d])return {ok:false,why:'Already locked.'};
  if(cbCounters(s,d).includes(uid))return {ok:false,why:'A different person from the one who counted must lock the day.'};
  if(cbCounterUnknown(s,d)&&u.role!=='Owner')return {ok:false,why:'This day was saved without the name of who counted: only the Owner (a different person from the one who closed it) may lock it.'};
  return {ok:true};
}
export const cbCanReopen=(s:State,uid:string,d:string)=>{const u=s.users.find(x=>x.id===uid);return !!u&&CB_REOPEN_ROLES.includes(u.role)&&!!cbSlice(s).locks[d];};

const csvCell=(v:string|number)=>'"'+String(v).split('"').join('""')+'"';
const rupees=(p:number)=>(p/100).toFixed(2);
/** CSV for a range: a day-by-day summary, then every entry line with the running balance. Money in rupees, 2 decimals. */
export function cbCsv(s:State,from:string,to:string){
  const r=cbRange(s,from,to);
  const rows:(string|number)[][]=[['Hotel Control Centre — DEMONSTRATION · fictional data · Cash book'],['Property',s.settings.hotel],['Business dates',from,to],['Generated (simulated clock)',s.clock],[],
    ['Date','Status','Opening','Receipts','Payments (incl. banking)','Closing (book)','Expected','Counted','Difference','Locked by'],
    ...r.days.map(d=>[d.date,d.status+(d.locked?' · locked':''),rupees(d.opening),rupees(d.receipts),rupees(d.payments),rupees(d.closing),d.expected===undefined?'':rupees(d.expected),d.counted===undefined?'':rupees(d.counted),d.difference===undefined?'':rupees(d.difference),d.locked?.lockedBy??''])];
  rows.push([],['Date','Reference','Entry','Detail','Booking','Guest','Receipt','Payment','Balance','By']);
  for(const d of r.days){
    let bal=d.opening;rows.push([d.date,'','Opening balance','','','','','',rupees(bal),'']);
    for(const e of d.entries){bal+=e.in-e.out;rows.push([d.date,e.id,e.label,e.reference,e.booking??'',e.guest??'',e.in?rupees(e.in):'',e.out?rupees(e.out):'',rupees(bal),e.actor]);}
    rows.push([d.date,'','Closing balance','','','',rupees(d.receipts),rupees(d.payments),rupees(d.closing),'']);
  }
  return rows.map(x=>x.map(csvCell).join(',')).join('\r\n');
}
export const cbPrev=(d:string)=>addDays(d,-1);
