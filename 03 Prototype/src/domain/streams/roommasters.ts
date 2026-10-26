// MA-1 · Rooms and room types masters (plan HCC_Customisation_Features_Plan_v1.md Part 2 §8 M2–M5, briefs MA-1). Four masters on the
// MA-0 framework (domain/streams/masters.ts), all in Master configuration › Rooms and rate plans:
//   roomTypes        (M2) code, name, max adults/children, extra bed, bed type, size, amenities, description, rack rate (dated money) —
//                    replaces the derived adminRoomTypes. Rows live in s.mastersX.rows.roomTypes (seeded from the fixture rooms); the
//                    rack rate is NOT a second copy: it is read from and written to the tariff schedule (base = the rooms' rate, dated
//                    amounts = s.tariffs of the type), so rateFor() and the Tariffs screen stay the single source.
//   rooms            (M3) backed by s.rooms itself through the spec `store` adapter (no second copy): number, floor (floors master),
//                    type (roomTypes), amenity overrides, smoking, connecting room, extension, notes; retire keeps history; out of
//                    order = a room hold (registerRoomHold) so availability, calendar, board, housekeeping and yield all follow.
//   amenities        (M4) code, name, icon, shown on the booking site.
//   occupancyCharges (M5) per room type: extra adult / child / bed as dated money + child age band; posted as a plain CHARGE with GST
//                    class "other services" (MA-2's extras catalogue has not landed in this branch), label "… (<code>)".
// Room.type keeps the type KEY (fixture: 'Classic' / 'Deluxe' / 'Suite'; production: the server type code). A room-type row carries
// `key` (immutable) next to its `code`, so renaming a type renames it on every screen without rewriting rooms, tariffs or history.
// Room.floor stays a number: floor code F<n> ↔ n (MA-0); rooms can therefore be placed on floors coded F<number> only.
import type {State,Room,Booking} from '../types';
import {addDays} from '../money';
import {registerRoomHold} from '../selectors';
import {
  registerMaster,mstRows,mstByCode,mstFloorCode,mstFloorNumber,mstMoneyOn,mstFail,MST_FLOORS,
  type MasterRow,type MasterSpec,type MstDatedAmount,type MSTState,
} from './masters';
import {seedRoomTypes,seedAmenities,seedOccupancy} from '../../fixtures/ext/masters-seed';
import {rmsRetiredDuring,rmsOooDuring,rmsListed} from './roomoffline';
export {rmsOffline,rmsListed,rmsRetiredDuring,rmsOooDuring} from './roomoffline';

export const MST_ROOM_TYPES='roomTypes';
export const MST_ROOMS='rooms';
export const MST_AMENITIES='amenities';
export const MST_OCC_CHARGES='occupancyCharges';
/** effectiveFrom of the base rack rate (the rooms' own rate, used before any dated tariff). */
export const RMS_BASE_FROM='2000-01-01';
/** Most rooms one range add may create. */
export const RMS_RANGE_MAX=50;
export const RMS_ROOM_NO=/^[A-Z0-9-]{1,10}$/;

export type RmsTypeRow=MasterRow&{key?:string;maxAdults?:number;maxChildren?:number;extraBedAllowed?:boolean;bedType?:string;sizeSqft?:number;
  amenities?:string[];description?:string;rackRate?:MstDatedAmount[]};
export type RmsRoomRow=MasterRow&{floor:string;type:string;features?:string[];smoking?:boolean;connecting?:string;extension?:string;notes?:string;
  ooo?:Room['ooo']};
export type RmsOccRow=MasterRow&{roomType:string;extraAdult?:MstDatedAmount[];extraChild?:MstDatedAmount[];extraBed?:MstDatedAmount[];childAgeFrom?:number;childAgeTo?:number};

const slice=(s:State):MSTState=>{const x=s.mastersX??(s.mastersX={rows:{},history:[]});x.rows??={};x.history??=[];return x;};
const same=(a:unknown,b:unknown)=>JSON.stringify(a??null)===JSON.stringify(b??null);

