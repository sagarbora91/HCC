// F12 MEAS · legacy golden, HCC side in EXTRACT mode (tools/meas-legacy-golden.ps1 -Extract <LG-EXTRACT folder>). THIS PC ONLY: the extract is the
// legacy customer data and is never committed; outputs go to .scratch\golden\ (not committed).
// Runs every ported study report (prototype selectors = the server's twins, parity-tested) on the lgc rows of the extract, built into LegacyAnRows
// with the SAME rules as the server loader 08 Production/src/Hcc.Reporting/Analytics/SqlAnalyticsInputSource.cs (duplicates out, halls CON / CONAC /
// CONF out of stays and bills, one AnStay per booked room, open stays cut at legacyUntil, bill paid = net - balance (DEC-265), receipts advance +
// payment only). No live HCC rows: the state has the hotel's rooms and no bookings, so the figures are the legacy series only.
// Usage: node tools/legacy/meas_golden_lgc.mjs --extract <dir> --results <file> [--asof 2026-10-31] [--fys 2017-18,2018-19,...]
import {createRequire} from 'node:module';import {readFileSync,writeFileSync,mkdirSync} from 'node:fs';import {dirname,resolve,join} from 'node:path';import {fileURLToPath,pathToFileURL} from 'node:url';
const here=dirname(fileURLToPath(import.meta.url));const proto=resolve(here,'../../03 Prototype');
const arg=k=>{const i=process.argv.indexOf('--'+k);return i>0?process.argv[i+1]:undefined;};
const dir=arg('extract'),out=arg('results'),asOf=arg('asof')??'2026-10-31';
const fys=(arg('fys')??'2017-18,2018-19,2019-20,2020-21,2021-22,2022-23,2023-24,2024-25,2025-26,2026-27').split(',');
if(!dir||!out){console.error('usage: --extract <dir> --results <file>');process.exit(2);}

function csv(file){
  const text=readFileSync(join(dir,file),'utf8').replace(/^﻿/,'');const rows=[];let row=[],f='',q=false;
  for(let i=0;i<text.length;i++){const c=text[i];
    if(q){if(c==='"'){if(text[i+1]==='"'){f+='"';i++;}else q=false;}else f+=c;continue;}
    if(c==='"')q=true;else if(c===','){row.push(f);f='';}else if(c==='\n'){row.push(f.replace(/\r$/,''));rows.push(row);row=[];f='';}else f+=c;}
  if(f.length||row.length){row.push(f.replace(/\r$/,''));rows.push(row);}
  const [h,...body]=rows;return body.filter(r=>r.length>1).map(r=>Object.fromEntries(h.map((k,i)=>[k,r[i]??''])));
}
const N=v=>{const n=Number(v);return Number.isFinite(n)?Math.trunc(n):0;};
const addDays=(d,n)=>{const t=new Date(d+'T00:00:00Z');t.setUTCDate(t.getUTCDate()+n);return t.toISOString().slice(0,10);};
const nightsOf=(a,b)=>Math.max(1,Math.round((Date.parse(b)-Date.parse(a))/86400000));
const HALLS=new Set(['CON','CONAC']);
const typeName=c=>({SUITE:'Suite',PREMIUM:'Premium',DELUXE:'Deluxe',TWIN:'Twin',CONF:'Conference Hall'})[String(c).trim().toUpperCase()]??String(c).trim();
const isHall=(room,type)=>HALLS.has(String(room).trim().toUpperCase())||(!!type&&(/^conf$/i.test(String(type).trim())||/hall|conference/i.test(String(type))));
const sourceOf=raw=>{const s=String(raw??'').trim();if(!s)return ['Direct',undefined];
  if(/^OTA:/i.test(s)){const c=s.slice(4).trim();return ['OTA',/AGODA/i.test(c)?'Agoda':/MMT/i.test(c)?'MakeMyTrip':c||'OTA'];}return [s,undefined];};
const method=m=>({CASH:'Cash',UPI:'UPI',CARD:'Card',BANK:'Bank transfer'})[String(m).toUpperCase()]??'Other';
const LEGACY_UNTIL='2026-10-30';

