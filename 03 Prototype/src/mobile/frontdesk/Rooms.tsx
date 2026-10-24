// M02 Room board (phone): floor chips + status chips, 2-column tiles; tap → 'mf-room' sheet with book / check in / block / repair.
import React,{useState} from 'react';
import {Plus,BedDouble,CheckCircle2,Brush,SearchCheck,Lock,Sparkles,Wrench,Unlock,DoorOpen,CalendarPlus,ArrowRight} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,occupant,hkGuests,resHoldLeftLabel,holdTonight} from '../../services/mock';
import {ChipFilter,Fab,ListCard} from '../../app/mobile/components';
import {Pill,Empty,shortDate} from '../../components/ui';
import {mfRoomList,mfInitials,mfArrivalFor,mfNextFor} from './logic';
import {MfRows,useMfPages,useMfTop} from './shared';

const STATUSES=['All','Occupied','Ready','Dirty','Cleaning','Inspection','Blocked'];
const ICON:Record<string,React.ComponentType<{size?:number}>>={Occupied:BedDouble,Ready:CheckCircle2,Dirty:Brush,Cleaning:Sparkles,Inspection:SearchCheck,Blocked:Lock};

export function MfRooms(){
  useMfTop();
  const {s,open,filter,setFilter,roomStatus,permission}=useHcc();
  const [floor,setFloor]=useState(0);
  const status=STATUSES.includes(filter)?filter:'All';
  const floors=[...new Set(s.rooms.map(r=>r.floor))].sort();
  const list=mfRoomList(s,floor,status,roomStatus);
  const count=(st:string)=>s.rooms.filter(r=>(!floor||r.floor===floor)&&(st==='All'||roomStatus(r)===st)).length;
  return <div className="mf-page">
    <ChipFilter label="Floor" value={String(floor)} onChange={v=>setFloor(Number(v))} options={[{id:'0',label:'All floors'},...floors.map(f=>({id:String(f),label:'Floor '+f}))]}/>
    <ChipFilter label="Room status" value={status} onChange={setFilter} options={STATUSES.map(x=>({id:x,label:x,count:count(x)}))}/>
    {list.length?<div className="mf-tiles">{list.map(r=>{const st=roomStatus(r),b=occupant(s,r.id),next=mfNextFor(s,r.id),Icon=ICON[st]||BedDouble;
      return <button key={r.id} type="button" className={'mf-tile mf-st-'+st.toLowerCase()} onClick={()=>open({type:'mf-room',id:r.id})} aria-label={`Room ${r.id}, ${r.type}, ${st}${b?', '+b.guest:''}`}>
        <span className="mf-tile-top"><strong>{r.id}</strong>{b?<span className="mf-initials" aria-hidden>{mfInitials(b.guest)}</span>:next&&next.arrival===s.date?<span className="mf-tile-arr">{next.status==='Held'?'Held':'Arr.'}</span>:null}</span>
        <span className="mf-tile-type">{r.type}</span>
        <span className="mf-tile-st"><Icon size={16}/>{st}</span>
      </button>;})}</div>
      :<Empty title="No rooms match" body="Try another floor or status."/>}
    {permission('reservations')&&<Fab label="New booking" icon={Plus} onClick={()=>open({type:'new'})}/>}
  </div>;
}

/** Modal 'mf-room' (phone room sheet): id = room ID. Money (tariff, balance) only with billing. */
export function MfRoomSheet({id}:{id?:string}){
  const {s,open,run,busy,permission,booking,go,roomStatus}=useHcc();const pages=useMfPages();
  const r=s.rooms.find(r=>r.id===id);
  if(!r)return <Empty title="This room is unavailable"/>;
  const st=roomStatus(r),b=occupant(s,r.id),arr=mfArrivalFor(s,r.id),next=mfNextFor(s,r.id),g=hkGuests(s,r.id),finance=permission('billing');
  const openRepair=s.tasks.find(t=>t.room===r.id&&t.kind==='repair'&&t.status!=='Done');
  const canReport=permission('housekeeping')||permission('reservations')||permission('inspect');
  return <><div className="modal-heading"><div className="eyebrow">FLOOR {r.floor} · {r.type.toUpperCase()}</div><h2>Room {r.id}</h2>
      <div className="mf-inline"><Pill tone={st==='Ready'?'green':st==='Occupied'?'blue':'orange'}>{st}</Pill>{r.blocked&&<Pill tone="orange"><Lock size={11}/> Out of service</Pill>}{holdTonight(s,r)&&<Pill tone="orange"><Lock size={11}/> {holdTonight(s,r)!.label}</Pill>}</div>
      {finance&&<p>{money(r.rate)} per night · synthetic tax additional</p>}</div>
    <MfRows rows={[['Housekeeping',r.hk],['Current guest',b?`${b.guest} · to ${shortDate(b.departure)}`:g.current?g.current.guest:'None'],
      ['Next arrival',next?`${next.guest} · ${shortDate(next.arrival)}${next.status==='Held'?' · held, '+resHoldLeftLabel(s,next):''}`:'None scheduled'],
      ...(r.blocked?[['Block reason',r.blockReason||'Not recorded']] as [string,string][]:[])]}/>
    {openRepair&&<div className="info-note"><Wrench size={16}/>Repair open: “{openRepair.note}” · {openRepair.status}</div>}
    {finance&&b&&<ListCard title={b.guest} sub={`${b.id} · open folio`} onClick={()=>booking(b.id)}/>}
    {finance&&arr&&!b&&<ListCard title={arr.guest} sub={`${arr.id} · arriving ${shortDate(arr.arrival)}`} onClick={()=>booking(arr.id)}/>}
    {permission('reservations')&&pages.includes('calendar')&&<button className="text-btn mf-link" onClick={()=>go('calendar')}>See the next 7 days in Calendar <ArrowRight size={14}/></button>}
    <div className="modal-actions">
      {canReport&&!openRepair&&<button className="btn" disabled={busy} onClick={()=>open({type:'room-repair',id:r.id})}><Wrench size={16}/>Report repair</button>}
      {permission('inspect')&&(r.blocked
        ?<button className="btn" disabled={busy} onClick={()=>run({type:'UNBLOCK',id:r.id,key:'unblock:'+r.id+':'+s.sequence},'Room unblocked')}><Unlock size={16}/>Unblock</button>
        :<button className="btn" disabled={busy} onClick={()=>open({type:'room-block',id:r.id})}><Lock size={16}/>Block</button>)}
      {arr&&permission('checkin')&&<button className="btn primary" disabled={busy} onClick={()=>open({type:'guest-checkin',id:arr.id})}><DoorOpen size={16}/>Check in {arr.guest.split(' ')[0]}</button>}
      {!arr&&!b&&!r.blocked&&permission('reservations')&&<button className="btn primary" disabled={busy} onClick={()=>open({type:'new',room:r.id})}><CalendarPlus size={16}/>Book this room</button>}
    </div></>;
}
