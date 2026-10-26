// CP-5 · Themes (Light, Dark, High contrast, Follow device) on the CP-3 colour tokens. Names prefixed [THEME].
// Static resolution (no DOM): every var() in the stylesheets is resolved per theme from styles.css :root + 0-tokens.css + the roles,
// aliases and generated overrides of tools/themes.mjs, and every colour/background rule is checked (dark AA 4.5:1, high contrast AAA 7:1).
import {build} from 'esbuild';
import {readFileSync,existsSync,readdirSync} from 'node:fs';
import {writeFile,mkdir} from 'node:fs/promises';
import {buildBundleCss} from '../tools/cssTextScale.mjs';
import {applyRoles,FILL_ROLES,parseTokenSheet,tokenSheetFile,TOKEN_SHEET,SEMANTIC} from '../tools/cssTokens.mjs';
import * as T from '../tools/themes.mjs';

await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/themes-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
export * from '../../src/app/prefs';
export {styleStatusBar,STATUS_BAR_COLOURS,resetNativeCache} from '../../src/app/native';
export {THEME_OPTIONS,themeHelp} from '../../src/features/prefs/parts';
import {extraPages,mobilePages} from '../../src/app/registry';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
export function renderPrefs(s:any,uid:string,mobile:boolean){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'prefs',go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'Arrivals',setTab:noop,arrivals:[],departures:[],pending:0,taskList:[],
    roomStatus:noop,switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop,framed:false},
    setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};
  const P=(mobile?mobilePages:extraPages)['prefs'];
  return renderToStaticMarkup(<HccContext.Provider value={ctx}><P/></HccContext.Provider>);
}
`);
await build({entryPoints:['tests/.generated/themes-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/themes.mjs',logLevel:'error',jsx:'automatic',
  define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __thReq} from 'node:module';const require=__thReq(import.meta.url);"}});
const X=await import('./.generated/themes.mjs');

const SHEETS=['src/styles.css',...readdirSync('src/styles').filter(f=>f.endsWith('.css')).sort().map(f=>'src/styles/'+f)];
const read=f=>readFileSync(f,'utf8');
const sheetText=read(TOKEN_SHEET);
const tokens=parseTokenSheet(sheetText);
const src=await T.usageSources('.');
const usage=T.tokenUsage(src);
const stylesRoot=T.stylesRootVars(read('src/styles.css'));
const ctx={tokens,usage,stylesRoot};
const vars=Object.fromEntries(T.THEMES.map(t=>[t,T.themeVars(t,ctx)]));
const audit=Object.fromEntries(T.THEMES.map(t=>[t,T.contrastAudit(src,vars[t])]));
const lightLow=new Set(audit.light.pairs.filter(p=>p.ratio<4.5).map(p=>p.idx));
const themesCss=read(T.THEMES_FILE);
const bundle=await buildBundleCss('.');
const HEX=/^#[0-9a-f]{6}([0-9a-f]{2})?$/;
const hueDiff=(a,b)=>{const d=Math.abs(T.oklch(a)[2]-T.oklch(b)[2])*180/Math.PI;return Math.min(d,360-d);};

function withStorage(fn){
  const desc=Object.getOwnPropertyDescriptor(globalThis,'localStorage');const data=new Map();
  Object.defineProperty(globalThis,'localStorage',{value:{getItem:k=>data.has(k)?data.get(k):null,setItem:(k,v)=>data.set(k,String(v)),removeItem:k=>data.delete(k)},configurable:true,writable:true});
  X.resetPrefsCache();
  try{return fn(data);}finally{if(desc)Object.defineProperty(globalThis,'localStorage',desc);else delete globalThis.localStorage;X.resetPrefsCache();}
}
function fakeDoc(){
  const attrs={},meta={};
  return {attrs,meta,doc:{documentElement:{style:{setProperty(){}},setAttribute:(k,v)=>{attrs[k]=v;}},querySelector:s=>s==='meta[name="theme-color"]'?{setAttribute:(k,v)=>{meta[k]=v;}}:null}};
}

export function register({test,assert,M}){
  test('[THEME] one token sheet: 0-tokens.css is unique, sorted, canonical; the CP-3 sheets and tokenizer copies are gone',()=>{
    assert.equal(sheetText,tokenSheetFile(tokens));
    const keys=[...tokens.keys()];assert.deepEqual(keys,[...keys].sort());assert.equal(new Set(keys).size,keys.length);
    assert.equal(keys.length,sheetText.split('\n').filter(l=>l.startsWith('--c-')).length);
    for(const f of ['src/styles/0-tokens-a.css','src/styles/0-tokens-b.css','src/styles/0-tokens-c.css','tools/cssTokens.b.mjs','tools/cssTokens.c.mjs'])assert.ok(!existsSync(f),f);
    assert.deepEqual(readdirSync('src/styles').filter(f=>f.startsWith('0-')),['0-tokens.css']);});
  test('[THEME] token sheet complete: every var(--c-…) in every stylesheet is defined, every definition is used',()=>{
    const used=new Set();for(const f of SHEETS)for(const m of read(f).matchAll(/var\((--c-[0-9a-f]+)\)/g))used.add(m[1]);
    for(const u of used)assert.ok(tokens.has(u),u);for(const k of tokens.keys())assert.ok(used.has(k),k+' unused');
    assert.ok(tokens.size>=290,'tokens '+tokens.size);});
  test('[THEME] themes.css is exactly what tools/themes.mjs generates (run node tools/themes.mjs --write after a stylesheet colour change)',()=>{
    assert.equal(themesCss,T.themesCss(tokens,usage));});
  test('[THEME] light theme unchanged: every --c- token and every role resolves to the very hex it had before (aliases included)',()=>{
    for(const [k,v] of tokens)assert.equal(T.resolveVars(`var(${k})`,vars.light),v,k);
    for(const [hex,role] of T.ALIASES){assert.equal(T.roleValue(role,'light'),'#'+hex,role);assert.ok(tokens.has('--c-'+hex),hex);}
    for(const [hex,name] of SEMANTIC)assert.equal(T.roleValue(name,'light'),hex,name);
    for(const [name,val] of stylesRoot)if(T.ROLE_LIGHT.has(name))assert.equal(T.ROLE_LIGHT.get(name),val,name);});
  test('[THEME] role rewrites: each fill / on-fill role has the light value of the colour it replaced; applyRoles leaves nothing to convert',()=>{
    for(const [from,to] of FILL_ROLES)assert.equal(T.resolveVars(`var(${to})`,vars.light),T.resolveVars(`var(${from})`,vars.light),to);
    assert.equal(T.roleValue('--on-primary','light'),'#ffffff');assert.equal(T.roleValue('--on-primary-soft','light'),T.roleValue('--primary-tint','light'));
    for(const r of T.REWRITE_ROLES)assert.ok(T.ROLE_LIGHT.has(r),r);
    for(const f of SHEETS)assert.equal(applyRoles(read(f)),read(f),f+' has a fill / white-text spot not moved to its role');
    const uses=SHEETS.map(read).join('\n');for(const r of T.REWRITE_ROLES)assert.ok(uses.includes(`var(${r})`),r+' unused');});
  test('[THEME] applyRoles: fills behind white text, borders of filled rules, white text and pale text on maroon; white surfaces untouched',()=>{
    const css='.a{background:var(--primary);border-color:var(--primary);color:var(--c-ffffff)}.b{color:var(--primary);border:1px solid var(--primary)}.c{background:var(--c-ffffff);color:var(--c-ffffff)}.d p{color:var(--primary-tint)}.e{background:linear-gradient(1deg,var(--primary-dark),var(--c-2e6b34))}';
    assert.equal(applyRoles(css),'.a{background:var(--primary-fill);border-color:var(--primary-fill);color:var(--on-primary)}.b{color:var(--primary);border:1px solid var(--primary)}.c{background:var(--c-ffffff);color:var(--c-ffffff)}.d p{color:var(--on-primary-soft)}.e{background:linear-gradient(1deg,var(--primary-fill-dark),var(--success-fill))}');
    assert.equal(applyRoles(applyRoles(css)),applyRoles(css));});
  test('[THEME] every role has a hex in light, dark and hc; role references point at roles; alias roles exist',()=>{
    for(const [n] of T.ROLES)for(const t of T.THEMES)assert.match(T.roleValue(n,t),/^#[0-9a-f]{6}$/,n+' '+t);
    for(const [,r] of T.ROLE_REFS)assert.ok(T.ROLE_LIGHT.has(r),r);
    for(const r of T.ALIASES.values())assert.ok(T.ROLE_LIGHT.has(r),r);
    assert.equal(new Set(T.ROLES.map(r=>r[0])).size,T.ROLES.length,'role names unique');});
  test('[THEME] every --c- token has a dark and a high-contrast value (its own, or through its role)',()=>{
    for(const t of ['dark','hc'])for(const k of tokens.keys())assert.match(T.resolveVars(`var(${k})`,vars[t]),HEX,k+' '+t);
    const gen=T.generatedValues(tokens,usage);
    assert.equal(gen.dark.size+[...T.ALIASES.keys()].filter(h=>tokens.has('--c-'+h)).length,tokens.size);
    for(const k of gen.dark.keys()){assert.ok(themesCss.includes(`${k}:${gen.dark.get(k)};`),k+' dark');assert.ok(themesCss.includes(`${k}:${gen.hc.get(k)};`),k+' hc');}});
  test('[THEME] dark: every colour/background rule resolvable in the bundle is ≥ 4.5:1 (0 failures)',()=>{
    const low=audit.dark.pairs.filter(p=>p.ratio<4.5&&!lightLow.has(p.idx)).map(p=>`${p.sel} ${p.fg}/${p.bg} ${p.ratio}`);
    assert.deepEqual(low,[]);assert.ok(audit.dark.pairs.length>=300,'pairs '+audit.dark.pairs.length);});
  test('[THEME] high contrast: every colour/background rule resolvable in the bundle is ≥ 7:1 (AAA, 0 failures)',()=>{
    const low=audit.hc.pairs.filter(p=>p.ratio<7&&!lightLow.has(p.idx)).map(p=>`${p.sel} ${p.fg}/${p.bg} ${p.ratio}`);
    assert.deepEqual(low,[]);assert.ok(audit.hc.pairs.length>=300);});
  test('[THEME] the only pairs below 4.5:1 in light are the hidden white-on-white ticks (not text), so no theme needs an exception for text',()=>{
    assert.deepEqual(audit.light.pairs.filter(p=>p.ratio<4.5).map(p=>p.sel).sort(),['.hov-check','.mo-box']);});
  test('[THEME] pairs that cannot be resolved statically are only transparent / none / inherit / gradients / translucent (listed, no unknown variable)',()=>{
    for(const t of ['dark','hc'])for(const u of audit[t].unresolved){
      assert.ok(!/var\(/.test(u.fg+u.bg),`${t} ${u.sel}: ${u.fg} / ${u.bg}`);
      assert.ok(/^(transparent|none|inherit)$|gradient|^#[0-9a-f]{8}$/.test(u.bg)||u.fg==='inherit',`${t} ${u.sel}: ${u.bg}`);}
    assert.ok(audit.dark.unresolved.length<=50,'unresolved '+audit.dark.unresolved.length);});
  test('[THEME] inherited text: every text role on every surface role ≥ 4.5:1 in dark and ≥ 7:1 in high contrast',()=>{
    assert.deepEqual(T.textOnSurfaces('dark',vars.dark,4.5),[]);assert.deepEqual(T.textOnSurfaces('hc',vars.hc,7),[]);});
  test('[THEME] solid fills: white text ≥ 4.5:1 (dark) / 7:1 (hc) on every fill; dark fills stand out ≥ 3:1 from the page',()=>{
    const fills=['--primary-fill','--primary-fill-dark','--success-fill','--danger-fill','--warn-fill','--info-fill','--accent','--st-ready'];
    for(const f of fills){
      assert.ok(T.contrast(T.roleValue('--on-primary','dark'),T.roleValue(f,'dark'))>=4.5,f+' dark');
      assert.ok(T.contrast(T.roleValue('--on-primary','hc'),T.roleValue(f,'hc'))>=7,f+' hc');
      assert.ok(T.contrast(T.roleValue('--on-primary-soft','dark'),T.roleValue('--primary-fill','dark'))>=4.5);}
    for(const f of ['--primary-fill','--success-fill','--danger-fill','--warn-fill','--info-fill','--accent'])assert.ok(T.contrast(T.roleValue(f,'dark'),T.roleValue('--bg','dark'))>=3,f);});
  test('[THEME] status colours keep their hue in dark and high contrast (labels stay in the markup; hue within 30°)',()=>{
    for(const [n,l,d,h] of T.ROLES){
      if(!/^--(success|danger|warn|info|st-)/.test(n))continue;
      if(T.oklch(l)[1]<0.03)continue; // near-neutral tints: hue is not meaningful
      if(T.oklch(d)[1]>=0.03)assert.ok(hueDiff(l,d)<=30,`${n} dark ${l}->${d}`);
      if(h!=='#ffffff'&&T.oklch(h)[1]>=0.03)assert.ok(hueDiff(l,h)<=30,`${n} hc ${l}->${h}`);}});
  test('[THEME] Maroon stays the brand: dark primary is a lighter maroon/rose, hc primary a darker maroon; surfaces are warm, never pure black',()=>{
    const hue=hueDiff(T.roleValue('--primary','light'),T.roleValue('--primary','dark'));assert.ok(hue<=25,'hue '+hue);
    assert.ok(T.luminance(T.roleValue('--primary','dark'))>T.luminance('#7a1f2b'));assert.ok(T.luminance(T.roleValue('--primary','hc'))<T.luminance('#7a1f2b'));
    for(const s of ['--bg','--surface','--sand-2']){const v=T.roleValue(s,'dark');assert.notEqual(v,'#000000');const [r,,b]=T.hexRgb(v);assert.ok(r>b,s+' warm');}});
  test('[THEME] high contrast: no tints (status and selection backgrounds are white), 2 px control borders and a 4 px focus ring',()=>{
    for(const [n] of T.ROLES)if(/-bg$|^--primary-tint$|^--surface|^--bg$|^--sand-[23]$/.test(n))assert.equal(T.roleValue(n,'hc'),'#ffffff',n);
    assert.match(themesCss,/\[data-theme="hc"\] :is\(input,select,textarea,\.btn[^)]*\)\{border-width:2px\}/);
    assert.match(themesCss,/\[data-theme="hc"\][^{]*:focus-visible\{outline:4px solid var\(--focus-ring\)!important/);});
  test('[THEME] print stays light: dark and hc blocks live only inside @media screen; light roles are not screen-only; bundle carries one copy',()=>{
    const screenAt=themesCss.indexOf('@media screen{'),end=themesCss.indexOf('\n}\n@media print');
    assert.ok(screenAt>0&&end>screenAt);
    for(const sel of ['[data-theme="dark"]','[data-theme="hc"]'])for(let i=themesCss.indexOf(sel);i>=0;i=themesCss.indexOf(sel,i+1))assert.ok(i>screenAt&&i<end,sel+' outside @media screen');
    assert.ok(themesCss.indexOf(':root,[data-theme="light"]{')<screenAt);
    assert.match(themesCss,/@media print\{:root\{color-scheme:light\}\}/);
    assert.equal(bundle.split('[data-theme="dark"]{').length,2);assert.ok(bundle.indexOf('[data-theme="dark"]{')>bundle.indexOf(':root,[data-theme="light"]{'));
    for(const s of ['src/styles/zz-print.css'])assert.doesNotMatch(read(s),/data-theme/);});
  test('[THEME] named colours that a dark surface must replace are overridden (styles.css .small-stats>div background:white)',()=>{
    assert.match(read('src/styles.css'),/\.small-stats>div\{[^}]*background:white/);
    assert.match(themesCss,/\[data-theme="dark"\] \.small-stats>div\{background:var\(--surface\)\}/);
    assert.match(themesCss,/\.pf-seg button\.on small\{color:inherit\}/,'size names on the selected text-size button follow its text colour');});
  test('[THEME] prefs: theme defaults to light, round-trips, invalid values fall back; resolveTheme follows the device only for "system"',()=>{
    assert.equal(X.PREFS_DEFAULTS.theme,'light');assert.deepEqual([...X.THEMES],['light','dark','hc','system']);
    withStorage(data=>{X.savePrefs({theme:'hc'});X.resetPrefsCache();assert.equal(X.readPrefs().theme,'hc');assert.equal(JSON.parse(data.get('hcc-prefs-1')).p.theme,'hc');
      X.savePrefs({theme:'system'});assert.equal(X.getPrefs().theme,'system');X.resetPrefs();assert.equal(X.getPrefs().theme,'light');});
    assert.equal(X.sanitizePrefs({theme:'sepia'}).theme,'light');assert.equal(X.parsePrefs(JSON.stringify({v:1,p:{theme:'dark'}})).theme,'dark');
    assert.equal(X.resolveTheme('system',true),'dark');assert.equal(X.resolveTheme('system',false),'light');
    for(const t of ['light','dark','hc'])for(const sys of [true,false])assert.equal(X.resolveTheme(t,sys),t);
    assert.equal(X.systemPrefersDark(),false,'no matchMedia in Node = light');});
  test('[THEME] applyPrefsToDocument sets data-theme and <meta theme-color> per theme ("system" resolved from the device)',()=>{
    for(const [pref,sys,want] of [['light',true,'light'],['dark',false,'dark'],['hc',true,'hc'],['system',true,'dark'],['system',false,'light']]){
      const {attrs,meta,doc}=fakeDoc();
      assert.equal(X.applyPrefsToDocument({...X.PREFS_DEFAULTS,theme:pref},doc,sys),want);
      assert.equal(attrs['data-theme'],want);assert.equal(meta.content,X.themeChrome(want));}
    assert.equal(X.applyPrefsToDocument({...X.PREFS_DEFAULTS,theme:'dark'},undefined,false),'dark');});
  test('[THEME] status bar / theme-color per theme = the phone app-bar fill of that theme; styleStatusBar is a no-op outside the APK and sets the colour inside',()=>{
    assert.deepEqual({...X.THEME_CHROME},{...T.THEME_CHROME});assert.deepEqual({...X.STATUS_BAR_COLOURS},{light:'#7a1f2b',dark:'#ae4155',hc:'#5e1620'});
    assert.match(read('src/styles/mobile.css'),/\.m-appbar\{[^}]*background:var\(--primary-fill\)/);
    assert.match(read('build.mjs'),/<meta name="theme-color" content="#7a1f2b">/);
    X.resetNativeCache();assert.doesNotThrow(()=>X.styleStatusBar('dark'));
    const calls=[];const plugin={setOverlaysWebView:()=>{},setBackgroundColor:a=>calls.push(['bg',a.color]),setStyle:a=>calls.push(['style',a.style])};
    globalThis.Capacitor={isNativePlatform:()=>true,registerPlugin:()=>plugin};
    try{X.resetNativeCache();for(const t of ['light','dark','hc'])X.styleStatusBar(t);X.styleStatusBar();}
    finally{delete globalThis.Capacitor;X.resetNativeCache();}
    assert.deepEqual(calls.filter(c=>c[0]==='bg').map(c=>c[1]),['#7a1f2b','#ae4155','#5e1620','#7a1f2b']);
    assert.ok(calls.filter(c=>c[0]==='style').every(c=>c[1]==='DARK'),'light icons on every fill');});
  test('[THEME] Appearance & display: the Theme row is a 4-way choice with a live 3-card preview (each card in its own data-theme) on desktop and phone',()=>{
    const s=M.seed();
    for(const mobile of [false,true]){
      const html=X.renderPrefs(s,'housekeeper',mobile);
      assert.match(html,/role="radiogroup" aria-label="Theme"/);
      for(const l of ['Light','Dark','High contrast','Follow device'])assert.ok(html.includes('>'+l+'<'),l);
      for(const t of ['light','dark','hc'])assert.match(html,new RegExp(`data-theme="${t}" class="pf-theme-card`));
      for(const l of ['Ready','Dirty','Occupied'])assert.ok(html.includes('>'+l+'</i>'),'status chip label '+l);
      assert.doesNotMatch(html,/dark theme coming/);assert.match(html,/Printed documents always stay light/);}
    assert.match(X.themeHelp('system','dark'),/now: Dark/);assert.equal(X.THEME_OPTIONS.length,4);
    assert.match(themesCss,/\.pf-m-page \.pf-theme-card\{[^}]*min-height:48px/);});
  test('[THEME] dist/index.html (when built) carries the theme blocks and the role aliases',()=>{
    for(const p of ['dist/index.html','dist/production/index.html']){if(!existsSync(p))continue;const h=read(p);
      assert.ok(h.includes('[data-theme="dark"]{'),p);assert.ok(h.includes('--c-ffffff:var(--surface)'),p);assert.ok(h.includes('--primary-fill:#ae4155'),p);}});
}
