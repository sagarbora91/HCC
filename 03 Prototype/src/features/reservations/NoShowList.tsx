// F8-QW-NS · Due no-shows: confirmed arrivals of the business date not checked in after fd.noShowTime (QWQ-7). One click marks a booking
// through CANCEL {noShow:true}; the policy charge is shown, never posted (DEC-MA-5b). The system never marks by itself.
import React from 'react';
import {nsDue,nsCutoff,nsPastCutoff,money} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill} from '../../components/ui';

export function NoShowList(){
  const {s,run,busy,booking,permission}=useHcc();
  const rows=nsDue(s),cut=nsCutoff(s),canMark=permission('reservations');
  if(!nsPastCutoff(s))return <p className="muted" data-testid="noshow-wait">Due no-shows appear after {cut}. Nothing is marked automatically.</p>;
  if(!rows.length)return <p className="muted" data-testid="noshow-none">No arrivals are waiting past the {cut} cut-off.</p>;
  return <div data-testid="noshow-list"><p className="muted">Past the {cut} cut-off, still not checked in. Marking a no-show does not post the charge; it is shown for the desk to settle.</p>
    <ul className="f-checks">{rows.map(r=><li key={r.booking.id} className="bad">
      <div><strong>{r.booking.id} · {r.booking.guest}</strong> <Pill tone="orange">Due no-show</Pill>
        <small>Room {r.booking.room} · {r.text}{r.charge?` (${money(r.charge)})`:''}</small>
        {r.blocked&&<small>{r.blocked}</small>}</div>
      <button className="btn small-btn" onClick={()=>booking(r.booking.id)}>Open</button>
      <button className="btn small-btn" disabled={busy||!canMark||!!r.blocked} title={r.blocked} onClick={()=>run({type:'CANCEL',id:r.booking.id,noShow:true,reason:`No-show after ${cut} cut-off`,key:'noshow:'+r.booking.id},'Marked no-show')}>Mark no-show</button></li>)}</ul></div>;
}
