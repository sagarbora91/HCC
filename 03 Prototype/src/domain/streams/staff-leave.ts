// Module 17 · leave — SCC LeaveDesk ported in full (DEC-116; DEC-119 keeps HCC's safeguards). Pure helpers for the leave screens, the request /
// import / clear handlers and the production client. Source: E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\
// SCC-modules\leave\index.html (extracted main script scratchpad\scc\leave.js; Lnnn below = its line). Exported names start with 'hr' / 'HR'.
//   submitLeave (L1321)        → hrLeaveRequestPlan / hrLeaveRequestToast   per date: week-off skip → duplicate skip → capacity skip → add (pending)
//   renderBalanceHint (L1271)  → hrLeaveBalanceHint                         "used U of Q this FY — this request adds R → A (EXCEEDS balance)"
//   renderCalendar (L662)      → hrLeaveMonthView                           chips, limit / agenda / blackout dots, week-off tint
//   renderStats (L836)         → hrLeaveStats · renderStaffStrip (L882) → hrLeaveStrip · storeLeaveTallyForDay (L1507) → hrDeptTally
//   openReport / exportReportCSV (L1688 / L1784) → hrLeaveReport / hrLeaveReportCsv · printCalendar (L2047) → hrLeavePrintRows
//   importJSON (L1843) → hrLeaveImportParse (+ the merge in the HR_LEAVE_IMPORT handler) · exportJSON (L1974) → hrLeaveExportJson
// Statuses: SCC pending / approved / rejected (+ legacy = approved). HCC adds Cancelled = SCC's "removed" record, kept for audit (DEC-119) and left
// out of every view, count and file below.
import type {State,Command} from '../types';
import {addDays} from '../money';
import {
  hrSlice,hrLeavesOn,hrLeaveActive,hrCapacity,hrCanAddLeave,hrIsDuplicate,hrIsWeekOff,hrLeaveDays,hrEntFor,hrFyUsage,hrFy,hrLeavePolicy,hrDeptCap,
  hrBlackoutsFor,hrApproverChoices,hrDaysInMonth,hrMonthDates,hrWeekday,HR_MAX_DAY,HR_TRACKED,HR_SCC_CATEGORY,HR_WEEKDAYS,
  type HRLeave,type HRHalf,type HRBlackout,type HRAgenda,type HREnt,type HRStaff,
} from './staff';

const MONTHS=['January','February','March','April','May','June','July','August','September','October','November','December'];
/** SCC fmt: whole numbers as they are, otherwise one decimal (toFixed(1)). */
export const hrLeaveFmt=(n:number)=>n%1===0?String(n):n.toFixed(1);
/** "27 Oct 2026" (SCC formatDateShort, en-IN). */
export const hrLeaveDateShort=(d:string)=>new Date(d+'T12:00:00Z').toLocaleDateString('en-IN',{day:'numeric',month:'short',year:'numeric',timeZone:'UTC'});
/** "Tuesday, 27 October 2026" (SCC formatDateLong). */
export const hrLeaveDateLong=(d:string)=>new Date(d+'T12:00:00Z').toLocaleDateString('en-IN',{weekday:'long',day:'numeric',month:'long',year:'numeric',timeZone:'UTC'});
const staffName=(s:State,id:string)=>hrSlice(s).staff.find(t=>t.id===id)?.name??id;
const userName=(s:State,id?:string)=>id?(s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id):'';
/** The nominated approver's name (user, or the imported SCC name). */
export const hrLeaveApproverName=(s:State,l:{approver?:string;approverName?:string})=>l.approver?userName(s,l.approver):l.approverName??'';
/** SCC category name (CSV / print / backup), e.g. "Casual Leave". */
export const hrLeaveCategory=(code:string)=>HR_SCC_CATEGORY[code]??code;

// ── Calendar range (SCC START_MONTH / START_YEAR, buildMonthJumper) ──
/** DEC-123 (owner, 27 Oct 2026): the calendar starts at April 2026, the start of FY 2026–27, so that year's history shows (SCC stopped at May 2026:
 *  START_MONTH 4, START_YEAR 2026). The month arrows, the month jumper, Today / View-as-of, the report and print all use this one minimum. */
