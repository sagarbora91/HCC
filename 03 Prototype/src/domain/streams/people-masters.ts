// MA-6 · People lists (plan Part 2 §8 M19–M22): designations, shift patterns, leave types and the holiday calendar, registered in the masters
// framework (domain/streams/masters.ts) under the "People, payroll and leave" group. Pure domain: specs, seeds, cached selectors.
//   designations   name · department · grade · default pay rule (pay rule: Owner-only) — the staff form picks from it (HRStaff.designationCode).
//   shiftPatterns  name · start · end · break · night · weekly-off rule — roster, attendance (late check-in), staff default shift.
//   leaveTypes     name · paid · yearly quota · carry-forward · encashment — leave policy per type (CL 12 · SL 7 · EL 15 stay the seed). Pay fields: Owner-only.
//   holidays       date (the code) · name · paid / restricted — paid holidays count in payroll day counts and leave days; the calendar shifts
//                  statutory due dates (Compliance). FY is derived from the date (Owner-only list, DEC-058).
// Exported names start with 'hr' / 'HR' (shared mock namespace). Seeds reproduce today's constants, so no fixture figure moves: BASE and FY26
// history are unchanged until a list is saved. Nothing here imports staff.ts at run time (staff.ts imports this file).
import type {State} from '../types';
import {registerMaster,mstRows,mstStored,type MasterRow,type MasterSpec} from './masters';

// ───────────────────────────── Row types ─────────────────────────────
export type HRPayRule='monthly-structured'|'monthly-basic'|'daily';
export type HRDesignation=MasterRow&{dept?:string;grade?:string;defaultPayRule?:HRPayRule};
export type HRWeeklyOffRule='fixed'|'roster';
export type HRShiftPattern=MasterRow&{start:string;end:string;breakMinutes?:number;night?:boolean;weeklyOffRule?:HRWeeklyOffRule};
export type HRLeaveTypeRow=MasterRow&{paid?:boolean;yearlyQuota?:number;carryForward?:number;encashment?:boolean};
export type HRHolidayKind='paid'|'restricted';
export type HRHoliday=MasterRow&{kind:HRHolidayKind};

export const HRM_DESIGNATIONS='designations',HRM_SHIFTS='shiftPatterns',HRM_LEAVE_TYPES='leaveTypes',HRM_HOLIDAYS='holidays';
export const HR_PAY_RULES:{value:HRPayRule;label:string}[]=[
  {value:'monthly-structured',label:'Monthly salary · structured (basic / HRA / washing / special, PF applies)'},
  {value:'monthly-basic',label:'Monthly salary · basic only'},
  {value:'daily',label:'Daily wage · basic only'}];
/** What a pay rule fills into a new staff record (the staff form; payroll roles only). */
export const hrPayRuleFields=(r:HRPayRule):{payType:'monthly'|'daily';structure:'S'|'B'}=>r==='monthly-structured'?{payType:'monthly',structure:'S'}:r==='monthly-basic'?{payType:'monthly',structure:'B'}:{payType:'daily',structure:'B'};
export const HR_WEEKLY_OFF_RULES:{value:HRWeeklyOffRule;label:string}[]=[
  {value:'fixed',label:'Fixed weekly off day (set on the staff record)'},
  {value:'roster',label:'Set in the roster each week (no automatic weekly off)'}];

// ───────────────────────────── Seeds (today's constants) ─────────────────────────────
/** Shift times of the prototype since module 17 (07–15, 15–23, 23–07, 09–18); break 30 minutes; fixed weekly off. */
export const HR_SHIFT_SEED:{code:string;name:string;start:string;end:string;night?:boolean}[]=[
  {code:'M',name:'Morning',start:'07:00',end:'15:00'},{code:'E',name:'Evening',start:'15:00',end:'23:00'},
  {code:'N',name:'Night',start:'23:00',end:'07:00',night:true},{code:'G',name:'General',start:'09:00',end:'18:00'}];
