// W4-1 front desk (sync/reservations.ts + sync/frontdesk.ts): the bookings list and guests loaders, and every mapped command
// (CONFIRM, CANCEL, no-show, CHANGE_DATES, MOVE, STAY_EARLY_DEPARTURE, REGISTER, CHECKIN, CHECKOUT, GUEST_SAVE) against a fake server shaped like the .NET DTOs.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const item=(id,over={})=>({reservationId:id,publicNo:'HCC-'+id,version:'V'+id+'a',status:'Confirmed',guestName:'Guest '+id,phone:'+91 90000 '+id,roomId:7,roomNo:'105',
  roomTypeId:2,roomType:'Deluxe',arrival:'2026-10-24',departure:'2026-10-26',adults:2,children:0,source:'Direct',nightlyRatePaise:200000,holdUntil:null,groupBlockId:null,
  restrictionOverride:null,cancelReason:null,notes:null,guestId:null,registered:false,stayId:null,stayVersion:null,stayStatus:null,checkedInDate:null,checkedOutDate:null,segments:[],...over});
const guest=(id,over={})=>({guestId:id,name:'Asha Rao',phone:'+91 98200 01111',email:'asha@example.test',address:'1 Demo Street (fictional)',nationality:'Indian',idType:'Passport (demo)',idLast4:'K123',
  company:null,gstin:null,party:[{name:'Om Rao',ageBand:'Child',relation:null}],verified:true,masked:false,version:'GV'+id,idMasked:'XXXX-K123',idOnFile:true,canRevealId:true,
  preferences:'High floor',notes:'Early breakfast',origin:'manual',...over});

