// F7-R2 · Reports hub: role filter, money flag (DEC-061), search / grouping / favourites, parameter defaults, schedule text, page registration, phone rules.
// Names prefixed [F7R2]. The browser checks (fit on the hub, phone at 360 px) are tools/fit-screen-check.mjs and the phone smoke, run by hand on the PC.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/rpt-hub-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rpt-hub.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const H=await import('./.generated/rpt-hub.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;const h=React.createElement;
const src=f=>readFileSync(f,'utf8');

const col=(key,money=false)=>({key,label:key,type:money?'money':'text',money});
const def=(id,o={})=>({id,title:o.title??id,area:o.area??'money',priority:o.priority??'P1',status:'built',moneyFlag:!!o.moneyFlag,viewPerms:['PERM-RPT-001'],params:o.params??[{key:'range',label:'Range',type:'range',dateClass:'D-B'}],
  columns:[col('a'),col('m',true)],dateBasis:'Business date',formats:['screen','xlsx','csv'],phone:o.phone??'yes',legacyIds:o.legacyIds??[],select:()=>({params:{},columns:[],rows:[]})});
const DEFS=[def('rpt.money.collections',{title:'Collection detail',moneyFlag:true,legacyIds:['A12']}),def('rpt.rooms.room-status',{title:'Room status',area:'rooms'}),
  def('rpt.guests.guest-register',{title:'Guest register',area:'guests'}),def('rpt.money.wide',{title:'Wide ledger',phone:'no'})];

