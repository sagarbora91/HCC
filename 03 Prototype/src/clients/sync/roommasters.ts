// Rooms masters area (MA-1): room types, amenities and occupancy charges in mastersX (routes in RMS_SERVER, spread into the masters
// area's MST_SERVER), the rooms master's generic commands on the server's rooms, and the rooms tools (range add, renumber, out of order).
//
//   server   GET /masters/room-types, /masters/amenities, /masters/occupancy-charges (every signed-in user; amounts empty without money
//            visibility), their /history (PERM-MST-001; occupancy charges PERM-MST-002), GET /masters/rooms (every signed-in user: floor,
//            master fields — read by sync/rooms.ts). Commands SaveRoomType … DeleteOccupancyCharge (module RoomMasters, PERM-MST-001 /
//            PERM-MST-002), SaveRoom / RetireRoom / ReactivateRoom / DeleteRoom / AddRooms / RenumberRoom (PERM-MST-001); out of order
//            = the Inventory BlockRoom / UnblockRoom commands (a room block on the server, a room hold in the prototype).
//   ids      room types: row id = String(roomTypeId), key = code (Room.type in production = the type code); rooms: room number
//            (ids.server('room', no)); floors: masters rows (String(floorId)).
import type {MasterRow} from '../../domain/streams/masters';
import type {MstDatedAmount} from '../../domain/streams/masters';
import {HccClientError} from '../errors';
import type {Command,State} from '../../services/mock';
import type {SyncArea,CommandCtx} from './types';
import type {MstServerRoute} from './masters';
import {protoId} from './map';
import {rmsParseRooms} from '../../domain/streams/roommasters';

type SrvRate={amountPaise:number;effectiveFrom:string};
type SrvRoomType={roomTypeId:number;code:string;name:string;maxAdults:number;maxChildren:number;extraBedAllowed:boolean;bedType:string|null;sizeSqft:number|null;
  amenities:string[];description:string|null;sortOrder:number;active:boolean;retiredOn:string|null;roomCount:number;rackRates:SrvRate[];updatedUtc:string;version:string};
type SrvAmenity={amenityId:number;code:string;name:string;icon:string|null;showOnWebsite:boolean;sortOrder:number;active:boolean;retiredOn:string|null;version:string};
type SrvOcc={chargeId:number;code:string;name:string;roomTypeId:number;roomTypeCode:string;childAgeFrom:number|null;childAgeTo:number|null;
  extraAdult:SrvRate[];extraChild:SrvRate[];extraBed:SrvRate[];sortOrder:number;active:boolean;retiredOn:string|null;version:string};
/** GET /masters/rooms row (no money). */
export type SrvRoomMaster={roomId:number;roomNo:string;floorId:number|null;floorCode:string;roomTypeId:number;roomTypeCode:string;features:string[];smoking:boolean;
  connectingRoomNo:string|null;extension:string|null;notes:string|null;active:boolean;retiredOn:string|null;version:string};

const rates=(l:SrvRate[]|null|undefined):MstDatedAmount[]=>(l??[]).map(x=>({amount:x.amountPaise,effectiveFrom:x.effectiveFrom}));
const toSrv=(v:unknown)=>Array.isArray(v)?(v as MstDatedAmount[]).map(x=>({amountPaise:Number(x.amount),effectiveFrom:String(x.effectiveFrom)})):[];
const base=(x:{code:string;name:string;sortOrder:number;active:boolean;retiredOn:string|null},id:number):MasterRow=>{
  const r:MasterRow={id:protoId(id),code:x.code,name:x.name,sortOrder:x.sortOrder,active:x.active};if(x.retiredOn)r.retiredOn=x.retiredOn;return r;};
const opt=(o:Record<string,unknown>,k:string,v:unknown)=>{if(v!==null&&v!==undefined&&v!=='')o[k]=v;};

