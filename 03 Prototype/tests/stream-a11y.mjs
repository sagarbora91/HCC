// Accessibility stream (audit A11y_Performance_Audit_2026-10-25): source/CSS checks, contrast maths, focus-trap logic.
import {readFileSync,readdirSync,statSync} from 'node:fs';
import {build} from 'esbuild';
import {mkdir} from 'node:fs/promises';
const read=f=>readFileSync(f,'utf8');
const sheets=['src/styles.css',...readdirSync('src/styles').filter(f=>f.endsWith('.css')).sort().map(f=>'src/styles/'+f)];
const lum=h=>{const c=[1,3,5].map(i=>parseInt(h.slice(i,i+2),16)/255).map(v=>v<=.03928?v/12.92:((v+.055)/1.055)**2.4);return .2126*c[0]+.7152*c[1]+.0722*c[2];};
export const ratio=(a,b)=>{const x=lum(a),y=lum(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);};
function walk(d,o=[]){for(const f of readdirSync(d)){const p=d+'/'+f;if(statSync(p).isDirectory())walk(p,o);else if(p.endsWith('.tsx'))o.push(p);}return o;}
/** Brace/quote-aware scan for button elements with an icon child, no visible text and no accessible name. Returns line numbers. */
function unnamedIconButtons(src){
  const out=[];let i=0;
  const icons=new Set();for(const m of src.matchAll(/import\s*\{([^}]*)\}\s*from\s*'lucide-react'/g))for(const n of m[1].split(','))icons.add(n.trim().split(/\s+as\s+/).pop());
  while((i=src.indexOf('<button',i))>=0){
    const after=src[i+7];if(!/[\s>]/.test(after)){i+=7;continue;}
    let j=i+7,depth=0,q='';
    for(;j<src.length;j++){const c=src[j];
      if(q){if(c===q&&src[j-1]!=='\\')q='';continue;}
      if(c==='"'||c==="'"||c==='`'){q=c;continue;}
      if(c==='{')depth++;else if(c==='}')depth--;
      else if(c==='>'&&depth===0&&src[j-1]!=='='){break;}
    }
    const attrs=src.slice(i+7,j);
    if(attrs.endsWith('/')){i=j;continue;}
    const end=src.indexOf('</button>',j);if(end<0)break;
    const inner=src.slice(j+1,end);
    if(!/aria-label|aria-labelledby|title=/.test(attrs)){
      const text=inner.replace(/<[A-Z][\w.]*\b(?:[^>{]|\{[^}]*\})*\/>/g,'').replace(/<\/?[a-z][^>]*>/g,'');
      const hasText=/[A-Za-z0-9]/.test(text)||/aria-label/.test(inner);
      if(!hasText&&[...inner.matchAll(/<([A-Z]\w*)/g)].some(m=>icons.has(m[1])))out.push(src.slice(0,i).split('\n').length);
    }
    i=end;
  }
  return out;
}
await mkdir('tests/.generated',{recursive:true});
await build({entryPoints:['src/components/ui.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/ui-a11y.mjs',external:['react','react-dom','lucide-react'],logLevel:'error'});
global.document={activeElement:null,body:{}};
const UI=await import('./.generated/ui-a11y.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
delete global.document;
export function register({test,assert}){
  test('[A11Y-01] ModalHost returns focus to the opener, moves focus in, traps Tab, Escape goes through closeModal',()=>{
    const m=read('src/app/ModalHost.tsx');
    assert.match(m,/opener=React\.useRef/);assert.match(m,/document\.activeElement/);assert.match(m,/el\.focus\(\)/);
    assert.match(m,/el\.isConnected/);
    assert.match(m,/trapTab\(e,e\.currentTarget\)/);
    assert.match(m,/e\.key==='Escape'\)\{e\.preventDefault\(\);closeModal\(\)/);
    assert.doesNotMatch(m,/autoFocus/);
    assert.match(m,/aria-labelledby/);
    // AC-04: submit still clears the dirty flag so no "Discard" prompt follows a successful submit
    assert.match(m,/onSubmitCapture=\{\(\)=>setDirty\(false\)\}/);
    const app=read('src/app/App.tsx');
    assert.match(app,/AC-04/);assert.match(app,/closeModal=\(\)=>\{if\(dirty\.current&&!window\.confirm/);
  });
  test('[A11Y-02] drawer, search palette and exit dialog trap Tab and give focus back',()=>{
    for(const f of ['src/app/NavDrawer.tsx','src/app/SearchPalette.tsx']){const t=read(f);assert.match(t,/useReturnFocus\(\)/,f);assert.match(t,/trapTab\(/,f);}
    assert.match(read('src/app/App.tsx'),/<ExitAsk /);
  });
  test('[A11Y-03] trapTab wraps focus at both ends (stub DOM)',()=>{
    const focused=[];
    const mk=n=>({n,offsetWidth:10,offsetHeight:10,focus(){global.document.activeElement=this;focused.push(n);}});
    const a=mk('a'),b=mk('b'),c=mk('c');
    const root={contains:x=>[a,b,c].includes(x),querySelectorAll:()=>[a,b,c],focus(){}};
    global.document={activeElement:c,body:{}};
    const {trapTab}=UI;
    const ev=shift=>({key:'Tab',shiftKey:shift,preventDefault(){}});
    assert.equal(trapTab(ev(false),root),true);assert.equal(focused[focused.length-1],'a');
    global.document.activeElement=a;assert.equal(trapTab(ev(true),root),true);assert.equal(focused[focused.length-1],'c');
    global.document.activeElement=b;assert.equal(trapTab(ev(false),root),false);assert.equal(trapTab(ev(true),root),false);
    assert.equal(trapTab({key:'Enter',shiftKey:false,preventDefault(){}},root),false);
    delete global.document;
  });
  test('[A11Y-04] ExitAsk renders a named, described alertdialog',()=>{
    const {ExitAsk}=UI;
    global.document={activeElement:null,body:{}};
    const html=renderToStaticMarkup(React.createElement(ExitAsk,{onStay(){},onExit(){}}));
    delete global.document;
    assert.match(html,/role="alertdialog"/);assert.match(html,/aria-modal="true"/);assert.match(html,/aria-labelledby="exit-ask-title"/);assert.match(html,/id="exit-ask-title"/);
  });
  test('[A11Y-05] focus ring has a :focus fallback (Chrome 83) and sign-in / tile / drawer / search fields keep a visible ring',()=>{
    const a=read('src/styles/y-a11y.css');
    assert.match(a,/button:focus,a:focus,input:focus,select:focus,textarea:focus/);
    assert.match(a,/outline:3px solid #7a1f2b/);
    for(const s of ['.signin-user:focus','.tile:focus','.drawer-item:focus','.yld-cell:focus'])assert.ok(a.includes(s),s);
    assert.match(a,/\.search:focus-within/);assert.match(a,/\.search-field:focus-within/);assert.match(a,/\.cfg-search:focus-within/);
    assert.ok(a.indexOf('button:focus,a:focus')<a.indexOf(':focus:not(:focus-visible)'));
    const sorted=readdirSync('src/styles').filter(f=>f.endsWith('.css')).sort();
    const idx=sorted.indexOf('y-a11y.css');
    assert.ok(idx>sorted.indexOf('home.css')&&idx>sorted.indexOf('m-yield.css')&&idx>sorted.indexOf('reservations.css')&&idx<sorted.indexOf('z-touch.css'));
  });
  test('[A11Y-06] Chrome 83 safe: vh before dvh, no inset / :is / .at() / replaceAll in the a11y sources',()=>{
    for(const f of sheets){const t=read(f);for(const m of t.matchAll(/([a-z-]+):([^;{}]*dvh[^;{}]*)/g)){const vh=m[2].replace(/dvh/g,'vh');assert.ok(t.includes(m[1]+':'+vh+';'+m[1]+':'+m[2]),f+' '+m[0]);}}
    assert.doesNotMatch(read('src/styles/y-a11y.css'),/\binset\s*:|:is\(|:where\(|:has\(/);
    for(const f of ['src/components/ui.tsx','src/app/ModalHost.tsx','src/app/NavDrawer.tsx','src/app/SearchPalette.tsx']){assert.doesNotMatch(read(f),/\.at\(-?\d|replaceAll\(|structuredClone/,f);}
  });
  test('[A11Y-07] contrast: control borders >= 3:1, ring >= 3:1, disabled label >= 4.5:1 (before 1.9 / 3.65)',()=>{
    assert.ok(ratio('#cdb9a3','#ffffff')<3,'old border was below 3:1');
    for(const bg of ['#ffffff','#faf6f0'])assert.ok(ratio('#8c7766',bg)>=3,'border on '+bg);
    for(const bg of ['#ffffff','#faf6f0','#efe4d6'])assert.ok(ratio('#7a1f2b',bg)>=3,'ring on '+bg);
    assert.ok(ratio('#ffffff','#7a1f2b')>=3,'white ring on maroon app bar');
    assert.ok(ratio('#bd8f95','#7a1f2b')<4.5,'old disabled label');
    assert.ok(ratio('#5c4a3e','#efe4d6')>=4.5,'disabled label');
    const a=read('src/styles/y-a11y.css');
    assert.ok(a.includes('#8c7766')&&a.includes('background:#efe4d6;color:#5c4a3e')&&a.includes('1px dashed'));
  });
  test('[A11Y-08] every icon-only button in src/**/*.tsx has an accessible name (scan)',()=>{
    const bad=[];for(const f of walk('src'))for(const l of unnamedIconButtons(read(f)))bad.push(f+':'+l);
    assert.deepEqual(bad,[]);
    assert.equal(unnamedIconButtons("import {X} from 'lucide-react';"+'<button onClick={()=>go(1)}><X size={18}/></button>').length,1);
    assert.equal(unnamedIconButtons("import {X} from 'lucide-react';"+'<button aria-label="Close" onClick={()=>go(1)}><X size={18}/></button>').length,0);
    assert.equal(unnamedIconButtons("import {X} from 'lucide-react';"+'<button onClick={()=>go(1)}><X size={18}/> Save</button>').length,0);
  });
  test('[A11Y-09] repeated buttons have unique names; room tiles show a letter; one h1 on phone; skip link and page titles',()=>{
    assert.ok(read('src/features/cashbook/CashBook.tsx').includes('aria-label={`Open day ${dateLabel(d.date)}`}'));
    assert.ok(read('src/features/housekeeping/Housekeeping.tsx').includes('aria-label={`Room details, room ${t.room}`}'));
    assert.ok(read('src/features/guests/CheckIn.tsx').includes("'Start check-in'}, ${b.guest}`}"));
    const o=read('src/features/overview/Overview.tsx');assert.match(o,/ROOM_CODE\[roomStatus\(r\)\]/);assert.match(o,/mr-code/);
    assert.doesNotMatch(read('src/app/mobile/MobileShell.tsx'),/<h1 className="m-title"/);
    const app=read('src/app/App.tsx');assert.match(app,/skip-link/);assert.match(app,/id="main-content"/);assert.match(app,/document\.title=/);
  });
  test('[A11Y-10] icons hidden from AT and table headers scoped by the enhancer',()=>{
    const u=read('src/components/ui.tsx');assert.match(u,/svg\.lucide:not\(\[aria-hidden\]\)/);assert.match(u,/thead th:not\(\[scope\]\)/);
    assert.match(read('src/main.tsx'),/installA11yEnhancer\(\)/);
  });
}
