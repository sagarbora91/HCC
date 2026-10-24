// Module 15 · Maintenance dialogs (desktop dialogs; full-screen sheets on phones). All mutations via run().
// mnt-wo · mnt-new · mnt-hold · mnt-part · mnt-done · mnt-asset · mnt-plan · mnt-vendor · mnt-amc · mnt-receive
import React,{useState} from 'react';
import {Lock,Unlock,Check,Undo2,X,Info,Wrench,PauseCircle,PackagePlus,CheckCircle2,Play,TriangleAlert} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Empty,dateLabel} from '../../components/ui';
import {mntSlice,mntAsset,mntSpare,mntVendor,mntWhere,mntAssigneeLabel,mntCanManage,mntCanSeeCost,mntPartsCost,mntTechnicians,mntSlaDue,
  mntDowntimeEnd,mntMinutes,mntDuration,hkAllocationsOverlapping,addDays,money,MNT_PRIORITIES,MNT_PRIORITY,MNT_ASSET_KINDS,type MNTWorkOrder} from '../../services/mock';
import {MntStatus,MntPriority,MntSla,mntWhen} from './shared';

const useWo=(id?:string)=>{const {s}=useHcc();return mntSlice(s).orders.find(w=>w.id===id);};
/** Technician actions allowed: supervisor/manager, or the assigned Maintenance user. */
function useWorker(w?:MNTWorkOrder){const {s,uid,user}=useHcc();return !!w&&(mntCanManage(s,uid)||(user.role==='Maintenance'&&w.assignee===uid));}

