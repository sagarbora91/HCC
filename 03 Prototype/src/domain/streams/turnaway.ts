// F12 MEAS · cap-desk rules (MEAS-CONTRACT §2): turn-away log, walked guests, allowance reasons, booking extras (upgrade, late check-out),
// customer preferences + blacklist. Pure helpers; the commands are in services/mock/handlers/meas-desk.ts. Money = integer paise.
// Every command first checks the switch (measOn, plus a direct read of the stored switch value so the rule holds before meas-settings lands).
import type {State,Booking} from '../types';
import type {MeasState,MeasTurnaway,MeasWalked,MeasAllowanceReason,MeasBookingExtra,MeasPreference,TurnawayReason} from './measure-types';
import {measOn} from './measure';
import {customerIndex} from './customers';
import {MEAS_ALLOWANCE_DEFAULTS} from '../../fixtures/ext/meas-desk';
import {nights} from '../money';

/** Switch keys of the desk captures (contract §1.2). */
export const MD_KEY={turnaway:'meas.daily.regrets',walked:'meas.rev.overbooking',allowance:'meas.daily.adjustments',extra:'meas.guest.upsell',preference:'meas.guest.preferences'} as const;
export const MD_ERR_OFF='MEAS_OFF';

export const TURNAWAY_REASONS:{id:TurnawayReason;label:string}[]=[
  {id:'full',label:'No room free'},{id:'price',label:'Price too high'},{id:'dates',label:'Dates did not suit'},
  {id:'room-type',label:'Room type not available'},{id:'policy',label:'Policy (ID, payment, group)'},{id:'other',label:'Other'}];
export const TURNAWAY_CHANNELS=['Walk-in','Phone','WhatsApp','Email','OTA','Corporate'] as const;

/** Folio items for the upsell captures (M-1). Amounts are pre-tax paise; GST comes from the dated master (category "other" 18 %). */
export const MD_ITEMS={
  UPG:{code:'UPG',label:'Room upgrade'},
  LCO1:{code:'LCO1',label:'Late check-out (after 14:00)',amount:30000,afterHhmm:'14:00'},
  LCO2:{code:'LCO2',label:'Late check-out (after 18:00)',amount:60000,afterHhmm:'18:00'},
} as const;
export type MdItemCode=keyof typeof MD_ITEMS;

export const mdSlice=(s:State):MeasState=>s.measX??{};
/** True when the capture may run: measOn AND the stored switch value (if any) is not false. */
export function mdOn(s:State,key:string):boolean{
  if(!measOn(s,key))return false;
  const v=s.configX?.values?.[key];
  return v!==false;
}
export const mdAllowanceReasons=(s:State):MeasAllowanceReason[]=>mdSlice(s).allowanceReasons??MEAS_ALLOWANCE_DEFAULTS;
export const mdTurnaways=(s:State):MeasTurnaway[]=>mdSlice(s).turnaway??[];
export const mdWalked=(s:State):MeasWalked[]=>mdSlice(s).walked??[];
export const mdExtra=(s:State,bookingId:string):MeasBookingExtra|undefined=>mdSlice(s).bookingExtra?.[bookingId];
export const mdPreference=(s:State,customerKey:string):MeasPreference|undefined=>mdSlice(s).preference?.[customerKey];
/** Lead time of a turn-away in days (arrival minus the business date, never negative). */
export const mdLeadDays=(date:string,arrival:string)=>Math.max(0,nights(date,arrival));
/** The booked room type of a booking (kept when the assigned type differs), else the assigned room's current type. */
export function mdBookedType(s:State,b:Booking):string|undefined{return mdExtra(s,b.id)?.bookedType??s.rooms.find(r=>r.id===b.room)?.type;}
/** Assigned type differs from the booked one: an upgrade (paid or free) or a downgrade the report must see. */
export function mdTypeDiffers(s:State,b:Booking):boolean{
  const booked=mdExtra(s,b.id)?.bookedType,now=s.rooms.find(r=>r.id===b.room)?.type;return !!booked&&!!now&&booked!==now;
}
/** Late check-out item for a clock time ("HH:mm"): LCO2 at or after 18:00, LCO1 at or after 14:00, otherwise none. */
export function mdLateCheckoutItem(hhmm:string):'LCO1'|'LCO2'|undefined{
  if(!/^\d{2}:\d{2}$/.test(hhmm))return undefined;
  return hhmm>=MD_ITEMS.LCO2.afterHhmm?'LCO2':hhmm>=MD_ITEMS.LCO1.afterHhmm?'LCO1':undefined;
}
/** Known-guest badge for the arrivals list: completed stays and the last visit (before today), from Customer 360; blacklist flag. */
export function mdKnownGuest(s:State,b:Booking):{stays:number;lastVisit?:string;blacklisted:boolean;key:string}|undefined{
  const c=customerIndex(s).find(c=>c.stays.some(x=>x.id===b.id));if(!c)return undefined;
  const past=c.completed.filter(x=>x.id!==b.id);
  const last=[...past.map(x=>x.arrival),...c.legacyStays.map(l=>l.arrival)].sort().pop();
  const stays=past.length+c.legacyCount;
  const bl=!!mdPreference(s,c.key)?.blacklist;
  return stays>0||bl?{stays,lastVisit:last,blacklisted:bl,key:c.key}:undefined;
}
