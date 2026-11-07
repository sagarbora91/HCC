// F12-RPT-DAILY · rpt.money.allowances-voids (catalogue 1.6, meas.daily.adjustments). Discount / allowance / rebate lines, reversed lines (voids),
// issued bill corrections and refund receipts. Exception: an entry without a reason or without an approver; a user whose entries on one day add up
// to more than that user's discount limit (the daily cap). Server twin: Reports/MeasDaily/AllowancesVoidsReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {blcState} from '../../streams/bill-correction';
import {rbacDiscountLimit} from '../../streams/rbac';
import {bookingNo,rangeGuard,userLabel} from '../guests/shared';
import {T,M,D,EXC,inRange,CAPTURE_NOTE} from './shared';

export const ALLOWANCE_KINDS=['discount','allowance','rebate','void','correction','refund'];
export const ALLOWANCES_COLS:RptColumn[]=[D('date','Date'),T('kind','Kind'),T('bill','Bill'),M('amount','Amount'),T('reasonCode','Reason code'),T('user','User'),T('approver','Approver'),EXC()];
export const ALLOWANCES_KEY='meas.daily.adjustments';
type Entry={date:string;kind:string;bill:string;amount:number;reason:string;user:string;approver:string};
const tail=(label:string)=>{const i=label.indexOf(' · ');return i<0?'':label.slice(i+3).trim();};

export function allowanceEntries(s:State):Entry[]{
  const out:Entry[]=[];
  for(const b of s.bookings){
    const bill=bookingNo(b);
    for(const l of b.lines){
      if(l.kind==='discount'&&!l.reversalOf){
        const low=l.label.toLowerCase(),reason=tail(l.label);
        const ap=s.approvals.find(a=>a.booking===b.id&&a.kind===undefined&&(a.status==='Executed'||a.status==='Approved')&&a.reason===reason&&a.amount===-l.amount);
        out.push({date:l.date,kind:low.includes('rebate')?'rebate':low.includes('allowance')?'allowance':'discount',bill,amount:Math.abs(l.amount),reason,user:ap?.actor??'',approver:ap?.approver??''});
      }else if(l.reversalOf){
        const ap=l.approvalId?s.approvals.find(a=>a.id===l.approvalId):undefined;
        out.push({date:l.date,kind:l.kind==='adjustment'?'correction':'void',bill,amount:Math.abs(l.amount),reason:tail(l.label),user:ap?.actor??'',approver:ap?.approver??''});
      }
    }
    for(const r of b.receipts)if(r.kind==='refund'){
      const ap=(r as unknown as {approvalId?:string}).approvalId?s.approvals.find(a=>a.id===(r as unknown as {approvalId?:string}).approvalId):undefined;
      out.push({date:r.date,kind:'refund',bill,amount:r.amount,reason:r.reference??'',user:r.actor??'',approver:ap?.approver??''});
    }
  }
  for(const c of blcState(s).corrections)if(c.status==='Issued'){
    const b=s.bookings.find(x=>x.id===c.booking);
    out.push({date:(c.decidedAt??c.at).slice(0,10),kind:'correction',bill:c.newInvoiceNo??c.oldInvoiceNo,amount:Math.abs(c.delta),reason:c.reason,user:c.requestedBy,approver:c.approvedBy??''});
    void b;
  }
  return out;
}
export function allowanceRows(s:State,p:{from?:string;to?:string;kind?:string;user?:string}):RptRow[]{
  const all=allowanceEntries(s);
  const dayTotal=new Map<string,number>();
  for(const e of all)if(e.user)dayTotal.set(e.user+'|'+e.date,(dayTotal.get(e.user+'|'+e.date)??0)+e.amount);
  return all.filter(e=>inRange(e.date,p.from,p.to)&&(!p.kind||p.kind==='all'||e.kind===p.kind)&&(!p.user||e.user===p.user))
    .sort((a,b)=>a.date.localeCompare(b.date)||a.kind.localeCompare(b.kind)||a.bill.localeCompare(b.bill)||a.amount-b.amount)
    .map(e=>{
      const why:string[]=[];
      if(!e.reason.trim())why.push('No reason');
      if(!e.approver)why.push('No approver');
      const cap=e.user?rbacDiscountLimit(s,e.user):undefined;
      if(cap!==undefined&&(dayTotal.get(e.user+'|'+e.date)??0)>cap)why.push('User above the daily cap');
      return {kind:'row' as const,cells:{date:e.date,kind:e.kind,bill:e.bill,amount:e.amount,reasonCode:e.reason,user:userLabel(s,e.user),approver:userLabel(s,e.approver),exception:why.join('; ')}};
    });
}
registerMeasReport('rpt.money.allowances-voids',ALLOWANCES_KEY);
export const allowancesVoids=registerReport({
  id:'rpt.money.allowances-voids',title:'Allowances, rebates, voids and corrections',area:'money',priority:'P1',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'kind',label:'Kind',type:'enum',options:['all',...ALLOWANCE_KINDS],default:'all'},{key:'user',label:'User',type:'user'}],
  columns:ALLOWANCES_COLS,
  dateBasis:'Entries by the business date they were posted on (range up to 92 days): discount, allowance and rebate lines, reversed lines (voids), issued bill corrections and refunds. Amount is the absolute value before tax for lines, the net change for corrections. Exception: no reason, no approver, or a user above the daily cap (that user\'s discount limit).'+CAPTURE_NOTE('allowance reason codes (meas-cap-desk)'),
  formats:['screen','xlsx','pdf','csv'],phone:'no',
  select:(s,_uid,p)=>{rangeGuard(p.from,p.to,92,s.date);return {params:p,columns:ALLOWANCES_COLS,rows:allowanceRows(s,p)};},
});
