// Printable in-house list and arrivals list (DEC-081, audit A09-06): the paper copy the front desk prints each morning and works from while the
// server is down. Wide modal 'front-list' (id = 'inhouse' | 'arrivals'), opened from the In-house stays and Arrivals pages, desktop and phone.
// Printed through printDocument() (browser print dialog, or the Android print service in the APK). On a phone the sheet shows cards; the
// printed copy is always the table (zz-print / guests.css), so the paper list is the same on every device.
import React from 'react';
import {Hotel,Printer} from 'lucide-react';
import {printDocument} from '../../app/native';
import {useHcc} from '../../app/context';
import {money,stayListRows,STAY_LIST_TITLE} from '../../services/mock';
import type {StayListKind} from '../../services/mock';
import {Empty,dateLabel,shortDate} from '../../components/ui';

/** Print button for the In-house stays and Arrivals pages (desktop title bar; the phone pages use the same button with `className="btn full mf-more"`). */
export function PrintListButton({kind,className='btn'}:{kind:StayListKind;className?:string}){
  const {open}=useHcc();
  return <button type="button" className={className} onClick={()=>open({type:'front-list',id:kind})}><Printer size={16}/> Print {kind==='inhouse'?'in-house':'arrivals'} list</button>;
}

export function FrontDeskListModal({id}:{id?:string}){
  const {s,uid,closeModal,permission}=useHcc();
  if(id!=='inhouse'&&id!=='arrivals')return <Empty title="This record is unavailable"/>;
  const kind:StayListKind=id,rows=stayListRows(s,uid,kind),finance=permission('billing');
  const title=STAY_LIST_TITLE[kind],arrivals=kind==='arrivals';
  const stay=(r:typeof rows[number])=>`${shortDate(r.arrival)} – ${shortDate(r.departure)}`;
  return <><div className="print-doc invoice fd-list">
    <div className="invoice-top"><Hotel size={30}/><div><h2>{title}</h2><p>{s.settings.hotel||'Hotel server'} · business date {dateLabel(s.date)} · {rows.length} {arrivals?(rows.length===1?'arrival':'arrivals'):(rows.length===1?'stay':'stays')}</p></div></div>
    {!rows.length?<p role="status">{arrivals?'No arrivals are waiting for check-in.':'No guests are in house.'}</p>:<>
      <ul className="fd-cards" aria-label={title}>{rows.map(r=><li key={r.ref+r.room}>
        <span className="room-number">{r.room}</span>
        <span className="fd-card-main"><strong>{r.guest}</strong><small>{r.ref} · {r.people} guest{r.people===1?'':'s'} · {stay(r)}{arrivals?(r.registered?' · registered':' · not registered'):''}</small>
          <small>{r.phone}{finance?` · ${arrivals?'Advance':'Balance'} ${money(arrivals?r.paid:r.balance)}`:''}</small></span></li>)}</ul>
      <div className="table-wrap fd-table"><table><thead><tr><th>Room</th><th>Guest</th><th>Booking</th><th className="number">Guests</th><th>{arrivals?'Stay':'Arrived – departs'}</th>
        {arrivals&&<th>Registered</th>}<th>Phone</th>{finance&&<th className="number">{arrivals?'Advance paid':'Balance due'}</th>}<th>Notes</th></tr></thead><tbody>
        {rows.map(r=><tr key={r.ref+r.room}><td><strong>{r.room}</strong></td><td>{r.guest}</td><td>{r.ref}</td><td className="number">{r.people}</td><td>{stay(r)}</td>
          {arrivals&&<td>{r.registered?'Yes':'No'}</td>}<td>{r.phone}</td>{finance&&<td className="number">{money(arrivals?r.paid:r.balance)}</td>}<td className="fd-notes"></td></tr>)}</tbody></table></div></>}
    <p className="footnote">Printed from the Hotel Control Centre. If the server is down, work from this list: take payments on paper and enter them again when the server is back.</p>
  </div>
  <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print</button></div></>;
}
