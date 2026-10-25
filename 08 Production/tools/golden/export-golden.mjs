// Exports golden figures from the prototype BASE fixture (read-only use of 03 Prototype/src) for the C# platform tests.
// Bundles the prototype mock service with esbuild, runs seed() and the worked Room 105 journey (03 Prototype/tests/run.mjs),
// and writes the raw source records + the prototype's own computed figures to
//   08 Production/tests/Hcc.Platform.Tests/Golden/base-fixture.golden.json
// The C# tests recompute the figures from the raw records with the C# money kernel and must match exactly.
// Usage (Node 25): node "08 Production/tools/golden/export-golden.mjs" [--esbuild <path to esbuild.cmd>]
import {spawnSync} from 'node:child_process';
import path from 'node:path';import fs from 'node:fs';import {fileURLToPath,pathToFileURL} from 'node:url';
const DIR=path.dirname(fileURLToPath(import.meta.url));
const PROD=path.resolve(DIR,'..','..'),ROOT=path.resolve(PROD,'..'),PROTO=path.join(ROOT,'03 Prototype');
const arg=n=>{const i=process.argv.indexOf(n);return i>0?process.argv[i+1]:undefined;};
const candidates=[arg('--esbuild'),path.join(PROTO,'node_modules','.bin','esbuild.cmd'),
  'E:/Projects/HCC/Hotel Control Centre/03 Prototype/node_modules/.bin/esbuild.cmd'].filter(Boolean);
const esbuild=candidates.find(p=>fs.existsSync(p));
if(!esbuild)throw new Error('esbuild not found; run npm install in 03 Prototype or pass --esbuild');
const outDir=path.join(PROD,'tools','golden','.generated');fs.mkdirSync(outDir,{recursive:true});
const bundle=path.join(outDir,'mock.mjs');
const r=spawnSync(`"${esbuild}"`,[`"${path.join(PROTO,'src','services','mock','index.ts')}"`,'--bundle','--platform=node','--format=esm',`--outfile="${bundle}"`,'--log-level=error'],{shell:true,encoding:'utf8'});
if(r.status!==0)throw new Error('esbuild failed: '+r.stderr+r.stdout);
const M=await import(pathToFileURL(bundle).href);
const {seed,execute,totals,drawer,metrics,drawerOutletCash}=M;

const s=seed();
const receipt=x=>({bookingId:x.b,date:x.r.date,method:x.r.method,kind:x.r.kind,amountPaise:x.r.amount});
const base={
  businessDate:s.date,openingCashPaise:s.openingCash,
  folioReceipts:s.bookings.flatMap(b=>b.receipts.map(r=>receipt({b:b.id,r}))),
  outletReceipts:(s.posX?.receipts??[]).map(r=>({date:r.date,method:r.method,amountPaise:r.amount})),
  expenses:s.expenses.map(e=>({id:e.id,date:e.date,status:e.status,method:e.method,amountPaise:e.amount})),
  expected:{drawerPaise:drawer(s),collectionsPaise:metrics(s).collections,outletCashPaise:drawerOutletCash(s),occupied:metrics(s).occupied}};

// Worked journey (03 Prototype/tests/run.mjs "Worked ₹4,400 journey and deposit separation").
let j=execute(s,'owner',{type:'BOOK',guest:'Journey Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2});
const id=j.bookings.at(-1).id;
j=execute(j,'owner',{type:'REGISTER',id});j=execute(j,'owner',{type:'CHECKIN',id});
j=execute(j,'owner',{type:'POST_NIGHTS',id});j=execute(j,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000});
j=execute(j,'owner',{type:'DISCOUNT',id,amount:50000,reason:'Service recovery'});
j=execute(j,'manager',{type:'APPROVAL',id:j.approvals.at(-1).id,approve:true});
j=execute(j,'cashier',{type:'PAYMENT',id,amount:100000,method:'Cash',kind:'payment',key:'advance-1'});
j=execute(j,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit'});
const mid=totals(j.bookings.find(b=>b.id===id));
j=execute(j,'cashier',{type:'PAYMENT',id,amount:340000,method:'UPI',kind:'payment',reference:'DEMO-UPI'});
j=execute(j,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit-return'});
const bk=j.bookings.find(b=>b.id===id);
const journey={room:bk.room,
  lines:bk.lines.map(l=>({kind:l.kind,label:l.label,date:l.date,amountPaise:l.amount,taxBps:l.taxBps})),
  receipts:bk.receipts.map(r=>({date:r.date,method:r.method,kind:r.kind,amountPaise:r.amount})),
  expected:{balanceBeforeSettlementPaise:mid.balance,...Object.fromEntries(Object.entries(totals(bk)).map(([k,v])=>[k+'Paise',v]))}};

const out={generatedBy:'08 Production/tools/golden/export-golden.mjs',source:'03 Prototype BASE fixture (seed()) + worked Room 105 journey',
  fictionalDataOnly:true,base,journey};
const dest=path.join(PROD,'tests','Hcc.Platform.Tests','Golden','base-fixture.golden.json');
fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,JSON.stringify(out,null,1)+'\n');
console.log('Wrote',dest,JSON.stringify(base.expected),JSON.stringify(journey.expected));