// ── rows ──
const liveRooms=csv('rooms.csv').map(r=>({no:String(r.roomNo).trim(),code:String(r.roomType).trim()}));
const segs=new Map();for(const g of csv('lgc_room_segment.csv')){if(g.kind!=='booked'||!String(g.room_no).trim())continue;const l=segs.get(g.stay_ref)??[];l.push({room:String(g.room_no).trim(),type:g.room_type_code,rate:N(g.rate_paise)});segs.set(g.stay_ref,l);}
const segType=new Map();for(const l of segs.values())for(const x of l)if(x.type&&!segType.has(x.room))segType.set(x.room,x.type);
const liveType=new Map(liveRooms.map(r=>[r.no,r.code]));const typeOf=room=>liveType.get(room)??segType.get(room);
const stays=[],stayIdByRoom=new Map(),firstStayId=new Map(),firstSale=new Map();
const lst=csv('lgc_stay.csv').filter(r=>r.status!=='Duplicate').sort((a,b)=>a.arrival_date<b.arrival_date?-1:a.arrival_date>b.arrival_date?1:a.legacy_fy<b.legacy_fy?-1:a.legacy_fy>b.legacy_fy?1:N(a.legacy_booking_id)-N(b.legacy_booking_id));
for(const r of lst){
  const ref=`${r.legacy_fy}|${r.legacy_booking_id}`,arrival=r.arrival_date;let nights=Math.max(1,N(r.nights));
  let to=r.departure_date||addDays(arrival,nights);const status=r.status==='CheckedOut'?'Departed':'In house';
  if(status!=='Departed'&&to>addDays(LEGACY_UNTIL,1)){to=arrival>LEGACY_UNTIL?arrival:addDays(LEGACY_UNTIL,1);nights=nightsOf(arrival,to);}
  const booked=segs.get(ref)??[];let rooms=[...new Set(booked.map(b=>b.room).filter(Boolean))].sort();
  if(!rooms.length)rooms=String(r.room_no).split(',').map(x=>x.trim()).filter(Boolean);
  const base=`L${r.legacy_fy}-${N(r.legacy_booking_id)}`;const [src,ch]=sourceOf(r.source_tag);
  for(const room of rooms){
    if(isHall(room,typeOf(room)))continue;
    const id=firstStayId.has(ref)?base+'/'+room:base;if(!firstStayId.has(ref))firstStayId.set(ref,id);stayIdByRoom.set(ref+'#'+room,id);
    stays.push({id,guestKey:r.customer_key,room,from:arrival,to,nights,ratePaise:booked.find(b=>b.room===room)?.rate??0,source:src,...(ch?{channel:ch}:{}),purpose:r.purpose||undefined,
      status,legacy:true,mealPlan:r.meal_plan||undefined,people:N(r.adults)+N(r.children)});
    if(!firstSale.has(room)||arrival<firstSale.get(room))firstSale.set(room,arrival.slice(0,7)+'-01'); // in service from the MONTH of the first sale (Occupancy workbook)
  }
}
// Receivables rule of the workbook (build_receivables_report.py, DEC-265): a bill flagged unpaid (paid_flag N) owes
// max(balance - receipts after the bill date, 0) when the stored balance is positive, else max(net - max(advance, payments - refunds), 0),
// over the receipts linked by stay_ref OR bill ref (each receipt row once). Every other bill is paid in full. paidPaise = net - due.
const rcpt=new Map();
for(const x of csv('lgc_receipt.csv')){const key=`${x.legacy_fy}|${x.legacy_source}|${x.legacy_row_id}`;for(const r of new Set([x.stay_ref,x.bill_ref].filter(Boolean))){const m=rcpt.get(r)??new Map();m.set(key,x);rcpt.set(r,m);}}
function legacyDue(b){
  if(b.paid_flag!=='N')return 0;
  const rs=new Map([...(rcpt.get(b.stay_ref)??new Map()),...(rcpt.get(`${b.legacy_fy}|${b.legacy_bill_no}`)??new Map())]);
  let pay=0,refund=0,later=0;for(const x of rs.values()){const a=N(x.amount_paise);if(x.kind==='payment'||x.kind==='advance'){pay+=a;if(x.received_date>b.bill_date)later+=a;}else if(x.kind==='refund')refund+=a;}
  const net=N(b.net_paise),adv=N(b.advance_paise),bal=N(b.balance_paise);
  return bal>0?Math.max(bal-later,0):Math.max(net-Math.max(adv,pay-refund),0);
}
const bills=[],billOfStay=new Map();
for(const b of csv('lgc_bill.csv').sort((a,b)=>a.bill_date<b.bill_date?-1:a.bill_date>b.bill_date?1:0)){
  const rooms=String(b.room_no).split(',').map(x=>x.trim()).filter(Boolean);if(!rooms.length||rooms.some(x=>HALLS.has(x.toUpperCase())))continue;
  const id=`LB${b.legacy_fy}-${b.legacy_bill_no}`,ref=b.stay_ref||'';
  const stay=ref?(stayIdByRoom.get(ref+'#'+rooms[0])??firstStayId.get(ref)??'L-stay-'+ref):'';if(ref&&!billOfStay.has(ref))billOfStay.set(ref,id);
  const net=N(b.net_paise),bal=N(b.balance_paise),gst=N(b.room_gst_paise)+N(b.fb_gst_paise)+N(b.laundry_gst_paise)+N(b.other_gst_paise);
  bills.push({id,stay,date:b.bill_date,roomChargePaise:N(b.room_charge_paise),fnbPaise:N(b.fb_paise),otherPaise:N(b.extra_paise)+N(b.laundry_paise)+N(b.other_paise),
    discountPaise:N(b.discount_paise),gstPaise:gst,totalPaise:net,chargedNights:N(b.nights)*rooms.length,paidPaise:Math.max(0,net-Math.min(Math.max(0,net),legacyDue(b))),legacy:true});void bal;
}
const billIds=new Set(bills.map(b=>b.id));
const receipts=csv('lgc_receipt.csv').filter(x=>x.kind==='advance'||x.kind==='payment').sort((a,b)=>a.received_date<b.received_date?-1:a.received_date>b.received_date?1:0).map((x,i)=>{
  const br=x.bill_ref?`LB${x.bill_ref.replace('|','-')}`:'';const bill=br&&billIds.has(br)?br:x.stay_ref?billOfStay.get(x.stay_ref)??'':'';
  return {id:`LR${x.legacy_fy}-${x.legacy_source}-${x.legacy_row_id||i}`,bill,date:x.received_date,method:method(x.method),amountPaise:N(x.amount_paise),legacy:true};});