export const HR_LEAVE_START_YM='2026-04';
export const hrLeaveClampYm=(ym:string)=>ym<HR_LEAVE_START_YM?HR_LEAVE_START_YM:ym;
/** SCC buildMonthJumper: every month from the start month (April 2026, DEC-123) to December of the year after the viewed one. */
export function hrLeaveMonthJumper(viewYm:string):string[]{
  const end=+viewYm.slice(0,4)+1,out:string[]=[];
  for(let y=+HR_LEAVE_START_YM.slice(0,4),m=+HR_LEAVE_START_YM.slice(5,7);y<=end;m++){if(m>12){m=1;y++;if(y>end)break;}out.push(`${y}-${String(m).padStart(2,'0')}`);}
  return out;
}
/** SCC getFY label of a month, e.g. "FY 2026–27". */
export const hrLeaveFyLabel=(ym:string)=>hrFy(ym+'-01').label;
export const hrLeaveMonthName=(ym:string)=>`${MONTHS[+ym.slice(5,7)-1]} ${ym.slice(0,4)}`;

// ── Add leave (SCC submitLeave) ──
export type HRLeaveRequestPlan={dates:{date:string;result:'added'|'blocked'|'duplicate'|'weekoff'}[];added:number;blocked:number;duplicates:number;weekOff:number};
/** SCC submitLeave's loop over from…to: a week-off date is skipped; else a duplicate (hrIsDuplicate) is skipped; else a date that cannot take this
 *  leave (hrCanAddLeave: 3 per day, 4 per shift) is skipped as full; else a record is added. `ls` = the current records (default: the state's). */
export function hrLeaveRequestPlan(s:State,staffId:string,from:string,to:string,half?:HRHalf,ls:HRLeave[]=hrSlice(s).leaves):HRLeaveRequestPlan{
  const p:HRLeaveRequestPlan={dates:[],added:0,blocked:0,duplicates:0,weekOff:0};
  if(!from||!to||from>to)return p;
  const added:HRLeave[]=[];
  for(let d=from;d<=to;d=addDays(d,1)){
    if(hrIsWeekOff(s,d)){p.weekOff++;p.dates.push({date:d,result:'weekoff'});continue;}
    const day=[...ls.filter(l=>l.date===d),...added.filter(l=>l.date===d)];
    if(hrIsDuplicate(day,staffId,d,half)){p.duplicates++;p.dates.push({date:d,result:'duplicate'});}
    else if(!hrCanAddLeave(s,d,half,day)){p.blocked++;p.dates.push({date:d,result:'blocked'});}
    else{p.added++;p.dates.push({date:d,result:'added'});added.push({id:'',staffId,date:d,type:'',from,to,days:half?0.5:1,reason:'',...(half?{half}:{}),status:'Pending',requestedBy:'',requestedAt:''});}
  }
  return p;
}
/** SCC's toast after Add Leave (L1401-1406); `ok` false = SCC's error toast (nothing added). */
export function hrLeaveRequestToast(p:HRLeaveRequestPlan):{text:string;ok:boolean}{
  const parts:string[]=[];
  if(p.added>0)parts.push(`${p.added} day(s) submitted — pending approval`);
  if(p.blocked>0)parts.push(`${p.blocked} skipped — day/shift full (max ${HR_MAX_DAY}/day)`);
  if(p.duplicates>0)parts.push(`${p.duplicates} skipped — duplicate`);
  if(p.weekOff>0&&p.added===0&&p.blocked===0&&p.duplicates===0)parts.push(`${p.weekOff} day(s) skipped — weekend/weekly-off`);
  return {text:parts.join(', ')||'Nothing added',ok:p.added>0};
}
/** SCC balance check before recording (L1342-1352): a tracked category whose approved FY use + this request's days exceeds the quota. The warning
 *  is SCC's confirm text; recording goes on only when the person confirms (`confirmOverBalance`). Undefined = no warning. */