// ───────────────────────────── Bookings that lock a room ─────────────────────────────
const live=(s:State,b:Booking)=>b.status==='In house'||((b.status==='Confirmed'||(b.status==='Held'&&(!b.holdUntil||s.clock<b.holdUntil)))&&b.departure>s.date);
/** Live bookings of a room (in house, or confirmed / held with nights from today on) — they lock renumbering, type change and retiring. */
export const rmsLiveBookings=(s:State,room:string)=>s.bookings.filter(b=>b.room===room&&live(s,b)).sort((a,b)=>a.arrival<b.arrival?-1:1);
/** "HCC-1041, HCC-1050 (until 2026-10-28)" */
export function rmsLockText(list:Booking[]){
  const last=list.reduce((m,b)=>b.departure>m?b.departure:m,'');
  return `${list.slice(0,4).map(b=>b.id).join(', ')}${list.length>4?` +${list.length-4}`:''} (booked until ${last})`;
}

// ───────────────────────────── Choices (the seed rows themselves: fixtures/ext/masters-seed.ts) ─────────────────────────────
export const RMS_AMENITY_ICONS=['wifi','snowflake','tv','coffee','briefcase','building','sofa','bath','utensils','bed','car','dumbbell','waves','cigarette-off','accessibility','baby'] as const;
export const RMS_BED_TYPES=['Single','Twin','Double','Queen','King'];

