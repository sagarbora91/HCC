// Stream B tests: guest profiles, registration, check-in, walk-in, in-house moves and segments.
export function register({test,M,assert}){
  const {execute,loadScenario,baseFixture,HccError}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const full={name:'Sonal Kulkarni',phone:'+91 98200 05555',email:'sonal@example.test',address:'5 Demo Lane, Latur (fictional)',nationality:'Indian',idType:'Passport (demo)',idLast4:'K555',preferences:'',notes:'',party:[{name:'Om Kulkarni',ageBand:'Child',relation:'Son'}]};
  const book=(s,room='105',guest='Walk In Guest',dep='2026-10-26')=>{const n=execute(s,'reception',{type:'BOOK',guest,room,arrival:s.date,departure:dep,people:1,source:'Walk-in'});return [n,n.bookings.at(-1).id];};

  test('[B] Fixture: 10 fictional guest profiles linked to base bookings; in-house stays have an open segment',()=>{
    const s=baseFixture();assert.equal(s.guests.length,10);
    for(const b of s.bookings){assert.ok(M.guestById(s,b.guestId),b.id);if(b.status==='In house')assert.deepEqual(b.segments,[{room:b.room,from:b.arrival}]);}
    assert.ok(s.guests.every(g=>/example\.test$/.test(g.email)&&g.idLast4.length===4));
    assert.equal(loadScenario('S1').guests.length,0);
  });

  test('[B] Duplicate detection: same phone (different formatting) and same normalised name',()=>{
    const s=baseFixture();
    const riya=s.guests.find(g=>g.name==='Riya Mehta');
    const m=M.guestDuplicates(s,{id:riya.id,name:riya.name,phone:riya.phone});
    assert.deepEqual(m.map(x=>[x.guest.name,x.reason]),[['Kabir Sen','phone']]);
    assert.deepEqual(M.guestDuplicates(s,{name:'  ananya  RAO (fictional)',phone:''}).map(x=>x.reason),['name']);
    assert.equal(M.guestDuplicates(s,{name:'Nobody Here',phone:'+91 90000 00000'}).length,0);
    assert.equal(M.guestDuplicates(s,{name:'X',phone:'123'}).length,0,'short phone fragments never match');
  });

  test('[B] Masking selector by role: ID "Restricted", phone masked last 4 without checkin permission',()=>{
    const s=baseFixture(),g=s.guests[0];
    const rec=M.guestView(s,'reception',g),csh=M.guestView(s,'cashier',g);
    assert.equal(rec.idLast4,g.idLast4);assert.equal(rec.phone,g.phone);assert.equal(rec.masked,false);
    assert.equal(csh.idLast4,'Restricted');assert.equal(csh.idType,'Restricted');assert.equal(csh.phone,'••••••1041');assert.equal(csh.masked,true);
    assert.notEqual(csh.email,g.email);
    assert.deepEqual(M.guestAccess(s,'housekeeper'),{idDetails:false,contact:false,edit:false});
    // Phone search is not available to roles that cannot see phones.
    assert.equal(M.guestSearch(s,'98200 01041','reception').length,1);assert.equal(M.guestSearch(s,'98200 01041','cashier').length,0);
    assert.equal(M.guestSearch(s,'HCC-1046').length,1);
  });

  test('[B] GUEST_SAVE: validation errors, create, edit with stale version, permission',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,phone:'12'}})),'VALIDATION');
    assert.throws(()=>execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,gstin:'BAD'}}),/GSTIN/);
    assert.throws(()=>execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,idLast4:'123456789012'}}),/last 4/);
    assert.equal(code(()=>execute(s,'cashier',{type:'GUEST_SAVE',guest:full})),'FORBIDDEN');
    s=execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,address:'',idType:'',idLast4:''}});
    const g=s.guests.at(-1);assert.equal(g.name,'Sonal Kulkarni');assert.equal(g.verified,false);assert.equal(g.party.length,1);
    assert.equal(code(()=>execute(s,'reception',{type:'GUEST_SAVE',id:g.id,verified:true,guest:{...full,address:''}})),'VALIDATION');
    s=execute(s,'reception',{type:'GUEST_SAVE',id:g.id,version:1,verified:true,guest:full});
    assert.equal(s.guests.at(-1).verified,true);assert.equal(s.guests.at(-1).version,2);
    assert.equal(code(()=>execute(s,'reception',{type:'GUEST_SAVE',id:g.id,version:1,guest:full})),'STALE_VERSION');
  });

  test('[B] REGISTER with details validates and saves the profile',()=>{
    let [s,id]=book(baseFixture());
    assert.equal(code(()=>execute(s,'reception',{type:'REGISTER',id,guest:{...full,idLast4:''}})),'VALIDATION');
    assert.throws(()=>execute(s,'reception',{type:'REGISTER',id,guest:{...full,address:''}}),/address/);
    const before=JSON.stringify(s);assert.throws(()=>execute(s,'reception',{type:'REGISTER',id,guest:{...full,name:'X'}}));assert.equal(JSON.stringify(s),before);
    s=execute(s,'reception',{type:'REGISTER',id,guest:full});
    const b=bk(s,id),g=M.guestForBooking(s,b);
    assert.equal(b.registered,true);assert.equal(g.name,'Sonal Kulkarni');assert.equal(g.verified,true);assert.equal(g.origin,'manual');
  });

  test('[B] Backward-compatible REGISTER {id}: auto-creates a minimal demo-verified profile; seeded profile reused',()=>{
    let [s,id]=book(baseFixture());const n=s.guests.length;
    s=execute(s,'owner',{type:'REGISTER',id});
    const g=M.guestForBooking(s,bk(s,id));assert.equal(s.guests.length,n+1);assert.equal(g.name,'Walk In Guest');assert.equal(g.verified,true);assert.equal(g.origin,'auto');
    let t=baseFixture();t=execute(t,'owner',{type:'REGISTER',id:'HCC-1049'});assert.equal(t.guests.length,10);assert.equal(M.guestById(t,'G-0009').verified,true);
    assert.equal(code(()=>execute(baseFixture(),'housekeeper',{type:'REGISTER',id:'HCC-1049'})),'FORBIDDEN');
    // S2: booking added by the scenario builder has no profile → auto-created.
    let s2=loadScenario('S2');s2=execute(s2,'reception',{type:'REGISTER',id:'HCC-1051'});assert.equal(M.guestForBooking(s2,bk(s2,'HCC-1051')).origin,'auto');
  });

  test('[B] Check-in denied for a dirty room with same-type alternatives listed; switch via MOVE then check in',()=>{
    let s=loadScenario('S2');const b=bk(s,'HCC-1051');
    const e=M.stayRoomEligibility(s,b);assert.equal(e.ok,false);assert.match(e.reasons.join(' '),/dirty/);
    s=execute(s,'reception',{type:'REGISTER',id:b.id});
    assert.throws(()=>execute(s,'reception',{type:'CHECKIN',id:b.id}),/vacant, ready and unblocked/);
    const alts=M.stayCheckinAlternatives(s,b);
    assert.ok(alts.length>0);assert.ok(alts.every(r=>r.type==='Classic'&&r.hk==='Ready'&&!r.blocked&&r.id!=='108'&&r.id!=='105'));
    s=execute(s,'reception',{type:'MOVE',id:b.id,room:alts[0].id,departure:b.departure});
    assert.equal(M.stayRoomEligibility(s,bk(s,b.id)).ok,true);
    s=execute(s,'reception',{type:'CHECKIN',id:b.id});
    assert.equal(bk(s,b.id).status,'In house');assert.deepEqual(bk(s,b.id).segments,[{room:alts[0].id,from:s.date}]);
    assert.equal(s.rooms.find(r=>r.id==='108').hk,'Dirty','Confirmed-booking move does not touch housekeeping');
  });

  test('[B] Walk-in sequence: BOOK → REGISTER(details) → CHECKIN with own keys; retries are idempotent; partial failure recoverable',()=>{
    let s=baseFixture();
    const cmdBook={type:'BOOK',guest:full.name,room:'106',arrival:s.date,departure:'2026-10-25',people:1,source:'Walk-in',key:'walkin:x:book'};
    s=execute(s,'reception',cmdBook);const id=s.bookings.at(-1).id;
    assert.deepEqual(execute(s,'reception',cmdBook),s,'repeated BOOK key creates nothing');
    // Partial failure: registration missing ID → booking exists, still Confirmed, not registered; retry with details succeeds.
    assert.equal(code(()=>execute(s,'reception',{type:'REGISTER',id,guest:{...full,idLast4:''},key:'walkin:x:register'})),'VALIDATION');
    assert.equal(bk(s,id).status,'Confirmed');assert.equal(bk(s,id).registered,false);assert.ok(!s.processed.includes('walkin:x:register'));
    assert.equal(code(()=>execute(s,'reception',{type:'CHECKIN',id,key:'walkin:x:checkin'})),'VALIDATION');
    s=execute(s,'reception',{type:'REGISTER',id,guest:full,key:'walkin:x:register'});
    s=execute(s,'reception',{type:'CHECKIN',id,key:'walkin:x:checkin'});
    assert.equal(bk(s,id).status,'In house');assert.equal(bk(s,id).source,'Walk-in');assert.equal(M.guestForBooking(s,bk(s,id)).phone,full.phone);
    assert.deepEqual(execute(s,'reception',{type:'CHECKIN',id,key:'walkin:x:checkin'}),s);
  });

  test('[B] In-house move creates dated segments, label, and exactly one HK task for the old room',()=>{
    let s=baseFixture();const tasks=s.tasks.length,b=bk(s,'HCC-1041');
    assert.equal(M.staySegmentLabel(b),'101: 23 Oct–');
    const alts=M.stayMoveAlternatives(s,b);assert.ok(alts.some(r=>r.id==='105'));assert.ok(!alts.some(r=>r.id==='101'||r.blocked||r.hk!=='Ready'));
    s=execute(s,'reception',{type:'MOVE',id:b.id,room:'105',departure:b.departure,key:'mv1'});
    const x=bk(s,b.id);
    assert.deepEqual(x.segments,[{room:'101',from:'2026-10-23',to:'2026-10-24'},{room:'105',from:'2026-10-24'}]);
    assert.equal(M.staySegmentLabel(x),'101: 23–24 Oct → 105: 24 Oct–');
    assert.equal(x.room,'105');assert.equal(s.rooms.find(r=>r.id==='101').hk,'Dirty');
    assert.equal(s.tasks.length,tasks+1);assert.equal(s.tasks.filter(t=>t.room==='101').length,1);
    assert.equal(execute(s,'reception',{type:'MOVE',id:b.id,room:'105',departure:b.departure,key:'mv1'}),s);
    assert.equal(M.occupant(s,'105').id,b.id);assert.equal(M.occupant(s,'101'),undefined);
  });

  test('[B] Failed move leaves state unchanged (conflict, dirty target, blocked target) and lists alternatives',()=>{
    const s=baseFixture(),copy=JSON.stringify(s),b=bk(s,'HCC-1041');
    assert.throws(()=>execute(s,'reception',{type:'MOVE',id:b.id,room:'102',departure:b.departure}),/Target room is unavailable for the requested stay\./);
    assert.equal(code(()=>execute(s,'reception',{type:'MOVE',id:b.id,room:'108',departure:b.departure})),'NOT_AVAILABLE');
    assert.equal(code(()=>execute(s,'reception',{type:'MOVE',id:b.id,room:'308',departure:b.departure})),'NOT_AVAILABLE');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'MOVE',id:b.id,room:'105',departure:b.departure})),'FORBIDDEN');
    assert.equal(JSON.stringify(s),copy);
    // Extension of 101 into a future booking conflicts; alternatives free for [business date, new departure) exclude the conflict.
    let t=execute(s,'reception',{type:'BOOK',guest:'Future Guest',room:'101',arrival:'2026-10-27',departure:'2026-10-29',people:1});
    const tc=JSON.stringify(t);
    assert.equal(M.stayCanExtend(t,bk(t,b.id),'2026-10-28'),false);
    assert.equal(code(()=>execute(t,'reception',{type:'MOVE',id:b.id,room:'101',departure:'2026-10-28'})),'NOT_AVAILABLE');
    assert.equal(JSON.stringify(t),tc);
    const alts=M.stayMoveAlternatives(t,bk(t,b.id),'2026-10-28');assert.ok(alts.length>0&&alts.every(r=>M.available(t,r.id,t.date,'2026-10-28',b.id)));
  });

  test('[B] Shorten bounds: not before business date + 1; posted future nights block shortening',()=>{
    let s=baseFixture();const b=bk(s,'HCC-1041');
    assert.equal(M.stayShortenMin(s),'2026-10-25');assert.deepEqual(M.stayDepartureBounds(s,b),{min:'2026-10-25',max:'2026-11-23'});
    assert.equal(code(()=>execute(s,'reception',{type:'MOVE',id:b.id,room:b.room,departure:'2026-10-24'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'reception',{type:'MOVE',id:b.id,room:b.room,departure:'2026-10-23'})),'VALIDATION');
    const t=execute(s,'reception',{type:'MOVE',id:b.id,room:b.room,departure:'2026-10-25'});
    assert.equal(bk(t,b.id).departure,'2026-10-25');assert.equal(bk(t,b.id).segments.length,1);assert.equal(t.tasks.length,s.tasks.length);
    s=execute(s,'owner',{type:'POST_NIGHTS',id:b.id});
    assert.equal(code(()=>execute(s,'reception',{type:'MOVE',id:b.id,room:b.room,departure:'2026-10-25'})),'INVALID_TRANSITION');
    // Extension on the same room is fine (no future booking).
    assert.equal(bk(execute(baseFixture(),'reception',{type:'MOVE',id:b.id,room:b.room,departure:'2026-10-30'}),b.id).departure,'2026-10-30');
  });

  test('[A02b-02] STAY_EARLY_DEPARTURE: an in-house guest leaves today — departure becomes the business date, a reason is required, then checkout works',()=>{
    const s=baseFixture(),b=bk(s,'HCC-1041'),ev=b.version;
    // the case the audit found: shortening to today is refused, and checkout needs every booked night posted
    assert.equal(code(()=>execute(s,'reception',{type:'MOVE',id:b.id,room:b.room,departure:s.date})),'VALIDATION');
    assert.equal(code(()=>execute(s,'reception',{type:'CHECKOUT',id:b.id})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'reception',{type:'STAY_EARLY_DEPARTURE',id:b.id})),'VALIDATION','a reason is required');
    assert.equal(code(()=>execute(s,'reception',{type:'STAY_EARLY_DEPARTURE',id:b.id,reason:'  '})),'VALIDATION');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'STAY_EARLY_DEPARTURE',id:b.id,reason:'Family emergency'})),'FORBIDDEN');
    let t=execute(s,'reception',{type:'STAY_EARLY_DEPARTURE',id:b.id,reason:'Family emergency',key:'ed1'});
    assert.deepEqual([bk(t,b.id).departure,bk(t,b.id).version,bk(t,b.id).status],[s.date,ev+1,'In house']);
    assert.equal(t.audit[0].detail,`${b.id} · early departure ${b.departure} → ${s.date} (${M.nights(s.date,b.departure)} night(s) released) · Family emergency`);
    assert.equal(execute(t,'reception',{type:'STAY_EARLY_DEPARTURE',id:b.id,reason:'Family emergency',key:'ed1'}),t,'same key = same result');
    assert.equal(code(()=>execute(t,'reception',{type:'STAY_EARLY_DEPARTURE',id:b.id,reason:'Again'})),'INVALID_TRANSITION','no nights left to release');
    assert.ok(M.available(t,b.room,s.date,'2026-10-26',b.id),'the released nights are bookable again');
    const due=M.totals(bk(t,b.id)).balance;
    t=execute(t,'reception',{type:'PAYMENT',id:b.id,amount:due,method:'Cash'});
    t=execute(t,'reception',{type:'CHECKOUT',id:b.id});assert.equal(bk(t,b.id).status,'Departed');
  });

  test('[A02b-02] STAY_EARLY_DEPARTURE is refused on the arrival day, when a night on or after today is posted, and for a stay not in house',()=>{
    const s=baseFixture(),b=bk(s,'HCC-1042');
    const posted=execute(s,'owner',{type:'POST_NIGHTS',id:b.id});
    assert.equal(code(()=>execute(posted,'reception',{type:'STAY_EARLY_DEPARTURE',id:b.id,reason:'Guest leaves'})),'INVALID_TRANSITION');
    const arrivedToday=structuredClone(s);bk(arrivedToday,b.id).arrival=s.date;
    assert.equal(code(()=>execute(arrivedToday,'reception',{type:'STAY_EARLY_DEPARTURE',id:b.id,reason:'Guest leaves'})),'VALIDATION');
    const confirmed=bk(s,'HCC-1049');assert.equal(confirmed.status,'Confirmed');
    assert.equal(code(()=>execute(s,'reception',{type:'STAY_EARLY_DEPARTURE',id:confirmed.id,reason:'Guest leaves'})),'INVALID_TRANSITION');
  });
  test('[B] Arrivals and in-house selectors',()=>{
    const s=baseFixture();assert.deepEqual(M.stayArrivals(s).map(b=>b.id),['HCC-1049','HCC-1050']);assert.equal(M.stayInHouse(s).length,8);
    assert.equal(M.stayArrivals(loadScenario('S0')).length,0);
    assert.ok(M.guestComplete(s.guests[0]));assert.equal(M.guestComplete({name:'A B',phone:'9820000000'}),false);
  });
}
