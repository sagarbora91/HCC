// Module 24 · WhatsApp / notifications (simulated). Desktop page + phone page.
import React,{useState} from 'react';
import {bookingRef,bookingRefOf} from '../../domain/bookingRef';
import {Send,FastForward,Zap,Pencil,ShieldOff,ShieldCheck} from 'lucide-react';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,clockTime,shortDate} from '../../components/ui';
import {ListCard,KpiCard,ChipFilter,Fab} from '../../app/mobile/components';
import {MSG_CHANNELS,MSG_EVENT_LABEL,MSGChannel,msgOutbox,msgCounts,msgPendingTriggers,msgRules,msgTemplates,msgTemplate,msgValues,msgRender,msgStatus,msgDone,
  msgConsent,msgGuestKey,msgSmsSegments,Booking} from '../../services/mock';
import {MsgBubble,MsgStatusPill,MsgSimulated,msgChannelIcon,msgTemplateName} from './shared';

const tabs=['Outbox','Triggers','Templates','Consent'] as const;
type Tab=typeof tabs[number];
/** Bookings whose guests are listed for consent: in house + confirmed (one row per guest key). */
function consentRows(s:ReturnType<typeof useHcc>['s']){
  const seen=new Set<string>();const out:{key:string;b:Booking}[]=[];
  for(const b of s.bookings.filter(b=>b.status==='In house'||b.status==='Confirmed')){const k=msgGuestKey(b);if(!seen.has(k)){seen.add(k);out.push({key:k,b});}}
  return out;
}
const consentText=(c:ReturnType<typeof msgConsent>)=>c.optOut?'Opted out':MSG_CHANNELS.filter(ch=>ch==='WhatsApp'?c.whatsapp:ch==='SMS'?c.sms:c.email).join(' · ')||'No channels';

function useMsgActions(){
  const {s,run,busy,permission}=useHcc();
  const pending=msgPendingTriggers(s);
  const unfinished=msgOutbox(s).filter(m=>!msgDone(m,s.clock)).length;
  return {pending,unfinished,
    queueAll:()=>run({type:'MSG_RUN_RULES',only:pending.map(t=>t.key),key:'rules:'+pending.map(t=>t.key).join(',')},`${pending.length} trigger${pending.length===1?'':'s'} processed (simulated)`),
    simulateAll:()=>run({type:'MSG_SIMULATE'},'Advanced one simulated delivery step'),
    canAct:permission('reservations'),busy};
}

