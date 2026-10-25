// FY history Phase B: daily operations on top of the generated stays — expenses, cash counts with differences and their approvals,
// one closing record per business day (computed by the real closingSummary selector), closed dates, compact housekeeping history
// (plan H-06) and the audit trail (one summary entry per closed day plus every approval / reversal / override event).
// Two steps (shared till, DEC-052): addOperations draws every random choice; closeHistoryDays runs after the POS history exists
// (modules-sales.ts) so restaurant cash enters each day's expected cash and count, and the records come from the full day.
import type {State,Booking,Expense,Approval,Task,Audit} from '../../domain/types';
import type {POSOrder,POSReceipt} from '../../domain/streams/pos';
import {addDays,money} from '../../domain/money';
import {drawer} from '../../domain/selectors';
import {closingSummary,reportDates,expenseCounts,EXPENSE_MANAGER_LIMIT} from '../../domain/streams/closing';
import type {HistoryDay} from '../../domain/streams/history';
import type {Rng} from './prng';

const pad=(n:number,w=4)=>String(n).padStart(w,'0');
const roundTo=(n:number,step:number)=>Math.round(n/step)*step;
/** Opening float restored every morning; surplus cash is banked after the count. */
export const HISTORY_FLOAT=500000;
const KEEP_IN_DRAWER=200000;
type Event={at:string;actor:string;action:string;detail:string};

const SMALL_ITEMS:readonly (readonly [string,string,number,number])[]=[
  ['Morning Supplies','Supplies',15000,150000],['Sai Provisions','Supplies',20000,180000],['Local Vegetable Market','Petty cash',30000,250000],
  ['Stationers & Co','Supplies',10000,60000],['Hardware Corner','Maintenance',15000,120000],['Tea & Snacks Counter','Petty cash',8000,40000]];
const MONTHLY:readonly (readonly [number,string,string,number,number])[]=[
  [5,'Marathwada Power Supply (fictional)','Utilities',2800000,5500000],[7,'Municipal Water Board (fictional)','Utilities',450000,900000],
  [10,'Latur Broadband (fictional)','Utilities',320000,320000],[15,'Lift & Genset AMC (fictional)','Maintenance',1200000,1200000]];

