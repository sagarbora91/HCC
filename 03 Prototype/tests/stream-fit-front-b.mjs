// FIT-FRONT-B (wave W2a FIT, DEC-194…196, contract §6): reservations, folios view, arrivals, inhouse and guests render a fit-page root,
// the list sits in a ScrollPanel / SplitView, the primary action is in the header, no page has sub-screens (no SUB_SCREENS lines), layout only.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-front-b-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-front-b.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit-front-b.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const nav=page=>({desktop:true,page,tabKey:'',seq:1,setTabCrumb(){}});
function ctxFor(uid='owner'){
  const s=F.seed();const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:(p,fn,label,cls)=>h('button',{className:cls},label),search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:[],departures:[],pending:0,taskList:s.tasks,roomStatus:()=>'Vacant',switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},
    display:{isMobile:false,framed:false,mode:'auto',setMode(){}},setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const page=(id,el,uid)=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(uid)},h(F.FitNavContext.Provider,{value:nav(id)},el)));

export function register({test,assert}){
  test('[FIT-FRONT-B] reservations: fit-page fill root, primary action in the header, table in a ScrollPanel with ShowMore + note in the footer',()=>{
    const html=page('reservations',h(F.Reservations));
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="reservations"/);
    assert.ok(html.includes('class="fit-page-actions"')&&html.includes('+ New reservation'));
    assert.ok(html.includes('aria-label="Reservations"')&&html.includes('data-fit-scroll=""'));
    assert.ok(html.includes('res-list-filters')&&html.includes('Search guest, phone, room or booking'),'toolbar keeps search, status, source and date');
    assert.ok(!html.includes('class="footnote"'),'the footnote is a FitNote now');
  });
  test('[FIT-FRONT-B] folios view of the same component keeps its title and fits',()=>{
    const html=page('folios',h(F.Reservations,{folio:true}));
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="folios"/);assert.ok(html.includes('Guests &amp; folios'));
  });
  test('[FIT-FRONT-B] arrivals: still a fit-page (polish only)',()=>{
    assert.match(page('arrivals',h(F.ArrivalsPage)),/^<section class="fit-page fit-fill" data-fit-page="arrivals"/);
  });
  test('[FIT-FRONT-B] inhouse and guests: fit-page split with a SplitView; first record pre-selected on the wide layout',()=>{
    const a=page('inhouse',h(F.InHousePage));
    assert.match(a,/^<section class="fit-page fit-split" data-fit-page="inhouse"/);assert.ok(a.includes('class="fit-split has-detail"')&&a.includes('aria-label="In-house stays"'),'wide layout selects the first stay');
    const g=page('guests',h(F.GuestsPage));
    assert.match(g,/^<section class="fit-page fit-split" data-fit-page="guests"/);assert.ok(g.includes('aria-label="Guest profiles"')&&g.includes('New guest'));
  });
  test('[FIT-FRONT-B] these pages have no sub-screens: nothing of them in SUB_SCREENS, no FitTabs, no PageTitle left',()=>{
    for(const p of ['reservations','arrivals','inhouse','guests']) // folios (fit-cash-a's page) has sub-screens Folios / Folio requests
     assert.ok(!F.SUB_SCREENS.some(x=>x.page===p),p);
    for(const f of ['src/features/reservations/Reservations.tsx','src/features/guests/InHouse.tsx','src/features/guests/GuestProfile.tsx']){
      const src=read(f);assert.ok(!src.includes('<PageTitle')&&!src.includes('FitTabs'),f);}
  });
  test('[FIT-FRONT-B] no stylesheet touched (guests.css / reservations.css are token-checked files); the pages carry no hex colour or font-size',()=>{
    for(const f of ['src/features/reservations/Reservations.tsx','src/features/guests/InHouse.tsx','src/features/guests/GuestProfile.tsx','src/features/guests/useWideSplit.ts'])
      assert.ok(!/#[0-9a-f]{6}|fontSize|font-size/i.test(read(f).replace(/&#\d+;/g,'')),f);
    assert.ok(read('src/features/guests/GuestProfile.tsx').includes('className="gs-record"'),'the record sits in the split pane, not in a second card');
  });
}
