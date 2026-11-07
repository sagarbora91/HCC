// F12 MEAS · meas-rpt-hk tests (MEAS-CONTRACT §10): the six housekeeping / engineering / asset measurement reports. Names prefixed [F12-RPT-HK].
// One boundary test per exception rule (just red / just not red), the capture-needed empty state, MEAS_OFF, DEC-061 money removal, and
// screen = Excel = PDF text (golden harness threeWay) on populated data.
import {threeWay} from './rpt-golden/harness.mjs';
export function register({test,M,assert}){
  const {seed,runReport,reportCatalogue,allReports,reportDef}=M;
  const T='[F12-RPT-HK] ';
  const IDS=['rpt.rooms.hk-productivity','rpt.rooms.ooo-days','rpt.rooms.ppm-compliance','rpt.rooms.utilities-per-room','rpt.stock.linen-cost','rpt.acc.asset-life'];
  const run=(s,uid,id,params={})=>runReport(s,uid,id,params,{all:true});
  const red=r=>r.rows.filter(x=>x.group==='red');
  const day=(d,n)=>new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);
  const withCfg=(s,values)=>({...s,configX:{...(s.configX??{}),values:{...(s.configX?.values??{}),...values}}});
  const throwsCode=(fn,re)=>assert.throws(fn,e=>re.test(String(e&&e.message)));

  // ── registration ──
  test(T+'six reports registered with the catalogue ids, areas, switches and money flags',()=>{
    const want={'rpt.rooms.hk-productivity':['rooms','meas.hk.productivity',false],'rpt.rooms.ooo-days':['rooms','meas.hk.ooo',true],'rpt.rooms.ppm-compliance':['rooms','meas.hk.ppm',false],
      'rpt.rooms.utilities-per-room':['rooms','meas.hk.utilities',true],'rpt.stock.linen-cost':['stock','meas.hk.linen',true],'rpt.acc.asset-life':['acc','meas.hk.assets',true]};
    for(const id of IDS){const d=reportDef(id);assert.ok(d,id);assert.equal(d.area,want[id][0]);assert.equal(d.status,'MEAS');assert.equal(M.MEAS_REPORT_KEY[id],want[id][1],id+' switch key');
      assert.equal(d.moneyFlag,want[id][2],id+' money flag');assert.equal(d.columns.some(c=>c.money),d.moneyFlag,id+' money columns');assert.ok(allReports().includes(d));}
  });
  test(T+'columns are in catalogue order and only the documented columns are money',()=>{
    const cols=id=>reportDef(id).columns.map(c=>c.label);
    assert.deepEqual(cols('rpt.rooms.hk-productivity'),['Attendant','Shift','Departure rooms','Stayover rooms','Minutes per room','Vacated-to-inspected minutes','Inspection pass %','Rejections by reason','Same-day re-let compliance']);
    assert.deepEqual(cols('rpt.rooms.ooo-days'),['Room','Reason','Room-days','Work order','Days without ticket','Lost revenue at ADR','Idle 14+ nights while sellable']);
    assert.deepEqual(cols('rpt.rooms.ppm-compliance'),['Asset kind','Plans due','Done on time','Overdue','Preventive-to-corrective ratio','MTTR by priority','Repeat failures in 90 days','AMC visits due']);
    assert.deepEqual(cols('rpt.rooms.utilities-per-room'),['Day','Electricity kWh','Water kL','LPG kg','Diesel litres','DG hours','Per occupied room-night','Per available room','Unit cost','Cost per occupied night','Last year same month']);
    assert.deepEqual(cols('rpt.stock.linen-cost'),['Item','Par','On hand','Laundry batches','Lost','Damaged','Discarded (reason)','Amenities issued','Cost per occupied room-night']);
    assert.deepEqual(cols('rpt.acc.asset-life'),['Asset','Age','Expected life','Condition grade','Replacement due','WDV','Replacement cost','FF&E reserve accrued','Capex spent','Refurbishment due (category)']);
    assert.deepEqual(reportDef('rpt.acc.asset-life').columns.filter(c=>c.money).map(c=>c.key),['wdv','replacementCost','reserveAccrued','capexSpent']);
    assert.deepEqual(reportDef('rpt.rooms.ooo-days').columns.filter(c=>c.money).map(c=>c.key),['lostRevenue']);
    assert.deepEqual(reportDef('rpt.rooms.utilities-per-room').columns.filter(c=>c.money).map(c=>c.key),['unitCost','costPerNight']);
    assert.deepEqual(reportDef('rpt.stock.linen-cost').columns.filter(c=>c.money).map(c=>c.key),['costPerNight']);
  });

  // ── empty data: the capture-needed note, zero rows, no error ──
  test(T+'with nothing captured the capture-driven reports show zero rows and the "capture needed" note',()=>{
    const s=seed();
    for(const id of ['rpt.rooms.hk-productivity','rpt.rooms.ooo-days','rpt.rooms.utilities-per-room','rpt.acc.asset-life']){
      const r=run(s,'owner',id);assert.equal(r.rows.length,0,id);assert.match(r.about.dateBasis,/capture needed/i,id);}
    assert.match(M.printText(M.toPrintModel(run(s,'owner','rpt.rooms.ooo-days'),'Test property')),/capture needed/i);
  });

  // ── MEAS_OFF / switch ──
  test(T+'a switched-off item hides its report and refuses the run with MEAS_OFF; the data is kept',()=>{
    const off=withCfg(seed(),{'meas.pack.hk':false});
    for(const id of IDS){assert.ok(!reportCatalogue(off,'owner').some(x=>x.id===id),id+' hidden');throwsCode(()=>run(off,'owner',id),/MEAS_OFF/);}
    const one=withCfg(seed(),{'meas.hk.ooo':false});
    assert.ok(!reportCatalogue(one,'owner').some(x=>x.id==='rpt.rooms.ooo-days'));assert.ok(reportCatalogue(one,'owner').some(x=>x.id==='rpt.rooms.hk-productivity'));
    assert.ok(run(withCfg(one,{'meas.hk.ooo':true}),'owner','rpt.rooms.ooo-days'));
  });

  // ── hk-productivity ──
  const hkState=(tasks,times,bookings=[])=>{const s=seed();s.tasks=tasks;s.bookings=[...s.bookings,...bookings];s.measX={...(s.measX??{}),taskTimes:times};return s;};
  const task=(id,kind,extra={})=>({id,room:extra.room??'105',assignee:extra.assignee??'housekeeper',status:'Done',checked:[true,true,true],kind,date:'2026-10-24',createdAt:'2026-10-24T07:00',...extra});
  const tm=(mins,result,extra={})=>({startedAt:'2026-10-24T09:00',finishedAt:'2026-10-24T'+String(9+Math.floor(mins/60)).padStart(2,'0')+':'+String(mins%60).padStart(2,'0'),inspectedAt:'2026-10-24T11:50',inspectResult:result,attendant:'housekeeper',...extra});
  const hk=(s,p={},uid='owner')=>run(s,uid,'rpt.rooms.hk-productivity',{from:'2026-10-24',to:'2026-10-24',...p});
  test(T+'hk-productivity: pass rate 95.0 % is not red, 90 % is red (boundary)',()=>{
    const mk=fails=>{const tasks=[],times={};for(let i=0;i<20;i++){tasks.push(task('T'+i,'stayover'));times['T'+i]=tm(20,i<fails?'fail':'pass',i<fails?{rejectReason:'Dust'}:{});}return hkState(tasks,times);};
    const ok=hk(mk(1));assert.equal(ok.rows.length,1);assert.equal(ok.rows[0].cells.passPct,95);assert.equal(red(ok).length,0);
    const bad=hk(mk(2));assert.equal(bad.rows[0].cells.passPct,90);assert.equal(red(bad).length,1);assert.equal(bad.rows[0].cells.rejections,'Dust ×2');
  });
  test(T+'hk-productivity: median departure room 45 min is not red, 46 min is red',()=>{
    const mk=m=>hkState([task('D1','checkout')],{D1:tm(m,'pass')});
    const a=hk(mk(45));assert.equal(a.rows[0].cells.minutesPerRoom,45);assert.equal(red(a).length,0);
    const b=hk(mk(46));assert.equal(b.rows[0].cells.departures,1);assert.equal(red(b).length,1);
  });
  test(T+'hk-productivity: same-day re-let inspected before arrival is compliant, after arrival is red',()=>{
    const s0=seed();const arr={...s0.bookings[0],id:'B-RELET',room:'105',arrival:'2026-10-24',departure:'2026-10-26',status:'Confirmed',checkedInAt:undefined};
    const mk=at=>hkState([task('D1','checkout')],{D1:tm(30,'pass',{inspectedAt:at})},[arr]);
    const ci=s0.settings.checkin;
    const ok=hk(mk('2026-10-24T'+ci));assert.equal(ok.rows[0].cells.relet,'1 of 1');assert.equal(red(ok).length,0);
    const late=hk(mk('2026-10-24T'+String(Number(ci.slice(0,2))+1).padStart(2,'0')+':00'));assert.equal(late.rows[0].cells.relet,'0 of 1');assert.equal(red(late).length,1);
    const none=hkState([task('D1','checkout')],{D1:tm(30,'pass')});assert.equal(hk(none).rows[0].cells.relet,'-');
  });
  test(T+'hk-productivity: attendant and shift filters, 31-day cap, shift by start hour',()=>{
    const s=hkState([task('A','stayover'),task('B','stayover',{assignee:'housekeeper2'}),task('C','stayover')],{A:tm(20,'pass'),B:tm(20,'pass',{attendant:'housekeeper2'}),C:tm(20,'pass',{startedAt:'2026-10-24T15:00',finishedAt:'2026-10-24T15:20'})});
    assert.equal(hk(s).rows.length,3);
    assert.deepEqual(hk(s,{shift:'Afternoon'}).rows.map(r=>r.cells.shift),['Afternoon']);
    assert.equal(hk(s,{attendant:'housekeeper2'}).rows.length,1);
    throwsCode(()=>hk(s,{from:'2026-09-01',to:'2026-10-24'}),/longer than 31 days/);
  });
  test(T+'hk-productivity: screen = Excel = PDF text on populated data',()=>{
    const s=hkState([task('A','stayover'),task('D1','checkout',{assignee:'housekeeper2'})],{A:tm(25,'pass'),D1:tm(50,'fail',{rejectReason:'Towels',attendant:'housekeeper2'})});
    assert.equal(threeWay(M,assert,hk(s)).length,1);
  });

  // ── ooo-days ──
  const oooState=(from,extra={})=>{const s=seed();const r=s.rooms.find(x=>x.id==='105');r.ooo={from,reason:'AC'};s.maintenanceX={...(s.maintenanceX??{}),orders:(s.maintenanceX?.orders??[]).filter(o=>o.room!=='105')};
    s.measX={...(s.measX??{}),offline:{'105':{reason:'OOO',...extra}}};return s;};
  const ooo=(s,p={},uid='owner')=>run(s,uid,'rpt.rooms.ooo-days',{from:'2026-10-01',to:'2026-10-24',...p});
  const row105=r=>r.rows.find(x=>x.cells.room==='105');
  test(T+'ooo-days: 8 days out of order without a work order is red, 7 days is not (boundary)',()=>{
    const bad=row105(ooo(oooState('2026-10-17')));assert.equal(bad.cells.roomDays,8);assert.equal(bad.cells.daysNoTicket,8);assert.equal(bad.group,'red');
    const ok=row105(ooo(oooState('2026-10-18')));assert.equal(ok.cells.roomDays,7);assert.equal(ok.group,undefined);
  });
  test(T+'ooo-days: a linked work order clears the no-ticket day count and the red flag; reason label and filter',()=>{
    const s=oooState('2026-10-10',{workOrder:'WO-9'});const r=row105(ooo(s));assert.equal(r.cells.workOrder,'WO-9');assert.equal(r.cells.daysNoTicket,0);assert.equal(r.group,undefined);assert.equal(r.cells.reason,'Out of order');
    assert.equal(ooo(s,{reason:'renovation'}).rows.length,0);assert.equal(ooo(s,{reason:'OOO'}).rows.filter(x=>x.cells.room==='105').length,1);
  });
  test(T+'ooo-days: lost revenue is room-days x the ADR target, kept as paise',()=>{
    const s=oooState('2026-10-17');const r=row105(ooo(s));assert.equal(r.cells.lostRevenue,8*205000);
    const s2=withCfg(s,{'meas.target.adr':300000});assert.equal(row105(ooo(s2)).cells.lostRevenue,8*300000);
  });
  test(T+'ooo-days: a sellable room idle 14 nights is red, 13 nights is not (boundary)',()=>{
    const s=seed();s.rooms=s.rooms.map(x=>({...x,ooo:undefined,blocked:false}));s.bookings=[];s.measX={...(s.measX??{}),offline:{'101':{reason:'OOS'}}};
    const a=run(s,'owner','rpt.rooms.ooo-days',{from:'2026-10-11',to:'2026-10-24'});const r=a.rows.find(x=>x.cells.room==='105');assert.equal(r.cells.idleNights,14);assert.equal(r.group,'red');
    const b=run(s,'owner','rpt.rooms.ooo-days',{from:'2026-10-12',to:'2026-10-24'});assert.equal(b.rows.find(x=>x.cells.room==='105'),undefined);
    s.bookings=[{...seed().bookings[0],room:'105',arrival:'2026-10-20',departure:'2026-10-22',status:'Departed'}];
    assert.equal(run(s,'owner','rpt.rooms.ooo-days',{from:'2026-10-11',to:'2026-10-24'}).rows.find(x=>x.cells.room==='105'),undefined); // the idle run after the stay is 3 nights
  });
  test(T+'ooo-days: DEC-061 the lost-revenue column is removed (not blanked) for the Supervisor; the Owner sees it',()=>{
    const s=oooState('2026-10-17');
    const sup=ooo(s,{},'supervisor');assert.deepEqual(sup.removed,['lostRevenue']);assert.ok(!sup.columns.some(c=>c.key==='lostRevenue'));assert.ok(!('lostRevenue' in sup.rows[0].cells));
    assert.ok(ooo(s,{},'owner').columns.some(c=>c.key==='lostRevenue'));
    for(const u of ['housekeeper','maint'])assert.equal(M.holdsMoneyGate(s,u,reportDef('rpt.rooms.ooo-days')),false,u);
  });
  test(T+'ooo-days: 92-day cap and screen = Excel = PDF text',()=>{
    const s=oooState('2026-10-17');throwsCode(()=>ooo(s,{from:'2026-07-01'}),/longer than 92 days/);
    assert.ok(threeWay(M,assert,ooo(s))[0].rows.length>=1);
  });

  // ── ppm-compliance ──
  const wo=(id,kind,extra)=>({id,kind,title:'T',priority:'P3',status:'Open',assetId:'A1',createdAt:'2026-10-01T08:00',createdBy:'maint',parts:[],history:[],...extra});
  const mntState=(orders,assets)=>{const s=seed();s.maintenanceX={assets:assets??[{id:'A1',name:'Pump',kind:'Pump',area:'Basement',make:'',model:'',installedOn:'2020-01-01',status:'Working',vendorId:'V1'}],vendors:[{id:'V1',name:'Vendor',trade:'x',phone:''}],amcs:[],spares:[],plans:[],orders};return s;};
  const ppm=(s,p={},uid='owner')=>run(s,uid,'rpt.rooms.ppm-compliance',{from:'2026-04-01',to:'2026-10-24',...p});
  test(T+'ppm: a preventive task overdue 7 days is not red, 8 days is red (boundary)',()=>{
    const a=ppm(mntState([wo('P1','Preventive',{dueDate:'2026-10-17'})]));assert.equal(a.rows[0].cells.plansDue,1);assert.equal(a.rows[0].cells.overdue,1);assert.equal(red(a).length,0);
    const b=ppm(mntState([wo('P1','Preventive',{dueDate:'2026-10-16'})]));assert.equal(red(b).length,1);
    const done=ppm(mntState([wo('P1','Preventive',{dueDate:'2026-10-16',status:'Done',doneAt:'2026-10-16T10:00'})]));assert.equal(done.rows[0].cells.doneOnTime,1);assert.equal(done.rows[0].cells.overdue,0);assert.equal(red(done).length,0);
  });
  test(T+'ppm: P1 MTTR exactly 24 h is not red, 24 h 1 min is red (boundary)',()=>{
    const mk=done=>mntState([wo('C1','Corrective',{priority:'P1',status:'Done',createdAt:'2026-10-20T08:00',doneAt:done})]);
    const a=ppm(mk('2026-10-21T08:00'));assert.equal(a.rows[0].cells.mttr,'P1 24 h');assert.equal(red(a).length,0);
    const b=ppm(mk('2026-10-21T08:01'));assert.equal(red(b).length,1);
    const p2=ppm(mntState([wo('C1','Corrective',{priority:'P2',status:'Done',createdAt:'2026-10-20T08:00',doneAt:'2026-10-25T08:00'})]));assert.equal(red(p2).length,0); // only P1 drives the rule
  });
  test(T+'ppm: a third failure of one asset inside 90 days is red, on day 91 it is not (boundary)',()=>{
    const mk=third=>mntState([wo('C1','Corrective',{createdAt:'2026-07-01T08:00'}),wo('C2','Corrective',{createdAt:'2026-08-01T08:00'}),wo('C3','Corrective',{createdAt:third+'T08:00'})]);
    const a=ppm(mk('2026-09-29'));assert.equal(a.rows[0].cells.repeatFailures,1);assert.equal(red(a).length,1);
    const b=ppm(mk('2026-09-30'));assert.equal(b.rows[0].cells.repeatFailures,0);assert.equal(red(b).length,0);
  });
  test(T+'ppm: ratio text, kind filter, AMC visits for the vendor, empty kinds omitted',()=>{
    const s=mntState([wo('P1','Preventive',{dueDate:'2026-10-01',createdAt:'2026-09-28T08:00'}),wo('C1','Corrective',{createdAt:'2026-10-02T08:00'})]);s.maintenanceX.amcs=[{id:'AMC1',vendorId:'V1',scope:'Pumps',from:'2026-04-01',to:'2027-03-31',visits:4,value:100000}];
    const r=ppm(s);assert.equal(r.rows.length,1);assert.equal(r.rows[0].cells.ratio,'1 : 1');assert.equal(r.rows[0].cells.amcVisits,4);assert.equal(r.rows[0].cells.kind,'Pump');
    assert.equal(ppm(s,{assetKind:'AC'}).rows.length,0);
    assert.doesNotThrow(()=>run(seed(),'owner','rpt.rooms.ppm-compliance',{from:'2026-04-01',to:'2026-10-24'}));
  });
  test(T+'ppm: screen = Excel = PDF text',()=>{
    assert.equal(threeWay(M,assert,ppm(mntState([wo('P1','Preventive',{dueDate:'2026-10-01'}),wo('C1','Corrective',{priority:'P1',status:'Done',doneAt:'2026-10-02T09:00'})]))).length,1);
  });

  // ── utilities-per-room ──
  const utilState=(sepUsed)=>{
    const s=seed();s.bookings=[];
    const hist={};for(let d='2026-05-01';d<='2026-09-30';d=day(d,1))hist[d]=10;s.inventoryX={...s.inventoryX,occHistory:hist};
    const rd=(date,value)=>({id:'R'+date,meter:'MTR-ELEC',date,value,by:'maint'});
    s.measX={...(s.measX??{}),readings:[rd('2026-06-01',0),rd('2026-06-30',300),rd('2026-07-31',610),rd('2026-08-31',920),rd('2026-09-15',920+sepUsed)]};
    return s;
  };
  const util=(s,p={},uid='owner')=>run(s,uid,'rpt.rooms.utilities-per-room',{month:'2026-09',...p});
  test(T+'utilities: 15 % above the 3-month average per occupied night is not red, above it is red (boundary)',()=>{
    const a=util(utilState(11.5));const d=a.rows.find(x=>x.cells.day==='2026-09-15');assert.equal(d.cells.perOccupied,'1.15');assert.equal(d.group,undefined);
    const b=util(utilState(11.6));assert.equal(b.rows.find(x=>x.cells.day==='2026-09-15').group,'red');
  });
  test(T+'utilities: use is the difference of consecutive monotonic readings; LPG cylinders convert to kg; month row sums and last year',()=>{
    const s=utilState(10);s.measX.readings.push({id:'L1',meter:'MTR-LPG',date:'2026-09-10',value:5,by:'maint'},{id:'L2',meter:'MTR-LPG',date:'2026-09-15',value:7,by:'maint'});
    s.measX.readings.push({id:'Y1',meter:'MTR-ELEC',date:'2025-09-01',value:0,by:'maint'},{id:'Y2',meter:'MTR-ELEC',date:'2025-09-30',value:200,by:'maint'});
    const r=util(s);const d=r.rows.find(x=>x.cells.day==='2026-09-15');assert.equal(d.cells.electricity,'10.00');assert.equal(d.cells.lpg,'38.00');
    const tot=r.rows.at(-1);assert.equal(tot.kind,'total');assert.equal(tot.cells.electricity,'10.00');assert.equal(tot.cells.lastYear,'200.00');
    assert.equal(util(s,{meter:'MTR-WATER'}).rows.filter(x=>x.kind==='row').length,0);
  });
  test(T+'utilities: unit cost and cost per occupied night come from Posted Utilities expenses (paise) on the month row only',()=>{
    const s=utilState(10);s.expenses=[{id:'E1',vendor:'Power',category:'Utilities',amount:5000,method:'Bank',date:'2026-09-20',status:'Posted',actor:'accountant'}];
    const tot=util(s).rows.at(-1);assert.equal(tot.cells.unitCost,500);assert.equal(tot.cells.costPerNight,17); // 5000 / 10 kWh; 5000 / 300 room-nights
    assert.ok(util(s,{},'supervisor').columns.every(c=>!c.money));
  });
  test(T+'utilities: screen = Excel = PDF text',()=>{assert.ok(threeWay(M,assert,util(utilState(11.6))).length===1);});

  // ── linen-cost ──
  const linenItem=s=>s.inventoryX.items.find(i=>i.kind==='linen'&&i.par);
  const lnState=(lossQty,discardQty=0)=>{const s=seed();const it=linenItem(s);
    s.inventoryX={...s.inventoryX,losses:lossQty?[{id:'L1',date:'2026-10-10',item:it.id,loc:'rooms',qty:lossQty,reason:'Missing',actor:'supervisor',value:0}]:[],moves:[],batches:[]};
    s.measX={...(s.measX??{}),discards:discardQty?[{id:'D1',item:it.name,qty:discardQty,reason:'Torn',date:'2026-10-11',by:'supervisor'}]:[]};return [s,it];};
  const linen=(s,p={},uid='owner')=>run(s,uid,'rpt.stock.linen-cost',{month:'2026-10',...p});
  test(T+'linen: loss of exactly 2 % of par is not red, one piece more is red (boundary)',()=>{
    const [s0,it]=lnState(0);const par=linen(s0).rows.find(x=>x.cells.item===it.name).cells.par;assert.ok(par>0);
    const L=Math.floor(2*par/100);assert.ok(L>=1,'par large enough for the fixture');
    const [a]=lnState(L);const ra=linen(a).rows.find(x=>x.cells.item===it.name);assert.equal(ra.cells.lost,L);assert.equal(ra.group,undefined);
    const [b]=lnState(L+1);assert.equal(linen(b).rows.find(x=>x.cells.item===it.name).group,'red');
  });
  test(T+'linen: captured discards count towards the loss and show their reason',()=>{
    const [s0,it]=lnState(0);const par=linen(s0).rows.find(x=>x.cells.item===it.name).cells.par;const L=Math.floor(2*par/100);
    const [s]=lnState(L,1);const r=linen(s).rows.find(x=>x.cells.item===it.name);assert.equal(r.cells.discarded,'1 (Torn 1)');assert.equal(r.group,'red');
    assert.match(linen(s0).about.dateBasis,/capture needed/i); // no discard captured yet: the note is added, the stock rows still show
  });
  test(T+'linen: cost per occupied night above a set target is red; without a target the rule is not applied',()=>{
    const [s,it]=lnState(1);const per=linen(s).rows.find(x=>x.cells.item===it.name).cells.costPerNight;
    if(per===null)return; // no occupied room-nights in the fixture month: nothing to compare
    const t=withCfg(s,{'meas.target.linenCost':per});assert.equal(linen(t).rows.find(x=>x.cells.item===it.name).group,undefined);
    const t2=withCfg(s,{'meas.target.linenCost':per-1});assert.equal(linen(t2).rows.find(x=>x.cells.item===it.name).group,'red');
  });
  test(T+'linen: DEC-061 the cost column is removed for the Supervisor / Housekeeper; group filter; screen = Excel = PDF',()=>{
    const [s]=lnState(1,1);
    for(const u of ['supervisor','housekeeper']){const r=linen(s,{},u);assert.ok(!r.columns.some(c=>c.key==='costPerNight'),u);assert.deepEqual(r.removed,['costPerNight']);}
    assert.ok(linen(s,{},'owner').columns.some(c=>c.key==='costPerNight'));
    const items=s.inventoryX.items;assert.equal(linen(s,{group:'amenity'}).rows.length,items.filter(i=>i.kind==='amenity').length);
    assert.ok(threeWay(M,assert,linen(s))[0].rows.length>0);
  });

  // ── asset-life ──
  const assetState=(life,extra)=>{const s=seed();s.maintenanceX={assets:[{id:'A1',name:'Boiler',kind:'Other',area:'Roof',make:'',model:'',installedOn:'2020-01-01',status:'Working'}],vendors:[],amcs:[],spares:[],plans:extra?.plans??[],orders:[]};
    s.measX={...(s.measX??{}),assetLife:{A1:life}};s.bookings=[];return s;};
  const al=(s,p={},uid='owner')=>run(s,uid,'rpt.acc.asset-life',{asOf:'2026-10-24',...p});
  test(T+'asset-life: on the replacement date the asset is not past life, the next day it is (red when no plan)',()=>{
    const life={expectedLifeMonths:60,conditionGrade:'C',replacementCostPaise:5000000};
    const a=run(assetState(life),'owner','rpt.acc.asset-life',{asOf:'2025-01-01'});const ra=a.rows.find(x=>x.cells.asset==='Boiler');assert.equal(ra.cells.replacementDue,'2025-01-01');assert.equal(ra.group,undefined);
    const b=run(assetState(life),'owner','rpt.acc.asset-life',{asOf:'2025-01-02'});assert.equal(b.rows.find(x=>x.cells.asset==='Boiler').group,'red');
    const planned=assetState(life,{plans:[{id:'PL1',assetId:'A1',title:'Service',everyDays:90,nextDue:'2026-11-01',active:true}]});
    assert.equal(run(planned,'owner','rpt.acc.asset-life',{asOf:'2025-01-02'}).rows.find(x=>x.cells.asset==='Boiler').group,undefined);
    assert.equal(ra.cells.expectedLife,'5 y 0 m');assert.equal(ra.cells.grade,'C');assert.equal(ra.cells.replacementCost,5000000);
  });
  test(T+'asset-life: reserve spent equal to accrued is not red, one paisa above is red (boundary)',()=>{
    const mk=capex=>{const s=assetState({expectedLifeMonths:300});
      s.bookings=[{...seed().bookings[0],lines:[{id:'RL',label:'Room',amount:1000000,date:'2026-06-01',kind:'room',taxBps:1200}]}];
      s.accountingX={...(s.accountingX??{}),assets:[{id:'FA1',no:'FA-2026-001',ledger:'1510',name:'AC plant',cost:capex,date:'2026-07-01',vendor:'V',expense:'EX1',itc:0,putToUse:'2026-07-02'}]};return s;};
    const tot=r=>r.rows.find(x=>x.kind==='total');
    const a=al(mk(30000));assert.equal(tot(a).cells.reserveAccrued,30000);assert.equal(tot(a).cells.capexSpent,30000);assert.equal(tot(a).group,undefined);
    assert.equal(tot(al(mk(30001))).group,'red');
    const s3=withCfg(mk(30000),{'ffe.reservePct':5});assert.equal(tot(al(s3)).cells.reserveAccrued,50000);
  });
  test(T+'asset-life: DEC-061 money columns removed for the Supervisor (desk right), Housekeeper / Maintenance hold no money gate',()=>{
    const s=assetState({expectedLifeMonths:300,replacementCostPaise:900});
    const sup=al(s,{},'supervisor');assert.deepEqual([...sup.removed].sort(),['capexSpent','replacementCost','reserveAccrued','wdv']);assert.ok(sup.columns.some(c=>c.key==='age'));
    assert.ok(al(s,{},'accountant').columns.some(c=>c.key==='replacementCost'));
    for(const u of ['housekeeper','maint','reception'])assert.equal(M.holdsMoneyGate(s,u,reportDef('rpt.acc.asset-life')),false,u);
  });
  test(T+'asset-life: kind filter, no-life assets blank, screen = Excel = PDF text',()=>{
    const s=assetState({expectedLifeMonths:60,conditionGrade:'B'});
    assert.equal(al(s,{kind:'AC'}).rows.filter(x=>x.kind==='row').length,0);assert.equal(al(s,{kind:'Other'}).rows.filter(x=>x.kind==='row').length,1);
    assert.ok(threeWay(M,assert,al(s)).length===1);
  });
}
