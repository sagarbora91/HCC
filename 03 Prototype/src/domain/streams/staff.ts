// Module 17 · HR & payroll — pure helpers, selectors and the state slice type. Exported names MUST start with 'hr' / 'HR' (shared mock namespace).
// Wave 0.5 (W05-B): payroll and leave RULES ported from SCC (read-only sources, rules re-written in HCC patterns; integer paise):
//   SCC payroll — E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules\payroll\index.html
//     calcAttendance (3 late marks = ½ day, half-day = ½ day loss of pay) · calcGM (structured pay: basic 50% of earned, HRA 80% of basic,
//     washing 10% of basic, special = rest; PF 12% employee / 13% employer on min(basic, ₹15,000); ESIC 0.75% / 3.25% on earned − washing
//     when earned ≤ ₹21,000; overtime at the ordinary rate) · ptFor (Maharashtra PT slabs by gender, ₹300 in February) · advScheduled
//     (monthly instalment, 0 = full balance) · lockRun / unlockRun · reportBankCsv / reportStatutoryCsv / statutoryMemberRows ·
//     waEmpMessage (WhatsApp text) · fnfAssemble / fnfCompose (full and final) · slipWords (amount in words).
//   SCC leave — ...\reference-inputs\SCC-modules\leave\index.html
//     ENT_DEFAULTS (CL 12 · SL 7 · EL 15 per Indian FY, fyRangeOf) · leaveDaysOf (half-day = 0.5) · isWeekOffDate (weekly offs skipped) ·
//     getCapacity / MAX_DAY (max staff off per day — per department here; pending + approved hold a slot, rejected frees it) · decideLeave.
// Statutory values are DATED settings (effective-from) and marked "Illustrative — CA to confirm for a hotel". Fictional staff only.
import type {State,Role} from '../types';
import {addDays} from '../money';
import {can} from '../selectors';

export type HRShiftCode='M'|'E'|'N'|'G'|'OFF';
export type HRAttStatus='P'|'L'|'H'|'A'|'OFF';
export type HRLeaveType='CL'|'SL'|'EL'|'CO'|'ML'|'LOP';
export type HRHalf='AM'|'PM';
export type HRGender='M'|'F';
/** S = structured (basic / HRA / washing / special, SCC 'S'); B = basic only (SCC 'B'). */
export type HRStructure='S'|'B';
export type HRDept={id:string;name:string};
export type HRStaff={id:string;name:string;dept:string;designation:string;
  /** Linked demo user (login); undefined = non-login staff (cooks, security…). */
  userId?:string;
  payType:'monthly'|'daily';
  /** Monthly salary (paise) — monthly pay type. */ monthly:number;
  /** Daily wage (paise) — daily pay type. */ daily:number;
  defaultShift:Exclude<HRShiftCode,'OFF'>;
  /** Weekly off: 0 = Sunday … 6 = Saturday. */ weeklyOff:number;
  joined:string;phone:string;active:boolean;exitedOn?:string;
  /** Professional tax slab (MH). Default 'M' as in SCC. */ gender?:HRGender;
  /** Default: monthly → 'S', daily → 'B'. */ structure?:HRStructure;
  /** Default (SCC migration rule): PF only for structured pay. */ pfApplicable?:boolean;
  /** Default true; ESIC still applies only when earned ≤ ceiling. */ esicApplicable?:boolean;
  /** Fictional bank details for the bank-transfer file. */ bankName?:string;accountNo?:string;ifsc?:string};
export type HRAtt={status:HRAttStatus;at:string;otMin:number;by:string;note?:string};
export type HRLeave={id:string;staffId:string;type:HRLeaveType;from:string;to:string;days:number;reason:string;
  /** Half-day leave (from = to, days = 0.5). */ half?:HRHalf;
  status:'Pending'|'Approved'|'Rejected'|'Cancelled';requestedBy:string;requestedAt:string;decidedBy?:string;decidedAt?:string;note?:string};
export type HRAdvance={id:string;staffId:string;amount:number;
  /** Monthly recovery; 0 = recover the full balance in the first month (SCC emi 0). */ installment:number;
  recovered:number;date:string;reason:string;by:string;recoveries:{runId:string;amount:number}[];
  /** First month to recover from (YYYY-MM). Default: month of `date`. */ fromMonth?:string};
export type HRRounding='paise'|'rupee';
/** Dated statutory rates (one version per effective-from date). bps = basis points (1% = 100); money in paise. */
export type HRRates={from:string;
  basicBps:number;hraBps:number;washBps:number;
  pfEeBps:number;pfErBps:number;pfWageCap:number;
  esicEeBps:number;esicErBps:number;esicCeiling:number;
  ptMaleExempt:number;ptMaleMid:number;ptMaleMidAmt:number;ptStd:number;ptFeb:number;ptFemaleExempt:number;
  otBps:number;lateBlock:number;
  /** paise = SCC (round to 0.01); rupee = PF nearest rupee, ESIC next higher rupee. */ rounding:HRRounding};
export type HRLeavePolicy={ent:{CL:number;SL:number;EL:number};
  /** Max staff off per department per day (pending + approved). */ defaultCap:number;deptCap:Record<string,number>};
export type HRCounts={ym:string;days:number;P:number;L:number;H:number;
  /** Half-day worked with the other half on approved half-day leave. */ HL:number;
  A:number;OFFW:number;off:number;paidLeave:number;lopLeave:number;unmarked:number;na:number;otMin:number};
export type HRLine={staffId:string;name:string;dept:string;designation:string;payType:'monthly'|'daily';structure:HRStructure;gender:HRGender;base:number;
  counts:HRCounts;latePenaltyHalf:number;payableDays:number;
  /** Earned (pro-rated) pay = SCC grossPayable. */ earned:number;basic:number;hra:number;washing:number;special:number;ot:number;
  /** earned + overtime. */ gross:number;
  pfWages:number;pf:number;pfEr:number;esicWages:number;esic:number;esicEr:number;pt:number;advance:number;deductions:number;net:number;
  /** Employer cost = gross + PF employer + ESIC employer. */ ctc:number};
