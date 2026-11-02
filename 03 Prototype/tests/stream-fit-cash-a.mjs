// FIT-CASH-A (wave W2a FIT, DEC-194…196, APR-025, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §6, lane fit-cash-a): the money pages
// folios (split + sub-screen tabs), cashier (split) and cashbook (fill) render one fit-page root on the desktop layout, their useFitTab ids
// equal their SUB_SCREENS lines, the defaults are as set, every action of the full-width pages is still on the page, and the phone markup of
// the shared pieces (CashBookView / DayCard / CashOutPanel phone) carries no fit- class. Layout only: the CSS is desktop-guarded and sets no font.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
let F=null,buildError='';
try{
  await build({entryPoints:['tests/fit-cash-a-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-cash-a.mjs',
    external:['react','react-dom','lucide-react'],logLevel:'silent',jsx:'automatic',define:{'process.env.HCC_BUILD':'"prototype"','process.env.NODE_ENV':'"production"'},
    loader:{'.css':'empty'},banner:{js:"import {createRequire as __fcaReq} from 'node:module';const require=__fcaReq(import.meta.url);"}});
  F=await import('./.generated/fit-cash-a.mjs');
}catch(e){buildError=String(e&&e.message).split(/\r?\n/)[0];}
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const nav=(page,tabKey='')=>({desktop:true,page,tabKey,seq:1,setTabCrumb(){}});
function ctxFor(s,uid='owner',isMobile=false){
  const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:{type:'x',key:'k1'},open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:(p,fn,label,style='btn')=>F.can(s,uid,p)?h('button',{className:style},label):null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date),departures:[],pending:0,taskList:s.tasks,roomStatus:()=>'Vacant',
    switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},display:{isMobile,framed:false,mode:'auto',setMode(){}},
    setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
/** Desktop render of a page component inside the desktop shell's contexts (FitNavContext desktop:true, the page id, optional asked tab). */
const page=(id,el,{uid='owner',tabKey='',s=F.seed()}={})=>withSession(()=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(s,uid)},
  h(F.FitNavContext.Provider,{value:nav(id,tabKey)},el))));
const phone=(el,{uid='owner',s=F.seed()}={})=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(s,uid,true)},el));
/** The single top-level element's opening tag and whether it spans the whole markup. */
const rootOf=html=>{const m=html.match(/^<([a-z0-9]+)([^>]*)>/);if(!m)return null;const tag=m[1];let depth=0,end=-1,mm;
  const re=new RegExp('<(/?)'+tag+'\\b[^>]*?(/?)>','g');while((mm=re.exec(html))){if(mm[2])continue;depth+=mm[1]?-1:1;if(depth===0){end=re.lastIndex;break;}}
  return {attrs:m[2],single:end===html.length};};
