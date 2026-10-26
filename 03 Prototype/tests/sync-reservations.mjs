// W4-0 reference area (sync/reservations.ts) — the model for every tests/sync-<area>.mjs: fake server routes shaped like the
// .NET API, the real runtime, assertions on the requests (method, path, body, Idempotency-Key) and on the slice the screens read,
// reproducing the prototype's own figures (M = the prototype mock service).
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const availBody={businessDate:'2026-10-24',arrival:'2026-10-24',departure:'2026-10-26',nights:2,party:2,types:[],
  rooms:[{roomId:7,roomNo:'105',roomTypeId:2,roomType:'Deluxe',capacity:3,available:true,reasons:[],heldForBlock:null,nightlyRatePaise:200000}]};
const nights=[{date:'2026-10-24',ratePaise:200000,taxBps:500,taxPaise:10000},{date:'2026-10-25',ratePaise:200000,taxBps:500,taxPaise:10000}];
const quoteBody={quoteId:41,version:'AAAAAAAAB9E=',roomTypeId:2,roomId:7,arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,ratePlanId:'BAR',nights,
  netPaise:400000,taxPaise:20000,totalPaise:420000,expiresAt:'2026-10-24T10:00:00.000Z',violations:[]};
const details=(id,over={})=>({reservationId:id,publicNo:'HCC-'+id,version:'AAAAAAAAB9I=',status:'Confirmed',guestName:'Journey Test',phone:'+91 90000 11111',roomId:7,roomNo:'105',
  roomTypeId:2,roomType:'Deluxe',arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,source:'Direct',nightlyRatePaise:200000,holdUntil:null,groupBlockId:null,
  restrictionOverride:null,cancelReason:null,notes:null,nights,netPaise:400000,taxPaise:20000,totalPaise:420000,
  guestId:null,registered:false,stayId:null,stayVersion:null,stayStatus:null,checkedInDate:null,checkedOutDate:null,segments:[],...over});

