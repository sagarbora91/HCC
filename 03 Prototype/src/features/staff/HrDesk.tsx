// Module 17 · HR desk (page 'staff'): staff master, roster, attendance, leave, payroll, advances, illustrative rates.
// Tabs by role: managers (approve) see people/roster/attendance/leave without salaries; Owner/Accountant see payroll.
// Anyone else who opens the page sees their own "My HR" view. Desktop renders tables; the phone renders cards (phone=true).
import {saveCsv} from '../../app/native';
import React,{useState} from 'react';
import {UserPlus,ChevronLeft,ChevronRight,Copy,Download,Lock,Calculator,Printer,Plus,Settings2,AlertTriangle,Check,X} from 'lucide-react';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {ListCard,KpiCard,ChipFilter,StickyFooter} from '../../app/mobile/components';
import {
  money,addDays,isDayClosed,hrSlice,hrCanManage,hrCanPay,hrCanRates,hrStaffOfUser,hrShiftFor,hrShiftInfo,hrMonday,hrWeek,hrKey,hrDayStatus,hrCounts,
  hrDeptName,hrDecidable,hrRunFor,hrRunTotals,hrMonthLabel,hrPrevMonth,hrMonthComplete,hrRegisterCsv,hrAdvanceBalance,hrComputeRun,
  HR_ATT_LABEL,HR_LEAVE_LABEL,HR_WEEKDAYS,HR_DISCLAIMER,type HRStaff,
} from '../../services/mock';
import {MyHr,hrLeaveTone,hrAttTone} from './MyHr';
import {hrUserName} from './modals';

type Tab='people'|'roster'|'attendance'|'leave'|'payroll'|'advances'|'settings';
const TAB_LABEL:Record<Tab,string>={people:'Staff',roster:'Roster',attendance:'Attendance',leave:'Leave',payroll:'Payroll',advances:'Advances',settings:'Rates'};
const day=(d:string)=>HR_WEEKDAYS[new Date(d+'T12:00:00Z').getUTCDay()];

