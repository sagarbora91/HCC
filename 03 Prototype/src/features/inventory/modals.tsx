// Module 16 · dialogs (desktop dialogs; full-screen sheets on phones). Modal IDs start with 'inv-'.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {PackagePlus,ArrowRightLeft,Shirt,Truck,AlertTriangle,ClipboardList,ClipboardCheck,Settings2,Printer,Info} from 'lucide-react';
import {money,INV_LOCS,INV_FLOORS,INV_COUNTABLE,INV_LOSS_REASONS,INV_SUPPLIERS,INV_KINDS,INVLoc,INVItem,invSlice,invItem,invLocLabel,invMissing,invCountVariance,invCountValue,
  invRoomTypes,invClean,invTotal,invValue,invConsumption,invFmt,invOccupied,addDays} from '../../services/mock';
import type {ModalProps} from '../../app/registry';
import {Empty,Pill,dateLabel,shortDate} from '../../components/ui';
import {InvQty,useInv,invUserName} from './shared';

const Why=({text}:{text:string})=>text?<p className="inv-why" aria-live="polite">{text}</p>:null;
function Heading({icon:Icon,title,sub}:{icon:React.ComponentType<{size?:number}>;title:string;sub:string}){return <div className="modal-heading"><Icon size={25}/><h2>{title}</h2><p>{sub}</p></div>;}

// ── Stock in (simulated GRN) ─────────────────────────────────────────
export function InvGrnModal({id,room}:ModalProps){
  const {s,run,busy,money:showMoney}=useInv();const items=invSlice(s).items;
  const first=id?invItem(s,id):undefined;
  const [supplier,setSupplier]=useState(first?.supplier??INV_SUPPLIERS[0]);const [ref,setRef]=useState('');
  const [rows,setRows]=useState<{item:string;qty:number;cost:string}[]>([{item:first?.id??items[0]?.id??'',qty:Number(room)||first?.reorderQty||1,cost:''}]);
  const set=(i:number,p:Partial<{item:string;qty:number;cost:string}>)=>setRows(rows.map((r,j)=>j===i?{...r,...p}:r));
  const dup=new Set(rows.map(r=>r.item)).size!==rows.length;
  const why=supplier.trim().length<2?'Enter the supplier.':dup?'Each item may appear only once.':rows.some(r=>r.qty<1)?'Quantities must be at least 1.':showMoney&&rows.some(r=>r.cost!==''&&!(Number(r.cost)>0))?'Unit cost must be greater than zero.':'';
  function submit(e:React.FormEvent){e.preventDefault();if(why)return;
    run({type:'INV_GRN',supplier:supplier.trim(),ref:ref.trim(),lines:rows.map(r=>({item:r.item,qty:r.qty,...(showMoney&&r.cost!==''?{cost:Math.round(Number(r.cost)*100)}:{})}))},'Stock received to Main store',{close:true});}
  return <><Heading icon={PackagePlus} title="Receive stock (GRN)" sub="Simulated goods receipt into the Main store. The supplier bill still goes through Expenses."/>
    <form className="form" onSubmit={submit} aria-label="Goods receipt">
      <div className="form-row"><label>Supplier<input list="inv-suppliers" value={supplier} onChange={e=>setSupplier(e.target.value)} maxLength={60}/></label>
        <datalist id="inv-suppliers">{INV_SUPPLIERS.map(x=><option key={x} value={x}/>)}</datalist>
        <label>Delivery note / bill ref (optional)<input value={ref} onChange={e=>setRef(e.target.value)} maxLength={40} enterKeyHint="next" placeholder="e.g. MS/INV/2291"/></label></div>
      {rows.map((r,i)=>{const it=items.find(x=>x.id===r.item);return <div className="inv-line" key={i}>
        <label>Item<select value={r.item} onChange={e=>set(i,{item:e.target.value})}>{INV_KINDS.map(k=><optgroup key={k.id} label={k.label}>{items.filter(x=>x.kind===k.id).map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</optgroup>)}</select></label>
        <InvQty label={`Quantity (${it?.unit??'pc'})`} value={r.qty} min={1} onChange={v=>set(i,{qty:v})}/>
        {showMoney&&<label>Unit cost ₹ (optional)<input type="number" inputMode="decimal" min="0.01" step="0.01" value={r.cost} placeholder={it?String(it.cost/100):''} onChange={e=>set(i,{cost:e.target.value})}/></label>}
        {rows.length>1&&<button type="button" className="text-btn" onClick={()=>setRows(rows.filter((_,j)=>j!==i))}>Remove</button>}
      </div>;})}
      <button type="button" className="btn" onClick={()=>setRows([...rows,{item:items.find(x=>!rows.some(r=>r.item===x.id))?.id??items[0].id,qty:1,cost:''}])}>+ Add line</button>
      {showMoney&&<p className="muted">A unit cost updates the item’s weighted-average cost. Leave blank to keep the standard cost.</p>}
      <Why text={why}/>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!!why}><PackagePlus size={16}/> Receive to store</button></div>
    </form></>;
}

