// Module 19 · shared UI pieces for desktop and phone (checkbox rows, progress, note view, helpers).
import React from 'react';
import {Check,Camera,StickyNote,ExternalLink,AlertTriangle,Clock,KeyRound,Users,Banknote,ListChecks,CheckCircle2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {navItems} from '../../app/nav';
import {money,hovProgress,hovItemOverdue,hovSeesCash,hovInDesk,hovTime,HOV_SHIFT_TIMES} from '../../services/mock';
import type {HOVRun,HOVRunItem,HOVNote,State} from '../../services/mock';
import {Pill,shortDate} from '../../components/ui';

/** Unique idempotency key per user action (HOV commands do not allocate sequence IDs). */
export const hovKey=(s:State,...parts:(string|number|boolean|undefined)[])=>['hov',...parts,s.clock,s.audit.length].join(':');
export const hovName=(s:State,id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
/** Page label + whether the current user may open it (link-outs via go()). */
export function useHovLink(){const {permission}=useHcc();
  return (page?:string)=>{if(!page)return null;const n=navItems.find(n=>n.id===page);return n&&permission(n.p)?n:null;};}

export function HovProgressBar({pct,label}:{pct:number;label?:string}){
  return <div className="hov-progress" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label={label||'Completion'}><span style={{width:pct+'%'}}/></div>;}

export function HovRunPills({run}:{run:HOVRun}){const {s}=useHcc();const p=hovProgress(s,run);
  return <span className="hov-pills">
    {run.status==='Submitted'?<Pill tone="green"><CheckCircle2 size={12}/>Submitted{run.late?' · late':''}</Pill>:p.done===0&&!run.items.some(i=>i.at)?<Pill>Not started</Pill>:<Pill tone="blue">In progress</Pill>}
    {p.overdue>0&&<span className="pill hov-overdue"><AlertTriangle size={12}/>{p.overdue} overdue</span>}
    {run.status==='Open'&&p.late&&<span className="pill hov-overdue"><Clock size={12}/>Shift ended</span>}
  </span>;}

export function HovRunHead({run,compact}:{run:HOVRun;compact?:boolean}){const {s}=useHcc();const p=hovProgress(s,run);
  return <div className="hov-run-head">
    <div><h3>{run.name}</h3><small>{run.desk} · {run.shift} {HOV_SHIFT_TIMES[run.shift].start}–{HOV_SHIFT_TIMES[run.shift].end} · {run.roles.join(', ')}</small></div>
    <div className="hov-run-meter"><strong className="number">{p.pct}%</strong><small>{p.done}/{p.total} done · mandatory {p.mandatoryDone}/{p.mandatory}</small></div>
    <HovProgressBar pct={p.pct} label={run.name+' completion'}/>
    {!compact&&<HovRunPills run={run}/>}
  </div>;}

/** One checklist item: big checkbox (≥48 px), text, due/mandatory/photo pills, note/photo + link-out buttons. */
export function HovItemRow({run,item,editable}:{run:HOVRun;item:HOVRunItem;editable:boolean}){
  const {s,run:exec,busy,open,go}=useHcc();const link=useHovLink()(item.link);
  const overdue=hovItemOverdue(s,run,item);
  const toggle=()=>{
    if(!item.done&&item.photo&&!item.photoName){open({type:'hov-item',id:run.template,room:item.id});return;}
    exec({type:'HOV_ITEM',template:run.template,item:item.id,done:!item.done,key:hovKey(s,run.template,item.id,!item.done)},item.done?'Item unticked':'Ticked · '+item.text);};
  return <li className={'hov-item'+(item.done?' done':'')+(overdue?' overdue':'')}>
    <button type="button" className="hov-check" role="checkbox" aria-checked={item.done} disabled={!editable||busy} onClick={toggle} aria-label={item.text}>{item.done&&<Check size={24}/>}</button>
    <div className="hov-item-body">
      <span className="hov-item-text">{item.text}</span>
      <span className="hov-item-meta">
        {item.mandatory?<span className="pill hov-req">Mandatory</span>:<span className="pill">Optional</span>}
        {item.due&&<span className={'pill '+(overdue?'hov-overdue':'')}><Clock size={11}/>{overdue?'Overdue · was due ':'Due '}{item.due}</span>}
        {item.photo&&<span className="pill blue"><Camera size={11}/>Photo{item.photoName?' · '+item.photoName:' needed'}</span>}
        {item.done&&item.at&&<small>✓ {hovName(s,item.by)} · {hovTime(item.at)}</small>}
      </span>
      {item.note&&<span className="hov-item-note"><StickyNote size={13}/>{item.note}</span>}
    </div>
    <div className="hov-item-actions">
      {editable&&<button type="button" className="btn hov-mini" onClick={()=>open({type:'hov-item',id:run.template,room:item.id})} aria-label={'Note or photo for '+item.text}><StickyNote size={15}/><span>Note / photo</span></button>}
      {link&&item.link!=='handover'&&<button type="button" className="btn hov-mini" onClick={()=>go(link.id)} aria-label={'Open '+link.label}><ExternalLink size={15}/><span>{link.label}</span></button>}
      {item.link==='handover'&&<span className="hov-hint">Handover tab</span>}
    </div>
  </li>;}

/** Handover note body (cash only for money roles). */
export function HovNoteView({note}:{note:HOVNote}){
  const {s,uid,run,busy}=useHcc();const cash=hovSeesCash(s,uid);const member=hovInDesk(s,uid,note.desk);
  return <div className="hov-note">
    <div className="hov-note-grid">
      <div><span><AlertTriangle size={14}/>Open issues</span><p>{note.nothing&&!note.issues?'Nothing to report.':note.issues||'—'}</p></div>
      <div><span><KeyRound size={14}/>Keys</span><p>{note.keys||'—'}</p></div>
      <div><span><Users size={14}/>Guest notes</span><p>{note.guests||'—'}</p></div>
      {note.cash&&cash&&<div><span><Banknote size={14}/>Cash float handed over</span><p><strong>{money(note.cash.amount)}</strong>{note.cash.note&&<> · {note.cash.note}</>}</p></div>}
    </div>
    {note.carry.length>0&&<div className="hov-carry"><span><ListChecks size={14}/>Carry-forward</span><ul>
      {note.carry.map(c=><li key={c.id} className={c.done?'done':''}>
        {c.done?<Check size={15} aria-label="Done"/>:<span className="hov-dot" aria-hidden/>}<span>{c.text}{c.done&&<small> · done by {hovName(s,c.doneBy)} {c.doneAt&&hovTime(c.doneAt)}</small>}</span>
        {!c.done&&member&&<button type="button" className="btn hov-mini" disabled={busy} onClick={()=>run({type:'HOV_CARRY_DONE',note:note.id,item:c.id,key:hovKey(s,c.id)},'Carry-forward item done')}>Mark done</button>}
      </li>)}</ul></div>}
    <small className="hov-note-foot">{hovName(s,note.author)} · {note.desk} · {note.shift} → {note.toShift} · {shortDate(note.date)} {hovTime(note.at)}
      {note.status==='Acknowledged'?<> · acknowledged by {hovName(s,note.ackBy)} {note.ackAt&&hovTime(note.ackAt)}{note.ackRemark&&<> — “{note.ackRemark}”</>}</>:<> · <b className="hov-await">awaiting acknowledgement</b></>}</small>
  </div>;}
