// Module 13 · Advanced tariff & yield — domain/command tests. Names prefixed [YLD].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const room=(s,type)=>s.rooms.find(r=>r.type===type);
  const pub=(s,uid='owner',extra={})=>execute(s,uid,{type:'YLD_PUBLISH',from:s.date,to:M.addDays(s.date,13),types:['Classic','Deluxe','Suite'],reason:'Test',...extra});

  test('[YLD] module slice: fixture rules loaded, absent slice falls back to defaults',()=>{
    const s=seed();const y=M.yldSlice(s);assert.equal(y.plans.length,5);assert.ok(y.plans.some(p=>p.dayUse));assert.equal(y.seasons.length,3);
    const old={...s};delete old.yieldX;assert.equal(M.yldSlice(old).plans.length,5);assert.equal(M.yldNight(old,'Classic','2026-10-27').suggested,M.yldNight(old,'Classic','2026-10-27').base);
  });
  test('[YLD] fixture leaves core figures and tariffs unchanged',()=>{const s=seed();assert.equal(M.metrics(s).occupied,8);assert.deepEqual(s.tariffs,[]);assert.equal(M.rateFor(s,room(s,'Classic'),s.date),200000);});
  test('[YLD] base → weekday → occupancy explanation (Sat 24 Oct Classic)',()=>{
    const n=M.yldNight(seed(),'Classic','2026-10-24');assert.equal(n.base,200000);assert.equal(n.occ.pct,61);
    assert.deepEqual(n.steps.map(x=>x.bps),[1000]);assert.equal(n.suggested,220000);assert.match(n.explain,/Sat \+10%/);
  });
  test('[YLD] low occupancy tier lowers the rate; season applies only to listed types',()=>{
    const s=seed();assert.equal(M.yldNight(s,'Classic','2026-11-02').suggested,180000);
    assert.equal(M.yldNight(s,'Classic','2026-10-28').suggested,200000);assert.equal(M.yldNight(s,'Deluxe','2026-10-28').suggested,325000);
  });
  test('[YLD] highest occupancy tier only + ceiling clamp + ₹50 rounding',()=>{
    const s=seed();const n=M.yldNight(s,'Suite','2026-11-07');
    assert.equal(n.rawBps,2000+1000+1500);assert.equal(n.appliedBps,4000);assert.equal(n.clamped,'ceiling');assert.equal(n.suggested,630000);
    assert.equal(M.yldRound(203333,5000),205000);
  });
  test('[YLD] forecast occupancy = max(on the books, simulated forecast) over sellable rooms',()=>{
    const s=seed();const o=M.yldOcc(s,'2026-10-24');assert.equal(o.sellable,23);assert.equal(o.onBooks,10);assert.equal(o.forecast,14);assert.equal(o.simulated,true);
  });
  test('[YLD] base follows a manual tariff schedule, never yield-owned rows',()=>{
    let s=execute(seed(),'owner',{type:'TARIFF_SCHEDULE',roomType:'Classic',rate:220000,effectiveFrom:'2026-10-27'});
    assert.equal(M.yldBase(s,'Classic','2026-10-27').rate,220000);
    s=pub(s);assert.equal(M.yldBase(s,'Classic','2026-10-31').rate,220000);assert.equal(M.yldBase(s,'Classic','2026-10-26').rate,200000);
  });
  test('[YLD] publish writes effective-dated tariffs; new bookings use them; existing bookings untouched',()=>{
    let s=seed();const before=JSON.stringify(s.bookings);s=pub(s);
    assert.equal(JSON.stringify(s.bookings),before);
    assert.equal(M.rateFor(s,room(s,'Suite'),'2026-11-06'),630000);assert.equal(M.rateFor(s,room(s,'Classic'),'2026-11-02'),180000);
    assert.equal(M.yldPendingNights(s).length,0);
    assert.equal(M.rateFor(s,room(s,'Suite'),'2026-11-07'),450000,'rate after the window unchanged (closing row)');
    s=execute(s,'owner',{type:'BOOK',guest:'Yield Test',room:'105',arrival:'2026-10-31',departure:'2026-11-01',people:1});
    assert.equal(s.bookings.at(-1).rate,240000);assert.equal(s.bookings.find(b=>b.id==='HCC-1041').rate,200000);
    assert.equal(M.yldSlice(s).publishes.length,1);assert.ok(M.yldSlice(s).history[0].area.startsWith('Publish'));
  });
  test('[YLD] publish is idempotent by key and fails when nothing changes',()=>{
    let s=pub(seed(),'owner',{key:'yld-1'});assert.deepEqual(pub(s,'owner',{key:'yld-1'}),s);
    assert.throws(()=>pub(s),/Nothing to publish/);
  });
  test('[YLD] partial window keeps later nights unchanged',()=>{
    const s=execute(seed(),'owner',{type:'YLD_PUBLISH',from:'2026-10-31',to:'2026-10-31',types:['Classic']});
    assert.equal(M.rateFor(s,room(s,'Classic'),'2026-10-31'),240000);assert.equal(M.rateFor(s,room(s,'Classic'),'2026-11-01'),200000);assert.equal(M.rateFor(s,room(s,'Classic'),'2026-10-30'),200000);
  });
  test('[YLD] cannot publish past nights, closed business day, or beyond the window',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'owner',{type:'YLD_PUBLISH',from:'2026-10-23',to:'2026-10-25',types:['Classic']}),e=>e.code==='INVALID_TRANSITION');
    assert.throws(()=>execute(s,'owner',{type:'YLD_PUBLISH',from:'2026-10-24',to:'2026-11-20',types:['Classic']}),/window/);
    const c={...structuredClone(s),closedDates:[...s.closedDates,s.date]};
    assert.throws(()=>execute(c,'owner',{type:'YLD_PUBLISH',from:s.date,to:s.date,types:['Classic']}),/closed/);
    assert.throws(()=>execute(s,'owner',{type:'YLD_PUBLISH',from:s.date,to:s.date,types:[]}),/room type/);
  });
  test('[YLD] rule changes are Owner-only (Manager, Accountant, Tech admin, Reception denied)',()=>{
    const s=seed();const cmd={type:'YLD_DOW_SET',dow:[0,0,0,0,0,800,1200]};
    for(const u of ['manager','accountant','techadmin','reception','housekeeper'])assert.throws(()=>execute(s,u,cmd),e=>e.code==='FORBIDDEN');
    assert.throws(()=>pub(s,'manager'),e=>e.code==='FORBIDDEN'&&/propose/.test(e.message));
    const n=execute(s,'owner',cmd);assert.equal(M.yldNight(n,'Classic','2026-10-30').steps[0].bps,800);
  });
  test('[YLD] seasons: add, priority on overlap, validation, remove with history',()=>{
    let s=execute(seed(),'owner',{type:'YLD_SEASON_SAVE',name:'Test peak',from:'2026-11-06',to:'2026-11-06',adjBps:3000,types:[],priority:5,reason:'demo'});
    const id=M.yldSlice(s).seasons.at(-1).id;assert.equal(M.yldSeasonFor(s,'Classic','2026-11-06').name,'Test peak');
    assert.throws(()=>execute(s,'owner',{type:'YLD_SEASON_SAVE',name:'Bad',from:'2026-11-06',to:'2026-11-01',adjBps:1000,types:[]}),/end on or after/);
    assert.throws(()=>execute(s,'owner',{type:'YLD_SEASON_SAVE',name:'Bad',from:'2026-11-06',to:'2026-11-07',adjBps:20000,types:[]}),/adjustment/);
    s=execute(s,'owner',{type:'YLD_SEASON_SAVE',id,remove:true});assert.equal(M.yldSeasonFor(s,'Classic','2026-11-06').name,'Diwali festive');
    assert.equal(M.yldSlice(s).history[0].after,'removed');assert.equal(M.yldSlice(s).history[1].reason,'demo');
  });
  test('[YLD] occupancy rules validated (direction, duplicates)',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'owner',{type:'YLD_OCC_SAVE',op:'>=',occ:80,adjBps:500}),/already exists/);
    assert.throws(()=>execute(s,'owner',{type:'YLD_OCC_SAVE',op:'>=',occ:70,adjBps:-500}),/raise/);
    const n=execute(s,'owner',{type:'YLD_OCC_SAVE',op:'>=',occ:60,adjBps:500});assert.equal(M.yldNight(n,'Classic','2026-10-25').suggested,210000);
  });
  test('[YLD] guardrails clamp the floor',()=>{
    let s=execute(seed(),'owner',{type:'YLD_GUARD_SET',floorBps:-500,ceilBps:4000,roundTo:5000});
    const n=M.yldNight(s,'Classic','2026-11-02');assert.equal(n.clamped,'floor');assert.equal(n.suggested,190000);
  });
  test('[YLD] override replaces computed rate and can be cleared',()=>{
    let s=execute(seed(),'owner',{type:'YLD_OVERRIDE',roomType:'Deluxe',date:'2026-10-27',rate:350000,reason:'Corporate block'});
    assert.equal(M.yldNight(s,'Deluxe','2026-10-27').suggested,350000);assert.match(M.yldNight(s,'Deluxe','2026-10-27').explain,/override/);
    assert.throws(()=>execute(s,'owner',{type:'YLD_OVERRIDE',roomType:'Deluxe',date:'2026-10-27',rate:350000,reason:''}),/reason/);
    s=execute(s,'owner',{type:'YLD_OVERRIDE',roomType:'Deluxe',date:'2026-10-27',rate:null,reason:'Block released'});assert.equal(M.yldNight(s,'Deluxe','2026-10-27').suggested,300000);
  });
  test('[YLD] rate plans derive from BAR; BAR fixed; day-use closes on very high demand',()=>{
    const s=seed();const y=M.yldSlice(s),corp=y.plans.find(p=>p.id==='CORP'),day=y.plans.find(p=>p.id==='DAY');
    assert.equal(M.yldPlanRate(s,corp,'Classic','2026-10-27'),175000);
    assert.equal(M.yldPlanAvailable(s,day,'2026-11-07').ok,false);assert.equal(M.yldPlanAvailable(s,day,'2026-10-27').ok,true);
    assert.equal(M.yldQuote(s,'DAY','Classic','2026-10-27',3).total,110000);
    assert.throws(()=>execute(s,'owner',{type:'YLD_PLAN_SAVE',id:'BAR',adjBps:-500}),/reference/);
    const n=execute(s,'owner',{type:'YLD_PLAN_SAVE',id:'CORP',adjBps:-1000,minNights:1,active:true});assert.equal(M.yldPlanRate(n,M.yldSlice(n).plans.find(p=>p.id==='CORP'),'Classic','2026-10-27'),180000);
  });
  test('[YLD] quotes report restrictions: min stay, closed to arrival, plan rules',()=>{
    const s=seed();
    assert.ok(M.yldCheckStay(s,'Classic','2026-11-07','2026-11-08').some(x=>/Minimum stay 2/.test(x)));
    assert.equal(M.yldCheckStay(s,'Classic','2026-11-07','2026-11-09').length,0);
    assert.ok(M.yldCheckStay(s,'Suite','2026-11-08','2026-11-10').some(x=>/Closed to arrival/.test(x)));
    assert.ok(M.yldQuote(s,'LONG','Deluxe','2026-10-26',3).issues.some(x=>/at least 7/.test(x)));
    assert.ok(M.yldQuote(s,'WKND','Deluxe','2026-10-26',2).issues.some(x=>/Not sold on Mon/.test(x)));
    assert.equal(M.yldQuote(s,'LONG','Classic','2026-10-26',7).rows.length,7);
  });
  test('[YLD] restrictions: Owner adds/removes; validation',()=>{
    let s=execute(seed(),'owner',{type:'YLD_RESTRICTION_SAVE',roomType:'Deluxe',from:'2026-10-30',to:'2026-10-31',minStay:3,cta:false});
    assert.equal(M.yldRestrictionsFor(s,'Deluxe','2026-10-31').length,1);assert.equal(M.yldRestrictionsFor(s,'Classic','2026-10-31').length,0);
    assert.throws(()=>execute(s,'owner',{type:'YLD_RESTRICTION_SAVE',roomType:'*',from:'2026-10-30',to:'2026-10-31',minStay:1,cta:false}),/minimum stay/);
    s=execute(s,'owner',{type:'YLD_RESTRICTION_SAVE',id:M.yldSlice(s).restrictions.at(-1).id,remove:true});assert.equal(M.yldRestrictionsFor(s,'Deluxe','2026-10-31').length,0);
  });
  test('[YLD] Manager proposes → Owner approves → tariffs published; self-approval blocked',()=>{
    let s=execute(seed(),'manager',{type:'YLD_PROPOSE',from:'2026-10-30',to:'2026-11-01',types:['Classic'],reason:'Weekend demand'});
    const a=s.approvals.at(-1);assert.equal(a.kind,'yield');assert.equal(a.meta.command,'YLD_DECIDE');assert.equal(M.yldPendingProposals(s).length,1);
    assert.deepEqual(s.tariffs,[]);
    assert.throws(()=>execute(s,'manager',{type:'YLD_DECIDE',id:a.id,approve:true}),/own request/);
    assert.throws(()=>execute(s,'manager',{type:'YLD_PROPOSE',from:'2026-10-30',to:'2026-10-30',types:['Deluxe']}),/already pending/);
    assert.throws(()=>execute(s,'accountant',{type:'YLD_PROPOSE',from:'2026-10-30',to:'2026-10-30',types:['Deluxe']}),e=>e.code==='FORBIDDEN');
    s=execute(s,'owner',{type:'YLD_DECIDE',id:a.id,approve:true});
    assert.equal(s.approvals.at(-1).status,'Executed');assert.equal(M.rateFor(s,room(s,'Classic'),'2026-10-31'),240000);assert.equal(M.yldSlice(s).publishes[0].approval,a.id);
  });
  test('[YLD] stale or rejected proposals do not publish',()=>{
    let s=execute(seed(),'manager',{type:'YLD_PROPOSE',from:'2026-10-30',to:'2026-11-01',types:['Classic']});const id=s.approvals.at(-1).id;
    const changed=execute(s,'owner',{type:'TARIFF_SCHEDULE',roomType:'Classic',rate:210000,effectiveFrom:'2026-10-29'});
    assert.equal(M.yldProposalStatus(changed,changed.approvals.at(-1)),'Stale');
    assert.throws(()=>execute(changed,'owner',{type:'YLD_DECIDE',id,approve:true}),e=>e.code==='STALE_VERSION');
    s=execute(s,'owner',{type:'YLD_DECIDE',id,approve:false});assert.equal(s.approvals.at(-1).status,'Rejected');assert.deepEqual(s.tariffs,[]);
    assert.throws(()=>execute(s,'owner',{type:'YLD_DECIDE',id,approve:true}),/no longer pending/);
  });
  test('[YLD] proposal expires on the simulated clock',()=>{
    let s=execute(seed(),'manager',{type:'YLD_PROPOSE',from:'2026-10-30',to:'2026-10-31',types:['Suite']});const a=s.approvals.at(-1);
    s=structuredClone(s);s.clock='2026-10-25T09:00';
    assert.equal(M.yldProposalStatus(s,s.approvals.at(-1)),'Expired');
    assert.throws(()=>execute(s,'owner',{type:'YLD_DECIDE',id:a.id,approve:true}),/expired/);
  });
}
