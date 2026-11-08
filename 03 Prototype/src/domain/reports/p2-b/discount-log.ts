// F8-RPT-P2-B · rpt.money.discount-log: every discount (stay folio discount lines and counter-bill discounts) with reason and approver, subtotal per reason.
// Reason = the discount-reason master name (code kept on the line) else the typed text. Approver = the approval that authorised it (line approvalId, else an
// approved discount request of the same booking and amount); a discount inside the poster's own limit has no approval, and the column is then blank.
// Server twin: Hcc.Reporting/Reports/P2B/DiscountLogReport.cs.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {catReason} from '../../streams/catalogue';
import {ctrState} from '../../streams/counter';
import {col,rangeParams,rowOf,userName,sumCells} from '../money/common';

export const DL_COLS=[col('date','Date','date'),col('ref','Booking or bill','text'),col('guest','Guest','text',false,{mask:'guestName'}),col('reason','Reason','text'),
  col('amount','Amount','money',true),col('approvedBy','Approved by','text')];
export type DiscountFact={date:string;ref:string;guest:string;reason:string;amount:number;approvedBy:string};
export function discountFacts(s:State,from:string,to:string):DiscountFact[]{
  const out:DiscountFact[]=[];
  for(const b of s.bookings)for(const l of b.lines){
    if(l.kind!=='discount'||l.date<from||l.date>to||l.reversalOf||b.lines.some(r=>r.reversalOf===l.id))continue;
    const a=(l.approvalId?s.approvals.find(x=>x.id===l.approvalId):undefined)
      ??s.approvals.find(x=>x.booking===b.id&&!x.kind&&(x.status==='Approved'||x.status==='Executed')&&x.amount===-l.amount);
    const name=l.discountReason?catReason(s,l.discountReason)?.name:undefined;
    out.push({date:l.date,ref:b.id,guest:b.guest,reason:name??l.label.replace(/^Discount · /,''),amount:-l.amount,approvedBy:a?.approver?userName(s,a.approver):''});
  }
  for(const c of ctrState(s).bills)if(c.discount&&c.date>=from&&c.date<=to)
    out.push({date:c.date,ref:c.no,guest:c.party.name,reason:c.discount.reason,amount:c.discount.amount,approvedBy:''});
  return out.sort((a,b)=>a.reason<b.reason?-1:a.reason>b.reason?1:a.date<b.date?-1:a.date>b.date?1:a.ref<b.ref?-1:1);
}
export const discountLog=registerReport({
  id:'rpt.money.discount-log',title:'Discounts given',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'reason',label:'Reason',type:'text'}],columns:DL_COLS,legacyIds:['C2.OtherDiscount'],
  dateBasis:'Discount lines posted on stays and discounts on counter bills, by posting date; a discount that was reversed is left out. Amount is the discount given (positive). Sorted by reason, then date, with a subtotal per reason. The summary by reason is in the plans and extras report.',
  formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,_u,p)=>{
    const q=String(p.reason??'').trim().toLowerCase();
    const facts=discountFacts(s,p.from!,p.to!).filter(f=>!q||f.reason.toLowerCase().includes(q));
    const rows=[];
    for(const r of [...new Set(facts.map(f=>f.reason))]){
      const g=facts.filter(f=>f.reason===r).map(f=>rowOf('row',{date:f.date,ref:f.ref,guest:f.guest,reason:f.reason,amount:f.amount,approvedBy:f.approvedBy}));
      rows.push(...g,rowOf('subtotal',{date:null,ref:null,guest:null,reason:'Subtotal · '+r,amount:sumCells(g,'amount'),approvedBy:null}));
    }
    if(facts.length)rows.push(rowOf('total',{date:null,ref:null,guest:null,reason:'Total discounts',amount:facts.reduce((a,f)=>a+f.amount,0),approvedBy:null}));
    return {params:p,columns:DL_COLS,rows};
  },
});