export function MntWoModal({id}:{id?:string}){
  const {s,uid,run,open,busy}=useHcc();
  const w=useWo(id);const worker=useWorker(w);
  const [mode,setMode]=useState<''|'return'|'cancel'|'block'>('');const [reason,setReason]=useState('');const [to,setTo]=useState('');
  if(!w)return <Empty title="This work order is unavailable"/>;
  const mgr=mntCanManage(s,uid),cost=mntCanSeeCost(s,uid);
  const asset=mntAsset(s,w.assetId),room=w.room?s.rooms.find(r=>r.id===w.room):undefined;
  const ownWork=w.doneBy===uid||w.assignee===uid;
  const live=['Open','In progress','On hold','Done'].includes(w.status);
  const conflicts=room&&!room.blocked&&mode==='block'?hkAllocationsOverlapping(s,room.id,s.date,to||undefined):[];
  const dtEnd=mntDowntimeEnd(s,w);
  const assignValue=w.vendorId?'v:'+w.vendorId:w.assignee?'u:'+w.assignee:'';
  const reset=()=>{setMode('');setReason('');};
  return <><div className="modal-heading"><div className="eyebrow">{w.kind.toUpperCase()} WORK ORDER · {w.id}</div><h2>{w.title}</h2>
      <div className="inline mnt-tags"><MntPriority p={w.priority}/><MntStatus st={w.status}/><MntSla w={w}/></div></div>
    <div className="mnt-wo-grid">
      <div className="summary-list mnt-summary">
        <div><span>Location</span><strong>{mntWhere(s,w)}</strong></div>
        <div><span>Asset</span><strong>{asset?`${asset.name} (${asset.id})`:'—'}</strong></div>
        <div><span>Assigned</span><strong>{mntAssigneeLabel(s,w)}</strong></div>
        <div><span>Reported</span><strong>{mntWhen(w.createdAt)}</strong></div>
        {w.dueDate&&<div><span>PM due</span><strong>{dateLabel(w.dueDate)}</strong></div>}
        <div><span>SLA ({MNT_PRIORITY[w.priority].sla})</span><strong>by {mntWhen(mntSlaDue(w))}</strong></div>
        {w.doneAt&&<div><span>Completed</span><strong>{mntWhen(w.doneAt)} · {mntDuration(mntMinutes(w.createdAt,w.doneAt))} to repair</strong></div>}
        {w.verifiedAt&&<div><span>Verified</span><strong>{mntWhen(w.verifiedAt)} · {s.users.find(u=>u.id===w.verifiedBy)?.name.split(' · ')[0]}</strong></div>}
        {w.repairTaskId&&<div><span>From repair request</span><strong>{w.repairTaskId}</strong></div>}
        {room&&<div><span>Room status</span><strong>{room.blocked?<><Lock size={12}/> Blocked · {room.blockReason}</>:'In inventory (not blocked)'}</strong></div>}
        {w.downtimeFrom&&<div><span>Room downtime</span><strong>{mntWhen(w.downtimeFrom)} → {w.downtimeTo?mntWhen(w.downtimeTo):'running'} · {mntDuration(mntMinutes(w.downtimeFrom,dtEnd!))}</strong></div>}
        {w.holdReason&&w.status==='On hold'&&<div><span>On hold</span><strong>{w.holdReason}{w.awaitingSpare?` · awaiting ${mntSpare(s,w.awaitingSpare)?.name}`:''}</strong></div>}
        {w.note&&<div><span>Work done</span><strong>{w.note}</strong></div>}
        {w.returnReason&&<div><span>Last returned</span><strong>{w.returnReason}</strong></div>}
        {w.cancelReason&&<div><span>Cancelled</span><strong>{w.cancelReason}</strong></div>}
      </div>
      <div>
        <h3 className="mnt-h3">Parts used</h3>
        {w.parts.length?<div className="table-wrap mnt-mini"><table><thead><tr><th>Part</th><th>Qty</th>{cost&&<th>Cost</th>}</tr></thead><tbody>
          {w.parts.map((p,i)=><tr key={i}><td>{mntSpare(s,p.spareId)?.name??p.spareId}</td><td>{p.qty} {mntSpare(s,p.spareId)?.unit}</td>{cost&&<td className="number">{money(p.qty*p.unitCost)}</td>}</tr>)}
          {cost&&<tr><td><strong>Total</strong></td><td/><td className="number"><strong>{money(mntPartsCost(w))}</strong></td></tr>}</tbody></table></div>:<p className="muted">No parts booked.</p>}
        <h3 className="mnt-h3">History</h3>
        <ol className="mnt-history">{[...w.history].reverse().map((h,i)=><li key={i}><strong>{h.action}</strong> · {h.by} · {mntWhen(h.at)}{h.note&&<small>{h.note}</small>}</li>)}</ol>
      </div>
    </div>
    {mgr&&live&&<div className="form mnt-assign"><label>Assign to<select value={assignValue} disabled={busy} onChange={e=>{const v=e.target.value;if(!v)return;run(v.startsWith('v:')?{type:'MNT_WO_ASSIGN',id:w.id,vendorId:v.slice(2)}:{type:'MNT_WO_ASSIGN',id:w.id,assignee:v.slice(2)},'Work order assigned');}}>
      <option value="">Choose technician or vendor…</option>
      <optgroup label="Maintenance staff">{mntTechnicians(s).map(u=><option key={u.id} value={'u:'+u.id}>{u.name.split(' · ')[0]}</option>)}</optgroup>
      <optgroup label="External vendors">{mntSlice(s).vendors.map(v=><option key={v.id} value={'v:'+v.id}>{v.name} · {v.trade}</option>)}</optgroup></select></label></div>}
    {mode==='return'||mode==='cancel'?<form className="form mnt-inline-form" onSubmit={e=>{e.preventDefault();run(mode==='return'?{type:'MNT_WO_VERIFY',id:w.id,approve:false,reason:reason.trim()}:{type:'MNT_WO_CANCEL',id:w.id,reason:reason.trim()},mode==='return'?'Returned to the technician':'Work order cancelled',{after:reset});}}>
        <label>{mode==='return'?'What still needs fixing? (required)':'Cancellation reason (required)'}<textarea rows={2} maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} autoFocus/></label>
        <div className="modal-actions"><button type="button" className="btn" onClick={reset}>Back</button><button className="btn danger" disabled={busy||reason.trim().length<3}>{mode==='return'?'Return for rework':'Cancel work order'}</button></div></form>
    :mode==='block'&&room?<form className="form mnt-inline-form" onSubmit={e=>{e.preventDefault();run({type:'MNT_WO_BLOCK',id:w.id,to:to||undefined},`Room ${room.id} blocked for maintenance`,{after:reset});}}>
        <p className="muted">Blocks room {room.id} from today ({dateLabel(s.date)}) so it cannot be sold. Same rules as a supervisor room block.</p>
        <label>Available again on (optional)<input type="date" value={to} min={addDays(s.date,1)} onChange={e=>setTo(e.target.value)}/></label>
        {conflicts.length>0&&<div className="info-note mnt-warn" role="alert"><TriangleAlert size={16}/><span>Resolve active and future allocations first: {conflicts.map(b=>`${b.id} (${b.status})`).join(', ')}. Move the stay or choose an end date before the arrival.</span></div>}
        <div className="modal-actions"><button type="button" className="btn" onClick={reset}>Back</button><button className="btn primary" disabled={busy||conflicts.length>0}><Lock size={15}/> Block room</button></div></form>
    :<div className="modal-actions">
      {worker&&w.status==='Open'&&<button className="btn primary" disabled={busy} onClick={()=>run({type:'MNT_WO_START',id:w.id},'Work started')}><Play size={15}/> Start</button>}
      {worker&&w.status==='On hold'&&<button className="btn primary" disabled={busy} onClick={()=>run({type:'MNT_WO_START',id:w.id},'Work resumed')}><Play size={15}/> Resume</button>}
      {worker&&w.status==='In progress'&&<><button className="btn" onClick={()=>open({type:'mnt-hold',id:w.id})}><PauseCircle size={15}/> Hold</button><button className="btn primary" onClick={()=>open({type:'mnt-done',id:w.id})}><CheckCircle2 size={15}/> Complete</button></>}
      {worker&&(w.status==='In progress'||w.status==='On hold')&&<button className="btn" onClick={()=>open({type:'mnt-part',id:w.id})}><PackagePlus size={15}/> Add part</button>}
      {mgr&&room&&live&&!room.blocked&&<button className="btn" onClick={()=>setMode('block')}><Lock size={15}/> Block room</button>}
      {mgr&&room&&live&&room.blocked&&!w.downtimeFrom&&<button className="btn" disabled={busy} onClick={()=>run({type:'MNT_WO_BLOCK',id:w.id},'Existing block linked; downtime tracked')}><Lock size={15}/> Link room block</button>}
      {mgr&&(w.status==='Open'||w.status==='On hold')&&<button className="btn danger" onClick={()=>setMode('cancel')}><X size={15}/> Cancel WO</button>}
      {mgr&&w.status==='Done'&&(ownWork?<span className="footnote">You worked on this order — another supervisor must verify it.</span>:<>
        <button className="btn" onClick={()=>setMode('return')}><Undo2 size={15}/> Return</button>
        {room?.blocked&&<button className="btn primary" disabled={busy} onClick={()=>run({type:'MNT_WO_VERIFY',id:w.id,approve:true,unblock:true},`Verified; room ${room.id} back in inventory`)}><Unlock size={15}/> Verify & unblock</button>}
        <button className={'btn'+(room?.blocked?'':' primary')} disabled={busy} onClick={()=>run({type:'MNT_WO_VERIFY',id:w.id,approve:true},'Work verified')}><Check size={15}/> Verify{room?.blocked?' (keep block)':''}</button></>)}
      {!mgr&&w.status==='Done'&&<span className="footnote">Waiting for a supervisor to verify.</span>}
      {(w.status==='Verified'||w.status==='Cancelled')&&<span className="footnote">This work order is closed.</span>}
    </div>}
  </>;
}

