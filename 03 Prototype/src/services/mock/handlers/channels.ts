// Module 21–22 · OTA channels & direct booking — command handlers. Command types MUST start with 'CHN_'. Return value = audit/summary text.
// SIMULATED integrations only. Inbound OTA and web bookings reuse stream A's BOOK / CONFIRM / CANCEL / CHANGE_DATES handlers so
// availability, capacity, hold and day-closed rules are identical to manual bookings.
import type {Handler,Kit} from '../commands';
import type {Command,State} from '../../../domain/types';
import {handlers as res} from './reservations';
import {money} from '../../../domain/money';
import {resHoldActive,resCapacity} from '../../../domain/streams/reservations';
import {chnEmpty,chnAcceptCheck,chnFreeRooms,chnPushPreview,chnRoomTypes,chnWebQuote,chnWebSearchError,chnPct,
  CHN_CODE,CHN_MAX_COMMISSION_BPS,CHN_MAX_MARKUP_BPS,CHN_WEB_SOURCE,type CHNState,type CHNChannel} from '../../../domain/streams/channels';

const slice=(s:State):CHNState=>{const x=s.channelsX=s.channelsX??chnEmpty();x.channels??=[];x.messages??=[];x.pushes??=[];x.outbox??=[];x.webRequests??=[];return x;};
/** Run another (stream A) handler on the same private clone with a different command. b() is rebound to the new command's id. */
function call(k:Kit,name:string,cmd:Command):string{
  const h=res[name];if(!h)k.fail('VALIDATION',`Booking action ${name} is unavailable.`);
  const b=()=>{const x=k.s.bookings.find(x=>x.id===cmd.id);k.assert(x,'Booking not found.','NOT_FOUND');return x!;};
  return h({...k,cmd,b});
}
function channel(k:Kit,id:string):CHNChannel{const c=slice(k.s).channels.find(c=>c.id===id);k.assert(c,'Channel not found.','NOT_FOUND');return c!;}
const reasonOf=(k:Kit)=>{const r=String(k.cmd.reason??'').trim();k.assert(r.length>=3,'Enter a reason (at least 3 characters).');return r.slice(0,200);};

