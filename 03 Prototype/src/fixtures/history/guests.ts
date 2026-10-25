// History guest profiles: one fictional profile per distinct history customer (same mobile = same profile), linked through
// Booking.guestId so Customers 360 shows a Profile tab for everyone. Uses its own PRNG stream, so adding it never shifts
// the booking/folio data. All values are fictional (example.test emails, "(fictional)" addresses, fictional full ID numbers, DEC-068).
import type {State,Booking} from '../../domain/types';
import {createRng} from './prng';
import {guestFictionalId,guestIdLast4} from '../../domain/streams/guests';

type G=NonNullable<State['guests']>[number];
const CITIES=['Latur, Maharashtra','Pune, Maharashtra','Mumbai, Maharashtra','Nanded, Maharashtra','Solapur, Maharashtra','Hyderabad, Telangana','Bengaluru, Karnataka','Nagpur, Maharashtra','Aurangabad, Maharashtra','Kolhapur, Maharashtra','Ahmedabad, Gujarat','New Delhi'];
const ID_TYPES=['Aadhaar (demo)','Aadhaar (demo)','Aadhaar (demo)','Driving licence (demo)','Voter ID (demo)','Passport (demo)'];
const PREFS=['','','','Quiet room','High floor','Early breakfast','Extra pillows','Vegetarian meals','Late checkout requests','Ground floor preferred','Twin beds','Arriving late evening'];
const phoneKey=(p?:string)=>{const d=String(p||'').replace(/\D/g,'');return d.length>=10?d.slice(-10):'';};

/** Adds `guests` to the state and sets `guestId` on every history booking. Deterministic for a given seed. */
export function addHistoryGuests(s:State,history:Booking[],seed:number):void{
  const rng=createRng((seed^0x9e3779b1)>>>0);
  const byKey=new Map<string,G>();
  const out:G[]=[];
  for(const b of history){
    const key=phoneKey(b.phone)||'n:'+b.guest.toLowerCase();
    let g=byKey.get(key);
    if(!g){
      const n=out.length+1,departed=b.status==='Departed';
      const idType=rng.pick(ID_TYPES),idNumber=guestFictionalId(idType,1000+n*37+rng.int(0,9999)),idLast4=guestIdLast4(idNumber); // FICTIONAL full ID (DEC-068), obviously fake
      const city=rng.pick(CITIES),pref=rng.pick(PREFS);
      g={id:'G-H'+String(n).padStart(4,'0'),name:b.guest,phone:b.phone??'',email:b.email??'',address:`${rng.int(1,120)} Demo Street, ${city} (fictional)`,nationality:'Indian',idType,idLast4,idNumber,
        ...(b.company?{company:b.company.name,gstin:b.company.gstin}:{}),preferences:pref,notes:'Fictional guest · FY 2026-27 history profile',
        party:b.people>1&&rng.chance(0.25)?[{name:'Companion (fictional)',ageBand:'Adult',relation:rng.pick(['Spouse','Friend','Colleague'])}]:[],verified:departed,origin:'seed',version:1};
      byKey.set(key,g);out.push(g);
    }
    b.guestId=g.id;
  }
  s.guests=[...(s.guests||[]),...out];
}
