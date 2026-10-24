// Module 21–22 · OTA channels & direct booking — domain/command tests. Names prefixed [CHN].
export function register({test,M,assert}){
  const {execute,seed,HccError,metrics,drawer,available}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const msg=(s,id)=>M.chnSlice(s).messages.find(m=>m.id===id);
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);

  test('[CHN] fixture: 4 channels, 6 pending messages, core figures untouched',()=>{
    const s=seed(),st=M.chnSlice(s);
    assert.equal(st.channels.length,4);assert.equal(M.chnPending(s).length,6);assert.equal(st.outbox.length,3);
    assert.equal(metrics(s).occupied,8);assert.equal(metrics(s).collections,2150000);assert.equal(drawer(s),1020000);
    assert.deepEqual(M.chnSlice({...s,channelsX:undefined}).channels,[]);
  });
  test('[CHN] accept New → Confirmed OTA booking via BOOK (hotel tariff, channel name, first free room)',()=>{
    let s=seed();const chk=M.chnAcceptCheck(s,msg(s,'CHM-F1'));assert.equal(chk.ok,true);assert.equal(chk.room,'103');
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F1'});
    const m=msg(s,'CHM-F1'),b=bk(s,m.booking);
    assert.equal(m.status,'Accepted');assert.equal(b.status,'Confirmed');assert.equal(b.source,'OTA');assert.equal(b.otaChannel,'BookNest');
    assert.equal(b.room,'103');assert.equal(b.rate,300000);assert.equal(b.arrival,'2026-10-27');assert.match(b.notes,/BN-88231/);
    assert.equal(metrics(s).occupied,8);assert.match(s.audit[0].detail,/BN-88231/);
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F1'})),'INVALID_TRANSITION');
  });
  test('[CHN] accept with staff-chosen room; wrong type rejected',()=>{
    let s=seed();
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F1',room:'101'})),'VALIDATION');
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F1',room:'204'});assert.equal(bk(s,msg(s,'CHM-F1').booking).room,'204');
  });
  test('[CHN] overbooking prevented: no free Suite → NOT_AVAILABLE, state unchanged; reject needs a reason',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F3'});s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F4'});
    assert.deepEqual([bk(s,msg(s,'CHM-F3').booking).room,bk(s,msg(s,'CHM-F4').booking).room],['106','306']);
    s=execute(s,'manager',{type:'CHN_PULL'});const late=M.chnSlice(s).messages.at(-1);
    assert.equal(late.ref,'BN-88275');assert.equal(late.receivedAt<=s.clock,true);
    const chk=M.chnAcceptCheck(s,late);assert.equal(chk.ok,false);assert.match(chk.reason,/No Suite room free/);
    const copy=JSON.stringify(s);
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_ACCEPT',id:late.id})),'NOT_AVAILABLE');assert.equal(JSON.stringify(s),copy);
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_REJECT',id:late.id,reason:''})),'VALIDATION');
    s=execute(s,'reception',{type:'CHN_REJECT',id:late.id,reason:chk.reason});
    assert.equal(msg(s,late.id).status,'Rejected');assert.match(msg(s,late.id).reason,/No Suite/);
  });
  test('[CHN] pre-checks: capacity, unmapped type, past arrival, duplicate ref',()=>{
    let s=seed();
    assert.match(M.chnAcceptCheck(s,msg(s,'CHM-F5')).reason,/exceeds Classic capacity \(2\)/);
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F5'})),'VALIDATION');
    const un={...msg(s,'CHM-F2'),id:'x',roomType:'Suite'};assert.match(M.chnAcceptCheck(s,un).reason,/not mapped on StayHub/);
    const past={...msg(s,'CHM-F2'),id:'y',arrival:'2026-10-20'};assert.match(M.chnAcceptCheck(s,past).reason,/before the business date/);
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F2'});
    const dup={...msg(s,'CHM-F2'),id:'z',status:'Pending'};assert.match(M.chnAcceptCheck(s,dup).reason,/Duplicate/);
  });
  test('[CHN] modification: pending request updated; accepted booking moved via CHANGE_DATES',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F6'});
    assert.equal(msg(s,'CHM-F6').status,'Applied');assert.equal(msg(s,'CHM-F2').departure,'2026-10-27');
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F2'});assert.equal(bk(s,msg(s,'CHM-F2').booking).departure,'2026-10-27');
    // accepted then modified
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F1'});const id=msg(s,'CHM-F1').booking;
    s=execute(s,'manager',{type:'CHN_PULL'});s=execute(s,'manager',{type:'CHN_PULL'});
    const mod=M.chnSlice(s).messages.find(m=>m.kind==='Modify'&&m.ref==='BN-88231');assert.ok(mod);
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:mod.id});
    assert.equal(bk(s,id).departure,'2026-10-30');assert.equal(bk(s,id).status,'Confirmed');assert.equal(bk(s,id).rate,300000);
    assert.equal(msg(s,'CHM-F1').departure,'2026-10-30');
  });
  test('[CHN] cancellation: accepted booking cancelled; pending request withdrawn; disconnected channel delivers nothing',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F4'});const id=msg(s,'CHM-F4').booking;
    // TripKart is in Error: its cancel stays in the outbox until reconnected
    s=execute(s,'manager',{type:'CHN_PULL'});s=execute(s,'manager',{type:'CHN_PULL'});s=execute(s,'manager',{type:'CHN_PULL'});
    assert.equal(M.chnSlice(s).outbox.length,1);assert.match(s.audit[0].detail,/no new messages/);
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_CHANNEL_STATUS',channel:'tripkart',action:'connect'})),'FORBIDDEN');
    s=execute(s,'manager',{type:'CHN_CHANNEL_STATUS',channel:'tripkart',action:'connect'});
    assert.equal(M.chnChannel(s,'tripkart').status,'Connected');assert.equal(M.chnChannel(s,'tripkart').lastSync<s.clock,true);
    s=execute(s,'manager',{type:'CHN_PULL'});const c=M.chnSlice(s).messages.at(-1);assert.equal(c.kind,'Cancel');
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:c.id});
    assert.equal(bk(s,id).status,'Cancelled');assert.match(bk(s,id).cancelReason,/TK-30017/);assert.equal(available(s,'106','2026-10-25','2026-10-26'),true);
    // pending withdrawn
    let t=seed();t=execute(t,'manager',{type:'CHN_CHANNEL_STATUS',channel:'tripkart',action:'connect'});
    t=execute(t,'manager',{type:'CHN_PULL'});t=execute(t,'manager',{type:'CHN_PULL'});t=execute(t,'manager',{type:'CHN_PULL'});
    const cc=M.chnSlice(t).messages.find(m=>m.kind==='Cancel');t=execute(t,'reception',{type:'CHN_ACCEPT',id:cc.id});
    assert.equal(msg(t,'CHM-F4').status,'Rejected');assert.match(msg(t,'CHM-F4').reason,/before acceptance/);
  });
  test('[CHN] push preview: availability, markup, stop-sell, unmapped; push refused unless connected',()=>{
    let s=seed();
    const p=M.chnPushPreview(s,'booknest');assert.equal(p.rows.length,21);assert.deepEqual(p.unmapped,[]);
    const suite0=p.rows.find(r=>r.type==='Suite'&&r.date==='2026-10-25');assert.equal(suite0.avail,2);assert.equal(suite0.rate,495000);
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F3'});
    assert.equal(M.chnPushPreview(s,'booknest').rows.find(r=>r.type==='Suite'&&r.date==='2026-10-25').avail,1);
    const sh=M.chnPushPreview(s,'stayhub');assert.deepEqual(sh.unmapped,['Suite']);assert.equal(sh.rows.filter(r=>r.closed&&r.why==='Stop-sell').length,7);
    s=execute(s,'reception',{type:'CHN_STOP_SELL',channel:'ALL',roomType:'Suite',on:true});
    assert.equal(M.chnPushPreview(s,'booknest').rows.filter(r=>r.type==='Suite').every(r=>r.closed),true);
    s=execute(s,'reception',{type:'CHN_PUSH',channel:'booknest',key:'push-1'});
    assert.equal(M.chnSlice(s).pushes[0].closed,7);assert.equal(M.chnChannel(s,'booknest').lastSync,M.chnSlice(s).pushes[0].at);
    assert.deepEqual(execute(s,'reception',{type:'CHN_PUSH',channel:'booknest',key:'push-1'}),s);
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_PUSH',channel:'tripkart'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'CHN_PUSH',channel:'booknest'})),'FORBIDDEN');
  });
  test('[CHN] channel save: commission/markup bounds, codes, reception denied',()=>{
    const s=seed();
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_CHANNEL_SAVE',channel:'booknest',commissionBps:1500})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'manager',{type:'CHN_CHANNEL_SAVE',channel:'booknest',commissionBps:3500})),'VALIDATION');
    assert.equal(code(()=>execute(s,'manager',{type:'CHN_CHANNEL_SAVE',channel:'booknest',commissionBps:1500,mapping:{Classic:'X1',Deluxe:'x1'}})),'VALIDATION');
    assert.equal(code(()=>execute(s,'manager',{type:'CHN_CHANNEL_SAVE',channel:'booknest',commissionBps:1500,mapping:{Classic:'bad code!'}})),'VALIDATION');
    const t=execute(s,'manager',{type:'CHN_CHANNEL_SAVE',channel:'roomraahi',commissionBps:1250,markupBps:0,mapping:{Classic:'rr-std',Deluxe:''}});
    assert.deepEqual(M.chnChannel(t,'roomraahi').mapping,{Classic:'RR-STD'});assert.equal(M.chnChannel(t,'roomraahi').commissionBps,1250);
  });
  test('[CHN] commission report: 18% / 15% of agreed room revenue, cancelled excluded',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F1'});s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F2'});
    let r=M.chnCommissionReport(s);
    const bn=r.byChannel.find(x=>x.channel.id==='booknest'),sh=r.byChannel.find(x=>x.channel.id==='stayhub');
    assert.deepEqual([bn.bookings,bn.nights,bn.base,bn.commission,bn.net],[1,2,600000,108000,492000]);
    assert.deepEqual([sh.base,sh.commission],[200000,30000]);assert.equal(r.total.commission,138000);
    s=execute(s,'reception',{type:'CANCEL',id:msg(s,'CHM-F2').booking,reason:'Test'});
    r=M.chnCommissionReport(s);assert.equal(r.total.commission,108000);
  });
  test('[CHN] web quote: synthetic tax, rooms left, capacity, search errors',()=>{
    const s=seed();
    const q=M.chnWebQuote(s,'Deluxe','2026-10-25','2026-10-27',2,0);
    assert.deepEqual([q.nights,q.rate,q.net,q.tax,q.total,q.ok],[2,300000,600000,60000,660000,true]);
    assert.equal(M.chnWebQuote(s,'Classic','2026-10-25','2026-10-26',3,0).ok,false);
    assert.match(M.chnWebSearchError(s,'2026-10-20','2026-10-21',1,0),/past/);
    assert.match(M.chnWebSearchError(s,'2026-10-25','2026-10-25',1,0),/after check-in/);
    assert.equal(M.chnWebSearchError(s,'2026-10-25','2026-10-26',2,1),'');
  });
  test('[CHN] web request → Held booking; confirm → Confirmed; second confirm refused',()=>{
    let s=seed();const n=s.bookings.length;
    const base={type:'CHN_WEB_REQUEST',roomType:'Suite',arrival:'2026-10-25',departure:'2026-10-27',adults:2,children:1,guest:'Meher Ansari',phone:'+91 98000 12345',email:'meher@example.test'};
    assert.equal(code(()=>execute(s,'reception',{...base,phone:'12'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'housekeeper',base)),'FORBIDDEN');
    s=execute(s,'reception',base);
    const w=M.chnSlice(s).webRequests[0],b=bk(s,w.booking);
    assert.equal(s.bookings.length,n+1);assert.equal(b.status,'Held');assert.equal(b.source,'Website');assert.equal(b.room,'106');
    assert.deepEqual([w.net,w.tax,w.total],[900000,90000,990000]);assert.equal(M.chnWebStatus(s,w),'Pending');
    assert.equal(available(s,'106','2026-10-25','2026-10-26'),false);
    s=execute(s,'manager',{type:'CHN_WEB_CONFIRM',id:w.id});
    assert.equal(bk(s,w.booking).status,'Confirmed');assert.equal(M.chnSlice(s).webRequests[0].status,'Confirmed');
    assert.equal(code(()=>execute(s,'manager',{type:'CHN_WEB_CONFIRM',id:w.id})),'INVALID_TRANSITION');
  });
  test('[CHN] web decline releases the hold; expired hold cannot be confirmed but can be declined',()=>{
    let s=seed();
    const base={type:'CHN_WEB_REQUEST',roomType:'Suite',arrival:'2026-10-25',departure:'2026-10-26',adults:2,children:0,guest:'Ira Bhatt',phone:'9800012345'};
    s=execute(s,'reception',base);let w=M.chnSlice(s).webRequests[0];
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_WEB_DECLINE',id:w.id})),'VALIDATION');
    const d=execute(s,'reception',{type:'CHN_WEB_DECLINE',id:w.id,reason:'Suite reserved for maintenance'});
    assert.equal(bk(d,w.booking).status,'Cancelled');assert.equal(available(d,'106','2026-10-25','2026-10-26'),true);
    s=structuredClone(s);s.clock='2026-10-24T23:00';
    assert.equal(M.chnWebStatus(s,w),'Expired');
    assert.equal(code(()=>execute(s,'manager',{type:'CHN_WEB_CONFIRM',id:w.id})),'INVALID_TRANSITION');
    s=execute(s,'manager',{type:'CHN_WEB_DECLINE',id:w.id,reason:'Hold expired'});assert.equal(M.chnSlice(s).webRequests[0].status,'Declined');
  });
  test('[CHN] sold-out web request refused; day-closed blocks accept',()=>{
    let s=seed();
    for(const g of ['A One','B Two'])s=execute(s,'reception',{type:'CHN_WEB_REQUEST',roomType:'Suite',arrival:'2026-10-25',departure:'2026-10-26',adults:1,children:0,guest:g,phone:'9800012345'});
    assert.equal(M.chnWebQuote(s,'Suite','2026-10-25','2026-10-26').ok,false);
    assert.equal(code(()=>execute(s,'reception',{type:'CHN_WEB_REQUEST',roomType:'Suite',arrival:'2026-10-25',departure:'2026-10-26',adults:1,children:0,guest:'C Three',phone:'9800012345'})),'NOT_AVAILABLE');
    const c=structuredClone(seed());c.closedDates.push(c.date);
    assert.equal(code(()=>execute(c,'reception',{type:'CHN_ACCEPT',id:'CHM-F1'})),'DAY_CLOSED');
  });
}
