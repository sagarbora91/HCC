// MB-07 phone page for `housekeeping` (role-aware): M12 Housekeeper "My tasks", M13 Supervisor board / Inspect, M14 Maintenance "My repairs".
// Reuses the stream-D commands (TASK start/check/complete/assign/inspect/reject, REPAIR_REQUEST, STAYOVER_GENERATE, UNBLOCK).
// No financial data is rendered anywhere on this page (AC-M07). Touch targets ≥ 48 px; main buttons 56 px.
import React,{useEffect,useState} from 'react';
import {Brush,BedDouble,Wrench,ArrowRight,Check,CheckCheck,CalendarClock,Lock,TriangleAlert,Sparkles,UserRound,Hammer,ListChecks,Info,X} from 'lucide-react';
import {Task,hkTaskKind,hkKindLabel,hkArrivalToday,hkStayoverRooms,hkStayoverId} from '../../services/mock';
import {useHcc} from '../../app/context';
import {MOBILE_TAB_EVENT,takeTabFocus} from '../../app/mobile/tabs';
import {Pill,shortDate,clockTime} from '../../components/ui';
import {moViewFor,moFocusFrom,moMyTasks,moSegments,moSegmentTasks,moSegmentCounts,moRepairRooms,MO_HK_CHECKLIST,MoSegment} from './logic';

const first=(name?:string)=>name?.split(' · ')[0]??'';
const statusLabel:Record<Task['status'],string>={'To do':'To do','In progress':'In progress','Inspection':'Awaiting inspection','Done':'Done'};
const statusTone:Record<Task['status'],string>={'To do':'neutral','In progress':'blue','Inspection':'orange','Done':'green'};
const hash=()=>{try{return location.hash||'';}catch{return '';}};

/** Keyed by user so switching the demo user (or a walkthrough "Go there") starts from that role's default view. */
export function MoHousekeeping(){const {uid}=useHcc();return <MoHousekeepingView key={uid}/>;}
function MoHousekeepingView(){
  const {s,uid,user,taskList,run,busy,permission}=useHcc();
  const view=moViewFor(user.role);
  const [init]=useState(()=>moFocusFrom(takeTabFocus(),hash()));
  const [seg,setSeg]=useState<MoSegment>(init.segment??'todo');
  const [report,setReport]=useState(!!init.report);
  useEffect(()=>{const on=()=>{const key=takeTabFocus();const f=moFocusFrom(key);
      if(['board','inspect'].includes(key))setSeg(f.segment??'todo');
      if(['mytasks','hkrepair'].includes(key))setReport(!!f.report);};
    window.addEventListener(MOBILE_TAB_EVENT,on);return ()=>window.removeEventListener(MOBILE_TAB_EVENT,on);},[]);

  if(view==='mytasks'){
    const mine=moMyTasks(s,uid),open=mine.filter(t=>t.status!=='Done');
    if(report)return <div className="mo-page">
      <div className="mo-head"><h2>Report a repair</h2><p>Pick one of your rooms. Maintenance gets a repair task; a supervisor decides whether to block the room.</p></div>
      {moRepairRooms(s,uid).map(({room,open:rep})=><div key={room} className="mo-card mo-repair-row">
        <div><strong>Room {room}</strong><span>{rep?`Repair already open: ${rep.note}`:s.rooms.find(r=>r.id===room)?.type}</span></div>
        <RepairButton room={room} disabled={!!rep}/></div>)}
      {!moRepairRooms(s,uid).length&&<p className="mo-empty">You have no open rooms. Repairs can be reported for rooms assigned to you.</p>}
      <button type="button" className="btn full mo-btn" onClick={()=>setReport(false)}><ListChecks size={18}/>Back to my tasks</button>
    </div>;
    return <div className="mo-page">
      <div className="mo-head"><h2>My tasks</h2><p>{open.length?`${open.length} open · your rooms only`:'Nothing open right now'}. Room prices and bills are never shown here.</p></div>
      {mine.map(t=><MoTaskCard key={t.id} t={t} mode="mine"/>)}
      {!mine.length&&<p className="mo-empty">No tasks are assigned to you right now.</p>}
    </div>;
  }
  if(view==='repairs'){
    const mine=moMyTasks(s,uid);
    return <div className="mo-page">
      <div className="mo-head"><h2>My repairs</h2><p>Start a repair when you reach the room; mark it done when finished. A supervisor unblocks the room after checking.</p></div>
      {mine.map(t=><MoTaskCard key={t.id} t={t} mode="mine"/>)}
      {!mine.length&&<p className="mo-empty">No repairs are assigned to you right now.</p>}
    </div>;
  }
  const counts=moSegmentCounts(taskList),list=moSegmentTasks(s,taskList,seg);
  const pendingStay=hkStayoverRooms(s).filter(r=>!s.tasks.some(t=>t.id===hkStayoverId(r,s.date))).length;
  return <div className="mo-page">
    <div className="mo-head"><h2>{seg==='inspect'?'Inspect rooms':'Housekeeping board'}</h2><p>{taskList.filter(t=>t.status!=='Done').length} open tasks · arrival-today rooms first.</p></div>
    <div className="mo-seg" role="group" aria-label="Task status">{moSegments.map(x=><button type="button" key={x.id} aria-pressed={seg===x.id} className={seg===x.id?'on':''} onClick={()=>setSeg(x.id)}><span>{x.label}</span><b>{counts[x.id]}</b></button>)}</div>
    {list.map(t=><MoTaskCard key={t.id} t={t} mode="board"/>)}
    {!list.length&&<p className="mo-empty">{seg==='inspect'?'No rooms are waiting for inspection.':'No rooms here.'}</p>}
    {permission('inspect')&&<button type="button" className="btn full mo-btn" disabled={busy||pendingStay===0} onClick={()=>run({type:'STAYOVER_GENERATE',key:'stayover:'+s.date+':'+s.sequence},'Stayover tasks generated')}>
      <Sparkles size={18}/>{pendingStay?`Generate stayover tasks (${pendingStay})`:'Stayover tasks: all created today'}</button>}
  </div>;
}

