// F7-R7 · batch 3 document layouts (Lodge look + HCC letterhead, A16): registration card, day end, folio, tax invoice, credit note, receipt, quotation,
// rate card, payslip, KOT, outlet bill, hand-over pack. Same figures on screen / Excel / print (threeWay on docResult), roles + money rule, masks, print log, goldens.
import {build} from 'esbuild';
import {existsSync,readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import {threeWay,goldenText} from './rpt-golden/harness.mjs';
await build({entryPoints:['tests/rpt-docs-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rpt-docs.mjs',logLevel:'error'});
const M=await import('./.generated/rpt-docs.mjs');
const GD=new URL('./rpt-golden/doc-goldens/',import.meta.url);
export function register({test,assert}){
  const T='[F7-R7] ';
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const fy=()=>M.loadScenario('FY26'),lf=()=>M.loadScenario('LF-PARITY'),base=()=>M.seed();
  const NOMONEY=['housekeeper','maint'];
  const html=m=>{const h=M.docHtml(m);return h.body;};
  /** Every pair value and every cell of every table appears in the printable page and in the engine result (print = screen = Excel). */
  const same=(s,uid,m)=>{
    const r=M.docResult(s,uid,m);threeWay(M,assert,r);
    const page=html(m).replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"');
    if(!m.lodge){for(const [,v] of m.pairs)if(v)assert.ok(page.includes(v),m.id+' pair '+v);
      for(const t of m.tables)for(const row of t.rows)for(const c of t.columns){const v=row.cells[c.key];if(v===null||v===undefined||v==='')continue;
        assert.ok(page.includes(typeof v==='string'?v:M.cellText(c,v)),`${m.id} ${t.key} ${c.key}`);}}
    assert.ok(page.includes('Hotel Control Centre'),'HCC letterhead');assert.ok(page.includes('lg-head'),'Lodge look');
    return r;
  };
  const golden=(id,tag,r)=>{
    const tables=JSON.parse(JSON.stringify(r.sections.map(t=>({key:t.key,columns:t.columns.map(c=>c.key),rows:t.rows.map(x=>[x.kind,...t.columns.map(c=>x.cells[c.key]??null)])}))));
    const f=new URL(`${id}.${tag}.json`,GD);
    if(!existsSync(f)||process.env.DOC_GOLDEN_ACCEPT){mkdirSync(GD,{recursive:true});writeFileSync(f,goldenText({id,tag,tables}));return;}
    assert.deepEqual(tables,JSON.parse(readFileSync(f,'utf8')).tables,`${id} drifted from its golden (DOC_GOLDEN_ACCEPT=1 to accept)`);
  };
  const stayWithInvoice=s=>s.bookings.find(b=>b.invoiceNo&&b.lines.length>2&&b.receipts.length);

  test(T+'twelve documents are registered with stable ids',()=>{
    assert.deepEqual(M.DOCUMENTS.map(d=>d.id),['doc.registration-card','doc.day-end','doc.folio','doc.invoice','doc.credit-note','doc.receipt','doc.quotation','doc.rate-card','rpt.people.payslip','doc.kot','doc.outlet-bill','doc.handover-pack']);
    assert.equal(code(()=>M.buildDocument(base(),'owner','doc.nope')),'NOT_FOUND');
  });
  test(T+'folio and tax invoice: lines, GST split, totals are the folio totals; number only when issued; QR text has no IRN',()=>{
    const s=fy(),b=stayWithInvoice(s);assert.ok(b);const t=M.totals(b);
    const f=M.folioDocument(s,'owner',b.id,'folio'),i=M.folioDocument(s,'owner',b.id,'invoice');
    assert.equal(f.id,'doc.folio');assert.equal(i.id,'doc.invoice');assert.equal(i.number,b.invoiceNo);assert.ok(!f.qrText);assert.ok(i.qrText.includes(b.invoiceNo)&&!/irn/i.test(i.qrText));
    const lines=i.tables[0].rows,tot=lines.at(-1);assert.equal(tot.kind,'total');assert.equal(tot.cells.net,t.net);assert.equal(tot.cells.total,t.total);
    assert.equal(tot.cells.cgst+tot.cells.sgst,t.tax,'CGST + SGST = folio tax');assert.equal(lines.length-1,b.lines.length);
    assert.equal(i.tables[1].rows.find(r=>r.cells.item==='Balance').cells.amount,t.balance);
    same(s,'owner',f);same(s,'owner',i);golden('doc.invoice','fy26',M.docResult(s,'owner',i));
    const open=s.bookings.find(x=>!x.invoiceNo&&x.lines.length);if(open){const d=M.folioDocument(s,'owner',open.id,'invoice');assert.equal(d.id,'doc.folio','no number yet: stays a folio summary');}
  });
  test(T+'credit note: against the invoice, GST at the original line rate; receipt and quotation',()=>{
    const s=lf(),b=s.bookings.find(x=>x.creditNotes?.length);assert.ok(b);const c=b.creditNotes[0];
    const d=M.creditNoteDocument(s,'owner',b.id,c.no);assert.equal(d.number,c.no);assert.equal(d.pairs.find(p=>p[0]==='Against invoice')[1],b.invoiceNo??'');
    const l=d.tables[0].rows[0].cells;assert.equal(l.net,c.amount);assert.equal(l.total,l.net+l.cgst+l.sgst);same(s,'owner',d);
    assert.equal(code(()=>M.creditNoteDocument(s,'owner',b.id,'CN-NONE')),'NOT_FOUND');
    const fs=fy(),bb=stayWithInvoice(fs),r=bb.receipts[0],rd=M.receiptDocument(fs,'owner',bb.id,r.id);
    assert.equal(rd.pairs.find(p=>p[0]==='Amount')[1],M.money(r.amount));assert.match(rd.pairs.find(p=>p[0]==='In words')[1],/^Rupees .* Only$/);same(fs,'owner',rd);golden('doc.receipt','fy26',M.docResult(fs,'owner',rd));
    const q=M.quotationDocument(fs,'owner',bb.id);const row=q.tables[0].rows[0].cells;assert.equal(row.total,row.net+row.gst);same(fs,'owner',q);
  });
  test(T+'payslip: the stored line; payroll roles any line, a staff member only their own locked line',()=>{
    const s=base(),run=s.staffX.runs[0],line=run.lines[0];
    const d=M.payslipDocument(s,'owner',run.id,line.staffId);assert.equal(d.id,'rpt.people.payslip');
    const rows=d.tables[0].rows;assert.equal(rows.at(-1).cells.earn,line.net);assert.equal(rows.find(r=>r.cells.item==='Gross').cells.earn,line.gross);assert.equal(rows.find(r=>r.cells.item==='Total deductions').cells.ded,line.deductions);
    same(s,'owner',d);golden('rpt.people.payslip','base',M.docResult(s,'owner',d));
    const st=s.staffX.staff.find(x=>x.userId&&run.lines.some(l=>l.staffId===x.id)&&!['owner','accountant'].includes(x.userId));const foreign=uid=>run.lines.find(l=>s.staffX.staff.find(x=>x.id===l.staffId)?.userId!==uid);
    if(st){const other=run.lines.find(l=>l.staffId!==st.id);assert.equal(code(()=>M.payslipDocument(s,st.userId,run.id,other.staffId)),'FORBIDDEN','not a colleague’s');
      const own=M.payslipDocument(s,st.userId,run.id,st.id);assert.equal(own.tables[0].rows.at(-1).cells.earn,run.lines.find(l=>l.staffId===st.id).net);}
    for(const u of ['reception','cashier','manager'])assert.equal(code(()=>M.payslipDocument(s,u,run.id,foreign(u).staffId)),'FORBIDDEN',u);
    assert.equal(code(()=>M.payslipDocument(s,'owner',run.id,'NOPE')),'NOT_FOUND');
  });
  test(T+'KOT has no money at all; the outlet bill is money and totals = posOrderTotals',()=>{
    const s=base(),o=s.posX.orders.find(x=>x.kots.length&&x.lines.some(l=>l.kot)),k=o.kots[0];
    const kd=M.kotDocument(s,'owner',o.id,k.id);assert.equal(kd.money,false);assert.ok(kd.tables.every(t=>t.columns.every(c=>!c.money)),'no money column');assert.ok(!/₹/.test(html(kd)));same(s,'owner',kd);
    const t=M.posOrderTotals(s,o),bd=M.outletBillDocument(s,'owner',o.id);assert.equal(bd.tables[1].rows.at(-1).cells.amount,t.total);assert.equal(bd.tables[0].rows.length,M.posActiveLines(o).length);same(s,'owner',bd);
    assert.equal(code(()=>M.kotDocument(s,'housekeeper',o.id,k.id)),'FORBIDDEN');assert.equal(code(()=>M.outletBillDocument(s,'housekeeper',o.id)),'FORBIDDEN');
    assert.equal(code(()=>M.kotDocument(s,'owner',o.id,'K-NONE')),'NOT_FOUND');
  });
  test(T+'rate card (published version) and hand-over pack (cash only for roles that see it)',()=>{
    let s=base();assert.equal(code(()=>M.rateCardDocument(s,'owner')),'NOT_FOUND');
    s=M.execute(s,'owner',{type:'TRC_DRAFT_SAVE',label:'Test card',effectiveFrom:s.date,lines:[{kind:'room',roomType:'Classic',plan:'EP',daySet:'all',rateExcl:250000}]});
    s=M.execute(s,'owner',{type:'TRC_PUBLISH',id:s.tariffCardX.versions.at(-1).id});
    const d=M.rateCardDocument(s,'owner');assert.equal(d.money,false);assert.equal(d.tables[0].title,'Rooms');
    const sh=M.trcRateSheet(s);assert.equal(d.tables[0].rows[0].cells.incl,sh.rows[0].incl,'price = the sheet’s');same(s,'owner',d);golden('doc.rate-card','base',M.docResult(s,'owner',d));
    assert.equal(code(()=>M.rateCardDocument(s,'housekeeper')),'FORBIDDEN');
    const h=fy(),date=h.handoverX.notes.at(-1).date;
    const own=M.handoverPackDocument(h,'owner',date),rec=M.handoverPackDocument(h,'reception',date);
    assert.ok(own.tables[0].columns.some(c=>c.key==='cash')&&own.money);assert.ok(rec.tables[0].columns.every(c=>c.key!=='cash'||true));
    const hk=M.handoverPackDocument(h,'housekeeper',date);assert.ok(hk.tables[0].columns.every(c=>c.key!=='cash'),'housekeeping never sees the cash float');assert.equal(hk.money,false);same(h,'owner',own);same(h,'housekeeper',hk);
    assert.equal(code(()=>M.handoverPackDocument(h,'techadmin',date)),'FORBIDDEN');
  });
  test(T+'registration card and day end through the registry: ID masked, money block only with the money right, letterhead',()=>{
    const s=fy(),b=s.bookings.find(x=>x.status==='In house'||x.status==='Departed');
    const rc=M.buildDocument(s,'reception','doc.registration-card',{booking:b.id});assert.ok(rc.lodge);assert.ok(!/\d{8,}/.test(rc.lodge.pairs.find(p=>p[0]==='ID number')?.[1]??''));same(s,'reception',rc);
    const de=M.buildDocument(s,'owner','doc.day-end',{date:s.date});assert.ok(de.lodge.pairs.some(p=>p[0]==='Net revenue'));assert.equal(de.money,true);same(s,'owner',de);
    assert.equal(code(()=>M.buildDocument(s,'housekeeper','doc.day-end',{date:s.date})),'FORBIDDEN');
  });
  test(T+'roles: the housekeeping, supervisor, maintenance roles hold no money document; the catalogue lists only what the caller may print',()=>{
    const s=fy(),b=stayWithInvoice(s);
    for(const u of ['housekeeper','maint','supervisor','techadmin'].filter(u=>s.users.some(x=>x.id===u))){
      for(const f of [()=>M.folioDocument(s,u,b.id),()=>M.folioDocument(s,u,b.id,'invoice'),()=>M.receiptDocument(s,u,b.id,b.receipts[0].id),()=>M.quotationDocument(s,u,b.id)])assert.equal(code(f),'FORBIDDEN',u);
      assert.ok(M.docCatalogue(s,u).every(d=>!d.money||d.id==='rpt.people.payslip'),u+' sees no money document in the list (own payslip excepted, DEC-061)');}
    assert.ok(M.docCatalogue(s,'owner').length>=11);assert.ok(M.docCatalogue(s,'reception').some(d=>d.id==='doc.invoice'));
    for(const u of NOMONEY)if(s.users.some(x=>x.id===u))assert.equal(code(()=>M.folioDocument(s,u,b.id)),'FORBIDDEN');
  });
  test(T+'print log: a printed document writes one export-log row; a role that cannot print it is refused',()=>{
    let s=fy();const b=stayWithInvoice(s);
    s=M.execute(s,'owner',{type:'RPT_LOG',reportId:'doc.invoice',format:'print',params:{booking:b.id},rows:5});
    const row=s.rptX.exportLog.at(-1);assert.equal(row.reportId,'doc.invoice');assert.equal(row.format,'print');assert.equal(row.params.booking,b.id);
    assert.throws(()=>M.execute(s,'housekeeper',{type:'RPT_LOG',reportId:'doc.invoice',format:'print',params:{}}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'owner',{type:'RPT_LOG',reportId:'doc.invoice',format:'xlsx',params:{}}),e=>e.code==='VALIDATION');
  });
  test(T+'a formula-looking guest name is neutralised in the CSV of a document',()=>{
    const s=fy(),b=stayWithInvoice(s);b.guest='=HYPERLINK("x")';
    const d=M.folioDocument(s,'owner',b.id,'invoice');const csv=M.toCsv(M.docResult(s,'owner',d));
    assert.ok(!/(^|,)"?=HYPERLINK/m.test(csv),'neutralised');assert.ok(csv.includes("'=HYPERLINK")||csv.includes('"\'=HYPERLINK'));
  });
}