export function roomTypeFromServer(x:SrvRoomType):MasterRow{
  const r=base(x,x.roomTypeId);Object.assign(r,{key:x.code,maxAdults:x.maxAdults,maxChildren:x.maxChildren,extraBedAllowed:x.extraBedAllowed,amenities:[...(x.amenities??[])],rackRate:rates(x.rackRates)});
  opt(r,'bedType',x.bedType);opt(r,'sizeSqft',x.sizeSqft);opt(r,'description',x.description);return r;
}
export function amenityFromServer(x:SrvAmenity):MasterRow{const r=base(x,x.amenityId);r.showOnWebsite=x.showOnWebsite;opt(r,'icon',x.icon);return r;}
export function occFromServer(x:SrvOcc):MasterRow{
  const r=base(x,x.chargeId);r.roomType=x.roomTypeCode;opt(r,'childAgeFrom',x.childAgeFrom);opt(r,'childAgeTo',x.childAgeTo);
  for(const k of ['extraAdult','extraChild','extraBed'] as const){const l=rates(x[k]);if(l.length)r[k]=l;}return r;
}
/** Server id of a master row by code (floors, room types) from the loaded mastersX rows. */
function rowIdByCode(s:State,master:string,code:unknown,what:string):number{
  const r=(s.mastersX?.rows?.[master]??[]).find(x=>x.code===String(code??''));
  const n=Number(r?.id);if(!r||!Number.isSafeInteger(n)||n<=0)throw new HccClientError('VALIDATION',`${what} ${String(code??'')} is not on the server. Refresh and try again.`);
  return n;
}
const roomIdOf=(c:{ids:CommandCtx['ids']},no:unknown)=>{const id=c.ids.server('room',String(no??''));
  if(id===undefined)throw new HccClientError('VALIDATION',`Room ${String(no??'')} is not on the server board. Refresh and try again.`);return id;};
const text=(v:unknown)=>{const t=typeof v==='string'?v.trim():'';return t||null;};

/** Routes of the MA-1 masters (spread into MST_SERVER of sync/masters.ts). */
export const RMS_SERVER:Record<string,MstServerRoute>={
  roomTypes:{list:'GET /masters/room-types',history:'GET /masters/room-types/history',listPath:'/masters/room-types',historyPath:'/masters/room-types/history',
    kind:'roomTypeRow',idField:'roomTypeId',idsField:'roomTypeIds',save:'SaveRoomType',retire:'RetireRoomType',reactivate:'ReactivateRoomType',reorder:'ReorderRoomTypes',remove:'DeleteRoomType',
    fromServer:x=>roomTypeFromServer(x as SrvRoomType),reloadAlso:['rooms'],
    body:row=>{const b:Record<string,unknown>={maxAdults:Number(row.maxAdults??1),maxChildren:Number(row.maxChildren??0),extraBedAllowed:!!row.extraBedAllowed,
      bedType:text(row.bedType),sizeSqft:row.sizeSqft===undefined||row.sizeSqft===null||row.sizeSqft===''?null:Number(row.sizeSqft),
      amenities:Array.isArray(row.amenities)?row.amenities.map(String):[],description:text(row.description)};
      if(Array.isArray(row.rackRate))b.rackRates=toSrv(row.rackRate);return b;}},
  amenities:{list:'GET /masters/amenities',history:'GET /masters/amenities/history',listPath:'/masters/amenities',historyPath:'/masters/amenities/history',
    kind:'amenity',idField:'amenityId',idsField:'amenityIds',save:'SaveAmenity',retire:'RetireAmenity',reactivate:'ReactivateAmenity',reorder:'ReorderAmenities',remove:'DeleteAmenity',
    fromServer:x=>amenityFromServer(x as SrvAmenity),body:row=>({icon:text(row.icon),showOnWebsite:!!row.showOnWebsite})},
  occupancyCharges:{list:'GET /masters/occupancy-charges',history:'GET /masters/occupancy-charges/history',listPath:'/masters/occupancy-charges',historyPath:'/masters/occupancy-charges/history',
    kind:'occupancyCharge',idField:'chargeId',idsField:'chargeIds',save:'SaveOccupancyCharge',retire:'RetireOccupancyCharge',reactivate:'ReactivateOccupancyCharge',reorder:'ReorderOccupancyCharges',remove:'DeleteOccupancyCharge',
    fromServer:x=>occFromServer(x as SrvOcc),
    body:(row,c)=>({roomTypeId:rowIdByCode(c.state,'roomTypes',row.roomType,'Room type'),childAgeFrom:row.childAgeFrom??null,childAgeTo:row.childAgeTo??null,
      extraAdult:toSrv(row.extraAdult),extraChild:toSrv(row.extraChild),extraBed:toSrv(row.extraBed)})},
  // Rooms: rows are s.rooms (sync/rooms.ts loads them, with GET /masters/rooms); generic commands act on the server's rooms by number.
  rooms:{list:'GET /masters/rooms',history:'GET /masters/room-types/history',listPath:'/masters/rooms',historyPath:'',
    kind:'room',idField:'roomId',idsField:'roomIds',save:'SaveRoom',retire:'RetireRoom',reactivate:'ReactivateRoom',reorder:'',remove:'DeleteRoom',
    noList:true,reloadAlso:['rooms','tasks'],serverId:(c,id)=>roomIdOf(c,id),
    refuse:{reorder:'Rooms are listed by floor and number — there is nothing to reorder.'},
    precheckSave:row=>row.id?undefined:'Add rooms with “Add rooms” (range add) above the list.',
    body:(row,c)=>{const b:Record<string,unknown>={roomNo:String(row.code??'').trim().toUpperCase(),floorId:rowIdByCode(c.state,'floors',row.floor,'Floor'),
      roomTypeId:rowIdByCode(c.state,'roomTypes',row.type,'Room type'),features:Array.isArray(row.features)?row.features.map(String):[],smoking:!!row.smoking,
      connectingRoomNo:text(row.connecting),extension:text(row.extension),notes:text(row.notes)};delete (b as {code?:unknown}).code;return b;},
    noBase:true},
};

