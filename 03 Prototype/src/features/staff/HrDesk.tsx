// Module 17 · HR desk (page 'staff'): staff master, roster, attendance, leave (requests, calendar, policy), payroll, advances, full and final, statutory rates.
// Tabs by role: managers (approve) see people/roster/attendance/leave without salaries; Owner/Accountant see payroll.
// Anyone else who opens the page sees their own "My HR" view. Desktop renders tables; the phone renders cards (phone=true).
// W05-B: SCC payroll rules (PF / ESIC / MH PT, lock + Owner unlock, bank and statutory files). SCC LeaveDesk in full (DEC-116 / DEC-119):
// the Leave tab = Leave.tsx (desk, approvals, report, settings) + the DEC-083 Holidays list. Payroll run and statutory rates are desktop/tablet tasks:
// the phone shows a read-only summary (Wave 0.5 placement §2).
import {saveCsv,saveBinaryFile} from '../../app/native';
import {getSyncRuntime} from '../../clients/sync/instance'; // production build only (W4 G2: payroll files from the server)
import {downloadServerPayrollFile} from '../../clients/sync/payfiles'; // production build only
import {errorText} from '../../clients/errors';
import {buildXlsx,XLSX_MIME} from '../../app/xlsx';
import React,{useState} from 'react';
import {UserPlus,ChevronLeft,ChevronRight,Copy,Download,Lock,Unlock,Calculator,Printer,Plus,Settings2,AlertTriangle,Check,X,Landmark,FileSpreadsheet,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,FitNote} from '../../components/fit';
import {ListCard,KpiCard,ChipFilter,StickyFooter} from '../../app/mobile/components';
import {
  money,addDays,isDayClosed,cfgModuleOn,hrSlice,hrCanManage,hrCanPay,hrCanRates,hrStaffOfUser,hrShiftFor,hrShiftInfo,hrMonday,hrWeek,hrKey,hrDayStatus,hrCounts,
  hrDeptName,hrPendingLeaves,hrLeaveClampYm,hrRunFor,hrRunTotals,hrMonthLabel,hrPrevMonth,hrNextMonth,hrMonthComplete,hrRegisterCsv,hrBankCsv,hrStatutoryCsv,hrAdvanceBalance,hrComputeRun,
  hrRateVersions,hrRatesFor,hrRatesDefaultFrom,hrRunErrors,hrFnfSettled,hrFnfAdvanceLeftOpen,HR_FNF_OWNER_ABOVE,hrLastLockedMonth,hrBankFilesIssued,hrPayslipMark,hrSettingDefs,hrSettingValue,hrWeekday,
  hrCorrectionWindow,hrPendingRequests,hrPaidHolidays,hrPaidHolidayOn,hrUnmarkedDates,hrEntryLabel,hrFy,
  HR_ATT_LABEL,hrLeaveName,hrShiftPatterns,HR_WEEKDAYS,HR_DISCLAIMER,HR_NOT_CALCULATED,HR_SCC_DAYS,type HRStaff,type HRRun,type HRLeave,type HRRequest,
} from '../../services/mock';
import {MyHr,hrAttTone} from './MyHr';
import {LeaveDeskView,LeaveApprovalsView,LeaveReportView,LeaveSettingsView} from './Leave';
import {MstLink} from '../masters/MasterList';
import {hrUserName,hrBankText} from './modals';

// FIT (DEC-194…196): the desktop tabs are page sub-screens (useFitTab 'staff', SUB_SCREENS block FIT-PEOPLE-A); the phone keeps its own chip state.
const STAFF_TABS=['people','roster','attendance','leave','payroll','advances','fnf','settings'] as const;
type Tab=typeof STAFF_TABS[number];
const TAB_LABEL:Record<Tab,string>={people:'Staff',roster:'Roster',attendance:'Attendance',leave:'Leave',payroll:'Payroll',advances:'Advances',fnf:'Full and final',settings:'Statutory rates'};
const day=(d:string)=>HR_WEEKDAYS[new Date(d+'T12:00:00Z').getUTCDay()];
/** "Casual leave · Half Day AM" style label (one record per date, SCC). */
export const hrLeaveKind=(l:HRLeave,s:ReturnType<typeof useHcc>['s'])=>hrLeaveName(s,l.type)+(l.half?` · ½ day (${l.half==='AM'?'morning':'afternoon'})`:'');

export function HrDesk({phone=false}:{phone?:boolean}){
  const {s,uid}=useHcc();const mgr=hrCanManage(s,uid),pay=hrCanPay(s,uid),payOn=cfgModuleOn(s,'payroll');
  // W05P-1: Payroll switched off in Master configuration hides the payroll tabs (its commands are refused anyway); attendance and leave stay.
  const tabs:Tab[]=[...(mgr||pay?['people','roster','attendance','leave'] as Tab[]:[]),...(pay&&payOn?['payroll','advances','fnf','settings'] as Tab[]:[])];
  const [pick,setTab]=useState<Tab>(tabs[0]??'people');
  const [fitPick,setFitTab]=useFitTab<Tab>('staff',STAFF_TABS,tabs[0]??'people');// desktop: go('staff',{tab}) / remembered; the role's own tabs only
  const chosen=phone?pick:fitPick;const tab=tabs.includes(chosen)?chosen:tabs[0]??'people';
  if(!tabs.length)return phone?<MyHr phone/>:<MyHr/>;
  const pending=hrPendingLeaves(s).length;// SCC approvals badge: every pending date record
  const body={people:<People phone={phone}/>,roster:<Roster phone={phone}/>,attendance:<Attendance phone={phone}/>,leave:<Leave phone={phone}/>,payroll:<Payroll phone={phone}/>,advances:<Advances phone={phone}/>,fnf:<Fnf phone={phone}/>,settings:<Rates phone={phone}/>}[tab];
  if(phone)return <div className="hr-m"><ChipFilter label="HR section" value={tab} onChange={setTab} options={tabs.map(t=>({id:t,label:TAB_LABEL[t],count:t==='leave'&&pending?pending:undefined}))}/>{body}</div>;
  // FIT: FitPage with the HR sections as page tabs (DEC-058 gating unchanged: `tabs` is still the role's list); each section fits its body.
  return <FitPage title="Staff & payroll" sub={`Fictional staff · business date ${dateLabel(s.date)} · ${pay&&!payOn?'Payroll is switched off in Master configuration (attendance and leave stay on)':pay?'PF / ESIC / PT from SCC — illustrative, CA to confirm for a hotel':'salary figures are visible to the Owner and Accountant only'}`}
      tabs={<FitTabs page="staff" value={tab} onChange={setFitTab} tabs={tabs.map(t=>({id:t,label:TAB_LABEL[t],count:t==='leave'&&pending>0?pending:undefined,badge:t==='leave'&&pending>0?'warn' as const:undefined}))}/>}>
    {body}</FitPage>;
}

