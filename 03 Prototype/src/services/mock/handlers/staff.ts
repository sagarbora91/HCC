// Module 17 · HR & payroll — command handlers. Command types MUST start with 'HR_'. Return value = audit/summary text.
// Salaries / payroll: Owner + Accountant only (hrCanPay). Roster, attendance for others, leave decisions: 'approve' holders.
// Statutory rates, leave policy, pay-run unlock: Owner (hrCanRates, DEC-058).
// W05-B rule sources (SCC, read-only): payroll\index.html (lockRun, unlockRun, createAdvance, fnfGenerate, waEmpMessage, saveSettings)
// and leave\index.html (submitLeave: weekly offs skipped, duplicate/half-day overlap, capacity per day; decideLeave).
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {
  hrEnsure,hrCanManage,hrCanPay,hrCanRates,hrStaffOfUser,hrShiftFor,hrKey,hrCheckinStatus,hrApprovedLeaveOn,hrLeaveDays,hrLeaveUsed,
  hrComputeRun,hrMonthComplete,hrApplyRecoveries,hrReverseRecoveries,hrAdvanceBalance,hrMonday,hrWeek,hrMonthLabel,hrRatesFor,hrRunErrors,
  hrEntitled,hrGenderOf,hrFnfSettled,HR_FNF_ADJ_MAX,HR_FNF_OWNER_ABOVE,hrBankFilesIssued,hrRatesDefaultFrom,hrRatesPastMonths,hrBankSnapshot,hrCanLink,hrLockedPayslipCount,hrMaskAccount,hrDeptOff,hrDeptCap,hrDeptName,hrFy,hrLastLockedMonth,hrFnfCompute,hrWaNumber,hrLeavePolicy,hrDefaultRates,HR_ATT_LABEL,HR_LEAVE_TYPES,HR_TRACKED,
  type HRStaff,type HRShiftCode,type HRAttStatus,type HRLeaveType,type HRRates,type HRFnfAdj,
} from '../../../domain/streams/staff';
import {addDays} from '../../../domain/money';
import {clone} from '../../../domain/clone';

const SHIFTS:HRShiftCode[]=['M','E','N','G','OFF'];
const DATE=/^\d{4}-\d{2}-\d{2}$/;
function staffOf(k:Kit,id:string):HRStaff{const st=hrEnsure(k.s).staff.find(x=>x.id===id);k.assert(st,'Staff member not found.','NOT_FOUND');return st!;}
function manage(k:Kit){if(!hrCanManage(k.s,k.uid))k.fail('FORBIDDEN','Access denied: approve permission is required to manage staff.');}
function payroll(k:Kit){if(!hrCanPay(k.s,k.uid))k.fail('FORBIDDEN','Access denied: payroll is limited to the Owner and Accountant roles.');}
function owner(k:Kit,what:string){if(!hrCanRates(k.s,k.uid))k.fail('FORBIDDEN',`Access denied: only the Owner can ${what}.`);}
const isOwn=(k:Kit,st:HRStaff)=>!!st.userId&&st.userId===k.uid;
/** F-10: no self-approval of money — salary, PF / ESIC flags, advances and attendance/overtime on your own record go to another person. */
const notSelf=(k:Kit,st:HRStaff|undefined,what:string)=>{if(st&&isOwn(k,st))k.fail('FORBIDDEN',`Self-approval is not allowed: you cannot ${what} on your own staff record. Ask another authorised user (the other payroll user or the Owner).`);};
const rupee=(n:unknown)=>Number.isSafeInteger(n)&&(n as number)>=0&&(n as number)<=100000000;
const dm=(d:string)=>new Date(d+'T12:00:00Z').toLocaleDateString('en-IN',{day:'numeric',month:'short',timeZone:'UTC'});

/** Statutory rate version from a command (callers may omit `from`: F-06 default = the current month, never before the month after the last locked run).
 *  F-05: the new version starts from the version IN FORCE ON ITS OWN DATE (not the current month); only the fields in the command change. */
