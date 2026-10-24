// Module 15 · Maintenance — domain/command tests. Names prefixed [MNT].
export function register({test,M,assert}){
  const {execute,baseFixture,loadScenario,HccError}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const x=s=>M.mntSlice(s);
  const w=(s,id)=>x(s).orders.find(o=>o.id===id);
  const last=s=>x(s).orders.at(-1);

  test('[MNT] Fixture: register, AMCs, spares, plans and work orders seeded; core figures unchanged',()=>{
    const s=baseFixture(),m=x(s);
    assert.equal(m.assets.length,17);assert.equal(m.vendors.length,4);assert.equal(m.amcs.length,3);assert.equal(m.spares.length,9);assert.equal(m.plans.length,7);
    assert.deepEqual(M.mntSummary(s),{open:1,inProgress:1,onHold:1,done:1,verified:4,breached:2});
    assert.equal(M.metrics(s).occupied,8);assert.equal(M.drawer(s),1075000);
    assert.equal(M.mntAmcStatus(m.amcs.find(a=>a.id==='AMC-03'),s.date),'Expiring');
    assert.deepEqual(M.mntLowSpares(s).map(x=>x.id).sort(),['SP-02','SP-03','SP-09']);
    assert.deepEqual(M.mntPendingRepairs(s).map(t=>t.id),['repair-308']);
    assert.equal(loadScenario('S1').maintenanceX.orders.length,0);
  });
  test('[MNT] Absent slice (old save) reads as empty and commands still work',()=>{
    let s=baseFixture();delete s.maintenanceX;
    assert.equal(M.mntSlice(s).orders.length,0);assert.equal(M.mntMttr(s),null);assert.equal(M.mntPmCompliance(s).pct,null);
    s=execute(s,'supervisor',{type:'MNT_WO_CREATE',title:'Door closer loose',priority:'P4',room:'105'});
    assert.equal(s.maintenanceX.orders.length,1);
  });
  test('[MNT] SLA, MTTR, PM compliance, ages and downtime derive from fixture data',()=>{
    const s=baseFixture();
    assert.equal(M.mntSla(w(s,'WO-0901'),s.clock),'Breached');   // P2 created 22 Oct 19:10, still on hold
    assert.equal(M.mntSla(w(s,'WO-0902'),s.clock),'On track');   // P1 created 07:20, clock 08:00
    assert.equal(M.mntSla(w(s,'WO-0904'),s.clock),'Met');
    assert.equal(M.mntSla(w(s,'WO-0905'),s.clock),'Met');
    // MTTR = mean(13h55, 5h, 4h30) for corrective WOs with doneAt
    assert.equal(M.mntMttr(s),Math.round((835+300+270)/3));
    assert.deepEqual(M.mntPmCompliance(s),{due:2,onTime:1,pct:50});
    assert.deepEqual(M.mntAgeBuckets(s).map(b=>b.count),[2,1,0,0]);
    assert.deepEqual(M.mntDowntimeByRoom(s),[{room:'203',minutes:270,orders:1,running:false}]);
    assert.equal(M.mntDuration(835),'13 h 55 min');assert.equal(M.mntDuration(1500),'1 d 1 h');
  });
  test('[MNT] Repair request → work order → start → hold → part → resume → complete → verify; repair task follows',()=>{
    let s=baseFixture();
    s=execute(s,'supervisor',{type:'MNT_WO_FROM_REPAIR',taskId:'repair-308',priority:'P2'});
    const id=last(s).id;
    assert.equal(w(s,id).repairTaskId,'repair-308');assert.equal(w(s,id).assignee,'maint');assert.equal(w(s,id).room,'308');
    assert.ok(w(s,id).downtimeFrom,'308 is blocked → downtime runs');
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_WO_FROM_REPAIR',taskId:'repair-308'})),'INVALID_TRANSITION');
    assert.equal(M.mntPendingRepairs(s).length,0);
    s=execute(s,'maint',{type:'MNT_WO_START',id});
    assert.equal(w(s,id).status,'In progress');assert.equal(s.tasks.find(t=>t.id==='repair-308').status,'In progress');
    s=execute(s,'maint',{type:'MNT_WO_HOLD',id,reason:'Basin trap on order',spareId:'SP-07'});
    assert.equal(w(s,id).status,'On hold');
    const before=x(s).spares.find(p=>p.id==='SP-07').stock;
    s=execute(s,'maint',{type:'MNT_WO_PART',id,spareId:'SP-07',qty:2});
    assert.equal(x(s).spares.find(p=>p.id==='SP-07').stock,before-2);assert.equal(M.mntPartsCost(w(s,id)),8000);assert.equal(w(s,id).awaitingSpare,undefined);
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_WO_COMPLETE',id,note:'Fixed'})),'INVALID_TRANSITION'); // on hold
    s=execute(s,'maint',{type:'MNT_WO_START',id});
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_WO_COMPLETE',id,note:''})),'VALIDATION');
    s=execute(s,'maint',{type:'MNT_WO_COMPLETE',id,note:'Basin trap replaced; no leak after 10 min'});
    assert.equal(w(s,id).status,'Done');assert.equal(s.tasks.find(t=>t.id==='repair-308').status,'Done');
    assert.equal(s.rooms.find(r=>r.id==='308').blocked,true,'completion never unblocks');
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_WO_VERIFY',id,approve:true})),'FORBIDDEN');
    s=execute(s,'supervisor',{type:'MNT_WO_VERIFY',id,approve:true,unblock:true});
    assert.equal(w(s,id).status,'Verified');assert.equal(s.rooms.find(r=>r.id==='308').blocked,false);assert.ok(w(s,id).downtimeTo);
    assert.ok(M.mntDowntimeByRoom(s).some(r=>r.room==='308'&&!r.running));
  });
  test('[MNT] Verification: return with reason; no self-verification even for a manager who did the work',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_WO_VERIFY',id:'WO-0904',approve:false})),'VALIDATION');
    s=execute(s,'supervisor',{type:'MNT_WO_VERIFY',id:'WO-0904',approve:false,reason:'Channel 5 still blank'});
    assert.equal(w(s,'WO-0904').status,'In progress');assert.equal(w(s,'WO-0904').doneAt,undefined);
    s=execute(s,'manager',{type:'MNT_WO_COMPLETE',id:'WO-0904',note:'Retuned channels'});
    assert.equal(code(()=>execute(s,'manager',{type:'MNT_WO_VERIFY',id:'WO-0904',approve:true})),'FORBIDDEN');
    s=execute(s,'owner',{type:'MNT_WO_VERIFY',id:'WO-0904',approve:true});
    assert.equal(w(s,'WO-0904').status,'Verified');
    assert.equal(code(()=>execute(s,'owner',{type:'MNT_WO_VERIFY',id:'WO-0904',approve:true})),'INVALID_TRANSITION');
  });
  test('[MNT] Technician scope: own work orders only, cannot assign/verify/block/cancel; housekeeper cannot create',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_WO_START',id:'WO-0903'})),'FORBIDDEN'); // unassigned
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_WO_ASSIGN',id:'WO-0903',assignee:'maint'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_WO_BLOCK',id:'WO-0903'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_WO_CANCEL',id:'WO-0903',reason:'Not needed'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'MNT_WO_CREATE',title:'Tap leak',priority:'P3',room:'105'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'MNT_WO_START',id:'WO-0902'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'reception',{type:'MNT_WO_CREATE',title:'Tap leak',priority:'P3',room:'105'})),'FORBIDDEN'); // no housekeeping
    // Technician raises a fault: always self-assigned, even if another assignee is passed
    s=execute(s,'maint',{type:'MNT_WO_CREATE',title:'Corridor light flickering',priority:'P4',area:'2nd floor corridor',assignee:'nobody'});
    assert.equal(last(s).assignee,'maint');
    const vis=M.mntVisibleOrders(s,'maint');assert.ok(vis.every(o=>o.assignee==='maint'));assert.ok(!vis.some(o=>o.id==='WO-0903'));
    assert.equal(M.mntVisibleOrders(s,'supervisor').length,x(s).orders.length);
    assert.equal(M.mntCanSeeCost(s,'maint'),false);assert.equal(M.mntCanSeeCost(s,'supervisor'),false);assert.equal(M.mntCanSeeCost(s,'manager'),true);
  });
  test('[MNT] Assign + start rules; asset marked faulty then working after verification',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_WO_START',id:'WO-0903'})),'INVALID_TRANSITION'); // unassigned
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_WO_ASSIGN',id:'WO-0903',assignee:'housekeeper'})),'VALIDATION');
    s=execute(s,'supervisor',{type:'MNT_WO_ASSIGN',id:'WO-0903',vendorId:'VEN-04'});
    assert.equal(w(s,'WO-0903').vendorId,'VEN-04');
    s=execute(s,'supervisor',{type:'MNT_WO_ASSIGN',id:'WO-0903',assignee:'maint'});
    assert.equal(w(s,'WO-0903').vendorId,undefined);
    s=execute(s,'maint',{type:'MNT_WO_START',id:'WO-0903'});
    s=execute(s,'maint',{type:'MNT_WO_PART',id:'WO-0903',spareId:'SP-04',qty:1});
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_WO_PART',id:'WO-0903',spareId:'SP-03',qty:1})),'NOT_AVAILABLE'); // out of stock
    s=execute(s,'maint',{type:'MNT_WO_COMPLETE',id:'WO-0903',note:'Thermostat replaced'});
    assert.equal(M.mntAsset(s,'GY-106').status,'Faulty');
    s=execute(s,'supervisor',{type:'MNT_WO_VERIFY',id:'WO-0903',approve:true});
    assert.equal(M.mntAsset(s,'GY-106').status,'Working');
  });
  test('[MNT] Room block from a work order: refuses overlapping allocations; downtime tracked; cancel rules',()=>{
    let s=baseFixture();
    s=execute(s,'supervisor',{type:'MNT_WO_CREATE',title:'Ceiling seepage',priority:'P2',room:'103'}); // 103 has an arrival today
    const busy=last(s).id;
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_WO_BLOCK',id:busy})),'NOT_AVAILABLE');
    s=execute(s,'supervisor',{type:'MNT_WO_BLOCK',id:'WO-0903',to:'2026-10-26'});
    const r=s.rooms.find(r=>r.id==='106');assert.equal(r.blocked,true);assert.equal(r.blockTo,'2026-10-26');assert.match(r.blockReason,/WO-0903/);
    assert.equal(M.available(s,'106','2026-10-24','2026-10-25'),false);
    assert.ok(M.mntDowntimeByRoom(s).find(d=>d.room==='106').running);
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_WO_BLOCK',id:'WO-0902'})),'VALIDATION'); // lift: no room
    s=execute(s,'supervisor',{type:'MNT_WO_CANCEL',id:'WO-0903',reason:'Duplicate of guest complaint'});
    assert.equal(w(s,'WO-0903').status,'Cancelled');
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_WO_CANCEL',id:'WO-0902',reason:'x y z'})),'INVALID_TRANSITION'); // in progress
  });
  test('[MNT] PM generation: due occurrences by business date, idempotent, advances plans; day-closed refused',()=>{
    let s=baseFixture();
    const due=M.mntPmDue(s).map(d=>d.plan.id+'@'+d.due);
    assert.deepEqual(due,['PM-01@2026-10-24','PM-02@2026-10-24','PM-03@2026-10-25','PM-04@2026-10-26']);
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_PM_GENERATE'})),'FORBIDDEN');
    s=execute(s,'supervisor',{type:'MNT_PM_GENERATE'});
    const pm=x(s).orders.filter(o=>o.kind==='Preventive'&&o.status==='Open');
    assert.equal(pm.length,4);
    assert.equal(pm.find(o=>o.planId==='PM-01').vendorId,'VEN-03');assert.equal(pm.find(o=>o.planId==='PM-03').assignee,'maint');
    assert.equal(x(s).plans.find(p=>p.id==='PM-01').nextDue,'2026-10-31');
    assert.equal(M.mntPmDue(s).length,0);
    const again=execute(s,'supervisor',{type:'MNT_PM_GENERATE'});
    assert.equal(x(again).orders.length,x(s).orders.length);
    // compliance: two new PMs due today; completing one on time
    assert.deepEqual(M.mntPmCompliance(s),{due:4,onTime:1,pct:25});
    const fp=pm.find(o=>o.planId==='PM-02').id;
    s=execute(s,'maint',{type:'MNT_WO_START',id:fp});s=execute(s,'maint',{type:'MNT_WO_COMPLETE',id:fp,note:'Pump ran 10 min; pressure ok'});
    assert.equal(M.mntPmCompliance(s).onTime,2);
    s=execute(s,'supervisor',{type:'MNT_WO_VERIFY',id:fp,approve:true});assert.equal(x(s).plans.find(p=>p.id==='PM-02').lastDone,'2026-10-24');
    const closed=baseFixture();closed.closedDates.push(closed.date);
    assert.equal(code(()=>execute(closed,'supervisor',{type:'MNT_PM_GENERATE'})),'DAY_CLOSED');
    assert.equal(code(()=>execute(closed,'supervisor',{type:'MNT_WO_CREATE',title:'Tap leak',priority:'P3',room:'105'})),'DAY_CLOSED');
  });
  test('[MNT] Registers: asset, plan, vendor, AMC (money permission), spare receive; validation',()=>{
    let s=baseFixture();
    s=execute(s,'supervisor',{type:'MNT_ASSET_SAVE',name:'Split AC 1.5 T · 107',kind:'AC',room:'107',make:'Himsheetal',model:'HS-18',installedOn:'2026-10-01',vendorId:'VEN-01'});
    const a=x(s).assets.at(-1);assert.equal(a.room,'107');
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_ASSET_SAVE',name:'X1',kind:'Rocket',area:'Roof'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'maint',{type:'MNT_ASSET_SAVE',name:'X1',kind:'AC',area:'Roof'})),'FORBIDDEN');
    s=execute(s,'supervisor',{type:'MNT_PLAN_SAVE',assetId:a.id,title:'AC filter cleaning',everyDays:30,nextDue:'2026-11-01'});
    assert.equal(x(s).plans.at(-1).assetId,a.id);
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_PLAN_SAVE',assetId:a.id,title:'AC filter',everyDays:0,nextDue:'2026-11-01'})),'VALIDATION');
    s=execute(s,'supervisor',{type:'MNT_VENDOR_SAVE',name:'Prakash Electricals',trade:'Electrical',phone:'+91 90000 11299'});
    const v=x(s).vendors.at(-1).id;
    assert.equal(code(()=>execute(s,'supervisor',{type:'MNT_AMC_SAVE',vendorId:v,scope:'Wiring check',from:'2026-11-01',to:'2027-10-31',visits:2,value:1500000})),'FORBIDDEN');
    s=execute(s,'manager',{type:'MNT_AMC_SAVE',vendorId:v,scope:'Wiring check',from:'2026-11-01',to:'2027-10-31',visits:2,value:1500000});
    assert.equal(M.mntAmcStatus(x(s).amcs.at(-1),s.date),'Not started');
    s=execute(s,'supervisor',{type:'MNT_SPARE_RECEIVE',spareId:'SP-03',qty:4});
    assert.equal(x(s).spares.find(p=>p.id==='SP-03').stock,4);
    // idempotent receive by key
    const k=execute(s,'supervisor',{type:'MNT_SPARE_RECEIVE',spareId:'SP-03',qty:4,key:'rcv-1'});
    assert.deepEqual(execute(k,'supervisor',{type:'MNT_SPARE_RECEIVE',spareId:'SP-03',qty:4,key:'rcv-1'}),k);
  });
}
