// Module 24 · WhatsApp / notifications (simulated). Desktop page + phone page.
import {NetBanner,netBannerText,netHeldOf} from '../admin/InternetPanel';
import React,{useState} from 'react';
import {bookingRef,bookingRefOf} from '../../domain/bookingRef';
import {Send,FastForward,Zap,Pencil,ShieldOff,ShieldCheck} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,Empty,clockTime,shortDate} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,StatStrip} from '../../components/fit';
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
import {ListCard,KpiCard,ChipFilter,Fab} from '../../app/mobile/components';
import {MSG_CHANNELS,MSG_EVENT_LABEL,MSGChannel,msgOutbox,msgCounts,msgPendingTriggers,msgRules,msgTemplates,msgTemplate,msgValues,msgRender,msgStatus,msgDone,
  msgConsent,msgGuestKey,msgSmsSegments,Booking,rbacCanEditTemplates,rbacTechAdminTemplates} from '../../services/mock';
import {MsgBubble,MsgStatusPill,MsgSimulated,MsgSimulatedNote,msgChannelIcon,msgTemplateName} from './shared';

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

// FIT (DEC-194…196, contract §6 `messages` = tabs): FitPage header (Compose primary, Simulate delivery second), the five counters as a StatStrip,
// the four sections as sub-screens (FitTabs; ids = the lower-case old labels), each section in a ScrollPanel. The Outbox (1,350 rows on the
// FY26 scenario) renders a page at a time (usePaged + ShowMore in the panel footer). A message row keeps opening its existing dialog.
const DESK_TAB_IDS=['outbox','triggers','templates','consent'] as const;
type DeskTab=typeof DESK_TAB_IDS[number];
const DESK_TAB_LABEL:Record<DeskTab,string>={outbox:'Outbox',triggers:'Triggers',templates:'Templates',consent:'Consent'};
export function MsgMessagesPage(){
  const {s,uid,open,busy}=useHcc();
  // DEC-079 D4: the Tech admin (production) sees and edits templates and rules only — no outbox, consent or sending.
  const templatesOnly=rbacTechAdminTemplates(s,uid);
  const shown:readonly DeskTab[]=templatesOnly?['triggers','templates']:DESK_TAB_IDS;
  const [tab,setTab]=useFitTab<DeskTab>('messages',shown,templatesOnly?'templates':'outbox');
  const a=useMsgActions();const c=msgCounts(s);const list=msgOutbox(s);
  const count=(t:DeskTab)=>t==='outbox'?list.length:t==='triggers'?a.pending.length:t==='templates'?msgTemplates(s).length:consentRows(s).length;
  return <FitPage title="Messages" sub={netBannerText(s,'messaging')+'Guest notifications — templates, consent, outbox and trigger rules. Simulated: nothing is ever sent.'}
      actions={<>{a.canAct&&<button className="btn primary" disabled={busy} onClick={()=>open({type:'msg-compose'})}><Send size={16}/> Compose</button>}
        {a.canAct&&<button className="btn" disabled={busy||!a.unfinished} onClick={a.simulateAll}><FastForward size={16}/> Simulate delivery</button>}</>}
      toolbar={<StatStrip items={[{title:'Queued / sent',value:String(c.Queued+c.Sent)},{title:'Delivered / read',value:String(c.Delivered+c.Read)},{title:'Failed',value:String(c.Failed)},
        {title:'Not sent (consent)',value:String(c['Not sent'])},{title:'Pending triggers',value:String(a.pending.length)}]} variant="small" className="msg-stats" compact collapseOnTablet/>}
      tabs={<FitTabs<DeskTab> page="messages" value={tab} onChange={setTab} tabs={shown.map(id=>({id,label:DESK_TAB_LABEL[id],count:count(id),badge:id==='outbox'&&netHeldOf(s,'messaging')>0?'warn' as const:undefined}))}/>}>
    {/* S2: the "Simulated" (demo) / "Messaging is off" (production) note on every tab, for every role (the Tech admin sees only Triggers / Templates) */}
    {/* FIT-2 integrator: on the Outbox the note shares the panel footer line with "Show more" (one more list row at tab-land) */}
    {tab!=='outbox'&&<MsgSimulatedNote/>}
    {tab==='outbox'&&<OutboxPanel/>}
    {tab==='triggers'&&<TriggersPanels/>}
    {tab==='templates'&&<TemplatesPanel/>}
    {tab==='consent'&&<ConsentPanel/>}
  </FitPage>;
}

