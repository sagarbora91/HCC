// F7-R8 · the closing ReportPack tabs as engine reports: daily operations, occupancy, receipts and deposits, expenses and cash.
// Adapter only: every figure is read from `reportPack(s, from, to)` (the same function the old Reports page calls); totals are the tab's tile values.
// Old page (features/reports/Reports.tsx) keeps its UI and its own export path until the integrator switches the entry point (F7-CONTRACT §5).
import type {State} from '../../types';
import type {RptColumn,RptRow,RptBoundParams} from '../types';
import {registerReport} from '../catalogue';
import {reportPack,type ReportTab} from '../../streams/closing';
import {col,rowOf,rangeParams} from '../money/common';

const tabOf=(s:State,p:RptBoundParams,id:string):ReportTab=>reportPack(s,p.from!,p.to!).tabs.find(t=>t.id===id)!;
const met=(t:ReportTab,id:string)=>t.metrics.find(m=>m.id===id)?.value??0;
const GATE='PERM-RPT-002';
const FIN={moneyFlag:true as const,moneyGate:GATE,viewPerms:[GATE],dateClass:'D-F' as const,formats:['screen','xlsx','pdf','csv'] as ('screen'|'xlsx'|'pdf'|'csv')[],phone:'yes' as const};

export const DAILY_OPS_COLS:RptColumn[]=[col('date','Business date','date'),col('status','Day status','text'),col('arrivals','Arrivals','int'),col('departures','Departures','int'),
  col('occupied','Occupied room-nights','int'),col('netRevenue','Net revenue','money',true),col('netReceipts','Net receipts','money',true),col('expenses','Expenses','money',true)];
export const dailyOps=registerReport({serverMirror:false,
  id:'rpt.guests.daily-ops',title:'Daily operations',area:'guests',priority:'P1',status:'built',...FIN,params:rangeParams('D-F'),
  columns:DAILY_OPS_COLS,legacyIds:[],
  dateBasis:'Charges by service date, receipts by receipt date, expenses by business date (the closing report pack, tab Daily operations). Total row = the tab tiles.',
  select:(s,_u,p)=>{
    const t=tabOf(s,p,'daily');
    const rows:RptRow[]=t.table!.rows.map(r=>rowOf('row',{date:String(r[0]),status:String(r[1]),arrivals:Number(r[2]),departures:Number(r[3]),occupied:Number(r[4]),netRevenue:Number(r[5]),netReceipts:Number(r[6]),expenses:Number(r[7])}));
    rows.push(rowOf('total',{date:'Total',status:null,arrivals:met(t,'arrivals'),departures:met(t,'departures'),occupied:met(t,'occupied'),netRevenue:met(t,'net-revenue'),netReceipts:met(t,'net-receipts'),expenses:met(t,'expenses')}));
    return {params:p,columns:DAILY_OPS_COLS,rows};
  },
});

export const OCCUPANCY_COLS:RptColumn[]=[col('date','Business date','date'),col('sellable','Sellable','int'),col('occupied','Occupied','int'),col('occupancy','Occupancy','text'),
  col('roomRevenue','Room revenue','money',true),col('adr','ADR','text',true),col('revpar','RevPAR','text',true)];
export const occupancyReport=registerReport({serverMirror:false,
  id:'rpt.rooms.occupancy',title:'Occupancy, ADR and RevPAR',area:'rooms',priority:'P1',status:'built',...FIN,params:rangeParams('D-F'),
  columns:OCCUPANCY_COLS,legacyIds:['A35'],
  dateBasis:'Occupancy = occupied ÷ sellable room-nights; ADR = room revenue ÷ occupied; RevPAR = room revenue ÷ sellable (rounded half-up, dash when the divisor is zero). The total row recomputes the ratios from the totals.',
  select:(s,_u,p)=>{
    const t=tabOf(s,p,'occupancy');
    const rows:RptRow[]=t.table!.rows.map(r=>rowOf('row',{date:String(r[0]),sellable:Number(r[1]),occupied:Number(r[2]),occupancy:String(r[3]),roomRevenue:Number(r[4]),adr:String(r[5]),revpar:String(r[6])}));
    const ra=(id:string)=>t.ratios.find(x=>x.id===id)!.display;
    rows.push(rowOf('total',{date:'Total',sellable:met(t,'sellable'),occupied:met(t,'occupied'),occupancy:ra('occupancy'),roomRevenue:met(t,'room-revenue'),adr:ra('adr'),revpar:ra('revpar')}));
    return {params:p,columns:OCCUPANCY_COLS,rows};
  },
});

