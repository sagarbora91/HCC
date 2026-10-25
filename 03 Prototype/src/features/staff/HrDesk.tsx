// Module 17 · HR desk (page 'staff'): staff master, roster, attendance, leave (requests, calendar, policy), payroll, advances, full and final, statutory rates.
// Tabs by role: managers (approve) see people/roster/attendance/leave without salaries; Owner/Accountant see payroll.
// Anyone else who opens the page sees their own "My HR" view. Desktop renders tables; the phone renders cards (phone=true).
// W05-B: SCC payroll rules (PF / ESIC / MH PT, lock + Owner unlock, bank and statutory files) and SCC leave rules (FY entitlements,
// half-day, max staff off per department per day, leave calendar). Payroll run and statutory rates are desktop/tablet tasks:
// the phone shows a read-only summary (Wave 0.5 placement §2).
import {saveCsv,saveBinaryFile} from '../../app/native';
import {buildXlsx,XLSX_MIME} from '../../app/xlsx';
import React,{useState} from 'react';
import {UserPlus,ChevronLeft,ChevronRight,Copy,Download,Lock,Unlock,Calculator,Printer,Plus,Settings2,AlertTriangle,Check,X,Landmark,FileSpreadsheet,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {ListCard,KpiCard,ChipFilter,StickyFooter} from '../../app/mobile/components';
import {
  money,addDays,isDayClosed,hrSlice,hrCanManage,hrCanPay,hrCanRates,hrStaffOfUser,hrShiftFor,hrShiftInfo,hrMonday,hrWeek,hrKey,hrDayStatus,hrCounts,
  hrDeptName,hrDecidable,hrRunFor,hrRunTotals,hrMonthLabel,hrPrevMonth,hrNextMonth,hrMonthComplete,hrRegisterCsv,hrBankCsv,hrStatutoryCsv,hrAdvanceBalance,hrComputeRun,
  hrRateVersions,hrRatesFor,hrLeavePolicy,hrDeptCap,hrLeaveCalendar,hrRunErrors,hrLastLockedMonth,hrSettingDefs,hrSettingValue,hrWeekday,
  HR_ATT_LABEL,HR_LEAVE_LABEL,HR_WEEKDAYS,HR_DISCLAIMER,type HRStaff,type HRRun,type HRLeave,
} from '../../services/mock';
import {MyHr,hrLeaveTone,hrAttTone} from './MyHr';
import {hrUserName} from './modals';

type Tab='people'|'roster'|'attendance'|'leave'|'payroll'|'advances'|'fnf'|'settings';
const TAB_LABEL:Record<Tab,string>={people:'Staff',roster:'Roster',attendance:'Attendance',leave:'Leave',payroll:'Payroll',advances:'Advances',fnf:'Full and final',settings:'Statutory rates'};
const day=(d:string)=>HR_WEEKDAYS[new Date(d+'T12:00:00Z').getUTCDay()];
/** "CL · ½ (first half)" style label. */
export const hrLeaveKind=(l:HRLeave)=>HR_LEAVE_LABEL[l.type]+(l.half?` · ½ day (${l.half==='AM'?'first':'second'} half)`:'');

export function HrDesk({phone=false}:{phone?:boolean}){
  const {s,uid}=useHcc();const mgr=hrCanManage(s,uid),pay=hrCanPay(s,uid);
  const tabs:Tab[]=[...(mgr||pay?['people','roster','attendance','leave'] as Tab[]:[]),...(pay?['payroll','advances','fnf','settings'] as Tab[]:[])];
  const [tab,setTab]=useState<Tab>(tabs[0]??'people');
  if(!tabs.length)return phone?<MyHr phone/>:<MyHr/>;
  const pending=hrDecidable(s,uid).length;
  const body={people:<People phone={phone}/>,roster:<Roster phone={phone}/>,attendance:<Attendance phone={phone}/>,leave:<Leave phone={phone}/>,payroll:<Payroll phone={phone}/>,advances:<Advances phone={phone}/>,fnf:<Fnf phone={phone}/>,settings:<Rates phone={phone}/>}[tab];
  if(phone)return <div className="hr-m"><ChipFilter label="HR section" value={tab} onChange={setTab} options={tabs.map(t=>({id:t,label:TAB_LABEL[t],count:t==='leave'&&pending?pending:undefined}))}/>{body}</div>;
  return <><PageTitle title="Staff & payroll" sub={`Fictional staff · business date ${dateLabel(s.date)} · ${pay?'PF / ESIC / PT from SCC — illustrative, CA to confirm for a hotel':'salary figures are visible to the Owner and Accountant only'}`}/>
    <div className="card hr-tabs-card"><div className="tabs" role="tablist">{tabs.map(t=><button key={t} role="tab" aria-selected={tab===t} className={tab===t?'active':''} onClick={()=>setTab(t)}>{TAB_LABEL[t]}{t==='leave'&&pending>0&&<span>{pending}</span>}</button>)}</div></div>
    {body}</>;
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
  if(phone)return <>
    <div className="hr-kpis"><KpiCard label="Active staff" value={active.length}/><KpiCard label="Present today" value={present} sub={`${onLeave} on leave`}/></div>
    <ChipFilter label="Department" value={dept} onChange={setDept} options={deptOpts}/>
    {list.map(st=><ListCard key={st.id} title={st.name} sub={`${st.designation} · ${hrDeptName(s,st.dept)}`} meta={`${hrShiftInfo(st.defaultShift).label} · off ${HR_WEEKDAYS[st.weeklyOff]}${st.userId?' · login '+hrUserName(s,st.userId):' · no login'}${pay?' · '+payOf(st):''}`}
      right={st.active?undefined:<Pill>Exited</Pill>} onClick={mgr||pay?()=>open({type:'hr-staff',id:st.id}):undefined}/>)}
    {(mgr||pay)&&<StickyFooter><button className="btn primary" onClick={()=>open({type:'hr-staff'})}><UserPlus size={18}/> Add staff member</button></StickyFooter>}</>;
  return <>
    <div className="small-stats"><div><span>Active staff</span><strong>{active.length}</strong></div><div><span>Present today</span><strong>{present}</strong></div><div><span>On leave today</span><strong>{onLeave}</strong></div><div><span>Pending leave</span><strong>{x.leaves.filter(l=>l.status==='Pending').length}</strong></div></div>
    <section className="card"><div className="section-title"><div className="filters">{deptOpts.map(o=><button key={o.id} className={dept===o.id?'active':''} onClick={()=>setDept(o.id)}>{o.label}{o.count!==undefined?` (${o.count})`:''}</button>)}</div>
      {(mgr||pay)&&<button className="btn primary" onClick={()=>open({type:'hr-staff'})}><UserPlus size={16}/> Add staff member</button>}</div>
      <div className="table-wrap"><table><thead><tr><th>Staff member</th><th>Department</th><th>Demo login</th><th>Default shift · weekly off</th>{pay&&<th className="number">Pay</th>}<th>Status</th><th></th></tr></thead><tbody>
        {list.map(st=><tr key={st.id}><td><strong>{st.name}</strong><small>{st.id} · {st.designation}</small></td><td>{hrDeptName(s,st.dept)}</td><td>{st.userId?hrUserName(s,st.userId):<span className="muted">No login</span>}</td>
          <td>{hrShiftInfo(st.defaultShift).label} {hrShiftInfo(st.defaultShift).start}–{hrShiftInfo(st.defaultShift).end} · {HR_WEEKDAYS[st.weeklyOff]}</td>
          {pay&&<td className="number">{payOf(st)}</td>}<td>{st.active?<Pill tone="green">Active</Pill>:<Pill>Exited {st.exitedOn&&shortDate(st.exitedOn)}</Pill>}</td>
          <td className="hr-actions">{(mgr||pay)&&<button className="text-btn" onClick={()=>open({type:'hr-staff',id:st.id})}>Edit</button>}{mgr&&<button className="text-btn" disabled={busy} onClick={()=>toggle(st)}>{st.active?'Exit':'Reactivate'}</button>}</td></tr>)}
      </tbody></table></div></section></>;
}

// ── Roster ──
function Roster({phone}:{phone:boolean}){
  const {s,uid,open,run,busy}=useHcc();const mgr=hrCanManage(s,uid);const [mon,setMon]=useState(hrMonday(s.date));
  const week=hrWeek(mon),staff=hrSlice(s).staff.filter(t=>t.active);
  const [pick,setPick]=useState(week.includes(s.date)?s.date:week[0]);const pd=week.includes(pick)?pick:week[0];
  const nav=<div className="inline hr-weeknav"><button className="btn" aria-label="Previous week" onClick={()=>setMon(addDays(mon,-7))}><ChevronLeft size={16}/></button><strong>{shortDate(week[0])} – {shortDate(week[6])}</strong><button className="btn" aria-label="Next week" onClick={()=>setMon(addDays(mon,7))}><ChevronRight size={16}/></button></div>;
  const copy=mgr&&addDays(mon,13)>=s.date?<button className="btn" disabled={busy} onClick={()=>run({type:'HR_ROSTER_COPY',from:mon},'Roster copied to the following week')}><Copy size={16}/> Copy to next week</button>:null;
  const cell=(st:HRStaff,d:string)=>{const c=hrShiftFor(s,st,d),h=hrShiftInfo(c),over=!!hrSlice(s).roster[hrKey(d,st.id)],edit=mgr&&d>=s.date;
    const label=<><b>{c==='OFF'?'Off':h.label}</b>{c!=='OFF'&&<small>{h.start}</small>}</>;
    return edit?<button className={'hr-cell hr-s-'+c+(over?' hr-over':'')} aria-label={`${st.name} ${day(d)} ${shortDate(d)}: ${h.label}. Change`} onClick={()=>open({type:'hr-roster',id:st.id,room:d})}>{label}</button>
      :<span className={'hr-cell hr-s-'+c+(over?' hr-over':'')}>{label}</span>;};
  if(phone)return <>{nav}
    <ChipFilter label="Day" value={pd} onChange={setPick} options={week.map(d=>({id:d,label:`${day(d)} ${d.slice(8)}`}))}/>
    {staff.map(st=>{const c=hrShiftFor(s,st,pd),h=hrShiftInfo(c);return <ListCard key={st.id} title={st.name} sub={c==='OFF'?'Weekly off':`${h.label} ${h.start}–${h.end}`} meta={hrDeptName(s,st.dept)}
      right={hrSlice(s).roster[hrKey(pd,st.id)]?<Pill tone="blue">Changed</Pill>:undefined} onClick={mgr&&pd>=s.date?()=>open({type:'hr-roster',id:st.id,room:pd}):undefined}/>;})}
    {copy&&<StickyFooter>{copy}</StickyFooter>}</>;
  return <section className="card"><div className="section-title"><div><h2>Weekly roster</h2><p>{mgr?'Select a cell to change the shift. Past days are history and cannot change.':'Read-only view.'} Outlined cells differ from the default pattern.</p></div><div className="inline">{nav}{copy}</div></div>
    <div className="table-wrap"><table className="hr-roster"><thead><tr><th>Staff</th>{week.map(d=><th key={d} className={d===s.date?'hr-th-today':''}>{day(d)} {d.slice(8)}</th>)}</tr></thead><tbody>
      {staff.map(st=><tr key={st.id}><td><strong>{st.name}</strong><small>{hrDeptName(s,st.dept)}</small></td>{week.map(d=><td key={d}>{cell(st,d)}</td>)}</tr>)}</tbody></table></div>
    <p className="footnote padded">Shifts: Morning 07:00–15:00 · Evening 15:00–23:00 · Night 23:00–07:00 · General 09:00–18:00. Late = check-in after start + 15 min.</p></section>;
}

// ── Attendance ──
function Attendance({phone}:{phone:boolean}){
  const {s,uid,open}=useHcc();const mgr=hrCanManage(s,uid),closed=isDayClosed(s);const [view,setView]=useState<'today'|'month'>('today');
  const staff=hrSlice(s).staff.filter(t=>t.active),ym=s.date.slice(0,7);
  const row=(st:HRStaff)=>{const x=hrDayStatus(s,st,s.date),sh=hrShiftInfo(hrShiftFor(s,st,s.date));
    const status=x.kind==='att'?<Pill tone={hrAttTone(x.att!.status)}>{HR_ATT_LABEL[x.att!.status]}</Pill>:x.kind==='leave'?<Pill tone="blue">On leave · {x.leave!.type}</Pill>:x.kind==='off'?<Pill>Weekly off</Pill>:<Pill tone="orange">Not marked</Pill>;
    const can=mgr&&!closed&&x.kind!=='leave';return {x,sh,status,can};};
  const toggle=<ChipFilter label="Attendance view" value={view} onChange={setView} options={[{id:'today',label:`Today · ${shortDate(s.date)}`},{id:'month',label:`${hrMonthLabel(ym)} to date`}]}/>;
  if(view==='month'){
    const rows=staff.map(st=>({st,c:hrCounts(s,st,ym,addDays(s.date,-1))}));
    return <>{toggle}{phone?rows.map(({st,c})=><ListCard key={st.id} title={st.name} sub={`P ${c.P} · Late ${c.L} · Half ${c.H} · Absent ${c.A}`} meta={`Leave ${c.paidLeave+c.lopLeave} · Off ${c.off} · OT ${Math.round(c.otMin/6)/10} h${c.unmarked?` · ${c.unmarked} unmarked`:''}`}/>)
      :<section className="card"><div className="section-title"><h2>{hrMonthLabel(ym)} · 1 – {shortDate(addDays(s.date,-1))}</h2><span className="muted">Closed days only; today is on the Today view</span></div>
        <div className="table-wrap"><table><thead><tr><th>Staff</th><th className="number">Present</th><th className="number">Late</th><th className="number">Half-day</th><th className="number">Absent</th><th className="number">Paid leave</th><th className="number">LOP leave</th><th className="number">Off</th><th className="number">Unmarked</th><th className="number">Overtime</th></tr></thead><tbody>
          {rows.map(({st,c})=><tr key={st.id}><td><strong>{st.name}</strong><small>{st.designation}</small></td>{[c.P,c.L,c.H,c.A,c.paidLeave,c.lopLeave,c.off,c.unmarked].map((v,i)=><td key={i} className="number">{v}</td>)}<td className="number">{Math.round(c.otMin/6)/10} h</td></tr>)}</tbody></table></div></section>}</>;
  }
  if(phone)return <>{toggle}{closed&&<div className="info-note">Business day closed — attendance is read-only.</div>}
    {staff.map(st=>{const r=row(st);return <ListCard key={st.id} title={st.name} sub={r.sh.code==='OFF'?'Weekly off':`${r.sh.label} ${r.sh.start}–${r.sh.end}`}
      meta={r.x.att?`${r.x.att.at.slice(11)}${r.x.att.otMin?` · OT ${r.x.att.otMin} min`:''}${r.x.att.note?' · '+r.x.att.note:''}`:undefined} right={r.status} onClick={r.can?()=>open({type:'hr-att',id:st.id}):undefined}/>;})}</>;
  return <>{toggle}<section className="card"><div className="section-title"><div><h2>Attendance register · {dateLabel(s.date)}</h2><p>{closed?'Business day closed — read-only.':mgr?'Mark or correct today. Staff with a login can also check themselves in from My shifts & pay.':'Read-only view.'}</p></div></div>
    <div className="table-wrap"><table><thead><tr><th>Staff</th><th>Rostered</th><th>Status</th><th>Recorded</th><th className="number">Overtime</th><th></th></tr></thead><tbody>
      {staff.map(st=>{const r=row(st);return <tr key={st.id}><td><strong>{st.name}</strong><small>{hrDeptName(s,st.dept)}</small></td><td>{r.sh.code==='OFF'?'Weekly off':`${r.sh.label} ${r.sh.start}–${r.sh.end}`}</td><td>{r.status}</td>
        <td>{r.x.att?<>{r.x.att.at.slice(11)} · {hrUserName(s,r.x.att.by)}{r.x.att.note&&<small>{r.x.att.note}</small>}</>:'—'}</td><td className="number">{r.x.att?.otMin?`${r.x.att.otMin} min`:'—'}</td>
        <td>{r.can&&<button className="text-btn" onClick={()=>open({type:'hr-att',id:st.id})}>{r.x.att?'Correct':'Mark'}</button>}</td></tr>;})}</tbody></table></div></section></>;
}

// ── Leave ──
function LeaveRequests({phone}:{phone:boolean}){
  const {s,uid,open,run,busy}=useHcc();const x=hrSlice(s),mgr=hrCanManage(s,uid),me=hrStaffOfUser(s,uid);
  const decidable=new Set(hrDecidable(s,uid).map(l=>l.id));
  const pending=x.leaves.filter(l=>l.status==='Pending'),history=x.leaves.filter(l=>l.status!=='Pending').sort((a,b)=>b.from.localeCompare(a.from));
  const name=(id:string)=>x.staff.find(t=>t.id===id)?.name??id;
  const dates=(l:{from:string;to:string})=>shortDate(l.from)+(l.to!==l.from?' – '+shortDate(l.to):'');
  const why=(l:typeof pending[number])=>decidable.has(l.id)?'':!mgr?'Managers decide':l.requestedBy===uid?'Your own request — another manager decides':l.staffId===me?.id?'Your own leave — another manager decides':'';
  const decide=(id:string)=><><button className="btn small-btn primary" disabled={busy} onClick={()=>open({type:'hr-leave-decide',id,room:'approve'})}><Check size={14}/> Approve</button><button className="btn small-btn" disabled={busy} onClick={()=>open({type:'hr-leave-decide',id,room:'reject'})}><X size={14}/> Reject</button></>;
  const cancelable=(l:typeof history[number])=>mgr&&l.status==='Approved'&&l.from>=s.date;
  if(phone)return <>
    <h2 className="m-group">AWAITING DECISION</h2>
    {pending.length?pending.map(l=><div key={l.id} className="hr-m-leave"><ListCard title={`${name(l.staffId)} · ${l.type}${l.half?' ½':''} ${l.days}d`} sub={`${dates(l)} · ${l.reason}`} meta={`Requested by ${hrUserName(s,l.requestedBy)}${why(l)?' · '+why(l):''}`}/>
      {decidable.has(l.id)&&<div className="hr-m-actions">{decide(l.id)}</div>}</div>):<p className="hr-m-note">No pending requests.</p>}
    <h2 className="m-group">HISTORY</h2>
    {history.map(l=><ListCard key={l.id} title={`${name(l.staffId)} · ${hrLeaveKind(l)}`} sub={`${dates(l)} · ${l.days} day${l.days===1?'':'s'}`} meta={l.decidedBy?`${l.status} by ${hrUserName(s,l.decidedBy)}${l.note?' · '+l.note:''}`:undefined} right={<Pill tone={hrLeaveTone(l.status)}>{l.status}</Pill>}
      onClick={cancelable(l)?()=>{if(window.confirm('Cancel this approved leave?'))run({type:'HR_LEAVE_CANCEL',id:l.id},'Leave cancelled');}:undefined}/>)}
    {mgr&&<StickyFooter><button className="btn primary" onClick={()=>open({type:'hr-leave'})}><Plus size={18}/> Leave for a staff member</button></StickyFooter>}</>;
  return <>
    <section className="card"><div className="section-title"><div><h2>Awaiting decision</h2><p>No self-approval: the requester and the person on leave cannot decide.</p></div>{mgr&&<button className="btn primary" onClick={()=>open({type:'hr-leave'})}><Plus size={16}/> Leave for a staff member</button>}</div>
      {pending.length?<div className="table-wrap"><table><thead><tr><th>Staff</th><th>Leave</th><th>Dates</th><th>Requested by</th><th></th></tr></thead><tbody>{pending.map(l=><tr key={l.id}>
        <td><strong>{name(l.staffId)}</strong><small>{l.id}</small></td><td>{hrLeaveKind(l)}<small>{l.reason}</small></td><td>{dates(l)}<small>{l.days} working day{l.days===1?'':'s'}</small></td><td>{hrUserName(s,l.requestedBy)}<small>{l.requestedAt.slice(0,10)} {l.requestedAt.slice(11)}</small></td>
        <td className="hr-actions">{decidable.has(l.id)?decide(l.id):<span className="muted">{why(l)}</span>}</td></tr>)}</tbody></table></div>:<Empty title="No pending leave" body="New requests from staff appear here."/>}</section>
    <section className="card"><div className="section-title"><h2>Leave history</h2></div>
      <div className="table-wrap"><table><thead><tr><th>Staff</th><th>Leave</th><th>Dates</th><th>Status</th><th>Decided by</th><th></th></tr></thead><tbody>{history.map(l=><tr key={l.id}>
        <td><strong>{name(l.staffId)}</strong><small>{l.id}</small></td><td>{hrLeaveKind(l)}<small>{l.reason}</small></td><td>{dates(l)}<small>{l.days} day{l.days===1?'':'s'}</small></td><td><Pill tone={hrLeaveTone(l.status)}>{l.status}</Pill></td>
        <td>{l.decidedBy?hrUserName(s,l.decidedBy):'—'}{l.note&&<small>{l.note}</small>}</td><td>{cancelable(l)&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'HR_LEAVE_CANCEL',id:l.id},'Leave cancelled')}>Cancel leave</button>}</td></tr>)}</tbody></table></div></section></>;
}