function OutboxPanel(){
  const {s,open,run,busy}=useHcc();const a=useMsgActions();const list=msgOutbox(s);
  const paged=usePaged(list,DESKTOP_PAGE);
  return <ScrollPanel label="Outbox" footer={<>{paged.more&&<ShowMore p={paged}/>}<MsgSimulatedNote/></>}>
    {!list.length?<Empty title="Outbox is empty" body="Queued, sent and failed messages appear here."/>:<div className="table-wrap">
        <table><thead><tr><th>Queued</th><th>Guest</th><th>Channel</th><th>Message</th><th>Status</th><th>Action</th></tr></thead><tbody>
          {paged.shown.map(m=>{const st=msgStatus(m,s.clock);return <tr key={m.id}>
            <td>{shortDate(m.queuedAt.slice(0,10))} {clockTime(m.queuedAt)}<small>{m.id}</small></td>
            <td><strong>{m.guest}</strong><small>{bookingRefOf(s.bookings,m.booking)} · {m.to||'no recipient'}</small></td>
            <td><span className="msg-kind">{msgChannelIcon(m.channel)}{m.channel}</span></td>
            <td className="msg-cell-body"><strong>{msgTemplateName(s,m.template)}</strong><small>{m.body}</small>{m.blocked&&<small className="f-why">{m.blocked}</small>}</td>
            <td><MsgStatusPill st={st}/></td>
            <td><div className="inline"><button className="text-btn" onClick={()=>open({type:'msg-message',id:m.id})}>View</button>
              {a.canAct&&!msgDone(m,s.clock)&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'MSG_SIMULATE',id:m.id},'Advanced one simulated step')}>Simulate</button>}
              {a.canAct&&st==='Failed'&&!m.retriedBy&&<button className="text-btn" disabled={busy} onClick={()=>run({type:'MSG_RETRY',id:m.id,key:'retry:'+m.id},'Retry queued (simulated)')}>Retry</button>}</div></td></tr>;})}
        </tbody></table></div>}
  </ScrollPanel>;
}

function TriggersPanels(){
  const {s,uid,run,busy}=useHcc();const a=useMsgActions();const canEdit=rbacCanEditTemplates(s,uid);
  // One panel, two sections (the old page's order): two stacked panels would each be under the 160 px minimum on a 640 px tablet.
  return <ScrollPanel label="Triggers and rules">
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
        </tbody></table></div>
  </ScrollPanel>;
}

function TemplatesPanel(){
  const {s,uid,open}=useHcc();const canEdit=rbacCanEditTemplates(s,uid);
  const sample=s.bookings.find(b=>b.status==='In house')||s.bookings[0];
  return <ScrollPanel label="Message templates"><div className="msg-templates">{msgTemplates(s).map(t=>{const p=sample?msgRender(t.body,msgValues(s,sample)).text:t.body;return <article key={t.id} className="msg-template">
        <div className="section-title"><h3>{t.name}</h3><span className="msg-kind">{msgChannelIcon(t.channel)}{t.channel}</span></div>
        <code className="msg-code">{t.body}</code>
        <MsgBubble m={{id:t.id,template:t.id,channel:t.channel,booking:sample?.id||'',guestKey:'',guest:sample?.guest||'Guest',to:'preview',body:p,subject:t.channel==='Email'&&sample?msgRender(t.subject,msgValues(s,sample)).text:undefined,queuedAt:s.clock,outcome:'Delivered',actor:'',blocked:undefined}}/>
        <div className="inline"><small className="muted">{p.length} chars{t.channel==='SMS'?` · ${msgSmsSegments(p)} segment(s)`:''}</small>{canEdit&&<button className="btn small-btn" onClick={()=>open({type:'msg-template',id:t.id})}><Pencil size={14}/> Edit</button>}</div>
      </article>;})}</div></ScrollPanel>;
}

