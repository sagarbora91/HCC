// M1 tests: typed errors, simulated clock, scenarios, fault simulation, persistence, scope rules, S0 walkthrough oracle.
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const {execute,loadScenario,scenarios,baseFixture,drawer,metrics,totals,executeWithFault,restoreState,HccError,SCHEMA}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};

  // BL-004 typed errors
  test('Typed error: overlapping booking → NOT_AVAILABLE',()=>{let s=baseFixture();s=execute(s,'owner',{type:'BOOK',guest:'A Guest',room:'105',arrival:s.date,departure:'2026-10-26',people:1});assert.equal(code(()=>execute(s,'owner',{type:'BOOK',guest:'B Guest',room:'105',arrival:'2026-10-25',departure:'2026-10-27',people:1})),'NOT_AVAILABLE');});
  test('Typed error: housekeeper payment → FORBIDDEN',()=>{assert.equal(code(()=>execute(baseFixture(),'housekeeper',{type:'PAYMENT',id:'HCC-1041',amount:100,method:'Cash'})),'FORBIDDEN');});
  test('Typed error: action on closed day → DAY_CLOSED',()=>{let s=loadScenario('S1');s=execute(s,'manager',{type:'SHIFT_CLOSE',counted:drawer(s)});s=execute(s,'manager',{type:'DAY_CLOSE'});assert.equal(code(()=>execute(s,'owner',{type:'EXPENSE',vendor:'V',category:'Supplies',amount:100,method:'Cash'})),'DAY_CLOSED');});
  test('Typed error: unknown booking → NOT_FOUND; bad input → VALIDATION',()=>{assert.equal(code(()=>execute(baseFixture(),'owner',{type:'REGISTER',id:'NOPE'})),'NOT_FOUND');assert.equal(code(()=>execute(baseFixture(),'owner',{type:'BOOK',guest:'X',room:'105',arrival:'2026-10-24',departure:'2026-10-25',people:1})),'VALIDATION');});

  // BL-003 simulated clock (DEC-030, QG-P10)
  test('Clock: audit uses simulated time; each command advances one minute',()=>{let s=baseFixture();assert.equal(s.clock,'2026-10-24T08:00');s=execute(s,'owner',{type:'NIGHT_POST'});assert.equal(s.clock,'2026-10-24T08:01');assert.equal(s.audit[0].at,'2026-10-24T08:01');});
  test('Clock: opening the next day resets to 08:00 on the new date',()=>{let s=loadScenario('S1');s=execute(s,'manager',{type:'SHIFT_CLOSE',counted:drawer(s)});s=execute(s,'manager',{type:'DAY_CLOSE'});s=execute(s,'manager',{type:'NEXT_DAY'});assert.equal(s.date,'2026-10-25');assert.equal(s.clock,'2026-10-25T08:00');});
  test('Determinism: same scenario + same commands → identical state',()=>{assert.deepEqual(walkthrough().s,walkthrough().s);});
  test('Determinism: no real-clock dependency (Date.now shifted)',()=>{const real=Date.now;Date.now=()=>real()+400*86400000;try{const a=walkthrough().s;Date.now=real;assert.deepEqual(a,walkthrough().s);}finally{Date.now=real;}});

  // BL-005 idempotency
  test('Idempotency: repeated keyed receipt has one effect',()=>{let s=baseFixture();const c={type:'PAYMENT',id:'HCC-1049',amount:5000,method:'UPI',reference:'DEMO-X',key:'pay:1'};s=execute(s,'cashier',c);const again=execute(s,'cashier',c);assert.equal(again,s);assert.equal(s.bookings.find(b=>b.id==='HCC-1049').receipts.length,2);});
  test('Idempotency: disabled user is denied even with a previously used key',()=>{let s=baseFixture();const c={type:'NIGHT_POST',key:'np:1'};s=execute(s,'manager',c);s=execute(s,'owner',{type:'USER',id:'manager'});assert.equal(code(()=>execute(s,'manager',c)),'FORBIDDEN');});

  // BL-006 fault simulation (AC-022)
  test('Fault: reject leaves state unchanged',()=>{const s=baseFixture();const r=executeWithFault(s,'cashier',{type:'PAYMENT',id:'HCC-1049',amount:5000,method:'UPI',reference:'R',key:'k1'},'reject');assert.equal(r.state,s);assert.equal(r.error.code,'SIMULATED_REJECTION');});
  test('Fault: stale leaves state unchanged',()=>{const s=baseFixture();const r=executeWithFault(s,'cashier',{type:'PAYMENT',id:'HCC-1049',amount:5000,method:'UPI',reference:'R',key:'k1'},'stale');assert.equal(r.state,s);assert.equal(r.error.code,'STALE_VERSION');});
  test('Fault: lost response commits once; retry with same key does not duplicate',()=>{const s=baseFixture(),c={type:'PAYMENT',id:'HCC-1049',amount:5000,method:'UPI',reference:'R',key:'k-lost'};const r=executeWithFault(s,'cashier',c,'lost');assert.equal(r.error.code,'RESULT_UNKNOWN');const retry=executeWithFault(r.state,'cashier',c,'none');assert.equal(retry.state,r.state);assert.equal(retry.state.bookings.find(b=>b.id==='HCC-1049').receipts.length,2);});
  test('Fault: latency mode executes normally',()=>{const s=baseFixture();const r=executeWithFault(s,'owner',{type:'NIGHT_POST'},'latency');assert.equal(r.error,undefined);assert.notEqual(r.state,s);});

  // BL-007 scenarios + persistence (AC-024)
  test('Scenarios: every available scenario loads with its own ID and schema',()=>{for(const x of scenarios.filter(x=>x.available)){const s=loadScenario(x.id);assert.equal(s.scenario,x.id);assert.equal(s.schema,SCHEMA);assert.equal(s.rooms.length,24);}});
  test('Scenarios: unbuilt scenarios refuse to load',()=>{for(const x of scenarios.filter(x=>!x.available))assert.throws(()=>loadScenario(x.id),/not available/);});
  test('Scenario S0: catalogue figures (drawer ₹10,750 = ₹10,200 + ₹550 restaurant cash, no arrivals today)',()=>{const s=loadScenario('S0'),m=metrics(s);assert.equal(drawer(s),1075000);assert.equal(m.occupied,8);assert.equal(s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date).length,0);});
  test('Scenario S1: empty hotel, all rooms sellable and ready',()=>{const s=loadScenario('S1'),m=metrics(s);assert.equal(m.occupied,0);assert.equal(m.available,24);assert.equal(drawer(s),500000);assert.equal(s.tasks.length,0);});
  test('Scenario S2: 105 blocked; arrival in dirty 108 cannot check in',()=>{let s=loadScenario('S2');assert.equal(code(()=>execute(s,'reception',{type:'BOOK',guest:'Test Guest',room:'105',arrival:s.date,departure:'2026-10-25',people:1})),'NOT_AVAILABLE');s=execute(s,'reception',{type:'REGISTER',id:'HCC-1051'});assert.equal(code(()=>execute(s,'reception',{type:'CHECKIN',id:'HCC-1051'})),'NOT_AVAILABLE');});
  test('Scenario S3: overdue unpaid departure blocks checkout; stale approval rejected',()=>{const s=loadScenario('S3');assert.equal(totals(s.bookings.find(b=>b.id==='HCC-1042')).balance,20000);assert.equal(code(()=>execute(s,'reception',{type:'CHECKOUT',id:'HCC-1042'})),'INVALID_TRANSITION');assert.equal(code(()=>execute(s,'manager',{type:'APPROVAL',id:'APR-1001',approve:true})),'STALE_VERSION');});
  test('Scenario S6: disabled second owner and housekeeper are denied',()=>{const s=loadScenario('S6');assert.equal(code(()=>execute(s,'owner2',{type:'NIGHT_POST'})),'FORBIDDEN');assert.equal(code(()=>execute(s,'housekeeper2',{type:'TASK',id:'task-1',action:'start'})),'FORBIDDEN');assert.equal(s.grants.Cashier.includes('reports'),false);});
  test('Persistence: empty storage → base scenario, no reset notice',()=>{const r=restoreState(null);assert.equal(r.state.scenario,'BASE');assert.equal(r.reset,false);});
  test('Persistence: valid stored state round-trips',()=>{const s=execute(baseFixture(),'owner',{type:'NIGHT_POST'});const r=restoreState(JSON.stringify(s));assert.deepEqual(r.state,s);assert.equal(r.reset,false);});
  test('Persistence: older schema, corrupt data or legacy key → safe reset with reason',()=>{assert.equal(restoreState(JSON.stringify({schema:1,bookings:[]})).reset,true);assert.equal(restoreState('{not json').reset,true);const r=restoreState(null,true);assert.equal(r.reset,true);assert.match(r.reason,/earlier prototype/);});

  // BL-009 roles and scope rules
  test('Roles: Maintenance limited to assigned tasks',()=>{const s=baseFixture();assert.equal(code(()=>execute(s,'maint',{type:'TASK',id:'task-0',action:'start'})),'FORBIDDEN');});
  test('Roles: cannot edit own role (self-escalation) or delegate admin',()=>{const s=baseFixture();assert.equal(code(()=>execute(s,'techadmin',{type:'GRANT',role:'Tech admin',permission:'billing'})),'FORBIDDEN');assert.equal(code(()=>execute(s,'techadmin',{type:'GRANT',role:'Reception',permission:'admin'})),'FORBIDDEN');});
  test('Fixture: demo property renamed; 10 demo users; 23 Oct recorded as closed',()=>{const s=baseFixture();assert.equal(s.settings.hotel,'HCC Demo Residency');assert.equal(s.users.length,10);assert.deepEqual(s.closedDates,['2026-10-23']);});

  // S0 walkthrough oracle (Fixture Catalogue §4, AC-001/AC-006)
  test('S0 walkthrough: Room 105 ₹4,400, drawer figures, room ready, rerun safe, disabled user denied',()=>{
    const w=walkthrough();
    assert.deepEqual(w.folio26,{net:400000,tax:40000,total:440000,paid:440000,deposit:0,balance:0});
    assert.equal(w.balance25,340000);assert.deepEqual(w.drawers,[1075000,1125000,1125000,1275000,1245000]);
    assert.equal(w.s.rooms.find(r=>r.id==='105').hk,'Ready');
    assert.equal(w.s.tasks.filter(t=>t.booking===w.id).length,1);
    assert.equal(w.rerun,'DAY_CLOSED');assert.equal(w.disabled,'FORBIDDEN');assert.equal(w.selfApprove,'FORBIDDEN');
  });

  function walkthrough(){
    let s=loadScenario('S0');const X=(u,c)=>{s=execute(s,u,c);};const drawers=[drawer(s)];
    X('reception',{type:'BOOK',guest:'Rahul Verma (fictional)',room:'105',arrival:'2026-10-24',departure:'2026-10-26',people:2,source:'Phone',key:'w-book'});
    const id=s.bookings.at(-1).id;
    X('reception',{type:'PAYMENT',id,amount:100000,method:'UPI',kind:'payment',reference:'DEMO-UPI-0001',key:'w-adv'});
    X('reception',{type:'REGISTER',id});X('reception',{type:'CHECKIN',id});
    X('cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit',key:'w-dep'});drawers.push(drawer(s));
    X('manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});X('manager',{type:'NIGHT_POST'});
    X('manager',{type:'SHIFT_CLOSE',counted:drawer(s)});s=tillClear(M,s);X('manager',{type:'DAY_CLOSE'});X('manager',{type:'NEXT_DAY'});drawers.push(drawer(s));
    X('reception',{type:'CHARGE',id,amount:50000,label:'Laundry service'});
    X('reception',{type:'DISCOUNT',id,amount:50000,reason:'Service delay goodwill'});
    const apr=s.approvals.at(-1).id;const selfApprove=code(()=>execute(s,'reception',{type:'APPROVAL',id:apr,approve:true}));
    X('manager',{type:'APPROVAL',id:apr,approve:true});X('manager',{type:'NIGHT_POST'});
    const balance25=totals(s.bookings.find(b=>b.id===id)).balance;
    X('manager',{type:'SHIFT_CLOSE',counted:drawer(s)});s=tillClear(M,s);X('manager',{type:'DAY_CLOSE'});X('manager',{type:'NEXT_DAY'});
    X('cashier',{type:'PAYMENT',id,amount:200000,method:'Cash',kind:'payment'});
    X('cashier',{type:'PAYMENT',id,amount:140000,method:'UPI',kind:'payment',reference:'DEMO-UPI-0002'});
    X('cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit-return'});drawers.push(drawer(s));
    const folio26=totals(s.bookings.find(b=>b.id===id));
    X('reception',{type:'CHECKOUT',id,key:'checkout:'+id});
    const t=s.tasks.find(t=>t.booking===id);
    X('housekeeper',{type:'TASK',id:t.id,action:'start'});[0,1,2].forEach(i=>X('housekeeper',{type:'TASK',id:t.id,action:'check',index:i}));
    X('housekeeper',{type:'TASK',id:t.id,action:'complete'});X('supervisor',{type:'TASK',id:t.id,action:'inspect'});
    X('cashier',{type:'EXPENSE',vendor:'Aqua Water Cans',category:'Supplies',amount:30000,method:'Cash'});
    X('manager',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});drawers.push(drawer(s));
    X('manager',{type:'NIGHT_POST'});X('manager',{type:'SHIFT_CLOSE',counted:drawer(s)});s=tillClear(M,s);X('manager',{type:'DAY_CLOSE'});
    const rerun=code(()=>execute(s,'manager',{type:'DAY_CLOSE'}));
    X('owner',{type:'USER',id:'reception'});
    const disabled=code(()=>execute(s,'reception',{type:'BOOK',guest:'X Y',room:'106',arrival:'2026-10-27',departure:'2026-10-28',people:1}));
    return {s,id,folio26,balance25,drawers,rerun,disabled,selfApprove};
  }
}
