// DEC-072 (audit A03a-01) · walk-in / paid-at-outlet restaurant bills are GST tax invoices in their own series RST/<FY>/<7 digits>,
// reversed by a credit note when voided, and appear in the GSTR-1 working summary — the mock does what the server does
// (08 Production PosInvoiceSource / InvoiceService / Gstr1Summary). Names prefixed [POS-GST]. Figures worked by hand in the comments
// (restaurant 5 % from the GST master; CGST = SGST = half, odd paisa to CGST).
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';

export async function register({test,M,assert}){
  const {execute,seed}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof M.HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const P=s=>M.posSlice(s);
  const ord=(s,id)=>P(s).orders.find(o=>o.id===id);
  /** A fresh restaurant check with the items sent to the kitchen. */
  const table=(s,tbl,items,u='cashier')=>{
    s=execute(s,u,{type:'POS_ORDER_OPEN',outlet:'restaurant',table:tbl,covers:2,guest:'Walk-in · Mr. Patil'});const id=P(s).orders.at(-1).id;
    for(const [item,qty] of items)s=execute(s,u,{type:'POS_ITEM_ADD',id,item,qty});
    return [execute(s,u,{type:'POS_KOT_SEND',id}),id];
  };
  const upi=(s,id,extra={})=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'UPI',reference:'DEMO-UPI-'+id,...extra});
  const onDate=(s,date)=>{const c=structuredClone(s);c.date=date;c.clock=date+'T12:00';return c;};

  test('[POS-GST] numbering: RST/<FY>/<7 digits> is 16 characters, restarts each 1 April, overflows with a refusal (never wraps)',()=>{
    assert.deepEqual([M.posFyCode('2026-04-01'),M.posFyCode('2027-03-31'),M.posFyCode('2027-04-01'),M.posFyCode('2026-03-31')],['2627','2627','2728','2526']);
    assert.equal(M.posRstNo('2627',1),'RST/2627/0000001');assert.equal(M.posRstNo('2627',9999999),'RST/2627/9999999');
    assert.ok([M.posRstNo('2627',1),M.posRstNo('2728',42),M.posRstNo('2627',9999999)].every(n=>n.length===16));
    assert.throws(()=>M.posRstNo('2627',10000000),/is full/);assert.throws(()=>M.posRstNo('2627',0),/is full/);
  });

  test('[POS-GST] a walk-in outlet bill is RST/2627/0000001 with the bill’s own CGST/SGST and SAC 996331; the next bill …0002; a replay numbers nothing',()=>{
    // Dal ₹280 + 2 roti ₹80 = ₹360 net; 5 % = ₹18.00 → CGST 900 + SGST 900 paise; total ₹378.
    let [s,id]=table(seed(),'T3',[['m-dal',1],['m-roti',2]]);
    s=upi(s,id,{key:'settle-a'});
    const st=ord(s,id).settlement;
    assert.deepEqual([st.invoiceNo,st.supply,st.net,st.tax,st.cgst,st.sgst,st.total,st.sac],['RST/2627/0000001','B2C',36000,1800,900,900,37800,'996331']);
    assert.equal(st.invoiceNo.length,16);
    assert.deepEqual(upi(s,id,{key:'settle-a'}),s,'same key = the stored result, no second number');
    // Paneer ₹380 + 3 naan ₹210 = ₹590 net; 5 % = 2,950 paise → SGST 1,475 (half toward zero), CGST 1,475; total ₹619.50.
    let id2;[s,id2]=table(s,'T4',[['m-paneer',1],['m-naan',3]]);s=upi(s,id2);
    const b=ord(s,id2).settlement;
    assert.deepEqual([b.invoiceNo,b.tax,b.cgst,b.sgst,b.total],['RST/2627/0000002',2950,1475,1475,61950]);
    assert.deepEqual(P(s).rstSeq,{'2627':2});
    assert.equal(M.posBillSplit(b).cgst+M.posBillSplit(b).sgst,b.tax);
    // Bills settled before the series (fixture POS-F01 / F02) keep no number until someone issues one.
    assert.equal(ord(s,'POS-F01').settlement.invoiceNo,undefined);
  });

  test('[POS-GST] per FY: 31 March 2027 is still RST/2627, 1 April 2027 starts RST/2728/0000001; a refused settle consumes no number',()=>{
    let s=seed(),a,b,c;
    [s,a]=table(s,'T3',[['m-tea',1]]);s=upi(s,a);
    s=onDate(s,'2027-03-31');[s,b]=table(s,'T4',[['m-tea',1]]);
    assert.equal(code(()=>upi(s,b,{buyerGstin:'27AAAAA0000A1Z5',buyerName:'X (fictional)'})),'VALIDATION'); // wrong check character
    assert.deepEqual(P(s).rstSeq,{'2627':1});assert.equal(ord(s,b).status,'Open');
    s=upi(s,b);
    s=onDate(s,'2027-04-01');[s,c]=table(s,'T5',[['m-tea',1]]);s=upi(s,c);
    assert.deepEqual([ord(s,a).settlement.invoiceNo,ord(s,b).settlement.invoiceNo,ord(s,c).settlement.invoiceNo],['RST/2627/0000001','RST/2627/0000002','RST/2728/0000001']);
    assert.deepEqual(P(s).rstSeq,{'2627':2,'2728':1});
  });

  test('[POS-GST] B2B needs a checksum-valid GSTIN and the legal name; ₹50,000 or more to a walk-in needs name and address; a room-folio bill takes no buyer and gets no RST',()=>{
    let s=seed(),id,big,room;
    [s,id]=table(s,'T3',[['m-thali',2]]);
    assert.throws(()=>upi(s,id,{buyerGstin:'27AAAAA0000A1Z5',buyerName:'X'}),/check character is wrong/);
    assert.throws(()=>upi(s,id,{buyerGstin:'29AAGCB7383J1Z4'}),/legal name/);
    // 2 thali ₹840; 5 % = ₹42 → 2,100 + 2,100. The GSTIN is trimmed and upper-cased.
    s=upi(s,id,{buyerGstin:' 29aagcb7383j1z4 ',buyerName:'Demo Industries Pvt Ltd (fictional)'});
    const st=ord(s,id).settlement;
    assert.deepEqual([st.invoiceNo,st.supply,st.buyer,st.cgst,st.sgst],['RST/2627/0000001','B2B',{name:'Demo Industries Pvt Ltd (fictional)',gstin:'29AAGCB7383J1Z4'},2100,2100]);
    // 50 thali ₹21,000 + 50 paneer ₹19,000 + 50 dal ₹14,000 = ₹54,000 net; 5 % = ₹2,700; total ₹56,700 ≥ ₹50,000.
    [s,big]=table(s,'T5',[['m-thali',50],['m-paneer',50],['m-dal',50]]);
    assert.throws(()=>upi(s,big),/₹50,000 or more/);
    s=upi(s,big,{buyerName:'Mr. Patil (fictional)',buyerAddress:'4 Ganj Golai, Latur (fictional)'});
    assert.deepEqual([ord(s,big).settlement.invoiceNo,ord(s,big).settlement.supply,ord(s,big).settlement.total],['RST/2627/0000002','B2C',5670000]);
    // Room service charged to the in-house guest of room 102: no restaurant invoice, buyer refused.
    s=execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'room-service',room:'102'});room=P(s).orders.at(-1).id;
    s=execute(s,'reception',{type:'POS_ITEM_ADD',id:room,item:'m-tea'});s=execute(s,'reception',{type:'POS_KOT_SEND',id:room});
    assert.throws(()=>execute(s,'reception',{type:'POS_SETTLE',id:room,mode:'folio',buyerGstin:'29AAGCB7383J1Z4',buyerName:'X'}),/room invoice/);
    s=execute(s,'reception',{type:'POS_SETTLE',id:room,mode:'folio'});
    assert.equal(ord(s,room).settlement.invoiceNo,undefined);assert.deepEqual(P(s).rstSeq,{'2627':2});
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_INVOICE_ISSUE',id:room})),'INVALID_TRANSITION');
  });

  test('[POS-GST] voiding an invoiced bill issues a credit note in the CN series (direct within the limit, after the approval above it); the invoice never changes',()=>{
    let s=seed(),small,large;
    [s,small]=table(s,'T3',[['m-tea',2]]);s=upi(s,small);                  // ₹120 + ₹6 = ₹126
    [s,large]=table(s,'T4',[['m-paneer',1],['m-naan',3]]);s=upi(s,large);  // ₹619.50 > ₹500
    s=execute(s,'cashier',{type:'POS_VOID_BILL',id:small,reason:'Wrong table billed'});
    const cn1=M.folioCreditNoteNo((seed().creditNoteSeqC??0)+1);
    assert.deepEqual([ord(s,small).status,ord(s,small).settlement.invoiceNo,ord(s,small).settlement.creditNoteNo,ord(s,small).settlement.creditNoteDate],['Void','RST/2627/0000001',cn1,s.date]);
    s=execute(s,'cashier',{type:'POS_VOID_BILL',id:large,reason:'Duplicate bill'});
    assert.equal(ord(s,large).settlement.creditNoteNo,undefined,'nothing before the approval');
    s=execute(s,'manager',{type:'POS_VOID_DECIDE',id:s.approvals.at(-1).id,approve:true});
    assert.equal(ord(s,large).settlement.creditNoteNo,M.folioCreditNoteNo((seed().creditNoteSeqC??0)+2));
    assert.deepEqual([ord(s,large).settlement.total,ord(s,large).settlement.cgst],[61950,1475],'the invoice figures stay as issued');
    // A bill settled before the series (fixture POS-F02, UPI ₹714) is voided without a credit note: it had no tax invoice.
    s=execute(s,'cashier',{type:'POS_VOID_BILL',id:'POS-F02',reason:'Charged twice'});s=execute(s,'manager',{type:'POS_VOID_DECIDE',id:s.approvals.at(-1).id,approve:true});
    assert.equal(ord(s,'POS-F02').settlement.creditNoteNo,undefined);
  });

  test('[POS-GST] POS_INVOICE_ISSUE numbers an outlet bill that has none (once), refuses voided and folio bills, payments permission only',()=>{
    let s=seed();
    s=execute(s,'cashier',{type:'POS_INVOICE_ISSUE',id:'POS-F01',key:'inv-f01'});
    assert.equal(ord(s,'POS-F01').settlement.invoiceNo,'RST/2627/0000001');
    const again=execute(s,'reception',{type:'POS_INVOICE_ISSUE',id:'POS-F01'});
    assert.equal(ord(again,'POS-F01').settlement.invoiceNo,'RST/2627/0000001');assert.deepEqual(P(again).rstSeq,{'2627':1});
    assert.equal(code(()=>execute(seed(),'housekeeper',{type:'POS_INVOICE_ISSUE',id:'POS-F01'})),'FORBIDDEN');
    assert.equal(code(()=>execute(seed(),'cashier',{type:'POS_INVOICE_ISSUE',id:'POS-F03'})),'INVALID_TRANSITION'); // still open
  });

  test('[POS-GST] GSTR-1 working summary: RST in B2CS / B2B like INV, the void credit note nets B2CS, documents table lists INV, RST (cancelled) and CRN',()=>{
    let s=seed(),a,b,c;
    // Room 105 stay 24 → 26 Oct at ₹2,000: sample invoice DEMO/2026-27/0001 dated its departure (26 Oct): 4,00,000 at 5 % = 20,000 (10,000 + 10,000).
    s=execute(s,'owner',{type:'BOOK',guest:'GSTR Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2});const bk=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id:bk});s=execute(s,'owner',{type:'CHECKIN',id:bk});s=execute(s,'owner',{type:'POST_NIGHTS',id:bk});
    s=execute(s,'cashier',{type:'PAYMENT',id:bk,amount:420000,method:'UPI',kind:'payment',reference:'DEMO-UPI-GSTR'});s=execute(s,'owner',{type:'CHECKOUT',id:bk});
    [s,a]=table(s,'T3',[['m-dal',1],['m-roti',2]]);s=upi(s,a);                                  // RST 1 · B2C ₹360 / 900 + 900
    [s,b]=table(s,'T4',[['m-thali',2]]);s=upi(s,b,{buyerGstin:'29AAGCB7383J1Z4',buyerName:'Demo Industries Pvt Ltd (fictional)'}); // RST 2 · B2B ₹840 / 2,100 + 2,100
    [s,c]=table(s,'T5',[['m-tea',2]]);s=upi(s,c);s=execute(s,'cashier',{type:'POS_VOID_BILL',id:c,reason:'Wrong table'}); // RST 3 · ₹120 / 300 + 300, voided → CN 1
    const g=M.gstr1Summary(s,'2026-10-01','2026-10-31');
    // B2B 5 %: 84,000 / 2,100. B2CS 5 %: 4,00,000 + 36,000 + 12,000 − 12,000 = 4,36,000; CGST 10,000 + 900 + 300 − 300 = 10,900; documents INV 1, RST 1, RST 3, CN 1.
    assert.deepEqual(g.rates.map(r=>[r.section,r.bps,r.taxable,r.cgst,r.sgst,r.documents]),[['B2B',500,84000,2100,2100,1],['B2CS',500,436000,10900,10900,4]]);
    // SAC: rooms 996311 4,00,000; restaurant 996331 36,000 + 84,000 + 12,000 − 12,000 = 1,20,000.
    assert.deepEqual(g.sac.map(r=>[r.sac,r.bps,r.taxable]),[['996311',500,400000],['996331',500,120000]]);
    assert.deepEqual(g.documents.map(d=>[d.docType,d.first,d.last,d.count,d.cancelled]),[
      ['INV','DEMO/2026-27/0001','DEMO/2026-27/0001',1,0],['RST','RST/2627/0000001','RST/2627/0000003',3,1],['CRN','DEMO-CN/2026-27/0001','DEMO-CN/2026-27/0001',1,0]]);
    assert.equal(g.note,M.GSTR1_NOTE);
    // Another month has nothing; the picker lists this FY's months up to the business date, newest first.
    assert.deepEqual(M.gstr1Summary(s,'2026-09-01','2026-09-30').documents,[]);
    assert.deepEqual(M.gstr1Months('2026-10-24').map(m=>m.id),['2026-10','2026-09','2026-08','2026-07','2026-06','2026-05','2026-04']);
    assert.deepEqual(M.gstr1Months('2027-03-31')[0],{id:'2027-03',label:'Mar 2027',from:'2027-03-01',to:'2027-03-31'});
  });

  // ── Render: bill print (tax invoice particulars), settle dialog (buyer fields), GSTR-1 dialog desktop + phone sheet (prototype build) ──
  await writeFile('tests/.generated/posgst-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {registration as pos} from '../../src/features/pos/register';
import {registration as cmp} from '../../src/features/compliance/register';
function ctx(s:any,uid:string,mobile:boolean){const user=s.users.find((u:any)=>u.id===uid);
  return {s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any;}
export function renderModal(s:any,uid:string,type:string,id?:string,room?:string,mobile=false){
  const reg:any=type.startsWith('pos-')?pos:cmp;const C=(mobile&&reg.mobile.modals[type])||reg.modals[type];
  return renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}><C id={id} room={room}/></HccContext.Provider>);}
`);
  await build({entryPoints:['tests/.generated/posgst-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/posgst-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __pgReq} from 'node:module';const require=__pgReq(import.meta.url);"}});
  const UI=await import('./.generated/posgst-ui.mjs');

  test('[POS-GST] the bill print shows the RST number, hotel GSTIN, SAC 996331 and the CGST / SGST split; a voided bill names its credit note; an older bill offers “Issue tax invoice”',()=>{
    let s=seed(),id,v;
    s=execute(s,'owner',{type:'SETTING_SET',setting:'brand.gstin',value:'27AAAAA0000A1Z2',effectiveFrom:s.date,reason:'Registered'});
    [s,id]=table(s,'T3',[['m-dal',1],['m-roti',2]]);s=upi(s,id,{buyerGstin:'29AAGCB7383J1Z4',buyerName:'Demo Industries Pvt Ltd (fictional)'});
    const html=UI.renderModal(s,'cashier','pos-print',id,'bill');
    for(const t of ['TAX INVOICE · B2B','RST/2627/0000001','Hotel GSTIN 27AAAAA0000A1Z2','SAC 996331','CGST @2.5%','SGST @2.5%',M.money(900),'Buyer GSTIN 29AAGCB7383J1Z4','Maharashtra (27)'])
      assert.ok(html.includes(t),'bill print lacks: '+t);
    assert.ok(!html.includes('not a tax invoice'),'an issued invoice is not called a pro-forma');
    [s,v]=table(s,'T4',[['m-tea',1]]);s=upi(s,v);s=execute(s,'cashier',{type:'POS_VOID_BILL',id:v,reason:'Wrong table'});
    assert.match(UI.renderModal(s,'cashier','pos-print',v,'bill'),/reversed in full by credit note DEMO-CN\/2026-27\/0001/);
    const older=UI.renderModal(s,'cashier','pos-print','POS-F01','bill');
    assert.ok(older.includes('Issue tax invoice')&&older.includes('not a tax invoice'));
    assert.ok(!UI.renderModal(s,'housekeeper','pos-print','POS-F01','bill').includes('Issue tax invoice'));
    // Phone sheet uses the same dialog.
    assert.ok(UI.renderModal(s,'cashier','pos-print',id,'bill',undefined,true).includes('RST/2627/0000001'));
  });
  test('[POS-GST] the settle dialog offers the buyer GSTIN / name / address for an outlet bill and names the RST series, desktop and phone',()=>{
    let [s,id]=table(seed(),'T3',[['m-dal',1]]);
    for(const mobile of [false,true]){const html=UI.renderModal(s,'cashier','pos-settle',id,undefined,mobile);
      for(const t of ['Buyer GSTIN for a B2B invoice','Buyer name','Buyer address','RST/2627/','SAC 996331'])assert.ok(html.includes(t),(mobile?'phone':'desktop')+' settle lacks: '+t);}
  });
  test('[POS-GST] Compliance › GSTR-1 working summary renders on desktop (tables) and phone (cards) for Owner and Accountant only',()=>{
    let [s,id]=table(seed(),'T3',[['m-dal',1],['m-roti',2]]);s=upi(s,id);
    const desk=UI.renderModal(s,'accountant','cmp-gstr1'),phone=UI.renderModal(s,'owner','cmp-gstr1',undefined,undefined,true);
    for(const t of ['GSTR-1 working summary','B2CS',M.money(36000),'996331','Tax invoices · restaurant (RST)','RST/2627/0000001'])assert.ok(desk.includes(t),'desktop lacks: '+t);
    assert.ok(desk.includes('<table'));assert.ok(!phone.includes('<table'),'phone uses cards, not tables');
    for(const t of ['B2CS · 5%',M.money(36000),'RST/2627/0000001'])assert.ok(phone.includes(t),'phone lacks: '+t);
    assert.match(UI.renderModal(s,'cashier','cmp-gstr1'),/Owner and Accountant only/);
    assert.deepEqual(Object.keys(M.GSTR1_SAC).sort(),['banquet','food','laundry','other','room']);
  });
}