// ── Staff master ──
function People({phone}:{phone:boolean}){
  const {s,uid,open,run,busy}=useHcc();const x=hrSlice(s),mgr=hrCanManage(s,uid),pay=hrCanPay(s,uid);
  const [dept,setDept]=useState('All');
  const list=x.staff.filter(st=>dept==='All'||st.dept===dept).sort((a,b)=>Number(b.active)-Number(a.active));
  const active=x.staff.filter(t=>t.active);
  const today=active.map(st=>hrDayStatus(s,st,s.date));
  const present=today.filter(t=>t.kind==='att'&&t.att!.status!=='A').length,onLeave=today.filter(t=>t.kind==='leave').length;
  const payOf=(st:HRStaff)=>st.payType==='monthly'?`${money(st.monthly)} / mo`:`${money(st.daily)} / day`;
  const deptOpts:{id:string;label:string;count?:number}[]=[{id:'All',label:'All'},...x.depts.map(d=>({id:d.id,label:d.name,count:x.staff.filter(t=>t.dept===d.id&&t.active).length}))];
  const toggle=(st:HRStaff)=>{if(!st.active||window.confirm(`Record ${st.name} as exited today? They leave rosters and future payroll.`))run({type:'HR_STAFF_ACTIVE',id:st.id,active:!st.active},st.active?'Staff member exited':'Staff member reactivated');};
  const bank=pay?<Requests kind="bank" phone={phone}/>:null;// DEC-084: bank-detail changes waiting for the Owner (payroll roles only)
  if(phone)return <>
    <div className="hr-kpis"><KpiCard label="Active staff" value={active.length}/><KpiCard label="Present today" value={present} sub={`${onLeave} on leave`}/></div>
    {bank}
    <ChipFilter label="Department" value={dept} onChange={setDept} options={deptOpts}/>
    {list.map(st=><ListCard key={st.id} title={st.name} sub={`${st.designation} · ${hrDeptName(s,st.dept)}`} meta={`${hrShiftInfo(st.defaultShift,s).label} · off ${HR_WEEKDAYS[st.weeklyOff]}${st.userId?' · login '+hrUserName(s,st.userId):' · no login'}${pay?' · '+payOf(st):''}`}
      right={st.active?undefined:<Pill>Exited</Pill>} onClick={mgr||pay?()=>open({type:'hr-staff',id:st.id}):undefined}/>)}
    {(mgr||pay)&&<StickyFooter>{process.env.HCC_BUILD==='production'&&<button className="btn" onClick={()=>open({type:'hr-dept'})}><Settings2 size={18}/> Departments</button>}<button className="btn primary" onClick={()=>open({type:'hr-staff'})}><UserPlus size={18}/> Add staff member</button></StickyFooter>}</>;
  // FIT: the four counters stay one row on top; the staff table is a ScrollPanel (filters + Add in its fixed head); bank-detail changes sit beside it.
  const hasBank=pay&&(x.requests??[]).some(r=>r.kind==='bank');
  const table=<ScrollPanel label="Staff list" actions={<><div className="filters">{deptOpts.map(o=><button key={o.id} className={dept===o.id?'active':''} onClick={()=>setDept(o.id)}>{o.label}{o.count!==undefined?` (${o.count})`:''}</button>)}</div>
      {(mgr||pay)&&(process.env.HCC_BUILD==='production'
        // N3: a fresh server has no departments — the production build adds and renames them here (SaveDepartment).
        ?<div className="inline"><button className="btn" onClick={()=>open({type:'hr-dept'})}><Settings2 size={16}/> Departments</button><button className="btn primary" onClick={()=>open({type:'hr-staff'})}><UserPlus size={16}/> Add staff member</button></div>
        :<button className="btn primary" onClick={()=>open({type:'hr-staff'})}><UserPlus size={16}/> Add staff member</button>)}</>}
      footer={<p className="footnote"><MstLink spec="designations"/> <MstLink spec="shiftPatterns"/></p>}>
      <div className="table-wrap"><table><thead><tr><th>Staff member</th><th>Department</th><th>Demo login</th><th>Default shift · weekly off</th>{pay&&<th className="number">Pay</th>}<th>Status</th><th></th></tr></thead><tbody>
        {list.map(st=><tr key={st.id}><td><strong>{st.name}</strong><small>{st.id} · {st.designation}</small></td><td>{hrDeptName(s,st.dept)}</td><td>{st.userId?hrUserName(s,st.userId):<span className="muted">No login</span>}</td>
          <td>{hrShiftInfo(st.defaultShift,s).label} {hrShiftInfo(st.defaultShift,s).start}–{hrShiftInfo(st.defaultShift,s).end} · {HR_WEEKDAYS[st.weeklyOff]}</td>
          {pay&&<td className="number">{payOf(st)}</td>}<td>{st.active?<Pill tone="green">Active</Pill>:<Pill>Exited {st.exitedOn&&shortDate(st.exitedOn)}</Pill>}</td>
          <td className="hr-actions">{(mgr||pay)&&<button className="text-btn" onClick={()=>open({type:'hr-staff',id:st.id})}>Edit</button>}{mgr&&<button className="text-btn" disabled={busy} onClick={()=>toggle(st)}>{st.active?'Exit':'Reactivate'}</button>}</td></tr>)}
      </tbody></table></div></ScrollPanel>;
  return <>
    <div className="small-stats"><div><span>Active staff</span><strong>{active.length}</strong></div><div><span>Present today</span><strong>{present}</strong></div><div><span>On leave today</span><strong>{onLeave}</strong></div><div><span>Pending leave</span><strong>{x.leaves.filter(l=>l.status==='Pending').length}</strong></div></div>
    {hasBank?<div className="fit-cols hr-fit-even"><div>{table}</div><div>{bank}</div></div>:table}</>;
}

// ── Roster ──
function Roster({phone}:{phone:boolean}){
  const {s,uid,open,run,busy}=useHcc();const mgr=hrCanManage(s,uid);const [mon,setMon]=useState(hrMonday(s.date));
  const week=hrWeek(mon),staff=hrSlice(s).staff.filter(t=>t.active);
  const [pick,setPick]=useState(week.includes(s.date)?s.date:week[0]);const pd=week.includes(pick)?pick:week[0];
  const nav=<div className="inline hr-weeknav"><button className="btn" aria-label="Previous week" onClick={()=>setMon(addDays(mon,-7))}><ChevronLeft size={16}/></button><strong>{shortDate(week[0])} – {shortDate(week[6])}</strong><button className="btn" aria-label="Next week" onClick={()=>setMon(addDays(mon,7))}><ChevronRight size={16}/></button></div>;
  const copy=mgr&&addDays(mon,13)>=s.date?<button className="btn" disabled={busy} onClick={()=>run({type:'HR_ROSTER_COPY',from:mon},'Roster copied to the following week')}><Copy size={16}/> Copy to next week</button>:null;
  const cell=(st:HRStaff,d:string)=>{const c=hrShiftFor(s,st,d),h=hrShiftInfo(c,s),over=!!hrSlice(s).roster[hrKey(d,st.id)],edit=mgr&&d>=s.date;
    const label=<><b>{c==='OFF'?'Off':h.label}</b>{c!=='OFF'&&<small>{h.start}</small>}</>;
    return edit?<button className={'hr-cell hr-s-'+c+(over?' hr-over':'')} aria-label={`${st.name} ${day(d)} ${shortDate(d)}: ${h.label}. Change`} onClick={()=>open({type:'hr-roster',id:st.id,room:d})}>{label}</button>
      :<span className={'hr-cell hr-s-'+c+(over?' hr-over':'')}>{label}</span>;};
  if(phone)return <>{nav}
    <ChipFilter label="Day" value={pd} onChange={setPick} options={week.map(d=>({id:d,label:`${day(d)} ${d.slice(8)}`}))}/>
    {staff.map(st=>{const c=hrShiftFor(s,st,pd),h=hrShiftInfo(c,s);return <ListCard key={st.id} title={st.name} sub={c==='OFF'?'Weekly off':`${h.label} ${h.start}–${h.end}`} meta={hrDeptName(s,st.dept)}
      right={hrSlice(s).roster[hrKey(pd,st.id)]?<Pill tone="blue">Changed</Pill>:undefined} onClick={mgr&&pd>=s.date?()=>open({type:'hr-roster',id:st.id,room:pd}):undefined}/>;})}
    {copy&&<StickyFooter>{copy}</StickyFooter>}</>;
  return <ScrollPanel title="Weekly roster" label="Weekly roster" actions={<div className="inline">{nav}{copy}</div>}
      footer={<><FitNote text={`${mgr?'Select a cell to change the shift. Past days are history and cannot change.':'Read-only view.'} Outlined cells differ from the default pattern.`}/>
        <FitNote text={`Shifts: ${hrShiftPatterns(s,{active:true}).map(p=>`${p.name} ${p.start}–${p.end}`).join(' · ')}. Late = check-in after start + 15 min (night shifts are never late).`}/><MstLink spec="shiftPatterns"/></>}>
    <div className="table-wrap"><table className="hr-roster"><thead><tr><th>Staff</th>{week.map(d=><th key={d} className={d===s.date?'hr-th-today':''}>{day(d)} {d.slice(8)}</th>)}</tr></thead><tbody>
      {staff.map(st=><tr key={st.id}><td><strong>{st.name}</strong><small>{hrDeptName(s,st.dept)}</small></td>{week.map(d=><td key={d}>{cell(st,d)}</td>)}</tr>)}</tbody></table></div>
  </ScrollPanel>;
}

