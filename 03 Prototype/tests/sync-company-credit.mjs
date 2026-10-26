// DEC-076 production mapping (sync/reservations.ts, sync/money.ts, clients/http.ts): a booking billed to a company account carries the
// server companyId both ways; BOOKING_COMPANY → SetReservationCompany; GRP_COMPANY_PAYMENT → RecordCompanyPayment; a bill-to-company block's
// master is created with the companyId; the company book's credit picture (exposure, receivable, remaining) and payments replace the local
// estimate; the server's CREDIT_LIMIT refusal reaches the screen word for word.
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const nights=[{date:'2026-10-24',ratePaise:200000,taxBps:500,taxPaise:10000},{date:'2026-10-25',ratePaise:200000,taxBps:500,taxPaise:10000}];
const availBody={businessDate:'2026-10-24',arrival:'2026-10-24',departure:'2026-10-26',nights:2,party:2,
  types:[{roomTypeId:2,code:'Deluxe',maxOccupancy:3,availableRooms:5,violations:[]}],
  rooms:[{roomId:7,roomNo:'105',roomTypeId:2,roomType:'Deluxe',capacity:3,available:true,reasons:[],heldForBlock:null,nightlyRatePaise:200000}]};
const quoteBody={quoteId:41,version:'AAAAAAAAB9E=',roomTypeId:2,roomId:7,arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,ratePlanId:'BAR',nights,
  netPaise:400000,taxPaise:20000,totalPaise:420000,expiresAt:'2026-10-24T10:00:00.000Z',violations:[]};
const item=(id,over={})=>({reservationId:id,publicNo:'HCC-'+id,version:'V'+id,status:'Confirmed',guestName:'Guest '+id,phone:'+91 90000 11111',roomId:7,roomNo:'105',
  roomTypeId:2,roomType:'Deluxe',arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,source:'Corporate',nightlyRatePaise:200000,holdUntil:null,groupBlockId:null,
  restrictionOverride:null,cancelReason:null,notes:null,company:null,guestId:null,registered:false,stayId:null,stayVersion:null,stayStatus:null,checkedInDate:null,
  checkedOutDate:null,segments:[],...over});
const REFUSAL='Credit limit exceeded for Kale Textiles (fictional): limit ₹5,000, used ₹4,200, remaining ₹800; this booking needs ₹4,200. Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).';
const ok=name=>({body})=>({name,ok:true,echo:body});

