// F8-RPT-P2-B · rpt.money.cash-count-sheet: printable denomination count sheet for one business date. Adapter over the shift count: the note grid
// (SHIFT_DENOMINATIONS + coins) of the open shift when it holds the date, the counted / expected cash of the closing record otherwise. A closed day keeps
// the counted total only (the grid is not stored after the close), so its note rows are left empty to be written by hand — nothing is invented.
// Server twin: Hcc.Reporting/Reports/P2B/CashCountSheetReport.cs.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {SHIFT_DENOMINATIONS,shiftDenominationTotal,shiftExpected} from '../../streams/closing';
import {col,rowOf,seesAll} from '../money/common';

export const CCS_COLS=[col('denomination','Denomination','text'),col('count','Notes or coins','int'),col('value','Value','money',true),col('expected','Expected','money',true),col('difference','Difference','money',true)];
export function countSheetRows(s:State,uid:string,date:string){
  const c=s.closings?.[date],open=date===s.date&&!s.closedDates.includes(date);
  if(!c&&!open)return [];
  const who=c?(c.cash.countedBy??c.closedBy):(s.shift?.submittedBy??'');
  if(!seesAll(s,uid)&&who!==uid)return [];
  const grid=open&&s.shift?.date===date?s.shift.denominations:undefined;
  const expected=c?c.cash.expected:shiftExpected(s).expected;
  const counted=c?c.cash.counted:grid?shiftDenominationTotal(grid):s.shiftClose?.counted;
  const rows=SHIFT_DENOMINATIONS.map(n=>{const k=grid?(grid[n]||0):null;return rowOf('row',{denomination:`₹${n} notes`,count:k,value:k===null?null:k*n*100,expected:null,difference:null});});
  const coins=grid?Math.round((grid.coins||0)*100):null;
  rows.push(rowOf('row',{denomination:'Coins (rupee total)',count:null,value:coins,expected:null,difference:null}));
  rows.push(rowOf('total',{denomination:grid?'Total counted':'Total counted (grid not kept)',count:null,value:counted??null,expected,difference:counted===undefined||counted===null?null:counted-expected}));
  rows.push(rowOf('row',{denomination:'Counted by (signature): ____________________',count:null,value:null,expected:null,difference:null}));
  rows.push(rowOf('row',{denomination:'Checked by (signature): ____________________',count:null,value:null,expected:null,difference:null}));
  return rows;
}
export const cashCountSheet=registerReport({
  id:'rpt.money.cash-count-sheet',title:'Cash count sheet',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',
  viewPerms:['PERM-RPT-002'],ownShift:true,dateClass:'D-DAY',
  params:[{key:'from',label:'Shift or date',type:'date',dateClass:'D-DAY'}],columns:CCS_COLS,
  dateBasis:'One business date (the drawer shift of the day). Notes and coins from the count grid where the shift kept one (the prototype keeps it only for the open shift); without a grid the note rows are blank for hand entry. Expected = opening + receipts - payments; difference = counted - expected.',
  formats:['pdf','screen'],phone:'yes',
  select:(s,uid,p)=>({params:p,columns:CCS_COLS,rows:countSheetRows(s,uid,p.from!)}),
});
