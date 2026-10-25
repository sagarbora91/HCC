// Phase A tests: FY26 history generator (bookings, folios, receipts, allocations) and IndexedDB storage adapter.
import {build} from 'esbuild';
import {createHash} from 'node:crypto';
await build({entryPoints:['src/app/storage.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/storage.mjs',logLevel:'error'});
const ST=await import('./.generated/storage.mjs');

const FROM='2026-04-01',TO='2026-10-23';
const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');

// ── fake IndexedDB (enough of the API for the adapter) ──
function fakeIdb({failOpen=false}={}){
  const data=new Map();let puts=0;
  return {data,get puts(){return puts;},open(){
    const req={};
    queueMicrotask(()=>{
      if(failOpen){req.error=new Error('blocked');req.onerror?.();return;}
      const db={createObjectStore(){},transaction(){
        const tx={objectStore:()=>({
          get:k=>{const r={};queueMicrotask(()=>{r.result=data.get(k);r.onsuccess?.();});return r;},
          put:(v,k)=>{puts++;data.set(k,v);queueMicrotask(()=>tx.oncomplete?.());},
        })};return tx;}};
      req.result=db;req.onupgradeneeded?.();req.onsuccess?.();
    });
    return req;}};
}
function fakeLs(){const m=new Map();return {m,getItem:k=>m.has(k)?m.get(k):null,setItem:(k,v)=>{m.set(k,String(v));},removeItem:k=>{m.delete(k);}};}
function timers(){const q=[];return {q,setTimer:fn=>{q.push(fn);return q.length;},clearTimer:h=>{q[h-1]=null;},fire(){const fns=q.splice(0).filter(Boolean);fns.forEach(f=>f());}};}
const tick=()=>new Promise(r=>setTimeout(r,5));

async function prepare(M){
  const {loadScenario,baseFixture,SCHEMA}=M;
  const fixture=baseFixture();
  const t=timers(),idb=fakeIdb(),ls=fakeLs();
  const a=ST.createStateStorage({idb,ls,delay:500,setTimer:t.setTimer,clearTimer:t.clearTimer});
  const out={};
  out.emptyLoad=await a.load();                                     // nothing saved → FY26 default
  a.save(fixture);a.save({...fixture,sequence:1101});a.save({...fixture,sequence:1102});
  out.putsBeforeFire=idb.puts;t.fire();await tick();out.putsAfterFire=idb.puts;
  out.roundTrip=(await ST.createStateStorage({idb,ls}).load());
  out.lsLeftover=ls.m.has('hcc-prototype-v2');
  // flush() writes immediately
  a.save({...fixture,sequence:1200});await a.flush();out.flushSeq=JSON.parse(idb.data.get('current')).sequence;
  // IndexedDB unavailable → localStorage fallback for small states
  const ls2=fakeLs(),b=ST.createStateStorage({idb:fakeIdb({failOpen:true}),ls:ls2,delay:0,setTimer:fn=>{fn();return 1;},clearTimer(){}});
  b.save(fixture);await b.flush();await tick();out.fallbackSaved=ls2.m.has('hcc-prototype-v2');
  out.fallbackLoad=(await ST.createStateStorage({idb:fakeIdb({failOpen:true}),ls:ls2}).load());
  // IndexedDB unavailable and state too large → error callback, save() reports false afterwards
  const ls3=fakeLs();let errors=0;
  const c=ST.createStateStorage({idb:fakeIdb({failOpen:true}),ls:ls3,delay:0,setTimer:fn=>{fn();return 1;},clearTimer(){}});
  c.onError(()=>{errors++;});c.save({...fixture,pad:'x'.repeat(ST.LS_LIMIT)});await c.flush();await tick();
  out.tooLargeErrors=errors;out.tooLargeNext=c.save(fixture);
  // old schema in IndexedDB → reset to the default scenario with a reason
  const idb4=fakeIdb();idb4.data.set('current',JSON.stringify({schema:4,bookings:[]}));
  out.oldSchema=await ST.createStateStorage({idb:idb4,ls:fakeLs()}).load();
  out.fy26=loadScenario('FY26');out.schema=SCHEMA;
  return out;
}

globalThis.__historyPrep=await prepare(await import('./.generated/mock.mjs'));

export function register({test,M,assert}){
  const {loadScenario,baseFixture,scenarios,DEFAULT_SCENARIO,totals,metrics,drawer,availableRaw,available,execute,restoreState,SCHEMA,buildHistory}=M;
  // Top-level async work happens in prepare(); tests are synchronous. prepare() result is cached on globalThis.
  const P=globalThis.__historyPrep;
  const base=baseFixture(),fy=P.fy26;
  const hist=fy.bookings.filter(b=>b.id.startsWith('HCC-H'));
  const departed=hist.filter(b=>b.status==='Departed');
  const live=['Departed','Confirmed','In house','Held'];

  test('[HIST-A] FY26 is the first scenario, the startup default, and loads with the current schema',()=>{
    assert.equal(DEFAULT_SCENARIO,'FY26');assert.equal(scenarios[0].id,'FY26');assert.match(scenarios[0].name,/FY 2026-27 history/);
    assert.equal(fy.scenario,'FY26');assert.equal(fy.schema,SCHEMA);assert.equal(SCHEMA,8);assert.equal(fy.rooms.length,24);assert.equal(fy.date,'2026-10-24');
  });
  test('[HIST-A] generator is deterministic (same seed → identical hash; other seed differs)',()=>{
    const h1=sha(buildHistory(baseFixture())),h2=sha(buildHistory(baseFixture()));
    assert.equal(h1,h2);
    assert.notEqual(h1,sha(buildHistory(baseFixture(),{seed:7})));
    assert.equal(sha(loadScenario('FY26').bookings),sha(loadScenario('FY26').bookings));
  });
  test('[HIST-A] BASE is untouched: its bookings, rooms and today figures are identical in FY26',()=>{
    for(const b of base.bookings)assert.deepEqual(fy.bookings.find(x=>x.id===b.id),b);
    assert.deepEqual(fy.rooms,base.rooms);assert.deepEqual(metrics(fy),metrics(base));assert.equal(drawer(fy),drawer(base));assert.equal(drawer(fy),1075000);
    assert.equal(metrics(fy).occupied,8);assert.equal(metrics(fy).collections,2150000);
    assert.equal(fy.rooms.find(r=>r.id==='308').blocked,true);assert.equal(fy.closedDates.includes('2026-10-23'),true);
  });
  test('[HIST-A] history stays inside 1 Apr – 23 Oct 2026; ids, receipts and approvals are unique',()=>{
    assert.ok(hist.length>1000);
    for(const b of hist){assert.ok(b.arrival>=FROM&&b.departure<=TO,b.id);assert.ok(b.arrival<b.departure,b.id);
      for(const l of b.lines)assert.ok(l.date>=FROM&&l.date<TO,b.id+' line '+l.date);
      for(const r of b.receipts)assert.ok(r.date>=FROM&&r.date<=TO,b.id+' receipt '+r.date);}
    const uniq=xs=>new Set(xs).size===xs.length;
    assert.ok(uniq(fy.bookings.map(b=>b.id)));assert.ok(uniq(fy.bookings.flatMap(b=>b.receipts.map(r=>r.id))));assert.ok(uniq(fy.approvals.map(a=>a.id)));
  });
  test('[HIST-A] no room is double-booked and room 308 stays out of service',()=>{
    const byRoom=new Map();
    for(const b of fy.bookings.filter(b=>live.includes(b.status))){if(!byRoom.has(b.room))byRoom.set(b.room,[]);byRoom.get(b.room).push(b);}
    for(const [room,list] of byRoom){list.sort((a,b)=>a.arrival<b.arrival?-1:1);
      for(let i=1;i<list.length;i++)assert.ok(list[i-1].departure<=list[i].arrival,`room ${room}: ${list[i-1].id} overlaps ${list[i].id}`);}
    assert.equal(byRoom.has('308'),false);
    // the live in-house guests of BASE are still bookable-free of history: availability agrees with the rule used by BOOK
    for(const b of base.bookings.filter(b=>b.status==='In house'))assert.equal(availableRaw(fy,b.room,b.arrival,b.departure,b.id),true);
  });
  test('[HIST-A] departed stays: one room-night line per night, tax snapshot, folio balanced, deposits returned, invoice numbered',()=>{
    let n=0;const inv=new Set();
    for(const b of departed){
      const nights=Math.round((Date.parse(b.departure)-Date.parse(b.arrival))/86400000);
      const room=b.lines.filter(l=>l.kind==='room');assert.equal(room.length,nights,b.id);
      for(let i=0;i<nights;i++){const d=new Date(Date.parse(b.arrival)+i*86400000).toISOString().slice(0,10);assert.ok(room.some(l=>l.date===d&&l.taxBps===1000&&l.amount===b.rate),b.id+' '+d);}
      const t=totals(b);assert.equal(t.balance,0,b.id);assert.equal(t.deposit,0,b.id);assert.equal(b.registered,true);
      assert.ok(b.invoiceNo&&!inv.has(b.invoiceNo));inv.add(b.invoiceNo);n++;}
    assert.equal(fy.invoiceSeqC,n);
  });
  test('[HIST-A] receivables: 1–6 company-credit folios, each within the manager limit with an executed approval',()=>{
    const cc=departed.filter(b=>b.receipts.some(r=>r.kind==='company-credit'));
    assert.ok(cc.length>=1&&cc.length<=6,'count '+cc.length);
    for(const b of cc){const r=b.receipts.find(r=>r.kind==='company-credit'),a=fy.approvals.find(a=>a.id===r.approvalId);
      assert.ok(a&&a.status==='Executed'&&a.booking===b.id&&a.amount===r.amount);assert.ok(b.company?.name.endsWith('(fictional)'));assert.ok(totals(b).total<=1000000);}
  });
  test('[HIST-A] cancelled stays net to zero, no-shows are charged only against an advance, neither holds a room',()=>{
    for(const b of hist.filter(b=>b.status==='Cancelled')){assert.equal(totals(b).paid,0);assert.equal(totals(b).balance,0);assert.ok(b.cancelReason);
      for(const r of b.receipts.filter(r=>r.kind==='refund'))assert.ok(b.receipts.some(p=>p.id===r.refundOf&&p.amount===r.amount));}
    const ns=hist.filter(b=>b.status==='No show');assert.ok(ns.length>5);
    for(const b of ns)assert.equal(totals(b).balance,0);
    assert.ok(hist.filter(b=>b.status==='Cancelled').length>30);
  });
  test('[HIST-A] realistic totals: occupancy ≈56 %, ADR ≈₹2,650, 1,200–1,400 bookings, room revenue ₹65–75 lakh (±10 %)',()=>{
    const lines=departed.flatMap(b=>b.lines.filter(l=>l.kind==='room'));
    const occ=lines.length/(23*206),adr=lines.reduce((a,l)=>a+l.amount,0)/lines.length/100,rev=lines.reduce((a,l)=>a+l.amount,0)/100;
    console.log(`  [HIST-A] stats: nights ${lines.length}, occupancy ${(occ*100).toFixed(1)}%, ADR ₹${adr.toFixed(0)}, room revenue ₹${(rev/1e5).toFixed(1)} lakh, bookings ${hist.length}, departed ${departed.length}`);
    assert.ok(occ>0.504&&occ<0.616,'occupancy '+occ);assert.ok(adr>2385&&adr<2915,'ADR '+adr);
    assert.ok(hist.length>=1200&&hist.length<=1400,'bookings '+hist.length);assert.ok(rev>=65e5*0.9&&rev<=75e5*1.1+0,'revenue '+rev);
  });
  test('[HIST-A] budgets: generation ≤1.5 s, state JSON ≤12 MB, a command on FY26 stays fast',()=>{
    const runs=[];for(let i=0;i<3;i++){const t=performance.now();loadScenario('FY26');runs.push(performance.now()-t);}
    runs.sort((a,b)=>a-b);const bytes=JSON.stringify(fy).length;
    const s=loadScenario('FY26');let t=performance.now();const next=execute(s,'owner',{type:'BOOK',guest:'History Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2});const cmd=performance.now()-t;
    console.log(`  [HIST-A] budgets: generate ${runs[1].toFixed(0)} ms (median of 3), JSON ${(bytes/1e6).toFixed(2)} MB, one execute() ${cmd.toFixed(0)} ms`);
    assert.ok(runs[1]<=1500,'generation '+runs[1]);assert.ok(bytes<=12e6,'size '+bytes);assert.ok(cmd<=500,'execute '+cmd);
    assert.equal(next.bookings.at(-1).room,'105');assert.equal(available(s,'105',s.date,'2026-10-26'),true);
  });
  test('[HIST-A] restoreState: default scenario FY26 only when asked; pure default stays BASE',()=>{
    assert.equal(restoreState(null).state.scenario,'BASE');
    const r=restoreState(null,false,DEFAULT_SCENARIO);assert.equal(r.state.scenario,'FY26');assert.equal(r.reset,false);
    const old=restoreState(JSON.stringify({schema:4,bookings:[]}),false,DEFAULT_SCENARIO);assert.equal(old.reset,true);assert.equal(old.state.scenario,'FY26');assert.match(old.reason,/older format/);
    assert.equal(restoreState(JSON.stringify(fy),false,DEFAULT_SCENARIO).state.scenario,'FY26');
  });
  test('[HIST-A] storage: empty store opens on FY26 without a reset notice',()=>{assert.equal(P.emptyLoad.state.scenario,'FY26');assert.equal(P.emptyLoad.reset,false);});
  test('[HIST-A] storage: writes are debounced into one IndexedDB put and round-trip',()=>{
    assert.equal(P.putsBeforeFire,0);assert.equal(P.putsAfterFire,1);assert.equal(P.roundTrip.state.sequence,1102);assert.equal(P.roundTrip.reset,false);assert.equal(P.lsLeftover,false);
    assert.equal(P.flushSeq,1200);
  });
  test('[HIST-A] storage: without IndexedDB small states fall back to localStorage; oversized states report an error',()=>{
    assert.equal(P.fallbackSaved,true);assert.equal(P.fallbackLoad.state.scenario,'BASE');assert.equal(P.fallbackLoad.reset,false);
    assert.equal(P.tooLargeErrors,1);assert.equal(P.tooLargeNext,false);
  });
  test('[HIST-A] storage: an older saved schema is reset to the default scenario with a reason',()=>{assert.equal(P.oldSchema.reset,true);assert.equal(P.oldSchema.state.scenario,'FY26');assert.match(P.oldSchema.reason,/older format/);});
}
