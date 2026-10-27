// Module 17 · Leave tab — SCC LeaveDesk screens ported in full (DEC-116 / DEC-119; report 05 Delivery Evidence\SCC Port\SCC_Leave_Port_2026-10-27.md).
// Desk (SCC header + read-only past view + stats strip + 7-day staffing strip + month calendar), Approvals (one row per pending DATE), monthly
// Report (+ CSV + print), Settings (SCC "Staff Master": entitlement defaults, per-staff ⚙ overrides, week-off days, advisory department caps,
// blackouts and store holidays) and the JSON backup / import / clear. Desktop renders the grid and tables; the phone renders cards and a compact
// month grid (48 px day targets, no sideways scroll at 360 px). Writes go through the HR_* leave commands (the production client maps them).
import React from 'react';
import {ChevronLeft,ChevronRight,Plus,Check,X,Printer,Download,Upload,Trash2,Settings2,CalendarDays,ListChecks,FileSpreadsheet,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,Empty,shortDate} from '../../components/ui';
import {ListCard,KpiCard,StickyFooter} from '../../app/mobile/components';
import {saveCsv,saveTextFile} from '../../app/native';
import {MstLink} from '../masters/MasterList';
import {
  hrSlice,hrCanManage,hrCanRates,hrCapacity,hrPendingLeaves,hrDecidable,hrLeaveMonthView,hrLeaveStats,hrLeaveStrip,hrLeaveReport,hrLeaveReportCsv,
  hrLeaveExportJson,hrLeaveClampYm,hrLeaveMonthJumper,hrLeaveFyLabel,hrLeaveMonthName,hrLeaveDateShort,hrLeaveFmt,hrLeaveApproverName,hrLeaveName,
  hrLeavePolicy,hrLeaveWeekOff,hrEntFor,hrDeptCap,hrKindLabel,hrPrevMonth,hrNextMonth,HR_LEAVE_START_YM,HR_TRACKED,HR_WEEKDAYS,HR_MAX_DAY,HR_MAX_SHIFT,
  HR_ENT_DEFAULTS,HR_SCC_WEEKOFF_DEFAULT,HR_SCC_DAYS,type HRLeave,
} from '../../services/mock';

type S=ReturnType<typeof useHcc>['s'];
const staffName=(s:S,id:string)=>hrSlice(s).staff.find(t=>t.id===id)?.name??id;
/** Past view (SCC isPastView): the as-of date is before the business date → read-only. */
export const hrLeaveReadOnly=(s:S,asOf:string)=>asOf<s.date;
/** SCC "Viewing … (past date) — read-only" banner. */
function ReadOnlyBanner({s,asOf}:{s:S;asOf:string}){
  return hrLeaveReadOnly(s,asOf)?<div className="info-note hr-warn lv-readonly" role="status"><Info size={16}/><span>Viewing {hrLeaveDateShort(asOf)} (past date) — read-only. Add / edit / delete are disabled.</span></div>:null;
}
const toneClass:Record<string,string>={amber:'lv-amber',mid:'lv-mid',red:'lv-red',blue:'lv-blue',"":''};

