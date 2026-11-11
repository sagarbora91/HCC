// Stream F · Cashier, closing & reports — pure domain helpers and selectors (re-exported by services/mock/index.ts for UI and tests).
// Every figure is derived from source records (REQ-RPT-003): lines by service date, receipts by receipt date,
// expenses by business date, occupancy from stay intervals. Money is integer paise.
import {csvQuoted} from '../csv';
import {bookingRef} from '../bookingRef';
import type {State,Booking,Expense,Receipt,Line} from '../types';
import {money,addDays} from '../money';
import {drawer,blockedDuring,totals,hallDrawerCash,lfCounterCash} from '../selectors';
import {posSlice,posOpenOrders,posOrderTotals,posSpot} from './pos';
import {lineTax} from '../gst';
import {mnyIsDrawer,mnyIsCredit,mnySettlesTo,mnyNames} from './money-master';
import {cxDeskMovements,cxPendingTransfers} from './cashout';
import {expUdDeskMovements} from './udhaar-desk';
import {expVendorPayCash} from './expenses-vendor'; // CU-TRN-FIX F14

// ── Limits (RBAC Matrix §6, demonstration defaults) ──
/** Manager may accept a cash difference up to ₹200; above → Owner (PERM-SHF-002). */
export const SHIFT_MANAGER_DIFF_LIMIT=20000;
/** Manager may approve expenses up to ₹5,000; above → Owner (PERM-EXP-002). */
export const EXPENSE_MANAGER_LIMIT=500000;
/** Roles whose approval limits are unlimited for cash differences and expenses. */
export const closingUnlimitedRoles=['Owner','Accountant'];
/** Note denominations for the cash count grid, in rupees. Coins are entered as a rupee total. */
export const SHIFT_DENOMINATIONS=[500,200,100,50,20,10];
/** LEGACY (MA-3): the funding sources, categories and tenders now come from the money masters (domain/streams/money-master.ts:
 *  mnyNames(s,'expenses'), mnyCategoryNames(s), mnyNames(s,'frontdesk')). These constants are only the seed values, kept for old imports. */
export const EXPENSE_METHODS=['Cash','UPI','Bank transfer','Vendor credit'];
/** LEGACY (MA-3) — seed of the expenseCategories master; read mnyCategoryNames(s). */
export const EXPENSE_CATEGORIES=['Supplies','Laundry','Maintenance','Utilities','Petty cash'];
/** LEGACY (MA-3) — seed of the front-desk payment methods; read mnyNames(s,'frontdesk'). */
export const TENDERS=['Cash','UPI','Card','Bank transfer'];

// ── Records (State · stream F fields refer to these) ──
export type ShiftRecord={date:string;status:'Submitted'|'Closed';expected:number;counted:number;reason?:string;submittedBy:string;
  approval?:string;decidedBy?:string;denominations?:Record<string,number>};
export type CloseProgress={date:string;posted:string[];total:number;interrupted:boolean;runs:number};
export type ClosingRecord={
  date:string;closedAt:string;closedBy:string;
  revenue:{room:number;service:number;discounts:number;adjustments:number;net:number;tax:number};
  receipts:{byTender:Record<string,number>;net:number;companyCredit:number};
  deposits:{received:number;returned:number;heldAtClose:number};
  expenses:{cash:number;nonCash:number;vendorCredit:number;total:number};
  occupancy:{sellable:number;occupied:number;occupancy:string;roomRevenue:number;adr:string;revpar:string};
  cash:{opening:number;expected:number;counted:number;difference:number;reason?:string;approvedBy?:string;
    /** Who counted the drawer (shift submitter / SHIFT_CLOSE user). Absent in saves made before the W05 review fix (F1): such days are Owner-lock only. */
    countedBy?:string;
    /** Cash taken out of the drawer after the count (bank drop / owner handover) so the next opening float is restored. FY26 history only. */
    banked?:number;
    /** Restaurant / room-service cash (net of same-day voids) inside expected cash (shared till, DEC-052). */
    restaurant?:number};
  /** Outlet (POS) receipts by method, net of same-day voids; Room folio = room-service charged to a stay (a charge, not a receipt). */
  outlet?:{byMethod:Record<string,number>;net:number;roomFolio:number;bills:number};
  nightAudit:{stays:number;resumed:boolean};
  exceptions:string[];
  /** DEC-077: every Owner reopen of this date, oldest first (the open one has no reclosedAt). */
  reopens?:ClosingReopen[];
  /** DEC-077: the earlier closing records of this date that a re-close replaced (kept unchanged as history, oldest first). */
  replaced?:ClosingRecord[];
};
/** DEC-077 (resolves DEC-028): one reopen of a closed business day by the Owner, with a reason; open while reclosedAt is unset. */
export type ClosingReopen={id:string;date:string;by:string;at:string;reason:string;
  /** The day's DEC-065 cash-book lock when reopened (a reopen never unlocks it). */cashBookLocked:boolean;
  /** The day had been exported to Tally: its vouchers were flagged for re-export. */tallyReexport:boolean;
  reclosedBy?:string;reclosedAt?:string};

// ── Cash shift (SCR-11, WF-SHIFT-001) ──
export type ShiftStatus='Open'|'Submitted'|'Closed';
export function shiftStatus(s:State):ShiftStatus{
  if(s.shiftOpen)return 'Open';
  return s.shift?.date===s.date&&s.shift.status==='Submitted'?'Submitted':'Closed';
}
export type CashMovement={id:string;kind:string;label:string;booking?:string;guest?:string;reference:string;amount:number;actor:string;
  /** POS check behind a restaurant / room-service movement. */ order?:string};
/** CU-TRN-FIX F14: drawer cash that drawer(s) counts but no receipt / expense record carries - vendor payments (out), counter bills and hall receipts.
 *  One signed movement each (only when not zero) so the page, the cash book and the close command all agree to the paise. */
const otherDrawerMovements=(s:State,d:string):CashMovement[]=>{
  const out:CashMovement[]=[],vp=expVendorPayCash(s,d),cb=lfCounterCash(s,d),hl=hallDrawerCash(s,d);
  if(vp)out.push({id:'vendor-pay:'+d,kind:'vendor-payment',label:'Vendor payments in cash',reference:'Vendor payments',amount:-vp,actor:'system'});
  if(cb)out.push({id:'counter:'+d,kind:'counter-bill',label:'Counter bills in cash',reference:'Counter billing',amount:cb,actor:'system'});
  if(hl)out.push({id:'hall:'+d,kind:'hall-receipt',label:'Hall advances and balances in cash',reference:'Hall booking',amount:hl,actor:'system'});
  return out;};
