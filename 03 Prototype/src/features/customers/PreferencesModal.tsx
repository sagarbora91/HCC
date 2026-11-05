// F12 MEAS · guest preferences, blacklist and the "known guest" badge (MEAS_PREFERENCE_SET). id = Customer 360 key.
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {mdPreference,mdKnownGuest,mdOn,MD_KEY} from '../../services/mock';
import {customerIndex} from '../../domain/streams/customers';
import type {Booking} from '../../services/mock';

export function PreferencesModal({id}:{id?:string}){
  const {s,run,busy,closeModal,user}=useHcc();
  const c=customerIndex(s).find(x=>x.key===id);
  const cur=mdPreference(s,id??'')??{};
  const [f,setF]=useState({floor:cur.floor??'',roomKind:cur.roomKind??'',meal:cur.meal??'',pillow:cur.pillow??'',billToCompany:cur.billToCompany??'',note:cur.note??'',dnd:!!cur.doNotDisturb});
  const [bl,setBl]=useState('');const [approver,setApprover]=useState('');const [clearWhy,setClearWhy]=useState('');
  if(!c)return <p>This customer is unavailable.</p>;
  const set=(k:keyof typeof f,v:string|boolean)=>setF({...f,[k]:v});
  const boss=user.role==='Manager'||user.role==='Owner';
  const owners=s.users.filter(u=>u.enabled&&u.role==='Owner'&&u.id!==user.id);
  const save=(extra:Record<string,unknown>,msg:string)=>run({type:'MEAS_PREFERENCE_SET',customer:c.key,floor:f.floor,roomKind:f.roomKind,meal:f.meal,pillow:f.pillow,billToCompany:f.billToCompany,note:f.note,doNotDisturb:f.dnd,...extra,key:`meas-pref-${c.key}-${s.clock}`},msg,{close:true});
  return <form className="form meas-form" onSubmit={e=>{e.preventDefault();save({},'Preferences saved');}}>
    <div className="modal-heading"><h2>Preferences · {c.name}</h2><p>Kept for the next stay. Shown at check-in.</p></div>
    <div className="hov-form-row"><label>Floor<input maxLength={80} value={f.floor} onChange={e=>set('floor',e.target.value)} placeholder="e.g. top floor"/></label>
      <label>Room kind<input maxLength={80} value={f.roomKind} onChange={e=>set('roomKind',e.target.value)} placeholder="e.g. quiet, away from lift"/></label></div>
    <div className="hov-form-row"><label>Meal<input maxLength={80} value={f.meal} onChange={e=>set('meal',e.target.value)} placeholder="e.g. Jain breakfast"/></label>
      <label>Pillow<input maxLength={80} value={f.pillow} onChange={e=>set('pillow',e.target.value)} placeholder="e.g. firm"/></label></div>
    <label>Bill to company<input maxLength={80} value={f.billToCompany} onChange={e=>set('billToCompany',e.target.value)}/></label>
    <label>Note<input maxLength={80} value={f.note} onChange={e=>set('note',e.target.value)}/></label>
    <label className="hov-inline"><input type="checkbox" checked={f.dnd} onChange={e=>set('dnd',e.target.checked)}/>Do not disturb in the morning</label>
    {boss&&(cur.blacklist
      ?<fieldset className="hov-fieldset"><legend>Blacklisted</legend><p>{cur.blacklist.reason} · approved by {cur.blacklist.approvedBy}</p>
        <label>Reason for clearing<input maxLength={80} value={clearWhy} onChange={e=>setClearWhy(e.target.value)}/></label>
        <button type="button" className="btn" disabled={busy||clearWhy.trim().length<3} onClick={()=>save({blacklist:null,reason:clearWhy},'Blacklist cleared')}>Clear blacklist</button></fieldset>
      :<fieldset className="hov-fieldset"><legend>Blacklist (Manager with the Owner’s approval)</legend>
        <label>Reason<input maxLength={80} value={bl} onChange={e=>setBl(e.target.value)}/></label>
        {user.role==='Manager'&&<label>Approved by<select value={approver} onChange={e=>setApprover(e.target.value)}><option value="">Choose the Owner…</option>{owners.map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label>}
        <button type="button" className="btn" disabled={busy||bl.trim().length<3||(user.role==='Manager'&&!approver)} onClick={()=>save({blacklist:bl,approvedBy:approver||undefined},'Guest blacklisted')}>Blacklist guest</button></fieldset>)}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save preferences</button></div>
  </form>;
}

/** Badge for the arrivals list: "Known guest · 3 stays · last 12 Aug 2026"; red "Blacklisted" when set. Nothing for a first-time guest or while off. */
export function KnownGuestBadge({b}:{b:Booking}){
  const {s,open,user}=useHcc();
  if(!mdOn(s,MD_KEY.preference))return null;
  const k=mdKnownGuest(s,b);if(!k)return null;
  const can=['Reception','Manager','Owner'].includes(user.role);
  const text=k.blacklisted?'Blacklisted — call a Manager':`Known guest · ${k.stays} stay${k.stays===1?'':'s'}${k.lastVisit?' · last '+k.lastVisit:''}`;
  return <button type="button" className={'pill'+(k.blacklisted?' bad':'')} disabled={!can} onClick={()=>open({type:'meas-prefs',id:k.key})}>{text}</button>;
}