function Leave({phone}:{phone:boolean}){
  const [view,setView]=useState<'requests'|'calendar'|'policy'>('requests');
  return <><ChipFilter label="Leave view" value={view} onChange={setView} options={[{id:'requests',label:'Requests'},{id:'calendar',label:'Leave calendar'},{id:'policy',label:'Policy'}]}/>
    {view==='requests'?<LeaveRequests phone={phone}/>:view==='calendar'?<LeaveCalendar phone={phone}/>:<LeavePolicy phone={phone}/>}</>;
}

// ── Leave calendar (SCC renderCalendar: who is off each day, capacity per day — per department here) ──
function LeaveCalendar({phone}:{phone:boolean}){
  const {s}=useHcc();const [ym,setYm]=useState(s.date.slice(0,7));const cal=hrLeaveCalendar(s,ym);
  const first=(n:string)=>n.split(' ')[0];
  const nav=<div className="inline hr-weeknav"><button className="btn" aria-label="Previous month" onClick={()=>setYm(hrPrevMonth(ym))}><ChevronLeft size={16}/></button><strong>{hrMonthLabel(ym)}</strong><button className="btn" aria-label="Next month" onClick={()=>setYm(hrNextMonth(ym))}><ChevronRight size={16}/></button></div>;
  const chip=(i:typeof cal[number]['items'][number])=><span key={i.leave.id} className={'hr-cal-chip'+(i.leave.status==='Pending'?' pending':'')} title={`${i.staff.name} · ${hrLeaveKind(i.leave)} · ${i.leave.status}`}>
    {first(i.staff.name)} · {i.leave.type}{i.leave.half?' ½':''}{i.leave.status==='Pending'?' (pending)':''}</span>;
  const days=cal.filter(d=>d.items.length);
  if(phone)return <>{nav}
    {days.length?days.map(d=><ListCard key={d.date} tone={d.date===s.date?'hr-today':''} title={`${day(d.date)} ${shortDate(d.date)}`} sub={d.items.map(i=>`${first(i.staff.name)} ${i.leave.type}${i.leave.half?' ½':''}${i.leave.status==='Pending'?' (pending)':''}`).join(' · ')}
      meta={d.depts.map(q=>`${q.name} ${q.count}/${q.cap}`).join(' · ')} right={d.full?<Pill tone="orange">Limit reached</Pill>:undefined}/>):<p className="hr-m-note">No leave in {hrMonthLabel(ym)}.</p>}</>;
  const lead=(hrWeekday(ym+'-01')+6)%7;
  return <section className="card"><div className="section-title"><div><h2>Leave calendar</h2><p>Pending requests hold a place (outlined). Weekly offs are not shown. A day reaches its limit when a department has its maximum staff off.</p></div>{nav}</div>
    <div className="hr-cal" role="grid" aria-label={`Leave calendar ${hrMonthLabel(ym)}`}>
      {['Mon','Tue','Wed','Thu','Fri','Sat','Sun'].map(w=><div key={w} className="hr-cal-head" role="columnheader">{w}</div>)}
      {Array.from({length:lead},(_,i)=><div key={'b'+i} className="hr-cal-cell blank"/>)}
      {cal.map(d=><div key={d.date} role="gridcell" className={'hr-cal-cell'+(d.date===s.date?' today':'')+(d.full?' full':'')}><div className="hr-cal-date"><b>{+d.date.slice(8)}</b>{d.full&&<Pill tone="orange">Limit</Pill>}</div>{d.items.map(chip)}</div>)}
    </div></section>;
}