// ───────────────────────────── Room types (M2) ─────────────────────────────
export const rmsTypeKey=(r:MasterRow)=>String(r.key??r.code);
/** Seed rows of the room types (fixtures/ext/masters-seed.ts); `keys` = only these types. */
const seedTypes=(s:State,keys?:string[]):RmsTypeRow[]=>seedRoomTypes(s.rooms,keys);
/** Dated rack rate of a type: the base (first room's rate) from RMS_BASE_FROM, then every dated tariff of the type. */
export function rmsDerivedRack(s:State,key:string):MstDatedAmount[]{
  const base=s.rooms.find(r=>r.type===key)?.rate;
  const out=new Map<string,number>();if(base!==undefined)out.set(RMS_BASE_FROM,base);
  for(const t of s.tariffs??[])if(t.type===key)out.set(t.effectiveFrom,t.rate);
  return [...out].map(([effectiveFrom,amount])=>({amount,effectiveFrom})).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
}
function readTypes(s:State):RmsTypeRow[]{
  const stored=s.mastersX?.rows?.[MST_ROOM_TYPES] as RmsTypeRow[]|undefined;
  const rows:RmsTypeRow[]=Array.isArray(stored)?stored.map(r=>({...r})):seedTypes(s);
  const known=new Set(rows.map(rmsTypeKey));
  const missing=[...new Set(s.rooms.map(r=>r.type))].filter(k=>!known.has(k));
  if(missing.length){const last=rows.reduce((m,r)=>Math.max(m,r.sortOrder),0);
    rows.push(...seedTypes(s,missing).map((r,i)=>({...r,sortOrder:last+(i+1)*10,id:rows.some(x=>x.id===r.id)?r.id+'-'+i:r.id})));}
  return rows.map(r=>({...r,rackRate:Array.isArray(r.rackRate)?r.rackRate:rmsDerivedRack(s,rmsTypeKey(r))}));
}
function writeTypes(s:State,rows:RmsTypeRow[]){
  slice(s).rows[MST_ROOM_TYPES]=rows.map(r=>{const {rackRate:_r,...rest}=r;return {...rest,key:rest.key??rest.code};});
  // Rack rate → tariff schedule (amounts from today's business date on; past amounts are frozen by mstProblems).
  let tariffs=s.tariffs??[],changed=false;
  for(const r of rows){
    const key=String(r.key??r.code),want=(r.rackRate??[]).filter(x=>x.effectiveFrom>=s.date&&x.effectiveFrom!==RMS_BASE_FROM)
      .map(x=>({type:key,rate:x.amount,effectiveFrom:x.effectiveFrom})).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
    const have=tariffs.filter(t=>t.type===key&&t.effectiveFrom>=s.date).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
    if(!same(want,have)){tariffs=[...tariffs.filter(t=>!(t.type===key&&t.effectiveFrom>=s.date)),...want];changed=true;}
  }
  if(changed)s.tariffs=tariffs;
}
/** Type row of a room type key (or code); retired rows resolve. */
export const rmsTypeRow=(s:State,key:string):RmsTypeRow|undefined=>(mstRows(s,MST_ROOM_TYPES) as RmsTypeRow[]).find(r=>rmsTypeKey(r)===key)??mstByCode(s,MST_ROOM_TYPES,key) as RmsTypeRow|undefined;
/** Display name of a room type key (unknown → the key itself). */
export const rmsTypeName=(s:State,key:string)=>rmsTypeRow(s,key)?.name??key;
/** Room type key of a master code (the room's stored value). */
export const rmsTypeKeyOfCode=(s:State,code:string)=>{const r=mstByCode(s,MST_ROOM_TYPES,code);return r?rmsTypeKey(r):code;};
/** Room types that have rooms in service, in master order (Tariffs, group rates, channel lists). Replaces the old derivation. */
export function rmsRoomTypeKeys(s:State):string[]{
  const have=new Set(s.rooms.filter(rmsListed).map(r=>r.type));
  const order=(mstRows(s,MST_ROOM_TYPES) as RmsTypeRow[]).map(rmsTypeKey);
  return [...have].sort((a,b)=>(order.indexOf(a)+1||1e6)-(order.indexOf(b)+1||1e6)||a.localeCompare(b));
}
/** Rack rate of a type on a date: the master's dated amount, else the base rate of its rooms. */
export function rmsRackOn(s:State,key:string,date:string):number|undefined{
  const row=rmsTypeRow(s,key);const v=row?mstMoneyOn(row.rackRate,date)??(row.rackRate??[]).find(x=>x.effectiveFrom>date)?.amount:undefined;
  return v??s.rooms.find(r=>r.type===key)?.rate;
}
/** Sleeps (max adults + max children) of a type; undefined when the master has no row. */
export const rmsCapacity=(s:State,key:string)=>{const r=rmsTypeRow(s,key);return r?Number(r.maxAdults??0)+Number(r.maxChildren??0):undefined;};
/** Booking-site copy of a type from the master (name, description, bed + amenities shown on the website, photo placeholder). */
export function rmsTypeCopy(s:State,key:string):{name:string;blurb:string;features:string[];photos:string[];sizeSqft?:number;sleeps?:number}|undefined{
  const r=rmsTypeRow(s,key);if(!r)return undefined;
  const am=(r.amenities??[]).map(c=>mstByCode(s,MST_AMENITIES,c)).filter(a=>a&&a.active&&a.showOnWebsite!==false).map(a=>a!.name);
  return {name:r.name,blurb:String(r.description??''),features:[...(r.bedType?[`${r.bedType} bed`]:[]),...am],photos:[],
    ...(r.sizeSqft?{sizeSqft:Number(r.sizeSqft)}:{}),sleeps:rmsCapacity(s,key)};
}
export const ROOM_TYPE_SPEC:MasterSpec<RmsTypeRow>={
  id:MST_ROOM_TYPES,label:'Room types',singular:'room type',group:'rooms',prefix:'RTY',money:false,permission:'masters',
  help:'Types sold to guests: occupancy, bed, size, amenities, description (booking site) and the dated rack rate (the same schedule as Tariffs).',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'DLX',help:'Short code, e.g. DLX. Cannot change once rooms use it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Deluxe',help:'Renaming changes it on every screen; rooms, tariffs and history keep the type.'},
    {key:'maxAdults',label:'Max adults',type:'number',required:true,min:1,max:8},
    {key:'maxChildren',label:'Max children',type:'number',min:0,max:6},
    {key:'extraBedAllowed',label:'Extra bed allowed',type:'toggle'},
    {key:'bedType',label:'Bed type',type:'select',options:RMS_BED_TYPES.map(b=>({value:b,label:b}))},
    {key:'sizeSqft',label:'Size (sq ft)',type:'number',min:50,max:10000},
    {key:'amenities',label:'Amenities',type:'multiselect',master:MST_AMENITIES},
    {key:'description',label:'Description (booking site)',type:'text',maxLength:500},
    {key:'rackRate',label:'Rack rate per night',type:'datedMoney',money:true,help:'The first amount is the base rack rate; a dated amount changes it from that date (the Tariffs schedule). Owner only.'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
  ],
  inUse:(s,row)=>{const key=rmsTypeKey(row),rooms=s.rooms.filter(r=>r.type===key);
    return {count:rooms.length,where:rooms.length?[`Rooms ${rooms.map(r=>r.id).join(', ')}`]:[]};},
  canRetire:(s,row)=>{const key=rmsTypeKey(row),rooms=s.rooms.filter(r=>r.type===key&&rmsListed(r));
    return rooms.length?`${row.name} still has rooms in service (${rooms.map(r=>r.id).join(', ')}) — change their type or retire them first.`:undefined;},
  validate:(_s,row)=>{
    if(!Number.isInteger(row.maxAdults))return 'Max adults: a whole number.';
    if(row.maxChildren!==undefined&&!Number.isInteger(row.maxChildren))return 'Max children: a whole number.';
    return undefined;},
  store:{read:readTypes,write:writeTypes},
};
/** Live bookings of a type with more guests than a proposed occupancy (warning in the form and the audit; never refused). */
export function rmsOverOccupancy(s:State,key:string,max:number):Booking[]{
  return s.bookings.filter(b=>{const r=s.rooms.find(x=>x.id===b.room);return r?.type===key&&live(s,b)&&b.people>max;});
}

