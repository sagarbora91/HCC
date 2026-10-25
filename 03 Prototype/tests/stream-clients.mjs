// P5 client boundary (contracts §2, §3, §6): HTTP adapter contract shape with a fake fetch, mock-adapter parity with the
// direct execute()/selector calls, and the production build flavour (HTTP adapter only, demo controls compiled out).
// test() is synchronous, so every async step runs first and the assertions run on the captured results.
import {build} from 'esbuild';
import {mkdir} from 'node:fs/promises';

const P=process.env.HCC_CLIENTS_TEST_DIR||'tests/.generated';
const settle=async f=>{try{return {ok:true,value:await f()};}catch(error){return {ok:false,error};}};

/** Fake fetch: answers from a queue of handlers; records every call (url, method, headers, parsed body). */
function fakeFetch(handlers){
  const calls=[];let i=0;
  const fetch=async(url,init={})=>{
    calls.push({url,method:init.method,headers:{...init.headers},body:init.body===undefined?undefined:JSON.parse(init.body)});
    const h=handlers[Math.min(i++,handlers.length-1)];
    const r=await h(url,init);
    if(r instanceof Error)throw r;
    const text=typeof r.raw==='string'?r.raw:r.body===undefined?'':JSON.stringify(r.body);
    return {status:r.status??200,ok:(r.status??200)<300,text:async()=>text};
  };
  return {fetch,calls};
}
const ok=body=>()=>({status:200,body});
const fail=(status,body)=>()=>({status,body});
const netDown=()=>new TypeError('Failed to fetch');