export function MntNewModal({room:presetRoom}:{room?:string}){
  const {s,uid,user,run,modal,busy}=useHcc();
  const x=mntSlice(s),mgr=mntCanManage(s,uid);
  const [where,setWhere]=useState<'room'|'area'>(presetRoom?'room':'room');
  const [room,setRoom]=useState(presetRoom||''),[area,setArea]=useState(''),[assetId,setAssetId]=useState(''),[title,setTitle]=useState(''),[priority,setPriority]=useState('P3'),[notes,setNotes]=useState(''),[assignee,setAssignee]=useState('');
  const assets=x.assets.filter(a=>a.status!=='Retired'&&(where==='room'?(!room||a.room===room)&&a.room:a.area));
  const ok=title.trim().length>=3&&(where==='room'?!!room:!!(area.trim()||assetId));
  return <><div className="modal-heading"><div className="eyebrow">MAINTENANCE</div><h2>{user.role==='Maintenance'?'Report a fault':'New work order'}</h2><p>Creates a corrective work order. {user.role==='Maintenance'?'It is assigned to you.':'Assign now or later.'} Block the room from the work order if it cannot be sold.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MNT_WO_CREATE',title:title.trim(),priority,room:where==='room'?room:undefined,area:where==='area'?area.trim():undefined,assetId:assetId||undefined,notes:notes.trim(),assignee:mgr&&assignee?assignee:undefined,key:modal!.key},'Work order created',{close:true});}}>
      <label>What is wrong? (required)<input value={title} maxLength={100} onChange={e=>setTitle(e.target.value)} placeholder="e.g. AC not cooling" enterKeyHint="next"/></label>
      <div className="mnt-seg" role="radiogroup" aria-label="Location type">{(['room','area'] as const).map(v=><button type="button" key={v} role="radio" aria-checked={where===v} className={where===v?'on':''} onClick={()=>{setWhere(v);setAssetId('');}}>{v==='room'?'Guest room':'Common area'}</button>)}</div>
      <div className="form-row">
        {where==='room'?<label>Room<select value={room} onChange={e=>{setRoom(e.target.value);setAssetId('');}}><option value="">Choose room…</option>{s.rooms.map(r=><option key={r.id} value={r.id}>{r.id} · {r.type}{r.blocked?' (blocked)':''}</option>)}</select></label>
          :<label>Area<input value={area} maxLength={60} onChange={e=>setArea(e.target.value)} placeholder="e.g. Lobby, Kitchen" list="mnt-areas"/></label>}
        <label>Asset (optional)<select value={assetId} onChange={e=>setAssetId(e.target.value)}><option value="">—</option>{assets.map(a=><option key={a.id} value={a.id}>{a.name}</option>)}</select></label>
      </div>
      <datalist id="mnt-areas">{[...new Set(x.assets.map(a=>a.area).filter(Boolean))].map(a=><option key={a} value={a}/>)}</datalist>
      <div className="form-row">
        <label>Priority<select value={priority} onChange={e=>setPriority(e.target.value)}>{MNT_PRIORITIES.map(p=><option key={p} value={p}>{p} · {MNT_PRIORITY[p].label} (fix within {MNT_PRIORITY[p].sla})</option>)}</select></label>
        {mgr&&user.role!=='Maintenance'?<label>Assign to (optional)<select value={assignee} onChange={e=>setAssignee(e.target.value)}><option value="">Later</option>{mntTechnicians(s).map(u=><option key={u.id} value={u.id}>{u.name.split(' · ')[0]}</option>)}</select></label>:<span/>}
      </div>
      <label>Notes (optional)<textarea rows={2} maxLength={200} value={notes} onChange={e=>setNotes(e.target.value)} placeholder="What the guest or staff noticed"/></label>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}><Wrench size={15}/> {user.role==='Maintenance'?'Report fault':'Create work order'}</button></div>
    </form></>;
}

