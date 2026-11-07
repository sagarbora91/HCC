// F7-R1 · report engine: registry, parameter binding by date class, permissions (rule V), money removal (rule M, DEC-061), masks, OP-13
// computed columns, paging / RPT_TOO_LARGE, renderers (Excel / print / CSV) identical to the screen, RPT_* commands, catalogue JSON, goldens.
import {readFileSync} from 'node:fs';
import {threeWay,goldenCheck} from './rpt-golden/harness.mjs';
export function register({test,M,assert}){
  const T='[F7-R1 RPT] ',ID='rpt.money.revenue-by-category-tender';
  const s0=()=>M.seed();
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  // A throw-away definition (never registered: the golden check walks the registry).
  const demo=(over={})=>({id:'rpt.admin.engine-test',title:'Engine test',area:'admin',priority:'P3',status:'planned',moneyFlag:true,moneyGate:'PERM-RPT-002',
    viewPerms:['PERM-RPT-001'],dateClass:'D-B',params:[{key:'tag',label:'Tag',type:'enum',options:['a','b'],default:'a'}],
    columns:[{key:'guest',label:'Guest',type:'text',money:false,mask:'guestName'},{key:'id',label:'ID',type:'text',money:false,mask:'idNumber'},
      {key:'n',label:'Nights',type:'int',money:false},{key:'amt',label:'Amount',type:'money',money:true},
      {key:'avg',label:'Per night',type:'money',money:false,computed:{expr:'div(amt,n)',deps:['amt','n']}},{key:'note',label:'Note',type:'text',money:false}],
    dateBasis:'Test basis',formats:['screen','xlsx','pdf','csv'],phone:'yes',
    select:(s,uid,p)=>({params:p,columns:demo().columns,rows:[{kind:'row',cells:{guest:'Journey Test',id:'1234 5678 9012',n:3,amt:100001,note:'=HYPERLINK("x")'}},
      {kind:'total',cells:{guest:'Total',id:null,n:3,amt:100001,note:''}}]}),...over});

  test(T+'registry: the demo report is registered; bad ids and unknown computed deps are refused',()=>{
    assert.ok(M.reportDef(ID));assert.equal(M.reportDef(ID).status,'built');
    assert.throws(()=>M.registerReport({...demo(),id:'report.bad'}),/rpt\.<area>/);
    assert.throws(()=>M.registerReport({...demo(),id:'rpt.money.x'}),/area/);
    assert.throws(()=>M.registerReport({...demo(),columns:[{key:'a',label:'A',type:'int',money:false,computed:{expr:'add(b)',deps:['b']}}]}),/unknown column/);
    assert.equal(M.reportDef('rpt.admin.engine-test'),undefined);
  });
  test(T+'demo report = rctReport to the paisa (A, B, C); Occ % comes from the OP-13 hook and equals the F4 figure',()=>{
    const s=s0();for(const [f,t] of [[s.date,s.date],['2026-10-18','2026-10-24']]){
      const r=M.runReport(s,'owner',ID,{from:f,to:t},{all:true}),x=M.rctReport(s,f,t);
      const [a,b,c]=r.sections;
      assert.equal(a.rows.at(-1).cells.net,x.aTotal.net);assert.equal(a.rows.at(-1).cells.gross,x.aTotal.gross);
      assert.deepEqual(a.rows.map(y=>y.cells.occPct),[...x.a,x.aTotal].map(y=>y.occPct));
      assert.equal(b.rows.at(-1).cells.netReceived,x.bTotal.netReceived);assert.equal(c.rows.at(-1).cells.amount,x.c.difference);
      assert.equal(r.columns,a.columns);
    }
  });
  test(T+'rule V: Reception, Cashier (own-shift V·S does not reach this report), Housekeeping, Maintenance refused; Accountant views and exports',()=>{
    const s=s0();
    for(const u of ['reception','cashier','housekeeper','supervisor','maint','techadmin'])assert.equal(code(()=>M.runReport(s,u,ID,{})),'FORBIDDEN',u);
    for(const u of ['owner','manager','accountant']){assert.ok(M.runReport(s,u,ID,{}));assert.ok(M.reportCatalogue(s,u).find(c=>c.id===ID).canExport,u);}
    assert.ok(!M.reportCatalogue(s,'reception').some(c=>c.id===ID));
    assert.equal(code(()=>M.runReport(s,'nobody',ID,{})),'FORBIDDEN');assert.equal(code(()=>M.runReport(s,'owner','rpt.money.nope',{})),'NOT_FOUND');
    // an export deny exception: view stays, export refused
    const t=structuredClone(s);t.policy=M.adminDefaultPolicy(t.clock);t.policy.exceptions.push({id:'X1',user:'accountant',perm:'PERM-RPT-003',effect:'deny',reason:'test',expires:'2099-01-01',by:'owner',at:t.clock});
    assert.ok(M.runReport(t,'accountant',ID,{}));assert.equal(code(()=>M.runReport(t,'accountant',ID,{},{purpose:'export'})),'FORBIDDEN');
  });
  test(T+'rule M: without the money gate money columns and computed columns over them are removed (not blanked); all-money → FORBIDDEN',()=>{
    const s=structuredClone(s0());s.policy=M.adminDefaultPolicy(s.clock);
    s.policy.exceptions.push({id:'X2',user:'manager',perm:'PERM-RPT-002',effect:'deny',reason:'test',expires:'2099-01-01',by:'owner',at:s.clock});
    const r=M.runDef(s,'manager',demo(),{});
    assert.deepEqual(r.columns.map(c=>c.key),['guest','id','n','note']);assert.deepEqual(r.removed.sort(),['amt','avg']);
    assert.ok(r.rows.every(x=>!('amt' in x.cells)&&!('avg' in x.cells)));
    const owner=M.runDef(s,'owner',demo(),{});assert.equal(owner.rows[0].cells.avg,33334);assert.equal(owner.removed,undefined);
    const onlyMoney=demo({columns:[{key:'amt',label:'Amount',type:'money',money:true}],select:(s_,u,p)=>({params:p,columns:[{key:'amt',label:'Amount',type:'money',money:true}],rows:[]})});
    assert.equal(code(()=>M.runDef(s,'manager',onlyMoney,{})),'FORBIDDEN');
  });
  test(T+'DEC-061 static check: no housekeeping / maintenance / stock role passes any money gate or export code named in the catalogue',()=>{
    const s=s0();const roles=new Set(M.RPT_NO_MONEY_ROLES);
    const gates=new Set(M.allReports().flatMap(d=>[d.moneyGate,d.exportPerm??'PERM-RPT-003']).filter(Boolean));gates.add('PERM-RPT-002');
    for(const u of s.users.filter(u=>roles.has(u.role)))for(const g of gates){assert.equal(M.rptHolds(s,u.id,g),false,u.id+' '+g);}
    const pol=M.adminDefaultPolicy(s.clock);
    for(const role of ['Supervisor','Housekeeper','Maintenance'])for(const g of gates){const c=pol.templates[role]?.[g];assert.ok(!c||c.effect==='deny',role+' '+g);}
    // a housekeeping-page report shows the rows but never money, and guest names are masked (no billing / reservations)
    const hk=demo({viewPerms:['housekeeping']});const r=M.runDef(s,'housekeeper',hk,{});
    assert.ok(!r.columns.some(c=>c.money||c.key==='avg'));assert.equal(r.rows[0].cells.guest,'J••• T•••');
  });
  test(T+'masks: ID numbers masked in every format for every role; guest names visible with billing',()=>{
    const s=s0();const r=M.runDef(s,'owner',demo(),{},{all:true});
    assert.equal(r.rows[0].cells.id,'XXXX-XXXX-9012');assert.equal(r.rows[0].cells.guest,'Journey Test');
    const csv=M.toCsv(r),txt=M.printText(M.toPrintModel(r,'P'));
    for(const out of [csv,txt,JSON.stringify(M.toXlsx(r,'P'))])assert.ok(!out.includes('5678 9012')&&!out.includes('123456789012'));
    assert.equal(M.maskValue('bankAccount','001234567890'),'XXXXXX7890');
  });
  test(T+'CSV: formula text neutralised; money in rupees; print / Excel / screen three-way equal on the throw-away report',()=>{
    const r=M.runDef(s0(),'owner',demo(),{},{all:true});const csv=M.toCsv(r);
    assert.ok(csv.includes(`"'=HYPERLINK(""x"")"`),csv);assert.ok(csv.includes('1000.01'));
    threeWay(M,assert,r);
  });
  test(T+'parameters: defaults from the business date; From > To, future, over-max, one-day class, months and FYs; legacy flag',()=>{
    const s=s0(),b=(dc,raw)=>M.bindParams({id:'x',params:[],dateClass:dc},raw,s);
    assert.deepEqual([b('D-B',{}).from,b('D-B',{}).to],[s.date,s.date]);
    for(const [dc,raw] of [['D-B',{from:'2026-10-24',to:'2026-10-20'}],['D-B',{from:s.date,to:'2026-10-25'}],['D-DAY',{from:'2026-10-20',to:'2026-10-21'}],
      ['D-S',{from:'2026-01-01',to:'2026-10-24'}],['D-B',{from:'2026-1-1',to:s.date}],['D-M',{month:'2026-13'}],['D-FY',{fy:'2026-28'}]])
      assert.equal(code(()=>b(dc,raw)),'VALIDATION',dc+JSON.stringify(raw));
    assert.equal(b('D-FWD',{from:s.date,to:'2026-10-30'}).to,'2026-10-30');
    assert.deepEqual([b('D-M',{month:'2026-10'}).from,b('D-M',{month:'2026-10'}).to],['2026-10-01',s.date]);
    assert.deepEqual([b('D-M',{month:'2026-09'}).to,b('D-FY',{fy:'2025-26'}).from,b('D-FY',{fy:'2025-26'}).to],['2026-09-30','2025-04-01','2026-03-31']);
    assert.equal(b('D-FY',{}).fy,'2026-27');assert.equal(b('D-FY',{fy:'2025-26'}).legacy,true);assert.equal(b('D-B',{}).legacy,false);
    assert.equal(b('D-ASOF',{}).to,s.date);
    assert.equal(code(()=>M.bindParams({id:'x',params:[{key:'tag',label:'Tag',type:'enum',options:['a']}],dateClass:'D-NOW'},{tag:'z'},s)),'VALIDATION');
    assert.equal(code(()=>M.runReport(s,'owner',ID,{basis:'weekly'})),'VALIDATION');
  });
  test(T+'computed engine: half away from zero both signs; pct to one decimal; unknown function refused',()=>{
    assert.deepEqual([M.rptDivRound(5,2),M.rptDivRound(-5,2),M.rptDivRound(7,3),M.rptDivRound(-7,3),M.rptDivRound(1,0)],[3,-3,2,-2,0]);
    assert.equal(M.evaluateComputed({expr:'pct(a,b)',deps:['a','b']},{a:1,b:3}),33.3);
    assert.equal(M.evaluateComputed({expr:'pct(a,b)',deps:['a','b']},{a:2,b:3}),66.7);
    assert.equal(M.evaluateComputed({expr:'sub(a,b,c)',deps:[]},{a:10,b:3,c:2}),5);
    assert.equal(M.evaluateComputed({expr:'add(a,b)',deps:[]},{a:1,b:null}),null);
    assert.throws(()=>M.evaluateComputed({expr:'formula(a)',deps:[]},{a:1}),/No engine/);
  });
  test(T+'paging (100 desktop) and RPT_TOO_LARGE above 50,000 file rows; print model pages, landscape over 8 columns',()=>{
    const many=n=>demo({select:(s,u,p)=>({params:p,columns:demo().columns,rows:Array.from({length:n},(_,i)=>({kind:'row',cells:{guest:'G'+i,id:null,n:1,amt:i,note:''}}))})});
    const s=s0();const pg=M.runDef(s,'owner',many(250),{},{page:2});assert.equal(pg.rows.length,50);assert.deepEqual(pg.page,{index:2,size:100,total:250});
    const e=(()=>{try{M.runDef(s,'owner',many(50001),{},{all:true});}catch(x){return x;}})();assert.equal(e.code,'VALIDATION');assert.equal(e.rptCode,'RPT_TOO_LARGE');
    const pm=M.toPrintModel(M.runDef(s,'owner',many(100),{},{all:true}),'P');assert.equal(pm.orientation,'portrait');assert.ok(pm.pages.length>=3);
    assert.equal(pm.pages.flatMap(p=>p.tables.flatMap(t=>t.rows)).length,100);
    const h=M.printHtml(pm);assert.ok(h.body.includes(`Page ${pm.pages.length} of ${pm.pages.length}`));assert.ok(h.body.includes('Test basis'));
    assert.equal(M.toPrintModel(M.runReport(s,'owner',ID,{}),'P').orientation,'landscape');
  });
  test(T+'provisional while the business day is open; About sheet carries parameters, basis, run time and user; file name',()=>{
    const s=s0();const r=M.runReport(s,'owner',ID,{});assert.equal(r.about.provisional,true);assert.equal(r.about.runAt,s.clock);assert.equal(r.about.user,'Sagar');
    const old=M.runReport(s,'owner',ID,{from:'2026-10-20',to:'2026-10-21'});assert.equal(old.about.provisional,false);
    const about=M.toXlsx(r,'HCC').at(-1);assert.equal(about.name,'About');const txt=JSON.stringify(about.rows);
    for(const w of ['Date basis','Provisional','Run by','from'])assert.ok(txt.includes(w),w);
    assert.equal(M.reportFileName('HPE',old,'xlsx'),'HPE-rpt.money.revenue-by-category-tender-2026-10-20-2026-10-21.xlsx');
  });
  test(T+'commands: favourites, schedules (money report only to Owner / Manager / Accountant), run due → held while the internet is off, print / export log',()=>{
    let s=s0();
    s=M.execute(s,'accountant',{type:'RPT_FAV_TOGGLE',reportId:ID});assert.deepEqual(s.rptX.favourites.accountant,[ID]);
    s=M.execute(s,'accountant',{type:'RPT_FAV_TOGGLE',reportId:ID});assert.deepEqual(s.rptX.favourites.accountant,[]);
    assert.throws(()=>M.execute(s,'reception',{type:'RPT_FAV_TOGGLE',reportId:ID}),e=>e.code==='FORBIDDEN');
    const sc={type:'RPT_SCHEDULE_SAVE',reportId:ID,params:{range:'yesterday'},format:'xlsx',cadence:{kind:'daily',time:'07:00'},recipients:['owner','accountant']};
    assert.throws(()=>M.execute(s,'manager',{...sc,recipients:['reception']}),e=>e.code==='FORBIDDEN'&&/Owner, Manager or Accountant/.test(e.message));
    assert.throws(()=>M.execute(s,'cashier',sc),e=>e.code==='FORBIDDEN');
    assert.throws(()=>M.execute(s,'manager',{...sc,format:'xml'}),e=>e.code==='VALIDATION');
    s=M.execute(s,'manager',sc);const sid=s.rptX.schedules[0].id;assert.equal(s.rptX.schedules[0].owner,'manager');
    assert.throws(()=>M.execute(s,'accountant',{type:'RPT_SCHEDULE_DELETE',id:sid}),e=>e.code==='FORBIDDEN');
    s=M.execute(s,'manager',{type:'RPT_RUN_DUE'});const run=s.rptX.runs.at(-1);
    assert.equal(run.status,'held');assert.equal(run.params.from,'2026-10-23');assert.ok(run.rows>0);
    assert.ok(s.egressX.outbox.some(x=>x.queue==='scheduled-report'&&x.ref===run.id&&x.status==='held'));
    s=M.execute(s,'manager',{type:'RPT_RUN_DUE'});assert.equal(s.rptX.runs.length,1,'once per business date');
    assert.equal(M.rptScheduleDue({...s.rptX.schedules[0],lastRun:undefined,cadence:{kind:'weekly',day:0,time:'07:00'}},'2026-10-25','08:00'),true);
    assert.equal(M.rptScheduleDue({...s.rptX.schedules[0],lastRun:undefined,cadence:{kind:'monthly',day:1,time:'07:00'}},'2026-10-25','08:00'),false);
    s=M.execute(s,'owner',{type:'RPT_SCHEDULE_DELETE',id:sid});assert.equal(s.rptX.schedules.filter(x=>x.reportId!=='rpt.study.management-pack').length,0,'F12: the run tick also syncs the study pack schedules (meas.study.packSchedule)');
    s=M.execute(s,'accountant',{type:'RPT_LOG',reportId:ID,params:{from:'2026-10-20'},format:'xlsx',rows:12,fileName:'x.xlsx'});
    assert.equal(s.rptX.exportLog.at(-1).by,'accountant');assert.equal(s.rptX.exportLog.at(-1).rows,12);
    assert.throws(()=>M.execute(s,'cashier',{type:'RPT_LOG',reportId:ID,params:{},format:'print',rows:1}),e=>e.code==='FORBIDDEN');
  });
  test(T+'a schedule runs as its owner: when the owner loses the report the run fails, nothing is delivered',()=>{
    let s=s0();s=M.execute(s,'accountant',{type:'RPT_SCHEDULE_SAVE',reportId:ID,params:{},format:'csv',cadence:{kind:'daily',time:'00:00'},recipients:['owner']});
    s=structuredClone(s);s.policy=M.adminDefaultPolicy(s.clock);s.policy.exceptions.push({id:'X3',user:'accountant',perm:'PERM-RPT-001',effect:'deny',reason:'t',expires:'2099-01-01',by:'owner',at:s.clock});
    s=M.execute(s,'owner',{type:'RPT_RUN_DUE'});assert.equal(s.rptX.runs.at(-1).status,'failed');assert.ok(!(s.egressX?.outbox??[]).some(x=>x.queue==='scheduled-report'));
  });
  test(T+'catalogue.json (server cross-check copy) is up to date with the registry',()=>{
    const want=JSON.stringify(M.allReports().map(M.catalogueRecord),null,1)+'\n';
    assert.equal(readFileSync('src/domain/reports/catalogue.json','utf8'),want,'run node tools/rpt-catalogue.mjs');
    assert.equal(readFileSync('../08 Production/tests/Hcc.Reporting.Tests/catalogue.json','utf8'),want,'run node tools/rpt-catalogue.mjs');
  });
  test(T+'goldens: every registered report — screen = Excel = print, and no drift from tests/rpt-golden/goldens',()=>{
    const fails=goldenCheck(M,assert);assert.deepEqual(fails,[]);
  });
}
