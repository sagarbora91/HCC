// F12-RPT-DAILY · rpt.guests.rate-variance (catalogue 1.3, meas.daily.rate-variance). Source: stay rate changes (s.stayRateX, AF2 CTL-01).
// Card rate = the tariff the change was judged against; charged rate = the new rate. Exception: no reason, or a rate below the agreed-rate floor
// (a rate AT the floor passes, one paisa below is red). Server twin: Reports/MeasDaily/RateVarianceReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {strState,strThresholdBps,strAgreedFloor} from '../../streams/stay-rate';
import {rateFor} from '../../selectors';
import {bookingNo,guestNameOf,rangeGuard,userLabel} from '../guests/shared';
import {T,M,P,EXC,inRange} from './shared';

export const RATE_VAR_COLS:RptColumn[]=[T('booking','Booking'),T('guest','Guest','guestName'),T('room','Room'),M('cardRate','Card rate'),M('chargedRate','Charged rate'),M('difference','Difference'),
  P('differencePct','Difference %'),T('reasonCode','Reason code'),T('user','User'),T('approver','Approver'),EXC()];
export const RATE_VAR_KEY='meas.daily.rate-variance';
/** True when `rate` is below the floor set by `agreed` and the threshold (bps). */
export const rateBelowFloor=(agreed:number,rate:number,thresholdBps:number)=>agreed>0&&rate<strAgreedFloor(agreed,thresholdBps);

export function rateVarianceRows(s:State,p:{from?:string;to?:string;roomType?:string;user?:string}):RptRow[]{
  return strState(s).changes.filter(c=>inRange(c.date,p.from,p.to)&&(!p.user||c.by===p.user)).filter(c=>{
    if(!p.roomType)return true;return (s.rooms.find(r=>r.id===c.room)?.type??'').toLowerCase()===p.roomType.toLowerCase();
  }).sort((a,b)=>a.at.localeCompare(b.at)||a.id.localeCompare(b.id)).map(c=>{
    const b=s.bookings.find(x=>x.id===c.booking),room=s.rooms.find(r=>r.id===c.room);
    const card=c.tariff??(room?rateFor(s,room,c.date):c.oldRate),diff=c.newRate-card;
    const agreed=c.agreedRate??c.oldRate,bps=c.thresholdBps??strThresholdBps(s);
    const noReason=!c.reason.trim(),below=rateBelowFloor(agreed,c.newRate,bps);
    return {kind:'row' as const,cells:{booking:b?bookingNo(b):c.booking,guest:b?guestNameOf(s,b):'',room:c.room,cardRate:card,chargedRate:c.newRate,difference:diff,
      differencePct:card>0?Math.round(diff*1000/card)/10:0,reasonCode:c.reason,user:userLabel(s,c.by),approver:userLabel(s,c.approvedBy),
      exception:noReason?'No reason recorded':below?`Below the agreed-rate floor (${bps/100} % threshold)`:''}};
  });
}
registerMeasReport('rpt.guests.rate-variance',RATE_VAR_KEY);
export const rateVariance=registerReport({
  id:'rpt.guests.rate-variance',title:'Rate variance / rate check',area:'guests',priority:'P1',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'roomType',label:'Room type',type:'text'},{key:'user',label:'User',type:'user'}],
  columns:RATE_VAR_COLS,
  dateBasis:'Stay rate changes by the business date they were made on (range up to 92 days). Card rate = the tariff at the change; difference = charged - card; difference % of the card rate. Exception: no reason, or the new rate is below the agreed-rate floor set by the CTL-01 threshold (default 15 %).',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{rangeGuard(p.from,p.to,92,s.date);return {params:p,columns:RATE_VAR_COLS,rows:rateVarianceRows(s,p)};},
});
