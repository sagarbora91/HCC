// F8-RPT-P2-A · rpt.rooms.rooms-left — Rooms left by type (catalogue A7). Rooms still free per room type per date. No money. Class D-FWD (forward dates allowed, catalogue note).
// free = total - occupied - held (unpicked group blocks) - out of order, never below 0.
import {registerReport} from '../catalogue';
import {addDays} from '../../money';
import {blockedDuring} from '../../selectors';
import {rmsOffline,rmsRetiredDuring} from '../../streams/roomoffline';
import {grpHolds} from '../../streams/groups';
import {T,I,D,row,rangeGuard,roomOnNight,natural} from './common';

export const ROOMS_LEFT_COLS=[D('date','Date'),T('roomType','Room type'),I('total','Total rooms'),I('occupied','Occupied'),I('held','Held (group blocks)'),I('ooo','Out of order'),I('free','Free')];

export const roomsLeft=registerReport({
  id:'rpt.rooms.rooms-left',title:'Rooms left by type',area:'rooms',priority:'P2',status:'built',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],dateClass:'D-FWD',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-FWD'},{key:'to',label:'To',type:'date',dateClass:'D-FWD'},{key:'roomType',label:'Room type',type:'text'}],
  columns:ROOMS_LEFT_COLS,
  dateBasis:'Per night (range up to 31 days, up to 90 days ahead). Total = rooms of the type in service; occupied = rooms booked or in house that night (confirmed, in house, departed, or a hold still running); held = rooms kept for open group blocks and not yet picked up; out of order = blocked or offline rooms. Free = what is left.',
  formats:['screen','xlsx','pdf'],phone:'yes',legacyIds:['A7'],
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,31,s.date,90);
    const q=String(p.roomType??'').trim().toLowerCase();
    const types=[...new Set(s.rooms.map(r=>r.type))].filter(t=>!q||t.toLowerCase()===q).sort((a,b)=>a.localeCompare(b));
    const holding=(b:{status:string;holdUntil?:string})=>b.status==='Confirmed'||b.status==='In house'||b.status==='Departed'||(b.status==='Held'&&(!b.holdUntil||s.clock<b.holdUntil));
    const holds=grpHolds(s);const rows=[];
    for(let d=p.from!;d<=p.to!;d=addDays(d,1)){
      for(const t of types){
        const inType=s.rooms.filter(r=>r.type===t&&!rmsRetiredDuring(r,d,addDays(d,1)));
        const ooo=inType.filter(r=>rmsOffline(r,d,addDays(d,1))||blockedDuring(r,d,addDays(d,1)));
        const live=inType.filter(r=>!ooo.includes(r));
        const occupied=live.filter(r=>s.bookings.some(b=>holding(b)&&b.arrival<=d&&d<b.departure&&roomOnNight(b,d)===r.id)).length;
        const held=holds.filter(h=>h.type===t&&h.a<=d&&d<h.d).length;
        rows.push(row('row',{date:d,roomType:t,total:inType.length,occupied,held,ooo:ooo.length,free:Math.max(0,inType.length-ooo.length-occupied-held)},d));
      }
    }
    void natural;
    return {params:p,columns:ROOMS_LEFT_COLS,rows};
  },
});
