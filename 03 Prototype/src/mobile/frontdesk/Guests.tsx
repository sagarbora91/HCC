// M07 Guest profile (phone): Guests page (search + cards) and the 'guest-profile' sheet override with collapsible sections
// Contact · ID (masked) · Company · Party · History, a duplicate banner on top, and the printable registration card kept (DEC-047).
import React,{useState} from 'react';
import {bookingRef} from '../../domain/bookingRef';
import {UserPlus,Printer,TriangleAlert,Lock} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Guest,guestView,guestAccess,guestById,guestStays,guestSearch,guestDuplicates,guestComplete,money,totals,nights,staySegmentLabel,plural} from '../../services/mock';
import {Fab,ListCard} from '../../app/mobile/components';
import {Pill,Empty,shortDate} from '../../components/ui';
import {GuestForm,emptyDraft,draftOf,hashOf} from '../../features/guests/GuestForm';
import {GuestIdValue} from '../../features/guests/GuestId';
import {mfInitials} from './logic';
import {MfSection,MfRows,MfSearch,MfStatus,useMfTop} from './shared';

export function MfGuests(){
  useMfTop();
  const {s,uid,open,search,setSearch}=useHcc();const access=guestAccess(s,uid);
  const list=guestSearch(s,search,uid).sort((a,b)=>a.name.localeCompare(b.name));
  return <div className="mf-page">
    <MfSearch value={search} onChange={setSearch} placeholder={access.contact?'Search name, phone or booking':'Search name or booking'}/>
    {!access.idDetails&&<p className="mf-note"><Lock size={13}/> ID and contact fields are masked for your role.</p>}
    <p className="mf-count">{list.length} profile{list.length===1?'':'s'}</p>
    {list.length?list.map(g=>{const v=guestView(s,uid,g),dup=guestDuplicates(s,g).length>0,stays=guestStays(s,g.id).length;
      return <ListCard key={g.id} title={<span className="mf-lc-guest"><span className="mf-initials" aria-hidden>{mfInitials(g.name)}</span>{g.name}</span>}
        sub={`${v.phone} · ${stays} stay${stays===1?'':'s'}`} meta={g.id}
        right={<span className="mf-stack">{g.verified?<Pill tone="green">Verified</Pill>:<Pill tone="orange">Unverified</Pill>}{dup&&<Pill tone="orange">Duplicate?</Pill>}</span>}
        onClick={()=>open({type:'guest-profile',id:g.id})}/>;})
      :<Empty title="No matching guests" body="Try another name, phone or booking ID."/>}
    {access.edit&&<Fab label="New guest" icon={UserPlus} onClick={()=>open({type:'guest-profile',id:'new'})}/>}
  </div>;
}

/** Modal 'guest-profile' (phone): id = guest ID or 'new'; room = booking ID to link to (optional). */
export function MfGuestProfile({id,room}:{id?:string;room?:string}){
  const {s,uid,run,modal,open,closeModal,booking,permission}=useHcc();
  const g=id&&id!=='new'?guestById(s,id):undefined;const linkTo=room?s.bookings.find(b=>b.id===room):undefined;
  const access=guestAccess(s,uid);const [editing,setEditing]=useState(!g);
  if(id&&id!=='new'&&!g)return <Empty title="This guest profile is unavailable"/>;
  const back=linkTo?<button type="button" className="btn" onClick={()=>booking(linkTo.id)}>Back</button>:<button type="button" className="btn" onClick={closeModal}>Close</button>;
  if(g&&!editing)return <MfGuestView g={g} back={back} onEdit={access.edit?()=>setEditing(true):undefined}/>;
  if(!access.edit)return <><div className="modal-heading"><h2>Guest profile</h2></div><Empty title="Not permitted" body="Creating or editing guest profiles needs the check-in permission."/><div className="modal-actions">{back}</div></>;
  const initial=g?draftOf(g):emptyDraft(linkTo?{name:linkTo.guest.replace(/\s*\(fictional\)\s*/,''),email:linkTo.email}:{});
  const save=(d:typeof initial,verified:boolean)=>run({type:'GUEST_SAVE',id:g?.id,version:g?.version,guest:d,verified,booking:linkTo?.id,key:modal!.key+':'+hashOf([d,verified])},
    g?'Guest profile updated':'Guest profile created',{after:()=>{if(linkTo)booking(linkTo.id);else if(g)setEditing(false);else closeModal();}});
  void permission;
  return <><div className="modal-heading"><div className="eyebrow">GUEST PROFILE{g?' · '+g.id:''}</div><h2>{g?'Edit '+g.name:'New guest'}</h2><p>{linkTo?`Will be linked to ${linkTo.id} · room ${linkTo.room}.`:'Fictional demonstration profile.'}</p></div>
    <div className="mf-guestform"><GuestForm initial={initial} guestId={g?.id} registration={false} submitLabel={g?'Save':'Create'} onSubmit={d=>save(d,!!g?.verified&&guestComplete(d))}
      onUseExisting={linkTo?(x=>run({type:'GUEST_SAVE',id:x.id,version:x.version,guest:draftOf(x),verified:x.verified,booking:linkTo.id,key:modal!.key+':use:'+x.id},`Linked ${x.id} to ${linkTo.id}`,{after:()=>booking(linkTo.id)})):undefined}
      secondary={g?<button type="button" className="btn" onClick={()=>setEditing(false)}>Cancel</button>:back}/></div></>;
}