/** Today's cash movements, signed (+ into the drawer, − out). Sum + opening float = drawer(s).
 *  Shared till (DEC-052): POS cash sales and same-day voids count once here; UPI/card bills and room charges to a folio do not. */
export function shiftMovements(s:State):CashMovement[]{
  const rec=s.bookings.flatMap(b=>b.receipts.filter(r=>r.date===s.date&&mnyIsDrawer(s,r.method)).map(r=>recMovement(b,r)));
  const exp=s.expenses.filter(e=>e.date===s.date&&mnyIsDrawer(s,e.method)&&expenseCounts(e)).map(expMovement);
  const p=posSlice(s);
  const pos=p.receipts.filter(r=>r.date===s.date&&mnyIsDrawer(s,r.method)).map(r=>posMovement(p,r,p.orders.find(x=>x.id===r.order)));
  return [...rec,...expUdDeskMovements(s,s.date),...pos,...exp,...cxDeskMovements(s,s.date),...otherDrawerMovements(s,s.date)]; // DEC-073: bank deposits, till transfers, Owner drawings; DEC-130: Udhaar cash
}
const recMovement=(b:Booking,r:Receipt):CashMovement=>({id:r.id,kind:r.kind,booking:b.id,guest:b.guest,
  label:receiptLabel(r.kind),reference:r.reference,amount:['payment','deposit'].includes(r.kind)?r.amount:-r.amount,actor:r.actor});
/** DEC-120: the vendor is optional — the label then names the description. */
const expMovement=(e:Expense):CashMovement=>({id:e.id,kind:e.reversalOf?'expense-reversal':'expense',
  label:(e.reversalOf?'Expense reversal · ':'Cash expense · ')+(e.vendor||e.description||'—'),reference:e.reversalOf?'Reverses '+e.reversalOf:e.category,amount:-e.amount,actor:e.actor});
const posMovement=(p:ReturnType<typeof posSlice>,r:ReturnType<typeof posSlice>['receipts'][number],o:ReturnType<typeof posSlice>['orders'][number]|undefined):CashMovement=>{
  const outlet=p.outlets.find(x=>x.id===r.outlet)?.name??r.outlet,bill=o?.settlement?.billNo??r.order;
  return {id:r.id,kind:r.kind==='void'?'pos-void':'pos-sale',label:`${outlet}${r.kind==='void'?' void':''} · ${bill}`,guest:o?.guest,order:r.order,
    reference:o?posSpot(o)+' · '+r.reference:r.reference,amount:r.amount,actor:r.actor};};
/** shiftMovements for many business dates in one pass over receipts / expenses (a range used to rescan every record once per day).
 *  Returns the movements of any date exactly as shiftMovements({...s,date}) would. Built per call, so it can never go stale. */
export function shiftMovementsByDate(s:State,from:string,to:string):(d:string)=>CashMovement[]{
  const rec=new Map<string,CashMovement[]>(),exp=new Map<string,CashMovement[]>(),pos=new Map<string,CashMovement[]>();
  const add=(m:Map<string,CashMovement[]>,d:string,x:CashMovement)=>{const l=m.get(d);if(l)l.push(x);else m.set(d,[x]);};
  const inR=(d:string)=>d>=from&&d<=to;
  for(const b of s.bookings)for(const r of b.receipts)if(mnyIsDrawer(s,r.method)&&inR(r.date))add(rec,r.date,recMovement(b,r));
  for(const e of s.expenses)if(mnyIsDrawer(s,e.method)&&inR(e.date)&&expenseCounts(e))add(exp,e.date,expMovement(e));
  const p=posSlice(s),orders=new Map<string,(typeof p.orders)[number]>();
  for(const o of p.orders)if(!orders.has(o.id))orders.set(o.id,o);
  for(const r of p.receipts)if(mnyIsDrawer(s,r.method)&&inR(r.date))add(pos,r.date,posMovement(p,r,orders.get(r.order)));
  return d=>[...(rec.get(d)??[]),...expUdDeskMovements(s,d),...(pos.get(d)??[]),...(exp.get(d)??[]),...cxDeskMovements(s,d),...otherDrawerMovements(s,d)];
}
export function receiptLabel(kind:string){return kind==='payment'?'Payment / advance':kind==='deposit'?'Refundable deposit':kind==='refund'?'Payment refund':kind==='deposit-return'?'Deposit returned':kind==='company-credit'?'Company credit':kind;}
/** AC-03: every cash-in component (receipts, deposits, shared-till restaurant / room-service cash, other) so opening + in − out = expected. */
export const shiftCashIn=(x:{payments:number;deposits:number;restaurant:number;other:number})=>x.payments+x.deposits+x.restaurant+x.other;
/** The expected-cash formula with its parts (Financial Rulebook §4). parts.expected always equals drawer(s). */
export function shiftExpected(s:State){
  const mv=shiftMovements(s),sum=(k:string[])=>mv.filter(m=>k.includes(m.kind)).reduce((a,m)=>a+m.amount,0);
  const neg=(n:number)=>0-n||0; // avoid −0 ("-₹0" on screen)
  const parts={opening:s.openingCash,payments:sum(['payment']),deposits:sum(['deposit']),refunds:neg(sum(['refund'])),returns:neg(sum(['deposit-return'])),
    expenses:neg(sum(['expense','expense-reversal'])),other:sum(['company-credit','counter-bill','hall-receipt']),restaurant:sum(['pos-sale','pos-void']),
    /** DEC-073: bank deposits, till transfers and Owner drawings, signed (usually negative: cash out of the drawer). */
    transfers:sum(['banking','transfer-out','transfer-in','owner-drawing','staff-advance','vendor-payment'])}; // ACC-2: a cash salary advance leaves the drawer
  const expected=parts.opening+parts.payments+parts.deposits+parts.restaurant+parts.other+parts.transfers-parts.refunds-parts.returns-parts.expenses;
  return {...parts,expected,consistent:expected===drawer(s)};
}
/** Denomination grid → paise. counts[note]=number of notes; coins = rupee amount. */
export function shiftDenominationTotal(d:Record<string,number>){
  return SHIFT_DENOMINATIONS.reduce((a,n)=>a+(d[n]||0)*n*100,0)+Math.round((d.coins||0)*100);
}

