// P3 golden figures: stays + housekeeping, exported from the prototype (read-only use of 03 Prototype/src).
// Bundles the prototype mock service with esbuild (same approach as tools/golden/export-golden.mjs), then records
//  - the BASE fixture rooms / bookings / tasks / blocks and the prototype's own room board (roomStatus, metrics),
//  - the worked checkout → housekeeping journey on room 105 (room + task state after every step),
//  - the negative cases the C# handlers must reproduce (dirty-room check-in, unpaid checkout, inspection keeps a block).
// Output: 08 Production/tests/Hcc.Stays.Tests/Golden/stays-golden.json
// Usage (Node 25): node "08 Production/tests/Hcc.Stays.Tests/Golden/export-stays-golden.mjs" [--esbuild <path to esbuild.cmd>]
import {spawnSync} from 'node:child_process';
import path from 'node:path';import fs from 'node:fs';import {fileURLToPath,pathToFileURL} from 'node:url';
const DIR=path.dirname(fileURLToPath(import.meta.url));
const PROD=path.resolve(DIR,'..','..','..'),ROOT=path.resolve(PROD,'..'),PROTO=path.join(ROOT,'03 Prototype');
const arg=n=>{const i=process.argv.indexOf(n);return i>0?process.argv[i+1]:undefined;};
const candidates=[arg('--esbuild'),path.join(PROTO,'node_modules','.bin','esbuild.cmd'),
  'E:/Projects/HCC/Hotel Control Centre/03 Prototype/node_modules/.bin/esbuild.cmd'].filter(Boolean);
const esbuild=candidates.find(p=>fs.existsSync(p));
if(!esbuild)throw new Error('esbuild not found; run npm install in 03 Prototype or pass --esbuild');
const outDir=path.join(PROD,'tools','golden','.generated');fs.mkdirSync(outDir,{recursive:true});
const bundle=path.join(outDir,'mock-stays.mjs');
const r=spawnSync(`"${esbuild}"`,[`"${path.join(PROTO,'src','services','mock','index.ts')}"`,'--bundle','--platform=node','--format=esm',`--outfile="${bundle}"`,'--log-level=error'],{shell:true,encoding:'utf8'});
if(r.status!==0)throw new Error('esbuild failed: '+r.stderr+r.stdout);
const M=await import(pathToFileURL(bundle).href);
const {seed,execute,totals,metrics,roomStatus,loadScenario,HccError,hkGuests,stayArrivals}=M;
const code=fn=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};

const s=seed();
const base={
  businessDate:s.date,inspectionRequired:!!s.settings.inspection,
  rooms:s.rooms.map(r=>({roomNo:r.id,type:r.type,hk:r.hk,blocked:!!r.blocked,blockFrom:r.blockFrom??null,blockTo:r.blockTo??null,blockReason:r.blockReason??null})),
  bookings:s.bookings.map(b=>({id:b.id,room:b.room,status:b.status,arrival:b.arrival,departure:b.departure,guest:b.guest,registered:!!b.registered})),
  tasks:s.tasks.map(t=>({id:t.id,room:t.room,booking:t.booking??null,assignee:t.assignee,status:t.status,kind:t.kind??'checkout'})),
  expected:{
    roomStatus:Object.fromEntries(s.rooms.map(r=>[r.id,roomStatus(s,r)])),
    occupied:metrics(s).occupied,available:metrics(s).available,sellable:metrics(s).sellable,
    currentGuest101:hkGuests(s,'101').current?.guest??null,
    arrivals:stayArrivals(s).map(b=>b.id)}};

// Worked journey: walk-in on 105 → register → check in → post nights → settle → checkout → clean → inspect.
const steps=[];let j=s;
const snap=(step,id)=>{const room=j.rooms.find(r=>r.id==='105');const bk=id&&j.bookings.find(b=>b.id===id);const t=id&&j.tasks.find(t=>t.booking===id);
  steps.push({step,roomHk:room.hk,roomStatus:roomStatus(j,room),bookingStatus:bk?bk.status:null,taskStatus:t?t.status:null,tasksForBooking:id?j.tasks.filter(t=>t.booking===id).length:0});};