export function HrDesk({phone=false}:{phone?:boolean}){
  const {s,uid}=useHcc();const mgr=hrCanManage(s,uid),pay=hrCanPay(s,uid);
  const tabs:Tab[]=[...(mgr||pay?['people','roster','attendance','leave'] as Tab[]:[]),...(pay?['payroll','advances','settings'] as Tab[]:[])];
  const [tab,setTab]=useState<Tab>(tabs[0]??'people');
  if(!tabs.length)return phone?<MyHr phone/>:<MyHr/>;
  const pending=hrDecidable(s,uid).length;
  const body={people:<People phone={phone}/>,roster:<Roster phone={phone}/>,attendance:<Attendance phone={phone}/>,leave:<Leave phone={phone}/>,payroll:<Payroll phone={phone}/>,advances:<Advances phone={phone}/>,settings:<Rates phone={phone}/>}[tab];
  if(phone)return <div className="hr-m"><ChipFilter label="HR section" value={tab} onChange={setTab} options={tabs.map(t=>({id:t,label:TAB_LABEL[t],count:t==='leave'&&pending?pending:undefined}))}/>{body}</div>;
  return <><PageTitle title="Staff & payroll" sub={`Fictional staff · business date ${dateLabel(s.date)} · ${pay?'payroll figures are illustrative — not statutory advice':'salary figures are visible to the Owner and Accountant only'}`}/>
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
function Leave({phone}:{phone:boolean}){
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
    {pending.length?pending.map(l=><div key={l.id} className="hr-m-leave"><ListCard title={`${name(l.staffId)} · ${l.type} ${l.days}d`} sub={`${dates(l)} · ${l.reason}`} meta={`Requested by ${hrUserName(s,l.requestedBy)}${why(l)?' · '+why(l):''}`}/>
      {decidable.has(l.id)&&<div className="hr-m-actions">{decide(l.id)}</div>}</div>):<p className="hr-m-note">No pending requests.</p>}
    <h2 className="m-group">HISTORY</h2>
    {history.map(l=><ListCard key={l.id} title={`${name(l.staffId)} · ${HR_LEAVE_LABEL[l.type]}`} sub={`${dates(l)} · ${l.days} day${l.days===1?'':'s'}`} meta={l.decidedBy?`${l.status} by ${hrUserName(s,l.decidedBy)}${l.note?' · '+l.note:''}`:undefined} right={<Pill tone={hrLeaveTone(l.status)}>{l.status}</Pill>}
      onClick={cancelable(l)?()=>{if(window.confirm('Cancel this approved leave?'))run({type:'HR_LEAVE_CANCEL',id:l.id},'Leave cancelled');}:undefined}/>)}
    {mgr&&<StickyFooter><button className="btn primary" onClick={()=>open({type:'hr-leave'})}><Plus size={18}/> Leave for a staff member</button></StickyFooter>}</>;
  return <>
    <section className="card"><div className="section-title"><div><h2>Awaiting decision</h2><p>No self-approval: the requester and the person on leave cannot decide.</p></div>{mgr&&<button className="btn primary" onClick={()=>open({type:'hr-leave'})}><Plus size={16}/> Leave for a staff member</button>}</div>
      {pending.length?<div className="table-wrap"><table><thead><tr><th>Staff</th><th>Leave</th><th>Dates</th><th>Requested by</th><th></th></tr></thead><tbody>{pending.map(l=><tr key={l.id}>
        <td><strong>{name(l.staffId)}</strong><small>{l.id}</small></td><td>{HR_LEAVE_LABEL[l.type]}<small>{l.reason}</small></td><td>{dates(l)}<small>{l.days} working day{l.days===1?'':'s'}</small></td><td>{hrUserName(s,l.requestedBy)}<small>{l.requestedAt.slice(0,10)} {l.requestedAt.slice(11)}</small></td>
        <td className="hr-actions">{decidable.has(l.id)?decide(l.id):<span className="muted">{why(l)}</span>}</td></tr>)}</tbody></table></div>:<Empty title="No pending leave" body="New requests from staff appear here."/>}</section>
    <section className="card"><div className="section-title"><h2>Leave history</h2></div>
      <div className="table-wrap"><table><thead><tr><th>Staff</th><th>Leave</th><th>Dates</th><th>Status</th><th>Decided by</th><th></th></tr></thead><tbody>{history.map(l=><tr key={l.id}>
        <td><strong>{name(l.staffId)}</strong><small>{l.id}</small></td><td>{HR_LEAVE_LABEL[l.type]}<small>{l.reason}</small></td><td>{dates(l)}<small>{l.days} day{l.days===1?'':'s'}</small></td><td><Pill tone={hrLeaveTone(l.status)}>{l.status}</Pill></td>
        <td>{l.decidedBy?hrUserName(s,l.decidedBy):'—'}{l.note&&<small>{l.note}</small>}</td><td>{cancelable(l)&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'HR_LEAVE_CANCEL',id:l.id},'Leave cancelled')}>Cancel leave</button>}</td></tr>)}</tbody></table></div></section></>;
}

