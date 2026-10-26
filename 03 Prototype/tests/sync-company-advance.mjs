// W4 gap G5: (1) the company (name + GSTIN) is a real field of the server reservation: sent by BOOK / CHANGE_DATES, read back from
// GET /reservations (older bookings: still read from the notes) and used for company credit + the B2B invoice; (2) an advance (deposit /
// payment) on a Confirmed booking that has no folio yet is sent as RecordReceipt by reservationId (folioId 0): the server opens the folio.
import {loadSync,fakeServer,startRuntime,reply,capabilities,ownErrors} from './_sync-harness.mjs';

const nights=[{date:'2026-10-24',ratePaise:200000,taxBps:500,taxPaise:10000},{date:'2026-10-25',ratePaise:200000,taxBps:500,taxPaise:10000}];
const availBody={businessDate:'2026-10-24',arrival:'2026-10-24',departure:'2026-10-26',nights:2,party:2,types:[],
  rooms:[{roomId:7,roomNo:'105',roomTypeId:2,roomType:'Deluxe',capacity:3,available:true,reasons:[],heldForBlock:null,nightlyRatePaise:200000}]};
const quoteBody={quoteId:41,version:'AAAAAAAAB9E=',roomTypeId:2,roomId:7,arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,ratePlanId:'BAR',nights,
  netPaise:400000,taxPaise:20000,totalPaise:420000,expiresAt:'2026-10-24T10:00:00.000Z',violations:[]};
const item=(id,over={})=>({reservationId:id,publicNo:'HCC-'+id,version:'V'+id,status:'Confirmed',guestName:'Guest '+id,phone:'+91 90000 11111',roomId:7,roomNo:'105',
  roomTypeId:2,roomType:'Deluxe',arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,source:'Direct',nightlyRatePaise:200000,holdUntil:null,groupBlockId:null,
  restrictionOverride:null,cancelReason:null,notes:null,company:null,guestId:null,registered:false,stayId:null,stayVersion:null,stayStatus:null,checkedInDate:null,
  checkedOutDate:null,segments:[],...over});

