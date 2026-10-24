// Module 26 · AI management assistant — domain/command tests. Names prefixed [AI].
export function register({test,M,assert}){
  const {execute,baseFixture,loadScenario,HccError}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const ask=(s,uid,question,key)=>execute(s,uid,{type:'AI_ASK',question,...(key?{key}:{})});
  const last=(s,uid)=>M.aiHistory(s,uid).at(-1);

  test('[AI] module slice defaults to empty and old saves without the slice work',()=>{
    const s=M.seed();assert.deepEqual(M.aiHistory(s,'owner'),[]);assert.equal(M.aiDirtyHours(s),4);
    delete s.assistantX;assert.deepEqual(M.aiHistory(s,'owner'),[]);assert.equal(M.aiDirtyHours(s),4);
    const n=ask(s,'owner','occupancy today');assert.equal(M.aiHistory(n,'owner').length,1);
  });
  test('[AI] intent matching by keywords, specific intents win ties',()=>{
    const m=M.aiMatchIntent;
    assert.equal(m('What is occupancy today?'),'occupancy-today');
    assert.equal(m('how full are we tomorrow'),'occupancy-tomorrow');
    assert.equal(m('Occupancy for the next 7 days'),'occupancy-7d');
    assert.equal(m('occupancy this week'),'occupancy-7d');
    assert.equal(m('Who is arriving today?'),'arrivals');
    assert.equal(m('check-outs today'),'departures');
    assert.equal(m('What is our ADR'),'adr');
    assert.equal(m('RevPAR tonight'),'revpar');
    assert.equal(m('collections vs outstanding'),'collections');
    assert.equal(m('Who has the biggest balances due?'),'balances');
    assert.equal(m('cash drawer variance'),'cash');
    assert.equal(m('pending approvals'),'approvals');
    assert.equal(m('which rooms are dirty or blocked'),'rooms');
    assert.equal(m('how much did we spend this week on expenses'),'expenses-week');
    assert.equal(m('housekeeping backlog'),'hk-backlog');
    assert.equal(m('Are we ready to close the day?'),'day-close');
    assert.equal(m('daily briefing'),'briefing');
    assert.equal(m('any anomalies?'),'anomalies');
    assert.equal(m('what is the weather in Pune'),'unknown');
    assert.equal(m('   '),'unknown');
  });
  test('[AI] base figures: occupancy, ADR, RevPAR, collections, drawer match the fixture',()=>{
    const s=baseFixture();
    let a=M.aiAnswer(s,'owner','occupancy today');
    assert.match(a.text,/^35% — 8 of 23 sellable rooms/);assert.match(a.text,/becomes 43%/);
    const r=M.aiRoomRevenueTonight(s);
    assert.deepEqual([r.revenue,r.occupied,r.sellable,r.posted,r.agreed,r.adr,r.revpar],[1950000,8,23,0,8,243750,84783]);
    a=M.aiAnswer(s,'owner','ADR');assert.ok(a.figures.some(f=>f.label==='ADR'&&f.value===M.money(243750)));
    a=M.aiAnswer(s,'owner','collections vs outstanding');assert.equal(a.figures[0].value,M.money(2150000));
    a=M.aiAnswer(s,'owner','cash drawer');assert.ok(a.figures.some(f=>f.label==='Expected'&&f.value===M.money(1020000)));
    assert.ok(a.how.length>20&&a.links.some(l=>l.page==='cashier'));
  });
  test('[AI] posted night replaces the agreed rate in ADR (no double count)',()=>{
    let s=execute(baseFixture(),'owner',{type:'NIGHT_POST'});
    const r=M.aiRoomRevenueTonight(s);assert.equal(r.posted,8);assert.equal(r.agreed,0);assert.equal(r.revenue,1950000);
  });
  test('[AI] occupancy forecast: tomorrow and 7 days use stays on the books and dated blocks',()=>{
    const s=baseFixture();const t=M.aiOccupancyOn(s,'2026-10-25');
    assert.deepEqual([t.booked,t.sellable],[10,23]);
    const r=M.aiOccupancyRange(s,7);assert.equal(r.length,7);assert.equal(r[2].booked,0);
    assert.equal(M.aiAnswer(s,'owner','next 7 days').items.length,7);assert.equal(M.aiMatchIntent('next 7 days'),'occupancy-7d');
  });
  test('[AI] arrivals, departures, approvals, rooms, backlog, expenses, balances answers',()=>{
    const s=baseFixture();
    assert.equal(M.aiAnswer(s,'owner','arrivals').figures[0].value,'2');
    assert.equal(M.aiAnswer(s,'owner','departures').figures[0].value,'0');
    const ap=M.aiAnswer(s,'owner','pending approvals');assert.equal(ap.figures[1].value,'1');assert.match(ap.items[0],/Morning Supplies/);
    const rm=M.aiAnswer(s,'owner','dirty rooms');assert.deepEqual(rm.figures.map(f=>f.value),['3','1','1','1']);
    const hk=M.aiAnswer(s,'owner','housekeeping backlog');assert.match(hk.text,/^5 open tasks/);
    const ex=M.aiAnswer(s,'owner','expenses this week');assert.equal(ex.figures[0].value,M.money(180000));assert.equal(ex.figures[2].value,'1');
    const bal=M.aiAnswer(s,'owner','top balances');assert.ok(bal.items.length>0&&bal.items.length<=5);
    const tops=M.aiTopBalances(s);for(let i=1;i<tops.length;i++)assert.ok(tops[i-1].balance>=tops[i].balance);
  });
  test('[AI] day-close readiness mirrors the closing preflight',()=>{
    const s=baseFixture();const a=M.aiAnswer(s,'manager','ready to close the day?');
    assert.match(a.text,/^Not ready/);assert.ok(a.items.some(i=>/Arrivals resolved/.test(i)));
    assert.equal(a.links[0].page,'closing');
  });
  test('[AI] permissions: housekeeper and maintenance never receive money',()=>{
    const s=loadScenario('AI1');
    for(const uid of ['housekeeper','maint']){
      assert.equal(M.aiAccess(s,uid).money,false);
      for(const q of ['ADR','RevPAR','collections vs outstanding','cash drawer','top balances due','expenses this week']){
        const a=M.aiAnswer(s,uid,q);assert.equal(a.restricted,true,q);assert.ok(!/₹/.test(JSON.stringify(a)),q+' leaks money');}
      for(const q of ['briefing','anomalies','dirty rooms','housekeeping backlog','what?'])assert.ok(!/₹/.test(JSON.stringify(M.aiAnswer(s,uid,q))),q);
      assert.ok(M.aiFlags(s,uid).every(f=>!f.money));
      assert.ok(!M.aiSuggestions(s,uid).some(t=>['adr','collections','cash','balances'].includes(t.intent)));
    }
    // Even if a housekeeper is granted reports, money stays hidden.
    const g=execute(s,'owner',{type:'GRANT',role:'Housekeeper',permission:'reports'});
    assert.equal(M.aiAccess(g,'housekeeper').money,false);assert.equal(M.aiAnswer(g,'housekeeper','RevPAR').restricted,true);
    // Accountant (reports) sees money; tech admin (admin only) sees no money nor rooms.
    assert.equal(M.aiAccess(s,'accountant').money,true);
    assert.equal(M.aiAnswer(s,'techadmin','ADR').restricted,true);
  });
  test('[AI] AI_ASK records per user, snapshot answer, simulated clock; other users unaffected',()=>{
    let s=baseFixture();s=ask(s,'owner','  What is   occupancy today? ');
    const m=last(s,'owner');assert.equal(m.question,'What is occupancy today?');assert.equal(m.answer.intent,'occupancy-today');
    assert.equal(m.at,'2026-10-24T08:00');assert.equal(m.date,'2026-10-24');assert.match(m.id,/^AIQ-/);
    assert.deepEqual(M.aiHistory(s,'manager'),[]);
    s=ask(s,'manager','ADR');assert.equal(M.aiHistory(s,'owner').length,1);assert.equal(M.aiHistory(s,'manager').length,1);
    assert.match(s.audit[0].detail,/simulated/);
    // Hotel data untouched
    const b=baseFixture();assert.deepEqual(s.bookings,b.bookings);assert.deepEqual(s.rooms,b.rooms);
  });
  test('[AI] AI_ASK is idempotent by key; validation of empty / long questions',()=>{
    let s=ask(baseFixture(),'owner','ADR','ai-1');const again=ask(s,'owner','ADR','ai-1');
    assert.equal(again,s);assert.equal(M.aiHistory(again,'owner').length,1);
    assert.equal(code(()=>ask(s,'owner','   ')),'VALIDATION');
    assert.equal(code(()=>ask(s,'owner','x'.repeat(301))),'VALIDATION');
  });
  test('[AI] AI_ASK allowed on a closed day; restricted answer recorded for housekeeper; disabled user denied',()=>{
    let s=baseFixture();s.closedDates.push(s.date);
    s=ask(s,'owner','briefing');assert.equal(last(s,'owner').answer.intent,'briefing');
    s=ask(s,'housekeeper','RevPAR');assert.equal(last(s,'housekeeper').answer.restricted,true);
    const d=execute(baseFixture(),'owner',{type:'USER',id:'reception'});
    assert.equal(code(()=>ask(d,'reception','ADR')),'FORBIDDEN');
  });
  test('[AI] history capped at 50; AI_CLEAR clears only own history',()=>{
    let s=baseFixture();for(let i=0;i<55;i++)s=ask(s,'owner','q'+i);
    const h=M.aiHistory(s,'owner');assert.equal(h.length,50);assert.equal(h[0].question,'q5');
    s=ask(s,'manager','ADR');s=execute(s,'owner',{type:'AI_CLEAR'});
    assert.equal(M.aiHistory(s,'owner').length,0);assert.equal(M.aiHistory(s,'manager').length,1);
  });
  test('[AI] unknown question → helpful fallback listing permitted topics',()=>{
    const a=M.aiAnswer(baseFixture(),'owner','tell me a joke');
    assert.equal(a.intent,'unknown');assert.match(a.text,/occupancy today/);assert.ok(a.items.length>0);
    const h=M.aiAnswer(baseFixture(),'housekeeper','tell me a joke');assert.ok(!/adr/i.test(h.text));
  });
  test('[AI] anomaly flags in AI1: departed balance, large discount, dirty rooms aged on the clock, pending',()=>{
    const s=loadScenario('AI1'),f=M.aiFlags(s,'owner'),ids=f.map(x=>x.id);
    assert.ok(ids.includes('unbal:HCC-1091'));assert.equal(f.find(x=>x.id==='unbal:HCC-1091').severity,'High');
    assert.ok(ids.includes('disc:ai1-disc'));assert.equal(f.find(x=>x.id==='disc:ai1-disc').severity,'High');
    assert.equal(f.filter(x=>x.id.startsWith('dirty:')).length,3);
    assert.equal(f.find(x=>x.id==='dirty:task-0').severity,'Medium');assert.match(f.find(x=>x.id==='dirty:task-0').title,/Room 108 dirty for 8 h 0 min/);
    assert.ok(!ids.some(i=>i.startsWith('credit:')),'advance payments on confirmed bookings are not credit anomalies');
    assert.ok(ids.includes('pending'));
    for(let i=1;i<f.length;i++)assert.ok({High:0,Medium:1,Low:2}[f[i-1].severity]<={High:0,Medium:1,Low:2}[f[i].severity]);
    // Base at 08:00: tasks created 07:30 → no flag; raising the threshold in AI1 clears the flags
    assert.equal(M.aiFlags(baseFixture()).filter(x=>x.id.startsWith('dirty:')).length,0);
    let b=execute(s,'manager',{type:'AI_SETTINGS',dirtyHours:10});assert.equal(M.aiDirtyHours(b),10);
    assert.equal(M.aiFlags(b).filter(x=>x.id.startsWith('dirty:')).length,0);
    // Tasks without a timestamp are aged from 00:00 of the business date and say so
    const n=baseFixture();n.tasks.forEach(t=>delete t.createdAt);const d=M.aiFlags(n).find(x=>x.id==='dirty:task-0');
    assert.ok(d&&d.detail.includes('aged from the start'));
  });
  test('[AI] cash difference flag and AI_SETTINGS permissions',()=>{
    let s=baseFixture();const exp=M.drawer(s);
    s=execute(s,'manager',{type:'SHIFT_SUBMIT',counted:exp-30000,reason:'Short count (test)'});
    const f=M.aiFlags(s,'owner').find(x=>x.id==='cash-diff');assert.ok(f);assert.equal(f.severity,'High');
    assert.ok(!M.aiFlags(s,'housekeeper').some(x=>x.id==='cash-diff'));
    assert.match(M.aiAnswer(s,'owner','cash variance').text,/short by/);
    assert.equal(code(()=>execute(baseFixture(),'reception',{type:'AI_SETTINGS',dirtyHours:6})),'FORBIDDEN');
    assert.equal(code(()=>execute(baseFixture(),'owner',{type:'AI_SETTINGS',dirtyHours:0})),'VALIDATION');
  });
  test('[AI] briefing respects role and links to screens',()=>{
    const s=baseFixture();
    const o=M.aiBriefing(s,'owner');assert.ok(o.lines.some(l=>l.id==='coll'&&l.value===M.money(2150000)));assert.ok(o.lines.some(l=>l.id==='close'&&l.value==='Not ready'));
    const h=M.aiBriefing(s,'housekeeper');assert.ok(!h.lines.some(l=>['coll','cash','occ'].includes(l.id)));assert.ok(h.lines.some(l=>l.id==='rooms'));
    assert.ok(!/₹/.test(JSON.stringify(h)));
    const e=M.aiBriefing(loadScenario('S1'),'owner');assert.ok(e.lines.length>0);
  });
}
