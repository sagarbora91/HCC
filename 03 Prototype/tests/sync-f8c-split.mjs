// F8C-SYNC (lane f8c-sync): split folio in the production build (SF-CONTRACT §2–§7, DEC-152). The production sync layer loads every folio of a
// split stay (GET /folios/by-reservation/{id}/summary + GET /folios/{folioId} for B–D + the folio invoices of a departed stay) into
// Booking.folios / Line.folio / Receipt.folio with each folio's own id and version, and sends every SF command to the server with its folio:
// SF_FOLIO_OPEN → OpenFolio, SF_ROUTE_SET → SetFolioRouting, SF_FOLIO_REMOVE → RemoveFolio, PAYMENT / CHARGE / DISCOUNT / REFUND_REQUEST /
// CORRECTION_REQUEST / COMPANY_CREDIT_REQUEST / CREDIT_NOTE / INVOICE_ISSUE with the folio's FolioId, GUEST_CREDIT_REQUEST with folioId,
// CHECKOUT with CreditApprovalIds (invoices numbered by the server inside the checkout). A single-folio stay is exactly today's (I1).
import {loadSync,fakeServer,startRuntime,capabilities,loadPages,reply} from './_sync-harness.mjs';

const BD='2026-10-24';
const item=(id,over={})=>({reservationId:id,publicNo:'HCC-'+id,version:'V'+id,status:'InHouse',guestName:'Guest '+id,phone:'+91 90000 11111',roomId:7,roomNo:'105',
  roomTypeId:2,roomType:'Deluxe',arrival:'2026-10-21',departure:BD,adults:2,children:0,source:'Corporate',nightlyRatePaise:300000,holdUntil:null,groupBlockId:null,
  restrictionOverride:null,cancelReason:null,notes:null,company:null,guestId:null,registered:true,stayId:500+id%100,stayVersion:'SV'+id,stayStatus:'InHouse',checkedInDate:'2026-10-21',
  checkedOutDate:null,segments:[],...over});
const line=(chargeId,kind,date,label,amountPaise,taxBps)=>({chargeId,kind,serviceDate:date,label,amountPaise,taxBps,taxPaise:Math.round(amountPaise*taxBps/10000),reversesChargeId:null,reversed:false});
const rcpt=(receiptId,kind,tender,amountPaise)=>({receiptId,kind,tender,amountPaise,businessDate:BD,reference:'R'+receiptId,refundOf:null,refundedPaise:0,refundablePaise:kind==='payment'?amountPaise:0});
function view(folioId,reservationId,status,lines,receipts,approvals=[]){
  const net=lines.reduce((a,l)=>a+l.amountPaise,0),tax=lines.reduce((a,l)=>a+l.taxPaise,0);
  const paid=receipts.filter(r=>r.kind==='payment').reduce((a,r)=>a+r.amountPaise,0),credit=receipts.filter(r=>r.kind==='company-credit'||r.kind==='guest-credit').reduce((a,r)=>a+r.amountPaise,0);
  return {folioId,reservationId,reservationStatus:status,businessDate:BD,version:'FV'+folioId,lines,receipts,approvals,
    totals:{netPaise:net,taxPaise:tax,totalPaise:net+tax,paidPaise:paid,depositPaise:0,companyCreditPaise:credit,balancePaise:net+tax-paid-credit}};
}
const row=(folioId,seq,payerKind,routes,over={})=>({folioId,seq,letter:'ABCD'[seq-1],payerKind,payerName:null,payerGstin:null,companyId:null,customerKey:null,routes,
  netPaise:0,taxPaise:0,totalPaise:0,paidPaise:0,depositPaise:0,balancePaise:0,invoiceNo:null,removed:false,...over});
const DECCAN={payerName:'Deccan Agro Ltd (fictional)',payerGstin:'27AAPFU0939F1ZV',companyId:5};
const approval=(approvalId,action,amountPaise,status='Approved')=>({approvalId,action,targetKind:'folio',targetId:0,amountPaise,status,reason:'Company credit · Deccan',requestedBy:3,
  decidedBy:status==='Approved'?1:null,expiresUtc:'2026-10-25T04:30:00.000Z',resultId:null,version:'AV'+approvalId});