export type HRSend={staffId:string;to:string;at:string;by:string};
/** Bank details as frozen in a locked run (F-02: the bank file pays where the checker approved, not the live staff master). */
export type HRBank={bankName:string;accountNo:string;ifsc:string};
export type HRRun={id:string;month:string;status:'Draft'|'Locked';rates:HRRates;lines:HRLine[];warnings:string[];
  preparedBy:string;preparedAt:string;lockedBy?:string;lockedAt?:string;
  /** Bank details per staff ID, copied at lock (F-02). Absent on runs locked before the snapshot existed. */ bank?:Record<string,HRBank>;
  /** Simulated WhatsApp payslip sends. */ sent?:HRSend[];
  /** Owner unlocks (SCC unlockRun): recoveries reversed, back to Draft. */ unlocks?:{at:string;by:string;reason:string}[]};
export type HRFnfAdj={label:string;kind:'add'|'ded';amount:number};
export type HRFnf={id:string;staffId:string;name:string;lastDay:string;month:string;
  /** False when the exit month was already paid in a locked run. */ salaryIncluded:boolean;line?:HRLine;rates:HRRates;
  advanceOutstanding:number;adjustments:HRFnfAdj[];earnings:number;recoveries:number;
  /** May be negative: recoverable from the employee. */ net:number;by:string;at:string};
/** State slice for this module, stored at s.staffX (optional; absent in old saves → treat as empty). */
export type HRState={depts:HRDept[];staff:HRStaff[];
  /** Roster overrides keyed "YYYY-MM-DD|staffId". */ roster:Record<string,HRShiftCode>;
  /** Attendance keyed "YYYY-MM-DD|staffId". */ att:Record<string,HRAtt>;
  leaves:HRLeave[];advances:HRAdvance[];
  /** Dated statutory rate versions, sorted by `from`. */ rateVersions:HRRates[];
  leavePolicy?:HRLeavePolicy;runs:HRRun[];fnf?:HRFnf[]};

export const HR_DISCLAIMER='Illustrative — CA to confirm for a hotel. PF, ESIC and Maharashtra PT rules ported from SCC as dated rates; not statutory advice. Fictional staff.';
export const hrDefaultRates=():HRRates=>({from:'2025-04-01',basicBps:5000,hraBps:8000,washBps:1000,pfEeBps:1200,pfErBps:1300,pfWageCap:1500000,
  esicEeBps:75,esicErBps:325,esicCeiling:2100000,ptMaleExempt:750000,ptMaleMid:1000000,ptMaleMidAmt:17500,ptStd:20000,ptFeb:30000,ptFemaleExempt:2500000,
  otBps:10000,lateBlock:3,rounding:'paise'});
export const hrDefaultLeavePolicy=():HRLeavePolicy=>({ent:{CL:12,SL:7,EL:15},defaultCap:2,deptCap:{}});
export const hrEmpty=():HRState=>({depts:[],staff:[],roster:{},att:{},leaves:[],advances:[],rateVersions:[hrDefaultRates()],runs:[]});
export const hrSlice=(s:State):HRState=>s.staffX??hrEmpty();
/** For handlers: returns the slice, creating it on the (cloned) state if absent. */
export function hrEnsure(s:State):HRState{if(!s.staffX)s.staffX=hrEmpty();if(!s.staffX.rateVersions?.length)s.staffX.rateVersions=[hrDefaultRates()];return s.staffX;}

export const HR_SHIFTS:{code:HRShiftCode;label:string;start:string;end:string}[]=[
  {code:'M',label:'Morning',start:'07:00',end:'15:00'},{code:'E',label:'Evening',start:'15:00',end:'23:00'},
  {code:'N',label:'Night',start:'23:00',end:'07:00'},{code:'G',label:'General',start:'09:00',end:'18:00'},{code:'OFF',label:'Weekly off',start:'',end:''}];
export const hrShiftInfo=(c:HRShiftCode)=>HR_SHIFTS.find(x=>x.code===c)!;
export const HR_ATT_LABEL:Record<HRAttStatus,string>={P:'Present',L:'Late',H:'Half-day',A:'Absent',OFF:'Worked on off day'};
export const HR_LEAVE_TYPES:HRLeaveType[]=['CL','SL','EL','CO','ML','LOP'];
export const HR_LEAVE_LABEL:Record<HRLeaveType,string>={CL:'Casual leave',SL:'Sick leave',EL:'Earned leave',CO:'Compensatory off',ML:'Maternity / paternity leave',LOP:'Leave without pay'};
/** Leave types with a yearly entitlement (SCC TRACKED_CATS). Comp off and maternity / paternity are paid but not tracked. */
export const HR_TRACKED:('CL'|'SL'|'EL')[]=['CL','SL','EL'];
export const hrPaidLeave=(t:HRLeaveType)=>t!=='LOP';
export const HR_GRACE_MIN=15;
export const HR_WEEKDAYS=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
export const HR_HALF_LABEL:Record<HRHalf,string>={AM:'first half',PM:'second half'};

// ── Exact integer arithmetic (half-up; BigInt so large products never lose precision) ──
const B=(n:number)=>BigInt(n);
/** round(a·b·c / den), half-up, for non-negative integers. */
export function hrMulDiv(a:number,b:number,c:number,den:number):number{const n=B(a)*B(b)*B(c);return Number((n*B(2)+B(den))/(B(2)*B(den)));}
/** ceil(a·b / den) for non-negative integers. */
const ceilDiv=(a:number,b:number,den:number)=>{const n=B(a)*B(b);return Number((n+B(den)-B(1))/B(den));};
/** Percentage (bps) of a paise amount with the statutory rounding of the rate version. */
export function hrPct(paise:number,bps:number,mode:HRRounding,kind:'pf'|'esic'='pf'):number{
  if(mode==='rupee')return (kind==='esic'?ceilDiv(paise,bps,10000*100):hrMulDiv(paise,bps,1,10000*100))*100;
  return hrMulDiv(paise,bps,1,10000);
}