/** One column per tender is dynamic (the tenders found in the range); the definition lists the fixed ones. */
export const RECEIPTS_COLS:RptColumn[]=[col('date','Receipt date','date'),col('net','Net receipts','money',true),col('depositsIn','Deposits in','money',true),col('depositsOut','Deposits out','money',true)];
export const receiptsDeposits=registerReport({serverMirror:false,
  id:'rpt.money.receipts-deposits',title:'Receipts and deposits',area:'money',priority:'P1',status:'built',...FIN,params:rangeParams('D-F'),
  columns:RECEIPTS_COLS,legacyIds:['C2.PreviousAdvance'],
  dateBasis:'Payments minus refunds by receipt date, one column per tender; deposits received and returned by receipt date. Totals per tender; deposits held now is a point-in-time figure in the second block.',
  select:(s,_u,p)=>{
    const t=tabOf(s,p,'receipts');const tenders=t.table!.columns.slice(1,-3);
    const cols:RptColumn[]=[RECEIPTS_COLS[0],...tenders.map(n=>col('t:'+n,n,'money',true)),...RECEIPTS_COLS.slice(1)];
    const rows:RptRow[]=t.table!.rows.map(r=>{const c:Record<string,string|number|null>={date:String(r[0])};tenders.forEach((n,i)=>{c['t:'+n]=Number(r[1+i]);});
      c.net=Number(r[1+tenders.length]);c.depositsIn=Number(r[2+tenders.length]);c.depositsOut=Number(r[3+tenders.length]);return rowOf('row',c);});
    const tc:Record<string,string|number|null>={date:'Total'};tenders.forEach(n=>{tc['t:'+n]=met(t,'tender:'+n);});
    tc.net=met(t,'net');tc.depositsIn=met(t,'deposit-in');tc.depositsOut=met(t,'deposit-out');rows.push(rowOf('total',tc));
    const pit:RptColumn[]=[col('item','Point in time','text'),col('amount','Amount','money',true)];
    const sections=[{key:'days',title:'By receipt date',columns:cols,rows},
      {key:'now',title:'Credit and deposits held',columns:pit,rows:[rowOf('row',{item:'Company credit (city ledger) in the range',amount:met(t,'company-credit')}),rowOf('row',{item:'Deposits held now · all open folios',amount:met(t,'held')})]}];
    return {params:p,columns:cols,rows,sections};
  },
});

export const EXPCASH_COLS:RptColumn[]=[col('date','Business date','date'),col('day','Day','text'),col('opening','Opening float','money',true),col('expected','Expected cash','money',true),
  col('counted','Counted','money',true),col('difference','Difference','money',true),col('cashExpenses','Cash expenses','money',true)];
export const expensesCash=registerReport({serverMirror:false,
  id:'rpt.money.expenses-cash',title:'Expenses and cash',area:'money',priority:'P1',status:'built',...FIN,params:rangeParams('D-F'),
  columns:EXPCASH_COLS,
  dateBasis:'Expenses by business date, split by what they settle to; drawer opening, expected, counted and difference from the closing record (open day: the live shift). Cash in / out by receipt date.',
  select:(s,_u,p)=>{
    const t=tabOf(s,p,'expenses');
    const num=(v:string|number)=>v===''?null:Number(v);
    const rows:RptRow[]=t.table!.rows.map(r=>rowOf('row',{date:String(r[0]),day:String(r[1]),opening:num(r[2]),expected:num(r[3]),counted:num(r[4]),difference:num(r[5]),cashExpenses:Number(r[6])}));
    rows.push(rowOf('total',{date:'Total',day:null,opening:null,expected:null,counted:null,difference:null,cashExpenses:met(t,'cash')}));
    const tiles:RptColumn[]=[col('tile','Figure','text'),col('amount','Amount','money',true)];
    const sections=[{key:'days',title:'By business date',columns:EXPCASH_COLS,rows},
      {key:'tiles',title:'Totals for the range',columns:tiles,rows:t.metrics.map(m=>rowOf('row',{tile:m.label,amount:m.value}))}];
    return {params:p,columns:EXPCASH_COLS,rows,sections};
  },
});