export async function register({test,assert,M}){
  const S=await loadSync();
  const store=new Map(),guests=new Map();
  const put=(x)=>store.set(x.reservationId,x);
  put(item(1301,{notes:'Corporate rate · E-mail: lead@corp.test · OTA: Booking.example · Company: Acme Ltd · GSTIN 27AAAAA0000A1Z5'}));
  put(item(1302,{status:'Held',holdUntil:'2026-10-24T06:30:00.000Z',roomId:12,roomNo:'204',arrival:'2026-10-25',departure:'2026-10-27'}));
  put(item(1303,{status:'InHouse',roomId:12,roomNo:'204',arrival:'2026-10-22',departure:'2026-10-25',guestId:9,registered:true,stayId:55,stayVersion:'S55a',stayStatus:'InHouse',checkedInDate:'2026-10-22',
    segments:[{roomNo:'105',from:'2026-10-22',to:'2026-10-23'},{roomNo:'204',from:'2026-10-23',to:null}]}));
  put(item(1304,{status:'Cancelled',cancelReason:'Changed plans',roomId:9,roomNo:'107'}));
  guests.set(9,guest(9));
  const bump=(x,over)=>{const n=Number(x.version.slice(1,5));store.set(x.reservationId,{...x,...over,version:'V'+n+String.fromCharCode(x.version.charCodeAt(5)+1)});return store.get(x.reservationId);};
  const srv=fakeServer({
    'GET /reservations':()=>{const items=[...store.values()];return {businessDate:'2026-10-24',total:items.length,page:1,pageSize:500,items};},
    'GET /guests':()=>{const items=[...guests.values()];return {total:items.length,page:1,pageSize:500,items};},
    'GET /availability':()=>({businessDate:'2026-10-24',arrival:'2026-10-24',departure:'2026-10-26',nights:2,party:1,types:[],
      rooms:[{roomId:7,roomNo:'105',roomTypeId:2,roomType:'Deluxe',capacity:3,available:true,reasons:[],heldForBlock:null,nightlyRatePaise:200000},
        {roomId:15,roomNo:'301',roomTypeId:3,roomType:'Suite',capacity:4,available:true,reasons:[],heldForBlock:null,nightlyRatePaise:400000}]}),
    'POST /commands/ConfirmHold':({body})=>{const x=bump(store.get(body.reservationId),{status:'Confirmed',holdUntil:null});return {reservationId:x.reservationId,publicNo:x.publicNo,version:x.version,status:'Confirmed',holdUntil:null};},
    'POST /commands/CancelReservation':({body})=>{const x=bump(store.get(body.reservationId),{status:'Cancelled',cancelReason:body.reason??null});return {reservationId:x.reservationId,version:x.version,status:'Cancelled'};},
    'POST /commands/MarkNoShow':({body})=>{const x=bump(store.get(body.reservationId),{status:'NoShow'});return {reservationId:x.reservationId,version:x.version,status:'NoShow'};},
    'POST /commands/AmendReservation':({body})=>{const room=body.roomId===12?{roomId:12,roomNo:'204'}:body.roomId===15?{roomId:15,roomNo:'301'}:{roomId:7,roomNo:'105'};
      const x=bump(store.get(body.reservationId),{arrival:body.arrival,departure:body.departure,...room});return {reservationId:x.reservationId,version:x.version,status:x.status};},
    'POST /commands/RegisterGuest':({body})=>{const gid=body.guestId??(guests.size+10);
      if(body.guest)guests.set(gid,guest(gid,{name:body.guest.name,phone:body.guest.phone,address:body.guest.address,idType:body.guest.idType,idLast4:body.guest.idLast4,version:'GV'+gid+'x'}));
      bump(store.get(body.reservationId),{registered:true,guestId:gid});return {reservationId:body.reservationId,guestId:gid,guestVersion:'GV'+gid+'x',verified:true};},
    'POST /commands/CheckIn':({body})=>{const x=bump(store.get(body.reservationId),{status:'InHouse',stayId:77,stayVersion:'S77a',stayStatus:'InHouse',checkedInDate:'2026-10-24',segments:[{roomNo:'105',from:'2026-10-24',to:null}]});
      return {stayId:77,reservationId:x.reservationId,folioId:5,roomId:7,roomNo:'105',businessDate:'2026-10-24',stayVersion:'S77a'};},
    'POST /commands/MoveRoom':({body})=>{const x=store.get(1303);const to=body.toRoomId===7?'105':'204';
      store.set(1303,{...x,roomId:body.toRoomId,roomNo:to,version:'V1303b',stayVersion:'S55b'});return {stayId:body.stayId,reservationId:1303,fromRoomNo:'204',toRoomNo:to,stayVersion:'S55b'};},
    'POST /commands/ExtendStay':({body})=>{const x=store.get(1303);store.set(1303,{...x,departure:body.newDeparture,version:'V1303c',stayVersion:'S55c'});return {stayId:body.stayId,stayVersion:'S55c'};},
    'POST /commands/ShortenStay':({body})=>{const x=store.get(1303);store.set(1303,{...x,departure:body.newDeparture,version:'V1303d',stayVersion:'S55d'});return {stayId:body.stayId,stayVersion:'S55d'};},
    'POST /commands/EarlyDeparture':({body})=>{const x=store.get(1303);store.set(1303,{...x,departure:'2026-10-24',version:'V1303f',stayVersion:'S55f'});return {stayId:body.stayId,stayVersion:'S55f'};},
    'POST /commands/CheckoutStay':({body})=>{const x=store.get(1303);store.set(1303,{...x,status:'Departed',stayStatus:'CheckedOut',version:'V1303e'});return {stayId:body.stayId,stayVersion:'S55e'};},
    'POST /commands/SaveGuestProfile':({body})=>{const created=!body.guestId,gid=body.guestId??(guests.size+20);
      guests.set(gid,guest(gid,{name:body.guest.name,phone:body.guest.phone,verified:!!body.verified,version:'GV'+gid+'s'}));return {guestId:gid,guestVersion:'GV'+gid+'s',verified:!!body.verified,created};},
  });
  const cache=S.createMemoryCache();
  const rt=await startRuntime(S,srv,{cache});
  const st=rt.get().state;
  const bootList=srv.gets('/reservations')[0],bootGuests=srv.gets('/guests').length;

  test('[W4-1 loaders] bookings ← GET /reservations (180-day window, 500 per page): figures, stay facts, notes read back; guests ← GET /guests',()=>{
    assert.deepEqual([bootList.query.view,bootList.query.from,bootList.query.pageSize,bootList.query.page],['range','2026-04-27','500','1']);assert.ok(bootGuests>=1);
    assert.deepEqual(st.bookings.map(b=>[b.id,b.status,b.room]),[['1301','Confirmed','105'],['1302','Held','204'],['1303','In house','204'],['1304','Cancelled','107']]);
    const a=st.bookings[0];
    assert.deepEqual([a.guest,a.phone,a.people,a.children,a.arrival,a.departure,a.rate,a.source,a.registered,a.email,a.otaChannel,a.company,a.notes],
      ['Guest 1301','+91 90000 1301',2,undefined,'2026-10-24','2026-10-26',200000,'Direct',false,'lead@corp.test','Booking.example',{name:'Acme Ltd',gstin:'27AAAAA0000A1Z5'},'Corporate rate']);
    assert.equal(st.bookings[1].holdUntil,'2026-10-24T12:00');
    const c=st.bookings[2];
    assert.deepEqual([c.registered,c.guestId,c.email,c.segments],[true,'9','asha@example.test',[{room:'105',from:'2026-10-22',to:'2026-10-23'},{room:'204',from:'2026-10-23'}]]);
    assert.equal(st.bookings[3].cancelReason,'Changed plans');
    assert.deepEqual([rt.ids.version('booking','1303'),rt.ids.version('stay','1303'),rt.ids.server('stay','1303'),rt.ids.server('room','204'),rt.ids.version('guest','9')],['V1303a','S55a',55,12,'GV9']);
    assert.equal(st.guests.length,1);
    const g=st.guests[0];
    assert.deepEqual([g.id,g.name,g.phone,g.email,g.address,g.idType,g.idLast4,g.preferences,g.notes,g.party,g.verified,g.origin,g.idNumber],
      ['9','Asha Rao','+91 98200 01111','asha@example.test','1 Demo Street (fictional)','Passport (demo)','K123','High floor','Early breakfast',[{name:'Om Rao',ageBand:'Child',relation:''}],true,'manual',undefined]);
  });

  test('[v12fix-b] booking reference: Booking.publicNo carries the server public number (HCC-1301); id stays the internal reservation id; bookingRef/bookingRefOf show it',async()=>{
    assert.deepEqual(st.bookings.map(b=>[b.id,b.publicNo]),[['1301','HCC-1301'],['1302','HCC-1302'],['1303','HCC-1303'],['1304','HCC-1304']]);
    const {bookingRef,bookingRefOf}=await import('../src/domain/bookingRef.ts').catch(()=>({}));
    if(bookingRef){assert.equal(bookingRef(st.bookings[0]),'HCC-1301');assert.equal(bookingRefOf(st.bookings,'1302'),'HCC-1302');assert.equal(bookingRef({id:'HCC-1101'}),'HCC-1101');}
    assert.equal(rt.ids.server('booking','1301'),1301);
  });

  // ── booking commands ──
  srv.reset();
  const confirm=await rt.run({type:'CONFIRM',id:'1302',key:'confirm:1302'});const confirmCalls=srv.calls.map(c=>`${c.method} ${c.path}`);const confirmBody=srv.commands('ConfirmHold')[0];
  srv.reset();
  const cancel=await rt.run({type:'CANCEL',id:'1301',reason:'Guest asked',key:'cancel:1301'});const cancelCall=srv.commands('CancelReservation')[0];
  srv.reset();
  const noshow=await rt.run({type:'CANCEL',id:'1301',noShow:true,key:'noshow:1301'});const noshowCall=srv.commands('MarkNoShow')[0];
  test('[W4-1 commands] CONFIRM → ConfirmHold, CANCEL → CancelReservation (reason), CANCEL noShow → MarkNoShow; versions from the load; bookings reload',()=>{
    assert.ok(confirm.ok,confirm.text);assert.deepEqual(confirmBody.body,{reservationId:1302,expectedVersion:'V1302a'});assert.equal(confirmBody.key,'confirm:1302');
    assert.deepEqual(confirmCalls.filter(x=>x.startsWith('GET')&&!x.startsWith('GET /folios/')),['GET /reservations']);
    assert.equal(rt.get().state.bookings.find(b=>b.id==='1302').status,'Confirmed');assert.equal(rt.get().state.bookings.find(b=>b.id==='1302').holdUntil,undefined);
    assert.ok(cancel.ok,cancel.text);assert.deepEqual(cancelCall.body,{reservationId:1301,reason:'Guest asked',expectedVersion:'V1301a'});assert.equal(cancelCall.key,'cancel:1301');
    // the second action uses the version the reload brought (V1301b)
    assert.ok(noshow.ok,noshow.text);assert.deepEqual(noshowCall.body,{reservationId:1301,expectedVersion:'V1301b'});assert.equal(noshowCall.key,'noshow:1301');
    assert.equal(rt.get().state.bookings.find(b=>b.id==='1301').status,'No show');
  });

  srv.reset();
  const change=await rt.run({type:'CHANGE_DATES',id:'1302',room:'204',arrival:'2026-10-26',departure:'2026-10-28',version:1,restrictionOverride:'Owner agreed',key:'chg:1302'});
  const amend=srv.commands('AmendReservation')[0];const changeGets=srv.gets().map(c=>c.path);
  srv.reset();
  const amendNew=await rt.run({type:'CHANGE_DATES',id:'1302',room:'301',arrival:'2026-10-26',departure:'2026-10-28',key:'chg:1302:b'});
  const amend2=srv.commands('AmendReservation')[0];const avail=srv.gets('/availability').length;
  test('[W4-1 commands] CHANGE_DATES → AmendReservation (priceDecision keep, restriction override, server room id; an unknown room asks GET /availability once)',()=>{
    assert.ok(change.ok,change.text);assert.deepEqual(amend.body,{reservationId:1302,arrival:'2026-10-26',departure:'2026-10-28',roomId:12,priceDecision:'keep',restrictionOverride:'Owner agreed',expectedVersion:'V1302b'});
    assert.ok(!changeGets.includes('/availability'),'room 204 was learned by the bookings loader');
    assert.ok(amendNew.ok,amendNew.text);assert.equal(avail,1);assert.equal(amend2.body.roomId,15);assert.equal(amend2.body.expectedVersion,'V1302c');
    const b=rt.get().state.bookings.find(x=>x.id==='1302');assert.deepEqual([b.room,b.arrival,b.departure],['301','2026-10-26','2026-10-28']);
  });

  // ── registration, check-in, move, check-out ──
  srv.reset();
  const draft={name:'Ravi Kale',phone:'+91 98200 02222',email:'ravi@example.test',address:'2 Demo Road (fictional)',nationality:'Indian',idType:'Passport (demo)',idLast4:'R777',idNumber:'R1234777',party:[{name:'Sia',ageBand:'Child',relation:'Daughter'}],preferences:'',notes:''};
  put(item(1305,{roomId:7,roomNo:'105'}));await rt.refresh();srv.reset();
  const reg=await rt.run({type:'REGISTER',id:'1305',guest:draft,key:'reg:1305'});const regCall=srv.commands('RegisterGuest')[0];
  const regGuestsAfter=rt.get().state.guests.map(g=>g.id);
  srv.reset();
  const regUse=await rt.run({type:'REGISTER',id:'1305',guestId:'9',key:'reg:1305:use'});const regUseCall=srv.commands('RegisterGuest')[0];
  srv.reset();
  const ci=await rt.run({type:'CHECKIN',id:'1305',key:'checkin:1305'});const ciCall=srv.commands('CheckIn')[0];
  test('[W4-1 commands] REGISTER → RegisterGuest (draft with the ID number, or just a chosen guestId); CHECKIN → CheckIn; slices reload',()=>{
    assert.ok(reg.ok,reg.text);assert.equal(regCall.key,'reg:1305');assert.equal(regCall.body.expectedVersion,'V1305a');assert.equal(regCall.body.reservationId,1305);
    assert.deepEqual([regCall.body.guest.name,regCall.body.guest.idNumber,regCall.body.guest.idLast4,regCall.body.guest.party,regCall.body.guestId],['Ravi Kale','R1234777','R777',[{name:'Sia',ageBand:'Child',relation:'Daughter'}],undefined]);
    assert.ok(regGuestsAfter.length===2,'guests reloaded');assert.equal(rt.get().state.bookings.find(b=>b.id==='1305').registered,true);
    assert.ok(regUse.ok,regUse.text);assert.deepEqual(regUseCall.body,{reservationId:1305,guestId:9,expectedVersion:'V1305b'});
    assert.ok(ci.ok,ci.text);assert.deepEqual(ciCall.body,{reservationId:1305,expectedVersion:'V1305c'});assert.equal(ciCall.key,'checkin:1305');
    const b=rt.get().state.bookings.find(x=>x.id==='1305');assert.deepEqual([b.status,b.segments],['In house',[{room:'105',from:'2026-10-24'}]]);assert.equal(rt.ids.server('stay','1305'),77);
  });

  srv.reset();
  const mv=await rt.run({type:'MOVE',id:'1303',room:'105',departure:'2026-10-27',key:'mv:1303'});
  const moveCalls=srv.calls.filter(c=>c.method==='POST').map(c=>c.path+' '+c.key+' '+JSON.stringify(c.body));
  srv.reset();
  const mvShort=await rt.run({type:'MOVE',id:'1303',room:'105',departure:'2026-10-26',reason:'Guest leaves early',key:'mv:1303:s'});
  const shortCall=srv.commands('ShortenStay')[0];
  srv.reset();
  const same=await rt.run({type:'MOVE',id:'1303',room:'105',departure:'2026-10-26',key:'mv:same'});
  const mvConfirmed=await (async()=>{put(item(1306,{roomId:7,roomNo:'105'}));await rt.refresh();srv.reset();const r=await rt.run({type:'MOVE',id:'1306',room:'204',departure:'2026-10-27',key:'mv:1306'});return {r,call:srv.commands('AmendReservation')[0]};})();
  test('[W4-1 commands] MOVE: in house → MoveRoom then ExtendStay with the returned stay version (steps keep keys stable); shorten needs a reason; Confirmed → AmendReservation; no change refused',()=>{
    assert.ok(mv.ok,mv.text);
    assert.deepEqual(moveCalls,['/commands/MoveRoom mv:1303:room {"stayId":55,"toRoomId":7,"reason":"Room move","rateDecision":"keep","expectedVersion":"S55a"}',
      '/commands/ExtendStay mv:1303:depart {"stayId":55,"newDeparture":"2026-10-27","rateDecision":"keep","expectedVersion":"S55b"}']);
    assert.ok(mvShort.ok,mvShort.text);assert.deepEqual(shortCall.body,{stayId:55,newDeparture:'2026-10-26',reason:'Guest leaves early',expectedVersion:'S55c'});
    assert.deepEqual([same.ok,same.reason],[false,'precheck']);assert.match(same.text,/Nothing changed/);
    assert.ok(mvConfirmed.r.ok,mvConfirmed.r.text);assert.deepEqual(mvConfirmed.call.body,{reservationId:1306,arrival:'2026-10-24',departure:'2026-10-27',roomId:12,priceDecision:'keep',expectedVersion:'V1306a'});
    const b=rt.get().state.bookings.find(x=>x.id==='1303');assert.deepEqual([b.room,b.departure],['105','2026-10-26']);
  });

  // A02b-02: "Leaves today" → EarlyDeparture {stayId, reason} with the stay version from the load; no reason is refused before any call.
  srv.reset();
  const edNoReason=await rt.run({type:'STAY_EARLY_DEPARTURE',id:'1303',reason:'  ',key:'ed:none'});const edNoCalls=srv.commands('EarlyDeparture').length;
  const edNotHouse=await rt.run({type:'STAY_EARLY_DEPARTURE',id:'1301',reason:'Guest called away',key:'ed:conf'});
  const edOk=await rt.run({type:'STAY_EARLY_DEPARTURE',id:'1303',reason:' Guest called away ',key:'ed:1303'});const edCall=srv.commands('EarlyDeparture')[0];
  test('[A02b-02] STAY_EARLY_DEPARTURE → EarlyDeparture {stayId, reason, expectedVersion}: reason required (precheck), in-house only, bookings reload with the new departure',()=>{
    assert.deepEqual([edNoReason.ok,edNoReason.reason],[false,'precheck']);assert.match(edNoReason.text,/reason/i);assert.equal(edNoCalls,0);
    assert.deepEqual([edNotHouse.ok,edNotHouse.reason],[false,'precheck']);assert.match(edNotHouse.text,/in-house/i);
    assert.ok(edOk.ok,edOk.text);assert.deepEqual(edCall.body,{stayId:55,reason:'Guest called away',expectedVersion:'S55d'});assert.equal(edCall.key,'ed:1303');
    assert.equal(rt.get().state.bookings.find(b=>b.id==='1303').departure,'2026-10-24');
  });
  srv.reset();
  const out=await rt.run({type:'CHECKOUT',id:'1303',key:'checkout:1303'});const outCall=srv.commands('CheckoutStay')[0];
  test('[W4-1 commands] CHECKOUT → CheckoutStay (stay id + stay version from the load)',()=>{
    assert.ok(out.ok,out.text);assert.deepEqual(outCall.body,{stayId:55,expectedVersion:'S55f'});assert.equal(outCall.key,'checkout:1303');
    assert.equal(rt.get().state.bookings.find(b=>b.id==='1303').status,'Departed');
  });

  // ── guest profile edits ──
  srv.reset();
  const gNew=await rt.run({type:'GUEST_SAVE',guest:{...draft,idNumber:undefined},verified:false,key:'gs:new'});const gNewCall=srv.commands('SaveGuestProfile')[0];
  srv.reset();
  const gEdit=await rt.run({type:'GUEST_SAVE',id:'9',version:1,guest:{...draft,idType:'Restricted',idLast4:'Restricted',idNumber:undefined},verified:true,key:'gs:edit'});const gEditCall=srv.commands('SaveGuestProfile')[0];
  put(item(1307,{roomId:7,roomNo:'105'}));await rt.refresh();srv.reset();
  const gLink=await rt.run({type:'GUEST_SAVE',guest:draft,verified:true,booking:'1307',key:'gs:link'});const gLinkCall=srv.commands('RegisterGuest')[0];
  const maskedTry=await rt.run({type:'GUEST_SAVE',id:'9',guest:{...draft,phone:'••••••1041'},key:'gs:masked'});
  test('[W4-1 commands] GUEST_SAVE → SaveGuestProfile (create / versioned edit; "Restricted" is never sent back); with a booking and verified → RegisterGuest; masked contact refused',()=>{
    assert.ok(gNew.ok,gNew.text);assert.deepEqual([gNewCall.body.verified,gNewCall.body.guestId,gNewCall.body.guest.name,gNewCall.body.guest.idNumber],[false,undefined,'Ravi Kale',undefined]);
    assert.ok(gEdit.ok,gEdit.text);assert.deepEqual([gEditCall.body.guestId,gEditCall.body.guestExpectedVersion,gEditCall.body.verified,gEditCall.body.guest.idType,gEditCall.body.guest.idLast4],[9,'GV9',true,undefined,undefined]);
    assert.ok(gLink.ok,gLink.text);assert.deepEqual([gLinkCall.body.reservationId,gLinkCall.body.expectedVersion,gLinkCall.body.guest.name],[1307,'V1307a','Ravi Kale']);
    assert.deepEqual([maskedTry.ok,maskedTry.reason],[false,'precheck']);assert.match(maskedTry.text,/hidden for your role/);
  });

  // ── refusals, unmapped, pages ──
  srv.reset();
  const stale=await (async()=>{srv.on('POST /commands/CheckIn',()=>reply(409,{code:'STALE_VERSION',message:'This reservation changed.'}));put(item(1308,{roomId:7,roomNo:'105',registered:true,guestId:9}));await rt.refresh();srv.reset();return rt.run({type:'CHECKIN',id:'1308',key:'checkin:1308'});})();
  const idView=await rt.run({type:'GUEST_ID_VIEW',id:'9',key:'idview'});
  const unknown=await rt.run({type:'CONFIRM',id:'9999',key:'confirm:9999'});
  const P=await loadPages();const shown=[P.renderPages(rt.get().state,'1',false),P.renderPages(rt.get().state,'1',true)];
  test('[W4-1 commands] A server refusal (STALE_VERSION) is reported; GUEST_ID_VIEW is unmapped with a clear refusal; an unloaded booking is refused locally; pages render on server data',()=>{
    assert.deepEqual([stale.ok,stale.reason],[false,'server']);assert.match(stale.text,/changed/i);
    assert.deepEqual([idView.ok,idView.reason],[false,'unmapped']);assert.match(idView.text,/Not available on the server yet/);
    assert.deepEqual([unknown.ok,unknown.reason],[false,'server']);assert.match(unknown.text,/not loaded/i);
    assert.deepEqual(shown.map(x=>x.errors),[{},{}]);
    const mapped=['BOOK','CHANGE_DATES','CANCEL','CONFIRM','REGISTER','CHECKIN','CHECKOUT','MOVE','STAY_EARLY_DEPARTURE','GUEST_SAVE'];
    const types=new Set(S.AREAS.flatMap(a=>a.commands.map(c=>c.type)));
    assert.ok(mapped.every(t=>types.has(t)));
    assert.deepEqual(Object.keys(S.AREAS.find(a=>a.id==='frontdesk').unmapped),['GUEST_ID_VIEW']);
  });

  // ── permissions: a housekeeper loads neither bookings nor guests ──
  const hk=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A']]})});
  const hkRt=await startRuntime(S,hk);
  test('[W4-1 loaders] A housekeeper never reads reservations or guests',()=>{
    assert.equal(hk.calls.filter(c=>c.path==='/reservations'||c.path==='/guests').length,0);assert.deepEqual([hkRt.get().state.bookings,hkRt.get().state.guests],[[],[]]);
  });
}
