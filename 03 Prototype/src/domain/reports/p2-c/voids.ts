// F8-RPT-P2-C · rpt.rest.voids — voided POS items and checks (control report). One row per voided line: when, check, item, qty, value (before tax), reason, who voided,
// who approved (a void above the limit is decided by a second user). Time = the void time of a whole-check void, else the void date. Adapter over the POS orders.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {posSlice,posLineValue} from '../../streams/pos';
import {col,rowOf,rangeParams,userName} from '../money/common';
import {cap92,stamp} from './shared';

const G='PERM-POS-004';
export const VOID_COLS:RptColumn[]=[col('time','Time','text'),col('check','Check','text'),col('item','Item','text'),col('qty','Qty','int'),col('value','Value','money',true),
  col('reason','Reason','text'),col('by','Voided by','text'),col('approvedBy','Approved by','text')];
export const posVoids=registerReport({
  id:'rpt.rest.voids',title:'POS voids',area:'rest',priority:'P2',status:'built',moneyFlag:true,moneyGate:G,viewPerms:[G],dateClass:'D-B',
  params:rangeParams('D-B'),columns:VOID_COLS,
  dateBasis:'Voided lines by the business date of the void, oldest first. Value = quantity x price before tax. Approved by is filled when the void needed a second user.',
  formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,_u,p)=>{
    cap92(p);
    const list:{time:string;check:string;item:string;qty:number;value:number;reason:string;by:string;appr:string;id:string}[]=[];
    for(const o of posSlice(s).orders)for(const l of o.lines){
      if(!l.voided||!l.voidDate||l.voidDate<p.from!||l.voidDate>p.to!)continue;
      const wholeBill=o.status==='Void'&&!!o.voidedAt&&o.voidedAt.slice(0,10)===l.voidDate;
      const a=l.voidApproval?s.approvals.find(x=>x.id===l.voidApproval):undefined;
      list.push({time:wholeBill?stamp(o.voidedAt!):l.voidDate,check:o.settlement?.billNo??o.id,item:l.name,qty:l.qty,value:posLineValue(l),
        reason:l.voidReason||o.voidReason||'',by:userName(s,l.voidBy),appr:a?.approver?userName(s,a.approver):'',id:o.id+'/'+l.id});}
    list.sort((a,b)=>a.time<b.time?-1:a.time>b.time?1:a.id<b.id?-1:a.id>b.id?1:0);
    const rows:RptRow[]=list.map(x=>rowOf('row',{time:x.time,check:x.check,item:x.item,qty:x.qty,value:x.value,reason:x.reason||null,by:x.by||null,approvedBy:x.appr||null}));
    if(rows.length)rows.push(rowOf('total',{time:'Total',check:null,item:null,qty:list.reduce((a,x)=>a+x.qty,0),value:list.reduce((a,x)=>a+x.value,0),reason:null,by:null,approvedBy:null}));
    return {params:p,columns:VOID_COLS,rows};
  },
});
