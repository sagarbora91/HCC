import React from 'react';
import {occupant,mstFloorGroups,rmsTypeName} from '../../services/mock';
import {useHcc} from '../../app/context';
import {SearchBox} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,FitNote} from '../../components/fit';
import {netBannerText} from '../admin/InternetPanel';
import {RoomCard} from './RoomCard';
import {MstLink} from '../masters/MasterList';
export function RoomBoard(){
  const {s,act,open,go,search,setSearch,filter,setFilter,roomStatus,permission}=useHcc();
  const q=search.toLowerCase();
  const list=s.rooms.filter(r=>r.status!=='retired'&&(filter==='All'||roomStatus(r)===filter)&&(r.id.includes(search)||r.type.toLowerCase().includes(q)||rmsTypeName(s,r.type).toLowerCase().includes(q)||occupant(s,r.id)?.guest.toLowerCase().includes(q)));
  // FIT (DEC-194/196, contract §6 'rooms' = board): sub-screens = "All floors" (first, default) + one tab per floor (generated from the floor master,
  // so only rooms/all is in SUB_SCREENS). All floors = every floor section in ONE panel, tiles unchanged, more columns (auto-fill 150 px).
  const floors=mstFloorGroups(s);
  const tabs=[{id:'all',label:'All floors'},...floors.map(({floor:f,label,retired})=>({id:'floor-'+f,label:label+(retired?' (retired)':'')}))];
  const [tab,setTab]=useFitTab<string>('rooms',tabs.map(t=>t.id),'all');
  const shown=tab==='all'?floors:floors.filter(({floor:f})=>'floor-'+f===tab);
  const note=<FitNote lines={2} text={<>“Ready” describes the room now. Future availability is rechecked for your booking dates.{permission('reservations')&&<> <button className="text-btn" onClick={()=>go('calendar')}>Open the calendar</button> to see the next two weeks.</>}</>}/>;
  return <FitPage title="Room board" sub={netBannerText(s,'ota')+'A clear picture of every room. Select a room to take action.'} layout="board"
      actions={act('reservations',()=>open({type:'new'}),'+ New reservation','btn primary')}
      toolbar={<><SearchBox value={search} onChange={setSearch} placeholder="Find a room or guest…"/><div className="filters">{['All','Occupied','Ready','Dirty','Cleaning','Inspection','Blocked'].map(x=><button className={filter===x?'active':''} key={x} aria-pressed={filter===x} onClick={()=>setFilter(x)}>{x}</button>)}</div><MstLink spec="floors"/></>}
      tabs={<FitTabs page="rooms" value={tab} onChange={setTab} tabs={tabs.map(t=>({...t,count:t.id==='all'?list.length:list.filter(r=>'floor-'+r.floor===t.id).length}))}/>}>
    <ScrollPanel label={tab==='all'?'Rooms on all floors':'Rooms on '+(tabs.find(t=>t.id===tab)?.label??'this floor')} footer={note}>
      <div className="fit-rooms">{shown.map(({floor:f,label,retired})=><section className="floor-section" key={f}><div className="floor-heading"><h2>{label}{retired?' (retired)':''}</h2><span>{list.filter(r=>r.floor===f).length} rooms</span><hr/></div><div className="room-grid">{list.filter(r=>r.floor===f).map(r=><RoomCard key={r.id} r={r}/>)}</div></section>)}</div>
    </ScrollPanel>
  </FitPage>;
}
