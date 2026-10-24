// Module 17 · HR & payroll — command handlers. Command types MUST start with 'HR_'. Return value = audit/summary text.
// Salaries / payroll: Owner + Accountant only (hrCanPay). Roster, attendance for others, leave decisions: 'approve' holders.
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {
  hrEnsure,hrCanManage,hrCanPay,hrCanRates,hrStaffOfUser,hrShiftFor,hrKey,hrCheckinStatus,hrApprovedLeaveOn,hrLeaveDays,hrLeaveUsed,
  hrComputeRun,hrMonthComplete,hrApplyRecoveries,hrAdvanceBalance,hrMonday,hrWeek,hrMonthLabel,HR_LEAVE_ENTITLEMENT,HR_ATT_LABEL,
  type HRStaff,type HRShiftCode,type HRAttStatus,type HRLeaveType,type HRRates,
} from '../../../domain/streams/staff';
import {addDays} from '../../../domain/money';

const SHIFTS:HRShiftCode[]=['M','E','N','G','OFF'];
const DATE=/^\d{4}-\d{2}-\d{2}$/;
function staffOf(k:Kit,id:string):HRStaff{const st=hrEnsure(k.s).staff.find(x=>x.id===id);k.assert(st,'Staff member not found.','NOT_FOUND');return st!;}
function manage(k:Kit){if(!hrCanManage(k.s,k.uid))k.fail('FORBIDDEN','Access denied: approve permission is required to manage staff.');}
function payroll(k:Kit){if(!hrCanPay(k.s,k.uid))k.fail('FORBIDDEN','Access denied: payroll is limited to the Owner and Accountant roles.');}
const isOwn=(k:Kit,st:HRStaff)=>!!st.userId&&st.userId===k.uid;
const rupee=(n:unknown)=>Number.isSafeInteger(n)&&(n as number)>=0&&(n as number)<=100000000;

