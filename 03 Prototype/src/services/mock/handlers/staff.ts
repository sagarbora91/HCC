// Module 17 · HR & payroll — command handlers. Command types MUST start with 'HR_'. Return value = audit/summary text.
// Salaries / payroll: Owner + Accountant only (hrCanPay). Roster, attendance for others, leave decisions: 'approve' holders.
// Statutory rates, leave policy, pay-run unlock: Owner (hrCanRates, DEC-058).
// W05-B rule sources (SCC, read-only): payroll\index.html (lockRun, unlockRun, createAdvance, fnfGenerate, waEmpMessage, saveSettings).
// SCC leave\index.html IN FULL (DEC-116 / DEC-119, 27 Oct 2026): submitLeave per date (week-off → duplicate → capacity 3/day · 4/shift), decideLeave per
// date, remove = cancel with audit, entitlements + overrides, week-off set, advisory department caps, blackouts, agenda, import / clear. HCC safeguards
// kept: nobody approves their own leave, no hard delete of leave, role permissions (DEC-058), staff by ID, past days through DEC-075 corrections.
import type {Handler,Kit} from '../commands';
import {mstSpec,mstNextSort,type MasterRow} from '../../../domain/streams/masters';
import {money} from '../../../domain/money';
import {
  hrEnsure,hrCanManage,hrCanPay,hrCanRates,hrStaffOfUser,hrShiftFor,hrKey,hrCheckinStatus,hrApprovedLeaveOn,
  HR_ATT_MARKS,hrComputeRun,hrMonthComplete,hrApplyRecoveries,hrReverseRecoveries,hrAdvanceBalance,hrMonday,hrWeek,hrMonthLabel,hrRatesFor,hrRunErrors,
  hrGenderOf,hrFnfSettled,HR_FNF_ADJ_MAX,HR_FNF_OWNER_ABOVE,hrBankFilesIssued,hrRatesDefaultFrom,hrRatesPastMonths,hrBankSnapshot,hrCanLink,hrLockedPayslipCount,hrMaskAccount,hrLastLockedMonth,hrFnfCompute,hrWaNumber,hrLeavePolicy,hrDefaultRates,HR_ATT_LABEL,HR_LEAVE_TYPES,HR_TRACKED,
  hrParseEntry,hrPlanCorrection,hrCorrectionDateRefusal,hrEntryLabel,hrSameEntry,hrPendingRequests,hrLeaveActive,hrIsDuplicate,hrCanAddLeave,hrIsWeekOff,hrBlackoutHits,
  hrApproverChoices,hrRunFor,HR_ENT_DEFAULTS,hrKindLabel,
  type HRStaff,type HRShiftCode,type HRAttStatus,type HRLeaveType,type HRRates,type HRFnfAdj,type HRDayEntry,type HRRequest,type HRBank,type HRLeave,type HRHalf,type HREnt,
} from '../../../domain/streams/staff';
import {hrLeaveRequestPlan,hrLeaveRequestToast,hrLeaveOverBalance,hrLeaveHardBlackoutText,hrLeaveSoftBlackoutText,hrLeaveCategory,hrLeaveImportParse} from '../../../domain/streams/staff-leave';
import {hrDesignationOf,hrShiftChoices,hrLeaveName,HRM_DESIGNATIONS,HRM_LEAVE_TYPES,HRM_HOLIDAYS,type HRDesignation} from '../../../domain/streams/people-masters';
import {mstRows,mstStored,mstByCode} from '../../../domain/streams/masters';
import {addDays} from '../../../domain/money';
import {clone} from '../../../domain/clone';

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
  int('otBps',0,30000,'overtime multiplier');int('lateBlock',0,10,'late rule');int('daysOverride',0,31,'total days of a month');
  for(const f of ['pfWageCap','esicCeiling','ptMaleExempt','ptMaleMid','ptMaleMidAmt','ptStd','ptFeb','ptFemaleExempt'] as const)if(cmd[f]!==undefined){assert(rupee(cmd[f]),`Invalid amount for ${f}.`);r[f]=cmd[f];}
  if(cmd.rounding!==undefined){assert(cmd.rounding==='paise'||cmd.rounding==='rupee','Rounding is paise or rupee.');r.rounding=cmd.rounding;}
  if(cmd.esicPeriod!==undefined){assert(typeof cmd.esicPeriod==='boolean','ESIC contribution-period rule is on or off.');r.esicPeriod=cmd.esicPeriod;}
  rateRules(k,r);
  return {r,base};
}
/** DEC-075: writes the corrected day (mark, removed mark or one-day approved leave; a replaced one-day leave is cancelled) and recalculates the
 *  month's DRAFT pay-run (the preparer stays the maker; the lock still compares a fresh computation). Returns the recalculation note or ''. */
function applyCorrection(k:Kit,st:HRStaff,date:string,day:{oneDayLeave?:{id:string}},target:HRDayEntry,reqId:string,reason:string,requestedBy:string,decidedBy:string){
  const {s}=k,x=hrEnsure(s),key=hrKey(date,st.id);
  if(day.oneDayLeave){const l=x.leaves.find(q=>q.id===day.oneDayLeave!.id)!;l.status='Cancelled';l.cancelledBy=decidedBy;l.cancelledAt=s.clock;l.note=`Replaced by attendance correction ${reqId}`;}
  if(target.kind==='mark')x.att[key]={status:target.status,at:s.clock,otMin:target.otMin,by:requestedBy,note:`Correction ${reqId}`};
  else{delete x.att[key];
    if(target.kind==='leave')x.leaves.push({id:k.id('HR-LV'),staffId:st.id,date,type:target.type,from:date,to:date,days:1,reason,approver:decidedBy,status:'Approved',requestedBy,requestedAt:s.clock,
      decidedBy,decidedAt:s.clock,note:`Back-dated by attendance correction ${reqId}`,correctionId:reqId});}
  const ym=date.slice(0,7),run=x.runs.find(r=>r.month===ym);if(!run||run.status!=='Draft')return '';
  const rates=hrRatesFor(s,ym),{lines,warnings}=hrComputeRun(s,ym,rates);if(!lines.length)return '';
  Object.assign(run,{lines,warnings,rates:{...rates}});return `${hrMonthLabel(ym)} draft pay-run recalculated`;
}
const bankText=(b:HRBank)=>b.bankName||b.accountNo||b.ifsc?`${b.bankName||'(none)'} ${hrMaskAccount(b.accountNo||undefined)} ${b.ifsc}`.trim():'(none)';
function rateRules(k:Kit,r:HRRates,where=''){
  k.assert(r.otBps>=10000,'Overtime multiplier must be at least 1×.'+where);
  k.assert(r.basicBps*(10000+r.hraBps+r.washBps)<=10000*10000,'Basic + HRA + washing cannot exceed the earned pay.'+where);
  k.assert(r.ptMaleExempt<r.ptMaleMid,'The PT nil slab must end below the middle slab.'+where);
}