function LeavePolicy({phone}:{phone:boolean}){
  const {s,uid,open}=useHcc();const p=hrLeavePolicy(s),edit=hrCanRates(s,uid),depts=hrSlice(s).depts;
  const rows:[string,string][]=[['Casual leave',`${p.ent.CL} days / financial year`],['Sick leave',`${p.ent.SL} days / financial year`],['Earned leave',`${p.ent.EL} days / financial year`],
    ['Compensatory off · maternity / paternity','Paid, no yearly balance'],['Leave without pay','Unpaid; daily-wage staff take this type only'],['Half-day leave','First or second half = 0.5 day'],
    ['Weekly offs','One per staff member (Staff tab); skipped inside leave dates'],
    ...depts.map(d=>[`Max off per day · ${d.name}`,`${hrDeptCap(s,d.id)}${p.deptCap[d.id]===undefined?' (default)':''}`] as [string,string])];
  const btn=edit?<button className="btn primary" onClick={()=>open({type:'hr-leave-policy'})}><Settings2 size={16}/> Edit leave policy</button>:null;
  return <section className={phone?'':'card'}><div className={phone?'':'section-title'}><div><h2>Leave policy</h2><p>From SCC: entitlements per April–March year, half-days, weekly offs and a daily limit on staff off (per department here).</p></div>{!phone&&btn}</div>
    <div className={'summary-list'+(phone?' hr-m-rates':' padded')}>{rows.map(([k,v])=><div key={k}><span>{k}</span><strong>{v}</strong></div>)}</div>
    {!edit&&<p className="footnote padded">Only the Owner changes the leave policy.</p>}{phone&&btn&&<StickyFooter>{btn}</StickyFooter>}</section>;
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
  const {s,uid,run,open,busy,toast}=useHcc();
  const months:string[]=[];for(let m=hrPrevMonth(s.date.slice(0,7)),i=0;i<6;i++,m=hrPrevMonth(m))months.push(m);
  const [ym,setYm]=useState(months.find(m=>!hrRunFor(s,m)||hrRunFor(s,m)!.status==='Draft')??months[0]);
  const r=hrRunFor(s,ym),t=r&&hrRunTotals(r),preview=!r&&hrMonthComplete(s,ym)?hrComputeRun(s,ym):undefined;
  const stale=r?.status==='Draft'&&(JSON.stringify(hrComputeRun(s,ym).lines)!==JSON.stringify(r.lines)||JSON.stringify(hrRatesFor(s,ym))!==JSON.stringify(r.rates));
  const own=r?.status==='Draft'&&r.preparedBy===uid,errors=r?.status==='Draft'?hrRunErrors(r):[];
  const canUnlock=r?.status==='Locked'&&hrCanRates(s,uid)&&hrLastLockedMonth(s)===ym;
  const file=(kind:'register'|'bank'|'statutory')=>{const name={register:'payroll-register',bank:'bank-transfer',statutory:'statutory-pf-esic-pt'}[kind]+'-'+ym+'.csv';
    downloadCsv(name,kind==='register'?hrRegisterCsv(s,r!):kind==='bank'?hrBankCsv(s,r!):hrStatutoryCsv(s,r!));toast(`${name} saved (fictional data)`);};
  const xlsx=()=>{void saveBinaryFile(`payroll-register-${ym}.xlsx`,registerXlsx(s,r!),XLSX_MIME);toast('Payroll register Excel saved (fictional data)');};
  const runBtn=<button className={'btn'+(r?'':' primary')} disabled={busy||r?.status==='Locked'} onClick={()=>run({type:'HR_PAYROLL_RUN',month:ym},r?'Payroll recalculated':'Payroll draft prepared')}><Calculator size={16}/> {r?'Recalculate':'Run payroll'}</button>;
  const lockBtn=r?.status==='Draft'&&<button className="btn primary" disabled={busy||own||errors.length>0} title={own?'Prepared by you — the other payroll user locks':undefined} onClick={()=>{if(window.confirm(`Lock ${hrMonthLabel(ym)} payroll? Figures freeze, advance recoveries are posted and payslips are released.`))run({type:'HR_PAYROLL_LOCK',id:r.id},'Payroll locked · payslips released');}}><Lock size={16}/> Lock pay-run</button>;
  const status=r?<Pill tone={r.status==='Locked'?'green':'orange'}>{r.status}</Pill>:<Pill>Not run</Pill>;
  const meta=r?`Prepared by ${hrUserName(s,r.preparedBy)} ${r.preparedAt.slice(0,10)} ${r.preparedAt.slice(11)}${r.lockedBy?` · locked by ${hrUserName(s,r.lockedBy)}`:''} · rates from ${r.rates?.from??'—'}${r.legacy?' · legacy run':''}${r.unlocks?.length?` · unlocked ${r.unlocks.length}× (${r.unlocks.at(-1)!.reason})`:''}`:'';
  const notes=<>{r&&!!r.warnings.length&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><div>{r.warnings.map(w=><div key={w}>{w}</div>)}</div></div>}
    {!!errors.length&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>Fix before locking: {errors.join('; ')}.</div>}
    {stale&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>Attendance, leave, advances or rates changed after this draft — recalculate before locking.</div>}
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
  return <section className="card"><div className="section-title"><div><h2>Monthly payroll</h2><p>{meta||'Runs only for a completed month. Draft → maker-checker lock → payslips released. Owner can unlock the latest month with a reason.'}</p></div>
      <div className="inline hr-pay-actions"><select aria-label="Payroll month" value={ym} onChange={e=>setYm(e.target.value)}>{monthOpts.map(o=><option key={o.id} value={o.id}>{o.label}{hrRunFor(s,o.id)?` · ${hrRunFor(s,o.id)!.status}`:''}</option>)}</select>{status}
        {r?.status!=='Locked'&&runBtn}{lockBtn}{canUnlock&&<button className="btn" disabled={busy} onClick={()=>open({type:'hr-unlock',id:r!.id})}><Unlock size={16}/> Unlock</button>}</div></div>
    {r&&<div className="inline padded hr-pay-files"><button className="btn" onClick={()=>file('register')}><Download size={16}/> Register CSV</button><button className="btn" onClick={xlsx}><FileSpreadsheet size={16}/> Register Excel</button>
      <button className="btn" disabled={r.status!=='Locked'} title={r.status!=='Locked'?'Bank file after the pay-run is locked':undefined} onClick={()=>file('bank')}><Landmark size={16}/> Bank transfer file</button>
      <button className="btn" onClick={()=>file('statutory')}><Download size={16}/> PF / ESIC / PT statement</button></div>}
    <div className="padded hr-notes">{notes}</div>
    {r&&t?<div className="table-wrap"><table className="hr-register"><thead><tr><th>Staff</th><th className="number">Days</th><th className="number">Earned</th><th className="number">OT</th><th className="number">Gross</th><th className="number">PF</th><th className="number">ESIC</th><th className="number">PT</th><th className="number">Advance</th><th className="number">Net pay</th><th></th></tr></thead><tbody>
      {r.lines.map(l=><tr key={l.staffId}><td><strong>{l.name}</strong><small>{l.designation} · {l.payType==='monthly'?money(l.base)+'/mo':money(l.base)+'/day'}{l.structure==='B'?' · basic only':''}</small></td><td className="number">{l.payableDays}</td>
        {[l.earned,l.ot,l.gross,l.pf,l.esic,l.pt,l.advance].map((v,i)=><td key={i} className="number">{money(v)}</td>)}<td className="number"><strong>{money(l.net)}</strong></td>
        <td><button className="text-btn" onClick={()=>open({type:'hr-payslip',id:r.id+'|'+l.staffId})}><Printer size={13}/> Payslip{(r.sent??[]).some(x=>x.staffId===l.staffId)?' · sent':''}</button></td></tr>)}
      <tr className="hr-total"><td><strong>Total · {t.staff} staff</strong></td><td></td>{[t.earned,t.ot,t.gross,t.pf,t.esic,t.pt,t.advance,t.net].map((v,i)=><td key={i} className="number"><strong>{money(v)}</strong></td>)}<td></td></tr>
    </tbody></table></div>:<Empty title={`No payroll for ${hrMonthLabel(ym)} yet`} body="Run payroll to calculate a draft from attendance, leave, overtime and advances."/>}
    {t&&<p className="footnote padded">Employer contributions (not deducted): PF {money(t.pfEr)} · ESIC {money(t.esicEr)} · employer cost {money(t.ctc)}.</p>}
    <p className="footnote padded">{HR_DISCLAIMER} Net pay is paid by simulated bank transfer; nothing is posted to the cash drawer or folios.</p></section>;
}

