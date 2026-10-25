// Stream F · Cashier, closing & reports — pure domain helpers and selectors (re-exported by services/mock/index.ts for UI and tests).
// Every figure is derived from source records (REQ-RPT-003): lines by service date, receipts by receipt date,
// expenses by business date, occupancy from stay intervals. Money is integer paise.
import type {State,Booking,Expense,Receipt,Line} from '../types';
import {money,addDays} from '../money';
import {drawer,blockedDuring,totals} from '../selectors';
import {posSlice,posOpenOrders,posOrderTotals,posSpot} from './pos';

// ── Limits (RBAC Matrix §6, demonstration defaults) ──
/** Manager may accept a cash difference up to ₹200; above → Owner (PERM-SHF-002). */
export const SHIFT_MANAGER_DIFF_LIMIT=20000;
/** Manager may approve expenses up to ₹5,000; above → Owner (PERM-EXP-002). */
export const EXPENSE_MANAGER_LIMIT=500000;
/** Roles whose approval limits are unlimited for cash differences and expenses. */
export const closingUnlimitedRoles=['Owner','Accountant'];
/** Note denominations for the cash count grid, in rupees. Coins are entered as a rupee total. */
export const SHIFT_DENOMINATIONS=[500,200,100,50,20,10];
/** Funding sources (REQ-EXP-001). 'Cash' is drawer cash; 'Vendor credit' has no drawer effect. */
export const EXPENSE_METHODS=['Cash','UPI','Bank transfer','Vendor credit'];
export const EXPENSE_CATEGORIES=['Supplies','Laundry','Maintenance','Utilities','Petty cash'];
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
};

// ── Cash shift (SCR-11, WF-SHIFT-001) ──
export type ShiftStatus='Open'|'Submitted'|'Closed';
export function shiftStatus(s:State):ShiftStatus{
  if(s.shiftOpen)return 'Open';
  return s.shift?.date===s.date&&s.shift.status==='Submitted'?'Submitted':'Closed';
}
export type CashMovement={id:string;kind:string;label:string;booking?:string;guest?:string;reference:string;amount:number;actor:string;
  /** POS check behind a restaurant / room-service movement. */ order?:string};
/** Today's cash movements, signed (+ into the drawer, − out). Sum + opening float = drawer(s).
 *  Shared till (DEC-052): POS cash sales and same-day voids count once here; UPI/card bills and room charges to a folio do not. */
export function shiftMovements(s:State):CashMovement[]{
  const rec=s.bookings.flatMap(b=>b.receipts.filter(r=>r.date===s.date&&r.method==='Cash').map(r=>({id:r.id,kind:r.kind,booking:b.id,guest:b.guest,
    label:receiptLabel(r.kind),reference:r.reference,amount:['payment','deposit'].includes(r.kind)?r.amount:-r.amount,actor:r.actor})));
  const exp=s.expenses.filter(e=>e.date===s.date&&e.method==='Cash'&&expenseCounts(e)).map(e=>({id:e.id,kind:e.reversalOf?'expense-reversal':'expense',
    label:e.reversalOf?'Expense reversal · '+e.vendor:'Cash expense · '+e.vendor,reference:e.reversalOf?'Reverses '+e.reversalOf:e.category,amount:-e.amount,actor:e.actor}));
  const p=posSlice(s);
  const pos=p.receipts.filter(r=>r.date===s.date&&r.method==='Cash').map(r=>{
    const o=p.orders.find(x=>x.id===r.order),outlet=p.outlets.find(x=>x.id===r.outlet)?.name??r.outlet,bill=o?.settlement?.billNo??r.order;
    return {id:r.id,kind:r.kind==='void'?'pos-void':'pos-sale',label:`${outlet}${r.kind==='void'?' void':''} · ${bill}`,guest:o?.guest,order:r.order,
      reference:o?posSpot(o)+' · '+r.reference:r.reference,amount:r.amount,actor:r.actor};});
  return [...rec,...pos,...exp];
}
export function receiptLabel(kind:string){return kind==='payment'?'Payment / advance':kind==='deposit'?'Refundable deposit':kind==='refund'?'Payment refund':kind==='deposit-return'?'Deposit returned':kind==='company-credit'?'Company credit':kind;}
/** The expected-cash formula with its parts (Financial Rulebook §4). parts.expected always equals drawer(s). */
export function shiftExpected(s:State){
  const mv=shiftMovements(s),sum=(k:string[])=>mv.filter(m=>k.includes(m.kind)).reduce((a,m)=>a+m.amount,0);
  const neg=(n:number)=>0-n||0; // avoid −0 ("-₹0" on screen)
  const parts={opening:s.openingCash,payments:sum(['payment']),deposits:sum(['deposit']),refunds:neg(sum(['refund'])),returns:neg(sum(['deposit-return'])),
    expenses:neg(sum(['expense','expense-reversal'])),other:sum(['company-credit']),restaurant:sum(['pos-sale','pos-void'])};
  const expected=parts.opening+parts.payments+parts.deposits+parts.restaurant+parts.other-parts.refunds-parts.returns-parts.expenses;
  return {...parts,expected,consistent:expected===drawer(s)};
}
/** Denomination grid → paise. counts[note]=number of notes; coins = rupee amount. */
export function shiftDenominationTotal(d:Record<string,number>){
  return SHIFT_DENOMINATIONS.reduce((a,n)=>a+(d[n]||0)*n*100,0)+Math.round((d.coins||0)*100);
}

