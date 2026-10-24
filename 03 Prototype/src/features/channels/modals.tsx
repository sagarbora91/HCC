// Module 21–22 · dialogs: inbound message, reject, channel terms & mapping, commission print view, web-request decline.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Check,X,Printer} from 'lucide-react';
import {money,chnSlice,chnAcceptCheck,chnChannelName,chnFreeRooms,chnRoomTypes,chnPct,chnOriginal,chnWebStatus,CHN_MAX_COMMISSION_BPS,CHN_MAX_MARKUP_BPS} from '../../services/mock';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {clockTime,shortDate,dateLabel} from '../../components/ui';
import {ChnPill,ChnSimulated,stay,party,firstName} from './shared';
import {ConnectionButtons,CommissionTables} from './Channels';

export function MessageModal({id}:ModalProps){
  const {s,run,busy,closeModal,open}=useHcc();
  const m=chnSlice(s).messages.find(m=>m.id===id);
  const [room,setRoom]=useState('');
  if(!m)return <div className="modal-heading"><h2>Message not found</h2></div>;
  const chk=chnAcceptCheck(s,m,room||undefined),orig=m.kind!=='New'?chnOriginal(s,m):undefined;
  const free=m.kind==='New'&&m.status==='Pending'?chnFreeRooms(s,m.roomType,m.arrival,m.departure,m.adults+m.children):[];
  return <><div className="modal-heading"><div className="eyebrow">{chnChannelName(s,m.channel).toUpperCase()} · {m.ref} · SIMULATED</div>
    <h2>{m.kind==='New'?'New reservation':m.kind==='Modify'?'Modification':'Cancellation'} · {m.guest}</h2><p>Received {shortDate(m.receivedAt.slice(0,10))} {clockTime(m.receivedAt)} · <ChnPill status={m.status}/></p></div>
    <div className="summary-list chn-sum">
      <div><span>Room type</span><strong>{m.roomType}</strong></div>
      <div><span>Stay</span><strong>{stay(m.arrival,m.departure)}</strong></div>
      <div><span>Party</span><strong>{party(m.adults,m.children)}</strong></div>
      <div><span>Contact (fictional)</span><strong>{m.phone||'—'}{m.email?' · '+m.email:''}</strong></div>
      <div><span>Channel amount (as received)</span><strong>{m.amount?money(m.amount):'—'}</strong></div>
      {orig&&<div><span>Original reservation</span><strong>{orig.status}{orig.booking?` · ${orig.booking}`:''} · {stay(orig.arrival,orig.departure)}</strong></div>}
      {m.booking&&<div><span>HCC booking</span><strong>{m.booking} · room {m.room}</strong></div>}
      {m.reason&&<div><span>Reason</span><strong>{m.reason}</strong></div>}
    </div>
    {m.status==='Pending'&&<>
      {free.length>1&&<label className="form chn-mt">Room to allocate<select value={room} onChange={e=>setRoom(e.target.value)}><option value="">First free room ({free[0].id})</option>{free.map(r=><option key={r.id} value={r.id}>Room {r.id} · floor {r.floor}</option>)}</select></label>}
      <div className={'chn-verdict '+(chk.ok?'ok':'bad')} role="status"><strong>{chk.ok?'Can accept':'Cannot accept'}</strong><span>{chk.reason}</span>{chk.warning&&<em>{chk.warning}</em>}</div>
      {m.kind==='New'&&<p className="chn-muted chn-mt">Accepting creates a Confirmed booking (source OTA · {chnChannelName(s,m.channel)}) at the hotel tariff through the normal booking rules.</p>}
      <div className="modal-actions"><button className="btn" onClick={()=>open({type:'chn-reject',id:m.id})}><X size={15}/> Reject…</button>
        <button className="btn primary" disabled={busy||!chk.ok} onClick={()=>run({type:'CHN_ACCEPT',id:m.id,room:room||undefined},m.kind==='New'?`${m.ref} accepted — booking created`:`${m.ref} ${m.kind.toLowerCase()} applied`,{close:true})}><Check size={15}/> Accept</button></div></>}
    {m.status!=='Pending'&&<div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div>}
  </>;
}