// ── Attendance ──
/** Day status pill: attendance mark, leave, weekly off, DEC-083 paid holiday, not in service or not marked. */
function dayPill(x:ReturnType<typeof hrDayStatus>){
  return x.kind==='att'?<Pill tone={hrAttTone(x.att!.status)}>{HR_ATT_LABEL[x.att!.status]}</Pill>:x.kind==='leave'?<Pill tone="blue">On leave · {x.leave!.type}</Pill>
    :x.kind==='off'?<Pill>Weekly off</Pill>:x.kind==='holiday'?<Pill tone="blue">Paid holiday</Pill>:x.kind==='na'?<Pill>Not in service</Pill>:<Pill tone="orange">Not marked</Pill>;
}
function Attendance({phone}:{phone:boolean}){
  const {s,uid,open}=useHcc();const mgr=hrCanManage(s,uid),closed=isDayClosed(s);const [view,setView]=useState<'today'|'month'|'past'>('today');
  const staff=hrSlice(s).staff.filter(t=>t.active),ym=s.date.slice(0,7);
  const row=(st:HRStaff)=>{const x=hrDayStatus(s,st,s.date),sh=hrShiftInfo(hrShiftFor(s,st,s.date),s);
    const can=mgr&&!closed&&x.kind!=='leave'&&st.userId!==uid;return {x,sh,status:dayPill(x),can};};// F-10: own attendance via self check-in only
  const waiting=hrPendingRequests(s,'attendance').length;
  const toggle=<ChipFilter label="Attendance view" value={view} onChange={setView} options={[{id:'today',label:`Today · ${shortDate(s.date)}`},{id:'month',label:`${hrMonthLabel(ym)} to date`},
    {id:'past',label:'Past days',count:waiting||undefined}]}/>;
  if(view==='past')return <>{toggle}<PastDays phone={phone}/></>;
  if(view==='month'){
    const rows=staff.map(st=>({st,c:hrCounts(s,st,ym,addDays(s.date,-1))}));
    return <>{toggle}{phone?rows.map(({st,c})=><ListCard key={st.id} title={st.name} sub={`P ${c.P} · Late ${c.L} · Half ${c.H} · Absent ${c.A}`} meta={`Leave ${c.paidLeave+c.lopLeave} · Off ${c.off}${c.hol?` · Holiday ${c.hol}`:''} · OT ${Math.round(c.otMin/6)/10} h${c.unmarked?` · ${c.unmarked} unmarked`:''}`}/>)
      :<ScrollPanel title={`${hrMonthLabel(ym)} · 1 – ${shortDate(addDays(s.date,-1))}`} label="Attendance this month" actions={<span className="muted">Closed days only; today is on the Today view; corrections on Past days</span>}>
        <div className="table-wrap"><table><thead><tr><th>Staff</th><th className="number">Present</th><th className="number">Late</th><th className="number">Half-day</th><th className="number">Absent</th><th className="number">Paid leave</th><th className="number">LOP leave</th><th className="number">Off</th><th className="number">Holiday</th><th className="number">Unmarked</th><th className="number">Overtime</th></tr></thead><tbody>
          {rows.map(({st,c})=><tr key={st.id}><td><strong>{st.name}</strong><small>{st.designation}</small></td>{[c.P,c.L,c.H,c.A,c.paidLeave,c.lopLeave,c.off,c.hol??0,c.unmarked].map((v,i)=><td key={i} className="number">{v}</td>)}<td className="number">{Math.round(c.otMin/6)/10} h</td></tr>)}</tbody></table></div></ScrollPanel>}</>;
  }
  if(phone)return <>{toggle}{closed&&<div className="info-note">Business day closed — attendance is read-only.</div>}
    {staff.map(st=>{const r=row(st);return <ListCard key={st.id} title={st.name} sub={r.sh.code==='OFF'?'Weekly off':`${r.sh.label} ${r.sh.start}–${r.sh.end}`}
      meta={r.x.att?`${r.x.att.at.slice(11)}${r.x.att.otMin?` · OT ${r.x.att.otMin} min`:''}${r.x.att.note?' · '+r.x.att.note:''}`:undefined} right={r.status} onClick={r.can?()=>open({type:'hr-att',id:st.id}):undefined}/>;})}</>;
  return <>{toggle}<ScrollPanel title={`Attendance register · ${dateLabel(s.date)}`} label="Attendance register"
      footer={<FitNote text={closed?'Business day closed — read-only.':mgr?'Mark or correct today. Staff with a login can also check themselves in from My shifts & pay.':'Read-only view.'}/>}>
    <div className="table-wrap"><table><thead><tr><th>Staff</th><th>Rostered</th><th>Status</th><th>Recorded</th><th className="number">Overtime</th><th></th></tr></thead><tbody>
      {staff.map(st=>{const r=row(st);return <tr key={st.id}><td><strong>{st.name}</strong><small>{hrDeptName(s,st.dept)}</small></td><td>{r.sh.code==='OFF'?'Weekly off':`${r.sh.label} ${r.sh.start}–${r.sh.end}`}</td><td>{r.status}</td>
        <td>{r.x.att?<>{r.x.att.at.slice(11)} · {hrUserName(s,r.x.att.by)}{r.x.att.note&&<small>{r.x.att.note}</small>}</>:'—'}</td><td className="number">{r.x.att?.otMin?`${r.x.att.otMin} min`:'—'}</td>
        <td>{r.can&&<button className="text-btn" onClick={()=>open({type:'hr-att',id:st.id})}>{r.x.att?'Correct':'Mark'}</button>}</td></tr>;})}</tbody></table></div></ScrollPanel></>;
}

// ── DEC-075 past days (corrections in the open pay month) ──
function PastDays({phone}:{phone:boolean}){
  const {s,uid,open}=useHcc();const mgr=hrCanManage(s,uid),w=hrCorrectionWindow(s),has=w.to>=w.from;
  const [pick,setPick]=useState(w.to);const date=pick<w.from||pick>w.to?w.to:pick;
  const staff=has?hrSlice(s).staff.filter(st=>date>=st.joined&&(!st.exitedOn||date<=st.exitedOn)):[];
  const hol=has?hrPaidHolidayOn(s,date):undefined,can=(st:HRStaff)=>mgr&&st.userId!==uid;// F-10: never your own record
  const intro=has?`Open pay month: ${dateLabel(w.from)} – ${dateLabel(w.to)}. ${mgr?'Correct a day with a reason: a correction that lowers or keeps pay applies at once; one that raises pay waits for the Owner.':'Managers correct past days; the Owner approves corrections that raise pay.'} A locked month never changes.`
    :'No past day is open for correction yet.';
  const picker=has?<label className="hr-past-date">Day<input type="date" min={w.from} max={w.to} value={date} onChange={e=>setPick(e.target.value)}/></label>:null;
  const fix=(st:HRStaff)=>open({type:'hr-att-correct',id:st.id,room:date});
  if(phone)return <><p className="hr-m-note">{intro}</p>{picker}{hol&&<div className="info-note"><Info size={16}/>Paid holiday: {hol.name}</div>}
    {staff.map(st=>{const x=hrDayStatus(s,st,date);return <ListCard key={st.id} title={st.name} sub={x.att?`${HR_ATT_LABEL[x.att.status]}${x.att.otMin?` · OT ${x.att.otMin} min`:''}`:hrDeptName(s,st.dept)}
      meta={x.att?.note} right={dayPill(x)} onClick={can(st)?()=>fix(st):undefined}/>;})}
    <Requests kind="attendance" phone/></>;
  const hasReq=(hrSlice(s).requests??[]).some(r=>r.kind==='attendance');
  const main=<ScrollPanel title={`Past days${has?` · ${dateLabel(date)}`:''}`} label="Past days" actions={picker} footer={<FitNote text={intro}/>}>
    {hol&&<div className="info-note padded"><Info size={16}/>Paid holiday: {hol.name} — paid like a weekly off; staff who worked are marked present.</div>}
    {has?<div className="table-wrap"><table><thead><tr><th>Staff</th><th>Rostered</th><th>Status</th><th>Recorded</th><th className="number">Overtime</th><th></th></tr></thead><tbody>
      {staff.map(st=>{const x=hrDayStatus(s,st,date),sh=hrShiftInfo(hrShiftFor(s,st,date));return <tr key={st.id}><td><strong>{st.name}</strong><small>{hrDeptName(s,st.dept)}</small></td>
        <td>{sh.code==='OFF'?'Weekly off':`${sh.label} ${sh.start}–${sh.end}`}</td><td>{dayPill(x)}</td>
        <td>{x.att?<>{x.att.at.slice(11)} · {hrUserName(s,x.att.by)}{x.att.note&&<small>{x.att.note}</small>}</>:'—'}</td><td className="number">{x.att?.otMin?`${x.att.otMin} min`:'—'}</td>
        <td>{can(st)&&<button className="text-btn" onClick={()=>fix(st)}>Correct</button>}</td></tr>;})}</tbody></table></div>:<Empty title="Nothing to correct yet"/>}</ScrollPanel>;
  // FIT: the corrections list sits beside the day (one panel per column keeps both tall enough on a tablet).
  return hasReq?<div className="fit-cols hr-fit-even"><div>{main}</div><div><Requests kind="attendance" phone={false}/></div></div>:main;
}

