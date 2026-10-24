// Module 17 · HR & payroll — pure helpers, selectors and the state slice type. Exported names MUST start with 'hr' / 'HR' (shared mock namespace).
// All deduction rates are synthetic placeholders: "illustrative — not statutory advice". Money = integer paise.
import type {State,Role} from '../types';
import {addDays} from '../money';
import {can} from '../selectors';

export type HRShiftCode='M'|'E'|'N'|'G'|'OFF';
export type HRAttStatus='P'|'L'|'H'|'A'|'OFF';
export type HRLeaveType='CL'|'SL'|'EL'|'LOP';
export type HRDept={id:string;name:string};
export type HRStaff={id:string;name:string;dept:string;designation:string;
  /** Linked demo user (login); undefined = non-login staff (cooks, security…). */
  userId?:string;
  payType:'monthly'|'daily';
  /** Monthly salary (paise) — monthly pay type. */ monthly:number;
  /** Daily wage (paise) — daily pay type. */ daily:number;
  defaultShift:Exclude<HRShiftCode,'OFF'>;
  /** Weekly off: 0 = Sunday … 6 = Saturday. */ weeklyOff:number;
  joined:string;phone:string;active:boolean;exitedOn?:string};
export type HRAtt={status:HRAttStatus;at:string;otMin:number;by:string;note?:string};
export type HRLeave={id:string;staffId:string;type:HRLeaveType;from:string;to:string;days:number;reason:string;
  status:'Pending'|'Approved'|'Rejected'|'Cancelled';requestedBy:string;requestedAt:string;decidedBy?:string;decidedAt?:string;note?:string};
export type HRAdvance={id:string;staffId:string;amount:number;installment:number;recovered:number;date:string;reason:string;by:string;recoveries:{runId:string;amount:number}[]};
/** Illustrative payroll configuration — NOT statutory rates. bps = basis points (1% = 100). */
export type HRRates={basicBps:number;pfBps:number;pfCap:number;esicBps:number;esicCeiling:number;pt:number;ptThreshold:number;otBps:number;lateBlock:number};
export type HRCounts={days:number;P:number;L:number;H:number;A:number;OFFW:number;off:number;paidLeave:number;lopLeave:number;unmarked:number;na:number;otMin:number};
export type HRLine={staffId:string;name:string;dept:string;designation:string;payType:'monthly'|'daily';base:number;
  counts:HRCounts;latePenaltyHalf:number;payableDays:number;basic:number;ot:number;gross:number;pf:number;esic:number;pt:number;advance:number;deductions:number;net:number};
export type HRRun={id:string;month:string;status:'Draft'|'Locked';rates:HRRates;lines:HRLine[];warnings:string[];
  preparedBy:string;preparedAt:string;lockedBy?:string;lockedAt?:string};
/** State slice for this module, stored at s.staffX (optional; absent in old saves → treat as empty). */
export type HRState={depts:HRDept[];staff:HRStaff[];
  /** Roster overrides keyed "YYYY-MM-DD|staffId". */ roster:Record<string,HRShiftCode>;
  /** Attendance keyed "YYYY-MM-DD|staffId". */ att:Record<string,HRAtt>;
  leaves:HRLeave[];advances:HRAdvance[];rates:HRRates;runs:HRRun[]};

export const HR_DISCLAIMER='Illustrative — not statutory advice. Synthetic placeholder rates; no PF/ESIC/PT compliance claim.';
export const hrDefaultRates=():HRRates=>({basicBps:5000,pfBps:1200,pfCap:1500000,esicBps:75,esicCeiling:2100000,pt:20000,ptThreshold:1000000,otBps:15000,lateBlock:3});
export const hrEmpty=():HRState=>({depts:[],staff:[],roster:{},att:{},leaves:[],advances:[],rates:hrDefaultRates(),runs:[]});
export const hrSlice=(s:State):HRState=>s.staffX??hrEmpty();
/** For handlers: returns the slice, creating it on the (cloned) state if absent. */
export function hrEnsure(s:State):HRState{if(!s.staffX)s.staffX=hrEmpty();return s.staffX;}

export const HR_SHIFTS:{code:HRShiftCode;label:string;start:string;end:string}[]=[
  {code:'M',label:'Morning',start:'07:00',end:'15:00'},{code:'E',label:'Evening',start:'15:00',end:'23:00'},
  {code:'N',label:'Night',start:'23:00',end:'07:00'},{code:'G',label:'General',start:'09:00',end:'18:00'},{code:'OFF',label:'Weekly off',start:'',end:''}];
