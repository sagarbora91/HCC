// F8-RPT-P2-B · rpt.money.counter-bills: walk-in bills without a room (LF A7, ctr.bill / ctr.bill_line). One row per bill line, the bill discount as its
// own negative row, a subtotal per bill (net, GST, tender) and a grand total. Adapter over ctrState; no figure of its own.
// Server twin: Hcc.Reporting/Reports/P2B/CounterBillsReport.cs.
import type {State,CTRBill} from '../../types';
import {registerReport} from '../catalogue';
import {ctrState} from '../../streams/counter';
import {col,rangeParams,rowOf} from '../money/common';

export const CB_COLS=[col('date','Date','date'),col('billNo','Bill no','text'),col('customer','Customer','text',false,{mask:'guestName'}),col('item','Item','text'),col('qty','Qty','int'),
  col('rate','Rate','money',true),col('amount','Amount','money',true),col('tax','Tax','money',true),col('tender','Tender','text')];
export const ctrTender=(b:CTRBill)=>(b.settlement.mode==='paid'?(b.settlement.method??'Paid'):b.settlement.mode==='udhaar'?'Udhaar (credit)':'Company credit')+(b.status==='Credited'?' · credited '+(b.creditNoteNo??''):'');
export function counterBillRows(s:State,from:string,to:string){
  const bills=[...ctrState(s).bills].filter(b=>b.date>=from&&b.date<=to).sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.no<b.no?-1:1);
  const rows=bills.flatMap(b=>{
    const base={date:b.date,billNo:b.no,customer:b.party.name};
    const out=b.lines.map(l=>rowOf('row',{...base,item:l.label,qty:l.qty,rate:l.unit,amount:l.amount,tax:null,tender:null}));
    if(b.discount)out.push(rowOf('row',{...base,item:'Discount · '+b.discount.reason,qty:null,rate:null,amount:-b.discount.amount,tax:null,tender:null}));
    out.push(rowOf('subtotal',{date:null,billNo:b.no,customer:null,item:'Bill total',qty:null,rate:null,amount:b.net,tax:b.tax,tender:ctrTender(b)}));
    return out;
  });
  if(bills.length)rows.push(rowOf('total',{date:null,billNo:null,customer:null,item:'Grand total',qty:null,rate:null,amount:bills.reduce((a,b)=>a+b.net,0),tax:bills.reduce((a,b)=>a+b.tax,0),tender:null}));
  return rows;
}
export const counterBills=registerReport({
  id:'rpt.money.counter-bills',title:'Counter bills',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:rangeParams('D-B'),columns:CB_COLS,legacyIds:['A39','C2.DirectBillSummary','C4.DirectBillDoc'],
  dateBasis:'Walk-in (counter) bills by bill date. Amount = net of the line (a bill discount is its own row); Tax and Tender are on the bill subtotal. A credited bill stays listed with its credit note. A counter bill is the old software\'s direct bill, not company credit.',
  formats:['screen','xlsx','pdf','csv'],phone:'no',
  select:(s:State,_u,p)=>({params:p,columns:CB_COLS,rows:counterBillRows(s,p.from!,p.to!)}),
});
