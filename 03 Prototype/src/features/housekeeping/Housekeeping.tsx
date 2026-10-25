// SCR-12 Housekeeping: supervisor board (filters by floor/assignee, reassign, inspect/reject with reason, stayover
// generation) and the housekeeper/maintenance "My tasks" view (own tasks first, large controls, no financial data).
import React,{useState} from 'react';
import {Brush,ArrowRight,Check,CheckCheck,Wrench,BedDouble,CalendarClock,Info,Sparkles,TriangleAlert,Lock} from 'lucide-react';
import {Task,assignedScopeRoles,hkTaskKind,hkKindLabel,hkArrivalToday,hkSortTasks,hkStayoverRooms,hkStayoverId} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,shortDate,clockTime} from '../../components/ui';
const columns=['To do','In progress','Inspection','Done'] as const;
const columnLabel:Record<string,string>={'To do':'To do','In progress':'In progress','Inspection':'Awaiting inspection','Done':'Done'};
const dotFor:Record<string,string>={'To do':'dirty','In progress':'cleaning','Inspection':'inspection','Done':'ready'};
export const hkChecklist=['Linen & bathroom','Surfaces & amenities','Final room check'];
const kindTone:Record<string,string>={checkout:'neutral',move:'neutral',stayover:'blue',repair:'orange'};
const first=(name?:string)=>name?.split(' · ')[0]??'';

export function Housekeeping(){
  const {s,user,run,permission,taskList,busy}=useHcc();
  const scoped=assignedScopeRoles.includes(user.role);
  const [floor,setFloor]=useState('All'),[assignee,setAssignee]=useState('All');
  const floors=[...new Set(s.rooms.map(r=>r.floor))].sort();
  const assignees=[...new Set(s.tasks.map(t=>t.assignee))].map(id=>s.users.find(u=>u.id===id)).filter(Boolean) as typeof s.users;
  const list=taskList.filter(t=>(floor==='All'||String(s.rooms.find(r=>r.id===t.room)?.floor)===floor)&&(assignee==='All'||t.assignee===assignee));
  const pendingStay=hkStayoverRooms(s).filter(r=>!s.tasks.some(t=>t.id===hkStayoverId(r,s.date))).length;
  const generate=permission('inspect')?<button className="btn" disabled={busy||pendingStay===0} title={pendingStay?'':'Stayover tasks already exist for every in-house room today'} onClick={()=>run({type:'STAYOVER_GENERATE',key:'stayover:'+s.date+':'+s.sequence},'Stayover tasks generated')}><Sparkles size={15}/> Generate stayover tasks{pendingStay?` (${pendingStay})`:' · all created'}</button>:undefined;

  if(scoped)return <><PageTitle title="My tasks" sub={user.role==='Maintenance'?'Your assigned repairs. Rooms stay blocked until a supervisor releases them.':'Your assigned rooms. One thoughtful detail at a time.'}/>
    <div className="small-stats">{columns.map(c=><div key={c}><span>{columnLabel[c]}</span><strong>{taskList.filter(x=>x.status===c).length}</strong></div>)}</div>
    <div className="hk-mine">{hkSortTasks(s,taskList).map(t=><TaskCard key={t.id} t={t} large/>)}
      {!taskList.length&&<p className="column-empty">No tasks are assigned to you right now.</p>}</div></>;

  return <><PageTitle title="Housekeeping" sub="A well-kept room is the beginning of a good stay." action={generate}/>
    <div className="small-stats">{columns.map(c=><div key={c}><span>{columnLabel[c]}</span><strong>{list.filter(x=>x.status===c).length}</strong></div>)}</div>
    <div className="hk-filters" role="group" aria-label="Filter tasks">
      <label>Floor<select value={floor} onChange={e=>setFloor(e.target.value)}><option value="All">All floors</option>{floors.map(f=><option key={f} value={String(f)}>Floor {f}</option>)}</select></label>
      <label>Assignee<select value={assignee} onChange={e=>setAssignee(e.target.value)}><option value="All">Everyone</option>{assignees.map(u=><option key={u.id} value={u.id}>{first(u.name)} · {u.role}</option>)}</select></label>
      {(floor!=='All'||assignee!=='All')&&<button className="text-btn" onClick={()=>{setFloor('All');setAssignee('All');}}>Clear filters</button>}
      <span className="muted">{list.length} of {taskList.length} tasks</span>
    </div>
    <div className="kanban">{columns.map(status=><section className="kanban-col" key={status} aria-label={columnLabel[status]}>
      <h3><i className={'dot '+dotFor[status]}/>{columnLabel[status]}<span>{list.filter(t=>t.status===status).length}</span></h3>
      {hkSortTasks(s,list.filter(t=>t.status===status)).map(t=><TaskCard key={t.id} t={t}/>)}
      {!list.some(t=>t.status===status)&&<p className="column-empty">No rooms here</p>}
    </section>)}</div></>;
}

function age(created:string|undefined,clock:string){
  if(!created)return '';
  const mins=Math.max(0,Math.round((Date.parse(clock+':00Z')-Date.parse(created+':00Z'))/60000));
  return mins<60?`${mins} min`:mins<1440?`${Math.floor(mins/60)} h ${mins%60} min`:`${Math.floor(mins/1440)} d`;
}

