// CP-1 tests: personal preferences of this device (src/app/prefs.ts), the "Appearance & display" page (desktop + phone), start page,
// list page size, toast duration, density/motion CSS, and the rule that preferences never touch hotel state (DEC-044 extended).
import {build} from 'esbuild';
import {writeFile,mkdir} from 'node:fs/promises';
import {readFileSync} from 'node:fs';
import {buildBundleCss,PRINT_RESET} from '../tools/cssTextScale.mjs';
await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/prefs-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
export * from '../../src/app/prefs';
export {PAGE_CATEGORY,PAGE_KEYWORDS,PAGE_BLURB,HOME_PAGE,buildSearchEntries,searchEntries,navFilterModules} from '../../src/app/navmap';
import {navItems,resolveStartPage as navResolveStartPage} from '../../src/app/nav';
export {navItems,navResolveStartPage};
import {extraPages,mobilePages} from '../../src/app/registry';
export {extraPages,mobilePages};
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {PREFS_PAGE} from '../../src/features/prefs/parts';
export {PREFS_PAGE};
export function renderPrefs(s:any,uid:string,mobile:boolean,section?:string){
  // FIT (lane fit-admin-b): the desktop page shows one section at a time (useFitTab 'prefs'); a section is opened through the remembered tab.
  const store:any=globalThis;const had='sessionStorage' in store,prev=store.sessionStorage;
  if(section)store.sessionStorage={getItem:(k:string)=>k==='hcc-fit-tab:prefs'?section:null,setItem(){},removeItem(){}};
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'prefs',go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'Arrivals',setTab:noop,arrivals:[],departures:[],pending:0,taskList:[],
    roomStatus:noop,switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop,framed:false},
    setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};
  const P=(mobile?mobilePages:extraPages)['prefs'];
  try{return renderToStaticMarkup(<HccContext.Provider value={ctx}><P/></HccContext.Provider>);}
  finally{if(section){if(had)store.sessionStorage=prev;else delete store.sessionStorage;}}
}
`);
await build({entryPoints:['tests/.generated/prefs-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/prefs.mjs',logLevel:'error',jsx:'automatic',
  define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __pfReq} from 'node:module';const require=__pfReq(import.meta.url);"}});
const X=await import('./.generated/prefs.mjs');
const css=readFileSync('src/styles/prefs.css','utf8');
const bundleCss=await buildBundleCss('.');

/** In-memory localStorage installed for one test, then the original global is restored. */
function withStorage(fn,{failGet=false,failSet=false,initial={}}={}){
  const desc=Object.getOwnPropertyDescriptor(globalThis,'localStorage');
  const data=new Map(Object.entries(initial)),writes=[];
  const fake={getItem:k=>{if(failGet)throw new Error('blocked');return data.has(k)?data.get(k):null;},setItem:(k,v)=>{if(failSet)throw new Error('quota');writes.push(k);data.set(k,String(v));},
    removeItem:k=>{writes.push(k);data.delete(k);},clear:()=>data.clear(),key:i=>[...data.keys()][i]??null,get length(){return data.size;}};
  Object.defineProperty(globalThis,'localStorage',{value:fake,configurable:true,writable:true});
  X.resetPrefsCache();
  try{return fn({data,writes});}
  finally{if(desc)Object.defineProperty(globalThis,'localStorage',desc);else delete globalThis.localStorage;X.resetPrefsCache();}
}
const stored=p=>JSON.stringify({v:X.PREFS_VERSION,p});

export function register({test,M,assert}){
  const D=X.PREFS_DEFAULTS;
  test('[PREFS] defaults: Default text, honour system font, follow device motion, comfortable, Modules Home, 100 rows, 6.5 s messages',()=>{
    assert.deepEqual({...D},{textScale:1,honourSystemFont:true,reduceMotion:'system',density:'comfortable',startPage:'',listPageSize:100,toastSeconds:6.5,theme:'light'}); // CP-5 adds theme
    assert.ok(Object.isFrozen(D));assert.equal(X.PREFS_KEY,'hcc-prefs-1');
    withStorage(()=>assert.deepEqual(X.readPrefs(),{...D}));});
  test('[PREFS] round trip: savePrefs merges a change, stores it under hcc-prefs-1 only, readPrefs returns it',()=>withStorage(({data,writes})=>{
    const p=X.savePrefs({textScale:1.3,startPage:'rooms'});assert.equal(p.textScale,1.3);assert.equal(p.listPageSize,100);
    p.density; const q=X.savePrefs({density:'compact'});assert.equal(q.textScale,1.3,'earlier change kept');
    assert.deepEqual([...new Set(writes)],['hcc-prefs-1']);X.resetPrefsCache();
    assert.deepEqual(X.readPrefs(),{...D,textScale:1.3,startPage:'rooms',density:'compact'});
    assert.equal(JSON.parse(data.get('hcc-prefs-1')).v,1);}));
  test('[PREFS] invalid values are ignored field by field (each falls back to its default)',()=>{
    const p=X.sanitizePrefs({textScale:2,honourSystemFont:'yes',reduceMotion:'sometimes',density:'tiny',startPage:'<script>',listPageSize:75,toastSeconds:-1});
    assert.deepEqual(p,{...D});
    const ok=X.sanitizePrefs({textScale:0.9,honourSystemFont:false,reduceMotion:'on',density:'compact',startPage:'cat:front',listPageSize:25,toastSeconds:0,theme:'hc'});
    assert.deepEqual(ok,{textScale:0.9,honourSystemFont:false,reduceMotion:'on',density:'compact',startPage:'cat:front',listPageSize:25,toastSeconds:0,theme:'hc'});
    assert.equal(X.sanitizePrefs({startPage:'x'.repeat(80)}).startPage,'');assert.equal(X.sanitizePrefs({textScale:'1.15'}).textScale,1,'strings are not numbers');});
  test('[PREFS] unknown fields are dropped; null / arrays / garbage give the defaults',()=>{
    assert.ok(!('accent' in X.sanitizePrefs({accent:'blue',textScale:1.15}))); // 'theme' became a real field in CP-5
    assert.equal(X.sanitizePrefs({theme:'neon'}).theme,'light');
    for(const raw of [null,undefined,42,'x',[1,2]])assert.deepEqual(X.sanitizePrefs(raw),{...D});
    assert.deepEqual(X.parsePrefs('{not json'),{...D});assert.deepEqual(X.parsePrefs(''),{...D});assert.deepEqual(X.parsePrefs(null),{...D});});
  test('[PREFS] version bump resets: a stored record of another version is ignored',()=>{
    assert.deepEqual(X.parsePrefs(JSON.stringify({v:2,p:{textScale:1.3}})),{...D});
    assert.deepEqual(X.parsePrefs(JSON.stringify({textScale:1.3})),{...D},'unversioned');
    assert.equal(X.parsePrefs(stored({textScale:1.3})).textScale,1.3);
    withStorage(()=>assert.deepEqual(X.readPrefs(),{...D}),{initial:{'hcc-prefs-1':JSON.stringify({v:0,p:{density:'compact'}})}});});
  test('[PREFS] storage unavailable: reads give the defaults, saves still apply for this session, nothing throws',()=>{
    withStorage(()=>{assert.deepEqual(X.readPrefs(),{...D});const p=X.savePrefs({textScale:1.15});assert.equal(p.textScale,1.15);assert.equal(X.getPrefs().textScale,1.15);},{failGet:true,failSet:true});
    const desc=Object.getOwnPropertyDescriptor(globalThis,'localStorage');
    Object.defineProperty(globalThis,'localStorage',{get(){throw new Error('SecurityError');},configurable:true});X.resetPrefsCache();
    try{assert.deepEqual(X.readPrefs(),{...D});assert.equal(X.savePrefs({density:'compact'}).density,'compact');X.resetPrefs();}
    finally{if(desc)Object.defineProperty(globalThis,'localStorage',desc);else delete globalThis.localStorage;X.resetPrefsCache();}});
  test('[PREFS] resetPrefs removes the stored record and returns the defaults; subscribers are told',()=>withStorage(({data})=>{
    let calls=0;const off=X.subscribePrefs(()=>calls++);
    X.savePrefs({toastSeconds:10});assert.ok(data.has('hcc-prefs-1'));
    assert.deepEqual(X.resetPrefs(),{...D});assert.ok(!data.has('hcc-prefs-1'));assert.equal(calls,2);off();X.savePrefs({toastSeconds:3});assert.equal(calls,2,'unsubscribed');}));
  test('[PREFS] cross-tab sync: a storage event for hcc-prefs-1 re-reads storage and notifies (other keys ignored)',()=>{
    const had=Object.getOwnPropertyDescriptor(globalThis,'window');const handlers={};
    // subscribePrefs hooks window "storage" once per module; give it a stub window if the module has not hooked one yet.
    Object.defineProperty(globalThis,'window',{value:{addEventListener:(t,h)=>{(handlers[t]??=[]).push(h);}},configurable:true,writable:true});
    try{withStorage(({data})=>{
      let calls=0;const off=X.subscribePrefs(()=>calls++);
      assert.ok(handlers.storage?.length===1,'storage listener attached');
      data.set('hcc-prefs-1',stored({textScale:0.9}));
      handlers.storage[0]({key:'hcc-display-mode'});assert.equal(calls,0);
      handlers.storage[0]({key:'hcc-prefs-1'});assert.equal(calls,1);assert.equal(X.getPrefs().textScale,0.9);off();});}
    finally{if(had)Object.defineProperty(globalThis,'window',had);else delete globalThis.window;}});
  test('[PREFS] resolveStartPage: an allowed page opens; a page the user may not open falls back to Modules Home',()=>{
    assert.equal(X.resolveStartPage('Reception','rooms',['rooms','reservations']),'rooms');
    assert.equal(X.resolveStartPage('Housekeeper','reports',['housekeeping','my-hr']),X.HOME_PAGE);
    assert.equal(X.HOME_PAGE,'home');assert.equal(X.navResolveStartPage,X.resolveStartPage,'nav.ts re-exports the same resolver');});
  test('[PREFS] resolveStartPage: empty = role default (Modules Home); Modules Home itself is always allowed',()=>{
    assert.equal(X.resolveStartPage('Owner','',['overview','reports']),'home');assert.equal(X.resolveStartPage('Owner','home',[]),'home');});
  test('[PREFS] start page per demo user: Owner may start on Reports, the Housekeeper falls back (DEC-061 no money pages)',()=>{
    const s=M.seed();const allowed=uid=>X.navFilterModules(s,X.navItems,uid).filter(n=>M.can(s,uid,n.p)).map(n=>n.id);
    assert.equal(X.resolveStartPage('Owner','reports',allowed('owner')),'reports');
    assert.equal(X.resolveStartPage('Housekeeper','reports',allowed('housekeeper')),'home');
    assert.equal(X.resolveStartPage('Housekeeper','housekeeping',allowed('housekeeper')),'housekeeping');
    for(const u of s.users.filter(u=>u.enabled))assert.equal(X.resolveStartPage(u.role,'prefs',allowed(u.id)),'prefs',u.id+' can start on Appearance & display');});
  test('[PREFS] list page size: desktop follows the preference; phones 25 → 25, 50/100 → 50, 200 → 100 (never above 100)',()=>{
    for(const n of X.PAGE_SIZES)assert.equal(X.resolvePageSize(n,false),n);
    assert.deepEqual(X.PAGE_SIZES.map(n=>X.resolvePageSize(n,true)),[25,50,50,100]);
    assert.equal(X.resolvePageSize(D.listPageSize,false),100,'desktop default unchanged');assert.equal(X.resolvePageSize(D.listPageSize,true),50,'phone default unchanged');
    for(const n of X.PAGE_SIZES)assert.ok(X.resolvePageSize(n,true)<=X.PHONE_PAGE_CAP);});
  test('[PREFS] usePaged keeps fixed steps (e.g. 31 closings) and maps only DESKTOP_PAGE / PHONE_PAGE (source check)',()=>{
    const src=readFileSync('src/components/ShowMore.tsx','utf8');
    assert.match(src,/step===DESKTOP_PAGE\?resolvePageSize\(prefs\.listPageSize,false\):step===PHONE_PAGE\?resolvePageSize\(prefs\.listPageSize,true\):step/);
    assert.match(src,/export const DESKTOP_PAGE=100,PHONE_PAGE=50;/);});
  test('[PREFS] toast duration: 3 / 6.5 / 10 s in ms, 0 = until dismissed (null); App uses it instead of the fixed 6500',()=>{
    assert.deepEqual(X.TOAST_SECONDS.map(X.toastMs),[3000,6500,10000,null]);assert.equal(X.toastMs(D.toastSeconds),6500);
    assert.equal(X.toastLabel(0),'Until dismissed');
    const app=readFileSync('src/app/App.tsx','utf8');assert.match(app,/const ms=toastDuration\(prefsRef\.current\.toastSeconds,minMs\);if\(ms!==null\)/);assert.doesNotMatch(app,/setNotice\(null\),6500\)/);});
  test('[PREFS] density tokens never below 44 px on touch; compact CSS stays inside the desktop shell (phone keeps 48 px)',()=>{
    for(const d of X.DENSITIES){const t=X.densityTokens(d);assert.ok(t.dense>=44&&t.primary>=44,d);}
    assert.deepEqual(X.densityTokens('comfortable'),{dense:44,primary:48},'DEC-060 values unchanged by default');
    const vals=[...css.matchAll(/--hcc-touch-(?:dense|primary):(\d+)px/g)].map(m=>+m[1]);assert.ok(vals.length>=2);for(const v of vals)assert.ok(v>=44,'token '+v);
    for(const line of css.split('\n').filter(l=>l.startsWith('[data-density')))assert.match(line,/^\[data-density="compact"\] \.app-shell/,line);
    assert.doesNotMatch(css,/\[data-density[^{]*\.m-shell/);});
  test('[PREFS] reduce motion: on → data-motion="off" (CSS kills transitions/animations), off → "on", system → "system"',()=>{
    assert.equal(X.motionAttr('on'),'off');assert.equal(X.motionAttr('off'),'on');assert.equal(X.motionAttr('system'),'system');
    assert.match(css,/\[data-motion="off"\] \*[^{]*\{transition:none!important;animation:none!important;scroll-behavior:auto!important\}/);});
  test('[PREFS] applyPrefsToDocument sets --hcc-text, data-density and data-motion on <html>; no document = no-op',()=>{
    const props={},attrs={};const doc={documentElement:{style:{setProperty:(k,v)=>{props[k]=v;}},setAttribute:(k,v)=>{attrs[k]=v;}}};
    X.applyPrefsToDocument({...D,textScale:1.15,density:'compact',reduceMotion:'on'},doc);
    assert.deepEqual(props,{'--hcc-text':'1.15'});assert.deepEqual(attrs,{'data-density':'compact','data-motion':'off','data-theme':'light'}); // CP-5 adds data-theme
    X.applyPrefsToDocument({...D},doc);assert.equal(props['--hcc-text'],'1');assert.equal(attrs['data-motion'],'system');
    assert.doesNotThrow(()=>X.applyPrefsToDocument({...D},undefined));});
  test('[PREFS] printing ignores personal text size: the bundle ends with the print reset that pins --hcc-text to 1 with !important (appended by CP-2, tools/cssTextScale.mjs)',()=>{
    assert.ok(bundleCss.trimEnd().endsWith(PRINT_RESET)&&PRINT_RESET.includes('--hcc-text:1!important'));});
  test('[PREFS] text size steps: Small 90 %, Default, Large 115 %, Extra large 130 %; A−/A+ stop at the ends',()=>{
    assert.deepEqual([...X.TEXT_SCALES],[0.9,1,1.15,1.3]);assert.deepEqual(X.TEXT_SCALES.map(X.textScaleLabel),['Small','Default','Large','Extra large']);
    assert.equal(X.stepTextScale(1,1),1.15);assert.equal(X.stepTextScale(1.3,1),1.3);assert.equal(X.stepTextScale(0.9,-1),0.9);assert.equal(X.stepTextScale(1.15,-1),1);});
  test('[PREFS] navigation: prefs is in PAGE_CATEGORY (Admin & Setup) with a blurb and keywords; search "text size" finds it',()=>{
    assert.equal(X.PAGE_CATEGORY.prefs,'admin');assert.ok(X.PAGE_BLURB.prefs);assert.ok(X.PAGE_KEYWORDS.prefs.includes('text size'));
    const hits=X.searchEntries(X.buildSearchEntries(X.navItems),'text size');assert.equal(hits[0].entry.page,'prefs');
    assert.equal(X.searchEntries(X.buildSearchEntries(X.navItems),'appearance')[0].entry.page,'prefs');});
  test('[PREFS] registrations: desktop and phone pages registered; nav item open to every role (any permission)',()=>{
    assert.equal(X.PREFS_PAGE,'prefs');assert.equal(typeof X.extraPages.prefs,'function');assert.equal(typeof X.mobilePages.prefs,'function');
    const item=X.navItems.find(n=>n.id==='prefs');assert.ok(item);assert.equal(item.label,'Appearance & display');
    const s=M.seed();for(const u of s.users.filter(u=>u.enabled))assert.ok(M.can(s,u.id,item.p),u.id);});
  test('[PREFS] desktop page renders every card in order with "This device only"; Housekeeper start-page list has no money pages',()=>{
    // FIT: one section per sub-screen (text, appearance, display, navigation, alerts) in this order + the Reset button in the sticky bar of every section.
    const s=M.seed();const secs=['text','appearance','display','navigation','alerts'];
    const pages=secs.map(id=>X.renderPrefs(s,'owner',false,id));const html=pages.join('');
    ['Text size','Appearance','Device &amp; display (this device only)','Start &amp; navigation','Alerts'].forEach((title,i)=>assert.ok(pages[i].includes('>'+title+'<'),'section '+i+' shows its card heading'));
    for(const p of pages){assert.match(p,/Reset this device/);assert.match(p,/fit-form-actions/);}
    assert.ok((html.match(/This device only/g)||[]).length>=5);assert.match(html,/role="radiogroup" aria-label="Theme"/);assert.match(html,/Follow device/); // CP-5: the Theme row is a real choice
    assert.doesNotMatch(html,/Honour the phone/,'APK-only toggle hidden in the browser');
    assert.match(html,/<option value="reports">/);
    const hk=X.renderPrefs(s,'housekeeper',false,'navigation');assert.doesNotMatch(hk,/value="reports"|value="folios"|value="cashbook"/);assert.match(hk,/value="housekeeping"/);});
  test('[PREFS] phone page renders cards (no tables), 16 px CSS for labels, the display-mode toggle and the reset button',()=>{
    const html=X.renderPrefs(M.seed(),'reception',true);
    assert.doesNotMatch(html,/<table/);assert.match(html,/pf-m-card/);assert.match(html,/display-toggle/);assert.match(html,/Reset this device/);
    assert.match(css,/\.pf-m-page \.pf-seg button,\.m-sheet-form \.pf-seg button\{[^}]*min-height:48px;font-size:16px/);
    assert.match(css,/\.pf-m-label\{font-size:16px/);});
  test('[PREFS] wiring: App applies prefs at boot, honours the start page at sign-in; top bar + phone user menu carry the quick control',()=>{
    const app=readFileSync('src/app/App.tsx','utf8'),sheets=readFileSync('src/app/mobile/sheets.tsx','utf8');
    assert.match(app,/export function App\(\)\{\n  useApplyPrefs\(\);/);assert.match(app,/function signIn\(id:string\)\{[^\n]*startFor\(s,id\)/);
    assert.match(app,/<TextSizeQuick\/>\n\s*<DisplayToggle display=\{display\} compact\/>/);
    assert.match(sheets,/<DisplayToggle display=\{display\}\/><\/div>\n\s*<PrefsQuickSheet\/>/);
    assert.match(readFileSync('src/app/context.tsx','utf8'),/prefs\?:import\('\.\/prefs'\)\.PrefsApi;/);});
  test('[PREFS] no hotel-state field touched: saving and resetting preferences leaves the demo state identical',()=>withStorage(({writes})=>{
    const s=M.seed();const before=JSON.stringify(s);
    X.savePrefs({textScale:1.3,density:'compact',startPage:'rooms',listPageSize:200,toastSeconds:0,reduceMotion:'on',honourSystemFont:false});X.resetPrefs();
    assert.equal(JSON.stringify(s),before);assert.ok(!('prefs' in s));
    assert.ok(writes.every(k=>k==='hcc-prefs-1'),'only the prefs key is written');
    const src=readFileSync('src/app/prefs.ts','utf8');assert.doesNotMatch(src,/\bexecute\(|\brun\(|fetch\(|services\/mock/,'never a command, never the server');}));
}
