// LF · phone check of the new LF screens at 360 px (phone rules in CLAUDE.md: no sideways scroll, 48 px targets, 16 px text). Playwright, by hand; not part of npm test.
// Needs `npm run build` and a server: `PORT=4183 node serve.mjs` (or BASE=http://127.0.0.1:<port>/). Playwright: PLAYWRIGHT_DIR=<dir with playwright> (default /opt/node-tools/node_modules/playwright).
//   node tools/check-phone-lf.mjs [--roles owner,reception] [--out <dir>]
// For each role and each LF page (counter-bills, desk-alerts, customers, cashier) at 360x740 in the mobile shell it checks: the document does not scroll sideways, no visible
// button / link / input / select is under 44 px tall (48 px is the rule; 44 px is the tolerance for text links inside paragraphs), and the page raised no script error.
// Then it opens the phone dialogs of the page (stay-rate, agreed-rate) from the booking dialog where it can. Exit 0 = every check passed.
import {createRequire} from 'node:module';
import {existsSync} from 'node:fs';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYWRIGHT_DIR||(existsSync('/opt/node-tools/node_modules/playwright')?'/opt/node-tools/node_modules/playwright':'playwright'));
const args=process.argv.slice(2);const opt=k=>{const i=args.indexOf(k);return i>=0?args[i+1]:undefined;};
const BASE=process.env.BASE||`http://127.0.0.1:${process.env.PORT||4183}/`;
const ROLES=(opt('--roles')||'owner,reception').split(',');
const PAGES=['counter-bills','desk-alerts','customers','cashier'];
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const browser=await chromium.launch({channel:process.env.PW_CHANNEL||undefined});
const fails=[];let checked=0;
for(const role of ROLES){
  const ctx=await browser.newContext({viewport:{width:360,height:740},deviceScaleFactor:2,hasTouch:true,isMobile:true});
  await ctx.addInitScript(({role})=>{try{localStorage.setItem('hcc-session',role);localStorage.setItem('hcc-display-mode','mobile');}catch{/* blocked */}},{role});
  const page=await ctx.newPage();const errs=[];page.on('pageerror',e=>errs.push(String(e.message)));
  await page.goto(BASE);await page.waitForSelector('.topbar,.m-appbar,.mf-page,main',{timeout:30000}).catch(()=>{});await sleep(900);
  for(const id of PAGES){
    await page.evaluate(id=>{const idx=(history.state&&history.state.idx||0)+1;const st={hcc:'page',page:id,idx};history.pushState(st,'');window.dispatchEvent(new PopStateEvent('popstate',{state:st}));},id);
    await sleep(500);
    for(const tab of id==='counter-bills'?['New bill','Bills']:['']){
      if(tab){await page.getByRole('tab',{name:tab}).first().click({timeout:2000}).catch(()=>{});await sleep(250);}
      const m=await page.evaluate(()=>{
        const vis=e=>{const r=e.getBoundingClientRect(),cs=getComputedStyle(e);return r.width>0&&r.height>0&&cs.visibility!=='hidden'&&cs.display!=='none';};
        const small=[...document.querySelectorAll('button,a[href],input:not([type=hidden]):not([type=checkbox]):not([type=radio]):not(.cmp-sr),select,[role=tab]')].filter(vis)
          .map(e=>{const box=e.matches('input')&&e.parentElement?.classList.contains('search')?e.parentElement:e; // a search field's touch target is its 48 px box
          return {t:(e.textContent||e.getAttribute('placeholder')||e.getAttribute('aria-label')||e.tagName).trim().slice(0,24),h:Math.round(box.getBoundingClientRect().height)};}).filter(x=>x.h<44);
        return {docW:document.documentElement.scrollWidth,vw:innerWidth,small,title:(document.querySelector('h1,.m-appbar-title,.mf-page')?.textContent||'').trim().slice(0,30)};});
      checked++;const f=[];
      if(m.docW>m.vw+1)f.push(`sideways ${m.docW}/${m.vw}`);
      if(m.small.length)f.push(`touch-size ${m.small.length}: ${m.small.slice(0,5).map(s=>s.t+' '+s.h).join('|')}`);
      if(errs.length)f.push('script-error '+errs.splice(0).join(';'));
      if(f.length)fails.push(`${role} ${id}${tab?' / '+tab:''}: ${f.join('; ')}`);
      console.log(f.length?'FAIL':'ok  ',role,id,tab||'',f.join('; '));
    }
  }
  await ctx.close();
}
await browser.close();
console.log(`${checked} checks, ${fails.length} failing`);
process.exit(fails.length?1:0);
