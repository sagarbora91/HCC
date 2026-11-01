// Room keys — domain / command tests (HCC_KeyAgent_Spec_v1). Names prefixed [KEY]. The simulated encoder is the keyagent MockEncoder.
export function register({test,M,assert}){
  const {seed,execute,totals,keySlice,keyLog,keyCounts,keyCountLine,keyLockOf,keyDefaultHasLock,keyBisDoor,keyMismatchWarning,keyJobState,keyEncoderOnline,keyActiveCards}=M;
  const code=c=>e=>e.code===c;
  const online=s=>execute(s,'manager',{type:'KEY_ENCODER',online:true,key:'enc-'+s.audit.length});
  /** In-house stay in room 105 with the folio settled, ready to check out. */
  function stay(){
    let s=seed();s=execute(s,'owner',{type:'BOOK',guest:'Key Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2});
    const id=s.bookings.at(-1).id;s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    return [s,id];}
  const req=(s,user,id,event,reason,extra={})=>execute(s,user,{type:'KEY_REQUEST',booking:id,event,reason,key:`r-${event}-${reason}-${s.audit.length}`,...extra});
  const step=(s,stage)=>execute(s,'manager',{type:'KEY_AGENT_STEP',stage,key:`a-${stage}-${s.audit.length}`});
  const last=s=>keySlice(s).jobs.at(-1);

  test('[KEY] absent slice reads as empty (old saves) and the default lock map follows the owner rulings',()=>{
    const s=seed();delete s.keysX;const x=keySlice(s);assert.equal(x.jobs.length,0);assert.equal(x.cards.length,0);
    assert.equal(keyDefaultHasLock('106'),false);assert.equal(keyBisDoor('106'),true);assert.equal(keyDefaultHasLock('101'),true);assert.equal(keyDefaultHasLock('314'),true);assert.equal(keyDefaultHasLock('999'),false);
    const rooms=[...Array.from({length:7},(_,i)=>String(101+i)),...Array.from({length:14},(_,i)=>String(201+i)),...Array.from({length:14},(_,i)=>String(301+i))];
    assert.equal(rooms.filter(keyBisDoor).length,35);assert.equal(rooms.filter(keyDefaultHasLock).length,34);
    assert.equal(keyLockOf(s,'106').hasLock,false);});

  test('[KEY] issue: offline fails at once with the manual hint; online queues; picked, waiting_card, done; card active; counts',()=>{
    let [s,id]=stay();
    s=req(s,'reception',id,'issued');
    assert.equal(last(s).state,'failed');assert.equal(last(s).code,'EncoderOffline');assert.match(last(s).message,/BIS Hotel manually/);
    s=online(s);assert.equal(keyEncoderOnline(s),true);
    s=req(s,'reception',id,'issued');assert.equal(last(s).state,'queued');assert.equal(last(s).kind,'issue_guest');
    s=step(s,'pick');assert.equal(last(s).state,'picked');s=step(s,'wait');assert.equal(last(s).state,'waiting_card');
    s=step(s,'finish');assert.equal(last(s).state,'done');assert.ok(last(s).cardNo);
    const cards=keyActiveCards(s,id);assert.equal(cards.length,1);assert.equal(cards[0].status,'active');assert.equal(cards[0].to,'2026-10-26T12:00');
    const c=keyCounts(s,{booking:id});assert.deepEqual([c.issued,c.reissued,c.reactivatedTotal],[1,0,0]);assert.equal(keyCountLine(c),'Issued 1, Re-issued 0, Reactivated 0');});

  test('[KEY] re-issue, extend and reactivate need a reason; a lost card marks the old card lost; Issued 1, Re-issued 2, Reactivated 1',()=>{
    let [s,id]=stay();s=online(s);
    s=req(s,'reception',id,'issued');s=step(s);
    for(const ev of ['reissued','extended','reactivated'])assert.throws(()=>req(s,'reception',id,ev),code('VALIDATION'));
    assert.throws(()=>req(s,'reception',id,'reissued','because'),code('VALIDATION'));
    s=req(s,'reception',id,'reissued','lost');s=step(s);
    s=req(s,'reception',id,'reissued','damaged');s=step(s);
    s=execute(s,'manager',{type:'KEY_SIM',supportsExtend:true,key:'sim-ext'});
    s=req(s,'manager',id,'extended','extension',{validTo:'2026-10-27'});s=step(s);
    const c=keyCounts(s,{booking:id});assert.deepEqual([c.issued,c.reissued,c.extended,c.reactivatedTotal],[1,2,1,1]);
    assert.equal(keyCountLine(c),'Issued 1, Re-issued 2, Reactivated 1');
    assert.equal(keySlice(s).cards.filter(x=>x.status==='lost').length,1);assert.equal(keySlice(s).cards.filter(x=>x.status==='active').length,1);
    assert.equal(keyActiveCards(s,id)[0].to,'2026-10-27T12:00');
    assert.equal(keyCounts(s,{room:'105'}).reissued,2);});

  test('[KEY] extend is NotSupported by the real adapter until mapped: the job fails with a re-issue hint and does not count',()=>{
    let [s,id]=stay();s=online(s);s=req(s,'reception',id,'issued');s=step(s);
    s=req(s,'reception',id,'extended','extension',{validTo:'2026-10-27'});s=step(s);
    assert.equal(last(s).state,'failed');assert.equal(last(s).code,'NotSupported');assert.match(last(s).message,/re-issue/i);
    assert.equal(keyCounts(s,{booking:id}).extended,0);});

  test('[KEY] failure codes (NoCard, WriteFailed) fail the job without a card; retry works; only successful writes count',()=>{
    let [s,id]=stay();s=online(s);
    s=execute(s,'manager',{type:'KEY_SIM',cardPresent:false,key:'sim-a'});s=req(s,'reception',id,'issued');s=step(s);assert.equal(last(s).code,'NoCard');
    s=execute(s,'manager',{type:'KEY_SIM',cardPresent:true,failNext:true,key:'sim-b'});s=req(s,'reception',id,'issued');s=step(s);assert.equal(last(s).code,'WriteFailed');
    s=req(s,'reception',id,'issued');s=step(s);assert.equal(last(s).state,'done');
    assert.equal(keyCounts(s,{booking:id}).issued,1);assert.equal(keyActiveCards(s,id).length,1);assert.equal(keySlice(s).jobs.length,3);});

  test('[KEY] room 106 has no lock (manual key); a Manager mapping changes it; the manual record is a done job and counts',()=>{
    let s=seed();s=online(s);
    s=execute(s,'owner',{type:'BOOK',guest:'Room 106',room:'106',arrival:s.date,departure:'2026-10-26',people:1});
    const id=s.bookings.at(-1).id;s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    s=req(s,'reception',id,'issued');assert.equal(last(s).state,'failed');assert.equal(last(s).code,'NoLockRoom');assert.match(last(s).message,/manual key/);
    s=execute(s,'reception',{type:'KEY_MANUAL',booking:id,event:'issued',note:'wooden key',key:'man-1'});
    assert.equal(last(s).kind,'manual');assert.equal(last(s).state,'done');assert.equal(keyActiveCards(s,id)[0].status,'manual');assert.equal(keyCounts(s,{booking:id}).issued,1);
    assert.throws(()=>execute(s,'reception',{type:'KEY_LOCK_SAVE',room:'106',hasLock:true,doorName:'106',key:'ls-r'}),code('FORBIDDEN'));
    s=execute(s,'manager',{type:'KEY_LOCK_SAVE',room:'106',hasLock:true,doorName:'106',lockNo:34,key:'ls-m'});assert.equal(keyLockOf(s,'106').hasLock,true);
    s=req(s,'reception',id,'reissued','other');assert.equal(last(s).state,'queued');
    assert.throws(()=>execute(s,'manager',{type:'KEY_LOCK_SAVE',room:'106',hasLock:true,key:'ls-nodoor'}),/door name/);});

  test('[KEY] a room without a BIS door has no lock until mapped (36 HCC rooms vs 35 doors)',()=>{
    let s=seed();const extra=s.rooms.find(r=>!keyBisDoor(r.id));
    if(extra){assert.equal(keyLockOf(s,extra.id).hasLock,false);s=execute(s,'manager',{type:'KEY_LOCK_SAVE',room:extra.id,hasLock:true,doorName:extra.id,key:'map-x'});assert.equal(keyLockOf(s,extra.id).hasLock,true);}
    s=execute(s,'manager',{type:'KEY_LOCK_SAVE',room:'105',hasLock:false,key:'map-105'});assert.equal(keyLockOf(s,'105').hasLock,false);});

  test('[KEY] a queued job expires after 2 minutes (simulated clock); an idle encoder finds nothing',()=>{
    let [s,id]=stay();s=online(s);s=req(s,'reception',id,'issued');const j=last(s);
    s=execute(s,'manager',{type:'KEY_CLOCK',minutes:3,key:'clk'});
    assert.equal(keyJobState(s,j),'expired');assert.equal(last(s).state,'expired');assert.equal(last(s).code,'Expired');
    s=step(s);assert.equal(last(s).state,'expired');assert.equal(keyActiveCards(s,id).length,0);});

  test('[KEY] permissions: housekeeper and accountant have no access; reception issues but cannot map locks or drive the encoder; manager can',()=>{
    let [s,id]=stay();s=online(s);
    for(const u of ['housekeeper','accountant']){assert.throws(()=>req(s,u,id,'issued'),code('FORBIDDEN'));assert.throws(()=>execute(s,u,{type:'KEY_READ',key:'rd-'+u}),code('FORBIDDEN'));}
    assert.throws(()=>execute(s,'reception',{type:'KEY_AGENT_STEP',key:'st-r'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'reception',{type:'KEY_ENCODER',online:false,key:'en-r'}),code('FORBIDDEN'));
    s=req(s,'reception',id,'issued');assert.equal(last(s).state,'queued');
    s=step(s);s=execute(s,'manager',{type:'KEY_CANCEL',booking:id,key:'c-m'});assert.equal(last(s).event,'cancelled');
    s=execute(s,'owner',{type:'KEY_READ',key:'rd-o'});assert.equal(last(s).event,'read');});

  test('[KEY] mismatch warning: card expires 12:00, the folio check-out is 11:00 → visible warning; none when the folio also says 12:00',()=>{
    let [s,id]=stay();s=online(s);
    s=req(s,'reception',id,'issued');assert.match(last(s).warning,/12:00.*11:00/);
    assert.equal(keyMismatchWarning('2026-10-26','12:00'),undefined);assert.ok(keyMismatchWarning('2026-10-26','14:00'));
    s.settings.checkout='12:00';s=req(s,'reception',id,'reissued','other');assert.equal(last(s).warning,undefined);});

  test('[KEY] check-out queues a cancel and never blocks: encoder offline leaves a failed job, a broken key slice is swallowed',()=>{
    let [s,id]=stay();s=online(s);s=req(s,'reception',id,'issued');s=step(s);
    let pay=execute(s,'cashier',{type:'PAYMENT',id,amount:totals(s.bookings.find(b=>b.id===id)).balance,method:'UPI',kind:'payment',reference:'DEMO-UPI'});
    let out=execute(pay,'owner',{type:'CHECKOUT',id,key:'co-1'});
    assert.equal(out.bookings.find(b=>b.id===id).status,'Departed');assert.equal(last(out).event,'cancelled');assert.equal(last(out).state,'queued');
    out=step(out);assert.equal(last(out).state,'done');assert.equal(keySlice(out).cards.filter(c=>c.status==='checked_out').length,1);
    // encoder offline at check-out: failed cancel job = the reconciliation task, check-out still completes
    const off=execute(pay,'manager',{type:'KEY_ENCODER',online:false,key:'off'});
    const out2=execute(off,'owner',{type:'CHECKOUT',id,key:'co-2'});assert.equal(out2.bookings.find(b=>b.id===id).status,'Departed');assert.equal(last(out2).code,'EncoderOffline');
    // a corrupt slice never blocks check-out
    const bad=structuredClone(pay);bad.keysX.cards='broken';
    const out3=execute(bad,'owner',{type:'CHECKOUT',id,key:'co-3'});assert.equal(out3.bookings.find(b=>b.id===id).status,'Departed');});

  test('[KEY] the key log filters by date, room, booking, user and event with totals; reasons are kept',()=>{
    let [s,id]=stay();s=online(s);
    s=req(s,'reception',id,'issued');s=step(s);s=req(s,'manager',id,'reissued','damaged');s=step(s);
    s=execute(s,'reception',{type:'KEY_READ',key:'rd-1'});s=step(s);
    assert.equal(keyLog(s).total,3);assert.equal(keyLog(s,{event:'reissued'}).rows[0].reason,'damaged');assert.equal(keyLog(s,{event:'reissued'}).totals.reissued,1);
    assert.equal(keyLog(s,{user:'manager'}).total,1);assert.equal(keyLog(s,{room:'105'}).total,2);assert.equal(keyLog(s,{booking:id}).total,2);
    assert.equal(keyLog(s,{from:'2999-01-01'}).total,0);assert.equal(keyLog(s,{to:s.date}).total,3);
    assert.deepEqual([keyLog(s).totals.issued,keyLog(s).totals.read],[1,1]);});

  test('[KEY] the same idempotency key is one request; the encoder works one job at a time, oldest first',()=>{
    let [s,id]=stay();s=online(s);
    const a=execute(s,'reception',{type:'KEY_REQUEST',booking:id,event:'issued',key:'same'});
    const b=execute(a,'reception',{type:'KEY_REQUEST',booking:id,event:'issued',key:'same'});assert.deepEqual(b,a);assert.equal(keySlice(b).jobs.length,1);
    let t=execute(a,'reception',{type:'KEY_READ',key:'rd-2'});t=step(t,'pick');assert.equal(keySlice(t).jobs[0].state,'picked');assert.equal(keySlice(t).jobs[1].state,'queued');
    t=step(t);assert.equal(keySlice(t).jobs[0].state,'done');assert.equal(keySlice(t).jobs[1].state,'queued');});

  test('[KEY] requests need an in-house booking and a valid end date',()=>{
    let s=seed();s=online(s);const conf=s.bookings.find(b=>b.status==='Confirmed');
    assert.throws(()=>execute(s,'reception',{type:'KEY_REQUEST',booking:conf.id,event:'issued',key:'x1'}),code('INVALID_TRANSITION'));
    let [t,id]=stay();t=online(t);
    assert.throws(()=>req(t,'reception',id,'issued',undefined,{validTo:t.date}),code('VALIDATION'));
    assert.throws(()=>req(t,'reception',id,'issued',undefined,{validTo:'2027-12-01'}),code('VALIDATION'));
    assert.throws(()=>execute(t,'reception',{type:'KEY_REQUEST',booking:'NOPE',event:'issued',key:'x2'}),code('NOT_FOUND'));});
}