// ── Expenses (SCR-13, WF-EXP-001) ──
/** Posted and later-reversed expenses both happened on their date; the reversal record (negative) offsets on its own date. */
export const expenseCounts=(e:Expense)=>e.status==='Posted'||e.status==='Reversed';
/** DEC-120 · SCC duplicate key (expense.js addEntry 526): same date + same amount + same description (case-insensitive), excluding rejected
 *  and void items and reversal records. SCC asks "Save anyway?" (EXPENSE refuses without confirmDuplicate). Records without a description
 *  (saved before DEC-120) never match. */
export function expenseDuplicates(s:State,description:string,amount:number,date:string,except=''):Expense[]{
  const d=String(description??'').trim().toLowerCase();if(!d||!amount)return [];
  return s.expenses.filter(e=>e.id!==except&&!e.reversalOf&&e.status!=='Rejected'&&e.status!=='Void'&&String(e.description??'').trim().toLowerCase()===d&&e.amount===amount&&e.date===date);
}

// ── Day closing (SCR-14, WF-DAY-001) ──
const holdUntil=(b:Booking)=>(b as Booking&{holdUntil?:string}).holdUntil;
/** In-house stays that need tonight's room line, in a stable order (booking ID). */
export function closingNightTargets(s:State):Booking[]{
  return s.bookings.filter(x=>x.status==='In house'&&x.arrival<=s.date&&x.departure>s.date).sort((a,b)=>a.id<b.id?-1:a.id>b.id?1:0);
}
export const closingHasNight=(b:Booking,date:string)=>b.lines.some(l=>l.kind==='room'&&l.date===date);
export type PreflightItem={label:string;booking?:string;page?:string};
export type PreflightIssue={id:string;title:string;hint:string;page?:string;items:PreflightItem[]};
/** Pending approvals that hold up the day close. Role-template (policy) and Master configuration requests do not (DEC-113, as on the
 *  server): they change no money of the day and wait for the Owner on their own. Used by the preflight and by DAY_CLOSE. */