export const HR_LEAVE_SEED:{code:string;name:string;paid:boolean;tracked?:'CL'|'SL'|'EL';encashment?:boolean}[]=[
  {code:'CL',name:'Casual leave',paid:true,tracked:'CL'},{code:'SL',name:'Sick leave',paid:true,tracked:'SL'},{code:'EL',name:'Earned leave',paid:true,tracked:'EL',encashment:true},
  {code:'CO',name:'Compensatory off',paid:true},{code:'ML',name:'Maternity / paternity leave',paid:true},{code:'LOP',name:'Leave without pay',paid:false}];
/** Illustrative holiday calendar (owner/CA to confirm each year). Paid = national and major public holidays; restricted = optional. */
export const HR_HOLIDAY_SEED:{code:string;name:string;kind:HRHolidayKind}[]=[
  {code:'2026-08-15',name:'Independence Day',kind:'paid'},{code:'2026-09-14',name:'Ganesh Chaturthi',kind:'restricted'},
  {code:'2026-10-02',name:'Gandhi Jayanti',kind:'paid'},{code:'2026-10-20',name:'Dussehra',kind:'paid'},
  {code:'2026-11-08',name:'Diwali (Lakshmi Puja)',kind:'paid'},{code:'2026-12-25',name:'Christmas',kind:'paid'},
  {code:'2027-01-26',name:'Republic Day',kind:'paid'},{code:'2027-05-01',name:'Maharashtra Day',kind:'paid'}];

const NO:object=Object.freeze({});
/** A selector memoised on the identity of the rows array it reads (the framework replaces row arrays on every write, never edits them in place). */
function memoOn<R>(key:(s:State)=>object|undefined,build:(s:State)=>R):(s:State)=>R{
  const wm=new WeakMap<object,R>();
  return s=>{const k=key(s)??NO;let r=wm.get(k);if(r===undefined){r=build(s);wm.set(k,r);}return r;};
}
const storedRows=(s:State,id:string)=>s.mastersX?.rows?.[id] as object|undefined;

// ───────────────────────────── Shift patterns ─────────────────────────────
const shiftMemo=memoOn(s=>storedRows(s,HRM_SHIFTS),s=>{const rows=mstRows(s,HRM_SHIFTS) as HRShiftPattern[];return {rows,byCode:new Map(rows.map(r=>[r.code,r]))};});
/** All shift patterns in sort order (retired ones too, so old rosters still resolve); active:true = the pick list. */
export const hrShiftPatterns=(s:State,opts:{active?:boolean}={}):HRShiftPattern[]=>shiftMemo(s).rows.filter(r=>opts.active===undefined||r.active===opts.active);
export const hrShiftPattern=(s:State,code:string):HRShiftPattern|undefined=>shiftMemo(s).byCode.get(code);
/** Pattern choices for a staff default shift / roster cell: active rows plus the current value (retired rows stay visible on old records). */
export const hrShiftChoices=(s:State,current?:string):HRShiftPattern[]=>hrShiftPatterns(s).filter(r=>r.active||r.code===current);
const minutes=(t:string)=>+t.slice(0,2)*60+ +t.slice(3,5);
const HHMM=/^([01]\d|2[0-3]):[0-5]\d$/;
/** Duty length in minutes (overnight shifts wrap), and the paid part after the unpaid break. */
export const hrShiftMinutes=(p:Pick<HRShiftPattern,'start'|'end'>)=>{const d=minutes(p.end)-minutes(p.start);return d>0?d:d+1440;};
export const hrShiftPaidMinutes=(p:HRShiftPattern)=>Math.max(0,hrShiftMinutes(p)-(Number(p.breakMinutes)||0));
/** Weekly off follows the staff record unless the staff member's default shift pattern says the roster decides. */
export const hrWeeklyOffAuto=(s:State,defaultShift:string)=>hrShiftPattern(s,defaultShift)?.weeklyOffRule!=='roster';