// ── Expenses (SCR-13, WF-EXP-001) ──
/** Posted and later-reversed expenses both happened on their date; the reversal record (negative) offsets on its own date. */
export const expenseCounts=(e:Expense)=>e.status==='Posted'||e.status==='Reversed';
/** Same vendor + amount + date (case-insensitive vendor), excluding rejected items and reversal records. Warning only. */
export function expenseDuplicates(s:State,vendor:string,amount:number,date:string,except=''):Expense[]{
  const v=vendor.trim().toLowerCase();if(!v||!amount)return [];
  return s.expenses.filter(e=>e.id!==except&&!e.reversalOf&&e.status!=='Rejected'&&e.vendor.trim().toLowerCase()===v&&e.amount===amount&&e.date===date);
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
/** All preflight checks in close order; a check with items is blocking (REQ-CLOSE-002). */
export function closingPreflight(s:State):PreflightIssue[]{
  const pendingApprovals=s.approvals.filter(a=>a.status==='Pending');
  const st=shiftStatus(s);
  return [
    {id:'arrivals',title:'Arrivals resolved',hint:'Check in, or cancel after refunding advances.',page:'reservations',
      items:s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date).map(b=>({label:`${b.guest} · room ${b.room} · ${b.id}`,booking:b.id}))},
    {id:'departures',title:'Overdue departures resolved',hint:'Settle and check out, or extend the stay.',page:'folios',
      items:s.bookings.filter(b=>b.status==='In house'&&b.departure<=s.date).map(b=>({label:`${b.guest} · room ${b.room} · due ${b.departure}`,booking:b.id}))},
    {id:'holds',title:'Expired holds released',hint:'Release or confirm holds whose expiry has passed.',page:'reservations',
      items:s.bookings.filter(b=>b.status==='Held'&&!!holdUntil(b)&&holdUntil(b)!<s.clock).map(b=>({label:`${b.guest} · room ${b.room} · hold expired ${holdUntil(b)!.replace('T',' ')}`,booking:b.id}))},
    {id:'nights',title:'Room-night charges posted',hint:'Post tonight’s room line for every in-house stay (step 2).',
      items:s.bookings.filter(b=>b.status==='In house'&&!closingHasNight(b,s.date)).map(b=>({label:`${b.guest} · room ${b.room} · ${money(b.rate)}`,booking:b.id}))},
    {id:'approvals',title:'Approvals resolved',hint:'Approve or reject pending requests.',page:'expenses',
      items:pendingApprovals.map(a=>({label:`${approvalKindLabel(a.kind)} · ${money(a.amount)} · ${a.id}`,page:a.kind==='cash-difference'?'cashier':'expenses'}))},
    {id:'expenses',title:'Expenses resolved',hint:'Approve or reject pending expenses.',page:'expenses',
      items:s.expenses.filter(e=>e.status==='Pending').map(e=>({label:`${e.vendor} · ${money(e.amount)} · ${e.id}`,page:'expenses'}))},
    {id:'pos',title:'Restaurant checks settled',hint:'Settle each open check, charge it to the room, or void it (with approval above the limit).',page:'pos',
      items:posOpenOrders(s).map(o=>({label:`${posSpot(o)} · ${o.id}${o.guest?' · '+o.guest:''} · ${money(posOrderTotals(s,o).total)} open`,page:'pos'}))},
    {id:'shift',title:'Cash shift reconciled',hint:st==='Submitted'?'The count awaits difference approval.':'Count the drawer and submit the shift.',page:'cashier',
      items:st==='Closed'?[]:[{label:st==='Submitted'?'Count submitted · difference awaiting approval':'Cash shift open · '+money(drawer(s))+' expected',page:'cashier'}]},
  ];
}
export function approvalKindLabel(kind?:string){return !kind?'Discount':kind==='cash-difference'?'Cash difference':kind==='closed-date-correction'?'Closed-date correction':kind[0].toUpperCase()+kind.slice(1);}
/** Closing record for the current business date (snapshot taken at DAY_CLOSE). */
export function closingSummary(s:State,closedBy=''):ClosingRecord{
  const d=s.date,p=reportPack(s,d,d),v=(tab:string,id:string)=>p.tabs.find(t=>t.id===tab)!.metrics.find(m=>m.id===id)?.value??0;
  const byTender:Record<string,number>={};for(const t of reportTenders(s,d,d))byTender[t]=v('receipts','tender:'+t);
  const occ=reportOccupancyRange(s,d,d),ex=shiftExpected(s);
  const counted=s.shiftClose?.counted??ex.expected,expected=s.shiftClose?.expected??ex.expected;
  const sh=s.shift?.date===d?s.shift:undefined,countedBy=sh?.submittedBy??s.shiftClose?.countedBy;
  const exceptions:string[]=[];
  if(counted!==expected)exceptions.push(`Cash difference ${money(counted-expected)} accepted${sh?.decidedBy?' by '+userName(s,sh.decidedBy):''}${sh?.reason?' · '+sh.reason:''}`);
  if(s.closeProgress?.date===d&&s.closeProgress.runs>1)exceptions.push(`Night audit resumed after an interruption (${s.closeProgress.runs} runs; no night posted twice)`);
  const corrections=s.bookings.flatMap(b=>b.lines.filter(l=>l.date===d&&l.kind==='adjustment'&&(l as Line&{correctionOf?:string}).correctionOf).map(l=>({b,l})));
  corrections.forEach(({b,l})=>exceptions.push(`Correction ${money(l.amount)} on ${b.id} for closed date ${(l as Line&{correctionOf?:string}).correctionOf}`));
  s.expenses.filter(e=>e.reversalOf&&e.date===d).forEach(e=>exceptions.push(`Expense ${e.reversalOf} reversed (${money(e.amount)})`));
  s.expenses.filter(e=>e.status==='Rejected'&&e.date===d).forEach(e=>exceptions.push(`Expense ${e.id} rejected${e.reason?' · '+e.reason:''}`));
  return {date:d,closedAt:s.clock,closedBy,
    revenue:{room:v('revenue','room'),service:v('revenue','service'),discounts:v('revenue','discount'),adjustments:v('revenue','adjustment'),net:v('revenue','net'),tax:v('revenue','tax')},
    receipts:{byTender,net:v('receipts','net'),companyCredit:v('receipts','company-credit')},
    deposits:{received:v('receipts','deposit-in'),returned:0-v('receipts','deposit-out')||0,heldAtClose:s.bookings.reduce((a,b)=>a+totals(b).deposit,0)},
    expenses:{cash:v('expenses','cash'),nonCash:v('expenses','noncash'),vendorCredit:v('expenses','vendor'),total:v('expenses','total')},
    occupancy:{sellable:occ.sellable,occupied:occ.occupied,occupancy:reportPct(occ.occupied,occ.sellable),roomRevenue:occ.roomRevenue,adr:reportRatioMoney(occ.roomRevenue,occ.occupied),revpar:reportRatioMoney(occ.roomRevenue,occ.sellable)},
    cash:{opening:s.openingCash,expected,counted,difference:counted-expected,reason:counted!==expected?sh?.reason:undefined,approvedBy:counted!==expected?sh?.decidedBy:undefined,...(countedBy?{countedBy}:{}),restaurant:ex.restaurant},
    outlet:closingOutlet(s,d),
    nightAudit:{stays:closingNightTargets(s).length,resumed:!!(s.closeProgress?.date===d&&s.closeProgress.runs>1)},
    exceptions};
}
const userName=(s:State,id:string)=>s.users.find(u=>u.id===id)?.name??id;
/** Outlet receipts of a business date by method (sales − same-day voids) and room-service bills charged to folios. */
export function closingOutlet(s:State,d:string){
  const p=posSlice(s),byMethod:Record<string,number>={Cash:0,UPI:0,Card:0};
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
const lineTax=(l:Line)=>Math.round(l.amount*l.taxBps/10000);
export function reportLines(s:State,from:string,to:string){
  return s.bookings.flatMap(b=>b.lines.filter(l=>inRange(l.date,from,to)).map(l=>({b,l})));
}
export function reportReceipts(s:State,from:string,to:string){
  return s.bookings.flatMap(b=>b.receipts.filter(r=>inRange(r.date,from,to)).map(r=>({b,r})));
}
export function reportExpenses(s:State,from:string,to:string){return s.expenses.filter(e=>expenseCounts(e)&&inRange(e.date,from,to));}
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
  return [...TENDERS,...found.filter((m,i)=>!TENDERS.includes(m)&&found.indexOf(m)===i)];
}
const lineRow=(b:Booking,l:Line,amount:number):DrillRow=>({date:l.date,ref:l.id,booking:b.id,guest:b.guest,room:b.room,detail:l.label,kind:l.kind,amount});
const receiptRow=(b:Booking,r:Receipt):DrillRow=>({date:r.date,ref:r.id,booking:b.id,guest:b.guest,room:b.room,detail:receiptLabel(r.kind)+' · '+r.reference,method:r.method,kind:r.kind,
  amount:r.kind==='refund'||r.kind==='deposit-return'?-r.amount:r.amount});
