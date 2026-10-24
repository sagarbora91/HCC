// In-house stays (SCR-07, REQ-STAY-002/003): list, stay detail with room segments, move / extend / shorten.
import React,{useState} from 'react';
import {ArrowRight,Info,AlertCircle,Printer,MoveRight,CalendarPlus} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Booking,money,nights,totals,guestForBooking,guestView,staySegments,staySegmentLabel,stayInHouse,stayMoveAlternatives,stayDepartureBounds,stayCanExtend,available,occupant,plural} from '../../services/mock';
import {PageTitle,Pill,Empty,shortDate,SearchBox} from '../../components/ui';

/** Stay detail body (shared by the In-house page and the 'stay-detail' modal). */
export function StayDetail({b}:{b:Booking;inModal?:boolean}){
  const {s,uid,open,permission,booking}=useHcc();const g=guestForBooking(s,b),v=g?guestView(s,uid,g):undefined;const t=totals(b);
  const segs=staySegments(b);const left=b.status==='In house'?Math.max(0,nights(s.date,b.departure)):0;
  return <div className="gs-stay">
    <div className="gs-stay-head"><div><div className="eyebrow">{b.id} · ROOM {b.room}</div><h2>{b.guest}</h2>
      <div className="inline"><Pill tone={b.status==='In house'?'green':'neutral'}>{b.status}</Pill><span className="muted">{shortDate(b.arrival)} — {shortDate(b.departure)} · {plural(nights(b.arrival,b.departure),'night')}{b.status==='In house'?` · ${left} night${left===1?'':'s'} left`:''}</span></div></div></div>
    <h3 className="gs-sub">Room segments</h3>
    <p className="gs-seg-label">{staySegmentLabel(b)||'—'}</p>
    <ol className="gs-timeline">{segs.map((x,i)=><li key={i} className={x.to?'':'open'}><span className="room-number">{x.room}</span><span>{shortDate(x.from)} — {x.to?shortDate(x.to):'now (to '+shortDate(b.departure)+')'}</span><small>{x.to?(nights(x.from,x.to)?`${nights(x.from,x.to)} night${nights(x.from,x.to)>1?'s':''}`:'same-day move'):'current room'}</small></li>)}</ol>
    <h3 className="gs-sub">Occupants ({b.people})</h3>
    <ul className="gs-party-list"><li><strong>{v?.name||b.guest}</strong><span>Primary guest{v?' · '+v.phone:''}</span></li>{g?.party.map((p,i)=><li key={i}><strong>{p.name}</strong><span>{p.ageBand}{p.relation?' · '+p.relation:''}</span></li>)}</ul>
    {!g&&<p className="muted">No guest profile linked.</p>}
    <h3 className="gs-sub">Folio summary</h3>
    <div className="folio-totals"><div><span>Charges + tax</span><strong>{money(t.total)}</strong></div><div><span>Net paid</span><strong>{money(t.paid)}</strong></div><div><span>Deposit held</span><strong>{money(t.deposit)}</strong></div><div className={t.balance>0?'balance-due':'balance-clear'}><span>{t.balance<0?'Credit balance':'Balance due'}</span><strong>{money(t.balance)}</strong></div></div>
    <p className="muted">Agreed tariff {money(b.rate)}/night · {b.lines.filter(l=>l.kind==='room').length} room night{b.lines.filter(l=>l.kind==='room').length===1?'':'s'} posted</p>
    {b.status==='In house'&&<div className="inline gs-actions">
      {permission('reservations')?<><button className="btn" onClick={()=>open({type:'stay-move',id:b.id,room:'move'})}><MoveRight size={15}/>Move room</button><button className="btn" onClick={()=>open({type:'stay-move',id:b.id,room:'dates'})}><CalendarPlus size={15}/>Extend / shorten</button></>:<span className="muted">Moves need reservations permission.</span>}
      {permission('billing')&&<button className="btn" onClick={()=>open({type:'charge',id:b.id})}>Add charge</button>}
      {permission('checkout')&&<button className="btn primary" onClick={()=>open({type:'checkout',id:b.id})}>Checkout</button>}
    </div>}
    <div className="inline gs-actions">{(permission('billing')||permission('reservations'))&&<button className="text-btn" onClick={()=>booking(b.id)}>Open folio <ArrowRight size={13}/></button>}
      {g&&<button className="text-btn" onClick={()=>open({type:'guest-profile',id:g.id})}>Guest profile</button>}
      {b.registered&&<button className="text-btn" onClick={()=>open({type:'guest-regcard',id:b.id})}><Printer size={13}/>Registration card</button>}</div>
  </div>;
}

