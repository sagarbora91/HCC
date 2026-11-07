// F12-RPT-PEOPLE · the five people measurement reports on the report engine: staff-productivity, attrition, agent-scorecard, training, staff-pulse.
// Screen = Excel = print (threeWay), roles, switches (MEAS_OFF, payroll module), pulse anonymity and suppression, one test per exception rule at the boundary.
import {build} from 'esbuild';
import {threeWay} from './rpt-golden/harness.mjs';
await build({entryPoints:['tests/rpt-meas-people-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rpt-meas-people.mjs',logLevel:'error'});
const G=await import('./.generated/rpt-meas-people.mjs');
export function register({test,assert}){
  const M=G,T='[F12-RPT-PEOPLE] ';
  const PR='rpt.people.staff-productivity',AT='rpt.people.attrition',SC='rpt.people.agent-scorecard',TR='rpt.people.training',PU='rpt.people.staff-pulse';
  const IDS=[PR,AT,SC,TR,PU];
  const fy=()=>M.loadScenario('FY26');
  const run=(s,uid,id,p={})=>M.runReport(s,uid,id,p,{all:true});
  const code=(fn)=>{try{fn();}catch(e){return e.code;}return 'none';};
  const rows=r=>r.rows.filter(x=>x.kind==='row');
  const staff=s=>M.hrSlice(s).staff;
  const setSwitch=(s,key,on)=>{s.configX={...(s.configX??{}),values:{...(s.configX?.values??{}),[key]:on}};return s;};
  let n=0;
  const addTraining=(s,o)=>{(s.measX??={}).training??=[];s.measX.training.push({id:'T'+(++n),staff:staff(s)[0].id,course:'Fire safety',date:'2026-10-01',hours:4,...o});return s;};
  const pulse=(dept,rec,q='2026-27-Q2')=>({id:'P'+(++n),quarter:q,dept,workload:3,respect:4,tools:3,payFairness:2,recommend:rec});
  const paramsOf=id=>id===TR?{asOf:'2026-10-24'}:id===PU||id===AT?{fy:'2026-27'}:{month:'2026-10'};

  test(T+'the five reports are registered with the catalogue columns, flags, switches and gates',()=>{
    const want={[PR]:[8,false,'PERM-HR-001','D-M','meas.people.productivity'],[AT]:[8,false,'PERM-HR-001','D-FY','meas.people.attrition'],[SC]:[14,true,'PERM-RPT-002','D-M','meas.people.scorecard'],
      [TR]:[9,false,'PERM-HR-001','D-ASOF','meas.people.training'],[PU]:[10,false,'PERM-HR-002','D-FY','meas.people.pulse']};
    for(const [id,[cols,money,perm,dc,key]] of Object.entries(want)){
      const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.columns.length,cols,id+' columns');assert.equal(d.moneyFlag,money,id);assert.deepEqual(d.viewPerms,[perm],id);assert.equal(d.dateClass,dc,id);
      assert.equal(d.area,'people');assert.equal(M.MEAS_REPORT_KEY[id],key,id+' switch');assert.equal(d.columns.at(-1).key,'exception');
    }
    assert.deepEqual(M.reportDef(SC).columns.filter(c=>c.money).map(c=>c.key),['discountAmount','cashVariance']);
    for(const id of [PR,AT,TR,PU])assert.ok(M.reportDef(id).columns.every(c=>!c.money),id+' has no money column (no pay amount anywhere)');
    assert.deepEqual(M.reportDef(PU).formats,['screen','pdf']);
  });

  test(T+'three-way: every report prints the same on screen, Excel and PDF text on the FY26 fixture',()=>{
    const s=fy();
    const month=run(s,'owner',PR,{month:'2026-10'});assert.ok(rows(month).length>=3,'department rows');assert.equal(month.rows.at(-1).kind,'total');threeWay(M,assert,month);
    threeWay(M,assert,run(s,'owner',AT,{fy:'2026-27'}));
    threeWay(M,assert,run(s,'owner',SC,{month:'2026-10'}));
    addTraining(s,{validTo:'2027-01-01',certificateNo:'FS-1'});s.measX.pulse=[0,1,2,3,4].map(i=>pulse(M.hrSlice(s).depts[1].id,9-i));
    threeWay(M,assert,run(s,'owner',TR,{asOf:'2026-10-24'}));threeWay(M,assert,run(s,'owner',PU,{fy:'2026-27'}));
  });

  test(T+'productivity: OT rule at the boundary (above the target % of rostered minutes, not at it)',()=>{
    const f={rosterMin:1000,otMin:100,scheduledDays:100,absentHalves:0,occRoomNights:100,lowRosterMin:0,lowRooms:0};
    assert.equal(M.productivityRule(f,10,5).ot,false,'exactly 10 % is not red');
    assert.equal(M.productivityRule({...f,otMin:101},10,5).ot,true,'just above is red');
  });
  test(T+'productivity: absenteeism rule at the boundary (half-day = 1/2)',()=>{
    const f={rosterMin:1000,otMin:0,scheduledDays:100,absentHalves:10,occRoomNights:100,lowRosterMin:0,lowRooms:0};
    assert.equal(M.productivityRule(f,10,5).absent,false,'5 days of 100 = 5 % is not red');
    assert.equal(M.productivityRule({...f,absentHalves:11},10,5).absent,true,'5.5 days is red');
  });
  test(T+'productivity: rostered hours on low-forecast nights above the need by 20 %',()=>{
    // month ratio = 1000 min / 100 room-nights; low nights sold 10 rooms -> need 100 min; 120 min is exactly +20 % (not red), 121 is red
    const f={rosterMin:1000,otMin:0,scheduledDays:100,absentHalves:0,occRoomNights:100,lowRosterMin:120,lowRooms:10};
    assert.equal(M.productivityRule(f,10,5).overRostered,false);
    assert.equal(M.productivityRule({...f,lowRosterMin:121},10,5).overRostered,true);
    assert.equal(M.productivityRule({...f,lowRosterMin:0},10,5).overRostered,false,'nothing rostered on low nights');
  });
  test(T+'productivity: department filter, HR-001 gate, same figures for Owner and Manager',()=>{
    const s=fy();const all=run(s,'owner',PR,{month:'2026-10'}),fo=run(s,'owner',PR,{month:'2026-10',dept:'Front office'});
    assert.deepEqual(rows(fo).map(x=>x.cells.dept),['Front office']);assert.equal(all.columns.length,8);
    assert.equal(run(s,'manager',PR,{month:'2026-10'}).rows.length,all.rows.length);
    for(const u of ['reception','cashier','housekeeper','maint','supervisor'])assert.equal(code(()=>run(s,u,PR,{month:'2026-10'})),'FORBIDDEN',u);
  });
  test(T+'productivity: hand-built month — late marks, absence, OT and the total row',()=>{
    const s=fy();const st=staff(s).find(x=>x.dept==='FO');
    const x=M.hrSlice(s);for(const k of Object.keys(x.att))if(k.startsWith('2026-10'))delete x.att[k];
    for(let i=0;i<10;i++){const d=`2026-10-${String(i+1).padStart(2,'0')}`;x.att[d+'|'+st.id]={status:i===0?'A':i===1?'L':'P',at:d+'T09:00',otMin:i===2?60:0,by:'manager'};}
    const r=run(s,'owner',PR,{month:'2026-10',dept:st.dept}),fo=rows(r)[0];
    assert.equal(fo.cells.lateMarks,1);assert.ok(fo.cells.absentPct>0);assert.ok(fo.cells.otPct>0);assert.equal(r.rows.at(-1).cells.dept,'All departments');
  });

  test(T+'attrition: 30 % exactly is not red, just above is (average headcount 10)',()=>{
    assert.equal(M.attritionRed(3,10,10,30),false);assert.equal(M.attritionRed(4,10,10,30),true);assert.equal(M.attritionRed(0,0,0,30),false);
  });
  test(T+'attrition: exit reasons, tenure and the capture-needed note',()=>{
    let s=fy();const front=staff(s).filter(x=>x.dept==='FO');
    const none=run(s,'owner',AT,{fy:'2026-27'});assert.match(none.about.dateBasis,/Capture needed: exit reason/);
    s=M.execute(s,'owner',{type:'MEAS_EXIT_SET',key:'x1',staff:front[0].id,reason:'Resigned',exitDate:'2026-09-30'});
    const r=run(s,'owner',AT,{fy:'2026-27'}),fo=rows(r).find(x=>x.cells.dept==='Front office');
    assert.equal(fo.cells.leavers,1);assert.match(fo.cells.exitReasons,/Resigned x1/);assert.match(fo.cells.tenure,/months/);assert.doesNotMatch(r.about.dateBasis,/Capture needed/);
    assert.equal(run(s,'manager',AT,{fy:'2026-27',dept:'FO'}).rows.length,1);
  });

  test(T+'training: nothing captured -> zero rows and the Capture needed note, never an error',()=>{
    const r=run(fy(),'owner',TR,{asOf:'2026-10-24'});assert.equal(r.rows.length,0);assert.match(r.about.dateBasis,/Capture needed: hr\.training/);
  });
  test(T+'training: a mandatory certificate expired is red; valid to today is not',()=>{
    let s=addTraining(fy(),{validTo:'2026-10-23'});
    assert.equal(rows(run(s,'owner',TR,{asOf:'2026-10-24'})).find(x=>x.cells.course==='Fire safety').cells.exception,1,'valid to yesterday = expired');
    s=addTraining(fy(),{validTo:'2026-10-24'});
    const x=rows(run(s,'owner',TR,{asOf:'2026-10-24'})).find(y=>y.cells.course==='Fire safety');
    assert.equal(x.cells.exception,0,'valid to today is still valid');assert.equal(x.cells.daysToExpiry,0);
  });
  test(T+'training: renewed by a later valid record is not red; missing mandatory courses are listed red',()=>{
    let s=addTraining(fy(),{validTo:'2026-05-01',date:'2025-05-02'});addTraining(s,{validTo:'2027-09-01',date:'2026-09-01'});
    const r=run(s,'owner',TR,{asOf:'2026-10-24'});
    const mine=rows(r).filter(x=>x.cells.course==='Fire safety');assert.equal(mine.length,2);assert.ok(mine.every(x=>x.cells.exception===0));
    const missing=rows(r).filter(x=>/\(missing\)/.test(x.cells.course));assert.ok(missing.length>=3&&missing.every(x=>x.cells.exception===1));
    assert.ok(missing.some(x=>x.cells.course.startsWith('FSSAI')));assert.equal(M.MANDATORY_COURSES.length,4);
  });
  test(T+'training: quarter hours per head and department filter',()=>{
    const s=fy();const p=staff(s)[0];addTraining(s,{date:'2026-10-02',hours:3,staff:p.id,course:'Guest service'});addTraining(s,{date:'2026-10-03',hours:2.5,staff:p.id,course:'Guest service 2'});
    const r=run(s,'owner',TR,{asOf:'2026-10-24',dept:p.dept});assert.equal(rows(r).find(y=>y.cells.course==='Guest service').cells.quarterHours,'5.5');
    assert.ok(!rows(run(s,'owner',TR,{asOf:'2026-10-24',dept:'Kitchen'})).some(y=>y.cells.staff===p.name&&p.dept!=='KIT'));
  });

  test(T+'pulse: a department with fewer than 5 answers is suppressed, 5 are shown',()=>{
    const s=fy(),d=M.hrSlice(s).depts[1].id;s.measX={pulse:[9,9,9,9].map(v=>pulse(d,v))};
    let r=run(s,'owner',PU,{fy:'2026-27'});assert.equal(r.rows.length,0);assert.match(r.about.dateBasis,/1 department-quarter suppressed/);
    s.measX.pulse.push(pulse(d,9));r=run(s,'owner',PU,{fy:'2026-27'});assert.equal(r.rows.length,1);assert.equal(r.rows[0].cells.responses,5);assert.equal(r.rows[0].cells.dept,M.hrSlice(s).depts[1].name);
  });
  test(T+'pulse: eNPS below 0 is red, 0 is not',()=>{
    assert.equal(M.enps([9,9,6,6]),0);assert.equal(M.enps([9,6,6,7,8]),-20);assert.equal(M.enps([10,9,9,7,0]),40);assert.equal(M.enps([]),0);
    const s=fy(),d=M.hrSlice(s).depts[1].id;
    s.measX={pulse:[9,9,6,6,8].map(v=>pulse(d,v))};
    assert.equal(run(s,'owner',PU,{fy:'2026-27'}).rows[0].cells.exception,0);assert.equal(run(s,'owner',PU,{fy:'2026-27'}).rows[0].cells.enps,0);
    s.measX={pulse:[9,6,6,8,8].map(v=>pulse(d,v))};
    assert.equal(run(s,'owner',PU,{fy:'2026-27'}).rows[0].cells.exception,1);
  });
  test(T+'pulse: Owner and Manager only, quarter filter, no staff identity anywhere in the result',()=>{
    const s=fy(),d=M.hrSlice(s).depts[1].id;s.measX={pulse:[...[9,9,6,6,8].map(v=>pulse(d,v)),...[9,9,9,8,8].map(v=>pulse(d,v,'2026-27-Q1'))]};
    assert.equal(run(s,'manager',PU,{fy:'2026-27'}).rows.length,2);assert.equal(run(s,'owner',PU,{fy:'2026-27',quarter:'Q1'}).rows.length,1);
    for(const u of ['reception','accountant','cashier','supervisor','housekeeper','maint'])assert.equal(code(()=>run(s,u,PU,{fy:'2026-27'})),'FORBIDDEN',u);
    const text=JSON.stringify(run(s,'owner',PU,{fy:'2026-27'}));
    for(const st of staff(s)){assert.ok(!text.includes(st.id),'staff id '+st.id);assert.ok(!text.includes(st.name),'staff name');}
    assert.deepEqual(M.PULSE_COLS.map(c=>c.key),['quarter','dept','responses','workload','respect','tools','payFairness','recommend','enps','exception']);
    assert.equal(M.PULSE_MIN_ANSWERS,5);
  });

  test(T+'scorecard: quartile rule needs a team of 4, a value above zero and above Q3',()=>{
    assert.equal(M.aboveUpperQuartile([10,20,30],1000),false,'team of 3');
    assert.equal(M.nearestRank([10,20,30,40],0.75),30);
    assert.equal(M.aboveUpperQuartile([10,20,30,40],40),true,'above Q3 (30)');
    assert.equal(M.aboveUpperQuartile([10,20,30,40],30),false,'at Q3 is not red');
    assert.equal(M.aboveUpperQuartile([0,0,0,0],0),false,'zero is never red');
    const z={checkins:0,checkouts:0,rateEdits:0,discountCount:0,discountAmount:0,withReason:0,comps:0,upsells:0,complaints:0,cashVariance:0,regrets:0,surveyTotal:0,surveyCount:0};
    const team=[10,20,30,40].map(v=>({...z,discountAmount:v}));assert.equal(M.scorecardRed(team,team[3]),true);assert.equal(M.scorecardRed(team,team[2]),false);
    const cv=[1,2,3,4].map(v=>({...z,cashVariance:v*100}));assert.equal(M.scorecardRed(cv,cv[3]),true);
  });
  test(T+'scorecard: Owner and Manager only; counts check-ins, regrets and desk surveys by user and month',()=>{
    const s=fy();
    for(const u of ['accountant','reception','cashier','supervisor','housekeeper','maint','techadmin'])assert.equal(code(()=>run(s,u,SC,{month:'2026-10'})),'FORBIDDEN',u);
    s.audit.unshift({id:'z1',at:'2026-10-05T10:00',actor:M.hrSlice(s).staff.length?s.users.find(u=>u.id==='reception').name:'reception',action:'CHECKIN',detail:'x'},
      {id:'z2',at:'2026-10-05T11:00',actor:'reception',action:'CHECKIN',detail:'x'},{id:'z3',at:'2026-09-30T11:00',actor:'reception',action:'CHECKIN',detail:'x'});
    s.measX={turnaway:[{id:'t1',at:'2026-10-06T10:00',by:'reception',reason:'full',arrival:'2026-10-10',nights:1,roomType:'x',rooms:1,channel:'walk-in',leadDays:4}],
      surveys:[{id:'v1',at:'2026-10-07T10:00',overall:5,cleanliness:5,staff:5,value:5,recommend:9,consent:true,via:'desk',enteredBy:'reception'},
        {id:'v2',at:'2026-10-07T11:00',overall:3,cleanliness:3,staff:3,value:3,recommend:5,consent:true,via:'desk',enteredBy:'reception'},
        {id:'v3',at:'2026-10-07T12:00',overall:1,cleanliness:1,staff:1,value:1,recommend:1,consent:true,via:'qr'}]};
    const r=run(s,'manager',SC,{month:'2026-10'}),x=rows(r).find(y=>y.cells.regrets===1);
    assert.ok(x,'a reception row');assert.equal(x.cells.checkins,2,'September check-in excluded; name and id both resolve');assert.equal(x.cells.surveyScore,'4.0');assert.equal(typeof x.cells.discountAmount,'number');
    const only=rows(run(s,'owner',SC,{month:'2026-10',user:'reception'}));assert.equal(only.length,1,'user filter');assert.equal(only[0].cells.checkins,2);
  });

  test(T+'switches: an item off refuses with MEAS_OFF and leaves the catalogue; payroll off hides the whole pack; data is kept',()=>{
    const KEY={[PR]:'meas.people.productivity',[AT]:'meas.people.attrition',[SC]:'meas.people.scorecard',[TR]:'meas.people.training',[PU]:'meas.people.pulse'};
    for(const id of IDS){
      const s=setSwitch(fy(),KEY[id],false);assert.equal(code(()=>run(s,'owner',id,paramsOf(id))),'MEAS_OFF',id);
      assert.ok(!M.reportCatalogue(s,'owner').some(d=>d.id===id),id+' hidden');
      const p=fy();p.configX={...(p.configX??{}),modulesOff:['payroll']};
      assert.equal(code(()=>run(p,'owner',id,paramsOf(id))),'MEAS_OFF',id+' payroll off');
      assert.ok(!M.reportCatalogue(p,'owner').some(d=>d.id===id));
      assert.ok(M.reportCatalogue(fy(),'owner').some(d=>d.id===id),id+' visible when on');
    }
    const s=fy();s.measX={pulse:[1,2,3,4,5].map(v=>pulse(M.hrSlice(s).depts[1].id,v))};const off=setSwitch(structuredClone(s),KEY[PU],false);
    assert.equal(off.measX.pulse.length,5,'data kept');
    assert.equal(run(setSwitch(off,KEY[PU],true),'owner',PU,{fy:'2026-27'}).rows.length,1,'back on shows the data again');
    const pack=fy();pack.configX={values:{'meas.pack.people':false}};assert.equal(code(()=>run(pack,'owner',PR,{month:'2026-10'})),'MEAS_OFF');
  });
}
