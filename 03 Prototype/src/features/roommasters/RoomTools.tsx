// MA-1 · Tools above the Rooms master list (Master configuration › Rooms and rate plans › Lists › Rooms; desktop and phone):
// add many rooms at once ("101–110, floor F1, type DLX"), renumber a room (no booking from today on), set / clear out of order
// (a room hold with dates and a reason). Rows are edited / retired with the shared MasterForm. Classes rms- (styles/m-roommasters.css).
import React,{useState} from 'react';
import {Plus,Hash,Ban,Check,RotateCcw} from 'lucide-react';
import {mstRows,mstCanEdit,mstSpec,mstFloorNumber,rmsParseRooms,rmsNumberTaken,rmsRangeLabel,rmsOooRooms,rmsTypeName,rmsLiveBookings,rmsLockText,
  MST_FLOORS,MST_ROOM_TYPES,MST_ROOMS} from '../../services/mock';
import {useHcc} from '../../app/context';
import {shortDate} from '../../components/ui';
import type {MstToolsProps} from '../masters/tools';

export function RoomTools({phone}:MstToolsProps){
  const {s,uid}=useHcc();
  const spec=mstSpec(MST_ROOMS);
  if(!spec||!mstCanEdit(s,uid,spec).ok)return <OooList readOnly phone={phone}/>;
  return <div className={'rms-tools'+(phone?' rms-phone':'')}>
    <RangeAdd/><Renumber/><OutOfOrder/><OooList phone={phone}/>
  </div>;
}

function RangeAdd(){
  const {s,run,busy}=useHcc();
  const floors=mstRows(s,MST_FLOORS,{active:true}).filter(f=>mstFloorNumber(f.code)!==undefined);
  const types=mstRows(s,MST_ROOM_TYPES,{active:true});
  const [text,setText]=useState('');const [floor,setFloor]=useState(floors[0]?.code??'');const [tcode,setTcode]=useState(types[0]?.code??'');
  const p=text.trim()?rmsParseRooms(text):{numbers:[] as string[]};
  const taken=p.numbers.map(n=>rmsNumberTaken(s,n)).filter(Boolean) as string[];
  const problem=p.error??taken[0];
  const ok=!!text.trim()&&!problem&&!!floor&&!!tcode;
  const add=()=>{if(!ok)return;void run({type:'RMS_ROOMS_ADD',rooms:text,floor,roomType:tcode,key:`rms-add:${text}:${floor}:${tcode}:${s.sequence}`},
    `Added ${rmsRangeLabel(p.numbers)}`,{after:()=>setText('')});};
  return <section className="rms-tool" aria-label="Add rooms">
    <h4><Plus size={15}/> Add rooms</h4>
    <form className="form rms-grid" noValidate onSubmit={e=>{e.preventDefault();add();}}>
      <label>Room numbers<input value={text} onChange={e=>setText(e.target.value)} placeholder="101–110 or 101, 103" autoComplete="off" enterKeyHint="done" aria-invalid={problem?true:undefined}/></label>
      <label>Floor<select value={floor} onChange={e=>setFloor(e.target.value)}>{floors.map(f=><option key={f.code} value={f.code}>{f.name}</option>)}</select></label>
      <label>Room type<select value={tcode} onChange={e=>setTcode(e.target.value)}>{types.map(t=><option key={t.code} value={t.code}>{t.name}</option>)}</select></label>
      <button className="btn primary" disabled={busy||!ok}>Add {p.numbers.length>0&&!problem?rmsRangeLabel(p.numbers):'rooms'} <Check size={15}/></button>
    </form>
    {text.trim()&&problem&&<p className="mst-err" role="alert">{problem}</p>}
    {!floors.length&&<p className="muted">Add a floor coded F&lt;number&gt; first (Floors list).</p>}
  </section>;
}

