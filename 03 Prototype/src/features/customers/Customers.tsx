// Customers (Customer 360): search by name or mobile, then the whole history of one customer — stays, profile, requests and complaints.
// Responsive: two columns on desktop; on a phone the list and the customer are separate screens. Booking-ID search is on Arrivals & check-in.
import React,{useMemo,useState} from 'react';
import {ArrowLeft,Lock,UserPlus,Printer,Repeat,BedDouble,CalendarClock,IndianRupee,Phone,Mail} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,nights,guestAccess,guestMaskPhone,guestDuplicates,staySegmentLabel,totals} from '../../services/mock';
import {customerIndex,customerSearch,customerService,type Customer} from '../../domain/streams/customers';
import {PageTitle,SearchBox,Pill,Empty,shortDate,dateLabel} from '../../components/ui';
import {usePaged,ShowMore,DESKTOP_PAGE,PHONE_PAGE} from '../../components/ShowMore';
import {GuestDetails,VerifiedPill,statusTone} from '../guests/GuestProfile';

type Tab='history'|'profile'|'service';
const initials=(n:string)=>n.split(' ').map(p=>p[0]).slice(0,2).join('').toUpperCase();

export function CustomersPage(){
  const {s,uid,search,setSearch,display}=useHcc();
  const access=guestAccess(s,uid);
  const all=useMemo(()=>customerIndex(s),[s.bookings,s.guests,s.date]); // eslint-disable-line react-hooks/exhaustive-deps
  const list=useMemo(()=>customerSearch(all,search,access.contact),[all,search,access.contact]);
  const paged=usePaged(list,display.isMobile?PHONE_PAGE:DESKTOP_PAGE);
  const [sel,setSel]=useState('');
  const picked=all.find(c=>c.key===sel);
  const c=picked??(display.isMobile?undefined:list[0]);
  const digitsOnly=search.trim()!==''&&!/[a-z]/i.test(search);
  return <>
    <PageTitle title="Customers" sub={`Everyone who has booked with us — ${all.length.toLocaleString('en-IN')} customers. Search by name or mobile number.`}/>
    <div className={'cust'+(c&&sel?' has-sel':'')}>
      <section className="card cust-list-card">
        <div className="toolbar"><SearchBox value={search} onChange={setSearch} placeholder={access.contact?'Search name or mobile number…':'Search by name…'}/>
          {!access.contact&&<span className="muted"><Lock size={12}/> Mobile numbers are masked for your role</span>}</div>
        <div className="section-title"><h2>Customers</h2><span className="muted">{list.length.toLocaleString('en-IN')} shown</span></div>
        {list.length?<><ul className="cust-list">{paged.shown.map(x=><li key={x.key}><button type="button" className={'cust-item'+(c?.key===x.key?' active':'')} aria-pressed={c?.key===x.key} onClick={()=>setSel(x.key)}>
          <span className="avatar" aria-hidden="true">{initials(x.name)}</span>
          <span className="cust-main"><strong>{x.name}</strong><small>{access.contact?x.phone||'No mobile':guestMaskPhone(x.phone)} · {x.completed.length} stay{x.completed.length===1?'':'s'}{x.lastStay?' · last '+shortDate(x.lastStay):''}</small></span>
          {x.current?<Pill tone="green">In house</Pill>:x.completed.length>1?<Pill tone="blue">Repeat</Pill>:null}</button></li>)}</ul><ShowMore p={paged}/></>
          :<Empty title="No matching customers" body={digitsOnly&&!access.contact?'Mobile search is not available for your role. Search by name.':digitsOnly?'Type at least 3 digits of the mobile number, or search by name.':'Try fewer letters or another spelling. Booking IDs are searched from Arrivals & check-in.'}/>}
      </section>
      {c?<CustomerPanel key={c.key} c={c} onBack={()=>setSel('')}/>:<section className="card cust-empty"><Empty title="Choose a customer" body="Their full stay history appears here."/></section>}
    </div>
    <p className="footnote">All customer records are fictional. Customers are matched by mobile number; the same person with two different numbers appears twice.</p></>;
}

function CustomerPanel({c,onBack}:{c:Customer;onBack:()=>void}){
  const {s,uid,permission,open}=useHcc();
  const [tab,setTab]=useState<Tab>('history');
  const access=guestAccess(s,uid),money_=permission('billing');
  const svc=customerService(s,c);const svcCount=svc.requests.length+svc.complaints.length;
  const stat=(Icon:React.ComponentType<{size?:number}>,k:string,v:string)=><div className="cust-stat"><Icon size={16}/><span>{k}</span><strong>{v}</strong></div>;
  const tabs:[Tab,string][]=[['history','Stay history ('+c.stays.length+')'],['profile','Profile'],['service','Requests & complaints'+(svcCount?' ('+svcCount+')':'')]];
  return <section className="card cust-panel" aria-label={'Customer '+c.name}>
    <button type="button" className="btn cust-back" onClick={onBack}><ArrowLeft size={16}/>All customers</button>
    <div className="cust-head"><span className="avatar big" aria-hidden="true">{initials(c.name)}</span>
      <div><h2>{c.name}</h2>
        <div className="inline">{c.guest&&<VerifiedPill g={c.guest}/>}{c.current&&<Pill tone="green">In house · room {c.current.room}</Pill>}{c.completed.length>1&&<Pill tone="blue"><Repeat size={11}/> Repeat customer</Pill>}{c.upcoming.length>0&&<Pill tone="orange">Upcoming {shortDate(c.upcoming[0].arrival)}</Pill>}</div>
        <p className="cust-contact"><span><Phone size={13}/> {access.contact?c.phone||'—':guestMaskPhone(c.phone)}</span>{c.email&&<span><Mail size={13}/> {access.contact?c.email:c.email[0]+'•••@'+(c.email.split('@')[1]||'')}</span>}</p></div></div>
    <div className="cust-stats">
      {stat(BedDouble,'Stays',String(c.completed.length))}{stat(CalendarClock,'Nights',String(c.nights))}
      {stat(CalendarClock,'First / last stay',c.firstStay?`${shortDate(c.firstStay)} / ${shortDate(c.lastStay!)}`:'—')}
      {money_?stat(IndianRupee,'Lifetime spend',money(c.lifetime)):null}{money_&&c.avgRate?stat(IndianRupee,'Average tariff',money(c.avgRate)+'/night'):null}
    </div>
    {(c.topRooms.length>0||c.topSources.length>0||c.cancelled>0)&&<p className="cust-habits">{c.topRooms.length>0&&<span>Usually books room {c.topRooms.join(', ')}</span>}{c.topSources.length>0&&<span>Via {c.topSources.join(', ')}</span>}{c.cancelled>0&&<span>{c.cancelled} cancelled / no-show</span>}</p>}
    <div className="cust-tabs" role="tablist" aria-label="Customer sections">{tabs.map(([k,l])=><button key={k} type="button" role="tab" aria-selected={tab===k} className={tab===k?'on':''} onClick={()=>setTab(k)}>{l}</button>)}</div>
    {tab==='history'&&<History c={c} showMoney={money_}/>}
    {tab==='profile'&&<Profile c={c}/>}
    {tab==='service'&&<Service c={c} svc={svc} onOpen={open}/>}
  </section>;
}

