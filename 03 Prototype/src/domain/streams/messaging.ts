// Module 24–25 · Notifications & document capture — pure helpers, selectors and the state slice type. Exported names MUST start with 'msg' / 'MSG' (shared mock namespace).
// ALL SIMULATED: nothing is ever sent, no network, no real OCR. Delivery status is derived from the simulated clock (s.clock).
import type {State,Booking} from '../types';
import {totals} from '../selectors';
import {money,addDays,addMinutes,nights} from '../money';

export type MSGChannel='WhatsApp'|'SMS'|'Email';
export const MSG_CHANNELS:MSGChannel[]=['WhatsApp','SMS','Email'];
export type MSGTemplateId='booking-confirmation'|'pre-arrival'|'checkin-welcome'|'payment-receipt'|'checkout-invoice'|'feedback';
export type MSGTemplate={id:MSGTemplateId;name:string;subject:string;body:string;channel:MSGChannel};
export type MSGEvent='booking-confirmed'|'pre-arrival'|'checked-in'|'payment-received'|'checked-out';
export type MSGRule={id:string;event:MSGEvent;template:MSGTemplateId;channel:MSGChannel;enabled:boolean};
export type MSGConsent={whatsapp:boolean;sms:boolean;email:boolean;optOut:boolean;updatedAt?:string;by?:string};
export type MSGStatus='Queued'|'Sent'|'Delivered'|'Read'|'Failed'|'Not sent';
export type MSGMessage={id:string;template:MSGTemplateId|'manual';channel:MSGChannel;booking:string;guestKey:string;guest:string;
  /** Masked recipient only (••••••1234 / a•••@example.test). */
  to:string;subject?:string;body:string;queuedAt:string;
  /** Final outcome predetermined at queue time (simulation). */
  outcome:'Read'|'Delivered'|'Failed';
  /** Minutes added by "Simulate delivery". */
  boost?:number;
  /** Set when the message was never sent (consent, opt-out, no recipient). */
  blocked?:string;
  rule?:string;retryOf?:string;retriedBy?:string;actor:string};
export type MSGDocKind='id-card'|'vendor-bill'|'company-letter';
export type MSGField={key:string;label:string;value:string;confidence:number;
  /** Read-only fields (e.g. masked ID number) cannot be corrected. */
  locked?:boolean};
export type MSGSample={id:string;kind:MSGDocKind;title:string;file:string;hint:string;fields:MSGField[]};
export type MSGDoc={id:string;sample:string;kind:MSGDocKind;title:string;status:'Extracted'|'Applied'|'Discarded';
  fields:(MSGField&{original?:string})[];actor:string;at:string;appliedTo?:string;appliedAt?:string;reason?:string};

/** State slice for this module, stored at s.messagingX (optional; absent in old saves → treat as empty). */
export type MSGState={templates?:MSGTemplate[];rules?:MSGRule[];consent?:Record<string,MSGConsent>;outbox?:MSGMessage[];fired?:string[];docs?:MSGDoc[]};
export const msgEmpty=():MSGState=>({});
export const msgSlice=(s:State):MSGState=>s.messagingX??msgEmpty();

