// ACC-3 lane acc3-ageing · Outstanding with ageing (rulebook §7.6, §3.3.3; contract 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §3).
// Receivables = parties under 1100 / 1110 / 1115 / 1120; payables = 2200 vendors. Reads ONLY the postings (contract §1: accRptVouchers).
// Bill-wise allocation: each Dr entry on a receivable party (Cr on a payable party) is a bill, ref = the voucher's source id (an opening voucher
// = `opening`, DEC-172: the oldest open item). Credits allocate in this order: (1) a vendor return / reversal record to its own bill,
// (2) ACC-2's explicit vendor-payment allocations (`vendorPayments[].allocations`, they win), (3) everything left oldest-first. A vendor
// payment voided later cancels against its own payment (the pair nets to nil). `days` = asOf − bill date; buckets 0–30 / 31–60 / 61–90 / 90+.
// Row `open` = the party's ledger balance on asOf (signed to the side: owed to / by the hotel); Σ buckets ≠ open (an advance / on-account
// credit, or a balance on the other side) sets `flagged` with a note. Pure selectors; money = integer paise. No credit-terms master exists,
// so `due` = the bill date (open item in the DONE file).
import type {State} from '../types';
import type {ACCVoucher} from './accounting';
import {accPartyName} from './accounting-parties';
import {accRptControl,accRptIsOpening,accRptLedgerName,accRptParty,accRptVouchers} from './accounting-reports';

// ── Contract §3 shapes (copied; `flagged`, `note`, `advance` are optional additions — written in the DONE file) ──
export type ACCAgeingBill={ref:string;date:string;due:string;amount:number;paid:number;open:number;days:number};
export type ACCAgeingRow={account:string;party:string;name:string;control:string;
  bills:ACCAgeingBill[];open:number;b0_30:number;b31_60:number;b61_90:number;b90:number;
  /** Σ buckets ≠ open (the ledger balance): an unallocated credit (advance / on account) or a balance on the other side. */flagged?:boolean;note?:string;
  /** Credits not allocated to any bill (advance received / paid on account), positive paise. */advance?:number};
export type ACCAgeingSide='receivable'|'payable';
export type ACCAgeingTotals={open:number;b0_30:number;b31_60:number;b61_90:number;b90:number};
export type ACCAgeing={asOf:string;side:ACCAgeingSide;rows:ACCAgeingRow[];totals:ACCAgeingTotals};

export const ACC_AGEING_CONTROLS:Record<ACCAgeingSide,readonly string[]>={receivable:['1100','1110','1115','1120'],payable:['2200']};
export const ACC_AGEING_OPENING_REF='opening';
export const accAgeingDays=(from:string,to:string)=>Math.round((Date.parse(to+'T00:00:00Z')-Date.parse(from+'T00:00:00Z'))/86400000);
export type ACCAgeingBucket='b0_30'|'b31_60'|'b61_90'|'b90';
export const accAgeingBucket=(days:number):ACCAgeingBucket=>days<=30?'b0_30':days<=60?'b31_60':days<=90?'b61_90':'b90';

/** One credit (payment / receipt / return) on a party ledger: key (voucher source id), date, amount, an own target bill or explicit allocations. */
export type ACCAgeingCredit={key:string;date:string;amount:number;target?:string;explicit?:{ref:string;amount:number}[]};
type Bill=ACCAgeingBill&{order:number};
const rupees=(p:number)=>(p/100).toFixed(2);

/** Explicit targets of a payable credit (prototype records): a return / reversal record → its bill; a vendor payment → its allocations. */
export function accAgeingPayableTargets(s:State,v:ACCVoucher):Pick<ACCAgeingCredit,'target'|'explicit'>{
  if(v.source.kind==='expense'){const e=s.expenses.find(x=>x.id===v.source.id);if(!e)return {};
    if(e.reversalOf)return {target:e.reversalOf};
    if(typeof e.sourceRef==='string'&&e.sourceRef.startsWith('ret:'))return {target:e.sourceRef.slice(4)};
    return {};}
  if(v.source.kind==='vendor'&&v.source.id.startsWith('vendor-payment:')&&!v.source.id.endsWith('/void')){
    const id=v.source.id.slice('vendor-payment:'.length),p=(s.expensesX?.vendorPayments??[]).find(x=>x.id===id);
    if(p?.allocations?.length)return {explicit:p.allocations.map(a=>({ref:a.expense,amount:a.amount}))};
  }
  return {};
}

/** Bill-wise allocation of one party ledger: bills in posting order (the `opening` ref is always the oldest), credits applied (1) to their own
 *  target bill, (2) by explicit allocations, (3) the rest oldest-first. Returns the bills with something still open and the unallocated rest. */
