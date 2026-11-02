// Room keys — command handlers (spec HCC_KeyAgent_Spec_v1 §3-§6). Command types MUST start with 'KEY_'. Return value = audit text.
//   KEY_REQUEST {booking, event, reason?, validTo?}   issue / re-issue / extend / reactivate (reason required except for issued)
//   KEY_CANCEL {booking}   KEY_READ {}   KEY_MANUAL {booking, event, reason?, note?}   "key issued manually" fallback record
//   KEY_LOCK_SAVE {room, hasLock, lockNo?, doorName?, note?}   Manager / Owner mapping   KEY_ENCODER {online}   KEY_SIM {...}   KEY_AGENT_STEP {stage?}
import type {Handler,Kit} from '../commands';
import {addDays,nights} from '../../../domain/money';
import {
  KEY_EVENTS,KEY_REASONS,keySlice,keyCanRead,keyCanIssue,keyCanCancel,keyCanConfigure,keyEnqueue,keyExpire,keyLockOf,keyAgentStep,keyCardExpiry,keyFolioCheckout,keyMismatchWarning,
} from '../../../domain/streams/keys';
import type {KEYEvent,KEYReason} from '../../../domain/streams/keys';

const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const own=(k:Kit)=>{const x=keySlice(k.s);k.s.keysX=x;return x;};
const forbid=(k:Kit,ok:boolean,what:string)=>{if(!ok)k.fail('FORBIDDEN',`Access denied: ${what}.`);};

function eventOf(k:Kit,allowed:KEYEvent[]):{event:KEYEvent;reason?:KEYReason}{
  const event=txt(k.cmd.event) as KEYEvent,reason=(txt(k.cmd.reason)||undefined) as KEYReason|undefined;
  k.assert(KEY_EVENTS.includes(event)&&allowed.includes(event),'Choose a key event.');
  k.assert(!reason||KEY_REASONS.includes(reason),'Reason must be extension, lost, damaged or other.');
  if(event!=='issued')k.assert(reason,'A reason is required: extension, lost, damaged or other.');
  return {event,reason};}

function inHouse(k:Kit){
  const b=k.s.bookings.find(b=>b.id===k.cmd.booking);k.assert(b,'Booking not found.','NOT_FOUND');
  k.assert(b!.status==='In house','A key can only be issued for an in-house stay.','INVALID_TRANSITION');return b!;}

