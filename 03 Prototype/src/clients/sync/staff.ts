// Staff, payroll and leave area (W4-6) — module 17 screens (Staff & payroll 'staff', My shifts & pay 'my-hr') on the server.
// Server: Payroll module (08 Production/src/Hcc.Api/Endpoints/PayrollEndpoints.cs, contracts Hcc.Contracts/Payroll).
//
//   loaders   staffX ← GET /payroll/staff (master; salaries / bank only for PERM-HR-005, own pay for its owner — DEC-061; phone and
//             account number arrive MASKED and stay masked) + GET /payroll/board (W4-6 read model: departments, leave policy, roster,
//             attendance with who/when/note, leaves, and — PERM-HR-006 only — rate versions, runs with the STORED lines, payslip sends,
//             bank files, unlocks, advances, full and final). Other staff get only their own line of each LOCKED run (own pay only).
//             Payroll figures are the server's stored lines, never recomputed here. Lines and rates are built in the prototype's own
//             key order (hrComputeLine / hrDefaultRates), so the screen's "changed after this draft" check compares like with like.
//   commands  HR_* → SaveStaff, SetStaffActive, SetRoster, CopyRosterWeek, MarkAttendance, RequestLeave, DecideLeave, CancelLeave,
//             SetLeavePolicy (+ SaveDepartment per changed department limit), GiveAdvance, SetStatutoryRates, PreparePayRun, LockPayRun,
//             UnlockPayRun, IssueBankFile, SendPayslip, PrepareFnf, ConfirmFnf, DiscardFnf. Versioned commands send the version loaded
//             with the record (STALE_VERSION when someone changed it meanwhile). Every command reloads staffX.
import type {Command} from '../../services/mock';
import type {HRState,HRStaff,HRRates,HRLine,HRCounts,HRRun,HRLeave,HRAdvance,HRFnf,HRAtt,HRShiftCode,HRSend,HRBankFile} from '../../domain/streams/staff';
import {HccClientError} from '../errors';
import type {SyncArea,CommandCtx} from './types';
import {protoId,serverId,utcToClock} from './map';

// ── Server DTOs (Hcc.Contracts.Payroll) ──
export type SrvStaffView={staffId:number;name:string;dept:string;designation:string;defaultShift:string;weeklyOff:number;joined:string;exitedOn:string|null;
  active:boolean;linked:boolean;phoneMasked:string;version:string;payType:string|null;monthlyPaise:number|null;dailyPaise:number|null;gender:string|null;
  structure:string|null;pfApplicable:boolean|null;esicApplicable:boolean|null;bankName:string|null;accountMasked:string|null;linkedUserId?:number|null};
export type SrvRates={from:string;basicBps:number;hraBps:number;washBps:number;pfEeBps:number;pfErBps:number;pfWageCap:number;esicEeBps:number;esicErBps:number;
  esicCeiling:number;ptMaleExempt:number;ptMaleMid:number;ptMaleMidAmt:number;ptStd:number;ptFeb:number;ptFemaleExempt:number;otBps:number;lateBlock:number;
  rounding:string;esicPeriod:boolean};
export type SrvCounts={ym:string;days:number;p:number;l:number;h:number;hl:number;a:number;offw:number;off:number;paidLeave:number;lopLeave:number;unmarked:number;na:number;otMin:number};
export type SrvLine={staffId:number;name:string;dept:string;designation:string;payType:string;structure:string;gender:string;basePaise:number;counts:SrvCounts;
  latePenaltyHalf:number;payableDays:number;earnedPaise:number;basicPaise:number;hraPaise:number;washingPaise:number;specialPaise:number;otPaise:number;grossPaise:number;
  pfWagesPaise:number;pfPaise:number;pfErPaise:number;esicWagesPaise:number;esicPaise:number;esicErPaise:number;ptPaise:number;advancePaise:number;
  deductionsPaise:number;netPaise:number;ctcPaise:number;esicCovered:boolean|null};
export type SrvRun={runId:number;month:string;status:'Draft'|'Locked';rates:SrvRates;warnings:string[];lockSeq:number;version:string;preparedBy:number;preparedUtc:string;
  lockedBy:number|null;lockedUtc:string|null;lines:SrvLine[];sends:{staffId:number;lockSeq:number;toMasked:string;sentBy:number;sentUtc:string}[];
  bankFiles:{issueNo:number;lockSeq:number;netPaise:number;issuedBy:number;issuedUtc:string}[];
  unlocks:{lockSeq:number;reason:string;nets:Record<string,number>;bankFiles:number;unlockedBy:number;unlockedUtc:string}[]};