export const closingBlockingApprovals=(s:State)=>s.approvals.filter(a=>a.status==='Pending'&&a.kind!=='policy'&&a.kind!=='config');
/** All preflight checks in close order; a check with items is blocking (REQ-CLOSE-002). */
export function closingPreflight(s:State):PreflightIssue[]{
  const pendingApprovals=closingBlockingApprovals(s);
  const st=shiftStatus(s),reopened=closingReopened(s);
  return [
    // DEC-077: a reopened earlier day blocks today's close until it is closed again (same check as the server's "reopened").
    {id:'reopened',title:'Reopened day closed again',hint:'Close the reopened day again (Day closing) before closing today.',page:'closing',
      items:reopened?[{label:`${reopened.date} reopened by the Owner · close it again`,page:'closing'}]:[]},
    {id:'arrivals',title:'Arrivals resolved',hint:'Check in, or cancel after refunding advances.',page:'reservations',
      items:s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date).map(b=>({label:`${b.guest} · room ${b.room} · ${bookingRef(b)}`,booking:b.id}))},
    {id:'departures',title:'Overdue departures resolved',hint:'Settle and check out, or extend the stay.',page:'folios',
      items:s.bookings.filter(b=>b.status==='In house'&&b.departure<=s.date).map(b=>({label:`${b.guest} · room ${b.room} · due ${b.departure}`,booking:b.id}))},
    {id:'holds',title:'Expired holds released',hint:'Release or confirm holds whose expiry has passed.',page:'reservations',
      items:s.bookings.filter(b=>b.status==='Held'&&!!holdUntil(b)&&holdUntil(b)!<s.clock).map(b=>({label:`${b.guest} · room ${b.room} · hold expired ${holdUntil(b)!.replace('T',' ')}`,booking:b.id}))},
    {id:'nights',title:'Room-night charges posted',hint:'Post tonight’s room line for every in-house stay (step 2).',
      items:s.bookings.filter(b=>b.status==='In house'&&!closingHasNight(b,s.date)).map(b=>({label:`${b.guest} · room ${b.room} · ${money(b.rate)}`,booking:b.id}))},
    {id:'approvals',title:'Approvals resolved',hint:'Approve or reject pending requests.',page:'expenses',
      items:[...pendingApprovals.map(a=>({label:`${approvalKindLabel(a.kind)} · ${money(a.amount)} · ${a.id}`,page:a.kind==='cash-difference'?'cashier':'expenses'})),
        // DEC-073: a pending till transfer holds up the close (a bank deposit awaiting confirmation does not).
        ...cxPendingTransfers(s).map(r=>({label:`Till transfer awaiting the receiver · ${money(r.amount)} · ${r.id}`,page:'cashier'}))]},
    {id:'expenses',title:'Expenses resolved',hint:'Approve or reject pending expenses.',page:'expenses',
      items:s.expenses.filter(e=>e.status==='Pending').map(e=>({label:`${e.vendor||e.description||'—'} · ${money(e.amount)} · ${e.id}`,page:'expenses'}))},
    {id:'pos',title:'Restaurant checks settled',hint:'Settle each open check, charge it to the room, or void it (with approval above the limit).',page:'pos',
      items:posOpenOrders(s).map(o=>({label:`${posSpot(o)} · ${o.id}${o.guest?' · '+o.guest:''} · ${money(posOrderTotals(s,o).total)} open`,page:'pos'}))},
    {id:'shift',title:'Cash shift reconciled',hint:st==='Submitted'?'The count awaits difference approval.':'Count the drawer and submit the shift.',page:'cashier',
      items:st==='Closed'?[]:[{label:st==='Submitted'?'Count submitted · difference awaiting approval':'Cash shift open · '+money(drawer(s))+' expected',page:'cashier'}]},
  ];
}
export function approvalKindLabel(kind?:string){return !kind?'Discount':kind==='cash-difference'?'Cash difference':kind==='closed-date-correction'?'Closed-date correction':kind[0].toUpperCase()+kind.slice(1);}
/** The figures of a closing record derived from the source records of date d (shared by the close and the DEC-077 re-close). */
function closingFigures(s:State,d:string){
  const p=reportPack(s,d,d),v=(tab:string,id:string)=>p.tabs.find(t=>t.id===tab)!.metrics.find(m=>m.id===id)?.value??0;
  const byTender:Record<string,number>={};for(const t of reportTenders(s,d,d))byTender[t]=v('receipts','tender:'+t);
  const occ=reportOccupancyRange(s,d,d);
  return {
    revenue:{room:v('revenue','room'),service:v('revenue','service'),discounts:v('revenue','discount'),adjustments:v('revenue','adjustment'),net:v('revenue','net'),tax:v('revenue','tax')},
    receipts:{byTender,net:v('receipts','net'),companyCredit:v('receipts','company-credit')},
    received:v('receipts','deposit-in'),returned:0-v('receipts','deposit-out')||0,
    expenses:{cash:v('expenses','cash'),nonCash:v('expenses','noncash'),vendorCredit:v('expenses','vendor'),total:v('expenses','total')},
    occupancy:{sellable:occ.sellable,occupied:occ.occupied,occupancy:reportPct(occ.occupied,occ.sellable),roomRevenue:occ.roomRevenue,adr:reportRatioMoney(occ.roomRevenue,occ.occupied),revpar:reportRatioMoney(occ.roomRevenue,occ.sellable)}};
}
/** Exceptions of date d that come from its records (closed-date corrections, expense reversals and rejections). */
function closingRecordExceptions(s:State,d:string,reversals=false):string[]{
  const out:string[]=[];
  const corrections=s.bookings.flatMap(b=>b.lines.filter(l=>l.date===d&&l.kind==='adjustment'&&(l as Line&{correctionOf?:string}).correctionOf).map(l=>({b,l})));
  corrections.forEach(({b,l})=>out.push(`Correction ${money(l.amount)} on ${bookingRef(b)} for closed date ${(l as Line&{correctionOf?:string}).correctionOf}`));
  // DEC-077 re-close: reversals posted on the reopened day are listed as well (the server lists every correction line of the day).
  if(reversals)s.bookings.forEach(b=>b.lines.filter(l=>l.date===d&&l.reversalOf).forEach(l=>out.push(`Reversal ${money(l.amount)} on ${bookingRef(b)} · ${l.label}`)));
  s.expenses.filter(e=>e.reversalOf&&e.date===d).forEach(e=>out.push(`Expense ${e.reversalOf} reversed (${money(e.amount)})`));
  s.expenses.filter(e=>e.status==='Rejected'&&e.date===d).forEach(e=>out.push(`Expense ${e.id} rejected${e.reason?' · '+e.reason:''}`));
  return out;
}
/** Closing record for the current business date (snapshot taken at DAY_CLOSE). */
export function closingSummary(s:State,closedBy=''):ClosingRecord{
  const d=s.date,f=closingFigures(s,d),ex=shiftExpected(s);
  const counted=s.shiftClose?.counted??ex.expected,expected=s.shiftClose?.expected??ex.expected;
  const sh=s.shift?.date===d?s.shift:undefined,countedBy=sh?.submittedBy??s.shiftClose?.countedBy;
  const exceptions:string[]=[];
  if(counted!==expected)exceptions.push(`Cash difference ${money(counted-expected)} accepted${sh?.decidedBy?' by '+userName(s,sh.decidedBy):''}${sh?.reason?' · '+sh.reason:''}`);
  if(s.closeProgress?.date===d&&s.closeProgress.runs>1)exceptions.push(`Night audit resumed after an interruption (${s.closeProgress.runs} runs; no night posted twice)`);
  exceptions.push(...closingRecordExceptions(s,d));
  return {date:d,closedAt:s.clock,closedBy,
    revenue:f.revenue,
    receipts:f.receipts,
    deposits:{received:f.received,returned:f.returned,heldAtClose:s.bookings.reduce((a,b)=>a+totals(b).deposit,0)},
    expenses:f.expenses,
    occupancy:f.occupancy,
    cash:{opening:s.openingCash,expected,counted,difference:counted-expected,reason:counted!==expected?sh?.reason:undefined,approvedBy:counted!==expected?sh?.decidedBy:undefined,...(countedBy?{countedBy}:{}),restaurant:ex.restaurant},
    outlet:closingOutlet(s,d),
    nightAudit:{stays:closingNightTargets(s).length,resumed:!!(s.closeProgress?.date===d&&s.closeProgress.runs>1)},
    exceptions};
}
const userName=(s:State,id:string)=>s.users.find(u=>u.id===id)?.name??id;

