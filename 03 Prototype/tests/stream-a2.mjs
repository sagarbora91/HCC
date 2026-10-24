// A2.1 · Yield stay restrictions enforced at every stay entry point (BOOK, CHANGE_DATES, group pickup, OTA accept, web request).
// Fixture restrictions: YLDR-1 all types, arrivals 7–9 Nov, minimum 2 nights; YLDR-2 Suite closed to arrival on 8 Nov. Names prefixed [A2].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const code=fn=>{try{fn();return 'none';}catch(e){return e.code??'other';}};
  const room=(s,type)=>s.rooms.find(r=>r.type===type&&!r.blocked).id;
  const book=(s,uid,type,arrival,departure,extra={})=>execute(s,uid,{type:'BOOK',guest:'Restriction Test',room:room(s,type),arrival,departure,people:1,...extra});

  test('[A2] violations helper: min stay and closed-to-arrival, structured codes',()=>{
    const s=seed();
    assert.deepEqual(M.yldStayViolations(s,'Classic','2026-11-07','2026-11-08').map(v=>v.code),['MIN_STAY']);
    assert.deepEqual(M.yldStayViolations(s,'Suite','2026-11-08','2026-11-10').map(v=>v.code),['CTA']);
    assert.deepEqual(M.yldStayViolations(s,'Classic','2026-11-07','2026-11-09'),[]);
    assert.deepEqual(M.yldStayViolations(s,'Classic','2026-11-10','2026-11-11'),[],'outside the arrival range');
    const old={...s};delete old.yieldX;assert.deepEqual(M.yldStayViolations(old,'Classic','2026-11-07','2026-11-08'),[],'no yield slice → no restrictions');
  });
  test('[A2] BOOK: min-stay and CTA rejected with RESTRICTED; permitted stay accepted; state untouched on failure',()=>{
    const s=seed(),copy=JSON.stringify(s);
    assert.equal(code(()=>book(s,'reception','Classic','2026-11-07','2026-11-08')),'RESTRICTED');
    assert.equal(code(()=>book(s,'reception','Suite','2026-11-08','2026-11-10')),'RESTRICTED');
    assert.equal(JSON.stringify(s),copy);
    const n=book(s,'reception','Classic','2026-11-07','2026-11-09');assert.equal(n.bookings.at(-1).status,'Confirmed');
    assert.ok(execute(s,'reception',{type:'BOOK',guest:'October Guest',room:'105',arrival:s.date,departure:'2026-10-25',people:1}),'October bookings unaffected');
  });
  test('[A2] override: approver with reason succeeds and is recorded; reception cannot; short reason rejected',()=>{
    const s=seed();
    assert.equal(code(()=>book(s,'reception','Classic','2026-11-07','2026-11-08',{restrictionOverride:'Regular guest'})),'FORBIDDEN');
    assert.equal(code(()=>book(s,'manager','Classic','2026-11-07','2026-11-08',{restrictionOverride:'x'})),'VALIDATION');
    const n=book(s,'manager','Classic','2026-11-07','2026-11-08',{restrictionOverride:'Regular corporate guest'});
    assert.match(n.bookings.at(-1).notes,/restriction overridden \(MIN_STAY YLDR-1\): Regular corporate guest/);
    assert.match(n.audit[0].detail??JSON.stringify(n.audit[0]),/restriction overridden/);
  });
  test('[A2] CHANGE_DATES into a restricted arrival is rejected; original reservation preserved',()=>{
    let s=book(seed(),'reception','Classic','2026-11-12','2026-11-13');const id=s.bookings.at(-1).id,copy=JSON.stringify(s);
    const r=s.bookings.at(-1).room;
    assert.equal(code(()=>execute(s,'reception',{type:'CHANGE_DATES',id,room:r,arrival:'2026-11-07',departure:'2026-11-08'})),'RESTRICTED');
    assert.equal(JSON.stringify(s),copy);
    const n=execute(s,'reception',{type:'CHANGE_DATES',id,room:r,arrival:'2026-11-07',departure:'2026-11-09'});
    assert.equal(n.bookings.find(b=>b.id===id).arrival,'2026-11-07');
  });
  test('[A2] group pickup inside a restricted window is rejected; approver override passes through',()=>{
    let s=execute(seed(),'owner',{type:'GRP_BLOCK_CREATE',name:'Diwali family group',billTo:'guest',arrival:'2026-11-07',departure:'2026-11-08',cutoff:'2026-11-05',allot:{Classic:1}});
    const blk=M.grpSlice(s).blocks.at(-1).id,rows=[{guest:'Desai Family',type:'Classic',people:2}];
    assert.equal(code(()=>execute(s,'reception',{type:'GRP_ROOMING',block:blk,rows})),'RESTRICTED');
    assert.equal(code(()=>execute(s,'reception',{type:'GRP_ROOMING',block:blk,rows,restrictionOverride:'Contracted group'})),'FORBIDDEN');
    const n=execute(s,'manager',{type:'GRP_ROOMING',block:blk,rows,restrictionOverride:'Contracted group rate'});
    assert.equal(M.grpSlice(n).blocks.at(-1).members.length,1);
  });
  test('[A2] OTA accept and web booking request follow the same restriction',()=>{
    const s=seed();
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_WEB_REQUEST',roomType:'Suite',arrival:'2026-11-08',departure:'2026-11-10',adults:2,children:0,guest:'Web Guest',phone:'9800012345'})),'RESTRICTED');
    const t=structuredClone(s),msg=t.channelsX.messages.find(m=>m.status==='Pending'&&m.kind==='New');
    assert.ok(msg,'fixture has a pending OTA booking');
    msg.arrival='2026-11-07';msg.departure='2026-11-08';
    assert.equal(code(()=>execute(t,'reception',{type:'CHN_ACCEPT',id:msg.id})),'RESTRICTED');
  });
  test('[A2] in-house extension (MOVE) is not restricted — arrival already happened',()=>{
    let s=execute(seed(),'owner',{type:'BOOK',guest:'Long Stayer',room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:1});const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    assert.ok(execute(s,'owner',{type:'MOVE',id,room:'105',departure:'2026-11-08'}));
  });
}
