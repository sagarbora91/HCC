// MB-07 phone sheets: assign picker (mo-assign), reject-reason sheet and report-repair sheet (phone overrides of
// stream D's 'hk-reject' and 'room-repair' — same commands, larger targets, quick-pick reasons).
import React,{useState} from 'react';
import {Check,UserRound,Wrench,TriangleAlert,Hammer} from 'lucide-react';
import {hkTaskKind,hkKindLabel,HK_REPAIR_ASSIGNEE} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty} from '../../components/ui';
import {moAssignPool} from './logic';
const first=(name?:string)=>name?.split(' · ')[0]??'';

export function MoAssignSheet({id}:{id?:string}){
  const {s,run,busy,closeModal}=useHcc();
  const t=s.tasks.find(x=>x.id===id);
  if(!t)return <Empty title="This task is unavailable"/>;
  const pool=moAssignPool(s,t),kind=hkTaskKind(t);
  return <><div className="modal-heading"><div className="eyebrow">ROOM {t.room} · {hkKindLabel[kind].toUpperCase()}</div><h2>Assign to</h2><p>Tap a name. {kind==='repair'?'Only enabled maintenance staff are listed.':'Only enabled housekeepers are listed.'}</p></div>
    <div className="mo-pick" role="radiogroup" aria-label={'Assign room '+t.room}>{pool.map(u=>{const load=s.tasks.filter(x=>x.assignee===u.id&&x.status!=='Done').length,on=u.id===t.assignee;
      return <button type="button" role="radio" aria-checked={on} key={u.id} className={on?'on':''} disabled={busy}
        onClick={()=>on?closeModal():run({type:'TASK',id:t.id,action:'assign',assignee:u.id},`Room ${t.room} assigned to ${first(u.name)}`,{close:true})}>
        <UserRound size={20}/><span><strong>{first(u.name)}</strong><small>{u.role} · {load} open task{load===1?'':'s'}</small></span>{on&&<Check size={20}/>}</button>;})}
      {!pool.length&&<p className="mo-empty">No enabled staff can take this task.</p>}</div></>;
}

const rejectReasons=['Bathroom not clean','Linen not changed','Amenities missing','Dust on surfaces'];
export function MoRejectSheet({id}:{id?:string}){
  const {s,run,modal,closeModal,busy}=useHcc();
  const t=s.tasks.find(x=>x.id===id);const [reason,setReason]=useState(''),[touched,setTouched]=useState(false);
  if(!t)return <Empty title="This task is unavailable"/>;
  const ok=reason.trim().length>=3;
  return <><div className="modal-heading"><div className="eyebrow">INSPECTION · ROOM {t.room}</div><h2>Return for cleaning</h2><p>The checklist is cleared and the housekeeper sees your reason on the task.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();setTouched(true);if(ok)run({type:'TASK',id:t.id,action:'reject',reason:reason.trim(),key:modal!.key},'Returned for cleaning with reason',{close:true});}}>
      <div className="mo-quick" role="group" aria-label="Common reasons">{rejectReasons.map(q=><button type="button" key={q} aria-pressed={reason===q} className={reason===q?'on':''} onClick={()=>{setReason(q);setTouched(true);}}>{q}</button>)}</div>
      <label>Reason (required)<textarea rows={3} maxLength={200} value={reason} enterKeyHint="done" onChange={e=>setReason(e.target.value)} onBlur={()=>setTouched(true)} aria-invalid={touched&&!ok} placeholder="What needs to be redone?"/></label>
      {touched&&!ok&&<small className="mo-hint error" role="alert"><TriangleAlert size={14}/> Enter a reason of at least 3 characters.</small>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn danger" disabled={busy}>Return for cleaning</button></div>
    </form></>;
}

const repairReasons=['Tap or shower leaking','AC not cooling','Light or switch not working','Door lock faulty','TV / remote not working'];
export function MoRepairSheet({id}:{id?:string}){
  const {s,run,modal,closeModal,busy}=useHcc();
  const r=s.rooms.find(x=>x.id===id);const [reason,setReason]=useState(''),[touched,setTouched]=useState(false);
  if(!r)return <Empty title="This room is unavailable"/>;
  const maint=s.users.find(u=>u.id===HK_REPAIR_ASSIGNEE);
  const existing=s.tasks.find(t=>t.room===r.id&&t.kind==='repair'&&t.status!=='Done');
  const ok=reason.trim().length>=3;
  return <><div className="modal-heading"><div className="eyebrow">ROOM {r.id}</div><h2>Report a repair</h2><p>Creates a repair task for {first(maint?.name)||'Maintenance'} (Maintenance). A supervisor decides whether to block the room.</p></div>
    {existing?<><div className="info-note" role="status"><Wrench size={16}/>A repair is already open for this room: “{existing.note}”.</div><div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div></>:
    <form className="form" onSubmit={e=>{e.preventDefault();setTouched(true);if(ok)run({type:'REPAIR_REQUEST',room:r.id,reason:reason.trim(),key:modal!.key},'Repair reported to Maintenance',{close:true});}}>
      <div className="mo-quick" role="group" aria-label="Common problems">{repairReasons.map(q=><button type="button" key={q} aria-pressed={reason===q} className={reason===q?'on':''} onClick={()=>{setReason(q);setTouched(true);}}>{q}</button>)}</div>
      <label>What needs repair? (required)<textarea rows={3} maxLength={200} value={reason} enterKeyHint="done" onChange={e=>setReason(e.target.value)} onBlur={()=>setTouched(true)} aria-invalid={touched&&!ok} placeholder="e.g. Shower mixer leaking"/></label>
      {touched&&!ok&&<small className="mo-hint error" role="alert"><TriangleAlert size={14}/> Describe the repair in at least 3 characters.</small>}
      {!maint?.enabled&&<small className="mo-hint error">The Maintenance user is disabled; the request will be refused.</small>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}><Hammer size={17}/> Report repair</button></div>
    </form>}</>;
}