export function addOperations(s:State,rng:Rng,o:{from:string;to:string}):void{
  const dates=reportDates(o.from,o.to);

  // Folio cash per day keeps cash expenses within the drawer (restaurant cash only adds to it, so the guard stays safe).
  const cashNet=new Map<string,number>();
  for(const b of s.bookings)for(const r of b.receipts)
    if(r.method==='Cash'&&r.date<=o.to)cashNet.set(r.date,(cashNet.get(r.date)??0)+(r.kind==='payment'||r.kind==='deposit'?r.amount:-r.amount));

  const events:Event[]=[],expenses:Expense[]=[],approvals:Approval[]=[];
  const reversalsDue=new Map<string,Expense[]>();
  let en=0,ap=0;
  let opening=HISTORY_FLOAT;

  for(const d of dates){
    const dayExp:Expense[]=[...(reversalsDue.get(d)??[])];
    const final=d===o.to;
    let cashOut=0;
    const cashRoom=()=>opening+(cashNet.get(d)??0)-KEEP_IN_DRAWER-cashOut;
    const mk=(vendor:string,category:string,amount:number,wantCash:boolean,actor:string,alt='UPI'):Expense=>{
      const cash=wantCash&&!final&&amount<=cashRoom();
      const method=cash?'Cash':alt;
      if(cash)cashOut+=amount;
      const e:Expense={id:'EXP-H'+pad(++en),vendor,category,amount,method,date:d,status:'Posted',actor,
        approver:amount>EXPENSE_MANAGER_LIMIT?'owner':'manager',decidedAt:d+'T18:00'};
      dayExp.push(e);return e;
    };
    // daily small items (supplies, kitchen vegetables, petty cash)
    for(let k=rng.int(1,3);k>0;k--){
      const [vendor,cat,lo,hi]=rng.pick(SMALL_ITEMS);
      mk(vendor,cat,roundTo(rng.int(lo,hi),500),rng.chance(0.75),rng.chance(0.6)?'reception':'cashier');
    }
    // weekly laundry vendor (Mondays)
    if(new Date(d+'T12:00:00Z').getUTCDay()===1)mk('Fresh Linen Services','Laundry',roundTo(rng.int(350000,900000),5000),rng.chance(0.4),'manager');
    // monthly bills (bank transfer, not drawer cash)
    for(const [day,vendor,cat,lo,hi] of MONTHLY)if(Number(d.slice(8,10))===day)mk(vendor,cat,roundTo(rng.int(lo,hi),10000),false,'accountant','Bank transfer');
    // occasional repairs
    if(rng.chance(0.08)){const [vendor]=rng.pick([['Joseph Repairs'],['Shree Electricals'],['Om Plumbing Works']] as const);
      mk(vendor,'Maintenance',roundTo(rng.int(80000,1400000),5000),rng.chance(0.5),'manager');}
    // a few rejected requests (no money moves)
    if(rng.chance(0.015))dayExp.push({id:'EXP-H'+pad(++en),vendor:rng.pick(['Unknown Traders','Roadside Vendor']),category:'Supplies',amount:roundTo(rng.int(50000,300000),5000),method:'Cash',date:d,
      status:'Rejected',actor:'reception',approver:'manager',decidedAt:d+'T17:30',reason:rng.pick(['No bill attached','Duplicate of an earlier bill','Not a hotel expense'])});
    // a few non-cash expenses reversed on a later day (negative record on that day)
    for(const e of dayExp.filter(x=>x.status==='Posted'&&!x.reversalOf&&x.method!=='Cash'&&x.category!=='Utilities')){
      if(!rng.chance(0.012))continue;
      const rd=addDays(d,rng.int(1,5));if(rd>o.to)continue;
      const reason=rng.pick(['Billed twice by the vendor','Wrong ledger category','Goods returned']);
      const rid='EXP-H'+pad(++en);
      e.status='Reversed';e.reversedBy=rid;e.reason=reason;
      const rec:Expense={id:rid,vendor:e.vendor,category:e.category,amount:-e.amount,method:e.method,date:rd,status:'Posted',actor:'manager',reversalOf:e.id,reason,approver:e.approver,decidedAt:rd+'T12:00'};
      reversalsDue.set(rd,[...(reversalsDue.get(rd)??[]),rec]);
      events.push({at:rd+'T12:00',actor:'manager',action:'Expense reversed',detail:`${e.id} reversed by ${rid} · ${money(e.amount)} · ${reason}`});
    }
    dayExp.filter(e=>e.status==='Rejected').forEach(e=>events.push({at:d+'T17:30',actor:'manager',action:'Expense rejected',detail:`${e.id} · ${e.vendor} · ${money(e.amount)} · ${e.reason}`}));
    expenses.push(...dayExp);

    // ── Cash shift as the folios and expenses see it; restaurant cash and the closing record are added in closeHistoryDays ──
    const expected=opening+(cashNet.get(d)??0)-dayExp.filter(e=>e.method==='Cash'&&expenseCounts(e)).reduce((a,e)=>a+e.amount,0);
    let counted=expected;
    if(!final&&rng.chance(0.08)){
      const diff=(rng.chance(0.5)?1:-1)*rng.int(1,20)*1000;counted=expected+diff;
      const aid='APR-HC'+pad(++ap,3),reason=diff<0?rng.pick(['Short change given at the desk','Count error found next morning','Float shortfall, cashier to recheck']):rng.pick(['Guest overpaid, refund declined','Unrecorded tip left at the desk']);
      // meta.expected / meta.counted are set by closeHistoryDays once restaurant cash is known; the difference drawn here is kept.
      approvals.push({id:aid,booking:'',amount:Math.abs(diff),reason,status:'Approved',actor:'cashier',version:ap,kind:'cash-difference',target:d,approver:'manager',decidedAt:d+'T21:30',meta:{command:'SHIFT_DIFFERENCE_DECIDE',diff}});
      events.push({at:d+'T21:30',actor:'manager',action:'Cash difference approved',detail:`${aid} · ${money(diff)} (${diff>0?'over':'short'}) · ${reason}`});
    }
    opening=Math.min(counted,HISTORY_FLOAT);
  }

  // Discount / company-credit approvals created with the stays also belong in the audit trail.
  for(const a of s.approvals.filter(a=>a.id.startsWith('APR-H')&&!a.id.startsWith('APR-HC'))){
    const b=s.bookings.find(x=>x.id===a.booking);if(!b)continue;
    events.push(a.kind==='company-credit'
      ?{at:String(a.meta?.executedAt??a.decidedAt),actor:'reception',action:'Company credit applied',detail:`${a.id} · ${b.id} · ${money(a.amount)} · ${b.company?.name}`}
      :{at:a.decidedAt!,actor:'manager',action:'Discount approved',detail:`${a.id} · ${b.id} · ${money(a.amount)} · ${a.reason}`});
  }

  // ── Compact housekeeping history: full task rows for the last 30 days, a daily summary row for every day ──
  const winFrom=addDays(o.to,-29);
  const tasks:Task[]=[],days:HistoryDay[]=dates.map(date=>({date,departures:0,cleans:0,stayovers:0,audits:0}));
  const dayRow=new Map(days.map(r=>[r.date,r]));
  let tn=0;const who=()=>rng.chance(0.5)?'housekeeper':'housekeeper2';
  for(const b of s.bookings){
    if(b.status!=='Departed'||!b.id.startsWith('HCC-H'))continue;
    const dep=dayRow.get(b.departure);if(dep){dep.departures++;dep.cleans++;dep.audits++;}
    if(b.departure>=winFrom)tasks.push({id:'task-H'+pad(++tn),room:b.room,booking:b.id,assignee:who(),status:'Done',checked:[true,true,true],kind:'checkout',date:b.departure,
      createdAt:b.departure+'T11:30',startedAt:b.departure+'T12:10',completedAt:b.departure+'T12:50'});
    for(let d=addDays(b.arrival,1);d<b.departure;d=addDays(d,1)){
      const r=dayRow.get(d);if(r)r.stayovers++;
      if(d>=winFrom)tasks.push({id:'task-H'+pad(++tn),room:b.room,booking:b.id,assignee:who(),status:'Done',checked:[true,true,true],kind:'stayover',date:d,
        createdAt:d+'T09:00',startedAt:d+'T10:00',completedAt:d+'T10:30'});
    }
  }

  s.expenses=[...expenses,...s.expenses];
  s.approvals=[...approvals,...s.approvals];
  s.closedDates=[...dates];
  s.tasks=[...tasks,...s.tasks];
  s.historyX={from:o.from,to:o.to,days};
  s.audit=auditTrail(events);
}

