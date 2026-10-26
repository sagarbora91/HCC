// Messaging area (W4-10): templates, trigger rules, consent, simulated send / outbox. ALL SENDING IS SIMULATED on the server too.
// Server: 08 Production/src/Hcc.Api/Endpoints/MessagingEndpoints.cs (+ the W4-10 additive reads: MessageView.outcome/boostMinutes/guestId and
// GET /messaging/consents).
//
//   loaders   messagingX ← GET /messaging/{messages,templates,rules,consents}: outbox oldest→newest (the screens reverse it), masked
//             recipients only, `fired` rebuilt from the messages' rule + reservation, consent keyed by guest id and aliased 'B:<booking>'
//             for registered guests of active bookings (the screens key by guestId or 'B:<booking>'). `docs` stay empty (see unmapped).
//   commands  MSG_SEND, MSG_RUN_RULES, MSG_SIMULATE, MSG_RETRY, MSG_CONSENT, MSG_TEMPLATE, MSG_RULE.
//   gaps      payment-received triggers (R4) are keyed by receipt in the prototype, by reservation on the server: they can show as
//             pending locally until the sweep is run; the server is the authority (RunMessagingTriggers is idempotent).
import type {MSGState,MSGMessage,MSGTemplate,MSGRule,MSGConsent,MSGChannel,MSGTemplateId,MSGEvent} from '../../../domain/streams/messaging';
import {HccClientError} from '../../errors';
import type {SyncArea,LoaderCtx,CommandCtx} from '../types';
import {utcToClock,serverId} from '../map';

type SrvMessage={messageId:number;id:string;reservationId:number;bookingNo:string;guestName:string;templateCode:string|null;channel:string;to:string;subject:string|null;body:string;
  status:string;blockedReason:string|null;queuedUtc:string;timeline:{status:string;atUtc:string}[];ruleId:string|null;retryOf:number|null;retriedBy:number|null;
  smsSegments:number|null;simulated:boolean;outcome?:string|null;boostMinutes?:number;guestId?:number|null};
type SrvTemplate={code:string;name:string;channel:string;subject:string;body:string;version:string};
type SrvRule={ruleId:string;event:string;templateCode:string;channel:string;enabled:boolean;version:string};
type SrvConsent={guestId:number;whatsApp:boolean;sms:boolean;email:boolean;optOut:boolean;updatedUtc:string|null;version:string|null};
type SrvRoster={consents:SrvConsent[];guests:{reservationId:number;guestId:number}[]};
const PAGE=100,MAX_PAGES=20;

const wire=(id:number|null)=>id==null?undefined:'MSG-'+id;
export function outcomeOf(m:SrvMessage):MSGMessage['outcome']{
  if(m.outcome==='Read'||m.outcome==='Delivered'||m.outcome==='Failed')return m.outcome;
  const seen=m.timeline.map(t=>t.status);
  return seen.includes('Failed')?'Failed':seen.includes('Read')?'Read':'Delivered';
}
export function messageFromServer(m:SrvMessage):MSGMessage{
  const out:MSGMessage={id:m.id,template:(m.templateCode??'manual') as MSGTemplateId|'manual',channel:m.channel as MSGChannel,booking:String(m.reservationId),
    guestKey:m.guestId!=null?String(m.guestId):'B:'+m.reservationId,guest:m.guestName,to:m.to,body:m.body,queuedAt:utcToClock(m.queuedUtc),outcome:outcomeOf(m),actor:''};
  if(m.subject)out.subject=m.subject;
  if(m.boostMinutes)out.boost=m.boostMinutes;
  if(m.blockedReason)out.blocked=m.blockedReason;
  if(m.ruleId)out.rule=m.ruleId;
  if(m.retryOf!=null)out.retryOf=wire(m.retryOf);
  if(m.retriedBy!=null)out.retriedBy=wire(m.retriedBy);
  return out;
}
const consentOf=(c:SrvConsent):MSGConsent=>({whatsapp:c.whatsApp,sms:c.sms,email:c.email,optOut:c.optOut,...(c.updatedUtc?{updatedAt:utcToClock(c.updatedUtc)}:{})});