// ── Issue / return / use / linen change ──────────────────────────────
type Mode='issue'|'return'|'use'|'change';
const modes:{id:Mode;label:string}[]=[{id:'issue',label:'Issue to floor'},{id:'return',label:'Return to store'},{id:'use',label:'Use supplies'},{id:'change',label:'Linen change'}];
export function InvMoveModal({id,room}:ModalProps){
  const {s,run,busy}=useInv();const items=invSlice(s).items;
  const [mode,setMode]=useState<Mode>((room as Mode)||'issue');
  const fit=(it:INVItem)=>mode==='use'?it.kind!=='linen':mode==='change'?it.kind==='linen':true;
  const list=items.filter(fit);
  const [itemId,setItem]=useState(id??'');const it=list.find(x=>x.id===itemId)??list[0];
  const [floor,setFloor]=useState<INVLoc>('floor1');const [q,setQ]=useState(1);const [rm,setRm]=useState('');
  const from:INVLoc=mode==='issue'?'store':floor,avail=it?.stock[from]??0;
  const why=!it?'Choose an item.':q<1?'Quantity must be at least 1.':q>avail?`Only ${avail} at ${invLocLabel(from)}.`:mode==='change'&&q>(it.stock.rooms??0)?'More than the pieces in rooms.':'';
  function submit(e:React.FormEvent){e.preventDefault();if(why||!it)return;
    const cmd=mode==='issue'?{type:'INV_ISSUE',item:it.id,qty:q,from:'store',to:floor}:mode==='return'?{type:'INV_ISSUE',item:it.id,qty:q,from:floor,to:'store'}
      :mode==='use'?{type:'INV_USE',item:it.id,qty:q,floor,room:rm||undefined}:{type:'INV_ROOM_CHANGE',item:it.id,qty:q,floor,room:rm||undefined};
    run(cmd,mode==='issue'?'Issued to floor':mode==='return'?'Returned to store':mode==='use'?'Use recorded':'Linen change recorded',{close:true});}
  return <><Heading icon={ArrowRightLeft} title="Issue, return or use" sub="Moves between the Main store and floor pantries; supplies used in rooms; linen changed in rooms."/>
    <div className="inv-seg" role="group" aria-label="Movement type">{modes.map(m=><button key={m.id} type="button" aria-pressed={mode===m.id} className={mode===m.id?'on':''} onClick={()=>setMode(m.id)}>{m.label}</button>)}</div>
    <form className="form" onSubmit={submit} aria-label="Stock movement">
      <label>Item<select value={it?.id??''} onChange={e=>setItem(e.target.value)}>{list.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
      <label>{mode==='issue'?'To floor pantry':'Floor pantry'}<select value={floor} onChange={e=>setFloor(e.target.value as INVLoc)}>{INV_FLOORS.map(f=><option key={f} value={f}>{invLocLabel(f)}</option>)}</select></label>
      {(mode==='use'||mode==='change')&&<label>Room (optional)<select value={rm} onChange={e=>setRm(e.target.value)}><option value="">Several rooms / not recorded</option>{s.rooms.filter(r=>r.floor===Number(floor.slice(-1))).map(r=><option key={r.id} value={r.id}>{r.id} · {r.type}</option>)}</select></label>}
      <InvQty label={`Quantity (${it?.unit??'pc'})`} value={q} min={1} max={Math.max(1,avail)} onChange={setQ}/>
      {it&&<div className="inv-avail" role="status">Available at {invLocLabel(from)}: <strong>{avail}</strong>{mode==='change'&&<> · in rooms: <strong>{it.stock.rooms??0}</strong> · used pieces go to Soiled</>}</div>}
      <Why text={why}/>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!!why}>{modes.find(m=>m.id===mode)!.label}</button></div>
    </form></>;
}

