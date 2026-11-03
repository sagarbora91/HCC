// F6-H3 · Hall booking prints: the A4 documents render the same figures as the screen (quotation, function sheet, tax invoice, receipt); no prices on the kitchen sheet.
// Names prefixed [HPR].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {hallFixtures} from './_hall.mjs';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/hpr-entry.tsx',`import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {QuotationDoc,BeoDoc,InvoiceDoc,ReceiptDoc} from '../../src/features/hall/prints';
import {hallQuoteDoc,hallBeo,hallInvoiceDoc,hallReceiptDoc} from '../../src/features/hall/logic';
import {money} from '../../src/domain/money';
export {money,hallQuoteDoc,hallBeo,hallInvoiceDoc,hallReceiptDoc};
export const html=(k:string,m:any)=>renderToStaticMarkup(k==='quote'?<QuotationDoc m={m}/>:k==='beo'?<BeoDoc m={m}/>:k==='invoice'?<InvoiceDoc m={m}/>:<ReceiptDoc m={m}/>);`);
  await build({entryPoints:['tests/.generated/hpr-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/hpr.mjs',external:['react','react-dom','lucide-react'],logLevel:'error',jsx:'automatic',loader:{'.tsx':'tsx'}});
  const X=await import('./.generated/hpr.mjs');
  const {quote,at}=hallFixtures(M);const {seed,execute}=M;
  const flat=h=>h.replace(/<[^>]+>/g,' ').replace(/&amp;/g,'&').replace(/\s+/g,' ');
  test('[HPR] quotation print shows the screen totals, every line, the payment plan and the terms',()=>{
    const [s,qid]=quote(seed());const q=s.hallX.quotations.find(o=>o.id===qid),v=q.versions[0],m=X.hallQuoteDoc(s,q,v),h=flat(X.html('quote',m));
    for(const t of [m.totals.taxablePaise,m.totals.cgstPaise,m.totals.sgstPaise,m.totals.totalPaise])assert.ok(h.includes(X.money(t)),'money '+t);
    for(const l of m.lines)assert.ok(h.includes(l.desc),l.desc);assert.match(h,/Payment plan/);assert.match(h,/Advances carry no GST/);assert.match(h,/QT\/2627\/0001/);assert.match(h,/Terms and conditions/);
    assert.ok(h.includes(X.money(m.advance.at(-1).amountPaise)),'balance row printed');
  });
  test('[HPR] function sheet: kitchen copy has no rupee amounts; the desk copy has the account',()=>{
    let [s,qid]=quote(seed());s=execute(s,'manager',{type:'QUO_ACCEPT',id:qid,version:1,advance:{amountPaise:2000000,method:'UPI',reference:'U-1'},key:'p1'});const e=s.hallX.events.at(-1);
    const k=flat(X.html('beo',X.hallBeo(s,e,false))),d=flat(X.html('beo',X.hallBeo(s,e,true)));
    assert.ok(!k.includes('₹'),'no ₹ on the kitchen sheet');assert.match(k,/No prices on this sheet/);assert.match(k,/Silver Veg per plate/);assert.match(k,/150 plate/);
    assert.ok(d.includes('₹')&&/Balance/.test(d));
  });
  test('[HPR] tax invoice and receipt print the posted figures',()=>{
    let [s,qid]=quote(seed());s=execute(s,'manager',{type:'QUO_ACCEPT',id:qid,version:1,advance:{amountPaise:2000000,method:'UPI',reference:'U-1'},key:'p2'});const e0=s.hallX.events.at(-1);
    s=at(s,e0.date);s=execute(s,'reception',{type:'HALL_EVENT_CLOSE',eventId:e0.id,actualPax:160,settle:{mode:'COMPANY',company:'CO-1'}});const e=s.hallX.events.find(o=>o.id===e0.id);
    const m=X.hallInvoiceDoc(s,e),h=flat(X.html('invoice',m));assert.ok(h.includes(m.no));for(const t of [m.taxablePaise,m.cgstPaise,m.sgstPaise,m.totalPaise,m.advanceAppliedPaise,m.balancePaise])assert.ok(h.includes(X.money(t)),'money '+t);
    assert.match(h,/Company credit CO-1/);assert.match(h,/160 plates billed/);
    const r=flat(X.html('receipt',X.hallReceiptDoc(s,e,e.receipts[0])));assert.match(r,/Advance receipt/);assert.match(r,/No GST on an advance/);assert.ok(r.includes(X.money(2000000)));
  });
}