function ctxFor(isMobile,uid){
  const s=H.seed();const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,page:'report-hub',go(){},open(){},toast(){},run:async()=>true,permission:p=>H.can(s,uid,p),display:{isMobile,framed:false,mode:'auto',setMode(){}},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const nav={desktop:true,page:'report-hub',tabKey:'',seq:1,setTabCrumb(){}};
const render=(c,el)=>renderToStaticMarkup(h(H.HccContext.Provider,{value:c},h(H.FitNavContext.Provider,{value:nav},el)));

export function register({test,assert}){
  const s0=H.seed();
  const roleUid=r=>s0.users.find(u=>u.role===r)?.id;
  test('[F7R2] hubDefs: money reports are removed for Housekeeper, Maintenance and Supervisor, kept for the money roles; non-money stay',()=>{
    for(const r of ['Housekeeper','Maintenance','Supervisor']){const u=roleUid(r);if(!u)continue;
      const ids=H.hubDefs(s0,u,DEFS,()=>true).map(d=>d.id);assert.ok(!ids.includes('rpt.money.collections'),r+' must not see money');assert.ok(ids.includes('rpt.rooms.room-status'),r+' keeps non-money');}
    for(const r of ['Owner','Manager','Accountant','Cashier','Reception']){const u=roleUid(r);if(!u)continue;assert.ok(H.hubDefs(s0,u,DEFS,()=>true).some(d=>d.moneyFlag),r);}
    assert.ok(H.NO_MONEY_ROLES.includes('Housekeeper')&&H.NO_MONEY_ROLES.includes('Maintenance'));
  });
  test('[F7R2] hubDefs applies the engine view right first (a report the engine refuses never appears)',()=>{
    const owner=roleUid('Owner');assert.deepEqual(H.hubDefs(s0,owner,DEFS,d=>d.area==='rooms').map(d=>d.id),['rpt.rooms.room-status']);
    assert.deepEqual(H.hubDefs(s0,'nobody',DEFS,()=>true).length,0,'an unknown or disabled user sees nothing');
  });
  test('[F7R2] search matches title, id, category and legacy ids; category and favourites filters; phone hides phone:no',()=>{
    const f=(o)=>H.filterHub(DEFS,{query:'',area:'all',favOnly:false,favs:[],...o}).map(d=>d.id);
    assert.deepEqual(f({query:'collection'}),['rpt.money.collections']);assert.deepEqual(f({query:'a12'}),['rpt.money.collections']);
    assert.deepEqual(f({query:'ROOMS'}),['rpt.rooms.room-status']);assert.deepEqual(f({area:'guests'}),['rpt.guests.guest-register']);
    assert.deepEqual(f({favOnly:true,favs:['rpt.guests.guest-register']}),['rpt.guests.guest-register']);
    assert.ok(f({}).includes('rpt.money.wide')&&!f({phone:true}).includes('rpt.money.wide'));
  });
  test('[F7R2] groups follow the catalogue category order and drop empty ones',()=>{
    const g=H.groupByArea(DEFS);assert.deepEqual(g.map(x=>x.area),['guests','rooms','money']);assert.equal(g[2].defs.length,2);
  });
  test('[F7R2] default parameters come from the business date, never the wall clock; file names follow <code>-<id>-<from>-<to>',()=>{
    const p=H.defaultParams([{key:'range',label:'R',type:'range'},{key:'d',label:'D',type:'date'},{key:'m',label:'M',type:'month'},{key:'e',label:'E',type:'enum',options:['x','y']},{key:'b',label:'B',type:'bool'}],'2026-10-27');
    assert.deepEqual(p,{from:'2026-10-01',to:'2026-10-27',d:'2026-10-27',m:'2026-10',e:'x',b:'false'});
    assert.equal(H.exportFileName('HPE','rpt.money.x',{from:'2026-10-01',to:'2026-10-27'},'xlsx'),'HPE-rpt.money.x-2026-10-01-2026-10-27.xlsx');
  });
  test('[F7R2] schedule text, recipients (money reports only to Owner / Manager / Accountant), visibility of schedules and the export log',()=>{
    assert.equal(H.cadenceText({kind:'daily',time:'07:00'}),'Every day at 07:00');assert.equal(H.cadenceText({kind:'weekly',day:1,time:'08:30'}),'Every Monday at 08:30');
    assert.equal(H.cadenceText({kind:'monthly',day:5,time:'09:00'}),'Day 5 of each month at 09:00');
    const money=H.recipientChoices(s0,DEFS[0]).map(x=>x.role);assert.ok(money.length&&money.every(r=>H.MONEY_RECIPIENT_ROLES.includes(r)));
    assert.ok(H.recipientChoices(s0,DEFS[1]).length>=money.length);
    const owner=roleUid('Owner'),mgr=roleUid('Manager');
    const s={...s0,rptX:{favourites:{[owner]:['a']},schedules:[{id:'1',reportId:'x',params:{},format:'xlsx',cadence:{kind:'daily',time:'07:00'},owner:mgr,recipients:[mgr],enabled:true}],runs:[],
      exportLog:[{id:'e1',at:'2026-10-27T10:00:00',by:mgr,reportId:'x',params:{},format:'xlsx',rows:3,provisional:false},{id:'e2',at:'2026-10-27T11:00:00',by:'zz',reportId:'x',params:{},format:'csv',rows:1,provisional:false}]}};
    assert.equal(H.visibleSchedules(s,owner).length,1);assert.equal(H.visibleSchedules(s,roleUid('Accountant')).length,0);
    assert.equal(H.visibleExportLog(s,owner)[0].id,'e2');assert.equal(H.visibleExportLog(s,roleUid('Cashier')).length,0);
    assert.deepEqual(H.favsOf(s,owner),['a']);assert.deepEqual(H.favsOf(s0,owner),[]);
  });
  test('[F7R2] page registration: PAGE_CATEGORY, SUB_SCREENS (3 tabs = HUB_TABS), nav item, phone page, blurb',()=>{
    assert.equal(H.PAGE_CATEGORY['report-hub'],'accounts');
    const subs=H.SUB_SCREENS.filter(x=>x.page==='report-hub').map(x=>x.id);assert.deepEqual(subs,H.HUB_TABS.map(t=>t.id));
    assert.ok(H.registration.pages['report-hub']&&H.registration.mobile.pages['report-hub']);assert.equal(H.registration.nav[0].item.id,'report-hub');
    assert.match(src('src/app/navmap.ts'),/'report-hub':'Every report/);
  });
  test('[F7R2] the hub renders on the real catalogue with the FitPage root and tabs (owner and reception), and lists their reports',()=>{
    for(const u of ['owner','reception']){
      const html=render(ctxFor(false,u),h(H.ReportsHub));
      assert.match(html,/class="fit-page fit-split"/,u);assert.ok(html.includes('role="tablist"'),u);assert.ok(html.includes('Scheduled runs')&&html.includes('Export log'),u);
      assert.ok(!html.includes('No reports available'),u+' has reports on the real engine catalogue');
    }
    assert.ok(render(ctxFor(false,'owner'),h(H.ReportsHub)).includes('Revenue by room category and tender'));
    assert.ok(render(ctxFor(false,'reception'),h(H.ReportsHub)).includes('Room status today'));
  });
  test('[F7R2] a role that may open no report gets the honest empty state (tech admin; F7-R5: the Cashier now sees the tax reports through PERM-INV-001), not a blank pane',()=>{
    for(const u of ['techadmin']){
      assert.equal(H.reportCatalogue(s0,u).length,0,u+' sees no report in the engine catalogue');
      const html=render(ctxFor(false,u),h(H.ReportsHub));
      assert.match(html,/class="fit-page fit-split"/,u);assert.ok(html.includes('No reports available'),u);
    }
  });
  test('[F7R2] the phone hub renders cards and chips, no table, no fit- class',()=>{
    const html=render(ctxFor(true,'owner'),h(H.ReportsHubMobile));
    assert.ok(html.includes('Search reports')&&html.includes('rpt-m'));assert.ok(!html.includes('<table')&&!/fit-/.test(html));
  });
  test('[F7R2] the viewer shows the engine error instead of figures when the report cannot run (unknown report id)',()=>{
    const html=render(ctxFor(false,'owner'),h(H.ReportView,{def:def('rpt.rooms.not-a-report',{title:'Not a report',area:'rooms'})}));
    assert.ok(html.includes('Cannot show this report')&&html.includes('No report rpt.rooms.not-a-report'),'the engine NOT_FOUND message is shown');
    assert.ok(!html.includes('<table')&&!html.includes('rpt-cards'),'no figures');assert.ok(!/Export Excel<\/button>/.test(html.replace(/<button[^>]*disabled=""[^>]*>.*?<\/button>/g,'')),'every export button is disabled');assert.ok(html.includes('Print'));
  });
  test('[F7R2] the viewer runs a real engine report for the owner (no error pane)',()=>{
    const html=render(ctxFor(false,'owner'),h(H.ReportView,{def:H.reportCatalogue(s0,'owner').find(d=>d.id==='rpt.rooms.room-status')}));
    assert.ok(!html.includes('Cannot show this report'));
  });
  test('[F7R2] stylesheet: tokens only (no hex colour), phone card rules, 48 px export buttons',()=>{
    const css=src('src/styles/rpt-hub.css');assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(css.replace(/\/\*[^]*?\*\//g,'')),'no hex colour');
    assert.match(css,/\.rpt-export-m \.btn\{[^}]*min-height:48px/);assert.match(css,/\.rpt-card\{[^}]*overflow-wrap:anywhere/);
  });
}
