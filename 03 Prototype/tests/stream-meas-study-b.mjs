// F12 MEAS · meas-study-b tests (MEAS-CONTRACT §10). Names prefixed [F12-STUDY-B]. The twelve ported study reports: definitions (hand-computed),
// three-way equality on the FY26 fixture, DEC-061 / MEAS_OFF, and the golden file the C# twin (Hcc.Reporting.Tests) must reproduce number for number.
import {build} from 'esbuild';
import {readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import {threeWay} from './rpt-golden/harness.mjs';

const IDS=['revenue-kpi','channel-economics','pricing-shape','guest-repeat-value','guest-origin-plan','housekeeping-operations','fnb-halls-ancillary','finance-cost','corporate-accounts','compliance-risk','forecast-scenarios','owner-dashboard'].map(x=>'rpt.study.'+x);
const GOLDEN='../08 Production/tests/Hcc.Reporting.Tests/Goldens/study-b-an.json';
mkdirSync('tests/.generated',{recursive:true});
await build({stdin:{contents:"export * from '../../src/domain/analytics/beyond-revenue';export * from '../../src/domain/analytics/beyond-guest';export * from '../../src/domain/analytics/beyond-ops';export * from '../../src/domain/analytics/beyond-finance';",resolveDir:'tests/.generated'},
  bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/study-b-an.mjs',logLevel:'error'});
const A=await import('./.generated/study-b-an.mjs');
const SEL={'rpt.study.revenue-kpi':A.revenueKpi,'rpt.study.channel-economics':A.channelEconomics,'rpt.study.pricing-shape':A.pricingShape,'rpt.study.guest-repeat-value':A.guestRepeatValue,'rpt.study.guest-origin-plan':A.guestOriginPlan,
  'rpt.study.housekeeping-operations':A.housekeepingOperations,'rpt.study.fnb-halls-ancillary':A.fnbHallsAncillary,'rpt.study.finance-cost':A.financeCost,'rpt.study.corporate-accounts':A.corporateAccounts,'rpt.study.compliance-risk':A.complianceRisk,
  'rpt.study.forecast-scenarios':A.forecastScenarios,'rpt.study.owner-dashboard':A.ownerDashboard};

const addDays=(d,n)=>new Date(Date.parse(d+'T12:00:00Z')+n*864e5).toISOString().slice(0,10);
/** Small hand-built input: 2 rooms from 1 Apr 2026, 10 days, 5 nights slept. Occupancy 5/20 = 25.0 %. */
const tiny=()=>({rooms:[{id:'R1',type:'Deluxe',inServiceFrom:'2026-04-01'},{id:'R2',type:'Twin',inServiceFrom:'2026-04-01'}],
  stays:[{id:'S1',guestKey:'g1',room:'R1',from:'2026-04-01',to:'2026-04-04',nights:3,ratePaise:200000,source:'Walk-in',status:'departed',legacy:false},
         {id:'S2',guestKey:'g2',room:'R2',from:'2026-04-02',to:'2026-04-04',nights:2,ratePaise:150000,source:'OTA',channel:'Agoda',status:'departed',legacy:false}],
  bills:[{id:'B1',stay:'S1',date:'2026-04-04',roomChargePaise:600000,fnbPaise:0,otherPaise:0,discountPaise:0,gstPaise:30000,totalPaise:630000,chargedNights:3,paidPaise:630000,legacy:false},
         {id:'B2',stay:'S2',date:'2026-04-04',roomChargePaise:240000,fnbPaise:0,otherPaise:0,discountPaise:0,gstPaise:12000,totalPaise:252000,chargedNights:2,paidPaise:0,legacy:false}],
  receipts:[],asOf:'2026-04-10',legacyUntil:'2026-10-30',covidWindow:{from:'2020-03-01',to:'2021-03-31'}});

/** The golden input: deterministic, 3 FYs, 6 rooms, legacy + live, OTA, companies, plans, states. The C# twin reads it from the JSON beside its tests. */
export function goldenInput(){
  let x=12345;const rnd=n=>{x=(x*1103515245+12345)%2147483648;return x%n;};
  const types=['Suite','Premium','Deluxe','Deluxe','Twin','Twin'];
  const rooms=types.map((t,i)=>({id:'R'+(i+1),type:t,inServiceFrom:i<5?'2024-04-01':'2025-04-01',suite:t==='Suite'}));
  const stays=[],bills=[],receipts=[];let n=0;
  const comps=['Acme Traders','Bharat Steel','Cobalt Foods'],states=['Maharashtra','Karnataka','Gujarat'],plans=['EP','CP','MAP'];
  for(let d='2024-04-01';d<='2026-10-27';d=addDays(d,2)){
    if(rnd(10)<4)continue;const room=rooms[rnd(6)];if(room.inServiceFrom>d)continue;
    const nights=1+rnd(rnd(10)<2?9:3),to=addDays(d,nights),id='S'+(++n),ota=rnd(10)<3,comp=rnd(10)<2,legacy=d<'2026-09-01';
    const rate=(room.type==='Suite'?400000:room.type==='Premium'?300000:room.type==='Deluxe'?250000:200000)+rnd(5)*10000;
    const chan=ota?(rnd(2)?'Agoda':'MakeMyTrip'):'';
    stays.push({id,guestKey:'g'+(1+rnd(30)),room:room.id,from:d,to,nights,ratePaise:rate,source:ota?'OTA':'Walk-in',channel:chan,company:comp?comps[rnd(3)]:undefined,purpose:rnd(10)<1?'Official':undefined,
      status:to<='2026-10-27'?'departed':'in house',legacy,mealPlan:plans[rnd(3)],state:states[rnd(3)],nationality:rnd(20)?'Indian':'Nepal',idCaptured:rnd(10)>0,phoneCaptured:true,people:1+rnd(3),
      bookedType:rnd(8)===0?'Twin':undefined,inTime:rnd(2)?'11:30':'14:00',outTime:rnd(2)?'10:30':'12:30'});
    const gross=rate*nights,disc=rnd(5)===0?Math.floor(gross/10):0,fnb=rnd(3)*50000,oth=rnd(4)===0?30000:0,gst=Math.round((gross-disc+fnb+oth)*0.12),total=gross-disc+fnb+oth+gst;
    const paid=rnd(12)===0?0:total,rd=addDays(to,rnd(3)-1);
    bills.push({id:'B'+n,stay:id,date:to,roomChargePaise:gross,fnbPaise:fnb,otherPaise:oth,discountPaise:disc,gstPaise:gst,totalPaise:total,chargedNights:nights,paidPaise:paid,legacy});
    if(paid)receipts.push({id:'RC'+n,bill:'B'+n,date:rd<d?d:rd,method:['Cash','UPI','Card'][rnd(3)],amountPaise:paid,legacy});
  }
  return {rooms,stays,bills,receipts,asOf:'2026-10-27',legacyUntil:'2026-10-30',covidWindow:{from:'2020-03-01',to:'2021-03-31'}};
}
const GP={'rpt.study.channel-economics':{commissionPct:'15'},'rpt.study.finance-cost':{fixedPerMonth:'20000000',variablePerRn:'50000'},'rpt.study.forecast-scenarios':{budgetGrowthPct:'10',adrDeltaPct:'5',compOccPct:'40',compAdr:'250000'}};
const gparams=id=>({fy:'2025-26',...(id.endsWith('owner-dashboard')?{month:'2026-10'}:{}),...(GP[id]??{}),topN:'10',
  extras:{hallEvents:[{date:'2025-11-02',days:2,revenuePaise:1500000}],expenses:[{date:'2025-06-10',category:'Supplies',amountPaise:250000},{date:'2025-07-01',category:'Repairs',amountPaise:90000}],
    audit:[{action:'Rate edit',count:12},{action:'Discount',count:7}],licences:[{name:'FSSAI',validTo:'2027-01-31'}]}});
export function goldenFile(){
  const input=goldenInput(),expected={};
  for(const id of IDS){const p=gparams(id),r=SEL[id](input,p);expected[id]={sections:r.sections.map(s=>({key:s.key,columns:s.columns.map(c=>c.key),rows:s.rows.map(w=>({kind:w.kind,cells:w.cells}))})),legacy:r.about.legacy};}
  return JSON.stringify({input,params:Object.fromEntries(IDS.map(id=>[id,gparams(id)])),expected},null,1)+'\n';
}

export function register({test,M,assert}){
  const T='[F12-STUDY-B] ';
  const run=(s,uid,id,p={})=>M.runReport(s,uid,id,p);
  let fy26;const fy=()=>fy26??=M.loadScenario('FY26');

  test(T+'twelve reports registered in area study with their switches and the legacy footer',()=>{
    for(const id of IDS){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.area,'study');assert.match(d.dateBasis,/Legacy to 30 Oct 2026 \/ HCC live/);assert.ok(M.MEAS_REPORT_KEY[id]?.startsWith('meas.study.'),id);}
    assert.equal(M.reportDef('rpt.study.owner-dashboard').priority,'P1');assert.equal(M.reportDef('rpt.study.owner-dashboard').phone,'yes');
    assert.equal(M.reportDef('rpt.study.guest-origin-plan').moneyFlag,false);assert.equal(M.reportDef('rpt.study.revenue-kpi').moneyFlag,true);
  });
  test(T+'definitions: occupancy = room-nights slept / (rooms in service x days); ADR = room charge / charged nights; RevPAR = room charge / available',()=>{
    const r=A.revenueKpi(tiny(),{fy:'2026-27'}),m=r.sections[0].rows[0].cells;
    assert.equal(m.month,'2026-04');assert.equal(m.avail,20);assert.equal(m.sold,5);assert.equal(m.occ,25);assert.equal(m.cn,5);assert.equal(m.rev,840000);assert.equal(m.adr,168000);assert.equal(m.revpar,42000);
    const t=r.sections.find(s=>s.key==='type').rows.find(w=>w.cells.type==='Deluxe').cells;assert.equal(t.avail,10);assert.equal(t.sold,3);assert.equal(t.occ,30);assert.equal(t.adr,200000);
    const ch=r.sections.find(s=>s.key==='channel').rows;assert.deepEqual(ch.map(w=>w.cells.channel),['Direct / walk-in','Agoda']);assert.equal(ch[1].cells.share,28.6);
  });
  test(T+'no division by zero: empty input gives null ratios, no rows',()=>{
    const r=A.revenueKpi({...tiny(),stays:[],bills:[]},{fy:'2026-27'}),m=r.sections[0].rows[0].cells;assert.equal(m.occ,0);assert.equal(m.adr,null);
    A.revenueKpi({rooms:[],stays:[],bills:[],receipts:[],asOf:'2026-04-10',legacyUntil:'2026-10-30',covidWindow:{from:'2020-03-01',to:'2021-03-31'}},{});
  });
  test(T+'channel economics: net OTA ADR = OTA ADR x (1 - commission %); commission scenarios 12 / 15 %',()=>{
    const r=A.channelEconomics(tiny(),{fy:'2026-27',commissionPct:'20'}),c=r.sections[0].rows.find(w=>w.cells.fy==='2026-27').cells;
    assert.equal(c.otaRn,2);assert.equal(c.otaAdr,120000);assert.equal(c.directAdr,200000);assert.equal(c.c12,28800);assert.equal(c.c15,36000);assert.equal(c.netAdr,96000);assert.equal(c.gap,104000);
  });
  test(T+'repeat list boundary: 3 stays are listed, 2 are not',()=>{
    const i=tiny(),base=i.stays[0];
    i.stays=[1,2,3].map(k=>({...base,id:'X'+k,from:'2026-04-0'+k,to:'2026-04-0'+(k+1),nights:1,guestKey:'a'})).concat([1,2].map(k=>({...base,id:'Y'+k,from:'2026-04-0'+k,to:'2026-04-0'+(k+1),nights:1,room:'R2',guestKey:'b'})));
    i.bills=[];const l=A.guestRepeatValue(i,{fy:'2026-27'}).sections.find(s=>s.key==='list').rows;assert.deepEqual(l.map(w=>w.cells.guest),['a']);assert.equal(l[0].cells.stays,3);
  });
  test(T+'break-even is empty when ADR does not exceed the variable cost; win-back needs 12 months without a stay',()=>{
    const i=tiny();assert.equal(A.financeCost(i,{fy:'2026-27',fixedPerMonth:'100',variablePerRn:'999999'}).sections[1].rows[0].cells.bePct,null);
    const be=A.financeCost(i,{fy:'2026-27',fixedPerMonth:'6000000',variablePerRn:'0'}).sections[1].rows[0].cells;assert.equal(be.adr,168000);assert.equal(be.bePct,59.5);
    i.stays[0].company='Acme';i.stays[0].from='2025-04-11';i.stays[0].to='2025-04-12';i.stays[0].nights=1;i.bills[0].date='2025-04-12';
    assert.equal(A.corporateAccounts(i,{fy:'2025-26'}).sections.find(s=>s.key==='winback').rows.length,0);
    assert.equal(A.corporateAccounts({...i,asOf:'2026-04-12'},{fy:'2025-26'}).sections.find(s=>s.key==='winback').rows.length,1);
  });
  test(T+'COVID window is left out of the night-of-week capacity',()=>{
    const i=tiny();i.asOf='2021-03-31';i.rooms[0].inServiceFrom='2020-01-01';i.rooms[1].inServiceFrom='2020-01-01';i.stays=[{...i.stays[0],id:'C1',from:'2020-01-01',to:'2020-01-02',nights:1}];i.bills=[];
    const cap=A.pricingShape(i,{fy:'2020-21'}).sections[0].rows.reduce((a,w)=>a+w.cells.cap,0);
    assert.equal(cap,2*60);
  });
  test(T+'owner dashboard has 12 months ending the chosen month with the 12 KPIs',()=>{
    const r=A.ownerDashboard(goldenInput(),{month:'2026-10'});assert.equal(r.rows.length,12);assert.equal(r.rows[11].cells.month,'2026-10');assert.equal(r.rows[0].cells.month,'2025-11');
    assert.equal(r.columns.filter(c=>!['month','src'].includes(c.key)).length,12);assert.equal(r.rows[11].cells.src,'Legacy + live');
  });
  test(T+'screen = Excel = PDF numbers on the FY26 fixture for every report, owner and accountant',()=>{
    const s=fy();for(const id of IDS)for(const u of ['owner','accountant']){const r=run(s,u,id,{});assert.ok(r.sections?.length,id);threeWay(M,assert,r);}
  });
  test(T+'DEC-061: Housekeeping / Maintenance cannot open a study report',()=>{
    const s=fy();for(const id of IDS)for(const u of ['housekeeper','maintenance'].filter(u=>s.users.some(x=>x.id===u)))assert.throws(()=>run(s,u,id,{}),/cannot open|Access|FORBIDDEN/i,u+' '+id);
  });
  test(T+'MEAS_OFF: a switched-off study item refuses the run; the other reports still run',()=>{
    let s=fy();assert.ok(M.reportCatalogue(s,'owner').some(c=>c.id==='rpt.study.revenue-kpi'));
    s={...s,configX:{...(s.configX??{}),values:{...(s.configX?.values??{}),'meas.study.rm-kpi':false}}};
    assert.throws(()=>run(s,'owner','rpt.study.revenue-kpi',{}),e=>e.code==='MEAS_OFF');
    assert.ok(!M.reportCatalogue(s,'owner').some(c=>c.id==='rpt.study.revenue-kpi'));assert.ok(run(s,'owner','rpt.study.pricing-shape',{}));
  });
  test(T+'golden file for the C# twin is current (STUDY_B_WRITE=1 rewrites it)',()=>{
    const want=goldenFile();if(process.env.STUDY_B_WRITE){mkdirSync('../08 Production/tests/Hcc.Reporting.Tests/Goldens',{recursive:true});writeFileSync(GOLDEN,want);}
    assert.equal(readFileSync(GOLDEN,'utf8'),want,'regenerate with STUDY_B_WRITE=1');
  });
}