/** DEC-075 / DEC-084 requests: those waiting for the Owner (the Owner decides — never the requester, never on their own record; the requester may
 *  withdraw), then the latest decided ones. The pay effect is shown to payroll roles only (the Manager sees no salaries). */
function Requests({kind,phone}:{kind:HRRequest['kind'];phone:boolean}){
  const {s,uid,open,run,busy}=useHcc();const x=hrSlice(s),pay=hrCanPay(s,uid),owner=hrCanRates(s,uid),me=hrStaffOfUser(s,uid);
  const all=(x.requests??[]).filter(r=>r.kind===kind),pending=all.filter(r=>r.status==='Pending'),done=all.filter(r=>r.status!=='Pending').slice(-8).reverse();
  if(!all.length)return null;
  const name=(id:string)=>x.staff.find(t=>t.id===id)?.name??id;
  const change=(r:HRRequest)=>r.kind==='bank'?`${hrBankText(r.bankBefore)} → ${hrBankText(r.bankAfter)}`:`${r.date?shortDate(r.date):''} · ${r.before?hrEntryLabel(r.before):'—'} → ${r.after?hrEntryLabel(r.after):'—'}`;
  const effect=(r:HRRequest)=>r.kind==='attendance'&&pay&&r.payEffect!==undefined?`${r.payEffect>0?'+':r.payEffect<0?'−':''}${money(Math.abs(r.payEffect))} gross pay`:'';
  const status=(r:HRRequest)=>r.status==='Approved'&&r.direct?'Applied':r.status;
  const tone=(r:HRRequest)=>r.status==='Approved'?'green':r.status==='Rejected'?'hr-red':r.status==='Pending'?'orange':'neutral';
  const actions=(r:HRRequest)=>owner&&r.requestedBy!==uid&&r.staffId!==me?.id
    ?<><button className="btn small-btn primary" disabled={busy} onClick={()=>open({type:'hr-request-decide',id:r.id,room:'approve'})}><Check size={14}/> Approve</button>
      <button className="btn small-btn" disabled={busy} onClick={()=>open({type:'hr-request-decide',id:r.id,room:'reject'})}><X size={14}/> Reject</button></>
    :r.requestedBy===uid?<button className="btn small-btn" disabled={busy} onClick={()=>run({type:'HR_REQUEST_WITHDRAW',id:r.id},'Request withdrawn')}>Withdraw</button>
    :<span className="muted">{owner?'Your own record or request — another person decides':'Waits for the Owner'}</span>;
  const title=kind==='bank'?'Bank-detail changes':'Attendance corrections';
  const why=kind==='bank'?'DEC-084: a change of bank details by anyone but the Owner waits for the Owner; payments and bank files keep the current details until then.'
    :'DEC-075: corrections that raise pay wait for the Owner; those that lower or keep pay are applied at once. Every correction is audited.';
  if(phone)return <><h2 className="m-group">{title.toUpperCase()}</h2><p className="hr-m-note">{why}</p>
    {pending.map(r=><div key={r.id} className="hr-m-leave"><ListCard title={name(r.staffId)} sub={change(r)} meta={[effect(r),r.reason,`by ${hrUserName(s,r.requestedBy)}`].filter(Boolean).join(' · ')} right={<Pill tone="orange">Waiting</Pill>}/>
      <div className="hr-m-actions">{actions(r)}</div></div>)}
    {done.map(r=><ListCard key={r.id} title={name(r.staffId)} sub={change(r)} meta={[effect(r),r.reason,r.note].filter(Boolean).join(' · ')} right={<Pill tone={tone(r)}>{status(r)}</Pill>}/>)}</>;
  return <ScrollPanel title={title} label={title} actions={pending.length>0&&<Pill tone="orange">{pending.length} waiting for the Owner</Pill>} footer={<FitNote text={why}/>}>
    <div className="table-wrap"><table><thead><tr><th>Staff</th><th>Change</th><th>Reason</th><th>Requested</th><th>Status</th><th></th></tr></thead><tbody>
      {[...pending,...done].map(r=><tr key={r.id}><td><strong>{name(r.staffId)}</strong><small>{r.id}</small></td><td>{change(r)}{effect(r)&&<small>{effect(r)}</small>}</td>
        <td>{r.reason}{r.note&&<small>{r.note}</small>}</td><td>{hrUserName(s,r.requestedBy)}<small>{r.requestedAt.slice(0,10)} {r.requestedAt.slice(11)}</small></td>
        <td><Pill tone={tone(r)}>{status(r)}</Pill>{r.status!=='Pending'&&!r.direct&&r.decidedBy&&<small>by {hrUserName(s,r.decidedBy)}</small>}</td>
        <td className="hr-actions">{r.status==='Pending'&&actions(r)}</td></tr>)}</tbody></table></div></ScrollPanel>;
}

// ── Leave (SCC LeaveDesk in full — Leave.tsx) ──
type LeaveView='desk'|'approvals'|'report'|'settings'|'holidays';
/** Shared month / as-of date (SCC currentMonth + getAsOf) for the desk and the report. */
function useLeaveDates(){const {s}=useHcc();const [ym,setYm]=useState(hrLeaveClampYm(s.date.slice(0,7)));const [asOf,setAsOf]=useState(s.date);return {ym,setYm,asOf,setAsOf};}
function Leave({phone}:{phone:boolean}){
  const {s,uid}=useHcc();const [view,setView]=useState<LeaveView>('desk');const d=useLeaveDates(),mgr=hrCanManage(s,uid),pending=hrPendingLeaves(s).length;
  const opts:{id:LeaveView;label:string;count?:number}[]=[{id:'desk',label:'Leave calendar'},...(mgr?[{id:'approvals' as LeaveView,label:'Approvals',count:pending||undefined}]:[]),
    {id:'report',label:'Monthly report'},{id:'settings',label:'Settings'},{id:'holidays',label:'Holidays'}];
  return <><ChipFilter label="Leave view" value={view} onChange={setView} options={opts}/>
    {view==='desk'?<LeaveDeskView phone={phone} {...d}/>:view==='approvals'?<LeaveApprovalsView phone={phone} asOf={d.asOf}/>:view==='report'?<LeaveReportView phone={phone} ym={d.ym} setYm={d.setYm}/>
      :view==='settings'?<LeaveSettingsView phone={phone} asOf={d.asOf}/>:<Holidays phone={phone}/>}</>;
}
/** Section names kept for the render tests: the approvals list, the SCC desk and the settings view. */
function LeaveRequests({phone}:{phone:boolean}){const {s}=useHcc();return <LeaveApprovalsView phone={phone} asOf={s.date}/>;}
function LeaveCalendar({phone}:{phone:boolean}){const d=useLeaveDates();return <LeaveDeskView phone={phone} {...d}/>;}
function LeavePolicy({phone}:{phone:boolean}){const {s}=useHcc();return <LeaveSettingsView phone={phone} asOf={s.date}/>;}