j=execute(j,'reception',{type:'BOOK',guest:'Journey Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2,source:'Walk-in'});
const id=j.bookings.at(-1).id;snap('book',id);
j=execute(j,'reception',{type:'REGISTER',id});snap('register',id);
j=execute(j,'reception',{type:'CHECKIN',id});snap('checkin',id);
j=execute(j,'owner',{type:'POST_NIGHTS',id});
const due=totals(j.bookings.find(b=>b.id===id)).balance;
const unpaidCheckout=code(()=>execute(j,'reception',{type:'CHECKOUT',id}));
j=execute(j,'cashier',{type:'PAYMENT',id,amount:due,method:'UPI',kind:'payment',reference:'DEMO-UPI-J'});
const bkPaid=j.bookings.find(b=>b.id===id);
const folio={lines:bkPaid.lines.map(l=>({kind:l.kind,date:l.date,amountPaise:l.amount,taxBps:l.taxBps})),
  receipts:bkPaid.receipts.map(x=>({kind:x.kind,method:x.method,amountPaise:x.amount})),balanceDuePaise:due,balanceAtCheckoutPaise:totals(bkPaid).balance};
j=execute(j,'reception',{type:'CHECKOUT',id,key:'checkout:'+id});snap('checkout',id);
const again=execute(j,'reception',{type:'CHECKOUT',id,key:'checkout:'+id});
const replaySame=again===j;
const secondCheckoutWithoutKey=code(()=>execute(j,'reception',{type:'CHECKOUT',id}));
const task=j.tasks.find(t=>t.booking===id);
const hkStartOther=code(()=>execute(j,'housekeeper2',{type:'TASK',id:task.id,action:'start'}));
j=execute(j,task.assignee,{type:'TASK',id:task.id,action:'start'});snap('start',id);
const completeEarly=code(()=>execute(j,task.assignee,{type:'TASK',id:task.id,action:'complete'}));
for(const i of [0,1,2])j=execute(j,task.assignee,{type:'TASK',id:task.id,action:'check',index:i});
j=execute(j,task.assignee,{type:'TASK',id:task.id,action:'complete'});snap('complete',id);
const hkInspect=code(()=>execute(j,task.assignee,{type:'TASK',id:task.id,action:'inspect'}));
const rejectNoReason=code(()=>execute(j,'supervisor',{type:'TASK',id:task.id,action:'reject'}));
j=execute(j,'supervisor',{type:'TASK',id:task.id,action:'reject',reason:'Mirror streaks in bathroom'});snap('reject',id);
for(const i of [0,1,2])j=execute(j,task.assignee,{type:'TASK',id:task.id,action:'check',index:i});
j=execute(j,task.assignee,{type:'TASK',id:task.id,action:'complete'});snap('complete-again',id);
j=execute(j,'supervisor',{type:'TASK',id:task.id,action:'inspect'});snap('inspect',id);
const journey={room:'105',arrival:s.date,departure:'2026-10-26',taskAssignee:task.assignee,folio,steps,
  expected:{unpaidCheckout,replaySame,secondCheckoutWithoutKey,hkStartOther,completeEarly,hkInspect,rejectNoReason}};

// Negative / rule cases.
let s2=loadScenario('S2');const b51=s2.bookings.find(b=>b.id==='HCC-1051');
s2=execute(s2,'reception',{type:'REGISTER',id:'HCC-1051'});
const s3=loadScenario('S3');
let blk=seed();const inspT=blk.tasks.find(t=>t.status==='Inspection');
blk=execute(blk,'supervisor',{type:'BLOCK',id:inspT.room,reason:'Window seal',from:blk.date,to:'2026-10-27'});
blk=execute(blk,'supervisor',{type:'TASK',id:inspT.id,action:'inspect'});
const blkRoom=blk.rooms.find(r=>r.id===inspT.room);
const cases={
  dirtyRoomCheckin:{room:b51.room,roomHk:s2.rooms.find(r=>r.id===b51.room).hk,code:code(()=>execute(s2,'reception',{type:'CHECKIN',id:'HCC-1051'}))},
  unpaidDeparture:{booking:'HCC-1042',balancePaise:totals(s3.bookings.find(b=>b.id==='HCC-1042')).balance,code:code(()=>execute(s3,'reception',{type:'CHECKOUT',id:'HCC-1042'}))},
  inspectionKeepsBlock:{room:inspT.room,hkAfter:blkRoom.hk,blockedAfter:!!blkRoom.blocked,statusAfter:roomStatus(blk,blkRoom),blockTo:blkRoom.blockTo},
  blockOverlappingInHouse:{room:'101',code:code(()=>execute(seed(),'supervisor',{type:'BLOCK',id:'101',reason:'Leak',from:'2026-10-25',to:'2026-10-27'}))},
  blockAfterDeparture:{room:'101',code:code(()=>execute(seed(),'supervisor',{type:'BLOCK',id:'101',reason:'Repaint after checkout',from:'2026-10-26',to:'2026-10-28'}))},
  blockInPast:{code:code(()=>execute(loadScenario('S1'),'supervisor',{type:'BLOCK',id:'105',reason:'Leak',from:'2026-10-20'}))},
  housekeeperBlock:{code:code(()=>execute(loadScenario('S1'),'housekeeper',{type:'BLOCK',id:'105',reason:'Leak'}))},
};

const out={generatedBy:'08 Production/tests/Hcc.Stays.Tests/Golden/export-stays-golden.mjs',source:'03 Prototype BASE fixture (seed()), S2, S3 + worked room 105 checkout/housekeeping journey',
  fictionalDataOnly:true,base,journey,cases};
const dest=path.join(DIR,'stays-golden.json');
fs.writeFileSync(dest,JSON.stringify(out,null,1)+'\n');
console.log('Wrote',dest,JSON.stringify(base.expected),JSON.stringify(journey.expected),JSON.stringify(cases));