export function hrLeaveOverBalance(s:State,st:Pick<HRStaff,'id'|'name'>,type:string,from:string,to:string,half?:HRHalf):string|undefined{
  if(!(HR_TRACKED as string[]).includes(type)||!from)return undefined;
  const fy=hrFy(from),used=hrFyUsage(s,st.id,fy)[type]??0,quota=hrEntFor(s,st.id)[type as 'CL'|'SL'|'EL'],req=hrLeaveDays(s,from,to||from,half);
  if(used+req<=quota)return undefined;
  return `${st.name} has used ${hrLeaveFmt(used)} of ${quota} ${hrLeaveCategory(type)} day(s) this ${fy.label}.\nThis request adds ${hrLeaveFmt(req)} more and EXCEEDS the balance.\n\nRecord it anyway (it will still need approval)?`;
}
/** DEC-122 (owner, 27 Oct 2026 — SCC re-checks nothing at approval): approving ONE date record of a tracked category re-checks the person's balance
 *  for that date's leave year — approved use by date (hrFyUsage, this record left out) + this date (SCC leaveDaysOf: full day 1, AM / PM 0.5) over
 *  the entitlement (per-staff override, else the default) → SCC's over-quota warning (the request's text, asking "Approve it anyway?"); the
 *  approval goes on only when confirmed (`confirmOverBalance`). Rejecting needs no check; LOP and the other categories have no quota. Undefined =
 *  no warning. Each date is decided on its own, so two pending requests that together pass the quota: the first approves, the second warns. */
export function hrLeaveApproveOverBalance(s:State,l:Pick<HRLeave,'id'|'staffId'|'type'|'date'|'half'>):string|undefined{
  if(!(HR_TRACKED as string[]).includes(l.type)||!l.date)return undefined;
  const fy=hrFy(l.date),used=hrFyUsage(s,l.staffId,fy,l.id)[l.type]??0,quota=hrEntFor(s,l.staffId)[l.type as 'CL'|'SL'|'EL'],add=l.half?0.5:1;
  if(used+add<=quota)return undefined;
  return `${staffName(s,l.staffId)} has used ${hrLeaveFmt(used)} of ${quota} ${hrLeaveCategory(l.type)} day(s) this ${fy.label}.\nThis request adds ${hrLeaveFmt(add)} more and EXCEEDS the balance.\n\nApprove it anyway?`;
}
/** DEC-122: the HR_LEAVE_DECIDE command a leave screen sends for one date (approvals list, day view, leave detail, decide dialog; desktop and phone).
 *  Approving over the quota first asks `ask` (the screen's confirm dialog) with hrLeaveApproveOverBalance's warning and then sends
 *  confirmOverBalance; undefined = the person said no (nothing is sent). Rejecting never asks. */
export function hrLeaveDecideCmd(s:State,l:HRLeave,approve:boolean,ask:(warning:string)=>boolean,note?:string):Command|undefined{
  const over=approve?hrLeaveApproveOverBalance(s,l):undefined;
  if(over&&!ask(over))return undefined;
  return {type:'HR_LEAVE_DECIDE',id:l.id,approve,...(note?{note}:{}),...(over?{confirmOverBalance:true}:{})};
}
/** SCC renderBalanceHint (L1271-1296): the live line under the form; `exceed` paints it red. */
export function hrLeaveBalanceHint(s:State,staffId:string,type:string,from:string,to:string,half?:HRHalf):{text:string;exceed:boolean;tracked:boolean}|undefined{
  if(!staffId||!from)return undefined;
  const fy=hrFy(from),used=hrFyUsage(s,staffId,fy)[type]??0,cat=hrLeaveCategory(type);
  if(!(HR_TRACKED as string[]).includes(type))return {text:`${cat}: ${hrLeaveFmt(used)} day(s) used this ${fy.label} (no annual quota tracked)`,exceed:false,tracked:false};
  const quota=hrEntFor(s,staffId)[type as 'CL'|'SL'|'EL'],req=hrLeaveDays(s,from,to||from,half),after=used+req,exceed=after>quota;
  return {text:`${cat}: used ${hrLeaveFmt(used)} of ${quota} this ${fy.label}`+(req?` — this request adds ${hrLeaveFmt(req)} → ${hrLeaveFmt(after)}${exceed?' (EXCEEDS balance)':''}`:''),exceed,tracked:true};
}
/** SCC onLeaveFormChange capacity warning for the FROM date (L1215-1222). */
export function hrLeaveCapacityWarning(s:State,date:string,half?:HRHalf):string{
  if(!date)return '';const c=hrCapacity(s,date);
  if(c.dayFull)return `⚠ Daily limit reached — max ${HR_MAX_DAY} leaves per day (${c.dayUsed}/${HR_MAX_DAY})`;
  if(!half&&!c.canAddFullDay)return `⚠ Full day blocked — AM: ${c.amUsed}/4, PM: ${c.pmUsed}/4`;
  if(half==='AM'&&!c.canAddAM)return `⚠ Morning shift is full (${c.amUsed}/4)`;
  if(half==='PM'&&!c.canAddPM)return `⚠ Afternoon shift is full (${c.pmUsed}/4)`;
  return '';
}
/** SCC live blackout warning (L1227-1251): "⛔ Hard block — …" / "⚠ Discouraged — …". */
export function hrLeaveBlackoutWarning(hits:{hits:HRBlackout[];hard:boolean;labels:string}):string{
  return hits.hits.length?(hits.hard?'⛔ Hard block — ':'⚠ Discouraged — ')+'blackout window: '+hits.labels:'';
}
/** SCC blackout gate texts (L1369 / L1372). */
export const hrLeaveHardBlackoutText=(labels:string)=>`Leave falls in a hard blackout window (${labels}) — blocked`;
export const hrLeaveSoftBlackoutText=(labels:string,category:string)=>`These dates fall in a blackout window (${labels}): ${category} leave is discouraged.\nRecord it anyway?`;

