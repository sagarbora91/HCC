// Guest profile: read-only (masked) card, edit modal, registration card print, and the Guests page (SCR-05).
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Printer,Hotel,ShieldCheck,UserPlus,Lock} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Guest,Booking,guestView,guestAccess,guestById,guestStays,guestSearch,guestDuplicates,guestComplete,totals,money,nights,staySegmentLabel,plural} from '../../services/mock';
import {PageTitle,SearchBox,Pill,Empty,shortDate,dateLabel} from '../../components/ui';
import {GuestForm,emptyDraft,draftOf,hashOf} from './GuestForm';
import {GuestIdValue} from './GuestId';

export const statusTone=(st:string)=>st==='In house'?'green':st==='Confirmed'?'blue':st==='Departed'?'neutral':'orange';
export function VerifiedPill({g}:{g:Guest}){return g.verified?<Pill tone="green">✓ Verified</Pill>:<Pill tone="orange">Not verified</Pill>;}

/** Masked, read-only profile details. */
export function GuestDetails({g}:{g:Guest}){
  const {s,uid}=useHcc();const v=guestView(s,uid,g);
  const row=(k:string,val:React.ReactNode)=><div><span>{k}</span><strong>{val||'—'}</strong></div>;
  return <>
    {v.masked&&<div className="info-note"><Lock size={16}/>ID details are restricted and contact details masked for your role (intended PERM-GST-003 guest.idDetails.view).</div>}
    <div className="summary-list gs-details">
      {row('Phone',v.phone)}{row('Email',v.email)}{row('Address',v.address)}{row('Nationality',v.nationality)}
      {row('ID',<GuestIdValue g={g}/>)}
      {row('Company',v.company?`${v.company}${v.gstin?' · GSTIN '+v.gstin:''}`:'')}
      {row('Preferences',v.preferences)}{row('Notes',v.notes)}
    </div>
    <h3 className="gs-sub">Accompanying guests</h3>
    {g.party.length?<ul className="gs-party-list">{g.party.map((p,i)=><li key={i}><strong>{p.name}</strong><span>{p.ageBand}{p.relation?' · '+p.relation:''}</span></li>)}</ul>:<p className="muted">No accompanying guests recorded.</p>}
  </>;
}

/** Stay history from linked bookings (REQ-GUEST-007). Folio totals only with billing permission. */
export function StayHistory({g}:{g:Guest}){
  const {s,permission,booking,open}=useHcc();const stays=guestStays(s,g.id);
  if(!stays.length)return <p className="muted padded">No stays linked to this profile yet.</p>;
  return <div className="table-wrap"><table><thead><tr><th>Booking</th><th>Room(s)</th><th>Stay</th><th>Status</th>{permission('billing')&&<th>Folio total</th>}<th></th></tr></thead><tbody>
    {stays.map(b=><tr key={b.id}><td><button className="guest-link" onClick={()=>booking(b.id)}><strong>{b.id}</strong></button><small>{b.source}</small></td>
      <td>{staySegmentLabel(b)||b.room}</td><td>{shortDate(b.arrival)} — {shortDate(b.departure)}<small>{plural(nights(b.arrival,b.departure),'night')}</small></td>
      <td><Pill tone={statusTone(b.status)}>{b.status}</Pill></td>{permission('billing')&&<td className="number">{money(totals(b).total)}</td>}
      <td>{b.registered&&<button className="text-btn" onClick={()=>open({type:'guest-regcard',id:b.id})}><Printer size={13}/>Card</button>}</td></tr>)}
  </tbody></table></div>;
}

