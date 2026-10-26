// Module 24–25 · dialogs (full-screen sheets on phones): compose, message detail, template editor, consent editor, document review.
import React,{useState,useMemo} from 'react';
import {bookingRefOf} from '../../domain/bookingRef';
import {Info,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Empty,clockTime,shortDate} from '../../components/ui';
import {Stepper} from '../../app/mobile/components';
import {MSGChannel,MSG_CHANNELS,MSG_PLACEHOLDERS,MSG_DEFAULT_CONSENT,msgInHouse,msgTemplates,msgTemplate,msgValues,msgRender,msgBlockReason,msgRecipient,
  msgSmsSegments,msgTimeline,msgStatus,msgDone,msgConsent,msgUnknownPlaceholders,msgSlice} from '../../services/mock';
import {MsgBubble,MsgStatusPill,MsgSimulated,MsgDocReview,MsgKindLabel,msgChannelIcon,msgTemplateName} from './shared';

/** 'msg-compose' — id = booking ID (optional). Manual message to an in-house guest. */
export function MsgComposeModal({id}:{id?:string}){
  const {s,run,busy,closeModal,modal}=useHcc();
  const guests=msgInHouse(s);
  const [booking,setBooking]=useState(id&&guests.some(b=>b.id===id)?id:guests[0]?.id||'');
  const [channel,setChannel]=useState<MSGChannel>('WhatsApp');
  const [tpl,setTpl]=useState('');
  const [body,setBody]=useState('');const [subject,setSubject]=useState('');
  const b=s.bookings.find(x=>x.id===booking);
  const pick=(t:string)=>{setTpl(t);const x=msgTemplate(s,t);if(x){setBody(x.body);setSubject(x.subject);setChannel(x.channel);}};
  if(!guests.length)return <><div className="modal-heading"><h2>Compose message</h2></div><Empty title="No in-house guests" body="Manual messages go only to guests who are checked in."/></>;
  const why=b?msgBlockReason(s,b,channel):'Choose a guest.';
  const preview=b?msgRender(body,msgValues(s,b)):{text:'',missing:[]};
  const unknown=msgUnknownPlaceholders(body);
  const to=b?msgRecipient(s,b,channel):null;
  const ok=!why&&body.trim().length>0&&body.length<=600&&!unknown.length;
  return <><div className="modal-heading"><h2>Compose message</h2><p>To an in-house guest. Placeholders are filled from the booking.</p></div>
    <MsgSimulated/>
    <form className="form" onSubmit={e=>{e.preventDefault();if(!ok)return;run({type:'MSG_SEND',booking,channel,body,subject:channel==='Email'?subject:undefined,template:tpl||undefined,key:modal!.key},'Message queued (simulated)',{close:true});}}>
      <label>Guest<select value={booking} onChange={e=>setBooking(e.target.value)}>{guests.map(x=><option key={x.id} value={x.id}>{x.guest} · room {x.room}</option>)}</select></label>
      <div className="form-row">
        <label>Channel<select value={channel} onChange={e=>setChannel(e.target.value as MSGChannel)}>{MSG_CHANNELS.map(c=><option key={c}>{c}</option>)}</select></label>
        <label>Start from template<select value={tpl} onChange={e=>pick(e.target.value)}><option value="">Blank message</option>{msgTemplates(s).map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
      </div>
      {channel==='Email'&&<label>Subject<input value={subject} maxLength={120} onChange={e=>setSubject(e.target.value)} enterKeyHint="next"/></label>}
      <label>Message<textarea required rows={4} maxLength={600} value={body} onChange={e=>setBody(e.target.value)} placeholder="e.g. Hello {firstName}, your room is ready."/></label>
      <p className="msg-hint">Placeholders: {MSG_PLACEHOLDERS.map(p=>`{${p}}`).join(' ')} · {body.length}/600{channel==='SMS'?` · ${msgSmsSegments(preview.text)} SMS segment(s)`:''}</p>
      {!!unknown.length&&<p className="f-why">Unknown placeholder: {unknown.map(u=>`{${u}}`).join(', ')}</p>}
      {why?<div className="info-note msg-warn" role="alert"><AlertTriangle size={16}/>{why} — this message cannot be queued. Change the channel or the guest’s consent.</div>
        :<p className="muted">Recipient: {to?.masked}{to&&!to.valid?' (number looks invalid — delivery will fail in the simulation)':''}</p>}
      {body.trim()&&b&&<div className="msg-preview"><span className="msg-preview-label">Preview</span>
        <div className={'msg-bubble msg-ch-'+channel.toLowerCase()}><div className="msg-bubble-head">{msgChannelIcon(channel,14)}<span>{channel} · {b.guest}</span></div>
          {channel==='Email'&&subject&&<strong className="msg-bubble-subject">{msgRender(subject,msgValues(s,b)).text}</strong>}<p>{preview.text}</p></div></div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!ok}>Queue message</button></div>
    </form></>;
}

/** 'msg-message' — id = message ID. Bubble preview + delivery timeline. */
export function MsgMessageModal({id}:{id?:string}){
  const {s,run,busy,closeModal,permission,booking}=useHcc();
  const m=(msgSlice(s).outbox||[]).find(x=>x.id===id);
  if(!m)return <Empty title="This message is unavailable"/>;
  const st=msgStatus(m,s.clock),tl=msgTimeline(m,s.clock);
  return <><div className="modal-heading"><h2>{msgTemplateName(s,m.template)}</h2><p>{m.id} · {m.channel} · booking {bookingRefOf(s.bookings,m.booking)}{m.rule?` · rule ${m.rule}`:' · manual'}{m.retryOf?` · retry of ${m.retryOf}`:''}</p></div>
    <MsgSimulated>Statuses advance on the simulated hotel clock; nothing is delivered for real.</MsgSimulated>
    <MsgBubble m={m}/>
    <h3 className="msg-sub">Delivery timeline</h3>
    <ol className="msg-timeline">{tl.map(x=><li key={x.status}><MsgStatusPill st={x.status}/><span>{shortDate(x.at.slice(0,10))} {clockTime(x.at)}</span></li>)}</ol>
    {m.blocked&&<p className="f-why">Not sent: {m.blocked}</p>}
    {st==='Failed'&&<p className="f-why">Simulated failure: {m.to?'handset unreachable or number invalid':'no recipient'}.{m.retriedBy?` Retried as ${m.retriedBy}.`:''}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
      <button type="button" className="btn subtle" onClick={()=>booking(m.booking)}>Open booking</button>
      {permission('reservations')&&!msgDone(m,s.clock)&&<button type="button" className="btn primary" disabled={busy} onClick={()=>run({type:'MSG_SIMULATE',id:m.id},'Advanced one simulated step')}>Simulate delivery</button>}
      {permission('reservations')&&st==='Failed'&&!m.retriedBy&&<button type="button" className="btn primary" disabled={busy} onClick={()=>run({type:'MSG_RETRY',id:m.id,key:'retry:'+m.id},'Retry queued (simulated)',{close:true})}>Retry</button>}
    </div></>;
}

/** 'msg-template' — id = template ID. */
export function MsgTemplateModal({id}:{id?:string}){
  const {s,run,busy,closeModal,modal}=useHcc();
  const t=msgTemplate(s,id||'');
  const [body,setBody]=useState(t?.body||'');const [subject,setSubject]=useState(t?.subject||'');const [channel,setChannel]=useState<MSGChannel>(t?.channel||'WhatsApp');
  const sample=s.bookings.find(b=>b.status==='In house')||s.bookings[0];
  if(!t)return <Empty title="This template is unavailable"/>;
  const unknown=[...msgUnknownPlaceholders(body),...msgUnknownPlaceholders(subject)];
  const ok=body.trim().length>=10&&body.length<=600&&subject.trim().length>=3&&!unknown.length;
  return <><div className="modal-heading"><h2>Edit template · {t.name}</h2><p>Only the listed placeholders are allowed.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)run({type:'MSG_TEMPLATE',id:t.id,body,subject,channel,key:modal!.key},'Template saved',{close:true});}}>
      <label>Default channel<select value={channel} onChange={e=>setChannel(e.target.value as MSGChannel)}>{MSG_CHANNELS.map(c=><option key={c}>{c}</option>)}</select></label>
      <label>Subject (email)<input value={subject} maxLength={120} onChange={e=>setSubject(e.target.value)}/></label>
      <label>Text<textarea rows={5} maxLength={600} value={body} onChange={e=>setBody(e.target.value)}/></label>
      <p className="msg-hint">{MSG_PLACEHOLDERS.map(p=>`{${p}}`).join(' ')} · {body.length}/600</p>
      {!!unknown.length&&<p className="f-why">Unknown placeholder: {unknown.map(u=>`{${u}}`).join(', ')}</p>}
      {sample&&<div className="msg-preview"><span className="msg-preview-label">Preview with {sample.guest}</span><div className={'msg-bubble msg-ch-'+channel.toLowerCase()}><p>{msgRender(body,msgValues(s,sample)).text}</p></div></div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!ok}>Save template</button></div>
    </form></>;
}