// ── Templates and placeholders ──
export const MSG_PLACEHOLDERS=['guest','firstName','hotel','booking','room','arrival','departure','nights','checkin','checkout','amount','balance','invoiceLink','feedbackLink'] as const;
export const MSG_DEFAULT_TEMPLATES:MSGTemplate[]=[
  {id:'booking-confirmation',name:'Booking confirmation',channel:'WhatsApp',subject:'Your booking {booking} is confirmed',
    body:'Namaste {firstName}, your stay at {hotel} is confirmed. Booking {booking}, room {room}, {arrival} to {departure} ({nights} nights). Check-in from {checkin}.'},
  {id:'pre-arrival',name:'Pre-arrival',channel:'WhatsApp',subject:'See you soon at {hotel}',
    body:'Hello {firstName}, we look forward to welcoming you on {arrival}. Check-in is from {checkin}. Reply here for airport pickup or early arrival requests.'},
  {id:'checkin-welcome',name:'Check-in welcome',channel:'WhatsApp',subject:'Welcome to {hotel}',
    body:'Welcome to {hotel}, {firstName}! You are in room {room} until {departure}. Checkout is at {checkout}. Dial 9 for the front desk.'},
  {id:'payment-receipt',name:'Payment receipt',channel:'SMS',subject:'Payment received · {booking}',
    body:'{hotel}: payment of {amount} received for booking {booking}. Balance due {balance}. Thank you.'},
  {id:'checkout-invoice',name:'Checkout invoice link',channel:'Email',subject:'Your invoice for booking {booking}',
    body:'Dear {guest}, thank you for staying with us. Your sample invoice for {booking} is at {invoiceLink} (simulated link).'},
  {id:'feedback',name:'Feedback request',channel:'WhatsApp',subject:'How was your stay?',
    body:'Hi {firstName}, how was your stay at {hotel}? Rate us in one tap: {feedbackLink} (simulated link).'},
];
export const MSG_DEFAULT_RULES:MSGRule[]=[
  {id:'R1',event:'booking-confirmed',template:'booking-confirmation',channel:'WhatsApp',enabled:true},
  {id:'R2',event:'pre-arrival',template:'pre-arrival',channel:'WhatsApp',enabled:true},
  {id:'R3',event:'checked-in',template:'checkin-welcome',channel:'WhatsApp',enabled:true},
  {id:'R4',event:'payment-received',template:'payment-receipt',channel:'SMS',enabled:true},
  {id:'R5',event:'checked-out',template:'checkout-invoice',channel:'Email',enabled:true},
  {id:'R6',event:'checked-out',template:'feedback',channel:'WhatsApp',enabled:false},
];
export const MSG_EVENT_LABEL:Record<MSGEvent,string>={'booking-confirmed':'Booking confirmed','pre-arrival':'Arrival today or tomorrow','checked-in':'Guest checked in','payment-received':'Payment received','checked-out':'Guest checked out'};
export const msgTemplates=(s:State)=>msgSlice(s).templates??MSG_DEFAULT_TEMPLATES;
export const msgTemplate=(s:State,id:string)=>msgTemplates(s).find(t=>t.id===id);
export const msgRules=(s:State)=>msgSlice(s).rules??MSG_DEFAULT_RULES;
/** Placeholders used in a text that are not in the whitelist. */
export function msgUnknownPlaceholders(text:string):string[]{
  const out:string[]=[];for(const m of String(text).matchAll(/\{([^{}]*)\}/g))if(!(MSG_PLACEHOLDERS as readonly string[]).includes(m[1])&&!out.includes(m[1]))out.push(m[1]);return out;
}
const fmtDate=(d:string)=>{const [y,m,day]=d.split('-');return `${Number(day)} ${['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][Number(m)-1]} ${y}`;};
/** Placeholder values for a booking (receipt = payment-receipt amount). */
export function msgValues(s:State,b:Booking,receiptId?:string):Record<string,string>{
  const g=msgGuestOf(s,b),t=totals(b),r=receiptId?b.receipts.find(r=>r.id===receiptId):undefined;
  const name=g?.name||b.guest;
  return {guest:name,firstName:name.split(/\s+/)[0],hotel:s.settings.hotel,booking:b.id,room:b.room,arrival:fmtDate(b.arrival),departure:fmtDate(b.departure),
    nights:String(nights(b.arrival,b.departure)),checkin:s.settings.checkin,checkout:s.settings.checkout,
    amount:money(r?r.amount:t.total),balance:money(Math.max(0,t.balance)),
    invoiceLink:`https://demo.invalid/inv/${b.id}`,feedbackLink:`https://demo.invalid/fb/${b.id}`};
}
/** Fill {placeholders}. Unknown ones stay visible as [name?] and are reported. */
export function msgRender(text:string,values:Record<string,string>):{text:string;missing:string[]}{
  const missing:string[]=[];
  const out=String(text).replace(/\{([^{}]*)\}/g,(_,k)=>{if(k in values)return values[k];missing.push(k);return `[${k}?]`;});
  return {text:out,missing};
}
/** SMS segments (160 GSM characters each; 153 when concatenated). */
export const msgSmsSegments=(text:string)=>text.length<=160?1:Math.ceil(text.length/153);

