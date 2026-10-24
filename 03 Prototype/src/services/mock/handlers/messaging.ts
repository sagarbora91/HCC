// Module 24–25 · Notifications & document capture — command handlers. Command types MUST start with 'MSG_'. Return value = audit/summary text.
// ALL SIMULATED: queuing a message never sends anything; "extraction" copies fixture fields; no image or full ID number is stored.
import type {Handler,Kit} from '../commands';
import type {Booking} from '../../../domain/types';
import {can} from '../../../domain/selectors';
import {addMinutes} from '../../../domain/money';
import {registry} from './index';
import {guestIdTypes,guestValidate} from '../../../domain/streams/guests';
import {EXPENSE_CATEGORIES,EXPENSE_METHODS} from '../../../domain/streams/closing';
import {msgSampleDocs} from '../../../fixtures/ext/messaging';
import {MSGState,MSGChannel,MSGMessage,MSG_CHANNELS,MSG_DEFAULT_TEMPLATES,MSG_DEFAULT_RULES,MSG_DEFAULT_CONSENT,msgTemplate,msgValues,msgRender,msgRecipient,
  msgBlockReason,msgGuestKey,msgOutcome,msgPendingTriggers,msgStatus,msgNextBoost,msgUnknownPlaceholders,msgLowFields,msgLooksLikeFullId,msgParseRupees,
  MSG_DOC_KIND_LABEL} from '../../../domain/streams/messaging';

/** The slice, created on first write (copies defaults so old saves keep working). */
function slice(k:Kit):Required<MSGState>{
  const x=(k.s.messagingX=k.s.messagingX||{});
  x.templates=x.templates||MSG_DEFAULT_TEMPLATES.map(t=>({...t}));x.rules=x.rules||MSG_DEFAULT_RULES.map(r=>({...r}));
  x.consent=x.consent||{};x.outbox=x.outbox||[];x.fired=x.fired||[];x.docs=x.docs||[];
  return x as Required<MSGState>;
}
const channelOk=(k:Kit,c:unknown)=>k.assert(MSG_CHANNELS.includes(c as MSGChannel),'Choose WhatsApp, SMS or Email.');
/** Build (and store) a message. blocked → recorded as Not sent. */
function queue(k:Kit,b:Booking,channel:MSGChannel,template:MSGMessage['template'],body:string,subject:string|undefined,extra:Partial<MSGMessage>={}):MSGMessage{
  const x=slice(k),id=k.id('MSG'),to=msgRecipient(k.s,b,channel);
  const m:MSGMessage={id,template,channel,booking:b.id,guestKey:msgGuestKey(b),guest:b.guest,to:to.masked,body,queuedAt:addMinutes(k.s.clock,1),
    outcome:msgOutcome(id,channel,to.valid),actor:k.uid,...extra};
  if(channel==='Email'&&subject)m.subject=subject;
  x.outbox.push(m);return m;
}

