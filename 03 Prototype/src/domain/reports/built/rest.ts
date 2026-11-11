// F7-R8 · restaurant reports on the engine: outlet day summary (posDaySummary) and closed bills. Cashier own-shift view applies to closed bills only
// (their own settlements); the day summary is the whole outlet, so it is Owner / Manager / Accountant (catalogue: Cashier own shift; narrower here, never wider).
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {posDaySummary,posSlice,posActiveLines,posSpot,posOutlet} from '../../streams/pos';
import {rptHolds} from '../mask';
import {col,rowOf,userName} from '../money/common';

const GATE='PERM-POS-004';
const dayParam=[{key:'from',label:'Business date',type:'date' as const,dateClass:'D-DAY' as const}];

export const REST_OUTLET_COLS:RptColumn[]=[col('outlet','Outlet','text'),col('bills','Bills','int'),col('net','Net','money',true),col('tax','Tax','money',true),col('total','Total','money',true),col('open','Open checks','int')];
export const REST_SETTLE_COLS:RptColumn[]=[col('settlement','Settlement','text'),col('amount','Amount','money',true)];
export const REST_TOP_COLS:RptColumn[]=[col('item','Top item','text'),col('qty','Item qty','int'),col('value','Item value','money',true)];
export const REST_OPEN_COLS:RptColumn[]=[col('open','Open checks','text'),col('count','Checks','int'),col('value','Value','money',true)];
export const restDaySummary=registerReport({
  id:'rpt.rest.day-summary',title:'Outlet day summary',area:'rest',priority:'P1',status:'built',moneyFlag:true,moneyGate:GATE,viewPerms:[GATE],dateClass:'D-DAY',params:dayParam,
  columns:REST_OUTLET_COLS,
  dateBasis:'Settled checks by settlement date; open checks as of now. Posted to room folios = checks settled to a stay (a charge, not a receipt).',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_u,p)=>{
    const d=posDaySummary(s,p.from!);
    const rows:RptRow[]=d.byOutlet.map(o=>rowOf('row',{outlet:o.name,bills:o.checks,net:o.net,tax:o.tax,total:o.gross,open:o.open}));
    rows.push(rowOf('total',{outlet:'All outlets',bills:d.checks,net:d.net,tax:d.tax,total:d.gross,open:d.open.count}));
    const set:RptRow[]=Object.entries(d.byMode).map(([k,v])=>rowOf('row',{settlement:k==='Room folio'?'Posted to room folios':k,amount:v}));
    const top:RptRow[]=d.top.map(t=>rowOf('row',{item:t.name,qty:t.qty,value:t.value}));
    const open:RptRow[]=[rowOf('row',{open:'Open checks now',count:d.open.count,value:d.open.value}),rowOf('row',{open:'Voided lines today',count:d.voids.count,value:d.voids.value})];
    return {params:p,columns:REST_OUTLET_COLS,rows,sections:[{key:'outlets',title:'By outlet',columns:REST_OUTLET_COLS,rows},{key:'settlement',title:'By settlement',columns:REST_SETTLE_COLS,rows:set},
      {key:'top',title:'Top items',columns:REST_TOP_COLS,rows:top},{key:'open',title:'Open checks',columns:REST_OPEN_COLS,rows:open}]};
  },
});

export const CLOSED_COLS:RptColumn[]=[col('check','Check','text'),col('outlet','Outlet','text'),col('table','Table','text'),col('closedAt','Closed at','text'),col('items','Items','int'),
  col('total','Total','money',true),col('tender','Tender','text'),col('server','Server','text')];
export const restClosedBills=registerReport({serverMirror:false,
  id:'rpt.rest.closed-bills',title:'Closed bills',area:'rest',priority:'P2',status:'built',moneyFlag:true,moneyGate:GATE,viewPerms:[GATE],ownShift:true,dateClass:'D-DAY',params:dayParam,columns:CLOSED_COLS,
  dateBasis:'Checks settled on the business date, in closing order. A Cashier sees only the bills they settled.',formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,uid,p)=>{
    const all=rptHolds(s,uid,GATE,false);
    const rows:RptRow[]=posSlice(s).orders.filter(o=>o.status==='Settled'&&o.settlement?.date===p.from&&(all||o.settlement!.by===uid))
      .sort((a,b)=>a.settlement!.at.localeCompare(b.settlement!.at)||a.id.localeCompare(b.id))
      .map(o=>rowOf('row',{check:o.settlement!.invoiceNo??o.settlement!.billNo,outlet:posOutlet(s,o.outlet)?.name??o.outlet,table:posSpot(o),closedAt:o.settlement!.at,items:posActiveLines(o).reduce((a,l)=>a+l.qty,0),
        total:o.settlement!.total,tender:o.settlement!.mode==='folio'?'Room folio':o.settlement!.method??null,server:userName(s,o.openedBy)}));
    if(rows.length)rows.push(rowOf('total',{check:'Total',outlet:null,table:null,closedAt:null,items:rows.reduce((a,r)=>a+Number(r.cells.items),0),total:rows.reduce((a,r)=>a+Number(r.cells.total),0),tender:null,server:null}));
    return {params:p,columns:CLOSED_COLS,rows};
  },
});
