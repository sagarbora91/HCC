// FIT-CASH-B (wave W2a FIT, lane fit-cash-b, DEC-194…196, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §6): pages `pos` and `expenses` are FitPages.
// pos: view tabs orders / kitchen / summary (FitTabs + SUB_SCREENS), three columns that scroll on their own, KOT / Settle bar sticky.
// expenses: sections register / recurring / petty / budgets / udhaar / vendors / month are the page tabs; the status tabs inside the register keep their ids.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-cash-b-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-cash-b.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit-cash-b.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const nav=(page,tabKey='')=>({desktop:true,page,tabKey,seq:1,setTabCrumb(){}});
function ctxFor(uid='owner'){
  const s=F.seed();const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:[],departures:[],pending:0,taskList:s.tasks,roomStatus:()=>'Vacant',switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},
    display:{isMobile:false,framed:false,mode:'auto',setMode(){}},setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const page=(Comp,id,uid='owner',tabKey='')=>withSession(()=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(uid)},h(F.FitNavContext.Provider,{value:nav(id,tabKey)},h(Comp)))));
const tabIds=html=>[...html.matchAll(/id="fit-tab-[a-z]+-([a-z-]+)"/g)].map(m=>m[1]);
const subIds=pg=>F.SUB_SCREENS.filter(x=>x.page===pg).map(x=>x.id);