// ── Advances ──
function Advances({phone}:{phone:boolean}){
  const {s,open}=useHcc();const x=hrSlice(s);const name=(id:string)=>x.staff.find(t=>t.id===id)?.name??id;
  const inst=(a:typeof x.advances[number])=>a.installment?`${money(a.installment)}/month`:'Full recovery';
  const add=<button className="btn primary" onClick={()=>open({type:'hr-advance'})}><Plus size={16}/> Record advance</button>;
  if(phone)return <>{x.advances.map(a=><ListCard key={a.id} title={name(a.staffId)} sub={`${money(a.amount)} · ${inst(a)}`} meta={`${shortDate(a.date)} · ${a.reason}`} right={hrAdvanceBalance(a)?<Pill tone="orange">Due {money(hrAdvanceBalance(a))}</Pill>:<Pill tone="green">Recovered</Pill>}/>)}
    {!x.advances.length&&<p className="hr-m-note">No advances.</p>}<StickyFooter>{add}</StickyFooter></>;
  return <section className="card"><div className="section-title"><div><h2>Salary advances</h2><p>Recovered at payroll lock (SCC instalment rule; blank = all at once), never beyond net pay. Full and final recovers the whole balance. Simulated bank transfer — no cash drawer effect.</p></div>{add}</div>
    {x.advances.length?<div className="table-wrap"><table><thead><tr><th>Staff</th><th>Given</th><th className="number">Amount</th><th className="number">Monthly recovery</th><th className="number">Recovered</th><th className="number">Balance</th></tr></thead><tbody>
      {x.advances.map(a=><tr key={a.id}><td><strong>{name(a.staffId)}</strong><small>{a.id} · {a.reason}</small></td><td>{dateLabel(a.date)}<small>by {hrUserName(s,a.by)}{a.fromMonth?` · from ${hrMonthLabel(a.fromMonth)}`:''}</small></td><td className="number">{money(a.amount)}</td><td className="number">{inst(a)}</td>
        <td className="number">{money(a.recovered)}<small>{a.recoveries.map(r=>r.runId).join(', ')}</small></td><td className="number"><strong>{money(hrAdvanceBalance(a))}</strong></td></tr>)}</tbody></table></div>:<Empty title="No advances"/>}</section>;
}

