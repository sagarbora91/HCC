// DEC-130 (owner, 27 Oct 2026): an Udhaar (customer credit) payment is a desk receipt. Taken in cash it goes into the open shift's drawer, so
// the cashier's count, the day close and the cash book include it; other modes (UPI / card / bank) go to their own ledgers like other
// receipts. In the books it settles the receivable (Dr the tender · Cr Udhaar receivables) — never income a second time.
// Pure LEAF module (only type imports + the money-master leaf): the drawer selector and the shift movements read it, so it must not import
// closing.ts or expenses-scc.ts at run time. Settlements recorded before DEC-130 have no `desk` flag and never reached the drawer or the books.
import type {State} from '../types';
import type {ExpUdReceivable,ExpUdSettlement} from './expenses-scc';
import {mnyIsDrawer} from './money-master';

/** Udhaar payments taken as desk receipts on business date `d` (every mode), with their credit. */
export function expUdDeskReceipts(s:State,d:string):{r:ExpUdReceivable;p:ExpUdSettlement}[]{
  const out:{r:ExpUdReceivable;p:ExpUdSettlement}[]=[];
  for(const r of s.expensesX?.udhaar??[])for(const p of r.settlements??[])if(p.desk&&p.date===d)out.push({r,p});
  return out;
}
/** Cash of those receipts (methods that settle to a till): part of the drawer and expected cash. */
export const expUdDeskCash=(s:State,d:string)=>expUdDeskReceipts(s,d).filter(x=>mnyIsDrawer(s,x.p.mode)).reduce((a,x)=>a+x.p.amount,0);
/** The drawer movements of `d` (kind 'payment' = cash in, as the server's pay.cash_movement with source type 'udhaar'). */
export function expUdDeskMovements(s:State,d:string):{id:string;kind:string;label:string;guest?:string;reference:string;amount:number;actor:string}[]{
  return expUdDeskReceipts(s,d).filter(x=>mnyIsDrawer(s,x.p.mode))
    .map(({r,p})=>({id:p.id,kind:'payment',label:'Udhaar received · '+r.name,guest:r.name,reference:p.reference||'Udhaar '+r.id,amount:p.amount,actor:p.by}));
}
/** Dates with Udhaar desk receipts (Accounting's date list). */
export function expUdDeskDates(s:State):string[]{
  const d=new Set<string>();for(const r of s.expensesX?.udhaar??[])for(const p of r.settlements??[])if(p.desk)d.add(p.date);return [...d];
}