// ── DEC-083 paid-holiday calendar (the Owner keeps it; everyone with the HR desk sees it) ──
function Holidays({phone}:{phone:boolean}){
  const {s,uid,open,run,busy}=useHcc();const edit=hrCanRates(s,uid),list=hrPaidHolidays(s),fy=hrFy(s.date);
  const locked=(d:string)=>hrRunFor(s,d.slice(0,7))?.status==='Locked';
  const remove=(d:string,name:string)=>{if(window.confirm(`Remove ${name} (${shortDate(d)}) from the paid-holiday calendar?`))run({type:'HR_HOLIDAY_REMOVE',date:d},'Paid holiday removed');};
  const add=edit?<button className="btn primary" onClick={()=>open({type:'hr-holiday'})}><Plus size={phone?18:16}/> Add holiday</button>:null;
  const intro='Dated paid holidays (DEC-083): paid like a weekly off — monthly staff keep their pay; daily-wage staff as for weekly offs (CA to confirm). Not skipped inside leave dates (SCC: store holidays are blackout markers); staff who work that day are marked present. None are seeded: the Owner adds the hotel’s list (DEC-114: none).';
  const inFy=list.filter(h=>h.date>=fy.from&&h.date<=fy.to).length;
  if(phone)return <><p className="hr-m-note">{intro}</p>
    {list.length?list.map(h=><ListCard key={h.date} title={h.name} sub={`${day(h.date)} ${dateLabel(h.date)}`} right={locked(h.date)?<Pill>Locked month</Pill>:undefined}
      onClick={edit&&!locked(h.date)?()=>open({type:'hr-holiday',id:h.date}):undefined}/>):<p className="hr-m-note">No paid holidays yet.</p>}
    {add&&<StickyFooter>{add}</StickyFooter>}</>;
  return <ScrollPanel title="Paid holidays" label="Paid holidays" actions={add}
      footer={<><FitNote text={intro}/><FitNote text={`${inFy} in ${fy.label}.${edit?'':' Only the Owner changes the holiday calendar.'} The paid rows of the Holiday calendar (restricted holidays are listed there too):`}/><MstLink spec="holidays"/></>}>
    {list.length?<div className="table-wrap"><table><thead><tr><th>Date</th><th>Holiday</th><th>Pay month</th><th></th></tr></thead><tbody>
      {list.map(h=><tr key={h.date}><td><strong>{day(h.date)} {dateLabel(h.date)}</strong></td><td>{h.name}</td>
        <td>{hrMonthLabel(h.date.slice(0,7))}{locked(h.date)?<small>Locked — cannot change</small>:hrRunFor(s,h.date.slice(0,7))?<small>Draft pay-run — recalculate after a change</small>:null}</td>
        <td className="hr-actions">{edit&&!locked(h.date)&&<><button className="text-btn" onClick={()=>open({type:'hr-holiday',id:h.date})}>Rename</button>
          <button className="text-btn" disabled={busy} onClick={()=>remove(h.date,h.name)}>Remove</button></>}</td></tr>)}</tbody></table></div>
      :<Empty title="No paid holidays yet" body={edit?'Add the hotel’s holiday list for the year.':'The Owner adds the hotel’s holiday list.'}/>}
  </ScrollPanel>;
}

