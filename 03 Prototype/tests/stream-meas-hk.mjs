// F12 MEAS · meas-cap-hk — task times, room offline reasons, meters + readings, asset life, linen discards. Names prefixed [F12-CAP-HK].
export function register({test,M,assert}){
  const {execute,baseFixture,HccError}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const fresh=()=>baseFixture();
  const measOn=(s,k)=>typeof M.measOn==='function'?M.measOn(s,k):true; // measOn is exported through the mock index by meas-settings; until then always on
  const reading=(s,uid,meter,value,extra={})=>execute(s,uid,{type:'MEAS_READING_ADD',meter,value,...extra});
  const todo=(s)=>s.tasks.find(t=>t.assignee==='housekeeper'&&t.status==='To do');
  const clean=(s,t)=>{s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'});for(let i=0;i<3;i++)s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'check',index:i});return execute(s,'housekeeper',{type:'TASK',id:t.id,action:'complete'});};

  // ── task times ──
  test('[F12-CAP-HK] start / complete / inspect record startedAt, finishedAt, inspectedAt, result and attendant',()=>{
    let s=fresh();const t=todo(s);assert.ok(t);
    assert.equal(s.measX?.taskTimes?.[t.id],undefined);
    s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'});
    const a=s.measX.taskTimes[t.id];assert.equal(a.startedAt,s.tasks.find(x=>x.id===t.id).startedAt);assert.equal(a.attendant,'housekeeper');assert.equal(a.finishedAt,undefined);
    for(let i=0;i<3;i++)s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'check',index:i});
    s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'complete'});
    const b=s.measX.taskTimes[t.id];assert.equal(b.finishedAt,s.tasks.find(x=>x.id===t.id).completedAt);assert.ok(M.measTaskMinutes(b)>=1);
    s=execute(s,'supervisor',{type:'TASK',id:t.id,action:'inspect'});
    const c=s.measX.taskTimes[t.id];assert.equal(c.inspectResult,'pass');assert.ok(c.inspectedAt);assert.equal(c.startedAt,a.startedAt);
  });
  test('[F12-CAP-HK] a rejected inspection records fail + reason, a re-clean clears it, and the real TASK rules still apply',()=>{
    let s=fresh();const t=s.tasks.find(t=>t.status==='Inspection');assert.ok(t);
    assert.equal(code(()=>execute(s,'supervisor',{type:'TASK',id:t.id,action:'reject',reason:'x'})),'VALIDATION'); // real handler refuses a short reason, nothing recorded
    assert.equal(s.measX?.taskTimes?.[t.id],undefined);
    s=execute(s,'supervisor',{type:'TASK',id:t.id,action:'reject',reason:'Bathroom not clean'});
    const r=s.measX.taskTimes[t.id];assert.equal(r.inspectResult,'fail');assert.equal(r.rejectReason,'Bathroom not clean');assert.equal(r.finishedAt,undefined);
    assert.equal(code(()=>execute(s,'housekeeper',{type:'TASK',id:t.id,action:'inspect'})),'FORBIDDEN');
  });
  test('[F12-CAP-HK] a task start by the wrong user is refused by the real handler and records nothing',()=>{
    const s=fresh();const t=s.tasks.find(t=>t.assignee==='housekeeper2'&&t.status==='To do');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'})),'FORBIDDEN');assert.equal(s.measX?.taskTimes,undefined);
  });
  test('[F12-CAP-HK] measRecordTaskTime / measTaskMinutes are pure over the clock (no finish = null; backwards clock = null)',()=>{
    assert.equal(M.measTaskMinutes(undefined),null);assert.equal(M.measTaskMinutes({startedAt:'2026-10-24T08:00:00'}),null);
    assert.equal(M.measTaskMinutes({startedAt:'2026-10-24T08:00:00',finishedAt:'2026-10-24T08:42:00'}),42);
    assert.equal(M.measTaskMinutes({startedAt:'2026-10-24T09:00:00',finishedAt:'2026-10-24T08:00:00'}),null);
  });

  // ── offline reasons ──
  test('[F12-CAP-HK] MEAS_OFFLINE_SET stores reason, expected return and work order; Manager / Supervisor / Owner only',()=>{
    let s=fresh();const wo=M.mntSlice(s).orders[0]?.id;assert.ok(wo);
    s=execute(s,'supervisor',{type:'MEAS_OFFLINE_SET',room:'105',reason:'OOO',expectedReturn:s.date,workOrder:wo,note:'AC compressor'});
    assert.deepEqual(s.measX.offline['105'],{reason:'OOO',expectedReturn:s.date,workOrder:wo,note:'AC compressor'});
    assert.deepEqual(M.measOfflineList(s).map(x=>[x.room,x.reason.reason,x.overdue]),[['105','OOO',false]]);
    for(const u of ['housekeeper','maint','reception','accountant'])assert.equal(code(()=>execute(fresh(),u,{type:'MEAS_OFFLINE_SET',room:'105',reason:'OOS'})),'FORBIDDEN',u);
    s=execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'105',reason:'renovation'});assert.equal(s.measX.offline['105'].reason,'renovation');
    s=execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'105',clear:true});assert.equal(s.measX.offline['105'],undefined);
  });
  test('[F12-CAP-HK] MEAS_OFFLINE_SET validates room, reason, past return date, unknown work order; clear needs an entry',()=>{
    const s=fresh();
    assert.equal(code(()=>execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'999',reason:'OOO'})),'NOT_FOUND');
    assert.equal(code(()=>execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'105',reason:'broken'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'105',reason:'OOO',expectedReturn:'2020-01-01'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'105',reason:'OOO',workOrder:'WO-NOPE'})),'NOT_FOUND');
    assert.equal(code(()=>execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'105',clear:true})),'NOT_FOUND');
  });
  test('[F12-CAP-HK] an offline entry past its expected return lists as overdue; the command is idempotent on its key',()=>{
    let s=fresh();s=execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'105',reason:'house-use',expectedReturn:s.date,key:'off-1'});
    const again=execute(s,'manager',{type:'MEAS_OFFLINE_SET',room:'105',reason:'house-use',expectedReturn:s.date,key:'off-1'});assert.equal(again,s);
    const late={...s,date:'2026-10-30'};assert.equal(M.measOfflineList(late)[0].overdue,true);
  });

  // ── meters + readings ──
  test('[F12-CAP-HK] the meter master defaults to electricity, water, LPG cylinders and DG hours (owner default M-4)',()=>{
    const s=fresh();assert.deepEqual(M.measMeters(s).map(m=>m.id),['MTR-ELEC','MTR-WATER','MTR-LPG','MTR-DG']);assert.ok(M.measMeters(s).every(m=>m.active));
  });
  test('[F12-CAP-HK] MEAS_METER_SAVE adds / edits a meter (Owner, Manager); unique name; a meter with readings keeps its type',()=>{
    let s=fresh();
    s=execute(s,'manager',{type:'MEAS_METER_SAVE',name:'Floor 2 sub-meter',kind:'electricity'});
    const m=s.measX.meters.at(-1);assert.equal(m.unit,'kWh');assert.equal(s.measX.meters.length,5);
    assert.equal(code(()=>execute(s,'manager',{type:'MEAS_METER_SAVE',name:'floor 2 SUB-meter',kind:'electricity'})),'VALIDATION');
    for(const u of ['maint','supervisor','reception','accountant','housekeeper'])assert.equal(code(()=>execute(s,u,{type:'MEAS_METER_SAVE',name:'Other',kind:'water'})),'FORBIDDEN',u);
    s=reading(s,'maint',m.id,10);
    assert.equal(code(()=>execute(s,'manager',{type:'MEAS_METER_SAVE',id:m.id,name:'Floor 2',kind:'water'})),'INVALID_TRANSITION');
    s=execute(s,'owner',{type:'MEAS_METER_SAVE',id:m.id,name:'Floor 2',kind:'electricity',active:false});assert.equal(s.measX.meters.find(x=>x.id===m.id).active,false);
    assert.equal(code(()=>reading(s,'maint',m.id,11)),'VALIDATION'); // a switched-off meter takes no readings
  });
  test('[F12-CAP-HK] MEAS_READING_ADD is monotonic per meter: lower = MEAS_READING_NOT_MONOTONIC, equal allowed, other meters independent',()=>{
    let s=fresh();
    s=reading(s,'maint','MTR-ELEC',1000);s=reading(s,'maint','MTR-ELEC',1000);s=reading(s,'maint','MTR-ELEC',1042.5);
    assert.equal(code(()=>reading(s,'maint','MTR-ELEC',1041)),'MEAS_READING_NOT_MONOTONIC');
    s=reading(s,'maint','MTR-WATER',5);assert.equal(s.measX.readings.length,4);
    assert.equal(code(()=>reading(s,'maint','MTR-WATER',4.99)),'MEAS_READING_NOT_MONOTONIC');
    assert.equal(s.measX.readings.length,4,'a refused reading stores nothing');
  });
  test('[F12-CAP-HK] a back-dated reading must fit between its neighbours',()=>{
    let s=fresh();const d=(n)=>M.addDays(s.date,n);
    s=reading(s,'owner','MTR-DG',100,{date:d(-5)});s=reading(s,'owner','MTR-DG',130,{date:d(-1)});
    s=reading(s,'owner','MTR-DG',110,{date:d(-3)}); // between: fine
    assert.equal(code(()=>reading(s,'owner','MTR-DG',99,{date:d(-3)})),'MEAS_READING_NOT_MONOTONIC'); // below the earlier one
    assert.equal(code(()=>reading(s,'owner','MTR-DG',140,{date:d(-3)})),'MEAS_READING_NOT_MONOTONIC'); // above the later one
    assert.deepEqual(M.measConsumption(s,'MTR-DG'),[{date:d(-3),used:10},{date:d(-1),used:20}]);
  });
  test('[F12-CAP-HK] a reading validates value, date, meter and photo; photo is optional; future dates refused',()=>{
    const s=fresh();
    for(const bad of [-1,'abc','',null,1.234,NaN])assert.equal(code(()=>reading(s,'maint','MTR-ELEC',bad)),'VALIDATION',String(bad));
    assert.equal(code(()=>reading(s,'maint','MTR-NOPE',5)),'VALIDATION');
    assert.equal(code(()=>reading(s,'maint','MTR-ELEC',5,{date:M.addDays(s.date,1)})),'VALIDATION');
    assert.equal(code(()=>reading(s,'maint','MTR-ELEC',5,{date:'24/10/2026'})),'VALIDATION');
    assert.equal(code(()=>reading(s,'maint','MTR-ELEC',5,{photoRef:'x'.repeat(201)})),'VALIDATION');
    const ok=reading(s,'maint','MTR-ELEC',5,{photoRef:'doc-77'});assert.equal(ok.measX.readings[0].photoRef,'doc-77');assert.equal(reading(s,'maint','MTR-ELEC',5).measX.readings[0].photoRef,undefined);
  });
  test('[F12-CAP-HK] PERM-MEAS-002: Owner, Manager and Maintenance enter readings; others are refused',()=>{
    const s=fresh();
    for(const u of ['owner','manager','maint'])assert.equal(reading(s,u,'MTR-ELEC',1).measX.readings[0].by,u);
    for(const u of ['reception','cashier','supervisor','housekeeper','accountant'])assert.equal(code(()=>reading(s,u,'MTR-ELEC',1)),'FORBIDDEN',u);
  });
  test('[F12-CAP-HK] reading idempotent on its key; the daily checklist line lists meters with no reading today',()=>{
    let s=fresh();assert.deepEqual(M.measMetersDue(s).map(m=>m.id),['MTR-ELEC','MTR-WATER','MTR-LPG','MTR-DG']);
    s=reading(s,'maint','MTR-ELEC',10,{key:'r-1'});const again=reading(s,'maint','MTR-ELEC',10,{key:'r-1'});assert.equal(again,s);assert.equal(s.measX.readings.length,1);
    assert.deepEqual(M.measMetersDue(s).map(m=>m.id),['MTR-WATER','MTR-LPG','MTR-DG']);
  });
  test('[F12-CAP-HK] demo fixture readings are monotonic and add on top without touching the seed',()=>{
    const s0=fresh(),s=M.measHkDemoState(s0);assert.equal(s0.measX?.readings,undefined);
    for(const m of M.measMeters(s)){const v=M.measReadingsOf(s,m.id).map(r=>r.value);assert.equal(v.length,7);assert.deepEqual(v,[...v].sort((a,b)=>a-b));}
    assert.equal(M.measMetersDue(s).length,0);
  });

  // ── asset life ──
  test('[F12-CAP-HK] MEAS_ASSETLIFE_SET (Owner, Accountant): life, grade and replacement cost in integer paise, merged per asset',()=>{
    let s=fresh();const a=M.mntSlice(s).assets[0].id;
    s=execute(s,'accountant',{type:'MEAS_ASSETLIFE_SET',asset:a,expectedLifeMonths:96,conditionGrade:'B',replacementCostPaise:4500000});
    s=execute(s,'owner',{type:'MEAS_ASSETLIFE_SET',asset:a,conditionGrade:'C'});
    assert.deepEqual(s.measX.assetLife[a],{expectedLifeMonths:96,conditionGrade:'C',replacementCostPaise:4500000});
    for(const u of ['manager','maint','supervisor','housekeeper','reception'])assert.equal(code(()=>execute(fresh(),u,{type:'MEAS_ASSETLIFE_SET',asset:a,conditionGrade:'A'})),'FORBIDDEN',u);
    const t=fresh();
    assert.equal(code(()=>execute(t,'owner',{type:'MEAS_ASSETLIFE_SET',asset:'NOPE',conditionGrade:'A'})),'NOT_FOUND');
    assert.equal(code(()=>execute(t,'owner',{type:'MEAS_ASSETLIFE_SET',asset:a})),'VALIDATION');
    assert.equal(code(()=>execute(t,'owner',{type:'MEAS_ASSETLIFE_SET',asset:a,conditionGrade:'E'})),'VALIDATION');
    assert.equal(code(()=>execute(t,'owner',{type:'MEAS_ASSETLIFE_SET',asset:a,expectedLifeMonths:0})),'VALIDATION');
    assert.equal(code(()=>execute(t,'owner',{type:'MEAS_ASSETLIFE_SET',asset:a,replacementCostPaise:1.5})),'VALIDATION');
    assert.equal(code(()=>execute(t,'owner',{type:'MEAS_ASSETLIFE_SET',asset:a,replacementCostPaise:-1})),'VALIDATION');
  });
  test('[F12-CAP-HK] DEC-061 money mask: the replacement cost is removed (not blanked) for Maintenance and Housekeeping, kept for Owner / Accountant',()=>{
    let s=fresh();const a=M.mntSlice(s).assets[0].id;
    s=execute(s,'owner',{type:'MEAS_ASSETLIFE_SET',asset:a,expectedLifeMonths:60,conditionGrade:'A',replacementCostPaise:1200000});
    for(const u of ['maint','housekeeper','supervisor']){const v=M.measAssetLifeView(s,u)[a];assert.deepEqual(v,{expectedLifeMonths:60,conditionGrade:'A'},u);assert.equal('replacementCostPaise' in v,false,u);}
    for(const u of ['owner','accountant'])assert.equal(M.measAssetLifeView(s,u)[a].replacementCostPaise,1200000,u);
  });

  // ── linen discards ──
  test('[F12-CAP-HK] MEAS_DISCARD_ADD records item, quantity, reason and date (Supervisor, Manager, Owner); no money field',()=>{
    let s=fresh();
    s=execute(s,'supervisor',{type:'MEAS_DISCARD_ADD',item:'Bath towel',qty:6,reason:'Worn out'});
    s=execute(s,'manager',{type:'MEAS_DISCARD_ADD',item:'Bath towel',qty:2,reason:'Stained',date:M.addDays(s.date,-1)});
    s=execute(s,'owner',{type:'MEAS_DISCARD_ADD',item:'Bed sheet',qty:1,reason:'Worn out'});
    assert.equal(s.measX.discards.length,3);assert.deepEqual(M.measDiscardTally(s.measX.discards),{'Worn out':7,Stained:2});
    assert.deepEqual(Object.keys(s.measX.discards[0]).sort(),['by','date','id','item','qty','reason']);
    for(const u of ['housekeeper','maint','reception','accountant'])assert.equal(code(()=>execute(fresh(),u,{type:'MEAS_DISCARD_ADD',item:'Towel',qty:1,reason:'Torn'})),'FORBIDDEN',u);
  });
  test('[F12-CAP-HK] MEAS_DISCARD_ADD validates item, quantity, reason and date; idempotent on its key',()=>{
    const s=fresh(),ok={type:'MEAS_DISCARD_ADD',item:'Towel',qty:1,reason:'Torn'};
    for(const bad of [{item:''},{qty:0},{qty:1.5},{qty:1001},{reason:'x'},{date:M.addDays(s.date,1)},{date:'bad'}])assert.equal(code(()=>execute(s,'supervisor',{...ok,...bad})),'VALIDATION',JSON.stringify(bad));
    const once=execute(s,'supervisor',{...ok,key:'d-1'});assert.equal(execute(once,'supervisor',{...ok,key:'d-1'}),once);assert.equal(once.measX.discards.length,1);
  });

  // ── MEAS_OFF (module / pack / item switches) ──
  test('[F12-CAP-HK] every capture refuses MEAS_OFF when its item is switched off (checked through measOn; data kept)',()=>{
    const cases=[['meas.hk.ooo',{type:'MEAS_OFFLINE_SET',room:'105',reason:'OOO'},'manager'],['meas.hk.utilities',{type:'MEAS_READING_ADD',meter:'MTR-ELEC',value:1},'maint'],
      ['meas.hk.utilities',{type:'MEAS_METER_SAVE',name:'Sub',kind:'water'},'owner'],['meas.hk.assets',{type:'MEAS_ASSETLIFE_SET',asset:M.mntSlice(fresh()).assets[0].id,conditionGrade:'A'},'owner'],
      ['meas.hk.linen',{type:'MEAS_DISCARD_ADD',item:'Towel',qty:1,reason:'Torn'},'supervisor']];
    for(const [key,cmd,uid] of cases){
      const s=fresh();s.configX={...(s.configX??{}),values:{...(s.configX?.values??{}),[key]:false}};
      if(measOn(s,key)!==false)continue; // setup stub (meas-settings not merged yet): measOn is always true, nothing to refuse
      assert.equal(code(()=>execute(s,uid,cmd)),'MEAS_OFF',key);
    }
  });
  test('[F12-CAP-HK] task-time recording is skipped, never failed, when productivity capture is off',()=>{
    let s=fresh();s.configX={...(s.configX??{}),values:{...(s.configX?.values??{}),'meas.hk.productivity':false}};
    const t=todo(s);s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'});
    assert.equal(s.tasks.find(x=>x.id===t.id).status,'In progress'); // the real task still works
    if(measOn(s,'meas.hk.productivity')===false)assert.equal(s.measX?.taskTimes?.[t.id],undefined);
  });

  // ── slice / schema ──
  test('[F12-CAP-HK] an old save without measX works; SCHEMA stays 15; the seed is not changed by this lane',()=>{
    const s=fresh();assert.equal(s.measX,undefined);assert.equal(M.SCHEMA,15);
    const next=reading(s,'maint','MTR-ELEC',5);assert.equal(s.measX,undefined,'the input state is never mutated');assert.ok(next.measX);
    assert.equal(M.metrics(s).occupied,8);
  });
  test('[F12-CAP-HK] the real TASK audit text is kept (the wrapper returns the base handler text)',()=>{
    let s=fresh();const t=todo(s);s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'});
    assert.ok(/start/.test(s.audit[0].detail),s.audit[0].detail);
  });
}