export function MntHoldModal({id}:{id?:string}){
  const {s,run,modal,busy}=useHcc();const w=useWo(id);
  const [reason,setReason]=useState('Waiting for parts'),[spareId,setSpareId]=useState('');
  if(!w)return <Empty title="This work order is unavailable"/>;
  return <><div className="modal-heading"><div className="eyebrow">{w.id}</div><h2>Put on hold</h2><p>{w.title} · {mntWhere(s,w)}. The SLA clock keeps running.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'MNT_WO_HOLD',id:w.id,reason:reason.trim(),spareId:spareId||undefined,key:modal!.key},'Work order on hold',{close:true});}}>
      <label>Reason (required)<input value={reason} maxLength={120} onChange={e=>setReason(e.target.value)} enterKeyHint="done"/></label>
      <label>Waiting for spare (optional)<select value={spareId} onChange={e=>setSpareId(e.target.value)}><option value="">—</option>{mntSlice(s).spares.map(p=><option key={p.id} value={p.id}>{p.name} (stock {p.stock})</option>)}</select></label>
      <div className="modal-actions"><button className="btn primary" disabled={busy||reason.trim().length<3}><PauseCircle size={15}/> Put on hold</button></div>
    </form></>;
}

export function MntPartModal({id}:{id?:string}){
  const {s,run,modal,busy,uid}=useHcc();const w=useWo(id);
  const spares=mntSlice(s).spares;const [spareId,setSpareId]=useState(w?.awaitingSpare&&(mntSpare(s,w.awaitingSpare)?.stock??0)>0?w.awaitingSpare:'');const [qty,setQty]=useState('1');
  if(!w)return <Empty title="This work order is unavailable"/>;
  const sp=mntSpare(s,spareId),n=Number(qty),ok=!!sp&&Number.isInteger(n)&&n>=1&&n<=sp.stock;
  return <><div className="modal-heading"><div className="eyebrow">{w.id}</div><h2>Add part used</h2><p>Stock reduces immediately.{mntCanSeeCost(s,uid)?' Unit cost is recorded on the work order.':''}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MNT_WO_PART',id:w.id,spareId,qty:n,key:modal!.key},`${n} × ${sp!.name} booked`,{close:true});}}>
      <label>Spare<select value={spareId} onChange={e=>setSpareId(e.target.value)}><option value="">Choose part…</option>{spares.map(p=><option key={p.id} value={p.id} disabled={p.stock<1}>{p.name} — {p.stock} {p.unit} in stock</option>)}</select></label>
      <label>Quantity{sp?` (max ${sp.stock} ${sp.unit})`:''}<input inputMode="numeric" enterKeyHint="done" value={qty} onChange={e=>setQty(e.target.value.replace(/\D/g,''))}/></label>
      {sp&&n>sp.stock&&<small className="mnt-error" role="alert">Only {sp.stock} {sp.unit} in stock.</small>}
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}><PackagePlus size={15}/> Book part</button></div>
    </form></>;
}