// ── Calendar, stats, staffing strip, day view ──
/** SCC storeLeaveTallyForDay, per department: active records per department vs its advisory cap (over = used > cap, strictly); `unassigned` = staff
 *  without a department (never flagged). */
export function hrDeptTally(s:State,date:string){
  const x=hrSlice(s),act=hrLeavesOn(s,date).filter(hrLeaveActive);
  const depts=x.depts.map(d=>{const used=act.filter(l=>x.staff.find(t=>t.id===l.staffId)?.dept===d.id).length,cap=hrDeptCap(s,d.id);return {dept:d.id,name:d.name,used,cap,over:used>cap};});
  const unassigned=act.filter(l=>{const st=x.staff.find(t=>t.id===l.staffId);return !st||!x.depts.some(d=>d.id===st.dept);}).length;
  return {depts,unassigned};
}
export type HRLeaveCell={date:string;day:number;weekOff:boolean;past:boolean;today:boolean;
  cap:ReturnType<typeof hrCapacity>;limitTitle:string;chips:{leave:HRLeave;name:string;pending:boolean}[];more:number;
  agendas:HRAgenda[];blackouts:HRBlackout[];blackoutHard:boolean;blackoutTitle:string};
export const hrAgendasOn=(s:State,date:string)=>(hrSlice(s).agendas??[]).filter(a=>a.date===date);
/** SCC renderCalendar for a month (`today` = HCC's business date). Rejected and cancelled records are not on the grid (SCC: audit only). */
export function hrLeaveMonthView(s:State,ym:string,today=s.date):{lead:number;cells:HRLeaveCell[]}{
  const lead=(hrWeekday(ym+'-01')+6)%7;
  const cells=hrMonthDates(ym).map(date=>{
    const cap=hrCapacity(s,date),vis=hrLeavesOn(s,date).filter(hrLeaveActive),bk=hrBlackoutsFor(s,date);
    return {date,day:+date.slice(8),weekOff:hrIsWeekOff(s,date),past:date<today,today:date===today,cap,
      limitTitle:cap.amFull||cap.pmFull?(cap.bothFull?'All shifts full':cap.amFull?'AM shift full':'PM shift full'):'',
      chips:vis.slice(0,3).map(l=>({leave:l,name:staffName(s,l.staffId),pending:l.status==='Pending'})),more:Math.max(0,vis.length-3),
      agendas:hrAgendasOn(s,date),blackouts:bk,blackoutHard:bk.some(b=>b.hard),
      blackoutTitle:bk.map(b=>(b.label||(b.kind==='holiday'?'Store holiday':'Blackout'))+' ('+(b.dept?hrSlice(s).depts.find(d=>d.id===b.dept)?.name??b.dept:'All departments')+(b.kind==='holiday'?', store holiday':'')+')').join('\n')};
  });
  return {lead,cells};
}
/** SCC renderStats (L836-872) for a month. */
export function hrLeaveStats(s:State,ym:string){
  let fullDays=0,halfDays=0,atLimit=0,leaveDays=0,pending=0,agendas=0;
  for(const d of hrMonthDates(ym)){
    for(const l of hrLeavesOn(s,d)){
      if(l.status==='Pending'){pending++;continue;}
      if(l.status!=='Approved')continue;
      if(!l.half){fullDays++;leaveDays+=1;}else{halfDays++;leaveDays+=0.5;}
    }
    if(hrCapacity(s,d).bothFull)atLimit++;
    agendas+=hrAgendasOn(s,d).length;
  }
  return [
    {label:'Leave Days',value:leaveDays%1===0?String(leaveDays):leaveDays.toFixed(1),sub:'approved payroll days this month',tone:''},
    {label:'Pending',value:String(pending),sub:'awaiting approval',tone:'amber'},
    {label:'Full Days',value:String(fullDays),sub:'full-day absences',tone:'mid'},
    {label:'Half Days',value:String(halfDays),sub:'= '+(halfDays/2).toFixed(1)+' payroll days',tone:'amber'},
    {label:'Limit Reached',value:String(atLimit),sub:'days at the '+HR_MAX_DAY+'/day leave limit',tone:'red'},
    {label:'Agenda Items',value:String(agendas),sub:'scheduled this month',tone:'blue'},
  ];
}
/** SCC renderStaffStrip (L882-939): seven dates from the as-of date. `onLeave` counts records (an AM + PM of one person = 2; owner question). */
export function hrLeaveStrip(s:State,asOf:string,today=s.date){
  const heads=hrSlice(s).staff.filter(t=>t.active).length;
  return Array.from({length:7},(_,i)=>{
    const date=addDays(asOf,i),onLeave=hrCapacity(s,date).dayUsed,nonTrading=hrIsWeekOff(s,date),blackout=hrBlackoutsFor(s,date).length>0;
    const short=nonTrading?[]:hrDeptTally(s,date).depts.filter(d=>d.over).map(d=>d.name);
    return {date,dow:HR_WEEKDAYS[hrWeekday(date)],day:+date.slice(8),today:date===today,onLeave,heads,avail:heads>0?`${Math.max(0,heads-onLeave)}/${heads} avail`:'',
      nonTrading,blackout:blackout&&!nonTrading,short};
  });
}
/** SCC openDayModal daily note (L981-987). */
export function hrLeaveDayNote(c:ReturnType<typeof hrCapacity>){
  return c.dayFull?`⚠ Daily limit reached — ${c.dayUsed}/${HR_MAX_DAY} leaves (max ${HR_MAX_DAY} per day)`:`${c.dayUsed}/${HR_MAX_DAY} leaves today — ${HR_MAX_DAY-c.dayUsed} remaining (max ${HR_MAX_DAY} per day)`;
}

