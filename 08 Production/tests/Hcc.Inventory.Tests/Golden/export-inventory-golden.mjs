// Exports P2 (inventory & reservations) golden figures from the prototype BASE scenario (read-only use of 03 Prototype/src).
// Bundles the prototype mock service with esbuild (like 08 Production/tools/golden/export-golden.mjs), then writes the raw
// BASE records (rooms, live bookings, group blocks, restrictions, tax) and the prototype's own computed figures
// (availability search reasons, group-hold room assignment, stay-restriction violations, nightly rate/tax, BOOK outcomes) to
//   08 Production/tests/Hcc.Inventory.Tests/Golden/inventory-base.golden.json
// The C# tests load the raw records into SQL and must reproduce the figures exactly.
// Usage (Node 24/25): node "08 Production/tests/Hcc.Inventory.Tests/Golden/export-inventory-golden.mjs" [--esbuild <esbuild.cmd>]
import {spawnSync} from 'node:child_process';
import path from 'node:path';import fs from 'node:fs';import os from 'node:os';import {fileURLToPath,pathToFileURL} from 'node:url';
const DIR=path.dirname(fileURLToPath(import.meta.url));
const PROD=path.resolve(DIR,'..','..','..'),ROOT=path.resolve(PROD,'..'),PROTO=path.join(ROOT,'03 Prototype');
const arg=n=>{const i=process.argv.indexOf(n);return i>0?process.argv[i+1]:undefined;};
const esbuild=[arg('--esbuild'),path.join(PROTO,'node_modules','.bin','esbuild.cmd'),
  'E:/Projects/HCC/Hotel Control Centre/03 Prototype/node_modules/.bin/esbuild.cmd'].filter(Boolean).find(p=>fs.existsSync(p));
if(!esbuild)throw new Error('esbuild not found; run npm install in 03 Prototype or pass --esbuild');
const outDir=fs.mkdtempSync(path.join(os.tmpdir(),'hcc-inv-golden-'));
const bundle=path.join(outDir,'mock.mjs');
const r=spawnSync(`"${esbuild}"`,[`"${path.join(PROTO,'src','services','mock','index.ts')}"`,'--bundle','--platform=node','--format=esm',`--outfile="${bundle}"`,'--log-level=error'],{shell:true,encoding:'utf8'});
if(r.status!==0)throw new Error('esbuild failed: '+r.stderr+r.stdout);
const M=await import(pathToFileURL(bundle).href);
const {loadScenario,execute,resSearch,resCapacity,grpHolds,grpSlice,yldSlice,yldStayViolations,roomHold,availableRaw,available,rateFor,taxRateFor,addDays,nights,RES_HOLD_MINUTES}=M;

const s=loadScenario('BASE');
const live=b=>b.status==='Confirmed'||b.status==='In house'||b.status==='Held';
const raw={
  businessDate:s.date,clock:s.clock,taxBps:s.settings.taxBps,holdMinutes:RES_HOLD_MINUTES,
  roomTypes:[...new Set(s.rooms.map(r=>r.type))].map(t=>({code:t,maxOccupancy:resCapacity(s.rooms.find(r=>r.type===t)),rackRatePaise:s.rooms.find(r=>r.type===t).rate})),
  rooms:s.rooms.map(r=>({roomNo:r.id,type:r.type,blocked:!!r.blocked,blockFrom:r.blockFrom??null,blockTo:r.blockTo??null})),
  bookings:s.bookings.filter(live).map(b=>({id:b.id,room:b.room,guest:b.guest,arrival:b.arrival,departure:b.departure,status:b.status,holdUntil:b.holdUntil??null,people:b.people,children:b.children??0})),
  blocks:grpSlice(s).blocks.map(b=>({id:b.id,name:b.name,arrival:b.arrival,departure:b.departure,cutoff:b.cutoff,status:b.status,allot:b.allot,rates:b.rates,
    picked:Object.fromEntries(Object.keys(b.allot).map(t=>[t,b.members.filter(m=>m.type===t&&s.bookings.find(x=>x.id===m.booking&&live(x))).length]))})),
  restrictions:yldSlice(s).restrictions.map(x=>({id:x.id,type:x.type,from:x.from,to:x.to,minStay:x.minStay,cta:x.cta,note:x.note})),
};

// Availability search (REQ-BOOK-001). The prototype's resExclusion has no group-hold reason: a room held for a group block
// (roomHold) falls through to its defensive "Booked". Production reports that case as GroupHold (contracts §3); the export
// marks it so the C# assertion is exact.
const queries=[
  {arrival:s.date,departure:'2026-10-25',people:2},
  {arrival:s.date,departure:'2026-10-25',people:3,type:'Classic'},
  {arrival:'2026-10-25',departure:'2026-10-28',people:1,type:'Deluxe'},
  {arrival:'2026-10-26',departure:'2026-10-28',people:2},
  {arrival:'2026-10-30',departure:'2026-11-01',people:1,type:'Classic'},
  {arrival:'2026-11-06',departure:'2026-11-08',people:4,type:'Suite'},
];
const searches=queries.map(q=>{const res=resSearch(s,q);const rows=[...res.available,...res.excluded].sort((a,b)=>a.room.id.localeCompare(b.room.id));
  return {query:{arrival:q.arrival,departure:q.departure,adults:q.people,children:0,type:q.type??null},
    rooms:rows.map(x=>{const held=!!roomHold(s,x.room.id,q.arrival,q.departure);
      const reasons=x.reasons.map(rr=>rr==='Booked'&&held&&!x.conflict?'GroupHold':rr);
      return {roomNo:x.room.id,available:x.ok,reasons,availableRaw:availableRaw(s,x.room.id,q.arrival,q.departure),bookable:available(s,x.room.id,q.arrival,q.departure)};})};});