function ratesFrom(k:Kit):{r:HRRates;base:HRRates}{
  const {cmd,assert,s}=k;
  const last=hrLastLockedMonth(s),from=String(cmd.from??hrRatesDefaultFrom(s));
  assert(/^\d{4}-\d{2}-01$/.test(from),'Rates take effect from the 1st of a month.');
  assert(!last||from.slice(0,7)>last,`Rates for ${hrMonthLabel(last??'')} and earlier are frozen in locked pay-runs. Choose a later month.`,'INVALID_TRANSITION');
  const base=hrRatesFor(s,from.slice(0,7)),r={...hrDefaultRates(),...base,from} as HRRates;
  const int=(f:keyof HRRates,min:number,max:number,label:string)=>{if(cmd[f]===undefined)return;assert(Number.isInteger(cmd[f])&&cmd[f]>=min&&cmd[f]<=max,`Invalid value for ${label}.`);(r as Record<string,unknown>)[f]=cmd[f];};
  int('basicBps',1000,10000,'basic share');int('hraBps',0,10000,'HRA');int('washBps',0,10000,'washing allowance');
  int('pfEeBps',0,2500,'PF employee rate');int('pfErBps',0,2500,'PF employer rate');int('esicEeBps',0,1000,'ESIC employee rate');int('esicErBps',0,1000,'ESIC employer rate');
  int('otBps',0,30000,'overtime multiplier');int('lateBlock',0,10,'late rule');
  for(const f of ['pfWageCap','esicCeiling','ptMaleExempt','ptMaleMid','ptMaleMidAmt','ptStd','ptFeb','ptFemaleExempt'] as const)if(cmd[f]!==undefined){assert(rupee(cmd[f]),`Invalid amount for ${f}.`);r[f]=cmd[f];}
  if(cmd.rounding!==undefined){assert(cmd.rounding==='paise'||cmd.rounding==='rupee','Rounding is paise or rupee.');r.rounding=cmd.rounding;}
  if(cmd.esicPeriod!==undefined){assert(typeof cmd.esicPeriod==='boolean','ESIC contribution-period rule is on or off.');r.esicPeriod=cmd.esicPeriod;}
  rateRules(k,r);
  return {r,base};
}
function rateRules(k:Kit,r:HRRates,where=''){
  k.assert(r.otBps>=10000,'Overtime multiplier must be at least 1×.'+where);
  k.assert(r.basicBps*(10000+r.hraBps+r.washBps)<=10000*10000,'Basic + HRA + washing cannot exceed the earned pay.'+where);
  k.assert(r.ptMaleExempt<r.ptMaleMid,'The PT nil slab must end below the middle slab.'+where);
}

