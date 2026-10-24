// Stream D tests: dated blocks, housekeeping completeness, repair handoff, stayover tasks, backup simulation.
export function register({test,M,assert}){
  const {execute,loadScenario,baseFixture,blockedDuring,available,HccError,hkBackupHistory,hkLastBackup,hkStayoverRooms,hkExpireBlocks,hkGuests,HK_ROADMAP}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const room=(o)=>({id:'X',floor:1,type:'Classic',rate:1,hk:'Ready',blocked:true,...o});
  const finish=(s,uid,id)=>{s=execute(s,uid,{type:'TASK',id,action:'start'});for(let i=0;i<3;i++)s=execute(s,uid,{type:'TASK',id,action:'check',index:i});return execute(s,uid,{type:'TASK',id,action:'complete'});};

  test('[D] blockedDuring: interval [from,to) overlap, back-to-back, open-ended, undated, unblocked',()=>{
    const r=room({blockFrom:'2026-10-25',blockTo:'2026-10-27'});
    assert.equal(blockedDuring(r,'2026-10-24','2026-10-25'),false,'stay ending on block start is back-to-back');
    assert.equal(blockedDuring(r,'2026-10-27','2026-10-29'),false,'stay starting on block end is back-to-back');
    assert.equal(blockedDuring(r,'2026-10-24','2026-10-26'),true);
    assert.equal(blockedDuring(r,'2026-10-26','2026-10-28'),true);
    assert.equal(blockedDuring(r,'2026-10-20','2026-10-30'),true,'stay containing the block');
    const open=room({blockFrom:'2026-10-25'});
    assert.equal(blockedDuring(open,'2026-10-24','2026-10-25'),false);assert.equal(blockedDuring(open,'2027-03-01','2027-03-02'),true,'open-ended');
    const untilOnly=room({blockTo:'2026-10-26'});
    assert.equal(blockedDuring(untilOnly,'2026-10-24','2026-10-25'),true);assert.equal(blockedDuring(untilOnly,'2026-10-26','2026-10-27'),false);
    assert.equal(blockedDuring(room({}),'2030-01-01','2030-01-02'),true,'blocked with no dates blocks everything (base 308)');
    assert.equal(blockedDuring(room({blocked:false,blockFrom:'2026-10-25'}),'2026-10-25','2026-10-26'),false);
  });

  test('[D] Dated block lets bookings outside its dates through; overlapping booking rejected',()=>{
    let s=loadScenario('S1');
    s=execute(s,'supervisor',{type:'BLOCK',id:'105',reason:'AC compressor replacement',from:'2026-10-26',to:'2026-10-28'});
    const r=s.rooms.find(r=>r.id==='105');assert.equal(r.blocked,true);assert.equal(r.blockReason,'AC compressor replacement');
    assert.equal(available(s,'105','2026-10-24','2026-10-26'),true);
    assert.equal(available(s,'105','2026-10-28','2026-10-30'),true);
    assert.equal(available(s,'105','2026-10-25','2026-10-27'),false);
    s=execute(s,'reception',{type:'BOOK',guest:'Before Block',room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:1});
    assert.equal(code(()=>execute(s,'reception',{type:'BOOK',guest:'During Block',room:'105',arrival:'2026-10-27',departure:'2026-10-29',people:1})),'NOT_AVAILABLE');
  });

  test('[D] BLOCK overlapping an active/future allocation rejected with M1 message; non-overlapping allowed',()=>{
    let s=baseFixture(); // 101 in house 23→26; 103 confirmed arrives today → 26
    assert.throws(()=>execute(s,'supervisor',{type:'BLOCK',id:'101',reason:'Leak',from:'2026-10-25',to:'2026-10-27'}),/Resolve active and future allocations before blocking this room/);
    assert.equal(code(()=>execute(s,'supervisor',{type:'BLOCK',id:'103',reason:'Leak'})),'NOT_AVAILABLE','open-ended from today overlaps the arrival');
    const ok=execute(s,'supervisor',{type:'BLOCK',id:'101',reason:'Repaint after checkout',from:'2026-10-26',to:'2026-10-28'});
    assert.equal(ok.rooms.find(r=>r.id==='101').blockFrom,'2026-10-26');
    // Expired Held booking does not hold inventory; active Held does.
    s=baseFixture();s.bookings.push({...s.bookings[0],id:'HELD-1',room:'106',status:'Held',arrival:'2026-10-25',departure:'2026-10-26',holdUntil:'2026-10-24T12:00'});
    assert.equal(code(()=>execute(s,'supervisor',{type:'BLOCK',id:'106',reason:'Repair'})),'NOT_AVAILABLE');
    s.clock='2026-10-24T13:00';assert.equal(code(()=>execute(s,'supervisor',{type:'BLOCK',id:'106',reason:'Repair'})),'NO_ERROR');
  });

  test('[D] BLOCK validation, permissions, UNBLOCK and legacy toggle',()=>{
    const s=loadScenario('S1');
    assert.equal(code(()=>execute(s,'supervisor',{type:'BLOCK',id:'105'})),'VALIDATION','reason required for a new block');
    assert.equal(code(()=>execute(s,'supervisor',{type:'BLOCK',id:'105',reason:'Leak',from:'2026-10-20'})),'VALIDATION','cannot start in the past');
    assert.equal(code(()=>execute(s,'supervisor',{type:'BLOCK',id:'105',reason:'Leak',from:'2026-10-26',to:'2026-10-26'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'BLOCK',id:'105',reason:'Leak'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'supervisor',{type:'BLOCK',id:'999',reason:'Leak'})),'NOT_FOUND');
    let x=execute(s,'supervisor',{type:'BLOCK',id:'105',reason:'Leak'});
    assert.equal(code(()=>execute(x,'housekeeper',{type:'UNBLOCK',id:'105'})),'FORBIDDEN');
    const u=execute(x,'supervisor',{type:'UNBLOCK',id:'105'});const r=u.rooms.find(r=>r.id==='105');
    assert.equal(r.blocked,false);assert.equal(r.blockReason,undefined);assert.equal(r.blockFrom,undefined);
    assert.equal(code(()=>execute(u,'supervisor',{type:'UNBLOCK',id:'105'})),'INVALID_TRANSITION');
    const legacy=execute(x,'supervisor',{type:'BLOCK',id:'105'});assert.equal(legacy.rooms.find(r=>r.id==='105').blocked,false,'BLOCK{id} on a blocked room = M1 toggle (unblock)');
  });

  test('[D] Expired dated block is released by hkExpireBlocks (for NEXT_DAY)',()=>{
    const s=loadScenario('S1');const r=s.rooms.find(r=>r.id==='105');Object.assign(r,{blocked:true,blockReason:'X',blockFrom:'2026-10-22',blockTo:'2026-10-24'});
    const r2=s.rooms.find(r=>r.id==='106');Object.assign(r2,{blocked:true,blockReason:'Y',blockFrom:'2026-10-24',blockTo:'2026-10-25'});
    assert.deepEqual(hkExpireBlocks(s),['105']);assert.equal(r.blocked,false);assert.equal(r2.blocked,true);
  });

  test('[D] Inspection reject requires a reason, stored on the task',()=>{
    let s=baseFixture();const t=s.tasks.find(t=>t.status==='Inspection');
    assert.equal(code(()=>execute(s,'supervisor',{type:'TASK',id:t.id,action:'reject'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'supervisor',{type:'TASK',id:t.id,action:'reject',reason:'   '})),'VALIDATION');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'TASK',id:t.id,action:'reject',reason:'Dusty'})),'FORBIDDEN','housekeeper cannot reject');
    s=execute(s,'supervisor',{type:'TASK',id:t.id,action:'reject',reason:'Mirror streaks in bathroom'});
    const x=s.tasks.find(x=>x.id===t.id);assert.equal(x.status,'In progress');assert.equal(x.rejectReason,'Mirror streaks in bathroom');assert.deepEqual(x.checked,[false,false,false]);
    assert.equal(s.rooms.find(r=>r.id===t.room).hk,'Cleaning');assert.match(s.audit[0].detail,/Mirror streaks/);
  });

  test('[D] Stayover generation: idempotent, one per in-house room, readiness unchanged, no inspection',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'housekeeper',{type:'STAYOVER_GENERATE'})),'FORBIDDEN');
    const rooms=hkStayoverRooms(s);assert.equal(rooms.length,8);
    const hkBefore=JSON.stringify(s.rooms.map(r=>r.hk));
    s=execute(s,'supervisor',{type:'STAYOVER_GENERATE'});
    const stay=s.tasks.filter(t=>t.kind==='stayover');assert.equal(stay.length,8);
    assert.ok(stay.every(t=>t.id===`stayover:${t.room}:${s.date}`));
    const again=execute(s,'supervisor',{type:'STAYOVER_GENERATE'});
    assert.equal(again.tasks.filter(t=>t.kind==='stayover').length,8,'second run creates nothing');
    const t=stay[0];s=finish(s,t.assignee,t.id);
    assert.equal(s.tasks.find(x=>x.id===t.id).status,'Done','stayover completes without inspection');
    assert.equal(JSON.stringify(s.rooms.map(r=>r.hk)),hkBefore,'readiness unchanged');
    assert.equal(M.roomStatus(s,s.rooms.find(r=>r.id===t.room)),'Occupied');
  });

  test('[D] Repair request goes to Maintenance; housekeeper cannot start it; completion keeps block',()=>{
    let s=baseFixture();const t=s.tasks.find(t=>t.assignee==='housekeeper'&&t.status==='To do');
    assert.equal(code(()=>execute(s,'accountant',{type:'REPAIR_REQUEST',room:t.room,reason:'Tap leaking'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'REPAIR_REQUEST',room:t.room,reason:''})),'VALIDATION');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'REPAIR_REQUEST',room:'101',reason:'Tap leaking'})),'FORBIDDEN','not an assigned room');
    s=execute(s,'housekeeper',{type:'REPAIR_REQUEST',room:t.room,reason:'Tap leaking'});
    const rep=s.tasks.at(-1);assert.equal(rep.kind,'repair');assert.equal(rep.assignee,'maint');assert.equal(rep.note,'Tap leaking');
    assert.equal(code(()=>execute(s,'reception',{type:'REPAIR_REQUEST',room:t.room,reason:'Again'})),'INVALID_TRANSITION','one open repair per room');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'TASK',id:rep.id,action:'start'})),'FORBIDDEN');
    assert.throws(()=>execute(s,'housekeeper',{type:'TASK',id:rep.id,action:'start'}),/assigned tasks/);
    s=execute(s,'supervisor',{type:'BLOCK',id:t.room,reason:'Tap leaking'});
    s=execute(s,'maint',{type:'TASK',id:rep.id,action:'start'});
    assert.equal(code(()=>execute(s,'maint',{type:'TASK',id:rep.id,action:'check',index:0})),'VALIDATION');
    s=execute(s,'maint',{type:'TASK',id:rep.id,action:'complete'});
    assert.equal(s.tasks.find(x=>x.id===rep.id).status,'Done');assert.equal(s.rooms.find(r=>r.id===t.room).blocked,true,'repair completion does not unblock');
    assert.equal(code(()=>execute(s,'maint',{type:'UNBLOCK',id:t.room})),'FORBIDDEN');
    assert.equal(execute(s,'supervisor',{type:'UNBLOCK',id:t.room}).rooms.find(r=>r.id===t.room).blocked,false);
    // Base fixture: Maintenance has its own repair task for 308.
    const base=baseFixture();assert.equal(base.tasks.find(t=>t.id==='repair-308').assignee,'maint');
    assert.equal(code(()=>execute(base,'maint',{type:'TASK',id:'repair-308',action:'start'})),'NO_ERROR');
    assert.equal(code(()=>execute(base,'supervisor',{type:'TASK',id:'repair-308',action:'assign',assignee:'housekeeper'})),'VALIDATION','repair tasks go to maintenance users');
  });

  test('[D] Inspection keeps a dated block',()=>{
    let s=baseFixture();const t=s.tasks.find(t=>t.status==='Inspection');
    s=execute(s,'supervisor',{type:'BLOCK',id:t.room,reason:'Window seal',from:s.date,to:'2026-10-27'});
    s=execute(s,'supervisor',{type:'TASK',id:t.id,action:'inspect'});
    const r=s.rooms.find(r=>r.id===t.room);assert.equal(r.hk,'Ready');assert.equal(r.blocked,true);assert.equal(r.blockTo,'2026-10-27');
    assert.equal(available(s,t.room,s.date,'2026-10-25'),false);
  });

  test('[D] Checkout clean: start → checklist → complete with timestamps; inspection policy off → Ready',()=>{
    let s=baseFixture();const t=s.tasks.find(t=>t.assignee==='housekeeper'&&t.status==='To do'&&t.kind==='checkout');
    s=finish(s,'housekeeper',t.id);const x=s.tasks.find(x=>x.id===t.id);assert.ok(x.startedAt&&x.completedAt);assert.equal(x.status,'Inspection');
    let off=baseFixture();off.settings.inspection=false;off=finish(off,'housekeeper',t.id);assert.equal(off.rooms.find(r=>r.id===t.room).hk,'Ready');
  });

  test('[D] Guest view for HK has no financial fields',()=>{
    const g=hkGuests(baseFixture(),'101');assert.equal(g.current.guest,'Ananya Rao');
    for(const k of ['rate','lines','receipts','email'])assert.equal(k in g.current,false);
  });

  test('[D] BACKUP_SIMULATE requires admin and only appends audit',()=>{
    const s=baseFixture();
    for(const u of ['manager','reception','supervisor'])assert.equal(code(()=>execute(s,u,{type:'BACKUP_SIMULATE'})),'FORBIDDEN');
    const x=execute(s,'techadmin',{type:'BACKUP_SIMULATE'});
    assert.equal(x.audit.length,s.audit.length+1);assert.equal(x.audit[0].action,'BACKUP SIMULATE');
    const strip=v=>({...v,audit:[],clock:'',sequence:0});assert.deepEqual(strip(x),strip(s));
    assert.equal(hkLastBackup(x).kind,'Manual');assert.equal(hkLastBackup(s).at,'2026-10-24T02:00');
    assert.ok(hkBackupHistory(s).some(r=>r.result==='Failed'));
  });

  test('[D] Roadmap lists areas 13–26',()=>{assert.deepEqual(HK_ROADMAP.map(r=>Number(r.no)),Array.from({length:14},(_,i)=>i+13));});
}