export const handlers:Record<string,Handler>={
  KEY_REQUEST:k=>{
    forbid(k,keyCanIssue(k.s,k.uid),'keys.issue needs the check-in permission');
    const {event,reason}=eventOf(k,['issued','reissued','extended','reactivated']);
    const b=inHouse(k);const today=k.s.clock.slice(0,10);
    const lastDay=txt(k.cmd.validTo)||b.departure;
    k.assert(/^\d{4}-\d{2}-\d{2}$/.test(lastDay),'Valid-to must be a date yyyy-MM-dd.');
    k.assert(lastDay>today,'The key must be valid until a day after today.');
    k.assert(nights(today,lastDay)<=60,'A key can be valid for at most 60 days.');
    const j=keyEnqueue(k.s,k.uid,{event,reason,kind:event==='extended'?'extend':'issue_guest',booking:b.id,room:b.room,validTo:lastDay,warning:keyMismatchWarning(lastDay,keyFolioCheckout(k.s))});
    return `${j.id} · ${event}${reason?' ('+reason+')':''} · room ${b.room} · ${b.id} · ${j.state}${j.code?' '+j.code:''}`;},

  KEY_CANCEL:k=>{
    forbid(k,keyCanCancel(k.s,k.uid),'keys.cancel needs the check-out permission');
    const b=k.s.bookings.find(b=>b.id===k.cmd.booking);k.assert(b,'Booking not found.','NOT_FOUND');
    const j=keyEnqueue(k.s,k.uid,{event:'cancelled',kind:'cancel',booking:b!.id,room:b!.room});
    return `${j.id} · cancelled · room ${b!.room} · ${j.state}${j.code?' '+j.code:''}`;},

  KEY_READ:k=>{
    forbid(k,keyCanRead(k.s,k.uid),'keys.read needs the check-in or check-out permission');
    const j=keyEnqueue(k.s,k.uid,{event:'read',kind:'read'});return `${j.id} · read the card on the encoder · ${j.state}${j.code?' '+j.code:''}`;},

  KEY_MANUAL:k=>{
    forbid(k,keyCanIssue(k.s,k.uid),'keys.issue needs the check-in permission');
    const {event,reason}=eventOf(k,['issued','reissued','extended','reactivated']);
    const b=inHouse(k),x=keyExpire(k.s);
    const id='KEYJ-'+(++x.seq);
    x.jobs.push({id,event,reason,kind:'manual',booking:b.id,room:b.room,state:'done',code:'Ok',message:'key issued manually',by:k.uid,at:k.s.clock,timeoutAt:k.s.clock,finishedAt:k.s.clock,note:txt(k.cmd.note)||undefined});
    for(const c of x.cards)if(c.room===b.room&&(c.status==='active'||c.status==='manual'))c.status='superseded';
    x.cards.push({id:'KEYC-'+(++x.seq),job:id,event,reason,booking:b.id,room:b.room,from:k.s.clock,to:keyCardExpiry(b.departure),by:k.uid,at:k.s.clock,status:'manual'});
    return `${id} · ${event} manually${reason?' ('+reason+')':''} · room ${b.room} · ${b.id}`;},

  KEY_LOCK_SAVE:k=>{
    forbid(k,keyCanConfigure(k.s,k.uid),'keys.configure is for the Owner and Manager');
    const room=txt(k.cmd.room);k.assert(k.s.rooms.some(r=>r.id===room),'Room not found.','NOT_FOUND');
    const hasLock=!!k.cmd.hasLock,doorName=txt(k.cmd.doorName)||undefined;
    k.assert(!hasLock||doorName,'A room with a lock needs the BIS door name.');
    const lockNo=k.cmd.lockNo===undefined||k.cmd.lockNo===''||k.cmd.lockNo===null?undefined:Number(k.cmd.lockNo);
    k.assert(lockNo===undefined||(Number.isInteger(lockNo)&&lockNo>0&&lockNo<1000),'Lock number must be a whole number.');
    const x=own(k);x.locks={...x.locks,[room]:{hasLock,lockNo,doorName,note:txt(k.cmd.note)||undefined}};
    return `room ${room} · ${hasLock?'lock '+doorName:'no lock (manual key)'}`;},

  KEY_ENCODER:k=>{
    forbid(k,keyCanConfigure(k.s,k.uid),'keys.configure is for the Owner and Manager');
    const x=own(k),online=!!k.cmd.online;x.encoderOnline=online;if(online){x.encoderSeen=k.s.clock;x.encoderSeenK=x.kmin;}
    return `Demo · encoder agent ${online?'online (heartbeat)':'offline'}`;},

  KEY_SIM:k=>{
    forbid(k,keyCanConfigure(k.s,k.uid),'keys.configure is for the Owner and Manager');
    const x=own(k);x.sim={...x.sim,...(k.cmd.cardPresent!==undefined?{cardPresent:!!k.cmd.cardPresent}:{}),...(k.cmd.failNext!==undefined?{failNext:!!k.cmd.failNext}:{}),...(k.cmd.supportsExtend!==undefined?{supportsExtend:!!k.cmd.supportsExtend}:{})};
    return `Demo · encoder switches card ${x.sim.cardPresent?'present':'absent'}, fail next write ${x.sim.failNext?'on':'off'}, extend ${x.sim.supportsExtend?'mapped':'not mapped'}`;},

  KEY_AGENT_STEP:k=>{
    forbid(k,keyCanConfigure(k.s,k.uid),'keys.configure is for the Owner and Manager');
    const stage=(['pick','wait','finish'].includes(k.cmd.stage)?k.cmd.stage:'finish') as 'pick'|'wait'|'finish';
    return 'Demo · '+keyAgentStep(k.s,k.uid,stage);},

  KEY_CLOCK:k=>{   // demo: move the simulated clock to show the 2-minute expiry (managers only)
    forbid(k,keyCanConfigure(k.s,k.uid),'keys.configure is for the Owner and Manager');
    const m=Number(k.cmd.minutes);k.assert(Number.isInteger(m)&&m>=1&&m<=30,'Advance the demo clock by 1–30 minutes.');
    const t=Date.parse(k.s.clock+':00Z')+m*60000;k.s.clock=new Date(t).toISOString().slice(0,16);own(k).kmin+=m;keyExpire(k.s);
    return `Demo · simulated clock +${m} min → ${k.s.clock.slice(11)} (${addDays(k.s.date,0)})`;},
};