export function MntDoneModal({id}:{id?:string}){
  const {s,run,modal,busy}=useHcc();const w=useWo(id);const [note,setNote]=useState('');
  if(!w)return <Empty title="This work order is unavailable"/>;
  const room=w.room?s.rooms.find(r=>r.id===w.room):undefined;
  return <><div className="modal-heading"><div className="eyebrow">{w.id}</div><h2>Complete work</h2><p>{w.title} · {mntWhere(s,w)}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'MNT_WO_COMPLETE',id:w.id,note:note.trim(),key:modal!.key},'Work completed — waiting for verification',{close:true});}}>
      <label>What did you do? (required)<textarea rows={3} maxLength={200} value={note} onChange={e=>setNote(e.target.value)} placeholder="e.g. Thermostat replaced, tested 15 min" enterKeyHint="done"/></label>
      {w.parts.length>0&&<p className="muted">Parts booked: {w.parts.map(p=>`${p.qty} × ${mntSpare(s,p.spareId)?.name}`).join(', ')}</p>}
      {room?.blocked&&<div className="info-note"><Info size={16}/>Room {room.id} stays blocked until a supervisor verifies the work and unblocks it.</div>}
      <div className="modal-actions"><button className="btn primary" disabled={busy||note.trim().length<3}><CheckCircle2 size={15}/> Mark complete</button></div>
    </form></>;
}