// ── Monthly report, CSV, print (SCC openReport / exportReportCSV / printCalendar) ──
export type HRLeaveReportRow={staffId:string;name:string;full:number;halfAM:number;halfPM:number;leaveDays:number;pending:number;cats:string[];
  /** "CL u/q · SL u/q · EL u/q" for the FY of the month (per-staff quota). */ balance:string;used:Record<string,number>;quota:{CL:number;SL:number;EL:number}};
/** SCC openReport: per staff member for the month (rejected and cancelled left out); pending counted apart; sorted by name; TOTAL. */
export function hrLeaveReport(s:State,ym:string){
  const by=new Map<string,HRLeaveReportRow>();
  for(const d of hrMonthDates(ym))for(const l of hrLeavesOn(s,d)){
    if(!hrLeaveActive(l))continue;
    let r=by.get(l.staffId);
    if(!r){r={staffId:l.staffId,name:staffName(s,l.staffId),full:0,halfAM:0,halfPM:0,leaveDays:0,pending:0,cats:[],balance:'',used:{},quota:{CL:0,SL:0,EL:0}};by.set(l.staffId,r);}
    if(l.status==='Pending'){r.pending++;continue;}
    if(!l.half)r.full++;else if(l.half==='AM')r.halfAM++;else r.halfPM++;
    if(!r.cats.includes(l.type))r.cats.push(l.type);
  }
  const fy=hrFy(ym+'-01');
  const rows=[...by.values()].sort((a,b)=>a.name.localeCompare(b.name)).map(r=>{
    const used=hrFyUsage(s,r.staffId,fy),quota=hrEntFor(s,r.staffId);
    return {...r,leaveDays:r.full+(r.halfAM+r.halfPM)*0.5,used,quota,balance:HR_TRACKED.map(c=>`${c} ${hrLeaveFmt(used[c]??0)}/${quota[c]}`).join(' · ')};
  });
  const total={full:0,halfAM:0,halfPM:0,leaveDays:0,pending:0};
  for(const r of rows){total.full+=r.full;total.halfAM+=r.halfAM;total.halfPM+=r.halfPM;total.leaveDays+=r.leaveDays;total.pending+=r.pending;}
  return {rows,total,fy:fy.label};
}
/** SCC exportReportCSV: every cell quoted (quotes doubled); HCC adds its formula guard (a text cell starting with = + - @ gets a leading '). */
export function hrLeaveReportCsv(s:State,ym:string):{name:string;csv:string}{
  const cell=(v:string|number)=>{let t=String(v);if(typeof v==='string'&&/^[=+\-@\t\r]/.test(t)&&!/^-?\d+(\.\d+)?$/.test(t))t="'"+t;return `"${t.replace(/"/g,'""')}"`;};
  const rows:(string|number)[][]=[['Employee','Full Days','Half AM','Half PM','Leave Days','Pending','Categories','FY Balance (used/quota)']];
  for(const r of hrLeaveReport(s,ym).rows)
    rows.push([r.name,r.full,r.halfAM,r.halfPM,r.leaveDays,r.pending,r.cats.map(hrLeaveCategory).join('; '),HR_TRACKED.map(c=>`${hrLeaveCategory(c)} ${r.used[c]??0}/${r.quota[c]}`).join('; ')]);
  return {name:`leave_report_${MONTHS[+ym.slice(5,7)-1]}_${ym.slice(0,4)}.csv`,csv:rows.map(r=>r.map(cell).join(',')).join('\n')};
}
/** SCC printCalendar rows (landscape "Leave Calendar — Month Year"): leaves (every record but cancelled — rejected included, SCC) and agendas. */
export function hrLeavePrintRows(s:State,ym:string){
  const mn=MONTHS[+ym.slice(5,7)-1],leaves:{date:string;dow:string;name:string;type:string;notes:string}[]=[],agendas:{date:string;dow:string;text:string}[]=[];
  for(const d of hrMonthDates(ym)){
    const date=`${d.slice(8)} ${mn.slice(0,3)}`,dow=HR_WEEKDAYS[hrWeekday(d)];
    for(const l of hrLeavesOn(s,d)){if(l.status==='Cancelled')continue;
      const tag=l.status!=='Approved'?'['+l.status.toUpperCase()+']':'',appr=hrLeaveApproverName(s,l);
      leaves.push({date,dow,name:staffName(s,l.staffId),type:l.half==='AM'?'Half Day — Morning':l.half==='PM'?'Half Day — Afternoon':'Full Day',
        notes:[tag,hrLeaveCategory(l.type),l.reason,appr?'By: '+appr:''].filter(Boolean).join(' · ')});}
    for(const a of hrAgendasOn(s,d))agendas.push({date,dow,text:(a.time?a.time+' · ':'')+a.title+(a.type?' ('+a.type+')':'')+(a.description?' — '+a.description:'')});
  }
  return {title:`Leave Calendar — ${mn} ${ym.slice(0,4)}`,leaves,agendas};
}