export type SrvBoard={businessDate:string;from:string;to:string;allStaff:boolean;payroll:boolean;departments:{code:string;name:string;leaveCap:number|null}[];
  leavePolicy:{cl:number;sl:number;el:number;defaultCap:number};roster:{staffId:number;date:string;shift:string}[];
  attendance:{staffId:number;date:string;status:string;otMinutes:number;markedBy:number;markedUtc:string;note:string|null}[];
  leaves:{leaveId:number;staffId:number;leaveType:string;from:string;to:string;half:string|null;days:number;reason:string;status:string;requestedBy:number;
    requestedUtc:string;decidedBy:number|null;decidedUtc:string|null;note:string|null;version:string}[];
  rateVersions:SrvRates[];runs:SrvRun[];
  advances:{advanceId:number;staffId:number;amountPaise:number;installmentPaise:number;recoveredPaise:number;date:string;fromMonth:string;reason:string;givenBy:number;
    recoveries:{runId:number|null;fnfId:number|null;amountPaise:number}[]}[];
  fnf:{fnfId:number;staffId:number;lastDay:string;month:string;salaryIncluded:boolean;line:SrvLine|null;rates:SrvRates;advanceOutstandingPaise:number;
    adjustments:{label:string;kind:string;amountPaise:number}[];earningsPaise:number;recoveriesPaise:number;netPaise:number;status:string;preparedBy:number;
    preparedUtc:string;decidedBy:number|null;decidedUtc:string|null;version:string}[]};

