// F5-E1 internet egress gate — prototype mirror tests (EGRESS-CONTRACT §1, §1.1, §1.2, §6). Names prefixed [NET].
export function register({test,M,assert}){
  const {seed,execute,netDecide,netState,netEnqueue,netReplay,netSlice,netHeldCounts,netParseWindow,netInWindow,NET_INTEGRATIONS}=M;
  const set=(s,key,value,reason='Owner test',uid='owner')=>execute(s,uid,{type:'CFG_NET_SET',setting:key,value,reason});
  const allOn=s=>{s=set(s,'net.internetEnabled',true);for(const i of NET_INTEGRATIONS)s=set(s,'net.'+i,true);return s;};

  test('[NET] default OFF for every integration (fresh and old saves) with reason master-off',()=>{
    const s=seed();delete s.egressX;
    for(const i of NET_INTEGRATIONS)assert.deepEqual(netDecide(s,i),{allowed:false,reason:'master-off'});
    const st=netState(s,'owner');assert.equal(st.masterOn,false);assert.equal(st.effectiveNow,false);assert.equal(netSlice(s).outbox.length,0);});

  test('[NET] CFG_NET_SET: Owner only, reason required, booleans only, audited change with before / after',()=>{
    const s=seed();
    for(const u of ['manager','reception','housekeeper'])assert.throws(()=>set(s,'net.internetEnabled',true,'x y z',u),e=>e.code==='FORBIDDEN');
    assert.throws(()=>set(s,'net.internetEnabled',true,''),/reason/);
    assert.throws(()=>set(s,'net.internetEnabled','yes'),/on or off/);
    assert.throws(()=>set(s,'net.bogus',true),/Unknown/);
    const n=set(s,'net.internetEnabled',true,'Go-live test');
    const c=netSlice(n).changes.at(-1);assert.deepEqual([c.key,c.before,c.after,c.reason,c.by],['net.internetEnabled',false,true,'Go-live test','owner']);
    assert.match(n.audit[0].detail,/net\.internetEnabled OFF → ON · Go-live test/);
    assert.equal(netState(n,'owner').changedBy,'owner');assert.equal(netState(n,'manager').changedBy,undefined);});

  test('[NET] master AND switch AND window; each sub-switch alone',()=>{
    let s=set(seed(),'net.internetEnabled',true);
    assert.equal(netDecide(s,'ota').reason,'integration-off');
    s=set(s,'net.ota',true);assert.equal(netDecide(s,'ota').allowed,true);assert.equal(netDecide(s,'messaging').allowed,false);
    s=set(s,'net.internetEnabled',false);assert.equal(netDecide(s,'ota').reason,'master-off');
    s=allOn(seed());for(const i of NET_INTEGRATIONS)assert.equal(netDecide(s,i).allowed,true);});

  test('[NET] window: from inclusive, to exclusive, across midnight; invalid stored value fails closed',()=>{
    const w=netParseWindow('{"from":"22:00","to":"06:00","days":[5]}');
    assert.equal(netInWindow(w,5,'22:00'),true);assert.equal(netInWindow(w,6,'05:59'),true);assert.equal(netInWindow(w,6,'06:00'),false);assert.equal(netInWindow(w,5,'05:00'),false);
    const d=netParseWindow({from:'09:00',to:'18:00',days:[1]});assert.equal(netInWindow(d,1,'09:00'),true);assert.equal(netInWindow(d,1,'18:00'),false);
    assert.equal(netParseWindow('{"from":"9","to":"18:00","days":[1]}'),undefined);assert.equal(netParseWindow(''),null);
    let s=allOn(seed());
    assert.throws(()=>set(s,'net.window','{"from":"25:00","to":"01:00","days":[1]}'),/Window/);
    s.configX.values['net.window']='{broken';assert.equal(netDecide(s,'ota').reason,'settings-unreadable');
    s.configX.values['net.window']=null;s.configX.values['net.ota']='true';assert.equal(netDecide(s,'ota').reason,'settings-unreadable');});

  test('[NET] outbox: OFF holds (no attempt), ON replays in original order, OFF again holds pending',()=>{
    let s=seed();
    for(const r of ['m1','m2','m3'])netEnqueue(s,'messaging','messaging',r);netEnqueue(s,'ota-push','ota','o1');
    assert.ok(netSlice(s).outbox.every(x=>x.status==='held'&&x.heldReason==='internet off'&&x.attempts===0));
    assert.deepEqual(netHeldCounts(s).map(c=>[c.queue,c.held]),[['messaging',3],['ota-push',1],['email',0],['scheduled-report',0]]);
    s=set(s,'net.internetEnabled',true);s=set(s,'net.messaging',true,'Messages may go');
    assert.deepEqual(netSlice(s).outbox.filter(x=>x.status==='pending').map(x=>x.ref),['m1','m2','m3']);
    assert.equal(netSlice(s).outbox.find(x=>x.ref==='o1').status,'held');
    s=set(s,'net.internetEnabled',false,'Back off');
    assert.equal(netHeldCounts(s)[0].held,3);
    s=execute(s,'owner',{type:'NET_TICK'});assert.equal(netReplay(s).released,0);});
}