function History({c,showMoney}:{c:Customer;showMoney:boolean}){
  const {booking,display}=useHcc();
  const paged=usePaged(c.stays,display.isMobile?PHONE_PAGE:DESKTOP_PAGE);
  return <>{!c.stays.length?<p className="muted padded">No stays yet.</p>:
    <ul className="cust-stays">{paged.shown.map(b=><li key={b.id}><button type="button" className="cust-stay" onClick={()=>booking(b.id)}>
      <span className="cust-stay-main"><strong>{shortDate(b.arrival)} — {dateLabel(b.departure)}</strong><small>Room {staySegmentLabel(b)||b.room} · {nights(b.arrival,b.departure)} night{nights(b.arrival,b.departure)===1?'':'s'} · {b.source} · {b.id}</small></span>
      <span className="cust-stay-right"><Pill tone={statusTone(b.status)}>{b.status}</Pill>{showMoney&&(b.status==='In house'||b.status==='Departed')&&<b>{money(totals(b).total)}</b>}</span></button></li>)}</ul>}
    <ShowMore p={paged}/></>;
}

function Profile({c}:{c:Customer}){
  const {s,uid,open}=useHcc();const access=guestAccess(s,uid);const g=c.guest;
  if(!g)return <div className="cust-noprofile"><Empty title="No saved profile yet" body="A profile keeps ID details, preferences and notes. One is created at registration."/>
    {access.edit&&c.stays[0]&&<button type="button" className="btn primary" onClick={()=>open({type:'guest-profile',id:'new',room:c.stays[0].id})}><UserPlus size={16}/>Create profile from latest booking</button>}</div>;
  const dups=guestDuplicates(s,g);const latest=c.stays.find(b=>b.registered);
  return <div className="cust-profile">
    {dups.length>0&&<div className="gs-dup" role="status"><div><strong>Possible match: {dups.map(m=>`${m.guest.id} (${m.reason==='phone'?'same phone':'same name'})`).join(', ')}</strong>
      <div className="inline">{dups.map(m=><button key={m.guest.id} className="text-btn" onClick={()=>open({type:'guest-profile',id:m.guest.id})}>Open {m.guest.id}</button>)}</div></div></div>}
    <GuestDetails g={g}/>
    <div className="inline gs-actions">{access.edit&&<button type="button" className="btn" onClick={()=>open({type:'guest-profile',id:g.id})}>Edit profile</button>}
      {latest&&<button type="button" className="btn" onClick={()=>open({type:'guest-regcard',id:latest.id})}><Printer size={15}/>Registration card</button>}</div></div>;
}

function Service({c,svc,onOpen}:{c:Customer;svc:ReturnType<typeof customerService>;onOpen:(m:{type:string;id?:string})=>void}){
  void c;
  if(!svc.requests.length&&!svc.complaints.length)return <p className="muted padded">No guest requests or complaints on record.</p>;
  return <>{svc.requests.length>0&&<><h3 className="gs-sub">Guest requests</h3><ul className="cust-stays">{svc.requests.slice(0,50).map(r=><li key={r.id}><button type="button" className="cust-stay" onClick={()=>onOpen({type:'gsv-request',id:r.id})}>
    <span className="cust-stay-main"><strong>{r.type}</strong><small>Room {r.room} · {r.detail||r.id}</small></span><span className="cust-stay-right"><Pill tone={r.status==='Done'?'green':r.status==='Cancelled'?'neutral':'orange'}>{r.status}</Pill></span></button></li>)}</ul></>}
    {svc.complaints.length>0&&<><h3 className="gs-sub">Complaints</h3><ul className="cust-stays">{svc.complaints.slice(0,50).map(k=><li key={k.id}><button type="button" className="cust-stay" onClick={()=>onOpen({type:'gsv-complaint',id:k.id})}>
    <span className="cust-stay-main"><strong>{k.category} · {k.severity}</strong><small>{k.description}</small></span><span className="cust-stay-right"><Pill tone={k.status==='Resolved'||k.status==='Closed'?'green':'orange'}>{k.status}</Pill></span></button></li>)}</ul></>}</>;
}
