// F12-RPT-REVENUE · State adapters shared by the revenue reports (read-only over the forecast / yield / capture data; nothing here writes).
import type {State,Booking,Room} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {addDays} from '../../money';
import {blockedDuring} from '../../selectors';
import {rmsOffline} from '../../streams/roomoffline';
import {yldRoomTypes} from '../../streams/yield';
import {fcBookedOn} from '../../forecast';
import {HccError} from '../../errors';

export const T=(key:string,label:string):RptColumn=>({key,label,type:'text',money:false});
export const D=(key:string,label:string):RptColumn=>({key,label,type:'date',money:false});
export const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const P=(key:string,label:string):RptColumn=>({key,label,type:'pct',money:false});
export const B=(key:string,label:string):RptColumn=>({key,label,type:'bool',money:false});
export const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});

/** A computed row with its red-row flag (RptRow has no flag; the digest lane and the tests read `red`). */
export type MrRow={cells:RptRow['cells'];red:boolean};
export const mrRows=(rows:MrRow[]):RptRow[]=>rows.map(r=>({kind:'row' as const,cells:r.cells}));
export const CAPTURE_NOTE=(what:string)=>` Capture needed: ${what} (nothing captured yet, so no rows).`;

/** A booking that holds a room on the books or slept: not cancelled, expired, no-show or a lapsed hold. */
export const mrLive=(s:State,b:Booking)=>b.arrival<b.departure&&(b.status==='Confirmed'||b.status==='In house'||b.status==='Departed'||(b.status==='Held'&&(!b.holdUntil||s.clock<b.holdUntil)));
export const mrTypeOf=(s:State,b:Booking)=>s.rooms.find(r=>r.id===b.room)?.type??'';
export const mrChannelOf=(b:Booking)=>/^ota/i.test(b.source)&&b.otaChannel?b.otaChannel:b.source;
/** Rooms in service for a night (optionally one type): the same rule as the yield module's sellable count. */
export function mrSellable(s:State,date:string,type?:string):number{
  const next=addDays(date,1);
  return s.rooms.filter((r:Room)=>(!type||r.type===type)&&!blockedDuring(r,date,next)&&!rmsOffline(r,date,next)).length;
}
/** Rooms with a live booking on the night (optionally one type). */
export function mrRoomsOn(s:State,date:string,type?:string):number{
  const set=new Set<string>();
  for(const b of s.bookings)if(b.arrival<=date&&b.departure>date&&mrLive(s,b)&&(!type||mrTypeOf(s,b)===type))set.add(b.room);
  return set.size;
}
/** Reference room type for single-rate columns: the type with the most rooms (ties: first in the master). */
export function mrRefType(s:State):string{
  const types=yldRoomTypes(s);let best='',n=-1;
  for(const t of types){const c=s.rooms.filter(r=>r.type===t).length;if(c>n){best=t;n=c;}}
  return best;
}
export const mrBookedOn=(s:State,b:Booking)=>fcBookedOn(s,b);
export const mrCancelledOn=(b:Booking)=>b.status==='Cancelled'?addDays(b.arrival,-1):undefined;
/** Whole-number parameter (text param) with limits; empty = default. */
export function mrIntParam(label:string,v:string|boolean|undefined,def:number,min:number,max:number):number{
  if(v===undefined||v===''||v===true||v===false)return def;
  const n=Number(v);if(!Number.isInteger(n)||n<min||n>max)throw new HccError('VALIDATION',`${label}: a whole number from ${min} to ${max}.`);
  return n;
}
/** Nights guard: from + nights - 1 must stay inside the report's window. */
export function mrNightsGuard(nights:number,max:number){if(nights>max)throw new HccError('VALIDATION',`The range is longer than ${max} days. Narrow the range.`);}
