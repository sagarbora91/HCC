// FIT · fit-to-screen check (DEC-178…180, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §8). Playwright, by hand; not part of npm test.
// Needs `npm run build` and a server: `PORT=4183 node serve.mjs` (or BASE=http://127.0.0.1:<port>/). Run through tools\Invoke-Serialized.ps1.
//   node tools/fit-screen-check.mjs [--pages a,b] [--roles owner,reception] [--viewports tab-land,lap-1366] [--text-scale 1.3]
//                                   [--theme light|dark|high-contrast] [--out <dir>] [--no-shots] [--no-scale-pass] [--no-modals] [--record-baseline]
// Default run = pass A (text scale 1, every check, five viewports) + pass B (text scale 1.3 on tab-land and lap-1366, checks 1, 2, 5 only).
// `--text-scale S` runs one pass at S (checks 1, 2, 5 only when S != 1). `--record-baseline` writes tools/fit-baseline/fonts-<viewport>.json
// (roles default owner,manager,reception,housekeeper,maint,accountant; every page the role can open, every local tab of it clicked in turn, + the
// drawer + the user-menu modal). Record on the BASE build (main), never on a FIT build: the baseline is what the fonts were before the wave.
// Baseline format (integrator, 2 Nov 2026): {pages:{<page id>:{<kind>:[sizes]}}, all:{<kind>:[sizes]}} — every visible element of a kind
// inside .content (not only the first one), per page; a page missing from the baseline is compared with "all". tools/fit-baseline/allow.json
// lists the few sizes allowed on a page with the reason (each one checked by hand against main).
// On the desktop layout the document and .content must never scroll; only [data-fit-scroll] panels may. Fonts must equal the
// baseline except the page h1 (22 px). Sub-screens: sessionStorage `hcc-fit-tab:<page>` (lane fit-shell's useFitTab) is set before the page opens.
// FAIL kinds: page-scroll, page-sideways, content-scroll, not-converted, cramped, hidden-action, font-changed, h1, touch-size,
// modal-clipped, opener-missing, not-reached, page-error. Exit 0 = every check passed.
import {createRequire} from 'node:module';
import {mkdirSync,readFileSync,writeFileSync,existsSync} from 'node:fs';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYWRIGHT_DIR||'E:/ClaudeDev/cache/npm/_npx/9833c18b2d85bc59/node_modules/playwright');
const HERE=dirname(fileURLToPath(import.meta.url)),ROOT=join(HERE,'..');
const args=process.argv.slice(2);const opt=k=>{const i=args.indexOf(k);return i>=0?args[i+1]:undefined;};const flag=k=>args.includes(k);
const stamp=new Date().toISOString().slice(0,16).replace(/[-:]/g,'').replace('T','-');
const OUT=opt('--out')||join(ROOT,'..','05 Delivery Evidence','Fit');
const BASE=process.env.BASE||`http://127.0.0.1:${process.env.PORT||4183}/`;
const RECORD=flag('--record-baseline'),SHOTS=!flag('--no-shots')&&!RECORD;
const sleep=ms=>new Promise(r=>setTimeout(r,ms));

