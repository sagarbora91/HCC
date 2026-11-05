// F12 MEAS · meas-forecast tests (MEAS-CONTRACT §10). Names prefixed [F12-FORECAST].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const T='[F12-FORECAST] ';
  const withCfg=(s,values)=>({...s,configX:{...(s.configX??{}),values:{...(s.configX?.values??{}),...values}}});
  const ev=(extra={})=>({type:'MEAS_EVENT_SAVE',name:'Agri trade fair',from:'2026-12-04',to:'2026-12-06',kind:'fair',impactPct:25,...extra});

  // ── commands ──
  test(T+'event save: Manager and Owner only; validation; same row twice is unchanged; update by id',()=>{
    assert.throws(()=>execute(seed(),'reception',ev()),/Access denied|permission/i);
    assert.throws(()=>execute(seed(),'housekeeper',ev()),/Access denied|permission/i);
    assert.throws(()=>execute(seed(),'manager',ev({impactPct:150})),/between −50 and \+100/);
    assert.throws(()=>execute(seed(),'manager',ev({to:'2026-12-01'})),/end date/);
    assert.throws(()=>execute(seed(),'manager',ev({kind:'party'})),/event kind/);
    assert.throws(()=>execute(seed(),'manager',ev({name:'x'})),/name/);
    let s=execute(seed(),'manager',ev({id:'EVT-A'}));assert.equal(s.measX.events.length,1);
    s=execute(s,'owner',ev({id:'EVT-A'}));assert.equal(s.measX.events.length,1);
    s=execute(s,'owner',ev({id:'EVT-A',impactPct:30}));assert.equal(s.measX.events[0].impactPct,30);
    assert.ok(s.audit.some(a=>/Event (added|updated) · Agri trade fair/.test(a.detail)));
  });
  test(T+'event delete is repeat-safe; Reception refused',()=>{
    let s=execute(seed(),'manager',ev({id:'EVT-A'}));
    assert.throws(()=>execute(s,'reception',{type:'MEAS_EVENT_DELETE',id:'EVT-A'}),/Access denied|permission/i);
    s=execute(s,'manager',{type:'MEAS_EVENT_DELETE',id:'EVT-A'});assert.equal(s.measX.events.length,0);
    s=execute(s,'manager',{type:'MEAS_EVENT_DELETE',id:'EVT-A'});assert.equal(s.measX.events.length,0);
  });
  test(T+'MEAS_OFF: a switched-off item refuses its capture (stub-aware until meas-settings lands)',()=>{
    const off=withCfg(seed(),{'meas.rev.demand-calendar':false,'meas.pack.revenue':false});
    if(!M.measOn||M.measOn(off,'meas.rev.demand-calendar')){ // setup stub: everything on — the guard still runs and the command succeeds
      assert.equal(execute(off,'manager',ev()).measX.events.length,1);
    }else assert.throws(()=>execute(off,'manager',ev()),/switched off/);
  });
  test(T+'OTB snapshot: rows from the bookings as known on the day; same day twice replaces; back-fill writes one snapshot per Monday',()=>{
    const s0=seed();
    assert.throws(()=>execute(s0,'housekeeper',{type:'MEAS_OTB_SNAPSHOT'}),/Access denied|permission/i);
    assert.throws(()=>execute(s0,'owner',{type:'MEAS_OTB_SNAPSHOT',takenOn:M.addDays(s0.date,1)}),/future/);
    let s=execute(s0,'owner',{type:'MEAS_OTB_SNAPSHOT'});const n=s.measX.otb.length;assert.ok(n>0);
    assert.ok(s.measX.otb.every(r=>r.takenOn===s0.date&&r.night>=s0.date&&Number.isInteger(r.revenuePaise)&&r.rooms>0));
    s=execute(s,'owner',{type:'MEAS_OTB_SNAPSHOT'});assert.equal(s.measX.otb.length,n);
    const from=M.addDays(s0.date,-28);s=execute(s,'owner',{type:'MEAS_OTB_SNAPSHOT',backfillFrom:from});
    const days=[...new Set(s.measX.otb.map(r=>r.takenOn))];
    const mondays=M.fcMondays(from,s0.date);assert.equal(mondays.length,4);assert.ok(days.filter(d=>d!==s0.date).every(d=>mondays.includes(d)));assert.ok(days.length>=2); // a Monday with nothing on the books writes no rows
    assert.ok(days.filter(d=>d!==s0.date).every(d=>M.FC_DAY(d)===1));
  });
  test(T+'competitor + rate shop: Manager entry, idempotent on the client id, unknown competitor and bad rate refused',()=>{
    let s=execute(seed(),'manager',{type:'MEAS_COMPETITOR_SAVE',id:'CMP-1',name:'Hotel Sample Residency'});
    assert.throws(()=>execute(s,'manager',{type:'MEAS_COMPETITOR_SAVE',name:'hotel sample residency'}),/already exists/);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_RATESHOP_ADD',clientId:'c1',competitor:'CMP-1',roomType:'Classic',stayDate:'2026-10-30',ratePaise:210000}),/Access denied|permission/i);
    const add={type:'MEAS_RATESHOP_ADD',clientId:'c1',competitor:'CMP-1',roomType:'Classic',stayDate:'2026-10-30',ratePaise:210000,withBreakfast:true,source:'phone'};
    s=execute(s,'manager',add);s=execute(s,'manager',add);assert.equal(s.measX.rateshop.length,1);
    assert.equal(s.measX.rateshop[0].date,s.date);assert.equal(s.measX.rateshop[0].ratePaise,210000);
    assert.throws(()=>execute(s,'manager',{...add,clientId:'c2',competitor:'CMP-9'}),/competitor/);
    assert.throws(()=>execute(s,'manager',{...add,clientId:'c3',ratePaise:2100.5}),/whole rupees/);
    assert.throws(()=>execute(s,'manager',{...add,clientId:'c4',roomType:'Penthouse'}),/room type/);
    s=execute(s,'manager',{type:'MEAS_COMPETITOR_SAVE',id:'CMP-1',name:'Hotel Sample Residency',active:false});
    assert.throws(()=>execute(s,'manager',{...add,clientId:'c5'}),/inactive/);
  });

  // ── forecast maths ──
  const st=(room,from,to,bookedOn,extra={})=>({room,from,to,bookedOn,...extra});
  test(T+'night counts: distinct rooms; cancelled and no-show stays excluded; on the books respects booking and cancellation dates',()=>{
    const xs=[st('101','2026-01-01','2026-01-03','2025-12-20'),st('101','2026-01-02','2026-01-03','2025-12-21'),st('102','2026-01-02','2026-01-03','2026-01-01'),
      st('103','2026-01-02','2026-01-04','2025-12-01',{cancelledOn:'2025-12-25'}),st('104','2026-01-02','2026-01-03','2025-12-01',{noShow:true})];
    const c=M.fcNightCounts(xs);assert.equal(c.get('2026-01-01'),1);assert.equal(c.get('2026-01-02'),2);
    assert.equal(M.fcOnBooks(xs,'2026-01-02','2025-12-24'),3); // 101, 103 (not yet cancelled), 104 (no-show still on the books before the night)
    assert.equal(M.fcOnBooks(xs,'2026-01-02','2025-12-31'),2);
    assert.equal(M.fcOnBooks(xs,'2026-01-02','2026-01-01'),3);
  });
  test(T+'baseline: last two years same weekday (2:1) beats the trailing mean; no history → none; events lift and are clamped',()=>{
    const rooms=10,xs=[];
    for(let d='2024-01-01';d<'2026-01-01';d=M.addDays(d,1)){const y=d<'2025-01-01'?3:6;for(let r=0;r<y;r++)xs.push(st(String(100+r),d,M.addDays(d,1),M.addDays(d,-30)));}
    // 2026-06-15: one year back (2025-06-16 ±2 weeks) = 60 %, two years back (2024-06-17 ±2 weeks) = 30 % → (2·60 + 30) ÷ 3 = 50 %.
    // Last year's night had 6 rooms and none on the books 166 days ahead → pickup 6; blend (0 + 6 + 5) ÷ 2 = 5.5 → 6 rooms.
    const g=M.fcForecastNight({stays:xs,rooms,asOf:'2026-01-01'},'2026-06-15');assert.equal(g.baselineSource,'last-years');assert.equal(g.baselinePct,50);
    assert.equal(g.pickup,6);assert.equal(g.rooms,6);
    const none=M.fcForecastNight({stays:[],rooms,asOf:'2026-01-01'},'2026-01-05');assert.equal(none.baselineSource,'none');assert.equal(none.rooms,0);
    const tr=M.fcForecastNight({stays:xs.filter(x=>x.from>='2025-11-01'),rooms,asOf:'2026-01-01',historyFrom:'2025-11-01'},'2026-01-05');
    assert.equal(tr.baselineSource,'trailing');assert.equal(tr.baselinePct,60);
    const up=M.fcForecastNight({stays:xs,rooms,asOf:'2026-01-01',events:[{id:'E',name:'Fair',from:'2026-06-15',to:'2026-06-15',kind:'fair',impactPct:50}]},'2026-06-15');
    assert.equal(up.eventPct,50);assert.equal(up.rooms,8); // 5.5 × 1.5 = 8.25 → 8
    assert.equal(M.fcEventPct([{from:'2026-01-01',to:'2026-01-02',impactPct:90},{from:'2026-01-01',to:'2026-01-01',impactPct:40}],'2026-01-01'),100);
    assert.equal(M.fcEventPct([{from:'2026-01-01',to:'2026-01-02',impactPct:-90}],'2026-01-02'),-50);
  });
  test(T+'pickup: future night blends on-the-books + last year\'s pickup with the baseline; never below on the books',()=>{
    const xs=[];for(let r=0;r<6;r++)xs.push(st(String(200+r),'2025-03-10','2025-03-11',r<2?'2025-02-01':'2025-03-08'));
    xs.push(st('300','2026-03-09','2026-03-10','2026-02-15'),st('301','2026-03-09','2026-03-10','2026-02-15'),st('302','2026-03-09','2026-03-10','2026-02-15'));
    const f=M.fcForecastNight({stays:xs,rooms:10,asOf:'2026-03-02'},'2026-03-09'); // last year aligned night = 2025-03-10
    assert.equal(f.onBooks,3);assert.equal(f.pickup,4);assert.ok(f.rooms>=f.onBooks);assert.ok(f.paceFactorBps>10000);
    assert.match(f.explain,/expected pickup 4/);
  });
  test(T+'DoD back-test: FY 2025-26 forecast (7 days ahead) within 2 points of the legacy-shaped actual, which sits at the study level 31 %',()=>{
    const xs=M.measFcLegacySample();const bt=M.fcBacktest(xs,36,'2025-04-01','2026-03-31',7,'2023-04-01');
    assert.equal(bt.nights,365);assert.ok(Math.abs(bt.actualPct-31)<=2,'actual '+bt.actualPct);
    assert.ok(Math.abs(bt.diffPts)<=2,JSON.stringify(bt));
  });
  test(T+'DoD back-test on the FY26 fixture (no prior year: trailing weekday curve + pickup) within 2 points',()=>{
    const s=M.loadScenario('FY26');const xs=M.fcStaysFromState(s);
    const bt=M.fcBacktest(xs,s.rooms.length,'2026-06-01',M.addDays(s.date,-1),7);
    assert.ok(bt.nights>100);assert.ok(Math.abs(bt.diffPts)<=2,JSON.stringify(bt));
  });
  test(T+'displacement: worked example accept / counter with break-even rounded up to ₹50; integer paise only',()=>{
    const nights=[{date:'2026-12-04',sellable:30,forecastRooms:25,adrPaise:200000},{date:'2026-12-05',sellable:30,forecastRooms:28,adrPaise:200000}];
    const a=M.fcDisplacement(nights,10,150000);
    assert.deepEqual(a.rows.map(r=>r.displaced),[5,8]);assert.equal(a.displacedPaise,2600000);assert.equal(a.groupPaise,3000000);assert.equal(a.netPaise,400000);assert.equal(a.decision,'accept');
    const c=M.fcDisplacement(nights,10,100000);assert.equal(c.netPaise,-600000);assert.equal(c.decision,'counter');assert.equal(c.breakEvenRatePaise,130000);
    const d=M.fcDisplacement(nights,10,100000,20000);assert.equal(d.groupPaise,2400000);assert.equal(d.breakEvenRatePaise,110000);
    const odd=M.fcDisplacement([{date:'2026-12-04',sellable:10,forecastRooms:10,adrPaise:123457}],3,0);assert.equal(odd.breakEvenRatePaise,125000);
    assert.throws(()=>M.fcDisplacement(nights,10,1500.5),/whole numbers/);
  });

  // ── yield block ──
  test(T+'yield guards unchanged at defaults: overbook 0, restrictions advisory setting off, simulated yldOcc untouched',()=>{
    const s=seed();assert.equal(M.yldOverbookAllowed(s,'Classic'),0);assert.equal(M.yldRestrictionsEnforced(s),false);
    const o=M.yldOcc(s,'2026-10-24');assert.equal(o.forecast,14);assert.equal(o.simulated,true);
    const t=withCfg(s,{'yld.overbook.Classic':2,'yld.restrictionsEnforce':true});assert.equal(M.yldOverbookAllowed(t,'Classic'),2);assert.equal(M.yldOverbookAllowed(t,'Deluxe'),0);assert.equal(M.yldRestrictionsEnforced(t),true);
    assert.equal(M.yldOverbookAllowed(withCfg(s,{'yld.overbook.Classic':-1}),'Classic'),0);
  });
  test(T+'real forecast occupancy behind meas.rev.forecast; oversold red only above the allowance; restriction counters',()=>{
    const s=seed();const r=M.yldOccReal(s,'2026-10-24');
    if(M.yldRealForecastOn(s)){assert.equal(r.simulated,false);assert.ok(r.real);assert.ok(r.pct>=Math.round(r.onBooks/r.sellable*100));}
    assert.equal(M.yldRealCurve(s,5).length,M.yldRealForecastOn(s)?5:0);
    const ov=M.yldOversold(s,'Classic',s.date);assert.equal(ov.allowed,0);assert.equal(ov.red,ov.over>0);
    let list=M.yldRestrictionCount(undefined,'2026-11-01','CTA','refused');list=M.yldRestrictionCount({restrictions:list},'2026-11-01','CTA','refused');
    list=M.yldRestrictionCount({restrictions:list},'2026-10-31','MIN_STAY','reshaped');
    assert.deepEqual(list,[{night:'2026-10-31',kind:'MIN_STAY',refused:0,reshaped:1},{night:'2026-11-01',kind:'CTA',refused:2,reshaped:0}]);
  });
}