// ── Guests, recipients and consent ──
export const msgGuestOf=(s:State,b:Booking)=>b.guestId?(s.guests||[]).find(g=>g.id===b.guestId):undefined;
export const msgGuestKey=(b:Booking)=>b.guestId||'B:'+b.id;
export const MSG_DEFAULT_CONSENT:MSGConsent={whatsapp:true,sms:true,email:true,optOut:false};
export const msgConsent=(s:State,key:string):MSGConsent=>msgSlice(s).consent?.[key]??MSG_DEFAULT_CONSENT;
const digits=(p:string)=>{const d=String(p||'').replace(/\D/g,'');return d.length>10?d.slice(-10):d;};
export function msgRecipient(s:State,b:Booking,channel:MSGChannel):{raw:string;masked:string;valid:boolean}{
  const g=msgGuestOf(s,b);
  if(channel==='Email'){const e=g?.email||b.email||'';const [u,d]=e.split('@');return {raw:e,masked:e?(u?.[0]||'')+'•••@'+(d||''):'',valid:/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e)};}
  const p=g?.phone||b.phone||'';const d=digits(p);return {raw:p,masked:p?'••••••'+d.slice(-4):'',valid:d.length===10};
}
const consentFlag=(c:MSGConsent,ch:MSGChannel)=>ch==='WhatsApp'?c.whatsapp:ch==='SMS'?c.sms:c.email;
/** Why a message to this booking on this channel cannot be sent ('' = allowed). Invalid numbers are allowed but fail delivery. */
export function msgBlockReason(s:State,b:Booking,channel:MSGChannel):string{
  const c=msgConsent(s,msgGuestKey(b));
  if(c.optOut)return 'Guest opted out of messages';
  if(!consentFlag(c,channel))return `No ${channel} consent`;
  if(!msgRecipient(s,b,channel).raw)return channel==='Email'?'No email address on file':'No phone number on file';
  return '';
}

// ── Delivery simulation (pure, from the simulated clock) ──
export const msgMinutes=(from:string,to:string)=>Math.round((Date.parse(to+':00Z')-Date.parse(from+':00Z'))/60000);
const hash=(t:string)=>{let h=7;for(const c of t)h=(h*31+c.charCodeAt(0))>>>0;return h;};
/** Deterministic simulated outcome for a new message. */
export function msgOutcome(id:string,channel:MSGChannel,validRecipient:boolean):MSGMessage['outcome']{
  if(!validRecipient||hash(id+channel)%7===3)return 'Failed';
  return channel==='WhatsApp'&&hash(id)%3!==0?'Read':'Delivered';
}
/** Step offsets (minutes after queuing) for a message's path. */
export function msgSteps(m:Pick<MSGMessage,'outcome'|'blocked'>):{status:MSGStatus;offset:number}[]{
  if(m.blocked)return [{status:'Not sent',offset:0}];
  const a:{status:MSGStatus;offset:number}[]=[{status:'Queued',offset:0},{status:'Sent',offset:1}];
  if(m.outcome==='Failed')a.push({status:'Failed',offset:2});
  else{a.push({status:'Delivered',offset:3});if(m.outcome==='Read')a.push({status:'Read',offset:8});}
  return a;
}
export function msgElapsed(m:MSGMessage,clock:string){return Math.max(0,msgMinutes(m.queuedAt,clock))+(m.boost||0);}
/** Timeline reached at the given clock (each step with its simulated time). */
export function msgTimeline(m:MSGMessage,clock:string):{status:MSGStatus;at:string}[]{
  const e=msgElapsed(m,clock),real=Math.max(0,msgMinutes(m.queuedAt,clock));
  return msgSteps(m).filter(x=>x.offset<=e).map(x=>({status:x.status,at:addMinutes(m.queuedAt,Math.min(x.offset,real))}));
}
export const msgStatus=(m:MSGMessage,clock:string):MSGStatus=>msgTimeline(m,clock).at(-1)!.status;
export const msgFinal=(st:MSGStatus)=>st==='Read'||st==='Failed'||st==='Not sent'||st==='Delivered';
/** Is the message at its final step for its path? (Delivered is final for SMS/Email or when outcome is Delivered.) */
export function msgDone(m:MSGMessage,clock:string){const st=msgSteps(m);return msgElapsed(m,clock)>=st.at(-1)!.offset;}
/** Minutes to add so the message reaches its next step (0 when done). */
export function msgNextBoost(m:MSGMessage,clock:string){const e=msgElapsed(m,clock),n=msgSteps(m).find(x=>x.offset>e);return n?n.offset-e:0;}
export const MSG_STATUS_TONE:Record<MSGStatus,string>={Queued:'neutral',Sent:'blue',Delivered:'green',Read:'green',Failed:'msg-fail','Not sent':'orange'};
export function msgCounts(s:State){
  const out:Record<MSGStatus,number>={Queued:0,Sent:0,Delivered:0,Read:0,Failed:0,'Not sent':0};
  for(const m of msgSlice(s).outbox||[])out[msgStatus(m,s.clock)]++;return out;
}
export const msgOutbox=(s:State)=>(msgSlice(s).outbox||[]).slice().reverse();