export async function register({test,assert}){
  const S=await loadSync();
  const store=new Map();let nextId=1301;
  const owner=capabilities({grants:[...capabilities().permissions.map(g=>[g.permissionId,'A'])]});
  const srv=fakeServer({
    'GET /me/capabilities':()=>owner,
    'GET /availability':()=>availBody,
    'POST /commands/QuoteStay':()=>quoteBody,
    'POST /commands/CreateReservation':({body})=>{const id=nextId++;
      store.set(id,item(id,{company:body.company?{name:body.company.name,gstin:body.company.gstin??null}:null,notes:body.notes??null}));
      return {reservationId:id,publicNo:'HCC-'+id,version:'V'+id,status:'Confirmed',holdUntil:null};},
    'POST /commands/AmendReservation':({body})=>({reservationId:body.reservationId,publicNo:'HCC-'+body.reservationId,version:'V2',status:'Confirmed',holdUntil:null}),
    'POST /commands/RecordReceipt':({body})=>{const id=Number(body.reservationId??0);
      return {receiptId:8001,folioId:id?9000+id:body.folioId,kind:body.kind,tender:body.tender,amountPaise:body.amountPaise,businessDate:'2026-10-24',shiftId:null,refundOf:null,folioVersion:'F1'};},
    'GET /reservations':()=>{const items=[...store.values()];return {businessDate:'2026-10-24',total:items.length,page:1,pageSize:500,items};},
    'GET /guests':()=>({total:0,page:1,pageSize:500,items:[]}),
    // Pre-arrival: the folio opens with the first receipt, so the read returns no folio until then.
    'GET /folios/by-reservation/:id':({params})=>store.has(Number(params.id))?{reservationId:Number(params.id),stayId:null,stayVersion:null,folio:null}:reply(404,{code:'NOT_FOUND',message:'Reservation not found.'}),
  });
  store.set(1200,item(1200,{notes:'Corporate rate · Company: Old Notes Ltd · GSTIN 27AAAAA0000A1Z5',guestName:'Old Booking'}));
  const rt=await startRuntime(S,srv,{areas:S.AREAS.filter(a=>a.id!=='channels')});
  const old=rt.get().state.bookings.find(b=>b.id==='1200');
  test('[G5 company] Older bookings: company still read from the notes (fallback) and kept out of the notes text',()=>{
    assert.deepEqual(old.company,{name:'Old Notes Ltd',gstin:'27AAAAA0000A1Z5'});assert.equal(old.notes,'Corporate rate');
  });

  srv.reset();
  const book=await rt.run({type:'BOOK',guest:'Corp Guest',phone:'+91 90000 22222',email:'g@corp.test',people:2,children:0,room:'105',arrival:'2026-10-24',departure:'2026-10-26',
    source:'Corporate',company:{name:'Acme Traders Pvt Ltd',gstin:'27ZZFIC0009F1Z5'},notes:'Late arrival',key:'new:co-1'});
  const create=srv.commands('CreateReservation')[0];const after=rt.get().state.bookings.find(b=>b.id==='1301');
  test('[G5 company] BOOK sends the company as a real field (not in notes); the list read gives it back as the booking company',()=>{
    assert.ok(book.ok,book.text);
    assert.deepEqual(create.body.company,{name:'Acme Traders Pvt Ltd',gstin:'27ZZFIC0009F1Z5'});
    assert.equal(create.body.notes,'Late arrival · E-mail: g@corp.test','e-mail stays in notes, the company does not');
    assert.deepEqual(after.company,{name:'Acme Traders Pvt Ltd',gstin:'27ZZFIC0009F1Z5'});
  });
  srv.reset();
  await rt.run({type:'BOOK',guest:'No Company',people:2,children:0,room:'105',arrival:'2026-10-24',departure:'2026-10-26',source:'Direct',key:'new:co-2'});
  const plain=srv.commands('CreateReservation')[0];
  srv.reset();
  await rt.run({type:'BOOK',guest:'Name Only',people:2,children:0,room:'105',arrival:'2026-10-24',departure:'2026-10-26',source:'Direct',company:{name:'  Solo Co  '},key:'new:co-3'});
  const nameOnly=srv.commands('CreateReservation')[0];
  test('[G5 company] No company means no company field; a name without GSTIN is sent trimmed with no gstin',()=>{
    assert.equal(plain.body.company,undefined);assert.deepEqual(nameOnly.body.company,{name:'Solo Co'});
    assert.equal(rt.get().state.bookings.find(b=>b.id==='1303').company?.gstin,undefined);
  });

  srv.reset();
  await rt.run({type:'CHANGE_DATES',id:'1301',room:'105',arrival:'2026-10-25',departure:'2026-10-27',company:{name:'Acme Group',gstin:'27ZZFIC0009F1Z5'},key:'chg:1'});
  const amend=srv.commands('AmendReservation')[0];
  srv.reset();
  await rt.run({type:'CHANGE_DATES',id:'1301',room:'105',arrival:'2026-10-25',departure:'2026-10-27',key:'chg:2'});
  const amend2=srv.commands('AmendReservation')[0];
  test('[G5 company] CHANGE_DATES carries the company only when the dialog gives one (a missing company leaves the stored one)',()=>{
    assert.deepEqual(amend.body.company,{name:'Acme Group',gstin:'27ZZFIC0009F1Z5'});assert.equal(amend2.body.company,undefined);
  });

  // advance before check-in
  srv.reset();
  const dep=await rt.run({type:'PAYMENT',id:'1301',amount:50000,kind:'deposit',method:'UPI',reference:'UPI-ADV-1',key:'adv:1'});
  const rr=srv.commands('RecordReceipt');const calls=srv.calls.map(c=>`${c.method} ${c.path}`);
  test('[G5 advance] PAYMENT on a Confirmed booking with no folio sends RecordReceipt by reservationId (folioId 0), then the bookings reload',()=>{
    assert.ok(dep.ok,dep.text);assert.equal(rr.length,1);
    assert.deepEqual([rr[0].key,rr[0].body],['adv:1',{folioId:0,reservationId:1301,kind:'deposit',tender:'UPI',amountPaise:50000,reference:'UPI-ADV-1',tillId:null}]);
    assert.ok(calls.includes('GET /reservations'),calls.join(','));
  });
  srv.reset();
  const split=await rt.run({type:'PAYMENT',id:'1301',amount:30000,kind:'payment',tenders:[{method:'UPI',amount:20000,reference:'U-1'},{method:'Card',amount:10000,reference:'C-1'}],key:'adv:2'});
  const rr2=srv.commands('RecordReceipt');
  test('[G5 advance] A split advance sends every tender by reservationId with step keys',()=>{
    assert.ok(split.ok,split.text);assert.deepEqual(rr2.map(x=>x.key),['adv:2:t1','adv:2:t2']);
    assert.deepEqual(rr2.map(x=>[x.body.folioId,x.body.reservationId,x.body.tender]),[[0,1301,'UPI'],[0,1301,'Card']]);
  });
  srv.reset();
  const ret=await rt.run({type:'PAYMENT',id:'1301',amount:1000,kind:'deposit-return',method:'UPI',reference:'R-1',key:'adv:3'});
  test('[G5 advance] A deposit return with no folio is still refused on the client (nothing to return)',()=>{
    assert.equal(ret.ok,false);assert.match(ret.text,/no folio/i);assert.equal(srv.commands('RecordReceipt').length,0);
  });
  test('[G5 company] No area error from the company / advance changes',()=>{assert.deepEqual(ownErrors(rt.get().errors,'reservations'),{});assert.equal(rt.get().errors.bookings,undefined,'bookings (list + folio patch) load cleanly');});
}