export function MsgMessagesPage(){
  const {s,open,run,permission,busy}=useHcc();
  const [tab,setTab]=useState<Tab>('Outbox');
  const a=useMsgActions();const c=msgCounts(s);const list=msgOutbox(s);
  const canEdit=permission('approve');
  const sample=s.bookings.find(b=>b.status==='In house')||s.bookings[0];
  return <><PageTitle title="Messages" sub="Guest notifications — templates, consent, outbox and trigger rules. Simulated: nothing is ever sent."
      action={<div className="inline">{a.canAct&&<button className="btn" disabled={busy||!a.unfinished} onClick={a.simulateAll}><FastForward size={16}/> Simulate delivery</button>}
        {a.canAct&&<button className="btn primary" disabled={busy} onClick={()=>open({type:'msg-compose'})}><Send size={16}/> Compose</button>}</div>}/>
    <MsgSimulated/>
    <div className="small-stats msg-stats"><div><span>Queued / sent</span><strong>{c.Queued+c.Sent}</strong></div><div><span>Delivered / read</span><strong>{c.Delivered+c.Read}</strong></div>
      <div><span>Failed</span><strong>{c.Failed}</strong></div><div><span>Not sent (consent)</span><strong>{c['Not sent']}</strong></div><div><span>Pending triggers</span><strong>{a.pending.length}</strong></div></div>
    <section className="card">
      <div className="tabs" role="tablist" aria-label="Messages sections">{tabs.map(t=><button key={t} role="tab" aria-selected={t===tab} className={t===tab?'active':''} onClick={()=>setTab(t)}>{t}
        <span>{t==='Outbox'?list.length:t==='Triggers'?a.pending.length:t==='Templates'?msgTemplates(s).length:consentRows(s).length}</span></button>)}</div>
      {tab==='Outbox'&&<div className="table-wrap">{!list.length?<Empty title="Outbox is empty" body="Queued, sent and failed messages appear here."/>:
        <table><thead><tr><th>Queued</th><th>Guest</th><th>Channel</th><th>Message</th><th>Status</th><th>Action</th></tr></thead><tbody>
          {list.map(m=>{const st=msgStatus(m,s.clock);return <tr key={m.id}>
            <td>{shortDate(m.queuedAt.slice(0,10))} {clockTime(m.queuedAt)}<small>{m.id}</small></td>
            <td><strong>{m.guest}</strong><small>{bookingRefOf(s.bookings,m.booking)} · {m.to||'no recipient'}</small></td>
            <td><span className="msg-kind">{msgChannelIcon(m.channel)}{m.channel}</span></td>
            <td className="msg-cell-body"><strong>{msgTemplateName(s,m.template)}</strong><small>{m.body}</small>{m.blocked&&<small className="f-why">{m.blocked}</small>}</td>
            <td><MsgStatusPill st={st}/></td>
            <td><div className="inline"><button className="text-btn" onClick={()=>open({type:'msg-message',id:m.id})}>View</button>
              {a.canAct&&!msgDone(m,s.clock)&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'MSG_SIMULATE',id:m.id},'Advanced one simulated step')}>Simulate</button>}
              {a.canAct&&st==='Failed'&&!m.retriedBy&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'MSG_RETRY',id:m.id,key:'retry:'+m.id},'Retry queued (simulated)')}>Retry</button>}</div></td></tr>;})}
        </tbody></table>}</div>}
      {tab==='Triggers'&&<>
        <div className="section-title"><h2>Pending triggers</h2>{a.canAct&&<button className="btn primary" disabled={busy||!a.pending.length} onClick={a.queueAll}><Zap size={16}/> Queue {a.pending.length||''} now</button>}</div>
        {!a.pending.length?<Empty title="No pending triggers" body="New bookings, check-ins, payments and checkouts create triggers here."/>:
          <div className="table-wrap"><table><thead><tr><th>Event</th><th>Guest</th><th>Template</th><th>Channel</th><th>Consent</th></tr></thead><tbody>
            {a.pending.map(t=>{const c=msgConsent(s,msgGuestKey(t.booking));return <tr key={t.key}><td><strong>{t.label}</strong><small>rule {t.rule.id} · {t.key}</small></td><td>{t.booking.guest}<small>{bookingRef(t.booking)} · room {t.booking.room}</small></td>
              <td>{msgTemplate(s,t.rule.template)?.name}</td><td>{t.rule.channel}</td><td>{c.optOut?<Pill tone="orange">Opted out · will not send</Pill>:consentText(c)}</td></tr>;})}
          </tbody></table></div>}
        <div className="section-title"><h2>Trigger rules</h2><span className="muted">{canEdit?'Owner / Manager can change rules':'View only — Owner or Manager changes rules'}</span></div>
        <div className="table-wrap"><table><thead><tr><th>Rule</th><th>When</th><th>Template</th><th>Channel</th><th>On</th></tr></thead><tbody>
          {msgRules(s).map(r=><tr key={r.id}><td><strong>{r.id}</strong></td><td>{MSG_EVENT_LABEL[r.event]}</td><td>{msgTemplate(s,r.template)?.name}</td>
            <td>{canEdit?<select aria-label={`Channel for rule ${r.id}`} value={r.channel} disabled={busy} onChange={e=>run({type:'MSG_RULE',id:r.id,channel:e.target.value},'Rule updated')}>{MSG_CHANNELS.map(c=><option key={c}>{c}</option>)}</select>:r.channel}</td>
            <td>{canEdit?<label className="msg-switch"><input type="checkbox" checked={r.enabled} disabled={busy} onChange={e=>run({type:'MSG_RULE',id:r.id,enabled:e.target.checked},e.target.checked?'Rule on':'Rule off')}/>{r.enabled?'On':'Off'}</label>:<Pill tone={r.enabled?'green':'neutral'}>{r.enabled?'On':'Off'}</Pill>}</td></tr>)}
        </tbody></table></div></>}
      {tab==='Templates'&&<div className="msg-templates">{msgTemplates(s).map(t=>{const p=sample?msgRender(t.body,msgValues(s,sample)).text:t.body;return <article key={t.id} className="msg-template">
        <div className="section-title"><h3>{t.name}</h3><span className="msg-kind">{msgChannelIcon(t.channel)}{t.channel}</span></div>
        <code className="msg-code">{t.body}</code>
        <MsgBubble m={{id:t.id,template:t.id,channel:t.channel,booking:sample?.id||'',guestKey:'',guest:sample?.guest||'Guest',to:'preview',body:p,subject:t.channel==='Email'&&sample?msgRender(t.subject,msgValues(s,sample)).text:undefined,queuedAt:s.clock,outcome:'Delivered',actor:'',blocked:undefined}}/>
        <div className="inline"><small className="muted">{p.length} chars{t.channel==='SMS'?` · ${msgSmsSegments(p)} segment(s)`:''}</small>{canEdit&&<button className="btn small-btn" onClick={()=>open({type:'msg-template',id:t.id})}><Pencil size={14}/> Edit</button>}</div>
      </article>;})}</div>}
      {tab==='Consent'&&<div className="table-wrap">{!consentRows(s).length?<Empty title="No current guests" body="In-house and confirmed guests appear here."/>:
        <table><thead><tr><th>Guest</th><th>Stay</th><th>Allowed channels</th><th>Updated</th><th>Action</th></tr></thead><tbody>
        {consentRows(s).map(({key,b})=>{const c=msgConsent(s,key);return <tr key={key}><td><strong>{b.guest}</strong><small>{key.startsWith('B:')?'No profile':key}</small></td><td>{b.status} · room {b.room}</td>
          <td>{c.optOut?<Pill tone="orange"><ShieldOff size={13}/> Opted out</Pill>:<Pill tone="green"><ShieldCheck size={13}/> {consentText(c)}</Pill>}</td>
          <td>{c.updatedAt?`${shortDate(c.updatedAt.slice(0,10))} ${clockTime(c.updatedAt)}`:'Default'}</td>
          <td>{a.canAct&&<button className="text-btn" onClick={()=>open({type:'msg-consent',id:key})}>Change</button>}</td></tr>;})}
        </tbody></table>}</div>}
    </section></>;
}