// ── Full and final (SCC fnf) ──
function Fnf({phone}:{phone:boolean}){
  const {s,open}=useHcc();const x=hrSlice(s),done=x.fnf??[],exited=x.staff.filter(t=>!t.active&&t.exitedOn);
  const pending=exited.filter(t=>!done.some(f=>f.staffId===t.id));
  const net=(n:number)=>n<0?`Recover ${money(-n)}`:`Pay ${money(n)}`;
  if(phone)return <>
    <h2 className="m-group">TO SETTLE</h2>
    {pending.length?pending.map(t=><ListCard key={t.id} title={t.name} sub={`Exited ${shortDate(t.exitedOn!)} · ${t.designation}`} right={<Pill tone="orange">Not settled</Pill>} onClick={()=>open({type:'hr-fnf',id:t.id})}/>):<p className="hr-m-note">Nobody waiting. Mark an exit in the Staff section first.</p>}
    <h2 className="m-group">SETTLED</h2>
    {done.map(f=><ListCard key={f.id} title={f.name} sub={`${net(f.net)} · last day ${shortDate(f.lastDay)}`} meta={`${f.id} · by ${hrUserName(s,f.by)}`} right={<Pill tone="green">Settled</Pill>}/>)}
    {!done.length&&<p className="hr-m-note">No settlements yet.</p>}</>;
  return <section className="card"><div className="section-title"><div><h2>Full and final settlement</h2><p>For staff marked as exited: last month’s salary to the exit date, PF / ESIC / PT, the full outstanding advance and up to four adjustments (e.g. earned-leave encashment). Gratuity is not calculated.</p></div></div>
    {pending.length||done.length?<div className="table-wrap"><table><thead><tr><th>Staff</th><th>Last day</th><th className="number">Earnings</th><th className="number">Recoveries</th><th className="number">Net</th><th>Status</th><th></th></tr></thead><tbody>
      {pending.map(t=><tr key={t.id}><td><strong>{t.name}</strong><small>{t.id} · {t.designation}</small></td><td>{dateLabel(t.exitedOn!)}</td><td className="number">—</td><td className="number">—</td><td className="number">—</td><td><Pill tone="orange">Not settled</Pill></td>
        <td><button className="text-btn" onClick={()=>open({type:'hr-fnf',id:t.id})}>Prepare settlement</button></td></tr>)}
      {done.map(f=><tr key={f.id}><td><strong>{f.name}</strong><small>{f.id}{f.salaryIncluded?'':` · ${hrMonthLabel(f.month)} paid in payroll`}</small></td><td>{dateLabel(f.lastDay)}</td><td className="number">{money(f.earnings)}</td><td className="number">{money(f.recoveries)}</td>
        <td className="number"><strong>{net(f.net)}</strong></td><td><Pill tone="green">Settled</Pill></td><td><small>by {hrUserName(s,f.by)} · {f.at.slice(0,10)}</small></td></tr>)}
    </tbody></table></div>:<Empty title="Nobody to settle" body="Mark a staff member as exited on the Staff tab first."/>}
    <p className="footnote padded">{HR_DISCLAIMER}</p></section>;
}