function ConsentPanel(){
  const {s,open}=useHcc();const a=useMsgActions();const rows=consentRows(s);
  return <ScrollPanel label="Guest consent">{!rows.length?<Empty title="No current guests" body="In-house and confirmed guests appear here."/>:<div className="table-wrap">
        <table><thead><tr><th>Guest</th><th>Stay</th><th>Allowed channels</th><th>Updated</th><th>Action</th></tr></thead><tbody>
        {rows.map(({key,b})=>{const c=msgConsent(s,key);return <tr key={key}><td><strong>{b.guest}</strong><small>{key.startsWith('B:')?'No profile':key}</small></td><td>{b.status} · room {b.room}</td>
          <td>{c.optOut?<Pill tone="orange"><ShieldOff size={13}/> Opted out</Pill>:<Pill tone="green"><ShieldCheck size={13}/> {consentText(c)}</Pill>}</td>
          <td>{c.updatedAt?`${shortDate(c.updatedAt.slice(0,10))} ${clockTime(c.updatedAt)}`:'Default'}</td>
          <td>{a.canAct&&<button className="text-btn" onClick={()=>open({type:'msg-consent',id:key})}>Change</button>}</td></tr>;})}
        </tbody></table></div>}</ScrollPanel>;
}

const mtabs=[{id:'outbox',label:'Outbox'},{id:'triggers',label:'Triggers'},{id:'templates',label:'Templates'},{id:'consent',label:'Consent'}] as const;
type MTab=typeof mtabs[number]['id'];
export function MsgMessagesMobile(){
  const {s,uid,open,run,busy}=useHcc();
  // DEC-079 D4 (Wave D5, B8 leftover): as on desktop, the Tech admin (production) gets Templates and Triggers only, opening on Templates.
  const templatesOnly=rbacTechAdminTemplates(s,uid);const canEdit=rbacCanEditTemplates(s,uid);
  const [tab,setTab]=useState<MTab>(templatesOnly?'templates':'outbox');const [ch,setCh]=useState<'all'|MSGChannel>('all');
  const a=useMsgActions();const c=msgCounts(s);
  const list=msgOutbox(s).filter(m=>ch==='all'||m.channel===ch);
  const rows=consentRows(s);
  const shown=templatesOnly?mtabs.filter(t=>t.id==='templates'||t.id==='triggers'):mtabs;
  const sample=s.bookings.find(b=>b.status==='In house')||s.bookings[0];
  return <div className="msg-m">
    <MsgSimulated/>
    <NetBanner kind="messaging"/>
    <div className="msg-m-kpis"><KpiCard label="Delivered / read" value={c.Delivered+c.Read}/><KpiCard label="Failed" value={c.Failed}/><KpiCard label="Pending triggers" value={a.pending.length} onClick={()=>setTab('triggers')}/></div>
    <ChipFilter label="Messages section" value={tab} onChange={setTab} options={shown.map(t=>({id:t.id,label:t.label,count:t.id==='outbox'?msgOutbox(s).length:t.id==='triggers'?a.pending.length:t.id==='templates'?msgTemplates(s).length:rows.length}))}/>
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
      <p className="muted">{canEdit?'Rules are changed on desktop.':'Rules are changed on desktop by the Owner or Manager.'}</p>
    </>}
    {tab==='templates'&&<>
      <p className="muted">{canEdit?'Tap a template to edit it. The preview uses a guest in house.':'View only — the Owner or Manager changes templates.'}</p>
      {msgTemplates(s).map(t=>{const p=sample?msgRender(t.body,msgValues(s,sample)).text:t.body;
        return <ListCard key={t.id} title={t.name} sub={`${t.channel} · ${p.length} chars${t.channel==='SMS'?` · ${msgSmsSegments(p)} segment(s)`:''}`} meta={p}
          right={canEdit?<Pill tone="blue"><Pencil size={13}/> Edit</Pill>:undefined} onClick={canEdit?()=>open({type:'msg-template',id:t.id}):undefined}/>;})}
    </>}
    {tab==='consent'&&(!rows.length?<Empty title="No current guests"/>:rows.map(({key,b})=>{const x=msgConsent(s,key);
      return <ListCard key={key} title={b.guest} sub={`${b.status} · room ${b.room}`} meta={consentText(x)} right={<Pill tone={x.optOut?'orange':'green'}>{x.optOut?'Opted out':'OK'}</Pill>}
        onClick={a.canAct?()=>open({type:'msg-consent',id:key}):undefined}/>;}))}
    {a.canAct&&<Fab label="Compose" icon={Send} onClick={()=>open({type:'msg-compose'})}/>}
  </div>;
}