// ───────────────────────────── Rooms (M3) — backed by s.rooms ─────────────────────────────
const roomNum=(id:string)=>/^\d+$/.test(id)?Number(id):Number.MAX_SAFE_INTEGER;
function readRooms(s:State):RmsRoomRow[]{
  const sorted=[...s.rooms].sort((a,b)=>a.floor-b.floor||roomNum(a.id)-roomNum(b.id)||a.id.localeCompare(b.id));
  return sorted.map((r,i)=>{const row:RmsRoomRow={id:r.id,code:r.id,name:'Room '+r.id,sortOrder:(i+1)*10,active:r.status!=='retired',
    floor:mstFloorCode(r.floor),type:rmsTypeRow(s,r.type)?.code??r.type,features:r.features??[],smoking:!!r.smoking};
    if(r.retiredOn)row.retiredOn=r.retiredOn;if(r.connecting)row.connecting=r.connecting;if(r.extension)row.extension=r.extension;if(r.notes)row.notes=r.notes;if(r.ooo)row.ooo={...r.ooo};
    return row;});
}
const opt=(o:Record<string,unknown>,k:string,v:unknown)=>{if(v===undefined||v===null||v===''||v===false||(Array.isArray(v)&&!v.length))delete o[k];else o[k]=v;};
function writeRooms(s:State,rows:RmsRoomRow[]){
  const byRow=new Map(rows.map(r=>[r.id,r]));let changed=false;const out:Room[]=[];let tasks=s.tasks;
  const apply=(prev:Room|undefined,row:RmsRoomRow):Room=>{
    const n=mstFloorNumber(String(row.floor));
    if(n===undefined)mstFail('MASTER_INVALID',`Floor ${row.floor}: rooms can be placed on floors coded F<number> (F0 = ground).`,'floor');
    const key=rmsTypeKeyOfCode(s,String(row.type));
    const next:Room=prev?{...prev}:{id:String(row.code),floor:n!,type:key,rate:0,hk:'Ready',blocked:false};
    if(!prev){const rate=rmsRackOn(s,key,s.date);if(!rate)mstFail('MASTER_INVALID',`Set the rack rate of ${row.type} before adding rooms of that type.`,'type');next.rate=rate!;}
    if(prev&&row.code!==prev.id){next.id=String(row.code);tasks=tasks.map(t=>t.room===prev.id?{...t,room:next.id}:t);}
    if(prev&&key!==prev.type){const l=rmsLiveBookings(s,prev.id);
      if(l.length)mstFail('MASTER_LOCKED_BY_BOOKINGS',`Room ${prev.id} has bookings ${rmsLockText(l)} — change its type from a date with no booking (move them first).`,'type');
      next.type=key;next.rate=rmsRackOn(s,key,s.date)??prev.rate;}
    next.floor=n!;
    const o=next as unknown as Record<string,unknown>;
    opt(o,'features',Array.isArray(row.features)?[...row.features]:undefined);opt(o,'smoking',!!row.smoking);
    for(const k of ['connecting','extension','notes'] as const)opt(o,k,typeof row[k]==='string'?String(row[k]).trim():undefined);
    if(!row.active){next.status='retired';next.retiredOn=row.retiredOn??s.date;delete next.ooo;}
    else if(prev?.status==='retired'){delete next.status;delete next.retiredOn;}
    return next;
  };
  for(const prev of s.rooms){const row=byRow.get(prev.id);if(!row){changed=true;continue;}const next=apply(prev,row);if(!same(next,prev))changed=true;out.push(next);byRow.delete(prev.id);}
  for(const row of byRow.values()){out.push(apply(undefined,row));changed=true;}
  if(changed)s.rooms=out;
  if(tasks!==s.tasks)s.tasks=tasks;
}
export const ROOM_SPEC:MasterSpec<RmsRoomRow>={
  id:MST_ROOMS,label:'Rooms',singular:'room',group:'rooms',prefix:'RM',money:false,permission:'masters',
  help:'Every room the hotel sells: floor, type, amenities and notes. Add many at once (101–110), renumber or set out of order with the tools above the list; retire keeps history.',
  fields:[
    {key:'code',label:'Room number',type:'text',required:true,maxLength:10,placeholder:'111',help:'Renumber an existing room with “Renumber” (only with no booking from today on).'},
    {key:'floor',label:'Floor',type:'select',master:MST_FLOORS,required:true},
    {key:'type',label:'Room type',type:'select',master:MST_ROOM_TYPES,required:true,help:'Changes only when the room has no booking from today on.'},
    {key:'features',label:'Extra amenities (on top of the type)',type:'multiselect',master:MST_AMENITIES},
    {key:'smoking',label:'Smoking room',type:'toggle'},
    {key:'connecting',label:'Connecting room',type:'text',maxLength:10,placeholder:'102'},
    {key:'extension',label:'Phone extension',type:'text',maxLength:6,placeholder:'2101'},
    {key:'notes',label:'Notes for staff',type:'text',maxLength:200},
  ],
  inUse:(s,row)=>{const stays=s.bookings.filter(b=>b.room===row.id),tasks=s.tasks.filter(t=>t.room===row.id);
    const where:string[]=[];if(stays.length)where.push(`Stays ${stays.slice(0,5).map(b=>b.id).join(', ')}${stays.length>5?` +${stays.length-5}`:''}`);if(tasks.length)where.push(`Housekeeping tasks ${tasks.length}`);
    return {count:stays.length+tasks.length,where};},
  canRetire:(s,row)=>{const l=rmsLiveBookings(s,row.id);return l.length?`Room ${row.id} has bookings ${rmsLockText(l)} — move them before retiring the room.`:undefined;},
  validate:(s,row)=>{
    if(!RMS_ROOM_NO.test(String(row.code)))return 'Room number: 1–10 capital letters, digits or “-”.';
    if(mstFloorNumber(String(row.floor))===undefined)return `Floor ${row.floor}: rooms can be placed on floors coded F<number> (F0 = ground).`;
    if(row.connecting&&(row.connecting===row.code||!s.rooms.some(r=>r.id===row.connecting)))return `Connecting room ${row.connecting}: choose another existing room.`;
    if(row.extension&&!/^\d{2,6}$/.test(String(row.extension)))return 'Phone extension: 2–6 digits.';
    return undefined;},
  store:{read:readRooms,write:writeRooms},
};
/** Room numbers already used: rooms (incl. retired) and numbers on past stays (renumbered rooms keep their old stays). */
export function rmsNumberTaken(s:State,no:string):string|undefined{
  if(s.rooms.some(r=>r.id===no))return `Room ${no} already exists${s.rooms.find(r=>r.id===no)?.status==='retired'?' (retired)':''}.`;
  if(s.bookings.some(b=>b.room===no))return `Room number ${no} appears on past stays — choose another number.`;
  return undefined;
}
/** Parse "101–110", "101-110", "101 to 110", "101, 103, 105-107" into room numbers (upper-case, max RMS_RANGE_MAX). */
export function rmsParseRooms(text:string):{numbers:string[];error?:string}{
  const norm=String(text??'').toUpperCase().replace(/[–—]/g,'-').replace(/\s+TO\s+/g,'-').replace(/(\d)\s*-\s*(\d)/g,'$1-$2');
  const out:string[]=[];
  for(const part of norm.split(/[,;\s]+/).filter(Boolean)){
    const m=/^(\d+)-(\d+)$/.exec(part);
    if(m){const [,a,b]=m;const from=Number(a),to=Number(b);
      if(to<from)return {numbers:[],error:`${part}: the range must go up (${a} to ${b}).`};
      if(to-from+1>RMS_RANGE_MAX)return {numbers:[],error:`At most ${RMS_RANGE_MAX} rooms at once.`};
      for(let n=from;n<=to;n++)out.push(String(n).padStart(a.length,'0'));}
    else if(RMS_ROOM_NO.test(part))out.push(part);
    else return {numbers:[],error:`${part}: room numbers are 1–10 capital letters, digits or “-”.`};
  }
  if(!out.length)return {numbers:[],error:'Enter room numbers, e.g. 101–110 or 101, 103.'};
  if(out.length>RMS_RANGE_MAX)return {numbers:[],error:`At most ${RMS_RANGE_MAX} rooms at once.`};
  const dup=out.find((x,i)=>out.indexOf(x)!==i);if(dup)return {numbers:[],error:`Room ${dup} is listed twice.`};
  if(out.some(x=>!RMS_ROOM_NO.test(x)))return {numbers:[],error:'Room numbers are at most 10 characters.'};
  return {numbers:out};
}
/** "101–110 (10 rooms)" / "101, 103 (2 rooms)" */
export const rmsRangeLabel=(n:string[])=>`${n.length>2&&n.every((x,i)=>i===0||Number(x)===Number(n[i-1])+1)?`${n[0]}–${n.at(-1)}`:n.join(', ')} (${n.length} room${n.length===1?'':'s'})`;
/** Out-of-order rooms (window running today or later), for the tools panel and boards. */
export const rmsOooRooms=(s:State)=>s.rooms.filter(r=>r.ooo&&(r.ooo.to??'9999-12-31')>s.date);
/** "Deluxe · Ground floor" — the room-picker line (type name from the master, floor name from the Floors master). */
export function rmsRoomLine(s:State,r:Room){const f=mstByCode(s,MST_FLOORS,mstFloorCode(r.floor))?.name??'Floor '+r.floor;return `${rmsTypeName(s,r.type)} · ${f}`;}
/** Rooms shown on boards (retired rooms hidden; they stay in history). */
export const rmsBoardRooms=(s:State)=>s.rooms.filter(rmsListed);

