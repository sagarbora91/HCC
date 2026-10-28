// Module 21–22 · OTA channels & direct booking — pure helpers, selectors and the state slice type. Exported names MUST start with 'chn' / 'CHN' (shared mock namespace).
// Everything here is SIMULATED: fictional channels, no network. Inventory rules come from the shared selectors (available, rateFor, taxRateFor).
import type {State,Booking,Room} from '../types';
import {bookingRef} from '../bookingRef';
import {available,rateFor} from '../selectors';
import {gstForRoomNight,gstStayTax} from '../gst';
import {addDays,nights,money} from '../money';
import {resCapacity,resHoldActive} from './reservations';

export type CHNChannelStatus='Connected'|'Paused'|'Error'|'Disconnected';
export type CHNChannel={id:string;name:string;status:CHNChannelStatus;
  /** Commission in basis points (1800 = 18%). */ commissionBps:number;
  /** Rate parity uplift pushed to this channel, basis points. */ markupBps:number;
  /** Hotel room type → channel room code. Missing/blank = not sold on this channel. */ mapping:Record<string,string>;
  /** Room types stop-sold on this channel. */ stopSell:string[];
  lastSync?:string;statusNote?:string;
  /** Production only (DEC-079 D7, Wave D5): the server withheld the commission rate — the reader lacks PERM-OTA-004 (Owner, Manager and
   *  Accountant hold it). commissionBps is then 0 and is not the channel's rate. */ commissionHidden?:boolean;
  /** ACC-2 flows (contract §4, rulebook §4.4 / DEC-143): the GST on this channel's commission invoice may be taken as input tax credit
   *  (Dr 1310 / 1320 at payout). Absent / false = disallowed: the commission GST is part of the 5070 cost. Owner setting (CHN_CHANNEL_SAVE). */
  itcAllowed?:boolean};
export type CHNMessageKind='New'|'Modify'|'Cancel';
export type CHNMessageStatus='Pending'|'Accepted'|'Applied'|'Rejected';
export type CHNMessage={id:string;channel:string;kind:CHNMessageKind;ref:string;guest:string;phone?:string;email?:string;roomType:string;
  arrival:string;departure:string;adults:number;children:number;
  /** Amount as received from the channel (paise, reference only). */ amount:number;
  receivedAt:string;status:CHNMessageStatus;booking?:string;room?:string;reason?:string;decidedBy?:string;decidedAt?:string};
export type CHNPush={id:string;at:string;actor:string;channel:string;rows:number;closed:number;summary:string};
export type CHNWebRequest={id:string;booking:string;guest:string;phone:string;email:string;roomType:string;arrival:string;departure:string;
  adults:number;children:number;net:number;tax:number;total:number;createdAt:string;status:'Pending'|'Confirmed'|'Declined';reason?:string;decidedBy?:string;notes?:string};
/** State slice for this module, stored at s.channelsX (optional; absent in old saves → treat as empty). */
/** ACC-2 (contract §4, DEC-150): one OTA payout from the channel's statement — gross = net + commission + commissionGst + tcs; settles the channel's 1115 party ledger. */
export type CHNPayout={id:string;channel:string;date:string;statementRef:string;gross:number;commission:number;commissionGst:number;tcs:number;net:number;bank:string;bookings:string[];by:string;at:string};
export type CHNState={channels:CHNChannel[];messages:CHNMessage[];pushes:CHNPush[];/** ACC-2 */payouts?:CHNPayout[];
  /** Scripted inbound messages delivered one at a time by CHN_PULL (simulation). */ outbox:CHNMessage[];
  webRequests:CHNWebRequest[]};
export const chnEmpty=():CHNState=>({channels:[],messages:[],pushes:[],outbox:[],webRequests:[]});
export const chnSlice=(s:State):CHNState=>({...chnEmpty(),...(s.channelsX??{})});

export const CHN_PUSH_DAYS=7;
export const CHN_MAX_COMMISSION_BPS=3000;
export const CHN_MAX_MARKUP_BPS=2500;
export const CHN_CODE=/^[A-Z0-9-]{2,20}$/;
/** Booking source used for direct-website requests. */
export const CHN_WEB_SOURCE='Website';