// The golden stay §5.7 (both suites): folio A = laundry ₹500 @18 % + food ₹1,200 @5 %, ₹1,000 UPI paid; folio B (Deccan, room) = 3 nights ₹3,000 @5 %,
// company credit ₹9,450 approved; folio C removed (empty); folio D = Udhaar (credit guest Asha Rao), empty.
const A1201=view(6201,1201,'InHouse',[line(9001,'service',BD,'Laundry',50000,1800),line(9002,'service',BD,'Room service food',120000,500)],[rcpt(7001,'payment','UPI',100000)]);
const B1201=view(6202,1201,'InHouse',['2026-10-21','2026-10-22','2026-10-23'].map((d,i)=>line(9101+i,'room',d,'Room night · '+d,300000,500)),[],[approval(801,'credit',945000)]);
const D1201=view(6204,1201,'InHouse',[],[]);
// HCC-1202: single-folio stay (I1). HCC-1203: departed split stay with both folio invoices and a pending credit-note request on B's.
// HCC-1204: departed split stay checked out without a hotel GSTIN (no folio invoice numbered yet, SFQ-19).
const A1202=view(6301,1202,'InHouse',[line(9201,'room','2026-10-23','Room night',300000,500)],[]);
const A1203=view(6401,1203,'Departed',[line(9301,'service','2026-10-23','Laundry',50000,1800)],[rcpt(7301,'payment','UPI',59000)]);
const B1203=view(6402,1203,'Departed',[line(9311,'room','2026-10-23','Room night',300000,500)],[rcpt(7311,'company-credit','Company credit',315000)]);
const A1204=view(6501,1204,'Departed',[line(9401,'service','2026-10-23','Laundry',50000,1800)],[rcpt(7401,'payment','UPI',59000)]);
const B1204=view(6502,1204,'Departed',[line(9411,'room','2026-10-23','Room night',300000,500)],[rcpt(7411,'company-credit','Company credit',315000)]);
const VIEWS=Object.fromEntries([A1201,B1201,D1201,A1202,A1203,B1203,A1204,B1204].map(v=>[v.folioId,v]));
const PRIMARY={1201:A1201,1202:A1202,1203:A1203,1204:A1204};
const SUMMARY={
  1201:[row(6201,1,'guest',['extras']),row(6202,2,'company',['room'],DECCAN),row(6203,3,'guest',[],{payerName:'Removed Friend',removed:true}),
    row(6204,4,'udhaar',[],{payerName:'Asha Rao',customerKey:'9820001111'})],
  1202:[row(6301,1,'guest',['room','extras'])],
  1203:[row(6401,1,'guest',['extras'],{invoiceNo:'HPE/2627/0101'}),row(6402,2,'company',['room'],{...DECCAN,invoiceNo:'HPE/2627/0102'})],
  1204:[row(6501,1,'guest',['extras']),row(6502,2,'company',['room'],DECCAN)],
};
const INVOICES={6401:{invoiceId:9101,invoiceNo:'HPE/2627/0101',docType:'TAX_INVOICE',invoiceDate:'2026-10-23',sourceKind:'folio',sourceId:6401,totalPaise:59000,creditNotes:[]},
  6402:{invoiceId:9102,invoiceNo:'HPE/2627/0102',docType:'TAX_INVOICE',invoiceDate:'2026-10-23',sourceKind:'folio',sourceId:6402,totalPaise:315000,
    creditNotes:[{creditNoteId:61,creditNoteNo:'CRN/2627/0001',noteDate:BD,reason:'Night disputed',taxablePaise:10000,totalPaise:10500}]}};
const shift={shiftId:41,tillId:3,businessDate:BD,status:'Open',openingFloatPaise:500000,expectedPaise:null,countedPaise:null,approvalId:null,version:'SHFv1'};
const guest=id=>({guestId:id,name:'Asha Rao',phone:'+91 98200 01111',email:'a@example.test',address:'x',nationality:'Indian',idType:'Aadhaar (demo)',idLast4:'1234',
  company:null,gstin:null,party:[],verified:true,masked:false,version:'GV'+id,idMasked:'XXXX-XXXX-1234',idOnFile:true,canRevealId:true,preferences:null,notes:null,origin:'manual'});
const echo=name=>({body})=>({name,echo:body,folioVersion:'X'});