export const handlers:Record<string,Handler>={
  /** Create / edit a staff record. Salary fields: payroll roles only. */
  HR_STAFF_SAVE:k=>{
    const {s,cmd,assert,fail}=k,x=hrEnsure(s),pay=hrCanPay(s,k.uid);
    if(!hrCanManage(s,k.uid)&&!pay)fail('FORBIDDEN','Access denied: only managers or payroll roles can edit the staff master.');
    assert(String(cmd.name??'').trim().length>=2,'Enter the staff member’s name.');
    assert(x.depts.some(d=>d.id===cmd.dept),'Choose a department.');
    assert(String(cmd.designation??'').trim(),'Enter a designation.');
    assert(['M','E','N','G'].includes(cmd.defaultShift),'Choose a default shift.');
    assert(Number.isInteger(cmd.weeklyOff)&&cmd.weeklyOff>=0&&cmd.weeklyOff<=6,'Choose a weekly off day.');
    if(cmd.userId){assert(s.users.some(u=>u.id===cmd.userId),'Linked demo user not found.');
      assert(!x.staff.some(st=>st.userId===cmd.userId&&st.id!==cmd.id),'That demo user is already linked to another staff record.');}
    const existing=cmd.id?x.staff.find(st=>st.id===cmd.id):undefined;
    if(cmd.id)assert(existing,'Staff member not found.','NOT_FOUND');
    const salaryGiven=cmd.payType!==undefined||cmd.monthly!==undefined||cmd.daily!==undefined;
    if(salaryGiven){
      const changed=!existing||existing.payType!==(cmd.payType??existing.payType)||existing.monthly!==(cmd.monthly??existing.monthly)||existing.daily!==(cmd.daily??existing.daily);
      if(changed&&!pay)fail('FORBIDDEN','Only the Owner or Accountant can set salary details.');
      if(cmd.payType!==undefined)assert(['monthly','daily'].includes(cmd.payType),'Choose a pay type.');
      if(cmd.monthly!==undefined)assert(rupee(cmd.monthly),'Enter a valid monthly salary.');
      if(cmd.daily!==undefined)assert(rupee(cmd.daily),'Enter a valid daily wage.');
    }
    const base={name:String(cmd.name).trim(),dept:cmd.dept,designation:String(cmd.designation).trim(),...(cmd.userId?{userId:cmd.userId as string}:{}),
      defaultShift:cmd.defaultShift,weeklyOff:cmd.weeklyOff,phone:String(cmd.phone??'').trim()};
    if(existing){if(!cmd.userId)delete existing.userId;Object.assign(existing,base);if(pay&&salaryGiven){existing.payType=cmd.payType??existing.payType;existing.monthly=cmd.monthly??existing.monthly;existing.daily=cmd.daily??existing.daily;}
      return `${existing.id} · ${existing.name} updated`;}
    const st:HRStaff={id:k.id('HR-S'),...base,payType:pay&&cmd.payType?cmd.payType:'monthly',monthly:pay?cmd.monthly??0:0,daily:pay?cmd.daily??0:0,joined:s.date,active:true};
    x.staff.push(st);return `${st.id} · ${st.name} added${pay?'':' (salary to be set by payroll)'}`;
  },
  /** Deactivate (exit) or reactivate a staff record. */
  HR_STAFF_ACTIVE:k=>{
    manage(k);const st=staffOf(k,k.cmd.id);
    if(k.cmd.active){k.assert(!st.active,'Already active.','INVALID_TRANSITION');st.active=true;delete st.exitedOn;return st.name+' · reactivated';}
    k.assert(st.active,'Already inactive.','INVALID_TRANSITION');k.assert(st.userId!==k.uid,'You cannot deactivate your own staff record.','FORBIDDEN');
    st.active=false;st.exitedOn=k.s.date;return `${st.name} · exited ${k.s.date}`;
  },
  /** Set one roster cell (business date or later). */
  HR_ROSTER_SET:k=>{
    manage(k);const {s,cmd,assert}=k,st=staffOf(k,cmd.staffId);
    assert(st.active,'This staff member is inactive.','INVALID_TRANSITION');
    assert(DATE.test(cmd.date)&&cmd.date>=s.date,'Only today’s or future roster can change; past roster is history.','INVALID_TRANSITION');
    assert(SHIFTS.includes(cmd.shift),'Choose a shift.');
    hrEnsure(s).roster[hrKey(cmd.date,st.id)]=cmd.shift;return `${st.name} · ${cmd.date} → ${cmd.shift}`;
  },
  /** Copy a week's effective roster onto the following week (dates ≥ business date only). */
  HR_ROSTER_COPY:k=>{
    manage(k);const {s,cmd,assert}=k,x=hrEnsure(s);
    assert(DATE.test(cmd.from),'Choose a week.');const mon=hrMonday(cmd.from);
    let n=0;for(const st of x.staff.filter(st=>st.active))for(const d of hrWeek(mon)){const t=addDays(d,7);if(t<s.date)continue;x.roster[hrKey(t,st.id)]=hrShiftFor(s,st,d);n++;}
    assert(n>0,'The target week is already in the past.','INVALID_TRANSITION');
    return `Roster week of ${mon} copied to ${addDays(mon,7)} · ${n} cells`;
  },
  /** Attendance for the business date. No status = self check-in from the simulated clock. */
  HR_ATT_MARK:k=>{
    const {s,cmd,assert,fail}=k;k.open();const st=staffOf(k,cmd.staffId),x=hrEnsure(s),key=hrKey(s.date,st.id);
    assert(st.active,'This staff member is inactive.','INVALID_TRANSITION');
    assert(!hrApprovedLeaveOn(s,st.id,s.date),'Approved leave covers today. Cancel the leave first.','INVALID_TRANSITION');
    const mgr=hrCanManage(s,k.uid);
    if(!cmd.status){
      if(!isOwn(k,st))fail('FORBIDDEN','You can only check yourself in.');
      assert(!x.att[key],'Attendance is already recorded for today.','INVALID_TRANSITION');
      const shift=hrShiftFor(s,st,s.date);
      assert(shift!=='OFF','You are rostered off today — ask a manager to record worked-off attendance.','INVALID_TRANSITION');
      const status=hrCheckinStatus(s.clock,shift);
      x.att[key]={status,at:s.clock,otMin:0,by:k.uid,note:'Self check-in'};
      return `${st.name} · checked in ${s.clock.slice(11)} · ${HR_ATT_LABEL[status]}`;
    }
    if(!mgr)fail('FORBIDDEN','Access denied: approve permission is required to record attendance for staff.');
    assert(['P','L','H','A','OFF'].includes(cmd.status),'Choose an attendance status.');
    const ot=cmd.otMin??0;assert(Number.isInteger(ot)&&ot>=0&&ot<=480,'Overtime must be 0–480 minutes.');
    assert(!(cmd.status==='A'&&ot>0),'An absent day cannot carry overtime.');
    const prev=x.att[key];
    const note=String(cmd.note??'').trim();x.att[key]={status:cmd.status as HRAttStatus,at:s.clock,otMin:ot,by:k.uid,...(note?{note}:{})};
    return `${st.name} · ${s.date} · ${HR_ATT_LABEL[cmd.status as HRAttStatus]}${ot?` · OT ${ot} min`:''}${prev?' (corrected)':''}`;
  },
  HR_LEAVE_REQUEST:k=>{
    const {s,cmd,assert,fail}=k,x=hrEnsure(s),st=staffOf(k,cmd.staffId);
    if(!isOwn(k,st)&&!hrCanManage(s,k.uid))fail('FORBIDDEN','You can request leave only for yourself.');
    assert(st.active,'This staff member is inactive.','INVALID_TRANSITION');
    const type=cmd.leaveType as HRLeaveType;assert(['CL','SL','EL','LOP'].includes(type),'Choose a leave type.');
    if(st.payType==='daily')assert(type==='LOP','Daily-wage staff can take leave without pay only (illustrative policy).');
    assert(DATE.test(cmd.from)&&DATE.test(cmd.to)&&cmd.to>=cmd.from,'The end date must be on or after the start date.');
    assert(cmd.from>=s.date,'Leave cannot start before the business date.');
    assert(addDays(cmd.from,14)>=cmd.to,'Maximum 15 days per request in this demo.');
    assert(String(cmd.reason??'').trim().length>=3,'Give a short reason.');
    assert(!x.leaves.some(l=>l.staffId===st.id&&(l.status==='Pending'||l.status==='Approved')&&l.from<=cmd.to&&l.to>=cmd.from),'These dates overlap another leave request.','NOT_AVAILABLE');
    const days=hrLeaveDays(s,st,cmd.from,cmd.to);assert(days>0,'The chosen dates are all weekly offs.');
    if(type!=='LOP'){const left=HR_LEAVE_ENTITLEMENT[type]-hrLeaveUsed(s,st.id,cmd.from.slice(0,4),type);
      assert(days<=left,`Not enough ${type} balance: ${left} day${left===1?'':'s'} left (illustrative entitlement).`);}
    const id=k.id('HR-LV');
    x.leaves.push({id,staffId:st.id,type,from:cmd.from,to:cmd.to,days,reason:String(cmd.reason).trim(),status:'Pending',requestedBy:k.uid,requestedAt:s.clock});
    return `${id} · ${st.name} · ${type} ${days} day${days===1?'':'s'} requested`;
  },
  HR_LEAVE_DECIDE:k=>{
    manage(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),l=x.leaves.find(l=>l.id===cmd.id);
    assert(l,'Leave request not found.','NOT_FOUND');assert(l!.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
    if(l!.requestedBy===k.uid||hrStaffOfUser(s,k.uid)?.id===l!.staffId)fail('FORBIDDEN','You cannot approve your own request. Switch to another authorized demo user.');
    if(!cmd.approve)assert(String(cmd.note??'').trim(),'Give a reason for rejecting.');
    if(cmd.approve){
      assert(!Object.keys(x.att).some(key=>{const [d,sid]=key.split('|');return sid===l!.staffId&&d>=l!.from&&d<=l!.to;}),'Attendance is already recorded inside these dates.','INVALID_TRANSITION');
      if(l!.type!=='LOP'){const left=HR_LEAVE_ENTITLEMENT[l!.type as 'CL']-hrLeaveUsed(s,l!.staffId,l!.from.slice(0,4),l!.type,l!.id);assert(l!.days<=left,'Leave balance is no longer sufficient.');}
    }
    l!.status=cmd.approve?'Approved':'Rejected';l!.decidedBy=k.uid;l!.decidedAt=s.clock;const note=String(cmd.note??'').trim();if(note)l!.note=note;
    return `${l!.id} · ${l!.status}`;
  },
  HR_LEAVE_CANCEL:k=>{
    const {s,cmd,assert,fail}=k,l=hrEnsure(s).leaves.find(l=>l.id===cmd.id);assert(l,'Leave request not found.','NOT_FOUND');
    const own=l!.requestedBy===k.uid||hrStaffOfUser(s,k.uid)?.id===l!.staffId;
    if(l!.status==='Pending'){if(!own&&!hrCanManage(s,k.uid))fail('FORBIDDEN','Only the requester or a manager can cancel this request.');}
    else{assert(l!.status==='Approved'&&l!.from>=s.date,'Only pending requests, or approved leave that has not started, can be cancelled.','INVALID_TRANSITION');manage(k);}
    l!.status='Cancelled';l!.decidedBy=k.uid;l!.decidedAt=s.clock;return `${l!.id} · cancelled`;
  },
  HR_ADVANCE_ADD:k=>{
    payroll(k);const {s,cmd,assert}=k,x=hrEnsure(s),st=staffOf(k,cmd.staffId);
    assert(st.active,'This staff member is inactive.','INVALID_TRANSITION');k.positive(cmd.amount);
    assert(Number.isSafeInteger(cmd.installment)&&cmd.installment>0&&cmd.installment<=cmd.amount,'Installment must be between ₹0.01 and the advance amount.');
    assert(!x.advances.some(a=>a.staffId===st.id&&hrAdvanceBalance(a)>0),'This staff member already has an open advance.','INVALID_TRANSITION');
    assert(String(cmd.reason??'').trim(),'Give a reason.');
    const id=k.id('HR-ADV');
    x.advances.push({id,staffId:st.id,amount:cmd.amount,installment:cmd.installment,recovered:0,date:s.date,reason:String(cmd.reason).trim(),by:k.uid,recoveries:[]});
    return `${id} · ${st.name} · advance ${money(cmd.amount)} · ${money(cmd.installment)}/month (simulated bank transfer, not drawer cash)`;
  },
  HR_RATES_SET:k=>{
    if(!hrCanRates(k.s,k.uid))k.fail('FORBIDDEN','Access denied: only the Owner can change illustrative payroll rates.');
    const {cmd,assert}=k,r={} as HRRates;
    const bps=(f:keyof HRRates,max:number)=>{assert(Number.isInteger(cmd[f])&&cmd[f]>=0&&cmd[f]<=max,`Invalid value for ${f}.`);r[f]=cmd[f];};
    bps('basicBps',10000);bps('pfBps',3000);bps('esicBps',1000);bps('otBps',30000);
    for(const f of ['pfCap','esicCeiling','pt','ptThreshold'] as const){assert(rupee(cmd[f]),`Invalid amount for ${f}.`);r[f]=cmd[f];}
    assert(cmd.otBps>=10000,'Overtime multiplier must be at least 1×.');
    assert(Number.isInteger(cmd.lateBlock)&&cmd.lateBlock>=0&&cmd.lateBlock<=10,'Late rule must be 0–10.');r.lateBlock=cmd.lateBlock;
    hrEnsure(k.s).rates=r;return 'Illustrative payroll rates updated · locked runs keep their snapshot';
  },
  HR_PAYROLL_RUN:k=>{
    payroll(k);const {s,cmd,assert}=k,x=hrEnsure(s);
    assert(/^\d{4}-\d{2}$/.test(cmd.month),'Choose a month.');
    assert(hrMonthComplete(s,cmd.month),'Payroll can run only for a completed month.','INVALID_TRANSITION');
    const {lines,warnings}=hrComputeRun(s,cmd.month);assert(lines.length,'No staff to pay for this month.');
    const ex=x.runs.find(r=>r.month===cmd.month);
    if(ex){assert(ex.status==='Draft','This payroll is locked and cannot be recalculated.','INVALID_TRANSITION');
      Object.assign(ex,{lines,warnings,rates:{...x.rates},preparedBy:k.uid,preparedAt:s.clock});return `${ex.id} · ${hrMonthLabel(cmd.month)} recalculated`;}
    const id=k.id('HR-PAY');
    x.runs.push({id,month:cmd.month,status:'Draft',rates:{...x.rates},lines,warnings,preparedBy:k.uid,preparedAt:s.clock});
    return `${id} · ${hrMonthLabel(cmd.month)} draft · ${lines.length} staff`;
  },
  HR_PAYROLL_LOCK:k=>{
    payroll(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),run=x.runs.find(r=>r.id===cmd.id);
    assert(run,'Payroll run not found.','NOT_FOUND');assert(run!.status==='Draft','This payroll is already locked.','INVALID_TRANSITION');
    if(run!.preparedBy===k.uid)fail('FORBIDDEN','The person who prepared the payroll cannot lock it. Switch to the other payroll user (Owner / Accountant).');
    const fresh=hrComputeRun(s,run!.month);
    assert(JSON.stringify(fresh.lines)===JSON.stringify(run!.lines)&&JSON.stringify(x.rates)===JSON.stringify(run!.rates),'Attendance, leave, advances or rates changed after this draft. Recalculate before locking.','STALE_VERSION');
    run!.status='Locked';run!.lockedBy=k.uid;run!.lockedAt=s.clock;hrApplyRecoveries(x,run!);
    return `${run!.id} · ${hrMonthLabel(run!.month)} locked · payslips released`;
  },
};