/** Modal 'guest-profile': id = guest ID, or 'new'; room = booking ID to link the profile to (optional). */
export function GuestProfileModal({id,room}:{id?:string;room?:string}){
  const {s,uid,run,modal,open,closeModal,booking}=useHcc();
  const g=id&&id!=='new'?guestById(s,id):undefined;const linkTo=room?s.bookings.find(b=>b.id===room):undefined;
  const access=guestAccess(s,uid);const [editing,setEditing]=useState(!g);
  if(id&&id!=='new'&&!g)return <Empty title="This guest profile is unavailable"/>;
  const back=linkTo?<button type="button" className="btn" onClick={()=>booking(linkTo.id)}>Back to booking</button>:<button type="button" className="btn" onClick={closeModal}>Close</button>;
  if(g&&!editing)return <><div className="modal-heading"><div className="eyebrow">GUEST PROFILE · {g.id}</div><h2>{g.name}</h2><div className="inline"><VerifiedPill g={g}/>{g.origin==='auto'&&<Pill tone="orange">Minimal demo profile</Pill>}</div></div>
    <GuestDetails g={g}/>
    <h3 className="gs-sub">Stay history</h3><StayHistory g={g}/>
    <div className="modal-actions">{back}{access.edit?<button className="btn primary" onClick={()=>setEditing(true)}>Edit profile</button>:<span className="muted">Editing needs check-in permission (PERM-GST-002).</span>}</div></>;
  if(!access.edit)return <><div className="modal-heading"><h2>Guest profile</h2></div><Empty title="Not permitted" body="Creating or editing guest profiles needs the check-in permission."/><div className="modal-actions">{back}</div></>;
  const initial=g?draftOf(g):emptyDraft(linkTo?{name:linkTo.guest.replace(/\s*\(fictional\)\s*/,''),email:linkTo.email}:{});
  const save=(d:typeof initial,verified:boolean)=>run({type:'GUEST_SAVE',id:g?.id,version:g?.version,guest:d,verified,booking:linkTo?.id,key:modal!.key+':'+hashOf([d,verified])},
    g?'Guest profile updated':'Guest profile created',{after:()=>{if(linkTo)booking(linkTo.id);else if(g)setEditing(false);else closeModal();}});
  return <><div className="modal-heading"><div className="eyebrow">GUEST PROFILE{g?' · '+g.id:''}</div><h2>{g?'Edit '+g.name:'New guest profile'}</h2><p>{linkTo?`Will be linked to ${linkTo.id} · room ${linkTo.room}.`:'Fictional demonstration profile.'}</p></div>
    <GuestForm initial={initial} guestId={g?.id} registration={false} submitLabel={g?'Save profile':'Create profile'} onSubmit={d=>save(d,!!g?.verified&&guestComplete(d))}
      onUseExisting={linkTo?(x=>run({type:'GUEST_SAVE',id:x.id,version:x.version,guest:draftOf(x),verified:x.verified,booking:linkTo.id,key:modal!.key+':use:'+x.id},`Linked ${x.id} to ${linkTo.id}`,{after:()=>booking(linkTo.id)})):undefined}
      secondary={g?<button type="button" className="btn" onClick={()=>setEditing(false)}>Cancel</button>:back}/></>;
}

