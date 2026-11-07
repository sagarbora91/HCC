// F12 MEAS · meas-settings tests (MEAS-CONTRACT §1, §10). Names prefixed [F12-SETTINGS]. Key table (pinned, parity with the C#
// MeasurementCatalogue), three-level rule (module / pack / item, core always on), MEAS_PACK_OFF, MEAS_OFF on capture + report run,
// hub / catalogue filter, Owner-only (PERM-MEAS-001), one change row per item on a pack switch, export / import carries the switches.
import {readFileSync} from 'node:fs';

export function register({test,M,assert}){
  const {seed,execute,MEAS_ITEMS,MEAS_PACKS,MEAS_TARGETS,MEAS_REPORT_KEY,measOn,measTarget,measGuard,measIsCore,measPackCount,measReportOn,measFilter,
    registerMeasReport,reportCatalogue,runReport,cfgGroupDefs,cfgGroup,cfgGroupAccess,cfgExport,cfgImportPlan,CFG_GROUPS}=M;
  const code=fn=>{try{fn();return 'OK';}catch(e){return e.code;}};
  const set=(s,uid,setting,value)=>execute(s,uid,{type:'SETTING_SET',setting,value});
  const PACK_KEYS={daily:['flash','regrets','rate-variance','discrepancy','high-balance','adjustments','no-show','inventory','cashier'].map(x=>'meas.daily.'+x),
    revenue:['demand-calendar','forecast','pickup','displacement','overbooking','restrictions','rate-shop'].map(x=>'meas.rev.'+x),
    guest:['survey','reputation','recovery','preferences','desk-timing','upsell','repeat'].map(x=>'meas.guest.'+x),
    hk:['productivity','ooo','ppm','utilities','linen','assets'].map(x=>'meas.hk.'+x),
    people:['productivity','attrition','scorecard','training','pulse'].map(x=>'meas.people.'+x),
    study:['revenue-monthly','year-comparison','covid-index','deep-dive','occupancy','patterns','rooms-rates','zero-rate','receivables','rm-kpi','rm-channel','rm-pricing',
      'guest-repeat','guest-origin','hk-ops','fnb-halls','finance','sales','compliance','forecast','dashboard','management-pack'].map(x=>'meas.study.'+x)};

  test('[F12-SETTINGS] key table pinned: 6 packs, 56 items (34 measures + 22 study), frozen keys, 3 core, module ties',()=>{
    assert.deepEqual(MEAS_PACKS.map(p=>p.key),['daily','revenue','guest','hk','people','study'].map(p=>'meas.pack.'+p));
    for(const [pack,keys] of Object.entries(PACK_KEYS))assert.deepEqual(MEAS_ITEMS.filter(i=>i.pack===pack).map(i=>i.key),keys,pack);
    assert.equal(MEAS_ITEMS.length,56);assert.equal(MEAS_ITEMS.filter(i=>i.report).length,54);
    assert.deepEqual(MEAS_ITEMS.filter(i=>i.core).map(i=>i.key),['meas.daily.flash','meas.daily.inventory']);
    assert.deepEqual(Object.fromEntries(MEAS_ITEMS.filter(i=>i.module).map(i=>[i.key,i.module])),{'meas.hk.ppm':'maintenance','meas.hk.utilities':'maintenance','meas.hk.linen':'inventory',
      'meas.people.productivity':'payroll','meas.people.attrition':'payroll','meas.people.scorecard':'payroll','meas.people.training':'payroll','meas.people.pulse':'payroll'});
  });
  test('[F12-SETTINGS] parity: TypeScript key table = C# MeasurementCatalogue (keys, packs, reports, core, modules, targets)',()=>{
    const cs=readFileSync(new URL('../../08 Production/src/Hcc.Application/Configuration/MeasurementCatalogue.cs',import.meta.url),'utf8');
    const rows=[...cs.matchAll(/^\s*I\("([a-z]+)", "(meas\.[a-z]+\.[a-z-]+)", "[^"]*"(?:, "(rpt\.[a-z]+\.[a-z0-9-]+)")?(.*)$/gm)].map(m=>({pack:m[1],key:m[2],report:m[3],core:/core: true/.test(m[4]),module:(m[4].match(/module: "([a-z]+)"/)||[])[1]}));
    assert.deepEqual(rows,MEAS_ITEMS.map(i=>({pack:i.pack,key:i.key,report:i.report,core:!!i.core,module:i.module})));
    const targets=[...cs.matchAll(/new\("(meas\.target\.[A-Za-z]+)", "[^"]*", "([a-z]+)", (\d+)\)/g)].map(m=>[m[1],m[2],Number(m[3])]);
    assert.deepEqual(targets,MEAS_TARGETS.map(t=>[t.key,t.kind,t.default]));
  });
  test('[F12-SETTINGS] group measure: Software and security, not a money group, every switch / target / other setting registered Owner-only',()=>{
    const g=cfgGroup('measure');assert.equal(g.label,'Measurement reports');assert.equal(g.section,'Software and security');assert.equal(g.money,false);
    assert.equal(CFG_GROUPS.at(-1).id,'measure');
    const s=seed(),keys=cfgGroupDefs(s,'measure').map(d=>d.key);
    for(const k of [...MEAS_PACKS.map(p=>p.key),...MEAS_ITEMS.map(i=>i.key),...MEAS_TARGETS.map(t=>t.key),'meas.study.packSchedule','meas.daily.blockClose','meas.regrets.mandatory','yld.restrictionsEnforce','ffe.reservePct'])assert.ok(keys.includes(k),k);
    for(const t of new Set(s.rooms.map(r=>r.type)))assert.ok(keys.includes('yld.overbook.'+t),t);
    assert.ok(cfgGroupDefs(s,'measure').every(d=>d.money&&d.approver==='Owner'));
    assert.equal(cfgGroupAccess(s,'owner','measure').readOnly,false);
    assert.equal(cfgGroupAccess(s,'techadmin','measure').readOnly,true);
  });
  test('[F12-SETTINGS] defaults: everything on, M-9 targets (occupancy 35, ADR ₹2,050 in paise, NPS 30, high balance ₹5,000)',()=>{
    const s=seed();for(const i of MEAS_ITEMS)assert.equal(measOn(s,i.key),true,i.key);
    assert.equal(measTarget(s,'meas.target.occupancy'),35);assert.equal(measTarget(s,'meas.target.adr'),205000);assert.equal(measTarget(s,'meas.target.nps'),30);
    assert.equal(measTarget(s,'meas.target.highBalance'),500000);assert.equal(measTarget(s,'meas.target.cashVariance'),10000);
    assert.deepEqual(measPackCount(s,'daily'),{on:9,total:9});
  });
  test('[F12-SETTINGS] item off: Owner switches it off through Master configuration; history row; Tech admin and Manager refused (PERM-MEAS-001)',()=>{
    let s=seed();
    assert.equal(code(()=>set(s,'techadmin','meas.daily.regrets',false)),'FORBIDDEN');
    assert.equal(code(()=>set(s,'manager','meas.daily.regrets',false)),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'manager',{type:'MEAS_ITEM_SET',setting:'meas.daily.regrets',on:false})),'FORBIDDEN');
    s=set(s,'owner','meas.daily.regrets',false);
    assert.equal(measOn(s,'meas.daily.regrets'),false);assert.equal(s.configHistory[0].key,'meas.daily.regrets');
    assert.deepEqual(measPackCount(s,'daily'),{on:8,total:9});
    s=execute(s,'owner',{type:'MEAS_ITEM_SET',setting:'meas.daily.regrets',on:true});assert.equal(measOn(s,'meas.daily.regrets'),true);
  });
  test('[F12-SETTINGS] pack off: every non-core item off, one change row per item, core always on; pack on brings them back',()=>{
    let s=seed();const before=(s.configHistory||[]).length;
    s=set(s,'owner','meas.pack.daily',false);
    const core=MEAS_ITEMS.filter(i=>i.pack==='daily'&&measIsCore(s,i.key)).map(i=>i.key);
    assert.ok(core.includes('meas.daily.flash')&&core.includes('meas.daily.inventory'));
    for(const i of MEAS_ITEMS.filter(i=>i.pack==='daily'))assert.equal(measOn(s,i.key),core.includes(i.key),i.key);
    const rows=s.configHistory.slice(0,s.configHistory.length-before);
    assert.equal(rows.filter(r=>r.key==='meas.pack.daily').length,1);
    assert.deepEqual(rows.filter(r=>r.key.startsWith('meas.daily.')).map(r=>r.key).sort(),MEAS_ITEMS.filter(i=>i.pack==='daily'&&!core.includes(i.key)).map(i=>i.key).sort());
    for(const i of MEAS_ITEMS.filter(i=>i.pack!=='daily'))assert.equal(measOn(s,i.key),true,i.key);
    s=execute(s,'owner',{type:'MEAS_PACK_SET',pack:'daily',on:true});
    for(const i of MEAS_ITEMS.filter(i=>i.pack==='daily'))assert.equal(measOn(s,i.key),true,i.key);
  });
  test('[F12-SETTINGS] switching an item on under an off pack is refused MEAS_PACK_OFF (direct and through Master configuration)',()=>{
    let s=seed();s=execute(s,'owner',{type:'MEAS_ITEM_SET',setting:'meas.guest.upsell',on:false});s=execute(s,'owner',{type:'MEAS_PACK_SET',pack:'guest',on:false});
    assert.equal(code(()=>execute(s,'owner',{type:'MEAS_ITEM_SET',setting:'meas.guest.upsell',on:true})),'MEAS_PACK_OFF');
    assert.equal(code(()=>set(s,'owner','meas.guest.upsell',true)),'MEAS_PACK_OFF');
    assert.equal(measOn(s,'meas.guest.upsell'),false);
  });
  test('[F12-SETTINGS] core items cannot be switched off; High-balance is core while company credit (Groups) is on',()=>{
    let s=seed();
    assert.equal(code(()=>set(s,'owner','meas.daily.flash',false)),'VALIDATION');
    assert.equal(code(()=>execute(s,'owner',{type:'MEAS_ITEM_SET',setting:'meas.daily.inventory',on:false})),'VALIDATION');
    assert.equal(measIsCore(s,'meas.daily.high-balance'),true);
    assert.equal(code(()=>set(s,'owner','meas.daily.high-balance',false)),'VALIDATION');
    s=set(s,'owner','module.groups',false);
    assert.equal(measIsCore(s,'meas.daily.high-balance'),false);
    s=set(s,'owner','meas.daily.high-balance',false);assert.equal(measOn(s,'meas.daily.high-balance'),false);
  });
  test('[F12-SETTINGS] module off: its items are off even with pack and item on (payroll → people, maintenance → ppm / utilities, inventory → linen)',()=>{
    const s=seed();s.configX={...(s.configX||{}),modulesOff:['payroll','maintenance','inventory']};
    for(const i of MEAS_ITEMS.filter(i=>i.module))assert.equal(measOn(s,i.key),false,i.key);
    assert.equal(measOn(s,'meas.hk.ooo'),true);
  });
  test('[F12-SETTINGS] measGuard (capture forms / commands) throws MEAS_OFF only when off; measFilter hides off entries',()=>{
    let s=seed();assert.equal(code(()=>measGuard(s,'meas.guest.survey')),'OK');
    s=set(s,'owner','meas.guest.survey',false);
    assert.equal(code(()=>measGuard(s,'meas.guest.survey')),'MEAS_OFF');
    assert.deepEqual(measFilter(s,[{k:'meas.guest.survey'},{k:'meas.guest.repeat'},{k:undefined}],x=>x.k).map(x=>x.k),['meas.guest.repeat',undefined]);
  });
  test('[F12-SETTINGS] report hub and run: an off measurement report is absent from reportCatalogue and refuses a run with MEAS_OFF',()=>{
    let s=seed();const id=reportCatalogue(s,'owner')[0].id;const had=MEAS_REPORT_KEY[id];
    try{
      registerMeasReport(id,'meas.daily.cashier');assert.equal(measReportOn(s,id),true);
      s=set(s,'owner','meas.daily.cashier',false);
      assert.equal(reportCatalogue(s,'owner').some(r=>r.id===id),false);
      assert.equal(code(()=>runReport(s,'owner',id,{})),'MEAS_OFF');
      assert.throws(()=>registerMeasReport(id,'meas.daily.nope'),/not a measurement key/);
    }finally{if(had)MEAS_REPORT_KEY[id]=had;else delete MEAS_REPORT_KEY[id];}
    assert.equal(MEAS_REPORT_KEY['rpt.study.management-pack'],'meas.study.management-pack');
  });
  test('[F12-SETTINGS] configuration export / import carries the switches and targets',()=>{
    let s=seed();s=set(s,'owner','meas.pack.study',false);s=set(s,'owner','meas.hk.ooo',false);s=set(s,'owner','meas.target.adr',210000);
    const ex=cfgExport(s);
    assert.equal(ex.values['meas.pack.study'],false);assert.equal(ex.values['meas.hk.ooo'],false);assert.equal(ex.values['meas.target.adr'],210000);
    const plan=cfgImportPlan(seed(),'owner',ex);assert.deepEqual(plan.errors,[]);
    for(const k of ['meas.pack.study','meas.hk.ooo','meas.target.adr'])assert.ok(plan.apply.some(a=>a.def.key===k),k);
    let t=execute(seed(),'owner',{type:'CFG_IMPORT',config:ex});
    assert.equal(measOn(t,'meas.study.dashboard'),false);assert.equal(measOn(t,'meas.hk.ooo'),false);assert.equal(measTarget(t,'meas.target.adr'),210000);
  });
  // F12-MEAS2-C (M9 + L7, owner 7 Nov 2026)
  const csCat=()=>readFileSync(new URL('../../08 Production/src/Hcc.Application/Configuration/MeasurementCatalogue.cs',import.meta.url),'utf8');
  test('[F12-MEAS2-C] M9: yld.overbook.<type code> per room type, max 10 on both twins, reader clamps at the same maximum',()=>{
    const s=seed(),defs=cfgGroupDefs(s,'measure').filter(d=>d.key.startsWith('yld.overbook.'));
    const types=[...new Set(s.rooms.map(r=>r.type))].sort();
    assert.deepEqual(defs.map(d=>d.key),types.map(t=>'yld.overbook.'+t));
    assert.equal(M.MEAS_OVERBOOK_MAX,10);
    assert.ok(defs.every(d=>d.min===0&&d.max===10&&d.default===0&&d.type==='integer'),'0 … 10 rooms, default 0');
    assert.ok(types.every(t=>/^[A-Za-z0-9_-]{1,20}$/.test(t)),'type codes fit the server family suffix');
    const cs=csCat();assert.match(cs,/public const int OverbookMax = 10;/);assert.match(cs,/OverbookPrefix = "yld\.overbook\."/);
    const withCfg=v=>({...s,configX:{...(s.configX||{}),values:{...(s.configX?.values||{}),...v}}});
    assert.equal(M.yldOverbookAllowed(withCfg({['yld.overbook.'+types[0]]:15}),types[0]),10);
    let t=set(s,'owner','yld.overbook.'+types[0],2);assert.equal(M.yldOverbookAllowed(t,types[0]),2,'a value saved in the hub is read');
    assert.notEqual(code(()=>set(s,'owner','yld.overbook.'+types[0],11)),'OK','above the maximum refused');
  });
  test('[F12-MEAS2-C] L7: meas.target.upsellPerArrival is an Owner-changeable money target, default Rs 50 (5000 paise), on both twins',()=>{
    const t=MEAS_TARGETS.find(x=>x.key==='meas.target.upsellPerArrival');assert.ok(t);assert.equal(t.kind,'money');assert.equal(t.default,5000);
    const s=seed();assert.equal(measTarget(s,'meas.target.upsellPerArrival'),5000);
    const d=cfgGroupDefs(s,'measure').find(x=>x.key==='meas.target.upsellPerArrival');assert.ok(d);assert.equal(d.type,'money');assert.equal(d.approver,'Owner');
    const u=set(s,'owner','meas.target.upsellPerArrival',6000);assert.equal(measTarget(u,'meas.target.upsellPerArrival'),6000);
    assert.notEqual(code(()=>set(s,'techadmin','meas.target.upsellPerArrival',6000)),'OK','Tech admin cannot change it directly');
    assert.match(csCat(),/new\("meas\.target\.upsellPerArrival", "[^"]*", "money", 5000\)/);
  });
}