const anRooms=[];
for(const x of liveRooms){if(isHall(x.no,x.code))continue;const suite=x.no==='211'||/suite/i.test(x.code);anRooms.push({id:x.no,type:suite?'Suite':typeName(x.code),inServiceFrom:firstSale.get(x.no)??'2000-01-01',...(suite?{suite:true}:{})});}
for(const [room,first] of [...firstSale].sort()){if(liveType.has(room))continue;const t=segType.get(room)??'';const suite=room==='211'||/^suite$/i.test(t);anRooms.push({id:room,type:suite?'Suite':typeName(t),inServiceFrom:first,...(suite?{suite:true}:{})});}

// ── run the ported reports ──
const {build}=createRequire(resolve(proto,'package.json'))('esbuild');
const bundle=resolve(proto,'tests/.generated/meas-golden-lgc.mjs');
await build({stdin:{contents:"export * from './src/services/mock/index.ts';export {setLegacyAnSource} from './src/domain/analytics/legacy-input.ts';",resolveDir:proto,loader:'ts',sourcefile:'golden-entry.ts'},
  bundle:true,platform:'node',format:'esm',outfile:bundle,logLevel:'error'});
const M=await import(pathToFileURL(bundle).href);
const s=structuredClone(M.seed());s.date=asOf;s.clock=asOf+'T23:00';s.bookings=[];s.audit=[];s.expenses=[];
// No live rooms in the state: the rooms come with the legacy rows (in service from the first sale, as SqlAnalyticsInputSource sets them on the
// server); live rooms in s.rooms would enter buildAnInput with in-service 2000-01-01 and win over the legacy rows.
s.rooms=[];
M.setLegacyAnSource(()=>({rooms:anRooms,stays,bills,receipts}));
const ids=M.allReports().map(d=>d.id).filter(id=>id.startsWith('rpt.study.')&&id!=='rpt.study.management-pack');
const reports=[];
for(const id of ids){const secs=[];let err;
  for(const fy of fys){try{const r=M.runReport(s,'owner',id,{fy},{all:true});
      for(const t of M.rptTables(r))secs.push({key:`${fy}/${t.key}`,title:t.title??r.title,columns:t.columns.map(c=>({key:c.key,label:c.label,type:c.type})),rows:t.rows.map(x=>({kind:x.kind,cells:x.cells}))});}
    catch(e){err=`${fy}: ${e?.message??e}`;}}
  reports.push(secs.length?{id,sections:secs}:{id,error:err});}
mkdirSync(dirname(resolve(out)),{recursive:true});
writeFileSync(out,JSON.stringify({source:'legacy extract (this PC only, never committed) through the prototype selectors',asOf,counts:{stays:stays.length,bills:bills.length,receipts:receipts.length,rooms:anRooms.length},reports}));
console.log(`HCC side (extract): ${stays.length} stays, ${bills.length} bills, ${receipts.length} receipts, ${anRooms.length} rooms; ${reports.length} report(s), ${reports.filter(r=>r.error).length} failed.`);