/** Modal 'guest-regcard': id = booking ID. Printable registration card with demonstration watermark (REQ-GUEST-005, AC-034). */
export function RegistrationCard({id}:{id?:string}){
  const {s,uid,booking}=useHcc();const b=s.bookings.find(b=>b.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  const g=guestById(s,b.guestId),v=g?guestView(s,uid,g):undefined;
  return <><div className="invoice gs-regcard" data-watermark="Demonstration — fictional">
    <div className="invoice-top"><Hotel size={34}/><div><h2>{s.settings.hotel}</h2><p>Guest registration card · Fictional property</p></div></div>
    <div className="invoice-stamp">DEMONSTRATION — FICTIONAL · NOT A STATUTORY REGISTRATION RECORD</div>
    <div className="invoice-meta"><div><small>GUEST</small><h3>{v?.name||b.guest}</h3><p>{v?.address||'—'}</p><p>{v?.phone||'—'} · {v?.email||'—'}</p></div>
      <div><small>REGISTRATION</small><h3>DEMO-REG-{b.id}</h3><p>Room {b.room} · {b.people} guest{b.people>1?'s':''}</p><p>{dateLabel(b.arrival)} — {dateLabel(b.departure)}</p></div></div>
    <div className="summary-list"><div><span>Nationality</span><strong>{v?.nationality||'—'}</strong></div>
      <div><span>ID document (masked)</span><strong>{!v?'—':v.masked?'Restricted':`${v.idType} · ${v.idMasked}`}</strong></div>
      <div><span>Company / GSTIN</span><strong>{v?.company?`${v.company}${v.gstin?' · '+v.gstin:''}`:'—'}</strong></div>
      <div><span>Agreed nightly tariff</span><strong>{money(b.rate)} + synthetic tax</strong></div>
      <div><span>Room history</span><strong>{staySegmentLabel(b)||b.room}</strong></div>
      <div><span>Booking source</span><strong>{b.source}</strong></div></div>
    <h3 className="gs-sub">Accompanying guests</h3>
    {g?.party.length?<table><thead><tr><th>Name</th><th>Age band</th><th>Relation</th></tr></thead><tbody>{g.party.map((p,i)=><tr key={i}><td>{p.name}</td><td>{p.ageBand}</td><td>{p.relation||'—'}</td></tr>)}</tbody></table>:<p className="muted">None recorded.</p>}
    <div className="gs-sign"><div><span/>Guest signature</div><div><span/>Front desk</div></div>
    <p className="footnote">All names, contact details and ID references are fictional. ID numbers are masked on this card; no ID images are stored. Printed from the HCC interactive prototype.</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={()=>booking(b.id)}>Back to booking</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}

/** Page 'guests' (SCR-05): search by name/phone/booking, profile detail, duplicates, history. */
export function GuestsPage(){
  const {s,uid,open,search,setSearch}=useHcc();const access=guestAccess(s,uid);
  const list=guestSearch(s,search,uid).sort((a,b)=>a.name.localeCompare(b.name));
  const [sel,setSel]=useState<string>('');const g=guestById(s,sel)||list[0];
  return <><PageTitle title="Guests" sub="Fictional guest profiles, accompanying guests and stay history." action={access.edit?<button className="btn primary" onClick={()=>open({type:'guest-profile',id:'new'})}><UserPlus size={16}/>New guest</button>:undefined}/>
    <div className="toolbar"><SearchBox value={search} onChange={setSearch} placeholder={access.contact?'Search name, phone or booking…':'Search name or booking…'}/>{!access.idDetails&&<span className="muted"><Lock size={12}/> ID and contact fields are masked for your role</span>}</div>
    {!(s.guests||[]).length?<section className="card"><Empty title="No guest profiles yet" body="Profiles are created at registration or with New guest."/></section>:
    <div className="gs-split">
      <section className="card"><div className="section-title"><h2>Profiles</h2><span className="muted">{list.length} shown</span></div>
        {list.length?<ul className="gs-list">{list.map(x=>{const v=guestView(s,uid,x);const dup=guestDuplicates(s,x).length>0;const stays=guestStays(s,x.id);
          return <li key={x.id}><button className={'gs-list-item '+(g?.id===x.id?'active':'')} aria-pressed={g?.id===x.id} onClick={()=>setSel(x.id)}>
            <span className="avatar">{x.name.split(' ').map(p=>p[0]).slice(0,2).join('')}</span>
            <span className="gs-list-main"><strong>{x.name}</strong><small>{x.id} · {v.phone} · {stays.length} stay{stays.length===1?'':'s'}</small></span>
            <span className="gs-list-tags">{x.verified?<Pill tone="green">Verified</Pill>:<Pill tone="orange">Unverified</Pill>}{dup&&<Pill tone="orange">Possible duplicate</Pill>}</span></button></li>;})}</ul>
          :<Empty title="No matching guests" body="Try another name, phone or booking ID."/>}</section>
      {g&&<GuestPanel g={g}/>}
    </div>}
    <p className="footnote">All guest records are fictional. Duplicate suggestions compare normalised phone numbers and names; they never merge records automatically.</p></>;
}
function GuestPanel({g}:{g:Guest}){
  const {s,uid,open}=useHcc();const access=guestAccess(s,uid);const dups=guestDuplicates(s,g);const latest:Booking|undefined=guestStays(s,g.id).find(b=>b.registered);
  return <section className="card"><div className="section-title"><div><div className="eyebrow">{g.id}</div><h2>{g.name}</h2></div><div className="inline"><VerifiedPill g={g}/></div></div>
    <div className="gs-panel">
      {dups.length>0&&<div className="gs-dup" role="status"><ShieldCheck size={17}/><div><strong>Possible match: {dups.map(m=>`${m.guest.id} (${m.reason==='phone'?'same phone':'same name'})`).join(', ')}</strong>
        <div className="inline">{dups.map(m=><button key={m.guest.id} className="text-btn" onClick={()=>open({type:'guest-profile',id:m.guest.id})}>Open {m.guest.id}</button>)}</div></div></div>}
      <GuestDetails g={g}/>
      <h3 className="gs-sub">Stay history</h3><StayHistory g={g}/>
      <div className="inline gs-actions">{access.edit&&<button className="btn" onClick={()=>open({type:'guest-profile',id:g.id})}>Edit profile</button>}
        {latest?<button className="btn" onClick={()=>open({type:'guest-regcard',id:latest.id})}><Printer size={15}/>Registration card</button>:<span className="muted">Registration card available after registration.</span>}</div>
    </div></section>;
}
