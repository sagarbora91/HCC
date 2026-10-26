// MA-5 · Front-desk lists on the server (module FrontDeskMasters, migrations 2850–2851): booking sources, guest ID types, numbering
// series and booking policies. One MST_SERVER entry per master (spread into masters.ts), so the generic MST_MASTER_* commands of the
// masters framework reach the server commands below and the masters loader fills s.mastersX.rows.<master>.
//
//   server   GET /masters/{booking-sources|id-types|number-series|policies} (every signed-in user: booking forms, registration, documents
//            and policies read them; commission is null for roles without money visibility) and …/history (PERM-MST-001 for sources and
//            ID types, PERM-MST-002 for numbering and policies).
//            POST /commands/{Save|Retire|Reactivate|Delete}{BookingSource|IdType|NumberSeries|BookingPolicy}, Reorder{BookingSources|IdTypes|
//            NumberSeries|BookingPolicies} — body id field `rowId` (+ expectedVersion on edits), reorder `rowIds`.
//   ids      Prototype row id = String(rowId). Until a list is first changed the server answers its built-in defaults with rowId −1, −2, …
//            (version ""): a command on such a row stores the defaults first (as the prototype stores its seed on the first change).
//            Booking-source rows that OTA channels / companies register on the client (id "bsrc-auto-…") are not on the server yet: saving
//            one creates it; reorder leaves them out.
import type {MasterRow} from '../../domain/streams/masters';
import type {MstServerRoute} from './masters';
import {protoId} from './map';

const num=(v:unknown,d=0)=>typeof v==='number'&&Number.isFinite(v)?v:d;
const str=(v:unknown)=>v===null||v===undefined?'':String(v);
/** Server row id of a prototype id: real (positive) and default (negative) server rows; anything else is client-only. */
export const fdmRowId=(id:string):number|undefined=>/^-?\d+$/.test(id)&&Number(id)!==0?Number(id):undefined;
function base(x:Record<string,unknown>):MasterRow{
  const r:MasterRow={id:protoId(num(x.rowId)),code:str(x.code),name:str(x.name),sortOrder:num(x.sortOrder),active:x.active!==false};
  if(x.retiredOn)r.retiredOn=str(x.retiredOn);
  return r;
}
const route=(path:string,historyPath:string,list:MstServerRoute['list'],history:MstServerRoute['history'],kind:string,cmd:{save:string;retire:string;reactivate:string;reorder:string;remove:string},
  fromServer:(x:Record<string,unknown>)=>MasterRow,toBody:(row:Record<string,unknown>)=>Record<string,unknown>):MstServerRoute=>
  ({list,history,listPath:path,historyPath,kind,idField:'rowId',idsField:'rowIds',...cmd,fromServer,toBody,rowId:fdmRowId});

/** Server routes of the four MA-5 masters (keys = prototype spec ids). */
export const FDM_SERVER:Record<string,MstServerRoute>={
  bookingSources:route('/masters/booking-sources','/masters/booking-sources/history','GET /masters/booking-sources','GET /masters/booking-sources/history','bookingSource',
    {save:'SaveBookingSource',retire:'RetireBookingSource',reactivate:'ReactivateBookingSource',reorder:'ReorderBookingSources',remove:'DeleteBookingSource'},
    x=>{const r=base(x);r.segment=str(x.segment)||'OTHER';if(x.channel)r.channel=str(x.channel);if(typeof x.commissionBps==='number')r.commissionBps=x.commissionBps;return r;},
    row=>({segment:str(row.segment),channel:str(row.channel).trim()||null,...(row.commissionBps===undefined||row.commissionBps===null?{}:{commissionBps:Number(row.commissionBps)})})),
  idTypes:route('/masters/id-types','/masters/id-types/history','GET /masters/id-types','GET /masters/id-types/history','idType',
    {save:'SaveIdType',retire:'RetireIdType',reactivate:'ReactivateIdType',reorder:'ReorderIdTypes',remove:'DeleteIdType'},
    x=>({...base(x),accepted:x.accepted!==false,needsPhoto:x.needsPhoto===true,foreignerOnly:x.foreignerOnly===true}),
    row=>({accepted:row.accepted!==false,needsPhoto:row.needsPhoto===true,foreignerOnly:row.foreignerOnly===true})),
  numberSeries:route('/masters/number-series','/masters/number-series/history','GET /masters/number-series','GET /masters/number-series/history','numberSeries',
    {save:'SaveNumberSeries',retire:'RetireNumberSeries',reactivate:'ReactivateNumberSeries',reorder:'ReorderNumberSeries',remove:'DeleteNumberSeries'},
    x=>{const r={...base(x),document:str(x.document),prefix:str(x.prefix),start:num(x.startNo,1),padding:num(x.padding,1),reset:str(x.reset)||'FY',activeFrom:str(x.activeFrom),issued:num(x.issued)} as MasterRow;
      if(x.nextNumber)r.nextNumber=str(x.nextNumber);return r;},
    row=>({document:str(row.document),prefix:str(row.prefix).trim().toUpperCase(),startNo:Number(row.start),padding:Number(row.padding),reset:str(row.reset),activeFrom:str(row.activeFrom)})),
  policies:route('/masters/policies','/masters/policies/history','GET /masters/policies','GET /masters/policies/history','bookingPolicy',
    {save:'SaveBookingPolicy',retire:'RetireBookingPolicy',reactivate:'ReactivateBookingPolicy',reorder:'ReorderBookingPolicies',remove:'DeleteBookingPolicy'},
    x=>({...base(x),freeCancelHours:num(x.freeCancelHours),cancelChargeBps:num(x.cancelChargeBps),noShowChargeBps:num(x.noShowChargeBps),depositBps:num(x.depositBps),
      sources:Array.isArray(x.sources)?(x.sources as unknown[]).map(str):[]}),
    row=>({freeCancelHours:Number(row.freeCancelHours??0),cancelChargeBps:Number(row.cancelChargeBps??0),noShowChargeBps:Number(row.noShowChargeBps??0),depositBps:Number(row.depositBps??0),
      sources:Array.isArray(row.sources)?(row.sources as unknown[]).map(String):[]})),
};