const expenseRow=(e:Expense):DrillRow=>({date:e.date,ref:e.id,detail:`${e.vendor} · ${e.category}${e.reversalOf?' · reverses '+e.reversalOf:''}${e.status==='Reversed'?' · later reversed':''}`,method:e.method,kind:e.reversalOf?'reversal':'expense',amount:e.amount});
const metric=(id:string,label:string,rows:DrillRow[],unit:'money'|'count'='money',note?:string):ReportMetric=>({id,label,unit,rows,note,value:rows.reduce((a,r)=>a+r.amount,0)});
const sumBy=(rows:DrillRow[],d:string)=>rows.filter(r=>r.date===d).reduce((a,r)=>a+r.amount,0);

/** Every report tab for an inclusive business-date range. Each metric's value is exactly the sum of its drill-down rows. */
export function reportPack(s:State,from:string,to:string):ReportPack{
  const dates=reportDates(from,to);
  const lines=reportLines(s,from,to),rec=reportReceipts(s,from,to),exp=reportExpenses(s,from,to);
  const L=(k?:Line['kind'])=>lines.filter(x=>!k||x.l.kind===k).map(x=>lineRow(x.b,x.l,x.l.amount));
  const taxRows=lines.map(x=>lineRow(x.b,x.l,lineTax(x.l)));
  const R=(kinds:string[],method?:string)=>rec.filter(x=>kinds.includes(x.r.kind)&&(!method||x.r.method===method)).map(x=>receiptRow(x.b,x.r));
  // Shared till: POS receipts are separate source records; folio transfers create no receipt here.
  const posCash:DrillRow[]=posSlice(s).receipts.filter(r=>r.method==='Cash'&&inRange(r.date,from,to)).map(r=>({
    date:r.date,ref:r.id,detail:`POS ${r.kind} · ${r.order} · ${r.reference}`,method:r.method,kind:'pos-'+r.kind,amount:r.amount}));
  const stayRows=dates.flatMap(d=>reportStaysOn(s,d).map(b=>({date:d,ref:b.id+':'+d,booking:b.id,guest:b.guest,room:b.room,detail:'Occupied room night · '+b.status,amount:1})));
  const sellRows=dates.flatMap(d=>reportSellableOn(s,d).map(r=>({date:d,ref:'room:'+r.id+':'+d,room:r.id,detail:`Sellable room ${r.id} · ${r.type}`,amount:1})));
  const arrRows=s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&inRange(b.arrival,from,to)).map(b=>({date:b.arrival,ref:b.id,booking:b.id,guest:b.guest,room:b.room,detail:'Arrival · '+b.source,amount:1}));
  const depRows=s.bookings.filter(b=>b.status==='Departed'&&inRange(b.departure,from,to)).map(b=>({date:b.departure,ref:b.id,booking:b.id,guest:b.guest,room:b.room,detail:'Departure (checked out)',amount:1}));
  const E=(f:(e:Expense)=>boolean)=>exp.filter(f).map(expenseRow);
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
      metric('adjustment','Adjustments & corrections',L('adjustment')),net,metric('tax','Synthetic tax (10% test rate)',taxRows,'money','Tax is a liability, not revenue'),
      metric('gross','Charges incl. tax',[...L(),...taxRows.map(r=>({...r,ref:r.ref+':tax',detail:'Tax · '+r.detail}))])],
      table:{columns:['Business date','Room','Service','Discounts','Adjustments','Net revenue','Tax'],units:['text','money','money','money','money','money','money'],
        rows:dates.map(d=>[d,sumBy(L('room'),d),sumBy(L('service'),d),sumBy(L('discount'),d),sumBy(L('adjustment'),d),sumBy(net.rows,d),sumBy(taxRows,d)])}},
    {id:'receipts',label:'Receipts & deposits',ratios:[],metrics:[...tenders.map(t=>metric('tender:'+t,t+' (payments − refunds)',R(['payment','refund'],t))),pay,
      metric('deposit-in','Deposits received',R(['deposit'])),metric('deposit-out','Deposits returned',R(['deposit-return'])),
      metric('company-credit','Company credit (city ledger)',R(['company-credit'])),
      metric('held','Deposits held now · all open folios',s.bookings.filter(b=>totals(b).deposit!==0).map(b=>({date:s.date,ref:b.id,booking:b.id,guest:b.guest,room:b.room,detail:'Deposit balance · '+b.status,amount:totals(b).deposit})),'money','Point-in-time liability; not filtered by date')],
      table:{columns:['Receipt date',...tenders,'Net receipts','Deposits in','Deposits out'],units:['text',...tenders.map(()=>'money' as const),'money','money','money'],
        rows:dates.map(d=>[d,...tenders.map(t=>sumBy(R(['payment','refund'],t),d)),sumBy(pay.rows,d),sumBy(R(['deposit']),d),sumBy(R(['deposit-return']),d)])}},
    {id:'expenses',label:'Expenses & cash',ratios:[],metrics:[metric('cash','Drawer-cash expenses',E(e=>e.method==='Cash')),metric('noncash','UPI / bank expenses',E(e=>e.method==='UPI'||e.method==='Bank transfer')),
      metric('vendor','Vendor-credit expenses (no drawer effect)',E(e=>e.method==='Vendor credit')),metric('reversals','Reversals included above',E(e=>!!e.reversalOf)),expTotal,
      metric('cash-in','Cash into drawer (payments + deposits + POS sales)',[...R(['payment','deposit'],'Cash'),...posCash.filter(r=>r.kind==='pos-sale')]),
      metric('cash-out','Cash out of drawer (refunds + returns + POS voids)',[...R(['refund','deposit-return'],'Cash'),...posCash.filter(r=>r.kind==='pos-void')])],
      table:{columns:['Business date','Day','Opening float','Expected cash','Counted','Difference','Cash expenses'],units:['text','text','money','money','money','money','money'],
        rows:dates.map(d=>{const c=closingCash(d);return [d,c[4],c[0],c[1],c[2],c[3],sumBy(E(e=>e.method==='Cash'),d)];})}},
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
  ['Synthetic tax','Per-line tax rounded half-up to the paisa at the line’s snapshot rate; test data, not GST','—'],
];
const csvCell=(v:string|number)=>'"'+String(v).split('"').join('""')+'"';
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