export const hrShiftInfo=(c:HRShiftCode)=>HR_SHIFTS.find(x=>x.code===c)!;
export const HR_ATT_LABEL:Record<HRAttStatus,string>={P:'Present',L:'Late',H:'Half-day',A:'Absent',OFF:'Worked on off day'};
export const HR_LEAVE_LABEL:Record<HRLeaveType,string>={CL:'Casual leave',SL:'Sick leave',EL:'Earned leave',LOP:'Leave without pay'};
/** Illustrative yearly entitlement (days) for paid leave types. */
export const HR_LEAVE_ENTITLEMENT:Record<'CL'|'SL'|'EL',number>={CL:8,SL:8,EL:12};
export const HR_GRACE_MIN=15;
export const HR_WEEKDAYS=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];

// ── Dates ──
export const hrWeekday=(d:string)=>new Date(d+'T12:00:00Z').getUTCDay();
/** Monday of the week containing d. */
export const hrMonday=(d:string)=>addDays(d,-((hrWeekday(d)+6)%7));
export const hrWeek=(monday:string)=>Array.from({length:7},(_,i)=>addDays(monday,i));
export const hrDaysInMonth=(ym:string)=>new Date(Date.UTC(+ym.slice(0,4),+ym.slice(5,7),0)).getUTCDate();
export const hrMonthDates=(ym:string)=>Array.from({length:hrDaysInMonth(ym)},(_,i)=>`${ym}-${String(i+1).padStart(2,'0')}`);
export const hrMonthEnd=(ym:string)=>`${ym}-${String(hrDaysInMonth(ym)).padStart(2,'0')}`;
export const hrPrevMonth=(ym:string)=>{const y=+ym.slice(0,4),m=+ym.slice(5,7);return m===1?`${y-1}-12`:`${y}-${String(m-1).padStart(2,'0')}`;};
export const hrMonthLabel=(ym:string)=>new Date(ym+'-15T12:00:00Z').toLocaleDateString('en-IN',{month:'long',year:'numeric',timeZone:'UTC'});
/** A month may be paid once it is complete: its last day is before the business date. */
export const hrMonthComplete=(s:State,ym:string)=>hrMonthEnd(ym)<s.date;
const toMin=(t:string)=>+t.slice(0,2)*60+ +t.slice(3,5);

// ── Roles / visibility ──
const PAYROLL_ROLES:Role[]=['Owner','Accountant'];
/** Roster, attendance for others, leave decisions, non-salary staff edits. */
export const hrCanManage=(s:State,uid:string)=>can(s,uid,'approve');
/** Salaries, payroll, advances, payslips of others. Role-based on purpose (brief: Owner / Accountant only). */
export function hrCanPay(s:State,uid:string){const u=s.users.find(x=>x.id===uid);return !!u&&PAYROLL_ROLES.includes(u.role)&&can(s,uid,'reports');}
/** Illustrative rate configuration: payroll role that also holds admin (Owner). */
export const hrCanRates=(s:State,uid:string)=>hrCanPay(s,uid)&&can(s,uid,'admin');
export const hrStaffOfUser=(s:State,uid:string)=>hrSlice(s).staff.find(x=>x.userId===uid);
export const hrStaffById=(s:State,id:string)=>hrSlice(s).staff.find(x=>x.id===id);
export const hrDeptName=(s:State,id:string)=>hrSlice(s).depts.find(d=>d.id===id)?.name??id;