// ── Dates ──
export const hrWeekday=(d:string)=>new Date(d+'T12:00:00Z').getUTCDay();
/** Monday of the week containing d. */
export const hrMonday=(d:string)=>addDays(d,-((hrWeekday(d)+6)%7));
export const hrWeek=(monday:string)=>Array.from({length:7},(_,i)=>addDays(monday,i));
export const hrDaysInMonth=(ym:string)=>new Date(Date.UTC(+ym.slice(0,4),+ym.slice(5,7),0)).getUTCDate();
export const hrMonthDates=(ym:string)=>Array.from({length:hrDaysInMonth(ym)},(_,i)=>`${ym}-${String(i+1).padStart(2,'0')}`);
export const hrMonthEnd=(ym:string)=>`${ym}-${String(hrDaysInMonth(ym)).padStart(2,'0')}`;
export const hrPrevMonth=(ym:string)=>{const y=+ym.slice(0,4),m=+ym.slice(5,7);return m===1?`${y-1}-12`:`${y}-${String(m-1).padStart(2,'0')}`;};
export const hrNextMonth=(ym:string)=>{const y=+ym.slice(0,4),m=+ym.slice(5,7);return m===12?`${y+1}-01`:`${y}-${String(m+1).padStart(2,'0')}`;};
export const hrMonthLabel=(ym:string)=>new Date(ym+'-15T12:00:00Z').toLocaleDateString('en-IN',{month:'long',year:'numeric',timeZone:'UTC'});
/** A month may be paid once it is complete: its last day is before the business date. */
export const hrMonthComplete=(s:State,ym:string)=>hrMonthEnd(ym)<s.date;
/** Indian financial year (April → March) containing a date; a 4-digit year means the FY starting in April of that year (SCC fyRangeOf). */
export function hrFy(dateOrYear:string){
  const y=dateOrYear.length===4?+dateOrYear:(+dateOrYear.slice(5,7)>=4?+dateOrYear.slice(0,4):+dateOrYear.slice(0,4)-1);
  return {from:`${y}-04-01`,to:`${y+1}-03-31`,label:`FY ${y}–${String(y+1).slice(-2)}`};
}
const toMin=(t:string)=>+t.slice(0,2)*60+ +t.slice(3,5);

// ── Roles / visibility ──
const PAYROLL_ROLES:Role[]=['Owner','Accountant'];
/** Roster, attendance for others, leave decisions, non-salary staff edits. */
export const hrCanManage=(s:State,uid:string)=>can(s,uid,'approve');
/** Salaries, payroll, advances, payslips of others. Role-based on purpose (brief: Owner / Accountant only). */
export function hrCanPay(s:State,uid:string){const u=s.users.find(x=>x.id===uid);return !!u&&PAYROLL_ROLES.includes(u.role)&&can(s,uid,'reports');}
/** Statutory rates, leave policy and unlocking a pay-run: payroll role that also holds admin (Owner, DEC-058). */
export const hrCanRates=(s:State,uid:string)=>hrCanPay(s,uid)&&can(s,uid,'admin');
export const hrStaffOfUser=(s:State,uid:string)=>hrSlice(s).staff.find(x=>x.userId===uid);
export const hrStaffById=(s:State,id:string)=>hrSlice(s).staff.find(x=>x.id===id);
export const hrDeptName=(s:State,id:string)=>hrSlice(s).depts.find(d=>d.id===id)?.name??id;
export const hrStructureOf=(st:HRStaff):HRStructure=>st.structure??(st.payType==='daily'?'B':'S');
export const hrPfOn=(st:HRStaff)=>st.pfApplicable??hrStructureOf(st)==='S';
export const hrEsicOn=(st:HRStaff)=>st.esicApplicable??true;
export const hrGenderOf=(st:HRStaff):HRGender=>st.gender??'M';

// ── Dated statutory rates ──
export const hrRateVersions=(s:State):HRRates[]=>{const v=hrSlice(s).rateVersions;return v?.length?v:[hrDefaultRates()];};
/** Rate version in force for a month (latest effective-from on or before the 1st). */
export function hrRatesFor(s:State,ym:string):HRRates{
  const v=hrRateVersions(s),d=ym+'-01';let r=v[0];for(const x of v)if(x.from<=d)r=x;return r;
}
export const hrLeavePolicy=(s:State):HRLeavePolicy=>hrSlice(s).leavePolicy??hrDefaultLeavePolicy();
export const hrDeptCap=(s:State,dept:string)=>{const p=hrLeavePolicy(s);return p.deptCap[dept]??p.defaultCap;};
/** Latest locked pay-run month (rates on or before it can no longer change). */
export const hrLastLockedMonth=(s:State)=>hrSlice(s).runs.filter(r=>r.status==='Locked').map(r=>r.month).sort().at(-1);

