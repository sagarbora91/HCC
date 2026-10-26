// DEC-072 (audit A03a-01) production client: the server's RST tax invoice on the POS bill (number, B2C / B2B, buyer, hotel GSTIN, SAC,
// CGST / SGST, the credit note of a voided bill), the buyer sent with SettleCheck, POS_INVOICE_ISSUE → IssueInvoice {pos, bill id}, and the
// GSTR-1 working summary read (GET /invoices/gstr1). Fake routes shaped like PosContracts.cs / InvoiceContracts.cs. Names prefixed [POS-GST sync].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {loadSync,loadPages,fakeServer,startRuntime} from './_sync-harness.mjs';

export async function register({test,assert}){
  const S=await loadSync();
  const T0='2026-10-24T05:00:00.000Z',BPS=500;
  const line=(id,name,price,qty)=>({lineId:id,itemId:id,name,pricePaise:price,qty,kotId:1,status:'Served',voided:false,voidReason:null});
  const kots=[{kotId:1,kotNo:1,status:'Served',createdUtc:T0,readyUtc:T0,servedUtc:T0,createdBy:4}];
  const invoice=(over={})=>({invoiceId:501,invoiceNo:'RST/2627/0000001',docType:'RST',invoiceDate:'2026-10-24',supplyType:'B2C',buyerName:'Walk-in · Mr. Patil',
    buyerGstin:null,supplierGstin:'27AAAAA0000A1Z2',sac:'996331',cgstPaise:900,sgstPaise:900,creditNoteNo:null,...over});
  const bill=(id,net,over={})=>{const tax=Math.round(net*BPS/10000);return {billId:id,billNo:'POS/2610/00'+id,mode:'outlet',tender:'UPI',reference:'DEMO-UPI-'+id,netPaise:net,
    taxBps:BPS,taxPaise:tax,totalPaise:net+tax,businessDate:'2026-10-24',status:'Settled',settledUtc:T0,settledBy:4,reservationId:null,...over};};
  const check=(id,table,lines,b,over={})=>{const net=lines.reduce((a,l)=>a+l.pricePaise*l.qty,0),tax=Math.round(net*BPS/10000);
    return {orderId:id,outletId:1,outlet:'Restaurant',spot:'Table '+table,guest:'Walk-in · Mr. Patil',covers:2,businessDate:'2026-10-24',status:b?'Settled':'Open',version:'CV'+id,
      pendingVoidId:null,lines,kots,netPaise:net,taxBps:BPS,taxPaise:tax,totalPaise:net+tax,items:lines.reduce((a,l)=>a+l.qty,0),bill:b,openedUtc:T0,openedBy:4,
      voidReason:null,voidedUtc:null,reservationId:null,...over};};
  const checks=new Map([
    // Dal ₹280 + 2 roti ₹80 = ₹360; 5 % = ₹18 (900 + 900): RST/2627/0000001, B2C to the walk-in.
    [901,check(901,'T3',[line(1,'Dal tadka',28000,1),line(2,'Tandoori roti',4000,2)],bill(41,36000,{invoice:invoice()}))],
    // 2 thali ₹840; 5 % = ₹42 (2,100 + 2,100): RST/2627/0000002, B2B.
    [902,check(902,'T4',[line(3,'Maharashtrian veg thali',42000,2)],bill(42,84000,{invoice:invoice({invoiceId:502,invoiceNo:'RST/2627/0000002',supplyType:'B2B',
      buyerName:'Demo Industries Pvt Ltd (fictional)',buyerGstin:'29AAGCB7383J1Z4',cgstPaise:2100,sgstPaise:2100})}))],
    // Voided the same day: the invoice stays, the credit note reverses it.
    [903,check(903,'T5',[{...line(4,'Masala chai',6000,2),voided:true,voidReason:'Wrong table'}],bill(43,12000,{status:'Void',invoice:invoice({invoiceId:503,invoiceNo:'RST/2627/0000003',
      cgstPaise:300,sgstPaise:300,creditNoteNo:'CN/2627/00000001'})}),{status:'Void',voidReason:'Wrong table',voidedUtc:T0})],
    // Settled while the hotel GSTIN was not recorded: no invoice yet.
    [904,check(904,'T6',[line(5,'Filter coffee',8000,1)],bill(44,8000))],
    // Open check to settle with a buyer.
    [905,check(905,'T7',[line(6,'Maharashtrian veg thali',42000,1)],null)],
  ]);
  const srv=fakeServer({
    'GET /pos/outlets':()=>[{outletId:1,code:'REST',name:'Restaurant',kind:'table',tables:['T3','T4','T5','T6','T7']}],
    'GET /pos/menu':()=>[],
    'GET /pos/checks':()=>[...checks.values()].filter(c=>c.status==='Open').map(c=>({orderId:c.orderId})),
    'GET /pos/checks/closed':()=>[...checks.values()].filter(c=>c.status!=='Open'),
    'GET /pos/checks/:id':({params})=>checks.get(Number(params.id)),
    'GET /pos/void-requests':()=>[],
    'GET /pos/till':()=>({tillId:3,code:'FD',shiftId:12,shiftOpen:true}),
    'GET /pos/in-house':()=>[],
    'POST /commands/SettleCheck':({body})=>{const c=checks.get(body.orderId);c.status='Settled';
      c.bill=bill(45,c.netPaise,{invoice:invoice({invoiceId:505,invoiceNo:'RST/2627/0000004',supplyType:body.buyerGstin?'B2B':'B2C',buyerName:body.buyerName??null,
        buyerGstin:body.buyerGstin?.toUpperCase()??null,cgstPaise:1050,sgstPaise:1050})});
      return {orderId:c.orderId,billId:45,billNo:c.bill.billNo,mode:'outlet',invoiceId:505,invoiceNo:'RST/2627/0000004',invoiceNote:null};},
    'POST /commands/IssueInvoice':({body})=>{const c=[...checks.values()].find(x=>x.bill?.billId===body.sourceId);
      c.bill.invoice=invoice({invoiceId:506,invoiceNo:'RST/2627/0000005',cgstPaise:200,sgstPaise:200});
      return {invoiceId:506,invoiceNo:'RST/2627/0000005',docType:'RST',invoiceDate:'2026-10-24',totalPaise:8400,created:true};},
    'GET /invoices/gstr1':({query})=>({from:query.from,to:query.to,
      rates:[{section:'B2B',taxBps:500,taxablePaise:84000,cgstPaise:2100,sgstPaise:2100,igstPaise:0,documents:1},
        {section:'B2CS',taxBps:500,taxablePaise:36000,cgstPaise:900,sgstPaise:900,igstPaise:0,documents:3}],
      sac:[{sac:'996331',taxBps:500,taxablePaise:120000,cgstPaise:3000,sgstPaise:3000,igstPaise:0}],
      documents:[{docType:'RST',first:'RST/2627/0000001',last:'RST/2627/0000003',count:3,cancelled:1},{docType:'CRN',first:'CN/2627/00000001',last:'CN/2627/00000001',count:1,cancelled:0}],
      note:'Working summary from issued documents — illustrative, CA to confirm before filing (thresholds, SAC codes, sections).'}),
  });
  const rt=await startRuntime(S,srv,{areas:S.AREAS.filter(a=>a.id!=='channels')});
  const st0=rt.get().state,o=id=>rt.get().state.posX.orders.find(x=>x.id===String(id));
  const P=await loadPages();
  const pages=[P.renderPages(st0,'1',false),P.renderPages(st0,'1',true)];

  test('[POS-GST sync] the check’s bill.invoice becomes the bill’s RST number, B2C / B2B, buyer, hotel GSTIN, SAC and CGST / SGST; a voided bill carries its credit note',()=>{
    const a=o(901).settlement,b=o(902).settlement,v=o(903).settlement,n=o(904).settlement;
    assert.deepEqual([a.invoiceNo,a.supply,a.buyer,a.hotelGstin,a.sac,a.cgst,a.sgst,a.billId],['RST/2627/0000001','B2C',{name:'Walk-in · Mr. Patil'},'27AAAAA0000A1Z2','996331',900,900,'41']);
    assert.deepEqual([b.invoiceNo,b.supply,b.buyer],['RST/2627/0000002','B2B',{name:'Demo Industries Pvt Ltd (fictional)',gstin:'29AAGCB7383J1Z4'}]);
    assert.deepEqual([v.invoiceNo,v.creditNoteNo,v.creditNoteDate,o(903).status],['RST/2627/0000003','CN/2627/00000001','2026-10-24','Void']);
    assert.deepEqual([n.invoiceNo,n.billId],[undefined,'44']);
    // The mapper is the same for a single check (exported for tests).
    assert.equal(S.orderFromServer(checks.get(902)).settlement.invoiceNo,'RST/2627/0000002');
  });
  test('[POS-GST sync] every desktop and phone page renders on the invoiced POS data',()=>{assert.deepEqual(pages.map(x=>x.errors),[{},{}]);});

  srv.reset();
  const badGstin=await rt.run({type:'POS_SETTLE',id:'905',mode:'outlet',method:'UPI',reference:'DEMO-UPI-905',buyerGstin:'27AAAAA0000A1Z5',buyerName:'X'});
  const noName=await rt.run({type:'POS_SETTLE',id:'905',mode:'outlet',method:'UPI',reference:'DEMO-UPI-905',buyerGstin:'29AAGCB7383J1Z4'});
  const folioBuyer=await rt.run({type:'POS_SETTLE',id:'905',mode:'folio',booking:'1201',buyerGstin:'29AAGCB7383J1Z4',buyerName:'X'});
  const refusedCalls=srv.calls.length;
  const settled=await rt.run({type:'POS_SETTLE',id:'905',mode:'outlet',method:'UPI',reference:'DEMO-UPI-905',buyerGstin:' 29AAGCB7383J1Z4 ',
    buyerName:' Demo Industries Pvt Ltd (fictional) ',buyerAddress:'MIDC, Latur (fictional)',key:'settle-905'});
  const settleCall=srv.commands('SettleCheck')[0];
  test('[POS-GST sync] POS_SETTLE sends the buyer GSTIN / name / address with SettleCheck; a bad GSTIN, a B2B without a name and a folio bill with a buyer are refused before any request',()=>{
    assert.deepEqual([badGstin.ok,badGstin.reason],[false,'precheck']);assert.match(badGstin.text,/check character is wrong/);
    assert.deepEqual([noName.ok,noName.reason],[false,'precheck']);assert.match(noName.text,/legal name/);
    assert.deepEqual([folioBuyer.ok,folioBuyer.reason],[false,'precheck']);assert.match(folioBuyer.text,/room invoice/);
    assert.equal(refusedCalls,0);
    assert.ok(settled.ok,settled.text);
    assert.deepEqual(settleCall.body,{orderId:905,mode:'outlet',tender:'UPI',reference:'DEMO-UPI-905',buyerGstin:'29AAGCB7383J1Z4',buyerName:'Demo Industries Pvt Ltd (fictional)',
      buyerAddress:'MIDC, Latur (fictional)',expectedVersion:'CV905'});
    assert.equal(settleCall.key,'settle-905');
    assert.deepEqual([o(905).settlement.invoiceNo,o(905).settlement.supply],['RST/2627/0000004','B2B'],'the server’s number shows after the reload');
  });

  srv.reset();
  const folioRefused=await rt.run({type:'POS_INVOICE_ISSUE',id:'999'});
  const issued=await rt.run({type:'POS_INVOICE_ISSUE',id:'904',key:'pos-invoice:904'});
  const issueCall=srv.commands('IssueInvoice')[0];
  test('[POS-GST sync] POS_INVOICE_ISSUE → IssueInvoice {sourceKind: pos, sourceId: the bill id} and the check reloads with the number',()=>{
    assert.deepEqual([folioRefused.ok,folioRefused.reason],[false,'precheck']);
    assert.ok(issued.ok,issued.text);assert.deepEqual(issueCall.body,{sourceKind:'pos',sourceId:44});assert.equal(issueCall.key,'pos-invoice:904');
    assert.ok(srv.gets().some(c=>c.path==='/pos/checks/closed'),'posX reloads');
    assert.equal(o(904).settlement.invoiceNo,'RST/2627/0000005');
    assert.ok(rt.mapped('POS_INVOICE_ISSUE'));assert.deepEqual(S.validateAreas(S.AREAS),[]);
  });

  srv.reset();
  const g=await S.loadServerGstr1(rt.api,'2026-10-01','2026-10-31');
  test('[POS-GST sync] the GSTR-1 working summary is the server’s (GET /invoices/gstr1?from&to) in the prototype shape: B2B / B2CS rates, SAC, RST row with cancelled',()=>{
    assert.deepEqual(srv.gets('/invoices/gstr1').map(c=>c.query),[{from:'2026-10-01',to:'2026-10-31'}]);
    assert.deepEqual(g.rates.map(r=>[r.section,r.bps,r.taxable,r.cgst,r.sgst,r.igst,r.documents]),[['B2B',500,84000,2100,2100,0,1],['B2CS',500,36000,900,900,0,3]]);
    assert.deepEqual(g.sac,[{sac:'996331',bps:500,taxable:120000,cgst:3000,sgst:3000,igst:0}]);
    assert.deepEqual(g.documents.map(d=>[d.docType,d.count,d.cancelled]),[['RST',3,1],['CRN',1,0]]);
    assert.deepEqual(S.gstr1FromServer({from:'a',to:'b',rates:[],sac:[],documents:[{docType:'INV',first:'x',last:'y',count:2}],note:''}).documents[0].cancelled,0);
  });

  // ── Production render of the bill print: the server's number and particulars, and the issue action for a bill without one ──
  await writeFile('tests/.generated/posgst-prod-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {registration} from '../../src/features/pos/register';
export function renderBill(s:any,uid:string,id:string){const C=(registration.modals as any)['pos-print'];const user=s.users.find((u:any)=>u.id===uid);
  return renderToStaticMarkup(<HccContext.Provider value={{s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:false}} as any}><C id={id} room="bill"/></HccContext.Provider>);}
`);
  await build({entryPoints:['tests/.generated/posgst-prod-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/posgst-prod.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"production"'},banner:{js:"import {createRequire as __ppReq} from 'node:module';const require=__ppReq(import.meta.url);"}});
  const UI=await import('./.generated/posgst-prod.mjs');
  test('[POS-GST sync] production bill print: the server’s RST number, hotel GSTIN, SAC and CGST / SGST — “issued and kept by the hotel server”',()=>{
    const s=rt.get().state,html=UI.renderBill(s,'1','902');
    for(const t of ['TAX INVOICE · B2B','RST/2627/0000002','Hotel GSTIN 27AAAAA0000A1Z2','SAC 996331','CGST @2.5%','Buyer GSTIN 29AAGCB7383J1Z4','issued and kept by the hotel server'])
      assert.ok(html.includes(t),'production bill lacks: '+t);
    assert.ok(!html.includes('Demonstration'),'no demo wording in the production build');
    assert.match(UI.renderBill(s,'1','903'),/reversed in full by credit note CN\/2627\/00000001/);
  });
}