// ── Mapping (server → prototype HRState) ──
const opt=<T,>(v:T|null|undefined)=>v===null||v===undefined?undefined:v;
/** Prototype HRRates in hrDefaultRates key order (JSON-comparable with hrRatesFor). */
export function ratesFromServer(r:SrvRates):HRRates{
  return {from:r.from,basicBps:r.basicBps,hraBps:r.hraBps,washBps:r.washBps,pfEeBps:r.pfEeBps,pfErBps:r.pfErBps,pfWageCap:r.pfWageCap,esicEeBps:r.esicEeBps,
    esicErBps:r.esicErBps,esicCeiling:r.esicCeiling,ptMaleExempt:r.ptMaleExempt,ptMaleMid:r.ptMaleMid,ptMaleMidAmt:r.ptMaleMidAmt,ptStd:r.ptStd,ptFeb:r.ptFeb,
    ptFemaleExempt:r.ptFemaleExempt,otBps:r.otBps,lateBlock:r.lateBlock,rounding:r.rounding==='rupee'?'rupee':'paise',esicPeriod:r.esicPeriod};
}
function countsFromServer(c:SrvCounts):HRCounts{
  return {ym:c.ym,days:c.days,P:c.p,L:c.l,H:c.h,HL:c.hl,A:c.a,OFFW:c.offw,off:c.off,paidLeave:c.paidLeave,lopLeave:c.lopLeave,unmarked:c.unmarked,na:c.na,otMin:c.otMin};
}
/** A stored server line → prototype HRLine (hrComputeLine key order); the figures are the server's, unchanged. */
export function lineFromServer(l:SrvLine):HRLine{
  return {staffId:protoId(l.staffId),name:l.name,dept:l.dept,designation:l.designation,payType:l.payType==='daily'?'daily':'monthly',structure:l.structure==='B'?'B':'S',
    gender:l.gender==='F'?'F':'M',base:l.basePaise,counts:countsFromServer(l.counts),latePenaltyHalf:l.latePenaltyHalf,payableDays:l.payableDays,earned:l.earnedPaise,
    basic:l.basicPaise,hra:l.hraPaise,washing:l.washingPaise,special:l.specialPaise,ot:l.otPaise,gross:l.grossPaise,pfWages:l.pfWagesPaise,pf:l.pfPaise,pfEr:l.pfErPaise,
    esicWages:l.esicWagesPaise,esic:l.esicPaise,esicEr:l.esicErPaise,pt:l.ptPaise,advance:l.advancePaise,deductions:l.deductionsPaise,net:l.netPaise,ctc:l.ctcPaise,
    ...(typeof l.esicCovered==='boolean'?{esicCovered:l.esicCovered}:{})};
}
export function staffFromServer(x:SrvStaffView):HRStaff{
  const st:HRStaff={id:protoId(x.staffId),name:x.name,dept:x.dept,designation:x.designation,
    ...(x.linkedUserId!=null?{userId:protoId(x.linkedUserId)}:{}),
    payType:x.payType==='daily'?'daily':'monthly',monthly:x.monthlyPaise??0,daily:x.dailyPaise??0,
    defaultShift:(['M','E','N','G'].includes(x.defaultShift)?x.defaultShift:'G') as HRStaff['defaultShift'],weeklyOff:x.weeklyOff,joined:x.joined,
    phone:x.phoneMasked==='(none)'?'':x.phoneMasked,active:x.active};
  if(x.exitedOn)st.exitedOn=x.exitedOn;
  if(x.gender==='M'||x.gender==='F')st.gender=x.gender;
  if(x.structure==='S'||x.structure==='B')st.structure=x.structure;
  if(x.pfApplicable!=null)st.pfApplicable=x.pfApplicable;
  if(x.esicApplicable!=null)st.esicApplicable=x.esicApplicable;
  if(x.bankName)st.bankName=x.bankName;
  if(x.accountMasked&&x.accountMasked!=='(none)')st.accountNo=x.accountMasked;   // masked (••••1234): never unmasked here
  return st;
}
function runFromServer(r:SrvRun):HRRun{
  const sent=(seq:number):HRSend[]=>r.sends.filter(x=>x.lockSeq===seq).map(x=>({staffId:protoId(x.staffId),to:x.toMasked,at:utcToClock(x.sentUtc),by:protoId(x.sentBy)}));
  const files=(seq:number):HRBankFile[]=>r.bankFiles.filter(x=>x.lockSeq===seq).map(x=>({at:utcToClock(x.issuedUtc),by:protoId(x.issuedBy),net:x.netPaise}));
  const run:HRRun={id:protoId(r.runId),month:r.month,status:r.status,rates:ratesFromServer(r.rates),lines:r.lines.map(lineFromServer),warnings:[...r.warnings],
    preparedBy:protoId(r.preparedBy),preparedAt:utcToClock(r.preparedUtc)};
  if(r.status==='Locked'){
    if(r.lockedBy!=null)run.lockedBy=protoId(r.lockedBy);
    if(r.lockedUtc)run.lockedAt=utcToClock(r.lockedUtc);
    const s=sent(r.lockSeq),f=files(r.lockSeq);if(s.length)run.sent=s;if(f.length)run.bankFiles=f;
  }
  if(r.unlocks.length)run.unlocks=r.unlocks.map(u=>{const s=sent(u.lockSeq),f=files(u.lockSeq);
    return {at:utcToClock(u.unlockedUtc),by:protoId(u.unlockedBy),reason:u.reason,...(s.length?{sent:s}:{}),...(f.length?{bankFiles:f}:{}),nets:{...u.nets}};});
  return run;
}

