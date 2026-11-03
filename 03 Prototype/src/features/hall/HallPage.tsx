// F6-H3 · Page hall-booking (REVENUE & SALES, module 27): tabs Enquiries · Quotations · Calendar · Events · Menus · Templates. FitPage; every list is a ScrollPanel / board column.
// Money is shown only to the roles that may see it (access.ts); housekeeping / maintenance / supervisors see the calendar and the function sheet (DEC-061).
import React,{useState} from 'react';
import {Plus,Phone,AlertTriangle,ChevronLeft,ChevronRight} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,Empty} from '../../components/ui';
import {FitPage,FitTabs,ScrollPanel,StatStrip,BoardColumns,useFitTab,FitNote} from '../../components/fit';
import {money} from '../../domain/money';
import {hallSlice,hallHasPlaceholder,type TncTemplate,type TncClause} from '../../domain/streams/hall';
import {hallUi,hallTabsFor,HALL_TAB_LABEL,type HallTab} from './access';
import {hallBoard,hallQuoteRows,hallEventRows,hallMonthCells,hallMonthShift,hallDayGrid,hallAlerts,hallAvailability,venueName,SLOT_LABEL,SLOT_STATE_LABEL,SLOT_STATE_TONE,
  EVENT_LABEL,ENQ_TONE,QUOTE_TONE,EVENT_TONE,SETUP_LABEL,dayLabel} from './logic';
import {Field} from './ui';

export function HallPage(){
  const {s,uid,open}=useHcc();const ui=hallUi(s,uid),tabs=hallTabsFor(s,uid),al=hallAlerts(s),x=hallSlice(s);
  const [tab,setTab]=useFitTab<HallTab>('hall-booking',tabs.length?tabs:['calendar'],tabs[0]??'calendar');
  if(!ui.view)return <FitPage title="Hall booking"><Empty title="Not available" body={ui.readOnlyNote}/></FitPage>;
  const open1=x.enquiries.filter(q=>q.status==='OPEN').length,quotes=x.quotations.filter(q=>q.versions.find(v=>v.v===q.currentVersion)?.status==='SENT').length,ahead=x.events.filter(e=>e.status==='CONFIRMED').length;
  const actions=<>
    {ui.enquiry&&<button className="btn primary" onClick={()=>open({type:'hall-enquiry'})}><Plus size={16}/> New enquiry</button>}
    {tab==='menus'&&ui.venues&&<button className="btn" onClick={()=>open({type:'hall-venue'})}><Plus size={16}/> New hall</button>}
    {tab==='menus'&&ui.menus&&<button className="btn" onClick={()=>open({type:'hall-menu'})}><Plus size={16}/> New menu</button>}
    {!ui.enquiry&&<Pill>{ui.seesMoney?'View only':'No prices shown'}</Pill>}</>;
  const stats=[{title:'Follow-ups today',value:String(al.followUps),tone:al.followUps||al.overdue?'warn' as const:undefined,sub:al.overdue?`${al.overdue} overdue`:undefined},
    {title:'Open enquiries',value:String(open1)},{title:'Quotations out',value:String(quotes),tone:al.expiring||al.expired?'warn' as const:undefined,sub:al.expiring?`${al.expiring} expire soon`:al.expired?`${al.expired} expired`:undefined},
    {title:'Events ahead',value:String(ahead),sub:ui.seesMoney&&al.advancesDue?`${al.advancesDue} advance(s) due`:undefined}];
  return <FitPage title="Hall booking" sub="Halls and lawn: enquiry, quotation with terms, calendar, event-day bill. Advances carry no GST; GST is on the tax invoice." actions={actions}
    toolbar={ui.enquiry||ui.seesMoney?<StatStrip page="hall-booking" variant="small" compact collapseOnTablet items={stats}/>:undefined}
    tabs={<FitTabs page="hall-booking" value={tab} onChange={setTab} tabs={tabs.map(t=>({id:t,label:HALL_TAB_LABEL[t],badge:t==='enquiries'&&al.followUps?'warn':undefined}))}/>}>
    {tab==='enquiries'&&<EnquiriesTab/>}{tab==='quotations'&&<QuotationsTab/>}{tab==='calendar'&&<CalendarTab/>}{tab==='events'&&<EventsTab/>}{tab==='menus'&&<MenusTab/>}{tab==='templates'&&<TemplatesTab/>}
  </FitPage>;
}