/** Modal 'stay-detail': id = booking ID. */
export function StayDetailModal({id}:{id?:string}){
  const {s,closeModal}=useHcc();const b=s.bookings.find(b=>b.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  return <><StayDetail b={b} inModal/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
}

/** Page 'inhouse': list of in-house stays with detail panel. */
export function InHousePage(){
  const {s,search,setSearch}=useHcc();const all=stayInHouse(s);
  const list=all.filter(b=>`${b.guest} ${b.id} ${b.room} ${staySegments(b).map(x=>x.room).join(' ')}`.toLowerCase().includes(search.toLowerCase()));
  const [sel,setSel]=useState('');const b=list.find(x=>x.id===sel)||list[0];
  return <><PageTitle title="In-house stays" sub={`${all.length} stay${all.length===1?'':'s'} in house · business date ${shortDate(s.date)}`}/>
    <div className="toolbar"><SearchBox value={search} onChange={setSearch} placeholder="Search guest, room or booking…"/></div>
    {!all.length?<section className="card"><Empty title="No guests in house" body="Check in an arrival from Arrivals & check-in."/></section>:
    <div className="gs-split">
      <section className="card"><div className="section-title"><h2>Stays</h2><span className="muted">{list.length} shown</span></div>
        {list.length?<ul className="gs-list">{list.map(x=>{const due=x.departure<=s.date;return <li key={x.id}><button className={'gs-list-item '+(b?.id===x.id?'active':'')} aria-pressed={b?.id===x.id} onClick={()=>setSel(x.id)}>
          <span className="room-number">{x.room}</span><span className="gs-list-main"><strong>{x.guest}</strong><small>{x.id} · {staySegmentLabel(x)}</small></span>
          <span className="gs-list-tags">{due?<Pill tone="orange">Departure due</Pill>:<Pill tone="blue">to {shortDate(x.departure)}</Pill>}<span className="number">Balance {money(totals(x).balance)}</span></span></button></li>;})}</ul>
        :<Empty title="No matching stays" body="Try another guest, room or booking."/>}</section>
      {b&&<section className="card"><div className="padded"><StayDetail b={b}/></div></section>}
    </div>}
    <p className="footnote">Moves reserve the new room before the old one is released; the old room becomes dirty with one housekeeping task. All records are fictional.</p></>;
}

/** Modal 'stay-move': id = booking ID; room = 'move' (change room) or 'dates' (extend/shorten). In-house stays only. */
export function StayMoveModal({id,room:mode}:{id?:string;room?:string}){
  const {s,run,open,modal,busy,error,permission}=useHcc();const b=s.bookings.find(b=>b.id===id);
  const [tab,setTab]=useState(mode==='dates'?'dates':'move');
  const [dep,setDep]=useState(b?.departure||'');const [target,setTarget]=useState('');const [failed,setFailed]=useState(false);
  if(!b)return <Empty title="This record is unavailable"/>;
  if(b.status!=='In house')return <><div className="modal-heading"><h2>Not in house</h2><p>{b.guest} · {b.id} is {b.status}. Use Move / extend in the booking dialog for reservations.</p></div>
    <div className="modal-actions"><button className="btn" onClick={()=>open({type:'move',id:b.id})}>Open reservation move</button></div></>;
  if(!permission('reservations'))return <Empty title="Not permitted" body="Room moves and date changes need the reservations permission (PERM-STY-002)."/>;
  const bounds=stayDepartureBounds(s,b);const validDep=dep>=bounds.min&&dep<=bounds.max;
  const alts=validDep?stayMoveAlternatives(s,b,dep):[];
  const roomTo=tab==='move'?(alts.some(r=>r.id===target)?target:alts[0]?.id||''):b.room;
  const curType=s.rooms.find(r=>r.id===b.room)?.type;
  const fits=validDep&&(tab==='move'?!!roomTo:stayCanExtend(s,b,dep));
  const postedBeyond=dep<b.departure&&b.lines.some(l=>l.kind==='room'&&l.date>=dep);
  const submit=(room:string,departure:string)=>{setFailed(false);run({type:'MOVE',id:b.id,room,departure,key:`${modal!.key}:${room}:${departure}`},room!==b.room?`Moved to room ${room}. Old room ${b.room} is dirty with one housekeeping task.`:`Departure changed to ${shortDate(departure)}. Availability revalidated.`,{after:()=>open({type:'stay-detail',id:b.id})}).then(ok=>{if(!ok)setFailed(true);});};
  const conflictAlts=validDep&&tab==='dates'&&!fits?stayMoveAlternatives(s,b,dep):[];
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · IN HOUSE</div><h2>{tab==='move'?'Move room':'Extend or shorten stay'}</h2><p>{b.guest} · room {b.room} ({curType}) · departs {shortDate(b.departure)} · agreed tariff {money(b.rate)}/night is kept.</p></div>
    <div className="tabs gs-tabs" role="tablist">{[['move','Move room'],['dates','Extend / shorten']].map(([k,l])=><button key={k} role="tab" aria-selected={tab===k} className={tab===k?'active':''} onClick={()=>{setTab(k);setFailed(false);}}>{l}</button>)}</div>
    <div className="form">
      <label>Departure<input type="date" value={dep} min={bounds.min} max={bounds.max} onChange={e=>{setDep(e.target.value);setFailed(false);}} aria-invalid={!validDep}/>
        <small>Earliest {shortDate(bounds.min)} (business date + 1) · latest {shortDate(bounds.max)}.</small>
        {!validDep&&<span className="gs-field-error">{dep<bounds.min?`Shortening cannot go before ${shortDate(bounds.min)}.`:'Maximum 30 nights from today.'}</span>}
        {postedBeyond&&<span className="gs-field-error">Room nights are already posted on or after this date; shortening will be refused.</span>}</label>
      {tab==='move'&&<fieldset className="gs-party"><legend>Target room — vacant, ready and free {shortDate(s.date)} — {validDep?shortDate(dep):'…'}</legend>
        {alts.length?<ul className="gs-rooms">{alts.map(r=><li key={r.id}><label className="gs-radio"><input type="radio" name="target" checked={roomTo===r.id} onChange={()=>setTarget(r.id)}/><span className="room-number">{r.id}</span><span>{r.type}{r.type===curType?' · same type':''} · floor {r.floor}<small>Ready · vacant · unblocked</small></span></label></li>)}</ul>
          :<Empty title="No room is free for these dates" body="Try a shorter stay, or ask housekeeping to ready a room."/>}</fieldset>}
      {tab==='dates'&&validDep&&<div className={fits?'gs-ok':'gs-deny'} role={fits?undefined:'alert'}>{fits?<><Info size={18}/><div><strong>Room {b.room} is free until {shortDate(dep)}.</strong><p>{dep>b.departure?`${nights(b.departure,dep)} extra night(s) at the agreed tariff.`:dep<b.departure?`${nights(dep,b.departure)} night(s) fewer.`:'No change.'}</p></div></>
        :<><AlertCircle size={18}/><div><strong>Room {b.room} is not free until {shortDate(dep)}.</strong><p>Choose an alternative room free for {shortDate(s.date)} — {shortDate(dep)}, or a different date.</p></div></>}</div>}
      {(conflictAlts.length>0||(failed&&/Not available/.test(error)))&&<div className="gs-alts"><h3 className="gs-sub">Alternative rooms free {shortDate(s.date)} — {shortDate(dep)}</h3>
        {(conflictAlts.length?conflictAlts:stayMoveAlternatives(s,b,dep)).length?<ul className="gs-rooms">{(conflictAlts.length?conflictAlts:stayMoveAlternatives(s,b,dep)).map(r=><li key={r.id}><span className="room-number">{r.id}</span><span>{r.type} · floor {r.floor}<small>{available(s,r.id,s.date,dep,b.id)&&!occupant(s,r.id)?'Free for the whole stay':''}</small></span><button className="btn small-btn" disabled={busy} onClick={()=>submit(r.id,dep)}>Move to {r.id} and change dates</button></li>)}</ul>
          :<p className="muted">No alternative room is free for the whole interval.</p>}</div>}
      <div className="info-note"><Info size={16}/>{tab==='move'?'The new room is reserved before the old one is released; the old room becomes dirty with one housekeeping task.':'Availability is rechecked for the whole new interval.'} If anything fails, the stay is left exactly as it was.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>open({type:'stay-detail',id:b.id})}>Back</button>
        <button type="button" className="btn primary" disabled={busy||(tab==='move'?!fits:!validDep||dep===b.departure)} onClick={()=>submit(roomTo,dep)}>{tab==='move'?`Move to ${roomTo||'…'}`:'Save new departure'}</button></div>
    </div></>;
}
