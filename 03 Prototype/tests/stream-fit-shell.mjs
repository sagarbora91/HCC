// FIT-SHELL (wave W2a FIT, DEC-178…180, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §3–§5, §7): the fit components render with
// the contract's classes and attributes, useFitTab picks go(page,{tab}) → sessionStorage → fallback, the sub-screen registry feeds the search
// palette, desktop dialogs get the scrolling body + sticky action bar, the print rule is present, and the phone shell shows no fit- class.
import {build} from 'esbuild';
import {readFileSync,readdirSync} from 'node:fs';
await build({entryPoints:['tests/fit-shell-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-shell.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit-shell.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const css=read('src/styles/z-fit.css');
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const nav=(page,tabKey='')=>({page,tabKey,seq:1,setTabCrumb(){}});
const inNav=(n,el)=>h(F.FitNavContext.Provider,{value:n},el);
function ctxFor(isMobile,uid='owner'){
  const s=F.seed();const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date),departures:[],pending:0,taskList:s.tasks,roomStatus:()=>'Vacant',
    switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},display:{isMobile,framed:false,mode:'auto',setMode(){}},
    setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const inCtx=(c,el)=>h(F.HccContext.Provider,{value:c},el);
const noComments=t=>t.replace(/\/\*[^]*?\*\//g,'');

export function register({test,assert}){
  test('[FIT-SHELL] FitPage: section.fit-page.fit-<layout> with data-fit-page, 22 px header row (page-title classes), one-line sub with title, actions, toolbar, tabs, body',()=>{
    const html=renderToStaticMarkup(inNav(nav('arrivals'),h(F.FitPage,{title:'Arrivals & check-in',sub:'Expected today',actions:h('button',{className:'btn primary'},'Walk-in'),toolbar:h('span',null,'tb'),tabs:h('div',{className:'tabs-x'}),layout:'split'},h('p',null,'body'))));
    assert.match(html,/^<section class="fit-page fit-split" data-fit-page="arrivals"/);
    assert.ok(html.includes('<header class="page-title fit-page-head"><div class="fit-page-heading"><h1 id="fit-page-h1">Arrivals &amp; check-in</h1><p class="fit-page-sub" title="Expected today">Expected today</p></div><div class="fit-page-actions"><button class="btn primary">Walk-in</button></div></header>'),html);
    assert.ok(html.includes('<div class="fit-page-toolbar"><span>tb</span></div><div class="tabs-x"></div><div class="fit-page-body"><p>body</p></div>'));
    assert.match(renderToStaticMarkup(h(F.FitPage,{title:'X'},'y')),/class="fit-page fit-fill"/,'default layout fill');
  });
  test('[FIT-SHELL] ScrollPanel: card with fixed head, focusable labelled data-fit-scroll body, footer; BoardColumns / StatStrip reuse the card and Stat classes',()=>{
    const html=renderToStaticMarkup(h(F.ScrollPanel,{title:'Arrivals',label:'Arrivals list',actions:h('span',null,'2'),footer:h('p',{className:'footnote'},'note'),grow:2},'rows'));
    assert.ok(html.startsWith('<section class="card fit-panel" aria-label="Arrivals list" style="flex-grow:2"><div class="section-title fit-panel-head"><h2>Arrivals</h2>'),html);
    assert.ok(html.includes('<div class="fit-panel-body" data-fit-scroll="" tabindex="0" aria-label="Arrivals list">rows</div><div class="fit-panel-foot"><p class="footnote">note</p></div>'),html);
    const b=renderToStaticMarkup(h(F.BoardColumns,{columns:[{id:'a',title:'Dirty',count:3,body:'x'},{id:'b',title:'Ready',body:'y'}],minColumn:260}));
    assert.ok(b.includes('class="fit-board" style="--fit-col:260px"'),b);assert.equal((b.match(/data-fit-scroll=""/g)||[]).length,2);
    const st=renderToStaticMarkup(h(F.StatStrip,{items:[{title:'Arrivals',value:'4',sub:'today',tone:'warn'}]}));
    assert.ok(st.includes('<div class="fit-stats" role="list" aria-label="Key figures"><div role="listitem" class="stat fit-stat warn"><div class="stat-label">Arrivals</div><strong class="money-stat">4</strong><small>today</small>'),st);
  });
  test('[FIT-SHELL] SplitView: list + empty pane, has-detail + Back button when a record is open, record pane scrolls (data-fit-scroll), ratio → grid columns',()=>{
    const empty=renderToStaticMarkup(h(F.SplitView,{list:'L',detail:null,empty:'Pick one',onCloseDetail(){},label:'Folios'}));
    assert.ok(empty.includes('class="fit-split" aria-label="Folios" role="group" style="--fit-split-cols:minmax(0,2fr) minmax(0,3fr)"'),empty);
    assert.ok(empty.includes('>Pick one<')&&!empty.includes('fit-split-back'));
    const open=renderToStaticMarkup(h(F.SplitView,{list:'L',detail:'Record',empty:'Pick one',onCloseDetail(){},label:'Folios',ratio:'1fr 1fr'}));
    assert.ok(open.includes('class="fit-split has-detail"')&&open.includes('fit-split-back')&&open.includes('>Record<')&&open.includes('minmax(0,1fr) minmax(0,1fr)'),open);
    assert.ok(open.includes('class="fit-split-pane" data-fit-scroll="" tabindex="0" aria-label="Folios — record"'));
    assert.ok(read('src/components/fit.tsx').includes('useStepBack(narrow&&open,()=>close.current());'),'Android Back closes the slide-over first');
    assert.ok(css.includes('@media (max-width:899.98px){\n  .fit-split{grid-template-columns:minmax(0,1fr)}'),'below 900 px the record slides over the list');
  });
  test('[FIT-SHELL] FitTabs: tablist with roving tabindex, aria-selected, counts; also .tabs so the old tab look and font stay',()=>{
    const html=renderToStaticMarkup(h(F.FitTabs,{page:'pos',value:'b',onChange(){},tabs:[{id:'a',label:'Orders'},{id:'b',label:'Tables',count:3,badge:'warn'}]}));
    assert.ok(html.startsWith('<div class="fit-tabs-bar tabs" role="tablist" aria-label="Sections of this page">'),html);
    assert.ok(html.includes('<button type="button" role="tab" id="fit-tab-pos-a" data-tab="a" aria-selected="false" tabindex="-1" class="">Orders</button>'),html);
    assert.ok(html.includes('<button type="button" role="tab" id="fit-tab-pos-b" data-tab="b" aria-selected="true" tabindex="0" class="active">Tables<span class="fit-tab-count warn">3</span></button>'),html);
    const src=read('src/components/fit.tsx');for(const k of ["'ArrowRight'","'ArrowLeft'","'Home'","'End'"])assert.ok(src.includes(k),k);
    assert.ok(css.includes('.app-shell .fit-tabs-bar [role=tab]{min-height:var(--hcc-touch-primary)!important}'),'48 px tabs on touch');
  });
  test('[FIT-SHELL] useFitTab: go(page,{tab}) wins, then sessionStorage hcc-fit-tab:<page>, then the fallback; unknown ids fall back',()=>{
    const Probe=({page})=>{const [t]=F.useFitTab(page,['all','pending','paid'],'all');return h('i',null,t);};
    const r=(n,page='expenses')=>withSession(()=>renderToStaticMarkup(inNav(n,h(Probe,{page}))));
    store.clear();assert.equal(F.fitTabKey('expenses'),'hcc-fit-tab:expenses');
    assert.equal(r(nav('expenses')),'<i>all</i>','fallback');
    store.set('hcc-fit-tab:expenses','paid');assert.equal(r(nav('expenses')),'<i>paid</i>','remembered in this session');
    assert.equal(r(nav('expenses','pending')),'<i>pending</i>','go(page,{tab}) wins');
    assert.equal(r(nav('cashier','pending')),'<i>paid</i>','a tab asked for another page is ignored');
    assert.equal(r(nav('expenses','nope')),'<i>paid</i>','unknown asked tab → remembered');
    store.set('hcc-fit-tab:expenses','gone');assert.equal(r(nav('expenses')),'<i>all</i>','unknown remembered tab → fallback');
    assert.equal(renderToStaticMarkup(h(Probe,{page:'expenses'})),'<i>all</i>','no sessionStorage, no shell: fallback');
    const src=read('src/components/fit.tsx');assert.ok(src.includes('const choose=useCallback((t:T)=>{writeTab(page,t);setTab(t);},[page]);'),'a switch is remembered, no history entry');
    const app=read('src/app/App.tsx');
    assert.ok(app.includes("setTabKey(opts.tab??'');setNavSeq(n=>n+1);setPage(p);"),'go passes the tab and moves the navigation counter');
    assert.ok(app.includes("if(st?.hcc==='page'&&st.page){setPage(st.page);setTabKey(st.tab??'');setNavSeq(n=>n+1);setDepth(histIdx());}"),'Back / Forward re-read the tab');
    assert.ok(app.includes('<FitNavContext.Provider value={fitNav}>'),'the desktop branch passes tabKey to pages through context');
  });
  test('[FIT-SHELL] StepFlow / FormSections / ModalFrame render the contract structure (sticky action bars use .modal-actions)',()=>{
    const sf=renderToStaticMarkup(h(F.StepFlow,{steps:[{id:'a',label:'Arrivals',status:'done'},{id:'b',label:'Cash',status:'current',note:'2 left'},{id:'c',label:'Close',status:'blocked'}],value:'b',onChange(){}},'body'));
    assert.ok(sf.includes('class="fit-steps"')&&sf.includes('aria-current="step"')&&sf.includes('class="fit-step current on"')&&sf.includes('<small>2 left · current step</small>')&&sf.includes('<div class="fit-step-body">body</div>'),sf);
    const fs=renderToStaticMarkup(h(F.FormSections,{sections:[{id:'a',label:'General'},{id:'b',label:'Money',dirty:true}],value:'b',onChange(){},actions:h('button',{className:'btn primary'},'Save')},'fields'));
    assert.ok(fs.includes('class="fit-form-body" data-fit-scroll="" tabindex="0" aria-label="Money — fields"')&&fs.includes('<div class="modal-actions fit-form-actions"><button class="btn primary">Save</button></div>')&&fs.includes('aria-label="unsaved changes"'),fs);
    const mf=renderToStaticMarkup(h(F.ModalFrame,{heading:h('h2',null,'Title'),actions:h('button',{className:'btn'},'Close')},'content'));
    assert.equal(mf,'<div class="fit-modal-frame"><div class="modal-heading"><h2>Title</h2></div><div class="modal-body fit-modal-frame-body" data-fit-scroll="" tabindex="0" aria-label="Dialog content">content</div><div class="modal-actions"><button class="btn">Close</button></div></div>');
  });
  test('[FIT-SHELL] sub-screen registry: seven empty lane blocks in order; entries unique, kebab-case, on a nav page; the palette lists them as "Page › Tab" opening go(page,{tab})',()=>{
    const src=read('src/app/subscreens.ts');
    const blocks=[...src.matchAll(/\/\/ (FIT-[A-Z]+)\r?\n/g)].map(m=>m[1]);
    assert.deepEqual(blocks,['FIT-FRONT','FIT-CASH','FIT-OPS','FIT-GUEST','FIT-REVENUE','FIT-PEOPLE','FIT-ADMIN']);
    assert.ok(src.includes('export type SubScreen={page:string;id:string;label:string;keywords?:string[]};'));
    const keys=F.SUB_SCREENS.map(x=>x.page+'/'+x.id);assert.equal(new Set(keys).size,keys.length,'unique page/tab');
    const navIds=new Set(F.navItems.map(n=>n.id));for(const x of F.SUB_SCREENS){assert.ok(navIds.has(x.page),x.page);assert.match(x.id,/^[a-z0-9]+(-[a-z0-9]+)*$/,x.id);}
    const e=F.subScreenEntries([{id:'expenses',label:'Expenses'}],[{page:'expenses',id:'pending',label:'Pending',keywords:['awaiting']},{page:'gone',id:'x',label:'X'}]);
    assert.equal(e.length,1);assert.equal(e[0].label,'Expenses › Pending');assert.equal(e[0].tab,'pending');assert.equal(e[0].page,'expenses');assert.equal(e[0].kind,'page');
    assert.deepEqual(e[0].path.slice(-2),['Expenses','Pending']);assert.ok(e[0].keywords.includes('awaiting'));
    const pal=read('src/app/SearchPalette.tsx');
    assert.ok(pal.includes('[...buildSearchEntries(navItems),...subScreenEntries(navItems)]')&&pal.includes('go(e.page,e.tab?{tab:e.tab}:{});'));
  });
  test('[FIT-SHELL] breadcrumb: Modules Home › Category › Page › Tab, the tab crumb is text (aria-current), not a button',()=>{
    const html=renderToStaticMarkup(inCtx(ctxFor(false),h(F.Breadcrumb,{page:'expenses',label:'Expenses',tab:'Pending'})));
    assert.ok(html.includes('<span>Expenses</span>')&&html.endsWith('<span aria-current="page">Pending</span></li></ol></nav>'),html);
    assert.ok(!/<button[^>]*>Pending/.test(html));
    assert.ok(read('src/app/App.tsx').includes('tab={crumbTab}'),'the shell passes the selected FitTabs label');
  });
  test('[FIT-SHELL] desktop dialogs: ModalHost wraps the body in .modal-body[data-fit-scroll] (not phone sheets); actions sticky at the bottom, heading fixed, max-height 100dvh − 32px',()=>{
    const mh=read('src/app/ModalHost.tsx');
    assert.ok(mh.includes('{sheet?body():<div className="modal-body" data-fit-scroll="" tabIndex={0} aria-label="Dialog content">{body()}</div>}'));
    assert.ok(css.includes('.modal:not(.m-sheet){max-height:calc(100vh - 32px);max-height:calc(100dvh - 32px);display:flex;flex-direction:column;overflow:hidden;padding:0}'));
    assert.ok(css.includes('.modal:not(.m-sheet)>.modal-body{flex:1 1 auto;min-height:0;overflow:auto;'));
    assert.ok(css.includes('.modal:not(.m-sheet) .modal-body .modal-actions{position:sticky;bottom:0;z-index:2;background:var(--c-ffffff);border-top:1px solid var(--line);'));
    assert.ok(css.includes('.modal:not(.m-sheet) .modal-body .modal-heading{position:sticky;top:0;'));
    assert.ok(mh.includes('<div className="fit-user-menu"><UserMenuSheet/><AboutBuild/></div>'),'About this build (the old footer lines) in the user menu on desktop');
  });
  test('[FIT-SHELL] print rule: the shell returns to normal flow, scroll panels print in full, the tab bar is hidden',()=>{
    const p=css.slice(css.indexOf('@media print{'));
    assert.ok(p.includes('.app-shell,.app-shell main,.app-shell .content{height:auto;overflow:visible}'));
    assert.ok(p.includes('[data-fit-scroll]{overflow:visible;max-height:none}'));assert.ok(p.includes('.fit-tabs-bar{display:none}'));
  });
  test('[FIT-SHELL] z-fit.css loads after z-touch.css and before zz-print.css; DEC-179: its only font sizes are the 22 px page h1, one "inherit" and the copied 12 px kbd; colours are tokens',()=>{
    const order=F.styleOrder(readdirSync('src/styles').filter(f=>f.endsWith('.css')));
    const i=order.indexOf('z-fit.css');assert.ok(i>order.indexOf('z-touch.css')&&i<order.indexOf('zz-print.css')&&i===order.length-2,order.slice(-4).join(','));
    assert.deepEqual(F.styleOrder(['b.css','a.css']),['a.css','b.css'],'no z-fit.css: plain name order');
    assert.ok(read('tools/cssTextScale.mjs').includes("const extra=styleOrder((await readdir(dir)).filter(f=>f.endsWith('.css')));"));
    const sizes=[...noComments(css).matchAll(/font-size:([^;}]+)/g)].map(m=>m[1]).sort();
    assert.deepEqual(sizes,['12px','22px','inherit']);
    assert.ok(css.includes('.app-shell .content .page-title h1{font-size:22px;font-weight:600;letter-spacing:-.4px;'));
    assert.doesNotMatch(noComments(css),/#[0-9a-fA-F]{3,8}\b|rgba?\(/);
  });
  test('[FIT-SHELL] shell: 56 px one-row top bar with priority squeeze, Demo pill holds the DemoToolbar, no page footer on work pages, legacy fallback marks data-fit-scroll="legacy"',()=>{
    const app=read('src/app/App.tsx');
    assert.ok(css.includes(':root{--fit-topbar:56px}')&&css.includes('.app-shell .topbar.fit-topbar{height:var(--fit-topbar);min-height:var(--fit-topbar);flex:none;flex-wrap:nowrap;'));
    for(const k of ['fit-clock','fit-date-text','fit-search-text'])assert.ok(app.includes(k)&&css.includes(k),k);
    assert.ok(app.includes('<DemoPill/>')&&!app.includes('<DemoToolbar/>')&&!app.includes('page-footer')&&!app.includes('demo-badge'));
    const demo=read('src/app/demo/DemoToolbar.tsx');assert.ok(demo.includes("className={'fit-demo-pill'")&&demo.includes('<DemoToolbar/>'));
    assert.ok(app.includes('<div className="fit-legacy" data-fit-scroll="legacy">{children}</div>'));
    assert.ok(css.includes('.app-shell .content{flex:1;min-height:0;width:100%;max-width:1580px;margin:0 auto;overflow:hidden;display:flex;flex-direction:column;padding:12px 16px 12px}'));
    assert.ok(css.includes('.app-shell{height:100vh;height:100dvh;min-height:0;overflow:hidden}'));
  });
  test('[FIT-SHELL] Modules Home / category page / sign-in: fit markup on the desktop layout, the old markup on the phone (no fit- class in the phone shell)',()=>{
    const desk=renderToStaticMarkup(inCtx(ctxFor(false),h(F.ModulesHome)));
    assert.match(desk,/^<section class="fit-page fit-fill fit-home"/);
    assert.ok(desk.includes('<h1 id="fit-page-h1">Modules</h1>')&&desk.includes('class="fit-home-grid" data-fit-scroll=""')&&desk.includes('fit-home-foot'),desk.slice(0,400));
    const cat=renderToStaticMarkup(inCtx(ctxFor(false),h(F.CategoryPage,{page:'cat:front'})));assert.match(cat,/^<section class="fit-page fit-fill fit-home"/);
    const signDesk=renderToStaticMarkup(inCtx(ctxFor(false),h(F.SignIn,{onPick(){}})));assert.ok(signDesk.includes('class="signin fit-signin"')&&signDesk.includes('data-fit-scroll=""'));
    const pc=ctxFor(true);
    const phone=renderToStaticMarkup(inCtx(pc,h(F.MobileShell,{allowedIds:F.navItems.map(n=>n.id),canGoBack:false,onBack(){},tabKey:'',onTab(){}},h(F.ModulesHome))));
    assert.ok(phone.includes('m-shell')&&phone.includes('class="home"'),'phone shell renders Modules Home as before');
    assert.doesNotMatch(phone,/fit-|data-fit-scroll/);
    assert.doesNotMatch(renderToStaticMarkup(inCtx(pc,h(F.SignIn,{onPick(){}}))),/fit-|data-fit-scroll/);
    assert.doesNotMatch(renderToStaticMarkup(inCtx(pc,h(F.CategoryPage,{page:'cat:front'}))),/fit-|data-fit-scroll/);
  });
  test('[FIT-SHELL] reference page arrivals is a FitPage: actions in the header, search in the toolbar, table in a ScrollPanel with the footnote in its footer',()=>{
    const src=read('src/features/guests/CheckIn.tsx');const p=src.slice(src.indexOf('export function ArrivalsPage'),src.indexOf('export function CheckinModal'));
    assert.ok(p.includes('return <FitPage title="Arrivals & check-in" sub={sub}')&&p.includes('toolbar={<><SearchBox')&&p.includes('<ScrollPanel label="Arrivals awaiting check-in" grow={2}')&&p.includes('footer={<p className="footnote"'));
    assert.ok(!p.includes('PageTitle'));
  });
}
