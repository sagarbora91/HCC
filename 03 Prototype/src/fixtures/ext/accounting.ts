// Module 20 · Accounting automation — fixture extensions (fictional data only).
// The base fixture is left untouched (no money posted); the ACC scenario demonstrates exceptions and a locked period.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {accLiveVouchers,accTotals} from '../../domain/streams/accounting';
export function extendBase(_s:State):void{}
/** ACC · 23 Oct locked by the accountant; a posted UPI expense in a category with no ledger mapping ("Kitchen gas") on 24 Oct. */
function acc(s:State){
  s.expenses.push({id:'EXP-ACC-1',vendor:'Shree Ganesh Gas Agency (fictional)',category:'Kitchen gas',amount:120000,method:'UPI',date:s.date,status:'Posted',actor:'reception',approver:'manager',decidedAt:s.date+'T08:00',notes:'Demonstration: category not yet mapped to a ledger'});
  const d='2026-10-23',vouchers=accLiveVouchers(s,d),t=accTotals(vouchers);
  s.accountingX={...(s.accountingX??{}),locks:{[d]:{date:d,lockedAt:s.date+'T07:30',lockedBy:'accountant',vouchers,debit:t.debit,credit:t.credit,count:vouchers.length}},
    lockHistory:[{id:'ACCL-seed',at:s.date+'T07:30',actor:'accountant',date:d,action:'lock'}]};
}
export const scenarioPatches:Record<string,(s:State)=>void>={ACC:acc};
export const scenarioInfo:ScenarioInfo[]=[
  {id:'ACC',name:'Accounting exceptions',purpose:'23 Oct locked; 24 Oct has a posted expense in an unmapped category (Kitchen gas) that blocks locking until it is mapped.',available:true},
];