/** SCC header + stats + staffing strip + month calendar. `ym` / `asOf` are shared with the report (HrDesk keeps them). */
export function LeaveDeskView({phone,ym,setYm,asOf,setAsOf}:{phone:boolean;ym:string;setYm:(v:string)=>void;asOf:string;setAsOf:(v:string)=>void}){
  const {s,uid,open,toast}=useHcc();const mgr=hrCanManage(s,uid),owner=hrCanRates(s,uid),ro=hrLeaveReadOnly(s,asOf);
  const view=hrLeaveClampYm(ym),cal=hrLeaveMonthView(s,view),stats=hrLeaveStats(s,view),strip=hrLeaveStrip(s,asOf),pending=hrPendingLeaves(s).length;
  const target=asOf<s.date?s.date:asOf,full=hrCapacity(s,target).dayFull;
  const day=(d:string)=>open({type:'hr-leave-day',id:d,room:asOf});
  const backup=()=>{const f=hrLeaveExportJson(s);void saveTextFile(f.name,f.json,'application/json').then(()=>toast(`${f.name} saved`));};
  const nav=<div className="inline lv-nav">
    <button className="btn" aria-label="Previous month" disabled={view<=HR_LEAVE_START_YM} onClick={()=>setYm(hrPrevMonth(view))}><ChevronLeft size={16}/></button>
    <strong className="lv-month">{hrLeaveMonthName(view)}</strong>
    <button className="btn" aria-label="Next month" onClick={()=>setYm(hrNextMonth(view))}><ChevronRight size={16}/></button>
    <button className="btn" onClick={()=>setYm(hrLeaveClampYm(s.date.slice(0,7)))}>Today</button>
    <label className="lv-jump"><span className="sr-only">Jump to month</span><select value={view} onChange={e=>setYm(e.target.value)}>{hrLeaveMonthJumper(view).map(m=><option key={m} value={m}>{hrLeaveMonthName(m).replace(/^(\w{3})\w*/,'$1')}</option>)}</select></label>
    <Pill tone="blue">{hrLeaveFyLabel(view)}</Pill></div>;
  const asof=<label className="lv-asof">View as of<input type="date" value={asOf} onChange={e=>{if(e.target.value){setAsOf(e.target.value);setYm(hrLeaveClampYm(e.target.value.slice(0,7)));}}}/></label>;
  const actions=<div className="inline lv-actions">
    {mgr&&!ro&&!full&&<button className="btn primary" onClick={()=>open({type:'hr-leave',room:target})}><Plus size={phone?18:16}/> Add Leave</button>}
    {mgr&&!ro&&<button className="btn" onClick={()=>open({type:'hr-agenda',room:target})}><CalendarDays size={phone?18:16}/> Add Agenda</button>}
    <button className="btn" onClick={()=>open({type:'hr-leave-print',id:view})}><Printer size={phone?18:16}/> Print</button>
    <button className="btn" onClick={backup}><Download size={phone?18:16}/> Backup JSON</button>
    {owner&&!ro&&<button className="btn" onClick={()=>open({type:'hr-leave-import'})}><Upload size={phone?18:16}/> Import JSON</button>}
    {owner&&!ro&&<button className="btn danger" onClick={()=>open({type:'hr-leave-clear'})}><Trash2 size={phone?18:16}/> Clear All</button>}</div>;
  const statsRow=phone?<div className="hr-kpis lv-kpis">{stats.map(c=><KpiCard key={c.label} label={c.label} value={c.value} sub={c.sub}/>)}</div>
    :<div className="lv-stats">{stats.map(c=><div key={c.label} className="lv-stat"><span>{c.label}</span><strong className={toneClass[c.tone]}>{c.value}</strong><small>{c.sub}</small></div>)}</div>;
  const stripRow=<section className={phone?'lv-strip-m':'card lv-strip-card'}><div className={phone?'':'section-title'}><h2 className={phone?'m-group':''}>{phone?'NEXT 7 DAYS':'Next 7 days · staffing'}</h2>{!phone&&<span className="muted">From {hrLeaveDateShort(asOf)} · records on leave (pending + approved) and staff available</span>}</div>
    <div className="lv-strip" role="list">{strip.map(d=><button type="button" role="listitem" key={d.date} onClick={()=>day(d.date)} className={'lv-sday'+(d.today?' today':'')+(d.nonTrading?' nontrading':'')+(d.blackout?' blackout':'')+(d.short.length?' understaffed':'')}
      aria-label={`${d.dow} ${shortDate(d.date)}: ${d.onLeave} on leave${d.avail?', '+d.avail:''}${d.nonTrading?', weekly off':''}${d.short.length?', short: '+d.short.join(', '):''}`}>
      <span className="lv-sdow">{d.dow}</span><b>{d.day}</b><span>{d.onLeave} on leave</span>{d.avail&&<small>{d.avail}</small>}
      {d.nonTrading?<em>Weekly off</em>:d.short.length?<em className="lv-short">Short: {d.short.join(', ')}</em>:null}</button>)}</div></section>;
  const legend=<p className="footnote padded lv-legend">Up to {HR_MAX_DAY} leave records a day for the hotel (pending + approved; SCC MAX_DAY) · {HR_MAX_SHIFT} per shift (SCC MAX_SHIFT) · “?” = pending · tinted days = leave week-off ({hrLeaveWeekOff(s).length?hrLeaveWeekOff(s).map(d=>HR_WEEKDAYS[d]).join(' + '):'none'}) · red dot = a shift full · blue dot = agenda · striped = blackout / store holiday.</p>;
  if(phone)return <>
    <ReadOnlyBanner s={s} asOf={asOf}/>{nav}{asof}{statsRow}{stripRow}
    <div className="lv-grid-m" role="grid" aria-label={`Leave calendar ${hrLeaveMonthName(view)}`}>
      {['M','T','W','T','F','S','S'].map((w,i)=><div key={i} className="lv-head" role="columnheader">{w}</div>)}
      {Array.from({length:cal.lead},(_,i)=><div key={'b'+i} className="lv-mcell blank"/>)}
      {cal.cells.map(c=><button type="button" role="gridcell" key={c.date} onClick={()=>day(c.date)} className={'lv-mcell'+(c.weekOff?' weekend':'')+(c.past?' past':'')+(c.today?' today':'')+(c.cap.bothFull?' at-limit':'')+(c.blackouts.length?(c.blackoutHard?' blackout-hard':' blackout'):'')}
        aria-label={`${shortDate(c.date)}: ${c.chips.length+c.more} on leave${c.agendas.length?`, ${c.agendas.length} agenda`:''}${c.limitTitle?', '+c.limitTitle:''}${c.blackouts.length?', blackout':''}`}>
        <b>{c.day}</b><span className="lv-dots">{c.limitTitle&&<i className="lv-dot-limit"/>}{c.agendas.length>0&&<i className="lv-dot-agenda"/>}{c.blackouts.length>0&&<i className="lv-dot-bk"/>}</span>
        {c.chips.length+c.more>0&&<small>{c.chips.length+c.more}</small>}</button>)}</div>
    {legend}
    {mgr&&<button className="btn lv-appr-m" onClick={()=>open({type:'hr-leave-approvals',room:asOf})}><ListChecks size={18}/> Approvals · {pending}</button>}
    <div className="lv-actions-m">{actions}</div></>;
  return <>
    <ReadOnlyBanner s={s} asOf={asOf}/>
    <section className="card lv-head-card"><div className="section-title"><div><h2>Leave calendar</h2><p>SCC LeaveDesk: one record per date (full day, morning or afternoon), decided date by date. Pending records hold a place; rejected ones stay for audit off the calendar; removed ones are cancelled and kept for audit.</p></div>
      <div className="inline">{asof}{mgr&&<button className="btn" onClick={()=>open({type:'hr-leave-approvals',room:asOf})}><ListChecks size={16}/> Approvals<span className="lv-badge">{pending}</span></button>}</div></div>
      <div className="lv-toolbar padded">{nav}{actions}</div></section>
    {statsRow}{stripRow}
    <section className="card"><div className="lv-cal" role="grid" aria-label={`Leave calendar ${hrLeaveMonthName(view)}`}>
      {['Mon','Tue','Wed','Thu','Fri','Sat','Sun'].map(w=><div key={w} className="lv-head" role="columnheader">{w}</div>)}
      {Array.from({length:cal.lead},(_,i)=><div key={'b'+i} className="lv-cell other"/>)}
      {cal.cells.map(c=><div role="gridcell" key={c.date} tabIndex={0} onClick={()=>day(c.date)} onKeyDown={e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();day(c.date);}}}
        className={'lv-cell'+(c.weekOff?' weekend':'')+(c.past?' past':'')+(c.today?' today':'')+(c.cap.bothFull?' at-limit':'')+(c.blackouts.length?(c.blackoutHard?' blackout blackout-hard':' blackout'):'')}>
        <div className="lv-date"><span>{c.day}</span>{c.limitTitle&&<span className="lv-dot-limit" title={c.limitTitle}/>}{c.agendas.length>0&&<span className="lv-dot-agenda" title={`${c.agendas.length} agenda item(s)`}/>}
          {c.blackouts.length>0&&<span className="lv-dot-bk" title={c.blackoutTitle}/>}</div>
        {c.chips.map(ch=><span key={ch.leave.id} className={'lv-chip '+(ch.leave.half==='AM'?'half-am':ch.leave.half==='PM'?'half-pm':'full')+(ch.pending?' chip-pending':'')} title={`${ch.name} · ${hrLeaveName(s,ch.leave.type)} · ${hrKindLabel(ch.leave)} · ${ch.leave.status}`}>
          {(ch.pending?'? ':'')+ch.name}</span>)}
        {c.more>0&&<span className="lv-more">+{c.more} more</span>}
        {c.agendas.length>0&&<span className="lv-agenda-mini">📌 {c.agendas[0].title}</span>}</div>)}
    </div>{legend}</section></>;
}

