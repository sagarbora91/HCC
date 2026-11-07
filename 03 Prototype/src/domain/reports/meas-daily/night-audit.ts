// F12-RPT-DAILY · night-audit auto-tick lines and the optional close block (MEAS-CONTRACT §1.4, §8). Pure functions of the state:
// F8 can adopt measNightAuditLines later (no dependency on F8). An item that is switched off is skipped, never failed.
// meas.daily.blockClose (default off) is the ONLY way a measurement can block a close.
import type {State} from '../../types';
import {measOn} from '../../streams/measure';
import {isRed} from './shared';
import {flashRows} from './flash';
import {rateVarianceRows} from './rate-variance';
import {discrepancyRows} from './room-discrepancy';
import {highBalanceRows,highBalanceThreshold} from './high-balance';
import {allowanceRows} from './allowances-voids';
import {noShowRows} from './no-show-cancel';
import {inventoryRows} from './inventory-exceptions';
import {cashierRows} from './cashier-variance';

export type MeasNightLine={key:string;label:string;ok:boolean;detail:string};
const redCount=(rows:{cells:Record<string,unknown>}[])=>rows.filter(r=>!!r.cells.exception).length;
void isRed;
export function measNightAuditLines(s:State):MeasNightLine[]{
  const d=s.date;
  const defs:{key:string;label:string;red:()=>number}[]=[
    {key:'meas.daily.flash',label:"Manager's flash: KPIs more than 10 % below target",red:()=>redCount(flashRows(s,d,'budget'))},
    {key:'meas.daily.rate-variance',label:'Rate variance: changes without a reason or below the floor',red:()=>redCount(rateVarianceRows(s,{from:d,to:d}))},
    {key:'meas.daily.discrepancy',label:'Room discrepancy: mismatches older than 2 hours',red:()=>redCount(discrepancyRows(s))},
    {key:'meas.daily.high-balance',label:'High balance: stays over the limit or threshold',red:()=>redCount(highBalanceRows(s,highBalanceThreshold(s)))},
    {key:'meas.daily.adjustments',label:'Allowances and voids: entries without reason or approver',red:()=>redCount(allowanceRows(s,{from:d,to:d}))},
    {key:'meas.daily.no-show',label:'No-show and cancellation: waivers without an approver',red:()=>redCount(noShowRows(s,{from:d,to:d}))},
    {key:'meas.daily.inventory',label:'Complimentary and out-of-order exceptions',red:()=>redCount(inventoryRows(s,d,d))},
    {key:'meas.daily.cashier',label:'Cashier variance: over the limit or repeated',red:()=>redCount(cashierRows(s,{from:d,to:d}))},
  ];
  return defs.filter(x=>measOn(s,x.key)).map(x=>{const n=x.red();return {key:x.key,label:x.label,ok:n===0,detail:n===0?'No exceptions':`${n} exception${n===1?'':'s'}`};});
}
/** The close block: with meas.daily.blockClose on, every line that is not ok blocks; with it off nothing ever blocks. */
export function measBlockClose(s:State):{blocked:boolean;reasons:string[]}{
  if(s.configX?.values?.['meas.daily.blockClose']!==true)return {blocked:false,reasons:[]};
  const reasons=measNightAuditLines(s).filter(l=>!l.ok).map(l=>`${l.label} (${l.detail})`);
  return {blocked:reasons.length>0,reasons};
}