export function EnquiryCard({q,mobile}:{q:ReturnType<typeof hallBoard>['columns'][number]['items'][number];mobile?:boolean}){
  const {s,open}=useHcc();const x=hallSlice(s),b=hallBoard(s),av=q.venueId&&(q.status==='OPEN'||q.status==='QUOTED')?hallAvailability(s,q.venueId,q.dates,q.slot):undefined;
  return <button type="button" className={'hall-card'+(mobile?' m':'')} onClick={()=>open({type:'hall-enquiry',id:q.id})}>
    <strong>{q.name}</strong><span>{EVENT_LABEL[q.eventType]} · {q.pax} guests</span><span>{q.dates.map(dayLabel).join(', ')} · {SLOT_LABEL[q.slot]}{q.venueId?' · '+venueName(x,q.venueId):''}</span>
    <small><Phone size={12}/> {q.phone} · follow up {dayLabel(q.followUp)}</small>
    <span className="hall-card-pills">{b.isDue(q)&&(q.status==='OPEN'||q.status==='QUOTED')&&<Pill tone="orange">{q.followUp<s.date?'Follow-up overdue':'Follow-up today'}</Pill>}
      {q.softHold&&<Pill tone="neutral">Hall held</Pill>}{av&&!av.free&&<Pill tone="red"><AlertTriangle size={12}/> Clash {av.clashes[0].eventId}</Pill>}</span>
  </button>;
}

function EnquiriesTab(){
  const b=hallBoard(useHcc().s);
  const label={OPEN:'Open',QUOTED:'Quoted',WON:'Won',LOST:'Lost'} as const;
  return <BoardColumns columns={b.columns.map(c=>({id:c.status,title:label[c.status],count:c.items.length,
    body:c.items.length?<div className="hall-cards">{c.items.map(q=><EnquiryCard key={q.id} q={q}/>)}</div>:<p className="muted">None.</p>}))} minColumn={250}/>;
}

function QuotationsTab(){
  const {s,uid,open}=useHcc();const ui=hallUi(s,uid);const [status,setStatus]=useState(''),[search,setSearch]=useState('');
  const rows=hallQuoteRows(s,status,search);
  return <ScrollPanel label="Quotations" title={`Quotations (${rows.length})`} actions={<div className="inline">
    <input type="search" aria-label="Search quotations" placeholder="Number, customer, hall" value={search} onChange={e=>setSearch(e.target.value)}/>
    <select aria-label="Status" value={status} onChange={e=>setStatus(e.target.value)}><option value="">All statuses</option>{['DRAFT','SENT','ACCEPTED','EXPIRED','LOST','SUPERSEDED','CANCELLED'].map(o=><option key={o}>{o}</option>)}</select></div>}
    footer={<FitNote lines={1} text="A sent quotation is locked: Revise makes the next version and the old one stays on file."/>}>
    {rows.length?<table className="table"><thead><tr><th>Number</th><th>Customer</th><th>Event</th><th>Hall</th><th>Version</th><th>Status</th>{ui.seesMoney&&<th className="num">Total</th>}<th>Valid until</th></tr></thead>
      <tbody>{rows.map(r=><tr key={r.q.id} className="clickable"><td><button type="button" className="link" onClick={()=>open({type:'hall-quote',id:r.q.id})}>{r.q.number}</button></td><td>{r.customer}</td>
        <td>{EVENT_LABEL[r.v.header.eventType]} · {dayLabel(r.date)}</td><td>{r.venue}</td><td>v{r.v.v}</td><td><Pill tone={QUOTE_TONE[r.v.status]}>{r.v.status}</Pill></td>
        {ui.seesMoney&&<td className="num">{money(r.totalPaise)}</td>}<td>{dayLabel(r.v.validUntil)}</td></tr>)}</tbody></table>
      :<Empty title="No quotations yet" body="Open an enquiry and choose Create quotation."/>}
  </ScrollPanel>;
}

