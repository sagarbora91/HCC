// F8-RPT-P2-B · rpt.money.refunds: refunds paid to guests (receipt kind 'refund' of a stay). Reason and approver come from the approval the refund executed.
// Server twin: Hcc.Reporting/Reports/P2B/RefundsReport.cs.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {reportReceipts} from '../../streams/closing';
import {col,rangeParams,rowOf,userName,sumCells} from '../money/common';

export const RF_COLS=[col('date','Date','date'),col('refundNo','Refund no','text'),col('guest','Guest','text',false,{mask:'guestName'}),col('billNo','Bill no','text'),
  col('amount','Amount','money',true),col('tender','Tender','text'),col('reason','Reason','text'),col('approvedBy','Approved by','text')];
export function refundRows(s:State,from:string,to:string){
  return reportReceipts(s,from,to).filter(x=>x.r.kind==='refund').sort((a,b)=>a.r.date<b.r.date?-1:a.r.date>b.r.date?1:a.r.id<b.r.id?-1:1).map(({b,r})=>{
    const a=r.approvalId?s.approvals.find(x=>x.id===r.approvalId):undefined;
    return rowOf('row',{date:r.date,refundNo:r.id,guest:b.guest,billNo:b.invoiceNo??b.id,amount:r.amount,tender:r.method,reason:a?.reason??r.reference,approvedBy:a?.approver?userName(s,a.approver):''});
  });
}
export const refunds=registerReport({
  id:'rpt.money.refunds',title:'Refunds',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:rangeParams('D-B'),columns:RF_COLS,legacyIds:['A41'],
  dateBasis:'Refund receipts of stays by receipt date (the refund of a payment, DEC-031). Reason and approver are those of the approval the refund executed; deposit returns are not refunds and are in the collection detail.',
  formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,_u,p)=>{const rows=refundRows(s,p.from!,p.to!);
    if(rows.length)rows.push(rowOf('total',{date:null,refundNo:'Total refunded',guest:null,billNo:null,amount:sumCells(rows,'amount'),tender:null,reason:null,approvedBy:null}));
    return {params:p,columns:RF_COLS,rows};},
});