async function loadStaffX(c:Parameters<SyncArea['loaders'][number]['load']>[0]):Promise<HRState>{
  const [list,b]=await Promise.all([c.api.get<SrvStaffView[]>('/payroll/staff'),c.api.get<SrvBoard>('/payroll/board')]);
  const staff=list.map(staffFromServer);
  list.forEach(x=>c.ids.setVersion('staff',protoId(x.staffId),x.version));
  const roster:Record<string,HRShiftCode>={};
  for(const x of b.roster)roster[x.date+'|'+protoId(x.staffId)]=x.shift as HRShiftCode;
  const att:Record<string,HRAtt>={};
  for(const x of b.attendance)att[x.date+'|'+protoId(x.staffId)]={status:x.status as HRAtt['status'],at:utcToClock(x.markedUtc),otMin:x.otMinutes,by:protoId(x.markedBy),...(x.note?{note:x.note}:{})};
  const leaves:HRLeave[]=b.leaves.map(l=>{c.ids.setVersion('leave',protoId(l.leaveId),l.version);
    return {id:protoId(l.leaveId),staffId:protoId(l.staffId),type:l.leaveType as HRLeave['type'],from:l.from,to:l.to,days:l.days,reason:l.reason,
      ...(l.half==='AM'||l.half==='PM'?{half:l.half}:{}),status:l.status as HRLeave['status'],requestedBy:protoId(l.requestedBy),requestedAt:utcToClock(l.requestedUtc),
      ...(l.decidedBy!=null?{decidedBy:protoId(l.decidedBy)}:{}),...(l.decidedUtc?{decidedAt:utcToClock(l.decidedUtc)}:{}),...(l.note?{note:l.note}:{})};});
  const runs=b.runs.map(r=>{c.ids.setVersion('run',protoId(r.runId),r.version);return runFromServer(r);});
  const advances:HRAdvance[]=b.advances.map(a=>({id:protoId(a.advanceId),staffId:protoId(a.staffId),amount:a.amountPaise,installment:a.installmentPaise,
    recovered:a.recoveredPaise,date:a.date,reason:a.reason,by:protoId(a.givenBy),
    recoveries:a.recoveries.map(q=>({runId:protoId((q.runId??q.fnfId)!),amount:q.amountPaise})),fromMonth:a.fromMonth}));
  const name=(id:number)=>list.find(x=>x.staffId===id)?.name??protoId(id);
  const fnf:HRFnf[]=b.fnf.map(f=>{c.ids.setVersion('fnf',protoId(f.fnfId),f.version);
    return {id:protoId(f.fnfId),staffId:protoId(f.staffId),name:name(f.staffId),lastDay:f.lastDay,month:f.month,salaryIncluded:f.salaryIncluded,
      ...(f.line?{line:lineFromServer(f.line)}:{}),rates:ratesFromServer(f.rates),advanceOutstanding:f.advanceOutstandingPaise,
      adjustments:f.adjustments.map(a=>({label:a.label,kind:a.kind==='ded'?'ded':'add',amount:a.amountPaise})),earnings:f.earningsPaise,recoveries:f.recoveriesPaise,
      net:f.netPaise,by:protoId(f.preparedBy),at:utcToClock(f.preparedUtc),status:f.status==='Draft'?'Draft':'Settled',
      ...(f.decidedBy!=null&&f.status!=='Draft'?{confirmedBy:protoId(f.decidedBy)}:{}),...(f.decidedUtc&&f.status!=='Draft'?{confirmedAt:utcToClock(f.decidedUtc)}:{})};});
  const deptCap:Record<string,number>={};for(const d of b.departments)if(d.leaveCap!=null)deptCap[d.code]=d.leaveCap;
  return {depts:b.departments.map(d=>({id:d.code,name:d.name})),staff,roster,att,leaves,advances,rateVersions:b.rateVersions.map(ratesFromServer),
    leavePolicy:{ent:{CL:b.leavePolicy.cl,SL:b.leavePolicy.sl,EL:b.leavePolicy.el},defaultCap:b.leavePolicy.defaultCap,deptCap},runs,fnf};
}

// ── Command mapping ──
const RELOAD:['staffX']=['staffX'];
/** The version of a record as last loaded (required by the server for edits of staff, leave, runs and settlements). */
function version(c:CommandCtx,kind:string,id:unknown,what:string):string{
  const v=c.ids.version(kind,String(id??''));
  if(!v)throw new HccClientError('VALIDATION',`${what} ${String(id??'')} is not loaded from the server. Refresh and try again.`);
  return v;
}
const hr=(c:CommandCtx)=>c.state.staffX;
const loadedStaff=(c:CommandCtx,id:unknown)=>hr(c)?.staff.find(st=>st.id===String(id??''));
const text=(v:unknown)=>v===undefined||v===null?undefined:String(v);

/** HR_STAFF_SAVE → SaveStaff. Masked or hidden values the screen shows are never sent back: an unchanged masked phone / account
 *  number, a blank IFSC (not loaded) and a PT slab the caller cannot see are left out (= unchanged on the server). */