export const chnPct=(bps:number)=>(bps/100).toLocaleString('en-IN',{maximumFractionDigits:2})+'%';
/** Shown instead of a commission rate the server withheld (DEC-079 D7). */
export const CHN_COMMISSION_HIDDEN='Not shown to your role';
/** True when the commission rates are withheld from this reader (production, no PERM-OTA-004): the commission figures, report and
 *  "Commission due" are hidden too, since they would be computed from no rate. Never in the demo build. */
export const chnCommissionHidden=(s:State)=>chnSlice(s).channels.some(c=>c.commissionHidden);
/** A channel's commission rate as text, or CHN_COMMISSION_HIDDEN. */
export const chnRate=(c:CHNChannel)=>c.commissionHidden?CHN_COMMISSION_HIDDEN:chnPct(c.commissionBps);
/** Hotel room types in display order (derived from rooms). */
export function chnRoomTypes(s:State):string[]{
  const order=['Classic','Deluxe','Suite'];
  return [...new Set(s.rooms.map(r=>r.type))].sort((a,b)=>(order.indexOf(a)+1||99)-(order.indexOf(b)+1||99)||a.localeCompare(b));
}
export const chnChannel=(s:State,id:string)=>chnSlice(s).channels.find(c=>c.id===id);
export const chnChannelName=(s:State,id:string)=>chnChannel(s,id)?.name??id;
export function chnStatusTone(st:string){return st==='New'||st==='Info'||st==='Held'?'chn-info':st==='Modify'?'chn-warn':st==='Cancel'?'chn-err':st==='Connected'||st==='Accepted'||st==='Applied'||st==='Confirmed'?'chn-ok':st==='Paused'||st==='Pending'?'chn-warn':st==='Error'||st==='Rejected'||st==='Declined'||st==='Expired'?'chn-err':'chn-mute';}

/** Rooms of a type that are free for [a,d) and fit the party, sorted by room number. */
export function chnFreeRooms(s:State,type:string,a:string,d:string,party=1,except=''):Room[]{
  return s.rooms.filter(r=>r.type===type&&resCapacity(r)>=party&&available(s,r.id,a,d,except)).sort((x,y)=>x.id.localeCompare(y.id));
}
/** Rooms of a type sellable on one night (capacity ignored). */
export const chnAvailOn=(s:State,type:string,date:string)=>s.rooms.filter(r=>r.type===type&&available(s,r.id,date,addDays(date,1))).length;
export const chnTypeCapacity=(s:State,type:string)=>{const r=s.rooms.find(r=>r.type===type);return r?resCapacity(r):0;};

// ── Rate & availability push preview (R5) ──
export type CHNPushRow={type:string;code:string;date:string;avail:number;rate:number;closed:boolean;why?:string};
export function chnPushPreview(s:State,channelId:string,days=CHN_PUSH_DAYS){
  const c=chnChannel(s,channelId);const rows:CHNPushRow[]=[];const unmapped:string[]=[];
  if(!c)return {rows,unmapped,dates:[] as string[]};
  const dates=Array.from({length:days},(_,i)=>addDays(s.date,i));
  for(const type of chnRoomTypes(s)){
    const code=(c.mapping[type]||'').trim();
    if(!code){unmapped.push(type);continue;}
    const room=s.rooms.find(r=>r.type===type)!;
    for(const date of dates){
      const avail=chnAvailOn(s,type,date);
      const rate=Math.round(rateFor(s,room,date)*(10000+c.markupBps)/10000);
      const stop=c.stopSell.includes(type);
      rows.push({type,code,date,avail,rate,closed:stop||avail===0,why:stop?'Stop-sell':avail===0?'Sold out':undefined});
    }
  }
  return {rows,unmapped,dates};
}

