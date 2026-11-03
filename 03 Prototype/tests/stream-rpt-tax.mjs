// F7-R5 · batch 2 tax on the report engine: SAC summary (Table 12), documents issued (Table 13 incl. the legacy LGC row), GST by bill, invoice register,
// bill corrections, GST summary (ACC-3). Screen = Excel = print (threeWay), tie to gstr1Summary / accGstSummary, roles (rule V / M), edges, goldens.
import {build} from 'esbuild';
import {threeWay,goldenCheck} from './rpt-golden/harness.mjs';
await build({entryPoints:['tests/rpt-tax-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rpt-tax.mjs',logLevel:'error'});
const M=await import('./.generated/rpt-tax.mjs');
export function register({test,assert}){
  const T='[F7-R5] ';
  const SAC='rpt.tax.sac-summary',DOC='rpt.tax.documents-issued',GBB='rpt.tax.gst-by-bill',REG='rpt.tax.invoice-register',COR='rpt.tax.bill-corrections',GSM='rpt.tax.gst-summary';
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const fy=()=>M.loadScenario('FY26'),lf=()=>M.loadScenario('LF-PARITY');
  const run=(s,uid,id,p={})=>M.runReport(s,uid,id,p,{all:true});
  const rows=r=>r.rows.filter(x=>x.kind==='row');
  const sum=(xs,k)=>xs.reduce((a,x)=>a+x.cells[k],0);

  test(T+'the six reports are registered with the catalogue columns, money flags and gates',()=>{
    const want={[SAC]:[9,true,'PERM-ACC-013'],[DOC]:[7,false],[GBB]:[13,true,'PERM-ACC-013'],[REG]:[14,true,'PERM-ACC-013'],[COR]:[13,true,'PERM-RPT-002'],[GSM]:[4,true,'PERM-ACC-013']};
    for(const [id,[n,money,gate]] of Object.entries(want)){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.columns.length,n,id+' columns');assert.equal(d.moneyFlag,money,id);assert.equal(d.moneyGate,gate,id);assert.equal(d.area,'tax');}
    assert.deepEqual(M.reportDef(GBB).columns.filter(c=>c.mask).map(c=>c.key+':'+c.mask),['party:guestName']);
    assert.deepEqual(M.reportDef(DOC).columns.filter(c=>c.money),[]);
  });

  test(T+'SAC summary: rows and totals equal gstr1Summary(...).sac, quantity = invoices carrying the SAC, three-way equal',()=>{
    const s=fy(),r=run(s,'owner',SAC,{month:'2026-10'}),g=M.gstr1Summary(s,'2026-10-01','2026-10-24');
    const d=rows(r);assert.equal(d.length,g.sac.length);
    g.sac.forEach((x,i)=>{assert.equal(d[i].cells.sac,x.sac);assert.equal(d[i].cells.taxable,x.taxable);assert.equal(d[i].cells.cgst,x.cgst);assert.equal(d[i].cells.sgst,x.sgst);assert.equal(d[i].cells.rate,x.bps/100);assert.equal(d[i].cells.totalValue,x.taxable+x.cgst+x.sgst);});
    const tot=r.rows.at(-1);assert.equal(tot.kind,'total');assert.equal(tot.cells.taxable,sum(d,'taxable'));assert.equal(tot.cells.totalValue,sum(d,'totalValue'));
    assert.equal(tot.cells.taxable,g.rates.reduce((a,x)=>a+(['CDNR','CDNUR'].includes(x.section)?-x.taxable:x.taxable),0),'Table 12 taxable = rate tables net of credit notes');
    assert.ok(d.every(x=>x.cells.uqc==='OTH'&&x.cells.quantity>0));
    threeWay(M,assert,r);
    const lfr=run(lf(),'owner',SAC,{month:'2026-10'});assert.ok(rows(lfr).some(x=>x.cells.taxable<0),'credit notes make a SAC row negative');threeWay(M,assert,lfr);
    assert.equal(r.about.legacy,false);
  });

  test(T+'documents issued: one row per series, cancelled from real numbers, the legacy LGC row first; total row',()=>{
    const s=fy(),r=run(s,'owner',DOC,{month:'2026-10'}),g=M.gstr1Summary(s,'2026-10-01','2026-10-24'),d=rows(r);
    assert.deepEqual(d.map(x=>x.cells.series),g.documents.map(x=>x.docType));
    d.forEach((x,i)=>{assert.equal(x.cells.fromNo,g.documents[i].first);assert.equal(x.cells.toNo,g.documents[i].last);assert.equal(x.cells.total,g.documents[i].count);assert.equal(x.cells.net,x.cells.total-x.cells.cancelled);});
    assert.equal(r.rows.at(-1).cells.total,sum(d,'total'));
    // May 2026: the fictional legacy sample has bill 1204 (before cut-over): an LGC row comes first.
    const may=run(s,'owner',DOC,{month:'2026-05'}),m=rows(may);assert.equal(m[0].cells.series,'LGC');assert.equal(m[0].cells.fromNo,'1204');assert.equal(m[0].cells.total,1);assert.equal(m[0].cells.cancelled,0);
    assert.equal(may.about.legacy,false,'May 2026 is HCC data (April 2026 is the first HCC date)');
    // LF-PARITY: a credit note series, a voided restaurant bill counts as cancelled when present
    const l=run(lf(),'owner',DOC,{month:'2026-10'});assert.ok(rows(l).some(x=>x.cells.series==='CRN'&&x.cells.nature==='Credit notes'));
    threeWay(M,assert,r);threeWay(M,assert,may);threeWay(M,assert,l);
    const s2=fy();const o=s2.posX?.orders.find(x=>x.settlement?.invoiceNo&&!x.settlement.creditNoteNo&&x.settlement.date>='2026-10-01');
    if(o){o.settlement.creditNoteNo='DEMO-CN/2026-27/9999';o.settlement.creditNoteDate=o.settlement.date;const x=rows(run(s2,'owner',DOC,{month:'2026-10'})).find(y=>y.cells.series==='RST');assert.equal(x.cells.cancelled,1);assert.equal(x.cells.net,x.cells.total-1);}
  });

  test(T+'GST by bill: totals tie to the GSTR-1 working summary (credit notes negative), level and zero-tax parameters',()=>{
    const s=fy(),r=run(s,'owner',GBB,{month:'2026-10'}),g=M.gstr1Summary(s,'2026-10-01','2026-10-24'),d=rows(r),tot=r.rows.at(-1);
    const sg=x=>['CDNR','CDNUR'].includes(x.section)?-1:1;
    assert.equal(tot.cells.net,g.rates.reduce((a,x)=>a+sg(x)*x.taxable,0));assert.equal(tot.cells.cgst,g.rates.reduce((a,x)=>a+sg(x)*x.cgst,0));assert.equal(tot.cells.sgst,g.rates.reduce((a,x)=>a+sg(x)*x.sgst,0));
    for(const b of [0,500,1200,1800,2800])assert.equal(tot.cells['tax'+b/100],g.rates.filter(x=>x.bps===b).reduce((a,x)=>a+sg(x)*x.taxable,0),'rate '+b);
    assert.equal(tot.cells.net,sum(d,'net'));assert.equal(d.length,g.documents.reduce((a,x)=>a+x.count,0));
    for(const x of d)assert.equal(x.cells.net,x.cells.tax0+x.cells.tax5+x.cells.tax12+x.cells.tax18+x.cells.tax28,x.cells.billNo);
    const line=run(s,'owner',GBB,{month:'2026-10',level:'line'});assert.ok(line.columns.some(c=>c.key==='item')&&line.columns.some(c=>c.key==='rate'));
    assert.equal(line.rows.at(-1).cells.net,tot.cells.net,'line level has the same total');assert.ok(rows(line).length>d.length);
    const s2=fy();const b=s2.bookings.find(x=>x.invoiceNo&&x.departure>='2026-10-01'&&x.departure<='2026-10-24');b.lines=b.lines.map(l=>({...l,taxBps:0}));
    const ex=run(s2,'owner',GBB,{month:'2026-10',zeroTax:'exclude'});assert.ok(!rows(ex).some(x=>x.cells.billNo===b.invoiceNo)&&rows(run(s2,'owner',GBB,{month:'2026-10'})).some(x=>x.cells.billNo===b.invoiceNo));
    assert.equal(code(()=>run(s,'owner',GBB,{month:'2026-10',level:'sideways'})),'VALIDATION');
    threeWay(M,assert,r);threeWay(M,assert,line);
    const l=run(lf(),'owner',GBB,{month:'2026-10'});assert.ok(rows(l).some(x=>x.cells.net<0));threeWay(M,assert,l);
  });

  test(T+'invoice register: tariff + other + discount + GST = net bill, series / bill type filters, credit notes negative, 92-day limit',()=>{
    const s=fy(),r=run(s,'owner',REG,{from:'2026-10-01',to:'2026-10-24'}),d=rows(r);
    for(const x of d)assert.equal(x.cells.net,x.cells.tariff+x.cells.other+x.cells.discount+x.cells.gst+x.cells.roundOff,x.cells.invoiceNo);
    const tot=r.rows.at(-1);assert.equal(tot.cells.net,sum(d,'net'));assert.equal(tot.cells.net,run(s,'owner',GBB,{month:'2026-10'}).rows.at(-1).cells.net+tot.cells.gst,'net bill = taxable + GST');
    assert.equal(tot.cells.gst,M.gstr1Summary(s,'2026-10-01','2026-10-24').rates.reduce((a,x)=>a+(['CDNR','CDNUR'].includes(x.section)?-1:1)*(x.cgst+x.sgst+x.igst),0));
    assert.ok(d.every(x=>x.cells.room!==''),'FY26 October has room invoices only');
    assert.equal(rows(run(s,'owner',REG,{from:'2026-10-01',to:'2026-10-24',series:'RST'})).length,0);assert.equal(rows(run(s,'owner',REG,{from:'2026-10-01',to:'2026-10-24',billType:'counter'})).length,0);
    const l=lf(),lr=run(l,'owner',REG,{from:'2026-10-01',to:'2026-10-24'}),ld=rows(lr);
    const crn=ld.filter(x=>x.cells.invoiceNo.includes('-CN/'));assert.ok(crn.length&&crn.every(x=>x.cells.net<0));assert.equal(rows(run(l,'owner',REG,{from:'2026-10-01',to:'2026-10-24',billType:'credit note'})).length,crn.length);
    assert.ok(ld.some(x=>x.cells.room===''&&x.cells.credit>0),'a counter bill on credit shows in the Credit column');assert.equal(rows(run(l,'owner',REG,{from:'2026-10-01',to:'2026-10-24',billType:'counter'})).length,1);
    assert.equal(code(()=>run(s,'owner',REG,{from:'2026-04-01',to:'2026-10-24'})),'VALIDATION');assert.equal(code(()=>run(s,'owner',REG,{from:'2026-10-01',to:'2026-10-24',series:'XYZ'})),'VALIDATION');
    threeWay(M,assert,r);threeWay(M,assert,lr);
  });

  test(T+'bill corrections: old / new invoice, amounts, GST of each, who and why; difference total',()=>{
    const l=lf(),r=run(l,'owner',COR,{from:'2026-10-01',to:'2026-10-24'}),d=rows(r);
    assert.equal(d.length,1);const x=d[0].cells;assert.equal(x.oldNo,'DEMO/2026-27/0001');assert.equal(x.newNo,'DEMO/2026-27/0002');assert.equal(x.difference,x.newAmount-x.oldAmount);
    assert.equal(x.oldAmount,1004000);assert.equal(x.newAmount,630000);assert.equal(x.oldGst,54000);assert.equal(x.newGst,30000);assert.equal(x.reason,'Wrong rent and laundry (fictional)');
    const b=l.bookings.find(y=>y.invoiceNo===x.newNo);assert.equal(x.newAmount,M.totals({...b,lines:b.lines.filter(z=>z.id.includes('/n'))}).total,'new amount = the re-issued invoice lines');
    const tot=r.rows.at(-1);assert.equal(tot.kind,'total');assert.equal(tot.cells.difference,x.difference);assert.match(tot.cells.correctedAt,/1 correction/);
    assert.equal(rows(run(l,'owner',COR,{from:'2026-10-01',to:'2026-10-24',correctedBy:'nobody'})).length,0);
    assert.equal(rows(run(l,'owner',COR,{from:'2026-10-01',to:'2026-10-23'})).length,0);
    assert.equal(rows(run(fy(),'owner',COR,{from:'2026-10-01',to:'2026-10-24'})).length,0);
    threeWay(M,assert,r);
    assert.equal(code(()=>run(l,'owner',COR,{from:'2026-04-01',to:'2026-10-24'})),'VALIDATION');
  });

  test(T+'GST summary: the ACC-3 figures on the engine, tie flags and differences in words',()=>{
    const s=fy(),r=run(s,'owner',GSM,{month:'2026-10'}),g=M.accGstSummary(s,'2026-10');
    const at=l=>r.rows.find(x=>x.cells.line===l).cells;
    assert.equal(at('Rate 5 %').amount,g.output.rate5);assert.equal(at('Rate 18 %').amount,g.output.rate18);assert.equal(at('B2B').amount,g.output.b2b);assert.equal(at('B2CS').amount,g.output.b2cs);
    assert.equal(at('Payable in cash').amount,g.setoff.payable);assert.equal(at('Challan to deposit').amount,g.setoff.challan);assert.equal(at('ITC CGST').amount,g.itc.cgst);
    const tot=r.rows.at(-1).cells;assert.equal(tot.tiesGstr1,g.ties.gstr1?1:0);assert.equal(tot.tiesGstr3b,g.ties.gstr3b?1:0);
    assert.equal(r.rows.filter(x=>String(x.cells.line).startsWith('Difference: ')).length,g.ties.differences.length);
    threeWay(M,assert,r);
  });

  test(T+'roles (DEC-202, coordinator ruling): tax reports Owner + Accountant only (PERM-ACC-013); bill corrections PERM-RPT-002 (Owner, Manager, Accountant); Cashier / Reception never see a tax report',()=>{
    const s=fy();
    for(const id of [SAC,DOC,GBB,REG,GSM])for(const u of ['owner','accountant'])assert.equal(code(()=>run(s,u,id,id===REG?{from:'2026-10-20',to:'2026-10-24'}:{month:'2026-10'})),'none',u+' '+id);
    for(const id of [SAC,DOC,GBB,REG,GSM])for(const u of ['manager','reception','cashier'])assert.equal(code(()=>run(s,u,id,id===REG?{from:'2026-10-20',to:'2026-10-24'}:{month:'2026-10'})),'FORBIDDEN',u+' '+id);
    for(const u of ['owner','manager','accountant'])assert.equal(code(()=>run(s,u,COR,{from:'2026-10-20',to:'2026-10-24'})),'none',u+' '+COR);
    for(const u of ['reception','cashier'])assert.equal(code(()=>run(s,u,COR)),'FORBIDDEN',u+' '+COR);
    for(const id of [SAC,DOC,GBB,REG,COR,GSM])for(const u of ['housekeeper','maint','techadmin','supervisor'])assert.equal(code(()=>run(s,u,id)),'FORBIDDEN',u+' '+id);
    for(const u of ['housekeeper','reception','cashier'])assert.ok(!M.reportCatalogue(s,u).some(d=>d.area==='tax'),u);
    assert.ok(M.reportCatalogue(s,'accountant').filter(d=>d.area==='tax').length===6);
    assert.equal(code(()=>M.runReport(s,'accountant',GBB,{month:'2026-10'},{all:true,purpose:'export'})),'none');
    assert.deepEqual(run(s,'accountant',DOC,{month:'2026-10'}).columns.map(c=>c.key),M.reportDef(DOC).columns.map(c=>c.key));
  });

  test(T+'date classes: month edges, no future month, provisional while the day is open, legacy flag before April 2026, CSV formula neutralised',()=>{
    const s=fy();
    assert.equal(code(()=>run(s,'owner',SAC,{month:'2026-13'})),'VALIDATION');assert.equal(code(()=>run(s,'owner',SAC,{month:'2026-11'})),'VALIDATION','a month after the business date is refused');
    assert.equal(run(s,'owner',SAC,{}).params.month,'2026-10','default = the business date\'s month');
    const cur=run(s,'owner',GBB,{month:'2026-10'});assert.equal(cur.about.provisional,!s.closedDates.includes(s.date));
    const old=run(s,'owner',DOC,{month:'2026-03'});assert.equal(old.about.legacy,true);
    const s2=fy();const b=s2.bookings.find(x=>x.invoiceNo&&x.departure>='2026-10-01'&&x.departure<='2026-10-24');b.guest='=HYPERLINK("x")';
    const csv=M.toCsv(run(s2,'owner',GBB,{month:'2026-10'}));assert.ok(!/(^|,)=HYPERLINK/m.test(csv)&&/'=HYPERLINK|"'=HYPERLINK/.test(csv),'formula cell neutralised');
  });

  test(T+'the tax goldens have no drift',()=>{
    const fails=goldenCheck(M,assert).filter(f=>/rpt\.tax\./.test(f));assert.deepEqual(fails,[]);
  });
}