// ───────────────────────────── Leave types ─────────────────────────────
const leaveMemo=memoOn(s=>storedRows(s,HRM_LEAVE_TYPES)??(s.staffX?.leavePolicy as object|undefined),s=>{const rows=mstRows(s,HRM_LEAVE_TYPES) as HRLeaveTypeRow[];return {rows,byCode:new Map(rows.map(r=>[r.code,r]))};});
export const hrLeaveTypeRows=(s:State,opts:{active?:boolean}={}):HRLeaveTypeRow[]=>leaveMemo(s).rows.filter(r=>opts.active===undefined||r.active===opts.active);
export const hrLeaveTypeRow=(s:State,code:string):HRLeaveTypeRow|undefined=>leaveMemo(s).byCode.get(code);
/** Name of a leave type (retired and unknown codes still print). */
export const hrLeaveName=(s:State,code:string)=>hrLeaveTypeRow(s,code)?.name??code;
/** Paid leave? Unknown codes count as paid, except the built-in unpaid LOP. */
export const hrLeavePaid=(s:State,code:string)=>{const r=hrLeaveTypeRow(s,code);return r?r.paid!==false:code!=='LOP';};
/** Yearly quota in days; undefined = not tracked (no balance check). */
export const hrLeaveQuota=(s:State,code:string):number|undefined=>{const q=hrLeaveTypeRow(s,code)?.yearlyQuota;return Number.isFinite(q)?q:undefined;};
/** Most days that may be carried into the next financial year (0 = none). */
export const hrLeaveCarryCap=(s:State,code:string)=>Number(hrLeaveTypeRow(s,code)?.carryForward)||0;
export const hrLeaveEncash=(s:State,code:string)=>!!hrLeaveTypeRow(s,code)?.encashment;
/** Active types with a yearly quota (balances shown and enforced). */
export const hrTrackedTypes=(s:State):string[]=>hrLeaveTypeRows(s,{active:true}).filter(r=>Number.isFinite(r.yearlyQuota)).map(r=>r.code);
/** Leave types a staff member may request: active; daily-wage staff only unpaid types. */
export const hrLeaveChoices=(s:State,payType:'monthly'|'daily'):HRLeaveTypeRow[]=>hrLeaveTypeRows(s,{active:true}).filter(r=>payType==='monthly'||r.paid===false);

// ───────────────────────────── Holidays ─────────────────────────────
const holMemo=memoOn(s=>storedRows(s,HRM_HOLIDAYS),s=>{const rows=mstRows(s,HRM_HOLIDAYS) as HRHoliday[];
  return {rows,byDate:new Map(rows.filter(r=>r.active).map(r=>[r.code,r])),paid:new Set(rows.filter(r=>r.active&&r.kind==='paid').map(r=>r.code))};});
export const hrHolidays=(s:State,opts:{active?:boolean}={}):HRHoliday[]=>holMemo(s).rows.filter(r=>opts.active===undefined||r.active===opts.active);
/** The active holiday on a date (any kind). */
export const hrHolidayOn=(s:State,date:string):HRHoliday|undefined=>holMemo(s).byDate.get(date);
/** Paid holiday on a date: payroll counts it as a paid day and leave days skip it. */
export const hrHolidayPaid=(s:State,date:string)=>holMemo(s).paid.has(date);
/** Dates of the active paid holidays (compliance due dates roll past them). */
export const hrHolidayDates=(s:State):ReadonlySet<string>=>holMemo(s).paid;
/** Indian financial year (April–March) label of a holiday date, e.g. "FY 2026–27". */
export function hrHolidayFy(date:string){const y=+date.slice(5,7)>=4?+date.slice(0,4):+date.slice(0,4)-1;return `FY ${y}–${String(y+1).slice(-2)}`;}

