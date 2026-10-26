// MA-1 · Rooms master commands beyond the generic MST_MASTER_* (which edit / retire / reactivate / delete rooms, room types, amenities
// and occupancy charges through domain/streams/roommasters.ts specs):
//   RMS_ROOMS_ADD {rooms:'101–110', floor:'F1', roomType:'DLX'}   range add (≤ 50), numbers unused (also on past stays), active floor / type
//   RMS_ROOM_RENUMBER {id, to}                                 only with no booking from today on; past stays keep the old number
//   RMS_ROOM_OOO {id, from, to?, reason}                       out of order = a room hold (not a block); refused over live bookings
//   RMS_ROOM_OOO_CLEAR {id}
// Permission: the rooms master's (`masters`, DEC-MA-1). Audit "Master Rooms: …" and one history row per change (s.mastersX.history).
// Extra adult / child / bed charges are posted with the existing CHARGE command (label + amount from rmsExtraQuote, gst 'other').
import type {Handler,Kit} from '../commands';
import type {State,Room} from '../../../domain/types';
import {can} from '../../../domain/selectors';
import {mstCanEdit,mstCanEditMoney,mstFail,mstByCode,mstFloorNumber,MST_FLOORS,type MSTState} from '../../../domain/streams/masters';
import {ROOM_SPEC,MST_ROOMS,MST_ROOM_TYPES,rmsParseRooms,rmsNumberTaken,rmsRangeLabel,rmsLiveBookings,rmsLockText,rmsRackOn,rmsTypeKey,rmsTypeName,
  RMS_ROOM_NO,type RmsTypeRow} from '../../../domain/streams/roommasters';

const ISO=/^\d{4}-\d{2}-\d{2}$/;
const slice=(s:State):MSTState=>{const x=s.mastersX??(s.mastersX={rows:{},history:[]});x.rows??={};x.history??=[];return x;};
function gate(k:Kit){
  if(!can(k.s,k.uid,'masters')&&!mstCanEditMoney(k.s,k.uid))k.fail('FORBIDDEN','Access denied: masters permission is required.');
  const d=mstCanEdit(k.s,k.uid,ROOM_SPEC as never);if(!d.ok)k.fail('FORBIDDEN',`Access denied: ${d.reason}`);
}
const hist=(k:Kit,room:string,field:string,old:string,nu:string)=>
  slice(k.s).history.unshift({id:k.id('MSTH'),master:MST_ROOMS,rowId:room,code:room,field,old,new:nu,by:k.uid,at:k.s.clock});
function roomOf(k:Kit):Room{
  const r=k.s.rooms.find(x=>x.id===String(k.cmd.id??''));if(!r)k.fail('NOT_FOUND','This room was not found.');
  if(r!.status==='retired')mstFail('MASTER_RETIRED',`Room ${r!.id} is retired — reactivate it first.`);
  return r!;
}

const RMS_ROOMS_ADD:Handler=k=>{
  gate(k);const {s,cmd}=k;
  const p=rmsParseRooms(String(cmd.rooms??''));if(p.error)mstFail('MASTER_INVALID',p.error,'rooms');
  const floor=mstByCode(s,MST_FLOORS,String(cmd.floor??'').toUpperCase());
  if(!floor)mstFail('MASTER_INVALID','Choose a floor.','floor');
  if(!floor!.active)mstFail('MASTER_RETIRED',`${floor!.name} is retired — choose an active floor.`,'floor');
  const n=mstFloorNumber(floor!.code);if(n===undefined)mstFail('MASTER_INVALID',`Floor ${floor!.code}: rooms can be placed on floors coded F<number> (F0 = ground).`,'floor');
  const type=mstByCode(s,MST_ROOM_TYPES,String(cmd.roomType??'').toUpperCase()) as RmsTypeRow|undefined;
  if(!type)mstFail('MASTER_INVALID','Choose a room type.','type');
  if(!type!.active)mstFail('MASTER_RETIRED',`${type!.name} is retired — choose an active room type.`,'type');
  const key=rmsTypeKey(type!),rate=rmsRackOn(s,key,s.date);
  if(!rate)mstFail('MASTER_INVALID',`Set the rack rate of ${type!.name} before adding rooms of that type.`,'type');
  for(const no of p.numbers){const why=rmsNumberTaken(s,no);if(why)mstFail('MASTER_CODE_TAKEN',why,'rooms');}
  s.rooms=[...s.rooms,...p.numbers.map(id=>({id,floor:n!,type:key,rate:rate!,hk:'Ready' as const,blocked:false,addedOn:s.date}))];
  for(const no of p.numbers)hist(k,no,'added','—',`${floor!.name} · ${type!.name}`);
  return `Master Rooms: added ${rmsRangeLabel(p.numbers)} on ${floor!.name} · ${type!.name}`;
};