// ── Import / export / clear (SCC importJSON / exportJSON / executeClearData) ──
const VALID_TYPES=['full_day','half_day_am','half_day_pm'],VALID_AGENDA=['meeting','agenda','reminder'];
const SCC_CATS=['Casual Leave','Sick Leave','Earned Leave','Maternity / Paternity','Compensatory Off','Other'];
const sanitize=(v:unknown,max:number)=>typeof v!=='string'?'':v.slice(0,max).replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g,'').trim();
const validDate=(k:string)=>/^\d{4}-\d{2}-\d{2}$/.test(k)&&new Date(k+'T00:00:00Z').toISOString().slice(0,10)===k;
const halfOf=(t:string):HRHalf|undefined=>t==='half_day_am'?'AM':t==='half_day_pm'?'PM':undefined;
const typeOf=(l:{half?:HRHalf})=>l.half==='AM'?'half_day_am':l.half==='PM'?'half_day_pm':'full_day';
/** A validated record of an imported SCC file (names resolved to HCC staff / users). */
export type HRLeaveImportRec={sccId:string;staffId:string;date:string;half?:HRHalf;type:string;reason:string;approver?:string;approverName?:string;
  from:string;to:string;status:'Pending'|'Approved'|'Rejected';legacy:boolean;decidedByName?:string;decidedAt?:string};