// ── Payroll ──
function downloadCsv(name:string,csv:string){void saveCsv(name,csv);}
function registerXlsx(s:ReturnType<typeof useHcc>['s'],r:HRRun){
  const head=['Staff ID','Name','Department','Payable days','Earned','Basic','HRA','Washing','Special','Overtime','Gross','PF (employee)','ESIC (employee)','PT','Advance','Deductions','Net pay','PF (employer)','ESIC (employer)'];
  const rows=r.lines.map(l=>[l.staffId,l.name,hrDeptName(s,l.dept),l.payableDays,...[l.earned,l.basic,l.hra,l.washing,l.special,l.ot,l.gross,l.pf,l.esic,l.pt,l.advance,l.deductions,l.net,l.pfEr,l.esicEr].map(p=>({paise:p}))]);
  const t=hrRunTotals(r);
  rows.push(['','TOTAL','','',...[t.earned,t.basic,null,null,null,t.ot,t.gross,t.pf,t.esic,t.pt,t.advance,t.deductions,t.net,t.pfEr,t.esicEr].map(p=>p===null?'':{paise:p})]);
  rows.push([`${r.status} · fictional data · ${HR_DISCLAIMER}`]);
  return buildXlsx([{name:'Payroll '+r.month,rows:[head,...rows],widths:[10,22,16,10,12,12,12,12,12,12,12,12,12,10,10,12,12,12,12]}]);
}
function Payroll({phone}:{phone:boolean}){
  const {s,uid,run,open,busy,toast,error}=useHcc();
  // N3 (production): the server's refusal of a pay run (e.g. "No staff to pay for this month.") stays on the tab, not only in the 6.5 s toast.
  const [refused,setRefused]=useState<string|undefined>();
  const months:string[]=[];for(let m=hrPrevMonth(s.date.slice(0,7)),i=0;i<6;i++,m=hrPrevMonth(m))months.push(m);
  const [ym,setYm]=useState(months.find(m=>!hrRunFor(s,m)||hrRunFor(s,m)!.status==='Draft')??months[0]);
  const r=hrRunFor(s,ym),t=r&&hrRunTotals(r),preview=!r&&hrMonthComplete(s,ym)?hrComputeRun(s,ym):undefined;
  const stale=r?.status==='Draft'&&(JSON.stringify(hrComputeRun(s,ym).lines)!==JSON.stringify(r.lines)||JSON.stringify(hrRatesFor(s,ym))!==JSON.stringify(r.rates));
  const own=r?.status==='Draft'&&r.preparedBy===uid,errors=r?.status==='Draft'?hrRunErrors(r):[];
  // DEC-075: corrections of this month waiting for the Owner block the lock; managers open the correction for an unmarked day from here.
  const fixWaiting=hrPendingRequests(s,'attendance').filter(q=>q.date?.startsWith(ym)).length,win=hrCorrectionWindow(s);
  const unmarked=hrCanManage(s,uid)&&r?.status!=='Locked'?(r?.lines??preview?.lines??[]).filter(l=>l.counts.unmarked>0).map(l=>{const st=hrSlice(s).staff.find(t=>t.id===l.staffId);
    const d=st&&st.userId!==uid?hrUnmarkedDates(s,st,ym).find(x=>x>=win.from):undefined;return d?{id:l.staffId,name:l.name,d}:undefined;}).filter((v):v is {id:string;name:string;d:string}=>!!v):[];
  const canUnlock=r?.status==='Locked'&&hrCanRates(s,uid)&&hrLastLockedMonth(s)===ym;
  // W4 G2: production downloads the files the server builds from its run snapshot (the loaded staff master holds MASKED account
  // numbers, so a browser-built bank file would be wrong); the prototype keeps building them here from its demo data.
  // (Inline in the branch so the prototype build drops the sync layer.)
  const file=(kind:'register'|'bank'|'statutory',issueNo?:number)=>{
    if(process.env.HCC_BUILD==='production'){void downloadServerPayrollFile(getSyncRuntime().transport,kind,r!,saveCsv,issueNo).then(name=>toast(`${name} saved`),
      e=>toast(`${kind==='bank'?'The bank file is recorded on the server but could not be downloaded':'The file could not be downloaded'}: ${errorText(e)}`,true));return;}
    const name={register:'payroll-register',bank:'bank-transfer',statutory:'statutory-pf-esic-pt'}[kind]+'-'+ym+'.csv';
    downloadCsv(name,kind==='register'?hrRegisterCsv(s,r!):kind==='bank'?hrBankCsv(s,r!):hrStatutoryCsv(s,r!));toast(`${name} saved (fictional data)`);};
  // F-09: the bank file is recorded; a repeat (also after an unlock) warns about paying twice.
  const issued=r?hrBankFilesIssued(r):[];
  const bankFile=async()=>{const again=issued.length>0;
    if(again&&!window.confirm(`A bank file for ${hrMonthLabel(ym)} was already generated on ${issued[issued.length-1].at} (net ${money(issued[issued.length-1].net)}). Paying a full file again may pay staff twice. Generate it again?`))return;
    if(await run({type:'HR_PAYROLL_BANK_FILE',id:r!.id,...(again?{confirmReissue:true}:{})},again?'Bank file generated again (reissue recorded)':'Bank file recorded'))file('bank',issued.length+1);};
  const xlsx=()=>{void saveBinaryFile(`payroll-register-${ym}.xlsx`,registerXlsx(s,r!),XLSX_MIME);toast('Payroll register Excel saved (fictional data)');};
  const runBtn=<button className={'btn'+(r?'':' primary')} disabled={busy||r?.status==='Locked'} onClick={()=>{const p=run({type:'HR_PAYROLL_RUN',month:ym},r?'Payroll recalculated':'Payroll draft prepared');
    if(process.env.HCC_BUILD==='production'){setRefused(undefined);void p.then(ok=>setRefused(ok?undefined:ym));}}}><Calculator size={16}/> {r?'Recalculate':'Run payroll'}</button>;
  const lockBtn=r?.status==='Draft'&&<button className="btn primary" disabled={busy||own||errors.length>0||fixWaiting>0} title={own?'Prepared by you — the other payroll user locks':fixWaiting?'Attendance corrections wait for the Owner':undefined} onClick={()=>{if(window.confirm(`Lock ${hrMonthLabel(ym)} payroll? Figures freeze, advance recoveries are posted and payslips are released.`))run({type:'HR_PAYROLL_LOCK',id:r.id},'Payroll locked · payslips released');}}><Lock size={16}/> Lock pay-run</button>;
  const status=r?<Pill tone={r.status==='Locked'?'green':'orange'}>{r.status}</Pill>:<Pill>Not run</Pill>;
  const meta=r?`Prepared by ${hrUserName(s,r.preparedBy)} ${r.preparedAt.slice(0,10)} ${r.preparedAt.slice(11)}${r.lockedBy?` · locked by ${hrUserName(s,r.lockedBy)}`:''} · rates from ${r.rates?.from??'—'}${r.legacy?' · legacy run':''}${r.unlocks?.length?` · unlocked ${r.unlocks.length}× (${r.unlocks[r.unlocks.length-1].reason})`:''}`:'';
  const notes=<>{process.env.HCC_BUILD==='production'&&refused===ym&&error&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>Payroll was not prepared. {error}</div>}
    {r&&!!r.warnings.length&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><div>{r.warnings.map(w=><div key={w}>{w}</div>)}</div></div>}
    {!!errors.length&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>Fix before locking: {errors.join('; ')}.</div>}
    {fixWaiting>0&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>{fixWaiting} attendance correction{fixWaiting===1?'':'s'} for {hrMonthLabel(ym)} {fixWaiting===1?'waits':'wait'} for the Owner — the pay-run is locked after {fixWaiting===1?'it is':'they are'} decided (Attendance › Past days).</div>}
    {unmarked.length>0&&<div className="info-note hr-fix" role="group" aria-label="Correct unmarked days"><Info size={16}/><div>Correct unmarked days (they count as absent): {unmarked.map(u=>
      <button key={u.id} type="button" className="text-btn" onClick={()=>open({type:'hr-att-correct',id:u.id,room:u.d})}>{u.name} · {shortDate(u.d)}</button>)}</div></div>}
    {stale&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>Attendance, leave, advances or rates changed after this draft — recalculate before locking.</div>}
    {r&&issued.length>0&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>A bank file for this month was already generated {issued.length}× (last {issued[issued.length-1].at}, net {money(issued[issued.length-1].net)}){r.status==='Draft'?' before the unlock':''}. Do not pay a full file twice.</div>}
    {r&&r.lines.some(l=>hrPayslipMark(r,l.staffId)==='resend')&&<div className="info-note hr-warn"><AlertTriangle size={16}/>Re-send payslips: {r.lines.filter(l=>hrPayslipMark(r,l.staffId)==='resend').map(l=>l.name).join(', ')} received figures that changed after the unlock.</div>}
    {own&&<div className="info-note"><Lock size={16}/>You prepared this draft. Maker-checker: the other payroll user (Owner / Accountant) locks it.</div>}
    {!r&&!hrMonthComplete(s,ym)&&<div className="info-note">This month is still in progress.</div>}
    {preview&&<div className="info-note">Preview: {preview.lines.length} staff · estimated net {money(hrRunTotals(preview).net)}. Run payroll to create a draft.</div>}</>;
  const monthOpts=months.map(m=>({id:m,label:hrMonthLabel(m)}));
  if(phone)return <>
    <ChipFilter label="Month" value={ym} onChange={setYm} options={monthOpts}/>
    <section className="card padded hr-m-run"><div className="inline">{status}<span className="muted">{meta}</span></div>
      {t&&<div className="hr-kpis"><KpiCard label="Gross" value={money(t.gross)}/><KpiCard label="Net pay" value={money(t.net)} sub={`${t.staff} staff`}/><KpiCard label="PF + ESIC (staff)" value={money(t.pf+t.esic)}/><KpiCard label="Professional tax" value={money(t.pt)}/></div>}</section>
    <div className="info-note"><Info size={16}/>Running, locking, unlocking and the bank file are done on the tablet or desktop. Payslips can be opened here.</div>
    {notes}
    {r?.lines.map(l=><ListCard key={l.staffId} title={l.name} sub={`Net ${money(l.net)} · gross ${money(l.gross)}`} meta={`${l.payableDays} days · PF ${money(l.pf)} · ESIC ${money(l.esic)} · PT ${money(l.pt)}${l.advance?` · advance ${money(l.advance)}`:''}`} onClick={()=>open({type:'hr-payslip',id:r.id+'|'+l.staffId})}/>)}
    <p className="hr-m-note">{HR_DISCLAIMER}</p>
    {r?.status==='Locked'&&<StickyFooter><button className="btn primary" onClick={()=>file('register')}><Download size={18}/> Register CSV</button></StickyFooter>}</>;
  // FIT: month, status, Run / Lock / Unlock stay in the panel's fixed head (always visible); files, notes, register and footnotes scroll.
  return <ScrollPanel title="Monthly payroll" label="Monthly payroll register"
      actions={<div className="inline hr-pay-actions"><select aria-label="Payroll month" value={ym} onChange={e=>setYm(e.target.value)}>{monthOpts.map(o=><option key={o.id} value={o.id}>{o.label}{hrRunFor(s,o.id)?` · ${hrRunFor(s,o.id)!.status}`:''}</option>)}</select>{status}
        {r?.status!=='Locked'&&runBtn}{lockBtn}{canUnlock&&<button className="btn" disabled={busy} onClick={()=>open({type:'hr-unlock',id:r!.id})}><Unlock size={16}/> Unlock</button>}</div>}
      footer={<FitNote text={meta||'Runs only for a completed month. Draft → maker-checker lock → payslips released. Owner can unlock the latest month with a reason.'}/>}>
    {r&&<div className="inline padded hr-pay-files"><button className="btn" onClick={()=>file('register')}><Download size={16}/> Register CSV</button><button className="btn" onClick={xlsx}><FileSpreadsheet size={16}/> Register Excel</button>
      <button className="btn" disabled={busy||r.status!=='Locked'} title={r.status!=='Locked'?'Bank file after the pay-run is locked':undefined} onClick={()=>{void bankFile();}}><Landmark size={16}/> Bank transfer file{issued.length?` · issued ${issued.length}×`:''}</button>
      <button className="btn" onClick={()=>file('statutory')}><Download size={16}/> PF / ESIC / PT statement{r.status==='Draft'?' (draft)':''}</button></div>}
    <div className="padded hr-notes">{notes}</div>
    {r&&t?<div className="table-wrap"><table className="hr-register"><thead><tr><th>Staff</th><th className="number">Days</th><th className="number">Earned</th><th className="number">OT</th><th className="number">Gross</th><th className="number">PF</th><th className="number">ESIC</th><th className="number">PT</th><th className="number">Advance</th><th className="number">Net pay</th><th></th></tr></thead><tbody>
      {r.lines.map(l=><tr key={l.staffId}><td><strong>{l.name}</strong><small>{l.designation} · {l.payType==='monthly'?money(l.base)+'/mo':money(l.base)+'/day'}{l.structure==='B'?' · basic only':''}</small></td><td className="number">{l.payableDays}{l.days?.otDays?<small>+ {l.days.otDays} OT</small>:null}</td>
        {[l.earned,l.ot,l.gross,l.pf,l.esic,l.pt,l.advance].map((v,i)=><td key={i} className="number">{money(v)}</td>)}<td className="number"><strong>{money(l.net)}</strong></td>
        <td><button className="text-btn" onClick={()=>open({type:'hr-payslip',id:r.id+'|'+l.staffId})}><Printer size={13}/> Payslip{{sent:' · sent',resend:' · re-send (figures changed)',superseded:' · earlier copy superseded','':''}[hrPayslipMark(r,l.staffId)]}</button></td></tr>)}
      <tr className="hr-total"><td><strong>Total · {t.staff} staff</strong></td><td></td>{[t.earned,t.ot,t.gross,t.pf,t.esic,t.pt,t.advance,t.net].map((v,i)=><td key={i} className="number"><strong>{money(v)}</strong></td>)}<td></td></tr>
    </tbody></table></div>:<Empty title={`No payroll for ${hrMonthLabel(ym)} yet`} body="Run payroll to calculate a draft from attendance, leave, overtime and advances."/>}
    {t&&<p className="footnote padded">Employer contributions (not deducted): PF {money(t.pfEr)} · ESIC {money(t.esicEr)} · employer cost {money(t.ctc)}.</p>}
    <p className="footnote padded">{HR_DISCLAIMER} Net pay is paid by simulated bank transfer; nothing is posted to the cash drawer or folios.</p></ScrollPanel>;
}

