// Module 19 · desktop page: My checklists · Handover · Dashboard · Templates (managers).
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Send,CheckCheck,Plus,Pencil,Power,RotateCcw,TimerReset,FlaskConical,AlertTriangle,Printer} from 'lucide-react';
import {useHcc} from '../../app/context';
import {hovSlice,hovMyRuns,hovIncoming,hovNotesFor,hovDesksFor,hovIsManager,hovHasAccess,hovDashboard,hovDates,hovOverdueList,hovShiftAt,hovProgress,hovTime,hovOpenCarry,hovClockCap,HOV_SHIFT_TIMES,addMinutes} from '../../services/mock';
import type {HOVRun,HOVDashRow} from '../../services/mock';
import {PageTitle,Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {HovRunHead,HovItemRow,HovNoteView,HovRunPills,HovProgressBar,hovKey,hovName} from './shared';

export type HovTab='checklists'|'handover'|'dashboard'|'templates';
export function useHovTabs(){const {s,uid}=useHcc();const mgr=hovIsManager(s,uid);
  const tabs:{id:HovTab;label:string;count?:number}[]=[{id:'checklists',label:'My checklists',count:hovMyRuns(s,uid).filter(r=>r.status==='Open').length},
    {id:'handover',label:'Handover',count:hovIncoming(s,uid).length}];
  if(mgr)tabs.push({id:'dashboard',label:'Dashboard'},{id:'templates',label:'Templates',count:hovSlice(s).templates.length});
  return tabs;}

/** Submit / reopen footer for a run. */
export function HovRunActions({run}:{run:HOVRun}){
  const {s,uid,run:exec,busy,open}=useHcc();const p=hovProgress(s,run),mgr=hovIsManager(s,uid);
  const inc=hovIncoming(s,uid,run.desk);
  if(run.status==='Submitted')return <div className="hov-run-actions"><small>Submitted by {hovName(s,run.submittedBy)} at {run.submittedAt&&hovTime(run.submittedAt)}{run.late?' · after shift end':''}</small>
    {mgr&&run.date===s.date&&<button className="btn" onClick={()=>open({type:'hov-reopen',id:run.template,room:run.date})}><RotateCcw size={15}/>Reopen</button>}</div>;
  const missing=p.mandatory-p.mandatoryDone;
  const why=missing?`${missing} mandatory item${missing===1?'':'s'} left`:inc.length?'Acknowledge the incoming handover note first':'';
  return <div className="hov-run-actions">{why&&<small className="f-why">{why}</small>}{run.reopenReason&&<small>Reopened: {run.reopenReason}</small>}
    {inc.length>0&&!missing&&<button className="btn" onClick={()=>open({type:'hov-ack',id:inc[0].id})}><CheckCheck size={15}/>Read & acknowledge</button>}
    <button className="btn primary" disabled={busy||!!why} onClick={()=>exec({type:'HOV_RUN_SUBMIT',template:run.template,key:hovKey(s,'submit',run.id)},'Checklist submitted')}><Send size={15}/>Submit checklist</button></div>;}

function RunCard({run}:{run:HOVRun}){
  const {s}=useHcc();const editable=run.status==='Open'&&run.date===s.date&&!s.closedDates.includes(s.date);
  return <section className="card hov-run"><HovRunHead run={run}/>
    <ul className="hov-items">{run.items.map(i=><HovItemRow key={i.id} run={run} item={i} editable={editable}/>)}</ul>
    {run.date===s.date&&<HovRunActions run={run}/>}</section>;}

function IncomingBanner(){
  const {s,uid,open}=useHcc();const inc=hovIncoming(s,uid);if(!inc.length)return null;
  return <div className="hov-banner" role="status"><AlertTriangle size={20}/><div><strong>{inc.length} handover note{inc.length===1?'':'s'} waiting for you</strong>
    <span>{inc.map(n=>`${n.desk} · ${n.shift} shift by ${hovName(s,n.author)}`).join(' · ')}</span></div>
    <button className="btn primary" onClick={()=>open({type:'hov-ack',id:inc[0].id})}><CheckCheck size={15}/>Read & acknowledge</button></div>;}

function Checklists(){
  const {s,uid}=useHcc();const runs=hovMyRuns(s,uid),cur=hovShiftAt(s.clock);
  const [all,setAll]=useState(false);
  const now=runs.filter(r=>r.shift===cur),later=runs.filter(r=>r.shift!==cur);
  if(!runs.length)return <Empty title="No checklists for your role today" body="Owner or Manager can add a template for your role."/>;
  return <><IncomingBanner/>
    <h2 className="hov-sub">Current shift · {cur} ({HOV_SHIFT_TIMES[cur].start}–{HOV_SHIFT_TIMES[cur].end})</h2>
    {now.length?<div className="hov-run-grid">{now.map(r=><RunCard key={r.id} run={r}/>)}</div>:<p className="muted">Nothing scheduled for this shift.</p>}
    {later.length>0&&<><h2 className="hov-sub">Other shifts today <button className="text-btn" onClick={()=>setAll(!all)}>{all?'Hide':'Show'} ({later.length})</button></h2>
      {all?<div className="hov-run-grid">{later.map(r=><RunCard key={r.id} run={r}/>)}</div>
        :<div className="hov-mini-list">{later.map(r=><div key={r.id}><strong>{r.name}</strong><small>{r.shift}</small><HovRunPills run={r}/></div>)}</div>}</>}</>;}

function Handover(){
  const {s,uid,open}=useHcc();const notes=hovNotesFor(s,uid),desks=hovDesksFor(s,uid);
  const [desk,setDesk]=useState('All');
  const shown=notes.filter(n=>desk==='All'||n.desk===desk);
  return <><IncomingBanner/>
    <div className="hov-toolbar"><div className="f-seg" role="group" aria-label="Desk">{['All',...desks].map(d=><button key={d} type="button" className={desk===d?'on':''} aria-pressed={desk===d} onClick={()=>setDesk(d)}>{d}</button>)}</div>
      <button className="btn primary" onClick={()=>open({type:'hov-note',id:desk==='All'?undefined:desk})}><Plus size={15}/>Write handover note</button></div>
    <div className="two-col hov-two">
      <div>{shown.map(n=><section className="card hov-note-card" key={n.id}><div className="section-title"><h2>{n.desk} · {n.shift} → {n.toShift}</h2>
          {n.status==='Submitted'?<span className="pill orange">Awaiting acknowledgement</span>:<Pill tone="green">Acknowledged</Pill>}</div>
          <div className="padded"><HovNoteView note={n}/>{n.status==='Submitted'&&n.author!==uid&&<div className="modal-actions"><button className="btn primary" onClick={()=>open({type:'hov-ack',id:n.id})}><CheckCheck size={15}/>Acknowledge</button></div>}</div></section>)}
        {!shown.length&&<Empty title="No handover notes yet"/>}</div>
      <section className="card"><div className="section-title"><h2>Open carry-forward</h2></div><div className="padded">
        {desks.filter(d=>desk==='All'||d===desk).map(d=>{const c=hovOpenCarry(s,d);return <div key={d} className="hov-carry-desk"><h3>{d}</h3>
          {c.length?<ul>{c.map(x=><li key={x.id}>{x.text}<small> · from {hovName(s,x.from)} {shortDate(x.date)}</small></li>)}</ul>:<p className="muted">Nothing open.</p>}</div>;})}
      </div></section></div></>;}

export function HovDashboardBody({compact}:{compact?:boolean}){
  const {s,go}=useHcc();const dates=hovDates(s);const [date,setDate]=useState(s.date);const [by,setBy]=useState<'role'|'desk'>('role');
  const rows=hovDashboard(s,date,by),over=hovOverdueList(s,date);
  const notes=hovSlice(s).notes.filter(n=>n.status==='Submitted');
  const total=rows.reduce((a,r)=>({items:a.items+r.items,done:a.done+r.done}),{items:0,done:0});
  const runs=hovSlice(s).runs.filter(r=>r.date===date);
  const lateRuns=runs.filter(r=>hovProgress(s,r).late).length;
  return <>
    <div className="hov-toolbar"><label className="hov-inline">Business date<select value={date} onChange={e=>setDate(e.target.value)}>{dates.map(d=><option key={d} value={d}>{dateLabel(d)}</option>)}</select></label>
      <div className="f-seg" role="group" aria-label="Group by">{(['role','desk'] as const).map(b=><button key={b} type="button" className={by===b?'on':''} aria-pressed={by===b} onClick={()=>setBy(b)}>By {b}</button>)}</div>
      {!compact&&<button className="btn" onClick={()=>{void printDocument();}}><Printer size={15}/>Print</button>}</div>
    <div className="small-stats hov-stats">
      <div><span>Completion</span><strong className="number">{total.items?Math.round(total.done*100/total.items):0}%</strong></div>
      <div><span>Items overdue</span><strong className="number">{over.length}</strong></div>
      <div><span>Runs past shift end</span><strong className="number">{lateRuns}</strong></div>
      <div><span>Notes not acknowledged</span><strong className="number">{notes.length}</strong></div></div>
    <section className="card"><div className="section-title"><h2>Completion by {by} · {dateLabel(date)}</h2></div>
      {compact?<div className="hov-dash-cards">{rows.map(r=><DashCard key={r.key} r={r}/>)}</div>:
      <div className="table-wrap"><table><thead><tr><th>{by==='role'?'Role':'Desk'}</th><th className="number">Checklists</th><th className="number">Submitted</th><th className="number">Not started</th><th className="number">Items done</th><th>Completion</th><th className="number">Overdue</th><th className="number">Past shift end</th></tr></thead>
        <tbody>{rows.map(r=><tr key={r.key}><td><strong>{r.label}</strong></td><td className="number">{r.runs}</td><td className="number">{r.submitted}</td><td className="number">{r.notStarted}</td><td className="number">{r.done}/{r.items}</td>
          <td><div className="hov-cell-bar"><HovProgressBar pct={r.pct} label={r.label}/><span className="number">{r.pct}%</span></div></td><td className="number">{r.overdue||'—'}</td><td className="number">{r.late||'—'}</td></tr>)}</tbody></table></div>}
    </section>
    <section className="card"><div className="section-title"><h2>Overdue items</h2><button className="text-btn" onClick={()=>go('housekeeping')}>Housekeeping board</button></div>
      {over.length?<ul className="hov-overdue-list">{over.map(({run,item})=><li key={run.id+item.id}><span className="pill hov-overdue">Due {item.due}</span><strong>{item.text}</strong><small>{run.name} · {run.roles.join(', ')}</small></li>)}</ul>:<p className="muted padded">No overdue items for this date.</p>}</section>
  </>;}
function DashCard({r}:{r:HOVDashRow}){return <div className="hov-dash-card"><div><strong>{r.label}</strong><span className="number">{r.pct}%</span></div><HovProgressBar pct={r.pct} label={r.label}/>
  <small>{r.submitted}/{r.runs} submitted · {r.done}/{r.items} items{r.overdue?` · ${r.overdue} overdue`:''}{r.notStarted?` · ${r.notStarted} not started`:''}</small></div>;}

export function HovTemplatesBody({compact}:{compact?:boolean}){
  const {s,open,run,busy}=useHcc();const ts=hovSlice(s).templates;
  return <>{!compact&&<div className="hov-toolbar"><span className="muted">Owner and Manager edit templates. Started runs keep their snapshot.</span><button className="btn primary" onClick={()=>open({type:'hov-template'})}><Plus size={15}/>New template</button></div>}
    <div className="hov-tpl-grid">{ts.map(t=><section key={t.id} className={'card hov-tpl'+(t.active?'':' inactive')}>
      <div className="section-title"><h2>{t.name}</h2>{t.active?<Pill tone="green">Active</Pill>:<Pill>Inactive</Pill>}</div>
      <div className="padded"><p>{t.desk} · {t.shift} · {t.roles.join(', ')}</p><small>{t.items.length} items · {t.items.filter(i=>i.mandatory).length} mandatory · {t.items.filter(i=>i.photo).length} photo · v{t.version} · updated by {hovName(s,t.updatedBy)}</small>
        <div className="hov-run-actions"><button className="btn" onClick={()=>open({type:'hov-template',id:t.id})}><Pencil size={15}/>Edit</button>
          <button className="btn" disabled={busy} onClick={()=>run({type:'HOV_TEMPLATE_TOGGLE',id:t.id,key:hovKey(s,'toggle',t.id)},t.active?'Template deactivated':'Template activated')}><Power size={15}/>{t.active?'Deactivate':'Activate'}</button></div></div></section>)}</div></>;}

/** Demo clock (managers): shows overdue flags appearing as simulated time passes. */
export function HovClockDemo(){
  const {s,run,busy,permission}=useHcc();if(!permission('close'))return null;
  const can=(m:number)=>addMinutes(s.clock,m)<=hovClockCap(s.date)&&!s.closedDates.includes(s.date);
  return <div className="hov-clock" aria-label="Demo clock"><FlaskConical size={15}/><span>Demo · simulated clock {hovTime(s.clock)}</span>
    {[30,60].map(m=><button key={m} className="btn hov-mini" disabled={busy||!can(m)} onClick={()=>run({type:'HOV_CLOCK',minutes:m,key:hovKey(s,'clock',m)},`Simulated clock +${m} min`)}><TimerReset size={14}/>+{m} min</button>)}</div>;}

export function HandoverPage(){
  const {s,uid}=useHcc();const tabs=useHovTabs();const [tab,setTab]=useState<HovTab>('checklists');
  if(!hovHasAccess(s,uid))return <><PageTitle title="Checklists & handover" sub="Shift checklists and handover notes"/><Empty title="No checklists for your role" body="Checklists are for front desk, cashier, housekeeping, maintenance and managers."/></>;
  const cur=tabs.some(t=>t.id===tab)?tab:'checklists';
  return <div className="hov-page"><PageTitle title="Checklists & handover" sub={`${dateLabel(s.date)} · ${hovShiftAt(s.clock)} shift · simulated clock ${hovTime(s.clock)} IST`} action={<HovClockDemo/>}/>
    <section className="card hov-tabs-card"><div className="tabs" role="tablist" aria-label="Checklists and handover">{tabs.map(t=><button key={t.id} role="tab" aria-selected={cur===t.id} className={cur===t.id?'active':''} onClick={()=>setTab(t.id)}>{t.label}{t.count!==undefined&&<span>{t.count}</span>}</button>)}</div></section>
    {cur==='checklists'&&<Checklists/>}{cur==='handover'&&<Handover/>}{cur==='dashboard'&&<HovDashboardBody/>}{cur==='templates'&&<HovTemplatesBody/>}
  </div>;}