export function MntAssetModal({id}:{id?:string}){
  const {s,uid,run,modal,busy}=useHcc();const x=mntSlice(s);const a=mntAsset(s,id);const mgr=mntCanManage(s,uid);
  const [f,setF]=useState({name:a?.name??'',kind:a?.kind??'AC',room:a?.room??'',area:a?.area??'',make:a?.make??'',model:a?.model??'',installedOn:a?.installedOn??'',vendorId:a?.vendorId??'',status:a?.status??'Working'});
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF({...f,[k]:e.target.value});
  const history=a?x.orders.filter(w=>w.assetId===a.id):[];
  if(id&&!a)return <Empty title="This asset is unavailable"/>;
  if(!mgr&&a)return <><div className="modal-heading"><div className="eyebrow">ASSET · {a.id}</div><h2>{a.name}</h2></div>
    <div className="summary-list mnt-summary"><div><span>Type</span><strong>{a.kind}</strong></div><div><span>Location</span><strong>{mntWhere(s,a)}</strong></div><div><span>Make / model</span><strong>{a.make} {a.model}</strong></div><div><span>Installed</span><strong>{a.installedOn?dateLabel(a.installedOn):'—'}</strong></div><div><span>Service vendor</span><strong>{mntVendor(s,a.vendorId)?.name??'—'}</strong></div><div><span>Status</span><strong>{a.status}</strong></div><div><span>Work orders</span><strong>{history.length}</strong></div></div></>;
  const ok=f.name.trim().length>=2&&(f.room||f.area.trim());
  return <><div className="modal-heading"><div className="eyebrow">ASSET REGISTER{a?' · '+a.id:''}</div><h2>{a?'Edit asset':'Add asset'}</h2>{a&&<p>{history.length} work order(s) on record.</p>}</div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MNT_ASSET_SAVE',id:a?.id,...f,room:f.room||undefined,area:f.room?undefined:f.area,vendorId:f.vendorId||undefined,key:modal!.key},a?'Asset updated':'Asset added',{close:true});}}>
      <label>Name<input value={f.name} maxLength={80} onChange={set('name')} placeholder="e.g. Split AC 1.5 T · 107"/></label>
      <div className="form-row"><label>Type<select value={f.kind} onChange={set('kind')}>{MNT_ASSET_KINDS.map(k=><option key={k}>{k}</option>)}</select></label>
        <label>Status<select value={f.status} onChange={set('status')}>{['Working','Faulty','Retired'].map(k=><option key={k}>{k}</option>)}</select></label></div>
      <div className="form-row"><label>Room<select value={f.room} onChange={set('room')}><option value="">Common area</option>{s.rooms.map(r=><option key={r.id} value={r.id}>{r.id}</option>)}</select></label>
        <label>Area{f.room?' (not used)':''}<input value={f.area} disabled={!!f.room} maxLength={60} onChange={set('area')} placeholder="e.g. Basement"/></label></div>
      <div className="form-row"><label>Make<input value={f.make} maxLength={40} onChange={set('make')}/></label><label>Model<input value={f.model} maxLength={40} onChange={set('model')}/></label></div>
      <div className="form-row"><label>Installed on<input type="date" value={f.installedOn} onChange={set('installedOn')}/></label>
        <label>Service vendor<select value={f.vendorId} onChange={set('vendorId')}><option value="">—</option>{x.vendors.map(v=><option key={v.id} value={v.id}>{v.name}</option>)}</select></label></div>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}>{a?'Save asset':'Add asset'}</button></div>
    </form></>;
}

export function MntPlanModal({id}:{id?:string}){
  const {s,run,modal,busy}=useHcc();const x=mntSlice(s);const p=x.plans.find(p=>p.id===id);
  const [f,setF]=useState({assetId:p?.assetId??'',title:p?.title??'',everyDays:String(p?.everyDays??30),nextDue:p?.nextDue??addDays(s.date,7),active:p?.active??true});
  const ok=!!f.assetId&&f.title.trim().length>=3&&Number(f.everyDays)>=1&&Number(f.everyDays)<=365&&!!f.nextDue;
  return <><div className="modal-heading"><div className="eyebrow">PREVENTIVE MAINTENANCE{p?' · '+p.id:''}</div><h2>{p?'Edit PM plan':'Add PM plan'}</h2><p>Work orders are generated for occurrences due within 3 days of the business date.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MNT_PLAN_SAVE',id:p?.id,...f,everyDays:Number(f.everyDays),key:modal!.key},p?'Plan updated':'Plan added',{close:true});}}>
      <label>Asset<select value={f.assetId} onChange={e=>setF({...f,assetId:e.target.value})}><option value="">Choose asset…</option>{x.assets.filter(a=>a.status!=='Retired').map(a=><option key={a.id} value={a.id}>{a.name} · {mntWhere(s,a)}</option>)}</select></label>
      <label>Task<input value={f.title} maxLength={100} onChange={e=>setF({...f,title:e.target.value})} placeholder="e.g. AC filter cleaning"/></label>
      <div className="form-row"><label>Repeat every (days)<input inputMode="numeric" value={f.everyDays} onChange={e=>setF({...f,everyDays:e.target.value.replace(/\D/g,'')})}/></label>
        <label>Next due<input type="date" value={f.nextDue} onChange={e=>setF({...f,nextDue:e.target.value})}/></label></div>
      <label className="checkbox-line"><input type="checkbox" checked={f.active} onChange={e=>setF({...f,active:e.target.checked})}/> Active (uncheck to pause)</label>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}>{p?'Save plan':'Add plan'}</button></div>
    </form></>;
}

