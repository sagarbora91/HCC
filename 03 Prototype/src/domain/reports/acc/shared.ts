// F7-R6 · helpers shared by the accounting reports (pure; read-only over State). Every figure comes from the ACC-1 / ACC-3 selectors
// (accStatement, accTrialBalance6, accDayBook, accPl, accBalanceSheet, accCashRatio, accAgeing, accTallyMasters / accTallyVouchers) — nothing is recomputed.
// Server twin: Hcc.Reporting/Reports/Acc/AccShared.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {accYearProvisional,ACC_PROVISIONAL_TEXT} from '../../streams/accounting-year';

export const T=(key:string,label:string):RptColumn=>({key,label,type:'text',money:false});
export const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const D=(key:string,label:string):RptColumn=>({key,label,type:'date',money:false});
export const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const P=(key:string,label:string):RptColumn=>({key,label,type:'pct',money:false});
export const B=(key:string,label:string):RptColumn=>({key,label,type:'bool',money:false});
/** Accounting view (Owner, Manager, Accountant) and the Owner + Accountant permission of accounting.tax (migration 4249), as the tax reports. */
export const ACC_VIEW='PERM-ACC-001',ACC_OWNER_ACCOUNTANT='PERM-ACC-013',ACC_TALLY='PERM-ACC-006';
/** ACC-4 §1.2: a table of a reopened year (or the year after) carries "PROVISIONAL — year reopened" as its first row, in every format. */
export function stampRows(s:State,from:string|undefined,to:string|undefined,textKey:string):RptRow[]{
  const hit=(d?:string)=>!!d&&accYearProvisional(s,d);
  return hit(from)||hit(to)?[{kind:'subtotal',cells:{[textKey]:ACC_PROVISIONAL_TEXT}}]:[];
}
export const stamped=(rows:RptRow[])=>rows.length>0;
/** Row builders: cells missing from a row are empty. */
export const row=(cells:Record<string,string|number|null>,kind:RptRow['kind']='row'):RptRow=>({kind,cells});
