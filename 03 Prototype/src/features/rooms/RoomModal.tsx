import React from 'react';
import {ArrowUpRight,Lock,Wrench,Unlock} from 'lucide-react';
import {money,occupant,hkGuests,holdTonight} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,shortDate} from '../../components/ui';
// Room details (stream D). Financial data (tariff, folio links) only for billing holders; HK roles see guest names/dates only.
export function RoomModal({id}:{id:string}){
  const {s,permission,booking,act,run,open,busy}=useHcc();
  const r=s.rooms.find(r=>r.id===id)!,b=occupant(s,r.id),future=s.bookings.filter(b=>b.room===r.id&&b.status==='Confirmed');
  const g=hkGuests(s,r.id),finance=permission('billing');
  const canReport=permission('housekeeping')||permission('reservations')||permission('inspect');
  const openRepair=s.tasks.find(t=>t.room===r.id&&t.kind==='repair'&&t.status!=='Done');
  const range=r.blockFrom||r.blockTo?`${r.blockFrom?shortDate(r.blockFrom):'Now'} → ${r.blockTo?shortDate(r.blockTo)+' (available from this date)':'open-ended'}`:'Until unblocked';
  return <><div className="modal-heading"><div className="eyebrow">FLOOR {r.floor} · {r.type.toUpperCase()}</div><h2>Room {r.id}</h2>{finance&&<p>{money(r.rate)} per night · GST additional</p>}</div>
    <div className="room-details"><div><span>Occupancy</span><Pill tone={b?'blue':'green'}>{b?'Occupied':'Vacant'}</Pill></div><div><span>Housekeeping</span><Pill>{r.hk}</Pill></div><div><span>Inventory block</span><Pill tone={r.blocked?'orange':'green'}>{r.blocked?<><Lock size={11}/> Out of service</>:'None'}</Pill></div></div>
    {r.blocked&&<div className="summary-list hk-block-info" aria-label="Block details"><div><span>Block reason</span><strong>{r.blockReason||'Not recorded (legacy block)'}</strong></div><div><span>Dates</span><strong>{range}</strong></div></div>}
    {holdTonight(s,r)&&<div className="info-note"><Lock size={16}/>{holdTonight(s,r)!.label} — held for the group's rooming list; not bookable from the room board.</div>}
    {openRepair&&<div className="info-note"><Wrench size={16}/>Repair open: “{openRepair.note}” · {openRepair.status} · {s.users.find(u=>u.id===openRepair.assignee)?.name.split(' · ')[0]}</div>}
    {finance?<>
      {b&&<button className="guest-preview" onClick={()=>booking(b.id)}><span className="avatar">{b.guest[0]}</span><span><strong>{b.guest}</strong><small>{shortDate(b.arrival)} — {shortDate(b.departure)}</small></span><ArrowUpRight size={18}/></button>}
      {future.length>0&&permission('reservations')&&<div className="padded"><h3>Upcoming reservations</h3>{future.map(b=><button className="guest-preview" key={b.id} onClick={()=>booking(b.id)}>{b.guest} · {shortDate(b.arrival)} <ArrowUpRight size={15}/></button>)}</div>}
    </>:<div className="summary-list" aria-label="Guests">
      <div><span>Current guest</span><strong>{g.current?`${g.current.guest} · ${g.current.people} guest(s) · departs ${shortDate(g.current.departure)}`:'None'}</strong></div>
      <div><span>Next arrival</span><strong>{g.next?`${g.next.guest} · ${shortDate(g.next.arrival)}${g.next.arrival===s.date?' (today)':''}`:'None scheduled'}</strong></div>
    </div>}
    <div className="modal-actions">
      {canReport&&!openRepair&&<button className="btn" disabled={busy} onClick={()=>open({type:'room-repair',id:r.id})}><Wrench size={15}/> Report repair</button>}
      {r.blocked?<>
        {act('inspect',()=>open({type:'room-block',id:r.id}),'Change block')}
        {permission('inspect')&&<button className="btn" disabled={busy} onClick={()=>run({type:'UNBLOCK',id:r.id,key:'unblock:'+r.id+':'+s.sequence},'Room unblocked')}><Unlock size={15}/> Unblock room</button>}
      </>:act('inspect',()=>open({type:'room-block',id:r.id}),'Block for repair')}
      {!b&&!r.blocked&&act('reservations',()=>open({type:'new',room:r.id}),'Reserve this room','btn primary')}
    </div></>;
}