const VIEWPORTS=[
  {id:'tab-land',w:1138,h:640,touch:true,screen:[1138,711]},
  {id:'tab-port',w:711,h:1066,touch:true,screen:[711,1138]},
  {id:'lap-1366',w:1366,h:657},
  {id:'lap-1280',w:1280,h:720},
  {id:'desk-1920',w:1920,h:969},
  // F8D-FIT: the phone layout (narrow window, no Capacitor mock). Opt-in only (--viewports phone): the page scrolls naturally there (DEC-213 applies to the desktop layout), so only sideways overflow, errors and not-reached are checked.
  {id:'phone',w:390,h:844,touch:true,phone:true},
].filter(v=>opt('--viewports')?opt('--viewports').split(',').includes(v.id):!v.phone);
const ROLES=(opt('--roles')||(RECORD?'owner,manager,reception,housekeeper,maint,accountant':'owner,manager,reception,cashier,supervisor,housekeeper,accountant,maint,techadmin')).split(',');
// Page ids: navmap.ts PAGE_CATEGORY (a test enforces every nav page is listed) + overview + Modules Home + the ten category pages.
const navSrc=readFileSync(join(ROOT,'src','app','navmap.ts'),'utf8');
const pcStart=navSrc.indexOf('export const PAGE_CATEGORY'),block=navSrc.slice(pcStart,navSrc.indexOf('};',pcStart));
const CATS=[...navSrc.slice(navSrc.indexOf('export const NAV_CATEGORIES'),navSrc.indexOf('export const DASHBOARD')).matchAll(/\{id:'([a-z]+)'/g)].map(m=>m[1]);
const ALL_PAGES=[...new Set(['home',...CATS.map(c=>'cat:'+c),'overview',...[...block.matchAll(/(?:^|[,{\s])'?([a-z][a-z0-9-]*)'?\s*:\s*'[a-z]+'/g)].map(m=>m[1])])];
const CAT_LABEL=Object.fromEntries([...navSrc.matchAll(/\{id:'([a-z]+)',label:'([^']+)'/g)].map(m=>[m[1],m[2]]));
// Every page has a nav item since 'guests' was linked (FIT cleanup); nothing extra to visit.
const EXTRA_PAGES=[];
const PAGES=opt('--pages')?opt('--pages').split(','):[...ALL_PAGES,...EXTRA_PAGES.filter(p=>!ALL_PAGES.includes(p))];
// Sub-screens from src/app/subscreens.ts (lane fit-shell); absent or empty before the wave.
const subPath=join(ROOT,'src','app','subscreens.ts');
const SUBS=existsSync(subPath)?[...readFileSync(subPath,'utf8').matchAll(/^\s*\{\s*page:\s*'([^']+)'\s*,\s*id:\s*'([^']+)'(?:\s*,\s*label:\s*'([^']+)')?/gm)].map(m=>({page:m[1],id:m[2],label:m[3]})):[];
// Modal openers: [{page, opener:"button text" | selector:"css", label?, roles?:[..], tab?}]. Module lanes add theirs; the integrator appends them.
const MODALS=!flag('--no-modals')&&!RECORD&&existsSync(join(HERE,'fit-modals.json'))?JSON.parse(readFileSync(join(HERE,'fit-modals.json'),'utf8')):[];
// Contract §8 check 6: key = the name in the baseline file, value = the selector actually measured (first visible match).
// Review B1: a plain count (.stat>strong) and the old small tiles (.small-stats) are kinds of their own, so a count turned into a money-stat shows.
const FONT_SEL={'td':'td','th':'th','button.btn':'button.btn','input':'input:not([type=checkbox]):not([type=radio])','select':'select','label':'label','.pill':'.pill',
  '.tabs button, .fit-tabs-bar button':'.tabs button, .fit-tabs-bar button','.stat-label':'.stat-label','.money-stat':'.money-stat',
  '.stat>strong':'.stat>strong:not(.money-stat)','.small-stats strong':'.small-stats strong','.small-stats span':'.small-stats>div>span','p':'p','.muted':'.muted',
  '.drawer a':'.drawer a, .drawer-sub, .drawer-item','.modal h2':'.modal h2'};
const CHROME_KEYS=['.drawer a','.modal h2'];
const ALLOW=existsSync(join(HERE,'fit-baseline','allow.json'))?JSON.parse(readFileSync(join(HERE,'fit-baseline','allow.json'),'utf8')):[];
const allowedFont=(page,k,v)=>ALLOW.some(a=>a.kind===k&&a.size===v&&(!a.pages||a.pages.includes(page)));
const PAGE_FONT_KEYS=Object.keys(FONT_SEL).filter(k=>!CHROME_KEYS.includes(k));
const prefFor=scale=>({textScale:scale,honourSystemFont:true,reduceMotion:'system',density:'comfortable',startPage:'',listPageSize:100,toastSeconds:6.5,theme:opt('--theme')||'light'});

// Passes: A = scale 1, all checks; B = scale 1.3 on tab-land + lap-1366, checks 1, 2, 5.
const PASSES=opt('--text-scale')?[{scale:Number(opt('--text-scale')),vps:null}]
  :RECORD||flag('--no-scale-pass')?[{scale:1,vps:null}]:[{scale:1,vps:null},{scale:1.3,vps:['tab-land','lap-1366']}];
for(const p of PASSES)p.full=p.scale===1;

mkdirSync(OUT,{recursive:true});
const shotDir=join(OUT,`screens-${stamp}`);if(SHOTS)mkdirSync(shotDir,{recursive:true});
const browser=await chromium.launch({channel:process.env.PW_CHANNEL||'chrome'});
const rows=[];let lastId='';

const pushPage=(page,id)=>page.evaluate(id=>{
  const idx=(history.state&&history.state.idx||0)+1;const st={hcc:'page',page:id,idx};
  history.pushState(st,'');window.dispatchEvent(new PopStateEvent('popstate',{state:st}));
},id);
async function openPage(page,id,tab){
  // A page that is already open does not remount on a repeated go(): leave through Modules Home first so the tab is re-read.
  if(lastId===id&&id!=='home'){await pushPage(page,'home');await sleep(120);}
  await page.evaluate(({id,tab})=>{
    try{if(tab)sessionStorage.setItem('hcc-fit-tab:'+id,tab);else sessionStorage.removeItem('hcc-fit-tab:'+id);}catch{/* blocked */}
  },{id,tab});
  await pushPage(page,id);lastId=id;await sleep(350);
}
const clickEl=async(page,sel)=>{const l=page.locator(sel).first();try{await l.click({timeout:2500});}catch{await l.evaluate(e=>e.click()).catch(()=>{});}};

// Pages the role can open = the drawer's `.drawer-sub[data-page]` list (every category expanded in turn) + Dashboard + Modules Home + category pages.
async function drawerPages(page){
  await clickEl(page,'button[aria-label="Open menu"]');await sleep(300);
  const out=new Set(['home']);
  const hasDash=await page.evaluate(()=>[...document.querySelectorAll('.drawer-nav > .drawer-item')].some(b=>b.textContent.trim()==='Dashboard'));
  if(hasDash)out.add('overview');
  const cats=await page.evaluate(()=>[...document.querySelectorAll('.drawer-cat-btn')].map(b=>b.textContent.trim()));
  const labels=[...navSrc.matchAll(/\{id:'([a-z]+)',label:'([^']+)'/g)].map(m=>({id:m[1],label:m[2]}));
  for(let i=0;i<cats.length;i++){
    const b=page.locator('.drawer-cat-btn').nth(i);
    if(await b.getAttribute('aria-expanded')!=='true'){await b.click().catch(()=>{});await sleep(80);}
    for(const id of await page.evaluate(()=>[...document.querySelectorAll('.drawer-sub[data-page]')].map(e=>e.getAttribute('data-page'))))out.add(id);
    const cat=labels.find(l=>cats[i].startsWith(l.label));if(cat)out.add('cat:'+cat.id);
  }
  await page.keyboard.press('Escape');await sleep(200);
  return out;
}
const measure=(page,touch)=>page.evaluate(({FONT_SEL,CHROME_KEYS,touch,wantTexts})=>{
  const de=document.documentElement,c=document.querySelector('.content');
  const vis=e=>{const r=e.getBoundingClientRect();const cs=getComputedStyle(e);return r.width>0&&r.height>0&&cs.visibility!=='hidden'&&cs.display!=='none';};
  const root=c&&c.firstElementChild;
  const panels=[...document.querySelectorAll('[data-fit-scroll]')].filter(vis).map(e=>({kind:e.getAttribute('data-fit-scroll')||'panel',sideways:e.matches('.fit-tabs-bar, [data-fit-scroll=x]'),h:Math.round(e.getBoundingClientRect().height)}));
  const actions=[...document.querySelectorAll('.fit-page-actions *:is(button,a), .modal-actions *:is(button,a), .fit-form-actions *:is(button,a)')].filter(vis).map(e=>{const r=e.getBoundingClientRect();return {t:(e.textContent||'').trim().slice(0,24),ok:r.top>=-1&&r.left>=-1&&r.bottom<=innerHeight+1&&r.right<=innerWidth+1};});
  // Page kinds: every visible element of the kind inside .content (the Demo toolbar / popover and the top bar are outside it). Chrome kinds: the
  // first visible one anywhere (drawer, user-menu dialog).
  // Text key of an element (first 48 characters of its text): a moved element is matched with the same text on the base build.
  const key=e=>{const t=(e.textContent||'').replace(/\s+/g,' ').trim().slice(0,48);return t.length>=4&&!/^[\d\s.,₹%:\/()+-]+$/.test(t)?t:'';};
  const fonts={},byKind={};for(const [k,s] of Object.entries(FONT_SEL)){const all=[...document.querySelectorAll(s)].filter(vis);
    if(CHROME_KEYS.includes(k)){if(all[0])fonts[k]=[getComputedStyle(all[0]).fontSize];continue;}
    const inC=all.filter(x=>x.closest('.content'));const sizes=[...new Set(inC.map(x=>getComputedStyle(x).fontSize))].sort();if(sizes.length)fonts[k]=sizes;
    const bk={};for(const x of inC){const fs=getComputedStyle(x).fontSize;const a=(bk[fs]??=[]);if(a.length<40)a.push(key(x));}byKind[k]=bk;}
  // Base build only (--record-baseline): the font size of every visible text in .content outside table bodies, by text key.
  const texts={};if(wantTexts&&c)for(const e of c.querySelectorAll('*')){if(e.closest('tbody')||![...e.childNodes].some(n=>n.nodeType===3&&n.nodeValue.trim()))continue;
    const k=key(e);if(!k||!vis(e))continue;const fs=getComputedStyle(e).fontSize;(texts[k]??=[]).includes(fs)||texts[k].push(fs);}
  const h1=[...document.querySelectorAll('.content h1')].find(vis);
  const modal=[...document.querySelectorAll('.modal')].find(vis);const mr=modal&&modal.getBoundingClientRect();
  const small=touch?[...document.querySelectorAll('.app-shell button, .app-shell input:not([type=checkbox]):not([type=radio]), .app-shell select, [role=tab]')].filter(vis)
    .map(e=>({t:(e.textContent||e.getAttribute('aria-label')||e.tagName).trim().slice(0,20),h:Math.round(e.getBoundingClientRect().height),need:e.matches('.btn.primary, .modal-actions .btn, .fit-tabs-bar [role=tab]')?48:44})).filter(x=>x.h<x.need):[];
  return {docH:de.scrollHeight,docW:de.scrollWidth,vh:innerHeight,vw:innerWidth,contentH:c?c.scrollHeight:0,contentClient:c?c.clientHeight:0,
    fitRoot:!!(root&&root.classList.contains('fit-page')),panels,actions,fonts,byKind,texts,h1:h1?getComputedStyle(h1).fontSize:null,
    contentText:c?c.innerText.slice(0,400):'',modalClipped:mr?(mr.top<-1||mr.bottom>innerHeight+1):false,small:small.slice(0,6),smallCount:small.length,title:document.title};
},{FONT_SEL,CHROME_KEYS,touch:!!touch,wantTexts:RECORD});

// Checks 1, 2 and 5 (run in every pass).
const baseFails=m=>{
  const f=[];
  if(m.docH>m.vh+1)f.push(`page-scroll ${m.docH}/${m.vh}`);
  if(m.docW>m.vw+1)f.push(`page-sideways ${m.docW}/${m.vw}`);
  if(m.contentH>m.contentClient+1)f.push(`content-scroll ${m.contentH}/${m.contentClient}`);
  if(m.panels.some(p=>p.kind==='legacy'))f.push('not-converted legacy-scroll');
  const hidden=m.actions.filter(a=>!a.ok);if(hidden.length)f.push(`hidden-action ${hidden.map(a=>a.t).join('|')}`);
  return f;
};

for(const pass of PASSES){
  for(const vp of VIEWPORTS.filter(v=>!pass.vps||pass.vps.includes(v.id))){
    const baseFile=join(HERE,'fit-baseline',`fonts-${vp.id}.json`);
    const baseline=!RECORD&&pass.full&&existsSync(baseFile)?JSON.parse(readFileSync(baseFile,'utf8')):null;
    const recorded={pages:{},all:{},texts:{}};const rec=(page,fonts,texts)=>{for(const [k,vs] of Object.entries(fonts))for(const v of vs){((recorded.pages[page]??={})[k]??=new Set()).add(v);(recorded.all[k]??=new Set()).add(v);}
      for(const [t,vs] of Object.entries(texts||{}))for(const v of vs)((recorded.texts[page]??={})[t]??=new Set()).add(v);};
    for(const role of ROLES){
      const ctx=await browser.newContext({viewport:{width:vp.w,height:vp.h},deviceScaleFactor:1,hasTouch:!!vp.touch,isMobile:false});
      await ctx.addInitScript(({role,PREF,vp})=>{
        try{localStorage.setItem('hcc-session',role);localStorage.setItem('hcc-display-mode','auto');localStorage.setItem('hcc-prefs-1',JSON.stringify({v:1,p:PREF}));}catch{/* blocked */}
        if(vp.touch&&!vp.phone){ // the Capacitor mock of tests/check-tablet-back.mjs: native platform + tablet-sized screen
          Object.defineProperty(screen,'width',{get:()=>vp.screen[0]});Object.defineProperty(screen,'height',{get:()=>vp.screen[1]});
          window.Capacitor={isNativePlatform:()=>true,registerPlugin:()=>({addListener:async()=>({remove(){}}),setOverlaysWebView:async()=>{},setBackgroundColor:async()=>{},setStyle:async()=>{}})};
        }
      },{role,PREF:prefFor(pass.scale),vp});
      const page=await ctx.newPage();const errs=[];page.on('pageerror',e=>errs.push(String(e.message)));
      await page.goto(BASE);await page.waitForSelector('.topbar',{timeout:30000}).catch(()=>{});await sleep(800);
      lastId='';
      const allowed=await drawerPages(page);
      const tag=r=>({scale:pass.scale,vp:vp.id,role,...r});
      for(const id of PAGES){
        if(!allowed.has(id)&&!EXTRA_PAGES.includes(id))continue; // the role cannot open this page (not in its drawer)
        const tabs=RECORD?[undefined]:[undefined,...SUBS.filter(s=>s.page===id).map(s=>s.id)]; // the base build has no sub-screens (its local tabs are clicked instead)
        for(const tab of tabs){
          await openPage(page,id,tab);
          // F8E-FIT: the phone shell ignores the sub-screen tab (its sections are chips, local state): click the chip with the sub-screen's label; no such chip = this page has no phone chip for it, skip.
          if(vp.phone&&tab){const lab=SUBS.find(x=>x.page===id&&x.id===tab)?.label;const chip=lab?page.locator('.m-chips button').filter({hasText:new RegExp('^'+[...lab].map(c=>/[a-z0-9 ]/i.test(c)?c:'\\'+c).join(''))}).first():null;
            if(!chip||!await chip.count())continue;await chip.click({timeout:2500}).catch(()=>{});await sleep(400);}
          const m=await measure(page,vp.touch);
          // a page with no drawer entry: measured only when the app really shows it for this role (FIT root, or on the base build a page title)
          const extra=EXTRA_PAGES.includes(id)&&!allowed.has(id);
          if(extra&&!await page.evaluate(id=>!!document.querySelector(`.content [data-fit-page="${id}"]`)||(!document.querySelector('.content .fit-page')&&history.state&&history.state.page===id&&!document.querySelector('.content .tiles')),id))continue;
          if(RECORD){
            const pf=o=>Object.fromEntries(Object.entries(o).filter(([k])=>PAGE_FONT_KEYS.includes(k)));rec(id,pf(m.fonts),m.texts);
            // the base build has no sub-screens: click every local tab of the page in turn so the fonts of every section are recorded
            const n=await page.evaluate(()=>[...document.querySelectorAll('.content .tabs button, .content [role=tab], .content .cfg-item')].length);
            for(let i=0;i<Math.min(n,30);i++){
              const ok=await page.evaluate(i=>{const b=[...document.querySelectorAll('.content .tabs button, .content [role=tab], .content .cfg-item')][i];if(!b||b.disabled)return false;b.click();return true;},i);
              if(!ok)continue;await sleep(220);
              if(await page.evaluate(id=>!history.state||history.state.page===id,id)){const mm=await measure(page,vp.touch);rec(id,pf(mm.fonts),mm.texts);}
              if(await page.locator('.modal').count()){await page.keyboard.press('Escape');await sleep(150);}
            }
            continue;
          }
          const catLabel=id.startsWith('cat:')?CAT_LABEL[id.slice(4)]:null; // category pages have no page label in the title: look for the category name in the content
          const reached=id==='home'||extra||(catLabel?m.contentText.includes(catLabel):!/^Hotel Control Centre$/.test(m.title));
          const f=vp.phone?(m.docW>m.vw+1?[`page-sideways ${m.docW}/${m.vw}`]:[]):baseFails(m);
          if(!reached)f.push('not-reached (in the drawer but the app showed Modules Home)');
          if(pass.full&&!vp.phone){
            if(!m.fitRoot)f.push('not-converted');
            const cramped=m.panels.filter(p=>p.kind!=='legacy'&&!p.sideways&&p.h<160);if(cramped.length)f.push(`cramped ${cramped.map(p=>p.h).join(',')}`);
            // A size the page never had for this kind on main is explained only when every element of it shows a text that main showed at that
            // same size (the element moved / changed tag, its font did not). Same text at another size on main = a real change.
            if(baseline){const bp=baseline.pages?.[id]??baseline.all??{};const bt=baseline.texts?.[id]??{};
              for(const k of PAGE_FONT_KEYS)for(const v of (m.fonts[k]||[])){
                if((bp[k]||[]).includes(v)||allowedFont(id,k,v))continue;
                const keys=m.byKind[k]?.[v]||[''];const changed=[],unknown=[];
                for(const t of keys){const was=t&&bt[t];if(was&&was.includes(v))continue;if(was)changed.push(`«${t}» was ${was.join('/')}`);else unknown.push(t?`«${t}»`:'(no text)');}
                if(changed.length)f.push(`font-changed ${k} ${v}: ${[...new Set(changed)].slice(0,3).join('; ')}`);
                else if(unknown.length)f.push(`font-changed ${k} ${v} (new element; main on this page: ${(bp[k]||[]).join('/')||'none'}): ${[...new Set(unknown)].slice(0,3).join(' ')}`);}}
            if(m.fitRoot&&m.h1&&m.h1!=='22px')f.push(`h1 ${m.h1}`);
            if(vp.touch&&m.smallCount)f.push(`touch-size ${m.smallCount}: ${m.small.map(s=>s.t+' '+s.h).join('|')}`);
          }
          if(errs.length)f.push(`page-error ${errs.splice(0).join(' | ').slice(0,120)}`);
          rows.push(tag({page:id,tab:tab||'',screens:+(m.docH/m.vh).toFixed(1),docH:m.docH,vh:m.vh,result:f.length?'FAIL':'PASS',fails:f}));
          if(SHOTS&&pass.full&&(role==='owner'||role==='housekeeper')&&(vp.id==='tab-land'||vp.id==='lap-1366'))
            await page.screenshot({path:join(shotDir,`${vp.id}-${role}-${id.replace(':','_')}${tab?'-'+tab:''}.png`),fullPage:false});
        }
      }
      // Chrome row (scale 1): the drawer's and the user-menu modal's fonts (`.drawer a`, `.modal h2`) against the baseline.
      if(pass.full){
        await openPage(page,'home');
        await clickEl(page,'button[aria-label="Open menu"]');await sleep(300);const dm=await measure(page,vp.touch);await page.keyboard.press('Escape');await sleep(200);
        await clickEl(page,'.current-user-btn');await sleep(400);const um=await measure(page,vp.touch);await page.keyboard.press('Escape');await sleep(250);
        const fonts={'.drawer a':dm.fonts['.drawer a']?.[0],'.modal h2':um.fonts['.modal h2']?.[0]};
        if(RECORD){for(const [k,v] of Object.entries(fonts))if(v)(recorded.all[k]??=new Set()).add(v);}
        else{
          const f=[];if(baseline)for(const [k,v] of Object.entries(fonts)){const b=baseline.all?.[k]||baseline[k];if(v&&b&&!b.includes(v))f.push(`font-changed ${k} ${v} (was ${b.join('/')})`);}
          rows.push(tag({page:'(chrome)',tab:'drawer + user menu',screens:0,result:f.length?'FAIL':'PASS',fails:f}));
        }
      }
      // Modal sample (check 8): open, checks 1 and 5 (+ the box inside the viewport), close.
      for(const md of MODALS.filter(x=>!vp.phone&&(!x.roles||x.roles.includes(role)))){
        if(!allowed.has(md.page))continue;
        await openPage(page,md.page,md.tab);
        const name='modal:'+(md.label||md.opener||md.selector);
        const b=md.selector?page.locator(md.selector).first():page.getByRole('button',{name:md.opener}).first();
        // the opener may sit in the header's More menu (narrow header), or in a record pane that is closed below 900 px ("prep" opens it)
        if(!await b.count()&&await page.locator('.fit-more-btn').count()){await clickEl(page,'.fit-more-btn');await sleep(200);}
        if(!await b.count()&&md.prep){await clickEl(page,md.prep);await sleep(350);}
        if(!await b.count()){rows.push(tag({page:md.page,tab:name,result:'FAIL',fails:['opener-missing']}));continue;}
        await b.click({timeout:4000}).catch(()=>b.evaluate(e=>e.click()).catch(()=>{}));await sleep(450);
        const m=await measure(page,vp.touch);const f=[];
        if(m.docH>m.vh+1)f.push(`page-scroll ${m.docH}/${m.vh}`);
        if(m.docW>m.vw+1)f.push(`page-sideways ${m.docW}/${m.vw}`);
        const hidden=m.actions.filter(a=>!a.ok);if(hidden.length)f.push(`hidden-action ${hidden.map(a=>a.t).join('|')}`);
        if(m.modalClipped)f.push('modal-clipped');
        rows.push(tag({page:md.page,tab:name,screens:+(m.docH/m.vh).toFixed(1),result:f.length?'FAIL':'PASS',fails:f}));
        await page.keyboard.press('Escape');await sleep(250);
        if(await page.locator('.modal').count()){await page.locator('.modal [aria-label=Close], .modal .btn').first().click({timeout:1500}).catch(()=>{});await sleep(250);}
        lastId='';
      }
      await ctx.close();
    }
    if(RECORD){mkdirSync(dirname(baseFile),{recursive:true});const fix=o=>Object.fromEntries(Object.entries(o).sort().map(([k,v])=>[k,[...v].sort()]));
      writeFileSync(baseFile,JSON.stringify({recorded:{base:process.env.BASE_LABEL||BASE,roles:ROLES,at:stamp,note:'every visible element of a kind inside .content, per page, each local tab clicked; record on the base build (main)'},
        pages:Object.fromEntries(Object.entries(recorded.pages).sort().map(([p,o])=>[p,fix(o)])),all:fix(recorded.all),
        texts:Object.fromEntries(Object.entries(recorded.texts).sort().map(([p,o])=>[p,fix(o)]))}));console.log('baseline',baseFile);}
  }
}
await browser.close();
if(RECORD)process.exit(0);

// Report: per page at each viewport the worst result over roles; per-role summary; every failing row.
const fails=rows.filter(r=>r.result==='FAIL').length;
const scaleTag=PASSES.length===1&&PASSES[0].scale!==1?`-text${PASSES[0].scale}`:'';
const name=`fit-check-${stamp}${scaleTag}${opt('--theme')?'-'+opt('--theme'):''}`;
writeFileSync(join(OUT,name+'.json'),JSON.stringify(rows,null,1));
const kindOf=s=>s.split(' ')[0];
const kinds={};for(const r of rows)for(const x of r.fails||[])kinds[kindOf(x)]=(kinds[kindOf(x)]||0)+1;
let md=`# FIT check ${stamp}\n\nBase ${BASE} · passes ${PASSES.map(p=>'text '+p.scale+(p.vps?' on '+p.vps.join('+'):'')+(p.full?' (all checks)':' (checks 1, 2, 5)')).join('; ')} · roles ${ROLES.join(', ')} · ${rows.length} checks · **${fails} failing** · ${fails?'FAIL':'PASS'}\n\n`;
md+=`FAIL kinds (count of findings): ${Object.entries(kinds).sort((a,b)=>b[1]-a[1]).map(([k,v])=>`${k} ${v}`).join(', ')||'none'}\n\n`;
for(const pass of PASSES){
  const pr=rows.filter(r=>r.scale===pass.scale);const vps=[...new Set(pr.map(r=>r.vp))];
  const keys=[...new Set(pr.map(r=>r.page+(r.tab?' › '+r.tab:'')))];
  const cell=(k,v)=>{const rs=pr.filter(r=>r.vp===v&&(r.page+(r.tab?' › '+r.tab:''))===k);if(!rs.length)return '–';const bad=rs.filter(r=>r.result==='FAIL');
    const scr=Math.max(...rs.map(r=>r.screens||0));return bad.length?`FAIL ${scr||''}`.trim():'PASS';};
  md+=`## Text scale ${pass.scale}\n\nCell = worst over roles; the number is the document height in screens (1.0 = fits).\n\n| Page | ${vps.join(' | ')} |\n|---|${vps.map(()=>'---').join('|')}|\n`;
  for(const k of keys)md+=`| ${k} | ${vps.map(v=>cell(k,v)).join(' | ')} |\n`;
  md+=`\n### Per role (checks failing / total)\n\n| Role | ${vps.join(' | ')} |\n|---|${vps.map(()=>'---').join('|')}|\n`;
  for(const role of ROLES)md+=`| ${role} | ${vps.map(v=>{const rs=pr.filter(r=>r.vp===v&&r.role===role);return rs.length?`${rs.filter(r=>r.result==='FAIL').length}/${rs.length}`:'–';}).join(' | ')} |\n`;
  md+='\n';
}
md+=`## Failures\n\n| Text | Viewport | Role | Page | Why |\n|---|---|---|---|---|\n`;
for(const r of rows.filter(r=>r.result==='FAIL'))md+=`| ${r.scale} | ${r.vp} | ${r.role} | ${r.page}${r.tab?' › '+r.tab:''} | ${r.fails.join('; ').replace(/\|/g,'/')} |\n`;
if(SHOTS)md+=`\nScreenshots (Owner + Housekeeper at tab-land and lap-1366, kept out of git): \`${shotDir}\`\n`;
writeFileSync(join(OUT,name+'.md'),md);
console.log(`${fails?'FAIL':'PASS'} — ${fails} failing of ${rows.length}. Report: ${join(OUT,name+'.md')}`);
process.exit(fails?1:0);