// ── Trigger rules (evaluated here; no other stream's handlers are edited) ──
export type MSGTrigger={key:string;rule:MSGRule;booking:Booking;receipt?:string;label:string};
/** Every trigger event present in the state for the given rules (fired or not). */
export function msgTriggerEvents(s:State,rules:MSGRule[]=msgRules(s)):MSGTrigger[]{
  const out:MSGTrigger[]=[];const tomorrow=addDays(s.date,1);
  for(const rule of rules)for(const b of s.bookings){
    const add=(receipt?:string,label='')=>out.push({key:`${rule.id}:${receipt||b.id}`,rule,booking:b,receipt,label:label||MSG_EVENT_LABEL[rule.event]});
    if(rule.event==='booking-confirmed'&&b.status==='Confirmed')add();
    else if(rule.event==='pre-arrival'&&b.status==='Confirmed'&&(b.arrival===s.date||b.arrival===tomorrow))add(undefined,b.arrival===s.date?'Arrives today':'Arrives tomorrow');
    else if(rule.event==='checked-in'&&b.status==='In house')add();
    else if(rule.event==='checked-out'&&b.status==='Departed')add();
    else if(rule.event==='payment-received')b.receipts.filter(r=>r.kind==='payment').forEach(r=>add(r.id,`Payment ${money(r.amount)} · ${r.method}`));
  }
  return out;
}
/** Enabled-rule events not yet queued. */
export function msgPendingTriggers(s:State):MSGTrigger[]{
  const fired=new Set(msgSlice(s).fired||[]);
  return msgTriggerEvents(s,msgRules(s).filter(r=>r.enabled)).filter(t=>!fired.has(t.key));
}
/** In-house bookings (manual compose targets). */
export const msgInHouse=(s:State)=>s.bookings.filter(b=>b.status==='In house');

// ── Document capture ──
export const MSG_LOW_CONFIDENCE=0.8;
export const MSG_DOC_KIND_LABEL:Record<MSGDocKind,string>={'id-card':'ID card','vendor-bill':'Vendor bill','company-letter':'Company letter'};
export const msgLowFields=(fields:MSGField[])=>fields.filter(f=>!f.locked&&f.confidence<MSG_LOW_CONFIDENCE).map(f=>f.key);
/** True when a text holds 8+ consecutive digits (looks like a full ID number). */
export const msgLooksLikeFullId=(t:string)=>/\d{8,}/.test(String(t).replace(/[\s-]/g,''));
export const msgDocs=(s:State)=>(msgSlice(s).docs||[]).slice().reverse();
export const msgFieldValue=(d:{fields:MSGField[]},key:string)=>d.fields.find(f=>f.key===key)?.value??'';
/** Parse a rupee amount text (e.g. "₹1,240.00") into paise; NaN when invalid. */
export function msgParseRupees(t:string):number{const c=String(t).replace(/[₹,\s]/g,'');if(!/^\d+(\.\d{1,2})?$/.test(c))return NaN;return Math.round(Number(c)*100);}