export function RejectModal({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const m=chnSlice(s).messages.find(m=>m.id===id);
  const chk=m?chnAcceptCheck(s,m):undefined;
  const [reason,setReason]=useState(chk&&!chk.ok?chk.reason:'');
  if(!m)return <div className="modal-heading"><h2>Message not found</h2></div>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'CHN_REJECT',id:m.id,reason:reason.trim()},`${m.ref} rejected`,{close:true});}}>
    <div className="modal-heading"><div className="eyebrow">{chnChannelName(s,m.channel).toUpperCase()} · {m.ref}</div><h2>Reject {m.kind.toLowerCase()} message</h2><p>The channel would be told the reason (simulated). No booking changes.</p></div>
    <label>Reason<textarea required minLength={3} maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. No Suite available for these dates" enterKeyHint="done"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||reason.trim().length<3}><X size={15}/> Reject message</button></div>
  </form>;
}

export function ChannelModal({id}:ModalProps){
  const {s,run,busy,closeModal,permission}=useHcc();const c=chnSlice(s).channels.find(c=>c.id===id);const types=chnRoomTypes(s);const ro=!permission('approve');
  const [com,setCom]=useState(c?String(c.commissionBps/100):'');const [mk,setMk]=useState(c?String(c.markupBps/100):'');
  const [map,setMap]=useState<Record<string,string>>(c?{...c.mapping}:{});
  if(!c)return <div className="modal-heading"><h2>Channel not found</h2></div>;
  const bps=(v:string)=>Math.round(Number(v)*100);
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'CHN_CHANNEL_SAVE',channel:c.id,commissionBps:bps(com),markupBps:bps(mk),mapping:Object.fromEntries(types.map(t=>[t,map[t]||'']))},`${c.name} terms saved`,{close:true});}}>
    <div className="modal-heading"><div className="eyebrow">CHANNEL · SIMULATED</div><h2>{c.name} · terms & room mapping</h2><p>Status <ChnPill status={c.status}/> {c.statusNote}</p></div>
    {ro?<div className="info-note chn-note">View only — a Manager or Owner changes connection, commission and mapping.</div>:<div className="chn-conn"><ConnectionButtons c={c}/></div>}
    <fieldset className="chn-fieldset chn-plain" disabled={ro}>
    <div className="form-row">
      <label>Commission %<input inputMode="decimal" enterKeyHint="next" value={com} onChange={e=>setCom(e.target.value)} aria-describedby="chn-com-help"/><small id="chn-com-help">0–{chnPct(CHN_MAX_COMMISSION_BPS)} of room revenue</small></label>
      <label>Rate markup %<input inputMode="decimal" enterKeyHint="next" value={mk} onChange={e=>setMk(e.target.value)}/><small>0–{chnPct(CHN_MAX_MARKUP_BPS)} added to the hotel tariff when pushed</small></label>
    </div>
    <fieldset className="chn-fieldset"><legend>Room type → channel room code</legend>
      {types.map(t=><label key={t}>{t}<input value={map[t]||''} autoCapitalize="characters" maxLength={20} placeholder="Not mapped" onChange={e=>setMap({...map,[t]:e.target.value.toUpperCase()})}/></label>)}
      <small>Leave blank to stop selling that room type on {c.name}. Codes: 2–20 letters, digits or dashes; unique per channel.</small></fieldset>
    </fieldset>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>{ro?'Close':'Cancel'}</button>{!ro&&<button className="btn primary" disabled={busy}><Check size={15}/> Save</button>}</div>
  </form>;
}

export function CommissionPrintModal(){
  const {s,closeModal}=useHcc();
  return <><div className="modal-heading"><div className="eyebrow">{s.settings.hotel.toUpperCase()} · SIMULATED</div><h2>OTA commission report</h2><p>As of {dateLabel(s.date)} · fictional channels · demonstration figures only</p></div>
    <CommissionTables/>
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={15}/> Print</button></div></>;
}

export function WebDeclineModal({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const w=chnSlice(s).webRequests.find(w=>w.id===id);
  const expired=w?chnWebStatus(s,w)==='Expired':false;
  const [reason,setReason]=useState(expired?'Hold expired before confirmation':'');
  if(!w)return <div className="modal-heading"><h2>Request not found</h2></div>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'CHN_WEB_DECLINE',id:w.id,reason:reason.trim()},`${w.id} declined`,{close:true});}}>
    <div className="modal-heading"><div className="eyebrow">{w.id} · {w.booking}</div><h2>Decline web request · {w.guest}</h2><p>{w.roomType} · {stay(w.arrival,w.departure)} · {expired?'the hold has already expired':'the held room is released'}. The guest would be notified (simulated).</p></div>
    <label>Reason<textarea required minLength={3} maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Rooms reserved for a group"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||reason.trim().length<3}><X size={15}/> Decline request</button></div>
  </form>;
}