export const handlers:Record<string,Handler>={
  CHN_CHANNEL_STATUS:k=>{
    const {s,cmd,req,assert}=k;req('approve');const c=channel(k,cmd.channel);
    const to={connect:'Connected',pause:'Paused',disconnect:'Disconnected'}[cmd.action as string] as CHNChannel['status']|undefined;
    assert(to,'Unknown connection action.');
    assert(c.status!==to,`${c.name} is already ${to!.toLowerCase()}.`,'INVALID_TRANSITION');
    const from=c.status;c.status=to!;
    if(to==='Connected'){c.lastSync=s.clock;c.statusNote='Simulated connection test passed';}
    else c.statusNote=to==='Paused'?'Paused by staff — nothing is sent or received':'Disconnected by staff';
    return `${c.name} · ${from} → ${to} (simulated)`;
  },
  CHN_CHANNEL_SAVE:k=>{
    const {cmd,req,assert}=k;req('approve');const c=channel(k,cmd.channel);
    const com=cmd.commissionBps,mk=cmd.markupBps??c.markupBps;
    assert(Number.isInteger(com)&&com>=0&&com<=CHN_MAX_COMMISSION_BPS,`Commission must be between 0% and ${chnPct(CHN_MAX_COMMISSION_BPS)}.`);
    assert(Number.isInteger(mk)&&mk>=0&&mk<=CHN_MAX_MARKUP_BPS,`Rate markup must be between 0% and ${chnPct(CHN_MAX_MARKUP_BPS)}.`);
    const mapping:Record<string,string>={};
    const types=chnRoomTypes(k.s);
    for(const [type,raw] of Object.entries((cmd.mapping??c.mapping) as Record<string,string>)){
      assert(types.includes(type),`Unknown room type ${type}.`);
      const code=String(raw??'').trim().toUpperCase();if(!code)continue;
      assert(CHN_CODE.test(code),`Channel code for ${type} must be 2–20 letters, digits or dashes.`);
      assert(!Object.values(mapping).includes(code),`Channel code ${code} is used twice.`);
      mapping[type]=code;
    }
    const before=`${chnPct(c.commissionBps)} · markup ${chnPct(c.markupBps)} · ${Object.keys(c.mapping).length} mapped`;
    c.commissionBps=com;c.markupBps=mk;c.mapping=mapping;
    return `${c.name} · ${before} → ${chnPct(com)} · markup ${chnPct(mk)} · ${Object.keys(mapping).length} mapped`;
  },
  CHN_STOP_SELL:k=>{
    const {s,cmd,req,assert}=k;req('reservations');
    assert(chnRoomTypes(s).includes(cmd.roomType),'Choose a room type.');
    const list=cmd.channel==='ALL'?slice(s).channels:[channel(k,cmd.channel)];
    for(const c of list){c.stopSell=c.stopSell.filter(t=>t!==cmd.roomType);if(cmd.on)c.stopSell.push(cmd.roomType);}
    return `${cmd.roomType} · stop-sell ${cmd.on?'on':'off'} · ${cmd.channel==='ALL'?'all channels':list[0].name}`;
  },
  CHN_PUSH:k=>{
    const {s,cmd,req,assert,id}=k;req('reservations');const c=channel(k,cmd.channel);
    assert(c.status==='Connected',`${c.name} is ${c.status.toLowerCase()}. Connect it before pushing.`,'INVALID_TRANSITION');
    const p=chnPushPreview(s,c.id);
    assert(p.rows.length,`No room types are mapped on ${c.name}.`);
    const closed=p.rows.filter(r=>r.closed).length;
    const summary=`${p.rows.length} rate/availability rows · ${closed} closed${p.unmapped.length?' · not mapped: '+p.unmapped.join(', '):''}`;
    slice(s).pushes.unshift({id:id('CHP'),at:s.clock,actor:k.uid,channel:c.id,rows:p.rows.length,closed,summary});
    c.lastSync=s.clock;
    return `${c.name} · simulated push · ${summary}`;
  },
  CHN_PULL:k=>{
    const {s,req,id}=k;req('reservations');const st=slice(s);
    const live=new Set(st.channels.filter(c=>c.status==='Connected').map(c=>c.id));
    const i=st.outbox.findIndex(m=>live.has(m.channel));
    for(const c of st.channels)if(c.status==='Connected')c.lastSync=s.clock;
    if(i<0)return 'Checked connected channels · no new messages (simulated)';
    const m={...st.outbox.splice(i,1)[0],id:id('CHM'),receivedAt:s.clock,status:'Pending' as const};
    st.messages.push(m);
    return `${m.kind} message ${m.ref} received from ${st.channels.find(c=>c.id===m.channel)!.name} (simulated)`;
  },
  CHN_ACCEPT:k=>{
    const {s,cmd,req,open,assert,fail,uid}=k;req('reservations');open();
    const st=slice(s),m=st.messages.find(m=>m.id===cmd.id);assert(m,'Message not found.','NOT_FOUND');
    assert(m!.status==='Pending',`This message is already ${m!.status.toLowerCase()}.`,'INVALID_TRANSITION');
    const c=channel(k,m!.channel);
    const chk=chnAcceptCheck(s,m!,cmd.room||undefined);
    if(!chk.ok){
      const code=/free|overbooking|not free/.test(chk.reason)?'NOT_AVAILABLE':/not found/.test(chk.reason)?'NOT_FOUND':/Duplicate|handle at the front desk|rejected/.test(chk.reason)?'INVALID_TRANSITION':'VALIDATION';
      fail(code,chk.reason+' Reject the message with a reason instead.');
    }
    const msg=m!;const done=(status:'Accepted'|'Applied')=>{msg.status=status;msg.decidedBy=uid;msg.decidedAt=s.clock;};
    if(msg.kind==='New'){
      call(k,'BOOK',{type:'BOOK',guest:msg.guest,email:msg.email||'',phone:msg.phone||'',people:msg.adults,children:msg.children,room:chk.room,
        arrival:msg.arrival,departure:msg.departure,source:'OTA',otaChannel:c.name,notes:`${c.name} reservation ${msg.ref} · simulated channel import`});
      const bk=s.bookings.slice(-1)[0]!;msg.booking=bk.id;msg.room=bk.room;done('Accepted');
      return `${c.name} ${msg.ref} → ${bk.id} · ${msg.guest} · room ${bk.room} · ${msg.arrival} → ${msg.departure}${chk.warning?' · '+chk.warning:''}`;
    }
    const o=chk.original!;
    if(o.status==='Pending'){
      if(msg.kind==='Cancel'){o.status='Rejected';o.reason='Cancelled by channel before acceptance';o.decidedBy=uid;o.decidedAt=s.clock;}
      else{o.arrival=msg.arrival;o.departure=msg.departure;o.adults=msg.adults;o.children=msg.children;if(msg.amount)o.amount=msg.amount;}
      done('Applied');
      return `${c.name} ${msg.ref} · ${msg.kind==='Cancel'?'pending request withdrawn':'pending request updated to '+msg.arrival+' → '+msg.departure}`;
    }
    const bk=s.bookings.find(b=>b.id===o.booking)!;
    if(msg.kind==='Cancel'){
      if(bk.status!=='Cancelled')call(k,'CANCEL',{type:'CANCEL',id:bk.id,reason:`${c.name} cancellation ${msg.ref}`});
      done('Applied');return `${c.name} ${msg.ref} · ${bk.id} cancelled`;
    }
    const changed=chk.room!==bk.room||msg.arrival!==bk.arrival||msg.departure!==bk.departure;
    if(changed)call(k,'CHANGE_DATES',{type:'CHANGE_DATES',id:bk.id,room:chk.room,arrival:msg.arrival,departure:msg.departure});
    const party=msg.adults+msg.children,room=s.rooms.find(r=>r.id===bk.room)!;
    assert(party<=resCapacity(room),`Room ${room.id} accommodates at most ${resCapacity(room)} guests.`);
    if(bk.people!==msg.adults||(bk.children||0)!==msg.children){bk.people=msg.adults;if(msg.children)bk.children=msg.children;else delete bk.children;if(!changed)bk.version++;}
    o.arrival=msg.arrival;o.departure=msg.departure;o.adults=msg.adults;o.children=msg.children;o.room=bk.room;
    done('Applied');
    return `${c.name} ${msg.ref} · ${bk.id} modified · room ${bk.room} · ${bk.arrival} → ${bk.departure} · ${party} guest${party===1?'':'s'}`;
  },
  CHN_REJECT:k=>{
    const {s,cmd,req,open,assert,uid}=k;req('reservations');open();
    const m=slice(s).messages.find(m=>m.id===cmd.id);assert(m,'Message not found.','NOT_FOUND');
    assert(m!.status==='Pending',`This message is already ${m!.status.toLowerCase()}.`,'INVALID_TRANSITION');
    m!.reason=reasonOf(k);m!.status='Rejected';m!.decidedBy=uid;m!.decidedAt=s.clock;
    return `${slice(s).channels.find(c=>c.id===m!.channel)?.name??m!.channel} ${m!.ref} (${m!.kind}) rejected · ${m!.reason} · channel notified (simulated)`;
  },
  CHN_WEB_REQUEST:k=>{
    const {s,cmd,req,open,assert,id}=k;req('reservations');open();
    const adults=Number(cmd.adults),children=Number(cmd.children??0);
    const err=chnWebSearchError(s,cmd.arrival,cmd.departure,adults,children);assert(!err,err);
    assert(chnRoomTypes(s).includes(cmd.roomType),'Choose a room type.');
    const q=chnWebQuote(s,cmd.roomType,cmd.arrival,cmd.departure,adults,children);
    assert(q.ok,q.reason==='Sold out for these dates'?`${cmd.roomType} is sold out for these dates.`:`${cmd.roomType}: ${q.reason}.`,q.left?'VALIDATION':'NOT_AVAILABLE');
    const guest=String(cmd.guest??'').trim(),phone=String(cmd.phone??'').trim(),email=String(cmd.email??'').trim(),notes=String(cmd.notes??'').trim().slice(0,200);
    assert(guest.length>=2,'Enter the guest’s name.');
    assert(/^\+?[\d\s-]{7,16}$/.test(phone),'Enter a valid phone number (7–16 digits; fictional).');
    assert(!email||/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email),'Enter a valid email address or leave it blank.');
    const room=chnFreeRooms(s,cmd.roomType,cmd.arrival,cmd.departure,adults+children)[0]?.id;
    assert(room,`${cmd.roomType} is sold out for these dates.`,'NOT_AVAILABLE');
    call(k,'BOOK',{type:'BOOK',guest,email,phone,people:adults,children,room,arrival:cmd.arrival,departure:cmd.departure,source:CHN_WEB_SOURCE,hold:true,
      notes:'Direct website request (preview) · pending hotel confirmation'+(notes?' · '+notes:'')});
    const bk=s.bookings.slice(-1)[0]!,wid=id('WEB');
    slice(s).webRequests.push({id:wid,booking:bk.id,guest,phone,email,roomType:cmd.roomType,arrival:cmd.arrival,departure:cmd.departure,adults,children,
      net:q.net,tax:q.tax,total:q.total,createdAt:s.clock,status:'Pending',notes:notes||undefined});
    return `${wid} · ${guest} · ${cmd.roomType} · ${cmd.arrival} → ${cmd.departure} · ${money(q.total)} incl. GST · held as ${bk.id}`;
  },
  CHN_WEB_CONFIRM:k=>{
    const {s,cmd,req,assert,uid}=k;req('reservations');
    const w=slice(s).webRequests.find(w=>w.id===cmd.id);assert(w,'Web request not found.','NOT_FOUND');
    assert(w!.status==='Pending',`This request is already ${w!.status.toLowerCase()}.`,'INVALID_TRANSITION');
    call(k,'CONFIRM',{type:'CONFIRM',id:w!.booking});
    w!.status='Confirmed';w!.decidedBy=uid;
    return `${w!.id} confirmed · ${w!.booking} · ${w!.guest} · confirmation sent to guest (simulated)`;
  },
  CHN_WEB_DECLINE:k=>{
    const {s,cmd,req,open,assert,uid}=k;req('reservations');open();
    const w=slice(s).webRequests.find(w=>w.id===cmd.id);assert(w,'Web request not found.','NOT_FOUND');
    assert(w!.status==='Pending',`This request is already ${w!.status.toLowerCase()}.`,'INVALID_TRANSITION');
    const reason=reasonOf(k);
    const bk=s.bookings.find(b=>b.id===w!.booking);
    if(bk&&resHoldActive(s,bk))call(k,'CANCEL',{type:'CANCEL',id:bk.id,reason:'Web request declined · '+reason});
    w!.status='Declined';w!.reason=reason;w!.decidedBy=uid;
    return `${w!.id} declined · ${reason}${bk&&bk.status==='Cancelled'?' · hold released':''}`;
  },
};
