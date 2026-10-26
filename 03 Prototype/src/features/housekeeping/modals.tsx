// Stream D dialogs. open({type:'hk-reject', id:taskId}) · open({type:'room-block', id:roomId}) · open({type:'room-repair', id:roomId}).
import React,{useState} from 'react';
import {bookingRef} from '../../domain/bookingRef';
import {Info,Lock,Wrench,TriangleAlert} from 'lucide-react';
import {hkAllocationsOverlapping,HK_REPAIR_ASSIGNEE,addDays} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,shortDate} from '../../components/ui';

const quickRejects=['Bathroom not clean','Linen not changed','Amenities missing','Dust on surfaces'];

export function RejectModal({id}:{id?:string}){
  const {s,run,modal,closeModal,busy}=useHcc();
  const t=s.tasks.find(t=>t.id===id);const [reason,setReason]=useState('');const [touched,setTouched]=useState(false);
  if(!t)return <Empty title="This task is unavailable"/>;
  const ok=reason.trim().length>=3;
  return <><div className="modal-heading"><div className="eyebrow">INSPECTION</div><h2>Return room {t.room} for cleaning</h2><p>The checklist is cleared and the housekeeper sees your reason on the task.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();setTouched(true);if(ok)run({type:'TASK',id:t.id,action:'reject',reason:reason.trim(),key:modal!.key},'Returned for cleaning with reason',{close:true});}}>
      <label>Reason (required)<textarea name="reason" rows={3} maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} onBlur={()=>setTouched(true)} aria-invalid={touched&&!ok} placeholder="What needs to be redone?"/></label>
      {touched&&!ok&&<small className="hk-reason error" role="alert">Enter a reason of at least 3 characters.</small>}
      <div className="hk-chips" aria-label="Common reasons">{quickRejects.map(q=><button type="button" key={q} className="small-btn" onClick={()=>{setReason(q);setTouched(true);}}>{q}</button>)}</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn danger" disabled={busy}>Return for cleaning</button></div>
    </form></>;
}

export function BlockModal({id}:{id?:string}){
  const {s,run,modal,open,busy,permission}=useHcc();
  const r=s.rooms.find(r=>r.id===id);
  const [reason,setReason]=useState(r?.blockReason||''),[from,setFrom]=useState(r?.blockFrom&&r.blockFrom>=s.date?r.blockFrom:s.date),[to,setTo]=useState(r?.blockTo||'');
  const [touched,setTouched]=useState(false);
  if(!r)return <Empty title="This room is unavailable"/>;
  if(!permission('inspect'))return <Empty title="Supervisor access required" body="Only a supervisor or manager can apply a room block. Use “Report repair” instead."/>;
  const conflicts=from?hkAllocationsOverlapping(s,r.id,from,to||undefined):[];
  const errors=[reason.trim().length<3&&'Enter a reason of at least 3 characters.',!from&&'Choose a start date.',from&&from<s.date&&'The block cannot start before the business date.',to&&from&&to<=from&&'The end date must be after the start date.'].filter(Boolean) as string[];
  const canSave=!errors.length&&!conflicts.length;
  return <><div className="modal-heading"><div className="eyebrow">ROOM {r.id} · {r.type.toUpperCase()}</div><h2>{r.blocked?'Change block':'Block for repair'}</h2><p>The room is out of service for nights from the start date up to (not including) the end date.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();setTouched(true);if(canSave)run({type:'BLOCK',id:r.id,reason:reason.trim(),from,to:to||undefined,key:modal!.key},'Room blocked',{after:()=>open({type:'room',id:r.id})});}}>
      <label>Reason (required)<input value={reason} maxLength={120} onChange={e=>setReason(e.target.value)} placeholder="e.g. AC compressor replacement" list="hk-block-reasons"/></label>
      <datalist id="hk-block-reasons">{['Plumbing repair','Electrical repair','AC servicing','Repainting','Deep cleaning','Pest control'].map(x=><option key={x} value={x}/>)}</datalist>
      <div className="form-row"><label>From<input type="date" value={from} min={s.date} onChange={e=>setFrom(e.target.value)} required/></label><label>To (optional)<input type="date" value={to} min={from?addDays(from,1):s.date} onChange={e=>setTo(e.target.value)}/></label></div>
      <small className="muted">{to?`Blocks ${shortDate(from)} → ${shortDate(to)} (${to>from?'available again on '+shortDate(to):'invalid'}).`:'No end date: the block stays until a supervisor unblocks the room.'}</small>
      {touched&&errors.map(x=><small key={x} className="hk-reason error" role="alert">{x}</small>)}
      {conflicts.length>0&&<div className="info-note hk-conflict" role="alert"><TriangleAlert size={16}/><span>Resolve active and future allocations before blocking this room: {conflicts.map(b=>`${bookingRef(b)} (${b.status}, ${shortDate(b.arrival)} → ${shortDate(b.departure)})`).join(', ')}. Move or shorten these stays, or choose other dates.</span></div>}
      <div className="info-note"><Info size={16}/>Inspection never clears a block. Completing a repair does not unblock either — unblock the room from its details when it is ready to sell.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>open({type:'room',id:r.id})}>Back</button><button className="btn primary" disabled={busy||!!conflicts.length} title={conflicts.length?'Dates overlap existing allocations':''}><Lock size={15}/> {r.blocked?'Save block':'Block room'}</button></div>
    </form></>;
}

export function RepairModal({id}:{id?:string}){
  const {s,run,modal,closeModal,open,busy}=useHcc();
  const r=s.rooms.find(r=>r.id===id);const [reason,setReason]=useState(''),[touched,setTouched]=useState(false);
  if(!r)return <Empty title="This room is unavailable"/>;
  const maint=s.users.find(u=>u.id===HK_REPAIR_ASSIGNEE);
  const existing=s.tasks.find(t=>t.room===r.id&&t.kind==='repair'&&t.status!=='Done');
  const ok=reason.trim().length>=3;
  return <><div className="modal-heading"><div className="eyebrow">ROOM {r.id}</div><h2>Report a repair</h2><p>Creates a repair task for {maint?.name.split(' · ')[0]??'Maintenance'} (Maintenance). A supervisor decides whether to block the room.</p></div>
    {existing?<div className="info-note" role="status"><Wrench size={16}/>A repair is already open for this room: “{existing.note}”.</div>:
    <form className="form" onSubmit={e=>{e.preventDefault();setTouched(true);if(ok)run({type:'REPAIR_REQUEST',room:r.id,reason:reason.trim(),key:modal!.key},'Repair reported to Maintenance',{close:true});}}>
      <label>What needs repair? (required)<textarea rows={3} maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} onBlur={()=>setTouched(true)} aria-invalid={touched&&!ok} placeholder="e.g. Shower mixer leaking"/></label>
      {touched&&!ok&&<small className="hk-reason error" role="alert">Describe the repair in at least 3 characters.</small>}
      {!maint?.enabled&&<small className="hk-reason error">The Maintenance user is disabled; the request will be refused.</small>}
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>open({type:'room',id:r.id})}>Room details</button><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}><Wrench size={15}/> Report repair</button></div>
    </form>}</>;
}