export async function register({test,assert,M}){
  const S=await loadSync();
  let checkoutInvoices=['HPE/2627/0103','HPE/2627/0104'];
  const srv=fakeServer({
    'GET /me/capabilities':()=>capabilities({grants:[...capabilities().permissions.map(g=>[g.permissionId,'A']),['PERM-INV-002','A']]}),
    'GET /reservations':()=>({businessDate:BD,total:4,page:1,pageSize:500,items:[item(1201),item(1202),
      item(1203,{status:'Departed',stayStatus:'Departed',checkedOutDate:BD,arrival:'2026-10-23'}),item(1204,{status:'Departed',stayStatus:'Departed',checkedOutDate:BD,arrival:'2026-10-23'})]}),
    'GET /guests':()=>({total:1,page:1,pageSize:500,items:[guest(9)]}),
    'GET /guests/credit':()=>[{guestId:9,name:'Asha Rao',phone:null,limitPaise:1000000,active:true,customerKey:'9820001111',note:null,openPaise:0,oldestOpenDate:null}],
    'GET /groups':()=>[],
    // the approvals slice: owner (admin) + the POS patch answer empty, so the money patch is what this test reads
    'GET /configuration/requests':()=>({items:[]}),'GET /pos/void-requests':()=>[],'GET /admin/users':()=>({page:1,pageSize:100,total:0,items:[]}),
    'GET /cashier/desk':()=>({businessDate:BD,tills:[{till:{tillId:3,code:'FRONT',name:'Front desk',version:'T1'},active:{shift,submittedBy:null,differenceReason:null,closedBy:null,approval:null},expected:{expectedPaise:500000},lastClosed:null}]}),
    'GET /folios/by-reservation/:id':({params})=>{const id=Number(params.id);return {reservationId:id,stayId:500+id%100,stayVersion:'SV'+id,folio:PRIMARY[id]??null};},
    'GET /folios/by-reservation/:id/summary':({params})=>SUMMARY[Number(params.id)]??[],
    'GET /folios/:id':({params})=>VIEWS[Number(params.id)],
    'GET /invoices/by-source/folio/:id':({params})=>INVOICES[Number(params.id)]??reply(404,{code:'NOT_FOUND',message:'No invoice for this folio.'}),
    'GET /credit-note-requests':()=>[{requestId:77,invoiceId:9102,invoiceNo:'HPE/2627/0102',mode:'amount',amountPaise:30000,lines:[],reason:'Second night disputed',status:'Pending',direct:false,
      requestedBy:3,requestedUtc:'2026-10-24T05:00:00.000Z',expiresUtc:'2026-10-25T05:00:00.000Z',decidedBy:null,decisionReason:null,creditNoteId:null,version:'CNRV77'}],
    'POST /commands/OpenFolio':({body})=>({reservationId:body.reservationId,folioId:6999,seq:2,letter:'B',payerKind:body.payerKind,routes:body.routes,removed:false}),
    'POST /commands/SetFolioRouting':({body})=>({reservationId:body.reservationId,folioId:6202,seq:body.folioSeq,letter:'ABCD'[body.folioSeq-1],payerKind:'company',routes:body.routes,removed:false}),
    'POST /commands/RemoveFolio':({body})=>({reservationId:body.reservationId,folioId:6204,seq:body.folioSeq,letter:'D',payerKind:'udhaar',routes:[],removed:true}),
    'POST /commands/RecordReceipt':echo('RecordReceipt'),'POST /commands/PostCharge':echo('PostCharge'),'POST /commands/RequestAdjustment':echo('RequestAdjustment'),
    'POST /commands/RequestGuestCredit':echo('RequestGuestCredit'),'POST /commands/RequestCreditNote':echo('RequestCreditNote'),'POST /commands/IssueInvoice':echo('IssueInvoice'),
    'POST /commands/CheckoutStay':({body})=>({stayId:body.stayId,reservationId:1201,roomId:7,roomNo:'105',roomHk:'Dirty',businessDate:BD,outboxId:1,stayVersion:'SV2',invoiceNos:checkoutInvoices}),
  });
  const rt=await startRuntime(S,srv,{areas:S.AREAS.filter(a=>a.id!=='channels')});
  const st=rt.get().state;
  const bk=id=>rt.get().state.bookings.find(b=>b.id===id);
  const b1=st.bookings.find(b=>b.id==='1201'),b2=st.bookings.find(b=>b.id==='1202'),b3=st.bookings.find(b=>b.id==='1203');
  const errors=rt.get().errors;

  test('[F8C-SYNC] read: a split stay loads every live folio (letters = server seq), payer, routes, removed C; lines and receipts carry their folio',()=>{
    assert.deepEqual([errors.bookings,errors.approvals,errors.guests],[undefined,undefined,undefined],JSON.stringify(errors)); // other areas have no fake routes here
    assert.deepEqual(b1.folios.map(f=>[f.id,f.payer.kind,f.payer.name,f.payer.ref??null,f.payer.gstin??null,f.routes,!!f.removedAt]),[
      ['A','guest','Guest 1201',null,null,['extras'],false],['B','company','Deccan Agro Ltd (fictional)','5','27AAPFU0939F1ZV',['room'],false],
      ['C','guest','Removed Friend',null,null,[],true],['D','udhaar','Asha Rao','9820001111',null,[],false]]);
    assert.deepEqual(b1.lines.map(l=>[l.id,l.folio]),[['9001','A'],['9002','A'],['9101','B'],['9102','B'],['9103','B']]);
    assert.deepEqual(b1.receipts.map(r=>[r.id,r.folio]),[['7001','A']]);
    assert.ok(M.sfIsSplit(b1));
    assert.equal(srv.gets('/folios/6202').length>0,true,'folio B read');assert.equal(srv.gets('/folios/6203').length,0,'a removed folio is not read');
    assert.equal(srv.gets('/folios/6201').length,0,'folio A comes with the by-reservation read');
  });
  test('[F8C-SYNC] read: per-folio figures are the server\'s to the paisa; the stay equals the unsplit control (₹11,300, I2)',()=>{
    const t=f=>{const x=M.totals(M.sfView(b1,f));return [x.net,x.tax,x.total,x.paid,x.balance];};
    assert.deepEqual(t('A'),[170000,15000,185000,100000,85000]);
    assert.deepEqual(t('B'),[900000,45000,945000,0,945000]);
    assert.deepEqual(t('D'),[0,0,0,0,0]);
    assert.equal(M.totals(b1).total,1130000);
    assert.equal(M.totals(b1).balance,85000+945000);
  });
  test('[F8C-SYNC] read: folio approvals carry meta.folio; a credit-note request on folio B\'s invoice maps to the stay and folio B',()=>{
    const a=st.approvals.find(x=>x.id==='801');
    assert.deepEqual([a.kind,a.booking,a.amount,a.meta.folio,a.status],['company-credit','1201',945000,'B','Approved']);
    const cn=st.approvals.find(x=>x.id==='cnr-77');
    assert.deepEqual([cn.booking,cn.kind,cn.meta.folio,cn.amount],['1203','credit-note','B',30000]);
  });
  test('[F8C-SYNC] read: departed split stay — each folio its invoice number, Booking.invoiceNo = folio A\'s, credit notes per folio',()=>{
    assert.deepEqual(b3.folios.map(f=>[f.id,f.invoiceNo]),[['A','HPE/2627/0101'],['B','HPE/2627/0102']]);
    assert.equal(b3.invoiceNo,'HPE/2627/0101');
    assert.deepEqual(b3.creditNotes.map(c=>[c.no,c.folio,c.amount]),[['CRN/2627/0001','B',10000]]);
    assert.deepEqual(M.sfInvoiceDocs(b3).map(d=>[d.folio,d.no]),[['A','HPE/2627/0101'],['B','HPE/2627/0102']]);
  });
  test('[F8C-SYNC] I1: a single-folio stay is exactly today\'s booking (no folios, no folio stamp)',()=>{
    assert.equal(b2.folios,undefined);assert.ok(b2.lines.every(l=>l.folio===undefined));assert.equal(M.sfIsSplit(b2),false);
  });
  const P=await loadPages();
  const desk=P.renderPages(st,rt.get().me?.uid),phone=P.renderPages(st,rt.get().me?.uid,true);
  test('[F8C-SYNC] every desktop and phone page renders on split-stay server data',()=>{assert.deepEqual(desk.errors,{});assert.deepEqual(phone.errors,{});});

  // ── commands ──
  const run=async cmd=>{srv.reset();const r=await rt.run(cmd);return {r,calls:[...srv.calls.filter(c=>c.method==='POST')],gets:srv.calls.filter(c=>c.method==='GET').map(c=>c.path)};};
  const payB=await run({type:'PAYMENT',id:'1201',folio:'B',kind:'payment',amount:100000,method:'UPI',reference:'UPI-1',key:'pay:b'});
  const cashB=await run({type:'PAYMENT',id:'1201',folio:'B',kind:'payment',amount:100000,method:'Cash',key:'pay:cash-b'});
  const depB=await run({type:'PAYMENT',id:'1201',folio:'B',kind:'deposit',amount:100000,method:'UPI',reference:'U',key:'pay:dep-b'});
  const payX=await run({type:'PAYMENT',id:'1201',folio:'C',kind:'payment',amount:100000,method:'UPI',reference:'U',key:'pay:c'});
  test('[F8C-SYNC] PAYMENT {folio}: RecordReceipt on that folio\'s id; cash on the company folio and a deposit off A refused before any request; a removed folio is unknown',()=>{
    assert.ok(payB.r.ok,payB.r.text);assert.equal(payB.calls[0].body.folioId,6202);assert.equal(payB.calls[0].key,'pay:b');
    assert.equal(cashB.r.reason,'precheck');assert.match(cashB.r.text,/company folio is not paid in cash/);assert.equal(cashB.calls.length,0);
    assert.equal(depB.r.reason,'precheck');assert.match(depB.r.text,/Deposits are held on folio A only/);
    assert.equal(payX.r.reason,'precheck');assert.match(payX.r.text,/no open folio C/);assert.equal(payX.calls.length,0);
  });
  const chB=await run({type:'CHARGE',id:'1201',folio:'B',label:'Airport drop',amount:60000,key:'ch:b'});
  const chR=await run({type:'CHARGE',id:'1201',label:'Laundry',amount:20000,key:'ch:routed'});
  const ch1=await run({type:'CHARGE',id:'1202',label:'Laundry',amount:20000,key:'ch:single'});
  test('[F8C-SYNC] CHARGE: a named folio posts there (explicitFolio); none = folio A routed by the server; a single-folio body is unchanged',()=>{
    assert.deepEqual(chB.calls[0].body,{folioId:6202,label:'Airport drop',amountPaise:60000,gstCategory:null,explicitFolio:true});
    assert.deepEqual(chR.calls[0].body,{folioId:6201,label:'Laundry',amountPaise:20000,gstCategory:null});
    assert.deepEqual(ch1.calls[0].body,{folioId:6301,label:'Laundry',amountPaise:20000,gstCategory:null});
  });
  const dsc=await run({type:'DISCOUNT',id:'1201',amount:30000,reason:'Long stay',key:'d:1'});
  const dscA=await run({type:'DISCOUNT',id:'1201',folio:'A',amount:5000,reason:'Laundry delay',key:'d:2'});
  const rfd=await run({type:'REFUND_REQUEST',id:'1201',receiptId:'7001',amount:10000,reason:'Overpaid',key:'r:1'});
  const cor=await run({type:'CORRECTION_REQUEST',id:'1201',lineId:'9102',reason:'Wrong night',key:'c:1'});
  test('[F8C-SYNC] DISCOUNT (default = the room folio) / REFUND_REQUEST (receipt\'s folio) / CORRECTION_REQUEST (line\'s folio): that folio\'s id and version',()=>{
    assert.deepEqual([dsc.calls[0].body.folioId,dsc.calls[0].body.expectedVersion,dsc.calls[0].body.action],[6202,'FV6202','discount']);
    assert.deepEqual([dscA.calls[0].body.folioId,dscA.calls[0].body.expectedVersion],[6201,'FV6201']);
    assert.deepEqual([rfd.calls[0].body.folioId,rfd.calls[0].body.targetId,rfd.calls[0].body.action],[6201,7001,'refund']);
    assert.deepEqual([cor.calls[0].body.folioId,cor.calls[0].body.targetId,cor.calls[0].body.amountPaise],[6202,9102,300000]);
  });
  const ccB=await run({type:'COMPANY_CREDIT_REQUEST',id:'1201',folio:'B',key:'cc:b'});
  const ccA=await run({type:'COMPANY_CREDIT_REQUEST',id:'1201',folio:'A',key:'cc:a'});
  test('[F8C-SYNC] COMPANY_CREDIT_REQUEST {folio}: the company folio\'s balance on its id; folio A refused (prototype words)',()=>{
    assert.deepEqual(ccB.calls[0].body,{action:'credit',folioId:6202,targetId:null,amountPaise:945000,reason:'Company credit · Deccan Agro Ltd (fictional)',expectedVersion:'FV6202'});
    assert.equal(ccA.r.reason,'precheck');assert.match(ccA.r.text,/company folio only/);assert.equal(ccA.calls.length,0);
  });
  const gcD=await run({type:'GUEST_CREDIT_REQUEST',id:'1201',folio:'D',key:'gc:d'});
  const gcA=await run({type:'GUEST_CREDIT_REQUEST',id:'1201',folio:'A',reason:'Regular guest',key:'gc:a'});
  const gcB=await run({type:'GUEST_CREDIT_REQUEST',id:'1201',folio:'B',key:'gc:b'});
  test('[F8C-SYNC] GUEST_CREDIT_REQUEST {folio}: an Udhaar folio sends its folioId (default reason as the prototype); A sends none; a company folio is refused',()=>{
    assert.deepEqual(gcD.calls[0].body,{stayId:501,reason:'Guest credit · Asha Rao',folioId:6204});
    assert.deepEqual(gcA.calls[0].body,{stayId:501,reason:'Regular guest'});
    assert.equal(gcB.r.reason,'precheck');assert.match(gcB.r.text,/folio A or an Udhaar folio/);
  });
  const opn=await run({type:'SF_FOLIO_OPEN',id:'1202',payer:{kind:'company',ref:'5'},routes:['room'],reason:'Company pays the room',key:'sf:open'});
  const opnU=await run({type:'SF_FOLIO_OPEN',id:'1202',payer:{kind:'udhaar',ref:'9'},routes:['extras'],key:'sf:open-u'});
  const opnG=await run({type:'SF_FOLIO_OPEN',id:'1202',payer:{kind:'guest',name:'Brother (fictional)',gstin:'27aapfu0939f1zv'},routes:[],key:'sf:open-g'});
  const opnBad=await run({type:'SF_FOLIO_OPEN',id:'1202',payer:{kind:'group',name:'X'},routes:[],key:'sf:open-x'});
  test('[F8C-SYNC] SF_FOLIO_OPEN → OpenFolio (company id, Udhaar customer key + name from the credit guest, guest name + GSTIN); group refused',()=>{
    assert.ok(opn.r.ok,opn.r.text);
    assert.deepEqual(opn.calls[0].body,{reservationId:1202,payerKind:'company',companyId:5,customerKey:null,payerName:null,payerGstin:null,routes:['room'],reason:'Company pays the room'});
    assert.deepEqual(opnU.calls[0].body,{reservationId:1202,payerKind:'udhaar',companyId:null,customerKey:'9820001111',payerName:'Asha Rao',payerGstin:null,routes:['extras'],reason:null});
    assert.deepEqual(opnG.calls[0].body,{reservationId:1202,payerKind:'guest',companyId:null,customerKey:null,payerName:'Brother (fictional)',payerGstin:'27AAPFU0939F1ZV',routes:[],reason:null});
    assert.equal(opnBad.r.reason,'precheck');assert.match(opnBad.r.text,/paid by a guest, a company account or an Udhaar/);
    assert.notEqual(opn.r.reason,'unmapped');
  });
  const rtB=await run({type:'SF_ROUTE_SET',id:'1201',folio:'B',routes:[],reason:'Room back to the guest',key:'sf:route'});
  const rtA=await run({type:'SF_ROUTE_SET',id:'1201',folio:'A',routes:['room'],reason:'Guest pays the room',key:'sf:route-a'});
  const rtC=await run({type:'SF_ROUTE_SET',id:'1201',folio:'C',routes:['room'],reason:'Removed folio',key:'sf:route-c'});
  const rtShort=await run({type:'SF_ROUTE_SET',id:'1201',folio:'B',routes:['room'],reason:'ok',key:'sf:route-s'});
  test('[F8C-SYNC] SF_ROUTE_SET → SetFolioRouting (letter → seq); on folio A the holder gives the category up; removed folio / short reason refused',()=>{
    assert.deepEqual(rtB.calls[0].body,{reservationId:1201,folioSeq:2,routes:[],reason:'Room back to the guest'});
    assert.deepEqual(rtA.calls.map(c=>[c.body,c.key]),[[{reservationId:1201,folioSeq:2,routes:[],reason:'Guest pays the room'},'sf:route-a:route-B']]);
    assert.equal(rtC.r.reason,'precheck');assert.match(rtC.r.text,/no open folio C/);
    assert.equal(rtShort.r.reason,'precheck');assert.match(rtShort.r.text,/3–200 characters/);
  });
  const rmD=await run({type:'SF_FOLIO_REMOVE',id:'1201',folio:'D',key:'sf:rm'});
  const rmA=await run({type:'SF_FOLIO_REMOVE',id:'1201',folio:'A',key:'sf:rm-a'});
  test('[F8C-SYNC] SF_FOLIO_REMOVE → RemoveFolio {folioSeq}; folio A refused before any request',()=>{
    assert.deepEqual(rmD.calls[0].body,{reservationId:1201,folioSeq:4});
    assert.equal(rmA.r.reason,'precheck');assert.match(rmA.r.text,/cannot be removed/);
    assert.ok(rmD.gets.includes('/folios/by-reservation/1201/summary')&&rmD.gets.includes('/folios/6202'),'the bookings slice reloads (folios read again)');
  });
  const cnB=await run({type:'CREDIT_NOTE',id:'1203',folio:'B',amount:30000,reason:'Night disputed',key:'cn:b'});
  const cnX=await run({type:'CREDIT_NOTE',id:'1204',folio:'B',amount:30000,reason:'Night disputed',key:'cn:x'});
  const inv3=await run({type:'INVOICE_ISSUE',id:'1203',key:'invoice:1203'});
  const inv4=await run({type:'INVOICE_ISSUE',id:'1204',key:'invoice:1204'});
  const blc=await run({type:'BILL_CORRECTION_REQUEST',booking:'1203',changes:[],reason:'x',key:'blc:1'});
  test('[F8C-SYNC] CREDIT_NOTE {folio} → that folio\'s invoice; INVOICE_ISSUE on a split stay = one IssueInvoice per folio, letter order, buyer = payer (SFQ-19)',()=>{
    assert.deepEqual(cnB.calls[0].body,{invoiceId:9102,mode:'amount',reason:'Night disputed',amountPaise:30000});
    assert.equal(cnX.r.reason,'precheck');assert.match(cnX.r.text,/Folio B has no tax invoice/);
    assert.equal(inv3.r.reason,'precheck');assert.match(inv3.r.text,/already has its tax invoice/);
    assert.deepEqual(inv4.calls.map(c=>[c.body,c.key]),[[{sourceKind:'folio',sourceId:6501},'invoice:1204:invoice-A'],
      [{sourceKind:'folio',sourceId:6502,supplyType:'B2B',buyerName:'Deccan Agro Ltd (fictional)',buyerGstin:'27AAPFU0939F1ZV'},'invoice:1204:invoice-B']]);
    assert.equal(blc.r.reason,'precheck');assert.match(blc.r.text,/split bill/);
  });
  // Checkout: folio A pays its ₹850 first (fake: the reload is not changed, so the client sends what the screen has), B is covered by approval 801.
  const co=await run({type:'CHECKOUT',id:'1201',key:'co:1'});
  checkoutInvoices=[];
  const coNo=await run({type:'CHECKOUT',id:'1201',key:'co:2'});
  test('[F8C-SYNC] CHECKOUT of a split stay → CheckoutStay with creditApprovalIds per folio; no IssueInvoice afterwards; no numbers = the SFQ-19 warning',()=>{
    assert.deepEqual(co.calls.map(c=>c.path),['/commands/CheckoutStay']);
    assert.deepEqual(co.calls[0].body,{stayId:501,creditApprovalIds:[801],desk:true,expectedVersion:'SV1201'});
    assert.ok(co.r.ok,co.r.text);assert.equal(co.r.warning,undefined);
    assert.ok(coNo.r.ok,coNo.r.text);assert.match(coNo.r.warning??coNo.r.text,/folio tax invoices were not numbered/);
  });
}