// ── Roster / attendance ──
export const hrKey=(date:string,staffId:string)=>date+'|'+staffId;
export function hrShiftFor(s:State,st:HRStaff,date:string):HRShiftCode{
  const o=hrSlice(s).roster[hrKey(date,st.id)];if(o)return o;
  return hrWeekday(date)===st.weeklyOff?'OFF':st.defaultShift;
}
export const hrApprovedLeaveOn=(s:State,staffId:string,date:string)=>hrSlice(s).leaves.find(l=>l.staffId===staffId&&l.status==='Approved'&&l.from<=date&&l.to>=date);
/** Day view for one staff member: explicit attendance → approved leave → rostered off → unmarked. */
export function hrDayStatus(s:State,st:HRStaff,date:string):{kind:'att'|'leave'|'off'|'unmarked'|'na';att?:HRAtt;leave?:HRLeave}{
  if(date<st.joined||(st.exitedOn&&date>st.exitedOn))return {kind:'na'};
  const att=hrSlice(s).att[hrKey(date,st.id)];if(att)return {kind:'att',att};
  const leave=hrApprovedLeaveOn(s,st.id,date);if(leave)return {kind:'leave',leave};
  if(hrShiftFor(s,st,date)==='OFF')return {kind:'off'};
  return {kind:'unmarked'};
}
/** Self check-in status from the simulated clock: Late after shift start + grace. */
export function hrCheckinStatus(clock:string,shift:HRShiftCode):'P'|'L'{
  if(shift==='OFF'||shift==='N')return 'P';
  return toMin(clock.slice(11,16))>toMin(hrShiftInfo(shift).start)+HR_GRACE_MIN?'L':'P';
}
/** Counts for a staff member over the given dates (defaults to the whole month). */
export function hrCounts(s:State,st:HRStaff,ym:string,upTo?:string):HRCounts{
  const c:HRCounts={days:hrDaysInMonth(ym),P:0,L:0,H:0,A:0,OFFW:0,off:0,paidLeave:0,lopLeave:0,unmarked:0,na:0,otMin:0};
  for(const d of hrMonthDates(ym)){
    if(upTo&&d>upTo)break;
    const x=hrDayStatus(s,st,d);
    if(x.kind==='na')c.na++;
    else if(x.kind==='att'){const a=x.att!;c.otMin+=a.otMin||0;if(a.status==='OFF')c.OFFW++;else c[a.status]++;}
    else if(x.kind==='leave'){if(x.leave!.type==='LOP')c.lopLeave++;else c.paidLeave++;}
    else if(x.kind==='off')c.off++;
    else c.unmarked++;
  }
  return c;
}

// ── Leave ──
/** Leave days in [from,to], excluding rostered weekly offs. */
export function hrLeaveDays(s:State,st:HRStaff,from:string,to:string){let n=0;for(let d=from;d<=to;d=addDays(d,1))if(hrShiftFor(s,st,d)!=='OFF')n++;return n;}
/** Paid leave used (approved + pending) in a calendar year, per type. */
export function hrLeaveUsed(s:State,staffId:string,year:string,type:HRLeaveType,except=''){
  return hrSlice(s).leaves.filter(l=>l.id!==except&&l.staffId===staffId&&l.type===type&&l.from.startsWith(year)&&(l.status==='Approved'||l.status==='Pending')).reduce((n,l)=>n+l.days,0);
}
export function hrLeaveBalance(s:State,staffId:string,year:string){
  return (['CL','SL','EL'] as const).map(t=>({type:t,entitled:HR_LEAVE_ENTITLEMENT[t],used:hrLeaveUsed(s,staffId,year,t),left:HR_LEAVE_ENTITLEMENT[t]-hrLeaveUsed(s,staffId,year,t)}));
}