export function TaskCard({t,large}:{t:Task;large?:boolean}){
  const {s,run,permission,open,busy}=useHcc();
  const room=s.rooms.find(r=>r.id===t.room),who=s.users.find(u=>u.id===t.assignee);
  const kind=hkTaskKind(t),arrival=hkArrivalToday(s,t.room);
  const pool=s.users.filter(u=>u.enabled&&u.role===(kind==='repair'?'Maintenance':'Housekeeper'));
  const btn='btn full'+(large?' hk-big':'');
  const Icon=kind==='repair'?Wrench:kind==='stayover'?BedDouble:Brush;
  const verb=kind==='repair'?'repair':kind==='stayover'?'service':'cleaning';
  const canReport=permission('housekeeping')||permission('reservations')||permission('inspect');
  const openRepair=s.tasks.some(x=>x.room===t.room&&x.kind==='repair'&&x.status!=='Done');
  return <article className={'task-card hk-card '+(large?'hk-large':'')} aria-label={`Room ${t.room} · ${hkKindLabel[kind]} · ${t.status}`}>
    <div className="flex-between"><h2>Room {t.room}</h2><Icon size={19} aria-hidden/></div>
    <div className="hk-tags"><Pill tone={kindTone[kind]}>{hkKindLabel[kind]}</Pill>{arrival&&t.status!=='Done'&&<Pill tone="orange"><CalendarClock size={12}/> Priority · Arrival today</Pill>}{large&&<Pill>{columnLabel[t.status]}</Pill>}</div>
    <p>{room?.type} · Floor {room?.floor}{t.createdAt?` · created ${clockTime(t.createdAt)} (${age(t.createdAt,s.clock)} ago)`:''}</p>
    {room?.blocked&&<div className="hk-note warn"><Lock size={13}/> Blocked · {room.blockReason||'Out of service'}{room.blockFrom||room.blockTo?` · ${room.blockFrom?shortDate(room.blockFrom):'now'} → ${room.blockTo?shortDate(room.blockTo):'open-ended'}`:''}</div>}
    {t.note&&<div className="hk-note"><Wrench size={13}/> {t.note}</div>}
    {t.rejectReason&&t.status!=='Done'&&<div className="hk-note warn"><TriangleAlert size={13}/> Returned: {t.rejectReason}</div>}
    <div className="assignee"><span className="avatar small">{who?.name[0]??'?'}</span>{first(who?.name)||t.assignee}{who&&!who.enabled?' (disabled)':''}</div>
    {permission('inspect')&&t.status!=='Done'&&!large&&<select aria-label={'Assign room '+t.room} value={t.assignee} disabled={busy} onChange={e=>run({type:'TASK',id:t.id,action:'assign',assignee:e.target.value},'Task reassigned')}>
      {!pool.some(h=>h.id===t.assignee)&&<option value={t.assignee}>{first(who?.name)||t.assignee}</option>}
      {pool.map(h=><option key={h.id} value={h.id}>{first(h.name)}</option>)}</select>}
    {t.status==='In progress'&&kind!=='repair'&&<div className={'checklist'+(large?' hk-big-check':'')}>{hkChecklist.map((l,i)=><label key={l}><input type="checkbox" checked={!!t.checked[i]} disabled={busy} onChange={()=>run({type:'TASK',id:t.id,action:'check',index:i},'Checklist updated')}/>{l}</label>)}</div>}
    {t.status==='To do'&&<button className={btn} disabled={busy} onClick={()=>run({type:'TASK',id:t.id,action:'start'},`Started ${verb}`)}>Start {verb} <ArrowRight size={15}/></button>}
    {t.status==='In progress'&&(()=>{const ready=kind==='repair'||t.checked.every(Boolean);
      return <><button className={btn+' primary'} disabled={busy||!ready} onClick={()=>run({type:'TASK',id:t.id,action:'complete'},kind==='repair'?'Repair marked done. A supervisor will unblock the room.':kind==='stayover'?'Stayover service completed':'Cleaning completed')}>{kind==='repair'?'Mark repair done':`Complete ${verb}`}</button>
        {!ready&&<small className="hk-reason">Tick all checklist items to complete.</small>}</>;})()}
    {t.status==='Inspection'&&(permission('inspect')
      ?<div className="stack"><button className={btn+' primary'} disabled={busy} onClick={()=>run({type:'TASK',id:t.id,action:'inspect'},room?.blocked?'Inspection passed. Room is clean but stays blocked.':'Inspection passed. Room readiness updated.')}>Inspect & release <Check size={16}/></button><button className="text-btn" disabled={busy} onClick={()=>open({type:'hk-reject',id:t.id})}>Return for cleaning…</button></div>
      :<small className="hk-reason">Waiting for a supervisor to inspect.</small>)}
    {t.status==='Done'&&<Pill tone="green"><CheckCheck size={13}/> {kind==='repair'?'Repair done':kind==='stayover'?'Service complete':'Inspection complete'}</Pill>}
    {t.status==='Done'&&kind==='repair'&&room?.blocked&&(permission('inspect')
      ?<button className="btn full" disabled={busy} onClick={()=>run({type:'UNBLOCK',id:t.room,key:'unblock:'+t.room+':'+s.sequence},'Room unblocked and back in inventory')}>Unblock room</button>
      :<small className="hk-reason">Room stays blocked until a supervisor unblocks it.</small>)}
    <div className="hk-links">
      <button className="text-btn" aria-label={`Room details, room ${t.room}`} onClick={()=>open({type:'room',id:t.room})}>Room details</button>
      {kind!=='repair'&&t.status!=='Done'&&canReport&&(openRepair?<small className="muted">Repair already reported</small>:<button className="text-btn" onClick={()=>open({type:'room-repair',id:t.room})}><Wrench size={12}/> Report repair</button>)}
    </div>
    {large&&kind==='stayover'&&t.status==='To do'&&<div className="hk-note"><Info size={13}/> Guest is staying — service only; no inspection needed.</div>}
  </article>;
}