async function loadMessaging(c:LoaderCtx):Promise<MSGState>{
  const api=c.api;
  const msgs:SrvMessage[]=[];
  for(let page=1;page<=MAX_PAGES;page++){
    const r=await api.get<{items:SrvMessage[];total:number}>('/messaging/messages',{page,pageSize:PAGE});
    msgs.push(...r.items);if(msgs.length>=r.total||!r.items.length)break;
  }
  const [tpl,rules,roster]=await Promise.all([api.get<{templates:SrvTemplate[]}>('/messaging/templates'),api.get<SrvRule[]>('/messaging/rules'),api.get<SrvRoster>('/messaging/consents')]);
  const outbox=msgs.map(m=>{c.ids.learn('message',m.id,m.messageId);return messageFromServer(m);}).reverse();
  const consent:Record<string,MSGConsent>={};
  for(const x of roster.consents){c.ids.setVersion('msgConsent',String(x.guestId),x.version??'');consent[String(x.guestId)]=consentOf(x);}
  for(const g of roster.guests){c.ids.learn('msgGuest','B:'+g.reservationId,g.guestId);const k=String(g.guestId);if(consent[k])consent['B:'+g.reservationId]=consent[k];}
  for(const m of msgs)if(m.guestId!=null)c.ids.learn('msgGuest','B:'+m.reservationId,m.guestId);
  return {
    templates:tpl.templates.map(t=>{c.ids.setVersion('msgTemplate',t.code,t.version);return {id:t.code as MSGTemplateId,name:t.name,subject:t.subject,body:t.body,channel:t.channel as MSGChannel} as MSGTemplate;}),
    rules:rules.map(r=>{c.ids.setVersion('msgRule',r.ruleId,r.version);return {id:r.ruleId,event:r.event as MSGEvent,template:r.templateCode as MSGTemplateId,channel:r.channel as MSGChannel,enabled:r.enabled} as MSGRule;}),
    consent,outbox,fired:msgs.filter(m=>m.ruleId).map(m=>`${m.ruleId}:${m.reservationId}`),docs:[],
  };
}

function ver(c:CommandCtx,kind:string,id:string):string{
  const v=c.ids.version(kind,id);
  if(!v)throw new HccClientError('VALIDATION','This record is not loaded from the server yet. Refresh and try again.');
  return v;
}
const msgId=(c:CommandCtx,id:unknown)=>{
  const s=c.ids.server('message',String(id??''));
  if(s===undefined)throw new HccClientError('NOT_FOUND','Message not found. Refresh and try again.');
  return s;
};
const optTxt=(v:unknown)=>String(v??'').trim()||undefined;
type Cmd=CommandCtx['cmd'];

export const messaging:SyncArea={
  id:'messaging',
  loaders:[{slice:'messagingX',requires:['reservations','checkin'],load:loadMessaging}],
  commands:[
    {type:'MSG_SEND',reload:['messagingX'],
      precheck:(cmd:Cmd)=>!cmd.booking?'Choose an in-house guest.':String(cmd.body??'').trim().length<1||String(cmd.body).trim().length>600?'Write a message of 1–600 characters.':undefined,
      async send(c){await c.api.command('SendMessage',{reservationId:serverId(c.cmd.booking,'Booking'),channel:c.cmd.channel,body:String(c.cmd.body).trim(),
        templateCode:optTxt(c.cmd.template),subject:optTxt(c.cmd.subject)});}},
    // The server sweeps every pending trigger (idempotent per rule + reservation); the screen's `only` list is not sent.
    {type:'MSG_RUN_RULES',reload:['messagingX'],async send(c){await c.api.command('RunMessagingTriggers',{});}},
    {type:'MSG_SIMULATE',reload:['messagingX'],
      async send(c){await c.api.command('SimulateMessageDelivery',c.cmd.id?{messageId:msgId(c,c.cmd.id)}:{});}},
    {type:'MSG_RETRY',reload:['messagingX'],async send(c){await c.api.command('RetryMessage',{messageId:msgId(c,c.cmd.id)});}},
    {type:'MSG_CONSENT',reload:['messagingX'],
      async send(c){
        const key=String(c.cmd.guestKey??'');
        const guestId=/^\d+$/.test(key)?Number(key):c.ids.server('msgGuest',key);
        if(guestId===undefined)throw new HccClientError('VALIDATION','Consent is kept on the guest profile: register the guest (check-in) first.');
        await c.api.command('SetMessagingConsent',{guestId,whatsApp:c.cmd.whatsapp,sms:c.cmd.sms,email:c.cmd.email,optOut:c.cmd.optOut});
      }},
    {type:'MSG_TEMPLATE',reload:['messagingX'],
      async send(c){await c.api.command('UpdateMessageTemplate',{code:String(c.cmd.id),body:String(c.cmd.body??'').trim(),subject:optTxt(c.cmd.subject),channel:optTxt(c.cmd.channel)},
        {expectedVersion:ver(c,'msgTemplate',String(c.cmd.id))});}},
    {type:'MSG_RULE',reload:['messagingX'],
      async send(c){await c.api.command('UpdateMessageRule',{ruleId:String(c.cmd.id),enabled:c.cmd.enabled,channel:optTxt(c.cmd.channel)},{expectedVersion:ver(c,'msgRule',String(c.cmd.id))});}},
  ],
  unmapped:{
    MSG_DOC_EXTRACT:'document capture (simulated OCR of fictional samples) belongs to the ID/document capture area (W4-11 / Documents module); no messaging server command',
    MSG_DOC_CONFIRM:'document capture: W4-11 (applies to guest profile / expense draft, other areas)',
    MSG_DOC_DISCARD:'document capture: W4-11',
  },
};
