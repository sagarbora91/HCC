// W2-10 golden figures: messaging rules exported from the prototype (read-only use of 03 Prototype/src).
// Bundles the prototype messaging domain + mock seed with esbuild, then records: placeholder whitelist, default templates/rules,
// rendered texts for the BASE in-house bookings, masked recipients, block reasons, SMS segments, deterministic delivery outcomes
// and timelines, and the pending triggers of the BASE fixture. Output: messaging-golden.json (same folder).
// Usage (Node 25): node "08 Production/tests/Hcc.Messaging.Tests/Golden/export-messaging-golden.mjs" [--esbuild <path>]
import {spawnSync} from 'node:child_process';
import path from 'node:path';import fs from 'node:fs';import {fileURLToPath,pathToFileURL} from 'node:url';
const DIR=path.dirname(fileURLToPath(import.meta.url));
const PROD=path.resolve(DIR,'..','..','..'),ROOT=path.resolve(PROD,'..'),PROTO=path.join(ROOT,'03 Prototype');
const arg=n=>{const i=process.argv.indexOf(n);return i>0?process.argv[i+1]:undefined;};
const esbuild=[arg('--esbuild'),path.join(PROTO,'node_modules','.bin','esbuild.cmd'),
  'E:/Projects/HCC/Hotel Control Centre/03 Prototype/node_modules/.bin/esbuild.cmd'].filter(Boolean).find(p=>fs.existsSync(p));
if(!esbuild)throw new Error('esbuild not found');
const outDir=path.join(PROD,'tools','golden','.generated');fs.mkdirSync(outDir,{recursive:true});
const entry=path.join(outDir,'entry-messaging.ts'),bundle=path.join(outDir,'mock-messaging.mjs');
const p=f=>JSON.stringify(path.join(PROTO,'src',f).split(path.sep).join('/'));
fs.writeFileSync(entry,`export * from ${p('domain/streams/messaging')};export {seed} from ${p('services/mock/index')};`);
const r=spawnSync(`"${esbuild}"`,[`"${entry}"`,'--bundle','--platform=node','--format=esm',`--outfile="${bundle}"`,'--log-level=error'],{shell:true,encoding:'utf8'});
if(r.status!==0)throw new Error('esbuild failed: '+r.stderr+r.stdout);
const M=await import(pathToFileURL(bundle).href);
const s=M.seed();
const channels=['WhatsApp','SMS','Email'];
const inHouse=s.bookings.filter(b=>b.status==='In house').slice(0,3);
const rendered=[];
for(const b of s.bookings.slice(0,6))for(const t of M.msgTemplates(s)){
  const v=M.msgValues(s,b);rendered.push({booking:b.id,template:t.id,body:M.msgRender(t.body,v).text,subject:M.msgRender(t.subject,v).text,amountText:v.amount,balanceText:v.balance});
}
const recipients=s.bookings.slice(0,6).flatMap(b=>channels.map(c=>{const x=M.msgRecipient(s,b,c);return {booking:b.id,channel:c,masked:x.masked,valid:x.valid,block:M.msgBlockReason(s,b,c),consent:M.msgConsent(s,M.msgGuestKey(b))};}));
const outcomes=[];for(let i=1;i<=40;i++)for(const c of channels)for(const valid of [true,false])outcomes.push({id:'MSG-'+i,channel:c,valid,outcome:M.msgOutcome('MSG-'+i,c,valid)});
const steps={};for(const o of ['Read','Delivered','Failed'])steps[o]=M.msgSteps({outcome:o});
steps.blocked=M.msgSteps({outcome:'Read',blocked:'x'});
const sms=[0,1,159,160,161,306,307,459,460].map(n=>({length:n,segments:M.msgSmsSegments('x'.repeat(n))}));
const unknown=[['Hi {guest} {nope} {x}',M.msgUnknownPlaceholders('Hi {guest} {nope} {x}')],['{firstName} ok',M.msgUnknownPlaceholders('{firstName} ok')]];
const pending=M.msgPendingTriggers(s).map(t=>({key:t.key,rule:t.rule.id,booking:t.booking.id}));
const allEvents=M.msgTriggerEvents(s).length;
const golden={businessDate:s.date,placeholders:[...M.MSG_PLACEHOLDERS],templates:M.MSG_DEFAULT_TEMPLATES,rules:M.MSG_DEFAULT_RULES,
  hotel:s.settings.hotel,checkin:s.settings.checkin,checkout:s.settings.checkout,
  bookings:s.bookings.slice(0,6).map(b=>({id:b.id,room:b.room,status:b.status,arrival:b.arrival,departure:b.departure,guest:b.guest,phone:b.phone??null,email:b.email??null})),
  rendered,recipients,outcomes,steps,sms,unknown,pending,allEvents,inHouse:inHouse.map(b=>b.id)};
fs.writeFileSync(path.join(DIR,'messaging-golden.json'),JSON.stringify(golden,null,1));
console.log('wrote messaging-golden.json',rendered.length,'rendered',pending.length,'pending triggers');
