// Module 18 · Guest services, complaints, lost & found — domain/command tests. Names prefixed [GSV].
export function register({test,M,assert}){
  const {execute,baseFixture,HccError,totals,drawer,metrics}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const req=(s,id)=>M.gsvSlice(s).requests.find(r=>r.id===id);
  const last=(s,k)=>M.gsvSlice(s)[k].at(-1);

  test('[GSV] module slice defaults to empty',()=>{const s=baseFixture();delete s.servicesX;const x=M.gsvSlice(s);assert.deepEqual([x.requests.length,x.complaints.length,x.items.length],[0,0,0]);});
  test('[GSV] fixture leaves core figures untouched and seeds registers',()=>{
    const s=baseFixture();assert.equal(metrics(s).occupied,8);assert.equal(metrics(s).collections,2150000);assert.equal(drawer(s),1020000);
    const x=M.gsvSlice(s);assert.equal(x.requests.length,6);assert.equal(x.complaints.length,3);assert.equal(x.items.length,4);
    assert.equal(M.gsvOverdue(s,req(s,'SR-903')),true);assert.equal(M.gsvLate(req(s,'SR-902')),true);assert.equal(M.gsvLate(req(s,'SR-901')),false);
    assert.equal(M.gsvDisposable(s,x.items.find(i=>i.id==='LF-902')),true);assert.equal(M.gsvDisposable(s,x.items.find(i=>i.id==='LF-901')),false);
    assert.equal(M.gsvFollowUpDue(s,x.complaints.find(c=>c.id==='CMP-902')),true);
  });
  test('[GSV] request: create sets SLA due time on the simulated clock; urgent halves it',()=>{
    let s=baseFixture();const at=s.clock;
    s=execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1043',requestType:'Extra towels',detail:'2 towels'});
    const r=last(s,'requests');assert.equal(r.room,'104');assert.equal(r.dept,'Housekeeping');assert.equal(r.status,'Open');
    assert.equal(r.createdAt,at);assert.equal(r.dueAt,M.gsvAddMinutes(at,20));
    s=execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1043',requestType:'Taxi',priority:'Urgent'});
    const u=last(s,'requests');assert.equal(u.dept,'Front desk');assert.equal(M.gsvMinutesBetween(u.createdAt,u.dueAt),15);
  });
  test('[GSV] request: only in-house stays; type, wake-up time and permission validated',()=>{
    const s=baseFixture();
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1049',requestType:'Extra towels'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Spa'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Wake-up call',time:'7am'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Extra towels'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Extra towels',assignee:'maint'})),'VALIDATION');
    // Wake-up at a time already passed today is scheduled for tomorrow
    const w=execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Wake-up call',time:'06:00'});
    assert.equal(last(w,'requests').scheduledFor,'2026-10-25T06:00');assert.equal(last(w,'requests').dueAt,'2026-10-25T06:05');
  });
  test('[GSV] request: housekeeper works only own assignment; completion needs a guest note; idempotent',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'housekeeper2',{type:'GSV_REQUEST_START',id:'SR-904'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'maint',{type:'GSV_REQUEST_START',id:'SR-904'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'accountant',{type:'GSV_REQUEST_START',id:'SR-904'})),'FORBIDDEN');
    s=execute(s,'housekeeper',{type:'GSV_REQUEST_START',id:'SR-904'});assert.equal(req(s,'SR-904').status,'In progress');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'GSV_REQUEST_COMPLETE',id:'SR-904',note:''})),'VALIDATION');
    const done=execute(s,'housekeeper',{type:'GSV_REQUEST_COMPLETE',id:'SR-904',note:'Four bottles delivered.',key:'k-done'});
    const r=req(done,'SR-904');assert.equal(r.status,'Done');assert.equal(r.note,'Four bottles delivered.');assert.equal(r.doneAt,done.audit[0].at>r.doneAt?r.doneAt:r.doneAt);
    assert.deepEqual(execute(done,'housekeeper',{type:'GSV_REQUEST_COMPLETE',id:'SR-904',note:'again',key:'k-done'}),done);
    assert.equal(code(()=>execute(done,'housekeeper',{type:'GSV_REQUEST_COMPLETE',id:'SR-904',note:'again'})),'INVALID_TRANSITION');
    assert.equal(M.gsvVisibleOps(done,'housekeeper').every(x=>x.assignee==='housekeeper'),true);
    assert.equal(M.gsvVisibleOps(done,'supervisor').some(x=>x.dept==='Front desk'),false);
  });
  test('[GSV] request: assignment pool by department; supervisor may assign HK, not front desk; cancel needs reason',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_REQUEST_ASSIGN',id:'SR-903',assignee:'maint'})),'VALIDATION');
    s=execute(s,'supervisor',{type:'GSV_REQUEST_ASSIGN',id:'SR-903',assignee:'housekeeper2'});assert.equal(req(s,'SR-903').status,'Assigned');
    assert.equal(code(()=>execute(s,'supervisor',{type:'GSV_REQUEST_ASSIGN',id:'SR-905',assignee:'reception'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_REQUEST_CANCEL',id:'SR-903',reason:''})),'VALIDATION');
    s=execute(s,'reception',{type:'GSV_REQUEST_CANCEL',id:'SR-903',reason:'Guest found spare towels'});assert.equal(req(s,'SR-903').status,'Cancelled');
    assert.equal(M.gsvOverdue(s,req(s,'SR-903')),false);
  });
  test('[GSV] SLA: overdue label follows the simulated clock; report figures derived',()=>{
    const s=baseFixture();
    assert.match(M.gsvSlaLabel(s,req(s,'SR-903')).label,/Overdue · 10 min/);
    assert.match(M.gsvSlaLabel(s,req(s,'SR-904')).label,/Due in 5 min/);
    assert.match(M.gsvSlaLabel(s,req(s,'SR-902')).label,/Done late · 28 min/);
    const t=M.gsvResponseTotals(s);assert.deepEqual([t.done,t.within,t.pct,t.avg,t.overdue],[2,1,50,23,1]);
    const fd=M.gsvResponseReport(s,'dept').find(r=>r.key==='Front desk');assert.deepEqual([fd.done,fd.avg,fd.pct],[1,18,100]);
  });
  test('[GSV] complaint: log → investigate → resolve requires root cause + action → follow-up closes',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'housekeeper',{type:'GSV_COMPLAINT_LOG',guest:'X Y',category:'Noise',severity:'Low',description:'Too loud'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_COMPLAINT_LOG',booking:'HCC-1041',category:'Noise',severity:'Severe',description:'Too loud at night'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_COMPLAINT_LOG',booking:'HCC-1041',category:'Noise',severity:'Low',description:'x',owner:'housekeeper'})),'VALIDATION');
    s=execute(s,'reception',{type:'GSV_COMPLAINT_LOG',booking:'HCC-1041',category:'Amenities',severity:'Low',description:'Kettle not working'});
    const c=last(s,'complaints');assert.equal(c.guest,'Ananya Rao');assert.equal(c.room,'101');assert.equal(c.owner,'reception');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_COMPLAINT_RESOLVE',id:c.id})),'VALIDATION');
    s=execute(s,'reception',{type:'GSV_COMPLAINT_UPDATE',id:c.id,rootCause:'Faulty kettle element'});assert.equal(last(s,'complaints').status,'Investigating');
    s=execute(s,'reception',{type:'GSV_COMPLAINT_RESOLVE',id:c.id,recoveryAction:'Kettle replaced'});
    assert.equal(last(s,'complaints').status,'Resolved');assert.equal(last(s,'complaints').followUpOn,'2026-10-25');
    const again=execute(s,'reception',{type:'GSV_COMPLAINT_FOLLOWUP',id:c.id,outcome:'Still noisy kettle',satisfied:false});
    assert.equal(last(again,'complaints').status,'Investigating');
    s=execute(s,'reception',{type:'GSV_COMPLAINT_FOLLOWUP',id:c.id,outcome:'All good now',satisfied:true});assert.equal(last(s,'complaints').status,'Closed');
    assert.equal(M.gsvComplaintReport(s).find(r=>r.category==='Amenities').total,1);
  });
  test('[GSV] complaint: High severity can only be closed by a user with approve',()=>{
    let s=baseFixture();
    s=execute(s,'reception',{type:'GSV_COMPLAINT_LOG',booking:'HCC-1042',category:'Staff behaviour',severity:'High',description:'Rude at the desk'});
    const id=last(s,'complaints').id;
    s=execute(s,'reception',{type:'GSV_COMPLAINT_RESOLVE',id,rootCause:'Shift overload',recoveryAction:'Apology by manager'});
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_COMPLAINT_FOLLOWUP',id,outcome:'Fine',satisfied:true})),'FORBIDDEN');
    s=execute(s,'manager',{type:'GSV_COMPLAINT_FOLLOWUP',id,outcome:'Guest accepted apology',satisfied:true});assert.equal(last(s,'complaints').status,'Closed');
  });
  test('[GSV] recovery discount above limit goes through the existing DISCOUNT approval; no self-approval',()=>{
    let s=baseFixture();const b0=totals(s.bookings.find(b=>b.id==='HCC-1045'));
    assert.equal(code(()=>execute(s,'supervisor',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-901',amount:30000})),'FORBIDDEN');
    s=execute(s,'reception',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-901',amount:30000});
    const a=s.approvals.at(-1);assert.equal(a.status,'Pending');assert.equal(a.booking,'HCC-1045');assert.equal(a.amount,30000);assert.match(a.reason,/Service recovery · CMP-901/);
    assert.equal(a.kind,undefined);assert.equal(a.actor,'reception');
    const c=M.gsvSlice(s).complaints.find(c=>c.id==='CMP-901');assert.equal(c.recovery.approvalId,a.id);assert.equal(M.gsvRecoveryStatus(s,c),'Pending approval');
    assert.deepEqual(totals(s.bookings.find(b=>b.id==='HCC-1045')),b0);
    assert.ok(s.audit.some(x=>x.action==='DISCOUNT'));
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-901',amount:10000})),'INVALID_TRANSITION');
    assert.throws(()=>execute(s,'reception',{type:'APPROVAL',id:a.id,approve:true}));
    s=execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true});
    assert.equal(M.gsvRecoveryStatus(s,M.gsvSlice(s).complaints.find(c=>c.id==='CMP-901')),'Approved');
    assert.equal(totals(s.bookings.find(b=>b.id==='HCC-1045')).net,b0.net-30000);
  });
  test('[GSV] recovery discount within limit posts a discount line; needs a linked stay; closed day refused',()=>{
    let s=baseFixture();
    s=execute(s,'cashier',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-902',amount:10000});
    const b=s.bookings.find(b=>b.id==='HCC-1044'),line=b.lines.at(-1);
    assert.equal(line.kind,'discount');assert.equal(line.amount,-10000);
    const c=M.gsvSlice(s).complaints.find(c=>c.id==='CMP-902');assert.equal(c.recovery.lineId,line.id);assert.equal(M.gsvRecoveryStatus(s,c),'Posted');
    assert.equal(code(()=>execute(s,'owner',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-903',amount:10000})),'INVALID_TRANSITION');
    const closed=baseFixture();closed.closedDates.push(closed.date);
    assert.equal(code(()=>execute(closed,'reception',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-901',amount:10000})),'DAY_CLOSED');
    assert.equal(code(()=>execute(baseFixture(),'reception',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-901',amount:99999999})),'VALIDATION');
  });
  test('[GSV] lost & found: valuables to the safe; housekeeper may log; claim needs ID note without full number',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'housekeeper',{type:'GSV_LF_LOG',description:'Watch',category:'Jewellery',foundWhere:'Room 101',room:'101',storage:'Housekeeping store'})),'VALIDATION');
    s=execute(s,'housekeeper',{type:'GSV_LF_LOG',description:'Steel wrist watch',category:'Jewellery',foundWhere:'Under the bed',room:'101',storage:'Front office safe',foundBy:'manager'});
    const i=last(s,'items');assert.equal(i.valuable,true);assert.equal(i.foundBy,'housekeeper');assert.equal(i.booking,'HCC-1041');assert.equal(i.status,'Stored');
    assert.equal(code(()=>execute(s,'accountant',{type:'GSV_LF_LOG',description:'Pen',category:'Other',foundWhere:'Lobby',storage:'Housekeeping store'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'housekeeper',{type:'GSV_LF_CLAIM',id:i.id,name:'Ananya Rao',phone:'9800000111',idType:'Passport (fictional)',idNote:'ends 1234',matchNote:'Steel strap'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_LF_CLAIM',id:i.id,name:'Ananya Rao',phone:'9800000111',idType:'Passport (fictional)',idNote:'123456789012',matchNote:'Steel strap'})),'VALIDATION');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_LF_HANDOVER',id:i.id})),'INVALID_TRANSITION');
    s=execute(s,'reception',{type:'GSV_LF_CLAIM',id:i.id,name:'Ananya Rao',phone:'98000 00111',idType:'Passport (fictional)',idNote:'Photo matched · ends 1234',matchNote:'Steel strap, scratched glass'});
    assert.equal(last(s,'items').status,'Claimed');assert.equal(last(s,'items').claim.phone,'9800000111');
    s=execute(s,'reception',{type:'GSV_LF_HANDOVER',id:i.id});assert.equal(last(s,'items').status,'Handed over');assert.equal(last(s,'items').handover.to,'Ananya Rao');
  });
  test('[GSV] lost & found: disposal only after retention; valuables need approve and cannot be destroyed',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'owner',{type:'GSV_LF_DISPOSE',id:'LF-901',method:'Donated'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_LF_DISPOSE',id:'LF-902',method:'Sold'})),'VALIDATION');
    s=execute(s,'reception',{type:'GSV_LF_DISPOSE',id:'LF-902',method:'Donated',note:'City shelter'});
    assert.equal(M.gsvSlice(s).items.find(i=>i.id==='LF-902').status,'Disposed');
    // Age the valuable past 180 days
    let v=baseFixture();v.servicesX.items.find(i=>i.id==='LF-901').foundAt='2026-01-01T10:00';
    assert.equal(code(()=>execute(v,'reception',{type:'GSV_LF_DISPOSE',id:'LF-901',method:'Donated'})),'FORBIDDEN');
    assert.equal(code(()=>execute(v,'manager',{type:'GSV_LF_DISPOSE',id:'LF-901',method:'Destroyed'})),'VALIDATION');
    v=execute(v,'manager',{type:'GSV_LF_DISPOSE',id:'LF-901',method:'Handed to police'});
    assert.equal(M.gsvLostFoundSummary(v).disposed,1);
  });
  test('[GSV] settings: approve permission; SLA bounds; retention affects disposal date',()=>{
    let s=baseFixture();
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_SETTINGS',retentionDays:60})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'manager',{type:'GSV_SETTINGS',sla:{'Extra towels':2}})),'VALIDATION');
    s=execute(s,'manager',{type:'GSV_SETTINGS',sla:{'Extra towels':30},retentionDays:30,valuableRetentionDays:60});
    assert.equal(M.gsvSlaMinutes(s,'Extra towels'),30);
    assert.equal(M.gsvDisposeOn(s,M.gsvSlice(s).items.find(i=>i.id==='LF-902')),'2026-07-30');
  });
  test('[GSV] state with module data survives JSON round trip (no undefined fields)',()=>{
    let s=baseFixture();
    s=execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Drinking water'});
    s=execute(s,'reception',{type:'GSV_COMPLAINT_LOG',guest:'Walk-in guest',category:'Other',severity:'Low',description:'Parking was full'});
    s=execute(s,'reception',{type:'GSV_LF_LOG',description:'Umbrella',category:'Other',foundWhere:'Porch',storage:'Back office cupboard'});
    s=execute(s,'reception',{type:'GSV_COMPLAINT_RECOVERY',id:'CMP-901',amount:30000});
    assert.deepEqual(JSON.parse(JSON.stringify(s)),s);
  });
}
