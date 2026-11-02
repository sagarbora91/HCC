// FIT-2 SHELL (owner rule after FIT, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §12: "lists scroll, tasks are sub-screens with a result
// rail"): the new optional props of the shell components — StepFlow / FormSections `summary`, SplitView list-only + Back at ≥ 900 px,
// FitTabs "More ▾" overflow menu, StatStrip `compact` / `collapsible` / `collapseOnTablet`, FitNote More only when cut — render as documented,
// old call sites render exactly as before, and z-fit.css adds no font size (DEC-195).
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-shell-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit2-shell.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit2-shell.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const css=read('src/styles/z-fit.css');
const src=read('src/components/fit.tsx');
const noComments=t=>t.replace(/\/\*[^]*?\*\//g,'');
const nav=page=>({desktop:true,page,tabKey:'',seq:1,setTabCrumb(){}});
const inNav=(n,el)=>h(F.FitNavContext.Provider,{value:n},el);
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const withWidth=(narrow,fn)=>{const had='window' in globalThis,prev=globalThis.window;
  globalThis.window={matchMedia:q=>({matches:q===F.TABLET_WIDE?narrow:false,addEventListener(){},removeEventListener(){}})};
  try{return fn();}finally{if(had)globalThis.window=prev;else delete globalThis.window;}};

export function register({test,assert}){
  test('[FIT-2] StepFlow summary: one-line result under each label in the rail; has-summary widens the rail to ~340 px at ≥ 1280 px; no summary = old markup',()=>{
    const steps=[{id:'a',label:'Arrivals',status:'done',summary:'All 4 checked in'},{id:'b',label:'Cash',status:'current',note:'2 left',summary:h('b',null,'₹1,200 short')},{id:'c',label:'Close',status:'waiting'}];
    const sf=renderToStaticMarkup(h(F.StepFlow,{steps,value:'b',onChange(){}},'body'));
    assert.ok(sf.startsWith('<div class="fit-steps has-summary">'),sf);
    assert.ok(sf.includes('<strong>1. Arrivals</strong><small>done</small><span class="fit-rail-sum" title="All 4 checked in">All 4 checked in</span>'),sf);
    assert.ok(sf.includes('<small>2 left · current step</small><span class="fit-rail-sum"><b>₹1,200 short</b></span>'),'a node summary has no title');
    assert.ok(sf.includes('<strong>3. Close</strong><small>waiting</small></span>'),'a step without summary has no summary line');
    assert.ok(sf.includes('<div class="fit-step-body">body</div>'),'the active step body is unchanged');
    const old=renderToStaticMarkup(h(F.StepFlow,{steps:[{id:'a',label:'A',status:'done'}],value:'a',onChange(){}},'x'));
    assert.ok(old.startsWith('<div class="fit-steps">')&&!old.includes('fit-rail-sum'),old);
    assert.ok(css.includes('@media (min-width:1280px){.fit-steps.has-summary:where(:not(.fit-page)),.fit-form.has-summary:where(:not(.fit-page)){grid-template-columns:minmax(260px,340px) minmax(0,1fr)}}'));
    assert.ok(css.includes('.fit-rail-sum{display:block;max-width:100%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;'),'one line, ellipsis');
  });
  test('[FIT-2] FormSections summary: label + current value under it; dirty dot kept; no summary = old markup',()=>{
    const fs=renderToStaticMarkup(h(F.FormSections,{sections:[{id:'g',label:'General',summary:'Hotel Parth · GSTIN set'},{id:'m',label:'Money',dirty:true}],value:'g',onChange(){},actions:h('button',{className:'btn primary'},'Save')},'fields'));
    assert.ok(fs.startsWith('<div class="fit-form has-summary">'),fs);
    assert.ok(fs.includes('<button type="button" class="on" aria-current="true"><span class="fit-form-nav-text"><span>General</span><span class="fit-rail-sum" title="Hotel Parth · GSTIN set">Hotel Parth · GSTIN set</span></span></button>'),fs);
    assert.ok(fs.includes('<button type="button" class="">Money<span class="fit-dirty" aria-label="unsaved changes">•</span></button>'),'a section without summary renders as before');
    assert.ok(fs.includes('<div class="fit-form-actions"><button class="btn primary">Save</button></div>'),'save bar unchanged');
    const old=renderToStaticMarkup(h(F.FormSections,{sections:[{id:'a',label:'A'}],value:'a',onChange(){},actions:null},'x'));
    assert.ok(old.startsWith('<div class="fit-form">')&&!old.includes('fit-rail-sum'));
  });
  test('[FIT-2] SplitView: no record → list-only (list takes all columns ≥ 900 px, pane hidden); record → has-detail + Back to list at every width; emptyPane="show" keeps the old two columns',()=>{
    const empty=renderToStaticMarkup(h(F.SplitView,{list:'L',detail:null,empty:'Pick one',onCloseDetail(){},label:'Folios'}));
    assert.ok(empty.startsWith('<div class="fit-split list-only" aria-label="Folios" role="group"'),empty);
    assert.ok(!empty.includes('fit-split-back'));
    const open=renderToStaticMarkup(h(F.SplitView,{list:'L',detail:'Record',empty:'Pick one',onCloseDetail(){},label:'Folios'}));
    assert.ok(open.startsWith('<div class="fit-split has-detail"')&&open.includes('fit-split-back')&&!open.includes('list-only'),open);
    const keep=renderToStaticMarkup(h(F.SplitView,{list:'L',detail:null,empty:'Pick one',onCloseDetail(){},label:'Docs',emptyPane:'show'}));
    assert.ok(keep.startsWith('<div class="fit-split" ')&&keep.includes('>Pick one<'),keep);
    for(const k of ['.fit-split.list-only:where(:not(.fit-page)){grid-template-columns:minmax(0,1fr)}','.fit-split.list-only>.fit-split-detail{display:none}',
      '.fit-split.has-detail .fit-split-back{display:inline-flex;'])assert.ok(css.slice(css.indexOf('@media (min-width:900px){\n  .fit-split.list-only')).includes(k),k);
    assert.ok(css.includes('.fit-split.has-detail .fit-split-detail{display:flex;position:absolute;'),'below 900 px the slide-over stays');
  });
  test('[FIT-2] FitTabs overflow: splitTabs keeps the first n and always the selected tab; the bar renders a keyboard "More ▾" menu button only when tabs are hidden',()=>{
    const tabs=['a','b','c','d','e'].map(id=>({id,label:id.toUpperCase()}));
    let r=F.splitTabs(tabs,5,'a');assert.deepEqual([...r.visible],['a','b','c','d','e']);assert.equal(r.hidden.length,0);
    r=F.splitTabs(tabs,3,'b');assert.deepEqual([...r.visible].sort(),['a','b','c']);assert.deepEqual(r.hidden.map(t=>t.id),['d','e']);
    r=F.splitTabs(tabs,3,'e');assert.deepEqual([...r.visible].sort(),['a','b','e'],'the selected tab takes the last place');assert.deepEqual(r.hidden.map(t=>t.id),['c','d']);
    r=F.splitTabs(tabs,0,'a');assert.deepEqual([...r.visible],['a'],'at least one tab stays');
    const html=renderToStaticMarkup(h(F.FitTabs,{page:'pos',value:'a',onChange(){},tabs}));
    assert.ok(html.startsWith('<div class="fit-tabs-bar tabs" role="tablist" aria-label="Sections of this page">')&&!html.includes('fit-tabs-more'),'everything fits before measuring: markup unchanged');
    for(const k of ['aria-haspopup="menu"','role="menu"','role="menuitem"',"e.key==='Escape'","e.key==='ArrowDown'","e.key==='ArrowUp'",'moreBtn.current?.focus()',
      "if((e.target as HTMLElement).getAttribute('role')!=='tab')return;",'if(n>1&&el.scrollWidth>el.clientWidth+1)setN(n-1);'])assert.ok(src.includes(k),k);
    assert.ok(css.includes('.app-shell .fit-tabs-bar:has(.fit-tabs-pop){overflow:visible}'),'the open menu is not clipped by the sideways bar');
    assert.ok(css.includes('.app-shell .fit-tabs-bar .fit-tabs-more-btn,.app-shell .fit-tabs-pop>[role=menuitem]{min-height:var(--hcc-touch-primary)!important}'),'48 px on touch');
  });
  test('[FIT-2] StatStrip: compact one-line variant; collapsible toggle remembered per page in sessionStorage; collapseOnTablet starts compact at ≤ 1180 px; old call sites unchanged',()=>{
    const items=[{title:'Arrivals',value:'4'}];
    assert.equal(renderToStaticMarkup(h(F.StatStrip,{items})),'<div class="fit-stats" role="list" aria-label="Key figures"><div role="listitem" class="stat fit-stat"><div class="stat-label">Arrivals</div><strong>4</strong></div></div>');
    assert.ok(renderToStaticMarkup(h(F.StatStrip,{items,compact:true})).startsWith('<div class="fit-stats fit-stats-compact" role="list"'));
    assert.ok(renderToStaticMarkup(h(F.StatStrip,{items,compact:true,variant:'small',className:'hov-stats'})).startsWith('<div class="small-stats fit-small-stats hov-stats fit-stats-compact"'));
    store.clear();assert.equal(F.fitStatsKey('cashbook'),'hcc-fit-stats:cashbook');
    const r=(props,narrow=false)=>withWidth(narrow,()=>withSession(()=>renderToStaticMarkup(inNav(nav('cashbook'),h(F.StatStrip,{items,...props})))));
    let x=r({collapsible:true});
    assert.ok(x.startsWith('<div class="fit-stats-wrap"><div class="fit-stats" role="list"')&&x.includes('class="icon-btn fit-stats-toggle" aria-label="Show key figures on one line"'),x);
    x=r({collapseOnTablet:true},true);assert.ok(x.startsWith('<div class="fit-stats-wrap compact"><div class="fit-stats fit-stats-compact"')&&x.includes('aria-label="Show key figures in full"'),'tab-land: compact by default');
    assert.ok(!r({collapseOnTablet:true},false).includes('fit-stats-compact'),'laptop: full by default');
    store.set('hcc-fit-stats:cashbook','full');assert.ok(!r({collapseOnTablet:true},true).includes('fit-stats-compact'),'the remembered choice wins');
    store.set('hcc-fit-stats:cashbook','compact');assert.ok(r({collapsible:true}).includes('fit-stats-compact'),'remembered compact');
    assert.ok(r({collapsible:true,page:'other'}).startsWith('<div class="fit-stats-wrap"><div class="fit-stats" '),'the page prop picks the key');
    assert.ok(src.includes("const flip=()=>{const next=compact?'full':'compact';if(page)writeStats(page,next);setMode(next);};"),'a toggle is remembered');
    assert.equal(F.TABLET_WIDE,'(max-width: 1180px)');
    assert.ok(css.includes('.app-shell .fit-stats.fit-stats-compact .fit-stat{flex-direction:row;align-items:baseline;'),'one line per figure');
  });
  test('[FIT-2] FitNote: full text and no More until measured as cut; measured before paint and on box resizes',()=>{
    assert.equal(renderToStaticMarkup(h(F.FitNote,{text:'Short'})),'<div class="fit-note footnote"><p class="fit-note-text">Short</p></div>');
    assert.ok(src.includes('const [long,setLong]=useState(false);'));
    assert.ok(src.includes('const m=()=>setLong(el.scrollWidth>el.clientWidth+1||el.scrollHeight>el.clientHeight+1);m();'));
    assert.ok(src.includes('ro.observe(el);if(el.parentElement)ro.observe(el.parentElement);'));
  });
  test('[FIT-2] DEC-195: z-fit.css still sets no font size outside h1 / inherit / kbd and the restoration section; the contract has §12',()=>{
    const a=css.indexOf('DEC-195 restorations'),b=css.indexOf('/* §5 spacing');
    const sizes=[...noComments(css.slice(0,a)+css.slice(b)).matchAll(/font-size:([^;}]+)/g)].map(m=>m[1]).sort();
    assert.deepEqual(sizes,['12px','22px','inherit']);
    const c=read('../02 Planning/agents/FIT-LAYOUT-CONTRACT.md');
    assert.ok(c.includes('## 12. FIT-2: lists scroll, tasks are sub-screens with a result rail'));
    for(const k of ['summary?:React.ReactNode','emptyPane?:','collapseOnTablet?:boolean','splitTabs'])assert.ok(c.includes(k),k);
  });
}
