// Module 15 · Maintenance — shared UI bits: status/priority/SLA pills and the technician work-order card (desktop + phone).
import React from 'react';
import {Wrench,Lock,Play,PauseCircle,CheckCircle2,PackagePlus,TriangleAlert,Clock3,MapPin,Undo2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,clockTime,shortDate} from '../../components/ui';
import {mntSla,mntAge,mntDuration,mntWhere,mntAsset,mntSpare,mntAssigneeLabel,MNT_PRIORITY,mntSlaDue,type MNTWorkOrder,type MNTWorkOrderView,type MNTStatus,type MNTPriority} from '../../services/mock';

const statusTone:Record<MNTStatus,string>={'Open':'neutral','In progress':'blue','On hold':'orange','Done':'mnt-teal','Verified':'green','Cancelled':'neutral'};
export const mntStatusLabel=(st:MNTStatus)=>st==='Done'?'Done · awaiting check':st==='On hold'?'On hold (parts)':st;
export function MntStatus({st}:{st:MNTStatus}){return <Pill tone={statusTone[st]}>{mntStatusLabel(st)}</Pill>;}
export function MntPriority({p}:{p:MNTPriority}){return <Pill tone={p==='P1'?'mnt-red':p==='P2'?'orange':'neutral'}>{p} · {MNT_PRIORITY[p].label}</Pill>;}
export function MntSla({w}:{w:MNTWorkOrderView}){
  const {s}=useHcc();const st=mntSla(w,s.clock);if(st==='n/a')return null;
  return <Pill tone={st==='Breached'?'mnt-red':st==='Due soon'?'orange':'green'}>{st==='Breached'&&<TriangleAlert size={11}/>}SLA {st.toLowerCase()}</Pill>;
}
export const mntWhen=(at?:string)=>at?`${shortDate(at.slice(0,10))} ${clockTime(at)}`:'—';

/** Technician card: one big card per work order with the next action (Start / Resume / Complete). */
export function MntWoCard({w,large}:{w:MNTWorkOrderView;large?:boolean}){
  const {s,run,open,busy}=useHcc();
  const asset=mntAsset(s,w.assetId),room=w.room?s.rooms.find(r=>r.id===w.room):undefined;
  const btn='btn full'+(large?' mnt-big':'');
  return <article className={'mnt-card'+(large?' mnt-card-large':'')+(w.priority==='P1'&&w.status!=='Verified'?' mnt-urgent':'')} aria-label={`${w.id} · ${w.title} · ${w.status}`}>
    <div className="mnt-card-top"><span className="mnt-card-id">{w.id}{w.kind==='Preventive'?' · PM':''}</span><Wrench size={18} aria-hidden/></div>
    <h3>{w.title}</h3>
    <p className="mnt-where"><MapPin size={14} aria-hidden/> {mntWhere(s,w)}{asset?` · ${asset.name}`:''}</p>
    <div className="mnt-tags"><MntPriority p={w.priority}/><MntStatus st={w.status}/><MntSla w={w}/></div>
    <p className="mnt-meta"><Clock3 size={13} aria-hidden/> {w.dueDate?`PM due ${shortDate(w.dueDate)}`:`Reported ${mntWhen(w.createdAt)} · ${mntDuration(mntAge(w,s.clock))} ago`}{w.status!=='Verified'&&w.status!=='Cancelled'&&!w.doneAt?` · SLA by ${mntWhen(mntSlaDue(w))}`:''}</p>
    {room?.blocked&&<div className="mnt-note warn"><Lock size={14}/> Room blocked · {room.blockReason||'Out of service'}</div>}
    {w.status==='On hold'&&w.holdReason&&<div className="mnt-note warn"><PauseCircle size={14}/> {w.holdReason}{w.awaitingSpare?` · awaiting ${mntSpare(s,w.awaitingSpare)?.name}`:''}</div>}
    {w.returnReason&&w.status==='In progress'&&<div className="mnt-note warn"><Undo2 size={14}/> Returned: {w.returnReason}</div>}
    {w.note&&w.status!=='In progress'&&<div className="mnt-note"><CheckCircle2 size={14}/> {w.note}</div>}
    {w.parts.length>0&&<p className="mnt-meta">Parts: {w.parts.map(p=>`${p.qty} × ${mntSpare(s,p.spareId)?.name??p.spareId}`).join(', ')}</p>}
    <p className="mnt-meta">Assigned: {mntAssigneeLabel(s,w)}</p>
    <div className="mnt-card-actions">
      {w.status==='Open'&&<button className={btn+' primary'} disabled={busy} onClick={()=>run({type:'MNT_WO_START',id:w.id},'Work started')}><Play size={17}/> Start work</button>}
      {w.status==='On hold'&&<button className={btn+' primary'} disabled={busy} onClick={()=>run({type:'MNT_WO_START',id:w.id},'Work resumed')}><Play size={17}/> Resume work</button>}
      {w.status==='In progress'&&<button className={btn+' primary'} disabled={busy} onClick={()=>open({type:'mnt-done',id:w.id})}><CheckCircle2 size={17}/> Complete</button>}
      {(w.status==='In progress'||w.status==='On hold')&&<div className="mnt-row2">
        {w.status==='In progress'&&<button className={btn} disabled={busy} onClick={()=>open({type:'mnt-hold',id:w.id})}><PauseCircle size={16}/> Hold for parts</button>}
        <button className={btn} disabled={busy} onClick={()=>open({type:'mnt-part',id:w.id})}><PackagePlus size={16}/> Add part</button></div>}
      {w.status==='Done'&&<p className="mnt-meta">Waiting for a supervisor to check the work.</p>}
      <button className="text-btn mnt-link" onClick={()=>open({type:'mnt-wo',id:w.id})}>Details & history</button>
    </div>
  </article>;
}