// ── Advances ──
function Advances({phone}:{phone:boolean}){
  const {s,open}=useHcc();const x=hrSlice(s);const name=(id:string)=>x.staff.find(t=>t.id===id)?.name??id;
  const inst=(a:typeof x.advances[number])=>a.installment?`${money(a.installment)}/month`:'Full recovery';
  const add=<button className="btn primary" onClick={()=>open({type:'hr-advance'})}><Plus size={16}/> Record advance</button>;
  if(phone)return <>{x.advances.map(a=><ListCard key={a.id} title={name(a.staffId)} sub={`${money(a.amount)} · ${inst(a)}`} meta={`${shortDate(a.date)} · ${a.reason}`} right={hrAdvanceBalance(a)?<Pill tone="orange">Due {money(hrAdvanceBalance(a))}</Pill>:<Pill tone="green">Recovered</Pill>}/>)}
    {!x.advances.length&&<p className="hr-m-note">No advances.</p>}<StickyFooter>{add}</StickyFooter></>;
  return <ScrollPanel title="Salary advances" label="Salary advances" actions={add}
      footer={<FitNote text="Recovered at payroll lock (SCC instalment rule; blank = all at once), never beyond net pay. Full and final recovers the balance up to what the settlement covers; the rest stays open. Simulated bank transfer — no cash drawer effect."/>}>
    {x.advances.length?<div className="table-wrap"><table><thead><tr><th>Staff</th><th>Given</th><th className="number">Amount</th><th className="number">Monthly recovery</th><th className="number">Recovered</th><th className="number">Balance</th></tr></thead><tbody>
      {x.advances.map(a=><tr key={a.id}><td><strong>{name(a.staffId)}</strong><small>{a.id} · {a.reason}</small></td><td>{dateLabel(a.date)}<small>by {hrUserName(s,a.by)}{a.fromMonth?` · from ${hrMonthLabel(a.fromMonth)}`:''}</small></td><td className="number">{money(a.amount)}</td><td className="number">{inst(a)}</td>
        <td className="number">{money(a.recovered)}<small>{a.recoveries.map(r=>r.runId).join(', ')}</small></td><td className="number"><strong>{money(hrAdvanceBalance(a))}</strong></td></tr>)}</tbody></table></div>:<Empty title="No advances"/>}</ScrollPanel>;
}

// ── Full and final (SCC fnf) ──
function Fnf({phone}:{phone:boolean}){
  const {s,uid,open,run,busy}=useHcc();const x=hrSlice(s),all=x.fnf??[],done=all.filter(hrFnfSettled),drafts=all.filter(f=>!hrFnfSettled(f)),exited=x.staff.filter(t=>!t.active&&t.exitedOn);
  const pending=exited.filter(t=>!all.some(f=>f.staffId===t.id));
  const net=(n:number)=>n<0?`Recover ${money(-n)}`:`Pay ${money(n)}`;
  // DEC-074 / A04-06: the advance a settlement does not cover stays open (owed by the leaver).
  const open_=(f:typeof all[number])=>{const o=hrFnfAdvanceLeftOpen(f);return o?` · advance ${money(o)} left open`:'';};
  // F-12 maker-checker: the other payroll user confirms a draft (the Owner when additions exceed the limit).
  const adds=(f:typeof all[number])=>f.adjustments.filter(a=>a.kind==='add').reduce((n,a)=>n+a.amount,0);
  const why=(f:typeof all[number])=>f.by===uid?'Prepared by you — the other payroll user confirms':adds(f)>HR_FNF_OWNER_ABOVE&&!hrCanRates(s,uid)?`Additions above ${money(HR_FNF_OWNER_ABOVE)} — the Owner confirms`:'';
  const confirm=(f:typeof all[number])=>{if(window.confirm(`Confirm the full and final settlement of ${f.name} (${net(f.net)})? Only the part of the outstanding advance this settlement covers is marked recovered; the rest stays open. This cannot be undone.`))run({type:'HR_FNF_CONFIRM',id:f.id},'Full and final settlement confirmed');};
  const actions=(f:typeof all[number])=><>{why(f)?<span className="muted">{why(f)}</span>:<button className="btn small-btn primary" disabled={busy} onClick={()=>confirm(f)}><Check size={14}/> Confirm</button>}
    <button className="btn small-btn" disabled={busy} onClick={()=>run({type:'HR_FNF_DISCARD',id:f.id},'Draft settlement discarded')}><X size={14}/> Discard</button></>;
  if(phone)return <>
    <h2 className="m-group">TO SETTLE</h2>
    {pending.length?pending.map(t=><ListCard key={t.id} title={t.name} sub={`Exited ${shortDate(t.exitedOn!)} · ${t.designation}`} right={<Pill tone="orange">Not settled</Pill>} onClick={()=>open({type:'hr-fnf',id:t.id})}/>):<p className="hr-m-note">Nobody waiting. Mark an exit in the Staff section first.</p>}
    {drafts.length>0&&<><h2 className="m-group">AWAITING CONFIRMATION</h2>{drafts.map(f=><div key={f.id} className="hr-m-leave"><ListCard title={f.name} sub={`${net(f.net)} · last day ${shortDate(f.lastDay)}`} meta={`${f.id} · prepared by ${hrUserName(s,f.by)}${open_(f)}`} right={<Pill tone="orange">Draft</Pill>}/><div className="hr-m-actions">{actions(f)}</div></div>)}</>}
    <h2 className="m-group">SETTLED</h2>
    {done.map(f=><ListCard key={f.id} title={f.name} sub={`${net(f.net)} · last day ${shortDate(f.lastDay)}`} meta={`${f.id} · by ${hrUserName(s,f.by)}${open_(f)}`} right={<Pill tone="green">Settled</Pill>}/>)}
    {!done.length&&<p className="hr-m-note">No settlements yet.</p>}</>;
  return <ScrollPanel title="Full and final settlement" label="Full and final settlement"
      footer={<FitNote text="For staff marked as exited: last month’s salary to the exit date, PF / ESIC / PT, the outstanding advance (as far as the settlement covers it — the rest stays open) and up to four adjustments (e.g. earned-leave encashment)."/>}>
    {pending.length||all.length?<div className="table-wrap"><table><thead><tr><th>Staff</th><th>Last day</th><th className="number">Earnings</th><th className="number">Recoveries</th><th className="number">Net</th><th>Status</th><th></th></tr></thead><tbody>
      {pending.map(t=><tr key={t.id}><td><strong>{t.name}</strong><small>{t.id} · {t.designation}</small></td><td>{dateLabel(t.exitedOn!)}</td><td className="number">—</td><td className="number">—</td><td className="number">—</td><td><Pill tone="orange">Not settled</Pill></td>
        <td><button className="text-btn" onClick={()=>open({type:'hr-fnf',id:t.id})}>Prepare settlement</button></td></tr>)}
      {drafts.map(f=><tr key={f.id}><td><strong>{f.name}</strong><small>{f.id}</small></td><td>{dateLabel(f.lastDay)}</td><td className="number">{money(f.earnings)}</td><td className="number">{money(f.recoveries)}</td>
        <td className="number"><strong>{net(f.net)}</strong>{!!hrFnfAdvanceLeftOpen(f)&&<small>{open_(f).slice(3)}</small>}</td><td><Pill tone="orange">Draft</Pill><small>prepared by {hrUserName(s,f.by)}</small></td><td className="hr-actions">{actions(f)}</td></tr>)}
      {done.map(f=><tr key={f.id}><td><strong>{f.name}</strong><small>{f.id}{f.salaryIncluded?'':` · ${hrMonthLabel(f.month)} paid in payroll`}</small></td><td>{dateLabel(f.lastDay)}</td><td className="number">{money(f.earnings)}</td><td className="number">{money(f.recoveries)}</td>
        <td className="number"><strong>{net(f.net)}</strong>{!!hrFnfAdvanceLeftOpen(f)&&<small>{open_(f).slice(3)}</small>}</td><td><Pill tone="green">Settled</Pill></td><td><small>by {hrUserName(s,f.by)} · {f.at.slice(0,10)}{f.confirmedBy?` · confirmed by ${hrUserName(s,f.confirmedBy)}`:''}</small></td></tr>)}
    </tbody></table></div>:<Empty title="Nobody to settle" body="Mark a staff member as exited on the Staff tab first."/>}
    <p className="footnote padded">{HR_DISCLAIMER} {HR_NOT_CALCULATED}</p></ScrollPanel>;
}