const holds=grpHolds(s).map(h=>({roomNo:h.room,block:h.block,type:h.type,from:h.a,to:h.d}));

const violationCases=[['Classic','2026-11-07','2026-11-08'],['Suite','2026-11-08','2026-11-10'],['Classic','2026-11-07','2026-11-09'],['Classic','2026-11-10','2026-11-11'],['Suite','2026-11-08','2026-11-09'],['Deluxe','2026-11-09','2026-11-10']]
  .map(([type,arrival,departure])=>({type,arrival,departure,violations:yldStayViolations(s,type,arrival,departure).map(v=>({code:v.code,restriction:v.restriction,message:v.message}))}));

// Nightly rate and tax: BOOK stores rateFor(room, arrival) as the agreed nightly rate (WF-BOOK-001); each night is posted with
// the tax rate effective on its service date (taxRateFor). Tax per night rounds half away from zero (Math.round on positives).
const quoteCases=[['105','2026-10-24','2026-10-26'],['203','2026-10-25','2026-10-28'],['106','2026-10-26','2026-10-27'],['107','2026-11-07','2026-11-09']].map(([room,arrival,departure])=>{
  const rm=s.rooms.find(r=>r.id===room),rate=rateFor(s,rm,arrival);const ns=[];
  for(let d=arrival;d<departure;d=addDays(d,1)){const bps=taxRateFor(s,d);ns.push({date:d,ratePaise:rate,taxBps:bps,taxPaise:Math.round(rate*bps/10000)});}
  const net=ns.reduce((a,n)=>a+n.ratePaise,0),tax=ns.reduce((a,n)=>a+n.taxPaise,0);
  return {roomNo:room,type:rm.type,arrival,departure,nights:ns,netPaise:net,taxPaise:tax,totalPaise:net+tax,violations:yldStayViolations(s,rm.type,arrival,departure).map(v=>v.code)};});

// BOOK outcomes on the BASE scenario (each from a fresh copy): error code or the created status.
const code=fn=>{try{const n=fn();return {code:'OK',status:n.bookings.at(-1).status,holdUntil:n.bookings.at(-1).holdUntil??null};}catch(e){return {code:e.code??'OTHER'};}};
const bookCases=[
  {name:'free room',uid:'reception',cmd:{room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:2}},
  {name:'hold',uid:'reception',cmd:{room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:2,hold:true}},
  {name:'blocked room 308',uid:'reception',cmd:{room:'308',arrival:'2026-10-24',departure:'2026-10-25',people:1}},
  {name:'running hold on 106',uid:'reception',cmd:{room:'106',arrival:'2026-10-24',departure:'2026-10-25',people:1}},
  {name:'adjacent to 106 hold',uid:'reception',cmd:{room:'106',arrival:'2026-10-25',departure:'2026-10-26',people:1}},
  {name:'in-house 101 overlap',uid:'reception',cmd:{room:'101',arrival:'2026-10-25',departure:'2026-10-27',people:1}},
  {name:'101 after departure',uid:'reception',cmd:{room:'101',arrival:'2026-10-26',departure:'2026-10-27',people:1}},
  {name:'over capacity Classic',uid:'reception',cmd:{room:'105',arrival:'2026-10-24',departure:'2026-10-25',people:3}},
  {name:'group-held room (GB-4)',uid:'reception',cmd:{room:holds.find(h=>h.block==='GB-4').roomNo,arrival:'2026-10-25',departure:'2026-10-26',people:1}},
  {name:'min stay',uid:'reception',cmd:{room:'105',arrival:'2026-11-07',departure:'2026-11-08',people:1}},
  {name:'CTA suite',uid:'reception',cmd:{room:'106',arrival:'2026-11-08',departure:'2026-11-10',people:1}},
  {name:'override by reception',uid:'reception',cmd:{room:'105',arrival:'2026-11-07',departure:'2026-11-08',people:1,restrictionOverride:'Regular guest'}},
  {name:'override short reason',uid:'manager',cmd:{room:'105',arrival:'2026-11-07',departure:'2026-11-08',people:1,restrictionOverride:'x'}},
  {name:'override by manager',uid:'manager',cmd:{room:'105',arrival:'2026-11-07',departure:'2026-11-08',people:1,restrictionOverride:'Regular corporate guest'}},
  {name:'arrival before business date',uid:'reception',cmd:{room:'105',arrival:'2026-10-23',departure:'2026-10-25',people:1}},
  {name:'31 nights',uid:'reception',cmd:{room:'105',arrival:'2026-11-12',departure:'2026-12-13',people:1}},
].map(c=>({...c,cmd:{guest:'Golden Guest',...c.cmd},result:code(()=>execute(loadScenario('BASE'),c.uid,{type:'BOOK',guest:'Golden Guest',...c.cmd}))}));

const out={generatedBy:'08 Production/tests/Hcc.Inventory.Tests/Golden/export-inventory-golden.mjs',source:"03 Prototype loadScenario('BASE')",fictionalDataOnly:true,
  raw,expected:{searches,holds,violationCases,quoteCases,bookCases}};
const dest=path.join(DIR,'inventory-base.golden.json');
fs.writeFileSync(dest,JSON.stringify(out,null,1)+'\n');
fs.rmSync(outDir,{recursive:true,force:true});
console.log('Wrote',dest,'searches',searches.length,'holds',holds.length,'bookCases',bookCases.map(c=>c.name+'='+c.result.code).join('; '));
