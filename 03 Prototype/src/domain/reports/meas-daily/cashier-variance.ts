// F12-RPT-DAILY · rpt.money.cashier-variance (catalogue 1.9, meas.daily.cashier). One row per closed business date from the closing records
// (the prototype has one drawer shift a day). Variance = counted - expected. Exception: |variance| above meas.target.cashVariance (Rs 100: exactly
// Rs 100 is not red), or the third (and later) variance of the month by one cashier. Month-to-date variance = running sum for that cashier in the month.
// Server twin: Reports/MeasDaily/CashierVarianceReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport,measTarget} from '../../streams/measure';
import {rangeGuard,userLabel} from '../guests/shared';
import {T,M,D,EXC,inRange} from './shared';

export const CASHIER_COLS:RptColumn[]=[D('date','Date'),T('shift','Shift'),T('cashier','Cashier'),M('expected','Expected cash'),M('counted','Counted cash'),M('paidOuts','Paid-outs'),M('float','Float'),M('variance','Variance'),M('mtdVariance','Month-to-date variance'),EXC()];
export const CASHIER_KEY='meas.daily.cashier';
export const CASHIER_REPEAT_LIMIT=3;

export function cashierRows(s:State,p:{from?:string;to?:string;cashier?:string}):RptRow[]{
  const limit=measTarget(s,'meas.target.cashVariance')??10000;
  const days=Object.values(s.closings??{}).filter(c=>c.cash).sort((a,b)=>a.date.localeCompare(b.date));
  const mtd=new Map<string,{sum:number;count:number}>(),out:RptRow[]=[];
  for(const c of days){
    const who=c.cash.countedBy??c.closedBy??'',key=who+'|'+c.date.slice(0,7);
    const m=mtd.get(key)??{sum:0,count:0};
    m.sum+=c.cash.difference;if(c.cash.difference!==0)m.count++;mtd.set(key,m);
    if(!inRange(c.date,p.from,p.to)||(p.cashier&&who!==p.cashier))continue;
    const why:string[]=[];
    if(Math.abs(c.cash.difference)>limit)why.push('Variance above the cash-variance limit');
    if(c.cash.difference!==0&&m.count>=CASHIER_REPEAT_LIMIT)why.push(`Variance number ${m.count} this month by this cashier`);
    out.push({kind:'row',cells:{date:c.date,shift:'Day',cashier:userLabel(s,who),expected:c.cash.expected,counted:c.cash.counted,paidOuts:c.expenses.cash,float:c.cash.opening,variance:c.cash.difference,mtdVariance:m.sum,exception:why.join('; ')}});
  }
  return out;
}
registerMeasReport('rpt.money.cashier-variance',CASHIER_KEY);
export const cashierVariance=registerReport({
  id:'rpt.money.cashier-variance',title:'Cashier and shift variance',area:'money',priority:'P1',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',ownShift:true,
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'cashier',label:'Cashier',type:'user'}],
  columns:CASHIER_COLS,
  dateBasis:'One row per closed business date (one drawer shift a day), from the closing record. Variance = counted - expected cash. Paid-outs = cash expenses of the day; float = opening cash. Month-to-date variance runs per cashier from the 1st. Exception: variance above Rs 100, or the third variance of the month by one cashier.',
  formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,uid,p)=>{rangeGuard(p.from,p.to,92,s.date);
    const own=s.users.find(u=>u.id===uid)?.role==='Cashier'; // rule V.S: a Cashier sees the own shifts only
    return {params:p,columns:CASHIER_COLS,rows:cashierRows(s,own?{...p,cashier:uid}:p)};},
});