// ── Laundry: send / receive ──────────────────────────────────────────
export function InvLaundrySendModal(){
  const {s,run,busy}=useInv();const x=invSlice(s);const linen=x.items.filter(i=>i.kind==='linen'&&(i.stock.soiled??0)>0);
  const [q,setQ]=useState<Record<string,number>>(()=>Object.fromEntries(linen.map(i=>[i.id,i.stock.soiled??0])));
  const [note,setNote]=useState('');
  const lines=linen.map(i=>({item:i.id,qty:q[i.id]??0})).filter(l=>l.qty>0);
  const why=!linen.length?'No soiled linen waiting.':!lines.length?'Enter at least one piece to send.':'';
  function submit(e:React.FormEvent){e.preventDefault();if(why)return;run({type:'INV_LAUNDRY_SEND',lines,note:note.trim()},'Laundry batch sent (simulated pickup)',{close:true});}
  return <><Heading icon={Truck} title="Send soiled linen" sub={`To ${x.vendor?.name??'Fresh Linen Services'} · simulated pickup · usual return in ${x.vendor?.turnaroundDays??1} day.`}/>
    <form className="form" onSubmit={submit} aria-label="Send to laundry">
      {!linen.length?<Empty title="Nothing to send" body="Soiled linen appears here after room linen changes."/>:linen.map(i=>
        <InvQty key={i.id} label={`${i.name} (soiled ${i.stock.soiled})`} value={q[i.id]??0} max={i.stock.soiled} onChange={v=>setQ({...q,[i.id]:v})}/>)}
      <label>Note (optional)<input value={note} onChange={e=>setNote(e.target.value)} maxLength={120} placeholder="e.g. 3 sheets with stains — special wash"/></label>
      <p className="muted">Total pieces: <strong>{lines.reduce((a,l)=>a+l.qty,0)}</strong></p>
      <Why text={why}/>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!!why}><Truck size={16}/> Send batch</button></div>
    </form></>;
}
export function InvLaundryReceiveModal({id}:ModalProps){
  const {s,run,busy,money:showMoney}=useInv();const b=invSlice(s).batches.find(b=>b.id===id);
  const [rec,setRec]=useState<Record<string,{received:number;damaged:number}>>(()=>Object.fromEntries((b?.lines??[]).map(l=>[l.item,{received:l.sent,damaged:0}])));
  const [note,setNote]=useState('');
  if(!b)return <Empty title="Batch not found"/>;
  const name=(i:string)=>invItem(s,i)?.name??i;
  if(b.status!=='Out')return <><Heading icon={Shirt} title={`${b.id} · ${b.status}`} sub={`${b.vendor} · sent ${dateLabel(b.sent)} · returned ${b.returned?dateLabel(b.returned):'—'}`}/>
    <div className="table-wrap"><table className="inv-table"><thead><tr><th>Item</th><th className="number">Sent</th><th className="number">Clean back</th><th className="number">Damaged</th><th className="number">Missing</th></tr></thead>
      <tbody>{b.lines.map(l=><tr key={l.item}><td>{name(l.item)}</td><td className="number">{l.sent}</td><td className="number">{l.received}</td><td className="number">{l.damaged||0}</td><td className="number">{invMissing(l)}</td></tr>)}</tbody></table></div>
    {showMoney&&b.charge!==undefined&&<p>Laundry charge (simulated): <strong>{money(b.charge)}</strong></p>}{b.note&&<p className="muted">{b.note}</p>}</>;
  const bad=b.lines.find(l=>{const r=rec[l.item];return r.received+r.damaged>l.sent;});
  const short=b.lines.some(l=>rec[l.item].received<l.sent);
  const why=bad?`${name(bad.item)}: received + damaged cannot exceed ${bad.sent}.`:'';
  const charge=b.lines.reduce((a,l)=>a+(rec[l.item].received+rec[l.item].damaged)*(invItem(s,l.item)?.laundryRate??0),0);
  function submit(e:React.FormEvent){e.preventDefault();if(why)return;
    run({type:'INV_LAUNDRY_RECEIVE',id:b!.id,lines:b!.lines.map(l=>({item:l.item,...rec[l.item]})),note:note.trim()||undefined},short?'Batch received short — loss recorded':'Batch received in full',{close:true});}
  return <><Heading icon={Shirt} title={`Receive ${b.id}`} sub={`${b.vendor} · sent ${dateLabel(b.sent)} by ${invUserName(s,b.actor)}. Count what came back.`}/>
    <form className="form" onSubmit={submit} aria-label="Receive laundry">
      {b.lines.map(l=>{const r=rec[l.item],miss=l.sent-r.received-r.damaged;return <fieldset className="inv-recv" key={l.item}><legend>{name(l.item)} · sent {l.sent}</legend>
        <InvQty label="Clean back" value={r.received} max={l.sent} onChange={v=>setRec({...rec,[l.item]:{...r,received:v}})}/>
        <InvQty label="Damaged" value={r.damaged} max={l.sent} onChange={v=>setRec({...rec,[l.item]:{...r,damaged:v}})}/>
        <span className={miss>0?'inv-bad':miss<0?'inv-bad':'inv-ok'}>{miss>0?`Missing ${miss}`:miss<0?'Over-counted':'All accounted'}</span></fieldset>;})}
      <label>Note (optional)<input value={note} onChange={e=>setNote(e.target.value)} maxLength={120} placeholder="e.g. vendor will search for missing pieces"/></label>
      {short&&<div className="info-note"><Info size={16}/>Damaged pieces are written off; missing pieces are recorded as a vendor shortage (claim) in the loss register.</div>}
      {showMoney&&<p className="muted">Laundry charge for this batch (simulated): <strong>{money(charge)}</strong></p>}
      <Why text={why}/>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!!why}>Receive to Main store</button></div>
    </form></>;
}

