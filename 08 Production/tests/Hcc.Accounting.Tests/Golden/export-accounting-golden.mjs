// W2-3 golden figures: accounting vouchers, trial balance, CSV and simulated Tally XML, exported from the prototype
// (read-only use of 03 Prototype/src; same esbuild approach as tools/golden/export-golden.mjs).
// For every scenario it records the RAW source facts in the prototype's derivation order (folio lines with their tax, folio
// receipts, outlet receipts, counted expenses, the accepted cash difference) and the prototype's own accLiveVouchers output,
// accTrialBalance, accCsv and accTallyXml. The C# tests rebuild the vouchers from the raw facts with Hcc.Domain.Accounting and
// must match exactly (numbers, types, entries, narration, CSV, XML body).
// Output: 08 Production/tests/Hcc.Accounting.Tests/Golden/accounting-golden.json
// Usage (Node 25): node "08 Production/tests/Hcc.Accounting.Tests/Golden/export-accounting-golden.mjs" [--esbuild <path to esbuild.cmd>]
import {spawnSync} from 'node:child_process';
import path from 'node:path';import fs from 'node:fs';import {fileURLToPath,pathToFileURL} from 'node:url';
const DIR=path.dirname(fileURLToPath(import.meta.url));
const PROD=path.resolve(DIR,'..','..','..'),ROOT=path.resolve(PROD,'..'),PROTO=path.join(ROOT,'03 Prototype');
const arg=n=>{const i=process.argv.indexOf(n);return i>0?process.argv[i+1]:undefined;};
const candidates=[arg('--esbuild'),path.join(PROTO,'node_modules','.bin','esbuild.cmd'),
  'E:/Projects/HCC/Hotel Control Centre/03 Prototype/node_modules/.bin/esbuild.cmd'].filter(Boolean);
const esbuild=candidates.find(p=>fs.existsSync(p));
if(!esbuild)throw new Error('esbuild not found; run npm install in 03 Prototype or pass --esbuild');
const protoSrc='E:/Projects/HCC/Hotel Control Centre/03 Prototype'; // MAIN checkout = golden source (worktree copies may be older)
const outDir=path.join(PROD,'tools','golden','.generated');fs.mkdirSync(outDir,{recursive:true});
const bundle=path.join(outDir,'mock-accounting.mjs');
const r=spawnSync(`"${esbuild}"`,[`"${path.join(protoSrc,'src','services','mock','index.ts')}"`,'--bundle','--platform=node','--format=esm',`--outfile="${bundle}"`,'--log-level=error'],{shell:true,encoding:'utf8'});
if(r.status!==0)throw new Error('esbuild failed: '+r.stderr+r.stdout);
const M=await import(pathToFileURL(bundle).href);
const {seed,execute,drawer,loadScenario,accLiveVouchers,accTrialBalance,accCsv,accTallyXml,accCashDifference,accExceptions,accMapping,lineTax,posSlice,expenseCounts}=M;

/** Raw source facts for a date in the prototype's derivation order (accLiveVouchers). */
function sources(s,date){
  const out=[];
  for(const b of s.bookings)for(const l of b.lines.filter(l=>l.date===date&&l.amount!==0))
    out.push({kind:'line',id:`${b.id}/${l.id}`,sub:l.kind,amountPaise:l.amount,taxPaise:lineTax(l),narration:`${l.label} · ${b.guest} · room ${b.room}`});
  for(const b of s.bookings)for(const r of b.receipts.filter(r=>r.date===date&&r.amount!==0)){
    const label=r.kind==='payment'?'Payment received':r.kind==='deposit'?'Deposit received':r.kind==='refund'?'Payment refunded':r.kind==='deposit-return'?'Deposit returned':r.kind;
    out.push({kind:'receipt',id:`${b.id}/${r.id}`,sub:r.kind,tender:r.method,amountPaise:r.amount,
      narration:r.kind==='company-credit'?`Company credit · ${r.reference} · ${b.guest}`:`${label} · ${r.method} · ${r.reference} · ${b.guest}`});}
  for(const r of posSlice(s).receipts.filter(r=>r.date===date&&r.amount!==0))
    out.push({kind:'pos',id:r.id,sub:r.kind,tender:r.method,amountPaise:r.amount,netPaise:r.net,taxPaise:r.tax,
      narration:`${r.kind==='void'?'Outlet bill void':'Outlet bill'} · ${r.outlet} · ${r.method} · ${r.reference} · ${r.order}`});
  for(const e of s.expenses.filter(e=>e.date===date&&expenseCounts(e)&&e.amount!==0))
    out.push({kind:'expense',id:e.id,sub:e.reversalOf?'reversal':'expense',category:e.category,funding:e.method,amountPaise:e.amount,
      narration:`${e.reversalOf?'Reversal of '+e.reversalOf+' · ':''}${e.vendor} · ${e.category} · ${e.method}`});
  const diff=accCashDifference(s,date);
  if(diff)out.push({kind:'closing',id:`CLOSE/${date}/cash-difference`,sub:'cash-difference',amountPaise:diff,narration:`Cash ${diff>0?'over':'short'} accepted at shift close · ${date}`});
  return out;
}
const voucher=v=>({no:v.no,date:v.date,type:v.type,narration:v.narration,sourceKind:v.source.kind,sourceId:v.source.id,sub:v.source.sub,
  entries:v.entries.map(e=>({account:e.account,debitPaise:e.debit,creditPaise:e.credit})),unmapped:v.unmapped});