/** SCC renderApprovals: every pending DATE record (badge = their number); approve / reject one by one. HCC (DEC-119): nobody decides their own leave. */
export function LeaveApprovalsView({phone,asOf}:{phone:boolean;asOf:string}){
  const {s,uid,run,busy}=useHcc();const pend=hrPendingLeaves(s),ok=new Set(hrDecidable(s,uid).map(l=>l.id)),ro=hrLeaveReadOnly(s,asOf);
  const meta=(l:HRLeave)=>`${hrKindLabel(l).replace('Half Day ','Half ')} · ${hrLeaveName(s,l.type)} · Approver: ${hrLeaveApproverName(s,l)||'—'}${l.reason?' · '+l.reason:''}`;
  const decide=(l:HRLeave,approve:boolean)=>run({type:'HR_LEAVE_DECIDE',id:l.id,approve},approve?'Leave approved — now live for floor gate & payroll':'Leave rejected — kept for audit');
  const why=(l:HRLeave)=>hrCanManage(s,uid)?'Your own leave or request — another manager decides':'Managers decide';
  const btns=(l:HRLeave)=>!ro&&ok.has(l.id)?<><button className="btn small-btn primary" disabled={busy} onClick={()=>decide(l,true)}><Check size={14}/> Approve</button>
    <button className="btn small-btn danger" disabled={busy} onClick={()=>decide(l,false)}><X size={14}/> Reject</button></>:<span className="muted">{ro?'Read-only (past view)':why(l)}</span>;
  const sub=`${pend.length} request(s) awaiting a decision`;
  if(phone)return <><ReadOnlyBanner s={s} asOf={asOf}/><p className="hr-m-note">{sub}. Decide each date within 7 days of the leave date (SCC).</p>
    {pend.length?pend.map(l=><div key={l.id} className="hr-m-leave"><ListCard title={`${staffName(s,l.staffId)} — ${hrLeaveDateShort(l.date)}`} sub={meta(l)}/><div className="hr-m-actions">{btns(l)}</div></div>)
      :<Empty title="No pending leave requests."/>}</>;
  return <><ReadOnlyBanner s={s} asOf={asOf}/><section className="card"><div className="section-title"><div><h2>Leave approvals</h2><p>{sub}. One row per date (SCC); approve or reject each. Nobody approves their own leave (DEC-119).</p></div></div>
    {pend.length?<div className="table-wrap"><table><thead><tr><th>Staff — date</th><th>Leave</th><th>Requested by</th><th></th></tr></thead><tbody>{pend.map(l=><tr key={l.id}>
      <td><strong>{staffName(s,l.staffId)} — {hrLeaveDateShort(l.date)}</strong><small>{l.id}{l.from!==l.to?` · request ${shortDate(l.from)} – ${shortDate(l.to)}`:''}</small></td><td>{meta(l)}</td>
      <td>{s.users.find(u=>u.id===l.requestedBy)?.name.split(' · ')[0]??l.requestedBy}<small>{l.requestedAt.slice(0,10)} {l.requestedAt.slice(11)}</small></td>
      <td className="hr-actions">{btns(l)}</td></tr>)}</tbody></table></div>:<Empty title="No pending leave requests."/>}</section></>;
}