// ── Shortage / damage / loss ─────────────────────────────────────────
export function InvLossModal({id,room}:ModalProps){
  const {s,run,busy,money:showMoney}=useInv();const items=invSlice(s).items;
  const [itemId,setItem]=useState(id??items[0]?.id??'');const it=items.find(i=>i.id===itemId);
  const locs=INV_LOCS.filter(l=>l.id!=='laundry'&&(it?.kind==='linen'||INV_COUNTABLE.includes(l.id)));
  const [loc,setLoc]=useState<INVLoc>((room as INVLoc)||'store');const [q,setQ]=useState(1);
  const [reason,setReason]=useState<string>(INV_LOSS_REASONS[0]);const [note,setNote]=useState('');
  const avail=it?.stock[loc]??0;
  const why=!it?'Choose an item.':!locs.some(l=>l.id===loc)?'Choose a location.':q<1?'Quantity must be at least 1.':q>avail?`Only ${avail} at ${invLocLabel(loc)}.`:reason==='Other'&&note.trim().length<3?'Describe the reason.':'';
  function submit(e:React.FormEvent){e.preventDefault();if(why)return;run({type:'INV_LOSS',item:itemId,loc,qty:q,reason,note:note.trim()},'Loss recorded',{close:true});}
  return <><Heading icon={AlertTriangle} title="Report shortage, damage or loss" sub="Written off from the location with a reason. Supervisors review the loss register."/>
    <form className="form" onSubmit={submit} aria-label="Report loss">
      <label>Item<select value={itemId} onChange={e=>setItem(e.target.value)}>{items.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
      <label>Location<select value={loc} onChange={e=>setLoc(e.target.value as INVLoc)}>{locs.map(l=><option key={l.id} value={l.id}>{l.label} ({it?.stock[l.id]??0})</option>)}</select></label>
      <InvQty label="Quantity" value={q} min={1} max={Math.max(1,avail)} onChange={setQ}/>
      <label>Reason<select value={reason} onChange={e=>setReason(e.target.value)}>{INV_LOSS_REASONS.map(r=><option key={r}>{r}</option>)}</select></label>
      <label>Note{reason==='Other'?' (required)':' (optional)'}<input value={note} onChange={e=>setNote(e.target.value)} maxLength={120} placeholder="e.g. Room 206 · hair-dye stains"/></label>
      {showMoney&&it&&<p className="muted">Value written off at standard cost: <strong>{money(q*it.cost)}</strong></p>}
      <Why text={why}/>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!!why}>Record loss</button></div>
    </form></>;
}

