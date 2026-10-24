import React from 'react';
import {BedDouble} from 'lucide-react';
import {Room,occupant,resHoldActive,resHoldLeftLabel} from '../../services/mock';
import {useHcc} from '../../app/context';
import {shortDate} from '../../components/ui';
export function RoomCard({r}:{r:Room}){
  const {s,open,roomStatus}=useHcc();
  // Next reservation: confirmed, or a hold that is still running (expired holds no longer reserve the room).
  const b=occupant(s,r.id),next=s.bookings.filter(b=>b.room===r.id&&(b.status==='Confirmed'||resHoldActive(s,b))).sort((a,c)=>a.arrival.localeCompare(c.arrival))[0],status=roomStatus(r);
  return <button className={'room-card '+status.toLowerCase()} onClick={()=>open({type:'room',id:r.id})}>
    <div className="room-top"><strong>{r.id}</strong><BedDouble size={18}/></div>
    <span className="room-type">{r.type}</span>
    <div className="room-guest">{b?b.guest:r.blocked?'Out of service':r.hk==='Ready'?'Ready for arrival':r.hk==='Dirty'?'Cleaning required':r.hk==='Inspection'?'Inspection pending':'Cleaning in progress'}</div>
    {!b&&next?.status==='Held'&&<div className="res-card-hold">Held · {resHoldLeftLabel(s,next)}</div>}
    <div className="room-foot"><span><i className={'dot '+status.toLowerCase()}/>{status}</span>{b?<span>{shortDate(b.departure)}</span>:next?<span>{next.status==='Held'?'Hold':'Arrival'} {shortDate(next.arrival)}</span>:<span>—</span>}</div>
  </button>;
}
