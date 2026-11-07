// F12 MEAS · rpt.rooms.hk-productivity — Attendant productivity and turnaround (catalogue §3.10; switch meas.hk.productivity). Money flag n.
// Data: housekeeping tasks (departure = checkout, stayover) with the captured start / finish / inspection times (s.measX.taskTimes, meas-cap-hk).
// Red row: pass rate under 95 %; median departure room over 45 minutes; a same-day re-let not inspected before arrival.
import type {State,Task} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {hkTaskKind,measTaskMinutes} from '../../streams/housekeeping';
import {T,I,P,mkRow,captureNote,median,mean,r1,minutesBetween,rangeGuard,userName} from './shared';

export const HK_SHIFTS=['Morning','Afternoon','Night'] as const;
export const HK_PASS_MIN_PCT=95;       // pass rate under this is red
export const HK_DEPARTURE_MAX_MIN=45;  // median departure-room minutes above this is red
export const HK_PRODUCTIVITY_COLS=[T('attendant','Attendant'),T('shift','Shift'),I('departures','Departure rooms'),I('stayovers','Stayover rooms'),I('minutesPerRoom','Minutes per room'),
  I('vacatedToInspected','Vacated-to-inspected minutes'),P('passPct','Inspection pass %'),T('rejections','Rejections by reason'),T('relet','Same-day re-let compliance')];

/** Shift of a task by the hour it started: Morning 06:00-13:59, Afternoon 14:00-21:59, Night otherwise. */
export function hkShiftOf(startedAt?:string):typeof HK_SHIFTS[number]{
  const h=startedAt?Number(startedAt.slice(11,13)):8;
  return h>=6&&h<14?'Morning':h>=14&&h<22?'Afternoon':startedAt?'Night':'Morning';
}
const taskDate=(s:State,t:Task)=>t.date??s.measX?.taskTimes?.[t.id]?.startedAt?.slice(0,10)??t.createdAt?.slice(0,10)??s.date;

export type HkGroup={attendant:string;shift:string;dep:Task[];stay:Task[]};
/** The rule evaluation of one attendant/shift group — exported so tests and the Owner digest use the very same code. */
export function hkProductivityGroup(s:State,g:HkGroup){
  const tt=s.measX?.taskTimes??{};
  const all=[...g.dep,...g.stay];
  const mins=all.map(t=>measTaskMinutes(tt[t.id])).filter((x):x is number=>x!==null);
  const depMins=g.dep.map(t=>measTaskMinutes(tt[t.id])).filter((x):x is number=>x!==null);
  const vac=g.dep.map(t=>{const x=tt[t.id];return t.createdAt&&x?.inspectedAt?minutesBetween(t.createdAt,x.inspectedAt):null;}).filter((x):x is number=>x!==null&&x>=0);
  const inspected=all.filter(t=>tt[t.id]?.inspectResult),pass=inspected.filter(t=>tt[t.id]!.inspectResult==='pass').length;
  const reasons=new Map<string,number>();
  for(const t of inspected)if(tt[t.id]!.inspectResult==='fail'){const r=tt[t.id]!.rejectReason||'No reason';reasons.set(r,(reasons.get(r)||0)+1);}
  let relets=0,ok=0;
  for(const t of g.dep){
    const d=taskDate(s,t);
    const b=s.bookings.find(x=>x.room===t.room&&x.arrival===d&&x.status!=='Cancelled'&&x.status!=='No show');
    if(!b)continue;relets++;
    const at=b.checkedInAt??`${d}T${s.settings.checkin}`,i=tt[t.id]?.inspectedAt;
    if(i&&tt[t.id]?.inspectResult==='pass'&&Date.parse(i)<=Date.parse(at))ok++;
  }
  const medianDep=median(depMins);
  const red=(inspected.length>0&&pass*100<HK_PASS_MIN_PCT*inspected.length)||(medianDep!==null&&medianDep>HK_DEPARTURE_MAX_MIN)||ok<relets;
  return {mins,vac,inspected:inspected.length,pass,reasons,relets,ok,medianDep,red};
}

const BASIS='Housekeeping tasks by the business date they were made for (range up to 31 days). Shift by the hour the task started (Morning 06:00, Afternoon 14:00, Night 22:00). Minutes per room = start to finish. Vacated-to-inspected = task created at departure to inspection. Re-let = a guest arriving the same day in the cleaned room; compliant when passed before arrival. Red: pass rate under 95 %, median departure room over 45 minutes, or a re-let not inspected before arrival.';
export const hkProductivity=registerReport({
  id:'rpt.rooms.hk-productivity',title:'Attendant productivity and turnaround',area:'rooms',priority:'P1',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-HK-001'],dateClass:'D-S',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-S'},{key:'to',label:'To',type:'date',dateClass:'D-S'},{key:'attendant',label:'Attendant',type:'user'},{key:'shift',label:'Shift',type:'enum',options:[...HK_SHIFTS]}],
  columns:HK_PRODUCTIVITY_COLS,
  dateBasis:BASIS,
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,31,s.date);
    const tt=s.measX?.taskTimes??{};
    if(!Object.keys(tt).length){return {params:p,columns:HK_PRODUCTIVITY_COLS,rows:[],about:{dateBasis:captureNote(BASIS,'task start, finish and inspection times (meas-cap-hk)')}};}
    const groups=new Map<string,HkGroup>();
    for(const t of s.tasks){
      const kind=hkTaskKind(t);if(kind!=='checkout'&&kind!=='stayover')continue;
      const d=taskDate(s,t);if(d<p.from!||d>p.to!)continue;
      if(p.attendant&&t.assignee!==p.attendant)continue;
      const shift=hkShiftOf(tt[t.id]?.startedAt??t.startedAt);if(p.shift&&shift!==p.shift)continue;
      const k=t.assignee+'|'+shift;const g=groups.get(k)??{attendant:t.assignee,shift,dep:[],stay:[]};
      (kind==='checkout'?g.dep:g.stay).push(t);groups.set(k,g);
    }
    const rows:RptRow[]=[...groups.values()].sort((a,b)=>userName(s,a.attendant).localeCompare(userName(s,b.attendant))||HK_SHIFTS.indexOf(a.shift as 'Morning')-HK_SHIFTS.indexOf(b.shift as 'Morning')).map(g=>{
      const x=hkProductivityGroup(s,g);const mpr=mean(x.mins),vac=median(x.vac);
      return mkRow({attendant:userName(s,g.attendant),shift:g.shift,departures:g.dep.length,stayovers:g.stay.length,minutesPerRoom:mpr===null?null:Math.round(mpr),
        vacatedToInspected:vac===null?null:Math.round(vac),passPct:x.inspected?r1(x.pass*100/x.inspected):null,
        rejections:[...x.reasons].sort((a,b)=>b[1]-a[1]||a[0].localeCompare(b[0])).map(([r,n])=>`${r} ×${n}`).join('; '),
        relet:x.relets?`${x.ok} of ${x.relets}`:'-'},x.red);
    });
    return {params:p,columns:HK_PRODUCTIVITY_COLS,rows};
  },
});
