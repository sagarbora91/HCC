// F12-RPT-REVENUE · rpt.revenue.overbooking-wash — Overbooking and wash (catalogue §3.10, switch meas.rev.overbooking). Booking history by arrival weekday and
// channel; the oversold-allowed number is the Owner setting yld.overbook.<type>; walked guests come from the desk capture (s.measX.walked).
import type {State} from '../../types';
import type {RptColumn,RptBoundParams} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {addDays,nights} from '../../money';
import {HccError} from '../../errors';
import {yldOverbookAllowed,yldOversold,yldRoomTypes} from '../../streams/yield';
import {mrOversoldRed,mrDow,MR_WEEKDAYS} from './logic';
import {T,I,P,M,mrRows,mrChannelOf,mrTypeOf,CAPTURE_NOTE,type MrRow} from './shared';

export const WASH_COLS:RptColumn[]=[T('weekday','Weekday'),T('channel','Channel'),P('noShowPct','No-show %'),P('cancelPct','Cancellation %'),I('washRooms','Expected wash rooms'),
  I('allowed','Oversold allowed (yld.overbook)'),I('walked','Walked guests'),M('walkCost','Cost of walk')];
const WASH_BASIS='Business date, range up to 366 days: bookings by arrival weekday and channel (held and expired excluded). No-show % and cancellation % = share of those bookings; expected wash rooms = (no-shows + cancellations) a week of that weekday, rounded; oversold allowed = the Owner setting per room type (0 until set); walked guests and their cost from the desk log. Red when rooms oversold on a night of that weekday are above the allowed number.';
/** Per weekday: the largest oversold figure over the nights in the range (sum over the chosen types) and the allowed number. */
export function overboughtByWeekday(s:State,from:string,to:string,type:string){
  const types=type?[type]:yldRoomTypes(s),allowed=types.reduce((a,t)=>a+yldOverbookAllowed(s,t),0);
  const worst=new Array<number>(7).fill(0);
  for(let d=from;d<=to;d=addDays(d,1)){const over=types.reduce((a,t)=>a+yldOversold(s,t,d).over,0);const w=mrDow(d);if(over>worst[w])worst[w]=over;}
  return {worst,allowed};
}
export function washCompute(s:State,from:string,to:string,type:string,channel:string):MrRow[]{
  const {worst,allowed}=overboughtByWeekday(s,from,to,type);
  const occ=new Array<number>(7).fill(0);for(let d=from;d<=to;d=addDays(d,1))occ[mrDow(d)]++;
  type Acc={total:number;ns:number;cx:number;walked:number;cost:number};
  const acc=new Map<string,Acc>();
  const get=(w:number,c:string)=>{const k=`${w}|${c}`;let a=acc.get(k);if(!a)acc.set(k,a={total:0,ns:0,cx:0,walked:0,cost:0});return a;};
  const bk=new Map(s.bookings.map(b=>[b.id,b]));
  for(const b of s.bookings){
    if(b.status==='Held'||b.status==='Expired'||b.arrival<from||b.arrival>to)continue;
    if(type&&mrTypeOf(s,b)!==type)continue;const c=mrChannelOf(b);if(channel&&c!==channel)continue;
    const a=get(mrDow(b.arrival),c);a.total++;if(b.status==='No show')a.ns++;if(b.status==='Cancelled')a.cx++;
  }
  for(const w of s.measX?.walked??[]){
    if(w.night<from||w.night>to)continue;const b=bk.get(w.booking);
    if(type&&(!b||mrTypeOf(s,b)!==type))continue;const c=b?mrChannelOf(b):'Unknown';if(channel&&c!==channel)continue;
    const a=get(mrDow(w.night),c);a.walked++;a.cost+=w.costPaise??0;
  }
  const out:MrRow[]=[];
  for(const [k,a] of [...acc].sort((x,y)=>{const [wx,cx]=x[0].split('|'),[wy,cy]=y[0].split('|');return +wx-+wy||cx.localeCompare(cy);})){
    const [w,c]=k.split('|'),wi=+w,weeks=Math.max(1,occ[wi]);
    out.push({cells:{weekday:MR_WEEKDAYS[wi],channel:c,noShowPct:a.total?Math.round(a.ns*1000/a.total)/10:0,cancelPct:a.total?Math.round(a.cx*1000/a.total)/10:0,
      washRooms:Math.round((a.ns+a.cx)/weeks),allowed,walked:a.walked,walkCost:a.cost},red:mrOversoldRed(worst[wi],allowed)});
  }
  return out;
}
export const overbookingWash=registerReport({
  id:'rpt.revenue.overbooking-wash',title:'Overbooking and wash',area:'revenue',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-F',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-F'},{key:'to',label:'To',type:'date',dateClass:'D-F'},{key:'roomType',label:'Room type',type:'text'},{key:'channel',label:'Channel',type:'text'}],
  columns:WASH_COLS,dateBasis:WASH_BASIS,formats:['screen','xlsx','pdf','csv'],phone:'no',
  select:(s:State,_uid:string,p:RptBoundParams)=>{
    const from=p.from!,to=p.to!;if(nights(from,to)+1>366)throw new HccError('VALIDATION','The range is longer than 366 days. Narrow the range.');
    const type=String(p.roomType??'');if(type&&!yldRoomTypes(s).includes(type))throw new HccError('VALIDATION',`Room type: choose one of ${yldRoomTypes(s).join(', ')}.`);
    const rows=washCompute(s,from,to,type,String(p.channel??''));
    return {params:p,columns:WASH_COLS,rows:mrRows(rows),...(rows.length?{}:{about:{dateBasis:WASH_BASIS+CAPTURE_NOTE('walked-guest log and yld.overbook')}})};
  },
});
registerMeasReport('rpt.revenue.overbooking-wash','meas.rev.overbooking');