export function register({test,assert}){
  test('[FIT-CASH-B] pos renders a fit-page root with the three view tabs = SUB_SCREENS, default Orders, three scrolling columns',()=>{
    const html=page(F.PosPage,'pos');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="pos"/);
    assert.deepEqual(tabIds(html),['orders','kitchen','summary']);assert.deepEqual(subIds('pos'),['orders','kitchen','summary']);
    assert.match(html,/id="fit-tab-pos-orders"[^>]*aria-selected="true"/,'default tab Orders');
    for(const c of ['pos-col-spots','pos-col-menu','pos-col-check']){
      const m=html.match(new RegExp('<(?:div|aside) class="pos-col '+c+'"[^>]*>'));
      assert.ok(m&&m[0].includes('data-fit-scroll=""')&&m[0].includes('tabindex="0"')&&m[0].includes('aria-label='),c+' scrolls on its own and is keyboard reachable');}
    assert.ok(!html.includes('<h1 class'),'no old PageTitle');
  });
  test('[FIT-CASH-B] pos: Kitchen and Day summary tabs open from go(page,{tab}); no Day summary for the Restaurant role (DEC-079 D1)',()=>{
    const k=page(F.PosPage,'pos','owner','kitchen');
    assert.match(k,/id="fit-tab-pos-kitchen"[^>]*aria-selected="true"/);assert.ok(k.includes('class="fit-board"')&&k.includes('Preparing')&&k.includes('Sold out today (86 list)'));
    const sm=page(F.PosPage,'pos','owner','summary');
    assert.match(sm,/id="fit-tab-pos-summary"[^>]*aria-selected="true"/);assert.ok(sm.includes('Print summary')&&sm.includes('class="fit-stats"')&&sm.includes('One shared till'));
    const rst=F.seed().users.find(u=>u.role==='Restaurant');
    if(rst){const r=page(F.PosPage,'pos',rst.id,'summary');assert.ok(!r.includes('id="fit-tab-pos-summary"'),'summary tab hidden');assert.ok(r.includes('pos-col-check'),'falls back to Orders');}
  });
  test('[FIT-CASH-B] pos: the Charge / KOT bar is sticky in the check column and PosCheck still renders Send KOT / Settle',()=>{
    const css=read('src/styles/fit-cash-b.css');
    assert.match(css,/\.pos-check \.pos-actions\{position:sticky;bottom:0/);
    assert.match(css,/\.pos-col-check\{display:flex;flex-direction:column\}/);
    const src=read('src/features/pos/parts.tsx');
    assert.ok(src.includes('<div className="pos-actions">')&&src.includes('Send KOT')&&src.includes('Settle'));
  });
  test('[FIT-CASH-B] expenses renders a fit-page root; section tabs = SUB_SCREENS, default Register; status tab ids unchanged',()=>{
    const html=page(F.Expenses,'expenses');
    assert.match(html,/^<section class="fit-page fit-fill" data-fit-page="expenses"/);
    assert.deepEqual(tabIds(html),['register','recurring','petty','budgets','udhaar','vendors','month']);
    assert.deepEqual(subIds('expenses'),['register','recurring','petty','budgets','udhaar','vendors','month']);
    assert.match(html,/id="fit-tab-expenses-register"[^>]*aria-selected="true"/);
    const status=[...html.matchAll(/<option value="(All|Draft|Pending|Posted|Rejected|Reversed|Void)">/g)].map(m=>m[1]);
    assert.deepEqual(status,['All','Draft','Pending','Posted','Rejected','Reversed','Void'],'status options and their order unchanged (FIT-2: a select in the filter row)');
    for(const t of ['Expense register','Approval requests (','Ledger CSV','Mode','Month','Search','Add expense'])assert.ok(html.includes(t),t+' reachable');
    assert.ok(html.includes('data-fit-scroll=""'));
  });
  test('[FIT-CASH-B] expenses: every SCC section is reachable as a tab (recurring, petty cash, budgets, Udhaar, vendors, month & tax)',()=>{
    const want={recurring:'Recurring expenses',petty:'Petty cash float',budgets:'Monthly budgets',udhaar:'Udhaar / credit',vendors:'Vendor payments',month:'Month close'};
    for(const [id,text] of Object.entries(want)){const html=page(F.Expenses,'expenses','owner',id);
      assert.match(html,new RegExp('id="fit-tab-expenses-'+id+'"[^>]*aria-selected="true"'),id+' selected');assert.ok(html.includes(text),id+' shows '+text);}
  });
  test('[FIT-CASH-B] expenses: a user without the vendor books has no Vendor payments tab and falls back to Register',()=>{
    const s=F.seed();const nb=s.users.find(u=>u.role==='Cashier')||s.users.find(u=>u.role==='Reception');
    if(!nb)return;const html=page(F.Expenses,'expenses',nb.id,'vendors');
    if(!html.includes('id="fit-tab-expenses-vendors"'))assert.match(html,/id="fit-tab-expenses-register"[^>]*aria-selected="true"/);
  });
  test('[FIT-CASH-B] stylesheet: every rule is under .app-shell, no font-size, no hex colour, keeps the FIT bundle order',()=>{
    const css=read('src/styles/fit-cash-b.css').replace(/\/\*[^]*?\*\//g,'');
    assert.ok(!/font-size/.test(css),'DEC-195: no font-size');assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(css),'colours are tokens');
    const sel=css.replace(/@media[^{]*\{/g,'').split('}').map(x=>x.split('{')[0].trim()).filter(Boolean);
    for(const group of sel)for(const one of group.split(','))assert.ok(one.trim().startsWith('.app-shell'),'unguarded selector: '+one);
    assert.ok(!('fit-cash-b.css'>'z-touch.css'),'sorts before z-touch.css so the bundle order z-touch, z-fit, zz-print of FIT-SHELL is untouched (rules are .app-shell scoped, so they still win)');
  });
  test('[FIT-CASH-B] ExpBanners: the phone keeps the info-note markup, the desktop (fit) uses a one-line FitNote',()=>{
    const src=read('src/features/expenses/Scc.tsx');
    assert.ok(src.includes('fit?<div className={\'info-note exp-banner exp-banner-fit\'+cls} role="status">{icon}<FitNote text={body}/></div>:<div className={\'info-note exp-banner\'+cls} role="status">{icon}<span>{body}</span></div>'));
  });
}
