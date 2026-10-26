import React from 'react';
import {bookingRef} from '../../domain/bookingRef';
import {ArrowUpRight} from 'lucide-react';
import {Booking,money,nights,totals,resStatus,resHoldLeftLabel,plural} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty,shortDate} from '../../components/ui';
const tone=(st:string)=>st==='In house'?'green':st==='Confirmed'?'blue':st==='Held'?'orange':'neutral';
export function BookingTable({list,compact=false}:{list:Booking[];compact?:boolean}){
  const {s,booking}=useHcc();
  return <div className="table-wrap"><table><thead><tr><th>Guest / booking</th><th>Room</th><th>Stay</th><th>Status</th>{!compact&&<th>Advance</th>}{!compact&&<th>Folio balance</th>}<th></th></tr></thead><tbody>
    {list.map(b=>{const st=resStatus(s,b),room=s.rooms.find(r=>r.id===b.room);return <tr key={b.id}>
      <td><button className="guest-link" onClick={()=>booking(b.id)}><span className="avatar">{b.guest.split(' ').map(x=>x[0]).slice(0,2).join('')}</span><span><strong>{b.guest}</strong><small>{bookingRef(b)} · {b.source}{b.otaChannel?' · '+b.otaChannel:''}{b.company?' · '+b.company.name:''}</small></span></button></td>
      <td><span className="room-number">{b.room}</span>{room&&<small>{room.type}</small>}</td>
      <td>{shortDate(b.arrival)} — {shortDate(b.departure)}<small>{plural(nights(b.arrival,b.departure),'night')} · {b.people} guest{b.people===1?'':'s'}{b.children?` + ${b.children} child${b.children===1?'':'ren'}`:''}</small></td>
      <td><Pill tone={tone(st)}>{st}</Pill>{b.status==='Held'&&<small>{resHoldLeftLabel(s,b)}</small>}</td>
      {!compact&&<td className="number">{money(totals(b).paid)}</td>}
      {!compact&&<td className="number">{money(totals(b).balance)}</td>}
      <td><button className="icon-btn" title={'Open '+b.guest} aria-label={'Open '+b.guest} onClick={()=>booking(b.id)}><ArrowUpRight size={18}/></button></td>
    </tr>;})}
  </tbody></table>{!list.length&&<Empty title="Nothing pending" body="Try another filter or create a reservation."/>}</div>;
}
