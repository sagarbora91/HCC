// FIT · fit-to-screen check (DEC-165…167, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §8). Playwright, by hand; not part of npm test.
// Needs `npm run build` and a server: `PORT=4183 node serve.mjs` (or BASE=http://127.0.0.1:<port>/). Run through tools\Invoke-Serialized.ps1.
//   node tools/fit-screen-check.mjs [--pages a,b] [--roles owner,reception] [--viewports tab-land,lap-1366] [--text-scale 1.3]
//                                   [--out <dir>] [--no-shots] [--record-baseline]
// On the desktop layout the document and .content must never scroll; only [data-fit-scroll] panels may. Fonts must equal the
// baseline (tools/fit-baseline/fonts-<viewport>.json) except the page h1 (22 px). Exit 0 = every check passed.
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
const TEXT_SCALE=Number(opt('--text-scale')||1);
const RECORD=flag('--record-baseline'),SHOTS=!flag('--no-shots')&&!RECORD;
const sleep=ms=>new Promise(r=>setTimeout(r,ms));

const VIEWPORTS=[
  {id:'tab-land',w:1138,h:640,touch:true,screen:[1138,711]},
  {id:'tab-port',w:711,h:1066,touch:true,screen:[711,1138]},
  {id:'lap-1366',w:1366,h:657},
  {id:'lap-1280',w:1280,h:720},
  {id:'desk-1920',w:1920,h:969},
].filter(v=>!opt('--viewports')||opt('--viewports').split(',').includes(v.id));
const ROLES=(opt('--roles')||'owner,manager,reception,cashier,supervisor,housekeeper,accountant,maint,techadmin').split(',');
// Page ids from navmap.ts PAGE_CATEGORY (every nav page is listed there; a test enforces it) + Modules Home.
const navSrc=readFileSync(join(ROOT,'src','app','navmap.ts'),'utf8');
const block=navSrc.slice(navSrc.indexOf('export const PAGE_CATEGORY'),navSrc.indexOf('};',navSrc.indexOf('export const PAGE_CATEGORY')));
const ALL_PAGES=['home',...[...block.matchAll(/'?([a-z][a-z0-9-]*)'?\s*:\s*'[a-z]+'/g)].map(m=>m[1])];
const PAGES=opt('--pages')?opt('--pages').split(','):ALL_PAGES;
// Sub-screens from src/app/subscreens.ts (created by lane fit-shell); absent before the wave.
const subPath=join(ROOT,'src','app','subscreens.ts');
const SUBS=existsSync(subPath)?[...readFileSync(subPath,'utf8').matchAll(/page:'([^']+)',id:'([^']+)'/g)].map(m=>({page:m[1],id:m[2]})):[];
const MODALS=existsSync(join(HERE,'fit-modals.json'))?JSON.parse(readFileSync(join(HERE,'fit-modals.json'),'utf8')):[];
const FONT_SEL=['td','th','button.btn','input:not([type=checkbox]):not([type=radio])','select','label','.pill','.tabs button, .fit-tabs-bar button','.stat-label','.money-stat','p','.muted','.drawer a','.modal h2'];
const PREF={textScale:TEXT_SCALE,honourSystemFont:true,reduceMotion:'system',density:'comfortable',startPage:'',listPageSize:100,toastSeconds:6.5};

mkdirSync(OUT,{recursive:true});
const shotDir=join(OUT,`screens-${stamp}`);if(SHOTS)mkdirSync(shotDir,{recursive:true});
const browser=await chromium.launch({channel:process.env.PW_CHANNEL||'chrome'});
const rows=[];let fails=0;

