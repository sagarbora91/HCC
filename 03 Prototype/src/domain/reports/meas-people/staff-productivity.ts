// F12-RPT-PEOPLE · rpt.people.staff-productivity — staff per occupied room, overtime, absenteeism (catalogue §3.10, switch meas.people.productivity).
// Hours and days only; no pay amount. Data: HR attendance / roster / leave (hrCounts), stays (occupied room-nights). Server twin: StaffProductivityReport.cs.
import type {State} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {measTarget} from '../../streams/measure';
import {hrSlice,hrCounts,hrShiftFor,hrShiftInfo,hrApprovedLeavesOn,hrMonthDates} from '../../streams/staff';
import {T,I,P,EXC,pct1,hoursText,shiftMinutes,deptList,row} from './shared';

export const PRODUCTIVITY_ID='rpt.people.staff-productivity';
export const PRODUCTIVITY_COLS=[T('dept','Department'),I('headcount','Headcount'),T('hoursPerRoomNight','Staff-hours per occupied room-night'),P('otPct','OT %'),P('absentPct','Absenteeism %'),
  I('lateMarks','Late marks'),T('lowNightHours','Rostered hours on low-forecast nights'),EXC];

/** Pure rule inputs of one department (minutes, halves, room-nights): the exception rule is integer arithmetic only. */
export type ProdFigures={rosterMin:number;otMin:number;scheduledDays:number;absentHalves:number;occRoomNights:number;lowRosterMin:number;lowRooms:number};
export type ProdRule={ot:boolean;absent:boolean;overRostered:boolean};
/** OT above target % of rostered minutes; absenteeism above target % of scheduled days; rostered hours on low-forecast nights above the need by 20 %. */
export function productivityRule(f:ProdFigures,otTarget:number,absentTarget:number):ProdRule{
  const ot=f.rosterMin>0&&f.otMin*100>otTarget*f.rosterMin;
  const absent=f.scheduledDays>0&&f.absentHalves*100>absentTarget*2*f.scheduledDays;
  // need on the low nights = rooms sold those nights x the month's hours per occupied room-night; red above need x 1.2
  const overRostered=f.lowRosterMin>0&&(f.occRoomNights===0||f.lowRosterMin*f.occRoomNights*100>120*f.lowRooms*f.rosterMin);
  return {ot,absent,overRostered};
}

export function productivityRows(s:State,from:string,to:string,deptFilter?:string):RptRow[]{
  const x=hrSlice(s),ym=from.slice(0,7),dates=hrMonthDates(ym).filter(d=>d>=from&&d<=to);
  const sellable=s.rooms.filter(r=>!r.blocked).length;
  const occ=new Map<string,number>();
  for(const d of dates)occ.set(d,s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&b.arrival<=d&&b.departure>d).length);
  const occRoomNights=[...occ.values()].reduce((a,n)=>a+n,0);
  const otTarget=measTarget(s,'meas.target.ot')!,absTarget=measTarget(s,'meas.target.absenteeism')!;
  const lowDates=new Set(dates.filter(d=>sellable>0&&occ.get(d)!*100<(measTarget(s,'meas.target.occupancy')??35)*sellable));
  const out:RptRow[]=[];const tot:ProdFigures&{head:number;late:number}={rosterMin:0,otMin:0,scheduledDays:0,absentHalves:0,occRoomNights,lowRosterMin:0,lowRooms:[...lowDates].reduce((a,d)=>a+occ.get(d)!,0),head:0,late:0};
  for(const d of deptList(s,deptFilter)){
    const f:ProdFigures&{head:number;late:number}={rosterMin:0,otMin:0,scheduledDays:0,absentHalves:0,occRoomNights,lowRosterMin:0,lowRooms:tot.lowRooms,head:0,late:0};
    for(const st of x.staff.filter(z=>z.dept===d.id&&z.joined<=to&&(!z.exitedOn||z.exitedOn>=from))){
      f.head++;
      const c=hrCounts(s,st,ym,to);
      f.otMin+=c.otMin;f.late+=c.L;f.scheduledDays+=c.P+c.L+c.H+c.HL+c.A+c.paidLeave+c.lopLeave+c.unmarked;f.absentHalves+=Math.round(2*c.A)+c.H+c.HL;
      for(const date of dates){
        if(date<st.joined||(st.exitedOn&&date>st.exitedOn))continue;
        const code=hrShiftFor(s,st,date);if(code==='OFF')continue;
        if(hrApprovedLeavesOn(s,st.id,date).some(l=>!l.half))continue;
        const info=hrShiftInfo(code,s),m=shiftMinutes(info.start,info.end);
        f.rosterMin+=m;if(lowDates.has(date))f.lowRosterMin+=m;
      }
    }
    if(!f.head)continue;
    const r=productivityRule(f,otTarget,absTarget);
    out.push(row({dept:d.name,headcount:f.head,hoursPerRoomNight:occRoomNights?(f.rosterMin/60/occRoomNights).toFixed(2):'0.00',otPct:pct1(f.otMin,f.rosterMin),absentPct:pct1(f.absentHalves,2*f.scheduledDays),
      lateMarks:f.late,lowNightHours:hoursText(f.lowRosterMin),exception:r.ot||r.absent||r.overRostered}));
    for(const k of ['rosterMin','otMin','scheduledDays','absentHalves','lowRosterMin','head','late'] as const)tot[k]+=f[k];
  }
  if(out.length){
    const r=productivityRule(tot,otTarget,absTarget);
    out.push(row({dept:'All departments',headcount:tot.head,hoursPerRoomNight:occRoomNights?(tot.rosterMin/60/occRoomNights).toFixed(2):'0.00',otPct:pct1(tot.otMin,tot.rosterMin),absentPct:pct1(tot.absentHalves,2*tot.scheduledDays),
      lateMarks:tot.late,lowNightHours:hoursText(tot.lowRosterMin),exception:r.ot||r.absent||r.overRostered},'total'));
  }
  return out;
}

export const staffProductivity=registerReport({
  id:PRODUCTIVITY_ID,title:'Staff per occupied room, overtime, absenteeism',area:'people',priority:'P2',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-HR-001'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'},{key:'dept',label:'Department',type:'text'}],
  columns:PRODUCTIVITY_COLS,
  dateBasis:'Rostered hours = shift length on every rostered day outside weekly offs and full-day leave. OT % = overtime minutes / rostered minutes. Absenteeism % = absent days (half-day = 1/2) / scheduled days. Occupied room-night = a stay that slept in a room that night. Low-forecast night = occupancy below the occupancy target; the need on those nights = rooms sold x the month\'s staff-hours per occupied room-night. Red: OT above the OT target, absenteeism above the absenteeism target, or rostered hours on low nights above that need by 20 %. Hours and days only: no pay amount.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const from=p.from!,to=p.to!;
    return {params:p,columns:PRODUCTIVITY_COLS,rows:productivityRows(s,from,to,typeof p.dept==='string'?p.dept:undefined)};
  },
});
