// F8-RPT-P2-C · rpt.admin.cancelled-lines — items and charges removed from guest bills: every reversal line (WF-FOLIO-002) with the original line it cancels.
// Credit notes are not cancellations (they are in the credit-note register). Split-folio commands write no money rows (SF-CONTRACT R7), so nothing from them appears.
// Qty is 1: a folio line carries no quantity. Cancelled by = the user who requested the correction (the Owner approved it). Guest names are not a column.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {col,rowOf,rangeParams,userName} from '../money/common';
import {cap92} from './shared';

const G='PERM-RPT-002';
export const CANCELLED_COLS:RptColumn[]=[col('date','Date','date'),col('booking','Booking','text'),col('room','Room','text'),col('item','Item','text'),col('qty','Qty','int'),
  col('amount','Amount','money',true),col('by','Cancelled by','text'),col('reason','Reason','text')];
export const cancelledLines=registerReport({
  id:'rpt.admin.cancelled-lines',title:'Cancelled folio lines',area:'admin',priority:'P2',status:'built',moneyFlag:true,moneyGate:G,viewPerms:[G],dateClass:'D-B',
  params:rangeParams('D-B'),columns:CANCELLED_COLS,legacyIds:['C1.CancelItem'],
  dateBasis:'Reversal lines by the date they were posted (the cancelled line is shown with its own amount before tax), newest first.',
  formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,_u,p)=>{
    cap92(p);
    const list:{date:string;id:string;booking:string;room:string;item:string;amount:number;by:string;reason:string}[]=[];
    for(const b of s.bookings)for(const r of b.lines){
      if(!r.reversalOf||r.date<p.from!||r.date>p.to!)continue;
      const orig=b.lines.find(x=>x.id===r.reversalOf);if(!orig)continue;
      const a=r.approvalId?s.approvals.find(x=>x.id===r.approvalId):undefined;
      list.push({date:r.date,id:r.id,booking:b.id,room:b.room,item:orig.label,amount:Math.abs(orig.amount),by:a?userName(s,a.actor):'',reason:a?.reason??''});}
    list.sort((a,b)=>a.date<b.date?1:a.date>b.date?-1:a.id<b.id?1:a.id>b.id?-1:0);
    const rows:RptRow[]=list.map(x=>rowOf('row',{date:x.date,booking:x.booking,room:x.room||null,item:x.item,qty:1,amount:x.amount,by:x.by||null,reason:x.reason||null}));
    if(rows.length)rows.push(rowOf('total',{date:null,booking:'Total',room:null,item:null,qty:list.length,amount:list.reduce((a,x)=>a+x.amount,0),by:null,reason:null}));
    return {params:p,columns:CANCELLED_COLS,rows};
  },
});