export const handlers:Record<string,Handler>={
  /** Manual compose to an in-house guest. {booking, channel, body, template?, subject?} */
  MSG_SEND:k=>{
    const {cmd,s,assert}=k;k.req('reservations');channelOk(k,cmd.channel);
    const b=s.bookings.find(b=>b.id===cmd.booking);assert(b,'Choose an in-house guest.','NOT_FOUND');
    assert(b!.status==='In house','Manual messages go only to in-house guests.','INVALID_TRANSITION');
    const body=String(cmd.body??'').trim();assert(body.length>=1&&body.length<=600,'Write a message of 1–600 characters.');
    const unknown=msgUnknownPlaceholders(body);assert(!unknown.length,`Unknown placeholder: {${unknown.join('}, {')}}.`);
    const why=msgBlockReason(s,b!,cmd.channel);assert(!why,why+' — the message was not queued.');
    const v=msgValues(s,b!);const tpl=cmd.template&&msgTemplate(s,cmd.template)?cmd.template:'manual';
    const m=queue(k,b!,cmd.channel,tpl,msgRender(body,v).text,cmd.subject?msgRender(String(cmd.subject),v).text:undefined);
    return `${m.id} · ${m.channel} to ${b!.guest} (${m.to}) · simulated, nothing sent`;
  },
  /** Queue every pending trigger (or only the listed keys). Idempotent through `fired`. */
  MSG_RUN_RULES:k=>{
    const {cmd,s}=k;k.req('reservations');const x=slice(k);
    const only:string[]|undefined=Array.isArray(cmd.only)?cmd.only:undefined;
    const list=msgPendingTriggers(s).filter(t=>!only||only.includes(t.key));
    k.assert(list.length,'There are no pending triggers to queue.','INVALID_TRANSITION');
    let queued=0,blocked=0;
    for(const t of list){
      const tpl=msgTemplate(s,t.rule.template)!,v=msgValues(s,t.booking,t.receipt),why=msgBlockReason(s,t.booking,t.rule.channel);
      queue(k,t.booking,t.rule.channel,tpl.id,msgRender(tpl.body,v).text,msgRender(tpl.subject,v).text,{rule:t.rule.id,...(why?{blocked:why}:{})});
      x.fired.push(t.key);if(why)blocked++;else queued++;
    }
    return `${queued} queued · ${blocked} not sent (consent/recipient) · simulated`;
  },
  /** Advance one message (id) or every unfinished message by one delivery step. */
  MSG_SIMULATE:k=>{
    const {cmd,s}=k;k.req('reservations');const x=slice(k);
    const list=cmd.id?x.outbox.filter(m=>m.id===cmd.id):x.outbox;
    k.assert(list.length,'Message not found.','NOT_FOUND');
    let n=0;for(const m of list){const add=msgNextBoost(m,s.clock);if(add>0){m.boost=(m.boost||0)+add;n++;}}
    k.assert(n,'Delivery simulation is already complete for '+(cmd.id?'this message.':'all messages.'),'INVALID_TRANSITION');
    return `${n} message${n===1?'':'s'} advanced one simulated delivery step`;
  },
  /** Retry a failed message once (new message, simulated success). */
  MSG_RETRY:k=>{
    const {cmd,s}=k;k.req('reservations');const x=slice(k);
    const m=x.outbox.find(m=>m.id===cmd.id);k.assert(m,'Message not found.','NOT_FOUND');
    k.assert(msgStatus(m!,s.clock)==='Failed','Only a failed message can be retried.','INVALID_TRANSITION');
    k.assert(!m!.retriedBy,'This message was already retried.','INVALID_TRANSITION');
    const b=s.bookings.find(b=>b.id===m!.booking);k.assert(b,'Booking not found.','NOT_FOUND');
    const why=msgBlockReason(s,b!,m!.channel);k.assert(!why,why+' — not retried.');
    const r=queue(k,b!,m!.channel,m!.template,m!.body,m!.subject,{retryOf:m!.id,...(m!.rule?{rule:m!.rule}:{})});
    r.outcome=m!.channel==='WhatsApp'?'Read':'Delivered';m!.retriedBy=r.id;
    return `${m!.id} retried as ${r.id} · simulated`;
  },
  /** Consent / opt-out per guest profile (or B:<booking>). Opt-out stops still-queued messages. */
  MSG_CONSENT:k=>{
    const {cmd,s,uid}=k;if(!can(s,uid,'reservations'))k.req('checkin');const x=slice(k);
    const key=String(cmd.guestKey||'');
    const known=(s.guests||[]).some(g=>g.id===key)||key.startsWith('B:')&&s.bookings.some(b=>'B:'+b.id===key);
    k.assert(known,'Guest not found.','NOT_FOUND');
    const prev=x.consent[key]??MSG_DEFAULT_CONSENT;
    const c={whatsapp:cmd.whatsapp??prev.whatsapp,sms:cmd.sms??prev.sms,email:cmd.email??prev.email,optOut:cmd.optOut??prev.optOut,updatedAt:s.clock,by:uid};
    ['whatsapp','sms','email','optOut'].forEach(f=>k.assert(typeof (c as any)[f]==='boolean','Consent flags must be yes or no.'));
    x.consent[key]=c;let stopped=0;
    for(const m of x.outbox)if(m.guestKey===key&&!m.blocked&&msgStatus(m,s.clock)==='Queued'){
      const ch=m.channel==='WhatsApp'?c.whatsapp:m.channel==='SMS'?c.sms:c.email;
      if(c.optOut||!ch){m.blocked=c.optOut?'Guest opted out before sending':`${m.channel} consent withdrawn before sending`;stopped++;}
    }
    return `${key} · ${c.optOut?'opted out':'WhatsApp '+(c.whatsapp?'yes':'no')+' · SMS '+(c.sms?'yes':'no')+' · Email '+(c.email?'yes':'no')}${stopped?` · ${stopped} queued stopped`:''}`;
  },
  /** Edit a template. {id, body, subject?, channel?} */
  MSG_TEMPLATE:k=>{
    const {cmd}=k;k.req('approve');const x=slice(k);
    const t=x.templates.find(t=>t.id===cmd.id);k.assert(t,'Template not found.','NOT_FOUND');
    const body=String(cmd.body??'').trim(),subject=String(cmd.subject??t!.subject).trim();
    k.assert(body.length>=10&&body.length<=600,'Template text must be 10–600 characters.');
    k.assert(subject.length>=3&&subject.length<=120,'Subject must be 3–120 characters.');
    const unknown=[...msgUnknownPlaceholders(body),...msgUnknownPlaceholders(subject)];
    k.assert(!unknown.length,`Unknown placeholder: {${unknown.join('}, {')}}. Use only the listed placeholders.`);
    if(cmd.channel!==undefined)channelOk(k,cmd.channel);
    t!.body=body;t!.subject=subject;if(cmd.channel)t!.channel=cmd.channel;
    return `${t!.name} template updated`;
  },
  /** Enable/disable a rule or change its channel. {id, enabled?, channel?} */
  MSG_RULE:k=>{
    const {cmd}=k;k.req('approve');const x=slice(k);
    const r=x.rules.find(r=>r.id===cmd.id);k.assert(r,'Rule not found.','NOT_FOUND');
    if(cmd.channel!==undefined){channelOk(k,cmd.channel);r!.channel=cmd.channel;}
    if(cmd.enabled!==undefined){k.assert(typeof cmd.enabled==='boolean','Choose on or off.');r!.enabled=cmd.enabled;}
    return `Rule ${r!.id} · ${r!.enabled?'on':'off'} · ${r!.channel}`;
  },
  /** Simulated extraction from a fictional sample. {sample} */
  MSG_DOC_EXTRACT:k=>{
    const {cmd,s}=k;k.req('checkin');const x=slice(k);
    const smp=msgSampleDocs.find(d=>d.id===cmd.sample);k.assert(smp,'Choose a sample document.','NOT_FOUND');
    const id=k.id('DOC');
    x.docs.push({id,sample:smp!.id,kind:smp!.kind,title:smp!.title,status:'Extracted',fields:smp!.fields.map(f=>({...f})),actor:k.uid,at:s.clock});
    return `${id} · ${MSG_DOC_KIND_LABEL[smp!.kind]} · ${smp!.fields.length} fields extracted (simulated OCR, ${msgLowFields(smp!.fields).length} need review)`;
  },
  /** Confirm reviewed fields. {id, fields:{key:value}, reviewed:[keys], guestId? (ID/letter), method? (bill)} */
  MSG_DOC_CONFIRM:k=>{
    const {cmd,s,assert}=k;k.req('checkin');const x=slice(k);
    const d=x.docs.find(d=>d.id===cmd.id);assert(d,'Capture not found.','NOT_FOUND');
    assert(d!.status==='Extracted',`This capture is already ${d!.status.toLowerCase()}.`,'INVALID_TRANSITION');
    const edits:Record<string,unknown>=cmd.fields&&typeof cmd.fields==='object'?cmd.fields:{};
    const reviewed:string[]=Array.isArray(cmd.reviewed)?cmd.reviewed:[];
    const next=d!.fields.map(f=>{const v=edits[f.key];return !f.locked&&typeof v==='string'&&v.trim()!==f.value?{...f,value:v.trim().slice(0,120),original:f.value}:{...f};});
    const unchecked=msgLowFields(d!.fields).filter(key=>!reviewed.includes(key)&&!next.find(f=>f.key===key)!.original);
    assert(!unchecked.length,`Review the low-confidence field${unchecked.length>1?'s':''}: ${unchecked.map(key=>d!.fields.find(f=>f.key===key)!.label).join(', ')}.`);
    const val=(key:string)=>next.find(f=>f.key===key)?.value??'';
    let target='';
    if(d!.kind==='vendor-bill'){
      const amount=msgParseRupees(val('amount'));assert(Number.isSafeInteger(amount)&&amount>0,'Enter the bill amount in rupees, e.g. 1240.00.');
      assert(EXPENSE_CATEGORIES.includes(val('category')),`Category must be one of: ${EXPENSE_CATEGORIES.join(', ')}.`);
      const method=cmd.method||'Cash';assert(EXPENSE_METHODS.includes(method),'Choose a funding source.');
      const ex=registry.EXPENSE;assert(ex,'The expense command is unavailable.','NOT_FOUND');
      const smp=msgSampleDocs.find(m=>m.id===d!.sample);
      ex!({...k,cmd:{type:'EXPENSE',vendor:val('vendor'),category:val('category'),amount,method,draft:true,
        receipt:`${smp?.file||d!.id} (simulated capture)`,notes:`Bill ${val('billNo')} dated ${val('billDate')} · from document capture ${d!.id}`}});
      target=s.expenses.at(-1)!.id;
    }else{
      const g=(s.guests||[]).find(g=>g.id===cmd.guestId);assert(g,'Choose the guest profile to update.','NOT_FOUND');
      if(d!.kind==='id-card'){
        assert(next.every(f=>f.locked||!msgLooksLikeFullId(f.value)),'Do not enter a full ID number — only the last 4 characters are kept.');
        const last4=val('idLast4').toUpperCase();
        assert(/^[A-Z0-9]{4}$/.test(last4),'ID last 4 must be exactly 4 letters or digits — never the full number.');
        assert(guestIdTypes.includes(val('idType')),`ID type must be one of: ${guestIdTypes.join(', ')}.`);
        g!.idType=val('idType');g!.idLast4=last4;
      }else{
        const e=guestValidate({...g!,company:val('company'),gstin:val('gstin')});
        assert(!e.gstin&&!e.company,e.gstin||e.company||'Check the company details.');
        assert(val('company').length>=2,'Enter the company name.');
        g!.company=val('company');g!.gstin=val('gstin').toUpperCase();
      }
      g!.version=(g!.version||1)+1;target=g!.id;
    }
    d!.fields=next;d!.status='Applied';d!.appliedTo=target;d!.appliedAt=s.clock;
    return d!.kind==='vendor-bill'?`${d!.id} → expense draft ${target} (submit it from Expenses)`:
      d!.kind==='id-card'?`${d!.id} → ${target} · ID type + last 4 only (no image, no full number)`:`${d!.id} → ${target} · company billing details`;
  },
  MSG_DOC_DISCARD:k=>{
    const {cmd,s}=k;k.req('checkin');const x=slice(k);
    const d=x.docs.find(d=>d.id===cmd.id);k.assert(d,'Capture not found.','NOT_FOUND');
    k.assert(d!.status==='Extracted',`This capture is already ${d!.status.toLowerCase()}.`,'INVALID_TRANSITION');
    d!.status='Discarded';d!.appliedAt=s.clock;if(cmd.reason?.trim())d!.reason=String(cmd.reason).trim().slice(0,200);
    return `${d!.id} discarded`;
  },
};
