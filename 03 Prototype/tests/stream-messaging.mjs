// Module 24–25 · Notifications & document capture — domain/command tests. Names prefixed [MSG].
export function register({test,M,assert}){
  const {seed,execute,loadScenario}=M;
  const outbox=s=>s.messagingX.outbox;
  const last=s=>outbox(s).at(-1);
  test('[MSG] module slice defaults to empty',()=>{const s=M.seed();assert.ok(M.msgSlice(s));assert.deepEqual(M.msgSlice({...s,messagingX:undefined}),{});});
  test('[MSG] fixture: templates, rules, seeded outbox; only today\'s pre-arrival triggers pending',()=>{
    const s=seed();assert.equal(M.msgTemplates(s).length,6);assert.equal(M.msgRules(s).length,6);
    const p=M.msgPendingTriggers(s);assert.deepEqual(p.map(t=>t.key).sort(),['R2:HCC-1049','R2:HCC-1050']);
    const c=M.msgCounts(s);assert.equal(c.Failed,1);assert.equal(c['Not sent'],1);assert.equal(c.Read+c.Delivered,5);
  });
  test('[MSG] placeholders filled from booking/guest; unknown flagged',()=>{
    const s=seed(),b=s.bookings.find(b=>b.id==='HCC-1041');const v=M.msgValues(s,b);
    const r=M.msgRender('Hi {firstName}, room {room}, {nights} nights at {hotel} {bogus}',v);
    assert.equal(r.text,'Hi Ananya, room 101, 3 nights at HCC Demo Residency [bogus?]');assert.deepEqual(r.missing,['bogus']);
    assert.deepEqual(M.msgUnknownPlaceholders('{guest} {x} {x}'),['x']);
  });
  test('[MSG] manual compose: in-house only, consent respected, nothing for housekeeper',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'MSG_SEND',booking:'HCC-1041',channel:'WhatsApp',body:'Hello {firstName}, towels on the way.'});
    assert.equal(last(s).body,'Hello Ananya, towels on the way.');assert.equal(last(s).to,'••••••1041');assert.equal(M.msgStatus(last(s),s.clock),'Queued');
    assert.throws(()=>execute(s,'reception',{type:'MSG_SEND',booking:'HCC-1049',channel:'SMS',body:'x'}),e=>e.code==='INVALID_TRANSITION');
    assert.throws(()=>execute(s,'reception',{type:'MSG_SEND',booking:'HCC-1046',channel:'SMS',body:'Hi'}),/opted out/);
    assert.throws(()=>execute(s,'reception',{type:'MSG_SEND',booking:'HCC-1047',channel:'WhatsApp',body:'Hi'}),/No WhatsApp consent/);
    execute(s,'reception',{type:'MSG_SEND',booking:'HCC-1047',channel:'SMS',body:'Hi'});
    assert.throws(()=>execute(s,'housekeeper',{type:'MSG_SEND',booking:'HCC-1041',channel:'SMS',body:'Hi'}),/Access denied/);
    assert.throws(()=>execute(s,'reception',{type:'MSG_SEND',booking:'HCC-1041',channel:'Fax',body:'Hi'}),/WhatsApp, SMS or Email/);
    assert.throws(()=>execute(s,'reception',{type:'MSG_SEND',booking:'HCC-1041',channel:'SMS',body:'Hi {pin}'}),/Unknown placeholder/);
  });
  test('[MSG] delivery advances on the simulated clock and via Simulate delivery',()=>{
    let s=execute(seed(),'reception',{type:'MSG_SEND',booking:'HCC-1041',channel:'SMS',body:'Test'});const id=last(s).id;
    const m=()=>outbox(s).find(x=>x.id===id);
    assert.equal(M.msgStatus(m(),s.clock),'Queued');
    // Any later command advances the clock one minute → Sent.
    s=execute(s,'owner',{type:'CHARGE',id:'HCC-1041',label:'Tea',amount:5000});assert.equal(M.msgStatus(m(),s.clock),'Sent');
    const final=m().outcome==='Failed'?'Failed':'Delivered';
    s=execute(s,'reception',{type:'MSG_SIMULATE',id});assert.equal(M.msgStatus(m(),s.clock),final);
    assert.throws(()=>execute(s,'reception',{type:'MSG_SIMULATE',id}),/already complete/);
    assert.ok(M.msgTimeline(m(),s.clock).length>=3);
  });
  test('[MSG] outcome is deterministic; invalid number fails; failed can be retried once',()=>{
    assert.equal(M.msgOutcome('MSG-9','SMS',false),'Failed');assert.equal(M.msgOutcome('MSG-77','WhatsApp',true),M.msgOutcome('MSG-77','WhatsApp',true));
    let s=seed();const failed=outbox(s).find(m=>m.outcome==='Failed');assert.equal(M.msgStatus(failed,s.clock),'Failed');
    s=execute(s,'reception',{type:'MSG_RETRY',id:failed.id});const r=last(s);assert.equal(r.retryOf,failed.id);assert.equal(r.outcome,'Delivered');
    assert.throws(()=>execute(s,'reception',{type:'MSG_RETRY',id:failed.id}),/already retried/);
    assert.throws(()=>execute(s,'reception',{type:'MSG_RETRY',id:outbox(s)[0].id}),e=>e.code==='INVALID_TRANSITION');
  });
  test('[MSG] trigger rules: new booking → pending confirmation; queue is idempotent; opted-out recorded as Not sent',()=>{
    let s=execute(seed(),'reception',{type:'BOOK',guest:'Trigger Test',room:'105',arrival:'2026-10-24',departure:'2026-10-25',people:1,phone:'+91 98200 05555'});
    const bid=s.bookings.at(-1).id;const keys=M.msgPendingTriggers(s).map(t=>t.key);
    assert.ok(keys.includes('R1:'+bid)&&keys.includes('R2:'+bid));
    const n=outbox(s).length;s=execute(s,'reception',{type:'MSG_RUN_RULES',key:'rules-1'});
    assert.equal(outbox(s).length,n+4);assert.equal(M.msgPendingTriggers(s).length,0);
    assert.deepEqual(execute(s,'reception',{type:'MSG_RUN_RULES',key:'rules-1'}),s);
    assert.throws(()=>execute(s,'reception',{type:'MSG_RUN_RULES'}),/no pending/);
    // Opt out Ishaan's profile, then a payment → receipt trigger recorded as Not sent.
    s=execute(s,'reception',{type:'MSG_CONSENT',guestKey:'G-0009',optOut:true});
    s=execute(s,'cashier',{type:'PAYMENT',id:'HCC-1049',amount:10000,method:'UPI',kind:'payment',reference:'DEMO-T'});
    assert.equal(M.msgPendingTriggers(s).length,1);s=execute(s,'reception',{type:'MSG_RUN_RULES'});
    assert.equal(last(s).blocked,'Guest opted out of messages');assert.equal(M.msgStatus(last(s),s.clock),'Not sent');
  });
  test('[MSG] opt-out stops still-queued messages; disabled rule does not fire',()=>{
    let s=execute(seed(),'reception',{type:'MSG_SEND',booking:'HCC-1041',channel:'WhatsApp',body:'Queued one'});
    s=execute(s,'reception',{type:'MSG_CONSENT',guestKey:'G-0001',optOut:true});
    assert.equal(M.msgStatus(last(s),s.clock),'Not sent');
    assert.throws(()=>execute(s,'reception',{type:'MSG_CONSENT',guestKey:'G-NOPE',optOut:true}),e=>e.code==='NOT_FOUND');
    let t=execute(seed(),'owner',{type:'MSG_RULE',id:'R2',enabled:false});assert.equal(M.msgPendingTriggers(t).length,0);
    assert.throws(()=>execute(seed(),'reception',{type:'MSG_RULE',id:'R2',enabled:false}),/Access denied/);
  });
  test('[MSG] template edit validates placeholders and length; manager allowed, reception denied',()=>{
    const s=execute(seed(),'manager',{type:'MSG_TEMPLATE',id:'feedback',body:'Dear {guest}, please rate us: {feedbackLink}'});
    assert.equal(M.msgTemplate(s,'feedback').body,'Dear {guest}, please rate us: {feedbackLink}');
    assert.throws(()=>execute(s,'manager',{type:'MSG_TEMPLATE',id:'feedback',body:'Dear {guest}, your OTP {otp}'}),/Unknown placeholder/);
    assert.throws(()=>execute(s,'manager',{type:'MSG_TEMPLATE',id:'feedback',body:'short'}),/10–600/);
    assert.throws(()=>execute(s,'reception',{type:'MSG_TEMPLATE',id:'feedback',body:'Dear {guest}, thanks!'}),/Access denied/);
  });
  test('[MSG] ID capture: low confidence needs review; applies ID type + last 4 only; no full number stored',()=>{
    let s=execute(seed(),'reception',{type:'MSG_DOC_EXTRACT',sample:'S-ID-2'});const d=s.messagingX.docs.at(-1);
    assert.deepEqual(M.msgLowFields(d.fields),['idLast4']);
    assert.throws(()=>execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,guestId:'G-0009'}),/Review the low-confidence field: ID last 4/);
    assert.throws(()=>execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,guestId:'G-0009',fields:{idLast4:'123456784219'}}),/full ID number/);
    assert.throws(()=>execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,guestId:'G-0009',fields:{idLast4:'42199'}}),/exactly 4/);
    s=execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,guestId:'G-0009',fields:{idLast4:'4219'},key:'doc-1'});
    const g=s.guests.find(g=>g.id==='G-0009');assert.equal(g.idType,'Passport (demo)');assert.equal(g.idLast4,'4219');assert.equal(g.version,2);
    const doc=s.messagingX.docs.at(-1);assert.equal(doc.status,'Applied');assert.equal(doc.fields.find(f=>f.key==='idLast4').original,'4Z19');
    assert.ok(!/\d{8,}/.test(JSON.stringify(s.messagingX)),'no long digit runs (full ID numbers) in the slice');
    assert.deepEqual(execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,guestId:'G-0009',key:'doc-1'}),s);
    assert.throws(()=>execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,guestId:'G-0009'}),e=>e.code==='INVALID_TRANSITION');
  });
  test('[MSG] vendor bill → expense draft through EXPENSE; permissions and day-closed respected',()=>{
    let s=execute(seed(),'reception',{type:'MSG_DOC_EXTRACT',sample:'S-BILL-2'});const d=s.messagingX.docs.at(-1);
    assert.throws(()=>execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,method:'Cash'}),/Amount/);
    const n=s.expenses.length;
    s=execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,method:'Vendor credit',reviewed:['billNo','amount']});
    const e=s.expenses.at(-1);assert.equal(s.expenses.length,n+1);
    assert.deepEqual([e.vendor,e.amount,e.status,e.method,e.category,e.actor],['Kaveri Electricals (fictional)',385000,'Draft','Vendor credit','Maintenance','reception']);
    assert.match(e.notes,/KE-0457/);assert.equal(s.messagingX.docs.at(-1).appliedTo,e.id);
    let t=execute(seed(),'manager',{type:'MSG_DOC_EXTRACT',sample:'S-BILL-1'});const d2=t.messagingX.docs.at(-1);
    assert.throws(()=>execute(t,'manager',{type:'MSG_DOC_CONFIRM',id:d2.id,fields:{category:'Snacks'}}),/Category/);
    t.closedDates.push(t.date);assert.throws(()=>execute(t,'manager',{type:'MSG_DOC_CONFIRM',id:d2.id}),e=>e.code==='DAY_CLOSED');
    assert.throws(()=>execute(seed(),'accountant',{type:'MSG_DOC_EXTRACT',sample:'S-BILL-1'}),/Access denied/);
  });
  test('[MSG] company letter applies company + GSTIN; discard is final',()=>{
    let s=execute(seed(),'reception',{type:'MSG_DOC_EXTRACT',sample:'S-LTR-1'});const d=s.messagingX.docs.at(-1);
    assert.throws(()=>execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,guestId:'G-0010',fields:{gstin:'BAD'}}),/GSTIN/);
    s=execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d.id,guestId:'G-0010',reviewed:['gstin']});
    const g=s.guests.find(g=>g.id==='G-0010');assert.equal(g.company,'Nair Demo Logistics Pvt Ltd');assert.equal(g.gstin,'32ABCDE4321K1Z2');
    s=execute(s,'reception',{type:'MSG_DOC_EXTRACT',sample:'S-ID-1'});const d2=s.messagingX.docs.at(-1);
    s=execute(s,'reception',{type:'MSG_DOC_DISCARD',id:d2.id,reason:'Wrong guest'});assert.equal(s.messagingX.docs.at(-1).status,'Discarded');
    assert.throws(()=>execute(s,'reception',{type:'MSG_DOC_CONFIRM',id:d2.id,guestId:'G-0010'}),e=>e.code==='INVALID_TRANSITION');
  });
  test('[MSG] S1 empty hotel and old saves without the slice work',()=>{
    const s=loadScenario('S1');assert.equal(M.msgOutbox(s).length,0);assert.equal(M.msgPendingTriggers(s).length,0);
    const old={...seed(),messagingX:undefined};assert.equal(M.msgTemplates(old).length,6);
    const n=execute(old,'reception',{type:'MSG_SEND',booking:'HCC-1041',channel:'Email',body:'Hello'});assert.equal(n.messagingX.outbox.length,1);
  });
}