function RepairButton({room,disabled}:{room:string;disabled?:boolean}){
  const {open}=useHcc();
  return <button type="button" className="btn mo-btn" disabled={disabled} onClick={()=>open({type:'room-repair',id:room})}><Hammer size={17}/>{disabled?'Reported':'Report repair'}</button>;
}

export function MoTaskCard({t,mode}:{t:Task;mode:'mine'|'board'}){
  const {s,run,busy,open,permission}=useHcc();
  const room=s.rooms.find(r=>r.id===t.room),who=s.users.find(u=>u.id===t.assignee);
  const kind=hkTaskKind(t),arrival=hkArrivalToday(s,t.room);
  const Icon=kind==='repair'?Wrench:kind==='stayover'?BedDouble:Brush;
  const verb=kind==='repair'?'repair':kind==='stayover'?'service':'cleaning';
  const ready=kind==='repair'||t.checked.every(Boolean);
  const openRepair=s.tasks.some(x=>x.room===t.room&&x.kind==='repair'&&x.status!=='Done');
  const inspector=permission('inspect');
  return <article className={'mo-card mo-task '+t.status.replace(/\s/g,'-').toLowerCase()} aria-label={`Room ${t.room} · ${hkKindLabel[kind]} · ${statusLabel[t.status]}`}>
    <div className="mo-task-top"><span className="mo-room"><Icon size={22} aria-hidden/>Room {t.room}</span><Pill tone={statusTone[t.status]}>{statusLabel[t.status]}</Pill></div>
    <div className="mo-tags"><Pill tone={kind==='repair'?'orange':kind==='stayover'?'blue':'neutral'}>{hkKindLabel[kind]}</Pill>
      {arrival&&t.status!=='Done'&&<Pill tone="orange"><CalendarClock size={13}/> Priority · arrival today</Pill>}</div>
    <p className="mo-sub">{room?.type} · Floor {room?.floor}{t.createdAt?` · since ${clockTime(t.createdAt)}`:''}</p>
    {room?.blocked&&<div className="mo-note warn"><Lock size={16}/><span>Room blocked · {room.blockReason||'Out of service'}{room.blockFrom||room.blockTo?` · ${room.blockFrom?shortDate(room.blockFrom):'now'} → ${room.blockTo?shortDate(room.blockTo):'open-ended'}`:''}</span></div>}
    {kind==='repair'&&!room?.blocked&&<div className="mo-note"><Info size={16}/><span>Room is not blocked — it stays sellable while you work.</span></div>}
    {t.note&&<div className="mo-note"><Wrench size={16}/><span>{t.note}</span></div>}
    {t.rejectReason&&t.status!=='Done'&&<div className="mo-note warn"><TriangleAlert size={16}/><span>Returned: {t.rejectReason}</span></div>}
    {mode==='board'&&<div className="mo-assignee"><UserRound size={18}/><span>{first(who?.name)||t.assignee}{who&&!who.enabled?' (disabled)':''}</span>
      {inspector&&t.status!=='Done'&&<button type="button" className="btn mo-btn-sm" disabled={busy} onClick={()=>open({type:'mo-assign',id:t.id})}>Assign</button>}</div>}
    {t.status==='In progress'&&kind!=='repair'&&<div className="mo-checklist" role="group" aria-label={`Checklist room ${t.room}`}>{MO_HK_CHECKLIST.map((l,i)=>
      <label key={l} className={t.checked[i]?'on':''}><input type="checkbox" checked={!!t.checked[i]} disabled={busy} onChange={()=>run({type:'TASK',id:t.id,action:'check',index:i},'Checklist updated')}/><span className="mo-box" aria-hidden>{t.checked[i]&&<Check size={20}/>}</span>{l}</label>)}</div>}
    <div className="mo-actions">
      {t.status==='To do'&&<button type="button" className="btn primary mo-big" disabled={busy} onClick={()=>run({type:'TASK',id:t.id,action:'start'},`Started ${verb} · Room ${t.room}`)}>Start {verb}<ArrowRight size={19}/></button>}
      {t.status==='In progress'&&<button type="button" className="btn primary mo-big" disabled={busy||!ready} onClick={()=>run({type:'TASK',id:t.id,action:'complete'},kind==='repair'?'Repair marked done. A supervisor will unblock the room.':kind==='stayover'?'Stayover service completed':`Cleaning completed · Room ${t.room}`)}><Check size={19}/>{kind==='repair'?'Mark repair done':`Complete ${verb}`}</button>}
      {t.status==='In progress'&&!ready&&<small className="mo-hint">Tick all {MO_HK_CHECKLIST.length} checklist items to complete.</small>}
      {t.status==='Inspection'&&(inspector?<div className="mo-pair">
          <button type="button" className="btn mo-big mo-reject" disabled={busy} onClick={()=>open({type:'hk-reject',id:t.id})}><X size={19}/>Reject</button>
          <button type="button" className="btn primary mo-big" disabled={busy} onClick={()=>run({type:'TASK',id:t.id,action:'inspect'},room?.blocked?'Inspection passed. Room is clean but stays blocked.':`Inspection passed · Room ${t.room} ready`)}><Check size={19}/>Pass</button></div>
        :<small className="mo-hint">Waiting for a supervisor to inspect.</small>)}
      {t.status==='Done'&&<Pill tone="green"><CheckCheck size={14}/> {kind==='repair'?'Repair done':kind==='stayover'?'Service complete':'Inspection complete'}</Pill>}
      {t.status==='Done'&&kind==='repair'&&room?.blocked&&(inspector
        ?<button type="button" className="btn mo-btn" disabled={busy} onClick={()=>run({type:'UNBLOCK',id:t.room,key:'unblock:'+t.room+':'+s.sequence},'Room unblocked and back in inventory')}>Unblock room</button>
        :<small className="mo-hint">The room stays blocked until a supervisor unblocks it.</small>)}
      {kind!=='repair'&&t.status!=='Done'&&(openRepair?<small className="mo-hint"><Wrench size={14}/> Repair already reported for this room</small>:<RepairButton room={t.room}/>)}
    </div>
  </article>;
}