function CalendarTab(){
  const {s,open}=useHcc();const [ym,setYm]=useState(s.date.slice(0,7)),[day,setDay]=useState(s.date);
  const cells=hallMonthCells(s,ym),grid=hallDayGrid(s,day);
  const mname=new Date(ym+'-01T12:00:00Z').toLocaleString('en-IN',{month:'long',year:'numeric',timeZone:'UTC'});
  return <div className="hall-cal">
    <ScrollPanel label="Month" title={mname} actions={<div className="inline"><button type="button" className="icon-btn" aria-label="Previous month" onClick={()=>setYm(hallMonthShift(ym,-1))}><ChevronLeft size={18}/></button>
      <button type="button" className="btn" onClick={()=>{setYm(s.date.slice(0,7));setDay(s.date);}}>Today</button><button type="button" className="icon-btn" aria-label="Next month" onClick={()=>setYm(hallMonthShift(ym,1))}><ChevronRight size={18}/></button></div>}>
      <div className="hall-month" role="grid" aria-label={mname}>{['Mon','Tue','Wed','Thu','Fri','Sat','Sun'].map(d=><div key={d} className="hall-month-h" role="columnheader">{d}</div>)}
        {cells.map(c=><button key={c.date} type="button" role="gridcell" aria-selected={c.date===day} className={'hall-day'+(c.inMonth?'':' out')+(c.date===day?' on':'')+(c.date===s.date?' today':'')} onClick={()=>setDay(c.date)}>
          <strong>{Number(c.date.slice(8))}</strong><small>{c.count?`${c.count} booked`:''}</small></button>)}</div>
    </ScrollPanel>
    <ScrollPanel label="Day" title={dayLabel(day)}>
      <table className="table hall-grid"><thead><tr><th>Hall</th>{(['MORNING','AFTERNOON','EVENING'] as const).map(p=><th key={p}>{SLOT_LABEL[p]}</th>)}</tr></thead>
        <tbody>{grid.map(r=><tr key={r.venue.id}><th scope="row">{r.venue.name}{r.venue.combinedOf&&<small className="muted"> combined</small>}</th>{r.slots.map(c=><td key={c.slot}>
          <Pill tone={SLOT_STATE_TONE[c.state]}>{SLOT_STATE_LABEL[c.state]}</Pill>
          {c.state!=='FREE'&&<button type="button" className="link hall-cell" onClick={()=>open(c.ref?.startsWith('HEV')?{type:'hall-event',id:c.ref}:{type:'hall-enquiry',id:c.ref})}>{c.label}</button>}
          {c.via&&<small className="muted">blocked by {c.via}</small>}</td>)}</tr>)}</tbody></table>
      <p className="muted">A full-day booking holds all three parts. A combined hall is blocked when any hall in it is booked.</p>
    </ScrollPanel></div>;
}

function EventsTab(){
  const {s,uid,open}=useHcc();const ui=hallUi(s,uid);const [f,setF]=useState<'upcoming'|'billed'|'cancelled'|'all'>('upcoming'),[search,setSearch]=useState('');
  const rows=hallEventRows(s,f,search);
  return <ScrollPanel label="Events" title={`Events (${rows.length})`} actions={<div className="inline"><input type="search" aria-label="Search events" placeholder="Event, customer, hall" value={search} onChange={e=>setSearch(e.target.value)}/>
    <div className="m-chips" role="group" aria-label="Show">{(['upcoming','billed','cancelled','all'] as const).map(o=><button type="button" key={o} className={f===o?'on':''} aria-pressed={f===o} onClick={()=>setF(o)}>{o[0].toUpperCase()+o.slice(1)}</button>)}</div></div>}>
    {rows.length?<table className="table"><thead><tr><th>Event</th><th>Date</th><th>Hall</th><th>Customer</th><th className="num">Plates</th><th>Status</th>{ui.seesMoney&&<><th className="num">Total</th><th className="num">Paid</th><th className="num">Balance</th></>}</tr></thead>
      <tbody>{rows.map(r=><tr key={r.e.id}><td><button type="button" className="link" onClick={()=>open({type:'hall-event',id:r.e.id})}>{r.e.id}</button></td><td>{dayLabel(r.e.date)} · {SLOT_LABEL[r.e.slot]}</td><td>{r.venue}</td><td>{r.customer}</td>
        <td className="num">{r.pax}</td><td><Pill tone={EVENT_TONE[r.e.status]}>{r.e.status==='CONFIRMED'?'Confirmed':r.e.status==='BILLED'?'Billed':'Cancelled'}</Pill></td>
        {ui.seesMoney&&<><td className="num">{money(r.totalPaise)}</td><td className="num">{money(r.paidPaise)}</td><td className="num">{money(r.balancePaise)}</td></>}</tr>)}</tbody></table>
      :<Empty title="No events here" body="An accepted quotation becomes an event."/>}
  </ScrollPanel>;
}