// ── Roster / attendance ──
export const hrKey=(date:string,staffId:string)=>date+'|'+staffId;
export function hrShiftFor(s:State,st:HRStaff,date:string):HRShiftCode{
  const o=hrSlice(s).roster[hrKey(date,st.id)];if(o)return o;
  return hrWeekday(date)===st.weeklyOff?'OFF':st.defaultShift;
}
const covers=(l:HRLeave,date:string)=>l.from<=date&&l.to>=date;
export const hrApprovedLeavesOn=(s:State,staffId:string,date:string)=>hrSlice(s).leaves.filter(l=>l.staffId===staffId&&l.status==='Approved'&&covers(l,date));
/** A full-day approved leave on the date, else the first half-day one. */
export const hrApprovedLeaveOn=(s:State,staffId:string,date:string)=>{const ls=hrApprovedLeavesOn(s,staffId,date);return ls.find(l=>!l.half)??ls[0];};
/** Day view for one staff member: explicit attendance → rostered off → approved leave → unmarked. Half-day leave can sit beside attendance. */
export function hrDayStatus(s:State,st:HRStaff,date:string):{kind:'att'|'leave'|'off'|'unmarked'|'na';att?:HRAtt;leave?:HRLeave}{
  if(date<st.joined||(st.exitedOn&&date>st.exitedOn))return {kind:'na'};
  const att=hrSlice(s).att[hrKey(date,st.id)],leave=hrApprovedLeaveOn(s,st.id,date);
  if(att)return leave?.half?{kind:'att',att,leave}:{kind:'att',att};
  if(hrShiftFor(s,st,date)==='OFF')return {kind:'off'};// F-01: weekly off inside a leave span stays a weekly off
  if(leave)return {kind:'leave',leave};
  return {kind:'unmarked'};
}
/** Self check-in status from the simulated clock: Late after shift start + grace. */
export function hrCheckinStatus(clock:string,shift:HRShiftCode):'P'|'L'{
  if(shift==='OFF'||shift==='N')return 'P';
  return toMin(clock.slice(11,16))>toMin(hrShiftInfo(shift).start)+HR_GRACE_MIN?'L':'P';
}
/** Counts for a staff member over a month (optionally up to a date). Leave and absence can be half days (0.5). */
export function hrCounts(s:State,st:HRStaff,ym:string,upTo?:string):HRCounts{
  const c:HRCounts={ym,days:hrDaysInMonth(ym),P:0,L:0,H:0,HL:0,A:0,OFFW:0,off:0,paidLeave:0,lopLeave:0,unmarked:0,na:0,otMin:0};
  const addLeave=(l:HRLeave,n:number)=>{if(hrPaidLeave(l.type))c.paidLeave+=n;else c.lopLeave+=n;};
  for(const d of hrMonthDates(ym)){
    if(upTo&&d>upTo)break;
    if(d<st.joined||(st.exitedOn&&d>st.exitedOn)){c.na++;continue;}
    const att=hrSlice(s).att[hrKey(d,st.id)],ls=hrApprovedLeavesOn(s,st.id,d),full=ls.find(l=>!l.half),halves=ls.filter(l=>l.half).slice(0,2);
    if(att){
      c.otMin+=att.otMin||0;
      if(!full&&halves.length&&(att.status==='H'||att.status==='A')){
        addLeave(halves[0],0.5);
        if(att.status==='H')c.HL++;else c.A+=0.5;
      }else if(att.status==='OFF')c.OFFW++;else c[att.status]++;
    }
    // F-01: a rostered weekly off inside a leave span is a weekly off, never a leave day (SCC isWeekOffDate; matches the stored `days`).
    // HCC has no holiday calendar yet; when one is added, holidays belong in this same branch.
    else if(hrShiftFor(s,st,d)==='OFF')c.off++;
    else if(full)addLeave(full,1);
    else if(halves.length){halves.forEach(l=>addLeave(l,0.5));c.unmarked+=1-0.5*halves.length;}
    else c.unmarked++;
  }
  return c;
}

// ── Leave ──
/** Leave days in [from,to], excluding rostered weekly offs (SCC isWeekOffDate); a half-day leave is 0.5. */
export function hrLeaveDays(s:State,st:HRStaff,from:string,to:string,half?:HRHalf){
  if(half)return hrShiftFor(s,st,from)==='OFF'?0:0.5;
  let n=0;for(let d=from;d<=to;d=addDays(d,1))if(hrShiftFor(s,st,d)!=='OFF')n++;return n;
}
/** Leave used (approved + pending) in the financial year containing a date (or starting in April of a 4-digit year), per type. */
export function hrLeaveUsed(s:State,staffId:string,dateOrYear:string,type:HRLeaveType,except=''){
  const fy=hrFy(dateOrYear);
  return hrSlice(s).leaves.filter(l=>l.id!==except&&l.staffId===staffId&&l.type===type&&l.from>=fy.from&&l.from<=fy.to&&(l.status==='Approved'||l.status==='Pending')).reduce((n,l)=>n+l.days,0);
}
export const hrEntitled=(s:State,type:'CL'|'SL'|'EL')=>hrLeavePolicy(s).ent[type];
export function hrLeaveBalance(s:State,staffId:string,dateOrYear:string){
  return HR_TRACKED.map(t=>{const used=hrLeaveUsed(s,staffId,dateOrYear,t),entitled=hrEntitled(s,t);return {type:t,entitled,used,left:entitled-used};});
}
/** Other staff of a department holding a leave slot (pending or approved) on a date — SCC getCapacity, per department. */
export function hrDeptOff(s:State,dept:string,date:string,exceptStaff=''){
  const x=hrSlice(s);
  return x.leaves.filter(l=>(l.status==='Pending'||l.status==='Approved')&&covers(l,date)&&l.staffId!==exceptStaff).filter(l=>{
    const st=x.staff.find(t=>t.id===l.staffId);return !!st&&st.dept===dept&&hrShiftFor(s,st,date)!=='OFF';});
}
/** Leave calendar for a month: per date, the leaves (pending + approved) and per-department counts against the cap. */
export function hrLeaveCalendar(s:State,ym:string){
  const x=hrSlice(s);
  return hrMonthDates(ym).map(date=>{
    const items=x.leaves.filter(l=>(l.status==='Pending'||l.status==='Approved')&&covers(l,date)).map(l=>({leave:l,staff:x.staff.find(t=>t.id===l.staffId)!}))
      .filter(i=>i.staff&&hrShiftFor(s,i.staff,date)!=='OFF');
    const depts=x.depts.map(d=>({dept:d.id,name:d.name,count:items.filter(i=>i.staff.dept===d.id).length,cap:hrDeptCap(s,d.id)})).filter(d=>d.count>0);
    return {date,items,depts,full:depts.some(d=>d.count>=d.cap)};
  });
}