/** MA-6: the yearly entitlements of CL / SL / EL live on the Leave types list; the leave-policy command writes through to it (history per type). */
function writeQuotas(k:Kit,ent:{CL:number;SL:number;EL:number}){
  const rows=mstRows(k.s,HRM_LEAVE_TYPES).map(r=>({...r})),changed:string[]=[];
  for(const code of HR_TRACKED){const r=rows.find(x=>x.code===code);if(!r||r.yearlyQuota===ent[code])continue;
    k.s.mastersX??={rows:{},history:[]};k.s.mastersX.history??=[];
    k.s.mastersX.history.unshift({id:k.id('MSTH'),master:HRM_LEAVE_TYPES,rowId:r.id,code,field:'yearlyQuota',old:r.yearlyQuota===undefined?'—':String(r.yearlyQuota),new:String(ent[code]),by:k.uid,at:k.s.clock});
    r.yearlyQuota=ent[code];delete r.carryForward;delete r.encashment;changed.push(code);}
  if(!changed.length)return;
  (k.s.mastersX!.rows??={})[HRM_LEAVE_TYPES]=rows;
}

/** Masters integration: the Holiday calendar rows (MA-6 master) for writing — the stored list, or the seed on the first change. */
function holidayRows(k:Kit):MasterRow[]{return mstRows(k.s,HRM_HOLIDAYS).map(r=>({...r}));}
/** Stores the calendar and one history line per change ("Master Holiday calendar: name old → new"), like the generic master commands. */
function holidayWrite(k:Kit,rows:MasterRow[],after:MasterRow|undefined,before:MasterRow|undefined){
  const x=k.s.mastersX??(k.s.mastersX={rows:{},history:[]});x.rows={...(x.rows??{}),[HRM_HOLIDAYS]:rows};x.history=[...(x.history??[])];
  const row=after??before!,was=before&&before.active&&before.kind==='paid'?before.name:'—',now=after?after.name:'—';
  if(was!==now)x.history.unshift({id:k.id('MSTH'),master:HRM_HOLIDAYS,rowId:row.id,code:row.code,field:'name',old:was,new:now,by:k.uid,at:k.s.clock});
}

