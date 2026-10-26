// Exports golden figures from the prototype BASE fixture (read-only use of 03 Prototype/src) for the C# platform tests.
// Bundles the prototype mock service with esbuild, runs seed() and the worked Room 105 journey (03 Prototype/tests/run.mjs),
// and writes the raw source records + the prototype's own computed figures to
//   08 Production/tests/Hcc.Platform.Tests/Golden/base-fixture.golden.json
// The C# tests recompute the figures from the raw records with the C# money kernel and must match exactly.
// GST: the prototype's dated GST master (DEC-069) — room nights 5 %, laundry 18 %, restaurant 5 %; outlet receipts carry net + tax.
// Usage (Node 25): node "08 Production/tools/golden/export-golden.mjs" [--check] [--proto <path to 03 Prototype>]   (see golden-lib.mjs)
import path from 'node:path';
import {PROD,CHECK,bundleMock,writeGolden} from './golden-lib.mjs';
const M=await bundleMock('mock');
const {seed,execute,totals,drawer,metrics,drawerOutletCash}=M;

const s=seed();
const receipt=x=>({bookingId:x.b,date:x.r.date,method:x.r.method,kind:x.r.kind,amountPaise:x.r.amount});
const base={
  businessDate:s.date,openingCashPaise:s.openingCash,
  folioReceipts:s.bookings.flatMap(b=>b.receipts.map(r=>receipt({b:b.id,r}))),
  outletReceipts:(s.posX?.receipts??[]).map(r=>({date:r.date,method:r.method,amountPaise:r.amount,netPaise:r.net,taxPaise:r.tax,taxBps:r.taxBps})),
  expenses:s.expenses.map(e=>({id:e.id,date:e.date,status:e.status,method:e.method,amountPaise:e.amount})),
  expected:{drawerPaise:drawer(s),collectionsPaise:metrics(s).collections,outletCashPaise:drawerOutletCash(s),occupied:metrics(s).occupied}};

// Worked journey (03 Prototype/tests/run.mjs "Worked ₹4,265 GST journey ... and deposit separation"): reception asks for the ₹500
// discount (above its limit) and the manager approves it (DEC-041); the UPI settlement pays the balance left after the advance.
let j=execute(s,'owner',{type:'BOOK',guest:'Journey Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2});
const id=j.bookings.at(-1).id;
j=execute(j,'owner',{type:'REGISTER',id});j=execute(j,'owner',{type:'CHECKIN',id});
j=execute(j,'owner',{type:'POST_NIGHTS',id});j=execute(j,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000});
j=execute(j,'reception',{type:'DISCOUNT',id,amount:50000,reason:'Service recovery'});
const approval=j.approvals.at(-1);
if(!approval||approval.status!=='Pending')throw new Error('The ₹500 discount by reception no longer waits for approval; re-check the journey against 03 Prototype/tests/run.mjs.');
j=execute(j,'manager',{type:'APPROVAL',id:approval.id,approve:true});
j=execute(j,'cashier',{type:'PAYMENT',id,amount:100000,method:'Cash',kind:'payment',key:'advance-1'});
j=execute(j,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit'});
const mid=totals(j.bookings.find(b=>b.id===id));
j=execute(j,'cashier',{type:'PAYMENT',id,amount:mid.balance,method:'UPI',kind:'payment',reference:'DEMO-UPI'});
j=execute(j,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit-return'});
const bk=j.bookings.find(b=>b.id===id);
const journey={room:bk.room,
  lines:bk.lines.map(l=>({kind:l.kind,label:l.label,date:l.date,amountPaise:l.amount,taxBps:l.taxBps})),
  receipts:bk.receipts.map(r=>({date:r.date,method:r.method,kind:r.kind,amountPaise:r.amount})),
  expected:{balanceBeforeSettlementPaise:mid.balance,...Object.fromEntries(Object.entries(totals(bk)).map(([k,v])=>[k+'Paise',v]))}};

const out={generatedBy:'08 Production/tools/golden/export-golden.mjs',source:'03 Prototype BASE fixture (seed()) + worked Room 105 journey',
  fictionalDataOnly:true,base,journey};
const dest=path.join(PROD,'tests','Hcc.Platform.Tests','Golden','base-fixture.golden.json');
if(writeGolden(dest,JSON.stringify(out,null,1)+'\n')&&!CHECK)console.log('Wrote',dest,JSON.stringify(base.expected),JSON.stringify(journey.expected));