// ── Inbound pre-check (R6–R8) ──
export type CHNCheck={ok:boolean;reason:string;room?:string;warning?:string;original?:CHNMessage};
export const chnOriginal=(s:State,m:CHNMessage)=>chnSlice(s).messages.find(x=>x.kind==='New'&&x.channel===m.channel&&x.ref===m.ref&&x.id!==m.id);
export function chnAcceptCheck(s:State,m:CHNMessage,room?:string):CHNCheck{
  if(m.status!=='Pending')return {ok:false,reason:`Already ${m.status.toLowerCase()}.`};
  const c=chnChannel(s,m.channel);
  if(!c)return {ok:false,reason:'Unknown channel.'};
  const party=m.adults+m.children;
  if(m.kind==='New'){
    if(chnSlice(s).messages.some(x=>x.id!==m.id&&x.kind==='New'&&x.channel===m.channel&&x.ref===m.ref&&x.status==='Accepted'))return {ok:false,reason:`Duplicate: ${m.ref} is already accepted.`};
    if(!(c.mapping[m.roomType]||'').trim())return {ok:false,reason:`${m.roomType} is not mapped on ${c.name}.`};
    if(m.arrival<s.date)return {ok:false,reason:'Arrival is before the business date.'};
    if(!(nights(m.arrival,m.departure)>0))return {ok:false,reason:'Departure must follow arrival.'};
    const cap=chnTypeCapacity(s,m.roomType);
    if(!cap)return {ok:false,reason:`The hotel has no ${m.roomType} rooms.`};
    if(m.adults<1||m.adults>4||m.children<0||m.children>3||party>cap)return {ok:false,reason:`Party of ${party} exceeds ${m.roomType} capacity (${cap}).`};
    const free=chnFreeRooms(s,m.roomType,m.arrival,m.departure,party);
    const warning=c.stopSell.includes(m.roomType)?`${m.roomType} is stop-sold on ${c.name}; this reservation was already sold.`:undefined;
    if(room){
      const r=s.rooms.find(r=>r.id===room);
      if(!r||r.type!==m.roomType)return {ok:false,reason:`Room ${room} is not a ${m.roomType} room.`};
      if(!free.some(f=>f.id===room))return {ok:false,reason:`Room ${room} is not free for ${m.arrival} → ${m.departure}.`};
      return {ok:true,reason:`Room ${room}`,room,warning};
    }
    if(!free.length)return {ok:false,reason:`No ${m.roomType} room free for ${m.arrival} → ${m.departure} (overbooking prevented).`};
    return {ok:true,reason:`Room ${free[0].id} (${free.length} free)`,room:free[0].id,warning};
  }
  const o=chnOriginal(s,m);
  if(!o)return {ok:false,reason:`Original reservation ${m.ref} not found.`};
  if(o.status==='Rejected')return {ok:false,reason:`Original reservation ${m.ref} was rejected.`,original:o};
  if(o.status==='Pending')return {ok:true,reason:m.kind==='Cancel'?'Withdraws the pending request':'Updates the pending request',original:o};
  const b=s.bookings.find(b=>b.id===o.booking);
  if(!b)return {ok:false,reason:'Linked booking not found.',original:o};
  if(m.kind==='Cancel'){
    if(b.status==='Cancelled')return {ok:true,reason:`${bookingRef(b)} already cancelled`,original:o};
    if(b.status!=='Confirmed')return {ok:false,reason:`${bookingRef(b)} is ${b.status} — handle at the front desk.`,original:o};
    return {ok:true,reason:`Cancels ${bookingRef(b)}`,original:o};
  }
  if(b.status!=='Confirmed')return {ok:false,reason:`${bookingRef(b)} is ${b.status} — handle at the front desk.`,original:o};
  const cap=chnTypeCapacity(s,o.roomType);
  if(party>cap)return {ok:false,reason:`Party of ${party} exceeds ${o.roomType} capacity (${cap}).`,original:o};
  if(m.arrival<s.date||!(nights(m.arrival,m.departure)>0))return {ok:false,reason:'Invalid new dates.',original:o};
  const target=available(s,b.room,m.arrival,m.departure,b.id)?b.room:chnFreeRooms(s,o.roomType,m.arrival,m.departure,party,b.id)[0]?.id;
  if(!target)return {ok:false,reason:`No ${o.roomType} room free for the new dates.`,original:o};
  return {ok:true,reason:target===b.room?`${bookingRef(b)} stays in room ${b.room}`:`${bookingRef(b)} moves to room ${target}`,room:target,original:o};
}
export function chnPending(s:State){return chnSlice(s).messages.filter(m=>m.status==='Pending').sort((a,b)=>a.receivedAt.localeCompare(b.receivedAt));}

