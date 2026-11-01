// FIT-FRONT-A (wave W2a FIT, DEC-194…196): Dashboard (overview), Room board (rooms) and Calendar render a fit-page root, their sub-screens match
// SUB_SCREENS, defaults as set, rooms keeps every room tile, calendar keeps its sticky grid inside one panel.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-front-a-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-front-a.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit-front-a.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const nav=(page,tabKey='')=>({desktop:true,page,tabKey,seq:1,setTabCrumb(){}});
function ctx(uid='owner'){
  const s=F.seed();const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:(p,fn,label,cls)=>h('button',{className:cls||'btn'},label),search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date),departures:[],pending:0,taskList:s.tasks,roomStatus:r=>r.hk==='Ready'?'Ready':'Dirty',
    switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},display:{isMobile:false,framed:false,mode:'auto',setMode(){}},
    setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const render=(C,page,tabKey='')=>withSession(()=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctx()},h(F.FitNavContext.Provider,{value:nav(page,tabKey)},h(C)))));
const read=f=>readFileSync(f,'utf8');

export function register({test,assert}){
  test('[FIT-FRONT-A] overview: fit-page root, StatStrip, three FitTabs sub-screens, default arrivals, three scroll panels, every block kept',()=>{
    store.clear();const html=render(F.Overview,'overview');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="overview"/);
    for(const id of ['arrivals','departures','in-house'])assert.ok(html.includes(`data-tab="${id}"`),id);
    assert.ok(html.includes('data-tab="arrivals" aria-selected="true"'),'default tab arrivals (as before)');
    for(const t of ['Room occupancy','Today’s arrivals','Room ready','Payment receipts','Front desk today','A view across your rooms','Needs your attention','Expected drawer cash','Outstanding balances','Deposits held','Today’s posted expenses','Keep the day in balance'])assert.ok(html.includes(t),t);
    assert.equal((html.match(/class="fit-panel-body"/g)||[]).length,3,'three panels');
    assert.ok(html.includes('A little clarity for a busy day.'),'welcome heading kept');
  });
  test('[FIT-FRONT-A] overview: a remembered or asked tab selects it; unknown id falls back to arrivals',()=>{
    store.clear();assert.ok(render(F.Overview,'overview','departures').includes('data-tab="departures" aria-selected="true"'));
    store.clear();store.set('hcc-fit-tab:overview','in-house');assert.ok(render(F.Overview,'overview').includes('data-tab="in-house" aria-selected="true"'));
    store.clear();store.set('hcc-fit-tab:overview','nope');assert.ok(render(F.Overview,'overview').includes('data-tab="arrivals" aria-selected="true"'));
  });
  test('[FIT-FRONT-A] rooms: fit-page board root, All floors first/default + one tab per floor, every non-retired room has a tile in All floors',()=>{
    store.clear();const html=render(F.RoomBoard,'rooms');const s=F.seed();
    assert.match(html,/^<section class="fit-page fit-board" data-fit-page="rooms"/);
    const tabs=[...html.matchAll(/data-tab="([^"]+)"/g)].map(m=>m[1]);
    assert.deepEqual(tabs,['all',...F.mstFloorGroups(s).map(f=>'floor-'+f.floor)]);
    assert.ok(html.includes('data-tab="all" aria-selected="true"'));
    const n=s.rooms.filter(r=>r.status!=='retired').length;
    assert.equal((html.match(/class="room-card /g)||[]).length,n,'all room tiles');
    assert.ok(html.includes('class="fit-rooms"')&&html.includes('Open the calendar'));
    const f1=F.mstFloorGroups(s)[0].floor;
    store.clear();const one=render(F.RoomBoard,'rooms','floor-'+f1);
    assert.equal((one.match(/class="room-card /g)||[]).length,s.rooms.filter(r=>r.status!=='retired'&&r.floor===f1).length,'one floor only');
  });
  test('[FIT-FRONT-A] calendar: fit-page root, one scroll panel holding the sticky grid, legend + note in the footer, no sub-screens',()=>{
    const html=render(F.Calendar,'calendar');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="calendar"/);
    assert.equal((html.match(/data-fit-scroll=/g)||[]).length,1);
    assert.ok(html.includes('role="grid"')&&html.includes('res-cal-head')&&html.includes('res-cal-room'));
    assert.ok(html.includes('aria-label="Legend"')&&html.includes('Bars cover nights')&&html.includes('Previous week'));
    assert.ok(!F.SUB_SCREENS.some(x=>x.page==='calendar'));
  });
  test('[FIT-FRONT-A] SUB_SCREENS lines of the lane: overview ×3, rooms/all; CSS is desktop-guarded, no font-size or hex',()=>{
    const mine=F.SUB_SCREENS.filter(x=>['overview','rooms','calendar'].includes(x.page)).map(x=>x.page+'/'+x.id);
    assert.deepEqual(mine,['overview/arrivals','overview/departures','overview/in-house','rooms/all']);
    const css=read('src/styles/fit-front-a.css').replace(/\/\*[^]*?\*\//g,'');
    assert.ok(!/font-size|#[0-9a-f]{3,6}\b/i.test(css));
    for(const rule of css.split('}').map(r=>r.trim()).filter(r=>r&&r.includes('{')))assert.ok(rule.replace(/^@media[^{]*\{/,'').trim().startsWith('.app-shell'),rule);
  });
}
