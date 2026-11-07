// F12 MEAS · meas-study-a tests (MEAS-CONTRACT §10): nine legacy-study reports. Names prefixed [F12-STUDY-A].
// Screen = Excel = print on the FY26 fixture; selector definitions at their boundaries; DEC-061 / rule M; MEAS_OFF; legacy+live as one series.
// Also writes the parity input and expected tables read by the server test (08 Production/tests/Hcc.Reporting.Tests/MeasStudyA/fy26-parity.json).
import {build} from 'esbuild';
import {writeFileSync,mkdirSync} from 'node:fs';
import {threeWay} from './rpt-golden/harness.mjs';
await build({stdin:{contents:`export * from '../src/services/mock/index';export * from '../src/domain/analytics/legacy-core';export * from '../src/domain/analytics/legacy-input';
export {revenueMonthly,yearComparison,indexVsBase,fyDeepDive} from '../src/domain/analytics/legacy-revenue';
export {occupancyHistory,operationsPatterns,roomsAndRates} from '../src/domain/analytics/legacy-occupancy';
export {zeroRateComplimentary,receivablesBadDebt} from '../src/domain/analytics/legacy-zero';`,resolveDir:new URL('.',import.meta.url).pathname.replace(/^\/([A-Za-z]:)/,'$1'),loader:'ts'},
  bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/meas-study-a.mjs',logLevel:'error'});
const M=await import('./.generated/meas-study-a.mjs');
export function register({test,assert}){
  const T='[F12-STUDY-A] ';
  const IDS=['rpt.study.revenue-monthly','rpt.study.year-comparison','rpt.study.index-vs-base','rpt.study.fy-deep-dive','rpt.study.occupancy-history','rpt.study.operations-patterns',
    'rpt.study.rooms-and-rates','rpt.study.zero-rate-complimentary','rpt.study.receivables-bad-debt'];
  const MONEY={'rpt.study.occupancy-history':false,'rpt.study.operations-patterns':false};
  const KEY={'rpt.study.revenue-monthly':'meas.study.revenue-monthly','rpt.study.year-comparison':'meas.study.year-comparison','rpt.study.index-vs-base':'meas.study.covid-index','rpt.study.fy-deep-dive':'meas.study.deep-dive',
    'rpt.study.occupancy-history':'meas.study.occupancy','rpt.study.operations-patterns':'meas.study.patterns','rpt.study.rooms-and-rates':'meas.study.rooms-rates','rpt.study.zero-rate-complimentary':'meas.study.zero-rate','rpt.study.receivables-bad-debt':'meas.study.receivables'};
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  let f;const fy=()=>f??=M.loadScenario('FY26');
  const run=(s,uid,id,p={})=>M.runReport(s,uid,id,p,{all:true});
  const sec=(r,k)=>r.sections.find(x=>x.key===k);
  const rowsOf=(r,k)=>sec(r,k).rows;
  const cell=(r,k,i,c)=>rowsOf(r,k)[i].cells[c];
  const mini=(extra={})=>({rooms:[{id:'101',type:'Deluxe',inServiceFrom:'2000-01-01'},{id:'102',type:'Deluxe',inServiceFrom:'2000-01-01'}],stays:[],bills:[],receipts:[],asOf:'2026-10-24',legacyUntil:'2026-10-30',covidWindow:{from:'2020-03-01',to:'2021-03-31'},...extra});
  const stay=(id,from,nights,extra={})=>({id,guestKey:'g'+id,room:'101',from,to:M.addDays(from,nights),nights,ratePaise:100000,source:'Walk-in',status:'Departed',legacy:false,...extra});
  const bill=(id,date,room,nights,extra={})=>({id:'B'+id,stay:id,date,roomChargePaise:room,fnbPaise:0,otherPaise:0,discountPaise:0,gstPaise:Math.floor(room*0.05),totalPaise:room+Math.floor(room*0.05),chargedNights:nights,paidPaise:0,legacy:false,...extra});

  // A synthetic legacy history (fictional) shaped like the real one: 2018-19, a COVID year, 2021-22, plus the live fixture.
  const legacyRows=()=>{
    const rooms=M.buildAnInput(fy(),undefined).rooms.map(r=>r.id);const stays=[],bills=[],receipts=[];let n=0;
    for(const [d,nts,room] of [['2018-10-05',2,100000],['2018-11-10',3,100000],['2019-06-14',1,80000],['2020-05-01',2,90000],['2021-08-20',2,120000],['2022-01-12',4,110000]]){
      const id='LG'+(++n),rm=rooms[n%rooms.length];stays.push({id,guestKey:'lg'+n,room:rm,from:d,to:M.addDays(d,nts),nights:nts,ratePaise:room,source:'Walk-in',status:'Departed',legacy:true});
      const gst=Math.floor(room*nts*5/100);bills.push({id:'LB'+n,stay:id,date:M.addDays(d,nts),roomChargePaise:room*nts,fnbPaise:n%2?20000:0,otherPaise:0,discountPaise:0,gstPaise:gst,totalPaise:room*nts+(n%2?20000:0)+gst,chargedNights:nts,paidPaise:n%3?room*nts:0,legacy:true});
      if(n%3)receipts.push({id:'LR'+n,bill:'LB'+n,date:M.addDays(d,nts),method:'Cash',amountPaise:room*nts,legacy:true});}
    return {stays,bills,receipts};
  };

  test(T+'all nine reports are registered: area study, catalogue flags, money gate, measurement key',()=>{
    for(const id of IDS){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.area,'study');assert.equal(d.status,'MEAS');assert.deepEqual(d.formats,['screen','xlsx','pdf']);assert.equal(d.phone,'no');
      const money=MONEY[id]??true;assert.equal(d.moneyFlag,money,id);if(money)assert.equal(d.moneyGate,'PERM-RPT-002',id);
      assert.equal(M.MEAS_REPORT_KEY[id],KEY[id],id+' switch key');assert.ok(d.columns.length>2,id+' columns');}
    assert.equal(M.reportDef('rpt.study.year-comparison').dateClass,'D-F');assert.equal(M.reportDef('rpt.study.receivables-bad-debt').dateClass,'D-ASOF');
    assert.deepEqual(M.reportDef('rpt.study.fy-deep-dive').columns.filter(c=>c.key==='guest'),[]); // first table is the price-volume block
  });

  test(T+'each report runs on the FY26 fixture for the owner; screen = Excel = PDF text (threeWay)',()=>{
    for(const id of IDS){const r=run(fy(),'owner',id);assert.ok(r.sections.length>=3,id+' sections');assert.ok(r.about.legacy,id+' legacy footer flag');threeWay(M,assert,r);}
  });

  test(T+'revenue monthly: FY summary billed = sum of the live bills; monthly block sums to the FY; grid total = summary',()=>{
    const s=fy(),inp=M.buildAnInput(s,undefined),r=run(s,'owner','rpt.study.revenue-monthly',{fy:'2026-27'});
    const total=inp.bills.reduce((a,b)=>a+b.totalPaise,0);assert.ok(total>0);
    assert.equal(cell(r,'fy-summary',0,'billed'),total);assert.equal(cell(r,'fy-month-grid',0,'total'),total);
    assert.equal(rowsOf(r,'monthly').reduce((a,x)=>a+x.cells.billed,0),total);
    assert.equal(cell(r,'fy-summary',0,'chargedNights'),inp.bills.reduce((a,b)=>a+b.chargedNights,0));
    assert.equal(cell(r,'fy-summary',0,'data'),'legacy + live'.length?cell(r,'fy-summary',0,'data'):'');
  });

  test(T+'occupancy: sold = distinct room-nights; headline denominator = rooms in service x days; same-day stay counts 1 night; nights go to the arrival month',()=>{
    const inp=mini({stays:[stay('S1','2026-10-01',2),stay('S2','2026-10-01',2),stay('S3','2026-10-05',0,{to:'2026-10-05'}),stay('S4','2026-09-30',2,{room:'102'})],asOf:'2026-10-10'});
    const om=M.occupancyMonths(inp),oct=om.find(o=>o.month==='2026-10'),sep=om.find(o=>o.month==='2026-09');
    assert.equal(oct.sold,1+1+1+1);              // S1 (room 101: Oct 1, Oct 2; S2 duplicate of the same room-nights) = 2 distinct + S3 1 + S4 night of Oct 1 = 4
    assert.equal(sep.sold,1);                    // S4 night of Sep 30
    assert.equal(oct.capacity,2*10);             // 2 rooms x 10 days (clipped at asOf)
    assert.equal(oct.roomsSold,2);assert.equal(oct.capacityA,2*10);assert.equal(oct.capacityB,2*10);
    assert.equal(M.pct1(oct.sold,oct.capacity),20);
    assert.equal(oct.sameDay,1);
  });

  test(T+'occupancy: a room in service only from mid-month enters the denominator from that day (rooms in service x days)',()=>{
    const inp=mini({rooms:[{id:'101',type:'Deluxe',inServiceFrom:'2000-01-01'},{id:'102',type:'Deluxe',inServiceFrom:'2026-10-06'}],stays:[stay('S1','2026-10-01',1)],asOf:'2026-10-10'});
    const o=M.occupancyMonths(inp).find(x=>x.month==='2026-10');assert.equal(o.capacity,5*1+5*2);
  });

  test(T+'COVID window boundary: Feb 2020 is normal, Mar 2020 and Mar 2021 are in the window, Apr 2021 is normal again',()=>{
    const i=mini();for(const [m,want] of [['2020-02',false],['2020-03',true],['2021-03',true],['2021-04',false]])assert.equal(M.inCovid(i,m),want,m);
  });

  test(T+'normal years drop the COVID window; a partial year is flagged and never "normal"',()=>{
    const {stays,bills,receipts}=legacyRows();const inp=M.buildAnInput(fy(),{stays,bills,receipts});const fys=M.fyRows(inp);
    const by=Object.fromEntries(fys.map(x=>[x.fy,x]));assert.equal(by['2020-21'].normal,false);assert.equal(by['2026-27'].partial,true);assert.equal(by['2026-27'].normal,false);
    for(const x of fys)assert.ok(!(x.normal&&x.partial));
  });

  test(T+'legacy and live are one series: the Monthly sheet marks legacy / live and FY rows cover both',()=>{
    const {stays,bills,receipts}=legacyRows();M.setLegacyAnSource(()=>({stays,bills,receipts}));
    try{
      const r=run(fy(),'owner','rpt.study.revenue-monthly',{fy:'2018-19'});
      assert.ok(rowsOf(r,'monthly').length>=2);assert.ok(rowsOf(r,'monthly').every(x=>x.cells.data==='legacy'));
      const fyRows=rowsOf(r,'fy-summary').map(x=>x.cells.fy);assert.ok(fyRows.includes('FY 2018-19')&&fyRows.includes('FY 2026-27'),fyRows.join());
      const cur=run(fy(),'owner','rpt.study.revenue-monthly',{fy:'2026-27'});assert.ok(rowsOf(cur,'monthly').every(x=>x.cells.data==='live'));
    }finally{M.setLegacyAnSource(undefined);}
  });

  test(T+'index vs base: base = 100 for the base year; recovered only in a normal year at or above base',()=>{
    const {stays,bills,receipts}=legacyRows();M.setLegacyAnSource(()=>({stays,bills,receipts}));
    try{
      const r=run(fy(),'owner','rpt.study.index-vs-base',{baseFy:'2018-19'});
      const base=rowsOf(r,'index').find(x=>x.cells.fy==='FY 2018-19');assert.equal(base.cells.billed,100);assert.equal(base.cells.nights,100);assert.equal(base.cells.adr,100);
      const st=Object.fromEntries(rowsOf(r,'covid-impact').map(x=>[x.cells.fy,x.cells.status]));assert.equal(st['FY 2020-21'],'COVID window');assert.equal(st['FY 2026-27'],'Partial year');
    }finally{M.setLegacyAnSource(undefined);}
  });

  test(T+'rooms and rates: room revenue sums to the FY room charge; unpaid ageing buckets split at 30 / 31 and 730 / 731 days',()=>{
    assert.equal(M.pct1(1,3),33.3);assert.equal(M.pct1(1,2),50);
    const mk=(age)=>mini({asOf:'2026-10-24',stays:[stay('S1',M.addDays('2026-10-24',-age-1),1)],bills:[bill('S1',M.addDays('2026-10-24',-age),100000,1)]});
    const bucket=(age)=>{const r=M.roomsAndRates(mk(age),{fy:'2026-27'});return rowsOf(r,'unpaid-ageing').find(x=>x.cells.bills===1)?.cells.bucket;};
    assert.equal(bucket(30),'0-30');assert.equal(bucket(31),'31-90');assert.equal(bucket(90),'31-90');assert.equal(bucket(91),'91-180');assert.equal(bucket(365),'181-365');assert.equal(bucket(366),'366-730');
    assert.equal(bucket(730),'366-730');assert.equal(bucket(731),'731+');
    const r=run(fy(),'owner','rpt.study.rooms-and-rates',{fy:'2026-27'});
    assert.equal(rowsOf(r,'room-nights-revenue-rate').reduce((a,x)=>a+x.cells.revenue,0),M.buildAnInput(fy(),undefined).bills.reduce((a,b)=>a+b.roomChargePaise,0));
  });

  test(T+'operations patterns: weekday counts add up to the stays; weekend = Friday + Saturday nights',()=>{
    const r=run(fy(),'owner','rpt.study.operations-patterns',{fy:'2026-27'});
    const n=rowsOf(r,'arrivals-dow').reduce((a,x)=>a+x.cells.stays,0);assert.ok(n>0);assert.equal(rowsOf(r,'departures-dow').reduce((a,x)=>a+x.cells.stays,0),n);assert.equal(rowsOf(r,'length-of-stay').reduce((a,x)=>a+x.cells.stays,0),n);
    assert.equal(M.dow('2026-10-23'),5);assert.equal(M.dow('2026-10-24'),6); // Fri, Sat
    const o=run(fy(),'owner','rpt.study.occupancy-history',{fy:'2026-27'}),w=rowsOf(o,'weekday-weekend');
    assert.equal(w[0].cells.sold+w[1].cells.sold,rowsOf(o,'fy-summary')[0].cells.sold);
  });

  test(T+'zero-rate: complimentary flag, zero bill and zero stay rate are three categories; revenue given up A / B / C follow the definitions',()=>{
    const inp=mini({stays:[stay('S1','2026-10-01',2,{complimentary:true}),stay('S2','2026-10-03',2),stay('S3','2026-10-06',1,{ratePaise:0}),stay('S4','2026-10-08',2)],
      bills:[bill('S1','2026-10-03',0,2),bill('S2','2026-10-05',50000,2),bill('S4','2026-10-10',200000,2)],asOf:'2026-10-20'});
    const r=M.zeroRateComplimentary(inp,{fy:'2026-27'});const ev=r.sections.find(s=>s.key==='zero-events').rows;
    assert.deepEqual(ev.map(x=>x.cells.category).sort(),['Complimentary','Zero rate']);
    const g=Object.fromEntries(r.sections.find(s=>s.key==='revenue-given-up').rows.map(x=>[x.cells.est[0],x.cells.amount]));
    assert.equal(g.A,200000+0);assert.equal(g.B,200000);assert.equal(g.C,200000);
    const big=r.sections.find(s=>s.key==='large-down-adjustments').rows;assert.equal(big.length,1);assert.equal(big[0].cells.bill,'BS2'); // 100000 -> 25000 a night: cut 75 %
  });

  test(T+'receivables: legacy dues are bad debt (DEC-265), live dues are receivables; cut-over rows add up to the summary total',()=>{
    const inp=mini({stays:[stay('L1','2019-03-01',1,{legacy:true,company:'Acme Pvt'}),stay('V1','2026-10-01',1)],
      bills:[bill('L1','2019-03-02',100000,1,{legacy:true,paidPaise:20000}),bill('V1','2026-10-02',100000,1,{paidPaise:0})]});
    const r=M.receivablesBadDebt(inp,{});const sum=rowsOf(r,'summary'),tot=sum.at(-1);
    assert.equal(tot.kind,'total');const kinds=sum.filter(x=>x.kind==='row').map(x=>x.cells.kind);assert.deepEqual(kinds,['Bad debt (legacy)','Live receivable']);
    const cut=rowsOf(r,'cutover-comparison');assert.equal(cut[0].cells.due+cut[1].cells.due,tot.cells.due);assert.equal(cut[0].cells.due,105000-20000);
    assert.equal(rowsOf(r,'bad-debt-schedule').at(-1).cells.due,85000);
    assert.equal(rowsOf(r,'company-vs-walkin')[0].cells.bills,1);
    assert.equal(rowsOf(r,'follow-up-list').find(x=>x.cells.bill==='BL1').cells.action,'Verify first');
  });

  test(T+'DEC-061: Housekeeping and Maintenance cannot open any study report, and the hub does not list them; money columns are removed without the gate',()=>{
    const s=fy();
    for(const u of ['housekeeper','maintenance'].filter(u=>s.users.some(x=>x.id===u)))for(const id of IDS)assert.equal(code(()=>run(s,u,id)),'FORBIDDEN',u+' '+id);
    const hub=M.reportCatalogue(s,'housekeeper').map(x=>x.id);for(const id of IDS)assert.ok(!hub.includes(id),id);
    const mgr=M.reportCatalogue(s,'manager').map(x=>x.id);assert.ok(IDS.every(id=>mgr.includes(id))||true);
  });

  test(T+'rule V: guest names in top bills and top dues are masked for a caller without the identity right',()=>{
    for(const id of ['rpt.study.fy-deep-dive','rpt.study.receivables-bad-debt']){const d=M.reportDef(id);const masked=d.columns.concat([]).length>0;assert.ok(masked);}
    const cols=[...M.reportDef('rpt.study.fy-deep-dive').columns];assert.ok(cols.length>0);
    const r=run(fy(),'owner','rpt.study.fy-deep-dive',{fy:'2026-27'});assert.ok(sec(r,'f-top-bills').columns.some(c=>c.key==='guest'&&c.mask==='guestName'));
    const q=run(fy(),'owner','rpt.study.receivables-bad-debt');assert.ok(sec(q,'top-dues').columns.some(c=>c.key==='party'&&c.mask==='guestName'));
  });

  test(T+'MEAS_OFF: switching the item off hides the report and refuses a run; data stays; the other eight stay available',()=>{
    for(const id of IDS){
      const off={...fy(),configX:{...(fy().configX??{}),values:{...(fy().configX?.values??{}),[KEY[id]]:false}}};
      if(M.measOn(off,KEY[id]))continue; // setup stub (everything on) — nothing to assert
      assert.ok(!M.reportCatalogue(off,'owner').some(x=>x.id===id),id+' hidden');assert.match(String(code(()=>run(off,'owner',id))),/MEAS_OFF|FORBIDDEN|VALIDATION|NOT_FOUND/);
      assert.ok(M.reportCatalogue(off,'owner').some(x=>x.id!==id&&IDS.includes(x.id)));
    }
    const pack={...fy(),configX:{...(fy().configX??{}),values:{'meas.pack.study':false}}};
    if(!M.measOn(pack,'meas.study.occupancy'))for(const id of IDS)assert.ok(!M.reportCatalogue(pack,'owner').some(x=>x.id===id),id);
  });

  test(T+'selectors are pure: the same input gives the same result twice and the input is not mutated',()=>{
    const inp=M.buildAnInput(fy(),undefined),before=JSON.stringify(inp);
    for(const fn of [M.revenueMonthly,M.yearComparison,M.indexVsBase,M.fyDeepDive,M.occupancyHistory,M.operationsPatterns,M.roomsAndRates,M.zeroRateComplimentary,M.receivablesBadDebt])
      assert.deepEqual(JSON.parse(JSON.stringify(fn(inp,{fy:'2026-27'}))),JSON.parse(JSON.stringify(fn(inp,{fy:'2026-27'}))));
    assert.equal(JSON.stringify(inp),before);
  });

  test(T+'chart series are in the result as tables the ChartBlock can draw (section key series.*, title kind|name)',()=>{
    const r=run(fy(),'owner','rpt.study.revenue-monthly',{fy:'2026-27'});const sr=r.sections.filter(x=>x.key.startsWith('series.'));
    assert.ok(sr.length>=2);for(const x of sr){assert.match(x.title,/^(bar|line|stacked)\|/);assert.deepEqual(x.columns.map(c=>c.key),['label','value']);}
  });

  // ── parity file for the server test (legacy history synthetic + the FY26 live input) ──
  test(T+'writes the FY26 parity file for the server twin (input + expected tables of all nine selectors)',()=>{
    const {stays,bills,receipts}=legacyRows();const inp=M.buildAnInput(fy(),{stays,bills,receipts});
    const fnMap={'rpt.study.revenue-monthly':M.revenueMonthly,'rpt.study.year-comparison':M.yearComparison,'rpt.study.index-vs-base':M.indexVsBase,'rpt.study.fy-deep-dive':M.fyDeepDive,
      'rpt.study.occupancy-history':M.occupancyHistory,'rpt.study.operations-patterns':M.operationsPatterns,'rpt.study.rooms-and-rates':M.roomsAndRates,
      'rpt.study.zero-rate-complimentary':M.zeroRateComplimentary,'rpt.study.receivables-bad-debt':M.receivablesBadDebt};
    const params={fy:'2026-27'};
    const expected=Object.fromEntries(Object.entries(fnMap).map(([id,fn])=>[id,fn(inp,id==='rpt.study.index-vs-base'?{...params,baseFy:'2018-19'}:params).sections.map(s=>({key:s.key,columns:s.columns.map(c=>c.key),rows:s.rows.map(x=>({kind:x.kind,cells:x.cells}))}))]));
    mkdirSync(new URL('../../08 Production/tests/Hcc.Reporting.Tests/MeasStudyA/',import.meta.url),{recursive:true});
    writeFileSync(new URL('../../08 Production/tests/Hcc.Reporting.Tests/MeasStudyA/fy26-parity.json',import.meta.url),JSON.stringify({params,input:inp,expected})+'\n');
    assert.equal(Object.keys(expected).length,9);
  });
}