const mtabs=[{id:'outbox',label:'Outbox'},{id:'triggers',label:'Triggers'},{id:'consent',label:'Consent'}] as const;
type MTab=typeof mtabs[number]['id'];
export function MsgMessagesMobile(){
  const {s,open,run,busy}=useHcc();
  const [tab,setTab]=useState<MTab>('outbox');const [ch,setCh]=useState<'all'|MSGChannel>('all');
  const a=useMsgActions();const c=msgCounts(s);
  const list=msgOutbox(s).filter(m=>ch==='all'||m.channel===ch);
  const rows=consentRows(s);
  return <div className="msg-m">
    <MsgSimulated/>
    <div className="msg-m-kpis"><KpiCard label="Delivered / read" value={c.Delivered+c.Read}/><KpiCard label="Failed" value={c.Failed}/><KpiCard label="Pending triggers" value={a.pending.length} onClick={()=>setTab('triggers')}/></div>
    <ChipFilter label="Messages section" value={tab} onChange={setTab} options={mtabs.map(t=>({id:t.id,label:t.label,count:t.id==='outbox'?msgOutbox(s).length:t.id==='triggers'?a.pending.length:rows.length}))}/>
    {tab==='outbox'&&<>
      <ChipFilter label="Channel" value={ch} onChange={setCh} options={[{id:'all',label:'All'},...MSG_CHANNELS.map(x=>({id:x,label:x}))]}/>
      {a.canAct&&a.unfinished>0&&<button type="button" className="btn full msg-m-btn" disabled={busy} onClick={a.simulateAll}><FastForward size={18}/> Simulate delivery ({a.unfinished})</button>}
      {!list.length?<Empty title="No messages" body="Compose a message or queue triggers."/>:<div className="msg-m-thread">{list.map(m=><MsgBubble key={m.id} m={m} onClick={()=>open({type:'msg-message',id:m.id})}/>)}</div>}
    </>}
    {tab==='triggers'&&<>
      {a.canAct&&<button type="button" className="btn primary full msg-m-btn" disabled={busy||!a.pending.length} onClick={a.queueAll}><Zap size={18}/> Queue {a.pending.length} trigger{a.pending.length===1?'':'s'}</button>}
      {!a.pending.length?<Empty title="No pending triggers" body="New bookings, check-ins, payments and checkouts create triggers."/>:
        a.pending.map(t=><ListCard key={t.key} title={t.booking.guest} sub={`${t.label} · ${msgTemplate(s,t.rule.template)?.name}`} meta={`${t.rule.channel} · rule ${t.rule.id}`}
          right={msgConsent(s,msgGuestKey(t.booking)).optOut?<Pill tone="orange">Opted out</Pill>:<Pill tone="blue">Ready</Pill>}/>)}
      <h2 className="msg-m-h">Rules</h2>
      {msgRules(s).map(r=><ListCard key={r.id} title={`${r.id} · ${MSG_EVENT_LABEL[r.event]}`} sub={`${msgTemplate(s,r.template)?.name} · ${r.channel}`} right={<Pill tone={r.enabled?'green':'neutral'}>{r.enabled?'On':'Off'}</Pill>}/>)}
      <p className="muted">Rules are changed on desktop by the Owner or Manager.</p>
    </>}
    {tab==='consent'&&(!rows.length?<Empty title="No current guests"/>:rows.map(({key,b})=>{const x=msgConsent(s,key);
      return <ListCard key={key} title={b.guest} sub={`${b.status} · room ${b.room}`} meta={consentText(x)} right={<Pill tone={x.optOut?'orange':'green'}>{x.optOut?'Opted out':'OK'}</Pill>}
        onClick={a.canAct?()=>open({type:'msg-consent',id:key}):undefined}/>;}))}
    {a.canAct&&<Fab label="Compose" icon={Send} onClick={()=>open({type:'msg-compose'})}/>}
  </div>;
}