const noComments=t=>t.replace(/\/\*[^]*?\*\//g,'');
/** CSS rules (selector, body) outside @media wrappers, from a stylesheet text. */
const rules=css=>[...noComments(css).replace(/@media[^{]*\{/g,'').matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(m=>({sel:m[1].trim(),body:m[2]})).filter(r=>r.sel);

export function register({test,assert}){
  test('[FIT-CASH-A] page harness builds',()=>{assert.ok(F,'could not build the fit-cash-a harness: '+buildError);});
  if(!F)return;
  test('[FIT-CASH-A] folios, cashier and cashbook render one fit-page root with data-fit-page on the desktop layout',()=>{
    const want={folios:['fill',h(F.Folios)],cashier:['fill',h(F.Cashier)],cashbook:['fill',h(F.CashBook)]};
    for(const [id,[layout,el]] of Object.entries(want)){
      store.clear();const html=page(id,el);const r=rootOf(html);
      assert.ok(r&&r.single,`${id}: one top-level element`);
      assert.match(r.attrs,new RegExp(`class="fit-page fit-${layout}"`),id);
      assert.match(r.attrs,new RegExp(`data-fit-page="${id}"`),id);
      assert.ok(!/class="page-title"/.test(html),`${id}: no PageTitle inside the FitPage`);
    }
  });
  test('[FIT-CASH-A] folios: useFitTab ids = SUB_SCREENS lines (folios, folio-requests); default tab Folios; the requests tab opens by go(page,{tab})',()=>{
    const subs=F.SUB_SCREENS.filter(x=>x.page==='folios').map(x=>x.id);
    assert.deepEqual(subs,['folios','folio-requests']);
    const src=read('src/features/folio/Folios.tsx');
    assert.ok(src.includes("const FOLIO_TABS=['folios','folio-requests'] as const;")&&src.includes("useFitTab<FolioTab>('folios',FOLIO_TABS,'folios')"),'same ids, default folios');
    store.clear();const def=page('folios',h(F.Folios));
    assert.match(def,/role="tab" id="fit-tab-folios-folios" data-tab="folios" aria-selected="true"/);
    assert.ok(def.includes('class="fit-split list-only"')&&def.includes('aria-label="Folio list"')&&def.includes('Pick a folio'),'split: list + (hidden at ≥ 900 px, FIT-2 §12) empty record pane');
    assert.ok(def.includes('placeholder="Search guest, room, company or invoice…"')&&def.includes('>Balance due</button>'),'search + filters in the toolbar');
    const req=page('folios',h(F.Folios),{tabKey:'folio-requests'});
    assert.match(req,/data-tab="folio-requests" aria-selected="true"/);
    assert.ok(!req.includes('class="fit-split')&&req.includes('>Folio requests</h2>')&&req.includes('no self-approval'),'requests tab = one ScrollPanel');
    assert.equal(store.get('hcc-fit-tab:folios'),'folio-requests','the asked tab is remembered for the session');
    for(const html of [def,req]){const row=html.slice(html.indexOf('class="fol-tabsrow"'),html.indexOf('class="fit-page-body"'));
      assert.ok(row.includes('role="tablist"')&&row.includes('class="small-stats fol-stats" role="list"'),'tabs and the totals strip share one row');
      for(const t of ['Outstanding balances','In house','Folio requests pending','Approved refunds to execute'])assert.ok(row.includes(`<span>${t}</span>`),t+' (small-stats markup kept)');}
  });
  test('[FIT-CASH-A] folios: every row keeps the dialog opener, the record pane renders the full folio (BookingModal) so every action of it stays',()=>{
    store.clear();const html=page('folios',h(F.Folios));
    assert.match(html,/aria-label="Open folio for [^"]+"/,'the arrow button still opens the folio dialog');
    assert.ok(html.includes('+ New reservation'),'header action');
    const src=read('src/features/folio/Folios.tsx');
    assert.ok(src.includes('detail={open1?<div className="fol-record"><BookingModal id={open1}/></div>:null}'),'the record pane is the booking/folio dialog body itself');
    assert.ok(src.includes('onClick={()=>booking(b.id)}')&&src.includes('onClick={()=>setSel(b.id)}'),'row opens the record in the pane; the arrow opens the dialog');
    assert.ok(/<FitNote text=\{NOTE\}\/>/.test(src)&&src.includes('All names and records are fictional. Tax figures use the dated GST master'),'footnote kept (FitNote)');
  });
  test('[FIT-CASH-A] cashier: split columns — expected cash + count left, movements + banking right; header holds the cash-out buttons and Day closing',()=>{
    store.clear();const html=page('cashier',h(F.Cashier));
    assert.ok(html.includes('class="fit-cols cashier-cols"'),'two columns');
    for(const t of ['>Expected cash</h2>','>Today’s cash movements</h2>','>Banking, transfers and drawings</h2>','SHIFT STATUS','= Expected cash in drawer'])assert.ok(html.includes(t),t);
    assert.match(html,/>(Count the drawer|Difference approval|Shift result)<\/h2>/);
    const head=html.slice(0,html.indexOf('</header>'));
    assert.ok(head.includes('Day closing'),'Day closing in the header');
    for(const b of ['Till transfer','Owner drawing'])assert.ok(head.includes(b),b+' in the header (Owner)');
    assert.ok(!head.includes('Bank deposit'),'Owner does not bank (CX_BANK_ROLES unchanged)');
    const body=html.slice(html.indexOf('</header>'));
    assert.ok(!/<button[^>]*>(<svg[^>]*>.*?<\/svg>)? ?(Till transfer|Owner drawing)<\/button>/.test(body),'the cash-out buttons are not repeated in the panel');
    const mgr=page('cashier',h(F.Cashier),{uid:'manager'});
    assert.ok(mgr.slice(0,mgr.indexOf('</header>')).includes('Bank deposit'),'Manager: Bank deposit in the header');
    assert.equal((html.match(/data-fit-scroll=""/g)||[]).length,4,'four scroll panels');
  });
  test('[FIT-CASH-A] cashier: a role without cash-out rights gets no cash-out buttons; the CashOutPanel phone markup has no fit- class',()=>{
    store.clear();const rec=page('cashier',h(F.Cashier),{uid:'reception'});
    assert.ok(rec.includes('Day closing'));
    const p=phone(h(F.CashOutPanel,{phone:true}));
    assert.ok(p.startsWith('<section class="mm-card" aria-label="Cash leaving the drawer">'),p.slice(0,80));
    assert.ok(!/fit-/.test(p),'phone panel unchanged');
    const d=phone(h(F.CashOutPanel,{}));assert.ok(d.startsWith('<section class="card" aria-label="Cash leaving the drawer">')&&!/fit-/.test(d),'old desktop panel markup kept for other users');
  });
  test('[FIT-CASH-A] cashbook: toolbar = range + cb-sums strip; days table in a ScrollPanel; "By account · settles to" in its own scroll box; phone unchanged',()=>{
    store.clear();const html=page('cashbook',h(F.CashBook));
    const tb=html.slice(html.indexOf('class="fit-page-toolbar"'),html.indexOf('class="fit-page-body"'));
    assert.ok(tb.includes('class="cb-range"')&&tb.includes('type="date"'),'range in the toolbar');
    assert.ok(tb.includes('class="cb-sums fit-stats" role="list"')&&tb.includes('<span>Cash over/short (counted − expected)</span>'),'sums strip keeps the cb-sums markup');
    assert.ok(html.includes('aria-label="Cash book days"')&&html.includes('<th>Business date</th>'),'days table in the panel');
    assert.match(html,/class="cb-settle" data-fit-scroll="" tabindex="0" aria-label="By account · settles to"><section class="card mny-settle"/);
    assert.match(html.slice(0,html.indexOf('</header>')),/CSV[^]*Excel/,'exports in the header');
    const ph=phone(h(F.CashBookView,{phone:true}));
    assert.ok(ph.startsWith('<div class="cb-page mm-page">')&&!/fit-/.test(ph),'phone cash book unchanged');
  });
  test('[FIT-CASH-A] cashbook day view: Lock / Reopen in the Day lock panel head (always visible), entries table left, back to all days in the toolbar',()=>{
    const s=F.seed();const date=F.cbDates(s).at(-1);
    store.clear();const html=page('cashbook',h(F.DayCard,{d:F.cbDay(s,date),phone:false,onBack(){}}),{s});
    assert.ok(rootOf(html).single&&html.startsWith('<section class="fit-page fit-fill" data-fit-page="cashbook"'));
    assert.match(html,/>Day lock<\/h2><div class="inline fit-panel-actions">(<button class="btn primary"[^>]*>.*?Lock this day<\/button>|<button class="btn"[^>]*>.*?Reopen…<\/button>)/);
    assert.ok(html.includes('aria-label="Cash book entries"')&&html.includes('Opening balance (carried forward)'),'entries table');
    assert.ok(html.slice(html.indexOf('fit-page-toolbar'),html.indexOf('fit-page-body')).includes('All days'),'back button in the toolbar');
    const ph=phone(h(F.DayCard,{d:F.cbDay(s,date),phone:true,onBack(){}}),{s});
    assert.ok(ph.startsWith('<div class="cb-daybar">')&&!/fit-/.test(ph),'phone day card unchanged');
  });
  test('[FIT-CASH-A] CSS: desktop rules only (every selector under .app-shell), no font size, no hex colour',()=>{
    for(const name of ['cashbook.css','cashier.css','folios.css']){const css=read('src/styles/'+name);
      const rs=rules(css);assert.ok(rs.length>3,name);
      for(const r of rs){for(const sel of r.sel.split(','))assert.match(sel.trim(),/^\.app-shell /,`${name}: ${sel}`);
        assert.ok(!/font-size|font:/.test(r.body),`${name}: no font change (${r.sel})`);assert.ok(!/#[0-9a-f]{3,8}\b/i.test(r.body),`${name}: tokens only (${r.sel})`);}
    }
  });
}