function Renumber(){
  const {s,run,busy}=useHcc();
  const rooms=s.rooms.filter(r=>r.status!=='retired');
  const [id,setId]=useState('');const [to,setTo]=useState('');
  const t=to.trim().toUpperCase();
  const live=id?rmsLiveBookings(s,id):[];
  const problem=!id||!t?undefined:t===id?'That is the same number.':rmsNumberTaken(s,t)??(live.length?`Room ${id} has bookings ${rmsLockText(live)} — renumber it when none remain.`:undefined);
  const go=()=>{if(!id||!t||problem)return;void run({type:'RMS_ROOM_RENUMBER',id,to:t,key:`rms-renum:${id}:${t}:${s.sequence}`},`Room ${id} is now ${t}`,{after:()=>{setId('');setTo('');}});};
  return <section className="rms-tool" aria-label="Renumber a room">
    <h4><Hash size={15}/> Renumber a room</h4>
    <form className="form rms-grid" noValidate onSubmit={e=>{e.preventDefault();go();}}>
      <label>Room<select value={id} onChange={e=>setId(e.target.value)}><option value="">Choose…</option>{rooms.map(r=><option key={r.id} value={r.id}>{r.id} · {rmsTypeName(s,r.type)}</option>)}</select></label>
      <label>New number<input value={to} onChange={e=>setTo(e.target.value)} maxLength={10} autoComplete="off" enterKeyHint="done"/></label>
      <button className="btn" disabled={busy||!id||!t||!!problem}>Renumber</button>
    </form>
    {problem&&<p className="mst-err" role="alert">{problem}</p>}
    <p className="muted rms-note">Only with no booking from today on; past stays keep the old number.</p>
  </section>;
}

function OutOfOrder(){
  const {s,run,busy}=useHcc();
  const rooms=s.rooms.filter(r=>r.status!=='retired'&&!r.ooo);
  const [id,setId]=useState('');const [from,setFrom]=useState(s.date);const [to,setTo]=useState('');const [reason,setReason]=useState('');
  const clash=id?rmsLiveBookings(s,id).filter(b=>b.arrival<(to||'9999-12-31')&&b.departure>from):[];
  const problem=!id?undefined:from<s.date?'Cannot start before today.':to&&to<=from?'The end must come after the start.':clash.length?`Booked on those nights: ${rmsLockText(clash)}.`:undefined;
  const ok=!!id&&!problem&&reason.trim().length>=3;
  const go=()=>{if(!ok)return;void run({type:'RMS_ROOM_OOO',id,from,...(to?{to}:{}),reason:reason.trim(),key:`rms-ooo:${id}:${from}:${s.sequence}`},`Room ${id} is out of order`,
    {after:()=>{setId('');setReason('');setTo('');}});};
  return <section className="rms-tool" aria-label="Out of order">
    <h4><Ban size={15}/> Set out of order</h4>
    <form className="form rms-grid" noValidate onSubmit={e=>{e.preventDefault();go();}}>
      <label>Room<select value={id} onChange={e=>setId(e.target.value)}><option value="">Choose…</option>{rooms.map(r=><option key={r.id} value={r.id}>{r.id} · {rmsTypeName(s,r.type)}</option>)}</select></label>
      <label>From<input type="date" min={s.date} value={from} onChange={e=>setFrom(e.target.value)}/></label>
      <label>Until (optional)<input type="date" min={from} value={to} onChange={e=>setTo(e.target.value)}/></label>
      <label>Reason<input value={reason} onChange={e=>setReason(e.target.value)} maxLength={120} placeholder="AC compressor replaced" autoComplete="off"/></label>
      <button className="btn" disabled={busy||!ok}>Set out of order</button>
    </form>
    {problem&&<p className="mst-err" role="alert">{problem}</p>}
    <p className="muted rms-note">Out of order holds the room on those nights: not bookable, shown “Blocked” on the boards and calendar, not counted as sellable.</p>
  </section>;
}

function OooList({readOnly,phone}:{readOnly?:boolean;phone?:boolean}){
  const {s,run,busy}=useHcc();
  const list=rmsOooRooms(s);
  if(!list.length)return readOnly?null:<p className="muted rms-none">No room is out of order.</p>;
  return <section className={'rms-tool'+(phone?' rms-phone':'')} aria-label="Rooms out of order"><h4>Out of order now or later ({list.length})</h4>
    <ul className="rms-ooo">{list.map(r=><li key={r.id}><span><strong>Room {r.id}</strong> · {shortDate(r.ooo!.from)} → {r.ooo!.to?shortDate(r.ooo!.to):'until cleared'} · {r.ooo!.reason}</span>
      {!readOnly&&<button type="button" className="btn" disabled={busy} onClick={()=>void run({type:'RMS_ROOM_OOO_CLEAR',id:r.id,key:`rms-ooo-clear:${r.id}:${s.sequence}`},`Room ${r.id} back in service`)}><RotateCcw size={14}/> Back in service</button>}</li>)}</ul>
  </section>;
}
