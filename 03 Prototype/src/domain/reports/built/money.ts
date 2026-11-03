// F7-R8 · money reports moved onto the engine: cash book, earlier closing records, banking / transfers / drawings register, expenses month.
// Adapters over cbRange / the closing records / cxSlice / expMonthSummary — no figure of their own.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {cbRange} from '../../streams/cashbook';
import {cxSlice,cxTillName} from '../../streams/cashout';
import {expMonthSummary} from '../../streams/expenses-scc';
import {expenseCounts} from '../../streams/closing';
import {col,rowOf,rangeParams,userName} from '../money/common';

const GATE='PERM-RPT-002';

export const CASHBOOK_COLS:RptColumn[]=[col('date','Business date','date'),col('status','Status','text'),col('opening','Opening','money',true),col('receipts','Receipts','money',true),
  col('payments','Payments (incl. banking)','money',true),col('closing','Closing (book)','money',true),col('counted','Counted','money',true),col('overShort','Over / short','money',true)];
export const CASHBOOK_ENTRY_COLS:RptColumn[]=[col('date','Business date','date'),col('ref','Reference','text'),col('entry','Entry','text'),col('detail','Detail','text'),col('booking','Booking','text'),
  col('guest','Guest','text',false,{mask:'guestName'}),col('in','Receipt','money',true),col('out','Payment','money',true),col('balance','Balance','money',true),col('by','By','text')];
export const cashBook=registerReport({serverMirror:false,
  id:'rpt.money.cash-book',title:'Cash book',area:'money',priority:'P1',status:'built',moneyFlag:true,moneyGate:GATE,viewPerms:[GATE],dateClass:'D-S',
  params:rangeParams('D-F').map(p=>({...p,dateClass:'D-S' as const})),columns:CASHBOOK_COLS,
  dateBasis:'One row per known business date: opening = the closing float, receipts and payments = the cashier-shift movements of that date (plus banking after the count), closing = book balance, counted and over / short from the closing record. Second block: every entry with the running balance.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_u,p)=>{
    const r=cbRange(s,p.from!,p.to!);
    const rows:RptRow[]=r.days.map(d=>rowOf('row',{date:d.date,status:d.status+(d.locked?' · locked':''),opening:d.opening,receipts:d.receipts,payments:d.payments,closing:d.closing,counted:d.counted??null,overShort:d.difference??null}));
    if(r.days.length)rows.push(rowOf('total',{date:'Range total',status:null,opening:r.opening,receipts:r.receipts,payments:r.payments,closing:r.closing,counted:null,overShort:r.differences}));
    const entries:RptRow[]=[];
    for(const d of r.days){let bal=d.opening;entries.push(rowOf('row',{date:d.date,ref:null,entry:'Opening balance',detail:null,booking:null,guest:null,in:null,out:null,balance:bal,by:null}));
      for(const e of d.entries){bal+=e.in-e.out;entries.push(rowOf('row',{date:d.date,ref:e.id,entry:e.label,detail:e.reference,booking:e.booking??null,guest:e.guest??null,in:e.in||null,out:e.out||null,balance:bal,by:e.actor}));}}
    return {params:p,columns:CASHBOOK_COLS,rows,sections:[{key:'days',title:'By business date',columns:CASHBOOK_COLS,rows},{key:'entries',title:'Entries with running balance',columns:CASHBOOK_ENTRY_COLS,rows:entries}]};
  },
});

export const CLOSING_COLS:RptColumn[]=[col('date','Business date','date'),col('closedBy','Closed by','text'),col('closedAt','Closed at','text'),col('netRevenue','Net revenue','money',true),
  col('netReceipts','Net receipts','money',true),col('cashDifference','Cash difference','money',true),col('corrections','Corrections','int')];
export const closingRecords=registerReport({serverMirror:false,
  id:'rpt.money.closing-records',title:'Earlier closing records',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:GATE,viewPerms:['PERM-CLS-001'],dateClass:'D-FY',
  params:[{key:'fy',label:'Financial year',type:'fy',dateClass:'D-FY'}],columns:CLOSING_COLS,
  dateBasis:'One row per closed business date, newest first, from the stored closing record. Corrections = how many times the Owner reopened that date.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_u,p)=>{
    const rows:RptRow[]=Object.values(s.closings??{}).filter(c=>c.date>=p.from!&&c.date<=p.to!).sort((a,b)=>b.date.localeCompare(a.date))
      .map(c=>rowOf('row',{date:c.date,closedBy:userName(s,c.closedBy),closedAt:c.closedAt,netRevenue:c.revenue.net,netReceipts:c.receipts.net,cashDifference:c.cash.difference,corrections:c.reopens?.length??0}));
    return {params:p,columns:CLOSING_COLS,rows};
  },
});

