// FIT-PEOPLE-B (wave W2a FIT, DEC-194…196, APR-025, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §6): the lane's desktop pages
// accounting (FitTabs over the existing sections), reports (split: report list left, report right with the exports in its panel header),
// plans-extras (FitTabs per section) and assistant (chat) render a fit-page root; their useFitTab ids equal their SUB_SCREENS lines; the
// default tabs are unchanged; role rules (Tally export Owner + Accountant, report exports not for the Cashier) are unchanged; the phone keeps
// its markup; the lane's CSS applies under .app-shell only.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-people-b-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-people-b.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"','process.env.NODE_ENV':'"production"'}});
const F=await import('./.generated/fit-people-b.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const nav=(page,tabKey='')=>({desktop:true,page,tabKey,seq:1,setTabCrumb(){}});
function ctxFor(uid='owner',isMobile=false){
  const s=F.seed();const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:[],departures:[],pending:0,taskList:s.tasks,roomStatus:()=>'Vacant',
    switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},display:{isMobile,framed:false,mode:'auto',setMode(){}},
    setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
/** Render a page on the desktop layout (FitNavContext desktop) or, with phone=true, without the desktop shell. */
const render=(Page,page,{uid='owner',tabKey='',phone=false}={})=>withSession(()=>{store.clear();
  const el=h(F.HccContext.Provider,{value:ctxFor(uid,phone)},h(Page));
  return renderToStaticMarkup(phone?el:h(F.FitNavContext.Provider,{value:nav(page,tabKey)},el));});
const subsOf=page=>F.SUB_SCREENS.filter(x=>x.page===page).map(x=>x.id);
const selected=html=>(html.match(/role="tab" id="fit-tab-[^"]+" data-tab="([^"]+)" aria-selected="true"/)||[])[1];
const tabIds=html=>[...html.matchAll(/role="tab" id="fit-tab-[^"]+" data-tab="([^"]+)"/g)].map(m=>m[1]);

export function register({test,assert}){
  test('[FIT-PEOPLE-B] every lane page renders a fit-page root with data-fit-page on the desktop layout',()=>{
    for(const [Page,page,layout] of [[F.AccountingPage,'accounting','fill'],[F.Reports,'reports','split'],[F.PlansExtrasPage,'plans-extras','fill'],[F.AssistantPage,'assistant','fill']]){
      const html=render(Page,page);
      assert.match(html,new RegExp(`^<section class="fit-page fit-${layout}" data-fit-page="${page}"`),page);
      assert.ok(html.includes('<h1 id="fit-page-h1">'),page+' h1 in the FitPage header');
      assert.ok(!html.includes('class="page-title"><div'),page+' no old PageTitle');
    }
  });
  test('[FIT-PEOPLE-B] accounting: useFitTab ids = the existing section ids = SUB_SCREENS; default tab Vouchers; go(accounting,{tab}) opens a section',()=>{
    assert.deepEqual([...F.ACC_TAB_IDS],subsOf('accounting'));
    const html=render(F.AccountingPage,'accounting');
    assert.equal(selected(html),'vouchers','default tab unchanged');
    assert.deepEqual(tabIds(html),[...F.ACC_TAB_IDS],'owner sees every section incl. Tally export, in the old order');
    for(const t of ['Vouchers','Trial balance','Statements','Day book','Chart of accounts','Parties','Opening balances','Exceptions','Reconciliation','Mapping','Tally export','P&amp;L','Balance sheet','GST summary'])assert.ok(html.includes('>'+t+'<'),t);
    const tb=render(F.AccountingPage,'accounting',{tabKey:'tb'});assert.equal(selected(tb),'tb');assert.match(tb,/Control ledgers/);
    assert.match(html,/class="fit-page-actions"><button class="btn primary"[^>]*><svg[^]*?Lock period/,'Lock period is the first header action');
    assert.ok(html.includes('class="fit-stats"')&&html.includes('Total debits')&&html.includes('id="acc-lock-why"')===html.includes('aria-describedby="acc-lock-why"'),'StatStrip in the toolbar; the lock reason keeps its id');
    assert.ok(/class="fit-panel-body" data-fit-scroll="" tabindex="0" aria-label="Vouchers — accounting"/.test(html),'section content in one ScrollPanel');
  });
  test('[FIT-PEOPLE-B] accounting: role rules unchanged — Manager has no Tally export (an asked or remembered tally tab falls back to Vouchers); cashier/reception/housekeeper: not available',()=>{
    const m=render(F.AccountingPage,'accounting',{uid:'manager',tabKey:'tally'});
    assert.ok(!tabIds(m).includes('tally')&&!m.includes('Tally export'));assert.equal(selected(m),'vouchers');
    assert.ok(tabIds(render(F.AccountingPage,'accounting',{uid:'accountant'})).includes('tally'));
    for(const u of ['cashier','reception','housekeeper']){const x=render(F.AccountingPage,'accounting',{uid:u});
      assert.match(x,/^<section class="fit-page fit-fill"/,u);assert.match(x,/Not available for your role/,u);assert.ok(!x.includes('role="tab"'),u);}
  });
  test('[FIT-PEOPLE-B] plans-extras: three sections as FitTabs (= SUB_SCREENS), default Service charges, CSV in the header; the phone keeps the old stacked cards',()=>{
    const ids=['service-charges','stays-by-meal-plan','discounts-by-reason'];
    assert.deepEqual(subsOf('plans-extras'),ids);
    const html=render(F.PlansExtrasPage,'plans-extras');
    assert.deepEqual(tabIds(html),ids);assert.equal(selected(html),'service-charges');
    assert.ok(html.includes('Service charges · catalogue vs free text')&&html.includes('From the catalogue'));
    assert.match(html,/class="fit-page-actions"><button class="btn"[^>]*><svg[^]*?CSV/);
    assert.match(render(F.PlansExtrasPage,'plans-extras',{tabKey:'stays-by-meal-plan'}),/GST treatment of meal plans/);
    const phone=render(F.PlansExtrasPage,'plans-extras',{phone:true});
    assert.ok(phone.startsWith('<div class="cat-page">')&&!phone.includes('fit-')&&phone.includes('Discounts by reason')&&phone.includes('Stays by meal plan'),'phone markup unchanged');
  });
  test('[FIT-PEOPLE-B] reports: split — grouped report list left, report right with the exports in its panel header; Cashier gets no exports; no sub-screens',()=>{
    assert.deepEqual(subsOf('reports'),[]);
    const html=render(F.Reports,'reports');
    assert.ok(html.includes('class="fit-split has-detail"'),'a report is open by default (Daily operations)');
    for(const t of ['Daily operations','Revenue &amp; tax','Receipts &amp; deposits','Expenses &amp; cash','Occupancy','Report dictionary'])assert.ok(html.includes('>'+t+'</button>'),t);
    assert.match(html,/class="f-report-item on" aria-current="page">Daily operations</);
    assert.match(html,/<div class="section-title fit-panel-head"><h2 id="[^"]+">Daily operations<\/h2><div class="inline fit-panel-actions"><button class="btn"><svg[^]*?Export CSV[^]*?Export Excel[^]*?Print/,'exports in the report panel header');
    assert.ok(html.includes('aria-label="Report filters"')&&html.includes('From (business date)'),'filters in the toolbar');
    const c=render(F.Reports,'reports',{uid:'cashier'});assert.ok(!/Export CSV|Export Excel|> Print/.test(c),'Cashier: no exports (RPT-003)');
  });
  test('[FIT-PEOPLE-B] assistant: chat — conversation ScrollPanel with the thread, question box pinned in its footer, side panel with suggestions, briefing and flags',()=>{
    const html=render(F.AssistantPage,'assistant');
    assert.match(html,/aria-label="Assistant conversation"><div class="ai-thread"/,'thread is the conversation panel body');
    assert.match(html,/<div class="fit-panel-foot"><div class="ai-fit-compose"><form class="ai-input"/,'input pinned in the footer');
    assert.match(html,/aria-label="Suggestions, daily briefing and anomaly flags"><div class="ai-fit-side"><div class="ai-chips"[^]*Daily briefing[^]*Anomaly flags/);
    assert.ok(html.includes('class="fit-cols ai-fit-cols"'));
  });
  test('[FIT-PEOPLE-B] lane CSS: every added rule is scoped to .app-shell (desktop) and sets no font size',()=>{
    const added=[['src/styles/m-accounting.css','/* FIT (DEC-194, lane fit-people-b)'],['src/styles/m-assistant.css','/* FIT (DEC-194, lane fit-people-b)'],['src/styles/m-catalogue.css','/* FIT (DEC-194, lane fit-people-b)'],['src/styles/reports.css','/* FIT (DEC-194, lane fit-people-b)']];
    for(const [f,mark] of added){const t=read(f);const i=t.indexOf(mark);assert.ok(i>=0,f);
      const rules=t.slice(i).replace(/\/\*[^]*?\*\//g,'').replace(/@media print\{[^{}]*\{[^}]*\}\}/g,'').split('}').map(x=>x.trim()).filter(Boolean);
      for(const r of rules){const sel=r.split('{')[0];for(const part of sel.split(','))assert.ok(part.trim().startsWith('.app-shell '),f+': '+part);}
      assert.ok(!/font-size/.test(t.slice(i)),f+' sets no font size');}
  });
}
