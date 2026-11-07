// F12-RPT-DAILY · rpt.money.high-balance (catalogue 1.5, meas.daily.high-balance; core once credit is on). In-house stays with a balance.
// Exception: a company-account stay whose balance is above deposit + credit limit; a stay without a company account above the threshold
// (meas.target.highBalance, default Rs 5,000: exactly the threshold is not red); any balance on a stay older than 2 nights. Server twin: HighBalanceReport.cs.
import type {State,Booking} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport,measTarget} from '../../streams/measure';
import {totals} from '../../selectors';
import {grpSlice} from '../../streams/groups';
import {nights} from '../../money';
import {guestNameOf} from '../guests/shared';
import {T,I,M,EXC} from './shared';

export const HIGH_BALANCE_COLS:RptColumn[]=[T('room','Room'),T('guest','Guest','guestName'),T('company','Company'),I('daysInHouse','Days in house'),M('balance','Balance'),M('deposit','Deposit'),M('limit','Limit'),M('overBy','Over by'),EXC()];
export const HIGH_BALANCE_KEY='meas.daily.high-balance';
export const HIGH_BALANCE_DAYS=2;
export const highBalanceThreshold=(s:State,rupees?:string)=>{
  const n=Number(String(rupees??'').replace(/[,\s]/g,''));
  return rupees!==undefined&&rupees!==''&&Number.isFinite(n)&&n>=0?Math.round(n*100):(measTarget(s,'meas.target.highBalance')??500000);
};
/** Credit limit of the stay's company account (0 when none). */
export const companyLimit=(s:State,b:Booking)=>b.company?.account?(grpSlice(s).companies.find(c=>c.id===b.company!.account)?.creditLimit??0):0;

export function highBalanceRows(s:State,threshold:number):RptRow[]{
  return s.bookings.filter(b=>b.status==='In house').map(b=>({b,t:totals(b)})).filter(x=>x.t.balance>0).sort((a,c)=>a.b.room.localeCompare(c.b.room)||a.b.id.localeCompare(c.b.id)).map(({b,t})=>{
    const days=Math.max(0,nights(b.arrival,s.date)),limit=companyLimit(s,b),hasCo=!!b.company?.account;
    const allowed=hasCo?t.deposit+limit:threshold;
    const reasons:string[]=[];
    if(hasCo&&t.balance>t.deposit+limit)reasons.push('Balance above deposit plus credit limit');
    if(!hasCo&&t.balance>threshold)reasons.push(`Walk-in balance above ${(threshold/100).toLocaleString('en-IN')} rupees`);
    if(days>HIGH_BALANCE_DAYS)reasons.push('Balance older than 2 nights');
    return {kind:'row' as const,cells:{room:b.room,guest:guestNameOf(s,b),company:b.company?.name??'',daysInHouse:days,balance:t.balance,deposit:t.deposit,limit,overBy:Math.max(0,t.balance-allowed),exception:reasons.join('; ')}};
  });
}
registerMeasReport('rpt.money.high-balance',HIGH_BALANCE_KEY);
export const highBalance=registerReport({
  id:'rpt.money.high-balance',title:'High-balance and credit-limit',area:'money',priority:'P1',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-NOW',
  params:[{key:'threshold',label:'Threshold in rupees (default meas.target.highBalance)',type:'text'}],
  columns:HIGH_BALANCE_COLS,
  dateBasis:'In-house stays with an unpaid balance now (GST included). Days in house = nights since arrival. Over by = balance above deposit plus credit limit (company account) or above the threshold (no company). Exception: over the limit or threshold, or a balance older than 2 nights.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_uid,p)=>({params:p,columns:HIGH_BALANCE_COLS,rows:highBalanceRows(s,highBalanceThreshold(s,p.threshold===undefined?undefined:String(p.threshold)))}),
});