// ── Commission report (R9) ──
export type CHNCommissionRow={booking:Booking;channel:string;nights:number;base:number;commission:number;net:number};
export function chnCommissionReport(s:State){
  const st=chnSlice(s);const rows:CHNCommissionRow[]=[];
  for(const b of s.bookings){
    if(!['Confirmed','In house','Departed'].includes(b.status)||!b.otaChannel)continue;
    const c=st.channels.find(c=>c.name===b.otaChannel);if(!c)continue;
    const n=nights(b.arrival,b.departure),base=b.rate*n,commission=Math.round(base*c.commissionBps/10000);
    rows.push({booking:b,channel:c.id,nights:n,base,commission,net:base-commission});
  }
  const byChannel=st.channels.map(c=>{const r=rows.filter(x=>x.channel===c.id);
    return {channel:c,bookings:r.length,nights:r.reduce((a,x)=>a+x.nights,0),base:r.reduce((a,x)=>a+x.base,0),commission:r.reduce((a,x)=>a+x.commission,0),net:r.reduce((a,x)=>a+x.net,0)};});
  const total={bookings:rows.length,nights:rows.reduce((a,x)=>a+x.nights,0),base:rows.reduce((a,x)=>a+x.base,0),commission:rows.reduce((a,x)=>a+x.commission,0),net:rows.reduce((a,x)=>a+x.net,0)};
  return {rows,byChannel,total};
}

// ── Direct booking site (R10–R12) ──
export type CHNQuote={type:string;nights:number;rate:number;net:number;taxBps:number;tax:number;total:number;left:number;capacity:number;ok:boolean;reason:string};
export function chnWebSearchError(s:State,arrival:string,departure:string,adults:number,children:number):string{
  if(!/^\d{4}-\d{2}-\d{2}$/.test(arrival)||!/^\d{4}-\d{2}-\d{2}$/.test(departure))return 'Choose check-in and check-out dates.';
  if(arrival<s.date)return 'Check-in cannot be in the past.';
  const n=nights(arrival,departure);if(!(n>0))return 'Check-out must be after check-in.';
  if(n>30)return 'Stays of up to 30 nights can be requested online.';
  if(!(adults>=1&&adults<=4))return 'Choose 1–4 adults.';
  if(!(children>=0&&children<=3))return 'Choose 0–3 children.';
  return '';
}
export function chnWebQuote(s:State,type:string,arrival:string,departure:string,adults=1,children=0):CHNQuote{
  const room=s.rooms.find(r=>r.type===type);const n=Math.max(0,nights(arrival,departure));
  const capacity=room?resCapacity(room):0,party=adults+children;
  const rate=room?rateFor(s,room,arrival):0,net=rate*n,taxBps=gstForRoomNight(s,arrival,rate).bps,tax=gstStayTax(s,rate,arrival,departure);
  const left=room&&n>0?chnFreeRooms(s,type,arrival,departure,1).length:0;
  const reason=!room?'Not offered':party>capacity?`Sleeps up to ${capacity}`:left===0?'Sold out for these dates':'';
  return {type,nights:n,rate,net,taxBps,tax,total:net+tax,left,capacity,ok:!reason,reason};
}
export function chnWebStatus(s:State,w:CHNWebRequest):'Pending'|'Confirmed'|'Declined'|'Expired'{
  if(w.status!=='Pending')return w.status;
  const b=s.bookings.find(b=>b.id===w.booking);
  return b&&resHoldActive(s,b)?'Pending':'Expired';
}
export const chnWebPending=(s:State)=>chnSlice(s).webRequests.filter(w=>w.status==='Pending');
/** Short marketing copy per room type for the preview page (fictional). */
export const CHN_TYPE_COPY:Record<string,{blurb:string;features:string[]}>={
  Classic:{blurb:'Cosy room with a queen bed, ideal for solo travellers and couples.',features:['Queen bed','Wi-Fi','Tea & coffee']},
  Deluxe:{blurb:'Spacious room with a king bed and a sitting corner.',features:['King bed','City view','Work desk']},
  Suite:{blurb:'Separate living area, ideal for families of up to four.',features:['Living room','Bathtub','Breakfast for 2']},
};

