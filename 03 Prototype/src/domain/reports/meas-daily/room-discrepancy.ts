// F12-RPT-DAILY · rpt.rooms.room-discrepancy (catalogue 1.4, meas.daily.discrepancy). Front-office status against housekeeping / master status.
// Kinds: sold-but-vacant (reservation due in, check-in time passed, not checked in), vacant-but-occupied (stay-over task open on a room with no stay),
// OOO-with-stay (a stay in a room that is out of order). sleep-out has no data source yet (no key / door capture) and never fires.
// Exception: a mismatch older than 2 hours (120 minutes; exactly 2 h is not red). Server twin: Reports/MeasDaily/RoomDiscrepancyReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {rmsOooDuring,rmsListed} from '../../streams/roomoffline';
import {addDays} from '../../money';
import {T,I,EXC,hoursBetween} from './shared';

export const DISCREPANCY_COLS:RptColumn[]=[T('room','Room'),T('foStatus','Front-office status'),T('hkStatus','Housekeeping status'),T('kind','Mismatch kind'),T('since','Since'),I('ageHours','Age hours'),EXC()];
export const DISCREPANCY_KEY='meas.daily.discrepancy';
export const DISCREPANCY_LIMIT_MINUTES=120;
const instant=(x:string)=>Date.parse(x.slice(0,10)+'T'+x.slice(11,16)+':00Z');
export const ageMinutes=(since:string,now:string)=>Math.max(0,Math.floor((instant(now)-instant(since))/60000));

export function discrepancyRows(s:State,at?:string):RptRow[]{
  const now=(at&&/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}$/.test(at)?at:s.clock).replace(' ','T'),date=now.slice(0,10);
  const out:{room:string;fo:string;hk:string;kind:string;since:string}[]=[];
  for(const r of s.rooms.filter(rmsListed)){
    const stay=s.bookings.find(b=>b.status==='In house'&&b.room===r.id);
    const off=rmsOooDuring(r,date,addDays(date,1))||s.measX?.offline?.[r.id]?.reason==='OOO'||s.measX?.offline?.[r.id]?.reason==='OOS';
    if(stay&&off){
      const from=r.ooo?.from??date;
      out.push({room:r.id,fo:'Occupied',hk:'Out of order',kind:'OOO-with-stay',since:(from>(stay.checkedInAt??'').slice(0,10)?from+'T00:00':stay.checkedInAt??`${stay.arrival}T${s.settings.checkin}`).slice(0,16)});
      continue;
    }
    if(!stay){
      const due=s.bookings.find(b=>b.status==='Confirmed'&&b.room===r.id&&b.arrival<=date&&`${b.arrival}T${s.settings.checkin}`<now);
      if(due){out.push({room:r.id,fo:'Reserved, arrival due',hk:r.hk,kind:'sold-but-vacant',since:`${due.arrival}T${s.settings.checkin}`});continue;}
      const so=s.tasks.find(t=>t.room===r.id&&t.kind==='stayover'&&(t.status==='To do'||t.status==='In progress'));
      if(so)out.push({room:r.id,fo:'Vacant',hk:'Stay-over task open',kind:'vacant-but-occupied',since:(so.createdAt??now).slice(0,16)});
    }
  }
  return out.sort((a,b)=>a.room.localeCompare(b.room)).map(x=>{
    const mins=ageMinutes(x.since,now);
    return {kind:'row' as const,cells:{room:x.room,foStatus:x.fo,hkStatus:x.hk,kind:x.kind,since:x.since.replace('T',' '),ageHours:hoursBetween(x.since,now),
      exception:mins>DISCREPANCY_LIMIT_MINUTES?`${x.kind} for more than 2 hours`:''}};
  });
}
registerMeasReport('rpt.rooms.room-discrepancy',DISCREPANCY_KEY);
export const roomDiscrepancy=registerReport({
  id:'rpt.rooms.room-discrepancy',title:'Room discrepancy',area:'rooms',priority:'P1',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-HK-001'],dateClass:'D-NOW',
  params:[{key:'at',label:'As-of time (YYYY-MM-DDTHH:mm, default now)',type:'text'}],
  columns:DISCREPANCY_COLS,
  dateBasis:'The state now (the simulated hotel clock) unless an as-of time is given. Mismatch kinds: sold-but-vacant, vacant-but-occupied, OOO-with-stay (sleep-out has no data source yet). Exception: a mismatch older than 2 hours.',
  formats:['screen','pdf'],phone:'yes',
  select:(s,_uid,p)=>({params:p,columns:DISCREPANCY_COLS,rows:discrepancyRows(s,String(p.at??''))}),
});