export async function register({test,M,assert}){
  await mkdir(P,{recursive:true});
  await build({entryPoints:['src/clients/index.ts'],bundle:true,platform:'node',format:'esm',outfile:P+'/clients.mjs',logLevel:'error'});
  const C=await import('../'+P+'/clients.mjs');

  // ── HTTP adapter: shape (server DTOs as the .NET API sends them → client types; Wave 1 integration) ──
  const rooms=[{roomId:7,roomNo:'105',roomType:'Deluxe'}];
  const availBody={businessDate:'2026-10-24',arrival:'2026-10-24',departure:'2026-10-26',nights:2,party:2,
    types:[{roomTypeId:2,code:'Deluxe',maxOccupancy:3,availableRooms:1,violations:[]},{roomTypeId:4,code:'Suite',maxOccupancy:4,availableRooms:0,violations:[]}],
    rooms:[{roomId:7,roomNo:'105',roomTypeId:2,roomType:'Deluxe',capacity:3,available:true,reasons:[],heldForBlock:null,nightlyRatePaise:200000},
      {roomId:19,roomNo:'308',roomTypeId:4,roomType:'Suite',capacity:4,available:false,reasons:['Blocked'],heldForBlock:null,nightlyRatePaise:450000}]};
  const quoteBody={quoteId:41,version:'AAAAAAAAB9E=',roomTypeId:2,roomId:7,arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,ratePlanId:'BAR',
    nights:[{date:'2026-10-24',ratePaise:200000,taxBps:1000,taxPaise:20000},{date:'2026-10-25',ratePaise:200000,taxBps:1000,taxPaise:20000}],netPaise:400000,taxPaise:40000,totalPaise:440000,expiresAt:'2026-10-24T10:00:00.000Z',violations:[]};
  const created={reservationId:1201,publicNo:'HCC-1201',version:'AAAAAAAAB9I=',status:'Confirmed',holdUntil:null};
  const req={roomId:'105',guest:{name:'Journey Test',phone:'+91 90000 11111'},source:'Direct'};

  const a=fakeFetch([ok(availBody)]);
  const avail=await settle(()=>C.createHttpClients({fetch:a.fetch,baseUrl:'https://hcc.test/'}).reservations.getAvailability({arrival:'2026-10-24',departure:'2026-10-26',adults:2}));
  const typed=await settle(()=>C.createHttpClients({fetch:fakeFetch([ok(availBody)]).fetch}).reservations.getAvailability({arrival:'2026-10-24',departure:'2026-10-26',adults:2,roomTypeId:'Suite'}));
  test('[P5] HTTP getAvailability: GET /api/v1/availability with query, no Idempotency-Key; server rooms mapped to room numbers / type codes',()=>{
    assert.ok(avail.ok,avail.error?.message);assert.equal(a.calls.length,1);
    assert.equal(a.calls[0].url,'https://hcc.test/api/v1/availability?arrival=2026-10-24&departure=2026-10-26&adults=2&children=0');
    assert.equal(a.calls[0].method,'GET');assert.equal(a.calls[0].headers['Idempotency-Key'],undefined);
    assert.deepEqual(avail.value.rooms.map(r=>[r.roomId,r.roomTypeId,r.available,r.nightlyRatePaise]),[['105','Deluxe',true,200000],['308','Suite',false,450000]]);
    const v=C.availabilityView(avail.value);assert.deepEqual(v.available.map(r=>[r.room.id,r.ratePaise]),[['105',200000]]);
    assert.deepEqual(v.excluded.map(r=>[r.room.id,r.reason,r.reasons]),[['308','Blocked',['Blocked']]]);
    assert.ok(typed.ok);assert.deepEqual(typed.value.rooms.map(r=>r.roomId),['308'],'type codes are filtered on the client');
  });

  const f=fakeFetch([ok(availBody),ok(quoteBody),ok(created)]);
  const flow=await settle(()=>C.quoteAndCreate(C.createHttpClients({fetch:f.fetch}).reservations,{arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0},req,{idempotencyKey:'new:abc'}));
  test('[P5] HTTP quote → create: room resolved to its server id, POST /commands/QuoteStay then /commands/CreateReservation with quoteId+version and Idempotency-Key',()=>{
    assert.ok(flow.ok,flow.error?.message);
    assert.deepEqual(f.calls.map(c=>[c.method,c.url.split('?')[0]]),[['GET','/api/v1/availability'],['POST','/api/v1/commands/QuoteStay'],['POST','/api/v1/commands/CreateReservation']]);
    assert.equal(f.calls[1].headers['Idempotency-Key'],'new:abc:quote');assert.equal(f.calls[2].headers['Idempotency-Key'],'new:abc');
    assert.equal(f.calls[2].headers['Content-Type'],'application/json');
    assert.deepEqual(f.calls[1].body,{arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,roomId:7});
    assert.equal(f.calls[2].body.quoteId,41);assert.equal(f.calls[2].body.quoteVersion,'AAAAAAAAB9E=');assert.equal(f.calls[2].body.roomId,7);assert.deepEqual(f.calls[2].body.guest,req.guest);
    assert.equal(flow.value.reservationId,'1201');assert.equal(flow.value.quote.quoteId,'41');assert.equal(flow.value.quote.roomId,'105');assert.equal(flow.value.quote.totalPaise,440000);
  });
  const detailsBody={reservationId:1201,publicNo:'HCC-1201',version:'AAAAAAAAB9I=',status:'InHouse',guestName:'Journey Test',phone:'+91 90000 11111',roomId:7,roomNo:'105',
    roomTypeId:2,roomType:'Deluxe',arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,source:'Direct',nightlyRatePaise:200000,holdUntil:null,groupBlockId:null,
    restrictionOverride:null,cancelReason:null,notes:null,nights:quoteBody.nights,netPaise:400000,taxPaise:40000,totalPaise:440000};
  const d=fakeFetch([ok(detailsBody)]);const details=await settle(()=>C.createHttpClients({fetch:d.fetch}).reservations.getBooking('1201'));
  test('[P5] HTTP getBooking: GET /api/v1/reservations/{id}; status spelled as the prototype, guest/room/type mapped',()=>{
    assert.ok(details.ok,details.error?.message);assert.equal(d.calls[0].url,'/api/v1/reservations/1201');
    const b=details.value;assert.deepEqual([b.reservationId,b.status,b.guest.name,b.roomId,b.roomTypeId,b.totals.totalPaise],['1201','In house','Journey Test','105','Deluxe',440000]);
  });

  // ── Idempotency-Key reuse on retry ──
  const r1=fakeFetch([netDown,ok(created)]);const conn1=C.createConnection();const states=[];conn1.subscribe(()=>states.push(conn1.get().status));
  const retried=await settle(()=>C.createHttpClients({fetch:r1.fetch,connection:conn1,rooms}).reservations.createReservation({...req,quoteId:'41',quoteVersion:'v1'},{idempotencyKey:'new:retry-1'}));
  const r2=fakeFetch([fail(503,{code:'UNAVAILABLE',message:'Database unavailable'}),ok(created)]);
  const retried503=await settle(()=>C.createHttpClients({fetch:r2.fetch,rooms}).reservations.createReservation({...req,quoteId:'41',quoteVersion:'v1'},{idempotencyKey:'new:retry-2'}));
  const r3=fakeFetch([netDown]);const conn3=C.createConnection();
  const lost=await settle(()=>C.createHttpClients({fetch:r3.fetch,connection:conn3,commandRetries:2,rooms}).reservations.createReservation({...req,quoteId:'41',quoteVersion:'v1'},{idempotencyKey:'new:retry-3'}));
  test('[P5] Retry after a lost request reuses the same Idempotency-Key and identical body; reachability recovers',()=>{
    assert.ok(retried.ok,retried.error?.message);assert.equal(r1.calls.length,2);
    assert.equal(r1.calls[0].headers['Idempotency-Key'],'new:retry-1');assert.equal(r1.calls[1].headers['Idempotency-Key'],'new:retry-1');
    assert.deepEqual(r1.calls[0].body,r1.calls[1].body);assert.deepEqual(states,['unavailable','online']);
    assert.ok(retried503.ok);assert.equal(r2.calls.length,2);assert.equal(r2.calls[1].headers['Idempotency-Key'],'new:retry-2');
  });
  test('[P5] Retries exhausted → UNAVAILABLE (prototype RESULT_UNKNOWN), read-only state, nothing reported as committed',()=>{
    assert.equal(lost.ok,false);assert.equal(r3.calls.length,3);assert.ok(r3.calls.every(c=>c.headers['Idempotency-Key']==='new:retry-3'));
    assert.ok(lost.error instanceof C.HccClientError);assert.equal(lost.error.wireCode,'UNAVAILABLE');assert.equal(lost.error.code,'RESULT_UNKNOWN');
    assert.equal(lost.error.resultUnknown,true);assert.equal(conn3.readOnly,true);assert.match(conn3.get().reason,/nothing was saved/);
  });
  const q1=fakeFetch([netDown,ok(availBody)]);
  const queryOnce=await settle(()=>C.createHttpClients({fetch:q1.fetch}).reservations.getAvailability({arrival:'2026-10-24',departure:'2026-10-26',adults:1}));
  test('[P5] Queries are not retried automatically (a failed read surfaces UNAVAILABLE)',()=>{
    assert.equal(queryOnce.ok,false);assert.equal(q1.calls.length,1);assert.equal(queryOnce.error.wireCode,'UNAVAILABLE');
  });

  // ── Error mapping {code,message,details} → HccError codes ──
  const mapped=[];
  for(const code of C.WIRE_CODES){
    const x=fakeFetch([fail(C.WIRE_STATUS[code],{code,message:'msg '+code,details:{violations:[{code:'MINLOS'}]}})]);
    mapped.push([code,await settle(()=>C.createHttpClients({fetch:x.fetch,commandRetries:0,rooms}).reservations.createReservation({...req,quoteId:'41',quoteVersion:'1'},{idempotencyKey:'k-'+code}))]);
  }
  const expected={VALIDATION:'VALIDATION',FORBIDDEN:'FORBIDDEN',NOT_FOUND:'NOT_FOUND',NOT_AVAILABLE:'NOT_AVAILABLE',RESTRICTED:'RESTRICTED',INVALID_TRANSITION:'INVALID_TRANSITION',
    STALE_VERSION:'STALE_VERSION',DAY_CLOSED:'DAY_CLOSED',APPROVAL_REQUIRED:'FORBIDDEN',IDEMPOTENCY_MISMATCH:'VALIDATION',UNAVAILABLE:'RESULT_UNKNOWN',UNAUTHENTICATED:'FORBIDDEN'};
  test('[P5] Every contract error code maps to the prototype HccError code; message, details and HTTP status kept',()=>{
    assert.equal(mapped.length,12);
    for(const [code,r] of mapped){
      assert.equal(r.ok,false,code);assert.ok(r.error instanceof C.HccClientError,code);
      assert.equal(r.error.wireCode,code);assert.equal(r.error.code,expected[code],code);assert.equal(r.error.message,'msg '+code);
      assert.equal(r.error.httpStatus,C.WIRE_STATUS[code]);assert.deepEqual(r.error.details,{violations:[{code:'MINLOS'}]});
    }
    assert.equal(C.errorText(mapped.find(([c])=>c==='APPROVAL_REQUIRED')[1].error),'Approval required: msg APPROVAL_REQUIRED');
    assert.equal(C.errorText(mapped.find(([c])=>c==='STALE_VERSION')[1].error),'Record changed: msg STALE_VERSION');
  });
  test('[P5] Statuses match HccErrorCatalog; unknown / missing codes fall back by HTTP status',()=>{
    assert.deepEqual(C.WIRE_STATUS,{VALIDATION:422,FORBIDDEN:403,NOT_FOUND:404,NOT_AVAILABLE:409,RESTRICTED:409,INVALID_TRANSITION:409,STALE_VERSION:409,DAY_CLOSED:409,APPROVAL_REQUIRED:403,IDEMPOTENCY_MISMATCH:409,UNAVAILABLE:503,UNAUTHENTICATED:401});
    assert.equal(C.errorFromResponse(401,undefined).wireCode,'UNAUTHENTICATED');
    assert.equal(C.errorFromResponse(500,{code:'BOOM'}).wireCode,'UNAVAILABLE');
    assert.equal(C.errorFromResponse(404,undefined).wireCode,'NOT_FOUND');
    const e=C.errorFromResponse(409,'not json');assert.equal(e.wireCode,'VALIDATION');assert.equal(e.message,'Check the details');
  });

  // ── expectedVersion, key rules ──
  const v=fakeFetch([ok({done:true})]);
  const tr=C.createHttpTransport({fetch:v.fetch});
  await settle(()=>tr.command('AmendReservation',{reservationId:'7f1c'},{idempotencyKey:'amend-1',expectedVersion:'AAAAAAAAB9I='}));
  const longKey=await settle(()=>tr.command('X',{},{idempotencyKey:'k'.repeat(81)}));
  const noKey=await settle(()=>C.createHttpClients({fetch:v.fetch,rooms}).reservations.createReservation({...req,quoteId:'41',quoteVersion:'1'},{}));
  const spaceKey=await settle(()=>tr.command('X',{},{idempotencyKey:'has space'}));
  test('[P5] expectedVersion travels in the command body; Idempotency-Key must be 1–80 visible ASCII and is mandatory for CreateReservation',()=>{
    assert.deepEqual(v.calls[0].body,{reservationId:'7f1c',expectedVersion:'AAAAAAAAB9I='});assert.equal(v.calls[0].headers['Idempotency-Key'],'amend-1');
    assert.equal(v.calls.length,1,'invalid keys never reach the network');
    for(const r of [longKey,noKey,spaceKey]){assert.equal(r.ok,false);assert.equal(r.error.code,'VALIDATION');}
    assert.ok(C.newIdempotencyKey('new').length<=80);assert.notEqual(C.newIdempotencyKey(),C.newIdempotencyKey());
  });

  // ── Safe-integer and date guards ──
  const big=fakeFetch([()=>({status:200,raw:'{"reservationId":1,"roomNo":"105","netPaise":9007199254740993,"taxPaise":0}'})]);
  const bigR=await settle(()=>C.createHttpClients({fetch:big.fetch}).reservations.getBooking('1'));
  const strR=await settle(()=>C.createHttpClients({fetch:fakeFetch([ok({...quoteBody,netPaise:'400000'})]).fetch,rooms}).reservations.quoteStay({arrival:'2026-10-24',departure:'2026-10-26',adults:2,roomId:'105'}));
  const capR=await settle(()=>C.createHttpClients({fetch:fakeFetch([ok({...quoteBody,totalPaise:10_000_000_001})]).fetch,rooms}).reservations.quoteStay({arrival:'2026-10-24',departure:'2026-10-26',adults:2,roomId:'105'}));
  const dateR=await settle(()=>C.createHttpClients({fetch:fakeFetch([ok({...availBody,arrival:'2026-02-30'})]).fetch}).reservations.getAvailability({arrival:'2026-10-24',departure:'2026-10-26',adults:1}));
  const out=fakeFetch([ok({})]);const outR=await settle(()=>C.createHttpTransport({fetch:out.fetch}).command('RecordReceipt',{amountPaise:1.5},{idempotencyKey:'r-1'}));
  const badDate=fakeFetch([ok(availBody)]);const badDateR=await settle(()=>C.createHttpClients({fetch:badDate.fetch}).reservations.getAvailability({arrival:'24/10/2026',departure:'2026-10-26',adults:1}));
  test('[P5] Safe-integer guard: unsafe, string or over-₹10-crore paise in a response → PROTOCOL error, never shown',()=>{
    for(const r of [bigR,strR,capR]){assert.equal(r.ok,false);assert.equal(r.error.wireCode,'PROTOCOL');assert.match(r.error.message,/Paise is not a safe paise amount/);}
    assert.equal(C.isPaise(C.MAX_PAISE),true);assert.equal(C.isPaise(-C.MAX_PAISE),true);assert.equal(C.isPaise(C.MAX_PAISE+1),false);assert.equal(C.isPaise(2**53),false);
  });
  test('[P5] Dates are YYYY-MM-DD calendar dates both ways; invalid request values never reach the network',()=>{
    assert.equal(dateR.ok,false);assert.equal(dateR.error.wireCode,'PROTOCOL');
    assert.equal(outR.ok,false);assert.equal(outR.error.code,'VALIDATION');assert.equal(out.calls.length,0);
    assert.equal(badDateR.ok,false);assert.equal(badDate.calls.length,0);
    assert.equal(C.isIsoDate('2028-02-29'),true);assert.equal(C.isIsoDate('2026-02-29'),false);assert.equal(C.isIsoDate('2026-10-24T00:00'),false);
  });
  const h=fakeFetch([ok({status:'ok',database:'ok',schemaVersion:2,serverTimeUtc:'2026-10-25T04:00:00Z'}),ok({userId:3,propertyId:1,displayName:'Reception',credentialKind:'PIN',roles:['REC'],permissions:[{permissionId:'PERM-RES-001',mode:'A',scope:'P',limit:null,limitUnit:null}],fieldMasks:{},session:{idleExpiresUtc:'2026-10-25T04:30:00.000Z',absoluteExpiresUtc:'2026-10-25T16:00:00.000Z',idleTimeoutSeconds:1800}})]);
  const hc=C.createHttpClients({fetch:h.fetch});const health=await settle(()=>hc.system.health());const caps=await settle(()=>hc.me.capabilities());
  const notJson=await settle(()=>C.createHttpClients({fetch:fakeFetch([()=>({status:200,raw:'<html>proxy</html>'})]).fetch}).system.health());
  test('[P5] SystemClient.health and MeClient.capabilities: GET /api/v1/system/health and /api/v1/me/capabilities; non-JSON → PROTOCOL',()=>{
    assert.deepEqual(h.calls.map(c=>c.url),['/api/v1/system/health','/api/v1/me/capabilities']);
    assert.equal(health.value.schemaVersion,2);assert.deepEqual(caps.value.permissions,['PERM-RES-001']);
    assert.deepEqual([caps.value.userId,caps.value.role,caps.value.grants[0].scope],['3','REC','P']);
    assert.equal(notJson.ok,false);assert.equal(notJson.error.wireCode,'PROTOCOL');
  });

  // ── Mock adapter parity with the direct calls ──
  const s0=M.seed();
  const queries=[{arrival:s0.date,departure:'2026-10-26',adults:2},{arrival:s0.date,departure:'2026-10-27',adults:2,children:2},{arrival:'2026-10-26',departure:'2026-10-28',adults:1,roomTypeId:'Suite'},{arrival:s0.date,departure:s0.date,adults:1}];
  const host=C.createLocalHost(s0,'reception',M.execute);const mc=C.createMockClients(host);
  const views=[];for(const q of queries)views.push([q,C.availabilityView(await mc.reservations.getAvailability(q))]);
  test('[P5] Mock availability = resSearch + rateFor + occupant (same rooms, order, reasons, texts and rates)',()=>{
    for(const [q,v] of views){
      const old=M.resSearch(s0,{arrival:q.arrival,departure:q.departure,people:q.adults,children:q.children,type:q.roomTypeId||'All'});
      assert.equal(v.valid,old.valid);assert.equal(v.party,old.party);
      assert.deepEqual(v.available.map(r=>[r.room.id,r.room.type,r.capacity,r.ratePaise,r.occupiedNow]),old.available.map(r=>[r.room.id,r.room.type,r.capacity,M.rateFor(s0,r.room,q.arrival),!!M.occupant(s0,r.room.id)]));
      assert.deepEqual(v.excluded.map(r=>[r.room.id,r.reason,r.reasons]),old.excluded.map(r=>[r.room.id,r.reason,r.reasons]));
    }
    assert.ok(views[0][1].excluded.some(r=>r.room.id==='308'&&/Blocked/.test(r.reason)));
  });
  const quote=await mc.reservations.quoteStay({arrival:s0.date,departure:'2026-10-26',adults:2,roomId:'105'});
  test('[P5] Mock quote: agreed arrival-night tariff × nights, tax per service date (same figures the folio posts)',()=>{
    const room=s0.rooms.find(r=>r.id==='105'),rate=M.rateFor(s0,room,s0.date);
    assert.equal(quote.nights.length,2);assert.equal(quote.netPaise,rate*2);
    assert.deepEqual(quote.nights.map(n=>n.taxBps),quote.nights.map(n=>M.taxRateFor(s0,n.date)));
    assert.equal(quote.taxPaise,quote.nights.reduce((a,n)=>a+Math.round(rate*n.taxBps/10000),0));assert.equal(quote.totalPaise,quote.netPaise+quote.taxPaise);
    assert.deepEqual(quote.violations,[]);
  });
  // Desktop dialog fields and phone wizard fields (createRequestFromBook) both reach execute() as the same BOOK command.
  const desk={type:'BOOK',guest:'Journey Test',phone:'+91 90000 11111',email:'',people:2,children:0,room:'105',arrival:s0.date,departure:'2026-10-26',source:'OTA',otaChannel:'Demo Travel Hub',
    company:{name:'Demo Industries',gstin:'27AAAAA0000A1Z5'},notes:'Late arrival',hold:false,restrictionOverride:undefined,key:'new:parity-1'};
  const direct=M.execute(s0,'reception',desk);
  const h1=C.createLocalHost(s0,'reception',M.execute);
  const viaClient=await settle(()=>C.quoteAndCreate(C.createMockClients(h1).reservations,{arrival:desk.arrival,departure:desk.departure,adults:2,children:0},C.createRequestFromBook(desk),{idempotencyKey:desk.key}));
  const heldCmd={...desk,source:'Direct',otaChannel:undefined,company:undefined,hold:true,room:'',key:'new:parity-2'};heldCmd.room=views[0][1].available.find(r=>r.room.id!=='105').room.id;
  const directHeld=M.execute(s0,'reception',heldCmd);
  const h2=C.createLocalHost(s0,'reception',M.execute);
  const viaHeld=await settle(()=>C.quoteAndCreate(C.createMockClients(h2).reservations,{arrival:heldCmd.arrival,departure:heldCmd.departure,adults:2,children:0},C.createRequestFromBook(heldCmd),{idempotencyKey:heldCmd.key}));
  test('[P5] Mock createReservation = execute(BOOK): identical resulting state, id and version (confirmed and held)',()=>{
    assert.ok(viaClient.ok,viaClient.error?.message);assert.deepEqual(h1.state(),direct);
    assert.equal(viaClient.value.reservationId,direct.bookings.at(-1).id);assert.equal(viaClient.value.version,String(direct.bookings.at(-1).version));
    assert.ok(viaHeld.ok,viaHeld.error?.message);assert.deepEqual(h2.state(),directHeld);assert.equal(h2.state().bookings.at(-1).status,'Held');
    assert.deepEqual(C.bookCommandFor({...C.createRequestFromBook(desk),quoteId:C.mockQuoteId({roomId:'105',arrival:desk.arrival,departure:desk.departure,adults:2,children:0}),quoteVersion:'1'},desk.key),desk);
  });
  const bk=viaClient.ok?await mc.reservations.getBooking('HCC-1041'):undefined;
  const hb=C.createLocalHost(direct,'reception',M.execute);const newB=await C.createMockClients(hb).reservations.getBooking(direct.bookings.at(-1).id);
  const missing=await settle(()=>mc.reservations.getBooking('HCC-9999'));
  test('[P5] Mock getBooking = booking + totals() (booking details read model)',()=>{
    const b=s0.bookings.find(x=>x.id==='HCC-1041'),t=M.totals(b);
    assert.equal(bk.reservationId,'HCC-1041');assert.equal(bk.guest.name,b.guest);assert.equal(bk.roomId,b.room);assert.equal(bk.status,M.resStatus(s0,b));
    assert.deepEqual(bk.totals,{netPaise:t.net,taxPaise:t.tax,totalPaise:t.total,paidPaise:t.paid,depositPaise:t.deposit,balancePaise:t.balance});
    assert.equal(newB.status,'Confirmed');assert.equal(newB.company.gstin,'27AAAAA0000A1Z5');assert.equal(newB.nightlyRatePaise,quote.nights[0].ratePaise);
    assert.equal(missing.ok,false);assert.equal(missing.error.code,'NOT_FOUND');
  });
  const blocked=await settle(()=>C.quoteAndCreate(C.createMockClients(C.createLocalHost(s0,'reception',M.execute)).reservations,{arrival:s0.date,departure:'2026-10-26',adults:1,children:0},
    {roomId:'308',guest:{name:'Test',phone:''},source:'Direct'},{idempotencyKey:'new:blocked'}));
  let directErr;try{M.execute(s0,'reception',{type:'BOOK',guest:'Test',phone:'',room:'308',arrival:s0.date,departure:'2026-10-26',people:1,children:0,source:'Direct'});}catch(e){directErr=e;}
  const hk=await settle(()=>C.quoteAndCreate(C.createMockClients(C.createLocalHost(s0,'housekeeper',M.execute)).reservations,{arrival:s0.date,departure:'2026-10-26',adults:1,children:0},{roomId:'105',guest:{name:'Test'},source:'Direct'},{idempotencyKey:'new:hk'}));
  const caps2=await C.createMockClients(C.createLocalHost(s0,'reception',M.execute)).me.capabilities();
  const health2=await C.createMockClients(C.createLocalHost(s0,'reception',M.execute)).system.health();
  test('[P5] Mock errors are execute()’s own (code + message); capabilities = role grants; health from the simulated clock',()=>{
    assert.equal(blocked.ok,false);assert.equal(blocked.error.code,directErr.code);assert.equal(blocked.error.message,directErr.message);
    assert.equal(hk.ok,false);assert.equal(hk.error.code,'FORBIDDEN');
    const u=s0.users.find(x=>x.id==='reception');assert.deepEqual(caps2.permissions,s0.grants[u.role]);assert.equal(caps2.businessDate,s0.date);
    assert.equal(health2.schemaVersion,M.SCHEMA);assert.equal(health2.serverTimeUtc.slice(0,10),s0.clock.slice(0,10));
  });

  // ── Build flavours: production = HTTP adapter only, demo controls compiled out; prototype unchanged ──
  const bundle=async flavour=>(await build({entryPoints:['src/main.tsx'],bundle:true,write:false,format:'iife',target:['es2020'],minify:true,logLevel:'error',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':`"${flavour}"`}})).outputFiles[0].text;
  const [proto,prod]=await Promise.all([bundle('prototype'),bundle('production')]);
  const demo=['DEMO CONTROLS','Choose a fixture scenario','Choose a scenario','Reset your demo?','Simulated hotel clock','Open demo controls','Demo controls','Local mock service','hcc-mock-adapter'];
  test('[P5] Production bundle: no demo toolbar, scenario switcher, simulated clock chip, Reset or mock adapter; HTTP adapter present',()=>{
    for(const m of demo)assert.ok(!prod.includes(m),'production bundle contains '+m);
    assert.ok(prod.includes('hcc-http-adapter'));assert.ok(prod.includes('/commands/'));assert.ok(prod.includes('read-only-banner'));
    assert.ok(!/process\.env\.HCC_BUILD/.test(prod),'flag is a compile-time constant');
  });
  test('[P5] Prototype bundle keeps every demo control and the mock adapter, and never contains the HTTP adapter',()=>{
    for(const m of demo)assert.ok(proto.includes(m),'prototype bundle lost '+m);
    assert.ok(!proto.includes('hcc-http-adapter'));assert.ok(!proto.includes('read-only-banner'));assert.ok(!/process\.env\.HCC_BUILD/.test(proto));
  });
}