// Out-of-order and retired rooms are room holds: available(), the calendar, the board ("Blocked"), channel pushes and pickers follow.
registerRoomHold((s,room,a,d)=>{
  const r=s.rooms.find(x=>x.id===room);if(!r||(!r.status&&!r.ooo))return undefined;
  if(rmsRetiredDuring(r,a,d))return {id:'rms-retired:'+room,label:'Retired room'};
  if(rmsOooDuring(r,a,d))return {id:'rms-ooo:'+room,label:`Out of order · ${r.ooo!.reason}`};
  return undefined;
});

// ───────────────────────────── Amenities (M4) ─────────────────────────────
export const AMENITY_SPEC:MasterSpec={
  id:MST_AMENITIES,label:'Amenities',singular:'amenity',group:'rooms',prefix:'AMN',money:false,permission:'masters',
  help:'Room features picked by room types and rooms; the ones shown on the booking site appear on its room cards.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'WIFI'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Wi-Fi'},
    {key:'icon',label:'Icon',type:'select',options:RMS_AMENITY_ICONS.map(i=>({value:i,label:i}))},
    {key:'showOnWebsite',label:'Shown on the booking site',type:'toggle'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
  ],
  inUse:(s,row)=>{const types=(mstRows(s,MST_ROOM_TYPES) as RmsTypeRow[]).filter(t=>(t.amenities??[]).includes(row.code)),rooms=s.rooms.filter(r=>(r.features??[]).includes(row.code));
    const where:string[]=[];if(types.length)where.push(`Room types ${types.map(t=>t.name).join(', ')}`);if(rooms.length)where.push(`Rooms ${rooms.map(r=>r.id).join(', ')}`);
    return {count:types.length+rooms.length,where};},
  seed:()=>seedAmenities(),
};

