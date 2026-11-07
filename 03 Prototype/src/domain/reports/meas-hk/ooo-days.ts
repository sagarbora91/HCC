// F12 MEAS · rpt.rooms.ooo-days — Out-of-order and lost inventory (catalogue §3.10; switch meas.hk.ooo).
// Data: room out-of-order windows and blocks, the captured offline reason / work-order link (s.measX.offline), Maintenance work orders, stays.
// Money: the "Lost revenue at ADR" column is money (catalogue says "behind PERM-RPT-002"), so the report carries moneyFlag with gate PERM-RPT-002
// and Housekeeping / Maintenance / Supervisor see no money (DEC-061). Red row: OOO more than 7 days without a work order; sellable but idle 14+ nights.
import type {State,Room} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {addDays} from '../../money';
import {mntSlice} from '../../streams/maintenance';
import {MEAS_OFFLINE_LABEL,MEAS_OFFLINE_REASONS} from '../../streams/housekeeping';
import {measTarget} from '../../streams/measure';
import {T,I,M,mkRow,captureNote,rangeGuard,eachDay} from './shared';

export const OOO_NO_TICKET_DAYS=7;  // more than this many days without a work order is red
export const OOO_IDLE_NIGHTS=14;    // sellable and unoccupied this many nights is red
export const OOO_COLS=[T('room','Room'),T('reason','Reason'),I('roomDays','Room-days'),T('workOrder','Work order'),I('daysNoTicket','Days without ticket'),M('lostRevenue','Lost revenue at ADR'),I('idleNights','Idle 14+ nights while sellable')];

/** The nights [from, to) a room is off the sellable inventory because of its out-of-order window or block, or null. */
export function oooWindow(r:Room):{from?:string;to?:string}|null{
  if(r.ooo)return {from:r.ooo.from,to:r.ooo.to};
  if(r.blocked)return {from:r.blockFrom,to:r.blockTo};
  return null;
}
/** Nights of [from, to] (both inclusive business dates) the window covers. */
export function oooDays(w:{from?:string;to?:string},from:string,to:string):number{
  const a=w.from&&w.from>from?w.from:from,endEx=w.to&&w.to<=addDays(to,1)?w.to:addDays(to,1);
  return a<endEx?Math.round((Date.parse(endEx)-Date.parse(a))/86400000):0;
}
/** Consecutive unoccupied nights of a sellable room ending on `to`, never before `from`. */
export function idleNights(s:State,room:string,from:string,to:string):number{
  const stays=s.bookings.filter(b=>b.room===room&&(b.status==='In house'||b.status==='Departed'));
  let n=0;
  for(const d of eachDay(from,to).reverse()){if(stays.some(b=>b.arrival<=d&&b.departure>d))break;n++;}
  return n;
}
const BASIS='Room-days out of the sellable inventory on the business dates of the range (up to 92 days), from the room out-of-order window or block; reason and work order as captured by the housekeeping supervisor. Days without ticket = room-days when no work order is linked. Lost revenue = room-days × the ADR target before GST. Idle = consecutive unoccupied nights of a sellable room up to the To date (never before the From date). Red: out of order more than 7 days without a work order, or idle 14+ nights.';

export const oooDaysReport=registerReport({
  id:'rpt.rooms.ooo-days',title:'Out-of-order and lost inventory',area:'rooms',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-HK-001'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'reason',label:'Reason',type:'enum',options:[...MEAS_OFFLINE_REASONS]}],
  columns:OOO_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    const captured=s.measX?.offline??{};
    if(!Object.keys(captured).length)return {params:p,columns:OOO_COLS,rows:[],about:{dateBasis:captureNote(BASIS,'the offline reason, expected return and work-order link (meas-cap-hk)')}};
    const adr=measTarget(s,'meas.target.adr')??0,orders=mntSlice(s).orders;
    const rows:RptRow[]=[];
    for(const r of [...s.rooms].filter(r=>r.status!=='retired').sort((a,b)=>a.id.localeCompare(b.id,undefined,{numeric:true}))){
      const w=oooWindow(r),days=w?oooDays(w,p.from!,p.to!):0,cap=captured[r.id];
      if(days>0){
        const code=cap?.reason??'OOO';if(p.reason&&p.reason!==code)continue;
        const wo=cap?.workOrder??orders.filter(o=>o.room===r.id&&o.status!=='Cancelled'&&o.createdAt.slice(0,10)<=p.to!).sort((a,b)=>b.createdAt.localeCompare(a.createdAt))[0]?.id??'';
        const noTicket=wo?0:days;
        rows.push(mkRow({room:r.id,reason:MEAS_OFFLINE_LABEL[code],roomDays:days,workOrder:wo,daysNoTicket:noTicket,lostRevenue:days*adr,idleNights:0},noTicket>OOO_NO_TICKET_DAYS));
      }else if(!w&&!p.reason){
        const idle=idleNights(s,r.id,p.from!,p.to!);
        if(idle>=OOO_IDLE_NIGHTS)rows.push(mkRow({room:r.id,reason:'',roomDays:0,workOrder:'',daysNoTicket:0,lostRevenue:0,idleNights:idle},true));
      }
    }
    return {params:p,columns:OOO_COLS,rows};
  },
});