export const CASHOUT_COLS:RptColumn[]=[col('date','Date','date'),col('kind','Kind','text'),col('amount','Amount','money',true),col('bankSlip','Bank and slip','text'),col('fromTill','From till','text'),
  col('toTill','To till','text'),col('reason','Reason','text'),col('status','Status','text'),col('decidedBy','Decided by','text')];
const KIND:Record<string,string>={banking:'bank deposit',till:'till transfer',drawing:'owner drawing'};
export const cashOutRegister=registerReport({serverMirror:false,
  id:'rpt.money.cash-out-register',title:'Banking, transfers and drawings',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:GATE,viewPerms:[GATE],dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'kind',label:'Kind',type:'enum',options:['all','bank deposit','till transfer','owner drawing'],default:'all'}],columns:CASHOUT_COLS,
  dateBasis:'Cash-out records by the business date the cash left the drawer. The slip photo is never in the report. Totals per kind leave out rejected records.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_u,p)=>{
    const want=String(p.kind??'all');
    const recs=cxSlice(s).records.filter(r=>r.date>=p.from!&&r.date<=p.to!&&(want==='all'||KIND[r.kind]===want)).sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.at.localeCompare(b.at));
    const rows:RptRow[]=recs.map(r=>rowOf('row',{date:r.date,kind:KIND[r.kind],amount:r.amount,bankSlip:[r.bank,r.slip].filter(Boolean).join(' · ')||null,fromTill:cxTillName(s,r.fromTill),
      toTill:r.toTill?cxTillName(s,r.toTill):null,reason:r.reason??null,status:r.status,decidedBy:r.decidedBy?userName(s,r.decidedBy):null}));
    const kc:RptColumn[]=[col('kind','Kind','text'),col('count','Records','int'),col('amount','Amount','money',true)];
    const per=Object.entries(KIND).map(([k,label])=>{const x=recs.filter(r=>r.kind===k&&r.status!=='Rejected');return rowOf('row',{kind:label,count:x.length,amount:x.reduce((a,r)=>a+r.amount,0)});}).filter(r=>want==='all'||r.cells.kind===want);
    return {params:p,columns:CASHOUT_COLS,rows,sections:[{key:'records',title:'Records',columns:CASHOUT_COLS,rows},{key:'kinds',title:'Totals per kind',columns:kc,rows:per}]};
  },
});

export const EXPMONTH_COLS:RptColumn[]=[col('head','Head','text'),col('amount','Amount','money',true),col('entries','Entries','int'),col('gstPaid','GST paid','money',true),col('itcAllowed','ITC allowed','money',true),col('itcDisallowed','ITC disallowed','money',true)];
export const expenseMonth=registerReport({serverMirror:false,
  id:'rpt.money.expense-month',title:'Expenses month and tax feed',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:GATE,viewPerms:[GATE],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'}],columns:EXPMONTH_COLS,
  dateBasis:'The month\'s counted expenses (the tax-feed figures of expMonthSummary), by head; GST paid and ITC allowed are month totals, shown on the total row.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_u,p)=>{
    const m=expMonthSummary(s,String(p.month));
    const rows:RptRow[]=Object.entries(m.byCategory).sort((a,b)=>a[0].localeCompare(b[0])).map(([head,amount])=>
      rowOf('row',{head,amount,entries:s.expenses.filter(e=>expenseCounts(e)&&e.category===head&&e.date.slice(0,7)===p.month).length,gstPaid:null,itcAllowed:null,itcDisallowed:null}));
    rows.push(rowOf('total',{head:'Total',amount:m.expense,entries:m.count,gstPaid:m.gstPaid,itcAllowed:m.gstItc,itcDisallowed:m.gstPaid-m.gstItc}));
    return {params:p,columns:EXPMONTH_COLS,rows};
  },
});
