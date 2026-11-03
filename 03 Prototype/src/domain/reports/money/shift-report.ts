// F7-R4 · rpt.money.shift-report: shift report (X and Z) and cash hand-over. Adapter over the closing record (opening / expected / counted /
// difference), the closing report pack's drawer movements and the cash-out records (bank deposit / till transfer / Owner drawing = the hand-over).
// Prototype: one drawer shift per business date ("Day"); the open day shows as X (running), a closed day as Z.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {reportPack,reportDates} from '../../streams/closing';
import {cxSlice,cxDeskMovements,cxTillName} from '../../streams/cashout';
import {col,userName,rangeParams,seesAll,rowOf,sumCells} from './common';
import {shiftExpected} from '../../streams/closing';

export const SHIFT_COLS=[col('date','Date','date'),col('shift','Shift','text'),col('cashier','Cashier','text'),col('opening','Opening float','money',true),
  col('receipts','Receipts','money',true),col('payments','Payments','money',true),col('expected','Expected cash','money',true),col('counted','Counted','money',true),
  col('difference','Difference','money',true),col('handedTo','Handed over to','text'),col('handedAt','Hand-over time','text')];
export const SHIFT_CASHIER_COLS=[col('cashier','Cashier','text'),col('shifts','Shifts','int'),col('receipts','Receipts','money',true),col('payments','Payments','money',true),
  col('difference','Difference','money',true)];

/** Drawer receipts / payments of one business date, from the closing pack's own metrics (same rows as the daily collections tab). */
export function shiftRows(s:State,from:string,to:string){
  const pack=reportPack(s,from,to),ex=pack.tabs.find(t=>t.id==='expenses')!;
  const m=(id:string)=>ex.metrics.find(x=>x.id===id)!;
  const by=(id:string,d:string)=>m(id).rows.filter(r=>r.date===d).reduce((a,r)=>a+r.amount,0);
  const x=cxSlice(s);
  return reportDates(from,to).flatMap(d=>{
    const c=s.closings?.[d],open=d===s.date&&!s.closedDates.includes(d);
    if(!c&&!open)return [];
    const net=cxDeskMovements(s,d).reduce((a,v)=>a+v.amount,0);
    const receipts=by('cash-in',d),payments=-by('cash-out',d)+by('cash',d)-net;
    const hand=x.records.filter(r=>r.date===d&&r.fromTill===x.desk&&r.status!=='Rejected')[0];
    const handedTo=hand?(hand.kind==='banking'?`Bank · ${hand.bank??''}`.trim():hand.kind==='till'?cxTillName(s,hand.toTill):'Owner (drawing)'):
      c?.cash.banked?'Bank (banked after the count)':'';
    const handedAt=hand?hand.at:c?.cash.banked?c.closedAt:'';
    if(c)return [{date:d,shift:'Day (Z)',cashier:c.cash.countedBy??c.closedBy,opening:c.cash.opening,receipts,payments,expected:c.cash.expected,counted:c.cash.counted,difference:c.cash.difference,handedTo,handedAt}];
    const e=shiftExpected(s),cnt=s.shiftClose?.counted;
    return [{date:d,shift:'Day (X, open)',cashier:s.shift?.submittedBy??x.opener?.by??'',opening:s.openingCash,receipts,payments,expected:e.expected,counted:cnt??null,
      difference:cnt===undefined?null:cnt-e.expected,handedTo,handedAt}];
  });
}

export const shiftReport=registerReport({
  id:'rpt.money.shift-report',title:'Shift report (X and Z) and cash hand-over',area:'money',priority:'P1',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',
  viewPerms:['PERM-RPT-002'],ownShift:true,dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'cashier',label:'Cashier',type:'user'},{key:'shift',label:'Shift',type:'enum',options:['all','Day'],default:'all'}],
  columns:SHIFT_COLS,dateBasis:'Per business date: opening float and counted cash from the shift count, receipts and payments from the drawer movements (cash in / cash out / cash expenses / bank and till hand-overs), expected = opening + receipts - payments. The open day is a running X reading; a closed day is the Z reading.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,uid,p)=>{
    const all=seesAll(s,uid);
    let rows=shiftRows(s,p.from!,p.to!).filter(r=>(all||r.cashier===uid)&&(!p.cashier||r.cashier===p.cashier));
    if(p.shift&&p.shift!=='all')rows=rows.filter(r=>r.shift.startsWith(String(p.shift)));
    const cells=(r:typeof rows[number])=>({...r,cashier:userName(s,r.cashier)});
    const body=rows.map(r=>rowOf('row',cells(r)));
    const tot=rowOf('total',{date:null,shift:'Total',cashier:null,opening:sumCells(body,'opening'),receipts:sumCells(body,'receipts'),payments:sumCells(body,'payments'),
      expected:sumCells(body,'expected'),counted:sumCells(body,'counted'),difference:sumCells(body,'difference'),handedTo:null,handedAt:null});
    const names=[...new Set(rows.map(r=>r.cashier))];
    const per=names.map(n=>{const g=rows.filter(r=>r.cashier===n);return rowOf('row',{cashier:userName(s,n),shifts:g.length,receipts:g.reduce((a,r)=>a+r.receipts,0),payments:g.reduce((a,r)=>a+r.payments,0),difference:g.reduce((a,r)=>a+(r.difference??0),0)});});
    const sections=[{key:'shifts',title:'Shifts',columns:SHIFT_COLS,rows:[...body,...(body.length?[tot]:[])]},
      {key:'cashiers',title:'Totals per cashier',columns:SHIFT_CASHIER_COLS,rows:per}];
    return {params:p,columns:sections[0].columns,rows:sections[0].rows,sections};
  },
});