// ── Physical count ───────────────────────────────────────────────────
export function InvCountForm({loc,after,close}:{loc:INVLoc;after?:()=>void;close?:boolean}){
  const {s,run,busy}=useInv();const x=invSlice(s);
  const [c,setC]=useState<Record<string,number>>(()=>Object.fromEntries(x.items.map(i=>[i.id,i.stock[loc]??0])));
  const [note,setNote]=useState('');
  const pending=x.counts.find(k=>k.loc===loc&&k.status==='Submitted');
  const diff=x.items.filter(i=>(c[i.id]??0)!==(i.stock[loc]??0)).length;
  function submit(e:React.FormEvent){e.preventDefault();if(pending)return;
    run({type:'INV_COUNT_SUBMIT',loc,counts:c,note:note.trim()||undefined},diff?'Count submitted — variance awaits review':'Count matches — posted',{after,close});}
  if(pending)return <div className="info-note"><Info size={16}/>{pending.id} for {invLocLabel(loc)} by {invUserName(s,pending.actor)} is awaiting review. Review or reject it before counting again.</div>;
  return <form className="form inv-count" onSubmit={submit} aria-label={`Count ${invLocLabel(loc)}`}>
    <p className="muted">Pre-filled with system quantities — confirm or correct each line ({x.items.length} items).</p>
    {INV_KINDS.map(k=><fieldset key={k.id} className="inv-count-group"><legend>{k.label}</legend>
      {x.items.filter(i=>i.kind===k.id).map(i=>{const sys=i.stock[loc]??0,d=(c[i.id]??0)-sys;return <div key={i.id} className={'inv-count-line'+(d?' diff':'')}>
        <InvQty label={i.name} value={c[i.id]??0} max={99999} onChange={v=>setC({...c,[i.id]:v})}/>
        {d!==0&&<span className="inv-bad">{d>0?'+':''}{d} vs system</span>}</div>;})}</fieldset>)}
    <label>Note (optional)<input value={note} onChange={e=>setNote(e.target.value)} maxLength={120}/></label>
    <div className="modal-actions"><button className="btn primary" disabled={busy}><ClipboardCheck size={16}/> Submit count{diff?` · ${diff} variance`:''}</button></div>
  </form>;
}
export function InvCountModal({room}:ModalProps){const loc=(room as INVLoc)||'store';
  return <><Heading icon={ClipboardList} title={`Count · ${invLocLabel(loc)}`} sub="A variance is reviewed by a supervisor or manager who did not count."/><InvCountForm loc={loc} close/></>;}