// ── Payroll ──
export const hrAdvanceBalance=(a:HRAdvance)=>a.amount-a.recovered;
export const hrOpenAdvance=(s:State,staffId:string)=>hrSlice(s).advances.find(a=>a.staffId===staffId&&hrAdvanceBalance(a)>0);
/** Whether a staff member belongs in a month's payroll. */
export const hrInMonth=(st:HRStaff,ym:string)=>st.joined<=hrMonthEnd(ym)&&(st.active||!!st.exitedOn&&st.exitedOn>=ym+'-01');
/** One payroll line. Pure: counts + rates + advance → figures. */
export function hrComputeLine(st:HRStaff,c:HRCounts,r:HRRates,adv?:{installment:number;balance:number}):HRLine{
  const D=c.days,late=r.lateBlock>0?Math.floor(c.L/r.lateBlock):0;
  let basic:number,payableHalf:number,ot:number;
  if(st.payType==='monthly'){
    const lop=Math.min(2*D,2*(c.A+c.unmarked+c.lopLeave+c.na)+c.H+late);
    payableHalf=2*D-lop;basic=Math.round(st.monthly*payableHalf/(2*D));
    ot=Math.round(st.monthly*c.otMin*r.otBps/(D*8*60*10000));
  }else{
    payableHalf=Math.max(0,2*(c.P+c.L+c.OFFW)+c.H-late);basic=Math.round(st.daily*payableHalf/2);
    ot=Math.round(st.daily*c.otMin*r.otBps/(8*60*10000));
  }
  const gross=basic+ot;
  const pf=Math.round(Math.min(Math.round(basic*r.basicBps/10000),r.pfCap)*r.pfBps/10000);
  const esic=gross>0&&gross<=r.esicCeiling?Math.round(gross*r.esicBps/10000):0;
  const pt=gross>=r.ptThreshold?r.pt:0;
  const advance=adv?Math.max(0,Math.min(adv.installment,adv.balance,gross-pf-esic-pt)):0;
  const deductions=pf+esic+pt+advance;
  return {staffId:st.id,name:st.name,dept:st.dept,designation:st.designation,payType:st.payType,base:st.payType==='monthly'?st.monthly:st.daily,
    counts:c,latePenaltyHalf:late,payableDays:payableHalf/2,basic,ot,gross,pf,esic,pt,advance,deductions,net:gross-deductions};
}
/** Computes a month's payroll lines and warnings from current state and rates. */
export function hrComputeRun(s:State,ym:string,rates=hrSlice(s).rates):{lines:HRLine[];warnings:string[]}{
  const x=hrSlice(s),warnings:string[]=[];
  const lines=x.staff.filter(st=>hrInMonth(st,ym)).map(st=>{
    const c=hrCounts(s,st,ym);
    if(c.unmarked)warnings.push(`${st.name}: ${c.unmarked} unmarked day${c.unmarked===1?'':'s'} counted as absent.`);
    if((st.payType==='monthly'?st.monthly:st.daily)<=0)warnings.push(`${st.name}: salary not set — pays ₹0.`);
    const a=x.advances.find(a=>a.staffId===st.id&&hrAdvanceBalance(a)>0&&a.date<=hrMonthEnd(ym));
    return hrComputeLine(st,c,rates,a?{installment:a.installment,balance:hrAdvanceBalance(a)}:undefined);
  });
  return {lines,warnings};
}
export const hrRunFor=(s:State,ym:string)=>hrSlice(s).runs.find(r=>r.month===ym);
export function hrRunTotals(run:{lines:HRLine[]}){
  const t={staff:run.lines.length,basic:0,ot:0,gross:0,pf:0,esic:0,pt:0,advance:0,deductions:0,net:0};
  for(const l of run.lines){t.basic+=l.basic;t.ot+=l.ot;t.gross+=l.gross;t.pf+=l.pf;t.esic+=l.esic;t.pt+=l.pt;t.advance+=l.advance;t.deductions+=l.deductions;t.net+=l.net;}
  return t;
}
/** Applies a run's advance recoveries to the advances (used when locking). */
export function hrApplyRecoveries(x:HRState,run:HRRun){
  for(const l of run.lines){if(!l.advance)continue;const a=x.advances.find(a=>a.staffId===l.staffId&&hrAdvanceBalance(a)>0&&a.date<=hrMonthEnd(run.month));
    if(a){a.recovered+=l.advance;a.recoveries.push({runId:run.id,amount:l.advance});}}
}
/** Payslips visible to a user: payroll roles see all; others only their own, from locked runs. */
export function hrVisiblePayslips(s:State,uid:string):{run:HRRun;line:HRLine}[]{
  const pay=hrCanPay(s,uid),me=hrStaffOfUser(s,uid);
  return hrSlice(s).runs.filter(r=>r.status==='Locked').flatMap(run=>run.lines.filter(l=>pay||l.staffId===me?.id).map(line=>({run,line})))
    .sort((a,b)=>b.run.month.localeCompare(a.run.month));
}
const rupees=(p:number)=>(p/100).toFixed(2);
const csvCell=(v:string|number)=>{const t=String(v);return /[",\n]/.test(t)?'"'+t.replaceAll('"','""')+'"':t;};
/** Payroll register CSV (rupees with 2 decimals). */
export function hrRegisterCsv(s:State,run:HRRun):string{
  const head=['Month','Staff ID','Name','Department','Designation','Pay type','Payable days','Basic earned','Overtime','Gross','PF (illustrative)','ESIC (illustrative)','PT (illustrative)','Advance recovery','Total deductions','Net pay'];
  const rows=run.lines.map(l=>[run.month,l.staffId,l.name,hrDeptName(s,l.dept),l.designation,l.payType,l.payableDays,rupees(l.basic),rupees(l.ot),rupees(l.gross),rupees(l.pf),rupees(l.esic),rupees(l.pt),rupees(l.advance),rupees(l.deductions),rupees(l.net)]);
  const t=hrRunTotals(run);
  rows.push([run.month,'','TOTAL','','','','',rupees(t.basic),rupees(t.ot),rupees(t.gross),rupees(t.pf),rupees(t.esic),rupees(t.pt),rupees(t.advance),rupees(t.deductions),rupees(t.net)]);
  return [head,...rows].map(r=>r.map(csvCell).join(',')).join('\n')+'\n'+csvCell(`${run.status} run · fictional data · ${HR_DISCLAIMER}`)+'\n';
}
/** Pending leave queue a user may decide (excludes own requests and own leave). */
export function hrDecidable(s:State,uid:string){
  if(!hrCanManage(s,uid))return [];
  const me=hrStaffOfUser(s,uid);
  return hrSlice(s).leaves.filter(l=>l.status==='Pending'&&l.requestedBy!==uid&&l.staffId!==me?.id);
}