// ───────────────────────────── Designations ─────────────────────────────
const slugCode=(name:string)=>name.toUpperCase().replace(/[^A-Z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,20).replace(/-+$/,'')||'DESIG';
/** Seed: one designation per distinct designation text of the staff master (first department / pay type seen), in order of appearance. */
export function hrSeedDesignations(s:State):HRDesignation[]{
  const out:HRDesignation[]=[],seen=new Set<string>(),codes=new Set<string>();
  for(const st of s.staffX?.staff??[]){
    const name=String(st.designation??'').trim();if(!name||seen.has(name.toLowerCase()))continue;seen.add(name.toLowerCase());
    let code=slugCode(name),n=2;while(codes.has(code)){const suf='-'+n++;code=slugCode(name).slice(0,20-suf.length)+suf;}codes.add(code);
    const rule:HRPayRule=st.payType==='daily'?'daily':(st.structure??'S')==='B'?'monthly-basic':'monthly-structured';
    out.push({id:'dsg-'+code,code,name:name.slice(0,60),sortOrder:(out.length+1)*10,active:true,dept:st.dept,defaultPayRule:rule});
  }
  return out;
}
/** Designation row of a staff record: by code when set, else by the designation text (legacy records). */
export function hrDesignationOf(s:State,st:{designation:string;designationCode?:string}):HRDesignation|undefined{
  const rows=mstRows(s,HRM_DESIGNATIONS) as HRDesignation[];
  return st.designationCode?rows.find(r=>r.code===st.designationCode):rows.find(r=>r.name.toLowerCase()===String(st.designation).trim().toLowerCase());
}
/** Designation choices for a department: active rows of that department (or with no department) plus the current one. */
export const hrDesignationChoices=(s:State,dept?:string,current?:string):HRDesignation[]=>
  (mstRows(s,HRM_DESIGNATIONS) as HRDesignation[]).filter(r=>(r.active&&(!dept||!r.dept||r.dept===dept))||r.code===current);

// ───────────────────────────── Specs ─────────────────────────────
const deptIds=(s:State)=>(s.staffX?.depts??[]).map(d=>d.id);
const DESIGNATION_SPEC:MasterSpec<HRDesignation>={
  id:HRM_DESIGNATIONS,label:'Designations',singular:'designation',group:'people',prefix:'DSG',money:false,permission:'masters',
  help:'Job titles the staff form picks from. The default pay rule prefills a new staff record (Owner sets it; amounts stay on the staff record).',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'ROOM-ATTENDANT',help:'Cannot change once staff use it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Room attendant',help:'Printed on payslips and registers.'},
    {key:'dept',label:'Department code',type:'text',maxLength:10,placeholder:'HK',help:'Department of the staff who hold it (as in Staff & payroll › Departments); leave empty for any department.'},
    {key:'grade',label:'Grade',type:'text',maxLength:20,placeholder:'G2',help:'Optional pay grade or level.'},
    {key:'defaultPayRule',label:'Default pay rule',type:'select',money:true,options:HR_PAY_RULES.map(x=>({value:x.value,label:x.label})),help:'Pay type and structure a new staff record starts with. Owner only (DEC-058).'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle',help:'Retire a designation instead of deleting it; staff keep the title.'},
  ],
  inUse:(s,row)=>{const staff=(s.staffX?.staff??[]).filter(st=>st.designationCode?st.designationCode===row.code:String(st.designation).trim().toLowerCase()===row.name.toLowerCase());
    return {count:staff.length,where:staff.length?[`Staff ${staff.map(x=>x.name).join(', ')}`]:[]};},
  validate:(s,row)=>{const ids=deptIds(s);
    if(row.dept&&ids.length&&!ids.includes(String(row.dept)))return `Department ${row.dept} does not exist. Use one of: ${ids.join(', ')}.`;
    if(row.grade&&!/^[A-Za-z0-9 ./-]{1,20}$/.test(String(row.grade)))return 'Grade: letters, digits, space, “.”, “/” or “-”.';return undefined;},
  seed:hrSeedDesignations,
};
const SHIFT_SPEC:MasterSpec<HRShiftPattern>={
  id:HRM_SHIFTS,label:'Shift patterns',singular:'shift pattern',group:'people',prefix:'SHP',money:false,permission:'masters',
  help:'Duty times for the roster and attendance. A staff member checks in late after the start time + 15 minutes; night patterns are never late.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:3,placeholder:'M',help:'1–3 capital letters or digits (not OFF). Cannot change once used.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Morning'},
    {key:'start',label:'Start (HH:MM)',type:'text',required:true,maxLength:5,placeholder:'07:00'},
    {key:'end',label:'End (HH:MM)',type:'text',required:true,maxLength:5,placeholder:'15:00',help:'Earlier than the start = ends the next morning (mark it a night shift).'},
    {key:'breakMinutes',label:'Unpaid break (minutes)',type:'number',min:0,max:240,help:'Shown on the roster as paid hours; not deducted from pay.'},
    {key:'night',label:'Night shift',type:'toggle',help:'Night shifts are never marked late on check-in.'},
    {key:'weeklyOffRule',label:'Weekly off',type:'select',options:HR_WEEKLY_OFF_RULES.map(x=>({value:x.value,label:x.label})),help:'Default: the staff record’s weekly off day applies.'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:(s,row)=>{const staff=(s.staffX?.staff??[]).filter(st=>st.defaultShift===row.code);
    const cells=Object.entries(s.staffX?.roster??{}).filter(([k,v])=>v===row.code&&k.slice(0,10)>=s.date).length;
    return {count:staff.length+cells,where:[...(staff.length?[`Staff ${staff.map(x=>x.name).join(', ')}`]:[]),...(cells?[`${cells} roster cell${cells===1?'':'s'}`]:[])]};},
  canRetire:(s,row)=>{const staff=(s.staffX?.staff??[]).filter(st=>st.active&&st.defaultShift===row.code);
    return staff.length?`${row.name} is the default shift of ${staff.map(x=>x.name).join(', ')} — change their default shift first.`:undefined;},
  validate:(s,row,rows)=>{
    if(!/^[A-Z0-9]{1,3}$/.test(row.code)||row.code==='OFF')return 'Code: 1–3 capital letters or digits (OFF is reserved for weekly off).';
    if(!HHMM.test(String(row.start))||!HHMM.test(String(row.end)))return 'Start and end are times like 07:00 (24-hour).';
    if(row.start===row.end)return 'Start and end cannot be the same time.';
    if(String(row.end)<String(row.start)&&!row.night)return 'A shift that ends after midnight must be marked as a night shift.';
    const m=hrShiftMinutes(row as HRShiftPattern);if((Number(row.breakMinutes)||0)>=m)return 'The break must be shorter than the shift.';
    return rows.some(r=>r.id!==row.id&&r.active&&row.active!==false&&r.start===row.start&&r.end===row.end)?'Another active pattern already has these times.':undefined;},
  seed:()=>HR_SHIFT_SEED.map((x,i)=>({id:'shp-'+x.code,code:x.code,name:x.name,sortOrder:(i+1)*10,active:true,start:x.start,end:x.end,breakMinutes:30,night:!!x.night,weeklyOffRule:'fixed' as const})),
};
const LEAVE_SPEC:MasterSpec<HRLeaveTypeRow>={
  id:HRM_LEAVE_TYPES,label:'Leave types',singular:'leave type',group:'people',prefix:'LVT',money:false,permission:'masters',
  help:'Leave policy per type. A yearly quota makes the type tracked (balance checked per financial year, April–March). Paid, quota, carry-forward and encashment are pay rules: Owner only.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:6,placeholder:'CL',help:'1–6 capital letters or digits. Cannot change once leave uses it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Casual leave'},
    {key:'paid',label:'Paid leave',type:'toggle',money:true,help:'Unpaid leave is loss of pay in payroll. Daily-wage staff can take unpaid types only.'},
    {key:'yearlyQuota',label:'Yearly quota (days)',type:'number',min:0,max:60,money:true,help:'Empty = not tracked (no balance, no limit).'},
    {key:'carryForward',label:'Carry forward (days)',type:'number',min:0,max:60,money:true,help:'Most unused days carried into the next financial year; 0 = none.'},
    {key:'encashment',label:'Paid out at full and final',type:'toggle',money:true,help:'Unused balance is offered as an encashment in full and final settlement.'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:(s,row)=>{const n=(s.staffX?.leaves??[]).filter(l=>l.type===row.code).length;return {count:n,where:n?[`${n} leave request${n===1?'':'s'}`]:[]};},
  canRetire:(s,row)=>{if(row.paid===false&&!(mstRows(s,HRM_LEAVE_TYPES) as HRLeaveTypeRow[]).some(r=>r.id!==row.id&&r.active&&r.paid===false))return 'Keep at least one active unpaid leave type — daily-wage staff can take unpaid leave only.';return undefined;},
  validate:(s,row,rows)=>{
    if(!/^[A-Z0-9]{1,6}$/.test(row.code))return 'Code: 1–6 capital letters or digits.';
    if(row.yearlyQuota!==undefined&&!Number.isInteger(row.yearlyQuota))return 'Yearly quota: whole days.';
    if(row.carryForward!==undefined&&!Number.isInteger(row.carryForward))return 'Carry forward: whole days.';
    if((row.carryForward??0)>0&&row.yearlyQuota===undefined)return 'Carry forward needs a yearly quota (an untracked type has no balance).';
    if(row.paid===false&&((row.yearlyQuota??0)>0||(row.carryForward??0)>0||row.encashment))return 'Unpaid leave has no quota, carry-forward or encashment.';
    const others=rows.filter(r=>r.id!==row.id&&r.active&&(r as HRLeaveTypeRow).paid===false);
    if(row.paid!==false&&row.active!==false&&!others.length)return 'Keep at least one active unpaid leave type — daily-wage staff can take unpaid leave only.';
    return undefined;},
  seed:s=>{const ent=s.staffX?.leavePolicy?.ent??{CL:12,SL:7,EL:15};
    return HR_LEAVE_SEED.map((x,i)=>({id:'lvt-'+x.code,code:x.code,name:x.name,sortOrder:(i+1)*10,active:true,paid:x.paid,...(x.tracked?{yearlyQuota:ent[x.tracked]}:{}),carryForward:0,encashment:!!x.encashment}));},
};
const ISO=/^\d{4}-\d{2}-\d{2}$/;
const validDate=(d:string)=>ISO.test(d)&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;
const HOLIDAY_SPEC:MasterSpec<HRHoliday>={
  id:HRM_HOLIDAYS,label:'Holiday calendar',singular:'holiday',group:'people',prefix:'HOL',money:true,permission:'settings',
  help:'Public holidays by date (Owner only — they change pay and statutory due dates). A paid holiday is a paid day for staff and is not counted as leave; compliance due dates falling on one move to the next working day. Restricted holidays are information only.',
  fields:[
    {key:'code',label:'Date (YYYY-MM-DD)',type:'text',required:true,maxLength:10,placeholder:'2026-11-08',help:'One holiday per date. The financial year (April–March) follows from the date. Cannot change once a locked pay-run covers it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Diwali (Lakshmi Puja)'},
    {key:'kind',label:'Kind',type:'select',required:true,options:[{value:'paid',label:'Paid holiday'},{value:'restricted',label:'Restricted (optional) holiday'}]},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle',help:'Retire a holiday to stop it counting; locked pay-runs keep their figures.'},
  ],
  inUse:(s,row)=>{const months=(s.staffX?.runs??[]).filter(r=>r.status==='Locked'&&r.month===row.code.slice(0,7));
    return {count:months.length,where:months.length?[`Locked pay-run ${months[0].month}`]:[]};},
  validate:(s,row)=>{if(!validDate(row.code))return 'Date: a real calendar date like 2026-11-08.';return undefined;},
  seed:()=>HR_HOLIDAY_SEED.map((x,i)=>({id:'hol-'+x.code,code:x.code,name:x.name,sortOrder:(i+1)*10,active:true,kind:x.kind})),
};
registerMaster(DESIGNATION_SPEC);registerMaster(SHIFT_SPEC);registerMaster(LEAVE_SPEC);registerMaster(HOLIDAY_SPEC);
/** True once any people list has been saved (rows live in s.mastersX). */
export const hrPeopleListsStored=(s:State)=>[HRM_DESIGNATIONS,HRM_SHIFTS,HRM_LEAVE_TYPES,HRM_HOLIDAYS].some(id=>mstStored(s,id));
