// Customers (Customer 360): every person who ever booked, built from bookings + saved guest profiles. Pure selectors, no state of its own.
// A customer is identified by mobile number (last 10 digits); bookings without a number fall back to the normalised name.
// Booking IDs are NOT searchable here (owner decision) — that lookup lives on Arrivals & check-in (`bookingIdLookup`).
import type {State,Booking} from '../types';
import {totals} from '../selectors';
import {nights} from '../money';

export type CustomerGuest=NonNullable<State['guests']>[number];
export type Customer={
  key:string;name:string;phone:string;email:string;
  /** Saved guest profile (preferences, masked ID details), when one exists. */
  guest?:CustomerGuest;
  /** Every booking of this customer, newest arrival first (all statuses). */
  stays:Booking[];
  /** Stays that happened or are happening (In house + Departed). */
  completed:Booking[];
  nights:number;
  /** Folio totals of completed stays (paise). Show only to roles that may see money. */
  lifetime:number;avgRate:number;
  firstStay?:string;lastStay?:string;
  current?:Booking;upcoming:Booking[];
  cancelled:number;
  topRooms:string[];topSources:string[];
};

const clean=(n:string)=>n.replace(/\s*\(fictional\)\s*/i,'').trim();
const normName=(n:string)=>clean(n).toLowerCase().replace(/[^a-z0-9 ]+/g,' ').replace(/\s+/g,' ').trim();
export const customerPhoneKey=(p?:string)=>{const d=String(p||'').replace(/\D/g,'');return d.length>=10?d.slice(-10):'';};
const top=(xs:string[],n:number)=>{const m=new Map<string,number>();for(const x of xs)if(x)m.set(x,(m.get(x)||0)+1);return [...m].sort((a,b)=>b[1]-a[1]||a[0].localeCompare(b[0])).slice(0,n).map(x=>x[0]);};

let cache:{b:State['bookings'];g:State['guests'];date:string;list:Customer[]}|undefined;
/** All customers, most recent stay first. Cached until the bookings or profiles change. */
export function customerIndex(s:State):Customer[]{
  if(cache&&cache.b===s.bookings&&cache.g===s.guests&&cache.date===s.date)return cache.list;
  const guests=s.guests||[],byGuest=new Map(guests.map(g=>[g.id,g])),byPhone=new Map<string,CustomerGuest>();
  for(const g of guests){const k=customerPhoneKey(g.phone);if(k&&!byPhone.has(k))byPhone.set(k,g);}
  const groups=new Map<string,Booking[]>();
  for(const b of s.bookings){
    const g=b.guestId?byGuest.get(b.guestId):undefined;
    const key=customerPhoneKey(b.phone)||customerPhoneKey(g?.phone)||'n:'+normName(g?.name||b.guest);
    (groups.get(key)||groups.set(key,[]).get(key)!).push(b);
  }
  const out:Customer[]=[];
  for(const [key,bs] of groups){
    const stays=[...bs].sort((a,b)=>b.arrival.localeCompare(a.arrival)||b.id.localeCompare(a.id));
    const guest=stays.map(b=>b.guestId?byGuest.get(b.guestId):undefined).find(Boolean)||(key.startsWith('n:')?undefined:byPhone.get(key));
    const completed=stays.filter(b=>b.status==='In house'||b.status==='Departed');
    const latest=stays[0];
    const spent=completed.reduce((t,b)=>t+totals(b).total,0);
    out.push({key,name:clean(guest?.name||latest.guest),phone:guest?.phone||stays.find(b=>b.phone)?.phone||'',email:guest?.email||stays.find(b=>b.email)?.email||'',guest,
      stays,completed,nights:completed.reduce((t,b)=>t+nights(b.arrival,b.departure),0),lifetime:spent,avgRate:completed.length?Math.round(completed.reduce((t,b)=>t+b.rate,0)/completed.length):0,
      firstStay:completed.length?completed[completed.length-1].arrival:undefined,lastStay:completed[0]?.arrival,
      current:stays.find(b=>b.status==='In house'),upcoming:stays.filter(b=>(b.status==='Confirmed'||b.status==='Held')&&b.departure>=s.date).reverse(),
      cancelled:stays.filter(b=>b.status==='Cancelled'||b.status==='No show').length,
      topRooms:top(completed.map(b=>b.room),3),topSources:top(completed.map(b=>b.source),3)});
  }
  out.sort((a,b)=>(b.stays[0].arrival.localeCompare(a.stays[0].arrival))||a.name.localeCompare(b.name));
  cache={b:s.bookings,g:s.guests,date:s.date,list:out};
  return out;
}

/** Search by name (every word) or mobile digits (3+). `phoneOk` is false for roles that only see masked numbers. No booking-ID matching. */
export function customerSearch(list:Customer[],q:string,phoneOk=true):Customer[]{
  const text=q.trim();
  if(!text)return list;
  if(/[a-z]/i.test(text)){const words=normName(text).split(' ').filter(Boolean);return list.filter(c=>words.every(w=>normName(c.name).includes(w)));}
  const digits=text.replace(/\D/g,'');
  if(!phoneOk||digits.length<3)return [];
  return list.filter(c=>c.phone.replace(/\D/g,'').includes(digits));
}

/** Guest requests and complaints raised on any of the customer's stays (module 18). */
export function customerService(s:State,c:Customer){
  const ids=new Set(c.stays.map(b=>b.id)),x=s.servicesX;
  return {requests:(x?.requests||[]).filter(r=>ids.has(r.booking)),complaints:(x?.complaints||[]).filter(k=>k.booking&&ids.has(k.booking))};
}

/** Booking-ID lookup for Arrivals & check-in: partial ID, any status, newest first (max 8). Needs 3+ characters. */
export function bookingIdLookup(s:State,q:string):Booking[]{
  const t=q.trim().toLowerCase();
  if(t.length<3)return [];
  const ref=(b:Booking)=>(b.publicNo||b.id).toLowerCase();
  return s.bookings.filter(b=>ref(b).includes(t)||b.id.toLowerCase()===t).sort((a,b)=>(ref(a)===t?-1:0)-(ref(b)===t?-1:0)||b.arrival.localeCompare(a.arrival)).slice(0,8);
}