export const handlers:Record<string,Handler>={
  /** Create / edit a staff record. Salary, statutory flags and bank details: payroll roles only. */
  HR_STAFF_SAVE:k=>{
    const {s,cmd,assert,fail}=k,x=hrEnsure(s),pay=hrCanPay(s,k.uid);
    if(!hrCanManage(s,k.uid)&&!pay)fail('FORBIDDEN','Access denied: only managers or payroll roles can edit the staff master.');
    assert(String(cmd.name??'').trim().length>=2,'Enter the staff member’s name.');
    assert(x.depts.some(d=>d.id===cmd.dept),'Choose a department.');
    // MA-6: a designation chosen from the Designations list (code) supplies the printed name; a free-text designation still works (older callers).
    let desig=String(cmd.designation??'').trim(),desigCode:string|undefined;
    if(cmd.designationCode){
      const row=mstByCode(s,HRM_DESIGNATIONS,String(cmd.designationCode)) as HRDesignation|undefined;
      assert(row,'Choose a designation from the list.');
      assert(row!.active||cmd.designationCode===x.staff.find(st=>st.id===cmd.id)?.designationCode,`${row!.name} is retired — choose an active designation.`,'INVALID_TRANSITION');
      assert(!row!.dept||row!.dept===cmd.dept,`${row!.name} belongs to department ${row!.dept}.`);
      desig=row!.name;desigCode=row!.code;
    }
    assert(desig,'Enter a designation.');
    const existing0=cmd.id?x.staff.find(st=>st.id===cmd.id):undefined;
    // MA-6: the default shift is a pattern of the Shift patterns list (a retired one stays valid on a record that already has it).
    assert(typeof cmd.defaultShift==='string'&&cmd.defaultShift!=='OFF'&&hrShiftChoices(s,existing0?.defaultShift).some(p=>p.code===cmd.defaultShift),'Choose a default shift.');
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
    // DEC-084: only the Owner's bank-detail change applies at once (audited); anyone else's — also a new record's first details — waits for the
    // Owner as a request; payments (lock snapshots, bank files) keep the stored details until it is approved.
    const BANK=['bankName','accountNo','ifsc'] as const;
    const bankSet=pay&&(existing?bankChanged:BANK.some(f=>norm(cmd[f])!==undefined)),bankRequest=bankSet&&!hrCanRates(s,k.uid);
    if(bankSet&&existing&&hrPendingRequests(s,'bank').some(r=>r.staffId===existing.id))
      fail('INVALID_TRANSITION',`A bank-detail change for ${existing.name} is already waiting for the Owner. Withdraw or decide it first.`);
    assert(String(cmd.bankReason??'').trim().length<=200,'Reason for the bank change: up to 200 characters.');
    const bankBefore:HRBank={bankName:existing?.bankName??'',accountNo:existing?.accountNo??'',ifsc:existing?.ifsc??''};
    const bankAfter:HRBank=Object.fromEntries(BANK.map(f=>[f,cmd[f]!==undefined?norm(cmd[f])??'':bankBefore[f]])) as HRBank;
    const bankWaits=(staffId:string)=>{if(!bankRequest)return '';const id=k.id('HR-REQ');
      x.requests=[...(x.requests??[]),{id,kind:'bank',staffId,bankBefore,bankAfter,reason:String(cmd.bankReason??'').trim()||'Bank details changed in the staff master',
        status:'Pending',requestedBy:k.uid,requestedAt:s.clock}];
      return ` · bank ${bankText(bankBefore)} → ${bankText(bankAfter)} sent to the Owner for approval (${id}; payments keep the current details until approved)`;};
    // F-11: the PT slab (gender) and the payslip WhatsApp number of an existing record are payroll fields (Owner / Accountant).
    const phone=cmd.phone===undefined?existing?.phone??'':String(cmd.phone).trim();
    const genderChanged=!!existing&&cmd.gender!==undefined&&cmd.gender!==hrGenderOf(existing),phoneChanged=!!existing&&phone!==existing.phone;
    if((genderChanged||phoneChanged)&&!pay)fail('FORBIDDEN',`Only the Owner or Accountant can change the ${genderChanged?'professional-tax slab':'payslip mobile number'} of a staff record.`);
    const base={name:String(cmd.name).trim(),dept:cmd.dept,designation:desig,...(desigCode?{designationCode:desigCode}:{}),...(cmd.userId?{userId:cmd.userId as string}:{}),
      defaultShift:cmd.defaultShift,weeklyOff:cmd.weeklyOff,phone,...(cmd.gender?{gender:cmd.gender as 'M'|'F'}:{})};
    const stat=(st:HRStaff)=>{if(!pay)return;
      if(cmd.structure!==undefined)st.structure=cmd.structure;if(cmd.pfApplicable!==undefined)st.pfApplicable=!!cmd.pfApplicable;if(cmd.esicApplicable!==undefined)st.esicApplicable=!!cmd.esicApplicable;
      if(!bankRequest)for(const f of BANK)if(cmd[f]!==undefined){const v=String(cmd[f]).trim();if(v)st[f]=v;else delete st[f];}};
    if(existing){const notes:string[]=[],was={...existing};
      if(!cmd.userId)delete existing.userId;Object.assign(existing,base);if(pay&&salaryGiven){existing.payType=cmd.payType??existing.payType;existing.monthly=cmd.monthly??existing.monthly;existing.daily=cmd.daily??existing.daily;}
      stat(existing);
      if(!desigCode&&was.designation!==existing.designation)delete existing.designationCode;
      if(was.designation!==existing.designation)notes.push(`designation ${was.designation} → ${existing.designation}`);
      if(was.defaultShift!==existing.defaultShift)notes.push(`default shift ${was.defaultShift} → ${existing.defaultShift}`);
      if(linkChanged)notes.push(`login ${was.userId??'(none)'} → ${existing.userId??'(none)'}`);
      if(genderChanged)notes.push(`PT slab ${hrGenderOf(was)==='F'?'women':'men'} → ${hrGenderOf(existing)==='F'?'women':'men'}`);
      if(phoneChanged)notes.push(`payslip mobile ${was.phone?'••••'+was.phone.replace(/\D/g,'').slice(-4):'(none)'} → ${existing.phone?'••••'+existing.phone.replace(/\D/g,'').slice(-4):'(none)'}`);
      // F-02: audit old → new bank details (masked); locked runs keep the details frozen at lock. DEC-084: the Owner's change applies here.
      if(bankChanged&&!bankRequest)notes.push(`bank ${was.bankName??'(none)'} ${hrMaskAccount(was.accountNo)} ${was.ifsc??''} → ${existing.bankName??'(none)'} ${hrMaskAccount(existing.accountNo)} ${existing.ifsc??''} (Owner; future runs only; locked runs keep their frozen details)`);
      return `${existing.id} · ${existing.name} updated${notes.length?' · '+notes.join(' · '):''}${bankWaits(existing.id)}`;}
    const st:HRStaff={id:k.id('HR-S'),...base,payType:pay&&cmd.payType?cmd.payType:'monthly',monthly:pay?cmd.monthly??0:0,daily:pay?cmd.daily??0:0,joined:s.date,active:true};
    stat(st);x.staff.push(st);return `${st.id} · ${st.name} added${st.userId?` · login ${st.userId}`:''}${pay?'':' (salary to be set by payroll)'}${bankWaits(st.id)}`;
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
    assert(cmd.shift==='OFF'||hrShiftChoices(s,hrShiftFor(s,st,cmd.date)).some(p=>p.code===cmd.shift),'Choose a shift.');
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
      const status:HRAttStatus=leave?'H':hrCheckinStatus(s.clock,shift,s);
      x.att[key]={status,at:s.clock,otMin:0,by:k.uid,note:leave?'Self check-in · half-day leave':'Self check-in'};
      return `${st.name} · checked in ${s.clock.slice(11)} · ${HR_ATT_LABEL[status]}`;
    }
    if(!mgr)fail('FORBIDDEN','Access denied: approve permission is required to record attendance for staff.');
    notSelf(k,st,'record or correct attendance or overtime (use self check-in)');
    assert((HR_ATT_MARKS as string[]).includes(cmd.status),'Choose an attendance status.');
    const ot=cmd.otMin??0;assert(Number.isInteger(ot)&&ot>=0&&ot<=480,'Overtime must be 0–480 minutes.');
    assert(!(cmd.status==='A'&&ot>0),'An absent day cannot carry overtime.');
    const prev=x.att[key];
    const note=String(cmd.note??'').trim();x.att[key]={status:cmd.status as HRAttStatus,at:s.clock,otMin:ot,by:k.uid,...(note?{note}:{})};
    return `${st.name} · ${s.date} · ${HR_ATT_LABEL[cmd.status as HRAttStatus]}${ot?` · OT ${ot} min`:''}${prev?' (corrected)':''}`;
  },
  /** DEC-075 (audit A04-02): correct a PAST day of the open pay month — mark (P/L/H/A/OFF + overtime), a full-day leave (back-dated leave) or
   *  'none'. A correction that raises the month's gross pay waits for the Owner (HR_REQUEST_DECIDE); one that lowers or keeps pay — or any by
   *  the Owner — applies at once and recalculates the month's draft pay-run. Never your own record; a locked month never changes. */
  HR_ATT_CORRECT:k=>{
    manage(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),st=staffOf(k,cmd.staffId);
    assert(DATE.test(String(cmd.date??'')),'Choose the day to correct.');
    const target=hrParseEntry(cmd.mark,cmd.otMin);if(typeof target==='string')fail('VALIDATION',target);
    const reason=String(cmd.reason??'').trim();assert(reason.length>=3&&reason.length<=200,'Give the reason for the correction (3–200 characters).');
    const when=hrCorrectionDateRefusal(s,cmd.date);if(when)fail(when.code,when.msg);
    notSelf(k,st,'correct attendance');
    if(hrPendingRequests(s,'attendance').some(r=>r.staffId===st.id&&r.date===cmd.date))
      fail('NOT_AVAILABLE',`A correction of ${st.name} on ${dm(cmd.date)} is already waiting for the Owner. Withdraw or decide it first.`);
    const entry=target as HRDayEntry,p=hrPlanCorrection(s,st,cmd.date,entry);if('refusal' in p)fail(p.refusal.code,p.refusal.msg);
    const {day,effect}=p as Exclude<typeof p,{refusal:unknown}>,direct=effect<=0||hrCanRates(s,k.uid),id=k.id('HR-REQ');
    const what=`${st.name} · ${cmd.date} · ${hrEntryLabel(day.entry)} → ${hrEntryLabel(entry)}`;
    const req:HRRequest={id,kind:'attendance',staffId:st.id,date:cmd.date,before:day.entry,after:entry,payEffect:effect,reason,status:direct?'Approved':'Pending',
      ...(direct?{direct:true,decidedBy:k.uid,decidedAt:s.clock}:{}),requestedBy:k.uid,requestedAt:s.clock};
    x.requests=[...(x.requests??[]),req];
    if(!direct)return `${id} · ${what} · raises gross pay by ${money(effect)} — waiting for the Owner · reason: ${reason}`;
    const redo=applyCorrection(k,st,cmd.date,day,entry,id,reason,k.uid,k.uid);
    return `${id} · ${what} · ${effect<0?'lowers gross pay by '+money(-effect):effect===0?'gross pay unchanged':'raises gross pay by '+money(effect)+' (Owner, applied directly)'} · reason: ${reason}${redo?' · '+redo:''}`;
  },
  /** The Owner approves or rejects a pending request (attendance correction or bank-detail change). Never the requester; never your own record. */
  HR_REQUEST_DECIDE:k=>{
    owner(k,'decide payroll requests');const {s,cmd,assert,fail}=k,x=hrEnsure(s),r=(x.requests??[]).find(q=>q.id===cmd.id);
    assert(r,'Request not found.','NOT_FOUND');
    const note=String(cmd.note??'').trim();if(!cmd.approve)assert(note.length>=3,'Give the reason for rejecting (at least 3 characters).');
    assert(note.length<=200,'Note: up to 200 characters.');
    assert(r!.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
    if(r!.requestedBy===k.uid)fail('FORBIDDEN','You cannot decide your own request (no self-approval).');
    const st=staffOf(k,r!.staffId);if(isOwn(k,st))fail('FORBIDDEN','You cannot decide a request about your own staff record.');
    const what=r!.kind==='bank'?`${st.name} · bank ${bankText(r!.bankBefore!)} → ${bankText(r!.bankAfter!)}`:`${st.name} · ${r!.date} · ${hrEntryLabel(r!.before!)} → ${hrEntryLabel(r!.after!)}`;
    const close=(status:'Approved'|'Rejected')=>{r!.status=status;r!.decidedBy=k.uid;r!.decidedAt=s.clock;if(note)r!.note=note;};
    if(!cmd.approve){close('Rejected');return `${r!.id} · ${what} · rejected · ${note}`;}
    let redo='';
    if(r!.kind==='bank'){
      const now:HRBank={bankName:st.bankName??'',accountNo:st.accountNo??'',ifsc:st.ifsc??''},b=r!.bankBefore!;
      assert(now.bankName===b.bankName&&now.accountNo===b.accountNo&&now.ifsc===b.ifsc,`${st.name}’s bank details changed after this request. Reject it and enter the change again.`,'STALE_VERSION');
      for(const f of ['bankName','accountNo','ifsc'] as const){const v=r!.bankAfter![f];if(v)st[f]=v;else delete st[f];}
    }else{
      const when=hrCorrectionDateRefusal(s,r!.date!);if(when)fail(when.code,when.msg);
      const p=hrPlanCorrection(s,st,r!.date!,r!.after!);if('refusal' in p)fail(p.refusal.code,p.refusal.msg);
      const {day}=p as Exclude<typeof p,{refusal:unknown}>;
      assert(hrSameEntry(day.entry,r!.before!),`${st.name}’s ${dm(r!.date!)} changed after this request (now ${hrEntryLabel(day.entry).toLowerCase()}). Reject it; the day can be corrected again.`,'STALE_VERSION');
      redo=applyCorrection(k,st,r!.date!,day,r!.after!,r!.id,r!.reason,r!.requestedBy,k.uid);
    }
    close('Approved');
    return `${r!.id} · ${what} · approved${r!.kind==='bank'?' (future runs only; locked runs keep their frozen details)':` · raises gross pay by ${money(r!.payEffect??0)}`}${note?' · '+note:''}${redo?' · '+redo:''}`;
  },
  /** The requester withdraws their own pending request. */
  HR_REQUEST_WITHDRAW:k=>{
    const {s,cmd,assert,fail}=k,x=hrEnsure(s);
    if(!hrCanManage(s,k.uid)&&!hrCanPay(s,k.uid))fail('FORBIDDEN','Access denied: only managers or payroll roles make and withdraw payroll requests.');
    const r=(x.requests??[]).find(q=>q.id===cmd.id);assert(r,'Request not found.','NOT_FOUND');
    assert(r!.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
    if(r!.requestedBy!==k.uid)fail('FORBIDDEN','Only the person who made the request can withdraw it.');
    r!.status='Withdrawn';r!.decidedBy=k.uid;r!.decidedAt=s.clock;
    return `${r!.id} · ${staffOf(k,r!.staffId).name} · ${r!.kind==='bank'?'bank-detail change':'attendance correction '+r!.date} · withdrawn`;
  },
  /** DEC-083 (audit A04-05): add or rename a paid holiday (Owner). Paid like a weekly off; a locked pay month keeps its holidays. */
  // DEC-083 paid holidays, written to MA-6's Holiday calendar master (masters integration: one holidays concept). SAVE makes the date an
  // active paid holiday (a restricted or retired row of that date becomes paid); REMOVE deletes the paid row. A locked pay month never changes.
  HR_HOLIDAY_SAVE:k=>{
    owner(k,'keep the paid-holiday calendar');const {s,cmd,assert}=k,x=hrEnsure(s);
    assert(DATE.test(String(cmd.date??'')),'Choose the holiday’s date.');
    const name=String(cmd.name??'').trim();assert(name.length>=2&&name.length<=60,'Name the holiday (2–60 characters).');
    const ym=String(cmd.date).slice(0,7),run=x.runs.find(r=>r.month===ym);
    assert(run?.status!=='Locked',`${hrMonthLabel(ym)} payroll is locked — its holidays cannot change.`,'INVALID_TRANSITION');
    const rows=holidayRows(k),i=rows.findIndex(r=>r.code===cmd.date),cur=i>=0?rows[i]:undefined;
    const old=cur&&cur.active&&cur.kind==='paid'?cur:undefined;
    const next:MasterRow={...(cur??{id:k.id(mstSpec(HRM_HOLIDAYS)?.prefix??'HOL'),code:String(cmd.date),sortOrder:mstNextSort(rows)}),name,kind:'paid',active:true};
    delete next.retiredOn;
    holidayWrite(k,i>=0?rows.map((r,j)=>j===i?next:r):[...rows,next],next,cur);
    return `Paid holiday ${cmd.date} · ${name}${!old?' added':old.name===name?' (unchanged)':` (was ${old.name})`} · paid like a weekly off${run?` · ${hrMonthLabel(ym)} draft pay-run must be recalculated`:''}`;
  },
  HR_HOLIDAY_REMOVE:k=>{
    owner(k,'keep the paid-holiday calendar');const {s,cmd,assert}=k,x=hrEnsure(s);
    const ym=String(cmd.date??'').slice(0,7),run=x.runs.find(r=>r.month===ym);
    assert(run?.status!=='Locked',`${hrMonthLabel(ym)} payroll is locked — its holidays cannot change.`,'INVALID_TRANSITION');
    const rows=holidayRows(k),old=rows.find(r=>r.code===cmd.date&&r.active&&r.kind==='paid');assert(old,'No paid holiday on that date.','NOT_FOUND');
    holidayWrite(k,rows.filter(r=>r!==old),undefined,old);
    return `Paid holiday ${cmd.date} · ${old!.name} removed${run?` · ${hrMonthLabel(ym)} draft pay-run must be recalculated`:''}`;
  },
  /** SCC submitLeave (L1321-1407): one PENDING record per date of from…to — a week-off date is skipped, then a duplicate (same person, not rejected,
   *  same half or either a full day: AM + PM coexist), then a date that is full (3 records per day for the hotel, 4 per shift); the other dates are
   *  still recorded. The approver (Owner or a Manager) is required; the reason is optional (≤ 500). A tracked category over its quota needs
   *  confirmOverBalance (SCC confirm); a hard blackout refuses; a soft one (or a store holiday) needs confirmBlackout. Nothing added = refused with
   *  SCC's counts. HCC: own leave, or a manager for anyone (DEC-058); not before the business date (past days: DEC-075 corrections). */
  HR_LEAVE_REQUEST:k=>{
    const {s,cmd,assert,fail}=k,x=hrEnsure(s),st=staffOf(k,cmd.staffId);
    if(!isOwn(k,st)&&!hrCanManage(s,k.uid))fail('FORBIDDEN','You can request leave only for yourself.');
    assert(st.active,'This staff member is inactive.','INVALID_TRANSITION');
    const approver=String(cmd.approver??'').trim();
    assert(approver,'Approver name is required');
    assert(hrApproverChoices(s).some(a=>a.id===approver),'Choose the approver from the list (the Owner or a Manager).');
    assert(!(st.userId&&st.userId===approver),'The approver cannot be the person on leave — nobody approves their own leave (DEC-119).');
    const from=String(cmd.from??''),half=cmd.half==='AM'||cmd.half==='PM'?cmd.half as HRHalf:undefined,to=String(cmd.to??'')||from;
    if(cmd.half!==undefined&&cmd.half!==null)assert(half,'Invalid leave type');
    assert(DATE.test(from)&&DATE.test(to),'Please select leave dates');
    assert(from<=to,'Leave From cannot be after Leave To');
    const type=String(cmd.leaveType??'') as HRLeaveType;assert((HR_LEAVE_TYPES as string[]).includes(type),'Choose a leave category.');
    assert(from>=s.date,'Leave cannot start before the business date. A past day of the open pay month is corrected on Attendance › Past days (DEC-075).');
    const reason=String(cmd.reason??'').trim();assert(reason.length<=500,'Reason / notes: up to 500 characters.');
    const over=hrLeaveOverBalance(s,st,type,from,to,half);
    if(over&&!cmd.confirmOverBalance)fail('INVALID_TRANSITION',over);
    const bk=hrBlackoutHits(s,from,to,st.dept);
    if(bk.hard)fail('NOT_AVAILABLE',hrLeaveHardBlackoutText(bk.labels));
    if(bk.hits.length&&!cmd.confirmBlackout)fail('INVALID_TRANSITION',hrLeaveSoftBlackoutText(bk.labels,hrLeaveCategory(type)));
    const plan=hrLeaveRequestPlan(s,st.id,from,to,half,x.leaves),toast=hrLeaveRequestToast(plan);
    if(!toast.ok)fail('NOT_AVAILABLE',toast.text);
    const base=k.id('HR-LV');let n=0;
    for(const d of plan.dates)if(d.result==='added')x.leaves.push({id:n++?`${base}-${n}`:base,staffId:st.id,date:d.date,type,from,to,days:half?0.5:1,reason,...(half?{half}:{}),
      approver,status:'Pending',requestedBy:k.uid,requestedAt:s.clock});
    return `${base} · ${st.name} · ${hrLeaveName(s,type)} · ${half?hrKindLabel({half}):'Full Day'} · ${from}${to!==from?' – '+to:''} · ${toast.text}${over?' · over the quota (confirmed)':''}${bk.hits.length?' · blackout '+bk.labels+' (confirmed)':''}`;
  },
  /** SCC decideLeave (L280-296): one date record; pending only. No balance check again (SCC). HCC keeps: a manager decides (DEC-058), nobody decides
   *  their own leave or their own request (DEC-119), an approved full day cannot sit on recorded attendance. The deciding user is recorded (audit). */
  HR_LEAVE_DECIDE:k=>{
    manage(k);const {s,cmd,assert,fail}=k,x=hrEnsure(s),l=x.leaves.find(l=>l.id===cmd.id);
    assert(l,'Leave record not found.','NOT_FOUND');
    assert(l!.status==='Pending',`This leave is already ${l!.status.toLowerCase()}`,'INVALID_TRANSITION');
    if(l!.requestedBy===k.uid||hrStaffOfUser(s,k.uid)?.id===l!.staffId)fail('FORBIDDEN','You cannot approve your own request. Switch to another authorized demo user.');
    const note=String(cmd.note??'').trim();assert(note.length<=200,'Note: up to 200 characters.');
    if(cmd.approve){const a=x.att[hrKey(l!.date,l!.staffId)];
      assert(!a||(!!l!.half&&(a.status==='H'||a.status==='A')),'Attendance is already recorded on this date.','INVALID_TRANSITION');}
    l!.status=cmd.approve?'Approved':'Rejected';l!.decidedBy=k.uid;l!.decidedAt=s.clock;if(note)l!.note=note;
    const st=x.staff.find(t=>t.id===l!.staffId);
    return `${l!.id} · ${st?.name??l!.staffId} · ${l!.date} · ${hrLeaveName(s,l!.type)} ${hrKindLabel(l!)} · ${cmd.approve?'Leave approved — now live for floor gate & payroll':'Leave rejected — kept for audit'}${note?' · '+note:''}`;
  },
  /** SCC removeLeave (L1452) = cancel with an audit record (DEC-119: no hard delete; the record leaves the calendar, counts and files). Pending: the
   *  requester, the person on leave or a manager; approved or rejected: a manager; an approved day before the business date is changed through a
   *  DEC-075 correction instead. */
  HR_LEAVE_CANCEL:k=>{
    const {s,cmd,assert,fail}=k,l=hrEnsure(s).leaves.find(l=>l.id===cmd.id);assert(l,'Leave record not found.','NOT_FOUND');
    assert(l!.status!=='Cancelled','This leave record is already removed (kept for audit).','INVALID_TRANSITION');
    const own=l!.requestedBy===k.uid||hrStaffOfUser(s,k.uid)?.id===l!.staffId;
    if(l!.status==='Pending'){if(!own&&!hrCanManage(s,k.uid))fail('FORBIDDEN','Only the requester or a manager can cancel this request.');}
    else{manage(k);
      if(l!.status==='Approved')assert(l!.date>=s.date,'An approved leave day before the business date is changed with an attendance correction (Attendance › Past days, DEC-075).','INVALID_TRANSITION');}
    const was=l!.status;l!.status='Cancelled';l!.cancelledBy=k.uid;l!.cancelledAt=s.clock;
    return `${l!.id} · ${l!.date} · ${was} leave removed (cancelled — kept for audit)`;
  },
  /** SCC leave settings (Owner, DEC-058): entitlement defaults (any number ≥ 0, fractions, no maximum; SCC: a blank field saves 0), the week-off set,
   *  advisory department caps (SCC per-store caps: whole number ≥ 0; a blank field keeps the old value). New records only. */
  HR_LEAVE_POLICY_SET:k=>{
    owner(k,'change the leave policy');const {s,cmd,assert}=k,x=hrEnsure(s),p=clone(hrLeavePolicy(s));const notes:string[]=[];
    if(cmd.ent!==undefined){for(const t of HR_TRACKED){const raw=cmd.ent?.[t],v=raw===''||raw===null||raw===undefined?0:Number(raw);
        assert(Number.isFinite(v)&&v>=0,`Entitlement for ${t}: a number of days, 0 or more.`);p.ent[t]=Math.max(0,Math.round(v*100)/100);}
      notes.push(`CL ${p.ent.CL} · SL ${p.ent.SL} · EL ${p.ent.EL}`);}
    if(cmd.weekOff!==undefined){assert(Array.isArray(cmd.weekOff),'Week-off days are a list of weekdays.');
      p.weekOff=[...new Set((cmd.weekOff as unknown[]).map(Number).filter(n=>Number.isInteger(n)&&n>=0&&n<=6))].sort();
      notes.push(`week-off ${p.weekOff.length?p.weekOff.map(d=>['Sun','Mon','Tue','Wed','Thu','Fri','Sat'][d]).join(' + '):'none'}`);}
    if(cmd.deptCap!==undefined){assert(cmd.deptCap&&typeof cmd.deptCap==='object','Department caps are missing.');
      for(const [d,v] of Object.entries(cmd.deptCap as Record<string,unknown>)){assert(x.depts.some(q=>q.id===d),'Unknown department.');
        if(v===null||v===undefined||v==='')continue;// SCC: a blank field keeps the cap
        const n=Math.floor(Number(v));assert(Number.isFinite(n)&&n>=0,'Department cap: a whole number, 0 or more.');p.deptCap[d]=n;}
      notes.push('department caps (advisory) saved');}
    delete p.defaultCap;
    if(cmd.ent!==undefined)writeQuotas(k,p.ent);
    x.leavePolicy=p;return `Leave settings updated · ${notes.join(' · ')||'no change'} · new leave only`;
  },
  /** SCC saveEntOverride (L478-493): a staff member's own CL / SL / EL quota; a blank field uses the default; all blank removes the override. Owner. */
  HR_LEAVE_ENT_OVERRIDE:k=>{
    owner(k,'change leave entitlements');const {s,cmd,assert}=k,x=hrEnsure(s),st=staffOf(k,cmd.staffId),p=clone(hrLeavePolicy(s)),ov:HREnt={};
    for(const t of HR_TRACKED){const raw=cmd.ent?.[t];if(raw===''||raw===null||raw===undefined)continue;
      const v=Number(raw);assert(Number.isFinite(v)&&v>=0,`${t}: a number of days, 0 or more.`);ov[t]=Math.max(0,Math.round(v*100)/100);}
    const any=Object.keys(ov).length>0;p.perStaff={...(p.perStaff??{})};
    if(any)p.perStaff[st.id]=ov;else delete p.perStaff[st.id];
    x.leavePolicy=p;
    return any?`Entitlement override saved for ${st.name} · ${HR_TRACKED.filter(t=>ov[t]!=null).map(t=>`${t} ${ov[t]}`).join(' · ')}`:`${st.name} · Override cleared — using defaults`;
  },
  /** SCC addBlackout (L441-462): from / to (swapped when reversed), scope = every department or one, kind blackout / store holiday, hard (blackouts
   *  only), label ≤ 80. Owner. */
  HR_LEAVE_BLACKOUT_ADD:k=>{
    owner(k,'change leave blackouts');const {s,cmd,assert}=k,x=hrEnsure(s),p=clone(hrLeavePolicy(s));
    let from=String(cmd.from??''),to=String(cmd.to??'');assert(from&&to,'Pick both blackout dates');
    assert(DATE.test(from)&&DATE.test(to)&&!Number.isNaN(Date.parse(from))&&!Number.isNaN(Date.parse(to)),'Invalid blackout date');
    if(from>to)[from,to]=[to,from];
    const dept=String(cmd.dept??'');assert(dept===''||x.depts.some(d=>d.id===dept),'Unknown department.');
    const kind=cmd.kind==='holiday'?'holiday':'blackout',hard=kind!=='holiday'&&!!cmd.hard,label=String(cmd.label??'').trim().slice(0,80);
    const id=k.id('HR-BK');p.blackouts=[...(p.blackouts??[]),{id,from,to,dept,kind,hard,label}];x.leavePolicy=p;
    return `${id} · ${label||(kind==='holiday'?'Store holiday':'Blackout')} · ${from}${to!==from?' – '+to:''} · ${dept||'all departments'} · ${kind==='holiday'?'Store holiday':hard?'Blackout (hard block)':'Blackout (warn)'} · Blackout saved`;
  },
  HR_LEAVE_BLACKOUT_REMOVE:k=>{
    owner(k,'change leave blackouts');const {s,cmd,assert}=k,x=hrEnsure(s),p=clone(hrLeavePolicy(s)),b=(p.blackouts??[]).find(e=>e.id===cmd.id);
    assert(b,'Blackout not found.','NOT_FOUND');p.blackouts=(p.blackouts??[]).filter(e=>e.id!==cmd.id);x.leavePolicy=p;
    return `${b!.id} · ${b!.label||(b!.kind==='holiday'?'Store holiday':'Blackout')} ${b!.from}${b!.to!==b!.from?' – '+b!.to:''} · Blackout removed`;
  },
  /** SCC submitAgenda (L1427-1447): date and title required (title ≤ 200, description ≤ 1000), time HH:MM or blank, type meeting / agenda /
   *  reminder; kept sorted by time with blank times last. A manager (DEC-058). */
  HR_AGENDA_ADD:k=>{
    manage(k);const {s,cmd,assert}=k,x=hrEnsure(s);
    const date=String(cmd.date??''),title=String(cmd.title??'').trim(),time=String(cmd.time??'').trim(),type=String(cmd.agendaType??cmd.kind??'meeting');
    assert(DATE.test(date)&&title,'Date and title are required');
    assert(['meeting','agenda','reminder'].includes(type),'Invalid type');
    assert(title.length<=200,'Title: up to 200 characters.');assert(!time||/^([01]\d|2[0-3]):[0-5]\d$/.test(time),'Time: HH:MM (24-hour) or blank.');
    const description=String(cmd.description??'').trim();assert(description.length<=1000,'Description: up to 1000 characters.');
    const id=k.id('HR-AG');
    const all=[...(x.agendas??[]),{id,date,title,time,type:type as 'meeting'|'agenda'|'reminder',description,by:k.uid,at:s.clock}];
    // SCC: sorted by time within the date, blank times last ('99:99'); dates stay in insertion order of their first item.
    x.agendas=all.map((a,i)=>({a,i})).sort((p,q)=>p.a.date!==q.a.date?0:((p.a.time||'99:99')>(q.a.time||'99:99')?1:(p.a.time||'99:99')<(q.a.time||'99:99')?-1:p.i-q.i)).map(v=>v.a);
    return `${id} · ${date}${time?' '+time:''} · ${type} · ${title} · Agenda item added`;
  },
  HR_AGENDA_REMOVE:k=>{
    manage(k);const {cmd,assert}=k,x=hrEnsure(k.s),a=(x.agendas??[]).find(q=>q.id===cmd.id);assert(a,'Agenda item not found.','NOT_FOUND');
    x.agendas=(x.agendas??[]).filter(q=>q.id!==cmd.id);return `${a!.id} · ${a!.date} · ${a!.title} · Agenda removed`;
  },
  /** SCC importJSON merge (L1936-1962) of an SCC LeaveDesk backup (`data` = the parsed file): a record whose id is already on that date is ignored;
   *  the same staff member with a record not rejected on that date → skipped (any half — stricter than the screen, SCC; owner question); a full
   *  date → skipped. Entitlements are merged and the week-off set replaced when the file has them. HCC: a record in a locked pay month is skipped
   *  (a locked month never changes). Owner. */
  HR_LEAVE_IMPORT:k=>{
    owner(k,'import leave data');const {s,cmd,fail}=k,x=hrEnsure(s);
    const plan=hrLeaveImportParse(s,cmd.data);if(typeof plan==='string')fail('VALIDATION','Import failed: '+plan);
    const pl=plan as Exclude<typeof plan,string>,locked=new Set(x.runs.filter(r=>r.status==='Locked').map(r=>r.month));
    let lAdded=0,lSkipped=0,aAdded=0;const base=k.id('HR-LV');
    for(const r of pl.leaves){
      const day=x.leaves.filter(l=>l.date===r.date);
      if(r.sccId&&day.some(l=>l.status!=='Cancelled'&&(l.sccId===r.sccId||l.id===r.sccId)))continue;// SCC: same id on that date → ignored (a cancelled record is SCC's removed one)
      if(locked.has(r.date.slice(0,7))){lSkipped++;continue;}
      if(day.some(l=>hrLeaveActive(l)&&l.staffId===r.staffId)){lSkipped++;continue;}
      if(!hrCanAddLeave(s,r.date,r.half,day)){lSkipped++;continue;}
      x.leaves.push({id:`${base}-${++lAdded}`,staffId:r.staffId,date:r.date,type:r.type,from:r.from,to:r.to,days:r.half?0.5:1,reason:r.reason,...(r.half?{half:r.half}:{}),
        ...(r.approver?{approver:r.approver}:{approverName:r.approverName}),status:r.status,requestedBy:k.uid,requestedAt:s.clock,...(r.legacy?{legacy:true}:{}),
        ...(r.sccId?{sccId:r.sccId}:{}),...(r.decidedByName?{decidedByName:r.decidedByName}:{}),...(r.decidedAt?{decidedAt:r.decidedAt}:{})});
    }
    for(const a of pl.agendas){const ag=x.agendas??[];if(a.sccId&&ag.some(q=>q.date===a.date&&q.id===a.sccId))continue;
      x.agendas=[...ag,{id:a.sccId||k.id('HR-AG'),date:a.date,title:a.title,time:a.time,type:a.type,description:a.description,by:k.uid,at:s.clock}];aAdded++;}
    const p=clone(hrLeavePolicy(s));
    if(pl.ent){p.ent={...p.ent,...pl.ent.defaults} as typeof p.ent;p.perStaff={...(p.perStaff??{}),...pl.ent.perStaff};writeQuotas(k,p.ent);}
    if(pl.weekOff)p.weekOff=pl.weekOff;
    delete p.defaultCap;x.leavePolicy=p;
    return `Imported: ${lAdded} leaves, ${aAdded} agendas. Skipped: ${pl.skipped+lSkipped}`;
  },
  /** SCC executeClearData (L2012-2026): type DELETE. HCC (DEC-119, no hard delete): every leave record not already cancelled is CANCELLED and kept
   *  for audit — except in a locked pay month, which never changes; agenda items are deleted; entitlement defaults, overrides and the week-off set
   *  go back to the defaults. Department caps and blackouts survive (SCC; owner question). HCC's staff master is not touched. Owner. */
  HR_LEAVE_CLEAR:k=>{
    owner(k,'clear the leave data');const {s,cmd,assert}=k,x=hrEnsure(s);
    assert(String(cmd.confirm??'').trim()==='DELETE','Type DELETE (uppercase) to confirm');
    const locked=new Set(x.runs.filter(r=>r.status==='Locked').map(r=>r.month));let n=0,kept=0;
    for(const l of x.leaves){if(l.status==='Cancelled')continue;if(locked.has(l.date.slice(0,7))){kept++;continue;}
      l.status='Cancelled';l.cancelledBy=k.uid;l.cancelledAt=s.clock;l.note='Clear All';n++;}
    const ag=(x.agendas??[]).length;x.agendas=[];
    const p=clone(hrLeavePolicy(s));p.ent={...HR_ENT_DEFAULTS};delete p.perStaff;delete p.weekOff;delete p.defaultCap;x.leavePolicy=p;writeQuotas(k,p.ent);
    return `All data cleared · ${n} leave record(s) cancelled (kept for audit)${kept?` · ${kept} in locked pay months kept`:''} · ${ag} agenda item(s) removed · entitlements and week-off back to the defaults`;
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
    // DEC-075: a correction of this month waiting for the Owner is decided before the month is locked (a locked month never changes).
    const waiting=hrPendingRequests(s,'attendance').filter(r=>r.date?.startsWith(run!.month)).length;
    assert(!waiting,`${waiting} attendance correction${waiting===1?'':'s'} for ${hrMonthLabel(run!.month)} ${waiting===1?'waits':'wait'} for the Owner — approve or reject ${waiting===1?'it':'them'} before locking.`,'INVALID_TRANSITION');
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
    return `${id} · ${st.name} · full and final DRAFT · ${c.net<0?'recoverable from employee '+money(-c.net):'net payable '+money(c.net)}${c.advanceLeftOpen>0?` · advance ${money(c.advanceLeftOpen)} not covered, stays open`:''} · to be confirmed by ${adds>HR_FNF_OWNER_ABOVE?'the Owner (additions above '+money(HR_FNF_OWNER_ABOVE)+')':'the other payroll user'}`;
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
    // DEC-074 / A04-06: only the part of the advance the settlement covers is recovered (oldest advance first); the rest stays open.
    let left=c.advanceRecovered;
    for(const a of x.advances.filter(a=>a.staffId===st.id&&hrAdvanceBalance(a)>0)){const b=Math.min(left,hrAdvanceBalance(a));if(b<=0)break;a.recovered+=b;a.recoveries.push({runId:f!.id,amount:b});left-=b;}
    f!.status='Settled';f!.confirmedBy=k.uid;f!.confirmedAt=s.clock;
    return `${f!.id} · ${f!.name} · full and final confirmed · ${f!.net<0?'recoverable from employee '+money(-f!.net):'net payable '+money(f!.net)} (simulated transfer)`
      +(c.advanceOutstanding>0?` · advance ${money(c.advanceRecovered)} recovered${c.advanceLeftOpen>0?`, ${money(c.advanceLeftOpen)} left open (still owed)`:''}`:'');
  },
  /** F-12: discard a draft settlement (payroll roles). */
  HR_FNF_DISCARD:k=>{
    payroll(k);const {cmd,assert}=k,x=hrEnsure(k.s),f=(x.fnf??[]).find(f=>f.id===cmd.id);
    assert(f,'Settlement not found.','NOT_FOUND');assert(f!.status==='Draft','A confirmed settlement cannot be discarded.','INVALID_TRANSITION');
    x.fnf=(x.fnf??[]).filter(q=>q.id!==f!.id);return `${f!.id} · ${f!.name} · draft settlement discarded`;
  },
};