async function openPage(page,id,tab){
  await page.evaluate(({id,tab})=>{
    if(tab)try{sessionStorage.setItem('hcc-fit-tab:'+id,tab);}catch{/* blocked */}
    const idx=(history.state&&history.state.idx||0)+1;const st={hcc:'page',page:id,idx};
    history.pushState(st,'');window.dispatchEvent(new PopStateEvent('popstate',{state:st}));
  },{id,tab});
  await sleep(450);
}
// What the page shows now: which page the app actually opened (a page the role cannot open falls back to Modules Home).
const measure=(page,vp)=>page.evaluate(({FONT_SEL,touch})=>{
  const de=document.documentElement,c=document.querySelector('.content');
  const crumbs=[...document.querySelectorAll('.topbar .crumbs > *, .topbar nav li, .topbar nav button, .topbar nav span')].map(e=>e.textContent.trim()).filter(Boolean);
  const vis=e=>{const r=e.getBoundingClientRect();const cs=getComputedStyle(e);return r.width>0&&r.height>0&&cs.visibility!=='hidden'&&cs.display!=='none';};
  const root=c&&c.firstElementChild;
  const panels=[...document.querySelectorAll('[data-fit-scroll]')].filter(vis).map(e=>({kind:e.getAttribute('data-fit-scroll')||'panel',h:Math.round(e.getBoundingClientRect().height)}));
  const actions=[...document.querySelectorAll('.fit-page-actions *:is(button,a), .modal-actions *:is(button,a)')].filter(vis).map(e=>{const r=e.getBoundingClientRect();return {t:(e.textContent||'').trim().slice(0,24),ok:r.top>=0&&r.left>=0&&r.bottom<=innerHeight+1&&r.right<=innerWidth+1};});
  const fonts={};for(const s of FONT_SEL){const e=[...document.querySelectorAll(s)].find(vis);if(e)fonts[s]=getComputedStyle(e).fontSize;}
  const h1=document.querySelector('.content h1');
  const small=touch?[...document.querySelectorAll('.app-shell button, .app-shell input:not([type=checkbox]):not([type=radio]), .app-shell select, [role=tab]')].filter(vis)
    .map(e=>({t:(e.textContent||e.getAttribute('aria-label')||e.tagName).trim().slice(0,20),h:Math.round(e.getBoundingClientRect().height),need:e.matches('.btn.primary, .modal-actions .btn, .fit-tabs-bar [role=tab]')?48:44})).filter(x=>x.h<x.need):[];
  return {crumbs,docH:de.scrollHeight,docW:de.scrollWidth,vh:innerHeight,vw:innerWidth,
    contentH:c?c.scrollHeight:0,contentClient:c?c.clientHeight:0,fitRoot:!!(root&&root.classList.contains('fit-page')),
    panels,actions,fonts,h1:h1?getComputedStyle(h1).fontSize:null,small:small.slice(0,6),smallCount:small.length,title:document.title};
},{FONT_SEL,touch:!!vp.touch});

for(const vp of VIEWPORTS){
  const baseFile=join(HERE,'fit-baseline',`fonts-${vp.id}.json`);
  const baseline=!RECORD&&existsSync(baseFile)?JSON.parse(readFileSync(baseFile,'utf8')):null;
  const recorded={};
  for(const role of ROLES){
    const ctx=await browser.newContext({viewport:{width:vp.w,height:vp.h},deviceScaleFactor:1,hasTouch:!!vp.touch,isMobile:false});
    await ctx.addInitScript(({role,PREF,vp})=>{
      try{localStorage.setItem('hcc-session',role);localStorage.setItem('hcc-display-mode','auto');localStorage.setItem('hcc-prefs-1',JSON.stringify({v:1,p:PREF}));}catch{/* blocked */}
      if(vp.touch){
        Object.defineProperty(screen,'width',{get:()=>vp.screen[0]});Object.defineProperty(screen,'height',{get:()=>vp.screen[1]});
        window.Capacitor={isNativePlatform:()=>true,registerPlugin:()=>({addListener:async()=>({remove(){}}),setOverlaysWebView:async()=>{},setBackgroundColor:async()=>{},setStyle:async()=>{}})};
      }
    },{role,PREF,vp});
    const page=await ctx.newPage();const errs=[];page.on('pageerror',e=>errs.push(String(e.message)));
    await page.goto(BASE);await page.waitForSelector('.topbar',{timeout:30000}).catch(()=>{});await sleep(800);
    for(const id of PAGES){
      const tabs=[undefined,...SUBS.filter(s=>s.page===id).map(s=>s.id)];
      for(const tab of tabs){
        await openPage(page,id,tab);
        const m=await measure(page,vp);
        const reached=id==='home'||!(m.crumbs.length<=1&&/^Hotel Control Centre$/.test(m.title));
        if(!reached)break; // role cannot open this page
        if(RECORD){for(const [k,v] of Object.entries(m.fonts))(recorded[k]??=new Set()).add(v);continue;}
        const f=[];
        if(m.docH>m.vh+1)f.push(`page-scroll ${m.docH}/${m.vh}`);
        if(m.docW>m.vw+1)f.push(`page-sideways ${m.docW}/${m.vw}`);
        if(m.contentH>m.contentClient+1)f.push(`content-scroll ${m.contentH}/${m.contentClient}`);
        if(!m.fitRoot||m.panels.some(p=>p.kind==='legacy'))f.push('not-converted');
        const cramped=m.panels.filter(p=>p.kind!=='legacy'&&p.h<160);if(cramped.length)f.push(`cramped ${cramped.map(p=>p.h).join(',')}`);
        const hidden=m.actions.filter(a=>!a.ok);if(hidden.length)f.push(`hidden-action ${hidden.map(a=>a.t).join('|')}`);
        if(TEXT_SCALE===1){
          if(baseline)for(const [k,v] of Object.entries(m.fonts))if(baseline[k]&&!baseline[k].includes(v))f.push(`font-changed ${k} ${v} (was ${baseline[k].join('/')})`);
          if(m.h1&&m.fitRoot&&m.h1!=='22px')f.push(`h1 ${m.h1}`);
        }
        if(vp.touch&&m.smallCount)f.push(`touch-size ${m.smallCount}: ${m.small.map(s=>s.t+' '+s.h).join('|')}`);
        if(errs.length)f.push(`page-error ${errs.splice(0).join(' | ').slice(0,120)}`);
        if(f.length)fails++;
        rows.push({vp:vp.id,role,page:id,tab:tab||'',screens:+(m.docH/m.vh).toFixed(1),docH:m.docH,vh:m.vh,result:f.length?'FAIL':'PASS',fails:f});
        if(SHOTS&&(role==='owner'||role==='housekeeper')&&(vp.id==='tab-land'||vp.id==='lap-1366'))
          await page.screenshot({path:join(shotDir,`${vp.id}-${role}-${id.replace(':','_')}${tab?'-'+tab:''}.png`),fullPage:false});
      }
    }
    // Modal sample (tools/fit-modals.json: [{page, opener: "button text", role?}]): checks page-scroll and hidden-action only.
    for(const md of MODALS.filter(x=>!x.role||x.role===role)){
      if(RECORD)break;
      await openPage(page,md.page);
      const b=page.getByRole('button',{name:md.opener}).first();
      if(!await b.count()){rows.push({vp:vp.id,role,page:md.page,tab:'modal:'+md.opener,result:'FAIL',fails:['opener-missing']});fails++;continue;}
      await b.click();await sleep(400);
      const m=await measure(page,vp);const f=[];
      if(m.docH>m.vh+1)f.push(`page-scroll ${m.docH}/${m.vh}`);
      const hidden=m.actions.filter(a=>!a.ok);if(hidden.length)f.push(`hidden-action ${hidden.map(a=>a.t).join('|')}`);
      if(f.length)fails++;
      rows.push({vp:vp.id,role,page:md.page,tab:'modal:'+md.opener,result:f.length?'FAIL':'PASS',fails:f});
      await page.keyboard.press('Escape');await sleep(250);
    }
    await ctx.close();
  }
  if(RECORD){mkdirSync(dirname(baseFile),{recursive:true});writeFileSync(baseFile,JSON.stringify(Object.fromEntries(Object.entries(recorded).map(([k,v])=>[k,[...v].sort()])),null,1));console.log('baseline',baseFile);}
}
await browser.close();
if(RECORD)process.exit(0);