/** Audit trail: newest first, ids stable by sort order. */
function auditTrail(events:Event[]):Audit[]{
  return events.sort((a,b)=>a.at<b.at?1:a.at>b.at?-1:a.detail<b.detail?1:-1).map((e,i)=>({id:'h-a-'+pad(i+1,5),at:e.at,actor:e.actor,action:e.action,detail:e.detail}));
}
const groupBy=<T,>(xs:T[],keys:(x:T)=>(string|undefined)[])=>{
  const m=new Map<string,T[]>();
  for(const x of xs)for(const k of new Set(keys(x)))if(k){let l=m.get(k);if(!l){l=[];m.set(k,l);}l.push(x);}
  return m;
};

/** Step 2 (after the POS history): one closing record per history day from the real closingSummary, with folio, expense AND
 *  restaurant cash in the drawer (shared till). The cash difference drawn in step 1 is kept; cash above the ₹5,000 float is banked. */
export function closeHistoryDays(s:State,o:{from:string;to:string}):void{
  const dates=reportDates(o.from,o.to);
  const live=(b:Booking)=>b.status==='Departed'||b.status==='In house';
  // Per-day indexes so each day's closing record is computed from the few folios and checks that touch it (not all 1,300).
  const touch=new Map<string,number[]>();
  const add=(d:string,i:number)=>{if(d>o.to||d<o.from)return;let l=touch.get(d);if(!l){l=[];touch.set(d,l);}if(l[l.length-1]!==i)l.push(i);};
  s.bookings.forEach((b,i)=>{
    const seen=new Set<string>();
    const mark=(d:string)=>{if(!seen.has(d)){seen.add(d);add(d,i);}};
    if(live(b))for(let d=b.arrival;d<b.departure&&d<=o.to;d=addDays(d,1))mark(d);
    b.lines.forEach(l=>mark(l.date));
    b.receipts.forEach(r=>mark(r.date));
  });
  const p=s.posX;
  const posOrders=groupBy<POSOrder>(p?.orders??[],x=>[x.date,x.settlement?.date]),posReceipts=groupBy<POSReceipt>(p?.receipts??[],r=>[r.date]);
  const dayExpenses=groupBy<Expense>(s.expenses,e=>[e.date]);
  const diffs=new Map(s.approvals.filter(a=>a.kind==='cash-difference'&&a.id.startsWith('APR-HC')).map(a=>[a.target!,a]));
  const closings:NonNullable<State['closings']>={},events:Event[]=[];
  let opening=HISTORY_FLOAT;
  for(const d of dates){
    const views=(touch.get(d)??[]).map(i=>{const b=s.bookings[i];return {...b,lines:b.lines.filter(l=>l.date<=d),receipts:b.receipts.filter(r=>r.date<=d)};});
    const base0:State={...s,date:d,clock:d+'T20:00',openingCash:opening,shiftOpen:true,shiftClose:undefined,shift:undefined,closeProgress:undefined,closings:undefined,
      bookings:views,expenses:dayExpenses.get(d)??[],approvals:[],posX:p&&{...p,orders:posOrders.get(d)??[],receipts:posReceipts.get(d)??[]}};
    const expected=drawer(base0);
    let counted=expected,shift:State['shift']=undefined;
    const a=diffs.get(d);
    if(a){
      counted=expected+Number(a.meta!.diff);
      a.meta={command:'SHIFT_DIFFERENCE_DECIDE',expected,counted};
      shift={date:d,status:'Closed',expected,counted,reason:a.reason,submittedBy:'cashier',approval:a.id,decidedBy:'manager'};
    }
    const banked=Math.max(0,counted-HISTORY_FLOAT);
    const ws:State={...base0,clock:d+'T23:30',shiftOpen:false,shiftClose:{expected,counted,countedBy:'cashier'},shift};
    const rec=closingSummary(ws,'manager');
    rec.nightAudit={stays:rec.occupancy.occupied,resumed:false};
    rec.cash.banked=banked;
    closings[d]=rec;
    events.push({at:d+'T23:30',actor:'manager',action:'Day closed',
      detail:`${d} · occupancy ${rec.occupancy.occupancy} · net revenue ${money(rec.revenue.net)} · receipts ${money(rec.receipts.net)} · cash ${shift?'difference '+money(counted-expected):'counted exactly'}`});
    opening=counted-banked;
  }
  if(opening!==s.openingCash)throw new Error(`History cash does not join up with BASE: closes with ${opening}, BASE opens with ${s.openingCash}`);
  s.closings={...(s.closings??{}),...closings};
  s.audit=auditTrail([...s.audit.map(({at,actor,action,detail})=>({at,actor,action,detail})),...events]);
}
