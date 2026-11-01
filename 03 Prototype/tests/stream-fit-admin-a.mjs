// FIT-ADMIN-A (wave W2a FIT, DEC-194…196): Master configuration (`settings`) = split view of the 12 groups, Approvals & audit (`approvals`) = three tabs.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-admin-a-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-admin-a.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit-admin-a.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
function ctxFor(uid='owner'){
  const s=F.seed();const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},arrivals:[],departures:[],pending:0,taskList:s.tasks,
    roomStatus:()=>'Vacant',switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},display:{isMobile:false,framed:false,mode:'auto',setMode(){}},
    setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const page=(Comp,pageId,tabKey='',uid='owner')=>withSession(()=>{store.clear();return renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(uid)},
  h(F.FitNavContext.Provider,{value:{desktop:true,page:pageId,tabKey,seq:1,setTabCrumb(){}}},h(Comp))));});
const read=f=>readFileSync(f,'utf8');

export function register({test,assert}){
  test('[FIT-ADMIN-A] settings renders a fit-page root with a SplitView with the group list, search in the toolbar and the first group open',()=>{
    const html=page(F.HotelSettings,'settings');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="settings"/);
    assert.ok(html.includes('aria-label="Configuration groups"')&&html.includes('Property and branding')&&html.includes('Data and system'),'every group listed');
    assert.ok(html.includes('class="cfg-search"')&&html.includes('fit-page-toolbar'),'search is in the toolbar');
    assert.ok(html.includes('class="fit-split has-detail"')&&html.includes('class="cfg-detail"'),'the selected group is open beside the list');
  });
  test('[FIT-ADMIN-A] settings: go(settings,{tab}) opens that group; unknown tab falls back to property; ids match SUB_SCREENS',()=>{
    assert.ok(page(F.HotelSettings,'settings','money').includes('<h2>Money and tax</h2><p>'),'money group open');
    assert.ok(page(F.HotelSettings,'settings','nope').includes('<h2>Property and branding</h2><p>'));
    const src=read('src/features/admin/HotelSettings.tsx');
    const ids=[...src.match(/const GROUP_IDS=\[([^\]]*)\]/)[1].matchAll(/'([^']+)'/g)].map(m=>m[1]);
    const reg=F.SUB_SCREENS.filter(x=>x.page==='settings').map(x=>x.id);
    assert.deepEqual(reg,ids,'12 groups registered in order');assert.equal(ids.length,12);
  });
  test('[FIT-ADMIN-A] approvals renders a fit-page root with three tabs (default Approvals); expenses and audit tabs open by id; ids match SUB_SCREENS',()=>{
    const html=page(F.ApprovalsAudit,'approvals');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="approvals"/);
    for(const id of ['approvals','expenses','audit'])assert.ok(html.includes(`data-tab="${id}"`),id);
    assert.ok(html.includes('data-tab="approvals" aria-selected="true"')&&html.includes('Approvals inbox'),'default tab = approvals');
    assert.ok(page(F.ApprovalsAudit,'approvals','expenses').includes('Pending expenses</h2>'));
    assert.ok(page(F.ApprovalsAudit,'approvals','audit').includes('Audit log</h2>'));
    assert.deepEqual(F.SUB_SCREENS.filter(x=>x.page==='approvals').map(x=>x.id),['approvals','expenses','audit']);
  });
  test('[FIT-ADMIN-A] approvals audit tab: filters sit in a side panel (fit-cols), role without audit permission sees the lock message in the panel',()=>{
    const html=page(F.ApprovalsAudit,'approvals','audit');
    assert.ok(/class="fit-cols".*aria-label="Audit filters".*<div class="adm-filters form-row"><label>Actor/.test(html),'filters in a side panel');
    const denied=page(F.ApprovalsAudit,'approvals','audit','reception');
    assert.ok(denied.includes('Audit view is limited to Owner, Manager, Accountant and Tech admin (view).'),'lock text kept');
    assert.ok(!denied.includes('adm-filters'));
  });
  test('[FIT-ADMIN-A] layout only: no font-size and no hex colour in the lane CSS; Master config still uses MstListsBlock; server device panel kept',()=>{
    const css=read('src/styles/z-tablet-masters.css');const mine=css.slice(css.indexOf('/* FIT-ADMIN-A'));
    assert.ok(!/font-size|#[0-9a-f]{3,6}\b/i.test(mine),'no font size / hex colour');
    const src=read('src/features/admin/HotelSettings.tsx');
    assert.ok(src.includes('<MstListsBlock key={g} g={g} q={q}/>')&&src.includes('<ServerDevicePanel/>'));
  });
}
