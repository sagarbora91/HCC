// Stream A · Calendar, availability & reservations (BL-011/012/013).
export function register({test,M,assert}){
  const {execute,loadScenario,baseFixture,available,HccError,resSearch,resCalendar,resStatus,resHoldLeftLabel,metrics,drawer}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const msg=(fn)=>{try{fn();}catch(e){return e.message;}return '';};
  const hold=(s,extra={})=>{const n=execute(s,'reception',{type:'BOOK',guest:'Hold Guest',phone:'+91 90000 11111',room:'105',arrival:s.date,departure:'2026-10-26',people:2,hold:true,...extra});return [n,n.bookings.at(-1)];};
  const plain={type:'BOOK',guest:'Other Guest',room:'105',arrival:'2026-10-25',departure:'2026-10-27',people:1};

  // BL-012 holds
  test('[A] BOOK hold:true creates Held with holdUntil = clock + 120 min',()=>{const s=baseFixture();const [n,b]=hold(s);assert.equal(b.status,'Held');assert.equal(b.holdUntil,'2026-10-24T10:00');assert.equal(b.phone,'+91 90000 11111');});
  test('[A] Held room blocks an overlapping booking (NOT_AVAILABLE) and availability',()=>{const [s]=hold(baseFixture());assert.equal(available(s,'105','2026-10-25','2026-10-27'),false);assert.equal(code(()=>execute(s,'owner',plain)),'NOT_AVAILABLE');assert.equal(available(s,'105','2026-10-26','2026-10-28'),true);});
  test('[A] Hold expiry releases inventory; status derives to Expired',()=>{let [s,b]=hold(baseFixture());s={...s,clock:'2026-10-24T10:00'};assert.equal(available(s,'105','2026-10-25','2026-10-27'),true);assert.equal(resStatus(s,s.bookings.find(x=>x.id===b.id)),'Expired');assert.match(resHoldLeftLabel(s,b),/expired at 10:00/);s=execute(s,'owner',plain);assert.equal(s.bookings.find(x=>x.id===b.id).status,'Expired');assert.equal(s.bookings.at(-1).status,'Confirmed');});
  test('[A] Hold expires by advancing the clock through other commands; never extended',()=>{let [s,b]=hold(baseFixture());for(let i=0;i<119;i++)s=execute(s,'owner',{type:'NIGHT_POST'});assert.equal(s.clock,'2026-10-24T10:00');assert.equal(s.bookings.find(x=>x.id===b.id).holdUntil,'2026-10-24T10:00');assert.equal(available(s,'105',s.date,'2026-10-26'),true);});
  test('[A] CONFIRM a running hold → Confirmed; after expiry → INVALID_TRANSITION, state unchanged',()=>{let [s,b]=hold(baseFixture());const ok=execute(s,'reception',{type:'CONFIRM',id:b.id});assert.equal(ok.bookings.find(x=>x.id===b.id).status,'Confirmed');assert.equal(ok.bookings.find(x=>x.id===b.id).holdUntil,undefined);
    const late={...s,clock:'2026-10-24T10:05'},copy=JSON.stringify(late);assert.equal(code(()=>execute(late,'reception',{type:'CONFIRM',id:b.id})),'INVALID_TRANSITION');assert.match(msg(()=>execute(late,'reception',{type:'CONFIRM',id:b.id})),/expired at 10:00/);assert.equal(JSON.stringify(late),copy);});
  test('[A] CONFIRM rejects non-held bookings and needs reservations permission',()=>{const s=baseFixture();assert.equal(code(()=>execute(s,'owner',{type:'CONFIRM',id:'HCC-1049'})),'INVALID_TRANSITION');const [h,b]=hold(s);assert.equal(code(()=>execute(h,'housekeeper',{type:'CONFIRM',id:b.id})),'FORBIDDEN');});
  test('[A] CANCEL works on a running hold (releases room); not on an expired one; no-show not allowed for holds',()=>{let [s,b]=hold(baseFixture());assert.equal(code(()=>execute(s,'owner',{type:'CANCEL',id:b.id,noShow:true})),'INVALID_TRANSITION');const c=execute(s,'owner',{type:'CANCEL',id:b.id,reason:'Guest changed plans'});const x=c.bookings.find(x=>x.id===b.id);assert.equal(x.status,'Cancelled');assert.equal(x.cancelReason,'Guest changed plans');assert.equal(available(c,'105',c.date,'2026-10-26'),true);assert.equal(code(()=>execute({...s,clock:'2026-10-24T11:00'},'owner',{type:'CANCEL',id:b.id})),'INVALID_TRANSITION');});
  test('[A] CANCEL still requires advances refunded; no-show only from arrival date',()=>{const s=baseFixture();assert.match(msg(()=>execute(s,'owner',{type:'CANCEL',id:'HCC-1049'})),/Refund the advance/);let t=execute(s,'owner',{type:'BOOK',guest:'Future Guest',room:'105',arrival:'2026-10-27',departure:'2026-10-28',people:1});assert.match(msg(()=>execute(t,'owner',{type:'CANCEL',id:t.bookings.at(-1).id,noShow:true})),/on or after the arrival/);});

  // Capacity + form fields (BL-012/013)
  test('[A] Capacity: Classic 2, Deluxe 3, Suite 4 (children counted) → VALIDATION',()=>{const s=baseFixture(),B=(room,people,children)=>code(()=>execute(s,'owner',{type:'BOOK',guest:'Cap Guest',room,arrival:s.date,departure:'2026-10-25',people,children}));
    assert.equal(B('105',3),'VALIDATION');assert.equal(B('105',2),'NO_ERROR');assert.equal(B('105',1,2),'VALIDATION');assert.equal(B('203',3),'NO_ERROR');assert.equal(B('203',3,1),'VALIDATION');assert.equal(B('106',4),'NO_ERROR');assert.match(msg(()=>execute(s,'owner',{type:'BOOK',guest:'Cap Guest',room:'105',arrival:s.date,departure:'2026-10-25',people:3})),/at most 2 guests/);});
  test('[A] BOOK stores children, company + GSTIN and OTA channel; validates them',()=>{const s=baseFixture(),base={type:'BOOK',guest:'Corp Guest',room:'203',arrival:s.date,departure:'2026-10-25',people:2};
    const n=execute(s,'owner',{...base,children:1,source:'OTA',otaChannel:'Demo Travel Hub',company:{name:'Demo Industries Pvt Ltd (fictional)',gstin:'27aaaaa0000a1z5'}}),b=n.bookings.at(-1);
    assert.equal(b.children,1);assert.equal(b.otaChannel,'Demo Travel Hub');assert.deepEqual(b.company,{name:'Demo Industries Pvt Ltd (fictional)',gstin:'27AAAAA0000A1Z5'});
    assert.equal(code(()=>execute(s,'owner',{...base,source:'OTA'})),'VALIDATION');assert.equal(code(()=>execute(s,'owner',{...base,company:{name:'X Co',gstin:'BAD'}})),'VALIDATION');assert.equal(code(()=>execute(s,'owner',{...base,phone:'abc'})),'VALIDATION');});
  test('[A] BOOK keeps the old minimal command shape',()=>{const s=execute(baseFixture(),'owner',{type:'BOOK',guest:'Legacy Guest',room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:2});const b=s.bookings.at(-1);assert.equal(b.status,'Confirmed');assert.equal(b.source,'Direct');assert.equal(b.holdUntil,undefined);assert.equal(b.company,undefined);});

  // Availability search reasons (REQ-BOOK-001)
  test('[A] Search exclusion reasons: blocked, occupied, booked, held, over capacity',()=>{let s=loadScenario('BASE');const q=resSearch(s,{arrival:s.date,departure:'2026-10-25',people:2});const row=id=>[...q.available,...q.excluded].find(r=>r.room.id===id);
    assert.equal(row('308').ok,false);assert.equal(row('308').reasons[0],'Blocked');assert.match(row('308').reason,/^Blocked/);
    assert.equal(row('101').reasons[0],'Occupied');assert.equal(row('103').reasons[0],'Booked');assert.equal(row('106').reasons[0],'Held');assert.equal(row('105').ok,true);
    const big=resSearch(s,{arrival:s.date,departure:'2026-10-25',people:3,type:'Classic'});assert.ok(big.excluded.every(r=>r.reasons.includes('Over capacity')));assert.equal(big.available.length,0);
    assert.ok(resSearch(s,{arrival:s.date,departure:'2026-10-25',people:1,type:'Suite'}).excluded.concat(resSearch(s,{arrival:s.date,departure:'2026-10-25',people:1,type:'Suite'}).available).every(r=>r.room.type==='Suite'));});
  test('[A] BASE has a running hold on 106; S3 has an expired one; S0 figures unchanged',()=>{const b=loadScenario('BASE'),h=b.bookings.find(x=>x.id==='HCC-1090');assert.equal(resStatus(b,h),'Held');assert.equal(available(b,'106',b.date,'2026-10-25'),false);
    const s3=loadScenario('S3'),e=s3.bookings.find(x=>x.id==='HCC-1091');assert.equal(resStatus(s3,e),'Expired');assert.equal(available(s3,'106',s3.date,'2026-10-25'),true);
    const s0=loadScenario('S0');assert.equal(s0.bookings.some(x=>x.status==='Held'),false);assert.equal(drawer(s0),1072500);assert.equal(metrics(b).collections,metrics(baseFixture()).collections);}); // DEC-069 GST: drawer 1075000 → 1072500 (POS-F01 ₹550 → ₹525)

  // Calendar selector (SCR-03)
  test('[A] Calendar: stay spanning the range start is clipped; cells correct',()=>{const s=loadScenario('BASE'),cal=resCalendar(s,'2026-10-24',14);assert.equal(cal.dates.length,14);assert.equal(cal.end,'2026-11-07');
    const r101=cal.groups.flatMap(g=>g.rows).find(r=>r.room.id==='101');const bar=r101.bars.find(b=>b.booking==='HCC-1041');
    assert.deepEqual({start:bar.start,span:bar.span,clipStart:bar.clipStart,clipEnd:bar.clipEnd},{start:0,span:2,clipStart:true,clipEnd:false});
    assert.deepEqual(r101.cells.slice(0,3).map(c=>c.kind),['stay','stay','free']);assert.equal(r101.cells[0].booking,'HCC-1041');assert.match(bar.label,/Ananya Rao · In house/);});
  test('[A] Calendar: stay crossing the range end is clipped; holds labelled with time left; groups by type; filter',()=>{let s=loadScenario('BASE');s=execute(s,'owner',{type:'BOOK',guest:'Long Stay',room:'105',arrival:'2026-11-05',departure:'2026-11-10',people:1});
    const cal=resCalendar(s,'2026-10-24',14),rows=cal.groups.flatMap(g=>g.rows);const bar=rows.find(r=>r.room.id==='105').bars[0];assert.deepEqual([bar.start,bar.span,bar.clipStart,bar.clipEnd],[12,2,false,true]);
    const h=rows.find(r=>r.room.id==='106').bars[0];assert.equal(h.status,'Held');assert.match(h.label,/min left/);
    assert.deepEqual(cal.groups.map(g=>g.type),['Classic','Deluxe','Suite']);assert.deepEqual(resCalendar(s,'2026-10-24',14,'Suite').groups.map(g=>g.type),['Suite']);
    const blocked=rows.find(r=>r.room.id==='308');assert.equal(blocked.bars[0].kind,'block');assert.ok(blocked.cells.every(c=>c.kind==='block'));
    const later={...s,clock:'2026-10-24T09:31'};assert.equal(resCalendar(later,'2026-10-24',14).groups.flatMap(g=>g.rows).find(r=>r.room.id==='106').bars.length,0);});

  // CHANGE_DATES (non-drag move dialog)
  test('[A] Change dates: conflict leaves the original intact; valid move applied with rate preserved',()=>{let s=baseFixture();const copy=JSON.stringify(s);
    assert.equal(code(()=>execute(s,'reception',{type:'CHANGE_DATES',id:'HCC-1049',room:'101',arrival:'2026-10-24',departure:'2026-10-27'})),'NOT_AVAILABLE');assert.equal(JSON.stringify(s),copy);
    const before=s.bookings.find(b=>b.id==='HCC-1049');s=execute(s,'reception',{type:'CHANGE_DATES',id:'HCC-1049',room:'105',arrival:'2026-10-25',departure:'2026-10-28',version:before.version});const after=s.bookings.find(b=>b.id==='HCC-1049');
    assert.deepEqual([after.room,after.arrival,after.departure,after.rate],['105','2026-10-25','2026-10-28',before.rate]);assert.equal(available(s,'103','2026-10-24','2026-10-26'),true);});
  test('[A] Change dates: stale version, in-house stay, capacity and past arrival rejected',()=>{const s=baseFixture();assert.equal(code(()=>execute(s,'owner',{type:'CHANGE_DATES',id:'HCC-1049',room:'105',arrival:'2026-10-25',departure:'2026-10-26',version:99})),'STALE_VERSION');
    assert.equal(code(()=>execute(s,'owner',{type:'CHANGE_DATES',id:'HCC-1041',room:'105',arrival:'2026-10-24',departure:'2026-10-26'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'owner',{type:'CHANGE_DATES',id:'HCC-1049',room:'105',arrival:'2026-10-23',departure:'2026-10-26'})),'VALIDATION');
    let t=execute(s,'owner',{type:'BOOK',guest:'Trio',room:'203',arrival:'2026-10-27',departure:'2026-10-28',people:3});assert.equal(code(()=>execute(t,'owner',{type:'CHANGE_DATES',id:t.bookings.at(-1).id,room:'105',arrival:'2026-10-27',departure:'2026-10-28'})),'VALIDATION');});
  test('[A] Change dates on a hold keeps its expiry (no extension)',()=>{let [s,b]=hold(baseFixture());s=execute(s,'owner',{type:'CHANGE_DATES',id:b.id,room:'107',arrival:'2026-10-25',departure:'2026-10-27'});const x=s.bookings.find(x=>x.id===b.id);assert.equal(x.holdUntil,b.holdUntil);assert.equal(x.status,'Held');assert.equal(available(s,'107','2026-10-25','2026-10-26'),false);});
  test('[A] Idempotent BOOK key creates one booking',()=>{let s=baseFixture();const c={...plain,key:'book:once'};s=execute(s,'owner',c);const n=s.bookings.length;assert.equal(execute(s,'owner',c),s);assert.equal(s.bookings.length,n);});
}