// ───────────────────────────── Occupancy and extra charges (M5) ─────────────────────────────
export type RmsExtraKind='adult'|'child'|'bed';
export const RMS_EXTRA_LABEL:Record<RmsExtraKind,string>={adult:'Extra adult',child:'Extra child',bed:'Extra bed'};
const OCC_FIELD:Record<RmsExtraKind,'extraAdult'|'extraChild'|'extraBed'>={adult:'extraAdult',child:'extraChild',bed:'extraBed'};
export const OCC_CHARGE_SPEC:MasterSpec<RmsOccRow>={
  id:MST_OCC_CHARGES,label:'Occupancy and extra charges',singular:'occupancy charge',group:'rooms',prefix:'OCC',money:true,permission:'settings',
  help:'Per room type: extra adult, extra child and extra bed per night (dated amounts) and the child age band. Posted on the folio as a service line (GST other services). Owner only.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'X-DLX'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Deluxe extras'},
    {key:'roomType',label:'Room type',type:'select',master:MST_ROOM_TYPES,required:true},
    {key:'extraAdult',label:'Extra adult per night',type:'datedMoney'},
    {key:'extraChild',label:'Extra child per night',type:'datedMoney'},
    {key:'extraBed',label:'Extra bed per night',type:'datedMoney'},
    {key:'childAgeFrom',label:'Child age from',type:'number',min:0,max:17},
    {key:'childAgeTo',label:'Child age to',type:'number',min:0,max:17,help:'Older guests are charged as adults; younger ones stay free.'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
  ],
  inUse:(s,row)=>{const tag=`(${row.code})`;const n=s.bookings.filter(b=>b.lines.some(l=>l.label.endsWith(tag))).length;return {count:n,where:n?[`Folios ${n}`]:[]};},
  validate:(s,row,rows)=>{
    if(row.childAgeFrom!==undefined&&row.childAgeTo!==undefined&&Number(row.childAgeFrom)>Number(row.childAgeTo))return 'Child age from must not be above child age to.';
    const other=rows.find(r=>r.id!==row.id&&r.active&&r.roomType===row.roomType);if(other)return `${other.name} already holds the charges of this room type — edit or retire it first.`;
    const t=mstByCode(s,MST_ROOM_TYPES,String(row.roomType)) as RmsTypeRow|undefined;
    if(t&&!t.extraBedAllowed&&(row.extraBed??[]).length)return `${t.name} does not allow an extra bed (room types master).`;
    return undefined;},
  seed:s=>seedOccupancy(s.rooms),
};
/** Active occupancy-charge row of a room type key. */
export function rmsOccFor(s:State,key:string):RmsOccRow|undefined{
  const code=rmsTypeRow(s,key)?.code??key;
  return (mstRows(s,MST_OCC_CHARGES,{active:true}) as RmsOccRow[]).find(r=>r.roomType===code);
}
/** The CHARGE command an extra posts (label, amount, GST class 'other' = services), or the reason it cannot. Pure. */
export function rmsExtraQuote(s:State,bookingId:string,kind:RmsExtraKind,qty:number,nightsN:number):{label:string;amount:number;gst:'other';perNight:number}|{error:string}{
  const b=s.bookings.find(x=>x.id===bookingId);if(!b)return {error:'Booking not found.'};
  const room=s.rooms.find(r=>r.id===b.room);if(!room)return {error:'This stay has no room.'};
  const t=rmsTypeRow(s,room.type);
  if(kind==='bed'&&t&&!t.extraBedAllowed)return {error:`${t.name} does not allow an extra bed.`};
  const occ=rmsOccFor(s,room.type);if(!occ)return {error:`No occupancy charges for ${rmsTypeName(s,room.type)} — the Owner adds them in Master configuration.`};
  const per=mstMoneyOn(occ[OCC_FIELD[kind]],s.date);if(!per)return {error:`No ${RMS_EXTRA_LABEL[kind].toLowerCase()} amount in force for ${rmsTypeName(s,room.type)}.`};
  if(!Number.isInteger(qty)||qty<1||qty>4)return {error:'Quantity: 1 to 4.'};
  if(!Number.isInteger(nightsN)||nightsN<1||nightsN>30)return {error:'Nights: 1 to 30.'};
  return {label:`${RMS_EXTRA_LABEL[kind]} × ${qty} · ${nightsN} night${nightsN===1?'':'s'} · ${rmsTypeName(s,room.type)} (${occ.code})`,amount:per*qty*nightsN,gst:'other',perNight:per};
}
/** Nights left in a stay from the business date (at least 1). */
export const rmsNightsLeft=(s:State,b:Booking)=>{let n=0;for(let d=b.arrival>s.date?b.arrival:s.date;d<b.departure;d=addDays(d,1))n++;return Math.max(1,n);};

registerMaster(AMENITY_SPEC);
registerMaster(ROOM_TYPE_SPEC);
registerMaster(ROOM_SPEC);
registerMaster(OCC_CHARGE_SPEC);
