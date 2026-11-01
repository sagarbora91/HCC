// FIT-REVENUE (wave W2a FIT, DEC-194…196, lane fit-revenue): yield, groups, channels and booking-site render a `fit-page` root with a FitTabs bar
// whose ids equal their SUB_SCREENS lines; default tabs are as set; the tariff grid keeps a sticky first column; the groups record pane holds
// the billing; layout CSS is desktop only (.app-shell) and sets no font size.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-revenue-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-revenue.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit-revenue.mjs');
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
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:[],departures:[],pending:0,taskList:s.tasks,roomStatus:r=>F.roomStatus(s,r),switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},
    display:{isMobile:false,framed:false,mode:'auto',setMode(){}},setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const page=(C,id,tabKey='',uid='owner',s=F.seed())=>withSession(()=>renderToStaticMarkup(
  h(F.FitNavContext.Provider,{value:{desktop:true,page:id,tabKey,seq:1,setTabCrumb(){}}},h(F.HccContext.Provider,{value:ctxFor(s,uid)},h(C)))));
const tabIds=html=>[...html.matchAll(/data-tab="([^"]+)"/g)].map(m=>m[1]);
const subs=pg=>F.SUB_SCREENS.filter(x=>x.page===pg).map(x=>x.id);

export function register({test,assert}){
  test('[FIT-REVENUE] yield: fit-page root, tabs = SUB_SCREENS (publish-requests only while a request exists), default Rate calendar, StatStrip in the toolbar',()=>{
    store.clear();const html=page(F.YieldPage,'yield');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="yield"/);
    assert.ok(html.includes('<h1 id="fit-page-h1">Tariff &amp; yield</h1>'));
    assert.deepEqual(tabIds(html).filter(x=>x!=='publish-requests'),['rate-calendar','rate-plans','rules','restrictions','history']);
    assert.ok(subs('yield').includes('publish-requests')&&subs('yield').length===6);
    assert.match(html,/data-tab="rate-calendar"[^>]*aria-selected="true"/,'default tab unchanged: Rate calendar');
    assert.ok(html.includes('fit-stats')&&html.includes('Avg suggested BAR'),'stats are a StatStrip in the toolbar');
    assert.ok(html.includes('<table class="yld-cal">'),'the tariff grid is in the default tab');
    assert.ok(!html.includes('class="small-stats'),'old stats block gone');
    for(const [id,needle] of [['rate-plans','Rate sheet'],['rules','Guardrails'],['restrictions','Stay restrictions'],['history','Publish log']])
      assert.ok(page(F.YieldPage,'yield',id).includes(needle),id);
  });
  test('[FIT-REVENUE] groups: fit-page split on Blocks, tab ids = SUB_SCREENS, every old tab reachable, Companies keeps the credit note',()=>{
    store.clear();const html=page(F.GroupsPage,'groups');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="groups"/,'FitPage layout stays fill: the page-root class fit-split would collide with the SplitView rule .fit-split (grid)');
    assert.deepEqual(tabIds(html),['blocks','companies','pickup-report','master-folio','limit-requests']);
    assert.deepEqual(subs('groups'),['blocks','companies','pickup-report','master-folio','limit-requests']);
    assert.match(html,/data-tab="blocks"[^>]*aria-selected="true"/,'default tab unchanged: Blocks');
    assert.ok(html.includes('class="fit-split')&&html.includes('Group blocks'),'blocks = SplitView');
    const co=page(F.GroupsPage,'groups','companies');assert.match(co,/^<section class="fit-page fit-fill"/);assert.ok(co.includes('Credit limit')&&co.includes('Credit limits are enforced'));
    assert.ok(page(F.GroupsPage,'groups','pickup-report').includes('Pickup report'));
    assert.ok(page(F.GroupsPage,'groups','master-folio').includes('Master folio'));
    assert.ok(page(F.GroupsPage,'groups','limit-requests').includes('Limit requests'));
  });
  test('[FIT-REVENUE] groups: the record pane holds the block card, rooming-list action and billing (master folio); the list row opens it',()=>{
    const src=read('src/features/groups/Groups.tsx');
    assert.ok(src.includes('<BlockCard blk={blk} showFolio={showFolio}/>')&&src.includes('<MasterFolio blk={blk}/>')&&src.includes("open({type:'grp-rooming'"),'record = card + billing; rooming list opens from the card');
    assert.ok(src.includes('SplitView')&&src.includes('usePaged(blocks,DESKTOP_PAGE)'),'split + paged list');
    assert.ok(src.includes("setTab('master-folio')"),'Master folio button still switches tab');
  });
  test('[FIT-REVENUE] channels: fit-page root, default Inbox, tabs follow the role (Payouts for the books roles), ids ⊂ SUB_SCREENS',()=>{
    store.clear();const owner=page(F.ChannelsPage,'channels');
    assert.match(owner,/^<section class="fit-page fit-fill" data-fit-page="channels"/);
    assert.deepEqual(tabIds(owner),['inbox','channels','push','commission','payouts']);
    assert.match(owner,/data-tab="inbox"[^>]*aria-selected="true"/,'default tab unchanged: Inbox');
    assert.ok(!tabIds(page(F.ChannelsPage,'channels','','reception')).includes('payouts'));
    const ids=subs('channels');for(const id of tabIds(owner))assert.ok(ids.includes(id),id);
    assert.ok(page(F.ChannelsPage,'channels','push').includes('Push now')&&page(F.ChannelsPage,'channels','commission').includes('Print view'));
    assert.ok(page(F.ChannelsPage,'channels','channels').includes('chn-channel'));
  });
  test('[FIT-REVENUE] booking-site: fit-page root, Bookings (default) and Preview tabs = SUB_SCREENS, requests paged with ShowMore',()=>{
    store.clear();const html=page(F.BookingSitePage,'booking-site');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="booking-site"/);
    assert.deepEqual(tabIds(html),['bookings','preview']);assert.deepEqual(subs('booking-site'),['bookings','preview']);
    assert.match(html,/data-tab="bookings"[^>]*aria-selected="true"/);
    const pv=page(F.BookingSitePage,'booking-site','preview');assert.ok(pv.includes('chn-device')&&pv.includes('Guest booking page preview'));
    const src=read('src/features/channels/BookingSite.tsx');assert.ok(src.includes('usePaged(all,DESKTOP_PAGE)')&&src.includes('<ShowMore p={pg}/>'));
  });
  test('[FIT-REVENUE] css: lane rules are desktop only (.app-shell), no font-size, tariff grid first column sticky, no hex colour',()=>{
    const css=read('src/styles/fit-revenue.css');
    const rules=css.replace(/\/\*[^]*?\*\//g,'').split('}').map(x=>x.trim()).filter(Boolean);
    for(const r of rules)assert.match(r,/^\.app-shell /,'guarded: '+r.slice(0,50));
    assert.ok(!/font-size|font:/.test(css),'DEC-195');assert.ok(!/#[0-9a-f]{3,8}\b/i.test(css),'tokens only');
    assert.ok(css.includes('.app-shell .yld-cal tbody th{position:sticky;left:0'),'sticky first column');
  });
}
