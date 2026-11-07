// F12-RPT-GUEST · rpt.guests.desk-timing — Arrival and departure experience (data: audit log, KEYS log, folio settle). Switch meas.guest.desk-timing. No capture needed.
// Prototype definitions (the server reads the real settle instant): arrival-to-key = check-in instant (booking.checkedInAt) to the first finished key job of the stay;
// bill-to-checkout = the last bill-side audit entry by the same desk user in the 60 minutes before the CHECKOUT audit entry. Both are whole minutes; medians are the lower median.
import type {State,Booking} from '../../types';
import {addMinutes} from '../../money';
import {registerReport} from '../catalogue';
import {keySlice} from '../../streams/keys';
import {measTarget} from '../../streams/measure';
import {rangeGuard} from '../guests/shared';
import {T,I,minutesBetween,lowerMedian} from './shared';

export const DESK_COLS=[T('hour','Hour'),T('deskUser','Desk user'),I('arrivals','Arrivals'),I('arrivalToKey','Median minutes arrival-to-key'),I('billToCheckout','Median minutes bill-to-checkout'),I('queuePeak','Queue peak')];
const BASIS='Check-ins and check-outs by the hour they happened (range up to 92 days), one row per hour and desk user. Arrival-to-key = check-in to the first finished key job; bill-to-checkout = last bill action to the check-out; medians are whole minutes (lower median); queue peak = most check-ins waiting for a key at the same time.';
const BILL_ACTIONS=/CHARGE|PAY|RECEIPT|BILL|DISCOUNT|SETTLE|DEPOSIT|REFUND/;
export const BILL_WINDOW_MINUTES=60;

export type DeskEvent={kind:'in'|'out';user:string;at:string;minutes:number|null;endAt?:string};
/** Desk events of the range: one per check-in (with its arrival-to-key) and one per check-out (with its bill-to-checkout). */
export function deskEvents(s:State,from:string,to:string):DeskEvent[]{
  const out:DeskEvent[]=[],jobs=keySlice(s).jobs;
  const inRange=(at:string)=>at.slice(0,10)>=from&&at.slice(0,10)<=to;
  for(const b of s.bookings as Booking[]){
    if(!b.checkedInAt||!inRange(b.checkedInAt))continue;
    const a=s.audit.find(x=>x.action==='CHECKIN'&&x.at===addMinutes(b.checkedInAt!,1));if(!a)continue;
    const j=jobs.filter(k=>k.booking===b.id&&k.state==='done'&&k.finishedAt&&k.finishedAt>=b.checkedInAt!&&k.kind!=='cancel'&&k.kind!=='read').sort((p,q)=>p.finishedAt!.localeCompare(q.finishedAt!))[0];
    out.push({kind:'in',user:a.actor,at:b.checkedInAt,minutes:j?minutesBetween(b.checkedInAt,j.finishedAt!):null,endAt:j?.finishedAt});
  }
  const audit=[...s.audit].sort((a,b)=>a.at.localeCompare(b.at)||a.id.localeCompare(b.id));
  audit.forEach((a,i)=>{
    if(a.action!=='CHECKOUT'||!inRange(a.at))return;
    let start:string|undefined;
    for(let k=i-1;k>=0;k--){const p=audit[k];if(minutesBetween(p.at,a.at)>BILL_WINDOW_MINUTES)break;if(p.actor===a.actor&&BILL_ACTIONS.test(p.action)){start=p.at;break;}}
    out.push({kind:'out',user:a.actor,at:a.at,minutes:start?minutesBetween(start,a.at):null});
  });
  return out;
}
/** Most check-ins whose waiting interval [check-in, key finished] overlaps at one moment (a check-in without a key counts for its own minute). */
export function queuePeak(list:DeskEvent[]):number{
  const iv=list.filter(e=>e.kind==='in').map(e=>[e.at,e.endAt??e.at] as const);
  return iv.reduce((m,[st])=>Math.max(m,iv.filter(([a,b])=>a<=st&&(st<b||a===st)).length),0);
}
export type DeskRow={hour:string;user:string;arrivals:number;arrivalToKey:number|null;billToCheckout:number|null;queuePeak:number};
export function deskRows(s:State,from:string,to:string,user?:string):DeskRow[]{
  const name=user?s.users.find(u=>u.id===user)?.name:undefined;
  const ev=deskEvents(s,from,to).filter(e=>!user||e.user===name);
  const groups=new Map<string,DeskEvent[]>();
  for(const e of ev){const k=e.at.slice(11,13)+':00\u0000'+e.user;(groups.get(k)??groups.set(k,[]).get(k)!).push(e);}
  return [...groups.entries()].sort((a,b)=>a[0].localeCompare(b[0])).map(([k,es])=>{
    const [hour,u]=k.split('\u0000'),ins=es.filter(e=>e.kind==='in'),outs=es.filter(e=>e.kind==='out');
    return {hour,user:u,arrivals:ins.length,arrivalToKey:lowerMedian(ins.map(e=>e.minutes).filter((m):m is number=>m!==null)),
      billToCheckout:lowerMedian(outs.map(e=>e.minutes).filter((m):m is number=>m!==null)),queuePeak:queuePeak(ins)};
  });
}
/** Exception rule (red row): median above 8 minutes at check-in; above 6 minutes at check-out (targets meas.target.checkinMinutes / checkoutMinutes). */
export function deskRed(s:State,r:Pick<DeskRow,'arrivalToKey'|'billToCheckout'>){
  const inT=measTarget(s,'meas.target.checkinMinutes')??8,outT=measTarget(s,'meas.target.checkoutMinutes')??6;
  const slowIn=r.arrivalToKey!==null&&r.arrivalToKey>inT,slowOut=r.billToCheckout!==null&&r.billToCheckout>outT;
  return {slowIn,slowOut,red:slowIn||slowOut};
}
export const deskTiming=registerReport({
  id:'rpt.guests.desk-timing',title:'Arrival and departure experience',area:'guests',priority:'P2',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'deskUser',label:'Desk user',type:'user'}],
  columns:DESK_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf','csv'],phone:'no',
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    return {params:p,columns:DESK_COLS,rows:deskRows(s,p.from!,p.to!,p.deskUser as string|undefined).map(r=>({kind:'row' as const,cells:{hour:r.hour,deskUser:r.user.split(' · ')[0],arrivals:r.arrivals,
      arrivalToKey:r.arrivalToKey,billToCheckout:r.billToCheckout,queuePeak:r.queuePeak}}))};
  },
});