// ── DEC-077: the Owner reopens a closed business day (resolves DEC-028; mirrors Hcc.Domain.Closing.ReopenRules) ──
// Owner only, with a reason; only a closed day before the business date that has a closing record, never one locked in Accounting, one
// reopened day at a time. While reopened, a correction of a line dated that day and a missed charge post ON it (open folios only: an
// invoice or credit note never changes); receipts and cash stay on the open day; the DEC-065 cash-book lock is not touched. Today cannot
// be closed until the reopened day is closed again (preflight 'reopened'); closing it again writes a new record and keeps the old one.
const reopenedOf=new WeakMap<object,ClosingReopen|null>(); // per closings object (replaced, never edited, when a reopen changes)
/** The open reopen (the earlier day the Owner reopened and nobody has closed again yet), if any. */
export function closingReopened(s:State):ClosingReopen|undefined{
  const all=s.closings;if(!all)return undefined;
  let found=reopenedOf.get(all);
  if(found===undefined){found=null;for(const c of Object.values(all))for(const r of c.reopens??[])if(!r.reclosedAt)found=r;reopenedOf.set(all,found);}
  return found??undefined;
}
/** Why `date` cannot be reopened now ('' = it can). `accountingLocked` = the date is locked in Accounting. The role is checked separately. */
export function closingReopenRefusal(s:State,date:string,accountingLocked:boolean):string{
  const open=closingReopened(s);
  if(open?.date===date)return `Business day ${date} is already reopened.`;
  if(!s.closedDates.includes(date))return `Business day ${date} is not closed: corrections post in the open day as usual.`;
  if(date>=s.date)return `Finish closing ${date} first: the next business day is not open yet.`;
  if(open)return `Business day ${open.date} is already reopened. Close it again before reopening another day.`;
  if(accountingLocked)return `Business day ${date} is locked in Accounting. The Owner unlocks that period first (with a reason).`;
  if(!s.closings?.[date])return `Business day ${date} has no closing record in this demo (it was closed before records were kept), so it cannot be reopened here.`;
  return '';
}
/** What the reopen tells the Owner: the cash-book lock and the Tally re-export spelled out (server ReopenRules.Message). */
export function closingReopenMessage(date:string,cashBookLocked:boolean,tallyReexport:boolean):string{
  return `Business day ${date} reopened. Corrections of lines dated ${date} and missed charges now post on ${date}; issued invoice and credit-note numbers and their GST never change (a checked-out stay is corrected with a credit note). Close ${date} again before closing today.`
    +(cashBookLocked?` The cash book of ${date} stays locked (DEC-065): reopening the day does not unlock it, so a correction touching ${date} is refused until the Owner reopens that cash-book day.`
      :` Reopening the day does not change cash-book locks (DEC-065); the cash book of ${date} is not locked.`)
    +(tallyReexport?` ${date} was exported to Tally: its vouchers are marked changed and the next Tally export carries them again.`:'');
}
/** Pending correction requests of lines dated `date` (closing the reopened day again waits for them: an approval would post on that day). */
export function closingPendingCorrections(s:State,date:string){
  return s.approvals.filter(a=>a.status==='Pending'&&a.kind==='correction'&&s.bookings.some(b=>b.id===a.booking&&b.lines.some(l=>l.id===a.target&&l.date===date)));
}
/** The exception line a closing record carries for one reopen of its day. */
export function closingReopenNote(s:State,r:ClosingReopen):string{
  return `Reopened ${r.at.replace('T',' ')} by ${userName(s,r.by)} (DEC-077): ${r.reason}`+(r.reclosedAt?` · closed again ${r.reclosedAt.replace('T',' ')} by ${userName(s,r.reclosedBy??'')}`:'');
}
/**
 * The record a re-close writes for the reopened date d: every figure derived again from the day's source records; the point-in-time values
 * of the first close stay (cash count, deposits held at close, night audit). `reopens` = all reopens of d, the current one already closed.
 */
export function closingResummary(s:State,d:string,closedBy:string,prior:ClosingRecord,reopens:ClosingReopen[]):ClosingRecord{
  const f=closingFigures(s,d);
  const keep=prior.exceptions.filter(e=>!/^(Correction |Reversal |Expense |Reopened )/.test(e));
  return {date:d,closedAt:s.clock,closedBy,revenue:f.revenue,receipts:f.receipts,
    deposits:{received:f.received,returned:f.returned,heldAtClose:prior.deposits.heldAtClose},expenses:f.expenses,occupancy:f.occupancy,
    cash:prior.cash,outlet:closingOutlet(s,d),nightAudit:prior.nightAudit,
    exceptions:[...keep,...closingRecordExceptions(s,d,true),...reopens.map(r=>closingReopenNote(s,r))]};
}
/** Outlet receipts of a business date by method (sales − same-day voids) and room-service bills charged to folios. */
export function closingOutlet(s:State,d:string){
  const p=posSlice(s),byMethod:Record<string,number>=Object.fromEntries(mnyNames(s,'pos').map(m=>[m,0])); // MA-3: POS methods (seed Cash, UPI, Card)
  for(const r of p.receipts)if(r.date===d)byMethod[r.method]=(byMethod[r.method]??0)+r.amount;
  const folio=p.orders.filter(o=>o.settlement?.mode==='folio'&&o.settlement.date===d);
  return {byMethod,net:Object.values(byMethod).reduce((a,v)=>a+v,0),roomFolio:folio.reduce((a,o)=>a+o.settlement!.total,0),
    bills:p.orders.filter(o=>o.status==='Settled'&&o.settlement?.date===d).length};
}

// ── Reports (SCR-15, REQ-RPT-002..006) ──
export type DrillRow={date:string;ref:string;booking?:string;guest?:string;room?:string;detail:string;method?:string;kind?:string;amount:number};
export type ReportMetric={id:string;label:string;unit:'money'|'count';value:number;rows:DrillRow[];note?:string};
export type ReportRatio={id:string;label:string;display:string;formula:string;value?:ReportRatioValue};
/** A ratio as numerator ÷ denominator so exports can write a number (percent fraction or paise), not the display text (W05 review F9). */
export type ReportRatioValue={kind:'pct'|'money';num:number;den:number};
export type ReportTable={columns:string[];units:('text'|'money'|'count')[];rows:(string|number)[][];
  /** Text columns that are ratios of two numeric columns of the same row (e.g. Occupancy % = Occupied ÷ Sellable). */
  derived?:{col:number;kind:'pct'|'money';num:number;den:number}[]};
export type ReportTab={id:string;label:string;metrics:ReportMetric[];ratios:ReportRatio[];table?:ReportTable};
export type ReportPack={from:string;to:string;dates:string[];tabs:ReportTab[]};
export function reportDates(from:string,to:string){const out:string[]=[];for(let d=from;d<=to&&out.length<366;d=addDays(d,1))out.push(d);return out;}
const inRange=(d:string,from:string,to:string)=>d>=from&&d<=to;
export function reportLines(s:State,from:string,to:string){
  return s.bookings.flatMap(b=>b.lines.filter(l=>inRange(l.date,from,to)).map(l=>({b,l})));
}
export function reportReceipts(s:State,from:string,to:string){
  return s.bookings.flatMap(b=>b.receipts.filter(r=>inRange(r.date,from,to)).map(r=>({b,r})));
}
/** ACC-2 integration (contract §3.4, purchase DONE item 7): a capital purchase (fixed asset) is never an expense in the reports — it sits on the
 *  asset register; the drawer movement of a cash capital bill still counts (cash summaries above). A reversal record of one follows it. */
