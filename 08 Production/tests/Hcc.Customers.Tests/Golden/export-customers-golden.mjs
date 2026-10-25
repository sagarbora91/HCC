// W3-5 golden figures: Customer 360 index of the prototype FY26 scenario (read-only use of 03 Prototype/src).
// Bundles domain/streams/customers + loadScenario with esbuild, records the raw inputs (guests, bookings with folio total and nightly rate) and the
// prototype's customerIndex / customerSearch results. Output: customers-golden.json (same folder).
// Usage (Node 25): node "08 Production/tests/Hcc.Customers.Tests/Golden/export-customers-golden.mjs" [--esbuild <path>]
import {spawnSync} from 'node:child_process';
import path from 'node:path';import fs from 'node:fs';import {fileURLToPath,pathToFileURL} from 'node:url';
const DIR=path.dirname(fileURLToPath(import.meta.url));
const PROD=path.resolve(DIR,'..','..','..'),ROOT=path.resolve(PROD,'..'),PROTO=path.join(ROOT,'03 Prototype');
const arg=n=>{const i=process.argv.indexOf(n);return i>0?process.argv[i+1]:undefined;};
const esbuild=[arg('--esbuild'),path.join(PROTO,'node_modules','.bin','esbuild.cmd'),
  'E:/Projects/HCC/Hotel Control Centre/03 Prototype/node_modules/.bin/esbuild.cmd'].filter(Boolean).find(p=>fs.existsSync(p));
if(!esbuild)throw new Error('esbuild not found');
const outDir=path.join(PROD,'tools','golden','.generated');fs.mkdirSync(outDir,{recursive:true});
const entry=path.join(outDir,'entry-customers.ts'),bundle=path.join(outDir,'mock-customers.mjs');
const p=f=>JSON.stringify(path.join(PROTO,'src',f).split(path.sep).join('/'));
fs.writeFileSync(entry,`export * from ${p('domain/streams/customers')};export {totals} from ${p('domain/selectors')};export {nights} from ${p('domain/money')};export {loadScenario} from ${p('services/mock/index')};`);
const r=spawnSync(`"${esbuild}"`,[`"${entry}"`,'--bundle','--platform=node','--format=esm',`--outfile="${bundle}"`,'--log-level=error'],{shell:true,encoding:'utf8'});
if(r.status!==0)throw new Error('esbuild failed: '+r.stderr+r.stdout);
const M=await import(pathToFileURL(bundle).href);
const s=M.loadScenario('FY26');
const idx=M.customerIndex(s);
const guests=(s.guests||[]).map(g=>({id:g.id,name:g.name,phone:g.phone||'',email:g.email||null,preferences:g.preferences||null}));
const bookings=s.bookings.map(b=>({id:b.id,guestId:b.guestId||null,guest:b.guest,phone:b.phone||null,email:b.email||null,arrival:b.arrival,departure:b.departure,status:b.status,room:b.room,total:M.totals(b).total,rate:b.rate}));
const customers=idx.map(c=>({key:c.key,name:c.name,stays:c.stays.length,completed:c.completed.length,nights:c.nights,lifetime:c.lifetime,avgRate:c.avgRate,firstStay:c.firstStay??null,lastStay:c.lastStay??null,
  current:c.current?.id??null,upcoming:c.upcoming.map(b=>b.id),cancelled:c.cancelled,topRooms:c.topRooms,stayIds:c.stays.map(b=>b.id)}));
const rep=idx.find(c=>c.completed.length>=3);
const named=idx.find(c=>c.name.includes(' '));const [first,last]=named.name.split(' ');
const phoned=idx.find(c=>c.phone);const d=phoned.phone.replace(/\D/g,'');
const searches=[{q:named.name,phoneOk:true},{q:last+' '+first,phoneOk:true},{q:first+' zzzzqq',phoneOk:true},{q:'',phoneOk:true},{q:d.slice(-6),phoneOk:true},{q:d.slice(-2),phoneOk:true},{q:d.slice(-6),phoneOk:false},{q:s.bookings.find(b=>b.id.startsWith('HCC-H05'))?.id??'HCC-H05',phoneOk:true}]
  .map(x=>({...x,keys:M.customerSearch(idx,x.q,x.phoneOk).map(c=>c.key).sort()}));
fs.writeFileSync(path.join(DIR,'customers-golden.json'),JSON.stringify({date:s.date,bookingCount:s.bookings.length,guests,bookings,customers,searches,repeatKey:rep?.key??null},null,0));
console.log('wrote customers-golden.json',customers.length,'customers',bookings.length,'bookings');
