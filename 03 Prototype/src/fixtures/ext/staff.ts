// Module 17 · HR & payroll — fixture extensions (fictional data only).
// 12 fictional staff (8 linked to demo users, 4 without login), deterministic attendance for Sep 2026 and 1–23 Oct 2026,
// a locked August 2026 payroll run, pending/approved leave (one half-day) and one open advance. No money is posted to bookings or the drawer.
// W05-B: gender (Maharashtra PT slab), fictional bank details, SCC statutory rate version (from 1 Apr 2025) and SCC leave policy.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {hrEmpty,hrDefaultLeavePolicy,hrKey,hrShiftFor,hrMonthDates,hrComputeRun,hrRatesFor,hrApplyRecoveries,hrLeaveDays,type HRStaff,type HRState} from '../../domain/streams/staff';

type Row=[string,string,string,string|undefined,'monthly'|'daily',number,HRStaff['defaultShift'],number,string,'M'|'F'];
// id, name, dept, userId, payType, rupees (monthly or daily), shift, weeklyOff, designation, gender (PT slab)
const STAFF:Row[]=[
  ['HR-S01','Meera Kulkarni','MGT','manager','monthly',45000,'G',0,'Hotel manager','F'],
  ['HR-S02','Aarav Joshi','FO','reception','monthly',18000,'M',2,'Front office associate','M'],
  ['HR-S03','Priya Nair','FO','cashier','monthly',17000,'E',3,'Front office cashier','F'],
  ['HR-S04','Neha Gaikwad','HK','supervisor','monthly',22000,'M',4,'Housekeeping supervisor','F'],
  ['HR-S05','Ravi Shinde','HK','housekeeper','monthly',13500,'M',5,'Room attendant','M'],
  ['HR-S06','Kavya Patil','HK','housekeeper2','monthly',13500,'E',1,'Room attendant','F'],
  ['HR-S07','Farhan Qureshi','ACC','accountant','monthly',28000,'G',0,'Accountant','M'],
  ['HR-S08','Joseph D’Souza','MNT','maint','monthly',19000,'G',6,'Maintenance technician','M'],
  ['HR-S09','Suresh Pawar','KIT',undefined,'monthly',16000,'M',3,'Cook','M'],
  ['HR-S10','Imran Shaikh','KIT',undefined,'daily',600,'M',2,'Kitchen helper (daily wage)','M'],
  ['HR-S11','Balu Jadhav','SEC',undefined,'monthly',12000,'G',4,'Security guard (day)','M'],
  ['HR-S12','Ganesh More','SEC',undefined,'monthly',12500,'N',1,'Security guard (night)','M'],
];
const SHIFT_START:Record<string,string>={M:'07:00',E:'15:00',N:'23:00',G:'09:00'};
/** Deterministic attendance for every rostered working day in the given dates. */
function genAttendance(s:State,x:HRState,dates:string[]){
  x.staff.forEach((st,i)=>dates.forEach(d=>{
    if(hrShiftFor(s,st,d)==='OFF')return;
    if(x.leaves.some(l=>l.staffId===st.id&&l.status==='Approved'&&l.from<=d&&l.to>=d))return;
    const day=+d.slice(8,10)+(+d.slice(5,7))*31,h=(i*37+day*23)%101;
    const status=h<3?'A':h<6?'H':h<12?'L':'P';
    const ot=status==='P'&&h%13===0?120:status==='P'&&h%17===0?60:0;
    x.att[hrKey(d,st.id)]={status,at:d+'T'+(status==='L'?SHIFT_START[st.defaultShift].replace(':00',':25'):SHIFT_START[st.defaultShift]),otMin:ot,by:'manager'};
  }));
}
export function extendBase(s:State):void{
  const x=hrEmpty();s.staffX=x;
  x.depts=[{id:'MGT',name:'Management'},{id:'FO',name:'Front office'},{id:'HK',name:'Housekeeping'},{id:'MNT',name:'Maintenance'},
    {id:'KIT',name:'Kitchen'},{id:'SEC',name:'Security'},{id:'ACC',name:'Accounts'}];
  x.staff=STAFF.map(([id,name,dept,userId,payType,rs,defaultShift,weeklyOff,designation,gender],i)=>({id,name,dept,designation,...(userId?{userId}:{}),payType,
    monthly:payType==='monthly'?rs*100:0,daily:payType==='daily'?rs*100:0,defaultShift,weeklyOff,
    joined:i===9?'2026-06-01':'2025-04-01',phone:'90000 '+String(10001+i*7).padStart(5,'0'),active:true,gender,
    bankName:'Demo Co-operative Bank (fictional)',accountNo:'000012'+String(340001+i*13),ifsc:'DEMO0000'+String(101+i%3)}));
  x.leavePolicy=hrDefaultLeavePolicy();
  const ravi=x.staff[4];
  x.advances=[{id:'HR-ADV-1',staffId:ravi.id,amount:300000,installment:100000,recovered:0,date:'2026-08-10',reason:'Festival advance (fictional)',by:'owner',recoveries:[]}];
  // August 2026: generate attendance, compute and lock the run (prepared by accountant, locked by owner), then drop the raw August marks.
  const aug=hrMonthDates('2026-08');genAttendance(s,x,aug);
  const a=hrComputeRun(s,'2026-08');
  const run={id:'HR-PAY-1',month:'2026-08',status:'Locked' as const,rates:{...hrRatesFor(s,'2026-08')},lines:a.lines,warnings:a.warnings,
    preparedBy:'accountant',preparedAt:'2026-09-02T11:10',lockedBy:'owner',lockedAt:'2026-09-02T17:40'};
  x.runs.push(run);hrApplyRecoveries(x,run);
  for(const k of Object.keys(x.att))if(k.startsWith('2026-08'))delete x.att[k];
  // Leave: past approved (September), upcoming approved, two pending (one raised by the manager → owner must decide).
  const leave=(id:string,st:HRStaff,type:'CL'|'SL'|'EL'|'LOP',from:string,to:string,reason:string,status:'Pending'|'Approved',requestedBy:string,decidedBy?:string,half?:'AM'|'PM')=>
    x.leaves.push({id,staffId:st.id,type,from,to,days:hrLeaveDays(s,st,from,to,half),reason,...(half?{half}:{}),status,requestedBy,requestedAt:from<'2026-10-01'?'2026-09-08T10:00':'2026-10-22T09:30',
      ...(decidedBy?{decidedBy,decidedAt:from<'2026-10-01'?'2026-09-08T12:00':'2026-10-22T12:15'}:{})});
  leave('HR-LV-1',ravi,'CL','2026-09-14','2026-09-15','Family function (fictional)','Approved','housekeeper','manager');
  leave('HR-LV-2',x.staff[1],'EL','2026-11-02','2026-11-03','Short trip home (fictional)','Approved','reception','manager');
  leave('HR-LV-3',x.staff[5],'CL','2026-10-28','2026-10-29','Personal work (fictional)','Pending','housekeeper2');
  leave('HR-LV-4',x.staff[9],'LOP','2026-10-27','2026-10-27','Village visit (fictional)','Pending','manager');
  leave('HR-LV-5',x.staff[3],'CL','2026-11-06','2026-11-06','Bank work, afternoon (fictional)','Pending','supervisor',undefined,'PM');
  // September complete, October up to the day before the business date.
  genAttendance(s,x,[...hrMonthDates('2026-09'),...hrMonthDates('2026-10').filter(d=>d<s.date)]);
  // Today (24 Oct, 08:00): two morning check-ins already recorded.
  x.att[hrKey(s.date,'HR-S05')]={status:'P',at:s.date+'T06:55',otMin:0,by:'housekeeper',note:'Self check-in'};
  x.att[hrKey(s.date,'HR-S09')]={status:'P',at:s.date+'T06:50',otMin:0,by:'manager'};
  // Roster: one swap next week to show overrides.
  x.roster[hrKey('2026-10-27','HR-S06')]='M';x.roster[hrKey('2026-10-27','HR-S05')]='E';
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];
