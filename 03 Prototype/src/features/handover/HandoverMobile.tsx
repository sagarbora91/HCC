// Module 19 · phone page (phone-first): chip sections, run list → run detail with 56 px checkboxes and a sticky Submit.
import React,{useState} from 'react';
import {ArrowLeft,Send,CheckCheck,PenLine,Plus,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {ListCard,ChipFilter,StickyFooter,Fab} from '../../app/mobile/components';
import {hovSlice,hovMyRuns,hovIncoming,hovNotesFor,hovIsManager,hovHasAccess,hovShiftAt,hovProgress,hovTime,hovOpenCarry,hovDesksFor} from '../../services/mock';
import {Empty,Pill} from '../../components/ui';
import {HovRunHead,HovItemRow,HovNoteView,HovRunPills,HovProgressBar,hovKey,hovName} from './shared';
import {useHovTabs,HovDashboardBody,HovClockDemo,HovTab} from './HandoverPage';

function RunDetail({id,onBack}:{id:string;onBack:()=>void}){
  const {s,uid,run:exec,busy,open}=useHcc();
  const r=hovMyRuns(s,uid).find(r=>r.id===id);
  if(!r)return <><button className="btn hov-back" onClick={onBack}><ArrowLeft size={18}/>All checklists</button><Empty title="Checklist unavailable"/></>;
  const p=hovProgress(s,r),editable=r.status==='Open'&&!s.closedDates.includes(s.date);
  const inc=hovIncoming(s,uid,r.desk),missing=p.mandatory-p.mandatoryDone;
  return <div className="hov-m-detail">
    <button className="btn hov-back" onClick={onBack}><ArrowLeft size={18}/>All checklists</button>
    <HovRunHead run={r}/>
    <ul className="hov-items hov-m-items">{r.items.map(i=><HovItemRow key={i.id} run={r} item={i} editable={editable}/>)}</ul>
    {r.status==='Submitted'?<p className="hov-m-done">Submitted by {hovName(s,r.submittedBy)} at {r.submittedAt&&hovTime(r.submittedAt)}{r.late?' · after shift end':''}.</p>:
    <StickyFooter>{missing>0?<button className="btn primary full" disabled>{missing} mandatory item{missing===1?'':'s'} left</button>
      :inc.length?<button className="btn primary full" onClick={()=>open({type:'hov-ack',id:inc[0].id})}><CheckCheck size={18}/>Acknowledge handover first</button>
      :<button className="btn primary full" disabled={busy} onClick={()=>exec({type:'HOV_RUN_SUBMIT',template:r.template,key:hovKey(s,'submit',r.id)},'Checklist submitted',{after:onBack})}><Send size={18}/>Submit checklist</button>}</StickyFooter>}
  </div>;}

function Checklists({onPick}:{onPick:(id:string)=>void}){
  const {s,uid,open}=useHcc();const runs=hovMyRuns(s,uid),cur=hovShiftAt(s.clock),inc=hovIncoming(s,uid);
  if(!runs.length)return <Empty title="No checklists for your role today"/>;
  return <>{inc.length>0&&<button type="button" className="hov-banner hov-m-banner" onClick={()=>open({type:'hov-ack',id:inc[0].id})}><AlertTriangle size={20}/><div><strong>Handover waiting</strong><span>{inc[0].desk} · from {hovName(s,inc[0].author)} — tap to read & acknowledge</span></div></button>}
    {runs.map(r=>{const p=hovProgress(s,r);return <ListCard key={r.id} onClick={()=>onPick(r.id)} tone={p.overdue?'hov-lc-overdue':''}
      title={r.name} sub={<>{r.shift}{r.shift===cur?' · now':''} · {p.done}/{p.total} done</>}
      meta={<span className="hov-lc-meta"><HovProgressBar pct={p.pct} label={r.name}/><HovRunPills run={r}/></span>} right={<strong className="number">{p.pct}%</strong>}/>;})}</>;}

function Handover(){
  const {s,uid,open}=useHcc();const notes=hovNotesFor(s,uid);
  return <>{hovDesksFor(s,uid).map(d=>{const c=hovOpenCarry(s,d);return c.length?<div key={d} className="card hov-m-carry"><h3>{d} · open carry-forward</h3><ul>{c.map(x=><li key={x.id}>{x.text}</li>)}</ul></div>:null;})}
    {notes.map(n=><div key={n.id} className="card hov-m-note"><div className="hov-m-note-head"><strong>{n.desk} · {n.shift} → {n.toShift}</strong>
      {n.status==='Submitted'?<span className="pill orange">Awaiting</span>:<Pill tone="green">Acknowledged</Pill>}</div>
      <HovNoteView note={n}/>
      {n.status==='Submitted'&&n.author!==uid&&<button className="btn primary full hov-m-ack" onClick={()=>open({type:'hov-ack',id:n.id})}><CheckCheck size={18}/>Read & acknowledge</button>}</div>)}
    {!notes.length&&<Empty title="No handover notes yet"/>}
    <Fab label="Write handover" icon={PenLine} onClick={()=>open({type:'hov-note'})}/></>;}

function Templates(){
  const {s,open}=useHcc();
  return <>{hovSlice(s).templates.map(t=><ListCard key={t.id} title={t.name} sub={`${t.desk} · ${t.shift} · ${t.roles.join(', ')}`} meta={`${t.items.length} items · v${t.version}`}
    right={t.active?<Pill tone="green">Active</Pill>:<Pill>Inactive</Pill>} onClick={()=>open({type:'hov-template',id:t.id})}/>)}
    <Fab label="New template" icon={Plus} onClick={()=>open({type:'hov-template'})}/></>;}

export function HandoverMobile(){
  const {s,uid}=useHcc();const tabs=useHovTabs();const [tab,setTab]=useState<HovTab>('checklists');const [pick,setPick]=useState('');
  if(!hovHasAccess(s,uid))return <Empty title="No checklists for your role"/>;
  if(pick&&tab==='checklists')return <div className="hov-m"><RunDetail id={pick} onBack={()=>setPick('')}/></div>;
  const cur=tabs.some(t=>t.id===tab)?tab:'checklists';
  return <div className="hov-m">
    <ChipFilter label="Section" options={tabs} value={cur} onChange={v=>setTab(v)}/>
    {hovIsManager(s,uid)&&cur==='dashboard'&&<HovClockDemo/>}
    {cur==='checklists'&&<Checklists onPick={setPick}/>}
    {cur==='handover'&&<Handover/>}
    {cur==='dashboard'&&<HovDashboardBody compact/>}
    {cur==='templates'&&<Templates/>}
  </div>;}