// Report: per page at each viewport the worst result over roles; then every failing row.
const name=`fit-check-${stamp}${TEXT_SCALE!==1?'-text'+TEXT_SCALE:''}`;
writeFileSync(join(OUT,name+'.json'),JSON.stringify(rows,null,1));
const vps=VIEWPORTS.map(v=>v.id);const keys=[...new Set(rows.map(r=>r.page+(r.tab?' › '+r.tab:'')))];
const cell=(k,v)=>{const rs=rows.filter(r=>r.vp===v&&(r.page+(r.tab?' › '+r.tab:''))===k);if(!rs.length)return '–';const bad=rs.filter(r=>r.result==='FAIL');
  const scr=Math.max(...rs.map(r=>r.screens||0));return bad.length?`FAIL ${scr||''}`.trim():'PASS';};
let md=`# FIT check ${stamp}\n\nBase ${BASE} · text scale ${TEXT_SCALE} · roles ${ROLES.join(', ')} · ${rows.length} checks · **${fails} failing** · ${fails?'FAIL':'PASS'}\n\nCell = worst over roles; the number is the document height in screens (1.0 = fits).\n\n| Page | ${vps.join(' | ')} |\n|---|${vps.map(()=>'---').join('|')}|\n`;
for(const k of keys)md+=`| ${k} | ${vps.map(v=>cell(k,v)).join(' | ')} |\n`;
md+=`\n## Failures\n\n| Viewport | Role | Page | Why |\n|---|---|---|---|\n`;
for(const r of rows.filter(r=>r.result==='FAIL'))md+=`| ${r.vp} | ${r.role} | ${r.page}${r.tab?' › '+r.tab:''} | ${r.fails.join('; ').replace(/\|/g,'/')} |\n`;
if(SHOTS)md+=`\nScreenshots (Owner + Housekeeper at tab-land and lap-1366): \`${shotDir}\`\n`;
writeFileSync(join(OUT,name+'.md'),md);
console.log(`${fails?'FAIL':'PASS'} — ${fails} failing of ${rows.length}. Report: ${join(OUT,name+'.md')}`);
process.exit(fails?1:0);