// ── ACC-2 flows · OTA payouts (contract §4, rulebook A13 / §4.14, DEC-150) ──
// At check-out an OTA-paid stay moves its folio balance to the channel's receivable (receipt kind 'ota-credit', Journal Dr 1115/<channel>
// · Cr 1100), allowed only when the booking's source is a CONNECTED channel. The OTA later pays net of its commission (+ 18 % GST) and the
// GST TCS, with a statement: CHN_PAYOUT records it (Receipt Dr 1030/<bank> net · Dr 5070 commission (+ GST unless ITC is allowed) · Dr 1340
// TCS · Cr 1115/<channel> gross). gross = net + commission + commissionGst + tcs; gross ≤ the channel's open receivable.
/** The channel of a booking whose source is an OTA (`otaChannel` = the channel's name or id), else undefined. */
export function chnChannelOfBooking(s:State,b:Pick<Booking,'source'|'otaChannel'>):CHNChannel|undefined{
  const n=String(b.otaChannel??'').trim().toLowerCase();if(!n||!/^ota/i.test(String(b.source??'')))return undefined;
  return chnSlice(s).channels.find(c=>c.id.toLowerCase()===n||c.name.toLowerCase()===n);
}
/** Why an OTA booking cannot go on OTA credit at check-out ('' = it can): its source must be a connected channel. */
export function chnOtaCreditRefusal(s:State,b:Booking):string{
  const c=chnChannelOfBooking(s,b);
  if(!c)return 'This booking did not come from an OTA channel. Only a booking whose source is a connected OTA channel can be settled to the OTA.';
  if(c.status!=='Connected')return `${c.name} is ${c.status.toLowerCase()} — settle the folio to the OTA only through a connected channel.`;
  return '';
}
export const chnPayouts=(s:State,channel?:string)=>(chnSlice(s).payouts??[]).filter(p=>!channel||p.channel===channel);
/** OTA credits (folio balances moved to the channel at check-out) per booking of a channel. */
export function chnOtaCredits(s:State,channel:string):{booking:string;guest:string;date:string;amount:number}[]{
  const out:{booking:string;guest:string;date:string;amount:number}[]=[];
  for(const b of s.bookings){if(chnChannelOfBooking(s,b)?.id!==channel)continue;
    for(const r of b.receipts)if(r.kind==='ota-credit')out.push({booking:b.id,guest:b.guest,date:r.date,amount:r.amount});}
  return out;
}
/** The channel's open receivable from the records (OTA credits − payouts gross). The command checks the 1115/<channel> ledger balance. */
export const chnOtaOpen=(s:State,channel:string)=>chnOtaCredits(s,channel).reduce((a,x)=>a+x.amount,0)-chnPayouts(s,channel).reduce((a,p)=>a+p.gross,0);
/** Bookings of a channel with an OTA credit not yet named by a payout (the statement picker). */
export const chnOtaUnpaid=(s:State,channel:string)=>{const paid=new Set(chnPayouts(s,channel).flatMap(p=>p.bookings));return chnOtaCredits(s,channel).filter(x=>!paid.has(x.booking));};
export type CHNPayoutInput={channel:string;statementRef:string;gross:number;commission:number;commissionGst:number;tcs:number;net:number;bank:string;bookings?:string[]};
/** Arithmetic and shape of a payout statement ('' = fine); the open receivable is checked by the command against the books. */
export function chnPayoutProblem(x:Partial<CHNPayoutInput>):string{
  const n=(v:unknown)=>Number.isSafeInteger(v)&&(v as number)>=0;
  if(!String(x.statementRef??'').trim())return 'Enter the OTA statement reference.';
  if(String(x.statementRef).trim().length>40)return 'Keep the statement reference to 40 characters.';
  for(const [k,label] of [['gross','Gross'],['commission','Commission'],['commissionGst','GST on the commission'],['tcs','TCS'],['net','Net received']] as const)
    if(!n(x[k]))return `${label}: enter an amount of ₹0 or more.`;
  if(!(x.gross! >0)||!(x.net! >0))return 'Enter the gross amount and the net amount received (both above ₹0).';
  if(x.gross!>100000000000)return 'The gross amount is too large.';
  if(x.net!+x.commission!+x.commissionGst!+x.tcs!!==x.gross)return `Gross ${money(x.gross!)} must equal net ${money(x.net!)} + commission ${money(x.commission!)} + GST on it ${money(x.commissionGst!)} + TCS ${money(x.tcs!)} (= ${money(x.net!+x.commission!+x.commissionGst!+x.tcs!)}).`;
  return '';
}
