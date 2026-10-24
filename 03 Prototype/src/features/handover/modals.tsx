// Module 19 · dialogs/sheets: item note+photo, handover note, acknowledge, template editor, reopen.
import React,{useState} from 'react';
import {Camera,Plus,Trash2,ArrowUp,ArrowDown,Send,CheckCheck,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {navItems} from '../../app/nav';
import {hovSlice,hovRunFor,hovNewRun,hovDesksFor,hovShiftAt,hovNextShift,hovOpenCarry,hovNoteId,HOV_DESK_ROLES,HOV_DESKS,HOV_SHIFTS,HOV_MAX_CARRY,HOV_MAX_ITEMS,HOV_NOTE_MAX} from '../../services/mock';
import type {HOVDesk,HOVShift,HOVItem,Role} from '../../services/mock';
import {Empty} from '../../components/ui';
import {hovKey,hovName,HovNoteView} from './shared';

export function HovItemModal({id,room}:ModalProps){
  const {s,uid,run,busy,closeModal}=useHcc();
  const t=hovSlice(s).templates.find(t=>t.id===id);const r=t&&(hovRunFor(s,t.id)??hovNewRun(t,s.date,uid,s.clock));
  const item=r?.items.find(i=>i.id===room);
  const [note,setNote]=useState(item?.note??'');const [photo,setPhoto]=useState(item?.photoName??'');
  if(!t||!r||!item)return <Empty title="This checklist item is unavailable"/>;
  const simulate=()=>setPhoto(`IMG_${s.clock.slice(0,10).replaceAll('-','')}_${s.clock.slice(11,16).replace(':','')}.jpg`);
  const save=(tick:boolean)=>run({type:'HOV_ITEM',template:t.id,item:item.id,note,photo,...(tick?{done:true}:{}),key:hovKey(s,'item',t.id,item.id,tick)},tick?'Saved and ticked':'Note / photo saved',{close:true});
  return <form className="form" onSubmit={e=>{e.preventDefault();save(!item.done);}}>
    <div className="modal-heading"><h2>{item.text}</h2><p>{r.name} · {r.shift}{item.due?' · due '+item.due:''}{item.mandatory?' · mandatory':''}</p></div>
    <label>Note (optional)<textarea maxLength={HOV_NOTE_MAX} rows={3} value={note} onChange={e=>setNote(e.target.value)} placeholder="e.g. Encoder slow — restarted" enterKeyHint="done"/><small>{note.length}/{HOV_NOTE_MAX}</small></label>
    <label>Photo file name {item.photo?'(required to tick)':'(optional)'}<input value={photo} maxLength={80} onChange={e=>setPhoto(e.target.value)} placeholder="IMG_20261024_0812.jpg" enterKeyHint="done" autoCapitalize="off"/></label>
    <div className="hov-photo-row"><button type="button" className="btn" onClick={simulate}><Camera size={16}/>Simulate photo</button><small>Simulated — no camera or upload in the prototype; only the file name is stored.</small></div>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button type="button" className="btn" disabled={busy} onClick={()=>save(false)}>Save only</button>
      {!item.done&&<button className="btn primary" disabled={busy||(item.photo&&!photo.trim())}>Save & tick</button>}</div>
  </form>;}

export function HovNoteModal({id}:ModalProps){
  const {s,uid,run,busy,closeModal,permission}=useHcc();
  const desks=hovDesksFor(s,uid);
  const [desk,setDesk]=useState<HOVDesk>((desks.includes(id as HOVDesk)?id:desks[0]) as HOVDesk);
  const [shift,setShift]=useState<HOVShift>(hovShiftAt(s.clock));
  const existing=hovSlice(s).notes.find(n=>n.id===hovNoteId(desk,s.date,shift)&&n.status==='Submitted');
  const [f,setF]=useState({issues:'',keys:'',guests:'',nothing:false,cash:'',cashNote:''});
  const [carry,setCarry]=useState<string[]>(['']);
  if(!desks.length)return <Empty title="No handover desk for your role"/>;
  const open=hovOpenCarry(s,desk);
  const showCash=desk==='Front desk'&&permission('payments');
  const set=(k:keyof typeof f,v:string|boolean)=>setF({...f,[k]:v});
  function submit(e:React.FormEvent){e.preventDefault();
    const cash=showCash&&f.cash.trim()!==''?{amount:Math.round(Number(f.cash)*100),note:f.cashNote}:undefined;
    run({type:'HOV_NOTE_SUBMIT',desk,shift,issues:f.issues,keys:f.keys,guests:f.guests,nothing:f.nothing,cash,carry:carry.filter(c=>c.trim()),key:hovKey(s,'note',desk,shift)},'Handover note submitted — the next shift must acknowledge it',{close:true});}
  return <form className="form" onSubmit={submit}>
    <div className="modal-heading"><h2>Write handover note</h2><p>For the {hovNextShift(shift)} shift · business date {s.date}. The next shift must acknowledge it before submitting their checklist.</p></div>
    <div className="hov-form-row">
      <label>Desk<select value={desk} onChange={e=>setDesk(e.target.value as HOVDesk)}>{desks.map(d=><option key={d}>{d}</option>)}</select></label>
      <label>Outgoing shift<select value={shift} onChange={e=>setShift(e.target.value as HOVShift)}>{HOV_SHIFTS.map(x=><option key={x}>{x}</option>)}</select></label>
    </div>
    {existing&&<div className="info-note"><Info size={16}/>A {desk} note for this shift by {hovName(s,existing.author)} is waiting. Submitting replaces it (author or manager only).</div>}
    <label className="hov-inline"><input type="checkbox" checked={f.nothing} onChange={e=>set('nothing',e.target.checked)}/>Nothing to report</label>
    <label>Open issues{f.nothing?' (optional)':''}<textarea rows={3} maxLength={400} value={f.issues} onChange={e=>set('issues',e.target.value)} placeholder="What is unresolved? Rooms, complaints, pending work…"/></label>
    <label>Keys / key cards<textarea rows={2} maxLength={400} value={f.keys} onChange={e=>set('keys',e.target.value)} placeholder="e.g. 14 cards out, master key in the safe"/></label>
    <label>Guest notes<textarea rows={2} maxLength={400} value={f.guests} onChange={e=>set('guests',e.target.value)} placeholder="Wake-up calls, requests, VIPs"/></label>
    {showCash&&<div className="hov-form-row">
      <label>Cash float handed over (₹)<input inputMode="decimal" type="number" min="0" step="0.01" value={f.cash} onChange={e=>set('cash',e.target.value)} placeholder="e.g. 5000" enterKeyHint="next"/></label>
      <label>Cash remark<input maxLength={HOV_NOTE_MAX} value={f.cashNote} onChange={e=>set('cashNote',e.target.value)} placeholder="e.g. counted with Priya" enterKeyHint="next"/></label></div>}
    <fieldset className="hov-fieldset"><legend>Carry-forward items (stay open until done)</legend>
      {open.length>0&&<small>Already open on this desk: {open.map(c=>c.text).join(' · ')}</small>}
      {carry.map((c,i)=><div className="hov-carry-input" key={i}><input aria-label={`Carry-forward item ${i+1}`} value={c} maxLength={120} onChange={e=>setCarry(carry.map((x,j)=>j===i?e.target.value:x))} placeholder="e.g. Call 301 about late checkout" enterKeyHint="next"/>
        <button type="button" className="btn hov-icon-btn" aria-label={`Remove item ${i+1}`} onClick={()=>setCarry(carry.filter((_,j)=>j!==i))}><Trash2 size={16}/></button></div>)}
      {carry.length<HOV_MAX_CARRY&&<button type="button" className="btn" onClick={()=>setCarry([...carry,''])}><Plus size={15}/>Add carry-forward item</button>}
    </fieldset>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}><Send size={15}/>Submit handover</button></div>
  </form>;}