// ── Statutory rates (dated, from SCC) ──
function Rates({phone}:{phone:boolean}){
  const {s,uid,open}=useHcc();const edit=hrCanRates(s,uid),ym=s.date.slice(0,7),now=hrRatesFor(s,ym),versions=hrRateVersions(s);
  const defs=hrSettingDefs().filter(d=>d.category==='Statutory rates');
  const usedBy=(from:string)=>hrSlice(s).runs.filter(r=>r.status==='Locked'&&r.rates.from===from).map(r=>hrMonthLabel(r.month).split(' ')[0].slice(0,3)).join(', ');
  const btn=edit&&!phone?<button className="btn primary" onClick={()=>open({type:'hr-rates'})}><Settings2 size={16}/> New rate version</button>:null;
  return <section className={phone?'':'card'}><div className={phone?'':'section-title'}><div><h2>Statutory rates</h2><p className="hr-disclaimer"><AlertTriangle size={14}/> Illustrative — CA to confirm for a hotel. Ported from SCC; in force for {hrMonthLabel(ym)}: version from {now.from}.</p></div>{btn}</div>
    <div className={'summary-list'+(phone?' hr-m-rates':' padded')}>{defs.map(d=><div key={d.key}><span>{d.label}</span><strong>{hrSettingValue(s,d.key)}</strong></div>)}</div>
    {phone?<div className="info-note"><Info size={16}/>Rate versions are added on the tablet or desktop (Owner).</div>:<>
      <h3 className="padded hr-sub">Versions</h3>
      <div className="table-wrap"><table><thead><tr><th>Effective from</th><th>PF</th><th>ESIC</th><th>PT (men / women)</th><th>Overtime</th><th>Rounding</th><th>Locked runs</th></tr></thead><tbody>
        {versions.map(v=><tr key={v.from}><td><strong>{dateLabel(v.from)}</strong>{v.from===now.from&&<small>In force now</small>}</td><td>{v.pfEeBps/100}% + {v.pfErBps/100}% · cap {money(v.pfWageCap)}</td><td>{v.esicEeBps/100}% + {v.esicErBps/100}% · ≤ {money(v.esicCeiling)}</td>
          <td>{money(v.ptStd)} (Feb {money(v.ptFeb)}) · women exempt ≤ {money(v.ptFemaleExempt)}</td><td>{v.otBps/10000}×</td><td>{v.rounding==='paise'?'Paise':'Rupee'}</td><td>{usedBy(v.from)||'—'}</td></tr>)}</tbody></table></div></>}
    {!edit&&<p className="footnote padded">Only the Owner adds a rate version (DEC-058). Locked pay-runs keep the rates they were calculated with.</p>}</section>;
}
export const HrDeskMobile=()=><HrDesk phone/>;
/** Section components, exported for render tests. */
export const HR_SECTIONS={People,Roster,Attendance,Leave,LeaveRequests,LeaveCalendar,LeavePolicy,Payroll,Advances,Fnf,Rates};