export async function register({test,assert,M}){
  const S=await loadSync();
  const store=new Map();let nextId=1201;
  const srv=fakeServer({
    'GET /availability':()=>availBody,
    'POST /commands/QuoteStay':()=>quoteBody,
    'POST /commands/CreateReservation':({body})=>{const id=nextId++;store.set(id,details(id,body.hold?{status:'Held',holdUntil:'2026-10-24T06:30:00.000Z'}:{}));
      return {reservationId:id,publicNo:'HCC-'+id,version:'AAAAAAAAB9I=',status:body.hold?'Held':'Confirmed',holdUntil:null};},
    // W4-1: the list endpoint (GET /reservations) replaced the per-id reads.
    'GET /reservations':()=>{const items=[...store.values()];return {businessDate:'2026-10-24',total:items.length,page:1,pageSize:500,items};},
    'GET /guests':()=>({total:0,page:1,pageSize:500,items:[]}),
  });
  const cache=S.createMemoryCache();
  const rt=await startRuntime(S,srv,{cache});
  const bootGets=srv.calls.filter(c=>c.path==='/reservations').length;const bootQuery=srv.gets('/reservations')[0]?.query;
  srv.reset();
  const cmd={type:'BOOK',guest:'Journey Test',phone:'+91 90000 11111',email:'',people:2,children:0,room:'105',arrival:'2026-10-24',departure:'2026-10-26',source:'Direct',key:'new:sync-1'};
  const r=await rt.run(cmd);const after=rt.get();const seq=srv.calls.map(c=>`${c.method} ${c.path}`);
  const q=srv.commands('QuoteStay')[0],c=srv.commands('CreateReservation')[0];
  const proto=M.execute(M.seed(),'reception',{...cmd,key:undefined});const pb=proto.bookings.at(-1);
  test('[W4-0 reservations] BOOK → GET /availability (room id) → QuoteStay → CreateReservation, keys <action>:quote / <action>, then the GET /reservations list',()=>{
    assert.equal(bootGets,1,'one list read at boot');assert.deepEqual([bootQuery.view,bootQuery.from,bootQuery.pageSize],['range','2026-04-27','500'],'window = 180 days before the business date');
    assert.ok(r.ok,r.text);assert.deepEqual(r.result,{reservationId:'1201',publicNo:'HCC-1201',version:'AAAAAAAAB9I='});
    assert.deepEqual(seq,['GET /availability','POST /commands/QuoteStay','POST /commands/CreateReservation','GET /reservations']);
    assert.equal(q.key,'new:sync-1:quote');assert.equal(c.key,'new:sync-1');
    assert.deepEqual(q.body,{arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,roomId:7});
    assert.deepEqual([c.body.quoteId,c.body.quoteVersion,c.body.roomId,c.body.guest],[41,'AAAAAAAAB9E=',7,{name:'Journey Test',phone:'+91 90000 11111'}]);
  });
  const P=await loadPages();const shown=[P.renderPages(after.state,'1',false),P.renderPages(after.state,'1',true)];
  test('[W4-0 reservations] The bookings slice is the server read mapped to the prototype Booking — same figures as execute(BOOK) in the prototype',()=>{
    const b=after.state.bookings[0];assert.equal(after.state.bookings.length,1);
    assert.deepEqual([b.id,b.guest,b.phone,b.room,b.arrival,b.departure,b.people,b.children,b.source,b.status,b.rate,b.registered],
      ['1201','Journey Test','+91 90000 11111','105','2026-10-24','2026-10-26',2,undefined,'Direct','Confirmed',200000,false]);
    assert.deepEqual([pb.guest,pb.room,pb.arrival,pb.departure,pb.people,pb.rate,pb.status,pb.source],[b.guest,b.room,b.arrival,b.departure,b.people,b.rate,b.status,b.source]);
    assert.deepEqual(b.lines,[],'folio lines come from the money area (patch loader), not from here');
    assert.equal(rt.ids.version('booking','1201'),'AAAAAAAAB9I=');
    assert.deepEqual(shown.map(x=>x.errors),[{},{}],'every desktop and phone page renders with the loaded booking');
  });

  srv.reset();const refused=await rt.run({...cmd,guest:'J',key:'new:sync-2'});const refusedCalls=srv.calls.length;
  // Wave 1 dialogs run the same flow through the typed client and report it (useReservations().commit → flowDone).
  await rt.run({...cmd,hold:true,key:'new:sync-3'});
  srv.reset();
  await rt.flowDone('BOOK',{reservationId:'1203',publicNo:'HCC-1203',version:'v'});
  const flowGets=srv.calls.map(c=>`${c.method} ${c.path}`);
  store.set(1203,details(1203,{status:'InHouse',guestName:'Walk In'}));
  store.delete(1201);
  await rt.refresh();const refreshed=rt.get().state.bookings;
  test('[W4-0 reservations] Precheck refuses an invalid BOOK without a request; held booking keeps its hold time (IST clock); typed-client flows are tracked and reloaded',()=>{
    assert.deepEqual([refused.ok,refused.reason],[false,'precheck']);assert.match(refused.text,/guest’s name/);assert.equal(refusedCalls,0);
    assert.ok(flowGets.includes('GET /reservations'),flowGets.join(','));
    assert.deepEqual(refreshed.map(b=>[b.id,b.status,b.guest]),[['1202','Held','Journey Test'],['1203','In house','Walk In']],'deleted 1201 dropped; InHouse → "In house"');
    assert.equal(refreshed[0].holdUntil,'2026-10-24T12:00');
  });

  // Read-only: the last bookings stay visible from the cache on a restart without the server.
  srv.down=true;
  const offline=await startRuntime(S,srv,{cache});const os=offline.get();
  const ro=await offline.run({...cmd,key:'new:sync-4'});
  const rec=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A']]})});
  const hk=await startRuntime(S,rec);
  test('[W4-0 reservations] Offline restart shows the cached bookings read-only and refuses BOOK; a housekeeper never loads bookings',()=>{
    assert.equal(os.readOnly,true);assert.deepEqual(os.state.bookings.map(b=>b.id),['1202','1203']);
    assert.deepEqual([ro.ok,ro.reason],[false,'read-only']);
    assert.equal(rec.calls.filter(c=>c.path.startsWith('/reservations')).length,0);assert.deepEqual(hk.get().state.bookings,[]);
  });
}