// ── Payroll ──
export const hrAdvanceBalance=(a:HRAdvance)=>a.amount-a.recovered;
export const hrOpenAdvance=(s:State,staffId:string)=>hrSlice(s).advances.find(a=>a.staffId===staffId&&hrAdvanceBalance(a)>0);
const advFrom=(a:HRAdvance)=>a.fromMonth??a.date.slice(0,7);
/** The open advance recovered in a month's run (one open advance per staff member). */
export const hrAdvanceFor=(x:HRState,staffId:string,ym:string)=>x.advances.find(a=>a.staffId===staffId&&hrAdvanceBalance(a)>0&&advFrom(a)<=ym);
/** Whether a staff member belongs in a month's payroll (a full-and-final settlement that paid the exit month removes them). */
export const hrInMonth=(st:HRStaff,ym:string)=>st.joined<=hrMonthEnd(ym)&&(st.active||!!st.exitedOn&&st.exitedOn>=ym+'-01');
/** Maharashtra professional tax for a month (SCC ptFor): women exempt up to the female limit; men nil / mid slab / standard; February top slab. */
export function hrPt(base:number,gender:HRGender,ym:string,r:HRRates):number{
  const top=ym.slice(5,7)==='02'?r.ptFeb:r.ptStd;
  if(gender==='F')return base<=r.ptFemaleExempt?0:top;
  if(base<=r.ptMaleExempt)return 0;
  if(base<=r.ptMaleMid)return r.ptMaleMidAmt;
  return top;
}
/** One payroll line. Pure: staff + counts + rate version + advance → figures (rounded to paise, half-up, in this order). */
export function hrComputeLine(st:HRStaff,c:HRCounts,r:HRRates,adv?:{installment:number;balance:number}):HRLine{
  const D=c.days,late=r.lateBlock>0?Math.floor(c.L/r.lateBlock):0,structure=hrStructureOf(st),gender=hrGenderOf(st);
  let payHalf:number,earned:number,ot:number;
  if(st.payType==='monthly'){
    const lop=Math.min(2*D,Math.round(2*(c.A+c.unmarked+c.lopLeave+c.na))+c.H+late);
    payHalf=2*D-lop;earned=hrMulDiv(st.monthly,payHalf,1,2*D);
    ot=hrMulDiv(st.monthly,c.otMin,r.otBps,D*8*60*10000);
  }else{
    payHalf=Math.max(0,Math.round(2*(c.P+c.L+c.OFFW))+c.H+c.HL-late);earned=hrMulDiv(st.daily,payHalf,1,2);
    ot=hrMulDiv(st.daily,c.otMin,r.otBps,8*60*10000);
  }
  let basic=earned,hra=0,washing=0;
  if(structure==='S'){basic=hrMulDiv(earned,r.basicBps,1,10000);hra=hrMulDiv(basic,r.hraBps,1,10000);washing=hrMulDiv(basic,r.washBps,1,10000);}
  const special=earned-basic-hra-washing,gross=earned+ot;
  const pfWages=hrPfOn(st)?Math.min(basic,r.pfWageCap):0;
  const pf=pfWages?hrPct(pfWages,r.pfEeBps,r.rounding):0,pfEr=pfWages?hrPct(pfWages,r.pfErBps,r.rounding):0;
  const esicWages=hrEsicOn(st)&&earned>0&&earned<=r.esicCeiling?earned-washing:0;
  const esic=esicWages?hrPct(esicWages,r.esicEeBps,r.rounding,'esic'):0,esicEr=esicWages?hrPct(esicWages,r.esicErBps,r.rounding,'esic'):0;
  const pt=earned>0?hrPt(earned,gender,c.ym,r):0;
  const due=adv?(adv.installment>0?Math.min(adv.installment,adv.balance):adv.balance):0;
  const advance=Math.max(0,Math.min(due,gross-pf-esic-pt));
  const deductions=pf+esic+pt+advance;
  return {staffId:st.id,name:st.name,dept:st.dept,designation:st.designation,payType:st.payType,structure,gender,base:st.payType==='monthly'?st.monthly:st.daily,
    counts:c,latePenaltyHalf:late,payableDays:payHalf/2,earned,basic,hra,washing,special,ot,gross,pfWages,pf,pfEr,esicWages,esic,esicEr,pt,advance,deductions,
    net:gross-deductions,ctc:gross+pfEr+esicEr};
}
/** Staff settled full-and-final with the exit month's salary inside the settlement. */
const settledFor=(x:HRState,staffId:string,ym:string)=>(x.fnf??[]).some(f=>f.staffId===staffId&&f.salaryIncluded&&f.month===ym);
/** Computes a month's payroll lines and warnings from current state and the rate version in force. */
export function hrComputeRun(s:State,ym:string,rates=hrRatesFor(s,ym)):{lines:HRLine[];warnings:string[]}{
  const x=hrSlice(s),warnings:string[]=[];
  const lines=x.staff.filter(st=>hrInMonth(st,ym)&&!settledFor(x,st.id,ym)).map(st=>{
    const c=hrCounts(s,st,ym);
    if(c.unmarked)warnings.push(`${st.name}: ${c.unmarked} unmarked day${c.unmarked===1?'':'s'} counted as absent.`);
    if((st.payType==='monthly'?st.monthly:st.daily)<=0)warnings.push(`${st.name}: salary not set — pays ₹0.`);
    const a=hrAdvanceFor(x,st.id,ym);
    return hrComputeLine(st,c,rates,a?{installment:a.installment,balance:hrAdvanceBalance(a)}:undefined);
  });
  return {lines,warnings};
}
/** Errors that block locking (SCC runBlockingErrors). */
export const hrRunErrors=(run:{lines:HRLine[]})=>run.lines.filter(l=>l.base<=0).map(l=>`${l.name}: salary not set`);
export const hrRunFor=(s:State,ym:string)=>hrSlice(s).runs.find(r=>r.month===ym);
export function hrRunTotals(run:{lines:HRLine[]}){
  const t={staff:run.lines.length,earned:0,basic:0,ot:0,gross:0,pf:0,pfEr:0,esic:0,esicEr:0,pt:0,advance:0,deductions:0,net:0,ctc:0};
  for(const l of run.lines){t.earned+=l.earned;t.basic+=l.basic;t.ot+=l.ot;t.gross+=l.gross;t.pf+=l.pf;t.pfEr+=l.pfEr;t.esic+=l.esic;t.esicEr+=l.esicEr;t.pt+=l.pt;t.advance+=l.advance;t.deductions+=l.deductions;t.net+=l.net;t.ctc+=l.ctc;}
  return t;
}
/** F-02: copy each line's bank details from the staff master into the run (called at lock). */
export function hrBankSnapshot(x:HRState,run:HRRun){
  run.bank=Object.fromEntries(run.lines.map(l=>{const st=x.staff.find(t=>t.id===l.staffId);return [l.staffId,{bankName:st?.bankName??'',accountNo:st?.accountNo??'',ifsc:st?.ifsc??''}];}));
}
/** Masked account number for audit text: last 4 digits only. */
export const hrMaskAccount=(a?:string)=>a?'••••'+a.slice(-4):'(none)';
/** Applies a run's advance recoveries to the advances (used when locking). */
export function hrApplyRecoveries(x:HRState,run:HRRun){
  for(const l of run.lines){if(!l.advance)continue;const a=hrAdvanceFor(x,l.staffId,run.month);
    if(a){a.recovered+=l.advance;a.recoveries.push({runId:run.id,amount:l.advance});}}
}
/** Reverses a run's advance recoveries (Owner unlock). */
export function hrReverseRecoveries(x:HRState,runId:string){
  for(const a of x.advances){const mine=a.recoveries.filter(r=>r.runId===runId);if(!mine.length)continue;
    a.recovered-=mine.reduce((n,r)=>n+r.amount,0);a.recoveries=a.recoveries.filter(r=>r.runId!==runId);}
}
/** Payslips visible to a user: payroll roles see all; others only their own, from locked runs. */
export function hrVisiblePayslips(s:State,uid:string):{run:HRRun;line:HRLine}[]{
  const pay=hrCanPay(s,uid),me=hrStaffOfUser(s,uid);
  return hrSlice(s).runs.filter(r=>r.status==='Locked').flatMap(run=>run.lines.filter(l=>pay||l.staffId===me?.id).map(line=>({run,line})))
    .sort((a,b)=>b.run.month.localeCompare(a.run.month));
}
const rupees=(p:number)=>(p/100).toFixed(2);
const csvCell=(v:string|number)=>{const t=String(v);return /[",\n]/.test(t)?'"'+t.split('"').join('""')+'"':t;};
const csv=(rows:(string|number)[][])=>rows.map(r=>r.map(csvCell).join(',')).join('\n')+'\n';
/** Payroll register CSV (rupees with 2 decimals). */
export function hrRegisterCsv(s:State,run:HRRun):string{
  const head=['Month','Staff ID','Name','Department','Designation','Pay type','Payable days','Earned','Basic','HRA','Washing','Special','Overtime','Gross','PF wages','PF (employee)','ESIC (employee)','PT','Advance recovery','Total deductions','PF (employer)','ESIC (employer)','Net pay'];
  const row=(l:HRLine)=>[run.month,l.staffId,l.name,hrDeptName(s,l.dept),l.designation,l.payType,l.payableDays,...[l.earned,l.basic,l.hra,l.washing,l.special,l.ot,l.gross,l.pfWages,l.pf,l.esic,l.pt,l.advance,l.deductions,l.pfEr,l.esicEr,l.net].map(rupees)];
  const t=hrRunTotals(run),pfw=run.lines.reduce((n,l)=>n+l.pfWages,0),hra=run.lines.reduce((n,l)=>n+l.hra,0),wash=run.lines.reduce((n,l)=>n+l.washing,0),spec=run.lines.reduce((n,l)=>n+l.special,0);
  const total=[run.month,'','TOTAL','','','','',...[t.earned,t.basic,hra,wash,spec,t.ot,t.gross,pfw,t.pf,t.esic,t.pt,t.advance,t.deductions,t.pfEr,t.esicEr,t.net].map(rupees)];
  return csv([head,...run.lines.map(row),total])+csvCell(`${run.status} run · fictional data · ${HR_DISCLAIMER}`)+'\n';
}
/** Bank transfer file (SCC reportBankCsv): one row per staff member with net pay; account numbers keep a leading apostrophe for Excel.
 *  F-02: a locked run pays to the bank details frozen at lock; later staff-master edits apply to future runs only. */
export function hrBankCsv(s:State,run:HRRun):string{
  const rows:(string|number)[][]=[['Sr','Employee Name','Employee ID','Bank Name','Account No','IFSC','Net Amount']];
  run.lines.forEach((l,i)=>{const st=hrStaffById(s,l.staffId),b=run.bank?.[l.staffId]??{bankName:st?.bankName??'',accountNo:st?.accountNo??'',ifsc:st?.ifsc??''};
    rows.push([i+1,l.name,l.staffId,b.bankName,"'"+b.accountNo,b.ifsc,rupees(l.net)]);});
  rows.push(['','TOTAL','','','','',rupees(hrRunTotals(run).net)]);
  const src=run.bank?'bank details as locked':'bank details from the staff master (run locked before bank snapshots)';
  return csv(rows)+csvCell(`${hrMonthLabel(run.month)} · ${run.status} · ${src} · fictional bank details · simulated transfer — no money moves`)+'\n';
}
/** Statutory summary + member-wise register (SCC reportStatutoryCsv + statutoryMemberRows). */
export function hrStatutoryCsv(s:State,run:HRRun):string{
  const t=hrRunTotals(run);
  const rows:(string|number)[][]=[['Component','Employee','Employer','Total'],
    ['Provident Fund',rupees(t.pf),rupees(t.pfEr),rupees(t.pf+t.pfEr)],['ESIC',rupees(t.esic),rupees(t.esicEr),rupees(t.esic+t.esicEr)],['Professional Tax (Maharashtra)',rupees(t.pt),'0.00',rupees(t.pt)],[],
    ['Sr','Staff ID','Name','Earned','PF wages','PF EE','PF ER','ESIC wages','ESIC EE','ESIC ER','PT']];
  run.lines.forEach((l,i)=>rows.push([i+1,l.staffId,l.name,...[l.earned,l.pfWages,l.pf,l.pfEr,l.esicWages,l.esic,l.esicEr,l.pt].map(rupees)]));
  return csv(rows)+csvCell(`${hrMonthLabel(run.month)} · rates from ${run.rates.from} · ${HR_DISCLAIMER}`)+'\n';
}
/** Pending leave queue a user may decide (excludes own requests and own leave). */
export function hrDecidable(s:State,uid:string){
  if(!hrCanManage(s,uid))return [];
  const me=hrStaffOfUser(s,uid);
  return hrSlice(s).leaves.filter(l=>l.status==='Pending'&&l.requestedBy!==uid&&l.staffId!==me?.id);
}

// ── Payslip delivery (DEC-056): PDF via the share sheet + simulated WhatsApp ──
/** WhatsApp number: digits only, 10 digits → +91 (SCC waEmpMessage). Empty when unusable. */
export function hrWaNumber(phone:string){let d=String(phone??'').replace(/[^0-9]/g,'');if(d.length===10)d='91'+d;return /^91\d{10}$/.test(d)?d:'';}
const fmtRs=(p:number)=>'Rs.'+(p/100).toLocaleString('en-IN',{minimumFractionDigits:2,maximumFractionDigits:2});
export function hrPayslipMessage(s:State,run:HRRun,l:HRLine){
  return `Namaste ${l.name.split(' ')[0]}, your salary for ${hrMonthLabel(run.month)} (${s.settings.hotel}):\nPayable days: ${l.payableDays}\nGross: ${fmtRs(l.gross)}\nDeductions: ${fmtRs(l.deductions)}\nNet pay: ${fmtRs(l.net)}\nPayslip PDF attached. — ${s.settings.hotel} (simulated WhatsApp · fictional data)`;
}
/** Amount in words, Indian system (SCC slipWords), with paise. */
export function hrAmountWords(paise:number){
  const a=['','One','Two','Three','Four','Five','Six','Seven','Eight','Nine','Ten','Eleven','Twelve','Thirteen','Fourteen','Fifteen','Sixteen','Seventeen','Eighteen','Nineteen'];
  const b=['','','Twenty','Thirty','Forty','Fifty','Sixty','Seventy','Eighty','Ninety'];
  const t2=(n:number)=>n<20?a[n]:b[Math.floor(n/10)]+(n%10?' '+a[n%10]:'');
  const t3=(n:number)=>{const h=Math.floor(n/100),r=n%100;return (h?a[h]+' Hundred'+(r?' ':''):'')+(r?t2(r):'');};
  const words=(num:number)=>{if(num===0)return 'Zero';const p:string[]=[];const cr=Math.floor(num/1e7);num%=1e7;const lk=Math.floor(num/1e5);num%=1e5;const th=Math.floor(num/1e3);num%=1e3;
    if(cr)p.push(t3(cr)+' Crore');if(lk)p.push(t3(lk)+' Lakh');if(th)p.push(t3(th)+' Thousand');if(num)p.push(t3(num));return p.join(' ');};
  const neg=paise<0,v=Math.abs(paise),r=Math.floor(v/100),ps=v%100;
  return (neg?'Minus ':'')+'Rupees '+words(r)+(ps?' and '+t2(ps)+' Paise':'')+' Only';
}

// ── Full and final settlement (SCC fnfAssemble / fnfCompose) ──
/** Final-month salary (unless already paid in a locked run) − statutory deductions − the FULL outstanding advance ± manual adjustments. */
export function hrFnfCompute(s:State,st:HRStaff,adjustments:HRFnfAdj[]){
  const x=hrSlice(s),lastDay=st.exitedOn??s.date,month=lastDay.slice(0,7),rates=hrRatesFor(s,month);
  const paid=x.runs.some(r=>r.month===month&&r.status==='Locked'&&r.lines.some(l=>l.staffId===st.id));
  const line=paid?undefined:hrComputeLine(st,hrCounts(s,st,month),rates);
  const advanceOutstanding=x.advances.filter(a=>a.staffId===st.id).reduce((n,a)=>n+hrAdvanceBalance(a),0);
  const add=adjustments.filter(a=>a.kind==='add').reduce((n,a)=>n+a.amount,0),ded=adjustments.filter(a=>a.kind==='ded').reduce((n,a)=>n+a.amount,0);
  const earnings=(line?line.gross:0)+add,recoveries=(line?line.pf+line.esic+line.pt:0)+advanceOutstanding+ded;
  return {lastDay,month,salaryIncluded:!paid,line,rates,advanceOutstanding,earnings,recoveries,net:earnings-recoveries};
}
/** Suggested earned-leave encashment: EL balance × (monthly ÷ days in the exit month). Manual adjustment in SCC; offered as a prefill here. */
export function hrElEncashment(s:State,st:HRStaff){
  if(st.payType!=='monthly')return 0;
  const last=st.exitedOn??s.date,left=Math.max(0,hrLeaveBalance(s,st.id,last).find(b=>b.type==='EL')!.left);
  return hrMulDiv(st.monthly,Math.round(left*2),1,2*hrDaysInMonth(last.slice(0,7)));
}

// ── Settings for the Master configuration hub (W05-A wires them; group "People, payroll and leave") ──
export type HRSettingDef={key:string;group:'People, payroll and leave';category:'Statutory rates'|'Leave policy';label:string;
  type:'percent'|'money'|'integer'|'text'|'fixed';unit:string;editable:boolean;approval:string;effective:string;help:string;
  visibility:string;approver:'none'|'Owner';dated:boolean;affects:string;command?:string;fields?:(keyof HRRates|string)[]};
const OWNER_PAY='Owner only (payroll and statutory, DEC-058); never self-approved';
const DATED='From a month (effective-from 1st); locked pay-runs keep their snapshot';
export function hrSettingDefs():HRSettingDef[]{
  const rate=(key:string,label:string,type:HRSettingDef['type'],unit:string,fields:(keyof HRRates)[],help:string):HRSettingDef=>({key,group:'People, payroll and leave',category:'Statutory rates',label,type,unit,editable:true,
    approval:OWNER_PAY,effective:DATED,help:help+' Illustrative — CA to confirm for a hotel.',visibility:'Owner, Accountant',approver:'Owner',dated:true,
    affects:'Draft and future payroll runs, payslips, bank and statutory files; locked runs never change',command:'HR_STAT_RATES_SET',fields});
  const leave=(key:string,label:string,type:HRSettingDef['type'],unit:string,fields:string[],help:string,editable=true):HRSettingDef=>({key,group:'People, payroll and leave',category:'Leave policy',label,type,unit,editable,
    approval:editable?OWNER_PAY:'Set per staff member in Staff and payroll',effective:editable?'Immediately; new leave requests only':'—',help,visibility:'Owner, Manager, Accountant',approver:editable?'Owner':'none',dated:false,
    affects:'Leave requests, balances and the leave calendar',...(editable?{command:'HR_LEAVE_POLICY_SET'}:{}),fields});
  return [
    rate('hr.structure','Salary structure (structured pay)','percent','% basic of earned · HRA and washing % of basic',['basicBps','hraBps','washBps'],'SCC: basic 50%, HRA 80% of basic, washing 10% of basic, special = rest.'),
    rate('hr.pf','Provident Fund','percent','% of PF wages · wage cap ₹',['pfEeBps','pfErBps','pfWageCap'],'Employee 12%, employer 13% on basic capped at ₹15,000 (SCC).'),
    rate('hr.esic','ESIC','percent','% of earned − washing · ceiling ₹',['esicEeBps','esicErBps','esicCeiling'],'Employee 0.75%, employer 3.25% while earned pay ≤ ₹21,000 (SCC).'),
    rate('hr.pt','Professional tax (Maharashtra)','money','₹ per month by slab',['ptMaleExempt','ptMaleMid','ptMaleMidAmt','ptStd','ptFeb','ptFemaleExempt'],'Men nil ≤ ₹7,500, ₹175 to ₹10,000, then ₹200 (₹300 in February); women nil ≤ ₹25,000 (SCC ptFor).'),
    rate('hr.ot','Overtime rate','percent','× ordinary hourly rate',['otBps'],'SCC pays overtime at the ordinary daily rate (1×).'),
    rate('hr.late','Late marks rule','integer','late marks per ½-day loss of pay',['lateBlock'],'SCC: 3 late marks = ½ day.'),
    rate('hr.rounding','Statutory rounding','text','paise / rupee',['rounding'],'SCC rounds to paise; "rupee" rounds PF to the nearest rupee and ESIC up to the next rupee.'),
    leave('hr.leave.ent','Yearly leave entitlement','integer','days per financial year (CL / SL / EL)',['ent'],'SCC defaults: casual 12, sick 7, earned 15 (April–March).'),
    leave('hr.leave.cap','Maximum staff off per department per day','integer','staff (pending + approved)',['defaultCap','deptCap'],'SCC MAX_DAY capacity, applied per department.'),
    leave('hr.leave.half','Half-day leave','fixed','first half / second half = 0.5 day',[],'Always on (SCC leaveDaysOf).',false),
    leave('hr.leave.weeklyoff','Weekly offs','fixed','one day per staff member',[],'Weekly offs inside leave dates are skipped (SCC isWeekOffDate).',false),
  ];
}
const pctOf=(bps:number)=>`${bps/100}%`;
const rs=(p:number)=>'₹'+(p/100).toLocaleString('en-IN');
/** One-line current value of an HR setting (for hub summaries). */
export function hrSettingValue(s:State,key:string,ym=s.date.slice(0,7)):string{
  const r=hrRatesFor(s,ym),p=hrLeavePolicy(s);
  switch(key){
    case 'hr.structure':return `Basic ${pctOf(r.basicBps)} · HRA ${pctOf(r.hraBps)} · washing ${pctOf(r.washBps)} (from ${r.from})`;
    case 'hr.pf':return `${pctOf(r.pfEeBps)} + ${pctOf(r.pfErBps)} on wages up to ${rs(r.pfWageCap)}`;
    case 'hr.esic':return `${pctOf(r.esicEeBps)} + ${pctOf(r.esicErBps)} while earned ≤ ${rs(r.esicCeiling)}`;
    case 'hr.pt':return `Men ${rs(r.ptMaleMidAmt)} / ${rs(r.ptStd)} (Feb ${rs(r.ptFeb)}) · women exempt ≤ ${rs(r.ptFemaleExempt)}`;
    case 'hr.ot':return `${r.otBps/10000}× ordinary rate`;
    case 'hr.late':return r.lateBlock?`${r.lateBlock} late = ½ day`:'Off';
    case 'hr.rounding':return r.rounding==='paise'?'To paise (SCC)':'PF nearest ₹ · ESIC next ₹';
    case 'hr.leave.ent':return `CL ${p.ent.CL} · SL ${p.ent.SL} · EL ${p.ent.EL} days / FY`;
    case 'hr.leave.cap':return `${p.defaultCap} per department per day${Object.keys(p.deptCap).length?` · ${Object.keys(p.deptCap).length} department override${Object.keys(p.deptCap).length===1?'':'s'}`:''}`;
    case 'hr.leave.half':return 'On';case 'hr.leave.weeklyoff':return 'Per staff member';
    default:return '—';
  }
}
