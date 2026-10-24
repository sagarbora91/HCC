// Conformance: every (status × command) cell of the HCC transition map is exercised against the live prototype mock.
// An allowed cell must succeed when its preconditions are met and land on the mapped status; a forbidden cell must throw
// and leave the input state untouched.
import {describe,it,expect} from 'vitest';
import {seed,execute,totals} from '../../../03 Prototype/src/services/mock/index';
import type {State,BookingStatus} from '../../../03 Prototype/src/domain/types';
import {HCC_RES_TRANSITIONS,allowed,type ResCommand} from './hcc-transition-map';

const DEP='2026-10-26';
function booked(hold=false):[State,string]{const s0=seed();const s=execute(s0,'owner',{type:'BOOK',guest:'Lab Guest',room:'105',arrival:s0.date,departure:DEP,people:2,hold});return [s,s.bookings.at(-1)!.id];}
function confirmed():[State,string]{let [s,id]=booked();s=execute(s,'owner',{type:'REGISTER',id});return [s,id];}
function inHouse():[State,string]{let [s,id]=confirmed();s=execute(s,'owner',{type:'CHECKIN',id});return [s,id];}
function settled():[State,string]{let [s,id]=inHouse();
  // Post both agreed nights (POST_NIGHTS posts the full stay in the prototype), then settle exactly.
  s=execute(s,'owner',{type:'POST_NIGHTS',id});
  const bal=totals(s.bookings.find(b=>b.id===id)!).balance;
  s=execute(s,'cashier',{type:'PAYMENT',id,amount:bal,method:'UPI',kind:'payment',reference:'LAB-UPI'});return [s,id];}
const STATES:Record<BookingStatus,()=>[State,string]>={
  'Held':()=>booked(true),
  'Expired':()=>{const [s,id]=booked(true);const x=structuredClone(s);x.bookings.find(b=>b.id===id)!.holdUntil='2000-01-01T00:00';return [x,id];},
  'Confirmed':confirmed,
  'In house':settled,
  'Departed':()=>{const [s,id]=settled();return [execute(s,'owner',{type:'CHECKOUT',id}),id];},
  'Cancelled':()=>{const [s,id]=booked();return [execute(s,'owner',{type:'CANCEL',id,reason:'lab'}),id];},
  'No show':()=>{const [s,id]=booked();return [execute(s,'owner',{type:'CANCEL',id,noShow:true}),id];},
};
const run=(s:State,id:string,c:ResCommand)=>{switch(c){
  case 'CONFIRM':return execute(s,'owner',{type:'CONFIRM',id});
  case 'CANCEL':return execute(s,'owner',{type:'CANCEL',id,reason:'lab'});
  case 'NO_SHOW':return execute(s,'owner',{type:'CANCEL',id,noShow:true});
  case 'CHECKIN':return execute(s,'owner',{type:'CHECKIN',id});
  case 'CHECKOUT':return execute(s,'owner',{type:'CHECKOUT',id});
  case 'CHANGE_DATES':return execute(s,'owner',{type:'CHANGE_DATES',id,room:'105',arrival:s.date,departure:'2026-10-27'});}};
const COMMANDS:ResCommand[]=['CONFIRM','CANCEL','NO_SHOW','CHECKIN','CHECKOUT','CHANGE_DATES'];

describe('H01 HCC transition map conformance',()=>{
  for(const st of Object.keys(HCC_RES_TRANSITIONS) as BookingStatus[])for(const c of COMMANDS){
    const to=allowed(st,c);
    it(`${st} + ${c} → ${to??'rejected'}`,()=>{
      const [s,id]=STATES[st]();
      expect(s.bookings.find(b=>b.id===id)!.status).toBe(st==='Expired'?'Held':st); // Expired is a lapsed Held until swept
      const before=JSON.stringify(s);
      if(to){const n=run(s,id,c);expect(n.bookings.find(b=>b.id===id)!.status).toBe(to);}
      else{expect(()=>run(s,id,c)).toThrow();expect(JSON.stringify(s)).toBe(before);}
    });
  }
});

describe('H01 timing and repeats',()=>{
  it('no-show before the arrival date is rejected',()=>{
    const s0=seed();const s=execute(s0,'owner',{type:'BOOK',guest:'Future Guest',room:'105',arrival:'2026-10-25',departure:DEP,people:1});
    expect(()=>execute(s,'owner',{type:'CANCEL',id:s.bookings.at(-1)!.id,noShow:true})).toThrow('on or after the arrival date');
  });
  it('confirming twice: the second CONFIRM is rejected',()=>{
    let [s,id]=booked(true);s=execute(s,'owner',{type:'CONFIRM',id});
    expect(()=>execute(s,'owner',{type:'CONFIRM',id})).toThrow('Only a held reservation');
  });
  it('checkout replay with the same idempotency key returns the same state',()=>{
    const [s,id]=settled();const a=execute(s,'owner',{type:'CHECKOUT',id,key:'lab-co-1'});
    expect(execute(a,'owner',{type:'CHECKOUT',id,key:'lab-co-1'})).toBe(a);
  });
  it('a cancelled booking with money on it cannot be cancelled (refund first)',()=>{
    let [s,id]=booked();s=execute(s,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'payment'});
    expect(()=>execute(s,'owner',{type:'CANCEL',id})).toThrow('Refund the advance');
  });
});