export function InvCountReviewModal({id}:ModalProps){
  const {s,uid,run,busy,permission,money:showMoney}=useInv();const c=invSlice(s).counts.find(c=>c.id===id);
  const [reason,setReason]=useState('');
  if(!c)return <Empty title="Count not found"/>;
  const v=invCountVariance(c),own=c.actor===uid;
  const why=c.status!=='Submitted'?'':!permission('inspect')?'Reviewing a count needs the inspect permission (supervisor or manager).':own?'You counted this — another supervisor or manager reviews it.':reason.trim().length<3?'Enter the reason for the variance (or for rejecting).':'';
  const decide=(approve:boolean)=>{if(why)return;run({type:'INV_COUNT_POST',id:c.id,approve,reason:reason.trim()},approve?'Variance posted — stock adjusted':'Count rejected',{close:true});};
  return <><Heading icon={ClipboardCheck} title={`${c.id} · ${invLocLabel(c.loc)}`} sub={`Counted by ${invUserName(s,c.actor)} · ${dateLabel(c.date)} ${c.at.slice(11)} · ${c.status}${c.reviewer?' · reviewed by '+invUserName(s,c.reviewer):''}`}/>
    {!v.length?<Empty title="No variance" body="The count matched the system quantities."/>:<div className="table-wrap"><table className="inv-table"><thead><tr><th>Item</th><th className="number">System</th><th className="number">Counted</th><th className="number">Variance</th>{showMoney&&<th className="number">Value</th>}</tr></thead>
      <tbody>{v.map(l=>{const it=invItem(s,l.item),d=l.counted-l.system;return <tr key={l.item}><td>{it?.name}</td><td className="number">{l.system}</td><td className="number">{l.counted}</td><td className={'number '+(d<0?'inv-bad':'inv-ok')}>{d>0?'+':''}{d}</td>{showMoney&&<td className="number">{money(d*(it?.cost??0))}</td>}</tr>;})}</tbody></table></div>}
    {showMoney&&v.length>0&&<p>Net variance value: <strong>{money(invCountValue(s,c))}</strong></p>}
    {c.note&&<p className="muted">Counter’s note: {c.note}</p>}{c.reason&&<p className="muted">Review reason: {c.reason}</p>}
    {c.status==='Submitted'&&<div className="form"><label>Reason (required)<input value={reason} onChange={e=>setReason(e.target.value)} maxLength={120} placeholder="e.g. 3 bottles issued to room 204 without a record"/></label>
      <Why text={why}/>
      <div className="modal-actions"><button className="btn" disabled={busy||!!why} onClick={()=>decide(false)}>Reject</button><button className="btn primary" disabled={busy||!!why} onClick={()=>decide(true)}>Post variance</button></div></div>}</>;
}