export type HRLeaveImportAgenda={sccId:string;date:string;title:string;type:HRAgenda['type'];time:string;description:string};
export type HRLeaveImportPlan={leaves:HRLeaveImportRec[];agendas:HRLeaveImportAgenda[];skipped:number;ent?:{defaults:HREnt;perStaff:Record<string,HREnt>};weekOff?:number[]};
/** SCC importJSON validation (L1849-1932) on an HCC state: a date key that is invalid or a week-off date skips its whole list; a record needs a name,
 *  an approver (≤ 100), a valid type, valid from / to when given; an unknown category becomes Other ('Leave without pay' = HCC's LOP); status kept
 *  when valid, absent = legacy approved. HCC keys staff by ID: a name that matches no staff member is skipped. A string = SCC's error. */
export function hrLeaveImportParse(s:State,imp:unknown):HRLeaveImportPlan|string{
  if(typeof imp!=='object'||imp===null)return 'Root must be an object';
  const o=imp as Record<string,unknown>;
  if(typeof o.leaves!=='object'||o.leaves===null)return 'Missing "leaves" object';
  if(typeof o.agendas!=='object'||o.agendas===null)return 'Missing "agendas" object';
  const staff=hrSlice(s).staff,appr=hrApproverChoices(s);
  const staffBy=(n:string)=>staff.find(t=>t.name.trim().toLowerCase()===n.toLowerCase());
  const out:HRLeaveImportPlan={leaves:[],agendas:[],skipped:0};
  for(const [k,arr] of Object.entries(o.leaves as Record<string,unknown>)){
    if(!validDate(k)||hrIsWeekOff(s,k)){out.skipped++;continue;}
    if(!Array.isArray(arr)){out.skipped++;continue;}
    for(const l of arr as Record<string,unknown>[]){
      if(typeof l!=='object'||l===null){out.skipped++;continue;}
      const name=sanitize(l.name,100),approvedBy=sanitize(l.approvedBy,100),type=sanitize(l.type,20),category=sanitize(l.category,60),reason=sanitize(l.reason,500);
      const leaveFrom=sanitize(l.leaveFrom,10),leaveTo=sanitize(l.leaveTo,10),id=sanitize(l.id,30),st0=sanitize(l.status,20);
      const status=st0==='pending'?'Pending':st0==='approved'?'Approved':st0==='rejected'?'Rejected':undefined;
      const decidedBy=sanitize(l.decidedBy,100),decidedAt=sanitize(l.decidedAt,30);
      if(!name||!approvedBy){out.skipped++;continue;}
      if(!VALID_TYPES.includes(type)){out.skipped++;continue;}
      if(leaveFrom&&!validDate(leaveFrom)){out.skipped++;continue;}
      if(leaveTo&&!validDate(leaveTo)){out.skipped++;continue;}
      const st=staffBy(name);if(!st){out.skipped++;continue;}// HCC: staff keyed by ID — the name must be a staff member
      const code=category==='Leave without pay'?'LOP':SCC_CATS.includes(category)?Object.keys(HR_SCC_CATEGORY).find(c=>HR_SCC_CATEGORY[c]===category)!:'OTH';
      const a=appr.find(u=>u.name.toLowerCase()===approvedBy.toLowerCase());
      const half=halfOf(type);
      out.leaves.push({sccId:id,staffId:st.id,date:k,...(half?{half}:{}),type:code,reason,...(a?{approver:a.id}:{approverName:approvedBy}),from:leaveFrom||k,to:leaveTo||leaveFrom||k,
        status:status??'Approved',legacy:!status,...(decidedBy?{decidedByName:decidedBy}:{}),...(decidedAt?{decidedAt}:{})});
    }
  }
  for(const [k,arr] of Object.entries(o.agendas as Record<string,unknown>)){
    if(!validDate(k)){out.skipped++;continue;}
    if(!Array.isArray(arr)){out.skipped++;continue;}
    for(const a of arr as Record<string,unknown>[]){
      if(typeof a!=='object'||a===null){out.skipped++;continue;}
      const title=sanitize(a.title,200),type=sanitize(a.type,20),time=sanitize(a.time,10),description=sanitize(a.description,1000),id=sanitize(a.id,30);
      if(!title||!VALID_AGENDA.includes(type)){out.skipped++;continue;}
      out.agendas.push({sccId:id,date:k,title,type:type as HRAgenda['type'],time,description});
    }
  }
  const ent=o.entitlements as Record<string,unknown>|undefined;
  if(ent&&typeof ent==='object'){
    const read=(src:unknown):HREnt=>{const r:HREnt={};if(src&&typeof src==='object')for(const c of HR_TRACKED){const v=Number((src as Record<string,unknown>)[hrLeaveCategory(c)]);if(Number.isFinite(v)&&v>=0)r[c]=v;}return r;};
    const e={defaults:read(ent.defaults),perStaff:{} as Record<string,HREnt>};
    if(ent.perEmployee&&typeof ent.perEmployee==='object')for(const k2 of Object.keys(ent.perEmployee).slice(0,500)){
      const ov=read((ent.perEmployee as Record<string,unknown>)[k2]),st=staffBy(sanitize(k2,100));
      if(st&&Object.keys(ov).length)e.perStaff[st.id]=ov;
    }
    out.ent=e;
  }
  const wo=o.weekOff as {days?:unknown}|undefined;
  if(wo&&Array.isArray(wo.days))out.weekOff=[...new Set((wo.days as unknown[]).map(Number).filter(n=>n>=0&&n<=6))];
  return out;
}
/** SCC import preview confirm (L1934). */
export const hrLeaveImportPreview=(p:HRLeaveImportPlan)=>`Import preview:\n• ${p.leaves.length} valid leave record(s)\n• ${p.agendas.length} valid agenda item(s)\n• ${p.skipped} record(s) skipped (invalid / weekend / missing fields)\n\nThis will MERGE with existing data. Continue?`;
/** SCC exportJSON (leavedesk_v3 + entitlements + weekOff; no caps or blackouts, as SCC — owner question). Cancelled records are left out (SCC removed
 *  them); legacy records carry no status. `staffId` is HCC's extra field (SCC ignores it). */