export async function register({test,assert,M}){
  const S=await loadSync();
  const store=new Map([[1201,item(1201,{company:{name:'Kale Textiles (fictional)',gstin:'27ZZFIC0010F1Z5',companyId:5}}),],[1202,item(1202,{company:{name:'Walk-in Co',gstin:null,companyId:null}})]]);
  let nextId=1301;
  // GET /groups/companies for a PERM-GRP-004 holder: limit ₹5,000; HCC-1201 (₹4,200) is billed to it; ₹1,000 was paid against an old invoice.
  const book={companies:[{companyId:5,name:'Kale Textiles (fictional)',gstin:'27ZZFIC0010F1Z5',contact:'S. Kale',phone:null,email:null,city:'Latur',rates:{},creditLimitPaise:500000,version:'CO5',
      credit:{companyId:5,creditLimitPaise:500000,exposurePaise:420000,receivablePaise:0,openStaysPaise:420000,groupMastersPaise:0,remainingPaise:80000,
        items:[{kind:'stay',id:1201,reference:'HCC-1201',amountPaise:420000},{kind:'receivable',id:7001,reference:'HCC-1100',amountPaise:100000},{kind:'payment',id:3,reference:'INV/2627/0009',amountPaise:-100000}]}}],
    requests:[],payments:[{paymentId:3,companyId:5,businessDate:'2026-10-23',tender:'Bank transfer',reference:'NEFT-1',invoiceNo:'INV/2627/0009',amountPaise:100000,recordedBy:4,recordedUtc:'2026-10-23T06:00:00.000Z'}]};
  const srv=fakeServer({
    'GET /me/capabilities':()=>capabilities({grants:[...capabilities().permissions.map(g=>[g.permissionId,'A'])]}),
    'GET /availability':()=>availBody,
    'POST /commands/QuoteStay':()=>quoteBody,
    'POST /commands/CreateReservation':({body})=>{const id=nextId++;
      store.set(id,item(id,{company:body.company?{name:body.company.companyId?'Kale Textiles (fictional)':body.company.name,gstin:body.company.companyId?'27ZZFIC0010F1Z5':body.company.gstin??null,companyId:body.company.companyId??null}:null}));
      return {reservationId:id,publicNo:'HCC-'+id,version:'V'+id,status:'Confirmed',holdUntil:null};},
    'POST /commands/SetReservationCompany':({body})=>body.reservationId===1202&&body.company?.companyId
      ?reply(409,{code:'INVALID_TRANSITION',message:REFUSAL,details:{reason:'CREDIT_LIMIT',companyId:5,limitPaise:500000,usedPaise:420000,remainingPaise:80000,neededPaise:420000}})
      :{reservationId:body.reservationId,publicNo:'HCC-'+body.reservationId,version:'V9',status:'Confirmed',holdUntil:null},
    'POST /commands/RecordCompanyPayment':ok('RecordCompanyPayment'),
    'POST /commands/CreateGroupBlock':()=>({groupBlockId:71,code:'X',version:'B1'}),
    'POST /commands/CreateGroup':ok('CreateGroup'),
    'GET /reservations':()=>{const items=[...store.values()];return {businessDate:'2026-10-24',total:items.length,page:1,pageSize:500,items};},
    'GET /guests':()=>({total:0,page:1,pageSize:500,items:[]}),
    'GET /groups':()=>[],
    'GET /groups/companies':()=>book,
    'GET /folios/by-reservation/:id':({params})=>store.has(Number(params.id))?{reservationId:Number(params.id),stayId:null,stayVersion:null,folio:null}:reply(404,{code:'NOT_FOUND',message:'Reservation not found.'}),
  });
  const rt=await startRuntime(S,srv,{areas:S.AREAS.filter(a=>a.id!=='channels')});
  const s0=rt.get().state;

  test('[DEC-076 sync] reads: the booking’s company account (companyId → account) and the server credit picture + payments replace the local estimate',()=>{
    assert.deepEqual(s0.bookings.find(b=>b.id==='1201').company,{name:'Kale Textiles (fictional)',gstin:'27ZZFIC0010F1Z5',account:'5'});
    assert.deepEqual(s0.bookings.find(b=>b.id==='1202').company,{name:'Walk-in Co'});
    const c=s0.groupsX.companies[0];
    assert.deepEqual([c.id,c.creditLimit,c.credit.exposure,c.credit.receivable,c.credit.remaining,c.credit.items.map(i=>[i.kind,i.id,i.amount])],
      ['5',500000,420000,0,80000,[['stay','1201',420000],['receivable','7001',100000],['payment','3',-100000]]]);
    const k=M.grpCredit(s0,c);assert.deepEqual([k.exposure,k.receivable,k.stays,k.available],[420000,0,420000,80000]);
    assert.deepEqual(s0.groupsX.payments,[{id:'3',company:'5',amount:100000,method:'Bank transfer',reference:'NEFT-1',invoice:'INV/2627/0009',date:'2026-10-23',actor:'4',at:'2026-10-23T11:30'}]);
    // The screen preview for HCC-1201: its own ₹4,200 comes from the server breakdown, so the rest of the company uses ₹0.
    assert.deepEqual(M.grpCreditPreview(s0,s0.bookings.find(b=>b.id==='1201'),100).used,0);
  });

  srv.reset();
  const bk=await rt.run({type:'BOOK',guest:'Corp Guest',phone:'+91 90000 22222',people:2,children:0,room:'105',arrival:'2026-10-24',departure:'2026-10-26',source:'Corporate',
    company:{name:'Kale Textiles (fictional)',gstin:'27ZZFIC0010F1Z5',account:'5'},key:'new:dec076-1'});
  const create=srv.commands('CreateReservation')[0];
  test('[DEC-076 sync] BOOK billed to a company account → CreateReservation company.companyId (the server fills the name and GSTIN)',()=>{
    assert.ok(bk.ok,bk.text);
    assert.deepEqual(create.body.company,{name:'Kale Textiles (fictional)',companyId:5});
    assert.equal(rt.get().state.bookings.find(b=>b.id==='1301').company.account,'5');
  });

  srv.reset();
  const link=await rt.run({type:'BOOKING_COMPANY',id:'1201',account:'',key:'bco:1201'});const unlinkCmd=srv.commands('SetReservationCompany')[0];
  srv.reset();
  const refused=await rt.run({type:'BOOKING_COMPANY',id:'1202',account:'5',key:'bco:1202'});const linkCmd=srv.commands('SetReservationCompany')[0];
  test('[DEC-076 sync] BOOKING_COMPANY → SetReservationCompany with the booking version; the CREDIT_LIMIT refusal is shown word for word',()=>{
    assert.ok(link.ok,link.text);
    assert.deepEqual(unlinkCmd.body,{reservationId:1201,company:{name:'Kale Textiles (fictional)',gstin:'27ZZFIC0010F1Z5'},expectedVersion:'V1201'});
    assert.deepEqual(linkCmd.body,{reservationId:1202,company:{name:'',companyId:5},expectedVersion:'V1202'});
    assert.equal(refused.ok,false);assert.equal(refused.reason,'server');assert.equal(refused.text,'Action not allowed now: '+REFUSAL); // the catalog title of INVALID_TRANSITION + the server text
    assert.equal(refused.error.code,'INVALID_TRANSITION');
  });

  srv.reset();
  const cash=await rt.run({type:'GRP_COMPANY_PAYMENT',company:'5',amount:50000,method:'Cash',reference:'x',key:'gcp:0'});
  const pay=await rt.run({type:'GRP_COMPANY_PAYMENT',company:'5',amount:50000,method:'UPI',reference:'UPI-77',invoice:'inv/2627/0010',key:'gcp:1'});const payCmd=srv.commands('RecordCompanyPayment')[0];
  test('[DEC-076 sync] GRP_COMPANY_PAYMENT → RecordCompanyPayment (cash refused before any request); the company book reloads',()=>{
    assert.equal(cash.reason,'precheck');assert.match(cash.text,/bank transfer, UPI or card/);
    assert.ok(pay.ok,pay.text);
    assert.deepEqual([payCmd.key,payCmd.body],['gcp:1',{companyId:5,tender:'UPI',amountPaise:50000,reference:'UPI-77',invoiceNo:'inv/2627/0010'}]);
    assert.ok(srv.gets('/groups/companies').length>0,'groups reload after the payment');
  });

  srv.reset();
  S.grpSetBlocksOnServer(true);   // DEC-080 refuses group blocks until Hcc:GroupBlocks:Enabled is on; this checks the mapping for that day
  const blk=await rt.run({type:'GRP_BLOCK_CREATE',name:'Kale dealers meet',billTo:'company',company:'5',arrival:'2026-12-20',departure:'2026-12-22',cutoff:'2026-12-10',allot:{Deluxe:2},
    rates:{Deluxe:250000},contact:'S. Kale',key:'grp:dec076'});
  S.grpSetBlocksOnServer(false);
  const grp=srv.commands('CreateGroup')[0];
  test('[DEC-076 sync] GRP_BLOCK_CREATE billed to a company account → CreateGroup with its companyId (routing to the master is credit-checked)',()=>{
    assert.ok(blk.ok,blk.text);assert.equal(grp.body.companyId,5);assert.equal(grp.body.billingName,'Kale Textiles (fictional)');
  });
}