export function reportExpenses(s:State,from:string,to:string){const cap=expCapitalIds(s);return s.expenses.filter(e=>expenseCounts(e)&&inRange(e.date,from,to)&&!cap.has(e.id));}
/** Ids of capital purchases and their reversal records (ACC-2 integration). */
export function expCapitalIds(s:State):Set<string>{const cap=new Set(s.expenses.filter(e=>e.capital).map(e=>e.id));for(const e of s.expenses)if(e.reversalOf&&cap.has(e.reversalOf))cap.add(e.id);return cap;}
/** Stays occupying a room on date d (from the stay interval [arrival, departure)). */
export function reportStaysOn(s:State,d:string){return s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&b.arrival<=d&&d<b.departure);}
export function reportSellableOn(s:State,d:string){return s.rooms.filter(r=>!blockedDuring(r,d,addDays(d,1)));}
export function reportOccupancyRange(s:State,from:string,to:string){
  const dates=reportDates(from,to);
  const sellable=dates.reduce((a,d)=>a+reportSellableOn(s,d).length,0),occupied=dates.reduce((a,d)=>a+reportStaysOn(s,d).length,0);
  const roomRevenue=reportLines(s,from,to).filter(x=>x.l.kind==='room').reduce((a,x)=>a+x.l.amount,0);
  return {sellable,occupied,roomRevenue};
}
/** Occupancy % to 1 dp; '—' when the denominator is zero (report dictionary §6). Integer arithmetic, half-up. */
export function reportPct(num:number,den:number){if(!den)return '—';const tenths=Math.floor((num*1000+Math.floor(den/2))/den);return (tenths/10).toFixed(1)+'%';}
/** Money ratio (ADR, RevPAR) rounded half-up to the paisa; '—' when the denominator is zero. */
export function reportRatioMoney(num:number,den:number){if(!den)return '—';return money(Math.round(num/den));}
export function reportTenders(s:State,from:string,to:string){
  const found=reportReceipts(s,from,to).filter(x=>x.r.kind==='payment'||x.r.kind==='refund').map(x=>x.r.method);
  const tenders=mnyNames(s,'frontdesk');
  return [...tenders,...found.filter((m,i)=>!tenders.includes(m)&&found.indexOf(m)===i)];
}
const lineRow=(b:Booking,l:Line,amount:number):DrillRow=>({date:l.date,ref:l.id,booking:b.id,guest:b.guest,room:b.room,detail:l.label,kind:l.kind,amount});
const receiptRow=(b:Booking,r:Receipt):DrillRow=>({date:r.date,ref:r.id,booking:b.id,guest:b.guest,room:b.room,detail:receiptLabel(r.kind)+' · '+r.reference,method:r.method,kind:r.kind,
  amount:r.kind==='refund'||r.kind==='deposit-return'?-r.amount:r.amount});
const expenseRow=(e:Expense):DrillRow=>({date:e.date,ref:e.id,detail:`${e.vendor||e.description||'—'} · ${e.category}${e.reversalOf?' · reverses '+e.reversalOf:''}${e.status==='Reversed'?' · later reversed':''}`,method:e.method,kind:e.reversalOf?'reversal':'expense',amount:e.amount});
const metric=(id:string,label:string,rows:DrillRow[],unit:'money'|'count'='money',note?:string):ReportMetric=>({id,label,unit,rows,note,value:rows.reduce((a,r)=>a+r.amount,0)});
/** Per-date totals of drill rows in one pass (same figure as filtering the rows by date and adding them up). */
const sumsByDate=(rows:DrillRow[])=>{const m=new Map<string,number>();for(const r of rows)m.set(r.date,(m.get(r.date)??0)+r.amount);return m;};
const dayOffset=(from:string,d:string)=>Math.round((Date.parse(d+'T12:00:00Z')-Date.parse(from+'T12:00:00Z'))/86400000);