// ── Payroll ──
function downloadCsv(name:string,csv:string){void saveCsv(name,csv);}
function Payroll({phone}:{phone:boolean}){
  const {s,uid,run,open,busy,toast}=useHcc();
  const months:string[]=[];for(let m=hrPrevMonth(s.date.slice(0,7)),i=0;i<6;i++,m=hrPrevMonth(m))months.push(m);
  const [ym,setYm]=useState(months.find(m=>!hrRunFor(s,m)||hrRunFor(s,m)!.status==='Draft')??months[0]);
  const r=hrRunFor(s,ym),t=r&&hrRunTotals(r),preview=!r&&hrMonthComplete(s,ym)?hrComputeRun(s,ym):undefined;
  const stale=r?.status==='Draft'&&JSON.stringify(hrComputeRun(s,ym).lines)!==JSON.stringify(r.lines);
  const own=r?.status==='Draft'&&r.preparedBy===uid;
  const csv=()=>{downloadCsv(`payroll-register-${ym}.csv`,hrRegisterCsv(s,r!));toast('Payroll register CSV downloaded (fictional data)');};
  const runBtn=<button className={'btn'+(r?'':' primary')} disabled={busy||r?.status==='Locked'} onClick={()=>run({type:'HR_PAYROLL_RUN',month:ym},r?'Payroll recalculated':'Payroll draft prepared')}><Calculator size={16}/> {r?'Recalculate':'Run payroll'}</button>;
  const lockBtn=r?.status==='Draft'&&<button className="btn primary" disabled={busy||own} title={own?'Prepared by you — the other payroll user locks':undefined} onClick={()=>{if(window.confirm(`Lock ${hrMonthLabel(ym)} payroll? Locked payroll cannot change and advance recoveries are posted.`))run({type:'HR_PAYROLL_LOCK',id:r.id},'Payroll locked · payslips released');}}><Lock size={16}/> Lock payroll</button>;
  const status=r?<Pill tone={r.status==='Locked'?'green':'orange'}>{r.status}</Pill>:<Pill>Not run</Pill>;
  const meta=r?`Prepared by ${hrUserName(s,r.preparedBy)} ${r.preparedAt.slice(0,10)} ${r.preparedAt.slice(11)}${r.lockedBy?` · locked by ${hrUserName(s,r.lockedBy)}`:''}`:'';
  const notes=<>{r&&!!r.warnings.length&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><div>{r.warnings.map(w=><div key={w}>{w}</div>)}</div></div>}
    {stale&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/>Attendance, leave, advances or rates changed after this draft — recalculate before locking.</div>}
    {own&&<div className="info-note"><Lock size={16}/>You prepared this draft. Maker-checker: the other payroll user (Owner / Accountant) locks it.</div>}
    {!r&&!hrMonthComplete(s,ym)&&<div className="info-note">This month is still in progress.</div>}
    {preview&&<div className="info-note">Preview: {preview.lines.length} staff · estimated net {money(hrRunTotals(preview).net)}. Run payroll to create a draft.</div>}</>;
  const monthOpts=months.map(m=>({id:m,label:hrMonthLabel(m)}));
  if(phone)return <>
    <ChipFilter label="Month" value={ym} onChange={setYm} options={monthOpts}/>
    <section className="card padded hr-m-run"><div className="inline">{status}<span className="muted">{meta}</span></div>
      {t&&<div className="hr-kpis"><KpiCard label="Gross" value={money(t.gross)}/><KpiCard label="Net pay" value={money(t.net)} sub={`${t.staff} staff`}/></div>}</section>
    {notes}
    {r?.lines.map(l=><ListCard key={l.staffId} title={l.name} sub={`Net ${money(l.net)} · gross ${money(l.gross)}`} meta={`${l.payableDays} days · deductions ${money(l.deductions)}${l.advance?' incl. advance':''}`} onClick={()=>open({type:'hr-payslip',id:r.id+'|'+l.staffId})}/>)}
    <p className="hr-m-note">{HR_DISCLAIMER}</p>
    <StickyFooter>{r?.status!=='Locked'&&runBtn}{lockBtn}{r?.status==='Locked'&&<button className="btn primary" onClick={csv}><Download size={18}/> Register CSV</button>}</StickyFooter></>;
  return <section className="card"><div className="section-title"><div><h2>Monthly payroll</h2><p>{meta||'Runs only for a completed month. Draft → maker-checker lock → payslips released.'}</p></div>
      <div className="inline"><select aria-label="Payroll month" value={ym} onChange={e=>setYm(e.target.value)}>{monthOpts.map(o=><option key={o.id} value={o.id}>{o.label}{hrRunFor(s,o.id)?` · ${hrRunFor(s,o.id)!.status}`:''}</option>)}</select>{status}
        {r?.status!=='Locked'&&runBtn}{lockBtn}{r&&<button className="btn" onClick={csv}><Download size={16}/> Register CSV</button>}</div></div>
    <div className="padded hr-notes">{notes}</div>
    {r&&t?<div className="table-wrap"><table className="hr-register"><thead><tr><th>Staff</th><th className="number">Days</th><th className="number">Basic</th><th className="number">OT</th><th className="number">Gross</th><th className="number">PF*</th><th className="number">ESIC*</th><th className="number">PT*</th><th className="number">Advance</th><th className="number">Net pay</th><th></th></tr></thead><tbody>
      {r.lines.map(l=><tr key={l.staffId}><td><strong>{l.name}</strong><small>{l.designation} · {l.payType==='monthly'?money(l.base)+'/mo':money(l.base)+'/day'}</small></td><td className="number">{l.payableDays}</td>
        {[l.basic,l.ot,l.gross,l.pf,l.esic,l.pt,l.advance].map((v,i)=><td key={i} className="number">{money(v)}</td>)}<td className="number"><strong>{money(l.net)}</strong></td>
        <td><button className="text-btn" onClick={()=>open({type:'hr-payslip',id:r.id+'|'+l.staffId})}><Printer size={13}/> Payslip</button></td></tr>)}
      <tr className="hr-total"><td><strong>Total · {t.staff} staff</strong></td><td></td>{[t.basic,t.ot,t.gross,t.pf,t.esic,t.pt,t.advance,t.net].map((v,i)=><td key={i} className="number"><strong>{money(v)}</strong></td>)}<td></td></tr>
    </tbody></table></div>:<Empty title={`No payroll for ${hrMonthLabel(ym)} yet`} body="Run payroll to calculate a draft from attendance, leave, overtime and advances."/>}
    <p className="footnote padded">* {HR_DISCLAIMER} Net pay is paid by simulated bank transfer; nothing is posted to the cash drawer or folios.</p></section>;
}