// ── Item settings (min, reorder, par, standard) ──────────────────────
export function InvItemModal({id}:ModalProps){
  const {s,run,busy,permission}=useInv();const it=id?invItem(s,id):undefined;const types=invRoomTypes(s);
  const [min,setMin]=useState(it?.min??0);const [ro,setRo]=useState(it?.reorderQty??0);const [std,setStd]=useState(String((it?.stdX100??0)/100));
  const [par,setPar]=useState<Record<string,number>>(()=>({...Object.fromEntries(types.map(t=>[t,0])),...(it?.par??{})}));
  if(!it)return <Empty title="Item not found"/>;
  const stdX=Math.round(Number(std)*100);
  const why=!permission('inspect')?'Changing minimums and par needs the inspect permission.':!(stdX>=0&&stdX<=10000)?'Standard use must be 0–100 per room-night.':'';
  function submit(e:React.FormEvent){e.preventDefault();if(why)return;run({type:'INV_ITEM_SET',id:it!.id,min,reorderQty:ro,stdX100:stdX,...(it!.kind==='linen'?{par}:{})},'Item settings saved',{close:true});}
  return <><Heading icon={Settings2} title={it.name} sub="Minimum clean stock triggers a reorder alert. Par = pieces per room; circulation needs par × rooms × par sets."/>
    <form className="form" onSubmit={submit} aria-label="Item settings">
      <InvQty label="Minimum clean stock" value={min} onChange={setMin}/>
      <InvQty label="Reorder quantity" value={ro} onChange={setRo}/>
      <label>Standard use per occupied room-night<input type="number" inputMode="decimal" min="0" max="100" step="0.01" value={std} onChange={e=>setStd(e.target.value)}/></label>
      {it.kind==='linen'&&<fieldset className="inv-count-group"><legend>Par per room</legend>{types.map(t=><InvQty key={t} label={t} value={par[t]??0} max={20} onChange={v=>setPar({...par,[t]:v})}/>)}</fieldset>}
      <Why text={why}/>
      <div className="modal-actions"><button className="btn primary" disabled={busy||!!why}>Save</button></div>
    </form></>;
}

// ── Printable report (stock sheet / consumption) ─────────────────────
export function InvPrintModal({id,room}:ModalProps){
  const {s,money:showMoney}=useInv();const x=invSlice(s);
  const [from,to]=(room||'').split('|');const f=from||addDays(s.date,-7),t=to||addDays(s.date,-1);
  const cons=id==='consumption'?invConsumption(s,f,t):null;
  return <div className="inv-print">
    <div className="inv-print-head"><div><h2>{s.settings.hotel} · {cons?'Consumption per occupied room-night':'Stock by location'}</h2>
      <p>{cons?`${dateLabel(f)} – ${dateLabel(t)} · ${cons.rn} occupied room-nights`:`Business date ${dateLabel(s.date)} · simulated clock ${s.clock.slice(11)}`} · Fictional demonstration data</p></div>
      <button className="btn no-print" onClick={()=>{void printDocument();}}><Printer size={16}/> Print</button></div>
    {cons?<table className="inv-table"><thead><tr><th>Item</th><th className="number">Used</th><th className="number">Per RN</th><th className="number">Standard</th><th className="number">Variance</th></tr></thead>
      <tbody>{cons.rows.map(r=><tr key={r.item.id}><td>{r.item.name}</td><td className="number">{r.used}</td><td className="number">{invFmt(r.perRN)}</td><td className="number">{invFmt(r.std)}</td><td className="number">{r.variance===null?'—':(r.variance>0?'+':'')+r.variance+'%'}{r.flag?' · check':''}</td></tr>)}</tbody></table>
    :<table className="inv-table"><thead><tr><th>Item</th>{INV_LOCS.map(l=><th key={l.id} className="number">{l.short}</th>)}<th className="number">Total</th><th className="number">Min</th>{showMoney&&<th className="number">Value</th>}</tr></thead>
      <tbody>{x.items.map(i=><tr key={i.id}><td>{i.name}</td>{INV_LOCS.map(l=><td key={l.id} className="number">{i.kind!=='linen'&&!INV_COUNTABLE.includes(l.id)?'—':i.stock[l.id]??0}</td>)}<td className="number">{invTotal(i)}</td><td className="number">{i.min}</td>{showMoney&&<td className="number">{money(invValue(i))}</td>}</tr>)}</tbody></table>}
    {!cons&&showMoney&&<p>Total stock value at standard cost: <strong>{money(x.items.reduce((a,i)=>a+invValue(i),0))}</strong></p>}
    <p className="muted">Printed from Hotel Control Centre prototype · {shortDate(s.date)} · Signature: ____________________</p>
  </div>;
}
