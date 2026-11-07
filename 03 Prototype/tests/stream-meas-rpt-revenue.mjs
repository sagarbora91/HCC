// F12 MEAS · meas-rpt-revenue tests (MEAS-CONTRACT §10). Names prefixed [F12-RPT-REVENUE]. Six revenue-management reports: registration, red-row
// rules at the boundary, MEAS_OFF, DEC-061 money masks, empty data, three-way equality (screen = Excel = PDF text) through the golden harness.
import {build} from 'esbuild';
import {threeWay} from './rpt-golden/harness.mjs';
const IDS=['rpt.revenue.demand-calendar','rpt.revenue.pickup-pace','rpt.revenue.displacement','rpt.revenue.overbooking-wash','rpt.revenue.restriction-effect','rpt.revenue.rate-parity'];
const KEYS={'rpt.revenue.demand-calendar':'meas.rev.demand-calendar','rpt.revenue.pickup-pace':'meas.rev.pickup','rpt.revenue.displacement':'meas.rev.displacement',
  'rpt.revenue.overbooking-wash':'meas.rev.overbooking','rpt.revenue.restriction-effect':'meas.rev.restrictions','rpt.revenue.rate-parity':'meas.rev.rate-shop'};
export async function register({test,M,assert}){
  const T='[F12-RPT-REVENUE] ';
  await build({entryPoints:['src/domain/reports/meas-revenue/index.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/meas-revenue.mjs',logLevel:'error'});
  const R=await import('./.generated/meas-revenue.mjs');
  const withCfg=(s,values)=>({...s,configX:{...(s.configX??{}),values:{...(s.configX?.values??{}),...values}}});
  const run=(s,uid,id,p={})=>M.runReport(s,uid,id,p,{all:true});
  const clone=o=>JSON.parse(JSON.stringify(o));
  /** A booking cloned from the seed with the given fields; receipts are replaced so the booking date is known. */
  const mk=(s,over)=>{const {bookedOn,...rest}=over;
    return {...clone(s.bookings[0]),lines:[],receipts:bookedOn?[{...clone(s.bookings[0].receipts[0]),date:bookedOn}]:[],guestId:undefined,segments:undefined,...rest};};

  // ── registration ──
  test(T+'six reports registered: ids, area revenue, money flag, view permission, switch key',()=>{
    const defs=Object.fromEntries(M.allReports().map(d=>[d.id,d]));
    for(const id of IDS){const d=defs[id];assert.ok(d,id);assert.equal(d.area,'revenue');assert.equal(d.status,'MEAS');assert.equal(d.moneyFlag,true);assert.equal(d.moneyGate,'PERM-RPT-002');
      assert.equal(M.MEAS_REPORT_KEY[id],KEYS[id],id+' switch key');assert.ok(d.columns.some(c=>c.money));
      assert.ok(d.columns.filter(c=>c.money).every(c=>c.type==='money'));}
    assert.deepEqual(defs['rpt.revenue.demand-calendar'].columns.map(c=>c.key),['date','weekday','onBooks','forecast','lastYear','event','bar','suggested','offerOpen']);
    assert.deepEqual(defs['rpt.revenue.pickup-pace'].columns.map(c=>c.key),['month','rooms','lastYearRooms','revenue','lastYearRevenue','pace','pickup7','lead']);
    assert.deepEqual(defs['rpt.revenue.displacement'].columns.map(c=>c.key),['night','forecast','displaced','displacedRevenue','offerRevenue','net','recommendation']);
    assert.deepEqual(defs['rpt.revenue.overbooking-wash'].columns.map(c=>c.key),['weekday','channel','noShowPct','cancelPct','washRooms','allowed','walked','walkCost']);
    assert.deepEqual(defs['rpt.revenue.restriction-effect'].columns.map(c=>c.key),['night','restriction','refused','reshaped','revenue','unrestricted','mode']);
    assert.deepEqual(defs['rpt.revenue.rate-parity'].columns.map(c=>c.key),['stayDate','direct','ota','comp1','comp2','comp3','comp4','comp5','comp6','median','position','exception']);
  });

  // ── red-row rules, one boundary pair each ──
  test(T+'demand calendar rule: forecast 85 % with BAR unchanged is red, 84 % is not; forecast 25 % with no offer is red, 26 % is not',()=>{
    assert.equal(R.mrDemandRed(85,true,true),true);assert.equal(R.mrDemandRed(84,true,true),false);
    assert.equal(R.mrDemandRed(85,false,true),false,'BAR already raised');
    assert.equal(R.mrDemandRed(25,false,false),true);assert.equal(R.mrDemandRed(26,false,false),false);assert.equal(R.mrDemandRed(25,false,true),false,'an offer is open');
    const row=(pct,bar)=>R.demandRow({date:'2026-12-01',onBooks:1,forecastPct:pct,lastYearPct:null,event:'',bar,suggested:bar,base:200000,offerOpen:true});
    assert.equal(row(85,200000).red,true);assert.equal(row(85,205000).red,false);assert.equal(row(84,200000).red,false);
    assert.equal(row(85,200000).cells.weekday,'Tuesday');
  });
  test(T+'pickup rule: pace -10.1 % is red, exactly -10.0 % is not; no last year is never red',()=>{
    assert.equal(R.mrPaceTenths(899,1000),-101);assert.equal(R.mrPaceRed(R.mrPaceTenths(899,1000)),true);
    assert.equal(R.mrPaceTenths(900,1000),-100);assert.equal(R.mrPaceRed(R.mrPaceTenths(900,1000)),false);
    assert.equal(R.mrPaceTenths(5,0),null);assert.equal(R.mrPaceRed(null),false);
    const r=R.pickupRow({month:'2026-12',rooms:899,lyRooms:1000,revenue:1,lyRevenue:1,pickup7:3,lead:'8-30 days'});assert.equal(r.red,true);assert.equal(r.cells.pace,-10.1);
    assert.equal(R.pickupRow({month:'2026-12',rooms:900,lyRooms:1000,revenue:1,lyRevenue:1,pickup7:3,lead:''}).red,false);
  });
  test(T+'displacement rule: net loss of 1 paisa is red, break-even is not; recommendation and counter-rate arithmetic',()=>{
    assert.equal(R.mrDisplacementRed(-1),true);assert.equal(R.mrDisplacementRed(0),false);
    assert.equal(R.mrRecommendation(0,150000,0),'accept');assert.equal(R.mrRecommendation(-1,100000,150000),'counter-rate');assert.equal(R.mrRecommendation(-1,100000,150001),'decline');
    assert.equal(R.mrCounterRate(1200000,10),120000);assert.equal(R.mrCounterRate(1210000,10),125000);assert.equal(R.mrCounterRate(0,10),0);
  });
  test(T+'displacement report: a rate far below the transient ADR is a red loss and recommends a counter-rate or decline; a high rate accepts',()=>{
    const s=M.seed();
    const low=run(s,'owner','rpt.revenue.displacement',{from:s.date,to:M.addDays(s.date,2),rooms:'24',rate:'100'});
    const tot=low.rows.at(-1);assert.equal(tot.cells.net<0,true);assert.match(String(tot.cells.recommendation),/counter-rate|decline/);
    assert.equal(R.dispCompute(s,s.date,3,24,10000,'').total.red,true);
    const hi=run(s,'owner','rpt.revenue.displacement',{from:s.date,to:M.addDays(s.date,2),rooms:'1',rate:'90000'});
    assert.equal(hi.rows.at(-1).cells.recommendation,'accept');assert.equal(R.dispCompute(s,s.date,3,1,9000000,'').rows.every(r=>!r.red),true);
    assert.equal(low.rows.length,4);assert.equal(low.rows.at(-1).cells.night,'Total · 3 nights');
  });
  test(T+'overbooking rule: oversold above the allowed number is red, equal is not; the allowed number is the Owner setting',()=>{
    assert.equal(R.mrOversoldRed(1,0),true);assert.equal(R.mrOversoldRed(1,1),false);assert.equal(R.mrOversoldRed(0,0),false);
    let s=M.seed();const a=s.bookings.find(b=>b.status==='Confirmed')??s.bookings[1];
    const aType=s.rooms.find(r=>r.id===a.room).type;
    // the type is cut down to the one room, then two bookings share it: one room sold above the rooms in service
    const dup=mk(s,{id:'DUP-1',room:a.room,arrival:a.arrival,departure:a.departure,status:'Confirmed',source:'Direct'});
    s={...s,rooms:s.rooms.filter(r=>r.type!==aType||r.id===a.room),bookings:[...s.bookings.filter(b=>s.rooms.find(r=>r.id===b.room)?.type!==aType||b.room===a.room),dup]};
    const rows=R.washCompute(s,M.addDays(a.arrival,-1),a.departure,'','');
    assert.ok(rows.length>0);assert.ok(rows.some(r=>r.red),'oversold with allowed 0 is red');
    const ok=withCfg(s,{['yld.overbook.'+aType]:1});
    assert.ok(R.washCompute(ok,M.addDays(a.arrival,-1),a.departure,'','').every(r=>!r.red),'allowed 1 covers one oversold room');
    assert.equal(R.washCompute(ok,M.addDays(a.arrival,-1),a.departure,'','')[0].cells.allowed>=1,true);
  });
  test(T+'overbooking report counts no-shows, cancellations and the walked-guest cost',()=>{
    let s=M.seed();const b=s.bookings[1];
    s={...s,bookings:[{...b,status:'No show'}],measX:{walked:[{id:'W1',at:s.clock,by:'manager',booking:b.id,night:b.arrival,reason:'Oversold',costPaise:250000}]}};
    const rows=R.washCompute(s,b.arrival,b.arrival,'','');const mine=rows.filter(r=>r.cells.channel===b.source);
    assert.equal(mine.length,1);assert.equal(mine[0].cells.noShowPct,100);assert.equal(mine[0].cells.walked,1);assert.equal(mine[0].cells.walkCost,250000);
  });
  test(T+'restriction rule: a restricted night at 39 % occupancy is red, at 40 % it is not; counters and advisory mode shown',()=>{
    assert.equal(R.mrRestrictionRed(39,100),true);assert.equal(R.mrRestrictionRed(40,100),false);assert.equal(R.mrRestrictionRed(0,0),false);assert.equal(R.mrRestrictionRed(9,24),true);assert.equal(R.mrRestrictionRed(10,24),false);
    let s=M.seed();const d=M.addDays(s.date,-20);
    s={...s,yieldX:{...s.yieldX,restrictions:[{id:'R1',type:'*',from:d,to:d,minStay:3,cta:true,note:''}]},measX:{restrictions:[{night:d,kind:'MIN_STAY',refused:4,reshaped:1}]}};
    const rows=R.restrictionCompute(s,d,d,'');assert.deepEqual(rows.map(r=>r.cells.restriction),['CTA','MIN_STAY']);
    const ms=rows.find(r=>r.cells.restriction==='MIN_STAY').cells;assert.equal(ms.refused,4);assert.equal(ms.reshaped,1);assert.equal(ms.mode,'Advisory');
    assert.equal(R.restrictionCompute(s,d,d,'CTA').length,1);
    assert.equal(R.restrictionCompute(withCfg(s,{'yld.restrictionsEnforce':true}),d,d,'')[0].cells.mode,'Enforced');
    const rep=run(s,'owner','rpt.revenue.restriction-effect',{from:d,to:d});assert.equal(rep.rows.length,2);
  });
  test(T+'parity rule: OTA one paisa below direct is red; 115 % above the median is not red, 116 % is (occupancy under 40 %); 40 % occupancy is not',()=>{
    assert.equal(R.mrParityRed(200000,199999,null,0,100),true);assert.equal(R.mrParityRed(200000,200000,null,0,100),false);
    assert.equal(R.mrParityRed(230000,null,200000,39,100),false,'exactly 115 %');assert.equal(R.mrParityRed(230100,null,200000,39,100),true);
    assert.equal(R.mrParityRed(240000,null,200000,40,100),false,'occupancy 40 %');assert.equal(R.mrParityRed(240000,null,200000,39,100),true);
    assert.equal(R.mrMedian([300,100,200]),200);assert.equal(R.mrMedian([100,200]),150);assert.equal(R.mrMedian([]),null);assert.equal(R.mrPositionPct(230000,200000),15);
  });
  test(T+'rate parity report: shop entries fill the competitor columns (newest entry wins), median and exception text; labels carry the competitor name',()=>{
    let s=M.seed();const type=s.rooms[0].type,d=M.addDays(s.date,3);
    s={...s,measX:{competitors:[{id:'C1',name:'Alpha Residency',active:true},{id:'C2',name:'Beta Inn',active:true},{id:'C3',name:'Gamma Lodge',active:false}],
      rateshop:[{id:'a',date:s.date,stayDate:d,competitor:'C1',roomType:type,ratePaise:150000,withBreakfast:false,source:'phone',by:'manager'},
        {id:'b',date:M.addDays(s.date,1),stayDate:d,competitor:'C1',roomType:type,ratePaise:100000,withBreakfast:false,source:'phone',by:'manager'},
        {id:'c',date:s.date,stayDate:d,competitor:'C2',roomType:type,ratePaise:120000,withBreakfast:false,source:'phone',by:'manager'}]}};
    const r=run(s,'owner','rpt.revenue.rate-parity',{from:d,nights:'1',roomType:type});
    assert.equal(r.rows.length,1);const c=r.rows[0].cells;assert.equal(c.comp1,100000);assert.equal(c.comp2,120000);assert.equal(c.comp3,null);assert.equal(c.median,110000);
    assert.equal(r.columns.find(x=>x.key==='comp1').label,'Alpha Residency rate');assert.equal(r.columns.find(x=>x.key==='comp3').label,'Competitor 3 rate');
    assert.equal(typeof c.exception,'string');
  });
  test(T+'pickup report: derived on-the-books vs last year, pace red below -10 %, 7-day pickup, lead bucket; a stored OTB snapshot replaces the derived last-year figure',()=>{
    let s=M.seed();const t=s.date,month=R.monthPlus(t,1),ly=`${+month.slice(0,4)-1}${month.slice(4)}`;
    const stay=(m,n,id,room,bookedOn)=>mk(s,{id,room,arrival:m+'-10',departure:M.addDays(m+'-10',n),status:'Confirmed',bookedOn,rate:200000,source:'Direct'});
    s={...s,bookings:[stay(month,8,'NOW-1','101',M.addDays(t,-20)),stay(ly,10,'LY-1','102',M.addDays(t,-364-30))]};
    const rows=R.pickupCompute(s,2,'');const r=rows[1];
    assert.equal(r.cells.month,month);assert.equal(r.cells.rooms,8);assert.equal(r.cells.lastYearRooms,10);assert.equal(r.cells.pace,-20);assert.equal(r.red,true);
    assert.equal(r.cells.revenue,8*200000);assert.equal(r.cells.lastYearRevenue,10*200000);assert.equal(r.cells.lead,'31-90 days');assert.equal(r.cells.pickup7,0,'booked 20 days ago: no 7-day pickup');
    const s3={...s,bookings:[...s.bookings,stay(month,3,'NOW-2','103',M.addDays(t,-2))]};assert.equal(R.pickupCompute(s3,2,'')[1].cells.pickup7,3,'booked 2 days ago: 3 room-nights picked up');
    const snap={...s,measX:{otb:[{id:'o',takenOn:M.addDays(t,-364),night:ly+'-10',rooms:8,revenuePaise:1600000}]}};
    const r2=R.pickupCompute(snap,2,'')[1];assert.equal(r2.cells.lastYearRooms,8);assert.equal(r2.cells.pace,0);assert.equal(r2.red,false);
    assert.equal(R.pickupCompute(s,2,'Booking.com').every(x=>x.cells.rooms===0),true,'channel filter');
  });

  // ── switch, masks, empty data ──
  test(T+'MEAS_OFF: a switched-off item (or its pack) hides its report from the catalogue and refuses a run; data is kept',()=>{
    const s=M.seed();
    for(const id of IDS){
      assert.ok(M.reportCatalogue(s,'owner').some(r=>r.id===id),id+' listed');
      const off=withCfg(s,{[KEYS[id]]:false});
      assert.ok(!M.reportCatalogue(off,'owner').some(r=>r.id===id),id+' hidden');assert.throws(()=>run(off,'owner',id),/MEAS_OFF|switched off/);
      const pack=withCfg(s,{'meas.pack.revenue':false});assert.throws(()=>run(pack,'owner',id),/MEAS_OFF|switched off/);
    }
    assert.ok(M.reportCatalogue(withCfg(s,{'meas.rev.pickup':false}),'owner').some(r=>r.id==='rpt.revenue.rate-parity'),'other items stay on');
  });
  test(T+'DEC-061: Housekeeper and Maintenance never see a money column in these reports; Owner does',()=>{
    const s=M.seed();
    for(const id of IDS){
      for(const u of ['housekeeper','maint']){const r=(()=>{try{return run(s,u,id);}catch(e){return e;}})();
        assert.ok(r instanceof Error||!r.columns.some(c=>c.money),`${u} ${id}`);}
      assert.ok(run(s,'owner',id).columns.some(c=>c.money),id+' owner money');
    }
  });
  test(T+'empty data: no bookings, no captures = the entry note and zero rows, never an error',()=>{
    const s={...M.seed(),bookings:[],measX:undefined};
    for(const [id,p] of [['rpt.revenue.pickup-pace',{}],['rpt.revenue.overbooking-wash',{}],['rpt.revenue.rate-parity',{}]]){
      const r=run(s,'owner',id,p);assert.equal(r.rows.length,0,id);assert.match(r.about.dateBasis,/Capture needed/,id);}
    const rs=run(s,'owner','rpt.revenue.restriction-effect',{});assert.ok(Array.isArray(rs.rows));
    const dc=run(s,'owner','rpt.revenue.demand-calendar',{nights:'5'});assert.equal(dc.rows.length,5);assert.match(dc.about.dateBasis,/Capture needed/);
    const dp=run(s,'owner','rpt.revenue.displacement',{from:s.date,to:s.date});assert.equal(dp.rows.length,2);
  });
  test(T+'parameter guards: more than 92 nights, bad numbers and unknown room types are refused with a plain message',()=>{
    const s=M.seed();
    assert.throws(()=>run(s,'owner','rpt.revenue.demand-calendar',{nights:'93'}),/from 1 to 92|longer than 92/);
    assert.throws(()=>run(s,'owner','rpt.revenue.rate-parity',{nights:'x'}),/whole number/);
    assert.throws(()=>run(s,'owner','rpt.revenue.rate-parity',{roomType:'Nope'}),/Room type/);
    assert.throws(()=>run(s,'owner','rpt.revenue.pickup-pace',{months:'13'}),/1 to 12/);
    assert.throws(()=>run(s,'owner','rpt.revenue.displacement',{from:s.date,to:M.addDays(s.date,92)}),/92 days/);
    assert.throws(()=>run(s,'owner','rpt.revenue.restriction-effect',{from:'2026-01-01',to:'2026-06-30'}),/92 days/);
  });
  test(T+'digest helper: red counts per report, switched-off items skipped',()=>{
    const s=M.seed();const all=R.measRevenueRedCounts(s);assert.ok(all.length>=4);assert.ok(all.every(([id,n])=>IDS.includes(id)&&Number.isInteger(n)&&n>=0));
    const off=R.measRevenueRedCounts(withCfg(s,{'meas.rev.pickup':false}));assert.ok(!off.some(([id])=>id==='rpt.revenue.pickup-pace'));
  });

  // ── three-way equality on the FY26 fixture ──
  test(T+'FY26: every report runs for Owner and Manager; screen = Excel = PDF text',()=>{
    const s=M.loadScenario('FY26');
    const cases={'rpt.revenue.demand-calendar':{nights:'30'},'rpt.revenue.pickup-pace':{months:'6'},'rpt.revenue.displacement':{from:s.date,to:M.addDays(s.date,6),rooms:'6',rate:'1500'},
      'rpt.revenue.overbooking-wash':{from:'2026-04-01',to:s.date},'rpt.revenue.restriction-effect':{from:M.addDays(s.date,-30),to:s.date},'rpt.revenue.rate-parity':{nights:'14'}};
    for(const id of IDS){const r=run(s,'owner',id,cases[id]);threeWay(M,assert,r);const m=run(s,'manager',id,cases[id]);threeWay(M,assert,m);}
  });
}
