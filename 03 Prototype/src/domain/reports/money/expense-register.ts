// F7-R4 · rpt.money.expense-register: expense register (partial → built). Adapter over the expense ledger (SCC records): same rows as the ledger CSV.
// Totals count what the day close counts (Posted / Reversed originals and reversal records) and leave out capital purchases (asset register).
import type {State,Expense} from '../../types';
import {registerReport} from '../catalogue';
import {expenseCounts,expCapitalIds} from '../../streams/closing';
import {expDescription} from '../../streams/expenses-scc';
import {col,userName,rangeParams,rowOf} from './common';

export const EXPREG_COLS=[col('date','Date','date'),col('description','Description or reference','text'),col('category','Category','text'),col('funding','Funding','text'),
  col('amount','Amount','money',true),col('gst','GST amount','money',true),col('gstRate','GST rate','pct'),col('itc','ITC','text'),col('status','Status','text'),
  col('vendor','Vendor','text'),col('by','Created by','text')];
const STATUSES=['all','Draft','Pending','Posted','Rejected','Reversed','Void'];

/** Expenses dated in the range, oldest first (the ledger order), with the register filters. */
export function expenseRegisterRows(s:State,from:string,to:string,f:{status?:string;category?:string;funding?:string;q?:string}={}):Expense[]{
  const q=(f.q??'').trim().toLowerCase();
  return s.expenses.map((e,i)=>({e,i})).filter(({e})=>e.date>=from&&e.date<=to&&(!f.status||f.status==='all'||e.status===f.status)&&(!f.category||e.category===f.category)
    &&(!f.funding||e.method===f.funding)&&(!q||(expDescription(e)+' '+(e.vendor??'')+' '+e.category).toLowerCase().includes(q)))
    .sort((a,b)=>a.e.date<b.e.date?-1:a.e.date>b.e.date?1:a.i-b.i).map(x=>x.e);
}
export const expenseRegister=registerReport({
  id:'rpt.money.expense-register',title:'Expense register',area:'money',priority:'P1',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',
  viewPerms:['PERM-EXP-001','PERM-RPT-002'],dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'status',label:'Status',type:'enum',options:STATUSES,default:'all'},{key:'category',label:'Category',type:'text'},
    {key:'funding',label:'Funding',type:'text'},{key:'q',label:'Search',type:'text'}],
  columns:EXPREG_COLS,dateBasis:'Expenses by business date, oldest first. Totals count Posted and Reversed expenses with their reversal records; Draft, Pending, Rejected and Void rows are listed but not totalled, and capital purchases (asset register) are listed but not totalled.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const list=expenseRegisterRows(s,p.from!,p.to!,{status:p.status as string,category:p.category as string,funding:p.funding as string,q:p.q as string});
    const cap=expCapitalIds(s);
    const counted=(e:Expense)=>expenseCounts(e)&&!cap.has(e.id);
    const cell=(e:Expense)=>({date:e.date,description:expDescription(e)+(e.reversalOf?' (reversal)':''),category:e.category,funding:e.method,amount:e.amount,
      gst:e.gstPaise??0,gstRate:e.gstRateBps?e.gstRateBps/100:null,itc:e.gstPaise?(e.itc==='allowed'?'allowed':'disallowed'):'',status:e.status,vendor:e.vendor??'',by:userName(s,e.actor)});
    const cats=[...new Set(list.map(e=>e.category))].sort();
    const rows=cats.flatMap(c=>{
      const g=list.filter(e=>e.category===c),body=g.map(e=>({...rowOf('row',cell(e),c),...(counted(e)?{}:{})}));
      const tot=g.filter(counted);
      return [...body,rowOf('subtotal',{date:null,description:'Total '+c,category:c,funding:null,amount:tot.reduce((a,e)=>a+e.amount,0),gst:tot.reduce((a,e)=>a+(e.gstPaise??0),0),gstRate:null,itc:null,status:null,vendor:null,by:null},c)];
    });
    const all=list.filter(counted);
    if(rows.length)rows.push(rowOf('total',{date:null,description:'Grand total',category:null,funding:null,amount:all.reduce((a,e)=>a+e.amount,0),gst:all.reduce((a,e)=>a+(e.gstPaise??0),0),gstRate:null,itc:null,status:null,vendor:null,by:null}));
    return {params:p,columns:EXPREG_COLS,rows};
  },
});
