// FIT-OPS (wave W2a FIT, lane fit-ops, DEC-194…196, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §6): housekeeping, maintenance, handover and
// inventory render a fit-page root, their sub-screens match SUB_SCREENS, the default tabs are as set, no money reaches the housekeeping / maintenance
// screens of floor roles (DEC-061), the handover run keeps its sign-off in the panel footer, and the lane CSS is desktop-only and font-free.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-ops-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-ops.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'},
  banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
const F=await import('./.generated/fit-ops.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const PAGES={housekeeping:F.Housekeeping,maintenance:F.MaintenancePage,handover:F.HandoverPage,inventory:F.InventoryPage};
function render(page,uid,tabKey=''){
  const s=F.seed();const user=s.users.find(u=>u.id===uid);
  const ctx={s,uid,user,m:F.metrics(s),page,go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},arrivals:[],departures:[],pending:0,
    taskList:s.tasks.filter(t=>F.can(s,uid,'inspect')||t.assignee===uid),roomStatus:()=>'Vacant',display:{isMobile:false,framed:false,mode:'auto',setMode(){}},
    setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
  const nav={desktop:true,page,tabKey,seq:1,setTabCrumb(){}};
  return withSession(()=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctx},h(F.FitNavContext.Provider,{value:nav},h(PAGES[page])))));
}
const ROLES={housekeeping:['owner','supervisor','housekeeper','maint'],maintenance:['owner','manager','maint','housekeeper'],handover:['owner','reception','housekeeper','maint'],inventory:['owner','supervisor','housekeeper']};

