// W2-10 golden figures: messaging rules exported from the prototype (read-only use of 03 Prototype/src).
// Bundles the prototype messaging domain + mock seed with esbuild (shared helper ../../../tools/golden/golden-lib.mjs), then records:
// placeholder whitelist, default templates/rules, rendered texts for the BASE bookings (amounts under the DEC-069 GST master), masked
// recipients, block reasons, SMS segments, deterministic delivery outcomes and timelines, and the pending triggers of the BASE fixture.
// Output: messaging-golden.json (same folder).
// Usage (Node 25): node "08 Production/tests/Hcc.Messaging.Tests/Golden/export-messaging-golden.mjs" [--check] [--proto <path>]
import path from 'node:path';import {fileURLToPath} from 'node:url';
import {CHECK,bundleSource,writeGolden} from '../../../tools/golden/golden-lib.mjs';
const DIR=path.dirname(fileURLToPath(import.meta.url));
const M=await bundleSource('mock-messaging',"export * from './domain/streams/messaging';export {seed} from './services/mock/index';");
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
if(writeGolden(path.join(DIR,'messaging-golden.json'),JSON.stringify(golden,null,1))&&!CHECK)console.log('wrote messaging-golden.json',rendered.length,'rendered',pending.length,'pending triggers');