// ── Advances ──
function Advances({phone}:{phone:boolean}){
  const {s,open}=useHcc();const x=hrSlice(s);const name=(id:string)=>x.staff.find(t=>t.id===id)?.name??id;
  const add=<button className="btn primary" onClick={()=>open({type:'hr-advance'})}><Plus size={16}/> Record advance</button>;
  if(phone)return <>{x.advances.map(a=><ListCard key={a.id} title={name(a.staffId)} sub={`${money(a.amount)} · ${money(a.installment)}/month`} meta={`${shortDate(a.date)} · ${a.reason}`} right={hrAdvanceBalance(a)?<Pill tone="orange">Due {money(hrAdvanceBalance(a))}</Pill>:<Pill tone="green">Recovered</Pill>}/>)}
    {!x.advances.length&&<p className="hr-m-note">No advances.</p>}<StickyFooter>{add}</StickyFooter></>;
  return <section className="card"><div className="section-title"><div><h2>Salary advances</h2><p>Recovered at payroll lock, never beyond net pay. Simulated bank transfer — no cash drawer effect.</p></div>{add}</div>
    {x.advances.length?<div className="table-wrap"><table><thead><tr><th>Staff</th><th>Given</th><th className="number">Amount</th><th className="number">Monthly recovery</th><th className="number">Recovered</th><th className="number">Balance</th></tr></thead><tbody>
      {x.advances.map(a=><tr key={a.id}><td><strong>{name(a.staffId)}</strong><small>{a.id} · {a.reason}</small></td><td>{dateLabel(a.date)}<small>by {hrUserName(s,a.by)}</small></td><td className="number">{money(a.amount)}</td><td className="number">{money(a.installment)}</td>
        <td className="number">{money(a.recovered)}<small>{a.recoveries.map(r=>r.runId).join(', ')}</small></td><td className="number"><strong>{money(hrAdvanceBalance(a))}</strong></td></tr>)}</tbody></table></div>:<Empty title="No advances"/>}</section>;
}

// ── Illustrative rates ──
function Rates({phone}:{phone:boolean}){
  const {s,uid,open}=useHcc();const r=hrSlice(s).rates,edit=hrCanRates(s,uid);
  const rows:[string,string][]=[['Basic share of earned pay',`${r.basicBps/100}%`],['PF placeholder',`${r.pfBps/100}% of basic, basic capped at ${money(r.pfCap)}`],['ESIC placeholder',`${r.esicBps/100}% of gross when gross ≤ ${money(r.esicCeiling)}`],
    ['PT placeholder',`${money(r.pt)} / month when gross ≥ ${money(r.ptThreshold)}`],['Overtime',`${r.otBps/10000}× hourly rate (monthly ÷ days ÷ 8 h)`],['Late rule',r.lateBlock?`${r.lateBlock} late marks = ½ day loss of pay`:'Off'],
    ['Leave entitlement','CL 8 · SL 8 · EL 12 days per year (daily wage: leave without pay only)']];
  const btn=edit?<button className="btn primary" onClick={()=>open({type:'hr-rates'})}><Settings2 size={16}/> Edit rates</button>:null;
  return <section className={phone?'':'card'}><div className={phone?'':'section-title'}><div><h2>Illustrative payroll rates</h2><p className="hr-disclaimer"><AlertTriangle size={14}/> {HR_DISCLAIMER}</p></div>{!phone&&btn}</div>
    <div className={'summary-list'+(phone?' hr-m-rates':'')}>{rows.map(([k,v])=><div key={k}><span>{k}</span><strong>{v}</strong></div>)}</div>
    {!edit&&<p className="footnote padded">Only the Owner can change these rates.</p>}{phone&&btn&&<StickyFooter>{btn}</StickyFooter>}</section>;
}
export const HrDeskMobile=()=><HrDesk phone/>;
