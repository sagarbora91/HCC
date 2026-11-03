// F7-R4 · rpt.money.expenses-by-head: expense totals by group and head over a range (FY history allowed). Counted expenses only (reportExpenses
// rule: Posted / Reversed with reversal records, capital purchases left to the asset register), so the grand total equals the closing pack's expense total.
// Group (no group master in HCC): Operating expenses (entered by hand), Petty cash (petty-cash disbursements), Recurring bills (recurring templates).
import type {State,Expense} from '../../types';
import {registerReport} from '../catalogue';
import {reportExpenses} from '../../streams/closing';
import {col,rangeParams,rowOf} from './common';

export const EXPHEAD_COLS=[col('group','Group','text'),col('head','Head','text'),col('entries','Entries','int'),col('amount','Amount','money',true),col('share','Share','pct')];
export const EXP_GROUPS=['Operating expenses','Petty cash','Recurring bills'];
export const expGroupOf=(e:Pick<Expense,'source'>)=>e.source==='petty'?EXP_GROUPS[1]:e.source==='recurring'?EXP_GROUPS[2]:EXP_GROUPS[0];
/** Share of the grand total in hundredths of a percent, half up, as a percent number. */
export const expShare=(amount:number,total:number)=>total>0?Math.floor((amount*20000+total)/(2*total))/100:0;

export const expensesByHead=registerReport({
  id:'rpt.money.expenses-by-head',title:'Expenses by head',area:'money',priority:'P1',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',
  viewPerms:['PERM-RPT-002'],dateClass:'D-F',
  params:[...rangeParams('D-F'),{key:'group',label:'Group',type:'enum',options:['all',...EXP_GROUPS],default:'all'},{key:'head',label:'Head',type:'text'}],
  columns:EXPHEAD_COLS,dateBasis:'Expenses by business date, net of reversals; Posted and Reversed expenses with their reversal records. Capital purchases sit on the asset register, not here.',
  formats:['screen','xlsx','pdf'],phone:'summary',
  select:(s:State,_uid,p)=>{
    const list=reportExpenses(s,p.from!,p.to!).filter(e=>(!p.group||p.group==='all'||expGroupOf(e)===p.group)&&(!p.head||e.category===p.head));
    const total=list.reduce((a,e)=>a+e.amount,0);
    const rows=EXP_GROUPS.flatMap(g=>{
      const gl=list.filter(e=>expGroupOf(e)===g);if(!gl.length)return [];
      const heads=[...new Set(gl.map(e=>e.category))].sort();
      const body=heads.map(h=>{const x=gl.filter(e=>e.category===h),a=x.reduce((n,e)=>n+e.amount,0);return rowOf('row',{group:g,head:h,entries:x.length,amount:a,share:expShare(a,total)},g);});
      const ga=gl.reduce((n,e)=>n+e.amount,0);
      return [...body,rowOf('subtotal',{group:g,head:'Total '+g,entries:gl.length,amount:ga,share:expShare(ga,total)},g)];
    });
    if(rows.length)rows.push(rowOf('total',{group:'Grand total',head:null,entries:list.length,amount:total,share:total>0?100:0}));
    return {params:p,columns:EXPHEAD_COLS,rows};
  },
});