export function hrLeaveExportJson(s:State):{name:string;json:string}{
  const x=hrSlice(s),leaves:Record<string,unknown[]>={},agendas:Record<string,unknown[]>={};
  for(const l of [...x.leaves].sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:0)){if(l.status==='Cancelled')continue;
    (leaves[l.date]??=[]).push({id:l.sccId??l.id,name:staffName(s,l.staffId),type:typeOf(l),category:hrLeaveCategory(l.type),reason:l.reason,approvedBy:hrLeaveApproverName(s,l),
      leaveFrom:l.from,leaveTo:l.to,...(l.legacy?{}:{status:l.status.toLowerCase()}),...(l.decidedBy||l.decidedByName?{decidedBy:l.decidedByName??userName(s,l.decidedBy)}:{}),
      ...(l.decidedAt?{decidedAt:l.decidedAt}:{}),staffId:l.staffId});}
  for(const a of x.agendas??[])(agendas[a.date]??=[]).push({id:a.id,title:a.title,time:a.time,type:a.type,description:a.description});
  const p=hrLeavePolicy(s),cats=(e:HREnt)=>Object.fromEntries(HR_TRACKED.filter(c=>e[c]!=null).map(c=>[hrLeaveCategory(c),e[c]]));
  const perEmployee=Object.fromEntries(Object.entries(p.perStaff??{}).map(([id,e])=>[staffName(s,id).trim().toLowerCase(),cats(e)]));
  const out={leaves,agendas,employees:x.staff.map(t=>({id:t.id,name:t.name,empId:t.id,department:x.depts.find(d=>d.id===t.dept)?.name??t.dept})),
    entitlements:{defaults:cats(p.ent),perEmployee},weekOff:{days:[...(p.weekOff??[])]}};
  return {name:`leavedesk_backup_${s.date}.json`,json:JSON.stringify(out,null,2)};
}
/** SCC Clear All warning list (L1995-2000), HCC wording: leave records are cancelled (kept for audit), not deleted. */
export function hrLeaveClearList(s:State){
  const x=hrSlice(s),n=x.leaves.filter(l=>l.status!=='Cancelled').length;
  return [`${n} leave record(s) across all dates (cancelled and kept for audit; a locked pay month keeps its records)`,`${(x.agendas??[]).length} agenda item(s) across all dates`,
    'Leave entitlement settings (defaults + per-employee overrides)','Weekly-off setting'];
}
/** Days in a month (re-export for the screens). */
export const hrLeaveDaysInMonth=(ym:string)=>hrDaysInMonth(ym);
