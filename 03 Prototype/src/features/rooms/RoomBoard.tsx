import React from 'react';
import {Info} from 'lucide-react';
import {occupant} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,SearchBox} from '../../components/ui';
import {RoomCard} from './RoomCard';
export function RoomBoard(){
  const {s,act,open,go,search,setSearch,filter,setFilter,roomStatus,permission}=useHcc();
  const q=search.toLowerCase();
  const list=s.rooms.filter(r=>(filter==='All'||roomStatus(r)===filter)&&(r.id.includes(search)||r.type.toLowerCase().includes(q)||occupant(s,r.id)?.guest.toLowerCase().includes(q)));
  return <><PageTitle title="Room board" sub="A clear picture of every room. Select a room to take action." action={act('reservations',()=>open({type:'new'}),'+ New reservation','btn primary')}/>
    <div className="toolbar"><SearchBox value={search} onChange={setSearch} placeholder="Find a room or guest…"/><div className="filters">{['All','Occupied','Ready','Dirty','Cleaning','Inspection','Blocked'].map(x=><button className={filter===x?'active':''} key={x} aria-pressed={filter===x} onClick={()=>setFilter(x)}>{x}</button>)}</div></div>
    {[1,2,3].map(f=><section className="floor-section" key={f}><div className="floor-heading"><h2>Floor {String(f).padStart(2,'0')}</h2><span>{list.filter(r=>r.floor===f).length} rooms</span><hr/></div><div className="room-grid">{list.filter(r=>r.floor===f).map(r=><RoomCard key={r.id} r={r}/>)}</div></section>)}
    <div className="info-note"><Info size={16}/><span>“Ready” describes the room now. Future availability is rechecked for your booking dates.{permission('reservations')&&<> <button className="text-btn" onClick={()=>go('calendar')}>Open the calendar</button> to see the next two weeks.</>}</span></div></>;
}