function saveStaffBody(c:CommandCtx):Record<string,unknown>{
  const cmd=c.cmd,old=cmd.id?loadedStaff(c,cmd.id):undefined;
  const body:Record<string,unknown>={staffId:cmd.id?serverId(cmd.id,'Staff'):null,name:cmd.name,dept:cmd.dept,designation:cmd.designation,
    defaultShift:cmd.defaultShift,weeklyOff:cmd.weeklyOff,linkUserId:cmd.userId?serverId(cmd.userId,'User'):null};
  if(cmd.confirmRelink)body.confirmRelink=true;
  if(cmd.phone!==undefined&&(!old||String(cmd.phone).trim()!==old.phone))body.phone=String(cmd.phone).trim();
  if(cmd.gender!==undefined&&(!old||old.gender!==undefined))body.gender=cmd.gender;
  if(cmd.payType!==undefined)body.payType=cmd.payType;
  if(cmd.monthly!==undefined)body.monthlyPaise=cmd.monthly;
  if(cmd.daily!==undefined)body.dailyPaise=cmd.daily;
  if(cmd.structure!==undefined)body.structure=cmd.structure;
  if(cmd.pfApplicable!==undefined)body.pfApplicable=!!cmd.pfApplicable;
  if(cmd.esicApplicable!==undefined)body.esicApplicable=!!cmd.esicApplicable;
  if(cmd.bankName!==undefined&&String(cmd.bankName).trim()!==(old?.bankName??''))body.bankName=String(cmd.bankName).trim();
  if(cmd.accountNo!==undefined&&String(cmd.accountNo).trim()!==(old?.accountNo??''))body.accountNo=String(cmd.accountNo).trim();
  if(cmd.ifsc!==undefined&&String(cmd.ifsc).trim())body.ifsc=String(cmd.ifsc).trim().toUpperCase();
  return body;
}

const ratesBody=(cmd:Command)=>{const b:Record<string,unknown>={effectiveFrom:cmd.from??null};
  for(const f of ['basicBps','hraBps','washBps','pfEeBps','pfErBps','pfWageCap','esicEeBps','esicErBps','esicCeiling','ptMaleExempt','ptMaleMid','ptMaleMidAmt','ptStd',
    'ptFeb','ptFemaleExempt','otBps','lateBlock','rounding','esicPeriod'])if(cmd[f]!==undefined)b[f]=cmd[f];
  return b;};
/** Sends one server command; `ver` = [kind, prototype id] of the record whose loaded version the server needs (expectedVersion). */
async function post(c:CommandCtx,name:string,body:Record<string,unknown>,ver?:[kind:string,id:unknown,what:string]):Promise<void>{
  await c.api.command(name,body,ver?{expectedVersion:version(c,ver[0],ver[1],ver[2])}:undefined);
}