/** SCC openReport + exportReportCSV + printCalendar for the month. */
export function LeaveReportView({phone,ym,setYm}:{phone:boolean;ym:string;setYm:(v:string)=>void}){
  const {s,open,toast}=useHcc();const view=hrLeaveClampYm(ym),r=hrLeaveReport(s,view);
  const csv=()=>{const f=hrLeaveReportCsv(s,view);void saveCsv(f.name,f.csv).then(()=>toast(`${f.name} saved`));};
  const nav=<div className="inline lv-nav"><button className="btn" aria-label="Previous month" disabled={view<=HR_LEAVE_START_YM} onClick={()=>setYm(hrPrevMonth(view))}><ChevronLeft size={16}/></button>
    <strong className="lv-month">{hrLeaveMonthName(view)}</strong><button className="btn" aria-label="Next month" onClick={()=>setYm(hrNextMonth(view))}><ChevronRight size={16}/></button></div>;
  const btns=<div className="inline"><button className="btn" onClick={csv}><FileSpreadsheet size={phone?18:16}/> CSV</button><button className="btn" onClick={()=>open({type:'hr-leave-print',id:view})}><Printer size={phone?18:16}/> Print</button></div>;
  const title=`Leave Report — ${hrLeaveMonthName(view)}`;
  if(phone)return <>{nav}<p className="hr-m-note">Employee-wise breakdown · {hrLeaveMonthName(view)} · FY balance {r.fy} (used/quota)</p>
    {r.rows.length?r.rows.map(x=><ListCard key={x.staffId} title={x.name} sub={`Full ${x.full} · Half AM ${x.halfAM} · Half PM ${x.halfPM} · Leave days ${hrLeaveFmt(x.leaveDays)}`}
      meta={`${x.pending?`Pending ${x.pending} · `:''}${x.cats.map(c=>hrLeaveName(s,c)).join(', ')||'—'} · ${x.balance}`}/>):<Empty title="No leave records for this month."/>}
    {r.rows.length>0&&<ListCard title="TOTAL" sub={`Full ${r.total.full} · Half AM ${r.total.halfAM} · Half PM ${r.total.halfPM} · Leave days ${hrLeaveFmt(r.total.leaveDays)}`} meta={`Pending ${r.total.pending}`}/>}
    <StickyFooter>{btns}</StickyFooter></>;
  return <section className="card"><div className="section-title"><div><h2>{title}</h2><p>Employee-wise breakdown · {hrLeaveMonthName(view)} · approved (and legacy) days; pending counted apart; rejected and removed left out.</p></div><div className="inline">{nav}{btns}</div></div>
    {r.rows.length?<div className="table-wrap"><table className="lv-report"><thead><tr><th>Employee</th><th className="number">Full Days</th><th className="number">Half AM</th><th className="number">Half PM</th><th className="number">Leave Days</th><th className="number">Pending</th><th>Categories</th><th>FY Balance (used/quota)</th></tr></thead>
      <tbody>{r.rows.map(x=><tr key={x.staffId}><td><strong>{x.name}</strong></td><td className="number">{x.full}</td><td className="number">{x.halfAM}</td><td className="number">{x.halfPM}</td><td className="number"><b>{hrLeaveFmt(x.leaveDays)}</b></td>
        <td className={'number'+(x.pending?' lv-amber':'')}>{x.pending}</td><td><small>{x.cats.map(c=>hrLeaveName(s,c)).join(', ')||'—'}</small></td><td><small>{x.balance}</small></td></tr>)}</tbody>
      <tfoot><tr><td>TOTAL</td><td className="number">{r.total.full}</td><td className="number">{r.total.halfAM}</td><td className="number">{r.total.halfPM}</td><td className="number"><b>{hrLeaveFmt(r.total.leaveDays)}</b></td><td className="number">{r.total.pending}</td><td></td><td></td></tr></tfoot></table></div>
      :<Empty title="No leave records for this month."/>}
    <p className="footnote padded">FY balance for {r.fy}: approved days used / the staff member’s quota (an override, else the default). The CSV quotes every cell as SCC does.</p></section>;
}

