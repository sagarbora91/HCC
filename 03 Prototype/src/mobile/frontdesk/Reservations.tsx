// M04 Reservations (phone): search + status chips, list cards (guest, room, dates, status, balance); tap → booking sheet.
import {usePaged,ShowMore,PHONE_PAGE} from '../../components/ShowMore';
import React from 'react';
import {Plus} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,totals,nights,resStatus,resHoldLeftLabel} from '../../services/mock';
import {ChipFilter,Fab,ListCard} from '../../app/mobile/components';
import {Empty,shortDate} from '../../components/ui';
import {mfFilterBookings,mfStatuses,mfInitials} from './logic';
import {MfStatus,MfSearch,useMfTop} from './shared';

export function MfReservations(){
  useMfTop();
  const {s,open,booking,search,setSearch,filter,setFilter,permission}=useHcc();
  const status=mfStatuses.includes(filter)?filter:'All';
  const list=mfFilterBookings(s,search,status);const finance=permission('billing');const pg=usePaged(list,PHONE_PAGE);
  const count=(st:string)=>st==='All'?s.bookings.length:s.bookings.filter(b=>resStatus(s,b)===st).length;
  return <div className="mf-page">
    <MfSearch value={search} onChange={setSearch} placeholder="Search guest, phone, room or booking"/>
    <ChipFilter label="Booking status" value={status} onChange={setFilter} options={mfStatuses.filter(x=>x==='All'||count(x)>0||x===status).map(x=>({id:x,label:x,count:count(x)}))}/>
    <p className="mf-count">{list.length} of {s.bookings.length} bookings</p>
    {list.length?pg.shown.map(b=>{const st=resStatus(s,b),n=nights(b.arrival,b.departure);
      return <ListCard key={b.id} title={<span className="mf-lc-guest"><span className="mf-initials" aria-hidden>{mfInitials(b.guest)}</span>{b.guest}</span>}
        sub={`Room ${b.room} · ${shortDate(b.arrival)} – ${shortDate(b.departure)} · ${n} night${n===1?'':'s'}`}
        meta={`${b.id} · ${b.source}${b.status==='Held'?' · '+resHoldLeftLabel(s,b):''}`}
        right={<><MfStatus status={st}/>{finance&&<small className="mf-bal">Bal {money(totals(b).balance)}</small>}</>} onClick={()=>booking(b.id)}/>;})
      :<Empty title="No bookings match" body="Try another status or search."/>}
    <ShowMore p={pg}/>
    {permission('reservations')&&<Fab label="New booking" icon={Plus} onClick={()=>open({type:'new'})}/>}
  </div>;
}