export const staff:SyncArea={
  id:'staff',
  loaders:[{slice:'staffX',load:loadStaffX}],
  commands:[
    {type:'HR_STAFF_SAVE',reload:RELOAD,
      precheck:cmd=>String(cmd.name??'').trim().length<2?'Enter the staff member’s name.':undefined,
      async send(c){const r=await c.api.command<{staffId:number}>('SaveStaff',saveStaffBody(c),c.cmd.id?{expectedVersion:version(c,'staff',c.cmd.id,'Staff member')}:undefined);
        return {result:{staffId:protoId(r.staffId)}};}},
    {type:'HR_STAFF_ACTIVE',reload:RELOAD,send:c=>post(c,'SetStaffActive',({staffId:serverId(c.cmd.id,'Staff'),active:!!c.cmd.active}),['staff',c.cmd.id,'Staff member'])},
    {type:'HR_ROSTER_SET',reload:RELOAD,send:c=>post(c,'SetRoster',({staffId:serverId(c.cmd.staffId,'Staff'),date:c.cmd.date,shift:c.cmd.shift}))},
    {type:'HR_ROSTER_COPY',reload:RELOAD,send:c=>post(c,'CopyRosterWeek',({week:c.cmd.from}))},
    {type:'HR_ATT_MARK',reload:RELOAD,
      // No status = self check-in (server clock decides Present / Late); with a status = a manager's mark (+ overtime, note).
      send:c=>post(c,'MarkAttendance',c.cmd.status?{staffId:serverId(c.cmd.staffId,'Staff'),status:c.cmd.status,otMinutes:Number(c.cmd.otMin??0),
        ...(String(c.cmd.note??'').trim()?{note:String(c.cmd.note).trim()}:{})}:{staffId:serverId(c.cmd.staffId,'Staff')})},
    {type:'HR_LEAVE_REQUEST',reload:RELOAD,send:c=>post(c,'RequestLeave',({staffId:serverId(c.cmd.staffId,'Staff'),leaveType:c.cmd.leaveType,from:c.cmd.from,
      to:c.cmd.half?null:c.cmd.to,half:c.cmd.half??null,reason:String(c.cmd.reason??'').trim()}))},
    {type:'HR_LEAVE_DECIDE',reload:RELOAD,send:c=>post(c,'DecideLeave',({leaveId:serverId(c.cmd.id,'Leave request'),approve:!!c.cmd.approve,...(text(c.cmd.note)?.trim()?{note:String(c.cmd.note).trim()}:{})}),['leave',c.cmd.id,'Leave request'])},
    {type:'HR_LEAVE_CANCEL',reload:RELOAD,send:c=>post(c,'CancelLeave',({leaveId:serverId(c.cmd.id,'Leave request')}),['leave',c.cmd.id,'Leave request'])},
    {type:'HR_LEAVE_POLICY_SET',reload:RELOAD,
      // SetLeavePolicy (entitlements + default limit), then SaveDepartment for each department whose own limit changed (step keys dept:<code>).
      async send(c){const cmd=c.cmd,x=hr(c),p=x?.leavePolicy??{ent:{CL:12,SL:7,EL:15},defaultCap:2,deptCap:{}};
        const ent=cmd.ent??p.ent;
        await c.api.command('SetLeavePolicy',{cl:ent.CL,sl:ent.SL,el:ent.EL,defaultCap:cmd.defaultCap??p.defaultCap});
        if(cmd.deptCap&&typeof cmd.deptCap==='object')for(const d of x?.depts??[]){
          const v=(cmd.deptCap as Record<string,unknown>)[d.id],next=v===undefined||v===null||v===''?null:Number(v),was=p.deptCap[d.id]??null;
          if(next!==was)await c.api.command('SaveDepartment',{code:d.id,name:d.name,leaveCap:next},{step:'dept:'+d.id});
        }}},
    {type:'HR_ADVANCE_ADD',reload:RELOAD,send:c=>post(c,'GiveAdvance',({staffId:serverId(c.cmd.staffId,'Staff'),amountPaise:c.cmd.amount,installmentPaise:c.cmd.installment??0,
      reason:String(c.cmd.reason??'').trim(),fromMonth:c.cmd.fromMonth||null}))},
    {type:'HR_STAT_RATES_SET',reload:RELOAD,send:c=>post(c,'SetStatutoryRates',ratesBody(c.cmd))},
    {type:'HR_RATES_SET',reload:RELOAD,send:c=>post(c,'SetStatutoryRates',ratesBody(c.cmd))},
    {type:'HR_PAYROLL_RUN',reload:RELOAD,send:c=>post(c,'PreparePayRun',({month:c.cmd.month}))},
    {type:'HR_PAYROLL_LOCK',reload:RELOAD,send:c=>post(c,'LockPayRun',({runId:serverId(c.cmd.id,'Payroll run')}),['run',c.cmd.id,'Payroll run'])},
    {type:'HR_PAYROLL_UNLOCK',reload:RELOAD,send:c=>post(c,'UnlockPayRun',({runId:serverId(c.cmd.id,'Payroll run'),reason:String(c.cmd.reason??'').trim(),confirmBankFile:!!c.cmd.confirmBankFile}),['run',c.cmd.id,'Payroll run'])},
    {type:'HR_PAYROLL_BANK_FILE',reload:RELOAD,send:c=>post(c,'IssueBankFile',({runId:serverId(c.cmd.id,'Payroll run'),confirmReissue:!!c.cmd.confirmReissue}))},
    {type:'HR_PAYSLIP_SEND',reload:RELOAD,send:c=>post(c,'SendPayslip',({runId:serverId(c.cmd.runId,'Payroll run'),staffId:serverId(c.cmd.staffId,'Staff')}))},
    {type:'HR_FNF_SETTLE',reload:RELOAD,send:c=>post(c,'PrepareFnf',({staffId:serverId(c.cmd.staffId,'Staff'),
      adjustments:(Array.isArray(c.cmd.adjustments)?c.cmd.adjustments:[]).map((a:{label:string;kind:string;amount:number})=>({label:a.label,kind:a.kind,amountPaise:a.amount}))}))},
    {type:'HR_FNF_CONFIRM',reload:RELOAD,send:c=>post(c,'ConfirmFnf',({fnfId:serverId(c.cmd.id,'Settlement')}),['fnf',c.cmd.id,'Settlement'])},
    {type:'HR_FNF_DISCARD',reload:RELOAD,send:c=>post(c,'DiscardFnf',({fnfId:serverId(c.cmd.id,'Settlement')}),['fnf',c.cmd.id,'Settlement'])},
  ],
  unmapped:{},
};