/** SCC Staff Master settings: entitlement defaults, per-staff ⚙ overrides, week-off days, advisory department caps, blackouts / store holidays. */
export function LeaveSettingsView({phone,asOf}:{phone:boolean;asOf:string}){
  const {s,uid,open,run,busy}=useHcc();const owner=hrCanRates(s,uid)&&!hrLeaveReadOnly(s,asOf),p=hrLeavePolicy(s),x=hrSlice(s);
  const wo=hrLeaveWeekOff(s),bks=p.blackouts??[];
  const deptName=(id:string)=>id?x.depts.find(d=>d.id===id)?.name??id:'All departments';
  const removeBk=(id:string)=>{if(window.confirm('Remove this blackout?'))run({type:'HR_LEAVE_BLACKOUT_REMOVE',id},'Blackout removed');};
  const rows:[string,string][]=[
    ['Entitlement defaults (days / FY)',`Casual ${p.ent.CL} · Sick ${p.ent.SL} · Earned ${p.ent.EL} (SCC: ${HR_ENT_DEFAULTS.CL} · ${HR_ENT_DEFAULTS.SL} · ${HR_ENT_DEFAULTS.EL}) — Maternity / Comp-Off / Other are not quota-tracked`],
    ['Leave week-off days',`${wo.length?wo.map(d=>HR_WEEKDAYS[d]).join(' + '):'None — every day can be a leave day'} (SCC default ${HR_SCC_WEEKOFF_DEFAULT.map(d=>HR_WEEKDAYS[d]).join(' + ')}; untick both for a 7-day trading week)`],
    ['Leave records per day',`${HR_MAX_DAY} for the whole hotel (pending + approved; a full date is skipped) · ${HR_MAX_SHIFT} per shift`],
    ...x.depts.map(d=>[`Advisory cap · ${d.name}`,`${hrDeptCap(s,d.id)}${p.deptCap[d.id]===undefined?' (default = '+HR_MAX_DAY+')':''} — flags “over capacity”, never blocks`] as [string,string])];
  const overrides=x.staff.filter(t=>t.active);
  const edit=owner?<div className="inline"><button className="btn primary" onClick={()=>open({type:'hr-leave-policy'})}><Settings2 size={phone?18:16}/> Defaults, week-off and caps</button>
    <button className="btn" onClick={()=>open({type:'hr-leave-blackout'})}><Plus size={phone?18:16}/> Blackout / store holiday</button></div>:null;
  const bkText=(b:typeof bks[number])=>`${b.from===b.to?hrLeaveDateShort(b.from):hrLeaveDateShort(b.from)+' – '+hrLeaveDateShort(b.to)} · ${deptName(b.dept)} · ${b.kind==='holiday'?'Store holiday':b.hard?'Blackout (hard block)':'Blackout (warn)'}`;
  const ent=(id:string)=>{const e=hrEntFor(s,id);return HR_TRACKED.map(c=>`${c} ${e[c]}`).join(' · ')+'/FY'+(p.perStaff?.[id]?' (custom)':'');};
  if(phone)return <><ReadOnlyBanner s={s} asOf={asOf}/>
    <div className="summary-list hr-m-rates">{rows.map(([k,v])=><div key={k}><span>{k}</span><strong>{v}</strong></div>)}</div>
    <h2 className="m-group">BLACKOUTS AND STORE HOLIDAYS</h2>
    {bks.length?bks.map(b=><ListCard key={b.id} title={b.label||(b.kind==='holiday'?'Store Holiday':'Blackout')} sub={bkText(b)} onClick={owner?()=>removeBk(b.id):undefined} right={owner?<Trash2 size={16} aria-label="Remove"/>:undefined}/>)
      :<p className="hr-m-note">No blackout dates or store holidays set.</p>}
    <h2 className="m-group">STAFF ENTITLEMENTS</h2>
    {overrides.map(t=><ListCard key={t.id} title={t.name} sub={ent(t.id)} onClick={owner?()=>open({type:'hr-leave-ent',id:t.id}):undefined}/>)}
    <div className="info-note"><Info size={16}/><span>{HR_SCC_DAYS}</span></div>
    {!owner&&<p className="hr-m-note">Only the Owner changes leave settings (DEC-058).</p>}
    {edit&&<StickyFooter>{edit}</StickyFooter>}</>;
  return <><ReadOnlyBanner s={s} asOf={asOf}/>
    <section className="card"><div className="section-title"><div><h2>Leave settings</h2><p>SCC LeaveDesk settings (Owner, DEC-058). New leave only; recorded leave keeps its dates.</p></div>{edit}</div>
      <div className="summary-list padded">{rows.map(([k,v])=><div key={k}><span>{k}</span><strong>{v}</strong></div>)}</div>
      <div className="info-note"><Info size={16}/><span>{HR_SCC_DAYS}</span></div>
      <p className="footnote padded"><MstLink spec="leaveTypes"/> <MstLink spec="holidays"/></p></section>
    <section className="card"><div className="section-title"><div><h2>Blackouts and store holidays</h2><p>A hard blackout refuses leave; a soft one (and a store holiday) asks for confirmation (SCC). Scope: every department or one.</p></div></div>
      {bks.length?<div className="table-wrap"><table><thead><tr><th>Label</th><th>Dates · scope · kind</th><th></th></tr></thead><tbody>{bks.map(b=><tr key={b.id}><td><strong>{b.label||(b.kind==='holiday'?'Store Holiday':'Blackout')}</strong></td><td>{bkText(b)}</td>
        <td className="hr-actions">{owner&&<button className="text-btn" disabled={busy} onClick={()=>removeBk(b.id)}>Remove</button>}</td></tr>)}</tbody></table></div>
        :<p className="footnote padded">No blackout dates or store holidays set.</p>}</section>
    <section className="card"><div className="section-title"><div><h2>Staff entitlements</h2><p>CL · SL · EL per financial year (April–March): the staff member’s override, else the defaults. ⚙ sets an override; all blank = back to the defaults.</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Staff</th><th>Entitlement</th><th></th></tr></thead><tbody>{overrides.map(t=><tr key={t.id}><td><strong>{t.name}</strong><small>{t.id} · {x.depts.find(d=>d.id===t.dept)?.name??t.dept}</small></td><td>{ent(t.id)}</td>
        <td className="hr-actions">{owner&&<button className="text-btn" onClick={()=>open({type:'hr-leave-ent',id:t.id})}>⚙ Set</button>}</td></tr>)}</tbody></table></div></section></>;
}