export const handlers:Record<string,Handler>={
  /** Create / edit a staff record. Salary, statutory flags and bank details: payroll roles only. */
  HR_STAFF_SAVE:k=>{
    const {s,cmd,assert,fail}=k,x=hrEnsure(s),pay=hrCanPay(s,k.uid);
    if(!hrCanManage(s,k.uid)&&!pay)fail('FORBIDDEN','Access denied: only managers or payroll roles can edit the staff master.');
    assert(String(cmd.name??'').trim().length>=2,'Enter the staff member’s name.');
    assert(x.depts.some(d=>d.id===cmd.dept),'Choose a department.');
    assert(String(cmd.designation??'').trim(),'Enter a designation.');
    assert(['M','E','N','G'].includes(cmd.defaultShift),'Choose a default shift.');
    assert(Number.isInteger(cmd.weeklyOff)&&cmd.weeklyOff>=0&&cmd.weeklyOff<=6,'Choose a weekly off day.');
    if(cmd.gender!==undefined)assert(cmd.gender==='M'||cmd.gender==='F','Choose M or F for the professional-tax slab.');
    const existing=cmd.id?x.staff.find(st=>st.id===cmd.id):undefined;
    if(cmd.id)assert(existing,'Staff member not found.','NOT_FOUND');
    // F-03: the login link decides whose payslips a user sees as "own" — Owner only, never your own link, audited old → new.
    const newUser=cmd.userId?String(cmd.userId):undefined,linkChanged=newUser!==existing?.userId;
    if(linkChanged){
      if(!hrCanLink(s,k.uid))fail('FORBIDDEN','Only the Owner can link or unlink a demo login — payslip access follows the link.');
      if(newUser===k.uid||(!!existing?.userId&&existing.userId===k.uid))fail('FORBIDDEN','You cannot change the login link of your own staff record.');
      const slips=existing?hrLockedPayslipCount(s,existing.id):0;
      if(newUser){assert(s.users.some(u=>u.id===newUser),'Linked demo user not found.');
        assert(!x.staff.some(st=>st.userId===newUser&&st.id!==cmd.id),'That demo user is already linked to another staff record.');}
      if(existing&&newUser&&slips&&!cmd.confirmRelink)fail('INVALID_TRANSITION',`${existing.name} has payslips in ${slips} locked pay-run${slips===1?'':'s'}. The linked login will see them — confirm the relink.`);
    }
    const salaryGiven=cmd.payType!==undefined||cmd.monthly!==undefined||cmd.daily!==undefined;
    if(salaryGiven){
      const changed=!existing||existing.payType!==(cmd.payType??existing.payType)||existing.monthly!==(cmd.monthly??existing.monthly)||existing.daily!==(cmd.daily??existing.daily);
      if(changed&&!pay)fail('FORBIDDEN','Only the Owner or Accountant can set salary details.');
      if(changed)notSelf(k,existing,'change the salary');
      if(cmd.payType!==undefined)assert(['monthly','daily'].includes(cmd.payType),'Choose a pay type.');
      if(cmd.monthly!==undefined)assert(rupee(cmd.monthly),'Enter a valid monthly salary.');
      if(cmd.daily!==undefined)assert(rupee(cmd.daily),'Enter a valid daily wage.');
    }
    const statKeys=['structure','pfApplicable','esicApplicable','bankName','accountNo','ifsc'] as const;
    const statGiven=statKeys.some(f=>cmd[f]!==undefined);
    const norm=(v:unknown)=>v===undefined?undefined:String(v).trim()||undefined;
    const bankChanged=!!existing&&(['bankName','accountNo','ifsc'] as const).some(f=>cmd[f]!==undefined&&norm(cmd[f])!==existing[f]);
    if(statGiven){
      const changed=!existing||statKeys.some(f=>cmd[f]!==undefined&&cmd[f]!==existing[f]);
      if(changed&&!pay)fail('FORBIDDEN','Only the Owner or Accountant can set PF / ESIC and bank details.');
      if(existing&&(['structure','pfApplicable','esicApplicable'] as const).some(f=>cmd[f]!==undefined&&cmd[f]!==existing[f]))notSelf(k,existing,'change the salary structure or PF / ESIC');
      if(bankChanged&&isOwn(k,existing!))fail('FORBIDDEN','You cannot change the bank details on your own staff record (no self-approval). Ask the other payroll user.');
      if(cmd.structure!==undefined)assert(cmd.structure==='S'||cmd.structure==='B','Choose a salary structure.');
      if(cmd.ifsc)assert(/^[A-Z]{4}0[A-Z0-9]{6}$/.test(String(cmd.ifsc)),'IFSC is 11 characters, e.g. DEMO0000001.');
      if(cmd.accountNo)assert(/^\d{6,18}$/.test(String(cmd.accountNo)),'Account number: 6–18 digits.');
    }
    // F-11: the PT slab (gender) and the payslip WhatsApp number of an existing record are payroll fields (Owner / Accountant).
    const phone=cmd.phone===undefined?existing?.phone??'':String(cmd.phone).trim();
    const genderChanged=!!existing&&cmd.gender!==undefined&&cmd.gender!==hrGenderOf(existing),phoneChanged=!!existing&&phone!==existing.phone;
    if((genderChanged||phoneChanged)&&!pay)fail('FORBIDDEN',`Only the Owner or Accountant can change the ${genderChanged?'professional-tax slab':'payslip mobile number'} of a staff record.`);
    const base={name:String(cmd.name).trim(),dept:cmd.dept,designation:String(cmd.designation).trim(),...(cmd.userId?{userId:cmd.userId as string}:{}),
      defaultShift:cmd.defaultShift,weeklyOff:cmd.weeklyOff,phone,...(cmd.gender?{gender:cmd.gender as 'M'|'F'}:{})};
    const stat=(st:HRStaff)=>{if(!pay)return;
      if(cmd.structure!==undefined)st.structure=cmd.structure;if(cmd.pfApplicable!==undefined)st.pfApplicable=!!cmd.pfApplicable;if(cmd.esicApplicable!==undefined)st.esicApplicable=!!cmd.esicApplicable;
      for(const f of ['bankName','accountNo','ifsc'] as const)if(cmd[f]!==undefined){const v=String(cmd[f]).trim();if(v)st[f]=v;else delete st[f];}};
    if(existing){const notes:string[]=[],was={...existing};
      if(!cmd.userId)delete existing.userId;Object.assign(existing,base);if(pay&&salaryGiven){existing.payType=cmd.payType??existing.payType;existing.monthly=cmd.monthly??existing.monthly;existing.daily=cmd.daily??existing.daily;}
      stat(existing);
      if(linkChanged)notes.push(`login ${was.userId??'(none)'} → ${existing.userId??'(none)'}`);
      if(genderChanged)notes.push(`PT slab ${hrGenderOf(was)==='F'?'women':'men'} → ${hrGenderOf(existing)==='F'?'women':'men'}`);
      if(phoneChanged)notes.push(`payslip mobile ${was.phone?'••••'+was.phone.replace(/\D/g,'').slice(-4):'(none)'} → ${existing.phone?'••••'+existing.phone.replace(/\D/g,'').slice(-4):'(none)'}`);
      // F-02: audit old → new bank details (masked); locked runs keep the details frozen at lock.
      if(bankChanged)notes.push(`bank ${was.bankName??'(none)'} ${hrMaskAccount(was.accountNo)} ${was.ifsc??''} → ${existing.bankName??'(none)'} ${hrMaskAccount(existing.accountNo)} ${existing.ifsc??''} (future runs only; locked runs keep their frozen details)`);
      return `${existing.id} · ${existing.name} updated${notes.length?' · '+notes.join(' · '):''}`;}
    const st:HRStaff={id:k.id('HR-S'),...base,payType:pay&&cmd.payType?cmd.payType:'monthly',monthly:pay?cmd.monthly??0:0,daily:pay?cmd.daily??0:0,joined:s.date,active:true};
    stat(st);x.staff.push(st);return `${st.id} · ${st.name} added${st.userId?` · login ${st.userId}`:''}${pay?'':' (salary to be set by payroll)'}`;
  },
  /** Deactivate (exit) or reactivate a staff record. */
  HR_STAFF_ACTIVE:k=>{
    manage(k);const st=staffOf(k,k.cmd.id);
    if(k.cmd.active){k.assert(!st.active,'Already active.','INVALID_TRANSITION');
      const f=(hrEnsure(k.s).fnf??[]).find(f=>f.staffId===st.id);
      k.assert(!f||hrFnfSettled(f),'A draft full and final settlement is waiting — discard it before reactivating.','INVALID_TRANSITION');
      k.assert(!f,'Full and final settlement is done — add a new staff record instead.','INVALID_TRANSITION');
      st.active=true;delete st.exitedOn;return st.name+' · reactivated';}
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
  /** Attendance for the business date. No status = self check-in from the simulated clock. A half-day leave leaves the other half to mark. */
  HR_ATT_MARK:k=>{
    const {s,cmd,assert,fail}=k;k.open();const st=staffOf(k,cmd.staffId),x=hrEnsure(s),key=hrKey(s.date,st.id);
    assert(st.active,'This staff member is inactive.','INVALID_TRANSITION');
    const leave=hrApprovedLeaveOn(s,st.id,s.date);
    assert(!leave||leave.half,'Approved leave covers today. Cancel the leave first.','INVALID_TRANSITION');
    const mgr=hrCanManage(s,k.uid);
    if(!cmd.status){
      if(!isOwn(k,st))fail('FORBIDDEN','You can only check yourself in.');
      assert(!x.att[key],'Attendance is already recorded for today.','INVALID_TRANSITION');
      const shift=hrShiftFor(s,st,s.date);
      assert(shift!=='OFF','You are rostered off today — ask a manager to record worked-off attendance.','INVALID_TRANSITION');
      const status:HRAttStatus=leave?'H':hrCheckinStatus(s.clock,shift);
      x.att[key]={status,at:s.clock,otMin:0,by:k.uid,note:leave?'Self check-in · half-day leave':'Self check-in'};
      return `${st.name} · checked in ${s.clock.slice(11)} · ${HR_ATT_LABEL[status]}`;
    }
    if(!mgr)fail('FORBIDDEN','Access denied: approve permission is required to record attendance for staff.');
    notSelf(k,st,'record or correct attendance or overtime (use self check-in)');
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
    const type=cmd.leaveType as HRLeaveType;assert(HR_LEAVE_TYPES.includes(type),'Choose a leave type.');
    if(st.payType==='daily')assert(type==='LOP','Daily-wage staff can take leave without pay only (illustrative policy).');
    const half=cmd.half as 'AM'|'PM'|undefined;if(half!==undefined)assert(half==='AM'||half==='PM','Choose the first or second half.');
    const to=half?cmd.from:cmd.to;
    assert(DATE.test(cmd.from)&&DATE.test(to)&&to>=cmd.from,'The end date must be on or after the start date.');
    assert(cmd.from>=s.date,'Leave cannot start before the business date.');
    assert(addDays(cmd.from,14)>=to,'Maximum 15 days per request in this demo.');
    assert(hrFy(cmd.from).to>=to,'A request cannot cross 31 March — split it per financial year.');
    assert(String(cmd.reason??'').trim().length>=3,'Give a short reason.');
    // SCC duplicate rule: a full day conflicts with anything; first and second half of the same day can coexist.
    assert(!x.leaves.some(l=>l.staffId===st.id&&(l.status==='Pending'||l.status==='Approved')&&l.from<=to&&l.to>=cmd.from&&!(half&&l.half&&l.half!==half)),'These dates overlap another leave request.','NOT_AVAILABLE');
    const days=hrLeaveDays(s,st,cmd.from,to,half);assert(days>0,half?'That day is a weekly off.':'The chosen dates are all weekly offs.');
    if((HR_TRACKED as string[]).includes(type)){const t=type as 'CL'|'SL'|'EL',left=hrEntitled(s,t)-hrLeaveUsed(s,st.id,cmd.from,t);
      assert(days<=left,`Not enough ${type} balance: ${left} day${left===1?'':'s'} left in ${hrFy(cmd.from).label}.`);}
    // SCC capacity: max staff off per day — here per department; pending and approved requests hold a slot.
    const cap=hrDeptCap(s,st.dept);
    for(let d=cmd.from;d<=to;d=addDays(d,1)){if(hrShiftFor(s,st,d)==='OFF')continue;
      const off=hrDeptOff(s,st.dept,d,st.id).length;
      assert(off<cap,`${hrDeptName(s,st.dept)} already has ${off} staff off on ${dm(d)} (maximum ${cap} per day).`,'NOT_AVAILABLE');}
    const id=k.id('HR-LV');
    x.leaves.push({id,staffId:st.id,type,from:cmd.from,to,days,reason:String(cmd.reason).trim(),...(half?{half}:{}),status:'Pending',requestedBy:k.uid,requestedAt:s.clock});
    return `${id} · ${st.name} · ${type} ${days} day${days===1?'':'s'}${half?` (${half==='AM'?'first':'second'} half)`:''} requested`;
  },
  HR_LEAVE_DECIDE:k=>{
    manage(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),l=x.leaves.find(l=>l.id===cmd.id);
    assert(l,'Leave request not found.','NOT_FOUND');assert(l!.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
    if(l!.requestedBy===k.uid||hrStaffOfUser(s,k.uid)?.id===l!.staffId)fail('FORBIDDEN','You cannot approve your own request. Switch to another authorized demo user.');
    if(!cmd.approve)assert(String(cmd.note??'').trim(),'Give a reason for rejecting.');
    if(cmd.approve){
      assert(!Object.keys(x.att).some(key=>{const [d,sid]=key.split('|');return sid===l!.staffId&&d>=l!.from&&d<=l!.to&&!(l!.half&&(x.att[key].status==='H'||x.att[key].status==='A'));}),'Attendance is already recorded inside these dates.','INVALID_TRANSITION');
      if((HR_TRACKED as string[]).includes(l!.type)){const left=hrEntitled(s,l!.type as 'CL')-hrLeaveUsed(s,l!.staffId,l!.from,l!.type,l!.id);assert(l!.days<=left,'Leave balance is no longer sufficient.');}
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
  /** Leave policy (SCC entitlements + daily capacity, per department). Owner. */
  HR_LEAVE_POLICY_SET:k=>{
    owner(k,'change the leave policy');const {s,cmd,assert}=k,x=hrEnsure(s),p=clone(hrLeavePolicy(s));
    if(cmd.ent!==undefined)for(const t of HR_TRACKED){const v=cmd.ent?.[t];assert(Number.isInteger(v)&&v>=0&&v<=60,`Entitlement for ${t}: 0–60 days.`);p.ent[t]=v;}
    if(cmd.defaultCap!==undefined){assert(Number.isInteger(cmd.defaultCap)&&cmd.defaultCap>=1&&cmd.defaultCap<=20,'Maximum staff off per day: 1–20.');p.defaultCap=cmd.defaultCap;}
    if(cmd.deptCap!==undefined){assert(cmd.deptCap&&typeof cmd.deptCap==='object','Department limits are missing.');p.deptCap={};
      for(const [d,v] of Object.entries(cmd.deptCap as Record<string,unknown>)){assert(x.depts.some(q=>q.id===d),'Unknown department.');
        if(v===null||v===undefined||v==='')continue;assert(Number.isInteger(v)&&(v as number)>=1&&(v as number)<=20,'Department limit: 1–20.');p.deptCap[d]=v as number;}}
    x.leavePolicy=p;return `Leave policy updated · CL ${p.ent.CL} · SL ${p.ent.SL} · EL ${p.ent.EL} · max ${p.defaultCap}/department/day · new requests only`;
  },
  HR_ADVANCE_ADD:k=>{
    payroll(k);const {s,cmd,assert}=k,x=hrEnsure(s),st=staffOf(k,cmd.staffId);notSelf(k,st,'give a salary advance');
    assert(st.active,'This staff member is inactive.','INVALID_TRANSITION');k.positive(cmd.amount);
    assert(Number.isSafeInteger(cmd.installment)&&cmd.installment>=0&&cmd.installment<=cmd.amount,'Monthly recovery must be between ₹0 (full recovery next payroll) and the advance amount.');
    assert(!x.advances.some(a=>a.staffId===st.id&&hrAdvanceBalance(a)>0),'This staff member already has an open advance.','INVALID_TRANSITION');
    assert(String(cmd.reason??'').trim(),'Give a reason.');
    const fromMonth=cmd.fromMonth??s.date.slice(0,7);assert(/^\d{4}-\d{2}$/.test(fromMonth)&&fromMonth>=s.date.slice(0,7),'Recovery starts this month or later.');
    const id=k.id('HR-ADV');
    x.advances.push({id,staffId:st.id,amount:cmd.amount,installment:cmd.installment,recovered:0,date:s.date,reason:String(cmd.reason).trim(),by:k.uid,recoveries:[],...(cmd.fromMonth?{fromMonth}:{})});
    return `${id} · ${st.name} · advance ${money(cmd.amount)} · ${cmd.installment?money(cmd.installment)+'/month':'full recovery'} from ${hrMonthLabel(fromMonth)} (simulated bank transfer, not drawer cash)`;
  },
  /** New dated statutory rate version (effective from the 1st of a month after the last locked run). Owner. */
  HR_STAT_RATES_SET:k=>{
    owner(k,'change statutory rates');const x=hrEnsure(k.s),{r,base}=ratesFrom(k);
    // F-05: a field changed here is carried into LATER versions that had inherited the old value, so a later version
    // never silently reverts it; a later version that set its own value for that field keeps it (and stops the carry).
    const changed=(Object.keys(r) as (keyof HRRates)[]).filter(f=>f!=='from'&&r[f]!==base[f]);
    const later=x.rateVersions.filter(v=>v.from>r.from).sort((a,b)=>a.from<b.from?-1:1).map(v=>({...v}));
    const carried:string[]=[],kept:string[]=[],live=new Set(changed);
    for(const v of later){for(const f of [...live]){if(v[f]===base[f]){(v as Record<string,unknown>)[f]=r[f];carried.push(`${String(f)} → ${v.from}`);}else{live.delete(f);kept.push(`${v.from} keeps its own ${String(f)}`);}}
      rateRules(k,v,` (version from ${v.from})`);}
    x.rateVersions=[...x.rateVersions.filter(v=>v.from<r.from),r,...later].sort((a,b)=>a.from<b.from?-1:1);
    const note=later.length?` · later version${later.length===1?'':'s'} from ${later.map(v=>v.from).join(', ')}: ${carried.length?'change carried forward ('+carried.join(', ')+')':'no change carried'}${kept.length?'; '+kept.join('; '):''}`:'';
    const past=hrRatesPastMonths(k.s,r.from);// F-06: say so when a completed (unlocked) month changes
    return `Statutory rates from ${r.from} saved (${changed.length?changed.join(', '):'no field changed'}; illustrative — CA to confirm) · locked runs keep their snapshot${note}${past.length?` · changes completed month${past.length===1?'':'s'} not yet locked: ${past.map(hrMonthLabel).join(', ')} — recalculate ${past.length===1?'its':'their'} payroll`:''}`;
  },
  /** Legacy name (Master configuration wrappers): same as HR_STAT_RATES_SET. */
  HR_RATES_SET:k=>handlers.HR_STAT_RATES_SET(k),
  HR_PAYROLL_RUN:k=>{
    payroll(k);const {s,cmd,assert}=k,x=hrEnsure(s);
    assert(/^\d{4}-\d{2}$/.test(cmd.month),'Choose a month.');
    assert(hrMonthComplete(s,cmd.month),'Payroll can run only for a completed month.','INVALID_TRANSITION');
    const rates=hrRatesFor(s,cmd.month),{lines,warnings}=hrComputeRun(s,cmd.month,rates);assert(lines.length,'No staff to pay for this month.');
    const ex=x.runs.find(r=>r.month===cmd.month);
    if(ex){assert(ex.status==='Draft','This payroll is locked and cannot be recalculated.','INVALID_TRANSITION');
      Object.assign(ex,{lines,warnings,rates:{...rates},preparedBy:k.uid,preparedAt:s.clock});return `${ex.id} · ${hrMonthLabel(cmd.month)} recalculated`;}
    const id=k.id('HR-PAY');
    x.runs.push({id,month:cmd.month,status:'Draft',rates:{...rates},lines,warnings,preparedBy:k.uid,preparedAt:s.clock});
    return `${id} · ${hrMonthLabel(cmd.month)} draft · ${lines.length} staff`;
  },
  HR_PAYROLL_LOCK:k=>{
    payroll(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),run=x.runs.find(r=>r.id===cmd.id);
    assert(run,'Payroll run not found.','NOT_FOUND');assert(run!.status==='Draft','This payroll is already locked.','INVALID_TRANSITION');
    if(run!.preparedBy===k.uid)fail('FORBIDDEN','The person who prepared the payroll cannot lock it. Switch to the other payroll user (Owner / Accountant).');
    const errs=hrRunErrors(run!);assert(!errs.length,'Fix before locking: '+errs.join('; ')+'.');
    const fresh=hrComputeRun(s,run!.month);
    assert(JSON.stringify(fresh.lines)===JSON.stringify(run!.lines)&&JSON.stringify(hrRatesFor(s,run!.month))===JSON.stringify(run!.rates),'Attendance, leave, advances or rates changed after this draft. Recalculate before locking.','STALE_VERSION');
    run!.status='Locked';run!.lockedBy=k.uid;run!.lockedAt=s.clock;hrApplyRecoveries(x,run!);hrBankSnapshot(x,run!);
    return `${run!.id} · ${hrMonthLabel(run!.month)} locked · bank details frozen · payslips released`;
  },
  /** SCC unlockRun: Owner only, latest locked run only, with a reason; advance recoveries are reversed and the run returns to Draft. */
  HR_PAYROLL_UNLOCK:k=>{
    owner(k,'unlock a pay-run');const {s,cmd,assert}=k,x=hrEnsure(s),run=x.runs.find(r=>r.id===cmd.id);
    assert(run,'Payroll run not found.','NOT_FOUND');assert(run!.status==='Locked','This payroll is not locked.','INVALID_TRANSITION');
    assert(String(cmd.reason??'').trim().length>=5,'Give the reason for unlocking (at least 5 characters).');
    assert(!x.runs.some(r=>r.status==='Locked'&&r.month>run!.month),'A later month is locked. Unlock the latest month first.','INVALID_TRANSITION');
    assert(Object.keys(x.att).some(key=>key.startsWith(run!.month)),'Attendance for this month is archived — the run cannot be unlocked.','INVALID_TRANSITION');
    assert(!(x.fnf??[]).some(f=>f.month>=run!.month),'A full and final settlement was made after this run. It cannot be unlocked.','INVALID_TRANSITION');
    // F-09: a bank file already given to the bank needs explicit confirmation; payslip sends and bank files of this lock are kept as superseded.
    const files=run!.bankFiles??[],sent=run!.sent??[];
    if(files.length&&!cmd.confirmBankFile)k.fail('INVALID_TRANSITION',`A bank transfer file for ${hrMonthLabel(run!.month)} was already generated (${files.length}×, last ${files[files.length-1].at}). Unlocking may lead to paying twice — confirm to unlock.`);
    hrReverseRecoveries(x,run!.id);
    run!.unlocks=[...(run!.unlocks??[]),{at:s.clock,by:k.uid,reason:String(cmd.reason).trim(),...(sent.length?{sent}:{}),...(files.length?{bankFiles:files}:{}),
      nets:Object.fromEntries(run!.lines.map(l=>[l.staffId,l.net]))}];
    run!.status='Draft';delete run!.lockedBy;delete run!.lockedAt;delete run!.bank;delete run!.sent;delete run!.bankFiles;
    return `${run!.id} · ${hrMonthLabel(run!.month)} unlocked · advance recoveries reversed · payslips withdrawn${sent.length?` · ${sent.length} sent payslip${sent.length===1?'':'s'} marked superseded`:''}${files.length?' · bank file already issued — next file must not pay twice':''}`;
  },
  /** F-09: record (and allow) a bank transfer file for a locked run; a repeat — including after an unlock — needs confirmReissue. */
  HR_PAYROLL_BANK_FILE:k=>{
    payroll(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),run=x.runs.find(r=>r.id===cmd.id);
    assert(run,'Payroll run not found.','NOT_FOUND');assert(run!.status==='Locked','The bank file is made after the pay-run is locked.','INVALID_TRANSITION');
    const prior=hrBankFilesIssued(run!);
    if(prior.length&&!cmd.confirmReissue)fail('INVALID_TRANSITION',`A bank file for ${hrMonthLabel(run!.month)} was already generated on ${prior[prior.length-1].at} (net ${money(prior[prior.length-1].net)}). Paying this file in full again may pay twice — confirm to generate it again.`);
    const net=run!.lines.reduce((n,l)=>n+l.net,0);
    run!.bankFiles=[...(run!.bankFiles??[]),{at:s.clock,by:k.uid,net}];
    return `${run!.id} · ${hrMonthLabel(run!.month)} bank transfer file · net ${money(net)}${prior.length?` · REISSUE (earlier file ${money(prior[prior.length-1].net)})`:''} (simulated — no money moves)`;
  },
  /** DEC-056: simulated WhatsApp send of a locked payslip (nothing leaves the device). */
  HR_PAYSLIP_SEND:k=>{
    payroll(k);const {s,cmd,assert}=k,x=hrEnsure(s),run=x.runs.find(r=>r.id===cmd.runId);
    assert(run,'Payroll run not found.','NOT_FOUND');assert(run!.status==='Locked','Payslips are sent after the pay-run is locked.','INVALID_TRANSITION');
    const line=run!.lines.find(l=>l.staffId===cmd.staffId);assert(line,'This staff member is not in the run.','NOT_FOUND');
    const st=staffOf(k,cmd.staffId),to=hrWaNumber(st.phone);assert(to,`${st.name} has no usable mobile number.`);
    run!.sent=[...(run!.sent??[]),{staffId:st.id,to,at:s.clock,by:k.uid}];
    return `Payslip ${hrMonthLabel(run!.month)} · ${st.name} · WhatsApp to +${to} (simulated — no message sent)`;
  },
  /** Full and final settlement (SCC fnfGenerate) for an exited staff member. F-12: the maker prepares a DRAFT (nothing is written off);
   *  another payroll user confirms it with HR_FNF_CONFIRM (the Owner when additions exceed ₹1,00,000). Each adjustment ≤ ₹2,00,000. */
  HR_FNF_SETTLE:k=>{
    payroll(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),st=staffOf(k,cmd.staffId);
    if(isOwn(k,st))fail('FORBIDDEN','You cannot settle your own account.');
    assert(!st.active&&st.exitedOn,'Record the exit in the staff master first.','INVALID_TRANSITION');
    const prev=(x.fnf??[]).find(f=>f.staffId===st.id);
    assert(!prev||!hrFnfSettled(prev),'Full and final is already done for this staff member.','INVALID_TRANSITION');
    assert(!prev,'A draft settlement is already waiting for confirmation — confirm or discard it first.','INVALID_TRANSITION');
    const adj:HRFnfAdj[]=(Array.isArray(cmd.adjustments)?cmd.adjustments:[]).filter((a:HRFnfAdj)=>a&&String(a.label??'').trim()&&a.amount);
    assert(adj.length<=4,'Up to 4 adjustments.');
    for(const a of adj){assert(a.kind==='add'||a.kind==='ded','Adjustment is an addition or a deduction.');assert(Number.isSafeInteger(a.amount)&&a.amount>0&&a.amount<=HR_FNF_ADJ_MAX,`Enter a valid adjustment amount (maximum ${money(HR_FNF_ADJ_MAX)} each).`);}
    const clean=adj.map(a=>({label:String(a.label).trim().slice(0,80),kind:a.kind,amount:a.amount}));
    const c=hrFnfCompute(s,st,clean),id=k.id('HR-FNF'),adds=clean.filter(a=>a.kind==='add').reduce((n,a)=>n+a.amount,0);
    x.fnf=[...(x.fnf??[]),{id,staffId:st.id,name:st.name,lastDay:c.lastDay,month:c.month,salaryIncluded:c.salaryIncluded,...(c.line?{line:c.line}:{}),rates:{...c.rates},
      advanceOutstanding:c.advanceOutstanding,adjustments:clean,earnings:c.earnings,recoveries:c.recoveries,net:c.net,by:k.uid,at:s.clock,status:'Draft'}];
    return `${id} · ${st.name} · full and final DRAFT · ${c.net<0?'recoverable from employee '+money(-c.net):'net payable '+money(c.net)} · to be confirmed by ${adds>HR_FNF_OWNER_ABOVE?'the Owner (additions above '+money(HR_FNF_OWNER_ABOVE)+')':'the other payroll user'}`;
  },
  /** F-12 checker: another payroll user (never the maker, never your own account) confirms; the figures must still match a fresh computation. */
  HR_FNF_CONFIRM:k=>{
    payroll(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),f=(x.fnf??[]).find(f=>f.id===cmd.id);
    assert(f,'Settlement not found.','NOT_FOUND');assert(f!.status==='Draft','This settlement is already confirmed.','INVALID_TRANSITION');
    if(f!.by===k.uid)fail('FORBIDDEN','The person who prepared the settlement cannot confirm it. Switch to the other payroll user (Owner / Accountant).');
    const st=staffOf(k,f!.staffId);if(isOwn(k,st))fail('FORBIDDEN','You cannot confirm your own settlement.');
    const adds=f!.adjustments.filter(a=>a.kind==='add').reduce((n,a)=>n+a.amount,0);
    if(adds>HR_FNF_OWNER_ABOVE&&!hrCanRates(s,k.uid))fail('FORBIDDEN',`Additions of ${money(adds)} exceed ${money(HR_FNF_OWNER_ABOVE)}: only the Owner can confirm this settlement.`);
    const c=hrFnfCompute(s,st,f!.adjustments);
    assert(c.net===f!.net&&c.earnings===f!.earnings&&c.recoveries===f!.recoveries&&c.advanceOutstanding===f!.advanceOutstanding&&JSON.stringify(c.line)===JSON.stringify(f!.line),
      'Attendance, advances or rates changed after this draft. Discard it and prepare the settlement again.','STALE_VERSION');
    for(const a of x.advances.filter(a=>a.staffId===st.id&&hrAdvanceBalance(a)>0)){const b=hrAdvanceBalance(a);a.recovered+=b;a.recoveries.push({runId:f!.id,amount:b});}
    f!.status='Settled';f!.confirmedBy=k.uid;f!.confirmedAt=s.clock;
    return `${f!.id} · ${f!.name} · full and final confirmed · ${f!.net<0?'recoverable from employee '+money(-f!.net):'net payable '+money(f!.net)} (simulated transfer)`;
  },
  /** F-12: discard a draft settlement (payroll roles). */
  HR_FNF_DISCARD:k=>{
    payroll(k);const {cmd,assert}=k,x=hrEnsure(k.s),f=(x.fnf??[]).find(f=>f.id===cmd.id);
    assert(f,'Settlement not found.','NOT_FOUND');assert(f!.status==='Draft','A confirmed settlement cannot be discarded.','INVALID_TRANSITION');
    x.fnf=(x.fnf??[]).filter(q=>q.id!==f!.id);return `${f!.id} · ${f!.name} · draft settlement discarded`;
  },
};