export function MntVendorModal({id}:{id?:string}){
  const {s,run,modal,busy}=useHcc();const v=mntVendor(s,id);
  const [f,setF]=useState({name:v?.name??'',trade:v?.trade??'',phone:v?.phone??''});
  const ok=f.name.trim().length>=3&&/^[+\d][\d\s-]{6,}$/.test(f.phone.trim());
  return <><div className="modal-heading"><div className="eyebrow">VENDORS</div><h2>{v?'Edit vendor':'Add vendor'}</h2><p>Fictional contact details only. No calls or messages are sent from the prototype.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MNT_VENDOR_SAVE',id:v?.id,...f,key:modal!.key},v?'Vendor updated':'Vendor added',{close:true});}}>
      <label>Name<input value={f.name} maxLength={80} onChange={e=>setF({...f,name:e.target.value})}/></label>
      <div className="form-row"><label>Trade<input value={f.trade} maxLength={50} onChange={e=>setF({...f,trade:e.target.value})} placeholder="e.g. Plumbing"/></label>
        <label>Phone<input type="tel" inputMode="tel" value={f.phone} maxLength={20} onChange={e=>setF({...f,phone:e.target.value})} placeholder="+91 90000 00000"/></label></div>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}>{v?'Save vendor':'Add vendor'}</button></div>
    </form></>;
}

export function MntAmcModal({id}:{id?:string}){
  const {s,run,modal,busy}=useHcc();const x=mntSlice(s);const a=x.amcs.find(a=>a.id===id);
  const [f,setF]=useState({vendorId:a?.vendorId??'',scope:a?.scope??'',from:a?.from??s.date,to:a?.to??addDays(s.date,365),visits:String(a?.visits??4),value:a?String(a.value/100):''});
  const value=Math.round(Number(f.value)*100);
  const ok=!!f.vendorId&&f.scope.trim().length>=3&&f.to>f.from&&value>0;
  return <><div className="modal-heading"><div className="eyebrow">ANNUAL MAINTENANCE CONTRACT</div><h2>{a?'Edit AMC':'Add AMC'}</h2><p>Contract record only — no payment is posted. Pay the vendor through Expenses.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MNT_AMC_SAVE',id:a?.id,vendorId:f.vendorId,scope:f.scope.trim(),from:f.from,to:f.to,visits:Number(f.visits),value,key:modal!.key},a?'AMC updated':'AMC added',{close:true});}}>
      <label>Vendor<select value={f.vendorId} onChange={e=>setF({...f,vendorId:e.target.value})}><option value="">Choose vendor…</option>{x.vendors.map(v=><option key={v.id} value={v.id}>{v.name}</option>)}</select></label>
      <label>Covers<input value={f.scope} maxLength={120} onChange={e=>setF({...f,scope:e.target.value})} placeholder="e.g. All split ACs — quarterly service"/></label>
      <div className="form-row"><label>From<input type="date" value={f.from} onChange={e=>setF({...f,from:e.target.value})}/></label><label>To<input type="date" value={f.to} onChange={e=>setF({...f,to:e.target.value})}/></label></div>
      <div className="form-row"><label>Visits per year<input inputMode="numeric" value={f.visits} onChange={e=>setF({...f,visits:e.target.value.replace(/\D/g,'')})}/></label>
        <label>Contract value (₹)<input inputMode="decimal" value={f.value} onChange={e=>setF({...f,value:e.target.value.replace(/[^\d.]/g,'')})}/></label></div>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}>{a?'Save AMC':'Add AMC'}</button></div>
    </form></>;
}

export function MntReceiveModal({id}:{id?:string}){
  const {s,run,modal,busy}=useHcc();const sp=mntSpare(s,id);const [qty,setQty]=useState('');
  if(!sp)return <Empty title="This spare is unavailable"/>;
  const n=Number(qty),ok=Number.isInteger(n)&&n>=1&&n<=999;
  return <><div className="modal-heading"><div className="eyebrow">SPARES</div><h2>Receive {sp.name}</h2><p>Current stock {sp.stock} {sp.unit} · reorder at {sp.reorderAt}.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MNT_SPARE_RECEIVE',spareId:sp.id,qty:n,key:modal!.key},`${n} ${sp.unit} received`,{close:true});}}>
      <label>Quantity received<input inputMode="numeric" enterKeyHint="done" value={qty} onChange={e=>setQty(e.target.value.replace(/\D/g,''))} autoFocus/></label>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!ok}>Receive</button></div>
    </form></>;
}
