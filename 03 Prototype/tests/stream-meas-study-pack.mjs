// F12 MEAS · meas-study-pack: ChartBlock (bar / line / stacked / heat; screen SVG = Excel native chart = print / PDF from the same cells),
// rpt.study.management-pack (composition by id, contents, executive summary, MEAS_OFF, three-way), meas.study.packSchedule (off / monthly /
// quarterly through the F7 schedule tick), and the legacy golden harness's dry-mode export.
import {build} from 'esbuild';
import {threeWay,unzipStored} from './rpt-golden/harness.mjs';

// One bundle = one registry: the mock service plus this lane's modules (the shared M does not export the chart helpers).
const out='tests/.generated/meas-study-pack.mjs';
await build({stdin:{contents:`export * from './src/services/mock/index.ts';export * from './src/domain/reports/charts/chart.ts';export * from './src/domain/reports/study-pack/management-pack.ts';`,
  resolveDir:process.cwd(),loader:'ts'},bundle:true,platform:'node',format:'esm',outfile:out,logLevel:'error'});
const C=await import('./.generated/meas-study-pack.mjs');

export function register({test,assert}){
  const T='[F12-STUDY-PACK] ';
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const bar={id:'t-bar',title:'Test bar',kind:'bar',labelTitle:'Month',labels:['Apr 25','May 25','Jun 25'],
    series:[{key:'rev',label:'Revenue',type:'money',values:[12345600,0,98765400]},{key:'n',label:'Nights',type:'int',values:[40,12,77]}]};
  const result=(sections)=>({id:'rpt.study.chart-test',title:'Chart test',params:{},columns:sections[0].columns,rows:sections[0].rows,sections,
    about:{runAt:'2026-10-24T10:00',user:'Owner',dateBasis:'Test',provisional:false,legacy:false}});
  let fy26;const FY26=()=>fy26??=C.loadScenario('FY26');
  const setVal=(s,k,v)=>{s=structuredClone(s);s.configX??={};s.configX.values={...(s.configX.values??{}),[k]:v};return s;};

  // ── ChartBlock ──
  test(T+'chartSection → chartOf round trip; kind in the label column key; legacy points marked',()=>{
    const sec=C.chartSection({...bar,legacy:[true,false,false]});
    assert.equal(sec.key,'chart:t-bar');assert.equal(sec.columns[0].key,'chart.bar');assert.equal(sec.columns[1].money,true);
    assert.equal(sec.rows[0].group,'legacy');
    const back=C.chartOf(sec);assert.equal(back.kind,'bar');assert.deepEqual(back.labels,bar.labels);assert.deepEqual(back.series.map(s=>s.values),bar.series.map(s=>s.values));
    assert.deepEqual(back.legacy,[true,false,false]);
    assert.equal(C.chartOf({columns:[{key:'x',label:'X',type:'text',money:false}],rows:[]}),null);
    assert.throws(()=>C.chartSection({...bar,series:[{key:'a',label:'A',type:'int',values:[1]}]}),/values for 3 labels/);
  });
  test(T+'bar parity: the same values in the screen SVG, the Excel native chart cache + cells, and the print / PDF SVG',()=>{
    const sec=C.chartSection(bar);const r=result([sec]);
    const want=bar.series.map(s=>s.values);
    // screen
    const svg=C.chartSvg(C.chartOf(sec));
    for(const [si,s] of bar.series.entries())assert.deepEqual([...svg.matchAll(new RegExp(`<rect [^>]*data-series="${s.key}" data-value="(\\d+)"`,'g'))].map(m=>+m[1]),want[si],'screen '+s.key);
    assert.ok(svg.includes('var(--primary)')&&!/#[0-9a-f]{6}/i.test(svg),'screen colours are tokens');
    // Excel: chart part, cache = the sheet cells (money in rupees), relationships and content types
    const files=unzipStored(C.buildXlsx(C.toXlsx(r,'Test')));
    const chart=files['xl/charts/chart1.xml'];assert.ok(chart,'chart part');assert.ok(chart.includes('<c:barChart>')&&chart.includes('<c:grouping val="clustered"/>'));
    const caches=[...chart.matchAll(/<c:numCache>(.*?)<\/c:numCache>/g)].map(m=>[...m[1].matchAll(/<c:v>([^<]*)<\/c:v>/g)].map(x=>+x[1]));
    assert.deepEqual(caches,[want[0].map(v=>v/100),want[1]],'Excel cache');
    assert.ok(chart.includes(`'Test bar'!$B$2:$B$4`),'series references the sheet cells');
    assert.ok(files['xl/worksheets/sheet1.xml'].includes('<drawing xmlns:r=')&&files['xl/worksheets/_rels/sheet1.xml.rels'].includes('../drawings/drawing1.xml'));
    assert.ok(files['xl/drawings/_rels/drawing1.xml.rels'].includes('../charts/chart1.xml'));
    assert.ok(files['[Content_Types].xml'].includes('/xl/charts/chart1.xml')&&files['[Content_Types].xml'].includes('drawing+xml'));
    threeWay(C,assert,r); // the cells themselves: screen = Excel = print text
    // print / PDF
    const html=C.printHtml(C.toPrintModel(r,'Test')).body;
    for(const [si,s] of bar.series.entries())assert.deepEqual([...html.matchAll(new RegExp(`<rect [^>]*data-series="${s.key}" data-value="(\\d+)"`,'g'))].map(m=>+m[1]),want[si],'print '+s.key);
    // geometry: bar heights are proportional to the drawn value (rupees for money)
    const g=C.chartGeometry(C.chartOf(sec));const revBars=g.bars.filter(b=>b.series==='rev');
    assert.ok(Math.abs(revBars[0].h/revBars[2].h-123456/987654)<0.001);assert.equal(revBars[1].h,0);
  });
  test(T+'line, stacked and heat map: line points, stacked tops add up, heat = shaded table (no Excel chart part)',()=>{
    const line=C.chartOf(C.chartSection({...bar,id:'l',kind:'line'}));const gl=C.chartGeometry(line);
    assert.equal(gl.lines.length,2);assert.deepEqual(gl.lines[1].points.map(p=>p.value),[40,12,77]);
    const st=C.chartOf(C.chartSection({...bar,id:'s',kind:'stacked',series:[{key:'a',label:'A',type:'int',values:[1,2,3]},{key:'b',label:'B',type:'int',values:[3,2,1]}]}));
    const gs=C.chartGeometry(st);const col0=gs.bars.filter(b=>b.index===0);assert.ok(Math.abs(col0[0].h+col0[1].h-gs.scale*4)<0.02);
    const files=unzipStored(C.buildXlsx(C.toXlsx(result([C.chartSection({...bar,id:'s',kind:'stacked'})]),'T')));assert.ok(files['xl/charts/chart1.xml'].includes('<c:grouping val="stacked"/>'));
    const heat=C.chartOf(C.chartSection({id:'h',title:'Heat',kind:'heat',labels:['Mon','Tue'],series:[{key:'apr',label:'Apr',type:'pct',values:[10,50]},{key:'may',label:'May',type:'pct',values:[30,null]}]}));
    assert.deepEqual(C.heatShare(heat),[[0,1],[0.5,null]]);
    const ht=C.chartSvg(heat);assert.ok(ht.startsWith('<table class="rpt-heat"')&&ht.includes('data-value="50"')&&ht.includes('color-mix'));
    const hf=unzipStored(C.buildXlsx(C.toXlsx(result([C.chartSection({id:'h',title:'Heat',kind:'heat',labels:['Mon'],series:[{key:'a',label:'A',type:'int',values:[1]}]})]),'T')));
    assert.ok(!Object.keys(hf).some(k=>k.startsWith('xl/charts/')),'heat has no chart part');
  });
  test(T+'rule M: a money series removed with its column leaves a chart of the remaining series (no money drawn)',()=>{
    const sec=C.chartSection(bar);const noMoney={...sec,columns:sec.columns.filter(c=>!c.money),rows:sec.rows.map(r=>({...r,cells:Object.fromEntries(Object.entries(r.cells).filter(([k])=>k!=='rev'))}))};
    const c=C.chartOf(noMoney);assert.deepEqual(c.series.map(s=>s.key),['n']);assert.ok(!C.chartSvg(c).includes('data-series="rev"'));
  });

  // ── rpt.study.management-pack ──
  test(T+'pack registered (study area, MEAS status, PERM-RPT-002 money gate, screen / xlsx / pdf, phone no)',()=>{
    const d=C.reportDef('rpt.study.management-pack');assert.ok(d);
    assert.deepEqual([d.area,d.status,d.moneyGate,d.formats.join(','),d.phone,d.dateClass],['study','MEAS','PERM-RPT-002','screen,xlsx,pdf','no','D-FY']);
    assert.ok(d.dateBasis.includes('Legacy to 30 Oct 2026 / HCC live'));
  });
  test(T+'pack switched off: absent from the catalogue and refused with MEAS_OFF; the pack switch off via the study pack too',()=>{
    let s=setVal(C.seed(),'meas.study.management-pack',false);
    assert.ok(!C.reportCatalogue(s,'owner').some(r=>r.id===C.PACK_ID));assert.equal(code(()=>C.runReport(s,'owner',C.PACK_ID,{})),'MEAS_OFF');
    s=setVal(C.seed(),'meas.pack.study',false);assert.equal(code(()=>C.runReport(s,'owner',C.PACK_ID,{})),'MEAS_OFF');
    assert.ok(C.reportCatalogue(C.seed(),'owner').some(r=>r.id===C.PACK_ID));
  });
  test(T+'pack composes the 21 study ids by registry lookup: absent ids skipped with a note, switched-off ids say so',()=>{
    const s=setVal(C.seed(),'meas.study.revenue-monthly',false);
    const r=C.runReport(s,'owner',C.PACK_ID,{},{all:true});const t=C.rptTables(r);
    assert.deepEqual(t.slice(0,5).map(x=>x.key),['contents','summary','scorecard','chart:pack-revenue','chart:pack-occupancy']);assert.equal(t.at(-1).key,'notes');
    const contents=t[0].rows;assert.equal(contents.length,21);assert.deepEqual(contents.map(x=>x.cells.report),C.PACK_STUDY_IDS);
    assert.equal(contents.find(x=>x.cells.report==='rpt.study.revenue-monthly').cells.status,'switched off');
    for(const x of contents.filter(x=>x.cells.report!=='rpt.study.revenue-monthly'))assert.equal(x.cells.status,C.reportDef(x.cells.report)?'included':'not built yet');
    assert.ok(t.at(-1).rows[0].cells.note.includes('Legacy to 30 Oct 2026 / HCC live'));
  });
  test(T+'pack on the FY26 fixture (FY 2026-27 to the business date): summary = sum of months, occupancy and ADR by the contract definitions, three-way equal',()=>{
    const s=FY26();const r=C.runReport(s,'owner',C.PACK_ID,{fy:'2026-27'},{all:true});
    const sum=C.rptTables(r).find(x=>x.key==='summary');const months=sum.rows.filter(x=>x.kind==='row'),fy=sum.rows.find(x=>x.kind==='total');
    assert.equal(months.length,7);
    for(const k of ['roomNights','chargedNights','roomRevenue'])assert.equal(fy.cells[k],months.reduce((a,m)=>a+m.cells[k],0),k);
    assert.ok(fy.cells.roomNights>0&&fy.cells.roomRevenue>0,'FY26 has stays');
    const rooms=fy.cells.rooms;assert.equal(fy.cells.occupancy,Math.round(fy.cells.roomNights*1000/(rooms*207))/10);
    assert.equal(fy.cells.adr,Math.round(fy.cells.roomRevenue/fy.cells.chargedNights));
    const ch=C.chartOf(C.rptTables(r).find(x=>x.key==='chart:pack-revenue'));assert.deepEqual(ch.series[0].values,months.map(m=>m.cells.roomRevenue));
    threeWay(C,assert,r);
    const files=unzipStored(C.buildXlsx(C.toXlsx(r,'Test')));assert.equal(Object.keys(files).filter(k=>/^xl\/charts\/chart\d+\.xml$/.test(k)).length,2,'two native charts');
    const score=C.rptTables(r).find(x=>x.key==='scorecard').rows;assert.equal(score[0].cells.targetPct,35);assert.equal(score[1].cells.targetMoney,205000);
    assert.equal(score[0].cells.status,fy.cells.occupancy>=35?'On target':'Below target');
  });

  // ── meas.study.packSchedule ──
  test(T+'packScheduleDue: monthly every 1st, quarterly 1 Apr / Jul / Oct / Jan, off never',()=>{
    const d=(st,date)=>C.packScheduleDue(st,date);
    assert.deepEqual(['2026-11-01','2026-11-02','2027-01-01'].map(x=>d('monthly',x)),[true,false,true]);
    assert.deepEqual(['2026-10-01','2026-11-01','2027-01-01','2027-04-01','2027-02-01'].map(x=>d('quarterly',x)),[true,false,true,true,false]);
    assert.equal(d('off','2026-11-01'),false);
  });
  test(T+'schedule honoured through the F7 tick: monthly runs Owner + Manager copies on the 1st, off and quarterly-off-month run nothing',()=>{
    const at=(s,date)=>{s=structuredClone(s);s.date=date;s.clock=date+'T06:30';return s;};
    const tick=(s)=>{s=structuredClone(s);s.rptX??={favourites:{},schedules:[],runs:[],exportLog:[]};s.rptX.schedules=C.packScheduleSync(s);return C.execute(s,'owner',{type:'RPT_RUN_DUE'});};
    const packRuns=s=>(s.rptX?.runs??[]).filter(r=>r.reportId===C.PACK_ID);
    let s=tick(at(C.seed(),'2026-11-01'));
    assert.deepEqual(packRuns(s).map(r=>[r.scheduleId,r.params.fy,r.status==='failed']),[[C.PACK_SCHEDULE_IDS.owner,'2026-27',false],[C.PACK_SCHEDULE_IDS.manager,'2026-27',false]]);
    assert.equal(packRuns(tick(s)).length,2,'once per date');
    assert.equal(packRuns(tick(at(setVal(C.seed(),'meas.study.packSchedule','off'),'2026-11-01'))).length,0,'off');
    assert.equal(packRuns(tick(at(setVal(C.seed(),'meas.study.packSchedule','quarterly'),'2026-11-01'))).length,0,'quarterly, November');
    assert.equal(packRuns(tick(at(setVal(C.seed(),'meas.study.packSchedule','quarterly'),'2027-01-01'))).length,2,'quarterly, January');
    assert.equal(packRuns(tick(at(setVal(C.seed(),'meas.study.management-pack',false),'2026-11-01'))).length,0,'item off: schedule disabled');
    const sc=C.packScheduleSync(at(C.seed(),'2026-11-01'));assert.deepEqual(sc.filter(x=>x.reportId===C.PACK_ID).map(x=>[x.format,x.cadence.kind,x.cadence.day,x.cadence.time]),[['pdf','monthly',1,'06:00'],['pdf','monthly',1,'06:00']]);
  });
}