// ── Statutory rates (dated, from SCC) ──
function Rates({phone}:{phone:boolean}){
  const {s,uid,open}=useHcc();const edit=hrCanRates(s,uid),ym=s.date.slice(0,7),now=hrRatesFor(s,ym),versions=hrRateVersions(s);
  const defs=hrSettingDefs().filter(d=>d.category==='Statutory rates');
  const usedBy=(from:string)=>hrSlice(s).runs.filter(r=>r.status==='Locked'&&r.rates.from===from).map(r=>hrMonthLabel(r.month).split(' ')[0].slice(0,3)).join(', ');
  const btn=edit&&!phone?<button className="btn primary" onClick={()=>open({type:'hr-rates'})}><Settings2 size={16}/> New rate version</button>:null;
  const disclaimer=<p className="hr-disclaimer"><AlertTriangle size={14}/> Illustrative — CA to confirm for a hotel. Ported from SCC; in force for {hrMonthLabel(ym)}: version from {now.from}. A new version (here or in Master configuration) applies from 1 {hrMonthLabel(hrRatesDefaultFrom(s).slice(0,7))} unless you choose another month.</p>;
  // FIT (desktop): the disclaimer, the rates in force, the versions table and the notes scroll in one panel; "New rate version" stays in its head.
  if(!phone)return <ScrollPanel title="Statutory rates" label="Statutory rates and versions" actions={btn}
      footer={<><FitNote text={HR_SCC_DAYS}/>{!edit&&<FitNote text="Only the Owner adds a rate version (DEC-058). Locked pay-runs keep the rates they were calculated with."/>}</>}>
    <div className="padded hr-rates-intro">{disclaimer}</div>
    <div className="summary-list padded">{defs.map(d=><div key={d.key}><span>{d.label}</span><strong>{hrSettingValue(s,d.key)}</strong></div>)}</div>
    <h3 className="padded hr-sub">Versions</h3>
    <div className="table-wrap"><table><thead><tr><th>Effective from</th><th>PF</th><th>ESIC</th><th>PT (men / women)</th><th>Overtime</th><th>Rounding</th><th>Locked runs</th></tr></thead><tbody>
      {versions.map(v=><tr key={v.from}><td><strong>{dateLabel(v.from)}</strong>{v.from===now.from&&<small>In force now</small>}</td><td>{v.pfEeBps/100}% + {v.pfErBps/100}% · cap {money(v.pfWageCap)}</td><td>{v.esicEeBps/100}% + {v.esicErBps/100}% · ≤ {money(v.esicCeiling)}{v.esicPeriod?' · per contribution period':' · month by month'}</td>
        <td>{money(v.ptStd)} (Feb {money(v.ptFeb)}) · women exempt ≤ {money(v.ptFemaleExempt)}</td><td>{v.otBps/10000}× per OT day{v.daysOverride?` · ${v.daysOverride}-day month`:''}</td><td>{v.rounding==='paise'?'Paise':'Rupee'}</td><td>{usedBy(v.from)||'—'}</td></tr>)}</tbody></table></div>
  </ScrollPanel>;
  return <section className={phone?'':'card'}><div className={phone?'':'section-title'}><div><h2>Statutory rates</h2><p className="hr-disclaimer"><AlertTriangle size={14}/> Illustrative — CA to confirm for a hotel. Ported from SCC; in force for {hrMonthLabel(ym)}: version from {now.from}. A new version (here or in Master configuration) applies from 1 {hrMonthLabel(hrRatesDefaultFrom(s).slice(0,7))} unless you choose another month.</p></div>{btn}</div>
    <div className={'summary-list'+(phone?' hr-m-rates':' padded')}>{defs.map(d=><div key={d.key}><span>{d.label}</span><strong>{hrSettingValue(s,d.key)}</strong></div>)}</div>
    {phone?<div className="info-note"><Info size={16}/>Rate versions are added on the tablet or desktop (Owner).</div>:<>
      <h3 className="padded hr-sub">Versions</h3>
      <div className="table-wrap"><table><thead><tr><th>Effective from</th><th>PF</th><th>ESIC</th><th>PT (men / women)</th><th>Overtime</th><th>Rounding</th><th>Locked runs</th></tr></thead><tbody>
        {versions.map(v=><tr key={v.from}><td><strong>{dateLabel(v.from)}</strong>{v.from===now.from&&<small>In force now</small>}</td><td>{v.pfEeBps/100}% + {v.pfErBps/100}% · cap {money(v.pfWageCap)}</td><td>{v.esicEeBps/100}% + {v.esicErBps/100}% · ≤ {money(v.esicCeiling)}{v.esicPeriod?' · per contribution period':' · month by month'}</td>
          <td>{money(v.ptStd)} (Feb {money(v.ptFeb)}) · women exempt ≤ {money(v.ptFemaleExempt)}</td><td>{v.otBps/10000}× per OT day{v.daysOverride?` · ${v.daysOverride}-day month`:''}</td><td>{v.rounding==='paise'?'Paise':'Rupee'}</td><td>{usedBy(v.from)||'—'}</td></tr>)}</tbody></table></div></>}
    <p className="footnote padded">{HR_SCC_DAYS}</p>
    {!edit&&<p className="footnote padded">Only the Owner adds a rate version (DEC-058). Locked pay-runs keep the rates they were calculated with.</p>}</section>;
}
export const HrDeskMobile=()=><HrDesk phone/>;
/** Section components, exported for render tests. */
export const HR_SECTIONS={People,Roster,Attendance,PastDays,Leave,LeaveRequests,LeaveCalendar,LeavePolicy,Holidays,Payroll,Advances,Fnf,Rates};
