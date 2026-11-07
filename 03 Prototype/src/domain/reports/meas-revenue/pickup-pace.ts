// F12-RPT-REVENUE · rpt.revenue.pickup-pace — Pickup and pace (catalogue §3.10, switch meas.rev.pickup). On-the-books figures come from the bookings'
// booking / cancellation dates (booking date = fcBookedOn); a stored OTB snapshot (weekly Monday job) replaces the derived figure for "last year"
// and for the 7-day pickup baseline when one lies within 7 days before the date asked for.
import type {State,Booking} from '../../types';
import type {RptColumn,RptBoundParams} from '../types';
import type {MeasOtbSnapshot} from '../../streams/measure-types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {addDays,nights} from '../../money';
import {HccError} from '../../errors';
import {mrPaceTenths,mrPaceRed,mrLeadBucket,MR_LEAD_BUCKETS} from './logic';
import {T,I,P,M,mrRows,mrBookedOn,mrCancelledOn,mrChannelOf,mrIntParam,CAPTURE_NOTE,type MrRow} from './shared';

export const PICKUP_COLS:RptColumn[]=[T('month','Month'),I('rooms','Rooms on books'),I('lastYearRooms','Last year same snapshot'),M('revenue','Revenue on books'),M('lastYearRevenue','Last year revenue'),
  P('pace','Pace %'),I('pickup7','Pickup last 7 days'),T('lead','Lead-time bucket')];
const PICKUP_BASIS='Business date now: room-nights on the books for each coming month as known today, against the same month last year as known one year (364 days) ago; revenue = nightly rate of each room-night. Pace % = (now - last year) / last year; red when worse than -10 %. Lead-time bucket = the most common gap between booking and arrival.';

type Night={night:string;booked:string;cancelled?:string;rate:number;lead:number};
export function pickupNights(s:State,channel:string):Night[]{
  const out:Night[]=[];
  for(const b of s.bookings as Booking[]){
    if(b.status==='Expired'||(b.status==='Held'&&b.holdUntil&&s.clock>=b.holdUntil)||b.arrival>=b.departure)continue;
    if(channel&&mrChannelOf(b)!==channel)continue;
    const booked=mrBookedOn(s,b),cancelled=mrCancelledOn(b),lead=Math.max(0,nights(booked,b.arrival));
    for(let d=b.arrival;d<b.departure;d=addDays(d,1))out.push({night:d,booked,cancelled,rate:b.rate,lead});
  }
  return out;
}
const inMonth=(d:string,m:string)=>d.slice(0,7)===m;
const known=(x:Night,asOf:string)=>x.booked<=asOf&&!(x.cancelled&&x.cancelled<=asOf);
/** Rooms and revenue on the books for a month as known on `asOf`. */
export function pickupAt(list:Night[],month:string,asOf:string){
  let rooms=0,revenue=0;for(const x of list)if(inMonth(x.night,month)&&known(x,asOf)){rooms++;revenue+=x.rate;}
  return {rooms,revenue};
}
/** The stored snapshot nearest on or before `target` (within 7 days), summed for the month; undefined when none. */
export function snapshotMonth(otb:MeasOtbSnapshot[]|undefined,target:string,month:string){
  if(!otb?.length)return undefined;
  const days=[...new Set(otb.map(r=>r.takenOn))].filter(d=>d<=target&&nights(d,target)<=7).sort();
  const day=days[days.length-1];if(!day)return undefined;
  let rooms=0,revenue=0;for(const r of otb)if(r.takenOn===day&&inMonth(r.night,month)){rooms+=r.rooms;revenue+=r.revenuePaise;}
  return {rooms,revenue};
}
export const monthPlus=(date:string,k:number)=>{const y=+date.slice(0,4),m=+date.slice(5,7)-1+k;return `${y+Math.floor(m/12)}-${String(m%12+1).padStart(2,'0')}`;};
const yearBack=(m:string)=>`${+m.slice(0,4)-1}${m.slice(4)}`;
export function pickupRow(x:{month:string;rooms:number;lyRooms:number;revenue:number;lyRevenue:number;pickup7:number;lead:string}):MrRow{
  const t=mrPaceTenths(x.rooms,x.lyRooms);
  return {cells:{month:x.month,rooms:x.rooms,lastYearRooms:x.lyRooms,revenue:x.revenue,lastYearRevenue:x.lyRevenue,pace:t===null?null:t/10,pickup7:x.pickup7,lead:x.lead},red:mrPaceRed(t)};
}
export function pickupCompute(s:State,months:number,channel:string):MrRow[]{
  const list=pickupNights(s,channel),today=s.date,ly=addDays(today,-364),wk=addDays(today,-7);
  const out:MrRow[]=[];
  for(let k=0;k<months;k++){
    const month=monthPlus(today,k),cur=pickupAt(list,month,today);
    const lyM=yearBack(month),lyV=snapshotMonth(s.measX?.otb,ly,lyM)??pickupAt(list,lyM,ly);
    const before=snapshotMonth(s.measX?.otb,wk,month)??pickupAt(list,month,wk);
    const count=new Map<string,number>();
    for(const x of list)if(inMonth(x.night,month)&&known(x,today)){const b=mrLeadBucket(x.lead);count.set(b,(count.get(b)??0)+1);}
    const lead=MR_LEAD_BUCKETS.reduce<string>((best,b)=>(count.get(b)??0)>(count.get(best)??0)?b:best,MR_LEAD_BUCKETS[0]);
    out.push(pickupRow({month,rooms:cur.rooms,lyRooms:lyV.rooms,revenue:cur.revenue,lyRevenue:lyV.revenue,pickup7:cur.rooms-before.rooms,lead:cur.rooms?lead:''}));
  }
  return out;
}
export const pickupPace=registerReport({
  id:'rpt.revenue.pickup-pace',title:'Pickup and pace',area:'revenue',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-NOW',
  params:[{key:'months',label:'Months ahead (1-12)',type:'text',default:'6'},{key:'channel',label:'Channel',type:'text'}],
  columns:PICKUP_COLS,dateBasis:PICKUP_BASIS,formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s:State,_uid:string,p:RptBoundParams)=>{
    const months=mrIntParam('Months ahead',p.months,6,1,12);
    if(p.channel&&String(p.channel).length>60)throw new HccError('VALIDATION','Channel is too long.');
    const empty=!s.bookings.length&&!s.measX?.otb?.length;
    return {params:p,columns:PICKUP_COLS,rows:empty?[]:mrRows(pickupCompute(s,months,String(p.channel??''))),
      ...(empty?{about:{dateBasis:PICKUP_BASIS+CAPTURE_NOTE('weekly on-the-books snapshot')}}:{})};
  },
});
registerMeasReport('rpt.revenue.pickup-pace','meas.rev.pickup');