export function register({test,assert}){
  for(const [page,roles] of Object.entries(ROLES))for(const uid of roles){
    test(`[FIT-OPS] ${page} (${uid}) renders one fit-page root with data-fit-page`,()=>{
      store.clear();const html=render(page,uid);
      assert.match(html,new RegExp(`^<section class="fit-page fit-[a-z]+" data-fit-page="${page}"`),html.slice(0,120));
      assert.ok(!html.includes('class="page-title"'),'no old PageTitle inside the FitPage');
    });
  }
  test('[FIT-OPS] housekeeping is a board for supervisors (4 status columns + attendant load, each scrolling) and a filled list for the housekeeper',()=>{
    store.clear();const sup=render('housekeeping','supervisor');
    assert.match(sup,/^<section class="fit-page fit-board"/);
    for(const c of ['To do','In progress','Awaiting inspection','Done','Attendant load'])assert.ok(sup.includes(`<h2>${c}</h2>`),c);
    assert.equal((sup.match(/class="card fit-board-col"/g)||[]).length,5);
    assert.ok(sup.includes('aria-label="Filter tasks"')&&sup.includes('class="fit-stats"'),'filters + StatStrip in the toolbar');
    const hk=render('housekeeping','housekeeper');
    assert.match(hk,/^<section class="fit-page fit-fill"/);assert.ok(hk.includes('<h1 id="fit-page-h1">My tasks</h1>')&&hk.includes('aria-label="My tasks"'));
  });
  test('[FIT-OPS] maintenance: tabs keep the old ids, default Work orders, go(page,{tab}) / session opens another; technician and housekeeper views fit',()=>{
    store.clear();const d=render('maintenance','owner');
    for(const id of ['orders','pm','assets','spares','vendors','reports'])assert.ok(d.includes(`data-tab="${id}"`),id);
    assert.ok(/data-tab="orders"[^>]*aria-selected="true"/.test(d),'default tab Work orders (was Work orders)');
    assert.ok(d.includes('aria-label="Work orders"'));
    const pm=render('maintenance','owner','pm');assert.ok(/data-tab="pm"[^>]*aria-selected="true"/.test(pm)&&pm.includes('PM schedule'));
    store.clear();store.set('hcc-fit-tab:maintenance','vendors');const v=render('maintenance','owner');assert.ok(v.includes('AMC contracts')&&v.includes('class="fit-cols fit-cols-even"'));
    store.clear();const rep=render('maintenance','owner','reports');assert.ok(rep.includes('class="fit-ops-grid"')&&rep.includes('Mean time to repair'));
    store.clear();assert.ok(render('maintenance','maint').includes('<h1 id="fit-page-h1">My work orders</h1>'));
    assert.ok(render('maintenance','housekeeper').includes('aria-label="Open repairs on your rooms"'));
  });
  test('[FIT-OPS] handover: tabs checklists / handover / dashboard / templates, default My checklists; a run is a StepFlow with the sign-off in the panel footer',()=>{
    store.clear();const d=render('handover','owner');
    assert.ok(/data-tab="checklists"[^>]*aria-selected="true"/.test(d)&&d.includes('data-tab="templates"'));
    assert.ok(d.includes('class="fit-steps"')&&d.includes('aria-label="Steps"'));
    const foot=d.slice(d.indexOf('class="fit-panel-foot"'));
    assert.ok(/Submit checklist|Submitted by|Reopen/.test(foot),'sign-off lives in .fit-panel-foot, outside the scrolling body');
    const body=d.slice(d.indexOf('class="fit-panel-body"'),d.indexOf('class="fit-panel-foot"'));
    assert.ok(!body.includes('Submit checklist'),'the sign-off button is not inside the scrolling body');
    const tpl=render('handover','owner','templates');assert.ok(tpl.includes('aria-label="Checklist templates"')&&tpl.includes('New template'));
    const dash=render('handover','owner','dashboard');assert.ok(dash.includes('Overdue items')&&dash.includes('class="fit-stats"'));
    const hn=render('handover','owner','handover');assert.ok(hn.includes('Write handover note')&&hn.includes('Open carry-forward')&&!hn.includes('modal-actions'),'no .modal-actions inside a page body');
    store.clear();assert.ok(!render('handover','housekeeper','templates').includes('Checklist templates'),'a role without the Templates tab falls back to My checklists');
  });
  test('[FIT-OPS] inventory: seven tabs with kebab ids, default Stock, alerts beside the stock table, money only for money roles',()=>{
    store.clear();const d=render('inventory','owner');
    for(const id of ['stock','laundry','counts','losses','consumption','par-minimums','movements'])assert.ok(d.includes(`data-tab="${id}"`),id);
    assert.ok(/data-tab="stock"[^>]*aria-selected="true"/.test(d)&&d.includes('aria-label="Stock"'));
    assert.ok(d.includes('Reorder alerts')&&d.includes('class="fit-cols"'),'the reorder alerts are the side panel of the Stock tab');
    for(const t of ['laundry','counts','losses','consumption','par-minimums','movements'])assert.ok(render('inventory','owner',t).includes('fit-panel-body'),t);
    store.clear();assert.ok(!render('inventory','housekeeper').includes('₹'),'DEC-061: no rupee amount on the housekeeper stock page');
  });
  test('[FIT-OPS] DEC-061: no hotel money on the housekeeping or maintenance screens of floor roles',()=>{
    store.clear();
    for(const [page,uid] of [['housekeeping','housekeeper'],['housekeeping','maint'],['maintenance','maint'],['maintenance','housekeeper'],['inventory','housekeeper']])
      assert.ok(!render(page,uid).includes('₹'),`${page} as ${uid} shows a rupee amount`);
  });
  test('[FIT-OPS] sub-screen registry: the FIT-OPS block lists exactly the maintenance, handover and inventory tab ids',()=>{
    const mine=F.SUB_SCREENS.filter(x=>['maintenance','handover','inventory','housekeeping'].includes(x.page));
    const ids=p=>mine.filter(x=>x.page===p).map(x=>x.id);
    assert.deepEqual(ids('maintenance'),['orders','pm','assets','spares','vendors','reports']);
    assert.deepEqual(ids('handover'),['checklists','handover','dashboard','templates']);
    assert.deepEqual(ids('inventory'),['stock','laundry','counts','losses','consumption','par-minimums','movements']);
    assert.deepEqual(ids('housekeeping'),[]);
    const src=read('src/app/subscreens.ts');assert.ok(src.indexOf('// FIT-OPS')>src.indexOf('// FIT-CASH')&&src.indexOf('// FIT-OPS')<src.indexOf('// FIT-GUEST'),'inside the FIT-OPS block');
  });
  test('[FIT-OPS] lane CSS: desktop-only (.app-shell / media), no font-size, no hex colour',()=>{
    const block=read('src/styles/fit-ops.css').replace(/\/\*[^]*?\*\//g,'');
    assert.ok(block.length>200);
    assert.ok(!/font-size/.test(block),'DEC-195: no font size');
    assert.ok(!/#[0-9a-f]{3,8}\b/i.test(block),'colours are tokens');
    const rules=block.split('\n').map(l=>l.trim()).filter(l=>l&&!l.startsWith('@media')&&l!=='}');
    for(const r of rules)assert.ok(r.startsWith('.app-shell'),'unguarded rule: '+r.slice(0,70));
  });
}