const ISO=/^\d{4}-\d{2}-\d{2}$/;
/** The rooms tools (RMS_*): range add, renumber (AddRooms / RenumberRoom), out of order (Inventory BlockRoom / UnblockRoom). */
export const roommasters:SyncArea={
  id:'roommasters',
  loaders:[],
  commands:[
    {type:'RMS_ROOMS_ADD',reload:['rooms','mastersX'],
      precheck(cmd:Command,s:State){const p=String(cmd.rooms??'').trim();if(!p)return 'Enter room numbers, e.g. 101–110.';
        if(!(s.mastersX?.rows?.floors??[]).some(f=>f.code===cmd.floor))return 'Choose a floor.';
        if(!(s.mastersX?.rows?.roomTypes??[]).some(t=>t.code===cmd.roomType))return 'Choose a room type.';return undefined;},
      async send(c){
        const p=rmsParseRooms(String(c.cmd.rooms));if(p.error)throw new HccClientError('VALIDATION',p.error);
        await c.api.command('AddRooms',{roomNos:p.numbers,floorId:rowIdByCode(c.state,'floors',c.cmd.floor,'Floor'),roomTypeId:rowIdByCode(c.state,'roomTypes',c.cmd.roomType,'Room type')});}},
    {type:'RMS_ROOM_RENUMBER',reload:['rooms','tasks'],
      precheck:cmd=>/^[A-Z0-9-]{1,10}$/.test(String(cmd.to??'').trim().toUpperCase())?undefined:'New number: 1–10 capital letters, digits or “-”.',
      async send(c){const no=String(c.cmd.id);
        await c.api.command('RenumberRoom',{roomId:roomIdOf(c,no),roomNo:String(c.cmd.to).trim().toUpperCase()},{expectedVersion:c.ids.version('room',no)});}},
    {type:'RMS_ROOM_OOO',reload:['rooms','tasks'],
      precheck(cmd,s){const r=String(cmd.reason??'').trim();if(r.length<3)return 'Give a reason (3–120 characters).';
        if(!ISO.test(String(cmd.from??''))||String(cmd.from)<s.date)return 'Choose the first night out of order (today or later).';
        if(cmd.to&&(!ISO.test(String(cmd.to))||String(cmd.to)<=String(cmd.from)))return 'The last date must come after the first night.';return undefined;},
      async send(c){const b:Record<string,unknown>={roomId:roomIdOf(c,c.cmd.id),reason:'Out of order · '+String(c.cmd.reason).trim(),from:c.cmd.from};
        if(c.cmd.to)b.to=c.cmd.to;await c.api.command('BlockRoom',b);}},
    {type:'RMS_ROOM_OOO_CLEAR',reload:['rooms','tasks'],
      precheck:(cmd,s)=>s.rooms.find(r=>r.id===cmd.id)?.blocked||s.rooms.find(r=>r.id===cmd.id)?.ooo?undefined:'This room is not out of order.',
      async send(c){await c.api.command('UnblockRoom',{roomId:roomIdOf(c,c.cmd.id)},{expectedVersion:c.ids.version('roomBlock',String(c.cmd.id))});}},
  ],
};