/** Every report tab for an inclusive business-date range. Each metric's value is exactly the sum of its drill-down rows. */
export function reportPack(s:State,from:string,to:string):ReportPack{
  const dates=reportDates(from,to);
  const lines=reportLines(s,from,to),rec=reportReceipts(s,from,to),exp=reportExpenses(s,from,to);
  // Rows and per-date sums are built once per selection (the day tables used to rebuild them for every date).
  const memoRows=<K,V>(f:(k:K)=>V)=>{const c=new Map<K,V>();return (k:K)=>{let v=c.get(k);if(v===undefined){v=f(k);c.set(k,v);}return v;};};
  // ACC-2 parity: F&B charged to a folio (kind 'food') stays in the report's service revenue, as on the server (a folio charge, not room revenue).
  const Lk=memoRows((k:string)=>lines.filter(x=>!k||x.l.kind===k||k==='service'&&x.l.kind==='food').map(x=>lineRow(x.b,x.l,x.l.amount)));
  const L=(k?:Line['kind'])=>Lk(k??'');
  const taxRows=lines.map(x=>lineRow(x.b,x.l,lineTax(x.l)));
  const Rk=memoRows((key:string)=>{const [kinds,method]=key.split('|');const ks=kinds.split(',');return rec.filter(x=>ks.includes(x.r.kind)&&(!method||x.r.method===method)).map(x=>receiptRow(x.b,x.r));});
  const R=(kinds:string[],method?:string)=>Rk(kinds.join(',')+'|'+(method??''));
  /** Drawer receipts (methods that settle to a till; seed: Cash), in record order. */
  const RD=(kinds:string[])=>rec.filter(x=>kinds.includes(x.r.kind)&&mnyIsDrawer(s,x.r.method)).map(x=>receiptRow(x.b,x.r));
  const sumsOf=memoRows((rows:DrillRow[])=>sumsByDate(rows));
  const sumBy=(rows:DrillRow[],d:string)=>sumsOf(rows).get(d)??0;
  // Shared till: POS receipts are separate source records; folio transfers create no receipt here.
  const posCash:DrillRow[]=posSlice(s).receipts.filter(r=>mnyIsDrawer(s,r.method)&&inRange(r.date,from,to)).map(r=>({
    date:r.date,ref:r.id,detail:`POS ${r.kind} · ${r.order} · ${r.reference}`,method:r.method,kind:'pos-'+r.kind,amount:r.amount}));
  // DEC-130: Udhaar payments taken in cash are desk receipts into the drawer (payments).
  const udCash:DrillRow[]=dates.flatMap(d=>expUdDeskMovements(s,d).map(m=>({date:d,ref:m.id,guest:m.guest,detail:m.label+' · '+m.reference,method:'Cash',kind:'udhaar-payment',amount:m.amount})));
  // Occupied room-nights: each stay is spread over its dates once (the old scan visited every booking for every date); order = date, then booking order.
  const stayBy:DrillRow[][]=dates.map(()=>[]);
  for(const b of s.bookings){
    if(b.status!=='In house'&&b.status!=='Departed')continue;
    for(let i=Math.max(0,dayOffset(from,b.arrival));i<dates.length&&i<dayOffset(from,b.departure);i++)
      stayBy[i].push({date:dates[i],ref:b.id+':'+dates[i],booking:b.id,guest:b.guest,room:b.room,detail:'Occupied room night · '+b.status,amount:1});
  }
  const stayRows=stayBy.flat();
  const sellRows=dates.flatMap(d=>reportSellableOn(s,d).map(r=>({date:d,ref:'room:'+r.id+':'+d,room:r.id,detail:`Sellable room ${r.id} · ${r.type}`,amount:1})));
  const arrRows=s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&inRange(b.arrival,from,to)).map(b=>({date:b.arrival,ref:b.id,booking:b.id,guest:b.guest,room:b.room,detail:'Arrival · '+b.source,amount:1}));
  const depRows=s.bookings.filter(b=>b.status==='Departed'&&inRange(b.departure,from,to)).map(b=>({date:b.departure,ref:b.id,booking:b.id,guest:b.guest,room:b.room,detail:'Departure (checked out)',amount:1}));
  const E=(f:(e:Expense)=>boolean)=>exp.filter(f).map(expenseRow);
  const cashExp=E(e=>mnyIsDrawer(s,e.method));
  // MA-3: split by "settles to" — till (drawer cash), a bank account (UPI / bank / card), none (credit). Seed: the old Cash / UPI+Bank / Vendor credit split.
  const bankExp=(e:Expense)=>mnySettlesTo(s,e.method)?.kind==='bank',creditExp=(e:Expense)=>mnyIsCredit(s,e.method);
  const net=metric('net','Net revenue (excl. tax)',L()),pay=metric('net','Net receipts (payments − refunds)',R(['payment','refund']));
  const expTotal=metric('total','Posted expenses (net of reversals)',E(()=>true));
  const occ={sellable:metric('sellable','Sellable room-nights',sellRows,'count'),occupied:metric('occupied','Occupied room-nights',stayRows,'count'),room:metric('room-revenue','Room revenue',L('room'))};
  const ratios:ReportRatio[]=[
    {id:'occupancy',label:'Occupancy %',display:reportPct(occ.occupied.value,occ.sellable.value),formula:`${occ.occupied.value} occupied ÷ ${occ.sellable.value} sellable × 100, 1 dp`,value:{kind:'pct',num:occ.occupied.value,den:occ.sellable.value}},
    {id:'adr',label:'ADR',display:reportRatioMoney(occ.room.value,occ.occupied.value),formula:`${money(occ.room.value)} room revenue ÷ ${occ.occupied.value} occupied room-nights`,value:{kind:'money',num:occ.room.value,den:occ.occupied.value}},
    {id:'revpar',label:'RevPAR',display:reportRatioMoney(occ.room.value,occ.sellable.value),formula:`${money(occ.room.value)} room revenue ÷ ${occ.sellable.value} sellable room-nights`,value:{kind:'money',num:occ.room.value,den:occ.sellable.value}}];
  const closingCash=(d:string)=>{const c=s.closings?.[d];if(c)return [c.cash.opening,c.cash.expected,c.cash.counted,c.cash.difference,'Closed'];
    if(d===s.date){const x=shiftExpected(s);return [s.openingCash,x.expected,s.shiftClose?.counted??'',s.shiftClose?s.shiftClose.counted-s.shiftClose.expected:'',s.closedDates.includes(d)?'Closed':'Open'];}
    return ['','','','',s.closedDates.includes(d)?'Closed (no record)':'—'];};
  const tenders=reportTenders(s,from,to);
  return {from,to,dates,tabs:[
    {id:'daily',label:'Daily operations',ratios:[ratios[0]],metrics:[metric('arrivals','Arrivals checked in',arrRows,'count'),metric('departures','Departures checked out',depRows,'count'),
      {...occ.occupied},{...net,id:'net-revenue'},{...pay,id:'net-receipts'},{...expTotal,id:'expenses'}],
      table:{columns:['Business date','Day status','Arrivals','Departures','Occupied','Net revenue','Net receipts','Expenses'],units:['text','text','count','count','count','money','money','money'],
        rows:dates.map(d=>[d,s.closedDates.includes(d)?'Closed':d===s.date?'Open':d>s.date?'Future':'—',sumBy(arrRows,d),sumBy(depRows,d),sumBy(stayRows,d),sumBy(net.rows,d),sumBy(pay.rows,d),sumBy(expTotal.rows,d)])}},
    {id:'revenue',label:'Revenue & tax',ratios:[],metrics:[metric('room','Room revenue',L('room')),metric('service','Service revenue',L('service')),metric('discount','Discounts',L('discount')),
      metric('adjustment','Adjustments & corrections',L('adjustment')),net,metric('tax','GST (CGST + SGST)',taxRows,'money','Tax is a liability, not revenue · GST master, illustrative — CA to confirm'),
      metric('gross','Charges incl. tax',[...L(),...taxRows.map(r=>({...r,ref:r.ref+':tax',detail:'Tax · '+r.detail}))])],
      table:{columns:['Business date','Room','Service','Discounts','Adjustments','Net revenue','Tax'],units:['text','money','money','money','money','money','money'],
        rows:dates.map(d=>[d,sumBy(L('room'),d),sumBy(L('service'),d),sumBy(L('discount'),d),sumBy(L('adjustment'),d),sumBy(net.rows,d),sumBy(taxRows,d)])}},
    {id:'receipts',label:'Receipts & deposits',ratios:[],metrics:[...tenders.map(t=>metric('tender:'+t,t+' (payments − refunds)',R(['payment','refund'],t))),pay,
      metric('deposit-in','Deposits received',R(['deposit'])),metric('deposit-out','Deposits returned',R(['deposit-return'])),
      metric('company-credit','Company credit (city ledger)',R(['company-credit'])),
      metric('held','Deposits held now · all open folios',s.bookings.filter(b=>totals(b).deposit!==0).map(b=>({date:s.date,ref:b.id,booking:b.id,guest:b.guest,room:b.room,detail:'Deposit balance · '+b.status,amount:totals(b).deposit})),'money','Point-in-time liability; not filtered by date')],
      table:{columns:['Receipt date',...tenders,'Net receipts','Deposits in','Deposits out'],units:['text',...tenders.map(()=>'money' as const),'money','money','money'],
        rows:dates.map(d=>[d,...tenders.map(t=>sumBy(R(['payment','refund'],t),d)),sumBy(pay.rows,d),sumBy(R(['deposit']),d),sumBy(R(['deposit-return']),d)])}},
    {id:'expenses',label:'Expenses & cash',ratios:[],metrics:[metric('cash','Drawer-cash expenses',cashExp),metric('noncash','UPI / bank expenses',E(bankExp)),
      metric('vendor','Vendor-credit expenses (no drawer effect)',E(creditExp)),metric('reversals','Reversals included above',E(e=>!!e.reversalOf)),expTotal,
      metric('cash-in','Cash into drawer (payments + deposits + POS sales)',[...RD(['payment','deposit']),...udCash,...posCash.filter(r=>r.kind==='pos-sale')]),
      metric('cash-out','Cash out of drawer (refunds + returns + POS voids)',[...RD(['refund','deposit-return']),...posCash.filter(r=>r.kind==='pos-void')])],
      table:{columns:['Business date','Day','Opening float','Expected cash','Counted','Difference','Cash expenses'],units:['text','text','money','money','money','money','money'],
        rows:dates.map(d=>{const c=closingCash(d);return [d,c[4],c[0],c[1],c[2],c[3],sumBy(cashExp,d)];})}},
    {id:'occupancy',label:'Occupancy',ratios,metrics:[occ.sellable,occ.occupied,occ.room],
      table:{columns:['Business date','Sellable','Occupied','Occupancy %','Room revenue','ADR','RevPAR'],units:['text','count','count','text','money','text','text'],derived:[{col:3,kind:'pct',num:2,den:1},{col:5,kind:'money',num:4,den:2},{col:6,kind:'money',num:4,den:1}],
        rows:dates.map(d=>{const sl=sumBy(sellRows,d),oc=sumBy(stayRows,d),rr=sumBy(occ.room.rows,d);return [d,sl,oc,reportPct(oc,sl),rr,reportRatioMoney(rr,oc),reportRatioMoney(rr,sl)];})}},
  ]};
}
/** Report dictionary (Financial Rulebook §6, REQ-RPT-006). */
export const reportDictionary:[string,string,string][]=[
  ['Sellable rooms','Rooms not blocked on the business date (sellable room-nights over a range)','—'],
  ['Occupied rooms','Stays in house (or since departed) whose stay interval [arrival, departure) covers the date','—'],
  ['Occupancy %','Occupied ÷ sellable × 100, rounded half-up to 1 dp','sellable = 0 → "—"'],
  ['Room revenue','Σ room-night lines with service date in range (discounts are reported separately)','—'],
  ['ADR','Room revenue ÷ occupied room-nights','0 nights → "—"'],
  ['RevPAR','Room revenue ÷ sellable room-nights','0 sellable → "—"'],
  ['Receipts','Σ payment receipts − refunds by receipt date; deposits shown separately','—'],
  ['Deposits held','Σ deposits − deposit returns across all folios, at the current moment','—'],
  ['Outstanding','Σ positive folio balances of open stays','—'],
  ['Expenses','Σ posted expenses by business date, split by funding source; a reversal is a negative record on its own date','—'],
  ['Drawer cash flows','Cash in = guest payments + deposits + POS cash sales; cash out = negative refunds + deposit returns + POS cash voids. Filtered by receipt date; cash expenses are separate.','Opening float + cash in + signed cash out − net cash expenses = expected cash for that day, before banking'],
  ['GST (CGST + SGST)','Per-line GST at the line’s snapshot rate from the dated GST master, rounded half away from zero to the paisa; CGST + SGST halves split per line (illustrative — CA to confirm)','—'],
];
const csvCell=csvQuoted;
const rupees=(p:number)=>(p/100).toFixed(2);
/** CSV for one report tab with a scenario/date header. Money in rupees with 2 decimals. */
export function reportCsv(s:State,pack:ReportPack,tabId:string){
  const t=pack.tabs.find(x=>x.id===tabId);
  const rows:(string|number)[][]=[['Hotel Control Centre — DEMONSTRATION · fictional data · not a tax document'],['Property',s.settings.hotel],['Scenario',s.scenario],
    ['Business dates',pack.from,pack.to],['Report',t?.label??'Report dictionary'],['Generated (simulated clock)',s.clock],[]];
  if(!t){rows.push(['Metric','Definition','Zero handling'],...reportDictionary);}
  else{
    rows.push(['Metric','Value','Unit','Source rows']);
    t.metrics.forEach(m=>rows.push([m.label,m.unit==='money'?rupees(m.value):m.value,m.unit==='money'?'INR':'count',m.rows.length]));
    t.ratios.forEach(r=>rows.push([r.label,r.display,r.formula,'']));
    if(t.table){rows.push([],t.table.columns);t.table.rows.forEach(r=>rows.push(r.map((v,i)=>t.table!.units[i]==='money'&&typeof v==='number'?rupees(v):v)));}
  }
  return rows.map(r=>r.map(csvCell).join(',')).join('\r\n');
}
/** CSV of one metric's drill-down rows (same filters as the screen). */
export function reportDrillCsv(s:State,pack:ReportPack,m:ReportMetric){
  const head=[['Hotel Control Centre — DEMONSTRATION · fictional data'],['Scenario',s.scenario],['Business dates',pack.from,pack.to],['Metric',m.label],[],
    ['Date','Reference','Booking','Guest','Room','Detail','Method','Amount']];
  const body=m.rows.map(r=>[r.date,r.ref,r.booking??'',r.guest??'',r.room??'',r.detail,r.method??'',m.unit==='money'?rupees(r.amount):r.amount]);
  return [...head,...body,['','','','','','Total','',m.unit==='money'?rupees(m.value):m.value]].map(r=>r.map(csvCell).join(',')).join('\r\n');
}