export function accAgeingAllocate(asOf:string,bills0:{ref:string;date:string;amount:number}[],credits:ACCAgeingCredit[]):{bills:ACCAgeingBill[];advance:number}{
  const bills:Bill[]=[];const byRef=new Map<string,Bill>();
  bills0.forEach((b,i)=>{const x=byRef.get(b.ref);if(x){x.amount+=b.amount;x.open+=b.amount;return;}
    const n:Bill={ref:b.ref,date:b.date,due:b.date,amount:b.amount,paid:0,open:b.amount,days:accAgeingDays(b.date,asOf),order:b.ref===ACC_AGEING_OPENING_REF?-1:i};
    bills.push(n);byRef.set(b.ref,n);});
  const oldest=[...bills].sort((a,b)=>a.order===-1?-1:b.order===-1?1:a.date<b.date?-1:a.date>b.date?1:a.order-b.order);
  const pay=(b:Bill,max:number)=>{const n=Math.min(Math.max(0,b.open),Math.max(0,max));if(n>0){b.paid+=n;b.open-=n;}return n;};
  const left=credits.map(c=>c.amount);
  credits.forEach((c,i)=>{
    if(c.target){const b=byRef.get(c.target);if(b)left[i]-=pay(b,left[i]!);}
    for(const a of c.explicit??[]){if(left[i]!<=0)break;const b=byRef.get(a.ref);if(b)left[i]-=pay(b,Math.min(a.amount,left[i]!));}
  });
  let pool=left.reduce((a,x)=>a+Math.max(0,x),0);
  for(const b of oldest){if(pool<=0)break;pool-=pay(b,pool);}
  return {bills:bills.filter(b=>b.open!==0).map(({order:_o,...b})=>b),advance:pool};
}

/** Outstanding with ageing on `asOf` for one side (contract §3), every voucher up to and including `asOf`. */
export function accAgeing(s:State,asOf:string,side:ACCAgeingSide):ACCAgeing{
  const controls=ACC_AGEING_CONTROLS[side],sign=side==='receivable'?1:-1;
  const {opening,period}=accRptVouchers(s,asOf,asOf);
  const per=new Map<string,{balance:number;bills:{ref:string;date:string;amount:number}[];credits:ACCAgeingCredit[]}>();
  for(const v of [...opening,...period]){
    for(const e of v.entries){
      if(!controls.includes(accRptControl(e.account)))continue;
      const amt=sign*(e.debit-e.credit);if(!amt)continue;
      const x=per.get(e.account)??{balance:0,bills:[],credits:[]};per.set(e.account,x);x.balance+=amt;
      const ref=accRptIsOpening(v)?ACC_AGEING_OPENING_REF:v.source.id;
      // a later void of a payment cancels that payment's credit first (the pair nets to nil)
      if(amt>0&&v.source.id.endsWith('/void')){const k=v.source.id.slice(0,-5),c=x.credits.find(c=>c.key===k&&c.amount>0);
        if(c){const n=Math.min(c.amount,amt);c.amount-=n;if(n<amt)x.bills.push({ref,date:v.date,amount:amt-n});continue;}}
      if(amt>0)x.bills.push({ref,date:v.date,amount:amt});
      else x.credits.push({key:v.source.id,date:v.date,amount:-amt,...(side==='payable'?accAgeingPayableTargets(s,v):{})});
    }
  }
  const rows:ACCAgeingRow[]=[];
  for(const [account,x] of per){
    const {bills,advance}=accAgeingAllocate(asOf,x.bills,x.credits);
    const open=bills.filter(b=>b.open>0);
    const k={b0_30:0,b31_60:0,b61_90:0,b90:0};for(const b of open)k[accAgeingBucket(b.days)]+=b.open;
    const sum=k.b0_30+k.b31_60+k.b61_90+k.b90;
    if(!x.balance&&!sum)continue;
    const party=accRptParty(account)??'',control=accRptControl(account);
    const row:ACCAgeingRow={account,party,name:party?accPartyName(s,account)??party:accRptLedgerName(s,account),control,bills:open,open:x.balance,...k};
    if(sum!==x.balance){row.flagged=true;if(advance)row.advance=advance;
      row.note=advance?`${side==='receivable'?'Advance received':'Advance paid'} ₹${rupees(advance)} not allocated to any bill; ledger balance ₹${rupees(x.balance)} vs open bills ₹${rupees(sum)}.`
        :`Ledger balance ₹${rupees(x.balance)} does not tie to the open bills ₹${rupees(sum)}.`;}
    rows.push(row);
  }
  const lc=(r:ACCAgeingRow)=>r.name.toLowerCase();
  rows.sort((a,b)=>a.control<b.control?-1:a.control>b.control?1:lc(a)<lc(b)?-1:lc(a)>lc(b)?1:a.account<b.account?-1:1);
  const totals=rows.reduce<ACCAgeingTotals>((t,r)=>({open:t.open+r.open,b0_30:t.b0_30+r.b0_30,b31_60:t.b31_60+r.b31_60,b61_90:t.b61_90+r.b61_90,b90:t.b90+r.b90}),
    {open:0,b0_30:0,b31_60:0,b61_90:0,b90:0});
  return {asOf,side,rows,totals};
}