function MenusTab(){
  const {s,uid,open}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s);
  return <div className="hall-cal">
    <ScrollPanel label="Menu packages" title="Menu packages (per plate)">
      {x.menus.length?<table className="table"><thead><tr><th>Menu</th><th>Items</th><th>Veg</th>{ui.seesMoney&&<th className="num">Per plate</th>}<th>Status</th></tr></thead>
        <tbody>{x.menus.map(m=><tr key={m.id}><td>{ui.menus?<button type="button" className="link" onClick={()=>open({type:'hall-menu',id:m.id})}>{m.name}</button>:m.name}</td><td>{m.posItemIds.length}</td><td>{m.veg?'Veg':'Veg + non-veg'}</td>
          {ui.seesMoney&&<td className="num">{money(m.perPlatePaise)}</td>}<td><Pill tone={m.active?'green':'neutral'}>{m.active?'Active':'Retired'}</Pill></td></tr>)}</tbody></table>:<Empty title="No menus yet"/>}
    </ScrollPanel>
    <ScrollPanel label="Halls" title="Halls and lawn">
      <table className="table"><thead><tr><th>Hall</th><th>Seats (round / theatre)</th><th>List rates</th><th>Status</th></tr></thead>
        <tbody>{x.venues.map(v=><tr key={v.id}><td>{ui.venues?<button type="button" className="link" onClick={()=>open({type:'hall-venue',id:v.id})}>{v.name}</button>:v.name}{v.combinedOf&&<small className="muted"> = {v.combinedOf.map(p=>venueName(x,p)).join(' + ')}</small>}</td>
          <td>{v.capacity.ROUND??'—'} / {v.capacity.THEATRE??'—'}</td><td>{ui.seesMoney?v.slots.map(o=>`${SLOT_LABEL[o.slot]} ${money(o.ratePaise)}`).join(' · '):v.slots.map(o=>SLOT_LABEL[o.slot]).join(' · ')}</td>
          <td><Pill tone={v.active?'green':'neutral'}>{v.active?'Active':'Retired'}</Pill></td></tr>)}</tbody></table>
    </ScrollPanel></div>;
}

function TemplatesTab(){
  const {s,uid}=useHcc();const ui=hallUi(s,uid),x=hallSlice(s);const [sel,setSel]=useState(x.templates[0]?.id??'');
  const t=x.templates.find(o=>o.id===sel)??x.templates[0];
  return <div className="hall-cal">
    <ScrollPanel label="Terms templates" title="Terms and conditions"><ul className="hall-tlist">{x.templates.map(o=>{const v=o.versions[o.versions.length-1],ph=!!v&&hallHasPlaceholder(v.clauses);
      return <li key={o.id}><button type="button" className={'hall-trow'+(o.id===t?.id?' on':'')} aria-pressed={o.id===t?.id} onClick={()=>setSel(o.id)}><strong>{o.eventType==='GENERAL'?'General (all events)':EVENT_LABEL[o.eventType]}</strong>
        <small>v{v?.v} from {v?dayLabel(v.effectiveFrom):'—'}</small>{ph&&<Pill tone="orange">Owner to replace</Pill>}</button></li>;})}</ul>
      {!ui.tnc&&<p className="muted">Only the Owner changes the terms. A quotation keeps the version it was sent with.</p>}</ScrollPanel>
    {t?<TemplateEditor key={t.id+':'+t.versions.length} t={t} canEdit={ui.tnc}/>:<Empty title="No templates"/>}</div>;
}

function TemplateEditor({t,canEdit}:{t:TncTemplate;canEdit:boolean}){
  const {s,run,busy}=useHcc();const last=t.versions[t.versions.length-1];
  const [clauses,setClauses]=useState<TncClause[]>(()=>last?.clauses.map(c=>({...c}))??[]);
  const dirty=JSON.stringify(clauses)!==JSON.stringify(last?.clauses??[]),ph=hallHasPlaceholder(clauses);
  return <ScrollPanel label="Clauses" title={`${t.eventType==='GENERAL'?'General':EVENT_LABEL[t.eventType]} · v${last?.v??0}`} actions={canEdit?<button className="btn primary" disabled={busy||!dirty} onClick={()=>run({type:'HALL_TNC_TEMPLATE_SAVE',eventType:t.eventType,clauses,effectiveFrom:s.date},'Terms saved as a new version',{close:false})}>Save as new version</button>:undefined}
    footer={<FitNote lines={1} text="Saving makes a new version from today. Quotations already sent keep their own copy of the terms."/>}>
    {ph&&<p className="hall-warn" role="alert">Some clauses still say “OWNER TO REPLACE”. A quotation cannot be sent with them until the Owner replaces the text or records an override.</p>}
    {clauses.map((c,i)=><div className="hall-clause" key={c.n}><Field label={`Clause ${c.n} heading`}><input value={c.heading} disabled={!canEdit} onChange={e=>setClauses(o=>o.map((z,j)=>j===i?{...z,heading:e.target.value}:z))}/></Field>
      <Field label="Text" wide><textarea rows={3} value={c.text} disabled={!canEdit} onChange={e=>setClauses(o=>o.map((z,j)=>j===i?{...z,text:e.target.value}:z))}/></Field></div>)}
    <h4>Versions</h4><ul className="hall-list">{t.versions.map(v=><li key={v.v}>v{v.v} · from {dayLabel(v.effectiveFrom)} · {v.clauses.length} clauses</li>)}</ul>
  </ScrollPanel>;
}