function day(name,s,date,extra={}){
  const vs=accLiveVouchers(s,date);
  return {name,date,company:s.settings.hotel,mappingOverrides:extra.mapping??{},sources:sources(s,date),vouchers:vs.map(voucher),
    trialBalance:accTrialBalance(vs).map(x=>({account:x.account,debitPaise:x.debit,creditPaise:x.credit,balancePaise:x.balance})),
    csv:accCsv(vs),exceptions:accExceptions(s,date).filter(x=>x.kind!=='open-day').map(x=>({kind:x.kind,severity:x.severity,key:x.key??null}))};
}
const D='2026-10-24',Y='2026-10-23';
const s0=seed();
const days=[day('base-23oct',s0,Y),day('base-24oct',s0,D)];

// Full day flow (03 Prototype/tests/stream-accounting.mjs "[ACC] full day flow").
let f=seed();const id='HCC-1041';
f=execute(f,'owner',{type:'NIGHT_POST'});
f=execute(f,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000});
f=execute(f,'owner',{type:'DISCOUNT',id,amount:20000,reason:'Goodwill'});
f=execute(f,'cashier',{type:'PAYMENT',id,amount:300000,method:'Cash',kind:'deposit'});
f=execute(f,'cashier',{type:'PAYMENT',id,amount:100000,method:'Cash',kind:'deposit-return'});
f=execute(f,'cashier',{type:'PAYMENT',id,amount:55000,method:'Card',kind:'payment',reference:'DEMO-CARD'});
f=execute(f,'cashier',{type:'REFUND_REQUEST',id,receiptId:'seed-pay-0',amount:5000,reason:'Overcharge'});
f=execute(f,'manager',{type:'APPROVAL',id:f.approvals.at(-1).id,approve:true});
f=execute(f,'owner',{type:'REFUND_EXECUTE',approvalId:f.approvals.at(-1).id});
days.push(day('full-day-flow',f,D));

// Cash short Rs 50 accepted ("[ACC] closed day ... over/short").
let c=seed();c=execute(c,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(c)-5000,reason:'Short by Rs 50'});
c=execute(c,'owner',{type:'SHIFT_DIFFERENCE_DECIDE',id:c.approvals.at(-1).id,approve:true});
days.push(day('cash-short',c,D));

// Expense reversal ("[ACC] ... expense reversal swaps sides").
let e=seed();e=execute(e,'manager',{type:'EXPENSE_REVERSE',id:'EXP-101',reason:'Duplicate entry'});
days.push(day('expense-reversal',e,D));

// Unmapped category (scenario ACC: Kitchen gas) and the same day after the Owner maps it to 5050.
const a=loadScenario('ACC');days.push(day('acc-unmapped',a,D));
const a2=execute(a,'owner',{type:'ACC_MAP_SET',source:'expense:Kitchen gas',account:'5050'});
days.push(day('acc-mapped',a2,D,{mapping:{'expense:Kitchen gas':'5050'}}));

// Tally XML for 23-24 Oct on the base fixture.
const t=accTallyXml(s0,Y,D);
const out={generatedBy:'08 Production/tests/Hcc.Accounting.Tests/Golden/export-accounting-golden.mjs',
  source:'03 Prototype BASE fixture + stream-accounting scenarios',fictionalDataOnly:true,defaultMapping:accMapping(s0),days,
  tally:{from:Y,to:D,company:s0.settings.hotel,xml:t.xml,count:t.count,debitPaise:t.debit,creditPaise:t.credit}};
const dest=path.join(DIR,'accounting-golden.json');
fs.writeFileSync(dest,JSON.stringify(out,null,1)+'\n');
console.log('Wrote',dest,days.map(d=>`${d.name}:${d.vouchers.length}`).join(' '),'tally',t.count);
