// O01/O02 comparison: does HCC have the gap these Open Hotel PMS helpers fill? (No donor code imported.)
import {describe,it,expect} from 'vitest';
import {seed,execute,available} from '../../../03 Prototype/src/services/mock/index';
import type {State} from '../../../03 Prototype/src/domain/types';

function inHouse(dep='2026-10-27'):[State,string]{const s0=seed();let s=execute(s0,'owner',{type:'BOOK',guest:'Segment Lab',room:'105',arrival:s0.date,departure:dep,people:2});
  const id=s.bookings.at(-1)!.id;s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});return [s,id];}

describe('O01 check-in room-night selection',()=>{
  it('HCC books one assigned room per reservation; check-in opens exactly one segment for that room',()=>{
    const [s,id]=inHouse();const x=s.bookings.find(b=>b.id===id)!;
    expect(x.segments).toEqual([{room:'105',from:'2026-10-24'}]);
  });
  it('there is no pre-planned per-night room list to choose from (no gap for O01)',()=>{
    const x=seed().bookings[0] as unknown as Record<string,unknown>;
    expect(x.nights===undefined&&x.roomNights===undefined).toBe(true);
  });
});

describe('O02 checkout release date',()=>{
  it('early departure is an explicit MOVE that shortens the stay; the released nights become bookable',()=>{
    let [s,id]=inHouse('2026-10-27');
    expect(available(s,'105','2026-10-25','2026-10-27')).toBe(false);
    s=execute(s,'owner',{type:'MOVE',id,room:'105',departure:'2026-10-25'});
    expect(available(s,'105','2026-10-25','2026-10-27')).toBe(true);
  });
  it('a stay cannot be shortened below a night that is already posted',()=>{
    let [s,id]=inHouse('2026-10-27');s=execute(s,'owner',{type:'POST_NIGHTS',id});
    expect(()=>execute(s,'owner',{type:'MOVE',id,room:'105',departure:'2026-10-25'})).toThrow('already posted');
  });
  it('checkout requires all agreed nights posted, so release always starts at the agreed departure',()=>{
    const [s,id]=inHouse('2026-10-27');
    expect(()=>execute(s,'owner',{type:'CHECKOUT',id})).toThrow('Post all agreed stay charges');
  });
});