export function HovAckModal({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const n=hovSlice(s).notes.find(n=>n.id===id);const [remark,setRemark]=useState('');
  if(!n)return <Empty title="This handover note is unavailable"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HOV_NOTE_ACK',id:n.id,remark,key:hovKey(s,'ack',n.id)},'Handover acknowledged',{close:true});}}>
    <div className="modal-heading"><h2>Acknowledge handover</h2><p>{n.desk} · {n.shift} → {n.toShift} · from {hovName(s,n.author)}</p></div>
    <HovNoteView note={n}/>
    {n.status==='Submitted'&&<label>Remark (optional)<input maxLength={HOV_NOTE_MAX} value={remark} onChange={e=>setRemark(e.target.value)} placeholder="e.g. Read, will call 301 at 10:00" enterKeyHint="done"/></label>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
      {n.status==='Submitted'&&<button className="btn primary" disabled={busy}><CheckCheck size={16}/>I have read this — acknowledge</button>}</div>
  </form>;}

type Draft=Partial<HOVItem>&{key:string};
const LINKABLE=()=>navItems.filter(n=>!['overview','access','settings'].includes(n.id));
export function HovTemplateModal({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();
  const t=hovSlice(s).templates.find(t=>t.id===id);
  const [name,setName]=useState(t?.name??'');const [desk,setDesk]=useState<HOVDesk>(t?.desk??'Front desk');const [shift,setShift]=useState<HOVShift>(t?.shift??'Morning');
  const [roles,setRoles]=useState<Role[]>(t?.roles??[]);
  const [items,setItems]=useState<Draft[]>(()=>(t?.items??[{id:'',text:'',mandatory:true}]).map((i,n)=>({...i,key:'k'+n})));
  const [seq,setSeq]=useState(100);
  const upd=(k:string,p:Partial<HOVItem>)=>setItems(items.map(i=>i.key===k?{...i,...p}:i));
  const move=(n:number,d:number)=>{const a=[...items];const [x]=a.splice(n,1);a.splice(n+d,0,x);setItems(a);};
  const deskRoles=[...HOV_DESK_ROLES[desk]];
  function submit(e:React.FormEvent){e.preventDefault();
    const tpl={id:t?.id,name,desk,shift,roles:roles.filter(r=>deskRoles.includes(r)),items:items.map(({key:_k,...i})=>({...i,id:i.id||undefined}))};
    run({type:'HOV_TEMPLATE_SAVE',template:tpl,key:hovKey(s,'tpl',t?.id??name)},t?'Template saved · today’s started runs keep their items':'Template created',{close:true});}
  return <form className="form hov-tpl-form" onSubmit={submit}>
    <div className="modal-heading"><h2>{t?`Edit template · v${t.version}`:'New checklist template'}</h2><p>Changes apply to checklists started after saving. Runs already started keep their snapshot.</p></div>
    <label>Template name<input required minLength={3} maxLength={60} value={name} onChange={e=>setName(e.target.value)} enterKeyHint="next"/></label>
    <div className="hov-form-row">
      <label>Desk<select value={desk} onChange={e=>{const d=e.target.value as HOVDesk;setDesk(d);setRoles(roles.filter(r=>HOV_DESK_ROLES[d].includes(r)));}}>{HOV_DESKS.map(d=><option key={d}>{d}</option>)}</select></label>
      <label>Shift<select value={shift} onChange={e=>setShift(e.target.value as HOVShift)}>{HOV_SHIFTS.map(x=><option key={x}>{x}</option>)}</select></label>
    </div>
    <fieldset className="hov-fieldset"><legend>Roles who tick this checklist</legend><div className="hov-role-row">
      {deskRoles.map(r=><label key={r} className="hov-inline"><input type="checkbox" checked={roles.includes(r)} onChange={e=>setRoles(e.target.checked?[...roles,r]:roles.filter(x=>x!==r))}/>{r}</label>)}</div></fieldset>
    <fieldset className="hov-fieldset"><legend>Items ({items.length}/{HOV_MAX_ITEMS})</legend>
      <ol className="hov-tpl-items">{items.map((i,n)=><li key={i.key}>
        <input aria-label={`Item ${n+1} text`} value={i.text??''} maxLength={120} onChange={e=>upd(i.key,{text:e.target.value})} placeholder="What must be checked?"/>
        <div className="hov-tpl-opts">
          <label className="hov-inline"><input type="checkbox" checked={!!i.mandatory} onChange={e=>upd(i.key,{mandatory:e.target.checked})}/>Mandatory</label>
          <label className="hov-inline"><input type="checkbox" checked={!!i.photo} onChange={e=>upd(i.key,{photo:e.target.checked})}/>Photo</label>
          <label className="hov-inline">Due<input type="time" aria-label={`Item ${n+1} due time`} value={i.due??''} onChange={e=>upd(i.key,{due:e.target.value||undefined})}/></label>
          <label className="hov-inline">Link<select aria-label={`Item ${n+1} link`} value={i.link??''} onChange={e=>upd(i.key,{link:e.target.value||undefined})}><option value="">None</option>{LINKABLE().map(p=><option key={p.id} value={p.id}>{p.label}</option>)}</select></label>
          <span className="hov-tpl-move">
            <button type="button" className="btn hov-icon-btn" aria-label={`Move item ${n+1} up`} disabled={n===0} onClick={()=>move(n,-1)}><ArrowUp size={15}/></button>
            <button type="button" className="btn hov-icon-btn" aria-label={`Move item ${n+1} down`} disabled={n===items.length-1} onClick={()=>move(n,1)}><ArrowDown size={15}/></button>
            <button type="button" className="btn hov-icon-btn" aria-label={`Remove item ${n+1}`} disabled={items.length<=1} onClick={()=>setItems(items.filter(x=>x.key!==i.key))}><Trash2 size={15}/></button></span>
        </div></li>)}</ol>
      {items.length<HOV_MAX_ITEMS&&<button type="button" className="btn" onClick={()=>{setItems([...items,{key:'n'+seq,text:'',mandatory:true}]);setSeq(seq+1);}}><Plus size={15}/>Add item</button>}
    </fieldset>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save template</button></div>
  </form>;}

export function HovReopenModal({id,room}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const r=hovRunFor(s,id||'',room||s.date);const [reason,setReason]=useState('');
  if(!r)return <Empty title="This checklist is unavailable"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HOV_RUN_REOPEN',template:r.template,date:r.date,reason,key:hovKey(s,'reopen',r.id)},'Checklist reopened',{close:true});}}>
    <div className="modal-heading"><h2>Reopen checklist</h2><p>{r.name} · {r.date} · submitted by {hovName(s,r.submittedBy)}</p></div>
    <label>Reason (required)<textarea required minLength={3} maxLength={HOV_NOTE_MAX} rows={3} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Lobby photo missing"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||reason.trim().length<3}>Reopen</button></div>
  </form>;}