/** 'msg-consent' — id = guest key (G-… or B:<booking>). */
export function MsgConsentModal({id}:{id?:string}){
  const {s,run,busy,closeModal,modal}=useHcc();
  const key=id||'';const c=msgConsent(s,key);
  const g=(s.guests||[]).find(x=>x.id===key),bk=key.startsWith('B:')?s.bookings.find(b=>'B:'+b.id===key):undefined;
  const [f,setF]=useState({...MSG_DEFAULT_CONSENT,...c});
  if(!g&&!bk)return <Empty title="This guest is unavailable"/>;
  const row=(k:'whatsapp'|'sms'|'email',label:string)=><label className="msg-switch"><input type="checkbox" disabled={f.optOut} checked={f[k]&&!f.optOut} onChange={e=>setF({...f,[k]:e.target.checked})}/>{label}</label>;
  return <><div className="modal-heading"><h2>Message consent · {g?.name||bk!.guest}</h2><p>{c.updatedAt?`Last changed ${shortDate(c.updatedAt.slice(0,10))} ${clockTime(c.updatedAt)}`:'Not recorded yet — all channels allowed for stay messages by default.'}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'MSG_CONSENT',guestKey:key,whatsapp:f.whatsapp,sms:f.sms,email:f.email,optOut:f.optOut,key:modal!.key},'Consent saved',{close:true});}}>
      <fieldset className="msg-fieldset"><legend>Allowed channels</legend>{row('whatsapp','WhatsApp')}{row('sms','SMS')}{row('email','Email')}</fieldset>
      <label className="msg-switch msg-optout"><input type="checkbox" checked={f.optOut} onChange={e=>setF({...f,optOut:e.target.checked})}/>Guest opted out of all messages</label>
      {f.optOut&&<div className="info-note msg-warn"><AlertTriangle size={16}/>No message will be queued; queued messages not yet sent are stopped and kept as “Not sent”.</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save consent</button></div>
    </form></>;
}

/** 'msg-doc' — id = capture ID. Review sheet (phone) / dialog. */
export function MsgDocModal({id}:{id?:string}){
  const {s,display}=useHcc();
  const d=(msgSlice(s).docs||[]).find(x=>x.id===id);
  const steps=useMemo(()=>['Pick sample','Extract','Review & confirm'],[]);
  if(!d)return <Empty title="This capture is unavailable"/>;
  return <><div className="modal-heading"><h2>Review capture · {d.id}</h2><p><MsgKindLabel kind={d.kind}/> · {d.title}</p></div>
    {display.isMobile&&<Stepper steps={steps} current={d.status==='Extracted'?2:3}/>}
    <MsgSimulated>Fields come from a fixture — no real OCR, no camera, no upload.</MsgSimulated>
    {d.status==='Extracted'?<MsgDocReview doc={d} sheet/>:<div className="info-note"><Info size={16}/>This capture is {d.status.toLowerCase()}{d.appliedTo?` → ${d.appliedTo}`:''}.</div>}
  </>;
}