const RMS_ROOM_RENUMBER:Handler=k=>{
  gate(k);const {s,cmd}=k;const r=roomOf(k),to=String(cmd.to??'').trim().toUpperCase();
  if(!RMS_ROOM_NO.test(to))mstFail('MASTER_INVALID','New number: 1–10 capital letters, digits or “-”.','to');
  if(to===r.id)k.fail('VALIDATION','Nothing changed.');
  const why=rmsNumberTaken(s,to);if(why)mstFail('MASTER_CODE_TAKEN',why,'to');
  const l=rmsLiveBookings(s,r.id);
  if(l.length)mstFail('MASTER_LOCKED_BY_BOOKINGS',`Room ${r.id} has bookings ${rmsLockText(l)} — renumber it when no booking remains (move them first).`);
  const old=r.id;
  s.rooms=s.rooms.map(x=>x.id===old?{...x,id:to}:x.connecting===old?{...x,connecting:to}:x);
  if(s.tasks.some(t=>t.room===old&&t.status!=='Done'))s.tasks=s.tasks.map(t=>t.room===old&&t.status!=='Done'?{...t,room:to}:t);
  hist(k,to,'code',old,to);
  return `Master Rooms: code ${old} → ${to} (renumbered; past stays keep room ${old})`;
};

const RMS_ROOM_OOO:Handler=k=>{
  gate(k);const {s,cmd}=k;const r=roomOf(k);
  const from=String(cmd.from??''),to=cmd.to?String(cmd.to):undefined,reason=String(cmd.reason??'').trim();
  if(!ISO.test(from))mstFail('MASTER_INVALID','Choose the first night out of order.','from');
  if(from<s.date)mstFail('MASTER_INVALID','Out of order cannot start before the current business date.','from');
  if(to!==undefined&&(!ISO.test(to)||to<=from))mstFail('MASTER_INVALID','The last date must come after the first night (or leave it open).','to');
  if(reason.length<3||reason.length>120)mstFail('MASTER_INVALID','Give a reason (3–120 characters).','reason');
  const clash=rmsLiveBookings(s,r.id).filter(b=>b.arrival<(to??'9999-12-31')&&b.departure>from);
  if(clash.length)mstFail('MASTER_LOCKED_BY_BOOKINGS',`Room ${r.id} is booked on those nights: ${rmsLockText(clash)} — move the bookings first.`);
  const before=r.ooo?`${r.ooo.from}–${r.ooo.to??'open'} · ${r.ooo.reason}`:'in service';
  const ooo={from,...(to?{to}:{}),reason};
  s.rooms=s.rooms.map(x=>x.id===r.id?{...x,status:'out-of-order' as const,ooo}:x);
  const after=`${from}–${to??'open'} · ${reason}`;hist(k,r.id,'status',before,'out of order '+after);
  return `Master Rooms: status ${r.id} ${before} → out of order ${after} (${rmsTypeName(s,r.type)})`;
};

const RMS_ROOM_OOO_CLEAR:Handler=k=>{
  gate(k);const {s}=k;const r=roomOf(k);
  if(!r.ooo)k.fail('INVALID_TRANSITION',`Room ${r.id} is not out of order.`);
  const before=`out of order ${r.ooo!.from}–${r.ooo!.to??'open'} · ${r.ooo!.reason}`;
  s.rooms=s.rooms.map(x=>{if(x.id!==r.id)return x;const n={...x};delete n.ooo;delete n.status;return n;});
  hist(k,r.id,'status',before,'in service');
  return `Master Rooms: status ${r.id} ${before} → in service`;
};

export const handlers:Record<string,Handler>={RMS_ROOMS_ADD,RMS_ROOM_RENUMBER,RMS_ROOM_OOO,RMS_ROOM_OOO_CLEAR};