function MfGuestView({g,back,onEdit}:{g:Guest;back:React.ReactNode;onEdit?:()=>void}){
  const {s,uid,open,booking,permission}=useHcc();const v=guestView(s,uid,g);
  const dups=guestDuplicates(s,g),stays=guestStays(s,g.id),latest=stays.find(b=>b.registered);
  return <><div className="modal-heading"><div className="eyebrow">GUEST PROFILE · {g.id}</div><h2>{g.name}</h2>
      <div className="mf-inline">{g.verified?<Pill tone="green">✓ Verified</Pill>:<Pill tone="orange">Not verified</Pill>}{g.origin==='auto'&&<Pill tone="orange">Minimal demo profile</Pill>}</div></div>
    {dups.length>0&&<div className="mf-dup" role="status"><TriangleAlert size={18}/><div><strong>Possible duplicate</strong>
      {dups.map(m=><button key={m.guest.id} type="button" className="text-btn" onClick={()=>open({type:'guest-profile',id:m.guest.id})}>{m.guest.id} · {m.guest.name} ({m.reason==='phone'?'same phone':'same name'})</button>)}
      <small>Suggestion only — records are never merged automatically.</small></div></div>}
    <MfSection title="Contact" open><MfRows rows={[['Phone',v.phone],['Email',v.email],['Address',v.address],['Nationality',v.nationality]]}/></MfSection>
    <MfSection title="ID (masked)" meta={v.masked?'Restricted':undefined}>
      {v.masked?<p className="mf-note"><Lock size={13}/> ID details are restricted for your role.</p>:<MfRows rows={[['ID type',v.idType],['ID',<GuestIdValue g={g}/>]]}/>}
      {!v.masked&&<p className="mf-note">Shown masked (last 4). "Show ID" is logged in the audit trail (DEC-068).</p>}</MfSection>
    <MfSection title="Company" meta={v.company?undefined:'None'}><MfRows rows={[['Company',v.company||''],['GSTIN',v.gstin||''],['Preferences',v.preferences],['Notes',v.notes]]}/></MfSection>
    <MfSection title="Party" meta={`${g.party.length}`}>{g.party.length?<ul className="mf-party">{g.party.map((p,i)=><li key={i}><strong>{p.name}</strong><span>{p.ageBand}{p.relation?' · '+p.relation:''}</span></li>)}</ul>:<p className="mf-note">No accompanying guests recorded.</p>}</MfSection>
    <MfSection title="History" meta={`${stays.length} stay${stays.length===1?'':'s'}`} open={stays.length>0}>
      {stays.length?stays.map(b=><div key={b.id} className="mf-hist">
        <ListCard title={`${bookingRef(b)} · ${staySegmentLabel(b)||'Room '+b.room}`} sub={`${shortDate(b.arrival)} – ${shortDate(b.departure)} · ${plural(nights(b.arrival,b.departure),'night')}`}
          right={<><MfStatus status={b.status}/>{permission('billing')&&<small className="mf-bal">{money(totals(b).total)}</small>}</>} onClick={()=>booking(b.id)}/>
        {b.registered&&<button type="button" className="text-btn" onClick={()=>open({type:'guest-regcard',id:b.id})}><Printer size={14}/>Registration card</button>}</div>)
        :<p className="mf-note">No stays linked to this profile yet.</p>}</MfSection>
    <div className="modal-actions">{back}
      {latest&&<button className="btn" onClick={()=>open({type:'guest-regcard',id:latest.id})}><Printer size={16}/>Print card</button>}
      {onEdit?<button className="btn primary" onClick={onEdit}>Edit</button>:null}</div></>;
}
