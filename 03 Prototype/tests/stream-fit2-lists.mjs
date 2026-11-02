// FIT2-LISTS (wave FIT-2, lane lists; contract §12 "a list scrolls inside its own panel"): Messages, Groups, Guest services, Dashboard, Yield,
// OTA channels, Reservations and the POS day summary give their data the height back - the key figures are a compact StatStrip that starts
// on one line at tab-land (collapseOnTablet), the Groups block list owns the whole width until a block is opened, the Yield notes show their
// full text before any More; every sub-screen id and every text size is unchanged (the stylesheet sets no font size).
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit2-lists-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit2-lists.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit2-lists.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
function ctxFor(s,uid){
  const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'x',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:(p,fn,label,cls)=>h('button',{className:cls},label),search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:[],departures:[],pending:0,taskList:s.tasks,roomStatus:r=>F.roomStatus(s,r),switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},
    display:{isMobile:false,framed:false,mode:'auto',setMode(){}},setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const page=(C,id,tabKey='',uid='owner',s=F.seed())=>withSession(()=>renderToStaticMarkup(
  h(F.FitNavContext.Provider,{value:{desktop:true,page:id,tabKey,seq:1,setTabCrumb(){}}},h(F.HccContext.Provider,{value:ctxFor(s,uid)},h(C)))));
const css=read('src/styles/fit2-lists.css');
const noComments=t=>t.replace(/\/\*[^]*?\*\//g,'');

export function register({test,assert}){
  test('[FIT2-LISTS] the key figures of every list page are a compact, collapsible StatStrip in the page (data below gets the height)',()=>{
    for(const [C,id] of [[F.MsgMessagesPage,'messages'],[F.GroupsPage,'groups'],[F.GuestServicesPage,'guest-services'],[F.Overview,'overview'],[F.YieldPage,'yield'],[F.ChannelsPage,'channels']]){
      store.clear();const html=page(C,id);
      assert.ok(html.includes('fit-stats-wrap compact'),id+': collapsible wrapper, compact');
      assert.ok(html.includes('fit-stats-compact'),id+': compact strip');
      assert.ok(html.includes('fit-stats-toggle'),id+': toggle button');
    }
    store.clear();const pos=page(F.PosPage,'pos','summary');
    assert.ok(pos.includes('fit-stats-wrap compact')&&pos.includes('Outlet sales (incl. tax)'),'POS day summary strip is compact too');
  });
  test('[FIT2-LISTS] sub-screens are unchanged: same tab ids, same defaults, no SUB_SCREENS line added',()=>{
    const tabs=(C,id,k)=>{store.clear();return [...page(C,id,k).matchAll(/data-tab="([^"]+)"/g)].map(m=>m[1]);};
    assert.deepEqual(tabs(F.MsgMessagesPage,'messages'),['outbox','triggers','templates','consent']);
    assert.deepEqual(tabs(F.GroupsPage,'groups'),['blocks','companies','pickup-report','master-folio','limit-requests']);
    assert.deepEqual(tabs(F.GuestServicesPage,'guest-services'),['requests','complaints','lost-found','reports']);
    assert.deepEqual(tabs(F.Overview,'overview'),['arrivals','departures','in-house']);
    assert.deepEqual(tabs(F.YieldPage,'yield').filter(x=>x!=='publish-requests'),['rate-calendar','rate-plans','rules','restrictions','history']);
    assert.deepEqual(tabs(F.ChannelsPage,'channels').slice(0,4),['inbox','channels','push','commission']);
    assert.deepEqual(tabs(F.PosPage,'pos'),['orders','kitchen','summary']);
    assert.ok(!read('src/app/subscreens.ts').includes('FIT2-LISTS'),'this lane adds no sub-screens');
  });
  test('[FIT2-LISTS] groups: the block list owns the whole width until a block is opened (no auto-selected record), all blocks in one panel',()=>{
    store.clear();const html=page(F.GroupsPage,'groups');
    assert.ok(html.includes('fit-split list-only'),'SplitView list-only');
    assert.ok(!html.includes('fit-split-back')&&!html.includes('class="grp-record"'),'no record open by default');
    assert.ok(html.includes('grp-list-row'),'the block rows are there');
    assert.ok(/\.fit-split\.list-only \.grp-list\{display:grid;/.test(noComments(css)),'two or three blocks per row when the list is alone');
  });
  test('[FIT2-LISTS] yield: legend and notes show their full text (two lines) before any measured More',()=>{
    store.clear();const html=page(F.YieldPage,'yield');
    assert.ok(html.includes('-webkit-line-clamp:2'),'two-line notes');
    assert.ok(html.includes('Legend: maroon = above base'),'the legend text is in the markup');
    assert.ok(!html.includes('fit-note-more'),'no More button before the measure (server render)');
  });
  test('[FIT2-LISTS] stylesheet: layout only, no font size, desktop shell only',()=>{
    const c=noComments(css);
    assert.ok(!/font-size|font:|line-height/.test(c),'DEC-195: no text size in fit2-lists.css');
    for(const rule of c.split('}').map(x=>x.trim()).filter(x=>x&&!x.startsWith('@')))assert.ok(rule.startsWith('.app-shell'),rule);
  });
}
